using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Minimal, safe chunk optimizations that won't break existing functionality
    /// </summary>
    public class MinimalChunkOptimizer : MonoBehaviour
    {
        [Header("Safe Performance Settings")]
        [Tooltip("Frame time budget in milliseconds")]
        [Range(10f, 33f)] public float targetFrameTime = 16.67f; // 60 FPS
        
        [Tooltip("Maximum chunks to load per frame")]
        [Range(1, 12)] public int maxChunksPerFrame = 8; // Increased for better chunk loading
        
        [Tooltip("Enable frame time budgeting")]
        public bool enableFrameBudgeting = true;
        
        private WorldGenerator worldGenerator;
        
        private void Awake()
        {
            worldGenerator = GetComponent<WorldGenerator>();
        }
        
        private void Start()
        {
            if (worldGenerator != null)
            {
                ApplyMinimalOptimizations();
            }
        }
        
        private void ApplyMinimalOptimizations()
        {
            // Apply safe settings
            worldGenerator.targetFrameTime = targetFrameTime;
            worldGenerator.maxChunkLoadsPerFrame = maxChunksPerFrame;
            
            // Disable potentially problematic systems
            worldGenerator.useJobSystem = false;
            // LOD system removed
            
            Debug.Log("Applied minimal chunk optimizations - frame budgeting enabled, complex systems disabled");
        }
        
        private void OnValidate()
        {
            if (worldGenerator != null && Application.isPlaying)
            {
                ApplyMinimalOptimizations();
            }
        }
    }
}