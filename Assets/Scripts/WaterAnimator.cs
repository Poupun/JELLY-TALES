using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Animates water materials by cycling through texture frames
/// </summary>
public class WaterAnimator : MonoBehaviour
{
    [Header("Animation Settings")]
    [SerializeField] private float animationSpeed = 0.2f; // Slower animation - reduced from 0.5 to 0.2 FPS
    [SerializeField] private bool enableAnimation = true;
    [SerializeField] private bool smoothTransitions = true; // Enable smooth UV transitions


    private static WaterAnimator _instance;
    private List<Material> waterMaterials = new List<Material>();
    private float animationTime = 0f;

    public static WaterAnimator Instance => _instance;

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        if (!enableAnimation || waterMaterials.Count == 0) return;

        animationTime += Time.deltaTime * animationSpeed;

        // Update all water materials
        foreach (var material in waterMaterials)
        {
            if (material == null) continue;

            // Skip texture animation if material is using wind/vertex animation shader
            bool isUsingWindShader = material.shader != null &&
                                    (material.shader.name == "Custom/LeavesWind" ||
                                     material.shader.name == "Custom/PlantWind" ||
                                     material.shader.name == "Custom/WaterWaves");

            if (isUsingWindShader)
            {
                // Skip texture frame animation for wind shaders - they handle animation via vertex displacement
                continue;
            }

            // Only do texture frame animation for standard materials with _FrameCount
            if (!material.HasProperty("_FrameCount")) continue;

            float frameCount = material.GetFloat("_FrameCount");
            if (frameCount <= 1) continue;

            float frameHeight = 1f / frameCount;
            float yOffset;

            if (smoothTransitions)
            {
                // Smooth interpolation between frames to reduce flickering
                float framePosition = (animationTime * animationSpeed) % frameCount;
                yOffset = 1f - frameHeight - (framePosition * frameHeight);
            }
            else
            {
                // Original discrete frame animation
                int currentFrame = Mathf.FloorToInt(animationTime * animationSpeed) % (int)frameCount;
                yOffset = 1f - frameHeight - (currentFrame * frameHeight);
            }

            material.SetTextureOffset("_BaseMap", new Vector2(0f, yOffset));
        }
    }


    public static void RegisterWaterMaterial(Material material)
    {
        if (_instance != null && material != null)
        {
            if (!_instance.waterMaterials.Contains(material))
            {
                _instance.waterMaterials.Add(material);
                Debug.Log($"Registered water material for animation: {material.name}");
            }
        }
    }

    public static void UnregisterWaterMaterial(Material material)
    {
        if (_instance != null && material != null)
        {
            _instance.waterMaterials.Remove(material);
        }
    }

    public void SetAnimationSpeed(float speed)
    {
        animationSpeed = speed;
    }

    public void SetAnimationEnabled(bool enabled)
    {
        enableAnimation = enabled;
    }

    public void SetSmoothTransitions(bool smooth)
    {
        smoothTransitions = smooth;
    }
}