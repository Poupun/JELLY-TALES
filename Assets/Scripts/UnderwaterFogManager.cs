using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages underwater fog effects for depth-based water rendering.
/// Detects when camera is underwater and applies fog shader properties.
/// Mimics Minecraft's water depth fog system with professional optimization.
/// </summary>
public class UnderwaterFogManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Main camera - auto-finds if null")]
    public Camera mainCamera;

    [Tooltip("WorldGenerator reference - auto-finds if null")]
    public WorldGenerator worldGenerator;

    [Header("Underwater Fog Settings")]
    [Tooltip("Enable underwater fog effect")]
    public bool enableUnderwaterFog = true;

    [Tooltip("Fog color when underwater (dark blue/green tint)")]
    public Color underwaterFogColor = new Color(0.1f, 0.3f, 0.4f, 1f);

    [Tooltip("Fog density - higher = less visibility")]
    [Range(0.001f, 0.5f)]
    public float underwaterFogDensity = 0.08f;

    [Tooltip("How far you can see underwater (in blocks)")]
    [Range(5f, 100f)]
    public float underwaterVisibilityRange = 30f;

    [Header("Surface Depth Fog Settings")]
    [Tooltip("Enable fog darkening effect visible from surface")]
    public bool enableSurfaceDepthFog = true;

    [Tooltip("Color tint for deep water when viewed from surface")]
    public Color deepWaterColor = new Color(0.05f, 0.15f, 0.3f, 1f);

    [Tooltip("Depth at which water reaches maximum darkness (in blocks)")]
    [Range(5f, 50f)]
    public float maxDepthForDarkening = 20f;

    [Header("Transition Settings")]
    [Tooltip("How fast fog transitions when entering/exiting water")]
    [Range(1f, 20f)]
    public float fogTransitionSpeed = 8f;

    // State tracking
    private bool isUnderwater = false;
    private bool wasUnderwater = false;
    private float currentFogDensity = 0f;
    private Color currentFogColor = Color.clear;

    // Original RenderSettings for restoration
    private bool originalFogEnabled;
    private Color originalFogColor;
    private float originalFogDensity;
    private FogMode originalFogMode;

    // Shader property IDs (cached for performance)
    private static readonly int _UnderwaterFogDensity = Shader.PropertyToID("_UnderwaterFogDensity");
    private static readonly int _UnderwaterFogColor = Shader.PropertyToID("_UnderwaterFogColor");
    private static readonly int _UnderwaterFogEnabled = Shader.PropertyToID("_UnderwaterFogEnabled");
    private static readonly int _DepthFogColor = Shader.PropertyToID("_DepthFogColor");
    private static readonly int _MaxDepthDarkening = Shader.PropertyToID("_MaxDepthDarkening");

    void Start()
    {
        // Auto-find references
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = GetComponentInChildren<Camera>();
            }
        }

        if (worldGenerator == null)
        {
            worldGenerator = FindFirstObjectByType<WorldGenerator>();
        }

        if (mainCamera == null)
        {
            Debug.LogError("UnderwaterFogManager: No camera found!");
            enabled = false;
            return;
        }

        if (worldGenerator == null)
        {
            Debug.LogWarning("UnderwaterFogManager: No WorldGenerator found - underwater detection may not work properly.");
        }

        // Store original fog settings
        StoreOriginalFogSettings();

        // Set initial global shader properties for depth fog (always active for surface viewing)
        UpdateDepthFogShaderProperties();

        Debug.Log("UnderwaterFogManager: Initialized - underwater depth fog system ready");
    }

    void Update()
    {
        if (!enableUnderwaterFog || mainCamera == null) return;

        // Check if camera is underwater
        isUnderwater = CheckIfUnderwater();

        // Handle underwater state changes
        if (isUnderwater != wasUnderwater)
        {
            OnUnderwaterStateChanged(isUnderwater);
            wasUnderwater = isUnderwater;
        }

        // Update fog gradually
        if (isUnderwater)
        {
            UpdateUnderwaterFog();
        }
        else
        {
            UpdateAboveWaterFog();
        }

        // Always update depth fog properties for surface viewing
        if (enableSurfaceDepthFog)
        {
            UpdateDepthFogShaderProperties();
        }
    }

    private bool CheckIfUnderwater()
    {
        if (worldGenerator == null) return false;

        Vector3 cameraPos = mainCamera.transform.position;
        Vector3Int blockPos = new Vector3Int(
            Mathf.FloorToInt(cameraPos.x),
            Mathf.FloorToInt(cameraPos.y),
            Mathf.FloorToInt(cameraPos.z)
        );

        // Check if the block at camera position is water
        BlockType blockType = worldGenerator.GetBlockType(blockPos);
        return blockType == BlockType.Water;
    }

    private void OnUnderwaterStateChanged(bool underwater)
    {
        if (underwater)
        {
            Debug.Log("UnderwaterFogManager: Camera entered water");
            EnableUnderwaterFog();
        }
        else
        {
            Debug.Log("UnderwaterFogManager: Camera exited water");
            DisableUnderwaterFog();
        }
    }

    private void EnableUnderwaterFog()
    {
        // Enable Unity fog for underwater effect
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;

        // Set target values
        currentFogColor = underwaterFogColor;
        currentFogDensity = underwaterFogDensity;

        // Set global shader property for shaders to use
        Shader.SetGlobalFloat(_UnderwaterFogEnabled, 1.0f);
        Shader.SetGlobalFloat(_UnderwaterFogDensity, underwaterFogDensity);
        Shader.SetGlobalColor(_UnderwaterFogColor, underwaterFogColor);
    }

    private void DisableUnderwaterFog()
    {
        // Restore original fog settings
        RenderSettings.fog = originalFogEnabled;
        RenderSettings.fogMode = originalFogMode;
        RenderSettings.fogColor = originalFogColor;
        RenderSettings.fogDensity = originalFogDensity;

        // Disable shader property
        Shader.SetGlobalFloat(_UnderwaterFogEnabled, 0.0f);
    }

    private void UpdateUnderwaterFog()
    {
        // Smoothly transition to underwater fog
        RenderSettings.fogColor = Color.Lerp(
            RenderSettings.fogColor,
            underwaterFogColor,
            Time.deltaTime * fogTransitionSpeed
        );

        RenderSettings.fogDensity = Mathf.Lerp(
            RenderSettings.fogDensity,
            underwaterFogDensity,
            Time.deltaTime * fogTransitionSpeed
        );

        // Update shader properties
        Shader.SetGlobalColor(_UnderwaterFogColor, RenderSettings.fogColor);
        Shader.SetGlobalFloat(_UnderwaterFogDensity, RenderSettings.fogDensity);
    }

    private void UpdateAboveWaterFog()
    {
        // Smoothly transition back to original fog
        if (RenderSettings.fog)
        {
            RenderSettings.fogColor = Color.Lerp(
                RenderSettings.fogColor,
                originalFogColor,
                Time.deltaTime * fogTransitionSpeed
            );

            RenderSettings.fogDensity = Mathf.Lerp(
                RenderSettings.fogDensity,
                originalFogDensity,
                Time.deltaTime * fogTransitionSpeed
            );
        }
    }

    private void UpdateDepthFogShaderProperties()
    {
        // Set global shader properties for water depth fog (visible from surface)
        Shader.SetGlobalColor(_DepthFogColor, deepWaterColor);
        Shader.SetGlobalFloat(_MaxDepthDarkening, maxDepthForDarkening);
    }

    private void StoreOriginalFogSettings()
    {
        originalFogEnabled = RenderSettings.fog;
        originalFogColor = RenderSettings.fogColor;
        originalFogDensity = RenderSettings.fogDensity;
        originalFogMode = RenderSettings.fogMode;
    }

    void OnDestroy()
    {
        // Restore original fog settings when destroyed
        if (originalFogEnabled)
        {
            RenderSettings.fog = originalFogEnabled;
            RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogDensity = originalFogDensity;
            RenderSettings.fogMode = originalFogMode;
        }

        // Clear global shader properties
        Shader.SetGlobalFloat(_UnderwaterFogEnabled, 0.0f);
    }

    // Public API for external control
    public bool IsUnderwater => isUnderwater;

    public void SetUnderwaterFogDensity(float density)
    {
        underwaterFogDensity = Mathf.Clamp(density, 0.001f, 0.5f);
        if (isUnderwater)
        {
            Shader.SetGlobalFloat(_UnderwaterFogDensity, underwaterFogDensity);
        }
    }

    public void SetUnderwaterFogColor(Color color)
    {
        underwaterFogColor = color;
        if (isUnderwater)
        {
            Shader.SetGlobalColor(_UnderwaterFogColor, underwaterFogColor);
        }
    }

    public void SetDeepWaterColor(Color color)
    {
        deepWaterColor = color;
        UpdateDepthFogShaderProperties();
    }

    public void SetMaxDepth(float depth)
    {
        maxDepthForDarkening = Mathf.Clamp(depth, 5f, 50f);
        UpdateDepthFogShaderProperties();
    }
}