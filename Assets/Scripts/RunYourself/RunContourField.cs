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
        public bool closed, formLine, hollow, index;   // index: an index contour (102)
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
    public static TvHeights Build(TvHeights grid, List<Line> lines, List<double[]> ticks, double eq, Action<string> log = null,
                                   List<double[]> water = null, Func<double, double, bool> lakeAt = null)
    {
        if (grid == null || lines == null || eq <= 0) return null;
        ticks = RealTicks(lines, ticks);
        int k = Math.Max(1, (int)Math.Round(grid.cell / Res));
        double res = grid.cell / k;
        int C = (grid.cols - 1) * k + 1, R = (grid.rows - 1) * k + 1;
        if ((long)C * R > 40000000) return null;
        double ox = grid.minx - res * 0.5, oy = grid.miny - res * 0.5;   // cell (0, 0) is centred on minx, miny

        var contours = new List<Contour>();
        bool anyIndex = false;
        foreach (Line l in lines)
        {
            if (l.formLine || l.xy.Length < 4) continue;
            var q = new double[l.xy.Length];
            for (int i = 0; i < q.Length; i += 2) { q[i] = (l.xy[i] - ox) / res; q[i + 1] = (l.xy[i + 1] - oy) / res; }
            contours.Add(new Contour { id = contours.Count + 1, line = l, p = q });
            anyIndex |= l.index;
        }
        if (contours.Count < 3) return null;

        var ids = new int[R * C];
        foreach (Contour c in contours)
            for (int i = 0; i + 3 < c.p.Length; i += 2)
                GridBuilder.Supercover(c.p[i], c.p[i + 1], c.p[i + 2], c.p[i + 3], C, R, (x, y) => ids[y * C + x] = c.id);

        // Streams, ditches, narrow marshes, lake shores and small marshes: water lies low
        bool[] wet = null;
        if (water != null && water.Count > 0)
        {
            wet = new bool[R * C];
            foreach (double[] w in water)
            {
                if (w.Length == 2)
                {
                    int cx = (int)Math.Floor((w[0] - ox) / res), cy = (int)Math.Floor((w[1] - oy) / res);
                    for (int y = cy - 1; y <= cy + 1; y++)
                        for (int x = cx - 1; x <= cx + 1; x++)
                            if (x >= 0 && y >= 0 && x < C && y < R) wet[y * C + x] = true;
                    continue;
                }
                for (int i = 0; i + 3 < w.Length; i += 2)
                    GridBuilder.Supercover((w[i] - ox) / res, (w[i + 1] - oy) / res, (w[i + 2] - ox) / res, (w[i + 3] - oy) / res, C, R,
                                           (x, y) => wet[y * C + x] = true);
            }
        }

        // Open water (lakes, ponds, wide rivers) from the map's 3D world layer (lakes have no lines in lines.json): -1
        // here, numbered by piece in LakeLevels
        int[] lake = null;
        if (lakeAt != null)
        {
            lake = new int[R * C];
            bool anyLake = false;
            for (int y = 0; y < R; y++)
                for (int x = 0; x < C; x++)
                    if (lakeAt(ox + (x + 0.5) * res, oy + (y + 0.5) * res)) { lake[y * C + x] = -1; anyLake = true; }
            if (!anyLake) lake = null;
        }

        // ---- What the map says about which way is up, contour by contour ----
        // ev > 0: the contour's left side is up. "strong" is what is certain enough to keep two pieces of the slope apart:
        // the slope ticks and the rings round knolls and hollows; the import's 10 m heights and the water only tip the
        // balance where nothing else says anything.
        var ev = new double[contours.Count + 1];
        var strong = new double[contours.Count + 1];
        // A tick with its direction (maps imported from 2026-11-23) says for certain which side is down
        var directed = new bool[contours.Count + 1];
        var tickUp = new int[contours.Count + 1];
        var tickDown = new int[contours.Count + 1];
        foreach (double[] t in ticks)
        {
            if (t.Length < 5 || t[2] > 0 || double.IsNaN(t[3])) continue;
            double px = (t[0] - ox) / res, py = (t[1] - oy) / res;
            int cid = ContourHeights.Nearest(ids, C, R, px, py, 2);
            if (cid == 0) continue;
            int side = ContourHeights.SideOf(contours[cid - 1].p, px + t[3] * 0.8, py + t[4] * 0.8);
            if (side > 0) tickDown[cid]++;   // the tick's side is downhill
            else if (side < 0) tickUp[cid]++;
            directed[cid] = true;
        }
        foreach (Contour c in contours)
        {
            if (!directed[c.id]) continue;
            ev[c.id] = strong[c.id] = 5 * (tickUp[c.id] - tickDown[c.id]);
            if (!c.line.closed || tickUp[c.id] == tickDown[c.id]) continue;
            bool insideLeft = MapGeometry.SignedArea(c.p) > 0, leftUp = tickUp[c.id] > tickDown[c.id];
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
            if (directed[c.id]) continue;
            if (c.line.closed)
            {
                // A ring is a knoll unless it is marked a hollow. The inside is on the left when the ring runs
                // counter-clockwise.
                bool insideLeft = MapGeometry.SignedArea(c.p) > 0;
                double v = insideLeft == !c.line.hollow ? 3 : -3;
                ev[c.id] += v;
                strong[c.id] += v;
            }
            else if (grid.h != null)
            {
                // An open contour: which side is up, from the import's 10 m heights a few metres either side of the
                // whole line, when they say it clearly. Only a hint: the import guessed too where the map says nothing
                double vote = 0, total = 0, reach = 6 / res;
                for (int i = 0; i + 3 < c.p.Length; i += 2)
                {
                    double sx = c.p[i + 2] - c.p[i], sy = c.p[i + 3] - c.p[i + 1], l = Math.Sqrt(sx * sx + sy * sy);
                    if (l < 1e-6) continue;
                    double mx = (c.p[i] + c.p[i + 2]) / 2, my = (c.p[i + 1] + c.p[i + 3]) / 2, nx = -sy / l * reach, ny = sx / l * reach;
                    vote += l * (grid.At(ox + (mx + nx) * res, oy + (my + ny) * res) - grid.At(ox + (mx - nx) * res, oy + (my - ny) * res));
                    total += l;
                }
                if (total * res > 15 && Math.Abs(vote) > 0.4 * total) ev[c.id] += vote > 0 ? 1 : -1;
            }
        }

        // ---- Neighbours: the first other contour straight out to each side (as ContourHeights) ----
        // and, on the way to it, water: the side of a contour where a stream or a marsh lies before the next contour is
        // down (on a slope the contour below sees the same water uphill, so the two cancel; in a valley they agree)
        var seen = new Dictionary<long, int[]>();
        var wetVote = new double[contours.Count + 1];
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
                        bool wetOnWay = false;
                        for (double t = 1.5; t < maxSteps; t += 0.5)
                        {
                            int cx = (int)Math.Floor(px + nx * side * t), cy = (int)Math.Floor(py + ny * side * t);
                            if (cx < 0 || cy < 0 || cx >= C || cy >= R) break;
                            if (wet != null && wet[cy * C + cx]) wetOnWay = true;
                            int v = ids[cy * C + cx];
                            if (v == 0) continue;
                            // The ray met this contour again (across a small ring, round a bend): what lies beyond is
                            // not its neighbour on this side (as ContourHeights since 2026-11-20)
                            if (v == c.id) { if (t > 3.0) break; continue; }
                            long key = (long)c.id << 32 | (uint)v;
                            if (!seen.TryGetValue(key, out int[] cnt)) seen[key] = cnt = new int[2];
                            cnt[side > 0 ? 0 : 1]++;
                            break;
                        }
                        if (wetOnWay) wetVote[c.id] -= side * 0.5;
                    }
                }
                next = s - len;
            }
        }
        foreach (Contour c in contours) ev[c.id] += Math.Max(-4, Math.Min(4, wetVote[c.id]));
        var edges = new List<(int a, int b, int sa, int sb, double w)>();
        foreach (var kv in seen)
        {
            int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
            if (a >= b) continue;
            if (!seen.TryGetValue((long)b << 32 | (uint)a, out int[] back)) continue;
            int sa = kv.Value[0] >= kv.Value[1] ? 1 : -1;
            int sb = back[0] >= back[1] ? 1 : -1;
            double w = Math.Min(Math.Max(kv.Value[0], kv.Value[1]), Math.Max(back[0], back[1]));
            edges.Add((a, b, sa, sb, w));
        }
        if (edges.Count == 0) return null;
        var adj = new List<int>[contours.Count + 1];
        for (int i = 0; i <= contours.Count; i++) adj[i] = new List<int>();
        for (int e = 0; e < edges.Count; e++) { adj[edges[e].a].Add(e); adj[edges[e].b].Add(e); }

        bool[] accepted = Orient(contours, edges, ev, strong, anyIndex, out int dropped, out int[] pieceOf);
        int knownSlope = 0;
        foreach (Contour c in contours)
        {
            if (c.orientation == 0) continue;
            knownSlope++;
            // A ring's knoll or hollow follows from the way its slope was found to go
            if (c.line.closed) c.line.hollow = (MapGeometry.SignedArea(c.p) > 0) != (c.orientation > 0);
        }
        if (knownSlope < contours.Count / 4) return null;

        var diff = new double[edges.Count];
        var weight = new double[edges.Count];
        for (int e = 0; e < edges.Count; e++)
        {
            var ed = edges[e];
            int oa = contours[ed.a - 1].orientation, ob = contours[ed.b - 1].orientation;
            if (oa == 0 || ob == 0) continue;
            diff[e] = (oa * ed.sa - ob * ed.sb) / 2.0;
            // A pair left out of the pieces above still ties the levels, but lightly
            weight[e] = ed.w * (accepted[e] ? 1 : 0.25);
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
        if (anyIndex) AlignIndex(contours, edges, diff, baseWeight, level, pieceOf);
        var lakes = LakeLevels(lake, contours, level, ids, C, R, res);
        ids = null;
        var known = new bool[contours.Count + 1];
        // Only a contour tied to a neighbour by its slope has a level (2026-11-27: a 12 m piece of an index contour on
        // Delsjön whose slope nothing said kept the level 0 and stood as a spike 40 m above the hillside)
        for (int e = 0; e < edges.Count; e++)
            if (baseWeight[e] > 0) known[edges[e].a] = known[edges[e].b] = true;
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
        // Lakes: one flat surface a little under the lowest contour by the shore
        if (lakes != null)
            for (int i = 0; i < lakes.Length; i++)
                if (!double.IsNaN(lakes[i])) { sum[i] += lakes[i] * eq; count[i]++; }
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
        log?.Invoke($"{contours.Count} contours ({knownSlope} with known slope, {dropped} pairs left apart, {nested} set by the ring round them, {moved} small rings moved to the ground round them), {forms} form lines, {edges.Count} pairs, {C}x{R} cells");
        return fine;
    }

    // Only the points that are real slope ticks, on a contour or form line and across it (ContourHeights.RealTicks)
    static List<double[]> RealTicks(List<Line> lines, List<double[]> ticks)
    {
        if (ticks == null || ticks.Count == 0) return ticks ?? new List<double[]>();
        var xy = new List<double[]>();
        foreach (Line l in lines) xy.Add(l.xy);
        int n = ticks.Count;
        double[] px = new double[n], py = new double[n], dx = new double[n], dy = new double[n];
        var code = new int[n];
        for (int t = 0; t < n; t++)
        {
            double[] k = ticks[t];
            px[t] = k[0]; py[t] = k[1];
            dx[t] = k.Length >= 5 ? k[3] : double.NaN;
            dy[t] = k.Length >= 5 ? k[4] : double.NaN;
            code[t] = k.Length >= 6 ? (int)k[5] : (k.Length > 2 && k[2] > 0 ? 103001 : 101001);
        }
        bool[] ok = ContourHeights.RealTicks(xy, px, py, dx, dy, code, 2.0);
        var kept = new List<double[]>();
        for (int t = 0; t < n; t++) if (ok[t]) kept.Add(ticks[t]);
        return kept;
    }

    // ---- Lakes and ponds lie flat, under every contour round them ----
    // No contour is drawn across water, so the ground over a lake was filled in from the contours on its far shores
    // and rose above the contour along its own bank: a ditch ran along the shore (Delsjön, Fossum), and the water of a
    // large lake sloped. Each piece of open water is held at one level, a fifth of an interval under the contour met
    // going out from it (within 25 m), when at least four in five of those contours are at the same level. Else it is
    // a river running downhill, or the contours round it don't agree: left to the contours. Returns each cell's water
    // level (in intervals; NaN on land), or null.
    static double[] LakeLevels(int[] label, List<Contour> contours, double[] level, int[] ids, int C, int R, double res)
    {
        if (label == null) return null;
        var result = new double[R * C];
        for (int i = 0; i < result.Length; i++) result[i] = double.NaN;
        int reach = (int)Math.Ceiling(25 / res);
        var queue = new List<int>();
        var dist = new Dictionary<int, int>();
        var met = new List<double>();
        int next = 0;
        for (int start = 0; start < label.Length; start++)
        {
            if (label[start] != -1) continue;
            // The piece of water
            int id = ++next;
            queue.Clear();
            queue.Add(start);
            label[start] = id;
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int c = queue[qi], x = c % C, y = c / C;
                if (x > 0 && label[c - 1] == -1) { label[c - 1] = id; queue.Add(c - 1); }
                if (x < C - 1 && label[c + 1] == -1) { label[c + 1] = id; queue.Add(c + 1); }
                if (y > 0 && label[c - C] == -1) { label[c - C] = id; queue.Add(c - C); }
                if (y < R - 1 && label[c + C] == -1) { label[c + C] = id; queue.Add(c + C); }
            }
            int size = queue.Count;
            if (size * res * res < 50) continue;   // a puddle
            // Out from it over land, up to the first contour each way
            dist.Clear();
            met.Clear();
            var front = new List<int>(queue);
            foreach (int c in front) dist[c] = 0;
            for (int qi = 0; qi < front.Count; qi++)
            {
                int c = front[qi], d = dist[c];
                int v = ids[c];
                if (v != 0)
                {
                    if (contours[v - 1].orientation != 0) met.Add(Math.Round(level[v]));
                    continue;   // not beyond the first contour
                }
                if (d >= reach) continue;
                int x = c % C, y = c / C;
                void Go(int n) { if (!dist.ContainsKey(n) && label[n] != id) { dist[n] = d + 1; front.Add(n); } }
                if (x > 0) Go(c - 1);
                if (x < C - 1) Go(c + 1);
                if (y > 0) Go(c - C);
                if (y < R - 1) Go(c + C);
            }
            if (met.Count < 3) continue;
            met.Sort();
            // A few contours by an outlet, or one wrongly levelled, don't set the level of the whole lake
            double low = met[met.Count / 10], mid = met[met.Count / 2];
            if (mid - low > 1.01) continue;
            // The shore all round at one level: else the contours round it don't agree, and the lake is left to them
            int atMid = 0;
            foreach (double v in met) if (v == mid) atMid++;
            if (atMid < 0.8 * met.Count) continue;
            foreach (int c in queue) result[c] = Math.Min(low, mid) - 0.2;
        }
        return result;
    }

    // ---- Every fifth contour an index contour, all over the map ----
    // The pieces the slope was joined into above are tied to each other only by the pairs left out of them, so two
    // large pieces can end up one interval apart in the levels solved from all pairs: on Fossum the index contours of
    // the east half sat one interval above those of the west, and the seam between them a step or a ridge. Each piece
    // is moved the fewest whole intervals that bring its index contours to the level (mod 5) most index contours have,
    // and the levels solved again with every index contour held at its own index level.
    static void AlignIndex(List<Contour> contours, List<(int a, int b, int sa, int sb, double w)> edges, double[] diff,
                           double[] baseWeight, double[] level, int[] pieceOf)
    {
        int Mod5(double v) => (((int)Math.Round(v)) % 5 + 5) % 5;
        // Index contour length at each level mod 5, for each piece and for the whole map
        var votes = new Dictionary<int, double[]>();
        var all = new double[5];
        double total = 0;
        foreach (Contour c in contours)
        {
            if (!c.line.index || c.orientation == 0) continue;
            double len = Length(c.p);
            int r = Mod5(level[c.id]);
            if (!votes.TryGetValue(pieceOf[c.id], out double[] v)) votes[pieceOf[c.id]] = v = new double[5];
            v[r] += len;
            all[r] += len;
            total += len;
        }
        if (total <= 0) return;
        int best = 0;
        for (int r = 1; r < 5; r++) if (all[r] > all[best]) best = r;
        // A map whose index contours don't keep to every fifth level (not index contours after all): left as it is
        if (all[best] < 0.4 * total) return;
        var shift = new Dictionary<int, int>();
        foreach (var kv in votes)
        {
            int top = 0;
            for (int r = 1; r < 5; r++) if (kv.Value[r] > kv.Value[top]) top = r;
            int d = ((best - top) % 5 + 5) % 5;
            shift[kv.Key] = d > 2 ? d - 5 : d;
        }
        // The anchors: an extra pair from the level 0 (index 0, no contour) to each index contour at its index level
        var ext = new List<(int a, int b, int sa, int sb, double w)>(edges);
        var extDiff = new List<double>(diff);
        var extW = new List<double>(baseWeight);
        foreach (Contour c in contours)
        {
            if (c.orientation == 0) continue;
            if (shift.TryGetValue(pieceOf[c.id], out int d)) level[c.id] += d;
            if (!c.line.index) continue;
            double at = level[c.id];
            double target = Math.Round((at - best) / 5) * 5 + best;
            ext.Add((0, c.id, 1, 1, 0));
            extDiff.Add(target);
            extW.Add(Math.Max(20, Length(c.p) / 10));
        }
        level[0] = 0;
        double[] dArr = extDiff.ToArray(), w = extW.ToArray(), w0 = (double[])w.Clone();
        for (int outer = 0; outer < 5; outer++)
        {
            ContourHeights.SolveLevels(level, ext, dArr, w);
            for (int e = 0; e < edges.Count; e++)
            {
                double r = level[ext[e].b] - level[ext[e].a] - dArr[e];
                w[e] = w0[e] / (1 + (r / 0.5) * (r / 0.5));
            }
        }
        double zero = level[0];
        for (int i = 0; i < level.Length; i++) level[i] -= zero;
    }

    static double Length(double[] p)
    {
        double len = 0;
        for (int i = 0; i + 3 < p.Length; i += 2) len += MapGeometry.Dist(p[i], p[i + 1], p[i + 2], p[i + 3]);
        return len;
    }

    // ---- Which way is up, for the whole map at once (2026-11-27, Anders: "höjdkurvorna ska vara 100 %") ----
    // Two contours next to each other (no contour between) are either one interval apart, the slope going on from one
    // to the other, or at the same level with a ridge or a valley between them. Which of the two, and so which way the
    // slope goes, is settled for pairs of contours in the order of how much of them face each other, joining them into
    // pieces of the map whose ups and downs are known relative to each other; each piece then turns the way the map's
    // own marks in it say (ticks, knoll rings, water). Before, every contour without a mark took its slope from its
    // neighbours one by one, so a single ridge or valley read as a slope turned a whole hillside upside down (Nya
    // Slogebo: the slope from the marsh up to the hill went down). A pair is not joined, or joined the other way (a
    // ridge or valley), when that would
    //   - break the index contours: every fifth contour is an index contour, so two index contours next to each other
    //     are always at the same level, an index contour and a plain one never are, and all the index contours of a
    //     piece lie at the same level (mod 5). Plain contours may lie at an index level (2026-11-27, Bondberget: some
    //     mappers draw index contours only here and there; counting those broke the right answer at control 4),
    //   - turn the slope round a loop of pairs the other way than the pairs already joined, or
    //   - turn ticks or knoll rings in one piece against those in the other.
    // The slope ticks counted here are only real ones (RealTicks). Sets every contour's orientation (1: the left side is
    // up, -1: the right, 0: nothing says); returns which pairs were joined as they were seen.
    static bool[] Orient(List<Contour> contours, List<(int a, int b, int sa, int sb, double w)> edges, double[] ev, double[] strong,
                         bool useIndex, out int dropped, out int[] pieceOf)
    {
        int n = contours.Count + 1;
        // Union-find: a contour's orientation is p times its root's, its level its root's plus q times the root's orientation
        var parent = new int[n];
        var p = new int[n];
        var q = new int[n];
        var sumEv = new double[n];
        var sumStrong = new double[n];
        // For each root: how many of the piece's index contours would lie off an index level, for each of the root's
        // level (mod 5) and orientation: [r * 2 + (o > 0 ? 0 : 1)]
        var broken = new int[n][];
        for (int i = 0; i < n; i++) { parent[i] = i; p[i] = 1; }
        foreach (Contour c in contours)
        {
            sumEv[c.id] = ev[c.id];
            sumStrong[c.id] = strong[c.id];
            var b = new int[10];
            for (int r = 0; r < 5; r++) b[r * 2] = b[r * 2 + 1] = c.line.index && r != 0 ? 1 : 0;
            broken[c.id] = b;
        }
        var path = new List<int>();
        int Find(int x, out int px, out int qx)
        {
            path.Clear();
            int root = x;
            while (parent[root] != root) { path.Add(root); root = parent[root]; }
            for (int i = path.Count - 1; i >= 0; i--)
            {
                int v = path[i], u = parent[v];
                if (u == root) continue;
                q[v] = q[u] + q[v] * p[u];
                p[v] *= p[u];
                parent[v] = root;
            }
            px = x == root ? 1 : p[x];
            qx = x == root ? 0 : q[x];
            return root;
        }
        int Min(int[] b) { int m = int.MaxValue; foreach (int v in b) m = Math.Min(m, v); return m; }

        var order = new int[edges.Count];
        for (int e = 0; e < order.Length; e++) order[e] = e;
        Array.Sort(order, (x, y) => { int c = edges[y].w.CompareTo(edges[x].w); return c != 0 ? c : x.CompareTo(y); });
        var accepted = new bool[edges.Count];
        var merged = new int[10];
        dropped = 0;
        pieceOf = new int[n];
        foreach (int e in order)
        {
            var (a, b, sa, sb, _) = edges[e];
            bool ia = contours[a - 1].line.index, ib = contours[b - 1].line.index;
            int ra = Find(a, out int pa, out int qa), rb = Find(b, out int pb, out int qb);
            bool done = false;
            for (int pass = 0; pass < 2 && !done; pass++)
            {
                // The slope going on from one to the other first, then a ridge or valley between them
                bool slope = pass == 0;
                if (useIndex && slope && ia && ib) continue;          // two index contours next to each other: same level
                if (useIndex && !slope && ia != ib) continue;          // an index contour and a plain one: never the same level
                int k = slope ? -sa * sb : sa * sb;                    // o_b = k o_a
                int d = slope ? sa : 0;                                // L_b = L_a + d o_a
                if (ra == rb)
                {
                    // Already joined: the slope must go the same way. The levels round a loop of pairs may not add up
                    // where contours crowd together at a cliff (one ray skips a contour); the levels are solved below
                    // with every pair, so a loop that doesn't add up only weighs less there
                    if (pb == k * pa) done = true;
                    continue;
                }
                int P = pb * k * pa, Q = qa + d * pa - qb * P;          // rb's orientation and level from ra's
                // Marks in the two pieces that would turn against each other
                double sA = sumStrong[ra], sB = P * sumStrong[rb];
                if (Math.Abs(sA) >= 5 && Math.Abs(sB) >= 5 && Math.Sign(sA) != Math.Sign(sB)) continue;
                if (useIndex)
                {
                    int[] A = broken[ra], B = broken[rb];
                    for (int r = 0; r < 5; r++)
                        for (int oi = 0; oi < 2; oi++)
                        {
                            int o = oi == 0 ? 1 : -1, rr = ((r + Q * o) % 5 + 5) % 5, oo = P * o;
                            merged[r * 2 + oi] = A[r * 2 + oi] + B[rr * 2 + (oo > 0 ? 0 : 1)];
                        }
                    if (Min(merged) > Min(A) + Min(B)) continue;
                    Array.Copy(merged, A, 10);
                }
                parent[rb] = ra;
                p[rb] = P;
                q[rb] = Q;
                sumEv[ra] += P * sumEv[rb];
                sumStrong[ra] += P * sumStrong[rb];
                done = true;
            }
            accepted[e] = done;
            if (!done) dropped++;
        }
        foreach (Contour c in contours)
        {
            int r = Find(c.id, out int pc, out _);
            pieceOf[c.id] = r;
            c.orientation = sumEv[r] > 0 ? pc : sumEv[r] < 0 ? -pc : 0;
        }
        return accepted;
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
        var indexes = new List<double[]>();
        var forms = new List<double[]>();
        foreach (var (code, xy, _) in Features(json))
        {
            if (code.Length < 3 || xy.Count < 4) continue;
            string major = code.Substring(0, 3);
            if (major != "101" && major != "102" && major != "103") continue;
            (major == "103" ? forms : major == "102" ? indexes : contours).Add(xy.ToArray());
        }
        // A contour is often drawn as several lines (cut at a tick, a text or a cliff): joined end to end again, so a
        // ring round a knoll or a hollow is known as closed (2026-11-20, as ContourHeights does at the import). Index
        // contours and plain ones are joined each on their own: they are never the same contour, and an end of one
        // that happened to lie by an end of the other made one line at two levels (2026-11-27)
        // A mapper may draw only part of a contour as an index contour, the rest plain (Fossum): a piece of index contour
        // that goes straight on into a plain one is joined to it, and the line is not counted as an index contour.
        foreach (var (p, index) in JoinIndexToPlain(ContourHeights.StitchContours(contours, 1.0), ContourHeights.StitchContours(indexes, 1.0)))
            list.Add(new Line { xy = CloseSmallGap(p, out bool closed), closed = closed, index = index });
        foreach (double[] p in ContourHeights.StitchContours(forms, 1.0))
            list.Add(new Line { xy = CloseSmallGap(p, out bool closed), closed = closed, formLine = true });
        return list;
    }

    // Index contour pieces joined to the plain contour they go straight on into (ends within 1 m, the line carrying on
    // in the same direction, not turning back as where two contours touch at a cliff); a joined line is not an index
    // contour. Returns every line with whether it is an index contour.
    static List<(double[] p, bool index)> JoinIndexToPlain(List<double[]> plain, List<double[]> indexes)
    {
        var result = new List<(double[] p, bool index)>();
        var used = new bool[plain.Count];
        // The way a line leaves at its end (end 0: its first point, 1: its last): from a point 3 m back (or its far end)
        bool Away(double[] p, int end, out double x, out double y, out double dx, out double dy)
        {
            int n = p.Length / 2, i = end == 0 ? 0 : n - 1, step = end == 0 ? 1 : -1;
            x = p[2 * i]; y = p[2 * i + 1];
            dx = dy = 0;
            if (n < 2 || MapGeometry.IsClosed(p, 1.0)) return false;
            int k = i + step;
            while (k + step >= 0 && k + step < n && MapGeometry.Dist(x, y, p[2 * k], p[2 * k + 1]) < 3) k += step;
            double d = MapGeometry.Dist(x, y, p[2 * k], p[2 * k + 1]);
            if (d < 1e-6) return false;
            dx = (x - p[2 * k]) / d; dy = (y - p[2 * k + 1]) / d;
            return true;
        }
        // The plain lines' ends, by 2 m squares
        var ends = new Dictionary<long, List<(int j, int e, double x, double y, double dx, double dy)>>();
        for (int j = 0; j < plain.Count; j++)
            for (int e = 0; e < 2; e++)
            {
                if (!Away(plain[j], e, out double x, out double y, out double dx, out double dy)) continue;
                long key = (long)Math.Floor(x / 2) * 1000003L + (long)Math.Floor(y / 2);
                if (!ends.TryGetValue(key, out var list)) ends[key] = list = new List<(int j, int e, double x, double y, double dx, double dy)>();
                list.Add((j, e, x, y, dx, dy));
            }
        foreach (double[] start in indexes)
        {
            double[] line = start;
            bool mixed = false;
            for (int end = 0; end < 2; end++)
            {
                // Each end may carry on into several plain pieces one after the other
                for (int guard = 0; guard < 50; guard++)
                {
                    if (!Away(line, end, out double xi, out double yi, out double dxi, out double dyi)) break;
                    int found = -1, foundEnd = 0;
                    long cx = (long)Math.Floor(xi / 2), cy = (long)Math.Floor(yi / 2);
                    for (long gy = cy - 1; gy <= cy + 1 && found < 0; gy++)
                        for (long gx = cx - 1; gx <= cx + 1 && found < 0; gx++)
                        {
                            if (!ends.TryGetValue(gx * 1000003L + gy, out List<(int j, int e, double x, double y, double dx, double dy)> here)) continue;
                            foreach (var t in here)
                                if (!used[t.j] && MapGeometry.Dist(xi, yi, t.x, t.y) <= 1.0 && dxi * t.dx + dyi * t.dy < -0.7)
                                { found = t.j; foundEnd = t.e; break; }
                        }
                    if (found < 0) break;
                    used[found] = true;
                    mixed = true;
                    double[] other = plain[found];
                    if (end == 1)
                    {
                        // line ... end, then the plain piece from its meeting end on
                        double[] b = foundEnd == 0 ? other : Reverse(other);
                        var m = new double[line.Length + b.Length - 2];
                        Array.Copy(line, m, line.Length);
                        Array.Copy(b, 2, m, line.Length, b.Length - 2);
                        line = m;
                    }
                    else
                    {
                        // the plain piece up to its meeting end, then line
                        double[] b = foundEnd == 1 ? other : Reverse(other);
                        var m = new double[b.Length + line.Length - 2];
                        Array.Copy(b, m, b.Length - 2);
                        Array.Copy(line, 0, m, b.Length - 2, line.Length);
                        line = m;
                    }
                }
            }
            result.Add((line, !mixed));
        }
        for (int j = 0; j < plain.Count; j++) if (!used[j]) result.Add((plain[j], false));
        return result;
    }

    static double[] Reverse(double[] p)
    {
        var q = new double[p.Length];
        for (int i = 0, n = p.Length / 2; i < n; i++) { q[2 * i] = p[2 * (n - 1 - i)]; q[2 * i + 1] = p[2 * (n - 1 - i) + 1]; }
        return q;
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

    // Water, which lies low: streams, ditches and narrow marshes (304-306, 309), the edges of lakes and ponds (301, 302)
    // as lines, and small marshes, water holes and springs (308, 303, 312) as single points (x, y)
    public static List<double[]> ReadWater(byte[] linesJson, byte[] pointsJson)
    {
        var list = new List<double[]>();
        if (linesJson != null)
            foreach (var (code, xy, _) in Features(linesJson))
            {
                if (code.Length < 3 || xy.Count < 4) continue;
                string major = code.Substring(0, 3);
                if (major == "301" || major == "302" || major == "304" || major == "305" || major == "306" || major == "309")
                    list.Add(xy.ToArray());
            }
        if (pointsJson != null)
            foreach (var (code, xy, _) in Features(pointsJson))
            {
                if (code.Length < 3 || xy.Count < 2) continue;
                string major = code.Substring(0, 3);
                if (major == "308" || major == "303" || major == "312") list.Add(new[] { xy[0], xy[1] });
            }
        return list;
    }

    // Slope ticks: points of the contour and form line symbols: x, y, 1 for a form line's tick, the way the tick points
    // downhill (x, y; NaN for a map imported before 2026-11-23, which did not save it) and the symbol (101001)
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
            {
                int.TryParse(code.Substring(0, 6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int symbol);
                list.Add(new[] { xy[0], xy[1], major == "103" ? 1.0 : 0.0, down != null ? down[0] : double.NaN, down != null ? down[1] : double.NaN, symbol });
            }
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
            // Only looked for up to this feature's coordinates: searching the rest of the file for a "Down" that
            // isn't there took minutes on a big map (Fossum, 40 MB of lines: 2026-11-25)
            int d = text.IndexOf("\"Down\":[", at, coords - at, StringComparison.Ordinal);
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
