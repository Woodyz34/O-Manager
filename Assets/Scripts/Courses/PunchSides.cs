using System;
using System.Collections.Generic;
using UnityEngine;

// Which side of a fence, a wall or an out-of-bounds area a control is punched from (2026-11-20).
//
// A control put right at a fence (in the corner of a fenced garden, on a wall, at the top of a cliff) could be reached
// from both sides, and which side the runners went to depended on how each player drew the line. When a control is
// put that close, the course builder asks the course setter "inside or outside?" and keeps the answer with the course
// (CourseData.punchSides): a point on the chosen side, in whole metres. It goes to the lobby with the course, so every
// player's runners head for exactly the same cell (PathfindingManager.MoveControlsOntoRunnableGround), and the 3D
// game mode puts the flag on that side.
[Serializable]
public class PunchSide
{
    public CoursePoint control;   // the control, as it is on the course (whole metres)
    public CoursePoint at;        // a point on the chosen side, on ground a runner can stand on (whole metres)
}

public static class PunchSides
{
    public const int MaxSides = CourseData.MaxControls + 10;   // every control and the finish, with room for relays

    public struct Option
    {
        public CoursePoint at;
        public string title;
        public string description;
    }

    static bool Same(CoursePoint a, CoursePoint b) => Math.Abs(a.e - b.e) < 1.5 && Math.Abs(a.n - b.n) < 1.5;

    // The side chosen for a control, if one was
    public static bool TryGet(CourseData course, CoursePoint control, out CoursePoint at)
    {
        at = control;
        if (course == null || course.punchSides == null) return false;
        foreach (PunchSide side in course.punchSides)
        {
            if (side != null && Same(side.control, control))
            {
                at = side.at;
                return true;
            }
        }
        return false;
    }

    // Where the 3D flag of a control stands: at the control, or, for a control with a chosen side, moved onto that
    // side (at most 6 m: the point on the side is the middle of the nearest grid cell there)
    public static CoursePoint FlagPoint(CourseData course, CoursePoint control)
    {
        if (!TryGet(course, control, out CoursePoint at)) return control;
        double de = at.e - control.e, dn = at.n - control.n, d = Math.Sqrt(de * de + dn * dn);
        if (d <= 6.0) return at;
        return new CoursePoint(control.e + de / d * 6.0, control.n + dn / d * 6.0);
    }

    // The sides a control at point could be punched from: two or more when it is right at something nobody crosses
    // (the course setter is asked), otherwise none or one. speed: the map's grid with the course's out-of-bounds areas.
    public static List<Option> Options(MapDefinition map, float[,] speed, CoursePoint point)
    {
        var options = new List<Option>();
        if (map == null || speed == null) return options;
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        Vector2Int c = map.ToCell(point);
        bool Open(int x, int y) => x >= 0 && y >= 0 && x < cols && y < rows && speed[y, x] > 0.01f;

        // The open cells round the control, joined the way A* joins them (diagonals only past two open cells)
        const int Window = 8;
        int size = 2 * Window + 1;
        int[] label = new int[size * size];
        int labels = 0;
        var stack = new Stack<int>();
        for (int k = 0; k < label.Length; k++)
        {
            int x = c.x - Window + k % size, y = c.y - Window + k / size;
            if (label[k] != 0 || !Open(x, y)) continue;
            labels++;
            label[k] = labels;
            stack.Push(k);
            while (stack.Count > 0)
            {
                int q = stack.Pop();
                int qx = q % size, qy = q / size;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = qx + dx, ny = qy + dy;
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                        int nk = ny * size + nx;
                        if (label[nk] != 0) continue;
                        int gx = c.x - Window + nx, gy = c.y - Window + ny;
                        if (!Open(gx, gy)) continue;
                        if (dx != 0 && dy != 0 && (!Open(c.x - Window + qx + dx, c.y - Window + qy) || !Open(c.x - Window + qx, c.y - Window + qy + dy))) continue;
                        label[nk] = labels;
                        stack.Push(nk);
                    }
            }
        }
        if (labels < 2) return options;

        // Each part that comes within 1.5 cells of the control: its nearest cell to the control
        var nearest = new Dictionary<int, (int x, int y, float d)>();
        for (int k = 0; k < label.Length; k++)
        {
            if (label[k] == 0) continue;
            int dx = k % size - Window, dy = k / size - Window;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > 1.5f) continue;
            if (!nearest.TryGetValue(label[k], out var best) || d < best.d || (d == best.d && k < best.y * size + best.x))
                nearest[label[k]] = (k % size, k / size, d);
        }
        if (nearest.Count < 2) return options;

        // Inside or outside: a part that is shut in (a garden, a yard) has few cells; the other runs on and on
        const int Cap = 2500;
        var parts = new List<(int label, Vector2Int cell, bool enclosed, Vector2 towards)>();
        var labelKeys = new List<int>(nearest.Keys);
        labelKeys.Sort();
        foreach (int l in labelKeys)
        {
            var n = nearest[l];
            var cell = new Vector2Int(c.x - Window + n.x, c.y - Window + n.y);
            int count = Reach(speed, cell, Cap);
            // Which way the part lies from the control, from its cells within three cells of it
            Vector2 sum = Vector2.zero;
            for (int k = 0; k < label.Length; k++)
            {
                if (label[k] != l) continue;
                int dx = k % size - Window, dy = k / size - Window;
                if (dx * dx + dy * dy <= 9) sum += new Vector2(dx, dy);
            }
            parts.Add((l, cell, count < Cap, sum));
        }

        int enclosedCount = 0;
        foreach (var p in parts) if (p.enclosed) enclosedCount++;
        bool insideOutside = parts.Count == 2 && enclosedCount == 1;
        foreach (var p in parts)
        {
            if (!PointIn(map, p.cell, out CoursePoint at)) continue;
            string direction = Direction(p.towards);
            string title, description;
            if (insideOutside)
            {
                title = p.enclosed ? "Inside" : "Outside";
                description = (p.enclosed ? "In the enclosed part" : "Out on the open side") + (direction.Length > 0 ? " (" + direction + " of the control)" : "")
                              + ". Every runner punches it from there.";
            }
            else
            {
                title = direction.Length > 0 ? char.ToUpper(direction[0]) + direction.Substring(1) + " side" : "This side";
                description = "The " + direction + " side of the fence, wall, cliff or out-of-bounds edge. Every runner punches it from there.";
            }
            options.Add(new Option { at = at, title = title, description = description });
        }
        if (options.Count > 4) options.RemoveRange(4, options.Count - 4);
        return options.Count >= 2 ? options : new List<Option>();
    }

    // How many open cells can be reached from cell, up to cap
    static int Reach(float[,] speed, Vector2Int start, int cap)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        var seen = new HashSet<int> { start.y * cols + start.x };
        var queue = new Queue<int>();
        queue.Enqueue(start.y * cols + start.x);
        while (queue.Count > 0 && seen.Count < cap)
        {
            int q = queue.Dequeue();
            int x = q % cols, y = q / cols;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || speed[ny, nx] <= 0.01f) continue;
                if (seen.Add(ny * cols + nx)) queue.Enqueue(ny * cols + nx);
            }
        }
        return seen.Count;
    }

    // The middle of a cell as a course point in whole metres (what the lobby sends), still inside that cell
    static bool PointIn(MapDefinition map, Vector2Int cell, out CoursePoint at)
    {
        float size = map.CellSize;
        Vector3 world = new Vector3((cell.x + 0.5f) * size - map.cols * size / 2f, (cell.y + 0.5f) * size - map.rows * size / 2f, -1f);
        CoursePoint exact = map.FromWorld(world);
        at = new CoursePoint(Math.Round(exact.e), Math.Round(exact.n));
        if (map.ToCell(at) == cell) return true;
        // A cell smaller than a metre or two: try the whole metres round the middle
        for (int de = -1; de <= 1; de++)
            for (int dn = -1; dn <= 1; dn++)
            {
                at = new CoursePoint(Math.Round(exact.e) + de, Math.Round(exact.n) + dn);
                if (map.ToCell(at) == cell) return true;
            }
        return false;
    }

    // "north-east" for a vector with y to the north; empty when it is too short to tell
    static string Direction(Vector2 v)
    {
        if (v.sqrMagnitude < 0.01f) return "";
        float angle = Mathf.Repeat(Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg, 360f);
        string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        return names[Mathf.RoundToInt(angle / 45f) % 8];
    }
}
