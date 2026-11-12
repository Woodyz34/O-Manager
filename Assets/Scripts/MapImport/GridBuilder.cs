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
//   6. everything outside the map is closed: beyond the edge of what is drawn, and under white "mask" areas that
//      mappers put over the parts that are not part of the map
namespace MapImport
{
    public class MapGrid2
    {
        public int rows, cols;
        public float cellSize;          // metres
        public double minE, minN;       // ground coordinates (metres) of the grid's lower left corner
        public float[] speed;           // rows * cols, row 0 at the south
        public float[] height;          // metres, relative
        public byte[] kind;             // rows * cols, what the ground is (GroundKind): marsh, open land, green ...
        public bool hasHeights;

        public float Speed(int row, int col) => speed[row * cols + col];
    }

    public static class GridBuilder
    {
        // The most cells a map's grid may have (the sprint pass uses cells half the size, so four times as many)
        const double MaxCells = 400e6;

        public static MapGrid2 Build(MapFile map, MapExtent extent, bool sprint, float cellSize, Action<string> log = null)
        {
            MapGrid2 g = BuildSpeed(map, extent, sprint, cellSize, log);

            // 7. Narrow passages: on sprint maps a gap between two walls, or a strip between a building and a fence,
            //    is often narrower than a cell, so every cell across it touches a wall and the way is shut. A grid
            //    with cells half the size sees the gap; where it does, the cells of this grid are opened along it.
            if (sprint)
            {
                var passSw = System.Diagnostics.Stopwatch.StartNew();
                MapGrid2 fine = BuildSpeed(map, extent, sprint, cellSize / 2f, null);
                int opened = OpenNarrowPassages(g, fine);
                log?.Invoke($"Narrow passages: {opened} cells opened in {passSw.ElapsedMilliseconds} ms");
            }

            // Heights from the contour lines
            var sw = System.Diagnostics.Stopwatch.StartNew();
            g.height = ContourHeights.Build(map, extent, g.rows, g.cols, cellSize, out g.hasHeights, log);
            log?.Invoke($"Heights from contours: {(g.hasHeights ? "done" : "no contours found")} in {sw.ElapsedMilliseconds} ms");
            return g;
        }

        // Steps 1 to 6: the speed of every cell
        public static MapGrid2 BuildSpeed(MapFile map, MapExtent extent, bool sprint, float cellSize, Action<string> log)
        {
            double mpm = map.MetersPerMm;
            var g = new MapGrid2
            {
                cellSize = cellSize,
                minE = extent.minX * mpm,
                minN = extent.minY * mpm
            };
            double colsWanted = Math.Ceiling(extent.Width * mpm / cellSize);
            double rowsWanted = Math.Ceiling(extent.Height * mpm / cellSize);
            // A map file with something drawn far away from the rest (or a damaged file) would ask for a grid of
            // billions of cells: say so instead of running out of memory
            if (double.IsNaN(colsWanted) || double.IsNaN(rowsWanted) || colsWanted * rowsWanted > MaxCells)
            {
                throw new System.IO.InvalidDataException("The map is too big for the game (" +
                    Math.Round(extent.Width * mpm / 1000.0, 1) + " x " + Math.Round(extent.Height * mpm / 1000.0, 1) +
                    " km). Is something drawn far outside the map? Set a map edge around the part to use.");
            }
            g.cols = Math.Max(2, (int)colsWanted);
            g.rows = Math.Max(2, (int)rowsWanted);
            int n = g.rows * g.cols;
            g.speed = new float[n];
            g.kind = new byte[n];
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
                // Areas that only tell what the ground looks like (broken ground, a boulder field) are kept for the
                // runners' map reading even when they do not change the speed
                if (rule.role != TerrainRole.Ground && rule.role != TerrainRole.Blocked && GroundKind.Of(o.symbol) == GroundKind.Forest) continue;
                areas.Add((TopColor(o.symbol), order++, o, rule));
            }
            areas.Sort((a, b) => a.prio != b.prio ? b.prio.CompareTo(a.prio) : a.order.CompareTo(b.order));
            foreach (var a in areas)
            {
                bool isBuilding = a.rule.role == TerrainRole.Blocked && SymbolRules.IsBuilding(a.obj.symbol);
                bool isHard = a.rule.role == TerrainRole.Blocked && (isBuilding || SymbolRules.IsOutOfBounds(a.obj.symbol));
                bool setsSpeed = a.rule.role == TerrainRole.Ground || a.rule.role == TerrainRole.Blocked;
                byte kind = GroundKind.Of(a.obj.symbol);
                FillArea(g, a.obj.parts, ox, oy, scale, (idx) =>
                {
                    // Plain forest (white) drawn over something else takes its place; a pattern without a meaning does not
                    if (setsSpeed || kind != GroundKind.Forest) g.kind[idx] = kind;
                    if (!setsSpeed) return;
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

            // 6. Outside the map
            int closed = MarkOutside(map, g, ox, oy, scale, sprint);
            // The parts of the map the player left out (not finished)
            if (map.boundary != null && map.boundary.Length >= 6)
            {
                for (int r = 0; r < g.rows; r++)
                {
                    double y = oy + (r + 0.5) / scale;
                    for (int c = 0; c < g.cols; c++)
                    {
                        int idx = r * g.cols + c;
                        if (g.speed[idx] > 0f && !MapBoundary.Inside(map.boundary, ox + (c + 0.5) / scale, y))
                        {
                            g.speed[idx] = 0f;
                            closed++;
                        }
                    }
                }
            }
            log?.Invoke($"Outside the map: {closed} of {n} cells closed");
            return g;
        }

        // Opens closed cells of g where the grid with half-size cells (fine, same corner) has a way through.
        // A closed cell is opened only when, inside the 3 x 3 cells around it, the open fine cells of it and of
        // every open neighbour are all joined up in the fine grid. So the opened cell joins only ground that really
        // is joined there, and a fence or wall through the middle of a cell (open fine cells on both sides, but not
        // joined) stays shut. Opening one cell can make its neighbour openable too, so it repeats until nothing changes.
        public static int OpenNarrowPassages(MapGrid2 g, MapGrid2 fine)
        {
            int rows = g.rows, cols = g.cols, fr = fine.rows, fc = fine.cols;
            bool FineOpen(int r, int c) => r >= 0 && c >= 0 && r < fr && c < fc && fine.speed[r * fc + c] > 0f;

            // Closed cells with at least one open fine cell are the ones that can be opened
            var candidates = new List<int>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (g.speed[r * cols + c] > 0f) continue;
                    if (FineOpen(2 * r, 2 * c) || FineOpen(2 * r + 1, 2 * c) || FineOpen(2 * r, 2 * c + 1) || FineOpen(2 * r + 1, 2 * c + 1))
                        candidates.Add(r * cols + c);
                }

            // Window: 6 x 6 fine cells = the 3 x 3 cells around the candidate
            var label = new int[36];
            var stack = new int[36];
            int opened = 0;
            for (int pass = 0; pass < 6; pass++)
            {
                int openedThisPass = 0;
                foreach (int idx in candidates)
                {
                    if (g.speed[idx] > 0f) continue;
                    int r = idx / cols, c = idx % cols;
                    int fr0 = 2 * r - 2, fc0 = 2 * c - 2;
                    // Fine cells that take part: open ones inside the candidate or inside an open neighbour
                    for (int k = 0; k < 36; k++)
                    {
                        int wr = k / 6, wc = k % 6;
                        int cr = r - 1 + wr / 2, cc = c - 1 + wc / 2;
                        bool inCell = cr == r && cc == c;
                        bool inOpenNeighbour = !inCell && cr >= 0 && cc >= 0 && cr < rows && cc < cols && g.speed[cr * cols + cc] > 0f;
                        label[k] = (inCell || inOpenNeighbour) && FineOpen(fr0 + wr, fc0 + wc) ? 0 : -1;
                    }
                    // Every open neighbour must have open fine cells of its own, or the step into it is not seen here
                    bool ok = true;
                    int openNeighbours = 0;
                    for (int dr = -1; dr <= 1 && ok; dr++)
                        for (int dc = -1; dc <= 1 && ok; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int cr = r + dr, cc = c + dc;
                            if (cr < 0 || cc < 0 || cr >= rows || cc >= cols || g.speed[cr * cols + cc] <= 0f) continue;
                            openNeighbours++;
                            bool any = false;
                            for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
                                    if (label[(2 * (dr + 1) + i) * 6 + 2 * (dc + 1) + j] == 0) any = true;
                            if (!any) ok = false;
                        }
                    if (!ok || openNeighbours == 0) continue;
                    // Flood the fine cells (8 directions, no cutting between two closed cells)
                    int components = 0;
                    for (int k = 0; k < 36 && components <= 1; k++)
                    {
                        if (label[k] != 0) continue;
                        components++;
                        int top = 0;
                        stack[top++] = k;
                        label[k] = components;
                        while (top > 0)
                        {
                            int q = stack[--top];
                            int qr = q / 6, qc = q % 6;
                            for (int dr = -1; dr <= 1; dr++)
                                for (int dc = -1; dc <= 1; dc++)
                                {
                                    if (dr == 0 && dc == 0) continue;
                                    int nr = qr + dr, nc = qc + dc;
                                    if (nr < 0 || nc < 0 || nr >= 6 || nc >= 6) continue;
                                    int nk = nr * 6 + nc;
                                    if (label[nk] != 0) continue;
                                    if (dr != 0 && dc != 0 && label[qr * 6 + nc] == -1 && label[nr * 6 + qc] == -1) continue;
                                    label[nk] = components;
                                    stack[top++] = nk;
                                }
                        }
                    }
                    if (components != 1) continue;
                    // The speed of the open fine cells inside it
                    float best = 0f;
                    for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
                            if (FineOpen(2 * r + i, 2 * c + j)) best = Math.Max(best, fine.speed[(2 * r + i) * fc + 2 * c + j]);
                    g.speed[idx] = best;
                    openedThisPass++;
                }
                opened += openedThisPass;
                if (openedThisPass == 0) break;
            }
            return opened;
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
                        // Fences and walls close only the cells they really run through the middle of (a chain where
                        // diagonal neighbours touch; A* does not cut corners between two closed cells), so a narrow
                        // passage next to them stays open on a 2 m grid. Everything else marks every cell it touches.
                        if (role == TerrainRole.BlockingLine) ThinLine(x0, y0, x1, y1, g.cols, g.rows, (c, r) => visit(r * g.cols + c, rule));
                        else Supercover(x0, y0, x1, y1, g.cols, g.rows, (c, r) => visit(r * g.cols + c, rule));
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

        // Closes the cells outside the map. A cell is outside when it is under a white mask area (an area symbol of
        // 600 and up filled with white, drawn over the terrain), or when it can be reached from the edge of the grid
        // without coming within reach of anything drawn on the map (50 m on forest maps, 20 m on sprint maps).
        public static int MarkOutside(MapFile map, MapGrid2 g, double ox, double oy, double scale, bool sprint)
        {
            int rows = g.rows, cols = g.cols, n = rows * cols;
            var covered = new bool[n];
            var outside = new bool[n];
            double step = 0.5 / scale;   // half a cell, in paper mm
            foreach (MapObject o in map.objects)
            {
                MapSymbol s = o.symbol;
                if (s == null) continue;
                if (o.kind == SymbolKind.Area && s.major >= 600 && IsWhiteFill(map, s))
                {
                    FillCells(o.parts, ox, oy, scale, cols, rows, (r, c) => outside[r * cols + c] = true);
                    continue;
                }
                if (!MapExtent.IsTerrain(s)) continue;
                foreach (double[] p in o.parts)
                {
                    int count = p.Length / 2;
                    for (int i = 0; i < count; i++)
                    {
                        double x0 = p[2 * i], y0 = p[2 * i + 1];
                        Mark(covered, (x0 - ox) * scale, (y0 - oy) * scale, cols, rows);
                        if (i + 1 >= count) continue;
                        double x1 = p[2 * i + 2], y1 = p[2 * i + 3];
                        int steps = (int)Math.Ceiling(MapGeometry.Dist(x0, y0, x1, y1) / step);
                        for (int k = 1; k < steps; k++)
                        {
                            double t = k / (double)steps;
                            Mark(covered, (x0 + (x1 - x0) * t - ox) * scale, (y0 + (y1 - y0) * t - oy) * scale, cols, rows);
                        }
                    }
                }
                if (o.kind == SymbolKind.Area)
                {
                    FillCells(o.parts, ox, oy, scale, cols, rows, (r, c) => covered[r * cols + c] = true);
                }
            }

            int reach = Math.Max(1, (int)Math.Round((sprint ? 20.0 : 50.0) / g.cellSize));
            int[] near = Distance(covered, rows, cols, reach + 1);
            // Flood from the edge through cells that are not near anything drawn
            var far = new bool[n];
            var queue = new Queue<int>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (r != 0 && c != 0 && r != rows - 1 && c != cols - 1) continue;
                    int idx = r * cols + c;
                    if (near[idx] > reach && !far[idx]) { far[idx] = true; queue.Enqueue(idx); }
                }
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int r = idx / cols, c = idx % cols;
                for (int d = 0; d < 4; d++)
                {
                    int rr = r + (d == 0 ? 1 : d == 1 ? -1 : 0), cc = c + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                    int j = rr * cols + cc;
                    if (far[j] || near[j] <= reach) continue;
                    far[j] = true;
                    queue.Enqueue(j);
                }
            }
            // Back towards the drawn map by the same distance, so the edge of the map is the edge of what is drawn
            int[] fromFar = Distance(far, rows, cols, reach + 2);
            int closed = 0;
            for (int i = 0; i < n; i++)
            {
                bool isOutside = outside[i] || (fromFar[i] <= reach + 1 && !covered[i]);
                if (!isOutside) continue;
                g.speed[i] = 0f;
                closed++;
            }
            return closed;
        }

        static void Mark(bool[] cells, double cx, double cy, int cols, int rows)
        {
            int c = (int)Math.Floor(cx), r = (int)Math.Floor(cy);
            if (c >= 0 && r >= 0 && c < cols && r < rows) cells[r * cols + c] = true;
        }

        static bool IsWhiteFill(MapFile map, MapSymbol s)
        {
            if (s.area == null || s.area.fillColor < 0 || s.area.fillColor >= map.colors.Count) return false;
            MapColor color = map.colors[s.area.fillColor];
            return color.r >= 245 && color.g >= 245 && color.b >= 245;
        }

        // Steps (8 directions) from each cell to the nearest marked cell, counted up to limit
        static int[] Distance(bool[] marked, int rows, int cols, int limit)
        {
            int n = rows * cols;
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
                int r = idx / cols, c = idx % cols;
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int rr = r + dr, cc = c + dc;
                        if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                        int j = rr * cols + cc;
                        if (dist[j] > d0 + 1) { dist[j] = d0 + 1; queue.Enqueue(j); }
                    }
            }
            return dist;
        }

        // The cells of a line drawn one cell per step along its longer direction (like Bresenham): neighbouring cells of
        // the chain can touch only at a corner
        public static void ThinLine(double x0, double y0, double x1, double y1, int cols, int rows, Action<int, int> visit)
        {
            double dx = x1 - x0, dy = y1 - y0;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) * 2));
            int lastX = int.MinValue, lastY = int.MinValue;
            for (int k = 0; k <= steps; k++)
            {
                double t = k / (double)steps;
                int cx = (int)Math.Floor(x0 + dx * t), cy = (int)Math.Floor(y0 + dy * t);
                if (cx == lastX && cy == lastY) continue;
                lastX = cx; lastY = cy;
                if (cx >= 0 && cy >= 0 && cx < cols && cy < rows) visit(cx, cy);
            }
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
