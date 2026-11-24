using System;
using System.Collections.Generic;
using MapImport;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Roads close beside a cliff, a fence or a lake shore on forest maps (2026-11-24, Delsjön: the road over the water could
// not be drawn the last bit to the shore, because an impassable cliff runs 5 to 9 m beside it). The forest grid has 10 m
// cells, so the cliff and the road share cells, and the cliff closed them; the road was cut off although the map shows it
// running all the way. The import does this itself now (GridBuilder.OpenPathsAlongLines), but the maps already built
// (the built-in ones too) keep their old grid, so the same is done here every time a map's grid is loaded.
//
// A closed cell that a path runs through, and that a blocking line closed, is opened again when the whole bit of path in
// it lies on one side of the line. The line is moved off the path instead: the open neighbour cells on the far side of
// the line are closed, so it still can't be crossed there. Where there is a path on the far side too, or the path touches
// the line, the cell stays shut.
public static class RoadsBesideLines
{
    struct Change
    {
        public int open;          // row * cols + col of the path cell to open
        public float speed;       // the path's speed (before the slope model)
        public int[] close;       // the neighbour cells on the far side of the line
    }

    static readonly Dictionary<string, List<Change>> cache = new Dictionary<string, List<Change>>();

    // gridName is "<package root>grid"; call after the speeds are final (slope model, passages)
    public static void Apply(string gridName, float[,] speed)
    {
        if (speed == null || string.IsNullOrEmpty(gridName) || !gridName.EndsWith("grid")) return;
        MapDefinition map = null;
        foreach (MapDefinition m in MapDefinition.All)
        {
            if (m != null && m.gridName == gridName) { map = m; break; }
        }
        if (map == null || string.IsNullOrEmpty(map.linesPath) || map.gridSize < 5f) return;   // forest maps only
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        if (rows != map.rows || cols != map.cols) return;

        string key = map.linesPath + "|" + rows + "x" + cols;
        if (!cache.TryGetValue(key, out List<Change> changes))
        {
            try
            {
                changes = Find(map, speed);
            }
            catch (Exception e)
            {
                Debug.LogWarning("RoadsBesideLines: " + map.displayName + ": " + e.Message);
                changes = new List<Change>();
            }
            cache[key] = changes;
        }
        int opened = 0;
        foreach (Change c in changes)
        {
            int r = c.open / cols, col = c.open % cols;
            if (speed[r, col] > 0.01f) continue;
            speed[r, col] = Mathf.Max(0.05f, Mathf.Max(c.speed, NeighbourSpeed(speed, r, col)));
            foreach (int n in c.close) speed[n / cols, n % cols] = 0f;
            opened++;
        }
        if (opened > 0) Debug.Log($"RoadsBesideLines: {opened} road cells beside cliffs, fences and shores opened on {map.displayName}");
    }

    // The quickest open neighbour (a road cell next to it, after the slope model), or 0
    static float NeighbourSpeed(float[,] speed, int r, int c)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float best = 0f;
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                int rr = r + dr, cc = c + dc;
                if ((dr == 0 && dc == 0) || rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                best = Mathf.Max(best, speed[rr, cc]);
            }
        return best;
    }

    static List<Change> Find(MapDefinition map, float[,] speed)
    {
        var changes = new List<Change>();
        byte[] bytes = MapPackages.LoadBytes(map.linesPath, ".json");
        if (bytes == null) return changes;
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        // Map coordinates to cell units: cell (c, r) spans c .. c + 1 (the same cells as MapDefinition.ToCell)
        double sx = (cols - 1) / ((double)map.maxx - map.minx), sy = (rows - 1) / ((double)map.maxy - map.miny);
        double CX(double e) => (e - map.minx) * sx + 0.5;
        double CY(double n) => (n - map.miny) * sy + 0.5;

        var segs = new List<double[]>();
        var paths = new List<(double[] seg, float speed)>();
        var rules = new Dictionary<string, TerrainRule>();
        JObject root = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        foreach (JToken feature in root["features"] ?? new JArray())
        {
            string layer = (string)feature["properties"]?["Layer"] ?? "";
            if (!rules.TryGetValue(layer, out TerrainRule rule)) rules[layer] = rule = Passages.Rule(layer, false);
            bool blocking = rule.role == TerrainRole.BlockingLine;
            bool isPath = rule.role == TerrainRole.Path;
            if (!blocking && !isPath) continue;
            JToken geometry = feature["geometry"];
            string type = (string)geometry?["type"] ?? "";
            JArray coords = geometry?["coordinates"] as JArray;
            if (coords == null) continue;
            var parts = new List<JArray>();
            if (type == "LineString") parts.Add(coords);
            else if (type == "MultiLineString") foreach (JToken part in coords) if (part is JArray a) parts.Add(a);
            foreach (JArray part in parts)
            {
                for (int i = 0; i + 1 < part.Count; i++)
                {
                    if (!(part[i] is JArray p0) || !(part[i + 1] is JArray p1)) continue;
                    double[] seg = { CX((double)p0[0]), CY((double)p0[1]), CX((double)p1[0]), CY((double)p1[1]) };
                    if (blocking) segs.Add(seg);
                    else paths.Add((seg, rule.speed));
                }
            }
        }
        if (segs.Count == 0 || paths.Count == 0) return changes;

        // Path points in closed cells, and the path cells (any path on the far side keeps the line where it is)
        var pathCell = new HashSet<int>();
        var points = new Dictionary<int, List<Vector2d>>();
        var pathSpeed = new Dictionary<int, float>();
        foreach (var (seg, s) in paths)
        {
            double len = Math.Sqrt((seg[2] - seg[0]) * (seg[2] - seg[0]) + (seg[3] - seg[1]) * (seg[3] - seg[1]));
            int steps = Math.Max(1, (int)Math.Ceiling(len * 4));
            for (int k = 0; k <= steps; k++)
            {
                double t = (double)k / steps;
                double x = seg[0] + (seg[2] - seg[0]) * t, y = seg[1] + (seg[3] - seg[1]) * t;
                int c = (int)Math.Floor(x), r = (int)Math.Floor(y);
                if (c < 0 || r < 0 || c >= cols || r >= rows) continue;
                int idx = r * cols + c;
                pathCell.Add(idx);
                if (speed[r, c] > 0.01f) continue;
                if (!points.TryGetValue(idx, out List<Vector2d> list)) points[idx] = list = new List<Vector2d>();
                list.Add(new Vector2d(x, y));
                pathSpeed[idx] = pathSpeed.TryGetValue(idx, out float was) ? Mathf.Max(was, s) : s;
            }
        }
        if (points.Count == 0) return changes;

        // The blocking line segments that pass through the 3 x 3 cells round each of those cells
        var near = new Dictionary<int, List<int>>();
        for (int k = 0; k < segs.Count; k++)
        {
            double[] s = segs[k];
            double len = Math.Sqrt((s[2] - s[0]) * (s[2] - s[0]) + (s[3] - s[1]) * (s[3] - s[1]));
            int steps = Math.Max(1, (int)Math.Ceiling(len * 3));
            int lastCell = int.MinValue;
            for (int q = 0; q <= steps; q++)
            {
                double t = (double)q / steps;
                int c = (int)Math.Floor(s[0] + (s[2] - s[0]) * t), r = (int)Math.Floor(s[1] + (s[3] - s[1]) * t);
                int here = r * cols + c;
                if (here == lastCell) continue;
                lastCell = here;
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int rr = r + dr, cc = c + dc;
                        if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                        int idx = rr * cols + cc;
                        if (!points.ContainsKey(idx)) continue;
                        if (!near.TryGetValue(idx, out List<int> list)) near[idx] = list = new List<int>();
                        if (!list.Contains(k)) list.Add(k);
                    }
            }
        }

        // Which side of the nearest segment a point is on (+1 / -1), and how far from it (cells)
        int Side(List<int> list, double x, double y, out double distance)
        {
            distance = double.MaxValue;
            int side = 0;
            foreach (int k in list)
            {
                double[] s = segs[k];
                double dx = s[2] - s[0], dy = s[3] - s[1], l2 = dx * dx + dy * dy;
                double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - s[0]) * dx + (y - s[1]) * dy) / l2));
                double px = s[0] + t * dx - x, py = s[1] + t * dy - y;
                double d = Math.Sqrt(px * px + py * py);
                if (d >= distance) continue;
                distance = d;
                double cross = dx * (y - s[1]) - dy * (x - s[0]);
                side = cross > 0 ? 1 : cross < 0 ? -1 : 0;
            }
            return side;
        }

        var cells = new List<int>(points.Keys);
        cells.Sort();
        var close = new List<int>();
        foreach (int idx in cells)
        {
            if (!near.TryGetValue(idx, out List<int> list)) continue;   // closed by something else (a house, out of bounds)
            int pathSide = 0;
            bool ok = true;
            foreach (Vector2d p in points[idx])
            {
                int s = Side(list, p.x, p.y, out double d);
                if (s == 0 || d < 0.05) { ok = false; break; }
                if (pathSide == 0) pathSide = s;
                else if (s != pathSide) { ok = false; break; }
            }
            if (!ok || pathSide == 0) continue;
            int r = idx / cols, c = idx % cols;
            close.Clear();
            for (int dr = -1; dr <= 1 && ok; dr++)
                for (int dc = -1; dc <= 1 && ok; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int rr = r + dr, cc = c + dc;
                    if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                    int n = rr * cols + cc;
                    int s = Side(list, cc + 0.5, rr + 0.5, out _);
                    if (s == 0 || s == pathSide) continue;
                    if (pathCell.Contains(n)) { ok = false; break; }   // a path on the other side too
                    if (speed[rr, cc] > 0.01f) close.Add(n);
                }
            if (!ok) continue;
            changes.Add(new Change { open = idx, speed = pathSpeed[idx], close = close.ToArray() });
        }
        return changes;
    }

    struct Vector2d
    {
        public double x, y;
        public Vector2d(double x, double y) { this.x = x; this.y = y; }
    }
}
