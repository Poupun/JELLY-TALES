using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// High-performance block generation that eliminates expensive operations
    /// </summary>
    public class OptimizedBlockGenerator : MonoBehaviour
    {
        [Header("Performance Settings")]
        [Tooltip("Cache noise values to avoid recalculation")]
        public bool enableNoiseCache = true;
        
        [Tooltip("Use fast hash-based random instead of System.Random")]
        public bool useFastRandom = true;
        
        [Tooltip("Pre-calculate surface heights for entire columns")]
        public bool preCalculateHeights = true;
        
        
        private WorldGenerator worldGenerator;
        
        // Cached noise values to avoid expensive Perlin noise calls
        private System.Collections.Generic.Dictionary<Vector2Int, float> baseNoiseCache = 
            new System.Collections.Generic.Dictionary<Vector2Int, float>();
        private System.Collections.Generic.Dictionary<Vector2Int, float> hillNoiseCache = 
            new System.Collections.Generic.Dictionary<Vector2Int, float>();
        private System.Collections.Generic.Dictionary<Vector2Int, float> detailNoiseCache = 
            new System.Collections.Generic.Dictionary<Vector2Int, float>();
        
        // Pre-calculated surface heights for entire chunk
        private System.Collections.Generic.Dictionary<Vector2Int, int> surfaceHeightCache = 
            new System.Collections.Generic.Dictionary<Vector2Int, int>();
        
        private void Awake()
        {
            worldGenerator = GetComponent<WorldGenerator>();
        }
        
        /// <summary>
        /// Pre-calculate all noise values for a chunk to avoid repeated expensive calls
        /// </summary>
        public void PreCalculateChunkNoise(Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ)
        {
            if (!enableNoiseCache) return;
            
            float seedOffset = (worldGenerator.worldSeed % 10000) * 0.01f;
            
            for (int lx = 0; lx < chunkSizeX; lx++)
            {
                for (int lz = 0; lz < chunkSizeZ; lz++)
                {
                    int worldX = chunkCoord.x * chunkSizeX + lx;
                    int worldZ = chunkCoord.y * chunkSizeZ + lz;
                    Vector2Int worldPos2D = new Vector2Int(worldX, worldZ);
                    
                    if (!baseNoiseCache.ContainsKey(worldPos2D))
                    {
                        // Base terrain noise
                        float baseX = worldX * 0.008f + seedOffset;
                        float baseZ = worldZ * 0.008f + seedOffset;
                        baseNoiseCache[worldPos2D] = Mathf.PerlinNoise(baseX, baseZ);
                        
                        // Hill noise
                        float hillX = worldX * 0.015f + seedOffset * 1.7f;
                        float hillZ = worldZ * 0.015f + seedOffset * 1.7f;
                        hillNoiseCache[worldPos2D] = Mathf.PerlinNoise(hillX, hillZ);
                        
                        // Detail noise
                        float detailX = worldX * 0.05f + seedOffset;
                        float detailZ = worldZ * 0.05f + seedOffset;
                        detailNoiseCache[worldPos2D] = Mathf.PerlinNoise(detailX, detailZ);
                    }
                }
            }
        }
        
        
        /// <summary>
        /// Pre-calculate surface heights for entire chunk
        /// </summary>
        public void PreCalculateSurfaceHeights(Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ)
        {
            if (!preCalculateHeights) return;
            
            for (int lx = 0; lx < chunkSizeX; lx++)
            {
                for (int lz = 0; lz < chunkSizeZ; lz++)
                {
                    int worldX = chunkCoord.x * chunkSizeX + lx;
                    int worldZ = chunkCoord.y * chunkSizeZ + lz;
                    Vector2Int worldPos2D = new Vector2Int(worldX, worldZ);
                    
                    if (!surfaceHeightCache.ContainsKey(worldPos2D))
                    {
                        surfaceHeightCache[worldPos2D] = CalculateSurfaceHeightOptimized(worldX, worldZ);
                    }
                }
            }
        }
        
        /// <summary>
        /// Fast block type generation using cached values
        /// </summary>
        public BlockType GenerateBlockTypeOptimized(Vector3Int worldPos)
        {
            if (worldPos.y < 0 || worldPos.y >= worldGenerator.worldHeight) return BlockType.Air;
            
            // Bedrock layer (expanded to Y=0-2)
            if (worldPos.y <= 2) return BlockType.Bedrock;
            
            // Check for NEW tunnel system FIRST at all Y levels (including surface!)
            if (worldGenerator.enableTunnels && WorldGeneration.HorizontalTunnelGenerator.IsTunnelBlock(worldPos, worldGenerator.worldSeed, worldGenerator.tunnelSettings))
            {
                return BlockType.Air; // Tunnel air space
            }
            
            // Deep underground layers (Y 3-99) - Only generate solid blocks if no tunnel
            if (worldPos.y <= 99)
            {
                return GenerateUndergroundBlockOptimized(worldPos);
            }
            
            // Surface terrain (Y 100+) - Only generate if no tunnel
            return GenerateSurfaceBlockOptimized(worldPos);
        }
        
        private BlockType GenerateUndergroundBlockOptimized(Vector3Int worldPos)
        {
            // Use WorldGenerator's ore settings instead of local settings
            var worldGen = worldGenerator;
            if (worldGen != null && worldGen.oreSettings.enableChunkOreGeneration)
            {
                // Calculate which chunk this position belongs to using WorldGenerator's chunk size
                Vector2Int chunkCoord = new Vector2Int(
                    Mathf.FloorToInt(worldPos.x / (float)worldGen.chunkSizeX),
                    Mathf.FloorToInt(worldPos.z / (float)worldGen.chunkSizeZ)
                );
                
                // Ensure chunk ore generation is initialized
                ChunkOreGenerator.GenerateChunkOreBlobs(chunkCoord, worldGen.chunkSizeX, worldGen.chunkSizeZ, worldGen.worldSeed, worldGen.oreSettings);
                
                // Get ore type from chunk-based system
                BlockType oreType = ChunkOreGenerator.GetOreAtPosition(worldPos, chunkCoord, worldGen.worldSeed, worldGen.oreSettings);
                
                if (oreType != BlockType.Stone)
                {
                    return oreType; // Return the ore from the chunk system
                }
                
                return BlockType.Stone; // Default to stone
            }
            
            // Fallback to old system if chunk ore generation is disabled or no WorldGenerator
            return GenerateUndergroundBlockLegacy(worldPos);
        }
        
        /// <summary>
        /// Legacy ore generation system (kept as fallback)
        /// </summary>
        private BlockType GenerateUndergroundBlockLegacy(Vector3Int worldPos)
        {
            // Original random-based ore generation
            float chance = GetFastRandom(worldPos);
            
            // Calculate depth factor: deeper = rarer ores (Y=3 is deepest, Y=99 is shallowest)
            float depthFactor = (100f - worldPos.y) / 97f; // 0.0 at surface, 1.0 at deepest
            
            // Deep Underground (Y 3-30): Deepest ores
            if (worldPos.y <= 30)
            {
                // Diamond (very rare, only in deepest layers)
                if (worldPos.y <= 20 && chance < 0.004f * depthFactor)
                    return BlockType.Diamond;
                
                // Gold (rare, deep preferred)
                if (worldPos.y <= 25 && chance < 0.010f * depthFactor)
                    return BlockType.Gold;
                
                // Iron (common at all depths)
                if (chance < 0.08f)
                    return BlockType.Iron;
                
                // Coal (most common)
                if (chance < 0.18f)
                    return BlockType.Coal;
                    
                // Gravel pockets
                if (chance < 0.25f)
                    return BlockType.Gravel;
                    
                return BlockType.Stone;
            }
            
            // Mid Underground (Y 31-60): Mixed ore distribution
            if (worldPos.y <= 60)
            {
                // Gold (less common than deep, but still present)
                if (chance < 0.008f * depthFactor)
                    return BlockType.Gold;
                
                // Iron (very common in mid layers)
                if (chance < 0.10f)
                    return BlockType.Iron;
                
                // Coal (abundant)
                if (chance < 0.20f)
                    return BlockType.Coal;
                    
                // Gravel
                if (chance < 0.15f)
                    return BlockType.Gravel;
                    
                return BlockType.Stone;
            }
            
            // Shallow Underground (Y 61-99): Mostly stone with some coal/iron
            if (worldPos.y <= 99)
            {
                // Iron (less common near surface)
                if (chance < 0.06f)
                    return BlockType.Iron;
                
                // Coal (still present but less dense)
                if (chance < 0.12f)
                    return BlockType.Coal;
                    
                // Gravel
                if (chance < 0.10f)
                    return BlockType.Gravel;
                    
                return BlockType.Stone;
            }
            
            return BlockType.Stone;
        }
        
        private BlockType GenerateSurfaceBlockOptimized(Vector3Int worldPos)
        {
            Vector2Int worldPos2D = new Vector2Int(worldPos.x, worldPos.z);
            
            // Get surface height from cache or calculate it
            int surfaceHeight;
            if (surfaceHeightCache.TryGetValue(worldPos2D, out surfaceHeight))
            {
                // Use cached value
            }
            else if (enableNoiseCache && baseNoiseCache.ContainsKey(worldPos2D))
            {
                // Calculate from cached noise values
                float baseNoise = baseNoiseCache[worldPos2D];
                float hillNoise = hillNoiseCache[worldPos2D];
                float detailNoise = detailNoiseCache[worldPos2D];
                
                float hillMultiplier = hillNoise > 0.3f ? Mathf.Pow((hillNoise - 0.3f) / 0.7f, 1.2f) : 0f;
                // Moved surface to Y=100-120 to allow for 100 underground levels (Y=0-99)
                float combinedHeight = 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise * 0.3f;
                surfaceHeight = Mathf.RoundToInt(combinedHeight);
                
                // Cache the result
                surfaceHeightCache[worldPos2D] = surfaceHeight;
            }
            else
            {
                // Fallback to expensive calculation (shouldn't happen if pre-calculation is done)
                surfaceHeight = CalculateSurfaceHeightOptimized(worldPos.x, worldPos.z);
            }
            
            // Generate block based on height
            if (worldPos.y < surfaceHeight - 4) return BlockType.Stone;
            if (worldPos.y < surfaceHeight) return BlockType.Dirt;
            if (worldPos.y == surfaceHeight) return BlockType.Grass;
            
            return BlockType.Air;
        }
        
        private int CalculateSurfaceHeightOptimized(int worldX, int worldZ)
        {
            float seedOffset = (worldGenerator.worldSeed % 10000) * 0.01f;
            
            // Base terrain
            float baseNoise = Mathf.PerlinNoise(worldX * 0.008f + seedOffset, worldZ * 0.008f + seedOffset);
            
            // Hills
            float hillNoise = Mathf.PerlinNoise(worldX * 0.015f + seedOffset * 1.7f, worldZ * 0.015f + seedOffset * 1.7f);
            float hillMultiplier = hillNoise > 0.3f ? Mathf.Pow((hillNoise - 0.3f) / 0.7f, 1.2f) : 0f;
            
            // Detail
            float detailNoise = Mathf.PerlinNoise(worldX * 0.05f + seedOffset, worldZ * 0.05f + seedOffset);
            
            // Moved surface to Y=100-120 to allow for 100 underground levels (Y=0-99)
            float combinedHeight = 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise * 0.3f;
            return Mathf.RoundToInt(combinedHeight);
        }
        
        /// <summary>
        /// Fast random number generation using hash function instead of System.Random
        /// </summary>
        private float GetFastRandom(Vector3Int worldPos)
        {
            if (!useFastRandom)
            {
                // Fallback to original (expensive) method
                System.Random rng = new System.Random(worldPos.x * 73856093 ^ worldPos.y * 19349663 ^ worldPos.z * 83492791 ^ worldGenerator.worldSeed);
                return (float)rng.NextDouble();
            }
            
            // Fast hash-based random (much faster than System.Random)
            int hash = worldPos.x;
            hash = hash * 31 + worldPos.y;
            hash = hash * 31 + worldPos.z;
            hash = hash * 31 + worldGenerator.worldSeed;
            
            // Thomas Wang hash
            hash = (hash ^ 61) ^ (hash >> 16);
            hash = hash + (hash << 3);
            hash = hash ^ (hash >> 4);
            hash = hash * 0x27d4eb2d;
            hash = hash ^ (hash >> 15);
            
            // Convert to 0-1 range
            return (hash & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }
        
        /// <summary>
        /// Clear all caches to free memory
        /// </summary>
        public void ClearCaches()
        {
            baseNoiseCache.Clear();
            hillNoiseCache.Clear();
            detailNoiseCache.Clear();
            surfaceHeightCache.Clear();
        }
        
        /// <summary>
        /// Get cache statistics for debugging
        /// </summary>
        public void LogCacheStats()
        {
            Debug.Log($"OptimizedBlockGenerator Cache Stats:");
            Debug.Log($"- Base Noise: {baseNoiseCache.Count} entries");
            Debug.Log($"- Hill Noise: {hillNoiseCache.Count} entries");
            Debug.Log($"- Detail Noise: {detailNoiseCache.Count} entries");
            Debug.Log($"- Surface Heights: {surfaceHeightCache.Count} entries");
        }
        
        private void OnDestroy()
        {
            ClearCaches();
        }
    }
}