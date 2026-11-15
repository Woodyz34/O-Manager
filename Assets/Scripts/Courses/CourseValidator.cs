using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

// A map's walkability grid, loaded once and kept for the course builder.
public class MapGrid
{
    public float[,] speed;
    public Dictionary<string, float[,]> slopes;

    static readonly Dictionary<string, MapGrid> cache = new Dictionary<string, MapGrid>();

    // After a map has been built again (its edge changed), so the new grid is read
    public static void Forget(string gridName)
    {
        if (gridName != null) cache.Remove(gridName);
    }

    public int Rows => speed.GetLength(0);
    public int Cols => speed.GetLength(1);

    public static MapGrid Load(string gridName)
    {
        if (cache.TryGetValue(gridName, out MapGrid cached))
        {
            return cached;
        }
        MapGrid grid = null;
        if (MapPackages.IsPackagePath(gridName))
        {
            byte[] bytes = MapPackages.LoadBytes(gridName, ".bytes");
            if (bytes == null || !GridBinary.TryRead(bytes, out GridData _, out float[,] packageSpeed, out Dictionary<string, float[,]> packageSlopes))
            {
                Debug.LogError("MapGrid: could not read grid " + gridName);
                return null;
            }
            grid = new MapGrid { speed = packageSpeed, slopes = packageSlopes };
            TerrainSpeedModel.Apply(grid.speed, grid.slopes);
            cache[gridName] = grid;
            return grid;
        }
        TextAsset binary = Resources.Load<TextAsset>("2d-gridJson/" + gridName + GridBinary.Suffix);
        if (binary != null)
        {
            if (GridBinary.TryRead(binary.bytes, out GridData _, out float[,] speedGrid, out Dictionary<string, float[,]> slopeFactors))
            {
                grid = new MapGrid { speed = speedGrid, slopes = slopeFactors };
            }
            Resources.UnloadAsset(binary);
        }
        if (grid == null)
        {
            TextAsset json = Resources.Load<TextAsset>("2d-gridJson/" + gridName);
            if (json == null)
            {
                Debug.LogError("MapGrid: no grid called " + gridName);
                return null;
            }
            GridData data = JsonConvert.DeserializeObject<GridData>(json.text);
            grid = new MapGrid { speed = ToArray(data.base_speed_grid), slopes = new Dictionary<string, float[,]>() };
            foreach (var pair in data.slope_factors)
            {
                grid.slopes[pair.Key] = ToArray(pair.Value);
            }
        }
        TerrainSpeedModel.Apply(grid.speed, grid.slopes);
        cache[gridName] = grid;
        return grid;
    }

    // The same rule as AStarPathfinder: a cell this slow can not be entered at all
    public bool IsPassable(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < Cols && cell.y >= 0 && cell.y < Rows && speed[cell.y, cell.x] > 0.01f;
    }

    static float[,] ToArray(float[][] jagged)
    {
        float[,] array = new float[jagged.Length, jagged[0].Length];
        for (int i = 0; i < jagged.Length; i++)
            for (int j = 0; j < jagged[0].Length; j++)
                array[i, j] = jagged[i][j];
        return array;
    }
}

// Checks that a runner can get from the start through every control to the finish, using the same A* the runners use.
public static class CourseValidator
{
    public class Result
    {
        public bool ok;
        public int badIndex = -1;      // individual courses: start = 0, controls 1..n, finish = n + 1
        public int badLeg = -1;        // relays: a leg whose course has the problem
        public bool hasBadPoint;       // relays: the point with the problem
        public CoursePoint badPoint;
        public string message;
        public List<float> legMeters = new List<float>(); // shortest walkable length of each leg
    }

    public static string PointName(int index, int controlCount)
    {
        if (index == 0) return "The start";
        if (index == controlCount + 1) return "The finish";
        return "Control " + index;
    }

    // Quick check for one point, used while the course is being laid
    public static string CheckPoint(MapDefinition map, MapGrid grid, CoursePoint point)
    {
        if (!map.Contains(point))
        {
            return "That is outside the map.";
        }
        if (RunCell(grid, map.ToCell(point)).x < 0)
        {
            return "Nobody can get there (lake, building or out-of-bounds area).";
        }
        return null;
    }

    // How many cells from a control the runner may punch it. A control may stand on a cliff, a fence, a boulder or
    // a lake shore (user's wish 2026-11-15): the runner punches it from the nearest cell they can run on, as in a real
    // race. The control stays where it was put; the cliff or fence is still not crossed.
    public const int PunchReachCells = 2;

    // The cell a runner punches a control from: the control's own cell, or the nearest cell that can be run on at
    // most PunchReachCells away. (-1, -1) when there is none (the middle of a lake, a building).
    public static Vector2Int RunCell(MapGrid grid, Vector2Int cell)
    {
        return RunCell(grid.speed, cell);
    }

    public static Vector2Int RunCell(float[,] speed, Vector2Int cell)
    {
        int rows = speed.GetLength(0);
        int cols = speed.GetLength(1);
        bool Open(int x, int y) => x >= 0 && x < cols && y >= 0 && y < rows && speed[y, x] > 0.01f;
        if (Open(cell.x, cell.y)) return cell;
        Vector2Int best = new Vector2Int(-1, -1);
        int bestDistance = int.MaxValue;
        for (int dy = -PunchReachCells; dy <= PunchReachCells; dy++)
        {
            for (int dx = -PunchReachCells; dx <= PunchReachCells; dx++)
            {
                int distance = dx * dx + dy * dy;
                if (distance >= bestDistance || distance > PunchReachCells * PunchReachCells + 1) continue;
                if (!Open(cell.x + dx, cell.y + dy)) continue;
                bestDistance = distance;
                best = new Vector2Int(cell.x + dx, cell.y + dy);
            }
        }
        return best;
    }

    public static Result Check(MapDefinition map, MapGrid grid, CourseData course)
    {
        // The runners can not go into the course's out-of-bounds areas, and no control may be in one
        grid = ForbiddenAreas.WithAreas(map, grid, course);
        if (!course.IsRelay())
        {
            Result individual = CheckIndividual(map, grid, course);
            if (individual.ok && course.ToLobbyString().Length > CourseData.MaxLobbyChars)
            {
                individual = Fail(new Result(), -1, "The course is too big to send to the other players. Use fewer corners on the out-of-bounds areas.");
            }
            return individual;
        }

        string relayProblem = CheckRelaySettings(course, out int badLeg);
        if (relayProblem != null)
        {
            Result settings = Fail(new Result(), -1, relayProblem);
            settings.badLeg = badLeg;
            return settings;
        }

        AStarPathfinder pathfinder = new AStarPathfinder(grid.speed, grid.slopes);
        HashSet<int> checkedCourses = new HashSet<int>();
        Result total = new Result();
        for (int leg = 0; leg < course.legs; leg++)
        {
            int ci = course.LegCourseIndex(leg);
            if (!checkedCourses.Add(ci)) continue;
            Result result = CheckRelayCourse(map, grid, course, leg, pathfinder);
            if (!result.ok)
            {
                result.badLeg = leg;
                result.message = "Leg " + (leg + 1) + ": " + result.message;
                return result;
            }
        }
        total.ok = true;
        total.message = course.AnyForks()
            ? "The relay works: every control on every branch can be reached."
            : "The relay works: every control can be reached.";
        return total;
    }

    static Result CheckIndividual(MapDefinition map, MapGrid grid, CourseData course)
    {
        Result result = new Result();
        List<CoursePoint> points = course.AllPoints();
        int controls = course.controls.Count;
        if (controls == 0)
        {
            return Fail(result, -1, "Add at least one control between the start and the finish.");
        }
        for (int i = 0; i < points.Count; i++)
        {
            string problem = CheckPoint(map, grid, points[i]);
            if (problem != null)
            {
                return Fail(result, i, PointName(i, controls) + " can not be reached. " + problem);
            }
        }
        AStarPathfinder pathfinder = new AStarPathfinder(grid.speed, grid.slopes);
        for (int i = 1; i < points.Count; i++)
        {
            Vector2Int from = RunCell(grid, map.ToCell(points[i - 1]));
            Vector2Int to = RunCell(grid, map.ToCell(points[i]));
            if (from == to)
            {
                return Fail(result, i, PointName(i, controls) + " is on top of " + PointName(i - 1, controls).ToLower() + ". Move it further away.");
            }
            List<List<Vector2Int>> paths = pathfinder.FindKShortestPaths(from, to, 1);
            pathfinder.ResetGrid();
            if (paths.Count == 0)
            {
                return Fail(result, i, PointName(i, controls) + " can not be reached from " + PointName(i - 1, controls).ToLower() + ". There is no way past water or out-of-bounds areas.");
            }
            result.legMeters.Add(PathMeters(paths[0], map.gridSize));
        }
        result.ok = true;
        result.message = "The course works: every control can be reached.";
        return result;
    }

    // ---- Relays ----

    // A named point on a relay course, for the checks: "control 3", "control 5B2" (fork at 5, branch B, its 2nd control)
    struct NamedPoint
    {
        public CoursePoint point;
        public string name;
    }

    // Every way a runner can go between two points of one relay course, through every branch of every fork
    static Result CheckRelayCourse(MapDefinition map, MapGrid grid, CourseData course, int leg, AStarPathfinder pathfinder)
    {
        Result result = new Result();
        RelayLegCourse relayCourse = course.LegCourse(leg);
        List<NamedPoint> previous = new List<NamedPoint> { new NamedPoint { point = relayCourse.start, name = "the start" } };
        List<(NamedPoint from, NamedPoint to)> ways = new List<(NamedPoint, NamedPoint)>();
        List<NamedPoint> all = new List<NamedPoint>(previous);
        int number = 0;
        foreach (RelayItem item in relayCourse.items)
        {
            number++;
            if (item.fork < 0)
            {
                NamedPoint control = new NamedPoint { point = item.point, name = "control " + number };
                foreach (NamedPoint from in previous) ways.Add((from, control));
                all.Add(control);
                previous = new List<NamedPoint> { control };
                continue;
            }
            RelayFork fork = course.Fork(item.fork);
            List<NamedPoint> ends = new List<NamedPoint>();
            for (int b = 0; b < fork.branches.Count; b++)
            {
                List<NamedPoint> branchPrevious = previous;
                List<CoursePoint> controls = fork.branches[b].controls;
                for (int k = 0; k < controls.Count; k++)
                {
                    NamedPoint control = new NamedPoint { point = controls[k], name = "control " + number + CourseData.BranchLetter(b) + (k > 0 ? (k + 1).ToString() : "") };
                    foreach (NamedPoint from in branchPrevious) ways.Add((from, control));
                    all.Add(control);
                    branchPrevious = new List<NamedPoint> { control };
                }
                foreach (NamedPoint end in branchPrevious) if (!ends.Exists(e => SamePoint(e.point, end.point))) ends.Add(end);
            }
            previous = ends;
        }
        NamedPoint finish = new NamedPoint { point = relayCourse.finish, name = "the finish" };
        foreach (NamedPoint from in previous) ways.Add((from, finish));
        all.Add(finish);

        foreach (NamedPoint p in all)
        {
            string problem = CheckPoint(map, grid, p.point);
            if (problem != null)
            {
                return FailAt(result, p.point, Capital(p.name) + " can not be reached. " + problem);
            }
        }
        HashSet<long> done = new HashSet<long>();
        foreach (var way in ways)
        {
            Vector2Int from = RunCell(grid, map.ToCell(way.from.point));
            Vector2Int to = RunCell(grid, map.ToCell(way.to.point));
            if (from == to)
            {
                return FailAt(result, way.to.point, Capital(way.to.name) + " is on top of " + way.from.name + ". Move it further away.");
            }
            long key = ((long)from.x << 48) ^ ((long)from.y << 32) ^ ((long)to.x << 16) ^ to.y;
            if (!done.Add(key)) continue;
            List<List<Vector2Int>> paths = pathfinder.FindKShortestPaths(from, to, 1);
            pathfinder.ResetGrid();
            if (paths.Count == 0)
            {
                return FailAt(result, way.to.point, Capital(way.to.name) + " can not be reached from " + way.from.name + ".");
            }
        }
        result.ok = true;
        return result;
    }

    // How many branches a fork may have: with every team running every branch, one per leg (at most the relay's legs)
    public static int MaxBranches(CourseData course)
    {
        return course.forkEveryVariant ? course.legs : CourseData.MaxControls;
    }

    // Problems with a relay's legs and forks that have nothing to do with the map
    public static string CheckRelaySettings(CourseData course, out int badLeg)
    {
        badLeg = -1;
        if (course.legs < CourseData.MinLegs || course.legs > CourseData.MaxLegs)
        {
            return "A relay has " + CourseData.MinLegs + " to " + CourseData.MaxLegs + " legs.";
        }
        for (int leg = 0; leg < course.legs; leg++)
        {
            badLeg = leg;
            RelayLegCourse relayCourse = course.LegCourse(leg);
            if (relayCourse == null || relayCourse.items.Count == 0)
            {
                return "Leg " + (leg + 1) + " has no controls.";
            }
            int longest = 0;
            for (int i = 0; i < relayCourse.items.Count; i++)
            {
                RelayItem item = relayCourse.items[i];
                if (item.fork < 0)
                {
                    longest++;
                    continue;
                }
                RelayFork fork = course.Fork(item.fork);
                string name = "Fork " + (item.fork + 1);
                if (fork == null) return "Leg " + (leg + 1) + " has a fork that is missing.";
                if (i > 0 && relayCourse.items[i - 1].fork >= 0)
                {
                    return "Leg " + (leg + 1) + ": two forks follow each other. There must be a common control between them, so every team meets there.";
                }
                if (fork.branches.Count < 2)
                {
                    return name + " has only one branch. Add a branch, or remove the fork.";
                }
                if (fork.branches.TrueForAll(b => b.controls.Count == 0))
                {
                    return name + " has no controls on any branch.";
                }
                int most = 0;
                foreach (RelayBranch branch in fork.branches) most = Mathf.Max(most, branch.controls.Count);
                longest += most;
            }
            if (longest > CourseData.MaxControls)
            {
                return "Leg " + (leg + 1) + " can have at most " + CourseData.MaxControls + " controls on its longest way round.";
            }
        }
        badLeg = -1;

        for (int f = 0; f < course.relayForks.Count; f++)
        {
            List<int> forkLegs = course.LegsUsingFork(f);
            int branches = course.relayForks[f].branches.Count;
            if (forkLegs.Count == 0) continue;
            badLeg = forkLegs[0];
            if (course.forkEveryVariant && branches > forkLegs.Count)
            {
                return "Fork " + (f + 1) + " has " + branches + " branches but is only on " + forkLegs.Count + (forkLegs.Count == 1 ? " leg" : " legs")
                    + ". Every team must run every branch, so put it on " + (branches - forkLegs.Count) + " more "
                    + (branches - forkLegs.Count == 1 ? "leg" : "legs") + " (click its faded controls when laying that leg), or remove a branch.";
            }
            // The common controls around a fork must be the same on every leg that has it
            bool first = true;
            CoursePoint entry = default, exit = default;
            foreach (int leg in forkLegs)
            {
                RelayLegCourse relayCourse = course.LegCourse(leg);
                int i = relayCourse.items.FindIndex(item => item.fork == f);
                CoursePoint before = i == 0 ? relayCourse.start : relayCourse.items[i - 1].point;
                CoursePoint after = i == relayCourse.items.Count - 1 ? relayCourse.finish : relayCourse.items[i + 1].point;
                if (first)
                {
                    entry = before;
                    exit = after;
                    first = false;
                }
                else if (!SamePoint(entry, before) || !SamePoint(exit, after))
                {
                    badLeg = leg;
                    return "Fork " + (f + 1) + " must have the same common control before and after it on every leg, but leg " + (leg + 1)
                        + " differs from leg " + (forkLegs[0] + 1) + ".";
                }
            }
        }
        badLeg = -1;
        if (course.ToLobbyString().Length > CourseData.MaxLobbyChars)
        {
            return "The relay is too big to send to the other players. Use fewer legs, controls, branches or out-of-bounds corners.";
        }
        return null;
    }

    public static bool SamePoint(CoursePoint a, CoursePoint b)
    {
        return Math.Abs(a.e - b.e) < 1.5 && Math.Abs(a.n - b.n) < 1.5;
    }

    static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0]) + text.Substring(1);

    static float PathMeters(List<Vector2Int> path, float cellMeters)
    {
        float total = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            total += Vector2Int.Distance(path[i - 1], path[i]) * cellMeters;
        }
        return total;
    }

    static Result Fail(Result result, int index, string message)
    {
        result.ok = false;
        result.badIndex = index;
        result.message = message;
        return result;
    }

    static Result FailAt(Result result, CoursePoint point, string message)
    {
        result.ok = false;
        result.hasBadPoint = true;
        result.badPoint = point;
        result.message = message;
        return result;
    }
}
