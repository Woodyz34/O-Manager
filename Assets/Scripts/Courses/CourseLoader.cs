using System.Collections.Generic;
using UnityEngine;

// Sets up the competition scene for the chosen course before anything else in the scene starts:
// puts the right map picture in place, creates the start, control and finish points the runners use
// and draws the course on top of the map.
// Runs before PathfindingManager.Awake, which reads the first control point.
[DefaultExecutionOrder(-500)]
public class CourseLoader : MonoBehaviour
{
    public PathfindingManager pathfindingManager;
    public SpriteRenderer mapRenderer;     // the map picture (tagged MapImage)
    public Transform pointParent;          // where the control points are created

    public static CourseData Loaded { get; private set; }
    public static MapDefinition LoadedMap { get; private set; }
    // The scene the course above was set up in (a spectator checks that the map it sees is this race's, Spectator)
    public static int LoadedScene { get; private set; }

    // A player who joins a lobby that is out racing (2026-11-26, Anders): the scene starts before the host's player is
    // there, so the course here was this player's own last course, drawn on its map, and the spectator watched the race
    // on the wrong map. Now nothing is shown until the race's own course is known: the map picture stays empty and the
    // course is not drawn (the points are still made, PathfindingManager needs them), and Spectator shows the race's
    // course with ShowRaceCourse once it has it, or says why it can't (not downloaded yet, or the host's private map).
    public static bool HeldForSpectator { get; private set; }

    static CourseLoader instance;

    // The local player has come into a race that is already on, instead of from the lobby
    static bool JoiningRaceInProgress()
    {
        Fusion.NetworkRunner runner = FindAnyObjectByType<Fusion.NetworkRunner>();
        if (runner == null || !runner.IsRunning || runner.GameMode == Fusion.GameMode.Single || runner.IsSharedModeMasterClient) return false;
        if (!SessionRacing.IsRacing(runner.SessionInfo)) return false;
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.Object == null || !local.Object.IsValid) return true;
        return local.spectator || !local.BeenInLobby;
    }

    void Awake()
    {
        LoadedScene = 0;
        HeldForSpectator = false;
        instance = this;
        if (JoiningRaceInProgress())
        {
            HoldForSpectator();
            return;
        }
        CourseLibrary.ApplyFromHost();
        CourseData course = CourseLibrary.CurrentOrDefault;
        MapDefinition map = course != null ? MapDefinition.Find(course.mapId) : null;
        if (course == null || map == null)
        {
            Debug.LogError("CourseLoader: no course or map to load");
            return;
        }
        Loaded = course;
        LoadedMap = map;
        LoadedScene = gameObject.scene.handle;
        Debug.Log($"CourseLoader: loading course '{course.name}' by {course.author} on {map.displayName}");

        MapBackground background = map.GetBackground(course.background);
        if (background != null)
        {
            Sprite sprite = MapPackages.LoadSprite(background);
            if (sprite != null)
            {
                mapRenderer.sprite = sprite;
            }
            else
            {
                Debug.LogError("CourseLoader: could not load map picture " + background.spritePath);
            }
            mapRenderer.transform.position = background.position;
            mapRenderer.transform.rotation = Quaternion.Euler(0f, 0f, background.rotation);
            mapRenderer.transform.localScale = new Vector3(background.scale, background.scale, 1f);
        }

        // The game runs through these in order: start, controls, finish
        if (pointParent == null)
        {
            pointParent = new GameObject("Course points").transform;
        }
        pathfindingManager.jsonFileName = map.gridName + ".json";

        // Out-of-bounds areas are drawn for every kind of course, also on pictures with the course printed in them
        if (ForbiddenAreas.Any(course))
        {
            new GameObject("Out-of-bounds areas").AddComponent<ForbiddenAreaRenderer>().Draw(map, course.forbidden);
        }

        // Relays set up their own points, leg by leg and with this team's fork variants
        if (course.IsRelay())
        {
            RelayRace.Create(course, map, pathfindingManager, pointParent);
            return;
        }
        List<Transform> points = new List<Transform>();
        List<CoursePoint> coursePoints = course.AllPoints();
        for (int i = 0; i < coursePoints.Count; i++)
        {
            Transform point = new GameObject(i == 0 ? "Start" : i == coursePoints.Count - 1 ? "Finish" : "Control " + i).transform;
            point.SetParent(pointParent, false);
            point.position = map.ToWorld(coursePoints[i]);
            points.Add(point);
        }
        pathfindingManager.controlPoints = points;
        pathfindingManager.jsonFileName = map.gridName + ".json";
        IndividualRace.Create(course);

        // The old pictures already have the course printed on them
        if (background == null || !background.courseInPicture)
        {
            CourseRenderer courseRenderer = new GameObject("Course").AddComponent<CourseRenderer>();
            courseRenderer.Draw(map, course);
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // Joined in the middle of a race: no map picture and no course yet. The points of a course whose map is on this
    // computer are made all the same (PathfindingManager reads them in its Awake), but nothing is drawn.
    void HoldForSpectator()
    {
        HeldForSpectator = true;
        Loaded = null;
        LoadedMap = null;
        if (mapRenderer != null)
        {
            mapRenderer.sprite = null;
            mapRenderer.enabled = false;
        }
        CourseData course = CourseLibrary.Current;
        if (course == null || MapDefinition.FindExact(course.mapId) == null)
        {
            course = CourseLibrary.BuiltIn.Count > 0 ? CourseLibrary.BuiltIn[0] : null;
        }
        MapDefinition map = course != null ? MapDefinition.FindExact(course.mapId) : null;
        if (course == null || map == null)
        {
            Debug.LogError("CourseLoader: joined a race, and there is no map on this computer to set the scene up with");
            return;
        }
        LoadedMap = map;   // the grid below is this map's; ShowRaceCourse replaces it with the race's
        if (pointParent == null) pointParent = new GameObject("Course points").transform;
        if (course.IsRelay()) course.Normalize();
        List<CoursePoint> coursePoints = course.IsRelay()
            ? course.Route(0, course.AssignBranches(new System.Random(0))[0]).course.AllPoints()
            : course.AllPoints();
        List<Transform> points = new List<Transform>();
        for (int i = 0; i < coursePoints.Count; i++)
        {
            Transform point = new GameObject(i == 0 ? "Start" : i == coursePoints.Count - 1 ? "Finish" : "Control " + i).transform;
            point.SetParent(pointParent, false);
            point.position = map.ToWorld(coursePoints[i]);
            points.Add(point);
        }
        pathfindingManager.controlPoints = points;
        pathfindingManager.jsonFileName = map.gridName + ".json";
        Debug.Log("CourseLoader: joined a race that is on, waiting for the race's course before showing a map");
    }

    // Spectator: the race's own course is known and its map is on this computer. Puts the map picture in place and
    // draws the course on it, only to look at (a spectator does not run, so the runners' grid is left as it is).
    // A relay shows the leg being run with every branch of its forks (ShowRelayLeg).
    public static bool ShowRaceCourse(CourseData course, MapDefinition map)
    {
        CourseLoader loader = instance != null ? instance : FindAnyObjectByType<CourseLoader>();
        if (loader == null || course == null || map == null) return false;
        MapBackground background = map.GetBackground(course.background);
        if (background == null)
        {
            Debug.LogError("CourseLoader: the race's map " + map.displayName + " has no picture");
            return false;
        }
        Sprite sprite = MapPackages.LoadSprite(background);
        if (sprite == null)
        {
            Debug.LogError("CourseLoader: could not load map picture " + background.spritePath);
            return false;
        }
        SpriteRenderer renderer = loader.mapRenderer;
        renderer.sprite = sprite;
        renderer.enabled = true;
        renderer.transform.position = background.position;
        renderer.transform.rotation = Quaternion.Euler(0f, 0f, background.rotation);
        renderer.transform.localScale = new Vector3(background.scale, background.scale, 1f);

        Loaded = course;
        LoadedMap = map;
        LoadedScene = loader.gameObject.scene.handle;
        HeldForSpectator = false;

        // A course drawn already (this player's own, if it was set up before the race's arrived) goes away
        foreach (CourseRenderer drawn in FindObjectsByType<CourseRenderer>(FindObjectsSortMode.None)) drawn.Clear();
        foreach (ForbiddenAreaRenderer drawn in FindObjectsByType<ForbiddenAreaRenderer>(FindObjectsSortMode.None)) Destroy(drawn.gameObject);

        if (ForbiddenAreas.Any(course))
        {
            new GameObject("Out-of-bounds areas").AddComponent<ForbiddenAreaRenderer>().Draw(map, course.forbidden);
        }
        if (!background.courseInPicture)
        {
            course.Normalize();
            spectatorCourse = new GameObject("Course").AddComponent<CourseRenderer>();
            spectatorLeg = -1;
            if (course.IsRelay()) ShowRelayLeg(0);
            else spectatorCourse.Draw(map, course);
        }
        Debug.Log($"CourseLoader: showing the race's course '{course.name}' on {map.displayName} to a spectator");
        return true;
    }

    static CourseRenderer spectatorCourse;
    static int spectatorLeg = -1;

    // Spectator, relay: the leg the runners are out on now, with every branch of its forks (the teams run different
    // ones), as the course builder shows a fork: 4A, 4B, ... and lines through every branch (2026-11-26, Anders)
    public static void ShowRelayLeg(int leg)
    {
        CourseData course = Loaded;
        MapDefinition map = LoadedMap;
        if (spectatorCourse == null || course == null || map == null || !course.IsRelay()) return;
        leg = Mathf.Clamp(leg, 0, Mathf.Max(0, course.legs - 1));
        if (leg == spectatorLeg) return;
        RelayLegCourse legCourse = course.LegCourse(leg);
        if (legCourse == null) return;
        spectatorLeg = leg;

        Color color = CourseRenderer.CoursePurple;
        var marks = new List<CourseRenderer.Mark>();
        var lines = new List<CourseRenderer.Line>();
        // The marks the next control (or the branches of the next fork) is drawn from
        var from = new List<Vector3>();
        var fromKind = new List<CourseRenderer.MarkKind>();
        Vector3 start = map.ToWorld(legCourse.start);
        from.Add(start);
        fromKind.Add(CourseRenderer.MarkKind.Start);
        int startIndex = marks.Count;
        marks.Add(new CourseRenderer.Mark { position = start, kind = CourseRenderer.MarkKind.Start, color = color, label = "", toward = Vector3.up });
        int number = 0;   // controls run so far

        void LineTo(Vector3 to, CourseRenderer.MarkKind toKind)
        {
            for (int i = 0; i < from.Count; i++)
                lines.Add(new CourseRenderer.Line { from = from[i], to = to, fromKind = fromKind[i], toKind = toKind, color = color });
        }

        foreach (RelayItem item in legCourse.items)
        {
            if (item.fork < 0)
            {
                Vector3 point = map.ToWorld(item.point);
                number++;
                marks.Add(new CourseRenderer.Mark { position = point, kind = CourseRenderer.MarkKind.Control, color = color, label = number.ToString() });
                LineTo(point, CourseRenderer.MarkKind.Control);
                from.Clear(); fromKind.Clear();
                from.Add(point); fromKind.Add(CourseRenderer.MarkKind.Control);
                continue;
            }
            RelayFork fork = course.Fork(item.fork);
            if (fork == null || fork.branches.Count == 0) continue;
            var ends = new List<Vector3>();
            var endKinds = new List<CourseRenderer.MarkKind>();
            int longest = 0;
            for (int b = 0; b < fork.branches.Count; b++)
            {
                List<CoursePoint> controls = fork.branches[b].controls;
                longest = Mathf.Max(longest, controls.Count);
                if (controls.Count == 0)
                {
                    // A branch straight on to the next common control
                    for (int i = 0; i < from.Count; i++) if (!ends.Contains(from[i])) { ends.Add(from[i]); endKinds.Add(fromKind[i]); }
                    continue;
                }
                Vector3 last = Vector3.zero;
                for (int k = 0; k < controls.Count; k++)
                {
                    Vector3 point = map.ToWorld(controls[k]);
                    string label = (number + k + 1) + CourseData.BranchLetter(b);
                    marks.Add(new CourseRenderer.Mark { position = point, kind = CourseRenderer.MarkKind.Control, color = color, label = label });
                    if (k == 0) LineTo(point, CourseRenderer.MarkKind.Control);
                    else lines.Add(new CourseRenderer.Line { from = last, to = point, fromKind = CourseRenderer.MarkKind.Control, toKind = CourseRenderer.MarkKind.Control, color = color });
                    last = point;
                }
                ends.Add(last);
                endKinds.Add(CourseRenderer.MarkKind.Control);
            }
            number += longest;
            from = ends;
            fromKind = endKinds;
        }
        Vector3 finish = map.ToWorld(legCourse.finish);
        marks.Add(new CourseRenderer.Mark { position = finish, kind = CourseRenderer.MarkKind.Finish, color = color, label = "" });
        LineTo(finish, CourseRenderer.MarkKind.Finish);
        // The start's triangle points at the first control (or the middle of the first fork's branches)
        Vector3 first = finish;
        for (int i = 1; i < marks.Count; i++)
        {
            if (marks[i].kind != CourseRenderer.MarkKind.Control) continue;
            first = marks[i].position;
            break;
        }
        CourseRenderer.Mark startMark = marks[startIndex];
        startMark.toward = first - start;
        marks[startIndex] = startMark;
        spectatorCourse.sizeFactor = CourseRenderer.SizeFor(map);   // smaller on sprint maps, as Draw does
        spectatorCourse.DrawMarks(marks, lines);
        Debug.Log($"CourseLoader: showing leg {leg + 1} of the relay with all its forks to a spectator");
    }
}
