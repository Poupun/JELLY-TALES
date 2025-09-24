using UnityEngine;

public enum BiomeType
{
    Plains,
    Ocean,
    Forest,    // For future expansion
    Desert,    // For future expansion
    Mountains  // For future expansion
}

[System.Serializable]
public class BiomeData
{
    public BiomeType type;
    public string displayName;
    public Color mapColor;

    // Terrain generation parameters
    public int baseElevation = 100;
    public float terrainScale = 0.008f;
    public float terrainAmplitude = 15f;
    public float hillThreshold = 0.3f;
    public float hillMultiplier = 2f;

    // Ocean specific parameters
    public int waterLevel = 62;  // Sea level like Minecraft
    public int maxDepth = 40;    // How deep oceans go
    public bool generateUnderwater = true;

    // Block generation settings
    public BlockType surfaceBlock = BlockType.Grass;
    public BlockType subSurfaceBlock = BlockType.Dirt;
    public BlockType deepBlock = BlockType.Stone;
    public BlockType fluidBlock = BlockType.Air;

    // Vegetation settings
    public bool enableTrees = true;
    public float treeDensity = 0.14f;
    public bool enablePlants = true;
    public float plantDensity = 0.15f;

    public BiomeData(BiomeType biomeType, string name, Color color)
    {
        type = biomeType;
        displayName = name;
        mapColor = color;
    }
}

public static class BiomeRegistry
{
    public static readonly BiomeData[] BIOMES = new BiomeData[]
    {
        // Plains biome (existing default)
        new BiomeData(BiomeType.Plains, "Plains", Color.green)
        {
            baseElevation = 100,
            terrainScale = 0.008f,
            terrainAmplitude = 15f,
            hillThreshold = 0.3f,
            hillMultiplier = 2f,
            surfaceBlock = BlockType.Grass,
            subSurfaceBlock = BlockType.Dirt,
            deepBlock = BlockType.Stone,
            enableTrees = true,
            treeDensity = 0.14f,
            enablePlants = true,
            plantDensity = 0.15f
        },

        // Ocean biome (new)
        new BiomeData(BiomeType.Ocean, "Ocean", Color.blue)
        {
            baseElevation = 35,      // Lower base elevation - well below sea level
            terrainScale = 0.007f,   // Increased scale for more terrain variation
            terrainAmplitude = 20f,  // Increased amplitude for more varied ocean floor
            hillThreshold = 0.3f,    // More frequent underwater hills
            hillMultiplier = 1.8f,   // More pronounced underwater hills
            waterLevel = 95,         // Sea level - closer to normal terrain
            maxDepth = 40,           // Deep oceans
            generateUnderwater = true,
            surfaceBlock = BlockType.Sand,  // Ocean floor is sand
            subSurfaceBlock = BlockType.Sand,
            deepBlock = BlockType.Stone,
            fluidBlock = BlockType.Water,
            enableTrees = false,     // No trees in oceans
            treeDensity = 0f,
            enablePlants = false,    // No land plants in oceans (kelp could be added later)
            plantDensity = 0f
        }
    };

    public static BiomeData GetBiome(BiomeType type)
    {
        foreach (var biome in BIOMES)
        {
            if (biome.type == type) return biome;
        }
        return BIOMES[0]; // Default to Plains
    }
}