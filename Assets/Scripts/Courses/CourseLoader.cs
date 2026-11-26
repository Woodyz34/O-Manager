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
    // A relay shows the first leg of one team (the forks differ between the teams).
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
            CourseData shown = course.IsRelay() ? course.Route(0, course.AssignBranches(new System.Random(0))[0]).course : course;
            new GameObject("Course").AddComponent<CourseRenderer>().Draw(map, shown);
        }
        Debug.Log($"CourseLoader: showing the race's course '{course.name}' on {map.displayName} to a spectator");
        return true;
    }
}
