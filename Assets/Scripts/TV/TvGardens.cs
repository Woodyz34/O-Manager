using System;
using System.Collections.Generic;
using MapImport;

// The gardens (olive green) and out-of-bounds areas of a sprint map as the map draws them, straight lines and all
// (2026-11-23, Anders: "Om det är raka streck i kartan så ska det vara rakt i verkligheten" - the gardens were the
// zigzag of the 2 m cells). Read from the world layer's outlines (WorldLayer.areas, maps built from 2026-11-23 on);
// a map built before has none, and the town falls back on the straightened borders of its cells (TvOutlines).
//  - Inside(e, n): in a garden (or out-of-bounds area), the way the map draws it;
//  - Near(e, n, d): an outline is within d metres;
//  - Segments(...): the outline pieces in a box, each with the area on its left.
// Plain C#: made on a background thread with the town, read from any thread afterwards.
public class TvGardens
{
    public readonly List<WorldArea> rings = new List<WorldArea>();
    const float Bucket = 16f;
    readonly double minE, minN;
    readonly int cols, rows;
    readonly List<int>[] buckets;

    TvGardens(WorldLayer layer, List<WorldArea> keep)
    {
        rings = keep;
        minE = layer.minE;
        minN = layer.minN;
        cols = Math.Max(1, (int)Math.Ceiling(layer.cols * layer.cell / Bucket) + 1);
        rows = Math.Max(1, (int)Math.Ceiling(layer.rows * layer.cell / Bucket) + 1);
        buckets = new List<int>[cols * rows];
        for (int r = 0; r < rings.Count; r++)
        {
            WorldArea a = rings[r];
            // Each ring goes in every bucket one of its sides passes through, and an outline's inside buckets get
            // it too (for Inside): the box is enough
            int i0 = Col(a.minE - 1), i1 = Col(a.maxE + 1), j0 = Row(a.minN - 1), j1 = Row(a.maxN + 1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    (buckets[j * cols + i] ?? (buckets[j * cols + i] = new List<int>())).Add(r);
        }
    }

    int Col(double e) => Math.Max(0, Math.Min(cols - 1, (int)Math.Floor((e - minE) / Bucket)));
    int Row(double n) => Math.Max(0, Math.Min(rows - 1, (int)Math.Floor((n - minN) / Bucket)));

    // null when the layer keeps no outlines (built before 2026-11-23) or has no gardens
    public static TvGardens Make(WorldLayer layer)
    {
        if (layer == null || layer.areas == null || layer.areas.Count == 0) return null;
        var keep = new List<WorldArea>();
        foreach (WorldArea a in layer.areas)
            if (a != null && a.xy != null && a.xy.Length >= 6) keep.Add(a);
        return keep.Count == 0 ? null : new TvGardens(layer, keep);
    }

    public bool HasGardens
    {
        get
        {
            foreach (WorldArea a in rings) if (a.kind == WorldAreaKind.Garden) return true;
            return false;
        }
    }

    // In an area of this kind (None: of either kind): inside more of their outlines than of their holes
    public bool Inside(double e, double n, WorldAreaKind kind = WorldAreaKind.None)
    {
        List<int> list = buckets[Row(n) * cols + Col(e)];
        if (list == null) return false;
        int count = 0;
        float x = (float)e, z = (float)n;
        foreach (int r in list)
        {
            WorldArea a = rings[r];
            if (kind != WorldAreaKind.None && a.kind != kind) continue;
            if (x < a.minE || x > a.maxE || z < a.minN || z > a.maxN) continue;
            if (In(a.xy, x, z)) count += a.hole ? -1 : 1;
        }
        return count > 0;
    }

    static bool In(float[] xy, float x, float z)
    {
        bool inside = false;
        int n = xy.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            float xi = xy[2 * i], zi = xy[2 * i + 1], xj = xy[2 * j], zj = xy[2 * j + 1];
            if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
        }
        return inside;
    }

    // Metres to the nearest outline (of either kind), up to the given distance (more: that distance)
    public float EdgeDist(double e, double n, float within)
    {
        float best = within, x = (float)e, z = (float)n;
        int i0 = Col(e - within), i1 = Col(e + within), j0 = Row(n - within), j1 = Row(n + within);
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                List<int> list = buckets[j * cols + i];
                if (list == null) continue;
                foreach (int r in list)
                {
                    WorldArea a = rings[r];
                    if (x < a.minE - best || x > a.maxE + best || z < a.minN - best || z > a.maxN + best) continue;
                    float[] xy = a.xy;
                    int c = xy.Length / 2;
                    for (int k = 0; k < c; k++)
                    {
                        int q = (k + 1) % c;
                        float d = Seg(x, z, xy[2 * k], xy[2 * k + 1], xy[2 * q], xy[2 * q + 1]);
                        if (d < best) best = d;
                    }
                }
            }
        return best;
    }

    public bool Near(double e, double n, float d) => EdgeDist(e, n, d) < d;

    static float Seg(float px, float pz, float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
        float t = l2 > 1e-8f ? Math.Max(0f, Math.Min(1f, ((px - ax) * dx + (pz - az) * dz) / l2)) : 0f;
        float qx = ax + dx * t - px, qz = az + dz * t - pz;
        return (float)Math.Sqrt(qx * qx + qz * qz);
    }

    // The outlines of the areas of a kind that come into a box, as polylines with the area on their left
    // (map metres, closed rings repeat their first point at the end)
    public List<float[]> Outlines(double e0, double n0, double e1, double n1, WorldAreaKind kind)
    {
        var result = new List<float[]>();
        foreach (WorldArea a in rings)
        {
            if (a.kind != kind) continue;
            if (a.maxE < e0 || a.minE > e1 || a.maxN < n0 || a.minN > n1) continue;
            int c = a.xy.Length / 2;
            // Turning left round an outline (anticlockwise), right round a hole: the area is on the left either way
            float area2 = 0f;
            for (int k = 0; k < c; k++)
            {
                int q = (k + 1) % c;
                area2 += a.xy[2 * k] * a.xy[2 * q + 1] - a.xy[2 * q] * a.xy[2 * k + 1];
            }
            bool forward = (area2 > 0f) != a.hole;
            var pts = new float[(c + 1) * 2];
            for (int k = 0; k <= c; k++)
            {
                int src = forward ? k % c : (c - k) % c;
                pts[2 * k] = a.xy[2 * src];
                pts[2 * k + 1] = a.xy[2 * src + 1];
            }
            result.Add(pts);
        }
        return result;
    }
}
