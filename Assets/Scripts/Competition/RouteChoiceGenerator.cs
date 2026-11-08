using System.Collections.Generic;
using UnityEngine;

// One route choice on a leg: the cells of the route and what it costs the speed-100 runner
public class RouteChoice
{
    public List<Vector2Int> cells;  // the grid cells A* found (used to tell routes apart)
    public List<Vector2> points;    // the smooth line shown and timed, in grid coordinates
    public float sharpestTurn;      // degrees; a runner never turns right round on a real route choice
    public float seconds;   // race time (game seconds) for the speed-100 runner
    public float meters;    // length of the route
    public int colour;      // index into RouteChoiceGame.Colours
    public float side;      // average distance left (-) or right (+) of the straight line, in cells
    public bool crossesBlocked; // route analysis: the line goes over ground nobody can run on (a building, a fence ...)
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
// How: first the fastest route. Then parts of it are closed off in turn (a fifth of it at a time, along the whole leg)
// and the fastest way round is searched again; that finds the other side of a block, the next street, the path through
// the park. The same is done with the first choices found. Besides that, the fastest time from the start to every
// point of the map round the leg, and from every point to the goal, is worked out once (Dijkstra both ways). The fastest route through any point is then known at once. Points are
// taken cheapest first, skipping points close to a route already taken, so every route found runs somewhere new:
// left, right, straight on, along a road or across the open. Each is the real fastest way through its point,
// on roads and paths before and after. All routes are timed with the speed-100 runner, including slope and the speed lost
// in turns (as AgentMovement runs). Routes that mostly run in the same place, or take nearly the same time as one
// already picked, are left out, so the choices are spread out and worth thinking about.
public class RouteChoiceGenerator
{
    const int MaxCandidates = 20;          // different routes timed per leg before picking
    const int MaxTries = 600;              // points tried per leg
    const float MinGapSeconds = 2f;        // two choices must differ at least this much (race seconds)...
    const float MinGapShare = 0.02f;       // ...and at least this share of the fastest time
    // Choices slower than this x the fastest are too easy to rule out. In the forest a slow choice is usually a
    // long way round that nobody would take, so there the cap is tighter (two good choices are enough)
    const float MaxSlowerSprint = 1.2f;
    const float MaxSlowerForest = 1.07f;
    const float ViaReach = 0.95f;          // a route may go its own way anywhere from 5 % to 95 % along the leg
    const float MaxTurn = 135f;            // routes with a sharper turn than this (out and back) are left out
    // Two routes may share most of the leg (everyone runs the same street at first), but somewhere they must part
    // properly: a stretch at least DivergeShare of the leg long where they are at least SpreadShare of the leg apart
    const float DivergeShare = 0.15f;
    const float SpreadShare = 0.10f;
    const float MinDivergeMeters = 50f;
    const float MinSpreadMeters = 35f;
    const float MinSameMeters = 8f;        // closer than this to another route counts as running in the same place
    const float LocalShare = 0.15f;        // round its point, a route must be the fastest way over this share of the leg's time
    const float LocalSlack = 1.05f;        // (5 % slower still counts as the fastest way)
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
    readonly float MaxSlower;
    // How fast ground nobody can run on counts when a line goes over it anyway (only drawn lines in route analysis do)
    public float BlockedSpeed { get; set; } = 0.05f;
    // When above 0: a choice that runs along a faster choice for a good part of the way (VariantShare) is only a
    // variant of it (both go the same side), so it may be at most this much slower than that one to be shown at all.
    // Choices that really go different ways keep the wide MaxSlower limit. The AI course uses it for its barriers.
    public float VariantSlower { get; set; } = 0f;
    const float VariantShare = 0.3f;

    public RouteChoiceGenerator(float[,] speedGrid, Dictionary<string, float[,]> slopeFactors, float cellSize,
                                float baseSpeed, float gameSecondsPerSecond, bool sprint)
    {
        MaxSlower = sprint ? MaxSlowerSprint : MaxSlowerForest;
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
        MaxSlower = full.MaxSlower;
        BlockedSpeed = full.BlockedSpeed;
        VariantSlower = full.VariantSlower;
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
            for (int i = 0; i < choice.points.Count; i++) choice.points[i] += (Vector2)offset;
        }
        return result;
    }

    // Route analysis: the computer's fastest route on a leg, as the fastest choice in the route choice game (the same
    // search in a window round the leg, made smooth and timed the same way). Null when there is no way through.
    public RouteChoice Fastest(Vector2Int from, Vector2Int to)
    {
        float straightMeters = Vector2.Distance(from, to) * cellMeters;
        int margin = Mathf.CeilToInt(Mathf.Max(0.6f * straightMeters, 80f) / cellMeters);
        int x0 = Mathf.Max(0, Mathf.Min(from.x, to.x) - margin);
        int y0 = Mathf.Max(0, Mathf.Min(from.y, to.y) - margin);
        int x1 = Mathf.Min(width - 1, Mathf.Max(from.x, to.x) + margin);
        int y1 = Mathf.Min(height - 1, Mathf.Max(from.y, to.y) + margin);
        Vector2Int offset = new Vector2Int(x0, y0);
        RouteChoiceGenerator window = new RouteChoiceGenerator(this, x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        List<Vector2Int> best = window.Path(from - offset, to - offset);
        if (best == null || best.Count < 2) return null;
        for (int i = 0; i < best.Count; i++) best[i] += offset;
        return Timed(best);
    }

    // Route analysis: a route the player drew (already made into grid cells) as a smooth line, timed. Unlike
    // Evaluate, the ends are left as drawn.
    public RouteChoice Timed(List<Vector2Int> route)
    {
        if (route == null || route.Count == 0) return null;
        List<Vector2> line = Smooth(route);
        float seconds = Seconds(line, out float sharpest);
        bool crosses = false;
        for (int i = 0; i < line.Count - 1 && !crosses; i++)
        {
            LineCost(line[i], line[i + 1], out bool blocked);
            crosses = blocked;
        }
        return new RouteChoice { cells = route, points = line, seconds = seconds, meters = Meters(line), sharpestTurn = sharpest, crossesBlocked = crosses };
    }

    // Route analysis: the fastest way from one control to the next that keeps within radius cells of a line (grid
    // coordinates), e.g. a route a player drew. The plain search finds the fastest way from cell to cell, but once
    // made smooth and timed with the turns, a way close to a player's line (say through a narrow passage) can come out
    // faster. Searching again along each player's line lets the computer take that way too, so its route is never
    // slower than a route someone drew on open ground. Null when there is no way along the line.
    public RouteChoice FastestNear(Vector2Int from, Vector2Int to, List<Vector2> line, int radius)
    {
        if (line == null || line.Count < 2) return null;
        float minX = Mathf.Min(from.x, to.x), maxX = Mathf.Max(from.x, to.x);
        float minY = Mathf.Min(from.y, to.y), maxY = Mathf.Max(from.y, to.y);
        foreach (Vector2 p in line)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        int x0 = Mathf.Max(0, Mathf.FloorToInt(minX) - radius - 2);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(minY) - radius - 2);
        int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(maxX) + radius + 2);
        int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(maxY) + radius + 2);
        if (x1 <= x0 || y1 <= y0) return null;
        Vector2Int offset = new Vector2Int(x0, y0);
        RouteChoiceGenerator window = new RouteChoiceGenerator(this, x0, y0, x1 - x0 + 1, y1 - y0 + 1);

        // Only the cells close to the line (and round the controls) are open
        float[,] near = new float[window.height, window.width];
        void Open(Vector2 p)
        {
            int cx = Mathf.RoundToInt(p.x) - x0, cy = Mathf.RoundToInt(p.y) - y0;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && y >= 0 && x < window.width && y < window.height) near[y, x] = window.speed[y, x];
                }
            }
        }
        Open(from);
        Open(to);
        for (int i = 0; i < line.Count - 1; i++)
        {
            Vector2 ab = line[i + 1] - line[i];
            int steps = Mathf.Max(1, Mathf.CeilToInt(ab.magnitude * 2f));
            for (int s = 0; s <= steps; s++) Open(line[i] + ab * ((float)s / steps));
        }
        List<Vector2Int> path = window.Path(from - offset, to - offset, near);
        if (path == null || path.Count < 2) return null;
        for (int i = 0; i < path.Count; i++) path[i] += offset;
        return Timed(path);
    }

    RouteChoiceLeg MakeHere(int leg, Vector2Int from, Vector2Int to, int seed, float straightMeters)
    {
        RouteChoiceLeg result = new RouteChoiceLeg { leg = leg };
        bucketed.Clear();
        List<Vector2Int> best = Path(from, to);
        if (best == null || best.Count < 2)
        {
            result.skipped = "no way through";
            return result;
        }

        // The corridor round a route, and how close two routes may run and still count as different
        // (and never closer than MinSpreadMeters: two ways a few metres apart, say both sides of a street, are the same choice)
        divergeCells = Mathf.Max(4f, Mathf.Max(straightMeters * DivergeShare, MinDivergeMeters) / cellMeters);
        spreadCells = Mathf.Max(3f, Mathf.Max(straightMeters * SpreadShare, MinSpreadMeters) / cellMeters);
        // (a little narrower than the spread, so a point on the other side of a house is still tried)
        float corridorMeters = Mathf.Min(Mathf.Clamp(straightMeters * 0.07f, 2.5f * cellMeters, 60f), Mathf.Max(1.5f * cellMeters, spreadCells * cellMeters * 0.8f));
        int corridor = Mathf.Max(1, Mathf.RoundToInt(corridorMeters / cellMeters));
        float sameMeters = Mathf.Max(Mathf.Max(1.5f * cellMeters, straightMeters * 0.035f), MinSameMeters);
        float endMeters = Mathf.Max(corridorMeters, 3f * cellMeters); // near the controls all routes may meet
        int endCells = Mathf.CeilToInt(endMeters / cellMeters);

        // Every point in the window, with the fastest time from the start to it and from it to the goal: the
        // fastest route through any point is then known at once. Points are taken cheapest first; a point next to a
        // route already taken adds nothing new, so each new one gives a route that is somewhere else. Together
        // that is every sensible way: left, right, straight on, along a road or across the open.
        int cellCount = width * height;
        float[] fromStart = new float[cellCount], toGoal = new float[cellCount];
        int[] backToStart = new int[cellCount], onToGoal = new int[cellCount];
        Tree(from, false, fromStart, backToStart);
        Tree(to, true, toGoal, onToGoal);
        int goalIndex = to.y * width + to.x;
        float bestCost = fromStart[goalIndex];
        if (float.IsInfinity(bestCost))
        {
            result.skipped = "no way through";
            return result;
        }
        List<KeyValuePair<float, int>> vias = new List<KeyValuePair<float, int>>();
        int every = Mathf.Max(1, Mathf.RoundToInt(sameMeters / cellMeters));
        // Only points in the first part of the leg (measured along the straight line): there the routes may split,
        // and from the point on each route takes the fastest way to the control
        Vector2 line = to - from;
        float lineSquared = Mathf.Max(1f, line.sqrMagnitude);
        for (int y = 0; y < height; y += every)
        {
            for (int x = 0; x < width; x += every)
            {
                float share = Vector2.Dot(new Vector2(x - from.x, y - from.y), line) / lineSquared;
                if (share < 0.05f || share > ViaReach) continue;
                int i = y * width + x;
                float total = fromStart[i] + toGoal[i];
                if (total <= bestCost * MaxSlower * 1.1f) vias.Add(new KeyValuePair<float, int>(total, i));
            }
        }
        vias.Sort((p, q) => p.Key.CompareTo(q.Key));

        // First the way an orienteer looks for choices: take the fastest route, close off a part of it and see
        // which way is fastest then (BlockParts). Then the points round the leg, for ways that closing off missed.
        List<RouteChoice> candidates = BlockParts(best, from, to, bestCost, endCells, sameMeters / cellMeters);
        bool[] covered = new bool[cellCount];
        foreach (RouteChoice c in candidates) Cover(covered, c.cells, corridor);
        int tried = 0;
        foreach (KeyValuePair<float, int> via in vias)
        {
            if (candidates.Count >= MaxCandidates) break;
            if (covered[via.Value]) continue;
            List<Vector2Int> route = Through(via.Value, backToStart, onToGoal);
            // (A point just beside a good route gives a route with a spike out to the point and back: the sharp
            // turn rules it out below.)
            covered[via.Value] = true;
            if (route.Count < 2) continue;
            if (++tried > MaxTries) break;
            // A route bent to touch its point is no real choice: round the point it must be the fastest way itself
            if (!Natural(route, via.Value, bestCost * LocalShare)) continue;
            Cover(covered, route, corridor);
            // Only routes that run somewhere else than every route found so far
            bool fresh = true;
            foreach (RouteChoice c in candidates)
            {
                if (!Different(route, c.cells, from, to, endCells, sameMeters / cellMeters)) { fresh = false; break; }
            }
            if (!fresh) continue;
            candidates.Add(Evaluate(route));
            if (candidates.Count >= MaxCandidates) break;
        }

        // The fastest first. Then, again and again, the route that runs furthest from the ones already taken
        // (and is not much slower), so the choices spread out: one far to the left, one far to the right, rather
        // than four ways down the same side
        candidates.Sort((a, b) => a.seconds.CompareTo(b.seconds));
        float fastest = candidates[0].seconds;
        float gap = Mathf.Max(MinGapSeconds, fastest * MinGapShare);
        // Up to 4 choices, as many as there are clearly different ones; on short legs at most 3, as more would not fit
        int most = straightMeters < 100f ? 3 : 4;
        float sameCells = sameMeters / cellMeters;
        Vector2 along = ((Vector2)(to - from)).normalized;
        List<RouteChoice> usable = new List<RouteChoice>();
        foreach (RouteChoice c in candidates)
        {
            if (c.seconds > fastest * MaxSlower + 3f) break;
            // Turning right round (out and back) is never a route anyone would choose
            // (unless the fastest route has to turn as sharply too, e.g. on a zigzag path up a hill)
            if (c.sharpestTurn > Mathf.Max(MaxTurn, candidates[0].sharpestTurn + 10f)) continue;
            c.side = Side(c.cells, from, along);
            usable.Add(c);
        }
        List<RouteChoice> picked = new List<RouteChoice>();
        if (usable.Count > 0) picked.Add(usable[0]);
        while (picked.Count < most)
        {
            RouteChoice next = null;
            float bestScore = 0f;
            foreach (RouteChoice c in usable)
            {
                if (picked.Contains(c)) continue;
                bool ok = true;
                float apart = float.MaxValue;
                foreach (RouteChoice p in picked)
                {
                    if (Mathf.Abs(c.seconds - p.seconds) < gap || !Different(c.cells, p.cells, from, to, endCells, sameCells)
                        || Detour(c.cells, p.cells, from, to, endCells, sameCells)
                        || (VariantSlower > 0f && c.seconds > p.seconds * VariantSlower
                            && Shared(c.cells, p.cells, from, to, endCells, sameCells) >= VariantShare))
                    {
                        ok = false;
                        break;
                    }
                    apart = Mathf.Min(apart, Mathf.Abs(c.side - p.side));
                }
                if (!ok) continue;
                // How far it runs from the others, less for every bit slower than the fastest
                float slower = c.seconds / Mathf.Max(0.001f, fastest) - 1f;
                float score = (apart + spreadCells) / (1f + 6f * slower);
                if (next == null || score > bestScore) { next = c; bestScore = score; }
            }
            if (next == null) break;
            picked.Add(next);
        }
        if (picked.Count < 2)
        {
            result.skipped = "no clear route choice";
            return result;
        }

        // Choices are in order from left to right (as seen running the leg), and the colours always go the same way:
        // left is blue, right is red, green and then yellow in between. So key 1 is always the left one.
        picked.Sort((a, b) => a.side.CompareTo(b.side));
        int[] order = picked.Count == 2 ? new[] { 1, 0 } : picked.Count == 3 ? new[] { 1, 2, 0 } : new[] { 1, 2, 3, 0 };
        for (int i = 0; i < picked.Count; i++) picked[i].colour = order[i];
        result.choices = picked;
        return result;
    }

    // True when a route is only another route with costly detours: wherever it leaves the other one, it is much
    // slower (DetourLoss) than the other one between the same two places, and then it comes back. That is a mistake
    // more than a route choice (out round something and back, losing a third of the time on that bit), so it is not
    // shown. A real choice is about as fast as the other way over the part where they differ.
    const float DetourLoss = 1.25f;

    bool Detour(List<Vector2Int> a, List<Vector2Int> b, Vector2Int from, Vector2Int to, int endCells, float sameCells)
    {
        int e2 = endCells * endCells;
        float near2 = sameCells * sameCells;
        int parts = 0;
        int i = 0;
        while (i < a.Count)
        {
            Vector2Int c = a[i];
            bool end = (c - from).sqrMagnitude <= e2 || (c - to).sqrMagnitude <= e2;
            if (end || NearbySquared(c, b) <= near2) { i++; continue; }
            // A part away from b: from the last cell next to b before it to the first one after it
            int start = Mathf.Max(0, i - 1);
            while (i < a.Count)
            {
                Vector2Int d = a[i];
                bool dEnd = (d - from).sqrMagnitude <= e2 || (d - to).sqrMagnitude <= e2;
                if (dEnd || NearbySquared(d, b) <= near2) break;
                i++;
            }
            int stop = Mathf.Min(a.Count - 1, i);
            if (stop - start < 3) continue;
            int j0 = NearestIndex(a[start], b), j1 = NearestIndex(a[stop], b);
            if (j1 <= j0) return false; // b runs the other way there: not comparable, so a real difference
            float own = 0f, other = 0f;
            for (int k = start; k < stop; k++) own += StepCost(a[k], a[k + 1]);
            for (int k = j0; k < j1; k++) other += StepCost(b[k], b[k + 1]);
            parts++;
            if (own <= other * DetourLoss) return false; // this part is a real choice
        }
        return parts > 0;
    }

    // Share of route a (leaving out the ends at the controls) that runs along route b
    float Shared(List<Vector2Int> a, List<Vector2Int> b, Vector2Int from, Vector2Int to, int endCells, float sameCells)
    {
        int e2 = endCells * endCells;
        float near2 = sameCells * sameCells;
        int counted = 0, along = 0;
        foreach (Vector2Int c in a)
        {
            if ((c - from).sqrMagnitude <= e2 || (c - to).sqrMagnitude <= e2) continue;
            counted++;
            if (NearbySquared(c, b) <= near2) along++;
        }
        return counted == 0 ? 1f : along / (float)counted;
    }

    static int NearestIndex(Vector2Int c, List<Vector2Int> route)
    {
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < route.Count; i++)
        {
            int dx = route[i].x - c.x, dy = route[i].y - c.y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // ---- Building the alternatives ----

    // Parts of the route closed off in turn, as shares of its length: a fifth at a time (every tenth), then two
    // fifths at a time. Closing off a part makes the search find the best way round it: the other side of a block of
    // houses, another street, a path through the park.
    static readonly float[][] BlockedParts =
    {
        new[] { 0.0f, 0.2f }, new[] { 0.1f, 0.3f }, new[] { 0.2f, 0.4f }, new[] { 0.3f, 0.5f }, new[] { 0.4f, 0.6f },
        new[] { 0.5f, 0.7f }, new[] { 0.6f, 0.8f }, new[] { 0.7f, 0.9f }, new[] { 0.8f, 1.0f },
        new[] { 0.0f, 0.4f }, new[] { 0.3f, 0.7f }, new[] { 0.6f, 1.0f },
    };
    // Round a closed-off part, the cells this far away (as shares of the spread) are closed too: first a narrow
    // band, so the way round can be the next street, then a wide one, so it can be a street further off
    static readonly float[] BlockRadiusShares = { 0.6f, 1.2f };
    const int BlockedRoutes = 4;            // the fastest route and the first three choices found get parts closed off

    // The route choices found by closing off parts of the fastest route (and then of the choices found that way).
    // A choice is kept when it is not much slower than the fastest, really runs somewhere else than every choice kept
    // so far (Different), and is a sensible way in itself: round the point where it is furthest from the route it
    // came from, it must be the fastest way on the open map too. A route that only bulges out round the closed part
    // and back (a "bubble") fails that, as the fastest way there runs through the closed part.
    List<RouteChoice> BlockParts(List<Vector2Int> best, Vector2Int from, Vector2Int to, float bestCost, int endCells, float sameCells)
    {
        List<RouteChoice> found = new List<RouteChoice> { Evaluate(best) };
        float limit = found[0].seconds * MaxSlower + 3f;
        float[,] grid = new float[height, width];
        for (int done = 0; done < found.Count && done < BlockedRoutes; done++)
        foreach (float radiusShare in BlockRadiusShares)
        {
            int radius = Mathf.Max(2, Mathf.RoundToInt(spreadCells * radiusShare));
            int e2 = (endCells + radius) * (endCells + radius);
            List<Vector2Int> route = found[done].cells;
            float[] along = new float[route.Count];
            for (int i = 1; i < route.Count; i++) along[i] = along[i - 1] + (route[i] - route[i - 1]).magnitude;
            float total = Mathf.Max(1f, along[route.Count - 1]);
            foreach (float[] part in BlockedParts)
            {
                System.Array.Copy(speed, grid, speed.Length);
                bool any = false;
                for (int i = 0; i < route.Count; i++)
                {
                    float share = along[i] / total;
                    if (share < part[0] || share > part[1]) continue;
                    Vector2Int c = route[i];
                    // Never round the controls: everybody leaves and reaches them the same way
                    if ((c - from).sqrMagnitude <= e2 || (c - to).sqrMagnitude <= e2) continue;
                    any = true;
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int x = c.x + dx, y = c.y + dy;
                            if (x >= 0 && y >= 0 && x < width && y < height && dx * dx + dy * dy <= radius * radius) grid[y, x] = 0f;
                        }
                    }
                }
                if (!any) continue;
                List<Vector2Int> other = Path(from, to, grid);
                if (other == null || other.Count < 2) continue;
                other = RemoveLoops(other);
                bool fresh = true;
                foreach (RouteChoice c in found)
                {
                    if (!Different(other, c.cells, from, to, endCells, sameCells)) { fresh = false; break; }
                }
                if (!fresh) continue;
                if (!Natural(other, Furthest(other, route), bestCost * LocalShare)) continue;
                RouteChoice choice = Evaluate(other);
                if (choice.seconds > limit) continue;
                found.Add(choice);
                if (found.Count >= MaxCandidates) return found;
            }
        }
        return found;
    }

    // The cell of a route furthest from another route (as an index into the window)
    int Furthest(List<Vector2Int> route, List<Vector2Int> from)
    {
        int bestIndex = 0;
        float bestDistance = -1f;
        for (int i = 0; i < route.Count; i += 2)
        {
            float d = NearbySquared(route[i], from);
            if (d == float.MaxValue) d = spreadCells * spreadCells * 4f + NearestSquared(route[i], from, int.MaxValue);
            if (d > bestDistance) { bestDistance = d; bestIndex = i; }
        }
        Vector2Int v = route[bestIndex];
        return v.y * width + v.x;
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

    float divergeCells;  // see DivergeShare, in cells, for the leg being worked out
    float spreadCells;   // see SpreadShare

    // True when the two routes part properly somewhere (away from the controls): each has a stretch at least
    // divergeCells long that runs away from the other one and gets at least spreadCells from it. Sharing the rest of
    // the leg is fine: a choice of going left or right round a house at the end is a real route choice.
    bool Different(List<Vector2Int> a, List<Vector2Int> b, Vector2Int from, Vector2Int to, int endCells, float sameCells)
    {
        return Parts(a, b, from, to, endCells, sameCells) && Parts(b, a, from, to, endCells, sameCells);
    }

    bool Parts(List<Vector2Int> a, List<Vector2Int> b, Vector2Int from, Vector2Int to, int endCells, float sameCells)
    {
        int e2 = endCells * endCells;
        float near2 = sameCells * sameCells;
        float run = 0f, far = 0f;
        for (int i = 0; i < a.Count; i++)
        {
            Vector2Int c = a[i];
            bool end = (c - from).sqrMagnitude <= e2 || (c - to).sqrMagnitude <= e2;
            float d2 = end ? 0f : NearbySquared(c, b);
            if (d2 <= near2)
            {
                run = 0f;
                far = 0f;
                continue;
            }
            if (i > 0) run += (a[i] - a[i - 1]).magnitude;
            far = Mathf.Max(far, d2);
            if (run >= divergeCells && far >= spreadCells * spreadCells) return true;
        }
        return false;
    }

    // Squared distance from a cell to the nearest cell of a route, looked up in buckets as big as spreadCells
    // (anything further than that is "far enough", float.MaxValue)
    readonly Dictionary<List<Vector2Int>, Dictionary<long, List<Vector2Int>>> bucketed = new Dictionary<List<Vector2Int>, Dictionary<long, List<Vector2Int>>>();

    float NearbySquared(Vector2Int c, List<Vector2Int> route)
    {
        int size = Mathf.Max(1, Mathf.CeilToInt(spreadCells));
        if (!bucketed.TryGetValue(route, out Dictionary<long, List<Vector2Int>> buckets))
        {
            buckets = new Dictionary<long, List<Vector2Int>>();
            foreach (Vector2Int r in route)
            {
                long key = Key(Floor(r.x, size), Floor(r.y, size));
                if (!buckets.TryGetValue(key, out List<Vector2Int> list)) buckets[key] = list = new List<Vector2Int>();
                list.Add(r);
            }
            bucketed[route] = buckets;
        }
        int bx = Floor(c.x, size), by = Floor(c.y, size);
        float best = float.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!buckets.TryGetValue(Key(bx + dx, by + dy), out List<Vector2Int> list)) continue;
                foreach (Vector2Int o in list) best = Mathf.Min(best, (o - c).sqrMagnitude);
            }
        }
        return best;
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

    // ---- Fastest times from the start and to the goal ----

    // Dijkstra over the whole window: the fastest time from the root to every cell (or, backwards, from every cell
    // to the root), and the next cell on the way back to the root
    void Tree(Vector2Int root, bool backwards, float[] cost, int[] next)
    {
        for (int i = 0; i < cost.Length; i++) { cost[i] = float.PositiveInfinity; next[i] = -1; }
        bool[] done = new bool[cost.Length];
        int rootIndex = root.y * width + root.x;
        cost[rootIndex] = 0f;
        heapCount = 0;
        Push(rootIndex, 0f);
        while (heapCount > 0)
        {
            int current = Pop();
            if (done[current]) continue;
            done[current] = true;
            int cx = current % width, cy = current / width;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + DirX[d], ny = cy + DirY[d];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                int other = ny * width + nx;
                if (done[other] || speed[ny, nx] <= 0.01f) continue;
                // No cutting a corner past a closed cell (a fence drawn diagonally across the grid), as AStarPathfinder
                if (DirX[d] != 0 && DirY[d] != 0 && (speed[cy, nx] <= 0.01f || speed[ny, cx] <= 0.01f)) continue;
                float step = (DirX[d] != 0 && DirY[d] != 0) ? 1.414f : 1f;
                float c;
                if (!backwards)
                {
                    // current -> other
                    float slope = slopes[d] != null ? slopes[d][cy, cx] : 1f;
                    if (slope <= 0f) continue;
                    c = step / speed[ny, nx] / slope;
                }
                else
                {
                    // other -> current, in the opposite direction
                    int back = (d + 4) % 8;
                    float slope = slopes[back] != null ? slopes[back][ny, nx] : 1f;
                    if (slope <= 0f) continue;
                    c = step / Mathf.Max(0.01f, speed[cy, cx]) / slope;
                }
                float total = cost[current] + c;
                if (total >= cost[other]) continue;
                cost[other] = total;
                next[other] = current;
                Push(other, total);
            }
        }
    }

    // True when the route, from a bit before the point to a bit after it (together about 'reach' seconds), is the
    // fastest way between those two places. A route that only goes out of its way to pass the point fails this:
    // the fastest way between them cuts the corner. A real route choice passes, as it is fastest all along.
    bool Natural(List<Vector2Int> route, int via, float reach)
    {
        Vector2Int v = new Vector2Int(via % width, via / width);
        int at = route.IndexOf(v);
        if (at < 0) return true;
        int a = at, b = at;
        float own = 0f;
        while (a > 0 && own < reach / 2f) { own += StepCost(route[a - 1], route[a]); a--; }
        float back = own;
        while (b < route.Count - 1 && own - back < reach / 2f) { own += StepCost(route[b], route[b + 1]); b++; }
        if (b - a < 2) return true;
        List<Vector2Int> shortcut = Path(route[a], route[b]);
        if (shortcut == null) return true;
        float fastest = 0f;
        for (int i = 0; i + 1 < shortcut.Count; i++) fastest += StepCost(shortcut[i], shortcut[i + 1]);
        return own <= fastest * LocalSlack + 0.0001f;
    }

    // The cost of one step between two neighbouring cells, as in Tree
    float StepCost(Vector2Int p, Vector2Int q)
    {
        int dx = q.x - p.x, dy = q.y - p.y;
        int d = 0;
        for (int k = 0; k < 8; k++) if (DirX[k] == dx && DirY[k] == dy) { d = k; break; }
        float step = (dx != 0 && dy != 0) ? 1.414f : 1f;
        float slope = slopes[d] != null ? slopes[d][p.y, p.x] : 1f;
        if (slope <= 0f || speed[q.y, q.x] <= 0.01f) return 1000f;
        return step / speed[q.y, q.x] / slope;
    }

    // The fastest route from the start through a cell to the goal, without loops
    List<Vector2Int> Through(int via, int[] backToStart, int[] onToGoal)
    {
        List<Vector2Int> route = new List<Vector2Int>();
        for (int c = via; c >= 0; c = backToStart[c]) route.Add(new Vector2Int(c % width, c / width));
        route.Reverse();
        for (int c = onToGoal[via]; c >= 0; c = onToGoal[c]) route.Add(new Vector2Int(c % width, c / width));
        return RemoveLoops(route);
    }

    // Marks the cells round a route
    void Cover(bool[] covered, List<Vector2Int> route, int radius)
    {
        int r2 = radius * radius;
        foreach (Vector2Int c in route)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int y = c.y + dy;
                if (y < 0 || y >= height) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = c.x + dx;
                    if (x < 0 || x >= width || dx * dx + dy * dy > r2) continue;
                    covered[y * width + x] = true;
                }
            }
        }
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
                // No cutting a corner past a closed cell (a fence drawn diagonally across the grid), as AStarPathfinder
                if (DirX[d] != 0 && DirY[d] != 0 && (grid[cy, nx] <= 0.01f || grid[ny, cx] <= 0.01f)) continue;
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

    // ---- Smooth lines ----
    // A* walks from cell to cell in 8 directions, so its routes zigzag. Before timing, each route is made into the line
    // a runner (or a pen) would take: straight wherever a straight line is at least as fast as the cells it replaces
    // and does not cross anything impassable, and with the corners rounded off where the ground allows it.
    // Points are in grid coordinates (cell centres on whole numbers).

    const float LookAheadMeters = 120f;   // longest straight piece tried at once
    const float CornerMeters = 10f;       // corners are rounded over at most this distance on each side

    List<Vector2> Smooth(List<Vector2Int> cells)
    {
        int n = cells.Count;
        List<Vector2> line = new List<Vector2>();
        if (n < 2)
        {
            foreach (Vector2Int c in cells) line.Add(c);
            return line;
        }
        // The cost along the cell route, from its start
        float[] along = new float[n];
        // How many steps of the cell route touch impassable ground, up to each cell. A route the player drew over
        // such ground (route analysis keeps a line over a lake or a garden as it is) may be made straight over it
        // too, as long as that is no slower; otherwise every wobble of the hand there would stay.
        int[] blockedSteps = new int[n];
        for (int i = 1; i < n; i++)
        {
            along[i] = along[i - 1] + LineCost(cells[i - 1], cells[i], out bool stepBlocked);
            blockedSteps[i] = blockedSteps[i - 1] + (stepBlocked ? 1 : 0);
        }

        // Straight pieces, as long as they are no slower than the cells they replace
        int reach = Mathf.Clamp(Mathf.RoundToInt(LookAheadMeters / cellMeters), 8, 80);
        line.Add(cells[0]);
        int at = 0;
        while (at < n - 1)
        {
            int best = at + 1, misses = 0;
            for (int j = at + 2; j < n && j <= at + reach; j++)
            {
                float cost = LineCost(cells[at], cells[j], out bool blocked);
                bool allowed = !blocked || blockedSteps[j] > blockedSteps[at];
                if (allowed && cost <= (along[j] - along[at]) * 1.01f + 0.0001f) { best = j; misses = 0; }
                else if (++misses > 6) break;
            }
            line.Add(cells[best]);
            at = best;
        }

        // Round the corners: a curve from a bit before each corner to a bit after it, if nothing is in the way
        float most = Mathf.Max(1.5f, CornerMeters / cellMeters);
        List<Vector2> round = new List<Vector2> { line[0] };
        for (int k = 1; k < line.Count - 1; k++)
        {
            Vector2 a = line[k - 1], b = line[k], c = line[k + 1];
            Vector2 ab = a - b, cb = c - b;
            float d = Mathf.Min(most, 0.4f * ab.magnitude, 0.4f * cb.magnitude);
            if (d < 0.3f || Vector2.Angle(-ab, cb) < 8f)
            {
                round.Add(b);
                continue;
            }
            List<Vector2> curve = null;
            for (int attempt = 0; attempt < 2 && curve == null; attempt++, d *= 0.5f)
            {
                Vector2 p0 = b + ab.normalized * d, p2 = b + cb.normalized * d;
                List<Vector2> pts = new List<Vector2>();
                for (int s = 0; s <= 6; s++)
                {
                    float t = s / 6f;
                    pts.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * b + t * t * p2);
                }
                bool clear = true;
                for (int s = 0; s < pts.Count - 1 && clear; s++)
                {
                    LineCost(pts[s], pts[s + 1], out bool blocked, EdgeMargin);
                    if (blocked) clear = false;
                }
                LineCost(round[round.Count - 1], pts[0], out bool blockedIn, EdgeMargin);
                if (clear && !blockedIn) curve = pts;
            }
            if (curve != null) round.AddRange(curve);
            else round.Add(b);
        }
        round.Add(line[line.Count - 1]);
        return round;
    }

    // How far (in cells) a rounded corner keeps from impassable ground. A cell is open or blocked as a whole, so a
    // curve that only keeps its points on open cells can still run up to half a cell into a garden or a house where
    // it cuts a corner. With the margin it stays about a third of a cell inside the open cells.
    const float EdgeMargin = 0.35f;

    // The A* cost (distance / ground speed / slope) of a straight line between two points, sampled about every
    // third of a cell; blocked when it touches impassable ground (or comes within margin cells of it)
    float LineCost(Vector2 a, Vector2 b, out bool blocked, float margin = 0f)
    {
        blocked = false;
        Vector2 ab = b - a;
        float length = ab.magnitude;
        if (length < 0.0001f) return 0f;
        int steps = Mathf.Max(1, Mathf.CeilToInt(length * 3f));
        float step = length / steps;
        int d = Direction(ab);
        float cost = 0f;
        int px = int.MinValue, py = int.MinValue;
        for (int s = 0; s < steps; s++)
        {
            Vector2 p = a + ab * ((s + 0.5f) / steps);
            int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
            if (x < 0 || y < 0 || x >= width || y >= height) { blocked = true; return float.MaxValue; }
            float cellSpeed = speed[y, x];
            if (cellSpeed <= 0.01f) { blocked = true; cellSpeed = BlockedSpeed; }
            // A line that goes from one cell to the next only through their corners slips through a fence drawn
            // diagonally across the grid (its cells only touch at the corners): that counts as going over it
            else if (px != int.MinValue && x != px && y != py && (speed[py, x] <= 0.01f || speed[y, px] <= 0.01f))
            {
                blocked = true;
                cellSpeed = BlockedSpeed;
            }
            else if (margin > 0f && !blocked && NearBlocked(p, margin)) blocked = true;
            float slope = slopes[d] != null ? Mathf.Max(0.05f, slopes[d][y, x]) : 1f;
            cost += step / cellSpeed / slope;
            px = x;
            py = y;
        }
        return cost;
    }

    // Whether a blocked (or outside) cell lies within margin cells of the point, along x and y
    bool NearBlocked(Vector2 p, float margin)
    {
        for (int i = 0; i < 4; i++)
        {
            int x = Mathf.RoundToInt(p.x + ((i & 1) == 0 ? -margin : margin));
            int y = Mathf.RoundToInt(p.y + ((i & 2) == 0 ? -margin : margin));
            if (x < 0 || y < 0 || x >= width || y >= height || speed[y, x] <= 0.01f) return true;
        }
        return false;
    }

    // The nearest of the 8 grid directions
    static int Direction(Vector2 v)
    {
        float degrees = 90f - Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        int d = Mathf.RoundToInt(degrees / 45f) % 8;
        return d < 0 ? d + 8 : d;
    }

    // ---- Timing ----

    public RouteChoice Evaluate(List<Vector2Int> route)
    {
        route = TrimEnds(route);
        List<Vector2> line = Smooth(route);
        float seconds = Seconds(line, out float sharpest);
        return new RouteChoice { cells = route, points = line, seconds = seconds, meters = Meters(line), sharpestTurn = sharpest };
    }

    // A route that is pushed round a corridor, or goes through a far point, can run past a control and come back to
    // it. Near the controls the fastest way in and out is used instead: from where the route first comes close to the
    // control you go to, and to where it last is close to the control you leave.
    List<Vector2Int> TrimEnds(List<Vector2Int> route)
    {
        if (route == null || route.Count < 3) return route;
        Vector2Int from = route[0], to = route[route.Count - 1];
        float straight = Vector2.Distance(from, to);
        float near = Mathf.Max(25f / cellMeters, straight * 0.12f);
        float near2 = near * near;
        int first = -1;
        for (int i = 0; i < route.Count; i++)
        {
            if ((route[i] - to).sqrMagnitude <= near2) { first = i; break; }
        }
        if (first > 0 && first < route.Count - 1)
        {
            List<Vector2Int> end = Path(route[first], to);
            if (end != null)
            {
                route = new List<Vector2Int>(route.GetRange(0, first));
                route.AddRange(end);
            }
        }
        int last = -1;
        for (int i = route.Count - 1; i >= 0; i--)
        {
            if ((route[i] - from).sqrMagnitude <= near2) { last = i; break; }
        }
        if (last > 0 && last < route.Count - 1)
        {
            List<Vector2Int> start = Path(from, route[last]);
            if (start != null)
            {
                start.RemoveAt(start.Count - 1);
                start.AddRange(route.GetRange(last, route.Count - last));
                route = start;
            }
        }
        return RemoveLoops(route);
    }

    float Meters(List<Vector2> line)
    {
        float m = 0f;
        for (int i = 0; i < line.Count - 1; i++) m += Vector2.Distance(line[i], line[i + 1]);
        return m * cellMeters;
    }

    // The race time of the speed-100 runner on a line: distance / (base speed x ground x slope) along it, plus the
    // time lost in each turn (AgentMovement slows down to TurnSpeedShare at the corner and then accelerates again at
    // TurnAcceleration: that costs v/(2a) x (1 - share)² seconds)
    public float Seconds(List<Vector2> line, out float sharpest)
    {
        sharpest = 0f;
        if (line == null || line.Count < 2 || baseSpeed <= 0f) return 0f;
        float cost = 0f;
        for (int i = 0; i < line.Count - 1; i++) cost += LineCost(line[i], line[i + 1], out _);
        float real = cost * cellSize / baseSpeed;

        // Points about one cell apart, with the runner's speed at each, for the turns
        List<Vector2> pts = new List<Vector2>();
        List<float> v = new List<float>();
        for (int i = 0; i < line.Count - 1; i++)
        {
            Vector2 ab = line[i + 1] - line[i];
            int steps = Mathf.Max(1, Mathf.CeilToInt(ab.magnitude));
            int d = Direction(ab);
            for (int s = 0; s < steps; s++)
            {
                Vector2 p = line[i] + ab * ((float)s / steps);
                int x = Mathf.Clamp(Mathf.RoundToInt(p.x), 0, width - 1), y = Mathf.Clamp(Mathf.RoundToInt(p.y), 0, height - 1);
                float slope = slopes[d] != null ? Mathf.Max(0.05f, slopes[d][y, x]) : 1f;
                pts.Add(p);
                v.Add(baseSpeed * Mathf.Max(0.05f, speed[y, x]) * slope);
            }
        }
        pts.Add(line[line.Count - 1]);
        v.Add(v[v.Count - 1]);

        // The turns, measured as AgentMovement.ComputeTurns does: direction in and out over about 20 m, and only
        // the sharpest point of each corner counts
        int n = pts.Count;
        int k = Mathf.Max(1, Mathf.RoundToInt(TurnWindowMeters / Mathf.Max(0.5f, cellMeters)));
        float nearControl = Mathf.Max(40f / cellMeters, Vector2.Distance(pts[0], pts[n - 1]) * 0.15f);
        float[] angle = new float[n];
        for (int i = 1; i < n - 1; i++)
        {
            Vector2 into = pts[i] - pts[Mathf.Max(0, i - k)];
            Vector2 outOf = pts[Mathf.Min(n - 1, i + k)] - pts[i];
            angle[i] = into.sqrMagnitude > 0.0001f && outOf.sqrMagnitude > 0.0001f ? Vector2.Angle(into, outOf) : 0f;
        }
        for (int i = 1; i < n - 1; i++)
        {
            // Coming in to a control from the side or leaving it back the way you came is fine
            if ((pts[i] - pts[0]).magnitude > nearControl && (pts[i] - pts[n - 1]).magnitude > nearControl) sharpest = Mathf.Max(sharpest, angle[i]);
            if (angle[i] <= 30f) continue;
            bool top = true;
            for (int j = Mathf.Max(0, i - k); j <= Mathf.Min(n - 1, i + k) && top; j++)
            {
                if (angle[j] > angle[i] || (angle[j] == angle[i] && j < i)) top = false;
            }
            if (!top) continue;
            float lost = 1f - AgentMovement.TurnSpeedShare(angle[i]);
            real += v[i] / (2f * TurnAcceleration) * lost * lost;
        }
        return real * gameSecondsPerSecond;
    }
}
