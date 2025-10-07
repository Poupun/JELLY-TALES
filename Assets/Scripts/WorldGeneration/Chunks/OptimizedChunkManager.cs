using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// High-performance chunk management system that integrates:
    /// - Async/threaded chunk data generation
    /// - Priority-based chunk loading
    /// - Optimized mesh building with caching
    ///
    /// Usage: Add this component alongside WorldGenerator and enable "useOptimizedChunkSystem"
    /// </summary>
    public class OptimizedChunkManager : MonoBehaviour
    {
        [Header("Performance Settings")]
        [Tooltip("Maximum chunks to generate per frame (data + mesh)")]
        [Range(1, 20)]
        public int maxChunksPerFrame = 5;

        [Tooltip("Use threaded async chunk generation (MUCH faster)")]
        public bool useAsyncGeneration = true;

        [Tooltip("Use optimized mesh builder (faster water rendering)")]
        public bool useOptimizedMeshBuilder = true;

        [Tooltip("Update chunk priorities when player moves (slight CPU cost)")]
        public bool dynamicPriorityUpdate = true;

        [Header("Debug Info")]
        [SerializeField] private int chunksInQueue = 0;
        [SerializeField] private int activeAsyncTasks = 0;
        [SerializeField] private float avgGenerationTime = 0f;

        private WorldGenerator world;
        private AsyncChunkDataGenerator asyncGenerator;
        private ChunkLoadPriority priorityQueue;

        private Dictionary<Vector2Int, Task<ChunkData>> activeGenerationTasks = new Dictionary<Vector2Int, Task<ChunkData>>();
        private List<Vector2Int> completedChunks = new List<Vector2Int>();

        private float lastPriorityUpdateTime = 0f;
        private List<float> generationTimes = new List<float>();

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();
            if (world == null)
            {
                Debug.LogError("OptimizedChunkManager requires WorldGenerator component!");
                enabled = false;
                return;
            }

            asyncGenerator = new AsyncChunkDataGenerator(world);
            priorityQueue = new ChunkLoadPriority();
        }

        /// <summary>
        /// Queue a chunk for loading with priority based on distance to player
        /// </summary>
        public void QueueChunk(Vector2Int coord, Vector3 playerPosition)
        {
            if (activeGenerationTasks.ContainsKey(coord))
                return;

            priorityQueue.Enqueue(coord, playerPosition, world.chunkSizeX, world.chunkSizeZ);
            chunksInQueue = priorityQueue.Count;
        }

        /// <summary>
        /// Check if chunk is queued or actively generating
        /// </summary>
        public bool IsChunkQueued(Vector2Int coord)
        {
            return priorityQueue.Contains(coord) || activeGenerationTasks.ContainsKey(coord);
        }

        /// <summary>
        /// Remove chunk from queue
        /// </summary>
        public void DequeueChunk(Vector2Int coord)
        {
            priorityQueue.Remove(coord);
            chunksInQueue = priorityQueue.Count;
        }

        private void Update()
        {
            // Update priorities periodically
            if (dynamicPriorityUpdate && world.player != null && Time.time - lastPriorityUpdateTime > 0.5f)
            {
                priorityQueue.UpdatePriorities(world.player.position, world.chunkSizeX, world.chunkSizeZ);
                lastPriorityUpdateTime = Time.time;
            }

            // Check completed async tasks
            completedChunks.Clear();
            foreach (var kvp in activeGenerationTasks)
            {
                if (kvp.Value.IsCompleted)
                {
                    completedChunks.Add(kvp.Key);
                }
            }

            // Process completed chunks
            foreach (var coord in completedChunks)
            {
                var task = activeGenerationTasks[coord];
                activeGenerationTasks.Remove(coord);

                if (task.IsFaulted)
                {
                    Debug.LogError($"Chunk generation failed at {coord}: {task.Exception?.GetBaseException().Message}");
                    continue;
                }

                // Start mesh building coroutine
                StartCoroutine(BuildChunkMesh(task.Result));
            }

            // Start new generation tasks
            int budget = maxChunksPerFrame - activeGenerationTasks.Count;
            while (budget > 0 && priorityQueue.TryDequeue(out Vector2Int coord))
            {
                StartChunkGeneration(coord);
                budget--;
            }

            // Update debug info
            chunksInQueue = priorityQueue.Count;
            activeAsyncTasks = activeGenerationTasks.Count;
            if (generationTimes.Count > 0)
            {
                float sum = 0f;
                foreach (var t in generationTimes)
                    sum += t;
                avgGenerationTime = sum / generationTimes.Count;
            }
        }

        private void StartChunkGeneration(Vector2Int coord)
        {
            if (useAsyncGeneration)
            {
                float startTime = Time.realtimeSinceStartup;
                var task = asyncGenerator.GenerateChunkDataAsync(coord, world.chunkSizeX, world.worldHeight, world.chunkSizeZ);

                activeGenerationTasks[coord] = task;

                // Track generation time when complete
                task.ContinueWith(_ =>
                {
                    float elapsed = Time.realtimeSinceStartup - startTime;
                    generationTimes.Add(elapsed);
                    if (generationTimes.Count > 20)
                        generationTimes.RemoveAt(0);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                // Fallback to coroutine-based generation
                StartCoroutine(GenerateChunkFallback(coord));
            }
        }

        private IEnumerator GenerateChunkFallback(Vector2Int coord)
        {
            var chunk = new Chunk(coord, world.chunkSizeX, world.worldHeight, world.chunkSizeZ, world.transform);

            // Use fallback generation
            yield return WorldGeneration.Chunks.ChunkGenerationFallback.GenerateChunkAsync(
                chunk, coord, world.chunkSizeX, world.worldHeight, world.chunkSizeZ,
                world.worldSeed, world.enableTunnels, world.tunnelSettings);

            // Build mesh
            if (useOptimizedMeshBuilder)
                OptimizedChunkMeshBuilder.BuildMeshOptimized(world, chunk, world.addChunkCollider);
            else
                ChunkMeshBuilder.BuildMesh(world, chunk, world.addChunkCollider);

            // Register chunk (you'll need to expose this in WorldGenerator)
            // world.RegisterChunk(coord, chunk);
        }

        private IEnumerator BuildChunkMesh(ChunkData chunkData)
        {
            // Create chunk from data
            var chunk = new Chunk(chunkData.coord, chunkData.sizeX, chunkData.sizeY, chunkData.sizeZ, world.transform);

            // Copy block data
            for (int x = 0; x < chunkData.sizeX; x++)
            {
                for (int y = 0; y < chunkData.sizeY; y++)
                {
                    for (int z = 0; z < chunkData.sizeZ; z++)
                    {
                        chunk.SetLocal(x, y, z, chunkData.blocks[x, y, z]);
                    }
                }
            }

            // Apply persistence
            if (world.enableChunkPersistence)
            {
                // Call world's persistence methods (you'll need to expose these)
                // world.LoadChunkFromDiskInto(chunkData.coord, chunk);
            }

            // Generate trees
            if (world.enableTrees)
            {
                // This needs to be called via world
                // yield return world.GenerateTreesInChunkRoutine(chunk);
            }

            // Build mesh
            if (useOptimizedMeshBuilder)
            {
                OptimizedChunkMeshBuilder.BuildMeshOptimized(world, chunk, world.addChunkCollider);
            }
            else
            {
                ChunkMeshBuilder.BuildMesh(world, chunk, world.addChunkCollider);
            }

            // Register chunk (you'll need to expose this in WorldGenerator)
            // world.RegisterChunk(chunkData.coord, chunk);

            yield return null;
        }

        /// <summary>
        /// Clear all queues and cancel pending operations
        /// </summary>
        public void ClearAll()
        {
            priorityQueue.Clear();

            foreach (var task in activeGenerationTasks.Values)
            {
                // Tasks will complete on their own, we just won't process them
            }
            activeGenerationTasks.Clear();

            chunksInQueue = 0;
            activeAsyncTasks = 0;
        }

        /// <summary>
        /// Get statistics for debugging
        /// </summary>
        public string GetStats()
        {
            return $"Queue: {chunksInQueue} | Active: {activeAsyncTasks} | Avg Gen Time: {avgGenerationTime:F3}s | Max Queue Distance: {priorityQueue.GetMaxDistance():F1}";
        }

        private void OnDestroy()
        {
            asyncGenerator?.Dispose();
            ClearAll();
        }

        private void OnDisable()
        {
            ClearAll();
        }
    }
}
