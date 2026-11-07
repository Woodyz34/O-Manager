using System.Collections.Generic;
using UnityEngine;

// Closes the grid cells an out-of-bounds area or uncrossable boundary covers, with the corners already in grid
// coordinates (x = column, y = row, cell j spans j to j + 1). Plain C# with no Unity objects, so the AI course
// can use it on a background thread (SprintBarriers) and it is exactly the same rule the race uses (ForbiddenAreas.Block).
public static class ForbiddenRaster
{
    // Closes every cell whose centre is inside the area, and every cell the border runs through, so even a narrow
    // area is a wall the runners can not slip through. Each cell closed is added to 'changed' with its old speed
    // (when given), so it can be opened again. Returns how many cells were closed.
    public static int Close(float[,] speed, IList<Vector2> poly, bool line, List<KeyValuePair<int, float>> changed = null)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        int closed = 0;
        // Inside, for an area (a boundary line is only its sides): one scanline through the middle of every row
        if (!line)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 p in poly) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(minY)), lastRow = Mathf.Min(rows - 1, Mathf.CeilToInt(maxY));
            List<float> crossings = new List<float>();
            for (int i = firstRow; i <= lastRow; i++)
            {
                float y = i + 0.5f;
                crossings.Clear();
                for (int k = 0; k < poly.Count; k++)
                {
                    Vector2 a = poly[k], b = poly[(k + 1) % poly.Count];
                    if ((a.y <= y && y < b.y) || (b.y <= y && y < a.y))
                    {
                        crossings.Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
                    }
                }
                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int from = Mathf.Max(0, Mathf.CeilToInt(crossings[k] - 0.5f));
                    int to = Mathf.Min(cols - 1, Mathf.FloorToInt(crossings[k + 1] - 0.5f));
                    for (int j = from; j <= to; j++) closed += CloseCell(speed, i, j, cols, changed);
                }
            }
        }

        // The border: walk along every side in quarter cells (a line's last corner is not joined to its first)
        int sides = line ? poly.Count - 1 : poly.Count;
        for (int k = 0; k < sides; k++)
        {
            Vector2 a = poly[k], b = poly[(k + 1) % poly.Count];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 4f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                int j = Mathf.FloorToInt(p.x), i = Mathf.FloorToInt(p.y);
                if (i >= 0 && i < rows && j >= 0 && j < cols) closed += CloseCell(speed, i, j, cols, changed);
            }
        }
        return closed;
    }

    // Opens again the cells Close closed (newest first, so a cell closed twice gets its first speed back)
    public static void Undo(float[,] speed, List<KeyValuePair<int, float>> changed)
    {
        int cols = speed.GetLength(1);
        for (int k = changed.Count - 1; k >= 0; k--)
        {
            int index = changed[k].Key;
            speed[index / cols, index % cols] = changed[k].Value;
        }
        changed.Clear();
    }

    static int CloseCell(float[,] speed, int i, int j, int cols, List<KeyValuePair<int, float>> changed)
    {
        if (speed[i, j] <= 0f) return 0;
        changed?.Add(new KeyValuePair<int, float>(i * cols + j, speed[i, j]));
        speed[i, j] = 0f;
        return 1;
    }
}
