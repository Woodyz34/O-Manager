using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MapImport;

// The edges of the gardens in a sprint town (2026-11-23, Anders, after the sketches in
// /mnt/project-files/run-yourself/tomter-hus: "Jag tycker det ska exakt vara så här"). A garden (olive green on the
// map) is out of bounds; before, red and white tape ran round it along the zigzag of the 2 m cells. Now its edge
// towards a street, a path or a park is what a Swedish garden has there: a clipped hedge, a low hedge, a white or
// coloured picket fence (now and then between brick posts), a stone wall, garden blocks with a flower bed or gravel
// and shrubs behind, a row of big stones, a plank screen, a flower bed, or only an edging of stones round an open lawn.
//  - The edge runs straight along the map's own outline of the garden (TvGardens, maps built from 2026-11-23); on a
//    map built before, along the straightened borders of its cells (TvOutlines).
//  - The kind changes from garden to garden (a 20 m lattice of the map, the same in every piece); a garden by a big
//    house (a row of terraced houses, a block of flats) is mostly open lawn, low hedges and flower beds; a gap is
//    left now and then for the drive, with a mailbox beside it.
//  - Every 2 m stretch is built on the ground where it stands, its foot sloping with the ground, so it follows a slope.
// The runner stops at the edge: RunTerrain keeps them out of the garden by its outline.
public partial class TvWorldBuilder
{
    TvMesh yardMesh;

    enum Yard { Open, Hedge, LowHedge, Picket, PicketColour, PicketBrick, Stone, BlocksBed, BlocksGravel, Boulders, Plank, Bed }

    static readonly (Yard kind, int weight)[] VillaYards =
    {
        (Yard.Hedge, 22), (Yard.LowHedge, 8), (Yard.Picket, 12), (Yard.PicketColour, 5), (Yard.PicketBrick, 4), (Yard.Stone, 8),
        (Yard.BlocksBed, 10), (Yard.BlocksGravel, 6), (Yard.Boulders, 6), (Yard.Plank, 7), (Yard.Bed, 4), (Yard.Open, 8)
    };
    // By a row of terraced houses or a block of flats: mostly an open lawn (Anders: "om det är radhus så är det bättre
    // att ha gräsmatta")
    static readonly (Yard kind, int weight)[] RowYards = { (Yard.Open, 40), (Yard.LowHedge, 25), (Yard.Bed, 20), (Yard.Hedge, 10), (Yard.Plank, 5) };
    static readonly (Yard kind, int weight)[] OldYards = { (Yard.Stone, 40), (Yard.Open, 30), (Yard.Hedge, 15), (Yard.Bed, 15) };

    static readonly TvColor HedgeA = TvColor.Hex("#3b6328"), HedgeB = TvColor.Hex("#4d7631"), HedgeTop = TvColor.Hex("#5a8436"),
        PicketWhite = TvColor.Hex("#f1efe8"), BrickPost = TvColor.Hex("#8a4a3a"), StoneA = TvColor.Hex("#9b958c"), StoneB = TvColor.Hex("#7e7a72"),
        BlockA = TvColor.Hex("#a39a8c"), BlockB = TvColor.Hex("#8f877a"), Bark = TvColor.Hex("#5a3e2c"), YardGravel = TvColor.Hex("#8c8781"),
        PlankA = TvColor.Hex("#8a7a66"), PlankB = TvColor.Hex("#6e6152"), Edging = TvColor.Hex("#a8a59f");
    static readonly TvColor[] PicketColours = { TvColor.Hex("#8f2f2a"), TvColor.Hex("#2f4a3a"), TvColor.Hex("#2c3e56"), TvColor.Hex("#d9c27a") };
    static readonly TvColor[] YardFlowers =
        { TvColor.Hex("#e23d5a"), TvColor.Hex("#f2c230"), TvColor.Hex("#f4f0f2"), TvColor.Hex("#c04fb0"), TvColor.Hex("#ff8fb0"), TvColor.Hex("#e86a2a") };

    const float YardUnit = 2f, YardLattice = 20f;

    // Small shapes of the garden edges, made once and shared by every piece
    static readonly ConcurrentDictionary<int, TvShape> yardShapes = new ConcurrentDictionary<int, TvShape>();

    // 0 lump on a hedge, 1 round shrub, 2 flowering plant, 3 big stone, 4 mailbox, 5 thuja
    static TvShape YardShape(int what, int look)
    {
        look = (look % 16 + 16) % 16;
        int key = what * 16 + look % 16;
        return yardShapes.GetOrAdd(key, _ =>
        {
            var r = new TvRandom(key * 31 + 7);
            var b = new TvShapeBuilder(r);
            switch (what)
            {
                case 0:
                    b.Blob(0.42f, 1.15f, 0.55f, 1f, 0f, 0f, 0f, 0.12f, 0, true, look % 2 == 0 ? HedgeB : HedgeA, HedgeTop, -0.2f, 0.25f);
                    break;
                case 1:
                    b.Blob(0.4f, 1f, 0.85f, 1f, 0f, 0.3f, 0f, 0.18f, 0, true, look % 3 == 0 ? TvColor.Hex("#2f5a26") : TvColor.Hex("#4c7d34"), HedgeTop, 0f, 0.7f);
                    break;
                case 2:
                    b.Blob(0.22f, 1f, 0.8f, 1f, 0f, 0.18f, 0f, 0.2f, 0, true, TvColor.Hex("#4c7d34"));
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * 1.57f + look;
                        b.Box((float)Math.Cos(a) * 0.15f - 0.05f, 0.32f, (float)Math.Sin(a) * 0.15f - 0.05f,
                              (float)Math.Cos(a) * 0.15f + 0.05f, 0.42f, (float)Math.Sin(a) * 0.15f + 0.05f, YardFlowers[(look + k) % YardFlowers.Length]);
                    }
                    break;
                case 3:
                    b.Rock(0.42f + (look % 4) * 0.05f, 0.3f + (look % 3) * 0.05f, 0.38f, 0.12f, 0, 2, 0.25f,
                           look % 2 == 0 ? TvColor.Hex("#9a948a") : TvColor.Hex("#8a8278"), TvColor.Hex("#6f7a4a"), 0.1f, 0.2f);
                    break;
                case 4:
                    b.Box(-0.03f, 0f, -0.03f, 0.03f, 1.0f, 0.03f, TvColor.Hex("#3a3a3a"));
                    b.Box(-0.22f, 1.0f, -0.11f, 0.22f, 1.25f, 0.11f, new[] { TvColor.Hex("#2c3e56"), TvColor.Hex("#a43a2e"), TvColor.Hex("#e9e5da"), TvColor.Hex("#3b3f45") }[look % 4]);
                    break;
                default:
                    b.Cone(0.55f, 2.2f, 0f, 7, TvColor.Hex("#2f5a2a"));
                    break;
            }
            return b.Done();
        });
    }

    void TownYards()
    {
        if (input.town == null || layer == null) return;
        EnsureTownMeshes();
        TvGardens gardens = input.town.gardens;
        const float Pad = 24f;
        var lines = new List<float[]>();   // in the piece's metres, the garden on the left
        if (gardens != null)
        {
            foreach (float[] ring in gardens.Outlines(cE - R - Pad, cN - R - Pad, cE + R + Pad, cN + R + Pad, WorldAreaKind.Garden))
            {
                var local = new float[ring.Length];
                for (int i = 0; i + 1 < ring.Length; i += 2)
                {
                    local[i] = (float)(ring[i] - input.centerE);
                    local[i + 1] = (float)(ring[i + 1] - input.centerN);
                }
                lines.Add(local);
            }
        }
        else
        {
            // A map built before 2026-11-23: the borders of its garden cells, straightened
            int i0 = (int)Math.Floor((input.centerE - R - Pad - layer.minE) / layer.cell), i1 = (int)Math.Floor((input.centerE + R + Pad - layer.minE) / layer.cell);
            int j0 = (int)Math.Floor((input.centerN - R - Pad - layer.minN) / layer.cell), j1 = (int)Math.Floor((input.centerN + R + Pad - layer.minN) / layer.cell);
            bool Plot(int i, int j) => (layer.cells[j * layer.cols + i] & WorldCell.BaseMask) == WorldCell.Plot;
            bool Open(int i, int j)
            {
                int b = layer.cells[j * layer.cols + i] & WorldCell.BaseMask;
                return b != WorldCell.Plot && b != WorldCell.Building && b != WorldCell.Water && b != WorldCell.Impassable;
            }
            foreach (var line in TvOutlines.CellBorders(layer, i0, j0, i1, j1, Plot, Open))
            {
                if (line.Count < 2) continue;
                var local = new float[line.Count * 2];
                for (int k = 0; k < line.Count; k++)
                {
                    local[2 * k] = (float)(line[k].e - input.centerE);
                    local[2 * k + 1] = (float)(line[k].n - input.centerN);
                }
                // Turn it so the garden is on its left
                float ax = local[0], az = local[1], bx = local[2], bz = local[3];
                float l = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                if (l > 1e-3f)
                {
                    float mx = (ax + bx) / 2f - (bz - az) / l * 1.0f, mz = (az + bz) / 2f + (bx - ax) / l * 1.0f;
                    if (Base(CellAt(mx, mz)) != WorldCell.Plot) Array.Reverse(local, 0, local.Length);
                    // Reversing a list of (x, z) pairs swaps x and z within each pair: swap them back
                    if (Base(CellAt(mx, mz)) != WorldCell.Plot)
                        for (int k = 0; k + 1 < local.Length; k += 2) { float t = local[k]; local[k] = local[k + 1]; local[k + 1] = t; }
                }
                lines.Add(local);
            }
        }
        var context = new Dictionary<long, bool>();
        foreach (float[] line in lines) YardLine(line, gardens, context);
    }

    // Ground a runner may be on (a street, a path, a lawn of a park), not a garden, a house or water
    bool YardFacing(float x, float z, TvGardens gardens)
    {
        int b = Base(CellAt(x, z));
        if (b == WorldCell.Building || b == WorldCell.Water || b == WorldCell.Impassable) return false;
        if (gardens != null)
        {
            if (gardens.Inside(input.centerE + x, input.centerN + z)) return false;
            return true;
        }
        return b != WorldCell.Plot;
    }

    // One outline: 2 m at a time from its start, each stretch built by the piece its middle is in
    void YardLine(float[] p, TvGardens gardens, Dictionary<long, bool> context)
    {
        int n = p.Length / 2;
        for (int k = 0; k + 1 < n; k++)
        {
            float ax = p[2 * k], az = p[2 * k + 1], bx = p[2 * k + 2], bz = p[2 * k + 3];
            float dx = bx - ax, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.3f) continue;
            float ux = dx / len, uz = dz / len, lx = -uz, lz = ux;   // along; to the left (into the garden)
            int units = Math.Max(1, (int)Math.Round(len / YardUnit));
            float step = len / units;
            for (int u = 0; u < units; u++)
            {
                float s0 = u * step, s1 = s0 + step;
                float mx = ax + ux * (s0 + s1) / 2f, mz = az + uz * (s0 + s1) / 2f;
                if (!Inside(mx, mz, 0f)) continue;
                // Only where the garden meets open ground, and not along a house's wall
                if (!YardFacing(mx - lx * 1.2f, mz - lz * 1.2f, gardens)) continue;
                if (Base(CellAt(mx + lx * 0.8f, mz + lz * 0.8f)) == WorldCell.Building) continue;
                if (TownKeepOut(mx, mz, 0.6f)) continue;
                YardUnitAt(ax + ux * s0, az + uz * s0, ax + ux * s1, az + uz * s1, ux, uz, lx, lz, step, context);
            }
        }
    }

    // The kind of edge of the garden round a place: from the 20 m lattice square it is in
    Yard YardKindAt(float x, float z, float lx, float lz, Dictionary<long, bool> context, out int look, out bool gapHere)
    {
        int gi = (int)Math.Floor((x + ox) / YardLattice), gj = (int)Math.Floor((z + oz) / YardLattice);
        float h = TvRandom.Hash(gi * 3 + 7001, gj * 5 - 3001), h2 = TvRandom.Hash(gi - 811, gj + 4243);
        look = (int)(h2 * 997f);
        // A drive into the garden: near the lattice line, now and then, where a street is in front
        float fx = (x + ox) / YardLattice - gi, fz = (z + oz) / YardLattice - gj;
        bool nearLine = Math.Min(Math.Min(fx, 1f - fx), Math.Min(fz, 1f - fz)) * YardLattice < 1.6f;
        gapHere = nearLine && h2 < 0.45f;
        (Yard kind, int weight)[] table;
        if (Old) table = OldYards;
        else
        {
            long key = ((long)gi << 32) ^ (uint)gj;
            if (!context.TryGetValue(key, out bool rows)) context[key] = rows = BigHouseInGarden(x, z, lx, lz);
            table = rows ? RowYards : VillaYards;
        }
        int total = 0;
        foreach (var t in table) total += t.weight;
        float pick = h * total;
        foreach (var t in table)
        {
            if (pick < t.weight) return t.kind;
            pick -= t.weight;
        }
        return table[0].kind;
    }

    // Whether the house behind this garden edge is a long one (a row of terraced houses, a block of flats): looking in
    // from the edge for the first house cell, then along it
    bool BigHouseInGarden(float x, float z, float lx, float lz)
    {
        for (float d = 3f; d <= 24f; d += 2f)
        {
            float hx = x + lx * d, hz = z + lz * d;
            if (Base(CellAt(hx, hz)) != WorldCell.Building) continue;
            float run = 0f;
            foreach (float s in new[] { -1f, 1f })
                for (float a = 2f; a <= 30f; a += 2f)
                {
                    if (Base(CellAt(hx - lz * a * s, hz + lx * a * s)) != WorldCell.Building) break;
                    run += 2f;
                }
            return run >= 24f;
        }
        return false;
    }

    // One stretch of garden edge from a to b (the garden to the left, (lx, lz))
    void YardUnitAt(float ax, float az, float bx, float bz, float ux, float uz, float lx, float lz, float len, Dictionary<long, bool> context)
    {
        float mx = (ax + bx) / 2f, mz = (az + bz) / 2f;
        Yard kind = YardKindAt(mx, mz, lx, lz, context, out int look, out bool gap);
        bool street = Base(CellAt(mx - lx * 2f, mz - lz * 2f)) == WorldCell.Paved;
        if (gap && street && kind != Yard.Open)
        {
            // The drive: a gap, and the mailbox at its side
            float y = H(mx, mz);
            if (TvRandom.Hash((int)Math.Floor((mx + ox) * 0.5f), (int)Math.Floor((mz + oz) * 0.5f)) < 0.35f)
                yardMesh.Add(YardShape(4, look), mx + lx * 0.3f, y, mz + lz * 0.3f, 1f, 1f, 1f, (float)Math.Atan2(ux, uz));
            return;
        }
        float yaw = -(float)Math.Atan2(uz, ux);
        int seed = (int)Math.Floor((mx + ox) * 2f) * 7919 + (int)Math.Floor((mz + oz) * 2f);
        float r1 = TvRandom.Hash(seed, 17), r2 = TvRandom.Hash(seed, 29), r3 = TvRandom.Hash(seed, 43);
        // Set back into the garden: the outline is the edge of the out-of-bounds ground, the thing stands on it
        float Back(float d, out float px0, out float pz0, out float px1, out float pz1)
        {
            px0 = ax + lx * d; pz0 = az + lz * d; px1 = bx + lx * d; pz1 = bz + lz * d;
            return d;
        }
        switch (kind)
        {
            case Yard.Hedge:
            case Yard.LowHedge:
            {
                bool low = kind == Yard.LowHedge;
                float w = low ? 0.6f : 0.85f, hgt = low ? 0.6f : 1.0f + (look % 5) * 0.08f;
                Back(w / 2f, out float x0, out float z0, out float x1, out float z1);
                TvColor col = look % 3 == 0 ? HedgeA : HedgeB;
                YardBox(x0 - ux * 0.05f, z0 - uz * 0.05f, x1 + ux * 0.05f, z1 + uz * 0.05f, w * 0.88f, -0.15f, hgt * 0.92f, col, HedgeTop);
                // Lumps along its top and sides, so it is a hedge and not a green brick
                int lumps = Math.Max(1, (int)Math.Round(len / 0.9f));
                for (int q = 0; q < lumps; q++)
                {
                    float t = (q + 0.5f) / lumps, px = x0 + (x1 - x0) * t, pz = z0 + (z1 - z0) * t, py = H(px, pz);
                    float sc = w / 0.85f;
                    yardMesh.Add(YardShape(0, seed + q), px, py + hgt * 0.86f, pz, sc, low ? 0.8f : 1f, sc, yaw + q * 0.7f);
                }
                break;
            }
            case Yard.Picket:
            case Yard.PicketColour:
            case Yard.PicketBrick:
            {
                TvColor col = kind == Yard.PicketColour ? PicketColours[look % PicketColours.Length] : PicketWhite;
                Back(0.1f, out float x0, out float z0, out float x1, out float z1);
                const float Hgt = 0.95f;
                // A post at the start of every stretch: brick pillars or wooden posts
                if (kind == Yard.PicketBrick) YardBox(x0 - ux * 0.19f, z0 - uz * 0.19f, x0 + ux * 0.19f, z0 + uz * 0.19f, 0.38f, -0.2f, Hgt + 0.2f, BrickPost, BrickPost.Shade(1.1f));
                else YardBox(x0 - ux * 0.06f, z0 - uz * 0.06f, x0 + ux * 0.06f, z0 + uz * 0.06f, 0.12f, -0.2f, Hgt + 0.1f, col, col);
                // Two rails, and the pickets
                foreach (float ry in new[] { 0.28f, 0.72f })
                    YardBox(x0, z0, x1, z1, 0.04f, ry * Hgt, 0.08f, col.Shade(0.92f), col, true);
                int pickets = Math.Max(2, (int)(len / 0.17f));
                for (int q = 0; q < pickets; q++)
                {
                    float t = (q + 0.5f) / pickets;
                    float px = x0 + (x1 - x0) * t, pz = z0 + (z1 - z0) * t;
                    YardBox(px - ux * 0.045f + lx * 0.035f, pz - uz * 0.045f + lz * 0.035f, px + ux * 0.045f + lx * 0.035f, pz + uz * 0.045f + lz * 0.035f,
                            0.025f, -0.1f, Hgt + 0.1f, col, col);
                }
                // A shrub or two behind it now and then
                if (r1 < 0.35f) yardMesh.Add(YardShape(1, seed), (x0 + x1) / 2f + lx * 0.9f, H((x0 + x1) / 2f + lx * 0.9f, (z0 + z1) / 2f + lz * 0.9f), (z0 + z1) / 2f + lz * 0.9f, 1.2f, 1.2f, 1.2f, r2 * 6.28f);
                break;
            }
            case Yard.Stone:
            {
                Back(0.3f, out float x0, out float z0, out float x1, out float z1);
                float hgt = 0.5f + (look % 3) * 0.08f;
                YardBox(x0, z0, x1, z1, 0.48f, -0.2f, hgt * 0.85f, StoneB, StoneA);
                // Stones standing out of the wall's faces and along its top
                int stones = Math.Max(2, (int)(len / 0.5f));
                for (int q = 0; q < stones; q++)
                {
                    float t = (q + 0.5f) / stones, px = x0 + (x1 - x0) * t, pz = z0 + (z1 - z0) * t;
                    float sh = TvRandom.Hash(seed + q, 3);
                    float row = (q % 3) * hgt * 0.3f;
                    TvColor c = sh < 0.33f ? StoneA : sh < 0.66f ? StoneB : TvColor.Hex("#a9a297");
                    float side = q % 2 == 0 ? 1f : -1f;
                    YardBox(px - ux * 0.22f + lx * side * 0.05f, pz - uz * 0.22f + lz * side * 0.05f, px + ux * 0.22f + lx * side * 0.05f, pz + uz * 0.22f + lz * side * 0.05f,
                            0.5f, row, hgt * 0.3f, c, c.Shade(1.06f));
                }
                break;
            }
            case Yard.BlocksBed:
            case Yard.BlocksGravel:
            {
                bool gravel = kind == Yard.BlocksGravel;
                Back(0.17f, out float x0, out float z0, out float x1, out float z1);
                float hgt = gravel ? 0.36f : 0.48f;
                YardBox(x0, z0, x1, z1, 0.32f, -0.15f, hgt + 0.15f, gravel ? BlockB : BlockA, (gravel ? BlockB : BlockA).Shade(1.08f));
                // The courses of blocks: thin darker lines
                for (float cy = 0.12f; cy < hgt - 0.02f; cy += 0.12f)
                    YardBox(x0 - lx * 0.005f, z0 - lz * 0.005f, x1 - lx * 0.005f, z1 - lz * 0.005f, 0.335f, cy - 0.01f, 0.02f, BlockB.Shade(0.8f), BlockB.Shade(0.8f));
                // The bed behind: bark or gravel, shrubs, flowers
                Back(0.95f, out float b0x, out float b0z, out float b1x, out float b1z);
                YardBox(b0x, b0z, b1x, b1z, 1.2f, -0.1f, 0.15f, gravel ? YardGravel : Bark, gravel ? YardGravel.Shade(1.05f) : Bark);
                int plants = Math.Max(1, (int)(len / 0.7f));
                for (int q = 0; q < plants; q++)
                {
                    float t = (q + 0.5f) / plants, px = b0x + (b1x - b0x) * t + lx * (TvRandom.Hash(seed + q, 5) - 0.5f) * 0.5f, pz = b0z + (b1z - b0z) * t + lz * (TvRandom.Hash(seed + q, 6) - 0.5f) * 0.5f;
                    float py = H(px, pz) + 0.05f;
                    if (gravel || TvRandom.Hash(seed + q, 7) < 0.45f) yardMesh.Add(YardShape(1, seed + q), px, py, pz, 0.7f, 0.7f, 0.7f, q * 1.3f);
                    else yardMesh.Add(YardShape(2, seed + q), px, py, pz, 1f, 1f, 1f, q * 1.1f);
                }
                break;
            }
            case Yard.Boulders:
            {
                int stones = Math.Max(1, (int)Math.Round(len / 0.95f));
                for (int q = 0; q < stones; q++)
                {
                    float t = (q + 0.5f) / stones;
                    Back(0.4f, out float x0, out float z0, out float x1, out float z1);
                    float px = x0 + (x1 - x0) * t, pz = z0 + (z1 - z0) * t;
                    float sc = 0.9f + TvRandom.Hash(seed + q, 11) * 0.5f;
                    yardMesh.Add(YardShape(3, seed + q), px, H(px, pz) - 0.05f, pz, sc, sc, sc, TvRandom.Hash(seed + q, 13) * 6.28f);
                }
                if (r1 < 0.3f) yardMesh.Add(YardShape(1, seed), mx + lx * 1.4f, H(mx + lx * 1.4f, mz + lz * 1.4f), mz + lz * 1.4f, 1.1f, 1.1f, 1.1f, r2 * 6.28f);
                break;
            }
            case Yard.Plank:
            {
                Back(0.1f, out float x0, out float z0, out float x1, out float z1);
                const float Hgt = 1.55f;
                TvColor col = look % 2 == 0 ? PlankA : PlankB;
                YardBox(x0 - ux * 0.06f, z0 - uz * 0.06f, x0 + ux * 0.06f, z0 + uz * 0.06f, 0.12f, -0.2f, Hgt + 0.25f, col.Shade(0.85f), col);
                for (int q = 0; q < 9; q++)
                    YardBox(x0, z0, x1, z1, 0.04f, 0.08f + q * 0.17f, 0.12f, col.Shade(0.96f + (q % 2) * 0.06f), col, true);
                break;
            }
            case Yard.Bed:
            {
                Back(0.5f, out float x0, out float z0, out float x1, out float z1);
                YardBox(x0, z0, x1, z1, 1.0f, -0.1f, 0.14f, Bark, Bark);
                int plants = Math.Max(1, (int)(len / 0.6f));
                for (int q = 0; q < plants; q++)
                {
                    float t = (q + 0.5f) / plants, px = x0 + (x1 - x0) * t, pz = z0 + (z1 - z0) * t;
                    float py = H(px, pz) + 0.04f;
                    yardMesh.Add(YardShape(TvRandom.Hash(seed + q, 8) < 0.6f ? 2 : 1, seed + q), px, py, pz, 0.9f, 0.9f, 0.9f, q * 1.7f);
                }
                break;
            }
            default:
            {
                // An open lawn: only a low edging of stones where it meets the street, and now and then a shrub
                Back(0.08f, out float x0, out float z0, out float x1, out float z1);
                YardBox(x0, z0, x1, z1, 0.12f, -0.1f, 0.2f, Edging, Edging.Shade(1.06f));
                if (r3 < 0.12f) yardMesh.Add(YardShape(r1 < 0.5f ? 1 : 5, seed), mx + lx * 1.5f, H(mx + lx * 1.5f, mz + lz * 1.5f), mz + lz * 1.5f, 1f, 1f, 1f, r2 * 6.28f);
                break;
            }
        }
    }

    // A long box from a to b, w wide, from y0 to y0 + h above the ground at each end (its foot and top follow the
    // slope): the sides in col, the top in top. thin: a board or rail (no ends drawn if short)
    void YardBox(float ax, float az, float bx, float bz, float w, float y0, float h, TvColor col, TvColor top, bool thin = false)
    {
        float dx = bx - ax, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-3f) return;
        float ux = dx / len, uz = dz / len, nx = -uz * w / 2f, nz = ux * w / 2f;
        float ya = H(ax, az) + y0, yb = H(bx, bz) + y0;
        float sx = ux, sz = uz, ox2 = -uz, oz2 = ux;   // along, across (unit)
        // Corners: a/b, left (+n) / right (-n), low / high
        float alx = ax + nx, alz = az + nz, arx = ax - nx, arz = az - nz, blx = bx + nx, blz = bz + nz, brx = bx - nx, brz = bz - nz;
        TvColor side = col, dark = col.Shade(0.9f);
        Face(yardMesh, alx, ya, alz, blx, yb, blz, blx, yb + h, blz, alx, ya + h, alz, ox2, 0f, oz2, side);
        Face(yardMesh, arx, ya, arz, brx, yb, brz, brx, yb + h, brz, arx, ya + h, arz, -ox2, 0f, -oz2, dark);
        Face(yardMesh, alx, ya + h, alz, blx, yb + h, blz, brx, yb + h, brz, arx, ya + h, arz, 0f, 1f, 0f, top);
        if (thin && len > 0.3f) return;
        Face(yardMesh, alx, ya, alz, arx, ya, arz, arx, ya + h, arz, alx, ya + h, alz, -sx, 0f, -sz, dark);
        Face(yardMesh, blx, yb, blz, brx, yb, brz, brx, yb + h, brz, blx, yb + h, blz, sx, 0f, sz, dark);
    }
}
