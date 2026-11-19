using System;
using System.Collections.Generic;
using System.Globalization;

// Finer heights for the run-yourself world, from the map's own contours (2026-11-19, Anders: in fairly flat forest
// everything looked flat; a knoll the mapper drew with a contour or a form line should stick up as it does in real
// life, but not higher than that). The map's height grid has 10 m cells, which rounds a small knoll away. Every small
// closed contour (a knoll or a hollow) is lifted (or sunk) until its top is about half a contour interval above the
// line: 2 m for a contour, 1 m for a form line. The rest of the ground stays as the grid has it. Plain C#: runs on a
// background thread.
public static class RunContours
{
    const int Fine = 4;              // the new grid has 4 x 4 cells for each cell of the old (2.5 m from 10 m)
    const float MaxLoop = 140f;      // larger closed contours are hills the grid already shows

    struct Loop
    {
        public float[] xy;
        public float rise;           // how far above the line the top should be
        public float minX, minY, maxX, maxY;
        public float Area => (maxX - minX) * (maxY - minY);
    }

    static string cachedFor;
    static TvHeights cached;

    // The grid with the knolls and hollows of the contours, or the grid itself when there are no contours
    public static TvHeights Detailed(string mapId, TvHeights grid, byte[] linesJson)
    {
        if (grid == null || grid.h == null || linesJson == null) return grid;
        lock (typeof(RunContours))
        {
            if (cachedFor == mapId && cached != null) return cached;
        }
        List<Loop> loops = ReadLoops(linesJson);
        if (loops.Count == 0) return grid;

        TvHeights fine = new TvHeights
        {
            cell = grid.cell / Fine, minx = grid.minx, miny = grid.miny,
            cols = (grid.cols - 1) * Fine + 1, rows = (grid.rows - 1) * Fine + 1
        };
        if ((long)fine.cols * fine.rows > 30000000) return grid;
        fine.h = new float[fine.cols * fine.rows];
        for (int r = 0; r < fine.rows; r++)
            for (int c = 0; c < fine.cols; c++)
                fine.h[r * fine.cols + c] = grid.At(fine.minx + c * fine.cell, fine.miny + r * fine.cell);

        // Outer rings first, so a ring inside another builds on it
        loops.Sort((a, b) => b.Area.CompareTo(a.Area));
        foreach (Loop loop in loops) Raise(fine, loop);

        lock (typeof(RunContours))
        {
            cachedFor = mapId;
            cached = fine;
        }
        return fine;
    }

    static void Raise(TvHeights g, Loop loop)
    {
        float[] p = loop.xy;
        int n = p.Length / 2;
        // The height of the line itself
        float level = 0f;
        for (int i = 0; i < n; i++) level += Sample(g, p[i * 2], p[i * 2 + 1]);
        level /= n;

        const float Skirt = 3f;
        int c0 = Math.Max(0, (int)Math.Floor((loop.minX - Skirt - g.minx) / g.cell));
        int c1 = Math.Min(g.cols - 1, (int)Math.Ceiling((loop.maxX + Skirt - g.minx) / g.cell));
        int r0 = Math.Max(0, (int)Math.Floor((loop.minY - Skirt - g.miny) / g.cell));
        int r1 = Math.Min(g.rows - 1, (int)Math.Ceiling((loop.maxY + Skirt - g.miny) / g.cell));
        if (c1 < c0 || r1 < r0) return;
        int w = c1 - c0 + 1, hgt = r1 - r0 + 1;
        float[] dist = new float[w * hgt];   // signed: inside > 0
        float deepest = 0f, top = float.MinValue, bottom = float.MaxValue, sum = 0f;
        int inside = 0;
        for (int r = 0; r < hgt; r++)
        {
            float y = g.miny + (r0 + r) * g.cell;
            for (int c = 0; c < w; c++)
            {
                float x = g.minx + (c0 + c) * g.cell;
                float d = Distance(p, x, y);
                bool inPoly = Inside(p, x, y);
                d = inPoly ? d : -d;
                dist[r * w + c] = d;
                if (!inPoly) continue;
                float h = g.h[(r0 + r) * g.cols + c0 + c];
                inside++;
                sum += h;
                if (h > top) top = h;
                if (h < bottom) bottom = h;
                if (d > deepest) deepest = d;
            }
        }
        if (inside == 0) return;
        // A hollow when the grid already goes down inside the ring; a knoll otherwise (the usual)
        bool hollow = sum / inside < level - 0.25f;
        float need = hollow ? (bottom - (level - loop.rise)) : (level + loop.rise - top);
        if (need <= 0.15f) return;
        need = Math.Min(need, loop.rise * 1.5f);
        if (hollow) need = -need;
        float reach = Math.Max(1.5f, deepest * 0.8f);
        for (int r = 0; r < hgt; r++)
            for (int c = 0; c < w; c++)
            {
                float d = dist[r * w + c];
                if (d <= -Skirt) continue;
                float t = Smooth(-Skirt, reach, d);
                g.h[(r0 + r) * g.cols + c0 + c] += need * t;
            }
    }

    static float Smooth(float a, float b, float x)
    {
        float t = (x - a) / (b - a);
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        return t * t * (3f - 2f * t);
    }

    static float Sample(TvHeights g, float x, float y)
    {
        int c = (int)Math.Round((x - g.minx) / g.cell), r = (int)Math.Round((y - g.miny) / g.cell);
        c = c < 0 ? 0 : c >= g.cols ? g.cols - 1 : c;
        r = r < 0 ? 0 : r >= g.rows ? g.rows - 1 : r;
        return g.h[r * g.cols + c];
    }

    static float Distance(float[] p, float x, float y)
    {
        float best = float.MaxValue;
        int n = p.Length / 2;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float ax = p[i * 2], ay = p[i * 2 + 1], bx = p[j * 2], by = p[j * 2 + 1];
            float dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
            float t = len < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, ((x - ax) * dx + (y - ay) * dy) / len));
            float ex = ax + dx * t - x, ey = ay + dy * t - y, d = ex * ex + ey * ey;
            if (d < best) best = d;
        }
        return (float)Math.Sqrt(best);
    }

    static bool Inside(float[] p, float x, float y)
    {
        bool inside = false;
        int n = p.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = p[i * 2], yi = p[i * 2 + 1], xj = p[j * 2], yj = p[j * 2 + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    // ---- lines.json: the small closed contours, index contours and form lines (ISOM 101, 102, 103) ----

    static List<Loop> ReadLoops(byte[] bytes)
    {
        var loops = new List<Loop>();
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        int at = 0;
        while (true)
        {
            at = text.IndexOf("\"Layer\":\"", at, StringComparison.Ordinal);
            if (at < 0) break;
            at += 9;
            if (at + 3 > text.Length) break;
            string code = text.Substring(at, 3);
            if (code != "101" && code != "102" && code != "103") continue;
            int coords = text.IndexOf("\"coordinates\":", at, StringComparison.Ordinal);
            int next = text.IndexOf("\"Layer\":\"", at, StringComparison.Ordinal);
            if (coords < 0 || (next >= 0 && coords > next)) continue;
            int end = text.IndexOf("]]", coords, StringComparison.Ordinal);
            if (end < 0) break;
            List<float> xy = Numbers(text, coords + 14, end);
            at = end;
            if (xy.Count < 8) continue;
            int n = xy.Count / 2;
            float fx = xy[0], fy = xy[1], lx = xy[(n - 1) * 2], ly = xy[(n - 1) * 2 + 1];
            if ((fx - lx) * (fx - lx) + (fy - ly) * (fy - ly) > 2.5f * 2.5f) continue;   // not a ring
            var loop = new Loop { xy = xy.ToArray(), rise = code == "103" ? 1f : 2f, minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue };
            for (int i = 0; i < n; i++)
            {
                loop.minX = Math.Min(loop.minX, xy[i * 2]); loop.maxX = Math.Max(loop.maxX, xy[i * 2]);
                loop.minY = Math.Min(loop.minY, xy[i * 2 + 1]); loop.maxY = Math.Max(loop.maxY, xy[i * 2 + 1]);
            }
            if (loop.maxX - loop.minX > MaxLoop || loop.maxY - loop.minY > MaxLoop) continue;
            if (loop.maxX - loop.minX < 1f || loop.maxY - loop.minY < 1f) continue;
            loops.Add(loop);
        }
        return loops;
    }

    static List<float> Numbers(string text, int from, int to)
    {
        var list = new List<float>();
        int i = from;
        while (i < to)
        {
            char ch = text[i];
            if (ch == '-' || ch == '.' || (ch >= '0' && ch <= '9'))
            {
                int s = i;
                while (i < to && (text[i] == '-' || text[i] == '.' || text[i] == 'e' || text[i] == 'E' || text[i] == '+' || (text[i] >= '0' && text[i] <= '9'))) i++;
                if (float.TryParse(text.Substring(s, i - s), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) list.Add(v);
            }
            else i++;
        }
        return list;
    }
}
