using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class EscapeMenuManager : MonoBehaviour
{
    public static EscapeMenuManager Instance { get; private set; }
    
    [Header("UI References")]
    public GameObject escapeMenuPanel;
    public Button resumeButton;
    public Button saveAndExitButton;
    
    [Header("Settings")]
    public string mainMenuSceneName = "MainMenu";
    
    private bool isMenuOpen = false;
    private bool wasGamePaused = false;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        SetupUI();
    }
    
    private void Start()
    {
        HideEscapeMenu();
    }
    
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isMenuOpen)
            {
                ResumeGame();
            }
            else
            {
                OpenEscapeMenu();
            }
        }
    }
    
    private void SetupUI()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveAllListeners();
            resumeButton.onClick.AddListener(ResumeGame);
        }
        
        if (saveAndExitButton != null)
        {
            saveAndExitButton.onClick.RemoveAllListeners();
            saveAndExitButton.onClick.AddListener(SaveAndExit);
        }
    }
    
    public void OpenEscapeMenu()
    {
        Debug.Log("Opening escape menu");
        
        if (escapeMenuPanel != null)
        {
            escapeMenuPanel.SetActive(true);
        }
        
        isMenuOpen = true;
        PauseGame();
        
        ShowCursor();
    }
    
    public void ResumeGame()
    {
        Debug.Log("Resuming game");
        HideEscapeMenu();
    }
    
    public void SaveAndExit()
    {
        Debug.Log("Save and exit requested");
        
        SaveGame();
        
        LoadMainMenu();
    }
    
    private void HideEscapeMenu()
    {
        if (escapeMenuPanel != null)
        {
            escapeMenuPanel.SetActive(false);
        }
        
        isMenuOpen = false;
        ResumeGameTime();
        
        HideCursor();
    }
    
    private void PauseGame()
    {
        wasGamePaused = Time.timeScale == 0f;
        
        if (!wasGamePaused)
        {
            Time.timeScale = 0f;
            Debug.Log("Game paused");
        }
    }
    
    private void ResumeGameTime()
    {
        if (!wasGamePaused)
        {
            Time.timeScale = 1f;
            Debug.Log("Game resumed");
        }
        
        wasGamePaused = false;
    }
    
    private void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    
    private void HideCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    
    private void SaveGame()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SaveGame();
            Debug.Log("Game saved via EscapeMenuManager");
        }
        else if (WorldSaveManager.Instance != null)
        {
            SavePlayerData();
            Debug.Log("Game saved directly via WorldSaveManager");
        }
        else
        {
            Debug.LogWarning("No save manager found!");
        }
    }
    
    private void SavePlayerData()
    {
        if (WorldSaveManager.Instance == null) return;
        
        WorldSaveData currentWorld = WorldSaveManager.Instance.GetCurrentWorldData();
        if (currentWorld == null) return;
        
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            currentWorld.playerPosition = player.transform.position;
            currentWorld.playerRotation = player.transform.eulerAngles;
        }
        
        currentWorld.playtimeMinutes += Time.realtimeSinceStartup / 60f;
        
        WorldSaveManager.Instance.SaveWorldData(currentWorld);
    }
    
    private void LoadMainMenu()
    {
        Time.timeScale = 1f;
        
        SceneManager.LoadScene(mainMenuSceneName);
    }
    
    public bool IsMenuOpen()
    {
        return isMenuOpen;
    }
    
    private void OnValidate()
    {
        if (escapeMenuPanel == null)
        {
            escapeMenuPanel = GameObject.Find("EscapeMenuPanel");
        }
        
        if (resumeButton == null && escapeMenuPanel != null)
        {
            Transform resumeBtn = escapeMenuPanel.transform.Find("Resume Button");
            if (resumeBtn != null)
            {
                resumeButton = resumeBtn.GetComponent<Button>();
            }
        }
        
        if (saveAndExitButton == null && escapeMenuPanel != null)
        {
            Transform saveBtn = escapeMenuPanel.transform.Find("Save and exit Button");
            if (saveBtn != null)
            {
                saveAndExitButton = saveBtn.GetComponent<Button>();
            }
        }
    }
}