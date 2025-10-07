using UnityEngine;

/// <summary>
/// Global debug log manager - disable all Debug.Log calls for maximum performance
/// Debug.Log calls can cost 1-5ms each, especially in builds!
/// </summary>
public static class DebugLogManager
{
    private static bool _logsEnabled = true;

    /// <summary>
    /// Enable or disable all debug logs globally
    /// </summary>
    public static bool LogsEnabled
    {
        get => _logsEnabled;
        set
        {
            _logsEnabled = value;
            Debug.unityLogger.logEnabled = value;

            if (!value)
            {
                Debug.Log("[DebugLogManager] All debug logs DISABLED for maximum performance");
            }
            else
            {
                Debug.Log("[DebugLogManager] Debug logs ENABLED");
            }
        }
    }

    /// <summary>
    /// Toggle logs on/off
    /// </summary>
    public static void ToggleLogs()
    {
        LogsEnabled = !LogsEnabled;
    }

    /// <summary>
    /// Safe log - only logs if enabled
    /// </summary>
    public static void Log(string message)
    {
        if (_logsEnabled)
            Debug.Log(message);
    }

    /// <summary>
    /// Safe log warning - only logs if enabled
    /// </summary>
    public static void LogWarning(string message)
    {
        if (_logsEnabled)
            Debug.LogWarning(message);
    }

    /// <summary>
    /// Critical errors always log (for important issues)
    /// </summary>
    public static void LogError(string message)
    {
        Debug.LogError(message);
    }
}

/// <summary>
/// Toggle debug logs with keyboard shortcut
/// Add this component to any GameObject to enable the toggle
/// </summary>
public class DebugLogToggle : MonoBehaviour
{
    [Header("Debug Log Control")]
    [Tooltip("Disable logs for maximum performance (saves 1-5ms per log call)")]
    public bool disableLogsOnStart = true;

    [Tooltip("Keyboard shortcut to toggle logs at runtime")]
    public KeyCode toggleKey = KeyCode.F3;

    [Header("Status")]
    [SerializeField] private bool logsCurrentlyEnabled = true;

    private void Start()
    {
        if (disableLogsOnStart)
        {
            DebugLogManager.LogsEnabled = false;
            logsCurrentlyEnabled = false;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            DebugLogManager.ToggleLogs();
            logsCurrentlyEnabled = DebugLogManager.LogsEnabled;
        }
    }

    private void OnGUI()
    {
        // Show status in corner
        string status = logsCurrentlyEnabled ? "ON" : "OFF";
        Color color = logsCurrentlyEnabled ? Color.green : Color.red;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.normal.textColor = color;
        style.fontSize = 12;
        style.fontStyle = FontStyle.Bold;

        GUI.Label(new Rect(10, Screen.height - 30, 200, 25),
            $"Debug Logs: {status} (F3)", style);
    }
}
