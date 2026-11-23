using System.Collections.Generic;
using MapImport;

// Stairs are quick up and slow down (2026-11-20, Anders: running up stairs is about as quick as running up a road,
// but down them it must be slower, 0.6). The grid gives stairs the speed of a road (SymbolRules.StairsSpeed), so the
// slope model already makes them as quick uphill as a road would be; here every direction that goes down the stairs
// is set to SymbolRules.StairsDownSpeed instead, whatever the slope.
//
// Which way is down comes from the ground heights a few cells either side (the height model is smooth, so the steps
// themselves don't show). Stairs where the heights are flat have no down: they run like a road both ways.
public static class StairsSlopes
{
    static readonly string[] directionNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    static readonly int[] directionX = { 0, 1, 1, 1, 0, -1, -1, -1 };
    static readonly int[] directionY = { 1, 1, 0, -1, -1, -1, 0, 1 };

    // Height difference over the baseline (metres) that counts as down
    const float MinDrop = 0.05f;

    public static void Apply(string gridName, float[,] speed, Dictionary<string, float[,]> slopes, float[,] heights)
    {
        if (speed == null || slopes == null || heights == null || string.IsNullOrEmpty(gridName) || !gridName.EndsWith("grid")) return;
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        if (heights.GetLength(0) != rows || heights.GetLength(1) != cols) return;
        byte[] kinds = MapPackages.LoadBytes(gridName.Substring(0, gridName.Length - 4) + "kinds", ".bytes");
        if (kinds == null || kinds.Length != rows * cols) return;

        float cellSize = 2f;
        foreach (MapDefinition m in MapDefinition.All)
            if (m != null && m.gridName == gridName) { cellSize = m.gridSize; break; }
        // About 4 m each way, at least two cells
        int reach = System.Math.Max(2, (int)System.Math.Round(4f / System.Math.Max(0.25f, cellSize)));

        var factors = new float[8][,];
        for (int d = 0; d < 8; d++)
        {
            if (!slopes.TryGetValue(directionNames[d], out factors[d]) || factors[d] == null) return;
        }
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                if (kinds[i * cols + j] != GroundKind.Stairs || speed[i, j] <= 0.01f) continue;
                for (int d = 0; d < 8; d++)
                {
                    float ahead = HeightAt(heights, i + directionY[d] * reach, j + directionX[d] * reach, rows, cols, heights[i, j]);
                    float behind = HeightAt(heights, i - directionY[d] * reach, j - directionX[d] * reach, rows, cols, heights[i, j]);
                    if (ahead - behind < -MinDrop)
                        factors[d][i, j] = SymbolRules.StairsDownSpeed / speed[i, j];
                }
            }
        }
    }

    static float HeightAt(float[,] h, int i, int j, int rows, int cols, float fallback)
    {
        if (i < 0) i = 0; else if (i >= rows) i = rows - 1;
        if (j < 0) j = 0; else if (j >= cols) j = cols - 1;
        float v = h[i, j];
        return float.IsNaN(v) ? fallback : v;
    }
}
