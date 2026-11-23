using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using static GridData;

public class GeoJsonSpawner : MonoBehaviour
{
    [Header("GeoJSON filer")]
    [SerializeField] private TextAsset pointGeoJsonFile;   // punkter
    [SerializeField] private TextAsset lineGeoJsonFile;    // linjer
    [SerializeField] private TextAsset polygonGeoJsonFile; // polygoner

    [Header("Inställningar")]
    [SerializeField] private Material defaultLineMaterial;
    [SerializeField] private Color polygonColor = new Color(0f, 0.5f, 1f, 0.3f);
    [SerializeField] private bool objectsVisible = false;

    [Header("Laddning")]
    [Tooltip("Polygonerna (skog, sankmark, byggnader ...) används inte av någon spellogik just nu. Slå på om du lägger till kod som reagerar på dem.")]
    [SerializeField] private bool spawnPolygons = false;
    [Tooltip("Hur många millisekunder per bildruta som får gå åt till att skapa kartobjekt medan banan laddas.")]
    [SerializeField] private float spawnBudgetMs = 6f;

    private BoundsData bounds;
    private PathfindingManager pathfindingManager;
    private Transform parentContainer;
    private Sprite pointSprite;
    private int lineLayer;

    private enum FeatureKind { Point, Line, Polygon }

    private struct ParsedFeature
    {
        public FeatureKind kind;
        public string layerName;
        public Vector3[] points;
        public int pointCount;   // lines: points before simplifying (the miss logic uses it as the line's size)
    }

    // The only line names the game's miss logic reacts to (RunnerCollision, RunnerMapCollision, AgentMovement),
    // written without spaces and in lower case. Other lines get no collider: on a sprint map there are thousands
    // of them (buildings, fences, walls ...) and they made the physics engine stall while the runner ran.
    private static readonly HashSet<string> UsedLineNames = new HashSet<string>
    {
        "höjdkurva", "opasserbarbrant", "opasserbarbrant(valfritagg)", "passerbarbrant/skrent", "avlångpunkthöjd",
        "storstig", "litenstig", "litenotydligstig", "passerbartlitetvattendrag", "mindredike", "störrebäck", "storbäck",
        "smalväg", "mindreväg0.30", "störreväg0.5", "körväg", "cykelväg", "cykelväg,sidlinje", "väg:sidlinje",
        "motorväg", "rågång/drivningsväg", "järnväg", "beståndsgräns"
    };

    // Contour lines only count for the miss logic when they are short (small knolls); long ones do nothing
    private const int LongestUsefulContour = 100;

    private static bool KeepLine(string layerName, int pointCount)
    {
        int space = layerName.IndexOf(' ');
        string name = (space >= 0 ? layerName.Substring(space + 1) : layerName).Replace(" ", "").Trim().ToLowerInvariant();
        if (!UsedLineNames.Contains(name)) return false;
        return name != "höjdkurva" || pointCount < LongestUsefulContour;
    }

    // Drops points that lie within 'tolerance' of the straight line between their neighbours (Douglas-Peucker).
    // The points are already snapped to grid cells, so a tolerance of one cell keeps the line on the same cells.
    private static Vector3[] Simplify(Vector3[] points, float tolerance)
    {
        if (points.Length <= 2) return points;
        var keep = new bool[points.Length];
        keep[0] = keep[points.Length - 1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Length - 1));
        float tol2 = tolerance * tolerance;
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            Vector2 pa = points[a], pb = points[b];
            Vector2 ab = pb - pa;
            float len2 = ab.sqrMagnitude;
            int worst = -1;
            float worstDist = tol2;
            for (int i = a + 1; i < b; i++)
            {
                Vector2 ap = (Vector2)points[i] - pa;
                float d2;
                if (len2 < 1e-12f) d2 = ap.sqrMagnitude;
                else
                {
                    float cross = ab.x * ap.y - ab.y * ap.x;
                    d2 = cross * cross / len2;
                }
                if (d2 > worstDist) { worstDist = d2; worst = i; }
            }
            if (worst < 0) continue;
            keep[worst] = true;
            stack.Push((a, worst));
            stack.Push((worst, b));
        }
        var result = new List<Vector3>();
        for (int i = 0; i < points.Length; i++)
            if (keep[i]) result.Add(points[i]);
        return result.ToArray();
    }

    // Allt som behövs för att räkna om lon/lat till världskoordinater, så att det kan göras i en bakgrundstråd
    private struct GridMapping
    {
        public int rows;
        public int cols;
        public float cellSize;
        public float minx, miny, maxx, maxy;

        public Vector3 ToWorld(float lon, float lat)
        {
            // Normalisera lon/lat till [0..1]
            float u = (lon - minx) / (maxx - minx);
            float v = (lat - miny) / (maxy - miny);

            // Skala till grid-index
            int j = Mathf.RoundToInt(u * (cols - 1)); // kolumn
            int i = Mathf.RoundToInt(v * (rows - 1)); // rad

            float totalWidth = cols * cellSize;
            float totalHeight = rows * cellSize;

            float x = j * cellSize + cellSize / 2f - totalWidth / 2f;
            float y = i * cellSize + cellSize / 2f - totalHeight / 2f;

            return new Vector3(x, y, -1f);
        }
    }

    public void LoadJson(BoundsData bounds, PathfindingManager pathfinding)
    {
        pathfindingManager = pathfinding;
        this.bounds = bounds;

        // Skapa parent om den inte redan finns
        GameObject parentGO = GameObject.Find("Objects");
        if (parentGO == null)
        {
            parentGO = new GameObject("Objects");
        }
        parentContainer = parentGO.transform;

        lineLayer = LayerMask.NameToLayer("LineLayer");
        if (objectsVisible)
        {
            pointSprite = Resources.Load<Sprite>("Sprites/Circle"); // lägg in en enkel cirkel i Resources
        }

        StartCoroutine(LoadRoutine());
    }

    // JSON-filerna läses i bakgrundstrådar och objekten skapas sedan utspritt över flera bildrutor,
    // så att scenen inte står still medan flera tusen objekt byggs.
    private IEnumerator LoadRoutine()
    {
        var totalWatch = System.Diagnostics.Stopwatch.StartNew();

        GridMapping mapping = new GridMapping
        {
            rows = pathfindingManager.speedGrid2D.GetLength(0),
            cols = pathfindingManager.speedGrid2D.GetLength(1),
            cellSize = pathfindingManager.cellSize,
            minx = bounds.minx,
            miny = bounds.miny,
            maxx = bounds.maxx,
            maxy = bounds.maxy
        };

        // Imported maps bring their own map objects; the old Illern map uses the files set in the scene
        var files = new List<byte[]>(); // måste läsas på huvudtråden
        MapDefinition map = CourseLoader.LoadedMap;
        if (map != null && !string.IsNullOrEmpty(map.pointsPath) && pathfindingManager.jsonFileName == map.gridName + ".json")
        {
            byte[] pointData = MapPackages.LoadBytes(map.pointsPath, ".json");
            byte[] lineData = MapPackages.LoadBytes(map.linesPath, ".json");
            if (pointData != null) files.Add(pointData);
            if (lineData != null) files.Add(lineData);
        }
        else
        {
            if (pointGeoJsonFile != null) files.Add(pointGeoJsonFile.bytes);
            if (lineGeoJsonFile != null) files.Add(lineGeoJsonFile.bytes);
            if (polygonGeoJsonFile != null && spawnPolygons) files.Add(polygonGeoJsonFile.bytes);
        }

        // The forest mistakes need every point and line object in metres, without rounding to the cell
        int indexGeneration = MapFeatureIndex.Clear();
        {
            var indexFiles = new List<byte[]>();
            if (map != null && !string.IsNullOrEmpty(map.pointsPath) && pathfindingManager.jsonFileName == map.gridName + ".json")
                indexFiles.AddRange(files);
            else
            {
                if (pointGeoJsonFile != null) indexFiles.Add(pointGeoJsonFile.bytes);
                if (lineGeoJsonFile != null) indexFiles.Add(lineGeoJsonFile.bytes);
            }
            var indexMap = new MapFeatureIndex.Mapping
            {
                rows = mapping.rows, cols = mapping.cols, cellSize = mapping.cellSize,
                minx = mapping.minx, miny = mapping.miny, maxx = mapping.maxx, maxy = mapping.maxy
            };
            float metresPerUnit = pathfindingManager.gridData != null && pathfindingManager.cellSize > 0f
                ? pathfindingManager.gridData.grid_size / pathfindingManager.cellSize : 100f;
            float[,] speedGrid = pathfindingManager.speedGrid2D;
            float[,] heightGrid = GridBinary.LastHeights;
            // What every cell's ground is (marsh, field, green ...), from maps imported since November 2026
            byte[] kinds = null;
            if (map != null && !string.IsNullOrEmpty(map.gridName) && map.gridName.EndsWith("grid") && pathfindingManager.jsonFileName == map.gridName + ".json")
                kinds = MapPackages.LoadBytes(map.gridName.Substring(0, map.gridName.Length - 4) + "kinds", ".bytes");
            Task.Run(() =>
            {
                try { MapFeatureIndex.Build(indexFiles, indexMap, metresPerUnit, speedGrid, heightGrid, kinds, indexGeneration); }
                catch (System.Exception e) { Debug.LogError("MapFeatureIndex: " + e); }
            });
        }

        var tasks = new List<Task<List<ParsedFeature>>>();
        foreach (byte[] file in files)
        {
            byte[] data = file;
            tasks.Add(Task.Run(() => ParseGeoJson(data, mapping)));
        }

        int created = 0;
        var frameWatch = new System.Diagnostics.Stopwatch();
        foreach (var task in tasks)
        {
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
            {
                Debug.LogError("GeoJsonSpawner: kunde inte läsa GeoJSON: " + task.Exception);
                continue;
            }

            frameWatch.Restart();
            foreach (ParsedFeature feature in task.Result)
            {
                switch (feature.kind)
                {
                    case FeatureKind.Point:
                        SpawnPoint(feature.points[0], feature.layerName);
                        break;
                    case FeatureKind.Line:
                        SpawnLine(feature.points, feature.layerName, feature.pointCount);
                        break;
                    case FeatureKind.Polygon:
                        SpawnPolygon(feature.points, feature.layerName);
                        break;
                }
                created++;

                if (frameWatch.Elapsed.TotalMilliseconds >= spawnBudgetMs)
                {
                    yield return null;
                    frameWatch.Restart();
                }
            }
        }

        Debug.Log($"GeoJsonSpawner: skapade {created} kartobjekt på {totalWatch.Elapsed.TotalSeconds:F1} s (utspritt över bildrutor)");
    }

    private static List<ParsedFeature> ParseGeoJson(byte[] data, GridMapping mapping)
    {
        JObject geoData;
        using (var reader = new JsonTextReader(new StreamReader(new MemoryStream(data))))
        {
            geoData = JObject.Load(reader);
        }
        JArray features = (JArray)geoData["features"];
        var result = new List<ParsedFeature>(features.Count);

        foreach (var feature in features)
        {
            string type = feature["geometry"]["type"].ToString();
            JArray coords = (JArray)feature["geometry"]["coordinates"];
            JObject properties = (JObject)feature["properties"];
            string layerName = properties["Layer"]?.ToString() ?? "Unknown";

            switch (type)
            {
                case "Point":
                    result.Add(new ParsedFeature
                    {
                        kind = FeatureKind.Point,
                        layerName = layerName,
                        points = new[] { mapping.ToWorld((float)coords[0], (float)coords[1]) }
                    });
                    break;
                case "LineString":
                    if (!KeepLine(layerName, coords.Count)) break;
                    result.Add(new ParsedFeature
                    {
                        kind = FeatureKind.Line,
                        layerName = layerName,
                        points = Simplify(ToWorldPoints(coords, mapping), mapping.cellSize),
                        pointCount = coords.Count
                    });
                    break;
                case "Polygon":
                    // GeoJSON polygoner har oftast coords[0] = yttre ring
                    result.Add(new ParsedFeature
                    {
                        kind = FeatureKind.Polygon,
                        layerName = layerName,
                        points = ToWorldPoints((JArray)coords[0], mapping)
                    });
                    break;
                default:
                    Debug.LogWarning($"GeoJsonSpawner: Geometri-typen '{type}' stöds inte ännu.");
                    break;
            }
        }

        return result;
    }

    private static Vector3[] ToWorldPoints(JArray coords, GridMapping mapping)
    {
        Vector3[] points = new Vector3[coords.Count];
        for (int i = 0; i < points.Length; i++)
        {
            JToken c = coords[i];
            points[i] = mapping.ToWorld((float)c[0], (float)c[1]);
        }
        return points;
    }

    private void SpawnPoint(Vector3 pos, string featureType)
    {
        GameObject obj = new GameObject("Point_" + featureType);
        obj.transform.position = pos;
        obj.transform.localScale = Vector3.one * 0.1f;

        // Gör den synlig (valfritt, en liten sprite istället för 3D-sfär)
        if (objectsVisible)
        {
            SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = pointSprite;
            sr.color = Color.red;
        }

        // Lägg till en 2D collider
        CircleCollider2D col = obj.AddComponent<CircleCollider2D>();
        col.isTrigger = true;

        obj.transform.SetParent(parentContainer);

        SetupFeature(obj, featureType, 0f, 0f, 1f);
    }


    private void SpawnLine(Vector3[] points, string featureType, int pointCount)
    {
        GameObject obj = new GameObject(featureType);
        obj.transform.SetParent(parentContainer);

        // Linjen ritas bara om objekten ska synas, annars räcker collidern
        if (objectsVisible)
        {
            LineRenderer lr = obj.AddComponent<LineRenderer>();
            lr.material = defaultLineMaterial;
            lr.widthMultiplier = 0.05f;
            lr.positionCount = points.Length;
            lr.SetPositions(points);
        }

        // Collider för kollision
        EdgeCollider2D col = obj.AddComponent<EdgeCollider2D>();
        Vector2[] edgePoints = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
            edgePoints[i] = new Vector2(points[i].x, points[i].y);
        col.points = edgePoints;
        col.isTrigger = true;

        // Sätt layer till "LineLayer" (lägg till i Unity Editor om du inte har det)
        obj.layer = lineLayer;

        SetupFeature(obj, featureType, 0f, pointCount, 0f);
    }

    private void SpawnPolygon(Vector3[] ring, string featureType)
    {
        List<Vector2> polyPoints = new List<Vector2>(ring.Length);
        foreach (Vector3 worldPos in ring)
        {
            polyPoints.Add(new Vector2(worldPos.x, worldPos.y));
        }

        GameObject obj = new GameObject("Polygon_" + featureType);
        obj.transform.SetParent(parentContainer);

        // Collider
        PolygonCollider2D col = obj.AddComponent<PolygonCollider2D>();
        col.points = polyPoints.ToArray();

        // Enkel visualisering
        if (objectsVisible)
        {
            SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
            sr.color = polygonColor;
        }

        float area = CalculatePolygonArea(polyPoints);
        SetupFeature(obj, featureType, area, 0f, 0f);
    }


    private void SetupFeature(GameObject obj, string featureType, float area, float length, float size)
    {
        obj.tag = "MapFeature";
        var mf = obj.AddComponent<MapFeature>();
        mf.featureType = featureType;
        mf.area = area;
        mf.length = length;
        mf.size = size;
    }

    private float CalculatePolygonArea(List<Vector2> poly)
    {
        float area = 0f;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            area += (poly[j].x + poly[i].x) * (poly[j].y - poly[i].y);

        return Mathf.Abs(area / 2f);
    }

    public Vector3 GeoToWorld(float lon, float lat, float[,] speedGrid2D, float cellSize)
    {
        GridMapping mapping = new GridMapping
        {
            rows = speedGrid2D.GetLength(0),
            cols = speedGrid2D.GetLength(1),
            cellSize = cellSize,
            minx = bounds.minx,
            miny = bounds.miny,
            maxx = bounds.maxx,
            maxy = bounds.maxy
        };
        return mapping.ToWorld(lon, lat);
    }
}
