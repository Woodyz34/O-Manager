using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Fullscreen or window, and UI that fits every screen.
//
// Display: the game starts in borderless fullscreen at the screen's own resolution. The player can switch
// between fullscreen and a resizable window in the Esc menu, or with F11 / Alt+Enter. The choice is saved.
//
// UI: every page is laid out for 1920x1080. Each screen-space canvas is scaled with the "Expand" rule, so the
// whole 1920x1080 layout is always visible: on a wider screen (21:9, 32:9) the page gets more room at the
// sides, on a taller one (16:10, 4:3) more room above and below, and nothing is cut off at the edges.
// Canvases made in scenes, prefabs or code all pass through Fit(), so new pages get it too.
public class ScreenSettings : MonoBehaviour
{
    public const string FullscreenKey = "Settings.Fullscreen";
    const string WindowWidthKey = "Settings.WindowWidth";
    const string WindowHeightKey = "Settings.WindowHeight";

    public static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);

    // The window is never made smaller than this when leaving fullscreen
    const int MinWindowWidth = 1024;
    const int MinWindowHeight = 600;

    public static event System.Action Changed;

    static ScreenSettings instance;

    int lastWidth;
    int lastHeight;
    int lastCanvasCount = -1;
    float nextCanvasCheck;

    public static bool Fullscreen
    {
        get
        {
#if UNITY_EDITOR
            // The editor's Game view is never fullscreen; show the saved choice instead
            return PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
#else
            return Screen.fullScreenMode != FullScreenMode.Windowed;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        if (instance != null)
        {
            return;
        }
        GameObject go = new GameObject("Screen settings");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ScreenSettings>();
        ApplySaved();
    }

    static void ApplySaved()
    {
        Apply(PlayerPrefs.GetInt(FullscreenKey, 1) == 1, false);
    }

    // Switches between borderless fullscreen (at the monitor's own resolution) and a window
    public static void SetFullscreen(bool on)
    {
        Apply(on, true);
    }

    public static void Toggle()
    {
        SetFullscreen(!Fullscreen);
    }

    static void Apply(bool on, bool save)
    {
        if (save)
        {
            PlayerPrefs.SetInt(FullscreenKey, on ? 1 : 0);
            PlayerPrefs.Save();
        }
#if !UNITY_EDITOR
        Resolution native = Screen.currentResolution;
        int screenWidth = Display.main.systemWidth > 0 ? Display.main.systemWidth : native.width;
        int screenHeight = Display.main.systemHeight > 0 ? Display.main.systemHeight : native.height;
        if (on)
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                RememberWindow();
            }
            Screen.SetResolution(screenWidth, screenHeight, FullScreenMode.FullScreenWindow);
        }
        else
        {
            // Back to the last window size, or 80% of the screen the first time
            int width = PlayerPrefs.GetInt(WindowWidthKey, Mathf.RoundToInt(screenWidth * 0.8f));
            int height = PlayerPrefs.GetInt(WindowHeightKey, Mathf.RoundToInt(screenHeight * 0.8f));
            width = Mathf.Clamp(width, Mathf.Min(MinWindowWidth, screenWidth), screenWidth);
            height = Mathf.Clamp(height, Mathf.Min(MinWindowHeight, screenHeight), screenHeight);
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }
#endif
        Changed?.Invoke();
    }

    static void RememberWindow()
    {
        if (Screen.fullScreenMode != FullScreenMode.Windowed)
        {
            return;
        }
        PlayerPrefs.SetInt(WindowWidthKey, Screen.width);
        PlayerPrefs.SetInt(WindowHeightKey, Screen.height);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FitAllCanvases();
    }

    void Update()
    {
        // F11, or Alt+Enter (Unity's own Alt+Enter is turned off in Player settings so the choice is saved here)
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (Input.GetKeyDown(KeyCode.F11) || (alt && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))))
        {
            Toggle();
        }

        if (Screen.width != lastWidth || Screen.height != lastHeight)
        {
            lastWidth = Screen.width;
            lastHeight = Screen.height;
#if !UNITY_EDITOR
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                RememberWindow();
            }
#endif
            Changed?.Invoke();
        }

        // Pages that make their canvases while running (popups, pack opening, end screen) are fitted
        // as soon as they show up. Checking a few times a second is cheap: there are only a handful.
        if (Time.unscaledTime >= nextCanvasCheck)
        {
            nextCanvasCheck = Time.unscaledTime + 0.2f;
            int count = FindObjectsByType<CanvasScaler>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            if (count != lastCanvasCount)
            {
                lastCanvasCount = count;
                FitAllCanvases();
            }
        }
    }

    static void FitAllCanvases()
    {
        foreach (CanvasScaler scaler in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Fit(scaler);
        }
    }

    // Scales a canvas so the whole 1920x1080 layout fits on any screen shape
    public static void Fit(CanvasScaler scaler)
    {
        if (scaler == null)
        {
            return;
        }
        Canvas canvas = scaler.GetComponent<Canvas>();
        if (canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace)
        {
            return;
        }
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPhysicalSize)
        {
            return;
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    }
}
