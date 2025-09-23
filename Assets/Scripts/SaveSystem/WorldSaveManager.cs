using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class WorldSaveManager : MonoBehaviour
{
    public static WorldSaveManager Instance { get; private set; }
    
    [Header("Save Settings")]
    public string savesFolder = "WorldSaves";
    public bool autoSaveEnabled = true;
    public float autoSaveIntervalMinutes = 5f;
    
    private string SavesPath => Path.Combine(Application.persistentDataPath, savesFolder);
    private float lastAutoSaveTime = 0f;
    private WorldSaveData currentWorldData;
    private bool hasUnsavedChanges = false;
    
    public static event Action<List<WorldSaveData>> OnWorldListUpdated;
    public static event Action<WorldSaveData> OnWorldLoaded;
    public static event Action OnWorldSaved;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeSaveSystem();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void Update()
    {
        if (autoSaveEnabled && currentWorldData != null && hasUnsavedChanges)
        {
            if (Time.time - lastAutoSaveTime >= autoSaveIntervalMinutes * 60f)
            {
                AutoSave();
            }
        }
    }
    
    private void InitializeSaveSystem()
    {
        if (!Directory.Exists(SavesPath))
        {
            Directory.CreateDirectory(SavesPath);
            Debug.Log($"Created saves directory at: {SavesPath}");
        }
    }
    
    public List<WorldSaveData> GetAllWorldSaves()
    {
        List<WorldSaveData> worldSaves = new List<WorldSaveData>();
        
        try
        {
            string[] saveFiles = Directory.GetFiles(SavesPath, "*.json");
            
            foreach (string filePath in saveFiles)
            {
                WorldSaveData worldData = LoadWorldData(Path.GetFileNameWithoutExtension(filePath));
                if (worldData != null)
                {
                    worldSaves.Add(worldData);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading world saves: {e.Message}");
        }
        
        worldSaves.Sort((a, b) => b.lastPlayedDate.CompareTo(a.lastPlayedDate));
        
        return worldSaves;
    }
    
    public WorldSaveData CreateNewWorld(string worldName, int worldSeed)
    {
        string safeName = GetSafeWorldName(worldName);
        WorldSaveData newWorld = new WorldSaveData(safeName, worldSeed);
        SaveWorldData(newWorld);
        Debug.Log($"Created new world: {safeName} with seed: {worldSeed}");
        return newWorld;
    }
    
    private string GetSafeWorldName(string originalName)
    {
        string safeName = originalName;
        string filePath = Path.Combine(SavesPath, safeName + ".json");
        int counter = 1;
        
        while (File.Exists(filePath))
        {
            safeName = $"{originalName} ({counter})";
            filePath = Path.Combine(SavesPath, safeName + ".json");
            counter++;
        }
        
        return safeName;
    }
    
    public bool SaveWorldData(WorldSaveData worldData)
    {
        try
        {
            string filePath = Path.Combine(SavesPath, worldData.worldName + ".json");
            worldData.lastPlayedDate = DateTime.Now;
            string json = JsonUtility.ToJson(worldData, true);
            File.WriteAllText(filePath, json);
            currentWorldData = worldData;
            hasUnsavedChanges = false;
            lastAutoSaveTime = Time.time;
            Debug.Log($"Saved world: {worldData.worldName}");
            OnWorldSaved?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save world {worldData.worldName}: {e.Message}");
            return false;
        }
    }
    
    public WorldSaveData LoadWorldData(string worldName)
    {
        try
        {
            string filePath = Path.Combine(SavesPath, worldName + ".json");
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"Save file not found: {filePath}");
                return null;
            }
            string json = File.ReadAllText(filePath);
            WorldSaveData worldData = JsonUtility.FromJson<WorldSaveData>(json);
            return worldData;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load world {worldName}: {e.Message}");
            return null;
        }
    }
    
    public void LoadWorld(WorldSaveData worldData)
    {
        currentWorldData = worldData;
        hasUnsavedChanges = false;
        currentWorldData.lastPlayedDate = DateTime.Now;
        OnWorldLoaded?.Invoke(worldData);
        Debug.Log($"Loading world: {worldData.worldName} (Seed: {worldData.worldSeed})");
    }
    
    public void MarkWorldAsModified()
    {
        hasUnsavedChanges = true;
    }
    
    private void AutoSave()
    {
        if (currentWorldData != null)
        {
            SaveWorldData(currentWorldData);
            Debug.Log("Auto-saved world");
        }
    }
    
    public WorldSaveData GetCurrentWorldData()
    {
        return currentWorldData;
    }
    
    public bool DeleteWorld(string worldName)
    {
        try
        {
            string filePath = Path.Combine(SavesPath, worldName + ".json");
            
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                
                // Also clean up chunk data for this world
                ClearWorldChunkData(worldName);
                
                Debug.Log($"Deleted world: {worldName}");
                return true;
            }
            
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to delete world {worldName}: {e.Message}");
            return false;
        }
    }
    
    // Clean up chunk data for a specific world (useful for resetting worlds)
    public bool ClearWorldChunkData(string worldName)
    {
        try
        {
            string chunkPath = Path.Combine(Application.persistentDataPath, "ChunkSaves", worldName);
            
            if (Directory.Exists(chunkPath))
            {
                Directory.Delete(chunkPath, true);
                Debug.Log($"Cleared chunk data for world: {worldName}");
                return true;
            }
            
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to clear chunk data for world {worldName}: {e.Message}");
            return false;
        }
    }
    
    // Debug method to clean up the old default chunk folder
    public bool ClearDefaultChunkData()
    {
        try
        {
            string defaultChunkPath = Path.Combine(Application.persistentDataPath, "ChunkSaves");
            
            if (Directory.Exists(defaultChunkPath))
            {
                // Only delete files directly in ChunkSaves, not subfolders
                string[] files = Directory.GetFiles(defaultChunkPath, "*.json");
                foreach (string file in files)
                {
                    File.Delete(file);
                }
                Debug.Log($"Cleared {files.Length} old chunk files from default folder");
                return true;
            }
            
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to clear default chunk data: {e.Message}");
            return false;
        }
    }
}