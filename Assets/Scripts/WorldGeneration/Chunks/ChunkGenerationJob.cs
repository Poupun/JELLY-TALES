using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Job System implementation for async chunk generation to reduce main thread blocking
    /// </summary>
    public struct ChunkGenerationJob : IJob
    {
        [ReadOnly] public Vector2Int chunkCoord;
        [ReadOnly] public int chunkSizeX;
        [ReadOnly] public int chunkSizeY;  
        [ReadOnly] public int chunkSizeZ;
        [ReadOnly] public int worldSeed;
        
        // Output array for block types (flattened 3D array)
        [WriteOnly] public NativeArray<int> blockData;
        
        public void Execute()
        {
            // Generate blocks using noise functions
            for (int x = 0; x < chunkSizeX; x++)
            {
                for (int z = 0; z < chunkSizeZ; z++)
                {
                    int worldX = chunkCoord.x * chunkSizeX + x;
                    int worldZ = chunkCoord.y * chunkSizeZ + z;
                    
                    // Calculate height using same noise as main generator
                    float height = GetHeightAt(worldX, worldZ);
                    int surfaceY = Mathf.FloorToInt(height);
                    
                    for (int y = 0; y < chunkSizeY; y++)
                    {
                        int index = GetFlatIndex(x, y, z);
                        BlockType blockType = GenerateBlockTypeAt(worldX, y, worldZ, surfaceY);
                        blockData[index] = (int)blockType;
                    }
                }
            }
        }
        
        private int GetFlatIndex(int x, int y, int z)
        {
            return x + y * chunkSizeX + z * chunkSizeX * chunkSizeY;
        }
        
        private float GetHeightAt(int worldX, int worldZ)
        {
            // Use Unity's Mathf.PerlinNoise for consistency with main generator
            float scale = 0.01f;
            float baseHeight = 8f;
            float amplitude = 4f;
            
            float noise = Mathf.PerlinNoise(worldX * scale + worldSeed * 0.1f, worldZ * scale + worldSeed * 0.1f);
            return baseHeight + noise * amplitude;
        }
        
        private BlockType GenerateBlockTypeAt(int worldX, int worldY, int worldZ, int surfaceY)
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
    }
    
    /// <summary>
    /// Job for generating chunk mesh data asynchronously
    /// </summary>
    public struct ChunkMeshJob : IJob
    {
        [ReadOnly] public NativeArray<int> blockData;
        [ReadOnly] public Vector2Int chunkCoord;
        [ReadOnly] public int chunkSizeX;
        [ReadOnly] public int chunkSizeY;
        [ReadOnly] public int chunkSizeZ;
        
        // Output mesh data
        public NativeList<Vector3> vertices;
        public NativeList<Vector3> normals;
        public NativeList<Vector2> uvs;
        public NativeList<Color> colors;
        public NativeList<int> triangles;
        
        private static readonly Vector3Int[] Directions = new Vector3Int[]
        {
            Vector3Int.right, Vector3Int.left,
            Vector3Int.up, Vector3Int.down,
            Vector3Int.forward, Vector3Int.back
        };
        
        private static readonly Vector3[][] FaceVerts = new Vector3[][]
        {
            // +X
            new [] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) },
            // -X  
            new [] { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) },
            // +Y
            new [] { new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0), new Vector3(0,1,0) },
            // -Y
            new [] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) },
            // +Z
            new [] { new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) },
            // -Z
            new [] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }
        };
        
        private static readonly Vector2[] QuadUV = new Vector2[]
        {
            new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0)
        };
        
        public void Execute()
        {
            for (int x = 0; x < chunkSizeX; x++)
            {
                for (int y = 0; y < chunkSizeY; y++)
                {
                    for (int z = 0; z < chunkSizeZ; z++)
                    {
                        int index = GetFlatIndex(x, y, z);
                        BlockType blockType = (BlockType)blockData[index];
                        
                        if (blockType == BlockType.Air) continue;
                        
                        Vector3 blockPos = new Vector3(x, y, z);
                        
                        // Check each face
                        for (int face = 0; face < 6; face++)
                        {
                            Vector3Int dir = Directions[face];
                            int nx = x + dir.x;
                            int ny = y + dir.y;
                            int nz = z + dir.z;
                            
                            BlockType neighborType = GetNeighborBlock(nx, ny, nz);
                            
                            // Only render face if neighbor is air or transparent
                            if (neighborType != BlockType.Air && IsBlockOpaque(neighborType)) continue;
                            
                            AddQuad(blockPos, face);
                        }
                    }
                }
            }
        }
        
        private BlockType GetNeighborBlock(int x, int y, int z)
        {
            // Check bounds
            if (x < 0 || x >= chunkSizeX || y < 0 || y >= chunkSizeY || z < 0 || z >= chunkSizeZ)
            {
                return BlockType.Air; // Assume air outside chunk bounds
            }
            
            int index = GetFlatIndex(x, y, z);
            return (BlockType)blockData[index];
        }
        
        private bool IsBlockOpaque(BlockType blockType)
        {
            return blockType != BlockType.Air; // Simplified - all non-air blocks are opaque
        }
        
        private void AddQuad(Vector3 blockPos, int faceIndex)
        {
            var faceVerts = FaceVerts[faceIndex];
            int startVertex = vertices.Length;
            
            // Add vertices
            for (int i = 0; i < 4; i++)
            {
                vertices.Add(blockPos + faceVerts[i]);
                normals.Add((Vector3)Directions[faceIndex]);
                uvs.Add(QuadUV[i]);
                
                // Simple face shading
                float shade = GetFaceShade(faceIndex);
                colors.Add(new Color(shade, shade, shade, 1f));
            }
            
            // Add triangles (two triangles per quad)
            triangles.Add(startVertex + 0);
            triangles.Add(startVertex + 1);
            triangles.Add(startVertex + 2);
            
            triangles.Add(startVertex + 0);
            triangles.Add(startVertex + 2);
            triangles.Add(startVertex + 3);
        }
        
        private float GetFaceShade(int faceIndex)
        {
            switch (faceIndex)
            {
                case 2: return 1.0f;    // +Y (top) brightest
                case 3: return 0.5f;    // -Y (bottom) darkest
                case 0:                 // +X
                case 1: return 0.9f;    // -X
                case 4:                 // +Z
                case 5: return 0.8f;    // -Z
                default: return 0.8f;
            }
        }
        
        private int GetFlatIndex(int x, int y, int z)
        {
            return x + y * chunkSizeX + z * chunkSizeX * chunkSizeY;
        }
    }
}