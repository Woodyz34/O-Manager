using System;
using System.Collections.Generic;
using MapImport;

// The border of a set of cells of the world layer as straightened lines in map metres (2026-11-20: the map's own
// out-of-bounds areas are taped in the run-yourself world, Anders: "allt sånt ska synas som snitslingar").
// The border is where a cell of the set meets an open cell (a cell of the set next to a house or water gets no tape
// there). The steps of the cell corners are straightened as the houses' outlines are (three quarters of a cell).
// Plain C#.
public static class TvOutlines
{
    public static List<List<(double e, double n)>> CellBorders(WorldLayer layer, int i0, int j0, int i1, int j1,
                                                                Func<int, int, bool> inside, Func<int, int, bool> open)
    {
        i0 = Math.Max(0, i0); j0 = Math.Max(0, j0);
        i1 = Math.Min(layer.cols - 1, i1); j1 = Math.Min(layer.rows - 1, j1);
        var result = new List<List<(double, double)>>();
        if (i1 < i0 || j1 < j0) return result;
        long W = layer.cols + 1;
        long Key(int i, int j) => j * W + i;
        var adj = new Dictionary<long, List<long>>();
        void Edge(long a, long b)
        {
            if (!adj.TryGetValue(a, out var la)) adj[a] = la = new List<long>(2);
            if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new List<long>(2);
            la.Add(b);
            lb.Add(a);
        }
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                if (!inside(i, j)) continue;
                if (i + 1 < layer.cols && open(i + 1, j)) Edge(Key(i + 1, j), Key(i + 1, j + 1));
                if (i > 0 && open(i - 1, j)) Edge(Key(i, j), Key(i, j + 1));
                if (j + 1 < layer.rows && open(i, j + 1)) Edge(Key(i, j + 1), Key(i + 1, j + 1));
                if (j > 0 && open(i, j - 1)) Edge(Key(i, j), Key(i + 1, j));
            }
        if (adj.Count == 0) return result;
        var used = new HashSet<(long, long)>();
        bool Take(long a, long b)
        {
            var k = a < b ? (a, b) : (b, a);
            return used.Add(k);
        }
        (double, double) At(long key) => (layer.minE + key % W * layer.cell, layer.minN + key / W * layer.cell);
        float tol = layer.cell * 0.76f;
        void Walk(long start, long next)
        {
            var pts = new List<(double e, double n)> { At(start) };
            long prev = start, at = next;
            Take(prev, at);
            while (true)
            {
                pts.Add(At(at));
                var list = adj[at];
                if (list.Count != 2 || at == start) break;
                long go = list[0] == prev ? list[1] : list[0];
                if (!Take(at, go)) break;
                prev = at;
                at = go;
            }
            if (pts.Count >= 2) result.Add(Straighten(pts, tol));
        }
        // From the ends and the crossings first, then the loops
        foreach (var kv in adj)
            if (kv.Value.Count != 2)
                foreach (long b in kv.Value)
                    if (!used.Contains(kv.Key < b ? (kv.Key, b) : (b, kv.Key))) Walk(kv.Key, b);
        foreach (var kv in adj)
            foreach (long b in kv.Value)
                if (!used.Contains(kv.Key < b ? (kv.Key, b) : (b, kv.Key))) Walk(kv.Key, b);
        return result;
    }

    static List<(double e, double n)> Straighten(List<(double e, double n)> pts, float tol)
    {
        if (pts.Count <= 2) return pts;
        bool loop = pts[0].e == pts[pts.Count - 1].e && pts[0].n == pts[pts.Count - 1].n;
        var keep = new bool[pts.Count];
        keep[0] = keep[pts.Count - 1] = true;
        if (loop)
        {
            int far = 0;
            double best = -1;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                double d = (pts[i].e - pts[0].e) * (pts[i].e - pts[0].e) + (pts[i].n - pts[0].n) * (pts[i].n - pts[0].n);
                if (d > best) { best = d; far = i; }
            }
            keep[far] = true;
            Dp(pts, 0, far, keep, tol);
            Dp(pts, far, pts.Count - 1, keep, tol);
        }
        else Dp(pts, 0, pts.Count - 1, keep, tol);
        var r = new List<(double, double)>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) r.Add(pts[i]);
        return r;
    }

    static void Dp(List<(double e, double n)> p, int a, int b, bool[] keep, double tol)
    {
        if (b <= a + 1) return;
        double best = -1;
        int at = -1;
        double ax = p[a].e, az = p[a].n, dx = p[b].e - ax, dz = p[b].n - az, len2 = dx * dx + dz * dz;
        for (int i = a + 1; i < b; i++)
        {
            double t = len2 > 1e-9 ? Math.Max(0, Math.Min(1, ((p[i].e - ax) * dx + (p[i].n - az) * dz) / len2)) : 0;
            double qx = ax + dx * t - p[i].e, qz = az + dz * t - p[i].n, d = Math.Sqrt(qx * qx + qz * qz);
            if (d > best) { best = d; at = i; }
        }
        if (best <= tol) return;
        keep[at] = true;
        Dp(p, a, at, keep, tol);
        Dp(p, at, b, keep, tol);
    }
}
