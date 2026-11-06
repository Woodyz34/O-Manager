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
    [Networked] public string gameMode { get; set; }
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

    // Imposter mode. The host stores who the imposter is on its own player object when the game starts,
    // and every player stores their own guess at the end of the competition.
    [Networked] public PlayerRef imposter { get; set; }
    [Networked] public PlayerRef imposterGuess { get; set; }
    [Networked] public bool hasGuessedImposter { get; set; }

    // The host's lobby choices, published on the host's own player so everyone can see them
    [Networked] public NetworkString<_64> lobbyGameMode { get; set; }
    [Networked] public NetworkString<_64> lobbyMap { get; set; }
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
    // The extra game modes the host has turned on in the lobby (ExtraMode flags)
    [Networked] public int lobbyModes { get; set; }

    // The extra game modes of the competition being played, set by the host on its own player when the game
    // starts (ExtraMode flags + ExtraModes.KnownBit, 0 = not set)
    [Networked] public int competitionModes { get; set; }

    // Bears mode: where this player hid their bear (BearMode)
    [Networked] public NetworkBool bearPlaced { get; set; }
    [Networked] public Vector2 bearPosition { get; set; }

    // Move a control mode: the control this player moved and where to (MoveControlMode)
    [Networked] public NetworkBool moveUsed { get; set; }
    [Networked] public int moveControl { get; set; }
    [Networked] public Vector2 moveTarget { get; set; }

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

    void ClearModeChoices()
    {
        analysisDone = false;
        analysisLegCount = 0;
        choiceScore = 0;
        choiceRight = 0;
        choiceAnswered = 0;
        choiceQuestions = 0;
        choiceDone = false;
        bearPlaced = false;
        bearPosition = Vector2.zero;
        moveUsed = false;
        moveControl = -1;
        moveTarget = Vector2.zero;
    }

    // Sent by a runner who got scared by someone's bear: everyone sees it
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BearScare(PlayerRef owner, Vector2 where)
    {
        BearMode.OnScare(owner, where, this);
    }

    // Relays: where this team's runner is, so the other runners can run behind it or hang on to it (PackRunning)
    [Networked] public int relayLeg { get; set; }
    [Networked] public int relayControl { get; set; }
    [Networked] public NetworkBool relayRunning { get; set; }

    // Relay results, for the statistics between the legs (RelayRace, RelayStats)
    [Networked, Capacity(10)] public NetworkArray<int> relayLegTimes => default;      // seconds on each leg
    [Networked, Capacity(10)] public NetworkArray<int> relayTotals => default;        // team clock after each leg
    [Networked, Capacity(53)] public NetworkArray<int> relaySplits => default;        // team clock leaving the start, at every control and at the finish of relaySplitLeg
    [Networked, Capacity(52)] public NetworkArray<byte> relaySplitVariants => default; // the variant run at every control of relaySplitLeg
    [Networked] public int relaySplitLeg { get; set; }
    [Networked] public int relaySplitCount { get; set; }
    [Networked] public NetworkString<_32> relayRunnerName { get; set; }

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
        RelayRace relay = RelayRace.Active;
        if ((relay == null && IndividualRace.Active == null) || SceneManager.GetActiveScene().name == "Lobby")
        {
            relayLeg = -1;
            relayRunning = false;
            return;
        }
        relayLeg = relay != null ? relay.Leg : 0; // an individual competition is "leg 0" for the replay
        relayControl = agentMovement.currentKontroll;
        ManualPathDrawer drawer = agentMovement.manualPathDrawer;
        relayRunning = agentMovement.canRunNextSegment && !agentMovement.hasFinished && !agentMovement.relayHold
            && !agentMovement.WaitingForAllPlayersToReachKontroll && !agentMovement.WaitingForAllPlayersToDraw
            && drawer != null && !drawer.timeToDraw;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        PackRegistry.Remove(this);
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

    void Update()
    {

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
            if (p.Object == null || !p.Object.IsValid)
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

    public override async void Spawned()
    {
        DontDestroyOnLoad(gameObject);
        PackRegistry.Add(this);

        changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

        Debug.Log($"{Runner.LocalPlayer}: Spawned player {Object.Id} in scene {SceneManager.GetActiveScene().name}");
        TrailRenderer trailRenderer = GetComponent<TrailRenderer>();
        bool isReadyScene = SceneManager.GetActiveScene().name == "Lobby";
        Vector3 spawnPosition;
        if (isReadyScene)
        {
            Runner.SessionInfo.IsOpen = true;

            if (Runner.SessionInfo.MaxPlayers - Runner.LocalPlayer.PlayerId == 1)
            {
                spawnPosition = new Vector3(-1 * 3, 1, 0);
            }
            else
            {
                spawnPosition = new Vector3(Runner.LocalPlayer.PlayerId * 3, 1, 0);
            }
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
        Debug.Log("player: " + Runner.LocalPlayer.PlayerId + " position: " + transform.position + "in scene" + SceneManager.GetActiveScene().name);
        CameraController localCamera = GetComponent<CameraController>();
        if (Object.HasStateAuthority)
        {
            Local = this;
            RPC_SetNickName(GameManager.Instance.playerNickName);
            playerColor = PlayerColor.SavedPacked();
            ownedRunners = GameModes.OwnedRunnerCount();

            if (isReadyScene)
            {
                StartCoroutine(AlignCameraNextFrame());
                localCamera.cam.enabled = false;
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

        GameModes.SameStats = null;

        // (Set before any early return, so a mode without runners never keeps an old game's same stats)
        bool sameStats = GameModes.TryGetSameStats(effectiveGameMode, out int sameSpeed, out int sameTechnique);
        GameModes.SameStats = sameStats ? new Vector2(sameSpeed, sameTechnique) : (Vector2?)null;

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

        // "Same stats": everyone picks any runner, and all of them run with the host's speed and technique
        if (Object.HasStateAuthority && sameStats)
        {
            manager.ChooseWhomeverYouWantMode = true;
            manager.ChooseRunnerPanel.SetActive(true);
            UIToast.Show("Same stats: every runner has speed " + sameSpeed + " and technique " + sameTechnique
                + ". Pick whoever you like.", 8f);
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

        if (Object.HasStateAuthority && GameModes.IsYourRunners(effectiveGameMode))
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
        Debug.Log($"{Time.time} OnSceneLoaded: {scene.name}");

        if (scene.name != "Lobby")
        {
            // A new competition starts: nobody has finished or guessed yet
            raceFinished = false;
            ClearRelayResults();
            hasGuessedImposter = false;
            imposterGuess = PlayerRef.None;
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
                Vector3 spawnPosition;
                if (Runner.SessionInfo.MaxPlayers - Runner.LocalPlayer.PlayerId == 1)
                {
                    spawnPosition = new Vector3(-1 * 3, 1, 0);
                }
                else
                {
                    spawnPosition = new Vector3(Runner.LocalPlayer.PlayerId * 3, 1, 0);
                }
                StartCoroutine(AlignCameraNextFrame());
                gameMode = "";
                HasChoosenRunner = false;
                hasDrawn = false;
                hasReachedControl = false;
                raceFinished = false;
                imposter = PlayerRef.None;
                imposterGuess = PlayerRef.None;
                hasGuessedImposter = false;
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
        Debug.Log("Initilize after scene load körs av " + Local.name + " scen: " + SceneManager.GetActiveScene().name);
        
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


    private void CheckAllPlayersReady()
    {
        var players = FindObjectsOfType<NetworkPlayer>();
        bool allReady = true;
        foreach (var p in players)
        {
            if (p.Object == null || !p.Object.IsValid)
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
        else
        {
            drawer.timeToDraw = true;
        }
        Debug.Log($"{name}: Spelet startar nu!");
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
    public void RPC_ShowResults()
    {
        CompetitionFlow.OnShowResults();
    }

    // Sent by the host in imposter mode when the guessing is done: everyone plays the reveal
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RevealImposter()
    {
        CompetitionFlow.OnRevealImposter();
    }
}
