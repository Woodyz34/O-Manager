using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Tunnels and underpasses (ISSprOM 512.2 "Tunnel el. genomgång under mark"), 2026-11-15.
//
// A tunnel is drawn as its two side lines across a road, a railway or a fenced street. The grid has one level, so the
// import only opened the cells under the two side lines (the bridge rule). On a sprint map those are thin diagonal
// lines of 2 m cells, and the runners and the drawn line could not get through (no corner-cutting past fences).
//
// Here, when a map's grid is loaded, every tunnel gets a solid passage between its side lines through the fences and
// walls it goes under. Two levels (2026-11-16, TwoLevels): the road above a tunnel stays open to walk along, and the
// way through the tunnel goes straight on under it. Who goes into the tunnel stays in it to its other end, and who is
// on the road can not drop down into it (no 90 degree turn from the tunnel up onto the road). Before, walls along the
// tunnel's sides cut the road above in two.
//
// Bridges and footbridges (512.1 "Bro / Spång") get the same passage between their side lines, without walls (the
// water or road under them is closed anyway): a narrow bridge drawn at a slant was, like the tunnels, only a thin
// chain of cells touching at the corners, and nobody could get over it.
public static class Tunnels
{
    struct Tunnel
    {
        public Vector2 a1, a2, b1, b2;   // side line a from a1 to a2, side line b from b1 (near a1) to b2 (near a2)
        public bool walls;               // a tunnel (true) or a bridge
    }

    static readonly Dictionary<string, List<Tunnel>> cache = new Dictionary<string, List<Tunnel>>();

    // gridName is "<package root>grid"; the map objects are in "<package root>lines"
    public static void Apply(string gridName, float[,] speed)
    {
        if (speed == null) return;
        // A grid without tunnels forgets the levels of an earlier grid of the same size
        TwoLevels.Register(new TwoLevels(speed.GetLength(0), speed.GetLength(1)));
        if (string.IsNullOrEmpty(gridName) || !gridName.EndsWith("grid")) return;
        MapDefinition map = null;
        foreach (MapDefinition m in MapDefinition.All)
        {
            if (m != null && m.gridName == gridName) { map = m; break; }
        }
        if (map == null || string.IsNullOrEmpty(map.linesPath)) return;
        List<Tunnel> tunnels = Load(map);
        if (tunnels.Count == 0) return;
        int opened = 0;
        TwoLevels levels = new TwoLevels(speed.GetLength(0), speed.GetLength(1));
        foreach (Tunnel tunnel in tunnels) if (!tunnel.walls) opened += Open(map, speed, tunnel, levels);
        foreach (Tunnel tunnel in tunnels) if (tunnel.walls) opened += Open(map, speed, tunnel, levels);
        TwoLevels.Register(levels);
        Debug.Log($"Tunnels and bridges: {tunnels.Count} on {map.displayName}, {opened} cells opened");
    }

    static List<Tunnel> Load(MapDefinition map)
    {
        if (cache.TryGetValue(map.linesPath, out List<Tunnel> known)) return known;
        List<Tunnel> tunnels = new List<Tunnel>();
        cache[map.linesPath] = tunnels;
        byte[] bytes = MapPackages.LoadBytes(map.linesPath, ".json");
        if (bytes == null) return tunnels;
        List<(Vector2 from, Vector2 to)> sides = new List<(Vector2, Vector2)>();
        List<(Vector2 from, Vector2 to)> bridgeSides = new List<(Vector2, Vector2)>();
        try
        {
            JObject root = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            foreach (JToken feature in root["features"] ?? new JArray())
            {
                string layer = (string)feature["properties"]?["Layer"] ?? "";
                bool tunnel = IsTunnel(layer);
                if (!tunnel && !IsBridge(layer)) continue;
                JArray coords = feature["geometry"]?["coordinates"] as JArray;
                if (coords == null || coords.Count < 2 || !(coords[0] is JArray)) continue;
                Vector2 first = new Vector2((float)coords[0][0], (float)coords[0][1]);
                Vector2 last = new Vector2((float)coords[coords.Count - 1][0], (float)coords[coords.Count - 1][1]);
                if (Vector2.Distance(first, last) < 1f) continue;
                (tunnel ? sides : bridgeSides).Add((first, last));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Tunnels: could not read " + map.linesPath + ": " + e.Message);
            return tunnels;
        }

        Pair(sides, true, tunnels);
        Pair(bridgeSides, false, tunnels);
        return tunnels;
    }

    // The two side lines of a tunnel (or a bridge) run side by side, a few metres apart
    static void Pair(List<(Vector2 from, Vector2 to)> sides, bool walls, List<Tunnel> tunnels)
    {
        bool[] used = new bool[sides.Count];
        for (int i = 0; i < sides.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            Vector2 a1 = sides[i].from, a2 = sides[i].to;
            Vector2 mid = (a1 + a2) / 2f;
            float length = Vector2.Distance(a1, a2);
            int best = -1;
            float bestDistance = Mathf.Min(15f, Mathf.Max(4f, length * 0.8f));
            for (int j = 0; j < sides.Count; j++)
            {
                if (used[j]) continue;
                float d = Vector2.Distance(mid, (sides[j].from + sides[j].to) / 2f);
                if (d < bestDistance) { bestDistance = d; best = j; }
            }
            Tunnel tunnel = new Tunnel { a1 = a1, a2 = a2, walls = walls };
            if (best >= 0)
            {
                used[best] = true;
                Vector2 b1 = sides[best].from, b2 = sides[best].to;
                if (Vector2.Distance(a1, b1) + Vector2.Distance(a2, b2) > Vector2.Distance(a1, b2) + Vector2.Distance(a2, b1))
                {
                    Vector2 t = b1; b1 = b2; b2 = t;
                }
                tunnel.b1 = b1;
                tunnel.b2 = b2;
            }
            else
            {
                // Only one line: a 3 m wide way along it
                Vector2 across = Vector2.Perpendicular((a2 - a1).normalized) * 1.5f;
                tunnel.a1 = a1 + across; tunnel.a2 = a2 + across;
                tunnel.b1 = a1 - across; tunnel.b2 = a2 - across;
            }
            tunnels.Add(tunnel);
        }
    }

    static bool IsTunnel(string layer)
    {
        if (layer.Length < 3) return false;
        string lower = layer.ToLowerInvariant();
        bool named = lower.Contains("tunnel") || lower.Contains("genomgång") || lower.Contains("underpass") || lower.Contains("under mark");
        return named && (lower.StartsWith("512") || lower.StartsWith("518") || lower.StartsWith("531") || lower.StartsWith("532"));
    }

    // 512.1 bridge / footbridge (not the tunnel, 512.2)
    static bool IsBridge(string layer)
    {
        if (!layer.StartsWith("512") || IsTunnel(layer)) return false;
        string lower = layer.ToLowerInvariant();
        return !lower.Contains("tunnel") && !lower.Contains("genomgång");
    }

    // Opens the passage and puts up the walls. Returns how many cells were opened.
    static int Open(MapDefinition map, float[,] speed, Tunnel t, TwoLevels levels)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float metresPerCell = Mathf.Max(0.5f, map.gridSize);
        if (!t.walls)
        {
            // A bridge reaches one cell onto the shore at each end, past the shore line
            Vector2 da = (t.a2 - t.a1).normalized * metresPerCell, db = (t.b2 - t.b1).normalized * metresPerCell;
            t.a1 -= da; t.a2 += da; t.b1 -= db; t.b2 += db;
        }
        float length = Mathf.Max(Vector2.Distance(t.a1, t.a2), Vector2.Distance(t.b1, t.b2));
        float width = Mathf.Max(Vector2.Distance(t.a1, t.b1), Vector2.Distance(t.a2, t.b2));
        int alongSteps = Mathf.Max(2, Mathf.CeilToInt(length / (metresPerCell * 0.4f)));
        int acrossSteps = Mathf.Max(2, Mathf.CeilToInt(width / (metresPerCell * 0.4f)));

        Vector2 At(float along, float across)
        {
            Vector2 a = Vector2.Lerp(t.a1, t.a2, along);
            Vector2 b = Vector2.Lerp(t.b1, t.b2, along);
            return Vector2.LerpUnclamped(a, b, across);
        }
        Vector2Int Cell(Vector2 p) => map.ToCell(new CoursePoint(p.x, p.y));
        bool Inside(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < cols && c.y < rows;

        // Where along the tunnel the ground is closed (the fences, walls and the road edges it goes under)
        float closedFrom = 2f, closedTo = -1f;
        for (int i = 0; i <= alongSteps; i++)
        {
            float along = i / (float)alongSteps;
            for (int k = 1; k < acrossSteps; k++)
            {
                Vector2Int c = Cell(At(along, k / (float)acrossSteps));
                if (!Inside(c) || speed[c.y, c.x] > 0.01f) continue;
                closedFrom = Mathf.Min(closedFrom, along);
                closedTo = Mathf.Max(closedTo, along);
            }
        }
        if (closedTo < 0f) return 0;   // nothing closed under it: it is open already

        // The passage: every cell between the side lines, and on a diagonal step the cell beside it as well, so the
        // way is joined up without cutting corners
        HashSet<Vector2Int> passage = new HashSet<Vector2Int>();
        for (int k = 0; k <= acrossSteps; k++)
        {
            float across = acrossSteps <= 2 ? 0.5f : Mathf.Lerp(0.15f, 0.85f, k / (float)acrossSteps);
            Vector2Int last = new Vector2Int(int.MinValue, 0);
            for (int i = 0; i <= alongSteps; i++)
            {
                Vector2Int c = Cell(At(i / (float)alongSteps, across));
                if (!Inside(c)) continue;
                if (last.x != int.MinValue && last.x != c.x && last.y != c.y) passage.Add(new Vector2Int(last.x, c.y));
                passage.Add(c);
                last = c;
            }
        }

        // The way's own cells (closed before: under the fence, the wall, over the water) and the cells it shares with
        // the ground above or below (the road over a tunnel, the path under a bridge)
        Vector2 CellF(Vector2 p)
        {
            Vector3 w = map.ToWorld(new CoursePoint(p.x, p.y));
            return new Vector2(w.x / map.CellSize + cols / 2f, w.y / map.CellSize + rows / 2f);
        }
        Vector2 from = CellF((t.a1 + t.b1) / 2f), to = CellF((t.a2 + t.b2) / 2f);
        var own = new List<Vector2Int>();
        var shared = new List<Vector2Int>();
        foreach (Vector2Int c in passage)
        {
            if (speed[c.y, c.x] <= 0.01f) own.Add(c);
            else shared.Add(c);
        }
        int opened = 0;
        foreach (Vector2Int c in own)
        {
            speed[c.y, c.x] = 1.2f;
            opened++;
        }
        levels.Add(from, to, own, shared);
        return opened;
    }
}
