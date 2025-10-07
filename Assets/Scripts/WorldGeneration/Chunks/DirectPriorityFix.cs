using System.Collections.Generic;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// DIRECT FIX for chunk loading priority
    /// Replaces WorldGenerator's Queue with a distance-based priority system
    ///
    /// PROBLEM: WorldGenerator loads chunks in Queue order (FIFO), not by distance!
    /// SOLUTION: This component intercepts chunk loading and re-orders by distance
    ///
    /// SETUP:
    /// 1. Add this component to WorldGenerator GameObject
    /// 2. Enable "Use Priority Loading"
    /// 3. Chunks now load closest-first!
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    public class DirectPriorityFix : MonoBehaviour
    {
        [Header("🎯 Priority Loading Fix")]
        [Tooltip("Enable distance-based chunk loading (closest chunks first)")]
        public bool usePriorityLoading = true;

        [Tooltip("Aggressively prioritize chunks within this distance")]
        [Range(1, 5)]
        public int immediateRadius = 2; // Load these FIRST

        [Header("📊 Debug Info")]
        [SerializeField] private int chunksInQueue = 0;
        [SerializeField] private float closestChunkDistance = 0f;
        [SerializeField] private int immediateChunksRemaining = 0;

        private WorldGenerator world;
        private SortedDictionary<float, List<Vector2Int>> prioritizedChunks = new SortedDictionary<float, List<Vector2Int>>();
        private Vector3 lastPlayerPos;
        private float lastUpdateTime = 0f;

        // Track what we're intercepting
        private HashSet<Vector2Int> interceptedChunks = new HashSet<Vector2Int>();

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();
        }

        private void LateUpdate()
        {
            if (!usePriorityLoading || world.player == null) return;

            // Re-prioritize periodically or when player moves
            if (Time.time - lastUpdateTime > 0.2f || Vector3.Distance(world.player.position, lastPlayerPos) > 5f)
            {
                ReprioritizeChunks();
                lastUpdateTime = Time.time;
                lastPlayerPos = world.player.position;
            }
        }

        /// <summary>
        /// Call this to add a chunk with priority (intercept WorldGenerator's queue)
        /// </summary>
        public void EnqueueWithPriority(Vector2Int coord)
        {
            if (!usePriorityLoading)
                return;

            if (interceptedChunks.Contains(coord))
                return;

            interceptedChunks.Add(coord);

            // Calculate distance to player
            Vector3 chunkCenter = new Vector3(
                coord.x * world.chunkSizeX + world.chunkSizeX * 0.5f,
                world.player.position.y,
                coord.y * world.chunkSizeZ + world.chunkSizeZ * 0.5f
            );

            float distance = Vector3.Distance(world.player.position, chunkCenter);

            // Add to priority queue
            if (!prioritizedChunks.TryGetValue(distance, out var list))
            {
                list = new List<Vector2Int>();
                prioritizedChunks[distance] = list;
            }
            list.Add(coord);

            chunksInQueue = GetTotalChunksInQueue();
        }

        /// <summary>
        /// Get next chunk to load (by distance priority)
        /// </summary>
        public bool TryDequeueByPriority(out Vector2Int coord)
        {
            coord = default;

            if (!usePriorityLoading || prioritizedChunks.Count == 0)
                return false;

            // Get closest chunks
            using (var enumerator = prioritizedChunks.GetEnumerator())
            {
                if (enumerator.MoveNext())
                {
                    var kvp = enumerator.Current;
                    closestChunkDistance = kvp.Key;

                    var list = kvp.Value;
                    if (list.Count > 0)
                    {
                        coord = list[0];
                        list.RemoveAt(0);

                        if (list.Count == 0)
                        {
                            prioritizedChunks.Remove(kvp.Key);
                        }

                        interceptedChunks.Remove(coord);
                        chunksInQueue = GetTotalChunksInQueue();

                        // Track immediate chunks
                        Vector2Int playerChunk = WorldToChunkCoord(world.player.position);
                        int dx = Mathf.Abs(coord.x - playerChunk.x);
                        int dz = Mathf.Abs(coord.y - playerChunk.y);
                        bool isImmediate = dx <= immediateRadius && dz <= immediateRadius;

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Re-prioritize all queued chunks (when player moves)
        /// </summary>
        private void ReprioritizeChunks()
        {
            if (world.player == null) return;

            // Collect all chunks
            var allChunks = new List<Vector2Int>();
            foreach (var kvp in prioritizedChunks)
            {
                allChunks.AddRange(kvp.Value);
            }

            // Clear and re-add with new distances
            prioritizedChunks.Clear();
            interceptedChunks.Clear();

            foreach (var coord in allChunks)
            {
                EnqueueWithPriority(coord);
            }

            // Count immediate chunks
            Vector2Int playerChunk = WorldToChunkCoord(world.player.position);
            immediateChunksRemaining = 0;

            foreach (var coord in allChunks)
            {
                int dx = Mathf.Abs(coord.x - playerChunk.x);
                int dz = Mathf.Abs(coord.y - playerChunk.y);
                if (dx <= immediateRadius && dz <= immediateRadius)
                {
                    immediateChunksRemaining++;
                }
            }
        }

        private Vector2Int WorldToChunkCoord(Vector3 wpos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(wpos.x / world.chunkSizeX),
                Mathf.FloorToInt(wpos.z / world.chunkSizeZ)
            );
        }

        private int GetTotalChunksInQueue()
        {
            int total = 0;
            foreach (var kvp in prioritizedChunks)
            {
                total += kvp.Value.Count;
            }
            return total;
        }

        /// <summary>
        /// Clear all queued chunks
        /// </summary>
        public void Clear()
        {
            prioritizedChunks.Clear();
            interceptedChunks.Clear();
            chunksInQueue = 0;
        }

        /// <summary>
        /// Get statistics
        /// </summary>
        public string GetStats()
        {
            return $"Queue: {chunksInQueue} | Closest: {closestChunkDistance:F1}m | Immediate: {immediateChunksRemaining}";
        }

        private void OnGUI()
        {
            if (!usePriorityLoading) return;

            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.fontSize = 11;
            style.normal.textColor = immediateChunksRemaining > 0 ? Color.yellow : Color.green;

            GUI.Box(new Rect(10, 140, 400, 50), "🎯 Priority Loading", style);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 10;
            labelStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(20, 165, 380, 20), GetStats(), labelStyle);
        }
    }
}
