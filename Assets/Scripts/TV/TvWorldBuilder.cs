using System;
using System.Collections.Generic;
using MapImport;

// Builds the 3D world round one TV control from what the map says is there: the heights of the map's grid, and the
// world layer the map import saved (what grows in every 2 m cell, and the stones, knolls, cliffs, paths, streams,
// walls and power lines). The player's "Terrain look" (MapImport.TerrainLook) decides what the ground is made of,
// which trees grow and how much undergrowth there is.
// Plain C# without Unity, so it runs on a background thread (it takes a second or two) and can be tested outside
// the game. The result is a list of TvMesh; TvWorldView turns them into Unity meshes.
// Everything is in metres from the control: x east, y up (0 = the ground at the control), z north.

// The heights of a map's grid (grid.bytes of a map package)
public class TvHeights
{
    public float cell, minx, miny;      // minx, miny: the centre of cell (0, 0), in the game's map coordinates
    public int rows, cols;
    public float[] h;                   // rows * cols, row 0 at the south; null = no heights

    public static TvHeights Read(byte[] grid)
    {
        try
        {
            if (grid == null || grid.Length < 36 || BitConverter.ToInt32(grid, 0) != 0x4452474F || BitConverter.ToInt32(grid, 4) != 2) return null;
            var t = new TvHeights
            {
                minx = BitConverter.ToSingle(grid, 8),
                miny = BitConverter.ToSingle(grid, 12),
                cell = BitConverter.ToSingle(grid, 24),
                rows = BitConverter.ToInt32(grid, 28),
                cols = BitConverter.ToInt32(grid, 32)
            };
            long count = (long)t.rows * t.cols;
            if (t.rows <= 0 || t.cols <= 0 || t.cell <= 0f || count > 40000000) return null;
            int at = 36 + (int)count * 4;
            if (grid.Length < at + 1 + count * 4 || grid[at] == 0) return t;   // no heights: flat
            t.h = new float[count];
            Buffer.BlockCopy(grid, at + 1, t.h, 0, (int)count * 4);
            return t;
        }
        catch (Exception)
        {
            return null;
        }
    }

    float Cell(int r, int c)
    {
        r = r < 0 ? 0 : r >= rows ? rows - 1 : r;
        c = c < 0 ? 0 : c >= cols ? cols - 1 : c;
        return h[r * cols + c];
    }

    static float Cubic(float a, float b, float c, float d, float t)
    {
        return b + 0.5f * t * (c - a + t * (2f * a - 5f * b + 4f * c - d + t * (3f * (b - c) + d - a)));
    }

    // Smooth between the cells (Catmull-Rom), so a 10 m grid gives round hills and not facets
    public float At(double e, double n)
    {
        if (h == null) return 0f;
        double x = (e - minx) / cell, y = (n - miny) / cell;
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y);
        float fx = (float)(x - i), fy = (float)(y - j);
        float r0 = Cubic(Cell(j - 1, i - 1), Cell(j - 1, i), Cell(j - 1, i + 1), Cell(j - 1, i + 2), fx);
        float r1 = Cubic(Cell(j, i - 1), Cell(j, i), Cell(j, i + 1), Cell(j, i + 2), fx);
        float r2 = Cubic(Cell(j + 1, i - 1), Cell(j + 1, i), Cell(j + 1, i + 1), Cell(j + 1, i + 2), fx);
        float r3 = Cubic(Cell(j + 2, i - 1), Cell(j + 2, i), Cell(j + 2, i + 1), Cell(j + 2, i + 2), fx);
        return Cubic(r0, r1, r2, r3, fy);
    }
}

public class TvWorldInput
{
    public WorldLayer layer;
    public TvHeights heights;           // null = flat
    public TerrainLook look;
    public double centerE, centerN;     // the control, in the game's map coordinates (metres)
    public float radius = 110f;         // the world is a square of 2 x radius
    public int seed = 1;
    public float camDirX, camDirZ;      // where the TV camera should stand, as a direction from the control (0, 0 = anywhere)
    public bool start;                  // the start of a leg and not a control: no flag and no punching unit
    public float legDirX, legDirZ;      // at the start: the way the first leg goes (0, 0 = not known)

    // ---- The run-yourself game mode (RunYourself/RunWorld.cs): the whole map, built as square pieces round the
    // runner. A piece has no TV camera and no control of its own, and fits its neighbours without a seam: the noise
    // patterns start at the same place (anchor) for every piece, y = 0 is the same height everywhere (baseHeight),
    // every tree, stone and tuft belongs to exactly one piece, and a piece is built the same on every computer.
    public bool chunk;                  // radius = half the side of the piece
    public bool chunkDetail;            // only the near detail of a piece: undergrowth and the young trees of thickets
    public float baseHeight;            // the map height that is y = 0
    public double anchorE, anchorN;     // where the noise patterns start
    public List<float> flags;           // the course's controls in this piece: x, z (metres from its centre) for each
    public List<TvSolid> baseSolids;    // chunkDetail: the stones and trunks the piece itself has (nothing grows in them)
    public float undergrowthShare = 1f; // chunkDetail: how much undergrowth (the player's 3D quality)
    public TvTown town;                 // a sprint map in the run-yourself game mode: build it as a town (TvWorldBuilderTown.cs)
    public TvPassable passable;         // the town: the 2D race's grid; no house stands where it lets a runner through
    public bool townRich = true;        // chunkDetail in a town: the houses in full detail (not on the Low 3D quality)
    // A piece far from the runner (2026-11-25): only the cheap far copy of its trees is made. The full trees are by far
    // the most of a piece (about 250 000 corners on High) and were made, kept and handed to the graphics card for every
    // piece in sight, though only drawn within a hundred metres; RunWorld builds the piece again for them when the
    // runner comes near. The trees stand in the same places either way (every square draws its own numbers).
    public bool skipNearTrees;
    // The run-yourself world draws the trees as copies of a few ready-made ones (RunTrees, 2026-11-25): a piece then
    // makes no tree meshes at all, only a list of where each tree stands (TvWorld.treeRecords)
    public bool treeRecords;
    // How detailed the trees, the undergrowth and the forest floor are made (RunSettings.Quality): 0 Low, 1 Medium,
    // 2 High, 3 Ultra. The TV controls use High.
    public int quality = 2;
}

// Something a runner cannot run through: a stone, a tree trunk, a pole
public struct TvSolid
{
    public float x, z, r;
}

public class TvWorld
{
    public float radius;
    // A piece of the run-yourself world (TvWorldInput.chunk): where the flag of each control in it stands, x, z as in
    // TvWorldInput.flags (beside a stone or a knoll top, clear of it)
    public readonly List<float> flagSpots = new List<float>();
    public int size;                    // corners per side of the ground: 2 * radius + 1, one per metre
    public float[] ground;              // size * size heights, [j * size + i], i east and j north
    public readonly List<TvMesh> meshes = new List<TvMesh>();
    public readonly List<TvSolid> solids = new List<TvSolid>();
    // A town's people (RunYourself/RunPeople.cs animates them): x, z, yaw, kind, look, walk length for each
    public readonly List<float> people = new List<float>();
    // Fallen trunks, stone walls and fences a runner jumps over rather than going round: ax, az, bx, bz, height for each
    public readonly List<float> logs = new List<float>();

    // The outside walls of a town's houses (run yourself): ax, az, bx, bz for each; the runner keeps its reach from them
    public readonly List<float> walls = new List<float>();

    // TvWorldInput.treeRecords: the trees, RecordSize numbers each: the full shape (TreeShapes(quality, true)), the far
    // shape (TreeShapes(quality, false)), x, y, z, scale across, scale up, yaw, tint, tilt x, tilt z
    public const int RecordSize = 11;
    public readonly List<float> treeRecords = new List<float>();

    // A town's tunnels near the piece (run yourself: in the tunnel or up on its roof, two levels): ax, az, ux, uz,
    // length, half width, floor height at the start and at the end, for each
    public readonly List<float> tunnels = new List<float>();

    // The tops of the rock cliffs as triangles (x, y, z three times each): the ground a runner stands on there, over
    // the ground's own step hidden under the rock (run yourself, 2026-11-26)
    public readonly List<float> caps = new List<float>();

    // The height of a cliff's rock top here, if there is one
    public bool CapAt(float x, float z, out float y)
    {
        y = float.MinValue;
        bool found = false;
        List<float> t = caps;
        for (int i = 0; i + 8 < t.Count; i += 9)
        {
            float ax = t[i], az = t[i + 2], bx = t[i + 3], bz = t[i + 5], cx = t[i + 6], cz = t[i + 8];
            if ((x < ax && x < bx && x < cx) || (x > ax && x > bx && x > cx) || (z < az && z < bz && z < cz) || (z > az && z > bz && z > cz)) continue;
            float d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
            if (Math.Abs(d) < 1e-8f) continue;
            float u = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / d;
            float v = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / d;
            float w = 1f - u - v;
            if (u < -1e-4f || v < -1e-4f || w < -1e-4f) continue;
            float h = u * t[i + 1] + v * t[i + 4] + w * t[i + 7];
            if (h > y) y = h;
            found = true;
        }
        return found;
    }

    public void AddWall(float ax, float az, float bx, float bz)
    {
        walls.Add(ax); walls.Add(az); walls.Add(bx); walls.Add(bz);
    }

    public void AddHurdle(float ax, float az, float bx, float bz, float height)
    {
        logs.Add(ax); logs.Add(az); logs.Add(bx); logs.Add(bz); logs.Add(height);
    }
    // The control flag's stand and the punching unit on top of it
    public float flagX, flagZ, unitX, unitY, unitZ;
    public bool hasUnit = true;         // false at the start of a leg: nothing to punch
    // Where the TV camera stands: a spot about 9 m from the control with a free view of it (trees keep off it)
    public float camX, camZ;
    public int trees, rocks, tufts;
    public long vertices, triangles;

    // 0 away from every hurdle, rising to 1 right over one: how far into the jump a runner here is; height: how high it is
    public float Hurdle(float x, float z, out float height)
    {
        float best = 1f;
        height = 0f;
        for (int i = 0; i + 4 < logs.Count; i += 5)
        {
            float ax = logs[i], az = logs[i + 1], dx = logs[i + 2] - ax, dz = logs[i + 3] - az, l2 = dx * dx + dz * dz;
            float t = l2 > 1e-6f ? ((x - ax) * dx + (z - az) * dz) / l2 : 0f;
            if (t < -0.05f || t > 1.05f) continue;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            float h = logs[i + 4];
            float ex = ax + dx * t - x, ez = az + dz * t - z, d = (float)Math.Sqrt(ex * ex + ez * ez) / (0.75f + h * 0.4f);
            if (d < best) { best = d; height = h; }
        }
        return best >= 1f ? 0f : (float)Math.Sin((1f - best) * Math.PI / 2);
    }

    public float HeightAt(float x, float z)
    {
        float fx = x + radius, fz = z + radius;
        fx = fx < 0f ? 0f : fx > size - 1.001f ? size - 1.001f : fx;
        fz = fz < 0f ? 0f : fz > size - 1.001f ? size - 1.001f : fz;
        int i = (int)fx, j = (int)fz;
        float u = fx - i, v = fz - j;
        float a = ground[j * size + i], b = ground[j * size + i + 1], c = ground[(j + 1) * size + i], d = ground[(j + 1) * size + i + 1];
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    // Moves a point out of the stones and trunks, so a runner goes round them. reach: the runner's own half width.
    public void PushOut(ref float x, ref float z, float reach)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < solids.Count; i++)
            {
                TvSolid s = solids[i];
                float dx = x - s.x, dz = z - s.z, r = s.r + reach;
                if (dx > r || dx < -r || dz > r || dz < -r) continue;
                float d2 = dx * dx + dz * dz;
                if (d2 >= r * r) continue;
                float d = (float)Math.Sqrt(d2);
                if (d < 0.001f) { x += r; continue; }
                // Soft at the edge, so the runner swerves and does not jump
                float push = r - d;
                x += dx / d * push;
                z += dz / d * push;
            }
        }
    }
}

public partial class TvWorldBuilder
{
    readonly TvWorldInput input;
    readonly WorldLayer layer;
    readonly TerrainLook look;
    readonly TvRandom random;
    readonly TvWorld world = new TvWorld();
    readonly int R, N;
    readonly float cE, cN;              // the control within the layer, as floats near the origin of the layer
    float[] F;                          // ground heights
    float[] rock;                       // how much bare rock shows (cliff faces)
    float[] trackBlend;                 // how much of a path or road shows
    TvColor[] trackColor;
    float[] clear;                      // metres to the nearest path, road, wall or stream edge (large = none near)
    float[] water;                      // how much stream water shows
    float[] bog;                        // how wet a narrow marsh strip makes the ground
    float[] soil;                       // bare earth (banks, ditches)
    float baseHeight;

    const int TreeTiles = 4;
    // Tiles the finished world is cut into for drawing: 10 x 10 of 22 m
    const int DrawTiles = 10;
    TvMesh groundMesh, rockMesh, thingMesh, wireMesh;
    // The cliff faces: drawn with the ground's grain (named "Ground ..."), so they look like the rock of the ground
    // round them, not like smooth grey slabs (2026-11-19, Anders)
    TvMesh cliffMesh;
    TvMesh[] treeMeshes, tuftMeshes;
    TvMesh litterMesh;                  // needles, twigs and cones lying on the forest floor (TvWorldBuilderFlora.Litter)
    TvMesh woodMesh;                    // fallen trunks and stumps with weathered wood and bark (TvWorldBuilderFlora)
    TvMesh pathMesh;                    // the trodden earth of the paths (TvWorldBuilderFlora.PathStrips)
    // A piece of the run-yourself world: the trees as they are drawn far away, and the young trees of the thickets
    TvMesh treeFarMesh, thicketMesh;
    readonly bool chunk;
    readonly float ox, oz;              // the piece's centre from the anchor: added to x, z wherever a noise pattern is read

    public TvWorldBuilder(TvWorldInput input)
    {
        this.input = input;
        layer = input.layer;
        look = (input.look ?? new TerrainLook()).Safe();
        random = new TvRandom(input.seed * 7919 + (int)(input.centerE * 3.0) + (int)(input.centerN * 5.0) * 31);
        chunk = input.chunk;
        R = chunk ? Math.Max(4, Math.Min(160, (int)input.radius)) : Math.Max(40, Math.Min(160, (int)input.radius));
        N = 2 * R + 1;
        cE = (float)input.centerE;
        cN = (float)input.centerN;
        if (chunk)
        {
            ox = (float)(input.centerE - input.anchorE);
            oz = (float)(input.centerN - input.anchorN);
        }
    }

    public static TvWorld Build(TvWorldInput input)
    {
        return new TvWorldBuilder(input).Build();
    }

    // ---- Small helpers ----

    static float Sm(float a, float b, float x) => TvShapeBuilder.Smooth(a, b, x);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    float Rr(float a, float b) => random.Range(a, b);
    // A piece of the run-yourself world keeps whatever stands on its own square (the margin is for the TV's edge)
    bool Inside(float x, float z, float margin) => chunk ? x >= -R && x < R && z >= -R && z < R
        : x > -R + margin && x < R - margin && z > -R + margin && z < R - margin;

    // Run-yourself pieces: the same numbers for the same place on the map, whatever piece it is built in
    void Reseed(float x, float z, int salt)
    {
        random.Reseed((int)Math.Floor(x + ox), (int)Math.Floor(z + oz), salt);
    }

    byte CellAt(float x, float z) => layer.At(input.centerE + x, input.centerN + z);
    static int Base(byte cell) => cell & WorldCell.BaseMask;

    // The ground kind at a point of a town with smooth edges (2026-11-23, Anders: the paved ways and the lake shores
    // were the staircase of the 2 m cells; they should be smooth like the map's lines). The 4 x 4 cells round the
    // point vote with cubic B-spline weights of their middles, and the kind with the most weight wins: a staircase
    // becomes a smooth slanted edge and a corner is rounded, while a way one cell wide still keeps its cell
    // (2/3 of the weight across it). Houses keep their own cells (their walls stand on them).
    readonly float[] voteWeight = new float[16];

    byte SmoothCell(float x, float z)
    {
        byte own = CellAt(x, z);
        if (Base(own) == WorldCell.Building) return own;
        double fe = (input.centerE + x - layer.minE) / layer.cell - 0.5, fn = (input.centerN + z - layer.minN) / layer.cell - 0.5;
        int i0 = (int)Math.Floor(fe), j0 = (int)Math.Floor(fn);
        float tx = (float)(fe - i0), tz = (float)(fn - j0);
        Span4(tx, out float x0, out float x1, out float x2, out float x3);
        Span4(tz, out float z0, out float z1, out float z2, out float z3);
        float[] wx = { x0, x1, x2, x3 }, wz = { z0, z1, z2, z3 };
        Array.Clear(voteWeight, 0, 16);
        var cells = new byte[16];
        for (int dj = 0; dj < 4; dj++)
            for (int di = 0; di < 4; di++)
            {
                byte c = layer.At(layer.minE + (i0 + di - 1 + 0.5) * layer.cell, layer.minN + (j0 + dj - 1 + 0.5) * layer.cell);
                int kind = Base(c) == WorldCell.Building ? WorldCell.Paved : Base(c);
                cells[dj * 4 + di] = c;
                // A cell green only from a hedge drawn through it has no say: the hedge stands on the ground round it
                // (2026-11-24, Anders: patches of grass stuck out from a hedge onto the asphalt)
                if (HedgeCell(i0 + di - 1, j0 + dj - 1) && kind != WorldCell.Paved && kind != WorldCell.Water) { cells[dj * 4 + di] = 0xFF; continue; }
                voteWeight[kind] += wx[di] * wz[dj];
            }
        int ownKind = Base(own), best = ownKind;
        for (int k = 0; k < 16; k++)
            if (voteWeight[k] > voteWeight[best] + 1e-4f) best = k;
        if (best == ownKind) return own;
        // The flags (marsh, stones) of the nearest cell of the winning kind
        int pick = -1;
        float near = float.MaxValue;
        for (int q = 0; q < 16; q++)
        {
            if (cells[q] == 0xFF) continue;
            int kind = Base(cells[q]) == WorldCell.Building ? WorldCell.Paved : Base(cells[q]);
            if (kind != best) continue;
            float dx = (q % 4) - 1 - tx, dz = (q / 4) - 1 - tz, d = dx * dx + dz * dz;
            if (d < near) { near = d; pick = q; }
        }
        return (byte)((cells[pick] & ~WorldCell.BaseMask) | best);
    }

    HashSet<long> hedgeCells;

    // A 2 m cell of the map a hedge line runs through (in a town)
    bool HedgeCell(int i, int j)
    {
        if (input.town == null || lines == null) return false;
        if (hedgeCells == null)
        {
            hedgeCells = new HashSet<long>();
            foreach (var line in lines)
            {
                if (line.kind != WorldLineKind.Hedge) continue;
                float[] c = line.xz;
                for (int q = 0; q + 3 < c.Length; q += 2)
                {
                    float dx = c[q + 2] - c[q], dz = c[q + 3] - c[q + 1], len = (float)Math.Sqrt(dx * dx + dz * dz);
                    for (float t = 0f; t <= len; t += 0.4f)
                    {
                        float f = len > 0f ? t / len : 0f;
                        double e = input.centerE + c[q] + dx * f, n = input.centerN + c[q + 1] + dz * f;
                        for (int a = -1; a <= 1; a++)
                            for (int b = -1; b <= 1; b++)
                            {
                                // The cells whose middle is within 1.3 m of the hedge's line
                                int ci = (int)Math.Floor((e - layer.minE) / layer.cell) + a, cj = (int)Math.Floor((n - layer.minN) / layer.cell) + b;
                                double mx = layer.minE + (ci + 0.5) * layer.cell - e, mz = layer.minN + (cj + 0.5) * layer.cell - n;
                                if (mx * mx + mz * mz <= 1.3 * 1.3) hedgeCells.Add(((long)ci << 32) ^ (uint)cj);
                            }
                    }
                }
            }
        }
        return hedgeCells.Count > 0 && hedgeCells.Contains(((long)i << 32) ^ (uint)j);
    }

    // Uniform cubic B-spline weights of the four cells round a point t (0..1) past the second one's middle
    static void Span4(float t, out float w0, out float w1, out float w2, out float w3)
    {
        float t2 = t * t, t3 = t2 * t;
        w0 = (1f - t) * (1f - t) * (1f - t) / 6f;
        w1 = (3f * t3 - 6f * t2 + 4f) / 6f;
        w2 = (-3f * t3 + 3f * t2 + 3f * t + 1f) / 6f;
        w3 = t3 / 6f;
    }

    float BaseHeight(float x, float z) => input.heights != null ? input.heights.At(input.centerE + x, input.centerN + z) : 0f;

    // Distance from (px, pz) to the stretch a-b, and the nearest point on it
    static float DistSeg(float px, float pz, float ax, float az, float bx, float bz, out float qx, out float qz)
    {
        float dx = bx - ax, dz = bz - az, l = dx * dx + dz * dz;
        float t = l > 0f ? ((px - ax) * dx + (pz - az) * dz) / l : 0f;
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        qx = ax + dx * t;
        qz = az + dz * t;
        return (float)Math.Sqrt((px - qx) * (px - qx) + (pz - qz) * (pz - qz));
    }

    // Every corner of the ground within reach of the stretch a-b
    void ForNear(float ax, float az, float bx, float bz, float reach, Action<int, float, float, float> each)
    {
        int i0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - reach) + R), i1 = Math.Min(N - 1, (int)Math.Ceiling(Math.Max(ax, bx) + reach) + R);
        int j0 = Math.Max(0, (int)Math.Floor(Math.Min(az, bz) - reach) + R), j1 = Math.Min(N - 1, (int)Math.Ceiling(Math.Max(az, bz) + reach) + R);
        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                float d = DistSeg(i - R, j - R, ax, az, bx, bz, out float qx, out float qz);
                if (d <= reach) each(j * N + i, d, qx, qz);
            }
        }
    }

    // The lines of the map that come near the world, as points in the world's own metres
    List<(WorldLineKind kind, float[] xz)> NearLines(float margin)
    {
        var list = new List<(WorldLineKind, float[])>();
        float lo = -R - margin, hi = R + margin;
        stepEnds.Clear();
        Dictionary<WorldLine, (float before, float after)> chains = null;
        foreach (WorldLine line in layer.lines)
        {
            if (line.xy == null || line.xy.Length < 4) continue;
            if (line.maxE - cE < lo || line.minE - cE > hi || line.maxN - cN < lo || line.minN - cN > hi) continue;
            var xz = new float[line.xy.Length];
            for (int i = 0; i + 1 < xz.Length; i += 2)
            {
                xz[i] = (float)(line.xy[i] - input.centerE);
                xz[i + 1] = (float)(line.xy[i + 1] - input.centerN);
            }
            list.Add((line.kind, xz));
            if (IsStepLine(line.kind))
            {
                if (chains == null) chains = CliffChains(layer);
                if (chains.TryGetValue(line, out var ends)) stepEnds[xz] = ends;
            }
        }
        return list;
    }

    List<(WorldPointKind kind, float x, float z, float angle)> NearPoints(float margin)
    {
        var list = new List<(WorldPointKind, float, float, float)>();
        foreach (WorldPoint p in layer.points)
        {
            float x = (float)(p.e - input.centerE), z = (float)(p.n - input.centerN);
            if (x < -R - margin || x > R + margin || z < -R - margin || z > R + margin) continue;
            list.Add((p.kind, x, z, p.angle));
        }
        return list;
    }

    // ---- The build ----

    List<(WorldLineKind kind, float[] xz)> lines;
    List<(WorldPointKind kind, float x, float z, float angle)> points;
    // A rock cliff's line and which side is the low one: +1 right of the line as drawn, -1 left
    readonly List<(float side, float[] xz)> cliffs = new List<(float, float[])>();

    TvWorld Build()
    {
        world.radius = R;
        world.size = N;
        lines = NearLines(12f);
        points = NearPoints(6f);
        ClearTunnels();
        F = new float[N * N];
        rock = new float[N * N];
        trackBlend = new float[N * N];
        trackColor = new TvColor[N * N];
        asphaltCorner = null;
        asphaltLevel = null;
        roadLevel = null;
        waterTop = null;
        stepSegs = null;
        pavedCorner = null;
        paveLevel = null;
        eDist = eLvl = wLvl = pDist = pLvl = null;
        eId = null;
        eCell = null;
        gKind = null;
        clear = new float[N * N];
        water = new float[N * N];
        bog = new float[N * N];
        soil = new float[N * N];
        for (int i = 0; i < clear.Length; i++) clear[i] = 99f;

        groundMesh = new TvMesh { name = "Ground", castShadows = false };
        rockMesh = new TvMesh { name = "Rocks" };
        cliffMesh = new TvMesh { name = "Ground cliffs" };
        thingMesh = new TvMesh { name = "Things" };
        woodMesh = new TvMesh { name = "Dead wood" };
        pathMesh = new TvMesh { name = "Paths", castShadows = false };
        wireMesh = new TvMesh { name = "Wires", castShadows = false };
        waterMesh = new TvMesh { name = "Water", castShadows = false };
        litterMesh = new TvMesh { name = "Litter", castShadows = false };
        treeMeshes = new TvMesh[TreeTiles * TreeTiles];
        tuftMeshes = new TvMesh[TreeTiles * TreeTiles];
        for (int i = 0; i < treeMeshes.Length; i++)
        {
            treeMeshes[i] = new TvMesh { name = "Trees " + i };
            tuftMeshes[i] = new TvMesh { name = "Undergrowth " + i, castShadows = false };
        }

        if (chunk)
        {
            BuildPiece();
            return world;
        }

        MakeShapes();
        Heights();
        Tracks();
        Ground();
        Waters();
        Stones();
        Structures();
        ForestHouses();
        if (input.town != null) TownStreets();
        else ForestBridges();
        ControlFlag();
        CameraSpot();
        Trees();
        Stumps();
        ClearCut();
        PathStrips();
        Undergrowth();
        Litter();

        world.ground = F;
        // Everything in small tiles, so only what the TV camera looks at is drawn (see TvMesh.SplitIntoTiles)
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { groundMesh }, "Ground", false, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { rockMesh }, "Rocks", true, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { cliffMesh }, "Ground cliffs", true, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { thingMesh }, "Things", true, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { woodMesh }, "Dead wood", true, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { pathMesh }, "Paths", false, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { waterMesh }, "Water", false, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { wireMesh }, "Wires", false, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh>(treeMeshes), "Trees", true, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh>(tuftMeshes), "Undergrowth", false, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { litterMesh }, "Litter", false, R, DrawTiles));
        if (townMesh != null)
        {
            // A sprint map's town (TV controls on sprint maps, 2026-11-20)
            world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { townMesh }, "Town houses", true, R, DrawTiles));
            world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { townThingMesh }, "Town things", true, R, DrawTiles));
            world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { townBuiltMesh }, "Town bridges", true, R, DrawTiles));
            if (yardMesh != null) world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { yardMesh }, "Town gardens", true, R, DrawTiles));
        }
        else if (townBuiltMesh != null)
            world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { townBuiltMesh }, "Bridges", true, R, DrawTiles));
        foreach (TvMesh m in world.meshes)
        {
            world.vertices += m.VertexCount;
            world.triangles += m.triangles.Count / 3;
        }
        return world;
    }

    // ---- A piece of the run-yourself world ----

    // The whole piece is one tile, drawn or not as a whole (RunWorld decides by its distance from the runner).
    // The base build has everything but the near detail, plus a cheaper copy of the ground and the trees for far away;
    // the detail build (chunkDetail) is only the undergrowth and the young trees of the thickets, made when the runner
    // comes near and thrown away when they leave.
    void BuildPiece()
    {
        world.radius = R;
        world.size = N;
        // The control and the TV camera are far away, so nothing is kept clear for them
        world.flagX = world.flagZ = world.camX = world.camZ = 1e6f;
        world.hasUnit = false;
        lines = NearLines(14f);
        points = NearPoints(12f);
        ClearTunnels();
        F = new float[N * N];
        rock = new float[N * N];
        trackBlend = new float[N * N];
        trackColor = new TvColor[N * N];
        asphaltCorner = null;
        asphaltLevel = null;
        roadLevel = null;
        waterTop = null;
        stepSegs = null;
        pavedCorner = null;
        paveLevel = null;
        eDist = eLvl = wLvl = pDist = pLvl = null;
        eId = null;
        eCell = null;
        gKind = null;
        clear = new float[N * N];
        water = new float[N * N];
        bog = new float[N * N];
        soil = new float[N * N];
        for (int i = 0; i < clear.Length; i++) clear[i] = 99f;
        groundMesh = new TvMesh(true) { name = "Ground", castShadows = false, group = TvMesh.Near };
        rockMesh = new TvMesh(true) { name = "Rocks" };
        cliffMesh = new TvMesh(true) { name = "Ground cliffs" };
        thingMesh = new TvMesh(true) { name = "Things" };
        woodMesh = new TvMesh(true) { name = "Dead wood" };
        pathMesh = new TvMesh(true) { name = "Paths", castShadows = false };
        wireMesh = new TvMesh(true) { name = "Wires", castShadows = false };
        waterMesh = new TvMesh(true) { name = "Water", castShadows = false };
        treeMeshes = new[] { new TvMesh(true, true) { name = "Trees", group = TvMesh.Near } };
        treeFarMesh = new TvMesh(true) { name = "Trees far", castShadows = false, group = TvMesh.Far };
        tuftMeshes = new[] { new TvMesh(true, true) { name = "Undergrowth", castShadows = false, ranks = TvMesh.PooledFloats(true) } };
        thicketMesh = new TvMesh(true, true) { name = "Thickets" };
        litterMesh = new TvMesh(true, true) { name = "Litter", castShadows = false };

        MakeShapes();
        Heights();
        Tracks();
        world.ground = F;
        if (tunnelPlans != null)
            foreach (TunnelPlan tp in tunnelPlans)
                world.tunnels.AddRange(new[] { tp.ax, tp.az, tp.ux, tp.uz, tp.len, tp.half, tp.fa, tp.fb });
        if (input.chunkDetail)
        {
            if (input.baseSolids != null) world.solids.AddRange(input.baseSolids);
            int thicketSolids = world.solids.Count;
            Thickets();
            OpenPassages(thicketSolids, world.solids.Count);
            Undergrowth();
            Litter();
            Keep(tuftMeshes[0]);
            Keep(thicketMesh);
            Keep(litterMesh);
            // A town's houses in full detail, right round the runner
            if (input.town != null && input.townRich)
            {
                Buildings();
                Keep(townMesh);
            }
            return;
        }
        Ground();
        Waters();
        Stones();
        int lineSolids = world.solids.Count;
        Structures();
        OpenPassages(lineSolids, world.solids.Count);
        ForestHouses();
        if (input.town != null) TownStreets();
        else ForestBridges();
        Flags();
        int treeSolids = world.solids.Count;
        Trees();
        Stumps();
        ClearCut();
        PathStrips();
        OpenPassages(treeSolids, world.solids.Count);
        // The ground and the water cut exactly along the map's lines (TvWorldBuilderSteps)
        cutLevel = new Dictionary<int, float>();
        GatherSteps();
        CutSteps(groundMesh, false);
        CutSteps(waterMesh, true);
        if (pavedCorner != null) KeepTown();
        CutGroundEdges();
        if (input.town == null) Keep(townBuiltMesh);
        Keep(groundMesh);
        Keep(GroundFar());
        Keep(waterMesh);
        Keep(rockMesh);
        Keep(cliffMesh);
        Keep(thingMesh);
        Keep(woodMesh);
        Keep(pathMesh);
        Keep(wireMesh);
        Keep(treeMeshes[0]);
        Keep(treeFarMesh);
    }

    // A run-yourself sprint: the runner is kept to the 2D race's grid and lines (RunTerrain), so the trunks, walls and
    // fences built from the map must not close what the 2D game leaves open (2026-11-20, Anders: stuck in a passage
    // between a wall and a green). Their solids on a cell the grid lets a runner through are dropped (index range).
    void OpenPassages(int from, int to)
    {
        if (input.passable == null || !input.passable.sprint || to <= from) return;
        int kept = from;
        for (int i = from; i < to; i++)
        {
            TvSolid s = world.solids[i];
            if (input.passable.NoHouse(input.centerE + s.x, input.centerN + s.z)) continue;
            world.solids[kept++] = s;
        }
        world.solids.RemoveRange(kept, to - kept);
    }

    void Keep(TvMesh m)
    {
        if (m == null || m.VertexCount == 0) return;
        world.meshes.Add(m);
        world.vertices += m.VertexCount;
        world.triangles += m.triangles.Count / 3;
    }

    // The ground far away: every fourth corner, with a short skirt hanging down at the edge, so the small steps
    // against a finer neighbour never show the sky through
    TvMesh GroundFar()
    {
        const int Step = 4;
        var m = new TvMesh { name = "Ground far", castShadows = false, group = TvMesh.Far };
        int n = (N - 1) / Step + 1;
        List<float> p = groundMesh.positions, nm = groundMesh.normals;
        List<byte> c = groundMesh.colors;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int k = Math.Min(N - 1, j * Step) * N + Math.Min(N - 1, i * Step);
                m.Vertex(p[k * 3], p[k * 3 + 1], p[k * 3 + 2], nm[k * 3], nm[k * 3 + 1], nm[k * 3 + 2], new TvColor(c[k * 3], c[k * 3 + 1], c[k * 3 + 2]));
            }
        }
        for (int j = 0; j < n - 1; j++)
        {
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i;
                m.triangles.Add(a); m.triangles.Add(a + n); m.triangles.Add(a + 1);
                m.triangles.Add(a + 1); m.triangles.Add(a + n); m.triangles.Add(a + n + 1);
            }
        }
        // The skirt: the edge again, 0.6 m lower, joined to the edge facing out
        void Skirt(int a, int b, bool flip)
        {
            int a2 = m.Vertex(m.positions[a * 3], m.positions[a * 3 + 1] - 0.6f, m.positions[a * 3 + 2], 0f, 1f, 0f,
                new TvColor(m.colors[a * 3], m.colors[a * 3 + 1], m.colors[a * 3 + 2]));
            int b2 = m.Vertex(m.positions[b * 3], m.positions[b * 3 + 1] - 0.6f, m.positions[b * 3 + 2], 0f, 1f, 0f,
                new TvColor(m.colors[b * 3], m.colors[b * 3 + 1], m.colors[b * 3 + 2]));
            if (flip) { m.triangles.Add(a); m.triangles.Add(b); m.triangles.Add(a2); m.triangles.Add(b); m.triangles.Add(b2); m.triangles.Add(a2); }
            else { m.triangles.Add(a); m.triangles.Add(a2); m.triangles.Add(b); m.triangles.Add(b); m.triangles.Add(a2); m.triangles.Add(b2); }
        }
        for (int i = 0; i < n - 1; i++)
        {
            Skirt(i, i + 1, true);                                   // south edge
            Skirt((n - 1) * n + i, (n - 1) * n + i + 1, false);      // north edge
            Skirt(i * n, (i + 1) * n, false);                        // west edge
            Skirt(i * n + n - 1, (i + 1) * n + n - 1, true);         // east edge
        }
        return m;
    }

    // The flag of every control of the course in the piece, beside the feature it marks, as at a TV control
    void Flags()
    {
        if (input.flags == null) return;
        for (int f = 0; f + 1 < input.flags.Count; f += 2)
        {
            float fx = input.flags[f], fz = input.flags[f + 1];
            float px = fx, pz = fz;
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (TvSolid s in world.solids)
                {
                    float dx = px - s.x, dz = pz - s.z, d = (float)Math.Sqrt(dx * dx + dz * dz), need = s.r + 0.45f;
                    if (d >= need) continue;
                    if (d < 0.05f) { dx = 0.6f; dz = -0.8f; d = 1f; }
                    px = s.x + dx / d * need;
                    pz = s.z + dz / d * need;
                }
            }
            world.flagSpots.Add(px);
            world.flagSpots.Add(pz);
            Reseed(fx, fz, 91);
            thingMesh.Add(FlagShape(random), px, H(px, pz), pz, 1f, 1f, 1f, Rr(0f, 6.283f));
            world.solids.Add(new TvSolid { x = px, z = pz, r = 0.12f });
        }
    }

    // The stand with the punching unit on top and the orange and white flag under it; it stands at 0, 0
    public static TvShape FlagShape(TvRandom random)
    {
        TvColor white = TvColor.Hex("#f4f2ea"), orange = TvColor.Hex("#f26a1b");
        var b = new TvShapeBuilder(random);
        float side = 0.3f, r = side / (float)Math.Sqrt(3.0);
        for (int i = 0; i < 3; i++)
        {
            float t0 = i * 2.094395f, t1 = t0 + 2.094395f;
            float x0 = (float)Math.Cos(t0) * r, z0 = (float)Math.Sin(t0) * r, x1 = (float)Math.Cos(t1) * r, z1 = (float)Math.Sin(t1) * r;
            // Each face: white above the diagonal, orange under it; both sides of the cloth
            b.Triangle(x0, 0.85f, z0, x1, 0.85f, z1, x0, 0.55f, z0, 0f, 0.7f, 0f, white);
            b.Triangle(x1, 0.85f, z1, x1, 0.55f, z1, x0, 0.55f, z0, 0f, 0.7f, 0f, orange);
            b.Triangle(x0 * 0.98f, 0.85f, z0 * 0.98f, x1 * 0.98f, 0.85f, z1 * 0.98f, x0 * 0.98f, 0.55f, z0 * 0.98f, x0 + x1, 0.7f, z0 + z1, white);
            b.Triangle(x1 * 0.98f, 0.85f, z1 * 0.98f, x1 * 0.98f, 0.55f, z1 * 0.98f, x0 * 0.98f, 0.55f, z0 * 0.98f, x0 + x1, 0.7f, z0 + z1, orange);
        }
        b.Tube(0.018f, 0.018f, 1.0f, 0f, 6, TvColor.Hex("#d8d8d2"));
        b.Box(-0.05f, 0.985f, -0.075f, 0.05f, 1.055f, 0.075f, TvColor.Hex("#c8281e"));
        return b.Done();
    }

    // ---- Heights: the map's heights, cliffs as steps, knolls and pits, a little roughness ----

    void Heights()
    {
        baseHeight = chunk ? input.baseHeight : BaseHeight(0f, 0f);
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                float x = i - R, z = j - R;
                F[j * N + i] = Plain(x, z);
            }
        }

        foreach (var p in points)
        {
            switch (p.kind)
            {
                case WorldPointKind.Knoll: Bump(p.x, p.z, 1.6f, 9f); break;   // clear to see (Anders, 2026-11-19)
                case WorldPointKind.LongKnoll:
                {
                    float a = p.angle * 0.0174533f, dx = (float)Math.Cos(a) * 2.2f, dz = (float)Math.Sin(a) * 2.2f;
                    Bump(p.x - dx, p.z - dz, 0.9f, 8f);
                    Bump(p.x, p.z, 1.2f, 8f);
                    Bump(p.x + dx, p.z + dz, 0.9f, 8f);
                    break;
                }
                case WorldPointKind.Depression: Bump(p.x, p.z, -0.8f, 9f); break;
                case WorldPointKind.Pit: Bump(p.x, p.z, -1.0f, 4f); break;
                case WorldPointKind.RockyPit: Bump(p.x, p.z, -1.1f, 5f); break;
                case WorldPointKind.WaterHole: Bump(p.x, p.z, -0.5f, 5f); break;
            }
        }

        cliffStep = new float[N * N];
        foreach (var line in lines)
        {
            float[] c = line.xz;
            switch (line.kind)
            {
                // As high as the ground makes them, but never lower than this (2026-11-20, Anders: an impassable
                // cliff was a decimetre high where the contours showed no drop)
                // ... and at most this high in the middle where the ground drops more; the rest of that drop stays
                // a slope below the face (2026-11-24, Anders: 2-6 m in the middle, gentler at the ends)
                // (2026-11-24 again, Anders: a bit higher, at least about 2.6 m, and up to 15 m for the big cliffs
                // of e.g. Czechia; small cliffs at least 1.2 m)
                case WorldLineKind.Cliff: Cliff(c, 2.8f, 15f, true); break;
                case WorldLineKind.SmallCliff: Cliff(c, 1.2f, 2.2f, true); break;
                case WorldLineKind.EarthBank: Cliff(c, 0.6f, 1.8f, false); break;
                case WorldLineKind.EarthWall: Groove(c, 0.6f, 1.6f, 0.5f, false); break;
                case WorldLineKind.Gully: Groove(c, -0.9f, 2.6f, 0.5f, false); break;
                // Every stream and water channel has some water in it, more in some places and less in others
                // (2026-11-26, Anders: the small ones were only dug out; something blue in all of them)
                case WorldLineKind.Ditch: Groove(c, -0.45f, 1.3f, 0.6f, true, 0.55f); break;
                case WorldLineKind.SmallStream: Groove(c, -0.4f, 1.5f, 0.3f, true, 0.7f); break;
                case WorldLineKind.Stream: Groove(c, -0.6f, 2.6f, 0.3f, true, 1.2f); break;
                case WorldLineKind.NarrowMarsh:
                    // A narrow marsh: a shallow wet strip with pools here and there
                    Groove(c, -0.12f, 1.6f, 0f, false);
                    for (int q = 0; q + 3 < c.Length; q += 2)
                        ForNear(c[q], c[q + 1], c[q + 2], c[q + 3], 2.5f, (k, d, qx, qz) => bog[k] = Math.Max(bog[k], 1f - Sm(0.6f, 1.8f, d)));
                    break;
            }
        }

        for (int k = 0; k < F.Length; k++) F[k] += cliffStep[k];
        cliffStep = null;

        // Lakes and ponds lie a little lower than their shores: lowered by how far inside the water's edge a corner
        // is, so the shore where the ground meets the water follows the map's bank line
        TownLines();
        EdgeField();
        LakeLevels();
        if (input.town != null) PoolGround();
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                int k = j * N + i;
                byte cell = input.town != null ? SmoothCell(i - R, j - R) : CellAt(i - R, j - R);
                float lake = lakeLevel[k];
                // Only the water's side of the edge is lowered: the land keeps its height right up to the edge line,
                // where the ground is cut (2026-11-25, Anders: the land dipped in jagged teeth at the water; the
                // corners up to half a metre on the land side were lowered under the surface too). Without a lake
                // level (a pond with no level found) the old half metre on the land side still slopes it in.
                float inside = Math.Max(0f, Math.Min(1f, float.IsNaN(lake) ? wLvl[k] + 0.5f : wLvl[k] + 0.05f));
                if (inside > 0f)
                {
                    F[k] -= 0.45f * inside + (quay != null ? 0.9f * quay[k] : 0f);
                    // Under the lake's one flat surface everywhere inside its edge
                    if (!float.IsNaN(lake))
                    {
                        F[k] = Math.Min(F[k], lake - 0.12f - 0.5f * inside);
                        // A natural shore shelves gently under the surface, not a 1 m drop in jagged teeth at the
                        // edge (2026-11-25, Anders); by a quay wall the water stays deep
                        if (quay == null || quay[k] < 0.05f) F[k] = Math.Max(F[k], lake - 0.15f - 0.3f * Math.Max(0f, wLvl[k]));
                    }
                }
                // The bank just outside the edge stays above the surface (the lake never spills onto the land)
                // (2026-11-25, Anders: the open land by the water dipped below the surface in jagged teeth and small
                // lakes: the map's heights by the shore lie lower than the water's own, so all the land near a lake
                // is kept above it, easing off only far from the edge)
                else if (!float.IsNaN(lake))
                {
                    if (F[k] < lake + 0.15f) F[k] = lake + 0.15f;
                    // A natural bank (not a street's quay) eases down to the water over its last 2.5 m, soft and
                    // even, instead of a step (2026-11-25, Anders: "mjukt, härligt, gå ner till vattnet")
                    if (wLvl[k] > -2.5f && (quay == null || quay[k] < 0.05f) && Base(cell) != WorldCell.Paved && Base(cell) != WorldCell.Building
                        && (input.town == null || PavedWeight(i - R, j - R) < 0.02f))
                    {
                        float near = lake + 0.15f;
                        F[k] = Math.Min(F[k], near + (F[k] - near) * Sm(0f, 2.5f, -wLvl[k]));
                    }
                }
                // Pools in a marsh are hollows with water in them
                else if (Base(cell) != WorldCell.Water && MarshPool(i - R - 0.5f, j - R - 0.5f, (cell & WorldCell.Marsh) != 0, bog[k])) F[k] -= 0.3f;
            }
        }
        // The town's bridges, tunnels and stairs; on a forest map its footbridges (TvBridges)
        TownHeights();
    }

    // One flat surface for a lake (2026-11-25, Anders: the water rose in an L-shaped bulge a metre or two out from a
    // quay and a bridge, where the ground was dug deeper by the wall). The surface at a corner is the lowest of the
    // map's own heights (no digging, no bridges) on the water cells within 12 m, a little lower: it changes only slowly along a river,
    // and every piece gets the same, as it comes from the map alone. NaN: no water near.
    float[] lakeLevel;
    const float LakeDrop = 0.35f;       // the surface under the lowest map height of the water round about

    void LakeLevels()
    {
        lakeLevel = new float[N * N];
        for (int k = 0; k < lakeLevel.Length; k++) lakeLevel[k] = float.NaN;
        const float Reach = 12f;
        double c = layer.cell;
        int i0 = (int)Math.Floor((input.centerE - R - Reach - layer.minE) / c), i1 = (int)Math.Floor((input.centerE + R + Reach - layer.minE) / c);
        int j0 = (int)Math.Floor((input.centerN - R - Reach - layer.minN) / c), j1 = (int)Math.Floor((input.centerN + R + Reach - layer.minN) / c);
        int ci = i1 - i0 + 1, cj = j1 - j0 + 1;
        var cellH = new float[ci * cj];
        bool any = false;
        for (int j = 0; j < cj; j++)
            for (int i = 0; i < ci; i++)
            {
                double e = layer.minE + (i0 + i + 0.5) * c, n = layer.minN + (j0 + j + 0.5) * c;
                bool wet = Base(layer.At(e, n)) == WorldCell.Water;
                cellH[j * ci + i] = wet ? (input.heights != null ? input.heights.At(e, n) : 0f) - baseHeight : float.NaN;
                any |= wet;
            }
        if (!any) return;
        // The lowest along each row first (within 12 m across), then down the columns, inside the circle's square
        int r = (int)Math.Ceiling(Reach / c);
        var rowMin = new float[ci * cj];
        for (int j = 0; j < cj; j++)
            for (int i = 0; i < ci; i++)
            {
                float m = float.NaN;
                for (int d = -r; d <= r; d++)
                {
                    int ii = i + d;
                    if (ii < 0 || ii >= ci) continue;
                    float v = cellH[j * ci + ii];
                    if (!float.IsNaN(v) && (float.IsNaN(m) || v < m)) m = v;
                }
                rowMin[j * ci + i] = m;
            }
        for (int k = 0; k < N * N; k++)
        {
            if (wLvl[k] < -3f) continue;
            double e = input.centerE + (k % N - R), n = input.centerN + (k / N - R);
            int i = (int)Math.Floor((e - layer.minE) / c) - i0, j = (int)Math.Floor((n - layer.minN) / c) - j0;
            float m = float.NaN;
            for (int d = -r; d <= r; d++)
            {
                int jj = j + d;
                if (jj < 0 || jj >= cj || i < 0 || i >= ci) continue;
                float v = rowMin[jj * ci + i];
                if (!float.IsNaN(v) && (float.IsNaN(m) || v < m)) m = v;
            }
            if (!float.IsNaN(m)) lakeLevel[k] = m - LakeDrop;
        }
    }

    // The lake's surface at a point from the map alone (as LakeLevels): the lowest map height of the water cells
    // within reach metres, minus the same LakeDrop; NaN when no water is that near
    float LakeLevelAt(float x, float z, float reach)
    {
        double c = layer.cell, e0 = input.centerE + x, n0 = input.centerN + z;
        int r = (int)Math.Ceiling(reach / c);
        int ic = (int)Math.Floor((e0 - layer.minE) / c), jc = (int)Math.Floor((n0 - layer.minN) / c);
        float m = float.NaN;
        for (int dj = -r; dj <= r; dj++)
            for (int di = -r; di <= r; di++)
            {
                double e = layer.minE + (ic + di + 0.5) * c, n = layer.minN + (jc + dj + 0.5) * c;
                if (Base(layer.At(e, n)) != WorldCell.Water) continue;
                float h = (input.heights != null ? input.heights.At(e, n) : 0f) - baseHeight;
                if (float.IsNaN(m) || h < m) m = h;
            }
        if (float.IsNaN(m)) return m;
        // The lake's own level round about, as its surface is drawn
        float wide = float.NaN;
        int R2 = (int)Math.Ceiling(12f / c);
        for (int dj = -R2; dj <= R2; dj++)
            for (int di = -R2; di <= R2; di++)
            {
                double e = layer.minE + (ic + di + 0.5) * c, n = layer.minN + (jc + dj + 0.5) * c;
                if (Base(layer.At(e, n)) != WorldCell.Water) continue;
                float h = (input.heights != null ? input.heights.At(e, n) : 0f) - baseHeight;
                if (float.IsNaN(wide) || h < wide) wide = h;
            }
        return wide - LakeDrop;
    }

    // The map's heights with a little roughness, before knolls, cliffs and ditches
    float Plain(float x, float z) => BaseHeight(x, z) - baseHeight + (TvRandom.Patches((x + ox) * 0.35f + 31f, (z + oz) * 0.35f + 17f) - 0.5f) * 0.22f
                                     * (input.town != null && Smooth(CellAt(x, z)) ? 0.12f : 1f);
    // Streets, squares and houses of a town are smooth
    static bool Smooth(byte cell) { int b = cell & WorldCell.BaseMask; return b == WorldCell.Paved || b == WorldCell.Building; }

    void Bump(float x, float z, float height, float spread)
    {
        float reach = (float)Math.Sqrt(spread) * 3.2f;
        ForNear(x, z, x, z, reach, (k, d, qx, qz) => F[k] += height * (float)Math.Exp(-d * d / spread));
    }

    // A ridge (amount > 0) or a groove along a line; wet: with water at the bottom
    void Groove(float[] c, float amount, float width, float earth, bool wet, float waterWidth = 0f)
    {
        var done = new Dictionary<int, float>();
        for (int q = 0; q + 3 < c.Length; q += 2)
        {
            ForNear(c[q], c[q + 1], c[q + 2], c[q + 3], width + 1f, (k, d, qx, qz) =>
            {
                if (!done.TryGetValue(k, out float best) || d < best) done[k] = d;
            });
        }
        foreach (var pair in done)
        {
            float d = pair.Value, shape = 1f - Sm(0f, width, d);
            F[pair.Key] += amount * shape;
            soil[pair.Key] = Math.Max(soil[pair.Key], shape * earth);
            if (wet)
            {
                // Wider and narrower along the line, the same on every computer (by where it is on the map)
                float px = pair.Key % N - R + ox, pz = pair.Key / N - R + oz;
                float w = waterWidth * (0.7f + 0.6f * TvRandom.Noise(px * 0.07f + 13.1f, pz * 0.07f - 5.3f));
                water[pair.Key] = Math.Max(water[pair.Key], 1f - Sm(w * 0.5f, w + 0.5f, d));
            }
            clear[pair.Key] = Math.Min(clear[pair.Key], d - width * 0.5f);
        }
    }

    // A cliff is a step in the ground: the side the ground falls towards drops, and the face shows bare rock.
    // minDrop and maxDrop: how high the face is in the middle of the cliff, whatever the ground says; towards the
    // ends it is lower (2026-11-24, Anders: cliffs stuck up a little too high and made small pits; a cliff should be
    // its full height, 2-6 m, in the middle and gentler at its ends, and fit the contours as a person would see it)
    void Cliff(float[] c, float minDrop, float maxDrop, bool rocky)
    {
        // Which side is low, from the map's heights 5 m either side of the whole line (2026-11-20, Anders: a cliff
        // faced the wrong way; it was decided by the slope at the line's middle alone, and one direction for the
        // whole line turned a curved cliff's other end round). Level ground: the right side, as the map draws it.
        float vote = 0f, total = 0f;
        for (int q = 0; q + 3 < c.Length; q += 2)
        {
            float sx = c[q + 2] - c[q], sz = c[q + 3] - c[q + 1], l = (float)Math.Sqrt(sx * sx + sz * sz);
            if (l < 1e-4f) continue;
            float rx = sz / l, rz = -sx / l, px = (c[q] + c[q + 2]) / 2f, pz = (c[q + 1] + c[q + 3]) / 2f;
            vote += l * (BaseHeight(px - rx * 5f, pz - rz * 5f) - BaseHeight(px + rx * 5f, pz + rz * 5f));
            total += l;
        }
        if (total < 1e-3f) return;
        float lowSide = vote < -0.02f * total ? -1f : 1f;
        if (rocky) cliffs.Add((lowSide, c));

        // How far along the line each corner is
        int corners = c.Length / 2;
        var along = new float[corners];
        for (int p = 1; p < corners; p++)
        {
            float sx = c[p * 2] - c[p * 2 - 2], sz = c[p * 2 + 1] - c[p * 2 - 1];
            along[p] = along[p - 1] + (float)Math.Sqrt(sx * sx + sz * sz);
        }
        float length = along[corners - 1];
        // The cliff may go on in another piece of line drawn end to end with this one: it tapers only at its real ends
        stepEnds.TryGetValue(c, out var more);
        float whole = length + more.before + more.after;
        float endLength = Math.Max(1.5f, Math.Min(6f, whole * 0.35f));

        // Stations every metre or so along the line: the ground's height a few metres above the edge and down at the
        // foot, evened out along the line, so the cliff's height changes smoothly along it and one odd spot in the
        // map's heights (a hollow or a knoll by the foot) does not make a pit or a bump at the cliff
        const float Reach = 6f, TopReach = 3f, Up = 0.65f, LiftBack = 4f;
        int stations = Math.Max(2, (int)Math.Ceiling(length / 1f) + 1);
        float gap = length / (stations - 1);
        var top = new float[stations];
        var foot = new float[stations];
        for (int t = 0; t < stations; t++)
        {
            float s = t * gap;
            PointOn(c, along, s, out float px, out float pz);
            PointOn(c, along, Math.Max(0f, s - 0.75f), out float bx, out float bz);
            PointOn(c, along, Math.Min(length, s + 0.75f), out float fx, out float fz);
            float sx = fx - bx, sz = fz - bz, l = (float)Math.Sqrt(sx * sx + sz * sz);
            if (l < 1e-5f) { sx = c[2] - c[0]; sz = c[3] - c[1]; l = Math.Max(1e-5f, (float)Math.Sqrt(sx * sx + sz * sz)); }
            float nx = sz / l * lowSide, nz = -sx / l * lowSide;
            top[t] = (BaseHeight(px - nx * (TopReach - 0.5f), pz - nz * (TopReach - 0.5f)) + BaseHeight(px - nx * (TopReach + 0.5f), pz - nz * (TopReach + 0.5f))) * 0.5f;
            foot[t] = (BaseHeight(px + nx * (Reach - 1f), pz + nz * (Reach - 1f)) + BaseHeight(px + nx * (Reach + 0.5f), pz + nz * (Reach + 0.5f))) * 0.5f;
        }
        EvenOut(top, 3);
        EvenOut(foot, 3);
        // The face's height at each station: what the ground drops there, held between the least and the most height,
        // and lower towards the cliff's ends (a third of it at the very end)
        var step = new float[stations];
        var drop = new float[stations];
        for (int t = 0; t < stations; t++)
        {
            float s = t * gap;
            float toEnd = Math.Min(s + more.before, length - s + more.after);
            float taper = 0.35f + 0.65f * Sm(0f, endLength, toEnd);
            drop[t] = Math.Max(0f, top[t] - foot[t]);
            step[t] = Math.Max(minDrop, Math.Min(maxDrop, drop[t])) * taper;
        }

        var best = new Dictionary<int, (float d, float side, float s, float nx, float nz)>();
        for (int q = 0; q + 3 < c.Length; q += 2)
        {
            float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
            float nx = bz - az, nz = -(bx - ax), nl = (float)Math.Sqrt(nx * nx + nz * nz);
            if (nl < 1e-6f) continue;
            nx *= lowSide / nl;
            nz *= lowSide / nl;
            float s0 = along[q / 2];
            ForNear(ax, az, bx, bz, 13f, (k, d, qx, qz) =>
            {
                if (best.TryGetValue(k, out var known) && known.d <= d) return;
                float px = k % N - R, pz = k / N - R;
                float s = s0 + (float)Math.Sqrt((qx - ax) * (qx - ax) + (qz - az) * (qz - az));
                best[k] = (d, (px - qx) * nx + (pz - qz) * nz, s, nx, nz);
            });
        }
        // The map's heights already have the cliff's height as a slope across it (the contours bunch up there). The
        // slope is gathered into a step: the ground just above is kept at the height a few metres above the edge, and
        // the ground below starts at the face's foot and runs on down to the height a few metres out. Where the
        // ground drops more than the face is high, the rest stays a slope below it; where it drops less, the rest is
        // made up, a little more by lifting the ground above the edge (a cliff stands up and is seen from afar,
        // 2026-11-21) than by lowering the foot, and both go back to the map's height gently, so there is no lip on
        // top and no pit at the foot.
        foreach (var pair in best)
        {
            float d = pair.Value.d, side = pair.Value.side;
            if (d > 12f) continue;
            float at = Math.Max(0f, Math.Min(stations - 1.001f, pair.Value.s / gap));
            int t0 = (int)at;
            float f = at - t0;
            float high = Lerp(top[t0], top[t0 + 1], f), height = Lerp(step[t0], step[t0 + 1], f), fall = Lerp(drop[t0], drop[t0 + 1], f);
            float extra = Math.Max(0f, height - fall);
            float lift = extra * Up;
            float bottom = high + lift - height, low = high - fall;
            // A rock cliff drops at once (a vertical face is built on it, CliffFaces); an earth bank slopes
            // The ground's own step (a slope a metre wide, as the ground has a corner every metre) lies just above the
            // line, behind the face, which stands on the line itself (CliffFaces)
            float down = rocky ? Sm(-0.95f, -0.15f, side) : Sm(-0.5f, 0.9f, side);
            int k = pair.Key;
            float here = BaseHeight(k % N - R, k / N - R);
            // Past the ends of the line the step fades out, so a cliff does not wrap round its own end
            float beyond = (float)Math.Sqrt(Math.Max(0f, d * d - side * side));
            float ends = 1f - Sm(0.5f, 3f, beyond);
            // Above the edge only the strip next to it is made level; below, the slope from the foot of the face
            float aboveWeight = 1f - Sm(TopReach - 1.5f, TopReach + 1f, d);
            float belowWeight = 1f - Sm(Reach - 1.5f, Reach + 1f, d);
            float below = bottom + (low - bottom) * Sm(0.4f, Reach, side);
            float delta = (1f - down) * (high - here) * aboveWeight + down * (below - here) * belowWeight;
            // The lifted top slopes back gently into the hillside, so it is a shoulder and not a lip on the edge
            if (lift > 0f) delta += lift * (1f - down) * (1f - Sm(0.5f, 3.5f + lift * LiftBack, -side));
            AddCliffStep(k, delta * ends);
            float face = Sm(-0.8f, 0.2f, side) * (1f - Sm(1.3f, 2.6f, d)) * (height > 0.6f ? 1f : height / 0.6f);
            if (rocky) rock[k] = Math.Max(rock[k], face);
            else soil[k] = Math.Max(soil[k], face * 0.8f);
        }
    }

    // Two cliffs side by side or end to end must not both lower (or lift) the same ground, or it sinks into a pit:
    // of two changes the same way the larger one is kept; opposite ones add up
    float[] cliffStep;
    void AddCliffStep(int k, float v)
    {
        float o = cliffStep[k];
        if (o == 0f || (o < 0f) != (v < 0f)) cliffStep[k] = o + v;
        else if (Math.Abs(v) > Math.Abs(o)) cliffStep[k] = v;
    }

    // The point the distance s along a line (along: how far along it each corner is)
    static void PointOn(float[] c, float[] along, float s, out float x, out float z)
    {
        int p = 1;
        while (p < along.Length - 1 && along[p] < s) p++;
        float l = along[p] - along[p - 1], f = l > 1e-5f ? Math.Max(0f, Math.Min(1f, (s - along[p - 1]) / l)) : 0f;
        x = Lerp(c[p * 2 - 2], c[p * 2], f);
        z = Lerp(c[p * 2 - 1], c[p * 2 + 1], f);
    }

    // Evens out a row of values: the mean of each value and its neighbours up to reach away, twice over
    static void EvenOut(float[] v, int reach)
    {
        var copy = new float[v.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            Array.Copy(v, copy, v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int j = Math.Max(0, i - reach); j <= Math.Min(v.Length - 1, i + reach); j++) { sum += copy[j]; count++; }
                v[i] = sum / count;
            }
        }
    }

    // ---- Cliffs drawn in pieces: the lines of a cliff (or a small cliff, or an earth bank) drawn end to end are one
    // cliff, so it gets lower only at its real ends. For every such line: how far the cliff goes on past its first
    // corner (before) and past its last (after). Worked out once for the whole map, so every piece of the
    // run-yourself world sees the same cliff.

    // The step lines near this world and how far each goes on past its ends (by the line's points, set in NearLines)
    readonly Dictionary<float[], (float before, float after)> stepEnds = new Dictionary<float[], (float before, float after)>();

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldLayer, Dictionary<WorldLine, (float before, float after)>> cliffChains
        = new System.Runtime.CompilerServices.ConditionalWeakTable<WorldLayer, Dictionary<WorldLine, (float before, float after)>>();

    static bool IsStepLine(WorldLineKind k) => k == WorldLineKind.Cliff || k == WorldLineKind.SmallCliff || k == WorldLineKind.EarthBank;

    static Dictionary<WorldLine, (float before, float after)> CliffChains(WorldLayer layer)
    {
        lock (cliffChains)
        {
            if (cliffChains.TryGetValue(layer, out var known)) return known;
            var made = MakeCliffChains(layer);
            cliffChains.Add(layer, made);
            return made;
        }
    }

    static Dictionary<WorldLine, (float before, float after)> MakeCliffChains(WorldLayer layer)
    {
        var result = new Dictionary<WorldLine, (float before, float after)>();
        var steps = new List<WorldLine>();
        foreach (WorldLine line in layer.lines)
            if (IsStepLine(line.kind) && line.xy != null && line.xy.Length >= 4) steps.Add(line);
        int n = steps.Count;
        var length = new float[n];
        // Line ends in 1 m squares: end e is line e / 2, its first corner when e is even, its last when odd
        var squares = new Dictionary<long, List<int>>();
        float EndX(int e) { float[] xy = steps[e / 2].xy; return (e & 1) == 0 ? xy[0] : xy[xy.Length - 2]; }
        float EndZ(int e) { float[] xy = steps[e / 2].xy; return (e & 1) == 0 ? xy[1] : xy[xy.Length - 1]; }
        long Square(float x, float z) => ((long)Math.Floor(x) << 32) ^ (long)(uint)(int)Math.Floor(z);
        for (int i = 0; i < n; i++)
        {
            float[] xy = steps[i].xy;
            for (int p = 2; p + 1 < xy.Length; p += 2)
                length[i] += (float)Math.Sqrt((xy[p] - xy[p - 2]) * (xy[p] - xy[p - 2]) + (xy[p + 1] - xy[p - 1]) * (xy[p + 1] - xy[p - 1]));
            for (int e = i * 2; e <= i * 2 + 1; e++)
            {
                long key = Square(EndX(e), EndZ(e));
                if (!squares.TryGetValue(key, out var list)) squares[key] = list = new List<int>();
                list.Add(e);
            }
        }
        // Each end's nearest end of another line of the same kind, within a metre
        const float Join = 1f;
        var next = new int[n * 2];
        for (int e = 0; e < n * 2; e++)
        {
            next[e] = -1;
            float x = EndX(e), z = EndZ(e), bestD = Join * Join;
            int fx = (int)Math.Floor(x), fz = (int)Math.Floor(z);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!squares.TryGetValue(((long)(fx + dx) << 32) ^ (long)(uint)(fz + dz), out var list)) continue;
                    foreach (int o in list)
                    {
                        if (o / 2 == e / 2 || steps[o / 2].kind != steps[e / 2].kind) continue;
                        float ddx = EndX(o) - x, ddz = EndZ(o) - z, dd = ddx * ddx + ddz * ddz;
                        if (dd <= bestD) { bestD = dd; next[e] = o; }
                    }
                }
            }
        }
        // How far the cliff goes on past an end: along the joined lines, far enough for the taper (20 m)
        float Past(int e)
        {
            float sum = 0f;
            var seen = new HashSet<int> { e / 2 };
            int at = e;
            while (sum < 20f)
            {
                int o = next[at];
                if (o < 0 || !seen.Add(o / 2)) break;
                sum += length[o / 2];
                at = o ^ 1;   // on out from the joined line's other end
            }
            return sum;
        }
        for (int i = 0; i < n; i++) result[steps[i]] = (Past(i * 2), Past(i * 2 + 1));
        return result;
    }

    // ---- Paths and roads: a colour on the ground, and no trees on them ----

    static readonly TvColor Gravel = TvColor.Hex("#9a917f"), Earth = TvColor.Hex("#8c7250"), Asphalt = TvColor.Hex("#6f6e6c"),
        RideGrass = TvColor.Hex("#7d8a4a"), Ballast = TvColor.Hex("#7f7a73");

    void Tracks()
    {
        foreach (var line in lines)
        {
            float half, strength;
            TvColor color;
            switch (line.kind)
            {
                // Roads are tarmac (2026-11-20, Anders: a narrow asphalt road looked like a path in the forest)
                case WorldLineKind.WideRoad: half = 2.6f; strength = 1f; color = Asphalt; break;
                case WorldLineKind.Road: half = 1.9f; strength = 1f; color = Asphalt; break;
                // Paths a little wider and clearer than in real life (2026-11-19, Anders: hard to find; the ground
                // has one corner per metre, so a narrow path barely showed)
                case WorldLineKind.Track: half = 1.5f; strength = 1f; color = Earth; break;
                case WorldLineKind.Footpath: half = 1.05f; strength = 1f; color = Earth; break;
                case WorldLineKind.SmallPath: half = 0.85f; strength = 0.95f; color = Earth; break;
                case WorldLineKind.FaintPath: half = 0.65f; strength = 0.75f; color = Earth; break;
                case WorldLineKind.Ride: half = 1.6f; strength = 0.35f; color = RideGrass; break;
                case WorldLineKind.Railway: half = 2.2f; strength = 1f; color = Ballast; break;
                case WorldLineKind.Wall: case WorldLineKind.RuinedWall: case WorldLineKind.HighWall:
                case WorldLineKind.Fence: case WorldLineKind.RuinedFence: case WorldLineKind.HighFence:
                    half = 0.3f; strength = 0f; color = Earth; break;
                case WorldLineKind.PowerLine: case WorldLineKind.MajorPowerLine:
                    half = line.kind == WorldLineKind.MajorPowerLine ? 9f : 4f; strength = 0f; color = Earth; break;
                default: continue;
            }
            float[] c = line.xz;
            bool tarmac = line.kind == WorldLineKind.WideRoad || line.kind == WorldLineKind.Road;
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                ForNear(c[q], c[q + 1], c[q + 2], c[q + 3], half + 3f, (k, d, qx, qz) =>
                {
                    clear[k] = Math.Min(clear[k], d - half);
                    if (tarmac) AsphaltAt(k, half - d);
                    if (strength <= 0f) return;
                    float blend = (1f - Sm(half * 0.45f, half + 0.35f, d)) * strength;
                    if (blend > trackBlend[k])
                    {
                        trackBlend[k] = blend;
                        trackColor[k] = color;
                    }
                });
            }
        }
        if (input.town != null) TownPathTracks();
    }

    // A town's paths and paved ways drawn as lines (2026-11-23, Anders: the paved path to the Tranås bridge was the
    // zigzag of the 2 m cells, and nothing kept trees off it): a straight strip along the line, asphalt when paved,
    // earth when not, kept clear of trees and stones. Paved areas of the map are already paved.
    void TownPathTracks()
    {
        TownLines();
        foreach (var (c, w, paved) in townPaths)
        {
            float half = Math.Max(0.6f, Math.Min(3f, w / 2f));
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                ForNear(c[q], c[q + 1], c[q + 2], c[q + 3], half + 2f, (k, d, qx, qz) =>
                {
                    clear[k] = Math.Min(clear[k], d - half);
                    int b = Base(CellAt(k % N - R, k / N - R));
                    // A paved path is asphalt all along its line, its own paved cells too (2026-11-23, Anders: grass
                    // broke a paved path where its cells' smooth share was low)
                    if (paved && b != WorldCell.Building && b != WorldCell.Water)
                    {
                        AsphaltAt(k, half - d);
                        return;
                    }
                    if (b == WorldCell.Paved || b == WorldCell.Building || b == WorldCell.Water) return;
                    float blend = 1f - Sm(half * 0.6f, half + 0.3f, d);
                    if (blend > trackBlend[k])
                    {
                        trackBlend[k] = blend;
                        trackColor[k] = Earth;
                    }
                });
            }
        }
    }

    float ClearAt(float x, float z)
    {
        int i = (int)Math.Round(x) + R, j = (int)Math.Round(z) + R;
        if (i < 0 || j < 0 || i >= N || j >= N) return 99f;
        return clear[j * N + i];
    }

    // ---- The ground mesh, coloured by what the map says is there ----

    struct GroundLook
    {
        public TvColor a, b, c;      // the main colour, patches of a second, and a third in smaller spots
    }

    GroundLook ForestFloor()
    {
        switch (look.ground)
        {
            case TerrainLook.Heather: return new GroundLook { a = TvColor.Hex("#5f5e3c"), b = TvColor.Hex("#74566a"), c = TvColor.Hex("#6a5a3a") };
            case TerrainLook.Grass: return new GroundLook { a = TvColor.Hex("#7f9448"), b = TvColor.Hex("#9aa556"), c = TvColor.Hex("#6f8440") };
            case TerrainLook.Hard: return new GroundLook { a = TvColor.Hex("#6a5a3a"), b = TvColor.Hex("#75683f"), c = TvColor.Hex("#5d6a3a") };
            case TerrainLook.Sand: return new GroundLook { a = TvColor.Hex("#b9a577"), b = TvColor.Hex("#a8946a"), c = TvColor.Hex("#8f8a5a") };
            case TerrainLook.Stone: return new GroundLook { a = TvColor.Hex("#9c9b8d"), b = TvColor.Hex("#86934f"), c = TvColor.Hex("#b0b09c") };
            case TerrainLook.Lichen: return new GroundLook { a = TvColor.Hex("#a9ad97"), b = TvColor.Hex("#8b8f73"), c = TvColor.Hex("#6a5a3a") };
            default: return new GroundLook { a = TvColor.Hex("#4d5f35"), b = TvColor.Hex("#627039"), c = TvColor.Hex("#625437") };
        }
    }

    static readonly TvColor Open = TvColor.Hex("#a39a58"), OpenGreen = TvColor.Hex("#8a9a4e"), Rough = TvColor.Hex("#4f4530"),
        RoughGreen = TvColor.Hex("#4a5630"), LightGreen = TvColor.Hex("#48642f"), MidGreen = TvColor.Hex("#3a5428"), DarkGreen = TvColor.Hex("#2f4722"),
        Field = TvColor.Hex("#b8a85a"), Field2 = TvColor.Hex("#a39a50"), Water = TvColor.Hex("#4c6a7c"), WaterDeep = TvColor.Hex("#3c5566"),
        Rock = TvColor.Hex("#a3a39f"), RockDark = TvColor.Hex("#8d8d89"), LichenRock = TvColor.Hex("#b9bba6"), SandColor = TvColor.Hex("#c2ad7c"),
        Paved = TvColor.Hex("#8a8782"), Plot = TvColor.Hex("#8f9a4e"), MarshColor = TvColor.Hex("#6f7440"), Wet = TvColor.Hex("#4a4a2e"),
        Soil = TvColor.Hex("#6b563c");

    // How far inside a tarmac road a ground corner is (metres, below 0 outside), for the road's smooth edge
    float[] asphaltLevel;

    void AsphaltAt(int k, float level)
    {
        if (asphaltCorner == null)
        {
            asphaltCorner = new bool[N * N];
            asphaltLevel = new float[N * N];
            for (int i = 0; i < asphaltLevel.Length; i++) asphaltLevel[i] = -9f;
        }
        if (level > asphaltLevel[k]) asphaltLevel[k] = level;
        if (level >= 0f) asphaltCorner[k] = true;
    }

    bool[] asphaltCorner;               // the ground corners on a tarmac road (Tracks), kept with the paved ground
    static readonly TvColor Lawn = TvColor.Hex("#5f8a36"), LawnLight = TvColor.Hex("#76a043");

    // What lies beside a garden's cells where the map's straight edge leaves them out of the garden: the nearest cell
    // round about that is no garden (a street, a lawn), else open land
    byte NotGardenNear(float x, float z)
    {
        for (float d = 1.5f; d <= 4.6f; d += 1.5f)
            for (int a = 0; a < 8; a++)
            {
                float ang = a * 0.785398f;
                byte c = CellAt(x + (float)Math.Cos(ang) * d, z + (float)Math.Sin(ang) * d);
                int b = Base(c);
                if (b != WorldCell.Plot && b != WorldCell.Building && b != WorldCell.Water) return c;
            }
        return WorldCell.Open;
    }


    // ---- The map's edge lines (TvEdges): the ground follows them up close ----

    float[] paveLevel;                  // how far inside the paving each ground corner is (metres; below 0 outside)

    float[] eDist, eLvl;                // the nearest edge line round each corner: distance, and signed (+ on its left)
    int[] eId;                          // which line (-1: none within reach)
    byte[] eCell;                       // the ground on the corner's side of it
    float[] wLvl;
    float[] quay;                       // 0..1: near a quay wall on its water side (EdgeField)                       // how far inside the water each corner is (metres; below 0 on land)
    float[] pDist, pLvl;                // the nearest edge line with paving on one side only: distance, + on the paved side
    const float EdgeReach = 1.6f;
    byte[] gKind;                       // a town: the ground kind each corner got (houses as paving)

    static int Kind2(byte cell) { int b = cell & WorldCell.BaseMask; return b == WorldCell.Building ? WorldCell.Paved : b; }

    // The ground kind of a point for the edge lines' sides (houses as paving)
    byte SideCell(float x, float z) => input.town != null ? SmoothCell(x, z) : CellAt(x, z);

    // The share of cells of one kind round a point, with the same cubic B-spline weights as SmoothCell
    float KindWeight(float x, float z, int kind)
    {
        double fe = (input.centerE + x - layer.minE) / layer.cell - 0.5, fn = (input.centerN + z - layer.minN) / layer.cell - 0.5;
        int i0 = (int)Math.Floor(fe), j0 = (int)Math.Floor(fn);
        Span4((float)(fe - i0), out float x0, out float x1, out float x2, out float x3);
        Span4((float)(fn - j0), out float z0, out float z1, out float z2, out float z3);
        float w = 0f;
        for (int dj = 0; dj < 4; dj++)
        {
            float wz = dj == 0 ? z0 : dj == 1 ? z1 : dj == 2 ? z2 : z3;
            for (int di = 0; di < 4; di++)
            {
                int b = Kind2(layer.At(layer.minE + (i0 + di - 1 + 0.5) * layer.cell, layer.minN + (j0 + dj - 1 + 0.5) * layer.cell));
                if (b == kind) w += (di == 0 ? x0 : di == 1 ? x1 : di == 2 ? x2 : x3) * wz;
            }
        }
        return w;
    }

    float PavedWeight(float x, float z) => KindWeight(x, z, WorldCell.Paved);

    // For every ground corner: the nearest edge line and the ground on its side, and the water's and paving's levels.
    // A stretch of line is used where its two sides (1.3 m off its middle) differ; a stretch with the same ground on
    // both sides (a step inside a square, a boundary the cells do not show) is left out.
    void EdgeField()
    {
        int n = N * N;
        eDist = new float[n]; eLvl = new float[n]; eId = new int[n]; eCell = new byte[n];
        pDist = new float[n]; pLvl = new float[n]; wLvl = new float[n]; quay = new float[n];
        var wDist = new float[n];
        for (int k = 0; k < n; k++) { eDist[k] = pDist[k] = wDist[k] = 99f; eId[k] = -1; }
        TvEdges edges = TvEdges.Of(layer);
        if (edges != null)
        {
            float lo = -R - 3f, hi = R + 3f;
            for (int li = 0; li < edges.lines.Count; li++)
            {
                float[] b = edges.bounds[li];
                if (b[2] - cE < lo || b[0] - cE > hi || b[3] - cN < lo || b[1] - cN > hi) continue;
                float[] xy = edges.lines[li];
                for (int q = 0; q + 3 < xy.Length; q += 2)
                {
                    float ax = (float)(xy[q] - input.centerE), az = (float)(xy[q + 1] - input.centerN);
                    float bx = (float)(xy[q + 2] - input.centerE), bz = (float)(xy[q + 3] - input.centerN);
                    if (Math.Max(ax, bx) < lo || Math.Min(ax, bx) > hi || Math.Max(az, bz) < lo || Math.Min(az, bz) > hi) continue;
                    float sx = bx - ax, sz = bz - az, sl = (float)Math.Sqrt(sx * sx + sz * sz);
                    if (sl < 0.05f) continue;
                    float nx = -sz / sl, nz = sx / sl, mx = (ax + bx) / 2f, mz = (az + bz) / 2f;
                    byte left = SideCell(mx + nx * 1.3f, mz + nz * 1.3f), right = SideCell(mx - nx * 1.3f, mz - nz * 1.3f);
                    int kl = Kind2(left), kr = Kind2(right);
                    if (kl == kr) continue;
                    // A house's cell keeps the house (its walls stand there): the side takes the paving round it
                    if (Base(left) == WorldCell.Building) left = (byte)((left & ~WorldCell.BaseMask) | WorldCell.Paved);
                    if (Base(right) == WorldCell.Building) right = (byte)((right & ~WorldCell.BaseMask) | WorldCell.Paved);
                    bool water = kl == WorldCell.Water || kr == WorldCell.Water, paved = kl == WorldCell.Paved || kr == WorldCell.Paved;
                    float wSide = kl == WorldCell.Water ? 1f : -1f, pSide = kl == WorldCell.Paved ? 1f : -1f;
                    int id = li;
                    ForNear(ax, az, bx, bz, EdgeReach, (k, d, qx, qz) =>
                    {
                        float vx = k % N - R, vz = k / N - R;
                        float side = (vx - qx) * nx + (vz - qz) * nz >= 0f ? 1f : -1f;
                        float dd = Math.Max(d, 0.02f);
                        if (d < eDist[k])
                        {
                            eDist[k] = d; eLvl[k] = side * dd; eId[k] = id; eCell[k] = side > 0f ? left : right;
                        }
                        if (water && d < wDist[k]) { wDist[k] = d; wLvl[k] = side * wSide * dd; }
                        if (paved && d < pDist[k]) { pDist[k] = d; pLvl[k] = side * pSide * dd; }
                    });
                }
            }
        }
        // In a town a wall or fence with water on one side is the water's edge (2026-11-24, Anders: a street along a
        // quay wall sloped down into the water before the wall, with water and stones on the street)
        if (input.town != null && lines != null)
        {
            foreach (var line in lines)
            {
                switch (line.kind)
                {
                    case WorldLineKind.Barrier: case WorldLineKind.Wall: case WorldLineKind.HighWall: case WorldLineKind.HighFence:
                        break;
                    default:
                        continue;
                }
                float[] c = line.xz;
                for (int q = 0; q + 3 < c.Length; q += 2)
                {
                    float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
                    if (Math.Max(ax, bx) < -R - 3f || Math.Min(ax, bx) > R + 3f || Math.Max(az, bz) < -R - 3f || Math.Min(az, bz) > R + 3f) continue;
                    float sx = bx - ax, sz = bz - az, sl = (float)Math.Sqrt(sx * sx + sz * sz);
                    if (sl < 0.05f) continue;
                    float nx = -sz / sl, nz = sx / sl, mx = (ax + bx) / 2f, mz = (az + bz) / 2f;
                    bool wl = Base(SideCell(mx + nx * 1.6f, mz + nz * 1.6f)) == WorldCell.Water, wr = Base(SideCell(mx - nx * 1.6f, mz - nz * 1.6f)) == WorldCell.Water;
                    if (wl == wr) continue;
                    float wSide = wl ? 1f : -1f;
                    // Deep water only by a street's quay wall; by a wall or barrier along grass the shore shelves
                    // gently (2026-11-25, Anders: the grass by the water fell in 1 m jagged teeth)
                    bool street = Base(SideCell(mx - nx * wSide * 1.6f, mz - nz * wSide * 1.6f)) == WorldCell.Paved;
                    ForNear(ax, az, bx, bz, 4f, (k, d, qx, qz) =>
                    {
                        float vx = k % N - R, vz = k / N - R;
                        float side = (vx - qx) * nx + (vz - qz) * nz >= 0f ? 1f : -1f;
                        // Its water side only where the map has water: a fence running on past the water's end (at a
                        // bridge's ends) made blue sunken boxes on the land beside it (2026-11-25, Anders)
                        bool dry = side * wSide > 0f && KindWeight(vx, vz, WorldCell.Water) < 0.25f;
                        if (!dry && d < EdgeReach && d < wDist[k]) { wDist[k] = d; wLvl[k] = side * wSide * Math.Max(d, 0.02f); }
                        // The water by a quay wall is deep: its surface lies well below the street
                        if (side * wSide > 0f && street) quay[k] = Math.Max(quay[k], 1f - Sm(2.5f, 4f, d));
                    });
                }
            }
        }
        for (int k = 0; k < n; k++)
        {
            float x = k % N - R, z = k / N - R;
            if (wDist[k] >= EdgeReach) wLvl[k] = (KindWeight(x, z, WorldCell.Water) - 0.5f) * 2.5f;
            if (BridgeWater(x, z)) wLvl[k] = Math.Max(wLvl[k], 1f);
        }
    }

    // The ground near a point that is not of one kind (a corner the exact edge leaves outside the water or paving)
    byte NotKindNear(float x, float z, int kind)
    {
        for (float d = 1f; d <= 4.1f; d += 1.5f)
            for (int a = 0; a < 8; a++)
            {
                float ang = a * 0.785398f;
                byte c = CellAt(x + (float)Math.Cos(ang) * d, z + (float)Math.Sin(ang) * d);
                int b = Base(c);
                if (b != kind && b != WorldCell.Building && b != WorldCell.Water && !(kind == WorldCell.Paved && b == WorldCell.Paved)) return c;
            }
        return WorldCell.Open;
    }

    // The ground cut along the edge lines: a triangle whose three corners have the same line nearest and lie on both
    // sides of it is split there, each part with its own side's colour, so the edge is the map's line and not a blend
    // across the 1 m squares. Up close only (the near ground); the far ground keeps its corners.
    void CutGroundEdges()
    {
        if (eId == null || groundMesh == null) return;
        var t = groundMesh.triangles;
        var kept = new List<int>(t.Count + 64);
        int baseCount = N * N;
        var poly = new List<int>(5);
        for (int q = 0; q + 2 < t.Count; q += 3)
        {
            int v0 = t[q], v1 = t[q + 1], v2 = t[q + 2];
            if (v0 >= baseCount || v1 >= baseCount || v2 >= baseCount) { kept.Add(v0); kept.Add(v1); kept.Add(v2); continue; }
            bool cut = eId[v0] >= 0 && eId[v0] == eId[v1] && eId[v0] == eId[v2]
                       && !((eLvl[v0] >= 0f) == (eLvl[v1] >= 0f) && (eLvl[v0] >= 0f) == (eLvl[v2] >= 0f));
            int[] vs = { v0, v1, v2 };
            float[] lv = { eLvl[v0], eLvl[v1], eLvl[v2] };
            if (!cut && gKind != null)
            {
                // A town's ground with no edge line here: two kinds meet in the triangle, cut where their smooth
                // shares of the cells round are equal, so the edge runs smooth and not as the 1 m squares
                int ka = gKind[v0], kb = gKind[v1] != ka ? gKind[v1] : gKind[v2];
                if (ka != kb && (gKind[v2] == ka || gKind[v2] == kb) && ka != WorldCell.Paved && kb != WorldCell.Paved)
                {
                    for (int m = 0; m < 3; m++)
                    {
                        int v = vs[m];
                        float x = v % N - R, z = v / N - R;
                        float d = KindWeight(x, z, ka) - KindWeight(x, z, kb);
                        lv[m] = gKind[v] == ka ? Math.Max(d, 0.01f) : Math.Min(d, -0.01f);
                    }
                    cut = true;
                }
            }
            if (!cut) { kept.Add(v0); kept.Add(v1); kept.Add(v2); continue; }
            for (int side = 0; side < 2; side++)
            {
                bool plus = side == 0;
                poly.Clear();
                for (int e = 0; e < 3; e++)
                {
                    int a = vs[e], b = vs[(e + 1) % 3];
                    float la = lv[e], lb = lv[(e + 1) % 3];
                    if ((la >= 0f) == plus) poly.Add(a);
                    if ((la >= 0f) != (lb >= 0f))
                    {
                        float tt = Math.Max(0.02f, Math.Min(0.98f, la / (la - lb)));
                        poly.Add(CopyVertex(groundMesh, a, groundMesh, b, (la >= 0f) == plus ? a : b, tt));
                    }
                }
                for (int m = 1; m + 1 < poly.Count; m++) { kept.Add(poly[0]); kept.Add(poly[m]); kept.Add(poly[m + 1]); }
            }
        }
        t.Clear();
        t.AddRange(kept);
    }

    void Ground()
    {
        GroundLook floor = ForestFloor();
        if (input.town != null || asphaltCorner != null)
        {
            pavedCorner = new bool[N * N];
            paveLevel = new float[N * N];
        }
        gKind = input.town != null ? new byte[N * N] : null;
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                int k = j * N + i;
                float x = i - R, z = j - R;
                // The 2 m cells' edges are made ragged, so the ground does not show squares
                float ax = x + ox, az = z + oz;
                float ragged = input.town != null ? 0.9f : 3f;    // a town's edges are straight
                byte cell = input.town != null ? SmoothCell(x, z)
                    : CellAt(x + (TvRandom.Noise(ax * 0.4f, az * 0.4f) - 0.5f) * ragged, z + (TvRandom.Noise(ax * 0.4f + 9f, az * 0.4f) - 0.5f) * ragged);
                // Near an edge line of the map: the ground of the line's side the corner is on
                if (eId != null && eId[k] >= 0 && Base(cell) != WorldCell.Building) cell = eCell[k];
                int kind = Base(cell);
                // Water where the corner is inside the water's edge (the map's bank line where near; a bridge made
                // from a path over water: water round its deck, not the zigzag land strip of the cells)
                if (wLvl != null)
                {
                    if (wLvl[k] >= 0f && kind != WorldCell.Building) { kind = WorldCell.Water; cell = WorldCell.Water; }
                    else if (wLvl[k] < 0f && kind == WorldCell.Water) { cell = NotKindNear(x, z, WorldCell.Water); kind = Base(cell); }
                }
                else if (kind != WorldCell.Water && BridgeWater(x, z)) { kind = WorldCell.Water; cell = WorldCell.Water; }
                // A garden as the map draws it, straight, not the zigzag of its cells (2026-11-23, Anders)
                if (input.town != null && input.town.gardens != null && kind != WorldCell.Water && kind != WorldCell.Building)
                {
                    double ge = input.centerE + x, gn = input.centerN + z;
                    if (input.town.gardens.Near(ge, gn, 2.2f))
                    {
                        bool inGarden = input.town.gardens.Inside(ge, gn);
                        if (inGarden && kind != WorldCell.Plot) { kind = WorldCell.Plot; cell = WorldCell.Plot; }
                        else if (!inGarden && kind == WorldCell.Plot) { cell = NotGardenNear(x, z); kind = Base(cell); }
                    }
                }
                float f = TvRandom.Patches(ax * 0.12f, az * 0.12f), f2 = TvRandom.Noise(ax * 0.9f, az * 0.9f);
                TvColor c;
                bool hard = false;
                switch (kind)
                {
                    case WorldCell.Open: case WorldCell.OpenTrees: c = Open.Lerp(OpenGreen, f); break;
                    case WorldCell.RoughOpen: case WorldCell.RoughOpenTrees: c = Rough.Lerp(RoughGreen, f); break;
                    case WorldCell.LightGreen: c = LightGreen.Lerp(floor.a, f * 0.5f); break;
                    case WorldCell.Green: c = MidGreen; break;
                    case WorldCell.DarkGreen: case WorldCell.Impassable: c = DarkGreen; break;
                    case WorldCell.Cultivated: c = Field.Lerp(Field2, Sm(0.4f, 0.6f, TvRandom.Noise(ax * 0.05f, az * 0.6f))); break;
                    case WorldCell.Water: c = Water.Lerp(WaterDeep, f); hard = true; break;
                    case WorldCell.BareRock: c = Rock.Lerp(LichenRock, f2 * 0.6f); hard = true; break;
                    case WorldCell.Sand: c = SandColor.Shade(0.94f + f2 * 0.1f); break;
                    case WorldCell.Paved: case WorldCell.Building:
                        c = (input.town == null ? Paved : TownPaved(ax, az)).Shade(0.95f + f2 * 0.08f);
                        hard = true;
                        if (pavedCorner != null) pavedCorner[k] = true;
                        break;
                    // A garden: in a town a mown lawn (2026-11-23)
                    case WorldCell.Plot: c = input.town != null ? Lawn.Lerp(LawnLight, f) : Plot.Lerp(OpenGreen, f); break;
                    default:
                        c = floor.a.Lerp(floor.b, Sm(0.35f, 0.7f, f)).Lerp(floor.c, Sm(0.55f, 0.8f, TvRandom.Noise(ax * 0.2f + 40f, az * 0.2f)) * 0.7f);
                        break;
                }
                if (asphaltCorner != null && asphaltCorner[k] && kind != WorldCell.Water)
                {
                    // A tarmac road: grey asphalt on the asphalt grain, whatever the ground is around it
                    c = Asphalt.Shade(0.95f + f2 * 0.08f);
                    hard = true;
                    pavedCorner[k] = true;
                }
                if (paveLevel != null)
                {
                    // How far inside the paving the corner is: the paved ground is cut along where this is 0, so its
                    // edge runs smooth between the corners, not as the 1 m squares (2026-11-23, Anders: the paths
                    // still zigzagged). The map's own edge lines of paved areas give the edge exactly.
                    float level;
                    if (Base(CellAt(x, z)) == WorldCell.Building && kind == WorldCell.Building) level = 9f;
                    else if (kind == WorldCell.Water) level = -9f;
                    else
                    {
                        // A little more paving than half (a narrow paved strip drawn slanting has under half its cells'
                        // weight in its middle: the grass showed through, 2026-11-23); the cell's own kind as well
                        level = (PavedWeight(x, z) - 0.42f) * 2.5f;
                        if (level < 0.02f && (kind == WorldCell.Paved || kind == WorldCell.Building)) level = 0.02f;
                        if (pDist != null && pDist[k] < EdgeReach) level = pLvl[k];
                        if (kind == WorldCell.Plot) level = Math.Min(level, -0.3f);
                        if (asphaltLevel != null) level = Math.Max(level, asphaltLevel[k]);
                    }
                    // The road over a tunnel and its causeway: paved right across, up to its walls (2026-11-24, Anders)
                    if (roadLevel != null && roadLevel[k] >= 0f) level = Math.Max(level, roadLevel[k]);
                    paveLevel[k] = level;
                    if (level >= 0f && !pavedCorner[k])
                    {
                        c = (input.town == null ? Asphalt : TownPaved(ax, az)).Shade(0.95f + f2 * 0.08f);
                        hard = true;
                        pavedCorner[k] = true;
                    }
                    else if (level < 0f && pavedCorner[k])
                    {
                        cell = NotKindNear(x, z, WorldCell.Paved);
                        kind = Base(cell);
                        c = kind == WorldCell.Plot ? Lawn.Lerp(LawnLight, f) : kind == WorldCell.RoughOpen || kind == WorldCell.RoughOpenTrees ? Rough.Lerp(RoughGreen, f)
                            : kind == WorldCell.Open || kind == WorldCell.OpenTrees ? Open.Lerp(OpenGreen, f)
                            : floor.a.Lerp(floor.b, Sm(0.35f, 0.7f, f));
                        hard = false;
                        pavedCorner[k] = false;
                    }
                }
                if (!hard)
                {
                    if ((cell & WorldCell.Marsh) != 0) c = c.Lerp(MarshColor, 0.85f).Lerp(Wet, Sm(0.62f, 0.8f, f2) * 0.8f);
                    if (bog[k] > 0f) c = c.Lerp(MarshColor, bog[k] * 0.85f).Lerp(Wet, bog[k] * Sm(0.5f, 0.75f, f2) * 0.8f);
                    if ((cell & (WorldCell.Stony | WorldCell.Boulders)) != 0) c = c.Lerp(Rock, 0.25f + f2 * 0.3f);
                    if (soil[k] > 0f) c = c.Lerp(Soil, soil[k]);
                    if (trackBlend[k] > 0f) c = c.Lerp(trackColor[k], trackBlend[k]);
                    if (water[k] > 0f) c = c.Lerp(WaterDeep, water[k]);
                }
                if (gKind != null) gKind[k] = (byte)(pavedCorner != null && pavedCorner[k] ? WorldCell.Paved : Kind2(cell));
                // Steep ground and cliff faces show the rock
                float sx = F[k + (i < N - 1 ? 1 : 0)] - F[k - (i > 0 ? 1 : 0)], sz = F[k + (j < N - 1 ? N : 0)] - F[k - (j > 0 ? N : 0)];
                if (input.town != null)
                {
                    // In a town the ground is cut into steps along its walls, quays and the road over a tunnel
                    // (CutSteps): each side is level up to the step, so its slope is the gentler of the two ways
                    // (else the corners next to a step lay tilted and dark, a zigzag along it; 2026-11-24)
                    float fx0 = i > 0 ? F[k] - F[k - 1] : 0f, fx1 = i < N - 1 ? F[k + 1] - F[k] : 0f;
                    float fz0 = j > 0 ? F[k] - F[k - N] : 0f, fz1 = j < N - 1 ? F[k + N] - F[k] : 0f;
                    sx = 2f * (fx0 * fx1 <= 0f && i > 0 && i < N - 1 ? 0f : Math.Abs(fx0) < Math.Abs(fx1) && i > 0 ? fx0 : fx1);
                    sz = 2f * (fz0 * fz1 <= 0f && j > 0 && j < N - 1 ? 0f : Math.Abs(fz0) < Math.Abs(fz1) && j > 0 ? fz0 : fz1);
                }
                if (chunk)
                {
                    // At a piece's edge the slope is taken over the edge too, so the light has no seam
                    if (i == 0 || i == N - 1) sx = (i < N - 1 ? F[k + 1] : Plain(x + 1f, z)) - (i > 0 ? F[k - 1] : Plain(x - 1f, z));
                    if (j == 0 || j == N - 1) sz = (j < N - 1 ? F[k + N] : Plain(x, z + 1f)) - (j > 0 ? F[k - N] : Plain(x, z - 1f));
                }
                float slope = (float)Math.Sqrt(sx * sx + sz * sz) / 2f;
                if (kind != WorldCell.Water) c = c.Lerp(RockDark, Math.Max(rock[k] * 0.95f, Sm(0.75f, 1.3f, slope)));
                c = c.Shade(0.86f + f2 * 0.24f);

                float nx = -sx / 2f, nz = -sz / 2f, nl = (float)Math.Sqrt(nx * nx + 1f + nz * nz);
                // Under a bridge's deck the ground is drawn well below it (the runner still walks on F, the deck's
                // height): else the ground at the deck's height showed through the deck as a zigzag strip
                // (2026-11-23, Anders on the Tranås bridge)
                float gy = F[k];
                if (UnderBridgeDeck(x, z)) gy = float.IsNaN(lakeLevel[k]) ? gy - 1.6f : Math.Min(gy - 1.6f, lakeLevel[k] - 0.6f);
                groundMesh.Vertex(x, gy, z, nx / nl, 1f / nl, nz / nl, c);
            }
        }
        for (int j = 0; j < N - 1; j++)
        {
            for (int i = 0; i < N - 1; i++)
            {
                int a = j * N + i;
                groundMesh.triangles.Add(a); groundMesh.triangles.Add(a + N); groundMesh.triangles.Add(a + 1);
                groundMesh.triangles.Add(a + 1); groundMesh.triangles.Add(a + N); groundMesh.triangles.Add(a + N + 1);
            }
        }
    }

    // ---- Water: a still surface on lakes, ponds and streams; pools of open water in marshes and along marsh strips ----

    TvMesh waterMesh;

    void Waters()
    {
        TvColor deep = TvColor.Hex("#34495a"), sky = TvColor.Hex("#6c8594"), bogC = TvColor.Hex("#3a4848");
        // Which 1 m squares hold water, and what kind: 1 lake, 2 stream, 3 marsh pool
        var kind = new byte[N * N];
        for (int j = 0; j < N - 1; j++)
        {
            for (int i = 0; i < N - 1; i++)
            {
                int a = j * N + i;
                float x = i - R, z = j - R;
                byte cell = CellAt(x + 0.5f, z + 0.5f);
                // A lake: where a corner is inside the water's edge (a little beyond it, under the bank; the surface
                // is cut along the edge below)
                float inside = Math.Max(Math.Max(wLvl[a], wLvl[a + 1]), Math.Max(wLvl[a + N], wLvl[a + N + 1]));
                if (inside >= -WaterMargin) { kind[a] = 1; continue; }
                if (Base(cell) == WorldCell.Water) cell = (byte)(cell & ~WorldCell.BaseMask);
                float wet = Math.Max(Math.Max(water[a], water[a + 1]), Math.Max(water[a + N], water[a + N + 1]));
                if (wet > 0.55f) { kind[a] = 2; continue; }
                float strip = Math.Max(Math.Max(bog[a], bog[a + 1]), Math.Max(bog[a + N], bog[a + N + 1]));
                if (MarshPool(x, z, (cell & WorldCell.Marsh) != 0 && Base(cell) != WorldCell.Building, strip)) kind[a] = 3;
            }
        }
        // One level for every corner: the lowest ground round it, so the surface is smooth and lies below the banks
        var level = new float[N * N];
        // Under a bridge's deck the ground is drawn low (Ground): the deck's own height must not lift the water over
        // the deck (2026-11-23: blue patches on the Tranås bridge)
        var low = new float[N * N];
        for (int k = 0; k < low.Length; k++) low[k] = UnderBridgeDeck(k % N - R, k / N - R) ? F[k] - 1.6f : F[k];
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                float lo = float.MaxValue;
                for (int dj = -2; dj <= 2; dj++)
                    for (int di = -2; di <= 2; di++)
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= N || jj >= N) continue;
                        lo = Math.Min(lo, low[jj * N + ii]);
                    }
                // By a bridge (not under its deck, where the ground is drawn low): never up to the deck's ends,
                // which lie on the ground (2026-11-23, Anders: water showed through the deck near the land)
                int kk = j * N + i;
                if (BridgeWater(i - R, j - R) && !UnderBridgeDeck(i - R, j - R)) lo = Math.Min(lo, F[kk] - 0.7f);
                level[kk] = lo;
            }
        }
        var corner = new int[N * N];
        for (int i = 0; i < corner.Length; i++) corner[i] = -1;
        waterTop = new float[N * N];
        for (int i = 0; i < waterTop.Length; i++) waterTop[i] = float.NaN;
        for (int j = 0; j < N - 1; j++)
        {
            for (int i = 0; i < N - 1; i++)
            {
                int a = j * N + i;
                byte k = kind[a];
                if (k == 0) continue;
                float lift = k == 1 ? 0.3f : k == 2 ? 0.12f : 0.12f;
                bool flat = k == 1;
                int V(int ci, int cj)
                {
                    int q = cj * N + ci;
                    if (corner[q] >= 0) return corner[q];
                    float x = ci - R, z = cj - R;
                    // The sky shows in the water in broad streaks; marsh pools are dark and brown
                    float glint = TvRandom.Noise((x + ox) * 0.12f + 3f, (z + oz) * 0.05f);
                    TvColor c = k == 3 ? bogC.Shade(0.9f + 0.2f * glint) : deep.Lerp(sky, 0.15f + 0.45f * glint);
                    float top = flat && !float.IsNaN(lakeLevel[q]) ? lakeLevel[q] : level[q] + lift;
                    corner[q] = waterMesh.Vertex(x, top, z, 0f, 1f, 0f, c);
                    if (k == 1) waterTop[q] = top;
                    return corner[q];
                }
                int v0 = V(i, j), v1 = V(i + 1, j), v2 = V(i, j + 1), v3 = V(i + 1, j + 1);
                int q0 = a, q1 = a + 1, q2 = a + N, q3 = a + N + 1;
                if (k != 1 || (wLvl[q0] >= -WaterMargin && wLvl[q1] >= -WaterMargin && wLvl[q2] >= -WaterMargin && wLvl[q3] >= -WaterMargin))
                {
                    waterMesh.triangles.Add(v0); waterMesh.triangles.Add(v2); waterMesh.triangles.Add(v1);
                    waterMesh.triangles.Add(v1); waterMesh.triangles.Add(v2); waterMesh.triangles.Add(v3);
                    continue;
                }
                // The lake's edge square: only the part inside the edge (the map's bank line where near)
                WaterPart(v0, v2, v1, q0, q2, q1);
                WaterPart(v1, v2, v3, q1, q2, q3);
            }
        }
    }

    const float WaterMargin = 0.6f;     // the lake's surface reaches this far beyond its edge, under the bank

    // The part of a triangle of the water's surface inside the lake's edge (vertices a, b, c at corners qa, qb, qc)
    void WaterPart(int a, int b, int c, int qa, int qb, int qc)
    {
        int[] vs = { a, b, c };
        float[] ls = { wLvl[qa] + WaterMargin, wLvl[qb] + WaterMargin, wLvl[qc] + WaterMargin };
        var poly = new List<int>(4);
        for (int e = 0; e < 3; e++)
        {
            int u = vs[e], v = vs[(e + 1) % 3];
            float lu = ls[e], lv = ls[(e + 1) % 3];
            if (lu >= 0f) poly.Add(u);
            if ((lu >= 0f) != (lv >= 0f))
                poly.Add(CopyVertex(waterMesh, u, waterMesh, v, lu >= 0f ? u : v, Math.Max(0.02f, Math.Min(0.98f, lu / (lu - lv)))));
        }
        for (int m = 1; m + 1 < poly.Count; m++) { waterMesh.triangles.Add(poly[0]); waterMesh.triangles.Add(poly[m]); waterMesh.triangles.Add(poly[m + 1]); }
    }

    // Open water in a marsh or a narrow marsh strip: where the noise is high
    bool MarshPool(float x, float z, bool marsh, float strip)
    {
        x += ox;
        z += oz;
        float n = TvRandom.Patches(x * 0.13f + 11f, z * 0.13f - 4f) + TvRandom.Noise(x * 0.6f, z * 0.6f) * 0.1f;
        return (marsh && n > 0.6f) || (strip > 0.55f && TvRandom.Patches(x * 0.3f + 11f, z * 0.3f - 4f) > 0.38f);
    }

    // A fence post's foot in a town: on the high side where the fence stands on a step (a quay, the road over a
    // causeway), not down the slope of the ground's 1 m squares beside it (2026-11-24, Anders: the fence on the bridge
    // leaned down towards the lake)
    float PostH(float x, float z)
    {
        float h = H(x, z);
        if (input.town == null) return h;
        float best = h;
        for (int a = 0; a < 8; a++)
        {
            float ang = a * 0.785398f;
            best = Math.Max(best, H(x + (float)Math.Cos(ang) * 0.6f, z + (float)Math.Sin(ang) * 0.6f));
        }
        return best - h > 0.25f ? best : h;
    }

    float H(float x, float z)
    {
        // Beyond a piece's edge: the map's own heights (a wire or a wall reaching over to the next piece)
        if (chunk && (x < -R || x > R || z < -R || z > R)) return BaseHeight(x, z) - baseHeight;
        float fx = x + R, fz = z + R;
        fx = fx < 0f ? 0f : fx > N - 1.001f ? N - 1.001f : fx;
        fz = fz < 0f ? 0f : fz > N - 1.001f ? N - 1.001f : fz;
        int i = (int)fx, j = (int)fz;
        float u = fx - i, v = fz - j;
        float a = F[j * N + i], b = F[j * N + i + 1], c = F[(j + 1) * N + i], d = F[(j + 1) * N + i + 1];
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    // ---- The low-poly shapes everything is put together from ----

    TvShape[] rocks, rocksLow, bushShapes, youngSpruce, youngLeafy;
    TvShape rootPlate, logShape, bigTree, poolShape, spruceFar;
    TvShape[] pineNear, pineFar, pine2Near, pine2Far, spruceShape, birchNear, birchFar, beechNear, beechFar;
    TvShape tuftBerry, tuftHeath, tuftGrass, tuftDry, tuftShort, tuftFern, tuftBerryNear, tuftHeathNear;

    void MakeShapes()
    {
        TvColor bark = TvColor.Hex("#5b4a3c"), barkHigh = TvColor.Hex("#b0683a"), pineG = TvColor.Hex("#3f5a2e"), pineG2 = TvColor.Hex("#4f6b33"),
            spruceG = TvColor.Hex("#2c4526"), spruceG2 = TvColor.Hex("#3a5630"), birchW = TvColor.Hex("#d9d6c8"), birchG = TvColor.Hex("#86a045"),
            birchG2 = TvColor.Hex("#9fb04e"), beechBark = TvColor.Hex("#8a8a80"), beechG = TvColor.Hex("#5d8a3a"), beechG2 = TvColor.Hex("#6f9a42");

        TvShape Pine(bool near)
        {
            var b = new TvShapeBuilder(random);
            b.Tube(0.19f, 0.1f, 15.5f, 0f, near ? 6 : 4, bark, barkHigh, 4f, 10f);
            b.Blob(2.3f, 1f, 0.55f, 1f, 0f, 14.6f, 0f, 0.35f, 1, near, pineG);
            b.Blob(1.7f, 1f, 0.55f, 1f, 1.2f, 13.2f, 0.6f, 0.35f, near ? 1 : 0, near, pineG2);
            b.Blob(1.5f, 1f, 0.5f, 1f, -1.1f, 12.6f, -0.7f, 0.35f, near ? 1 : 0, near, pineG);
            b.Blob(1.2f, 1f, 0.5f, 1f, 0.2f, 16f, -0.3f, 0.3f, near ? 1 : 0, near, pineG2);
            return b.Done();
        }
        TvShape Pine2(bool near)
        {
            var b = new TvShapeBuilder(random);
            b.Tube(0.17f, 0.09f, 13f, 0f, near ? 6 : 4, bark, barkHigh, 3f, 8f);
            b.Blob(2f, 1f, 0.5f, 1f, 0.3f, 12.2f, 0f, 0.4f, 1, near, pineG2);
            b.Blob(1.6f, 1f, 0.5f, 1f, -1f, 10.8f, 0.8f, 0.35f, near ? 1 : 0, near, pineG);
            b.Blob(1.3f, 1f, 0.5f, 1f, 0.9f, 10.2f, -0.9f, 0.35f, near ? 1 : 0, near, pineG);
            return b.Done();
        }
        TvShape Spruce()
        {
            var b = new TvShapeBuilder(random);
            b.Tube(0.17f, 0.06f, 13f, 0f, 5, bark);
            b.Cone(2.5f, 4.2f, 2.2f, 7, spruceG);
            b.Cone(2.15f, 4f, 4.6f, 7, spruceG2);
            b.Cone(1.75f, 3.8f, 7f, 7, spruceG);
            b.Cone(1.3f, 3.4f, 9.3f, 7, spruceG2);
            b.Cone(0.8f, 3f, 11.6f, 7, spruceG);
            return b.Done();
        }
        TvShape Birch(bool near)
        {
            var b = new TvShapeBuilder(random);
            b.Tube(0.12f, 0.05f, 11f, 0f, near ? 5 : 4, birchW);
            b.Blob(1.9f, 1f, 0.9f, 1f, 0f, 9.6f, 0f, 0.4f, 1, near, birchG);
            b.Blob(1.4f, 1f, 0.8f, 1f, 1f, 8f, 0.5f, 0.4f, near ? 1 : 0, near, birchG2);
            b.Blob(1.3f, 1f, 0.8f, 1f, -0.9f, 7.4f, -0.6f, 0.4f, near ? 1 : 0, near, birchG);
            return b.Done();
        }
        // Beech and other broad-leaved trees: a smooth grey trunk and a wide, high crown
        TvShape Beech(bool near)
        {
            var b = new TvShapeBuilder(random);
            b.Tube(0.3f, 0.16f, 12f, 0f, near ? 7 : 4, beechBark);
            b.Blob(3.6f, 1f, 0.7f, 1f, 0f, 15f, 0f, 0.35f, 1, near, beechG);
            b.Blob(2.6f, 1f, 0.7f, 1f, 2.2f, 13f, 1f, 0.35f, near ? 1 : 0, near, beechG2);
            b.Blob(2.5f, 1f, 0.7f, 1f, -2f, 12.6f, -1.2f, 0.35f, near ? 1 : 0, near, beechG);
            b.Blob(2.2f, 1f, 0.65f, 1f, 0.4f, 17.4f, -0.6f, 0.3f, near ? 1 : 0, near, beechG2);
            return b.Done();
        }
        // Young trees for thickets, 2.5 m tall: spruces with branches down to the ground, and leafy saplings
        TvShape YoungSpruce()
        {
            var b = new TvShapeBuilder(random);
            TvColor c1 = spruceG.Shade(Rr(0.9f, 1.15f)), c2 = spruceG2.Shade(Rr(0.9f, 1.15f));
            b.Tube(0.05f, 0.02f, 2.5f, 0f, 4, bark);
            b.Cone(0.8f, 1.1f, 0.05f, 6, c1);
            b.Cone(0.62f, 1f, 0.72f, 6, c2);
            b.Cone(0.44f, 0.9f, 1.35f, 6, c1);
            b.Cone(0.25f, 0.65f, 1.9f, 5, c2);
            return b.Done();
        }
        TvShape YoungLeafy()
        {
            var b = new TvShapeBuilder(random);
            b.Tube(0.035f, 0.02f, 1.8f, 0f, 4, birchW);
            b.Blob(0.6f, 1f, 1.15f, 1f, 0f, 1.55f, 0f, 0.5f, 0, true, birchG.Shade(0.8f), birchG2, 0.9f, 2.1f);
            b.Blob(0.42f, 1f, 1f, 1f, 0.3f, 0.95f, 0.25f, 0.5f, 0, true, birchG.Shade(0.75f), birchG, 0.6f, 1.4f);
            return b.Done();
        }
        youngSpruce = new[] { YoungSpruce(), YoungSpruce(), YoungSpruce() };
        youngLeafy = new[] { YoungLeafy(), YoungLeafy() };

        // Two of each, so a forest is not one tree copied
        pineNear = new[] { Pine(true), Pine(true) };
        pineFar = new[] { Pine(false), Pine(false) };
        pine2Near = new[] { Pine2(true), Pine2(true) };
        pine2Far = new[] { Pine2(false), Pine2(false) };
        spruceShape = new[] { Spruce(), Spruce() };
        if (chunk)
        {
            // A spruce for far away: fewer and plainer layers
            var b = new TvShapeBuilder(random);
            b.Tube(0.17f, 0.06f, 13f, 0f, 3, bark);
            b.Cone(2.4f, 5.6f, 2.2f, 5, spruceG);
            b.Cone(1.8f, 5f, 6f, 5, spruceG2);
            b.Cone(1.1f, 4.4f, 9.6f, 5, spruceG);
            spruceFar = b.Done();
        }
        birchNear = new[] { Birch(true), Birch(true) };
        birchFar = new[] { Birch(false), Birch(false) };
        beechNear = new[] { Beech(true), Beech(true) };
        beechFar = new[] { Beech(false), Beech(false) };

        // Rocks: granite broken by flat planes, moss on top where the ground is mossy, pale lichen spots
        float mossy = look.ground == TerrainLook.Moss ? 0.5f : look.ground == TerrainLook.Stone || look.ground == TerrainLook.Lichen || look.ground == TerrainLook.Sand ? 0.18f : 0.4f;
        TvColor granite = TvColor.Hex("#9e9e99"), granite2 = TvColor.Hex("#94948f"), mossG = TvColor.Hex("#667a3a");
        TvShape RockShape(float sx, float sy, float sz, float oy, int detail, int cuts, TvColor c)
        {
            var b = new TvShapeBuilder(random);
            b.Rock(sx, sy, sz, oy, detail, cuts, 0.3f, c, mossG, mossy, 0.12f, -0.35f, true);
            return b.Done();
        }
        // Every stone its own (2026-11-25, Anders): rounded and angular, flat slabs and tall blocks, split ones; each
        // gets its own colour and moss where it lies (StoneLife)
        rocks = new[]
        {
            RockShape(1f, 0.75f, 1f, 0.3f, 1, 7, granite), RockShape(1.15f, 0.6f, 0.85f, 0.24f, 1, 8, granite2),
            RockShape(0.9f, 0.85f, 1f, 0.33f, 1, 6, granite2), RockShape(1.2f, 0.5f, 0.95f, 0.2f, 1, 8, granite),
            RockShape(1.35f, 0.42f, 1.05f, 0.16f, 1, 5, granite), RockShape(0.8f, 1.05f, 0.85f, 0.42f, 1, 10, granite2),
            RockShape(1.1f, 0.7f, 0.75f, 0.27f, 1, 12, granite), RockShape(1f, 0.62f, 1.1f, 0.25f, 1, 3, granite2)
        };
        rocksLow = new[] { RockShape(1f, 0.7f, 1f, 0.28f, 0, 3, granite), RockShape(1.1f, 0.55f, 0.9f, 0.22f, 0, 3, granite2), RockShape(0.95f, 0.8f, 1f, 0.3f, 0, 3, granite) };
        var s = new TvShapeBuilder(random);
        // The root plate of a fallen tree: earth and roots, standing on edge
        s.Rock(1f, 1f, 0.3f, 0.75f, 1, 4, 0.45f, TvColor.Hex("#5a4636"), TvColor.Hex("#4a3a2c"), 0.3f, 0f, -0.75f);
        rootPlate = s.Done();

        // Bilberry: a low tangle of small leafy clumps, lit lighter on top
        TvShape Berry(bool near, int clumps)
        {
            var b = new TvShapeBuilder(random);
            TvColor[] leaf = { TvColor.Hex("#3f5f2a"), TvColor.Hex("#4b6c2e"), TvColor.Hex("#456628") };
            for (int i = 0; i < clumps; i++)
            {
                float a = i * 2.4f + random.Value(), d = i == 0 ? 0f : 0.12f + 0.22f * random.Value(), r = 0.13f + 0.08f * random.Value();
                TvColor c = leaf[i % leaf.Length];
                b.Blob(r, 1f, 0.75f, 1f, (float)Math.Cos(a) * d, 0.1f + 0.08f * random.Value(), (float)Math.Sin(a) * d, 0.55f, 0, near, c.Shade(0.8f), c.Lerp(TvColor.Hex("#7d9a3e"), 0.45f), 0f, 0.3f);
            }
            return b.Done();
        }
        // Heather: olive-brown cushions with a purple tinge on top
        TvShape Heath(bool near, int clumps)
        {
            var b = new TvShapeBuilder(random);
            for (int i = 0; i < clumps; i++)
            {
                float a = i * 2.4f + random.Value(), d = i == 0 ? 0f : 0.1f + 0.18f * random.Value(), r = 0.12f + 0.07f * random.Value();
                b.Blob(r, 1f, 0.7f, 1f, (float)Math.Cos(a) * d, 0.08f + 0.05f * random.Value(), (float)Math.Sin(a) * d, 0.55f, 0, near,
                    TvColor.Hex("#4e4a2c"), i % 2 == 0 ? TvColor.Hex("#7a5868") : TvColor.Hex("#6d6a3c"), 0.02f, 0.24f);
            }
            return b.Done();
        }
        tuftBerry = Berry(false, 4);
        tuftHeath = Heath(false, 3);
        s = new TvShapeBuilder(random);
        // A tussock: a low mound with many thin blades
        s.Blob(0.2f, 1f, 0.45f, 1f, 0f, 0.03f, 0f, 0.3f, 0, false, TvColor.Hex("#7f8a48"));
        TvColor[] blades = { TvColor.Hex("#a7a35a"), TvColor.Hex("#8f9a4c"), TvColor.Hex("#b4a860"), TvColor.Hex("#99a052") };
        for (int i = 0; i < 9; i++)
        {
            float a = i * 2.4f, d = 0.04f + 0.02f * i;
            s.Cone(0.045f + 0.004f * (i % 3), 0.3f + 0.035f * ((i * 5) % 7), 0.02f, 3, blades[i % blades.Length], (float)Math.Cos(a) * d, (float)Math.Sin(a) * d);
        }
        tuftGrass = s.Done();
        // The same undergrowth with hard facets and more clumps, for the tufts near the control
        tuftBerryNear = Berry(true, 6);
        tuftHeathNear = Heath(true, 5);
        s = new TvShapeBuilder(random);
        s.Cone(0.12f, 0.36f, 0f, 4, TvColor.Hex("#b9ab72"));
        s.Cone(0.09f, 0.3f, 0f, 4, TvColor.Hex("#a39a62"), 0.13f, -0.06f);
        tuftDry = s.Done();
        s = new TvShapeBuilder(random);
        s.Blob(0.2f, 1f, 0.3f, 1f, 0f, 0.03f, 0f, 0.5f, 0, false, TvColor.Hex("#7d9148"));
        tuftShort = s.Done();
        s = new TvShapeBuilder(random);
        s.Cone(0.34f, 0.5f, 0f, 5, TvColor.Hex("#5f8a3a"));
        s.Cone(0.22f, 0.62f, 0f, 5, TvColor.Hex("#6d9a40"), 0.1f, 0.1f);
        tuftFern = s.Done();

        // A bush: several leafy lumps, dark and dense inside, lighter where the sun reaches, a few stems at the foot
        TvShape Bush()
        {
            var b = new TvShapeBuilder(random);
            TvColor low = TvColor.Hex("#2f4824"), high = TvColor.Hex("#5f7f37");
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.7f + random.Value(), d = 0.15f + 0.3f * random.Value();
                b.Tube(0.035f, 0.02f, 0.55f, -0.05f, 4, TvColor.Hex("#4a3e30"), null, 0f, 1f, (float)Math.Cos(a) * d, (float)Math.Sin(a) * d);
            }
            b.Blob(0.62f, 1f, 0.78f, 1f, 0f, 0.82f, 0f, 0.42f, 1, true, low, high, 0.35f, 1.35f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f + random.Value() * 0.6f, d = 0.38f + 0.2f * random.Value(), r = 0.32f + 0.14f * random.Value();
                b.Blob(r, 1f, 0.8f, 1f, (float)Math.Cos(a) * d, 0.45f + 0.45f * random.Value(), (float)Math.Sin(a) * d, 0.45f, 0, true, low, high, 0.3f, 1.3f);
            }
            return b.Done();
        }
        bushShapes = new[] { Bush(), Bush() };

        // A distinct big tree: an old oak
        s = new TvShapeBuilder(random);
        s.Tube(0.45f, 0.28f, 6f, 0f, 7, TvColor.Hex("#5a4c3e"));
        s.Blob(4.2f, 1f, 0.75f, 1f, 0f, 9f, 0f, 0.35f, 1, true, TvColor.Hex("#4f7a34"));
        s.Blob(3f, 1f, 0.7f, 1f, 2.6f, 7.4f, 1.2f, 0.35f, 1, true, TvColor.Hex("#5d8a3a"));
        s.Blob(2.9f, 1f, 0.7f, 1f, -2.4f, 7.2f, -1.4f, 0.35f, 1, true, TvColor.Hex("#4a7230"));
        bigTree = s.Done();

        s = new TvShapeBuilder(random);
        s.Tube(0.17f, 0.12f, 1f, 0f, 6, TvColor.Hex("#5e5346"));
        logShape = s.Done();

        s = new TvShapeBuilder(random);
        s.Blob(1f, 1f, 0.06f, 1f, 0f, 0.02f, 0f, 0.15f, 1, false, TvColor.Hex("#3f5a6c"));
        poolShape = s.Done();

        // The trees, bushes and undergrowth with bark, needles and leaves (TvWorldBuilderFlora.cs)
        MakeFlora();
    }

    // ---- Stones, boulder clusters, stony ground, cairns: exactly where the map has them ----

    void AddRock(float x, float z, float s, float sink, bool solid, float tint = -1f)
    {
        if (!Inside(x, z, 1.5f)) return;
        if (InTunnel(x, z, 0.6f + s * 0.5f) != null) return;
        // No loose stones on a town's streets or in its water (2026-11-24, Anders: stones on the street by a quay)
        if (!solid && input.town != null)
        {
            int b = Base(CellAt(x, z));
            if (b == WorldCell.Paved || b == WorldCell.Water || b == WorldCell.Building) return;
        }
        rockMesh.Add(RockFor(x, z, s), x, H(x, z) - sink * s, z, s, s * Rr(0.85f, 1.15f), s, Rr(0f, 6.283f), tint < 0f ? Rr(0.9f, 1.08f) : tint);
        world.rocks++;
        if (solid) world.solids.Add(new TvSolid { x = x, z = z, r = s * 0.95f });
    }

    // Small or far stones get the plain shape: nobody sees the extra facets
    TvShape RockFor(float x, float z, float size)
    {
        bool near = chunk ? size > 0.5f : size > 0.3f && x * x + z * z < 60f * 60f;
        return near ? rocks[(int)(random.Value() * rocks.Length) % rocks.Length] : rocksLow[(int)(random.Value() * rocksLow.Length) % rocksLow.Length];
    }

    void Stones()
    {
        foreach (var p in points)
        {
            float x = p.x, z = p.z;
            switch (p.kind)
            {
                case WorldPointKind.Boulder: AddRock(x, z, random.Value() < 0.25f ? Rr(1.6f, 2.2f) : Rr(1.05f, 1.6f), 0.1f, true); break;
                case WorldPointKind.LargeBoulder: AddRock(x, z, Rr(2.3f, 3.6f), 0.1f, true); break;
                case WorldPointKind.BoulderCluster:
                {
                    int count = 4 + (int)(random.Value() * 3f);
                    for (int i = 0; i < count; i++)
                    {
                        float a = Rr(0f, 6.283f), d = Rr(0.3f, 2.6f);
                        AddRock(x + (float)Math.Cos(a) * d, z + (float)Math.Sin(a) * d, Rr(0.45f, 1.05f), 0.08f, false);
                    }
                    world.solids.Add(new TvSolid { x = x, z = z, r = 2.2f });
                    break;
                }
                case WorldPointKind.BoulderField:
                {
                    int count = 3 + (int)(random.Value() * 3f);
                    for (int i = 0; i < count; i++)
                    {
                        float a = Rr(0f, 6.283f), d = Rr(0f, 2.8f);
                        AddRock(x + (float)Math.Cos(a) * d, z + (float)Math.Sin(a) * d, Rr(0.3f, 0.8f), 0.1f, false);
                    }
                    break;
                }
                case WorldPointKind.StonyGround:
                    for (int i = 0; i < 6; i++)
                    {
                        float a = Rr(0f, 6.283f), d = Rr(0f, 3.2f);
                        AddRock(x + (float)Math.Cos(a) * d, z + (float)Math.Sin(a) * d, Rr(0.14f, 0.42f), 0.1f, false);
                    }
                    break;
                case WorldPointKind.Cairn:
                    for (int i = 0; i < 7; i++)
                    {
                        float a = Rr(0f, 6.283f), d = Rr(0f, 0.7f) * (1f - i * 0.1f);
                        float rx = x + (float)Math.Cos(a) * d, rz = z + (float)Math.Sin(a) * d, s = Rr(0.25f, 0.4f);
                        if (Inside(rx, rz, 1.5f)) rockMesh.Add(RockFor(rx, rz, s), rx, H(rx, rz) + i * 0.07f - 0.05f, rz, s, s, s, Rr(0f, 6.283f), Rr(0.9f, 1.05f));
                    }
                    world.solids.Add(new TvSolid { x = x, z = z, r = 1.0f });
                    break;
                case WorldPointKind.RockyPit:
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 1.05f + Rr(0f, 0.5f);
                        AddRock(x + (float)Math.Cos(a) * 1.9f, z + (float)Math.Sin(a) * 1.9f, Rr(0.3f, 0.6f), 0.1f, false);
                    }
                    break;
            }
        }

        // Boulder fields and stony ground drawn as areas
        float edge = chunk ? 0f : 2f;   // a run-yourself piece reaches its own edge
        for (float gz = -R + edge; gz < R - edge; gz += 2f)
        {
            for (float gx = -R + edge; gx < R - edge; gx += 2f)
            {
                byte cell = CellAt(gx + 1f, gz + 1f);
                if ((cell & WorldCell.Boulders) != 0)
                {
                    if (random.Value() < 0.55f) AddRock(gx + Rr(0f, 2f), gz + Rr(0f, 2f), Rr(0.35f, 1.1f), 0.1f, false);
                }
                else if ((cell & WorldCell.Stony) != 0)
                {
                    int count = (int)(random.Value() * 3f);
                    for (int i = 0; i < count; i++) AddRock(gx + Rr(0f, 2f), gz + Rr(0f, 2f), Rr(0.12f, 0.4f), 0.1f, false);
                }
            }
        }

        // Loose rock at the foot of every cliff
        foreach (var cliff in cliffs)
        {
            float[] c = cliff.xz;
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                float len = (float)Math.Sqrt((c[q + 2] - c[q]) * (c[q + 2] - c[q]) + (c[q + 3] - c[q + 1]) * (c[q + 3] - c[q + 1]));
                if (len < 1e-4f) continue;
                // Out on the low side of this stretch of the line
                float lx = cliff.side * (c[q + 3] - c[q + 1]) / len, lz = -cliff.side * (c[q + 2] - c[q]) / len;
                for (float t = 0f; t < len; t += 1.3f)
                {
                    float f = t / len, d = Rr(1.4f, 3.4f);
                    AddRock(Lerp(c[q], c[q + 2], f) + lx * d, Lerp(c[q + 1], c[q + 3], f) + lz * d, Rr(0.2f, 0.6f), 0.08f, false, Rr(0.8f, 1f));
                }
            }
        }
        CliffFaces();
    }

    // A rock cliff stands straight up (2026-11-19, Anders: a cliff is a wall, not a slope): a rough vertical face along
    // the line with a rock top on it, one solid block of rock. The ground has a corner only every metre, so its own
    // drop is a slope a metre wide: that slope lies just above the line (Cliff), the face stands on the line in front
    // of it and the top covers it back to the ground above (2026-11-24, Anders: a thin sheet stuck up above the
    // ground behind it). The face is one unbroken strip along the whole line, bent at the corners, and is drawn from
    // both sides, so there is no gap to see through at a corner or from inside (2026-11-24, Anders: gaps in the
    // cliffs, and the face stood out on the low side, so a runner put their head into the rock before being stopped:
    // the runner is stopped 0.45 m from the line, RunTerrain.Blocked).
    void CliffFaces()
    {
        const float Out = 0.12f, Back = 1.45f, Gap = 0.7f;
        const int Rows = 4;   // the face in bands, each bulging a little its own way, so it is not one flat slab
        foreach (var cliff in cliffs)
        {
            float[] c = cliff.xz;
            stepEnds.TryGetValue(c, out var more);
            int corners = c.Length / 2;
            var along = new float[corners];
            for (int p = 1; p < corners; p++)
            {
                float sx = c[p * 2] - c[p * 2 - 2], sz = c[p * 2 + 1] - c[p * 2 - 1];
                along[p] = along[p - 1] + (float)Math.Sqrt(sx * sx + sz * sz);
            }
            float length = along[corners - 1];
            if (length < 0.05f) continue;
            // Columns at the same places along the line in every piece of the world, so pieces meet without a gap
            int count = Math.Max(1, (int)Math.Ceiling(length / Gap));
            float gap = length / count;
            int lastColumn = -1, lastFace = -1, lastTop = -1;
            for (int i = 0; i < count; i++)
            {
                PointOn(c, along, (i + 0.5f) * gap, out float mx0, out float mz0);
                if (!Inside(mx0, mz0, chunk ? 0f : 2f)) continue;
                int faceA, topA;
                if (lastColumn == i) { faceA = lastFace; topA = lastTop; }
                else Column(c, along, length, i * gap, cliff.side, more, out faceA, out topA);
                Column(c, along, length, (i + 1) * gap, cliff.side, more, out int faceB, out int topB);
                // Both windings: seen from outside or from inside the rock, it is closed
                for (int r = 0; r < Rows; r++) Quad(cliffMesh, faceA + r, faceA + r + 1, faceB + r, faceB + r + 1);
                for (int r = 0; r < 2; r++) Quad(cliffMesh, topA + r, topA + r + 1, topB + r, topB + r + 1);
                lastColumn = i + 1;
                lastFace = faceB;
                lastTop = topB;
            }
            // The rock top as solid ground for the runner (2026-11-26, Anders on Fossum: from above you could walk
            // onto the top of a cliff and sink through it into the ground's step behind the face). A little past the
            // piece too, so the top is solid right up to the piece's edge.
            for (int i = 0; i < count; i++)
            {
                PointOn(c, along, (i + 0.5f) * gap, out float mx0, out float mz0);
                if (mx0 < -R - 2f || mx0 > R + 2f || mz0 < -R - 2f || mz0 > R + 2f) continue;
                CapColumn(c, along, length, i * gap, cliff.side, more, out float[] a);
                CapColumn(c, along, length, (i + 1) * gap, cliff.side, more, out float[] b);
                CapTriangle(a, 0, b, 0, a, 1);
                CapTriangle(b, 0, b, 1, a, 1);
                CapTriangle(a, 1, b, 1, a, 2);
                CapTriangle(b, 1, b, 2, a, 2);
            }
        }

        void CapTriangle(float[] p, int i, float[] q, int j, float[] r, int k)
        {
            List<float> caps = world.caps;
            caps.Add(p[i * 3]); caps.Add(p[i * 3 + 1]); caps.Add(p[i * 3 + 2]);
            caps.Add(q[j * 3]); caps.Add(q[j * 3 + 1]); caps.Add(q[j * 3 + 2]);
            caps.Add(r[k * 3]); caps.Add(r[k * 3 + 1]); caps.Add(r[k * 3 + 2]);
        }

        // The front, middle and back of the rock top at the distance s along the line (x, y, z each), as Column
        // builds it
        void CapColumn(float[] c, float[] along, float length, float s, float side, (float before, float after) more, out float[] points)
        {
            ColumnBase(c, along, length, s, side, more, out float px, out float pz, out float nx, out float nz, out float fx, out float fz, out float top);
            float mx = px - nx * 0.75f, mz = pz - nz * 0.75f, kx = px - nx * Back, kz = pz - nz * Back;
            points = new[] { fx, top, fz, mx, Math.Max(top, H(mx, mz) + 0.06f), mz, kx, H(kx, kz) - 0.04f, kz };
        }

        void ColumnBase(float[] c, float[] along, float length, float s, float side, (float before, float after) more,
                        out float px, out float pz, out float nx, out float nz, out float fx, out float fz, out float top)
        {
            PointOn(c, along, s, out px, out pz);
            PointOn(c, along, Math.Max(0f, s - 0.75f), out float bx, out float bz);
            PointOn(c, along, Math.Min(length, s + 0.75f), out float fx0, out float fz0);
            float sx = fx0 - bx, sz = fz0 - bz, l = (float)Math.Sqrt(sx * sx + sz * sz);
            if (l < 1e-5f) { sx = c[2] - c[0]; sz = c[3] - c[1]; l = Math.Max(1e-5f, (float)Math.Sqrt(sx * sx + sz * sz)); }
            // The low side, as the drop was made
            nx = side * sz / l; nz = -side * sx / l;
            float jag = (TvRandom.Noise((px + ox) * 1.7f, (pz + oz) * 1.7f) - 0.5f) * 0.16f;
            fx = px + nx * (Out + jag); fz = pz + nz * (Out + jag);
            // The top: level with the ground above the step (a little over it, so the ground does not show through)
            top = Math.Max(H(px - nx * 1.1f, pz - nz * 1.1f), H(px - nx * 1.45f, pz - nz * 1.45f)) + 0.1f;
            // At the cliff's own ends the face sinks into the ground where it stands
            float toEnd = Math.Min(more.before > 0f ? 9f : s, more.after > 0f ? 9f : length - s);
            top = Lerp(H(fx, fz) - 0.05f, top, Sm(0f, 1.2f, toEnd));
        }

        // One column of the face (Rows + 1 corners from the top down) and of the top (3 corners from the front back),
        // the distance s along the line
        void Column(float[] c, float[] along, float length, float s, float side, (float before, float after) more, out int face, out int topFirst)
        {
            ColumnBase(c, along, length, s, side, more, out float px, out float pz, out float nx, out float nz, out float fx, out float fz, out float top);
            // The foot: in the ground below
            float foot = H(px + nx * 0.8f, pz + nz * 0.8f) - 0.4f;
            // A low cliff: the face reaches further into the ground below, never up above the top
            if (top < foot + 0.5f) foot = top - 0.5f;
            float lean = (TvRandom.Noise((px + ox) * 0.9f + 7f, (pz + oz) * 0.9f) - 0.5f) * 0.3f;
            face = -1;
            for (int r = 0; r <= Rows; r++)
            {
                float h = r / (float)Rows;                       // 0 top .. 1 foot
                float y = top + (foot - top) * h;
                float bulge = r == 0 || r == Rows ? 0f : (TvRandom.Noise((px + ox) * 1.1f + r * 3.1f, (pz + oz) * 1.1f + y * 0.9f) - 0.5f) * 0.16f;
                float out0 = 0.06f * h + bulge;
                // Grey granite in patches, lichen-pale near the top, darker and damp at the foot
                float patch = TvRandom.Noise((px + ox) * 0.45f + y * 0.6f, (pz + oz) * 0.45f - y * 0.4f);
                TvColor col = Rock.Lerp(RockDark, Math.Max(0f, Math.Min(1f, patch * 1.3f - 0.15f)));
                if (h < 0.3f) col = col.Lerp(LichenRock, (0.3f - h) * 1.6f * TvRandom.Noise((px + ox) * 0.8f, (pz + oz) * 0.8f + 11f));
                if (h > 0.75f) col = col.Lerp(RockDark, (h - 0.75f) * 2.4f);
                int v = cliffMesh.Vertex(fx + nx * out0, y, fz + nz * out0, nx + lean * nz + bulge * 0.8f * nx, 0.15f - bulge * 0.3f, nz - lean * nx + bulge * 0.8f * nz, RockTint(col));
                // The cliff rock picture (2026-11-25): layers, wet streaks, moss in the cracks, laid along the line
                CliffUv(v, s / 3.6f, y / 3.6f, TvAtlas.CliffRock);
                if (r == 0) face = v;
            }
            // The rock top: from the face's top edge back over the ground's step to the ground above
            float mx = px - nx * 0.75f, mz = pz - nz * 0.75f, kx = px - nx * Back, kz = pz - nz * Back;
            float lichen = TvRandom.Noise((px + ox) * 0.6f + 3f, (pz + oz) * 0.6f);
            TvColor capCol = Rock.Lerp(LichenRock, 0.35f + 0.5f * lichen);
            topFirst = cliffMesh.Vertex(fx, top, fz, 0f, 1f, 0f, RockTint(capCol));
            CliffUv(topFirst, s / 2f, 0f, TvAtlas.Granite);
            CliffUv(cliffMesh.Vertex(mx, Math.Max(top, H(mx, mz) + 0.06f), mz, 0f, 1f, 0f, RockTint(capCol)), s / 2f, 0.75f / 2f, TvAtlas.Granite);
            CliffUv(cliffMesh.Vertex(kx, H(kx, kz) - 0.04f, kz, 0f, 1f, 0f, RockTint(capCol.Lerp(Rock, 0.3f))), s / 2f, Back / 2f, TvAtlas.Granite);
        }
    }

    // The cliff's colours are tints over the rock picture: the plain grey made lighter, the patches kept
    static TvColor RockTint(TvColor c) => c.Shade(1.45f);

    void CliffUv(int vertex, float u, float v, int tile)
    {
        cliffMesh.PadUvs(vertex);
        cliffMesh.uvs.Add(u); cliffMesh.uvs.Add(v); cliffMesh.uvs.Add(tile);
    }

    // A four-cornered piece (a, b one edge; c, d the next, b and d the far corners) drawn from both sides
    static void Quad(TvMesh mesh, int a, int b, int c, int d)
    {
        var t = mesh.triangles;
        t.Add(a); t.Add(b); t.Add(c); t.Add(c); t.Add(b); t.Add(d);
        t.Add(a); t.Add(c); t.Add(b); t.Add(c); t.Add(d); t.Add(b);
    }

    // ---- Walls, fences, power lines, towers, buildings, fallen trees, springs ----

    // A straight bar from a to b with a round cross-section (a wire, a rail, a fallen trunk, a pole)
    static void Bar(TvMesh mesh, float ax, float ay, float az, float bx, float by, float bz, float r0, float r1, int sides, TvColor color)
    {
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-5f) return;
        dx /= len; dy /= len; dz /= len;
        // Two directions across the bar
        float ux, uy, uz;
        if (Math.Abs(dy) < 0.9f) { ux = dz; uy = 0f; uz = -dx; }
        else { ux = 1f; uy = 0f; uz = 0f; }
        float ul = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz);
        ux /= ul; uy /= ul; uz /= ul;
        float vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
        int first = mesh.VertexCount;
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * 6.2831853f, ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            float nx = ux * ca + vx * sa, ny = uy * ca + vy * sa, nz = uz * ca + vz * sa;
            mesh.Vertex(ax + nx * r0, ay + ny * r0, az + nz * r0, nx, ny, nz, color);
            mesh.Vertex(bx + nx * r1, by + ny * r1, bz + nz * r1, nx, ny, nz, color);
        }
        // Which way round the ring runs decides which side faces out
        float wx = uy * vz - uz * vy, wy = uz * vx - ux * vz, wz = ux * vy - uy * vx;
        bool flip = wx * dx + wy * dy + wz * dz > 0f;
        for (int i = 0; i < sides; i++)
        {
            int a = first + i * 2, b = a + 1, q = a + 2, d = a + 3;
            if (flip)
            {
                mesh.triangles.Add(a); mesh.triangles.Add(q); mesh.triangles.Add(b);
                mesh.triangles.Add(q); mesh.triangles.Add(d); mesh.triangles.Add(b);
            }
            else
            {
                mesh.triangles.Add(a); mesh.triangles.Add(b); mesh.triangles.Add(q);
                mesh.triangles.Add(q); mesh.triangles.Add(b); mesh.triangles.Add(d);
            }
        }
    }

    TvShape Box(float w, float h, float d, TvColor color, float y = 0f)
    {
        var b = new TvShapeBuilder(random);
        b.Box(-w / 2f, y, -d / 2f, w / 2f, y + h, d / 2f, color);
        return b.Done();
    }

    // Walks along a line in steps and calls each(x, z, direction, step length)
    void Along(float[] c, float step, Action<float, float, float, float> each)
    {
        for (int q = 0; q + 3 < c.Length; q += 2)
        {
            float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
            float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            if (len < 0.05f) continue;
            int pieces = Math.Max(1, (int)Math.Round(len / step));
            float yaw = (float)Math.Atan2(bz - az, bx - ax);
            for (int i = 0; i < pieces; i++)
            {
                float f = (i + 0.5f) / pieces;
                float x = Lerp(ax, bx, f), z = Lerp(az, bz, f);
                if (Inside(x, z, 1.5f)) each(x, z, yaw, len / pieces);
            }
        }
    }

    // Like Along, but a post at both ends of the line and at every bend (2026-11-20, Anders: a fence stopped short of
    // the house wall; the posts stood half a step in from the ends)
    // own: the post is this piece's to build; the posts just over the edge come too (own false), so the rails reach
    // across to the next piece's first post
    void AlongPosts(float[] c, float step, Action<float, float, bool> each)
    {
        for (int q = 0; q + 3 < c.Length; q += 2)
        {
            float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
            float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            if (len < 0.05f) continue;
            int pieces = Math.Max(1, (int)Math.Round(len / step));
            for (int i = q == 0 ? 0 : 1; i <= pieces; i++)
            {
                float f = i / (float)pieces;
                float x = Lerp(ax, bx, f), z = Lerp(az, bz, f);
                bool own = Inside(x, z, 1.5f);
                if (own || (chunk && x >= -R - 4f && x < R + 4f && z >= -R - 4f && z < R + 4f)) each(x, z, own);
            }
        }
    }

    // A fence that ends by a house reaches right to its wall: an end within 1.5 m of a house is carried on into it
    float[] ToHouse(float[] c)
    {
        if (input.town == null || c.Length < 4) return c;
        float[] r = null;
        for (int end = 0; end < 2; end++)
        {
            int a = end == 0 ? 0 : c.Length - 2, b = end == 0 ? 2 : c.Length - 4;
            float dx = c[a] - c[b], dz = c[a + 1] - c[b + 1], len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.05f) continue;
            dx /= len; dz /= len;
            for (float s = 0f; s <= 1.5f; s += 0.25f)
            {
                if (Base(CellAt(c[a] + dx * s, c[a + 1] + dz * s)) != WorldCell.Building) continue;
                // Well into the house (a stepped corner of the map is cut straight, so the wall may stand back from
                // the cells), but never out of the other side of it
                float into = s + 1.6f;
                while (into > s + 0.3f && Base(CellAt(c[a] + dx * into, c[a + 1] + dz * into)) != WorldCell.Building) into -= 0.25f;
                if (r == null) r = (float[])c.Clone();
                r[a] = c[a] + dx * into;
                r[a + 1] = c[a + 1] + dz * into;
                break;
            }
        }
        return r ?? c;
    }

    void Structures()
    {
        TvColor stone = TvColor.Hex("#7e7b74"), wood = TvColor.Hex("#6a5a48"), pole = TvColor.Hex("#4e4033"), wire = TvColor.Hex("#2b2b2b"),
            steel = TvColor.Hex("#8c9094"), house = TvColor.Hex("#b4472f"), roof = TvColor.Hex("#3f3b38");
        TvColor grey = TvColor.Hex("#857d70"), wireC = TvColor.Hex("#9a9a94");
        TvShape wallPiece = Box(1f, 1f, 0.55f, stone), wallCap = Box(1f, 0.14f, 0.68f, TvColor.Hex("#6c6a64"));

        foreach (var line in lines)
        {
            float[] c = line.xz;
            switch (line.kind)
            {
                case WorldLineKind.Hedge:
                {
                    // An impassable hedge (ISSprOM 410.1): a dense, clipped green wall about 1.8 m high
                    TvShape hedge = Box(1f, 1f, 0.9f, TvColor.Hex("#2f5a2a"));
                    Along(c, 1f, (x, z, yaw, len) =>
                    {
                        thingMesh.Add(hedge, x, H(x, z) - 0.1f, z, len + 0.1f, Rr(1.7f, 1.95f), Rr(0.95f, 1.1f), -yaw, Rr(0.9f, 1.05f));
                        world.solids.Add(new TvSolid { x = x, z = z, r = 0.6f });
                    });
                    break;
                }
                case WorldLineKind.Stairs:
                {
                    // Stairs outside a town (a town draws its own from the map's lines): a stone step every 0.35 m, each
                    // level at the ground's height there, so they climb with the hill
                    if (input.town != null) break;
                    TvShape step = Box(1f, 1f, 1f, TvColor.Hex("#9a968e")), cheek = Box(1f, 1f, 0.25f, TvColor.Hex("#7e7b74"));
                    Along(c, 0.35f, (x, z, yaw, len) =>
                    {
                        float y = H(x, z) + 0.06f;
                        thingMesh.Add(step, x, y - 0.5f, z, len + 0.02f, 0.5f, 1.6f, -yaw);
                        float sx = (float)Math.Sin(yaw), cz = (float)Math.Cos(yaw);
                        foreach (float s in new[] { -0.92f, 0.92f })
                            thingMesh.Add(cheek, x - sx * s, y - 0.5f, z + cz * s, len + 0.02f, 0.75f, 1f, -yaw);
                    });
                    break;
                }
                case WorldLineKind.HighWall:
                    Along(c, 1.2f, (x, z, yaw, len) =>
                    {
                        // The mesh copy turns its x axis to (cos, -sin) of the angle given, so the line's own angle goes in
                        // negated (2026-11-20: walls on slanting lines stood across the line)
                        // A wall with the ground higher on one side (a town's drop, WallSteps): from the low ground's
                        // foot up to a little over the high ground (2026-11-23, Anders)
                        float wnx = (float)Math.Sin(yaw), wnz = -(float)Math.Cos(yaw);
                        // Each side's ground a little out from the wall too: the ground is cut along the wall (CutSteps),
                        // each side level to it, but the 1 m squares' heights mix the two sides near it
                        float g0 = Math.Max(H(x + wnx * 0.8f, z + wnz * 0.8f), H(x + wnx * 1.5f, z + wnz * 1.5f)),
                              g1 = Math.Max(H(x - wnx * 0.8f, z - wnz * 0.8f), H(x - wnx * 1.5f, z - wnz * 1.5f));
                        float lo = Math.Min(Math.Min(g0, g1), H(x, z)), hi = Math.Max(g0, g1);
                        float top = hi - lo >= 0.5f ? hi + 0.35f : H(x, z) + 1.8f;
                        float tall = Math.Max(0.5f, top - (lo - 0.1f));
                        thingMesh.Add(wallPiece, x, lo - 0.1f, z, len + 0.05f, tall, 1f, -yaw, Rr(0.94f, 1.04f));
                        thingMesh.Add(wallCap, x, top - 0.02f, z, len + 0.05f, 1f, 1f, -yaw);
                        world.solids.Add(new TvSolid { x = x, z = z, r = 0.55f });
                    });
                    break;
                case WorldLineKind.Wall:
                case WorldLineKind.RuinedWall:
                {
                    // A Swedish stone wall: field stones piled in two or three rows, mossy on top. A ruined one is one low row with gaps.
                    bool ruined = line.kind == WorldLineKind.RuinedWall;
                    Along(c, 0.48f, (x, z, yaw, len) =>
                    {
                        if (ruined && random.Value() < 0.35f) return;
                        float dx = (float)Math.Cos(yaw), dz = (float)Math.Sin(yaw), side = ruined ? 0.35f : 0.12f;
                        float bx = x + (-dz) * Rr(-side, side), bz = z + dx * Rr(-side, side), s0 = ruined ? Rr(0.22f, 0.36f) : Rr(0.34f, 0.44f);
                        float y0 = H(bx, bz);
                        rockMesh.Add(RockFor(bx, bz, s0), bx, y0 - 0.12f, bz, s0, s0 * Rr(0.8f, 1.1f), s0, Rr(0f, 6.283f), Rr(0.88f, 1.08f));
                        world.rocks++;
                        if (!ruined)
                        {
                            float s1 = Rr(0.26f, 0.35f), off = Rr(-0.2f, 0.2f);
                            float tx = x + dx * off, tz = z + dz * off;
                            rockMesh.Add(RockFor(tx, tz, s1), tx, y0 + s0 * 0.55f, tz, s1, s1 * Rr(0.75f, 1f), s1, Rr(0f, 6.283f), Rr(0.88f, 1.1f));
                            if (random.Value() < 0.45f)
                            {
                                float s2 = Rr(0.16f, 0.24f);
                                rockMesh.Add(RockFor(x, z, s2), x + dx * Rr(-0.15f, 0.15f), y0 + s0 * 0.55f + s1 * 0.6f, z + dz * Rr(-0.15f, 0.15f), s2, s2, s2, Rr(0f, 6.283f), Rr(0.9f, 1.1f));
                            }
                            world.rocks += 2;
                        }
                    });
                    // Runners jump a stone wall rather than going round it
                    for (int q = 0; q + 3 < c.Length; q += 2) world.AddHurdle(c[q], c[q + 1], c[q + 2], c[q + 3], ruined ? 0.4f : 0.8f);
                    break;
                }
                case WorldLineKind.Fence:
                case WorldLineKind.RuinedFence:
                {
                    // Weathered grey posts with two split rails; a ruined fence leans, misses posts and has a rail left here and there
                    bool ruined = line.kind == WorldLineKind.RuinedFence;
                    float lastX = float.NaN, lastY = 0f, lastZ = 0f;
                    AlongPosts(ToHouse(c), 2.4f, (x, z, own) =>
                    {
                        float y = PostH(x, z);
                        // A post over the piece's edge is the next piece's: only where the rail to the first post here starts
                        if (!own) { lastX = ruined ? float.NaN : x; lastY = y; lastZ = z; return; }
                        if (ruined && random.Value() < 0.3f) { lastX = float.NaN; return; }
                        float lean = ruined ? Rr(0.05f, 0.4f) : Rr(0f, 0.04f), la = Rr(0f, 6.283f);
                        float topX = x + (float)Math.Cos(la) * lean, topZ = z + (float)Math.Sin(la) * lean;
                        Bar(thingMesh, x, y - 0.15f, z, topX, y + 1.15f, topZ, 0.06f, 0.05f, 5, grey.Shade(Rr(0.85f, 1.1f)));
                        if (!float.IsNaN(lastX) && (!ruined || random.Value() < 0.3f) && (x - lastX) * (x - lastX) + (z - lastZ) * (z - lastZ) < 16f)
                        {
                            float sag = ruined ? Rr(0.2f, 0.6f) : 0f;
                            foreach (float up in ruined ? new[] { 0.5f } : new[] { 0.48f, 0.98f })
                                Bar(thingMesh, lastX, lastY + up, lastZ, x, y + up - sag, z, 0.04f, 0.035f, 4, grey.Shade(Rr(0.9f, 1.1f)));
                        }
                        lastX = x; lastY = y; lastZ = z;
                    });
                    for (int q = 0; q + 3 < c.Length; q += 2) world.AddHurdle(c[q], c[q + 1], c[q + 2], c[q + 3], ruined ? 0.6f : 1f);
                    break;
                }
                case WorldLineKind.HighFence:
                {
                    // A game fence: tall wooden posts with wire mesh; the mesh is only drawn where it can be seen
                    float lastX = float.NaN, lastY = 0f, lastZ = 0f;
                    AlongPosts(ToHouse(c), 3f, (x, z, own) =>
                    {
                        float y = PostH(x, z);
                        if (!own) { lastX = x; lastY = y; lastZ = z; return; }
                        Bar(thingMesh, x, y - 0.2f, z, x, y + 2.1f, z, 0.07f, 0.06f, 5, grey.Shade(Rr(0.85f, 1.05f)));
                        if (!float.IsNaN(lastX) && (x - lastX) * (x - lastX) + (z - lastZ) * (z - lastZ) < 25f)
                        {
                            bool near = chunk || x * x + z * z < 55f * 55f;
                            foreach (float up in near ? new[] { 0.1f, 0.35f, 0.65f, 1f, 1.4f, 1.95f } : new[] { 0.1f, 1f, 1.95f })
                                Bar(wireMesh, lastX, lastY + up, lastZ, x, y + up, z, 0.008f, 0.008f, 3, wireC);
                            if (near)
                                for (float f = 0.15f; f < 1f; f += 0.15f)
                                {
                                    float wx = Lerp(lastX, x, f), wz = Lerp(lastZ, z, f), wy = Lerp(lastY, y, f);
                                    Bar(wireMesh, wx, wy + 0.1f, wz, wx, wy + 1.95f, wz, 0.007f, 0.007f, 3, wireC);
                                }
                        }
                        lastX = x; lastY = y; lastZ = z;
                    });
                    break;
                }
                case WorldLineKind.Log:
                    for (int q = 0; q + 3 < c.Length; q += 2)
                    {
                        float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
                        if (chunk ? !Inside((ax + bx) / 2f, (az + bz) / 2f, 0f) : !Inside(ax, az, 2f) || !Inside(bx, bz, 2f)) continue;
                        Log(ax, H(ax, az) + 0.2f, az, bx, H(bx, bz) + 0.2f, bz, 0.19f, 0.13f);
                        // Runners jump over a fallen trunk, so it is not a solid to go round
                        world.AddHurdle(ax, az, bx, bz, 0.4f);
                    }
                    break;
                case WorldLineKind.PowerLine:
                case WorldLineKind.MajorPowerLine:
                {
                    bool major = line.kind == WorldLineKind.MajorPowerLine;
                    float top = major ? 19f : 11f, spread = major ? 4.5f : 2.8f;
                    int count = c.Length / 2;
                    float Top(int i) => H(Math.Max(-R, Math.Min(R, c[i * 2])), Math.Max(-R, Math.Min(R, c[i * 2 + 1]))) + top;
                    for (int i = 0; i < count; i++)
                    {
                        float x = c[i * 2], z = c[i * 2 + 1];
                        if (!Inside(x, z, 4f)) continue;
                        int other = i + 1 < count ? i + 1 : i - 1;
                        float dx = c[other * 2] - x, dz = c[other * 2 + 1] - z, l = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (l < 0.01f) continue;
                        float px = -dz / l, pz = dx / l, y = H(x, z);
                        foreach (float side in new[] { -1f, 1f })
                        {
                            float sx = x + px * spread * 0.57f * side, sz = z + pz * spread * 0.57f * side;
                            Bar(thingMesh, sx, y - 0.3f, sz, sx, y + top + 0.5f, sz, major ? 0.24f : 0.17f, major ? 0.17f : 0.13f, 6, major ? steel : pole);
                            world.solids.Add(new TvSolid { x = sx, z = sz, r = 0.35f });
                        }
                        Bar(thingMesh, x - px * spread * 1.15f, y + top - 0.3f, z - pz * spread * 1.15f, x + px * spread * 1.15f, y + top - 0.3f, z + pz * spread * 1.15f,
                            0.09f, 0.09f, 4, major ? steel : pole);
                    }
                    for (int i = 0; i + 1 < count; i++)
                    {
                        float ax = c[i * 2], az = c[i * 2 + 1], bx = c[i * 2 + 2], bz = c[i * 2 + 3];
                        float dx = bx - ax, dz = bz - az, l = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (l < 0.5f) continue;
                        float px = -dz / l, pz = dx / l, ya = Top(i), yb = Top(i + 1);
                        foreach (float s in new[] { -spread, 0f, spread })
                        {
                            const int Parts = 12;
                            float lx = 0f, ly = 0f, lz = 0f;
                            for (int t = 0; t <= Parts; t++)
                            {
                                float f = t / (float)Parts;
                                float x = Lerp(ax, bx, f) + px * s, z = Lerp(az, bz, f) + pz * s, y = Lerp(ya, yb, f) - 4f * f * (1f - f) * l * 0.03f - 0.3f;
                                if (t > 0 && (Inside(x, z, -30f) || Inside(lx, lz, -30f))) Bar(wireMesh, lx, ly, lz, x, y, z, 0.025f, 0.025f, 3, wire);
                                lx = x; ly = y; lz = z;
                            }
                        }
                    }
                    break;
                }
            }
        }

        foreach (var p in points)
        {
            float x = p.x, z = p.z;
            if (!Inside(x, z, 3f)) continue;
            float y = H(x, z);
            switch (p.kind)
            {
                case WorldPointKind.Tower:
                case WorldPointKind.SmallTower:
                {
                    // A hunting tower: four slanting legs, a ladder, and a plank cabin with a window band and a roof.
                    // The small one is an open stand with a rail round the seat.
                    bool tall = p.kind == WorldPointKind.Tower;
                    float top = tall ? 4.6f : 2.4f, w = tall ? 0.8f : 0.6f, yaw = Rr(0f, 6.283f);
                    float ca = (float)Math.Cos(yaw), sa = (float)Math.Sin(yaw);
                    (float, float) At(float lx, float lz) => (x + lx * ca + lz * sa, z - lx * sa + lz * ca);
                    foreach (float sx in new[] { -1f, 1f })
                        foreach (float sz in new[] { -1f, 1f })
                        {
                            var (fx, fz) = At(sx * w * 1.45f, sz * w * 1.45f);
                            var (tx, tz) = At(sx * w * 0.95f, sz * w * 0.95f);
                            Bar(thingMesh, fx, H(fx, fz) - 0.2f, fz, tx, y + top, tz, 0.07f, 0.06f, 5, wood);
                        }
                    // Cross braces
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        var (ax, az) = At(-w * 1.3f, sz * w * 1.3f);
                        var (bx, bz) = At(w * 1.05f, sz * w * 1.05f);
                        Bar(thingMesh, ax, y + top * 0.25f, az, bx, y + top * 0.8f, bz, 0.035f, 0.035f, 4, wood);
                    }
                    // The ladder up the front
                    foreach (float lx in new[] { -0.22f, 0.22f })
                    {
                        var (fx, fz) = At(lx, w * 1.0f + top * 0.32f);
                        var (tx, tz) = At(lx, w * 1.0f);
                        Bar(thingMesh, fx, H(fx, fz) - 0.1f, fz, tx, y + top + 0.1f, tz, 0.035f, 0.035f, 4, wood);
                    }
                    for (float r = 0.35f; r < top; r += 0.35f)
                    {
                        float f = r / top, lz = w * 1.0f + top * 0.32f * (1f - f);
                        var (ax, az) = At(-0.22f, lz);
                        var (bx, bz) = At(0.22f, lz);
                        Bar(thingMesh, ax, y + r, az, bx, y + r, bz, 0.025f, 0.025f, 3, wood);
                    }
                    var t = new TvShapeBuilder(random);
                    TvColor plank = wood.Shade(1.12f), dark = TvColor.Hex("#1f2326");
                    t.Box(-w - 0.1f, -0.08f, -w - 0.1f, w + 0.1f, 0.04f, w + 0.1f, plank.Shade(0.85f));   // the floor
                    if (tall)
                    {
                        t.Box(-w, 0f, -w, w, 0.85f, w, plank);
                        t.Box(-w + 0.04f, 0.85f, -w + 0.04f, w - 0.04f, 1.35f, w - 0.04f, dark);   // the open window band
                        foreach (float sx in new[] { -1f, 1f })
                            foreach (float sz in new[] { -1f, 1f })
                                t.Box(sx * w - 0.06f, 0.85f, sz * w - 0.06f, sx * w + 0.06f, 1.35f, sz * w + 0.06f, plank);
                        t.Box(-w, 1.35f, -w, w, 1.6f, w, plank);
                        t.Triangle(-w, 1.6f, -w, -w, 1.6f, w, -w, 2.1f, 0f, 0f, 1.6f, 0f, plank);
                        t.Triangle(w, 1.6f, -w, w, 1.6f, w, w, 2.1f, 0f, 0f, 1.6f, 0f, plank);
                        Roof(t, w, w, 1.6f, 2.1f, 0.25f, roof);
                    }
                    else
                    {
                        t.Box(-w, 0.5f, -w, w, 0.58f, w, plank);    // the seat rail
                        t.Box(-0.35f, 0.35f, -w + 0.05f, 0.35f, 0.42f, -w + 0.4f, plank);   // the seat
                        foreach (float sx in new[] { -1f, 1f })
                            foreach (float sz in new[] { -1f, 1f })
                                t.Box(sx * w - 0.04f, 0f, sz * w - 0.04f, sx * w + 0.04f, 0.58f, sz * w + 0.04f, plank);
                    }
                    thingMesh.Add(t.Done(), x, y + top, z, 1f, 1f, 1f, yaw);
                    world.solids.Add(new TvSolid { x = x, z = z, r = 1.3f });
                    break;
                }
                case WorldPointKind.SmallBuilding:
                {
                    // A small building of the forest (a hut, a shed, a shelter), turned as the map draws it
                    // (2026-11-26, Anders on Fossum: the map's small buildings were missing)
                    float hl = Rr(1.8f, 2.4f), hw = Rr(1.4f, 1.8f);
                    Hut(x, z, hl, hw, -p.angle * 0.0174533f);
                    break;
                }
                case WorldPointKind.SmallRuin:
                {
                    // What is left of a small house: a low square of stones, overgrown
                    var t = new TvShapeBuilder(random);
                    float hl = Rr(1.6f, 2.2f), hw = Rr(1.3f, 1.7f);
                    foreach (float sz in new[] { -1f, 1f })
                        t.Box(-hl, -0.3f, sz * hw - 0.25f, hl, Rr(0.3f, 0.7f), sz * hw + 0.25f, stone);
                    foreach (float sx in new[] { -1f, 1f })
                        t.Box(sx * hl - 0.25f, -0.3f, -hw, sx * hl + 0.25f, Rr(0.3f, 0.7f), hw, stone.Shade(0.9f));
                    thingMesh.Add(t.Done(), x, y, z, 1f, 1f, 1f, Rr(0f, 6.283f));
                    break;
                }
                case WorldPointKind.FodderRack:
                {
                    // A fodder rack: a slatted hay box on two legs under a small pitched roof
                    var t = new TvShapeBuilder(random);
                    foreach (float sx in new[] { -1f, 1f })
                        t.Box(sx * 0.7f - 0.05f, -0.1f, -0.05f, sx * 0.7f + 0.05f, 1.45f, 0.05f, wood);
                    t.Box(-0.7f, 0.55f, -0.3f, 0.7f, 0.65f, 0.3f, wood);
                    for (float sx = -0.6f; sx <= 0.61f; sx += 0.2f)
                        t.Box(sx - 0.02f, 0.65f, -0.32f, sx + 0.02f, 1.15f, 0.32f, wood.Shade(1.1f));
                    t.Box(-0.6f, 0.68f, -0.28f, 0.6f, 0.95f, 0.28f, TvColor.Hex("#a59a5c"));   // hay
                    Roof(t, 0.75f, 0.45f, 1.4f, 1.7f, 0.15f, roof);
                    thingMesh.Add(t.Done(), x, y, z, 1f, 1f, 1f, Rr(0f, 6.283f));
                    world.solids.Add(new TvSolid { x = x, z = z, r = 1f });
                    break;
                }
                case WorldPointKind.BigTree:
                    treeMeshes[Tile(x, z)].Add(bigTree, x, y - 0.15f, z, 1f, Rr(0.95f, 1.15f), 1f, Rr(0f, 6.283f), Rr(0.9f, 1.08f));
                    world.solids.Add(new TvSolid { x = x, z = z, r = 0.75f });
                    world.trees++;
                    break;
                case WorldPointKind.Bush:
                {
                    float s = Rr(0.9f, 1.4f);
                    treeMeshes[Tile(x, z)].Add(bushShapes[random.Value() < 0.5f ? 0 : 1], x, y - 0.1f, z, s, s * Rr(0.85f, 1.1f), s, Rr(0f, 6.283f), Rr(0.85f, 1.1f));
                    world.solids.Add(new TvSolid { x = x, z = z, r = 0.8f * s });
                    break;
                }
                case WorldPointKind.Spring: thingMesh.Add(poolShape, x, y + 0.02f, z, 0.7f, 1f, 0.7f, 0f); break;
                case WorldPointKind.Well:
                    Bar(thingMesh, x, y - 0.1f, z, x, y + 0.5f, z, 0.6f, 0.6f, 8, stone);
                    thingMesh.Add(poolShape, x, y + 0.42f, z, 0.55f, 1f, 0.55f, 0f);
                    world.solids.Add(new TvSolid { x = x, z = z, r = 0.7f });
                    break;
                case WorldPointKind.WaterHole: thingMesh.Add(poolShape, x, y + 0.12f, z, 1.5f, 1f, 1.5f, 0f); break;
                case WorldPointKind.Feature:
                case WorldPointKind.ManMade:
                    OddThing(x, y, z);
                    break;
                case WorldPointKind.RootStock:
                {
                    // The root plate of a fallen tree, standing on edge
                    float yaw = Rr(0f, 6.283f);
                    thingMesh.Add(rootPlate, x, y - 0.3f, z, 1.2f, 1.1f, 1.2f, yaw);
                    // The trunk lies away from the plate
                    float tx = (float)Math.Sin(yaw), tz = (float)Math.Cos(yaw);
                    Log(x + tx * 0.3f, y + 0.55f, z + tz * 0.3f, x + tx * 7f, H(x + tx * 7f, z + tz * 7f) + 0.2f, z + tz * 7f, 0.2f, 0.12f);
                    world.AddHurdle(x + tx * 1.2f, z + tz * 1.2f, x + tx * 7f, z + tz * 7f, 0.4f);
                    world.solids.Add(new TvSolid { x = x, z = z, r = 0.9f });
                    break;
                }
            }
        }

        Buildings();
    }

    // ---- Houses: every group of building cells on the map becomes one house with walls, windows and a roof ----

    void Buildings()
    {
        // A piece looks a little past its edge, so a house across the edge is one house, built by the piece its middle is in
        // (a town looks further: its blocks are cut into houses, and each piece must cut a block the same way)
        int reach = chunk ? R + (input.town != null ? 90 : 40) : R;
        double e0 = Math.Floor((input.centerE - reach - layer.minE) / layer.cell), n0 = Math.Floor((input.centerN - reach - layer.minN) / layer.cell);
        int cells = (int)(2 * reach / layer.cell) + 2;
        if (chunk && input.town != null)
        {
            // Every block a house of this piece can belong to, whole (2026-11-24, Anders: a big house was see-through
            // now and then). A block longer than the piece's look was cut into houses differently by each piece that
            // saw a part of it, so the wall a house left to its neighbour was missing where the neighbour, built by
            // another piece, stood elsewhere. The window now holds all of every block that reaches into the piece.
            double i0 = e0, j0 = n0, i1 = e0 + cells, j1 = n0 + cells;
            double pi0 = Math.Floor((input.centerE - R - 2 - layer.minE) / layer.cell), pi1 = Math.Floor((input.centerE + R + 2 - layer.minE) / layer.cell);
            double pj0 = Math.Floor((input.centerN - R - 2 - layer.minN) / layer.cell), pj1 = Math.Floor((input.centerN + R + 2 - layer.minN) / layer.cell);
            foreach (int[] bb in TownBlocks())
            {
                if (bb[2] < pi0 || bb[0] > pi1 || bb[3] < pj0 || bb[1] > pj1) continue;
                i0 = Math.Min(i0, bb[0] - 1); j0 = Math.Min(j0, bb[1] - 1); i1 = Math.Max(i1, bb[2] + 2); j1 = Math.Max(j1, bb[3] + 2);
            }
            e0 = i0;
            n0 = j0;
            cells = (int)Math.Max(i1 - i0, j1 - j0) + 1;
        }
        var isHouse = new bool[cells * cells];
        if (input.town != null) TownLines();
        // A town: no house where the 2D race lets a runner through, except over a passage through a house (a tunnel)
        bool Open(float x, float z) => input.passable != null && input.passable.NoHouse(input.centerE + x, input.centerN + z)
                                       && !(input.town != null && TownLineDist(x, z, 2) < 1.8f);
        float CellX(int i) => (float)(layer.minE + (e0 + i + 0.5) * layer.cell - input.centerE);
        float CellZ(int j) => (float)(layer.minN + (n0 + j + 0.5) * layer.cell - input.centerN);
        for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
                isHouse[j * cells + i] = Base(layer.At(input.centerE + CellX(i), input.centerN + CellZ(j))) == WorldCell.Building
                                         && (chunk || Inside(CellX(i), CellZ(j), 2f))
                                         && !Open(CellX(i), CellZ(j));

        var seen = new bool[cells * cells];
        var group = new List<int>();
        var todo = new Stack<int>();
        for (int start = 0; start < isHouse.Length; start++)
        {
            if (!isHouse[start] || seen[start]) continue;
            group.Clear();
            todo.Push(start);
            seen[start] = true;
            while (todo.Count > 0)
            {
                int k = todo.Pop();
                group.Add(k);
                int ci = k % cells, cj = k / cells;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= cells || nj >= cells) continue;
                    int nk = nj * cells + ni;
                    if (isHouse[nk] && !seen[nk]) { seen[nk] = true; todo.Push(nk); }
                }
            }
            if (chunk && input.town == null)
            {
                float mx = 0f, mz = 0f;
                foreach (int k in group) { mx += CellX(k % cells); mz += CellZ(k / cells); }
                if (!Inside(mx / group.Count, mz / group.Count, 0f)) continue;
            }
            if (input.town != null) TownBlock(group, cells, CellX, CellZ, e0, n0);
            else House(group, cells, CellX, CellZ);
        }
    }

    // The town's blocks of joined house cells (as Buildings joins them), each as its cells' box: min col, min row,
    // max col, max row of the world layer. Worked out once for the whole map.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TvTown, Tuple<TvPassable, List<int[]>>> blockBoxes =
        new System.Runtime.CompilerServices.ConditionalWeakTable<TvTown, Tuple<TvPassable, List<int[]>>>();

    List<int[]> TownBlocks()
    {
        TvTown town = input.town;
        lock (blockBoxes)
        {
            if (blockBoxes.TryGetValue(town, out var known) && known.Item1 == input.passable) return known.Item2;
        }
        int C = layer.cols, Rw = layer.rows;
        var house = new bool[C * Rw];
        for (int j = 0; j < Rw; j++)
            for (int i = 0; i < C; i++)
            {
                double e = layer.minE + (i + 0.5) * layer.cell, n = layer.minN + (j + 0.5) * layer.cell;
                if ((layer.At(e, n) & WorldCell.BaseMask) != WorldCell.Building) continue;
                if (input.passable != null && input.passable.NoHouse(e, n) && !NearTunnel(town, e, n, 1.8)) continue;
                house[j * C + i] = true;
            }
        var boxes = new List<int[]>();
        var seen = new bool[C * Rw];
        var todo = new Stack<int>();
        for (int s = 0; s < house.Length; s++)
        {
            if (!house[s] || seen[s]) continue;
            int[] bb = { s % C, s / C, s % C, s / C };
            seen[s] = true;
            todo.Push(s);
            while (todo.Count > 0)
            {
                int k = todo.Pop(), ci = k % C, cj = k / C;
                bb[0] = Math.Min(bb[0], ci); bb[1] = Math.Min(bb[1], cj); bb[2] = Math.Max(bb[2], ci); bb[3] = Math.Max(bb[3], cj);
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= C || nj >= Rw) continue;
                    int nk = nj * C + ni;
                    if (house[nk] && !seen[nk]) { seen[nk] = true; todo.Push(nk); }
                }
            }
            boxes.Add(bb);
        }
        lock (blockBoxes)
        {
            blockBoxes.Remove(town);
            blockBoxes.Add(town, Tuple.Create(input.passable, boxes));
        }
        return boxes;
    }

    static bool NearTunnel(TvTown town, double e, double n, double reach)
    {
        foreach (float[] c in town.tunnels)
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                double sx = c[q + 2] - c[q], sz = c[q + 3] - c[q + 1], l2 = sx * sx + sz * sz;
                double t = l2 < 1e-6 ? 0 : Math.Max(0, Math.Min(1, ((e - c[q]) * sx + (n - c[q + 1]) * sz) / l2));
                double dx = c[q] + sx * t - e, dz = c[q + 1] + sz * t - n;
                if (dx * dx + dz * dz < reach * reach) return true;
            }
        return false;
    }

    // A pitched roof along x: two thick slabs meeting at the ridge, reaching out over the walls by over
    // A red forest house: plank walls on a stone plinth, a door and a window, white corners, a pitched roof. hl, hw: half
    // its length and width; yaw: which way its length goes. Solid: the runner keeps off its walls.
    void Hut(float x, float z, float hl, float hw, float yaw)
    {
        TvColor house = TvColor.Hex("#b4472f"), roof = TvColor.Hex("#3f3b38"), stone = TvColor.Hex("#7e7b74");
        float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw);
        // The corners as the mesh puts them (TvMesh.Add: x' = x cos + z sin, z' = -x sin + z cos)
        float CornerX(float lx, float lz) => x + lx * cos + lz * sin;
        float CornerZ(float lx, float lz) => z - lx * sin + lz * cos;
        // Standing level on the highest corner, the plinth down to the lowest
        float hi = float.MinValue, lo = float.MaxValue;
        foreach (float sx in new[] { -1f, 0f, 1f })
            foreach (float sz in new[] { -1f, 0f, 1f })
            {
                float h = H(CornerX(sx * hl, sz * hw), CornerZ(sx * hl, sz * hw));
                hi = Math.Max(hi, h);
                lo = Math.Min(lo, h);
            }
        float y = hi + 0.05f, foot = lo - y - 0.3f;
        // A bigger house stands taller
        float eaves = hw > 3f ? 2.8f : 2.2f, ridge = eaves + Math.Min(hw * 0.8f, 3.2f);
        var t = new TvShapeBuilder(random);
        t.Box(-hl, foot, -hw, hl, eaves, hw, house);
        t.Box(-hl - 0.02f, foot, -hw - 0.02f, hl + 0.02f, 0.05f, hw + 0.02f, stone);   // the plinth
        t.Box(-0.45f, 0.05f, -hw - 0.04f, 0.45f, 1.95f, -hw + 0.02f, house.Shade(0.6f));   // the door
        t.Box(hl * 0.45f, 0.95f, -hw - 0.04f, hl * 0.45f + 0.7f, 1.6f, -hw + 0.02f, TvColor.Hex("#2c3238"));   // a window
        if (hl > 3f) t.Box(-hl * 0.45f - 0.7f, 0.95f, -hw - 0.04f, -hl * 0.45f, 1.6f, -hw + 0.02f, TvColor.Hex("#2c3238"));
        // The white corners and the gable ends up to the ridge
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                t.Box(sx * hl - 0.06f, foot, sz * hw - 0.06f, sx * hl + 0.06f, eaves, sz * hw + 0.06f, TvColor.Hex("#e8e4da"));
        for (int k = 0; k < 6; k++)
        {
            float f0 = k / 6f, f1 = (k + 1) / 6f, yy0 = eaves + (ridge - eaves) * f0, yy1 = eaves + (ridge - eaves) * f1;
            float half = hw * (1f - f1 * 0.98f);
            foreach (float sx in new[] { -1f, 1f })
                t.Box(sx * hl - 0.05f, yy0, -half, sx * hl + 0.05f, yy1, half, house);
        }
        Roof(t, hl, hw, eaves, ridge, 0.3f, roof);
        thingMesh.Add(t.Done(), x, y, z, 1f, 1f, 1f, yaw);
        // The walls (the runner keeps its reach from them), and a solid in the middle for the people and the trees
        float ax = CornerX(-hl, -hw), az = CornerZ(-hl, -hw), bx = CornerX(hl, -hw), bz = CornerZ(hl, -hw);
        float cx = CornerX(hl, hw), cz = CornerZ(hl, hw), dx = CornerX(-hl, hw), dz = CornerZ(-hl, hw);
        world.AddWall(ax, az, bx, bz);
        world.AddWall(bx, bz, cx, cz);
        world.AddWall(cx, cz, dx, dz);
        world.AddWall(dx, dz, ax, az);
        world.solids.Add(new TvSolid { x = x, z = z, r = Math.Min(hl, hw) + 0.3f });
    }

    // The buildings a forest map draws as areas (2026-11-26, Anders on Fossum: of two buildings beside each other
    // only the one drawn as a point stood in 3D): a house to the shape of each, the smallest rectangle round its
    // outline. Built by the piece its middle is in.
    void ForestHouses()
    {
        if (input.town != null || layer.areas == null) return;
        foreach (WorldArea a in layer.areas)
        {
            if (a.kind != WorldAreaKind.House || a.hole || a.xy == null || a.xy.Length < 6) continue;
            float cxE = (a.minE + a.maxE) * 0.5f, cxN = (a.minN + a.maxN) * 0.5f;
            if (Math.Abs(cxE - input.centerE) > R + 60f || Math.Abs(cxN - input.centerN) > R + 60f) continue;
            float[] xy = a.xy;
            int n = xy.Length / 2;
            // The smallest rectangle round the outline: one of its sides lies along a side of the outline
            float best = float.MaxValue, ux = 1f, uz = 0f, u0 = 0f, u1 = 0f, v0 = 0f, v1 = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ex = xy[j * 2] - xy[i * 2], ez = xy[j * 2 + 1] - xy[i * 2 + 1], l = (float)Math.Sqrt(ex * ex + ez * ez);
                if (l < 0.2f) continue;
                ex /= l; ez /= l;
                float lu = float.MaxValue, hu = float.MinValue, lv = float.MaxValue, hv = float.MinValue;
                for (int k = 0; k < n; k++)
                {
                    float px = xy[k * 2] - cxE, pz = xy[k * 2 + 1] - cxN;
                    float pu = px * ex + pz * ez, pv = -px * ez + pz * ex;
                    lu = Math.Min(lu, pu); hu = Math.Max(hu, pu);
                    lv = Math.Min(lv, pv); hv = Math.Max(hv, pv);
                }
                float area = (hu - lu) * (hv - lv);
                if (area < best - 1e-3f) { best = area; ux = ex; uz = ez; u0 = lu; u1 = hu; v0 = lv; v1 = hv; }
            }
            if (best == float.MaxValue) continue;
            float mu = (u0 + u1) * 0.5f, mv = (v0 + v1) * 0.5f;
            float x = (float)(cxE + mu * ux - mv * uz - input.centerE), z = (float)(cxN + mu * uz + mv * ux - input.centerN);
            if (!Inside(x, z, 0f)) continue;
            float hl = Math.Max(1f, (u1 - u0) * 0.5f), hw = Math.Max(1f, (v1 - v0) * 0.5f);
            // The length along the longer side
            if (hw > hl) { float s = hl; hl = hw; hw = s; float ox2 = ux; ux = -uz; uz = ox2; }
            // Very big buildings stay as the map has them (blocked paving): a forest map's barns and halls are rare
            if (hl > 25f) continue;
            Reseed(x, z, 97);
            Hut(x, z, hl, hw, (float)Math.Atan2(-uz, ux));
        }
    }

    static void Roof(TvShapeBuilder b, float hl, float hw, float eaves, float ridge, float over, TvColor color)
    {
        float pitch = (ridge - eaves) / Math.Max(0.01f, hw);
        foreach (float side in new[] { -1f, 1f })
        {
            float x0 = -hl - over, x1 = hl + over, ze = side * (hw + over), yr = ridge + 0.05f, ye = eaves - over * pitch;
            const float T = 0.14f;
            float[] q =
            {
                x0, ye, ze, x1, ye, ze, x1, yr, 0f, x0, yr, 0f,
                x0, ye + T, ze, x1, ye + T, ze, x1, yr + T, 0f, x0, yr + T, 0f
            };
            float mx = 0f, my = 0f, mz = 0f;
            for (int i = 0; i < 24; i += 3) { mx += q[i]; my += q[i + 1]; mz += q[i + 2]; }
            mx /= 8f; my /= 8f; mz /= 8f;
            void F(int a, int c, int e, int d)
            {
                b.Triangle(q[a * 3], q[a * 3 + 1], q[a * 3 + 2], q[c * 3], q[c * 3 + 1], q[c * 3 + 2], q[e * 3], q[e * 3 + 1], q[e * 3 + 2], mx, my, mz, color);
                b.Triangle(q[a * 3], q[a * 3 + 1], q[a * 3 + 2], q[e * 3], q[e * 3 + 1], q[e * 3 + 2], q[d * 3], q[d * 3 + 1], q[d * 3 + 2], mx, my, mz, color);
            }
            F(4, 5, 6, 7); F(0, 1, 2, 3); F(0, 1, 5, 4); F(2, 3, 7, 6); F(0, 3, 7, 4); F(1, 2, 6, 5);
        }
    }

    void House(List<int> group, int cells, Func<int, float> cellX, Func<int, float> cellZ)
    {
        float c = layer.cell;
        // Which way the house lies: the long axis of its cells
        float mx = 0f, mz = 0f;
        foreach (int k in group) { mx += cellX(k % cells); mz += cellZ(k / cells); }
        mx /= group.Count; mz /= group.Count;
        float sxx = 0f, szz = 0f, sxz = 0f;
        foreach (int k in group)
        {
            float dx = cellX(k % cells) - mx, dz = cellZ(k / cells) - mz;
            sxx += dx * dx; szz += dz * dz; sxz += dx * dz;
        }
        float angle = 0.5f * (float)Math.Atan2(2f * sxz, sxx - szz);
        // A house drawn as a square of cells lies along the grid
        if (Math.Abs(sxx - szz) < 0.15f * (sxx + szz) && Math.Abs(sxz) < 0.15f * (sxx + szz)) angle = 0f;
        float ux = (float)Math.Cos(angle), uz = (float)Math.Sin(angle);
        float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
        foreach (int k in group)
        {
            float dx = cellX(k % cells) - mx, dz = cellZ(k / cells) - mz;
            float u = dx * ux + dz * uz, v = -dx * uz + dz * ux;
            float half = c * 0.5f * (Math.Abs(ux) + Math.Abs(uz));
            u0 = Math.Min(u0, u - half); u1 = Math.Max(u1, u + half);
            v0 = Math.Min(v0, v - half); v1 = Math.Max(v1, v + half);
        }
        // Cells round a slanted outline stick out a little: pull the rectangle in to the area the cells cover
        float area = group.Count * c * c, len = u1 - u0, wid = v1 - v0;
        float fill = area / Math.Max(1f, len * wid);
        if (fill < 0.92f)
        {
            float shrink = (float)Math.Sqrt(Math.Max(0.55f, fill));
            float cu = (u0 + u1) / 2f, cv = (v0 + v1) / 2f;
            len *= shrink; wid *= shrink;
            u0 = cu - len / 2f; u1 = cu + len / 2f; v0 = cv - wid / 2f; v1 = cv + wid / 2f;
        }
        float cx = mx + ux * (u0 + u1) / 2f - uz * (v0 + v1) / 2f, cz = mz + uz * (u0 + u1) / 2f + ux * (v0 + v1) / 2f;
        len = Math.Max(len, 3f); wid = Math.Max(wid, 2.6f);
        foreach (int k in group) world.solids.Add(new TvSolid { x = cellX(k % cells), z = cellZ(k / cells), r = c * 0.75f });

        // A shape that is far from a rectangle (an L, a farm with wings) keeps its cells, with one roof each
        if (fill < 0.55f)
        {
            TvColor w = TvColor.Hex("#8f3426"), r = TvColor.Hex("#3a3634");
            foreach (int k in group)
            {
                float x = cellX(k % cells), z = cellZ(k / cells), y = H(x, z);
                thingMesh.Add(Box(c + 0.02f, 3.2f, c + 0.02f, w), x, y - 0.6f, z, 1f, 1f, 1f, 0f);
                thingMesh.Add(Box(c + 0.3f, 0.22f, c + 0.3f, r), x, y + 2.6f, z, 1f, 1f, 1f, 0f);
            }
            return;
        }

        float big = len * wid;
        bool shed = big > 260f || len > 24f;
        int look = (int)(TvRandom.Hash((int)(cx * 7f), (int)(cz * 7f)) * 4f);
        TvColor wall = shed ? TvColor.Hex("#8d877b") : look == 3 ? TvColor.Hex("#d9cfb4") : TvColor.Hex("#8f3426");
        TvColor trim = TvColor.Hex("#e9e5da"), glass = TvColor.Hex("#26313a"), plinth = TvColor.Hex("#6b6964"),
            roofC = shed ? TvColor.Hex("#5a5e60") : look == 1 ? TvColor.Hex("#7b3a2b") : TvColor.Hex("#363331"), door = TvColor.Hex("#3d4f5e");
        if (look == 3) trim = TvColor.Hex("#ffffff");

        float hl = len / 2f, hw = wid / 2f;
        float lowest = float.MaxValue, highest = float.MinValue;
        foreach (float su in new[] { -1f, 0f, 1f })
            foreach (float sv in new[] { -1f, 0f, 1f })
            {
                float x = cx + ux * su * hl - uz * sv * hw, z = cz + uz * su * hl + ux * sv * hw, y = H(x, z);
                lowest = Math.Min(lowest, y); highest = Math.Max(highest, y);
            }
        float floor = highest - lowest + 0.35f;           // the plinth takes up the slope
        float eaves = floor + (shed ? 4.2f : 2.5f);
        float pitch = shed ? 0.22f : 0.7f;                 // rise per metre
        float ridge = eaves + hw * pitch;
        float over = 0.35f;

        var b = new TvShapeBuilder(random);
        b.Box(-hl - 0.05f, 0f, -hw - 0.05f, hl + 0.05f, floor, hw + 0.05f, plinth);
        b.Box(-hl, floor, -hw, hl, eaves, hw, wall);
        // The gables
        foreach (float end in new[] { -hl, hl })
            b.Triangle(end, eaves, -hw, end, eaves, hw, end, ridge, 0f, 0f, eaves, 0f, wall);
        // White corner boards and windows with frames
        if (!shed)
        {
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f })
                    b.Box(su * hl - 0.09f, floor, sv * hw - 0.09f, su * hl + 0.09f, eaves, sv * hw + 0.09f, trim);
        }
        float winY = floor + 0.85f, winH = shed ? 0.7f : 1.1f, winW = shed ? 1.6f : 0.95f, spacing = shed ? 4.5f : 2.6f;
        int perSide = Math.Max(1, (int)((len - 1.2f) / spacing));
        int doorAt = perSide > 1 ? perSide / 2 : -1;
        foreach (float sv in new[] { -1f, 1f })
        {
            for (int i = 0; i < perSide; i++)
            {
                float u = -hl + len * (i + 0.5f) / perSide, wv = sv * hw;
                if (sv > 0f && i == doorAt)
                {
                    b.Box(u - 0.55f, floor - 0.05f, wv - 0.02f * sv - 0.05f, u + 0.55f, floor + 2.15f, wv + 0.05f * sv + 0.05f, trim);
                    b.Box(u - 0.45f, floor, wv - 0.02f, u + 0.45f, floor + 2.05f, wv + 0.09f * sv, door);
                    continue;
                }
                b.Box(u - winW / 2f - 0.08f, winY - 0.08f, wv - 0.03f, u + winW / 2f + 0.08f, winY + winH + 0.08f, wv + 0.03f, trim);
                b.Box(u - winW / 2f, winY, wv - 0.05f, u + winW / 2f, winY + winH, wv + 0.05f, glass);
                // The cross bars of an old window
                if (!shed) b.Box(u - 0.03f, winY, wv - 0.06f, u + 0.03f, winY + winH, wv + 0.06f, trim);
            }
        }
        if (!shed)
            foreach (float su in new[] { -1f, 1f })
            {
                float wu = su * hl, y = eaves + 0.15f;
                b.Box(wu - 0.03f, y - 0.08f, -0.4f, wu + 0.03f, y + 0.82f, 0.4f, trim);
                b.Box(wu - 0.05f, y, -0.32f, wu + 0.05f, y + 0.74f, 0.32f, glass);
            }
        Roof(b, hl, hw, eaves, ridge, over, roofC);
        if (!shed && len > 5f)
        {
            float u = hl * 0.4f;
            b.Box(u - 0.3f, ridge - 0.9f, -0.75f, u + 0.3f, ridge + 0.75f, -0.15f, TvColor.Hex("#7d746c"));
            b.Box(u - 0.36f, ridge + 0.75f, -0.81f, u + 0.36f, ridge + 0.85f, -0.09f, TvColor.Hex("#4a4644"));
        }
        thingMesh.Add(b.Done(), cx, lowest - 0.25f, cz, 1f, 1f, 1f, -angle);
    }

    // ---- The control: a stand with the punching unit on top and the flag under it ----

    void ControlFlag()
    {
        // The control point is the feature itself (a stone, a knoll top); the flag stands beside it, clear of the stone
        float fx = 0f, fz = 0f;
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (TvSolid s in world.solids)
            {
                float dx = fx - s.x, dz = fz - s.z, d = (float)Math.Sqrt(dx * dx + dz * dz), need = s.r + 0.45f;
                if (d >= need) continue;
                if (d < 0.05f) { dx = 0.6f; dz = -0.8f; d = 1f; }
                fx = s.x + dx / d * need;
                fz = s.z + dz / d * need;
            }
        }
        float y = H(fx, fz);
        world.flagX = fx;
        world.flagZ = fz;
        world.unitX = fx;
        world.unitY = y + 1.02f;
        world.unitZ = fz;
        world.hasUnit = !input.start;
        if (input.start) return;

        thingMesh.Add(FlagShape(random), fx, y, fz, 1f, 1f, 1f, Rr(0f, 6.283f));
    }

    // ---- The TV camera's spot: the direction with the fewest stones and walls between it and the control ----

    void CameraSpot()
    {
        float bestScore = float.MaxValue, bx = 9f, bz = -9f;
        float y0 = H(world.flagX, world.flagZ);
        bool wish = input.camDirX != 0f || input.camDirZ != 0f;
        // At the start the camera may stand further off, up on a rise or a stone, looking down the first leg
        bool start = input.start && (input.legDirX != 0f || input.legDirZ != 0f);
        int rings = start ? 3 : 2;
        for (int k = 0; k < 24 * rings; k++)
        {
            float a = (k / rings) / 24f * 6.2831853f, dx = (float)Math.Cos(a), dz = (float)Math.Sin(a);
            float far = start ? 10f + 4f * (k % rings) : k % 2 == 0 ? 10f : 13f;
            float x = world.flagX + dx * far, z = world.flagZ + dz * far;
            float score = far > 10f ? 0.5f : 0f;
            // The side the course asks for: where the runners come in and go out in front of the camera
            if (wish) score += (1f - (dx * input.camDirX + dz * input.camDirZ)) * 14f;
            foreach (TvSolid s in world.solids)
            {
                float d = DistSeg(s.x, s.z, world.flagX, world.flagZ, x, z, out _, out _);
                if (d < s.r + 0.6f) score += 10f;
            }
            // A spot a little above the control sees it best; a spot in a hollow or on water does not
            float dy = H(x, z) - y0;
            if (start)
            {
                // The higher the better, and a clear view 40 m down the first leg
                score -= Math.Min(dy, 3f) * 1.5f;
                float tx = world.flagX + input.legDirX * 40f, tz = world.flagZ + input.legDirZ * 40f;
                foreach (TvSolid s in world.solids)
                    if (DistSeg(s.x, s.z, x, z, tx, tz, out _, out _) < s.r + 0.8f) score += 1.5f;
                // Ground in the way: the line of sight dips under a hill
                for (float f = 0.2f; f < 1f; f += 0.2f)
                {
                    float sx = Lerp(x, tx, f), sz = Lerp(z, tz, f), sight = Lerp(H(x, z) + 1.6f, H(tx, tz) + 1f, f);
                    if (H(sx, sz) > sight) score += 4f;
                }
                // A stone or a knoll right there to stand on
                foreach (var p in points)
                    if ((p.kind == WorldPointKind.Boulder || p.kind == WorldPointKind.LargeBoulder || p.kind == WorldPointKind.Knoll)
                        && (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < 9f) { score -= 3f; break; }
            }
            else score += Math.Abs(dy - 1f) * 2f;
            int kind = Base(CellAt(x, z));
            if (kind == WorldCell.Water || kind == WorldCell.Building) score += 50f;
            if (ClearAt(x, z) < 0.5f) score += 2f;
            // The sun stands in the south-east (TvWorldView); a camera on that side has the light behind it
            score += (dx * 0.55f - dz * 0.5f) > 0f ? 0f : 1.5f;
            if (score < bestScore)
            {
                bestScore = score;
                bx = x;
                bz = z;
            }
        }
        world.camX = bx;
        world.camZ = bz;
    }

    // ---- Trees: the kind follows the terrain look, how dense they stand follows the map ----

    int Tile(float x, float z)
    {
        if (chunk) return 0;   // a run-yourself piece is one tile
        int i = Math.Max(0, Math.Min(TreeTiles - 1, (int)((x + R) / (2f * R) * TreeTiles)));
        int j = Math.Max(0, Math.Min(TreeTiles - 1, (int)((z + R) / (2f * R) * TreeTiles)));
        return j * TreeTiles + i;
    }

    bool Blocked(float x, float z, float gap)
    {
        if (!chunk && x * x + z * z < 9f) return true;                        // the control stands free
        float dx = x - world.flagX, dz = z - world.flagZ;
        if (dx * dx + dz * dz < 6f) return true;
        // Nothing between the TV camera and the control
        if (DistSeg(x, z, world.camX, world.camZ, world.flagX, world.flagZ, out _, out _) < 1.8f) return true;
        dx = x - world.camX;
        dz = z - world.camZ;
        if (dx * dx + dz * dz < 12f) return true;
        if (ClearAt(x, z) < gap) return true;
        foreach (TvSolid s in world.solids)
        {
            float sx = x - s.x, sz = z - s.z, r = s.r + 0.8f;
            if (sx * sx + sz * sz < r * r) return true;
        }
        return false;
    }

    enum Tree { Pine, Pine2, Spruce, Birch, Beech }

    Tree ForestTree()
    {
        float r = random.Value();
        switch (look.forest)
        {
            case TerrainLook.Pine: return r < 0.55f ? Tree.Pine : r < 0.95f ? Tree.Pine2 : Tree.Birch;
            case TerrainLook.Spruce: return r < 0.8f ? Tree.Spruce : r < 0.9f ? Tree.Pine : Tree.Birch;
            case TerrainLook.Birch: return r < 0.8f ? Tree.Birch : r < 0.9f ? Tree.Pine2 : Tree.Spruce;
            case TerrainLook.Beech: return r < 0.75f ? Tree.Beech : r < 0.87f ? Tree.Birch : Tree.Spruce;
            default: return r < 0.48f ? Tree.Pine : r < 0.74f ? Tree.Pine2 : r < 0.93f ? Tree.Spruce : Tree.Birch;
        }
    }

    // The young, dense forest of the green areas
    Tree ThicketTree(bool dense)
    {
        bool leafy = look.forest == TerrainLook.Birch || look.forest == TerrainLook.Beech;
        if (leafy) return random.Value() < 0.8f ? Tree.Birch : Tree.Spruce;
        if (look.forest == TerrainLook.Pine && !dense) return random.Value() < 0.6f ? Tree.Pine2 : Tree.Birch;
        return dense || random.Value() < 0.8f ? Tree.Spruce : Tree.Birch;
    }

    void Trees()
    {
        const float Step = 2.2f;
        bool beech = look.forest == TerrainLook.Beech;
        // Bare ground carries fewer trees
        float thin = look.ground == TerrainLook.Stone ? 0.55f : look.ground == TerrainLook.Sand || look.ground == TerrainLook.Lichen ? 0.8f : 1f;
        // A run-yourself piece: the squares of the steps lie the same way on the whole map, a square belongs to the
        // piece its corner is in, and each square draws its own numbers, so a tree is the same in every build
        float first = -R + 3f, last = R - 3f;
        float firstX = first, firstZ = first;
        if (chunk)
        {
            firstX = (float)Math.Ceiling((-R + ox) / Step) * Step - ox;
            firstZ = (float)Math.Ceiling((-R + oz) / Step) * Step - oz;
            last = R;
        }
        for (float gz = firstZ; gz < last; gz += Step)
        {
            for (float gx = firstX; gx < last; gx += Step)
            {
                if (chunk) Reseed(gx + Step * 0.5f, gz + Step * 0.5f, 17);
                float x = gx + Rr(0f, Step), z = gz + Rr(0f, Step);
                byte cell = CellAt(x, z);
                int kind = Base(cell);
                bool marsh = (cell & WorldCell.Marsh) != 0;
                float clump = TvRandom.Patches((x + ox) * 0.05f + 7f, (z + oz) * 0.05f + 3f), p, s;
                Tree tree;
                switch (kind)
                {
                    case WorldCell.Forest:
                        p = 0.2f * (0.55f + clump) * thin;
                        tree = ForestTree();
                        s = Rr(0.8f, 1.22f);
                        if (tree == Tree.Beech) p *= 0.5f;
                        if (marsh)
                        {
                            p *= 0.45f;
                            tree = random.Value() < 0.7f ? Tree.Pine2 : Tree.Birch;
                            s = Rr(0.3f, 0.55f);
                        }
                        break;
                    case WorldCell.LightGreen: p = 0.42f; tree = ThicketTree(false); s = Rr(0.28f, 0.62f); break;
                    case WorldCell.Green: p = 0.8f; tree = ThicketTree(true); s = Rr(0.2f, 0.45f); break;
                    case WorldCell.DarkGreen: case WorldCell.Impassable: p = 0.95f; tree = ThicketTree(true); s = Rr(0.18f, 0.4f); break;
                    case WorldCell.OpenTrees:
                    case WorldCell.RoughOpenTrees:
                        p = 0.035f;
                        tree = beech ? Tree.Birch : random.Value() < 0.5f ? Tree.Birch : Tree.Pine2;
                        s = Rr(0.4f, 0.75f);
                        break;
                    case WorldCell.Open:
                        p = 0.004f;
                        tree = random.Value() < 0.5f ? Tree.Birch : Tree.Pine2;
                        s = Rr(0.35f, 0.7f);
                        break;
                    case WorldCell.RoughOpen:
                    {
                        // A clear-cut: a few full-grown trees left standing for the birds (2026-11-25, Anders' photos),
                        // tall thin pines, spruces and birches, often two or three together
                        p = 0.01f * (0.3f + 1.7f * clump);
                        float r = random.Value();
                        tree = r < 0.45f ? Tree.Pine : r < 0.7f ? Tree.Spruce : Tree.Birch;
                        s = Rr(0.85f, 1.15f);
                        break;
                    }
                    case WorldCell.Plot: p = 0.012f; tree = Tree.Birch; s = Rr(0.5f, 0.9f); break;
                    case WorldCell.BareRock: p = 0.012f; tree = Tree.Pine2; s = Rr(0.3f, 0.5f); break;
                    default: continue;
                }
                // Young spruces and pines coming up between the old trees (Anders' photos of pine forest)
                if (kind == WorldCell.Forest && !marsh && input.town == null && youngSpruce != null && random.Value() < 0.05f)
                {
                    float yx = x + Rr(-0.8f, 0.8f), yz = z + Rr(-0.8f, 0.8f);
                    if (!Blocked(yx, yz, 0.6f) && (townLines == null || townLines.Count == 0 || !BridgeWater(yx, yz)))
                    {
                        float yh = Rr(0.6f, 2.8f) / 2.5f;
                        (chunk ? thicketMesh : treeMeshes[Tile(yx, yz)]).Add(youngSpruce[(int)(random.Value() * youngSpruce.Length) % youngSpruce.Length],
                            yx, H(yx, yz) - 0.05f, yz, yh * Rr(0.85f, 1.2f), yh, yh * Rr(0.85f, 1.2f), Rr(0f, 6.283f), Rr(0.8f, 1.1f),
                            Rr(-0.04f, 0.04f), Rr(-0.04f, 0.04f));
                        world.trees++;
                    }
                }
                if (random.Value() > p || Blocked(x, z, 1.3f)) continue;
                // A town: no crown in a house, a canopy, a bridge or a street thing (the crown reaches about 2 m)
                if (input.town != null && (TownKeepOut(x, z, 1.2f + 1.6f * s) || !Free(x, z, 1.2f + 1.4f * s))) continue;
                // A forest map: not on its footbridges, nor in the water round one (TvBridges)
                if (townLines != null && townLines.Count > 0 && (BridgeWater(x, z) || (input.town == null && TownLineDist(x, z, 1) < 2.5f))) continue;

                bool near = chunk || x * x + z * z < 70f * 70f;
                float pickAt = random.Value();
                int Pick(TvShape[] set) => set == null ? 0 : Math.Min(set.Length - 1, (int)(pickAt * set.Length));
                TvShape shape, far;
                float trunk;
                switch (tree)
                {
                    case Tree.Pine: shape = (near ? pineNear : pineFar)[Pick(near ? pineNear : pineFar)]; far = pineFar?[Pick(pineFar)]; trunk = 0.3f; break;
                    case Tree.Pine2: shape = (near ? pine2Near : pine2Far)[Pick(near ? pine2Near : pine2Far)]; far = pine2Far?[Pick(pine2Far)]; trunk = 0.28f; break;
                    case Tree.Spruce: shape = spruceShape[Pick(spruceShape)]; far = spruceFar; trunk = 0.45f; break;   // the low branches are in the way too
                    case Tree.Birch: shape = (near ? birchNear : birchFar)[Pick(near ? birchNear : birchFar)]; far = birchFar?[Pick(birchFar)]; trunk = 0.22f; break;
                    default: shape = (near ? beechNear : beechFar)[Pick(near ? beechNear : beechFar)]; far = beechFar?[Pick(beechFar)]; trunk = 0.42f; break;
                }
                float y = H(x, z) - 0.15f, sy = s * Rr(0.9f, 1.12f), yaw = Rr(0f, 6.283f), tint = Rr(0.82f, 1.12f), tx = Rr(-0.03f, 0.03f), tz = Rr(-0.03f, 0.03f);
                if (chunk && input.treeRecords)
                {
                    world.treeRecords.Add(TreeIndex(shape, true));
                    world.treeRecords.Add(TreeIndex(far, false));
                    world.treeRecords.Add(x); world.treeRecords.Add(y); world.treeRecords.Add(z);
                    world.treeRecords.Add(s); world.treeRecords.Add(sy); world.treeRecords.Add(yaw); world.treeRecords.Add(tint);
                    world.treeRecords.Add(tx); world.treeRecords.Add(tz);
                }
                else
                {
                    if (!(chunk && input.skipNearTrees)) treeMeshes[Tile(x, z)].Add(shape, x, y, z, s, sy, s, yaw, tint, tx, tz);
                    // A piece of the run-yourself world: the same tree, cheaper, for when it is far from the runner
                    if (chunk) treeFarMesh.Add(far, x, y, z, s, sy, s, yaw, tint, tx, tz);
                }
                world.trees++;
                world.solids.Add(new TvSolid { x = x, z = z, r = Math.Max(0.18f, trunk * (0.5f + s * 0.5f)) });
            }
        }

        if (!chunk) Thickets();
    }

    // Thickets near the camera: green areas packed with young trees, as hard to see through as they are to run through.
    // A run-yourself piece has them over the whole piece, in its near detail.
    void Thickets()
    {
        const float Near = 75f, Fine = 0.95f;
        TvMesh target = chunk ? thicketMesh : null;
        float from = chunk ? -R : -Near, to = chunk ? R : Near;
        for (float gz = from; gz < to; gz += Fine)
        {
            for (float gx = from; gx < to; gx += Fine)
            {
                float x = gx + Rr(-0.4f, 0.4f), z = gz + Rr(-0.4f, 0.4f);
                if ((!chunk && x * x + z * z > Near * Near) || !Inside(x, z, 2f)) continue;
                int kind = Base(CellAt(x, z));
                float p, h;
                switch (kind)
                {
                    case WorldCell.LightGreen: p = 0.22f; h = Rr(1.2f, 2.6f); break;
                    case WorldCell.Green: p = 0.55f; h = Rr(1.4f, 2.8f); break;
                    case WorldCell.DarkGreen: p = 0.85f; h = Rr(1.6f, 3.2f); break;
                    case WorldCell.Impassable: p = 1f; h = Rr(2f, 3.4f); break;
                    default: continue;
                }
                if (random.Value() > p || Blocked(x, z, 0.8f)) continue;
                if (input.town != null && (TownKeepOut(x, z, 0.9f) || TownBare(x, z))) continue;
                if (townLines != null && townLines.Count > 0 && (BridgeWater(x, z) || (input.town == null && TownLineDist(x, z, 1) < 1.8f))) continue;
                bool spruce = ThicketTree(kind != WorldCell.LightGreen) != Tree.Birch;
                TvShape[] set = spruce ? youngSpruce : youngLeafy;
                float sc = h / 2.5f;
                (target ?? treeMeshes[Tile(x, z)]).Add(set[(int)(random.Value() * set.Length) % set.Length], x, H(x, z) - 0.05f, z,
                    sc * Rr(0.85f, 1.2f), sc, sc * Rr(0.85f, 1.2f), Rr(0f, 6.283f), Rr(0.8f, 1.1f), Rr(-0.04f, 0.04f), Rr(-0.04f, 0.04f));
                world.trees++;
            }
        }
    }

    // ---- Undergrowth: what covers the ground between the trees ----

    static readonly float[] UndergrowthDensity = { 0.7f, 1.2f, 2.2f, 3.2f };

    void Undergrowth()
    {
        const float Reach = 75f;
        // How much grows: the player's slider, and the kind of ground
        float amount = (look.undergrowth - 0.5f) * 0.5f;
        switch (look.ground)
        {
            case TerrainLook.Heather: amount += 0.12f; break;
            case TerrainLook.Grass: amount += 0.16f; break;
            case TerrainLook.Hard: amount -= 0.16f; break;
            case TerrainLook.Sand: amount -= 0.2f; break;
            case TerrainLook.Stone: amount -= 0.1f; break;
            case TerrainLook.Lichen: amount -= 0.16f; break;
        }
        if (look.forest == TerrainLook.Beech) amount -= 0.12f;
        // Thicker on the higher 3D qualities (the plants are cards now, cheap to draw; 2026-11-25)
        int tries = (int)(36000f * (0.6f + look.undergrowth * 0.8f) * UndergrowthDensity[Q]);
        // A run-yourself piece: as many per square metre as round a TV control, times the player's 3D quality
        if (chunk) tries = (int)(tries * (2f * R) * (2f * R) / (Math.PI * Reach * Reach) * Math.Max(0f, input.undergrowthShare));
        for (int i = 0; i < tries; i++)
        {
            float x, z;
            if (chunk)
            {
                x = Rr(-R, R);
                z = Rr(-R, R);
            }
            else
            {
                float a = Rr(0f, 6.283f), d = (float)Math.Sqrt(random.Value()) * Reach;
                x = (float)Math.Cos(a) * d;
                z = (float)Math.Sin(a) * d;
            }
            if (!Inside(x, z, 2f)) continue;
            byte cell = CellAt(x, z);
            int kind = Base(cell);
            if (kind == WorldCell.Water || kind == WorldCell.BareRock || kind == WorldCell.Paved || kind == WorldCell.Building
                || kind == WorldCell.Sand || kind == WorldCell.Cultivated) continue;
            if (ClearAt(x, z) < 0.3f) continue;
            if (input.town != null && TownBare(x, z)) continue;
            float cx = x - world.camX, cz = z - world.camZ;
            if (cx * cx + cz * cz < 6.25f) continue;   // nothing in front of the TV camera's lens
            int gi = (int)Math.Round(x) + R, gj = (int)Math.Round(z) + R;
            float strip = gi >= 0 && gj >= 0 && gi < N && gj < N ? bog[gj * N + gi] : 0f;
            if (MarshPool((float)Math.Floor(x), (float)Math.Floor(z), (cell & WorldCell.Marsh) != 0, strip)) continue;   // open water
            bool marsh = (cell & WorldCell.Marsh) != 0 || strip > 0.4f, open = kind == WorldCell.Open || kind == WorldCell.OpenTrees || kind == WorldCell.Plot;
            bool roughOpen = kind == WorldCell.RoughOpen || kind == WorldCell.RoughOpenTrees;
            float rough = TvRandom.Patches((x + ox) * 0.07f + 20f, (z + oz) * 0.07f + 11f) + amount + ((cell & WorldCell.Undergrowth) != 0 ? 0.3f : 0f);
            TvShape shape;
            float s, tint;
            if (open || marsh)
            {
                if (random.Value() > 0.54f) continue;   // as many as before the tries grew (2026-11-25)
                float r = random.Value();
                // Sedge tussocks in the marsh; grass with flowers here and there and now and then a juniper in the open
                shape = marsh ? (r < 0.7f ? tuftSedge : tuftGrass) : r < 0.12f ? tuftFlowers : r < 0.125f && kind != WorldCell.Plot ? tuftJuniper : r < 0.45f ? tuftShort : tuftGrass;
                s = Rr(0.7f, 1.5f) * (marsh ? 1.25f : 1f);
                tint = Rr(0.8f, 1.15f);
                if (shape == tuftJuniper) { s = Rr(0.6f, 1.3f); tint = Rr(0.75f, 1f); }
                else if (shape == tuftShort) s *= 1.4f;
            }
            else if (roughOpen)
            {
                // A clear-cut (2026-11-25, Anders' photos): grass, much of it dry, between the slash and stumps
                // (ClearCut), some heather, bilberry and lingonberry left from the forest, now and then fireweed
                if (random.Value() > 0.5f) continue;
                float r = random.Value();
                shape = r < 0.3f ? tuftGrass : r < 0.58f ? tuftDry : r < 0.68f ? tuftShort : r < 0.76f ? tuftHeath : r < 0.84f ? tuftBerry
                      : r < 0.94f ? tuftLingon : tuftFlowers;
                s = Rr(0.6f, 1.25f);
                tint = Rr(0.6f, 0.9f);
            }
            else
            {
                bool spruceWood = look.forest == TerrainLook.Spruce;
                // (2026-11-25, Anders: the moss cushions floated, flat and without detail, like green heaps: taken out;
                // the moss is the ground itself)
                if (rough <= 0.42f || random.Value() > Sm(0.42f, 0.62f, rough)) continue;
                // Inside a carpet: thick in some spots, thinner in others, moss showing between the bushes
                // (2026-11-25, Anders' photos of bilberry)
                float thick = TvRandom.Patches((x + ox) * 0.32f + 3f, (z + oz) * 0.32f + 8f);
                if (random.Value() > 0.08f + Sm(0.3f, 0.72f, thick)) continue;
                // A spruce forest's floor is mostly bare moss: few plants, more thin grass than bilberry
                if (spruceWood && random.Value() < 0.8f) continue;
                float r = random.Value();
                // Wide, low clumps that grow together into a carpet. Plants grow in patches (2026-11-25, Anders: the
                // mix of everything everywhere looked chaotic): a slow noise says which plant a place has, and the
                // light a slower one, so a carpet of bilberry is one calm colour that only shifts from place to place
                float patch = TvRandom.Patches((x + ox) * 0.05f + 40f, (z + oz) * 0.05f + 13f);
                float light = TvRandom.Patches((x + ox) * 0.15f + 5f, (z + oz) * 0.15f + 9f);
                s = Rr(0.85f, 1.25f);
                tint = 0.8f + 0.36f * light;
                switch (look.ground)
                {
                    case TerrainLook.Heather: shape = patch < 0.6f ? (r < 0.9f ? tuftHeath : tuftLingon) : (r < 0.85f ? tuftBerry : tuftHeath); s *= 1.15f; break;
                    case TerrainLook.Grass: shape = patch < 0.7f ? tuftGrass : (r < 0.6f ? tuftFern : tuftGrass); s = Rr(0.9f, 1.8f); tint = Rr(0.85f, 1.05f); break;
                    case TerrainLook.Hard: shape = patch < 0.6f ? (r < 0.92f ? tuftBerry : tuftLingon) : (r < 0.6f ? tuftLingon : tuftDry); break;
                    case TerrainLook.Sand: shape = r < 0.6f ? tuftDry : tuftHeath; tint = Rr(0.8f, 1.05f); break;
                    case TerrainLook.Stone: shape = r < 0.8f ? tuftShort : tuftDry; tint = Rr(0.8f, 1.05f); break;
                    case TerrainLook.Lichen: shape = patch < 0.5f ? (r < 0.8f ? tuftHeath : tuftLingon) : (r < 0.7f ? tuftLingon : tuftBerry); s *= 0.8f; break;
                    // Moss forest: mostly bilberry carpets; drier patches of lingonberry, a few of heather, ferns where
                    // it is moist
                    default:
                        shape = patch < 0.56f ? (r < 0.96f ? tuftBerry : tuftLingon)
                              : patch < 0.66f ? (r < 0.65f ? tuftLingon : tuftBerry)
                              : patch < 0.69f ? (r < 0.6f ? tuftHeath : tuftLingon)
                              : (r < 0.55f ? tuftFern : r < 0.85f ? tuftBerry : tuftGrass);
                        break;
                }
                if (spruceWood && look.ground == TerrainLook.Moss && r < 0.65f) { shape = r < 0.1f ? tuftFern : tuftGrass; tint = Rr(0.7f, 0.95f); s = Rr(0.6f, 1.1f); }
                else if (spruceWood) tint *= 0.85f;   // the bilberry in a spruce forest's shade
                // Under broad-leaved trees: ferns and grass more than blueberry
                if ((look.forest == TerrainLook.Beech || look.forest == TerrainLook.Birch) && look.ground == TerrainLook.Moss && r < 0.5f)
                {
                    shape = r < 0.3f ? tuftFern : tuftGrass;
                    tint = Rr(0.75f, 1f);
                }
            }
            bool bushy = shape == tuftBerry || shape == tuftHeath;
            if (!chunk && x * x + z * z < 18f * 18f)
            {
                if (shape == tuftBerry) shape = tuftBerryNear;
                else if (shape == tuftHeath) shape = tuftHeathNear;
            }
            else if (shape == tuftBerry && berrySet != null) shape = berrySet[(int)(random.Value() * berrySet.Length) % berrySet.Length];
            else if (shape == tuftHeath && heathSet != null) shape = heathSet[(int)(random.Value() * heathSet.Length) % heathSet.Length];
            // Every bush its own shade and size (2026-11-25, Anders: each single bush clear)
            if (bushy) { tint *= Rr(0.7f, 1f); s *= Rr(0.8f, 1.3f); }
            tuftMeshes[Tile(x, z)].Add(shape, x, H(x, z) - 0.02f, z, s, s, s, Rr(0f, 6.283f), tint);
            world.tufts++;
        }
    }
}
