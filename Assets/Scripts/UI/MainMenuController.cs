using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

public class MainMenuController : MonoBehaviour
{
    [Header("Window References")]
    public GameObject mainMenuWindow;
    public GameObject worldSelectionWindow;
    public GameObject newWorldWindow;
    
    [Header("Main Menu Buttons")]
    public Button playButton;
    public Button exitButton;
    
    [Header("World Selection Elements")]
    public Button newWorldButton;
    public Button worldSelectionExitButton;
    public Transform worldListContent;
    public GameObject worldItemPrefab;
    
    [Header("New World Elements")]
    public TMP_InputField worldNameInput;
    public TMP_InputField worldSeedInput;
    public Button createWorldButton;
    public Button newWorldExitButton;
    
    [Header("Game Settings")]
    public string gameSceneName = "World";
    
    private List<WorldSaveData> availableWorlds;
    private List<GameObject> worldListItems = new List<GameObject>();
    
    private void Start()
    {
        InitializeMainMenu();
        SetupButtonListeners();
        ShowMainMenuWindow();
    }
    
    private void InitializeMainMenu()
    {
        if (WorldSaveManager.Instance == null)
        {
            GameObject saveManagerObject = new GameObject("WorldSaveManager");
            saveManagerObject.AddComponent<WorldSaveManager>();
        }
        
        GenerateRandomSeed();
    }
    
    private void SetupButtonListeners()
    {
        if (playButton != null)
            playButton.onClick.AddListener(ShowWorldSelectionWindow);
        
        if (exitButton != null)
            exitButton.onClick.AddListener(ExitGame);
        
        if (newWorldButton != null)
            newWorldButton.onClick.AddListener(ShowNewWorldWindow);
        
        if (worldSelectionExitButton != null)
            worldSelectionExitButton.onClick.AddListener(ShowMainMenuWindow);
        
        if (createWorldButton != null)
            createWorldButton.onClick.AddListener(CreateNewWorld);
        
        if (newWorldExitButton != null)
            newWorldExitButton.onClick.AddListener(ShowWorldSelectionWindow);
    }
    
    public void ShowMainMenuWindow()
    {
        SetActiveWindow(mainMenuWindow);
    }
    
    public void ShowWorldSelectionWindow()
    {
        SetActiveWindow(worldSelectionWindow);
        RefreshWorldList();
    }
    
    public void ShowNewWorldWindow()
    {
        SetActiveWindow(newWorldWindow);
        GenerateRandomSeed();
    }
    
    private void SetActiveWindow(GameObject activeWindow)
    {
        if (mainMenuWindow != null) mainMenuWindow.SetActive(false);
        if (worldSelectionWindow != null) worldSelectionWindow.SetActive(false);
        if (newWorldWindow != null) newWorldWindow.SetActive(false);
        
        if (activeWindow != null) activeWindow.SetActive(true);
    }
    
    private void RefreshWorldList()
    {
        ClearWorldList();
        
        if (WorldSaveManager.Instance != null)
        {
            availableWorlds = WorldSaveManager.Instance.GetAllWorldSaves();
            
            foreach (WorldSaveData worldData in availableWorlds)
            {
                CreateWorldListItem(worldData);
            }
        }
    }
    
    private void ClearWorldList()
    {
        foreach (GameObject item in worldListItems)
        {
            if (item != null)
                Destroy(item);
        }
        worldListItems.Clear();
    }
    
    private void CreateWorldListItem(WorldSaveData worldData)
    {
        if (worldItemPrefab == null || worldListContent == null) return;
        
        GameObject worldItem = Instantiate(worldItemPrefab, worldListContent);
        worldListItems.Add(worldItem);
        
        WorldListItem worldListItem = worldItem.GetComponent<WorldListItem>();
        if (worldListItem != null)
        {
            worldListItem.Setup(worldData, LoadSelectedWorld);
        }
        else
        {
            Button worldButton = worldItem.GetComponent<Button>();
            if (worldButton != null)
            {
                worldButton.onClick.AddListener(() => LoadSelectedWorld(worldData));
            }
            
            TMP_Text worldText = worldItem.GetComponentInChildren<TMP_Text>();
            if (worldText != null)
            {
                worldText.text = worldData.worldName + " - Created: " + worldData.GetFormattedCreatedDate();
            }
        }
    }
    
    private void LoadSelectedWorld(WorldSaveData worldData)
    {
        if (WorldSaveManager.Instance != null)
        {
            WorldSaveManager.Instance.LoadWorld(worldData);
            SceneManager.LoadScene(gameSceneName);
        }
    }
    
    private void CreateNewWorld()
    {
        string worldName = worldNameInput != null ? worldNameInput.text.Trim() : "";
        string seedText = worldSeedInput != null ? worldSeedInput.text.Trim() : "";
        
        if (string.IsNullOrEmpty(worldName))
        {
            worldName = "New World";
        }
        
        int worldSeed;
        if (!int.TryParse(seedText, out worldSeed))
        {
            worldSeed = Random.Range(int.MinValue, int.MaxValue);
        }
        
        if (WorldSaveManager.Instance != null)
        {
            WorldSaveData newWorld = WorldSaveManager.Instance.CreateNewWorld(worldName, worldSeed);
            if (newWorld != null)
            {
                LoadSelectedWorld(newWorld);
            }
        }
    }
    
    private void GenerateRandomSeed()
    {
        if (worldSeedInput != null)
        {
            int randomSeed = Random.Range(int.MinValue, int.MaxValue);
            worldSeedInput.text = randomSeed.ToString();
        }
    }
    
    private void ExitGame()
    {
        Application.Quit();
    }
}