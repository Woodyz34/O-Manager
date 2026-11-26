using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Watching a race (2026-11-24, Anders): a player who joins a lobby while it is out racing comes straight into the
// race, without a runner of their own, and can only watch. From the next race on they race like everyone else (the
// spectator flag on NetworkPlayer is cleared in the lobby).
//  - A normal race or a relay: the map follows one of the runners. "<" and ">" change runner, and a click on a row of
//    the leaderboard watches that runner. Follow runner off: move and zoom the map freely.
//  - Run it yourself (3D): the forest is built and the list of runners on the left shows what each of them sees
//    (RunSession.Spectate).
//  - The route choice game and route analysis: nothing to see while the others play, so a card shows how far everyone
//    has come and their scores, until the host goes back to the lobby.
//  - When the map of the race could not be set up here (a private map not on this computer, or the course arrived too
//    late), the same card shows how the race goes.
// When the host shows the results, the usual results screen opens for the spectator too (without money for them).
public class Spectator : MonoBehaviour
{
    public static Spectator Active { get; private set; }

    enum View { Deciding, Map, ThreeD, Card }

    NetworkPlayer local;
    View view = View.Deciding;
    float startedAt;
    string cardNote;

    Canvas canvas;
    RectTransform bar;
    TextMeshProUGUI barTitle;
    TextMeshProUGUI barHint;
    RectTransform card;
    TextMeshProUGUI cardTitle;
    TextMeshProUGUI cardBody;
    float nextRefresh;

    public static void Begin(NetworkPlayer local)
    {
        if (local == null) return;
        if (Active != null) Destroy(Active.gameObject);
        Spectator spectator = new GameObject("Spectator").AddComponent<Spectator>();
        spectator.local = local;
        Active = spectator;
    }

    // Run it yourself could not build the forest here: the card instead
    public static void ThreeDFailed(string message)
    {
        if (Active == null) return;
        Active.cardNote = message;
        Active.ShowCard();
    }

    void Awake()
    {
        startedAt = Time.unscaledTime;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby; anything else ends the watching
        if (mode == LoadSceneMode.Additive) return;
        if (this != null) Destroy(gameObject);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Active == this) Active = null;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void Update()
    {
        if (local == null || local.Object == null || !local.Object.IsValid || !local.spectator)
        {
            Destroy(gameObject);
            return;
        }
        if (view == View.Deciding)
        {
            Decide();
            // The loading card is not in the way of the results either
            if (view == View.Deciding && canvas != null && canvas.enabled == CompetitionFlow.ResultsShown) canvas.enabled = !CompetitionFlow.ResultsShown;
            return;
        }
        // The results screen is up: it shows everything, this would only be in the way
        bool results = CompetitionFlow.ResultsShown;
        if (canvas != null && canvas.enabled == results) canvas.enabled = !results;
        if (results) return;

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        if (view == View.Map) RefreshBar();
        else if (view == View.Card) RefreshCard();
    }

    // Waits a moment for the host's game modes and course to arrive, then picks how to watch.
    // (2026-11-26, Anders) The spectator used to see its own last course here, on its own map, because the scene was set
    // up before the host's course had arrived (and a missing map fell back to the first map there is). Now the map
    // shows only once the race's own course is known and its map is on this computer (CourseLoader.ShowRaceCourse).
    // Until then a card with a loading bar says what it waits for; a race on the host's private map says the spectator
    // gets it from the next game, when the host has let everyone in the lobby have it.
    void Decide()
    {
        AnimateLoadingBar();
        float waited = Time.unscaledTime - startedAt;
        if (!ExtraModes.Known && waited < 8f) return;
        if (waited < 0.5f) return;
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;

        // Nothing on the map to watch in these: the card shows how everyone is doing
        if (RouteChoiceGame.On || RouteAnalysis.On)
        {
            CloseLoading();
            ShowCard();
            return;
        }

        CourseLibrary.ApplyFromHost();
        CourseData course = RaceCourse(out string owner, out string hostName);
        if (course == null)
        {
            ShowLoading("Loading the race", "Waiting for the race's course from the host ...", -1);
            return;
        }

        bool shown = !CourseLoader.HeldForSpectator && CourseLoader.Loaded != null && CourseLoader.LoadedMap != null
            && CourseLoader.Loaded.id == course.id && CourseLoader.LoadedMap.id == course.mapId
            && CourseLoader.LoadedScene == SceneManager.GetActiveScene().handle;
        if (!shown)
        {
            MapDefinition map = MapDefinition.FindExact(course.mapId);
            if (map == null)
            {
                if (owner.Length > 0 && owner != CloudCourses.PlayerId)
                {
                    // The host's own private map: only the players in the lobby get a copy, so this race can't be shown
                    CloseLoading();
                    cardHeading = "Private map";
                    cardNote = hostName + " is racing on a private map, so you don't have access to it yet. "
                        + "You get access when " + hostName + " goes back to the lobby and the next game starts.";
                    Debug.Log("Spectator: the race is on the host's private map, showing the card");
                    ShowCard();
                    return;
                }
                // A shared map not downloaded yet: look for it, as the lobby does, and wait
                SharedMaps.RefreshForLobby();
                string downloading = SharedMaps.Downloading;
                ShowLoading("The map has not loaded yet",
                    (downloading != null ? "Downloading the map " + downloading + " ..." : "Getting the map of " + course.name + " ...")
                    + "\nYou see the race as soon as the map is on your computer.", -1);
                return;
            }
            if (!CourseLoader.ShowRaceCourse(course, map))
            {
                CloseLoading();
                cardNote = "The map of this race could not be shown on your computer.";
                Debug.Log("Spectator: the race's map could not be shown here, showing the card");
                ShowCard();
                return;
            }
            CameraController cam = Cam;
            if (cam != null) cam.RefreshMap();
        }
        CloseLoading();

        if (RunSession.On)
        {
            view = View.ThreeD;
            RunSession.Spectate();
            Debug.Log("Spectator: watching a run-it-yourself race");
            return;
        }
        ShowMap();
    }

    float nextCheck;
    string cardHeading;
    string lastCourseText;
    CourseData lastCourse;

    // The course the host is racing, as the host published it in the lobby (the host is the one with the lobby's game
    // mode on its player, LobbySettingsUI), or null while it has not arrived
    CourseData RaceCourse(out string owner, out string hostName)
    {
        owner = "";
        hostName = "The host";
        foreach (NetworkPlayer p in NetworkPlayer.All())
        {
            if (p == local || p.Object == null || !p.Object.IsValid || p.Object.HasStateAuthority) continue;
            if (p.lobbyGameMode.Length == 0 || p.lobbyCourse.Length == 0) continue;
            string text = p.LobbyCourseText();
            if (text != lastCourseText)
            {
                lastCourseText = text;
                lastCourse = CourseData.FromLobbyString(text);
                if (lastCourse != null) CourseLibrary.ApplyLobbyString(text);
            }
            owner = p.lobbyMapOwner.ToString();
            hostName = NameOf(p);
            return lastCourse;
        }
        return null;
    }

    // ---- Loading: the race's map is not here yet ----

    RectTransform loadCard;
    TextMeshProUGUI loadTitle;
    TextMeshProUGUI loadBody;
    RectTransform loadFill;
    bool loadClosed;

    // progress 0..100, or -1 when it is not known (the bar then slides back and forth)
    void ShowLoading(string title, string body, int progress)
    {
        view = View.Deciding;
        BuildCanvas();
        if (loadClosed) return;
        if (loadCard == null)
        {
            Image back = CourseUI.CardPanel("Loading the race", canvas.transform);
            loadCard = back.rectTransform;
            CourseUI.Place(loadCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 300f));
            loadTitle = CourseUI.Text("Title", loadCard, "", 36, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            CourseUI.Place(loadTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(680f, 52f));
            loadBody = CourseUI.Text("Body", loadCard, "", 22, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
            CourseUI.Place(loadBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(680f, 96f));
            Image track = CourseUI.Panel("Bar", loadCard, CourseUI.FieldBorder, false);
            CourseUI.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(600f, 18f));
            Image fill = CourseUI.Panel("Fill", track.transform, CourseUI.Accent, false);
            loadFill = fill.rectTransform;
            loadFill.offsetMin = Vector2.zero;
            loadFill.offsetMax = Vector2.zero;
            Button close = CourseUI.Button("Close", loadCard, "X", CourseUI.ButtonKind.Secondary, () =>
            {
                loadClosed = true;
                CloseLoading();
            }, 24);
            CourseUI.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -12f), new Vector2(44f, 44f));
            HoverTip.Add(close, "Close this");
        }
        loadTitle.text = title;
        loadBody.text = body;
        loadProgress = progress;
    }

    int loadProgress = -1;

    void AnimateLoadingBar()
    {
        if (loadFill == null) return;
        if (loadProgress >= 0)
        {
            loadFill.anchorMin = Vector2.zero;
            loadFill.anchorMax = new Vector2(Mathf.Clamp01(loadProgress / 100f), 1f);
            return;
        }
        // Not known how far: a piece of the bar slides back and forth
        float at = Mathf.PingPong(Time.unscaledTime * 0.6f, 0.7f);
        loadFill.anchorMin = new Vector2(at, 0f);
        loadFill.anchorMax = new Vector2(at + 0.3f, 1f);
    }

    void CloseLoading()
    {
        if (loadCard != null) Destroy(loadCard.gameObject);
        loadCard = null;
        loadFill = null;
        loadTitle = null;
        loadBody = null;
    }

    // ---- A normal race: the map follows a runner ----

    void ShowMap()
    {
        view = View.Map;
        BuildCanvas();
        Image back = CourseUI.CardPanel("Watching", canvas.transform);
        bar = back.rectTransform;
        CourseUI.Place(bar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(820f, 118f));

        Button previous = CourseUI.Button("Previous", bar, "<", CourseUI.ButtonKind.Secondary, () => Step(-1), 34);
        CourseUI.Place((RectTransform)previous.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(64f, 64f));
        HoverTip.Add(previous, "Watch the runner before");
        Button next = CourseUI.Button("Next", bar, ">", CourseUI.ButtonKind.Secondary, () => Step(1), 34);
        CourseUI.Place((RectTransform)next.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(64f, 64f));
        HoverTip.Add(next, "Watch the next runner");

        barTitle = CourseUI.Text("Title", bar, "", 30, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(barTitle.rectTransform, 96, 96, 12, 56);
        barTitle.richText = true;
        barHint = CourseUI.Text("Hint", bar, "Click a runner on the leaderboard to watch them.",
            18, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Stretch(barHint.rectTransform, 96, 96, 58, 8);
        Button hide = CourseUI.Button("Hide", bar, "X", CourseUI.ButtonKind.Secondary, () => { if (bar != null) bar.gameObject.SetActive(false); }, 22);
        CourseUI.Place((RectTransform)hide.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -4f), new Vector2(34f, 34f));
        HoverTip.Add(hide, "Hide this bar");

        // The leader to begin with
        List<NetworkPlayer> runners = LeaderboardManager.Standings();
        if (runners.Count > 0) Watch(runners[0]);
        RefreshBar();
    }

    CameraController Cam => local != null && local.agentMovement != null ? local.agentMovement.CamController : local != null ? local.GetComponent<CameraController>() : null;

    void Watch(NetworkPlayer runner)
    {
        CameraController cam = Cam;
        if (cam == null || runner == null) return;
        cam.Follow(runner.transform);
        nextRefresh = 0f;
    }

    // The runner watched now (also when picked on the leaderboard)
    NetworkPlayer Watched()
    {
        Transform watched = CameraController.Watched;
        NetworkPlayer runner = watched != null ? watched.GetComponent<NetworkPlayer>() : null;
        return NetworkPlayer.Races(runner) ? runner : null;
    }

    // "<" and ">": the runner before or after in the order of the race
    void Step(int by)
    {
        List<NetworkPlayer> runners = LeaderboardManager.Standings();
        if (runners.Count == 0) return;
        int at = runners.IndexOf(Watched());
        int index = at < 0 ? 0 : ((at + by) % runners.Count + runners.Count) % runners.Count;
        Watch(runners[index]);
    }

    void RefreshBar()
    {
        if (barTitle == null) return;
        List<NetworkPlayer> runners = LeaderboardManager.Standings();
        NetworkPlayer watched = Watched();
        // The runner watched has left: on to the leader
        if (watched == null && runners.Count > 0)
        {
            Watch(runners[0]);
            watched = runners[0];
        }
        int inCount = 0;
        foreach (NetworkPlayer p in runners) if (p.raceFinished) inCount++;
        string who = watched != null ? NameOf(watched) : "nobody yet";
        string state = runners.Count > 0 && inCount >= runners.Count ? "Everyone is in. The results come when the host shows them."
            : inCount + " of " + runners.Count + " in";
        barTitle.text = "Watching " + who + "   <size=70%><color=#5B7563>" + state + "</color></size>";
    }

    // ---- The card: the route choice game, route analysis, or no map here ----

    void ShowCard()
    {
        view = View.Card;
        if (cardClosed) return;
        BuildCanvas();
        if (bar != null) bar.gameObject.SetActive(false);
        if (card != null) return;
        Image back = CourseUI.CardPanel("Watching", canvas.transform);
        card = back.rectTransform;
        CourseUI.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 620f));
        cardTitle = CourseUI.Text("Title", card, "", 36, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(cardTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(800f, 56f));
        cardBody = CourseUI.Text("Body", card, "", 24, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        CourseUI.Stretch(cardBody.rectTransform, 40, 40, 96, 30);
        // The card can be closed: the one who watches may not want it in the way (2026-11-25, Anders)
        Button close = CourseUI.Button("Close", card, "X", CourseUI.ButtonKind.Secondary, CloseCard, 28);
        CourseUI.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(56f, 56f));
        HoverTip.Add(close, "Close this");
        cardBody.richText = true;
        cardBody.overflowMode = TextOverflowModes.Truncate;
        RefreshCard();
    }

    bool cardClosed;

    void CloseCard()
    {
        cardClosed = true;
        if (card != null) Destroy(card.gameObject);
        card = null;
        cardBody = null;
        cardTitle = null;
    }

    void RefreshCard()
    {
        if (cardBody == null) return;
        List<NetworkPlayer> players = new List<NetworkPlayer>();
        foreach (NetworkPlayer p in NetworkPlayer.All())
        {
            if (NetworkPlayer.Races(p)) players.Add(p);
        }
        StringBuilder text = new StringBuilder();
        if (!string.IsNullOrEmpty(cardNote)) text.Append("<color=#5B7563>").Append(cardNote).Append("</color>\n\n");

        int done = 0;
        if (RouteChoiceGame.On)
        {
            cardTitle.text = "Watching the route choice game";
            players.Sort((a, b) => b.choiceScore.CompareTo(a.choiceScore));
            int place = 0;
            foreach (NetworkPlayer p in players)
            {
                place++;
                if (p.choiceDone) done++;
                string points = RouteChoiceGame.Points(p.choiceScore) + " points";
                string state = p.choiceDone
                    ? "done, " + points + " (" + p.choiceRight + " of " + p.choiceQuestions + " right)"
                    : "playing, " + p.choiceAnswered + " answered, " + points;
                text.Append("<b>").Append(place).Append(". ").Append(NameOf(p)).Append("</b>   ").Append(state).Append('\n');
            }
        }
        else if (RouteAnalysis.On)
        {
            cardTitle.text = "Watching the route analysis";
            players.Sort((a, b) =>
            {
                if (a.analysisDone != b.analysisDone) return a.analysisDone ? -1 : 1;
                return AnalysisSeconds(a).CompareTo(AnalysisSeconds(b));
            });
            foreach (NetworkPlayer p in players)
            {
                if (p.analysisDone) done++;
                string state = p.analysisDone ? "done, " + CompetitionFlow.FormatTime(AnalysisSeconds(p)) + " with the standard runner" : "drawing the routes";
                text.Append("<b>").Append(NameOf(p)).Append("</b>   ").Append(state).Append('\n');
            }
        }
        else
        {
            cardTitle.text = "Watching the race";
            players.Sort((a, b) =>
            {
                if (a.raceFinished != b.raceFinished) return a.raceFinished ? -1 : 1;
                return Seconds(a).CompareTo(Seconds(b));
            });
            int place = 0;
            foreach (NetworkPlayer p in players)
            {
                string state;
                if (p.raceFinished)
                {
                    done++;
                    place++;
                    state = place + ".  " + CompetitionFlow.FormatTime(Seconds(p));
                }
                else state = p.relayLeg > 0 ? "out on leg " + (p.relayLeg + 1) : "out on the course";
                text.Append("<b>").Append(NameOf(p)).Append("</b>   ").Append(state).Append('\n');
            }
        }

        if (!string.IsNullOrEmpty(cardHeading)) cardTitle.text = cardHeading;
        text.Append('\n');
        if (players.Count > 0 && done >= players.Count) text.Append("<b>Everyone is done.</b> Waiting for the host to go on.");
        else text.Append(done).Append(" of ").Append(players.Count).Append(" done.");
        text.Append("\n<color=#5B7563>You joined while the game was on, so you watch this one. You play with everyone from the next game.</color>");
        cardBody.text = text.ToString();
    }

    static int AnalysisSeconds(NetworkPlayer p)
    {
        int total = 0;
        int legs = Mathf.Min(p.analysisLegCount, p.analysisLegTimes.Length);
        for (int i = 0; i < legs; i++) total += Mathf.Max(0, p.analysisLegTimes[i]);
        return total;
    }

    static int Seconds(NetworkPlayer p) => p.hourCount * 3600 + p.minuterCount * 60 + p.secoundCount;

    static string NameOf(NetworkPlayer p)
    {
        string name = p.nickName.ToString();
        return string.IsNullOrWhiteSpace(name) ? "Player " + p.Object.StateAuthority.PlayerId : name;
    }

    void BuildCanvas()
    {
        if (canvas != null) return;
        canvas = new GameObject("Spectator view", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        ScreenSettings.Fit(canvas.GetComponent<CanvasScaler>());
    }
}
