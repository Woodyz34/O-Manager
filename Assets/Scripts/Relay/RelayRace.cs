using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Runs a relay in the competition scene for the local player's team. Created by CourseLoader when the course is a relay.
//
// Every player is a team. Before the start the player picks one runner per leg. Then, leg by leg:
//   1. the player draws the whole leg, every stretch from the start to the finish, before anyone runs
//   2. when every team has drawn, the leg starts as a chasing start: each team's runner waits until the race clock
//      reaches the team's own time, so the gaps between the teams are the real gaps (like a changeover)
//   3. the runner runs the whole leg without stopping at the controls; every punch is recorded for the statistics
//   4. when every team's runner has finished the leg, everyone sees the leg's statistics (and can watch a replay)
//      with the clock stopped, until the host starts the next leg (or, after the last leg, shows the results)
// The team's time is the clock, which keeps counting through all the legs.
// Each leg has its own course; on forks each team gets its own branch (CourseData.AssignBranches).
public class RelayRace : MonoBehaviour, IRaceStats
{
    public static RelayRace Active { get; private set; }

    // The leg the local team is on, or -1 outside a relay. Other runners only matter on the same leg.
    public static int CurrentLegIndex => Active != null ? Active.leg : -1;

    CourseData course;
    MapDefinition map;
    PathfindingManager pathfinding;
    Transform pointParent;
    CourseRenderer courseRenderer;
    int[][] branches;               // [leg][fork]: the branch this team runs
    LegRoute route;                 // how this team runs the current leg
    int leg = -1;
    readonly List<CardData> picks = new List<CardData>();
    bool hangEnabled = true;
    bool raceStarted;               // from the moment every team has drawn: the hang-on choice is locked
    int legStartSeconds;

    // Statistics and replay
    public readonly RelayHistory history = new RelayHistory();
    public RelayHistory History => history;
    public bool IsRelay => true;
    RelayReplayRecorder recorder;
    RelayStatsUI statsUI;
    int statsShownForLeg = -1;
    public bool FinalResultsAllowed { get; private set; }

    // HUD
    GameObject hud;
    TextMeshProUGUI legText;
    TextMeshProUGUI statusText;
    Button hangButton;
    TextMeshProUGUI pickHeader;
    string legMessage = "";
    string waitMessage = "";

    public CourseData Course => course;
    public MapDefinition Map => map;
    public int LegCount => course.legs;
    public int Leg => leg;
    public LegRoute Route => route;
    public bool LegIsStraight => !course.LegForked(leg);
    public int[] BranchesOfLeg(int legIndex) => branches[Mathf.Clamp(legIndex, 0, branches.Length - 1)];

    public static RelayRace Create(CourseData course, MapDefinition map, PathfindingManager pathfinding, Transform pointParent)
    {
        course.Normalize();
        RelayRace race = new GameObject("Relay").AddComponent<RelayRace>();
        race.course = course;
        race.map = map;
        race.pathfinding = pathfinding;
        race.pointParent = pointParent;
        race.branches = course.AssignBranches(new System.Random());
        race.courseRenderer = new GameObject("Course").AddComponent<CourseRenderer>();
        race.recorder = race.gameObject.AddComponent<RelayReplayRecorder>();
        TvDirector.Create(race.gameObject, course, map);   // the TV of the TV controls, if the course has any
        Active = race;
        race.SetupLegPoints(0);
        Debug.Log($"RelayRace: {course.legs} legs, {course.relayCourses.Count} courses, {course.relayForks.Count} forks");
        return race;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        if (hud != null) Destroy(hud);
        if (statsUI != null) Destroy(statsUI.gameObject);
    }

    // ---- Points and course for one leg ----

    // Creates this team's start, controls and finish for the leg and draws them on the map
    void SetupLegPoints(int legIndex)
    {
        route = course.Route(legIndex, BranchesOfLeg(legIndex));
        CourseData legCourse = route.course;
        foreach (Transform child in pointParent) Destroy(child.gameObject);
        List<Transform> points = new List<Transform>();
        List<CoursePoint> coursePoints = legCourse.AllPoints();
        for (int i = 0; i < coursePoints.Count; i++)
        {
            Transform point = new GameObject(i == 0 ? "Start" : i == coursePoints.Count - 1 ? "Finish" : "Control " + i).transform;
            point.SetParent(pointParent, false);
            point.position = map.ToWorld(coursePoints[i]);
            points.Add(point);
        }
        pathfinding.controlPoints = points;
        pathfinding.MoveControlsOntoRunnableGround();   // nothing until the grid is loaded; PathfindingManager.Awake does leg 1
        pathfinding.startPoint = points[0].position;
        courseRenderer.Draw(map, legCourse);
    }

    // ---- Picking the runners ----

    // Called when the choose-runner panel opens at the start of the competition. The cards for leg 1 are made here
    // too, so the leg's women/men rule and the max average overall count from the first pick.
    public void BeginPicking()
    {
        picks.Clear();
        pool = null;
        ShowCardsForNextPick();
    }

    // A card was clicked. Returns true when every leg has a runner and the competition can go on.
    public bool PickRunner(CardDisplay card)
    {
        CardData data = card.CardData ?? RunnerCards.Find(card.runnerName);
        if (data == null || picks.Count >= course.legs) return false;
        // Only cards shown for this leg can be clicked, but check again: the leg's rule and what is left of the max
        if (!RunnerRules.Allowed(data, Pool(), picks, LegSexes, RunnerRules.Cap) && AnyAllowed())
        {
            UIToast.Show(data.RunnerName + " can't run leg " + (picks.Count + 1) + ".", 3f);
            return false;
        }
        picks.Add(data);
        if (picks.Count < course.legs)
        {
            ShowCardsForNextPick();
            return false;
        }
        RunnerFilterBar.ClearRules();
        if (pickHeader != null) pickHeader.transform.parent.gameObject.SetActive(false);
        return true;
    }

    // The runners the team picks from (without "Same stats" values): the player's own (and lent) runners in
    // "Your runners", the runners out of the packs in "Open packs", else every runner in the packs
    List<CardData> pool;
    List<CardData> Pool()
    {
        if (pool != null) return pool;
        if (RunnerRules.OpenPacksRound && RunnerRules.Opened.Count > 0) pool = new List<CardData>(RunnerRules.Opened);
        else if (pathfinding.YourRunnersMode) pool = RunnerRules.YourRunnersPool(LegSexes, RunnerRules.Cap);
        else pool = RunnerCards.InPacks();
        if (pool.Count == 0) pool = RunnerCards.InPacks();
        return pool;
    }

    List<LegSex> legSexes;
    List<LegSex> LegSexes => legSexes ?? (legSexes = RunnerRules.LegSexes(course));

    bool AnyAllowed()
    {
        return RunnerRules.AllowedList(Pool(), picks, LegSexes, RunnerRules.Cap).Count > 0;
    }

    void ShowCardsForNextPick()
    {
        CardManager cards = FindObjectOfType<CardManager>();
        if (cards == null) return;
        cards.ClearOffered();

        // Runners not picked yet that may run this leg (women's/men's leg, and with a max average overall the legs
        // after it must still be possible), or everyone again when there are more legs than runners.
        List<CardData> all = Pool();
        List<CardData> left = RunnerRules.AllowedList(all, picks, LegSexes, RunnerRules.Cap);
        // Nothing fits (should not happen: loans fill up "Your runners"): the leg's rule still counts if it can
        if (left.Count == 0) left = all.FindAll(c => RunnerRules.Fits(c, LegSexes[picks.Count]));
        if (left.Count == 0) left = new List<CardData>(all);
        // "Same stats": this leg's values (a relay can have other values on each leg)
        left = GameModes.WithSameStats(left, picks.Count);

        // A women's or men's leg shows only them; an open leg in "Open packs" shows both
        LegSex sex = LegSexes[picks.Count];
        RunnerFilterBar.SexRule = sex == LegSex.Women ? true : sex == LegSex.Men ? false : (bool?)null;
        RunnerFilterBar.BothSexes = sex == LegSex.Any && RunnerRules.OpenPacksRound;

        foreach (CardData data in left)
        {
            cards.Offer(data);
        }
        RunnerFilterBar.NotifyAll();
        RefreshPickHeader();
    }

    // "Leg 2: forked, 12 controls, 4.3 km" (along branch A of every fork)
    public string LegDescription(int legIndex)
    {
        CourseData first = course.Route(legIndex, null).course;
        return "Leg " + (legIndex + 1) + ": " + (course.LegForked(legIndex) ? "forked" : "straight") + ", "
            + first.controls.Count + " controls, " + CourseData.KmText(first.LengthMeters());
    }

    void RefreshPickHeader()
    {
        GameObject panel = pathfinding.ChooseRunnerPanel;
        if (panel == null) return;
        // The screen's own title says which leg we are picking for
        foreach (TextMeshProUGUI text in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.name == "Chose a runner text")
            {
                text.text = "CHOOSE THE RUNNER FOR LEG " + (picks.Count + 1) + " OF " + course.legs;
                text.enableWordWrapping = false;
                text.enableAutoSizing = true;
                text.fontSizeMin = 20;
                text.fontSizeMax = text.fontSize > 0 ? text.fontSize : 60;
            }
        }
        if (pickHeader == null)
        {
            Image back = CourseUI.Panel("Relay pick info", panel.transform, CourseUI.Card);
            CourseUI.Place(back.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(1240, 76));
            pickHeader = CourseUI.Text("Text", back.transform, "", 22, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Center);
            CourseUI.Stretch(pickHeader.rectTransform, 16, 16, 4, 4);
            pickHeader.enableWordWrapping = true;
            back.raycastTarget = false;
        }
        pickHeader.transform.parent.gameObject.SetActive(true);
        string team = picks.Count > 0 ? "\n<size=90%><color=#5B7563>Team so far:  " + PickedList() + "</color></size>" : "\n<size=90%><color=#5B7563>Speed decides how long a runner can hang on to someone faster.</color></size>";
        // Women's or men's leg, and the max average overall
        LegSex sex = LegSexes[Mathf.Clamp(picks.Count, 0, LegSexes.Count - 1)];
        string rules = sex != LegSex.Any ? "   <color=#E8742A>" + (sex == LegSex.Women ? "Women only" : "Men only") + "</color>" : "";
        string cap = RunnerRules.CapText(picks, course.legs);
        if (cap.Length > 0) rules += "   <color=#E8742A>" + cap + "</color>";
        // "Same stats": the values every runner gets on this leg
        Vector2Int? values = GameModes.SameStatsForLeg(picks.Count);
        string stats = values != null ? "   <color=#E8742A>Speed " + values.Value.x + ", technique " + values.Value.y + "</color>" : "";
        pickHeader.text = "<b><size=120%>" + LegDescription(picks.Count) + "</size></b>" + stats + rules + team;
    }

    string PickedList()
    {
        List<string> names = new List<string>();
        for (int k = 0; k < picks.Count; k++) names.Add((k + 1) + ": " + picks[k].RunnerName);
        return string.Join("   ", names);
    }

    public string RunnerOfLeg(int legIndex)
    {
        return picks.Count > 0 ? picks[Mathf.Clamp(legIndex, 0, picks.Count - 1)].RunnerName : "";
    }

    // ---- Legs ----

    // Everyone has picked their runners: draw the first leg
    public void StartFirstLeg(AgentMovement agent)
    {
        StartLeg(agent, 0);
    }

    void StartLeg(AgentMovement agent, int legIndex)
    {
        leg = legIndex;
        raceStarted = false;
        SetupLegPoints(leg);
        CardData runner = picks.Count > 0 ? picks[Mathf.Min(leg, picks.Count - 1)] : null;
        if (runner != null) agent.SetRunner(runner);

        agent.ResetForRelayLeg(pathfinding.controlPoints[0].position);
        if (agent.trailRenderer != null) agent.trailRenderer.Clear();

        PackRunning pack = agent.packRunning;
        if (pack == null)
        {
            pack = agent.GetComponent<PackRunning>();
            if (pack == null) pack = agent.gameObject.AddComponent<PackRunning>();
            agent.packRunning = pack;
        }
        pack.BeginLeg(LegIsStraight && hangEnabled);

        NetworkPlayer np = agent.GetComponent<NetworkPlayer>();
        if (np != null && np.Object != null && np.Object.HasStateAuthority)
        {
            np.relayRunnerName = runner != null ? runner.RunnerName : "";
            np.relaySplitLeg = leg;
            np.relaySplitCount = 0;
        }

        agent.manualPathDrawer.BeginRelayLeg();
        agent.CamController.FocusOnSegment(
            agent.WorldToGridPositionCentered(pathfinding.controlPoints[0].position),
            agent.WorldToGridPositionCentered(pathfinding.controlPoints[1].position));

        waitMessage = "";
        legMessage = "Draw the whole leg, from the start to the finish, before the start.";
        BuildHud();
        if (hud != null) hud.SetActive(true);
        RefreshHud();
        Debug.Log($"RelayRace: {LegDescription(leg)}, runner {(runner != null ? runner.RunnerName : "?")}");
    }

    // The drawer finished one stretch of the leg
    public void OnStretchDrawn(int stretchesDone)
    {
        int total = pathfinding.controlPoints.Count - 1;
        legMessage = stretchesDone < total
            ? "Draw the whole leg before the start: " + stretchesDone + " of " + total + " stretches drawn."
            : "Leg drawn. Waiting for the other teams to draw...";
        RefreshHud();
    }

    // Every team has drawn the leg: work out how long our runner waits for the changeover, then go
    public void OnAllDrawn(AgentMovement agent)
    {
        raceStarted = true;
        int ourTime = TeamSeconds(NetworkPlayer.Local);
        int firstTime = ourTime;
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid) continue;
            firstTime = Mathf.Min(firstTime, TeamSeconds(player));
        }
        int waitSeconds = Mathf.Max(0, ourTime - firstTime);
        legStartSeconds = ourTime;
        recorder.Begin(leg, agent);
        if (waitSeconds <= 0)
        {
            Go(agent);
            return;
        }
        StartCoroutine(WaitForChangeover(agent, waitSeconds));
    }

    IEnumerator WaitForChangeover(AgentMovement agent, int waitSeconds)
    {
        agent.relayHold = true;
        agent.canRunNextSegment = false;
        float until = Time.time + waitSeconds / PackRunning.GameSecondsPerSecond;
        while (Time.time < until)
        {
            int left = Mathf.CeilToInt((until - Time.time) * PackRunning.GameSecondsPerSecond);
            waitMessage = leg == 0
                ? "Starting in " + CompetitionFlow.FormatTime(left) + " (you took longer to draw)"
                : "Waiting for the changeover: " + CompetitionFlow.FormatTime(left) + " behind the leading team";
            RefreshHud();
            yield return null;
        }
        Go(agent);
    }

    void Go(AgentMovement agent)
    {
        agent.relayHold = false;
        agent.canRunNextSegment = true;
        // After drawing, the map still shows the last stretch: turn it to the first one
        if (agent.CamController != null) agent.CamController.FocusOnNextStretch();
        waitMessage = "";
        legMessage = "";
        RecordPunch(agent, 0);
        RefreshHud();
    }

    // The runner reached the control it was heading for
    public void OnControlReached(AgentMovement agent)
    {
        int last = pathfinding.controlPoints.Count - 1;
        RecordPunch(agent, agent.currentKontroll);
        if (agent.currentKontroll < last)
        {
            agent.currentKontroll++;
            if (agent.packRunning == null || !agent.packRunning.IsHanging)
            {
                agent.FollowOwnSegment(agent.currentKontroll - 1);
            }
            if (agent.CamController != null) agent.CamController.FocusOnNextStretch();
            return;
        }

        // Finished the leg
        if (agent.packRunning != null)
        {
            agent.packRunning.StopHanging();
            agent.packRunning.EndLeg();
        }
        NetworkPlayer np = agent.GetComponent<NetworkPlayer>();
        int finishSeconds = ClockSeconds(agent);
        int legSeconds = Mathf.Max(0, finishSeconds - legStartSeconds);
        if (np != null && np.Object != null && np.Object.HasStateAuthority)
        {
            np.relayLegTimes.Set(leg, legSeconds);
            np.relayTotals.Set(leg, finishSeconds);
        }
        if (leg >= course.legs - 1)
        {
            legMessage = "Finished! Leg " + (leg + 1) + " took " + CompetitionFlow.FormatTime(legSeconds) + ". Waiting for the other teams...";
            RefreshHud();
            agent.FinishRace();
            return;
        }
        legMessage = "Leg " + (leg + 1) + " done in " + CompetitionFlow.FormatTime(legSeconds)
            + ". Waiting for the other teams to finish the leg...";
        RefreshHud();
        if (pathfinding.multiplayer && np != null)
        {
            np.RPC_SetHasReachedControl();
            np.RPC_UpdateLeaderboardStep1();
            agent.WaitingForAllPlayersToReachKontroll = true;
        }
        else
        {
            OnAllFinishedLeg(agent);
        }
    }

    // The team clock at the start and at every common control (the ones every team runs) and the finish, for the split
    // times. index is the point's place in this team's route (0 = start); controls on fork branches are not recorded,
    // since the teams ran different ones.
    void RecordPunch(AgentMovement agent, int index)
    {
        NetworkPlayer np = agent.GetComponent<NetworkPlayer>();
        if (np == null || np.Object == null || !np.Object.HasStateAuthority || route == null) return;
        if (index < 0 || index >= route.commonOrdinal.Count) return;
        int ordinal = route.commonOrdinal[index];
        if (ordinal < 0 || ordinal >= np.relaySplits.Length) return;
        np.relaySplits.Set(ordinal, ClockSeconds(agent));
        np.relaySplitVariants.Set(ordinal, (byte)(route.segmentBranch[ordinal] + 1));
        np.relaySplitLeg = leg;
        np.relaySplitCount = Mathf.Max(np.relaySplitCount, ordinal + 1);
    }

    public static int ClockSeconds(AgentMovement agent)
    {
        Clock clock = agent.GetComponent<Clock>();
        if (clock == null) return TeamSeconds(agent.GetComponent<NetworkPlayer>());
        return clock.hourCount * 3600 + clock.minuterCount * 60 + Mathf.FloorToInt(clock.sekundercount);
    }

    public static int TeamSeconds(NetworkPlayer player)
    {
        if (player == null) return 0;
        return player.hourCount * 3600 + player.minuterCount * 60 + player.secoundCount;
    }

    // ---- Between the legs: statistics, then the host starts the next leg ----

    // Every team has finished the leg (NetworkPlayer saw everyone reach the finish)
    public void OnAllFinishedLeg(AgentMovement agent)
    {
        if (statsShownForLeg >= leg || statsDueForLeg >= leg) return;
        agent.relayHold = true; // the clock stands still while everyone looks at the statistics
        AskForStats(false);
    }

    // The statistics are due. They come up in Update, at once, or when the finish is on TV only once the runner
    // there has punched the finish unit and a second has gone (2026-11-14: they came up as the runner reached the
    // finish circle, about two seconds before the punch on TV).
    int statsDueForLeg = -1;
    bool statsDueFinal;
    float statsDueSince;
    const float StatsLongestWait = 10f;

    void AskForStats(bool final)
    {
        statsDueForLeg = leg;
        statsDueFinal = final;
        statsDueSince = Time.unscaledTime;
    }

    bool TvHoldsStats()
    {
        TvDirector tv = TvDirector.Active;
        if (tv == null) return false;
        float waited = Time.unscaledTime - statsDueSince;
        if (waited > StatsLongestWait) return false;
        // The TV notices a finished runner on its next update, so give it a moment
        if (waited < 0.3f) return true;
        return tv.FinishStillOnTheWay();
    }

    void ShowStats(bool final)
    {
        statsShownForLeg = leg;
        recorder.Stop();
        if (hud != null) hud.SetActive(false);
        if (statsUI == null)
        {
            statsUI = RelayStatsUI.Create(this);
        }
        statsUI.Open(leg, final, recorder.Tracks);
    }

    // The host pressed the button on the statistics screen
    public void HostContinue()
    {
        if (!CompetitionFlow.LocalIsHost()) return;
        if (leg >= course.legs - 1)
        {
            FinalResultsAllowed = true;
            CompetitionFlow.HostShowResults();
            return;
        }
        NetworkPlayer.Local.RPC_RelayStartLeg(leg + 1);
    }

    // Received by every player: the host started the next leg
    public void OnHostStartsLeg(int legIndex)
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || legIndex != leg + 1 || legIndex >= course.legs) return;
        history.Capture(this, leg);
        if (statsUI != null) statsUI.Close();
        AgentMovement agent = local.agentMovement;
        if (local.Object != null && local.Object.HasStateAuthority) local.hasDrawn = false;
        agent.relayHold = false;
        StartLeg(agent, legIndex);
    }

    // ---- HUD ----

    void BuildHud()
    {
        if (hud != null) return;
        GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
        if (canvas == null) return;
        // A compact box under the timer (top right), so the map and the control are never covered
        Image panel = CourseUI.CardPanel("Relay HUD", canvas.transform);
        hud = panel.gameObject;
        CourseUI.Place(panel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32, -112), new Vector2(330, 270));

        legText = CourseUI.Text("Leg", panel.transform, "", 20, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(legText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0, -12), new Vector2(-32, 118));
        legText.rectTransform.anchorMin = new Vector2(0f, 1f);
        legText.rectTransform.anchorMax = new Vector2(1f, 1f);
        legText.rectTransform.offsetMin = new Vector2(16, -134);
        legText.rectTransform.offsetMax = new Vector2(-16, -12);
        legText.alignment = TextAlignmentOptions.TopLeft;
        legText.enableWordWrapping = true;

        hangButton = CourseUI.Button("Hang", panel.transform, "", CourseUI.ButtonKind.Secondary, ToggleHang, 18);
        CourseUI.Place((RectTransform)hangButton.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -140), new Vector2(298, 44));

        statusText = CourseUI.Text("Status", panel.transform, "", 17, CourseUI.InkMuted);
        statusText.rectTransform.anchorMin = new Vector2(0f, 0f);
        statusText.rectTransform.anchorMax = new Vector2(1f, 0f);
        statusText.rectTransform.pivot = new Vector2(0.5f, 0f);
        statusText.rectTransform.offsetMin = new Vector2(16, 10);
        statusText.rectTransform.offsetMax = new Vector2(-16, 80);
        statusText.alignment = TextAlignmentOptions.TopLeft;
    }

    // The choice is made while drawing, never during the race
    void ToggleHang()
    {
        if (raceStarted) return;
        hangEnabled = !hangEnabled;
        NetworkPlayer local = NetworkPlayer.Local;
        PackRunning pack = local != null ? local.agentMovement.packRunning : null;
        if (pack != null) pack.HangAllowed = LegIsStraight && hangEnabled;
        RefreshHud();
    }

    void RefreshHud()
    {
        if (hud == null || leg < 0) return;
        CardData runner = picks.Count > 0 ? picks[Mathf.Min(leg, picks.Count - 1)] : null;
        string text = runner != null ? runner.RunnerName + "\n" : "";
        text += "<size=85%><color=#5B7563>Leg " + (leg + 1) + " of " + course.legs + "  ·  " + (LegIsStraight ? "straight" : "forked") + "\n"
            + CourseData.KmText(route.course.LengthMeters()) + "  ·  " + route.course.controls.Count + " controls</color></size>";
        if (runner != null)
        {
            text += "\n<size=85%><color=#5B7563>Physics " + Mathf.RoundToInt(runner.physics) + "  ·  Speed " + runner.SprintSpeedText + "  ·  Technique " + Mathf.RoundToInt(runner.technique) + "</color></size>";
        }
        legText.text = text;
        bool showHang = LegIsStraight && !raceStarted;
        hangButton.gameObject.SetActive(showHang);
        CourseUI.SetButtonText(hangButton, hangEnabled ? "Hang on: follow faster runners" : "Hang on: own route");
        CourseUI.SetButtonColor(hangButton, hangEnabled ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
    }

    void Update()
    {
        // After the last leg: the statistics come up once every team is in, before the results
        if (leg >= 0 && leg == course.legs - 1 && statsShownForLeg < leg && statsDueForLeg < leg && AllTeamsFinished(leg))
        {
            AskForStats(true);
        }
        if (leg >= 0 && statsDueForLeg == leg && statsShownForLeg < leg && !TvHoldsStats())
        {
            ShowStats(statsDueFinal);
        }
        if (CompetitionFlow.ResultsShown && statsUI != null && statsUI.IsOpen)
        {
            statsUI.Close();
        }

        if (hud == null || leg < 0) return;
        NetworkPlayer local = NetworkPlayer.Local;
        PackRunning pack = local != null && local.agentMovement != null ? local.agentMovement.packRunning : null;
        string text = waitMessage != "" ? waitMessage : legMessage;
        if (text == "" && pack != null) text = pack.Status;
        if (text == "" && pack != null && LegIsStraight)
        {
            text = hangEnabled ? "Running alone. You hang on to anyone who passes you." : "Running alone.";
        }
        statusText.text = text;
    }

    static bool AllTeamsFinished(int legIndex)
    {
        int count = 0;
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid) continue;
            count++;
            if (player.relayTotals[legIndex] <= 0) return false;
        }
        return count > 0;
    }
}
