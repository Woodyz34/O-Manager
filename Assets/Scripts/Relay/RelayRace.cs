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
    Image reserveFill;
    Button hangButton;
    TextMeshProUGUI pickHeader;
    string legMessage = "";
    string waitMessage = "";

    public CourseData Course => course;
    public MapDefinition Map => map;
    public int LegCount => course.legs;
    public int Leg => leg;
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
        pathfinding.startPoint = points[0].position;
        courseRenderer.Draw(map, legCourse);
    }

    // ---- Picking the runners ----

    // Called when the choose-runner panel opens at the start of the competition
    public void BeginPicking()
    {
        picks.Clear();
        RefreshPickHeader();
    }

    // A card was clicked. Returns true when every leg has a runner and the competition can go on.
    public bool PickRunner(CardDisplay card)
    {
        CardData data = card.CardData ?? RunnerCards.Find(card.runnerName);
        if (data == null) return false;
        picks.Add(data);
        if (picks.Count < course.legs)
        {
            ShowCardsForNextPick();
            return false;
        }
        if (pickHeader != null) pickHeader.transform.parent.gameObject.SetActive(false);
        return true;
    }

    void ShowCardsForNextPick()
    {
        CardManager cards = FindObjectOfType<CardManager>();
        if (cards == null) return;
        foreach (Transform child in cards.parent.transform) Destroy(child.gameObject);

        // Runners not picked yet, or everyone again when there are more legs than runners.
        // In "Your runners" only the player's own runners can be picked, each once.
        List<CardData> all = pathfinding.YourRunnersMode ? RunnerCollection.OwnedCards() : RunnerCards.All();
        if (all.Count == 0) all = RunnerCards.All();
        List<CardData> left = all.FindAll(c => !picks.Contains(c));
        if (left.Count == 0) left = new List<CardData>(all);
        bool showAll = pathfinding.ChooseWhomeverYouWantMode || pathfinding.YourRunnersMode;
        List<CardData> show = new List<CardData>(left);
        if (pathfinding.ChooseRunnerOf3Mode && !showAll)
        {
            show.Clear();
            while (show.Count < 3 && left.Count > 0)
            {
                int k = Random.Range(0, left.Count);
                show.Add(left[k]);
                left.RemoveAt(k);
            }
        }
        foreach (CardData data in show)
        {
            GameObject cardObject = Instantiate(cards.CardPrefab, cards.parent.transform);
            cardObject.GetComponent<CardDisplay>().SetValues(data);
        }
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
        pickHeader.text = "<b><size=120%>" + LegDescription(picks.Count) + "</size></b>" + team;
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
        if (statsShownForLeg >= leg) return;
        agent.relayHold = true; // the clock stands still while everyone looks at the statistics
        ShowStats(false);
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
        Image panel = CourseUI.CardPanel("Relay HUD", canvas.transform);
        hud = panel.gameObject;
        CourseUI.Place(panel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(900, 128));

        legText = CourseUI.Text("Leg", panel.transform, "", 30, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(legText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24, -12), new Vector2(620, 40));
        legText.enableWordWrapping = false;

        statusText = CourseUI.Text("Status", panel.transform, "", 22, CourseUI.InkMuted);
        CourseUI.Place(statusText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24, -54), new Vector2(620, 64));

        TextMeshProUGUI reserveLabel = CourseUI.Text("Reserve label", panel.transform, "Reserve", 18, CourseUI.InkMuted, FontStyles.Bold);
        CourseUI.Place(reserveLabel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24, -14), new Vector2(220, 24));
        Image reserveBack = CourseUI.Panel("Reserve", panel.transform, new Color(0.12f, 0.24f, 0.17f, 0.2f), false);
        CourseUI.Place(reserveBack.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24, -42), new Vector2(220, 16));
        reserveFill = CourseUI.Panel("Fill", reserveBack.transform, CourseUI.Forest, false);
        reserveFill.rectTransform.anchorMin = Vector2.zero;
        reserveFill.rectTransform.anchorMax = Vector2.one;
        reserveFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        reserveFill.rectTransform.offsetMin = Vector2.zero;
        reserveFill.rectTransform.offsetMax = Vector2.zero;

        hangButton = CourseUI.Button("Hang", panel.transform, "", CourseUI.ButtonKind.Secondary, ToggleHang, 20);
        CourseUI.Place((RectTransform)hangButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24, -68), new Vector2(220, 46));
    }

    void ToggleHang()
    {
        hangEnabled = !hangEnabled;
        NetworkPlayer local = NetworkPlayer.Local;
        PackRunning pack = local != null ? local.agentMovement.packRunning : null;
        if (pack != null)
        {
            pack.HangAllowed = LegIsStraight && hangEnabled;
            if (!hangEnabled && pack.IsHanging) pack.LetGo("Stopped hanging on", false);
        }
        RefreshHud();
    }

    void RefreshHud()
    {
        if (hud == null || leg < 0) return;
        CardData runner = picks.Count > 0 ? picks[Mathf.Min(leg, picks.Count - 1)] : null;
        legText.text = "Leg " + (leg + 1) + " of " + course.legs + (runner != null ? "  ·  " + runner.RunnerName : "")
            + "  <size=70%><color=#5B7563>" + (LegIsStraight ? "straight" : "forked") + "</color></size>";
        hangButton.gameObject.SetActive(LegIsStraight);
        CourseUI.SetButtonText(hangButton, hangEnabled ? "Hang on: ON" : "Hang on: OFF");
        CourseUI.SetButtonColor(hangButton, hangEnabled ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
    }

    void Update()
    {
        // After the last leg: the statistics come up once every team is in, before the results
        if (leg >= 0 && leg == course.legs - 1 && statsShownForLeg < leg && AllTeamsFinished(leg))
        {
            ShowStats(true);
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
            text = hangEnabled ? "Running alone. You will hang on to anyone who passes you." : "Running alone.";
        }
        statusText.text = text;
        float share = pack != null ? pack.ReserveShare : 1f;
        reserveFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(share), 1f);
        reserveFill.color = share > 0.5f ? CourseUI.Forest : share > 0.2f ? CourseUI.Accent : CourseUI.Danger;
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
