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
        // (No message at the start: it covered the start control, where the first line is drawn from)
    }

    // Everyone is ready: draw the whole course before anything else happens (the same drawing as a relay leg)
    public static void StartDrawing(AgentMovement agent)
    {
        agent.relaySegments = new List<List<Vector2Int>>();
        agent.manualPathDrawer.BeginRelayLeg();
        agent.startView();
    }

    // The last stretch is drawn: work out the times and share the routes. Each route is the drawn line made into
    // grid cells (A* close to the line, as in a race), then made smooth the way the route choice game does it:
    // straight wherever that is no slower, with rounded corners. That smooth line is what is timed and shown.
    public static void OnCourseDrawn(AgentMovement agent)
    {
        NetworkPlayer np = agent.GetComponent<NetworkPlayer>();
        PathfindingManager manager = agent.pathfindingManager;
        agent.manualPathDrawer.timeToDraw = false;
        agent.canRunNextSegment = false;
        agent.WaitingForAllPlayersToDraw = true; // stops the clock
        if (np == null || manager == null) return;

        List<List<Vector2Int>> legs = agent.relaySegments;
        int count = Mathf.Min(legs.Count, np.analysisLegTimes.Length);
        RouteChoiceGenerator generator = Generator(manager, agent.originalBaseSpeed);
        List<List<Vector2>> lines = new List<List<Vector2>>();
        for (int k = 0; k < count; k++)
        {
            RouteChoice timed = generator.Timed(legs[k]);
            lines.Add(timed != null ? timed.points : new List<Vector2>());
            // A route over ground nobody can run on is shared with its time made negative, so it can be marked
            int seconds = timed != null ? Mathf.Max(1, Mathf.RoundToInt(timed.seconds)) : 0;
            np.analysisLegTimes.Set(k, timed != null && timed.crossesBlocked ? -seconds : seconds);
        }

        // The lines, made simpler until they fit in what is shared
        int scale = Scale(manager);
        int cols = manager.speedGrid2D.GetLength(1) * scale;
        float tolerance = 0.15f;
        List<List<Vector2>> simple = null;
        for (int attempt = 0; attempt < 14; attempt++)
        {
            simple = new List<List<Vector2>>();
            int total = 0;
            for (int k = 0; k < count; k++)
            {
                List<Vector2> s = Simplify(lines[k], tolerance);
                simple.Add(s);
                total += s.Count;
            }
            if (total <= np.analysisPoints.Length) break;
            tolerance *= 1.6f;
        }
        int at = 0;
        for (int k = 0; k < count; k++)
        {
            foreach (Vector2 p in simple[k])
            {
                if (at >= np.analysisPoints.Length) break;
                np.analysisPoints.Set(at++, Mathf.RoundToInt(p.y * scale) * cols + Mathf.RoundToInt(p.x * scale));
            }
            np.analysisLegEnds.Set(k, at);
        }
        np.analysisLegCount = count;
        np.analysisDone = true;
    }

    // The shared points are grid coordinates in quarter cells (whole cells on a very big grid, so they fit in an int)
    static int Scale(PathfindingManager manager)
    {
        long cells = (long)manager.speedGrid2D.GetLength(0) * manager.speedGrid2D.GetLength(1);
        return cells * 16 < int.MaxValue ? 4 : 1;
    }

    public static bool IsSprint(PathfindingManager manager)
    {
        return CourseLoader.LoadedMap != null ? CourseLoader.LoadedMap.IsSprint : manager.cellSize / MapDefinition.WorldUnitsPerMeter < 5f;
    }

    // The same smoothing and timing as the route choice game, with this runner's speed
    public static RouteChoiceGenerator Generator(PathfindingManager manager, float baseSpeed)
    {
        // Ground nobody can run on (a building, a fence, private land) counts as 100 times slower than open ground
        // when a drawn line goes over it, so going through is never faster than the way round
        return new RouteChoiceGenerator(manager.speedGrid2D, manager.slopeFactors, manager.cellSize,
            baseSpeed * TerrainSpeedModel.RaceSpeedScale(), PackRunning.GameSecondsPerSecond, IsSprint(manager)) { BlockedSpeed = 0.01f };
    }

    // A point in grid coordinates (cell centres on whole numbers) on the map
    public static Vector3 GridToWorld(PathfindingManager manager, Vector2 p)
    {
        Vector3 origin = manager.GridToWorldPosition(0, 0);
        return new Vector3(origin.x + p.x * manager.cellSize, origin.y + p.y * manager.cellSize, origin.z);
    }

    // How far from the drawn line its route may run: about 4 m, so a line drawn roughly along a road or a path
    // follows it (one cell on a forest map, two on a sprint map)
    public static int CorridorCells(PathfindingManager manager)
    {
        float cellMeters = manager.cellSize / MapDefinition.WorldUnitsPerMeter;
        return Mathf.Clamp(Mathf.CeilToInt(4f / Mathf.Max(0.1f, cellMeters)), 1, 3);
    }

    // Douglas-Peucker: keeps the corners of a route, drops the cells in between
    public static List<Vector2Int> Simplify(List<Vector2Int> route, float tolerance)
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

    public static List<Vector2> Simplify(List<Vector2> route, float tolerance)
    {
        if (route == null || route.Count <= 2) return route != null ? new List<Vector2>(route) : new List<Vector2>();
        bool[] keep = new bool[route.Count];
        keep[0] = keep[route.Count - 1] = true;
        Stack<Vector2Int> spans = new Stack<Vector2Int>();
        spans.Push(new Vector2Int(0, route.Count - 1));
        while (spans.Count > 0)
        {
            Vector2Int span = spans.Pop();
            float worst = 0f;
            int index = -1;
            for (int i = span.x + 1; i < span.y; i++)
            {
                float d = DistanceToSegment(route[i], route[span.x], route[span.y]);
                if (d > worst) { worst = d; index = i; }
            }
            if (index >= 0 && worst > tolerance)
            {
                keep[index] = true;
                spans.Push(new Vector2Int(span.x, index));
                spans.Push(new Vector2Int(index, span.y));
            }
        }
        List<Vector2> result = new List<Vector2>();
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
        int scale = Scale(manager);
        int cols = manager.speedGrid2D.GetLength(1) * scale;
        int legs = manager.controlPoints.Count - 1;
        List<Color> used = new List<Color>();
        foreach (NetworkPlayer player in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid || !player.analysisDone) continue;
            RouteEntry entry = new RouteEntry
            {
                label = BearMode.Name(player),
                isLocal = player.Object.HasStateAuthority,
                color = RouteEntry.Distinct(RouteEntry.ColorOf(player, entries.Count), used),
                legs = new List<Vector3>[legs + 1],
                times = new int[legs + 1],
                crosses = new bool[legs + 1],
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
                    points.Add(GridToWorld(manager, new Vector2((float)(packed % cols) / scale, (float)(packed / cols) / scale)));
                }
                // Every route starts and ends on the controls
                if (points.Count > 0)
                {
                    points.Insert(0, manager.controlPoints[k].position);
                    points.Add(manager.controlPoints[k + 1].position);
                }
                entry.legs[k + 1] = points;
                int shared = player.analysisLegTimes[k];
                entry.times[k + 1] = Mathf.Abs(shared);
                entry.crosses[k + 1] = shared < 0;
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
    public bool[] crosses;       // route analysis: the route goes over ground nobody can run on (null after a race)
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

    // Two players can have (nearly) the same colour, and a very dark one looks like the computer's route: then the
    // next free colour of the palette is taken, so every route can be told apart. The colour is added to used.
    public static Color Distinct(Color wanted, List<Color> used)
    {
        Color pick = wanted;
        bool dark = wanted.r + wanted.g + wanted.b < 0.6f;
        if (dark || Taken(wanted, used))
        {
            foreach (Color c in Palette)
            {
                if (Taken(c, used)) continue;
                pick = c;
                break;
            }
        }
        used.Add(pick);
        return pick;
    }

    static bool Taken(Color c, List<Color> used)
    {
        foreach (Color u in used)
        {
            float d = Mathf.Abs(c.r - u.r) + Mathf.Abs(c.g - u.g) + Mathf.Abs(c.b - u.b);
            if (d < 0.35f) return true;
        }
        return false;
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
    // The computer's fastest route on every leg (index 1 = start to control 1), worked out in the background one leg
    // after the other, and the leg shown when it was last drawn
    RouteChoice[] computer;
    volatile bool stopWorking;
    volatile int computerRun;   // a new run of StartComputer stops the one before
    int computerShown = -1;
    int focus = -2;         // the route shown clearly, the others faded: -2 = all, -1 = the computer's, else a player
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
        view.StartComputer();
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
        view.Build(CompetitionFlow.LocalIsHost() ? CompetitionFlow.BackButtonText : "The host takes everyone back to the lobby");
        view.StartComputer();
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
        List<Color> used = new List<Color>();
        foreach (RelayTrack track in tracks)
        {
            RouteEntry entry = new RouteEntry
            {
                label = track.label,
                isLocal = track.isLocal,
                color = RouteEntry.Distinct(track.color, used),
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
                // The runner first comes this close a little before the control and then runs on into it. Cut where
                // it came closest while still near the control, or the last bit into the control would be drawn as
                // the start of the next leg (a hook back towards where the runner came from).
                if (hit >= 0)
                {
                    best = Vector2.Distance(track.positions[hit], control);
                    for (int i = hit + 1; i < track.positions.Count; i++)
                    {
                        float d = Vector2.Distance(track.positions[i], control);
                        if (d > 0.35f) break;
                        if (d < best) { best = d; hit = i; }
                    }
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

    // ---- The computer's routes ----

    // The same fastest route the route choice game finds (a search in a window round each leg), made smooth and timed
    // the same way as the players' routes, with the local runner (in route analysis the standard runner). The search is
    // also run again close to each player's line on open ground: where that comes out faster (a narrow passage, a
    // smoother way), the computer takes it, so the computer's route is always the fastest. Started again when more
    // players' routes come in.
    void StartComputer()
    {
        int run = ++computerRun;
        PathfindingManager manager = Manager;
        AgentMovement agent = NetworkPlayer.Local != null ? NetworkPlayer.Local.agentMovement : null;
        int legs = Legs;
        computer = new RouteChoice[legs + 1];
        if (manager == null || manager.speedGrid2D == null || agent == null || legs <= 0) return;
        Vector2Int[] cells = new Vector2Int[legs + 1];
        for (int k = 0; k <= legs; k++) cells[k] = manager.WorldToGridPositionCentered(manager.controlPoints[k].position);
        RouteChoiceGenerator generator = RouteAnalysis.Generator(manager, agent.originalBaseSpeed);

        // Everyone's lines in grid coordinates, leg by leg (not the ones over ground nobody can run on)
        Vector3 origin = manager.GridToWorldPosition(0, 0);
        float cell = manager.cellSize;
        List<List<Vector2>>[] lines = new List<List<Vector2>>[legs + 1];
        for (int k = 1; k <= legs; k++)
        {
            lines[k] = new List<List<Vector2>>();
            foreach (RouteEntry entry in entries)
            {
                if (entry.legs == null || k >= entry.legs.Length || entry.legs[k] == null || entry.legs[k].Count < 2) continue;
                if (entry.crosses != null && entry.crosses[k]) continue;
                List<Vector2> line = new List<Vector2>();
                foreach (Vector3 p in entry.legs[k]) line.Add(new Vector2((p.x - origin.x) / cell, (p.y - origin.y) / cell));
                lines[k].Add(line);
            }
        }
        float cellMeters = cell / MapDefinition.WorldUnitsPerMeter;
        int radius = Mathf.Clamp(Mathf.CeilToInt(6f / Mathf.Max(0.1f, cellMeters)), 1, 4);

        RouteChoice[] target = computer;
        Task.Run(() =>
        {
            for (int k = 1; k <= legs && !stopWorking && run == computerRun; k++)
            {
                RouteChoice best = null;
                try
                {
                    best = generator.Fastest(cells[k - 1], cells[k]);
                    foreach (List<Vector2> line in lines[k])
                    {
                        if (stopWorking || run != computerRun) break;
                        RouteChoice near = generator.FastestNear(cells[k - 1], cells[k], line, radius);
                        if (near != null && !near.crossesBlocked && (best == null || near.seconds < best.seconds)) best = near;
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Route choices: no computer route on leg " + k + ": " + e.Message);
                }
                target[k] = best ?? new RouteChoice { points = new List<Vector2>() };
            }
        });
    }

    RouteChoice Computer(int k) => computer != null && k > 0 && k < computer.Length ? computer[k] : null;

    // The computer's routes of the legs shown are all worked out
    bool ComputerReady()
    {
        if (leg > 0) return Computer(leg) != null;
        for (int k = 1; k <= Legs; k++) if (Computer(k) == null) return false;
        return true;
    }

    int ComputerTime()
    {
        if (leg > 0)
        {
            RouteChoice c = Computer(leg);
            return c != null && c.points.Count > 1 ? Mathf.RoundToInt(c.seconds) : 0;
        }
        int sum = 0;
        for (int k = 1; k <= Legs; k++)
        {
            RouteChoice c = Computer(k);
            if (c == null || c.points.Count < 2) return 0;
            sum += Mathf.RoundToInt(c.seconds);
        }
        return sum;
    }

    // ---- Showing a leg ----

    void Show(int k)
    {
        int legs = Legs;
        if (legs <= 0) return;
        if (k != leg) focus = -2;
        leg = Mathf.Clamp(k, 0, legs);
        PathfindingManager manager = Manager;
        int first = leg == 0 ? 1 : leg;
        int last = leg == 0 ? legs : leg;

        // A leg is turned so that the control you leave is at the bottom of the screen and the one you go to at the
        // top, as you hold the map when you run; the whole course is shown north up
        Vector3 origin = manager.controlPoints[first - 1].position;
        Quaternion turn = Quaternion.identity;
        if (leg > 0)
        {
            Vector2 along = manager.controlPoints[leg].position - origin;
            if (along.sqrMagnitude > 0.000001f) turn = Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg - 90f);
        }
        Quaternion back = Quaternion.Inverse(turn);

        // Fit the leg (or the whole course) and everyone's routes on the screen (measured as the camera sees them)
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        void Add(Vector3 p)
        {
            Vector3 local = back * new Vector3(p.x - origin.x, p.y - origin.y, 0f);
            bounds.Encapsulate(local);
        }
        for (int i = first; i <= last; i++) Add(manager.controlPoints[i].position);
        foreach (RouteEntry entry in entries)
        {
            for (int i = first; i <= last; i++)
            {
                if (entry.legs[i] != null) foreach (Vector3 p in entry.legs[i]) Add(p);
            }
        }
        for (int i = first; i <= last; i++)
        {
            RouteChoice c = Computer(i);
            if (c == null) continue;
            foreach (Vector2 p in c.points) Add(RouteAnalysis.GridToWorld(manager, p));
        }
        // The panel covers the left of the screen, so the leg is shown to the right of it
        float size = Mathf.Max(bounds.extents.y, bounds.extents.x / Mathf.Max(0.1f, cam.aspect * 0.62f)) * 1.25f
            + 40f * MapDefinition.WorldUnitsPerMeter;
        cam.transform.rotation = turn;
        cam.orthographicSize = size;
        float shift = size * cam.aspect * 0.32f;
        Vector3 centre = origin + turn * (bounds.center - new Vector3(shift, 0f, 0f));
        cam.transform.position = new Vector3(centre.x, centre.y, -10f);

        // On the first and last leg the button that leads nowhere is not shown
        previous.interactable = leg > 0;
        next.interactable = leg < legs;
        previous.gameObject.SetActive(leg > 0);
        next.gameObject.SetActive(leg < legs);
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
        Redraw();
    }

    // The routes on the map and the list, as far as the computer's routes are worked out
    void Redraw()
    {
        foreach (GameObject go in drawn) if (go != null) Destroy(go);
        drawn.Clear();
        PathfindingManager manager = Manager;
        int legs = Legs;
        int first = leg == 0 ? 1 : leg;
        int last = leg == 0 ? legs : leg;
        bool ready = ComputerReady();
        computerShown = ready ? leg : -1;

        // As thick as the route choice game's lines, with a dark edge so they show on every kind of ground
        float width = cam.orthographicSize * (leg == 0 ? 0.008f : 0.011f);
        for (int e = 0; e < entries.Count; e++)
        {
            RouteEntry entry = entries[e];
            bool clear = focus == -2 || focus == e;
            Color colour = entry.color;
            colour.a = clear ? 1f : 0.25f;
            int order = 50 + 2 * e + (entry.isLocal ? 30 : 0) + (focus == e ? 60 : 0);
            for (int i = first; i <= last; i++)
            {
                List<Vector3> points = entry.legs[i];
                if (points == null || points.Count < 2) continue;
                drawn.Add(Line("Route edge " + entry.label + " leg " + i, points, new Color(0.08f, 0.08f, 0.08f, clear ? 0.75f : 0.2f), width * 1.6f, order));
                drawn.Add(Line("Route " + entry.label + " leg " + i, points, colour, width, order + 1));
            }
        }

        // The computer's route: black with a white edge, wider than the players' lines and under them, so where a
        // player ran the same way it shows as a black band round that player's line
        if (ready)
        {
            bool clear = focus == -2 || focus == -1;
            int order = focus == -1 ? 200 : 30;
            for (int i = first; i <= last; i++)
            {
                RouteChoice c = Computer(i);
                if (c == null || c.points.Count < 2) continue;
                List<Vector3> points = new List<Vector3> { manager.controlPoints[i - 1].position };
                foreach (Vector2 p in c.points) points.Add(RouteAnalysis.GridToWorld(manager, p));
                points.Add(manager.controlPoints[i].position);
                drawn.Add(Line("Computer's route edge leg " + i, points, new Color(1f, 1f, 1f, clear ? 0.95f : 0.3f), width * 2.9f, order));
                drawn.Add(Line("Computer's route leg " + i, points, new Color(0.08f, 0.08f, 0.08f, clear ? 1f : 0.3f), width * 1.9f, order + 1));
            }
        }
        FillList(ready);
    }

    void Update()
    {
        // The computer's route is drawn as soon as it is worked out
        if (computerShown != leg && ComputerReady()) Redraw();
        // Route analysis: players who finish drawing later are added
        if (source != null && Time.unscaledTime > nextPoll)
        {
            nextPoll = Time.unscaledTime + 1f;
            List<RouteEntry> fresh = source();
            if (fresh.Count != entries.Count)
            {
                entries = fresh;
                focus = -2;
                StartComputer();
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

    // Click a row to see that route clearly (the others fade); click it again to see all
    void Focus(int index)
    {
        focus = focus == index ? -2 : index;
        Redraw();
    }

    // The players, fastest first, and the computer's route last
    void FillList(bool ready)
    {
        foreach (Transform child in list) Destroy(child.gameObject);
        List<int> rows = new List<int>();
        for (int e = 0; e < entries.Count; e++) rows.Add(e);
        rows.Sort((x, y) => TimeOf(entries[x]).CompareTo(TimeOf(entries[y])));
        int fastest = rows.Count > 0 ? TimeOf(entries[rows[0]]) : int.MaxValue;
        string approx = shared ? "~" : "";

        int place = 0, shownPlace = 0, previousTime = -1;
        foreach (int e in rows)
        {
            RouteEntry row = entries[e];
            int t = TimeOf(row);
            place++;
            if (t != previousTime) shownPlace = place;
            previousTime = t;
            string time = t < int.MaxValue ? approx + RelayStats.Time(t) : "-";
            string behind = t < int.MaxValue && t > fastest ? "  <color=#C0392B>+" + RelayStats.Time(t - fastest) + "</color>" : "";
            string number = t < int.MaxValue ? shownPlace + ". " : "";
            int index = e;
            bool crosses = false;
            if (row.crosses != null)
            {
                for (int i = leg == 0 ? 1 : leg; i <= (leg == 0 ? Legs : leg) && i < row.crosses.Length; i++) crosses |= row.crosses[i];
            }
            string name = number + "<b>" + row.label + "</b>" + (row.isLocal ? " (you)" : "")
                + (crosses ? "\n<size=75%><color=#C0392B>Goes through ground you may not run on</color></size>" : "");
            AddRow(row.color, name, time + behind, row.isLocal, focus == e, () => Focus(index));
        }

        if (ready)
        {
            int estimate = ComputerTime();
            string text = estimate > 0 ? "~" + RelayStats.Time(estimate) : "-";
            if (estimate > 0 && fastest < int.MaxValue && fastest > estimate)
            {
                text += "  <color=#3F7D54>-" + RelayStats.Time(fastest - estimate) + "</color>";
            }
            AddRow(new Color(0.08f, 0.08f, 0.08f), "<b>Computer's route</b>", text, false, focus == -1, () => Focus(-1));
        }
        else
        {
            AddRow(new Color(0.6f, 0.6f, 0.6f), "<color=#5B7563>Working out the computer's route...</color>", "", false, false, null);
        }

        string note = (shared
            ? "Times are what the same standard runner would need on each drawn route, so only the route choice counts."
            : "The runners have different speed and technique, so compare the lines more than the times.")
            + " Click a route to see it on its own.";
        TextMeshProUGUI noteText = CourseUI.Text("Note", list, note, 18, CourseUI.InkMuted);
        CourseUI.Size(noteText, 80);
    }

    void AddRow(Color color, string name, string time, bool local, bool focused, System.Action onClick)
    {
        Color background = focused ? new Color(0.13f, 0.30f, 0.20f, 0.22f)
            : local ? new Color(0.91f, 0.45f, 0.16f, 0.16f) : new Color(1f, 1f, 1f, 0.5f);
        Image row = CourseUI.Panel("Row", list, background);
        CourseUI.Size(row, 54);
        if (onClick != null)
        {
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;
            button.onClick.AddListener(() => onClick());
        }
        Image swatch = CourseUI.Panel("Colour", row.transform, color);
        CourseUI.Place(swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(34, 10));
        swatch.raycastTarget = false;
        TextMeshProUGUI label = CourseUI.Text("Name", row.transform, name, 21, CourseUI.Ink);
        CourseUI.Stretch(label.rectTransform, 58, 200, 4, 4);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        TextMeshProUGUI value = CourseUI.Text("Time", row.transform, time, 21, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10, 0), new Vector2(190, 50));
        value.enableWordWrapping = false;
        value.overflowMode = TextOverflowModes.Overflow;
        value.enableAutoSizing = true;
        value.fontSizeMin = 14;
        value.fontSizeMax = 21;
        value.raycastTarget = false;
    }

    // Drawn as a flat mesh (FlatLine), not a LineRenderer: that one narrowed whole pieces to a thin wedge where the
    // route starts and ends at the controls
    GameObject Line(string name, List<Vector3> points, Color color, float width, int order)
    {
        return FlatLine.Make(name, points, color, width, order, material);
    }

    // The runners stand on the map; they are hidden so the routes can be seen
    public static void HideRunners(bool hide)
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
        stopWorking = true;
        HideRunners(false);
        // Closed by a scene change (the host took everyone back to the lobby) rather than by Close()
        if (cameraController != null) cameraController.enabled = true;
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
