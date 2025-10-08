using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

// Attach to a GameObject to control world shadow intensity at runtime.
// - Adjusts Light.shadowStrength on all Directional Lights (or a specific target Light).
// - Optional: Adjust URP shadow distance via reflection (no hard URP dependency).
// - Optional: Keyboard controls [ and ] to tweak strength during play.
[ExecuteAlways]
public class ShadowController : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("If sstrue, applies to all active Directional Lights. If false, uses Target Light only.")]
    public bool controlAllDirectionalLights = true;

    [Tooltip("Directional sLight to control when 'controlAllDirectionalLights' is false.")]
    public Light targetLight;

    [Header("Shadow Intensity")]
    [Range(0f, 1f)] public float shadowStrength = 1.0f;

    [Header("URP (optional)")]
    [Tooltip("Also adjust the URP Shadow Distance (via reflection)")]
    public bool adjustShadowDistance = false;

    [Min(0f)] public float shadowDistance = 100f;

    [Header("Runtime Controls")]
    public bool enableKeyboardAdjust = true;
    public KeyCode decreaseKey = KeyCode.LeftBracket;   // [
    public KeyCode increaseKey = KeyCode.RightBracket;  // ]
    [Range(0.01f, 0.25f)] public float step = 0.05f;

    [Space]
    public bool applyOnAwake = true;

#if UNITY_EDITOR
    public bool applyEveryUpdateInEditor = true;
#endif

    private float _lastStrength = -1f;
    private float _lastShadowDistance = -1f;

    private void Awake()
    {
        if (applyOnAwake)
        {
            Apply();
        }
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Apply();
        }
#endif
    }

    private void OnValidate()
    {
        shadowStrength = Mathf.Clamp01(shadowStrength);
        shadowDistance = Mathf.Max(0f, shadowDistance);
        step = Mathf.Clamp(step, 0.01f, 0.25f);
        // In editor, reflect changes
        if (!Application.isPlaying)
        {
            Apply();
        }
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && applyEveryUpdateInEditor)
        {
            Apply();
        }
#endif
        if (Application.isPlaying && enableKeyboardAdjust)
        {
            bool changed = false;
            if (Input.GetKeyDown(increaseKey))
            {
                shadowStrength = Mathf.Clamp01(shadowStrength + step);
                changed = true;
            }
            else if (Input.GetKeyDown(decreaseKey))
            {
                shadowStrength = Mathf.Clamp01(shadowStrength - step);
                changed = true;
            }

            if (changed)
            {
                ApplyLights();
                _lastStrength = shadowStrength;
#if UNITY_EDITOR
                Debug.Log($"Shadow strength: {shadowStrength:0.00}");
#endif
            }
        }

        // Avoid redundant work; only apply when values actually changed
        if (Math.Abs(_lastStrength - shadowStrength) > 0.0001f)
        {
            ApplyLights();
            _lastStrength = shadowStrength;
        }
        if (adjustShadowDistance && Math.Abs(_lastShadowDistance - shadowDistance) > 0.0001f)
        {
            TryApplyURPShadowDistance(shadowDistance);
            _lastShadowDistance = shadowDistance;
        }
    }

    [ContextMenu("Apply Now")] 
    public void Apply()
    {
        ApplyLights();
        _lastStrength = shadowStrength;

        if (adjustShadowDistance)
        {
            TryApplyURPShadowDistance(shadowDistance);
            _lastShadowDistance = shadowDistance;
        }
    }

    private void ApplyLights()
    {
        if (controlAllDirectionalLights)
        {
            var dirLights = FindObjectsOfType<Light>()
                .Where(l => l != null && l.type == LightType.Directional).ToList();
            foreach (var l in dirLights)
            {
                // If the light has shadows disabled, keep it disabled — only set strength.
                l.shadowStrength = shadowStrength;
            }
        }
        else if (targetLight != null && targetLight.type == LightType.Directional)
        {
            targetLight.shadowStrength = shadowStrength;
        }
    }

    private static void TryApplyURPShadowDistance(float distance)
    {
        try
        {
            // Use reflection to avoid a hard dependency on URP assemblies in case they're not present at compile time.
            RenderPipelineAsset rpa = GraphicsSettings.currentRenderPipeline;
            if (rpa == null)
            {
                rpa = GraphicsSettings.defaultRenderPipeline;
            }
            if (rpa == null) return;

            var type = rpa.GetType();
            // Property names may vary across URP versions; try common ones.
            var prop = type.GetProperty("shadowDistance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(rpa, distance, null);
                return;
            }

            // Some versions use a nested ShadowSettings object; attempt to drill down.
            var shadowProp = type.GetProperty("shadowCascadeOption", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            // If needed, extend reflection mapping here.
        }
        catch (Exception)
        {
            // Silently ignore if URP not present or API changed; it's optional.
        }
    }
}
