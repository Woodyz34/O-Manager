using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows everyone in the lobby which runner pool, game mode and course the host has picked.
// "Runner pool" (your runners or any runner) is what used to be called game mode; "Game mode" is now
// a set of extra modes that can be combined (Imposter, Bears, Move a control) or Route analysis on its own.
// The names are changed here at start, so the saved lobby scene needs no rebuild.
// The host publishes its choices on its own player object; the other players read them from there.
// If the host leaves, the player who takes over keeps the settings the old host had.
public class LobbySettingsUI : MonoBehaviour
{
    public GameModeHandler gameModeHandler;
    public MapHandler mapHandler;

    [Header("Shown to everyone")]
    public TextMeshProUGUI modeText;
    public TextMeshProUGUI imposterText;
    public TextMeshProUGUI mapText;

    [Header("Host only")]
    public Button imposterToggle;
    public TextMeshProUGUI imposterToggleText;
    public Color toggleOffColor = new Color(1f, 1f, 1f, 0.78f);
    public Color toggleOnColor = new Color(0.91f, 0.45f, 0.16f);
    public Color toggleOffTextColor = new Color(0.12f, 0.24f, 0.17f);

    // The host's choice of extra game modes
    public ExtraMode modes;

    // Imposter: one random player becomes the imposter when the game starts
    public bool imposterEnabled => (modes & ExtraMode.Imposter) != 0;

    string seenMode;
    string seenMap;
    ExtraMode seenModes;
    bool hasSeenHostSettings;
    bool wasHost;
    float nextRefresh;

    void Start()
    {
        imposterToggle.onClick.AddListener(OpenGameModes);
        imposterToggle.gameObject.SetActive(false);
        Rename();
        // Several modes can be on at once, so the text has to fit a longer line
        if (imposterText != null)
        {
            imposterText.enableAutoSizing = true;
            imposterText.fontSizeMax = imposterText.fontSize;
            imposterText.fontSizeMin = Mathf.Min(14f, imposterText.fontSize);
        }
        Show("...", ExtraMode.None, "...");
    }

    void Rename()
    {
        SetCaption(modeText, "RUNNER POOL");
        SetCaption(imposterText, "GAME MODE");
        Transform poolButton = transform.Find("Player setup panel/InnerPanel/Change GameMode");
        if (poolButton != null)
        {
            TextMeshProUGUI label = poolButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = "Runner pool";
        }
    }

    static void SetCaption(TextMeshProUGUI value, string caption)
    {
        if (value == null || value.transform.parent == null) return;
        Transform captionObject = value.transform.parent.Find("Caption");
        TextMeshProUGUI text = captionObject != null ? captionObject.GetComponent<TextMeshProUGUI>() : null;
        if (text != null) text.text = caption;
    }

    // The host picks the game modes. Several can be on at once; none means a normal race.
    public void OpenGameModes()
    {
        ModesPopup.Open(modes, chosen =>
        {
            modes = chosen;
            nextRefresh = 0f;
        });
    }

    public void ToggleImposter()
    {
        modes = ExtraModes.Toggle(modes, ExtraMode.Imposter);
        nextRefresh = 0f;
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh)
        {
            return;
        }
        nextRefresh = Time.unscaledTime + 0.25f;

        NetworkPlayer local = NetworkPlayer.Local;
        bool ready = local != null && local.Object != null && local.Object.IsValid && local.Runner != null;
        bool host = ready && local.Runner.IsSharedModeMasterClient;

        if (imposterToggle.gameObject.activeSelf != host)
        {
            imposterToggle.gameObject.SetActive(host);
        }
        if (!ready)
        {
            return;
        }

        if (host)
        {
            // We have just taken over from a host who left: keep what they had picked
            if (!wasHost && hasSeenHostSettings)
            {
                if (!string.IsNullOrEmpty(seenMode)) gameModeHandler.gameMode = seenMode;
                // CourseLibrary.Current already holds the course the old host had picked
                if (CourseLibrary.Current != null) mapHandler.Select(CourseLibrary.Current);
                modes = seenModes;
            }

            if (local.lobbyGameMode.ToString() != gameModeHandler.gameMode) local.lobbyGameMode = gameModeHandler.gameMode;
            if (local.lobbyMap.ToString() != mapHandler.map) local.lobbyMap = mapHandler.map;
            string courseText = mapHandler.course != null ? mapHandler.course.ToLobbyString() : "";
            local.SetLobbyCourseText(courseText);
            if (local.lobbyModes != (int)modes) local.lobbyModes = (int)modes;

            Show(gameModeHandler.gameMode, modes, CourseTitle(mapHandler.course));
        }
        else
        {
            if (local.lobbyGameMode.Length > 0)
            {
                local.lobbyGameMode = "";
            }
            foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                if (player == local || player.Object == null || !player.Object.IsValid || player.lobbyGameMode.Length == 0)
                {
                    continue;
                }
                seenMode = player.lobbyGameMode.ToString();
                seenMap = player.lobbyMap.ToString();
                CourseLibrary.ApplyLobbyString(player.LobbyCourseText());
                seenModes = (ExtraMode)player.lobbyModes;
                hasSeenHostSettings = true;
                break;
            }
            if (hasSeenHostSettings)
            {
                Show(seenMode, seenModes, CourseLibrary.Current != null ? CourseTitle(CourseLibrary.Current) : seenMap);
            }
        }
        wasHost = host;

        imposterToggleText.text = "Game mode";
        imposterToggleText.color = toggleOffTextColor;
        imposterToggle.image.color = toggleOffColor;
    }

    static string CourseTitle(CourseData course)
    {
        if (course == null) return "...";
        return string.IsNullOrEmpty(course.author) ? course.name : course.name + "  <size=70%>by " + course.author + "</size>";
    }

    void Show(string mode, ExtraMode extra, string map)
    {
        // Route analysis gives everyone the same runner, so the pool does not matter then
        modeText.text = mode == "..." ? mode : (extra & ExtraMode.RouteAnalysis) != 0 ? "Same runner for all" : GameModes.PoolName(mode);
        imposterText.text = mode == "..." ? "..." : ExtraModes.Describe(extra);
        mapText.text = map;
    }
}
