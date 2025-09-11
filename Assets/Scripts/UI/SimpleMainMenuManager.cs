using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

public class SimpleMainMenuManager : MonoBehaviour
{
    [Header("Window GameObjects")]
    public GameObject mainMenuWindow;
    public GameObject worldSelectionWindow;
    public GameObject newWorldWindow;
    
    [Header("Main Menu UI")]
    public Button playButton;
    public Button exitButton;
    
    [Header("World Selection UI")]
    public Button newWorldButton;
    public Button worldSelectionBackButton;
    public Transform worldListParent;
    public GameObject worldItemPrefab;
    
    [Header("New World UI")]
    public TMP_InputField worldNameInput;
    public TMP_InputField worldSeedInput;
    public Button createButton;
    public Button newWorldBackButton;
    
    [Header("Delete Confirmation UI")]
    public GameObject deleteConfirmationPopup;
    public Button yesButton;
    public Button noButton;
    public Text messageText;
    
    [Header("Settings")]
    public string worldSceneName = "World";
    
    private List<GameObject> spawnedWorldItems = new List<GameObject>();
    private WorldSaveData worldToDelete;
    
    private void Start()
    {
        InitializeUI();
    }
    
    private void InitializeUI()
    {
        Debug.Log("Initializing UI...");
        
        if (playButton) 
        {
            playButton.onClick.AddListener(() => ShowWindow(worldSelectionWindow));
            Debug.Log("Play button listener added");
        }
        else Debug.LogWarning("Play button is null!");
        
        if (exitButton) 
        {
            exitButton.onClick.AddListener(ExitApplication);
            Debug.Log("Exit button listener added");
        }
        else Debug.LogWarning("Exit button is null!");
        
        if (newWorldButton) 
        {
            newWorldButton.onClick.AddListener(() => ShowWindow(newWorldWindow));
            Debug.Log("New World button listener added");
        }
        else Debug.LogWarning("New World button is null!");
        
        if (worldSelectionBackButton) 
        {
            worldSelectionBackButton.onClick.AddListener(() => ShowWindow(mainMenuWindow));
            Debug.Log("World Selection back button listener added");
        }
        else Debug.LogWarning("World Selection back button is null!");
        
        if (createButton) 
        {
            createButton.onClick.AddListener(CreateNewWorld);
            Debug.Log("Create button listener added");
        }
        else Debug.LogWarning("Create button is null!");
        
        if (newWorldBackButton) 
        {
            newWorldBackButton.onClick.AddListener(() => ShowWindow(worldSelectionWindow));
            Debug.Log("New World back button listener added");
        }
        else Debug.LogWarning("New World back button is null!");
        
        if (yesButton) 
        {
            yesButton.onClick.AddListener(ConfirmDeleteWorld);
            Debug.Log("Yes button listener added");
        }
        else Debug.LogWarning("Yes button is null!");
        
        if (noButton) 
        {
            noButton.onClick.AddListener(CancelDeleteWorld);
            Debug.Log("No button listener added");
        }
        else Debug.LogWarning("No button is null!");
        
        // Initialize WorldSaveManager at startup
        EnsureWorldSaveManagerExists();
        
        // Clean up old default chunk data to prevent contamination
        if (WorldSaveManager.Instance != null)
        {
            WorldSaveManager.Instance.ClearDefaultChunkData();
        }
        
        ShowWindow(mainMenuWindow);
        
        if (worldSeedInput)
        {
            worldSeedInput.text = Random.Range(-2000000000, 2000000000).ToString();
        }
        else Debug.LogWarning("World seed input is null!");
    }
    
    private void ShowWindow(GameObject windowToShow)
    {
        if (mainMenuWindow) mainMenuWindow.SetActive(windowToShow == mainMenuWindow);
        if (worldSelectionWindow) worldSelectionWindow.SetActive(windowToShow == worldSelectionWindow);
        if (newWorldWindow) newWorldWindow.SetActive(windowToShow == newWorldWindow);
        
        if (windowToShow == worldSelectionWindow)
        {
            EnsureWorldSaveManagerExists();
            RefreshWorldList();
        }
        
        if (windowToShow == newWorldWindow)
        {
            if (worldSeedInput)
            {
                worldSeedInput.text = Random.Range(-2000000000, 2000000000).ToString();
            }
        }
    }
    
    private void EnsureWorldSaveManagerExists()
    {
        if (WorldSaveManager.Instance == null)
        {
            Debug.Log("WorldSaveManager.Instance is null, creating one...");
            GameObject saveManagerObject = new GameObject("WorldSaveManager");
            saveManagerObject.AddComponent<WorldSaveManager>();
        }
    }
    
    private void RefreshWorldList()
    {
        ClearWorldList();
        
        EnsureWorldSaveManagerExists();
        
        if (WorldSaveManager.Instance != null)
        {
            List<WorldSaveData> worlds = WorldSaveManager.Instance.GetAllWorldSaves();
            Debug.Log($"Found {worlds.Count} world saves to display");
            
            foreach (WorldSaveData world in worlds)
            {
                CreateWorldListItem(world);
            }
            
            // Scroll to top after adding items
            ScrollRect scrollRect = worldListParent?.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 1f; // 1 = top, 0 = bottom
            }
        }
        else
        {
            Debug.LogError("WorldSaveManager.Instance is still null after creation attempt!");
        }
    }
    
    private void ClearWorldList()
    {
        foreach (GameObject item in spawnedWorldItems)
        {
            if (item != null) Destroy(item);
        }
        spawnedWorldItems.Clear();
    }
    
    private void CreateWorldListItem(WorldSaveData worldData)
    {
        if (!worldItemPrefab || !worldListParent) return;
        
        GameObject item = Instantiate(worldItemPrefab, worldListParent);
        spawnedWorldItems.Add(item);
        
        Button button = item.GetComponent<Button>();
        if (button)
        {
            button.onClick.AddListener(() => LoadWorld(worldData));
        }
        
        Button[] allButtons = item.GetComponentsInChildren<Button>();
        foreach (Button btn in allButtons)
        {
            if (btn != button && btn.name == "Delete Button")
            {
                btn.onClick.AddListener(() => ShowDeleteConfirmation(worldData));
                break;
            }
        }
        
        TMP_Text[] texts = item.GetComponentsInChildren<TMP_Text>();
        if (texts.Length > 0)
        {
            texts[0].text = worldData.worldName;
            if (texts.Length > 1)
            {
                texts[1].text = $"Created: {worldData.GetFormattedCreatedDateTime()}\nSeed: {worldData.worldSeed}\nPlaytime: {worldData.GetFormattedPlaytime()}";
            }
        }
    }
    
    private void LoadWorld(WorldSaveData worldData)
    {
        Debug.Log($"SimpleMainMenuManager: LoadWorld called for '{worldData.worldName}'");
        
        // Ensure WorldSceneManager exists
        EnsureWorldSceneManagerExists();
        
        if (WorldSceneManager.Instance != null)
        {
            Debug.Log($"SimpleMainMenuManager: Using WorldSceneManager to load world '{worldData.worldName}'");
            WorldSceneManager.Instance.LoadWorldScene(worldData);
        }
        else
        {
            Debug.LogWarning("SimpleMainMenuManager: WorldSceneManager not available, falling back to direct scene load");
            if (WorldSaveManager.Instance != null)
            {
                WorldSaveManager.Instance.LoadWorld(worldData);
                SceneManager.LoadScene(worldSceneName);
            }
            else
            {
                Debug.LogError("SimpleMainMenuManager: WorldSaveManager.Instance is also null!");
            }
        }
    }
    
    private void EnsureWorldSceneManagerExists()
    {
        if (WorldSceneManager.Instance == null)
        {
            Debug.Log("SimpleMainMenuManager: Creating WorldSceneManager");
            GameObject sceneManager = new GameObject("WorldSceneManager");
            sceneManager.AddComponent<WorldSceneManager>();
        }
    }
    
    private void CreateNewWorld()
    {
        Debug.Log("CreateNewWorld called!");
        
        // Try to find the input fields dynamically if references are null
        if (worldNameInput == null)
        {
            worldNameInput = GameObject.Find("World Name")?.GetComponent<TMP_InputField>();
        }
        if (worldSeedInput == null)
        {
            worldSeedInput = GameObject.Find("Seed Name")?.GetComponent<TMP_InputField>();
        }
        
        string worldName = worldNameInput ? worldNameInput.text : "New World";
        string seedText = worldSeedInput ? worldSeedInput.text : "0";
        
        Debug.Log($"World Name: '{worldName}', Seed Text: '{seedText}'");
        
        if (string.IsNullOrEmpty(worldName.Trim()))
        {
            worldName = "New World";
        }
        
        int seed = 0;
        if (string.IsNullOrEmpty(seedText) || !int.TryParse(seedText, out seed))
        {
            seed = Random.Range(-2000000000, 2000000000);
        }
        
        Debug.Log($"Final World Name: '{worldName.Trim()}', Final Seed: {seed}");
        
        // Ensure WorldSaveManager exists
        EnsureWorldSaveManagerExists();
        
        if (WorldSaveManager.Instance != null)
        {
            Debug.Log("Creating new world...");
            WorldSaveData newWorld = WorldSaveManager.Instance.CreateNewWorld(worldName.Trim(), seed);
            if (newWorld != null)
            {
                Debug.Log($"New world created successfully: {newWorld.worldName}");
                LoadWorld(newWorld);
            }
            else
            {
                Debug.LogError("Failed to create new world!");
            }
        }
        else
        {
            Debug.LogError("WorldSaveManager.Instance is still null!");
        }
    }
    
    private void ExitApplication()
    {
        Application.Quit();
    }
    
    private void ShowDeleteConfirmation(WorldSaveData worldData)
    {
        worldToDelete = worldData;
        if (messageText)
        {
            messageText.text = $"Are you sure you want to delete '{worldData.worldName}'? This action cannot be undone.";
        }
        
        if (deleteConfirmationPopup)
        {
            deleteConfirmationPopup.SetActive(true);
        }
    }
    
    private void ConfirmDeleteWorld()
    {
        if (worldToDelete != null && WorldSaveManager.Instance != null)
        {
            bool success = WorldSaveManager.Instance.DeleteWorld(worldToDelete.worldName);
            if (success)
            {
                Debug.Log($"World '{worldToDelete.worldName}' deleted successfully");
                RefreshWorldList();
            }
            else
            {
                Debug.LogError($"Failed to delete world '{worldToDelete.worldName}'");
            }
        }
        
        CancelDeleteWorld();
    }
    
    private void CancelDeleteWorld()
    {
        worldToDelete = null;
        if (deleteConfirmationPopup)
        {
            deleteConfirmationPopup.SetActive(false);
        }
    }
}