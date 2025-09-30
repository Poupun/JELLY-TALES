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


        private static bool ShouldRenderFace(BlockType currentBlock, BlockType neighborBlock, int faceDirection, WorldGenerator world,
            Vector3Int currentPos = default, Vector3Int neighborPos = default)
        {
            // Always render faces against air
            if (neighborBlock == BlockType.Air)
                return true;

            // Special handling for water blocks - strategic interior face rendering
            if (currentBlock == BlockType.Water)
            {
                // NEVER render bottom faces (-Y) - causes Z-fighting with blocks below
                if (faceDirection == 3) // -Y is index 3
                    return false;

                // Don't render water faces against sand to prevent Z-fighting
                if (neighborBlock == BlockType.Sand)
                    return false;

                // ALWAYS render top faces (+Y direction) regardless of what's above
                // This is crucial for sloped water surfaces to display correctly
                if (faceDirection == 2) // +Y is index 2
                    return true;

                // Always render faces against other non-water blocks
                if (neighborBlock != BlockType.Water)
                    return true;

                // Don't render water-to-water interior faces (reduces overdraw)
                // The sloped top faces handle the visual flow representation
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

            // Separate collision mesh data (excludes water blocks)
            var collisionVerts = new List<Vector3>();
            var collisionTris = new List<int>();

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

                            // Calculate world positions for current block and neighbor
                            int currentWorldX = chunk.coord.x * chunk.sizeX + x;
                            int currentWorldY = y;
                            int currentWorldZ = chunk.coord.y * chunk.sizeZ + z;
                            Vector3Int currentWorldPos = new Vector3Int(currentWorldX, currentWorldY, currentWorldZ);

                            int neighborWorldX = chunk.coord.x * chunk.sizeX + x + dir.x;
                            int neighborWorldY = y + dir.y;
                            int neighborWorldZ = chunk.coord.y * chunk.sizeZ + z + dir.z;
                            Vector3Int neighborWorldPos = new Vector3Int(neighborWorldX, neighborWorldY, neighborWorldZ);

                            bool inside = nx >= 0 && nx < chunk.sizeX && ny >= 0 && ny < chunk.sizeY && nz >= 0 && nz < chunk.sizeZ;
                            if (inside)
                            {
                                neighbor = chunk.GetLocal(nx, ny, nz);
                            }
                            else if (world != null)
                            {
                                if (neighborWorldY >= 0 && neighborWorldY < world.worldHeight)
                                {
                                    neighbor = world.GetBlockType(neighborWorldPos);
                                }
                            }

                            // Enhanced face culling logic for different block types
                            bool shouldRenderFace = ShouldRenderFace(t, neighbor, d, world, currentWorldPos, neighborWorldPos);


                            // Remove interior face rendering - use alpha-based depth instead
                            // Water depth effect is now achieved through material transparency layering

                            if (!shouldRenderFace) continue;

                            var f = FaceVerts[d];
                            int vi = verts.Count;

                            // For water blocks, we need to pass world position to shader for seamless animation
                            if (t == BlockType.Water)
                            {
                                // Use already calculated world position
                                int worldX = currentWorldX;
                                int worldY = currentWorldY;
                                int worldZ = currentWorldZ;

                                // Get water flow system for level-based heights
                                var waterFlow = world.GetComponent<WaterFlowSystem>();

                                // Helper function to get water height at a position
                                System.Func<Vector3Int, float> GetWaterHeightAt = (Vector3Int pos) =>
                                {
                                    BlockType blockType = world.GetBlockType(pos);
                                    if (blockType == BlockType.Water && waterFlow != null)
                                    {
                                        byte level = waterFlow.GetWaterLevel(pos);
                                        return 1f - (level / 9f);
                                    }
                                    return blockType == BlockType.Air ? 0f : 1f;
                                };

                                // Calculate vertex heights based on water levels
                                Vector3[] adjustedVerts = new Vector3[4];
                                bool isTopFace = (d == 2); // +Y is top face (index 2 in Directions)
                                bool isSideFace = (d == 0 || d == 1 || d == 4 || d == 5); // Horizontal faces

                                if (isTopFace && waterFlow != null)
                                {
                                    Vector3Int waterPos = currentWorldPos;
                                    byte centerLevel = waterFlow.GetWaterLevel(waterPos);

                                    // Minecraft water height formula: full height at level 0, decreasing by 1/9th per level
                                    float centerHeight = 1f - (centerLevel / 9f);

                                    // Get heights of the 4 corners based on neighbor levels
                                    // Corner 0: (0, 1, 1) - southwest top
                                    // Corner 1: (1, 1, 1) - southeast top
                                    // Corner 2: (1, 1, 0) - northeast top
                                    // Corner 3: (0, 1, 0) - northwest top

                                    float[] cornerHeights = new float[4];
                                    Vector3Int[] cornerOffsets = new Vector3Int[]
                                    {
                                        new Vector3Int(-1, 0, 1),  // SW: check west and south
                                        new Vector3Int(1, 0, 1),   // SE: check east and south
                                        new Vector3Int(1, 0, -1),  // NE: check east and north
                                        new Vector3Int(-1, 0, -1)  // NW: check west and north
                                    };

                                    for (int i = 0; i < 4; i++)
                                    {
                                        // Minecraft algorithm: average the heights of the 4 blocks surrounding this corner
                                        // For corner at (x, z), we check blocks at: current, +x, +z, and diagonal (+x,+z)
                                        Vector3Int offset = cornerOffsets[i];

                                        // The 4 blocks that touch this corner
                                        Vector3Int[] blocksAtCorner = new Vector3Int[]
                                        {
                                            waterPos,                                    // Current block (center)
                                            waterPos + new Vector3Int(offset.x, 0, 0),  // Adjacent X
                                            waterPos + new Vector3Int(0, 0, offset.z),  // Adjacent Z
                                            waterPos + offset                            // Diagonal
                                        };

                                        float totalHeight = 0f;
                                        int validCount = 0;
                                        bool hasAir = false;

                                        // Check all 4 blocks at this corner
                                        foreach (var blockPos in blocksAtCorner)
                                        {
                                            BlockType blockType = world.GetBlockType(blockPos);

                                            if (blockType == BlockType.Water)
                                            {
                                                byte level = waterFlow.GetWaterLevel(blockPos);
                                                float height = 1f - (level / 9f);
                                                totalHeight += height;
                                                validCount++;
                                            }
                                            else if (blockType == BlockType.Air)
                                            {
                                                // Air contributes height 0
                                                hasAir = true;
                                                totalHeight += 0f;
                                                validCount++;
                                            }
                                            // Solid blocks don't contribute to corner height
                                        }

                                        // Average the heights of all contributing blocks
                                        if (validCount > 0)
                                        {
                                            cornerHeights[i] = totalHeight / validCount;

                                            // If there's air at this corner, slightly lower it for better visual flow
                                            if (hasAir)
                                            {
                                                cornerHeights[i] *= 0.9f;
                                            }
                                        }
                                        else
                                        {
                                            // No valid blocks, use center height
                                            cornerHeights[i] = centerHeight;
                                        }
                                    }

                                    // Apply corner heights to vertices
                                    // FaceVerts[2] (+Y top): {(0,1,1), (1,1,1), (1,1,0), (0,1,0)}
                                    adjustedVerts[0] = basePos + new Vector3(f[0].x, cornerHeights[0], f[0].z);
                                    adjustedVerts[1] = basePos + new Vector3(f[1].x, cornerHeights[1], f[1].z);
                                    adjustedVerts[2] = basePos + new Vector3(f[2].x, cornerHeights[2], f[2].z);
                                    adjustedVerts[3] = basePos + new Vector3(f[3].x, cornerHeights[3], f[3].z);
                                }
                                else if (isSideFace && waterFlow != null)
                                {
                                    // Side faces - adjust top vertices to match water surface slopes
                                    // Side face vertices: [bottom-left, top-left, top-right, bottom-right]
                                    // Top vertices (index 1 and 2) need height adjustment

                                    for (int i = 0; i < 4; i++)
                                    {
                                        Vector3 vert = f[i];

                                        // Only adjust top vertices (y = 1)
                                        if (vert.y == 1f)
                                        {
                                            // Calculate which corner this vertex is at
                                            int xOff = Mathf.RoundToInt(vert.x) == 0 ? -1 : 1;
                                            int zOff = Mathf.RoundToInt(vert.z) == 0 ? -1 : 1;

                                            // The 4 blocks that touch this corner (same as top face calculation)
                                            Vector3Int[] blocksAtCorner = new Vector3Int[]
                                            {
                                                currentWorldPos,                                      // Current block
                                                currentWorldPos + new Vector3Int(xOff, 0, 0),        // Adjacent X
                                                currentWorldPos + new Vector3Int(0, 0, zOff),        // Adjacent Z
                                                currentWorldPos + new Vector3Int(xOff, 0, zOff)      // Diagonal
                                            };

                                            float totalHeight = 0f;
                                            int validCount = 0;
                                            bool hasAir = false;

                                            // Check all 4 blocks at this corner
                                            foreach (var blockPos in blocksAtCorner)
                                            {
                                                BlockType blockType = world.GetBlockType(blockPos);

                                                if (blockType == BlockType.Water)
                                                {
                                                    byte level = waterFlow.GetWaterLevel(blockPos);
                                                    float height = 1f - (level / 9f);
                                                    totalHeight += height;
                                                    validCount++;
                                                }
                                                else if (blockType == BlockType.Air)
                                                {
                                                    hasAir = true;
                                                    totalHeight += 0f;
                                                    validCount++;
                                                }
                                            }

                                            float cornerHeight;
                                            if (validCount > 0)
                                            {
                                                cornerHeight = totalHeight / validCount;
                                                if (hasAir)
                                                {
                                                    cornerHeight *= 0.9f;
                                                }
                                            }
                                            else
                                            {
                                                byte currentLevel = waterFlow.GetWaterLevel(currentWorldPos);
                                                cornerHeight = 1f - (currentLevel / 9f);
                                            }

                                            adjustedVerts[i] = basePos + new Vector3(vert.x, cornerHeight, vert.z);
                                        }
                                        else
                                        {
                                            // Bottom vertices stay at y=0
                                            adjustedVerts[i] = basePos + vert;
                                        }
                                    }
                                }
                                else
                                {
                                    // Bottom face or other - no adjustment
                                    adjustedVerts[0] = basePos + f[0];
                                    adjustedVerts[1] = basePos + f[1];
                                    adjustedVerts[2] = basePos + f[2];
                                    adjustedVerts[3] = basePos + f[3];
                                }

                                // Add adjusted vertices
                                verts.Add(adjustedVerts[0]);
                                verts.Add(adjustedVerts[1]);
                                verts.Add(adjustedVerts[2]);
                                verts.Add(adjustedVerts[3]);

                                // Normal is dir
                                var n = (Vector3)dir;
                                norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);

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

                            // For water blocks, encode water level in red channel for shader
                            // For other blocks, use shading color
                            Color c;
                            if (t == BlockType.Water)
                            {
                                var waterFlow = world?.GetComponent<WaterFlowSystem>();
                                byte waterLevel = waterFlow != null ? waterFlow.GetWaterLevel(currentWorldPos) : (byte)0;
                                float levelNormalized = waterLevel / 7f; // Normalize to 0-1 range
                                // Store level in red, keep shading in green/blue for lighting
                                c = new Color(levelNormalized, shade, shade, 1f);
                            }
                            else
                            {
                                c = new Color(shade, shade, shade, 1f);
                            }
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

                            // Add to collision mesh ONLY if NOT water
                            if (t != BlockType.Water)
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
                // Expand bounds significantly to prevent aggressive frustum culling
                // This ensures water side faces remain visible from all angles and distances
                var bounds = mesh.bounds;
                bounds.Expand(100f); // Expand by 100 units - water needs very large bounds for distant visibility
                mesh.bounds = bounds;

            }

            if (addCollider)
            {
                var mc = parent.GetComponent<MeshCollider>();
                if (mc == null) mc = parent.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = null; // force refresh

                // Create separate collision mesh that EXCLUDES water blocks
                if (collisionVerts.Count > 0 && collisionTris.Count > 0)
                {
                    Mesh collisionMesh = new Mesh();
                    collisionMesh.name = "CollisionMesh_NoWater";
                    collisionMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    collisionMesh.SetVertices(collisionVerts);
                    collisionMesh.SetTriangles(collisionTris, 0);
                    collisionMesh.RecalculateBounds();
                    mc.sharedMesh = collisionMesh;
                }
                else
                {
                    // No solid blocks in this chunk (only water/air), no collision
                    mc.sharedMesh = null;
                }
            }
        }


    }
}
