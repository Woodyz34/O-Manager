using System;
using System.Collections.Generic;

namespace MapImport
{
    // The part of the paper the terrain is drawn on, in mm. Legends, frames and course overprint (symbols 600 and up)
    // are left out, and a few stray objects far from the rest do not stretch it.
    public class MapExtent
    {
        public double minX, minY, maxX, maxY;

        public double Width => maxX - minX;
        public double Height => maxY - minY;

        public static bool IsTerrain(MapSymbol s)
        {
            return s != null && s.major >= 100 && s.major < 600 && s.kind != SymbolKind.Text;
        }

        // Everything on the paper, as an export of the whole map shows it (line widths and symbols reach a little further)
        public static MapExtent Full(MapFile map)
        {
            var e = new MapExtent { minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue };
            foreach (MapObject o in map.objects)
            {
                foreach (double[] p in o.parts)
                {
                    for (int i = 0; i + 1 < p.Length; i += 2)
                    {
                        e.minX = Math.Min(e.minX, p[i]); e.maxX = Math.Max(e.maxX, p[i]);
                        e.minY = Math.Min(e.minY, p[i + 1]); e.maxY = Math.Max(e.maxY, p[i + 1]);
                    }
                }
            }
            if (e.minX > e.maxX) return new MapExtent { maxX = 1, maxY = 1 };
            return e;
        }

        public static MapExtent Of(MapFile map)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (MapObject o in map.objects)
            {
                if (!IsTerrain(o.symbol)) continue;
                foreach (double[] p in o.parts)
                {
                    for (int i = 0; i < p.Length; i += 2) { xs.Add(p[i]); ys.Add(p[i + 1]); }
                }
            }
            if (xs.Count == 0)
            {
                foreach (MapObject o in map.objects)
                    foreach (double[] p in o.parts)
                        for (int i = 0; i < p.Length; i += 2) { xs.Add(p[i]); ys.Add(p[i + 1]); }
            }
            if (xs.Count == 0) return new MapExtent { maxX = 1, maxY = 1 };
            xs.Sort();
            ys.Sort();
            // Trim the outermost 0.05 % of the points, then go out to the furthest point within 2 cm of that
            int k = (int)(xs.Count * 0.0005);
            double x0 = xs[k], x1 = xs[xs.Count - 1 - k], y0 = ys[k], y1 = ys[ys.Count - 1 - k];
            double reach = 20;
            var e = new MapExtent { minX = x0, maxX = x1, minY = y0, maxY = y1 };
            foreach (double x in xs) { if (x >= x0 - reach) { e.minX = Math.Min(e.minX, x); break; } }
            for (int i = xs.Count - 1; i >= 0; i--) { if (xs[i] <= x1 + reach) { e.maxX = Math.Max(e.maxX, xs[i]); break; } }
            foreach (double y in ys) { if (y >= y0 - reach) { e.minY = Math.Min(e.minY, y); break; } }
            for (int i = ys.Count - 1; i >= 0; i--) { if (ys[i] <= y1 + reach) { e.maxY = Math.Max(e.maxY, ys[i]); break; } }
            // A little white margin round the map
            double margin = 2;
            e.minX -= margin; e.minY -= margin; e.maxX += margin; e.maxY += margin;
            return e;
        }
    }
}
