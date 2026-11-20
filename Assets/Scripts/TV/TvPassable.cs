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

    // The grid lets a runner through here, so no house stands here (a cell the course's purple shapes closed keeps
    // whatever the map has: closed by them, it is no way through either way)
    public bool NoHouse(double e, double n)
    {
        if (!Cell(e, n, out int i, out int j)) return false;
        return speed[i, j] > 0.01f;
    }
}
