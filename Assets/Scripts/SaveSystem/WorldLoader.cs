using UnityEngine;
using UnityEngine.SceneManagement;

public class WorldLoader : MonoBehaviour
{
    public static WorldLoader Instance { get; private set; }
    
    [Header("References")]
    public WorldGenerator worldGenerator;
    public Transform playerTransform;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        WorldSaveManager.OnWorldLoaded += OnWorldLoaded;
    }
    
    private void Start()
    {
        Debug.Log($"WorldLoader: Start() called in scene '{SceneManager.GetActiveScene().name}' (Instance: {(Instance == this ? "THIS" : "OTHER")})");
        if (SceneManager.GetActiveScene().name == "World")
        {
            Debug.Log("WorldLoader: Scene is World, re-finding references and starting initialization");
            
            // Re-find references in the new scene (since this object persists across scenes)
            if (worldGenerator == null)
            {
                worldGenerator = FindObjectOfType<WorldGenerator>();
                Debug.Log($"WorldLoader: Found WorldGenerator: {(worldGenerator != null ? "YES" : "NO")}");
            }
            
            if (playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    Debug.Log($"WorldLoader: Found Player: YES");
                }
                else
                {
                    Debug.LogError("WorldLoader: Player not found!");
                }
            }
            
            StartCoroutine(InitializeWorldCoroutine());
        }
    }
    
    private System.Collections.IEnumerator InitializeWorldCoroutine()
    {
        // Wait a frame to ensure all scene objects are initialized
        yield return null;
        
        // Wait for WorldSaveManager to be ready
        int attempts = 0;
        while (WorldSaveManager.Instance == null && attempts < 50)
        {
            Debug.Log($"WorldLoader: Waiting for WorldSaveManager... attempt {attempts + 1}");
            yield return new WaitForSeconds(0.1f);
            attempts++;
        }
        
        if (WorldSaveManager.Instance != null)
        {
            Debug.Log("WorldLoader: WorldSaveManager found, checking for world data");
            
            // Wait for world data to be available
            WorldSaveData worldData = null;
            attempts = 0;
            while (worldData == null && attempts < 50)
            {
                worldData = WorldSaveManager.Instance.GetCurrentWorldData();
                if (worldData == null)
                {
                    Debug.Log($"WorldLoader: Waiting for world data... attempt {attempts + 1}");
                    yield return new WaitForSeconds(0.1f);
                    attempts++;
                }
            }
            
            if (worldData != null)
            {
                Debug.Log($"WorldLoader: World data found for '{worldData.worldName}', initializing world");
                InitializeWorld();
            }
            else
            {
                Debug.LogWarning("WorldLoader: No world data found, creating default world");
                CreateDefaultWorld();
            }
        }
        else
        {
            Debug.LogError("WorldLoader: WorldSaveManager not found after waiting!");
        }
    }
    
    private void OnWorldLoaded(WorldSaveData worldData)
    {
        Debug.Log($"World loaded event received: {worldData.worldName}");
    }
    
    private void InitializeWorld()
    {
        Debug.Log("WorldLoader: InitializeWorld() starting");
        if (WorldSaveManager.Instance == null) 
        {
            Debug.LogError("WorldLoader: WorldSaveManager.Instance is null!");
            return;
        }
        
        WorldSaveData currentWorld = WorldSaveManager.Instance.GetCurrentWorldData();
        Debug.Log($"WorldLoader: Current world data: {(currentWorld?.worldName ?? "null")} (seed: {currentWorld?.worldSeed ?? -1})");
        
        if (currentWorld != null)
        {
            Debug.Log($"WorldLoader: Found world data for '{currentWorld.worldName}', calling LoadWorldFromData");
            LoadWorldFromData(currentWorld);
        }
        else
        {
            Debug.LogWarning("WorldLoader: No current world data found! This means WorldSaveManager has no active world set");
            Debug.LogWarning("WorldLoader: This suggests the world data wasn't properly set before scene transition");
            CreateDefaultWorld();
        }
        
        Debug.Log("WorldLoader: InitializeWorld() completed");
    }
    
    private void LoadWorldFromData(WorldSaveData worldData)
    {
        Debug.Log($"WorldLoader: LoadWorldFromData() called for world '{worldData.worldName}'");
        if (worldGenerator == null)
        {
            Debug.Log("WorldLoader: worldGenerator is null, searching for WorldGenerator...");
            worldGenerator = FindObjectOfType<WorldGenerator>();
        }
        
        if (worldGenerator != null)
        {
            Debug.Log($"WorldLoader: Found WorldGenerator, calling InitializeForWorld('{worldData.worldName}')");
            // Initialize the world generator for this specific world
            worldGenerator.InitializeForWorld(worldData.worldName);
            worldGenerator.worldSeed = worldData.worldSeed;
            Debug.Log($"WorldLoader: Completed world initialization - world: {worldData.worldName}, seed: {worldData.worldSeed}");
            
            LoadPlayerPosition(worldData);
        }
        else
        {
            Debug.LogError("WorldLoader: WorldGenerator not found in scene!");
        }
    }
    
    private void LoadPlayerPosition(WorldSaveData worldData)
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
            }
        }
        
        if (playerTransform != null)
        {
            playerTransform.position = worldData.playerPosition;
            playerTransform.eulerAngles = worldData.playerRotation;
            Debug.Log($"WorldLoader: Player position loaded from world data: {worldData.playerPosition}, rotation: {worldData.playerRotation} for world '{worldData.worldName}'");
        }
        else
        {
            Debug.LogError($"WorldLoader: Player transform not found when loading position for world '{worldData.worldName}'");
        }
    }
    
    private void CreateDefaultWorld()
    {
        if (worldGenerator != null)
        {
            // Initialize for default world
            worldGenerator.InitializeForWorld("DefaultWorld");
            int randomSeed = Random.Range(int.MinValue, int.MaxValue);
            worldGenerator.worldSeed = randomSeed;
            
            // Position player at a safe starting height for new worlds
            if (playerTransform == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                }
            }
            
            if (playerTransform != null)
            {
                Vector3 safeStartPos = new Vector3(0, 150, 0); // Start at Y=150, will snap to ground later
                playerTransform.position = safeStartPos;
                Debug.Log($"WorldLoader: Positioned player at safe starting height {safeStartPos} for new world");
            }
            
            Debug.Log($"Created default world with seed: {randomSeed}");
        }
    }
    
    public void SaveCurrentWorldState()
    {
        if (WorldSaveManager.Instance == null) return;
        
        WorldSaveData currentWorld = WorldSaveManager.Instance.GetCurrentWorldData();
        if (currentWorld == null) return;
        
        SavePlayerPosition(currentWorld);
        
        // Save chunk modifications
        if (worldGenerator != null)
        {
            worldGenerator.SaveAllLoadedChunks();
        }
        
        WorldSaveManager.Instance.MarkWorldAsModified();
    }
    
    private void SavePlayerPosition(WorldSaveData worldData)
    {
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerTransform = player.transform;
            }
        }
        
        if (playerTransform != null)
        {
            worldData.playerPosition = playerTransform.position;
            worldData.playerRotation = playerTransform.eulerAngles;
        }
    }
    
    private void OnDestroy()
    {
        WorldSaveManager.OnWorldLoaded -= OnWorldLoaded;
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
        {
            SaveCurrentWorldState();
        }
    }
    
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SaveCurrentWorldState();
        }
    }
}