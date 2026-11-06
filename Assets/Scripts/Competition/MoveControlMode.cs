using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Move a control game mode. While drawing a stretch, every player may once in the race move the control they are
// drawing to, at most 100 m (GameModeTools). Nobody else sees it: they draw to where the control was. When everyone
// has drawn, the control flies to its new place for everyone; if several players moved it, it lands in the middle of
// their places. Then every runner runs the line they drew and, from where it ended, on to the new place: A* over the
// whole new line (drawn line + the way on to the new place), the same way a drawn line is turned into a route.
// A route choice that was good can turn out bad.
public static class MoveControlMode
{
    public const float MaxMeters = 100f;

    static float U => MapDefinition.WorldUnitsPerMeter;

    public static bool On => ExtraModes.Is(ExtraMode.MoveControl) && RelayRace.Active == null;

    static List<Vector3> originals;
    static readonly HashSet<int> handled = new HashSet<int>();
    static bool revealing;
    static CourseRenderer pictureMarks;      // maps with the course printed in the picture get the moved controls drawn on top
    static readonly Dictionary<int, Vector3> moved = new Dictionary<int, Vector3>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        ResetState();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit); not a new race
        if (mode == LoadSceneMode.Additive) return;
        ResetState();
    }

    static void ResetState()
    {
        originals = null;
        handled.Clear();
        moved.Clear();
        revealing = false;
        pictureMarks = null;
    }

    static PathfindingManager Manager => UnityEngine.Object.FindObjectOfType<PathfindingManager>();

    // Where the controls were laid, before anyone moved anything
    public static void Capture(PathfindingManager manager)
    {
        if (originals != null || manager == null || manager.controlPoints == null) return;
        originals = new List<Vector3>();
        foreach (Transform point in manager.controlPoints) originals.Add(point != null ? point.position : Vector3.zero);
    }

    public static Vector3 Original(int index)
    {
        PathfindingManager manager = Manager;
        Capture(manager);
        if (originals != null && index >= 0 && index < originals.Count) return originals[index];
        return manager != null ? manager.controlPoints[index].position : Vector3.zero;
    }

    // Why the local player cannot move the control they are drawing to now, or null when they can
    public static string CannotMove(NetworkPlayer local, AgentMovement agent)
    {
        if (local.moveUsed && local.moveControl != agent.currentKontroll) return "You have used your move (control " + local.moveControl + ").";
        if (agent.pathfindingManager == null) return "The map is not ready yet.";
        if (agent.currentKontroll >= agent.pathfindingManager.controlPoints.Count - 1) return "The finish cannot be moved.";
        if (handled.Contains(agent.currentKontroll)) return "This control has already been moved.";
        return null;
    }

    // Keeps the place within 100 m of where the control was laid
    public static Vector3 Clamp(int index, Vector3 world)
    {
        Vector3 original = Original(index);
        Vector2 offset = (Vector2)(world - original);
        float max = MaxMeters * U;
        if (offset.magnitude > max) offset = offset.normalized * max;
        return new Vector3(original.x + offset.x, original.y + offset.y, original.z);
    }

    // Why the control cannot go here, or null when it can
    public static string PlaceProblem(Vector3 world, AgentMovement agent)
    {
        PathfindingManager manager = agent.pathfindingManager;
        if (manager == null || manager.speedGrid2D == null) return "The map is not ready yet.";
        Vector2Int cell = agent.WorldToGridPositionCentered(world);
        if (manager.speedGrid2D[cell.y, cell.x] <= 0.01f) return "Nobody can get there. Put the control on ground you can run on.";
        return null;
    }

    // The local player moves the control they are drawing to. Only they see it until everyone has drawn.
    public static void PlaceLocal(NetworkPlayer local, AgentMovement agent, Vector3 world)
    {
        int index = agent.currentKontroll;
        world = Clamp(index, world);
        local.moveControl = index;
        local.moveTarget = new Vector2(world.x, world.y);
        local.moveUsed = true;

        Transform point = agent.pathfindingManager.controlPoints[index];
        point.position = new Vector3(world.x, world.y, point.position.z);
        // A line already drawn to the old place is no good any more
        agent.manualPathDrawer.CancelCurrentLine();
        Redraw(-1, index);
    }

    // ---- When everyone has drawn ----

    // Called instead of starting the runners when everyone has drawn. Returns true while the moved control is flying
    // to its new place; proceed is called when the runners may go.
    public static bool InterceptAllDrawn(AgentMovement agent, Action proceed)
    {
        if (!On || agent == null || agent.pathfindingManager == null) return false;
        int index = agent.currentKontroll;
        if (handled.Contains(index)) return revealing;

        List<NetworkPlayer> movers = new List<NetworkPlayer>();
        Vector2 sum = Vector2.zero;
        foreach (NetworkPlayer player in UnityEngine.Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid || !player.moveUsed || player.moveControl != index) continue;
            movers.Add(player);
            sum += player.moveTarget;
        }
        handled.Add(index);
        if (movers.Count == 0) return false;

        movers.Sort((a, b) => a.Object.StateAuthority.PlayerId.CompareTo(b.Object.StateAuthority.PlayerId));
        Vector2 middle = sum / movers.Count;
        Capture(agent.pathfindingManager);
        revealing = true;
        GameModeRuntime.Host.StartCoroutine(Reveal(agent, index, middle, movers, proceed));
        return true;
    }

    static IEnumerator Reveal(AgentMovement agent, int index, Vector2 middle, List<NetworkPlayer> movers, Action proceed)
    {
        PathfindingManager manager = agent.pathfindingManager;
        Transform point = manager.controlPoints[index];
        Vector3 from = point.position;
        Vector3 to = new Vector3(middle.x, middle.y, from.z);

        // The runner's new route, worked out while the control flies
        List<Vector2Int> drawn = agent.path != null ? new List<Vector2Int>(agent.path) : null;
        Vector2Int startCell = agent.WorldToGridPositionCentered(manager.controlPoints[index - 1].position);
        Vector2Int goal = agent.WorldToGridPositionCentered(to);
        ManualPathDrawer drawer = agent.manualPathDrawer;
        float[,] speed = manager.speedGrid2D;
        Dictionary<string, float[,]> slopes = manager.slopeFactors;
        Task<List<Vector2Int>> task = Task.Run(() => NewRoute(speed, slopes, drawer, drawn, startCell, goal));

        // Who moved it
        List<string> names = new List<string>();
        foreach (NetworkPlayer mover in movers) names.Add(mover.Object.HasStateAuthority ? "you" : BearMode.Name(mover));
        string who = names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
        float meters = Vector2.Distance(Original(index), to) / U;
        string detail = "Moved by " + who + (names.Count > 1 ? ", so it lands in the middle" : "") + ": " + Mathf.RoundToInt(meters) + " m from where it was laid.";
        ModeBanner.Show(null, "Control " + index + " is on the move!", detail, CourseRenderer.CoursePurple, 3.4f, false, ModeBanner.LoadSound("control_whoosh"));

        // Show the old place and the new one
        if (agent.CamController != null)
        {
            agent.CamController.FocusOnSegment(startCell, goal);
        }

        // The control lifts off, flies to its new place spinning and lands with a puff
        Redraw(index, -1);
        GameObject flying = ControlMark("Flying control " + index, index.ToString(), CourseRenderer.CoursePurple, 35);
        List<GameObject> trail = new List<GameObject>();
        float t = 0f;
        float nextDot = 0f;
        const float lift = 0.35f, fly = 1.3f;
        while (t < lift + fly)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01((t - lift) / fly);
            float ease = f * f * (3f - 2f * f);
            Vector3 at = Vector3.Lerp(from, to, ease);
            float hop = t < lift ? Mathf.Sin(t / lift * Mathf.PI * 0.5f) * 0.4f : 0.4f * Mathf.Sin((1f - f) * Mathf.PI * 0.5f) + 0.5f * Mathf.Sin(f * Mathf.PI);
            flying.transform.position = new Vector3(at.x, at.y, -1.5f);
            flying.transform.localScale = Vector3.one * (1f + hop);
            flying.transform.localRotation = Quaternion.Euler(0f, 0f, -360f * ease + (t < lift ? Mathf.Sin(t * 40f) * 8f : 0f));
            if (t > lift && t >= nextDot)
            {
                nextDot = t + 0.07f;
                trail.Add(Dot(at));
            }
            FadeTrail(trail);
            yield return null;
        }
        point.position = to;
        moved[index] = to;
        UnityEngine.Object.Destroy(flying);
        Redraw(-1, -1);

        // Landing puff
        GameObject puff = ControlMark("Landing", "", CourseRenderer.CoursePurple, 34);
        float p = 0f;
        while (p < 0.5f)
        {
            p += Time.deltaTime;
            puff.transform.position = new Vector3(to.x, to.y, -1.5f);
            puff.transform.localScale = Vector3.one * (1f + p * 2.4f);
            foreach (LineRenderer line in puff.GetComponentsInChildren<LineRenderer>())
            {
                Color c = CourseRenderer.CoursePurple;
                c.a = 1f - p / 0.5f;
                line.startColor = line.endColor = c;
            }
            FadeTrail(trail);
            yield return null;
        }
        UnityEngine.Object.Destroy(puff);
        foreach (GameObject dot in trail) if (dot != null) UnityEngine.Object.Destroy(dot);

        // Wait for the new route (it is usually long done)
        float waited = 0f;
        while (!task.IsCompleted && waited < 8f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        if (task.IsCompleted && !task.IsFaulted && task.Result != null && task.Result.Count > 0)
        {
            agent.Initialize(task.Result);
        }
        else if (task.IsFaulted)
        {
            Debug.LogWarning("Move a control: could not work out the new route: " + task.Exception);
        }
        revealing = false;
        proceed?.Invoke();
    }

    // The drawn line, then on from where it ends to the control's new place, made into a route with A* in a narrow
    // corridor round the whole of it
    static List<Vector2Int> NewRoute(float[,] speed, Dictionary<string, float[,]> slopes, ManualPathDrawer drawer,
                                     List<Vector2Int> drawn, Vector2Int startCell, Vector2Int goal)
    {
        AStarPathfinder astar = new AStarPathfinder(speed, slopes);
        if (drawn == null || drawn.Count == 0)
        {
            List<List<Vector2Int>> direct = astar.GetAllPaths(startCell, goal, 1);
            return direct.Count > 0 ? direct[0] : null;
        }
        Vector2Int end = drawn[drawn.Count - 1];
        if (end == goal) return drawn;
        List<Vector2Int> combined = new List<Vector2Int>(drawn);
        List<List<Vector2Int>> onward = astar.GetAllPaths(end, goal, 1);
        if (onward.Count > 0 && onward[0] != null && onward[0].Count > 1)
        {
            combined.AddRange(onward[0].GetRange(1, onward[0].Count - 1));
        }
        else
        {
            combined.Add(goal);
        }
        List<Vector2Int> route = drawer.FindOptimizedPathInCorridor(combined, 1);
        if (route == null || route.Count == 0) route = drawer.FindOptimizedPathInCorridor(combined, 3);
        return route != null && route.Count > 0 ? route : combined;
    }

    // ---- Drawing the course ----

    // Draws the course again where the controls are now. skip: a control left out (it is flying); shadow: a control
    // whose first place is shown faded (the player's own move, before the others know)
    public static void Redraw(int skip, int shadow)
    {
        PathfindingManager manager = Manager;
        if (manager == null) return;
        List<Transform> points = manager.controlPoints;
        CourseRenderer course = FindCourseRenderer();
        if (course == null)
        {
            DrawOnPicture(skip, shadow, points);
            return;
        }
        List<CourseRenderer.Mark> marks = new List<CourseRenderer.Mark>();
        List<CourseRenderer.Line> lines = new List<CourseRenderer.Line>();
        int last = points.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            if (i == skip) continue;
            CourseRenderer.MarkKind kind = i == 0 ? CourseRenderer.MarkKind.Start : i == last ? CourseRenderer.MarkKind.Finish : CourseRenderer.MarkKind.Control;
            marks.Add(new CourseRenderer.Mark
            {
                position = points[i].position,
                kind = kind,
                label = kind == CourseRenderer.MarkKind.Control ? i.ToString() : "",
                color = CourseRenderer.CoursePurple,
                toward = i == 0 && points.Count > 1 ? points[1].position - points[0].position : Vector3.zero
            });
            if (i > 0 && i - 1 != skip)
            {
                lines.Add(new CourseRenderer.Line
                {
                    from = points[i - 1].position,
                    to = points[i].position,
                    fromKind = i - 1 == 0 ? CourseRenderer.MarkKind.Start : CourseRenderer.MarkKind.Control,
                    toKind = kind,
                    color = CourseRenderer.CoursePurple
                });
            }
        }
        course.DrawMarks(marks, lines);
        if (shadow > 0 && shadow < last)
        {
            course.AddShadows(new List<Vector3> { Original(shadow) }, new List<string> { shadow + " (laid)" });
        }
    }

    static CourseRenderer FindCourseRenderer()
    {
        foreach (CourseRenderer renderer in UnityEngine.Object.FindObjectsByType<CourseRenderer>(FindObjectsSortMode.None))
        {
            if (renderer != pictureMarks && renderer.gameObject.name == "Course") return renderer;
        }
        return null;
    }

    // Old map pictures have the course printed in them, so the moved controls are drawn on top of the picture
    static void DrawOnPicture(int skip, int shadow, List<Transform> points)
    {
        if (pictureMarks == null)
        {
            pictureMarks = new GameObject("Moved controls").AddComponent<CourseRenderer>();
        }
        pictureMarks.sizeFactor = CourseRenderer.SizeFor(CourseLoader.LoadedMap);
        List<CourseRenderer.Mark> marks = new List<CourseRenderer.Mark>();
        Dictionary<int, Vector3> show = new Dictionary<int, Vector3>(moved);
        if (shadow > 0 && shadow < points.Count) show[shadow] = points[shadow].position;
        foreach (KeyValuePair<int, Vector3> pair in show)
        {
            if (pair.Key == skip) continue;
            marks.Add(new CourseRenderer.Mark { position = pair.Value, kind = CourseRenderer.MarkKind.Control, label = pair.Key.ToString(), color = CourseRenderer.CoursePurple });
        }
        pictureMarks.DrawMarks(marks, new List<CourseRenderer.Line>());
    }

    // A control circle with its number, drawn round its own centre so it can be moved, turned and scaled
    public static GameObject ControlMark(string name, string number, Color color, int order)
    {
        GameObject root = new GameObject(name);
        float size = U * CourseRenderer.SizeFor(CourseLoader.LoadedMap);   // smaller on sprint maps
        LineRenderer circle = root.AddComponent<LineRenderer>();
        circle.sharedMaterial = LineMaterial;
        circle.useWorldSpace = false;
        circle.loop = true;
        circle.widthMultiplier = 4.5f * size;
        circle.startColor = circle.endColor = color;
        circle.sortingOrder = order;
        const int segments = 48;
        circle.positionCount = segments;
        float radius = 37f * size;
        for (int k = 0; k < segments; k++)
        {
            float a = k * Mathf.PI * 2f / segments;
            circle.SetPosition(k, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
        }
        if (!string.IsNullOrEmpty(number))
        {
            TextMeshPro text = new GameObject("Number").AddComponent<TextMeshPro>();
            text.transform.SetParent(root.transform, false);
            float offset = (37f + 45f * 0.45f) * size;
            text.transform.localPosition = new Vector3(offset, offset, 0f);
            text.text = number;
            text.fontSize = 45f * size * 10f;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.rectTransform.sizeDelta = new Vector2(45f * size * 3f, 45f * size * 1.5f);
            text.sortingOrder = order;
        }
        return root;
    }

    static Material lineMaterial;
    static Material LineMaterial
    {
        get
        {
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
            return lineMaterial;
        }
    }

    // A small dot of the flight's trail, which fades away
    static GameObject Dot(Vector3 at)
    {
        GameObject dot = new GameObject("Trail dot");
        dot.transform.position = new Vector3(at.x, at.y, -1.45f);
        LineRenderer ring = dot.AddComponent<LineRenderer>();
        ring.sharedMaterial = LineMaterial;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.widthMultiplier = 5f * U;
        ring.startColor = ring.endColor = CourseRenderer.CoursePurple;
        ring.sortingOrder = 33;
        ring.positionCount = 12;
        for (int k = 0; k < 12; k++)
        {
            float a = k * Mathf.PI * 2f / 12;
            ring.SetPosition(k, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 4f * U);
        }
        return dot;
    }

    static void FadeTrail(List<GameObject> trail)
    {
        foreach (GameObject dot in trail)
        {
            if (dot == null) continue;
            LineRenderer ring = dot.GetComponent<LineRenderer>();
            Color c = ring.startColor;
            c.a = Mathf.Max(0f, c.a - Time.deltaTime * 0.9f);
            ring.startColor = ring.endColor = c;
        }
    }
}
