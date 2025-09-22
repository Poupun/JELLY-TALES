using System.Collections;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Optimizes ONLY the chunk generation process to prevent FPS drops
    /// Does not modify rendering, meshing, or any visual aspects
    /// </summary>
    public class ChunkGenerationOptimizer : MonoBehaviour
    {
        [Header("Generation Optimization")]
        [Tooltip("Maximum time per frame for chunk generation (milliseconds)")]
        [Range(0.5f, 15f)] public float maxGenerationTimePerFrame = 8f; // Increased for faster generation
        
        [Tooltip("Blocks to process before yielding to main thread")]
        [Range(10, 2000)] public int blocksPerYield = 1000; // Significantly increased for faster generation
        
        [Tooltip("Enable optimized noise calculation")]
        public bool optimizeNoise = true;
        
        [Tooltip("Cache noise values to avoid recalculation")]
        public bool cacheNoiseValues = true;
        
        private WorldGenerator worldGenerator;
        private System.Collections.Generic.Dictionary<Vector2Int, float> noiseCache = 
            new System.Collections.Generic.Dictionary<Vector2Int, float>();
        
        private void Awake()
        {
            worldGenerator = GetComponent<WorldGenerator>();
        }
        
        /// <summary>
        /// Optimized chunk generation that yields frequently to prevent FPS drops
        /// </summary>
        public IEnumerator GenerateChunkOptimized(WorldGeneration.Chunks.Chunk chunk, Vector2Int coord)
        {
            float startTime = Time.realtimeSinceStartup;
            int processedBlocks = 0;
            
            // Pre-calculate surface heights for the entire chunk to avoid redundant noise calls
            float[,] surfaceHeights = null;
            if (optimizeNoise)
            {
                surfaceHeights = PreCalculateSurfaceHeights(coord);
                
                // Yield if pre-calculation took too long
                if ((Time.realtimeSinceStartup - startTime) * 1000f > maxGenerationTimePerFrame)
                {
                    yield return null;
                    startTime = Time.realtimeSinceStartup;
                }
            }
            
            // Generate blocks with frequent yielding
            for (int lx = 0; lx < worldGenerator.chunkSizeX; lx++)
            {
                for (int lz = 0; lz < worldGenerator.chunkSizeZ; lz++)
                {
                    int worldX = coord.x * worldGenerator.chunkSizeX + lx;
                    int worldZ = coord.y * worldGenerator.chunkSizeZ + lz;
                    
                    // Get surface height (cached or calculated)
                    int surfaceY;
                    if (surfaceHeights != null)
                    {
                        surfaceY = Mathf.FloorToInt(surfaceHeights[lx, lz]);
                    }
                    else
                    {
                        surfaceY = worldGenerator.GetColumnTopY(worldX, worldZ);
                    }
                    
                    // Generate column of blocks
                    int columnMaxY = Mathf.Min(worldGenerator.worldHeight - 1, surfaceY);
                    for (int ly = 0; ly <= columnMaxY; ly++)
                    {
                        var wp = new Vector3Int(worldX, ly, worldZ);
                        BlockType blockType = worldGenerator.GenerateBlockTypeAt(wp);
                        chunk.SetLocal(lx, ly, lz, blockType);
                        
                        processedBlocks++;
                        
                        // Yield more aggressively to prevent any micro-freezes
                        if (processedBlocks >= blocksPerYield || 
                            (Time.realtimeSinceStartup - startTime) * 1000f > maxGenerationTimePerFrame)
                        {
                            yield return null;
                            startTime = Time.realtimeSinceStartup;
                            processedBlocks = 0;
                        }
                        
                        // Extra yield every 250 blocks for faster generation
                        if (processedBlocks % 250 == 0)
                        {
                            yield return null;
                            startTime = Time.realtimeSinceStartup;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Pre-calculate all surface heights for a chunk to reduce noise calculation overhead
        /// </summary>
        private float[,] PreCalculateSurfaceHeights(Vector2Int coord)
        {
            float[,] heights = new float[worldGenerator.chunkSizeX, worldGenerator.chunkSizeZ];
            
            for (int lx = 0; lx < worldGenerator.chunkSizeX; lx++)
            {
                for (int lz = 0; lz < worldGenerator.chunkSizeZ; lz++)
                {
                    int worldX = coord.x * worldGenerator.chunkSizeX + lx;
                    int worldZ = coord.y * worldGenerator.chunkSizeZ + lz;
                    
                    heights[lx, lz] = GetOptimizedHeight(worldX, worldZ);
                }
            }
            
            return heights;
        }
        
        /// <summary>
        /// Get height with caching to avoid redundant noise calculations
        /// </summary>
        private float GetOptimizedHeight(int worldX, int worldZ)
        {
            if (cacheNoiseValues)
            {
                var key = new Vector2Int(worldX, worldZ);
                if (noiseCache.TryGetValue(key, out float cachedHeight))
                {
                    return cachedHeight;
                }
                
                float height = CalculateHeightAt(worldX, worldZ);
                
                // Limit cache size to prevent memory issues
                if (noiseCache.Count < 10000)
                {
                    noiseCache[key] = height;
                }
                
                return height;
            }
            else
            {
                return CalculateHeightAt(worldX, worldZ);
            }
        }
        
        /// <summary>
        /// Calculate height using the same logic as WorldGenerator but optimized
        /// </summary>
        private float CalculateHeightAt(int worldX, int worldZ)
        {
            // Use the same noise parameters as the original WorldGenerator
            float scale = 0.01f;
            float baseHeight = 8f;
            float amplitude = 4f;
            
            float noise = Mathf.PerlinNoise(
                worldX * scale + worldGenerator.worldSeed * 0.1f, 
                worldZ * scale + worldGenerator.worldSeed * 0.1f
            );
            
            return baseHeight + noise * amplitude;
        }
        
        /// <summary>
        /// Clear the noise cache to free memory
        /// </summary>
        public void ClearCache()
        {
            noiseCache.Clear();
        }
        
        /// <summary>
        /// Get cache statistics
        /// </summary>
        public int GetCacheSize() => noiseCache.Count;
        
        private void OnDestroy()
        {
            ClearCache();
        }
    }
}