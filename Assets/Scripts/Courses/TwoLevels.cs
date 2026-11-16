using System.Collections.Generic;
using UnityEngine;

// Two levels on a one-level grid (2026-11-16): tunnels under a road, bridges over a path (Tunnels.cs finds them).
//
// Each tunnel or bridge is a "way". Its own cells are the ones only it has (under the fence or the wall, over the
// water); its shared cells are the ones where it crosses ground that can be run on at the other level (the road over
// the tunnel). A runner is on the ground (level 0) or on a way (level 1):
//  - onto a way's own cells only from beyond its ends, and then the runner is on the way;
//  - on the way it stays on it (over its shared cells too) and leaves it only beyond its ends;
//  - from the ground onto shared cells is fine (the road over the tunnel), but not on into the way's own cells.
// So the road can be run along over the tunnel, the tunnel straight through under it, and nobody turns from one to
// the other. AStarPathfinder searches with the level, and ManualPathDrawer checks the drawn line with the same rules.
public class TwoLevels
{
    public readonly int rows, cols;

    class Way
    {
        public Vector2 from, dir;   // the middle line, in cells (cell (x, y) has its centre at x + 0.5, y + 0.5)
        public float lengthSq;
        public float lo = float.MaxValue, hi = float.MinValue;   // where along it (0..1) its own cells are
        public float margin;
        public float Along(int x, int y) => Vector2.Dot(new Vector2(x + 0.5f, y + 0.5f) - from, dir) / lengthSq;
        // At or past an end: from the first or last row of its own cells (so a line drawn in at a slant, or a
        // runner stepping in from the side of the mouth, gets in), not from the middle where the other level is
        public bool Beyond(int x, int y)
        {
            float t = Along(x, y);
            return t <= lo + margin || t >= hi - margin;
        }
    }

    readonly List<Way> ways = new List<Way>();
    readonly Dictionary<int, int> wayOf = new Dictionary<int, int>();   // cell -> way number
    readonly HashSet<int> own = new HashSet<int>();                     // cells only the way has

    public TwoLevels(int rows, int cols)
    {
        this.rows = rows;
        this.cols = cols;
    }

    public bool Any => ways.Count > 0;

    // The grids loaded so far that have tunnels or bridges, by size: a pathfinder only has a copy of the speeds, so
    // it finds its levels by the size of its grid (two maps of exactly the same size would share them)
    static readonly List<TwoLevels> known = new List<TwoLevels>();

    public static void Register(TwoLevels levels)
    {
        known.RemoveAll(l => l.rows == levels.rows && l.cols == levels.cols);
        if (levels.Any) known.Add(levels);
    }

    public static TwoLevels For(int rows, int cols)
    {
        foreach (TwoLevels l in known) if (l.rows == rows && l.cols == cols) return l;
        return null;
    }

    public void Add(Vector2 from, Vector2 to, List<Vector2Int> ownCells, List<Vector2Int> sharedCells)
    {
        Way way = new Way { from = from, dir = to - from };
        way.lengthSq = Mathf.Max(0.01f, way.dir.sqrMagnitude);
        way.margin = 0.35f / Mathf.Sqrt(way.lengthSq);
        int number = ways.Count;
        foreach (Vector2Int c in ownCells)
        {
            int key = c.y * cols + c.x;
            if (wayOf.ContainsKey(key)) continue;
            wayOf[key] = number;
            own.Add(key);
            float t = way.Along(c.x, c.y);
            way.lo = Mathf.Min(way.lo, t);
            way.hi = Mathf.Max(way.hi, t);
        }
        if (way.hi < way.lo) return;
        ways.Add(way);
        // Shared only where the way goes over or under something: between its first and last own cell
        foreach (Vector2Int c in sharedCells)
        {
            int key = c.y * cols + c.x;
            if (wayOf.ContainsKey(key)) continue;
            float t = way.Along(c.x, c.y);
            if (t < way.lo || t > way.hi) continue;
            wayOf[key] = number;
        }
    }

    // The level a runner standing on this cell is on, when nothing else is known
    public int StartLevel(int x, int y) => own.Contains(y * cols + x) ? 1 : 0;

    // May a runner on (fx, fy) at level 'level' step to (tx, ty)? 'next' is its level there.
    public bool Step(int fx, int fy, int level, int tx, int ty, out int next)
    {
        next = 0;
        if (tx < 0 || ty < 0 || tx >= cols || ty >= rows) return true;
        int fromKey = fy * cols + fx, toKey = ty * cols + tx;
        bool fromOn = fx >= 0 && fy >= 0 && fx < cols && fy < rows && wayOf.TryGetValue(fromKey, out int fromWay);
        bool toOn = wayOf.TryGetValue(toKey, out int toWay);
        if (!fromOn && !toOn) return true;
        if (!fromOn)
        {
            // From the ground: onto the road over a tunnel, or into the way at its end
            if (!own.Contains(toKey)) return true;
            next = 1;
            return ways[toWay].Beyond(fx, fy);
        }
        wayOf.TryGetValue(fromKey, out fromWay);
        bool onWay = own.Contains(fromKey) || level == 1;
        if (!onWay)
        {
            // On the road over a tunnel: not down into it
            return !(toOn && own.Contains(toKey));
        }
        if (!toOn) return ways[fromWay].Beyond(tx, ty);
        next = 1;
        return true;
    }
}
