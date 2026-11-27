using System;
using System.Collections.Generic;

// Finer heights for the run-yourself world (2026-11-19, Anders: the world should be identical to the OCAD file,
// every contour seen). RunContourField makes the ground exactly from the contours and form lines on a 2.5 m grid;
// inside the innermost closed rings, where the contours leave the ground flat, the top of a knoll is lifted about
// 2 m above a contour ring and 1 m above a form line ring (a hollow is sunk the same). When the contours can't be
// solved the 10 m grid is used, with the same rings added. Plain C#: runs on a background thread.
public static class RunContours
{
    const int Fine = 4;              // the new grid has 4 x 4 cells for each cell of the old (2.5 m from 10 m)
    const float MaxLoop = 250f;      // larger rings are left as the contours make them

    struct Loop
    {
        public float[] xy;
        public float rise;           // how far above the line the top should be
        public int hollow;           // 1 a hollow, -1 a knoll, 0 unknown (decided from the ground)
        public float level;          // the line's own height when known, else NaN
        public float minX, minY, maxX, maxY;
        public float Area => (maxX - minX) * (maxY - minY);
    }

    static string cachedFor;
    static TvHeights cached;

    // The ground made exactly from the contours (RunContourField), with the tops of knolls and the bottoms of hollows
    // added inside the innermost rings; the 10 m grid with the rings added when the contours can't be solved
    // lakeAt: whether a place (game metres) is open water (lakes, ponds), from the map's 3D world layer; null: not known
    public static TvHeights Detailed(string mapId, TvHeights grid, byte[] linesJson, byte[] pointsJson, float interval, System.Action<string> log = null, string cacheFile = null,
                                     Func<double, double, bool> lakeAt = null)
    {
        if (grid == null || grid.h == null || linesJson == null) return grid;
        lock (typeof(RunContours))
        {
            if (cachedFor == mapId && cached != null) return cached;
        }
        TvHeights saved = Load(cacheFile);
        if (saved != null)
        {
            lock (typeof(RunContours)) { cachedFor = mapId; cached = saved; }
            return saved;
        }
        TvHeights fine = Make(grid, linesJson, pointsJson, interval, log, lakeAt, out bool exact);
        if (fine == null) return grid;
        lock (typeof(RunContours))
        {
            cachedFor = mapId;
            cached = fine;
        }
        if (exact) Save(cacheFile, fine);
        return fine;
    }

    // ---- Made once with the map (2026-11-25, Anders: Fossum took 7 minutes on every computer, the first race) ----
    // The import (and every rebuild of a map: Edit map, a shared map downloaded, the built-in maps) works the ground out
    // here and keeps it in the map package as runheights.bytes, so a race only reads it. The file says which version of
    // this code made it and how long the lines and points it was made from were, so a stale one is never used.

    public const int Version = 6;          // raise when the ground comes out differently (the old files are then remade)
                                           // 5 (2026-11-27): only real slope ticks, which way is up solved for the whole map at once
                                           // 6 (2026-11-27): index contours on one level over the whole map, lakes flat, half-index
                                           // contours joined, contour pieces with no known slope left out
    const int PackageMagic = 0x32485552;   // "RUH2"

    // The ground for a map package (gzipped), or null when the map has no heights or the contours can't be solved
    public static byte[] Bake(TvHeights grid, byte[] linesJson, byte[] pointsJson, float interval, Action<string> log = null,
                              Func<double, double, bool> lakeAt = null)
    {
        if (grid == null || grid.h == null || linesJson == null) return null;
        TvHeights fine = Make(grid, linesJson, pointsJson, interval, log, lakeAt, out bool exact);
        if (fine == null || !exact) return null;
        byte[] body = ToBytes(fine);
        if (body == null) return null;
        var raw = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(raw, System.Text.Encoding.UTF8, true))
        {
            w.Write(PackageMagic);
            w.Write(Version);
            w.Write(Size(linesJson));
            w.Write(Size(pointsJson));
            w.Write(body);
        }
        var zipped = new System.IO.MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(zipped, System.IO.Compression.CompressionLevel.Optimal, true))
            gz.Write(raw.GetBuffer(), 0, (int)raw.Length);
        return zipped.ToArray();
    }

    // The ground kept in the map package, or null when there is none, or it was made by another version of this code or
    // from other lines (bytes already unzipped, as MapPackages.LoadBytes gives them)
    public static TvHeights FromPackage(string mapId, byte[] bytes, byte[] linesJson, byte[] pointsJson)
    {
        if (bytes == null || bytes.Length < 16 || linesJson == null) return null;
        try
        {
            if (BitConverter.ToInt32(bytes, 0) != PackageMagic || BitConverter.ToInt32(bytes, 4) != Version) return null;
            if (BitConverter.ToInt32(bytes, 8) != Size(linesJson) || BitConverter.ToInt32(bytes, 12) != Size(pointsJson)) return null;
            TvHeights g;
            using (var r = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes, 16, bytes.Length - 16))) g = Read(r);
            if (g == null) return null;
            lock (typeof(RunContours)) { cachedFor = mapId; cached = g; }
            return g;
        }
        catch (Exception) { return null; }
    }

    // A file's length without carriage returns, so a checkout that turned the line ends into CR LF still matches
    static int Size(byte[] b)
    {
        if (b == null) return 0;
        int n = 0;
        foreach (byte x in b) if (x != 13) n++;
        return n;
    }

    // The fine ground: exact (from the contours) or the 10 m grid made finer when they can't be solved; null on failure
    static TvHeights Make(TvHeights grid, byte[] linesJson, byte[] pointsJson, float interval, Action<string> log,
                          Func<double, double, bool> lakeAt, out bool exact)
    {
        exact = false;
        float eq = interval > 0.5f ? interval : 5f;
        List<RunContourField.Line> lines = RunContourField.ReadLines(linesJson);
        TvHeights fine = null;
        try { fine = RunContourField.Build(grid, lines, RunContourField.ReadTicks(pointsJson), eq, log, RunContourField.ReadWater(linesJson, pointsJson),
                                                 lakeAt); }
        catch (Exception e) { log?.Invoke("contour field failed: " + e.Message); }
        exact = fine != null;
        if (!exact) fine = Upsampled(grid);
        if (fine == null) return null;

        var loops = new List<Loop>();
        foreach (RunContourField.Line l in lines)
        {
            if (!l.closed) continue;
            var loop = new Loop { xy = ToFloats(l.xy), rise = l.formLine ? eq * 0.2f : eq * 0.4f, hollow = exact ? (l.hollow ? 1 : -1) : 0,
                                  level = exact && l.solved ? (float)l.level : float.NaN,
                                  minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue };
            for (int i = 0; i < loop.xy.Length; i += 2)
            {
                loop.minX = Math.Min(loop.minX, loop.xy[i]); loop.maxX = Math.Max(loop.maxX, loop.xy[i]);
                loop.minY = Math.Min(loop.minY, loop.xy[i + 1]); loop.maxY = Math.Max(loop.maxY, loop.xy[i + 1]);
            }
            if (loop.maxX - loop.minX > MaxLoop || loop.maxY - loop.minY > MaxLoop) continue;
            if (loop.maxX - loop.minX < 1f || loop.maxY - loop.minY < 1f) continue;
            loops.Add(loop);
        }
        // Outer rings first, so a ring inside another builds on it
        loops.Sort((a, b) => b.Area.CompareTo(a.Area));
        foreach (Loop loop in loops) Raise(fine, loop, exact ? 0f : 3f);
        return fine;
    }

    // ---- Kept on disk, so the next race on the map starts at once (heights to the centimetre) ----

    const int Magic = 0x48525552;   // "RURH"

    static void Save(string file, TvHeights g)
    {
        if (string.IsNullOrEmpty(file)) return;
        try
        {
            byte[] body = ToBytes(g);
            if (body == null) return;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
            System.IO.File.WriteAllBytes(file, body);
        }
        catch (Exception) { }
    }

    // Heights to the centimetre, two bytes each; null when the map rises more than 600 m (two bytes can't hold it)
    static byte[] ToBytes(TvHeights g)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in g.h) { if (v < min) min = v; if (v > max) max = v; }
        if (max - min > 600f) return null;
        var stream = new System.IO.MemoryStream(28 + g.h.Length * 2);
        using (var w = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            w.Write(Magic); w.Write(g.cell); w.Write(g.minx); w.Write(g.miny); w.Write(g.rows); w.Write(g.cols); w.Write(min);
            var bytes = new byte[g.h.Length * 2];
            for (int i = 0; i < g.h.Length; i++)
            {
                int q = (int)Math.Round((g.h[i] - min) * 100f);
                bytes[i * 2] = (byte)q; bytes[i * 2 + 1] = (byte)(q >> 8);
            }
            w.Write(bytes);
        }
        return stream.ToArray();
    }

    static TvHeights Load(string file)
    {
        if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file)) return null;
        try
        {
            using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(file))) return Read(r);
        }
        catch (Exception) { return null; }
    }

    static TvHeights Read(System.IO.BinaryReader r)
    {
        if (r.ReadInt32() != Magic) return null;
        var g = new TvHeights { cell = r.ReadSingle(), minx = r.ReadSingle(), miny = r.ReadSingle(), rows = r.ReadInt32(), cols = r.ReadInt32() };
        float min = r.ReadSingle();
        if (g.rows <= 0 || g.cols <= 0 || (long)g.rows * g.cols > 40000000) return null;
        byte[] bytes = r.ReadBytes(g.rows * g.cols * 2);
        if (bytes.Length != g.rows * g.cols * 2) return null;
        g.h = new float[g.rows * g.cols];
        for (int i = 0; i < g.h.Length; i++) g.h[i] = min + (bytes[i * 2] | bytes[i * 2 + 1] << 8) * 0.01f;
        return g;
    }

    static float[] ToFloats(double[] d)
    {
        var f = new float[d.Length];
        for (int i = 0; i < d.Length; i++) f[i] = (float)d[i];
        return f;
    }

    static TvHeights Upsampled(TvHeights grid)
    {
        TvHeights fine = new TvHeights
        {
            cell = grid.cell / Fine, minx = grid.minx, miny = grid.miny,
            cols = (grid.cols - 1) * Fine + 1, rows = (grid.rows - 1) * Fine + 1
        };
        if ((long)fine.cols * fine.rows > 30000000) return null;
        fine.h = new float[fine.cols * fine.rows];
        for (int r = 0; r < fine.rows; r++)
            for (int c = 0; c < fine.cols; c++)
                fine.h[r * fine.cols + c] = grid.At(fine.minx + c * fine.cell, fine.miny + r * fine.cell);
        return fine;
    }

    static void Raise(TvHeights g, Loop loop, float Skirt)
    {
        float[] p = loop.xy;
        int n = p.Length / 2;
        // The height of the line itself
        float level = loop.level;
        if (float.IsNaN(level))
        {
            level = 0f;
            for (int i = 0; i < n; i++) level += Sample(g, p[i * 2], p[i * 2 + 1]);
            level /= n;
        }

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
        bool hollow = loop.hollow != 0 ? loop.hollow > 0 : sum / inside < level - 0.25f;
        float need = hollow ? (bottom - (level - loop.rise)) : (level + loop.rise - top);
        if (need <= 0.15f) return;
        need = Math.Min(need, loop.rise * 1.5f);
        if (hollow) need = -need;
        float reach = Math.Max(1.5f, deepest * 0.8f);
        for (int r = 0; r < hgt; r++)
            for (int c = 0; c < w; c++)
            {
                float d = dist[r * w + c];
                if (d <= -Skirt || (Skirt <= 0f && d <= 0f)) continue;
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
}
