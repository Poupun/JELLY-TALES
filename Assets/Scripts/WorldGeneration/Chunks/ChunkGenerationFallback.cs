using System.Collections;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Fallback chunk generation for systems without Unity Jobs package
    /// Provides similar optimization benefits through coroutines and smart yielding
    /// </summary>
    public static class ChunkGenerationFallback
    {
        public static IEnumerator GenerateChunkAsync(WorldGeneration.Chunks.Chunk chunk, Vector2Int coord, 
                                                   int chunkSizeX, int chunkSizeY, int chunkSizeZ, int worldSeed)
        {
            int processedBlocks = 0;
            int yieldFrequency = Mathf.Max(100, chunkSizeX * chunkSizeZ * 2); // Reduced yielding for faster generation

            for (int x = 0; x < chunkSizeX; x++)
            {
                for (int z = 0; z < chunkSizeZ; z++)
                {
                    int worldX = coord.x * chunkSizeX + x;
                    int worldZ = coord.y * chunkSizeZ + z;
                    
                    // Calculate height using same logic as job system
                    float height = GetHeightAt(worldX, worldZ, worldSeed);
                    int surfaceY = Mathf.FloorToInt(height);
                    
                    for (int y = 0; y < chunkSizeY; y++)
                    {
                        BlockType blockType = GenerateBlockTypeAt(worldX, y, worldZ, surfaceY, worldSeed);
                        chunk.SetLocal(x, y, z, blockType);
                        processedBlocks++;
                        
                        // Yield periodically to maintain frame rate
                        if (processedBlocks % yieldFrequency == 0)
                        {
                            yield return null;
                        }
                    }
                }
            }
        }
        
        private static float GetHeightAt(int worldX, int worldZ, int worldSeed)
        {
            // Updated to match main generator: surface at Y=100-120
            float seedOffset = (worldSeed % 10000) * 0.01f;
            
            // Base terrain noise
            float baseX = worldX * 0.008f + seedOffset;
            float baseZ = worldZ * 0.008f + seedOffset;
            float baseNoise = Mathf.PerlinNoise(baseX, baseZ) * 2f - 1f;
            
            // Hill noise for occasional hills
            float hillX = worldX * 0.003f + seedOffset;
            float hillZ = worldZ * 0.003f + seedOffset;
            float hillNoise = Mathf.PerlinNoise(hillX, hillZ);
            float hillMultiplier = Mathf.Max(0f, hillNoise - 0.6f) * 10f;
            
            // Detail noise for fine terrain variation
            float detailX = worldX * 0.05f + seedOffset;
            float detailZ = worldZ * 0.05f + seedOffset;
            float detailNoise = Mathf.PerlinNoise(detailX, detailZ) * 0.3f;
            
            // Combine: surface at Y=100-120
            float combinedHeight = 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise;
            return combinedHeight;
        }
        
        private static BlockType GenerateBlockTypeAt(int worldX, int worldY, int worldZ, int surfaceY, int worldSeed)
        {
            // Bedrock layer (expanded to Y=0-2)
            if (worldY <= 2) return BlockType.Bedrock;
            
            // Air above surface
            if (worldY > surfaceY) return BlockType.Air;
            
            // Surface layer
            if (worldY == surfaceY) return BlockType.Grass;
            
            // Subsurface layers (dirt below grass)
            if (worldY >= surfaceY - 4) return BlockType.Dirt;
            
            // Underground layers (Y 3-99) - Expanded underground generation
            if (worldY <= 99)
            {
                return GenerateUndergroundBlock(worldX, worldY, worldZ, worldSeed);
            }
            
            return BlockType.Air;
        }
        
        private static BlockType GenerateUndergroundBlock(int worldX, int worldY, int worldZ, int worldSeed)
        {
            float chance = GetHashedFloat(worldX, worldY, worldZ, worldSeed);
            
            // Calculate depth factor: deeper = rarer ores (Y=3 is deepest, Y=99 is shallowest)
            float depthFactor = (100f - worldY) / 97f; // 0.0 at surface, 1.0 at deepest
            
            // Deep Underground (Y 3-30): Deepest ores
            if (worldY <= 30)
            {
                // Diamond (very rare, only in deepest layers)
                if (worldY <= 20 && chance < 0.004f * depthFactor)
                    return BlockType.Diamond;
                
                // Gold (rare, deep preferred)
                if (worldY <= 25 && chance < 0.010f * depthFactor)
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
            if (worldY <= 60)
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
            if (worldY <= 99)
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
        
        private static float GetHashedFloat(int x, int y, int z, int seed)
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
    }
}