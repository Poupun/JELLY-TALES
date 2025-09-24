using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Builds a single mesh per chunk.
    /// Submeshes are grouped by the actual Material used per-face, allowing
    /// per-face textures (e.g., Grass: top=grass, sides=grass_side, bottom=dirt).
    /// </summary>
    public static class ChunkMeshBuilder
    {
        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.right, Vector3Int.left,
            Vector3Int.up, Vector3Int.down,
            Vector3Int.forward, Vector3Int.back
        };

        // For each dir, vertices of a unit quad at origin facing that dir
        private static readonly Vector3[][] FaceVerts =
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

        private static readonly Vector2[] QuadUV =
        {
            new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0)
        };


        private static bool ShouldRenderFace(BlockType currentBlock, BlockType neighborBlock, int faceDirection, WorldGenerator world)
        {
            // Always render faces against air
            if (neighborBlock == BlockType.Air)
                return true;

            // Special handling for water blocks - strategic interior face rendering
            if (currentBlock == BlockType.Water)
            {
                // Don't render water faces against sand to prevent Z-fighting
                if (neighborBlock == BlockType.Sand)
                    return false;

                // Always render faces against other non-water blocks
                if (neighborBlock != BlockType.Water)
                    return true;

                // For water-to-water faces, selectively render to show depth layers
                // This will be handled in the main mesh building loop
                return false;
            }

            // For solid blocks: always render faces against water (for underwater visibility)
            if (neighborBlock == BlockType.Water)
                return true;

            // Default behavior: don't render faces between opaque blocks
            if (world != null && world.IsBlockOpaque(neighborBlock))
                return false;

            // Render faces against non-opaque blocks (like leaves)
            return true;
        }

        private static Dictionary<int, Material> _waterDepthMaterials = new Dictionary<int, Material>();

        private static Material GetWaterFogMaterial(WorldGenerator world, float depthFromSurface)
        {
            // Create depth-based material categories for fog effect
            int depthCategory = Mathf.FloorToInt(depthFromSurface / 3f); // Every 3 blocks = new depth category
            depthCategory = Mathf.Clamp(depthCategory, 0, 6); // Limit to 7 categories (0-18 blocks)

            // Check if we already have a material for this depth
            if (_waterDepthMaterials.TryGetValue(depthCategory, out Material cachedMaterial))
            {
                return cachedMaterial;
            }

            // Create new depth-specific material
            Material baseMaterial = world.GetBlockMaterial(BlockType.Water);
            if (baseMaterial == null) return null;

            // Create a new material instance for this depth
            Material depthMaterial = new Material(baseMaterial);
            depthMaterial.name = $"WaterFog_Depth_{depthCategory}";

            // Use emission to simulate depth fog - deeper = less emission = darker
            Color baseColor = baseMaterial.color;
            float fogIntensity = Mathf.Clamp01(1f - (depthCategory * 0.35f)); // Reduce brightness by 35% per category

            // Create depth fog color
            Color fogColor = new Color(
                baseColor.r * fogIntensity,
                baseColor.g * fogIntensity,
                baseColor.b * Mathf.Clamp01(fogIntensity + 0.2f), // Keep some blue
                baseColor.a
            );

            // Apply fog via emission (this affects brightness)
            Color emissionColor = fogColor * 0.3f; // Emission intensity
            depthMaterial.SetColor("_EmissionColor", emissionColor);
            depthMaterial.EnableKeyword("_EMISSION");

            // Also adjust base color
            depthMaterial.color = fogColor;
            depthMaterial.SetColor("_BaseColor", fogColor);

            // Cache the material for reuse
            _waterDepthMaterials[depthCategory] = depthMaterial;

            Debug.Log($"Created water fog material: depth {depthFromSurface:F1} -> category {depthCategory}, fog {fogIntensity:F2}, emission {emissionColor}");

            return depthMaterial;
        }

        private static Material GetWaterMaterialForDepth(WorldGenerator world, float depthFromSurface)
        {
            // Create depth-based material categories
            int depthCategory = Mathf.FloorToInt(depthFromSurface / 5f); // Every 5 blocks = new depth category
            depthCategory = Mathf.Clamp(depthCategory, 0, 4); // Limit to 5 categories (0-20 blocks)

            // Check if we already have a material for this depth
            if (_waterDepthMaterials.TryGetValue(depthCategory, out Material cachedMaterial))
            {
                return cachedMaterial;
            }

            // Create new depth-specific material
            Material baseMaterial = world.GetBlockMaterial(BlockType.Water);
            if (baseMaterial == null) return null;

            // Create a new material instance for this depth
            Material depthMaterial = new Material(baseMaterial);
            depthMaterial.name = $"Water_Depth_{depthCategory}";

            // Adjust color and transparency based on depth category
            Color baseColor = baseMaterial.color;
            float depthDarkening = 1f - (depthCategory * 0.25f); // 25% darker per category
            float depthTransparency = Mathf.Clamp01(baseColor.a + (depthCategory * 0.1f)); // More opaque with depth

            Color depthColor = new Color(
                baseColor.r * depthDarkening * 0.3f, // Reduce red significantly
                baseColor.g * depthDarkening * 0.6f, // Reduce green moderately
                baseColor.b * Mathf.Clamp01(1f - depthCategory * 0.1f), // Keep blue, slight reduction
                depthTransparency
            );

            depthMaterial.color = depthColor;
            depthMaterial.SetColor("_BaseColor", depthColor);

            // Cache the material for reuse
            _waterDepthMaterials[depthCategory] = depthMaterial;

            return depthMaterial;
        }

        private static float CalculateDistanceFromShore(WorldGenerator world, int worldX, int worldZ, int waterSurfaceLevel)
        {
            if (world == null) return 0f;

            // Simple approximation: check nearby blocks for non-water blocks
            int checkRadius = 8; // Check 8 blocks in each direction
            int nonWaterCount = 0;
            int totalChecked = 0;

            for (int dx = -checkRadius; dx <= checkRadius; dx += 2) // Skip every other block for performance
            {
                for (int dz = -checkRadius; dz <= checkRadius; dz += 2)
                {
                    int checkX = worldX + dx;
                    int checkZ = worldZ + dz;

                    // Check if this position has land above water level
                    BlockType surfaceBlock = world.GetBlockType(new Vector3Int(checkX, waterSurfaceLevel + 1, checkZ));
                    BlockType atWaterLevel = world.GetBlockType(new Vector3Int(checkX, waterSurfaceLevel, checkZ));

                    if (surfaceBlock != BlockType.Air || atWaterLevel != BlockType.Water)
                    {
                        nonWaterCount++;
                    }
                    totalChecked++;
                }
            }

            // Return proportion of non-water blocks nearby (0 = deep ocean, 1 = near shore)
            return totalChecked > 0 ? 1f - ((float)nonWaterCount / totalChecked) : 0f;
        }

        private static float CalculateWaterDepth(WorldGenerator world, int worldX, int waterY, int worldZ)
        {
            if (world == null) return 0f;

            // Find the water surface (highest water block at this x,z position)
            int waterSurface = waterY;
            for (int y = waterY + 1; y < world.worldHeight; y++)
            {
                BlockType blockAbove = world.GetBlockType(new Vector3Int(worldX, y, worldZ));
                if (blockAbove == BlockType.Water)
                    waterSurface = y;
                else
                    break; // Hit air or solid block
            }

            // Depth is distance from surface to current water block
            return Mathf.Max(0f, waterSurface - waterY);
        }

        public static void BuildMesh(WorldGenerator world, Chunk chunk, bool addCollider)
        {
            if (world == null || chunk == null) return;
            var parent = chunk.parent;
            if (parent == null) return;

            var mf = parent.GetComponent<MeshFilter>();
            if (mf == null) mf = parent.gameObject.AddComponent<MeshFilter>();
            var mr = parent.GetComponent<MeshRenderer>();
            if (mr == null) mr = parent.gameObject.AddComponent<MeshRenderer>();

            // Triangles grouped by material used for each face
            var trisByMaterial = new Dictionary<Material, List<int>>();
            var verts = new List<Vector3>(chunk.sizeX * chunk.sizeY * chunk.sizeZ);
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>(); // vertex color for fake lighting / variation

            // Helper to get/create tri list for a material
            List<int> GetList(Material m)
            {
                if (!trisByMaterial.TryGetValue(m, out var list))
                {
                    list = new List<int>(1024);
                    trisByMaterial[m] = list;
                }
                return list;
            }

            // Iterate all blocks and emit faces against air
            for (int x = 0; x < chunk.sizeX; x++)
            {
                for (int y = 0; y < chunk.sizeY; y++)
                {
                    for (int z = 0; z < chunk.sizeZ; z++)
                    {
                        var t = chunk.GetLocal(x, y, z);
                        if (t == BlockType.Air) continue;


                        // Remove LOD filtering to restore original functionality

                        // Local pos of this block's origin (mesh is in chunk parent's local space)
                        Vector3 basePos = new Vector3(x, y, z);

                        for (int d = 0; d < 6; d++)
                        {
                            var dir = Directions[d];
                            int nx = x + dir.x, ny = y + dir.y, nz = z + dir.z;
                            BlockType neighbor = BlockType.Air;
                            bool inside = nx >= 0 && nx < chunk.sizeX && ny >= 0 && ny < chunk.sizeY && nz >= 0 && nz < chunk.sizeZ;
                            if (inside)
                            {
                                neighbor = chunk.GetLocal(nx, ny, nz);
                            }
                            else if (world != null)
                            {
                                // Fix coordinate calculation for chunk boundaries
                                int worldX = chunk.coord.x * chunk.sizeX + x + dir.x;
                                int worldY = y + dir.y;
                                int worldZ = chunk.coord.y * chunk.sizeZ + z + dir.z;
                                if (worldY >= 0 && worldY < world.worldHeight)
                                {
                                    neighbor = world.GetBlockType(new Vector3Int(worldX, worldY, worldZ));

                                }
                            }
                            // Enhanced face culling logic for different block types
                            bool shouldRenderFace = ShouldRenderFace(t, neighbor, d, world);


                            // Remove interior face rendering - use alpha-based depth instead
                            // Water depth effect is now achieved through material transparency layering

                            if (!shouldRenderFace) continue;

                            var f = FaceVerts[d];
                            int vi = verts.Count;

                            // For water blocks, we need to pass world position to shader for seamless animation
                            if (t == BlockType.Water)
                            {
                                // Calculate world position for each vertex
                                int worldX = chunk.coord.x * chunk.sizeX + x;
                                int worldZ = chunk.coord.y * chunk.sizeZ + z;

                                // Add vertices with world-space information
                                verts.Add(basePos + f[0]);
                                verts.Add(basePos + f[1]);
                                verts.Add(basePos + f[2]);
                                verts.Add(basePos + f[3]);

                                // Normal is dir
                                var n = (Vector3)dir;
                                norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);

                                // Pass world coordinates through UV2 channel for shader world-space calculations
                                var worldPosUV0 = new Vector2(worldX + f[0].x, worldZ + f[0].z);
                                var worldPosUV1 = new Vector2(worldX + f[1].x, worldZ + f[1].z);
                                var worldPosUV2 = new Vector2(worldX + f[2].x, worldZ + f[2].z);
                                var worldPosUV3 = new Vector2(worldX + f[3].x, worldZ + f[3].z);

                                // Regular UVs for texture mapping
                                uvs.Add(QuadUV[0]); uvs.Add(QuadUV[1]); uvs.Add(QuadUV[2]); uvs.Add(QuadUV[3]);
                            }
                            else
                            {
                                // Simple vertex placement for non-water blocks
                                verts.Add(basePos + f[0]);
                                verts.Add(basePos + f[1]);
                                verts.Add(basePos + f[2]);
                                verts.Add(basePos + f[3]);

                                // Normal is dir
                                var n = (Vector3)dir;
                                norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);

                                // Simple UVs
                                uvs.Add(QuadUV[0]); uvs.Add(QuadUV[1]); uvs.Add(QuadUV[2]); uvs.Add(QuadUV[3]);
                            }

                            // --- Fake face lighting + subtle per-block variation ---
                            // Minecraft-like depth: darken certain faces & bottom, lighten top.
                            float shade = 1f;
                            if (world != null && world.enableFaceShading)
                            {
                                // Direction order matches Directions array
                                switch (d)
                                {
                                    case 2: shade = 1.00f; break; // +Y top brightest
                                    case 3: shade = world.bottomShade; break; // -Y bottom darkest
                                    case 0: // +X
                                    case 1: // -X
                                        shade = world.eastWestShade; break;
                                    case 4: // +Z (forward)
                                    case 5: // -Z (back)
                                        shade = world.northSouthShade; break;
                                }

                                // Subtle per-block random variation to break tiling
                                if (world.variationStrength > 0f)
                                {
                                    int worldX = chunk.coord.x * chunk.sizeX + x;
                                    int worldY = y;
                                    int worldZ = chunk.coord.y * chunk.sizeZ + z;
                                    float h = WorldGenerator.Hash(worldX, worldY, worldZ); // 0..1
                                    float v = (h - 0.5f) * 2f * world.variationStrength; // -var..+var
                                    shade = Mathf.Clamp01(shade * (1f + v));
                                }
                            }

                            var c = new Color(shade, shade, shade, 1f);
                            colors.Add(c); colors.Add(c); colors.Add(c); colors.Add(c);

                            // Simple material selection
                            Material faceMat = null;
                            if (world != null)
                            {
                                faceMat = world.GetFaceMaterial(t, d);
                                if (faceMat == null)
                                {
                                    faceMat = world.GetBlockMaterial(t);
                                }
                            }
                            if (faceMat == null) continue; // skip if no material configured

                            var tri = GetList(faceMat);
                            tri.Add(vi + 0); tri.Add(vi + 1); tri.Add(vi + 2);
                            tri.Add(vi + 0); tri.Add(vi + 2); tri.Add(vi + 3);
                        }
                    }
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            if (colors.Count == verts.Count) mesh.SetColors(colors);
            // Stable ordering by material name (fallback to instanceID)
            var materials = new List<Material>(trisByMaterial.Keys);
            materials.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return -1;
                if (b == null) return 1;
                int byName = string.Compare(a.name, b.name, System.StringComparison.Ordinal);
                if (byName != 0) return byName;
                return a.GetInstanceID().CompareTo(b.GetInstanceID());
            });
            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++)
            {
                mesh.SetTriangles(trisByMaterial[materials[i]], i);
            }
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
            mr.sharedMaterials = materials.ToArray();

            // Check if this chunk contains water and disable shadows + fix culling issues
            bool hasWater = materials.Any(m => m != null && m.name.Contains("Water"));
            if (hasWater)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // Fix potential culling issues with transparent water
                // Expand bounds slightly to prevent aggressive frustum culling
                var bounds = mesh.bounds;
                bounds.Expand(2f); // Expand by 2 units in all directions
                mesh.bounds = bounds;

            }

            if (addCollider)
            {
                var mc = parent.GetComponent<MeshCollider>();
                if (mc == null) mc = parent.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = null; // force refresh
                mc.sharedMesh = mesh;
            }
        }


    }
}
