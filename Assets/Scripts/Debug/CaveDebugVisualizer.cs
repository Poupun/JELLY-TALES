using UnityEngine;
using System.Collections.Generic;

namespace CaveDebug
{
    /// <summary>
    /// Real-time cave debug visualizer that shows cave generation data
    /// Works directly with the WorldGenerator to show cave patterns
    /// </summary>
    public class CaveDebugVisualizer : MonoBehaviour
    {
        [Header("Cave Debug Visualization")]
        [Tooltip("Enable cave debug visualization")]
        public bool enableVisualization = false;
        
        [Tooltip("Show cave generation in real-time")]
        public bool showCaveGeneration = true;
        
        [Tooltip("Show cave boundaries and limits")]
        public bool showCaveLimits = true;
        
        [Tooltip("Visualization radius around player")]
        [Range(5f, 50f)] public float visualizationRadius = 25f;
        
        [Tooltip("Cave visualization color")]
        public Color caveColor = Color.cyan;
        
        [Tooltip("Cave boundary color")]
        public Color boundaryColor = Color.yellow;
        
        private Transform playerTransform;
        private WorldGenerator worldGenerator;
        private List<Vector3> cavePositions = new List<Vector3>();
        private List<Vector3> boundaryPositions = new List<Vector3>();
        
        void Start()
        {
            // Find player and world generator
            if (GameObject.FindWithTag("Player") != null)
                playerTransform = GameObject.FindWithTag("Player").transform;
            else
                playerTransform = Camera.main?.transform;
                
            worldGenerator = FindObjectOfType<WorldGenerator>();
        }
        
        void Update()
        {
            // Toggle with F4
            if (Input.GetKeyDown(KeyCode.F4))
            {
                enableVisualization = !enableVisualization;
                UnityEngine.Debug.Log($"Cave Debug Visualization: {(enableVisualization ? "ON" : "OFF")}");
            }
            
            if (enableVisualization && playerTransform != null && worldGenerator != null)
            {
                UpdateVisualization();
            }
        }
        
        void UpdateVisualization()
        {
            cavePositions.Clear();
            boundaryPositions.Clear();
            
            Vector3 playerPos = playerTransform.position;
            int radius = Mathf.RoundToInt(visualizationRadius);
            
            // Sample cave generation in a grid around the player
            for (int x = -radius; x <= radius; x += 2)
            {
                for (int y = -radius; y <= radius; y += 2)
                {
                    for (int z = -radius; z <= radius; z += 2)
                    {
                        Vector3Int worldPos = new Vector3Int(
                            Mathf.RoundToInt(playerPos.x) + x,
                            Mathf.RoundToInt(playerPos.y) + y,
                            Mathf.RoundToInt(playerPos.z) + z
                        );
                        
                        // Check if position is within visualization radius
                        if (Vector3.Distance(playerPos, worldPos) > visualizationRadius)
                            continue;
                        
                        // Check cave generation
                        if (showCaveGeneration && ShouldShowCaveAt(worldPos))
                        {
                            cavePositions.Add(worldPos);
                        }
                        
                        // Check cave boundaries
                        if (showCaveLimits && IsAtCaveBoundary(worldPos))
                        {
                            boundaryPositions.Add(worldPos);
                        }
                    }
                }
            }
        }
        
        bool ShouldShowCaveAt(Vector3Int worldPos)
        {
            if (worldGenerator == null) return false;
            
            // Use the world generator's cave detection
            try
            {
                return worldGenerator.enableCaves && 
                       worldPos.y >= worldGenerator.minCaveHeight && 
                       worldPos.y <= worldGenerator.maxCaveHeight &&
                       worldGenerator.GetType()
                           .GetMethod("ShouldGenerateCaveAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                           ?.Invoke(worldGenerator, new object[] { worldPos }) is bool result && result;
            }
            catch
            {
                return false;
            }
        }
        
        bool IsAtCaveBoundary(Vector3Int worldPos)
        {
            if (worldGenerator == null) return false;
            
            // Check if at cave height boundaries
            return worldPos.y == worldGenerator.minCaveHeight || 
                   worldPos.y == worldGenerator.maxCaveHeight ||
                   worldPos.y == 66; // The problematic boundary
        }
        
        void OnDrawGizmos()
        {
            if (!enableVisualization) return;
            
            // Draw cave positions
            Gizmos.color = caveColor;
            foreach (var pos in cavePositions)
            {
                Gizmos.DrawWireCube(pos, Vector3.one * 0.8f);
            }
            
            // Draw boundary positions
            Gizmos.color = boundaryColor;
            foreach (var pos in boundaryPositions)
            {
                Gizmos.DrawWireCube(pos, Vector3.one * 1.2f);
            }
            
            // Draw visualization radius
            if (playerTransform != null)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(playerTransform.position, visualizationRadius);
            }
        }
        
        void OnGUI()
        {
            if (enableVisualization)
            {
                string info = $"Cave Debug Visualizer\n" +
                             $"F4: Toggle\n" +
                             $"Caves Found: {cavePositions.Count}\n" +
                             $"Boundaries: {boundaryPositions.Count}\n" +
                             $"Radius: {visualizationRadius:F0}m";
                             
                GUI.Box(new Rect(10, 100, 200, 100), info);
                
                // Show cave generation settings
                if (worldGenerator != null)
                {
                    string settings = $"Cave Settings:\n" +
                                    $"Enabled: {worldGenerator.enableCaves}\n" +
                                    $"Min Height: {worldGenerator.minCaveHeight}\n" +
                                    $"Max Height: {worldGenerator.maxCaveHeight}\n" +
                                    $"Density: {worldGenerator.caveDensity:F1}\n" +
                                    $"Mode: {worldGenerator.caveMode}";
                                    
                    GUI.Box(new Rect(220, 100, 200, 120), settings);
                }
            }
        }
    }
}