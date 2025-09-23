using UnityEngine;
using System.Linq;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Debug helper to diagnose and fix chunk generation issues
    /// </summary>
    public class ChunkDebugHelper : MonoBehaviour
    {
        [Header("Debug Info")]
        [SerializeField] private bool showDebugGUI = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;
        
        private WorldGenerator worldGenerator;
        
        private void Awake()
        {
            worldGenerator = GetComponent<WorldGenerator>();
        }
        
        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                showDebugGUI = !showDebugGUI;
            }
        }
        
        private void OnGUI()
        {
            if (!showDebugGUI || worldGenerator == null) return;
            
            GUILayout.BeginArea(new Rect(10, Screen.height - 200, 400, 190));
            GUILayout.BeginVertical("Box");
            
            GUILayout.Label("=== CHUNK DEBUG INFO ===");
            
            // Basic world info
            GUILayout.Label($"World Size: {worldGenerator.worldWidth}x{worldGenerator.worldHeight}x{worldGenerator.worldDepth}");
            GUILayout.Label($"Chunk Streaming: {worldGenerator.useChunkStreaming}");
            GUILayout.Label($"Chunk Meshing: {worldGenerator.useChunkMeshing}");
            
            // Player info
            var player = worldGenerator.player;
            if (player != null)
            {
                GUILayout.Label($"Player Position: {player.position}");
                var chunkCoord = WorldToChunkCoord(player.position);
                GUILayout.Label($"Player Chunk: ({chunkCoord.x}, {chunkCoord.y})");
            }
            else
            {
                GUILayout.Label("Player: NOT FOUND!");
            }
            
            // Chunk info
            int chunkCount = GetChunkCount();
            GUILayout.Label($"Active Chunks: {chunkCount}");
            
            // Check for visible chunk objects
            var chunkObjects = FindObjectsOfType<MeshRenderer>()
                .Where(mr => mr.name.StartsWith("Chunk"))
                .ToArray();
            GUILayout.Label($"Chunk MeshRenderers: {chunkObjects.Length}");
            
            // Check for blocks
            var blockObjects = FindObjectsOfType<GameObject>()
                .Where(go => go.name.Contains("Block") || go.transform.parent?.name.StartsWith("Chunk") == true)
                .ToArray();
            GUILayout.Label($"Block GameObjects: {blockObjects.Length}");
            
            if (GUILayout.Button("Force Regenerate World"))
            {
                RegenerateWorld();
            }
            
            GUILayout.Label($"Press {toggleKey} to toggle this display");
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        private Vector2Int WorldToChunkCoord(Vector3 worldPos)
        {
            int chunkX = Mathf.FloorToInt(worldPos.x / worldGenerator.chunkSizeX);
            int chunkZ = Mathf.FloorToInt(worldPos.z / worldGenerator.chunkSizeZ);
            return new Vector2Int(chunkX, chunkZ);
        }
        
        private int GetChunkCount()
        {
            if (worldGenerator == null) return 0;
            
            var field = typeof(WorldGenerator).GetField("_chunks", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var chunks = field?.GetValue(worldGenerator) as System.Collections.IDictionary;
            return chunks?.Count ?? 0;
        }
        
        [ContextMenu("Regenerate World")]
        public void RegenerateWorld()
        {
            if (worldGenerator == null) return;
            
            Debug.Log("ChunkDebugHelper: Forcing world regeneration...");
            
            // Try to call the public regeneration method
            try
            {
                var method = typeof(WorldGenerator).GetMethod("ReloadWorld", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(worldGenerator, null);
                    Debug.Log("ChunkDebugHelper: Called ReloadWorld()");
                }
                else
                {
                    Debug.LogWarning("ChunkDebugHelper: ReloadWorld method not found");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ChunkDebugHelper: Failed to regenerate world: {e.Message}");
            }
        }
        
        public void LogWorldState()
        {
            Debug.Log("=== WORLD STATE DEBUG ===");
            Debug.Log($"WorldGenerator found: {worldGenerator != null}");
            
            if (worldGenerator != null)
            {
                Debug.Log($"Chunk Streaming: {worldGenerator.useChunkStreaming}");
                Debug.Log($"Chunk Meshing: {worldGenerator.useChunkMeshing}");
                Debug.Log($"Job System: {worldGenerator.useJobSystem}");
                Debug.Log($"LOD System: Disabled (removed)");
                Debug.Log($"Max Chunks Per Frame: {worldGenerator.maxChunkLoadsPerFrame}");
                
                var player = worldGenerator.player;
                Debug.Log($"Player found: {player != null}");
                if (player != null)
                {
                    Debug.Log($"Player active: {player.gameObject.activeInHierarchy}");
                    Debug.Log($"Player position: {player.position}");
                }
            }
            
            var chunkCount = GetChunkCount();
            Debug.Log($"Active chunks: {chunkCount}");
            
            Debug.Log("=== END WORLD STATE DEBUG ===");
        }
    }
}