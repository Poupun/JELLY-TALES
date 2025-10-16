using System.Collections.Generic;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Greedy meshing algorithm - Minecraft's secret weapon.
    ///
    /// Instead of 1 quad per block face (24 vertices per block),
    /// combines adjacent identical faces into larger quads.
    ///
    /// Example: 10x10 flat grass surface
    /// - Naive: 100 blocks × 4 vertices = 400 vertices
    /// - Greedy: 1 large quad = 4 vertices (100x reduction!)
    ///
    /// Expected performance: 80-95% fewer vertices than FastChunkMeshBuilder
    /// </summary>
    public static class GreedyMeshBuilder
    {
        // Face directions
        private static readonly Vector3Int[] Normals = new Vector3Int[]
        {
            new Vector3Int(1, 0, 0),   // Right
            new Vector3Int(-1, 0, 0),  // Left
            new Vector3Int(0, 1, 0),   // Up
            new Vector3Int(0, -1, 0),  // Down
            new Vector3Int(0, 0, 1),   // Forward
            new Vector3Int(0, 0, -1)   // Back
        };

        public static void BuildMesh(WorldGenerator world, Chunk chunk, bool addCollider)
        {
            if (world == null || chunk == null) return;
            var parent = chunk.parent;
            if (parent == null) return;

            var mf = parent.GetComponent<MeshFilter>();
            if (mf == null) mf = parent.gameObject.AddComponent<MeshFilter>();
            var mr = parent.GetComponent<MeshRenderer>();
            if (mr == null) mr = parent.gameObject.AddComponent<MeshRenderer>();

            // Use minimal allocations
            var verts = new List<Vector3>(chunk.sizeX * chunk.sizeZ * 4); // Conservative
            var uvs = new List<Vector2>(chunk.sizeX * chunk.sizeZ * 4);
            var colors = new List<Color>(chunk.sizeX * chunk.sizeZ * 4);
            var trisByMaterial = new Dictionary<Material, List<int>>(4);

            var collisionVerts = new List<Vector3>(chunk.sizeX * chunk.sizeZ * 2);
            var collisionTris = new List<int>(chunk.sizeX * chunk.sizeZ * 3);

            int chunkOriginX = chunk.coord.x * chunk.sizeX;
            int chunkOriginZ = chunk.coord.y * chunk.sizeZ;

            // Process each face direction with greedy meshing
            for (int dir = 0; dir < 6; dir++)
            {
                Vector3Int normal = Normals[dir];
                bool isPositive = normal.x > 0 || normal.y > 0 || normal.z > 0;

                // Determine axis (0=X, 1=Y, 2=Z)
                int axis = normal.x != 0 ? 0 : (normal.y != 0 ? 1 : 2);

                // Get perpendicular axes
                int axis1 = (axis + 1) % 3;
                int axis2 = (axis + 2) % 3;

                int[] dimensions = { chunk.sizeX, chunk.sizeY, chunk.sizeZ };
                int mainAxis = dimensions[axis];
                int width = dimensions[axis1];
                int height = dimensions[axis2];

                // Mask for tracking which faces are merged
                bool[,] mask = new bool[width, height];
                BlockType[,] blockMask = new BlockType[width, height];

                // Sweep through each slice perpendicular to normal
                for (int d = 0; d < mainAxis; d++)
                {
                    // Build mask for this slice
                    for (int i = 0; i < width; i++)
                    {
                        for (int j = 0; j < height; j++)
                        {
                            mask[i, j] = false;
                            blockMask[i, j] = BlockType.Air;

                            // Get block position
                            Vector3Int pos = GetBlockPos(axis, axis1, axis2, d, i, j);
                            if (!IsInBounds(pos, chunk)) continue;

                            BlockType block = chunk.GetLocal(pos.x, pos.y, pos.z);
                            if (block == BlockType.Air) continue;

                            // Get neighbor position
                            Vector3Int neighborPos = pos + normal;
                            BlockType neighbor;

                            if (IsInBounds(neighborPos, chunk))
                            {
                                neighbor = chunk.GetLocal(neighborPos.x, neighborPos.y, neighborPos.z);
                            }
                            else
                            {
                                // Check across chunk boundary
                                int worldX = chunkOriginX + neighborPos.x;
                                int worldY = neighborPos.y;
                                int worldZ = chunkOriginZ + neighborPos.z;

                                if (worldY < 0 || worldY >= world.worldHeight)
                                {
                                    neighbor = BlockType.Air;
                                }
                                else
                                {
                                    Vector3Int worldPos = new Vector3Int(worldX, worldY, worldZ);
                                    if (world.IsChunkLoadedAt(worldPos))
                                    {
                                        neighbor = world.GetBlockType(worldPos);
                                    }
                                    else
                                    {
                                        neighbor = (block == BlockType.Water && worldY <= world.seaLevel)
                                            ? BlockType.Water
                                            : BlockType.Air;
                                    }
                                }
                            }

                            // Should this face be rendered?
                            if (ShouldRenderFace(block, neighbor, dir, world))
                            {
                                mask[i, j] = true;
                                blockMask[i, j] = block;
                            }
                        }
                    }

                    // Greedy meshing: merge adjacent faces
                    for (int j = 0; j < height; j++)
                    {
                        for (int i = 0; i < width; )
                        {
                            if (!mask[i, j])
                            {
                                i++;
                                continue;
                            }

                            BlockType currentBlock = blockMask[i, j];

                            // Find width of quad (expand horizontally)
                            int w;
                            for (w = 1; i + w < width && mask[i + w, j] && blockMask[i + w, j] == currentBlock; w++) { }

                            // Find height of quad (expand vertically)
                            int h;
                            bool done = false;
                            for (h = 1; j + h < height; h++)
                            {
                                for (int k = 0; k < w; k++)
                                {
                                    if (!mask[i + k, j + h] || blockMask[i + k, j + h] != currentBlock)
                                    {
                                        done = true;
                                        break;
                                    }
                                }
                                if (done) break;
                            }

                            // Create merged quad (w × h instead of 1x1)
                            AddQuad(verts, uvs, colors, trisByMaterial, collisionVerts, collisionTris,
                                    axis, axis1, axis2, d, i, j, w, h, normal, currentBlock, world, dir, addCollider,
                                    chunkOriginX, chunkOriginZ);

                            // Clear merged area from mask
                            for (int jj = 0; jj < h; jj++)
                            {
                                for (int ii = 0; ii < w; ii++)
                                {
                                    mask[i + ii, j + jj] = false;
                                }
                            }

                            i += w;
                        }
                    }
                }
            }

            // Build final mesh
            var mesh = new Mesh();
            mesh.name = $"GreedyChunk_{chunk.coord.x}_{chunk.coord.y}";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);

            // Calculate normals (simple, fast)
            mesh.RecalculateNormals();

            // Setup materials
            var materials = new List<Material>(trisByMaterial.Keys);
            materials.Sort((a, b) => string.Compare(a?.name, b?.name, System.StringComparison.Ordinal));

            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++)
            {
                mesh.SetTriangles(trisByMaterial[materials[i]], i);
            }
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
            mr.sharedMaterials = materials.ToArray();

            // Water settings
            bool hasWater = materials.Exists(m => m != null && m.name.Contains("Water"));
            if (hasWater)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            // Collision
            if (addCollider && collisionVerts.Count > 0)
            {
                var mc = parent.GetComponent<MeshCollider>();
                if (mc == null) mc = parent.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = null;

                Mesh collisionMesh = new Mesh();
                collisionMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                collisionMesh.SetVertices(collisionVerts);
                collisionMesh.SetTriangles(collisionTris, 0);
                collisionMesh.RecalculateBounds();
                mc.sharedMesh = collisionMesh;
            }
        }

        private static void AddQuad(List<Vector3> verts, List<Vector2> uvs, List<Color> colors,
                                     Dictionary<Material, List<int>> trisByMaterial,
                                     List<Vector3> collisionVerts, List<int> collisionTris,
                                     int axis, int axis1, int axis2, int d, int i, int j, int w, int h,
                                     Vector3Int normal, BlockType blockType, WorldGenerator world, int dir, bool addCollider,
                                     int chunkOriginX, int chunkOriginZ)
        {
            // Get material
            Material mat = world?.GetBlockMaterial(blockType);
            if (mat == null) return;

            if (!trisByMaterial.TryGetValue(mat, out List<int> tris))
            {
                tris = new List<int>(256);
                trisByMaterial[mat] = tris;
            }

            // Vertex positions
            Vector3 pos = GetBlockPosFloat(axis, axis1, axis2, d, i, j);
            Vector3 du = GetDU(axis, axis1, axis2) * w;
            Vector3 dv = GetDV(axis, axis1, axis2) * h;

            // Adjust for face direction
            if (normal.x < 0 || normal.y < 0 || normal.z < 0)
            {
                pos += GetAxisVector(axis);
            }

            int vi = verts.Count;
            verts.Add(pos);
            verts.Add(pos + dv);
            verts.Add(pos + du + dv);
            verts.Add(pos + du);

            // UVs (tiled)
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(0, h));
            uvs.Add(new Vector2(w, h));
            uvs.Add(new Vector2(w, 0));

            // Colors (shading)
            float shade = 1f;
            if (blockType != BlockType.Water && world != null && world.enableFaceShading)
            {
                switch (dir)
                {
                    case 2: shade = 1.00f; break; // Up
                    case 3: shade = world.bottomShade; break; // Down
                    case 0: case 1: shade = world.eastWestShade; break; // X
                    case 4: case 5: shade = world.northSouthShade; break; // Z
                }
            }

            Color c = new Color(shade, shade, shade, 1f);
            colors.Add(c); colors.Add(c); colors.Add(c); colors.Add(c);

            // Triangles
            tris.Add(vi + 0); tris.Add(vi + 1); tris.Add(vi + 2);
            tris.Add(vi + 0); tris.Add(vi + 2); tris.Add(vi + 3);

            // Collision (skip water)
            if (addCollider && blockType != BlockType.Water)
            {
                int cvi = collisionVerts.Count;
                collisionVerts.Add(pos);
                collisionVerts.Add(pos + dv);
                collisionVerts.Add(pos + du + dv);
                collisionVerts.Add(pos + du);

                collisionTris.Add(cvi + 0); collisionTris.Add(cvi + 1); collisionTris.Add(cvi + 2);
                collisionTris.Add(cvi + 0); collisionTris.Add(cvi + 2); collisionTris.Add(cvi + 3);
            }
        }

        private static Vector3Int GetBlockPos(int axis, int axis1, int axis2, int d, int i, int j)
        {
            Vector3Int pos = Vector3Int.zero;
            pos[axis] = d;
            pos[axis1] = i;
            pos[axis2] = j;
            return pos;
        }

        private static Vector3 GetBlockPosFloat(int axis, int axis1, int axis2, int d, int i, int j)
        {
            Vector3 pos = Vector3.zero;
            pos[axis] = d;
            pos[axis1] = i;
            pos[axis2] = j;
            return pos;
        }

        private static Vector3 GetDU(int axis, int axis1, int axis2)
        {
            Vector3 du = Vector3.zero;
            du[axis1] = 1f;
            return du;
        }

        private static Vector3 GetDV(int axis, int axis1, int axis2)
        {
            Vector3 dv = Vector3.zero;
            dv[axis2] = 1f;
            return dv;
        }

        private static Vector3 GetAxisVector(int axis)
        {
            Vector3 v = Vector3.zero;
            v[axis] = 1f;
            return v;
        }

        private static bool IsInBounds(Vector3Int pos, Chunk chunk)
        {
            return pos.x >= 0 && pos.x < chunk.sizeX &&
                   pos.y >= 0 && pos.y < chunk.sizeY &&
                   pos.z >= 0 && pos.z < chunk.sizeZ;
        }

        private static bool ShouldRenderFace(BlockType currentBlock, BlockType neighborBlock, int faceDirection, WorldGenerator world)
        {
            if (neighborBlock == BlockType.Air) return true;
            if (currentBlock == BlockType.Water)
            {
                if (faceDirection == 3) return false; // No bottom
                if (neighborBlock == BlockType.Water) return false;
                return true;
            }
            if (neighborBlock == BlockType.Water) return true;
            if (world != null && world.IsBlockOpaque(neighborBlock)) return false;
            return true;
        }
    }
}
