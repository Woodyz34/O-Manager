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

    void Awake()
    {
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
}
