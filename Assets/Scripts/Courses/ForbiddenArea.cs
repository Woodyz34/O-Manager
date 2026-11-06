using System;
using System.Collections.Generic;
using UnityEngine;

// An out-of-bounds area (ISOM / ISSprOM 709) the course setter draws on the map: purple vertical stripes the runners
// must not enter. It is a polygon of corners in the map's own coordinates, like the controls, and belongs to the
// whole course (every leg of a relay).
[Serializable]
public class ForbiddenArea
{
    public const int MaxCorners = 40;       // per area
    public const int MaxAreas = 20;
    public const int MaxCornersTotal = 150; // keeps the course small enough to send in the lobby (6 characters a corner)

    public List<CoursePoint> corners = new List<CoursePoint>();
}

// Turning out-of-bounds areas into closed grid cells, and other geometry for them
public static class ForbiddenAreas
{
    public static bool Any(CourseData course)
    {
        if (course == null || course.forbidden == null) return false;
        foreach (ForbiddenArea area in course.forbidden)
        {
            if (area != null && area.corners != null && area.corners.Count >= 3) return true;
        }
        return false;
    }

    // Closes every cell an area covers: cells whose centre is inside, and every cell the border runs through, so
    // even a narrow area is a wall the runners can not slip through. Returns how many cells were closed.
    public static int Block(float[,] speed, MapDefinition map, List<ForbiddenArea> areas)
    {
        if (speed == null || map == null || areas == null) return 0;
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float cell = map.CellSize;
        int closed = 0;
        foreach (ForbiddenArea area in areas)
        {
            if (area == null || area.corners == null || area.corners.Count < 3) continue;
            // Corners in grid cells (x = column, y = row), the same way MapDefinition.ToCell works
            List<Vector2> poly = new List<Vector2>(area.corners.Count);
            foreach (CoursePoint corner in area.corners)
            {
                Vector3 world = map.ToWorld(corner);
                poly.Add(new Vector2(world.x / cell + cols / 2f, world.y / cell + rows / 2f));
            }

            // Inside: one scanline through the middle of every row
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
                    for (int j = from; j <= to; j++) closed += Close(speed, i, j);
                }
            }

            // The border: walk along every side in quarter cells
            for (int k = 0; k < poly.Count; k++)
            {
                Vector2 a = poly[k], b = poly[(k + 1) % poly.Count];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) * 4f));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                    int j = Mathf.FloorToInt(p.x), i = Mathf.FloorToInt(p.y);
                    if (i >= 0 && i < rows && j >= 0 && j < cols) closed += Close(speed, i, j);
                }
            }
        }
        return closed;
    }

    static int Close(float[,] speed, int i, int j)
    {
        if (speed[i, j] <= 0f) return 0;
        speed[i, j] = 0f;
        return 1;
    }

    // A copy of the grid with the course's out-of-bounds areas closed, or the grid itself when the course has none
    public static MapGrid WithAreas(MapDefinition map, MapGrid grid, CourseData course)
    {
        if (grid == null || !Any(course)) return grid;
        MapGrid copy = new MapGrid { speed = (float[,])grid.speed.Clone(), slopes = grid.slopes };
        Block(copy.speed, map, course.forbidden);
        return copy;
    }

    // In the competition: close the areas of the course being run in the grid every runner uses
    public static void ApplyToCompetition(PathfindingManager manager)
    {
        CourseData course = CourseLoader.Loaded;
        MapDefinition map = CourseLoader.LoadedMap;
        if (manager == null || manager.speedGrid2D == null || map == null || !Any(course)) return;
        string gridName = manager.jsonFileName ?? "";
        if (gridName.EndsWith(".json")) gridName = gridName.Substring(0, gridName.Length - 5);
        if (gridName != map.gridName) return;   // a scene that runs another map
        int closed = Block(manager.speedGrid2D, map, course.forbidden);
        Debug.Log("ForbiddenAreas: closed " + closed + " grid cells for " + course.forbidden.Count + " out-of-bounds areas");
    }

    // Even-odd rule, so it works for any outline the course setter clicks, also one that crosses itself
    public static bool Contains(IList<Vector3> polygon, Vector3 point)
    {
        if (polygon == null || polygon.Count < 3) return false;
        bool inside = false;
        for (int k = 0, prev = polygon.Count - 1; k < polygon.Count; prev = k++)
        {
            Vector3 a = polygon[k], b = polygon[prev];
            if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    // Triangles for drawing a polygon (ear clipping). An outline that crosses itself falls back to a fan.
    public static List<int> Triangulate(IList<Vector3> polygon)
    {
        List<int> triangles = new List<int>();
        int n = polygon.Count;
        if (n < 3) return triangles;
        List<int> left = new List<int>(n);
        float area = 0f;
        for (int k = 0; k < n; k++)
        {
            Vector3 a = polygon[k], b = polygon[(k + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        // Work counter-clockwise
        if (area > 0f) for (int k = 0; k < n; k++) left.Add(k);
        else for (int k = n - 1; k >= 0; k--) left.Add(k);

        int guard = 0;
        while (left.Count > 3 && guard++ < n * n)
        {
            bool clipped = false;
            for (int k = 0; k < left.Count; k++)
            {
                int ia = left[(k + left.Count - 1) % left.Count], ib = left[k], ic = left[(k + 1) % left.Count];
                Vector2 a = polygon[ia], b = polygon[ib], c = polygon[ic];
                if (Cross(a, b, c) <= 0f) continue;   // a reflex corner
                bool empty = true;
                foreach (int other in left)
                {
                    if (other == ia || other == ib || other == ic) continue;
                    if (InTriangle(polygon[other], a, b, c)) { empty = false; break; }
                }
                if (!empty) continue;
                triangles.Add(ia); triangles.Add(ib); triangles.Add(ic);
                left.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped) break;
        }
        if (left.Count == 3)
        {
            triangles.Add(left[0]); triangles.Add(left[1]); triangles.Add(left[2]);
        }
        else if (left.Count > 3)
        {
            for (int k = 1; k + 1 < left.Count; k++) { triangles.Add(left[0]); triangles.Add(left[k]); triangles.Add(left[k + 1]); }
        }
        return triangles;
    }

    static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross(a, b, p) >= 0f && Cross(b, c, p) >= 0f && Cross(c, a, p) >= 0f;
    }
}
