using System;
using System.Collections.Generic;
using UnityEngine;

// Where on a map a generated course may go. The grid covers a rectangle around the map, and everything outside
// the drawn map (and the legend, logo and north arrow) is stored as ordinary fast ground. So the drawn map is
// found from the grid itself: every cell that is not plain 1.0 ground is a map feature, the features are grown
// together, enclosed holes are filled, and the biggest piece is the map. Controls must lie in the part of it a
// runner can reach from everywhere else.
// Imported maps (OCAD/OOM) instead close every cell outside the drawn map (speed 0), so there the map is
// everything that is not closed ground reached from the grid's border.
public class MapPlayArea
{
    public int Rows { get; private set; }
    public int Cols { get; private set; }
    public bool[,] playable;          // walkable, inside the map and connected to the rest of the course area
    public int[,] distanceToOutside;  // cells to the nearest cell outside the drawn map (chessboard distance)
    public List<Vector2Int> cells = new List<Vector2Int>();

    public float CellMeters { get; private set; }

    const float CloseMeters = 60f;        // closes the gaps between map features at the map edge
    const float EdgeMarginForest = 50f;   // keep controls this far inside the map edge
    const float EdgeMarginSprint = 20f;

    static readonly Dictionary<string, MapPlayArea> cache = new Dictionary<string, MapPlayArea>();
    static readonly object cacheLock = new object();

    public static MapPlayArea For(MapDefinition map, MapGrid grid)
    {
        lock (cacheLock)
        {
            if (!cache.TryGetValue(map.id, out MapPlayArea area))
            {
                area = new MapPlayArea(grid, map.gridSize, map.IsSprint);
                cache[map.id] = area;
            }
            return area;
        }
    }

    MapPlayArea() { }

    // The part of this area inside a polygon the player drew (corners in grid coordinates, x = column, y = row),
    // at least marginMeters inside its edge. Controls then stay inside it; the routes between them may still leave it.
    public MapPlayArea Within(IList<Vector2> gridPolygon, float marginMeters)
    {
        MapPlayArea area = new MapPlayArea { Rows = Rows, Cols = Cols, CellMeters = CellMeters };
        // The same rule as an out-of-bounds area: the cells whose centre is inside, and the cells the edge runs through
        float[,] mark = new float[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                mark[i, j] = 1f;
        ForbiddenRaster.Close(mark, gridPolygon, false);
        bool[,] outsideDrawn = new bool[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                outsideDrawn[i, j] = mark[i, j] > 0f;
        int[,] toDrawnEdge = area.Distance(outsideDrawn);
        int margin = Mathf.Max(0, Mathf.RoundToInt(marginMeters / CellMeters));

        area.distanceToOutside = new int[Rows, Cols];
        bool[,] candidate = new bool[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
            {
                area.distanceToOutside[i, j] = Math.Min(distanceToOutside[i, j], toDrawnEdge[i, j]);
                candidate[i, j] = playable[i, j] && toDrawnEdge[i, j] > margin;
            }
        area.playable = area.Largest(candidate, (i, j) => true);
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                if (area.playable[i, j]) area.cells.Add(new Vector2Int(j, i));
        return area;
    }

    // The longest distance across the area in metres (the diagonal of the box round it)
    public float SpanMeters()
    {
        if (cells.Count == 0) return 0f;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector2Int c in cells)
        {
            minX = Math.Min(minX, c.x); maxX = Math.Max(maxX, c.x);
            minY = Math.Min(minY, c.y); maxY = Math.Max(maxY, c.y);
        }
        return Mathf.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY)) * CellMeters;
    }

    public MapPlayArea(MapGrid grid, float cellMeters, bool sprint)
    {
        Rows = grid.Rows;
        Cols = grid.Cols;
        CellMeters = Mathf.Max(0.5f, cellMeters);
        int edgeMargin = Mathf.Max(1, Mathf.RoundToInt((sprint ? EdgeMarginSprint : EdgeMarginForest) / CellMeters));

        bool[,] mapped = new bool[Rows, Cols];
        if (ClosedOutside(grid))
        {
            // Imported map: the closed ground that can be reached from the border is outside the map
            bool[,] open = new bool[Rows, Cols];
            for (int i = 0; i < Rows; i++)
                for (int j = 0; j < Cols; j++)
                    open[i, j] = grid.speed[i, j] > 0.01f;
            bool[,] outside = FloodFromBorder(open);
            for (int i = 0; i < Rows; i++)
                for (int j = 0; j < Cols; j++)
                    mapped[i, j] = !outside[i, j];
        }
        else
        {
            int closeRadius = Mathf.Max(1, Mathf.RoundToInt(CloseMeters / CellMeters));
            bool[,] feature = new bool[Rows, Cols];
            for (int i = 0; i < Rows; i++)
                for (int j = 0; j < Cols; j++)
                    feature[i, j] = Mathf.Abs(grid.speed[i, j] - 1f) > 0.001f;

            // Close: grow the features, fill what they enclose, then shrink back
            int[,] toFeature = Distance(feature);
            bool[,] grown = new bool[Rows, Cols];
            for (int i = 0; i < Rows; i++)
                for (int j = 0; j < Cols; j++)
                    grown[i, j] = toFeature[i, j] <= closeRadius;
            bool[,] outside = FloodFromBorder(grown);
            int[,] toOutside = Distance(outside);
            for (int i = 0; i < Rows; i++)
                for (int j = 0; j < Cols; j++)
                    mapped[i, j] = toOutside[i, j] > closeRadius;
        }
        mapped = Largest(mapped, (i, j) => true);

        // Distance from the drawn map's edge, used to keep controls away from it
        bool[,] notMapped = new bool[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                notMapped[i, j] = !mapped[i, j];
        distanceToOutside = Distance(notMapped);

        bool[,] candidate = new bool[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                candidate[i, j] = distanceToOutside[i, j] > edgeMargin && grid.speed[i, j] > 0.01f;
        playable = Largest(candidate, (i, j) => true);
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                if (playable[i, j]) cells.Add(new Vector2Int(j, i));
    }

    // True when most of the grid's border is closed ground, as in maps imported from OCAD/OOM files
    static bool ClosedOutside(MapGrid grid)
    {
        int closed = 0, total = 0;
        for (int j = 0; j < grid.Cols; j++)
        {
            total += 2;
            if (grid.speed[0, j] <= 0.01f) closed++;
            if (grid.speed[grid.Rows - 1, j] <= 0.01f) closed++;
        }
        for (int i = 0; i < grid.Rows; i++)
        {
            total += 2;
            if (grid.speed[i, 0] <= 0.01f) closed++;
            if (grid.speed[i, grid.Cols - 1] <= 0.01f) closed++;
        }
        return closed * 2 > total;
    }

    // Distance from a cell to the drawn map's edge, in metres
    public float MetersToOutside(Vector2Int cell)
    {
        return distanceToOutside[cell.y, cell.x] * CellMeters;
    }

    public bool IsPlayable(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < Cols && cell.y < Rows && playable[cell.y, cell.x];
    }

    // Chessboard distance from every cell to the nearest marked cell (two-pass chamfer)
    int[,] Distance(bool[,] marked)
    {
        const int far = 1 << 20;
        int[,] d = new int[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                d[i, j] = marked[i, j] ? 0 : far;
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
            {
                int v = d[i, j];
                if (i > 0) { v = Math.Min(v, d[i - 1, j] + 1); if (j > 0) v = Math.Min(v, d[i - 1, j - 1] + 1); if (j < Cols - 1) v = Math.Min(v, d[i - 1, j + 1] + 1); }
                if (j > 0) v = Math.Min(v, d[i, j - 1] + 1);
                d[i, j] = v;
            }
        for (int i = Rows - 1; i >= 0; i--)
            for (int j = Cols - 1; j >= 0; j--)
            {
                int v = d[i, j];
                if (i < Rows - 1) { v = Math.Min(v, d[i + 1, j] + 1); if (j > 0) v = Math.Min(v, d[i + 1, j - 1] + 1); if (j < Cols - 1) v = Math.Min(v, d[i + 1, j + 1] + 1); }
                if (j < Cols - 1) v = Math.Min(v, d[i, j + 1] + 1);
                d[i, j] = v;
            }
        return d;
    }

    // Cells not in 'blocked' that can be reached from the grid's border
    bool[,] FloodFromBorder(bool[,] blocked)
    {
        bool[,] seen = new bool[Rows, Cols];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                if ((i == 0 || j == 0 || i == Rows - 1 || j == Cols - 1) && !blocked[i, j])
                {
                    seen[i, j] = true;
                    queue.Enqueue(new Vector2Int(j, i));
                }
        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = p.x + dx, y = p.y + dy;
                    if (x < 0 || y < 0 || x >= Cols || y >= Rows || seen[y, x] || blocked[y, x]) continue;
                    seen[y, x] = true;
                    queue.Enqueue(new Vector2Int(x, y));
                }
        }
        return seen;
    }

    // The biggest 8-connected group of marked cells
    bool[,] Largest(bool[,] marked, Func<int, int, bool> extra)
    {
        int[,] label = new int[Rows, Cols];
        int best = 0, bestSize = 0, next = 0;
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
            {
                if (!marked[i, j] || label[i, j] != 0) continue;
                next++;
                int size = 0;
                label[i, j] = next;
                queue.Enqueue(new Vector2Int(j, i));
                while (queue.Count > 0)
                {
                    Vector2Int p = queue.Dequeue();
                    size++;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = p.x + dx, y = p.y + dy;
                            if (x < 0 || y < 0 || x >= Cols || y >= Rows || label[y, x] != 0 || !marked[y, x]) continue;
                            label[y, x] = next;
                            queue.Enqueue(new Vector2Int(x, y));
                        }
                }
                if (size > bestSize) { bestSize = size; best = next; }
            }
        bool[,] result = new bool[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                result[i, j] = best != 0 && label[i, j] == best && extra(i, j);
        return result;
    }
}

// Makes a course on its own from a few choices: long, middle or sprint distance, the length and the number of controls.
// Long distance mixes short legs with a few long ones; middle distance only has short to medium-short legs;
// sprint has short legs and fits the 2 m grid of a sprint map.
// Every course it returns has been checked with the same A* the runners use, so all controls can be reached.
public static class CourseGenerator
{
    public enum Kind { Long, Middle, Sprint }

    public const float MinLengthKm = 1.5f;
    public const float MaxLengthKm = 20f;       // the longest course the game makes; still well under a second
    public const float SprintMinLengthKm = 1f;
    public const float SprintMaxLengthKm = 6f;
    public const float MinMetersPerControl = 50f; // at most one control per 50 m
    public const float SprintMinMetersPerControl = 40f;
    public const float MiddleMaxAverageLeg = 500f;
    public const float LongMaxAverageLeg = 800f;   // fewer controls gives legs too long to fit the map and slow to place
    public const float SprintMaxAverageLeg = 250f;
    public const int MinControls = 3;
    const float MinLegMeters = 60f;
    const float SprintMinLegMeters = 40f;
    const float MinControlSpacing = 60f;        // two controls never closer than this
    const float SprintMinControlSpacing = 45f;   // closer controls overlap on the map
    const float MaxDetour = 2.0f;               // a leg's shortest route at most this many times the straight line
    const float SprintMaxDetour = 2.5f;         // houses and fences make sprint routes wind more
    const int Attempts = 60;
    const double TimeBudgetSeconds = 8.0;

    public class Settings
    {
        public Kind kind = Kind.Middle;
        public float lengthKm = 4f;
        public int controls = 12;
        public int seed;
        public bool barriers;   // sprint: out-of-bounds barriers that close the obvious straight way on some legs
        // The part of the map the player drew round (map coordinates); null or fewer than 3 corners: the whole map
        public List<CoursePoint> region;
        public bool HasRegion => region != null && region.Count >= 3;
    }

    public class Result
    {
        public CourseData course;
        public string error;
        public float shortestLeg, longestLeg;
        public int routeChoiceLegs = -1;   // sprint: legs with two or more close route choices (-1: not checked)
        public int legCount;
        public int barriers;               // sprint: out-of-bounds barriers placed
    }

    // Sprint: a leg is fun when it has at least two route choices and the second fastest is at most this much
    // slower than the fastest (about 60-40 to 45-55: both are worth thinking about)
    public const float CloseChoice = 1.10f;
    const int ChoiceTriesPerLeg = 8;            // places for a control checked for route choices on each leg
    const float GoodShare = 0.6f;               // a sprint course is good enough when this share of its legs is fun
    const double ChoiceBudgetSeconds = 12.0;    // all route choice checks of one course together

    // What PlacePoints needs to check route choices on sprint legs
    class ChoiceCheck
    {
        public RouteChoiceGenerator generator;
        public DateTime started;
        public int goodLegs;
        public bool OutOfTime => (DateTime.UtcNow - started).TotalSeconds > ChoiceBudgetSeconds;
    }

    public static float MinLengthFor(Kind kind) => kind == Kind.Sprint ? SprintMinLengthKm : MinLengthKm;
    public static float MaxLengthFor(Kind kind) => kind == Kind.Sprint ? SprintMaxLengthKm : MaxLengthKm;
    public static float MinMetersPerControlFor(Kind kind) => kind == Kind.Sprint ? SprintMinMetersPerControl : MinMetersPerControl;

    public static int MaxControlsFor(float lengthKm) => MaxControlsFor(Kind.Middle, lengthKm);

    public static int MaxControlsFor(Kind kind, float lengthKm)
    {
        return Mathf.Clamp(Mathf.FloorToInt(lengthKm * 1000f / MinMetersPerControlFor(kind)), MinControls, CourseData.MaxControls);
    }

    public static int MinControlsFor(Kind kind, float lengthKm)
    {
        // Sprint legs average at most 250 m, middle distance legs at most 500 m and long distance legs at most 800 m,
        // so a longer course needs more controls
        float maxAverage = kind == Kind.Sprint ? SprintMaxAverageLeg : kind == Kind.Middle ? MiddleMaxAverageLeg : LongMaxAverageLeg;
        int needed = Mathf.CeilToInt(lengthKm * 1000f / maxAverage) - 1;
        return Mathf.Clamp(needed, MinControls, MaxControlsFor(kind, lengthKm));
    }

    public static string KindName(Kind kind) => kind == Kind.Long ? "Long" : kind == Kind.Sprint ? "Sprint" : "Middle";

    // The kinds of course a map can have: forest maps long and middle, sprint maps sprint, maps for both all three
    public static List<Kind> KindsFor(MapDefinition map)
    {
        List<Kind> kinds = new List<Kind>();
        if (map == null || map.IsForest) { kinds.Add(Kind.Long); kinds.Add(Kind.Middle); }
        if (map != null && map.IsSprint) kinds.Add(Kind.Sprint);
        return kinds;
    }

    // Can run off the main thread. The grid must already be loaded (MapGrid.Load uses Resources).
    public static Result Generate(MapDefinition map, MapGrid grid, Settings settings)
    {
        float length = Mathf.Clamp(settings.lengthKm, MinLengthFor(settings.kind), MaxLengthFor(settings.kind)) * 1000f;
        int controls = Mathf.Clamp(settings.controls, MinControlsFor(settings.kind, settings.lengthKm), MaxControlsFor(settings.kind, settings.lengthKm));
        MapPlayArea area = MapPlayArea.For(map, grid);
        if (area.cells.Count == 0)
        {
            return new Result { error = "This map has no area to lay a course on." };
        }
        string lengthText = (length / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km";
        string tooSmall = "The area you drew is too small for a " + lengthText + " course with " + controls
                          + " controls. Draw a bigger area, or choose a shorter course or fewer controls.";
        if (settings.HasRegion)
        {
            area = area.Within(ToGrid(map, settings.region), settings.kind == Kind.Sprint ? RegionMarginSprint : RegionMarginForest);
            if (area.cells.Count == 0)
            {
                return new Result { error = "The area you drew has no ground a course can use. Draw it over the map, round open ground, paths or forest." };
            }
            // Quick checks before trying: every control needs room of its own, and the legs must fit across the area
            float spacing = settings.kind == Kind.Sprint ? SprintMinControlSpacing : MinControlSpacing;
            float squareMeters = area.cells.Count * area.CellMeters * area.CellMeters;
            float averageLeg = length / (controls + 1);
            if (squareMeters < (controls + 2) * spacing * spacing * 1.2f || area.SpanMeters() < averageLeg * 0.9f)
            {
                return new Result { error = tooSmall };
            }
        }

        System.Random random = new System.Random(settings.seed);
        AStarPathfinder pathfinder = new AStarPathfinder(grid.speed, grid.slopes);
        DateTime started = DateTime.UtcNow;
        ChoiceCheck choices = null;
        if (settings.kind == Kind.Sprint)
        {
            // The same route choices as the route choice game (speed and clock do not matter: only how much slower
            // one choice is than the other)
            choices = new ChoiceCheck
            {
                generator = new RouteChoiceGenerator(grid.speed, grid.slopes, map.CellSize, 1f, 20f, true) { VariantSlower = VariantSlower },
                started = DateTime.UtcNow
            };
        }
        Result best = null;
        for (int attempt = 0; attempt < Attempts && (DateTime.UtcNow - started).TotalSeconds < TimeBudgetSeconds; attempt++)
        {
            List<float> legs = PlanLegs(settings.kind, length, controls + 1, random);
            if (choices != null) choices.goodLegs = 0;
            List<CoursePoint> points = PlacePoints(map, area, pathfinder, legs, settings.kind == Kind.Sprint, choices, random);
            if (points == null)
            {
                continue;
            }

            CourseData course = new CourseData
            {
                id = "ai-" + CourseData.NewId(),
                name = "AI " + KindName(settings.kind) + " " + (length / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km",
                author = "AI",
                authorId = "",
                mapId = map.id,
                background = "",
                builtIn = false,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                terrain = settings.kind == Kind.Sprint ? CourseData.Sprint : CourseData.Forest,
                start = points[0],
                finish = points[points.Count - 1],
                controls = points.GetRange(1, points.Count - 2)
            };

            // The final word: the same check the course builder uses
            CourseValidator.Result check = CourseValidator.Check(map, grid, course);
            if (!check.ok)
            {
                continue;
            }
            Result result = new Result { course = course, shortestLeg = float.MaxValue, legCount = legs.Count,
                                         routeChoiceLegs = choices != null ? choices.goodLegs : -1 };
            List<CoursePoint> all = course.AllPoints();
            for (int i = 1; i < all.Count; i++)
            {
                float leg = Distance(all[i - 1], all[i]);
                result.shortestLeg = Mathf.Min(result.shortestLeg, leg);
                result.longestLeg = Mathf.Max(result.longestLeg, leg);
            }
            if (choices == null) return result;
            // Sprint: keep the course with the most fun legs; try again while too few are fun and there is time
            if (best == null || result.routeChoiceLegs > best.routeChoiceLegs) best = result;
            if (best.routeChoiceLegs >= Mathf.CeilToInt(GoodShare * best.legCount) || choices.OutOfTime) return WithBarriers(map, grid, best, settings);
        }
        if (best != null) return WithBarriers(map, grid, best, settings);
        if (settings.HasRegion) return new Result { error = "Could not fit a " + lengthText + " course with " + controls + " controls in the area you drew. Draw a bigger area, or choose a shorter course or fewer controls." };
        return new Result { error = "Could not make a course with these settings. Try a shorter course or fewer controls." };
    }

    const double BarrierBudgetSeconds = 15.0;
    // Same rule as the route choice game: a variant of a faster choice must be close in time to count
    const float VariantSlower = RouteChoiceGenerator.Variants;

    // Sprint with barriers ticked: closes the obvious straight way on legs with one clear route (SprintBarriers),
    // so they get a choice left or right. The course is kept without barriers when they would break it.
    static Result WithBarriers(MapDefinition map, MapGrid grid, Result result, Settings settings)
    {
        if (!settings.barriers || settings.kind != Kind.Sprint || result?.course == null) return result;
        try
        {
            float cs = map.CellSize;
            int rows = grid.Rows, cols = grid.Cols;
            float[,] work = (float[,])grid.speed.Clone();
            RouteChoiceGenerator generator = new RouteChoiceGenerator(work, grid.slopes, cs, 1f, 20f, true) { VariantSlower = VariantSlower };
            List<Vector2Int> cells = new List<Vector2Int>();
            foreach (CoursePoint p in result.course.AllPoints()) cells.Add(map.ToCell(p));
            // Grid coordinates (cell j spans j to j + 1) to the map's whole metres and back, as the corners are saved
            CoursePoint ToPoint(Vector2 g)
            {
                CoursePoint p = map.FromWorld(new Vector3(g.x * cs - cols * cs / 2f, g.y * cs - rows * cs / 2f, 0f));
                return new CoursePoint(Math.Round(p.e), Math.Round(p.n));
            }
            Vector2 ToGrid(CoursePoint p)
            {
                Vector3 w = map.ToWorld(p);
                return new Vector2(w.x / cs + cols / 2f, w.y / cs + rows / 2f);
            }
            SprintBarriers planner = new SprintBarriers(work, generator, map.gridSize, CloseChoice, BarrierBudgetSeconds, g => ToGrid(ToPoint(g)));
            List<SprintBarriers.Barrier> barriers = planner.Plan(cells);
            foreach (string line in planner.Log) Debug.Log("AI course barriers: " + line);
            if (barriers.Count == 0) return result;

            CourseData course = result.course;
            course.forbidden = new List<ForbiddenArea>();
            foreach (SprintBarriers.Barrier b in barriers)
            {
                ForbiddenArea area = new ForbiddenArea();
                foreach (Vector2 corner in b.corners) area.corners.Add(ToPoint(corner));
                course.forbidden.Add(area);
            }
            // The final word, with the barriers: every control can still be reached
            if (!CourseValidator.Check(map, grid, course).ok)
            {
                course.forbidden = new List<ForbiddenArea>();
                return result;
            }
            result.barriers = barriers.Count;
            result.routeChoiceLegs = planner.FunAfter;
        }
        catch (Exception e)
        {
            Debug.LogWarning("AI course barriers failed: " + e.Message);
            if (result.course != null) result.course.forbidden = new List<ForbiddenArea>();
        }
        return result;
    }

    // Controls stay this far inside the edge of a drawn area, so their circles are inside it too
    const float RegionMarginForest = 25f;
    const float RegionMarginSprint = 8f;

    // Map coordinates to grid coordinates (cell j spans j to j + 1)
    static List<Vector2> ToGrid(MapDefinition map, List<CoursePoint> points)
    {
        float cs = map.CellSize;
        List<Vector2> grid = new List<Vector2>();
        foreach (CoursePoint p in points)
        {
            Vector3 w = map.ToWorld(p);
            grid.Add(new Vector2(w.x / cs + map.cols / 2f, w.y / cs + map.rows / 2f));
        }
        return grid;
    }

    // Leg lengths that add up to the course length
    static List<float> PlanLegs(Kind kind, float length, int legCount, System.Random random)
    {
        List<float> weights = new List<float>();
        float minLeg = kind == Kind.Sprint ? SprintMinLegMeters : MinLegMeters;
        if (kind == Kind.Middle || kind == Kind.Sprint)
        {
            for (int i = 0; i < legCount; i++) weights.Add(Range(random, 0.55f, 1.45f));
        }
        else
        {
            // About a quarter of the legs are long, the rest short; never all long
            int longLegs = Mathf.Clamp(Mathf.RoundToInt(legCount * 0.25f), 1, Mathf.Max(1, legCount - 2));
            for (int i = 0; i < legCount; i++) weights.Add(i < longLegs ? Range(random, 3f, 5f) : Range(random, 0.6f, 1.4f));
            Shuffle(weights, random);
            // Start and finish on short legs, as on a real long-distance course
            MoveShortTo(weights, 0);
            MoveShortTo(weights, legCount - 1);
        }

        // Scale to the length, keeping every leg at least the shortest leg allowed
        List<float> legs = new List<float>(weights);
        for (int round = 0; round < 6; round++)
        {
            float fixedSum = 0f, freeWeight = 0f;
            for (int i = 0; i < legCount; i++)
            {
                if (legs[i] < 0f) fixedSum += minLeg; else freeWeight += weights[i];
            }
            bool changed = false;
            for (int i = 0; i < legCount; i++)
            {
                if (legs[i] < 0f) continue;
                float leg = (length - fixedSum) * weights[i] / freeWeight;
                if (leg < minLeg) { legs[i] = -1f; changed = true; } else legs[i] = leg;
            }
            if (!changed) break;
        }
        for (int i = 0; i < legCount; i++) if (legs[i] < 0f) legs[i] = minLeg;
        return legs;
    }

    static void MoveShortTo(List<float> weights, int index)
    {
        if (weights[index] < 2f) return;
        for (int i = 0; i < weights.Count; i++)
        {
            if (weights[i] < 2f && i != 0 && i != weights.Count - 1)
            {
                (weights[i], weights[index]) = (weights[index], weights[i]);
                return;
            }
        }
    }

    // Walks the legs across the map: each control at the planned distance from the one before, in a direction
    // that stays inside the map, keeps away from earlier controls and, in the second half, turns back towards the start.
    // All distances are in metres, so the same rules work on 10 m forest grids and 2 m sprint grids.
    // On sprint each leg is also checked for route choices: of the best few places for the control, the first that
    // gives a close route choice is taken (else the best place), so most legs are fun and a few may be plain.
    static List<CoursePoint> PlacePoints(MapDefinition map, MapPlayArea area, AStarPathfinder pathfinder, List<float> legs, bool sprint,
                                         ChoiceCheck choices, System.Random random)
    {
        float startRoom = sprint ? 40f : 150f;        // the start this far inside the map edge
        float roomWanted = sprint ? 60f : 250f;       // controls further inside than this score no better
        float spacing = sprint ? SprintMinControlSpacing : MinControlSpacing;
        float maxDetour = sprint ? SprintMaxDetour : MaxDetour;
        int blockedRadius = Mathf.Max(1, Mathf.RoundToInt((sprint ? 20f : 50f) / area.CellMeters));
        float blockedWeight = sprint ? 0.5f : 1.5f;   // houses are everywhere on a sprint map

        // Start somewhere with room around it (in a small drawn area: with as much room as there is)
        CoursePoint start = default;
        bool found = false;
        for (int tries = 0; tries < 400 && !found; tries++)
        {
            Vector2Int cell = area.cells[random.Next(area.cells.Count)];
            float room = tries < 200 ? startRoom : tries < 300 ? startRoom / 3f : 0f;
            if (area.MetersToOutside(cell) < room || !OpenAround(area, cell)) continue;
            start = CellCenter(map, cell);
            found = true;
        }
        if (!found) return null;

        List<CoursePoint> points = new List<CoursePoint> { start };
        float remaining = 0f;
        foreach (float leg in legs) remaining += leg;
        double heading = random.NextDouble() * Math.PI * 2;

        for (int legIndex = 0; legIndex < legs.Count; legIndex++)
        {
            float leg = legs[legIndex];
            remaining -= leg;
            CoursePoint from = points[points.Count - 1];
            bool lastLeg = legIndex == legs.Count - 1;
            bool homeward = legIndex >= legs.Count / 2;

            List<(double score, CoursePoint point)> options = new List<(double, CoursePoint)>();
            // 40 directions at the planned length; when none of them fits (a small drawn area, or a map with little
            // open ground) up to 160 more, with the leg up to a quarter shorter or longer
            for (int k = 0; k < 200 && (k < 40 || options.Count == 0); k++)
            {
                double turn = (random.NextDouble() * 2 - 1) * Math.PI * 0.85; // not straight back
                double angle = heading + turn;
                double reach = k < 40 ? leg : leg * Range(random, 0.75f, 1.25f);
                CoursePoint p = new CoursePoint(from.e + Math.Cos(angle) * reach, from.n + Math.Sin(angle) * reach);
                Vector2Int cell = map.ToCell(p);
                // Never right at the edge of a building, plot or fence: the runner must be able to stand there and
                // the line drawn from it must start on open ground
                if (!area.IsPlayable(cell) || !OpenAround(area, cell)) continue;

                float nearest = float.MaxValue;
                for (int i = 0; i < points.Count; i++)
                {
                    // Also the finish: near the start is good, on top of it is not
                    nearest = Mathf.Min(nearest, Distance(points[i], p));
                }
                if (nearest < spacing) continue;

                // Prefer places the rest of the legs can bring the runner back from, so the finish ends up near the start
                float home = Distance(p, start);
                double score = random.NextDouble() * 0.6;
                if (!lastLeg && home > remaining + 100f) score -= 1.0;
                score += Mathf.Min(area.MetersToOutside(cell), roomWanted) / roomWanted;  // room to the map edge
                score += Mathf.Min(nearest / (2f * leg + 1f), 1f);                        // spread out
                if (homeward) score -= home / Mathf.Max(remaining + leg, 1f);             // head for home
                double turnSize = Math.Abs(turn);
                if (turnSize < 0.2 || turnSize > 2.4) score -= 0.4;                       // no straight runs or dog-legs
                score -= 0.5 * Crossings(points, from, p);                                // legs crossing earlier legs
                score -= blockedWeight * BlockedShare(area, cell, blockedRadius);         // built-up areas full of plots and houses
                options.Add((score, p));
            }
            if (options.Count == 0) return null;
            options.Sort((a, b) => b.score.CompareTo(a.score));

            // The best options whose shortest route is not a big detour (e.g. round a lake)
            bool checkChoices = choices != null && !choices.OutOfTime;
            int wanted = checkChoices ? ChoiceTriesPerLeg : 1;
            List<CoursePoint> usable = new List<CoursePoint>();
            for (int k = 0; k < Math.Min(checkChoices ? 16 : 4, options.Count) && usable.Count < wanted; k++)
            {
                CoursePoint p = options[k].point;
                List<List<Vector2Int>> paths = pathfinder.FindKShortestPaths(map.ToCell(from), map.ToCell(p), 1);
                pathfinder.ResetGrid();
                if (paths.Count == 0) continue;
                float route = 0f;
                List<Vector2Int> path = paths[0];
                for (int i = 1; i < path.Count; i++) route += Vector2Int.Distance(path[i - 1], path[i]) * map.gridSize;
                if (route > leg * maxDetour + 2f * map.gridSize) continue;
                usable.Add(new CoursePoint(Math.Round(p.e), Math.Round(p.n))); // whole metres, like saved courses
            }
            if (usable.Count == 0) return null;

            CoursePoint chosen = usable[0];
            if (checkChoices)
            {
                // The first place (best score first) with a close route choice; else the place whose choices are closest
                float bestRatio = float.MaxValue;
                foreach (CoursePoint p in usable)
                {
                    if (choices.OutOfTime) break;
                    float ratio = ChoiceRatio(choices.generator, map.ToCell(from), map.ToCell(p), legIndex);
                    if (ratio < bestRatio) { bestRatio = ratio; chosen = p; }
                    if (ratio <= CloseChoice) break;
                }
                if (bestRatio <= CloseChoice) choices.goodLegs++;
            }
            heading = Math.Atan2(chosen.n - from.n, chosen.e - from.e);
            points.Add(chosen);
        }
        return points;
    }

    // Every cell within about 3 m (at least the 8 cells round it) can be run on, so a control is never put on a
    // thin strip of open ground beside a house, a plot or a fence
    static bool OpenAround(MapPlayArea area, Vector2Int cell)
    {
        int r = Mathf.Max(1, Mathf.RoundToInt(3f / area.CellMeters));
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (!area.IsPlayable(new Vector2Int(cell.x + dx, cell.y + dy))) return false;
        return true;
    }

    // How much slower the second fastest route choice is than the fastest (1.05 = 5 % slower).
    // Max value when the leg has fewer than two clear route choices.
    static float ChoiceRatio(RouteChoiceGenerator generator, Vector2Int from, Vector2Int to, int leg)
    {
        RouteChoiceLeg made;
        try
        {
            made = generator.Make(leg, from, to, leg * 7919 + from.x * 31 + to.y);
        }
        catch (Exception)
        {
            return float.MaxValue;
        }
        if (made == null || made.choices.Count < 2) return float.MaxValue;
        float fastest = float.MaxValue, second = float.MaxValue;
        foreach (RouteChoice c in made.choices)
        {
            if (c.seconds < fastest) { second = fastest; fastest = c.seconds; }
            else if (c.seconds < second) second = c.seconds;
        }
        return fastest > 0f && second < float.MaxValue ? second / fastest : float.MaxValue;
    }

    // How many earlier legs the new leg from 'from' to 'to' crosses
    static int Crossings(List<CoursePoint> points, CoursePoint from, CoursePoint to)
    {
        int count = 0;
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (SegmentsCross(points[i - 1], points[i], from, to)) count++;
        }
        return count;
    }

    static bool SegmentsCross(CoursePoint a, CoursePoint b, CoursePoint c, CoursePoint d)
    {
        double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    static double Cross(CoursePoint a, CoursePoint b, CoursePoint p)
    {
        return (b.e - a.e) * (p.n - a.n) - (b.n - a.n) * (p.e - a.e);
    }

    // Share of the cells within a radius (50 m in forest, 20 m on sprint) that can not be walked on.
    // High in villages (plots, houses, fences).
    static float BlockedShare(MapPlayArea area, Vector2Int cell, int radius)
    {
        int blocked = 0, total = 0;
        int step = Mathf.Max(1, radius / 5);   // about 11 x 11 samples whatever the cell size
        for (int dy = -radius; dy <= radius; dy += step)
            for (int dx = -radius; dx <= radius; dx += step)
            {
                int x = cell.x + dx, y = cell.y + dy;
                if (x < 0 || y < 0 || x >= area.Cols || y >= area.Rows) continue;
                total++;
                if (!area.playable[y, x]) blocked++;
            }
        return total == 0 ? 1f : (float)blocked / total;
    }

    static CoursePoint CellCenter(MapDefinition map, Vector2Int cell)
    {
        float cs = map.CellSize;
        Vector3 world = new Vector3(cell.x * cs + cs / 2f - map.cols * cs / 2f, cell.y * cs + cs / 2f - map.rows * cs / 2f, 0f);
        CoursePoint p = map.FromWorld(world);
        return new CoursePoint(Math.Round(p.e), Math.Round(p.n));
    }

    static float Distance(CoursePoint a, CoursePoint b)
    {
        double de = a.e - b.e, dn = a.n - b.n;
        return (float)Math.Sqrt(de * de + dn * dn);
    }

    static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
