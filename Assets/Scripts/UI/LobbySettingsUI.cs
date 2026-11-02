using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows everyone in the lobby which runner pool, game mode and course the host has picked.
// "Runner pool" (your runners or any runner) is what used to be called game mode; "Game mode" is now
// Normal or Imposter (it used to be an Imposter on/off switch). The names are changed here at start,
// so the saved lobby scene needs no rebuild.
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

    // The host's choice: one random player becomes the imposter when the game starts
    public bool imposterEnabled;

    string seenMode;
    string seenMap;
    bool seenImposter;
    bool hasSeenHostSettings;
    bool wasHost;
    float nextRefresh;

    const string Normal = "Normal";
    const string Imposter = "Imposter";

    void Start()
    {
        imposterToggle.onClick.AddListener(OpenGameModes);
        imposterToggle.gameObject.SetActive(false);
        Rename();
        Show("...", false, "...");
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

    // The host picks the game mode. Imposter: one random player secretly has to finish last.
    public void OpenGameModes()
    {
        ChoicePopup.Option[] options =
        {
            new ChoicePopup.Option { id = Normal, title = "Normal", description = "Everyone races to finish first." },
            new ChoicePopup.Option { id = Imposter, title = "Imposter", description = "One random player secretly has to finish last. Can you spot who?" },
        };
        ChoicePopup.Open("Game mode", options, imposterEnabled ? Imposter : Normal, id =>
        {
            imposterEnabled = id == Imposter;
            nextRefresh = 0f;
        });
    }

    public void ToggleImposter()
    {
        imposterEnabled = !imposterEnabled;
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
                imposterEnabled = seenImposter;
            }

            if (local.lobbyGameMode.ToString() != gameModeHandler.gameMode) local.lobbyGameMode = gameModeHandler.gameMode;
            if (local.lobbyMap.ToString() != mapHandler.map) local.lobbyMap = mapHandler.map;
            string courseText = mapHandler.course != null ? mapHandler.course.ToLobbyString() : "";
            local.SetLobbyCourseText(courseText);
            if (local.lobbyImposter != imposterEnabled) local.lobbyImposter = imposterEnabled;

            Show(gameModeHandler.gameMode, imposterEnabled, CourseTitle(mapHandler.course));
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
                seenImposter = player.lobbyImposter;
                hasSeenHostSettings = true;
                break;
            }
            if (hasSeenHostSettings)
            {
                Show(seenMode, seenImposter, CourseLibrary.Current != null ? CourseTitle(CourseLibrary.Current) : seenMap);
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

    void Show(string mode, bool imposter, string map)
    {
        modeText.text = mode == "..." ? mode : GameModes.PoolName(mode);
        imposterText.text = imposter ? "Imposter" : "Normal";
        mapText.text = map;
    }
}
