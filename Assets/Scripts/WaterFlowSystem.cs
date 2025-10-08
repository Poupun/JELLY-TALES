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
    [Range(0.1f, 5f)]
    public float waterTickRate = 0.2f;

    [Tooltip("Enable water flow simulation")]
    public bool enableWaterFlow = true;

    [Tooltip("Max water blocks to process per tick")]
    [Range(1, 50)]
    public int maxBlocksToProcessPerTick = 5;

    [Tooltip("How often to update chunk meshes (every N ticks)")]
    [Range(1, 20)]
    public int meshUpdateInterval = 2;

    // Water level storage: position -> level (0 = source, 1-7 = flowing)
    private Dictionary<Vector3Int, byte> waterLevels = new Dictionary<Vector3Int, byte>();

    // Queue of water blocks that need to be processed
    private Queue<Vector3Int> waterQueue = new Queue<Vector3Int>();

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
        waterQueue.Enqueue(pos);
        Debug.Log($"WaterFlowSystem: Registered water source at {pos}");
    }

    /// <summary>
    /// Process all water updates
    /// </summary>
    public void ProcessWaterUpdates()
    {
        if (waterQueue.Count == 0) return;

        int blocksProcessed = 0;

        isSpreading = true;

        while (waterQueue.Count > 0 && blocksProcessed < maxBlocksToProcessPerTick)
        {
            Vector3Int pos = waterQueue.Dequeue();
            blocksProcessed++;

            // Skip if water no longer exists at this position
            if (!waterLevels.TryGetValue(pos, out byte level))
            {
                continue;
            }

            // Verify block is still water
            if (worldGenerator.GetBlockType(pos) != BlockType.Water)
            {
                waterLevels.Remove(pos);
                continue;
            }

            // Try to flow down first - water falls ONE block per tick for gradual flow
            Vector3Int below = pos + Vector3Int.down;
            bool fell = false;

            if (below.y >= 0)
            {
                // Check if chunk is loaded before flowing down
                if (!worldGenerator.IsChunkLoadedAt(below))
                {
                    Debug.Log($"WaterFlowSystem: Chunk not loaded below {below}, skipping fall");
                    continue;
                }

                BlockType blockBelow = worldGenerator.GetBlockType(below);

                // Only fall through Air or Water, NOT leaves or other blocks
                if (blockBelow == BlockType.Air || blockBelow == BlockType.Water)
                {
                    // Water can fall here (only if it's truly air, not water)
                    if (blockBelow == BlockType.Air && !waterLevels.ContainsKey(below))
                    {
                        // Double-check it's truly air before placing
                        BlockType doubleCheck = worldGenerator.GetBlockType(below);
                        if (doubleCheck != BlockType.Air)
                        {
                            Debug.LogWarning($"WaterFlowSystem: BLOCKED falling to {below} - was Air, now {doubleCheck}!");
                            continue;
                        }

                        Debug.Log($"WaterFlowSystem: Water falling from {pos} to {below}");
                        waterLevels[below] = 0; // Falling water becomes source
                        waterQueue.Enqueue(below); // Add to queue for processing
                        worldGenerator.PlaceBlock(below, BlockType.Water, skipMeshUpdate: true);
                        fell = true;

                        // Mark chunk as dirty
                        Vector2Int chunkCoord = new Vector2Int(
                            Mathf.FloorToInt(below.x / 16f),
                            Mathf.FloorToInt(below.z / 16f)
                        );
                        dirtyChunks.Add(chunkCoord);
                    }
                }
            }

            // If water fell, don't spread horizontally this tick
            if (fell) continue;

            // Spread horizontally only if not at max level
            if (level < maxFlowDistance)
            {
                byte nextLevel = (byte)(level + 1);

                // Try all 4 horizontal directions
                TrySpreadTo(pos + Vector3Int.right, nextLevel);
                TrySpreadTo(pos + Vector3Int.left, nextLevel);
                TrySpreadTo(pos + Vector3Int.forward, nextLevel);
                TrySpreadTo(pos + Vector3Int.back, nextLevel);
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

    private void TrySpreadTo(Vector3Int targetPos, byte level)
    {
        // Check bounds
        if (targetPos.y < 0 || targetPos.y >= worldGenerator.worldHeight) return;

        // CRITICAL: Check if chunk is loaded FIRST
        // If chunk isn't loaded, we don't know if there's a tree there
        // DON'T spread to unloaded chunks to prevent destroying trees
        if (!worldGenerator.IsChunkLoadedAt(targetPos))
        {
            Debug.Log($"WaterFlowSystem: Chunk not loaded at {targetPos}, skipping water spread (may have trees)");
            return;
        }

        BlockType targetBlock = worldGenerator.GetBlockType(targetPos);

        // Can only spread to air (not water, not leaves, not anything else)
        if (targetBlock != BlockType.Air)
        {
            if (targetBlock == BlockType.Leaves)
            {
                Debug.LogError($"WaterFlowSystem: BLOCKED! Tried to spread to LEAVES at {targetPos}!");
            }
            else
            {
                Debug.Log($"WaterFlowSystem: Blocked spreading to {targetPos} - block is: {targetBlock}");
            }
            return;
        }

        Debug.Log($"WaterFlowSystem: Target {targetPos} is Air, will spread there");

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

        // Double-check it's still air before placing
        BlockType finalCheck = worldGenerator.GetBlockType(targetPos);
        if (finalCheck != BlockType.Air)
        {
            Debug.LogWarning($"WaterFlowSystem: BLOCKED spreading to {targetPos} - target changed to {finalCheck}!");
            return;
        }

        // Place flowing water at this position
        Debug.Log($"WaterFlowSystem: Water spreading to {targetPos} at level {level}");
        waterLevels[targetPos] = level;
        waterQueue.Enqueue(targetPos); // Add to queue for processing
        worldGenerator.PlaceBlock(targetPos, BlockType.Water, skipMeshUpdate: true);

        // Mark chunk as dirty
        Vector2Int chunkCoord = new Vector2Int(
            Mathf.FloorToInt(targetPos.x / 16f),
            Mathf.FloorToInt(targetPos.z / 16f)
        );
        dirtyChunks.Add(chunkCoord);

        // Water will fall on the next tick (gradual falling instead of instant cascade)
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