using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows everyone in the lobby which game mode, imposter setting and course the host has picked.
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

    void Start()
    {
        imposterToggle.onClick.AddListener(ToggleImposter);
        imposterToggle.gameObject.SetActive(false);
        Show("...", false, "...");
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

        imposterToggleText.text = imposterEnabled ? "Imposter: On" : "Imposter: Off";
        imposterToggleText.color = imposterEnabled ? Color.white : toggleOffTextColor;
        imposterToggle.image.color = imposterEnabled ? toggleOnColor : toggleOffColor;
    }

    static string CourseTitle(CourseData course)
    {
        if (course == null) return "...";
        return string.IsNullOrEmpty(course.author) ? course.name : course.name + "  <size=70%>by " + course.author + "</size>";
    }

    void Show(string mode, bool imposter, string map)
    {
        modeText.text = mode;
        imposterText.text = imposter ? "On" : "Off";
        mapText.text = map;
    }
}
