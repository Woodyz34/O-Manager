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
//
// Multi-level maps (2026-11-25, the Italian "ML test" map): the entrances to the lower level are drawn as lines of
// small triangles ("tags", "Ingresso multilivello"), with the triangles on the left of the line pointing out of the
// covered part. They were read as bridges, so every tag line opened a way through the walls under it, and the routes
// jumped down from the upper level wherever there were tags. Now:
//  - a tag line is a level edge nobody can cross (only stairs go through it);
//  - where tags close the end of a tunnel (its two dashed side lines, or two tag lines facing each other across a
//    covered passage), the tunnel is a way through them: in at one end, straight through, out at the other, never out
//    to the side and never down from the road above (TwoLevels). Who stays on top crosses over it as before.
public static class Tunnels
{
    struct Tunnel
    {
        public Vector2 a1, a2, b1, b2;   // side line a from a1 to a2, side line b from b1 (near a1) to b2 (near a2)
        public bool walls;               // a tunnel (true) or a bridge
        public bool across;              // forest map brackets: the way goes from one bracket to the other (see Oriented)
        public bool tagged;              // multi-level tags close its ends: open the way right through them
    }

    struct Tag
    {
        public Vector2 from, to;         // the line; its triangles are on the left, pointing out of the covered part
        public Vector2[] points;         // the whole line (a long level edge bends)
        public bool mouth;               // closes the end of a tunnel
    }

    static readonly Dictionary<string, List<Tunnel>> cache = new Dictionary<string, List<Tunnel>>();
    static readonly Dictionary<string, List<Tag>> tagCache = new Dictionary<string, List<Tag>>();
    static readonly Dictionary<string, List<Vector2[]>> stairsCache = new Dictionary<string, List<Vector2[]>>();

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
        List<Tag> tags = tagCache.TryGetValue(map.linesPath, out List<Tag> t) ? t : new List<Tag>();
        if (tunnels.Count == 0 && tags.Count == 0) return;
        // The level edges first, so the tunnels through them are opened again as their own way below
        int shut = tags.Count > 0 ? CloseTags(map, speed, tags) : 0;
        int opened = 0;
        TwoLevels levels = new TwoLevels(speed.GetLength(0), speed.GetLength(1));
        foreach (Tunnel tunnel in tunnels) if (!tunnel.walls) opened += Open(map, speed, tunnel, levels);
        foreach (Tunnel tunnel in tunnels) if (tunnel.walls) opened += Open(map, speed, tunnel, levels);
        TwoLevels.Register(levels);
        Debug.Log($"Tunnels and bridges: {tunnels.Count} on {map.displayName}, {opened} cells opened"
            + (tags.Count > 0 ? $"; {tags.Count} multi-level tag lines, {shut} cells closed" : ""));
    }

    static List<Tunnel> Load(MapDefinition map)
    {
        if (cache.TryGetValue(map.linesPath, out List<Tunnel> known)) return known;
        List<Tunnel> tunnels = new List<Tunnel>();
        List<Tag> tags = new List<Tag>();
        List<Vector2[]> stairs = new List<Vector2[]>();
        cache[map.linesPath] = tunnels;
        tagCache[map.linesPath] = tags;
        stairsCache[map.linesPath] = stairs;
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
                bool tag = IsTag(layer);
                bool stair = !tag && IsStairs(layer);
                bool tunnel = !tag && IsTunnel(layer);
                if (!tag && !stair && !tunnel && !IsBridge(layer)) continue;
                JArray coords = feature["geometry"]?["coordinates"] as JArray;
                if (coords == null || coords.Count < 2 || !(coords[0] is JArray)) continue;
                if (tag || stair)
                {
                    Vector2[] line = new Vector2[coords.Count];
                    for (int i = 0; i < coords.Count; i++) line[i] = new Vector2((float)coords[i][0], (float)coords[i][1]);
                    if (stair) stairs.Add(line);
                    else tags.Add(new Tag { from = line[0], to = line[line.Length - 1], points = line });
                    continue;
                }
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

        // (on a multi-level map a long lone dashed line is the edge of a roof or deck overhead, not a tunnel)
        Pair(sides, true, tunnels, !map.IsSprint, map.IsSprint && tags.Count > 0);
        Pair(bridgeSides, false, tunnels, false, false);
        if (tags.Count > 0) TagTunnels(tunnels, tags);
        return tunnels;
    }

    // The two side lines of a tunnel (or a bridge) run side by side, a few metres apart
    // brackets: a forest map's tunnel (ISOM 512), drawn as two short brackets on either side of the road above it
    // multiLevel: a multi-level sprint map, where a lone dashed line longer than LoneMax is the edge of something
    // overhead, not a tunnel (elsewhere lone lines keep their 3 m way: Bergen's X-shaped underpass is four lone curves)
    const float LoneMax = 12f;

    static void Pair(List<(Vector2 from, Vector2 to)> sides, bool walls, List<Tunnel> tunnels, bool brackets, bool multiLevel)
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
            // A forest map (ISOM 512) draws the two short brackets on either side of the road the tunnel goes under, as
            // far apart as the road is wide, often further than they are long (2026-11-20, Illern: 7 m brackets 21 m
            // apart): look up to 30 m away for a bracket running the same way, straight across from this one
            if (brackets) bestDistance = 30f;
            // A wide underpass has its side lines further apart than that (2026-11-25, Bergen: 16 m side lines 15 m
            // apart): two lines running the same way, mostly side by side, up to wideMax apart
            float wideMax = Mathf.Min(25f, Mathf.Max(4f, length * 1.2f));
            float bestScore = float.MaxValue;
            Vector2 way = (a2 - a1).normalized;
            for (int j = 0; j < sides.Count; j++)
            {
                if (used[j]) continue;
                Vector2 otherMid = (sides[j].from + sides[j].to) / 2f;
                float d = Vector2.Distance(mid, otherMid);
                float score = float.MaxValue;
                if (brackets)
                {
                    Vector2 other = (sides[j].to - sides[j].from).normalized;
                    if (Mathf.Abs(Vector2.Dot(way, other)) < 0.7f) continue;
                    if (d > 0.01f && Mathf.Abs(Vector2.Dot(way, (otherMid - mid) / d)) > 0.75f) continue;
                    if (d < bestDistance) score = d;
                }
                else if (d < bestDistance) score = d;
                else if (length <= 30f && SideBySide(sides[i], sides[j], out float apart) && apart <= wideMax)
                {
                    // (only short lines of about the same length: the two edges of a roof along a street are not a tunnel)
                    float otherLength = Vector2.Distance(sides[j].from, sides[j].to);
                    if (otherLength <= 30f && Mathf.Min(length, otherLength) >= 0.7f * Mathf.Max(length, otherLength))
                        score = bestDistance + apart;   // after any pair the plain rule finds
                }
                if (score < bestScore) { bestScore = score; best = j; }
            }
            Tunnel tunnel = new Tunnel { a1 = a1, a2 = a2, walls = walls, across = brackets };
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
                // A long lone dashed line on a sprint map is the edge of a roof or a deck overhead (2026-11-25, ML
                // test: a 70 m line along an arcade): a 3 m way along it ran through the walls beside it
                if (multiLevel && length > LoneMax) continue;
                // Only one line: a 3 m wide way along it
                tunnel.across = false;
                Vector2 across = Vector2.Perpendicular((a2 - a1).normalized) * 1.5f;
                tunnel.a1 = a1 + across; tunnel.a2 = a2 + across;
                tunnel.b1 = a1 - across; tunnel.b2 = a2 - across;
            }
            tunnels.Add(tunnel);
        }
    }

    // Two lines running the same way (within about 30 degrees), side by side for at least half the shorter one;
    // apart = how far the middle of the second is from the first line, straight across
    static bool SideBySide((Vector2 from, Vector2 to) a, (Vector2 from, Vector2 to) b, out float apart)
    {
        apart = float.MaxValue;
        Vector2 da = a.to - a.from, db = b.to - b.from;
        float la = da.magnitude, lb = db.magnitude;
        if (la < 0.5f || lb < 0.5f) return false;
        Vector2 u = da / la;
        if (Mathf.Abs(Vector2.Dot(u, db / lb)) < 0.85f) return false;
        float s0 = Vector2.Dot(b.from - a.from, u), s1 = Vector2.Dot(b.to - a.from, u);
        float overlap = Mathf.Min(la, Mathf.Max(s0, s1)) - Mathf.Max(0f, Mathf.Min(s0, s1));
        if (overlap < 0.5f * Mathf.Min(la, lb)) return false;
        Vector2 n = Vector2.Perpendicular(u);
        apart = Mathf.Abs(Vector2.Dot((b.from + b.to) / 2f - a.from, n));
        return true;
    }

    // ---- Multi-level tags ----

    // Tag lines at the ends of a tunnel are its mouths: the tunnel gets the way right through them. Tag lines facing
    // each other across a covered passage (no dashed side lines) make a tunnel of their own.
    static void TagTunnels(List<Tunnel> tunnels, List<Tag> tags)
    {
        for (int i = 0; i < tunnels.Count; i++)
        {
            Tunnel t = tunnels[i];
            if (!t.walls || t.a1 == t.b1) continue;
            bool first = MouthAt(tags, t.a1, t.b1, t.a2 - t.a1, out float pastFirst);
            bool last = MouthAt(tags, t.a2, t.b2, t.a1 - t.a2, out float pastLast);
            if (!first && !last) continue;
            t.tagged = true;
            // Reach a little past the tags, so the way goes right through the cells they close
            Vector2 dir = ((t.a2 + t.b2) - (t.a1 + t.b1)).normalized;
            float reach = 1.5f;
            if (first) { t.a1 -= dir * (pastFirst + reach); t.b1 -= dir * (pastFirst + reach); }
            if (last) { t.a2 += dir * (pastLast + reach); t.b2 += dir * (pastLast + reach); }
            tunnels[i] = t;
        }

        // Two tag lines facing each other: each with its triangles pointing away from the other
        for (int i = 0; i < tags.Count; i++)
        {
            Tag a = tags[i];
            if (a.mouth) continue;
            float la = Vector2.Distance(a.from, a.to);
            if (la < 1.5f || la > 40f) continue;
            Vector2 ua = (a.to - a.from) / la;
            Vector2 outA = Vector2.Perpendicular(ua);   // left of the line: where its triangles point
            Vector2 midA = (a.from + a.to) / 2f;
            int best = -1;
            float bestApart = 60f;
            for (int j = 0; j < tags.Count; j++)
            {
                if (j == i || tags[j].mouth) continue;
                Tag b = tags[j];
                float lb = Vector2.Distance(b.from, b.to);
                if (lb < 1.5f || lb > 40f || Mathf.Min(la, lb) < 0.5f * Mathf.Max(la, lb)) continue;
                if (!SideBySide((a.from, a.to), (b.from, b.to), out float apart) || apart < 2f || apart >= bestApart) continue;
                Vector2 midB = (b.from + b.to) / 2f;
                Vector2 ub = (b.to - b.from) / lb;
                // b lies behind a (opposite to a's triangles), and b's triangles point away from a
                if (Vector2.Dot(midB - midA, outA) > -1f) continue;
                if (Vector2.Dot(midA - midB, Vector2.Perpendicular(ub)) > -1f) continue;
                best = j;
                bestApart = apart;
            }
            if (best < 0) continue;
            Tag other = tags[best];
            Vector2 b1 = other.from, b2 = other.to;
            if (Vector2.Distance(a.from, b1) > Vector2.Distance(a.from, b2)) { Vector2 s = b1; b1 = b2; b2 = s; }
            // The way runs from tag a to tag b: side line one from a.from to b1, side line two from a.to to b2
            Vector2 dir = ((b1 + b2) - (a.from + a.to)).normalized * 1.5f;
            tunnels.Add(new Tunnel { a1 = a.from - dir, a2 = b1 + dir, b1 = a.to - dir, b2 = b2 + dir, walls = true, tagged = true });
            a.mouth = true;
            other.mouth = true;
            tags[i] = a;
            tags[best] = other;
        }
    }

    // A tag line across the end of a tunnel (from side line point p to side line point q, the tunnel going on
    // 'inward' from there). past = how far beyond the end, outward, the tag line lies.
    static bool MouthAt(List<Tag> tags, Vector2 p, Vector2 q, Vector2 inward, out float past)
    {
        past = 0f;
        Vector2 end = (p + q) / 2f;
        float width = Vector2.Distance(p, q);
        Vector2 across = width > 0.01f ? (q - p) / width : Vector2.zero;
        Vector2 inDir = inward.normalized;
        for (int i = 0; i < tags.Count; i++)
        {
            Tag tag = tags[i];
            float length = Vector2.Distance(tag.from, tag.to);
            if (length < 0.5f || length > Mathf.Max(8f, width * 3f)) continue;
            Vector2 u = (tag.to - tag.from) / length;
            if (across != Vector2.zero && Mathf.Abs(Vector2.Dot(u, across)) < 0.7f) continue;
            // Its nearest point to the end of the tunnel, close beside it
            Vector2 near = Nearest(tag.from, tag.to, end);
            float off = Vector2.Distance(near, end);
            if (off > Mathf.Max(3f, width * 0.6f)) continue;
            float outward = -Vector2.Dot((tag.from + tag.to) / 2f - end, inDir);
            if (outward < -3f) continue;   // well inside the tunnel: not its end
            past = Mathf.Max(past, outward);
            tag.mouth = true;
            tags[i] = tag;
            return true;
        }
        return false;
    }

    static Vector2 Nearest(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return a + ab * t;
    }

    // Every cell a tag line runs through is closed (a level edge: no stepping off it, no jumping down), but not where
    // stairs cross it. A tunnel's mouth is opened again by Open, as the tunnel's own way.
    static int CloseTags(MapDefinition map, float[,] speed, List<Tag> tags)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        HashSet<Vector2Int> stairCells = new HashSet<Vector2Int>();
        if (stairsCache.TryGetValue(map.linesPath, out List<Vector2[]> stairs))
        {
            foreach (Vector2[] line in stairs)
                for (int i = 0; i + 1 < line.Length; i++) Cover(CellF(map, cols, rows, line[i]), CellF(map, cols, rows, line[i + 1]), stairCells);
        }
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        foreach (Tag tag in tags)
            for (int i = 0; i + 1 < tag.points.Length; i++) Cover(CellF(map, cols, rows, tag.points[i]), CellF(map, cols, rows, tag.points[i + 1]), cells);
        int shut = 0;
        foreach (Vector2Int c in cells)
        {
            if (c.x < 0 || c.y < 0 || c.x >= cols || c.y >= rows || stairCells.Contains(c)) continue;
            if (speed[c.y, c.x] <= 0.01f) continue;
            speed[c.y, c.x] = 0f;
            shut++;
        }
        return shut;
    }

    // Map metres to cell coordinates (cell (x, y) has its centre at x + 0.5, y + 0.5)
    static Vector2 CellF(MapDefinition map, int cols, int rows, Vector2 p)
    {
        Vector3 w = map.ToWorld(new CoursePoint(p.x, p.y));
        return new Vector2(w.x / map.CellSize + cols / 2f, w.y / map.CellSize + rows / 2f);
    }

    // Every cell a line (in cell coordinates) touches, and on a diagonal step both cells beside it, so nothing slips
    // through at a corner
    static void Cover(Vector2 a, Vector2 b, HashSet<Vector2Int> cells)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 4f));
        Vector2Int last = new Vector2Int(int.MinValue, 0);
        for (int s = 0; s <= steps; s++)
        {
            Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
            Vector2Int c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
            if (c == last) continue;
            if (last.x != int.MinValue && last.x != c.x && last.y != c.y)
            {
                cells.Add(new Vector2Int(last.x, c.y));
                cells.Add(new Vector2Int(c.x, last.y));
            }
            cells.Add(c);
            last = c;
        }
    }

    // ISSprOM 2019-2 has no symbol for it; maps that show several levels draw their own: "Ingresso multilivello",
    // "Multi-level entrance", "Flernivå" ... (SymbolRules.MultiLevelTag)
    static bool IsTag(string layer) => layer.Length >= 3 && MapImport.SymbolRules.MultiLevelTag(layer.ToLowerInvariant());

    // Stairs by name ("Trapp", "Stairway", "Scalette"; ML test: "Step or edge of paved area, narrow stairway"), or a
    // 532 line that is not the edge of a paved area
    static bool IsStairs(string layer)
    {
        string lower = layer.ToLowerInvariant();
        if (lower.Contains("kontur")) return false;   // "Konturlinje och trappa": every paved edge of the map
        if (lower.Contains("trapp") || lower.Contains("stair") || lower.Contains("scalett") || lower.Contains("scalinat")) return true;
        return lower.StartsWith("532") && !lower.Contains("kontur") && !lower.Contains("edge") && !lower.Contains("outline");
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
        if (!layer.StartsWith("512") || IsTunnel(layer) || IsTag(layer)) return false;
        string lower = layer.ToLowerInvariant();
        return !lower.Contains("tunnel") && !lower.Contains("genomgång");
    }

    // Which way a tunnel goes. A sprint map draws its two side lines along it (ISSprOM 512.2); a forest map draws two
    // short brackets across it, one each side of the road above (ISOM 512, the picture in the standard: a path and a
    // stream under a road). Read the wrong way round, the way ran along the road, and a path through the tunnel
    // came in from its side, which the two levels do not allow, so the route could not be drawn through (2026-11-20).
    // The right way is the one with open ground at both ends and closed ground (or the road) in the middle.
    // Sprint maps keep their side lines as they are; forest brackets are turned, unless the ground clearly says the
    // way runs along them (open at both ends that way, not the other).
    static Tunnel Oriented(MapDefinition map, float[,] speed, Tunnel t)
    {
        if (!t.across) return t;
        Tunnel across = new Tunnel { a1 = t.a1, a2 = t.b1, b1 = t.a2, b2 = t.b2, walls = t.walls };
        int keep = EndScore(map, speed, t), turn = EndScore(map, speed, across);
        return keep > turn + 1 ? t : across;
    }

    // 2 for each end of the way with ground a runner can stand on just beyond it, and 1 when the middle is closed
    static int EndScore(MapDefinition map, float[,] speed, Tunnel t)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float metresPerCell = Mathf.Max(0.5f, map.gridSize);
        bool Open(Vector2 p)
        {
            Vector2Int c = map.ToCell(new CoursePoint(p.x, p.y));
            return c.x >= 0 && c.y >= 0 && c.x < cols && c.y < rows && speed[c.y, c.x] > 0.01f;
        }
        Vector2 start = (t.a1 + t.b1) / 2f, end = (t.a2 + t.b2) / 2f;
        Vector2 dir = end - start;
        float length = dir.magnitude;
        if (length < 0.01f) return 0;
        dir /= length;
        int score = 0;
        // Just beyond each end, a cell or two out
        for (int k = 1; k <= 2; k++)
        {
            if (Open(start - dir * metresPerCell * k)) { score += 2; break; }
        }
        for (int k = 1; k <= 2; k++)
        {
            if (Open(end + dir * metresPerCell * k)) { score += 2; break; }
        }
        bool closedInside = false;
        for (int i = 1; i < 8 && !closedInside; i++)
        {
            if (!Open(Vector2.Lerp(start, end, i / 8f))) closedInside = true;
        }
        if (closedInside) score += 1;
        return score;
    }

    // Opens the passage and puts up the walls. Returns how many cells were opened.
    static int Open(MapDefinition map, float[,] speed, Tunnel t, TwoLevels levels)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float metresPerCell = Mathf.Max(0.5f, map.gridSize);
        if (t.b1 != t.b2) t = Oriented(map, speed, t);
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
        levels.Add(from, to, own, shared, t.tagged);
        return opened;
    }
}
