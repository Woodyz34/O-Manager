using System;
using System.Collections.Generic;

// Works out the ground's height from the map's contour lines, so no outside height data is needed.
//
// A contour has no height written on it, but the map tells which way is up:
//   - slope ticks (101.1) point downhill,
//   - a closed contour without ticks is a hill (ISOM: depressions always get ticks).
// That gives most contours an "uphill side". Two neighbouring contours then differ by one contour interval
// (a slope between them) or by none (a hilltop or a hollow between them), depending on which sides face each other.
// The levels are solved for all contours together (least squares, so a few odd neighbours don't spoil the rest),
// and the ground between the contours is filled in smoothly.
// Tested on Illern against Lantmäteriet's height model: about 1.5 m off (median) inside the mapped area.
namespace MapImport
{
    public static class ContourHeights
    {
        class Contour
        {
            public int id;
            public double[] p;        // analysis-cell coordinates
            public bool closed;
            public int orientation;   // +1: the left side is uphill, -1: downhill, 0: unknown
            public int votesUp, votesDown;
        }

        public static float[] Build(MapFile map, MapExtent extent, int rows, int cols, float cellSize, out bool ok, Action<string> log = null)
        {
            var heights = new float[rows * cols];
            ok = false;
            double mpm = map.MetersPerMm;
            double equidistance = SymbolRules.Equidistance(map);

            // Analysis grid: fine enough that neighbouring contours stay apart
            int k = Math.Max(1, (int)Math.Ceiling(cellSize / 2.5));
            double res = cellSize / k;
            int R = rows * k, C = cols * k;
            double scale = mpm / res;
            double ox = extent.minX, oy = extent.minY;

            var contours = new List<Contour>();
            var lines = new List<double[]>();
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Line || !SymbolRules.IsContour(o.symbol)) continue;
                foreach (double[] part in o.parts)
                    if (part.Length >= 4) lines.Add(part);
            }
            // A contour is often drawn as several lines (cut where a tick, a text or a cliff sits). Joined end to end
            // again, a ring round a hill is closed and so known as a hill; before (2026-11-20, Anders) the pieces of a
            // hill's rings had no known up side, were guessed from their neighbours and a hilltop could become a hollow.
            foreach (double[] part in StitchContours(lines, 0.25))
            {
                var q = new double[part.Length];
                for (int i = 0; i < part.Length; i += 2) { q[i] = (part[i] - ox) * scale; q[i + 1] = (part[i + 1] - oy) * scale; }
                contours.Add(new Contour { id = contours.Count + 1, p = q, closed = MapGeometry.IsClosed(part, 0.25) });
            }
            if (contours.Count < 3) return heights;

            // Contour ids on the analysis grid
            var ids = new int[R * C];
            foreach (Contour c in contours)
            {
                for (int i = 0; i + 3 < c.p.Length; i += 2)
                {
                    GridBuilder.Supercover(c.p[i], c.p[i + 1], c.p[i + 2], c.p[i + 3], C, R, (x, y) => ids[y * C + x] = c.id);
                }
            }

            // ---- Which way is up ----
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Point || !SymbolRules.IsSlopeTick(o.symbol) || o.symbol.point == null) continue;
                if (!TickDirection(o.symbol, out double tx, out double ty)) continue;
                double a = o.angle * Math.PI / 180;
                double dx = tx * Math.Cos(a) - ty * Math.Sin(a), dy = tx * Math.Sin(a) + ty * Math.Cos(a);
                double px = (o.parts[0][0] - ox) * scale, py = (o.parts[0][1] - oy) * scale;
                int cid = Nearest(ids, C, R, px, py, 4);
                if (cid == 0) continue;
                int side = SideOf(contours[cid - 1].p, px + dx * 100, py + dy * 100);
                if (side > 0) contours[cid - 1].votesDown += 5;   // the tick's side is downhill
                else if (side < 0) contours[cid - 1].votesUp += 5;
            }
            foreach (Contour c in contours)
            {
                if (c.votesUp + c.votesDown == 0 && c.closed)
                {
                    // A hill: the inside is up. The inside is on the left when the ring runs counter-clockwise.
                    if (MapGeometry.SignedArea(c.p) > 0) c.votesUp += 3; else c.votesDown += 3;
                }
                c.orientation = c.votesUp > c.votesDown ? 1 : c.votesDown > c.votesUp ? -1 : 0;
            }

            // ---- Neighbours: the first other contour straight out to each side ----
            var seen = new Dictionary<long, int[]>();   // (c, L) -> counts on the left and right of c
            int maxSteps = (int)(200 / res);
            foreach (Contour c in contours)
            {
                double next = 0;          // distance along this segment to the next sample
                double spacing = 10 / res;  // a sample every 10 m
                for (int i = 0; i + 3 < c.p.Length; i += 2)
                {
                    double x0 = c.p[i], y0 = c.p[i + 1], x1 = c.p[i + 2], y1 = c.p[i + 3];
                    double len = MapGeometry.Dist(x0, y0, x1, y1);
                    if (len < 1e-9) continue;
                    double tx = (x1 - x0) / len, ty = (y1 - y0) / len;
                    double nx = -ty, ny = tx;   // left
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
                                if (v == 0) continue;
                                // The ray met this contour again (across a small ring, round a bend): what lies beyond
                                // is not its neighbour on this side. Before (2026-11-20) it went on, so the ring round a
                                // hilltop saw the ring outside it on its inner side too, and a hill could become a hollow.
                                if (v == c.id) { if (t > 3.0) break; continue; }
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

            // Pairs seen from both contours: which side each one sees the other on
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
            if (edges.Count == 0) return heights;
            var adj = new List<int>[contours.Count + 1];
            for (int i = 0; i <= contours.Count; i++) adj[i] = new List<int>();
            for (int e = 0; e < edges.Count; e++) { adj[edges[e].a].Add(e); adj[edges[e].b].Add(e); }

            // Contours without a known up side: assume the slope goes on the same way as next door
            for (int pass = 0; pass < 6; pass++)
            {
                foreach (Contour c in contours)
                {
                    if (c.orientation != 0) continue;
                    double vote = 0;
                    foreach (int e in adj[c.id])
                    {
                        var ed = edges[e];
                        int other = ed.a == c.id ? ed.b : ed.a;
                        int mySide = ed.a == c.id ? ed.sa : ed.sb;
                        int otherSide = ed.a == c.id ? ed.sb : ed.sa;
                        int o = contours[other - 1].orientation;
                        if (o == 0) continue;
                        // Slope continues: my side towards the other is uphill exactly when the other's side towards me is downhill
                        vote += ed.w * -o * otherSide * mySide;
                    }
                    if (vote != 0) c.orientation = vote > 0 ? 1 : -1;
                }
            }
            int known = 0;
            foreach (Contour c in contours) if (c.orientation != 0) known++;
            if (known < contours.Count / 4) return heights;

            // ---- Levels: least squares over all neighbour pairs, down-weighting pairs that disagree ----
            var diff = new double[edges.Count];
            var weight = new double[edges.Count];
            for (int e = 0; e < edges.Count; e++)
            {
                var ed = edges[e];
                int oa = contours[ed.a - 1].orientation, ob = contours[ed.b - 1].orientation;
                if (oa == 0 || ob == 0) { weight[e] = 0; continue; }
                diff[e] = (oa * ed.sa - ob * ed.sb) / 2.0;   // level(b) - level(a)
                weight[e] = ed.w;
            }
            var level = new double[contours.Count + 1];
            var baseWeight = (double[])weight.Clone();
            for (int outer = 0; outer < 5; outer++)
            {
                SolveLevels(level, edges, diff, weight);
                for (int e = 0; e < edges.Count; e++)
                {
                    double r = level[edges[e].b] - level[edges[e].a] - diff[e];
                    weight[e] = baseWeight[e] / (1 + (r / 0.5) * (r / 0.5));
                }
            }

            // ---- The ground between the contours: fixed on the lines, smooth in between ----
            var fixedValue = new double[rows * cols];
            var fixedCount = new int[rows * cols];
            double toGrid = 1.0 / k;
            foreach (Contour c in contours)
            {
                if (adj[c.id].Count == 0) continue;
                double z = level[c.id] * equidistance;
                for (int i = 0; i + 3 < c.p.Length; i += 2)
                {
                    GridBuilder.Supercover(c.p[i] * toGrid, c.p[i + 1] * toGrid, c.p[i + 2] * toGrid, c.p[i + 3] * toGrid, cols, rows,
                        (x, y) => { fixedValue[y * cols + x] += z; fixedCount[y * cols + x]++; });
                }
            }
            var isFixed = new bool[rows * cols];
            double mean = 0; int nf = 0;
            for (int i = 0; i < heights.Length; i++)
            {
                if (fixedCount[i] > 0) { isFixed[i] = true; fixedValue[i] /= fixedCount[i]; mean += fixedValue[i]; nf++; }
            }
            if (nf == 0) return heights;
            mean /= nf;
            var z0 = new double[rows * cols];
            for (int i = 0; i < z0.Length; i++) z0[i] = isFixed[i] ? fixedValue[i] : mean;
            SolveLaplace(z0, isFixed, rows, cols);
            double min = double.MaxValue;
            for (int i = 0; i < z0.Length; i++) min = Math.Min(min, z0[i]);
            for (int i = 0; i < z0.Length; i++) heights[i] = (float)(z0[i] - min);
            ok = true;
            log?.Invoke($"{contours.Count} contours, {known} with known slope direction, {edges.Count} neighbour pairs");
            return heights;
        }

        // Joins contour lines whose ends meet (within tolerance, map mm), each end to its nearest other end when that
        // end's nearest is this one too; a line whose two ends meet is closed up
        internal static List<double[]> StitchContours(List<double[]> parts, double tolerance)
        {
            var left = new List<List<double>>();
            foreach (double[] p in parts) left.Add(new List<double>(p));
            double t2 = tolerance * tolerance;
            double D2(double ax, double ay, double bx, double by) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);
            bool changed = true;
            int guard = 0;
            var turned = new HashSet<List<double>>();
            while (changed && guard++ < 50)
            {
                changed = false;
                for (int a = 0; a < left.Count; a++)
                {
                    List<double> A = left[a];
                    int n = A.Count;
                    if (n >= 8 && D2(A[0], A[1], A[n - 2], A[n - 1]) <= t2) continue;   // closed already
                    double ex = A[n - 2], ey = A[n - 1];
                    // The nearest end of another line to this line's end
                    int best = -1;
                    bool bestHead = true;
                    double bd = t2;
                    for (int b = 0; b < left.Count; b++)
                    {
                        if (b == a) continue;
                        List<double> B = left[b];
                        int m = B.Count;
                        double h = D2(ex, ey, B[0], B[1]), t = D2(ex, ey, B[m - 2], B[m - 1]);
                        if (h < bd) { bd = h; best = b; bestHead = true; }
                        if (t < bd) { bd = t; best = b; bestHead = false; }
                    }
                    if (best < 0)
                    {
                        // Nothing at the end: turn the line round once, so its start is tried too
                        if (!turned.Contains(A)) { turned.Add(A); A.Reverse(); for (int i = 0; i + 1 < A.Count; i += 2) { double t = A[i]; A[i] = A[i + 1]; A[i + 1] = t; } a--; }
                        continue;
                    }
                    // ...and no end of a third line nearer to that end (a fork: leave it)
                    List<double> J = left[best];
                    double jx = bestHead ? J[0] : J[J.Count - 2], jy = bestHead ? J[1] : J[J.Count - 1];
                    bool mutual = true;
                    for (int c = 0; c < left.Count && mutual; c++)
                    {
                        List<double> Cc = left[c];
                        int m = Cc.Count;
                        bool skipHead = c == best && bestHead, skipTail = (c == best && !bestHead) || c == a;
                        if (!skipHead && D2(jx, jy, Cc[0], Cc[1]) < bd) mutual = false;
                        if (!skipTail && D2(jx, jy, Cc[m - 2], Cc[m - 1]) < bd) mutual = false;
                    }
                    if (!mutual) continue;
                    if (!bestHead)
                    {
                        var r = new List<double>(J.Count);
                        for (int i = J.Count - 2; i >= 0; i -= 2) { r.Add(J[i]); r.Add(J[i + 1]); }
                        J = r;
                    }
                    A.AddRange(J.GetRange(2, J.Count - 2));
                    left.RemoveAt(best);
                    if (best < a) a--;
                    changed = true;
                }
            }
            var result = new List<double[]>(left.Count);
            foreach (List<double> l in left)
            {
                int n = l.Count;
                // Ends that meet: closed exactly
                if (n >= 8 && D2(l[0], l[1], l[n - 2], l[n - 1]) <= t2) { l[n - 2] = l[0]; l[n - 1] = l[1]; }
                result.Add(l.ToArray());
            }
            return result;
        }

        // Weighted least squares for the levels: minimise sum w (level[b] - level[a] - diff)^2 with conjugate gradients.
        // (Plain sweeps converge far too slowly over a whole map and leave the map tilted.)
        internal static void SolveLevels(double[] x, List<(int a, int b, int sa, int sb, double w)> edges, double[] diff, double[] weight)
        {
            int n = x.Length;
            const double eps = 1e-6;   // ties every level weakly to 0 so the system has one answer
            var rhs = new double[n];
            for (int e = 0; e < edges.Count; e++)
            {
                double w = weight[e];
                if (w <= 0) continue;
                rhs[edges[e].b] += w * diff[e];
                rhs[edges[e].a] -= w * diff[e];
            }
            double[] Apply(double[] v)
            {
                var r = new double[n];
                for (int i = 0; i < n; i++) r[i] = eps * v[i];
                for (int e = 0; e < edges.Count; e++)
                {
                    double w = weight[e];
                    if (w <= 0) continue;
                    double d = w * (v[edges[e].b] - v[edges[e].a]);
                    r[edges[e].b] += d;
                    r[edges[e].a] -= d;
                }
                return r;
            }
            double[] ax = Apply(x);
            var res = new double[n];
            for (int i = 0; i < n; i++) res[i] = rhs[i] - ax[i];
            var dir = (double[])res.Clone();
            double rr = 0;
            for (int i = 0; i < n; i++) rr += res[i] * res[i];
            for (int it = 0; it < 5000 && rr > 1e-12; it++)
            {
                double[] ad = Apply(dir);
                double dad = 0;
                for (int i = 0; i < n; i++) dad += dir[i] * ad[i];
                if (dad <= 0) break;
                double alpha = rr / dad;
                double rrNew = 0;
                for (int i = 0; i < n; i++)
                {
                    x[i] += alpha * dir[i];
                    res[i] -= alpha * ad[i];
                    rrNew += res[i] * res[i];
                }
                double beta = rrNew / rr;
                rr = rrNew;
                for (int i = 0; i < n; i++) dir[i] = res[i] + beta * dir[i];
            }
        }

        // Smooth fill between fixed cells: first on coarser grids, then refined
        internal static void SolveLaplace(double[] z, bool[] isFixed, int rows, int cols)
        {
            if (rows > 64 && cols > 64)
            {
                int r2 = (rows + 1) / 2, c2 = (cols + 1) / 2;
                var zc = new double[r2 * c2];
                var fc = new bool[r2 * c2];
                var cnt = new int[r2 * c2];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c, j = (r / 2) * c2 + c / 2;
                        if (isFixed[i]) { if (!fc[j]) zc[j] = 0; fc[j] = true; zc[j] += z[i]; cnt[j]++; }
                    }
                double mean = 0; int nf = 0;
                for (int j = 0; j < zc.Length; j++) if (fc[j]) { zc[j] /= cnt[j]; mean += zc[j]; nf++; }
                mean = nf > 0 ? mean / nf : 0;
                for (int j = 0; j < zc.Length; j++) if (!fc[j]) zc[j] = mean;
                SolveLaplace(zc, fc, r2, c2);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c;
                        if (!isFixed[i]) z[i] = zc[(r / 2) * c2 + c / 2];
                    }
            }
            const double omega = 1.8;
            int iterations = 60;
            for (int it = 0; it < iterations; it++)
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int i = r * cols + c;
                        if (isFixed[i]) continue;
                        double s = 0; int n = 0;
                        if (c > 0) { s += z[i - 1]; n++; }
                        if (c < cols - 1) { s += z[i + 1]; n++; }
                        if (r > 0) { s += z[i - cols]; n++; }
                        if (r < rows - 1) { s += z[i + cols]; n++; }
                        z[i] += omega * (s / n - z[i]);
                    }
                }
            }
        }

        // The direction a slope tick points, from its symbol: towards its element point furthest from the origin
        static bool TickDirection(MapSymbol s, out double x, out double y)
        {
            x = y = 0;
            double best = 0;
            foreach (SymbolElement e in s.point)
            {
                for (int i = 0; i < e.x.Length; i++)
                {
                    double d = e.x[i] * e.x[i] + e.y[i] * e.y[i];
                    if (d > best) { best = d; x = e.x[i]; y = e.y[i]; }
                }
            }
            if (best <= 0) return false;
            double len = Math.Sqrt(best);
            x /= len; y /= len;
            return true;
        }

        internal static int Nearest(int[] ids, int C, int R, double px, double py, int reach)
        {
            int cx = (int)Math.Floor(px), cy = (int)Math.Floor(py);
            for (int r = 0; r <= reach; r++)
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (x < 0 || y < 0 || x >= C || y >= R) continue;
                        int v = ids[y * C + x];
                        if (v != 0) return v;
                    }
            return 0;
        }

        // +1 if the point is on the left of the polyline (at its nearest segment), -1 on the right
        internal static int SideOf(double[] p, double x, double y)
        {
            double best = double.MaxValue; int side = 0;
            for (int i = 0; i + 3 < p.Length; i += 2)
            {
                double ax = p[i], ay = p[i + 1], dx = p[i + 2] - ax, dy = p[i + 3] - ay;
                double l2 = dx * dx + dy * dy;
                if (l2 <= 0) continue;
                double t = Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / l2));
                double qx = ax + t * dx - x, qy = ay + t * dy - y;
                double d = qx * qx + qy * qy;
                if (d < best)
                {
                    best = d;
                    side = dx * (y - ay) - dy * (x - ax) > 0 ? 1 : -1;
                }
            }
            return side;
        }
    }
}
