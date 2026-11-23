using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MapImport;

// Bridges the map does not draw as bridges (2026-11-23, Anders: "if a path goes over water there should be some kind
// of bridge, a wooden footbridge for a path, an asphalt bridge for a paved way, in the forest too").
//  - A path or road drawn straight over a lake or pond. The world layer's 2 m cells often keep the path's own strip
//    as land (Tranås: a paved path between two high fences over the lake was a zigzag strip of land between water
//    cells), so a point counts as over water when its cell is water, or the cells just beyond the path on both
//    sides are.
//  - A path crossing a stream (forest maps): a short footbridge over it.
//  - The bridge lines of a forest map (ISOM 512), which had no 3D shape at all.
// Worked out once per map (cached by its world layer), plain C#, so every piece of the world agrees.
public static class TvBridges
{
    public const int Wood = 1;          // planks (a path), not concrete and asphalt (a paved way or road)
    public const int Fenced = 2;        // fences or walls on both sides already: no railings of its own
    public const int OverWater = 4;     // made from a path over water: the land strip under it is water in 3D

    public class Span
    {
        public float[] xy;              // e0, n0, e1, n1, ... in the game's coordinates
        public float width;             // metres across the deck
        public int flags;
    }

    public struct PathLine
    {
        public float[] xy;
        public float width;             // metres across the path
        public bool paved;
    }

    static bool IsWater(WorldLayer layer, float x, float z) => (layer.At(x, z) & WorldCell.BaseMask) == WorldCell.Water;

    // Over water: the cell itself, or the cells just beyond the path on both sides (a narrow land strip under it)
    static bool Wet(WorldLayer layer, float x, float z, float nx, float nz, float half, bool sidesOnly = false)
    {
        if (!sidesOnly && IsWater(layer, x, z)) return true;
        float s = half + 1.6f, s2 = s + 1.4f;
        return (IsWater(layer, x + nx * s, z + nz * s) || IsWater(layer, x + nx * s2, z + nz * s2))
               && (IsWater(layer, x - nx * s, z - nz * s) || IsWater(layer, x - nx * s2, z - nz * s2));
    }

    // The stretches of the paths that run over water (at least 3 m, land at both ends), each with a metre of land at
    // either end so the deck meets the ground. Skipped where a bridge in known already stands (within 8 m).
    public static List<Span> OverWaterSpans(WorldLayer layer, List<PathLine> paths, List<float[]> known)
    {
        var found = new List<Span>();
        const float Step = 1f;
        foreach (PathLine path in paths)
        {
            float[] xy = path.xy;
            float half = Math.Max(0.5f, path.width / 2f);
            var pts = new List<float>();
            var wet = new List<bool>();
            for (int i = 0; i + 3 < xy.Length; i += 2)
            {
                float ax = xy[i], az = xy[i + 1], bx = xy[i + 2], bz = xy[i + 3];
                float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                if (len < 1e-3f) continue;
                float nx = -(bz - az) / len, nz = (bx - ax) / len;
                int k = Math.Max(1, (int)Math.Ceiling(len / Step));
                for (int j = (pts.Count == 0 ? 0 : 1); j <= k; j++)
                {
                    float t = j / (float)k, x = ax + (bx - ax) * t, z = az + (bz - az) * t;
                    pts.Add(x); pts.Add(z);
                    wet.Add(Wet(layer, x, z, nx, nz, half));
                }
            }
            int at = 0;
            while (at < wet.Count)
            {
                if (!wet[at]) { at++; continue; }
                int first = at;
                while (at < wet.Count && wet[at]) at++;
                int last = at - 1;
                if (first == 0 || at >= wet.Count || last - first + 1 < 3) continue;
                int from = first - 1, to = at;
                float mx = (pts[2 * from] + pts[2 * to]) / 2f, mz = (pts[2 * from + 1] + pts[2 * to + 1]) / 2f;
                if (Near(known, mx, mz, 8f) || NearSpans(found, mx, mz, 8f)) continue;
                int a0 = Math.Max(0, from - 1), a1 = Math.Min(wet.Count - 1, to + 1);
                var deck = Straight(pts, a0, a1);
                var span = new Span { xy = deck, width = Math.Max(1.2f, path.width), flags = OverWater | (path.paved ? 0 : Wood) };
                Flanks(layer, span);
                found.Add(span);
            }
        }
        return found;
    }

    // Two fences or walls running side by side over water, 1-6 m apart, with no path drawn between them (Tranås by
    // the start, 2026-11-23: a paved area between two high fences over the river). The deck runs half way between
    // them, out to land at both ends.
    public static List<Span> FencedOverWater(WorldLayer layer, List<float[]> known)
    {
        var found = new List<Span>();
        var segs = new List<float[]>();
        foreach (WorldLine l in layer.lines)
        {
            switch (l.kind)
            {
                case WorldLineKind.Fence: case WorldLineKind.HighFence: case WorldLineKind.Wall: case WorldLineKind.HighWall:
                case WorldLineKind.Barrier:
                    break;
                default:
                    continue;
            }
            // The fifth number: 1 for an uncrossable water's edge (a narrow strip of land between two of them is a
            // footbridge the map draws too thin to see, ISSprOM 2019 draws small footbridges like that)
            float edge = l.kind == WorldLineKind.Barrier ? 1f : 0f;
            for (int i = 0; i + 3 < l.xy.Length; i += 2)
            {
                float sx = l.xy[i + 2] - l.xy[i], sz = l.xy[i + 3] - l.xy[i + 1];
                if (sx * sx + sz * sz >= 16f) segs.Add(new[] { l.xy[i], l.xy[i + 1], l.xy[i + 2], l.xy[i + 3], edge });
            }
        }
        for (int a = 0; a < segs.Count; a++)
        {
            float[] A = segs[a];
            float ax = A[0], az = A[1], adx = A[2] - ax, adz = A[3] - az, al = (float)Math.Sqrt(adx * adx + adz * adz);
            float ux = adx / al, uz = adz / al, nx = -uz, nz = ux;
            for (int b = a + 1; b < segs.Count; b++)
            {
                float[] B = segs[b];
                float bdx = B[2] - B[0], bdz = B[3] - B[1], bl = (float)Math.Sqrt(bdx * bdx + bdz * bdz);
                if (A[4] != B[4] || Math.Abs((bdx * ux + bdz * uz) / bl) < 0.97f) continue;
                bool shores = A[4] > 0f;
                float c0 = (B[0] - ax) * nx + (B[1] - az) * nz, c1 = (B[2] - ax) * nx + (B[3] - az) * nz;
                float gap = (c0 + c1) / 2f;
                if (Math.Abs(gap) < (shores ? 0.3f : 1f) || Math.Abs(gap) > (shores ? 3f : 6f) || Math.Abs(c0 - c1) > 1f) continue;
                float t0 = (B[0] - ax) * ux + (B[1] - az) * uz, t1 = (B[2] - ax) * ux + (B[3] - az) * uz;
                float lo = Math.Max(0f, Math.Min(t0, t1)), hi = Math.Min(al, Math.Max(t0, t1));
                if (hi - lo < 4f) continue;
                float half = Math.Abs(gap) / 2f;
                float mx0 = ax + nx * gap / 2f, mz0 = az + nz * gap / 2f;
                // (between two water's edges: water on both sides, not a narrow channel of water between two shores)
                bool WetAt(float t) => Wet(layer, mx0 + ux * t, mz0 + uz * t, nx, nz, half, shores);
                // Over water for at least 3 m between the fences
                int wetRun = 0, best = 0;
                for (float t = lo; t <= hi; t += 1f) { wetRun = WetAt(t) ? wetRun + 1 : 0; best = Math.Max(best, wetRun); }
                if (best < 3) continue;
                // Out to land at both ends (up to 6 m beyond the fences), then a metre more
                float s0 = lo, s1 = hi;
                while (s0 > lo - 6f && WetAt(s0)) s0 -= 1f;
                while (s1 < hi + 6f && WetAt(s1)) s1 += 1f;
                if (WetAt(s0) || WetAt(s1)) continue;
                s0 -= 1f; s1 += 1f;
                float mx = mx0 + ux * (s0 + s1) / 2f, mz = mz0 + uz * (s0 + s1) / 2f;
                if (OnLine(known, mx, mz, 5f) || found.Exists(f => OnLine(f.xy, mx, mz, 5f))) continue;
                bool paved = (layer.At(mx, mz) & WorldCell.BaseMask) == WorldCell.Paved;
                // Between two water's edges the strip is a small wooden footbridge with its own railings, made wide
                // enough to run over (the map draws it a metre or two wide)
                found.Add(new Span
                {
                    xy = new[] { mx0 + ux * s0, mz0 + uz * s0, mx0 + ux * s1, mz0 + uz * s1 },
                    width = shores ? Math.Max(2.4f, Math.Abs(gap)) : Math.Abs(gap),
                    flags = shores ? OverWater | Wood : OverWater | Fenced | (paved ? 0 : Wood),
                });
            }
        }
        return found;
    }

    static bool OnLine(List<float[]> lines, float x, float z, float reach)
    {
        if (lines == null) return false;
        foreach (float[] o in lines) if (OnLine(o, x, z, reach)) return true;
        return false;
    }

    // Within reach of the line's segments, not only its corners (a long bridge has its middle far from both ends)
    static bool OnLine(float[] o, float x, float z, float reach)
    {
        for (int q = 0; q + 3 < o.Length; q += 2)
        {
            float sx = o[q + 2] - o[q], sz = o[q + 3] - o[q + 1], l2 = sx * sx + sz * sz;
            float t = l2 < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, ((x - o[q]) * sx + (z - o[q + 1]) * sz) / l2));
            float dx = o[q] + sx * t - x, dz = o[q + 1] + sz * t - z;
            if (dx * dx + dz * dz < reach * reach) return true;
        }
        return false;
    }

    // The points of a run where the line bends (more than 0.3 m off the straight line), not one every metre: a path
    // drawn straight over the water gives a straight deck
    static float[] Straight(List<float> pts, int a0, int a1)
    {
        var deck = new List<float> { pts[2 * a0], pts[2 * a0 + 1] };
        int kept = a0;
        for (int j = a0 + 1; j < a1; j++)
        {
            float kx = pts[2 * kept], kz = pts[2 * kept + 1], nx = pts[2 * j + 2], nz = pts[2 * j + 3];
            float dx = nx - kx, dz = nz - kz, l = (float)Math.Sqrt(dx * dx + dz * dz);
            bool bends = false;
            for (int m = kept + 1; m <= j && !bends; m++)
                bends = l > 1e-3f && Math.Abs((pts[2 * m] - kx) * dz - (pts[2 * m + 1] - kz) * dx) / l > 0.3f;
            if (!bends) continue;
            deck.Add(pts[2 * j]); deck.Add(pts[2 * j + 1]);
            kept = j;
        }
        deck.Add(pts[2 * a1]); deck.Add(pts[2 * a1 + 1]);
        return deck.ToArray();
    }

    static bool Near(List<float[]> lines, float x, float z, float reach)
    {
        if (lines == null) return false;
        foreach (float[] o in lines)
            for (int q = 0; q + 1 < o.Length; q += 2)
                if (Math.Abs(o[q] - x) < reach && Math.Abs(o[q + 1] - z) < reach) return true;
        return false;
    }

    static bool NearSpans(List<Span> spans, float x, float z, float reach)
    {
        foreach (Span s in spans)
            for (int q = 0; q + 1 < s.xy.Length; q += 2)
                if (Math.Abs(s.xy[q] - x) < reach && Math.Abs(s.xy[q + 1] - z) < reach) return true;
        return false;
    }

    // Fences or walls running along both sides of the deck (Tranås): the deck reaches them and has no railings of its own
    static void Flanks(WorldLayer layer, Span span)
    {
        float[] c = span.xy;
        float ax = c[0], az = c[1], bx = c[c.Length - 2], bz = c[c.Length - 1];
        float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        if (len < 1f) return;
        float ux = (bx - ax) / len, uz = (bz - az) / len, nx = -uz, nz = ux;
        float mx = (ax + bx) / 2f, mz = (az + bz) / 2f;
        float left = 99f, right = 99f;
        foreach (WorldLine l in layer.lines)
        {
            switch (l.kind)
            {
                case WorldLineKind.Fence: case WorldLineKind.HighFence: case WorldLineKind.Wall: case WorldLineKind.HighWall:
                case WorldLineKind.Hedge: case WorldLineKind.Barrier:
                    break;
                default:
                    continue;
            }
            if (l.maxE < mx - 6f || l.minE > mx + 6f || l.maxN < mz - 6f || l.minN > mz + 6f) continue;
            for (int i = 0; i + 3 < l.xy.Length; i += 2)
            {
                float sx = l.xy[i + 2] - l.xy[i], sz = l.xy[i + 3] - l.xy[i + 1], sl = (float)Math.Sqrt(sx * sx + sz * sz);
                if (sl < 0.5f || Math.Abs((sx * ux + sz * uz) / sl) < 0.9f) continue;
                // Where the segment passes the deck's middle, across the deck
                float t = ((mx - l.xy[i]) * sx + (mz - l.xy[i + 1]) * sz) / (sl * sl);
                if (t < -0.1f || t > 1.1f) continue;
                float px = l.xy[i] + sx * t, pz = l.xy[i + 1] + sz * t;
                float across = (px - mx) * nx + (pz - mz) * nz;
                if (across > 0.4f && across < 4.5f) left = Math.Min(left, across);
                else if (across < -0.4f && across > -4.5f) right = Math.Min(right, -across);
            }
        }
        if (left > 50f || right > 50f) return;
        // The deck between the fences, its middle half way between them
        float shift = (left - right) / 2f;
        for (int q = 0; q + 1 < c.Length; q += 2)
        {
            c[q] += nx * shift;
            c[q + 1] += nz * shift;
        }
        span.width = left + right;
        span.flags |= Fenced;
    }

    // ---- A forest map ----

    static readonly ConditionalWeakTable<WorldLayer, List<Span>> forest = new ConditionalWeakTable<WorldLayer, List<Span>>();

    public static List<Span> Forest(WorldLayer layer)
    {
        if (layer == null) return new List<Span>();
        lock (forest)
        {
            if (forest.TryGetValue(layer, out List<Span> had)) return had;
        }
        List<Span> made;
        try { made = MakeForest(layer); }
        catch (Exception) { made = new List<Span>(); }
        lock (forest)
        {
            if (forest.TryGetValue(layer, out List<Span> had)) return had;
            forest.Add(layer, made);
        }
        return made;
    }

    static float PathWidth(WorldLineKind k)
    {
        switch (k)
        {
            case WorldLineKind.WideRoad: return 5f;
            case WorldLineKind.Road: return 4f;
            case WorldLineKind.Track: return 3f;
            case WorldLineKind.Footpath: return 1.4f;
            case WorldLineKind.SmallPath: return 1.1f;
            case WorldLineKind.FaintPath: return 0.9f;
            case WorldLineKind.Ride: return 1.4f;
            default: return 0f;
        }
    }

    static List<Span> MakeForest(WorldLayer layer)
    {
        var spans = new List<Span>();
        // The map's own bridges: a footbridge, as wide as its two side lines are apart when it has two
        // A bridge line running along a path or road is a side mark of that way's bridge (ISOM 512.1, over a stream
        // or another road): the way's own crossing (below) makes it. A lone one is a footbridge.
        var drawn = new List<float[]>();
        foreach (WorldLine l in layer.lines)
            if (l.kind == WorldLineKind.Bridge && l.xy.Length >= 4 && !AlongWay(layer, l) && OverWet(layer, l)) drawn.Add((float[])l.xy.Clone());
        var widths = new List<float>();
        TvTown.PairSides(drawn, widths);
        for (int i = 0; i < drawn.Count; i++)
            spans.Add(new Span { xy = drawn[i], width = widths[i] > 0.5f ? Math.Min(6f, widths[i]) : 1.6f, flags = Wood });

        var paths = new List<PathLine>();
        var lineOf = new List<WorldLine>();
        foreach (WorldLine l in layer.lines)
        {
            float w = PathWidth(l.kind);
            if (w <= 0f || l.xy.Length < 4) continue;
            paths.Add(new PathLine { xy = l.xy, width = w, paved = l.kind == WorldLineKind.WideRoad || l.kind == WorldLineKind.Road });
            lineOf.Add(l);
        }
        var known = new List<float[]>(drawn);
        spans.AddRange(OverWaterSpans(layer, paths, known));

        // Where a path crosses a stream: a footbridge from bank to bank
        foreach (Span s in spans) known.Add(s.xy);
        var crossings = new List<Span>();
        for (int p = 0; p < paths.Count; p++)
        {
            WorldLine pl = lineOf[p];
            foreach (WorldLine s in layer.lines)
            {
                if (s.kind != WorldLineKind.Stream && s.kind != WorldLineKind.SmallStream) continue;
                if (s.maxE < pl.minE || s.minE > pl.maxE || s.maxN < pl.minN || s.minN > pl.maxN) continue;
                float halfLen = s.kind == WorldLineKind.Stream ? 2.8f : 1.8f;
                for (int i = 0; i + 3 < pl.xy.Length; i += 2)
                {
                    float ax = pl.xy[i], az = pl.xy[i + 1], bx = pl.xy[i + 2], bz = pl.xy[i + 3];
                    for (int j = 0; j + 3 < s.xy.Length; j += 2)
                    {
                        if (!Cross(ax, az, bx, bz, s.xy[j], s.xy[j + 1], s.xy[j + 2], s.xy[j + 3], out float px, out float pz)) continue;
                        if (Near(known, px, pz, 5f) || NearSpans(crossings, px, pz, 5f)) continue;
                        float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                        if (len < 1e-3f) continue;
                        float ux = (bx - ax) / len, uz = (bz - az) / len;
                        // Square to the stream when the path crosses it at a slant, so the deck is not needlessly long
                        float sx = s.xy[j + 2] - s.xy[j], sz = s.xy[j + 3] - s.xy[j + 1], sl = (float)Math.Sqrt(sx * sx + sz * sz);
                        float sin = sl > 1e-3f ? Math.Abs(ux * sz / sl - uz * sx / sl) : 1f;
                        float L = halfLen / Math.Max(0.5f, sin);
                        crossings.Add(new Span
                        {
                            xy = new[] { px - ux * L, pz - uz * L, px + ux * L, pz + uz * L },
                            width = Math.Max(1.2f, Math.Min(4f, paths[p].width)),
                            flags = paths[p].paved ? 0 : Wood
                        });
                    }
                }
            }
        }
        spans.AddRange(crossings);
        return spans;
    }

    // A footbridge crosses something wet: water or marsh cells, or a stream, ditch or narrow marsh
    static bool OverWet(WorldLayer layer, WorldLine b)
    {
        for (int i = 0; i + 3 < b.xy.Length; i += 2)
        {
            float ax = b.xy[i], az = b.xy[i + 1], bx = b.xy[i + 2], bz = b.xy[i + 3];
            float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int k = Math.Max(1, (int)(len / 0.5f));
            for (int j = 0; j <= k; j++)
            {
                byte c = layer.At(ax + (bx - ax) * j / k, az + (bz - az) * j / k);
                if ((c & WorldCell.BaseMask) == WorldCell.Water || (c & WorldCell.Marsh) != 0) return true;
            }
            foreach (WorldLine s in layer.lines)
            {
                if (s.kind != WorldLineKind.Stream && s.kind != WorldLineKind.SmallStream && s.kind != WorldLineKind.Ditch && s.kind != WorldLineKind.NarrowMarsh) continue;
                if (s.maxE < Math.Min(ax, bx) || s.minE > Math.Max(ax, bx) || s.maxN < Math.Min(az, bz) || s.minN > Math.Max(az, bz)) continue;
                for (int q = 0; q + 3 < s.xy.Length; q += 2)
                    if (Cross(ax, az, bx, bz, s.xy[q], s.xy[q + 1], s.xy[q + 2], s.xy[q + 3], out _, out _)) return true;
            }
        }
        return false;
    }

    static bool AlongWay(WorldLayer layer, WorldLine b)
    {
        float ax = b.xy[0], az = b.xy[1], bx = b.xy[b.xy.Length - 2], bz = b.xy[b.xy.Length - 1];
        float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        if (len < 0.5f) return false;
        float ux = (bx - ax) / len, uz = (bz - az) / len, mx = (ax + bx) / 2f, mz = (az + bz) / 2f;
        foreach (WorldLine l in layer.lines)
        {
            if (PathWidth(l.kind) <= 0f && l.kind != WorldLineKind.Railway) continue;
            if (l.maxE < mx - 8f || l.minE > mx + 8f || l.maxN < mz - 8f || l.minN > mz + 8f) continue;
            for (int i = 0; i + 3 < l.xy.Length; i += 2)
            {
                float sx = l.xy[i + 2] - l.xy[i], sz = l.xy[i + 3] - l.xy[i + 1], sl = (float)Math.Sqrt(sx * sx + sz * sz);
                if (sl < 0.3f || Math.Abs((sx * ux + sz * uz) / sl) < 0.8f) continue;
                float t = Math.Max(0f, Math.Min(1f, ((mx - l.xy[i]) * sx + (mz - l.xy[i + 1]) * sz) / (sl * sl)));
                float px = l.xy[i] + sx * t - mx, pz = l.xy[i + 1] + sz * t - mz;
                if (px * px + pz * pz < 64f) return true;
            }
        }
        return false;
    }

    static bool Cross(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy, out float px, out float py)
    {
        px = py = 0f;
        float rx = bx - ax, ry = by - ay, sx = dx - cx, sy = dy - cy;
        float den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-6f) return false;
        float t = ((cx - ax) * sy - (cy - ay) * sx) / den, u = ((cx - ax) * ry - (cy - ay) * rx) / den;
        if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
        px = ax + rx * t;
        py = ay + ry * t;
        return true;
    }

    // Under a bridge made from a path over water, beside its deck: water in 3D (the land strip of the 2 m cells is
    // not drawn). Ends: the metre of land at each end of the deck stays land.
    public static bool UnderDeck(float[] c, float width, float x, float z, float beyond)
    {
        float reach = width / 2f + beyond;
        for (int q = 0; q + 3 < c.Length; q += 2)
        {
            float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
            float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
            if (l2 < 1e-6f) continue;
            float t = ((x - ax) * dx + (z - az) * dz) / l2;
            float l = (float)Math.Sqrt(l2);
            float lo = q == 0 ? 1.5f / l : 0f, hi = q + 4 >= c.Length ? 1f - 1.5f / l : 1f;
            if (t < lo || t > hi) continue;
            float qx = ax + dx * t - x, qz = az + dz * t - z;
            if (qx * qx + qz * qz <= reach * reach) return true;
        }
        return false;
    }
}
