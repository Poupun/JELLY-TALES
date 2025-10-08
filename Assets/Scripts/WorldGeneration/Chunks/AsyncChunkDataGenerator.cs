using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// High-performance async chunk data generator using C# Tasks and threading.
    /// Generates chunk block data off the main thread for maximum performance.
    /// </summary>
    public class AsyncChunkDataGenerator
    {
        private readonly WorldGenerator world;
        private readonly CancellationTokenSource cancellationTokenSource;

        // Thread-safe cache for height calculations
        private readonly Dictionary<Vector2Int, float> heightCache = new Dictionary<Vector2Int, float>();
        private readonly object heightCacheLock = new object();

        public AsyncChunkDataGenerator(WorldGenerator worldGenerator)
        {
            world = worldGenerator;
            cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Generate chunk block data asynchronously on a background thread
        /// </summary>
        public async Task<ChunkData> GenerateChunkDataAsync(Vector2Int coord, int sizeX, int sizeY, int sizeZ)
        {
            return await Task.Run(() => GenerateChunkDataThreaded(coord, sizeX, sizeY, sizeZ), cancellationTokenSource.Token);
        }

        /// <summary>
        /// Thread-safe chunk data generation (runs on worker thread)
        /// </summary>
        private ChunkData GenerateChunkDataThreaded(Vector2Int coord, int sizeX, int sizeY, int sizeZ)
        {
            ChunkData data = new ChunkData
            {
                coord = coord,
                sizeX = sizeX,
                sizeY = sizeY,
                sizeZ = sizeZ,
                blocks = new BlockType[sizeX, sizeY, sizeZ]
            };

            // Pre-calculate all surface heights for this chunk (cache per column)
            float[,] surfaceHeights = new float[sizeX, sizeZ];
            for (int lx = 0; lx < sizeX; lx++)
            {
                for (int lz = 0; lz < sizeZ; lz++)
                {
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
                    int worldX = coord.x * sizeX + lx;
                    int worldZ = coord.y * sizeZ + lz;
                    int surfaceY = Mathf.FloorToInt(surfaceHeights[lx, lz]);

                    // Check for ocean biome to extend generation to water level
                    Vector3 checkPos = new Vector3(worldX, 0, worldZ);
                    BiomeData biome = GetBiomeDataThreadSafe(checkPos);
                    int maxGenerationY = biome.type == BiomeType.Ocean ?
                        Mathf.Max(surfaceY, biome.waterLevel + 5) : surfaceY;

                    int columnMaxY = Mathf.Min(sizeY - 1, maxGenerationY);

                    for (int ly = 0; ly <= columnMaxY; ly++)
                    {
                        Vector3Int worldPos = new Vector3Int(worldX, ly, worldZ);
                        data.blocks[lx, ly, lz] = GenerateBlockTypeThreadSafe(worldPos, surfaceY, biome);
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// Thread-safe height calculation with caching
        /// </summary>
        private float GetHeightCached(int worldX, int worldZ)
        {
            Vector2Int key = new Vector2Int(worldX, worldZ);

            lock (heightCacheLock)
            {
                if (heightCache.TryGetValue(key, out float cached))
                    return cached;

                float height = CalculateHeight(worldX, worldZ);

                // Limit cache size to prevent memory bloat (keep last 50k entries)
                if (heightCache.Count < 50000)
                    heightCache[key] = height;
                else if (heightCache.Count % 1000 == 0) // Periodic cleanup
                    heightCache.Clear();

                return height;
            }
        }

        /// <summary>
        /// Calculate terrain height (thread-safe, uses only Mathf.PerlinNoise)
        /// </summary>
        private float CalculateHeight(int worldX, int worldZ)
        {
            float seedOffset = (world.worldSeed % 10000) * 0.01f;

            // Base terrain noise
            float baseX = worldX * 0.008f + seedOffset;
            float baseZ = worldZ * 0.008f + seedOffset;
            float baseNoise = Mathf.PerlinNoise(baseX, baseZ) * 2f - 1f;

            // Hill noise
            float hillX = worldX * 0.003f + seedOffset;
            float hillZ = worldZ * 0.003f + seedOffset;
            float hillNoise = Mathf.PerlinNoise(hillX, hillZ);
            float hillMultiplier = Mathf.Max(0f, hillNoise - 0.6f) * 10f;

            // Detail noise
            float detailX = worldX * 0.05f + seedOffset;
            float detailZ = worldZ * 0.05f + seedOffset;
            float detailNoise = Mathf.PerlinNoise(detailX, detailZ) * 0.3f;

            // Combined height
            return 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise;
        }

        /// <summary>
        /// Thread-safe biome data calculation (doesn't access Unity objects)
        /// </summary>
        private BiomeData GetBiomeDataThreadSafe(Vector3 position)
        {
            float seedOffset = (world.worldSeed % 10000) * 0.01f;

            // Biome noise for ocean/land determination
            float biomeX = position.x * 0.002f + seedOffset;
            float biomeZ = position.z * 0.002f + seedOffset;
            float biomeNoise = Mathf.PerlinNoise(biomeX, biomeZ);

            BiomeData biome;

            if (biomeNoise < world.oceanCoverage)
            {
                biome = BiomeRegistry.GetBiome(BiomeType.Ocean);
            }
            else
            {
                biome = BiomeRegistry.GetBiome(BiomeType.Plains);
            }

            return biome;
        }

        /// <summary>
        /// Thread-safe block type generation (no Unity API calls)
        /// </summary>
        private BlockType GenerateBlockTypeThreadSafe(Vector3Int worldPos, int surfaceY, BiomeData biome)
        {
            int y = worldPos.y;

            // Bedrock layer
            if (y <= 2) return BlockType.Bedrock;

            // Check for tunnels (thread-safe)
            if (world.enableTunnels &&
                WorldGeneration.HorizontalTunnelGenerator.IsTunnelBlock(worldPos, world.worldSeed, world.tunnelSettings))
            {
                return BlockType.Air;
            }

            // Ocean biome handling
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
                return GenerateUndergroundBlockThreadSafe(worldPos.x, y, worldPos.z);

            return BlockType.Air;
        }

        /// <summary>
        /// Thread-safe underground block generation with ores
        /// </summary>
        private BlockType GenerateUndergroundBlockThreadSafe(int x, int y, int z)
        {
            float chance = GetHashedFloat(x, y, z, world.worldSeed);
            float depthFactor = (100f - y) / 97f;

            // Deep underground (Y 3-30)
            if (y <= 30)
            {
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
                if (chance < 0.008f * depthFactor) return BlockType.Gold;
                if (chance < 0.10f) return BlockType.Iron;
                if (chance < 0.20f) return BlockType.Coal;
                if (chance < 0.15f) return BlockType.Gravel;
                return BlockType.Stone;
            }

            // Shallow underground (Y 61-99)
            if (y <= 99)
            {
                if (chance < 0.06f) return BlockType.Iron;
                if (chance < 0.12f) return BlockType.Coal;
                if (chance < 0.10f) return BlockType.Gravel;
                return BlockType.Stone;
            }

            return BlockType.Stone;
        }

        /// <summary>
        /// Thread-safe hash function for deterministic randomness
        /// </summary>
        private float GetHashedFloat(int x, int y, int z, int seed)
        {
            int hash = x;
            hash = hash * 31 + y;
            hash = hash * 31 + z;
            hash = hash * 31 + seed;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = (hash >> 16) ^ hash;
            return (hash & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        /// <summary>
        /// Clear caches to free memory
        /// </summary>
        public void ClearCaches()
        {
            lock (heightCacheLock)
            {
                heightCache.Clear();
            }
        }

        /// <summary>
        /// Cancel all pending async operations
        /// </summary>
        public void Dispose()
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            ClearCaches();
        }
    }

    /// <summary>
    /// Container for chunk block data (thread-safe, no Unity objects)
    /// </summary>
    public class ChunkData
    {
        public Vector2Int coord;
        public int sizeX;
        public int sizeY;
        public int sizeZ;
        public BlockType[,,] blocks;
    }
}
