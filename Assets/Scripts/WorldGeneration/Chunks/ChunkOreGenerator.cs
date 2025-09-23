using UnityEngine;
using System.Collections.Generic;

namespace WorldGeneration.Chunks
{
    /// <summary>
    /// Minecraft-style chunk-based ore generation system
    /// Generates ore blobs and veins instead of single scattered blocks
    /// </summary>
    public static class ChunkOreGenerator
    {
        [System.Serializable]
        public class OreSettings
        {
            [Header("Ore Generation Settings")]
            [Tooltip("Enable chunk-based ore generation")]
            public bool enableChunkOreGeneration = true;
            
            [Header("Coal Ore")]
            [Tooltip("Coal blobs per chunk attempt")]
            [Range(0f, 20f)] public float coalBlobsPerChunk = 12f;
            [Tooltip("Coal blob size (blocks)")]
            [Range(1f, 15f)] public float coalBlobSize = 8f;
            [Tooltip("Coal Y-level peak")]
            [Range(1f, 90f)] public float coalPeakY = 45f;
            [Tooltip("Coal Y-level range")]
            [Range(5f, 100f)] public float coalYRange = 25f;
            
            [Header("Iron Ore")]
            [Tooltip("Iron blobs per chunk attempt")]
            [Range(0f, 15f)] public float ironBlobsPerChunk = 8f;
            [Tooltip("Iron blob size (blocks)")]
            [Range(1f, 12f)] public float ironBlobSize = 6f;
            [Tooltip("Iron Y-level peak")]
            [Range(1f, 80f)] public float ironPeakY = 35f;
            [Tooltip("Iron Y-level range")]
            [Range(5f, 100f)] public float ironYRange = 20f;
            
            [Header("Gold Ore")]
            [Tooltip("Gold blobs per chunk attempt")]
            [Range(0f, 8f)] public float goldBlobsPerChunk = 3f;
            [Tooltip("Gold blob size (blocks)")]
            [Range(1f, 8f)] public float goldBlobSize = 4f;
            [Tooltip("Gold Y-level peak")]
            [Range(1f, 50f)] public float goldPeakY = 18f;
            [Tooltip("Gold Y-level range")]
            [Range(3f, 100f)] public float goldYRange = 12f;
            
            [Header("Diamond Ore")]
            [Tooltip("Diamond blobs per chunk attempt")]
            [Range(0f, 5f)] public float diamondBlobsPerChunk = 1.2f;
            [Tooltip("Diamond blob size (blocks)")]
            [Range(1f, 6f)] public float diamondBlobSize = 3f;
            [Tooltip("Diamond Y-level peak")]
            [Range(1f, 30f)] public float diamondPeakY = 12f;
            [Tooltip("Diamond Y-level range")]
            [Range(2f, 100f)] public float diamondYRange = 8f;
            
            [Header("Gravel Pockets")]
            [Tooltip("Gravel pockets per chunk")]
            [Range(0f, 10f)] public float gravelPocketsPerChunk = 4f;
            [Tooltip("Gravel pocket size (blocks)")]
            [Range(3f, 20f)] public float gravelPocketSize = 12f;
            
            [Header("Advanced Settings")]
            [Tooltip("Ore blob shape variation (0 = spherical, 1 = very irregular)")]
            [Range(0f, 1f)] public float blobIrregularity = 0.4f;
            [Tooltip("Enable large ore veins (rare, big formations)")]
            public bool enableLargeVeins = true;
            [Tooltip("Large vein frequency (lower = rarer)")]
            [Range(0f, 0.1f)] public float largeVeinFrequency = 0.02f;
            
            [Header("Debug")]
            [Tooltip("Enable debug logging for ore generation")]
            public bool enableDebugLogging = false;
        }
        
        /// <summary>
        /// Ore blob data structure
        /// </summary>
        private struct OreBlob
        {
            public Vector3 center;
            public BlockType oreType;
            public float size;
            public float irregularity;
            public int chunkSeed;
            
            public OreBlob(Vector3 center, BlockType oreType, float size, float irregularity, int chunkSeed)
            {
                this.center = center;
                this.oreType = oreType;
                this.size = size;
                this.irregularity = irregularity;
                this.chunkSeed = chunkSeed;
            }
        }
        
        /// <summary>
        /// Cached ore blobs for each chunk to avoid recalculation
        /// </summary>
        private static Dictionary<Vector2Int, List<OreBlob>> chunkOreBlobCache = new Dictionary<Vector2Int, List<OreBlob>>();
        
        /// <summary>
        /// Generate ore blobs for a chunk (called once per chunk)
        /// </summary>
        public static void GenerateChunkOreBlobs(Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ, int worldSeed, OreSettings settings)
        {
            if (!settings.enableChunkOreGeneration) return;
            
            // Check if already generated
            if (chunkOreBlobCache.ContainsKey(chunkCoord)) return;
            
            List<OreBlob> chunkBlobs = new List<OreBlob>();
            int chunkSeed = GetChunkSeed(chunkCoord.x, chunkCoord.y, worldSeed);
            System.Random chunkRandom = new System.Random(chunkSeed);
            
            // Generate different ore types
            GenerateCoalBlobs(chunkBlobs, chunkCoord, chunkSizeX, chunkSizeZ, chunkRandom, settings);
            GenerateIronBlobs(chunkBlobs, chunkCoord, chunkSizeX, chunkSizeZ, chunkRandom, settings);
            GenerateGoldBlobs(chunkBlobs, chunkCoord, chunkSizeX, chunkSizeZ, chunkRandom, settings);
            GenerateDiamondBlobs(chunkBlobs, chunkCoord, chunkSizeX, chunkSizeZ, chunkRandom, settings);
            GenerateGravelPockets(chunkBlobs, chunkCoord, chunkSizeX, chunkSizeZ, chunkRandom, settings);
            
            // Generate large ore veins (rare)
            if (settings.enableLargeVeins && chunkRandom.NextDouble() < settings.largeVeinFrequency)
            {
                GenerateLargeOreVein(chunkBlobs, chunkCoord, chunkSizeX, chunkSizeZ, chunkRandom, settings);
            }
            
            // Cache the generated blobs
            chunkOreBlobCache[chunkCoord] = chunkBlobs;
            
            // Debug logging
            if (settings.enableDebugLogging)
            {
                Debug.Log($"🟫 Generated {chunkBlobs.Count} ore blobs for chunk {chunkCoord}:");
                foreach (var blob in chunkBlobs)
                {
                    Debug.Log($"  - {blob.oreType}: size {blob.size:F1} at {blob.center}, irregularity {blob.irregularity:F2}");
                }
            }
        }
        
        /// <summary>
        /// Check if a world position should be an ore block based on chunk ore blobs
        /// </summary>
        public static BlockType GetOreAtPosition(Vector3Int worldPos, Vector2Int chunkCoord, int worldSeed, OreSettings settings)
        {
            if (!settings.enableChunkOreGeneration) return BlockType.Stone;
            
            // Ensure chunk blobs are generated
            if (!chunkOreBlobCache.ContainsKey(chunkCoord))
            {
                // This shouldn't happen if properly called, but safety check
                return BlockType.Stone;
            }
            
            List<OreBlob> chunkBlobs = chunkOreBlobCache[chunkCoord];
            
            // Check each blob to see if this position is inside it
            foreach (OreBlob blob in chunkBlobs)
            {
                if (IsPositionInOreBlob(worldPos, blob))
                {
                    return blob.oreType;
                }
            }
            
            return BlockType.Stone; // No ore at this position
        }
        
        /// <summary>
        /// Generate coal ore blobs in chunk
        /// </summary>
        private static void GenerateCoalBlobs(List<OreBlob> chunkBlobs, Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ, System.Random random, OreSettings settings)
        {
            // FIXED: Reduce variation to prevent too many blobs
            int numBlobs = Mathf.RoundToInt(settings.coalBlobsPerChunk + (float)(random.NextDouble() - 0.5) * 2f);
            numBlobs = Mathf.Clamp(numBlobs, 0, (int)(settings.coalBlobsPerChunk * 2f)); // Cap at 2x the setting
            
            for (int i = 0; i < numBlobs; i++)
            {
                // Random position in chunk
                float localX = (float)random.NextDouble() * chunkSizeX;
                float localZ = (float)random.NextDouble() * chunkSizeZ;
                float worldX = chunkCoord.x * chunkSizeX + localX;
                float worldZ = chunkCoord.y * chunkSizeZ + localZ;
                
                // Y distribution based on coal peak
                float yPos = GenerateYPositionWithDistribution(random, settings.coalPeakY, settings.coalYRange, 5f, 90f);
                
                Vector3 blobCenter = new Vector3(worldX, yPos, worldZ);
                
                // FIXED: Limit size variation and add maximum size
                float blobSize = settings.coalBlobSize + (float)(random.NextDouble() - 0.5) * 1.5f;
                blobSize = Mathf.Clamp(blobSize, 1f, settings.coalBlobSize * 2f); // Max 2x the setting
                
                chunkBlobs.Add(new OreBlob(blobCenter, BlockType.Coal, blobSize, settings.blobIrregularity, random.Next()));
            }
        }
        
        /// <summary>
        /// Generate iron ore blobs in chunk
        /// </summary>
        private static void GenerateIronBlobs(List<OreBlob> chunkBlobs, Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ, System.Random random, OreSettings settings)
        {
            // FIXED: Reduce variation
            int numBlobs = Mathf.RoundToInt(settings.ironBlobsPerChunk + (float)(random.NextDouble() - 0.5) * 1.5f);
            numBlobs = Mathf.Clamp(numBlobs, 0, (int)(settings.ironBlobsPerChunk * 2f));
            
            for (int i = 0; i < numBlobs; i++)
            {
                float localX = (float)random.NextDouble() * chunkSizeX;
                float localZ = (float)random.NextDouble() * chunkSizeZ;
                float worldX = chunkCoord.x * chunkSizeX + localX;
                float worldZ = chunkCoord.y * chunkSizeZ + localZ;
                
                float yPos = GenerateYPositionWithDistribution(random, settings.ironPeakY, settings.ironYRange, 5f, 80f);
                
                Vector3 blobCenter = new Vector3(worldX, yPos, worldZ);
                
                // FIXED: Limit size variation
                float blobSize = settings.ironBlobSize + (float)(random.NextDouble() - 0.5) * 1f;
                blobSize = Mathf.Clamp(blobSize, 1f, settings.ironBlobSize * 2f);
                
                chunkBlobs.Add(new OreBlob(blobCenter, BlockType.Iron, blobSize, settings.blobIrregularity, random.Next()));
            }
        }
        
        /// <summary>
        /// Generate gold ore blobs in chunk
        /// </summary>
        private static void GenerateGoldBlobs(List<OreBlob> chunkBlobs, Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ, System.Random random, OreSettings settings)
        {
            // FIXED: Reduce variation
            int numBlobs = Mathf.RoundToInt(settings.goldBlobsPerChunk + (float)(random.NextDouble() - 0.5) * 1f);
            numBlobs = Mathf.Clamp(numBlobs, 0, (int)(settings.goldBlobsPerChunk * 2f));
            
            for (int i = 0; i < numBlobs; i++)
            {
                float localX = (float)random.NextDouble() * chunkSizeX;
                float localZ = (float)random.NextDouble() * chunkSizeZ;
                float worldX = chunkCoord.x * chunkSizeX + localX;
                float worldZ = chunkCoord.y * chunkSizeZ + localZ;
                
                float yPos = GenerateYPositionWithDistribution(random, settings.goldPeakY, settings.goldYRange, 5f, 50f);
                
                Vector3 blobCenter = new Vector3(worldX, yPos, worldZ);
                
                // FIXED: Limit size variation
                float blobSize = settings.goldBlobSize + (float)(random.NextDouble() - 0.5) * 0.8f;
                blobSize = Mathf.Clamp(blobSize, 1f, settings.goldBlobSize * 2f);
                
                chunkBlobs.Add(new OreBlob(blobCenter, BlockType.Gold, blobSize, settings.blobIrregularity, random.Next()));
            }
        }
        
        /// <summary>
        /// Generate diamond ore blobs in chunk (rarest)
        /// </summary>
        private static void GenerateDiamondBlobs(List<OreBlob> chunkBlobs, Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ, System.Random random, OreSettings settings)
        {
            // Diamond has a chance-based generation (not every chunk gets diamonds)
            if (random.NextDouble() > settings.diamondBlobsPerChunk / 3f) return; // 3f makes it rarer
            
            // FIXED: Limit diamond blob count more strictly
            int numBlobs = 1; // Usually just 1 diamond blob per chunk when it generates
            if (random.NextDouble() < 0.05) numBlobs = 2; // REDUCED to 5% chance for 2 blobs
            
            for (int i = 0; i < numBlobs; i++)
            {
                float localX = (float)random.NextDouble() * chunkSizeX;
                float localZ = (float)random.NextDouble() * chunkSizeZ;
                float worldX = chunkCoord.x * chunkSizeX + localX;
                float worldZ = chunkCoord.y * chunkSizeZ + localZ;
                
                float yPos = GenerateYPositionWithDistribution(random, settings.diamondPeakY, settings.diamondYRange, 3f, 25f);
                
                Vector3 blobCenter = new Vector3(worldX, yPos, worldZ);
                
                // FIXED: Much smaller size variation for diamonds
                float blobSize = settings.diamondBlobSize + (float)(random.NextDouble() - 0.5) * 0.5f;
                blobSize = Mathf.Clamp(blobSize, 1f, settings.diamondBlobSize * 1.5f); // Smaller max multiplier
                
                chunkBlobs.Add(new OreBlob(blobCenter, BlockType.Diamond, blobSize, settings.blobIrregularity, random.Next()));
            }
        }
        
        /// <summary>
        /// Generate gravel pockets in chunk
        /// </summary>
        private static void GenerateGravelPockets(List<OreBlob> chunkBlobs, Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ, System.Random random, OreSettings settings)
        {
            // FIXED: Reduce variation - gravel was the worst offender with ±6 size variation!
            int numPockets = Mathf.RoundToInt(settings.gravelPocketsPerChunk + (float)(random.NextDouble() - 0.5) * 1f);
            numPockets = Mathf.Clamp(numPockets, 0, (int)(settings.gravelPocketsPerChunk * 2f));
            
            for (int i = 0; i < numPockets; i++)
            {
                float localX = (float)random.NextDouble() * chunkSizeX;
                float localZ = (float)random.NextDouble() * chunkSizeZ;
                float worldX = chunkCoord.x * chunkSizeX + localX;
                float worldZ = chunkCoord.y * chunkSizeZ + localZ;
                
                // Gravel more common in mid-depths
                float yPos = 20f + (float)random.NextDouble() * 50f; // Y 20-70
                
                Vector3 blobCenter = new Vector3(worldX, yPos, worldZ);
                
                // FIXED: Much smaller size variation - was ±6, now ±2
                float blobSize = settings.gravelPocketSize + (float)(random.NextDouble() - 0.5) * 2f;
                blobSize = Mathf.Clamp(blobSize, 2f, settings.gravelPocketSize * 1.8f); // Smaller max for gravel
                
                // FIXED: Reduce gravel irregularity multiplier
                float gravelIrregularity = Mathf.Clamp(settings.blobIrregularity * 1.1f, 0f, 0.6f);
                chunkBlobs.Add(new OreBlob(blobCenter, BlockType.Gravel, blobSize, gravelIrregularity, random.Next()));
            }
        }
        
        /// <summary>
        /// Generate large ore veins (rare, branching structures)
        /// </summary>
        private static void GenerateLargeOreVein(List<OreBlob> chunkBlobs, Vector2Int chunkCoord, int chunkSizeX, int chunkSizeZ, System.Random random, OreSettings settings)
        {
            // Choose ore type for large vein (bias toward common ores)
            BlockType veinOreType;
            double oreChoice = random.NextDouble();
            if (oreChoice < 0.5) veinOreType = BlockType.Coal;
            else if (oreChoice < 0.8) veinOreType = BlockType.Iron;
            else if (oreChoice < 0.95) veinOreType = BlockType.Gold;
            else veinOreType = BlockType.Diamond;
            
            // Create main vein body
            float centerX = chunkCoord.x * chunkSizeX + chunkSizeX * 0.5f;
            float centerZ = chunkCoord.y * chunkSizeZ + chunkSizeZ * 0.5f;
            float centerY = 30f + (float)random.NextDouble() * 40f; // Mid-range depth
            
            // FIXED: Much smaller main vein blob - was 15-25, now 8-12
            Vector3 mainCenter = new Vector3(centerX, centerY, centerZ);
            float mainSize = 8f + (float)random.NextDouble() * 4f; // Smaller large veins
            chunkBlobs.Add(new OreBlob(mainCenter, veinOreType, mainSize, 0.5f, random.Next())); // Reduced irregularity
            
            // FIXED: Fewer and smaller branches - was 2-4 branches of 6-14 size, now 1-2 branches of 3-6 size
            int numBranches = 1 + random.Next(2); // 1-2 branches instead of 2-4
            for (int i = 0; i < numBranches; i++)
            {
                float branchAngle = (float)random.NextDouble() * 2f * Mathf.PI;
                float branchDistance = 6f + (float)random.NextDouble() * 8f; // Shorter branches
                
                Vector3 branchCenter = new Vector3(
                    centerX + Mathf.Cos(branchAngle) * branchDistance,
                    centerY + ((float)random.NextDouble() - 0.5f) * 6f, // Less vertical spread
                    centerZ + Mathf.Sin(branchAngle) * branchDistance
                );
                
                // FIXED: Much smaller branch size - was 6-14, now 3-6
                float branchSize = 3f + (float)random.NextDouble() * 3f;
                chunkBlobs.Add(new OreBlob(branchCenter, veinOreType, branchSize, 0.5f, random.Next())); // Reduced irregularity
            }
        }
        
        /// <summary>
        /// Generate Y position with Gaussian-like distribution around peak
        /// </summary>
        private static float GenerateYPositionWithDistribution(System.Random random, float peakY, float range, float minY, float maxY)
        {
            // Use Box-Muller transform for Gaussian distribution
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log((float)u1)) * Mathf.Sin(2.0f * Mathf.PI * (float)u2);
            
            float y = peakY + (float)randStdNormal * range * 0.3f;
            return Mathf.Clamp(y, minY, maxY);
        }
        
        /// <summary>
        /// Check if a position is inside an ore blob (with irregular shape)
        /// </summary>
        private static bool IsPositionInOreBlob(Vector3Int worldPos, OreBlob blob)
        {
            Vector3 offset = worldPos - blob.center;
            float distance = offset.magnitude;
            
            // FIXED: Pre-check with maximum possible size to avoid huge blobs
            float maxPossibleSize = blob.size * (1f + blob.irregularity * 0.5f);
            if (distance > maxPossibleSize) return false;
            
            // Add irregularity using noise
            if (blob.irregularity > 0.01f)
            {
                float noiseValue = Mathf.PerlinNoise(
                    worldPos.x * 0.1f + blob.chunkSeed * 0.01f,
                    worldPos.y * 0.1f + blob.chunkSeed * 0.01f
                ) + Mathf.PerlinNoise(
                    worldPos.z * 0.1f + blob.chunkSeed * 0.01f,
                    worldPos.x * 0.05f + blob.chunkSeed * 0.01f
                ) * 0.5f;
                
                // FIXED: Much more conservative irregularity calculation
                float irregularityMultiplier = 1f + (noiseValue - 0.5f) * blob.irregularity * 0.6f;
                irregularityMultiplier = Mathf.Clamp(irregularityMultiplier, 0.5f, 1.5f); // Limit to 50%-150% of original size
                
                float adjustedSize = blob.size * irregularityMultiplier;
                
                return distance < adjustedSize;
            }
            
            return distance < blob.size; // Inside spherical blob
        }
        
        /// <summary>
        /// Generate consistent chunk seed
        /// </summary>
        private static int GetChunkSeed(int chunkX, int chunkZ, int worldSeed)
        {
            int hash = chunkX;
            hash = hash * 31 + chunkZ;
            hash = hash * 31 + worldSeed;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = (hash >> 16) ^ hash;
            return hash;
        }
        
        /// <summary>
        /// Clear cache for memory management
        /// </summary>
        public static void ClearCache()
        {
            chunkOreBlobCache.Clear();
        }
        
        /// <summary>
        /// Get cache statistics for debugging
        /// </summary>
        public static void LogCacheStats()
        {
            int totalBlobs = 0;
            foreach (var kvp in chunkOreBlobCache)
            {
                totalBlobs += kvp.Value.Count;
            }
            
            Debug.Log($"ChunkOreGenerator Cache: {chunkOreBlobCache.Count} chunks, {totalBlobs} total blobs");
        }
    }
}