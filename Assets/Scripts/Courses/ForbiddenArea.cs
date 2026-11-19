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
    // Areas and lines together. No real limit since 2026-11-19 (the user: "no max"); only a guard against broken data.
    // What limits them is the corners in all of them together (MaxCornersTotal), so the course still fits in the lobby.
    public const int MaxAreas = 1000;
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
    public static int Block(float[,] speed, MapDefinition map, List<ForbiddenArea> areas, HashSet<int> purpleCells = null)
    {
        if (speed == null || map == null || areas == null) return 0;
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float cell = map.CellSize;
        int closed = 0;
        var shapes = new List<(List<Vector2> poly, bool line)>();
        var changed = new List<KeyValuePair<int, float>>();
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

            closed += ForbiddenRaster.Close(speed, poly, area.line, changed);
            shapes.Add((poly, area.line));
        }
        closed -= OpenGaps(speed, map, shapes, changed);
        if (purpleCells != null) foreach (var c in changed) purpleCells.Add(c.Key);
        return closed;
    }

    // ---- Gaps the course setter left on purpose (2026-11-15) ----
    // Every cell a purple line or area touches is closed, so a small gap between two of them (or between the end of a
    // purple line and a house or a wall) was closed too: the line could be drawn through it but nobody could run
    // there. Where a corner or the end of a line is 0.5 m to 2.5 cells from another purple shape (or, for the end of
    // a line, from closed ground on the map) and there is open ground on both sides, the cells across the gap that only
    // the purple shapes closed are opened again. The AI course's barriers are areas that reach into the walls, so they
    // get no gaps from the map; the cells the map itself closes always stay closed.

    static int OpenGaps(float[,] speed, MapDefinition map, List<(List<Vector2> poly, bool line)> shapes, List<KeyValuePair<int, float>> changed)
    {
        if (shapes.Count == 0 || changed.Count == 0) return 0;
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float metres = Mathf.Max(0.5f, map.gridSize);
        // A line that just stops a little short of a house is not a gap: from the map it takes at least 1 m
        float minGap = 0.5f / metres, minMapGap = 1f / metres, maxGap = 2.5f;

        // The speed each purple cell had before (the first time it was closed)
        var before = new Dictionary<int, float>();
        foreach (var c in changed) if (!before.ContainsKey(c.Key)) before[c.Key] = c.Value;
        bool MapClosed(int x, int y) => x < 0 || y < 0 || x >= cols || y >= rows || (speed[y, x] <= 0.01f && !before.ContainsKey(y * cols + x));

        var gaps = new List<(Vector2 middle, Vector2 across)>();
        for (int s = 0; s < shapes.Count; s++)
        {
            var (poly, line) = shapes[s];
            for (int k = 0; k < poly.Count; k++)
            {
                Vector2 corner = poly[k];
                bool end = line && (k == 0 || k == poly.Count - 1);
                // The nearest point of another purple shape
                float best = float.MaxValue;
                Vector2 nearest = corner;
                for (int o = 0; o < shapes.Count; o++)
                {
                    if (o == s) continue;
                    var (other, otherLine) = shapes[o];
                    if (!otherLine && Inside(other, corner)) { best = 0f; break; }
                    int sides = otherLine ? other.Count - 1 : other.Count;
                    for (int i = 0; i < sides; i++)
                    {
                        Vector2 q = Nearest(corner, other[i], other[(i + 1) % other.Count]);
                        float d = Vector2.Distance(corner, q);
                        if (d < best) { best = d; nearest = q; }
                    }
                }
                // The end of a purple line: also closed ground on the map (a house, a wall)
                bool toMap = false;
                if (end && best > 0f)
                {
                    int cx = Mathf.FloorToInt(corner.x), cy = Mathf.FloorToInt(corner.y);
                    int r = Mathf.CeilToInt(maxGap) + 1;
                    for (int y = cy - r; y <= cy + r; y++)
                    {
                        for (int x = cx - r; x <= cx + r; x++)
                        {
                            if (!MapClosed(x, y)) continue;
                            Vector2 q = new Vector2(Mathf.Clamp(corner.x, x, x + 1f), Mathf.Clamp(corner.y, y, y + 1f));
                            float d = Vector2.Distance(corner, q);
                            if (d < best) { best = d; nearest = q; toMap = true; }
                        }
                    }
                }
                if (best < (toMap ? minMapGap : minGap) || best > maxGap) continue;
                Vector2 along = (nearest - corner) / best;
                Vector2 middle = (corner + nearest) / 2f;
                bool twice = false;
                foreach (var g in gaps) if (Vector2.Distance(g.middle, middle) < 0.5f) { twice = true; break; }
                if (!twice) gaps.Add((middle, new Vector2(-along.y, along.x)));
            }
        }

        int opened = 0;
        const float Reach = 1.6f;
        foreach (var (middle, across) in gaps)
        {
            if (!OpenAt(speed, middle + across * (Reach + 0.6f), before) && !OpenAt(speed, middle + across * (Reach + 1.2f), before)) continue;
            if (!OpenAt(speed, middle - across * (Reach + 0.6f), before) && !OpenAt(speed, middle - across * (Reach + 1.2f), before)) continue;
            int n = Mathf.CeilToInt(2f * Reach / 0.4f);
            Vector2Int last = new Vector2Int(int.MinValue, 0);
            for (int k = 0; k <= n; k++)
            {
                Vector2 p = middle + across * Mathf.Lerp(-Reach, Reach, k / (float)n);
                Vector2Int c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
                // A diagonal step goes through the cell beside it too (no corner-cutting in the pathfinding)
                if (last.x != int.MinValue && last.x != c.x && last.y != c.y) opened += Reopen(speed, last.x, c.y, before);
                opened += Reopen(speed, c.x, c.y, before);
                last = c;
            }
        }
        return opened;
    }

    static bool OpenAt(float[,] speed, Vector2 p, Dictionary<int, float> before)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        return x >= 0 && y >= 0 && x < cols && y < rows && speed[y, x] > 0.01f;
    }

    static int Reopen(float[,] speed, int x, int y, Dictionary<int, float> before)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        if (x < 0 || y < 0 || x >= cols || y >= rows || speed[y, x] > 0.01f) return 0;
        if (!before.TryGetValue(y * cols + x, out float old) || old <= 0.01f) return 0;
        speed[y, x] = old;
        return 1;
    }

    static Vector2 Nearest(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len = ab.sqrMagnitude;
        if (len < 1e-6f) return a;
        return a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
    }

    static bool Inside(List<Vector2> poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
        }
        return inside;
    }

    // A copy of the grid with the course's out-of-bounds areas closed, or the grid itself when the course has none
    public static MapGrid WithAreas(MapDefinition map, MapGrid grid, CourseData course)
    {
        if (grid == null || !Any(course)) return grid;
        MapGrid copy = new MapGrid { speed = (float[,])grid.speed.Clone(), slopes = grid.slopes };
        Block(copy.speed, map, course.forbidden);
        return copy;
    }

    // The cells (row * cols + column) the competition's purple areas and lines closed: the drawing is as strict there
    // as at a fence, whatever ground is under them (ManualPathDrawer)
    public static readonly HashSet<int> CompetitionCells = new HashSet<int>();

    // In the competition: close the areas of the course being run in the grid every runner uses
    public static void ApplyToCompetition(PathfindingManager manager)
    {
        CourseData course = CourseLoader.Loaded;
        MapDefinition map = CourseLoader.LoadedMap;
        if (manager == null || manager.speedGrid2D == null || map == null || !Any(course)) return;
        string gridName = manager.jsonFileName ?? "";
        if (gridName.EndsWith(".json")) gridName = gridName.Substring(0, gridName.Length - 5);
        if (gridName != map.gridName) return;   // a scene that runs another map
        CompetitionCells.Clear();
        int closed = Block(manager.speedGrid2D, map, course.forbidden, CompetitionCells);
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
