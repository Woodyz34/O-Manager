using System;
using System.Collections.Generic;
using UnityEngine;

// Something the course setter draws on the map that the runners must keep out of, in the map's own coordinates like
// the controls. It belongs to the whole course (every leg of a relay). Two kinds, as in ISOM / ISSprOM:
// - an out-of-bounds area (709): a polygon with purple vertical stripes the runners must not enter
// - an uncrossable boundary (707): a thick purple line the runners must not cross (line = true; its ends are not joined)
[Serializable]
public class ForbiddenArea
{
    public const int MaxCorners = 40;       // per area or line
    public const int MaxAreas = 20;         // areas and lines together
    public const int MaxCornersTotal = 150; // keeps the course small enough to send in the lobby (6 characters a corner)

    public List<CoursePoint> corners = new List<CoursePoint>();
    public bool line;                       // an uncrossable boundary instead of an area

    public int MinCorners => line ? 2 : 3;
    public bool IsValid => corners != null && corners.Count >= MinCorners;
}

// Turning out-of-bounds areas into closed grid cells, and other geometry for them
public static class ForbiddenAreas
{
    public static bool Any(CourseData course)
    {
        if (course == null || course.forbidden == null) return false;
        foreach (ForbiddenArea area in course.forbidden)
        {
            if (area != null && area.IsValid) return true;
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
            if (area == null || !area.IsValid) continue;
            // Corners in grid cells (x = column, y = row), the same way MapDefinition.ToCell works
            List<Vector2> poly = new List<Vector2>(area.corners.Count);
            foreach (CoursePoint corner in area.corners)
            {
                Vector3 world = map.ToWorld(corner);
                poly.Add(new Vector2(world.x / cell + cols / 2f, world.y / cell + rows / 2f));
            }

            closed += ForbiddenRaster.Close(speed, poly, area.line);
        }
        return closed;
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
        Debug.Log("ForbiddenAreas: closed " + closed + " grid cells for " + course.forbidden.Count + " out-of-bounds areas and uncrossable boundaries");
    }

    // How far a point is from a polyline (closed = the last corner joins the first), in the same units as the points
    public static float DistanceToLine(IList<Vector3> corners, Vector3 point, bool closed)
    {
        float best = float.MaxValue;
        int sides = closed ? corners.Count : corners.Count - 1;
        for (int k = 0; k < sides; k++)
        {
            Vector2 a = corners[k], b = corners[(k + 1) % corners.Count], p = point;
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
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
