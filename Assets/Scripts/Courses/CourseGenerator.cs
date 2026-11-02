using System;
using System.Collections.Generic;
using UnityEngine;

// Where on a map a generated course may go. The grid covers a rectangle around the map, and everything outside
// the drawn map (and the legend, logo and north arrow) is stored as ordinary fast ground. So the drawn map is
// found from the grid itself: every cell that is not plain 1.0 ground is a map feature, the features are grown
// together, enclosed holes are filled, and the biggest piece is the map. Controls must lie in the part of it a
// runner can reach from everywhere else.
public class MapPlayArea
{
    public int Rows { get; private set; }
    public int Cols { get; private set; }
    public bool[,] playable;          // walkable, inside the map and connected to the rest of the course area
    public int[,] distanceToOutside;  // cells to the nearest cell outside the drawn map (chessboard distance)
    public List<Vector2Int> cells = new List<Vector2Int>();

    const int CloseRadius = 6;        // 60 m: closes the gaps between map features at the map edge
    const int EdgeMargin = 5;         // 50 m: keep controls this far inside the map edge

    static readonly Dictionary<string, MapPlayArea> cache = new Dictionary<string, MapPlayArea>();
    static readonly object cacheLock = new object();

    public static MapPlayArea For(MapDefinition map, MapGrid grid)
    {
        lock (cacheLock)
        {
            if (!cache.TryGetValue(map.id, out MapPlayArea area))
            {
                area = new MapPlayArea(grid);
                cache[map.id] = area;
            }
            return area;
        }
    }

    MapPlayArea(MapGrid grid)
    {
        Rows = grid.Rows;
        Cols = grid.Cols;
        bool[,] feature = new bool[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                feature[i, j] = Mathf.Abs(grid.speed[i, j] - 1f) > 0.001f;

        // Close: grow the features, fill what they enclose, then shrink back
        int[,] toFeature = Distance(feature);
        bool[,] grown = new bool[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                grown[i, j] = toFeature[i, j] <= CloseRadius;
        bool[,] outside = FloodFromBorder(grown);
        int[,] toOutside = Distance(outside);
        bool[,] mapped = new bool[Rows, Cols];
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                mapped[i, j] = toOutside[i, j] > CloseRadius;
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
                candidate[i, j] = distanceToOutside[i, j] > EdgeMargin && grid.speed[i, j] > 0.01f;
        playable = Largest(candidate, (i, j) => true);
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++)
                if (playable[i, j]) cells.Add(new Vector2Int(j, i));
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

// Makes a course on its own from a few choices: long or middle distance, the length and the number of controls.
// Long distance mixes short legs with a few long ones; middle distance only has short to medium-short legs.
// Every course it returns has been checked with the same A* the runners use, so all controls can be reached.
public static class CourseGenerator
{
    public enum Kind { Long, Middle }

    public const float MinLengthKm = 1.5f;
    public const float MaxLengthKm = 20f;       // the longest course the game makes; still well under a second
    public const float MinMetersPerControl = 50f; // at most one control per 50 m
    public const float MiddleMaxAverageLeg = 500f;
    public const float LongMaxAverageLeg = 800f;   // fewer controls gives legs too long to fit the map and slow to place
    public const int MinControls = 3;
    const float MinLegMeters = 60f;
    const float MinControlSpacing = 60f;        // two controls never closer than this
    const float MaxDetour = 2.0f;               // a leg's shortest route at most this many times the straight line
    const int Attempts = 60;
    const double TimeBudgetSeconds = 8.0;

    public class Settings
    {
        public Kind kind = Kind.Middle;
        public float lengthKm = 4f;
        public int controls = 12;
        public int seed;
    }

    public class Result
    {
        public CourseData course;
        public string error;
        public float shortestLeg, longestLeg;
    }

    public static int MaxControlsFor(float lengthKm)
    {
        return Mathf.Clamp(Mathf.FloorToInt(lengthKm * 1000f / MinMetersPerControl), MinControls, CourseData.MaxControls);
    }

    public static int MinControlsFor(Kind kind, float lengthKm)
    {
        // Middle distance legs average at most 500 m and long distance legs at most 800 m,
        // so a longer course needs more controls
        float maxAverage = kind == Kind.Middle ? MiddleMaxAverageLeg : LongMaxAverageLeg;
        int needed = Mathf.CeilToInt(lengthKm * 1000f / maxAverage) - 1;
        return Mathf.Clamp(needed, MinControls, MaxControlsFor(lengthKm));
    }

    public static string KindName(Kind kind) => kind == Kind.Long ? "Long" : "Middle";

    // Can run off the main thread. The grid must already be loaded (MapGrid.Load uses Resources).
    public static Result Generate(MapDefinition map, MapGrid grid, Settings settings)
    {
        float length = Mathf.Clamp(settings.lengthKm, MinLengthKm, MaxLengthKm) * 1000f;
        int controls = Mathf.Clamp(settings.controls, MinControlsFor(settings.kind, settings.lengthKm), MaxControlsFor(settings.lengthKm));
        MapPlayArea area = MapPlayArea.For(map, grid);
        if (area.cells.Count == 0)
        {
            return new Result { error = "This map has no area to lay a course on." };
        }

        System.Random random = new System.Random(settings.seed);
        AStarPathfinder pathfinder = new AStarPathfinder(grid.speed, grid.slopes);
        DateTime started = DateTime.UtcNow;
        for (int attempt = 0; attempt < Attempts && (DateTime.UtcNow - started).TotalSeconds < TimeBudgetSeconds; attempt++)
        {
            List<float> legs = PlanLegs(settings.kind, length, controls + 1, random);
            List<CoursePoint> points = PlacePoints(map, area, pathfinder, legs, random);
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
            Result result = new Result { course = course, shortestLeg = float.MaxValue };
            List<CoursePoint> all = course.AllPoints();
            for (int i = 1; i < all.Count; i++)
            {
                float leg = Distance(all[i - 1], all[i]);
                result.shortestLeg = Mathf.Min(result.shortestLeg, leg);
                result.longestLeg = Mathf.Max(result.longestLeg, leg);
            }
            return result;
        }
        return new Result { error = "Could not make a course with these settings. Try a shorter course or fewer controls." };
    }

    // Leg lengths that add up to the course length
    static List<float> PlanLegs(Kind kind, float length, int legCount, System.Random random)
    {
        List<float> weights = new List<float>();
        if (kind == Kind.Middle)
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

        // Scale to the length, keeping every leg at least MinLegMeters
        List<float> legs = new List<float>(weights);
        for (int round = 0; round < 6; round++)
        {
            float fixedSum = 0f, freeWeight = 0f;
            for (int i = 0; i < legCount; i++)
            {
                if (legs[i] < 0f) fixedSum += MinLegMeters; else freeWeight += weights[i];
            }
            bool changed = false;
            for (int i = 0; i < legCount; i++)
            {
                if (legs[i] < 0f) continue;
                float leg = (length - fixedSum) * weights[i] / freeWeight;
                if (leg < MinLegMeters) { legs[i] = -1f; changed = true; } else legs[i] = leg;
            }
            if (!changed) break;
        }
        for (int i = 0; i < legCount; i++) if (legs[i] < 0f) legs[i] = MinLegMeters;
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
    static List<CoursePoint> PlacePoints(MapDefinition map, MapPlayArea area, AStarPathfinder pathfinder, List<float> legs, System.Random random)
    {
        // Start somewhere with room around it
        CoursePoint start = default;
        bool found = false;
        for (int tries = 0; tries < 200 && !found; tries++)
        {
            Vector2Int cell = area.cells[random.Next(area.cells.Count)];
            if (area.distanceToOutside[cell.y, cell.x] < 15) continue;
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
            for (int k = 0; k < 40; k++)
            {
                double turn = (random.NextDouble() * 2 - 1) * Math.PI * 0.85; // not straight back
                double angle = heading + turn;
                CoursePoint p = new CoursePoint(from.e + Math.Cos(angle) * leg, from.n + Math.Sin(angle) * leg);
                Vector2Int cell = map.ToCell(p);
                if (!area.IsPlayable(cell)) continue;

                float nearest = float.MaxValue;
                for (int i = 0; i < points.Count; i++)
                {
                    // Also the finish: near the start is good, on top of it is not
                    nearest = Mathf.Min(nearest, Distance(points[i], p));
                }
                if (nearest < MinControlSpacing) continue;

                // Prefer places the rest of the legs can bring the runner back from, so the finish ends up near the start
                float home = Distance(p, start);
                double score = random.NextDouble() * 0.6;
                if (!lastLeg && home > remaining + 100f) score -= 1.0;
                score += Mathf.Min(area.distanceToOutside[cell.y, cell.x], 25) / 25.0;   // room to the map edge
                score += Mathf.Min(nearest / (2f * leg + 1f), 1f);                        // spread out
                if (homeward) score -= home / Mathf.Max(remaining + leg, 1f);             // head for home
                double turnSize = Math.Abs(turn);
                if (turnSize < 0.2 || turnSize > 2.4) score -= 0.4;                       // no straight runs or dog-legs
                score -= 0.5 * Crossings(points, from, p);                                // legs crossing earlier legs
                score -= 1.5 * BlockedShare(area, cell);                                  // built-up areas full of plots and houses
                options.Add((score, p));
            }
            if (options.Count == 0) return null;
            options.Sort((a, b) => b.score.CompareTo(a.score));

            // Take the best option whose shortest route is not a big detour (e.g. round a lake)
            bool placed = false;
            for (int k = 0; k < Math.Min(4, options.Count) && !placed; k++)
            {
                CoursePoint p = options[k].point;
                List<List<Vector2Int>> paths = pathfinder.FindKShortestPaths(map.ToCell(from), map.ToCell(p), 1);
                pathfinder.ResetGrid();
                if (paths.Count == 0) continue;
                float route = 0f;
                List<Vector2Int> path = paths[0];
                for (int i = 1; i < path.Count; i++) route += Vector2Int.Distance(path[i - 1], path[i]) * map.gridSize;
                if (route > leg * MaxDetour + 2f * map.gridSize) continue;

                p = new CoursePoint(Math.Round(p.e), Math.Round(p.n)); // whole metres, like saved courses
                heading = Math.Atan2(p.n - from.n, p.e - from.e);
                points.Add(p);
                placed = true;
            }
            if (!placed) return null;
        }
        return points;
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

    // Share of the cells within 50 m that can not be walked on. High in villages (plots, houses, fences).
    static float BlockedShare(MapPlayArea area, Vector2Int cell)
    {
        int blocked = 0, total = 0;
        for (int dy = -5; dy <= 5; dy++)
            for (int dx = -5; dx <= 5; dx++)
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
