using System.Collections;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Ultra-smooth chunk generation that virtually eliminates all micro-freezes
    /// Spreads chunk generation across many frames with minimal work per frame
    /// </summary>
    public class UltraSmoothChunkGenerator : MonoBehaviour
    {
        [Header("Ultra-Smooth Settings")]
        [Tooltip("Maximum blocks to process per frame")]
        [Range(5, 2000)] public int maxBlocksPerFrame = 1000; // Significantly increased for faster generation
        
        [Tooltip("Maximum time per frame in milliseconds")]
        [Range(0.1f, 10f)] public float maxTimePerFrame = 5f; // Increased time budget for faster generation
        
        [Tooltip("Yield after every N blocks regardless of time")]
        [Range(5, 1000)] public int forceYieldEvery = 500; // Much less frequent yielding for faster generation
        
        private WorldGenerator worldGenerator;
        
        private void Awake()
        {
            worldGenerator = GetComponent<WorldGenerator>();
        }
        
        /// <summary>
        /// Generate chunk with ultra-smooth frame distribution and optimized block generation
        /// </summary>
        public IEnumerator GenerateChunkUltraSmooth(WorldGeneration.Chunks.Chunk chunk, Vector2Int coord)
        {
            // Use optimized block generator if available
            var blockOptimizer = GetComponent<OptimizedBlockGenerator>();
            if (blockOptimizer != null)
            {
                // Pre-calculate all expensive operations
                blockOptimizer.PreCalculateChunkNoise(coord, worldGenerator.chunkSizeX, worldGenerator.chunkSizeZ);
                yield return null; // Yield after noise pre-calculation
                
                blockOptimizer.PreCalculateSurfaceHeights(coord, worldGenerator.chunkSizeX, worldGenerator.chunkSizeZ);
                yield return null; // Yield after height pre-calculation
            }
            
            // Pre-calculate surface heights in small batches (fallback)
            float[,] surfaceHeights = new float[worldGenerator.chunkSizeX, worldGenerator.chunkSizeZ];
            if (blockOptimizer == null)
            {
                yield return StartCoroutine(PreCalculateHeightsSmooth(coord, surfaceHeights));
            }
            
            // Generate blocks with ultra-frequent yielding
            int totalProcessed = 0;
            float frameStartTime = Time.realtimeSinceStartup;
            
            for (int lx = 0; lx < worldGenerator.chunkSizeX; lx++)
            {
                for (int lz = 0; lz < worldGenerator.chunkSizeZ; lz++)
                {
                    int worldX = coord.x * worldGenerator.chunkSizeX + lx;
                    int worldZ = coord.y * worldGenerator.chunkSizeZ + lz;
                    int surfaceY = Mathf.FloorToInt(surfaceHeights[lx, lz]);
                    
                    // Generate column of blocks using optimized generation
                    int columnMaxY = Mathf.Min(worldGenerator.worldHeight - 1, surfaceY);
                    for (int ly = 0; ly <= columnMaxY; ly++)
                    {
                        var wp = new Vector3Int(worldX, ly, worldZ);
                        
                        // Use optimized block generation if available
                        BlockType blockType;
                        if (blockOptimizer != null)
                        {
                            blockType = blockOptimizer.GenerateBlockTypeOptimized(wp);
                        }
                        else
                        {
                            blockType = worldGenerator.GenerateBlockTypeAt(wp);
                        }
                        
                        chunk.SetLocal(lx, ly, lz, blockType);
                        
                        totalProcessed++;
                        
                        // Ultra-aggressive yielding - multiple conditions
                        bool shouldYield = false;
                        
                        // Time-based yield
                        if ((Time.realtimeSinceStartup - frameStartTime) * 1000f > maxTimePerFrame)
                        {
                            shouldYield = true;
                        }
                        
                        // Block count yield
                        if (totalProcessed >= maxBlocksPerFrame)
                        {
                            shouldYield = true;
                        }
                        
                        // Forced periodic yield
                        if (totalProcessed % forceYieldEvery == 0)
                        {
                            shouldYield = true;
                        }
                        
                        if (shouldYield)
                        {
                            yield return null;
                            frameStartTime = Time.realtimeSinceStartup;
                            totalProcessed = 0;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Pre-calculate surface heights with frequent yields
        /// </summary>
        private IEnumerator PreCalculateHeightsSmooth(Vector2Int coord, float[,] surfaceHeights)
        {
            int processed = 0;
            float startTime = Time.realtimeSinceStartup;
            
            for (int lx = 0; lx < worldGenerator.chunkSizeX; lx++)
            {
                for (int lz = 0; lz < worldGenerator.chunkSizeZ; lz++)
                {
                    int worldX = coord.x * worldGenerator.chunkSizeX + lx;
                    int worldZ = coord.y * worldGenerator.chunkSizeZ + lz;
                    
                    // Calculate height using optimized noise
                    surfaceHeights[lx, lz] = CalculateOptimizedHeight(worldX, worldZ);
                    processed++;
                    
                    // Yield less frequently during height calculation
                    if (processed % 100 == 0 || (Time.realtimeSinceStartup - startTime) * 1000f > 5f)
                    {
                        yield return null;
                        startTime = Time.realtimeSinceStartup;
                    }
                }
            }
        }
        
        private float CalculateOptimizedHeight(int worldX, int worldZ)
        {
            // Optimized height calculation matching WorldGenerator
            float scale = 0.01f;
            float baseHeight = 8f;
            float amplitude = 4f;
            
            float noise = Mathf.PerlinNoise(
                worldX * scale + worldGenerator.worldSeed * 0.1f,
                worldZ * scale + worldGenerator.worldSeed * 0.1f
            );
            
            return baseHeight + noise * amplitude;
        }
    }
}