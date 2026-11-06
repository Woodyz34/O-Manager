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
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit); not a new race
        if (mode == LoadSceneMode.Additive) return;
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
                float grab = Mathf.Max(manager.cellSize * 3f, 0.12f);   // at least 12 m, so the start is easy to hit
                if (Input.GetMouseButtonDown(0))
                {
                    // A line that was stopped (it could not get past something) is carried on from its end
                    if (smoothPath.Count > 0 && Vector3.Distance(worldPos, smoothPath[smoothPath.Count - 1]) < grab)
                    {
                        isDrawing = true;
                    }
                    else if (Vector3.Distance(worldPos, ThisControll.position) < grab)
                    {
                        isDrawing = true;
                        ClearLine();
                    }
                }
                if (Input.GetMouseButtonUp(0)) isDrawing = false;

                if (isDrawing && Input.GetMouseButton(0))
                {
                    // Sprint maps have 2 m cells, so points are kept at least once a cell there
                    if (smoothPath.Count == 0 ||
                        Vector3.Distance(smoothPath[smoothPath.Count - 1], worldPos) > Mathf.Min(minPointDistance, manager.cellSize))
                    {
                        if (!AddPoint(worldPos)) return;   // stopped where the line can not get on
                    }

                    // Check reach target
                    if (Vector3.Distance(worldPos, targetControl.position) < manager.cellSize)
                    {
                        Debug.Log("har ritat färdigt");
                        isDrawing = false;
                        timeToDraw = false;
                        OnPathFinished();
                        ClearLine();   // the next stretch starts from its own control
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
        FitLineWidth();
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

        // The runner follows the drawn line; where it crossed ground nobody can run on (a building, private land,
        // a lake ...) it takes the way round that was found while drawing. Route analysis keeps such lines as they
        // are (that ground then counts as very slow).
        var route = new List<Vector2Int>();
        foreach (Vector2Int c in gridPath) if (RouteAnalysis.On || Passable(c)) route.Add(c);
        foreach (List<Vector2Int> d in detours) route.AddRange(d);
        // (Route analysis lets the route run a few metres from the line, so a line drawn roughly along a road follows it)
        int corridor = RouteAnalysis.On ? RouteAnalysis.CorridorCells(manager) : 1;
        List<Vector2Int> corridorPath = CorridorPath(route, gridPath[0], gridPath[gridPath.Count - 1], corridor);
        if ((corridorPath == null || corridorPath.Count == 0) && CrossesBlocked(gridPath) && !RouteAnalysis.On)
        {
            corridorPath = FindOptimizedPathInCorridor(gridPath, CorrectionCells);
        }
        if (!RouteAnalysis.On && corridorPath != null && corridorPath.Count > 0)
        {
            // Keep the route that will be run on the map (relays keep the drawn stretches)
            smoothPath.Clear();
            foreach (Vector2Int c in corridorPath) smoothPath.Add(manager.GridToWorldPosition(c.y, c.x));
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
            // Route analysis: the stretches drawn before are not left on the map (they were in the way while drawing
            // the next one); the routes are seen again in the comparison
            if (!RouteAnalysis.On || RelayRace.Active != null) KeepLine();
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
    // While the line is drawn, each piece that goes over ground nobody can run on (a building, a fence, a lake ...)
    // is checked as soon as the line is out on the other side: when there is a way round within 100 m of it, the
    // runner takes that way, without a word. Otherwise the line stops just before the piece, and the player carries on
    // drawing from the end of the line (a piece longer than 200 m stops it at once).

    const float CorrectionMeters = 100f;

    // How many cells 100 m is (10 cells on a forest map, 50 on a sprint map)
    int CorrectionCells => Mathf.Max(1, Mathf.CeilToInt(CorrectionMeters * MapDefinition.WorldUnitsPerMeter / manager.cellSize));

    readonly List<int> cellPoint = new List<int>();                    // for each cell of gridPath: the smoothPath point that added it
    readonly List<List<Vector2Int>> detours = new List<List<Vector2Int>>();
    readonly List<int> detourFrom = new List<int>();                  // the gridPath index each detour starts at
    int blockedFrom = -1;                                             // gridPath index of the last good cell before a closed piece

    bool Passable(Vector2Int c) => manager.speedGrid2D[c.y, c.x] > 0.01f;

    bool CrossesBlocked(List<Vector2Int> cells)
    {
        foreach (Vector2Int c in cells)
        {
            if (!Passable(c)) return true;
        }
        return false;
    }

    // The drawn line as wide as the prefab makes it on forest maps, thinner on sprint maps in every game mode (in scale
    // with the smaller controls there), and a bit thinner in route analysis, so it does not cover what you want to see
    float normalLineWidth = -1f;

    void FitLineWidth()
    {
        if (line == null) return;
        if (normalLineWidth < 0f) normalLineWidth = line.widthMultiplier;
        bool sprint = CourseLoader.LoadedMap != null ? CourseLoader.LoadedMap.IsSprint
            : manager != null && manager.cellSize / MapDefinition.WorldUnitsPerMeter < 5f;
        float share = sprint ? CourseRenderer.SprintSize : (RouteAnalysis.On && RelayRace.Active == null ? 0.6f : 1f);
        line.widthMultiplier = normalLineWidth * share;
    }

    void ClearLine()
    {
        FitLineWidth();
        gridPath.Clear();
        smoothPath.Clear();
        cellPoint.Clear();
        detours.Clear();
        detourFrom.Clear();
        blockedFrom = -1;
        if (line != null) line.positionCount = 0;
    }

    // Adds a drawn point and the cells up to it. Returns false when the line was stopped.
    bool AddPoint(Vector3 point)
    {
        smoothPath.Add(point);
        line.positionCount = smoothPath.Count;
        line.SetPosition(smoothPath.Count - 1, point);
        int pointIndex = smoothPath.Count - 1;
        Vector3 from = pointIndex == 0 ? point : smoothPath[pointIndex - 1];
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, point) / (manager.cellSize * 0.5f)));
        for (int s = pointIndex == 0 ? 0 : 1; s <= steps; s++)
        {
            Vector2Int g = WorldToGrid(Vector3.Lerp(from, point, s / (float)steps));
            if (gridPath.Count > 0 && gridPath[gridPath.Count - 1] == g) continue;
            // A diagonal step goes through a cell beside it too: otherwise the line could slip between two closed
            // cells that only touch at a corner (a fence across the grid)
            if (gridPath.Count > 0)
            {
                Vector2Int last = gridPath[gridPath.Count - 1];
                if (last.x != g.x && last.y != g.y && !AddCell(new Vector2Int(last.x, g.y), pointIndex)) return false;
            }
            if (!AddCell(g, pointIndex)) return false;
        }
        return true;
    }

    // Adds one cell of the drawn line and checks closed ground. Returns false when the line was stopped.
    bool AddCell(Vector2Int g, int pointIndex)
    {
        {
            gridPath.Add(g);
            cellPoint.Add(pointIndex);
            if (RouteAnalysis.On) return true;
            bool open = Passable(g);
            if (!open && blockedFrom < 0)
            {
                blockedFrom = gridPath.Count - 2;   // the last good cell (-1 when the line starts on closed ground)
            }
            else if (!open && blockedFrom >= 0 && (gridPath.Count - 1 - blockedFrom) * manager.cellSize > 2f * CorrectionMeters * MapDefinition.WorldUnitsPerMeter)
            {
                StopLine();
                return false;
            }
            else if (open && blockedFrom >= 0)
            {
                List<Vector2Int> way = blockedFrom < 0 ? null : SearchInBox(gridPath[blockedFrom], g, CorrectionCells, null);
                if (way == null || way.Count == 0)
                {
                    StopLine();
                    return false;
                }
                detours.Add(way);
                detourFrom.Add(blockedFrom);
                blockedFrom = -1;
            }
        }
        return true;
    }

    // Cuts the line back to just before the closed piece and stops drawing; the player carries on from there
    void StopLine()
    {
        int keepCells = Mathf.Max(1, blockedFrom + 1);
        int keepPoints = Mathf.Max(1, cellPoint[keepCells - 1]);
        var removed = smoothPath.GetRange(keepPoints, smoothPath.Count - keepPoints);
        FlashLine(removed, new Color(0.85f, 0.15f, 0.1f), 1.2f);
        TrimTo(keepPoints);
        isDrawing = false;
        UIToast.Show("You can't get past there. Carry on drawing from the end of your line, round it.", 3f);
    }

    // Keeps the first count drawn points and the cells and detours that belong to them
    void TrimTo(int count)
    {
        count = Mathf.Clamp(count, 0, smoothPath.Count);
        smoothPath.RemoveRange(count, smoothPath.Count - count);
        int cells = 0;
        while (cells < cellPoint.Count && cellPoint[cells] < count) cells++;
        gridPath.RemoveRange(cells, gridPath.Count - cells);
        cellPoint.RemoveRange(cells, cellPoint.Count - cells);
        for (int d = detours.Count - 1; d >= 0; d--)
        {
            if (detourFrom[d] + 1 < cells && DetourEnds(d) < cells) continue;
            detours.RemoveAt(d);
            detourFrom.RemoveAt(d);
        }
        blockedFrom = -1;
        for (int i = cells - 1; i >= 0 && !RouteAnalysis.On; i--)
        {
            if (Passable(gridPath[i])) break;
            blockedFrom = i - 1;
        }
        if (line != null)
        {
            line.positionCount = smoothPath.Count;
            if (smoothPath.Count > 0) line.SetPositions(smoothPath.ToArray());
        }
    }

    // The gridPath index where detour d comes out on good ground again
    int DetourEnds(int d)
    {
        Vector2Int end = detours[d][detours[d].Count - 1];
        for (int i = detourFrom[d] + 1; i < gridPath.Count; i++) if (gridPath[i] == end) return i;
        return int.MaxValue;
    }

    // The fastest way from a to b inside a box round them (margin cells on each side), only over the allowed cells
    // when a set is given. Works on a small copy of that part of the grid, so it is quick even on a 2 m grid.
    List<Vector2Int> SearchInBox(Vector2Int a, Vector2Int b, int margin, HashSet<Vector2Int> allowed)
    {
        return SearchInBox(new List<Vector2Int> { a, b }, a, b, margin, allowed);
    }

    List<Vector2Int> SearchInBox(List<Vector2Int> around, Vector2Int a, Vector2Int b, int margin, HashSet<Vector2Int> allowed)
    {
        int rows = manager.speedGrid2D.GetLength(0), cols = manager.speedGrid2D.GetLength(1);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (Vector2Int c in around)
        {
            x0 = Mathf.Min(x0, c.x); y0 = Mathf.Min(y0, c.y); x1 = Mathf.Max(x1, c.x); y1 = Mathf.Max(y1, c.y);
        }
        x0 = Mathf.Max(0, x0 - margin); y0 = Mathf.Max(0, y0 - margin);
        x1 = Mathf.Min(cols - 1, x1 + margin); y1 = Mathf.Min(rows - 1, y1 + margin);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var speed = new float[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                speed[y, x] = allowed == null || allowed.Contains(new Vector2Int(x + x0, y + y0)) ? manager.speedGrid2D[y + y0, x + x0] : 0f;
        Dictionary<string, float[,]> slopes = null;
        if (manager.slopeFactors != null)
        {
            slopes = new Dictionary<string, float[,]>();
            foreach (var pair in manager.slopeFactors)
            {
                if (pair.Value == null) continue;
                var part = new float[h, w];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        part[y, x] = pair.Value[y + y0, x + x0];
                slopes[pair.Key] = part;
            }
        }
        var offset = new Vector2Int(x0, y0);
        List<Vector2Int> path = new AStarPathfinder(speed, slopes).FindPath(a - offset, b - offset, 1);
        if (path == null) return null;
        for (int i = 0; i < path.Count; i++) path[i] += offset;
        return path;
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

        
        TrimTo(smoothPath.Count - Mathf.Min(count, smoothPath.Count));
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
        ClearLine();
    }

    // The fastest way from the first to the last cell that stays within corridorRadius cells of the drawn cells.
    // Only reads the grid, so it can also be run on a background thread.
    public List<Vector2Int> FindOptimizedPathInCorridor(List<Vector2Int> drawnPath, int corridorRadius)
    {
        if (drawnPath == null || drawnPath.Count == 0) return null;
        return CorridorPath(drawnPath, drawnPath[0], drawnPath[drawnPath.Count - 1], corridorRadius);
    }

    List<Vector2Int> CorridorPath(List<Vector2Int> cells, Vector2Int start, Vector2Int end, int corridorRadius)
    {
        int rows = manager.speedGrid2D.GetLength(0);
        int cols = manager.speedGrid2D.GetLength(1);
        var allowed = new HashSet<Vector2Int>();
        foreach (Vector2Int p in cells)
        {
            for (int dy = -corridorRadius; dy <= corridorRadius; dy++)
            {
                for (int dx = -corridorRadius; dx <= corridorRadius; dx++)
                {
                    int nx = p.x + dx, ny = p.y + dy;
                    if (nx >= 0 && nx < cols && ny >= 0 && ny < rows) allowed.Add(new Vector2Int(nx, ny));
                }
            }
        }
        var around = new List<Vector2Int>(cells) { start, end };
        return SearchInBox(around, start, end, corridorRadius + 1, allowed);
    }
}
