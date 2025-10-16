using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Ultra-optimized chunk generation focused on minimal memory and CPU usage.
    ///
    /// Key optimizations:
    /// - Generate only necessary Y range (skip empty space above surface)
    /// - Single-pass biome determination (cached per column)
    /// - Eliminated height cache (direct calculation is faster)
    /// - Skip air block generation entirely
    /// - Simplified block type logic
    ///
    /// Expected performance: 3-5x faster than ChunkGenerationSystem
    /// </summary>
    public class UltraFastChunkGenerator : IDisposable
    {
        private readonly WorldGenerator world;
        private readonly CancellationTokenSource cancellationSource;

        // Performance tracking
        private int chunksGenerated = 0;
        private float totalGenerationTime = 0f;

        public UltraFastChunkGenerator(WorldGenerator worldGenerator)
        {
            world = worldGenerator ?? throw new ArgumentNullException(nameof(worldGenerator));
            cancellationSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Generate chunk data asynchronously with minimal allocations
        /// </summary>
        public async Task<ChunkData> GenerateChunkAsync(Vector2Int coord, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancellationSource.Token);

            float startTime = Time.realtimeSinceStartup;

            // Run on background thread
            ChunkData data = await Task.Run(() => GenerateChunkData(coord, linkedCts.Token), linkedCts.Token);

            // Track performance
            float elapsed = Time.realtimeSinceStartup - startTime;
            totalGenerationTime += elapsed;
            chunksGenerated++;

            return data;
        }

        /// <summary>
        /// Generate chunk data - ULTRA OPTIMIZED
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

            float seedOffset = (world.worldSeed % 10000) * 0.01f;

            // Pre-calculate column data (biome + surface height)
            ColumnData[,] columns = new ColumnData[sizeX, sizeZ];
            int maxY = 0; // Track highest surface for early exit

            for (int lx = 0; lx < sizeX; lx++)
            {
                for (int lz = 0; lz < sizeZ; lz++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int worldX = coord.x * sizeX + lx;
                    int worldZ = coord.y * sizeZ + lz;

                    // Calculate surface height (inline, no cache)
                    float height = CalculateTerrainHeight(worldX, worldZ, seedOffset);
                    int surfaceY = Mathf.FloorToInt(height);

                    // Fast biome determination (inline)
                    BiomeType biomeType = GetBiomeType(worldX, worldZ, seedOffset);

                    // Determine generation height
                    int columnMaxY = surfaceY;
                    if (biomeType == BiomeType.Ocean)
                    {
                        columnMaxY = Mathf.Max(surfaceY, world.seaLevel + 5);
                    }

                    columns[lx, lz] = new ColumnData
                    {
                        surfaceY = surfaceY,
                        biomeType = biomeType,
                        maxY = columnMaxY
                    };

                    if (columnMaxY > maxY) maxY = columnMaxY;
                }
            }

            // Clamp generation to actual max height (HUGE OPTIMIZATION)
            int actualMaxY = Mathf.Min(maxY + 1, sizeY);

            // Generate blocks only up to actualMaxY (skip empty air above)
            for (int lx = 0; lx < sizeX; lx++)
            {
                for (int lz = 0; lz < sizeZ; lz++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ColumnData col = columns[lx, lz];
                    int worldX = coord.x * sizeX + lx;
                    int worldZ = coord.y * sizeZ + lz;

                    // Generate column ONLY up to necessary height
                    for (int ly = 0; ly <= col.maxY && ly < actualMaxY; ly++)
                    {
                        Vector3Int worldPos = new Vector3Int(worldX, ly, worldZ);
                        data.blocks[lx, ly, lz] = GenerateBlockType(worldPos, col.surfaceY, col.biomeType);
                    }
                    // Everything above col.maxY is already BlockType.Air (default array value = 0)
                }
            }

            return data;
        }

        /// <summary>
        /// Calculate terrain height (inline, no cache)
        /// </summary>
        private float CalculateTerrainHeight(int worldX, int worldZ, float seedOffset)
        {
            // Base terrain
            float baseX = worldX * 0.008f + seedOffset;
            float baseZ = worldZ * 0.008f + seedOffset;
            float baseNoise = Mathf.PerlinNoise(baseX, baseZ) * 2f - 1f;

            // Hills
            float hillX = worldX * 0.003f + seedOffset;
            float hillZ = worldZ * 0.003f + seedOffset;
            float hillNoise = Mathf.PerlinNoise(hillX, hillZ);
            float hillMultiplier = Mathf.Max(0f, hillNoise - 0.6f) * 10f;

            // Detail (simplified)
            float detailX = worldX * 0.05f + seedOffset;
            float detailZ = worldZ * 0.05f + seedOffset;
            float detailNoise = Mathf.PerlinNoise(detailX, detailZ) * 0.3f;

            return 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise;
        }

        /// <summary>
        /// Fast biome type determination (returns enum, not BiomeData)
        /// </summary>
        private BiomeType GetBiomeType(int worldX, int worldZ, float seedOffset)
        {
            // Ocean check
            float biomeX = worldX * world.oceanPlainsNoiseScale + seedOffset;
            float biomeZ = worldZ * world.oceanPlainsNoiseScale + seedOffset;
            float biomeNoise = Mathf.PerlinNoise(biomeX, biomeZ);

            if (biomeNoise < world.oceanCoverage)
                return BiomeType.Ocean;

            // Forest check (land only)
            float forestX = worldX * world.forestNoiseScale + seedOffset * 2.5f;
            float forestZ = worldZ * world.forestNoiseScale + seedOffset * 3.7f;
            float forestNoise = Mathf.PerlinNoise(forestX, forestZ);

            return forestNoise < world.forestCoverage ? BiomeType.Forest : BiomeType.Plains;
        }

        /// <summary>
        /// Generate single block type (simplified logic)
        /// </summary>
        private BlockType GenerateBlockType(Vector3Int worldPos, int surfaceY, BiomeType biome)
        {
            int y = worldPos.y;

            // Bedrock
            if (y <= 2) return BlockType.Bedrock;

            // Tunnel check (expensive, but necessary)
            if (world.enableTunnels &&
                WorldGeneration.HorizontalTunnelGenerator.IsTunnelBlock(worldPos, world.worldSeed, world.tunnelSettings))
            {
                return BlockType.Air;
            }

            // Ocean biome
            if (biome == BiomeType.Ocean)
            {
                int oceanFloor = world.seaLevel - world.maxOceanDepth;

                if (y > world.seaLevel)
                    return BlockType.Air;
                else if (y > surfaceY && y <= world.seaLevel)
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
        /// Ultra-fast underground generation
        /// </summary>
        private BlockType GenerateUndergroundBlock(int x, int y, int z)
        {
            // Fast random
            int hash = x;
            hash = hash * 31 + y;
            hash = hash * 31 + z;
            hash = hash * 31 + world.worldSeed;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = (hash >> 16) ^ hash;
            float chance = (hash & 0x7FFFFFFF) / (float)0x7FFFFFFF;

            // 70% is stone - fast path
            if (chance > 0.30f) return BlockType.Stone;

            // Simplified ore distribution
            if (y <= 30)
            {
                float depth = (100f - y) / 97f;
                if (y <= 20 && chance < 0.004f * depth) return BlockType.Diamond;
                if (y <= 25 && chance < 0.010f * depth) return BlockType.Gold;
                if (chance < 0.08f) return BlockType.Iron;
                if (chance < 0.18f) return BlockType.Coal;
                if (chance < 0.25f) return BlockType.Gravel;
            }
            else if (y <= 60)
            {
                if (chance < 0.008f) return BlockType.Gold;
                if (chance < 0.10f) return BlockType.Iron;
                if (chance < 0.20f) return BlockType.Coal;
                if (chance < 0.15f) return BlockType.Gravel;
            }
            else
            {
                if (chance < 0.06f) return BlockType.Iron;
                if (chance < 0.12f) return BlockType.Coal;
                if (chance < 0.10f) return BlockType.Gravel;
            }

            return BlockType.Stone;
        }

        public ChunkGenerationStats GetStats()
        {
            return new ChunkGenerationStats
            {
                ChunksGenerated = chunksGenerated,
                AverageGenerationTime = chunksGenerated > 0 ? totalGenerationTime / chunksGenerated : 0f,
                CacheSize = 0 // No cache
            };
        }

        public void Dispose()
        {
            cancellationSource?.Cancel();
            cancellationSource?.Dispose();
        }

        /// <summary>
        /// Column metadata for pre-calculation
        /// </summary>
        private struct ColumnData
        {
            public int surfaceY;
            public BiomeType biomeType;
            public int maxY;
        }
    }
}
