using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGeneration.Chunks;

[System.Serializable]
public class WorldSaveData
{
    public string worldName;
    public int worldSeed;
    [SerializeField] private string createdDateString;
    [SerializeField] private string lastPlayedDateString;
    public float playtimeMinutes;
    
    public DateTime createdDate 
    {
        get 
        { 
            if (string.IsNullOrEmpty(createdDateString))
                return DateTime.Now;
            try
            {
                return DateTime.Parse(createdDateString);
            }
            catch
            {
                return DateTime.Now;
            }
        }
        set { createdDateString = value.ToString("O"); }
    }
    
    public DateTime lastPlayedDate 
    {
        get 
        { 
            if (string.IsNullOrEmpty(lastPlayedDateString))
                return DateTime.Now;
            try
            {
                return DateTime.Parse(lastPlayedDateString);
            }
            catch
            {
                return DateTime.Now;
            }
        }
        set { lastPlayedDateString = value.ToString("O"); }
    }
    public Vector3 playerPosition;
    public Vector3 playerRotation;
    
    public string worldVersion = "1.0.0";
    
    public Dictionary<Vector2Int, ChunkSaveData> chunkData = new Dictionary<Vector2Int, ChunkSaveData>();
    public PlayerInventoryData playerInventoryData = new PlayerInventoryData();
    
    public WorldSaveData()
    {
        // Only set creation date if it's not already set (for new worlds)
        if (string.IsNullOrEmpty(createdDateString))
        {
            createdDate = DateTime.Now;
        }
        lastPlayedDate = DateTime.Now;
        playtimeMinutes = 0f;
        playerPosition = Vector3.zero;
        playerRotation = Vector3.zero;
    }
    
    public WorldSaveData(string name, int seed)
    {
        worldName = name;
        worldSeed = seed;
        createdDate = DateTime.Now;  // Always set for new worlds
        lastPlayedDate = DateTime.Now;
        playtimeMinutes = 0f;
        playerPosition = new Vector3(0, 100, 0); // Start high up
        playerRotation = Vector3.zero;
    }
    
    public string GetFormattedCreatedDate()
    {
        return createdDate.ToString("dd/MM/yyyy");
    }
    
    public string GetFormattedCreatedDateTime()
    {
        return createdDate.ToString("dd/MM/yyyy HH:mm");
    }
    
    public string GetFormattedLastPlayedDate()
    {
        return lastPlayedDate.ToString("dd/MM/yyyy HH:mm");
    }
    
    public string GetFormattedPlaytime()
    {
        int hours = Mathf.FloorToInt(playtimeMinutes / 60f);
        int minutes = Mathf.FloorToInt(playtimeMinutes % 60f);
        
        if (hours > 0)
            return $"{hours}h {minutes}m";
        else
            return $"{minutes}m";
    }
}