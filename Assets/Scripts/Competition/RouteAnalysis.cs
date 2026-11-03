using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Route analysis game mode, for learning route choice (e.g. in a club): nobody picks a runner card. Everyone runs
// with the same standard runner, who never makes mistakes, so the only difference between the players is the routes
// they draw. Draw how you would run it yourself in a real race; afterwards the statistics screen's "Route choices"
// tab (RouteCompareView) shows everyone's routes leg by leg, with the times and the computer's fastest route.
// Played on its own, without other game modes.
public static class RouteAnalysis
{
    public const float StandardSpeed = 85f;
    public const string RunnerName = "Standard runner";

    public static bool On => ExtraModes.Is(ExtraMode.RouteAnalysis) && RelayRace.Active == null;

    // Instead of the choose-a-runner screen: everyone gets the same runner and is ready at once
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
        local.SetHasChoosenRunner();
        UIToast.Show("Route analysis: everyone runs with the same runner, who makes no mistakes, so only the route choices count. "
            + "Draw the way you would run it yourself. Afterwards, compare everyone's routes under \"Route choices\".", 10f);
    }
}

// Everyone's route on one leg, side by side on the map, with the time each took and the computer's fastest route.
// Opened from the statistics screen after an individual competition. The routes are cut out of the replay recording
// (every runner's position a few times a second), so nothing extra is sent over the network.
public class RouteCompareView : MonoBehaviour
{
    IRaceStats race;
    List<RelayTrack> tracks;
    List<RelayTeamResult> results;
    System.Action onClose;
    int leg;                         // 1 = start to control 1 ... last = last control to the finish

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
    Task<List<Vector2Int>> bestTask;
    int bestLeg = -1;
    bool bestShown;
    static Material material;

    // Every track cut into legs: cuts[track][leg] = (first sample, last sample)
    readonly Dictionary<RelayTrack, Vector2Int[]> cuts = new Dictionary<RelayTrack, Vector2Int[]>();

    static readonly HashSet<Renderer> hiddenRunnerRenderers = new HashSet<Renderer>();
    static readonly HashSet<Canvas> hiddenRunnerCanvases = new HashSet<Canvas>();

    PathfindingManager Manager => FindObjectOfType<PathfindingManager>();
    int Legs => Manager != null ? Manager.controlPoints.Count - 1 : 0;

    public static RouteCompareView Open(IRaceStats race, List<RelayTrack> tracks, int leg, System.Action onClose)
    {
        RouteCompareView view = new GameObject("Route choices").AddComponent<RouteCompareView>();
        view.race = race;
        view.tracks = tracks ?? new List<RelayTrack>();
        view.onClose = onClose;
        view.Build();
        view.Show(Mathf.Max(1, leg));
        return view;
    }

    void Build()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        cameraController = local != null ? local.GetComponent<CameraController>() : null;
        cam = cameraController != null && cameraController.cam != null ? cameraController.cam : Camera.main;
        if (cameraController != null) cameraController.enabled = false;
        savedPosition = cam.transform.position;
        savedRotation = cam.transform.rotation;
        savedSize = cam.orthographicSize;
        if (material == null) material = new Material(Shader.Find("Sprites/Default"));
        results = RelayStats.ForLeg(race, 0);
        CutTracks();
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
        previous = CourseUI.Button("Previous", arrows, "< Previous leg", CourseUI.ButtonKind.Light, () => Show(leg - 1), 22);
        next = CourseUI.Button("Next", arrows, "Next leg >", CourseUI.ButtonKind.Light, () => Show(leg + 1), 22);
        Button back = CourseUI.Button("Back", buttons, "Back to statistics", CourseUI.ButtonKind.Primary, Close, 22);
        CourseUI.Size(back, 56);
    }

    // ---- Cutting the recorded routes into legs ----

    void CutTracks()
    {
        PathfindingManager manager = Manager;
        if (manager == null) return;
        List<Transform> points = manager.controlPoints;
        float reach = 0.35f;
        foreach (RelayTrack track in tracks)
        {
            Vector2Int[] legCuts = new Vector2Int[points.Count];
            int from = 0;
            for (int k = 1; k < points.Count; k++)
            {
                Vector3 control = points[k].position;
                int hit = -1;
                int closest = from;
                float best = float.MaxValue;
                for (int i = from; i < track.positions.Count; i++)
                {
                    float d = Vector2.Distance(track.positions[i], control);
                    if (d < best) { best = d; closest = i; }
                    if (d <= reach) { hit = i; break; }
                }
                int end = hit >= 0 ? hit : closest;
                legCuts[k] = new Vector2Int(from, end);
                from = end;
            }
            cuts[track] = legCuts;
        }
    }

    List<Vector3> LegPoints(RelayTrack track, int k)
    {
        List<Vector3> result = new List<Vector3>();
        PathfindingManager manager = Manager;
        if (!cuts.TryGetValue(track, out Vector2Int[] legCuts) || k >= legCuts.Length) return result;
        Vector2Int range = legCuts[k];
        result.Add(manager.controlPoints[k - 1].position);
        for (int i = range.x; i <= range.y && i < track.positions.Count; i++) result.Add(track.positions[i]);
        result.Add(manager.controlPoints[k].position);
        return result;
    }

    // ---- Showing a leg ----

    void Show(int k)
    {
        int legs = Legs;
        if (legs <= 0) return;
        leg = Mathf.Clamp(k, 1, legs);
        foreach (GameObject go in drawn) if (go != null) Destroy(go);
        drawn.Clear();
        PathfindingManager manager = Manager;
        Vector3 a = manager.controlPoints[leg - 1].position;
        Vector3 b = manager.controlPoints[leg].position;

        // Fit the leg and everyone's route on it on the screen
        Bounds bounds = new Bounds(a, Vector3.zero);
        bounds.Encapsulate(b);
        foreach (RelayTrack track in tracks) foreach (Vector3 p in LegPoints(track, leg)) bounds.Encapsulate(p);
        // The panel covers the left of the screen, so the leg is shown to the right of it
        float size = Mathf.Max(bounds.extents.y, bounds.extents.x / Mathf.Max(0.1f, cam.aspect * 0.62f)) * 1.25f + 0.25f;
        cam.transform.rotation = Quaternion.identity;
        cam.orthographicSize = size;
        float shift = size * cam.aspect * 0.32f;
        cam.transform.position = new Vector3(bounds.center.x - shift, bounds.center.y, -10f);

        float width = size * 0.011f;
        foreach (RelayTrack track in tracks)
        {
            List<Vector3> points = LegPoints(track, leg);
            if (points.Count < 2) continue;
            drawn.Add(Line("Route " + track.label, points, track.color, width, 40));
        }

        // The computer's fastest route, worked out in the background
        bestShown = false;
        bestLeg = leg;
        Vector2Int from = manager.WorldToGridPositionCentered(a);
        Vector2Int to = manager.WorldToGridPositionCentered(b);
        float[,] speed = manager.speedGrid2D;
        Dictionary<string, float[,]> slopes = manager.slopeFactors;
        bestTask = Task.Run(() =>
        {
            List<List<Vector2Int>> paths = new AStarPathfinder(speed, slopes).GetAllPaths(from, to, 1);
            return paths.Count > 0 ? paths[0] : null;
        });

        previous.interactable = leg > 1;
        next.interactable = leg < legs;
        string fromName = leg - 1 == 0 ? "Start" : "Control " + (leg - 1);
        string toName = leg == legs ? "Finish" : "Control " + leg;
        float straight = Vector2.Distance(a, b) / MapDefinition.WorldUnitsPerMeter;
        heading.text = "Leg " + leg + " of " + legs + "\n<size=60%><color=#5B7563>" + fromName + " to " + toName + ", " + Mathf.RoundToInt(straight) + " m straight</color></size>";
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
                GameObject line = Line("Computer's route", points, new Color(0.1f, 0.1f, 0.1f, 0.85f), cam.orthographicSize * 0.006f, 39);
                drawn.Add(line);
                FillList(best);
            }
        }
        if (Input.GetKeyDown(KeyCode.RightArrow)) Show(leg + 1);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Show(leg - 1);
    }

    // The players on this leg, fastest first, and the computer's route last
    void FillList(List<Vector2Int> best)
    {
        foreach (Transform child in list) Destroy(child.gameObject);
        List<(RelayTrack track, RelayTeamResult result)> rows = new List<(RelayTrack, RelayTeamResult)>();
        foreach (RelayTrack track in tracks) rows.Add((track, Result(track)));
        rows.Sort((x, y) => Split(x.result).CompareTo(Split(y.result)));
        int fastest = rows.Count > 0 ? Split(rows[0].result) : 0;

        foreach (var row in rows)
        {
            int split = Split(row.result);
            string time = split < int.MaxValue ? RelayStats.Time(split) : "-";
            string behind = split < int.MaxValue && split > fastest ? "  <color=#C0392B>+" + RelayStats.Time(split - fastest) + "</color>" : "";
            string place = split < int.MaxValue && row.result != null ? row.result.splitPlace[leg] + ". " : "";
            AddRow(row.track.color, place + "<b>" + row.track.label + "</b>" + (row.track.isLocal ? " (you)" : ""), time + behind, row.track.isLocal);
        }

        if (best != null)
        {
            int estimate = EstimateSeconds(best);
            string text = estimate > 0 ? "about " + RelayStats.Time(estimate) : "";
            if (estimate > 0 && fastest > 0 && fastest < int.MaxValue)
            {
                int gain = fastest - estimate;
                if (gain > 0) text += "  <color=#3F7D54>-" + RelayStats.Time(gain) + "</color>";
            }
            AddRow(new Color(0.1f, 0.1f, 0.1f), "<b>Computer's fastest route</b>", text, false);
        }
        else
        {
            AddRow(new Color(0.6f, 0.6f, 0.6f), "<color=#5B7563>Working out the computer's route...</color>", "", false);
        }

        TextMeshProUGUI note = CourseUI.Text("Note", list, RouteAnalysis.On
            ? "Everyone ran with the same runner and no mistakes, so the differences are the route choices."
            : "The runners have different speed and technique, so compare the lines more than the times.", 18, CourseUI.InkMuted);
        CourseUI.Size(note, 70);
    }

    void AddRow(Color color, string name, string time, bool local)
    {
        Image row = CourseUI.Panel("Row", list, local ? new Color(0.91f, 0.45f, 0.16f, 0.16f) : new Color(1f, 1f, 1f, 0.5f));
        CourseUI.Size(row, 54);
        Image swatch = CourseUI.Panel("Colour", row.transform, color);
        CourseUI.Place(swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(34, 10));
        TextMeshProUGUI label = CourseUI.Text("Name", row.transform, name, 22, CourseUI.Ink);
        CourseUI.Stretch(label.rectTransform, 58, 170, 4, 4);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        TextMeshProUGUI value = CourseUI.Text("Time", row.transform, time, 22, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12, 0), new Vector2(200, 50));
        value.enableWordWrapping = false;
    }

    RelayTeamResult Result(RelayTrack track)
    {
        if (results == null) return null;
        foreach (RelayTeamResult result in results)
        {
            if (result.player == null) continue;
            if ((track.look != null && result.player.transform == track.look) || result.team == track.label) return result;
        }
        return null;
    }

    int Split(RelayTeamResult result)
    {
        if (result == null || !result.hasSplits || leg >= result.split.Length) return int.MaxValue;
        return result.split[leg];
    }

    // The time the local runner would need on the route, from the same speeds the runners use
    // (speed = base speed x ground x slope, the competition clock running GameSecondsPerSecond)
    int EstimateSeconds(List<Vector2Int> route)
    {
        PathfindingManager manager = Manager;
        AgentMovement agent = NetworkPlayer.Local != null ? NetworkPlayer.Local.agentMovement : null;
        if (manager == null || agent == null || agent.originalBaseSpeed <= 0f) return 0;
        float cost = new AStarPathfinder(manager.speedGrid2D, manager.slopeFactors).CalculatePathCost(route);
        if (cost >= 10000f) return 0;
        float realSeconds = cost * manager.cellSize / agent.originalBaseSpeed;
        return Mathf.RoundToInt(realSeconds * PackRunning.GameSecondsPerSecond);
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

    // The runners stand where they finished; they are hidden so the routes can be seen
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

    public void Close()
    {
        cam.transform.position = savedPosition;
        cam.transform.rotation = savedRotation;
        cam.orthographicSize = savedSize;
        if (cameraController != null) cameraController.enabled = true;
        HideRunners(false);
        foreach (GameObject go in drawn) if (go != null) Destroy(go);
        if (panel != null) Destroy(panel);
        Destroy(gameObject);
        onClose?.Invoke();
    }
}
