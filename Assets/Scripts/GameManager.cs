using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    
    [Header("References")]
    public WorldGenerator worldGenerator;
    public Transform playerSpawn;
    
    [Header("Settings")]
    public string mainMenuSceneName = "MainMenu";
    public bool autoSaveEnabled = true;
    public float autoSaveInterval = 300f; // 5 minutes
    
    private float lastSaveTime;
    private bool gameInitialized = false;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            InitializeGame();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        Debug.Log($"GameManager: Start() called, gameInitialized: {gameInitialized}");
        if (!gameInitialized)
        {
            Debug.Log("GameManager: Calling InitializeGame()");
            InitializeGame();
        }
    }
    
    private void Update()
    {
        if (autoSaveEnabled && Time.time - lastSaveTime >= autoSaveInterval)
        {
            SaveGame();
        }
        
        // Don't handle escape key here anymore, let EscapeMenuManager handle it
        // The escape menu system will call SaveGame() and ReturnToMainMenu() as needed
    }
    
    private void InitializeGame()
    {
        if (gameInitialized) return;
        
        SetupSaveSystem();
        LoadWorldData();
        gameInitialized = true;
    }
    
    private void SetupSaveSystem()
    {
        if (WorldSaveManager.Instance == null)
        {
            GameObject saveManager = new GameObject("WorldSaveManager");
            saveManager.AddComponent<WorldSaveManager>();
            DontDestroyOnLoad(saveManager);
        }
    }
    
    private void LoadWorldData()
    {
        if (WorldSaveManager.Instance == null) return;
        
        WorldSaveData currentWorld = WorldSaveManager.Instance.GetCurrentWorldData();
        
        if (currentWorld != null)
        {
            ApplyWorldSettings(currentWorld);
        }
        else
        {
            Debug.LogWarning("No world data found, using default settings");
            CreateDefaultWorld();
        }
    }
    
    private void ApplyWorldSettings(WorldSaveData worldData)
    {
        if (worldGenerator == null)
        {
            worldGenerator = FindObjectOfType<WorldGenerator>();
        }
        
        if (worldGenerator != null)
        {
            // Only set seed, WorldLoader handles the initialization
            worldGenerator.worldSeed = worldData.worldSeed;
            Debug.Log($"Applied world seed: {worldData.worldSeed} for world: {worldData.worldName}");
            
            // WORLD INITIALIZATION: Ensure world name is properly set through fallback system
            StartCoroutine(EnsureWorldNameIsSet(worldData.worldName));
        }
        
        LoadPlayerPosition(worldData);
    }
    
    private System.Collections.IEnumerator EnsureWorldNameIsSet(string worldName)
    {
        // Wait a moment for WorldLoader to complete its initialization
        yield return new WaitForSeconds(1.0f);
        
        if (worldGenerator != null && string.IsNullOrEmpty(worldGenerator.currentWorldName))
        {
            Debug.Log($"GameManager: Fallback world initialization - setting world name to '{worldName}' (WorldLoader initialization incomplete)");
            worldGenerator.InitializeForWorld(worldName);
        }
        else if (worldGenerator != null)
        {
            Debug.Log($"GameManager: World name properly set to '{worldGenerator.currentWorldName}' - primary initialization successful");
        }
    }
    
    private void LoadPlayerPosition(WorldSaveData worldData)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            if (worldData.playerPosition != Vector3.zero)
            {
                player.transform.position = worldData.playerPosition;
                player.transform.eulerAngles = worldData.playerRotation;
                Debug.Log($"GameManager: Player position loaded from world data: {worldData.playerPosition}, rotation: {worldData.playerRotation} for world '{worldData.worldName}'");
            }
            else if (playerSpawn != null)
            {
                player.transform.position = playerSpawn.position;
                player.transform.rotation = playerSpawn.rotation;
                Debug.Log($"GameManager: Player position set to spawn point: {playerSpawn.position} for world '{worldData.worldName}' (no saved position found)");
            }
            else
            {
                Debug.LogWarning($"GameManager: No saved position and no spawn point for world '{worldData.worldName}'");
            }
        }
        else
        {
            Debug.LogError($"GameManager: Player not found when loading position for world '{worldData.worldName}'");
        }
    }
    
    private void CreateDefaultWorld()
    {
        if (worldGenerator != null)
        {
            int randomSeed = Random.Range(-2000000000, 2000000000);
            
            if (WorldSaveManager.Instance != null)
            {
                WorldSaveData defaultWorld = WorldSaveManager.Instance.CreateNewWorld("Default World", randomSeed);
                WorldSaveManager.Instance.LoadWorld(defaultWorld);
                
                // Initialize the world generator for this specific world
                worldGenerator.InitializeForWorld(defaultWorld.worldName);
                worldGenerator.worldSeed = randomSeed;
                
                // Position player at a safe starting height for new worlds
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    Vector3 safeStartPos = new Vector3(0, 150, 0); // Start at Y=150, will snap to ground later
                    player.transform.position = safeStartPos;
                    Debug.Log($"GameManager: Positioned player at safe starting height {safeStartPos} for new world");
                }
            }
            
            Debug.Log($"Created default world with seed: {randomSeed}");
        }
    }
    
    public void SaveGame()
    {
        Debug.Log("GameManager: SaveGame called");
        if (WorldSaveManager.Instance == null) 
        {
            Debug.LogError("GameManager: WorldSaveManager.Instance is null");
            return;
        }
        
        WorldSaveData currentWorld = WorldSaveManager.Instance.GetCurrentWorldData();
        if (currentWorld == null) 
        {
            Debug.LogError("GameManager: No current world data found");
            return;
        }
        
        Debug.Log($"GameManager: Saving game for world '{currentWorld.worldName}'");
        SavePlayerData(currentWorld);
        
        // Save chunk modifications (only if we're in a world scene)
        if (worldGenerator != null)
        {
            Debug.Log("GameManager: Calling SaveAllLoadedChunks");
            worldGenerator.SaveAllLoadedChunks();
        }
        else
        {
            Debug.Log("GameManager: No worldGenerator found (likely in MainMenu scene) - skipping chunk save");
        }
        
        WorldSaveManager.Instance.SaveWorldData(currentWorld);
        
        lastSaveTime = Time.time;
        Debug.Log($"GameManager: Game saved successfully for world '{currentWorld.worldName}'");
    }
    
    private void SavePlayerData(WorldSaveData worldData)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            worldData.playerPosition = player.transform.position;
            worldData.playerRotation = player.transform.eulerAngles;
            Debug.Log($"GameManager: Saved player position {worldData.playerPosition} and rotation {worldData.playerRotation} for world '{worldData.worldName}'");
        }
        else
        {
            Debug.LogWarning("GameManager: Player not found when trying to save player data");
        }
        
        worldData.playtimeMinutes += Time.deltaTime / 60f;
    }
    
    public void ReturnToMainMenu()
    {
        SaveGame();
        SceneManager.LoadScene(mainMenuSceneName);
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
        {
            SaveGame();
        }
    }
    
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SaveGame();
        }
    }
    
    private void OnDestroy()
    {
        if (Instance == this && SceneManager.GetActiveScene().name == "World" && GameObject.FindGameObjectWithTag("Player") != null)
        {
            SaveGame();
        }
    }
}