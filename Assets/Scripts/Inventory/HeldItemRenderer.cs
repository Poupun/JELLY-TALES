using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Renders the currently selected hotbar entry (first: blocks) as a held item
/// in the bottom-right of the screen, Minecraft-style. Designed to be easily
/// extended to support items (flat sprite quads) afterward.
/// Attach this to the player camera (or a child under it). It will create an
/// internal holder transform with local offset / rotation to mimic hand pose.
/// </summary>
[DefaultExecutionOrder(200)]
public class HeldItemRenderer : MonoBehaviour
{
    [Header("References")] public UnifiedPlayerInventory inventory; public MiningSystem miningSystem; public PlayerController playerController;
    [Tooltip("Camera to track for sway (leave empty to auto-find)")]
    public Camera cameraToTrack;

    [Header("Block Display Settings")] public Vector3 blockLocalPosition = new Vector3(0.55f, -0.55f, 0.75f); public Vector3 blockLocalEuler = new Vector3(35f, 225f, 0f); public float blockScale = 0.35f;
    [Header("Idle Animation")]
    [Tooltip("Enable idle bobbing animation")]
    public bool enableIdleBob = true;
    [Tooltip("Y bob amplitude")] public float idleBobAmplitude = 0.02f; public float idleBobSpeed = 2.2f;
    [Tooltip("How fast we lerp toward target pose")] public float poseLerpSpeed = 10f;
    [Tooltip("Enable break/place swing animations")] public bool enableSwing = true; 
    [Header("Break (Mining) Swing")] public float breakSwingDuration = 0.25f; public float breakSwingAngle = 30f; public AnimationCurve breakSwingCurve = AnimationCurve.EaseInOut(0,0,1,1); public Vector3 breakSwingOffset = new Vector3(0,-0.05f,-0.05f);
    [Header("Placement Swing")] public float placeSwingDuration = 0.18f; public float placeSwingAngle = 22f; public AnimationCurve placeSwingCurve = AnimationCurve.EaseInOut(0,0,1,1); public Vector3 placeSwingOffset = new Vector3(0,-0.04f,-0.03f);
    [Header("Shared Swing Tweaks")] [Range(0f,1f)] public float swingSmoothing = 1f; public Vector3 swingAxis = Vector3.right;
    [Header("Camera Sway")]
    [Tooltip("Enable camera movement following for organic feeling")]
    public bool enableCameraSway = true;
    [Tooltip("How much the held item follows camera rotation (0 = no follow, 1 = full follow)")]
    [Range(0f, 1f)] public float cameraSwayStrength = 0.3f;
    [Tooltip("Speed multiplier for camera sway responsiveness (higher = faster response)")]
    [Range(0.1f, 5f)] public float cameraSwaySpeed = 1f;
    [Tooltip("Delay factor for camera following (higher = more delay)")]
    [Range(0.1f, 5f)] public float cameraSwayDelay = 1.5f;
    [Header("Item Switch Animation")]
    [Tooltip("Enable smooth animation when switching between different held items")]
    public bool enableSwitchAnimation = true;
    [Tooltip("Duration of the switch animation in seconds")]
    public float switchAnimationDuration = 0.25f;
    [Tooltip("How far down the item moves during switch (negative = down)")]
    public float switchDownOffset = -0.3f;
    [Tooltip("Animation curve for the switch animation")]
    public AnimationCurve switchAnimationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Header("Mining Loop (continuous while holding mouse)")]
    public bool loopWhileMining = true; public float miningLoopPeriod = 0.55f; public float miningLoopAngle = 30f; 
    public Vector3 miningLoopOffset = new Vector3(0,-0.045f,-0.045f); 
    public AnimationCurve miningLoopCurve = AnimationCurve.Linear(0,0,1,1); // Will be interpreted as up-down via triangle shaping below

    [Header("Item (2D) Settings")] public Vector3 itemLocalPosition = new Vector3(0.58f, -0.62f, 0.72f); public Vector3 itemLocalEuler = new Vector3(25f, 210f, 0f); public float itemScale = 0.52f; 
    [Tooltip("Uniform thickness (world units) for flat or outline extrusion")] public float itemDepth = 0.04f; 
    [Tooltip("Darken multiplier for side faces (0-1, lower = darker)")] [Range(0.1f, 1f)] public float sideDarken = 0.65f; 
    [Tooltip("Use sprite geometry (outline) instead of simple quad for flat extrusion mode")] public bool generateAccurateOutline = true; 
    [Tooltip("If true, build a voxel-style per-pixel 3D mesh giving each opaque pixel thickness like Minecraft item models")] public bool perPixelExtrusion = true; 
    [Range(0f,1f)] public float pixelAlphaThreshold = 0.1f; 
    [Tooltip("Optional max sprite size for voxelization (scales down sampling to cap cost). 0 = no cap")] public int maxVoxelSampleSize = 64; 
    [Tooltip("Merge contiguous pixels horizontally into strips to reduce poly count (only affects per-pixel mode)")] public bool mergeHorizontalStrips = true; 
    [Tooltip("Cache generated meshes/materials per item")] public bool cacheItemMeshes = true;
    [Tooltip("Use an unlit shader for items (crisper pixel look, no lighting gradients)")] public bool unlitItemMaterial = true;
    [Tooltip("Force held item sprite texture to Point filtering at runtime for sharp pixels")] public bool forcePointFilter = true;
    [Tooltip("Use a solid colour (no texture sampling) for the thin side faces to avoid alpha flicker")] public bool solidColorSides = true;

    [Header("Debug / State")] [SerializeField] private InventoryEntry currentEntry; [SerializeField] private BlockType shownBlock = BlockType.Air; [SerializeField] private ItemType shownItem = default;

    private Transform holder;            // Root pivot for all held visuals
    private GameObject blockObject;      // Cube for blocks
    private GameObject itemObject;       // Extruded sprite mesh for items
    private MeshFilter itemMeshFilter;   // Cache components
    private MeshRenderer itemRenderer;
    private static Dictionary<ItemType, Mesh> itemMeshCache = new Dictionary<ItemType, Mesh>();
    private static Dictionary<ItemType, Material> itemMaterialCache = new Dictionary<ItemType, Material>();

    [Header("Error Handling / Fallbacks")] public bool autoFallbackOnUnreadableTexture = true; public bool logUnreadableTextureWarning = true;
    private Material runtimeBlockMaterial; // Instance if we need per-block instancing modifications later

    private float swingTimer = 0f; private bool swinging = false; private bool swingIsPlacement = false; private Vector3 basePos; private Quaternion baseRot;
    
    // Switch animation state
    private float switchTimer = 0f;
    private bool isSwitching = false;
    private InventoryEntry pendingEntry; // The new item to switch to
    private bool switchPhaseOut = true; // true = animating old item out, false = animating new item in
    
    // Camera sway state
    private Vector3 lastCameraRotation;
    private Vector3 lastCameraPosition;
    private Vector3 cameraSwayOffset;
    private Vector3 cameraSwayVelocity;

    void Awake()
    {
        EnsureReferences();
        EnsureHolder();
    }

    void Start()
    {
        Subscribe();
        ForceRefresh();
        
        // Initialize camera sway tracking
        if (enableCameraSway && cameraToTrack != null)
        {
            lastCameraRotation = cameraToTrack.transform.eulerAngles;
            lastCameraPosition = cameraToTrack.transform.position;
            cameraSwayOffset = Vector3.zero;
            cameraSwayVelocity = Vector3.zero;
        }
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void EnsureReferences()
    {
        if (inventory == null) inventory = FindFirstObjectByType<UnifiedPlayerInventory>();
        if (miningSystem == null) miningSystem = FindFirstObjectByType<MiningSystem>();
        if (playerController == null) playerController = FindFirstObjectByType<PlayerController>();
        if (cameraToTrack == null) cameraToTrack = GetComponentInParent<Camera>();
        if (cameraToTrack == null) cameraToTrack = Camera.main;
    }

    void Subscribe()
    {
        if (inventory != null) inventory.OnInventoryChanged += HandleInventoryChanged;
        if (miningSystem != null)
        {
            miningSystem.OnMiningStarted += () => HandleSwing(false); // start mining swing
            miningSystem.OnBlockBroken += _ => HandleSwing(false); // reinforce at break
        }
        if (playerController != null)
        {
            playerController.OnBlockPlaced += (_, __) => HandleSwing(true);
        }
    }

    void Unsubscribe()
    {
        if (inventory != null) inventory.OnInventoryChanged -= HandleInventoryChanged;
        if (miningSystem != null)
        {
            // We used lambdas; can't directly unsubscribe those inline. In production store delegates.
        }
        if (playerController != null)
        {
            playerController.OnBlockPlaced -= (_, __) => HandleSwing(true); // same lambda caveat
        }
    }

    void EnsureHolder()
    {
        if (holder != null) return;
        holder = new GameObject("HeldItemHolder").transform;
        holder.SetParent(transform, false);
        holder.localPosition = Vector3.zero;
        holder.localRotation = Quaternion.identity;
        holder.localScale = Vector3.one;
    }

    void HandleInventoryChanged()
    {
        RefreshIfChanged();
    }

    void ForceRefresh()
    {
        currentEntry = default; // force mismatch
        RefreshIfChanged(force:true);
    }

    void RefreshIfChanged(bool force = false)
    {
        if (inventory == null) return;
        var selected = inventory.GetSelectedEntry();

        bool changed = force || EntryChanged(selected, currentEntry);
        if (!changed) return;

        // If switch animations are enabled and we're not already switching
        if (enableSwitchAnimation && !isSwitching && !force)
        {
            // Start switch animation
            pendingEntry = selected;
            StartSwitchAnimation();
        }
        else
        {
            // Immediate switch (no animation or forced)
            currentEntry = selected;
            BuildVisualForCurrentEntry();
        }
    }

    bool EntryChanged(InventoryEntry a, InventoryEntry b)
    {
        if (a.entryType != b.entryType) return true;
        if (a.entryType == InventoryEntryType.Block) return a.blockType != b.blockType || a.IsEmpty != b.IsEmpty;
        return a.itemType != b.itemType || a.IsEmpty != b.IsEmpty;
    }

    void BuildVisualForCurrentEntry()
    {
        if (currentEntry.IsEmpty)
        {
            SetActive(blockObject, false);
            SetActive(itemObject, false);
            shownBlock = BlockType.Air; shownItem = default;
            return;
        }

        if (currentEntry.entryType == InventoryEntryType.Block)
        {
            ShowBlock(currentEntry.blockType);
        }
        else
        {
            // Items to implement next: for now hide block and show placeholder (or nothing)
            SetActive(blockObject, false);
            ShowItem(currentEntry.itemType); // placeholder path
        }
    }

    void ShowBlock(BlockType blockType)
    {
        EnsureBlockObject();

        if (shownBlock != blockType)
        {
            // Assign material
            Material mat = BlockManager.GetBlockMaterial(blockType);
            if (mat != null)
            {
                // For safety, use a shared instance without modifying original (future modifications: emission / highlight)
                runtimeBlockMaterial = mat; // not instanced yet; if modifications needed use new Material(mat)
                var renderer = blockObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = runtimeBlockMaterial;
            }
            shownBlock = blockType;
        }

        // Activate
        SetActive(blockObject, true);
        SetActive(itemObject, false);
    }

    void EnsureBlockObject()
    {
        if (blockObject != null) return;
        blockObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blockObject.name = "HeldBlock";
        blockObject.transform.SetParent(holder, false);
        blockObject.transform.localPosition = blockLocalPosition;
        blockObject.transform.localRotation = Quaternion.Euler(blockLocalEuler);
        blockObject.transform.localScale = Vector3.one * blockScale;

        // Remove collider (not needed in first-person overlay)
        var col = blockObject.GetComponent<Collider>(); if (col) Destroy(col);
    }

    void ShowItem(ItemType itemType)
    {
        EnsureItemObject();
        if (shownItem != itemType)
        {
            // Acquire sprite
            Sprite sprite = ItemManager.GetItemSprite(itemType);
            if (sprite == null)
            {
                Debug.LogWarning($"[HeldItemRenderer] No sprite for item {itemType}");
                // fallback hide
                SetActive(itemObject, false); return;
            }

            // Build / assign mesh
            Mesh mesh = null;
            if (cacheItemMeshes && itemMeshCache.TryGetValue(itemType, out mesh) && mesh != null)
            {
                itemMeshFilter.sharedMesh = mesh;
            }
            else
            {
                if (perPixelExtrusion)
                {
                    mesh = CreateVoxelizedItemMesh(sprite, itemDepth, pixelAlphaThreshold, mergeHorizontalStrips);
                    if (mesh == null) // fallback triggered
                    {
                        mesh = CreateExtrudedItemMesh(sprite, itemDepth, generateAccurateOutline);
                    }
                }
                else
                {
                    mesh = CreateExtrudedItemMesh(sprite, itemDepth, generateAccurateOutline);
                }
                if (cacheItemMeshes && mesh != null) itemMeshCache[itemType] = mesh;
                itemMeshFilter.sharedMesh = mesh;
            }

            // Material
            if (!itemMaterialCache.TryGetValue(itemType, out var mat) || mat == null)
            {
                mat = CreateItemMaterial(sprite);
                if (cacheItemMeshes) itemMaterialCache[itemType] = mat;
            }
            itemRenderer.sharedMaterial = mat;
            shownItem = itemType;
        }
        SetActive(itemObject, true);
        SetActive(blockObject, false);
    }

    void EnsureItemObject()
    {
        if (itemObject != null) return;
        itemObject = new GameObject("HeldItem");
        itemObject.transform.SetParent(holder, false);
        itemObject.transform.localPosition = itemLocalPosition;
        itemObject.transform.localRotation = Quaternion.Euler(itemLocalEuler);
        itemObject.transform.localScale = Vector3.one * itemScale;
        itemMeshFilter = itemObject.AddComponent<MeshFilter>();
        itemRenderer = itemObject.AddComponent<MeshRenderer>();
        itemRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        itemRenderer.receiveShadows = false;
        itemRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    Material CreateItemMaterial(Sprite sprite)
    {
        Shader shader = null;
        if (unlitItemMaterial)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
        }
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
        }
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", sprite.texture);
        else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", sprite.texture);
        if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.3f);
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f); // URP Unlit alpha clip toggle
        mat.EnableKeyword("_ALPHATEST_ON");
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0); // opaque
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
        if (forcePointFilter && sprite.texture != null) { sprite.texture.filterMode = FilterMode.Point; sprite.texture.anisoLevel = 0; }
        return mat;
    }

    Mesh CreateExtrudedItemMesh(Sprite sprite, float depth, bool accurateOutline)
    {
        Mesh m = new Mesh();
        var vertsData = sprite.vertices;
        var trisData = sprite.triangles;
        var uvData = sprite.uv;
        float half = depth * 0.5f;
        
        if (vertsData == null || vertsData.Length == 0 || !accurateOutline)
        {
            // Simple rectangle with improved normals and UVs
            var r = sprite.rect; float ppu = sprite.pixelsPerUnit; float w = r.width/ppu; float h = r.height/ppu;
            
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<Vector3> norms = new List<Vector3>();
            List<int> tris = new List<int>();
            
            Color sideCol = new Color(sideDarken, sideDarken, sideDarken, 1f);
            Vector2 sideUV = solidColorSides ? Vector2.one * 0.5f : new Vector2(0.5f, 0.5f);
            
            void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uA, Vector2 uB, Vector2 uC, Vector2 uD, Color col, Vector3 normal)
            {
                int s = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                uvs.Add(uA); uvs.Add(uB); uvs.Add(uC); uvs.Add(uD);
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                norms.Add(normal); norms.Add(normal); norms.Add(normal); norms.Add(normal);
                tris.Add(s); tris.Add(s+1); tris.Add(s+2);
                tris.Add(s); tris.Add(s+2); tris.Add(s+3);
            }
            
            // Front face (Z+)
            AddQuad(new Vector3(-w*0.5f,-h*0.5f, half), new Vector3(w*0.5f,-h*0.5f, half), 
                   new Vector3(w*0.5f,h*0.5f, half), new Vector3(-w*0.5f,h*0.5f, half),
                   new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1),
                   Color.white, Vector3.forward);
            
            // Back face (Z-)
            AddQuad(new Vector3(w*0.5f,-h*0.5f,-half), new Vector3(-w*0.5f,-h*0.5f,-half), 
                   new Vector3(-w*0.5f,h*0.5f,-half), new Vector3(w*0.5f,h*0.5f,-half),
                   new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1),
                   Color.white, Vector3.back);
            
            // Bottom (Y-)
            AddQuad(new Vector3(-w*0.5f,-h*0.5f,-half), new Vector3(w*0.5f,-h*0.5f,-half), 
                   new Vector3(w*0.5f,-h*0.5f, half), new Vector3(-w*0.5f,-h*0.5f, half),
                   sideUV, sideUV, sideUV, sideUV, sideCol, Vector3.down);
            
            // Top (Y+)
            AddQuad(new Vector3(-w*0.5f,h*0.5f, half), new Vector3(w*0.5f,h*0.5f, half), 
                   new Vector3(w*0.5f,h*0.5f,-half), new Vector3(-w*0.5f,h*0.5f,-half),
                   sideUV, sideUV, sideUV, sideUV, sideCol, Vector3.up);
            
            // Left (X-)
            AddQuad(new Vector3(-w*0.5f,-h*0.5f,-half), new Vector3(-w*0.5f,-h*0.5f, half), 
                   new Vector3(-w*0.5f,h*0.5f, half), new Vector3(-w*0.5f,h*0.5f,-half),
                   sideUV, sideUV, sideUV, sideUV, sideCol, Vector3.left);
            
            // Right (X+)
            AddQuad(new Vector3(w*0.5f,-h*0.5f, half), new Vector3(w*0.5f,-h*0.5f,-half), 
                   new Vector3(w*0.5f,h*0.5f,-half), new Vector3(w*0.5f,h*0.5f, half),
                   sideUV, sideUV, sideUV, sideUV, sideCol, Vector3.right);
            
            m.SetVertices(verts); 
            m.SetUVs(0, uvs); 
            m.SetColors(cols); 
            m.SetTriangles(tris, 0); 
            m.SetNormals(norms);
            m.RecalculateTangents();
            return m;
        }

        // Complex sprite outline extrusion with proper normals
        int count = vertsData.Length;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvCoords = new List<Vector2>();
        List<Color> colors = new List<Color>();
        List<Vector3> vertexNormals = new List<Vector3>();
        
        // Front vertices
        for (int i = 0; i < count; i++)
        {
            Vector2 v = vertsData[i];
            vertices.Add(new Vector3(v.x, v.y, half));
            uvCoords.Add(uvData[i]);
            colors.Add(Color.white);
            vertexNormals.Add(Vector3.forward);
        }
        
        // Back vertices
        for (int i = 0; i < count; i++)
        {
            Vector2 v = vertsData[i];
            vertices.Add(new Vector3(v.x, v.y, -half));
            uvCoords.Add(uvData[i]);
            colors.Add(Color.white);
            vertexNormals.Add(Vector3.back);
        }
        
        List<int> triangles = new List<int>();
        
        // Front triangles
        for (int t = 0; t < trisData.Length; t += 3)
        {
            triangles.Add(trisData[t]);
            triangles.Add(trisData[t+1]);
            triangles.Add(trisData[t+2]);
        }
        
        // Back triangles (reversed winding)
        for (int t = 0; t < trisData.Length; t += 3)
        {
            triangles.Add(trisData[t+2] + count);
            triangles.Add(trisData[t+1] + count);
            triangles.Add(trisData[t] + count);
        }

        // Generate side faces along outline edges
        var edgeUse = new Dictionary<(int,int), int>();
        void AddEdge(int a, int b)
        { 
            var key = a < b ? (a, b) : (b, a); 
            if (edgeUse.ContainsKey(key)) edgeUse[key]++; 
            else edgeUse[key] = 1; 
        }
        
        for (int t = 0; t < trisData.Length; t += 3)
        { 
            AddEdge(trisData[t], trisData[t+1]); 
            AddEdge(trisData[t+1], trisData[t+2]); 
            AddEdge(trisData[t+2], trisData[t]); 
        }
        
        Color sideColor = new Color(sideDarken, sideDarken, sideDarken, 1f);
        Vector2 centerUV = solidColorSides ? Vector2.one * 0.5f : Vector2.zero;
        
        foreach (var kv in edgeUse)
        { 
            if (kv.Value != 1) continue; // Only outline edges
            
            int a = kv.Key.Item1; int b = kv.Key.Item2; 
            int aFront = a; int bFront = b; 
            int aBack = a + count; int bBack = b + count;
            
            // Calculate edge normal (perpendicular to edge, pointing outward)
            Vector3 edgeDir = (vertices[bFront] - vertices[aFront]).normalized;
            Vector3 edgeNormal = new Vector3(-edgeDir.y, edgeDir.x, 0).normalized;
            
            // Add side quad with proper winding and normals
            int vertStart = vertices.Count;
            vertices.Add(vertices[aFront]); vertices.Add(vertices[bFront]); 
            vertices.Add(vertices[bBack]); vertices.Add(vertices[aBack]);
            
            uvCoords.Add(centerUV); uvCoords.Add(centerUV); 
            uvCoords.Add(centerUV); uvCoords.Add(centerUV);
            
            colors.Add(sideColor); colors.Add(sideColor); 
            colors.Add(sideColor); colors.Add(sideColor);
            
            vertexNormals.Add(edgeNormal); vertexNormals.Add(edgeNormal); 
            vertexNormals.Add(edgeNormal); vertexNormals.Add(edgeNormal);
            
            triangles.Add(vertStart); triangles.Add(vertStart+1); triangles.Add(vertStart+2);
            triangles.Add(vertStart); triangles.Add(vertStart+2); triangles.Add(vertStart+3);
        }

        m.SetVertices(vertices); 
        m.SetUVs(0, uvCoords); 
        m.SetColors(colors); 
        m.SetTriangles(triangles, 0, false); 
        m.SetNormals(vertexNormals);
        m.RecalculateTangents();
        return m;
    }

    Mesh CreateVoxelizedItemMesh(Sprite sprite, float depth, float alphaThreshold, bool mergeStrips)
    {
        if (sprite == null || sprite.texture == null) return null;
        Texture2D tex = sprite.texture;
        bool readable = true; try { _ = tex.isReadable; readable = tex.isReadable; } catch { readable = false; }
        if (!readable){ if (logUnreadableTextureWarning) Debug.LogWarning($"[HeldItemRenderer] '{tex.name}' not readable; voxel mode skipped."); return null; }

        Rect r = sprite.rect; int texW = (int)r.width; int texH = (int)r.height;
        int step = 1;
        if (maxVoxelSampleSize>0 && (texW>maxVoxelSampleSize || texH>maxVoxelSampleSize))
        { float scale = Mathf.Max((float)texW/maxVoxelSampleSize,(float)texH/maxVoxelSampleSize); step = Mathf.CeilToInt(scale); }

        Color[] pixels; try { pixels = tex.GetPixels((int)r.x,(int)r.y,texW,texH); } catch { return null; }
        int sw = texW/step; int sh = texH/step;
        bool[,] solid = new bool[sw,sh];
        for(int y=0;y<sh;y++) for(int x=0;x<sw;x++){ float aMax=0f; for(int oy=0;oy<step;oy++){ int py=y*step+oy; if(py>=texH) break; for(int ox=0;ox<step;ox++){ int px=x*step+ox; if(px>=texW) break; float a=pixels[py*texW+px].a; if(a>aMax)aMax=a; }} solid[x,y]=aMax>=alphaThreshold; }

        List<Vector3> verts = new List<Vector3>(); List<Vector2> uvs = new List<Vector2>(); List<Color> cols = new List<Color>(); List<int> tris = new List<int>(); List<Vector3> meshNormals = new List<Vector3>();
        float ppu = sprite.pixelsPerUnit; float half = depth*0.5f; float unit = step/ppu; float originX = -(texW/ppu)*0.5f; float originY = -(texH/ppu)*0.5f;
        Color sideCol = new Color(sideDarken,sideDarken,sideDarken,1f); Color faceCol=Color.white;
        
        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uA, Vector2 uB, Vector2 uC, Vector2 uD, Color col, Vector3 normal)
        {
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(uA); uvs.Add(uB); uvs.Add(uC); uvs.Add(uD);
            cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
            meshNormals.Add(normal); meshNormals.Add(normal); meshNormals.Add(normal); meshNormals.Add(normal);
            // Proper triangle winding for outward-facing normals
            tris.Add(s); tris.Add(s+1); tris.Add(s+2);
            tris.Add(s); tris.Add(s+2); tris.Add(s+3);
        }
        
        bool IsSolid(int x,int y){ if(x<0||y<0||x>=sw||y>=sh) return false; return solid[x,y]; }
        
        // Get averaged color for side UV mapping - find a representative opaque color
        Vector2 GetSideUV(int pixelX, int pixelY)
        {
            if (solidColorSides)
            {
                // Find first fully opaque pixel near this location
                int searchRadius = Mathf.Max(step, 3);
                for (int radius = 0; radius <= searchRadius; radius++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int px = pixelX + dx; int py = pixelY + dy;
                            if (px >= 0 && py >= 0 && px < texW && py < texH && pixels[py*texW + px].a >= alphaThreshold)
                            {
                                return new Vector2((r.x + px + 0.5f) / tex.width, (r.y + py + 0.5f) / tex.height);
                            }
                        }
                    }
                }
            }
            // Fallback to center UV of this pixel
            return new Vector2((r.x + pixelX + 0.5f) / tex.width, (r.y + pixelY + 0.5f) / tex.height);
        }

        // Front/back faces with proper normals
        if (mergeStrips)
        {
            for (int y=0;y<sh;y++)
            {
                int x=0;
                while (x<sw)
                {
                    if (!solid[x,y]) { x++; continue; }
                    int startX = x;
                    while (x+1<sw && solid[x+1,y]) x++;
                    int endX = x;
                    
                    float px0=originX + startX*unit; float px1=originX + (endX+1)*unit; 
                    float py0=originY + y*unit; float py1=py0+unit;
                    float u0=(r.x + startX*step)/tex.width; float u1=(r.x + (endX+1)*step)/tex.width; 
                    float v0=(r.y + y*step)/tex.height; float v1=(r.y + (y+1)*step)/tex.height;
                    
                    Vector2 UVA=new Vector2(u0,v0), UVB=new Vector2(u1,v0), UVC=new Vector2(u1,v1), UVD=new Vector2(u0,v1);
                    
                    // Front face (positive Z normal)
                    AddQuad(new Vector3(px0,py0,half), new Vector3(px1,py0,half), new Vector3(px1,py1,half), new Vector3(px0,py1,half), 
                            UVA, UVB, UVC, UVD, faceCol, Vector3.forward);
                    
                    // Back face (negative Z normal) - flip UV horizontal for correct orientation
                    AddQuad(new Vector3(px1,py0,-half), new Vector3(px0,py0,-half), new Vector3(px0,py1,-half), new Vector3(px1,py1,-half), 
                            UVA, UVB, UVC, UVD, faceCol, Vector3.back);
                    x++;
                }
            }
        }
        else
        {
            for(int y=0;y<sh;y++) for(int x=0;x<sw;x++) if(solid[x,y])
            { 
                float px0=originX + x*unit; float px1=px0+unit; float py0=originY + y*unit; float py1=py0+unit; 
                float u0=(r.x + x*step)/tex.width; float u1=(r.x + (x+1)*step)/tex.width; 
                float v0=(r.y + y*step)/tex.height; float v1=(r.y + (y+1)*step)/tex.height; 
                Vector2 UVA=new Vector2(u0,v0), UVB=new Vector2(u1,v0), UVC=new Vector2(u1,v1), UVD=new Vector2(u0,v1);
                
                // Front face (positive Z normal)
                AddQuad(new Vector3(px0,py0,half), new Vector3(px1,py0,half), new Vector3(px1,py1,half), new Vector3(px0,py1,half), 
                        UVA, UVB, UVC, UVD, faceCol, Vector3.forward);
                
                // Back face (negative Z normal) - flip UV horizontal
                AddQuad(new Vector3(px1,py0,-half), new Vector3(px0,py0,-half), new Vector3(px0,py1,-half), new Vector3(px1,py1,-half), 
                        UVA, UVB, UVC, UVD, faceCol, Vector3.back);
            }
        }

        // Side faces with proper normals and UV mapping
        for(int y=0;y<sh;y++) for(int x=0;x<sw;x++) if(solid[x,y])
        { 
            float px0=originX + x*unit; float px1=px0+unit; float py0=originY + y*unit; float py1=py0+unit; 
            int centerPixelX = x * step + step/2; int centerPixelY = y * step + step/2;
            Vector2 sideUV = GetSideUV(centerPixelX, centerPixelY);
            
            // Left side (-X normal)
            if(!IsSolid(x-1,y)) 
            {
                AddQuad(new Vector3(px0,py0,-half), new Vector3(px0,py0,half), new Vector3(px0,py1,half), new Vector3(px0,py1,-half),
                        sideUV, sideUV, sideUV, sideUV, sideCol, Vector3.left);
            }
            
            // Right side (+X normal)  
            if(!IsSolid(x+1,y)) 
            {
                AddQuad(new Vector3(px1,py0,half), new Vector3(px1,py0,-half), new Vector3(px1,py1,-half), new Vector3(px1,py1,half),
                        sideUV, sideUV, sideUV, sideUV, sideCol, Vector3.right);
            }
            
            // Bottom side (-Y normal)
            if(!IsSolid(x,y-1)) 
            {
                AddQuad(new Vector3(px0,py0,-half), new Vector3(px1,py0,-half), new Vector3(px1,py0,half), new Vector3(px0,py0,half),
                        sideUV, sideUV, sideUV, sideUV, sideCol, Vector3.down);
            }
            
            // Top side (+Y normal)
            if(!IsSolid(x,y+1)) 
            {
                AddQuad(new Vector3(px0,py1,half), new Vector3(px1,py1,half), new Vector3(px1,py1,-half), new Vector3(px0,py1,-half),
                        sideUV, sideUV, sideUV, sideUV, sideCol, Vector3.up);
            }
        }
        
        Mesh m = new Mesh(); 
        m.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16; 
        m.SetVertices(verts); 
        m.SetUVs(0, uvs); 
        m.SetColors(cols); 
        m.SetTriangles(tris, 0, false); 
        m.SetNormals(meshNormals); // Use our custom normals instead of recalculating
        m.RecalculateTangents();
        return m;
    }

    void ApplySideVertexColor(Mesh m, Sprite sprite, float darken)
    {
        // Simple heuristic: vertices with normal not facing camera z (|nz|<0.9) are sides -> darken 
        // Fixed variable naming conflict
        var meshNormals = m.normals; var cols = m.colors.Length==meshNormals.Length? m.colors : new Color[meshNormals.Length];
        for (int i=0;i<meshNormals.Length;i++)
        {
            if (Mathf.Abs(meshNormals[i].z) < 0.9f) cols[i] = new Color(darken,darken,darken,1f); else cols[i] = Color.white;
        }
        m.colors = cols;
    }

    void Update()
    {
        if (inventory == null) return; // wait until available
        // Poll in case selection changed without event (e.g. early frame). Typically event-based; safe polling.
        RefreshIfChanged();
        UpdateSwitchAnimation();
        UpdateCameraSway();
        AnimateIdleAndSwing();
    }

    void AnimateIdleAndSwing()
    {
        if (holder == null) return;
        // Choose active object (block for now)
        Transform active = null;
        if (blockObject != null && blockObject.activeSelf) active = blockObject.transform; else if (itemObject != null && itemObject.activeSelf) active = itemObject.transform; else return;

        // Base target pose differs for block vs item
        Vector3 targetPos = (active == blockObject?.transform) ? blockLocalPosition : itemLocalPosition;
        Quaternion targetRot = Quaternion.Euler((active == blockObject?.transform) ? blockLocalEuler : itemLocalEuler);
        Vector3 targetScale = (active == blockObject?.transform) ? Vector3.one * blockScale : Vector3.one * itemScale;

        // Idle bob
        float bob = enableIdleBob ? Mathf.Sin(Time.time * idleBobSpeed) * idleBobAmplitude : 0f;
        Vector3 bobOffset = new Vector3(0, bob, 0);

        // Switch animation offset
        float switchOffset = GetSwitchAnimationOffset();
        Vector3 switchOffsetVector = new Vector3(0, switchOffset, 0);

        // Swing overlay
        float breakFactor = 0f; float duration= breakSwingDuration; float angle= breakSwingAngle; AnimationCurve curveRef = breakSwingCurve; Vector3 swingBaseOffset = breakSwingOffset;
        if (swingIsPlacement){ duration = placeSwingDuration; angle = placeSwingAngle; curveRef = placeSwingCurve; swingBaseOffset = placeSwingOffset; }
        if (enableSwing && swinging)
        {
            swingTimer += Time.deltaTime;
            float t = Mathf.Clamp01(swingTimer / Mathf.Max(0.01f,duration));
            float curve = curveRef != null ? curveRef.Evaluate(t) : t;
            breakFactor = 1f - curve;
            if (t >= 1f) swinging = false;
        }
        // Continuous mining loop (only for mining, not placement swings)
        float loopFactor = 0f;
        if (!swingIsPlacement && loopWhileMining && enableSwing && miningSystem != null && miningSystem.IsMining)
        {
            float phase = Mathf.Repeat(Time.time, Mathf.Max(0.05f, miningLoopPeriod)) / Mathf.Max(0.05f,miningLoopPeriod);
            // Shape: rise and fall (triangle) mapped through optional curve
            float tri = phase < 0.5f ? (phase*2f) : (2f - phase*2f); // 0->1->0
            float shaped = miningLoopCurve != null ? miningLoopCurve.Evaluate(tri) : tri;
            loopFactor = shaped;
        }
        // Combine one-shot (breakFactor) and loop (loopFactor)
        float finalAngle = breakFactor * angle + loopFactor * miningLoopAngle;
        Vector3 finalOffset = swingBaseOffset * breakFactor + miningLoopOffset * loopFactor;
        Quaternion swingRot = Quaternion.AngleAxis(finalAngle, swingAxis.sqrMagnitude>0.0001f? swingAxis.normalized : Vector3.right);
        Vector3 swingPosOffset = finalOffset;

        // Lerp to final
        active.localPosition = Vector3.Lerp(active.localPosition, targetPos + bobOffset + swingPosOffset + switchOffsetVector + cameraSwayOffset, Time.deltaTime * poseLerpSpeed);
        active.localRotation = Quaternion.Slerp(active.localRotation, swingRot * targetRot, Time.deltaTime * poseLerpSpeed);
        active.localScale = Vector3.Lerp(active.localScale, targetScale, Time.deltaTime * poseLerpSpeed * 2f);
    }

    void StartSwitchAnimation()
    {
        if (!enableSwitchAnimation) return;
        
        isSwitching = true;
        switchTimer = 0f;
        switchPhaseOut = true; // Start with phase out
        
        Debug.Log($"[HeldItemRenderer] Starting switch animation: {currentEntry.entryType}:{(currentEntry.entryType == InventoryEntryType.Block ? currentEntry.blockType.ToString() : currentEntry.itemType.ToString())} -> {pendingEntry.entryType}:{(pendingEntry.entryType == InventoryEntryType.Block ? pendingEntry.blockType.ToString() : pendingEntry.itemType.ToString())}");
    }
    
    void UpdateSwitchAnimation()
    {
        if (!isSwitching) return;
        
        switchTimer += Time.deltaTime;
        float halfDuration = switchAnimationDuration * 0.5f;
        
        if (switchPhaseOut)
        {
            // Phase 1: Animate current item out (down and fade)
            if (switchTimer >= halfDuration)
            {
                // Switch to phase in
                switchPhaseOut = false;
                switchTimer = 0f;
                
                // Actually switch the item now (at the midpoint)
                currentEntry = pendingEntry;
                BuildVisualForCurrentEntry();
            }
        }
        else
        {
            // Phase 2: Animate new item in (up from bottom)
            if (switchTimer >= halfDuration)
            {
                // Animation complete
                isSwitching = false;
                switchTimer = 0f;
            }
        }
    }
    
    void UpdateCameraSway()
    {
        if (!enableCameraSway || cameraToTrack == null) return;
        
        // Get current camera state
        Vector3 currentCameraRotation = cameraToTrack.transform.eulerAngles;
        Vector3 currentCameraPosition = cameraToTrack.transform.position;
        
        // Calculate rotation velocity (degrees per second)
        Vector3 rotationVelocity = Vector3.zero;
        if (Time.deltaTime > 0)
        {
            rotationVelocity.x = Mathf.DeltaAngle(lastCameraRotation.x, currentCameraRotation.x) / Time.deltaTime;
            rotationVelocity.y = Mathf.DeltaAngle(lastCameraRotation.y, currentCameraRotation.y) / Time.deltaTime;
            rotationVelocity.z = Mathf.DeltaAngle(lastCameraRotation.z, currentCameraRotation.z) / Time.deltaTime;
        }
        
        // Calculate position velocity (units per second)
        Vector3 positionVelocity = Vector3.zero;
        if (Time.deltaTime > 0)
        {
            Vector3 positionDelta = currentCameraPosition - lastCameraPosition;
            positionVelocity = positionDelta / Time.deltaTime;
            // Convert to local space relative to camera
            positionVelocity = cameraToTrack.transform.InverseTransformDirection(positionVelocity);
        }
        
        // Calculate target sway velocity based on camera movement
        Vector3 targetSwayVelocity = Vector3.zero;
        
        // Rotation-based sway velocity
        targetSwayVelocity.x += -rotationVelocity.y * cameraSwayStrength * 0.001f; // Horizontal rotation -> horizontal sway
        targetSwayVelocity.y += -rotationVelocity.x * cameraSwayStrength * 0.001f; // Vertical rotation -> vertical sway
        
        // Position-based sway velocity (walking bob, head movement, etc.)
        targetSwayVelocity.x += -positionVelocity.x * cameraSwayStrength * 0.5f; // Side movement
        targetSwayVelocity.y += -positionVelocity.y * cameraSwayStrength * 0.5f; // Up/down movement  
        targetSwayVelocity.z += -positionVelocity.z * cameraSwayStrength * 0.3f; // Forward/back movement
        
        // Apply spring-damper physics for continuous animation
        float springForce = 5f * cameraSwaySpeed; // How strong the spring pulls back to center
        float dampening = 2f * cameraSwaySpeed;   // How much dampening to prevent oscillation
        
        // Calculate forces
        Vector3 springAcceleration = -cameraSwayOffset * springForce; // Spring force toward center
        Vector3 dampenAcceleration = -cameraSwayVelocity * dampening; // Dampening force
        Vector3 inputAcceleration = targetSwayVelocity * 10f * cameraSwaySpeed; // Input from camera movement
        
        // Update velocity and position using physics
        cameraSwayVelocity += (springAcceleration + dampenAcceleration + inputAcceleration) * Time.deltaTime;
        cameraSwayOffset += cameraSwayVelocity * Time.deltaTime;
        
        // Update last state
        lastCameraRotation = currentCameraRotation;
        lastCameraPosition = currentCameraPosition;
        
        // Debug info (disabled to reduce console spam)
        // if (Application.isPlaying && (positionVelocity.magnitude > 0.1f || rotationVelocity.magnitude > 5f))
        // {
        //     Debug.Log($"[CameraSway] PosVel: {positionVelocity:F2}, RotVel: {rotationVelocity:F1}, SwayOffset: {cameraSwayOffset:F3}");
        // }
    }
    
    float GetSwitchAnimationOffset()
    {
        if (!isSwitching) return 0f;
        
        float halfDuration = switchAnimationDuration * 0.5f;
        float t = Mathf.Clamp01(switchTimer / halfDuration);
        float curveValue = switchAnimationCurve.Evaluate(t);
        
        if (switchPhaseOut)
        {
            // Phase out: move from 0 to switchDownOffset
            return Mathf.Lerp(0f, switchDownOffset, curveValue);
        }
        else
        {
            // Phase in: move from switchDownOffset to 0
            return Mathf.Lerp(switchDownOffset, 0f, curveValue);
        }
    }

    void HandleSwing(bool placement)
    {
        if (!enableSwing) return;
        swinging = true; swingTimer = 0f; swingIsPlacement = placement;
    }

    void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
