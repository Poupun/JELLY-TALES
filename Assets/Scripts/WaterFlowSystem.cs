using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple Minecraft-style water flow system
/// Water levels: 0 = source block (full), 1-7 = flowing water (decreasing)
/// </summary>
public class WaterFlowSystem : MonoBehaviour
{
    [Header("Water Flow Settings")]
    [Tooltip("Maximum horizontal spread distance from source")]
    [Range(1, 8)]
    public int maxFlowDistance = 7;

    [Tooltip("Tick rate for water updates (seconds)")]
    [Range(0.1f, 2f)]
    public float waterTickRate = 0.5f;

    [Tooltip("Enable water flow simulation")]
    public bool enableWaterFlow = true;

    [Tooltip("Max water blocks to update per tick")]
    [Range(10, 500)]
    public int maxUpdatesPerTick = 100;

    [Tooltip("How often to update chunk meshes (every N ticks)")]
    [Range(1, 10)]
    public int meshUpdateInterval = 5;

    // Water level storage: position -> level (0 = source, 1-7 = flowing)
    private Dictionary<Vector3Int, byte> waterLevels = new Dictionary<Vector3Int, byte>();

    private WorldGenerator worldGenerator;
    private float tickTimer = 0f;

    // Flag to prevent recursive water placement during spreading
    private bool isSpreading = false;

    // Track chunks needing mesh updates across multiple ticks
    private HashSet<Vector2Int> dirtyChunks = new HashSet<Vector2Int>();
    private int ticksSinceLastMeshUpdate = 0;

    void Start()
    {
        worldGenerator = GetComponent<WorldGenerator>();
        if (worldGenerator == null)
        {
            Debug.LogError("WaterFlowSystem: WorldGenerator not found!");
            enabled = false;
        }
    }

    void Update()
    {
        if (!enableWaterFlow || worldGenerator == null) return;

        tickTimer += Time.deltaTime;
        if (tickTimer >= waterTickRate)
        {
            tickTimer = 0f;
            ProcessWaterUpdates();
        }
    }

    /// <summary>
    /// Register a water source block (called after water is already placed)
    /// </summary>
    public void PlaceWaterSource(Vector3Int pos)
    {
        // Skip if we're placing water programmatically (not by player)
        if (isSpreading) return;

        // Only register if not already tracked
        if (waterLevels.ContainsKey(pos)) return;

        // Register as source block (level 0)
        waterLevels[pos] = 0;
        Debug.Log($"WaterFlowSystem: Registered water source at {pos}");
    }

    /// <summary>
    /// Process all water updates
    /// </summary>
    private void ProcessWaterUpdates()
    {
        if (waterLevels.Count == 0) return;

        // Make a snapshot to avoid modification during iteration
        var waterSnapshot = new List<KeyValuePair<Vector3Int, byte>>(waterLevels);

        int updates = 0;

        isSpreading = true;

        foreach (var kvp in waterSnapshot)
        {
            if (updates >= maxUpdatesPerTick) break;

            Vector3Int pos = kvp.Key;
            byte level = kvp.Value;

            // Skip if water was removed
            if (worldGenerator.GetBlockType(pos) != BlockType.Water)
            {
                waterLevels.Remove(pos);
                continue;
            }

            // Try to flow down first - water falls as far as it can
            Vector3Int checkPos = pos;
            bool fell = false;

            // Keep falling until hitting something solid
            for (int i = 0; i < 50; i++) // Max 50 blocks per tick
            {
                Vector3Int below = checkPos + Vector3Int.down;
                if (below.y < 0) break; // Hit bottom of world

                BlockType blockBelow = worldGenerator.GetBlockType(below);

                if (blockBelow == BlockType.Air)
                {
                    // Water can fall here
                    if (!waterLevels.ContainsKey(below))
                    {
                        waterLevels[below] = 0; // Falling water becomes source
                        worldGenerator.PlaceBlock(below, BlockType.Water, skipMeshUpdate: true);
                        updates++;
                        fell = true;

                        // Mark chunk as dirty
                        Vector2Int chunkCoord = new Vector2Int(
                            Mathf.FloorToInt(below.x / 16f),
                            Mathf.FloorToInt(below.z / 16f)
                        );
                        dirtyChunks.Add(chunkCoord);
                    }
                    checkPos = below; // Continue falling
                }
                else
                {
                    // Hit a solid block, stop falling
                    break;
                }

                if (updates >= maxUpdatesPerTick) break;
            }

            // If water fell, don't spread horizontally this tick
            if (fell) continue;

            // Spread horizontally only if not at max level
            if (level < maxFlowDistance)
            {
                byte nextLevel = (byte)(level + 1);

                // Try all 4 horizontal directions
                TrySpreadTo(pos + Vector3Int.right, nextLevel, ref updates, maxUpdatesPerTick);
                if (updates >= maxUpdatesPerTick) break;

                TrySpreadTo(pos + Vector3Int.left, nextLevel, ref updates, maxUpdatesPerTick);
                if (updates >= maxUpdatesPerTick) break;

                TrySpreadTo(pos + Vector3Int.forward, nextLevel, ref updates, maxUpdatesPerTick);
                if (updates >= maxUpdatesPerTick) break;

                TrySpreadTo(pos + Vector3Int.back, nextLevel, ref updates, maxUpdatesPerTick);
            }
        }

        isSpreading = false;

        // Update meshes periodically, not every tick
        ticksSinceLastMeshUpdate++;
        if (ticksSinceLastMeshUpdate >= meshUpdateInterval && dirtyChunks.Count > 0)
        {
            // Batch update all dirty chunks
            foreach (var chunkCoord in dirtyChunks)
            {
                worldGenerator.UpdateChunkMesh(chunkCoord);
            }
            dirtyChunks.Clear();
            ticksSinceLastMeshUpdate = 0;
        }
    }

    private void TrySpreadTo(Vector3Int targetPos, byte level, ref int updateCount, int maxUpdates)
    {
        if (updateCount >= maxUpdates) return;

        // Check bounds
        if (targetPos.y < 0 || targetPos.y >= worldGenerator.worldHeight) return;

        BlockType targetBlock = worldGenerator.GetBlockType(targetPos);

        // Can only spread to air
        if (targetBlock != BlockType.Air) return;

        // Check if water already exists
        if (waterLevels.ContainsKey(targetPos))
        {
            // Update if new level is better (lower = more water)
            if (level < waterLevels[targetPos])
            {
                waterLevels[targetPos] = level;
            }
            return;
        }

        // Place flowing water at this position
        waterLevels[targetPos] = level;
        worldGenerator.PlaceBlock(targetPos, BlockType.Water, skipMeshUpdate: true);
        updateCount++;

        // Mark chunk as dirty
        Vector2Int chunkCoord = new Vector2Int(
            Mathf.FloorToInt(targetPos.x / 16f),
            Mathf.FloorToInt(targetPos.z / 16f)
        );
        dirtyChunks.Add(chunkCoord);

        // If there's air below, water should fall immediately
        Vector3Int below = targetPos + Vector3Int.down;
        if (below.y >= 0)
        {
            BlockType blockBelow = worldGenerator.GetBlockType(below);

            if (blockBelow == BlockType.Air)
            {
                // Water falls down from this newly placed position
                Vector3Int checkPos = targetPos;
                for (int i = 0; i < 50; i++) // Max 50 blocks fall
                {
                    Vector3Int belowCheck = checkPos + Vector3Int.down;
                    if (belowCheck.y < 0) break;

                    BlockType belowType = worldGenerator.GetBlockType(belowCheck);
                    if (belowType == BlockType.Air && !waterLevels.ContainsKey(belowCheck))
                    {
                        waterLevels[belowCheck] = 0; // Falling water becomes source
                        worldGenerator.PlaceBlock(belowCheck, BlockType.Water, skipMeshUpdate: true);
                        updateCount++;
                        checkPos = belowCheck;
                    }
                    else
                    {
                        break;
                    }

                    if (updateCount >= maxUpdates) break;
                }
            }
        }
    }

    public void OnBlockPlaced(Vector3Int pos)
    {
        // Block placed might affect water flow - nothing to do for now
    }

    public void OnBlockRemoved(Vector3Int pos, BlockType removedType)
    {
        if (removedType == BlockType.Water)
        {
            waterLevels.Remove(pos);
        }
    }

    /// <summary>
    /// Get the water level at a position (0 = source/full, 1-7 = flowing)
    /// Returns 0 if no water data exists
    /// </summary>
    public byte GetWaterLevel(Vector3Int pos)
    {
        if (waterLevels.TryGetValue(pos, out byte level))
            return level;
        return 0; // Default to full if not tracked
    }

    /// <summary>
    /// Check if a position has water flow data
    /// </summary>
    public bool HasWaterData(Vector3Int pos)
    {
        return waterLevels.ContainsKey(pos);
    }
}