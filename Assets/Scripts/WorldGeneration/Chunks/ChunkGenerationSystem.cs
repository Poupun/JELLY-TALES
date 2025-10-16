using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Professional-grade chunk generation system with async/await and smart optimization.
    /// Handles all chunk data generation off the main thread with proper cancellation support.
    ///
    /// Features:
    /// - Fully asynchronous generation (no frame drops)
    /// - Height caching to avoid redundant noise calculations
    /// - Thread-safe operations
    /// - Proper memory management with size limits
    /// - Cancellation token support for cleanup
    /// </summary>
    public class ChunkGenerationSystem : IDisposable
    {
        private readonly WorldGenerator world;
        private readonly CancellationTokenSource cancellationSource;

        // Thread-safe height cache with automatic size management
        private readonly Dictionary<Vector2Int, float> heightCache;
        private readonly object heightCacheLock = new object();
        private const int MAX_CACHE_SIZE = 100000; // Limit memory usage

        // Performance tracking
        private int chunksGenerated = 0;
        private float totalGenerationTime = 0f;

        public ChunkGenerationSystem(WorldGenerator worldGenerator)
        {
            world = worldGenerator ?? throw new ArgumentNullException(nameof(worldGenerator));
            cancellationSource = new CancellationTokenSource();
            heightCache = new Dictionary<Vector2Int, float>(10000);
        }

        /// <summary>
        /// Generate chunk data asynchronously on a background thread.
        /// This is the main entry point for chunk generation.
        /// </summary>
        public async Task<ChunkData> GenerateChunkAsync(Vector2Int coord, CancellationToken cancellationToken = default)
        {
            // Combine cancellation tokens
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellationSource.Token);

            float startTime = Time.realtimeSinceStartup;

            // Run generation on background thread
            ChunkData data = await Task.Run(() => GenerateChunkData(coord, linkedCts.Token), linkedCts.Token);

            // Track performance
            float elapsed = Time.realtimeSinceStartup - startTime;
            totalGenerationTime += elapsed;
            chunksGenerated++;

            return data;
        }

        /// <summary>
        /// Generate chunk data on background thread (thread-safe).
        /// </summary>
        private ChunkData GenerateChunkData(Vector2Int coord, CancellationToken cancellationToken)
        {
            int sizeX = world.chunkSizeX;
            int sizeY = world.worldHeight;
            int sizeZ = world.chunkSizeZ;

            ChunkData data = new ChunkData
            {
                coord = coord,
                sizeX = sizeX,
                sizeY = sizeY,
                sizeZ = sizeZ,
                blocks = new BlockType[sizeX, sizeY, sizeZ]
            };

            // Pre-calculate all heights for this chunk (more efficient than per-block)
            float[,] surfaceHeights = new float[sizeX, sizeZ];
            for (int lx = 0; lx < sizeX; lx++)
            {
                for (int lz = 0; lz < sizeZ; lz++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int worldX = coord.x * sizeX + lx;
                    int worldZ = coord.y * sizeZ + lz;
                    surfaceHeights[lx, lz] = GetHeightCached(worldX, worldZ);
                }
            }

            // Generate all blocks using cached heights
            for (int lx = 0; lx < sizeX; lx++)
            {
                for (int lz = 0; lz < sizeZ; lz++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int worldX = coord.x * sizeX + lx;
                    int worldZ = coord.y * sizeZ + lz;
                    int surfaceY = Mathf.FloorToInt(surfaceHeights[lx, lz]);

                    // Determine biome once per column
                    BiomeData biome = GetBiomeData(worldX, worldZ);

                    // Determine generation height (higher for ocean to include water)
                    int maxY = biome.type == BiomeType.Ocean
                        ? Mathf.Max(surfaceY, biome.waterLevel + 5)
                        : surfaceY;
                    int columnMaxY = Mathf.Min(sizeY - 1, maxY);

                    // Generate column
                    for (int ly = 0; ly <= columnMaxY; ly++)
                    {
                        Vector3Int worldPos = new Vector3Int(worldX, ly, worldZ);
                        data.blocks[lx, ly, lz] = GenerateBlockType(worldPos, surfaceY, biome);
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Get terrain height with thread-safe caching.
        /// </summary>
        private float GetHeightCached(int worldX, int worldZ)
        {
            Vector2Int key = new Vector2Int(worldX, worldZ);

            lock (heightCacheLock)
            {
                if (heightCache.TryGetValue(key, out float cachedHeight))
                    return cachedHeight;

                float height = CalculateTerrainHeight(worldX, worldZ);

                // Add to cache with size limit
                if (heightCache.Count < MAX_CACHE_SIZE)
                {
                    heightCache[key] = height;
                }
                else if (heightCache.Count % 10000 == 0)
                {
                    // Periodic cleanup when cache is full
                    Debug.LogWarning($"ChunkGenerationSystem: Height cache full ({MAX_CACHE_SIZE}), clearing old entries.");
                    heightCache.Clear();
                    heightCache[key] = height;
                }

                return height;
            }
        }

        /// <summary>
        /// Calculate terrain height using world generator's noise parameters.
        /// Thread-safe (only uses Mathf.PerlinNoise).
        /// </summary>
        private float CalculateTerrainHeight(int worldX, int worldZ)
        {
            float seedOffset = (world.worldSeed % 10000) * 0.01f;

            // Base terrain noise
            float baseX = worldX * 0.008f + seedOffset;
            float baseZ = worldZ * 0.008f + seedOffset;
            float baseNoise = Mathf.PerlinNoise(baseX, baseZ) * 2f - 1f;

            // Hill noise for variation
            float hillX = worldX * 0.003f + seedOffset;
            float hillZ = worldZ * 0.003f + seedOffset;
            float hillNoise = Mathf.PerlinNoise(hillX, hillZ);
            float hillMultiplier = Mathf.Max(0f, hillNoise - 0.6f) * 10f;

            // Detail noise for fine variation
            float detailX = worldX * 0.05f + seedOffset;
            float detailZ = worldZ * 0.05f + seedOffset;
            float detailNoise = Mathf.PerlinNoise(detailX, detailZ) * 0.3f;

            // Surface at Y=100-120
            return 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise;
        }

        /// <summary>
        /// Get biome data for a position (thread-safe, optimized).
        /// </summary>
        private BiomeData GetBiomeData(int worldX, int worldZ)
        {
            float seedOffset = (world.worldSeed % 10000) * 0.01f;

            // Ocean/Plains noise - single calculation
            float biomeX = worldX * world.oceanPlainsNoiseScale + seedOffset;
            float biomeZ = worldZ * world.oceanPlainsNoiseScale + seedOffset;
            float biomeNoise = Mathf.PerlinNoise(biomeX, biomeZ);

            // Determine ocean vs land (FAST PATH: skip forest noise if ocean)
            if (biomeNoise < world.oceanCoverage)
            {
                BiomeData oceanBiome = BiomeRegistry.GetBiome(BiomeType.Ocean);
                oceanBiome.waterLevel = world.seaLevel;
                oceanBiome.maxDepth = world.maxOceanDepth;
                return oceanBiome;
            }

            // Land biome - only calculate forest noise if NOT ocean
            float forestX = worldX * world.forestNoiseScale + seedOffset * 2.5f;
            float forestZ = worldZ * world.forestNoiseScale + seedOffset * 3.7f;
            float forestNoise = Mathf.PerlinNoise(forestX, forestZ);

            if (forestNoise < world.forestCoverage)
            {
                BiomeData forestBiome = BiomeRegistry.GetBiome(BiomeType.Forest);
                forestBiome.treeDensity = world.forestTreeDensity;
                forestBiome.plantDensity = world.forestPlantDensity;
                return forestBiome;
            }
            else
            {
                BiomeData plainsBiome = BiomeRegistry.GetBiome(BiomeType.Plains);
                plainsBiome.waterLevel = world.seaLevel;
                plainsBiome.treeDensity = world.plainsTreeDensity;
                plainsBiome.plantDensity = world.plainsPlantDensity;
                return plainsBiome;
            }
        }

        /// <summary>
        /// Generate block type at position (thread-safe).
        /// </summary>
        private BlockType GenerateBlockType(Vector3Int worldPos, int surfaceY, BiomeData biome)
        {
            int y = worldPos.y;

            // Bedrock layer
            if (y <= 2) return BlockType.Bedrock;

            // Check for tunnels
            if (world.enableTunnels &&
                WorldGeneration.HorizontalTunnelGenerator.IsTunnelBlock(worldPos, world.worldSeed, world.tunnelSettings))
            {
                return BlockType.Air;
            }

            // Ocean biome
            if (biome.type == BiomeType.Ocean)
            {
                int oceanFloor = biome.waterLevel - biome.maxDepth;

                if (y > biome.waterLevel)
                    return BlockType.Air;
                else if (y > surfaceY && y <= biome.waterLevel)
                    return BlockType.Water;
                else if (y == surfaceY || y == oceanFloor)
                    return BlockType.Sand;
                else if (y > oceanFloor)
                    return BlockType.Sand;
            }

            // Standard terrain
            if (y > surfaceY)
                return BlockType.Air;
            else if (y == surfaceY)
                return BlockType.Grass;
            else if (y >= surfaceY - 4)
                return BlockType.Dirt;
            else if (y <= 99)
                return GenerateUndergroundBlock(worldPos.x, y, worldPos.z);

            return BlockType.Air;
        }

        /// <summary>
        /// Generate underground blocks with ore distribution (thread-safe, optimized).
        /// </summary>
        private BlockType GenerateUndergroundBlock(int x, int y, int z)
        {
            // FAST PATH: Most blocks are stone, check this first
            float chance = GetDeterministicRandom(x, y, z);

            // Skip expensive ore calculations for most blocks
            // Only 10-25% of underground blocks are ores
            if (chance > 0.30f)
                return BlockType.Stone;

            // Use chunk-based ore system if enabled
            if (world.oreSettings.enableChunkOreGeneration)
            {
                Vector2Int chunkCoord = new Vector2Int(
                    Mathf.FloorToInt(x / (float)world.chunkSizeX),
                    Mathf.FloorToInt(z / (float)world.chunkSizeZ)
                );

                ChunkOreGenerator.GenerateChunkOreBlobs(chunkCoord, world.chunkSizeX, world.chunkSizeZ,
                    world.worldSeed, world.oreSettings);

                BlockType oreType = ChunkOreGenerator.GetOreAtPosition(
                    new Vector3Int(x, y, z), chunkCoord, world.worldSeed, world.oreSettings);

                if (oreType != BlockType.Stone)
                    return oreType;

                return BlockType.Stone;
            }

            // Fallback: simplified hash-based ore generation
            // Deep underground (Y 3-30) - most valuable ores
            if (y <= 30)
            {
                float depthFactor = (100f - y) / 97f;
                if (y <= 20 && chance < 0.004f * depthFactor) return BlockType.Diamond;
                if (y <= 25 && chance < 0.010f * depthFactor) return BlockType.Gold;
                if (chance < 0.08f) return BlockType.Iron;
                if (chance < 0.18f) return BlockType.Coal;
                if (chance < 0.25f) return BlockType.Gravel;
                return BlockType.Stone;
            }

            // Mid underground (Y 31-60)
            if (y <= 60)
            {
                if (chance < 0.008f) return BlockType.Gold;
                if (chance < 0.10f) return BlockType.Iron;
                if (chance < 0.20f) return BlockType.Coal;
                if (chance < 0.15f) return BlockType.Gravel;
                return BlockType.Stone;
            }

            // Shallow underground (Y 61-99)
            if (chance < 0.06f) return BlockType.Iron;
            if (chance < 0.12f) return BlockType.Coal;
            if (chance < 0.10f) return BlockType.Gravel;
            return BlockType.Stone;
        }

        /// <summary>
        /// Deterministic random number generator using hash function.
        /// </summary>
        private float GetDeterministicRandom(int x, int y, int z)
        {
            int hash = x;
            hash = hash * 31 + y;
            hash = hash * 31 + z;
            hash = hash * 31 + world.worldSeed;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = (hash >> 16) ^ hash;
            return (hash & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        /// <summary>
        /// Get performance statistics.
        /// </summary>
        public ChunkGenerationStats GetStats()
        {
            return new ChunkGenerationStats
            {
                ChunksGenerated = chunksGenerated,
                AverageGenerationTime = chunksGenerated > 0 ? totalGenerationTime / chunksGenerated : 0f,
                CacheSize = heightCache.Count
            };
        }

        /// <summary>
        /// Clear all caches to free memory.
        /// </summary>
        public void ClearCaches()
        {
            lock (heightCacheLock)
            {
                heightCache.Clear();
            }
        }

        /// <summary>
        /// Dispose and cleanup resources.
        /// </summary>
        public void Dispose()
        {
            cancellationSource?.Cancel();
            cancellationSource?.Dispose();
            ClearCaches();
        }
    }

    /// <summary>
    /// Statistics for chunk generation performance monitoring.
    /// </summary>
    public struct ChunkGenerationStats
    {
        public int ChunksGenerated;
        public float AverageGenerationTime;
        public int CacheSize;

        public override string ToString()
        {
            return $"Chunks: {ChunksGenerated} | Avg: {AverageGenerationTime * 1000f:F1}ms | Cache: {CacheSize}";
        }
    }
}
