using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
#if UNITY_JOBS
using Unity.Jobs;
using Unity.Collections;
#endif
// PlantDefinition and PlantDatabase are in global namespace after refactor

public class WorldGenerator : MonoBehaviour
{
    [Header("World Settings")]
    public int worldWidth = 16;
    public int worldHeight = 150;
    public int worldDepth = 16;

    [Header("World Generation")]
    [Tooltip("World seed for deterministic generation")]
    public int worldSeed = 12345;

    [Header("Cave System - NEW")]
    [Tooltip("Enable horizontal tunnel generation")]
    public bool enableTunnels = true;
    public WorldGeneration.HorizontalTunnelGenerator.TunnelSettings tunnelSettings = new WorldGeneration.HorizontalTunnelGenerator.TunnelSettings();
    
    [Header("Ore Generation System")]
    [Tooltip("Minecraft-style chunk-based ore generation settings")]
    public WorldGeneration.Chunks.ChunkOreGenerator.OreSettings oreSettings = new WorldGeneration.Chunks.ChunkOreGenerator.OreSettings();
    

    [Header("Chunk Streaming")] 
    [Tooltip("Enable player-centered chunk streaming for infinite world")]
    public bool useChunkStreaming = true;
    [Min(4)] public int chunkSizeX = 16;
    [Min(4)] public int chunkSizeZ = 16;
    [Min(1)] public int viewDistanceChunks = 8; // Manhattan or square radius (increased for better chunk preloading)
    [Tooltip("Player transform used to center chunk streaming. If null, will try to auto-find.")]
    public Transform player;
    private Transform _chunksRoot;
    private readonly Dictionary<Vector2Int, WorldGeneration.Chunks.Chunk> _chunks = new Dictionary<Vector2Int, WorldGeneration.Chunks.Chunk>();
    private readonly Queue<Vector2Int> _pendingLoads = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> _queued = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> _loading = new HashSet<Vector2Int>();
    [Min(1)] public int maxChunkLoadsPerFrame = 10; // Increased for larger view distance (8 chunks)
    [Header("Performance Optimization")]
    [Tooltip("Enable async Job System for chunk generation")]
    public bool useJobSystem = true; // Re-enabled for fast chunk generation
    [Tooltip("Frame time budget in milliseconds")]
    [Range(5f, 33f)] public float targetFrameTime = 16.67f;
    [Header("Meshing (Experimental)")]
    [Tooltip("If enabled, build one mesh per chunk instead of instantiating each block GameObject.")]
    public bool useChunkMeshing = true;
    [Tooltip("Add MeshCollider to chunk mesh for collisions.")]
    public bool addChunkCollider = true;
    [Header("Persistence")]
    [Tooltip("Save chunk edits so placed/removed blocks persist across unloads.")]
    public bool enableChunkPersistence = true;
    [Tooltip("Folder name under persistent data path to store chunk saves.")]
    public string saveFolderName = "ChunkSaves";
    [Tooltip("Current world name for save isolation - set by WorldLoader")]
    [System.NonSerialized]
    public string currentWorldName = "";
    
    [Header("Block Prefab")]
    public GameObject blockPrefab;
    
    [Header("Player Spawn Gating")]
    [Tooltip("If enabled, the player GameObject stays inactive until the first center chunk finishes building its mesh.")]
    public bool delayPlayerSpawnUntilChunks = false; // Disabled to fix spawn issues
    [Tooltip("After chunks are ready, reposition the player to stand on the highest solid block under them.")]
    public bool snapPlayerToGroundOnSpawn = false; // Disabled to avoid spawn issues
    
    [Header("Block Management System")]
    [Tooltip("Block textures and properties are now managed by BlockManager using BlockConfiguration assets. See Assets/Data/Blocks/ folder.")]
    [SerializeField] private bool _useNewBlockSystem = true; // Visual indicator in Inspector
    
    [Header("Legacy Block Textures (For Backward Compatibility)")]
    [Tooltip("These textures are kept for fallback compatibility. Use BlockConfiguration assets instead.")]
    public Texture2D grassTexture;
    public Texture2D grassSideTexture;
    public Texture2D dirtTexture;
    public Texture2D stoneTexture;
    public Texture2D sandTexture;
    public Texture2D coalTexture;
    public Texture2D logTexture;
    public Texture2D leavesTexture;
    public Texture2D woodPlanksTexture;
    public Texture2D bedrockTexture;
    public Texture2D gravelTexture;
    public Texture2D ironTexture;
    public Texture2D goldTexture;
    public Texture2D diamondTexture;
    public Texture2D craftingTableTexture;
    public Texture2D craftingTableFrontTexture;
    public Texture2D craftingTableSideTexture;

    [Header("Plants (New)")]
    [Tooltip("Scriptable Object list of plants with textures, sizes, and weights.")]
    public ScriptableObject plantDatabase;
    [Tooltip("Expected plant instances per exposed grass tile (can be fractional).")]
    [Range(0f, 3f)] public float plantDensity = 0.7f;
    // Track to detect runtime changes and rebuild plants live
    private float _lastPlantDensity = -12345.678f;

    [Header("Anti-Tiling Settings")]
    [Range(0.8f, 1.2f)]
    public float textureScaleVariation = 1.0f;
    
    [Range(0f, 0.3f)]
    public float textureOffsetVariation = 0.1f;

    [Header("Voxel Face Shading (Depth Illusion)")]
    [Tooltip("Apply simple directional face shading like Minecraft to add depth without lights.")]
    public bool enableFaceShading = true;
    [Tooltip("Brightness multiplier for east/west faces (X axis). 1 = no change.")]
    [Range(0.3f,1.2f)] public float eastWestShade = 0.9f;
    [Tooltip("Brightness multiplier for north/south faces (Z axis). 1 = no change.")]
    [Range(0.3f,1.2f)] public float northSouthShade = 0.85f;
    [Tooltip("Brightness multiplier for bottom faces (Y-).")]
    [Range(0.1f,1.0f)] public float bottomShade = 0.7f;
    [Tooltip("Random per-block shade variation strength (0 disables variation).")]
    [Range(0f,0.3f)] public float variationStrength = 0.08f;
    
    private BlockType[,,] worldData;
    private Dictionary<Vector3Int, GameObject> blockObjects = new Dictionary<Vector3Int, GameObject>();
    private Dictionary<Vector3Int, GameObject> plantObjects = new Dictionary<Vector3Int, GameObject>(); // legacy per-tile plants (non-meshing)
    private Material[] blockMaterials;
    // Cached special-case materials
    private Material _grassSideMaterial;
    private Material _grassSideOverlayMaterial; // transparent cutout overlay used atop dirt on grass sides
    private TextureVariationManager textureManager;
    // Cache for plant materials by texture so we don't create per-instance materials
    private readonly Dictionary<Texture2D, Material> _plantMaterialCache = new Dictionary<Texture2D, Material>();
    // Runtime plant defs discovered from textures (merged with PlantDatabase at runtime)
    private readonly List<object> _extraPlantDefs = new List<object>();
    // Minimal runtime plant def type to work with reflection helpers
    private class RuntimePlantDef
    {
        public Texture2D texture;
        public Vector2 heightRange = new Vector2(0.6f, 1.2f);
        public float width = 0.9f;
        public float yOffset = 0.02f;
        public float weight = 1f;
    }

    [Header("Trees")] 
    [Tooltip("Enable procedural tree generation (advanced)." )]
    public bool enableTrees = true;
    [Tooltip("Average number of trees per chunk (scaled by noise + randomness)." )]
    [Min(0f)] public float treesPerChunk = 0.15f;
    [Tooltip("Seed offset for tree placement noise.")]
    public int treeSeedOffset = 98765;
    [Header("Tree Placement")] 
    [Tooltip("World grid size for deterministic tree trunk candidates (larger = sparser, more uniform).")]
    [Range(2, 32)] public int treeGridSize = 6;
    [Header("Tree Height")] 
    [Range(4,64)] public int minTrunkHeight = 6;
    [Range(6,128)] public int maxTrunkHeight = 24;
    [Header("Canopy Shape")] 
    [Range(2,16)] public int leavesRadius = 5; // base horizontal radius at widest point
    [Range(2,24)] public int leavesDepth = 7;   // base vertical half-thickness downward from canopy top
    [Tooltip("If enabled, canopy radius/depth grow with trunk height above the minimum.")]
    public bool autoScaleCanopyWithHeight = true;
    [Tooltip("Extra canopy radius added per block of trunk height above min.")]
    [Range(0f,0.5f)] public float canopyRadiusGrowthPerBlock = 0.12f;
    [Tooltip("Extra canopy depth added per block of trunk height above min.")]
    [Range(0f,0.5f)] public float canopyDepthGrowthPerBlock = 0.10f;
    [Header("Canopy Style")] 
    [Tooltip("If true, generate near-spherical (ellipsoid) canopies with minimal randomness.")]
    public bool sphericalCanopy = false;
    [Tooltip("Remove isolated / stray leaves after generation (only in spherical mode or when desired).")]
    public bool pruneLooseLeaves = true;
    [Tooltip("Minimum number of neighboring leaf blocks (6-neighborhood) required to keep a leaf when pruning.")]
    [Range(0,6)] public int leafPruneNeighborThreshold = 2;
    [Range(0f,0.6f)] public float leavesSparsity = 0.08f;
    [Range(0f,1f)] public float canopyRoundness = 0.65f; // 0=cubic,1=spherical metric blend
    [Range(0f,1f)] public float canopyDomeBias = 0.35f;   // flatten underside (0 full sphere,1 strong dome)
    [Header("Branches")]
    [Range(0,12)] public int maxPrimaryBranches = 5;
    [Range(0,8)] public int maxSecondaryBranches = 3;
    [Range(2,12)] public int branchMinLength = 3;
    [Range(3,18)] public int branchMaxLength = 8;
    [Range(1,4)] public int branchThickness = 1;
    [Range(0f,1f)] public float branchSpawnProbability = 0.55f;
    [Range(0f,1f)] public float secondaryBranchProbability = 0.35f;
    [Range(0f,1f)] public float branchUpBias = 0.4f; // chance upward step per segment
    [Range(0f,1f)] public float canopyTrimUnderBranches = 0.35f; // trim dense leaves below center near trunk
    [Tooltip("Fraction of trunk height used to cap branch max length.")]
    [Range(0f,1f)] public float branchLengthFactor = 0.5f;
    [Tooltip("Guaranteed dense leaf core (normalized sphere distance).")]
    [Range(0f,1f)] public float canopyCoreFill = 0.4f;
    [Tooltip("Radius (blocks) of leaf puff added at each branch tip.")]
    [Range(0,6)] public int branchTipLeafRadius = 2;
    [Tooltip("Minimum normalized shell band always filled (prevents hollow canopy). 0 = off.")]
    [Range(0f,0.6f)] public float canopyShellGuarantee = 0.15f;
    [Tooltip("If true, prevent trees from spawning too close (Manhattan distance)." )]
    public bool enforceTreeSpacing = false;
    [Tooltip("Minimum Manhattan spacing between tree trunks.")]
    [Range(1,32)] public int treeSpacing = 6;
    private readonly HashSet<Vector3Int> _treeTrunkPositions = new HashSet<Vector3Int>();
    [Tooltip("If true, trees are only spawned when their full canopy fits inside the chunk to avoid cut trees at borders.")]
    public bool constrainTreesInsideChunk = false;
    // Batch flags to avoid per-cell mesh rebuilds during procedural generation
    private bool _isProceduralBatch = false;
    private readonly HashSet<Vector2Int> _batchDirtyChunks = new HashSet<Vector2Int>();
    // Safety net: deferred rebuild queue to handle cross-chunk writes even if batching overlaps
    private readonly HashSet<Vector2Int> _deferredRebuild = new HashSet<Vector2Int>();

    [Header("Leaf Wind Animation")] 
    [Tooltip("Enable subtle shader-based wind sway for leaf blocks (vertex animation).")]
    public bool enableLeafWind = true;
    [Range(0f,0.2f)] public float leafWindAmplitude = 0.05f;
    [Range(0.1f,5f)] public float leafWindSpeed = 1.2f;
    [Range(0.1f,2f)] public float leafWindScale = 0.5f; // spatial frequency influence
    [Range(0f,1f)] public float leafWindVerticalFactor = 0.3f; // proportion of motion applied vertically
    [Tooltip("Wind direction on XZ plane.")]
    public Vector2 leafWindDirection = new Vector2(1f, 0.3f);

    [Header("Leaves Rendering")]
    [Tooltip("Treat leaves as opaque for face culling to remove internal faces and stop flicker.")]
    public bool leavesOccludeFaces = false;
    [Tooltip("Alpha cutoff for leaf textures (higher = more holes).")]
    [Range(0.1f, 0.8f)] public float leavesCutoff = 0.35f;
    [Tooltip("If off, leaves will not receive shadows (often makes canopies less dark).")]
    public bool leavesReceiveShadows = true;
    [Tooltip("Filter mode for leaves texture (Point keeps crisp pixels; Bilinear smooths edges).")]
    public FilterMode leavesFilterMode = FilterMode.Point;
    [Tooltip("Mip map bias for leaves (-1 sharper / +1 blurrier if mipmaps exist). 0 keeps import setting.")]
    [Range(-1f,1f)] public float leavesMipMapBias = 0f;

    [Header("Lighting Balancer")]
    [Tooltip("Adds a subtle emission to blocks/leaves to prevent fully black faces in deep shadow.")]
    public bool enableAmbientFill = true;
    [Range(0f, 0.2f)] public float ambientFill = 0.06f;
    private bool _lastEnableAmbientFill = false;
    private float _lastAmbientFill = -1f;
    // Track leaf wind toggle to live-swap shaders safely
    private bool _lastEnableLeafWindToggle = false;

    // Small integer hash for variation (kept here for chunk mesher)
    public static float Hash(int x, int y, int z)
    {
        unchecked
        {
            int h = x * 73856093 ^ y * 19349663 ^ z * 83492791;
            h ^= (h >> 13);
            h *= 60493;
            h ^= (h >> 17);
            // Map to 0..1
            return (h & 0x7FFFFFFF) / (float)int.MaxValue;
        }
    }

    [Header("Plant Wind Animation")] 
    [Tooltip("Enable wind sway for grass/plant billboards.")]
    public bool enablePlantWind = true;
    [Range(0f,0.25f)] public float plantWindAmplitude = 0.07f;
    [Range(0.1f,5f)] public float plantWindSpeed = 1.4f;
    [Range(0.1f,3f)] public float plantWindScale = 0.8f;
    [Range(0f,1f)] public float plantWindVerticalFactor = 0.2f;
    [Tooltip("Per-plant randomization factor (0 = uniform motion).")]
    [Range(0f,1f)] public float plantWindVariation = 0.5f;

    [Header("Plant Controls")]
    [Tooltip("Weight multipliers for plant selection by texture name.")]
    [Range(0f,5f)] public float plantWeightFern = 1.0f;
    [Range(0f,5f)] public float plantWeightPlant = 1.0f;
    [Range(0f,5f)] public float plantWeightGrass = 1.5f;
    [Tooltip("Uniform size multipliers applied to height and width per plant kind.")]
    [Range(0.5f,2f)] public float plantSizeFern = 1.0f;
    [Range(0.5f,2f)] public float plantSizePlant = 1.0f;
    [Range(0.5f,2f)] public float plantSizeGrass = 1.0f;
    private float _lastPlantWeightFern = -1f, _lastPlantWeightPlant = -1f, _lastPlantWeightGrass = -1f;
    private float _lastPlantSizeFern = -1f, _lastPlantSizePlant = -1f, _lastPlantSizeGrass = -1f;

    [Header("Debug / Reload")] 
    [Tooltip("If true, a reload will discard persisted chunk edits (fresh world)." )]
    public bool clearSavedEditsOnReload = false;
    [Tooltip("If true when reloading, a new random seed will be chosen.")]
    public bool reseedOnReload = false;
    [Tooltip("Helper flag you can tick in play mode to trigger a reload; auto-resets.")]
    public bool triggerReloadInPlay = false;
    
    // Tick system for delayed plant behavior
    [Header("Tick Settings")]
    [Tooltip("Seconds per game tick (Minecraft-like pacing)")]
    public float tickIntervalSeconds = 0.5f;
    [Tooltip("Ticks before plants reappear after being broken")] public int plantRespawnDelayTicks = 5;
    [Tooltip("Ticks before plants appear on newly placed/revealed grass")] public int newGrassPlantDelayTicks = 5;
    [Tooltip("Ticks before exposed Dirt turns into Grass")] public int dirtToGrassDelayTicks = 10;
    private float _tickAccum = 0f;
    private int _currentTick = 0;
    private struct TickAction { public int dueTick; public int type; public Vector3Int cell; }
    private const int TA_PlantRespawn = 1;
    private const int TA_GrassGrow = 2;
    private readonly List<TickAction> _tickQueue = new List<TickAction>();
    private readonly HashSet<Vector3Int> _scheduledPlantCells = new HashSet<Vector3Int>(); // grass cell keys
    private readonly HashSet<Vector3Int> _scheduledGrassCells = new HashSet<Vector3Int>(); // dirt cell keys
    // Chunk persistence: per-chunk map of edited cells (world cell -> type)
    private readonly Dictionary<Vector2Int, Dictionary<Vector3Int, BlockType>> _chunkEdits = new Dictionary<Vector2Int, Dictionary<Vector3Int, BlockType>>();
    // Meshing mode: per-chunk set of plant cells removed by the player (world-space plant cell = above grass)
    private readonly Dictionary<Vector2Int, HashSet<Vector3Int>> _removedBatchedPlants = new Dictionary<Vector2Int, HashSet<Vector3Int>>();
    // One-time logs to avoid spamming when falling back from custom shaders
    private bool _loggedPlantWindFallback = false;
    private bool _loggedLeavesWindFallback = false;

    // Save DTOs (local to avoid cross-file dependency)
    [System.Serializable]
    private class ChangedCellDTO { public int x; public int y; public int z; public int t; }
    [System.Serializable]
    private class ChunkSaveDTO { public int cx; public int cz; public int sizeX; public int sizeY; public int sizeZ; public ChangedCellDTO[] changes; }

    
    void Start()
    {
    // Try to find TextureVariationManager if already configured in the scene (optional)
    textureManager = GetComponent<TextureVariationManager>();
        
        LoadTextures();
        CreateBlockMaterials();

        // Ensure a F3 debug overlay exists; avoid compile-time dependency via reflection.
        try
        {
            bool hasOverlay = false;
            var mbs = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var mb in mbs)
            {
                if (mb == null) continue;
                var t = mb.GetType();
                if (t != null && t.Name == "F3DebugOverlay") { hasOverlay = true; break; }
            }
            if (!hasOverlay)
            {
                var overlayType = ResolveTypeByName("F3DebugOverlay");
                if (overlayType != null)
                {
                    var go = new GameObject("DebugOverlay");
                    var comp = go.AddComponent(overlayType);
                    // Assign fields if they exist
                    var wf = overlayType.GetField("world"); if (wf != null) wf.SetValue(comp, this);
                    AutoFindPlayer();
                    var pf = overlayType.GetField("player"); if (pf != null) pf.SetValue(comp, player);
                    DontDestroyOnLoad(go);
                }
            }
        }
        catch { /* ignore overlay setup errors */ }
        if (useChunkStreaming)
        {
            EnsureChunksRoot();
            AutoFindPlayer();
            // Don't start chunk streaming until InitializeForWorld is called
            // UpdateStreaming(force: true) will be called by InitializeForWorld
            if (delayPlayerSpawnUntilChunks)
            {
                StartCoroutine(StartupPlayerGate());
            }
        }
        else
        {
            GenerateWorld();
        }
        
        // Note: currentWorldName will be set by WorldLoader or GameManager after Start()
        // Persistence will be properly managed by InitializeForWorld method
    }
    
    void Update()
    {
        // Live-apply ambient fill tuning
        if (blockMaterials != null && (enableAmbientFill != _lastEnableAmbientFill || Mathf.Abs(ambientFill - _lastAmbientFill) > 0.0001f))
        {
            _lastEnableAmbientFill = enableAmbientFill;
            _lastAmbientFill = ambientFill;
            var ec = new Color(ambientFill, ambientFill, ambientFill, 1f);
            for (int i = 0; i < blockMaterials.Length; i++)
            {
                var m = blockMaterials[i];
                if (m == null) continue;
                if (enableAmbientFill)
                {
                    m.SetColor("_EmissionColor", ec);
                    m.EnableKeyword("_EMISSION");
                }
                else
                {
                    m.SetColor("_EmissionColor", Color.black);
                    m.DisableKeyword("_EMISSION");
                }
            }
        }
        // Update leaf wind material params each frame (cheap, single material)
        if (enableLeafWind && blockMaterials != null && (int)BlockType.Leaves < blockMaterials.Length)
        {
            var lm = blockMaterials[(int)BlockType.Leaves];
            if (lm != null)
            {
                if (lm.shader != null && lm.shader.name == "Custom/LeavesWind")
                {
                    lm.SetFloat("_WindAmp", leafWindAmplitude);
                    lm.SetFloat("_WindSpeed", leafWindSpeed);
                    lm.SetFloat("_WindScale", leafWindScale);
                    lm.SetFloat("_WindVertical", leafWindVerticalFactor);
                    Vector2 dir = leafWindDirection.sqrMagnitude < 0.0001f ? new Vector2(1,0) : leafWindDirection.normalized;
                    lm.SetVector("_WindDir", new Vector4(dir.x, dir.y, 0, 0));
                }
            }
        }
        // Live leaf wind shader swap to avoid pink and allow toggling at runtime
        if (blockMaterials != null && (enableLeafWind != _lastEnableLeafWindToggle))
        {
            _lastEnableLeafWindToggle = enableLeafWind;
            if ((int)BlockType.Leaves < blockMaterials.Length)
            {
                var lm = blockMaterials[(int)BlockType.Leaves];
                if (lm != null)
                {
                    if (enableLeafWind)
                    {
                        var ws = Shader.Find("Custom/LeavesWind");
                        if (ws != null && ws.isSupported && IsShaderURPCompatible(ws))
                        {
                            lm.shader = ws;
                        }
                        else if (!_loggedLeavesWindFallback)
                        {
                            _loggedLeavesWindFallback = true;
                            Debug.LogWarning("Leaves wind shader not available/compatible; keeping URP Simple Lit to avoid pink.");
                        }
                    }
                    else
                    {
                        var simpleLit = Shader.Find("Universal Render Pipeline/Simple Lit");
                        if (simpleLit != null)
                        {
                            lm.shader = simpleLit;
                        }
                    }
                    ApplyLeafMaterialSettings(lm);
                }
            }
        }
        // Update plant wind
        if (enablePlantWind && _plantMaterialCache != null)
        {
            foreach (var kv in _plantMaterialCache)
            {
                var pm = kv.Value;
                if (pm == null) continue;
                if (pm.shader != null && pm.shader.name == "Custom/PlantWind")
                {
                    pm.SetFloat("_WindAmp", plantWindAmplitude);
                    pm.SetFloat("_WindSpeed", plantWindSpeed);
                    pm.SetFloat("_WindScale", plantWindScale);
                    pm.SetFloat("_WindVertical", plantWindVerticalFactor);
                    pm.SetFloat("_WindVar", plantWindVariation);
                    Vector2 dir = leafWindDirection.sqrMagnitude < 0.0001f ? new Vector2(1,0) : leafWindDirection.normalized; // reuse leaf direction
                    pm.SetVector("_WindDir", new Vector4(dir.x, dir.y, 0, 0));
                }
            }
        }
        // Debug trigger reload
        if (Application.isPlaying && triggerReloadInPlay)
        {
            triggerReloadInPlay = false; // reset flag
            ReloadWorld();
        }
        // Tick scheduler processing
        _tickAccum += Time.deltaTime;
        while (_tickAccum >= tickIntervalSeconds)
        {
            _tickAccum -= tickIntervalSeconds;
            _currentTick++;
            ProcessTick();
        }

        if (useChunkStreaming)
        {
            UpdateStreaming();
            // Live plant density adjustment: rebuild plant meshes if value changed
            if (useChunkMeshing && Mathf.Abs(plantDensity - _lastPlantDensity) > 0.0001f)
            {
                _lastPlantDensity = plantDensity;
                RebuildPlantsForAllLoadedChunks();
            }
            // Live plant wind shader swap
            if (_plantMaterialCache != null)
            {
                foreach (var kv in new List<Texture2D>(_plantMaterialCache.Keys))
                {
                    var mat = _plantMaterialCache[kv];
                    if (mat == null) { _plantMaterialCache.Remove(kv); continue; }
                    bool hasWind = mat.shader != null && mat.shader.name == "Custom/PlantWind";
                    if (enablePlantWind && !hasWind)
                    {
                        var ws = Shader.Find("Custom/PlantWind");
                        if (ws != null && ws.isSupported && IsShaderURPCompatible(ws))
                        {
                            mat.shader = ws;
                            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
                            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
                            if (mat.HasProperty("_AlphaToMask")) mat.SetFloat("_AlphaToMask", 1f);
                        }
                        else if (!_loggedPlantWindFallback)
                        {
                            _loggedPlantWindFallback = true;
                            Debug.LogWarning("Plant wind shader not available/compatible; using URP Simple Lit instead to avoid pink.");
                        }
                    }
                    else if (!enablePlantWind && hasWind)
                    {
                        var sl = Shader.Find("Universal Render Pipeline/Simple Lit");
                        if (sl != null)
                        {
                            mat.shader = sl;
                            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
                            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
                            if (mat.HasProperty("_AlphaToMask")) mat.SetFloat("_AlphaToMask", 1f);
                        }
                    }
                }
            }
            // Live plant weights/sizes update
            if (useChunkMeshing && (
                Mathf.Abs(plantWeightFern - _lastPlantWeightFern) > 0.0001f ||
                Mathf.Abs(plantWeightPlant - _lastPlantWeightPlant) > 0.0001f ||
                Mathf.Abs(plantWeightGrass - _lastPlantWeightGrass) > 0.0001f ||
                Mathf.Abs(plantSizeFern - _lastPlantSizeFern) > 0.0001f ||
                Mathf.Abs(plantSizePlant - _lastPlantSizePlant) > 0.0001f ||
                Mathf.Abs(plantSizeGrass - _lastPlantSizeGrass) > 0.0001f))
            {
                _lastPlantWeightFern = plantWeightFern;
                _lastPlantWeightPlant = plantWeightPlant;
                _lastPlantWeightGrass = plantWeightGrass;
                _lastPlantSizeFern = plantSizeFern;
                _lastPlantSizePlant = plantSizePlant;
                _lastPlantSizeGrass = plantSizeGrass;
                RebuildPlantsForAllLoadedChunks();
            }
            // Start background chunk loads (removed frame time restriction for faster loading)
            int budget = Mathf.Max(1, maxChunkLoadsPerFrame);
            while (budget-- > 0 && _pendingLoads.Count > 0)
            {
                var next = _pendingLoads.Dequeue();
                _queued.Remove(next);
                if (_chunks.ContainsKey(next) || _loading.Contains(next)) continue;
                // Mark as loading before starting coroutine to avoid races
                _loading.Add(next);
                
                // Use optimized generation to prevent FPS drops
                StartCoroutine(LoadChunkRoutineOptimized(next));
            }
            // Process a small number of deferred neighbor mesh rebuilds per frame to avoid spikes
            int rebuildBudget = 2;
            if (_deferredRebuild.Count > 0)
            {
                var toProcess = new List<Vector2Int>(_deferredRebuild);
                foreach (var c in toProcess)
                {
                    if (rebuildBudget-- <= 0) break;
                    if (_chunks.ContainsKey(c))
                    {
                        RebuildChunkMeshAt(c);
                        _deferredRebuild.Remove(c);
                    }
                }
            }
        }
    }

    // Rebuild only the plant meshes for all currently loaded chunks (cheap vs full chunk mesh rebuild)
    private void RebuildPlantsForAllLoadedChunks()
    {
        if (!useChunkStreaming || !useChunkMeshing) return;
        foreach (var kv in _chunks)
        {
            var chunk = kv.Value;
            if (chunk == null) continue;
            int fullY = Mathf.Clamp(chunk.sizeY, 1, worldHeight);
            BuildChunkPlants(chunk, fullY);
        }
    }

    /// <summary>
    /// Public debug-friendly API to fully reload the world (clears streamed chunks and regenerated content).
    /// </summary>
    public void ReloadWorld()
    {
        StopAllCoroutines();

        // Optionally reseed
        if (reseedOnReload)
        {
            worldSeed = UnityEngine.Random.Range(int.MinValue / 2, int.MaxValue / 2);
        }

        // Persist current chunks before wiping (unless clearing edits)
        if (enableChunkPersistence && !clearSavedEditsOnReload)
        {
            foreach (var kv in _chunks)
            {
                SaveChunkToDisk(kv.Key);
            }
        }
        // Unload existing chunk GameObjects
        foreach (var kv in _chunks)
        {
            kv.Value.Unload(this);
        }
        _chunks.Clear();
        _pendingLoads.Clear();
        _queued.Clear();
        _loading.Clear();
        blockObjects.Clear();
        plantObjects.Clear();
        _treeTrunkPositions.Clear();
        _scheduledPlantCells.Clear();
        _scheduledGrassCells.Clear();
        _tickQueue.Clear();
        if (clearSavedEditsOnReload)
        {
            _chunkEdits.Clear();
            // Wipe on-disk saves directory
            try
            {
                var dir = GetSaveFolderPath();
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
            catch { /* ignore */ }
        }

        // Non-streaming world data clear
        if (!useChunkStreaming)
        {
            worldData = null;
        }

        if (useChunkStreaming)
        {
            EnsureChunksRoot();
            UpdateStreaming(force: true);
            if (delayPlayerSpawnUntilChunks)
            {
                StartCoroutine(StartupPlayerGate());
            }
        }
        else
        {
            GenerateWorld();
        }
    }
    
    
    void LoadTextures()
    {
        // Load textures from Resources folder if not assigned
        if (grassTexture == null) grassTexture = Resources.Load<Texture2D>("Textures/top_grass");
        if (grassSideTexture == null) grassSideTexture = Resources.Load<Texture2D>("Textures/side_grass");
        if (dirtTexture == null) dirtTexture = Resources.Load<Texture2D>("Textures/dirt");
        if (stoneTexture == null) stoneTexture = Resources.Load<Texture2D>("Textures/stone");
        if (sandTexture == null) sandTexture = Resources.Load<Texture2D>("Textures/sand");
        if (coalTexture == null) coalTexture = Resources.Load<Texture2D>("Textures/coal");
        // Try common names for log/leaves so we work with multiple packs
        if (logTexture == null)
            logTexture = Resources.Load<Texture2D>("Textures/log") ?? Resources.Load<Texture2D>("Textures/oak_log");
        if (leavesTexture == null)
            leavesTexture = Resources.Load<Texture2D>("Textures/leaves") ?? Resources.Load<Texture2D>("Textures/leaves_oak");

    // Plants: STRICT sources
    // Editor: use Assets/Textures/plants/{fern,plant}.png
    // Runtime: use Resources/Textures/plants/{fern,plant}
    _extraPlantDefs.Clear();
#if UNITY_EDITOR
    var fernEd = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/plants/fern.png");
    var plantEd = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/plants/plant.png");
    var grassEd = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/plants/grass.png");
    if (fernEd != null) { ConfigureTexture(fernEd); AddRuntimePlantIfNew(fernEd); }
    if (plantEd != null) { ConfigureTexture(plantEd); AddRuntimePlantIfNew(plantEd); }
    if (grassEd != null) { ConfigureTexture(grassEd); AddRuntimePlantIfNew(grassEd); }
    if (fernEd == null && plantEd == null)
    {
        Debug.LogWarning("Plants: drop fern.png, plant.png, or grass.png into Assets/Textures/plants/. No other sources are used.");
    }
#else
    var fernRes = Resources.Load<Texture2D>("Textures/plants/fern");
    var plantRes = Resources.Load<Texture2D>("Textures/plants/plant");
    var grassRes = Resources.Load<Texture2D>("Textures/plants/grass");
    if (fernRes != null) { ConfigureTexture(fernRes); AddRuntimePlantIfNew(fernRes); }
    if (plantRes != null) { ConfigureTexture(plantRes); AddRuntimePlantIfNew(plantRes); }
    if (grassRes != null) { ConfigureTexture(grassRes); AddRuntimePlantIfNew(grassRes); }
#endif

#if UNITY_EDITOR
        // Editor-only fallback to load directly from Assets/Textures if not found in Resources
        if (grassTexture == null) grassTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/top_grass.png");
        if (grassSideTexture == null) grassSideTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/side_grass.png");
        if (dirtTexture == null) dirtTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/dirt.png");
        if (stoneTexture == null) stoneTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/stone.png");
        if (sandTexture == null) sandTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/sand.png");
        if (coalTexture == null) coalTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/coal.png");
        // logs/leaves: try multiple names present in this repo
        if (logTexture == null)
        {
            logTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/log.png");
            if (logTexture == null)
                logTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/oak_log.png");
        }
        if (leavesTexture == null)
        {
            leavesTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/leaves.png");
            if (leavesTexture == null)
                leavesTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/leaves_oak.png");
        }

    // Plants are assigned via PlantDatabase assets in the editor.
#endif
        
        // Developer hints if critical textures are still missing
        if (logTexture == null)
        {
            Debug.LogWarning("WorldGenerator: Log texture not found (tried Resources/Textures/log, Resources/Textures/oak_log and Assets/Textures/{log|oak_log}.png). Tree trunks may appear white.");
        }
        if (leavesTexture == null)
        {
            Debug.LogWarning("WorldGenerator: Leaves texture not found (tried Resources/Textures/leaves, Resources/Textures/leaves_oak and Assets/Textures/{leaves|leaves_oak}.png). Leaves may appear white.");
        }

        // Configure textures for better pixel art rendering
        ConfigureTexture(grassTexture);
    ConfigureTexture(grassSideTexture);
        ConfigureTexture(dirtTexture);
        ConfigureTexture(stoneTexture);
        ConfigureTexture(sandTexture);
        ConfigureTexture(coalTexture);
    ConfigureTexture(logTexture);
        ConfigureTexture(leavesTexture);
        // Leaves: previously forced Bilinear (causing blur). Now honor user-configurable setting.
        if (leavesTexture != null)
        {
            leavesTexture.filterMode = leavesFilterMode;
            leavesTexture.anisoLevel = 0;
            try { leavesTexture.mipMapBias = leavesMipMapBias; } catch { /* ignore if platform disallows */ }
        }
    // Plant textures are configured via PlantDatabase above
        
        // Assign textures to block data
        BlockDatabase.blockTypes[(int)BlockType.Grass].blockTexture = grassTexture;
        BlockDatabase.blockTypes[(int)BlockType.Dirt].blockTexture = dirtTexture;
        BlockDatabase.blockTypes[(int)BlockType.Stone].blockTexture = stoneTexture;
        BlockDatabase.blockTypes[(int)BlockType.Sand].blockTexture = sandTexture;
        BlockDatabase.blockTypes[(int)BlockType.Coal].blockTexture = coalTexture;
    BlockDatabase.blockTypes[(int)BlockType.Log].blockTexture = logTexture;
    BlockDatabase.blockTypes[(int)BlockType.Leaves].blockTexture = leavesTexture;
    }
    
    void ConfigureTexture(Texture2D texture)
    {
        if (texture == null) return;
        // Apply settings that reduce tiling appearance
        texture.filterMode = FilterMode.Point; // Pixelated look
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.anisoLevel = 0; // No anisotropic filtering for pixel art
    }

    void CreateBlockMaterials()
    {
        blockMaterials = new Material[BlockDatabase.blockTypes.Length];
        
        for (int i = 0; i < BlockDatabase.blockTypes.Length; i++)
        {
            var blockType = BlockDatabase.blockTypes[i].blockType;
            if (blockType != BlockType.Air)
            {
                // Try to get material from BlockManager first
                Material mat = BlockManager.GetBlockMaterial(blockType);
                
                // If BlockManager doesn't have it, create one using available textures
                if (mat == null)
                {
                    mat = CreateMaterialForBlockType(blockType);
                }
                
                if (mat != null)
                {
                    blockMaterials[i] = mat;
                    BlockDatabase.blockTypes[i].blockMaterial = mat;
                }
                else
                {
                    Debug.LogError($"WorldGenerator: Failed to create material for {blockType}!");
                }
            }
        }
        
        // Create special materials for multi-sided blocks
        CreateGrassSideMaterial();
        CreateCraftingTableMaterials();
        
        // Configure special material properties (leaves, etc.)
        ConfigureSpecialMaterials();
        
        Debug.Log($"WorldGenerator: Created materials for {blockMaterials.Length} block types using BlockManager system");
    }
    
    Material CreateMaterialForBlockType(BlockType blockType)
    {
        // Use URP/Lit shader
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        
        // Get texture from BlockManager or fallback to hardcoded textures
        Texture2D tex = GetTextureForBlockType(blockType);
        
        if (tex != null)
        {
            mat.mainTexture = tex;
            mat.SetTexture("_BaseMap", tex);
            mat.color = Color.white;
        }
        else
        {
            // Fallback to block database color
            mat.color = BlockDatabase.blockTypes[(int)blockType].blockColor;
        }
        
        // Configure material properties for pixel art
        mat.SetFloat("_Smoothness", 0.0f);
        mat.SetFloat("_Metallic", 0.0f);
        mat.SetFloat("_Cull", 0f); // Two-sided
        
        mat.name = blockType + "Material";
        return mat;
    }
    
    Texture2D GetTextureForBlockType(BlockType blockType)
    {
        // First try BlockManager
        var blockConfig = BlockManager.GetBlockConfiguration(blockType);
        if (blockConfig != null && blockConfig.mainTexture != null)
        {
            return blockConfig.mainTexture;
        }
        
        // Fallback to legacy hardcoded textures (for backward compatibility during transition)
        switch (blockType)
        {
            case BlockType.Grass: return grassTexture;
            case BlockType.Dirt: return dirtTexture;
            case BlockType.Stone: return stoneTexture;
            case BlockType.Sand: return sandTexture;
            case BlockType.Coal: return coalTexture;
            case BlockType.Log: return logTexture;
            case BlockType.Leaves: return leavesTexture;
            case BlockType.WoodPlanks: return woodPlanksTexture;
            case BlockType.CraftingTable: return craftingTableTexture;
            case BlockType.Bedrock: return bedrockTexture;
            case BlockType.Gravel: return gravelTexture;
            case BlockType.Iron: return ironTexture;
            case BlockType.Gold: return goldTexture;
            case BlockType.Diamond: return diamondTexture;
            default: return null;
        }
    }
    
    void CreateGrassSideMaterial()
    {
        var blockConfig = BlockManager.GetBlockConfiguration(BlockType.Grass);
        Texture2D grassSideTex = (blockConfig != null && blockConfig.hasMultipleSides && blockConfig.sideTexture != null) 
            ? blockConfig.sideTexture 
            : grassSideTexture; // Fallback
            
        if (grassSideTex != null)
        {
            // Main grass side material
            _grassSideMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _grassSideMaterial.mainTexture = grassSideTex;
            _grassSideMaterial.SetTexture("_BaseMap", grassSideTex);
            _grassSideMaterial.color = Color.white;
            _grassSideMaterial.SetFloat("_Smoothness", 0.0f);
            _grassSideMaterial.SetFloat("_Metallic", 0.0f);
            _grassSideMaterial.SetFloat("_Cull", 0f);
            _grassSideMaterial.name = "GrassSideMaterial";
            
            // Overlay variant (alpha-clipped, unlit-like) to layer over dirt base
            _grassSideOverlayMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _grassSideOverlayMaterial.name = "GrassSideOverlay";
            _grassSideOverlayMaterial.mainTexture = grassSideTex;
            _grassSideOverlayMaterial.SetTexture("_BaseMap", grassSideTex);
            _grassSideOverlayMaterial.color = Color.white;
            _grassSideOverlayMaterial.SetFloat("_Cull", 0f);
            _grassSideOverlayMaterial.SetFloat("_AlphaClip", 1f);
            _grassSideOverlayMaterial.SetFloat("_Cutoff", 0.5f);
            _grassSideOverlayMaterial.EnableKeyword("_ALPHATEST_ON");
            _grassSideOverlayMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
    }
    
    void CreateCraftingTableMaterials()
    {
        var blockConfig = BlockManager.GetBlockConfiguration(BlockType.CraftingTable);
        if (blockConfig != null && blockConfig.hasMultipleSides)
        {
            // Create materials for different sides if textures are available
            // This method can be expanded based on specific crafting table texture needs
        }
    }
    
    void ConfigureSpecialMaterials()
    {
        // Leaves: ensure proper lighting with URP Simple Lit + alpha cutout
        if (BlockDatabase.blockTypes[(int)BlockType.Leaves].blockMaterial != null)
        {
            var lm = BlockDatabase.blockTypes[(int)BlockType.Leaves].blockMaterial;
            var simpleLit = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (simpleLit != null)
            {
                lm.shader = simpleLit;
            }
            // Optional wind swap before applying settings so final shader gets configured once.
            if (enableLeafWind)
            {
                var ws = Shader.Find("Custom/LeavesWind");
                if (ws != null && ws.isSupported && IsShaderURPCompatible(ws))
                {
                    lm.shader = ws;
                }
                else if (!_loggedLeavesWindFallback)
                {
                    _loggedLeavesWindFallback = true;
                    Debug.LogWarning("Leaves wind shader not available/compatible; using URP Simple Lit instead to avoid pink.");
                }
            }
            ApplyLeafMaterialSettings(lm);
        }
    }

    // Centralized leaf material configuration so runtime swaps don't lose settings or cause unexpected blur
    private void ApplyLeafMaterialSettings(Material lm)
    {
        if (lm == null) return;
        lm.SetFloat("_AlphaClip", 1f);
        lm.SetFloat("_Cutoff", Mathf.Clamp(leavesCutoff, 0.1f, 0.8f));
        lm.EnableKeyword("_ALPHATEST_ON");
        if (lm.HasProperty("_Cull")) lm.SetFloat("_Cull", 0f);
        lm.doubleSidedGI = true;
        if (lm.HasProperty("_AlphaToMask")) lm.SetFloat("_AlphaToMask", 1f);
        lm.renderQueue = (int)RenderQueue.AlphaTest + 10;
        if (lm.HasProperty("_Smoothness")) lm.SetFloat("_Smoothness", 0.0f);
        if (lm.HasProperty("_Metallic")) lm.SetFloat("_Metallic", 0.0f);
        lm.SetShaderPassEnabled("ShadowCaster", leavesReceiveShadows);
        if (enableAmbientFill)
        {
            Color ec = new Color(ambientFill, ambientFill, ambientFill, 1f);
            lm.SetColor("_EmissionColor", ec);
            lm.EnableKeyword("_EMISSION");
        }
        else
        {
            lm.DisableKeyword("_EMISSION");
        }
    }

    // Public accessor for chunk mesh builder
    public Material GetBlockMaterial(BlockType t)
    {
        if (blockMaterials == null) return null;
        
        int blockIndex = (int)t;
        
        // Migration: Handle old saved BlockType values after Stick removal
        if (blockIndex == 16) // Old CraftingTable value
        {
            Debug.Log($"Migrating old BlockType value 16 (CraftingTable) to new value 15");
            blockIndex = 15; // New CraftingTable value
            t = BlockType.CraftingTable;
        }
        
        if (blockIndex < 0 || blockIndex >= blockMaterials.Length)
        {
            Debug.LogWarning($"BlockType {t} (index {blockIndex}) is out of bounds for blockMaterials array (length {blockMaterials.Length})");
            return null;
        }
        
        return blockMaterials[blockIndex];
    }

    // Returns the material to use for a particular face of a block.
    // faceIndex matches ChunkMeshBuilder's order: 0=+X,1=-X,2=+Y(top),3=-Y(bottom),4=+Z,5=-Z
    public Material GetFaceMaterial(BlockType t, int faceIndex)
    {
        // Check if this block type has multi-sided configuration
        var blockConfig = BlockManager.GetBlockConfiguration(t);
        if (blockConfig != null && blockConfig.hasMultipleSides)
        {
            return GetMultiSidedMaterial(t, faceIndex, blockConfig);
        }
        
        // For single-sided blocks or fallback, use standard material
        return GetBlockMaterial(t);
    }
    
    Material GetMultiSidedMaterial(BlockType blockType, int faceIndex, BlockConfiguration config)
    {
        switch (blockType)
        {
            case BlockType.Grass:
                return GetGrassFaceMaterial(faceIndex, config);
                
            case BlockType.CraftingTable:
                return GetCraftingTableFaceMaterial(faceIndex, config);
                
            default:
                // For other multi-sided blocks, use main texture
                return GetBlockMaterial(blockType);
        }
    }
    
    Material GetGrassFaceMaterial(int faceIndex, BlockConfiguration config)
    {
        // faceIndex: 0=+X,1=-X,2=+Y(top),3=-Y(bottom),4=+Z,5=-Z
        switch (faceIndex)
        {
            case 2: // Top face
                return config.topTexture != null ? 
                    CreateTempMaterial(config.topTexture) : 
                    GetBlockMaterial(BlockType.Grass);
                    
            case 3: // Bottom face  
                return config.bottomTexture != null ?
                    CreateTempMaterial(config.bottomTexture) :
                    GetBlockMaterial(BlockType.Dirt); // Fallback
                    
            default: // Side faces
                if (config.sideTexture != null && _grassSideMaterial != null)
                    return _grassSideMaterial;
                return GetBlockMaterial(BlockType.Grass); // Fallback
        }
    }
    
    Material GetCraftingTableFaceMaterial(int faceIndex, BlockConfiguration config)
    {
        // Similar logic for crafting table faces
        switch (faceIndex)
        {
            case 2: // Top face
                return config.topTexture != null ?
                    CreateTempMaterial(config.topTexture) :
                    GetBlockMaterial(BlockType.CraftingTable);
                    
            case 3: // Bottom face
                return config.bottomTexture != null ?
                    CreateTempMaterial(config.bottomTexture) :
                    GetBlockMaterial(BlockType.WoodPlanks); // Fallback to wood planks
                    
            default: // Side faces
                return config.sideTexture != null ?
                    CreateTempMaterial(config.sideTexture) :
                    GetBlockMaterial(BlockType.CraftingTable);
        }
    }
    
    // Helper method to create temporary materials for specific textures
    Material CreateTempMaterial(Texture2D texture)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.mainTexture = texture;
        mat.SetTexture("_BaseMap", texture);
        mat.color = Color.white;
        mat.SetFloat("_Smoothness", 0.0f);
        mat.SetFloat("_Metallic", 0.0f);
        mat.SetFloat("_Cull", 0f);
        return mat;
    }

    // Exposed for mesh builder overlay composition
    public Material GetGrassSideOverlayMaterial()
    {
        return _grassSideOverlayMaterial;
    }

    // Determine if a block type should occlude adjacent faces (used for meshing & visibility tests)
    public bool IsBlockOpaque(BlockType t)
    {
        switch (t)
        {
            case BlockType.Air: return false;
            case BlockType.Leaves: return false; // non-occluding like before; let faces render behind leaves
            default: return true;
        }
    }
    
    void GenerateWorld()
    {
        worldData = new BlockType[worldWidth, worldHeight, worldDepth];

        // Generate world using the new clean system
        for (int x = 0; x < worldWidth; x++)
        {
            for (int z = 0; z < worldDepth; z++)
            {
                for (int y = 0; y < worldHeight; y++)
                {
                    worldData[x, y, z] = GenerateBlockTypeAt(new Vector3Int(x, y, z));
                }
            }
        }

        // Create visible blocks and spawn plants
        UpdateVisibleBlocks();
        SpawnPlants();
    }

    // Clean, simple world generation - Minecraft-style layers
    public BlockType GenerateBlockTypeAt(Vector3Int worldPos)
    {
        if (worldPos.y < 0 || worldPos.y >= worldHeight) return BlockType.Air;

        // Bedrock layer (unbreakable foundation)
        if (worldPos.y <= 2) return BlockType.Bedrock;
        
        // Check for NEW tunnel system FIRST at all Y levels
        if (enableTunnels && WorldGeneration.HorizontalTunnelGenerator.IsTunnelBlock(worldPos, worldSeed, tunnelSettings))
        {
            return BlockType.Air; // Tunnel air space
        }
        
        // Deep underground layers (Y 3-99) - Only generate solid blocks if no cave
        if (worldPos.y <= 99)
        {
            return GenerateUndergroundBlock(worldPos);
        }
        
        // Surface terrain (Y 100+) - Only generate if no cave
        return GenerateSurfaceBlock(worldPos);
    }
    
    private BlockType GenerateUndergroundBlock(Vector3Int worldPos)
    {
        // Use new chunk-based ore generation system
        if (oreSettings.enableChunkOreGeneration)
        {
            // Calculate which chunk this position belongs to
            Vector2Int chunkCoord = new Vector2Int(
                Mathf.FloorToInt(worldPos.x / (float)chunkSizeX),
                Mathf.FloorToInt(worldPos.z / (float)chunkSizeZ)
            );
            
            // Ensure chunk ore generation is initialized
            WorldGeneration.Chunks.ChunkOreGenerator.GenerateChunkOreBlobs(chunkCoord, chunkSizeX, chunkSizeZ, worldSeed, oreSettings);
            
            // Get ore type from chunk-based system
            BlockType oreType = WorldGeneration.Chunks.ChunkOreGenerator.GetOreAtPosition(worldPos, chunkCoord, worldSeed, oreSettings);
            
            if (oreType != BlockType.Stone)
            {
                return oreType; // Return the ore from the chunk system
            }
            
            return BlockType.Stone; // Default to stone
        }
        
        // Fallback to legacy random system if chunk ore generation is disabled
        return GenerateUndergroundBlockLegacy(worldPos);
    }
    
    /// <summary>
    /// Legacy underground block generation (kept as fallback)
    /// </summary>
    private BlockType GenerateUndergroundBlockLegacy(Vector3Int worldPos)
    {
        // Original random-based ore generation
        System.Random rng = new System.Random(worldPos.x * 73856093 ^ worldPos.y * 19349663 ^ worldPos.z * 83492791 ^ worldSeed);
        float chance = (float)rng.NextDouble();
        
        // Calculate depth factor: deeper = rarer ores (Y=3 is deepest, Y=99 is shallowest)
        float depthFactor = (100f - worldPos.y) / 97f; // 0.0 at surface, 1.0 at deepest
        
        // Deep Underground (Y 3-30): Deepest ores
        if (worldPos.y <= 30)
        {
            // Diamond (very rare, only in deepest layers)
            if (worldPos.y <= 20 && chance < 0.004f * depthFactor)
                return BlockType.Diamond;
            
            // Gold (rare, deep preferred)
            if (worldPos.y <= 25 && chance < 0.010f * depthFactor)
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
        if (worldPos.y <= 60)
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
        if (worldPos.y <= 99)
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
    
    private BlockType GenerateSurfaceBlock(Vector3Int worldPos)
    {
        // Debug warning if using default seed (indicates timing issue)
        if (worldSeed == 12345 && !string.IsNullOrEmpty(currentWorldName))
        {
            Debug.LogWarning($"WorldGenerator: Using default seed {worldSeed} for world '{currentWorldName}' - possible timing issue!");
        }
        
        // Plains biome: mostly flat with occasional small hills
        // Use smaller seed offset to avoid large coordinate values that break Perlin noise
        float seedOffset = (worldSeed % 10000) * 0.01f;
        
        // Base flat terrain with higher default variation
        float baseX = worldPos.x * 0.008f + seedOffset; // Slightly faster variation for more rolling terrain
        float baseZ = worldPos.z * 0.008f + seedOffset;
        float baseNoise = Mathf.PerlinNoise(baseX, baseZ);
        
        // Hill detection - smaller but more common hills
        float hillX = worldPos.x * 0.015f + seedOffset * 1.7f; // Higher frequency for more common hills
        float hillZ = worldPos.z * 0.015f + seedOffset * 1.7f;
        float hillNoise = Mathf.PerlinNoise(hillX, hillZ);
        
        // Very common hills (lower threshold for more frequent hills)
        float hillMultiplier = hillNoise > 0.3f ? Mathf.Pow((hillNoise - 0.3f) / 0.7f, 1.2f) : 0f;
        
        // Small detail noise for micro-variations
        float detailX = worldPos.x * 0.05f + seedOffset;
        float detailZ = worldPos.z * 0.05f + seedOffset;
        float detailNoise = Mathf.PerlinNoise(detailX, detailZ) * 0.3f;
        
        // Combine: higher base variation + smaller common hills + details
        // Moved surface to Y=100-120 to allow for 100 underground levels (Y=0-99)
        float combinedHeight = 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise;
        int surfaceHeight = Mathf.RoundToInt(combinedHeight); // Base Y=105-115, hills up to Y=119
        
        // Debug logging disabled to improve performance
        // if (worldPos.x >= -2 && worldPos.x <= 2 && worldPos.z >= -2 && worldPos.z <= 2 && worldPos.y == surfaceHeight)
        // {
        //     Debug.Log($"Plains Terrain - Pos: {worldPos}, baseNoise: {baseNoise:F3}, hillNoise: {hillNoise:F3}, hillMult: {hillMultiplier:F3}, detailNoise: {detailNoise:F3}, height: {surfaceHeight}, seed: {worldSeed}");
        // }
        
        if (worldPos.y < surfaceHeight - 4) return BlockType.Stone;
        if (worldPos.y < surfaceHeight) return BlockType.Dirt;
        if (worldPos.y == surfaceHeight) return BlockType.Grass;
        
        return BlockType.Air;
    }

    // Public helper (used in chunk streaming to compute useful Y per chunk)
    public int GetColumnTopY(int worldX, int worldZ)
    {
        // Use the same surface generation logic as GenerateSurfaceBlock
        // Plains biome: mostly flat with occasional small hills
        float seedOffset = (worldSeed % 10000) * 0.01f;
        
        // Base flat terrain with higher default variation
        float baseX = worldX * 0.008f + seedOffset; // Slightly faster variation for more rolling terrain
        float baseZ = worldZ * 0.008f + seedOffset;
        float baseNoise = Mathf.PerlinNoise(baseX, baseZ);
        
        // Hill detection - smaller but more common hills
        float hillX = worldX * 0.015f + seedOffset * 1.7f; // Higher frequency for more common hills
        float hillZ = worldZ * 0.015f + seedOffset * 1.7f;
        float hillNoise = Mathf.PerlinNoise(hillX, hillZ);
        
        // Very common hills (lower threshold for more frequent hills)
        float hillMultiplier = hillNoise > 0.3f ? Mathf.Pow((hillNoise - 0.3f) / 0.7f, 1.2f) : 0f;
        
        // Small detail noise for micro-variations
        float detailX = worldX * 0.05f + seedOffset;
        float detailZ = worldZ * 0.05f + seedOffset;
        float detailNoise = Mathf.PerlinNoise(detailX, detailZ) * 0.3f;
        
        // Combine: higher base variation + smaller common hills + details
        // Moved surface to Y=100-120 to allow for 100 underground levels (Y=0-99)
        float combinedHeight = 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise;
        int surfaceHeight = Mathf.RoundToInt(combinedHeight);
        return Mathf.Min(worldHeight - 1, surfaceHeight);
    }

    // Removed old complex noise system - now using simple Perlin noise in surface generation

    // --- Chunk streaming helpers ---
    private void EnsureChunksRoot()
    {
        if (_chunksRoot == null)
        {
            var go = new GameObject("Chunks");
            go.transform.SetParent(this.transform, false);
            _chunksRoot = go.transform;
        }
    }

    // --- Public accessors (for debug/overlay tools) ---
    public Vector2Int GetChunkCoord(Vector3 position) => WorldToChunkCoord(position);
    public Vector3 GetChunkOrigin(Vector2Int coord) => new Vector3(coord.x * Mathf.Max(1, chunkSizeX), 0f, coord.y * Mathf.Max(1, chunkSizeZ));
    public int GetChunkSizeX() => Mathf.Max(1, chunkSizeX);
    public int GetChunkSizeZ() => Mathf.Max(1, chunkSizeZ);
    public int GetWorldHeight() => Mathf.Max(1, worldHeight);
    public List<Vector2Int> SnapshotLoadedChunkCoords()
    {
        // Return a snapshot copy to avoid collection modified exceptions during iteration
        return new List<Vector2Int>(_chunks.Keys);
    }
    public string GetBiomeAt(Vector3 worldPos)
    {
        // Simple plains biome - can be expanded for multiple biomes in the future
        return "Plains";
    }

    private void AutoFindPlayer()
    {
        if (player != null) return;
        var fpc = FindFirstObjectByType<FirstPersonController>(FindObjectsInactive.Exclude);
        if (fpc != null) { player = fpc.transform; return; }
        var pc = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Exclude);
        if (pc != null) { player = pc.transform; return; }
        var cam = Camera.main; if (cam != null) player = cam.transform;
    }

    private Vector2Int WorldToChunkCoord(Vector3 position)
    {
        int cx = Mathf.FloorToInt(position.x / Mathf.Max(1, chunkSizeX));
        int cz = Mathf.FloorToInt(position.z / Mathf.Max(1, chunkSizeZ));
        return new Vector2Int(cx, cz);
    }

    private Vector2Int WorldToChunkCoord(Vector3Int wpos)
    {
        return new Vector2Int(Mathf.FloorToInt((float)wpos.x / Mathf.Max(1, chunkSizeX)), Mathf.FloorToInt((float)wpos.z / Mathf.Max(1, chunkSizeZ)));
    }

    private void UpdateStreaming(bool force = false)
    {
        if (player == null) return;
        EnsureChunksRoot();

        var center = WorldToChunkCoord(player.position);
        int r = Mathf.Max(1, viewDistanceChunks);

        // Determine required chunk coords in a square region
        var needed = new HashSet<Vector2Int>();
        for (int dz = -r; dz <= r; dz++)
        {
            int rem = r - Mathf.Abs(dz);
            for (int dx = -rem; dx <= rem; dx++)
            {
                needed.Add(new Vector2Int(center.x + dx, center.y + dz));
            }
        }

        // Unload chunks not needed
        var toUnload = new List<Vector2Int>();
        foreach (var kv in _chunks)
        {
            if (!needed.Contains(kv.Key)) toUnload.Add(kv.Key);
        }
        foreach (var c in toUnload)
        {
            // Persist edits before unloading
            if (enableChunkPersistence) SaveChunkToDisk(c);
            _chunks[c].Unload(this);
            _chunks.Remove(c);
        }

        // Enqueue missing chunks for background loading
        foreach (var c in needed)
        {
            if (_chunks.ContainsKey(c) || _loading.Contains(c) || _queued.Contains(c)) continue;
            _pendingLoads.Enqueue(c);
            _queued.Add(c);
        }
    }

    private IEnumerator LoadChunkRoutine(Vector2Int coord)
    {
    // Create chunk and generate data with small yields to avoid spikes
        var chunk = new WorldGeneration.Chunks.Chunk(coord, chunkSizeX, worldHeight, chunkSizeZ, _chunksRoot);

        // Determine vertical budget for this chunk by scanning surface heights
        int maxTop = 0;
        for (int lx = 0; lx < chunkSizeX; lx++)
        {
            int worldX = coord.x * chunkSizeX + lx;
            for (int lz = 0; lz < chunkSizeZ; lz++)
            {
                int worldZ = coord.y * chunkSizeZ + lz;
                int top = GetColumnTopY(worldX, worldZ);
                if (top > maxTop) maxTop = top;
            }
        }
        int usefulY = Mathf.Min(worldHeight, maxTop + 1); // +1 because top is zero-based
        usefulY = Mathf.Max(1, usefulY);

        for (int lx = 0; lx < chunkSizeX; lx++)
        {
            for (int lz = 0; lz < chunkSizeZ; lz++)
            {
                // Column-specific top to avoid iterating unnecessary upper air cells
                int columnTop = GetColumnTopY(coord.x * chunkSizeX + lx, coord.y * chunkSizeZ + lz);
                int columnMaxY = Mathf.Min(worldHeight - 1, columnTop);
                for (int ly = 0; ly <= columnMaxY; ly++)
                {
                    var wp = new Vector3Int(coord.x * chunkSizeX + lx, ly, coord.y * chunkSizeZ + lz);
                    chunk.SetLocal(lx, ly, lz, GenerateBlockTypeAt(wp));
                }
            }
            if ((lx & 3) == 0) yield return null; // yield every few columns
        }

        // Apply persisted edits (disk then memory)
        if (enableChunkPersistence)
        {
            LoadChunkFromDiskInto(coord, chunk);
            if (_chunkEdits.TryGetValue(coord, out var edits))
            {
                foreach (var kv in edits)
                {
                    var lp = chunk.WorldToLocal(kv.Key);
                    if (lp.x >= 0 && lp.x < chunk.sizeX && lp.y >= 0 && lp.y < chunk.sizeY && lp.z >= 0 && lp.z < chunk.sizeZ)
                    {
                        chunk.SetLocal(lp.x, lp.y, lp.z, kv.Value);
                    }
                }
            }
        }

        // Trees (before meshing so logs/leaves are part of mesh). Only procedural if not already edited at those cells.
        if (enableTrees)
        {
            _isProceduralBatch = true;
            _batchDirtyChunks.Clear();
            yield return GenerateTreesInChunkRoutine(chunk);
            _isProceduralBatch = false;
        }

    // Register chunk early so GetBlockType works during spawning logic
    _chunks[coord] = chunk;

    if (useChunkMeshing)
        {
            // Use reflection to avoid compile-order/type resolution issues
            var chunkType = ResolveTypeByName("WorldGeneration.Chunks.Chunk");
            var builderType = ResolveTypeByName("WorldGeneration.Chunks.ChunkMeshBuilder");
            if (builderType != null)
            {
                var build = builderType.GetMethod(
                    "BuildMesh",
                    new System.Type[] { typeof(WorldGenerator), chunkType, typeof(bool) }
                );
                if (build != null)
                {
                    build.Invoke(null, new object[] { this, chunk, addChunkCollider });
                }
            }

            // Spawn plants in batched renderer for very lightweight loads
            BuildChunkPlants(chunk, usefulY);

            // After meshing this chunk, rebuild any neighbor chunks we touched during batched generation
            if (_batchDirtyChunks.Count > 0)
            {
                foreach (var dc in _batchDirtyChunks)
                {
                    if (dc != coord)
                    {
                        // Defer to frame budget to reduce spikes
                        _deferredRebuild.Add(dc);
                    }
                }
                _batchDirtyChunks.Clear();
            }

            // Also request immediate neighbors to rebuild to refresh border culling (cheap, deferred)
            Vector2Int[] ortho = { new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) };
            foreach (var o in ortho)
            {
                var nc = new Vector2Int(coord.x + o.x, coord.y + o.y);
                if (_chunks.ContainsKey(nc)) _deferredRebuild.Add(nc);
            }
        }
        else
        {
            // Build visible with yields (GameObject per block)
            for (int lx = 0; lx < chunkSizeX; lx++)
            {
                for (int ly = 0; ly < usefulY; ly++)
                {
                    for (int lz = 0; lz < chunkSizeZ; lz++)
                    {
                        var t = chunk.GetLocal(lx, ly, lz);
                        if (t == BlockType.Air) continue;
                        var wp = new Vector3Int(coord.x * chunkSizeX + lx, ly, coord.y * chunkSizeZ + lz);
                        if (ShouldRenderBlock(wp.x, wp.y, wp.z))
                        {
                            CreateBlock(wp, t, chunk.parent);
                        }
                    }
                }
                if ((lx & 3) == 0) yield return null; // yield periodically
            }

            // Spawn plants on exposed grass; coarse pass with yields
            for (int lx = 0; lx < chunkSizeX; lx++)
            {
                for (int lz = 0; lz < chunkSizeZ; lz++)
                {
                    for (int ly = 1; ly < usefulY; ly++)
                    {
                        var wp = new Vector3Int(coord.x * chunkSizeX + lx, ly, coord.y * chunkSizeZ + lz);
                        if (GetBlockType(wp) == BlockType.Grass && GetBlockType(wp + Vector3Int.up) == BlockType.Air)
                        {
                            if (ShouldRenderBlock(wp.x, wp.y, wp.z))
                            {
                                TrySpawnPlantClusterAt(wp, chunk.parent);
                            }
                        }
                    }
                }
                if ((lx & 3) == 0) yield return null;
            }
        }

    _loading.Remove(coord);
    }
    
    private IEnumerator LoadChunkRoutineOptimized(Vector2Int coord)
    {
        // No LOD system - always use full detail

        // Create chunk
        var chunk = new WorldGeneration.Chunks.Chunk(coord, chunkSizeX, worldHeight, chunkSizeZ, _chunksRoot);

        // Priority 1: Use Job System for fastest generation (if enabled)
        if (useJobSystem)
        {
            yield return StartCoroutine(GenerateChunkWithJobSystem(chunk, coord));
        }
        // Priority 2: Use ultra-smooth generation for virtually zero micro-freezes
        else if (GetComponent<WorldGeneration.Chunks.UltraSmoothChunkGenerator>() != null)
        {
            var ultraSmooth = GetComponent<WorldGeneration.Chunks.UltraSmoothChunkGenerator>();
            yield return StartCoroutine(ultraSmooth.GenerateChunkUltraSmooth(chunk, coord));
        }
        // Priority 3: Use optimizer as fallback
        else
        {
            var optimizer = GetComponent<WorldGeneration.Chunks.ChunkGenerationOptimizer>();
            if (optimizer != null)
            {
                yield return StartCoroutine(optimizer.GenerateChunkOptimized(chunk, coord));
            }
            else
            {
                // Final fallback to traditional generation
                yield return StartCoroutine(GenerateChunkTraditionalOptimized(chunk, coord));
            }
        }

        // Apply persisted edits
        if (enableChunkPersistence)
        {
            LoadChunkFromDiskInto(coord, chunk);
            if (_chunkEdits.TryGetValue(coord, out var edits))
            {
                foreach (var kv in edits)
                {
                    var lp = chunk.WorldToLocal(kv.Key);
                    if (lp.x >= 0 && lp.x < chunk.sizeX && lp.y >= 0 && lp.y < chunk.sizeY && lp.z >= 0 && lp.z < chunk.sizeZ)
                    {
                        chunk.SetLocal(lp.x, lp.y, lp.z, kv.Value);
                    }
                }
            }
        }

        // Trees generation
        if (enableTrees)
        {
            _isProceduralBatch = true;
            _batchDirtyChunks.Clear();
            yield return GenerateTreesInChunkRoutine(chunk);
            _isProceduralBatch = false;
        }

        // Register chunk early
        _chunks[coord] = chunk;

        // Build mesh with LOD optimization
        if (useChunkMeshing)
        {
            BuildChunkMesh(chunk);
            
            // Build plants
            int usefulY = Mathf.Clamp(chunk.sizeY, 1, worldHeight);
            BuildChunkPlants(chunk, usefulY);
            
            // Handle neighbor rebuilds
            HandleNeighborRebuilds(coord);
        }
        else
        {
            // Traditional visible block building with LOD filtering
            yield return StartCoroutine(BuildVisible(chunk));
        }

        _loading.Remove(coord);
    }

    private IEnumerator GenerateChunkWithJobSystem(WorldGeneration.Chunks.Chunk chunk, Vector2Int coord)
    {
        // For now, use the optimized fallback system until Unity Jobs package is properly installed
        yield return StartCoroutine(WorldGeneration.Chunks.ChunkGenerationFallback.GenerateChunkAsync(
            chunk, coord, chunkSizeX, worldHeight, chunkSizeZ, worldSeed, enableTunnels, tunnelSettings));
    }

    private IEnumerator GenerateChunkTraditional(WorldGeneration.Chunks.Chunk chunk, Vector2Int coord)
    {
        // Original generation logic with yields
        for (int lx = 0; lx < chunkSizeX; lx++)
        {
            for (int lz = 0; lz < chunkSizeZ; lz++)
            {
                int columnTop = GetColumnTopY(coord.x * chunkSizeX + lx, coord.y * chunkSizeZ + lz);
                int columnMaxY = Mathf.Min(worldHeight - 1, columnTop);
                for (int ly = 0; ly <= columnMaxY; ly++)
                {
                    var wp = new Vector3Int(coord.x * chunkSizeX + lx, ly, coord.y * chunkSizeZ + lz);
                    chunk.SetLocal(lx, ly, lz, GenerateBlockTypeAt(wp));
                }
            }
            if ((lx & 3) == 0) yield return null;
        }
    }
    
    private IEnumerator GenerateChunkTraditionalOptimized(WorldGeneration.Chunks.Chunk chunk, Vector2Int coord)
    {
        // Optimized generation with more frequent yields to prevent FPS drops
        float frameStartTime = Time.realtimeSinceStartup;
        int processedBlocks = 0;
        
        for (int lx = 0; lx < chunkSizeX; lx++)
        {
            for (int lz = 0; lz < chunkSizeZ; lz++)
            {
                int columnTop = GetColumnTopY(coord.x * chunkSizeX + lx, coord.y * chunkSizeZ + lz);
                int columnMaxY = Mathf.Min(worldHeight - 1, columnTop);
                for (int ly = 0; ly <= columnMaxY; ly++)
                {
                    var wp = new Vector3Int(coord.x * chunkSizeX + lx, ly, coord.y * chunkSizeZ + lz);
                    chunk.SetLocal(lx, ly, lz, GenerateBlockTypeAt(wp));
                    processedBlocks++;
                    
                    // Yield very frequently to eliminate micro-freezes
                    if (processedBlocks >= 50 || (Time.realtimeSinceStartup - frameStartTime) * 1000f > 1f)
                    {
                        yield return null;
                        frameStartTime = Time.realtimeSinceStartup;
                        processedBlocks = 0;
                    }
                    
                    // Extra yield every 500 blocks for faster generation
                    if (processedBlocks % 500 == 0)
                    {
                        yield return null;
                        frameStartTime = Time.realtimeSinceStartup;
                    }
                }
            }
        }
    }

    private void BuildChunkMesh(WorldGeneration.Chunks.Chunk chunk)
    {
        // Standard BuildMesh call
        WorldGeneration.Chunks.ChunkMeshBuilder.BuildMesh(this, chunk, addChunkCollider);
    }

    private IEnumerator BuildVisible(WorldGeneration.Chunks.Chunk chunk)
    {
        int usefulY = Mathf.Clamp(chunk.sizeY, 1, worldHeight);
        
        for (int lx = 0; lx < chunkSizeX; lx++)
        {
            for (int ly = 0; ly < usefulY; ly++)
            {
                for (int lz = 0; lz < chunkSizeZ; lz++)
                {
                    var t = chunk.GetLocal(lx, ly, lz);
                    if (t == BlockType.Air) continue;
                    
                    // No LOD filtering - render all blocks
                    
                    var wp = new Vector3Int(chunk.coord.x * chunkSizeX + lx, ly, chunk.coord.y * chunkSizeZ + lz);
                    if (ShouldRenderBlock(wp.x, wp.y, wp.z))
                    {
                        CreateBlock(wp, t, chunk.parent);
                    }
                }
            }
            if ((lx & 3) == 0) yield return null;
        }
    }

    private void HandleNeighborRebuilds(Vector2Int coord)
    {
        if (_batchDirtyChunks.Count > 0)
        {
            foreach (var dc in _batchDirtyChunks)
            {
                if (dc != coord)
                {
                    _deferredRebuild.Add(dc);
                }
            }
            _batchDirtyChunks.Clear();
        }

        // Request neighbor chunks to rebuild for proper border culling
        Vector2Int[] ortho = { new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) };
        foreach (var o in ortho)
        {
            var nc = new Vector2Int(coord.x + o.x, coord.y + o.y);
            if (_chunks.ContainsKey(nc)) _deferredRebuild.Add(nc);
        }
    }
    
    void UpdateVisibleBlocks()
    {
        // Clear existing blocks
        foreach (var block in blockObjects.Values)
        {
            if (block != null) Destroy(block);
        }
        blockObjects.Clear();
        // Clear existing plants
        foreach (var plant in plantObjects.Values)
        {
            if (plant != null) Destroy(plant);
        }
        plantObjects.Clear();
        
        // Create new blocks
        for (int x = 0; x < worldWidth; x++)
        {
            for (int y = 0; y < worldHeight; y++)
            {
                for (int z = 0; z < worldDepth; z++)
                {
                    if (worldData[x, y, z] != BlockType.Air && ShouldRenderBlock(x, y, z))
                    {
                        CreateBlock(new Vector3Int(x, y, z), worldData[x, y, z]);
                    }
                }
            }
        }
    }

    // Spawn plants on top of exposed Grass blocks
    void SpawnPlants()
    {
        System.Random rng = new System.Random(12345);

        for (int x = 0; x < worldWidth; x++)
        {
            for (int z = 0; z < worldDepth; z++)
            {
                for (int y = 1; y < worldHeight; y++)
                {
                    var pos = new Vector3Int(x, y, z);
                    // Check block is Grass and the block above is Air
                    if (GetBlockType(pos) == BlockType.Grass && GetBlockType(new Vector3Int(x, y + 1, z)) == BlockType.Air)
                    {
                        // Only if the top face is exposed
                        if (ShouldRenderBlock(x, y, z))
                        {
                            if (HasAnyPlants())
                            {
                                // Interpret plantDensity as expected blades per tile. Values >1 spawn clusters.
                                float desired = Mathf.Max(0f, plantDensity);
                                int count = Mathf.FloorToInt(desired);
                                double remainder = desired - count;
                                if (rng.NextDouble() < remainder) count++;
                                if (count > 0)
                                {
                                    var parent = new GameObject($"Plants ({x},{y+1},{z})");
                                    parent.transform.parent = this.transform;
                                    // place on exact top face center using renderer bounds if available
                    // Default: stand just above the top face of the grass block
                    Vector3 placePos = new Vector3(x + 0.5f, y + 1.001f, z + 0.5f);
                                    if (blockObjects.TryGetValue(new Vector3Int(x, y, z), out var grassGo))
                                    {
                                        var rend = grassGo.GetComponent<Renderer>();
                                        if (rend != null)
                                        {
                                            var b = rend.bounds;
                        placePos = new Vector3(b.center.x, b.max.y + 0.001f, b.center.z);
                                        }
                                    }
                                    parent.transform.position = placePos;
                                    // Pick a plant definition from DB + runtime extras
                                    var def = WG_PickPlantByWeight(rng);
                                    var tex = PlantDef_GetTexture(def);
                                    if (tex != null)
                                    {
                                        var hr = PlantDef_GetHeightRange(def);
                                        var w = PlantDef_GetWidth(def);
                                        var yo = PlantDef_GetYOffset(def);
                                        SpawnPlantChildrenIntoParent(parent, new Vector3Int(x, y, z), count, rng, tex, hr, w, yo);
                                    }
                                    plantObjects[new Vector3Int(x, y + 1, z)] = parent;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public bool HasAnyPlants()
    {
        // Extras-only to avoid unintended textures and pink regressions
        if (_extraPlantDefs.Count > 0) return true;
        return false;
    }
    
    public bool ShouldRenderBlock(int x, int y, int z)
    {
        // Check if block has at least one exposed face
        Vector3Int[] directions = {
            Vector3Int.up, Vector3Int.down,
            Vector3Int.left, Vector3Int.right,
            Vector3Int.forward, Vector3Int.back
        };
        
        foreach (Vector3Int dir in directions)
        {
            Vector3Int neighbor = new Vector3Int(x, y, z) + dir;
            if (IsOutOfBounds(neighbor)) return true;
            var nt = GetBlockType(neighbor);
            if (nt == BlockType.Air || !IsBlockOpaque(nt))
            {
                return true;
            }
        }
        
        return false;
    }
    
    bool IsOutOfBounds(Vector3Int pos)
    {
        if (useChunkStreaming)
        {
            // Infinite world horizontally; only clamp vertically
            return pos.y < 0 || pos.y >= worldHeight;
        }
        else
        {
            return pos.x < 0 || pos.x >= worldWidth ||
                   pos.y < 0 || pos.y >= worldHeight ||
                   pos.z < 0 || pos.z >= worldDepth;
        }
    }
    
    public void CreateBlock(Vector3Int position, BlockType blockType)
    {
        if (blockPrefab == null) return;

        if (blockType == BlockType.Grass)
        {
            // Grass block is special: create a parent and then individual faces
            GameObject grassBlockParent = new GameObject($"{BlockDatabase.GetBlockData(blockType).blockName} ({position.x},{position.y},{position.z})");
            grassBlockParent.transform.position = position;
            grassBlockParent.transform.parent = transform;

            // Define faces: 0=up, 1=down, 2=left, 3=right, 4=forward, 5=back
            Vector3[] faceNormals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            Material[] faceMaterials = {
                GetBlockMaterial(BlockType.Grass), // Top
                GetBlockMaterial(BlockType.Dirt),  // Bottom
                GetBlockMaterial(BlockType.Dirt), // Side
                GetBlockMaterial(BlockType.Dirt), // Side
                GetBlockMaterial(BlockType.Dirt), // Side
                GetBlockMaterial(BlockType.Dirt)  // Side
            };
            Texture2D[] faceTextures = {
                grassTexture,
                dirtTexture,
                grassSideTexture,
                grassSideTexture,
                grassSideTexture,
                grassSideTexture
            };

            for (int i = 0; i < 6; i++)
            {
                Vector3Int neighborPos = position + Vector3Int.RoundToInt(faceNormals[i]);
                if (IsOutOfBounds(neighborPos) || GetBlockType(neighborPos) == BlockType.Air)
                {
                    GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    face.transform.SetParent(grassBlockParent.transform, false);
                    face.transform.position = position + faceNormals[i] * 0.5f;
                    face.transform.rotation = Quaternion.LookRotation(-faceNormals[i]);
                    
                    Renderer faceRenderer = face.GetComponent<Renderer>();
                    if (faceRenderer != null)
                    {
                        Material mat = CreateVariationMaterial(faceMaterials[i], position);
                        mat.mainTexture = faceTextures[i];
                        mat.SetTexture("_BaseMap", faceTextures[i]);
                        faceRenderer.material = mat;
                    }
                    Destroy(face.GetComponent<Collider>());
                }
            }
            blockObjects[position] = grassBlockParent;
        }
        else if (blockType == BlockType.CraftingTable)
        {
            // Crafting table has different textures for different faces
            GameObject craftingTableParent = new GameObject($"{BlockDatabase.GetBlockData(blockType).blockName} ({position.x},{position.y},{position.z})");
            craftingTableParent.transform.position = position;
            craftingTableParent.transform.parent = transform;

            // Define faces: 0=up, 1=down, 2=left, 3=right, 4=forward, 5=back
            Vector3[] faceNormals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            Material[] faceMaterials = {
                GetBlockMaterial(BlockType.CraftingTable), // Top
                GetBlockMaterial(BlockType.WoodPlanks),    // Bottom (wood planks)
                GetBlockMaterial(BlockType.CraftingTable), // Side
                GetBlockMaterial(BlockType.CraftingTable), // Side
                GetBlockMaterial(BlockType.CraftingTable), // Front
                GetBlockMaterial(BlockType.CraftingTable)  // Back
            };
            Texture2D[] faceTextures = {
                craftingTableTexture,      // Top
                woodPlanksTexture,         // Bottom (wood planks)
                craftingTableSideTexture,  // Left side
                craftingTableSideTexture,  // Right side  
                craftingTableFrontTexture, // Front
                craftingTableSideTexture   // Back
            };

            for (int i = 0; i < 6; i++)
            {
                Vector3Int neighborPos = position + Vector3Int.RoundToInt(faceNormals[i]);
                if (IsOutOfBounds(neighborPos) || GetBlockType(neighborPos) == BlockType.Air)
                {
                    GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    face.transform.SetParent(craftingTableParent.transform, false);
                    face.transform.position = position + faceNormals[i] * 0.5f;
                    face.transform.rotation = Quaternion.LookRotation(-faceNormals[i]);
                    
                    Renderer faceRenderer = face.GetComponent<Renderer>();
                    if (faceRenderer != null)
                    {
                        Material mat = CreateVariationMaterial(faceMaterials[i], position);
                        mat.mainTexture = faceTextures[i];
                        mat.SetTexture("_BaseMap", faceTextures[i]);
                        faceRenderer.material = mat;
                    }
                    Destroy(face.GetComponent<Collider>());
                }
            }
            blockObjects[position] = craftingTableParent;
        }
        else
        {
            GameObject block = Instantiate(blockPrefab, position, Quaternion.identity, transform);
            block.name = $"{BlockDatabase.GetBlockData(blockType).blockName} ({position.x},{position.y},{position.z})";
            
            Renderer renderer = block.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material baseMaterial = blockMaterials[(int)blockType];
                if (baseMaterial != null)
                {
                    Material instanceMaterial = CreateVariationMaterial(baseMaterial, position);
                    renderer.material = instanceMaterial;
                }
            }
            blockObjects[position] = block;
        }

        // Add BlockInfo component to the root block object
        GameObject rootObject = blockObjects[position];
        BlockInfo blockInfo = rootObject.GetComponent<BlockInfo>();
        if (blockInfo == null)
        {
            blockInfo = rootObject.AddComponent<BlockInfo>();
        }
        blockInfo.blockType = blockType;
        blockInfo.position = position;
    }

    // Overload to allow parenting under a chunk parent
    public void CreateBlock(Vector3Int position, BlockType blockType, Transform parentOverride)
    {
        if (blockPrefab == null) return;

        Transform p = parentOverride != null ? parentOverride : this.transform;
        
        if (blockType == BlockType.Grass)
        {
            GameObject grassBlockParent = new GameObject($"{BlockDatabase.GetBlockData(blockType).blockName} ({position.x},{position.y},{position.z})");
            grassBlockParent.transform.position = position;
            grassBlockParent.transform.SetParent(p, true);

            Vector3[] faceNormals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            Material[] faceMaterials = {
                GetBlockMaterial(BlockType.Grass), // Top
                GetBlockMaterial(BlockType.Dirt),  // Bottom
                GetBlockMaterial(BlockType.Dirt), // Side
                GetBlockMaterial(BlockType.Dirt), // Side
                GetBlockMaterial(BlockType.Dirt), // Side
                GetBlockMaterial(BlockType.Dirt)  // Side
            };
            Texture2D[] faceTextures = {
                grassTexture,
                dirtTexture,
                grassSideTexture,
                grassSideTexture,
                grassSideTexture,
                grassSideTexture
            };

            for (int i = 0; i < 6; i++)
            {
                 Vector3Int neighborPos = position + Vector3Int.RoundToInt(faceNormals[i]);
                if (IsOutOfBounds(neighborPos) || GetBlockType(neighborPos) == BlockType.Air)
                {
                    GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    face.transform.SetParent(grassBlockParent.transform, false);
                    face.transform.position = position + faceNormals[i] * 0.5f;
                    face.transform.rotation = Quaternion.LookRotation(-faceNormals[i]);
                    
                    Renderer faceRenderer = face.GetComponent<Renderer>();
                    if (faceRenderer != null)
                    {
                        Material mat = CreateVariationMaterial(faceMaterials[i], position);
                        mat.mainTexture = faceTextures[i];
                        mat.SetTexture("_BaseMap", faceTextures[i]);
                        faceRenderer.material = mat;
                    }
                    Destroy(face.GetComponent<Collider>());
                }
            }
            blockObjects[position] = grassBlockParent;
        }
        else if (blockType == BlockType.CraftingTable)
        {
            // Crafting table has different textures for different faces
            GameObject craftingTableParent = new GameObject($"{BlockDatabase.GetBlockData(blockType).blockName} ({position.x},{position.y},{position.z})");
            craftingTableParent.transform.position = position;
            craftingTableParent.transform.SetParent(p, true);

            // Define faces: 0=up, 1=down, 2=left, 3=right, 4=forward, 5=back
            Vector3[] faceNormals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            Material[] faceMaterials = {
                GetBlockMaterial(BlockType.CraftingTable), // Top
                GetBlockMaterial(BlockType.WoodPlanks),    // Bottom (wood planks)
                GetBlockMaterial(BlockType.CraftingTable), // Side
                GetBlockMaterial(BlockType.CraftingTable), // Side
                GetBlockMaterial(BlockType.CraftingTable), // Front
                GetBlockMaterial(BlockType.CraftingTable)  // Back
            };
            Texture2D[] faceTextures = {
                craftingTableTexture,      // Top
                woodPlanksTexture,         // Bottom (wood planks)
                craftingTableSideTexture,  // Left side
                craftingTableSideTexture,  // Right side  
                craftingTableFrontTexture, // Front
                craftingTableSideTexture   // Back
            };

            for (int i = 0; i < 6; i++)
            {
                Vector3Int neighborPos = position + Vector3Int.RoundToInt(faceNormals[i]);
                if (IsOutOfBounds(neighborPos) || GetBlockType(neighborPos) == BlockType.Air)
                {
                    GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    face.transform.SetParent(craftingTableParent.transform, false);
                    face.transform.position = position + faceNormals[i] * 0.5f;
                    face.transform.rotation = Quaternion.LookRotation(-faceNormals[i]);
                    
                    Renderer faceRenderer = face.GetComponent<Renderer>();
                    if (faceRenderer != null)
                    {
                        Material mat = CreateVariationMaterial(faceMaterials[i], position);
                        mat.mainTexture = faceTextures[i];
                        mat.SetTexture("_BaseMap", faceTextures[i]);
                        faceRenderer.material = mat;
                    }
                    Destroy(face.GetComponent<Collider>());
                }
            }
            blockObjects[position] = craftingTableParent;
        }
        else
        {
            GameObject block = Instantiate(blockPrefab, position, Quaternion.identity, p);
            block.name = $"{BlockDatabase.GetBlockData(blockType).blockName} ({position.x},{position.y},{position.z})";

            Renderer renderer = block.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material baseMaterial = blockMaterials[(int)blockType];
                if (baseMaterial != null)
                {
                    Material instanceMaterial = CreateVariationMaterial(baseMaterial, position);
                    renderer.material = instanceMaterial;
                }
            }
            blockObjects[position] = block;
        }

        // Add BlockInfo component
        GameObject rootObject = blockObjects[position];
        BlockInfo blockInfo = rootObject.GetComponent<BlockInfo>();
        if (blockInfo == null)
        {
            blockInfo = rootObject.AddComponent<BlockInfo>();
        }
        blockInfo.blockType = blockType;
        blockInfo.position = position;
    }
    
    Material CreateVariationMaterial(Material baseMaterial, Vector3Int position)
    {
        // Create an instance of the material
        Material instanceMaterial = new Material(baseMaterial);
        
        // Create position-based seed for consistent variation
        int seed = position.x * 73856093 + position.y * 19349663 + position.z * 83492791;
        System.Random posRandom = new System.Random(seed);
        
        // Apply subtle scale variations
        float scaleVariation = 1.0f + (posRandom.Next(-10, 11) * 0.01f * textureScaleVariation);
        instanceMaterial.mainTextureScale = Vector2.one * scaleVariation;
        
        // Apply small random texture offset to break up tiling patterns
        float offsetX = (float)posRandom.NextDouble() * textureOffsetVariation;
        float offsetY = (float)posRandom.NextDouble() * textureOffsetVariation;
        instanceMaterial.mainTextureOffset = new Vector2(offsetX, offsetY);
        
        // The color is already set on the base material, so no need to change it
        // float colorVariation = 0.95f + (float)posRandom.NextDouble() * 0.1f; // 95% to 105%
        // Color baseColor = baseMaterial.color;
        // instanceMaterial.color = new Color(
        //     baseColor.r * colorVariation,
        //     baseColor.g * colorVariation,
        //     baseColor.b * colorVariation,
        //     baseColor.a
        // );
        
        return instanceMaterial;
    }
    
    public BlockType GetBlockType(Vector3Int position)
    {
        if (useChunkStreaming)
        {
            if (IsOutOfBounds(position)) return BlockType.Air;
            var cc = WorldToChunkCoord(position);
            if (_chunks.TryGetValue(cc, out var chunk))
            {
                var lp = chunk.WorldToLocal(position);
                return chunk.GetLocal(lp.x, lp.y, lp.z);
            }
            // Not loaded: treat as Air for visibility; if needed, could compute procedural type
            return BlockType.Air;
        }
        else
        {
            if (IsOutOfBounds(position)) return BlockType.Air;
            return worldData[position.x, position.y, position.z];
        }
    }

    // Chunk-aware peek used during generation within an owning chunk.
    // Falls back to loaded chunks or world data; never forces a load.
    private BlockType PeekBlockForGeneration(Vector3Int position, WorldGeneration.Chunks.Chunk owningChunk)
    {
        if (useChunkStreaming)
        {
            if (IsOutOfBounds(position)) return BlockType.Air;
            var cc = WorldToChunkCoord(position);
            if (owningChunk != null && owningChunk.coord == cc)
            {
                var lp = owningChunk.WorldToLocal(position);
                return owningChunk.GetLocal(lp.x, lp.y, lp.z);
            }
            if (_chunks.TryGetValue(cc, out var ch))
            {
                var lp = ch.WorldToLocal(position);
                return ch.GetLocal(lp.x, lp.y, lp.z);
            }
            return BlockType.Air;
        }
        else
        {
            if (IsOutOfBounds(position)) return BlockType.Air;
            return worldData[position.x, position.y, position.z];
        }
    }
    
    public bool PlaceBlock(Vector3Int position, BlockType blockType)
    {
        if (IsOutOfBounds(position)) return false;

        if (useChunkStreaming)
        {
            var cc = WorldToChunkCoord(position);
            if (!_chunks.TryGetValue(cc, out var chunk))
            {
                // Load the chunk on-demand
                chunk = new WorldGeneration.Chunks.Chunk(cc, chunkSizeX, worldHeight, chunkSizeZ, _chunksRoot);
                chunk.GenerateSuperflat(this);
                _chunks[cc] = chunk;
            }
            var lp = chunk.WorldToLocal(position);
            chunk.SetLocal(lp.x, lp.y, lp.z, blockType);
            if (enableChunkPersistence && !string.IsNullOrEmpty(currentWorldName))
            {
                if (!_chunkEdits.TryGetValue(cc, out var map)) { map = new Dictionary<Vector3Int, BlockType>(); _chunkEdits[cc] = map; }
                map[position] = blockType;
                Debug.Log($"WorldGenerator: Recorded block change at {position} to {blockType} in world '{currentWorldName}' (Chunk {cc})");
            }
            else
            {
                Debug.LogWarning($"WorldGenerator: Block change at {position} NOT recorded - persistence disabled: {!enableChunkPersistence} or world name empty: '{currentWorldName}'");
            }
        }
        else
        {
            worldData[position.x, position.y, position.z] = blockType;
        }

        // If a block is placed where a plant currently exists, remove the plant at that cell
        if (blockType != BlockType.Air)
        {
            // Non-meshing: legacy GameObject plants
            if (plantObjects.TryGetValue(position, out var plantAtPos))
            {
                if (plantAtPos != null) Destroy(plantAtPos);
                plantObjects.Remove(position);
            }
            // Meshing: remove batched plant at this cell if present
            if (useChunkStreaming && useChunkMeshing)
            {
                if (HasBatchedPlantAt(position))
                {
                    RemoveBatchedPlantAt(position);
                }
            }
        }

    // If the support block is not Grass anymore (including Air), remove plant above it
    var above = position + Vector3Int.up;
    if (blockType != BlockType.Grass)
        {
            if (plantObjects.TryGetValue(above, out var plantAbove))
            {
                if (plantAbove != null) Destroy(plantAbove);
                plantObjects.Remove(above);
            }
            if (useChunkStreaming && useChunkMeshing)
            {
                if (HasBatchedPlantAt(above))
                {
                    RemoveBatchedPlantAt(above);
                }
            }
        }

    // If placing a non-air block and the block below is Grass, convert it to Dirt
        if (blockType != BlockType.Air)
        {
            var below = position + Vector3Int.down;
            if (!IsOutOfBounds(below) && GetBlockType(below) == BlockType.Grass)
            {
                if (useChunkStreaming)
                {
                    var ccBelow = WorldToChunkCoord(below);
                    if (_chunks.TryGetValue(ccBelow, out var chBelow))
                    {
                        var lpBelow = chBelow.WorldToLocal(below);
                        chBelow.SetLocal(lpBelow.x, lpBelow.y, lpBelow.z, BlockType.Dirt);
                        if (enableChunkPersistence)
                        {
                            var ccb = WorldToChunkCoord(below);
                            if (!_chunkEdits.TryGetValue(ccb, out var mapBelow)) { mapBelow = new Dictionary<Vector3Int, BlockType>(); _chunkEdits[ccb] = mapBelow; }
                            mapBelow[below] = BlockType.Dirt;
                        }
                    }
                }
                else
                {
                    worldData[below.x, below.y, below.z] = BlockType.Dirt;
                }
                // Remove any plant above that grass cell
                var aboveBelow = below + Vector3Int.up;
                if (plantObjects.TryGetValue(aboveBelow, out var plantAtAboveBelow))
                {
                    if (plantAtAboveBelow != null) Destroy(plantAtAboveBelow);
                    plantObjects.Remove(aboveBelow);
                }
                if (useChunkStreaming && useChunkMeshing)
                {
                    if (HasBatchedPlantAt(aboveBelow))
                    {
                        RemoveBatchedPlantAt(aboveBelow);
                    }
                }
                _scheduledPlantCells.Remove(below);
                // Refresh visuals for the converted block
                if (!useChunkStreaming || !useChunkMeshing)
                {
                    if (blockObjects.ContainsKey(below))
                    {
                        Destroy(blockObjects[below]);
                        blockObjects.Remove(below);
                    }
                    CreateBlock(below, BlockType.Dirt);
                }
            }
            // If covering exposed Dirt, cancel growth
            if (!IsOutOfBounds(below) && GetBlockType(below) == BlockType.Dirt)
            {
                _scheduledGrassCells.Remove(below);
            }
        }
        
    if (blockType == BlockType.Air)
        {
            // Remove block
            if (blockObjects.ContainsKey(position))
            {
                Destroy(blockObjects[position]);
                blockObjects.Remove(position);
            }
            // If removing a block exposes Dirt below, schedule grass growth
            var belowIfAny = position + Vector3Int.down;
            if (!IsOutOfBounds(belowIfAny) && GetBlockType(belowIfAny) == BlockType.Dirt)
            {
                // The cell we just made Air exposes 'belowIfAny' to sky
                if (GetBlockType(position) == BlockType.Air)
                {
                    ScheduleGrassGrowth(belowIfAny, dirtToGrassDelayTicks);
                }
            }
        }
        else
        {
            // Place block
            if (useChunkStreaming)
            {
                if (!useChunkMeshing)
                {
                    var cc = WorldToChunkCoord(position);
                    var parent = _chunks.TryGetValue(cc, out var ch) ? ch.parent : this.transform;
                    CreateBlock(position, blockType, parent);
                }
                // If meshing, we'll rebuild chunk mesh below
            }
            else
            {
                CreateBlock(position, blockType);
            }
        }

        // If we placed Grass with Air above, schedule delayed plant spawn at this position
    if (blockType == BlockType.Grass)
        {
            if (GetBlockType(above) == BlockType.Air)
            {
                SchedulePlantRespawn(position, newGrassPlantDelayTicks);
            }
        }
        // Side neighbors: if any Dirt neighbor is now exposed (air above it), schedule growth
        if (blockType != BlockType.Air)
        {
            Vector3Int[] lateral = { Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back };
            foreach (var d in lateral)
            {
                var n = position + d;
                if (!IsOutOfBounds(n) && GetBlockType(n) == BlockType.Dirt && GetBlockType(n + Vector3Int.up) == BlockType.Air)
                {
                    ScheduleGrassGrowth(n, dirtToGrassDelayTicks);
                }
            }
        }
        // If we placed Dirt with Air above, schedule grass growth for this cell
        if (blockType == BlockType.Dirt)
        {
            if (GetBlockType(above) == BlockType.Air)
            {
                ScheduleGrassGrowth(position, dirtToGrassDelayTicks);
            }
        }
        
    // Update neighboring blocks visibility / rebuild meshes as needed
    UpdateNeighboringBlocks(position);
        
        
        return true;
    }

    // ----------------- TREE GENERATION (Hytale-style) -----------------
    private IEnumerator GenerateTreesInChunkRoutine(WorldGeneration.Chunks.Chunk chunk)
    {
        if (!enableTrees) yield break;

        // Deterministic world-space sampling across chunk borders.
        int grid = Mathf.Max(2, treeGridSize);

        // Conservative horizontal reach to include neighbors when checking constrain option
        int reach = Mathf.Max(8, branchTipLeafRadius + 6);

        // Chunk world bounds
        int wx0 = chunk.coord.x * chunk.sizeX;
        int wz0 = chunk.coord.y * chunk.sizeZ;
        int wx1 = wx0 + chunk.sizeX - 1;
        int wz1 = wz0 + chunk.sizeZ - 1;

        // Expanded area for candidate trunks (only used to decide candidates; we still emit blocks only within this chunk)
        int minX = wx0 - reach;
        int maxX = wx1 + reach;
        int minZ = wz0 - reach;
        int maxZ = wz1 + reach;

        // Iterate grid cells deterministically
    int workCounter = 0;
    for (int gx = FloorDiv(minX, grid) * grid; gx <= maxX; gx += grid)
        {
            for (int gz = FloorDiv(minZ, grid) * grid; gz <= maxZ; gz += grid)
            {
        // Periodically yield to keep frame time responsive
        if ((workCounter++ & 31) == 0) yield return null;
                // Density modulation using simple terrain noise at cell center
                int cx = gx + grid / 2;
                int cz = gz + grid / 2;
                float baseNoise = Mathf.PerlinNoise((cx + worldSeed) * 0.01f, (cz + worldSeed) * 0.01f); // [0,1]
                float densityScale = Mathf.Clamp01(0.6f + 0.4f * baseNoise);
                // Expected trees per block
                float densityPerBlock = (treesPerChunk <= 0f || chunk.sizeX <= 0 || chunk.sizeZ <= 0)
                    ? 0f
                    : treesPerChunk / (float)(chunk.sizeX * chunk.sizeZ);
                float pCell = Mathf.Clamp01(densityPerBlock * (grid * grid) * densityScale);

                // Deterministic PRNG per cell
                int cellSeed = worldSeed ^ treeSeedOffset ^ (gx * 73856093) ^ (gz * 19349663);
                System.Random cellRng = new System.Random(cellSeed);
                if (cellRng.NextDouble() > pCell) continue; // no tree in this cell

                // Offset trunk inside cell for natural distribution
                int offX = (int)(cellRng.NextDouble() * grid);
                int offZ = (int)(cellRng.NextDouble() * grid);
                int tx = gx + offX;
                int tz = gz + offZ;

                // If constraining inside chunk, skip trunks that can't fit (approx)
        if (constrainTreesInsideChunk)
                {
                    if (tx < wx0 + reach || tx > wx1 - reach || tz < wz0 + reach || tz > wz1 - reach)
            continue;
                }

                // Find ground (grass with air above) at trunk position
                int topY = -1;
                int maxScanY = Mathf.Min(chunk.sizeY - 2, worldHeight - 2);
                for (int y = maxScanY; y >= 1; y--)
                {
                    if (PeekBlockForGeneration(new Vector3Int(tx, y, tz), chunk) == BlockType.Grass &&
                        PeekBlockForGeneration(new Vector3Int(tx, y + 1, tz), chunk) == BlockType.Air)
                    { topY = y; break; }
                }
                if (topY < 0) continue;
                var basePos = new Vector3Int(tx, topY + 1, tz);

                // Optional spacing vs previous trunks (global set for play session)
                if (enforceTreeSpacing)
                {
                    bool tooClose = false;
                    foreach (var p in _treeTrunkPositions)
                    {
                        int manhattan = Mathf.Abs(p.x - basePos.x) + Mathf.Abs(p.z - basePos.z);
                        if (manhattan < treeSpacing) { tooClose = true; break; }
                    }
                    if (tooClose) continue;
                }

                // Per-tree RNG seeded by trunk position
                int treeSeed = worldSeed ^ treeSeedOffset ^ (tx * 951631) ^ (tz * 105467);
                System.Random rng = new System.Random(treeSeed);
                int minH = Mathf.Clamp(minTrunkHeight, 4, 128);
                int maxH = Mathf.Clamp(maxTrunkHeight, minH + 1, 256);
                int trunkH = rng.Next(minH, maxH + 1);
                if (basePos.y + trunkH + 6 >= worldHeight)
                    trunkH = Mathf.Max(minH, worldHeight - basePos.y - 6);
                if (trunkH < minH) continue;

                // Build trunk (emit only cells inside current chunk bounds)
                for (int dy = 0; dy < trunkH; dy++)
                {
                    var wp = new Vector3Int(basePos.x, basePos.y + dy, basePos.z);
                    if (!chunk.ContainsWorldCell(wp)) continue;
                    if (PeekBlockForGeneration(wp, chunk) == BlockType.Air)
                        SetBlockInChunkOrWorld(wp, BlockType.Log, chunk);
                    if ((workCounter++ & 63) == 0) yield return null;
                }

                int trunkTopY = basePos.y + trunkH - 1;
                // Crown bush (allow crossing chunk edges unless explicitly constrained)
                BuildBushAt(new Vector3Int(basePos.x, trunkTopY, basePos.z), 2, chunk);
                if ((workCounter++ & 63) == 0) yield return null;

                // Branches
                int branchCount = rng.Next(2, 5);
                var usedDirs = new HashSet<Vector2Int>();
                var tips = new List<Vector3Int>();

                for (int b = 0; b < branchCount; b++)
                {
                    int minBy = basePos.y + Mathf.Max(2, Mathf.RoundToInt(trunkH * 0.6f));
                    int maxBy = Mathf.Max(minBy, trunkTopY - 2);
                    int by = rng.Next(minBy, maxBy + 1);
                    Vector2Int dir2 = RandomHorizontalDir(rng, usedDirs, allowReuse: false);
                    if (dir2 == Vector2Int.zero) continue;
                    usedDirs.Add(dir2);

                    int horizLen = rng.Next(2, 5);
                    int vertLen = rng.Next(3, 7);
                    int desiredTipMinY = trunkTopY - 1;
                    if (by + vertLen < desiredTipMinY) vertLen += (desiredTipMinY - (by + vertLen));

                    int x = basePos.x; int y = by; int z = basePos.z;
                    Vector3Int lastPos = new Vector3Int(x, y, z);
                    bool aborted = false;

                    // Horizontal
                    for (int i = 0; i < horizLen; i++)
                    {
                        x += dir2.x; z += dir2.y;
                        var wp = new Vector3Int(x, y, z);
                        if (wp.y < 0 || wp.y >= worldHeight) { aborted = true; break; }
                        if (PeekBlockForGeneration(wp, chunk) != BlockType.Air) { aborted = true; break; }
                        SetBlockInChunkOrWorld(wp, BlockType.Log, chunk);
                        lastPos = wp;
                        if ((workCounter++ & 63) == 0) yield return null;
                    }
                    if (aborted)
                    {
                        tips.Add(lastPos);
                        continue;
                    }

                    // Vertical
                    for (int j = 0; j < vertLen; j++)
                    {
                        y += 1;
                        var wp = new Vector3Int(x, y, z);
                        if (wp.y < 0 || wp.y >= worldHeight) { aborted = true; break; }
                        if (PeekBlockForGeneration(wp, chunk) != BlockType.Air) { aborted = true; break; }
                        SetBlockInChunkOrWorld(wp, BlockType.Log, chunk);
                        lastPos = wp;
                        if ((workCounter++ & 63) == 0) yield return null;
                    }
                    tips.Add(lastPos);
                }

                // Tip bushes (allow crossing chunk edges unless explicitly constrained)
                foreach (var tip in tips)
                {
                    BuildBushAt(tip, rng.Next(2, 3), chunk);
                    if ((workCounter++ & 63) == 0) yield return null;
                }

                // Track trunk positions to optionally enforce spacing against later chunks
                _treeTrunkPositions.Add(basePos);
            }
        }
        yield break;
    }

    private int FloorDiv(int a, int b)
    {
        int q = a / b;
        if ((a ^ b) < 0 && a % b != 0) q--;
        return q;
    }

    private void BuildBushClampedToChunk(Vector3Int center, int radius, WorldGeneration.Chunks.Chunk owningChunk)
    {
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    int d2 = dx*dx + dy*dy + dz*dz;
                    if (d2 > r2) continue;
                    var wp = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                    if (wp.y < 0 || wp.y >= worldHeight) continue;
                    if (!owningChunk.ContainsWorldCell(wp)) continue; // clamp to this chunk only
                    if (PeekBlockForGeneration(wp, owningChunk) == BlockType.Air)
                        SetBlockInChunkOrWorld(wp, BlockType.Leaves, owningChunk);
                }
            }
        }
    }

    private void TrySetLog(Vector3Int p, WorldGeneration.Chunks.Chunk owningChunk)
    {
        if (p.y < 0 || p.y >= worldHeight) return;
        if (PeekBlockForGeneration(p, owningChunk) == BlockType.Air)
            SetBlockInChunkOrWorld(p, BlockType.Log, owningChunk);
    }

    private void BuildBushAt(Vector3Int center, int radius, WorldGeneration.Chunks.Chunk owningChunk)
    {
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    int d2 = dx*dx + dy*dy + dz*dz;
                    if (d2 > r2) continue;
                    var wp = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                    if (wp.y < 0 || wp.y >= worldHeight) continue;
                    if (constrainTreesInsideChunk)
                    {
                        var lc = owningChunk.WorldToLocal(wp);
                        if (lc.x < 0 || lc.x >= owningChunk.sizeX || lc.z < 0 || lc.z >= owningChunk.sizeZ) continue;
                    }
                    if (PeekBlockForGeneration(wp, owningChunk) == BlockType.Air)
                        SetBlockInChunkOrWorld(wp, BlockType.Leaves, owningChunk);
                }
            }
        }
    }

    // (top crown uses simple BuildBushAt)

    // (no canopy/capsule helpers needed with edge-bush style)

    private Vector2Int RandomHorizontalDir(System.Random rng, HashSet<Vector2Int> used, bool allowReuse)
    {
        Vector2Int[] dirs = { new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) };
        for (int i = 0; i < 8; i++)
        {
            var d = dirs[rng.Next(0, dirs.Length)];
            if (allowReuse || used == null || !used.Contains(d)) return d;
        }
        return Vector2Int.zero;
    }

    private void BuildBranch(int baseX, int baseY, int baseZ, Vector2Int dir2, int length, int thickness, float upBias, WorldGeneration.Chunks.Chunk owningChunk, List<Vector3Int> tips)
    {
        int y = baseY; int x = baseX; int z = baseZ;
        for (int i = 0; i < length; i++)
        {
            if (upBias > 0f && UnityEngine.Random.value < upBias) y += 1;
            x += dir2.x; z += dir2.y;
            for (int tx = -thickness + 1; tx <= thickness - 1; tx++)
            {
                for (int tz = -thickness + 1; tz <= thickness - 1; tz++)
                {
                    var wp = new Vector3Int(x + tx, y, z + tz);
                    if (wp.y < 0 || wp.y >= worldHeight) continue;
                    if (PeekBlockForGeneration(wp, owningChunk) == BlockType.Air)
                        SetBlockInChunkOrWorld(wp, BlockType.Log, owningChunk);
                }
            }
        }
        if (tips != null)
        {
            tips.Add(new Vector3Int(x, y, z));
        }
    }

    private void SetBlockInChunkOrWorld(Vector3Int wp, BlockType type, WorldGeneration.Chunks.Chunk owningChunk)
    {
        if (useChunkStreaming)
        {
            var cc = WorldToChunkCoord(wp);
            if (_chunks.TryGetValue(cc, out var ch))
            {
                var lp = ch.WorldToLocal(wp);
                ch.SetLocal(lp.x, lp.y, lp.z, type);
                // If we modified a different loaded chunk during generation, delay rebuild in batch mode
                if (useChunkMeshing && (owningChunk == null || ch.coord != owningChunk.coord))
                {
                    if (_isProceduralBatch)
                        _batchDirtyChunks.Add(cc);
                    else
                    {
                        // Queue for deferred rebuild to avoid immediate spikes
                        _deferredRebuild.Add(cc);
                    }
                }
            }
            else if (owningChunk != null && owningChunk.coord == cc)
            {
                var lp = owningChunk.WorldToLocal(wp);
                owningChunk.SetLocal(lp.x, lp.y, lp.z, type);
            }
            else
            {
                // Target chunk not loaded yet: record as a pending edit so it's applied on load
                if (!_chunkEdits.TryGetValue(cc, out var map))
                {
                    map = new Dictionary<Vector3Int, BlockType>();
                    _chunkEdits[cc] = map;
                }
                map[wp] = type;
            }
        }
        else
        {
            if (!IsOutOfBounds(wp))
            {
                worldData[wp.x, wp.y, wp.z] = type;
            }
        }
    }

    private void OnDestroy()
    {
        // Save chunks when WorldGenerator is about to be destroyed (e.g., scene switch)
        SaveAllLoadedChunks();
        Debug.Log("WorldGenerator: OnDestroy - saved chunks before destruction");
    }
    
    private void OnApplicationQuit()
    {
        if (!enableChunkPersistence) return;
        if (string.IsNullOrEmpty(currentWorldName)) return; // Can't save without world name
        
        // Save every loaded chunk's edits to disk
        foreach (var kv in _chunks)
        {
            SaveChunkToDisk(kv.Key);
        }
    }
    
    void UpdateNeighboringBlocks(Vector3Int position)
    {
        if (useChunkStreaming)
        {
            if (useChunkMeshing)
            {
                // Rebuild the chunk containing this cell and any neighboring chunk across boundaries
                var cc = WorldToChunkCoord(position);
                RebuildChunkMeshAt(cc);
                Vector2Int[] neighborOffsets = { new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) };
                foreach (var off in neighborOffsets)
                {
                    var nwp = position + new Vector3Int(off.x * chunkSizeX, 0, off.y * chunkSizeZ);
                    var ncc = WorldToChunkCoord(nwp);
                    if (ncc != cc) RebuildChunkMeshAt(ncc);
                }
                return;
            }
            // Streaming without meshing: operate on per-block GameObjects under chunk parents
        }

        Vector3Int[] directions = {
            Vector3Int.up, Vector3Int.down,
            Vector3Int.left, Vector3Int.right,
            Vector3Int.forward, Vector3Int.back
        };
        
        foreach (Vector3Int dir in directions)
        {
            Vector3Int neighbor = position + dir;
            if (!IsOutOfBounds(neighbor))
            {
                BlockType blockType = GetBlockType(neighbor);
                
                if (blockType != BlockType.Air)
                {
                    if (ShouldRenderBlock(neighbor.x, neighbor.y, neighbor.z))
                    {
                        if (!blockObjects.ContainsKey(neighbor))
                        {
                            if (useChunkStreaming)
                            {
                                var cc = WorldToChunkCoord(neighbor);
                                var parent = _chunks.TryGetValue(cc, out var ch) ? ch.parent : this.transform;
                                CreateBlock(neighbor, blockType, parent);
                            }
                            else
                            {
                                CreateBlock(neighbor, blockType);
                            }
                        }
                        // If this is grass with air above, schedule a delayed plant spawn (on-demand)
                        if (blockType == BlockType.Grass && GetBlockType(neighbor + Vector3Int.up) == BlockType.Air)
                        {
                            SchedulePlantRespawn(neighbor, newGrassPlantDelayTicks);
                        }
                        // If this is dirt with air above, schedule grass growth
                        if (blockType == BlockType.Dirt && GetBlockType(neighbor + Vector3Int.up) == BlockType.Air)
                        {
                            ScheduleGrassGrowth(neighbor, dirtToGrassDelayTicks);
                        }
                    }
                    else
                    {
                        if (blockObjects.ContainsKey(neighbor))
                        {
                            Destroy(blockObjects[neighbor]);
                            blockObjects.Remove(neighbor);
                        }
                        // remove plant above if exists
                        if (plantObjects.ContainsKey(neighbor + Vector3Int.up))
                        {
                            Destroy(plantObjects[neighbor + Vector3Int.up]);
                            plantObjects.Remove(neighbor + Vector3Int.up);
                        }
                        // also if neighbor became non-grass or air, ensure plant above is cleared
                        var neighborType = GetBlockType(neighbor);
                        if (neighborType == BlockType.Air || neighborType != BlockType.Grass)
                        {
                            var posAbove = neighbor + Vector3Int.up;
                            if (plantObjects.TryGetValue(posAbove, out var p))
                            {
                                if (p != null) Destroy(p);
                                plantObjects.Remove(posAbove);
                            }
                        }
                    }
                }
            }
        }
    }

    private void RebuildChunkMeshAt(Vector2Int coord)
    {
        if (!_chunks.TryGetValue(coord, out var chunk)) return;
        var builderType = ResolveTypeByName("WorldGeneration.Chunks.ChunkMeshBuilder");
        var chunkType = ResolveTypeByName("WorldGeneration.Chunks.Chunk");
        if (builderType == null || chunkType == null) return;
        var build = builderType.GetMethod("BuildMesh", new System.Type[] { typeof(WorldGenerator), chunkType, typeof(bool) });
        if (build == null) return;
        build.Invoke(null, new object[] { this, chunk, addChunkCollider });

        // After rebuilding block mesh, rebuild batched plants using the actual chunk vertical size
        // (previously used flat layer counts causing plants above that threshold to vanish after any rebuild).
        if (useChunkMeshing && HasAnyPlants())
        {
            int usefulY = Mathf.Min(worldHeight, chunk.sizeY); // full vertical scan within world bounds
            BuildChunkPlants(chunk, usefulY);
        }
    }

    // --- Startup player gate ---
    private System.Collections.IEnumerator StartupPlayerGate()
    {
        // If no player, try to find again; if still none, there's nothing to gate.
        if (player == null) AutoFindPlayer();
        if (player == null) yield break;

        // Compute center chunk from current player position before disabling them
        var center = WorldToChunkCoord(player.position);

        // Disable player GameObject during initial chunk build
        var playerGO = player.gameObject;
        bool wasActive = playerGO.activeSelf;
        if (wasActive) playerGO.SetActive(false);

        // Wait until the center chunk is loaded and meshed
        while (true)
        {
            if (_chunks.TryGetValue(center, out var ch) && ch != null && ch.parent != null)
            {
                var mf = ch.parent.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    break;
                }
            }
            yield return null;
        }

        // Optionally snap player to ground (only for new worlds without saved player data)
        if (snapPlayerToGroundOnSpawn)
        {
            bool hasSavedPlayerData = false;
            if (WorldSaveManager.Instance != null)
            {
                var currentWorld = WorldSaveManager.Instance.GetCurrentWorldData();
                hasSavedPlayerData = (currentWorld != null && currentWorld.playerPosition != Vector3.zero);
            }
            
            if (!hasSavedPlayerData)
            {
                var p = player.position;
                int x = Mathf.FloorToInt(p.x);
                int z = Mathf.FloorToInt(p.z);
                int gy = FindHighestSolidYAt(x, z);
                if (gy >= 0)
                {
                    // place a bit above ground to avoid collisions
                    player.position = new Vector3(p.x, gy + 1.6f, p.z);
                    Debug.Log($"WorldGenerator: Player snapped to ground at Y={gy + 1.6f} (new world, no saved position)");
                }
            }
            else
            {
                Debug.Log($"WorldGenerator: Player position preserved from save data, skipping ground snap");
            }
        }

        if (wasActive) playerGO.SetActive(true);
    }

    private int FindHighestSolidYAt(int x, int z)
    {
        for (int y = worldHeight - 1; y >= 0; y--)
        {
            if (GetBlockType(new Vector3Int(x, y, z)) != BlockType.Air)
                return y;
        }
        // Fallback to estimated surface height if none found
        return GetColumnTopY(x, z);
    }

    // Build batched plants for a meshed chunk to keep loads lightweight
    private void BuildChunkPlants(WorldGeneration.Chunks.Chunk chunk, int usefulY)
    {
        if (!HasAnyPlants() || chunk == null || chunk.parent == null) return;

        // Ensure renderer component exists on chunk parent
        var pcr = chunk.parent.GetComponent<PlantChunkRenderer>();
        if (pcr == null) pcr = chunk.parent.gameObject.AddComponent<PlantChunkRenderer>();
        pcr.chunkCoord = chunk.coord;
        pcr.chunkSizeX = chunk.sizeX;
        pcr.chunkSizeZ = chunk.sizeZ;
        pcr.worldHeight = worldHeight;
        pcr.Clear();

        for (int lx = 0; lx < chunk.sizeX; lx++)
        {
            for (int lz = 0; lz < chunk.sizeZ; lz++)
            {
                // ly+1 is accessed below (tAbove), so stop at sizeY-1 to stay in range
                int maxLy = Mathf.Min(usefulY, chunk.sizeY - 1);
                for (int ly = 1; ly < maxLy; ly++)
                {
                    var t = chunk.GetLocal(lx, ly, lz);
                    var tAbove = chunk.GetLocal(lx, ly + 1, lz);
                    if (t != BlockType.Grass || tAbove != BlockType.Air) continue;
                    // Determine deterministic world-space grass cell and plant cell
                    var worldGrass = chunk.LocalToWorld(new Vector3Int(lx, ly, lz));
                    var plantCell = worldGrass + Vector3Int.up;

                    // Skip if this plant cell has been removed by the player
                    var cc = chunk.coord;
                    if (_removedBatchedPlants.TryGetValue(cc, out var removedSet) && removedSet.Contains(plantCell))
                        continue;

                    // Deterministic count per cell
                    int count = CountBatchedPlantsAtCell(worldGrass);
                    if (count <= 0) continue;

                    // Per-cell deterministic RNG for visuals/definition pick
                    int seed = worldGrass.x * 73856093 ^ worldGrass.y * 19349663 ^ worldGrass.z * 83492791;
                    var rng = new System.Random(seed);

                    // Pick a definition and its texture and params via reflection helpers
                    var def = WG_PickPlantByWeight(rng);
                    var tex = PlantDef_GetTexture(def);
                    if (tex == null) continue;
                    var hr = PlantDef_GetHeightRange(def);
                    var w = PlantDef_GetWidth(def);
                    var yo = PlantDef_GetYOffset(def);

                    // Get shared plant material for this texture
                    var mat = GetPlantSharedMaterial(tex);
                    if (mat == null) continue;

                    // Local position inside chunk parent space
                    var localPos = new Vector3(lx + 0.5f, ly + 1.001f, lz + 0.5f);

                    // Aggregate density into number of crossed-quad sets (clamped 1..4)
                    int quads = Mathf.Clamp(count * 2, 1, 4); // base 2 quads scaled by density count
                    pcr.AddPlantCluster(localPos, hr, w, yo, quads, mat, rng, plantCell);
                }
            }
        }

        pcr.Flush(this);
    }

    // Return the expected plant instances at a grass cell using deterministic RNG per cell
    private int CountBatchedPlantsAtCell(Vector3Int grassCell)
    {
        if (GetBlockType(grassCell) != BlockType.Grass) return 0;
        if (GetBlockType(grassCell + Vector3Int.up) != BlockType.Air) return 0;
        float desired = Mathf.Max(0f, plantDensity);
        int count = Mathf.FloorToInt(desired);
        double remainder = desired - count;
        int seed = grassCell.x * 73856093 ^ grassCell.y * 19349663 ^ grassCell.z * 83492791;
        var rng = new System.Random(seed);
        if (rng.NextDouble() < remainder) count++;
        return count;
    }

    // Query if a batched plant exists at the given plant cell (world space cell above a grass block)
    public bool HasBatchedPlantAt(Vector3Int plantCell)
    {
        if (!useChunkStreaming || !useChunkMeshing) return false;
        var grassCell = plantCell + Vector3Int.down;
        var cc = WorldToChunkCoord(plantCell);
        if (_removedBatchedPlants.TryGetValue(cc, out var removedSet) && removedSet.Contains(plantCell))
            return false;
        return CountBatchedPlantsAtCell(grassCell) > 0;
    }

    // Remove a batched plant at the given plant cell (no delayed respawn for instant feedback)
    public void RemoveBatchedPlantAt(Vector3Int plantCell)
    {
        if (!useChunkStreaming || !useChunkMeshing) return;
        var cc = WorldToChunkCoord(plantCell);
        if (!_removedBatchedPlants.TryGetValue(cc, out var removedSet))
        {
            removedSet = new HashSet<Vector3Int>();
            _removedBatchedPlants[cc] = removedSet;
        }
        // Mark removed (idempotent)
        removedSet.Add(plantCell);

        // Perform a surgical rebuild affecting only this one cell's geometry instead of rebuilding
        // the entire chunk plant mesh. We rebuild per material: remove triangles for this cell then
        // (optionally) regenerate if it should still exist (it should not since we removed it).
        if (_chunks.TryGetValue(cc, out var chunk))
        {
            RebuildSinglePlantCell(chunk, plantCell);
        }

    // No respawn scheduling: plants reappear only if chunk is regenerated or grass regrows and density logic includes them
    }

    // Rebuild only one plant cell inside the chunk's PlantChunkRenderer to avoid full chunk flicker.
    private void RebuildSinglePlantCell(WorldGeneration.Chunks.Chunk chunk, Vector3Int plantCell)
    {
        if (chunk == null || chunk.parent == null) return;
        var renderer = chunk.parent.GetComponent<PlantChunkRenderer>();
        if (renderer == null)
        {
            // Nothing to do if no plants were built yet for this chunk.
            return;
        }
    // Surgical removal: PlantChunkRenderer tracks triangle ranges per plant cell.
    // We remove only that cell's triangles; fallback rebuild only if tracking unavailable.
    // Try surgical removal first (if renderer exposes tracking) else fallback to rebuild
        var pcrRenderer = chunk.parent.GetComponent<PlantChunkRenderer>();
        if (pcrRenderer != null)
        {
            pcrRenderer.RemovePlantCellGeometry(plantCell);
        }
        else
        {
            int fullY = Mathf.Clamp(chunk.sizeY, 1, worldHeight);
            BuildChunkPlants(chunk, fullY);
        }
    }

    private void SchedulePlantRespawnBatched(Vector3Int grassCell, int delayTicks)
    {
        if (IsOutOfBounds(grassCell)) return;
        if (_scheduledPlantCells.Contains(grassCell)) return;
        _scheduledPlantCells.Add(grassCell);
        _tickQueue.Add(new TickAction { dueTick = _currentTick + Mathf.Max(1, delayTicks), type = TA_PlantRespawn, cell = grassCell });
    }

    // Public helper for PlantBillboard and internal cleanup
    public void RemovePlantAt(Vector3Int cell)
    {
        if (plantObjects.TryGetValue(cell, out var obj))
        {
            if (obj != null) Destroy(obj);
            plantObjects.Remove(cell);
            // Schedule a delayed respawn on the supporting grass cell
            var grassCell = cell + Vector3Int.down;
            SchedulePlantRespawn(grassCell, plantRespawnDelayTicks);
        }
    }

    // ---------- Persistence helpers ----------
    private string GetSaveFolderPath()
    {
        if (string.IsNullOrEmpty(currentWorldName))
        {
            // World name not set yet - this is normal during startup
            return null; // Return null to disable persistence when world name is not set
        }
        
        // Create world-specific subfolder
        return System.IO.Path.Combine(Application.persistentDataPath, saveFolderName, currentWorldName);
    }

    private string GetChunkSavePath(Vector2Int coord)
    {
        string folderPath = GetSaveFolderPath();
        if (string.IsNullOrEmpty(folderPath)) return null;
        return System.IO.Path.Combine(folderPath, $"chunk_{coord.x}_{coord.y}.json");
    }
    
    // Public method to clear all chunk data for debugging (useful after fixing seed timing issues)
    [ContextMenu("Clear All World Chunks")]
    public void ClearAllWorldChunks()
    {
        Debug.Log("WorldGenerator: Manually clearing all world chunks for debugging");
        ClearAllChunks();
        WorldSaveManager.Instance?.ClearDefaultChunkData();
        if (!string.IsNullOrEmpty(currentWorldName))
        {
            WorldSaveManager.Instance?.ClearWorldChunkData(currentWorldName);
        }
    }
    
    // Public method to set the world name and clear existing world data
    public void InitializeForWorld(string worldName)
    {
        Debug.Log($"WorldGenerator: Initializing for world '{worldName}' (current: '{currentWorldName}')");
        
        // Store the previous world name for saving
        string previousWorldName = currentWorldName;
        
        // Only clear chunks if we're switching to a different world
        if (!string.IsNullOrEmpty(previousWorldName) && previousWorldName != worldName)
        {
            Debug.Log($"WorldGenerator: Switching from '{previousWorldName}' to '{worldName}' - saving current chunks then clearing");
            // currentWorldName is still set to the previous world for saving
            SaveAllLoadedChunks(); // Save current world's chunks before clearing
            ClearAllChunks();
        }
        else if (string.IsNullOrEmpty(previousWorldName))
        {
            Debug.Log($"WorldGenerator: First world initialization for '{worldName}' - clearing any existing chunks");
            ClearAllChunks();
        }
        else
        {
            Debug.Log($"WorldGenerator: Reloading same world '{worldName}' - keeping existing chunks");
        }
        
        // Now set the new world name
        currentWorldName = worldName;
        
        // Re-enable persistence if it was disabled due to missing world name
        if (!enableChunkPersistence)
        {
            Debug.Log("WorldGenerator: Re-enabling chunk persistence now that world name is set");
            enableChunkPersistence = true;
        }
        
        // Now that world name is set, start chunk streaming if enabled
        if (useChunkStreaming)
        {
            Debug.Log($"WorldGenerator: Starting chunk streaming for world '{worldName}' with seed {worldSeed}");
            UpdateStreaming(force: true);
        }
    }
    
    // Public method to save all loaded chunks - called by GameManager when saving
    public void SaveAllLoadedChunks()
    {
        if (!enableChunkPersistence) 
        {
            Debug.LogWarning($"WorldGenerator: SaveAllLoadedChunks called but persistence disabled");
            return;
        }
        if (string.IsNullOrEmpty(currentWorldName)) 
        {
            Debug.LogWarning($"WorldGenerator: SaveAllLoadedChunks called but currentWorldName is empty");
            return;
        }
        
        int editsCount = _chunkEdits.Values.Sum(dict => dict.Count);
        Debug.Log($"WorldGenerator: SaveAllLoadedChunks called - {_chunks.Count} loaded chunks, {editsCount} total edits for world '{currentWorldName}'");
        
        int savedChunks = 0;
        foreach (var kv in _chunks)
        {
            if (_chunkEdits.TryGetValue(kv.Key, out var edits) && edits.Count > 0)
            {
                SaveChunkToDisk(kv.Key);
                savedChunks++;
                Debug.Log($"WorldGenerator: Saved chunk {kv.Key} with {edits.Count} edits");
            }
        }
        
        Debug.Log($"WorldGenerator: Completed saving {savedChunks} chunks with modifications for world '{currentWorldName}'");
    }
    
    private void ClearAllChunks()
    {
        int chunksToDelete = _chunks.Count;
        int editsToDelete = _chunkEdits.Values.Sum(dict => dict.Count);
        Debug.Log($"WorldGenerator: ClearAllChunks called - clearing {chunksToDelete} chunks and {editsToDelete} edits");
        
        // Stop any pending loads
        _pendingLoads.Clear();
        _queued.Clear();
        _loading.Clear();
        
        // Destroy existing chunk GameObjects
        foreach (var chunk in _chunks.Values)
        {
            if (chunk?.parentGO != null)
            {
                DestroyImmediate(chunk.parentGO);
            }
        }
        
        // Clear chunk dictionary and edit history
        _chunks.Clear();
        _chunkEdits.Clear();
        
        Debug.Log($"WorldGenerator: Cleared {chunksToDelete} chunks and {editsToDelete} edits from memory");
    }

    private void EnsureSaveFolder()
    {
        var dir = GetSaveFolderPath();
        if (string.IsNullOrEmpty(dir)) return;
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
    }

    private void SaveChunkToDisk(Vector2Int coord)
    {
        if (!enableChunkPersistence) return;
        if (!_chunkEdits.TryGetValue(coord, out var map) || map.Count == 0) return;
        
        string savePath = GetChunkSavePath(coord);
        if (string.IsNullOrEmpty(savePath))
        {
            Debug.LogWarning($"Cannot save chunk {coord} - world name not set");
            return;
        }
        
        EnsureSaveFolder();
    var data = new ChunkSaveDTO
        {
            cx = coord.x,
            cz = coord.y,
            sizeX = chunkSizeX,
            sizeY = worldHeight,
            sizeZ = chunkSizeZ,
        changes = new ChangedCellDTO[map.Count]
        };
        int i = 0;
        foreach (var kv in map)
        {
        data.changes[i++] = new ChangedCellDTO { x = kv.Key.x, y = kv.Key.y, z = kv.Key.z, t = (int)kv.Value };
        }
        var json = JsonUtility.ToJson(data, false);
        System.IO.File.WriteAllText(savePath, json);
        Debug.Log($"WorldGenerator: Saved chunk {coord} with {map.Count} changes to {savePath} for world '{currentWorldName}'");
    }

    private void LoadChunkFromDiskInto(Vector2Int coord, WorldGeneration.Chunks.Chunk chunk)
    {
        if (!enableChunkPersistence) return;
        var path = GetChunkSavePath(coord);
        if (string.IsNullOrEmpty(path)) return; // World name not set, can't load
        if (!System.IO.File.Exists(path)) return;
        var json = System.IO.File.ReadAllText(path);
    var data = JsonUtility.FromJson<ChunkSaveDTO>(json);
        if (data?.changes == null) 
        {
            Debug.LogWarning($"WorldGenerator: No changes found in chunk file {path}");
            return;
        }
        
        Debug.Log($"WorldGenerator: Loading chunk {coord} with {data.changes.Length} changes from {path} for world '{currentWorldName}'");
        foreach (var c in data.changes)
        {
            var wp = new Vector3Int(c.x, c.y, c.z);
            var lp = chunk.WorldToLocal(wp);
            if (lp.x >= 0 && lp.x < chunk.sizeX && lp.y >= 0 && lp.y < chunk.sizeY && lp.z >= 0 && lp.z < chunk.sizeZ)
            {
        chunk.SetLocal(lp.x, lp.y, lp.z, (BlockType)c.t);
            }
        }
        if (!_chunkEdits.ContainsKey(coord)) _chunkEdits[coord] = new Dictionary<Vector3Int, BlockType>();
        var map = _chunkEdits[coord];
    foreach (var c in data.changes) map[new Vector3Int(c.x, c.y, c.z)] = (BlockType)c.t;
    }

    // --- Tick scheduling and helpers ---
    public void SchedulePlantRespawn(Vector3Int grassCell, int delayTicks)
    {
        if (IsOutOfBounds(grassCell)) return;
        var above = grassCell + Vector3Int.up;
        if (GetBlockType(grassCell) != BlockType.Grass) return;
        if (GetBlockType(above) != BlockType.Air) return;
        if (plantObjects.TryGetValue(above, out var obj))
        {
            if (obj == null)
            {
                plantObjects.Remove(above);
            }
            else
            {
                return;
            }
        }
        if (_scheduledPlantCells.Contains(grassCell)) return;
        _scheduledPlantCells.Add(grassCell);
        _tickQueue.Add(new TickAction { dueTick = _currentTick + Mathf.Max(1, delayTicks), type = TA_PlantRespawn, cell = grassCell });
    }

    public void ScheduleGrassGrowth(Vector3Int dirtCell, int delayTicks)
    {
        if (IsOutOfBounds(dirtCell)) return;
        var above = dirtCell + Vector3Int.up;
        if (GetBlockType(dirtCell) != BlockType.Dirt) return;
        if (GetBlockType(above) != BlockType.Air) return;
        if (_scheduledGrassCells.Contains(dirtCell)) return;
        _scheduledGrassCells.Add(dirtCell);
        _tickQueue.Add(new TickAction { dueTick = _currentTick + Mathf.Max(1, delayTicks), type = TA_GrassGrow, cell = dirtCell });
    }

    private void ProcessTick()
    {
        if (_tickQueue.Count == 0) return;
        for (int i = _tickQueue.Count - 1; i >= 0; i--)
        {
            var ta = _tickQueue[i];
            if (ta.dueTick > _currentTick) continue;
            _tickQueue.RemoveAt(i);
            if (ta.type == TA_PlantRespawn)
            {
                _scheduledPlantCells.Remove(ta.cell);
                // Batched plants no longer auto-respawn; only legacy per-plant GameObjects do.
                if (!(useChunkStreaming && useChunkMeshing))
                {
                    TrySpawnPlantClusterAt(ta.cell);
                }
            }
            else if (ta.type == TA_GrassGrow)
            {
                _scheduledGrassCells.Remove(ta.cell);
                if (GetBlockType(ta.cell) == BlockType.Dirt && GetBlockType(ta.cell + Vector3Int.up) == BlockType.Air)
                {
                    if (useChunkStreaming)
                    {
                        var cc = WorldToChunkCoord(ta.cell);
                        if (_chunks.TryGetValue(cc, out var ch))
                        {
                            var lp = ch.WorldToLocal(ta.cell);
                            ch.SetLocal(lp.x, lp.y, lp.z, BlockType.Grass);
                            if (enableChunkPersistence)
                            {
                                var ccg = WorldToChunkCoord(ta.cell);
                                if (!_chunkEdits.TryGetValue(ccg, out var mapG)) { mapG = new Dictionary<Vector3Int, BlockType>(); _chunkEdits[ccg] = mapG; }
                                mapG[ta.cell] = BlockType.Grass;
                            }
                            if (useChunkMeshing)
                            {
                                RebuildChunkMeshAt(cc);
                            }
                            else
                            {
                                if (blockObjects.ContainsKey(ta.cell))
                                {
                                    Destroy(blockObjects[ta.cell]);
                                    blockObjects.Remove(ta.cell);
                                }
                                CreateBlock(ta.cell, BlockType.Grass, ch.parent);
                            }
                        }
                    }
                    else
                    {
                        worldData[ta.cell.x, ta.cell.y, ta.cell.z] = BlockType.Grass;
                        if (blockObjects.ContainsKey(ta.cell))
                        {
                            Destroy(blockObjects[ta.cell]);
                            blockObjects.Remove(ta.cell);
                        }
                        CreateBlock(ta.cell, BlockType.Grass);
                    }
                    // Plants should appear later on new grass
                    SchedulePlantRespawn(ta.cell, newGrassPlantDelayTicks);
                    UpdateNeighboringBlocks(ta.cell);
                }
            }
        }
    }

    public void TrySpawnPlantClusterAt(Vector3Int grassCell, Transform parentOverride = null)
    {
        if (IsOutOfBounds(grassCell)) return;
        var above = grassCell + Vector3Int.up;
        if (GetBlockType(grassCell) != BlockType.Grass) return;
        if (GetBlockType(above) != BlockType.Air) return;
        if (plantObjects.TryGetValue(above, out var existing))
        {
            // If an entry exists but the object was already destroyed (Unity-null), clean it up
            if (existing == null)
            {
                plantObjects.Remove(above);
            }
            else
            {
                return;
            }
        }
    if (!ShouldRenderBlock(grassCell.x, grassCell.y, grassCell.z)) return;
    if (!HasAnyPlants()) return;

    System.Random rng = new System.Random(grassCell.GetHashCode() ^ (_currentTick * 997));
    float desired = Mathf.Max(0f, plantDensity); // use configured density directly
        int count = Mathf.FloorToInt(desired);
        double remainder = desired - count;
        if (rng.NextDouble() < remainder) count++;
        if (count <= 0) return;

        var parent = new GameObject($"Plants ({grassCell.x},{grassCell.y+1},{grassCell.z})");
        parent.transform.parent = parentOverride != null ? parentOverride : this.transform;
    Vector3 placePos = new Vector3(grassCell.x + 0.5f, grassCell.y + 1.001f, grassCell.z + 0.5f);
        if (blockObjects.TryGetValue(grassCell, out var grassGo))
        {
            var rend = grassGo.GetComponent<Renderer>();
            if (rend != null)
            {
                var b = rend.bounds;
        placePos = new Vector3(b.center.x, b.max.y + 0.001f, b.center.z);
            }
        }
        // Snap to actual surface if a collider exists under the spawn point to avoid intersection
        var rayOrigin = placePos + new Vector3(0, 2f, 0);
        if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
        {
            placePos = new Vector3(placePos.x, hit.point.y + 0.001f, placePos.z);
        }
    parent.transform.position = placePos;
    var def = WG_PickPlantByWeight(rng);
        var tex = PlantDef_GetTexture(def);
        if (tex != null)
        {
            var hr = PlantDef_GetHeightRange(def);
            var w = PlantDef_GetWidth(def);
            var yo = PlantDef_GetYOffset(def);
            SpawnPlantChildrenIntoParent(parent, grassCell, count, rng, tex, hr, w, yo);
        }
        plantObjects[above] = parent;
    }

    // Called by Chunk to unload its rendered cells safely
    public void UnloadCells(IEnumerable<Vector3Int> worldCells, GameObject chunkParent)
    {
        foreach (var cell in worldCells)
        {
            if (blockObjects.TryGetValue(cell, out var go) && go != null) Destroy(go);
            blockObjects.Remove(cell);
            var above = cell + Vector3Int.up;
            if (plantObjects.TryGetValue(above, out var plant) && plant != null) Destroy(plant);
            plantObjects.Remove(above);
        }
        // In meshed mode, renderedCells may be empty; sweep plantObjects by chunk area to remove stale entries
        if (chunkParent != null)
        {
            var basePos = chunkParent.transform.position;
            int baseX = Mathf.FloorToInt(basePos.x);
            int baseZ = Mathf.FloorToInt(basePos.z);
            int maxX = baseX + chunkSizeX;
            int maxZ = baseZ + chunkSizeZ;
            var toRemove = new List<Vector3Int>();
            foreach (var kv in plantObjects)
            {
                var p = kv.Key;
                if (p.x >= baseX && p.x < maxX && p.z >= baseZ && p.z < maxZ)
                {
                    if (kv.Value != null) Destroy(kv.Value);
                    toRemove.Add(p);
                }
            }
            foreach (var k in toRemove) plantObjects.Remove(k);
            Destroy(chunkParent);
        }
    }

    private void SpawnPlantChildrenIntoParent(GameObject parent, Vector3Int grassCell, int count, System.Random rng, Texture2D texture, Vector2 heightRange, float width, float yOffset)
    {
        // Cap quads per cluster for performance
        const int MaxPlantQuadsPerCluster = 8;
        count = Mathf.Clamp(count, 1, MaxPlantQuadsPerCluster);
        if (texture == null) return;

        // Build a combined mesh with 'count' crossed-quads
        var mf = parent.AddComponent<MeshFilter>();
        var mr = parent.AddComponent<MeshRenderer>();
        var mesh = new Mesh { name = $"PlantCluster_{grassCell.x}_{grassCell.y}_{grassCell.z}" };

        var vertices = new List<Vector3>(count * 8);
        var uvs = new List<Vector2>(count * 8);
        var tris = new List<int>(count * 24);

        float maxH = 0.0f;
        for (int i = 0; i < count; i++)
        {
            // Random offset and rotation per crossed-quad set
            float off = 0.18f;
            float ox = (float)(rng.NextDouble()*2 - 1) * off;
            float oz = (float)(rng.NextDouble()*2 - 1) * off;
            float h = Mathf.Lerp(heightRange.x, heightRange.y, (float)rng.NextDouble());
            maxH = Mathf.Max(maxH, h);

            // Two quads centered on origin; we build them aligned (like PlantBillboard) and offset
            int vStart = vertices.Count;
            float halfW = Mathf.Clamp(width, 0.1f, 2f) * 0.5f;
            float y0 = yOffset; // small lift to avoid z-fighting/embedding
            float y1 = h + yOffset;

            // Quad A (along Z)
            vertices.Add(new Vector3(-halfW + ox, y0, 0 + oz));
            vertices.Add(new Vector3( halfW + ox, y0, 0 + oz));
            vertices.Add(new Vector3( halfW + ox, y1, 0 + oz));
            vertices.Add(new Vector3(-halfW + ox, y1, 0 + oz));
            // Quad B (along X)
            vertices.Add(new Vector3(0 + ox, y0, -halfW + oz));
            vertices.Add(new Vector3(0 + ox, y0,  halfW + oz));
            vertices.Add(new Vector3(0 + ox, y1,  halfW + oz));
            vertices.Add(new Vector3(0 + ox, y1, -halfW + oz));

            // UVs
            uvs.Add(new Vector2(0,0)); uvs.Add(new Vector2(1,0)); uvs.Add(new Vector2(1,1)); uvs.Add(new Vector2(0,1));
            uvs.Add(new Vector2(0,0)); uvs.Add(new Vector2(1,0)); uvs.Add(new Vector2(1,1)); uvs.Add(new Vector2(0,1));

            // Triangles (double-sided)
            // Quad A
            tris.Add(vStart + 0); tris.Add(vStart + 2); tris.Add(vStart + 1);
            tris.Add(vStart + 0); tris.Add(vStart + 3); tris.Add(vStart + 2);
            tris.Add(vStart + 1); tris.Add(vStart + 2); tris.Add(vStart + 0);
            tris.Add(vStart + 2); tris.Add(vStart + 3); tris.Add(vStart + 0);
            // Quad B
            tris.Add(vStart + 4); tris.Add(vStart + 6); tris.Add(vStart + 5);
            tris.Add(vStart + 4); tris.Add(vStart + 7); tris.Add(vStart + 6);
            tris.Add(vStart + 5); tris.Add(vStart + 6); tris.Add(vStart + 4);
            tris.Add(vStart + 6); tris.Add(vStart + 7); tris.Add(vStart + 4);
        }

    mesh.SetVertices(vertices);
    mesh.SetUVs(0, uvs);
    mesh.SetTriangles(tris, 0);
    // Provide stable lighting: use an upright normal so cutout cards receive sky and main light
    var norms = new List<Vector3>(vertices.Count);
    for (int i = 0; i < vertices.Count; i++) norms.Add(Vector3.up);
    mesh.SetNormals(norms);
    mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        // Assign shared material (cached by texture)
    mr.sharedMaterial = GetPlantSharedMaterial(texture);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;

        // Single trigger collider on the parent to allow interaction
        var col = parent.AddComponent<BoxCollider>();
        col.isTrigger = true;
        float radius = 0.6f; // encompass offsets
        col.size = new Vector3(radius, Mathf.Max(0.6f, maxH), radius);
        col.center = new Vector3(0f, 0.01f + col.size.y * 0.5f, 0f);
    }

    // --- Plant reflection helpers ---
    private bool PlantDB_HasAny()
    {
        if (plantDatabase == null) return false;
        var dbType = plantDatabase.GetType();
        // Try property HasAny
        var hasAnyProp = dbType.GetProperty("HasAny");
        if (hasAnyProp != null && hasAnyProp.PropertyType == typeof(bool))
        {
            return (bool)hasAnyProp.GetValue(plantDatabase);
        }
        // Fallback: iterate plants array and check texture
        var plantsField = dbType.GetField("plants");
        if (plantsField == null) return false;
        var arr = plantsField.GetValue(plantDatabase) as System.Array;
        if (arr == null) return false;
        foreach (var def in arr)
        {
            if (def == null) continue;
            var tex = PlantDef_GetTexture(def);
            var weightField = def.GetType().GetField("weight");
            float w = weightField != null ? Mathf.Max(0f, (float)(weightField.GetValue(def) ?? 0f)) : 1f;
            if (tex != null && w > 0f) return true;
        }
        return false;
    }

    private object PlantDB_PickByWeight(System.Random rng)
    {
        if (plantDatabase == null) return null;
        var dbType = plantDatabase.GetType();
        var pick = dbType.GetMethod("PickByWeight", new System.Type[] { typeof(System.Random) });
        if (pick != null)
        {
            return pick.Invoke(plantDatabase, new object[] { rng });
        }
        // Fallback: simple first non-null
        var plantsField = dbType.GetField("plants");
        var arr = plantsField?.GetValue(plantDatabase) as System.Array;
        if (arr == null || arr.Length == 0) return null;
        foreach (var def in arr) if (def != null) return def;
        return null;
    }

    // Unified picker over DB + runtime extras
    private object WG_PickPlantByWeight(System.Random rng)
    {
        // Merge runtime extras (fern/plant) with DB entries that match allowed textures
        // so both fern.png and plant.png can spawn even if only one was added as an extra.
        var defs = new System.Collections.Generic.List<(object def, float weight)>();

        // Gather extras and build an allow-list of textures by reference and by name
        var allowedTextures = new HashSet<Texture2D>();
    var allowedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "fern", "plant", "grass" };
        foreach (var d in _extraPlantDefs)
        {
            var tex = PlantDef_GetTexture(d);
            if (tex != null) { allowedTextures.Add(tex); }
            var wf = d.GetType().GetField("weight");
            float w = wf != null ? Mathf.Max(0f, (float)(wf.GetValue(d) ?? 0f)) : 1f;
            if (tex == null || w <= 0f) continue;
            defs.Add((d, w));
        }

        // Include DB entries. If we have extras, only include DB entries whose textures are
        // either in the allow-list or clearly named fern/plant. If no extras, include all DB entries.
        if (plantDatabase != null)
        {
            var dbType = plantDatabase.GetType();
            var plantsField = dbType.GetField("plants");
            var arr = plantsField?.GetValue(plantDatabase) as System.Array;
            if (arr != null)
            {
                foreach (var d in arr)
                {
                    if (d == null) continue;
                    var tex = PlantDef_GetTexture(d);
                    var wf = d.GetType().GetField("weight");
                    float w = wf != null ? Mathf.Max(0f, (float)(wf.GetValue(d) ?? 0f)) : 1f;
                    if (tex == null || w <= 0f) continue;
                    bool include;
                    if (_extraPlantDefs.Count == 0)
                    {
                        include = true; // no extras detected, allow DB freely
                    }
                    else
                    {
                        include = allowedTextures.Contains(tex) || allowedNames.Contains(tex.name);
                    }
                    if (include)
                    {
                        defs.Add((d, w));
                    }
                }
            }
        }

        if (defs.Count == 0) return null;
        // Apply inspector weight multipliers by texture name
        for (int i = 0; i < defs.Count; i++)
        {
            var tex = PlantDef_GetTexture(defs[i].def);
            if (tex != null)
            {
                var n = tex.name.ToLowerInvariant();
                float mul = 1f;
                if (n.Contains("fern")) mul = plantWeightFern;
                else if (n.Contains("grass")) mul = plantWeightGrass;
                else if (n.Contains("plant")) mul = plantWeightPlant;
                defs[i] = (defs[i].def, Mathf.Max(0f, defs[i].weight * mul));
            }
        }
        float total = 0f; foreach (var e in defs) total += e.weight;
        float r = (float)rng.NextDouble() * total;
        foreach (var e in defs)
        {
            if (r < e.weight) return e.def; r -= e.weight;
        }
        return defs[0].def;
    }

    private Texture2D PlantDef_GetTexture(object def)
    {
        if (def == null) return null;
        return def.GetType().GetField("texture")?.GetValue(def) as Texture2D;
    }

    // Register a runtime plant def for a texture if it isn't already present in DB or extras
    private void AddRuntimePlantIfNew(Texture2D tex)
    {
        if (tex == null) return;
        // Skip if this texture already exists in the PlantDatabase
        if (IsTextureInDatabase(tex)) return;
        // Skip if already added to extras
        foreach (var d in _extraPlantDefs)
        {
            var existing = PlantDef_GetTexture(d);
            if (existing == tex) return;
        }
        // Create a minimal runtime def with sensible defaults
        var def = new RuntimePlantDef { texture = tex };
        var name = tex != null ? tex.name.ToLowerInvariant() : string.Empty;
        if (name.Contains("grass"))
        {
            // Shorter, slightly wider patches
            def.heightRange = new Vector2(0.45f, 0.9f);
            def.width = 1.0f;
            def.yOffset = 0.01f;
            def.weight = 1.2f; // a bit more common
        }
        else if (name.Contains("fern"))
        {
            def.heightRange = new Vector2(0.7f, 1.4f);
            def.width = 0.8f;
            def.yOffset = 0.01f;
            def.weight = 1.0f;
        }
        else
        {
            def.heightRange = new Vector2(0.6f, 1.2f);
            def.width = 0.9f;
            def.yOffset = 0.01f;
            def.weight = 1.0f;
        }
        _extraPlantDefs.Add(def);
    }

    private bool IsTextureInDatabase(Texture2D tex)
    {
        if (tex == null || plantDatabase == null) return false;
        var dbType = plantDatabase.GetType();
        var plantsField = dbType.GetField("plants");
        var arr = plantsField?.GetValue(plantDatabase) as System.Array;
        if (arr == null) return false;
        foreach (var d in arr)
        {
            if (d == null) continue;
            var t = PlantDef_GetTexture(d);
            if (t == tex) return true;
        }
        return false;
    }

    private Vector2 PlantDef_GetHeightRange(object def)
    {
        if (def == null) return new Vector2(0.6f, 1.2f);
        var f = def.GetType().GetField("heightRange");
        return f != null ? (Vector2)f.GetValue(def) : new Vector2(0.6f, 1.2f);
    }

    private float PlantDef_GetWidth(object def)
    {
        if (def == null) return 0.9f;
        var f = def.GetType().GetField("width");
        float baseW = f != null ? Mathf.Clamp((float)(f.GetValue(def) ?? 0.9f), 0.1f, 2f) : 0.9f;
        // Apply per-kind size multiplier
        var tex = PlantDef_GetTexture(def);
        if (tex != null)
        {
            var n = tex.name.ToLowerInvariant();
            if (n.Contains("fern")) baseW *= plantSizeFern;
            else if (n.Contains("grass")) baseW *= plantSizeGrass;
            else if (n.Contains("plant")) baseW *= plantSizePlant;
        }
        return baseW;
    }

    private float PlantDef_GetYOffset(object def)
    {
        if (def == null) return 0.02f;
        var f = def.GetType().GetField("yOffset");
        return f != null ? (float)(f.GetValue(def) ?? 0.02f) : 0.02f;
    }

    private Material GetPlantSharedMaterial(Texture2D texture)
    {
        if (texture == null) return null;
        if (_plantMaterialCache.TryGetValue(texture, out var cached))
        {
            if (cached != null && cached.shader != null && cached.shader.isSupported) return cached;
            // If cached is invalid or unsupported, drop it and rebuild
            _plantMaterialCache.Remove(texture);
        }

        // Prefer wind shader when enabled, fallback to URP Simple Lit
        Shader shader = null;
        if (enablePlantWind)
        {
            var ws = Shader.Find("Custom/PlantWind");
            if (ws != null && ws.isSupported && IsShaderURPCompatible(ws)) shader = ws;
        }
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        }

        var mat = new Material(shader)
        {
            name = $"Plant_{texture.name}",
            color = Color.white,
            enableInstancing = true
        };
        mat.SetTexture("_BaseMap", texture);
        mat.mainTexture = texture;
        // Alpha clip and double-sided so cards render correctly
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
        if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // double-sided
        if (mat.HasProperty("_AlphaToMask")) mat.SetFloat("_AlphaToMask", 1f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        _plantMaterialCache[texture] = mat;
        return mat;
    }

    // Utility: find a type by simple name across loaded assemblies
    private System.Type ResolveTypeByName(string typeName)
    {
        var t = System.Type.GetType(typeName);
        if (t != null) return t;
        var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            t = asm.GetType(typeName);
            if (t != null) return t;
            foreach (var candidate in asm.GetTypes())
            {
                if (candidate.Name == typeName) return candidate;
            }
        }
        return null;
    }

    // Returns true if the shader is usable in the current pipeline (URP). We check RenderPipeline tag.
    private bool IsShaderURPCompatible(Shader s)
    {
        if (s == null) return false;
        try
        {
            var tmp = new Material(s);
            string rp = tmp.GetTag("RenderPipeline", false);
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(tmp); else Destroy(tmp);
#else
            Destroy(tmp);
#endif
            // Accept empty (some custom shaders omit tag but still work) or explicitly UniversalPipeline
            if (string.IsNullOrEmpty(rp)) return true;
            return rp.IndexOf("Universal", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    // Grid raycast using 3D DDA. Returns the first solid block hit and the empty cell just before it.
    public bool TryVoxelRaycast(Ray ray, float maxDistance, out Vector3Int hitCell, out Vector3Int placeCell, out Vector3 hitNormal)
    {
        hitCell = default;
        placeCell = default;
        hitNormal = Vector3.zero;

        Vector3 pos = ray.origin;
        Vector3 dir = ray.direction;
        if (dir.sqrMagnitude < 1e-8f) return false;
        dir.Normalize();

        // Current cell is floor of position
        Vector3Int cell = new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));
        Vector3Int step = new Vector3Int(dir.x > 0 ? 1 : -1, dir.y > 0 ? 1 : -1, dir.z > 0 ? 1 : -1);

        float t = 0f;
        // Distance to first boundary along each axis
        float nextBoundaryX = (step.x > 0 ? cell.x + 1 : cell.x) - pos.x;
        float nextBoundaryY = (step.y > 0 ? cell.y + 1 : cell.y) - pos.y;
        float nextBoundaryZ = (step.z > 0 ? cell.z + 1 : cell.z) - pos.z;
        float tMaxX = (Mathf.Abs(dir.x) < 1e-8f) ? float.PositiveInfinity : nextBoundaryX / dir.x;
        float tMaxY = (Mathf.Abs(dir.y) < 1e-8f) ? float.PositiveInfinity : nextBoundaryY / dir.y;
        float tMaxZ = (Mathf.Abs(dir.z) < 1e-8f) ? float.PositiveInfinity : nextBoundaryZ / dir.z;
        if (tMaxX < 0) tMaxX = 0; if (tMaxY < 0) tMaxY = 0; if (tMaxZ < 0) tMaxZ = 0;
        float tDeltaX = (Mathf.Abs(dir.x) < 1e-8f) ? float.PositiveInfinity : Mathf.Abs(1f / dir.x);
        float tDeltaY = (Mathf.Abs(dir.y) < 1e-8f) ? float.PositiveInfinity : Mathf.Abs(1f / dir.y);
        float tDeltaZ = (Mathf.Abs(dir.z) < 1e-8f) ? float.PositiveInfinity : Mathf.Abs(1f / dir.z);

        Vector3Int prevCell = cell;
        Vector3 lastNormal = Vector3.zero;

        while (t <= maxDistance)
        {
            // Step to next cell
            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                prevCell = cell;
                cell.x += step.x;
                t = tMaxX;
                tMaxX += tDeltaX;
                lastNormal = new Vector3(-step.x, 0, 0); // face normal of the block entered
            }
            else if (tMaxY < tMaxZ)
            {
                prevCell = cell;
                cell.y += step.y;
                t = tMaxY;
                tMaxY += tDeltaY;
                lastNormal = new Vector3(0, -step.y, 0);
            }
            else
            {
                prevCell = cell;
                cell.z += step.z;
                t = tMaxZ;
                tMaxZ += tDeltaZ;
                lastNormal = new Vector3(0, 0, -step.z);
            }

            // Stop if out of vertical bounds
            if (cell.y < 0 || cell.y >= worldHeight) return false;

            // Check for solid block
            if (GetBlockType(cell) != BlockType.Air)
            {
                hitCell = cell;
                placeCell = prevCell;
                hitNormal = lastNormal;
                return true;
            }
        }

        return false;
    }
}
