using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Route analysis game mode, for learning route choice (e.g. in a club). Nobody picks a runner and nobody runs:
// everyone draws the whole course, stretch after stretch, the way they would run it themselves in a real race.
// When everyone has drawn, the routes are compared leg by leg on the map (RouteCompareView): every drawn route with
// the time a standard runner would need on it, and the computer's fastest route. Played on its own.
//
// Each player works out their own routes (the drawn line made into a route with A*, as in a race) and shares a
// simplified copy and the times on their NetworkPlayer, so everyone can draw everyone's routes.
public static class RouteAnalysis
{
    public const float StandardSpeed = 85f;
    public const string RunnerName = "Standard runner";

    public static bool On => ExtraModes.Is(ExtraMode.RouteAnalysis) && RelayRace.Active == null;

    // Instead of the choose-a-runner screen: everyone is ready at once. The standard runner's speed is only used to
    // turn the routes into times.
    public static void Begin(NetworkPlayer local)
    {
        AgentMovement agent = local.agentMovement;
        CardData standard = new CardData(RunnerName, StandardSpeed, 100f, null, null, "", true, null);
        agent.SetRunner(standard);
        local.relayRunnerName = RunnerName;
        if (local.manager != null && local.manager.ChooseRunnerPanel != null)
        {
            local.manager.ChooseRunnerPanel.SetActive(false);
        }
        if (GameModeRuntime.Host.GetComponent<RouteAnalysisWatcher>() == null)
        {
            GameModeRuntime.Host.gameObject.AddComponent<RouteAnalysisWatcher>();
        }
        local.SetHasChoosenRunner();
        // Nobody runs, so the race leaderboard has nothing to show (it also showed through the comparison panel)
        GameObject board = GameObject.Find("LeaderboardPanel");
        if (board != null) board.SetActive(false);
        UIToast.Show("Route analysis: draw the whole course, stretch by stretch, the way you would run it yourself. "
            + "Nobody runs. When everyone is done, you compare everyone's route choices.", 10f);
    }

    // Everyone is ready: draw the whole course before anything else happens (the same drawing as a relay leg)
    public static void StartDrawing(AgentMovement agent)
    {
        agent.relaySegments = new List<List<Vector2Int>>();
        agent.manualPathDrawer.BeginRelayLeg();
        agent.startView();
    }

    // The last stretch is drawn: work out the times and share the routes
    public static void OnCourseDrawn(AgentMovement agent)
    {
        NetworkPlayer np = agent.GetComponent<NetworkPlayer>();
        PathfindingManager manager = agent.pathfindingManager;
        agent.manualPathDrawer.timeToDraw = false;
        agent.canRunNextSegment = false;
        agent.WaitingForAllPlayersToDraw = true; // stops the clock
        if (np == null || manager == null) return;

        List<List<Vector2Int>> legs = agent.relaySegments;
        int cols = manager.speedGrid2D.GetLength(1);
        int count = Mathf.Min(legs.Count, np.analysisLegTimes.Length);

        // The routes, made simpler until they fit in what is shared
        float tolerance = 0.8f;
        List<List<Vector2Int>> simple = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            simple = new List<List<Vector2Int>>();
            int total = 0;
            for (int k = 0; k < count; k++)
            {
                List<Vector2Int> s = Simplify(legs[k], tolerance);
                simple.Add(s);
                total += s.Count;
            }
            if (total <= np.analysisPoints.Length) break;
            tolerance *= 1.6f;
        }
        int at = 0;
        for (int k = 0; k < count; k++)
        {
            foreach (Vector2Int cell in simple[k])
            {
                if (at >= np.analysisPoints.Length) break;
                np.analysisPoints.Set(at++, cell.y * cols + cell.x);
            }
            np.analysisLegEnds.Set(k, at);
            np.analysisLegTimes.Set(k, Seconds(legs[k], manager, agent.originalBaseSpeed));
        }
        np.analysisLegCount = count;
        np.analysisDone = true;
    }

    // The time the standard runner needs on a route, from the same speeds the runners use in a race
    // (speed = base speed x ground x slope; the competition clock runs GameSecondsPerSecond)
    public static int Seconds(List<Vector2Int> route, PathfindingManager manager, float baseSpeed)
    {
        if (route == null || route.Count < 2 || baseSpeed <= 0f) return 0;
        float cost = PathCost(manager.speedGrid2D, manager.slopeFactors, route);
        if (cost <= 0f) return 0;
        float realSeconds = cost * manager.cellSize / baseSpeed;
        return Mathf.RoundToInt(realSeconds * PackRunning.GameSecondsPerSecond);
    }

    // The same cost as A* uses: distance / ground speed / slope, step by step. Cells that are not next to each other
    // are filled in with a straight line.
    static float PathCost(float[,] speed, Dictionary<string, float[,]> slopes, List<Vector2Int> route)
    {
        float cost = 0f;
        for (int i = 0; i < route.Count - 1; i++)
        {
            Vector2Int a = route[i];
            Vector2Int b = route[i + 1];
            int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            Vector2Int from = a;
            for (int s = 1; s <= steps; s++)
            {
                Vector2Int to = new Vector2Int(
                    Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, (float)s / steps)),
                    Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, (float)s / steps)));
                float cellSpeed = speed[to.y, to.x];
                if (cellSpeed <= 0.01f) cellSpeed = 0.05f;
                float slope = 1f;
                string dir = Direction(from, to);
                if (slopes != null && slopes.TryGetValue(dir, out float[,] grid)) slope = Mathf.Max(0.05f, grid[from.y, from.x]);
                float distance = (to.x != from.x && to.y != from.y) ? 1.414f : 1f;
                cost += distance / cellSpeed / slope;
                from = to;
            }
        }
        return cost;
    }

    static string Direction(Vector2Int from, Vector2Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        if (dx == 0 && dy > 0) return "N";
        if (dx > 0 && dy > 0) return "NE";
        if (dx > 0 && dy == 0) return "E";
        if (dx > 0 && dy < 0) return "SE";
        if (dx == 0 && dy < 0) return "S";
        if (dx < 0 && dy < 0) return "SW";
        if (dx < 0 && dy == 0) return "W";
        return "NW";
    }

    // Douglas-Peucker: keeps the corners of a route, drops the cells in between
    static List<Vector2Int> Simplify(List<Vector2Int> route, float tolerance)
    {
        if (route == null || route.Count <= 2) return route != null ? new List<Vector2Int>(route) : new List<Vector2Int>();
        bool[] keep = new bool[route.Count];
        keep[0] = keep[route.Count - 1] = true;
        Stack<Vector2Int> spans = new Stack<Vector2Int>();
        spans.Push(new Vector2Int(0, route.Count - 1));
        while (spans.Count > 0)
        {
            Vector2Int span = spans.Pop();
            Vector2 a = route[span.x];
            Vector2 b = route[span.y];
            float worst = 0f;
            int index = -1;
            for (int i = span.x + 1; i < span.y; i++)
            {
                float d = DistanceToSegment(route[i], a, b);
                if (d > worst) { worst = d; index = i; }
            }
            if (index >= 0 && worst > tolerance)
            {
                keep[index] = true;
                spans.Push(new Vector2Int(span.x, index));
                spans.Push(new Vector2Int(index, span.y));
            }
        }
        List<Vector2Int> result = new List<Vector2Int>();
        for (int i = 0; i < route.Count; i++) if (keep[i]) result.Add(route[i]);
        return result;
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    // Everyone's shared routes, for the comparison
    public static List<RouteEntry> SharedRoutes()
    {
        List<RouteEntry> entries = new List<RouteEntry>();
        PathfindingManager manager = Object.FindObjectOfType<PathfindingManager>();
        if (manager == null || manager.speedGrid2D == null) return entries;
        int cols = manager.speedGrid2D.GetLength(1);
        int legs = manager.controlPoints.Count - 1;
        foreach (NetworkPlayer player in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid || !player.analysisDone) continue;
            RouteEntry entry = new RouteEntry
            {
                label = BearMode.Name(player),
                isLocal = player.Object.HasStateAuthority,
                color = RouteEntry.ColorOf(player, entries.Count),
                legs = new List<Vector3>[legs + 1],
                times = new int[legs + 1],
                estimated = true
            };
            int start = 0;
            for (int k = 0; k < player.analysisLegCount && k < legs; k++)
            {
                int end = player.analysisLegEnds[k];
                List<Vector3> points = new List<Vector3>();
                for (int i = start; i < end; i++)
                {
                    int packed = player.analysisPoints[i];
                    points.Add(manager.GridToWorldPosition(packed / cols, packed % cols));
                }
                // Every route starts and ends on the controls
                if (points.Count > 0)
                {
                    points.Insert(0, manager.controlPoints[k].position);
                    points.Add(manager.controlPoints[k + 1].position);
                }
                entry.legs[k + 1] = points;
                entry.times[k + 1] = player.analysisLegTimes[k];
                start = end;
            }
            entries.Add(entry);
        }
        entries.Sort((a, b) => b.isLocal.CompareTo(a.isLocal));
        return entries;
    }

    public static List<string> StillDrawing()
    {
        List<string> names = new List<string>();
        foreach (NetworkPlayer player in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid || player.analysisDone) continue;
            names.Add(player.Object.HasStateAuthority ? "you" : BearMode.Name(player));
        }
        return names;
    }
}

// One player's routes, leg by leg (index 1 = start to control 1), with the time on each leg
public class RouteEntry
{
    public string label;
    public bool isLocal;
    public Color color;
    public List<Vector3>[] legs;
    public int[] times;          // seconds, 0 = not known
    public bool estimated;       // worked out from the route (route analysis), not run

    static readonly Color[] Palette =
    {
        new Color(0.91f, 0.45f, 0.16f), new Color(0.13f, 0.47f, 0.85f), new Color(0.20f, 0.62f, 0.33f),
        new Color(0.80f, 0.16f, 0.30f), new Color(0.55f, 0.30f, 0.75f), new Color(0.10f, 0.65f, 0.70f),
        new Color(0.85f, 0.70f, 0.10f), new Color(0.45f, 0.30f, 0.20f), new Color(0.30f, 0.30f, 0.30f), new Color(0.95f, 0.35f, 0.70f)
    };

    // The player's own colour, as their runner dot on the map
    public static Color ColorOf(NetworkPlayer player, int index)
    {
        BodyHandler body = player.GetComponent<BodyHandler>();
        if (body != null && body.cirkel != null)
        {
            Color own = body.cirkel.material != null ? body.cirkel.material.color : body.cirkel.color;
            own.a = 1f;
            return own;
        }
        return Palette[index % Palette.Length];
    }

    public int Total
    {
        get
        {
            int sum = 0;
            for (int k = 1; k < times.Length; k++)
            {
                if (times[k] <= 0) return 0;
                sum += times[k];
            }
            return sum;
        }
    }
}

// Route analysis: while the others are still drawing, says who; when everyone is done, opens the comparison
public class RouteAnalysisWatcher : MonoBehaviour
{
    GameObject waitPanel;
    TextMeshProUGUI waitText;
    RouteCompareView view;
    float next;

    void Update()
    {
        if (Time.unscaledTime < next || view != null) return;
        next = Time.unscaledTime + 0.4f;
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.Object == null || !local.Object.IsValid || !local.analysisDone) return;
        List<string> drawing = RouteAnalysis.StillDrawing();
        if (drawing.Count == 0)
        {
            OpenComparison();
            return;
        }
        if (waitPanel == null) BuildWaitPanel();
        if (waitText != null) waitText.text = "<b>Your routes are done.</b>\nWaiting for " + string.Join(", ", drawing) + " to finish drawing...";
    }

    void BuildWaitPanel()
    {
        GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
        if (canvas == null) return;
        Image card = CourseUI.CardPanel("Waiting for the others", canvas.transform);
        waitPanel = card.gameObject;
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(900, 150));
        waitText = CourseUI.Text("Text", card.transform, "", 26, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        CourseUI.Place(waitText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -18), new Vector2(560, 120));
        Button now = CourseUI.Button("Compare now", card.transform, "Compare now", CourseUI.ButtonKind.Primary, OpenComparison, 22);
        CourseUI.Place((RectTransform)now.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24, 0), new Vector2(290, 60));
    }

    void OpenComparison()
    {
        if (waitPanel != null) Destroy(waitPanel);
        view = RouteCompareView.OpenShared();
    }

    void OnDestroy()
    {
        if (waitPanel != null) Destroy(waitPanel);
    }
}

// Everyone's route on one leg, side by side on the map, with the time on it and the computer's fastest route.
// Leg 0 is the whole course with everyone's total time.
// Opened from the statistics screen after an individual competition (the routes are cut out of the replay recording,
// with the split times), and in route analysis with the routes everyone drew (RouteAnalysis.SharedRoutes).
public class RouteCompareView : MonoBehaviour
{
    List<RouteEntry> entries = new List<RouteEntry>();
    System.Func<List<RouteEntry>> source;   // route analysis: asked again, as late players finish drawing
    System.Action onClose;
    bool shared;
    int leg;

    Camera cam;
    CameraController cameraController;
    Vector3 savedPosition;
    Quaternion savedRotation;
    float savedSize;

    readonly List<GameObject> drawn = new List<GameObject>();
    GameObject panel;
    TextMeshProUGUI heading;
    RectTransform list;
    Button previous;
    Button next;
    Button back;
    Task<List<Vector2Int>> bestTask;
    int bestLeg = -1;
    bool bestShown;
    float nextPoll;
    static Material material;

    static readonly HashSet<Renderer> hiddenRunnerRenderers = new HashSet<Renderer>();
    static readonly HashSet<Canvas> hiddenRunnerCanvases = new HashSet<Canvas>();

    PathfindingManager Manager => FindObjectOfType<PathfindingManager>();
    int Legs => Manager != null ? Manager.controlPoints.Count - 1 : 0;

    // After a race: the recorded routes and the split times
    public static RouteCompareView Open(IRaceStats race, List<RelayTrack> tracks, int leg, System.Action onClose)
    {
        RouteCompareView view = new GameObject("Route choices").AddComponent<RouteCompareView>();
        view.onClose = onClose;
        view.entries = FromTracks(race, tracks ?? new List<RelayTrack>(), view.Legs);
        view.Build("Back to statistics");
        view.Show(Mathf.Max(1, leg));
        return view;
    }

    // Route analysis: the drawn routes; the host takes everyone back to the lobby from here
    public static RouteCompareView OpenShared()
    {
        RouteCompareView view = new GameObject("Route choices").AddComponent<RouteCompareView>();
        view.shared = true;
        view.source = RouteAnalysis.SharedRoutes;
        view.entries = view.source();
        view.Build(CompetitionFlow.LocalIsHost() ? "Back to the lobby" : "The host takes everyone back to the lobby");
        view.Show(0);
        return view;
    }

    void Build(string backLabel)
    {
        NetworkPlayer local = NetworkPlayer.Local;
        cameraController = local != null ? local.GetComponent<CameraController>() : null;
        cam = cameraController != null && cameraController.cam != null ? cameraController.cam : Camera.main;
        if (cameraController != null) cameraController.enabled = false;
        savedPosition = cam.transform.position;
        savedRotation = cam.transform.rotation;
        savedSize = cam.orthographicSize;
        if (material == null) material = new Material(Shader.Find("Sprites/Default"));
        HideRunners(true);

        GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
        if (canvas == null) return;
        Image card = CourseUI.CardPanel("Route choices", canvas.transform);
        panel = card.gameObject;
        CourseUI.Place(card.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24, 0), new Vector2(560, 900));
        heading = CourseUI.Text("Heading", card.transform, "", 34, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -20), new Vector2(504, 90));

        list = CourseUI.Rect("Routes", card.transform);
        CourseUI.Place(list, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -120), new Vector2(504, 620));
        CourseUI.Column(list.gameObject, 6, new RectOffset(0, 0, 0, 0)).childForceExpandHeight = false;

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Place(buttons, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28, 22), new Vector2(504, 130));
        CourseUI.Column(buttons.gameObject, 10, new RectOffset(0, 0, 0, 0));
        RectTransform arrows = CourseUI.Rect("Arrows", buttons);
        CourseUI.Row(arrows.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(arrows, 56);
        previous = CourseUI.Button("Previous", arrows, "< Previous", CourseUI.ButtonKind.Light, () => Show(leg - 1), 22);
        next = CourseUI.Button("Next", arrows, "Next >", CourseUI.ButtonKind.Light, () => Show(leg + 1), 22);
        back = CourseUI.Button("Back", buttons, backLabel, CourseUI.ButtonKind.Primary, Back, 22);
        CourseUI.Size(back, 56);
        if (shared && !CompetitionFlow.LocalIsHost()) back.interactable = false;
    }

    void Back()
    {
        if (shared)
        {
            CompetitionFlow.HostReturnToLobby();
            return;
        }
        Close();
    }

    // ---- The routes after a race, cut out of the replay recording ----

    static List<RouteEntry> FromTracks(IRaceStats race, List<RelayTrack> tracks, int legs)
    {
        List<RelayTeamResult> results = RelayStats.ForLeg(race, 0);
        PathfindingManager manager = FindObjectOfType<PathfindingManager>();
        List<RouteEntry> entries = new List<RouteEntry>();
        if (manager == null) return entries;
        List<Transform> points = manager.controlPoints;
        foreach (RelayTrack track in tracks)
        {
            RouteEntry entry = new RouteEntry
            {
                label = track.label,
                isLocal = track.isLocal,
                color = track.color,
                legs = new List<Vector3>[legs + 1],
                times = new int[legs + 1]
            };
            RelayTeamResult result = null;
            foreach (RelayTeamResult r in results)
            {
                if (r.player != null && ((track.look != null && r.player.transform == track.look) || r.team == track.label)) result = r;
            }
            // Cut where the runner reached each control
            int from = 0;
            for (int k = 1; k <= legs && k < points.Count; k++)
            {
                Vector3 control = points[k].position;
                int hit = -1, closest = from;
                float best = float.MaxValue;
                for (int i = from; i < track.positions.Count; i++)
                {
                    float d = Vector2.Distance(track.positions[i], control);
                    if (d < best) { best = d; closest = i; }
                    if (d <= 0.35f) { hit = i; break; }
                }
                int end = hit >= 0 ? hit : closest;
                List<Vector3> line = new List<Vector3> { points[k - 1].position };
                for (int i = from; i <= end && i < track.positions.Count; i++) line.Add(track.positions[i]);
                line.Add(control);
                entry.legs[k] = line;
                if (result != null && result.hasSplits && k < result.split.Length) entry.times[k] = result.split[k];
                from = end;
            }
            entries.Add(entry);
        }
        return entries;
    }

    // ---- Showing a leg ----

    void Show(int k)
    {
        int legs = Legs;
        if (legs <= 0) return;
        leg = Mathf.Clamp(k, 0, legs);
        foreach (GameObject go in drawn) if (go != null) Destroy(go);
        drawn.Clear();
        PathfindingManager manager = Manager;
        int first = leg == 0 ? 1 : leg;
        int last = leg == 0 ? legs : leg;

        // Fit the leg (or the whole course) and everyone's routes on the screen
        Bounds bounds = new Bounds(manager.controlPoints[first - 1].position, Vector3.zero);
        for (int i = first; i <= last; i++) bounds.Encapsulate(manager.controlPoints[i].position);
        foreach (RouteEntry entry in entries)
        {
            for (int i = first; i <= last; i++)
            {
                if (entry.legs[i] != null) foreach (Vector3 p in entry.legs[i]) bounds.Encapsulate(p);
            }
        }
        // The panel covers the left of the screen, so the leg is shown to the right of it
        float size = Mathf.Max(bounds.extents.y, bounds.extents.x / Mathf.Max(0.1f, cam.aspect * 0.62f)) * 1.25f + 0.25f;
        cam.transform.rotation = Quaternion.identity;
        cam.orthographicSize = size;
        float shift = size * cam.aspect * 0.32f;
        cam.transform.position = new Vector3(bounds.center.x - shift, bounds.center.y, -10f);

        float width = size * (leg == 0 ? 0.007f : 0.011f);
        foreach (RouteEntry entry in entries)
        {
            for (int i = first; i <= last; i++)
            {
                List<Vector3> points = entry.legs[i];
                if (points == null || points.Count < 2) continue;
                drawn.Add(Line("Route " + entry.label + " leg " + i, points, entry.color, width, entry.isLocal ? 41 : 40));
            }
        }

        // The computer's fastest route on the leg, worked out in the background
        bestShown = false;
        bestLeg = leg;
        bestTask = null;
        if (leg > 0)
        {
            Vector2Int from = manager.WorldToGridPositionCentered(manager.controlPoints[leg - 1].position);
            Vector2Int to = manager.WorldToGridPositionCentered(manager.controlPoints[leg].position);
            float[,] speed = manager.speedGrid2D;
            Dictionary<string, float[,]> slopes = manager.slopeFactors;
            bestTask = Task.Run(() =>
            {
                List<List<Vector2Int>> paths = new AStarPathfinder(speed, slopes).GetAllPaths(from, to, 1);
                return paths.Count > 0 ? paths[0] : null;
            });
        }

        previous.interactable = leg > 0;
        next.interactable = leg < legs;
        if (leg == 0)
        {
            heading.text = "Whole course\n<size=60%><color=#5B7563>" + legs + " legs. Press Next for leg 1.</color></size>";
        }
        else
        {
            string fromName = leg - 1 == 0 ? "Start" : "Control " + (leg - 1);
            string toName = leg == legs ? "Finish" : "Control " + leg;
            float straight = Vector2.Distance(manager.controlPoints[leg - 1].position, manager.controlPoints[leg].position) / MapDefinition.WorldUnitsPerMeter;
            heading.text = "Leg " + leg + " of " + legs + "\n<size=60%><color=#5B7563>" + fromName + " to " + toName + ", " + Mathf.RoundToInt(straight) + " m straight</color></size>";
        }
        FillList(null);
    }

    void Update()
    {
        if (bestTask != null && !bestShown && bestTask.IsCompleted && bestLeg == leg)
        {
            bestShown = true;
            List<Vector2Int> best = bestTask.IsFaulted ? null : bestTask.Result;
            if (best != null && best.Count > 1)
            {
                PathfindingManager manager = Manager;
                List<Vector3> points = new List<Vector3>();
                foreach (Vector2Int cell in best) points.Add(manager.GridToWorldPosition(cell.y, cell.x));
                drawn.Add(Line("Computer's route", points, new Color(0.1f, 0.1f, 0.1f, 0.85f), cam.orthographicSize * 0.006f, 39));
            }
            FillList(best);
        }
        // Route analysis: players who finish drawing later are added
        if (source != null && Time.unscaledTime > nextPoll)
        {
            nextPoll = Time.unscaledTime + 1f;
            List<RouteEntry> fresh = source();
            if (fresh.Count != entries.Count)
            {
                entries = fresh;
                Show(leg);
            }
        }
        if (Input.GetKeyDown(KeyCode.RightArrow)) Show(leg + 1);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Show(leg - 1);
    }

    int TimeOf(RouteEntry entry)
    {
        int t = leg == 0 ? entry.Total : entry.times[leg];
        return t > 0 ? t : int.MaxValue;
    }

    // The players, fastest first, and on a leg the computer's route last
    void FillList(List<Vector2Int> best)
    {
        foreach (Transform child in list) Destroy(child.gameObject);
        List<RouteEntry> rows = new List<RouteEntry>(entries);
        rows.Sort((x, y) => TimeOf(x).CompareTo(TimeOf(y)));
        int fastest = rows.Count > 0 ? TimeOf(rows[0]) : int.MaxValue;
        string approx = shared ? "~" : "";

        int place = 0, shownPlace = 0, previousTime = -1;
        foreach (RouteEntry row in rows)
        {
            int t = TimeOf(row);
            place++;
            if (t != previousTime) shownPlace = place;
            previousTime = t;
            string time = t < int.MaxValue ? approx + RelayStats.Time(t) : "-";
            string behind = t < int.MaxValue && t > fastest ? "  <color=#C0392B>+" + RelayStats.Time(t - fastest) + "</color>" : "";
            string number = t < int.MaxValue ? shownPlace + ". " : "";
            AddRow(row.color, number + "<b>" + row.label + "</b>" + (row.isLocal ? " (you)" : ""), time + behind, row.isLocal);
        }

        if (leg > 0)
        {
            if (best != null)
            {
                int estimate = EstimateSeconds(best);
                string text = estimate > 0 ? "~" + RelayStats.Time(estimate) : "";
                if (estimate > 0 && fastest < int.MaxValue && fastest > estimate)
                {
                    text += "  <color=#3F7D54>-" + RelayStats.Time(fastest - estimate) + "</color>";
                }
                AddRow(new Color(0.1f, 0.1f, 0.1f), "<b>Computer's route</b>", text, false);
            }
            else if (!bestShown)
            {
                AddRow(new Color(0.6f, 0.6f, 0.6f), "<color=#5B7563>Working out the computer's route...</color>", "", false);
            }
        }

        string note = shared
            ? "Times are what the same standard runner would need on each drawn route, so only the route choice counts."
            : "The runners have different speed and technique, so compare the lines more than the times.";
        TextMeshProUGUI noteText = CourseUI.Text("Note", list, note, 18, CourseUI.InkMuted);
        CourseUI.Size(noteText, 70);
    }

    void AddRow(Color color, string name, string time, bool local)
    {
        Image row = CourseUI.Panel("Row", list, local ? new Color(0.91f, 0.45f, 0.16f, 0.16f) : new Color(1f, 1f, 1f, 0.5f));
        CourseUI.Size(row, 54);
        Image swatch = CourseUI.Panel("Colour", row.transform, color);
        CourseUI.Place(swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(34, 10));
        TextMeshProUGUI label = CourseUI.Text("Name", row.transform, name, 21, CourseUI.Ink);
        CourseUI.Stretch(label.rectTransform, 58, 200, 4, 4);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        TextMeshProUGUI value = CourseUI.Text("Time", row.transform, time, 21, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(190, 50));
        value.enableWordWrapping = false;
        value.overflowMode = TextOverflowModes.Overflow;
        value.enableAutoSizing = true;
        value.fontSizeMin = 14;
        value.fontSizeMax = 21;
    }

    // The time the local runner would need on the route
    int EstimateSeconds(List<Vector2Int> route)
    {
        PathfindingManager manager = Manager;
        AgentMovement agent = NetworkPlayer.Local != null ? NetworkPlayer.Local.agentMovement : null;
        if (manager == null || agent == null) return 0;
        return RouteAnalysis.Seconds(route, manager, agent.originalBaseSpeed);
    }

    GameObject Line(string name, List<Vector3> points, Color color, float width, int order)
    {
        LineRenderer line = new GameObject(name).AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.startColor = line.endColor = color;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.sortingOrder = order;
        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) line.SetPosition(i, new Vector3(points[i].x, points[i].y, -1.3f));
        return line.gameObject;
    }

    // The runners stand on the map; they are hidden so the routes can be seen
    static void HideRunners(bool hide)
    {
        if (!hide)
        {
            foreach (Renderer r in hiddenRunnerRenderers) if (r != null) r.enabled = true;
            foreach (Canvas c in hiddenRunnerCanvases) if (c != null) c.enabled = true;
            hiddenRunnerRenderers.Clear();
            hiddenRunnerCanvases.Clear();
            return;
        }
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (!(runner is MonoBehaviour behaviour) || behaviour == null) continue;
            foreach (Renderer r in behaviour.GetComponentsInChildren<Renderer>())
            {
                if (r.enabled) { r.enabled = false; hiddenRunnerRenderers.Add(r); }
            }
            foreach (Canvas c in behaviour.GetComponentsInChildren<Canvas>())
            {
                if (c.enabled) { c.enabled = false; hiddenRunnerCanvases.Add(c); }
            }
        }
    }

    void OnDestroy()
    {
        HideRunners(false);
        foreach (GameObject go in drawn) if (go != null) Destroy(go);
        if (panel != null) Destroy(panel);
    }

    public void Close()
    {
        cam.transform.position = savedPosition;
        cam.transform.rotation = savedRotation;
        cam.orthographicSize = savedSize;
        if (cameraController != null) cameraController.enabled = true;
        Destroy(gameObject);
        onClose?.Invoke();
    }
}
