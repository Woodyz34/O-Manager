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

    // The host's choice of extra game modes. Remembered for the next race in the same session (the lobby scene is
    // loaded again after every race).
    public ExtraMode modes
    {
        get => remembered;
        set => remembered = value;
    }
    static ExtraMode remembered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRemembered()
    {
        remembered = ExtraMode.None;
    }

    // Imposter: one random player becomes the imposter when the game starts
    public bool imposterEnabled => (modes & ExtraMode.Imposter) != 0;

    string seenMode;
    string seenMap;
    ExtraMode seenModes;
    int seenTasksOff;
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
        // The course line also says Sprint or Forest now, so it may need to shrink a little
        if (mapText != null && !mapText.enableAutoSizing)
        {
            mapText.enableAutoSizing = true;
            mapText.fontSizeMax = mapText.fontSize;
            mapText.fontSizeMin = Mathf.Min(14f, mapText.fontSize);
        }
        MoveHeaderUp();
        // Hover texts explain what is shown now (the pool and modes that are picked), not every possible choice
        if (modeText != null) HoverTip.Add(modeText.transform.parent, () => poolTip);
        if (imposterText != null) HoverTip.Add(imposterText.transform.parent, () => modeTip);
        if (mapText != null) HoverTip.Add(mapText.transform.parent, UIHints.Course + " The host decides.");
        Show("...", ExtraMode.None, "...");
        LobbyVisit.AddButtons();
    }

    // The strip with runner pool, game mode and course sat so low that the Runner setup and Players panels
    // covered its ends. It goes up to the top row, between Leave lobby (left) and the money pill (right).
    void MoveHeaderUp()
    {
        if (mapText == null) return;
        Transform card = mapText.transform;
        while (card != null && card.name != "Game setup card") card = card.parent;
        RectTransform rect = card as RectTransform;
        if (rect == null) return;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -30f);
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
        NetworkPlayer local = NetworkPlayer.Local;
        bool host = local != null && local.Object != null && local.Object.IsValid && local.Runner != null && local.Runner.IsSharedModeMasterClient;
        if (!host)
        {
            // The other players can look at what the host has picked, including which secret tasks are in
            ModesPopup.Open(seenModes, null, null, "What the host has picked. Only the host can change it.", true, seenTasksOff);
            return;
        }
        ModesPopup.Open(modes, chosen =>
        {
            modes = chosen;
            nextRefresh = 0f;
        }, null, null, false, SecretTasks.HostOff, off =>
        {
            SecretTasks.HostOff = off;
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

        // Everyone gets the button: the host to change the modes, the others to look at them
        if (imposterToggle.gameObject.activeSelf != ready)
        {
            imposterToggle.gameObject.SetActive(ready);
        }
        if (!ready)
        {
            return;
        }

        PublishCloudId(local);

        if (host)
        {
            // We have just taken over from a host who left: keep what they had picked
            if (!wasHost && hasSeenHostSettings)
            {
                if (!string.IsNullOrEmpty(seenMode)) gameModeHandler.gameMode = seenMode;
                // CourseLibrary.Current already holds the course the old host had picked
                if (CourseLibrary.Current != null) mapHandler.Select(CourseLibrary.Current);
                modes = seenModes;
                SecretTasks.HostOff = seenTasksOff;
            }

            // A course on the old host's private map cannot be raced once that host has left
            if (mapHandler.course != null && !MapDefinition.Exists(mapHandler.course.mapId) && CourseLibrary.BuiltIn.Count > 0)
            {
                UIToast.Show("The course was on the old host's private map, so a built-in course is picked instead.", 5f);
                mapHandler.Select(CourseLibrary.BuiltIn[0]);
            }
            // A course on this player's own private map: the others in the lobby get a copy for the race (PrivateMaps)
            MapDefinition courseMap = mapHandler.course != null ? MapDefinition.FindExact(mapHandler.course.mapId) : null;
            string mapOwner = "";
            if (PrivateMaps.IsMine(courseMap))
            {
                mapOwner = CloudCourses.PlayerId;
                PrivateMaps.HostTick(courseMap, GuestIds(local));
            }
            if (local.lobbyMapOwner.ToString() != mapOwner) local.lobbyMapOwner = mapOwner;
            if (local.privateMapReady.Length > 0) local.privateMapReady = "";
            if (local.lobbyCourseReady != 0) local.lobbyCourseReady = 0;

            // Imposter and Secret task are always raced with Same stats (everyone runs with the same values, so the
            // runner gives nobody away): turning one of them on switches the pool and lets the host set the values
            if ((modes & ExtraModes.ImposterKinds) != 0 && !GameModes.IsSameStats(gameModeHandler.gameMode))
            {
                gameModeHandler.gameMode = SameStatsPopup.LastValuesMode();
                UIToast.Show("Imposter and Secret task are raced with Same stats: everyone runs with the values you set.", 5f);
                SameStatsPopup.Open(gameModeHandler.gameMode, mode =>
                {
                    if (GameModes.IsSameStats(mode)) gameModeHandler.gameMode = mode;
                    nextRefresh = 0f;
                });
            }

            if (local.lobbyGameMode.ToString() != gameModeHandler.gameMode) local.lobbyGameMode = gameModeHandler.gameMode;
            if (local.lobbyMap.ToString() != mapHandler.map) local.lobbyMap = mapHandler.map;
            string courseText = mapHandler.course != null ? mapHandler.course.ToLobbyString() : "";
            local.SetLobbyCourseText(courseText);
            if (local.lobbyModes != (int)modes) local.lobbyModes = (int)modes;
            if (local.lobbyTasksOff != SecretTasks.HostOff) local.lobbyTasksOff = SecretTasks.HostOff;

            Show(gameModeHandler.gameMode, modes, CourseTitle(mapHandler.course));
        }
        else
        {
            if (local.lobbyGameMode.Length > 0)
            {
                local.lobbyGameMode = "";
            }
            int courseReady = 0;
            foreach (NetworkPlayer player in NetworkPlayer.All())
            {
                if (player == local || player.Object == null || !player.Object.IsValid || player.lobbyGameMode.Length == 0)
                {
                    continue;
                }
                seenMode = player.lobbyGameMode.ToString();
                seenMap = player.lobbyMap.ToString();
                CourseLibrary.ApplyLobbyString(player.LobbyCourseText());
                seenModes = (ExtraMode)player.lobbyModes;
                seenTasksOff = player.lobbyTasksOff;
                hasSeenHostSettings = true;
                FollowPrivateMap(local, player.lobbyMapOwner.ToString());
                courseReady = HaveCourse(local, player.LobbyCourseText());
                break;
            }
            if (local.lobbyCourseReady != courseReady) local.lobbyCourseReady = courseReady;
            if (hasSeenHostSettings)
            {
                string title = CourseLibrary.Current != null ? CourseTitle(CourseLibrary.Current) : seenMap;
                if (guestNote != null) title += "  <size=70%><color=#E8742A>" + guestNote + "</color></size>";
                Show(seenMode, seenModes, title);
            }
        }
        wasHost = host;

        imposterToggleText.text = host ? "Game mode" : "See game modes";
        imposterToggleText.color = toggleOffTextColor;
        imposterToggle.image.color = toggleOffColor;
    }

    // ---- Everyone has the course before the race starts (2026-11-26, the user) ----

    // A number made from the host's course text (FNV-1a), never 0. The same on every computer for the same text.
    public static int CourseKey(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            int key = (int)hash;
            return key == 0 ? 1 : key;
        }
    }

    // Not the host: the key of the host's course once this computer has set it up and has its map, else 0
    static int HaveCourse(NetworkPlayer local, string hostText)
    {
        CourseData course = CourseLibrary.Current;
        if (string.IsNullOrEmpty(hostText) || course == null || CourseLibrary.LastLobbyString != hostText) return 0;
        if (local.privateMapReady.ToString() != course.mapId) return 0;
        return CourseKey(hostText);
    }

    // True when this player has the host's course (the host always has it)
    public static bool HasCourse(NetworkPlayer player)
    {
        if (player == null || player.Object == null || !player.Object.IsValid) return false;
        if (player.lobbyGameMode.Length > 0) return true;
        string hostText = null;
        foreach (NetworkPlayer p in NetworkPlayer.All())
        {
            if (p.Object != null && p.Object.IsValid && p.lobbyGameMode.Length > 0)
            {
                hostText = p.LobbyCourseText();
                break;
            }
        }
        // No host settings seen yet: nothing to wait for
        if (string.IsNullOrEmpty(hostText)) return true;
        return player.lobbyCourseReady == CourseKey(hostText);
    }

    // The host, at Start game: why it has to wait, or null when every player in the lobby has the course and its map.
    // A player who has just come in and has no player of its own yet counts as waiting too.
    public static string CourseStartProblem(Fusion.NetworkRunner runner)
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (runner == null || local == null || runner.GameMode == Fusion.GameMode.Single) return null;
        // The others only follow the host's course once its lobby settings are published
        if (local.lobbyGameMode.Length == 0) return null;
        int want = CourseKey(local.LobbyCourseText());
        if (want == 0) return null;
        int waiting = 0, total = 0;
        string first = null;
        foreach (Fusion.PlayerRef player in runner.ActivePlayers)
        {
            if (player == runner.LocalPlayer) continue;
            total++;
            NetworkPlayer found = null;
            foreach (NetworkPlayer p in NetworkPlayer.All())
            {
                if (p.Object != null && p.Object.IsValid && p.Object.StateAuthority == player)
                {
                    found = p;
                    break;
                }
            }
            if (found != null && found.lobbyCourseReady == want) continue;
            waiting++;
            if (first == null)
            {
                string name = found != null ? found.nickName.ToString() : "";
                first = string.IsNullOrWhiteSpace(name) ? "a player who is joining" : name;
            }
        }
        if (waiting == 0) return null;
        return waiting == 1
            ? "Waiting for " + first + " to get the course. The race can start when everyone has it."
            : "Waiting for " + waiting + " of " + total + " players to get the course. The race can start when everyone has it.";
    }

    // ---- Private maps (PrivateMaps) ----

    string guestNote;
    float nextSignIn;

    // Every player shows its Unity player id, so a host can let it download a private map
    void PublishCloudId(NetworkPlayer local)
    {
        if (CloudCourses.IsSignedIn)
        {
            if (local.cloudId.ToString() != CloudCourses.PlayerId) local.cloudId = CloudCourses.PlayerId;
        }
        else if (Time.unscaledTime >= nextSignIn)
        {
            nextSignIn = Time.unscaledTime + 15f;
            SignInQuietly();
        }
    }

    static async void SignInQuietly()
    {
        try { await CloudCourses.SignInAsync(); }
        catch (System.Exception e) { Debug.LogWarning("LobbySettingsUI: could not sign in to Unity Cloud: " + e.Message); }
    }

    static System.Collections.Generic.List<string> GuestIds(NetworkPlayer local)
    {
        var ids = new System.Collections.Generic.List<string>();
        foreach (NetworkPlayer player in NetworkPlayer.All())
        {
            if (player == local || player.Object == null || !player.Object.IsValid) continue;
            string id = player.cloudId.ToString();
            if (id.Length > 0 && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    // Not the host: when the host's course is on the host's private map, get a copy of it for this lobby and say
    // when it is ready
    void FollowPrivateMap(NetworkPlayer local, string owner)
    {
        string readyId = "";
        int progress = 0;
        guestNote = null;
        CourseData course = CourseLibrary.Current;
        if (owner.Length == 0 && course != null)
        {
            // A map that is not private (2026-11-19): ready once this computer has it. A map the host has just imported
            // reaches the others through the shared maps, which are looked for again while it is missing.
            MapDefinition have = MapDefinition.FindExact(course.mapId);
            if (have != null && MapPackages.GridExists(have.gridName)) readyId = course.mapId;
            else
            {
                SharedMaps.RefreshForLobby();
                guestNote = SharedMaps.Downloading != null ? "Downloading the map " + SharedMaps.Downloading + " ..." : "Getting the host's map ...";
            }
        }
        else if (owner.Length > 0 && course != null)
        {
            if (owner == CloudCourses.PlayerId)
            {
                readyId = course.mapId;   // my own map, raced while someone else hosts
            }
            else
            {
                readyId = PrivateMaps.GuestTick(owner, course.mapId);
                progress = PrivateMaps.GuestProgress;
                if (readyId.Length == 0 && PrivateMaps.GuestStatus != null)
                    guestNote = PrivateMaps.GuestStatus + (progress > 0 ? " " + progress + " %" : "");
            }
        }
        if (local.privateMapReady.ToString() != readyId) local.privateMapReady = readyId;
        if (local.privateMapProgress != progress) local.privateMapProgress = progress;
    }

    public static string CourseTitle(CourseData course)
    {
        if (course == null) return "...";
        // Everyone sees whether it is a sprint or a forest course, next to the name and who made it
        string type = course.IsSprint() ? "<color=#E8742A>Sprint</color>" : "<color=#3F7D54>Forest</color>";
        if (course.IsRelay()) type = "<color=#2C6E8F>Relay · " + course.legs + (course.legs == 1 ? " leg" : " legs") + "</color>  ·  " + type;
        MapDefinition courseMap = MapDefinition.FindExact(course.mapId);
        if (courseMap != null && courseMap.isPrivate) type += "  ·  <color=#8A5A2B>Private map</color>";
        string by = string.IsNullOrEmpty(course.author) ? "" : "  ·  by " + course.author;
        return course.name + "  <size=70%><b>" + type + "</b>" + by + "</size>";
    }

    string poolTip = "";
    string modeTip = "";

    void Show(string mode, ExtraMode extra, string map)
    {
        bool noRunners = (extra & ExtraModes.NoRunners) != 0;
        poolTip = GameModes.ExplainPool(mode, noRunners);
        modeTip = mode == "..." ? "" : ExtraModes.Explain(extra);
        // Route analysis and the route choice game give everyone the same runner, so the pool does not matter then
        modeText.text = mode == "..." ? mode : (extra & ExtraModes.NoRunners) != 0 ? "No runners" : GameModes.PoolName(mode);
        imposterText.text = mode == "..." ? "..." : ExtraModes.Describe(extra);
        mapText.text = map;
    }
}
