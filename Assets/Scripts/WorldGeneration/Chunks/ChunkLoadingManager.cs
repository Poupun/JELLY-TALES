using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Professional chunk loading manager with intelligent prioritization and resource management.
    ///
    /// Features:
    /// - Distance-based priority queue (load closest chunks first)
    /// - Async chunk generation with controlled concurrency
    /// - Automatic chunk unloading for distant chunks
    /// - Frame budget system to prevent stuttering
    /// - Memory-efficient chunk pooling
    /// </summary>
    public class ChunkLoadingManager : MonoBehaviour
    {
        [Header("Loading Settings")]
        [Tooltip("Maximum chunks to process per frame")]
        [Range(1, 10)]
        public int maxChunksPerFrame = 1;

        [Tooltip("Maximum concurrent async chunk generations")]
        [Range(1, 8)]
        public int maxConcurrentGenerations = 2;

        [Tooltip("Frame time budget in milliseconds (prevents stuttering)")]
        [Range(2f, 16f)]
        public float frameBudgetMs = 4f;

        [Header("Unloading Settings")]
        [Tooltip("Unload chunks beyond this distance (in chunks)")]
        [Range(8, 32)]
        public int unloadDistance = 16;

        [Tooltip("Check for chunks to unload every N seconds")]
        [Range(1f, 10f)]
        public float unloadCheckInterval = 2f;

        [Header("Debug Info")]
        [SerializeField] private int chunksLoaded = 0;
        [SerializeField] private int chunksQueued = 0;
        [SerializeField] private int activeGenerations = 0;
        [SerializeField] private float avgFrameTime = 0f;

        private WorldGenerator world;
        private UltraFastChunkGenerator generationSystem;
        private Transform player;

        // Priority queue for chunk loading (sorted by distance)
        private readonly SortedDictionary<float, Queue<Vector2Int>> priorityQueue = new SortedDictionary<float, Queue<Vector2Int>>();
        private readonly HashSet<Vector2Int> queuedChunks = new HashSet<Vector2Int>();

        // Active chunk tracking
        private readonly Dictionary<Vector2Int, Chunk> loadedChunks = new Dictionary<Vector2Int, Chunk>();
        private readonly Dictionary<Vector2Int, Task<ChunkData>> activeGenerationTasks = new Dictionary<Vector2Int, Task<ChunkData>>();
        private readonly List<Vector2Int> completedChunks = new List<Vector2Int>();

        // Mesh building throttling (limit concurrent mesh builds)
        private int activeMeshBuilds = 0;
        private const int MAX_CONCURRENT_MESH_BUILDS = 1; // Only 1 mesh building at a time!

        // Frame timing
        private readonly Queue<float> recentFrameTimes = new Queue<float>(60);
        private float lastUnloadCheck = 0f;

        // Cancellation
        private CancellationTokenSource cancellationSource;

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();
            if (world == null)
            {
                Debug.LogError("ChunkLoadingManager requires WorldGenerator component!");
                enabled = false;
                return;
            }

            generationSystem = new UltraFastChunkGenerator(world);
            cancellationSource = new CancellationTokenSource();
        }

        private void Start()
        {
            player = world.player;
            if (player == null)
            {
                Debug.LogWarning("ChunkLoadingManager: Player not assigned, will try to find automatically.");
            }
        }

        private void Update()
        {
            if (player == null)
            {
                player = world.player;
                if (player == null) return;
            }

            float frameStartTime = Time.realtimeSinceStartup;

            // Check for completed async generations
            ProcessCompletedGenerations();

            // Start new chunk generations (respecting concurrency limit)
            StartPendingGenerations();

            // Periodic unload check
            if (Time.time - lastUnloadCheck > unloadCheckInterval)
            {
                UnloadDistantChunks();
                lastUnloadCheck = Time.time;
            }

            // Update stats
            float frameTime = (Time.realtimeSinceStartup - frameStartTime) * 1000f;
            recentFrameTimes.Enqueue(frameTime);
            if (recentFrameTimes.Count > 60) recentFrameTimes.Dequeue();
            avgFrameTime = recentFrameTimes.Average();

            chunksLoaded = loadedChunks.Count;
            chunksQueued = queuedChunks.Count;
            activeGenerations = activeGenerationTasks.Count;
        }

        /// <summary>
        /// Queue a chunk for loading with priority based on distance to player.
        /// </summary>
        public void QueueChunk(Vector2Int coord)
        {
            // Skip if already loaded or queued
            if (loadedChunks.ContainsKey(coord) || queuedChunks.Contains(coord))
                return;

            // Calculate priority (distance to player)
            float distance = GetChunkDistanceToPlayer(coord);

            // Add to priority queue
            if (!priorityQueue.TryGetValue(distance, out Queue<Vector2Int> queue))
            {
                queue = new Queue<Vector2Int>();
                priorityQueue[distance] = queue;
            }

            queue.Enqueue(coord);
            queuedChunks.Add(coord);
        }

        /// <summary>
        /// Queue multiple chunks for loading.
        /// </summary>
        public void QueueChunks(IEnumerable<Vector2Int> coords)
        {
            foreach (var coord in coords)
            {
                QueueChunk(coord);
            }
        }

        /// <summary>
        /// Get chunk at coordinate (null if not loaded).
        /// </summary>
        public Chunk GetChunk(Vector2Int coord)
        {
            return loadedChunks.TryGetValue(coord, out Chunk chunk) ? chunk : null;
        }

        /// <summary>
        /// Check if chunk is loaded.
        /// </summary>
        public bool IsChunkLoaded(Vector2Int coord)
        {
            return loadedChunks.ContainsKey(coord);
        }

        /// <summary>
        /// Check if chunk is queued or generating.
        /// </summary>
        public bool IsChunkPending(Vector2Int coord)
        {
            return queuedChunks.Contains(coord) || activeGenerationTasks.ContainsKey(coord);
        }

        /// <summary>
        /// Force unload a specific chunk.
        /// </summary>
        public void UnloadChunk(Vector2Int coord)
        {
            if (loadedChunks.TryGetValue(coord, out Chunk chunk))
            {
                chunk.Unload(world);
                loadedChunks.Remove(coord);
            }
        }

        /// <summary>
        /// Process completed async chunk generations.
        /// </summary>
        private void ProcessCompletedGenerations()
        {
            completedChunks.Clear();

            foreach (var kvp in activeGenerationTasks)
            {
                if (kvp.Value.IsCompleted)
                {
                    completedChunks.Add(kvp.Key);
                }
            }

            // Build meshes for completed chunks (throttled)
            foreach (var coord in completedChunks)
            {
                var task = activeGenerationTasks[coord];
                activeGenerationTasks.Remove(coord);

                if (task.IsFaulted)
                {
                    Debug.LogError($"Chunk generation failed at {coord}: {task.Exception?.GetBaseException().Message}");
                    queuedChunks.Remove(coord);
                    continue;
                }

                // Create chunk from generated data
                ChunkData data = task.Result;
                Chunk chunk = CreateChunkFromData(data);

                // Apply persistence if enabled
                if (world.enableChunkPersistence)
                {
                    world.LoadChunkFromDiskInto(coord, chunk);
                }

                // THROTTLE: Only start mesh build if under limit
                if (activeMeshBuilds < MAX_CONCURRENT_MESH_BUILDS)
                {
                    StartCoroutine(BuildChunkMeshAsyncThrottled(chunk));
                }
                else
                {
                    // Queue for later (re-add to generation queue)
                    QueueChunk(coord);
                    continue;
                }

                // Register chunk
                loadedChunks[coord] = chunk;
                queuedChunks.Remove(coord);
            }
        }

        /// <summary>
        /// Start new chunk generations from queue.
        /// </summary>
        private void StartPendingGenerations()
        {
            // Respect concurrency limit
            int budget = maxConcurrentGenerations - activeGenerationTasks.Count;
            if (budget <= 0) return;

            // Respect frame budget
            float frameStartTime = Time.realtimeSinceStartup;

            int processed = 0;
            while (processed < maxChunksPerFrame && activeGenerationTasks.Count < maxConcurrentGenerations)
            {
                // Get next highest priority chunk
                if (!TryDequeueNextChunk(out Vector2Int coord))
                    break;

                // Start async generation
                var task = generationSystem.GenerateChunkAsync(coord, cancellationSource.Token);
                activeGenerationTasks[coord] = task;

                processed++;

                // Check frame budget
                float elapsed = (Time.realtimeSinceStartup - frameStartTime) * 1000f;
                if (elapsed > frameBudgetMs)
                    break;
            }
        }

        /// <summary>
        /// Dequeue next chunk from priority queue.
        /// </summary>
        private bool TryDequeueNextChunk(out Vector2Int coord)
        {
            coord = default;

            // Get lowest distance queue
            foreach (var kvp in priorityQueue)
            {
                if (kvp.Value.Count > 0)
                {
                    coord = kvp.Value.Dequeue();

                    // Remove empty queue
                    if (kvp.Value.Count == 0)
                    {
                        priorityQueue.Remove(kvp.Key);
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Unload chunks that are too far from player.
        /// </summary>
        private void UnloadDistantChunks()
        {
            if (player == null) return;

            Vector2Int playerChunk = GetPlayerChunkCoord();
            List<Vector2Int> toUnload = new List<Vector2Int>();

            foreach (var coord in loadedChunks.Keys)
            {
                int dx = Mathf.Abs(coord.x - playerChunk.x);
                int dz = Mathf.Abs(coord.y - playerChunk.y);
                int distance = Mathf.Max(dx, dz); // Chebyshev distance

                if (distance > unloadDistance)
                {
                    toUnload.Add(coord);
                }
            }

            // Unload distant chunks
            foreach (var coord in toUnload)
            {
                UnloadChunk(coord);
            }

            if (toUnload.Count > 0)
            {
                Debug.Log($"ChunkLoadingManager: Unloaded {toUnload.Count} distant chunks.");
            }
        }

        /// <summary>
        /// Create Chunk object from generated data.
        /// </summary>
        private Chunk CreateChunkFromData(ChunkData data)
        {
            Chunk chunk = new Chunk(data.coord, data.sizeX, data.sizeY, data.sizeZ, world.transform);

            // Copy block data
            for (int x = 0; x < data.sizeX; x++)
            {
                for (int y = 0; y < data.sizeY; y++)
                {
                    for (int z = 0; z < data.sizeZ; z++)
                    {
                        chunk.SetLocal(x, y, z, data.blocks[x, y, z]);
                    }
                }
            }

            return chunk;
        }

        /// <summary>
        /// Build chunk mesh asynchronously with throttling (spread across frames).
        /// </summary>
        private IEnumerator BuildChunkMeshAsyncThrottled(Chunk chunk)
        {
            activeMeshBuilds++;

            try
            {
                // ALWAYS yield one frame before building to prevent FPS spikes
                yield return null;

                if (world.useChunkMeshing)
                {
                    // Use GREEDY meshing system (Minecraft's algorithm - 80-95% fewer vertices!)
                    // Combines adjacent identical faces into larger quads
                    GreedyMeshBuilder.BuildMesh(world, chunk, world.addChunkCollider);
                    yield return null; // Yield after building
                }
                else
                {
                    // Fallback: instantiate individual blocks (slow)
                    chunk.BuildVisible(world);
                    yield return null;
                }

                // Generate trees if enabled (yield after)
                if (world.enableTrees)
                {
                    // Tree generation is handled by WorldGenerator
                    // This is a placeholder for future integration
                    yield return null;
                }
            }
            finally
            {
                activeMeshBuilds--;
            }
        }

        /// <summary>
        /// Get player's current chunk coordinate.
        /// </summary>
        private Vector2Int GetPlayerChunkCoord()
        {
            if (player == null) return Vector2Int.zero;

            Vector3 pos = player.position;
            return new Vector2Int(
                Mathf.FloorToInt(pos.x / world.chunkSizeX),
                Mathf.FloorToInt(pos.z / world.chunkSizeZ)
            );
        }

        /// <summary>
        /// Calculate distance from chunk to player (in chunks).
        /// </summary>
        private float GetChunkDistanceToPlayer(Vector2Int coord)
        {
            if (player == null) return float.MaxValue;

            Vector2Int playerChunk = GetPlayerChunkCoord();
            int dx = coord.x - playerChunk.x;
            int dz = coord.y - playerChunk.y;

            // Use Chebyshev distance (max of dx, dz)
            return Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
        }

        /// <summary>
        /// Clear all chunks and queues.
        /// </summary>
        public void ClearAll()
        {
            // Cancel active tasks
            cancellationSource?.Cancel();
            cancellationSource?.Dispose();
            cancellationSource = new CancellationTokenSource();

            // Unload all chunks
            foreach (var chunk in loadedChunks.Values)
            {
                chunk.Unload(world);
            }

            loadedChunks.Clear();
            activeGenerationTasks.Clear();
            priorityQueue.Clear();
            queuedChunks.Clear();

            Debug.Log("ChunkLoadingManager: Cleared all chunks and queues.");
        }

        /// <summary>
        /// Get performance statistics.
        /// </summary>
        public string GetStats()
        {
            var genStats = generationSystem.GetStats();
            return $"Loaded: {chunksLoaded} | Queued: {chunksQueued} | Active: {activeGenerations} | " +
                   $"Avg Frame: {avgFrameTime:F1}ms | {genStats}";
        }

        private void OnDestroy()
        {
            cancellationSource?.Cancel();
            cancellationSource?.Dispose();
            generationSystem?.Dispose();
        }

        private void OnDisable()
        {
            ClearAll();
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || player == null) return;

            // Draw loaded chunks
            Gizmos.color = Color.green;
            foreach (var coord in loadedChunks.Keys)
            {
                Vector3 center = new Vector3(
                    coord.x * world.chunkSizeX + world.chunkSizeX * 0.5f,
                    world.worldHeight * 0.5f,
                    coord.y * world.chunkSizeZ + world.chunkSizeZ * 0.5f
                );
                Gizmos.DrawWireCube(center, new Vector3(world.chunkSizeX, world.worldHeight, world.chunkSizeZ));
            }

            // Draw unload distance
            Vector2Int playerChunk = GetPlayerChunkCoord();
            Gizmos.color = Color.red;
            Vector3 playerCenter = new Vector3(
                playerChunk.x * world.chunkSizeX + world.chunkSizeX * 0.5f,
                world.worldHeight * 0.5f,
                playerChunk.y * world.chunkSizeZ + world.chunkSizeZ * 0.5f
            );
            float unloadRadius = unloadDistance * world.chunkSizeX;
            Gizmos.DrawWireCube(playerCenter, new Vector3(unloadRadius * 2, 1, unloadRadius * 2));
        }
    }
}
