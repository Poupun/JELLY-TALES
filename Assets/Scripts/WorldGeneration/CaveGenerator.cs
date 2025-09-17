using UnityEngine;

namespace WorldGeneration
{
    /// <summary>
    /// Lightweight cave generation system using 3D noise for tunnel-like caves
    /// Optimized for performance with minimal computation per block
    /// 
    /// Cave Generation Modes:
    /// - Simple: Single 3D noise, fastest performance (~1 Perlin lookup per block)
    /// - Tunnels: Dual noise system, balanced performance (~2 Perlin lookups per block) 
    /// - Advanced: Ridged noise with chambers, most realistic (~4 Perlin lookups per block)
    /// 
    /// All methods respect height limits and density multipliers for consistent behavior
    /// </summary>
    public static class CaveGenerator
    {
        // Performance optimized constants - tuned for balanced cave generation
        public const float CAVE_FREQUENCY = 0.02f;          // Primary cave chamber frequency
        public const float CAVE_THRESHOLD = 0.4f;           // Threshold for large caves
        public const float TUNNEL_FREQUENCY = 0.015f;       // Secondary tunnel frequency  
        public const float TUNNEL_THRESHOLD = 0.35f;        // Threshold for connecting tunnels
        public const int MIN_CAVE_HEIGHT = 1;               // Minimum Y level for caves
        public const int MAX_CAVE_HEIGHT = 100;             // Maximum Y level for caves
        
        // Advanced cave generation constants
        public const float RIDGE_FREQUENCY = 0.025f;        // Ridged noise frequency for tunnels
        public const float CHAMBER_FREQUENCY = 0.015f;      // Chamber noise frequency
        public const float SIMPLE_FREQUENCY = 0.018f;       // Simple cave noise frequency
        
        /// <summary>
        /// Modded-style cave generation with multiple cave types and sizes
        /// Creates a varied underground experience with small tunnels, medium caves, and rare large caverns
        /// </summary>
        /// <param name="worldPos">World position of the block</param>
        /// <param name="worldSeed">World seed for deterministic generation</param>
        /// <param name="densityMultiplier">Cave density multiplier (1.0 = default, higher = more caves)</param>
        /// <returns>True if block should be air (cave), false if solid</returns>
        public static bool ShouldGenerateCave(Vector3Int worldPos, int worldSeed, float densityMultiplier = 1f)
        {
            // Only generate caves in appropriate underground layers
            if (worldPos.y < MIN_CAVE_HEIGHT || worldPos.y > MAX_CAVE_HEIGHT)
                return false;
                
            // Avoid generating caves too close to bedrock
            if (worldPos.y <= 2)
                return false;
            
            float seedOffset = worldSeed % 10000 * 0.001f;
            float x = worldPos.x;
            float y = worldPos.y;
            float z = worldPos.z;
            
            // Organic tunnel system with natural curves
            float caveOffset1 = seedOffset;
            float caveOffset2 = seedOffset * 1.61f + 31.4f;
            
            // Organic caves with proper Y-variation to break vertical alignment
            float caveX = x * CAVE_FREQUENCY + Mathf.Sin(z * 0.03f + y * 0.025f + caveOffset1) * 0.2f;
            float caveZ = z * CAVE_FREQUENCY + Mathf.Cos(x * 0.025f + y * 0.022f + caveOffset1) * 0.15f;
            float caveNoise = Mathf.PerlinNoise(caveX + y * 0.018f, caveZ + y * 0.015f);
            
            // Organic secondary tunnels with Y-variation
            float tunnelX = z * TUNNEL_FREQUENCY + Mathf.Sin(y * 0.028f + x * 0.02f + caveOffset2) * 0.15f;
            float tunnelZ = x * TUNNEL_FREQUENCY + Mathf.Cos(z * 0.035f + y * 0.025f + caveOffset2) * 0.1f;
            float tunnelNoise = Mathf.PerlinNoise(tunnelX + y * 0.02f, tunnelZ + y * 0.017f);
            
            // ===== MODDED-STYLE CAVE GENERATION SYSTEM =====
            // Tiered cave system: Small Tunnels (Common) -> Medium Caves (Uncommon) -> Large Caverns (Rare)
            
            // Proper organic height variation to prevent vertical alignment
            float heightVar = Mathf.Sin(y * 0.08f + caveOffset1) * 0.06f + 
                            Mathf.Cos(y * 0.055f + x * 0.02f + caveOffset2) * 0.04f;
            
            // Organic tunnel layers with proper Y-variation
            float microTunnelX = x * 0.08f + Mathf.Sin(z * 0.12f + y * 0.035f) * 0.08f + caveOffset1;
            float microTunnelZ = z * 0.085f + Mathf.Cos(x * 0.11f + y * 0.03f) * 0.06f + caveOffset2;
            float microTunnelNoise = Mathf.PerlinNoise(microTunnelX, microTunnelZ + y * 0.025f);
            
            float branchX = x * 0.045f + Mathf.Sin(y * 0.04f + caveOffset2) * 0.12f;
            float branchZ = z * 0.048f + Mathf.Cos(y * 0.038f + caveOffset1) * 0.1f;
            float branchNoise = Mathf.PerlinNoise(branchX + y * 0.02f, branchZ + y * 0.018f);
            
            // === TIER 1: SMALL TUNNELS (Common - 70% of caves) ===
            float smallTunnelThreshold = 0.62f + heightVar * 0.5f;
            bool hasSmallTunnels = caveNoise > smallTunnelThreshold * (1.8f - densityMultiplier) ||
                                   tunnelNoise > smallTunnelThreshold * (1.7f - densityMultiplier) ||
                                   microTunnelNoise > (smallTunnelThreshold + 0.05f) * (1.6f - densityMultiplier);
            
            // === TIER 2: MEDIUM CAVES (Uncommon - 25% of caves) ===
            float mediumCaveThreshold = 0.72f + heightVar * 0.6f;
            bool hasMediumCaves = (caveNoise * tunnelNoise) > mediumCaveThreshold * (1.5f - densityMultiplier) ||
                                 ((caveNoise + tunnelNoise) > 0.68f * (1.6f - densityMultiplier) && microTunnelNoise > 0.55f) ||
                                 (branchNoise > 0.7f && caveNoise > 0.65f);
            
            // === TIER 3: LARGE CAVERNS (Rare - 5% of caves) ===
            float largeCavernThreshold = 0.78f + heightVar * 0.7f;
            bool hasLargeCaverns = (caveNoise * tunnelNoise * microTunnelNoise) > largeCavernThreshold * (1.3f - densityMultiplier) ||
                                  ((caveNoise + tunnelNoise + microTunnelNoise) > 0.75f * (1.4f - densityMultiplier) && branchNoise > 0.75f);
            
            // === CONNECTING NETWORKS (For seamless exploration) ===
            bool hasConnections = (caveNoise > 0.65f && tunnelNoise > 0.6f) ||
                                 (microTunnelNoise > 0.7f && branchNoise > 0.65f) ||
                                 (caveNoise * tunnelNoise) > 0.5f * (1.6f - densityMultiplier) ||
                                 ((caveNoise + tunnelNoise) > 0.58f * (1.7f - densityMultiplier));
            
            // === DEPTH-BASED BONUSES (More caves deeper) ===
            float depthMultiplier = 1f;
            if (y < 20) depthMultiplier = 1.3f;      // Deep caves much more common
            else if (y < 40) depthMultiplier = 1.15f; // Mid-level more common
            else if (y < 60) depthMultiplier = 1.0f;  // Normal frequency
            else depthMultiplier = 0.85f;             // Surface caves less common
            
            // Apply depth multiplier to all cave types
            hasSmallTunnels = hasSmallTunnels || (caveNoise * depthMultiplier) > (0.58f * (1.9f - densityMultiplier));
            hasMediumCaves = hasMediumCaves || ((caveNoise + tunnelNoise) * depthMultiplier > (0.65f * (1.7f - densityMultiplier)) && microTunnelNoise > 0.5f);
            hasLargeCaverns = hasLargeCaverns || ((caveNoise + tunnelNoise + microTunnelNoise) * depthMultiplier > (0.72f * (1.5f - densityMultiplier)) && branchNoise > 0.65f);
            
            return hasSmallTunnels || hasMediumCaves || hasLargeCaverns || hasConnections;
        }
        
        /// <summary>
        /// Organic cave generation with irregular, winding tunnel systems
        /// Creates natural cave networks that avoid symmetry and flat layers
        /// </summary>
        public static bool ShouldGenerateAdvancedCave(Vector3Int worldPos, int worldSeed, float densityMultiplier = 1f)
        {
            if (worldPos.y < MIN_CAVE_HEIGHT || worldPos.y > MAX_CAVE_HEIGHT)
                return false;
                
            if (worldPos.y <= 2)
                return false;
            
            float seedOffset = (worldSeed % 10000) * 0.001f;
            
            // Multiple offset layers to break symmetry
            float offset1 = seedOffset;
            float offset2 = seedOffset * 1.73f + 47.3f;
            float offset3 = seedOffset * 2.41f + 123.7f;
            
            // Organic winding tunnel system - avoid straight vertical drops
            float x = worldPos.x;
            float y = worldPos.y;
            float z = worldPos.z;
            
            // Organic winding tunnels with proper Y-variation
            float tunnel1X = x * 0.027f + y * 0.015f + Mathf.Sin(y * 0.055f + offset1) * 0.2f + offset1;
            float tunnel1Y = z * 0.025f + y * 0.018f + Mathf.Cos(x * 0.04f + offset1) * 0.15f + offset1 * 0.7f;
            float tunnel1 = Mathf.PerlinNoise(tunnel1X, tunnel1Y);
            
            float tunnel2X = z * 0.023f + x * 0.011f + Mathf.Sin(z * 0.055f + offset2) * 0.2f + offset2;
            float tunnel2Y = x * 0.029f + y * 0.016f + Mathf.Cos(y * 0.048f + offset2) * 0.15f + offset2 * 1.3f;
            float tunnel2 = Mathf.PerlinNoise(tunnel2X, tunnel2Y);
            
            // Organic worm-like tunnels with natural curves
            float wormX = x * 0.015f + Mathf.Sin(y * 0.042f + offset3) * 0.3f + Mathf.Cos(z * 0.03f + offset3) * 0.2f;
            float wormZ = z * 0.016f + Mathf.Cos(x * 0.048f + offset3) * 0.3f + Mathf.Sin(y * 0.038f + offset3) * 0.2f;
            float wormNoise = Mathf.PerlinNoise(wormX, wormZ + y * 0.012f);
            
            // Complex chamber system with multiple scales for varied cave sizes
            float chamberScale1 = 0.018f; // Large chambers
            float chamberScale2 = 0.035f; // Medium chambers  
            float chamberScale3 = 0.055f; // Small pockets
            
            // Organic chamber network with natural variation
            float chamber1X = x * chamberScale1 + Mathf.Sin(z * 0.03f + y * 0.022f) * 0.25f + offset1;
            float chamber1Z = z * chamberScale1 + Mathf.Cos(x * 0.035f + y * 0.025f) * 0.2f + offset2;
            float chamber1Y = y * 0.012f + Mathf.Sin(x * 0.04f + z * 0.03f) * 0.08f + offset3;
            float chamber1Noise = Mathf.PerlinNoise(chamber1X + chamber1Y, chamber1Z + chamber1Y);
            
            // Medium organic chambers
            float chamber2X = x * chamberScale2 + Mathf.Sin(z * 0.045f + y * 0.02f) * 0.18f + offset2;
            float chamber2Z = z * chamberScale2 + Mathf.Cos(x * 0.04f + y * 0.024f) * 0.15f + offset3;
            float chamber2Noise = Mathf.PerlinNoise(chamber2X, chamber2Z + y * 0.014f);
            
            // Small organic pockets
            float chamber3X = x * chamberScale3 + Mathf.Sin(z * 0.06f + y * 0.018f) * 0.12f + offset1;
            float chamber3Z = z * chamberScale3 + Mathf.Cos(x * 0.055f + y * 0.021f) * 0.1f + offset2;
            float chamber3Noise = Mathf.PerlinNoise(chamber3X, chamber3Z + y * 0.016f);
            
            // Organic detail noise for complex cave boundaries
            float detail1X = x * 0.045f + y * 0.028f + Mathf.Sin(y * 0.055f + offset2) * 0.15f + offset2;
            float detail1Z = z * 0.043f + x * 0.015f + Mathf.Cos(y * 0.048f + offset3) * 0.12f + offset3;
            float detail1Noise = Mathf.PerlinNoise(detail1X, detail1Z);
            
            float detail2X = x * 0.075f + Mathf.Sin(z * 0.08f + y * 0.042f) * 0.1f + offset1;
            float detail2Z = z * 0.08f + Mathf.Cos(x * 0.07f + y * 0.038f) * 0.08f + offset3;
            float detail2Noise = Mathf.PerlinNoise(detail2X, detail2Z + y * 0.025f);
            
            // Combine detail layers
            float combinedDetail = (detail1Noise * 0.7f + detail2Noise * 0.3f);
            
            // Proper organic height-based variation to prevent vertical alignment
            float heightVariation = Mathf.Sin(y * 0.085f + offset1) * 0.07f + 
                                  Mathf.Cos(y * 0.065f + offset2) * 0.05f;
            
            // Combine tunnels with organic intersection logic (avoid vertical amplification)
            float tunnelCombined = Mathf.Max(tunnel1 * tunnel2 * 0.8f, wormNoise);
            
            // ===== ADVANCED MODDED-STYLE CAVE GENERATION =====
            // Multi-tier system with complex interconnected cave networks
            
            // === TIER 1: INTRICATE TUNNELS (Common - 60% of caves) ===
            float intricateTunnelThreshold = 0.60f - heightVariation * 0.5f;
            bool hasIntricateTunnels = tunnelCombined > intricateTunnelThreshold * (1.9f - densityMultiplier) && combinedDetail > 0.4f ||
                                      wormNoise > (intricateTunnelThreshold + 0.02f) * (1.8f - densityMultiplier) && combinedDetail > 0.35f ||
                                      (tunnel1 > 0.65f || tunnel2 > 0.65f) && combinedDetail > 0.5f;
            
            // === TIER 2: COMPLEX CHAMBERS (Uncommon - 30% of caves) ===
            float complexChamberThreshold = 0.70f - heightVariation * 0.6f;
            bool hasComplexChambers = (tunnel1 * tunnel2) > complexChamberThreshold * (1.6f - densityMultiplier) && combinedDetail > 0.6f ||
                                     (tunnelCombined * wormNoise) > (complexChamberThreshold - 0.02f) * (1.7f - densityMultiplier) && combinedDetail > 0.55f ||
                                     (chamber2Noise > 0.7f && tunnel1 > 0.6f && combinedDetail > 0.65f);
            
            // === TIER 3: GRAND CAVERNS (Rare - 10% of caves) ===
            float grandCavernThreshold = 0.78f - heightVariation * 0.7f;
            bool hasGrandCaverns = (tunnel1 * tunnel2 * wormNoise) > grandCavernThreshold * (1.4f - densityMultiplier) && combinedDetail > 0.7f ||
                                  (chamber1Noise > 0.75f && tunnelCombined > 0.7f && combinedDetail > 0.75f);
            
            // === SPECIAL CHAMBER TYPES ===
            // Rare location bonus for special chambers
            float rarityFactor = Mathf.PerlinNoise(x * 0.003f + seedOffset, z * 0.003f + seedOffset * 2.1f);
            bool isSpecialLocation = rarityFactor > 0.88f; // 12% of locations can have special chambers
            
            bool hasLargeChambers = isSpecialLocation && chamber1Noise > 0.78f && combinedDetail > 0.7f && (tunnel1 > 0.65f || tunnel2 > 0.65f);
            bool hasMediumChambers = chamber2Noise > 0.72f && combinedDetail > 0.6f && (tunnel2 > 0.6f || wormNoise > 0.6f);
            bool hasSmallPockets = chamber3Noise > 0.68f && combinedDetail > 0.5f;
            
            // === ADVANCED CONNECTIVITY SYSTEM ===
            // Multiple connection types for seamless cave networks
            bool hasDirectConnections = (tunnel1 * tunnel2) > 0.55f * (1.4f - densityMultiplier) && combinedDetail > 0.6f;
            bool hasWormConnections = (tunnel2 * wormNoise) > 0.52f * (1.5f - densityMultiplier) && combinedDetail > 0.55f;
            bool hasWormTunnel1Connections = (tunnel1 * wormNoise) > 0.58f * (1.35f - densityMultiplier) && combinedDetail > 0.58f;
            
            // Chamber-tunnel connections for natural flow
            bool hasChamberConnections = (chamber1Noise * tunnel1) > 0.6f * (1.3f - densityMultiplier) && combinedDetail > 0.65f ||
                                        (chamber2Noise * tunnel2) > 0.58f * (1.35f - densityMultiplier) && combinedDetail > 0.6f;
            
            // High-detail connection areas
            bool hasDetailConnections = combinedDetail > 0.8f && (tunnel1 > 0.65f || tunnel2 > 0.65f || wormNoise > 0.65f);
            
            // Complex multi-tunnel intersections
            bool hasCrossConnections = (tunnel1 * wormNoise * tunnel2) > 0.45f * (1.6f - densityMultiplier) && combinedDetail > 0.65f;
            
            // Seamless connecting passages
            bool hasConnectingPassages = hasDirectConnections || hasWormConnections || hasWormTunnel1Connections || 
                                       hasChamberConnections || hasDetailConnections || hasCrossConnections;
            
            // Rare organic variations (small features only)
            float organicFactor1 = Mathf.PerlinNoise(x * 0.051f + offset3, z * 0.047f + y * 0.03f + offset1);
            bool hasOrganicVariation = organicFactor1 > 0.85f && tunnelCombined > 0.7f && combinedDetail > 0.65f;
            
            // === ADVANCED CAVE NETWORK ASSEMBLY ===
            // Depth-based cave distribution for realistic underground exploration
            float depthBonus = 0f;
            if (y < 25) depthBonus = 0.15f;       // Deep caves very common
            else if (y < 50) depthBonus = 0.08f;  // Mid-depth common
            else if (y < 75) depthBonus = 0.02f;  // Upper caves less common
            
            // Apply depth bonuses to all cave types
            bool hasDepthBonusTunnels = (tunnelCombined + depthBonus) > 0.55f * (1.8f - densityMultiplier);
            bool hasDepthBonusChambers = (chamber2Noise + depthBonus) > 0.65f * (1.6f - densityMultiplier) && combinedDetail > 0.5f;
            bool hasDepthBonusCaverns = (chamber1Noise + depthBonus) > 0.72f * (1.4f - densityMultiplier) && combinedDetail > 0.65f;
            
            // Update organic variation for natural cave boundaries
            hasOrganicVariation = organicFactor1 > 0.75f && (tunnelCombined > 0.6f || wormNoise > 0.65f) && combinedDetail > 0.6f;
            
            // Final cave network assembly
            bool isCave = hasIntricateTunnels || hasComplexChambers || hasGrandCaverns || hasConnectingPassages ||
                         hasDepthBonusTunnels || hasDepthBonusChambers || hasDepthBonusCaverns ||
                         (hasSmallPockets && combinedDetail > 0.6f) ||
                         (hasMediumChambers && combinedDetail > 0.7f) ||
                         (hasLargeChambers && combinedDetail > 0.8f) ||
                         (hasOrganicVariation && combinedDetail > 0.65f);
            
            return isCave;
        }
        
        /// <summary>
        /// Organic simple cave generation optimized for performance
        /// Creates natural irregular caves without symmetrical patterns
        /// </summary>
        public static bool ShouldGenerateSimpleCave(Vector3Int worldPos, int worldSeed, float densityMultiplier = 1f)
        {
            if (worldPos.y < MIN_CAVE_HEIGHT || worldPos.y > MAX_CAVE_HEIGHT)
                return false;
                
            if (worldPos.y <= 2)
                return false;
            
            float seedOffset = worldSeed % 10000 * 0.001f;
            float x = worldPos.x;
            float y = worldPos.y;
            float z = worldPos.z;
            
            // Organic curved noise with proper Y-variation
            float curve1 = x * 0.019f + Mathf.Sin(z * 0.04f + y * 0.022f + seedOffset) * 0.15f;
            float curve2 = z * 0.021f + Mathf.Cos(x * 0.038f + y * 0.018f + seedOffset) * 0.1f;
            float noise = Mathf.PerlinNoise(curve1 + y * 0.016f, curve2 + y * 0.014f);
            
            // Proper organic height variation to prevent vertical alignment
            float heightVar = Mathf.Sin(y * 0.075f + seedOffset) * 0.06f + 
                            Mathf.Cos(y * 0.055f + x * 0.025f + seedOffset) * 0.04f;
            
            // Enhanced simple cave with secondary features
            
            // Organic secondary cave layer
            float cave2X = x * 0.032f + Mathf.Sin(z * 0.055f + y * 0.02f) * 0.1f + seedOffset * 1.4f;
            float cave2Z = z * 0.035f + Mathf.Cos(x * 0.05f + y * 0.018f) * 0.08f + seedOffset * 0.7f;
            float noise2 = Mathf.PerlinNoise(cave2X + y * 0.015f, cave2Z + y * 0.012f);
            
            // Organic pocket caves
            float pocketX = x * 0.065f + Mathf.Sin(y * 0.035f + seedOffset) * 0.06f;
            float pocketZ = z * 0.07f + Mathf.Cos(y * 0.032f + seedOffset) * 0.05f;
            float pocketNoise = Mathf.PerlinNoise(pocketX, pocketZ + y * 0.018f);
            
            // ===== SIMPLE MODDED-STYLE CAVE GENERATION =====
            // Accessible cave system with clear size tiers
            
            // === TIER 1: BASIC TUNNELS (Common - 75% of caves) ===
            float basicTunnelThreshold = 0.58f + heightVar * 0.5f;
            bool hasBasicTunnels = noise > (basicTunnelThreshold * (1.9f - densityMultiplier)) ||
                                  noise2 > ((basicTunnelThreshold + 0.03f) * (1.8f - densityMultiplier)) ||
                                  (noise + noise2) > (0.55f * (2.0f - densityMultiplier));
            
            // === TIER 2: MEDIUM CAVES (Uncommon - 20% of caves) ===
            float mediumCaveThreshold = 0.68f + heightVar * 0.6f;
            bool hasMediumSimpleCaves = (noise * noise2) > (mediumCaveThreshold * (1.6f - densityMultiplier)) ||
                                       ((noise + noise2) > (0.64f * (1.7f - densityMultiplier)) && pocketNoise > 0.5f);
            
            // === TIER 3: LARGE POCKETS (Rare - 5% of caves) ===
            float largePocketThreshold = 0.76f + heightVar * 0.7f;
            bool hasLargePockets = (noise * noise2 * pocketNoise) > (largePocketThreshold * (1.4f - densityMultiplier)) ||
                                  (pocketNoise > 0.75f && (noise > 0.7f || noise2 > 0.7f));
            
            // === SIMPLE CONNECTIVITY SYSTEM ===
            bool hasSimpleConnections = (noise > 0.6f && noise2 > 0.58f) ||
                                       (pocketNoise > 0.65f && (noise > 0.55f || noise2 > 0.55f)) ||
                                       (noise * noise2) > (0.45f * (1.7f - densityMultiplier));
            
            // === DEPTH-BASED SIMPLE CAVES ===
            float simpleDepthMultiplier = 1f;
            if (y < 25) simpleDepthMultiplier = 1.25f;    // Deep simple caves more common
            else if (y < 50) simpleDepthMultiplier = 1.1f; // Mid-level slightly more common
            else simpleDepthMultiplier = 0.9f;             // Surface caves less common
            
            // Apply simple depth bonuses
            bool hasDepthBasicTunnels = (noise * simpleDepthMultiplier) > (0.54f * (2.0f - densityMultiplier));
            bool hasDepthMediumCaves = ((noise + noise2) * simpleDepthMultiplier) > (0.60f * (1.8f - densityMultiplier));
            bool hasDepthLargePockets = ((noise + noise2 + pocketNoise) * simpleDepthMultiplier) > (0.68f * (1.6f - densityMultiplier)) && pocketNoise > 0.6f;
            
            return hasBasicTunnels || hasMediumSimpleCaves || hasLargePockets || hasSimpleConnections ||
                   hasDepthBasicTunnels || hasDepthMediumCaves || hasDepthLargePockets;
        }
        
        /// <summary>
        /// Calculates height-based factor for cave generation probability
        /// Higher values at optimal cave depths, lower near surface/bedrock
        /// </summary>
        private static float GetHeightFactor(int y)
        {
            // Optimal cave generation around Y=5-55 (extended to allow near-surface caves)
            if (y >= 5 && y <= 55)
                return 0f; // No bonus/penalty in optimal zone
            
            if (y < 5)
            {
                // Fewer caves near bedrock
                return (5f - y) * 0.2f;
            }
            else
            {
                // Gradually fewer caves approaching surface
                return (y - 55f) * 0.08f; // Moderate reduction near surface
            }
        }
        
        /// <summary>
        /// Fast, optimized cave entrance generation with organic variation
        /// Balances visual quality with performance for smooth chunk loading
        /// </summary>
        public static bool ShouldCreateCaveEntrance(Vector3Int worldPos, int surfaceHeight, int worldSeed)
        {
            // Only create entrances in reasonable range from surface
            if (worldPos.y < surfaceHeight - 20 || worldPos.y > surfaceHeight + 2)
                return false;
            
            float seedOffset = worldSeed % 10000 * 0.001f;
            float x = worldPos.x;
            float y = worldPos.y;
            float z = worldPos.z;
            
            // Fast pre-check: Only proceed if this could be an entrance location
            float preCheckNoise = Mathf.PerlinNoise(x * 0.008f + seedOffset, z * 0.008f + seedOffset * 1.7f);
            if (preCheckNoise < 0.82f) return false; // Quick rejection for most positions
            
            // Lightweight cave connectivity check (only check directly below)
            bool hasNearbyCave = false;
            for (int checkY = worldPos.y - 1; checkY >= worldPos.y - 8; checkY--)
            {
                Vector3Int checkPos = new Vector3Int(worldPos.x, checkY, worldPos.z);
                if (checkPos.y >= MIN_CAVE_HEIGHT && checkPos.y <= MAX_CAVE_HEIGHT)
                {
                    if (ShouldGenerateAdvancedCave(checkPos, worldSeed))
                    {
                        hasNearbyCave = true;
                        break;
                    }
                }
            }
            
            if (!hasNearbyCave) return false;
            
            // Simple organic variation using just 2 noise layers
            float entranceNoise1 = Mathf.PerlinNoise(x * 0.025f + seedOffset * 2.3f, z * 0.025f + y * 0.01f + seedOffset);
            float entranceNoise2 = Mathf.PerlinNoise(x * 0.06f + seedOffset * 3.1f, z * 0.06f + y * 0.02f + seedOffset * 1.4f);
            
            // Height-based entrance probability
            float heightFromSurface = surfaceHeight - worldPos.y;
            float heightFactor = heightFromSurface > 10 ? 0.5f : 1f;
            
            // Combine for organic entrance shapes (simplified)
            float combinedNoise = (entranceNoise1 * 0.7f + entranceNoise2 * 0.3f) * heightFactor;
            
            // Final entrance threshold (higher threshold = fewer entrances)
            return combinedNoise > 0.6f;
        }
        
        /// <summary>
        /// Fast organic entrance shaping - balances quality with performance
        /// </summary>
        public static bool ShouldCreateOrganicEntranceShape(Vector3Int worldPos, int surfaceHeight, int worldSeed)
        {
            // Use the optimized entrance check
            if (!ShouldCreateCaveEntrance(worldPos, surfaceHeight, worldSeed))
                return false;
                
            // Simple additional shaping with minimal computation
            float seedOffset = worldSeed % 10000 * 0.001f;
            float x = worldPos.x;
            float y = worldPos.y;
            float z = worldPos.z;
            
            // Single additional noise layer for organic variation
            float shapeNoise = Mathf.PerlinNoise(x * 0.1f + seedOffset, z * 0.1f + y * 0.04f + seedOffset);
            
            // Simple height-based variation
            float heightFromSurface = surfaceHeight - worldPos.y;
            float heightFactor = heightFromSurface < 5 ? 1f : 0.7f;
            
            // Final shape check (simplified)
            return (shapeNoise * heightFactor) > 0.5f;
        }
    }
}