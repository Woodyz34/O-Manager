using System;
using System.Collections.Generic;

// The edge of the finished part of a map. Map files often hold parts the mapper has not finished: streets drawn
// without houses, olive green without buildings, roads in white. Those parts use the same symbols as the finished
// map, so no single symbol tells them apart. What does: the finished map is drawn edge to edge (hardly any white
// paper) and has contour lines. Guess finds the largest such region, a little wider, and gives its outline as a
// polygon on the paper. The player sees it on a small picture of the map and can move its corners before importing.
// Everything outside the edge is left out of the picture, the grid and the map objects.
// Tried on Ängaryd sprint (2026-11-05) against the mapper's own export: 99.5 % of the finished map inside the edge.
namespace MapImport
{
    public class MapPreview
    {
        public int width, height;
        public byte[] rgba;          // row 0 at the top
        public MapExtent extent;     // paper mm the picture covers
        public double pixelsPerMm;

        public double PaperX(double px) => extent.minX + px / pixelsPerMm;
        public double PaperY(double py) => extent.maxY - py / pixelsPerMm;
        public double PixelX(double x) => (x - extent.minX) * pixelsPerMm;
        public double PixelY(double y) => (extent.maxY - y) * pixelsPerMm;
    }

    public static class MapBoundary
    {
        const double BlockMeters = 5;         // the grid the guess works on
        const int ContourReachBlocks = 16;    // 80 m: finished ground is this close to a contour line
        const int CoverBlocks = 9;            // 45 m square for "drawn edge to edge"
        const double CoverShare = 0.5;        // at least half of it not white paper
        const int CloseBlocks = 4;            // 20 m: small notches in the edge are filled
        const int MarginBlocks = 2;           // 10 m added all round, so nothing at the edge is cut

        // A small picture of the whole map (about 1 pixel per metre, at most maxSide pixels each way)
        public static MapPreview Preview(MapFile map, int maxSide = 3000)
        {
            MapExtent extent = MapExtent.Of(map);
            double ppm = Math.Min(map.MetersPerMm / 1.0, maxSide / Math.Max(1.0, Math.Max(extent.Width, extent.Height)));
            var painter = new MapPainter(map, extent, ppm);
            painter.Paint();
            return new MapPreview { width = painter.width, height = painter.height, rgba = painter.rgba, extent = extent, pixelsPerMm = ppm };
        }

        // The guessed edge, x,y pairs in paper mm, or null when the whole map looks finished (or it can't tell)
        public const int MaxCorners = 30;

        public static double[] Guess(MapFile map, MapPreview preview)
        {
            double blockMm = BlockMeters / map.MetersPerMm;
            MapExtent e = preview.extent;
            int bw = Math.Max(1, (int)Math.Ceiling(e.Width / blockMm));
            int bh = Math.Max(1, (int)Math.Ceiling(e.Height / blockMm));
            int n = bw * bh;

            // Share of each block that is not white paper (rows from the top, like the picture)
            var fill = new float[n];
            var count = new int[n];
            for (int py = 0; py < preview.height; py++)
            {
                int by = Math.Min(bh - 1, (int)(py / preview.pixelsPerMm / blockMm));
                for (int px = 0; px < preview.width; px++)
                {
                    int bx = Math.Min(bw - 1, (int)(px / preview.pixelsPerMm / blockMm));
                    int i = (py * preview.width + px) * 4;
                    byte r = preview.rgba[i], g = preview.rgba[i + 1], b = preview.rgba[i + 2];
                    int k = by * bw + bx;
                    count[k]++;
                    if (Math.Min(r, Math.Min(g, b)) < 235) fill[k] += 1;
                }
            }
            int drawnBlocks = 0;
            for (int i = 0; i < n; i++)
            {
                fill[i] = count[i] > 0 ? fill[i] / count[i] : 0;
                if (fill[i] > 0) drawnBlocks++;
            }
            if (drawnBlocks == 0) return null;

            // Blocks a contour line runs through
            var contour = new bool[n];
            int contourBlocks = 0;
            foreach (MapObject o in map.objects)
            {
                MapSymbol s = o.symbol;
                if (s == null || s.kind != SymbolKind.Line || (s.major != 101 && s.major != 102 && s.major != 103)) continue;
                foreach (double[] p in o.parts)
                {
                    for (int i = 0; i + 1 < p.Length; i += 2)
                    {
                        double x0 = p[i], y0 = p[i + 1];
                        double x1 = i + 3 < p.Length ? p[i + 2] : x0, y1 = i + 3 < p.Length ? p[i + 3] : y0;
                        int steps = Math.Max(1, (int)Math.Ceiling(MapGeometry.Dist(x0, y0, x1, y1) / (blockMm / 2)));
                        for (int k = 0; k <= steps; k++)
                        {
                            double t = k / (double)steps;
                            int bx = (int)((x0 + (x1 - x0) * t - e.minX) / blockMm);
                            int by = (int)((e.maxY - (y0 + (y1 - y0) * t)) / blockMm);
                            if (bx < 0 || by < 0 || bx >= bw || by >= bh) continue;
                            int idx = by * bw + bx;
                            if (!contour[idx]) { contour[idx] = true; contourBlocks++; }
                        }
                    }
                }
            }
            if (contourBlocks == 0) return null;   // no contours to go by

            // Finished: near a contour, and drawn edge to edge round about
            int[] toContour = Distance(contour, bw, bh, ContourReachBlocks + 1);
            float[] cover = BoxMean(fill, bw, bh, CoverBlocks / 2);
            var finished = new bool[n];
            for (int i = 0; i < n; i++) finished[i] = toContour[i] <= ContourReachBlocks && cover[i] > CoverShare;
            finished = Largest(finished, bw, bh);
            if (finished == null) return null;
            finished = Grow(Shrink(Grow(finished, bw, bh, CloseBlocks), bw, bh, CloseBlocks), bw, bh, MarginBlocks);
            finished = FillHoles(finished, bw, bh);

            // Nothing worth cutting, or so little left that the guess is not to be trusted
            int inside = 0, drawnOutside = 0;
            for (int i = 0; i < n; i++)
            {
                if (finished[i]) inside++;
                else if (fill[i] > 0) drawnOutside++;
            }
            if (drawnOutside < drawnBlocks * 0.03 || inside < drawnBlocks * 0.2) return null;

            List<(int x, int y)> outline = Outline(finished, bw, bh);
            if (outline == null || outline.Count < 3) return null;
            var poly = new List<double>();
            // Few enough corners to move by hand: loosen the fit until there are at most MaxCorners
            List<(int x, int y)> corners = Simplify(outline, 2.5);
            for (double tolerance = 3; corners.Count > MaxCorners && tolerance < 40; tolerance *= 1.25)
                corners = Simplify(outline, tolerance);
            foreach ((int x, int y) in corners)
            {
                poly.Add(Math.Min(e.maxX, e.minX + x * blockMm));
                poly.Add(Math.Max(e.minY, e.maxY - y * blockMm));
            }
            return poly.Count >= 6 ? poly.ToArray() : null;
        }

        // ---- Using the edge ----

        public static bool Inside(double[] poly, double x, double y)
        {
            bool inside = false;
            int n = poly.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[2 * i], yi = poly[2 * i + 1], xj = poly[2 * j], yj = poly[2 * j + 1];
                if ((yi > y) != (yj > y) && x < xi + (xj - xi) * (y - yi) / (yj - yi)) inside = !inside;
            }
            return inside;
        }

        // The extent cut down to the edge's box, with the usual small margin
        public static MapExtent Clip(MapExtent e, double[] poly)
        {
            if (poly == null || poly.Length < 6) return e;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i + 1 < poly.Length; i += 2)
            {
                x0 = Math.Min(x0, poly[i]); x1 = Math.Max(x1, poly[i]);
                y0 = Math.Min(y0, poly[i + 1]); y1 = Math.Max(y1, poly[i + 1]);
            }
            const double margin = 2;
            var c = new MapExtent
            {
                minX = Math.Max(e.minX, x0 - margin),
                minY = Math.Max(e.minY, y0 - margin),
                maxX = Math.Min(e.maxX, x1 + margin),
                maxY = Math.Min(e.maxY, y1 + margin)
            };
            return c.Width > 0 && c.Height > 0 ? c : e;
        }

        // For each row of a picture (row 0 at the top), the spans of pixels outside the edge are passed on
        public static void ForOutside(double[] poly, int width, int height, Func<int, double> rowToPaperY,
                                      Func<double, double> paperXToPixel, Action<int, int, int> outsideSpan)
        {
            var xs = new List<double>();
            int n = poly.Length / 2;
            for (int row = 0; row < height; row++)
            {
                double y = rowToPaperY(row);
                xs.Clear();
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double yi = poly[2 * i + 1], yj = poly[2 * j + 1];
                    if ((yi > y) != (yj > y)) xs.Add(paperXToPixel(poly[2 * i] + (poly[2 * j] - poly[2 * i]) * (y - yi) / (yj - yi)));
                }
                xs.Sort();
                int from = 0;
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int a = Math.Max(0, Math.Min(width, (int)Math.Ceiling(xs[k] - 0.5)));
                    int b = Math.Max(0, Math.Min(width, (int)Math.Floor(xs[k + 1] - 0.5) + 1));
                    if (a > from) outsideSpan(row, from, a);
                    from = Math.Max(from, b);
                }
                if (from < width) outsideSpan(row, from, width);
            }
        }

        // The parts of a line inside the edge
        public static List<double[]> ClipLine(double[] p, double[] poly)
        {
            var runs = new List<double[]>();
            var run = new List<double>();
            for (int i = 0; i + 1 < p.Length; i += 2)
            {
                if (Inside(poly, p[i], p[i + 1]))
                {
                    run.Add(p[i]); run.Add(p[i + 1]);
                }
                else
                {
                    if (run.Count >= 4) runs.Add(run.ToArray());
                    run.Clear();
                }
            }
            if (run.Count >= 4) runs.Add(run.ToArray());
            return runs;
        }

        // ---- Block grid helpers ----

        static int[] Distance(bool[] marked, int w, int h, int limit)
        {
            int n = w * h;
            var dist = new int[n];
            var queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (marked[i]) { dist[i] = 0; queue.Enqueue(i); }
                else dist[i] = limit;
            }
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int d0 = dist[idx];
                if (d0 + 1 >= limit) continue;
                int r = idx / w, c = idx % w;
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int rr = r + dr, cc = c + dc;
                        if (rr < 0 || cc < 0 || rr >= h || cc >= w) continue;
                        int j = rr * w + cc;
                        if (dist[j] > d0 + 1) { dist[j] = d0 + 1; queue.Enqueue(j); }
                    }
            }
            return dist;
        }

        static float[] BoxMean(float[] v, int w, int h, int r)
        {
            // Summed area table
            var s = new double[(w + 1) * (h + 1)];
            for (int y = 0; y < h; y++)
            {
                double row = 0;
                for (int x = 0; x < w; x++)
                {
                    row += v[y * w + x];
                    s[(y + 1) * (w + 1) + x + 1] = s[y * (w + 1) + x + 1] + row;
                }
            }
            var m = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Max(0, x - r), x1 = Math.Min(w, x + r + 1), y0 = Math.Max(0, y - r), y1 = Math.Min(h, y + r + 1);
                    double sum = s[y1 * (w + 1) + x1] - s[y0 * (w + 1) + x1] - s[y1 * (w + 1) + x0] + s[y0 * (w + 1) + x0];
                    m[y * w + x] = (float)(sum / ((x1 - x0) * (y1 - y0)));
                }
            return m;
        }

        static bool[] Largest(bool[] m, int w, int h)
        {
            var label = new int[w * h];
            int best = 0, bestSize = 0, next = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < m.Length; i++)
            {
                if (!m[i] || label[i] != 0) continue;
                next++;
                int size = 0;
                label[i] = next;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int q = stack.Pop();
                    size++;
                    int r = q / w, c = q % w;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int rr = r + dr, cc = c + dc;
                            if (rr < 0 || cc < 0 || rr >= h || cc >= w) continue;
                            int j = rr * w + cc;
                            if (m[j] && label[j] == 0) { label[j] = next; stack.Push(j); }
                        }
                }
                if (size > bestSize) { bestSize = size; best = next; }
            }
            if (best == 0) return null;
            var o = new bool[m.Length];
            for (int i = 0; i < m.Length; i++) o[i] = label[i] == best;
            return o;
        }

        // Round growing and shrinking (distance in 8 directions, about a circle)
        static bool[] Grow(bool[] m, int w, int h, int r)
        {
            int[] d = Distance(m, w, h, r + 1);
            var o = new bool[m.Length];
            for (int i = 0; i < m.Length; i++) o[i] = d[i] <= r;
            return o;
        }

        static bool[] Shrink(bool[] m, int w, int h, int r)
        {
            var inv = new bool[m.Length];
            for (int i = 0; i < m.Length; i++) inv[i] = !m[i];
            // Outside the grid counts as outside
            int[] d = Distance(inv, w, h, r + 1);
            var o = new bool[m.Length];
            for (int i = 0; i < m.Length; i++)
            {
                int y = i / w, x = i % w;
                int edge = Math.Min(Math.Min(x + 1, w - x), Math.Min(y + 1, h - y));
                o[i] = d[i] > r && edge > r;
            }
            return o;
        }

        static bool[] FillHoles(bool[] m, int w, int h)
        {
            var outside = new bool[m.Length];
            var queue = new Queue<int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (x != 0 && y != 0 && x != w - 1 && y != h - 1) continue;
                    int i = y * w + x;
                    if (!m[i] && !outside[i]) { outside[i] = true; queue.Enqueue(i); }
                }
            while (queue.Count > 0)
            {
                int q = queue.Dequeue();
                int r = q / w, c = q % w;
                for (int d = 0; d < 4; d++)
                {
                    int rr = r + (d == 0 ? 1 : d == 1 ? -1 : 0), cc = c + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (rr < 0 || cc < 0 || rr >= h || cc >= w) continue;
                    int j = rr * w + cc;
                    if (m[j] || outside[j]) continue;
                    outside[j] = true;
                    queue.Enqueue(j);
                }
            }
            var o = new bool[m.Length];
            for (int i = 0; i < m.Length; i++) o[i] = !outside[i];
            return o;
        }

        // The outer edge of the region, as block corners in order (x right, y down). The longest loop of cell edges
        // with the region on one side.
        static List<(int, int)> Outline(bool[] m, int w, int h)
        {
            bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && m[y * w + x];
            // Directed edges with the region on the right (y down): from corner -> to corner
            var next = new Dictionary<long, List<long>>();
            long Key(int x, int y) => ((long)x << 32) | (uint)y;
            void AddEdge(int x0, int y0, int x1, int y1)
            {
                long k = Key(x0, y0);
                if (!next.TryGetValue(k, out List<long> list)) next[k] = list = new List<long>(1);
                list.Add(Key(x1, y1));
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!In(x, y)) continue;
                    if (!In(x, y - 1)) AddEdge(x, y, x + 1, y);           // top, going right
                    if (!In(x + 1, y)) AddEdge(x + 1, y, x + 1, y + 1);   // right, going down
                    if (!In(x, y + 1)) AddEdge(x + 1, y + 1, x, y + 1);   // bottom, going left
                    if (!In(x - 1, y)) AddEdge(x, y + 1, x, y);           // left, going up
                }
            List<(int, int)> best = null;
            while (next.Count > 0)
            {
                var loop = new List<(int, int)>();
                long start = 0;
                foreach (long k in next.Keys) { start = k; break; }
                long at = start;
                int guard = 0;
                while (next.TryGetValue(at, out List<long> outs) && outs.Count > 0 && guard++ < 10_000_000)
                {
                    long to = outs[outs.Count - 1];
                    outs.RemoveAt(outs.Count - 1);
                    if (outs.Count == 0) next.Remove(at);
                    loop.Add(((int)(at >> 32), (int)(uint)at));
                    at = to;
                    if (at == start) break;
                }
                if (best == null || loop.Count > best.Count) best = loop;
            }
            return best;
        }

        // Douglas-Peucker on a closed loop, tolerance in blocks
        static List<(int x, int y)> Simplify(List<(int x, int y)> loop, double tolerance)
        {
            int n = loop.Count;
            if (n < 4) return loop;
            // Split the loop at its two most distant points
            int a = 0, b = 0;
            double far = -1;
            for (int i = 0; i < n; i++)
            {
                double d = Sq(loop[i].x - loop[0].x) + Sq(loop[i].y - loop[0].y);
                if (d > far) { far = d; b = i; }
            }
            var keep = new bool[n];
            keep[a] = keep[b] = true;
            Dp(loop, a, b, tolerance, keep);
            Dp(loop, b, n, tolerance, keep);
            var o = new List<(int, int)>();
            for (int i = 0; i < n; i++) if (keep[i]) o.Add(loop[i]);
            return o;
        }

        static void Dp(List<(int x, int y)> p, int from, int to, double tol, bool[] keep)
        {
            int n = p.Count;
            if (to - from < 2) return;
            var A = p[from % n];
            var B = p[to % n];
            double dx = B.x - A.x, dy = B.y - A.y, len = Math.Sqrt(dx * dx + dy * dy);
            int best = -1;
            double worst = tol;
            for (int i = from + 1; i < to; i++)
            {
                var P = p[i % n];
                double d = len < 1e-9 ? Math.Sqrt(Sq(P.x - A.x) + Sq(P.y - A.y)) : Math.Abs(dx * (A.y - P.y) - dy * (A.x - P.x)) / len;
                if (d > worst) { worst = d; best = i; }
            }
            if (best < 0) return;
            keep[best % n] = true;
            Dp(p, from, best, tol, keep);
            Dp(p, best, to, tol, keep);
        }

        static double Sq(double v) => v * v;
    }
}
