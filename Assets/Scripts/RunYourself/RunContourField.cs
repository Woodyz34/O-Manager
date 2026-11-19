using System;
using System.Collections.Generic;
using System.Globalization;
using MapImport;

// The ground for the run-yourself world, made exactly from the map's contours on a 2.5 m grid (2026-11-19, Anders:
// the world should be identical to the OCAD file, every contour seen; flat where there is no contour).
//
// The map import already works the heights out from the contours (ContourHeights: which way is up, the level of
// every contour, the ground smooth in between), but keeps them on a 10 m grid, which rounds off knolls, noses and
// re-entrants. This does the same from the map package's lines.json and points.json at 2.5 m:
//   - contours and index contours get their level by the same least-squares solver as the import,
//   - every contour is fixed at its level on the fine grid and the ground in between is filled smoothly,
//   - form lines then get the half level between the contours round them, are fixed too, and the fill is redone.
// Slope ticks in points.json carry no direction, so a tick on a closed contour marks a hollow and other ticks are
// not used (the import also used their direction); the slope direction spreads from the neighbours as in the import.
// Plain C#, for a background thread. Illern (6.7 x 5.9 km) takes a few seconds; the result is kept per map.
public static class RunContourField
{
    public const float Res = 2.5f;

    public class Line
    {
        public double[] xy;          // metres
        public bool closed, formLine, hollow;
        public double level;         // metres, once solved
        public bool solved;
    }

    class Contour
    {
        public int id;
        public Line line;
        public double[] p;           // cell coordinates
        public int votesUp, votesDown, orientation;
    }

    // The fine heights, or null when the contours can't be solved (then the 10 m grid is used)
    public static TvHeights Build(TvHeights grid, List<Line> lines, List<double[]> ticks, double eq, Action<string> log = null)
    {
        if (grid == null || lines == null || eq <= 0) return null;
        int k = Math.Max(1, (int)Math.Round(grid.cell / Res));
        double res = grid.cell / k;
        int C = (grid.cols - 1) * k + 1, R = (grid.rows - 1) * k + 1;
        if ((long)C * R > 40000000) return null;
        double ox = grid.minx - res * 0.5, oy = grid.miny - res * 0.5;   // cell (0, 0) is centred on minx, miny

        var contours = new List<Contour>();
        foreach (Line l in lines)
        {
            if (l.formLine || l.xy.Length < 4) continue;
            var q = new double[l.xy.Length];
            for (int i = 0; i < q.Length; i += 2) { q[i] = (l.xy[i] - ox) / res; q[i + 1] = (l.xy[i + 1] - oy) / res; }
            contours.Add(new Contour { id = contours.Count + 1, line = l, p = q });
        }
        if (contours.Count < 3) return null;

        var ids = new int[R * C];
        foreach (Contour c in contours)
            for (int i = 0; i + 3 < c.p.Length; i += 2)
                GridBuilder.Supercover(c.p[i], c.p[i + 1], c.p[i + 2], c.p[i + 3], C, R, (x, y) => ids[y * C + x] = c.id);

        // ---- Which way is up: a closed contour is a hill, unless it has a tick (a hollow) ----
        foreach (double[] t in ticks)
        {
            int cid = ContourHeights.Nearest(ids, C, R, (t[0] - ox) / res, (t[1] - oy) / res, 2);
            if (cid == 0 || !contours[cid - 1].line.closed) continue;
            contours[cid - 1].line.hollow = true;
        }
        foreach (Contour c in contours)
        {
            if (c.line.closed)
            {
                // The inside is on the left when the ring runs counter-clockwise
                bool insideLeft = MapGeometry.SignedArea(c.p) > 0;
                bool insideUp = !c.line.hollow;
                if (insideLeft == insideUp) c.votesUp += 3; else c.votesDown += 3;
            }
            c.orientation = c.votesUp > c.votesDown ? 1 : c.votesDown > c.votesUp ? -1 : 0;
        }

        // ---- Neighbours: the first other contour straight out to each side (as ContourHeights) ----
        var seen = new Dictionary<long, int[]>();
        int maxSteps = (int)(200 / res);
        foreach (Contour c in contours)
        {
            double next = 0, spacing = 10 / res;
            for (int i = 0; i + 3 < c.p.Length; i += 2)
            {
                double x0 = c.p[i], y0 = c.p[i + 1], x1 = c.p[i + 2], y1 = c.p[i + 3];
                double len = MapGeometry.Dist(x0, y0, x1, y1);
                if (len < 1e-9) continue;
                double tx = (x1 - x0) / len, ty = (y1 - y0) / len, nx = -ty, ny = tx;
                double s;
                for (s = next; s < len; s += spacing)
                {
                    double px = x0 + tx * s, py = y0 + ty * s;
                    for (int side = 1; side >= -1; side -= 2)
                    {
                        for (double t = 1.5; t < maxSteps; t += 0.5)
                        {
                            int cx = (int)Math.Floor(px + nx * side * t), cy = (int)Math.Floor(py + ny * side * t);
                            if (cx < 0 || cy < 0 || cx >= C || cy >= R) break;
                            int v = ids[cy * C + cx];
                            if (v == 0 || v == c.id) continue;
                            long key = (long)c.id << 32 | (uint)v;
                            if (!seen.TryGetValue(key, out int[] cnt)) seen[key] = cnt = new int[2];
                            cnt[side > 0 ? 0 : 1]++;
                            break;
                        }
                    }
                }
                next = s - len;
            }
        }
        var edges = new List<(int a, int b, int sa, int sb, double w)>();
        foreach (var kv in seen)
        {
            int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
            if (a >= b) continue;
            if (!seen.TryGetValue((long)b << 32 | (uint)a, out int[] back)) continue;
            int sa = kv.Value[0] >= kv.Value[1] ? 1 : -1;
            int sb = back[0] >= back[1] ? 1 : -1;
            double w = Math.Min(Math.Max(kv.Value[0], kv.Value[1]), Math.Max(back[0], back[1]));
            edges.Add((a, b, sa, sb, Math.Min(w, 20)));
        }
        if (edges.Count == 0) return null;
        var adj = new List<int>[contours.Count + 1];
        for (int i = 0; i <= contours.Count; i++) adj[i] = new List<int>();
        for (int e = 0; e < edges.Count; e++) { adj[edges[e].a].Add(e); adj[edges[e].b].Add(e); }

        // Open contours: the slope goes on the same way as next door
        for (int pass = 0; pass < 8; pass++)
        {
            foreach (Contour c in contours)
            {
                if (c.orientation != 0) continue;
                double vote = 0;
                foreach (int e in adj[c.id])
                {
                    var ed = edges[e];
                    int other = ed.a == c.id ? ed.b : ed.a;
                    int mySide = ed.a == c.id ? ed.sa : ed.sb, otherSide = ed.a == c.id ? ed.sb : ed.sa;
                    int o = contours[other - 1].orientation;
                    if (o == 0) continue;
                    vote += ed.w * -o * otherSide * mySide;
                }
                if (vote != 0) c.orientation = vote > 0 ? 1 : -1;
            }
        }
        int known = 0;
        foreach (Contour c in contours) if (c.orientation != 0) known++;
        if (known < contours.Count / 4) return null;

        var diff = new double[edges.Count];
        var weight = new double[edges.Count];
        for (int e = 0; e < edges.Count; e++)
        {
            var ed = edges[e];
            int oa = contours[ed.a - 1].orientation, ob = contours[ed.b - 1].orientation;
            if (oa == 0 || ob == 0) continue;
            diff[e] = (oa * ed.sa - ob * ed.sb) / 2.0;
            weight[e] = ed.w;
        }
        var level = new double[contours.Count + 1];
        var baseWeight = (double[])weight.Clone();
        for (int outer = 0; outer < 5; outer++)
        {
            ContourHeights.SolveLevels(level, edges, diff, weight);
            for (int e = 0; e < edges.Count; e++)
            {
                double r = level[edges[e].b] - level[edges[e].a] - diff[e];
                weight[e] = baseWeight[e] / (1 + (r / 0.5) * (r / 0.5));
            }
        }
        ids = null;

        // ---- The ground: fixed on every contour, smooth in between ----
        var z = new double[R * C];
        var isFixed = new bool[R * C];
        var sum = new double[R * C];
        var count = new int[R * C];
        foreach (Contour c in contours)
        {
            if (adj[c.id].Count == 0) continue;
            c.line.level = Math.Round(level[c.id]) * eq;
            c.line.solved = true;
            Fix(c.p, c.line.level, sum, count, C, R);
        }
        if (!Fill(z, isFixed, sum, count, R, C)) return null;

        // ---- Form lines: half way between the contours round them ----
        int forms = 0;
        foreach (Line l in lines)
        {
            if (!l.formLine || l.xy.Length < 4) continue;
            var q = new double[l.xy.Length];
            double m = 0; int n = 0;
            for (int i = 0; i < q.Length; i += 2)
            {
                q[i] = (l.xy[i] - ox) / res; q[i + 1] = (l.xy[i + 1] - oy) / res;
                int cx = (int)Math.Floor(q[i]), cy = (int)Math.Floor(q[i + 1]);
                if (cx < 0 || cy < 0 || cx >= C || cy >= R) continue;
                m += z[cy * C + cx]; n++;
            }
            if (n == 0) continue;
            l.level = (Math.Floor(m / n / eq) + 0.5) * eq;
            l.solved = true;
            Fix(q, l.level, sum, count, C, R);
            forms++;
        }
        if (forms > 0) Fill(z, isFixed, sum, count, R, C);
        // The fill bends sharply at every fixed line and steps along the grid cells: a light smoothing rounds that
        // off and moves no contour more than a metre or so
        Blur(z, R, C, 2);

        double min = double.MaxValue;
        for (int i = 0; i < z.Length; i++) min = Math.Min(min, z[i]);
        var fine = new TvHeights { cell = (float)res, minx = grid.minx, miny = grid.miny, cols = C, rows = R, h = new float[R * C] };
        for (int i = 0; i < z.Length; i++) fine.h[i] = (float)(z[i] - min);
        foreach (Line l in lines) if (l.solved) l.level -= min;
        log?.Invoke($"{contours.Count} contours ({known} with known slope), {forms} form lines, {edges.Count} pairs, {C}x{R} cells");
        return fine;
    }

    static void Blur(double[] z, int R, int C, int passes)
    {
        var t = new double[z.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int r = 0; r < R; r++)
                for (int c = 0; c < C; c++)
                {
                    int c0 = Math.Max(0, c - 1), c1 = Math.Min(C - 1, c + 1);
                    t[r * C + c] = (z[r * C + c0] + 2 * z[r * C + c] + z[r * C + c1]) * 0.25;
                }
            for (int r = 0; r < R; r++)
            {
                int r0 = Math.Max(0, r - 1) * C, r1 = Math.Min(R - 1, r + 1) * C, rc = r * C;
                for (int c = 0; c < C; c++) z[rc + c] = (t[r0 + c] + 2 * t[rc + c] + t[r1 + c]) * 0.25;
            }
        }
    }

    static void Fix(double[] p, double value, double[] sum, int[] count, int C, int R)
    {
        for (int i = 0; i + 3 < p.Length; i += 2)
            GridBuilder.Supercover(p[i], p[i + 1], p[i + 2], p[i + 3], C, R, (x, y) => { sum[y * C + x] += value; count[y * C + x]++; });
    }

    static bool Fill(double[] z, bool[] isFixed, double[] sum, int[] count, int R, int C)
    {
        double mean = 0; int nf = 0;
        for (int i = 0; i < z.Length; i++)
        {
            isFixed[i] = count[i] > 0;
            if (isFixed[i]) { z[i] = sum[i] / count[i]; mean += z[i]; nf++; }
        }
        if (nf == 0) return false;
        mean /= nf;
        for (int i = 0; i < z.Length; i++) if (!isFixed[i]) z[i] = mean;
        ContourHeights.SolveLaplace(z, isFixed, R, C);
        return true;
    }

    // ---- Reading the package's lines.json and points.json ----

    // Contours (ISOM 101), index contours (102) and form lines (103)
    public static List<Line> ReadLines(byte[] json)
    {
        var list = new List<Line>();
        if (json == null) return list;
        foreach (var (code, xy) in Features(json))
        {
            if (code.Length < 3 || xy.Count < 4) continue;
            string major = code.Substring(0, 3);
            if (major != "101" && major != "102" && major != "103") continue;
            double[] p = xy.ToArray();
            list.Add(new Line { xy = p, closed = MapGeometry.IsClosed(p, 1.5), formLine = major == "103" });
        }
        return list;
    }

    // Slope ticks: points of the contour symbols with a sub-number (101.1)
    public static List<double[]> ReadTicks(byte[] json)
    {
        var list = new List<double[]>();
        if (json == null) return list;
        foreach (var (code, xy) in Features(json))
        {
            if (code.Length < 6 || xy.Count < 2) continue;
            string major = code.Substring(0, 3);
            if ((major == "101" || major == "102") && code.Substring(3, 3) != "000") list.Add(new[] { xy[0], xy[1] });
        }
        return list;
    }

    // Every feature's symbol number ("101000") and coordinates, from the GeoJSON the import writes
    static IEnumerable<(string code, List<double> xy)> Features(byte[] bytes)
    {
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        int at = 0;
        while (true)
        {
            at = text.IndexOf("\"Layer\":\"", at, StringComparison.Ordinal);
            if (at < 0) yield break;
            at += 9;
            int stop = at;
            while (stop < text.Length && text[stop] >= '0' && text[stop] <= '9') stop++;
            string code = text.Substring(at, stop - at);
            int coords = text.IndexOf("\"coordinates\":", at, StringComparison.Ordinal);
            if (coords < 0) yield break;
            int next = text.IndexOf("\"Layer\":\"", at, StringComparison.Ordinal);
            if (next >= 0 && coords > next) continue;
            int end = next >= 0 ? next : text.Length;
            var xy = new List<double>();
            int i = coords + 14;
            while (i < end)
            {
                char ch = text[i];
                if (ch == '-' || ch == '.' || (ch >= '0' && ch <= '9'))
                {
                    int s = i;
                    while (i < end && (text[i] == '-' || text[i] == '.' || text[i] == 'e' || text[i] == 'E' || text[i] == '+' || (text[i] >= '0' && text[i] <= '9'))) i++;
                    if (double.TryParse(text.Substring(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) xy.Add(v);
                }
                else i++;
            }
            at = end;
            yield return (code, xy);
        }
    }
}
