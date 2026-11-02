using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The menu that opens with Esc on every page: resume, volume, back to the main menu and quit the game.
// The host of a running competition can also end it for everyone and take them back to the lobby.
// The game keeps running behind the menu (other players are still racing), so nothing is paused.
public class EscMenu : MonoBehaviour
{
    const string VolumeKey = "Settings.MasterVolume";

    // Pages that are menus, not a competition
    static readonly string[] MenuScenes = { "MainMenu", "MultiPlayer", "SingelPlayer", "NewCompetition", "Lobby", CourseScenes.Builder };

    public static bool IsOpen => instance != null && instance.root != null && instance.root.activeSelf;

    static EscMenu instance;

    GameObject root;
    TextMeshProUGUI subtitle;
    TextMeshProUGUI volumeValue;
    Slider volumeSlider;
    Button endButton;
    Button mainMenuButton;
    TextMeshProUGUI hostNote;
    float endConfirmUntil = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        AudioListener.volume = SavedVolume();
        if (instance != null)
        {
            return;
        }
        GameObject go = new GameObject("Esc menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<EscMenu>();
    }

    static float SavedVolume()
    {
        return Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
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
        Close();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
        {
            return;
        }
        if (IsOpen)
        {
            Close();
            return;
        }
        // Esc skips the pack opening show, so leave it to that while it plays
        if (FindAnyObjectByType<PackOpening>() != null)
        {
            return;
        }
        Open();
    }

    // Also kept up to date while open: the host can change, or the competition can end behind the menu
    void LateUpdate()
    {
        if (IsOpen)
        {
            Refresh();
        }
    }

    public void Open()
    {
        if (root == null)
        {
            Build();
        }
        endConfirmUntil = -1f;
        volumeSlider.SetValueWithoutNotify(Mathf.Round(AudioListener.volume * 100f));
        ShowVolume(volumeSlider.value);
        root.SetActive(true);
        Cursor.visible = true;
        Refresh();
    }

    public void Close()
    {
        if (root != null)
        {
            root.SetActive(false);
        }
    }

    // ---- What the menu shows ----

    static NetworkRunner ActiveRunner()
    {
        foreach (NetworkRunner runner in FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None))
        {
            if (runner != null && runner.IsRunning)
            {
                return runner;
            }
        }
        return null;
    }

    static bool InCompetition(NetworkRunner runner)
    {
        if (runner == null || NetworkPlayer.Local == null)
        {
            return false;
        }
        string scene = SceneManager.GetActiveScene().name;
        foreach (string menu in MenuScenes)
        {
            if (scene == menu)
            {
                return false;
            }
        }
        return true;
    }

    void Refresh()
    {
        NetworkRunner runner = ActiveRunner();
        bool inCompetition = InCompetition(runner);
        bool single = runner != null && runner.GameMode == GameMode.Single;
        bool host = inCompetition && CompetitionFlow.IsHostRunner(runner);

        endButton.gameObject.SetActive(host);
        if (host)
        {
            bool confirming = Time.unscaledTime < endConfirmUntil;
            string label = single ? "End the race" : "End competition for everyone";
            CourseUI.SetButtonText(endButton, confirming ? "Sure? Click again" : label);
        }
        hostNote.gameObject.SetActive(inCompetition && !host);

        mainMenuButton.gameObject.SetActive(SceneManager.GetActiveScene().name != "MainMenu");

        if (inCompetition && !single)
        {
            subtitle.text = "The race keeps running while the menu is open";
        }
        else if (runner != null && !single && SceneManager.GetActiveScene().name == "Lobby")
        {
            subtitle.text = "Going to the main menu leaves the lobby";
        }
        else
        {
            subtitle.text = "";
        }
        subtitle.gameObject.SetActive(subtitle.text.Length > 0);
    }

    void ShowVolume(float percent)
    {
        volumeValue.text = Mathf.RoundToInt(percent) + "%";
    }

    // ---- Buttons ----

    void OnVolumeChanged(float percent)
    {
        float volume = Mathf.Clamp01(percent / 100f);
        AudioListener.volume = volume;
        PlayerPrefs.SetFloat(VolumeKey, volume);
        PlayerPrefs.Save();
        ShowVolume(percent);
    }

    void OnEndCompetition()
    {
        NetworkRunner runner = ActiveRunner();
        if (!InCompetition(runner) || !CompetitionFlow.IsHostRunner(runner))
        {
            return;
        }
        // Ending it for everyone is a big step, so the button asks once more
        if (Time.unscaledTime >= endConfirmUntil)
        {
            endConfirmUntil = Time.unscaledTime + 3f;
            return;
        }
        Close();
        // Takes every player back to the lobby (single player: back to the single-player page)
        ReturnToLobby.LoadLobby(runner);
    }

    void OnMainMenu()
    {
        Close();
        NetworkRunner runner = ActiveRunner();
        if (runner != null)
        {
            // Leaves the session; in a shared session another player becomes host
            runner.Shutdown();
        }
        SceneManager.LoadScene("MainMenu");
    }

    void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- Building the UI ----

    void Build()
    {
        root = new GameObject("Esc menu canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250; // above every page, below the short messages at the bottom
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        // A dark veil that also catches clicks, so nothing behind the menu is pressed. Clicking it closes the menu.
        Image veil = CourseUI.Panel("Veil", root.transform, CourseUI.Hex("10241A", 0.6f), false);
        CourseUI.Stretch(veil.rectTransform);
        Button veilButton = veil.gameObject.AddComponent<Button>();
        veilButton.transition = Selectable.Transition.None;
        veilButton.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", root.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 0));
        CourseUI.Column(card.gameObject, 18, new RectOffset(48, 48, 40, 40));
        ContentSizeFitter fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Menu", 48, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(title, 60);

        subtitle = CourseUI.Text("Subtitle", card.transform, "", 22, CourseUI.InkMuted, FontStyles.Italic, TextAlignmentOptions.Center);
        CourseUI.Size(subtitle, 30);

        // Volume: a label and the value on one line, the slider under it
        RectTransform volumeRow = CourseUI.Rect("Volume row", card.transform);
        CourseUI.Row(volumeRow.gameObject, 12, new RectOffset(4, 4, 6, 0));
        CourseUI.Size(volumeRow, 40);
        TextMeshProUGUI volumeLabel = CourseUI.Text("Label", volumeRow, "Volume", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(volumeLabel, -1, -1, 1);
        volumeValue = CourseUI.Text("Value", volumeRow, "100%", 28, CourseUI.Accent, FontStyles.Bold, TextAlignmentOptions.Right);
        CourseUI.Size(volumeValue, -1, 110);

        volumeSlider = CourseUI.SliderControl("Volume", card.transform, 0, 100, 100, true, OnVolumeChanged);
        CourseUI.Size(volumeSlider, 40);

        RectTransform gap = CourseUI.Rect("Gap", card.transform);
        CourseUI.Size(gap, 6);

        Button resume = CourseUI.Button("Resume", card.transform, "Resume", CourseUI.ButtonKind.Primary, Close, 28);
        CourseUI.Size(resume, 68);

        endButton = CourseUI.Button("End competition", card.transform, "End competition for everyone", CourseUI.ButtonKind.Danger, OnEndCompetition, 26);
        CourseUI.Size(endButton, 68);

        hostNote = CourseUI.Text("Host note", card.transform, "Only the host can end the competition", 20, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Size(hostNote, 26);

        mainMenuButton = CourseUI.Button("Main menu", card.transform, "Back to main menu", CourseUI.ButtonKind.Secondary, OnMainMenu, 26);
        CourseUI.Size(mainMenuButton, 68);

        Button quit = CourseUI.Button("Quit", card.transform, "Quit game", CourseUI.ButtonKind.Light, OnQuit, 26);
        CourseUI.Size(quit, 68);

        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, "Esc closes the menu", 18, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Size(hint, 24);

        root.SetActive(false);
    }
}
