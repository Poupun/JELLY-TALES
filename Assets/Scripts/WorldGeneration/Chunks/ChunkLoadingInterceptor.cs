using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// AGGRESSIVE FIX: Intercepts WorldGenerator's chunk loading and forces priority order
    ///
    /// HOW IT WORKS:
    /// 1. Monitors WorldGenerator's _pendingLoads queue via reflection
    /// 2. Extracts chunks from the queue
    /// 3. Re-orders them by distance to player
    /// 4. Injects them back in priority order
    ///
    /// This ensures CLOSEST chunks ALWAYS load first, regardless of WorldGenerator's queue logic.
    ///
    /// SETUP:
    /// 1. Add this component to WorldGenerator GameObject
    /// 2. It automatically activates
    /// 3. Chunks now load in perfect priority order!
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    public class ChunkLoadingInterceptor : MonoBehaviour
    {
        [Header("🔧 Aggressive Priority Fix")]
        [Tooltip("Force chunk loading by distance (overrides WorldGenerator queue)")]
        public bool enableInterception = true;

        [Tooltip("Chunks within this distance load IMMEDIATELY")]
        [Range(1, 4)]
        public int criticalRadius = 2;

        [Tooltip("How often to re-sort the queue (seconds)")]
        [Range(0.05f, 0.5f)]
        public float resortInterval = 0.1f;

        [Header("📊 Status")]
        [SerializeField] private int chunksReordered = 0;
        [SerializeField] private int criticalChunksRemaining = 0;
        [SerializeField] private bool isIntercepting = false;

        private WorldGenerator world;
        private FieldInfo pendingLoadsField;
        private FieldInfo queuedField;
        private Queue<Vector2Int> pendingLoadsQueue;
        private HashSet<Vector2Int> queuedSet;
        private float lastResortTime = 0f;

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();

            // Use reflection to access private fields
            var worldType = typeof(WorldGenerator);

            pendingLoadsField = worldType.GetField("_pendingLoads", BindingFlags.NonPublic | BindingFlags.Instance);
            queuedField = worldType.GetField("_queued", BindingFlags.NonPublic | BindingFlags.Instance);

            if (pendingLoadsField == null || queuedField == null)
            {
                Debug.LogError("ChunkLoadingInterceptor: Failed to access WorldGenerator fields! Disabling.");
                enabled = false;
                return;
            }

            pendingLoadsQueue = (Queue<Vector2Int>)pendingLoadsField.GetValue(world);
            queuedSet = (HashSet<Vector2Int>)queuedField.GetValue(world);

            isIntercepting = true;
            Debug.Log("🔧 ChunkLoadingInterceptor: Active! Forcing priority chunk loading.");
        }

        private void Update()
        {
            if (!enableInterception || !isIntercepting || world.player == null)
                return;

            // Re-sort queue periodically
            if (Time.time - lastResortTime >= resortInterval)
            {
                ReorderChunkQueue();
                lastResortTime = Time.time;
            }
        }

        /// <summary>
        /// Extract chunks from queue, sort by distance, re-inject
        /// </summary>
        private void ReorderChunkQueue()
        {
            if (pendingLoadsQueue == null || pendingLoadsQueue.Count == 0)
                return;

            // Extract all queued chunks
            var allChunks = new List<Vector2Int>(pendingLoadsQueue);
            pendingLoadsQueue.Clear();

            Vector3 playerPos = world.player.position;
            Vector2Int playerChunk = WorldToChunkCoord(playerPos);

            // Sort by distance (closest first)
            allChunks.Sort((a, b) =>
            {
                Vector3 centerA = new Vector3(
                    a.x * world.chunkSizeX + world.chunkSizeX * 0.5f,
                    playerPos.y,
                    a.y * world.chunkSizeZ + world.chunkSizeZ * 0.5f
                );

                Vector3 centerB = new Vector3(
                    b.x * world.chunkSizeX + world.chunkSizeX * 0.5f,
                    playerPos.y,
                    b.y * world.chunkSizeZ + world.chunkSizeZ * 0.5f
                );

                float distA = Vector3.Distance(playerPos, centerA);
                float distB = Vector3.Distance(playerPos, centerB);

                // Critical chunks (immediate neighbors) ALWAYS first
                bool criticalA = Mathf.Abs(a.x - playerChunk.x) <= criticalRadius &&
                                Mathf.Abs(a.y - playerChunk.y) <= criticalRadius;
                bool criticalB = Mathf.Abs(b.x - playerChunk.x) <= criticalRadius &&
                                Mathf.Abs(b.y - playerChunk.y) <= criticalRadius;

                if (criticalA && !criticalB) return -1;
                if (!criticalA && criticalB) return 1;

                return distA.CompareTo(distB);
            });

            // Re-inject in priority order
            foreach (var chunk in allChunks)
            {
                pendingLoadsQueue.Enqueue(chunk);
            }

            // Count critical chunks
            criticalChunksRemaining = 0;
            foreach (var chunk in allChunks)
            {
                if (Mathf.Abs(chunk.x - playerChunk.x) <= criticalRadius &&
                    Mathf.Abs(chunk.y - playerChunk.y) <= criticalRadius)
                {
                    criticalChunksRemaining++;
                }
            }

            chunksReordered = allChunks.Count;
        }

        private Vector2Int WorldToChunkCoord(Vector3 wpos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(wpos.x / world.chunkSizeX),
                Mathf.FloorToInt(wpos.z / world.chunkSizeZ)
            );
        }

        /// <summary>
        /// Force immediate re-sort (call when player teleports)
        /// </summary>
        public void ForceResort()
        {
            ReorderChunkQueue();
        }

        private void OnGUI()
        {
            if (!enableInterception || !isIntercepting) return;

            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.fontSize = 11;
            style.normal.textColor = criticalChunksRemaining > 0 ? Color.red : Color.green;
            style.fontStyle = FontStyle.Bold;

            GUI.Box(new Rect(10, 200, 450, 50), "🔧 CHUNK PRIORITY INTERCEPTOR - ACTIVE", style);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 10;
            labelStyle.normal.textColor = criticalChunksRemaining > 0 ? Color.yellow : Color.white;

            string status = $"Queued: {chunksReordered} | Critical (within {criticalRadius} chunks): {criticalChunksRemaining}";
            GUI.Label(new Rect(20, 225, 420, 20), status, labelStyle);
        }
    }
}
