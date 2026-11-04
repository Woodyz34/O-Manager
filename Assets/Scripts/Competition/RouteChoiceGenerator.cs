using System.Collections.Generic;
using UnityEngine;

// One route choice on a leg: the cells of the route and what it costs the speed-100 runner
public class RouteChoice
{
    public List<Vector2Int> cells;
    public float seconds;   // race time (game seconds) for the speed-100 runner
    public float meters;    // length of the route
    public int colour;      // index into RouteChoiceGame.Colours
}

// The route choices of one leg (index 1 = start to control 1), in the order they are shown: from left to right
// as seen from the control you leave
public class RouteChoiceLeg
{
    public int leg;
    public List<RouteChoice> choices = new List<RouteChoice>();
    public string skipped;  // why this leg has no question, or null

    public int Fastest
    {
        get
        {
            int best = 0;
            for (int i = 1; i < choices.Count; i++) if (choices[i].seconds < choices[best].seconds) best = i;
            return best;
        }
    }
}

// Finds 2 to 4 clearly different routes on a leg for the route choice game. Plain C# with no Unity objects, so it
// runs on a background thread.
//
// How: the fastest route is found with A*. Then the ground along and around the routes found so far is made much
// slower (a "penalty corridor") and A* is asked again, which pushes the next route to the other side of a house,
// hill or lake. Each such route is then straightened out: its point farthest from the fastest route is kept, and the
// fastest way through that point is used, so the alternatives are routes a runner would really take and not a
// wiggle round the corridor. All routes are timed with the speed-100 runner, including slope and the speed lost
// in turns (as AgentMovement runs). Routes that mostly run in the same place, or take nearly the same time as one
// already picked, are left out, so the choices are spread out and worth thinking about.
public class RouteChoiceGenerator
{
    const int Iterations = 7;              // penalty rounds per leg
    const float PenaltyShare = 0.35f;      // ground speed kept inside a corridor, per round
    const float MinGapSeconds = 4f;        // two choices must differ at least this much (race seconds)...
    const float MinGapShare = 0.03f;       // ...and at least this share of the fastest time
    const float MaxSlower = 1.45f;         // choices slower than this x the fastest are too easy to rule out
    const float MaxSameShare = 0.5f;       // at most half of a route's middle may run next to another route
    const float TurnAcceleration = 2f;     // as AgentMovement.TurnAcceleration (world units per second²)
    const float TurnWindowMeters = 20f;    // as AgentMovement.TurnWindowMeters

    static readonly int[] DirX = { 0, 1, 1, 1, 0, -1, -1, -1 };
    static readonly int[] DirY = { 1, 1, 0, -1, -1, -1, 0, 1 };
    static readonly string[] DirNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    readonly float[,] speed;
    readonly float[][,] slopes = new float[8][,];
    float leastCost = 1f;              // the lowest cost of one cell step in the window, for the A* estimate
    readonly float cellSize;     // world units
    readonly float cellMeters;
    readonly float baseSpeed;    // world units per real second for the speed-100 runner
    readonly float gameSecondsPerSecond;
    readonly int width, height;

    public RouteChoiceGenerator(float[,] speedGrid, Dictionary<string, float[,]> slopeFactors, float cellSize,
                                float baseSpeed, float gameSecondsPerSecond)
    {
        speed = speedGrid;
        height = speedGrid.GetLength(0);
        width = speedGrid.GetLength(1);
        if (slopeFactors != null)
        {
            for (int d = 0; d < 8; d++) slopeFactors.TryGetValue(DirNames[d], out slopes[d]);
        }
        this.cellSize = cellSize;
        cellMeters = cellSize / MapDefinition.WorldUnitsPerMeter;
        this.baseSpeed = baseSpeed;
        this.gameSecondsPerSecond = gameSecondsPerSecond;
    }

    // A copy of a part of the grid
    RouteChoiceGenerator(RouteChoiceGenerator full, int x0, int y0, int w, int h)
    {
        width = w;
        height = h;
        speed = Cut(full.speed, x0, y0, w, h);
        for (int d = 0; d < 8; d++)
        {
            if (full.slopes[d] == null) continue;
            slopes[d] = Cut(full.slopes[d], x0, y0, w, h);
        }
        // The fastest ground and the best slope in the window: the estimate in Path must never be more than the
        // real cost, or A* can open the same cells again and again (sprint ground goes up to 1.5)
        float fastest = 0.05f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float best = 0f;
                for (int d = 0; d < 8; d++) best = Mathf.Max(best, slopes[d] != null ? slopes[d][y, x] : 1f);
                fastest = Mathf.Max(fastest, speed[y, x] * best);
            }
        }
        leastCost = 1f / fastest;
        gScore = new float[w * h];
        cameFrom = new int[w * h];
        closed = new int[w * h];
        cellSize = full.cellSize;
        cellMeters = full.cellMeters;
        baseSpeed = full.baseSpeed;
        gameSecondsPerSecond = full.gameSecondsPerSecond;
    }

    static float[,] Cut(float[,] grid, int x0, int y0, int w, int h)
    {
        float[,] part = new float[h, w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) part[y, x] = grid[y0 + y, x0 + x];
        }
        return part;
    }

    // The route choices of a leg. Only a window round the leg is searched (the straight distance to each side, at
    // least 80 m): a route that goes further out is never a real choice, and a search of the whole map would take
    // far too long on a sprint map with 2 m cells.
    public RouteChoiceLeg Make(int leg, Vector2Int from, Vector2Int to, int seed)
    {
        float straightMeters = Vector2.Distance(from, to) * cellMeters;
        if (straightMeters < 30f)
        {
            return new RouteChoiceLeg { leg = leg, skipped = "too short for a route choice" };
        }
        int margin = Mathf.CeilToInt(Mathf.Max(0.6f * straightMeters, 80f) / cellMeters);
        int x0 = Mathf.Max(0, Mathf.Min(from.x, to.x) - margin);
        int y0 = Mathf.Max(0, Mathf.Min(from.y, to.y) - margin);
        int x1 = Mathf.Min(width - 1, Mathf.Max(from.x, to.x) + margin);
        int y1 = Mathf.Min(height - 1, Mathf.Max(from.y, to.y) + margin);
        Vector2Int offset = new Vector2Int(x0, y0);
        RouteChoiceGenerator window = new RouteChoiceGenerator(this, x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        RouteChoiceLeg result = window.MakeHere(leg, from - offset, to - offset, seed, straightMeters);
        foreach (RouteChoice choice in result.choices)
        {
            for (int i = 0; i < choice.cells.Count; i++) choice.cells[i] += offset;
        }
        return result;
    }

    RouteChoiceLeg MakeHere(int leg, Vector2Int from, Vector2Int to, int seed, float straightMeters)
    {
        RouteChoiceLeg result = new RouteChoiceLeg { leg = leg };
        List<Vector2Int> best = Path(from, to);
        if (best == null || best.Count < 2)
        {
            result.skipped = "no way through";
            return result;
        }

        // The corridor round a route, and how close two routes may run and still count as different
        float corridorMeters = Mathf.Clamp(straightMeters * 0.07f, 2.5f * cellMeters, 60f);
        int corridor = Mathf.Max(1, Mathf.RoundToInt(corridorMeters / cellMeters));
        float sameMeters = Mathf.Max(1.5f * cellMeters, straightMeters * 0.035f);
        float endMeters = Mathf.Max(corridorMeters, 3f * cellMeters); // near the controls all routes may meet
        int endCells = Mathf.CeilToInt(endMeters / cellMeters);

        List<RouteChoice> candidates = new List<RouteChoice> { Evaluate(best) };
        float[,] work = (float[,])speed.Clone();
        int[] stamp = new int[width * height];
        List<Vector2Int> last = best;
        for (int round = 1; round <= Iterations; round++)
        {
            Penalise(work, stamp, round, last, corridor, from, to, endCells);
            List<Vector2Int> pushed = Path(from, to, work);
            if (pushed == null) break;
            last = pushed;
            candidates.Add(Evaluate(pushed));
            Vector2Int via = FarthestFrom(pushed, best);
            List<Vector2Int> natural = Through(from, via, to);
            if (natural != null) candidates.Add(Evaluate(natural));
        }

        // Fastest first; take every route that is clearly different from the ones already taken
        candidates.Sort((a, b) => a.seconds.CompareTo(b.seconds));
        float fastest = candidates[0].seconds;
        float gap = Mathf.Max(MinGapSeconds, fastest * MinGapShare);
        int most = straightMeters < 80f ? 2 : straightMeters < 200f ? 3 : 4;
        float sameCells = sameMeters / cellMeters;
        List<RouteChoice> picked = new List<RouteChoice>();
        foreach (RouteChoice c in candidates)
        {
            if (c.seconds > fastest * MaxSlower + 8f) break;
            bool ok = true;
            foreach (RouteChoice p in picked)
            {
                if (Mathf.Abs(c.seconds - p.seconds) < gap || !Different(c.cells, p.cells, from, to, endCells, sameCells))
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;
            picked.Add(c);
            if (picked.Count >= most) break;
        }
        if (picked.Count < 2)
        {
            result.skipped = "no clear route choice";
            return result;
        }

        // Two choices are red and blue, three add green, four add yellow. Which route gets which colour is shuffled
        // (the same way for every player, from the seed), and the buttons are in colour order.
        Vector2 along = ((Vector2)(to - from)).normalized;
        picked.Sort((a, b) => Side(a.cells, from, along).CompareTo(Side(b.cells, from, along)));
        System.Random rng = new System.Random(seed);
        List<int> colours = new List<int>();
        for (int i = 0; i < picked.Count; i++) colours.Add(i);
        for (int i = colours.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int t = colours[i]; colours[i] = colours[j]; colours[j] = t;
        }
        for (int i = 0; i < picked.Count; i++) picked[i].colour = colours[i];
        picked.Sort((a, b) => a.colour.CompareTo(b.colour));
        result.choices = picked;
        return result;
    }

    // ---- Building the alternatives ----

    // Makes the ground round a route slower, except close to the controls (every route has to leave and reach them)
    void Penalise(float[,] work, int[] stamp, int round, List<Vector2Int> route, int radius, Vector2Int from, Vector2Int to, int endCells)
    {
        int r2 = radius * radius;
        int e2 = endCells * endCells;
        foreach (Vector2Int c in route)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = c.y + dy;
                if (y < 0 || y >= height) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int x = c.x + dx;
                    if (x < 0 || x >= width) continue;
                    int index = y * width + x;
                    if (stamp[index] == round) continue;
                    stamp[index] = round;
                    int fx = x - from.x, fy = y - from.y, tx = x - to.x, ty = y - to.y;
                    if (fx * fx + fy * fy <= e2 || tx * tx + ty * ty <= e2) continue;
                    work[y, x] *= PenaltyShare;
                }
            }
        }
    }

    // The point of a route farthest from another route
    static Vector2Int FarthestFrom(List<Vector2Int> route, List<Vector2Int> other)
    {
        Vector2Int farthest = route[route.Count / 2];
        int worst = -1;
        for (int i = 0; i < route.Count; i++)
        {
            int d = NearestSquared(route[i], other, int.MaxValue);
            if (d > worst) { worst = d; farthest = route[i]; }
        }
        return farthest;
    }

    // The fastest route through a point, without loops
    List<Vector2Int> Through(Vector2Int from, Vector2Int via, Vector2Int to)
    {
        if (via == from || via == to) return null;
        List<Vector2Int> first = Path(from, via);
        List<Vector2Int> second = Path(via, to);
        if (first == null || second == null) return null;
        List<Vector2Int> joined = new List<Vector2Int>(first.Count + second.Count);
        joined.AddRange(first);
        for (int i = 1; i < second.Count; i++) joined.Add(second[i]);
        return RemoveLoops(joined);
    }

    static List<Vector2Int> RemoveLoops(List<Vector2Int> route)
    {
        List<Vector2Int> result = new List<Vector2Int>(route.Count);
        Dictionary<Vector2Int, int> seen = new Dictionary<Vector2Int, int>();
        foreach (Vector2Int c in route)
        {
            if (seen.TryGetValue(c, out int at))
            {
                for (int i = at + 1; i < result.Count; i++) seen.Remove(result[i]);
                result.RemoveRange(at + 1, result.Count - at - 1);
                continue;
            }
            seen[c] = result.Count;
            result.Add(c);
        }
        return result;
    }

    // True when the middle of each route (away from the controls) mostly runs somewhere else than the other one
    static bool Different(List<Vector2Int> a, List<Vector2Int> b, Vector2Int from, Vector2Int to, int endCells, float sameCells)
    {
        return SameShare(a, b, from, to, endCells, sameCells) < MaxSameShare
            && SameShare(b, a, from, to, endCells, sameCells) < MaxSameShare;
    }

    static float SameShare(List<Vector2Int> a, List<Vector2Int> b, Vector2Int from, Vector2Int to, int endCells, float sameCells)
    {
        int e2 = endCells * endCells;
        int s2 = Mathf.CeilToInt(sameCells * sameCells);
        // The cells of b in buckets as big as "the same place", so each cell of a only looks at the buckets round it
        int bucket = Mathf.Max(1, Mathf.CeilToInt(sameCells));
        Dictionary<long, List<Vector2Int>> buckets = new Dictionary<long, List<Vector2Int>>();
        foreach (Vector2Int c in b)
        {
            long key = Key(Floor(c.x, bucket), Floor(c.y, bucket));
            if (!buckets.TryGetValue(key, out List<Vector2Int> list)) buckets[key] = list = new List<Vector2Int>();
            list.Add(c);
        }
        int middle = 0, same = 0;
        foreach (Vector2Int c in a)
        {
            if ((c - from).sqrMagnitude <= e2 || (c - to).sqrMagnitude <= e2) continue;
            middle++;
            int bx = Floor(c.x, bucket), by = Floor(c.y, bucket);
            bool near = false;
            for (int dy = -1; dy <= 1 && !near; dy++)
            {
                for (int dx = -1; dx <= 1 && !near; dx++)
                {
                    if (!buckets.TryGetValue(Key(bx + dx, by + dy), out List<Vector2Int> list)) continue;
                    foreach (Vector2Int o in list)
                    {
                        if ((o - c).sqrMagnitude <= s2) { near = true; break; }
                    }
                }
            }
            if (near) same++;
        }
        return middle == 0 ? 1f : (float)same / middle;
    }

    static int Floor(int v, int size) => v >= 0 ? v / size : (v - size + 1) / size;
    static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

    // Squared distance from a cell to the nearest cell of a route; stops early once within "enough"
    static int NearestSquared(Vector2Int c, List<Vector2Int> route, int enough)
    {
        int best = int.MaxValue;
        foreach (Vector2Int r in route)
        {
            int dx = r.x - c.x, dy = r.y - c.y;
            int d = dx * dx + dy * dy;
            if (d < best)
            {
                best = d;
                if (best <= enough && enough != int.MaxValue) return best;
            }
        }
        return best;
    }

    // Average sideways position of a route (negative = left of the straight line)
    static float Side(List<Vector2Int> route, Vector2Int from, Vector2 along)
    {
        float sum = 0f;
        foreach (Vector2Int c in route)
        {
            Vector2 v = c - from;
            sum += along.x * v.y - along.y * v.x; // > 0 = left
        }
        return -sum / Mathf.Max(1, route.Count);
    }

    // ---- A* ----

    float[] gScore;
    int[] cameFrom;
    int[] closed;          // the search number a cell was finished in
    int search;
    float[] heapKeys = new float[4096];
    int[] heapItems = new int[4096];
    int heapCount;

    // The fastest path on the window's own grid, or on a changed copy of it (the penalty corridors). The same cost as
    // AStarPathfinder (distance / ground speed / slope), with an estimate that never overshoots.
    List<Vector2Int> Path(Vector2Int start, Vector2Int goal, float[,] grid = null)
    {
        grid = grid ?? speed;
        if (start.x < 0 || start.y < 0 || start.x >= width || start.y >= height) return null;
        if (goal.x < 0 || goal.y < 0 || goal.x >= width || goal.y >= height) return null;
        search++;
        int startIndex = start.y * width + start.x;
        int goalIndex = goal.y * width + goal.x;
        heapCount = 0;
        gScore[startIndex] = 0f;
        cameFrom[startIndex] = -1;
        closed[startIndex] = -search; // negative: seen in this search, not finished
        Push(startIndex, Estimate(start.x, start.y, goal));
        while (heapCount > 0)
        {
            int current = Pop();
            if (closed[current] == search) continue;
            closed[current] = search;
            if (current == goalIndex)
            {
                List<Vector2Int> path = new List<Vector2Int>();
                for (int c = current; c >= 0; c = cameFrom[c]) path.Add(new Vector2Int(c % width, c / width));
                path.Reverse();
                return path;
            }
            int cx = current % width, cy = current / width;
            float g = gScore[current];
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + DirX[d], ny = cy + DirY[d];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                float cellSpeed = grid[ny, nx];
                if (cellSpeed <= 0.01f) continue;
                int next = ny * width + nx;
                if (closed[next] == search) continue;
                float slope = slopes[d] != null ? slopes[d][cy, cx] : 1f;
                if (slope <= 0f) continue;
                float cost = g + ((DirX[d] != 0 && DirY[d] != 0) ? 1.414f : 1f) / cellSpeed / slope;
                if (closed[next] == -search && cost >= gScore[next]) continue;
                closed[next] = -search;
                gScore[next] = cost;
                cameFrom[next] = current;
                Push(next, cost + Estimate(nx, ny, goal));
            }
        }
        return null;
    }

    // Octile distance at the fastest speed in the window
    float Estimate(int x, int y, Vector2Int goal)
    {
        int dx = Mathf.Abs(x - goal.x), dy = Mathf.Abs(y - goal.y);
        int small = Mathf.Min(dx, dy), big = Mathf.Max(dx, dy);
        return (big - small + 1.414f * small) * leastCost;
    }

    void Push(int item, float key)
    {
        if (heapCount == heapKeys.Length)
        {
            System.Array.Resize(ref heapKeys, heapCount * 2);
            System.Array.Resize(ref heapItems, heapCount * 2);
        }
        int i = heapCount++;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (heapKeys[parent] <= key) break;
            heapKeys[i] = heapKeys[parent];
            heapItems[i] = heapItems[parent];
            i = parent;
        }
        heapKeys[i] = key;
        heapItems[i] = item;
    }

    int Pop()
    {
        int result = heapItems[0];
        heapCount--;
        float key = heapKeys[heapCount];
        int item = heapItems[heapCount];
        int i = 0;
        while (true)
        {
            int child = 2 * i + 1;
            if (child >= heapCount) break;
            if (child + 1 < heapCount && heapKeys[child + 1] < heapKeys[child]) child++;
            if (key <= heapKeys[child]) break;
            heapKeys[i] = heapKeys[child];
            heapItems[i] = heapItems[child];
            i = child;
        }
        heapKeys[i] = key;
        heapItems[i] = item;
        return result;
    }

    // ---- Timing ----

    public RouteChoice Evaluate(List<Vector2Int> route)
    {
        return new RouteChoice { cells = route, seconds = Seconds(route), meters = Meters(route) };
    }

    float Meters(List<Vector2Int> route)
    {
        float m = 0f;
        for (int i = 0; i < route.Count - 1; i++) m += Vector2.Distance(route[i], route[i + 1]);
        return m * cellMeters;
    }

    // The race time of the speed-100 runner on a route: distance / (base speed x ground x slope), step by step,
    // plus the time lost in each turn (AgentMovement slows down to TurnSpeedShare at the corner and then
    // accelerates again at TurnAcceleration: that costs v/(2a) x (1 - share)² seconds)
    public float Seconds(List<Vector2Int> route)
    {
        if (route == null || route.Count < 2 || baseSpeed <= 0f) return 0f;
        int n = route.Count;
        float[] v = new float[n];
        float real = 0f;
        for (int i = 0; i < n - 1; i++)
        {
            Vector2Int a = route[i], b = route[i + 1];
            int d = Direction(a, b);
            float cellSpeed = speed[b.y, b.x];
            if (cellSpeed <= 0.01f) cellSpeed = 0.05f;
            float slope = slopes[d] != null ? Mathf.Max(0.05f, slopes[d][a.y, a.x]) : 1f;
            float worldSpeed = baseSpeed * cellSpeed * slope;
            float distance = (a.x != b.x && a.y != b.y ? 1.414f : 1f) * cellSize;
            real += distance / worldSpeed;
            v[i] = worldSpeed;
        }
        v[n - 1] = v[Mathf.Max(0, n - 2)];

        // The turns, measured as AgentMovement.ComputeTurns does
        int k = Mathf.Max(1, Mathf.RoundToInt(TurnWindowMeters / Mathf.Max(0.5f, cellMeters)));
        float[] angle = new float[n];
        for (int i = 1; i < n - 1; i++)
        {
            Vector2 into = route[i] - route[Mathf.Max(0, i - k)];
            Vector2 outOf = route[Mathf.Min(n - 1, i + k)] - route[i];
            angle[i] = into.sqrMagnitude > 0.0001f && outOf.sqrMagnitude > 0.0001f ? Vector2.Angle(into, outOf) : 0f;
        }
        for (int i = 1; i < n - 1; i++)
        {
            if (angle[i] <= 30f) continue;
            bool sharpest = true;
            for (int j = Mathf.Max(0, i - k); j <= Mathf.Min(n - 1, i + k) && sharpest; j++)
            {
                if (angle[j] > angle[i] || (angle[j] == angle[i] && j < i)) sharpest = false;
            }
            if (!sharpest) continue;
            float lost = 1f - AgentMovement.TurnSpeedShare(angle[i]);
            real += v[i] / (2f * TurnAcceleration) * lost * lost;
        }
        return real * gameSecondsPerSecond;
    }

    static int Direction(Vector2Int from, Vector2Int to)
    {
        int dx = Mathf.Clamp(to.x - from.x, -1, 1);
        int dy = Mathf.Clamp(to.y - from.y, -1, 1);
        for (int d = 0; d < 8; d++) if (DirX[d] == dx && DirY[d] == dy) return d;
        return 0;
    }
}
