using UnityEngine;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Simple component to optimize ONLY chunk generation FPS drops
    /// Add this to your WorldGenerator to automatically apply generation optimizations
    /// </summary>
    public class ChunkGenerationFpsOptimizer : MonoBehaviour
    {
        [Header("FPS Optimization Settings")]
        [Tooltip("Automatically add ultra-smooth chunk generator on Start")]
        public bool autoSetup = true;
        
        [Tooltip("Use ultra-smooth generator (virtually eliminates micro-freezes)")]
        public bool useUltraSmoothGenerator = true;
        
        [Tooltip("Use optimized block generation (eliminates expensive operations)")]
        public bool useOptimizedBlockGeneration = true;
        
        [Tooltip("Milliseconds per frame budget for chunk generation")]
        [Range(0.1f, 2f)] public float generationTimeBudget = 0.5f;
        
        [Tooltip("Blocks to process before yielding")]
        [Range(5, 50)] public int blocksPerYield = 20;
        
        [Tooltip("Reduce chunk loads per frame to improve FPS")]
        public bool limitChunkLoadsPerFrame = true;
        
        [Tooltip("Max chunks to load per frame")]
        [Range(1, 3)] public int maxChunksPerFrame = 1;
        
        
        private WorldGenerator worldGenerator;
        
        private void Awake()
        {
            worldGenerator = GetComponent<WorldGenerator>();
        }
        
        private void Start()
        {
            if (autoSetup)
            {
                SetupOptimizations();
            }
        }
        
        [ContextMenu("Setup Chunk Generation Optimizations")]
        public void SetupOptimizations()
        {
            if (worldGenerator == null)
            {
                Debug.LogError("ChunkGenerationFpsOptimizer: No WorldGenerator found!");
                return;
            }
            
            Debug.Log("ChunkGenerationFpsOptimizer: Setting up chunk generation optimizations...");
            
            // Add optimized block generator first (reduces expensive operations)
            if (useOptimizedBlockGeneration)
            {
                var blockOptimizer = GetComponent<OptimizedBlockGenerator>();
                if (blockOptimizer == null)
                {
                    blockOptimizer = gameObject.AddComponent<OptimizedBlockGenerator>();
                }
                
                // Configure optimization settings
                blockOptimizer.enableNoiseCache = true;
                blockOptimizer.useFastRandom = true;
                blockOptimizer.preCalculateHeights = true;
                
                Debug.Log("Added OptimizedBlockGenerator for faster block calculations");
            }
            
            if (useUltraSmoothGenerator)
            {
                // Add the ultra-smooth generator for maximum smoothness
                var ultraSmooth = GetComponent<UltraSmoothChunkGenerator>();
                if (ultraSmooth == null)
                {
                    ultraSmooth = gameObject.AddComponent<UltraSmoothChunkGenerator>();
                }
                
                // Configure ultra-smooth settings
                ultraSmooth.maxBlocksPerFrame = blocksPerYield;
                ultraSmooth.maxTimePerFrame = generationTimeBudget;
                ultraSmooth.forceYieldEvery = Mathf.Max(5, blocksPerYield / 2);
                
                Debug.Log("Added UltraSmoothChunkGenerator for maximum smoothness");
            }
            else
            {
                // Add the standard generation optimizer component if not present
                var optimizer = GetComponent<ChunkGenerationOptimizer>();
                if (optimizer == null)
                {
                    optimizer = gameObject.AddComponent<ChunkGenerationOptimizer>();
                }
                
                // Configure the optimizer
                optimizer.maxGenerationTimePerFrame = generationTimeBudget;
                optimizer.blocksPerYield = blocksPerYield;
                optimizer.optimizeNoise = true;
                optimizer.cacheNoiseValues = true;
            }
            
            // Apply safe WorldGenerator settings for better FPS
            if (limitChunkLoadsPerFrame)
            {
                worldGenerator.maxChunkLoadsPerFrame = maxChunksPerFrame;
            }
            
            // Ensure frame budgeting is enabled
            worldGenerator.targetFrameTime = 16.67f; // 60 FPS target
            
            
            Debug.Log("ChunkGenerationFpsOptimizer: Optimization setup complete!");
            if (useUltraSmoothGenerator)
            {
                Debug.Log($"- Ultra-smooth generation enabled");
                Debug.Log($"- Max blocks per frame: {blocksPerYield}");
                Debug.Log($"- Time budget: {generationTimeBudget}ms per frame");
                Debug.Log($"- Force yield every: {Mathf.Max(5, blocksPerYield / 2)} blocks");
            }
            else
            {
                Debug.Log($"- Standard optimized generation");
                Debug.Log($"- Generation time budget: {generationTimeBudget}ms per frame");
                Debug.Log($"- Blocks per yield: {blocksPerYield}");
            }
            Debug.Log($"- Max chunks per frame: {maxChunksPerFrame}");
        }
        
        [ContextMenu("Remove Optimizations")]
        public void RemoveOptimizations()
        {
            var optimizer = GetComponent<ChunkGenerationOptimizer>();
            if (optimizer != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(optimizer);
                }
                else
                {
                    DestroyImmediate(optimizer);
                }
                Debug.Log("ChunkGenerationFpsOptimizer: Removed chunk generation optimizer");
            }
            
            // Reset WorldGenerator to default settings
            if (worldGenerator != null)
            {
                worldGenerator.maxChunkLoadsPerFrame = 1;
                Debug.Log("ChunkGenerationFpsOptimizer: Reset WorldGenerator to default settings");
            }
        }
        
        private void OnValidate()
        {
            // Apply settings in real-time during development
            if (Application.isPlaying && autoSetup)
            {
                var optimizer = GetComponent<ChunkGenerationOptimizer>();
                if (optimizer != null)
                {
                    optimizer.maxGenerationTimePerFrame = generationTimeBudget;
                    optimizer.blocksPerYield = blocksPerYield;
                }
                
                if (worldGenerator != null && limitChunkLoadsPerFrame)
                {
                    worldGenerator.maxChunkLoadsPerFrame = maxChunksPerFrame;
                }
            }
        }
    }
}