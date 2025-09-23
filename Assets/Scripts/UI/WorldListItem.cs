using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class WorldListItem : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text worldNameText;
    public TMP_Text worldInfoText;
    public Button selectButton;
    public Button deleteButton;
    
    private WorldSaveData worldData;
    private Action<WorldSaveData> onSelectWorld;
    
    public void Setup(WorldSaveData data, Action<WorldSaveData> selectCallback)
    {
        worldData = data;
        onSelectWorld = selectCallback;
        
        UpdateDisplay();
        SetupButtons();
    }
    
    private void UpdateDisplay()
    {
        if (worldData == null) return;
        
        if (worldNameText != null)
        {
            worldNameText.text = worldData.worldName;
        }
        
        if (worldInfoText != null)
        {
            string infoText = string.Format("Created: {0}\nLast Played: {1}\nSeed: {2}",
                worldData.GetFormattedCreatedDate(),
                worldData.GetFormattedLastPlayedDate(),
                worldData.worldSeed);
            
            worldInfoText.text = infoText;
        }
    }
    
    private void SetupButtons()
    {
        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(SelectWorld);
        }
        
        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(DeleteWorld);
        }
    }
    
    private void SelectWorld()
    {
        onSelectWorld?.Invoke(worldData);
    }
    
    private void DeleteWorld()
    {
        if (worldData != null && WorldSaveManager.Instance != null)
        {
            bool success = WorldSaveManager.Instance.DeleteWorld(worldData.worldName);
            if (success)
            {
                Destroy(gameObject);
            }
        }
    }
    
    private void OnValidate()
    {
        if (selectButton == null)
        {
            selectButton = GetComponent<Button>();
        }
    }
}