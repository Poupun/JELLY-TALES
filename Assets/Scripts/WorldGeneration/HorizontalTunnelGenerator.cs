using UnityEngine;

namespace WorldGeneration
{
    /// <summary>
    /// Clean, progressive cave system focused on horizontal tunnel networks
    /// Phase 1: Simple horizontal tunnels that branch and connect naturally
    /// </summary>
    public static class HorizontalTunnelGenerator
    {
        [System.Serializable]
        public class TunnelSettings
        {
            [Header("Basic Tunnel Generation")]
            [Tooltip("Enable tunnel generation")]
            public bool enableTunnels = true;
            
            [Tooltip("Tunnel network spacing (0 = very sparse/far apart, 1 = very dense/close together)")]
            [Range(0f, 1f)] public float tunnelSpacing = 0.3f;
            
            [Tooltip("Tunnel thickness/size (0 = thin, 1 = thick tunnels)")]
            [Range(0f, 1f)] public float tunnelDensity = 0.3f;
            
            [Tooltip("Minimum Y level for tunnels")]
            public int minTunnelDepth = 10;
            
            [Tooltip("Maximum Y level for tunnels")]
            public int maxTunnelDepth = 90;
            
            [Header("Tunnel Shape")]
            [Tooltip("Average tunnel width (blocks)")]
            [Range(1f, 5f)] public float tunnelWidth = 2.5f;
            
            [Tooltip("Average tunnel height (blocks)")]
            [Range(1f, 4f)] public float tunnelHeight = 2.0f;
            
            [Tooltip("Tunnel width variation")]
            [Range(0f, 1f)] public float widthVariation = 0.3f;
            
            [Header("Tunnel Network")]
            [Tooltip("How often tunnels branch (0 = straight, 1 = very branchy)")]
            [Range(0f, 1f)] public float branchingFrequency = 0.4f;
            
            [Tooltip("Main tunnel direction scale (smaller = longer straight sections)")]
            [Range(0.001f, 0.1f)] public float directionScale = 0.02f;
            
            [Tooltip("How much tunnels curve and wander horizontally")]
            [Range(0f, 1f)] public float windiness = 0.5f;
            
            [Header("3-Network Cave System")]
            [Tooltip("Center Y level for Network A (deep tunnels)")]
            [Range(5f, 50f)] public float networkA_CenterY = 15f;
            
            [Tooltip("Y range for Network A (0 to this value)")]
            [Range(20f, 60f)] public float networkA_MaxY = 35f;
            
            [Tooltip("Center Y level for Network C (mid-level tunnels)")]
            [Range(30f, 80f)] public float networkC_CenterY = 55f;
            
            [Tooltip("Y range for Network C (networkA_MaxY to this value)")]
            [Range(50f, 90f)] public float networkC_MaxY = 70f;
            
            [Tooltip("Center Y level for Network Y (shallow tunnels)")]
            [Range(60f, 120f)] public float networkY_CenterY = 85f;
            
            [Header("Network A (Deep) - Custom Properties")]
            [Tooltip("Tunnel spacing for Network A (0 = sparse, 1 = dense)")]
            [Range(0f, 1f)] public float networkA_Spacing = 0.3f;
            
            [Tooltip("Tunnel thickness for Network A (0 = thin, 1 = thick)")]
            [Range(0f, 1f)] public float networkA_Density = 0.4f;
            
            [Tooltip("Tunnel width for Network A")]
            [Range(1f, 8f)] public float networkA_Width = 3.0f;
            
            [Tooltip("Tunnel height for Network A")]
            [Range(1f, 6f)] public float networkA_Height = 2.5f;
            
            [Tooltip("Branching frequency for Network A")]
            [Range(0f, 1f)] public float networkA_Branching = 0.5f;
            
            [Tooltip("Horizontal curve amount for Network A")]
            [Range(0f, 1f)] public float networkA_Windiness = 0.6f;
            
            [Header("Network C (Mid) - Custom Properties")]
            [Tooltip("Tunnel spacing for Network C (0 = sparse, 1 = dense)")]
            [Range(0f, 1f)] public float networkC_Spacing = 0.4f;
            
            [Tooltip("Tunnel thickness for Network C (0 = thin, 1 = thick)")]
            [Range(0f, 1f)] public float networkC_Density = 0.3f;
            
            [Tooltip("Tunnel width for Network C")]
            [Range(1f, 8f)] public float networkC_Width = 2.5f;
            
            [Tooltip("Tunnel height for Network C")]
            [Range(1f, 6f)] public float networkC_Height = 2.0f;
            
            [Tooltip("Branching frequency for Network C")]
            [Range(0f, 1f)] public float networkC_Branching = 0.4f;
            
            [Tooltip("Horizontal curve amount for Network C")]
            [Range(0f, 1f)] public float networkC_Windiness = 0.5f;
            
            [Header("Network Y (Shallow) - Custom Properties")]
            [Tooltip("Tunnel spacing for Network Y (0 = sparse, 1 = dense)")]
            [Range(0f, 1f)] public float networkY_Spacing = 0.2f;
            
            [Tooltip("Tunnel thickness for Network Y (0 = thin, 1 = thick)")]
            [Range(0f, 1f)] public float networkY_Density = 0.2f;
            
            [Tooltip("Tunnel width for Network Y")]
            [Range(1f, 8f)] public float networkY_Width = 2.0f;
            
            [Tooltip("Tunnel height for Network Y")]
            [Range(1f, 6f)] public float networkY_Height = 1.8f;
            
            [Tooltip("Branching frequency for Network Y")]
            [Range(0f, 1f)] public float networkY_Branching = 0.3f;
            
            [Tooltip("Horizontal curve amount for Network Y")]
            [Range(0f, 1f)] public float networkY_Windiness = 0.4f;
            
            [Header("Vertical Connector Tunnels")]
            [Tooltip("Enable vertical connector tunnels between networks")]
            public bool enableConnectorTunnels = true;
            
            [Tooltip("Frequency of connector tunnels (0 = very rare, 1 = common)")]
            [Range(0f, 1f)] public float connectorFrequency = 0.05f;
            
            [Tooltip("Connector tunnel width")]
            [Range(1f, 6f)] public float connectorWidth = 2.5f;
            
            [Tooltip("Connector tunnel height")]
            [Range(1f, 5f)] public float connectorHeight = 2.5f;
            
            [Tooltip("Organic curve strength for connectors (0 = straight, 1 = very curvy)")]
            [Range(0f, 1f)] public float connectorCurviness = 0.7f;
            
            [Tooltip("Length variation for connectors (0 = direct, 1 = wandering)")]
            [Range(0f, 1f)] public float connectorLengthVariation = 0.5f;
            
            [Header("Surface Cave Entrances")]
            [Tooltip("Enable natural cave entrances from surface to underground")]
            public bool enableCaveEntrances = true;
            
            [Tooltip("Frequency of cave entrances (0 = very rare, 1 = common)")]
            [Range(0f, 1f)] public float entranceFrequency = 0.15f;
            
            [Tooltip("Cave entrance width at surface")]
            [Range(2f, 8f)] public float entranceWidth = 4.0f;
            
            [Tooltip("Cave entrance depth (how far down they go)")]
            [Range(5f, 25f)] public float entranceDepth = 15f;
            
            [Tooltip("Natural shape variation (0 = circular, 1 = very organic)")]
            [Range(0f, 1f)] public float entranceOrganicness = 0.6f;
            
            [Tooltip("Entrance slope steepness (0 = gentle, 1 = steep)")]
            [Range(0f, 1f)] public float entranceSteepness = 0.4f;
            
            [Header("Debug")]
            [Tooltip("Enable debug logging for tunnel generation")]
            public bool enableDebugLogging = false;
        }
        
        /// <summary>
        /// Check if a block should be air (part of tunnel system)
        /// </summary>
        public static bool IsTunnelBlock(Vector3Int worldPos, int worldSeed, TunnelSettings settings)
        {
            if (!settings.enableTunnels) return false;
            
            // Check if within tunnel generation range
            if (worldPos.y < settings.minTunnelDepth || worldPos.y > settings.maxTunnelDepth)
                return false;
            
            // Generate tunnel network (horizontal networks + vertical connectors + surface entrances)
            bool isHorizontalTunnel = GenerateHorizontalTunnel(worldPos, worldSeed, settings);
            bool isConnectorTunnel = false;
            bool isCaveEntrance = false;
            
            if (!isHorizontalTunnel && settings.enableConnectorTunnels)
            {
                isConnectorTunnel = GenerateConnectorTunnel(worldPos, worldSeed, settings);
            }
            
            if (!isHorizontalTunnel && !isConnectorTunnel && settings.enableCaveEntrances)
            {
                isCaveEntrance = GenerateCaveEntrance(worldPos, worldSeed, settings);
            }
            
            bool isTunnel = isHorizontalTunnel || isConnectorTunnel || isCaveEntrance;
            
            // Debug logging with network info
            if (isTunnel && settings.enableDebugLogging && worldPos.x % 50 == 0 && worldPos.z % 50 == 0)
            {
                string networkName = worldPos.y <= settings.networkA_MaxY ? "A (Deep)" : 
                                   worldPos.y <= settings.networkC_MaxY ? "C (Mid)" : "Y (Shallow)";
                float networkSpacing = GetNetworkSpacing(worldPos.y, settings);
                float networkDensity = GetNetworkDensity(worldPos.y, settings);
                UnityEngine.Debug.Log($"🚇 TUNNEL BLOCK at {worldPos} (Network: {networkName}, Spacing: {networkSpacing:F2}, Density: {networkDensity:F2})");
            }
            
            return isTunnel;
        }
        
        /// <summary>
        /// Generate horizontal tunnel networks using Y-level dependent noise
        /// </summary>
        private static bool GenerateHorizontalTunnel(Vector3Int pos, int seed, TunnelSettings settings)
        {
            float x = pos.x;
            float y = pos.y;
            float z = pos.z;
            float seedOffset = (seed % 10000) * 0.1f;
            
            // FREQUENCY/SCALE CONTROL - use network-specific spacing
            float networkSpacing = GetNetworkSpacing(y, settings);
            float frequencyMultiplier = Mathf.Lerp(0.2f, 3.0f, networkSpacing);
            float adjustedPathScale = settings.directionScale * frequencyMultiplier;
            
            // CREATE 3 DISTINCT TUNNEL NETWORKS AT SPECIFIC Y LEVELS
            float yInfluence = GetNetworkYInfluence(y, settings);
            
            // Primary tunnel path - DIFFERENT at each Y level
            float pathNoise1 = Sample2DNoise(
                x * adjustedPathScale + seedOffset + yInfluence,
                z * adjustedPathScale + seedOffset + yInfluence
            );
            
            // Secondary tunnel path - DIFFERENT at each Y level
            float pathNoise2 = Sample2DNoise(
                x * adjustedPathScale + seedOffset + 1000 + yInfluence,
                z * adjustedPathScale + seedOffset + 1000 + yInfluence
            );
            
            // Check for tunnel paths - now each Y level has its own unique patterns
            bool onMainPath = IsOnTunnelPath(pos, pathNoise1, settings, seed, 0);
            bool onBranchPath = IsOnTunnelPath(pos, pathNoise2, settings, seed, 2000);
            
            // Apply branching - use network-specific branching frequency
            if (onBranchPath)
            {
                float branchChance = GetBranchChance(pos, seed, settings);
                float networkBranching = GetNetworkBranching(y, settings);
                if (branchChance < networkBranching)
                {
                    return true;
                }
            }
            
            return onMainPath;
        }
        
        /// <summary>
        /// Get network Y influence for 3 distinct tunnel networks
        /// </summary>
        private static float GetNetworkYInfluence(float y, TunnelSettings settings)
        {
            // Network A: Deep tunnels
            if (y <= settings.networkA_MaxY)
            {
                return 100f; // Network A seed offset
            }
            // Network C: Mid-level tunnels
            else if (y <= settings.networkC_MaxY)
            {
                return 500f; // Network C seed offset
            }
            // Network Y: Shallow tunnels
            else
            {
                return 900f; // Network Y seed offset
            }
        }
        
        /// <summary>
        /// Get the center Y coordinate for each network
        /// </summary>
        private static float GetNetworkCenterY(float y, TunnelSettings settings)
        {
            // Network A: User-adjustable center
            if (y <= settings.networkA_MaxY)
            {
                return settings.networkA_CenterY;
            }
            // Network C: User-adjustable center
            else if (y <= settings.networkC_MaxY)
            {
                return settings.networkC_CenterY;
            }
            // Network Y: User-adjustable center
            else
            {
                return settings.networkY_CenterY;
            }
        }
        
        /// <summary>
        /// Get network-specific tunnel spacing
        /// </summary>
        private static float GetNetworkSpacing(float y, TunnelSettings settings)
        {
            if (y <= settings.networkA_MaxY)
                return settings.networkA_Spacing;
            else if (y <= settings.networkC_MaxY)
                return settings.networkC_Spacing;
            else
                return settings.networkY_Spacing;
        }
        
        /// <summary>
        /// Get network-specific tunnel density
        /// </summary>
        private static float GetNetworkDensity(float y, TunnelSettings settings)
        {
            if (y <= settings.networkA_MaxY)
                return settings.networkA_Density;
            else if (y <= settings.networkC_MaxY)
                return settings.networkC_Density;
            else
                return settings.networkY_Density;
        }
        
        /// <summary>
        /// Get network-specific tunnel width
        /// </summary>
        private static float GetNetworkWidth(float y, TunnelSettings settings)
        {
            if (y <= settings.networkA_MaxY)
                return settings.networkA_Width;
            else if (y <= settings.networkC_MaxY)
                return settings.networkC_Width;
            else
                return settings.networkY_Width;
        }
        
        /// <summary>
        /// Get network-specific tunnel height
        /// </summary>
        private static float GetNetworkHeight(float y, TunnelSettings settings)
        {
            if (y <= settings.networkA_MaxY)
                return settings.networkA_Height;
            else if (y <= settings.networkC_MaxY)
                return settings.networkC_Height;
            else
                return settings.networkY_Height;
        }
        
        /// <summary>
        /// Get network-specific branching frequency
        /// </summary>
        private static float GetNetworkBranching(float y, TunnelSettings settings)
        {
            if (y <= settings.networkA_MaxY)
                return settings.networkA_Branching;
            else if (y <= settings.networkC_MaxY)
                return settings.networkC_Branching;
            else
                return settings.networkY_Branching;
        }
        
        /// <summary>
        /// Get network-specific windiness
        /// </summary>
        private static float GetNetworkWindiness(float y, TunnelSettings settings)
        {
            if (y <= settings.networkA_MaxY)
                return settings.networkA_Windiness;
            else if (y <= settings.networkC_MaxY)
                return settings.networkC_Windiness;
            else
                return settings.networkY_Windiness;
        }
        
        /// <summary>
        /// Get a consistent random value for a grid cell
        /// </summary>
        private static float GetGridSeed(int gridX, int gridZ, int worldSeed)
        {
            int hash = gridX;
            hash = hash * 31 + gridZ;
            hash = hash * 31 + worldSeed;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
            hash = (hash >> 16) ^ hash;
            return (hash & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }
        
        /// <summary>
        /// Check if position is on a tunnel path - each Y level has independent horizontal patterns
        /// </summary>
        private static bool IsOnTunnelPath(Vector3Int pos, float pathNoise, TunnelSettings settings, int seed, int seedOffset)
        {
            float x = pos.x;
            float y = pos.y;
            float z = pos.z;
            
            // Generate horizontal tunnel pattern for THIS specific Y level - use network-specific windiness
            float networkWindiness = GetNetworkWindiness(y, settings);
            float tunnelCenterOffset = (pathNoise - 0.5f) * networkWindiness * 20f;
            
            // Calculate horizontal tunnel center with curves
            float tunnelCenterX = x + Mathf.Sin(z * 0.1f + seedOffset) * tunnelCenterOffset;
            float tunnelCenterZ = z + Mathf.Sin(x * 0.1f + seedOffset) * tunnelCenterOffset;
            
            // Horizontal distance from tunnel center line
            float horizontalDistance = Mathf.Sqrt(
                Mathf.Pow(x - tunnelCenterX, 2f) + 
                Mathf.Pow(z - tunnelCenterZ, 2f)
            );
            
            // Get tunnel dimensions
            float currentWidth = GetTunnelWidth(x, z, y, seed, settings, seedOffset);
            float currentHeight = GetTunnelHeight(x, z, y, seed, settings, seedOffset);
            
            // Create proper horizontal tunnels with vertical thickness
            // Each network gets some vertical thickness (a few blocks high)
            float networkCenterY = GetNetworkCenterY(y, settings);
            float verticalDistance = Mathf.Abs(y - networkCenterY);
            
            // Check if we're within tunnel bounds (elliptical cross-section)
            float normalizedH = horizontalDistance / currentWidth;
            float normalizedV = verticalDistance / currentHeight;
            float tunnelDistance = normalizedH * normalizedH + normalizedV * normalizedV;
            
            // Apply density threshold - use network-specific density
            float networkDensity = GetNetworkDensity(y, settings);
            float densityThreshold = 1f - networkDensity;
            
            return tunnelDistance < (1f - densityThreshold);
        }
        
        
        /// <summary>
        /// Get tunnel width with local variation
        /// </summary>
        private static float GetTunnelWidth(float x, float z, float y, int seed, TunnelSettings settings, int seedOffset)
        {
            // Apply frequency control to tunnel width variation
            float networkSpacing = GetNetworkSpacing(y, settings);
            float frequencyMultiplier = Mathf.Lerp(0.2f, 3.0f, networkSpacing);
            float adjustedWidthScale = 0.05f * frequencyMultiplier;
            
            float variationNoise = Sample2DNoise(
                x * adjustedWidthScale + seed * 0.1f + seedOffset,
                z * adjustedWidthScale + seed * 0.1f + seedOffset
            );
            
            // Use network-specific width
            float networkWidth = GetNetworkWidth(y, settings);
            float variation = (variationNoise - 0.5f) * settings.widthVariation * networkWidth;
            return networkWidth + variation;
        }
        
        /// <summary>
        /// Get tunnel height with local variation
        /// </summary>
        private static float GetTunnelHeight(float x, float z, float y, int seed, TunnelSettings settings, int seedOffset)
        {
            // Apply frequency control to tunnel height variation
            float networkSpacing = GetNetworkSpacing(y, settings);
            float frequencyMultiplier = Mathf.Lerp(0.2f, 3.0f, networkSpacing);
            float adjustedHeightScale = 0.05f * frequencyMultiplier;
            
            float variationNoise = Sample2DNoise(
                x * adjustedHeightScale + seed * 0.1f + seedOffset + 500,
                z * adjustedHeightScale + seed * 0.1f + seedOffset + 500
            );
            
            // Use network-specific height
            float networkHeight = GetNetworkHeight(y, settings);
            float variation = (variationNoise - 0.5f) * settings.widthVariation * networkHeight;
            return networkHeight + variation;
        }
        
        /// <summary>
        /// Calculate chance for branch tunnels at this position
        /// </summary>
        private static float GetBranchChance(Vector3Int pos, int seed, TunnelSettings settings)
        {
            // Apply frequency control to branching patterns - use network-specific spacing
            float networkSpacing = GetNetworkSpacing(pos.y, settings);
            float frequencyMultiplier = Mathf.Lerp(0.2f, 3.0f, networkSpacing);
            float adjustedBranchScale = 0.03f * frequencyMultiplier;
            
            float branchNoise = Sample2DNoise(
                pos.x * adjustedBranchScale + seed * 0.1f + 5000,
                pos.z * adjustedBranchScale + seed * 0.1f + 5000
            );
            
            return branchNoise;
        }
        
        /// <summary>
        /// Generate vertical connector tunnels between networks
        /// </summary>
        private static bool GenerateConnectorTunnel(Vector3Int pos, int seed, TunnelSettings settings)
        {
            float x = pos.x;
            float y = pos.y;
            float z = pos.z;
            float seedOffset = (seed % 10000) * 0.1f;
            
            // Check for connector spawn points using large-scale noise (rare occurrences)
            float connectorSpawnNoise = Sample2DNoise(
                x * 0.002f + seedOffset * 5.7f,  // Very low frequency for rare spawns
                z * 0.002f + seedOffset * 5.7f
            );
            
            // FIXED: Connector frequency threshold - higher frequency = more connectors
            float threshold = 1f - settings.connectorFrequency;
            if (connectorSpawnNoise > threshold)
            {
                // SIMPLIFIED diagonal connectors that actually work
                if (settings.connectorFrequency > 0.5f) // Easier test mode
                {
                    // Create connector grid - every 40 blocks
                    int testGridSize = 40;
                    int testGridX = Mathf.FloorToInt(x / testGridSize);
                    int testGridZ = Mathf.FloorToInt(z / testGridSize);
                    
                    // Check if we're in a connector grid cell
                    float testGridNoise = GetGridSeed(testGridX, testGridZ, seed + 3000);
                    if (testGridNoise > 0.7f) // 30% of grid cells have connectors
                    {
                        // Determine connector type based on grid position
                        float connectorType = GetGridSeed(testGridX, testGridZ, seed + 4000);
                        
                        float startY, endY;
                        if (connectorType < 0.5f) // A to C connector
                        {
                            startY = settings.networkA_CenterY;
                            endY = settings.networkC_CenterY;
                        }
                        else // C to Y connector  
                        {
                            startY = settings.networkC_CenterY;
                            endY = settings.networkY_CenterY;
                        }
                        
                        // Only generate in the Y range between networks
                        if (y >= startY - 2f && y <= endY + 2f)
                        {
                            // Calculate progress (0 to 1) through the connector
                            float totalHeight = endY - startY;
                            float progress = (y - startY) / totalHeight;
                            progress = Mathf.Clamp01(progress);
                            
                            // Grid cell center
                            float cellCenterX = testGridX * testGridSize + testGridSize * 0.5f;
                            float cellCenterZ = testGridZ * testGridSize + testGridSize * 0.5f;
                            
                            // Diagonal movement - moves horizontally as it goes up
                            float diagonalOffset = totalHeight * 0.6f; // 60% of height as horizontal movement
                            float angle = GetGridSeed(testGridX, testGridZ, seed + 5000) * 2f * Mathf.PI;
                            
                            float currentCenterX = cellCenterX + Mathf.Cos(angle) * diagonalOffset * progress;
                            float currentCenterZ = cellCenterZ + Mathf.Sin(angle) * diagonalOffset * progress;
                            
                            // Check distance from current center
                            float distance = Mathf.Sqrt(
                                (x - currentCenterX) * (x - currentCenterX) + 
                                (z - currentCenterZ) * (z - currentCenterZ)
                            );
                            
                            if (distance < settings.connectorWidth)
                            {
                                if (settings.enableDebugLogging && x % 20 == 0 && z % 20 == 0)
                                {
                                    string type = connectorType < 0.5f ? "A→C" : "C→Y";
                                    UnityEngine.Debug.Log($"🚇 SIMPLE CONNECTOR {type} at {pos} (progress: {progress:F2})");
                                }
                                return true;
                            }
                        }
                    }
                }
            }
            
            // Fallback: Use the simpler reliable connector system for lower frequencies too
            // Use grid-based generation for all frequencies
            int gridSize = Mathf.RoundToInt(Mathf.Lerp(80f, 30f, settings.connectorFrequency)); // Larger grid = rarer
            int gridX = Mathf.FloorToInt(x / gridSize);
            int gridZ = Mathf.FloorToInt(z / gridSize);
            
            float gridNoise = GetGridSeed(gridX, gridZ, seed + 6000);
            float spawnThreshold = 1f - (settings.connectorFrequency * 0.8f); // Scale threshold based on frequency
            
            if (gridNoise > spawnThreshold)
            {
                // Determine connector type
                float connectorType = GetGridSeed(gridX, gridZ, seed + 7000);
                
                float startY, endY;
                string connectionType;
                
                if (connectorType < 0.4f) // A to C (40%)
                {
                    startY = settings.networkA_CenterY;
                    endY = settings.networkC_CenterY;
                    connectionType = "A→C";
                }
                else if (connectorType < 0.7f) // C to Y (30%)
                {
                    startY = settings.networkC_CenterY;
                    endY = settings.networkY_CenterY;
                    connectionType = "C→Y";
                }
                else // Direct A to Y for variety (30%)
                {
                    startY = settings.networkA_CenterY;
                    endY = settings.networkY_CenterY;
                    connectionType = "A→Y";
                }
                
                // Check if Y is in range
                if (y >= Mathf.Min(startY, endY) - 3f && y <= Mathf.Max(startY, endY) + 3f)
                {
                    // Calculate progress through connector
                    float totalHeight = Mathf.Abs(endY - startY);
                    float progress = (y - Mathf.Min(startY, endY)) / totalHeight;
                    progress = Mathf.Clamp01(progress);
                    
                    // Grid cell center with some randomness
                    float cellCenterX = gridX * gridSize + gridSize * 0.5f;
                    float cellCenterZ = gridZ * gridSize + gridSize * 0.5f;
                    
                    // Add cell variation
                    float cellVariationX = (GetGridSeed(gridX, gridZ, seed + 8000) - 0.5f) * gridSize * 0.3f;
                    float cellVariationZ = (GetGridSeed(gridX, gridZ, seed + 9000) - 0.5f) * gridSize * 0.3f;
                    cellCenterX += cellVariationX;
                    cellCenterZ += cellVariationZ;
                    
                    // Diagonal movement
                    float diagonalDistance = totalHeight * Mathf.Lerp(0.4f, 1.2f, settings.connectorLengthVariation);
                    float angle = GetGridSeed(gridX, gridZ, seed + 10000) * 2f * Mathf.PI;
                    
                    float currentCenterX = cellCenterX + Mathf.Cos(angle) * diagonalDistance * progress;
                    float currentCenterZ = cellCenterZ + Mathf.Sin(angle) * diagonalDistance * progress;
                    
                    // Add organic curves
                    if (settings.connectorCurviness > 0.1f)
                    {
                        float curveX = Mathf.Sin(progress * Mathf.PI * 2f) * settings.connectorCurviness * 8f;
                        float curveZ = Mathf.Cos(progress * Mathf.PI * 1.5f) * settings.connectorCurviness * 6f;
                        currentCenterX += curveX;
                        currentCenterZ += curveZ;
                    }
                    
                    // Check distance
                    float distance = Mathf.Sqrt(
                        (x - currentCenterX) * (x - currentCenterX) + 
                        (z - currentCenterZ) * (z - currentCenterZ)
                    );
                    
                    // Variable width - wider at ends
                    float widthMultiplier = 1f + Mathf.Sin(progress * Mathf.PI) * 0.5f; // Wider in middle
                    float currentWidth = settings.connectorWidth * widthMultiplier;
                    
                    if (distance < currentWidth)
                    {
                        if (settings.enableDebugLogging && x % 30 == 0 && z % 30 == 0)
                        {
                            UnityEngine.Debug.Log($"🚇 RELIABLE CONNECTOR {connectionType} at {pos} (progress: {progress:F2}, width: {currentWidth:F1})");
                        }
                        return true;
                    }
                }
            }
            
            return false; // No connector in this area
        }
        
        /// <summary>
        /// Generate natural diagonal cave entrances that intelligently reach the actual surface
        /// </summary>
        private static bool GenerateCaveEntrance(Vector3Int pos, int seed, TunnelSettings settings)
        {
            float x = pos.x;
            float y = pos.y;
            float z = pos.z;
            float seedOffset = (seed % 10000) * 0.1f;
            
            // Grid-based entrance placement
            int entranceGridSize = Mathf.RoundToInt(Mathf.Lerp(100f, 50f, settings.entranceFrequency));
            int entranceGridX = Mathf.FloorToInt(x / entranceGridSize);
            int entranceGridZ = Mathf.FloorToInt(z / entranceGridSize);
            
            // Check if this grid cell should have an entrance
            float entranceSpawnNoise = GetGridSeed(entranceGridX, entranceGridZ, seed + 15000);
            float entranceThreshold = 1f - (settings.entranceFrequency * 0.8f);
            
            if (entranceSpawnNoise <= entranceThreshold)
            {
                return false; // No entrance in this grid cell
            }
            
            // Calculate entrance START point (surface) with variation
            float entranceStartX = entranceGridX * entranceGridSize + entranceGridSize * 0.5f;
            float entranceStartZ = entranceGridZ * entranceGridSize + entranceGridSize * 0.5f;
            
            // Add organic variation to start position
            float startVariationX = (GetGridSeed(entranceGridX, entranceGridZ, seed + 16000) - 0.5f) * entranceGridSize * 0.3f;
            float startVariationZ = (GetGridSeed(entranceGridX, entranceGridZ, seed + 17000) - 0.5f) * entranceGridSize * 0.3f;
            entranceStartX += startVariationX;
            entranceStartZ += startVariationZ;
            
            // Get EXACT surface height matching the block generator
            int exactSurfaceY = GetExactSurfaceHeight((int)entranceStartX, (int)entranceStartZ, seed);
            
            // Calculate entrance END point (near Network A) with diagonal offset
            float networkACenterY = settings.networkA_CenterY;
            float entranceEndY = networkACenterY + 2f; // Just above Network A
            
            // Create DIAGONAL entrance path
            float totalVerticalDistance = exactSurfaceY - entranceEndY;
            float diagonalHorizontalDistance = totalVerticalDistance * Mathf.Lerp(0.7f, 1.8f, settings.entranceSteepness);
            
            // Diagonal direction based on grid position
            float diagonalAngle = GetGridSeed(entranceGridX, entranceGridZ, seed + 18000) * 2f * Mathf.PI;
            
            float entranceEndX = entranceStartX + Mathf.Cos(diagonalAngle) * diagonalHorizontalDistance;
            float entranceEndZ = entranceStartZ + Mathf.Sin(diagonalAngle) * diagonalHorizontalDistance;
            
            // Extend the range to ABOVE surface level for proper surface connection
            if (y > exactSurfaceY + 8f || y < entranceEndY - 5f)
            {
                return false; // Outside entrance vertical range
            }
            
            // Calculate progress along the diagonal entrance (0 = surface, 1 = Network A)
            float verticalProgress = (exactSurfaceY - y) / totalVerticalDistance;
            verticalProgress = Mathf.Clamp01(verticalProgress);
            
            // Calculate current position along the diagonal path
            float currentEntranceX = Mathf.Lerp(entranceStartX, entranceEndX, verticalProgress);
            float currentEntranceZ = Mathf.Lerp(entranceStartZ, entranceEndZ, verticalProgress);
            
            // Add ZIGZAG WIGGLE motion for more natural caves
            float wiggleFrequency = 8f; // How often the zigzag changes direction
            float wiggleAmplitude = settings.entranceOrganicness * 6f; // How strong the zigzag is
            
            // Primary wiggle (back and forth)
            float primaryWiggle = Mathf.Sin(verticalProgress * Mathf.PI * wiggleFrequency) * wiggleAmplitude;
            // Secondary wiggle (perpendicular to primary, different frequency)
            float secondaryWiggle = Mathf.Cos(verticalProgress * Mathf.PI * wiggleFrequency * 1.3f) * wiggleAmplitude * 0.7f;
            
            // Apply wiggle in perpendicular directions to the main diagonal
            float perpAngle1 = diagonalAngle + Mathf.PI * 0.5f; // 90 degrees to diagonal
            float perpAngle2 = diagonalAngle; // Parallel to diagonal
            
            currentEntranceX += Mathf.Cos(perpAngle1) * primaryWiggle + Mathf.Cos(perpAngle2) * secondaryWiggle * 0.5f;
            currentEntranceZ += Mathf.Sin(perpAngle1) * primaryWiggle + Mathf.Sin(perpAngle2) * secondaryWiggle * 0.5f;
            
            // Add organic long-scale curves
            float longCurveX = Mathf.Sin(verticalProgress * Mathf.PI * 2f) * settings.entranceOrganicness * 3f;
            float longCurveZ = Mathf.Cos(verticalProgress * Mathf.PI * 1.5f) * settings.entranceOrganicness * 2f;
            currentEntranceX += longCurveX;
            currentEntranceZ += longCurveZ;
            
            // Calculate distance from the current entrance center
            float horizontalDistance = Mathf.Sqrt(
                (x - currentEntranceX) * (x - currentEntranceX) + 
                (z - currentEntranceZ) * (z - currentEntranceZ)
            );
            
            // Create natural funnel shape - wide at surface, narrow at depth
            float surfaceRadius = settings.entranceWidth;
            float bottomRadius = settings.entranceWidth * 0.4f;
            float currentRadius = Mathf.Lerp(surfaceRadius, bottomRadius, verticalProgress);
            
            // Add organic shape variation based on position
            float shapeNoise1 = Sample2DNoise(
                (x - currentEntranceX) * 0.08f + seed * 0.01f + seedOffset,
                (z - currentEntranceZ) * 0.08f + seed * 0.01f + seedOffset
            );
            float shapeNoise2 = Sample2DNoise(
                (x - currentEntranceX) * 0.15f + y * 0.05f + seedOffset + 1000,
                (z - currentEntranceZ) * 0.15f + y * 0.05f + seedOffset + 1000
            );
            
            // Apply organic shape modulation
            float organicMultiplier = 1f + (shapeNoise1 - 0.5f) * settings.entranceOrganicness * 0.6f;
            organicMultiplier += (shapeNoise2 - 0.5f) * settings.entranceOrganicness * 0.3f;
            currentRadius *= Mathf.Clamp(organicMultiplier, 0.5f, 1.8f);
            
            // Check if within entrance tunnel
            if (horizontalDistance < currentRadius)
            {
                // Add natural cave wall irregularities
                float wallDetailNoise = Sample2DNoise(
                    x * 0.2f + y * 0.1f + seedOffset + 2000,
                    z * 0.2f + y * 0.1f + seedOffset + 2000
                );
                
                // Create wall texture - small pockets and protrusions
                float wallThickness = currentRadius * 0.15f;
                float distanceFromWall = currentRadius - horizontalDistance;
                
                // Add stalactites/stalagmites near entrance walls
                if (distanceFromWall < wallThickness)
                {
                    float wallFeatureChance = Sample2DNoise(
                        x * 0.25f + y * 0.2f + seedOffset + 3000,
                        z * 0.25f + y * 0.2f + seedOffset + 3000
                    );
                    
                    if (wallFeatureChance > 0.7f) // 30% chance for wall features
                    {
                        return false; // Create natural wall protrusions
                    }
                }
                
                // Add floor/ceiling irregularities for more natural caves
                float floorCeilingNoise = Sample2DNoise(
                    x * 0.12f + seedOffset + 4000,
                    z * 0.12f + seedOffset + 4000
                );
                
                if (settings.enableDebugLogging && x % 40 == 0 && z % 40 == 0)
                {
                    UnityEngine.Debug.Log($"🕳️ ZIGZAG ENTRANCE at {pos} (progress: {verticalProgress:F2}, exactSurface: {exactSurfaceY}, networkA: {networkACenterY}, radius: {currentRadius:F1})");
                }
                
                return true;
            }
            
            return false; // Outside entrance radius
        }
        
        /// <summary>
        /// Get EXACT surface height matching OptimizedBlockGenerator calculation
        /// </summary>
        private static int GetExactSurfaceHeight(int worldX, int worldZ, int worldSeed)
        {
            // EXACTLY match OptimizedBlockGenerator.CalculateSurfaceHeightOptimized()
            float seedOffset = (worldSeed % 10000) * 0.01f;
            
            // Base terrain - EXACT match
            float baseNoise = Mathf.PerlinNoise(worldX * 0.008f + seedOffset, worldZ * 0.008f + seedOffset);
            
            // Hills - EXACT match
            float hillNoise = Mathf.PerlinNoise(worldX * 0.015f + seedOffset * 1.7f, worldZ * 0.015f + seedOffset * 1.7f);
            float hillMultiplier = hillNoise > 0.3f ? Mathf.Pow((hillNoise - 0.3f) / 0.7f, 1.2f) : 0f;
            
            // Detail - EXACT match  
            float detailNoise = Mathf.PerlinNoise(worldX * 0.05f + seedOffset, worldZ * 0.05f + seedOffset);
            
            // EXACT surface calculation match
            float combinedHeight = 110f + baseNoise * 5f + hillMultiplier * 4f + detailNoise * 0.3f;
            return Mathf.RoundToInt(combinedHeight);
        }
        
        /// <summary>
        /// Get the start point for a connector tunnel (procedurally placed in network layers)
        /// </summary>
        private static Vector3 GetConnectorStartPoint(float x, float z, int seed, TunnelSettings settings, int seedOffset)
        {
            // Use grid-based positioning for more consistent connector placement
            int gridSize = 128; // Connector grid size
            int gridX = Mathf.FloorToInt(x / gridSize);
            int gridZ = Mathf.FloorToInt(z / gridSize);
            
            float gridSeed = GetGridSeed(gridX, gridZ, seed + 1000);
            
            // Weight network selection based on depth preference (deeper networks more likely to connect)
            float networkSelection = gridSeed;
            float startY;
            
            if (networkSelection < 0.4f) // Favor deep network (40%)
            {
                startY = settings.networkA_CenterY + (Sample2DNoise(gridX * 0.1f + seedOffset, gridZ * 0.1f + seedOffset) - 0.5f) * 8f;
            }
            else if (networkSelection < 0.7f) // Mid network (30%)
            {
                startY = settings.networkC_CenterY + (Sample2DNoise(gridX * 0.1f + seedOffset + 500, gridZ * 0.1f + seedOffset + 500) - 0.5f) * 6f;
            }
            else // Shallow network (30%)
            {
                startY = settings.networkY_CenterY + (Sample2DNoise(gridX * 0.1f + seedOffset + 1000, gridZ * 0.1f + seedOffset + 1000) - 0.5f) * 4f;
            }
            
            // Position within grid cell with organic variation
            float cellX = (gridX * gridSize) + (Sample2DNoise(gridX * 0.07f + seedOffset + 2000, gridZ * 0.07f + seedOffset + 2000) * gridSize);
            float cellZ = (gridZ * gridSize) + (Sample2DNoise(gridX * 0.07f + seedOffset + 3000, gridZ * 0.07f + seedOffset + 3000) * gridSize);
            
            // Add fine-scale positioning variation
            float startXOffset = (Sample2DNoise(cellX * 0.01f + seedOffset + 4000, cellZ * 0.01f + seedOffset + 4000) - 0.5f) * 20f;
            float startZOffset = (Sample2DNoise(cellX * 0.01f + seedOffset + 5000, cellZ * 0.01f + seedOffset + 5000) - 0.5f) * 20f;
            
            return new Vector3(cellX + startXOffset, startY, cellZ + startZOffset);
        }
        
        /// <summary>
        /// Get the end point for a connector tunnel (ONLY connects to 1 adjacent network)
        /// </summary>
        private static Vector3 GetConnectorEndPoint(float x, float z, int seed, TunnelSettings settings, int seedOffset)
        {
            Vector3 startPoint = GetConnectorStartPoint(x, z, seed, settings, seedOffset);
            
            // Use same grid system for consistency
            int gridSize = 128;
            int gridX = Mathf.FloorToInt(x / gridSize);
            int gridZ = Mathf.FloorToInt(z / gridSize);
            
            float endGridSeed = GetGridSeed(gridX, gridZ, seed + 2000);
            
            // Determine which network the start point belongs to
            float startDistToA = Mathf.Abs(startPoint.y - settings.networkA_CenterY);
            float startDistToC = Mathf.Abs(startPoint.y - settings.networkC_CenterY);
            float startDistToY = Mathf.Abs(startPoint.y - settings.networkY_CenterY);
            
            float endY;
            
            // ONLY connect to adjacent networks (no A->Y direct connections)
            if (startDistToA < startDistToC && startDistToA < startDistToY) // Started from A (deep)
            {
                // A can ONLY connect to C (no direct A->Y)
                endY = settings.networkC_CenterY + (Sample2DNoise(gridX * 0.13f + seedOffset + 6000, gridZ * 0.13f + seedOffset + 6000) - 0.5f) * 4f;
            }
            else if (startDistToC < startDistToY) // Started from C (mid)
            {
                // C can connect to either A or Y
                if (endGridSeed < 0.5f) // 50% to A
                {
                    endY = settings.networkA_CenterY + (Sample2DNoise(gridX * 0.13f + seedOffset + 8000, gridZ * 0.13f + seedOffset + 8000) - 0.5f) * 6f;
                }
                else // 50% to Y
                {
                    endY = settings.networkY_CenterY + (Sample2DNoise(gridX * 0.13f + seedOffset + 9000, gridZ * 0.13f + seedOffset + 9000) - 0.5f) * 4f;
                }
            }
            else // Started from Y (shallow)
            {
                // Y can ONLY connect to C (no direct Y->A)
                endY = settings.networkC_CenterY + (Sample2DNoise(gridX * 0.13f + seedOffset + 10000, gridZ * 0.13f + seedOffset + 10000) - 0.5f) * 4f;
            }
            
            // Calculate diagonal distance for natural paths
            float verticalDistance = Mathf.Abs(endY - startPoint.y);
            float diagonalDistance = verticalDistance * Mathf.Lerp(1.2f, 2.0f, settings.connectorLengthVariation);
            
            // Create diagonal direction with organic variation
            float directionNoise = Sample2DNoise(gridX * 0.05f + seedOffset + 12000, gridZ * 0.05f + seedOffset + 12000);
            float baseAngle = directionNoise * 2f * Mathf.PI;
            
            // Add some randomness to angle for natural feel
            float angleVariation = (Sample2DNoise(gridX * 0.08f + seedOffset + 13000, gridZ * 0.08f + seedOffset + 13000) - 0.5f) * 1.0f;
            float finalAngle = baseAngle + angleVariation;
            
            float endXOffset = Mathf.Cos(finalAngle) * diagonalDistance;
            float endZOffset = Mathf.Sin(finalAngle) * diagonalDistance;
            
            // Add smaller organic variation for natural positioning
            float organicVariationX = (Sample2DNoise(x * 0.012f + seedOffset + 14000, z * 0.012f + seedOffset + 14000) - 0.5f) * 8f;
            float organicVariationZ = (Sample2DNoise(x * 0.012f + seedOffset + 15000, z * 0.012f + seedOffset + 15000) - 0.5f) * 8f;
            
            return new Vector3(
                startPoint.x + endXOffset + organicVariationX,
                endY,
                startPoint.z + endZOffset + organicVariationZ
            );
        }
        
        /// <summary>
        /// Check if a position is on the organic connector tunnel path
        /// </summary>
        private static bool IsOnConnectorPath(Vector3 pos, Vector3 start, Vector3 end, int seed, TunnelSettings settings, int seedOffset)
        {
            // Calculate the progress along the connector (0 to 1)
            float totalDistance = Vector3.Distance(start, end);
            if (totalDistance < 10f) return false; // Minimum distance for valid connector
            
            // Use 3D distance calculation for better path following
            Vector3 pathDirection = (end - start).normalized;
            Vector3 toPos = pos - start;
            float projectedDistance = Vector3.Dot(toPos, pathDirection);
            float progress = projectedDistance / totalDistance;
            
            // Extended range for organic tunnels
            if (progress < -0.1f || progress > 1.1f) return false;
            
            // Calculate multiple points along the path for smoother organic curves
            int segments = Mathf.Max(3, (int)(totalDistance * 0.1f));
            float minDistanceToPath = float.MaxValue;
            
            for (int i = 0; i <= segments; i++)
            {
                float segmentProgress = (float)i / segments;
                Vector3 pathPoint = GetCurvedPathPosition(start, end, segmentProgress, seed, settings, seedOffset);
                float distanceToSegment = Vector3.Distance(pos, pathPoint);
                minDistanceToPath = Mathf.Min(minDistanceToPath, distanceToSegment);
            }
            
            // Use elliptical cross-section for more organic feel
            float horizontalRadius = settings.connectorWidth * 0.5f;
            float verticalRadius = settings.connectorHeight * 0.5f;
            
            // Add organic thickness variation
            float thicknessNoise = Sample2DNoise(
                pos.x * 0.02f + seed * 0.01f + seedOffset + 10000,
                pos.z * 0.02f + seed * 0.01f + seedOffset + 10000
            );
            float thicknessMultiplier = 0.7f + thicknessNoise * 0.6f; // 0.7 to 1.3
            
            float effectiveRadius = Mathf.Max(horizontalRadius, verticalRadius) * thicknessMultiplier;
            
            return minDistanceToPath < effectiveRadius;
        }
        
        /// <summary>
        /// Get position on curved connector path using organic curves
        /// </summary>
        private static Vector3 GetCurvedPathPosition(Vector3 start, Vector3 end, float progress, int seed, TunnelSettings settings, int seedOffset)
        {
            // Base linear interpolation
            Vector3 linearPos = Vector3.Lerp(start, end, progress);
            
            if (settings.connectorCurviness <= 0.01f)
            {
                return linearPos; // Straight connector
            }
            
            // Multi-scale organic curves for natural feel
            float baseScale = Vector3.Distance(start, end) * 0.01f;
            float progressNoise = progress * 15f; // More detailed curves
            
            // Large-scale curves (main path deviation)
            float largeCurveX = Sample2DNoise(
                progressNoise * 0.3f + seed * 0.01f + seedOffset + 7000,
                start.x * 0.001f + start.z * 0.001f
            );
            float largeCurveY = Sample2DNoise(
                progressNoise * 0.2f + seed * 0.01f + seedOffset + 8000,
                start.y * 0.001f + end.y * 0.001f
            );
            float largeCurveZ = Sample2DNoise(
                progressNoise * 0.3f + seed * 0.01f + seedOffset + 9000,
                start.z * 0.001f + start.x * 0.001f
            );
            
            // Medium-scale curves (secondary deviation)
            float medCurveX = Sample2DNoise(
                progressNoise + seed * 0.01f + seedOffset + 10000,
                linearPos.y * 0.01f
            );
            float medCurveZ = Sample2DNoise(
                progressNoise + seed * 0.01f + seedOffset + 11000,
                linearPos.x * 0.01f
            );
            
            // Small-scale curves (fine detail)
            float smallCurveX = Sample2DNoise(
                progressNoise * 3f + seed * 0.01f + seedOffset + 12000,
                linearPos.z * 0.05f
            );
            float smallCurveZ = Sample2DNoise(
                progressNoise * 3f + seed * 0.01f + seedOffset + 13000,
                linearPos.x * 0.05f
            );
            
            // Smooth falloff at tunnel ends with more natural curve
            float endFalloff = Mathf.Sin(progress * Mathf.PI);
            float strengthFalloff = endFalloff * endFalloff; // More gradual falloff
            
            // Combine curve scales with different strengths
            float curveStrength = settings.connectorCurviness;
            float totalDistance = Vector3.Distance(start, end);
            
            Vector3 curveOffset = new Vector3(
                // Horizontal X curves
                ((largeCurveX - 0.5f) * 8f + (medCurveX - 0.5f) * 3f + (smallCurveX - 0.5f) * 1f) * curveStrength * strengthFalloff,
                // Vertical Y curves (more conservative)
                (largeCurveY - 0.5f) * 2f * curveStrength * strengthFalloff * 0.3f,
                // Horizontal Z curves
                ((largeCurveZ - 0.5f) * 8f + (medCurveZ - 0.5f) * 3f + (smallCurveZ - 0.5f) * 1f) * curveStrength * strengthFalloff
            );
            
            // Scale curves based on distance for proportional organic feel
            curveOffset *= Mathf.Min(1f, totalDistance * 0.1f);
            
            return linearPos + curveOffset;
        }
        
        /// <summary>
        /// Simple 2D Perlin noise sampling
        /// </summary>
        private static float Sample2DNoise(float x, float y)
        {
            return Mathf.PerlinNoise(x, y);
        }
    }
}