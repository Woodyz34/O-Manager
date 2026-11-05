using System;
using System.Collections.Generic;

// Draws a map picture from the map file's own symbols and colours: areas with fills, hatching and patterns,
// lines with dashes, double lines and symbols along them, and point symbols. Text is left out.
// Colours are drawn in the file's order (the top colour last), the same rule OCAD and Mapper follow.
// Plain C#: the result is an RGBA byte array, row 0 at the top.
namespace MapImport
{
    public class MapPainter
    {
        public readonly int width, height;
        public readonly byte[] rgba;
        readonly double minX, maxY, ppm;   // paper mm -> pixels
        readonly MapFile map;

        struct Op
        {
            public int priority;
            public int order;
            public Action draw;
        }
        readonly List<Op> ops = new List<Op>();

        public MapPainter(MapFile map, MapExtent extent, double pixelsPerMm)
        {
            this.map = map;
            ppm = pixelsPerMm;
            minX = extent.minX;
            maxY = extent.maxY;
            width = Math.Max(1, (int)Math.Ceiling((extent.maxX - extent.minX) * ppm));
            height = Math.Max(1, (int)Math.Ceiling((extent.maxY - extent.minY) * ppm));
            rgba = new byte[width * height * 4];
            for (int i = 0; i < rgba.Length; i++) rgba[i] = 255;   // white paper
            foreach (MapColor c in map.colors)
                if (c.maskSpot >= 0) inkAt = new byte[width * height];   // only maps with knockout colours need it
        }

        // The ink (spot colour priority + 1, 0 = paper or unknown) last painted on each pixel, for knockout colours
        byte[] inkAt;

        // Paints coverage a of a colour on pixel index p (not times 4)
        void Put(int p, MapColor c, float a)
        {
            int i = p * 4;
            if (c.maskSpot >= 0)
            {
                if (inkAt[p] != c.maskSpot + 1) return;   // only erases its own ink
                if (a >= 0.5f) inkAt[p] = 0;
            }
            else if (inkAt != null && a >= 0.5f) inkAt[p] = (byte)(c.spot >= 0 && c.spot < 255 ? c.spot + 1 : 0);
            if (a >= 0.999f)
            {
                rgba[i] = c.r; rgba[i + 1] = c.g; rgba[i + 2] = c.b;
            }
            else
            {
                rgba[i] = (byte)(rgba[i] + (c.r - rgba[i]) * a);
                rgba[i + 1] = (byte)(rgba[i + 1] + (c.g - rgba[i + 1]) * a);
                rgba[i + 2] = (byte)(rgba[i + 2] + (c.b - rgba[i + 2]) * a);
            }
        }

        // Picks a resolution that stays under maxSide pixels on the longest side
        public static double PixelsPerMm(MapExtent extent, double wanted, int maxSide)
        {
            double longest = Math.Max(extent.maxX - extent.minX, extent.maxY - extent.minY);
            return Math.Min(wanted, maxSide / Math.Max(1.0, longest));
        }

        public void Paint(Action<float> progress = null)
        {
            ops.Clear();
            int order = 0;
            foreach (MapObject obj in map.objects)
            {
                if (obj.symbol == null || obj.kind == SymbolKind.Text) continue;
                AddObject(obj, obj.symbol, ref order);
            }
            ops.Sort((a, b) => a.priority != b.priority ? b.priority.CompareTo(a.priority) : a.order.CompareTo(b.order));
            for (int i = 0; i < ops.Count; i++)
            {
                ops[i].draw();
                if (progress != null && (i & 1023) == 0) progress(i / (float)ops.Count);
            }
            ops.Clear();
        }

        void Add(int color, Action draw, ref int order)
        {
            if (color < 0 || color >= map.colors.Count || map.colors[color].invisible) return;
            ops.Add(new Op { priority = color, order = order++, draw = draw });
        }

        // ---- Objects ----

        void AddObject(MapObject obj, MapSymbol s, ref int order)
        {
            if (s.parts != null && s.parts.Count > 0)
            {
                foreach (MapSymbol part in s.parts)
                {
                    if (obj.kind == SymbolKind.Area && part.kind == SymbolKind.Area) AddArea(obj, part.area, ref order);
                    else if (part.line != null) AddLineObject(obj, part.line, ref order);
                }
                return;
            }
            switch (obj.kind)
            {
                case SymbolKind.Point:
                    if (s.point != null)
                    {
                        AddElements(s.point, obj.parts[0][0], obj.parts[0][1], obj.angle, ref order);
                    }
                    break;
                case SymbolKind.Line:
                    if (s.line != null) AddLineObject(obj, s.line, ref order);
                    else if (s.area != null) AddArea(obj, s.area, ref order);
                    break;
                case SymbolKind.Area:
                    if (s.area != null)
                    {
                        AddArea(obj, s.area, ref order);
                        if (s.area.border != null && s.area.border.line != null) AddLineObject(obj, s.area.border.line, ref order);
                    }
                    else if (s.line != null) AddLineObject(obj, s.line, ref order);
                    break;
            }
        }

        void AddArea(MapObject obj, AreaStyle a, ref int order)
        {
            if (a == null) return;
            List<double[]> rings = obj.parts;
            if (a.fillColor >= 0)
            {
                int c = a.fillColor;
                Add(c, () => FillPaper(rings, c), ref order);
            }
            if (a.hatchCount > 0 && a.hatchColor >= 0 && a.hatchDistance > 0)
            {
                int c = a.hatchColor;
                double w = Math.Max(a.hatchWidth, 0.05);
                double d = a.hatchDistance + a.hatchWidth;
                double a1 = a.hatchAngle1 + obj.angle, a2 = a.hatchAngle2 + obj.angle;
                bool two = a.hatchCount == 2;
                Add(c, () =>
                {
                    Hatch(rings, a1, d, w, c);
                    if (two) Hatch(rings, a2, d, w, c);
                }, ref order);
            }
            if (a.pattern != null && a.pattern.Count > 0 && a.patternWidth > 0 && a.patternHeight > 0)
            {
                // Points of the pattern that fall inside the area; each element is drawn in its own colour
                List<(double x, double y)> spots = PatternSpots(rings, a, obj.angle);
                double angle = a.patternAngle + obj.angle;
                foreach (SymbolElement e in a.pattern)
                {
                    SymbolElement el = e;
                    Add(el.color, () =>
                    {
                        foreach (var p in spots) DrawElement(el, p.x, p.y, angle);
                    }, ref order);
                }
            }
        }

        void AddLineObject(MapObject obj, LineStyle l, ref int order)
        {
            foreach (double[] part in obj.parts)
            {
                AddLine(part, l, ref order);
            }
        }

        void AddLine(double[] p, LineStyle l, ref int order)
        {
            if (p.Length < 4) return;
            if (l.fillColor >= 0 && l.doubleWidth > 0)
            {
                int c = l.fillColor;
                Add(c, () => StrokePaper(p, l.doubleWidth, c, false), ref order);
            }
            if (l.leftColor >= 0 && l.leftWidth > 0)
            {
                int c = l.leftColor;
                double off = (l.doubleWidth + l.leftWidth) / 2;
                Add(c, () => StrokeMaybeDashed(Offset(p, off), l.leftWidth, c, l.borderDash, l.borderGap), ref order);
            }
            if (l.rightColor >= 0 && l.rightWidth > 0)
            {
                int c = l.rightColor;
                double off = -(l.doubleWidth + l.rightWidth) / 2;
                Add(c, () => StrokeMaybeDashed(Offset(p, off), l.rightWidth, c, l.borderDash, l.borderGap), ref order);
            }
            if (l.color >= 0 && l.width > 0)
            {
                int c = l.color;
                if (l.mainGap > 0 && l.mainLength > 0)
                {
                    Add(c, () =>
                    {
                        foreach (double[] dash in Dashes(p, l.mainLength, l.mainGap)) StrokePaper(dash, l.width, c, l.roundCaps);
                    }, ref order);
                }
                else
                {
                    Add(c, () => StrokePaper(p, l.width, c, l.roundCaps), ref order);
                }
            }
            if (l.midElements != null && l.midElements.Count > 0)
            {
                double spacing = l.midSpacing > 0 ? l.midSpacing : (l.mainLength > 0 ? l.mainLength + l.mainGap : 0);
                if (spacing <= 0.05) spacing = 1;
                List<(double x, double y, double a)> spots = l.oomMid && !(l.mainGap > 0 && l.mainLength > 0)
                    ? AlongLineOom(p, l.midSpacing, l.midEnd, l.midCount, l.midDistance, l.midAtLeastOne, l.midMinCount)
                    : AlongLine(p, spacing, l.midCount, l.midDistance);
                foreach (SymbolElement e in l.midElements)
                {
                    SymbolElement el = e;
                    Add(el.color, () =>
                    {
                        foreach (var s in spots) DrawElement(el, s.x, s.y, s.a);
                    }, ref order);
                }
            }
        }

        void AddElements(List<SymbolElement> elements, double x, double y, double angle, ref int order)
        {
            foreach (SymbolElement e in elements)
            {
                SymbolElement el = e;
                Add(el.color, () => DrawElement(el, x, y, angle), ref order);
            }
        }

        // ---- Geometry on paper ----

        void DrawElement(SymbolElement e, double ox, double oy, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            int n = e.x.Length;
            var p = new double[n * 2];
            for (int i = 0; i < n; i++)
            {
                p[2 * i] = ox + e.x[i] * ca - e.y[i] * sa;
                p[2 * i + 1] = oy + e.x[i] * sa + e.y[i] * ca;
            }
            switch (e.type)
            {
                case SymbolElement.ElementType.Dot:
                    FillCirclePaper(p[0], p[1], e.diameter / 2, e.color);
                    break;
                case SymbolElement.ElementType.Circle:
                    StrokePaper(CirclePoints(p[0], p[1], e.diameter / 2), Math.Max(e.lineWidth, 0.05), e.color, false);
                    break;
                case SymbolElement.ElementType.Line:
                    if (n >= 2) StrokePaper(p, Math.Max(e.lineWidth, 0.05), e.color, false);
                    break;
                case SymbolElement.ElementType.Area:
                    if (n >= 3) FillPaper(new List<double[]> { p }, e.color);
                    break;
            }
        }

        static double[] CirclePoints(double cx, double cy, double r)
        {
            int n = Math.Max(12, Math.Min(64, (int)(r * 40)));
            var p = new double[(n + 1) * 2];
            for (int i = 0; i <= n; i++)
            {
                double t = 2 * Math.PI * i / n;
                p[2 * i] = cx + r * Math.Cos(t);
                p[2 * i + 1] = cy + r * Math.Sin(t);
            }
            return p;
        }

        // A polyline moved sideways (positive = left)
        static double[] Offset(double[] p, double d)
        {
            int n = p.Length / 2;
            var r = new double[p.Length];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                double nx1 = 0, ny1 = 0, nx2 = 0, ny2 = 0;
                if (i > 0) Normal(p, i - 1, i, out nx1, out ny1);
                if (i < n - 1) Normal(p, i, i + 1, out nx2, out ny2);
                double nx = nx1 + nx2, ny = ny1 + ny2;
                double len = Math.Sqrt(nx * nx + ny * ny);
                if (len < 1e-9) { nx = nx1 + nx2 == 0 ? nx2 : nx; ny = ny2; len = 1; }
                nx /= len; ny /= len;
                // Keep the offset distance at sharp corners, but not endlessly
                double cos = (i > 0 && i < n - 1) ? Math.Max(0.3, nx * nx2 + ny * ny2) : 1;
                r[2 * i] = p[2 * i] + nx * d / cos;
                r[2 * i + 1] = p[2 * i + 1] + ny * d / cos;
            }
            return r;
        }

        static void Normal(double[] p, int i, int j, out double nx, out double ny)
        {
            double dx = p[2 * j] - p[2 * i], dy = p[2 * j + 1] - p[2 * i + 1];
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-12) { nx = ny = 0; return; }
            nx = -dy / len; ny = dx / len;
        }

        static List<double[]> Dashes(double[] p, double dash, double gap)
        {
            var result = new List<double[]>();
            var cur = new List<double>();
            bool on = true;
            double left = dash;
            int n = p.Length / 2;
            cur.Add(p[0]); cur.Add(p[1]);
            for (int i = 0; i < n - 1; i++)
            {
                double x0 = p[2 * i], y0 = p[2 * i + 1], x1 = p[2 * i + 2], y1 = p[2 * i + 3];
                double len = MapGeometry.Dist(x0, y0, x1, y1);
                double pos = 0;
                while (len - pos > left)
                {
                    pos += left;
                    double t = pos / len;
                    double x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t;
                    if (on)
                    {
                        cur.Add(x); cur.Add(y);
                        result.Add(cur.ToArray());
                        cur.Clear();
                    }
                    else
                    {
                        cur.Add(x); cur.Add(y);
                    }
                    on = !on;
                    left = on ? dash : gap;
                }
                left -= len - pos;
                if (on) { cur.Add(x1); cur.Add(y1); }
            }
            if (on && cur.Count >= 4) result.Add(cur.ToArray());
            return result;
        }

        // Places symbols along a line: groups of 'count' symbols 'distance' apart, every 'spacing' mm
        void StrokeMaybeDashed(double[] p, double width, int color, double dash, double gap)
        {
            if (dash > 0 && gap > 0)
            {
                foreach (double[] d in Dashes(p, dash, gap)) StrokePaper(d, width, color, false);
            }
            else StrokePaper(p, width, color, false);
        }

        // Symbols along a solid line the way OpenOrienteering Mapper puts them: the first and last group endLength
        // from the ends and the rest evenly between, as near segment apart as fits. No room: none, or one group in
        // the middle when atLeastOne is set.
        static List<(double, double, double)> AlongLineOom(double[] p, double segment, double endLength, int count,
                                                            double distance, bool atLeastOne, int minCount)
        {
            int n = p.Length / 2;
            double total = 0;
            for (int i = 0; i < n - 1; i++) total += MapGeometry.Dist(p[2 * i], p[2 * i + 1], p[2 * i + 2], p[2 * i + 3]);
            var centres = new List<double>();
            double groupWidth = (count - 1) * distance;
            double room = total - 2 * endLength - groupWidth;
            if (total <= 0) return new List<(double, double, double)>();
            if (segment <= 0 || room < 0)
            {
                if (atLeastOne || (segment > 0 && minCount > 0)) centres.Add(total / 2);
            }
            else
            {
                int gaps = Math.Max(Math.Max(0, minCount - 1), (int)Math.Round(room / segment));
                if (gaps == 0) centres.Add(total / 2);
                else for (int g = 0; g <= gaps; g++) centres.Add(endLength + groupWidth / 2 + room * g / gaps);
            }
            var targets = new List<double>();
            foreach (double c in centres)
                for (int k = 0; k < count; k++) targets.Add(c - groupWidth / 2 + k * distance);
            return SpotsAt(p, targets, total);
        }

        static List<(double, double, double)> AlongLine(double[] p, double spacing, int count, double distance)
        {
            var spots = new List<(double, double, double)>();
            int n = p.Length / 2;
            double total = 0;
            for (int i = 0; i < n - 1; i++) total += MapGeometry.Dist(p[2 * i], p[2 * i + 1], p[2 * i + 2], p[2 * i + 3]);
            if (total <= 0) return spots;
            int groups = Math.Max(1, (int)Math.Round(total / spacing));
            double step = total / groups;
            double groupWidth = (count - 1) * distance;
            var targets = new List<double>();
            for (int g = 0; g < groups; g++)
            {
                double centre = (g + 0.5) * step;
                for (int k = 0; k < count; k++) targets.Add(centre - groupWidth / 2 + k * distance);
            }
            return SpotsAt(p, targets, total);
        }

        // Points (and the line's direction) at these distances along a polyline
        static List<(double, double, double)> SpotsAt(double[] p, List<double> targets, double total)
        {
            var spots = new List<(double, double, double)>();
            int n = p.Length / 2;
            int seg = 0;
            double segStart = 0;
            foreach (double t in targets)
            {
                if (t < 0 || t > total) continue;
                while (seg < n - 2)
                {
                    double l = MapGeometry.Dist(p[2 * seg], p[2 * seg + 1], p[2 * seg + 2], p[2 * seg + 3]);
                    if (segStart + l >= t) break;
                    segStart += l;
                    seg++;
                }
                double x0 = p[2 * seg], y0 = p[2 * seg + 1], x1 = p[2 * seg + 2], y1 = p[2 * seg + 3];
                double len = MapGeometry.Dist(x0, y0, x1, y1);
                double u = len > 0 ? (t - segStart) / len : 0;
                spots.Add((x0 + (x1 - x0) * u, y0 + (y1 - y0) * u, Math.Atan2(y1 - y0, x1 - x0) * 180 / Math.PI));
            }
            return spots;
        }

        static List<(double x, double y)> PatternSpots(List<double[]> rings, AreaStyle a, double objAngle)
        {
            var spots = new List<(double, double)>();
            double ang = (a.patternAngle + objAngle) * Math.PI / 180, ca = Math.Cos(ang), sa = Math.Sin(ang);
            Bounds(rings, out double x0, out double y0, out double x1, out double y1);
            // Corners of the box in the pattern's own rotated frame
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var c in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
            {
                double u = c.Item1 * ca + c.Item2 * sa, v = -c.Item1 * sa + c.Item2 * ca;
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            double w = a.patternWidth, h = a.patternHeight;
            long count = (long)((u1 - u0) / w + 2) * (long)((v1 - v0) / h + 2);
            if (count > 2000000) return spots;   // something odd; skip rather than hang
            int row0 = (int)Math.Floor(v0 / h);
            for (int row = row0; row * h <= v1 + h; row++)
            {
                double shift = a.patternShifted && (row & 1) != 0 ? w / 2 : 0;
                for (int col = (int)Math.Floor(u0 / w) - 1; col * w <= u1 + w; col++)
                {
                    double u = col * w + shift, v = row * h;
                    double x = u * ca - v * sa, y = u * sa + v * ca;
                    if (x < x0 || x > x1 || y < y0 || y > y1) continue;
                    if (Inside(rings, x, y)) spots.Add((x, y));
                }
            }
            return spots;
        }

        public static bool Inside(List<double[]> rings, double x, double y)
        {
            bool inside = false;
            foreach (double[] r in rings)
            {
                int n = r.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double yi = r[2 * i + 1], yj = r[2 * j + 1];
                    if ((yi > y) != (yj > y))
                    {
                        double xi = r[2 * i], xj = r[2 * j];
                        if (x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                    }
                }
            }
            return inside;
        }

        static void Bounds(List<double[]> rings, out double x0, out double y0, out double x1, out double y1)
        {
            x0 = y0 = double.MaxValue; x1 = y1 = double.MinValue;
            foreach (double[] r in rings)
            {
                for (int i = 0; i < r.Length; i += 2)
                {
                    x0 = Math.Min(x0, r[i]); x1 = Math.Max(x1, r[i]);
                    y0 = Math.Min(y0, r[i + 1]); y1 = Math.Max(y1, r[i + 1]);
                }
            }
        }

        // Parallel lines across the area, cut to the inside
        void Hatch(List<double[]> rings, double angleDeg, double spacing, double lineWidth, int color)
        {
            double a = angleDeg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            Bounds(rings, out double x0, out double y0, out double x1, out double y1);
            double v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var c in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
            {
                double v = -c.Item1 * sa + c.Item2 * ca;
                v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            if ((v1 - v0) / spacing > 100000) return;
            var hits = new List<double>();
            for (double v = Math.Floor(v0 / spacing) * spacing; v <= v1; v += spacing)
            {
                hits.Clear();
                // Intersections of the line {u*(ca,sa) + v*(-sa,ca)} with every edge, as u values
                foreach (double[] r in rings)
                {
                    int n = r.Length / 2;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        double vi = -r[2 * i] * sa + r[2 * i + 1] * ca;
                        double vj = -r[2 * j] * sa + r[2 * j + 1] * ca;
                        if ((vi > v) != (vj > v))
                        {
                            double ui = r[2 * i] * ca + r[2 * i + 1] * sa;
                            double uj = r[2 * j] * ca + r[2 * j + 1] * sa;
                            hits.Add(ui + (uj - ui) * (v - vi) / (vj - vi));
                        }
                    }
                }
                hits.Sort();
                for (int k = 0; k + 1 < hits.Count; k += 2)
                {
                    double ua = hits[k], ub = hits[k + 1];
                    var seg = new[] { ua * ca - v * sa, ua * sa + v * ca, ub * ca - v * sa, ub * sa + v * ca };
                    StrokePaper(seg, lineWidth, color, false);
                }
            }
        }

        // ---- Paper to pixels ----

        double PX(double x) => (x - minX) * ppm;
        double PY(double y) => (maxY - y) * ppm;

        void FillPaper(List<double[]> rings, int color)
        {
            var px = new List<double[]>(rings.Count);
            foreach (double[] r in rings)
            {
                var q = new double[r.Length];
                for (int i = 0; i < r.Length; i += 2) { q[i] = PX(r[i]); q[i + 1] = PY(r[i + 1]); }
                px.Add(q);
            }
            FillPixels(px, color);
        }

        void FillCirclePaper(double x, double y, double r, int color)
        {
            double rp = r * ppm;
            if (rp < 0.35)
            {
                // Smaller than a pixel: a faint dot keeps the overall tone
                BlendPixel((int)PX(x), (int)PY(y), color, (float)Math.Min(1, Math.PI * rp * rp));
                return;
            }
            FillPaper(new List<double[]> { CirclePoints(x, y, r) }, color);
        }

        void StrokePaper(double[] p, double w, int color, bool round)
        {
            int n = p.Length / 2;
            if (n < 2) return;
            double hw = Math.Max(w * ppm, 0.6) / 2 / ppm;   // at least a thin visible line
            var polys = new List<double[]>();
            for (int i = 0; i < n - 1; i++)
            {
                double x0 = p[2 * i], y0 = p[2 * i + 1], x1 = p[2 * i + 2], y1 = p[2 * i + 3];
                double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double nx = -dy / len * hw, ny = dx / len * hw;
                FillPaper(new List<double[]> { new[] { x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny } }, color);
                // Joins: a round dot at every inner corner closes the gaps
                if ((i > 0 || round) && hw * ppm > 0.8) FillCircleNoMin(x0, y0, hw, color);
            }
            if (round && hw * ppm > 0.8) FillCircleNoMin(p[2 * n - 2], p[2 * n - 1], hw, color);
        }

        void FillCircleNoMin(double x, double y, double r, int color)
        {
            FillPaper(new List<double[]> { CirclePoints(x, y, r) }, color);
        }

        // ---- Rasteriser: even-odd polygon fill with 4 sub-scanlines and exact horizontal coverage ----

        const int Sub = 4;
        float[] coverage = new float[0];
        readonly List<double> xs = new List<double>();

        void FillPixels(List<double[]> rings, int color)
        {
            double minYp = double.MaxValue, maxYp = double.MinValue, minXp = double.MaxValue, maxXp = double.MinValue;
            foreach (double[] r in rings)
            {
                for (int i = 0; i < r.Length; i += 2)
                {
                    minXp = Math.Min(minXp, r[i]); maxXp = Math.Max(maxXp, r[i]);
                    minYp = Math.Min(minYp, r[i + 1]); maxYp = Math.Max(maxYp, r[i + 1]);
                }
            }
            if (maxXp < 0 || maxYp < 0 || minXp >= width || minYp >= height) return;
            int x0 = Math.Max(0, (int)Math.Floor(minXp)), x1 = Math.Min(width - 1, (int)Math.Floor(maxXp));
            int y0 = Math.Max(0, (int)Math.Floor(minYp)), y1 = Math.Min(height - 1, (int)Math.Floor(maxYp));
            int span = x1 - x0 + 2;
            if (coverage.Length < span) coverage = new float[Math.Max(span, coverage.Length * 2)];
            for (int py = y0; py <= y1; py++)
            {
                Array.Clear(coverage, 0, span);
                bool any = false;
                for (int s = 0; s < Sub; s++)
                {
                    double sy = py + (s + 0.5) / Sub;
                    xs.Clear();
                    foreach (double[] r in rings)
                    {
                        int n = r.Length / 2;
                        for (int i = 0, j = n - 1; i < n; j = i++)
                        {
                            double yi = r[2 * i + 1], yj = r[2 * j + 1];
                            if ((yi > sy) != (yj > sy))
                            {
                                xs.Add(r[2 * i] + (r[2 * j] - r[2 * i]) * (sy - yi) / (yj - yi));
                            }
                        }
                    }
                    if (xs.Count < 2) continue;
                    xs.Sort();
                    for (int k = 0; k + 1 < xs.Count; k += 2)
                    {
                        double a = Math.Max(xs[k], x0), b = Math.Min(xs[k + 1], x1 + 1);
                        if (b <= a) continue;
                        any = true;
                        int ia = (int)Math.Floor(a), ib = (int)Math.Floor(b);
                        const float w = 1f / Sub;
                        if (ia == ib)
                        {
                            coverage[ia - x0] += (float)(b - a) * w;
                            continue;
                        }
                        coverage[ia - x0] += (float)(ia + 1 - a) * w;
                        for (int x = ia + 1; x < ib; x++) coverage[x - x0] += w;
                        if (ib <= x1) coverage[ib - x0] += (float)(b - ib) * w;
                    }
                }
                if (!any) continue;
                MapColor c = map.colors[color];
                int row = py * width;
                for (int x = x0; x <= x1; x++)
                {
                    float a = coverage[x - x0];
                    if (a <= 0.001f) continue;
                    if (a > 1) a = 1;
                    Put(row + x, c, a);
                }
            }
        }

        void BlendPixel(int x, int y, int color, float a)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            Put(y * width + x, map.colors[color], a);
        }
    }
}
