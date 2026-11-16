using System;
using System.Collections.Generic;
using MapImport;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Gaps in lines nobody can cross (2026-11-15): an opening in a fence or a wall, the gap between two cliffs.
//
// The grid closes every cell a blocking line runs through. Where two such lines end a few metres from each other the
// map shows a way through, but on a 10 m forest grid (and often on a 2 m sprint grid) the two ends close neighbouring
// cells, and the gap was gone. Here, when a map's grid is loaded, every end of a blocking line that stops a little short
// of another blocking line gets a way through that gap: the cells across the gap that only the lines closed are opened
// again (cells closed by an area, a lake or a house, stay closed).
public static class Passages
{
    struct Gap
    {
        public Vector2 middle;   // the middle of the gap
        public Vector2 across;   // unit vector across the line of the barrier, the way a runner goes through
    }

    static readonly Dictionary<string, List<Gap>> cache = new Dictionary<string, List<Gap>>();
    static readonly Dictionary<string, List<Vector2[]>> lineCache = new Dictionary<string, List<Vector2[]>>();
    static readonly HashSet<Vector2[]> trimmable = new HashSet<Vector2[]>();   // lines whose end cells StubCells may open

    // gridName is "<package root>grid"; the map objects are in "<package root>lines"
    public static void Apply(string gridName, float[,] speed)
    {
        if (speed == null || string.IsNullOrEmpty(gridName) || !gridName.EndsWith("grid")) return;
        MapDefinition map = null;
        foreach (MapDefinition m in MapDefinition.All)
        {
            if (m != null && m.gridName == gridName) { map = m; break; }
        }
        if (map == null || string.IsNullOrEmpty(map.linesPath)) return;
        bool sprint = map.gridSize < 5f;
        List<Vector2[]> lines = Lines(map, sprint);
        if (lines.Count == 0) return;
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        // The cells the blocking lines run through: only these are opened again
        HashSet<Vector2Int> lineCells = LineCells(map, sprint);

        int stubs = 0;
        if (!sprint)
        {
            foreach (Vector2Int c in StubCells(map, lines))
            {
                if (c.x >= 0 && c.y >= 0 && c.x < cols && c.y < rows) stubs += OpenCell(speed, c, lineCells);
            }
            if (stubs > 0) Debug.Log($"Passages: {stubs} cells opened where a cliff or fence only just reaches into them on {map.displayName}");
        }

        List<Gap> gaps = Gaps(map, lines, sprint);
        if (gaps.Count == 0) return;

        int opened = 0;
        float reach = map.gridSize * 1.6f;
        foreach (Gap gap in gaps)
        {
            // Only where there is ground to run on at both sides of the gap (not a gap at the edge of a lake)
            if (!OpenBeyond(map, speed, gap, reach) || !OpenBeyond(map, speed, gap, -reach)) continue;
            int n = Mathf.Max(2, Mathf.CeilToInt(2f * reach / (map.gridSize * 0.4f)));
            Vector2Int last = new Vector2Int(int.MinValue, 0);
            for (int k = 0; k <= n; k++)
            {
                Vector2Int c = Cell(map, gap.middle + gap.across * Mathf.Lerp(-reach, reach, k / (float)n));
                if (c.x < 0 || c.y < 0 || c.x >= cols || c.y >= rows) continue;
                // A diagonal step goes through the cell beside it as well (no corner-cutting in the pathfinding)
                if (last.x != int.MinValue && last.x != c.x && last.y != c.y) opened += OpenCell(speed, new Vector2Int(last.x, c.y), lineCells);
                opened += OpenCell(speed, c, lineCells);
                last = c;
            }
        }
        Debug.Log($"Passages: {gaps.Count} gaps in fences, walls and cliffs on {map.displayName}, {opened} cells opened");
    }

    static readonly Dictionary<string, HashSet<Vector2Int>> lineCellCache = new Dictionary<string, HashSet<Vector2Int>>();

    // Every grid cell a blocking line (fence, wall, cliff ...) runs through
    public static HashSet<Vector2Int> LineCells(MapDefinition map, bool sprint)
    {
        string key = map.linesPath + (sprint ? "|s" : "|f");
        if (lineCellCache.TryGetValue(key, out HashSet<Vector2Int> known)) return known;
        var cells = new HashSet<Vector2Int>();
        lineCellCache[key] = cells;
        if (string.IsNullOrEmpty(map.linesPath)) return cells;
        float step = Mathf.Max(0.25f, map.gridSize * 0.25f);
        foreach (Vector2[] line in Lines(map, sprint))
        {
            for (int i = 0; i + 1 < line.Length; i++)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(line[i], line[i + 1]) / step));
                for (int k = 0; k <= n; k++) cells.Add(Cell(map, Vector2.Lerp(line[i], line[i + 1], k / (float)n)));
            }
        }
        return cells;
    }

    static Vector2Int Cell(MapDefinition map, Vector2 p) => map.ToCell(new CoursePoint(p.x, p.y));

    static readonly Dictionary<string, List<Vector2Int>> stubCache = new Dictionary<string, List<Vector2Int>>();

    // Forest maps (2026-11-16): the import closes every 10 m cell a blocking line touches, so a cliff that ends just
    // inside a cell closes the whole cell, and the gap beside the cliff's end was gone (Illern: a cliff reaching 0.7 m
    // into a cell shut the way between it and the next cliff). The end cells of blocking lines that hold less than
    // StubShare of a cell of line (all blocking lines counted) are opened again; the barrier is at most that much shorter.
    // An end that touches another blocking line (two fence pieces joined, a T) is left alone, or the fence would leak.
    const float StubShare = 0.3f;

    static List<Vector2Int> StubCells(MapDefinition map, List<Vector2[]> lines)
    {
        if (stubCache.TryGetValue(map.linesPath, out List<Vector2Int> known)) return known;
        var stubs = new List<Vector2Int>();
        stubCache[map.linesPath] = stubs;
        // Metres of blocking line in every cell, and the line pieces in it
        var length = new Dictionary<Vector2Int, float>();
        var pieces = new Dictionary<Vector2Int, List<(int line, int piece)>>();
        float step = Mathf.Max(0.25f, map.gridSize * 0.05f);
        for (int l = 0; l < lines.Count; l++)
        {
            Vector2[] line = lines[l];
            for (int i = 0; i + 1 < line.Length; i++)
            {
                float d = Vector2.Distance(line[i], line[i + 1]);
                if (d < 1e-4f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt(d / step));
                for (int k = 0; k < n; k++)
                {
                    Vector2Int c = Cell(map, Vector2.Lerp(line[i], line[i + 1], (k + 0.5f) / n));
                    length.TryGetValue(c, out float sum);
                    length[c] = sum + d / n;
                    if (!pieces.TryGetValue(c, out var list)) pieces[c] = list = new List<(int, int)>();
                    if (list.Count == 0 || list[list.Count - 1] != (l, i)) list.Add((l, i));
                }
            }
        }
        float most = map.gridSize * StubShare;
        const float touching = 2.5f;
        var done = new HashSet<Vector2Int>();
        var bad = new HashSet<Vector2Int>();
        for (int l = 0; l < lines.Count; l++)
        {
            Vector2[] line = lines[l];
            if (Vector2.Distance(line[0], line[line.Length - 1]) < 0.01f) continue;   // a closed ring has no ends
            foreach (Vector2 end in new[] { line[0], line[line.Length - 1] })
            {
                Vector2Int c = Cell(map, end);
                if (!trimmable.Contains(line)) { bad.Add(c); continue; }
                if (!length.TryGetValue(c, out float metres) || metres >= most) continue;
                bool joined = false;
                for (int dx = -1; dx <= 1 && !joined; dx++)
                {
                    for (int dy = -1; dy <= 1 && !joined; dy++)
                    {
                        if (!pieces.TryGetValue(new Vector2Int(c.x + dx, c.y + dy), out var list)) continue;
                        foreach ((int other, int piece) in list)
                        {
                            if (other == l) continue;
                            Vector2[] o = lines[other];
                            if (Vector2.Distance(end, NearestOnPiece(end, o[piece], o[piece + 1])) < touching) { joined = true; break; }
                        }
                    }
                }
                if (joined) bad.Add(c);
                else done.Add(c);
            }
        }
        foreach (Vector2Int c in done) if (!bad.Contains(c)) stubs.Add(c);
        return stubs;
    }

    static bool OpenBeyond(MapDefinition map, float[,] speed, Gap gap, float reach)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        for (int k = 0; k < 3; k++)
        {
            float s = reach + Mathf.Sign(reach) * map.gridSize * 0.6f * k;
            Vector2Int c = Cell(map, gap.middle + gap.across * s);
            if (c.x >= 0 && c.y >= 0 && c.x < cols && c.y < rows && speed[c.y, c.x] > 0.01f) return true;
        }
        return false;
    }

    static int OpenCell(float[,] speed, Vector2Int c, HashSet<Vector2Int> lineCells)
    {
        if (speed[c.y, c.x] > 0.01f || !lineCells.Contains(c)) return 0;
        // As quick as the open ground round it
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float sum = 0f;
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = c.x + dx, y = c.y + dy;
                if (x < 0 || y < 0 || x >= cols || y >= rows || speed[y, x] <= 0.01f) continue;
                sum += speed[y, x];
                count++;
            }
        }
        speed[c.y, c.x] = count > 0 ? Mathf.Clamp(sum / count, 0.3f, 1.5f) : 1f;
        return 1;
    }

    // The blocking lines of the map (cliffs, walls, fences, building outlines ... as the import reads them)
    static List<Vector2[]> Lines(MapDefinition map, bool sprint)
    {
        if (lineCache.TryGetValue(map.linesPath, out List<Vector2[]> known)) return known;
        List<Vector2[]> lines = new List<Vector2[]>();
        lineCache[map.linesPath] = lines;
        byte[] bytes = MapPackages.LoadBytes(map.linesPath, ".json");
        if (bytes == null) return lines;
        var blocking = new Dictionary<string, bool>();
        try
        {
            JObject root = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            foreach (JToken feature in root["features"] ?? new JArray())
            {
                string layer = (string)feature["properties"]?["Layer"] ?? "";
                if (!blocking.TryGetValue(layer, out bool blocks))
                {
                    blocks = Blocks(layer, sprint);
                    blocking[layer] = blocks;
                }
                if (!blocks) continue;
                JToken geometry = feature["geometry"];
                string type = (string)geometry?["type"] ?? "";
                JArray coords = geometry?["coordinates"] as JArray;
                if (coords == null) continue;
                int before = lines.Count;
                if (type == "LineString") AddLine(lines, coords);
                else if (type == "MultiLineString") foreach (JToken part in coords) AddLine(lines, part as JArray);
                // Cliffs (2xx), walls and fences (5xx, not building outlines): the ends that may be trimmed.
                // Lake shores, vegetation edges and buildings keep their cells.
                bool trim = layer.StartsWith("2") || (layer.StartsWith("5") && !layer.StartsWith("521"));
                for (int k = before; k < lines.Count; k++) if (trim) trimmable.Add(lines[k]);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Passages: could not read " + map.linesPath + ": " + e.Message);
        }
        return lines;
    }

    static void AddLine(List<Vector2[]> lines, JArray coords)
    {
        if (coords == null || coords.Count < 2 || !(coords[0] is JArray)) return;
        var points = new Vector2[coords.Count];
        for (int i = 0; i < coords.Count; i++) points[i] = new Vector2((float)coords[i][0], (float)coords[i][1]);
        lines.Add(points);
    }

    // "516001 Staket / stängsel": the import's own rule for the symbol says if nobody can cross it
    static bool Blocks(string layer, bool sprint)
    {
        if (layer.Length < 6 || !int.TryParse(layer.Substring(0, 6), out int number)) return false;
        var symbol = new MapSymbol
        {
            major = number / 1000,
            minor = number % 1000,
            name = layer.Length > 7 ? layer.Substring(7) : "",
            kind = SymbolKind.Line,
        };
        symbol.code = symbol.major + (symbol.minor > 0 ? "." + symbol.minor : "");
        try
        {
            return SymbolRules.For(symbol, sprint).role == TerrainRole.BlockingLine;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // The ends of blocking lines that stop a little short of another blocking line
    static List<Gap> Gaps(MapDefinition map, List<Vector2[]> lines, bool sprint)
    {
        string key = map.linesPath + (sprint ? "|s" : "|f");
        if (cache.TryGetValue(key, out List<Gap> known)) return known;
        var gaps = new List<Gap>();
        cache[key] = gaps;
        // Narrower than this is drawn as a closed line (the map maker leaves at least about 0.3 mm on the paper);
        // wider than this is already a gap in the grid
        float minGap = sprint ? 1f : 2.5f;
        float maxGap = map.gridSize * 3f;

        // The line pieces in buckets, so each end only looks at the pieces near it
        float bucket = Mathf.Max(maxGap, 10f);
        var buckets = new Dictionary<Vector2Int, List<(int line, int piece)>>();
        for (int l = 0; l < lines.Count; l++)
        {
            Vector2[] line = lines[l];
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 lo = Vector2.Min(line[i], line[i + 1]), hi = Vector2.Max(line[i], line[i + 1]);
                for (int bx = Mathf.FloorToInt(lo.x / bucket); bx <= Mathf.FloorToInt(hi.x / bucket); bx++)
                {
                    for (int by = Mathf.FloorToInt(lo.y / bucket); by <= Mathf.FloorToInt(hi.y / bucket); by++)
                    {
                        var b = new Vector2Int(bx, by);
                        if (!buckets.TryGetValue(b, out var list)) buckets[b] = list = new List<(int, int)>();
                        list.Add((l, i));
                    }
                }
            }
        }

        for (int l = 0; l < lines.Count; l++)
        {
            Vector2[] line = lines[l];
            if (Vector2.Distance(line[0], line[line.Length - 1]) < 0.01f) continue;   // a closed ring has no ends
            foreach (Vector2 end in new[] { line[0], line[line.Length - 1] })
            {
                float best = float.MaxValue;
                Vector2 nearest = end;
                int bx0 = Mathf.FloorToInt(end.x / bucket), by0 = Mathf.FloorToInt(end.y / bucket);
                for (int bx = bx0 - 1; bx <= bx0 + 1; bx++)
                {
                    for (int by = by0 - 1; by <= by0 + 1; by++)
                    {
                        if (!buckets.TryGetValue(new Vector2Int(bx, by), out var list)) continue;
                        foreach ((int other, int piece) in list)
                        {
                            if (other == l) continue;
                            Vector2[] o = lines[other];
                            Vector2 p = NearestOnPiece(end, o[piece], o[piece + 1]);
                            float d = Vector2.Distance(end, p);
                            if (d < best) { best = d; nearest = p; }
                        }
                    }
                }
                // Touching another line (a corner) or no line near: no gap
                if (best < minGap || best > maxGap) continue;
                Vector2 along = (nearest - end) / best;
                var gap = new Gap { middle = (end + nearest) / 2f, across = new Vector2(-along.y, along.x) };
                // The same gap seen from both its ends
                bool twice = false;
                foreach (Gap g in gaps) if (Vector2.Distance(g.middle, gap.middle) < minGap * 0.5f) { twice = true; break; }
                if (!twice) gaps.Add(gap);
            }
        }
        return gaps;
    }

    static Vector2 NearestOnPiece(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len = ab.sqrMagnitude;
        if (len < 1e-6f) return a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
        return a + ab * t;
    }
}
