using Fusion;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using static GridData;
using Random = UnityEngine.Random;

public class AgentMovement : NetworkBehaviour
{
    public string runnerName;
    private Camera cam;
    public Camera cameraPrefab;
    [Networked] public Vector3 NetPosition { get; set; }

    // Another player's runner further than this from where it should be is moved there at once
    const float RemoteSnapDistance = 1f;
    [Networked] private Quaternion NetRotation { get; set; }
    public int currentKontroll = 1;
    private bool doingSlowMovingCoroutine = false;
    public bool useManualPath = true; // välj i Unity Inspector
    [HideInInspector]
    public bool AI = false; // välj i Unity Inspector
    public float runnerStatTec = 20;  //Detta ska ändras när jag har lagt till löpare
    [HideInInspector]  
    public List<Vector2Int> path;
    public float baseSpeed = 1; //Ska ändras när man väljer en löpare
    public float originalBaseSpeed;
    // Prefabens grundhastighet, innan någon löpares kort har räknats in. Sparas en gång eftersom
    // spelarobjektet lever kvar mellan tävlingarna och baseSpeed annars skulle krympa för varje ny tävling.
    private float unscaledBaseSpeed = -1f;
    // Samma sak för teknik-värdet (0-100) från prefaben, som räknas om till en bom-risk i Awake
    private float unscaledStatTec = -1f;
    public float SlowBaseSpeed;
    public bool hasFinished = false;
    [HideInInspector]
    public int currentTargetIndex = 0;
    private Vector3 currentTargetPosition;
    [HideInInspector]
    public PathfindingManager pathfindingManager;
    public TrailRenderer trailRenderer;
    private Clock clock;
    public ManualPathDrawer manualPathDrawer;
    public RunnerCollision RunnerCollision;
    public CameraController CamController;
   
    [HideInInspector]
    public bool isWaiting = false;
    // Reading the map on the run (forest mistakes): slower for a few seconds instead of standing still
    [System.NonSerialized] public float readingSlowUntil = -1f;
    [System.NonSerialized] public float readingSlowFactor = 1f;
    float ReadingFactor => Time.time < readingSlowUntil ? readingSlowFactor : 1f;
    private bool isInitialized = false;
    private Vector2Int lastGridCell;
    [HideInInspector]
    public float acceleration = 4f; // Enhet: units per second²
    private float acceleration2; // Enhet: units per second²
    public float currentSpeed = 0f; // Agentens aktuella hastighet

    private bool isMissingControl = false;
    private Vector3 missTarget;
    private float missDuration = 0f;
    private int missCount = 0;
    private bool shouldNotMiss = false;
    private float missDistanse = 2f;
    [HideInInspector]
    public bool canRunNextSegment = false;


    private bool DrawMiss = false;
    private bool isDrawMissing = false;

    public BoxCollider2D boxCollider;
    private Vector2 baseColliderSize;
    private float lastColliderCellSpeed = -1f;

    private bool isAngleMissing = false;
    private Vector3 angleMissDir;
    private Coroutine angleMissRoutine;
    public RunnerMapCollision angleMissCollider;

    private bool hasDoneADrawCloseMiss = false;
    private bool doDrawCloseMiss = false;
    private int maxBom = 0;

    private Vector2 lastPos;
    private bool findSafeObject= false;

    public bool WaitingForAllPlayersToReachKontroll = false;
    public bool WaitingForAllPlayersToDraw = false;
    public bool WaitingForAllPlayersToChooseRunner = false;

    // Relays (RelayRace): the drawn line of every stretch of the leg (stretch s runs from point s to point s + 1),
    // drafting and hanging on (PackRunning), and the wait for the changeover before the leg starts
    [HideInInspector] public List<List<Vector2Int>> relaySegments = new List<List<Vector2Int>>();
    [HideInInspector] public PackRunning packRunning;
    [HideInInspector] public bool relayHold = false;
    // Game modes: the runner stands still for a moment (scared by a bear). The clock keeps running.
    [HideInInspector] public bool modeHold = false;
    public bool IsMissing => isAngleMissing || isDrawMissing || isMissingControl || (forestMistakes != null && forestMistakes.Active);
    // Off the plan for real (not just working out a new path after reading in): the runner does not know where it is
    public bool IsLost => isAngleMissing || isDrawMissing || isMissingControl || (forestMistakes != null && forestMistakes.BlocksPunch);

    // Forest mistakes (Mistakes/ForestMistakes.cs): direction errors, parallel errors and reading in on the map
    [HideInInspector] public ForestMistakes forestMistakes;
    public Vector3 CurrentControlPosition => pathfindingManager.controlPoints[currentKontroll].position;
    public float UnscaledBaseSpeed => unscaledBaseSpeed > 0f ? unscaledBaseSpeed : baseSpeed;
    bool ForestMistakesApply => useManualPath && !AI && !(packRunning != null && packRunning.IsHanging);


    private static System.Random random = new System.Random();

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
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
        if (scene.name != "Lobby" && Object != null && Object.HasStateAuthority)
        {
            StartCoroutine(InitializeGameScene());
        }
        else if (scene.name == "Lobby")
        {
            // Om tävlingen avbryts mitt i ett lopp kan bom-coroutiner och rit-läget annars leva kvar in i lobbyn
            StopAllCoroutines();
            isWaiting = false;
            doingSlowMovingCoroutine = false;
            readingSlowUntil = -1f;
            if (originalBaseSpeed > 0f)
            {
                // En avbruten bom kan ha lämnat hastigheten sänkt
                baseSpeed = originalBaseSpeed;
            }
            if (manualPathDrawer != null)
            {
                manualPathDrawer.timeToDraw = false;
                manualPathDrawer.isDrawing = false;
            }
        }
    }

    private IEnumerator InitializeGameScene()
    {
        while (pathfindingManager == null)
        {
            pathfindingManager = FindObjectOfType<PathfindingManager>(true);
            yield return null;
        }

        if (manualPathDrawer == null)
        {
            manualPathDrawer = GetComponent<ManualPathDrawer>();
        }
        if (clock == null)
        {
            clock = GetComponent<Clock>();
        }

        SetInitialPosition(pathfindingManager.startPoint);
        InitializeAfterManagerFound();
        Start(); 
    }

    public void Awake()
    {
        if (unscaledBaseSpeed < 0f)
        {
            unscaledBaseSpeed = baseSpeed;
            unscaledStatTec = runnerStatTec;
        }
        WaitingForAllPlayersToDraw = false;
        WaitingForAllPlayersToReachKontroll = false;
        modeHold = false;
        hasFinished  = false;
        isWaiting = false;
        isDrawMissing = false;
        DrawMiss = false;
        canRunNextSegment = false;
        doingSlowMovingCoroutine = false;
        findSafeObject = false;
        isAngleMissing = false;
        isMissingControl = false;
        Debug.Log("agentmovemoen awake " + currentKontroll + " " + currentTargetIndex);
        currentTargetIndex = 0;
        currentKontroll = 1;
        hasDoneADrawCloseMiss = false;
        shouldNotMiss = false;
        isInitialized = false;
        Debug.Log("position" + transform.position);
        cam = Instantiate(cameraPrefab);
        CamController = GetComponent<CameraController>();
        trailRenderer = GetComponent<TrailRenderer>();
        CamController.cam = cam;
        if (CamController.cam != null)
        {
            Debug.Log("Player has a camera");
        }
        else
        {
            Debug.Log("Player has no camera");
        }
        if(ActiveScene.Name == "Lobby")
        {
            return;
        }
        runnerStatTec = (100 - unscaledStatTec) / 100;
        lastHeading = Vector3.zero;
        baseSpeed = unscaledBaseSpeed; // börja varje tävling från grundhastigheten
        originalBaseSpeed = baseSpeed;
        SlowBaseSpeed = originalBaseSpeed / 20;
        baseColliderSize = boxCollider.size;
        lastColliderCellSpeed = -1f;
        acceleration2 = acceleration;
        StartCoroutine(WaitForPathfindingManager());
        clock = GetComponent<Clock>();
        manualPathDrawer = GetComponent<ManualPathDrawer>();
    }

    private IEnumerator WaitForPathfindingManager()
    {
        // Vänta tills PathfindingManager dyker upp i scenen
        while (pathfindingManager == null)
        {
            Debug.Log("WaitingForPathFindingManager");
            if (ActiveScene.Name == "Lobby")
            {
                break;
            }
            pathfindingManager = FindObjectOfType<PathfindingManager>(true);
            yield return null; // vänta en frame
        }

        if (manualPathDrawer == null)
        {
            manualPathDrawer = GetComponent<ManualPathDrawer>();
        }
        if (clock == null)
        {
            clock = GetComponent<Clock>();
        }

        Debug.Log("PathfindingManager hittad! Fortsätter initiering...");
        InitializeAfterManagerFound();
    }

    private void InitializeAfterManagerFound()
    {
        if (pathfindingManager.multiplayer)
        {
            if (!AI && currentKontroll == 1)
            {
                startView();
            }
        }
        else
        {
            GameObject WaitScreen = GameObject.FindGameObjectWithTag("WaitingForPlayersToConnect");
            if (WaitScreen != null)
                WaitScreen.SetActive(false);
        }

        
        if (manualPathDrawer == null)
        {
            Debug.LogError("AgentPrefab saknar manualPathDrawer komponent!");
            return;
        }

        
    }


    public void Start()
    {
        if(pathfindingManager == null)
        {
            pathfindingManager = FindObjectOfType<PathfindingManager>(true);
        }
        if (clock == null)
        {
            clock = GetComponent<Clock>();
        }
        Debug.Log("agentmovemoen awake " + currentKontroll + " " + currentTargetIndex);
        if (ActiveScene.Name == "Lobby")
        {
            return;
        }
        if (!Object.HasStateAuthority)
        {
            return;
        }
        lastPos = transform.position;
        if (useManualPath)
        {
            if (pathfindingManager.multiplayer)
            {

            }
            else
            {
                manualPathDrawer.timeToDraw = true;
            }
                
        }
        else
        {
            // Din vanliga A*-kod
            List<Vector2Int> fullPath = new List<Vector2Int>();
            var pathfinder = new AStarPathfinder(pathfindingManager.speedGrid2D, pathfindingManager.slopeFactors);

            for (int idx = 0; idx < pathfindingManager.controlPoints.Count - 1; idx++)
            {
                pathfindingManager.start = pathfindingManager.controlPoints[idx];
                pathfindingManager.end = pathfindingManager.controlPoints[idx + 1];

                Vector2Int startPosition = WorldToGridPositionCentered(pathfindingManager.start.position);
                Vector2Int endPosition = WorldToGridPositionCentered(pathfindingManager.end.position);
                List<Vector2Int> segment = pathfinder.FindPath(startPosition, endPosition);
                if (segment == null)
                {
                    Debug.LogError($"Ingen väg hittades från {pathfindingManager.start} till {pathfindingManager.end}");
                    return;
                }
                pathfinder.ResetGrid();
                if (idx > 0 && segment.Count > 0)
                    segment.RemoveAt(0);

                fullPath.AddRange(segment);
            }
            canRunNextSegment = true;
            Initialize(fullPath);
        }
        trailRenderer.Clear();
    }

    public void Initialize(List<Vector2Int> newPath)
    {
        path.Clear();
        path = newPath;
        // A route that starts or ends on ground nobody can run on (the line was started a few metres from the control,
        // inside a building or a plot) would leave the runner standing still for good: its speed there is 0.
        // Those cells are left out; the runner runs straight from the control to the first open cell.
        if (path != null && pathfindingManager != null && pathfindingManager.speedGrid2D != null)
        {
            while (path.Count > 1 && !OpenCell(path[0])) path.RemoveAt(0);
            while (path.Count > 1 && !OpenCell(path[path.Count - 1])) path.RemoveAt(path.Count - 1);
        }
        ComputeTurns();
        currentTargetIndex = 0;
        ApplyTurn(0);   // the turn at a control, from the way the runner came in
        if (path != null && path.Count > 0)
            currentTargetPosition = GridToWorldPosition(path[currentTargetIndex]);
        isInitialized = true;
        if(path == null)
        {
            return;
        }
        lastGridCell = path[0];
        if (forestMistakes == null) forestMistakes = new ForestMistakes(this);
        forestMistakes.StartLeg();
    }

    // A path the forest mistakes picked (wrong direction, searching, back to the control). Keeps the mistake state.
    public void SetMistakePath(List<Vector2Int> newPath)
    {
        if (newPath == null || newPath.Count == 0) return;
        path = newPath;
        while (path.Count > 1 && !OpenCell(path[0])) path.RemoveAt(0);
        ComputeTurns();
        currentTargetIndex = 0;
        currentTargetPosition = GridToWorldPosition(path[0]);
        lastGridCell = path[0];
    }

    public IEnumerator FindPathAsync(Vector2Int start, Vector2Int goal, Action<List<Vector2Int>> onPathReady)
    {
        return CalculatePathAsync(start, goal, onPathReady, false);
    }

    void Update()
    {
        if (ActiveScene.Name == "Lobby")
        {
            // De andra spelarnas objekt ligger annars kvar där de stod när banan tog slut
            if (Object != null && Object.IsValid && !Object.HasStateAuthority && NetPosition != default)
            {
                transform.position = NetPosition;
            }
            return;
        }
        
        // I Shared Mode är det HasStateAuthority som bestämmer ägandeskap, inte HasInputAuthority.
        if (!Object.HasStateAuthority)
        {
            if (NetPosition == default)
                return;
            // Snapping här orsakade det hackiga utseendet. Lerp mjukar upp rörelsen för de andra spelarna.
            // A big gap is not running but a teleport (new scene, start of the course): jump straight there,
            // otherwise the runner slides across the map and drags its trail along.
            if ((NetPosition - transform.position).sqrMagnitude > RemoteSnapDistance * RemoteSnapDistance)
            {
                transform.position = NetPosition;
                return;
            }
            transform.position = Vector3.Lerp(transform.position, NetPosition, 15f * Time.deltaTime);
            return;
        }

        if (Object.HasStateAuthority || !pathfindingManager.multiplayer) // jag är ägare
        {
            //if (!pathfindingManager.multiplayer)
            //{
                //clock.UpdateClock(Time.deltaTime);
            //}
            if (WaitingForAllPlayersToReachKontroll == true)
            {
                return;
            }
            if (AI && manualPathDrawer.timeToDraw && !doingSlowMovingCoroutine)
            {
                doingSlowMovingCoroutine = true;
                StartCoroutine(handleSpeedWhenDrawing());
            }
            if (hasFinished)
            {
                return;
            }
            if (!isInitialized) return;

            // Vägen är slut men kontrollen är inte tagen. Det händer t.ex. när den ritade linjen slutar några
            // celler från kontrollen (kontrollen tas först inom 0.3 enheter). Lägg då till kontrollens cell
            // sist i vägen så att löparen springer den sista biten i stället för att bli stående.
            if (useManualPath && path != null && path.Count > 0 && currentTargetIndex >= path.Count
                && !isWaiting && !isAngleMissing && !manualPathDrawer.isDrawing && canRunNextSegment
                && !(packRunning != null && packRunning.IsHanging)
                && !(forestMistakes != null && forestMistakes.Blocking))
            {
                Vector2Int controlCell = WorldToGridPositionCentered(pathfindingManager.controlPoints[currentKontroll].position);
                if (path[path.Count - 1] != controlCell)
                {
                    path.Add(controlCell);
                }
            }

            if (relayHold || modeHold)
                return;
            bool hanging = packRunning != null && packRunning.IsHanging;
            if (isWaiting || (path == null && !useManualPath) || (currentTargetIndex >= path.Count && currentTargetIndex != 0) || manualPathDrawer.isDrawing || !canRunNextSegment)
                return;
            Vector3 pos = transform.position;
            Vector2Int nextGrid = path[currentTargetIndex];

            // Hämta world-position för nästa mål
            Vector3 targetWorld = GridToWorldPosition(nextGrid);
            Vector3 dirVector = (targetWorld - pos);
            float distanceToTarget = dirVector.magnitude;

            // Kontrollera om vi närmar oss en kontrollpunkt
            Vector3 controlPos = pathfindingManager.controlPoints[currentKontroll].position;
            float controlDistance = (controlPos - pos).magnitude;

            //AIMiss
            if (!isMissingControl && controlDistance < missDistanse && shouldNotMiss == false)
            {
                if (!useManualPath)
                {
                    if (missCount == 0)
                    {
                        if (Random.value < 0.25f) // 25% chans
                        {
                            isMissingControl = true;
                            missDuration = Random.Range(1f, 2f); // 1-2 sekunder
                            missTarget = controlPos + new Vector3(
                                Random.Range(-0.6f, 0.6f),
                                Random.Range(-0.6f, 0.6f),
                                0f
                            );
                        }
                        else
                        {
                            shouldNotMiss = true;
                        }
                    }
                    else
                    {
                        if (Random.value < 0.5f) // 50% chans
                        {
                            isMissingControl = true;
                            missDuration = Random.Range(1f, 1.5f); // 1-2 sekunder
                            missTarget = controlPos + new Vector3(
                                Random.Range(-0.6f, 0.6f),
                                Random.Range(-0.6f, 0.6f),
                                0f
                            );
                        }
                        else
                        {
                            shouldNotMiss = true;
                            int addIndex = (int)(missDistanse / pathfindingManager.cellSize);
                            currentTargetIndex = (currentTargetIndex + addIndex - (int)(addIndex / 4));
                            Debug.Log(missDistanse / pathfindingManager.cellSize);
                        }
                    }
                }

            }

            //AIMiss
            if (isMissingControl && !useManualPath)
            {
                acceleration = 0.5f;
                lastGridCell = nextGrid;
                nextGrid = WorldToGridPosition(transform.position);
                Vector3 missDir = (missTarget - pos).normalized;
                if (missDir.magnitude < 0.1f)
                {
                    if (missDuration < 0.3)
                    {

                    }
                    else
                    {
                        missDuration = 0.3f;
                    }
                }

                float cellSpeed2 = pathfindingManager.speedGrid2D[lastGridCell.y, lastGridCell.x];

                // Hämta lutningsfaktor från precomputade slopeFactors
                string dirName2 = GetDirectionName(lastGridCell, nextGrid);
                float slopeFactor2 = 1f;
                if (pathfindingManager.slopeFactors != null && pathfindingManager.slopeFactors.ContainsKey(dirName2))
                    slopeFactor2 = pathfindingManager.slopeFactors[dirName2][lastGridCell.y, lastGridCell.x]; // OBS: from-cellen


                // Kombinera bas-hastighet och lutning
                float targetSpeed2 = (baseSpeed * cellSpeed2) * slopeFactor2 * TerrainSpeedModel.RaceSpeedScale() * ReadingFactor
                                     * RunnerRules.SurfaceFactor(cardPhysics, cardSprintSpeed, cellSpeed2);

                // Lerp mot mål-hastighet för mjuk acceleration/deceleration
                float maxDelta2 = acceleration * Time.deltaTime;
                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed2, maxDelta2);

                // Flytta agent
                float steps = currentSpeed * Time.deltaTime;
                transform.position += missDir * steps;
                missDuration -= Time.deltaTime;
                if (missDuration <= 0f)
                {
                    missCount++;
                    isMissingControl = false; // återgå till rätt kontroll
                    acceleration = acceleration2;
                }
                else
                {
                    return; // hoppa över resten av Update tills bommen är klar
                }
            }
            if ((RunnerCollision.cantBäckMiss || RunnerCollision.cantStigMiss || RunnerCollision.cantBeståndsgränsMiss) && (isDrawMissing || isAngleMissing))
            {
                RecoverFromMiss(controlPos);
            }
            //DrawMissClose
            if (useManualPath && DrawMiss && !isDrawMissing && !hasDoneADrawCloseMiss)
            {
                isDrawMissing = true;
                // Starta coroutine som hanterar bommen
                StartCoroutine(HandleCloseFirstDrawMiss());
                DrawMiss = false; // reset så att vi inte startar flera gånger direkt
            }
            else if (useManualPath && isDrawMissing && (findSafeObject || (angleMissCollider.numberOfBigStones >= 1 && RunnerCollision.RunnerMapCollision.numberOfBigStones <= 4) || RunnerCollision.cantBeståndsgränsMiss || RunnerCollision.cantBäckMiss || (angleMissCollider.numberOfStones >= 1 && RunnerCollision.RunnerMapCollision.numberOfStones <= 4) || RunnerCollision.cantStigMiss))
            {
                StartCoroutine(RecoverFromMiss(controlPos));
            }
            Vector2 currentPos = transform.position;
            if (useManualPath && (isDrawMissing || isAngleMissing))
            {
                // Kolla om vi korsade en linje mellan förra och nuvarande pos
                int lineLayer = LayerMask.GetMask("LineLayer");
                RaycastHit2D[] hits = Physics2D.LinecastAll(lastPos, currentPos, lineLayer);
                foreach (var hit in hits)
                {
                    Debug.Log("Korsade linje: " + hit.collider.name);
                    HandleLineCollision(hit.collider.gameObject);
                }

            }
            lastPos = currentPos;
            // Hanging on to someone in a relay: punch the control on the way through, once the leader has.
            // Right at the flag (15 m) the runner punches anyway, and a runner that came within 30 m punches as soon
            // as the leader's punch is known (it can come late over the network), up to 60 m on.
            if (hanging)
            {
                if (hangControl != currentKontroll) { hangControl = currentKontroll; hangClosest = float.MaxValue; }
                hangClosest = Mathf.Min(hangClosest, controlDistance);
            }
            if (hanging && RelayRace.Active != null
                && (controlDistance <= 0.15f
                    || (packRunning.LeaderPassed(currentKontroll) && (controlDistance <= 0.3f || (hangClosest <= 0.3f && controlDistance <= 0.6f)))))
            {
                if (controlDistance > 0.3f || !packRunning.LeaderPassed(currentKontroll))
                    Debug.Log("PackRunning: punched control " + currentKontroll + " while hanging on, " + Mathf.RoundToInt(controlDistance * 100f) + " m from it");
                RelayRace.Active.OnControlReached(this);
                ClearMissState();
                if (hasFinished || WaitingForAllPlayersToReachKontroll) return;
                controlPos = pathfindingManager.controlPoints[currentKontroll].position;
                controlDistance = (controlPos - pos).magnitude;
            }
            // Om vi nått nästa gridcell (8 m, or less on a map with smaller cells, so a sprint runner keeps to its route)
            if (distanceToTarget < Mathf.Min(0.08f, pathfindingManager.cellSize * 0.8f))
            {
                lastGridCell = nextGrid;
                currentTargetIndex++;
                ApplyTurn(currentTargetIndex);
                if ((currentTargetIndex < path.Count) && useManualPath)
                    nextGrid = path[currentTargetIndex];

                // Off the plan (a forest mistake): no flag here, the mistake logic decides what comes next
                else if (useManualPath && forestMistakes != null && forestMistakes.Blocking)
                {
                    forestMistakes.OnPathEnd();
                    return;
                }
                // Kontrollpunkter
                else if ((controlDistance <= 0.3f))
                {
                    if (AI)
                    {
                        if (currentKontroll == pathfindingManager.controlPoints.Count - 1)
                        {

                        }
                        else
                        {
                            currentKontroll++;
                        }
                    }
                    else if (RelayRace.Active != null)
                    {
                        // Relays run the whole leg without stopping: next stretch, end of the leg or end of the race
                        RelayRace.Active.OnControlReached(this);
                    }
                    else
                    {
                        // Individual competitions: the split time at this control (or the finish)
                        if (IndividualRace.Active != null)
                        {
                            IndividualRace.Active.OnPunch(this, currentKontroll);
                        }
                        if (currentKontroll == pathfindingManager.controlPoints.Count - 1)
                        {
                            FinishRace();
                        }
                        else
                        {

                            
                            currentKontroll++;
                            if (useManualPath)
                            {
                                
                                if (pathfindingManager.multiplayer)
                                {
                                    
                                    NetworkPlayer np = GetComponent<NetworkPlayer>();
                                    np.RPC_SetHasReachedControl();
                                    np.RPC_UpdateLeaderboardStep1();
                                    WaitingForAllPlayersToReachKontroll = true;
                                    Debug.Log("Paleyr " + np.name + " har kommit till en kontroll");
                                    if (ActiveScene.Name == "Lobby")
                                        return;
                                    Debug.Log("Scenen är inte Lobby");
                                }
                                else
                                {
                                    canRunNextSegment = false;
                                    manualPathDrawer.ThisControll = pathfindingManager.controlPoints[currentKontroll-1];
                                    manualPathDrawer.targetControl = pathfindingManager.controlPoints[currentKontroll];
                                    manualPathDrawer.timeToDraw = true;
                                    CamController.FocusOnSegment(
                                        WorldToGridPositionCentered(controlPos),
                                        WorldToGridPositionCentered(pathfindingManager.controlPoints[currentKontroll].position)
                                    );
                                }
                                    
                            }
                            

                            

                        }
                    }
                    shouldNotMiss = false;
                    missCount = 0;
                    maxBom = 0;
                    isAngleMissing = false;
                    isDrawMissing = false;
                }
                // Uppdatera targetWorld och dirVector
                targetWorld = GridToWorldPosition(nextGrid);
                dirVector = (targetWorld - pos);
                distanceToTarget = dirVector.magnitude;
            }

            dirVector.Normalize();

            // Hämta base speed från grid
            // (2026-11-19: an IndexOutOfRangeException here; a cell outside the grid, from a route made on another
            // map or a moved control at the edge, is pulled back onto the grid instead of stopping the runner's Update)
            float[,] speedGrid = pathfindingManager.speedGrid2D;
            lastGridCell = new Vector2Int(Mathf.Clamp(lastGridCell.x, 0, speedGrid.GetLength(1) - 1), Mathf.Clamp(lastGridCell.y, 0, speedGrid.GetLength(0) - 1));
            float cellSpeed = speedGrid[lastGridCell.y, lastGridCell.x];
            // Never stand still for good on a closed cell (speed 0) that slipped into the route: walk off it slowly
            if (cellSpeed <= 0.01f) cellSpeed = 0.2f;
            // Skala collidern efter cellSpeed2
            if (boxCollider != null && !Mathf.Approximately(cellSpeed, lastColliderCellSpeed))
            {
                // Direkt proportionell skalning. Sätts bara när värdet ändras, eftersom fysiken
                // bygger om collidern varje gång storleken skrivs.
                lastColliderCellSpeed = cellSpeed;
                boxCollider.size = baseColliderSize * cellSpeed;
            }
            // Hämta lutningsfaktor från precomputade slopeFactors
            string dirName = GetDirectionName(lastGridCell, nextGrid);
            float slopeFactor = 1f;
            if (pathfindingManager.slopeFactors != null && pathfindingManager.slopeFactors.TryGetValue(dirName, out float[,] slopeGrid))
            {
                if (lastGridCell.y < slopeGrid.GetLength(0) && lastGridCell.x < slopeGrid.GetLength(1))
                    slopeFactor = slopeGrid[lastGridCell.y, lastGridCell.x]; // OBS: from-cellen
            }

            // Uphill, downhill, too steep and the kind of ground are all in the slope factor now (TerrainSpeedModel)

            // Kombinera bas-hastighet och lutning
            // Sprint courses run slower than the grid alone says (see TerrainSpeedModel.SprintSpeedScale)
            float targetSpeed = (baseSpeed * cellSpeed) * slopeFactor * TerrainSpeedModel.RaceSpeedScale() * ReadingFactor;
            // On asphalt, gravel and paths the runner's sprint speed counts instead of (or as well as) physics
            targetSpeed *= RunnerRules.SurfaceFactor(cardPhysics, cardSprintSpeed, cellSpeed);
            // Relays: a runner just ahead helps, hanging on sets the pace, and a tired runner is slower
            if (packRunning != null)
            {
                targetSpeed *= packRunning.SpeedMultiplier(targetSpeed);
            }

            // Lerp mot mål-hastighet för mjuk acceleration/deceleration
            // After a sharp turn the runner gets back up to speed more slowly (see ComputeTurns)
            if (turnRecovering && currentSpeed >= targetSpeed - 0.001f) turnRecovering = false;
            float maxDelta = (turnRecovering ? Mathf.Min(acceleration, TurnAcceleration) : acceleration) * Time.deltaTime;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, maxDelta);

            // Flytta agent
            float step = currentSpeed * Time.deltaTime;
            // Small cells (2 m on sprint maps): run on through every cell this frame's step reaches instead of
            // stopping at each one, which would cap the speed at one cell a frame. The last cell of the path is left
            // to the check above, which also handles the controls.
            while (step > distanceToTarget && useManualPath && path != null && currentTargetIndex + 1 < path.Count
                   && !isDrawMissing && !isAngleMissing && !isMissingControl)
            {
                transform.position = new Vector3(targetWorld.x, targetWorld.y, transform.position.z);
                step -= distanceToTarget;
                lastGridCell = nextGrid;
                currentTargetIndex++;
                ApplyTurn(currentTargetIndex);
                nextGrid = path[currentTargetIndex];
                targetWorld = GridToWorldPosition(nextGrid);
                dirVector = targetWorld - transform.position;
                distanceToTarget = dirVector.magnitude;
                dirVector = distanceToTarget > 0.0001f ? dirVector / distanceToTarget : Vector3.zero;
            }
            if (step > distanceToTarget) step = distanceToTarget;
            transform.position += dirVector * step;
            if (dirVector.sqrMagnitude > 0.001f) lastHeading = dirVector;

            // Fixera Z
            Vector3 posFixed = transform.position;
            posFixed.z = -1f;
            transform.position = posFixed;
            // Rikta agenten i rörelseriktningen
            if (dirVector.sqrMagnitude > 0.001f)
            {
                transform.up = dirVector;   // eller transform.right = dirVector;
            }
            if (forestMistakes != null && ForestMistakesApply)
            {
                forestMistakes.Tick(pos, transform.position);
            }
        }
        else
        {
            
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
        {
            if (NetPosition == default)
                return;
            // Smidigare synk än direkt snapping om vi ändå gör detta manuellt:
            // transform.position = Vector3.Lerp(transform.position, NetPosition, 15f * Runner.DeltaTime);
        }
        
        if (Object.HasStateAuthority)
        {
            // I Shared Mode kan klienten skriva direkt till [Networked] properties på objekt de äger!
            // Vi behöver ingen RPC.
            NetPosition = transform.position;
            
            if (clock == null)
            {
                clock = GetComponent<Clock>();
            }
        }
    }

    public void SetInitialPosition(Vector3 pos)
    {
        NetPosition = pos;
        transform.position = pos;
        trailRenderer.Clear();
    }




    // ---- Turning ----
    // Running straight on keeps the speed. A turn costs speed at the corner, and the runner then accelerates again
    // (more slowly than usual): a gentle bend (up to 30 degrees) costs nothing, a right angle brings the speed down to
    // 55 %, and turning right round (180 degrees, back the way you came) to a near stop. With the usual speeds that
    // is about 1 s for a right angle and 4 to 5 s for turning back, in race time.
    const float TurnAcceleration = 2f;   // world units per second², while getting back up to speed after a turn
    const float TurnWindowMeters = 20f;  // the direction in and out of a corner is taken over this distance

    private float[] turnFactor;          // per cell of the path: the share of the speed kept there (1 = no turn)
    private bool turnRecovering;
    private Vector3 lastHeading;

    public static float TurnSpeedShare(float degrees)
    {
        if (degrees <= 30f) return 1f;
        if (degrees <= 90f) return Mathf.Lerp(1f, 0.55f, (degrees - 30f) / 60f);
        return Mathf.Lerp(0.55f, 0.05f, (Mathf.Min(degrees, 180f) - 90f) / 90f);
    }

    bool OpenCell(Vector2Int c)
    {
        float[,] grid = pathfindingManager.speedGrid2D;
        return c.y >= 0 && c.x >= 0 && c.y < grid.GetLength(0) && c.x < grid.GetLength(1) && grid[c.y, c.x] > 0.01f;
    }

    // The turns along a new path. The direction in and out of each cell is measured over about 20 m, so the zigzag
    // of a grid path is not a turn, and only the sharpest point of each corner slows the runner down.
    private void ComputeTurns()
    {
        turnRecovering = false;
        if (path == null || path.Count == 0 || pathfindingManager == null)
        {
            turnFactor = null;
            return;
        }
        int n = path.Count;
        float cellMeters = pathfindingManager.cellSize / 0.01f;
        int k = Mathf.Max(1, Mathf.RoundToInt(TurnWindowMeters / Mathf.Max(0.5f, cellMeters)));
        float[] angle = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 into = i > 0 ? (Vector2)(path[i] - path[Mathf.Max(0, i - k)]) : (Vector2)lastHeading;
            Vector2 outOf = (Vector2)(path[Mathf.Min(n - 1, i + k)] - path[i]);
            angle[i] = into.sqrMagnitude > 0.0001f && outOf.sqrMagnitude > 0.0001f ? Vector2.Angle(into, outOf) : 0f;
        }
        turnFactor = new float[n];
        for (int i = 0; i < n; i++)
        {
            turnFactor[i] = 1f;
            if (angle[i] <= 30f) continue;
            bool sharpest = true;
            for (int j = Mathf.Max(0, i - k); j <= Mathf.Min(n - 1, i + k) && sharpest; j++)
            {
                // Ties: the first cell of the corner counts
                if (angle[j] > angle[i] || (angle[j] == angle[i] && j < i)) sharpest = false;
            }
            if (sharpest) turnFactor[i] = TurnSpeedShare(angle[i]);
        }
    }

    // Called as the runner reaches a cell of its path
    private void ApplyTurn(int index)
    {
        if (turnFactor == null || index < 0 || index >= turnFactor.Length || turnFactor[index] >= 1f) return;
        currentSpeed *= turnFactor[index];
        turnRecovering = true;
    }

    private string GetDirectionName(Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Clamp(to.x - from.x, -1, 1);
        int dy = Mathf.Clamp(to.y - from.y, -1, 1);

        if (dx == 0 && dy > 0) return "N";
        if (dx > 0 && dy > 0) return "NE";
        if (dx > 0 && dy == 0) return "E";
        if (dx > 0 && dy < 0) return "SE";
        if (dx == 0 && dy < 0) return "S";
        if (dx < 0 && dy < 0) return "SW";
        if (dx < 0 && dy == 0) return "W";
        if (dx < 0 && dy > 0) return "NW";
        return "N";
    }


    IEnumerator WaitAtControlToDraw()
    {
        isWaiting = true;
        Debug.Log("IsWaiting");
        yield return new WaitForSeconds(10f);
        if (manualPathDrawer.timeToDraw == true)
        {
            Debug.Log("Du ritade inte klart");
        }
        isWaiting = false;
    }



    public Vector3 GridToWorldPosition(Vector2Int gridPos)
    {
        if (pathfindingManager == null)
        {
            Debug.LogError("PathfindingManager är null i GridToWorldPosition!");
            return Vector3.zero;
        }
        // gridPos.y = rad i, gridPos.x = kolumn j
        return pathfindingManager.GridToWorldPosition(gridPos.y, gridPos.x);
    }

    public Vector2Int WorldToGridPosition(Vector3 worldPos)
    {
        int j = Mathf.Clamp((int)(worldPos.x / pathfindingManager.cellSize),
                            0, pathfindingManager.speedGrid2D.GetLength(1) - 1); // kolumn
        int i = Mathf.Clamp((int)(worldPos.y / pathfindingManager.cellSize),
                            0, pathfindingManager.speedGrid2D.GetLength(0) - 1); // rad
        return new Vector2Int(j, i); // x=kolumn, y=rad
    }
    public Vector2Int WorldToGridPositionCentered(Vector3 worldPos)
    {
        if (pathfindingManager.speedGrid2D == null)
        {
            Debug.Log("speedGrid  är null");
        }
        int rows = pathfindingManager.speedGrid2D.GetLength(0);
        int cols = pathfindingManager.speedGrid2D.GetLength(1);

        float totalWidth = cols * pathfindingManager.cellSize;
        float totalHeight = rows * pathfindingManager.cellSize;

        // Invertera centrerings-offseten (+ half extents), sen dela med cellSize
        int j = Mathf.FloorToInt((worldPos.x + totalWidth / 2f) / pathfindingManager.cellSize); // kolumn
        int i = Mathf.FloorToInt((worldPos.y + totalHeight / 2f) / pathfindingManager.cellSize); // rad

        j = Mathf.Clamp(j, 0, cols - 1);
        i = Mathf.Clamp(i, 0, rows - 1);
        return new Vector2Int(j, i); // x=kolumn=j, y=rad=i
    }


    public void startView()
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }
        Debug.Log("pathfindingManager " + pathfindingManager);
        if(pathfindingManager == null)
        {
            pathfindingManager = FindObjectOfType<PathfindingManager>();
        }
        Debug.Log("controlPoints " + pathfindingManager.controlPoints[0].position + " " + pathfindingManager.controlPoints[1].position);
        CamController.FocusOnSegment(WorldToGridPositionCentered(pathfindingManager.controlPoints[0].position), WorldToGridPositionCentered(pathfindingManager.controlPoints[1].position));
        Debug.Log("startView has been done");
    }

    public void DoDrawMistake()
    {
        if (RunnerCollision.cantBäckMiss || RunnerCollision.cantStigMiss || RunnerCollision.cantBeståndsgränsMiss || !useManualPath)
        {
            return;
        }
        // Someone hanging on follows the leader, mistakes and all, but makes none of their own
        if (packRunning != null && packRunning.IsHanging)
        {
            return;
        }
        
        // Hämta positioner
        Vector3 pos = transform.position;
        Vector3 controlPos = pathfindingManager.controlPoints[currentKontroll].position;
        Vector3 lastControlPos = pathfindingManager.controlPoints[currentKontroll - 1].position;

        // Hela sträckan mellan kontroller
        float totalDistance = (controlPos - lastControlPos).magnitude;
        // Hur långt kvar till kontrollen
        float remainingDistance = (controlPos - pos).magnitude;

        // Hur långt agenten hunnit
        float traveled = (pos-lastControlPos).magnitude;

        // Om mindre än 35% är avklarat return
        if (traveled / totalDistance < 0.25f || isAngleMissing || isDrawMissing)
        {
            return;
        }
        // Annars: chans att missa
        else if (remainingDistance < 1.5f)
        {
            Debug.Log("blev en nära bom");
            DrawMiss = true;
        }
        else if (remainingDistance >= 1.5f)
        {
            if (random.NextDouble() < runnerStatTec)
            {
                if (!isAngleMissing)
                {
                    Debug.Log("Börjar boma Långt");
                    isAngleMissing = true;
                    StartAngleMiss(controlPos);
                }
            }
        }
    }
    private IEnumerator HandleMissCloseToControll(int missIndex)
    {
        Debug.Log("gör en miss nära kontrollen");
        // Vänta tills vi når missIndex
        while (currentTargetIndex < missIndex)
        {
            yield return null;
        }
        findSafeObject = false;
        // Vänta 0.7 sek
        isWaiting = true;
        yield return new WaitForSeconds(0.1f);
        isWaiting = false;

        if (hasDoneADrawCloseMiss && (isDrawMissing || isAngleMissing))
        {
            if (Random.value < runnerStatTec && maxBom<15)
            {
                maxBom++;
                yield return StartCoroutine(DoExtraMiss());
            }
            else
            {
                hasDoneADrawCloseMiss = false;
            }
        }
        // Sänk hastigheten till hälften under 0.7 sek
        float slowFactor = 0.75f;
        baseSpeed *= slowFactor;

        yield return new WaitForSeconds(0.8f);

        // Återställ hastigheten
        baseSpeed = originalBaseSpeed;
    }

    private void StartAngleMiss(Vector3 controlPos)
    {

        Debug.Log("Är i start angle bom");
        // Slumpa vinkel 2–10 grader
        //float angle = Random.Range(2f, 8f);
        float angle = Random.Range(20f, 45f);
        //Debug.Log(angle + " angle bom");
        if (Random.value < 0.5f) angle = -angle; // höger eller vänster

        // Rotera riktningen
        angleMissDir = Quaternion.Euler(0, 0, angle) * transform.up;

        // Starta coroutine
        StartCoroutine(HandleAngleMiss(controlPos));
    }
    private IEnumerator HandleAngleMiss(Vector3 controlPos)
    {
        Debug.Log("Startar angle bom");
        isAngleMissing = true;
        findSafeObject = false;

        // Beräkna missmål i världskordinater (t.ex. 1.2 * startDistance i angleMissDir)
        float startDistance = Vector3.Distance(transform.position, controlPos);
        float missDistance = startDistance * 1.3f;
        Vector3 missDir = angleMissDir.normalized;
        Vector3 missWorldTarget = transform.position + missDir * missDistance;

        // Konvertera till grid
        Vector2Int missCell = WorldToGridPositionCentered(missWorldTarget);

        // Rak linje i den felaktiga riktningen. Ingen A*-sökning behövs, så agenten svänger direkt utan att stanna.
        List<Vector2Int> missPath = BuildStraightPath(WorldToGridPositionCentered(transform.position), missCell);

        if (missPath.Count < 2)
        {
            Debug.LogWarning("MissPath kunde inte genereras, fortsätter på den ritade vägen mot kontrollen.");
            isAngleMissing = false;
            yield break;
        }

        // Sätt fel-path som aktiv
        path = missPath;
        currentTargetIndex = 0;
        currentTargetPosition = GridToWorldPosition(path[currentTargetIndex]);

        // Följ fel-path tills den tar slut ELLER tills vi krockar
        while (currentTargetIndex < path.Count)
        {
            float controlDist = Vector3.Distance(transform.position, controlPos);

            
            // Avsluta om vi krockar
            if (findSafeObject
                  || (angleMissCollider.numberOfBigStones >= 1 && RunnerCollision.RunnerMapCollision.numberOfBigStones <= 4)
                  || RunnerCollision.cantBeståndsgränsMiss
                  || RunnerCollision.cantBäckMiss
                  || (angleMissCollider.numberOfStones >= 1 && RunnerCollision.RunnerMapCollision.numberOfStones <= 4)
                  || RunnerCollision.cantStigMiss)
            {
                findSafeObject = false;
                Debug.Log("Agenten hittar ett tydligt object och startar recover metoden bom");
                yield return StartCoroutine(RecoverFromMiss(controlPos));
                yield break;
            }

            yield return null;
        }

        Debug.Log("agenten sprang så långt så att han nu rör sig mot kontrollen bom");
        isWaiting = true;
        yield return new WaitForSeconds(0.2f);
        isWaiting = false;
        baseSpeed = originalBaseSpeed;

        // Ny path till kontrollen (async)
        Vector2Int controlCell = WorldToGridPositionCentered(controlPos);
        List<Vector2Int> newPath = null;
        yield return StartCoroutine(CalculatePathAsync(WorldToGridPositionCentered(transform.position), controlCell, result => newPath = result, true));

        if (newPath != null && newPath.Count > 0)
        {
            path = newPath;
            currentTargetIndex = 0;
            currentTargetPosition = GridToWorldPosition(path[currentTargetIndex]);
        }
        // Sätts sist: om ingen väg hittades tar Update över och låter löparen springa rakt mot kontrollen
        isAngleMissing = false;
    }

    // Cellerna på en rak linje från start mot goal. Linjen bryts före första cell som inte går att springa i,
    // eftersom agenten annars skulle fastna där.
    private List<Vector2Int> BuildStraightPath(Vector2Int start, Vector2Int goal)
    {
        var cells = new List<Vector2Int> { start };
        float[,] grid = pathfindingManager.speedGrid2D;

        int steps = Mathf.Max(Mathf.Abs(goal.x - start.x), Mathf.Abs(goal.y - start.y));
        for (int step = 1; step <= steps; step++)
        {
            float t = (float)step / steps;
            Vector2Int cell = new Vector2Int(
                Mathf.RoundToInt(Mathf.Lerp(start.x, goal.x, t)),
                Mathf.RoundToInt(Mathf.Lerp(start.y, goal.y, t)));

            if (grid[cell.y, cell.x] <= 0.01f)
            {
                break;
            }
            if (cell != cells[cells.Count - 1])
            {
                cells.Add(cell);
            }
        }
        return cells;
    }

    private IEnumerator RecoverFromMiss(Vector3 controlPos)
    {
        findSafeObject = false;
        Debug.Log("Hittade något tydligt så att agenten rör sig nu mot kontrollen boma");

        // Vänta 0.5 sek innan vi räknar path
        isWaiting = true;
        yield return new WaitForSeconds(0.3f);
        isWaiting = false;

        // Försök hitta ny path till kontrollen (async)
        Vector2Int controlCell = WorldToGridPositionCentered(controlPos);
        List<Vector2Int> newPath = null;

        yield return StartCoroutine(
            CalculatePathAsync(WorldToGridPositionCentered(transform.position), controlCell,
                result => newPath = result, true)
        );

        if (newPath != null && newPath.Count > 0)
        {
            path = newPath; // ersätter direkt istället för Clear + assign
            currentTargetIndex = 0;
            currentTargetPosition = GridToWorldPosition(path[currentTargetIndex]);
        }
        else
        {
            Debug.LogWarning("Kunde inte hitta ny path till kontrollen vid RecoverFromMiss!");
        }

        // Sänk hastigheten till hälften under 1.2 sek
        float slowFactor = 0.5f;
        baseSpeed *= slowFactor;
        isAngleMissing = false;
        isDrawMissing = false;
        findSafeObject = false;
        yield return new WaitForSeconds(1f);

        // Återställ hastigheten
        baseSpeed = originalBaseSpeed;

    }

    private IEnumerator DoExtraMiss()
    {
        isDrawMissing = true;
        findSafeObject = false;
        Vector3 controlPos = pathfindingManager.controlPoints[currentKontroll].position;
        bool missTowardControl = Random.value < runnerStatTec * 3f; 

        Debug.Log(missTowardControl ? "gör en extra bom mot kontrollen" : "gör en extra bom bort ifrån kontrollen");

        // Förbered start- och kontrollceller
        Vector2Int startCell = WorldToGridPositionCentered(transform.position);
        Vector2Int controlCell = WorldToGridPositionCentered(controlPos);

        // --- Beräkna misspunkten ---
        Vector3 missPoint;
        if (missTowardControl)
        {
            Vector3 dirToControl = (controlPos - transform.position).normalized;

            // ±90° sidosteg
            Vector3 perpDir = UnityEngine.Random.value < 0.5f
                ? new Vector3(-dirToControl.y, dirToControl.x, 0f)
                : new Vector3(dirToControl.y, -dirToControl.x, 0f);

            float missOffset = UnityEngine.Random.Range(0.5f, 0.9f);
            missPoint = controlPos + perpDir * missOffset;

            // Slump kort/lång
            missPoint += dirToControl * UnityEngine.Random.Range(0.0f, 0.5f);
        }
        else
        {
            Vector3 dirToControl = (controlPos - transform.position).normalized;
            Vector3 dirAway = -dirToControl;

            // ±35°
            float angle = UnityEngine.Random.value < 0.5f ? 30f : -30f;
            dirAway = Quaternion.Euler(0, 0, angle) * dirAway;

            float distToControl = Vector3.Distance(transform.position, controlPos);
            float missDist = distToControl * 1.1f;
            missPoint = transform.position + dirAway * missDist;
        }

        Vector2Int missCell = WorldToGridPositionCentered(missPoint);

        // --- Hämta paths async ---
        List<Vector2Int> pathToMiss = null;
        List<Vector2Int> pathToControl = null;

        yield return StartCoroutine(CalculatePathAsync(startCell, missCell, result => pathToMiss = result, true));
        yield return StartCoroutine(CalculatePathAsync(missCell, controlCell, result => pathToControl = result, false));

        int missIndex = -1;
        if (pathToMiss != null && pathToControl != null)
        {
            path.Clear();
            path.AddRange(pathToMiss);
            missIndex = path.Count - 1; // sista punkten i missPath
            path.AddRange(pathToControl.Skip(1)); // undvik duplicering
            currentTargetIndex = 0;
            currentTargetPosition = GridToWorldPosition(path[currentTargetIndex]);
        }
        else
        {
            Debug.LogWarning("MissPath kunde inte genereras!");
            yield break;
        }

        // Starta nästa miss-hantering
        StartCoroutine(HandleMissCloseToControll(missIndex));
    }
    private IEnumerator HandleCloseFirstDrawMiss()
    {
        isDrawMissing = true;
        findSafeObject = false;
        Vector3 controlPos2 = pathfindingManager.controlPoints[currentKontroll].position;

        // Slumpa höger/vänster (±90 grader från riktningen mot kontrollen)
        Vector3 dirToControl = (controlPos2 - transform.position).normalized;
        Vector3 perpDir = UnityEngine.Random.value < 0.5f
            ? new Vector3(-dirToControl.y, dirToControl.x, 0f)   // vänster
            : new Vector3(dirToControl.y, -dirToControl.x, 0f);  // höger

        // Slumpa avstånd 0.4f–1.7f
        float missOffset = UnityEngine.Random.Range(0.4f, 1.7f);
        Vector3 missPoint = controlPos2 + perpDir * missOffset;

        // Slumpa kort/lång (0–1f)
        missPoint += dirToControl * UnityEngine.Random.Range(0.0f, 1f);

        // Konvertera till grid
        Vector2Int startCell = WorldToGridPositionCentered(transform.position);
        Vector2Int missCell = WorldToGridPositionCentered(missPoint);
        Vector2Int controlCell = WorldToGridPositionCentered(controlPos2);

        // Kör båda A*-sökningarna i en bakgrundstråd så att spelet inte hackar till mitt i löpningen.
        // Agenten står still (isWaiting) de få bildrutor sökningen tar.
        List<Vector2Int> pathToMiss = null;
        List<Vector2Int> pathToControl = null;
        bool pathsDone = false;
        float[,] speedGrid = pathfindingManager.speedGrid2D;
        var slopeFactors = pathfindingManager.slopeFactors;

        isWaiting = true;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var astar = new AStarPathfinder(speedGrid, slopeFactors);

                // Path till missPoint
                pathToMiss = astar.FindPath(startCell, missCell);
                // Path från missPoint till kontrollen
                pathToControl = astar.FindPath(missCell, controlCell);
            }
            finally
            {
                pathsDone = true;
            }
        });

        while (!pathsDone)
            yield return null;
        isWaiting = false;

        int missIndex = -1;

        if (pathToMiss != null && pathToControl != null)
        {
            path = new List<Vector2Int>();
            path.AddRange(pathToMiss);
            missIndex = path.Count - 1; // sista punkten i pathToMiss
            path.AddRange(pathToControl.Skip(1)); // undvik duplicering
        }
        else
        {
            Debug.LogWarning("MissPath kunde inte genereras med A*");
            path = new List<Vector2Int> { missCell, controlCell };
        }

        currentTargetIndex = 0;
        currentTargetPosition = GridToWorldPosition(path[currentTargetIndex]);
        hasDoneADrawCloseMiss = true;

        // Starta coroutine som hanterar när agenten når missIndex
        yield return StartCoroutine(HandleMissCloseToControll(missIndex));
    }

    private IEnumerator CalculatePathAsync(Vector2Int start, Vector2Int goal, Action<List<Vector2Int>> onPathReady, bool shouldStartBeTherePlayerIs)
    {
        if (shouldStartBeTherePlayerIs)
        {
            start = WorldToGridPositionCentered(transform.position);
        }
        List<Vector2Int> result = null;
        bool done = false;

        // Kör A* i en bakgrundstråd
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var astar = new AStarPathfinder(pathfindingManager.speedGrid2D, pathfindingManager.slopeFactors);
                result = astar.FindPath(start, goal);
            }
            catch (Exception e)
            {
                Debug.LogError("A* misslyckades: " + e);
            }
            finally
            {
                // Måste alltid sättas, annars väntar coroutinen (och agenten) för alltid
                done = true;
            }
        });

        // Vänta tills klar
        while (!done)
            yield return null;

        onPathReady?.Invoke(result);
    }

    private IEnumerator handleSpeedWhenDrawing()
    {
        while (manualPathDrawer.timeToDraw)
        {
            baseSpeed = SlowBaseSpeed;
            yield return null;
        }
        baseSpeed = originalBaseSpeed;
        doingSlowMovingCoroutine = false;
    }
    private void HandleLineCollision(GameObject lineObj)
    {
        // Här skriver du vad som ska hända när agenten korsar en linje
        Debug.Log("Agenten reagerar på linje: " + lineObj.name);
        if (CheckFeatureName("höjdkurva", lineObj.name))
        {
            MapFeature mf = lineObj.GetComponent<MapFeature>();
            if (mf.length < 30 && RunnerCollision.RunnerMapCollision.numberOfSmallHills < 2)
            {
                findSafeObject = true;
            }
        }
        else if (CheckFeatureName("Opasserbar brant(valfri tagg)", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfCliffs <=4)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Passerbar brant/Skrent", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfCliffs <= 4)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Opasserbar brant", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfCliffs <= 4)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Stor stig", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Liten stig", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Större bäck", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfBäck <= 4)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Smal väg", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Mindre väg 0.30", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Liten otydlig stig", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Körväg", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Större väg 0.5", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Cykelväg", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Väg: sidlinje", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Motorväg", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Rågång/Drivningsväg", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Cykelväg, sidlinje", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }
        else if (CheckFeatureName("Järnväg", lineObj.name) && RunnerCollision.RunnerMapCollision.numberOfStig <= 10)
        {
            findSafeObject = true;
        }

    }
    public bool CheckFeatureName(string objectName, string name)
    {
        string processedName = name;

        // Ta bort allt fram till första mellanslaget (inklusive det)
        int spaceIndex = processedName.IndexOf(' ');
        if (spaceIndex >= 0)
            processedName = processedName.Substring(spaceIndex + 1);

        // Ta bort mellanslag och jämför skiftlägesokänsligt
        processedName = processedName.Replace(" ", "").Trim();
        string cleanObjectName = objectName.Replace(" ", "").Trim();

        return processedName.Equals(cleanObjectName, System.StringComparison.OrdinalIgnoreCase);
    }
    public void RemoveLine()
    {
        if (!Object.HasInputAuthority)
        {
            Debug.Log("Har inte inputAthuority");
            return;
        }
        Debug.Log("HAr inputAthority så remove line");
        manualPathDrawer.RemoveLastPoints();
    }

    [Serializable]
    public class AgentSnapshot
    {
        // Grundläggande spelstatus
        public Vector3 position;
        public int currentTargetIndex;
        public bool hasFinished;
        public bool isWaiting;
        public bool canRunNextSegment;

        // Rörelse- och pathdata
        public List<Vector2Int> path;
        public float baseSpeed;
        public float runnerStatTec;
        public float acceleration;
        public float currentSpeed;
        public int currentKontroll;
        public Vector2Int lastGridCell;

        // Miss/Draw-status
        public bool isMissingControl;
        public bool shouldNotMiss;
        public bool isDrawMissing;
        public bool isAngleMissing;
        public bool hasDoneADrawCloseMiss;
        public bool doDrawCloseMiss;
        public bool findSafeObject;
        public bool DrawMiss;
        public int maxBom;
        public float missDistanse;
        public Vector2 lastPos;

        // Extra logikflaggor
        public bool isInitialized;
        public bool useManualPath;
    }


    public AgentSnapshot GetSnapshot()
    {
        Debug.Log("speed" + baseSpeed);
        Debug.Log("curentkontroll" + currentKontroll);
        return new AgentSnapshot
        {
            position = transform.position,
            currentTargetIndex = currentTargetIndex,
            hasFinished = hasFinished,
            isWaiting = isWaiting,
            canRunNextSegment = canRunNextSegment,
            path = path != null ? new List<Vector2Int>(path) : new List<Vector2Int>(),

            baseSpeed = baseSpeed,
            runnerStatTec = runnerStatTec,
            acceleration = acceleration,
            currentSpeed = currentSpeed,
            currentKontroll = currentKontroll,
            lastGridCell = lastGridCell,

            isMissingControl = isMissingControl,
            shouldNotMiss = shouldNotMiss,
            isDrawMissing = isDrawMissing,
            isAngleMissing = isAngleMissing,
            hasDoneADrawCloseMiss = hasDoneADrawCloseMiss,
            doDrawCloseMiss = doDrawCloseMiss,
            findSafeObject = findSafeObject,
            DrawMiss = DrawMiss,
            maxBom = maxBom,
            missDistanse = missDistanse,
            lastPos = lastPos,

            isInitialized = isInitialized,
            useManualPath = useManualPath
        };
    }


    public void ApplySnapshot(AgentSnapshot snapshot)
    {
        // Återställ grundläggande status
        transform.position = snapshot.position;
        currentTargetIndex = snapshot.currentTargetIndex;
        hasFinished = snapshot.hasFinished;
        isWaiting = snapshot.isWaiting;
        canRunNextSegment = snapshot.canRunNextSegment;

        path = new List<Vector2Int>(snapshot.path);

        baseSpeed = snapshot.baseSpeed;
        runnerStatTec = snapshot.runnerStatTec;
        acceleration = snapshot.acceleration;
        currentSpeed = snapshot.currentSpeed;
        currentKontroll = snapshot.currentKontroll;
        lastGridCell = snapshot.lastGridCell;

        isMissingControl = snapshot.isMissingControl;
        shouldNotMiss = snapshot.shouldNotMiss;
        isDrawMissing = snapshot.isDrawMissing;
        isAngleMissing = snapshot.isAngleMissing;
        hasDoneADrawCloseMiss = snapshot.hasDoneADrawCloseMiss;
        doDrawCloseMiss = snapshot.doDrawCloseMiss;
        findSafeObject = snapshot.findSafeObject;
        DrawMiss = snapshot.DrawMiss;
        maxBom = snapshot.maxBom;
        missDistanse = snapshot.missDistanse;
        lastPos = snapshot.lastPos;

        isInitialized = snapshot.isInitialized;
        useManualPath = snapshot.useManualPath;

        SetInitialPosition(snapshot.position);
    }

    // The card's physics (forest) and sprint speed (hard ground, below 0 = none), for RunnerRules.SurfaceFactor
    float cardPhysics;
    float cardSprintSpeed = CardData.NoValue;

    // The runner's physics, sprint speed (-1 = none) and technique, for the leaderboard (0 before a runner is set)
    public Vector3Int RunnerStats => string.IsNullOrEmpty(runnerName) ? Vector3Int.zero
        : new Vector3Int(Mathf.RoundToInt(cardPhysics), cardSprintSpeed >= 0f ? Mathf.RoundToInt(cardSprintSpeed) : -1,
            Mathf.RoundToInt(100f - runnerStatTec * 100f));

    public void SetRunner(CardDisplay card)
    {
        Debug.Log("SetRunner starts");
        cardPhysics = card.CardData != null ? card.CardData.physics : card.speed;
        cardSprintSpeed = card.CardData != null ? card.CardData.sprintSpeed : card.sprintSpeed;
        // Women run with RunnerRules.WomenSpeedFactor of their card speed (the cards' speed is measured within each class)
        float raceSpeed = card.CardData != null ? RunnerRules.RaceSpeed(card.CardData) : card.man ? card.speed : card.speed * RunnerRules.WomenSpeedFactor;
        baseSpeed = unscaledBaseSpeed * raceSpeed / 100;
        runnerStatTec = card.technique;
        runnerName = card.runnerName;

        // The results table after the race shows who ran
        NetworkPlayer np = GetComponent<NetworkPlayer>();
        if (np != null && np.Object != null && np.Object.HasStateAuthority)
        {
            np.relayRunnerName = runnerName ?? "";
        }

        runnerStatTec = (100 - runnerStatTec) / 100;
        originalBaseSpeed = baseSpeed;
        SlowBaseSpeed = originalBaseSpeed / 20;
    }
    // The runner for a relay leg
    public void SetRunner(CardData card)
    {
        cardPhysics = card.physics;
        cardSprintSpeed = card.sprintSpeed;
        baseSpeed = unscaledBaseSpeed * RunnerRules.RaceSpeed(card) / 100;
        runnerName = card.RunnerName;
        runnerStatTec = (100 - card.technique) / 100;
        originalBaseSpeed = baseSpeed;
        SlowBaseSpeed = originalBaseSpeed / 20;
    }

    // Reached the last control: stop the clock and show the finish
    public void FinishRace()
    {
        if (pathfindingManager.multiplayer)
        {
            if (Object.HasStateAuthority)
            {
                clock.PlayMultiPlayerEndScreen();
            }
        }
        else
        {
            clock.PlaySingelPlayerEndScreen();
        }
        hasFinished = true;
        clock.ChangeClockRunning();
        clock.setEndTimeText();
    }

    void ClearMissState()
    {
        shouldNotMiss = false;
        missCount = 0;
        maxBom = 0;
        isAngleMissing = false;
        isDrawMissing = false;
    }

    // Relays: runs the drawn line of stretch s (from point s to point s + 1)
    public void FollowOwnSegment(int segment)
    {
        if (segment < 0 || segment >= relaySegments.Count || relaySegments[segment] == null || relaySegments[segment].Count == 0)
        {
            // Nothing drawn for it: find the way on our own
            FindControlAlone();
            return;
        }
        path = new List<Vector2Int>(relaySegments[segment]);
        currentTargetIndex = 0;
        currentTargetPosition = GridToWorldPosition(path[0]);
        lastGridCell = path[0];
    }

    // After letting go of a runner: find the control we are heading for with A*, then go on with our own line
    public void FindControlAlone()
    {
        if (findingControl) return;
        StartCoroutine(FindControlAloneRoutine());
    }

    bool findingControl;
    int hangControl = -1;          // the control the closest distance below is for
    float hangClosest = float.MaxValue;   // how close to it the runner has come while hanging on

    IEnumerator FindControlAloneRoutine()
    {
        findingControl = true;
        Vector2Int controlCell = WorldToGridPositionCentered(pathfindingManager.controlPoints[currentKontroll].position);
        List<Vector2Int> newPath = null;
        yield return StartCoroutine(CalculatePathAsync(WorldToGridPositionCentered(transform.position), controlCell, result => newPath = result, true));
        findingControl = false;
        if (packRunning != null && packRunning.IsHanging)
        {
            yield break; // started hanging on to someone else meanwhile
        }
        if (newPath == null || newPath.Count == 0)
        {
            newPath = new List<Vector2Int> { WorldToGridPositionCentered(transform.position), controlCell };
        }
        path = newPath;
        currentTargetIndex = 0;
        currentTargetPosition = GridToWorldPosition(path[0]);
    }

    // Puts the runner on the start of a new relay leg, ready to draw
    public void ResetForRelayLeg(Vector3 startPosition)
    {
        StopAllCoroutines();
        findingControl = false;
        doingSlowMovingCoroutine = false;
        isWaiting = false;
        relayHold = false;
        hasFinished = false;
        canRunNextSegment = false;
        WaitingForAllPlayersToDraw = false;
        WaitingForAllPlayersToReachKontroll = false;
        isMissingControl = false;
        DrawMiss = false;
        findSafeObject = false;
        hasDoneADrawCloseMiss = false;
        ClearMissState();
        currentKontroll = 1;
        currentTargetIndex = 0;
        currentSpeed = 0f;
        lastHeading = Vector3.zero;
        relaySegments = new List<List<Vector2Int>>();
        path = new List<Vector2Int>();
        isInitialized = false;
        if (acceleration2 > 0f) acceleration = acceleration2;
        baseSpeed = originalBaseSpeed;
        SetInitialPosition(startPosition);
        lastPos = startPosition;
        lastGridCell = WorldToGridPositionCentered(startPosition);
    }

    public void SetBetOnRunnerPanel(bool Bool)
    {
        if (pathfindingManager == null)
        {
            pathfindingManager = FindObjectOfType<PathfindingManager>(true);
        }
        pathfindingManager.BetOnRunnerPanel.SetActive(Bool);
    }
}
