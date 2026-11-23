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
// Slope ticks in points.json carry their direction ("Down") on maps imported from 2026-11-23; on older maps they carry
// none, so a tick on a closed contour marks a hollow and the import's 10 m heights help; the slope direction spreads
// from the neighbours as in the import.
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
        // A tick with its direction (maps imported from 2026-11-23) says for certain which side is down
        var directed = new bool[contours.Count + 1];
        foreach (double[] t in ticks)
        {
            if (t.Length < 5 || t[2] > 0 || double.IsNaN(t[3])) continue;
            double px = (t[0] - ox) / res, py = (t[1] - oy) / res;
            int cid = ContourHeights.Nearest(ids, C, R, px, py, 2);
            if (cid == 0) continue;
            int side = ContourHeights.SideOf(contours[cid - 1].p, px + t[3] * 0.8, py + t[4] * 0.8);
            if (side > 0) contours[cid - 1].votesDown += 5;   // the tick's side is downhill
            else if (side < 0) contours[cid - 1].votesUp += 5;
            directed[cid] = true;
        }
        foreach (Contour c in contours)
        {
            if (!directed[c.id] || !c.line.closed || c.votesUp == c.votesDown) continue;
            bool insideLeft = MapGeometry.SignedArea(c.p) > 0, leftUp = c.votesUp > c.votesDown;
            c.line.hollow = insideLeft != leftUp;
        }
        // Without its direction, a tick on a closed contour marks a hollow
        foreach (double[] t in ticks)
        {
            if (t.Length > 2 && t[2] > 0) continue;   // a form line's tick (below)
            if (t.Length >= 5 && !double.IsNaN(t[3])) continue;
            int cid = ContourHeights.Nearest(ids, C, R, (t[0] - ox) / res, (t[1] - oy) / res, 2);
            if (cid == 0 || !contours[cid - 1].line.closed || directed[cid]) continue;
            contours[cid - 1].line.hollow = true;
        }
        // A tick carries no direction here, and a tick by a large ring can belong to the hill's own outside or to the
        // contour next to it. The map import knew the ticks' directions and its 10 m heights clearly show a hill
        // inside: then it is a hill (2026-11-22: on Stora Stöeryd whole hillsides of 300 m sank into hollows)
        foreach (Contour c in contours)
        {
            if (!c.line.closed || grid.h == null || directed[c.id]) continue;
            double rise = RiseInside(grid, c.line.xy, out bool large);
            if (c.line.hollow && rise > (large ? 1.5 : 0.6)) c.line.hollow = false;
            // A small ring whose tick was not found next to it (drawn a little off, or on a piece of the ring the
            // stitching left apart): the import's heights clearly sink inside it
            else if (!c.line.hollow && !large && rise < -0.75) c.line.hollow = true;
        }
        foreach (Contour c in contours)
        {
            if (directed[c.id]) { }
            else if (c.line.closed)
            {
                // The inside is on the left when the ring runs counter-clockwise
                bool insideLeft = MapGeometry.SignedArea(c.p) > 0;
                bool insideUp = !c.line.hollow;
                if (insideLeft == insideUp) c.votesUp += 3; else c.votesDown += 3;
            }
            else if (grid.h != null)
            {
                // An open contour: which side is up, from the import's 10 m heights (the import read the ticks'
                // directions from the OCAD file) a few metres either side of the whole line, when they say it clearly
                double vote = 0, total = 0, reach = 6 / res;
                for (int i = 0; i + 3 < c.p.Length; i += 2)
                {
                    double sx = c.p[i + 2] - c.p[i], sy = c.p[i + 3] - c.p[i + 1], l = Math.Sqrt(sx * sx + sy * sy);
                    if (l < 1e-6) continue;
                    double mx = (c.p[i] + c.p[i + 2]) / 2, my = (c.p[i + 1] + c.p[i + 3]) / 2, nx = -sy / l * reach, ny = sx / l * reach;
                    vote += l * (grid.At(ox + (mx + nx) * res, oy + (my + ny) * res) - grid.At(ox + (mx - nx) * res, oy + (my - ny) * res));
                    total += l;
                }
                if (total * res > 15 && Math.Abs(vote) > 0.4 * total) { if (vote > 0) c.votesUp += 3; else c.votesDown += 3; }
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
        int knownSlope = 0;
        foreach (Contour c in contours) if (c.orientation != 0) knownSlope++;
        if (knownSlope < contours.Count / 4) return null;

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
        var known = new bool[contours.Count + 1];
        foreach (Contour c in contours) known[c.id] = adj[c.id].Count > 0;
        int nested = NestLevels(contours, level, known, out bool[] certain, out bool[] hasInside);

        // ---- The ground: fixed on every contour, smooth in between ----
        var z = new double[R * C];
        var isFixed = new bool[R * C];
        var sum = new double[R * C];
        var count = new int[R * C];
        foreach (Contour c in contours)
        {
            if (!known[c.id]) continue;
            c.line.level = Math.Round(level[c.id]) * eq;
            c.line.solved = true;
            Fix(c.p, c.line.level, sum, count, C, R);
        }
        if (!Fill(z, isFixed, sum, count, R, C)) return null;

        // ---- A small knoll or hollow on a slope: level with the ground round it ----
        // An innermost ring whose level came from the rays alone (not from a ring round it) can end up one interval
        // too low for a knoll (or too high for a hollow): the ground round it is then above its top and it becomes a
        // pit (2026-11-22). The ground round the ring is filled again without the ring, and a knoll's ring is put at
        // the first contour level at or above that ground (a hollow's at the first at or below).
        int moved = 0;
        foreach (Contour c in contours)
        {
            if (!known[c.id] || certain[c.id] || hasInside[c.id] || !c.line.closed) continue;
            double without = GroundWithout(c.p, z, isFixed, sum, count, C, R);
            if (double.IsNaN(without)) continue;
            double at = c.line.level / eq, g = without / eq;
            double want = c.line.hollow ? Math.Min(at, Math.Floor(g + 0.1)) : Math.Max(at, Math.Ceiling(g - 0.1));
            if (want == at) continue;
            double shift = (want - at) * eq;
            for (int i = 0; i + 3 < c.p.Length; i += 2)
                GridBuilder.Supercover(c.p[i], c.p[i + 1], c.p[i + 2], c.p[i + 3], C, R, (x, y) => sum[y * C + x] += shift);
            c.line.level = want * eq;
            moved++;
        }
        if (moved > 0 && !Fill(z, isFixed, sum, count, R, C)) return null;

        // ---- Form lines: half way between the contours round them ----
        // A closed form line is a small knoll, or a small hollow when it has a slope tick (2026-11-20, Anders: hollows
        // drawn with form lines on Illern came out as knolls; the form lines' ticks (103.1) were not read at all)
        foreach (Line l in lines)
        {
            if (!l.formLine || !l.closed) continue;
            foreach (double[] t in ticks)
            {
                if (!NearLine(l.xy, t[0], t[1], 2.5)) continue;
                // With its direction: a hollow when the tick points into the ring, a knoll when it points out
                if (t.Length >= 5 && !double.IsNaN(t[3])) l.hollow = Inside(l.xy, t[0] + t[3] * 2, t[1] + t[4] * 2);
                else l.hollow = true;
                break;
            }
        }
        var rings = new List<Ring>();
        foreach (Contour c in contours)
            if (c.line.closed && c.line.solved) rings.Add(new Ring(c.line));
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
            m /= n;
            // Inside a closed contour the ground is above its level for a hill, below for a hollow, so the form line
            // is half a contour above (or below) it, or further when it lies above a contour drawn in part inside.
            // The fill's own height is not trusted there: where two contours run close it sinks below the ring's
            // level, and a knoll's form line on a hilltop was put half a contour down, digging a hollow round the
            // top (2026-11-22, Anders: a hill with a rim, a big hollow and a small knoll in the middle).
            Line ring = Enclosing(rings, l.xy);
            if (ring != null && !ring.hollow)
                l.level = ring.level + (Math.Max(0, Math.Floor((m - ring.level) / eq + 0.1)) + 0.5) * eq;
            else if (ring != null)
                l.level = ring.level - (Math.Max(0, Math.Floor((ring.level - m) / eq + 0.1)) + 0.5) * eq;
            else if (l.closed && l.hollow) l.level = (Math.Ceiling(m / eq - 0.02) - 0.5) * eq;
            else if (l.closed) l.level = (Math.Floor(m / eq + 0.02) + 0.5) * eq;
            else l.level = (Math.Floor(m / eq) + 0.5) * eq;
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
        log?.Invoke($"{contours.Count} contours ({knownSlope} with known slope, {nested} set by the ring round them, {moved} small rings moved to the ground round them), {forms} form lines, {edges.Count} pairs, {C}x{R} cells");
        return fine;
    }

    // A closed contour straight inside another, with no other contour between them, is one interval above it when
    // both are hill rings (below when both are hollows), and at the same level when one is a hill and the other a
    // hollow (a hollow on a hilltop). That is certain from the map alone, so it is put above what the solver found
    // from its rays, which could put a small top ring below the ring round it and dig a hole in the top
    // (2026-11-22, Anders: wanted the heights 100% right). Rings the rays never reached get their level this way too.
    static int NestLevels(List<Contour> contours, double[] level, bool[] known, out bool[] certain, out bool[] hasInside)
    {
        certain = new bool[contours.Count + 1];
        hasInside = new bool[contours.Count + 1];
        var rings = new List<(Contour c, double area, double minX, double minY, double maxX, double maxY)>();
        foreach (Contour c in contours)
        {
            if (!c.line.closed) continue;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i + 1 < c.p.Length; i += 2)
            {
                minX = Math.Min(minX, c.p[i]); maxX = Math.Max(maxX, c.p[i]);
                minY = Math.Min(minY, c.p[i + 1]); maxY = Math.Max(maxY, c.p[i + 1]);
            }
            rings.Add((c, Math.Abs(MapGeometry.SignedArea(c.p)), minX, minY, maxX, maxY));
        }
        // The smallest ring round each contour (round its first and middle points)
        var parent = new int[contours.Count + 1];
        foreach (Contour c in contours)
        {
            int n = c.p.Length / 2;
            double x0 = c.p[0], y0 = c.p[1], x1 = c.p[(n / 2) * 2], y1 = c.p[(n / 2) * 2 + 1];
            double ownArea = c.line.closed ? Math.Abs(MapGeometry.SignedArea(c.p)) : 0;
            double best = double.MaxValue;
            foreach (var r in rings)
            {
                if (r.c == c || r.area <= ownArea || r.area >= best) continue;
                if (x0 < r.minX || x0 > r.maxX || y0 < r.minY || y0 > r.maxY || x1 < r.minX || x1 > r.maxX || y1 < r.minY || y1 > r.maxY) continue;
                if (!Inside(r.c.p, x0, y0) || !Inside(r.c.p, x1, y1)) continue;
                best = r.area;
                parent[c.id] = r.c.id;
            }
        }
        // A ring with an open contour (a contour cut by a cliff or the map's edge) inside it: what lies between is not known
        var mixed = new bool[contours.Count + 1];
        foreach (Contour c in contours)
        {
            if (parent[c.id] == 0) continue;
            hasInside[parent[c.id]] = true;
            if (!c.line.closed) mixed[parent[c.id]] = true;
        }
        for (int i = 1; i < level.Length; i++) if (known[i]) level[i] = Math.Round(level[i]);
        rings.Sort((a, b) => b.area.CompareTo(a.area));
        int set = 0;
        foreach (var r in rings)
        {
            int pid = parent[r.c.id];
            if (pid == 0 || !known[pid] || mixed[pid]) continue;
            bool outerHollow = contours[pid - 1].line.hollow, hollow = r.c.line.hollow;
            double want = level[pid] + (outerHollow == hollow ? (hollow ? -1 : 1) : 0);
            certain[r.c.id] = true;
            if (known[r.c.id] && level[r.c.id] == want) continue;
            level[r.c.id] = want;
            known[r.c.id] = true;
            set++;
        }
        return set;
    }

    // How much higher the 10 m heights are inside a ring than on it; large: 25 m across or more
    static double RiseInside(TvHeights grid, double[] xy, out bool large)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, on = 0;
        int n = 0;
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            minX = Math.Min(minX, xy[i]); maxX = Math.Max(maxX, xy[i]);
            minY = Math.Min(minY, xy[i + 1]); maxY = Math.Max(maxY, xy[i + 1]);
            on += grid.At(xy[i], xy[i + 1]); n++;
        }
        large = maxX - minX >= 25 && maxY - minY >= 25;
        if (n == 0) return 0;
        on /= n;
        double step = Math.Max(0.5, Math.Min(maxX - minX, maxY - minY) / 10), inside = 0;
        int m = 0;
        for (double y = minY + step * 0.5; y < maxY; y += step)
            for (double x = minX + step * 0.5; x < maxX; x += step)
                if (Inside(xy, x, y)) { inside += grid.At(x, y); m++; }
        return m == 0 ? 0 : inside / m - on;
    }

    class Ring
    {
        public Line line;
        public double area, minX, minY, maxX, maxY;
        public Ring(Line l)
        {
            line = l;
            area = Math.Abs(MapGeometry.SignedArea(l.xy));
            minX = minY = double.MaxValue; maxX = maxY = double.MinValue;
            for (int i = 0; i + 1 < l.xy.Length; i += 2)
            {
                minX = Math.Min(minX, l.xy[i]); maxX = Math.Max(maxX, l.xy[i]);
                minY = Math.Min(minY, l.xy[i + 1]); maxY = Math.Max(maxY, l.xy[i + 1]);
            }
        }
    }

    // The smallest closed contour round a line (round its first and middle points), or null
    static Line Enclosing(List<Ring> rings, double[] xy)
    {
        int n = xy.Length / 2;
        double x0 = xy[0], y0 = xy[1], x1 = xy[(n / 2) * 2], y1 = xy[(n / 2) * 2 + 1];
        Ring best = null;
        foreach (Ring r in rings)
        {
            if (best != null && r.area >= best.area) continue;
            if (x0 < r.minX || x0 > r.maxX || y0 < r.minY || y0 > r.maxY || x1 < r.minX || x1 > r.maxX || y1 < r.minY || y1 > r.maxY) continue;
            if (!Inside(r.line.xy, x0, y0) || !Inside(r.line.xy, x1, y1)) continue;
            best = r;
        }
        return best?.line;
    }

    static bool Inside(double[] p, double x, double y)
    {
        bool inside = false;
        int n = p.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = p[2 * i], yi = p[2 * i + 1], xj = p[2 * j], yj = p[2 * j + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    // The ground along a ring when the ring itself is not there: the fill redone in a window round it (the ring's own
    // cells free, the rest as filled), averaged over the ring's cells; NaN when it can't be told
    static double GroundWithout(double[] p, double[] z, bool[] isFixed, double[] sum, int[] count, int C, int R)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i + 1 < p.Length; i += 2)
        {
            minX = Math.Min(minX, p[i]); maxX = Math.Max(maxX, p[i]);
            minY = Math.Min(minY, p[i + 1]); maxY = Math.Max(maxY, p[i + 1]);
        }
        const int Margin = 8;
        int x0 = Math.Max(0, (int)minX - Margin), x1 = Math.Min(C - 1, (int)maxX + Margin);
        int y0 = Math.Max(0, (int)minY - Margin), y1 = Math.Min(R - 1, (int)maxY + Margin);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 3 || h < 3 || (long)w * h > 20000) return double.NaN;
        var own = new bool[w * h];
        for (int i = 0; i + 3 < p.Length; i += 2)
            GridBuilder.Supercover(p[i], p[i + 1], p[i + 2], p[i + 3], C, R, (x, y) =>
            {
                if (x >= x0 && x <= x1 && y >= y0 && y <= y1) own[(y - y0) * w + (x - x0)] = true;
            });
        var t = new double[w * h];
        var held = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = y * w + x, g = (y + y0) * C + x + x0;
                t[k] = z[g];
                // The window's edge stays as filled; another line's cell stays fixed (a cell the ring shares with
                // another line is let go)
                bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                held[k] = edge || (isFixed[g] && !own[k]);
            }
        // Inside the ring there is nothing to hold the ground up any more: start it from the window's mean
        double mean = 0;
        int n = 0;
        for (int k = 0; k < t.Length; k++) if (held[k]) { mean += t[k]; n++; }
        if (n == 0) return double.NaN;
        mean /= n;
        for (int k = 0; k < t.Length; k++) if (!held[k]) t[k] = mean;
        for (int it = 0; it < 150; it++)
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int k = y * w + x;
                    if (held[k]) continue;
                    t[k] += 1.8 * ((t[k - 1] + t[k + 1] + t[k - w] + t[k + w]) / 4 - t[k]);
                }
        double s = 0;
        int m = 0;
        for (int k = 0; k < t.Length; k++) if (own[k]) { s += t[k]; m++; }
        return m == 0 ? double.NaN : s / m;
    }

    // Within reach metres of the line
    static bool NearLine(double[] p, double x, double y, double reach)
    {
        double r2 = reach * reach;
        for (int i = 0; i + 3 < p.Length; i += 2)
        {
            double ax = p[i], ay = p[i + 1], dx = p[i + 2] - ax, dy = p[i + 3] - ay;
            if (Math.Min(ax, ax + dx) - reach > x || Math.Max(ax, ax + dx) + reach < x || Math.Min(ay, ay + dy) - reach > y || Math.Max(ay, ay + dy) + reach < y) continue;
            double l2 = dx * dx + dy * dy;
            double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / l2)) : 0;
            double qx = ax + t * dx - x, qy = ay + t * dy - y;
            if (qx * qx + qy * qy <= r2) return true;
        }
        return false;
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
        var contours = new List<double[]>();
        var forms = new List<double[]>();
        foreach (var (code, xy, _) in Features(json))
        {
            if (code.Length < 3 || xy.Count < 4) continue;
            string major = code.Substring(0, 3);
            if (major != "101" && major != "102" && major != "103") continue;
            (major == "103" ? forms : contours).Add(xy.ToArray());
        }
        // A contour is often drawn as several lines (cut at a tick, a text or a cliff): joined end to end again, so a
        // ring round a knoll or a hollow is known as closed (2026-11-20, as ContourHeights does at the import)
        foreach (double[] p in ContourHeights.StitchContours(contours, 1.0))
            list.Add(new Line { xy = CloseSmallGap(p, out bool closed), closed = closed });
        foreach (double[] p in ContourHeights.StitchContours(forms, 1.0))
            list.Add(new Line { xy = CloseSmallGap(p, out bool closed), closed = closed, formLine = true });
        return list;
    }

    // A ring the mapper left with a small gap between its ends (2026-11-22, Anders: a hill on Nya Slogebo became a
    // rim round a big hollow: the form line on its top had a 1.7 m gap, so it was read as an open line on flat ground
    // and put half a contour below the top). The gap is closed when it is small next to the ring.
    static double[] CloseSmallGap(double[] p, out bool closed)
    {
        closed = MapGeometry.IsClosed(p, 1.5);
        int n = p.Length / 2;
        if (closed || n < 4) return p;
        double length = 0;
        for (int i = 0; i + 3 < p.Length; i += 2) length += MapGeometry.Dist(p[i], p[i + 1], p[i + 2], p[i + 3]);
        double gap = MapGeometry.Dist(p[0], p[1], p[2 * n - 2], p[2 * n - 1]);
        if (gap > Math.Min(8.0, Math.Max(1.5, 0.12 * length)) || length < 4 * gap) return p;
        closed = true;
        var q = new double[p.Length + 2];
        Array.Copy(p, q, p.Length);
        q[p.Length] = p[0]; q[p.Length + 1] = p[1];
        return q;
    }

    // Slope ticks: points of the contour and form line symbols: x, y, 1 for a form line's tick, and the way the tick
    // points downhill (x, y; NaN for a map imported before 2026-11-23, which did not save it)
    public static List<double[]> ReadTicks(byte[] json)
    {
        var list = new List<double[]>();
        if (json == null) return list;
        foreach (var (code, xy, down) in Features(json))
        {
            if (code.Length < 6 || xy.Count < 2) continue;
            string major = code.Substring(0, 3);
            // Any point of the contour symbols is a tick: most maps number it 101.1 or 103.1, but some (Stora Stöeryd,
            // Ängaryd) draw it as a point of 101.0 itself, and those ticks were not read before (2026-11-22)
            if (major == "101" || major == "102" || major == "103")
                list.Add(new[] { xy[0], xy[1], major == "103" ? 1.0 : 0.0, down != null ? down[0] : double.NaN, down != null ? down[1] : double.NaN });
        }
        return list;
    }

    // Every feature's symbol number ("101000") and coordinates, from the GeoJSON the import writes
    static IEnumerable<(string code, List<double> xy, double[] down)> Features(byte[] bytes)
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
            // A tick's way downhill ("Down":[x, y], written by imports from 2026-11-23)
            double[] down = null;
            int d = text.IndexOf("\"Down\":[", at, StringComparison.Ordinal);
            if (d >= 0 && d < coords)
            {
                int close = text.IndexOf(']', d);
                string[] parts = close > d ? text.Substring(d + 8, close - d - 8).Split(',') : new string[0];
                if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double dx)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double dy))
                    down = new[] { dx, dy };
            }
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
            yield return (code, xy, down);
        }
    }
}
