using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Performance monitoring system for chunk generation optimizations
    /// </summary>
    public class ChunkPerformanceMonitor : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private bool showOnScreenGUI = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F4;

        [Header("Performance Tracking")]
        [SerializeField] private int frameHistorySize = 60;
        [SerializeField] private float updateInterval = 0.5f;

        private WorldGenerator worldGenerator;
        private ChunkMeshPool meshPool;
        
        // Performance metrics
        private Queue<float> frameTimeHistory = new Queue<float>();
        private Queue<float> chunkLoadTimeHistory = new Queue<float>();
        
        private float lastUpdateTime;
        private float averageFrameTime;
        private float averageChunkLoadTime;
        private int totalChunksLoaded = 0;
        private int totalMeshesPooled = 0;
        
        // Current frame stats
        private float currentFrameTime;
        private float lastFrameStartTime;

        private void Awake()
        {
            worldGenerator = FindObjectOfType<WorldGenerator>();
            meshPool = ChunkMeshPool.Instance;
        }

        private void Start()
        {
            lastFrameStartTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            // Toggle display
            if (Input.GetKeyDown(toggleKey))
            {
                showOnScreenGUI = !showOnScreenGUI;
            }

            // Track frame time
            float currentTime = Time.realtimeSinceStartup;
            currentFrameTime = (currentTime - lastFrameStartTime) * 1000f; // Convert to milliseconds
            lastFrameStartTime = currentTime;

            // Add to history
            frameTimeHistory.Enqueue(currentFrameTime);
            if (frameTimeHistory.Count > frameHistorySize)
            {
                frameTimeHistory.Dequeue();
            }

            // Update averages periodically
            if (currentTime - lastUpdateTime >= updateInterval)
            {
                UpdateAverages();
                lastUpdateTime = currentTime;
            }
        }

        private void UpdateAverages()
        {
            // Calculate average frame time
            if (frameTimeHistory.Count > 0)
            {
                float total = 0f;
                foreach (float time in frameTimeHistory)
                {
                    total += time;
                }
                averageFrameTime = total / frameTimeHistory.Count;
            }
        }

        public void OnChunkLoaded(float loadTime)
        {
            totalChunksLoaded++;
            chunkLoadTimeHistory.Enqueue(loadTime);
            
            if (chunkLoadTimeHistory.Count > frameHistorySize)
            {
                chunkLoadTimeHistory.Dequeue();
            }

            // Update average chunk load time
            if (chunkLoadTimeHistory.Count > 0)
            {
                float total = 0f;
                foreach (float time in chunkLoadTimeHistory)
                {
                    total += time;
                }
                averageChunkLoadTime = total / chunkLoadTimeHistory.Count;
            }
        }

        private void OnGUI()
        {
            if (!showOnScreenGUI) return;

            // Create a styled GUI box
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            };

            var shadowStyle = new GUIStyle(style)
            {
                normal = { textColor = Color.black }
            };

            // Performance info box
            float boxWidth = 320f;
            float boxHeight = 200f;
            Rect boxRect = new Rect(10, 10, boxWidth, boxHeight);
            Rect shadowRect = new Rect(boxRect.x + 1, boxRect.y + 1, boxRect.width, boxRect.height);

            // Draw shadow
            GUI.Box(shadowRect, GetPerformanceInfo(), shadowStyle);
            // Draw main box
            GUI.Box(boxRect, GetPerformanceInfo(), style);

            // Optimization status box
            Rect statusRect = new Rect(10, boxHeight + 20, boxWidth, 120f);
            Rect statusShadowRect = new Rect(statusRect.x + 1, statusRect.y + 1, statusRect.width, statusRect.height);
            
            GUI.Box(statusShadowRect, GetOptimizationStatus(), shadowStyle);
            GUI.Box(statusRect, GetOptimizationStatus(), style);
        }

        private string GetPerformanceInfo()
        {
            string info = "=== CHUNK PERFORMANCE MONITOR ===\n";
            info += $"Frame Time: {currentFrameTime:F1}ms (Avg: {averageFrameTime:F1}ms)\n";
            info += $"FPS: {1000f / Mathf.Max(averageFrameTime, 0.001f):F1}\n";
            info += $"Target Frame Time: {worldGenerator?.targetFrameTime:F1}ms\n";
            info += "\n";
            info += $"Chunks Loaded: {totalChunksLoaded}\n";
            info += $"Avg Chunk Load Time: {averageChunkLoadTime:F1}ms\n";
            
            if (worldGenerator != null)
            {
                var chunks = GetChunksCount();
                var pendingLoads = GetPendingLoadsCount();
                info += $"Active Chunks: {chunks}\n";
                info += $"Pending Loads: {pendingLoads}\n";
            }

            info += $"\nPress {toggleKey} to toggle this display";
            
            return info;
        }

        private string GetOptimizationStatus()
        {
            string status = "=== OPTIMIZATION STATUS ===\n";
            
            if (worldGenerator != null)
            {
                status += $"Job System: {(worldGenerator.useJobSystem ? "ENABLED" : "DISABLED")}\n";
                status += $"LOD System: DISABLED (removed)\n";
                status += $"Chunk Meshing: {(worldGenerator.useChunkMeshing ? "ENABLED" : "DISABLED")}\n";
                status += $"Max Chunks/Frame: {worldGenerator.maxChunkLoadsPerFrame}\n";
            }

            if (meshPool != null)
            {
                status += $"\nMesh Pool Status:\n";
                status += $"Active: {meshPool.ActiveMeshCount}\n";
                status += $"Available: {meshPool.AvailableMeshCount}\n";
                status += $"Total Created: {meshPool.TotalCreated}\n";
            }

            return status;
        }

        private int GetChunksCount()
        {
            if (worldGenerator == null) return 0;
            
            var field = typeof(WorldGenerator).GetField("_chunks", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var chunks = field?.GetValue(worldGenerator) as System.Collections.IDictionary;
            return chunks?.Count ?? 0;
        }

        private int GetPendingLoadsCount()
        {
            if (worldGenerator == null) return 0;
            
            var field = typeof(WorldGenerator).GetField("_pendingLoads", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var pendingLoads = field?.GetValue(worldGenerator) as System.Collections.ICollection;
            return pendingLoads?.Count ?? 0;
        }

        // Static method for easy integration
        public static void LogChunkLoadTime(float loadTime)
        {
            var monitor = FindObjectOfType<ChunkPerformanceMonitor>();
            monitor?.OnChunkLoaded(loadTime);
        }

        // Performance analysis methods
        public bool IsPerformanceGood()
        {
            return averageFrameTime <= (worldGenerator?.targetFrameTime ?? 16.67f) * 1.2f; // 20% tolerance
        }

        public float GetFrameTimeStability()
        {
            if (frameTimeHistory.Count < 10) return 1f;

            float variance = 0f;
            foreach (float time in frameTimeHistory)
            {
                variance += Mathf.Pow(time - averageFrameTime, 2);
            }
            variance /= frameTimeHistory.Count;
            
            float stability = 1f - Mathf.Clamp01(Mathf.Sqrt(variance) / averageFrameTime);
            return stability;
        }

        public void LogPerformanceReport()
        {
            Debug.Log("=== CHUNK PERFORMANCE REPORT ===");
            Debug.Log($"Average Frame Time: {averageFrameTime:F2}ms");
            Debug.Log($"Average FPS: {1000f / Mathf.Max(averageFrameTime, 0.001f):F1}");
            Debug.Log($"Frame Time Stability: {GetFrameTimeStability():F2} (1.0 = perfect)");
            Debug.Log($"Total Chunks Loaded: {totalChunksLoaded}");
            Debug.Log($"Average Chunk Load Time: {averageChunkLoadTime:F2}ms");
            Debug.Log($"Performance Status: {(IsPerformanceGood() ? "GOOD" : "NEEDS IMPROVEMENT")}");
        }
    }
}