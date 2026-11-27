using System;
using System.Collections.Generic;

// Turns a map file into the walkability grid the runners use: one speed per cell (0 = can't be entered) and a
// ground height per cell for uphill and downhill.
//
// Order of work, each step on top of the one before:
//   1. areas (forest, open land, marsh, lakes, buildings ...), the colour drawn on top wins, like on the paper
//   1b. stony ground and boulder fields slow down the ground under them
//   2. walls and fences that take a moment to cross
//   3. paths and roads: every cell a path line passes through gets the path's speed, so a path has no slow gaps
//   4. impassable lines (cliffs, high fences, impassable walls): no way through, except where a path crosses one
//      on a forest map (there is a gate, even if it is not drawn)
//   5. bridges and crossing points open the way again
//   6. everything outside the map is closed: beyond the edge of what is drawn, under white "mask" areas that
//      mappers put over the parts that are not part of the map, under symbols named as a club badge or logo, and
//      under small groups of drawn things out in the white margin (logos, the legend, a sponsor)
//   7. narrow passages are opened again on sprint maps
//   8. small runnable islands nobody can get to from the rest of the map are closed (the legend, a badge)
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
        // The quarter-size pass for narrow passages is made this many cells square at a time
        const int QuarterTile = 300;

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
                int opened = OpenNarrowPassages(g, fine, 2);
                fine = null;
                // Then with cells a quarter of the size, the impassable lines (walls, high fences, hedges) as wide as
                // the map draws them and square at their ends (2026-11-27, Anders: a gap between two walls that shows
                // on the map was often shut: a 1 m cell is not always free in a 1.4 m gap). Drawn as wide as on the
                // map, a line that ends against another one leaves no way through, as on the map.
                // Built a square of the map at a time, so a big town map needs little memory
                double mpm = map.MetersPerMm;
                for (int r0 = 0; r0 < g.rows; r0 += QuarterTile)
                    for (int c0 = 0; c0 < g.cols; c0 += QuarterTile)
                    {
                        int r1 = Math.Min(g.rows, r0 + QuarterTile), c1 = Math.Min(g.cols, c0 + QuarterTile);
                        // A few cells more all round, so the cells at the square's edge see their neighbours
                        int mr0 = Math.Max(0, r0 - 4), mc0 = Math.Max(0, c0 - 4), mr1 = Math.Min(g.rows, r1 + 4), mc1 = Math.Min(g.cols, c1 + 4);
                        var part = new MapExtent
                        {
                            minX = extent.minX + mc0 * cellSize / mpm, maxX = extent.minX + mc1 * cellSize / mpm,
                            minY = extent.minY + mr0 * cellSize / mpm, maxY = extent.minY + mr1 * cellSize / mpm
                        };
                        MapGrid2 quarter = BuildSpeed(map, part, sprint, cellSize / 4f, null, true);
                        opened += OpenNarrowPassages(g, quarter, 4, r0, c0, r1, c1, mr0, mc0);
                    }
                log?.Invoke($"Narrow passages: {opened} cells opened in {passSw.ElapsedMilliseconds} ms");
            }

            // 8. Small islands: runnable bits that can't be reached from the rest of the map (a club badge or the
            //    legend in the white margin, a courtyard shut in by buildings) are closed, so nothing can be put there
            int islands = CloseIslands(g, out int islandCells);
            if (islands > 0) log?.Invoke($"Islands: {islands} small parts that can't be reached closed ({islandCells} cells)");

            // Heights from the contour lines
            var sw = System.Diagnostics.Stopwatch.StartNew();
            g.height = ContourHeights.Build(map, extent, g.rows, g.cols, cellSize, out g.hasHeights, log);
            log?.Invoke($"Heights from contours: {(g.hasHeights ? "done" : "no contours found")} in {sw.ElapsedMilliseconds} ms");
            return g;
        }

        // Steps 1 to 6: the speed of every cell
        // drawnLines: the impassable lines are as wide as the map draws them (the narrow-passage pass), not one cell
        public static MapGrid2 BuildSpeed(MapFile map, MapExtent extent, bool sprint, float cellSize, Action<string> log, bool drawnLines = false)
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
                TerrainRule rule = SymbolRules.For(o.symbol, sprint, map.terrain);
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

            // 1b. Stony ground: slower than the ground it lies on
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Area) continue;
                TerrainRule rule = SymbolRules.For(o.symbol, sprint, map.terrain);
                if (rule.role != TerrainRole.SlowArea) continue;
                FillArea(g, o.parts, ox, oy, scale, (idx) =>
                {
                    if (g.speed[idx] > 0f) g.speed[idx] = Math.Max(0.05f, g.speed[idx] * rule.speed);
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
            // 3b. Stairs (ISOM / ISSprOM 532): a way up or down with its own speed (quick up, slow down: the kind
            //     tells StairsSlopes which cells are stairs), so the speed of the stairs is set, not the quicker of the two
            var stairs = new bool[n];
            ForLines(map, sprint, TerrainRole.Stairs, g, ox, oy, scale, (idx, rule) =>
            {
                if (building[idx]) return;
                g.speed[idx] = rule.speed;
                g.kind[idx] = GroundKind.Stairs;
                hard[idx] = false;
                stairs[idx] = true;
            });

            // 4. Impassable lines. On forest maps a path that crosses the line goes through it (the gate is rarely
            //    drawn), but only in the cell where the two lines really cross: a path that runs along a fence must not
            //    open the whole fence. On sprint maps the cells are small enough that a path beside a wall is its own cell.
            var lineSpeed = new Dictionary<int, float>();
            //    Stairs go through the line they cross on every map: a stairway is drawn exactly to take the runner up
            //    or down a cliff, a retaining wall or an impassable wall (ISOM 2017-2, 532).
            ForLines(map, sprint, TerrainRole.BlockingLine, g, ox, oy, scale, (idx, rule) =>
            {
                if (((!sprint && path[idx]) || stairs[idx]) && !lineSpeed.ContainsKey(idx)) lineSpeed[idx] = g.speed[idx];
                g.speed[idx] = 0f;
                hard[idx] = true;
            }, drawnLines);
            if (lineSpeed.Count > 0)
            {
                bool[] crossers = path;
                if (sprint) crossers = stairs;
                else for (int i = 0; i < n; i++) if (stairs[i]) crossers[i] = true;
                int gates = OpenPathCrossings(map, sprint, g, ox, oy, scale, lineSpeed, hard, crossers);
                log?.Invoke($"Paths and stairs through impassable lines: {gates} crossings opened");
                if (!sprint)
                {
                    int along = OpenPathsAlongLines(map, sprint, g, ox, oy, scale, lineSpeed, hard, crossers, stairs);
                    log?.Invoke($"Paths beside impassable lines: {along} cells opened (the line moved off the path)");
                }
            }

            // 5. Bridges and crossing points
            ForLines(map, sprint, TerrainRole.Bridge, g, ox, oy, scale, (idx, rule) =>
            {
                g.speed[idx] = Math.Max(g.speed[idx], rule.speed);
                hard[idx] = false;
            });
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Point) continue;
                TerrainRule rule = SymbolRules.For(o.symbol, sprint, map.terrain);
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

        // Opens closed cells of g where the grid with cells f times smaller (fine, same corner: 2 = half the size,
        // 4 = a quarter) has a way through. A closed cell is opened only when, inside the 3 x 3 cells around it, the
        // open fine cells of it and of every open neighbour are all joined up in the fine grid. So the opened cell
        // joins only ground that really is joined there, and a fence or wall through the middle of a cell (open fine
        // cells on both sides, but not joined) stays shut. Opening one cell can make its neighbour openable too, so it
        // repeats until nothing changes.
        // Only the cells of rows r0..r1-1 and columns c0..c1-1 are opened (all when r1 is 0); fine starts at cell
        // (fineRow, fineCol) of g (a part of the map, GridBuilder.Build)
        public static int OpenNarrowPassages(MapGrid2 g, MapGrid2 fine, int f = 2, int r0 = 0, int c0 = 0, int r1 = 0, int c1 = 0,
                                             int fineRow = 0, int fineCol = 0)
        {
            int rows = g.rows, cols = g.cols, fr = fine.rows, fc = fine.cols;
            if (r1 <= 0) { r1 = rows; c1 = cols; }
            int fro = f * fineRow, fco = f * fineCol;
            bool FineOpen(int r, int c)
            {
                r -= fro;
                c -= fco;
                return r >= 0 && c >= 0 && r < fr && c < fc && fine.speed[r * fc + c] > 0f;
            }
            float FineSpeed(int r, int c) => fine.speed[(r - fro) * fc + c - fco];
            bool AnyFineOpen(int r, int c)
            {
                for (int i = 0; i < f; i++)
                    for (int j = 0; j < f; j++)
                        if (FineOpen(f * r + i, f * c + j)) return true;
                return false;
            }

            // Closed cells with at least one open fine cell are the ones that can be opened
            var candidates = new List<int>();
            for (int r = Math.Max(0, r0); r < Math.Min(rows, r1); r++)
                for (int c = Math.Max(0, c0); c < Math.Min(cols, c1); c++)
                {
                    if (g.speed[r * cols + c] > 0f) continue;
                    if (AnyFineOpen(r, c)) candidates.Add(r * cols + c);
                }

            // Window: 3f x 3f fine cells = the 3 x 3 cells around the candidate
            int side = 3 * f, size = side * side;
            var label = new int[size];
            var stack = new int[size];
            int opened = 0;
            for (int pass = 0; pass < 6; pass++)
            {
                int openedThisPass = 0;
                foreach (int idx in candidates)
                {
                    if (g.speed[idx] > 0f) continue;
                    int r = idx / cols, c = idx % cols;
                    int fr0 = f * r - f, fc0 = f * c - f;
                    // Fine cells that take part: open ones inside the candidate or inside an open neighbour
                    for (int k = 0; k < size; k++)
                    {
                        int wr = k / side, wc = k % side;
                        int cr = r - 1 + wr / f, cc = c - 1 + wc / f;
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
                            for (int i = 0; i < f; i++) for (int j = 0; j < f; j++)
                                    if (label[(f * (dr + 1) + i) * side + f * (dc + 1) + j] == 0) any = true;
                            if (!any) ok = false;
                        }
                    if (!ok || openNeighbours == 0) continue;
                    // Flood the fine cells (8 directions, no cutting between two closed cells)
                    int components = 0;
                    for (int k = 0; k < size && components <= 1; k++)
                    {
                        if (label[k] != 0) continue;
                        components++;
                        int top = 0;
                        stack[top++] = k;
                        label[k] = components;
                        while (top > 0)
                        {
                            int q = stack[--top];
                            int qr = q / side, qc = q % side;
                            for (int dr = -1; dr <= 1; dr++)
                                for (int dc = -1; dc <= 1; dc++)
                                {
                                    if (dr == 0 && dc == 0) continue;
                                    int nr = qr + dr, nc = qc + dc;
                                    if (nr < 0 || nc < 0 || nr >= side || nc >= side) continue;
                                    int nk = nr * side + nc;
                                    if (label[nk] != 0) continue;
                                    if (dr != 0 && dc != 0 && label[qr * side + nc] == -1 && label[nr * side + qc] == -1) continue;
                                    label[nk] = components;
                                    stack[top++] = nk;
                                }
                        }
                    }
                    if (components != 1) continue;
                    // The speed of the open fine cells inside it
                    float best = 0f;
                    for (int i = 0; i < f; i++) for (int j = 0; j < f; j++)
                            if (FineOpen(f * r + i, f * c + j)) best = Math.Max(best, FineSpeed(f * r + i, f * c + j));
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
                             Action<int, TerrainRule> visit, bool drawnLines = false)
        {
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Line && o.kind != SymbolKind.Area) continue;
                TerrainRule rule = SymbolRules.For(o.symbol, sprint, map.terrain);
                if (o.kind == SymbolKind.Area)
                {
                    // An area's outline can be a blocking line (lake shore drawn as part of a combined symbol)
                    continue;
                }
                if (rule.role != role) continue;
                // Lines wider than a cell (streets on sprint maps) cover several cells across
                // (and, in the narrow-passage pass, the impassable lines: drawnLines)
                double halfCells = role == TerrainRole.Path || role == TerrainRole.Stairs || (drawnLines && role == TerrainRole.BlockingLine)
                    ? LineWidthMm(o.symbol) * scale / 2 : 0;
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

        // Forest maps: where a path crosses an impassable line (fence, wall, cliff), the cell of the crossing is opened
        // again with the path's speed. lineSpeed: the cells of impassable lines that a path also passes through, with
        // the speed they had before the line closed them; path: the cells paths pass through. Returns the number of cells opened.
        // On sprint maps only stairs open a line this way (path holds the stairs' cells then).
        static int OpenPathCrossings(MapFile map, bool sprint, MapGrid2 g, double ox, double oy, double scale, Dictionary<int, float> lineSpeed, bool[] hard, bool[] path)
        {
            // The impassable lines' segments (cell units), listed by the cells they pass through
            var segs = new List<double[]>();
            var byCell = new Dictionary<int, List<int>>();
            var pathSegs = new List<(double[] seg, float speed)>();
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Line) continue;
                TerrainRule rule = SymbolRules.For(o.symbol, sprint, map.terrain);
                bool blocking = rule.role == TerrainRole.BlockingLine;
                bool crosses = rule.role == TerrainRole.Stairs || (!sprint && rule.role == TerrainRole.Path);
                if (!blocking && !crosses) continue;
                foreach (double[] p in o.parts)
                {
                    for (int i = 0; i + 3 < p.Length; i += 2)
                    {
                        double[] seg = { (p[i] - ox) * scale, (p[i + 1] - oy) * scale, (p[i + 2] - ox) * scale, (p[i + 3] - oy) * scale };
                        if (!blocking)
                        {
                            pathSegs.Add((seg, rule.speed));
                            continue;
                        }
                        int k = segs.Count;
                        segs.Add(seg);
                        Supercover(seg[0], seg[1], seg[2], seg[3], g.cols, g.rows, (c, r) =>
                        {
                            int idx = r * g.cols + c;
                            if (!path[idx]) return;   // only where a path could cross it
                            if (!byCell.TryGetValue(idx, out List<int> list)) byCell[idx] = list = new List<int>();
                            if (list.Count == 0 || list[list.Count - 1] != k) list.Add(k);
                        });
                    }
                }
            }
            int opened = 0;
            var crossings = new List<(double x, double y)>();
            var seen = new HashSet<int>();
            foreach (var (seg, speed) in pathSegs)
            {
                // Where this bit of path crosses an impassable line
                crossings.Clear();
                seen.Clear();
                Supercover(seg[0], seg[1], seg[2], seg[3], g.cols, g.rows, (c, r) =>
                {
                    if (!byCell.TryGetValue(r * g.cols + c, out List<int> list)) return;
                    foreach (int k in list)
                    {
                        if (!seen.Add(k)) continue;
                        if (Cross(seg, segs[k], out double x, out double y)) crossings.Add((x, y));
                    }
                });
                if (crossings.Count == 0) continue;
                // Open the line's closed cells this bit of path passes through right at a crossing. A path chain of
                // cells has to go through at least one cell of the line to get across it, and that cell is close to
                // where the two lines cross.
                Supercover(seg[0], seg[1], seg[2], seg[3], g.cols, g.rows, (c, r) =>
                {
                    int idx = r * g.cols + c;
                    if (g.speed[idx] > 0f || !lineSpeed.TryGetValue(idx, out float before)) return;
                    double cx = c + 0.5, cy = r + 0.5;
                    foreach (var (x, y) in crossings)
                    {
                        if ((cx - x) * (cx - x) + (cy - y) * (cy - y) > 1.6 * 1.6) continue;
                        g.speed[idx] = Math.Max(before, speed);
                        hard[idx] = false;
                        opened++;
                        break;
                    }
                });
            }
            return opened;
        }

        // Forest maps (10 m cells): a road that runs close beside an impassable line without crossing it (a road along
        // the foot of a cliff, a fence or a lake shore) shares cells with the line, and the line closed them, so the
        // road was cut off (2026-11-24, Delsjön: the road over the water could not be drawn the last bit to the shore
        // because a cliff runs a few metres beside it). Such a cell is opened again when the whole bit of path in it
        // lies on one side of the line; the line is then moved off the path instead: the neighbour cells on the far
        // side of the line are closed, so the line still can't be crossed there. Where there is a path on the far side
        // too, or the path touches the line, the cell stays shut. Returns the number of cells opened.
        static int OpenPathsAlongLines(MapFile map, bool sprint, MapGrid2 g, double ox, double oy, double scale,
                                       Dictionary<int, float> lineSpeed, bool[] hard, bool[] path, bool[] stairs)
        {
            var todo = new HashSet<int>();
            foreach (var kv in lineSpeed) if (g.speed[kv.Key] <= 0f && !stairs[kv.Key]) todo.Add(kv.Key);
            if (todo.Count == 0) return 0;

            int cols = g.cols, rows = g.rows;
            var segs = new List<double[]>();
            var lineNear = new Dictionary<int, List<int>>();                    // todo cell -> line segments through its 3 x 3 cells
            var pathPoints = new Dictionary<int, List<(double x, double y)>>(); // todo cell -> points of paths inside it
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Line) continue;
                TerrainRule rule = SymbolRules.For(o.symbol, sprint, map.terrain);
                bool blocking = rule.role == TerrainRole.BlockingLine;
                bool isPath = rule.role == TerrainRole.Path;
                if (!blocking && !isPath) continue;
                foreach (double[] p in o.parts)
                {
                    for (int i = 0; i + 3 < p.Length; i += 2)
                    {
                        double[] seg = { (p[i] - ox) * scale, (p[i + 1] - oy) * scale, (p[i + 2] - ox) * scale, (p[i + 3] - oy) * scale };
                        if (isPath)
                        {
                            double len = Math.Sqrt((seg[2] - seg[0]) * (seg[2] - seg[0]) + (seg[3] - seg[1]) * (seg[3] - seg[1]));
                            int steps = Math.Max(1, (int)Math.Ceiling(len * 4));
                            for (int s = 0; s <= steps; s++)
                            {
                                double t = (double)s / steps;
                                double x = seg[0] + (seg[2] - seg[0]) * t, y = seg[1] + (seg[3] - seg[1]) * t;
                                int c = (int)Math.Floor(x), r = (int)Math.Floor(y);
                                if (c < 0 || r < 0 || c >= cols || r >= rows) continue;
                                int idx = r * cols + c;
                                if (!todo.Contains(idx)) continue;
                                if (!pathPoints.TryGetValue(idx, out var list)) pathPoints[idx] = list = new List<(double, double)>();
                                list.Add((x, y));
                            }
                            continue;
                        }
                        int k = segs.Count;
                        segs.Add(seg);
                        Supercover(seg[0], seg[1], seg[2], seg[3], cols, rows, (c, r) =>
                        {
                            for (int dr = -1; dr <= 1; dr++)
                                for (int dc = -1; dc <= 1; dc++)
                                {
                                    int rr = r + dr, cc = c + dc;
                                    if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                                    int idx = rr * cols + cc;
                                    if (!todo.Contains(idx)) continue;
                                    if (!lineNear.TryGetValue(idx, out List<int> list)) lineNear[idx] = list = new List<int>();
                                    if (!list.Contains(k)) list.Add(k);
                                }
                        });
                    }
                }
            }

            // Which side of the nearest line segment a point is on (+1 / -1), and how far from it (cells)
            int Side(List<int> near, double x, double y, out double distance)
            {
                distance = double.MaxValue;
                int side = 0;
                foreach (int k in near)
                {
                    double[] s = segs[k];
                    double dx = s[2] - s[0], dy = s[3] - s[1], l2 = dx * dx + dy * dy;
                    double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - s[0]) * dx + (y - s[1]) * dy) / l2));
                    double px = s[0] + t * dx - x, py = s[1] + t * dy - y;
                    double d = Math.Sqrt(px * px + py * py);
                    if (d >= distance) continue;
                    distance = d;
                    double cross = dx * (y - s[1]) - dy * (x - s[0]);
                    side = cross > 0 ? 1 : cross < 0 ? -1 : 0;
                }
                return side;
            }

            var cells = new List<int>(todo);
            cells.Sort();
            var close = new List<int>();
            int opened = 0;
            foreach (int idx in cells)
            {
                if (!pathPoints.TryGetValue(idx, out var points) || !lineNear.TryGetValue(idx, out List<int> near)) continue;
                // The whole bit of path in the cell must be on one side of the line, clear of it
                int pathSide = 0;
                bool ok = true;
                foreach (var (x, y) in points)
                {
                    int s = Side(near, x, y, out double d);
                    if (s == 0 || d < 0.05) { ok = false; break; }
                    if (pathSide == 0) pathSide = s;
                    else if (s != pathSide) { ok = false; break; }
                }
                if (!ok || pathSide == 0) continue;
                // The neighbours on the far side of the line take its place
                int r = idx / cols, c = idx % cols;
                close.Clear();
                for (int dr = -1; dr <= 1 && ok; dr++)
                    for (int dc = -1; dc <= 1 && ok; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int rr = r + dr, cc = c + dc;
                        if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                        int n = rr * cols + cc;
                        int s = Side(near, cc + 0.5, rr + 0.5, out _);
                        if (s == 0 || s == pathSide) continue;
                        if (path[n]) { ok = false; break; }   // a path on the other side too: the line has no room
                        if (g.speed[n] > 0f) close.Add(n);
                    }
                if (!ok) continue;
                g.speed[idx] = lineSpeed[idx];
                hard[idx] = false;
                foreach (int n in close)
                {
                    g.speed[n] = 0f;
                    hard[n] = true;
                }
                opened++;
            }
            return opened;
        }

        // Where two segments cross, if they do
        static bool Cross(double[] a, double[] b, out double x, out double y)
        {
            x = y = 0;
            double rx = a[2] - a[0], ry = a[3] - a[1], sx = b[2] - b[0], sy = b[3] - b[1];
            double den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-12) return false;
            double qx = b[0] - a[0], qy = b[1] - a[1];
            double t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
            if (t < 0 || t > 1 || u < 0 || u > 1) return false;
            x = a[0] + t * rx;
            y = a[1] + t * ry;
            return true;
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
                // A club badge or logo is often drawn with map symbols ("Blå för klubbmärke" 302.1): it is not ground
                if (IsDecoration(s))
                {
                    if (o.kind == SymbolKind.Area) FillCells(o.parts, ox, oy, scale, cols, rows, (r, c) => outside[r * cols + c] = true);
                    continue;
                }
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
            // Logos, the legend and the like out in the white margin: small groups of drawn things set apart from the map
            CloseMarginGroups(covered, outside, rows, cols, reach);
            bool[] far = FarFromDrawn(covered, rows, cols, reach);
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

        // Symbols for what is printed round the map, not the terrain, going by their names. Mappers often draw a club
        // badge with copies of map symbols ("Blå för klubbmärke", "Röd för klubbmärke"), so the number does not tell.
        static readonly string[] DecorationWords =
        {
            "klubbmärke", "klubbmarke", "klubbemblem", "logo", "logga", "emblem", "sponsor", "reklam",
            "teckenförklaring", "teckenforklaring", "legend", "kartfakta", "copyright"
        };

        public static bool IsDecoration(MapSymbol s)
        {
            if (s == null || string.IsNullOrEmpty(s.name)) return false;
            string name = s.name.ToLowerInvariant();
            foreach (string word in DecorationWords)
                if (name.Contains(word)) return true;
            return false;
        }

        // Closes every part of the open ground that is cut off from the main part of the map and is much smaller than
        // it (under 3 %), moving like the runners do (8 ways, no cutting a corner between two closed cells). Nobody can
        // run there from the rest of the map, so a start or control there could never be reached anyway.
        public static int CloseIslands(MapGrid2 g, out int closedCells)
        {
            closedCells = 0;
            int rows = g.rows, cols = g.cols, n = rows * cols;
            var label = new int[n];
            var sizes = new List<int> { 0 };
            var stack = new Stack<int>();
            for (int start = 0; start < n; start++)
            {
                if (label[start] != 0 || g.speed[start] <= 0.01f) continue;
                int id = sizes.Count, size = 0;
                label[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    size++;
                    int r = idx / cols, c = idx % cols;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int rr = r + dr, cc = c + dc;
                            if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                            int j = rr * cols + cc;
                            if (label[j] != 0 || g.speed[j] <= 0.01f) continue;
                            if (dr != 0 && dc != 0 && (g.speed[r * cols + cc] <= 0.01f || g.speed[rr * cols + c] <= 0.01f)) continue;
                            label[j] = id;
                            stack.Push(j);
                        }
                }
                sizes.Add(size);
            }
            int largest = 0;
            for (int i = 1; i < sizes.Count; i++) largest = Math.Max(largest, sizes[i]);
            int keepFrom = (int)Math.Ceiling(largest * 0.03);
            var close = new bool[sizes.Count];
            int islands = 0;
            for (int i = 1; i < sizes.Count; i++)
            {
                if (sizes[i] >= keepFrom) continue;
                close[i] = true;
                islands++;
            }
            if (islands == 0) return 0;
            for (int i = 0; i < n; i++)
            {
                if (label[i] == 0 || !close[label[i]]) continue;
                g.speed[i] = 0f;
                closedCells++;
            }
            return islands;
        }

        // Cells that can be reached from the edge of the grid without coming within reach of anything drawn
        static bool[] FarFromDrawn(bool[] covered, int rows, int cols, int reach)
        {
            int n = rows * cols;
            int[] near = Distance(covered, rows, cols, reach + 1);
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
            return far;
        }

        // A club badge, a sponsor's logo or the legend printed in the white margin is drawn, so the white paper round
        // it would count as ground. The drawn cells are put in groups (things within a quarter of the reach of each
        // other, 10 m on a forest map, belong together); a group much smaller than the map itself (under 5 % of the biggest) that lies out in the
        // white margin, next to paper that is far from the map, is taken off the map: its cells are closed and it no
        // longer counts as drawn. A lone stone in a big white forest is not touched, since it is not near the margin.
        // Returns how many groups were taken off.
        static int CloseMarginGroups(bool[] covered, bool[] outside, int rows, int cols, int reach)
        {
            int n = rows * cols;
            int link = Math.Max(1, reach / 4);
            int[] toDrawn = Distance(covered, rows, cols, link + 1);
            var group = new int[n];
            var drawnCount = new List<int> { 0 };
            var stack = new Stack<int>();
            for (int start = 0; start < n; start++)
            {
                if (group[start] != 0 || toDrawn[start] > link) continue;
                int id = drawnCount.Count, count = 0;
                group[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    if (covered[idx]) count++;
                    int r = idx / cols, c = idx % cols;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int rr = r + dr, cc = c + dc;
                            if (rr < 0 || cc < 0 || rr >= rows || cc >= cols) continue;
                            int j = rr * cols + cc;
                            if (group[j] != 0 || toDrawn[j] > link) continue;
                            group[j] = id;
                            stack.Push(j);
                        }
                }
                drawnCount.Add(count);
            }
            int groups = drawnCount.Count - 1;
            if (groups < 2) return 0;
            int biggest = 0;
            for (int i = 1; i <= groups; i++) biggest = Math.Max(biggest, drawnCount[i]);
            int smallBelow = Math.Max(1, (int)(biggest * 0.05));
            var small = new bool[groups + 1];
            bool any = false;
            for (int i = 1; i <= groups; i++)
            {
                small[i] = drawnCount[i] < smallBelow;
                any |= small[i];
            }
            if (!any) return 0;

            // The white margin as it would be without the small groups
            var mapOnly = new bool[n];
            for (int i = 0; i < n; i++) mapOnly[i] = covered[i] && !small[group[i]];
            bool[] far = FarFromDrawn(mapOnly, rows, cols, reach);
            int[] toFar = Distance(far, rows, cols, reach + 3);
            var inMargin = new bool[groups + 1];
            for (int i = 0; i < n; i++)
            {
                if (covered[i] && small[group[i]] && toFar[i] <= reach + 2) inMargin[group[i]] = true;
            }
            int taken = 0;
            for (int i = 1; i <= groups; i++) if (inMargin[i]) taken++;
            if (taken == 0) return 0;
            for (int i = 0; i < n; i++)
            {
                if (!covered[i] || !inMargin[group[i]]) continue;
                covered[i] = false;
                outside[i] = true;
            }
            return taken;
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
