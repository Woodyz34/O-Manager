using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CameraController : MonoBehaviour
{
    public Camera cam;
    private PathfindingManager pathManager;

    [SerializeField]
    private float zoomStep, minCamSize, maxCamSize;

    [Header("Draw view")]
    [Tooltip("How much of the screen height the leg between two controls fills when it is time to draw. Lower means more zoomed out.")]
    [SerializeField, Range(0.3f, 0.9f)] private float legShareOfScreen = 0.6f;
    [Tooltip("The draw view is never more zoomed in than this, however short the leg is.")]
    [SerializeField] private float minLegCamSize = 4.5f;
    // The zoom the current leg needs. Scrolling out is always allowed at least this far.
    private float legCamSize;

    public SpriteRenderer mapRenderer;
    private float mapMinX, mapMaxX, mapMinY, mapMaxY;

    private Vector3 dragOrigin;


  
    Vector3 velocity = Vector3.zero;

    [Range(0, 1)]
    public float smoothTime;

    public Vector3 positionOffset;

    private bool canWrite = true;

    [Header("Följ löparen")]
    [Tooltip("Kamerans zoom när den följer löparen. Ändras om spelaren zoomar medan kameran följer.")]
    [SerializeField] private float followCamSize = 3f;
    private bool followRunner = false;
    private bool wasFollowing = false;
    // Set by the TV production (TvViewer): a place the following camera shows in place of the player's own runner
    public static Vector3? LookHere;
    // Another player's runner picked on the leaderboard (2026-11-19): Follow runner follows it instead of your own,
    // without Follow runner the map jumps there once. Pick your own row to come back.
    public static Transform Watched;
    private AgentMovement agent;
    private Button followButton;
    private TextMeshProUGUI followButtonText;


    private AudioListener ear;

    public void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Every player has a camera with its own AudioListener, but only the local player's camera is switched
        // on. Listeners of switched-off cameras stay enabled otherwise, and Unity then warns that there are
        // several in the scene. The listener follows its camera (LateUpdate) and starts off.
        if (cam != null)
        {
            ear = cam.GetComponent<AudioListener>();
            if (ear != null) ear.enabled = false;
        }

        if (ActiveScene.Name == "Lobby")
        {
            return;
        }
        InitializeCamera();
    }

    private void OnDestroy()
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
        // The route choice game, the route comparison and the relay replay switch this controller off while they
        // own the camera. The player (and so this controller) lives on into the next race, so a view that was closed
        // by a scene change (the host taking everyone back to the lobby) left zoom, panning and Follow runner off in
        // every race after it. A new scene always starts with the player's own camera control on.
        if (!enabled) enabled = true;
        if (scene.name == "Lobby") return;
        InitializeCamera();
    }

    private void InitializeCamera()
    {
        pathManager = FindObjectOfType<PathfindingManager>();
        GameObject mapObject = GameObject.FindGameObjectWithTag("MapImage");
        if (mapObject != null)
        {
            mapRenderer = mapObject.GetComponent<SpriteRenderer>();
            mapMinX = mapRenderer.transform.position.x - mapRenderer.bounds.size.x;
            mapMaxX = mapRenderer.transform.position.x + mapRenderer.bounds.size.x;

            mapMinY = mapRenderer.transform.position.y - mapRenderer.bounds.size.y;
            mapMaxY = mapRenderer.transform.position.y + mapRenderer.bounds.size.y;
        }
    }

    void Update()
    {
        if (cam == null || !cam.enabled)
        {
            return;
        }
        if (ActiveScene.Name == "Lobby")
        {
            return;
        }
        UpdateFollowButton();

        bool following = IsFollowingRunner();
        if (following && !wasFollowing)
        {
            // Byter från rit-vyn (eller översikten) till följ-vyn
            cam.orthographicSize = Mathf.Clamp(followCamSize, minCamSize, maxCamSize);
        }
        wasFollowing = following;

        // Before the competition has started the Choose a runner screen covers the map. Scrolling its list or
        // dragging on it used to zoom and move the camera behind it, so the first leg came up zoomed in.
        if (agent == null)
        {
            agent = GetComponent<AgentMovement>();
        }
        if (agent != null && agent.WaitingForAllPlayersToChooseRunner)
        {
            return;
        }

        if (mapRenderer == null)
        {
            // The camera was set up before the map picture was in place; look again
            InitializeCamera();
        }

        FocusOnNewStretchToDraw();

        if (!following && !EscMenu.IsOpen && !HostSettingsPopup.IsOpen && !TvViewer.BlocksMapInput && mapRenderer != null)
        {
            PanCamera();
        }
        
        // The wheel belongs to the list under the mouse (the leaderboard, for one), not to the map behind it
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel != 0f && !PointerOverUI() && !EscMenu.IsOpen && !HostSettingsPopup.IsOpen && !TvViewer.BlocksMapInput && mapRenderer != null)
        {
            if (wheel > 0f) { ZoomIn(); }
            else { ZoomOut(); }
        }

        if (following)
        {
            // Kom ihåg spelarens egen zoom till nästa gång kameran börjar följa
            followCamSize = cam.orthographicSize;
        }
    }

    // Same test as for drawing: where you cannot draw because the interface is in the way, you cannot zoom either
    private bool PointerOverUI()
    {
        return EventSystem.current != null && agent != null && agent.manualPathDrawer != null
            && agent.manualPathDrawer.IsPointerOverTaggedCanvas();
    }

    void LateUpdate()
    {
        if (ear != null)
        {
            bool wanted = cam != null && cam.enabled;
            if (ear.enabled != wanted) ear.enabled = wanted;
        }
        if (Watched != null && ActiveScene.Name == "Lobby") Watched = null;
        bool watching = Watched != null && followRunner && agent != null && agent.manualPathDrawer != null && !agent.manualPathDrawer.timeToDraw;
        if (cam == null || !cam.enabled || (!IsFollowingRunner() && !watching))
        {
            return;
        }
        // Efter att löparen har flyttats i Update, så att kameran inte ligger en bildruta efter
        // In a TV production the map goes to where something happens (another team's mistake) and back again
        Vector3 look = LookHere ?? (Watched != null ? Watched.position : transform.position);
        cam.transform.position = new Vector3(look.x, look.y, -10f);
    }

    // Löparen springer (eller väntar på de andra) och spelaren ritar inte just nu
    private bool IsRunPhase()
    {
        if (agent == null)
        {
            agent = GetComponent<AgentMovement>();
        }
        return agent != null
            && agent.manualPathDrawer != null
            && !agent.manualPathDrawer.timeToDraw
            && !agent.WaitingForAllPlayersToChooseRunner
            && !agent.hasFinished
            && !RouteAnalysis.On // nobody runs in route analysis
            && !RouteChoiceGame.On; // nor in the route choice game
    }

    private bool IsFollowingRunner()
    {
        return followRunner && IsRunPhase();
    }

    // A row on the leaderboard was clicked: watch that runner (your own: back to your own)
    public void ShowRunner(Transform runner)
    {
        if (runner == null) return;
        Watched = runner == transform ? null : runner;
        if (agent == null) agent = GetComponent<AgentMovement>();
        bool drawing = agent != null && agent.manualPathDrawer != null && agent.manualPathDrawer.timeToDraw;
        // Without Follow runner (or while drawing) the map just jumps there; you move it about yourself from there
        if (cam != null && (!followRunner || drawing))
        {
            cam.transform.position = new Vector3(runner.position.x, runner.position.y, cam.transform.position.z);
        }
    }

    public void ToggleFollowRunner()
    {
        followRunner = !followRunner;
        RefreshFollowButtonText();

        if (!followRunner && IsRunPhase())
        {
            // Tillbaka till översikten över sträckan när följ-läget stängs av mitt i löpningen
            FocusOnCurrentLeg();
        }
    }

    // A new stretch has started while running (relays run the whole leg without stopping): turn the map so the next
    // control is straight up. When following the runner only the turn is taken, not the zoom.
    public void FocusOnNextStretch()
    {
        if (cam == null || agent == null) return;
        if (IsFollowingRunner())
        {
            if (pathManager == null) pathManager = FindObjectOfType<PathfindingManager>();
            int kontroll = agent.currentKontroll;
            if (pathManager == null || kontroll < 1 || kontroll >= pathManager.controlPoints.Count) return;
            Vector3 dir = pathManager.controlPoints[kontroll].position - pathManager.controlPoints[kontroll - 1].position;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            cam.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
            return;
        }
        FocusOnCurrentLeg();
    }

    // Every new stretch to draw starts with the whole of it on the screen (2026-11-16: some ways of getting to the next
    // stretch did not turn and zoom the map, and the player had to drag it to see the control). Once per stretch, so
    // the player can zoom and drag freely after that.
    private Transform lastDrawTarget;

    private void FocusOnNewStretchToDraw()
    {
        if (agent == null) agent = GetComponent<AgentMovement>();
        ManualPathDrawer drawer = agent != null ? agent.manualPathDrawer : null;
        if (drawer == null || !drawer.timeToDraw || RouteChoiceGame.On)
        {
            if (drawer != null && !drawer.timeToDraw) lastDrawTarget = null;
            return;
        }
        if (drawer.targetControl == null || drawer.ThisControll == null || drawer.targetControl == lastDrawTarget) return;
        lastDrawTarget = drawer.targetControl;
        FocusOnSegment(agent.WorldToGridPositionCentered(drawer.ThisControll.position),
            agent.WorldToGridPositionCentered(drawer.targetControl.position));
    }

    private void FocusOnCurrentLeg()
    {
        if (pathManager == null)
        {
            pathManager = FindObjectOfType<PathfindingManager>();
        }
        int kontroll = agent.currentKontroll;
        if (pathManager == null || kontroll < 1 || kontroll >= pathManager.controlPoints.Count)
        {
            return;
        }
        FocusOnSegment(
            agent.WorldToGridPositionCentered(pathManager.controlPoints[kontroll - 1].position),
            agent.WorldToGridPositionCentered(pathManager.controlPoints[kontroll].position)
        );
    }

    // Knappen skapas i koden så att den finns i alla banor utan att varje scen behöver ändras.
    // Den visas bara medan löparen springer, inte medan spelaren ritar.
    // A replay takes the camera over (and turns this off): the button goes with it
    public void HideFollowButton()
    {
        if (followButton != null) followButton.gameObject.SetActive(false);
    }

    private void UpdateFollowButton()
    {
        if (followButton == null)
        {
            CreateFollowButton();
            if (followButton == null)
            {
                return;
            }
        }

        // Never in a replay (2026-11-20, Anders: it does nothing there and is in the way)
        bool show = IsRunPhase() && RelayReplayView.Current == null;
        if (followButton.gameObject.activeSelf != show)
        {
            followButton.gameObject.SetActive(show);
        }
    }

    private void CreateFollowButton()
    {
        GameObject canvasObject = GameObject.FindGameObjectWithTag("Canvas");
        if (canvasObject == null)
        {
            return;
        }

        // Use the styled button that is already in the scene when there is one
        Transform existing = canvasObject.transform.Find("FollowRunnerButton");
        if (existing != null)
        {
            followButton = existing.GetComponent<Button>();
            followButtonText = existing.GetComponentInChildren<TextMeshProUGUI>(true);
            followButton.onClick.RemoveListener(ToggleFollowRunner);
            followButton.onClick.AddListener(ToggleFollowRunner);
            RefreshFollowButtonText();
            existing.gameObject.SetActive(false);
            return;
        }

        GameObject buttonObject = new GameObject("FollowRunnerButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-640f, 442f); // uppe till vänster, bredvid ångra-knappen
        rect.sizeDelta = new Vector2(240f, 70f);

        GameObject textObject = new GameObject("Text (TMP)", typeof(RectTransform));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        followButtonText = textObject.AddComponent<TextMeshProUGUI>();
        followButtonText.alignment = TextAlignmentOptions.Center;
        followButtonText.fontSize = 26f;
        followButtonText.color = Color.black;
        followButtonText.raycastTarget = false;

        followButton = buttonObject.GetComponent<Button>();
        followButton.onClick.AddListener(ToggleFollowRunner);
        RefreshFollowButtonText();
        buttonObject.SetActive(false);
    }

    private void RefreshFollowButtonText()
    {
        if (followButtonText != null)
        {
            followButtonText.text = followRunner ? "Follow runner: ON" : "Follow runner: OFF";
        }
        if (followButton != null)
        {
            followButton.image.color = followRunner ? new Color(0.6f, 0.9f, 0.6f, 1f) : Color.white;
        }
    }

    public void FocusOnSegment(Vector2Int start, Vector2Int end)
    {
        Debug.Log(start);
        Debug.Log(end);
        Debug.Log(pathManager);
        Debug.Log(cam);
        if(pathManager == null)
        {
            pathManager = FindObjectOfType<PathfindingManager>();
        }
        Vector3 startPos = pathManager.GridToWorldPosition(start.y, start.x);
        Vector3 endPos = pathManager.GridToWorldPosition(end.y, end.x);

        // riktning och mittpunkt
        Vector3 dir = (endPos - startPos).normalized;
        Vector3 midpoint = (startPos + endPos) / 2f;
        float distance = Vector3.Distance(startPos, endPos);

        // beräkna vinkel för rotation (segment blir "lodrätt" i kamera)
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        cam.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);

        // placera kameran på mitten mellan punkterna
        Vector3 camPos = midpoint;
        camPos.z = -10f;
       


        
        // Zoom so both control circles are wholly on the screen with half a circle more beyond each of them.
        // The leg runs straight up the screen, so only the height matters (a landscape screen is wider than tall).
        float circle = 37f * MapDefinition.WorldUnitsPerMeter * CourseRenderer.SizeFor(CourseLoader.LoadedMap);   // the control circle's radius (CourseRenderer)
        // (2.6 circles since 2026-11-16: the clock and tool bars cover the top and the bottom of the screen)
        legCamSize = distance / 2f + 2.6f * circle;
        cam.orthographicSize = Mathf.Max(minCamSize, legCamSize);
        cam.transform.position = camPos;
    }

    // Drag with the right or the middle mouse button (the left one draws the route)
    private void PanCamera()
    {
        int button = Input.GetMouseButton(1) || Input.GetMouseButtonDown(1) ? 1 : 2;

        if (Input.GetMouseButtonDown(button))
            dragOrigin = cam.ScreenToWorldPoint(Input.mousePosition);

        if (Input.GetMouseButton(button))
        {
            Vector3 difference = dragOrigin - cam.ScreenToWorldPoint(Input.mousePosition);



            cam.transform.position = ClampCamera(cam.transform.position + difference);

        }


    }

    private void ZoomIn()
    {
        float newSize = cam.orthographicSize - zoomStep * MapScale();

        cam.orthographicSize = Mathf.Clamp(newSize, MinZoomIn(), MaxZoomOut());
        cam.transform.position = ClampCamera(cam.transform.position);
    }

    private void ZoomOut()
    {
        float newSize = cam.orthographicSize + zoomStep * MapScale();
        cam.orthographicSize = Mathf.Clamp(newSize, MinZoomIn(), MaxZoomOut());
        cam.transform.position = ClampCamera(cam.transform.position);
    }

    // Sprint maps are drawn at a larger scale (1:4000) and are small in the world, so zoom steps and the closest
    // zoom are smaller there. With the forest steps a whole sprint leg was already as close as you could get and
    // one step out showed most of the map, so the wheel seemed to do nothing.
    private static float MapScale()
    {
        return CourseLoader.LoadedMap != null && CourseLoader.LoadedMap.IsSprint ? 0.25f : 1f;
    }

    private float MinZoomIn()
    {
        return minCamSize * MapScale();
    }

    // How far out the player may scroll: as wide as the map, and always far enough to see the whole
    // current leg. Without the second part one scroll step on a long leg pushed its ends off the screen.
    // On sprint maps also as tall as the map, since the camera turns with the leg and sprint maps are often narrow.
    private float MaxZoomOut()
    {
        float mapLimit = (mapRenderer.bounds.size.x / 2f) / cam.aspect;
        if (MapScale() < 1f) mapLimit = Mathf.Max(mapLimit, mapRenderer.bounds.size.y / 2f);
        return Mathf.Max(MinZoomIn(), Mathf.Min(maxCamSize, Mathf.Max(mapLimit, legCamSize)));
    }

    private Vector3 ClampCamera(Vector3 targetPosition)
    {
        float camHeight = cam.orthographicSize;
        float camWidth = cam.orthographicSize * cam.aspect;

        float minX = mapMinX + camWidth;
        float maxX = mapMaxX - camWidth;
        float minY = mapMinY + camHeight;
        float maxY = mapMaxY - camHeight;

        // Zoomed out wider than the map allows: keep the map in the middle instead of pinning it to one side
        float newX = minX <= maxX ? Mathf.Clamp(targetPosition.x, minX, maxX) : (mapMinX + mapMaxX) / 2f;
        float newY = minY <= maxY ? Mathf.Clamp(targetPosition.y, minY, maxY) : (mapMinY + mapMaxY) / 2f;

        return new Vector3(newX, newY, targetPosition.z);
    }
    public void CanWrite()
    {
        
        canWrite = true;
        
    }
    public void CantWrite()
    {
        
        canWrite = false;
        
    }
}
