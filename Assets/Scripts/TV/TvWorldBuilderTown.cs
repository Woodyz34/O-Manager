using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MapImport;

// The town of a sprint map in the run-yourself 3D world (2026-11-20, Anders: "Detta ser riktigt bra ut du kan börja
// implementera allt detta till spelet"). A piece of the world on a sprint map is built like a forest piece, and then:
//  - the map's house areas become houses: a block is cut into town houses along its long side, long slabs become
//    blocks of flats, small houses in gardens villas, the church and the town hall stand where TvTown found them;
//    each house is drawn in full near the runner and as a plain box with a roof further away;
//  - streets and squares are asphalt (the Swedish town) or cobbles (the old stone town), with lamps, benches, bins,
//    trees, parked cars and bicycles, café tables, flowers and, in the old town, washing over the narrow lanes;
//  - bridges, tunnels (also the passages through houses), stairs and canopies from the map's lines;
//  - people: some sit on the benches (drawn with the street), the rest watch or walk, and RunYourself/RunPeople.cs
//    makes them turn, cheer and walk.
// Every choice is made from the place on the map, so the pieces agree with each other.
public partial class TvWorldBuilder
{
    bool[] pavedCorner;                 // the ground corners on streets, squares and under houses (Ground)
    TvMesh townMesh, townFarMesh, townThingMesh, townBuiltMesh, pavedMesh;
    List<(int kind, float[] xz, float w, int flags)> townLines;    // 1 bridge, 2 tunnel, 3 stairs, 4 canopy outline; the piece's metres; flags: TvBridges
    List<(float[] xz, float w, bool paved)> townPaths;
    List<float[]> townEdges;                                         // the map's edge lines of paved areas near the piece              // the town's paths and paved ways near the piece
    readonly List<(float x, float z, float r, int kind)> townPlaced = new List<(float, float, float, int)>();

    bool Old => input.town != null && input.town.style == TvCityShapes.Style.OldStone;

    static readonly TvColor AsphaltA = TvColor.Hex("#6c6b69"), AsphaltB = TvColor.Hex("#7b7a76"),
        CobbleA = TvColor.Hex("#a39a8a"), CobbleB = TvColor.Hex("#8e8676");

    // The colour of a street or square: asphalt with patches, light paving stones on squares; worn cobbles
    TvColor TownPaved(float ax, float az)
    {
        float f = TvRandom.Patches(ax * 0.08f, az * 0.08f);
        if (Old) return CobbleA.Lerp(CobbleB, f).Lerp(TvColor.Hex("#b7a98e"), Sm(0.6f, 0.85f, TvRandom.Noise(ax * 0.3f, az * 0.3f)) * 0.35f);
        return AsphaltA.Lerp(AsphaltB, f);
    }

    // The town's lines that come near this piece, in the piece's metres
    void TownLines()
    {
        if (townLines != null) return;
        townLines = new List<(int, float[], float, int)>();
        townPaths = new List<(float[], float, bool)>();
        townEdges = new List<float[]>();
        TvTown t = input.town;
        float[] Local(float[] xy)
        {
            var xz = new float[xy.Length];
            float lo = float.MaxValue, hi = float.MinValue, lo2 = float.MaxValue, hi2 = float.MinValue;
            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                xz[i] = (float)(xy[i] - input.centerE);
                xz[i + 1] = (float)(xy[i + 1] - input.centerN);
                lo = Math.Min(lo, xz[i]); hi = Math.Max(hi, xz[i]); lo2 = Math.Min(lo2, xz[i + 1]); hi2 = Math.Max(hi2, xz[i + 1]);
            }
            if (hi < -R - 100 || lo > R + 100 || hi2 < -R - 100 || lo2 > R + 100) return null;   // as far as Buildings looks
            return xz;
        }
        void Take(List<float[]> src, int kind, List<float> widths = null, List<int> flags = null)
        {
            for (int s = 0; s < src.Count; s++)
            {
                float[] xz = Local(src[s]);
                if (xz == null) continue;
                townLines.Add((kind, xz, widths != null && s < widths.Count ? widths[s] : 0f, flags != null && s < flags.Count ? flags[s] : 0));
            }
        }
        if (t == null)
        {
            // A forest map: only its footbridges (TvBridges)
            foreach (TvBridges.Span span in TvBridges.Forest(layer))
            {
                float[] xz = Local(span.xy);
                if (xz != null) townLines.Add((1, xz, span.width, span.flags));
            }
            return;
        }
        Take(t.bridges, 1, t.bridgeWidths, t.bridgeFlags);
        Take(t.tunnels, 2, t.tunnelWidths);
        Take(t.stairs, 3, t.stairWidths);
        Take(t.canopies, 4);
        foreach (TvBridges.PathLine path in t.paths)
        {
            float[] xz = Local(path.xy);
            if (xz != null) townPaths.Add((xz, path.width, path.paved));
        }
        foreach (float[] edge in t.pavedEdges)
        {
            float[] xz = Local(edge);
            if (xz != null) townEdges.Add(xz);
        }
    }

    // Under the deck of a bridge (not its last metre and a half at each end, where it meets the ground)
    bool UnderBridgeDeck(float x, float z)
    {
        if (townLines == null) return false;
        foreach (var l in townLines)
            if (l.kind == 1 && TvBridges.UnderDeck(l.xz, BridgeWidth(l.w), x, z, -0.1f)) return true;
        return false;
    }

    // Beside the deck of a bridge made from a path over water (TvBridges.OverWater): water, whatever the 2 m cells say
    bool BridgeWater(float x, float z)
    {
        if (townLines == null) return false;
        foreach (var l in townLines)
        {
            if (l.kind != 1 || (l.flags & TvBridges.OverWater) == 0) continue;
            if (TvBridges.UnderDeck(l.xz, BridgeWidth(l.w), x, z, 3f)) return true;
        }
        return false;
    }

    // Metres to the nearest line of a kind (99 when none is near)
    float TownLineDist(float x, float z, int kind)
    {
        float best = 99f;
        foreach (var l in townLines)
        {
            if (l.kind != kind) continue;
            for (int q = 0; q + 3 < l.xz.Length; q += 2)
                best = Math.Min(best, DistSeg(x, z, l.xz[q], l.xz[q + 1], l.xz[q + 2], l.xz[q + 3], out _, out _));
        }
        return best;
    }

    // Inside the outline of a canopy (a roof on posts, or a passage under a house)
    bool InsideCanopy(float x, float z)
    {
        foreach (var l in townLines)
        {
            if (l.kind != 4) continue;
            float[] c = l.xz;
            bool inside = false;
            for (int i = 0, j = c.Length / 2 - 1; i < c.Length / 2; j = i++)
            {
                float xi = c[i * 2], zi = c[i * 2 + 1], xj = c[j * 2], zj = c[j * 2 + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
            }
            if (inside) return true;
        }
        return false;
    }

    // A canopy or a passage under a house on the map (light grey, ISSprOM 522), from the 2D grid's ground kinds; the
    // outlines alone miss the ones whose outline ends on the house walls
    bool CanopyCell(float x, float z) => input.passable != null && input.passable.Canopy(input.centerE + x, input.centerN + z);

    // Something built stands within gap of here: a house, a canopy, a bridge, a tunnel or stairs (2026-11-20, Anders:
    // a tree grew up through a bicycle shelter). Trees, thickets and street things keep out of them.
    bool TownKeepOut(float x, float z, float gap)
    {
        if (Base(CellAt(x, z)) == WorldCell.Building) return true;
        if (gap > 0.3f)
            for (int k = 0; k < 8; k++)
            {
                float a = k * 0.785398f;
                if (Base(CellAt(x + (float)Math.Cos(a) * gap, z + (float)Math.Sin(a) * gap)) == WorldCell.Building) return true;
            }
        TownLines();
        if (InsideCanopy(x, z) || TownLineDist(x, z, 4) < gap) return true;
        if (CanopyCell(x, z)) return true;
        if (gap > 0.3f)
            for (int k = 0; k < 8; k += 2)
            {
                float a = k * 0.785398f;
                if (CanopyCell(x + (float)Math.Cos(a) * gap, z + (float)Math.Sin(a) * gap)) return true;
            }
        return TownLineDist(x, z, 1) < gap + 2.5f || TownLineDist(x, z, 2) < gap + 2.5f || TownLineDist(x, z, 3) < gap + 1f;
    }

    // ---- Heights: bridge decks are level with their ends ----

    void TownHeights()
    {
        TownLines();
        WallSegments();
        // Retaining walls first: the stairs then see the ground as it stands by the walls
        if (input.town != null) WallSteps();
        var plans = new List<StairPlan>();
        foreach (var l in townLines)
        {
            if (l.kind != 3) continue;
            StairPlan plan = PlanStairs(l.xz, l.w);
            if (plan != null) plans.Add(plan);
        }
        JoinStairs(plans);
        foreach (StairPlan plan in plans) ApplyStairs(plan);
        foreach (var l in townLines)
        {
            if (l.kind != 1) continue;
            float[] c = l.xz;
            float bh = BridgeWidth(l.w) / 2f;
            int n = c.Length / 2;
            var along = new float[n];
            for (int i = 1; i < n; i++)
                along[i] = along[i - 1] + (float)Math.Sqrt((c[i * 2] - c[i * 2 - 2]) * (c[i * 2] - c[i * 2 - 2]) + (c[i * 2 + 1] - c[i * 2 - 1]) * (c[i * 2 + 1] - c[i * 2 - 1]));
            float total = Math.Max(0.1f, along[n - 1]);
            float h0 = Plain(c[0], c[1]), h1 = Plain(c[c.Length - 2], c[c.Length - 1]);
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                int seg = q / 2;
                float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
                float segLen = along[seg + 1] - along[seg];
                ForNear(ax, az, bx, bz, bh + 0.4f, (k, d, qx, qz) =>
                {
                    float t = segLen > 0.01f ? (float)Math.Sqrt((qx - ax) * (qx - ax) + (qz - az) * (qz - az)) / segLen : 0f;
                    float deck = Lerp(h0, h1, (along[seg] + t * segLen) / total) + 0.1f;
                    F[k] = Lerp(deck, F[k], Sm(bh - 0.05f, bh + 0.4f, d));
                });
            }
        }
    }

    // ---- Walls in a town: a step in the ground, not a slope down to their foot ----

    List<float[]> wallSegs;             // the walls and fences near the piece (ax, az, bx, bz): the stairs' digging stops at them

    void WallSegments()
    {
        if (wallSegs != null) return;
        wallSegs = new List<float[]>();
        foreach (var line in lines)
        {
            switch (line.kind)
            {
                case WorldLineKind.Wall: case WorldLineKind.HighWall: case WorldLineKind.Fence: case WorldLineKind.HighFence:
                case WorldLineKind.Hedge: case WorldLineKind.Cliff: case WorldLineKind.SmallCliff:
                    break;
                default:
                    continue;
            }
            float[] c = line.xz;
            for (int q = 0; q + 3 < c.Length; q += 2) wallSegs.Add(new[] { c[q], c[q + 1], c[q + 2], c[q + 3] });
        }
    }

    // A straight from (ax, az) to (bx, bz) crosses a wall or fence
    bool AcrossWall(float ax, float az, float bx, float bz)
    {
        if (wallSegs == null) return false;
        foreach (float[] w in wallSegs)
        {
            if (Math.Max(w[0], w[2]) < Math.Min(ax, bx) || Math.Min(w[0], w[2]) > Math.Max(ax, bx)
                || Math.Max(w[1], w[3]) < Math.Min(az, bz) || Math.Min(w[1], w[3]) > Math.Max(az, bz)) continue;
            float d1x = bx - ax, d1z = bz - az, d2x = w[2] - w[0], d2z = w[3] - w[1];
            float den = d1x * d2z - d1z * d2x;
            if (Math.Abs(den) < 1e-6f) continue;
            float t = ((w[0] - ax) * d2z - (w[1] - az) * d2x) / den, u = ((w[0] - ax) * d1z - (w[1] - az) * d1x) / den;
            if (t > 0.02f && t < 0.98f && u >= 0f && u <= 1f) return true;
        }
        return false;
    }

    // A wall with the ground higher on one side (2026-11-23, Anders: a town map draws a drop as a wall, not as a
    // cliff; the ground on the high side is level with the wall's top, it does not slope down to the wall's foot):
    // the ground beside it is flat at each side's own height, from the map's heights 4 m out, so the wall stands as
    // a step. Worked out from the map's heights alone, so neighbouring pieces agree.
    void WallSteps()
    {
        foreach (var line in lines)
        {
            if (line.kind != WorldLineKind.HighWall && line.kind != WorldLineKind.Wall) continue;
            float[] c = line.xz;
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
                float sx = bx - ax, sz = bz - az, sl = (float)Math.Sqrt(sx * sx + sz * sz);
                if (sl < 0.3f) continue;
                float nx = -sz / sl, nz = sx / sl;
                ForNear(ax, az, bx, bz, 3.5f, (k, d, qx, qz) =>
                {
                    float x = k % N - R, z = k / N - R;
                    float side = (x - qx) * nx + (z - qz) * nz >= 0f ? 1f : -1f;
                    float hThis = Plain(qx + nx * side * 4f, qz + nz * side * 4f), hOther = Plain(qx - nx * side * 4f, qz - nz * side * 4f);
                    if (Math.Abs(hThis - hOther) < 0.5f) return;
                    if (Base(CellAt(x, z)) == WorldCell.Building) return;
                    float w = 1f - Sm(2.4f, 3.5f, d);
                    if (hThis > hOther) F[k] = Math.Max(F[k], Lerp(F[k], hThis, w));      // the high side: flat up to the top
                    else F[k] = Math.Min(F[k], Lerp(F[k], hThis, w));                     // the low side: flat to the foot
                });
            }
        }
    }

    // ---- Stairs ----

    // Stairs always climb (2026-11-20, Anders: flat stairs looked wrong), and at a real stair angle (2026-11-23,
    // Anders: a street climbed evenly up to a flat stair; it should be flat up to the stairs and then steeply up them).
    // Which way they climb: the map's heights a little beyond each end when they differ; else up to the house the
    // stairs lead to (steps up to a door); else the way the land rises round them; else up along the line as drawn.
    // When the land falls away from the low end, the ground in front is dug to a flat way in, as far as the map's own
    // ground is that low. Where the land is flat they go a little down at the low end and a little up at the top
    // (2026-11-23, Anders: no deep pit like a bomb crater, half steep at both ends), and stairs whose ends are near
    // each other share those ends' heights, so stairs that belong together work together (a sunken yard, a terrace).
    // The digging and raising stay narrow and stop at walls, fences and houses.
    const float StairGrade = 0.55f;

    class StairPlan
    {
        public float[] c;
        public float[] along;
        public float total, half;
        public int sign;                                // +1: climbs from the line's first point
        public float lx, lz, lux, luz, tx, tz, tux, tuz; // the low and the high end, each with its way out
        public float hLow, hHigh;                       // the heights of the low and the high end
        public float dig;                               // a flat way in dug out this far in front of the low end (0: none)
        public bool door;                               // steps up to a door: nothing dug or raised
    }

    StairPlan PlanStairs(float[] c, float width)
    {
        int n = c.Length / 2;
        if (n < 2) return null;
        var along = new float[n];
        for (int i = 1; i < n; i++)
            along[i] = along[i - 1] + (float)Math.Sqrt((c[i * 2] - c[i * 2 - 2]) * (c[i * 2] - c[i * 2 - 2]) + (c[i * 2 + 1] - c[i * 2 - 1]) * (c[i * 2 + 1] - c[i * 2 - 1]));
        float total = along[n - 1];
        if (total < 0.6f || total > 80f) return null;
        float ax = c[0], az = c[1], bx = c[c.Length - 2], bz = c[c.Length - 1];
        float adx = c[0] - c[2], adz = c[1] - c[3], al = Math.Max(0.01f, (float)Math.Sqrt(adx * adx + adz * adz));
        float bdx = c[c.Length - 2] - c[c.Length - 4], bdz = c[c.Length - 1] - c[c.Length - 3], bl = Math.Max(0.01f, (float)Math.Sqrt(bdx * bdx + bdz * bdz));
        adx /= al; adz /= al; bdx /= bl; bdz /= bl;
        var p = new StairPlan { c = c, along = along, total = total, half = (width > 0.5f ? width : 2f) / 2f };
        float hA = Plain(ax + adx * 2.5f, az + adz * 2.5f), hB = Plain(bx + bdx * 2.5f, bz + bdz * 2.5f);
        float dh = hB - hA;
        float need = Math.Max(0.45f, Math.Min(total * StairGrade, 6f));
        if (Math.Abs(dh) >= 0.3f) p.sign = Math.Sign(dh);
        else
        {
            bool houseA = Base(CellAt(ax + adx * 1.2f, az + adz * 1.2f)) == WorldCell.Building;
            bool houseB = Base(CellAt(bx + bdx * 1.2f, bz + bdz * 1.2f)) == WorldCell.Building;
            if (houseB && !houseA) p.sign = 1;
            else if (houseA && !houseB) p.sign = -1;
            else
            {
                float wide = Plain(bx + bdx * 9f, bz + bdz * 9f) - Plain(ax + adx * 9f, az + adz * 9f);
                p.sign = Math.Abs(wide) > 0.1f ? Math.Sign(wide) : 1;
            }
        }
        bool up = p.sign > 0;
        p.lx = up ? ax : bx; p.lz = up ? az : bz; p.lux = up ? adx : bdx; p.luz = up ? adz : bdz;
        p.tx = up ? bx : ax; p.tz = up ? bz : az; p.tux = up ? bdx : adx; p.tuz = up ? bdz : adz;
        float hTop = Plain(p.tx + p.tux, p.tz + p.tuz);
        // Out from the low end: the first place low enough for a real stair angle, else the lowest within 30 m
        float lowest = float.MaxValue, lowestAt = 0f, dig = -1f;
        for (float d = 0.5f; d <= 30f; d += 1f)
        {
            float h = Plain(p.lx + p.lux * d, p.lz + p.luz * d);
            if (hTop - h >= need) { dig = d; lowest = h; break; }
            if (h < lowest) { lowest = h; lowestAt = d; }
        }
        if (dig < 0f && hTop - lowest >= 0.3f) dig = lowestAt;
        if (dig >= 0f)
        {
            p.hHigh = hTop;
            p.hLow = lowest;
            p.dig = dig > 1.6f ? dig : 0f;
            return p;
        }
        p.door = Base(CellAt(p.tx + p.tux * 1.2f, p.tz + p.tuz * 1.2f)) == WorldCell.Building;
        if (p.door)
        {
            p.hLow = Plain(p.lx, p.lz);
            p.hHigh = p.hLow + Math.Max(0.45f, Math.Min(total * 0.5f, 1.4f));
            return p;
        }
        // Flat land: half steep, a little down at the low end and a little up at the top
        float rise = Math.Max(0.45f, Math.Min(total * 0.3f, 2.4f));
        float mid = (Plain(p.lx, p.lz) + Plain(p.tx, p.tz)) / 2f;
        p.hLow = mid - rise / 2f;
        p.hHigh = mid + rise / 2f;
        return p;
    }

    // Stairs with ends near each other (6 m) share those ends' heights
    static void JoinStairs(List<StairPlan> plans)
    {
        const float Near = 6f;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < plans.Count; i++)
                for (int j = i + 1; j < plans.Count; j++)
                {
                    StairPlan a = plans[i], b = plans[j];
                    if (a.door || b.door) continue;
                    for (int ea = 0; ea < 2; ea++)
                        for (int eb = 0; eb < 2; eb++)
                        {
                            float axp = ea == 0 ? a.lx : a.tx, azp = ea == 0 ? a.lz : a.tz, bxp = eb == 0 ? b.lx : b.tx, bzp = eb == 0 ? b.lz : b.tz;
                            if ((axp - bxp) * (axp - bxp) + (azp - bzp) * (azp - bzp) > Near * Near) continue;
                            float ha = ea == 0 ? a.hLow : a.hHigh, hb = eb == 0 ? b.hLow : b.hHigh, h = (ha + hb) / 2f;
                            if (ea == 0) a.hLow = h; else a.hHigh = h;
                            if (eb == 0) b.hLow = h; else b.hHigh = h;
                        }
                    // Always a climb
                    foreach (StairPlan s in new[] { a, b })
                        if (s.hHigh - s.hLow < 0.45f) { float m = (s.hHigh + s.hLow) / 2f; s.hLow = m - 0.225f; s.hHigh = m + 0.225f; }
                }
    }

    void ApplyStairs(StairPlan p)
    {
        if (!p.door)
        {
            // The low end: dug down to its height (a flat way in), or raised a little to it
            float atLow = Plain(p.lx, p.lz), atTop = Plain(p.tx, p.tz);
            float digLen = Math.Max(p.dig, Math.Max(2f, Math.Abs(atLow - p.hLow) * 1.5f));
            Shape(p.lx, p.lz, p.lux, p.luz, p.half, digLen, p.hLow);
            float raiseLen = Math.Max(2f, Math.Abs(p.hHigh - atTop) * 1.5f);
            Shape(p.tx, p.tz, p.tux, p.tuz, p.half, raiseLen, p.hHigh);
        }
        float[] c = p.c;
        float rise = p.hHigh - p.hLow;
        for (int q = 0; q + 3 < c.Length; q += 2)
        {
            int seg = q / 2;
            float sx = c[q], sz = c[q + 1], ex = c[q + 2], ez = c[q + 3];
            float segLen = p.along[seg + 1] - p.along[seg];
            ForNear(sx, sz, ex, ez, p.half + 0.9f, (k, d, qx, qz) =>
            {
                float x = k % N - R, z = k / N - R;
                if (d > p.half + 0.1f && AcrossWall(qx, qz, x, z)) return;
                float t = segLen > 0.01f ? (float)Math.Sqrt((qx - sx) * (qx - sx) + (qz - sz) * (qz - sz)) / segLen : 0f;
                float f = (p.along[seg] + t * segLen) / p.total;
                if (p.sign < 0) f = 1f - f;
                float y = p.hLow + rise * f;
                F[k] = Lerp(y, F[k], Sm(p.half + 0.15f, p.half + 0.9f, d));
            });
        }
    }

    // The ground out from an end of stairs (from lx, lz along ux, uz) brought to level: flat for len metres a little
    // wider than the stairs, easing back into the land over 3 m beyond and 1.5 m to the sides. Not on houses, and not
    // past a wall or fence (that ground keeps its own height).
    void Shape(float lx, float lz, float ux, float uz, float half, float len, float level)
    {
        float wide = half + 0.6f, reach = wide + 1.5f;
        ForNear(lx, lz, lx + ux * (len + 3f), lz + uz * (len + 3f), reach, (k, d, qx, qz) =>
        {
            float x = k % N - R, z = k / N - R;
            float along = (x - lx) * ux + (z - lz) * uz;
            if (along < -0.3f) return;
            float side = Math.Abs(-(x - lx) * uz + (z - lz) * ux);
            if (Base(CellAt(x, z)) == WorldCell.Building) return;
            if (AcrossWall(lx + ux * 0.3f, lz + uz * 0.3f, x, z)) return;
            float w = (1f - Sm(len, len + 3f, along)) * (1f - Sm(wide, reach, side));
            if (w <= 0f) return;
            F[k] += w * (level - F[k]);
        });
    }

    // A bridge's deck across, from the metres between its side lines (0: drawn as one line)
    static float BridgeWidth(float w) => w > 0.5f ? Math.Max(1.2f, Math.Min(10f, w)) : 2.6f;
    static float TunnelWidth(float w) => w > 0.5f ? Math.Max(2.4f, Math.Min(10f, w - 0.4f)) : 3.2f;

    // The height of a bridge's deck at a point on it
    float DeckAt(float x, float z) => H(x, z);

    // ---- Houses ----

    // One group of joined house cells (Buildings): cut into houses, each built by the piece its middle is in
    void TownBlock(List<int> group, int cells, Func<int, float> cellX, Func<int, float> cellZ, double e0, double n0)
    {
        TownLines();
        EnsureTownMeshes();
        TvTown t = input.town;
        int Index(double e, double n)
        {
            if (double.IsNaN(e)) return -1;
            int i = (int)(Math.Floor((e - layer.minE) / layer.cell) - e0), j = (int)(Math.Floor((n - layer.minN) / layer.cell) - n0);
            return i < 0 || j < 0 || i >= cells || j >= cells ? -1 : j * cells + i;
        }
        int church = Index(t.churchE, t.churchN), hall = Index(t.hallE, t.hallN);
        var pts = new List<(float x, float z)>(group.Count);
        var arch = new List<(float x, float z)>();
        foreach (int k in group)
        {
            float x = cellX(k % cells), z = cellZ(k / cells);
            // A passage through the house: the cells along a tunnel line are left open, with the house over them
            if (TownLineDist(x, z, 2) < 1.8f) { arch.Add((x, z)); continue; }
            pts.Add((x, z));
            // The run-yourself world stops the runner by the race's own grid (TvPassable), exactly at the walls
            if (!input.chunkDetail && input.passable == null && Inside(x, z, 0f)) world.solids.Add(new TvSolid { x = x, z = z, r = layer.cell * 0.75f });
        }
        foreach (var a in arch)
        {
            if (input.chunkDetail || !Inside(a.x, a.z, 0f)) continue;
            float y = H(a.x, a.z);
            int look = (int)(TvRandom.Hash((int)Math.Floor((a.x + ox) / 6f), (int)Math.Floor((a.z + oz) / 6f)) * 97f);
            TvColor wall = Old ? TvColor.Hex("#ece6d6") : TvCityShapes.Facades[look % TvCityShapes.Facades.Length];
            float top = Old ? 5.9f : 6.8f;
            // With the bridges: the houses near the runner are swapped for the detail build's, which has no arches
            townBuiltMesh.Add(Box(layer.cell + 0.04f, top - 3.0f, layer.cell + 0.04f, wall), a.x, y + 3.0f, a.z, 1f, 1f, 1f, 0f);
            townBuiltMesh.Add(Box(layer.cell + 0.04f, 0.1f, layer.cell + 0.04f, TvColor.Hex("#3e3a36")), a.x, y + 2.95f, a.z, 1f, 1f, 1f, 0f);
            townFarMesh.Add(Box(layer.cell + 0.04f, top - 3.0f, layer.cell + 0.04f, wall), a.x, y + 3.0f, a.z, 1f, 1f, 1f, 0f);
        }
        if (pts.Count == 0) return;
        bool isChurch = church >= 0 && group.Contains(church), isHall = !isChurch && hall >= 0 && group.Contains(hall);
        if (isChurch || isHall)
        {
            if (arch.Count == 0) { PlaceHouse(pts, isChurch ? TvCityShapes.Kind.Church : TvCityShapes.Kind.TownHall); return; }
        }
        // After a passage is cut out, the parts on each side are houses of their own
        // The houses get the map's own outline (TvTownHouses): cut first, then built together so they meet exactly
        blockHouses = new List<(List<(float x, float z)> pts, TvCityShapes.Kind kind)>();
        blockGroup = new List<int>();
        blockFloors = new List<int>();
        groups = new List<HouseGroup>();
        try
        {
            foreach (var part in Parts(pts)) SplitHouses(part, 0);
            BuildBlockHouses(blockHouses);
        }
        finally { blockHouses = null; blockGroup = null; blockFloors = null; groups = null; }
    }

    // The joined parts of a set of cells
    List<List<(float x, float z)>> Parts(List<(float x, float z)> pts)
    {
        var parts = new List<List<(float, float)>>();
        var left = new List<(float x, float z)>(pts);
        float c2 = layer.cell * 1.1f;
        while (left.Count > 0)
        {
            var part = new List<(float x, float z)> { left[left.Count - 1] };
            left.RemoveAt(left.Count - 1);
            for (int i = 0; i < part.Count; i++)
            {
                for (int j = left.Count - 1; j >= 0; j--)
                {
                    if (Math.Abs(left[j].x - part[i].x) + Math.Abs(left[j].z - part[i].z) <= c2)
                    {
                        part.Add(left[j]);
                        left.RemoveAt(j);
                    }
                }
            }
            parts.Add(part);
        }
        return parts;
    }

    struct Fit
    {
        public float cx, cz, angle, ux, uz, len, wid, fill;
    }

    // The rectangle round a set of cells: along the given angle, or along whichever fits them best of the grid and
    // their long axis
    Fit FitCells(List<(float x, float z)> pts, float? along = null)
    {
        float mx = 0f, mz = 0f;
        foreach (var p in pts) { mx += p.x; mz += p.z; }
        mx /= pts.Count; mz /= pts.Count;
        if (along.HasValue) return FitAt(pts, mx, mz, along.Value);
        float sxx = 0f, szz = 0f, sxz = 0f;
        foreach (var p in pts)
        {
            float dx = p.x - mx, dz = p.z - mz;
            sxx += dx * dx; szz += dz * dz; sxz += dx * dz;
        }
        Fit grid = FitAt(pts, mx, mz, 0f);
        if (sxx + szz < 1e-3f) return grid;
        Fit axis = FitAt(pts, mx, mz, 0.5f * (float)Math.Atan2(2f * sxz, sxx - szz));
        return axis.fill > grid.fill + 0.04f ? axis : grid;
    }

    Fit FitAt(List<(float x, float z)> pts, float mx, float mz, float angle)
    {
        float c = layer.cell;
        float ux = (float)Math.Cos(angle), uz = (float)Math.Sin(angle);
        float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
        float half = c * 0.5f * (Math.Abs(ux) + Math.Abs(uz));
        foreach (var p in pts)
        {
            float dx = p.x - mx, dz = p.z - mz, u = dx * ux + dz * uz, v = -dx * uz + dz * ux;
            u0 = Math.Min(u0, u - half); u1 = Math.Max(u1, u + half);
            v0 = Math.Min(v0, v - half); v1 = Math.Max(v1, v + half);
        }
        float len = u1 - u0, wid = v1 - v0;
        if (wid > len)
        {
            // Keep x along the long side
            angle += (float)(Math.PI / 2);
            float t = len; len = wid; wid = t;
            float a0 = u0, a1 = u1;
            u0 = v0; u1 = v1; v0 = -a1; v1 = -a0;
            ux = (float)Math.Cos(angle); uz = (float)Math.Sin(angle);
        }
        float fill = pts.Count * c * c / Math.Max(1f, len * wid);
        if (fill < 0.92f && fill > 0.6f)
        {
            // Cells round a slanted outline stick out a little
            float shrink = (float)Math.Sqrt(fill);
            float cu = (u0 + u1) / 2f, cv = (v0 + v1) / 2f;
            len = Math.Max(len - c, len * shrink); wid = Math.Max(wid - c, wid * shrink);
            u0 = cu - len / 2f; u1 = cu + len / 2f; v0 = cv - wid / 2f; v1 = cv + wid / 2f;
        }
        return new Fit
        {
            cx = mx + ux * (u0 + u1) / 2f - uz * (v0 + v1) / 2f,
            cz = mz + uz * (u0 + u1) / 2f + ux * (v0 + v1) / 2f,
            angle = angle, ux = ux, uz = uz, len = len, wid = wid, fill = fill
        };
    }

    // Cuts a block into houses, all square to the block: in two rows when it is deep, then along each row until each
    // part is a fair town house
    void SplitHouses(List<(float x, float z)> pts, int depth, float? along = null)
    {
        if (pts.Count == 0) return;
        Fit f = FitCells(pts, along);
        float area = f.len * f.wid;
        if (!Old && depth == 0)
        {
            // A big house is one house (2026-11-23, Anders: "om det är ett gigantiskt hus så blir det alltid bara radhus
            // överallt ... men det kan ju vara en jättestor skola, kyrka eller lägenhetshus"): a school, a block of flats
            // or a hall, in wings of their own height where its outline turns. Only the blocks along a shopping street
            // (paving all round) are still cut into narrow town houses.
            float cellsArea = pts.Count * layer.cell * layer.cell;
            if (cellsArea >= 420f && PavedRound(f) < 0.7f) { BigHouse(pts, f, cellsArea); return; }
            // A long low row in gardens: terraced houses, all alike, one or two floors
            if (f.wid <= 13f && f.len >= 18f && cellsArea >= 120f && f.fill >= 0.75f && GardenNear(f)) { RowHouses(pts, f); return; }
            // A long slab is a block of flats, a big plain house a hall
            if (f.fill >= 0.82f && f.wid >= 9.5f && f.wid <= 18f && f.len <= 75f && area >= 330f) { PlaceHouse(pts, TvCityShapes.Kind.Apartments, f); return; }
            if (f.fill >= 0.8f && f.wid > 18f && area >= 650f && f.len <= 90f) { PlaceHouse(pts, TvCityShapes.Kind.Hall, f); return; }
        }
        float maxLen = Old ? 15f : 21f, minLen = Old ? 5.5f : 7f, maxDepth = Old ? 13f : 17f;
        bool deep = f.wid > maxDepth * 1.3f, longer = f.len > maxLen || (f.fill < 0.78f && f.len >= 2f * minLen);
        if ((!deep && !longer) || pts.Count <= 3 || depth >= 9)
        {
            PlaceHouse(pts, TvCityShapes.Kind.TownHouse, f);
            return;
        }
        // Where to cut: across the long side a little off the middle, or along a deep block (from the place, so every
        // piece cuts alike)
        float h = TvRandom.Hash((int)Math.Floor((f.cx + ox) * 0.5f), (int)Math.Floor((f.cz + oz) * 0.5f) + depth * 7919);
        float cut = deep ? 0f : (h - 0.5f) * 0.3f * f.len;
        var a = new List<(float x, float z)>();
        var b = new List<(float x, float z)>();
        foreach (var p in pts)
        {
            float dx = p.x - f.cx, dz = p.z - f.cz;
            float w = deep ? -dx * f.uz + dz * f.ux : dx * f.ux + dz * f.uz;
            (w < cut ? a : b).Add(p);
        }
        if (a.Count == 0 || b.Count == 0)
        {
            PlaceHouse(pts, TvCityShapes.Kind.TownHouse, f);
            return;
        }
        foreach (var part in Parts(a)) SplitHouses(part, depth + 1, f.angle);
        foreach (var part in Parts(b)) SplitHouses(part, depth + 1, f.angle);
    }

    // ---- One big house in wings, a row of terraced houses ----

    // Houses that belong together: the wings of one big house, the homes of one row. They share a look (the same
    // walls and roof) and take their floors from it.
    class HouseGroup
    {
        public int look, floors;
        public TvCityShapes.Kind kind;
        public bool row;
    }
    List<HouseGroup> groups;
    List<int> blockGroup, blockFloors;
    int placingGroup = -1, placingFloors;

    // How much of the ground round a block is street or square (0..1)
    float PavedRound(Fit f)
    {
        int hits = 0, all = 0;
        for (int a = 0; a < 16; a++)
        {
            float ang = a * 0.3927f, cu = (float)Math.Cos(ang), cv = (float)Math.Sin(ang);
            // On the fitted rectangle's edge, 3 m out
            float su = Math.Abs(cu) > 1e-3f ? (f.len / 2f) / Math.Abs(cu) : float.MaxValue, sv = Math.Abs(cv) > 1e-3f ? (f.wid / 2f) / Math.Abs(cv) : float.MaxValue;
            float r = Math.Min(su, sv) + 3f, u = cu * r, v = cv * r;
            float x = f.cx + f.ux * u - f.uz * v, z = f.cz + f.uz * u + f.ux * v;
            int b = Base(CellAt(x, z));
            if (b == WorldCell.Building) continue;
            all++;
            if (b == WorldCell.Paved) hits++;
        }
        return all == 0 ? 1f : hits / (float)all;
    }

    // A school stands by its playground and bicycle sheds (Anders: "om det är lekplatser runt om ... cykelförråd ...
    // då kanske det är en skola"): canopies or odd man-made things (the X of the map: here a playground) within 40 m,
    // and room round it
    bool SchoolNear(Fit f)
    {
        float reach = Math.Max(f.len, f.wid) / 2f + 40f;
        int canopy = 0, open = 0, all = 0;
        for (float a = -reach; a <= reach; a += 4f)
            for (float b = -reach; b <= reach; b += 4f)
            {
                float x = f.cx + a, z = f.cz + b;
                int k = Base(CellAt(x, z));
                if (k == WorldCell.Building) continue;
                all++;
                if (k == WorldCell.Paved || k == WorldCell.Open || k == WorldCell.OpenTrees) open++;
                if (CanopyCell(x, z) || InsideCanopy(x, z)) canopy++;
            }
        int odd = 0;
        double e0 = input.centerE + f.cx - reach, e1 = input.centerE + f.cx + reach, n0 = input.centerN + f.cz - reach, n1 = input.centerN + f.cz + reach;
        foreach (WorldPoint p in layer.points)
            if ((p.kind == WorldPointKind.ManMade || p.kind == WorldPointKind.Feature) && p.e >= e0 && p.e <= e1 && p.n >= n0 && p.n <= n1) odd++;
        return (canopy >= 2 || odd >= 1) && open >= all * 0.45f;
    }

    void BigHouse(List<(float x, float z)> pts, Fit f, float area)
    {
        int hx = (int)Math.Floor((f.cx + ox) * 0.5f), hz = (int)Math.Floor((f.cz + oz) * 0.5f);
        float h1 = TvRandom.Hash(hx + 17, hz - 5), h2 = TvRandom.Hash(hx - 3, hz + 91);
        TvCityShapes.Kind kind;
        int floors;
        if (area >= 600f && SchoolNear(f)) { kind = TvCityShapes.Kind.School; floors = 2; }
        else if (f.fill >= 0.8f && f.wid > 20f && area >= 900f) { kind = TvCityShapes.Kind.Hall; floors = 1; }
        else { kind = TvCityShapes.Kind.Apartments; floors = 3 + (int)(h2 * 3f); }
        var g = new HouseGroup { look = (int)(h1 * 997f), floors = floors, kind = kind };
        groups.Add(g);
        int id = groups.Count - 1;
        var wings = new List<List<(float x, float z)>>();
        SplitWings(pts, 0, wings);
        // The biggest wing has the house's floors, the others one less now and then (a lower wing, an entrance hall)
        int biggest = 0;
        for (int w = 1; w < wings.Count; w++) if (wings[w].Count > wings[biggest].Count) biggest = w;
        for (int w = 0; w < wings.Count; w++)
        {
            Fit wf = FitCells(wings[w]);
            float wingArea = wings[w].Count * layer.cell * layer.cell;
            int fl = floors;
            if (w != biggest && kind != TvCityShapes.Kind.Hall)
            {
                float hw = TvRandom.Hash((int)Math.Floor((wf.cx + ox) * 0.5f) + 5, (int)Math.Floor((wf.cz + oz) * 0.5f) - 9);
                if (wingArea < 160f || hw < 0.4f) fl = Math.Max(1, floors - 1);
                else if (hw > 0.85f && kind == TvCityShapes.Kind.Apartments) fl = floors + 1;
            }
            placingGroup = id;
            placingFloors = fl;
            try { PlaceHouse(wings[w], kind, wf); }
            finally { placingGroup = -1; }
        }
    }

    // Cuts a big outline where it turns (an L, a T, a U) into wings that each fill their rectangle well
    void SplitWings(List<(float x, float z)> pts, int depth, List<List<(float x, float z)>> into)
    {
        Fit f = FitCells(pts);
        float c = layer.cell, area = pts.Count * c * c;
        if (f.fill >= 0.82f || depth >= 3 || area < 140f) { into.Add(pts); return; }
        float bestScore = f.fill + 0.06f;
        List<(float x, float z)> bestA = null, bestB = null;
        for (int axis = 0; axis < 2; axis++)
        {
            float half = (axis == 0 ? f.len : f.wid) / 2f;
            for (float cut = -half + 4f; cut <= half - 4f; cut += c)
            {
                var a = new List<(float x, float z)>();
                var b = new List<(float x, float z)>();
                foreach (var p in pts)
                {
                    float dx = p.x - f.cx, dz = p.z - f.cz;
                    float w = axis == 0 ? dx * f.ux + dz * f.uz : -dx * f.uz + dz * f.ux;
                    (w < cut ? a : b).Add(p);
                }
                if (a.Count * c * c < 60f || b.Count * c * c < 60f) continue;
                float fa = FitCells(a).fill, fb = FitCells(b).fill;
                float score = (fa * a.Count + fb * b.Count) / pts.Count;
                if (score > bestScore) { bestScore = score; bestA = a; bestB = b; }
            }
        }
        if (bestA == null) { into.Add(pts); return; }
        foreach (var part in Parts(bestA)) SplitWings(part, depth + 1, into);
        foreach (var part in Parts(bestB)) SplitWings(part, depth + 1, into);
    }

    // A row of terraced houses: homes of 6 to 7.5 m along the row, one look for the row, one or two floors
    void RowHouses(List<(float x, float z)> pts, Fit f)
    {
        int hx = (int)Math.Floor((f.cx + ox) * 0.5f), hz = (int)Math.Floor((f.cz + oz) * 0.5f);
        float h1 = TvRandom.Hash(hx + 41, hz + 7), h2 = TvRandom.Hash(hx - 19, hz - 63);
        var g = new HouseGroup { look = (int)(h1 * 997f), floors = h2 < 0.55f ? 1 : 2, kind = TvCityShapes.Kind.TownHouse, row = true };
        groups.Add(g);
        int id = groups.Count - 1;
        int homes = Math.Max(1, (int)Math.Round(f.len / (6f + h2 * 1.5f)));
        var parts = new List<(float x, float z)>[homes];
        for (int k = 0; k < homes; k++) parts[k] = new List<(float x, float z)>();
        foreach (var p in pts)
        {
            float u = (p.x - f.cx) * f.ux + (p.z - f.cz) * f.uz + f.len / 2f;
            int k = Math.Max(0, Math.Min(homes - 1, (int)(u / f.len * homes)));
            parts[k].Add(p);
        }
        foreach (var part in parts)
        {
            if (part.Count == 0) continue;
            placingGroup = id;
            placingFloors = g.floors;
            try { PlaceHouse(part, TvCityShapes.Kind.TownHouse, FitCells(part, f.angle)); }
            finally { placingGroup = -1; }
        }
    }

    void PlaceHouse(List<(float x, float z)> pts, TvCityShapes.Kind kind) => PlaceHouse(pts, kind, FitCells(pts));

    void PlaceHouse(List<(float x, float z)> pts, TvCityShapes.Kind kind, Fit f)
    {
        if (blockHouses != null)
        {
            blockHouses.Add((pts, kind));
            blockGroup.Add(placingGroup);
            blockFloors.Add(placingGroup >= 0 ? placingFloors : 0);
            return;
        }
        if (!Inside(f.cx, f.cz, 0f)) return;
        float len = Math.Max(f.len, 4f), wid = Math.Max(f.wid, 3.5f), area = len * wid;
        int hx = (int)Math.Floor((f.cx + ox) * 0.5f), hz = (int)Math.Floor((f.cz + oz) * 0.5f);
        float h1 = TvRandom.Hash(hx, hz), h2 = TvRandom.Hash(hx + 101, hz - 37), h3 = TvRandom.Hash(hx - 59, hz + 211);
        int look = (int)(h1 * 997f);
        int floors;
        if (kind == TvCityShapes.Kind.TownHouse)
        {
            // Small houses with a garden round them are villas
            bool garden = area < 170f && GardenNear(f);
            if (garden || (Old && area < 60f)) kind = TvCityShapes.Kind.Villa;
        }
        switch (kind)
        {
            case TvCityShapes.Kind.Apartments: floors = 3 + (int)(h2 * 3f); break;
            case TvCityShapes.Kind.TownHouse: floors = Old ? 1 + (int)(h2 * (area > 110f ? 3.99f : 2.5f)) : 2 + (int)(h2 * 3f); break;
            default: floors = 2; break;
        }
        float hl = len / 2f, hw = wid / 2f, lowest = float.MaxValue, highest = float.MinValue;
        foreach (float su in new[] { -1f, 0f, 1f })
            foreach (float sv in new[] { -1f, 0f, 1f })
            {
                float x = f.cx + f.ux * su * hl - f.uz * sv * hw, z = f.cz + f.uz * su * hl + f.ux * sv * hw, y = H(x, z);
                lowest = Math.Min(lowest, y); highest = Math.Max(highest, y);
            }
        float plinth = Math.Min(3f, highest - lowest) + 0.3f;
        // Which long side faces the street: the side with more paving in front of it
        float yaw = -f.angle;
        if (PavedFront(f, -1f) > PavedFront(f, 1f) + 0.1f || (Math.Abs(PavedFront(f, -1f) - PavedFront(f, 1f)) <= 0.1f && h3 < 0.5f)) yaw += (float)Math.PI;
        random.Reseed(hx, hz, 4100 + (int)kind);
        // Three looks: in full right round the runner (the piece's detail), a little leaner within the near distance,
        // a box with a roof further away
        TvShapeBuilder.Lean = !input.chunkDetail;
        try
        {
            TvShape shape = TvCityShapes.Building(random, kind, len, wid, floors, look, input.town.style, plinth);
            townMesh.Add(shape, f.cx, lowest - 0.25f, f.cz, 1f, 1f, 1f, yaw);
        }
        finally { TvShapeBuilder.Lean = false; }
        if (input.chunkDetail) return;
        random.Reseed(hx, hz, 4200);
        townFarMesh.Add(TvCityShapes.Lod(random, kind, len, wid, floors, look, input.town.style, plinth), f.cx, lowest - 0.25f, f.cz, 1f, 1f, 1f, yaw);
        townPlaced.Add((f.cx, f.cz, 0f, -1));
    }

    // How much of the ground before one long side (+1: the side of local +z) is street or square
    float PavedFront(Fit f, float side)
    {
        int hits = 0;
        for (int i = -2; i <= 2; i++)
        {
            float u = i * f.len * 0.2f, v = side * (f.wid / 2f + 3f);
            float x = f.cx + f.ux * u - f.uz * v, z = f.cz + f.uz * u + f.ux * v;
            if (Base(CellAt(x, z)) == WorldCell.Paved) hits++;
        }
        return hits / 5f;
    }

    bool GardenNear(Fit f)
    {
        int plots = 0, all = 0;
        for (float a = 0f; a < 6.28f; a += 0.785f)
        {
            float r = Math.Max(f.len, f.wid) / 2f + 3f, x = f.cx + (float)Math.Cos(a) * r, z = f.cz + (float)Math.Sin(a) * r;
            int b = Base(CellAt(x, z));
            all++;
            if (b == WorldCell.Plot || b == WorldCell.Open || b == WorldCell.OpenTrees || b == WorldCell.Forest) plots++;
        }
        return plots >= all / 2;
    }

    void EnsureTownMeshes()
    {
        if (townMesh != null) return;
        // The full houses of a piece's detail have a name of their own: RunWorld hides the leaner ones under them
        townMesh = new TvMesh { name = input.chunkDetail ? "Town houses full" : "Town houses", group = TvMesh.Near };
        townFarMesh = new TvMesh { name = "Town houses far", group = TvMesh.Far };
        townThingMesh = new TvMesh { name = "Town things", group = TvMesh.Near };
        townBuiltMesh = new TvMesh { name = "Town bridges" };
        // The garden edges (TvWorldBuilderYards): drawn near and far, they show where the gardens are
        yardMesh = new TvMesh { name = "Town gardens" };
    }

    // ---- The town's small things, made once and shared by every piece ----

    static readonly ConcurrentDictionary<int, TvShape> townShapes = new ConcurrentDictionary<int, TvShape>();

    enum Thing { Lamp, Bench, Bin, Tree, Car, Bike, Cafe, Planter, Pots, Sitter, Bollard, OldLamp }

    static TvShape Shape(Thing thing, int look, TvCityShapes.Style style)
    {
        int key = ((int)thing * 64 + look) * 2 + (int)style;
        return townShapes.GetOrAdd(key, _ =>
        {
            var r = new TvRandom(key * 7 + 3);
            switch (thing)
            {
                case Thing.Lamp: return TvCityShapes.StreetLamp(r, style == TvCityShapes.Style.OldStone ? -1 : look);
                case Thing.Bench: return TvCityShapes.Bench(r);
                case Thing.Bin: return TvCityShapes.Bin(r);
                case Thing.Tree: return style == TvCityShapes.Style.OldStone ? TvCityShapes.OrangeTree(r, look) : TvCityShapes.StreetTree(r, look);
                case Thing.Car: return TvCityShapes.Car(r, look);
                case Thing.Bike: return TvCityShapes.Bicycle(r, look);
                case Thing.Cafe: return TvCityShapes.CafeSet(r, look);
                case Thing.Planter: return TvCityShapes.Planter(r);
                case Thing.Pots: return TvCityShapes.PotCluster(r, look);
                case Thing.Sitter: return TvCityShapes.Person(r, TvCityShapes.Pose.Sit, look);
                case Thing.Bollard: return TvCityShapes.Bollard(r);
                default: return TvCityShapes.StreetLamp(r, -1);
            }
        });
    }

    // The 2D race shuts this spot (a wall, a fence, the course's tape or out-of-bounds): only known when the world is
    // built for running (RunYourself), else false
    bool ShutAt(float x, float z) => input.passable != null && input.passable.sprint && input.passable.Closed(input.centerE + x, input.centerN + z);

    // A car's place (4.6 x 1.9 m along the street's edge, which is towards (ex, ez)) and a metre round it on the
    // street's side are all open paving
    bool RoomForCar(float x, float z, float ex, float ez)
    {
        for (float a = -3.3f; a <= 3.31f; a += 1.1f)
            for (float c = -2.1f; c <= 0.51f; c += 0.65f)
            {
                float px = x - ez * a + ex * c, pz = z + ex * a + ez * c;
                if (Base(CellAt(px, pz)) != WorldCell.Paved || ShutAt(px, pz)) return false;
            }
        return true;
    }

    // ---- Streets: what stands on them, and the bridges, tunnels, stairs and canopies ----

    void TownStreets()
    {
        TownLines();
        EnsureTownMeshes();
        TownStructures();
        TownYards();
        var style = input.town.style;
        const float Step = 2f;
        // Spots on a lattice of whole map metres, so a spot is the same in every piece that sees it
        int i0 = (int)Math.Floor((-R + ox) / Step), i1 = (int)Math.Ceiling((R + ox) / Step);
        int j0 = (int)Math.Floor((-R + oz) / Step), j1 = (int)Math.Ceiling((R + oz) / Step);
        var d = new float[8];
        var hitHouse = new bool[8];
        int peopleHere = 0;
        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                float x = i * Step + 1f - ox, z = j * Step + 1f - oz;
                if (!Inside(x, z, 0f)) continue;
                if (Base(CellAt(x, z)) != WorldCell.Paved) continue;
                // Clear of the course's flags and the bridges, tunnels and stairs
                if (NearFlag(x, z, 4f)) continue;
                if (TownLineDist(x, z, 1) < 3f || TownLineDist(x, z, 2) < 3f || TownLineDist(x, z, 3) < 2.5f || TownLineDist(x, z, 4) < 2f) continue;
                if (InsideCanopy(x, z) || CanopyCell(x, z)) continue;
                // How far the paving reaches in 8 directions
                float min = 99f;
                int minDir = 0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 0.785398f, dx = (float)Math.Cos(a), dz = (float)Math.Sin(a);
                    d[k] = 14f;
                    hitHouse[k] = false;
                    for (float s = 0.7f; s < 14f; s += 0.7f)
                    {
                        int b = Base(CellAt(x + dx * s, z + dz * s));
                        if (b != WorldCell.Paved || ShutAt(x + dx * s, z + dz * s))
                        {
                            d[k] = s;
                            hitHouse[k] = b == WorldCell.Building;
                            break;
                        }
                    }
                    if (d[k] < min) { min = d[k]; minDir = k; }
                }
                float across = Math.Min(Math.Min(d[0] + d[4], d[2] + d[6]), Math.Min((d[1] + d[5]), (d[3] + d[7])));
                int acrossDir = d[0] + d[4] <= d[2] + d[6] ? 0 : 2;
                float ea = minDir * 0.785398f, ex = (float)Math.Cos(ea), ez = (float)Math.Sin(ea);   // towards the nearest edge
                bool wall = hitHouse[minDir];
                float h = TvRandom.Hash(i * 3 + 17, j * 5 - 11), h2 = TvRandom.Hash(i * 7 - 5, j * 3 + 29), h3 = TvRandom.Hash(i + 911, j - 433);
                int look = (int)(h3 * 61f);
                // Facing away from the edge (a bench looks out over the street): local -z towards (-ex, -ez)
                float faceOut = (float)Math.Atan2(ex, ez);
                // Along the edge: local +x along (-ez, ex)
                float alongEdge = (float)Math.Atan2(-ex, -ez);
                float y = H(x, z);

                // People first, so there are a few on every street
                if (peopleHere < 14 && min > 0.6f && min < 3f && h < 0.05f && Free(x, z, 1.2f))
                {
                    // Watchers, alone or a few together
                    int count = 1 + (int)(h2 * 2.6f);
                    for (int q = 0; q < count && peopleHere < 14; q++)
                    {
                        float px = x - ez * (q - (count - 1) / 2f) * 0.75f, pz = z + ex * (q - (count - 1) / 2f) * 0.75f;
                        int kind = (look + q) % 5 == 2 ? 2 : 0;
                        world.people.Add(px); world.people.Add(pz); world.people.Add(faceOut); world.people.Add(kind); world.people.Add((look * 7 + q * 13) % 24); world.people.Add(0f);
                        peopleHere++;
                    }
                    Mark(x, z, 1.2f, 9);
                    continue;
                }
                if (peopleHere < 14 && across >= 3f && min >= 1f && h > 0.97f && Free(x, z, 1f))
                {
                    // A walker going along the street, back and forth
                    float wa = (acrossDir == 0 ? 2 : 0) * 0.785398f, wx = (float)Math.Cos(wa), wz = (float)Math.Sin(wa);
                    float reach = Math.Min(Walk(x, z, wx, wz), Walk(x, z, -wx, -wz));
                    if (reach >= 3f)
                    {
                        float len = Math.Min(2f * reach - 1f, 22f);
                        world.people.Add(x); world.people.Add(z); world.people.Add((float)Math.Atan2(-wx, -wz)); world.people.Add(1f);
                        world.people.Add(look % 24); world.people.Add(len);
                        peopleHere++;
                        continue;
                    }
                }

                // Nothing that stands in the way in a lane narrower than 5 m (2026-11-20, Anders got stuck in a passage)
                bool roomy = across >= 5f;
                // At the street's edge: lamps, benches, bins, parked cars
                if (min >= 0.6f && min < 1.6f && roomy)
                {
                    if (h < 0.4f && FarFrom(x, z, 12f, 0) && Free(x, z, 1f))
                    {
                        townThingMesh.Add(Shape(Thing.Lamp, look % 5, style), x, y, z, 1f, 1f, 1f, alongEdge);
                        world.solids.Add(new TvSolid { x = x, z = z, r = 0.18f });
                        Mark(x, z, 0.5f, 0);
                        continue;
                    }
                    if (!wall && h > 0.55f && h < 0.75f && FarFrom(x, z, 14f, 1) && Free(x, z, 1.4f))
                    {
                        townThingMesh.Add(Shape(Thing.Bench, 0, style), x, y, z, 1f, 1f, 1f, faceOut);
                        world.solids.Add(new TvSolid { x = x, z = z, r = 0.55f });
                        if (h2 < 0.45f)
                        {
                            // Someone sitting on it (still: they are drawn with the street)
                            float off = h2 < 0.2f ? -0.4f : 0.4f;
                            townThingMesh.Add(Shape(Thing.Sitter, look % 24, style), x - ez * off, y, z + ex * off, 1f, 1f, 1f, faceOut);
                        }
                        Mark(x, z, 1.2f, 1);
                        continue;
                    }
                    if (h > 0.75f && h < 0.82f && FarFrom(x, z, 15f, 2) && Free(x, z, 0.8f))
                    {
                        townThingMesh.Add(Shape(Thing.Bin, 0, style), x, y, z, 1f, 1f, 1f, faceOut);
                        world.solids.Add(new TvSolid { x = x, z = z, r = 0.25f });
                        Mark(x, z, 0.5f, 2);
                        continue;
                    }
                    // A parked car only where the street is wide and open: at least 6 m free beside it, and nothing
                    // the runner can't pass (a wall, a fence, tape) under it (2026-11-22, Anders: a car blocked a
                    // narrow paved way between walls and tape, so nobody got past)
                    if (!Old && !wall && across >= 10f && d[(minDir + 4) % 8] >= 6f && h > 0.82f && h < 0.95f && FarFrom(x, z, 7f, 3) && Free(x, z, 3f)
                        && RoomForCar(x, z, ex, ez))
                    {
                        townThingMesh.Add(Shape(Thing.Car, look % 12, style), x, y, z, 1f, 1f, 1f, alongEdge + (h2 < 0.5f ? 0f : (float)Math.PI));
                        foreach (float s in new[] { -1.4f, 0f, 1.4f })
                            world.solids.Add(new TvSolid { x = x - ez * s, z = z + ex * s, r = 0.95f });
                        Mark(x, z, 2.6f, 3);
                        continue;
                    }
                }
                // By a house wall: bicycles, café tables, flowers
                if (wall && min < 2.6f && roomy)
                {
                    if (min < 1.3f && h > 0.4f && h < 0.5f && FarFrom(x, z, 4f, 4) && Free(x, z, 1f))
                    {
                        townThingMesh.Add(Shape(Thing.Bike, look % 8, style), x, y, z, 1f, 1f, 1f, alongEdge);
                        Mark(x, z, 1f, 4);
                        continue;
                    }
                    if (min >= 1.3f && across >= 7f && h > 0.5f && h < 0.6f && FarFrom(x, z, 5f, 5) && Free(x, z, 1.6f))
                    {
                        townThingMesh.Add(Shape(Thing.Cafe, look % 8, style), x, y, z, 1f, 1f, 1f, faceOut);
                        world.solids.Add(new TvSolid { x = x, z = z, r = 0.9f });
                        Mark(x, z, 1.6f, 5);
                        continue;
                    }
                    if (min < 1.3f && h > 0.6f && h < 0.7f && FarFrom(x, z, 5f, 6) && Free(x, z, 0.8f))
                    {
                        townThingMesh.Add(Old ? Shape(Thing.Pots, look % 6, style) : Shape(Thing.Planter, 0, style), x, y, z, 1f, 1f, 1f, faceOut);
                        world.solids.Add(new TvSolid { x = x, z = z, r = 0.4f });
                        Mark(x, z, 0.6f, 6);
                        continue;
                    }
                }
                // Trees on wide streets and squares
                if (min >= 3f && across >= 12f && h > 0.3f && h < 0.42f && FarFrom(x, z, 9f, 7) && Free(x, z, 2.5f) && !TownKeepOut(x, z, 3.2f))
                {
                    townThingMesh.Add(Shape(Thing.Tree, look % 4, style), x, y, z, 1f, 1f, 1f, h2 * 6.28f);
                    world.solids.Add(new TvSolid { x = x, z = z, r = 0.3f });
                    Mark(x, z, 1.5f, 7);
                    continue;
                }
                // The old town: washing on a line between the houses over a narrow lane
                if (Old && across >= 2.5f && across <= 7.5f && h > 0.85f && FarFrom(x, z, 7f, 8))
                {
                    int k0 = acrossDir, k1 = acrossDir + 4;
                    if (hitHouse[k0] && hitHouse[k1])
                    {
                        float a = k0 * 0.785398f, dx = (float)Math.Cos(a), dz = (float)Math.Sin(a);
                        float ax = x + dx * d[k0], az = z + dz * d[k0], bx = x - dx * d[k1], bz = z - dz * d[k1];
                        float mx = (ax + bx) / 2f, mz = (az + bz) / 2f, l = d[k0] + d[k1] + 0.4f;
                        random.Reseed(i, j, 4300);
                        townThingMesh.Add(TvCityShapes.WashingLine(random, l, look), mx, y + 4.2f + h2 * 1.4f, mz, 1f, 1f, 1f, (float)Math.Atan2(-dz, dx));
                        Mark(x, z, 0f, 8);
                    }
                }
            }
        }
    }

    bool NearFlag(float x, float z, float gap)
    {
        if (input.flags == null) return false;
        for (int i = 0; i + 1 < input.flags.Count; i += 2)
        {
            float dx = x - input.flags[i], dz = z - input.flags[i + 1];
            if (dx * dx + dz * dz < gap * gap) return true;
        }
        return false;
    }

    // Nothing else stands within gap (plus the other thing's own size)
    bool Free(float x, float z, float gap)
    {
        foreach (var p in townPlaced)
        {
            if (p.kind < 0) continue;
            float dx = x - p.x, dz = z - p.z, r = gap + p.r;
            if (dx * dx + dz * dz < r * r) return false;
        }
        return true;
    }

    // No other thing of the same kind within gap
    bool FarFrom(float x, float z, float gap, int kind)
    {
        foreach (var p in townPlaced)
        {
            if (p.kind != kind) continue;
            float dx = x - p.x, dz = z - p.z;
            if (dx * dx + dz * dz < gap * gap) return false;
        }
        return true;
    }

    void Mark(float x, float z, float r, int kind) => townPlaced.Add((x, z, r, kind));

    // How far one can walk from a spot on paving, straight on (at most 12 m)
    float Walk(float x, float z, float dx, float dz)
    {
        for (float s = 0.7f; s < 12f; s += 0.7f)
        {
            float px = x + dx * s, pz = z + dz * s;
            if (Base(CellAt(px, pz)) != WorldCell.Paved || !Inside(px, pz, 0f)) return s - 0.7f;
            // Keep a step off the edge
            if (Base(CellAt(px - dz * 0.8f, pz + dx * 0.8f)) != WorldCell.Paved || Base(CellAt(px + dz * 0.8f, pz - dx * 0.8f)) != WorldCell.Paved) return s - 0.7f;
        }
        return 12f;
    }

    // ---- Bridges, tunnels, stairs and canopies ----

    void TownStructures()
    {
        bool old = Old;
        TvColor concrete = old ? TvColor.Hex("#a69d8c") : TvColor.Hex("#9a9893"), dark = TvColor.Hex("#3b3936"),
            stepC = old ? TvColor.Hex("#a49c8e") : TvColor.Hex("#8f8d88");
        CanopyAreas();
        foreach (var l in townLines)
        {
            float[] c = l.xz;
            if (l.kind == 4)
            {
                Canopy(c);
                continue;
            }
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
                float dx = bx - ax, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 0.2f) continue;
                if (!Inside((ax + bx) / 2f, (az + bz) / 2f, 0f)) continue;
                dx /= len; dz /= len;
                float yaw = (float)Math.Atan2(-dz, dx);
                int parts = Math.Max(1, (int)Math.Ceiling(len / 1.5f));
                if (l.kind == 1)
                {
                    BridgePiece(l.w, l.flags, ax, az, bx, bz, len, dx, dz, yaw, q == 0, q + 4 >= c.Length);
                }
                else if (l.kind == 2)
                {
                    // A tunnel or an underpass: walls on both sides and a slab over it
                    float W = TunnelWidth(l.w);
                    const float Top = 2.8f;
                    for (int p = 0; p < parts; p++)
                    {
                        float t = (p + 0.5f) / parts, x = ax + dx * len * t, z = az + dz * len * t, y = H(x, z), pl = len / parts + 0.05f;
                        var b = new TvShapeBuilder(random);
                        foreach (float s in new[] { -1f, 1f })
                        {
                            b.Box(-pl / 2f, -0.4f, s * (W / 2f + 0.2f) - 0.2f, pl / 2f, Top, s * (W / 2f + 0.2f) + 0.2f, concrete);
                            world.solids.Add(new TvSolid { x = x + dz * s * (W / 2f + 0.25f), z = z - dx * s * (W / 2f + 0.25f), r = 0.3f });
                        }
                        b.Box(-pl / 2f, Top, -W / 2f - 0.6f, pl / 2f, Top + 0.6f, W / 2f + 0.6f, concrete.Shade(0.92f));
                        b.Box(-pl / 2f, Top - 0.05f, -W / 2f, pl / 2f, Top, W / 2f, dark);
                        townBuiltMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, yaw);
                    }
                }
                else if (l.kind == 3)
                {
                    // Stairs: a step every 0.35 m, each as high as the ground there, with low cheeks
                    float w = l.w > 0.5f ? l.w : 2f;
                    int steps = Math.Max(1, (int)(len / 0.35f));
                    for (int p = 0; p < steps; p++)
                    {
                        float t = (p + 0.5f) / steps, x = ax + dx * len * t, z = az + dz * len * t, y = H(x, z) + 0.06f, sl = len / steps + 0.02f;
                        var b = new TvShapeBuilder(random);
                        b.Box(-sl / 2f, -0.5f, -w / 2f, sl / 2f, 0f, w / 2f, stepC.Shade(p % 2 == 0 ? 1f : 0.94f));
                        b.Box(-sl / 2f, -0.5f, -w / 2f - 0.25f, sl / 2f, 0.3f, -w / 2f, concrete);
                        b.Box(-sl / 2f, -0.5f, w / 2f, sl / 2f, 0.3f, w / 2f + 0.25f, concrete);
                        townBuiltMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, yaw);
                    }
                }
            }
        }
    }

    // The bridges of a forest map (TvBridges.Forest): footbridges over streams and lakes, and the map's own bridges
    void ForestBridges()
    {
        TownLines();
        foreach (var l in townLines)
        {
            if (l.kind != 1) continue;
            float[] c = l.xz;
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
                float dx = bx - ax, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 0.2f || !Inside((ax + bx) / 2f, (az + bz) / 2f, 0f)) continue;
                dx /= len; dz /= len;
                BridgePiece(l.w, l.flags, ax, az, bx, bz, len, dx, dz, (float)Math.Atan2(-dz, dx), q == 0, q + 4 >= c.Length);
            }
        }
    }

    // One straight stretch of a bridge, in short level pieces that follow the ramp between its ends (TownHeights).
    // A paved way: a concrete bridge with an asphalt deck, railings or stone parapets and piers at the ends. A path:
    // a wooden footbridge of planks on two beams, with a handrail when it crosses a lake, posts down into the water.
    // Between fences or walls (TvBridges.Fenced) the fences are its sides and it has no railings of its own.
    void BridgePiece(float w, int flags, float ax, float az, float bx, float bz, float len, float dx, float dz, float yaw, bool first, bool last)
    {
        if (townBuiltMesh == null) townBuiltMesh = new TvMesh { name = "Town bridges" };
        bool old = Old, wood = (flags & TvBridges.Wood) != 0, fenced = (flags & TvBridges.Fenced) != 0, lake = (flags & TvBridges.OverWater) != 0;
        float W = BridgeWidth(w);
        if (wood)
        {
            TvColor plank = TvColor.Hex("#8a6a48"), beam = TvColor.Hex("#5a4632"), post = TvColor.Hex("#6b5038");
            int planks = Math.Max(1, (int)Math.Round(len / 0.26f));
            float pl = len / planks;
            // In pieces of about 1.5 m, each level at its own height
            int per = Math.Max(1, (int)Math.Round(1.5f / pl));
            for (int p0 = 0; p0 < planks; p0 += per)
            {
                int count = Math.Min(per, planks - p0);
                float t = (p0 + count / 2f) / planks, x = ax + dx * len * t, z = az + dz * len * t, y = DeckAt(x, z);
                float half = count * pl / 2f;
                var b = new TvShapeBuilder(random);
                // Two beams under the planks
                foreach (float s in new[] { -1f, 1f })
                    b.Box(-half - 0.01f, -0.32f, s * (W / 2f - 0.18f) - 0.08f, half + 0.01f, -0.06f, s * (W / 2f - 0.18f) + 0.08f, beam);
                for (int k = 0; k < count; k++)
                {
                    float x0 = -half + k * pl;
                    float h = TvRandom.Hash(p0 + k + (int)(ax * 7f), (int)(az * 7f));
                    b.Box(x0 + 0.015f, -0.06f, -W / 2f, x0 + pl - 0.015f, 0.03f + h * 0.015f, W / 2f, plank.Shade(0.88f + h * 0.22f));
                }
                if (lake && !fenced)
                {
                    // A handrail on posts on both sides
                    foreach (float s in new[] { -1f, 1f })
                    {
                        b.Box(-0.05f, -0.1f, s * W / 2f - 0.05f, 0.05f, 1.0f, s * W / 2f + 0.05f, post);
                        b.Box(-half - 0.01f, 0.92f, s * W / 2f - 0.04f, half + 0.01f, 1.0f, s * W / 2f + 0.04f, post.Shade(1.1f));
                    }
                }
                if (lake)
                {
                    // Posts down into the water
                    foreach (float s in new[] { -1f, 1f })
                        b.Box(-0.08f, -1.6f, s * (W / 2f - 0.18f) - 0.08f, 0.08f, -0.3f, s * (W / 2f - 0.18f) + 0.08f, beam.Shade(0.85f));
                }
                townBuiltMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, yaw);
            }
            return;
        }
        TvColor deckC = old ? TvColor.Hex("#9d968a") : AsphaltA, rail = old ? TvColor.Hex("#b9b1a1") : TvColor.Hex("#3f5e48"),
            concrete = old ? TvColor.Hex("#a69d8c") : TvColor.Hex("#9a9893"), dark = TvColor.Hex("#3b3936");
        int parts = Math.Max(1, (int)Math.Ceiling(len / 1.5f));
        for (int p = 0; p < parts; p++)
        {
            float t = (p + 0.5f) / parts, x = ax + dx * len * t, z = az + dz * len * t, y = DeckAt(x, z), pl = len / parts + 0.05f;
            var b = new TvShapeBuilder(random);
            b.Box(-pl / 2f, -0.3f, -W / 2f, pl / 2f, 0.03f, W / 2f, deckC);
            b.Box(-pl / 2f, -0.55f, -W / 2f + 0.2f, pl / 2f, -0.3f, W / 2f - 0.2f, dark);
            if (!fenced)
                foreach (float s in new[] { -1f, 1f })
                {
                    if (old)
                    {
                        b.Box(-pl / 2f, -0.35f, s * W / 2f - 0.17f, pl / 2f, 0.95f, s * W / 2f + 0.17f, rail);
                        b.Box(-pl / 2f - 0.02f, 0.95f, s * W / 2f - 0.21f, pl / 2f + 0.02f, 1.05f, s * W / 2f + 0.21f, rail.Shade(1.08f));
                    }
                    else
                    {
                        b.Box(-0.04f, 0.03f, s * W / 2f - 0.04f, 0.04f, 1.05f, s * W / 2f + 0.04f, rail);
                        b.Box(-pl / 2f, 1.0f, s * W / 2f - 0.04f, pl / 2f, 1.08f, s * W / 2f + 0.04f, rail);
                        b.Box(-pl / 2f, 0.5f, s * W / 2f - 0.02f, pl / 2f, 0.55f, s * W / 2f + 0.02f, rail);
                    }
                }
            else
                // Low concrete edges under the fences
                foreach (float s in new[] { -1f, 1f })
                    b.Box(-pl / 2f, -0.3f, s * W / 2f - 0.12f, pl / 2f, 0.12f, s * W / 2f + 0.12f, concrete);
            townBuiltMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, yaw);
        }
        if (first) BridgePier(ax, az, yaw, W, concrete);
        if (last) BridgePier(bx, bz, yaw, W, concrete);
    }

    void BridgePier(float x, float z, float yaw, float w, TvColor color)
    {
        float y = DeckAt(x, z);
        var b = new TvShapeBuilder(random);
        b.Box(-0.5f, -2.5f, -w / 2f - 0.3f, 0.5f, -0.25f, w / 2f + 0.3f, color);
        townBuiltMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, yaw);
    }

    // The light grey canopy areas of the map that no closed outline covers (2026-11-20, Anders: a passage under a
    // shopping centre was open sky). The cells of the 2D grid, in rows of 2 m cells. A canopy that touches a house is
    // a passage under the house: a deck of concrete over it from 3 m up, dark underneath. One standing alone is a roof
    // on posts.
    void CanopyAreas()
    {
        if (input.passable == null || !input.passable.sprint) return;
        var cells = new List<(int i, int j, double e, double n)>();
        // Wider than the piece, so a passage reaching into the next piece is the same passage (look, height) in both
        input.passable.CanopyCells(input.centerE - R - 40, input.centerN - R - 40, input.centerE + R + 40, input.centerN + R + 40, cells);
        if (cells.Count == 0) return;
        float size = (float)input.passable.CellSize, half = size / 2f + 0.03f;
        var at = new Dictionary<long, int>();
        long Key(int i, int j) => (long)i * 100000 + j;
        for (int k = 0; k < cells.Count; k++) at[Key(cells[k].i, cells[k].j)] = k;
        var comp = new int[cells.Count];
        for (int k = 0; k < comp.Length; k++) comp[k] = -1;
        int comps = 0;
        var stack = new Stack<int>();
        for (int k = 0; k < cells.Count; k++)
        {
            if (comp[k] >= 0) continue;
            comp[k] = comps;
            stack.Push(k);
            while (stack.Count > 0)
            {
                var c = cells[stack.Pop()];
                foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (at.TryGetValue(Key(c.i + di, c.j + dj), out int q) && comp[q] < 0) { comp[q] = comps; stack.Push(q); }
            }
            comps++;
        }
        var lowest = new float[comps];
        var highest = new float[comps];
        var byHouse = new bool[comps];
        var count = new int[comps];
        // From the first cell's own height (2026-11-23: they started at 0, so on ground above y = 0 the roof of a
        // bicycle shelter came down to head height)
        for (int q = 0; q < comps; q++) { lowest[q] = float.MaxValue; highest[q] = float.MinValue; }
        for (int k = 0; k < cells.Count; k++)
        {
            float x = (float)(cells[k].e - input.centerE), z = (float)(cells[k].n - input.centerN);
            int q = comp[k];
            float h = H(x, z);
            lowest[q] = Math.Min(lowest[q], h);
            highest[q] = Math.Max(highest[q], h);
            count[q]++;
            if (!byHouse[q])
                foreach (var (dx, dz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
                    if (Base(CellAt(x + dx * (size * 0.5f + 0.8f), z + dz * (size * 0.5f + 0.8f))) == WorldCell.Building) { byHouse[q] = true; break; }
        }
        bool old = Old;
        TvColor under = TvColor.Hex("#3e3a36"), concrete = old ? TvColor.Hex("#d8cfbd") : TvColor.Hex("#b7b4ad"), post = old ? TvColor.Hex("#5b3a26") : TvColor.Hex("#3c3f42"),
            roof = old ? TvColor.Hex("#b4663f") : TvColor.Hex("#6d7476");
        // Rows of cells along the columns, each row one box; a cell inside a closed outline is that canopy's own
        var order = new List<int>();
        for (int k = 0; k < cells.Count; k++) order.Add(k);
        order.Sort((a, b) => cells[a].i != cells[b].i ? cells[a].i.CompareTo(cells[b].i) : cells[a].j.CompareTo(cells[b].j));
        var builders = new TvShapeBuilder[comps];
        for (int s = 0; s < order.Count;)
        {
            int k = order[s], e = s;
            while (e + 1 < order.Count && cells[order[e + 1]].i == cells[k].i && cells[order[e + 1]].j == cells[order[e]].j + 1
                   && comp[order[e + 1]] == comp[k]) e++;
            var first = cells[k];
            var last = cells[order[e]];
            int runStart = s;
            s = e + 1;
            float x0 = (float)(first.e - input.centerE) - half, x1 = (float)(last.e - input.centerE) + half;
            float z0 = (float)(first.n - input.centerN) - half, z1 = (float)(first.n - input.centerN) + half;
            float mx = (x0 + x1) / 2f, mz = (z0 + z1) / 2f;
            if (!Inside(mx, mz, 0f) || InsideCanopy(mx, mz)) continue;
            int c = comp[k];
            if (count[c] < 2) continue;
            var b = builders[c] ?? (builders[c] = new TvShapeBuilder(random));
            float y = lowest[c];
            if (byHouse[c])
            {
                // A deck of concrete over the passage, level with the lowest end, from 3 m to 4.2 m above it
                float slab = highest[c] - y;
                b.Box(x0, 3.0f + slab, z0, x1, 4.2f + slab, z1, concrete);
                b.Box(x0, 2.92f + slab, z0, x1, 3.0f + slab, z1, under);
            }
            else
            {
                // Each row's roof at least 2.6 m over its own highest ground (a long shelter on a slope)
                float rowHigh = float.MinValue;
                for (int q = runStart; q <= e; q++)
                {
                    var rc = cells[order[q]];
                    rowHigh = Math.Max(rowHigh, H((float)(rc.e - input.centerE), (float)(rc.n - input.centerN)));
                }
                float roofAt = Math.Max(2.9f, rowHigh - y + 2.6f);
                b.Box(x0 - 0.1f, roofAt, z0 - 0.1f, x1 + 0.1f, roofAt + 0.2f, z1 + 0.1f, roof);
                // Posts on the edge of the roof, about every 4 m
                for (int q = runStart; q <= e; q++)
                {
                    var cell = cells[order[q]];
                    if (cell.i % 2 != 0 || cell.j % 2 != 0) continue;
                    bool edge = !at.ContainsKey(Key(cell.i + 1, cell.j)) || !at.ContainsKey(Key(cell.i - 1, cell.j))
                                || !at.ContainsKey(Key(cell.i, cell.j + 1)) || !at.ContainsKey(Key(cell.i, cell.j - 1));
                    if (!edge) continue;
                    float px = (float)(cell.e - input.centerE), pz = (float)(cell.n - input.centerN);
                    b.Box(px - 0.08f, H(px, pz) - y - 0.3f, pz - 0.08f, px + 0.08f, roofAt, pz + 0.08f, post);
                    world.solids.Add(new TvSolid { x = px, z = pz, r = 0.15f });
                }
            }
        }
        for (int c = 0; c < comps; c++)
            if (builders[c] != null) townBuiltMesh.Add(builders[c].Done(), 0f, lowest[c], 0f, 1f, 1f, 1f, 0f);
    }

    // A canopy: a roof on posts, or, between houses, the house over a passage
    void Canopy(float[] c)
    {
        var pts = new List<(float x, float z)>();
        for (int i = 0; i + 1 < c.Length; i += 2) pts.Add((c[i], c[i + 1]));
        float mx = 0f, mz = 0f;
        foreach (var p in pts) { mx += p.x; mz += p.z; }
        mx /= pts.Count; mz /= pts.Count;
        if (!Inside(mx, mz, 0f)) return;
        // The rectangle along the outline's long side
        float sxx = 0f, szz = 0f, sxz = 0f;
        foreach (var p in pts) { float dx = p.x - mx, dz = p.z - mz; sxx += dx * dx; szz += dz * dz; sxz += dx * dz; }
        float angle = 0.5f * (float)Math.Atan2(2f * sxz, sxx - szz), ux = (float)Math.Cos(angle), uz = (float)Math.Sin(angle);
        float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
        foreach (var p in pts)
        {
            float dx = p.x - mx, dz = p.z - mz, u = dx * ux + dz * uz, v = -dx * uz + dz * ux;
            u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
        }
        float len = u1 - u0, wid = v1 - v0;
        if (len < 1f || wid < 1f || len > 80f || wid > 80f) return;
        float cx = mx + ux * (u0 + u1) / 2f - uz * (v0 + v1) / 2f, cz = mz + uz * (u0 + u1) / 2f + ux * (v0 + v1) / 2f;
        float lowest = float.MaxValue, highest = float.MinValue;
        foreach (var p in pts) { float h = H(p.x, p.z); lowest = Math.Min(lowest, h); highest = Math.Max(highest, h); }
        // The ground under it, not only at its corners (a long one on a slope)
        for (float su = -0.5f; su <= 0.5f; su += 0.125f)
            for (float sv = -0.4f; sv <= 0.4f; sv += 0.4f)
            {
                float h = H(cx + ux * su * (u1 - u0) - uz * sv * (v1 - v0), cz + uz * su * (u1 - u0) + ux * sv * (v1 - v0));
                lowest = Math.Min(lowest, h);
                highest = Math.Max(highest, h);
            }
        // Room to run under the roof everywhere (2026-11-23, Anders: a long bicycle shelter's roof was below head height)
        float roofAt = Math.Max(2.9f, highest - lowest + 2.6f);
        // Houses on two sides: a passage under a house
        int houses = 0;
        foreach (float s in new[] { -1f, 1f })
        {
            float x = cx - uz * s * (wid / 2f + 1.5f), z = cz + ux * s * (wid / 2f + 1.5f);
            float x2 = cx + ux * s * (len / 2f + 1.5f), z2 = cz + uz * s * (len / 2f + 1.5f);
            if (Base(CellAt(x, z)) == WorldCell.Building) houses++;
            if (Base(CellAt(x2, z2)) == WorldCell.Building) houses++;
        }
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f;
        int look = (int)(TvRandom.Hash((int)Math.Floor(cx + ox), (int)Math.Floor(cz + oz)) * 97f);
        if (houses >= 2)
        {
            TvColor wall = Old ? TvColor.Hex("#ece6d6") : TvCityShapes.Facades[look % TvCityShapes.Facades.Length];
            float top = Old ? 6.2f : 7.1f;
            b.Box(-hl, 3.0f, -hw, hl, top, hw, wall);
            b.Box(-hl, 2.92f, -hw, hl, 3.0f, hw, TvColor.Hex("#3e3a36"));
            b.Box(-hl - 0.2f, top, -hw - 0.2f, hl + 0.2f, top + 0.35f, hw + 0.2f, Old ? TvColor.Hex("#b85a3a") : TvColor.Hex("#4e5254"));
            // With the bridges, not the houses: the houses near the runner are swapped for the full ones of the
            // detail build, which has no canopies, so the house over the passage went with them (2026-11-20)
            townBuiltMesh.Add(b.Done(), cx, lowest, cz, 1f, 1f, 1f, -angle);
            return;
        }
        TvColor post = Old ? TvColor.Hex("#5b3a26") : TvColor.Hex("#3c3f42"), roof = Old ? TvColor.Hex("#b4663f") : TvColor.Hex("#6d7476");
        int alongPosts = Math.Max(1, (int)Math.Round(len / 4f));
        for (int i = 0; i <= alongPosts; i++)
        {
            float u = -hl + 0.15f + (len - 0.3f) * i / alongPosts;
            foreach (float s in new[] { -1f, 1f })
            {
                float v = s * (hw - 0.15f);
                b.Box(u - 0.08f, 0f, v - 0.08f, u + 0.08f, roofAt, v + 0.08f, post);
                world.solids.Add(new TvSolid { x = cx + ux * u - uz * v, z = cz + uz * u + ux * v, r = 0.15f });
            }
        }
        b.Box(-hl - 0.2f, roofAt, -hw - 0.2f, hl + 0.2f, roofAt + 0.2f, hw + 0.2f, roof);
        townBuiltMesh.Add(b.Done(), cx, lowest, cz, 1f, 1f, 1f, -angle);
        // A canopy standing alone on the paving is most often a bicycle shelter (2026-11-20, Anders): a row of bikes
        // under it along its back
        if (len >= 2.5f && wid >= 1.6f)
        {
            int look2 = look;
            for (float u = -hl + 0.5f; u <= hl - 0.4f; u += 0.75f)
            {
                look2 = look2 * 7 + 3;
                if (TvRandom.Hash((int)Math.Floor((cx + ox) * 3f + u * 5f), (int)Math.Floor((cz + oz) * 3f)) < 0.25f) continue;   // a gap now and then
                float v = -hw + 0.95f, x = cx + ux * u - uz * v, z = cz + uz * u + ux * v;
                townThingMesh.Add(Shape(Thing.Bike, Math.Abs(look2) % 8, input.town.style), x, H(x, z), z, 1f, 1f, 1f, -angle);
            }
        }
    }

    // ---- Keeping it: the paved ground as a mesh of its own (asphalt or cobble grain), and the town's meshes ----

    // A new corner in "into": the ground corners a and b mixed by t (position and normal), coloured as corner "colour"
    static int CopyVertex(TvMesh from, int a, TvMesh into, int b, int colour, float t)
    {
        List<float> p = from.positions, n = from.normals;
        float x = p[a * 3] + (p[b * 3] - p[a * 3]) * t, y = p[a * 3 + 1] + (p[b * 3 + 1] - p[a * 3 + 1]) * t, z = p[a * 3 + 2] + (p[b * 3 + 2] - p[a * 3 + 2]) * t;
        float nx = n[a * 3] + (n[b * 3] - n[a * 3]) * t, ny = n[a * 3 + 1] + (n[b * 3 + 1] - n[a * 3 + 1]) * t, nz = n[a * 3 + 2] + (n[b * 3 + 2] - n[a * 3 + 2]) * t;
        float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (nl < 1e-5f) { nx = 0f; ny = 1f; nz = 0f; nl = 1f; }
        var c = new TvColor(from.colors[colour * 3], from.colors[colour * 3 + 1], from.colors[colour * 3 + 2]);
        return into.Vertex(x, y, z, nx / nl, ny / nl, nz / nl, c);
    }

    void KeepTown()
    {
        if (pavedCorner != null && groundMesh != null)
        {
            pavedMesh = new TvMesh { name = "Ground paved", castShadows = false, group = TvMesh.Near };
            var map = new int[N * N];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var kept = new List<int>(groundMesh.triangles.Count);
            var t = groundMesh.triangles;
            float Lv(int v) => paveLevel != null ? paveLevel[v] : (pavedCorner[v] ? 1f : -1f);
            int PavedVertex(int v)
            {
                if (map[v] < 0) map[v] = CopyVertex(groundMesh, v, pavedMesh, v, v, 0f);
                return map[v];
            }
            // Each triangle whole on one side, or cut where the level is 0: the part inside the paving to the paved
            // mesh, the rest stays ground. A new corner on the cut takes the colour of the side it is drawn with.
            int baseCount = N * N;
            var poly = new List<(int v, int a, int b, float t)>(5);
            for (int q = 0; q + 2 < t.Count; q += 3)
            {
                int v0 = t[q], v1 = t[q + 1], v2 = t[q + 2];
                if (v0 >= baseCount || v1 >= baseCount || v2 >= baseCount) { kept.Add(v0); kept.Add(v1); kept.Add(v2); continue; }
                float l0 = Lv(v0), l1 = Lv(v1), l2 = Lv(v2);
                bool p0 = l0 >= 0f, p1 = l1 >= 0f, p2 = l2 >= 0f;
                if (!p0 && !p1 && !p2) { kept.Add(v0); kept.Add(v1); kept.Add(v2); continue; }
                if (p0 && p1 && p2)
                {
                    pavedMesh.triangles.Add(PavedVertex(v0)); pavedMesh.triangles.Add(PavedVertex(v1)); pavedMesh.triangles.Add(PavedVertex(v2));
                    continue;
                }
                int[] vs = { v0, v1, v2 };
                float[] ls = { l0, l1, l2 };
                for (int side = 0; side < 2; side++)
                {
                    bool wantPaved = side == 0;
                    poly.Clear();
                    for (int e = 0; e < 3; e++)
                    {
                        int a = vs[e], b = vs[(e + 1) % 3];
                        float la = ls[e], lb = ls[(e + 1) % 3];
                        if ((la >= 0f) == wantPaved) poly.Add((a, a, a, 0f));
                        if ((la >= 0f) != (lb >= 0f))
                        {
                            float tt = la / (la - lb);
                            poly.Add((-1, a, b, Math.Max(0.02f, Math.Min(0.98f, tt))));
                        }
                    }
                    if (poly.Count < 3) continue;
                    TvMesh into = wantPaved ? pavedMesh : groundMesh;
                    var ids = new int[poly.Count];
                    for (int m = 0; m < poly.Count; m++)
                    {
                        var (v, a, b, tt) = poly[m];
                        if (v >= 0) ids[m] = wantPaved ? PavedVertex(v) : v;
                        else
                        {
                            // The colour from the end on this side
                            int own = ((Lv(a) >= 0f) == wantPaved) ? a : b;
                            ids[m] = CopyVertex(groundMesh, a, into, b, own, tt);
                        }
                    }
                    var list = wantPaved ? pavedMesh.triangles : kept;
                    for (int m = 1; m + 1 < ids.Length; m++) { list.Add(ids[0]); list.Add(ids[m]); list.Add(ids[m + 1]); }
                }
            }
            groundMesh.triangles.Clear();
            groundMesh.triangles.AddRange(kept);
            Keep(pavedMesh);
        }
        if (townMesh != null)
        {
            Keep(townMesh);
            Keep(townFarMesh);
            Keep(townThingMesh);
            Keep(townBuiltMesh);
            Keep(yardMesh);
        }
    }
}
