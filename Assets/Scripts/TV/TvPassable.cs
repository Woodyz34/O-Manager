using System;
using System.Collections.Generic;

// Where the 2D game lets a runner go, for the run-yourself 3D world (2026-11-20, Anders: in a 3D sprint one could run
// through the course's out-of-bounds areas and barriers, and got stuck in a passage that is open on the map). It is
// the competition's own grid (PathfindingManager.speedGrid2D: the map's speeds with the course's out-of-bounds areas,
// barriers and bridges put in), so the 3D world closes exactly where the 2D race does.
//   - Closed(e, n): the runner may not stand here. On a sprint map every closed grid cell (houses, walls, high fences,
//     gardens, out of bounds); in the forest only the course's purple cells (the forest keeps its own 3D rules).
//   - NoHouse(e, n): the grid lets a runner through here, so no house is built on it: a passage between two houses
//     that the 2D game opens is a passage in 3D too.
// Plain C#: read from background threads. Made on the main thread from the scene's grid; never changed afterwards.
public class TvPassable
{
    readonly float[,] speed;
    readonly HashSet<int> purple;
    readonly double minx, miny, spanX, spanY;
    readonly int rows, cols;
    public readonly bool sprint;
    // What the map has in every cell (GroundKind, the package's kinds.bytes), or null: where the map has a canopy or
    // a passage under a house (ISSprOM 522, light grey), the 3D world puts a roof (2026-11-20, Anders: a passage under
    // a shopping centre was open sky with trees in it)
    readonly byte[] kinds;

    public TvPassable(float[,] speed, HashSet<int> purple, double minx, double miny, double maxx, double maxy, bool sprint, byte[] kinds)
        : this(speed, purple, minx, miny, maxx, maxy, sprint)
    {
        if (kinds != null && kinds.Length == rows * cols) this.kinds = kinds;
    }

    public TvPassable(float[,] speed, HashSet<int> purple, double minx, double miny, double maxx, double maxy, bool sprint)
    {
        // A copy: the scene's grid may change later (a new race), the 3D world must not
        this.speed = (float[,])speed.Clone();
        this.purple = purple != null ? new HashSet<int>(purple) : new HashSet<int>();
        rows = speed.GetLength(0);
        cols = speed.GetLength(1);
        this.minx = minx;
        this.miny = miny;
        spanX = Math.Max(1e-6, maxx - minx);
        spanY = Math.Max(1e-6, maxy - miny);
        this.sprint = sprint;
    }

    // The grid cell of a map point, the same way MapDefinition.ToCell works; false when off the grid
    bool Cell(double e, double n, out int i, out int j)
    {
        j = (int)Math.Floor((e - minx) / spanX * (cols - 1) + 0.5);
        i = (int)Math.Floor((n - miny) / spanY * (rows - 1) + 0.5);
        return i >= 0 && j >= 0 && i < rows && j < cols;
    }

    // Metres between two cell centres
    public double CellSize => spanX / Math.Max(1, cols - 1);

    // Under a canopy or a passage under a house on the map
    public bool Canopy(double e, double n) => kinds != null && Cell(e, n, out int i, out int j) && kinds[i * cols + j] == MapImport.GroundKind.Canopy;

    // The centres of the canopy cells in a box (map metres), with their row and column
    public void CanopyCells(double e0, double n0, double e1, double n1, List<(int i, int j, double e, double n)> into)
    {
        if (kinds == null) return;
        int j0 = Math.Max(0, (int)Math.Floor((e0 - minx) / spanX * (cols - 1))), j1 = Math.Min(cols - 1, (int)Math.Ceiling((e1 - minx) / spanX * (cols - 1)));
        int i0 = Math.Max(0, (int)Math.Floor((n0 - miny) / spanY * (rows - 1))), i1 = Math.Min(rows - 1, (int)Math.Ceiling((n1 - miny) / spanY * (rows - 1)));
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
                if (kinds[i * cols + j] == MapImport.GroundKind.Canopy)
                    into.Add((i, j, minx + j * spanX / (cols - 1), miny + i * spanY / (rows - 1)));
    }

    // A purple cell: inside an out-of-bounds area or on an uncrossable boundary the course setter drew
    public bool Purple(double e, double n) => Cell(e, n, out int i, out int j) && purple.Contains(i * cols + j);

    public bool Closed(double e, double n)
    {
        if (!Cell(e, n, out int i, out int j)) return sprint;
        if (purple.Contains(i * cols + j)) return true;
        return sprint && speed[i, j] <= 0.01f;
    }

    // An open cell of the grid (not one the course closed) has its middle within reach metres of here. The 2D grid's
    // cells are 2 m, and a narrow way (a gap between two walls, a footbridge between two fences) is often a single
    // row of open cells that does not lie quite on the way: in 3D the runner then got stuck in a gap that is open on
    // the map (2026-11-22, Anders). Near such a cell the 3D world's own walls, fences and ground decide.
    public bool NearOpen(double e, double n, double reach)
    {
        if (!Cell(e, n, out int i, out int j)) return false;
        double sx = spanX / Math.Max(1, cols - 1), sy = spanY / Math.Max(1, rows - 1), r2 = reach * reach;
        for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
            {
                int ii = i + di, jj = j + dj;
                if (ii < 0 || jj < 0 || ii >= rows || jj >= cols || speed[ii, jj] <= 0.01f || purple.Contains(ii * cols + jj)) continue;
                double de = minx + jj * sx - e, dn = miny + ii * sy - n;
                if (de * de + dn * dn <= r2) return true;
            }
        return false;
    }

    // Closed in the grid, but the map's ground under it is ground a runner runs on (paving, grass, forest): only a
    // blocking line (a high fence, a wall, an impassable cliff) closed it. The 2D grid draws such a line a whole 2 m
    // cell thick, so a narrow way between two fences can be closed from end to end in the grid though open on the map
    // (2026-11-26, Anders: every runner stopped in a gap between two fences with a path through it). In 3D the
    // fences themselves stand there and decide. False without the map's ground kinds.
    public bool ClosedByLine(double e, double n)
    {
        if (kinds == null || !Cell(e, n, out int i, out int j)) return false;
        if (purple.Contains(i * cols + j) || speed[i, j] > 0.01f) return false;
        switch (kinds[i * cols + j])
        {
            case MapImport.GroundKind.Forest: case MapImport.GroundKind.Open: case MapImport.GroundKind.Cultivated:
            case MapImport.GroundKind.RoughOpen: case MapImport.GroundKind.OpenTrees: case MapImport.GroundKind.LightGreen:
            case MapImport.GroundKind.Paved: case MapImport.GroundKind.Stony: case MapImport.GroundKind.Sand:
            case MapImport.GroundKind.BareRock: case MapImport.GroundKind.BrokenGround: case MapImport.GroundKind.Stairs:
            case MapImport.GroundKind.Canopy:
                return true;
        }
        return false;
    }

    // The grid lets a runner through here, so no house stands here (a cell the course's purple shapes closed keeps
    // whatever the map has: closed by them, it is no way through either way)
    public bool NoHouse(double e, double n)
    {
        if (!Cell(e, n, out int i, out int j)) return false;
        return speed[i, j] > 0.01f;
    }
}
