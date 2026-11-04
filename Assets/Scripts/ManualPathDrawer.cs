using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class ManualPathDrawer : MonoBehaviour
{
    [HideInInspector]
    public Transform ThisControll;
    [HideInInspector]
    public Transform targetControl; // nästa kontroll i världen
    private LineRenderer line;
    private AgentMovement agent;
    private List<Vector2Int> gridPath = new List<Vector2Int>();
    [HideInInspector]
    public PathfindingManager manager;
    public bool isDrawing = false;
    public bool timeToDraw = false;
    private bool canDraw = true;
    public float distans;

    private List<Vector3> smoothPath = new List<Vector3>();
    public float minPointDistance = 0.1f; // hur ofta nya punkter läggs till

    // Relays: the whole leg is drawn before the start, one stretch after the other. The finished stretches stay on the map.
    [HideInInspector] public bool relayMode = false;
    private int relayStretch = 0;
    private readonly List<GameObject> keptLines = new List<GameObject>();


    public void Awake()
    {
        line = GetComponent<LineRenderer>();
        agent = GetComponent<AgentMovement>();
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (SceneManager.GetActiveScene().name == "Lobby")
        {
            return;
        }

        StartCoroutine(WaitForPathfindingManager());
        if (line == null)
        {
            Debug.LogError("AgentPrefab saknar Line komponent!");
            return;
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The object can be destroyed by another scene-loaded handler earlier in the same call (e.g. when the
        // network session shuts down as the scene changes); Unity then still calls this handler
        if (this == null)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            return;
        }
        if (scene.name == "Lobby") return;

        if (manager == null)
        {
            StartCoroutine(WaitForPathfindingManager());
        }
    }

    private IEnumerator WaitForPathfindingManager()
    {
        // Vänta tills PathfindingManager dyker upp i scenen
        while (manager == null)
        {
            if(SceneManager.GetActiveScene().name == "Lobby")
            {
                break;
            }
            manager = FindObjectOfType<PathfindingManager>();
            yield return null; // vänta en frame
        }

        Debug.Log("PathfindingManager hittad! Fortsätter initiering...");
        InitializeAfterManagerFound();
    }
    private void InitializeAfterManagerFound()
    {
        ThisControll = manager.controlPoints[0]; // startpunkten
        targetControl = manager.controlPoints[1]; // första kontrollen
        line.positionCount = 0;
    }

    void Update()
    {
        if(ThisControll == null)
        {
            return;
        }
        if (SceneManager.GetActiveScene().name == "Lobby")
        {
            return;
        }
        if (!agent.Object.HasStateAuthority)
        {
            return;
        }
        if (timeToDraw)
        {
            Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            worldPos.z = -1;
            // Kolla om musen är över UI
            bool overUI = IsPointerOverTaggedCanvas() || EscMenu.IsOpen; // no drawing behind the Esc menu
            // Placing a bear or moving a control takes the clicks on the map instead
            if (GameModeTools.TakesMouse)
            {
                return;
            }
            if (!overUI && canDraw) // Endast rita om vi inte trycker på UI
            {
                if (Input.GetMouseButtonDown(0) && Vector3.Distance(worldPos, ThisControll.position) < manager.cellSize * 3f)
                {
                    isDrawing = true;
                    gridPath.Clear();
                    smoothPath.Clear();
                    line.positionCount = 0;
                }

                if (isDrawing && Input.GetMouseButton(0))
                {
                    // Sprint maps have 2 m cells, so points are kept at least once a cell there
                    if (smoothPath.Count == 0 ||
                        Vector3.Distance(smoothPath[smoothPath.Count - 1], worldPos) > Mathf.Min(minPointDistance, manager.cellSize))
                    {
                        smoothPath.Add(worldPos);

                        line.positionCount = smoothPath.Count;
                        line.SetPosition(smoothPath.Count - 1, worldPos);
                    }

                    // Check reach target
                    if (Vector3.Distance(worldPos, targetControl.position) < manager.cellSize)
                    {
                        Debug.Log("har ritat färdigt");
                        isDrawing = false;
                        timeToDraw = false;
                        OnPathFinished();
                        line.positionCount = 0;
                    }
                }
            }
        }
    }

    public bool IsPointerOverTaggedCanvas(string tag = "Canvas")
    {
        // Skapa ett nytt PointerEventData för nuvarande musposition
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = Input.mousePosition;

        // Samla alla UI-element under musen
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        // Kolla om något av de träffade objekten tillhör en Canvas med rätt tag
        foreach (RaycastResult result in results)
        {
            Canvas parentCanvas = result.gameObject.GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas.CompareTag(tag))
            {
                return true; // Vi klickade på ett UI-element på en Canvas med rätt tag
            }
        }

        return false; // Ingen träff på rätt Canvas
    }

    // Starts drawing a new relay leg from the start (the leg's points are already in manager.controlPoints)
    public void BeginRelayLeg()
    {
        relayMode = true;
        relayStretch = 0;
        ClearKeptLines();
        gridPath.Clear();
        smoothPath.Clear();
        if (line != null) line.positionCount = 0;
        isDrawing = false;
        ThisControll = manager.controlPoints[0];
        targetControl = manager.controlPoints[1];
        timeToDraw = true;
    }

    public void ClearKeptLines()
    {
        foreach (GameObject kept in keptLines)
        {
            if (kept != null) Destroy(kept);
        }
        keptLines.Clear();
    }

    // Leaves a copy of the stretch just drawn on the map, so the whole leg can be seen while drawing the rest
    void KeepLine()
    {
        if (line == null || smoothPath.Count < 2) return;
        GameObject copy = new GameObject("Drawn stretch " + (relayStretch + 1));
        LineRenderer kept = copy.AddComponent<LineRenderer>();
        kept.sharedMaterial = line.sharedMaterial;
        kept.widthMultiplier = line.widthMultiplier;
        kept.widthCurve = line.widthCurve;
        kept.colorGradient = line.colorGradient;
        kept.numCapVertices = line.numCapVertices;
        kept.numCornerVertices = line.numCornerVertices;
        kept.sortingLayerID = line.sortingLayerID;
        kept.sortingOrder = line.sortingOrder;
        kept.useWorldSpace = true;
        kept.positionCount = smoothPath.Count;
        kept.SetPositions(smoothPath.ToArray());
        keptLines.Add(copy);
    }

    void OnPathFinished()
    {
        if (smoothPath.Count < 2)
        {
            Debug.LogWarning("För kort path ritad.");
            if (relayMode) timeToDraw = true; // draw the stretch again
            return;
        }

        // Every cell the line goes through, also between the points (they can be several cells apart on a sprint map)
        for (int p = 0; p < smoothPath.Count; p++)
        {
            Vector3 from = p == 0 ? smoothPath[0] : smoothPath[p - 1];
            Vector3 to = smoothPath[p];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / (manager.cellSize * 0.5f)));
            for (int s = p == 0 ? 0 : 1; s <= steps; s++)
            {
                Vector2Int g = WorldToGrid(Vector3.Lerp(from, to, s / (float)steps));
                if (gridPath.Count == 0 || gridPath[gridPath.Count - 1] != g)
                {
                    gridPath.Add(g);
                }
            }
        }

        // Skapa korridor runt pathen
        List<Vector2Int> corridorPath = FindOptimizedPathInCorridor(gridPath, 1);

        // A line over ground nobody can run on (a building, private land, a lake ...) is moved round it when the way
        // round stays within 20 m of the line; otherwise the stretch has to be drawn again.
        // Route analysis keeps such lines as they are (that ground then counts as very slow).
        if (CrossesBlocked(gridPath) && !RouteAnalysis.On)
        {
            if (corridorPath == null || corridorPath.Count == 0)
            {
                corridorPath = FindOptimizedPathInCorridor(gridPath, CorrectionCells);
            }
            if (corridorPath == null || corridorPath.Count == 0)
            {
                RejectLine();
                return;
            }
            ShowCorrection(corridorPath);
        }
        if (relayMode)
        {
            if (corridorPath == null || corridorPath.Count == 0)
            {
                corridorPath = FindOptimizedPathInCorridor(gridPath, 10);
            }
            // Route analysis: a line over ground nobody can run on (a lake) has no route round it, so the drawn
            // line itself is kept; that ground then counts as very slow
            if ((corridorPath == null || corridorPath.Count == 0) && RouteAnalysis.On)
            {
                corridorPath = new List<Vector2Int>(gridPath);
            }
            agent.relaySegments.Add(corridorPath);
            KeepLine();
            // The next stretch starts from scratch
            gridPath.Clear();
            smoothPath.Clear();
            relayStretch++;
            if (RelayRace.Active != null)
            {
                RelayRace.Active.OnStretchDrawn(relayStretch);
            }
            if (relayStretch < manager.controlPoints.Count - 1)
            {
                // On to the next stretch
                ThisControll = manager.controlPoints[relayStretch];
                targetControl = manager.controlPoints[relayStretch + 1];
                timeToDraw = true;
                agent.CamController.FocusOnSegment(
                    agent.WorldToGridPositionCentered(ThisControll.position),
                    agent.WorldToGridPositionCentered(targetControl.position));
                return;
            }
            // Route analysis: the whole course is drawn; nobody runs, the routes are compared instead
            if (RouteAnalysis.On && RelayRace.Active == null)
            {
                ClearKeptLines();
                relayMode = false;
                RouteAnalysis.OnCourseDrawn(agent);
                return;
            }
            // The whole leg is drawn: the runner starts on the first stretch when every team is ready.
            // The drawn lines are taken off the map again.
            ClearKeptLines();
            agent.Initialize(new List<Vector2Int>(agent.relaySegments[0]));
            if (manager.multiplayer)
            {
                if (!agent.Object.HasInputAuthority)
                {
                    return;
                }
                agent.WaitingForAllPlayersToDraw = true;
                GetComponent<NetworkPlayer>().RPC_SetHasDrawn();
            }
            else
            {
                RelayRace.Active?.OnAllDrawn(agent);
            }
            return;
        }
        if (manager.multiplayer)
        {
            if (!agent.Object.HasInputAuthority)
            {
                return;
            }
            agent.WaitingForAllPlayersToDraw = true;
            GetComponent<NetworkPlayer>().RPC_SetHasDrawn();
        }
        else
        {
            agent.canRunNextSegment = true;
        }

        // Om vi hittade en bättre path i korridoren -> använd den
        if (corridorPath != null && corridorPath.Count > 0)
        {
            agent.Initialize(corridorPath);
        }
        else
        {
            corridorPath = FindOptimizedPathInCorridor(gridPath, 10);
            Debug.Log("Ingen optimerad path hittades, använder ritad path.");
            if (corridorPath == null || corridorPath.Count == 0)
            {
                // The line goes over ground nobody can run on (straight over a lake), so there is no route near it.
                // Without a route the runner stood still for good; it takes the way round instead.
                corridorPath = WayRound(gridPath[0], gridPath[gridPath.Count - 1]);
            }
            agent.Initialize(corridorPath);
        }

    }

    // ---- Lines over ground nobody can run on ----

    const float CorrectionMeters = 20f;

    // How many cells 20 m is (2 cells on a forest map, 10 on a sprint map)
    int CorrectionCells => Mathf.Max(1, Mathf.CeilToInt(CorrectionMeters * MapDefinition.WorldUnitsPerMeter / manager.cellSize));

    bool CrossesBlocked(List<Vector2Int> cells)
    {
        foreach (Vector2Int c in cells)
        {
            if (manager.speedGrid2D[c.y, c.x] <= 0.01f) return true;
        }
        return false;
    }

    void RejectLine()
    {
        UIToast.Show("You can't run there: buildings, private land, lakes and other closed areas are in the way. Draw the stretch again, round them.", 5f);
        FlashLine(new List<Vector3>(smoothPath), new Color(0.85f, 0.15f, 0.1f), 1.5f);
        gridPath.Clear();
        smoothPath.Clear();
        line.positionCount = 0;
        isDrawing = false;
        timeToDraw = true;
    }

    // Shows the route the runner will take instead, in orange for a moment
    void ShowCorrection(List<Vector2Int> cells)
    {
        UIToast.Show("Your line went through a closed area, so the route was moved round it.", 3f);
        var points = new List<Vector3>(cells.Count);
        foreach (Vector2Int c in cells) points.Add(manager.GridToWorldPosition(c.y, c.x));
        FlashLine(points, new Color(0.91f, 0.45f, 0.16f), 2f);
        // A relay keeps the drawn stretches on the map: keep the route that will be run
        smoothPath.Clear();
        smoothPath.AddRange(points);
    }

    void FlashLine(List<Vector3> points, Color color, float seconds)
    {
        if (line == null || points.Count < 2) return;
        GameObject copy = new GameObject("Route flash");
        LineRenderer flash = copy.AddComponent<LineRenderer>();
        flash.sharedMaterial = line.sharedMaterial;
        flash.widthMultiplier = line.widthMultiplier;
        flash.widthCurve = line.widthCurve;
        flash.startColor = color;
        flash.endColor = color;
        flash.numCapVertices = line.numCapVertices;
        flash.numCornerVertices = line.numCornerVertices;
        flash.sortingLayerID = line.sortingLayerID;
        flash.sortingOrder = line.sortingOrder + 1;
        flash.useWorldSpace = true;
        flash.positionCount = points.Count;
        flash.SetPositions(points.ToArray());
        Destroy(copy, seconds);
    }

    // The fastest route over the whole map from where the line starts to where it ends (the control when that fails)
    List<Vector2Int> WayRound(Vector2Int from, Vector2Int to)
    {
        AStarPathfinder pathfinder = new AStarPathfinder(manager.speedGrid2D, manager.slopeFactors);
        List<List<Vector2Int>> paths = pathfinder.GetAllPaths(from, to, 1);
        if (paths.Count == 0 || paths[0] == null || paths[0].Count == 0)
        {
            paths = pathfinder.GetAllPaths(WorldToGrid(ThisControll.position), WorldToGrid(targetControl.position), 1);
        }
        return paths.Count > 0 && paths[0] != null && paths[0].Count > 0 ? paths[0] : new List<Vector2Int>(gridPath);
    }

    Vector2Int WorldToGrid(Vector3 worldPos)
    {
        int j = Mathf.Clamp((int)((worldPos.x + (manager.speedGrid2D.GetLength(1) * manager.cellSize) / 2f) / manager.cellSize),
                            0, manager.speedGrid2D.GetLength(1) - 1);
        int i = Mathf.Clamp((int)((worldPos.y + (manager.speedGrid2D.GetLength(0) * manager.cellSize) / 2f) / manager.cellSize),
                            0, manager.speedGrid2D.GetLength(0) - 1);

        return new Vector2Int(j, i); // x=kolumn=j, y=rad=i
    }


    public void RemoveLastPoints(int count = 5)
    {
        Debug.Log(smoothPath.Count + " gridpath " + line.positionCount + " line.positionCount");
        if (smoothPath.Count == 0 || line.positionCount == 0) return;

        
        // Begränsa count till antalet punkter vi faktiskt har
        int removeCount = Mathf.Min(count, smoothPath.Count);

        // Ta bort från listan
        smoothPath.RemoveRange(smoothPath.Count - removeCount, removeCount);

        // Ta bort från LineRenderer
        line.positionCount -= removeCount;
    }
    public void usingMoveTool()
    {
        canDraw = false;
    }
    public void usingDrawTool()
    {
        canDraw = true;
    }

    // Throws away the line being drawn (e.g. when the control it was heading for is moved)
    public void CancelCurrentLine()
    {
        if (relayMode) return;
        isDrawing = false;
        gridPath.Clear();
        smoothPath.Clear();
        if (line != null) line.positionCount = 0;
    }

    // Only reads the grid, so it can also be run on a background thread
    public List<Vector2Int> FindOptimizedPathInCorridor(List<Vector2Int> drawnPath, int corridorRadius)
    {
        int rows = manager.speedGrid2D.GetLength(0);
        int cols = manager.speedGrid2D.GetLength(1);

        // Gör en kopia av grid
        float[,] corridorGrid = new float[rows, cols];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                corridorGrid[i, j] = 0f; // default: blockerat
            }
        }

        // Tillåt celler inom korridoren
        foreach (var p in drawnPath)
        {
            for (int dy = -corridorRadius; dy <= corridorRadius; dy++)
            {
                for (int dx = -corridorRadius; dx <= corridorRadius; dx++)
                {
                    int nx = p.x + dx;
                    int ny = p.y + dy;

                    if (nx >= 0 && nx < cols && ny >= 0 && ny < rows)
                    {
                        corridorGrid[ny, nx] = manager.speedGrid2D[ny, nx];
                    }
                }
            }
        }

        // Kör A* i korridoren
        var pathfinder = new AStarPathfinder(corridorGrid, manager.slopeFactors);
        Vector2Int start = drawnPath[0];
        Vector2Int end = drawnPath[drawnPath.Count-1];
        List<Vector2Int> optimizedPath = pathfinder.FindPath(start, end, 1);

        return optimizedPath;
    }
}
