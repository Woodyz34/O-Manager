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
                    if (smoothPath.Count == 0 ||
                        Vector3.Distance(smoothPath[smoothPath.Count - 1], worldPos) > minPointDistance)
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

        foreach (var wp in smoothPath)
        {
            Vector2Int g = WorldToGrid(wp);

            if (gridPath.Count == 0 || gridPath[gridPath.Count - 1] != g)
            {
                gridPath.Add(g);
            }
        }

        // Skapa korridor runt pathen
        List<Vector2Int> corridorPath = FindOptimizedPathInCorridor(gridPath, 1);
        if (relayMode)
        {
            if (corridorPath == null || corridorPath.Count == 0)
            {
                corridorPath = FindOptimizedPathInCorridor(gridPath, 10);
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
            agent.Initialize(corridorPath);
        }

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

    List<Vector2Int> FindOptimizedPathInCorridor(List<Vector2Int> drawnPath, int corridorRadius)
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
