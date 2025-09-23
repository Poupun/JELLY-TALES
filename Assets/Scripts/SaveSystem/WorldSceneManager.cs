using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

public class WorldSceneManager : MonoBehaviour
{
    public static WorldSceneManager Instance { get; private set; }
    
    [Header("Scene Configuration")]
    public string baseWorldSceneName = "World";
    public string generatedScenesFolder = "Assets/Scenes/Generated/";
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Ensure the generated scenes folder exists
#if UNITY_EDITOR
            if (!Directory.Exists(generatedScenesFolder))
            {
                Directory.CreateDirectory(generatedScenesFolder);
                UnityEditor.AssetDatabase.Refresh();
            }
#endif
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    public void LoadWorldScene(WorldSaveData worldData)
    {
        Debug.Log($"WorldSceneManager: Loading world scene for '{worldData.worldName}'");
        
        // Store the world data so the next scene can access it
        if (WorldSaveManager.Instance != null)
        {
            WorldSaveManager.Instance.LoadWorld(worldData);
            Debug.Log($"WorldSceneManager: World data set for '{worldData.worldName}', loading base world scene");
        }
        
        // Always load the base World scene - WorldLoader will handle world-specific initialization
        SceneManager.LoadScene(baseWorldSceneName);
    }
    
#if UNITY_EDITOR
    private void CreateWorldScene(WorldSaveData worldData, string scenePath)
    {
        // Copy the base World scene to create a world-specific scene
        string baseScenePath = $"Assets/Scenes/{baseWorldSceneName}.unity";
        
        if (File.Exists(baseScenePath))
        {
            // Copy the scene file
            File.Copy(baseScenePath, scenePath);
            
            // Also copy the .meta file if it exists
            string baseMetaPath = baseScenePath + ".meta";
            string newMetaPath = scenePath + ".meta";
            if (File.Exists(baseMetaPath))
            {
                File.Copy(baseMetaPath, newMetaPath);
                
                // Update the GUID in the meta file to make it unique
                string metaContent = File.ReadAllText(newMetaPath);
                string newGuid = System.Guid.NewGuid().ToString("N");
                metaContent = System.Text.RegularExpressions.Regex.Replace(
                    metaContent, 
                    @"guid: [a-f0-9]{32}", 
                    $"guid: {newGuid}"
                );
                File.WriteAllText(newMetaPath, metaContent);
            }
            
            // Refresh the asset database
            UnityEditor.AssetDatabase.Refresh();
            
            Debug.Log($"WorldSceneManager: Created world scene at '{scenePath}'");
        }
        else
        {
            Debug.LogError($"WorldSceneManager: Base scene not found at '{baseScenePath}'");
        }
    }
#endif
}