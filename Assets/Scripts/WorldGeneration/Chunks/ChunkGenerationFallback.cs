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
            int yieldFrequency = Mathf.Max(1, chunkSizeX * chunkSizeZ / 8); // Yield every ~1/8th of layer

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
            float scale = 0.01f;
            float baseHeight = 8f;
            float amplitude = 4f;
            
            float noise = Mathf.PerlinNoise(worldX * scale + worldSeed * 0.1f, worldZ * scale + worldSeed * 0.1f);
            return baseHeight + noise * amplitude;
        }
        
        private static BlockType GenerateBlockTypeAt(int worldX, int worldY, int worldZ, int surfaceY, int worldSeed)
        {
            // Bedrock at bottom
            if (worldY == 0) return BlockType.Bedrock;
            
            // Air above surface
            if (worldY > surfaceY) return BlockType.Air;
            
            // Surface layer
            if (worldY == surfaceY) return BlockType.Grass;
            
            // Subsurface layers
            if (worldY >= surfaceY - 3) return BlockType.Dirt;
            
            // Deep stone with ore generation
            if (worldY < surfaceY - 3)
            {
                // Simple ore generation using hash-based random
                float oreChance = GetHashedFloat(worldX, worldY, worldZ, worldSeed);
                
                if (oreChance < 0.02f) return BlockType.Diamond;
                if (oreChance < 0.05f) return BlockType.Gold;
                if (oreChance < 0.08f) return BlockType.Iron;
                if (oreChance < 0.12f) return BlockType.Coal;
                
                return BlockType.Stone;
            }
            
            return BlockType.Air;
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