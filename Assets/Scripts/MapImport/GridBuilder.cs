using System;
using System.Collections.Generic;

// Turns a map file into the walkability grid the runners use: one speed per cell (0 = can't be entered) and a
// ground height per cell for uphill and downhill.
//
// Order of work, each step on top of the one before:
//   1. areas (forest, open land, marsh, lakes, buildings ...), the colour drawn on top wins, like on the paper
//   2. walls and fences that take a moment to cross
//   3. paths and roads: every cell a path line passes through gets the path's speed, so a path has no slow gaps
//   4. impassable lines (cliffs, high fences, impassable walls): no way through
//   5. bridges and crossing points open the way again
namespace MapImport
{
    public class MapGrid2
    {
        public int rows, cols;
        public float cellSize;          // metres
        public double minE, minN;       // ground coordinates (metres) of the grid's lower left corner
        public float[] speed;           // rows * cols, row 0 at the south
        public float[] height;          // metres, relative
        public bool hasHeights;

        public float Speed(int row, int col) => speed[row * cols + col];
    }

    public static class GridBuilder
    {
        public static MapGrid2 Build(MapFile map, MapExtent extent, bool sprint, float cellSize, Action<string> log = null)
        {
            double mpm = map.MetersPerMm;
            var g = new MapGrid2
            {
                cellSize = cellSize,
                minE = extent.minX * mpm,
                minN = extent.minY * mpm
            };
            g.cols = Math.Max(2, (int)Math.Ceiling(extent.Width * mpm / cellSize));
            g.rows = Math.Max(2, (int)Math.Ceiling(extent.Height * mpm / cellSize));
            int n = g.rows * g.cols;
            g.speed = new float[n];
            var hard = new bool[n];           // buildings, out of bounds, impassable lines: paths don't open them
            var path = new bool[n];
            var building = new bool[n];
            for (int i = 0; i < n; i++) g.speed[i] = 1f;

            // Paper mm -> cell coordinates
            double scale = mpm / cellSize;
            double ox = extent.minX, oy = extent.minY;

            // 1. Areas, bottom colour first
            var areas = new List<(int prio, int order, MapObject obj, TerrainRule rule)>();
            int order = 0;
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Area) continue;
                TerrainRule rule = SymbolRules.For(o.symbol, sprint);
                if (rule.role != TerrainRole.Ground && rule.role != TerrainRole.Blocked) continue;
                areas.Add((TopColor(o.symbol), order++, o, rule));
            }
            areas.Sort((a, b) => a.prio != b.prio ? b.prio.CompareTo(a.prio) : a.order.CompareTo(b.order));
            foreach (var a in areas)
            {
                bool isHard = a.rule.role == TerrainRole.Blocked && (a.obj.symbol.major == 520 || a.obj.symbol.major == 521 || a.obj.symbol.major >= 700);
                bool isBuilding = a.obj.symbol.major == 521;
                FillArea(g, a.obj.parts, ox, oy, scale, (idx) =>
                {
                    g.speed[idx] = a.rule.speed;
                    hard[idx] = isHard;
                    building[idx] = isBuilding;
                });
            }

            // 2. Walls and fences
            ForLines(map, sprint, TerrainRole.SlowLine, g, ox, oy, scale, (idx, rule) =>
            {
                if (g.speed[idx] > 0f) g.speed[idx] = Math.Min(g.speed[idx], Math.Max(0.3f, g.speed[idx] * rule.speed));
            });

            // 3. Paths
            // A path drawn over an out-of-bounds area is a way through it (sprint maps draw streets as wide lines
            // on top of the olive green), so only buildings and impassable lines keep a path out.
            ForLines(map, sprint, TerrainRole.Path, g, ox, oy, scale, (idx, rule) =>
            {
                if (building[idx]) return;
                g.speed[idx] = Math.Max(g.speed[idx], rule.speed);
                hard[idx] = false;
                path[idx] = true;
            });

            // 4. Impassable lines. On forest maps a path through the line wins (the gate is rarely drawn);
            //    on sprint maps the cells are small enough that a path beside a wall is its own cell.
            ForLines(map, sprint, TerrainRole.BlockingLine, g, ox, oy, scale, (idx, rule) =>
            {
                if (!sprint && path[idx]) return;
                g.speed[idx] = 0f;
                hard[idx] = true;
            });

            // 5. Bridges and crossing points
            ForLines(map, sprint, TerrainRole.Bridge, g, ox, oy, scale, (idx, rule) =>
            {
                g.speed[idx] = Math.Max(g.speed[idx], rule.speed);
                hard[idx] = false;
            });
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Point) continue;
                TerrainRule rule = SymbolRules.For(o.symbol, sprint);
                if (rule.role != TerrainRole.Crossing) continue;
                int c = (int)Math.Floor((o.parts[0][0] - ox) * scale);
                int r = (int)Math.Floor((o.parts[0][1] - oy) * scale);
                int reach = sprint ? 1 : 0;
                for (int dr = -reach; dr <= reach; dr++)
                    for (int dc = -reach; dc <= reach; dc++)
                    {
                        int rr = r + dr, cc = c + dc;
                        if (rr < 0 || cc < 0 || rr >= g.rows || cc >= g.cols) continue;
                        int idx = rr * g.cols + cc;
                        g.speed[idx] = Math.Max(g.speed[idx], rule.speed);
                    }
            }

            // Heights from the contour lines
            var sw = System.Diagnostics.Stopwatch.StartNew();
            g.height = ContourHeights.Build(map, extent, g.rows, g.cols, cellSize, out g.hasHeights, log);
            log?.Invoke($"Heights from contours: {(g.hasHeights ? "done" : "no contours found")} in {sw.ElapsedMilliseconds} ms");
            return g;
        }

        // The colour of a symbol that is highest up in the drawing order (smallest index)
        public static int TopColor(MapSymbol s)
        {
            int best = int.MaxValue;
            void Take(int c) { if (c >= 0 && c < best) best = c; }
            void FromSymbol(MapSymbol x)
            {
                if (x == null) return;
                if (x.area != null)
                {
                    Take(x.area.fillColor);
                    Take(x.area.hatchColor);
                    if (x.area.pattern != null) foreach (SymbolElement e in x.area.pattern) Take(e.color);
                }
                if (x.parts != null) foreach (MapSymbol p in x.parts) if (p.kind == SymbolKind.Area) FromSymbol(p);
            }
            FromSymbol(s);
            if (best == int.MaxValue) best = s.mainColor >= 0 ? s.mainColor : 1000;
            return best;
        }

        static void ForLines(MapFile map, bool sprint, TerrainRole role, MapGrid2 g, double ox, double oy, double scale,
                             Action<int, TerrainRule> visit)
        {
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Line && o.kind != SymbolKind.Area) continue;
                TerrainRule rule = SymbolRules.For(o.symbol, sprint);
                if (o.kind == SymbolKind.Area)
                {
                    // An area's outline can be a blocking line (lake shore drawn as part of a combined symbol)
                    continue;
                }
                if (rule.role != role) continue;
                // Lines wider than a cell (streets on sprint maps) cover several cells across
                double halfCells = role == TerrainRole.Path ? LineWidthMm(o.symbol) * scale / 2 : 0;
                int offsets = (int)Math.Floor(halfCells / 0.5);
                foreach (double[] p in o.parts)
                {
                    for (int i = 0; i + 3 < p.Length; i += 2)
                    {
                        double x0 = (p[i] - ox) * scale, y0 = (p[i + 1] - oy) * scale;
                        double x1 = (p[i + 2] - ox) * scale, y1 = (p[i + 3] - oy) * scale;
                        Supercover(x0, y0, x1, y1, g.cols, g.rows, (c, r) => visit(r * g.cols + c, rule));
                        if (offsets == 0) continue;
                        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                        if (len < 1e-9) continue;
                        double nx = -(y1 - y0) / len, ny = (x1 - x0) / len;
                        for (int k = 1; k <= offsets; k++)
                        {
                            double d = Math.Min(k * 0.5, halfCells);
                            for (int side = -1; side <= 1; side += 2)
                            {
                                Supercover(x0 + nx * d * side, y0 + ny * d * side, x1 + nx * d * side, y1 + ny * d * side,
                                    g.cols, g.rows, (c, r) => visit(r * g.cols + c, rule));
                            }
                        }
                    }
                }
            }
        }

        // How wide a line symbol is drawn on paper, mm
        static double LineWidthMm(MapSymbol s)
        {
            LineStyle l = s.line;
            if (l == null && s.parts != null) foreach (MapSymbol p in s.parts) if (p.line != null) { l = p.line; break; }
            if (l == null) return 0;
            double w = l.width;
            if (l.doubleWidth > 0) w = Math.Max(w, l.doubleWidth + l.leftWidth + l.rightWidth);
            return w;
        }

        // Every cell a straight line passes through, in order (Amanatides & Woo)
        public static void Supercover(double x0, double y0, double x1, double y1, int cols, int rows, Action<int, int> visit)
        {
            int cx = (int)Math.Floor(x0), cy = (int)Math.Floor(y0);
            int ex = (int)Math.Floor(x1), ey = (int)Math.Floor(y1);
            double dx = x1 - x0, dy = y1 - y0;
            int stepX = dx > 0 ? 1 : -1, stepY = dy > 0 ? 1 : -1;
            double tMaxX = dx != 0 ? ((stepX > 0 ? cx + 1 - x0 : x0 - cx) / Math.Abs(dx)) : double.MaxValue;
            double tMaxY = dy != 0 ? ((stepY > 0 ? cy + 1 - y0 : y0 - cy) / Math.Abs(dy)) : double.MaxValue;
            double tDeltaX = dx != 0 ? 1 / Math.Abs(dx) : double.MaxValue;
            double tDeltaY = dy != 0 ? 1 / Math.Abs(dy) : double.MaxValue;
            int guard = Math.Abs(ex - cx) + Math.Abs(ey - cy) + 2;
            for (int k = 0; k < guard; k++)
            {
                if (cx >= 0 && cy >= 0 && cx < cols && cy < rows) visit(cx, cy);
                if (cx == ex && cy == ey) break;
                if (tMaxX < tMaxY) { tMaxX += tDeltaX; cx += stepX; }
                else { tMaxY += tDeltaY; cy += stepY; }
            }
        }

        // Calls fill(index) for every cell whose centre is inside the area (even-odd over all rings)
        public static void FillArea(MapGrid2 g, List<double[]> rings, double ox, double oy, double scale, Action<int> fill)
        {
            FillCells(rings, ox, oy, scale, g.cols, g.rows, (r, c) => fill(r * g.cols + c));
        }

        public static void FillCells(List<double[]> rings, double ox, double oy, double scale, int cols, int rows, Action<int, int> fill)
        {
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (double[] p in rings)
                for (int i = 1; i < p.Length; i += 2) { minY = Math.Min(minY, p[i]); maxY = Math.Max(maxY, p[i]); }
            int r0 = Math.Max(0, (int)Math.Floor((minY - oy) * scale - 0.5));
            int r1 = Math.Min(rows - 1, (int)Math.Ceiling((maxY - oy) * scale));
            var xs = new List<double>();
            for (int r = r0; r <= r1; r++)
            {
                double y = oy + (r + 0.5) / scale;   // cell centre on paper
                xs.Clear();
                foreach (double[] p in rings)
                {
                    int n = p.Length / 2;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        double yi = p[2 * i + 1], yj = p[2 * j + 1];
                        if ((yi > y) != (yj > y))
                        {
                            xs.Add(p[2 * i] + (p[2 * j] - p[2 * i]) * (y - yi) / (yj - yi));
                        }
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    // Cells whose centre (c + 0.5) lies between the two crossings
                    int c0 = Math.Max(0, (int)Math.Ceiling((xs[k] - ox) * scale - 0.5));
                    int c1 = Math.Min(cols - 1, (int)Math.Floor((xs[k + 1] - ox) * scale - 0.5));
                    for (int c = c0; c <= c1; c++) fill(r, c);
                }
            }
        }
    }
}
