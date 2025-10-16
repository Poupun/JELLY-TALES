using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Incremental mesh builder that spreads mesh building across multiple frames.
    /// Prevents FPS drops by limiting work per frame.
    /// </summary>
    public static class IncrementalMeshBuilder
    {
        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.right, Vector3Int.left,
            Vector3Int.up, Vector3Int.down,
            Vector3Int.forward, Vector3Int.back
        };

        private static readonly Vector3[][] FaceVerts =
        {
            new [] { new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) },
            new [] { new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) },
            new [] { new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0), new Vector3(0,1,0) },
            new [] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1) },
            new [] { new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) },
            new [] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }
        };

        private static readonly Vector2[] QuadUV =
        {
            new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0)
        };

        /// <summary>
        /// Build mesh incrementally across multiple frames to prevent FPS drops
        /// </summary>
        public static IEnumerator BuildMeshIncremental(WorldGenerator world, Chunk chunk, bool addCollider, float maxFrameTime = 0.003f)
        {
            if (world == null || chunk == null) yield break;
            var parent = chunk.parent;
            if (parent == null) yield break;

            var mf = parent.GetComponent<MeshFilter>();
            if (mf == null) mf = parent.gameObject.AddComponent<MeshFilter>();
            var mr = parent.GetComponent<MeshRenderer>();
            if (mr == null) mr = parent.gameObject.AddComponent<MeshRenderer>();

            // Pre-allocate conservatively
            int estimatedFaces = (chunk.sizeX * chunk.sizeY * chunk.sizeZ) / 6;
            var trisByMaterial = new Dictionary<Material, List<int>>(4);
            var verts = new List<Vector3>(estimatedFaces * 4);
            var norms = new List<Vector3>(estimatedFaces * 4);
            var uvs = new List<Vector2>(estimatedFaces * 4);
            var colors = new List<Color>(estimatedFaces * 4);

            var collisionVerts = new List<Vector3>(estimatedFaces * 2);
            var collisionTris = new List<int>(estimatedFaces * 3);

            List<int> GetList(Material m)
            {
                if (!trisByMaterial.TryGetValue(m, out var list))
                {
                    list = new List<int>(512);
                    trisByMaterial[m] = list;
                }
                return list;
            }

            int chunkOriginX = chunk.coord.x * chunk.sizeX;
            int chunkOriginZ = chunk.coord.y * chunk.sizeZ;

            float frameStartTime = Time.realtimeSinceStartup;
            int blocksProcessed = 0;
            const int BLOCKS_PER_CHECK = 16; // Check time every 16 blocks

            // Process blocks slice by slice (Y layers)
            for (int y = 0; y < chunk.sizeY; y++)
            {
                for (int x = 0; x < chunk.sizeX; x++)
                {
                    for (int z = 0; z < chunk.sizeZ; z++)
                    {
                        var blockType = chunk.GetLocal(x, y, z);
                        if (blockType == BlockType.Air) continue;

                        Vector3 basePos = new Vector3(x, y, z);

                        // Process all 6 faces
                        for (int d = 0; d < 6; d++)
                        {
                            var dir = Directions[d];
                            int nx = x + dir.x, ny = y + dir.y, nz = z + dir.z;
                            BlockType neighbor;

                            bool inside = nx >= 0 && nx < chunk.sizeX && ny >= 0 && ny < chunk.sizeY && nz >= 0 && nz < chunk.sizeZ;
                            if (inside)
                            {
                                neighbor = chunk.GetLocal(nx, ny, nz);
                            }
                            else if (world != null && ny >= 0 && ny < world.worldHeight)
                            {
                                int worldX = chunkOriginX + nx;
                                int worldZ = chunkOriginZ + nz;
                                Vector3Int neighborWorldPos = new Vector3Int(worldX, ny, worldZ);

                                if (world.IsChunkLoadedAt(neighborWorldPos))
                                {
                                    neighbor = world.GetBlockType(neighborWorldPos);
                                }
                                else
                                {
                                    neighbor = (blockType == BlockType.Water && ny <= world.seaLevel)
                                        ? BlockType.Water
                                        : BlockType.Air;
                                }
                            }
                            else
                            {
                                neighbor = BlockType.Air;
                            }

                            if (!ShouldRenderFace(blockType, neighbor, d, world))
                                continue;

                            // Add face geometry
                            var f = FaceVerts[d];
                            int vi = verts.Count;

                            verts.Add(basePos + f[0]);
                            verts.Add(basePos + f[1]);
                            verts.Add(basePos + f[2]);
                            verts.Add(basePos + f[3]);

                            var n = (Vector3)dir;
                            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);

                            uvs.Add(QuadUV[0]); uvs.Add(QuadUV[1]); uvs.Add(QuadUV[2]); uvs.Add(QuadUV[3]);

                            float shade = 1f;
                            if (blockType != BlockType.Water && world != null && world.enableFaceShading)
                            {
                                switch (d)
                                {
                                    case 2: shade = 1.00f; break;
                                    case 3: shade = world.bottomShade; break;
                                    case 0: case 1: shade = world.eastWestShade; break;
                                    case 4: case 5: shade = world.northSouthShade; break;
                                }

                                if (world.variationStrength > 0f)
                                {
                                    int worldX = chunkOriginX + x;
                                    float h = WorldGenerator.Hash(worldX, y, chunkOriginZ + z);
                                    float v = (h - 0.5f) * 2f * world.variationStrength;
                                    shade = Mathf.Clamp01(shade * (1f + v));
                                }
                            }

                            Color c = new Color(shade, shade, shade, 1f);
                            colors.Add(c); colors.Add(c); colors.Add(c); colors.Add(c);

                            Material faceMat = null;
                            if (world != null)
                            {
                                faceMat = world.GetFaceMaterial(blockType, d);
                                if (faceMat == null)
                                {
                                    faceMat = world.GetBlockMaterial(blockType);
                                }
                            }
                            if (faceMat == null) continue;

                            var tri = GetList(faceMat);
                            tri.Add(vi + 0); tri.Add(vi + 1); tri.Add(vi + 2);
                            tri.Add(vi + 0); tri.Add(vi + 2); tri.Add(vi + 3);

                            if (blockType != BlockType.Water)
                            {
                                int collisionVI = collisionVerts.Count;
                                collisionVerts.Add(basePos + f[0]);
                                collisionVerts.Add(basePos + f[1]);
                                collisionVerts.Add(basePos + f[2]);
                                collisionVerts.Add(basePos + f[3]);

                                collisionTris.Add(collisionVI + 0);
                                collisionTris.Add(collisionVI + 1);
                                collisionTris.Add(collisionVI + 2);
                                collisionTris.Add(collisionVI + 0);
                                collisionTris.Add(collisionVI + 2);
                                collisionTris.Add(collisionVI + 3);
                            }
                        }

                        blocksProcessed++;

                        // Check frame time every N blocks
                        if (blocksProcessed % BLOCKS_PER_CHECK == 0)
                        {
                            float elapsed = Time.realtimeSinceStartup - frameStartTime;
                            if (elapsed > maxFrameTime)
                            {
                                yield return null;
                                frameStartTime = Time.realtimeSinceStartup;
                            }
                        }
                    }
                }
            }

            // Build final mesh (this part is fast)
            yield return null; // Yield before final mesh construction

            var mesh = new Mesh();
            mesh.name = $"Chunk_{chunk.coord.x}_{chunk.coord.y}";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);

            var materials = new List<Material>(trisByMaterial.Keys);
            materials.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                return string.Compare(a.name, b.name, System.StringComparison.Ordinal);
            });

            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++)
            {
                mesh.SetTriangles(trisByMaterial[materials[i]], i);
            }
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
            mr.sharedMaterials = materials.ToArray();

            bool hasWater = materials.Exists(m => m != null && m.name.Contains("Water"));
            if (hasWater)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            // Collision mesh
            if (addCollider && collisionVerts.Count > 0)
            {
                yield return null; // Yield before collision mesh

                var mc = parent.GetComponent<MeshCollider>();
                if (mc == null) mc = parent.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = null;

                Mesh collisionMesh = new Mesh();
                collisionMesh.name = "CollisionMesh";
                collisionMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                collisionMesh.SetVertices(collisionVerts);
                collisionMesh.SetTriangles(collisionTris, 0);
                collisionMesh.RecalculateBounds();
                mc.sharedMesh = collisionMesh;
            }
        }

        private static bool ShouldRenderFace(BlockType currentBlock, BlockType neighborBlock, int faceDirection, WorldGenerator world)
        {
            if (neighborBlock == BlockType.Air)
                return true;

            if (currentBlock == BlockType.Water)
            {
                if (faceDirection == 3) return false;
                if (neighborBlock == BlockType.Water) return false;
                return true;
            }

            if (neighborBlock == BlockType.Water)
                return true;

            if (world != null && world.IsBlockOpaque(neighborBlock))
                return false;

            return true;
        }
    }
}
