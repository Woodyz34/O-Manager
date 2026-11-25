using Fusion;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion.Analyzer;

public class NetworkPlayer : NetworkBehaviour, IPlayerLeft, IPackRunner
{
    ChangeDetector changeDetector;
    public static NetworkPlayer Local { get; set; }

    [Networked]  public int token {  get; set; }

    public TextMeshProUGUI playerNickName;

    [Networked]
    public NetworkString<_16> nickName { get; set; }

    public PathfindingManager manager;
    public AgentMovement agentMovement;
    public Clock clock;
    public Betting bet;

    [Networked] public Vector3 RestoredPosition { get; set; }
    [Networked] public bool HasRestoredPosition { get; set; }

    [Networked] public int currenKontroll { get; set; }

    [Networked] public bool hasFinished { get; set; }
    [Networked] public bool canRunNextSegment { get; set; }
    [Networked] public int currentTargetIndex { get; set; }
    [Networked] public int currentKontroll { get; set; }

    [Networked] public bool hasReachedControl { get; set; }

    [Networked] public bool hasDrawn { get; set; }
    [Networked] public bool HasChoosenRunner { get; set; }
    [Networked] public bool HasMadeABet { get; set; }
    [Networked] public bool IsInBetMode { get; set; }
    [Networked] public bool BetMode { get; set; }
    [Networked] public string BetPlayer { get; set; }
    [Networked] public int secoundCount { get; set; }
    [Networked] public int minuterCount { get; set; }
    [Networked] public int hourCount { get; set; }
    // 64 characters: "Same stats" with values for every relay leg is longer than the default 16
    [Networked, Capacity(128)] public string gameMode { get; set; }   // 128: Same stats with physics, speed and technique on 10 legs
    [Networked, Capacity(32)]
    public NetworkDictionary<int, PlayerRef> CardOwner => default;
    



    public List<Vector2Int> path { get; set; }

    public bool shouldMiss;

    public Transform ThisControll;
    public Transform targetControl;
    public bool isDrawing;
    public bool timeToDraw;


    [Networked]
    public int appearanceIndex { get; set; }

    // The player's own colour (0xRRGGBB + 1, 0 = none chosen), saved between games (PlayerColor)
    [Networked] public int playerColor { get; set; }

    // How many different runners this player owns, so the lobby can stop relays with more legs than that
    [Networked] public int ownedRunners { get; set; }

    // True when this player has reached the last control, so everyone can see who has finished
    [Networked] public bool raceFinished { get; set; }

    // A player who joined while the lobby was out racing (2026-11-24): only watches this race (Spectator) and races
    // from the next one. Spectators are left out of everything in the race: waiting for everyone, standings,
    // leaderboards, results, relays. Set by the player itself when it arrives in the middle of a race, cleared in the lobby.
    [Networked] public NetworkBool spectator { get; set; }

    // A player in the race: valid and not only watching. Use this wherever "every player" means "every runner".
    public static bool Races(NetworkPlayer p) => p != null && p.Object != null && p.Object.IsValid && !p.spectator;

    // True for the local player while it only watches the race
    public static bool LocalWatches => Local != null && Local.Object != null && Local.Object.IsValid && Local.spectator;

    // This player's object has been in the lobby (a late joiner's has not: it arrives straight in the race)
    bool beenInLobby;

    // Imposter mode. The host stores who the imposter is on its own player object when the game starts,
    // and every player stores their own guess at the end of the competition.
    [Networked] public PlayerRef imposter { get; set; }
    [Networked] public PlayerRef imposterGuess { get; set; }
    [Networked] public bool hasGuessedImposter { get; set; }
    // Secret task mode: the task everyone but the imposter has, set by the host (SecretTasks; 0 = none)
    [Networked] public int secretTask { get; set; }
    // Secret task mode: a caught imposter's guess at the task, set on the imposter's own player
    // (0 = not guessed yet, -1 = ran out of time, otherwise a SecretTasks.Task)
    [Networked] public int secretTaskGuess { get; set; }

    // The host's lobby choices, published on the host's own player so everyone can see them
    [Networked] public NetworkString<_128> lobbyGameMode { get; set; }
    [Networked] public NetworkString<_64> lobbyMap { get; set; }
    // This player's Unity player id (Unity Cloud), so a host can let the players in its lobby download its private map
    [Networked] public NetworkString<_64> cloudId { get; set; }
    // Set by the host when the lobby course is on a private map of its own: the host's Unity player id (PrivateMaps)
    [Networked] public NetworkString<_64> lobbyMapOwner { get; set; }
    // Set by the other players: the id of the host's private map once their copy is ready, and how far they are (0-100)
    [Networked] public NetworkString<_64> privateMapReady { get; set; }
    [Networked] public int privateMapProgress { get; set; }
    // The chosen course in compact text form (CourseData.ToLobbyString), so the others can run it even if it is not on their computer
    [Networked] public NetworkString<_512> lobbyCourse { get; set; }
    // A relay with several courses can be longer, so it continues in these
    [Networked] public NetworkString<_512> lobbyCourse2 { get; set; }
    [Networked] public NetworkString<_512> lobbyCourse3 { get; set; }
    [Networked] public NetworkString<_512> lobbyCourse4 { get; set; }

    const int LobbyChunk = 510;

    public string LobbyCourseText()
    {
        return lobbyCourse.ToString() + lobbyCourse2.ToString() + lobbyCourse3.ToString() + lobbyCourse4.ToString();
    }

    // Only writes the parts that changed. Text longer than the four parts is cut off (the builder stops that).
    public void SetLobbyCourseText(string text)
    {
        text = text ?? "";
        string Part(int k) => text.Length > k * LobbyChunk ? text.Substring(k * LobbyChunk, Mathf.Min(LobbyChunk, text.Length - k * LobbyChunk)) : "";
        if (lobbyCourse.ToString() != Part(0)) lobbyCourse = Part(0);
        if (lobbyCourse2.ToString() != Part(1)) lobbyCourse2 = Part(1);
        if (lobbyCourse3.ToString() != Part(2)) lobbyCourse3 = Part(2);
        if (lobbyCourse4.ToString() != Part(3)) lobbyCourse4 = Part(3);
    }
    // The host's lobby settings (HostSettings: TV production, race speed, bears and moves per leg), "" on the others
    [Networked] public NetworkString<_64> hostOptions { get; set; }
    // The player is drawing its route right now (the leaderboard shows the ones who are not done yet)
    [Networked] public NetworkBool drawingNow { get; set; }

    // The Monday relay (MondayRelayLobby). On the host's player: the three relays to vote on (MondayRelayLobby.Encode),
    // how far the lobby has come (MondayRelay.StateVoting ...) and the relay that won (1-3). On every player: its vote (1-3, 0 = none).
    [Networked] public NetworkString<_512> mondayChoices { get; set; }
    [Networked] public int mondayState { get; set; }
    [Networked] public int mondayPick { get; set; }
    [Networked] public int mondayVote { get; set; }

    // A lobby chat message (LobbyChat), sent from the writer's own player to everyone
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_Chat(string text)
    {
        LobbyChat.Receive(this, text);
    }

    // The host kicks a player (2026-11-19, someone went AFK): sent from the host's player to everyone, and the kicked
    // player leaves for the main menu
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_Kick(PlayerRef who)
    {
        if (Runner == null || who != Runner.LocalPlayer) return;
        // The Monday relay has no real host: nobody can be taken out
        if (MondayRelay.InSession(Runner)) return;
        UIToast.Show("The host took you out of the game.", 5f);
        SceneLoader.LeaveSession(Runner, "MainMenu");
    }
    // Relays: the host's race clock in game seconds while a leg is under way (RelayClock), so every team's clock
    // shows exactly the same time once its runner is off; negative = no leg under way
    [Networked] public float relayRaceClock { get; set; }
    [Networked] public int relayRaceClockTick { get; set; }
    // The extra game modes the host has turned on in the lobby (ExtraMode flags)
    [Networked] public int lobbyModes { get; set; }
    // Secret task tasks the host has switched off in the lobby (one bit per SecretTasks.Task; 0 = all tasks on)
    [Networked] public int lobbyTasksOff { get; set; }

    // The extra game modes of the competition being played, set by the host on its own player when the game
    // starts (ExtraMode flags + ExtraModes.KnownBit, 0 = not set)
    [Networked] public int competitionModes { get; set; }

    // Bears mode: the bears this player hid (BearMode): x and y on the map, z how big (1 = a normal bear, 2 = twice
    // the reach after saving one on a relay leg, 0 = no bear in that place)
    [Networked, Capacity(BearMode.MaxBears)] public NetworkArray<Vector3> bears => default;

    // Move a control mode: the controls this player moved (0 = no move in that place) and where to (MoveControlMode).
    // Up to three: the host picks how many moves everyone gets (2026-11-19)
    [Networked, Capacity(MoveControlMode.MaxMoves)] public NetworkArray<int> moveControls => default;
    [Networked, Capacity(MoveControlMode.MaxMoves)] public NetworkArray<Vector2> moveTargets => default;

    // Route analysis: this player's drawn routes, simplified (grid cells packed as row * columns + column), where each
    // leg ends in that list, and the time the standard runner would need on each leg (RouteAnalysis)
    [Networked, Capacity(800)] public NetworkArray<int> analysisPoints => default;
    [Networked, Capacity(52)] public NetworkArray<int> analysisLegEnds => default;
    [Networked, Capacity(52)] public NetworkArray<int> analysisLegTimes => default;
    [Networked] public int analysisLegCount { get; set; }
    [Networked] public NetworkBool analysisDone { get; set; }

    // Route choice game: this player's points, right answers, questions answered so far, and when done how many
    // questions there were (RouteChoiceGame)
    [Networked] public int choiceScore { get; set; }
    [Networked] public int choiceRight { get; set; }
    [Networked] public int choiceAnswered { get; set; }
    [Networked] public int choiceQuestions { get; set; }
    [Networked] public NetworkBool choiceDone { get; set; }
    // Run it yourself (3D, RunSession): where the runner is in the 3D world (map metres east, north), the way it
    // faces (degrees from north), its real-life speed, and RunSession.Flag bits (map up, fallen, in the air, loaded)
    [Networked] public Vector2 run3dAt { get; set; }
    [Networked] public float run3dYaw { get; set; }
    [Networked] public float run3dSpeed { get; set; }
    [Networked] public int run3dFlags { get; set; }
    // For watching a runner after the finish: the head's tilt (degrees, down positive), the leg the map shows and
    // how far it is turned, and how many controls are punched in order (for "Leg 3 of 7")
    [Networked] public float run3dPitch { get; set; }
    [Networked] public int run3dMapLeg { get; set; }
    [Networked] public float run3dMapTurn { get; set; }
    [Networked] public Vector2 run3dMapAt { get; set; }     // the map point in the middle of the sheet (zoomed and dragged)
    [Networked] public float run3dMapSize { get; set; }     // how far the map is zoomed out
    [Networked] public int run3dPunched { get; set; }
    // Per leg (index 1 = start to control 1): the route picked and the points (RouteChoiceGame.WriteLeg / ReadLeg)
    [Networked, Capacity(52)] public NetworkArray<int> choiceLegs => default;

    void ClearModeChoices()
    {
        analysisDone = false;
        analysisLegCount = 0;
        choiceScore = 0;
        choiceRight = 0;
        choiceAnswered = 0;
        choiceQuestions = 0;
        choiceDone = false;
        for (int i = 0; i < choiceLegs.Length; i++) choiceLegs.Set(i, 0);
        for (int i = 0; i < bears.Length; i++) bears.Set(i, Vector3.zero);
        for (int i = 0; i < moveControls.Length; i++)
        {
            moveControls.Set(i, 0);
            moveTargets.Set(i, Vector2.zero);
        }
    }

    // Sent by a runner who got scared by someone's bear: everyone sees it
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BearScare(PlayerRef owner, int slot, Vector2 where)
    {
        BearMode.OnScare(owner, slot, where, this);
    }

    // Relays: where this team's runner is, so the other runners can run behind it or hang on to it (PackRunning)
    [Networked] public int relayLeg { get; set; }
    [Networked] public int relayControl { get; set; }
    // How far along the leg the runner is, counted in common controls (the ones every team runs): 3.5 = half way from the
    // third common control to the next. On a fork two short controls and one long one both count as one step, so the
    // live standings are even again once the fork is done (2026-11-16)
    [Networked] public float relayProgress { get; set; }
    [Networked] public NetworkBool relayRunning { get; set; }
    [Networked] public NetworkBool relayMistake { get; set; }   // the runner is making a mistake right now, shown on the map of a TV production
    [Networked] public NetworkBool relayLost { get; set; }      // the runner is off the plan right now (no TV punch); relayMistake only once it is a real mistake
    // TV production of a relay with TV controls (TvDirector): what the host puts on TV. 0 = nothing (the race runs at
    // full pace), else TvDirector's number of the TV shot. Everyone slows the race down while something is on.
    [Networked] public int tvShot { get; set; }

    // Relay results, for the statistics between the legs (RelayRace, RelayStats)
    [Networked, Capacity(10)] public NetworkArray<int> relayLegTimes => default;      // seconds on each leg
    [Networked, Capacity(10)] public NetworkArray<int> relayTotals => default;        // team clock after each leg
    [Networked, Capacity(53)] public NetworkArray<int> relaySplits => default;        // team clock leaving the start, at every control and at the finish of relaySplitLeg
    [Networked, Capacity(52)] public NetworkArray<byte> relaySplitVariants => default; // the variant run at every control of relaySplitLeg
    [Networked] public int relaySplitLeg { get; set; }
    [Networked] public int relaySplitCount { get; set; }
    [Networked] public NetworkString<_32> relayRunnerName { get; set; }
    // The runner's physics, sprint speed and technique for the leaderboard: physics * 65536 + (speed + 1) * 256 +
    // technique, 0 = no runner yet (2026-11-16)
    [Networked] public int runnerStats { get; set; }

    // "P85  S90  T70" (physics, sprint speed, technique), or "" before a runner is picked
    public string RunnerStatsText
    {
        get
        {
            int packed = IsValidRunner ? runnerStats : 0;
            if (packed == 0) return "";
            int physics = packed >> 16, speed = ((packed >> 8) & 0xFF) - 1, technique = packed & 0xFF;
            return "P" + physics + "  S" + (speed >= 0 ? speed.ToString() : "-") + "  T" + technique;
        }
    }

    // A new competition: forget the last relay's results
    void ClearRelayResults()
    {
        for (int i = 0; i < relayLegTimes.Length; i++) relayLegTimes.Set(i, 0);
        for (int i = 0; i < relayTotals.Length; i++) relayTotals.Set(i, 0);
        relaySplitCount = 0;
        relaySplitLeg = -1;
        relayRunnerName = "";
    }

    // Sent by the host from the relay statistics screen: everyone starts drawing the next leg
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RelayStartLeg(int leg)
    {
        // Someone who only watches has no leg to draw
        if (LocalWatches) return;
        if (RelayRace.Active != null)
        {
            RelayRace.Active.OnHostStartsLeg(leg);
        }
    }

    public bool IsValidRunner => this != null && Object != null && Object.IsValid;
    public bool IsLocalRunner => Object != null && Object.IsValid && Object.HasStateAuthority;
    public Vector3 RunnerPosition => transform.position;
    public int RelayLeg => IsValidRunner ? relayLeg : -1;
    public int RelayControl => IsValidRunner ? relayControl : 0;
    public bool RelayRunning => IsValidRunner && relayRunning;
    public string RunnerLabel
    {
        get
        {
            string label = IsValidRunner ? nickName.ToString() : "";
            return string.IsNullOrWhiteSpace(label) ? "another runner" : label;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || agentMovement == null)
        {
            return;
        }
        string options = HostSettings.ToPublish;
        if (hostOptions.ToString() != options) hostOptions = options;
        if (spectator)
        {
            // Watching: nothing to tell the others about a race of its own
            if (relayLeg != -1) relayLeg = -1;
            if (relayRunning) relayRunning = false;
            if (drawingNow) drawingNow = false;
            return;
        }
        // Still drawing (the others see it on the leaderboard, 2026-11-19)
        ManualPathDrawer drawing = agentMovement.manualPathDrawer;
        bool drawingNowValue = ActiveScene.Name != "Lobby" && drawing != null && drawing.timeToDraw
            && !agentMovement.WaitingForAllPlayersToDraw && !agentMovement.WaitingForAllPlayersToChooseRunner && !CompetitionFlow.ResultsShown;
        if (drawingNow != drawingNowValue) drawingNow = drawingNowValue;
        float raceClock = RelayClock.ToPublish;
        if (relayRaceClock != raceClock)
        {
            relayRaceClock = raceClock;
            relayRaceClockTick = (int)Runner.Tick;
        }
        Vector3Int stats = agentMovement.RunnerStats;
        int packedStats = stats == Vector3Int.zero ? 0
            : (Mathf.Clamp(stats.x, 0, 255) << 16) | (Mathf.Clamp(stats.y + 1, 0, 255) << 8) | Mathf.Clamp(stats.z, 0, 255);
        if (runnerStats != packedStats) runnerStats = packedStats;
        RelayRace relay = RelayRace.Active;
        if ((relay == null && IndividualRace.Active == null) || ActiveScene.Name == "Lobby")
        {
            relayLeg = -1;
            relayRunning = false;
            relayMistake = false;
            relayLost = false;
            tvShot = 0;
            return;
        }
        relayLeg = relay != null ? relay.Leg : 0; // an individual competition is "leg 0" for the replay
        relayControl = agentMovement.currentKontroll;
        relayProgress = Progress(relay != null ? relay.Route : null, relayControl - 1); // currentKontroll is the control it runs to
        ManualPathDrawer drawer = agentMovement.manualPathDrawer;
        relayRunning = agentMovement.canRunNextSegment && !agentMovement.hasFinished && !agentMovement.relayHold
            && !agentMovement.WaitingForAllPlayersToReachKontroll && !agentMovement.WaitingForAllPlayersToDraw
            && drawer != null && !drawer.timeToDraw;
        bool lost = relayRunning && agentMovement.IsLost;
        relayLost = lost;
        relayMistake = MistakeWorthShowing(lost);
        tvShot = TvDirector.Active != null ? TvDirector.Active.ShotToSend : 0;
    }

    // The "Mistake" label on the map (2026-11-19): only for a real mistake, not every small turn the wrong way that is
    // put right a second later. The runner must have been off the plan for 3 seconds on the screen and 20 seconds of
    // race time, and the label goes as soon as the runner knows where it is again.
    float lostSinceReal = -1f, lostSinceRace;

    bool MistakeWorthShowing(bool lost)
    {
        if (!lost)
        {
            lostSinceReal = -1f;
            return false;
        }
        if (lostSinceReal < 0f)
        {
            lostSinceReal = Time.unscaledTime;
            lostSinceRace = Time.time;
        }
        return Time.unscaledTime - lostSinceReal >= 3f && (Time.time - lostSinceRace) * PackRunning.GameSecondsPerSecond >= 20f;
    }

    // The point index in the team's route (0 = start) as common controls: the last common point passed plus the part of
    // the way to the next common point, by the number of controls between them
    public static float Progress(LegRoute route, int index)
    {
        if (route == null || route.commonOrdinal.Count == 0) return index;
        int count = route.commonOrdinal.Count;
        if (index >= count - 1) return route.commonOrdinal[count - 1] >= 0 ? route.commonOrdinal[count - 1] : index;
        int before = Mathf.Clamp(index, 0, count - 1);
        while (before > 0 && route.commonOrdinal[before] < 0) before--;
        int after = Mathf.Clamp(index, 0, count - 1) + (index > before ? 0 : 1);
        while (after < count - 1 && route.commonOrdinal[after] < 0) after++;
        float from = Mathf.Max(0, route.commonOrdinal[before]);
        if (after <= before || route.commonOrdinal[after] < 0) return from;
        return from + (index - before) / (float)(after - before);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        PackRegistry.Remove(this);
        if (Local == this) RacePace.Normal();
    }



    void Awake()
    {
        agentMovement = GetComponent<AgentMovement>();
        clock = GetComponent<Clock>();
    }
    // Start is called before the first frame update
    void Start()
    {
       
    }

    // ---- Places in the lobby ----
    // Players stand in a row from the host, in the order they came in. When someone leaves, everyone after them moves
    // one step closer to the host, so there is never a gap in the row.

    public static Vector3 LobbySpot(NetworkRunner runner, PlayerRef player)
    {
        int index = 0;
        if (runner != null)
        {
            foreach (PlayerRef other in runner.ActivePlayers)
            {
                if (other.PlayerId < player.PlayerId) index++;
            }
        }
        return new Vector3((index + 1) * 3, 1, 0);
    }

    float nextSpotCheck;

    void Update()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || Runner == null) return;
        RacePace.Apply(agentMovement);
        WatchForStuckStart();
        if (Time.unscaledTime < nextSpotCheck) return;
        nextSpotCheck = Time.unscaledTime + 0.5f;
        if (ActiveScene.Name != "Lobby") return;
        Vector3 spot = LobbySpot(Runner, Runner.LocalPlayer);
        if ((transform.position - spot).sqrMagnitude < 0.01f) return;
        // Someone before us left: step into the free place
        TrailRenderer trail = GetComponent<TrailRenderer>();
        if (trail != null) trail.Clear();
        agentMovement.NetPosition = spot;
        transform.position = spot;
    }

    public override void Render()
    {
        foreach  (var change in changeDetector.DetectChanges(this, out  var previousBuffer, out var currentBUffer))
        {
            switch (change)
            {
                case nameof(nickName):
                    OnNickNameChanged();
                    break;
                case nameof(appearanceIndex):
                    UpdateAppearance();
                    break;
                case nameof(playerColor):
                    UpdateAppearance();
                    break;
                case nameof(spectator):
                    ApplySpectatorLook();
                    break;
            }
        }

        // I Shared Mode kan ingen peer starta spelet åt de andra, så varje spelare startar sig själv
        // när den ser att alla har valt löpare.
        if (Object.HasStateAuthority && HasChoosenRunner && agentMovement.WaitingForAllPlayersToChooseRunner)
        {
            CheckAllPlayersReady();
        }

        // Samma sak för att rita och för att nå en kontroll. Flaggan för den andra fasen nollställs här,
        // först när alla säkert har hunnit se den, så att ingen spelare missar den.
        if (Object.HasStateAuthority && hasDrawn && agentMovement.WaitingForAllPlayersToDraw && AllPlayers(p => p.hasDrawn))
        {
            hasReachedControl = false;
            RunAfterAllDrawnedLocally();
        }
        if (Object.HasStateAuthority && hasReachedControl && agentMovement.WaitingForAllPlayersToReachKontroll && AllPlayers(p => p.hasReachedControl))
        {
            hasDrawn = false;
            ResetAfterAllReachedLocally();
        }

        // Reports our own finish and, for the host, moves everyone on to the results when the competition is over
        if (Object.HasStateAuthority)
        {
            CompetitionFlow.Tick(this);
        }
    }

    private static bool AllPlayers(Func<NetworkPlayer, bool> condition)
    {
        foreach (var p in FindObjectsOfType<NetworkPlayer>())
        {
            // Spectators are not waited for
            if (!Races(p))
            {
                continue;
            }
            if (!condition(p))
            {
                return false;
            }
        }
        return true;
    }

    // The runner's three map sensors (RunnerMap, WhatRunnerAreStandingOn, WhatRunnerSees) count the stones, paths and
    // cliffs round the runner for the mistakes. Only the player's own runner uses them (the scripts on them do nothing
    // for the others), so on the other players' runners they are switched off: the physics engine then does not have
    // to follow what every other runner touches on the map, which is a lot of work on a big map with many players.
    void SenseTheMapOnlyIfOwn()
    {
        bool own = Object != null && Object.HasStateAuthority;
        foreach (Collider2D sensor in GetComponentsInChildren<Collider2D>(true))
        {
            if (sensor.GetComponent<RunnerMapCollision>() == null && sensor.GetComponent<RunnerCollision>() == null) continue;
            if (sensor.enabled != own) sensor.enabled = own;
        }
    }

    public override async void Spawned()
    {
        RunnerVisibility.Remember(this);
        DontDestroyOnLoad(gameObject);
        PackRegistry.Add(this);
        SenseTheMapOnlyIfOwn();

        changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        Debug.Log($"{Runner.LocalPlayer}: Spawned player {Object.Id} in scene {ActiveScene.Name}");
        TrailRenderer trailRenderer = GetComponent<TrailRenderer>();
        bool isReadyScene = ActiveScene.Name == "Lobby";
        Vector3 spawnPosition;
        if (isReadyScene)
        {
            Runner.SessionInfo.IsOpen = true;

            spawnPosition = LobbySpot(Runner, Runner.LocalPlayer);
            if (Object.HasStateAuthority)
            {
                agentMovement.NetPosition = spawnPosition;
            }

        }
        if (Object.HasStateAuthority)
        {
            // Bara servern ska sätta startpositionen
            transform.position = agentMovement.NetPosition;
            if (isReadyScene)
            {
                Runner.SessionInfo.IsOpen = true;
            }
        }
        else
        {
            // Klienten ska ta emot den från nätverket
            transform.position = agentMovement.NetPosition;
        }  
        Debug.Log("player: " + Runner.LocalPlayer.PlayerId + " position: " + transform.position + "in scene" + ActiveScene.Name);
        CameraController localCamera = GetComponent<CameraController>();
        if (Object.HasStateAuthority)
        {
            Local = this;
            RPC_SetNickName(GameManager.Instance.playerNickName);
            playerColor = PlayerColor.SavedPacked();
            ownedRunners = GameModes.RaceRunnerCount();

            if (isReadyScene)
            {
                beenInLobby = true;
                if (spectator) spectator = false;
            }
            else if (!beenInLobby && Runner.GameMode != GameMode.Single && ActiveScene.Name == CourseScenes.Competition
                && SessionRacing.IsRacing(Runner.SessionInfo))
            {
                // Joined while the lobby was out racing: watch this race, race the next one. (Playing the competition
                // scene straight from the editor makes a test lobby that is not marked as racing: that one is raced.)
                spectator = true;
            }

            if (isReadyScene)
            {
                StartCoroutine(AlignCameraNextFrame());
                localCamera.cam.enabled = false;
            }
            else if (spectator)
            {
                BeginSpectating(localCamera);
            }
            else
            {
                var agent = GetComponent<AgentMovement>();
                agent.WaitingForAllPlayersToChooseRunner = true;
                HasChoosenRunner = false;
                hasDrawn = false;
                hasReachedControl = false;
                manager = FindObjectOfType<PathfindingManager>(true);

                if (HasRestoredPosition)
                {
                    Vector3 desiredPos = RestoredPosition;
                    transform.position = desiredPos;
                    if (agentMovement != null)
                    {
                        agentMovement.SetInitialPosition(desiredPos);

                        agentMovement.hasFinished = hasFinished;
                        agentMovement.canRunNextSegment = canRunNextSegment;
                        agentMovement.currentTargetIndex = currentTargetIndex;
                        agentMovement.currentKontroll = currentKontroll;
                        agentMovement.path = path;
                        agentMovement.RunnerCollision.shouldMiss = shouldMiss;
                        agentMovement.manualPathDrawer.ThisControll = ThisControll;
                        agentMovement.manualPathDrawer.targetControl = targetControl;
                        agentMovement.manualPathDrawer.isDrawing = isDrawing;
                        agentMovement.manualPathDrawer.timeToDraw = timeToDraw;
                        agentMovement.CamController.FocusOnSegment(
                                 agentMovement.WorldToGridPositionCentered(agentMovement.pathfindingManager.controlPoints[currentKontroll-1].position),
                                 agentMovement.WorldToGridPositionCentered(agentMovement.pathfindingManager.controlPoints[currentKontroll].position)
                        );
                        Debug.Log("currenttagrgetindex "+agentMovement.currentTargetIndex);
                    }
                    Debug.Log($"Spawned local player using RestoredPosition: {desiredPos}");
                }
                else if (manager == null || manager.controlPoints == null || manager.controlPoints.Count == 0)
                {
                    // No course could be loaded (none chosen, or its map is missing): stop here instead of throwing
                    AbortRaceWithoutCourse();
                    return;
                }
                else
                {
                    // Fallback till normalt beteende (startpunkt)
                    transform.position = manager.controlPoints[0].transform.position;
                    if (agentMovement != null)
                    {
                        agentMovement.SetInitialPosition(manager.startPoint);
                    }
                    Debug.Log($"Spawned local player using manager.startPoint: {manager.startPoint}");
                }

                localCamera.cam.enabled = true;
                if(agentMovement.currentKontroll == 1)
                {
                    agentMovement.startView();
                }
                InitAfterSpawn();
            }
               
            Debug.Log("Spawned local player");

            
        }
        else
        {
            localCamera.cam.enabled = false;
            Debug.Log("Spawned remote player");
        }
        if (Object.HasStateAuthority)
        {
            Runner.SetPlayerObject(Object.InputAuthority, Object);
        }

        transform.name = $"P_{Object.Id}";
        trailRenderer.Clear();
        trailRenderer.enabled = true;

        // Ensure only local player sees their own drawing
        LineRenderer lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.enabled = Object.HasStateAuthority;
        }

        OnNickNameChanged();
        UpdateAppearance();
        ApplySpectatorLook();
    }

    // ---- Watching a race (a player who joined while the lobby was out racing) ----

    readonly List<Renderer> spectatorHiddenRenderers = new List<Renderer>();
    readonly List<Canvas> spectatorHiddenCanvases = new List<Canvas>();

    // A spectator has no runner on the map: its dot, trail and name tag are hidden for everyone, and it is not one of
    // the runners others can run behind (PackRegistry). Shown again when it is back in the lobby.
    void ApplySpectatorLook()
    {
        bool hide = IsValidRunner && spectator;
        if (hide)
        {
            PackRegistry.Remove(this);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                spectatorHiddenRenderers.Add(r);
            }
            foreach (Canvas c in GetComponentsInChildren<Canvas>(true))
            {
                if (c == null || !c.enabled) continue;
                c.enabled = false;
                spectatorHiddenCanvases.Add(c);
            }
            return;
        }
        if (spectatorHiddenRenderers.Count == 0 && spectatorHiddenCanvases.Count == 0) return;
        foreach (Renderer r in spectatorHiddenRenderers) if (r != null) r.enabled = true;
        foreach (Canvas c in spectatorHiddenCanvases) if (c != null) c.enabled = true;
        spectatorHiddenRenderers.Clear();
        spectatorHiddenCanvases.Clear();
        if (IsValidRunner) PackRegistry.Add(this);
        TrailRenderer trail = GetComponent<TrailRenderer>();
        if (trail != null) trail.Clear();
    }

    // The local player arrived in the middle of a race: no runner, no drawing; the camera and the watching are
    // set up by Spectator
    void BeginSpectating(CameraController localCamera)
    {
        AgentMovement agent = GetComponent<AgentMovement>();
        manager = FindObjectOfType<PathfindingManager>(true);
        HasChoosenRunner = true;
        hasDrawn = true;
        hasReachedControl = true;
        if (agent != null)
        {
            agent.WaitingForAllPlayersToChooseRunner = false;
            agent.WaitingForAllPlayersToDraw = false;
            agent.WaitingForAllPlayersToReachKontroll = false;
            agent.canRunNextSegment = false;
            if (agent.manualPathDrawer != null)
            {
                agent.manualPathDrawer.timeToDraw = false;
                agent.manualPathDrawer.isDrawing = false;
            }
            // Out of the way: at the start, hidden
            Vector3 at = manager != null && manager.controlPoints != null && manager.controlPoints.Count > 0
                ? manager.controlPoints[0].position : transform.position;
            agent.SetInitialPosition(at);
            transform.position = at;
        }
        if (manager != null && manager.ChooseRunnerPanel != null) manager.ChooseRunnerPanel.SetActive(false);
        if (localCamera != null && localCamera.cam != null) localCamera.cam.enabled = true;
        Debug.Log("NetworkPlayer: joined while the lobby is racing, watching this race");
        Spectator.Begin(this);
    }

    // The competition scene has no course to run: say so and go back to where the player started from
    void AbortRaceWithoutCourse()
    {
        Debug.LogError("NetworkPlayer: the competition has no course (none chosen or its map is missing)");
        UIToast.Show("You haven't chosen a course to run. Pick a course and try again.");
        if (Runner == null) return;
        if (Runner.GameMode == GameMode.Single)
        {
            SinglePlayerMenu.EndGame(Runner);
        }
        else if (Runner.IsServer || Runner.IsSharedModeMasterClient)
        {
            Runner.LoadScene("Lobby");
        }
    }

    // A player who never gets a runner (2026-11-19: a new player in their very first game, an Imposter round, sat on
    // the waiting page while everyone else waited for them). Whatever stopped it, after 10 seconds without a runner
    // and without the runner picker the picking is started again, a few times at most.
    float stuckSince = -1f;
    int stuckRetries;

    void WatchForStuckStart()
    {
        if (ActiveScene.Name == "Lobby" || agentMovement == null || manager == null)
        {
            stuckSince = -1f;
            stuckRetries = 0;
            return;
        }
        bool stuck = agentMovement.WaitingForAllPlayersToChooseRunner && !HasChoosenRunner
            && (manager.ChooseRunnerPanel == null || !manager.ChooseRunnerPanel.activeSelf)
            && !RouteAnalysis.On && !RouteChoiceGame.On && !RunnerRules.OpenPacksRound && !CompetitionFlow.ResultsShown;
        if (!stuck)
        {
            stuckSince = -1f;
            return;
        }
        if (stuckSince < 0f)
        {
            stuckSince = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - stuckSince < 10f || stuckRetries >= 3) return;
        stuckRetries++;
        stuckSince = Time.unscaledTime;
        string mode = GetSharedGameMode();
        Debug.LogWarning("NetworkPlayer: no runner after 10 s, starting the picking again (" + mode + ")");
        try
        {
            ShowChooseRunnerForMode(mode);
        }
        catch (Exception e)
        {
            Debug.LogError("NetworkPlayer: could not start the picking again: " + e);
        }
    }

    private void InitAfterSpawn()
    {
        Clock clock = GetComponent<Clock>();
        clock.Start();
        CameraController cam = GetComponent<CameraController>();
        cam.Awake();
        // Ensure we have a reference to the PathfindingManager for this peer
        if (manager == null)
        {
            manager = FindObjectOfType<PathfindingManager>(true);
        }

        // Show choose-runner UI for the local player (state authority in Shared Mode).
        // The gameMode is set on the starting player's object and might not have arrived yet on this peer,
        // so wait a few frames for it before deciding. If it's already present, continue immediately.
        string effectiveGameMode = GetSharedGameMode();
        Debug.Log("Shared gameMode: " + effectiveGameMode);

        if (Object.HasStateAuthority && (string.IsNullOrEmpty(effectiveGameMode) || !ExtraModes.Known))
        {
            // Wait for RPC or networked value to arrive (non-blocking)
            StartCoroutine(WaitForGameModeAndShow());
        }
        else
        {
            ShowChooseRunnerForMode(effectiveGameMode);
        }
    }

    // The game mode lives on the player object of whoever started the game, so look at all players
    // instead of only our own copy.
    private static string GetSharedGameMode()
    {
        if (!string.IsNullOrEmpty(SinglePlayerMenu.ChosenGameMode) && Local != null && Local.Runner != null && Local.Runner.GameMode == GameMode.Single)
        {
            return SinglePlayerMenu.ChosenGameMode;
        }
        foreach (var p in FindObjectsOfType<NetworkPlayer>())
        {
            if (p.Object == null || !p.Object.IsValid)
            {
                continue;
            }
            string mode = (p.localGameModeOverride != null) ? p.localGameModeOverride : p.gameMode;
            if (!string.IsNullOrEmpty(mode))
            {
                return mode;
            }
        }
        return null;
    }

    private System.Collections.IEnumerator WaitForGameModeAndShow()
    {
        int attempts = 0;
        string effective = GetSharedGameMode();
        // The extra game modes (bears, route analysis, ...) are shared the same way and may come a moment later
        while ((string.IsNullOrEmpty(effective) || !ExtraModes.Known) && attempts < 180)
        {
            attempts++;
            yield return null; // wait one frame
            effective = GetSharedGameMode();
        }

        ShowChooseRunnerForMode(effective);
    }

    private void ShowChooseRunnerForMode(string effectiveGameMode)
    {
        if (string.IsNullOrWhiteSpace(effectiveGameMode))
        {
            // Default to Your runners when no mode was received in time
            effectiveGameMode = GameModes.YourRunners;
        }

        // Imposter is an extra on top of either runner-choice mode. The intro shows itself only
        // if the host picked an imposter for this competition.
        if (Object.HasStateAuthority)
        {
            ImposterOverlay.ShowRoleIntro();
            // Bears and Move a control: the tools shown while drawing
            GameModeRuntime.StartIfNeeded(this);
        }

        // (Set before any early return, so a mode without runners never keeps an old game's same stats.
        // A relay gets one pair of values per leg.)
        bool sameStats = GameModes.TryGetSameStatsLegs(effectiveGameMode, out List<Vector2Int> sameStatsLegs);
        GameModes.SameStatsLegs = sameStats ? sameStatsLegs : null;
        GameModes.SameStatsSpeeds = sameStats ? GameModes.SameStatsSpeedsOf(effectiveGameMode) : null;
        // Max average overall and "Open packs" (RunnerRules)
        RunnerRules.BeginRound(effectiveGameMode);
        RunnerFilterBar.ClearRules();

        // Route analysis: nobody picks a runner, everyone runs with the same one
        if (Object.HasStateAuthority && ExtraModes.Is(ExtraMode.RouteAnalysis) && RelayRace.Active == null)
        {
            RouteAnalysis.Begin(this);
            return;
        }

        // Route choice game: nobody picks a runner either; the routes are timed with a speed-100 runner
        if (Object.HasStateAuthority && ExtraModes.Is(ExtraMode.RouteChoice) && RelayRace.Active == null)
        {
            RouteChoiceGame.Begin(this);
            return;
        }

        // Run it yourself (3D): nobody picks a runner; the player is the runner
        if (Object.HasStateAuthority && RunSession.On)
        {
            RunSession.Begin(this);
            return;
        }

        // "Open packs": first the packs, then the usual picking among the runners that came out of them
        if (Object.HasStateAuthority && RunnerRules.OpenPacksRound)
        {
            manager.ChooseRunnerPanel.SetActive(false);
            OpenPacks.Begin(RelayRace.Active != null ? RelayRace.Active.Course : null, () =>
            {
                if (this == null || manager == null) return;
                manager.ChooseRunnerPanel.SetActive(true);
                if (RelayRace.Active != null)
                {
                    RelayRace.Active.BeginPicking();
                }
                else
                {
                    // Women and men alike: the runner from the pack is shown whoever it is
                    RunnerFilterBar.BothSexes = true;
                    FindObjectOfType<CardManager>().ShowCards(RunnerRules.Opened);
                    RunnerFilterBar.NotifyAll();
                    if (IndividualRace.Active != null) IndividualRace.Active.ShowPickInfo(manager.ChooseRunnerPanel);
                }
            });
            return;
        }

        // "Same stats": nobody picks a runner (2026-11-15). Everyone runs as themselves, a runner with the player's
        // name and the host's speed and technique; on a women's relay leg a woman with the same values.
        if (Object.HasStateAuthority && sameStats)
        {
            manager.ChooseRunnerPanel.SetActive(false);
            PickSameStatsRunner();
            return;
        }

        if (Object.HasStateAuthority && effectiveGameMode.Contains("Choose a runner"))
        {
            Debug.Log("Choose runner startades");
            manager.ChooseRunnerOf3Mode = true;
            manager.ChooseRunnerPanel.SetActive(true);
        }
        else
        {
            Debug.Log("Choose runner startades inte " + Object.HasStateAuthority + " " + effectiveGameMode);
        }

        if (Object.HasStateAuthority && GameModes.PicksFromCollection(effectiveGameMode))
        {
            manager.YourRunnersMode = true;
            manager.ChooseRunnerPanel.SetActive(true);
        }

        if (Object.HasStateAuthority && effectiveGameMode.Contains("Choose whomever"))
        {
            Debug.Log("Choose whomever you want startades");
            manager.ChooseWhomeverYouWantMode = true;
            manager.ChooseRunnerPanel.SetActive(true);
        }
        else
        {
            Debug.Log("Choose whomever you want startades inte " + Object.HasStateAuthority + " " + effectiveGameMode);
        }

        CardManager cardManager = FindObjectOfType<CardManager>();
        if (manager.ChooseRunnerOf3Mode)
        {
            cardManager.ShowRandomCards(3);
        }
        if (manager.ChooseWhomeverYouWantMode)
        {
            cardManager.ShowAllCards();
        }
        if (manager.YourRunnersMode)
        {
            cardManager.ShowOwnedCards();
        }
        // Relays: one runner per leg, picked one after the other
        if (Object.HasStateAuthority && RelayRace.Active != null)
        {
            RelayRace.Active.BeginPicking();
        }
        // Individual competitions: show how long the course is
        if (Object.HasStateAuthority && IndividualRace.Active != null)
        {
            IndividualRace.Active.ShowPickInfo(manager.ChooseRunnerPanel);
        }
    }
    public void PlayerLeft(PlayerRef player)
    {
        if(player == Object.StateAuthority)
        {
            Runner.Despawn(Object);
        }
    }
    void OnDestroy()
    {
        PackRegistry.Remove(this);
        CameraController localCamera = GetComponent<CameraController>();
        if (localCamera != null)
        {
            Destroy(localCamera.cam);
                      
        }
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit); not a new race
        if (mode == LoadSceneMode.Additive) return;
        // The object can be destroyed by another scene-loaded handler earlier in the same call (e.g. when the
        // network session shuts down as the scene changes); Unity then still calls this handler
        if (this == null)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            return;
        }
        if (scene.name == "Lobby")
        {
            // Glöm förra omgångens spelläge så att nästa omgång inte läser ett gammalt värde
            localGameModeOverride = null;
        }
        if (Object == null || !Object.HasStateAuthority)
        {
            return;
        }
        if (scene.name == "Lobby")
        {
            // Back in the lobby: a spectator races from the next race on
            beenInLobby = true;
            if (spectator) spectator = false;
            ApplySpectatorLook();
        }
        Debug.Log($"{Time.time} OnSceneLoaded: {scene.name}");

        if (scene.name != "Lobby")
        {
            // A new competition starts: nobody has finished or guessed yet
            raceFinished = false;
            run3dFlags = 0;
            run3dPunched = 0;
            ClearRelayResults();
            hasGuessedImposter = false;
            imposterGuess = PlayerRef.None;
            secretTaskGuess = 0;
            ClearModeChoices();

            InitializeAfterSceneLoad();

            if (Object.HasStateAuthority)
            {
                Spawned();
            }
        }
        else
        {
            if (Object.HasStateAuthority)
            {
                Vector3 spawnPosition = LobbySpot(Runner, Runner.LocalPlayer);
                StartCoroutine(AlignCameraNextFrame());
                gameMode = "";
                HasChoosenRunner = false;
                hasDrawn = false;
                hasReachedControl = false;
                raceFinished = false;
                imposter = PlayerRef.None;
                imposterGuess = PlayerRef.None;
                hasGuessedImposter = false;
                secretTask = 0;
                secretTaskGuess = 0;
                competitionModes = 0;
                ClearModeChoices();
                agentMovement.NetPosition = spawnPosition;
                Debug.Log("Set spawn position for lobby: " + spawnPosition);
                transform.position = agentMovement.NetPosition;
            } 
        }
    }

    void InitializeAfterSceneLoad()
    {
        Debug.Log("Initilize after scene load körs av " + Local.name + " scen: " + ActiveScene.Name);
        
        CameraController cameraController = GetComponent<CameraController>();
        ManualPathDrawer manualPathDrawer = GetComponent<ManualPathDrawer>();
        MistakeLogic mistakeLogic = GetComponent<MistakeLogic>();
        Clock clock = GetComponent<Clock>();

        agentMovement.Awake();
        cameraController.Awake();
        manualPathDrawer.Awake();
        mistakeLogic.Start();
        agentMovement.Start();

        Debug.Log("Local player fully initialized after scene load");
    }

    private IEnumerator AlignCameraNextFrame()
    {
        // Vänta tills positionen är nätverkssynkad
        yield return null; // 1 frame
        Camera.main.transform.position = new Vector3(
            transform.position.x,
            Camera.main.transform.position.y,
            Camera.main.transform.position.z
        );
    }

    private void OnNickNameChanged()
    {
        Debug.Log($"NickName  changed for player to {nickName} for player {gameObject.name}");
        playerNickName.text = nickName.ToString();
    }

    [Rpc(RpcSources.InputAuthority,  RpcTargets.StateAuthority)]
    public void RPC_SetNickName(string nickName, RpcInfo info = default)
    {
        Debug.Log($"[Rpc] SetNickname {nickName}");
        this.nickName = nickName;
    }

    private void ResetAfterAllReachedLocally()
    {

        if (!Object.HasStateAuthority)
        {
            // Denna spelare tillhör inte denna peer
            return;
        }
        Debug.Log("ResetAfterAllReached (lokal spelare)");
        var agent = GetComponent<AgentMovement>();
        
        if (agent == null)
        {
            Debug.LogError($"{name}: AgentMovement saknas!");
            return;
        }



        // Nollställ värden lokalt
        agent.WaitingForAllPlayersToReachKontroll = false;
        agent.canRunNextSegment = false;

        // Relays: every team has finished the leg, so everyone looks at the leg's statistics until the host starts the next one
        if (RelayRace.Active != null)
        {
            RelayRace.Active.OnAllFinishedLeg(agent);
            return;
        }

        // Sätt upp nästa kontroll
        agent.manualPathDrawer.ThisControll = agent.pathfindingManager.controlPoints[agent.currentKontroll-1];
        agent.manualPathDrawer.targetControl = agent.pathfindingManager.controlPoints[agent.currentKontroll];
        agent.manualPathDrawer.timeToDraw = true;
        agent.CamController.FocusOnSegment(
            agent.WorldToGridPositionCentered(agent.pathfindingManager.controlPoints[agent.currentKontroll -1].position),
            agent.WorldToGridPositionCentered(agent.pathfindingManager.controlPoints[agent.currentKontroll].position)
        );

        Debug.Log($"{name}: RPC_ResetAfterAllReached kördes");
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetHasReachedControl()
    {
        hasReachedControl = true;
        Debug.Log($"{name} satte hasReachedControl = true på SERVERN");
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetHasDrawn()
    {
        hasDrawn = true;
        Debug.Log($"{name} satte hasDrawn = true på SERVERN");
    }

    private void RunAfterAllDrawnedLocally()
    {

        if (!Object.HasStateAuthority)
        {
            // Denna spelare tillhör inte denna peer
            return;
        }
        var agent = GetComponent<AgentMovement>();

        if (agent == null)
        {
            Debug.LogError($"{name}: AgentMovement saknas!");
            return;
        }

        // Move a control: the moved control flies to its new place first, then everyone runs
        if (MoveControlMode.InterceptAllDrawn(agent, ContinueAfterAllDrawn))
        {
            return;
        }
        ContinueAfterAllDrawn();
    }

    private void ContinueAfterAllDrawn()
    {
        Debug.Log("RunAfterdrawned (lokal spelare)");
        var agent = GetComponent<AgentMovement>();
        if (agent == null)
        {
            return;
        }
        agent.canRunNextSegment = true;
        agent.WaitingForAllPlayersToDraw = false;

        // Individual competitions: the first time everyone has drawn, the race starts (for the split times)
        if (IndividualRace.Active != null)
        {
            IndividualRace.Active.OnRaceStart(agent);
        }

        // Relays: the runner may have to wait for the changeover first
        if (RelayRace.Active != null)
        {
            RelayRace.Active.OnAllDrawn(agent);
        }

        Debug.Log($"{name}: RPC_RunAfterAllDrawned kördes");
    }


    // Körs lokalt av spelaren som äger objektet (state authority i Shared Mode).
    // Render() kollar sedan varje frame om alla spelare har valt.
    public void SetHasChoosenRunner()
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }
        HasChoosenRunner = true;

        Debug.Log($"{name} har valt löpare");
    }


    void PickSameStatsRunner()
    {
        string name = nickName.ToString();
        if (string.IsNullOrWhiteSpace(name)) name = GameManager.Instance != null ? GameManager.Instance.playerNickName : "Runner";
        if (RelayRace.Active != null)
        {
            RelayRace.Active.PickSameStatsTeam(name);
        }
        else
        {
            CardData card = GameModes.SameStatsRunner(name, 0, true);
            agentMovement.SetRunner(card);
            relayRunnerName = name;
        }
        SetHasChoosenRunner();
    }

    // "Change runner" on the waiting page: back to picking, as long as the race has not started for us
    public bool UnchooseRunner()
    {
        if (Object == null || !Object.HasStateAuthority || !HasChoosenRunner) return false;
        AgentMovement agent = GetComponent<AgentMovement>();
        if (agent == null || !agent.WaitingForAllPlayersToChooseRunner) return false;
        HasChoosenRunner = false;
        return true;
    }

    private void CheckAllPlayersReady()
    {
        var players = FindObjectsOfType<NetworkPlayer>();
        bool allReady = true;
        foreach (var p in players)
        {
            if (!Races(p))
            {
                continue;
            }
            if (!p.HasChoosenRunner)
            {
                allReady = false;
                break;
            }
        }

        if (allReady)
        {
            BetMode = false;
            Debug.Log($"är betMOde on {BetMode}");
            if (BetMode)
            {
                foreach (var p in players)
                {
                    Debug.Log(p.name + " TJOHO");
                    p.HasChoosenRunner = false;
                    p.IsInBetMode = true;
                    p.manager.BetOnRunnerPanel.SetActive(true);
                    p.bet = FindObjectOfType<Betting>();
                    p.bet.ShowAllBettingCards();
                }
            }
            else
            {
                Debug.Log("Alla spelare har valt löpare. Spelet kan starta!");
                StartGameLocally();
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetHasMadeBet(string playerName)
    {
        HasMadeABet = true;
        IsInBetMode = false;
        BetPlayer = playerName;

        Debug.Log($"{name} har bettat på {BetPlayer}");

        // Efter varje spelare kan vi checka om alla är redo
        CheckAllPlayersHasBet();
    }

    private void CheckAllPlayersHasBet()
    {
        var players = FindObjectsOfType<NetworkPlayer>();
        bool allReady = true;
        Debug.Log("players  antal " + players.Length);
        foreach (var p in players)
        {
            Debug.Log("player " + p.name + " has made bet " + p.HasMadeABet);
            if (!p.HasMadeABet)
            {
                allReady = false;
                break;
            }
        }

        if (allReady)
        {
            Debug.Log("Alla spelare har bettat. Spelet kan starta!");
            StartGameForAllPlayers();
           
        }
    }

    private void StartGameForAllPlayers()
    {
        var players = FindObjectsOfType<NetworkPlayer>();
        Debug.Log("players  antal " + players.Length);
        foreach (var p in players)
        {
            p.HasChoosenRunner = false;
            p.HasMadeABet = false;
            p.RPC_StartGame();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_StartGame()
    {
        // Denna kod körs på alla peers när alla är redo
        // t.ex. slå på input, starta klocka, låta spelare rita
        if (!Object.HasStateAuthority)
        {
            return;
        }
        StartGameLocally();
    }

    private void StartGameLocally()
    {
        var agent = GetComponent<AgentMovement>();
        var drawer = GetComponent<ManualPathDrawer>();
        GameObject WaitScreen = GameObject.FindGameObjectWithTag("WaitingForPlayersToConnect");
        if(WaitScreen  != null)
        {
            WaitScreen.SetActive(false);
        }
        agent.WaitingForAllPlayersToChooseRunner = false;
        if (RelayRace.Active != null)
        {
            RelayRace.Active.StartFirstLeg(agent);
        }
        else if (RouteAnalysis.On)
        {
            // Route analysis: the whole course is drawn first, and nobody runs
            RouteAnalysis.StartDrawing(agent);
        }
        else if (RouteChoiceGame.On)
        {
            // Route choice game: questions on the map, nobody draws or runs
            RouteChoiceGame.Start(agent);
        }
        else if (RunSession.On)
        {
            // Run it yourself: the race is run in the 3D world; the 2D runner stands still
            RunSession.StartRace(agent);
        }
        else
        {
            drawer.timeToDraw = true;
        }
        Debug.Log($"{name}: Spelet startar nu!");
        CourseData raced = CourseLibrary.CurrentOrDefault;
        GameAnalytics.Record("race_started",
            "mode", RelayRace.Active != null ? "relay" : RouteAnalysis.On ? "route_analysis" : RouteChoiceGame.On ? "route_choice" : "normal",
            "terrain", raced != null && raced.IsSprint() ? "sprint" : "forest",
            "players", FindObjectsOfType<NetworkPlayer>().Length);

    }


    public void UpdateAppearance()
    {
        var body = GetComponent<BodyHandler>();
        if (body == null) return;
        if (playerColor > 0) body.ApplyColor(PlayerColor.Unpack(playerColor));
        else body.ApplyAppearance(appearanceIndex);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetAppearance(int index)
    {
        appearanceIndex = index;
    }


    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetTimerSekunder(int sekunder)
    {
        secoundCount = sekunder;
    }
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetTimerMinuter(int minuter)
    {
        minuterCount = minuter;
    }
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetTimerHour(int hour)
    {
        hourCount = hour;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_UpdateLeaderboardStep2()
    {
        // RPC:n kan komma fram efter att banan har laddats ur (t.ex. när tävlingen avslutas i förtid)
        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.BuildLeaderboardUI();
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_UpdateLeaderboardStep1()
    {
        RPC_UpdateLeaderboardStep2();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_UpdateLeaderboardTimeStep2()
    {
        if (LeaderboardManager.Instance != null)
        {
            LeaderboardManager.Instance.UpdateLeaderboardTimesOnly();
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_UpdateLeaderboardTimeStep1()
    {
        RPC_UpdateLeaderboardTimeStep2();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ResetTimer()
    {
        secoundCount = 0;
        minuterCount = 0;
        hourCount = 0;
    }
    public void setGameMode(string GameMode)
    {
        // Set on this object (server/state-authority) and broadcast to all peers so clients also know the mode
        gameMode = GameMode;
        try
        {
            RPC_BroadcastGameMode(GameMode);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to RPC_BroadcastGameMode: {ex}");
        }
    }

    // Local override stored on each peer when server broadcasts the selected game mode
    private string localGameModeOverride = null;

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BroadcastGameMode(string mode)
    {
        localGameModeOverride = mode;
        Debug.Log($"[RPC] BroadcastGameMode received: {mode} on {name}");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SeeResult()
    {
        Clock clock = GetComponent<Clock>();
        Debug.Log(clock + " clock");
        clock.finishTime.gameObject.SetActive(false);
        clock.EndScreen.SetActive(false);
        clock.HideMultiPlayerEndScreen();

        ReturnToLobby returnToLobby = FindObjectOfType<ReturnToLobby>();

        if (BetMode == true)
        {
            returnToLobby.SeeBetResult();
        }
        else
        {
            returnToLobby.SeeResultWithoutBet();
        }
    }

    // Sent by the host from its own player: the competition is over, everyone opens the results screen
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowResults(int playedSeconds)
    {
        CompetitionFlow.OnShowResults(playedSeconds);
    }

    // Sent by the host in imposter mode when the guessing is done: everyone plays the reveal
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RevealImposter()
    {
        CompetitionFlow.OnRevealImposter();
    }
}
