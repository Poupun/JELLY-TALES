using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Reverts all chunk optimizations to ensure the game works properly
    /// Use this component to get back to a stable state
    /// </summary>
    public class ChunkOptimizationReverter : MonoBehaviour
    {
        [Header("Revert Settings")]
        [Tooltip("Automatically revert all optimizations on Start")]
        public bool revertOnStart = true;
        
        [Tooltip("Apply safe performance settings instead of completely reverting")]
        public bool applySafeSettings = true;
        
        private WorldGenerator worldGenerator;
        
        private void Awake()
        {
            worldGenerator = GetComponent<WorldGenerator>();
        }
        
        private void Start()
        {
            if (revertOnStart && worldGenerator != null)
            {
                RevertOptimizations();
            }
        }
        
        [ContextMenu("Revert All Optimizations")]
        public void RevertOptimizations()
        {
            if (worldGenerator == null)
            {
                Debug.LogError("ChunkOptimizationReverter: No WorldGenerator found!");
                return;
            }
            
            Debug.Log("ChunkOptimizationReverter: Reverting chunk optimizations to stable state...");
            
            // Disable potentially problematic optimizations
            worldGenerator.useJobSystem = false;
            // LOD system removed
            worldGenerator.delayPlayerSpawnUntilChunks = false;
            worldGenerator.snapPlayerToGroundOnSpawn = false;
            
            if (applySafeSettings)
            {
                // Apply only safe optimizations
                worldGenerator.maxChunkLoadsPerFrame = 2; // Slightly better than default (1)
                worldGenerator.targetFrameTime = 16.67f; // 60 FPS target
                
                Debug.Log("ChunkOptimizationReverter: Applied safe performance settings");
            }
            else
            {
                // Completely revert to original settings
                worldGenerator.maxChunkLoadsPerFrame = 1;
                worldGenerator.targetFrameTime = 16.67f;
                
                Debug.Log("ChunkOptimizationReverter: Completely reverted to original settings");
            }
            
            Debug.Log("ChunkOptimizationReverter: Optimization reversion complete. Player should now spawn properly.");
        }
        
        [ContextMenu("Apply Safe Optimizations Only")]
        public void ApplySafeOptimizations()
        {
            if (worldGenerator == null) return;
            
            Debug.Log("ChunkOptimizationReverter: Applying only safe optimizations...");
            
            // Disable problematic features
            worldGenerator.useJobSystem = false;
            // LOD system removed
            worldGenerator.delayPlayerSpawnUntilChunks = false;
            worldGenerator.snapPlayerToGroundOnSpawn = false;
            
            // Apply safe performance improvements
            worldGenerator.maxChunkLoadsPerFrame = 2;
            worldGenerator.targetFrameTime = 16.67f;
            
            Debug.Log("ChunkOptimizationReverter: Safe optimizations applied");
        }
    }
}