using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Removes all optimization components added in this session
    /// Run this in the Editor to clean up everything
    /// </summary>
    public class RemoveAllOptimizations : MonoBehaviour
    {
#if UNITY_EDITOR
        [MenuItem("Tools/Remove All Chunk Optimizations")]
        public static void RemoveOptimizations()
        {
            var worldGen = FindObjectOfType<WorldGenerator>();
            if (worldGen == null)
            {
                Debug.LogError("No WorldGenerator found in scene!");
                return;
            }

            int removed = 0;

            // Remove all optimization components
            var componentsToRemove = new System.Type[]
            {
                typeof(ChunkOptimizationIntegration),
                typeof(OptimizedChunkManager),
                typeof(UltraPerformanceMode),
                typeof(UltraFastChunkGenerator),
                typeof(ChunkLoadingInterceptor),
                typeof(DirectPriorityFix),
                typeof(DebugLogToggle)
            };

            foreach (var compType in componentsToRemove)
            {
                var comp = worldGen.GetComponent(compType);
                if (comp != null)
                {
                    DestroyImmediate(comp);
                    removed++;
                    Debug.Log($"Removed: {compType.Name}");
                }
            }

            // Re-enable debug logs
            Debug.unityLogger.logEnabled = true;
            Debug.Log($"Re-enabled debug logs");

            Debug.Log($"✅ Cleanup complete! Removed {removed} optimization components.");
            Debug.Log("Your WorldGenerator is back to original state.");
        }
#endif
    }
}
