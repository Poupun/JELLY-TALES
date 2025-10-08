using System.Collections.Generic;
using UnityEngine;
using WorldGeneration.Chunks;

/// <summary>
/// Dynamically animates water vertices in real-time using world coordinates
/// to create seamless wave animation across all chunks without boundaries.
/// This replaces shader-based animation with direct mesh manipulation.
/// </summary>
public class DynamicWaterAnimator : MonoBehaviour
{
    [Header("Animation Settings")]
    [Tooltip("Enable dynamic water vertex animation")]
    public bool enableDynamicAnimation = true;

    [Header("Performance")]
    [Tooltip("Maximum chunks to update per frame")]
    [Range(1, 10)] public int maxChunksPerFrame = 3;
    [Tooltip("Animation update frequency (lower = better performance)")]
    [Range(30f, 120f)] public float targetFPS = 60f;

    private WorldGenerator worldGenerator;
    private Dictionary<Vector2Int, ChunkWaterData> chunkWaterCache = new Dictionary<Vector2Int, ChunkWaterData>();
    private Queue<Vector2Int> chunksToUpdate = new Queue<Vector2Int>();
    private float lastUpdateTime = 0f;
    private float updateInterval;

    private struct ChunkWaterData
    {
        public Chunk chunk;
        public MeshFilter meshFilter;
        public List<WaterVertex> waterVertices;
        public Vector3[] originalVertices;
        public bool needsUpdate;
    }

    private struct WaterVertex
    {
        public int vertexIndex;
        public Vector3 worldPos;
        public Vector3 originalLocalPos;
        public bool isTopFace;
    }

    void Start()
    {
        worldGenerator = FindFirstObjectByType<WorldGenerator>();
        if (worldGenerator == null)
        {
            Debug.LogError("DynamicWaterAnimator: WorldGenerator not found!");
            enabled = false;
            return;
        }

        updateInterval = 1f / targetFPS;
        Debug.Log("DynamicWaterAnimator: Initialized for seamless water animation");
    }

    void Update()
    {
        if (!enableDynamicAnimation) return;
        if (worldGenerator == null || !worldGenerator.enableWaterWaves) return;

        // Throttle updates for performance
        if (Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;

        // Scan for new water chunks
        ScanForWaterChunks();

        // Update water vertices
        UpdateWaterVertices();
    }

    private void ScanForWaterChunks()
    {
        // Get all loaded chunks from WorldGenerator
        var loadedChunks = GetLoadedWaterChunks();

        foreach (var chunkCoord in loadedChunks)
        {
            if (!chunkWaterCache.ContainsKey(chunkCoord))
            {
                // Register new water chunk
                RegisterWaterChunk(chunkCoord);
            }
        }

        // Remove unloaded chunks
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var kvp in chunkWaterCache)
        {
            if (!loadedChunks.Contains(kvp.Key) || kvp.Value.chunk?.parent == null)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var coord in toRemove)
        {
            chunkWaterCache.Remove(coord);
        }
    }

    private HashSet<Vector2Int> GetLoadedWaterChunks()
    {
        HashSet<Vector2Int> waterChunks = new HashSet<Vector2Int>();

        // Use reflection to access private _chunks field (hacky but necessary)
        var chunksField = typeof(WorldGenerator).GetField("_chunks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (chunksField != null)
        {
            var chunks = (Dictionary<Vector2Int, Chunk>)chunksField.GetValue(worldGenerator);
            foreach (var kvp in chunks)
            {
                if (ContainsWater(kvp.Value))
                {
                    waterChunks.Add(kvp.Key);
                }
            }
        }

        return waterChunks;
    }

    private bool ContainsWater(Chunk chunk)
    {
        if (chunk == null || chunk.parent == null) return false;

        var renderer = chunk.parent.GetComponent<MeshRenderer>();
        if (renderer?.sharedMaterials == null) return false;

        foreach (var material in renderer.sharedMaterials)
        {
            if (material != null && material.name.Contains("Water"))
            {
                return true;
            }
        }
        return false;
    }

    private void RegisterWaterChunk(Vector2Int chunkCoord)
    {
        // Get chunk from WorldGenerator
        var chunksField = typeof(WorldGenerator).GetField("_chunks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (chunksField == null) return;

        var chunks = (Dictionary<Vector2Int, Chunk>)chunksField.GetValue(worldGenerator);
        if (!chunks.TryGetValue(chunkCoord, out Chunk chunk) || chunk?.parent == null) return;

        var meshFilter = chunk.parent.GetComponent<MeshFilter>();
        if (meshFilter?.sharedMesh == null) return;

        // Analyze mesh to find water vertices
        var waterData = AnalyzeWaterVertices(chunk, meshFilter);

        if (waterData.waterVertices.Count > 0)
        {
            chunkWaterCache[chunkCoord] = waterData;
            chunksToUpdate.Enqueue(chunkCoord);
            Debug.Log($"DynamicWaterAnimator: Registered chunk {chunkCoord} with {waterData.waterVertices.Count} water vertices");
        }
    }

    private ChunkWaterData AnalyzeWaterVertices(Chunk chunk, MeshFilter meshFilter)
    {
        var mesh = meshFilter.sharedMesh;
        var vertices = mesh.vertices;
        var waterVertices = new List<WaterVertex>();

        // Create a working copy of vertices
        var originalVertices = new Vector3[vertices.Length];
        System.Array.Copy(vertices, originalVertices, vertices.Length);

        // Find vertices that belong to water faces
        // This is a simplified approach - in a real implementation you'd need to
        // correlate with the material assignments and triangle indices
        for (int i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];

            // Check if this vertex could be part of a water block
            int localX = Mathf.FloorToInt(vertex.x);
            int localY = Mathf.FloorToInt(vertex.y);
            int localZ = Mathf.FloorToInt(vertex.z);

            if (localX >= 0 && localX < chunk.sizeX &&
                localY >= 0 && localY < chunk.sizeY &&
                localZ >= 0 && localZ < chunk.sizeZ)
            {
                var blockType = chunk.GetLocal(localX, localY, localZ);
                if (blockType == BlockType.Water)
                {
                    // Calculate world position
                    int worldX = chunk.coord.x * chunk.sizeX + localX;
                    int worldZ = chunk.coord.y * chunk.sizeZ + localZ;

                    // Check if this is a top face vertex (Y > block center)
                    bool isTopFace = vertex.y > localY + 0.5f;

                    waterVertices.Add(new WaterVertex
                    {
                        vertexIndex = i,
                        worldPos = new Vector3(worldX + (vertex.x - localX), localY + (vertex.y - localY), worldZ + (vertex.z - localZ)),
                        originalLocalPos = vertex,
                        isTopFace = isTopFace
                    });
                }
            }
        }

        return new ChunkWaterData
        {
            chunk = chunk,
            meshFilter = meshFilter,
            waterVertices = waterVertices,
            originalVertices = originalVertices,
            needsUpdate = true
        };
    }

    private void UpdateWaterVertices()
    {
        int chunksUpdated = 0;

        while (chunksToUpdate.Count > 0 && chunksUpdated < maxChunksPerFrame)
        {
            var chunkCoord = chunksToUpdate.Dequeue();

            if (chunkWaterCache.TryGetValue(chunkCoord, out ChunkWaterData waterData))
            {
                if (waterData.meshFilter?.sharedMesh != null)
                {
                    UpdateChunkWaterVertices(waterData);
                    chunksUpdated++;
                }
            }

            // Re-queue for continuous updates
            if (chunkWaterCache.ContainsKey(chunkCoord))
            {
                chunksToUpdate.Enqueue(chunkCoord);
            }
        }
    }

    private void UpdateChunkWaterVertices(ChunkWaterData waterData)
    {
        var mesh = waterData.meshFilter.sharedMesh;
        var vertices = new Vector3[waterData.originalVertices.Length];
        System.Array.Copy(waterData.originalVertices, vertices, vertices.Length);

        float time = Time.time * worldGenerator.waterWaveSpeed;

        // Update each water vertex
        foreach (var waterVertex in waterData.waterVertices)
        {
            var worldPos = waterVertex.worldPos;
            var originalPos = waterVertex.originalLocalPos;

            // Calculate wave offset using world coordinates
            Vector3 offset = CalculateWaveOffset(worldPos, time, waterVertex.isTopFace);

            // Apply offset to vertex
            vertices[waterVertex.vertexIndex] = originalPos + offset;
        }

        // Update mesh
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private Vector3 CalculateWaveOffset(Vector3 worldPos, float time, bool isTopFace)
    {
        // Multi-layered wave calculation using world coordinates
        float wave1 = Mathf.Sin(worldPos.x * worldGenerator.waterWaveScale + time) *
                     Mathf.Sin(worldPos.z * worldGenerator.waterWaveScale + time);

        float wave2 = Mathf.Sin((worldPos.x + worldPos.z) * worldGenerator.waterWaveScale * 0.7f + time * 1.3f);

        float wave3 = Mathf.Sin((worldPos.x - worldPos.z) * worldGenerator.waterWaveScale * 1.2f + time * 0.8f);

        // Wave direction
        Vector2 waveDir = worldGenerator.waterWaveDirection.normalized;
        float directionalWave = Mathf.Sin((worldPos.x * waveDir.x + worldPos.z * waveDir.y) * worldGenerator.waterWaveScale + time);

        // Combine waves
        float totalWave = (wave1 + wave2 * 0.6f + wave3 * 0.4f + directionalWave * 0.8f) * worldGenerator.waterWaveAmplitude;

        Vector3 offset = Vector3.zero;

        if (isTopFace)
        {
            // Top faces get full animation
            offset.y = totalWave;
            offset.x = Mathf.Sin(worldPos.z * worldGenerator.waterWaveScale * 0.5f + time) * worldGenerator.waterWaveAmplitude * 0.2f;
            offset.z = Mathf.Sin(worldPos.x * worldGenerator.waterWaveScale * 0.5f + time * 1.1f) * worldGenerator.waterWaveAmplitude * 0.2f;
        }
        else
        {
            // Side faces get reduced animation
            offset.y = totalWave * 0.3f;
        }

        return offset;
    }

    // Public control methods
    public void SetAnimationEnabled(bool enabled)
    {
        enableDynamicAnimation = enabled;
    }

    public void ForceUpdateAllChunks()
    {
        foreach (var kvp in chunkWaterCache)
        {
            if (!chunksToUpdate.Contains(kvp.Key))
            {
                chunksToUpdate.Enqueue(kvp.Key);
            }
        }
    }

    // Debug info
    public int GetRegisteredChunkCount() => chunkWaterCache.Count;
    public int GetPendingUpdates() => chunksToUpdate.Count;
}