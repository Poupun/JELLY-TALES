using System.Collections.Generic;
using UnityEngine;
using WorldGeneration.Chunks;

/// <summary>
/// Creates a unified water surface that spans across all chunks, eliminating chunk boundary separations
/// in water animation. This system detects water surface blocks and creates a continuous mesh.
/// </summary>
public class UnifiedWaterSurface : MonoBehaviour
{
    [Header("Water Surface Settings")]
    [Tooltip("Enable unified water surface rendering")]
    public bool enableUnifiedSurface = true;

    [Header("Animation Settings")]
    [Tooltip("Wave height intensity")]
    [Range(0f, 0.5f)] public float waveAmplitude = 0.1f;
    [Tooltip("Wave animation speed")]
    [Range(0.1f, 5f)] public float waveSpeed = 1.5f;
    [Tooltip("Wave spatial frequency")]
    [Range(0.1f, 3f)] public float waveScale = 0.8f;
    [Tooltip("Primary wave direction")]
    public Vector2 waveDirection = new Vector2(1f, 0.3f);

    [Header("Performance")]
    [Tooltip("Update frequency for water surface detection")]
    [Range(0.1f, 5f)] public float updateInterval = 0.5f; // More frequent updates
    [Tooltip("Maximum water surface blocks to process per frame")]
    [Range(100, 5000)] public int maxBlocksPerFrame = 2000; // Higher limit

    private WorldGenerator worldGenerator;
    private GameObject waterSurfaceRoot;
    private MeshFilter waterMeshFilter;
    private MeshRenderer waterMeshRenderer;
    private Material waterSurfaceMaterial;

    private HashSet<Vector3Int> waterSurfaceBlocks = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> previousWaterBlocks = new HashSet<Vector3Int>();
    private float lastUpdateTime = 0f;
    private bool needsMeshRebuild = true;

    void Start()
    {
        worldGenerator = FindFirstObjectByType<WorldGenerator>();
        if (worldGenerator == null)
        {
            Debug.LogError("UnifiedWaterSurface: WorldGenerator not found!");
            enabled = false;
            return;
        }

        // UnifiedWaterSurface is deprecated - using world-space shader approach instead
        Debug.Log("UnifiedWaterSurface: System deprecated - using world-space shader approach instead");
        enabled = false;
        return;

        InitializeWaterSurface();

        // Initial detection
        DetectWaterSurface();
    }

    void Update()
    {
        if (!enableUnifiedSurface) return;

        // Update water surface detection periodically
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            DetectWaterSurface();
            lastUpdateTime = Time.time;
        }

        // Rebuild mesh if needed
        if (needsMeshRebuild)
        {
            RebuildWaterSurfaceMesh();
            needsMeshRebuild = false;
        }

        // Update animation parameters
        UpdateWaterAnimation();
    }

    private void InitializeWaterSurface()
    {
        // Create water surface root object
        waterSurfaceRoot = new GameObject("UnifiedWaterSurface");
        waterSurfaceRoot.transform.SetParent(transform);

        // Add mesh components
        waterMeshFilter = waterSurfaceRoot.AddComponent<MeshFilter>();
        waterMeshRenderer = waterSurfaceRoot.AddComponent<MeshRenderer>();

        // Create water surface material
        CreateWaterSurfaceMaterial();

        // Set rendering properties
        waterMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        waterMeshRenderer.receiveShadows = false;

        // Add collider for water surface
        var meshCollider = waterSurfaceRoot.AddComponent<MeshCollider>();
        meshCollider.convex = false; // Allow complex mesh collision

        Debug.Log("UnifiedWaterSurface: Initialized water surface system");
    }

    private void CreateWaterSurfaceMaterial()
    {
        // Get the base water material from WorldGenerator
        Material baseWaterMaterial = worldGenerator.GetBlockMaterial(BlockType.Water);
        if (baseWaterMaterial == null)
        {
            Debug.LogWarning("UnifiedWaterSurface: No base water material found, creating default");
            waterSurfaceMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            waterSurfaceMaterial.color = new Color(0.2f, 0.6f, 1f, 0.7f);
        }
        else
        {
            // Create a copy of the base material
            waterSurfaceMaterial = new Material(baseWaterMaterial);
        }

        waterSurfaceMaterial.name = "UnifiedWaterSurface_Material";

        // Try to use wind/wave shader for animation
        var waveShader = Shader.Find("Custom/WaterWaves");
        if (waveShader == null) waveShader = Shader.Find("Custom/LeavesWind");

        if (waveShader != null && waveShader.isSupported)
        {
            waterSurfaceMaterial.shader = waveShader;
            Debug.Log("UnifiedWaterSurface: Using wave animation shader");
        }
        else
        {
            Debug.LogWarning("UnifiedWaterSurface: No wave shader found, using standard shader");
        }

        waterMeshRenderer.material = waterSurfaceMaterial;

        // Register with WaterAnimator if it exists
        WaterAnimator.RegisterWaterMaterial(waterSurfaceMaterial);
    }

    private void DetectWaterSurface()
    {
        if (worldGenerator == null) return;

        waterSurfaceBlocks.Clear();
        int blocksProcessed = 0;

        // Get player position for centered detection
        Vector3 playerPos = worldGenerator.player != null ?
            worldGenerator.player.position : transform.position;

        // Calculate detection range based on view distance
        int detectionRange = worldGenerator.viewDistanceChunks * worldGenerator.chunkSizeX;
        int minX = Mathf.FloorToInt(playerPos.x) - detectionRange;
        int maxX = Mathf.FloorToInt(playerPos.x) + detectionRange;
        int minZ = Mathf.FloorToInt(playerPos.z) - detectionRange;
        int maxZ = Mathf.FloorToInt(playerPos.z) + detectionRange;

        // Scan for water surface blocks
        for (int x = minX; x <= maxX && blocksProcessed < maxBlocksPerFrame; x += 4) // Sample every 4th block for performance
        {
            for (int z = minZ; z <= maxZ && blocksProcessed < maxBlocksPerFrame; z += 4)
            {
                blocksProcessed++;

                // Find water surface at this XZ coordinate
                Vector3Int surfacePos = FindWaterSurfaceAt(x, z);
                if (surfacePos != Vector3Int.zero) // zero means no water surface found
                {
                    // Add a 4x4 area around the detected surface for better coverage
                    for (int dx = 0; dx < 4; dx++)
                    {
                        for (int dz = 0; dz < 4; dz++)
                        {
                            Vector3Int detailedPos = FindWaterSurfaceAt(x + dx, z + dz);
                            if (detailedPos != Vector3Int.zero)
                            {
                                waterSurfaceBlocks.Add(detailedPos);
                            }
                        }
                    }
                }
            }
        }

        // Check if water surface changed
        if (!waterSurfaceBlocks.SetEquals(previousWaterBlocks))
        {
            needsMeshRebuild = true;
            previousWaterBlocks = new HashSet<Vector3Int>(waterSurfaceBlocks);

            Debug.Log($"UnifiedWaterSurface: Detected {waterSurfaceBlocks.Count} water surface blocks");
        }
    }

    private Vector3Int FindWaterSurfaceAt(int x, int z)
    {
        // Start from a reasonable height and work down to find the highest water block
        int searchStartY = Mathf.Min(worldGenerator.worldHeight - 1, 80); // Start from reasonable height

        for (int y = searchStartY; y >= 0; y--)
        {
            Vector3Int pos = new Vector3Int(x, y, z);
            BlockType blockType = worldGenerator.GetBlockType(pos);

            if (blockType == BlockType.Water)
            {
                // Check if this is a surface block (air or nothing above)
                Vector3Int abovePos = new Vector3Int(x, y + 1, z);
                BlockType aboveBlock = y >= worldGenerator.worldHeight - 1 ? BlockType.Air : worldGenerator.GetBlockType(abovePos);

                if (aboveBlock == BlockType.Air)
                {
                    return pos; // This is a water surface block
                }
            }
        }

        return Vector3Int.zero; // No water surface found at this position
    }

    private void RebuildWaterSurfaceMesh()
    {
        if (waterSurfaceBlocks.Count == 0)
        {
            // Clear mesh if no water surface
            waterMeshFilter.mesh = null;
            return;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        // Create quads for each water surface block
        foreach (Vector3Int blockPos in waterSurfaceBlocks)
        {
            AddWaterSurfaceQuad(blockPos, vertices, normals, uvs, triangles);
        }

        // Create and assign mesh
        Mesh waterMesh = new Mesh();
        waterMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        waterMesh.name = "UnifiedWaterSurface";

        waterMesh.SetVertices(vertices);
        waterMesh.SetNormals(normals);
        waterMesh.SetUVs(0, uvs);
        waterMesh.SetTriangles(triangles, 0);

        waterMesh.RecalculateBounds();

        waterMeshFilter.mesh = waterMesh;

        // Update collision mesh
        var meshCollider = waterSurfaceRoot.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = waterMesh;
        }

        Debug.Log($"UnifiedWaterSurface: Built mesh with {vertices.Count} vertices for {waterSurfaceBlocks.Count} water blocks");
    }

    private void AddWaterSurfaceQuad(Vector3Int blockPos, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
    {
        // Create a slightly elevated quad above the water block to avoid z-fighting
        float surfaceOffset = 0.02f; // Increased offset to ensure visibility
        Vector3 basePos = new Vector3(blockPos.x, blockPos.y + 1 + surfaceOffset, blockPos.z);

        // Define quad vertices (top face of water block, slightly elevated)
        Vector3[] quadVerts = {
            basePos + new Vector3(0, 0, 1), // 0: front-left
            basePos + new Vector3(1, 0, 1), // 1: front-right
            basePos + new Vector3(1, 0, 0), // 2: back-right
            basePos + new Vector3(0, 0, 0)  // 3: back-left
        };

        int startIndex = vertices.Count;

        // Add vertices
        vertices.AddRange(quadVerts);

        // Add normals (all pointing up)
        Vector3 normal = Vector3.up;
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);
        normals.Add(normal);

        // Add UVs
        uvs.Add(new Vector2(0, 1)); // front-left
        uvs.Add(new Vector2(1, 1)); // front-right
        uvs.Add(new Vector2(1, 0)); // back-right
        uvs.Add(new Vector2(0, 0)); // back-left

        // Add triangles (two triangles forming a quad)
        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 1);
        triangles.Add(startIndex + 2);

        triangles.Add(startIndex + 0);
        triangles.Add(startIndex + 2);
        triangles.Add(startIndex + 3);
    }

    private void UpdateWaterAnimation()
    {
        if (waterSurfaceMaterial == null) return;

        // Update animation parameters similar to WorldGenerator's water animation
        bool hasWindShader = waterSurfaceMaterial.shader != null &&
                            (waterSurfaceMaterial.shader.name.Contains("Wind") ||
                             waterSurfaceMaterial.shader.name.Contains("Wave"));

        if (hasWindShader)
        {
            // Update wave parameters
            if (waterSurfaceMaterial.HasProperty("_WindAmp"))
                waterSurfaceMaterial.SetFloat("_WindAmp", waveAmplitude);
            if (waterSurfaceMaterial.HasProperty("_WindSpeed"))
                waterSurfaceMaterial.SetFloat("_WindSpeed", waveSpeed);
            if (waterSurfaceMaterial.HasProperty("_WindScale"))
                waterSurfaceMaterial.SetFloat("_WindScale", waveScale);

            // Water-specific settings
            if (waterSurfaceMaterial.HasProperty("_WindVertical"))
                waterSurfaceMaterial.SetFloat("_WindVertical", 0.9f);
            if (waterSurfaceMaterial.HasProperty("_WindVar"))
                waterSurfaceMaterial.SetFloat("_WindVar", 0.1f);

            // Wave direction
            if (waterSurfaceMaterial.HasProperty("_WindDir"))
            {
                Vector2 dir = waveDirection.sqrMagnitude < 0.0001f ?
                            new Vector2(1, 0) : waveDirection.normalized;
                waterSurfaceMaterial.SetVector("_WindDir", new Vector4(dir.x, dir.y, 0, 0));
            }

            // Force world-space animation for seamless surface
            if (waterSurfaceMaterial.HasProperty("_UseWorldPos"))
                waterSurfaceMaterial.SetFloat("_UseWorldPos", 1.0f);
            if (waterSurfaceMaterial.HasProperty("_WorldSpaceUV"))
                waterSurfaceMaterial.SetFloat("_WorldSpaceUV", 1.0f);
        }
    }

    public void SetAnimationEnabled(bool enabled)
    {
        enableUnifiedSurface = enabled;
        if (waterSurfaceRoot != null)
        {
            waterSurfaceRoot.SetActive(enabled);
        }
    }

    public void ForceRebuild()
    {
        needsMeshRebuild = true;
        DetectWaterSurface();
    }

    void OnDestroy()
    {
        // Unregister material
        if (waterSurfaceMaterial != null)
        {
            WaterAnimator.UnregisterWaterMaterial(waterSurfaceMaterial);
        }
    }

    // Public getters for inspector tweaking
    public int WaterSurfaceBlockCount => waterSurfaceBlocks.Count;
    public bool IsRebuildNeeded => needsMeshRebuild;
}