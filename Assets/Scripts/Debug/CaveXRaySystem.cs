using UnityEngine;
using System.Collections.Generic;

namespace CaveDebug
{
    /// <summary>
    /// X-Ray vision system for debugging cave generation
    /// Allows seeing underground structures and cave systems
    /// </summary>
    public class CaveXRaySystem : MonoBehaviour
    {
        [Header("X-Ray Debug Settings")]
        [Tooltip("Enable X-Ray vision (F3 to toggle)")]
        public bool enableXRay = false;
        
        [Tooltip("X-Ray mode type")]
        public XRayMode xrayMode = XRayMode.CaveHighlight;
        
        [Tooltip("Maximum distance to apply X-Ray effect")]
        [Range(10f, 100f)] public float xrayDistance = 50f;
        
        [Tooltip("Alpha value for transparent blocks")]
        [Range(0.1f, 0.8f)] public float transparentAlpha = 0.3f;
        
        [Tooltip("Cave highlight color")]
        public Color caveHighlightColor = Color.cyan;
        
        [Tooltip("Ore highlight color")]
        public Color oreHighlightColor = Color.yellow;
        
        public enum XRayMode
        {
            CaveHighlight,      // Highlight caves in bright color
            Transparent,        // Make non-cave blocks transparent  
            CaveOnly,          // Show only caves and ores
            FullUnderground    // Show entire underground structure
        }
        
        private Transform playerTransform;
        private WorldGenerator worldGenerator;
        private Dictionary<Renderer, Material> originalMaterials = new Dictionary<Renderer, Material>();
        private Dictionary<Renderer, Material> xrayMaterials = new Dictionary<Renderer, Material>();
        private bool lastXRayState = false;
        
        // X-Ray materials
        private Material transparentMaterial;
        private Material caveHighlightMaterial;
        private Material oreHighlightMaterial;
        
        void Start()
        {
            // Find player and world generator
            if (GameObject.FindWithTag("Player") != null)
                playerTransform = GameObject.FindWithTag("Player").transform;
            else
                playerTransform = Camera.main?.transform;
                
            worldGenerator = FindObjectOfType<WorldGenerator>();
            
            CreateXRayMaterials();
        }
        
        void Update()
        {
            // Toggle X-Ray with F3
            if (Input.GetKeyDown(KeyCode.F3))
            {
                enableXRay = !enableXRay;
                UnityEngine.Debug.Log($"X-Ray Vision: {(enableXRay ? "ON" : "OFF")}");
            }
            
            // Apply/remove X-Ray effect when state changes
            if (enableXRay != lastXRayState)
            {
                if (enableXRay)
                    ApplyXRayEffect();
                else
                    RemoveXRayEffect();
                    
                lastXRayState = enableXRay;
            }
            
            // Update X-Ray effect if enabled
            if (enableXRay && playerTransform != null)
            {
                UpdateXRayEffect();
            }
        }
        
        void CreateXRayMaterials()
        {
            // Create transparent material
            transparentMaterial = new Material(Shader.Find("Standard"));
            transparentMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            transparentMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            transparentMaterial.SetInt("_ZWrite", 0);
            transparentMaterial.DisableKeyword("_ALPHATEST_ON");
            transparentMaterial.EnableKeyword("_ALPHABLEND_ON");
            transparentMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            transparentMaterial.renderQueue = 3000;
            
            // Create cave highlight material
            caveHighlightMaterial = new Material(Shader.Find("Standard"));
            caveHighlightMaterial.color = caveHighlightColor;
            caveHighlightMaterial.SetFloat("_Metallic", 0f);
            caveHighlightMaterial.SetFloat("_Smoothness", 0.5f);
            caveHighlightMaterial.EnableKeyword("_EMISSION");
            caveHighlightMaterial.SetColor("_EmissionColor", caveHighlightColor * 0.3f);
            
            // Create ore highlight material
            oreHighlightMaterial = new Material(Shader.Find("Standard"));
            oreHighlightMaterial.color = oreHighlightColor;
            oreHighlightMaterial.SetFloat("_Metallic", 0.2f);
            oreHighlightMaterial.SetFloat("_Smoothness", 0.8f);
            oreHighlightMaterial.EnableKeyword("_EMISSION");
            oreHighlightMaterial.SetColor("_EmissionColor", oreHighlightColor * 0.5f);
        }
        
        void ApplyXRayEffect()
        {
            if (playerTransform == null) return;
            
            // Find all block renderers within range
            Collider[] colliders = Physics.OverlapSphere(playerTransform.position, xrayDistance);
            
            foreach (var collider in colliders)
            {
                var renderer = collider.GetComponent<Renderer>();
                if (renderer == null) continue;
                
                // Skip if already processed
                if (originalMaterials.ContainsKey(renderer)) continue;
                
                // Store original material
                originalMaterials[renderer] = renderer.material;
                
                // Determine block type and apply appropriate X-Ray material
                BlockType blockType = GetBlockTypeFromRenderer(renderer);
                Material xrayMat = GetXRayMaterial(blockType);
                
                if (xrayMat != null)
                {
                    renderer.material = xrayMat;
                    xrayMaterials[renderer] = xrayMat;
                }
            }
        }
        
        void UpdateXRayEffect()
        {
            if (playerTransform == null) return;
            
            Vector3 playerPos = playerTransform.position;
            List<Renderer> toRemove = new List<Renderer>();
            
            // Check existing X-Ray objects
            foreach (var kvp in originalMaterials)
            {
                var renderer = kvp.Key;
                if (renderer == null)
                {
                    toRemove.Add(renderer);
                    continue;
                }
                
                float distance = Vector3.Distance(playerPos, renderer.transform.position);
                if (distance > xrayDistance)
                {
                    // Restore original material if too far
                    renderer.material = kvp.Value;
                    toRemove.Add(renderer);
                }
            }
            
            // Clean up removed renderers
            foreach (var renderer in toRemove)
            {
                originalMaterials.Remove(renderer);
                xrayMaterials.Remove(renderer);
            }
            
            // Apply X-Ray to new objects in range
            ApplyXRayEffect();
        }
        
        void RemoveXRayEffect()
        {
            // Restore all original materials
            foreach (var kvp in originalMaterials)
            {
                if (kvp.Key != null)
                    kvp.Key.material = kvp.Value;
            }
            
            originalMaterials.Clear();
            xrayMaterials.Clear();
        }
        
        Material GetXRayMaterial(BlockType blockType)
        {
            switch (xrayMode)
            {
                case XRayMode.CaveHighlight:
                    // Highlight caves, make others transparent
                    if (blockType == BlockType.Air)
                        return caveHighlightMaterial;
                    else if (IsOreBlock(blockType))
                        return oreHighlightMaterial;
                    else
                        return GetTransparentMaterial();
                        
                case XRayMode.Transparent:
                    // Make all solid blocks transparent
                    if (blockType != BlockType.Air)
                        return GetTransparentMaterial();
                    break;
                    
                case XRayMode.CaveOnly:
                    // Show only caves and ores, hide everything else
                    if (blockType == BlockType.Air)
                        return caveHighlightMaterial;
                    else if (IsOreBlock(blockType))
                        return oreHighlightMaterial;
                    else
                        return null; // Hide other blocks
                        
                case XRayMode.FullUnderground:
                    // Show everything with highlights
                    if (blockType == BlockType.Air)
                        return caveHighlightMaterial;
                    else if (IsOreBlock(blockType))
                        return oreHighlightMaterial;
                    break;
            }
            
            return null;
        }
        
        Material GetTransparentMaterial()
        {
            transparentMaterial.color = new Color(1f, 1f, 1f, transparentAlpha);
            return transparentMaterial;
        }
        
        bool IsOreBlock(BlockType blockType)
        {
            return blockType == BlockType.Coal || 
                   blockType == BlockType.Iron || 
                   blockType == BlockType.Gold || 
                   blockType == BlockType.Diamond;
        }
        
        BlockType GetBlockTypeFromRenderer(Renderer renderer)
        {
            // Try to determine block type from renderer name or parent name
            string name = renderer.name.ToLower();
            if (name.Contains("air")) return BlockType.Air;
            if (name.Contains("coal")) return BlockType.Coal;
            if (name.Contains("iron")) return BlockType.Iron;
            if (name.Contains("gold")) return BlockType.Gold;
            if (name.Contains("diamond")) return BlockType.Diamond;
            if (name.Contains("stone")) return BlockType.Stone;
            if (name.Contains("dirt")) return BlockType.Dirt;
            if (name.Contains("grass")) return BlockType.Grass;
            
            // Default to stone if unknown
            return BlockType.Stone;
        }
        
        void OnDestroy()
        {
            RemoveXRayEffect();
        }
        
        void OnGUI()
        {
            if (enableXRay)
            {
                GUI.Box(new Rect(10, 10, 200, 60), $"X-Ray Mode: {xrayMode}\nDistance: {xrayDistance:F0}m\nPress F3 to toggle");
            }
        }
    }
}