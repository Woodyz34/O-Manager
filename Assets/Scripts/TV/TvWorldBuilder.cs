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
}

// Something a runner cannot run through: a stone, a tree trunk, a pole
public struct TvSolid
{
    public float x, z, r;
}

public class TvWorld
{
    public float radius;
    public int size;                    // corners per side of the ground: 2 * radius + 1, one per metre
    public float[] ground;              // size * size heights, [j * size + i], i east and j north
    public readonly List<TvMesh> meshes = new List<TvMesh>();
    public readonly List<TvSolid> solids = new List<TvSolid>();
    // The control flag's stand and the punching unit on top of it
    public float flagX, flagZ, unitX, unitY, unitZ;
    public int trees, rocks, tufts;
    public long vertices, triangles;

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

public class TvWorldBuilder
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
    float[] soil;                       // bare earth (banks, ditches)
    float baseHeight;

    const int TreeTiles = 4;
    TvMesh groundMesh, rockMesh, thingMesh, wireMesh;
    TvMesh[] treeMeshes, tuftMeshes;

    public TvWorldBuilder(TvWorldInput input)
    {
        this.input = input;
        layer = input.layer;
        look = (input.look ?? new TerrainLook()).Safe();
        random = new TvRandom(input.seed * 7919 + (int)(input.centerE * 3.0) + (int)(input.centerN * 5.0) * 31);
        R = Math.Max(40, Math.Min(160, (int)input.radius));
        N = 2 * R + 1;
        cE = (float)input.centerE;
        cN = (float)input.centerN;
    }

    public static TvWorld Build(TvWorldInput input)
    {
        return new TvWorldBuilder(input).Build();
    }

    // ---- Small helpers ----

    static float Sm(float a, float b, float x) => TvShapeBuilder.Smooth(a, b, x);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    float Rr(float a, float b) => random.Range(a, b);
    bool Inside(float x, float z, float margin) => x > -R + margin && x < R - margin && z > -R + margin && z < R - margin;

    byte CellAt(float x, float z) => layer.At(input.centerE + x, input.centerN + z);
    static int Base(byte cell) => cell & WorldCell.BaseMask;

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
    readonly List<(float x, float z, float dx, float dz, float[] xz)> cliffs = new List<(float, float, float, float, float[])>();

    TvWorld Build()
    {
        world.radius = R;
        world.size = N;
        lines = NearLines(12f);
        points = NearPoints(6f);
        F = new float[N * N];
        rock = new float[N * N];
        trackBlend = new float[N * N];
        trackColor = new TvColor[N * N];
        clear = new float[N * N];
        water = new float[N * N];
        soil = new float[N * N];
        for (int i = 0; i < clear.Length; i++) clear[i] = 99f;

        groundMesh = new TvMesh { name = "Ground", castShadows = false };
        rockMesh = new TvMesh { name = "Rocks" };
        thingMesh = new TvMesh { name = "Things" };
        wireMesh = new TvMesh { name = "Wires", castShadows = false };
        treeMeshes = new TvMesh[TreeTiles * TreeTiles];
        tuftMeshes = new TvMesh[TreeTiles * TreeTiles];
        for (int i = 0; i < treeMeshes.Length; i++)
        {
            treeMeshes[i] = new TvMesh { name = "Trees " + i };
            tuftMeshes[i] = new TvMesh { name = "Undergrowth " + i, castShadows = false };
        }

        MakeShapes();
        Heights();
        Tracks();
        Ground();
        Stones();
        Structures();
        ControlFlag();
        Trees();
        Undergrowth();

        world.ground = F;
        world.meshes.Add(groundMesh);
        world.meshes.Add(rockMesh);
        world.meshes.Add(thingMesh);
        if (wireMesh.VertexCount > 0) world.meshes.Add(wireMesh);
        foreach (TvMesh m in treeMeshes) if (m.VertexCount > 0) world.meshes.Add(m);
        foreach (TvMesh m in tuftMeshes) if (m.VertexCount > 0) world.meshes.Add(m);
        foreach (TvMesh m in world.meshes)
        {
            world.vertices += m.VertexCount;
            world.triangles += m.triangles.Count / 3;
        }
        return world;
    }

    // ---- Heights: the map's heights, cliffs as steps, knolls and pits, a little roughness ----

    void Heights()
    {
        baseHeight = BaseHeight(0f, 0f);
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                float x = i - R, z = j - R;
                F[j * N + i] = BaseHeight(x, z) - baseHeight + (TvRandom.Patches(x * 0.35f + 31f, z * 0.35f + 17f) - 0.5f) * 0.22f;
            }
        }

        foreach (var p in points)
        {
            switch (p.kind)
            {
                case WorldPointKind.Knoll: Bump(p.x, p.z, 1.3f, 9f); break;
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

        foreach (var line in lines)
        {
            float[] c = line.xz;
            switch (line.kind)
            {
                case WorldLineKind.Cliff: Cliff(c, 3.2f, true); break;
                case WorldLineKind.SmallCliff: Cliff(c, 1.7f, true); break;
                case WorldLineKind.EarthBank: Cliff(c, 1.0f, false); break;
                case WorldLineKind.EarthWall: Groove(c, 0.6f, 1.6f, 0.5f, false); break;
                case WorldLineKind.Gully: Groove(c, -0.9f, 2.6f, 0.5f, false); break;
                case WorldLineKind.Ditch: Groove(c, -0.45f, 1.3f, 0.7f, false); break;
                case WorldLineKind.SmallStream: Groove(c, -0.4f, 1.5f, 0.3f, true, 0.45f); break;
                case WorldLineKind.Stream: Groove(c, -0.6f, 2.6f, 0.3f, true, 1.0f); break;
            }
        }

        // Lakes and ponds lie a little lower than their shores
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                if (Base(CellAt(i - R, j - R)) == WorldCell.Water) F[j * N + i] -= 0.45f;
            }
        }
    }

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
            if (wet) water[pair.Key] = Math.Max(water[pair.Key], 1f - Sm(waterWidth * 0.5f, waterWidth + 0.5f, d));
            clear[pair.Key] = Math.Min(clear[pair.Key], d - width * 0.5f);
        }
    }

    // A cliff is a step in the ground: the side the ground falls towards drops, and the face shows bare rock
    void Cliff(float[] c, float drop, bool rocky)
    {
        int mid = (c.Length / 4) * 2;
        float mx = c[mid], mz = c[mid + 1];
        float dx = -(BaseHeight(mx + 4f, mz) - BaseHeight(mx - 4f, mz)), dz = -(BaseHeight(mx, mz + 4f) - BaseHeight(mx, mz - 4f));
        if (dx * dx + dz * dz < 0.0004f)
        {
            // Flat ground: the lower side is to the right of the line as it was drawn
            int a = Math.Max(0, mid - 2);
            dx = c[a + 3] - c[a + 1];
            dz = -(c[a + 2] - c[a]);
        }
        float len = (float)Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-6f) return;
        dx /= len;
        dz /= len;
        if (rocky) cliffs.Add((mx, mz, dx, dz, c));

        var best = new Dictionary<int, (float d, float side)>();
        for (int q = 0; q + 3 < c.Length; q += 2)
        {
            float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
            float nx = -(bz - az), nz = bx - ax, nl = (float)Math.Sqrt(nx * nx + nz * nz);
            if (nl < 1e-6f) continue;
            nx /= nl;
            nz /= nl;
            if (nx * dx + nz * dz < 0f) { nx = -nx; nz = -nz; }
            ForNear(ax, az, bx, bz, 10f, (k, d, qx, qz) =>
            {
                if (best.TryGetValue(k, out var known) && known.d <= d) return;
                float px = k % N - R, pz = k / N - R;
                best[k] = (d, (px - qx) * nx + (pz - qz) * nz);
            });
        }
        foreach (var pair in best)
        {
            float d = pair.Value.d, side = pair.Value.side;
            if (side <= -0.5f) continue;
            F[pair.Key] -= drop * Sm(-0.5f, 0.9f, side) * (1f - Sm(1.2f, 10f, d));
            float face = Sm(-0.8f, 0.2f, side) * (1f - Sm(1.3f, 2.6f, d));
            if (rocky) rock[pair.Key] = Math.Max(rock[pair.Key], face);
            else soil[pair.Key] = Math.Max(soil[pair.Key], face * 0.8f);
        }
    }

    // ---- Paths and roads: a colour on the ground, and no trees on them ----

    static readonly TvColor Gravel = TvColor.Hex("#9a917f"), Earth = TvColor.Hex("#7b6546"), Asphalt = TvColor.Hex("#6f6e6c"),
        RideGrass = TvColor.Hex("#7d8a4a"), Ballast = TvColor.Hex("#7f7a73");

    void Tracks()
    {
        foreach (var line in lines)
        {
            float half, strength;
            TvColor color;
            switch (line.kind)
            {
                case WorldLineKind.WideRoad: half = 2.6f; strength = 1f; color = Gravel; break;
                case WorldLineKind.Road: half = 1.8f; strength = 1f; color = Gravel; break;
                case WorldLineKind.Track: half = 1.3f; strength = 0.9f; color = Earth; break;
                case WorldLineKind.Footpath: half = 0.75f; strength = 0.9f; color = Earth; break;
                case WorldLineKind.SmallPath: half = 0.5f; strength = 0.8f; color = Earth; break;
                case WorldLineKind.FaintPath: half = 0.4f; strength = 0.5f; color = Earth; break;
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
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                ForNear(c[q], c[q + 1], c[q + 2], c[q + 3], half + 3f, (k, d, qx, qz) =>
                {
                    clear[k] = Math.Min(clear[k], d - half);
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
            default: return new GroundLook { a = TvColor.Hex("#55683a"), b = TvColor.Hex("#6b7a3c"), c = TvColor.Hex("#6a5a3a") };
        }
    }

    static readonly TvColor Open = TvColor.Hex("#a39a58"), OpenGreen = TvColor.Hex("#8a9a4e"), Rough = TvColor.Hex("#8f8a55"),
        RoughGreen = TvColor.Hex("#768548"), LightGreen = TvColor.Hex("#48642f"), MidGreen = TvColor.Hex("#3a5428"), DarkGreen = TvColor.Hex("#2f4722"),
        Field = TvColor.Hex("#b8a85a"), Field2 = TvColor.Hex("#a39a50"), Water = TvColor.Hex("#4c6a7c"), WaterDeep = TvColor.Hex("#3c5566"),
        Rock = TvColor.Hex("#8c8a84"), RockDark = TvColor.Hex("#6d6c69"), LichenRock = TvColor.Hex("#b9bba6"), SandColor = TvColor.Hex("#c2ad7c"),
        Paved = TvColor.Hex("#8a8782"), Plot = TvColor.Hex("#8f9a4e"), MarshColor = TvColor.Hex("#6f7440"), Wet = TvColor.Hex("#4a4a2e"),
        Soil = TvColor.Hex("#6b563c");

    void Ground()
    {
        GroundLook floor = ForestFloor();
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                int k = j * N + i;
                float x = i - R, z = j - R;
                // The 2 m cells' edges are made ragged, so the ground does not show squares
                byte cell = CellAt(x + (TvRandom.Noise(x * 0.4f, z * 0.4f) - 0.5f) * 3f, z + (TvRandom.Noise(x * 0.4f + 9f, z * 0.4f) - 0.5f) * 3f);
                int kind = Base(cell);
                float f = TvRandom.Patches(x * 0.12f, z * 0.12f), f2 = TvRandom.Noise(x * 0.9f, z * 0.9f);
                TvColor c;
                bool hard = false;
                switch (kind)
                {
                    case WorldCell.Open: case WorldCell.OpenTrees: c = Open.Lerp(OpenGreen, f); break;
                    case WorldCell.RoughOpen: case WorldCell.RoughOpenTrees: c = Rough.Lerp(RoughGreen, f); break;
                    case WorldCell.LightGreen: c = LightGreen.Lerp(floor.a, f * 0.5f); break;
                    case WorldCell.Green: c = MidGreen; break;
                    case WorldCell.DarkGreen: c = DarkGreen; break;
                    case WorldCell.Cultivated: c = Field.Lerp(Field2, Sm(0.4f, 0.6f, TvRandom.Noise(x * 0.05f, z * 0.6f))); break;
                    case WorldCell.Water: c = Water.Lerp(WaterDeep, f); hard = true; break;
                    case WorldCell.BareRock: c = Rock.Lerp(LichenRock, f2 * 0.6f); hard = true; break;
                    case WorldCell.Sand: c = SandColor.Shade(0.94f + f2 * 0.1f); break;
                    case WorldCell.Paved: case WorldCell.Building: c = Paved.Shade(0.95f + f2 * 0.08f); hard = true; break;
                    case WorldCell.Plot: c = Plot.Lerp(OpenGreen, f); break;
                    default:
                        c = floor.a.Lerp(floor.b, Sm(0.35f, 0.7f, f)).Lerp(floor.c, Sm(0.55f, 0.8f, TvRandom.Noise(x * 0.2f + 40f, z * 0.2f)) * 0.7f);
                        break;
                }
                if (!hard)
                {
                    if ((cell & WorldCell.Marsh) != 0) c = c.Lerp(MarshColor, 0.85f).Lerp(Wet, Sm(0.62f, 0.8f, f2) * 0.8f);
                    if ((cell & (WorldCell.Stony | WorldCell.Boulders)) != 0) c = c.Lerp(Rock, 0.25f + f2 * 0.3f);
                    if (soil[k] > 0f) c = c.Lerp(Soil, soil[k]);
                    if (trackBlend[k] > 0f) c = c.Lerp(trackColor[k], trackBlend[k]);
                    if (water[k] > 0f) c = c.Lerp(WaterDeep, water[k]);
                }
                // Steep ground and cliff faces show the rock
                float sx = F[k + (i < N - 1 ? 1 : 0)] - F[k - (i > 0 ? 1 : 0)], sz = F[k + (j < N - 1 ? N : 0)] - F[k - (j > 0 ? N : 0)];
                float slope = (float)Math.Sqrt(sx * sx + sz * sz) / 2f;
                if (kind != WorldCell.Water) c = c.Lerp(RockDark, Math.Max(rock[k] * 0.95f, Sm(0.75f, 1.3f, slope)));
                c = c.Shade(0.86f + f2 * 0.24f);

                float nx = -sx / 2f, nz = -sz / 2f, nl = (float)Math.Sqrt(nx * nx + 1f + nz * nz);
                groundMesh.Vertex(x, F[k], z, nx / nl, 1f / nl, nz / nl, c);
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

    float H(float x, float z)
    {
        float fx = x + R, fz = z + R;
        fx = fx < 0f ? 0f : fx > N - 1.001f ? N - 1.001f : fx;
        fz = fz < 0f ? 0f : fz > N - 1.001f ? N - 1.001f : fz;
        int i = (int)fx, j = (int)fz;
        float u = fx - i, v = fz - j;
        float a = F[j * N + i], b = F[j * N + i + 1], c = F[(j + 1) * N + i], d = F[(j + 1) * N + i + 1];
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    // ---- The low-poly shapes everything is put together from ----

    TvShape rockA, rockB, logShape, bushShape, bigTree, poolShape;
    TvShape[] pineNear, pineFar, pine2Near, pine2Far, spruceShape, birchNear, birchFar, beechNear, beechFar;
    TvShape tuftBerry, tuftHeath, tuftGrass, tuftDry, tuftShort, tuftFern;

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
        // Two of each, so a forest is not one tree copied
        pineNear = new[] { Pine(true), Pine(true) };
        pineFar = new[] { Pine(false), Pine(false) };
        pine2Near = new[] { Pine2(true), Pine2(true) };
        pine2Far = new[] { Pine2(false), Pine2(false) };
        spruceShape = new[] { Spruce(), Spruce() };
        birchNear = new[] { Birch(true), Birch(true) };
        birchFar = new[] { Birch(false), Birch(false) };
        beechNear = new[] { Beech(true), Beech(true) };
        beechFar = new[] { Beech(false), Beech(false) };

        var s = new TvShapeBuilder(random);
        s.Blob(1f, 1f, 0.72f, 1f, 0f, 0.42f, 0f, 0.42f, 1, true, TvColor.Hex("#85837d"), TvColor.Hex("#7e8a5c"), 0.55f, 1.1f);
        rockA = s.Done();
        s = new TvShapeBuilder(random);
        s.Blob(1f, 1.2f, 0.6f, 0.85f, 0f, 0.32f, 0f, 0.5f, 0, true, TvColor.Hex("#797874"), TvColor.Hex("#8d8f7c"), 0.4f, 0.9f);
        rockB = s.Done();

        s = new TvShapeBuilder(random);
        s.Blob(0.34f, 1f, 0.5f, 1f, 0f, 0.1f, 0f, 0.5f, 0, false, TvColor.Hex("#3d5a2a"));
        s.Blob(0.24f, 1f, 0.55f, 1f, 0.3f, 0.08f, 0.16f, 0.5f, 0, false, TvColor.Hex("#4b6a2d"));
        s.Blob(0.22f, 1f, 0.5f, 1f, -0.2f, 0.07f, 0.28f, 0.5f, 0, false, TvColor.Hex("#5a4a2c"));
        tuftBerry = s.Done();
        s = new TvShapeBuilder(random);
        s.Blob(0.3f, 1f, 0.55f, 1f, 0f, 0.1f, 0f, 0.5f, 0, false, TvColor.Hex("#5c5a34"));
        s.Blob(0.2f, 1f, 0.6f, 1f, 0.25f, 0.08f, -0.12f, 0.5f, 0, false, TvColor.Hex("#7a4a62"));
        tuftHeath = s.Done();
        s = new TvShapeBuilder(random);
        s.Cone(0.16f, 0.55f, 0f, 5, TvColor.Hex("#a7a35a"));
        s.Cone(0.12f, 0.42f, 0f, 5, TvColor.Hex("#8f9a4c"), 0.17f, 0.08f);
        s.Cone(0.12f, 0.48f, 0f, 5, TvColor.Hex("#b4a860"), -0.12f, 0.16f);
        tuftGrass = s.Done();
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

        s = new TvShapeBuilder(random);
        s.Blob(0.9f, 1f, 0.8f, 1f, 0f, 0.6f, 0f, 0.4f, 1, true, TvColor.Hex("#476a2c"));
        s.Blob(0.6f, 1f, 0.8f, 1f, 0.6f, 0.45f, 0.3f, 0.4f, 0, true, TvColor.Hex("#557a33"));
        bushShape = s.Done();

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
    }

    // ---- Stones, boulder clusters, stony ground, cairns: exactly where the map has them ----

    void AddRock(float x, float z, float s, float sink, bool solid, float tint = -1f)
    {
        if (!Inside(x, z, 1.5f)) return;
        rockMesh.Add(random.Value() < 0.5f ? rockA : rockB, x, H(x, z) - sink * s, z, s, s * Rr(0.85f, 1.15f), s, Rr(0f, 6.283f), tint < 0f ? Rr(0.88f, 1.1f) : tint);
        world.rocks++;
        if (solid) world.solids.Add(new TvSolid { x = x, z = z, r = s * 0.95f });
    }

    void Stones()
    {
        foreach (var p in points)
        {
            float x = p.x, z = p.z;
            switch (p.kind)
            {
                case WorldPointKind.Boulder: AddRock(x, z, Rr(1.05f, 1.5f), 0.1f, true); break;
                case WorldPointKind.LargeBoulder: AddRock(x, z, Rr(2.1f, 2.7f), 0.1f, true); break;
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
                        if (Inside(rx, rz, 1.5f)) rockMesh.Add(rockA, rx, H(rx, rz) + i * 0.07f - 0.05f, rz, s, s, s, Rr(0f, 6.283f), Rr(0.9f, 1.05f));
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
        for (float gz = -R + 2f; gz < R - 2f; gz += 2f)
        {
            for (float gx = -R + 2f; gx < R - 2f; gx += 2f)
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
                for (float t = 0f; t < len; t += 1.3f)
                {
                    float f = t / len, d = Rr(1.4f, 3.4f);
                    AddRock(Lerp(c[q], c[q + 2], f) + cliff.dx * d, Lerp(c[q + 1], c[q + 3], f) + cliff.dz * d, Rr(0.2f, 0.6f), 0.08f, false, Rr(0.8f, 1f));
                }
            }
        }
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

    void Structures()
    {
        TvColor stone = TvColor.Hex("#8a8880"), wood = TvColor.Hex("#6a5a48"), pole = TvColor.Hex("#4e4033"), wire = TvColor.Hex("#2b2b2b"),
            steel = TvColor.Hex("#8c9094"), house = TvColor.Hex("#b4472f"), roof = TvColor.Hex("#3f3b38");
        TvShape wallPiece = Box(1f, 1f, 0.55f, stone);

        foreach (var line in lines)
        {
            float[] c = line.xz;
            switch (line.kind)
            {
                case WorldLineKind.Wall:
                case WorldLineKind.RuinedWall:
                case WorldLineKind.HighWall:
                {
                    bool ruined = line.kind == WorldLineKind.RuinedWall;
                    float height = line.kind == WorldLineKind.HighWall ? 1.9f : ruined ? 0.45f : 0.85f;
                    Along(c, 1.2f, (x, z, yaw, len) =>
                    {
                        if (ruined && random.Value() < 0.3f) return;
                        // Unity turns clockwise round y for a positive angle seen from above; the mesh copy turns the same way
                        thingMesh.Add(wallPiece, x, H(x, z) - 0.1f, z, len + 0.05f, height * Rr(0.85f, 1.1f), 1f, yaw, Rr(0.9f, 1.08f));
                        world.solids.Add(new TvSolid { x = x, z = z, r = 0.55f });
                    });
                    break;
                }
                case WorldLineKind.Fence:
                case WorldLineKind.RuinedFence:
                case WorldLineKind.HighFence:
                {
                    bool high = line.kind == WorldLineKind.HighFence, ruined = line.kind == WorldLineKind.RuinedFence;
                    float height = high ? 2f : 1.05f;
                    float lastX = float.NaN, lastY = 0f, lastZ = 0f;
                    Along(c, 2.5f, (x, z, yaw, len) =>
                    {
                        float y = H(x, z);
                        Bar(thingMesh, x, y - 0.1f, z, x, y + height, z, 0.045f, 0.04f, 4, high ? steel : wood);
                        if (!float.IsNaN(lastX) && !ruined && (x - lastX) * (x - lastX) + (z - lastZ) * (z - lastZ) < 16f)
                        {
                            for (int rail = 1; rail <= (high ? 3 : 2); rail++)
                            {
                                float up = height * rail / (high ? 3f : 2f) - 0.08f;
                                Bar(wireMesh, lastX, lastY + up, lastZ, x, y + up, z, 0.015f, 0.015f, 3, high ? steel : wood);
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
                        if (!Inside(ax, az, 2f) || !Inside(bx, bz, 2f)) continue;
                        Bar(thingMesh, ax, H(ax, az) + 0.2f, az, bx, H(bx, bz) + 0.2f, bz, 0.19f, 0.13f, 6, TvColor.Hex("#5e5346"));
                        float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                        for (float t = 0.3f; t < len; t += 0.6f)
                            world.solids.Add(new TvSolid { x = Lerp(ax, bx, t / len), z = Lerp(az, bz, t / len), r = 0.3f });
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
                    // A hunting tower: four legs and a box on top
                    float top = p.kind == WorldPointKind.Tower ? 5f : 2.6f, w = 0.75f;
                    foreach (float sx in new[] { -1f, 1f })
                        foreach (float sz in new[] { -1f, 1f })
                            Bar(thingMesh, x + sx * w * 1.2f, y - 0.2f, z + sz * w * 1.2f, x + sx * w, y + top, z + sz * w, 0.06f, 0.05f, 4, wood);
                    thingMesh.Add(Box(w * 2.2f, 1.3f, w * 2.2f, wood), x, y + top, z, 1f, 1f, 1f, Rr(0f, 0.2f));
                    thingMesh.Add(Box(w * 2.6f, 0.1f, w * 2.6f, roof), x, y + top + 1.5f, z, 1f, 1f, 1f, 0f);
                    world.solids.Add(new TvSolid { x = x, z = z, r = 1.3f });
                    break;
                }
                case WorldPointKind.FodderRack:
                    foreach (float sx in new[] { -1f, 1f })
                        Bar(thingMesh, x + sx * 0.6f, y - 0.1f, z, x + sx * 0.6f, y + 1.3f, z, 0.05f, 0.05f, 4, wood);
                    thingMesh.Add(Box(1.5f, 0.5f, 0.7f, wood), x, y + 0.7f, z, 1f, 1f, 1f, 0f);
                    thingMesh.Add(Box(1.8f, 0.08f, 1.1f, roof), x, y + 1.35f, z, 1f, 1f, 1f, 0f);
                    world.solids.Add(new TvSolid { x = x, z = z, r = 1f });
                    break;
                case WorldPointKind.BigTree:
                    treeMeshes[Tile(x, z)].Add(bigTree, x, y - 0.15f, z, 1f, Rr(0.95f, 1.15f), 1f, Rr(0f, 6.283f), Rr(0.9f, 1.08f));
                    world.solids.Add(new TvSolid { x = x, z = z, r = 0.75f });
                    world.trees++;
                    break;
                case WorldPointKind.Bush:
                {
                    float s = Rr(0.9f, 1.4f);
                    treeMeshes[Tile(x, z)].Add(bushShape, x, y - 0.1f, z, s, s, s, Rr(0f, 6.283f), Rr(0.85f, 1.1f));
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
                case WorldPointKind.RootStock:
                {
                    // The root plate of a fallen tree, standing on edge
                    float yaw = Rr(0f, 6.283f);
                    thingMesh.Add(rockA, x, y + 0.2f, z, 0.35f, 1.5f, 1.3f, yaw, 0.55f);
                    world.solids.Add(new TvSolid { x = x, z = z, r = 0.9f });
                    break;
                }
            }
        }

        // Buildings: every 2 m cell of one becomes a block, so a house has the outline the map gives it
        TvShape block = Box(2.02f, 3f, 2.02f, house), lid = Box(2.3f, 0.25f, 2.3f, roof);
        double e0 = Math.Floor((input.centerE - R - layer.minE) / layer.cell), n0 = Math.Floor((input.centerN - R - layer.minN) / layer.cell);
        int cells = (int)(2 * R / layer.cell) + 2;
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                double e = layer.minE + (e0 + i + 0.5) * layer.cell, n = layer.minN + (n0 + j + 0.5) * layer.cell;
                if (Base(layer.At(e, n)) != WorldCell.Building) continue;
                float x = (float)(e - input.centerE), z = (float)(n - input.centerN);
                if (!Inside(x, z, 2f)) continue;
                float y = H(x, z);
                thingMesh.Add(block, x, y - 0.6f, z, layer.cell / 2f, 1.2f, layer.cell / 2f, 0f);
                thingMesh.Add(lid, x, y + 3f, z, layer.cell / 2f, 1f, layer.cell / 2f, 0f);
                world.solids.Add(new TvSolid { x = x, z = z, r = layer.cell * 0.75f });
            }
        }
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
        thingMesh.Add(b.Done(), fx, y, fz, 1f, 1f, 1f, Rr(0f, 6.283f));
    }

    // ---- Trees: the kind follows the terrain look, how dense they stand follows the map ----

    int Tile(float x, float z)
    {
        int i = Math.Max(0, Math.Min(TreeTiles - 1, (int)((x + R) / (2f * R) * TreeTiles)));
        int j = Math.Max(0, Math.Min(TreeTiles - 1, (int)((z + R) / (2f * R) * TreeTiles)));
        return j * TreeTiles + i;
    }

    bool Blocked(float x, float z, float gap)
    {
        if (x * x + z * z < 9f) return true;                                  // the control stands free
        float dx = x - world.flagX, dz = z - world.flagZ;
        if (dx * dx + dz * dz < 6f) return true;
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
        for (float gz = -R + 3f; gz < R - 3f; gz += Step)
        {
            for (float gx = -R + 3f; gx < R - 3f; gx += Step)
            {
                float x = gx + Rr(0f, Step), z = gz + Rr(0f, Step);
                byte cell = CellAt(x, z);
                int kind = Base(cell);
                bool marsh = (cell & WorldCell.Marsh) != 0;
                float clump = TvRandom.Patches(x * 0.05f + 7f, z * 0.05f + 3f), p, s;
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
                    case WorldCell.DarkGreen: p = 0.95f; tree = ThicketTree(true); s = Rr(0.18f, 0.4f); break;
                    case WorldCell.OpenTrees:
                    case WorldCell.RoughOpenTrees:
                        p = 0.035f;
                        tree = beech ? Tree.Birch : random.Value() < 0.5f ? Tree.Birch : Tree.Pine2;
                        s = Rr(0.4f, 0.75f);
                        break;
                    case WorldCell.Open:
                    case WorldCell.RoughOpen:
                        p = kind == WorldCell.RoughOpen ? 0.008f : 0.004f;
                        tree = random.Value() < 0.5f ? Tree.Birch : Tree.Pine2;
                        s = Rr(0.35f, 0.7f);
                        break;
                    case WorldCell.Plot: p = 0.012f; tree = Tree.Birch; s = Rr(0.5f, 0.9f); break;
                    case WorldCell.BareRock: p = 0.012f; tree = Tree.Pine2; s = Rr(0.3f, 0.5f); break;
                    default: continue;
                }
                if (random.Value() > p || Blocked(x, z, 1.3f)) continue;

                bool near = x * x + z * z < 45f * 45f;
                int pick = random.Value() < 0.5f ? 0 : 1;
                TvShape shape;
                float trunk;
                switch (tree)
                {
                    case Tree.Pine: shape = (near ? pineNear : pineFar)[pick]; trunk = 0.3f; break;
                    case Tree.Pine2: shape = (near ? pine2Near : pine2Far)[pick]; trunk = 0.28f; break;
                    case Tree.Spruce: shape = spruceShape[pick]; trunk = 0.45f; break;   // the low branches are in the way too
                    case Tree.Birch: shape = (near ? birchNear : birchFar)[pick]; trunk = 0.22f; break;
                    default: shape = (near ? beechNear : beechFar)[pick]; trunk = 0.42f; break;
                }
                treeMeshes[Tile(x, z)].Add(shape, x, H(x, z) - 0.15f, z, s, s * Rr(0.9f, 1.12f), s, Rr(0f, 6.283f), Rr(0.82f, 1.12f), Rr(-0.03f, 0.03f), Rr(-0.03f, 0.03f));
                world.trees++;
                world.solids.Add(new TvSolid { x = x, z = z, r = Math.Max(0.18f, trunk * (0.5f + s * 0.5f)) });
            }
        }
    }

    // ---- Undergrowth: what covers the ground between the trees ----

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
        int tries = (int)(26000f * (0.6f + look.undergrowth * 0.8f));
        for (int i = 0; i < tries; i++)
        {
            float a = Rr(0f, 6.283f), d = (float)Math.Sqrt(random.Value()) * Reach;
            float x = (float)Math.Cos(a) * d, z = (float)Math.Sin(a) * d;
            if (!Inside(x, z, 2f)) continue;
            byte cell = CellAt(x, z);
            int kind = Base(cell);
            if (kind == WorldCell.Water || kind == WorldCell.BareRock || kind == WorldCell.Paved || kind == WorldCell.Building
                || kind == WorldCell.Sand || kind == WorldCell.Cultivated) continue;
            if (ClearAt(x, z) < 0.3f) continue;
            bool marsh = (cell & WorldCell.Marsh) != 0, open = kind == WorldCell.Open || kind == WorldCell.OpenTrees || kind == WorldCell.Plot;
            bool roughOpen = kind == WorldCell.RoughOpen || kind == WorldCell.RoughOpenTrees;
            float rough = TvRandom.Patches(x * 0.07f + 20f, z * 0.07f + 11f) + amount + ((cell & WorldCell.Undergrowth) != 0 ? 0.3f : 0f);
            TvShape shape;
            float s, tint;
            if (open || marsh)
            {
                if (random.Value() > 0.75f) continue;
                shape = tuftGrass;
                s = Rr(0.7f, 1.5f) * (marsh ? 1.25f : 1f);
                tint = Rr(0.8f, 1.15f);
            }
            else if (roughOpen)
            {
                if (random.Value() > 0.8f) continue;
                shape = random.Value() < 0.6f ? tuftGrass : tuftHeath;
                s = Rr(0.8f, 1.6f);
                tint = Rr(0.75f, 1.1f);
            }
            else
            {
                if (rough <= 0.5f || random.Value() > Sm(0.5f, 0.68f, rough)) continue;
                float r = random.Value();
                s = Rr(0.4f, 0.95f);
                tint = Rr(0.55f, 0.85f);
                switch (look.ground)
                {
                    case TerrainLook.Heather: shape = r < 0.8f ? tuftHeath : tuftBerry; s *= 1.15f; tint = Rr(0.7f, 1f); break;
                    case TerrainLook.Grass: shape = r < 0.85f ? tuftGrass : tuftFern; s = Rr(0.9f, 1.8f); tint = Rr(0.8f, 1.1f); break;
                    case TerrainLook.Hard: shape = r < 0.6f ? tuftBerry : tuftDry; break;
                    case TerrainLook.Sand: shape = r < 0.6f ? tuftDry : tuftHeath; tint = Rr(0.8f, 1.05f); break;
                    case TerrainLook.Stone: shape = r < 0.8f ? tuftShort : tuftDry; tint = Rr(0.8f, 1.05f); break;
                    case TerrainLook.Lichen: shape = r < 0.6f ? tuftHeath : tuftBerry; s *= 0.8f; tint = Rr(0.7f, 0.95f); break;
                    default: shape = r < 0.8f ? tuftBerry : r < 0.93f ? tuftHeath : tuftFern; break;
                }
                // Under broad-leaved trees: ferns and grass more than blueberry
                if ((look.forest == TerrainLook.Beech || look.forest == TerrainLook.Birch) && look.ground == TerrainLook.Moss && r < 0.5f)
                {
                    shape = r < 0.3f ? tuftFern : tuftGrass;
                    tint = Rr(0.75f, 1f);
                }
            }
            tuftMeshes[Tile(x, z)].Add(shape, x, H(x, z) - 0.02f, z, s, s, s, Rr(0f, 6.283f), tint);
            world.tufts++;
        }
    }
}
