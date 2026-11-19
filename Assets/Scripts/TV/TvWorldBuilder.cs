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
    // Fallen trunks, stone walls and fences a runner jumps over rather than going round: ax, az, bx, bz, height for each
    public readonly List<float> logs = new List<float>();

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
    float[] bog;                        // how wet a narrow marsh strip makes the ground
    float[] soil;                       // bare earth (banks, ditches)
    float baseHeight;

    const int TreeTiles = 4;
    // Tiles the finished world is cut into for drawing: 10 x 10 of 22 m
    const int DrawTiles = 10;
    TvMesh groundMesh, rockMesh, thingMesh, wireMesh;
    TvMesh[] treeMeshes, tuftMeshes;
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
        bog = new float[N * N];
        soil = new float[N * N];
        for (int i = 0; i < clear.Length; i++) clear[i] = 99f;

        groundMesh = new TvMesh { name = "Ground", castShadows = false };
        rockMesh = new TvMesh { name = "Rocks" };
        thingMesh = new TvMesh { name = "Things" };
        wireMesh = new TvMesh { name = "Wires", castShadows = false };
        waterMesh = new TvMesh { name = "Water", castShadows = false };
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
        ControlFlag();
        CameraSpot();
        Trees();
        Undergrowth();

        world.ground = F;
        // Everything in small tiles, so only what the TV camera looks at is drawn (see TvMesh.SplitIntoTiles)
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { groundMesh }, "Ground", false, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { rockMesh }, "Rocks", true, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { thingMesh }, "Things", true, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { waterMesh }, "Water", false, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh> { wireMesh }, "Wires", false, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh>(treeMeshes), "Trees", true, R, DrawTiles));
        world.meshes.AddRange(TvMesh.SplitIntoTiles(new List<TvMesh>(tuftMeshes), "Undergrowth", false, R, DrawTiles));
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
        F = new float[N * N];
        rock = new float[N * N];
        trackBlend = new float[N * N];
        trackColor = new TvColor[N * N];
        clear = new float[N * N];
        water = new float[N * N];
        bog = new float[N * N];
        soil = new float[N * N];
        for (int i = 0; i < clear.Length; i++) clear[i] = 99f;
        groundMesh = new TvMesh { name = "Ground", castShadows = false, group = TvMesh.Near };
        rockMesh = new TvMesh { name = "Rocks" };
        thingMesh = new TvMesh { name = "Things" };
        wireMesh = new TvMesh { name = "Wires", castShadows = false };
        waterMesh = new TvMesh { name = "Water", castShadows = false };
        treeMeshes = new[] { new TvMesh { name = "Trees", group = TvMesh.Near } };
        treeFarMesh = new TvMesh { name = "Trees far", castShadows = false, group = TvMesh.Far };
        tuftMeshes = new[] { new TvMesh { name = "Undergrowth", castShadows = false } };
        thicketMesh = new TvMesh { name = "Thickets" };

        MakeShapes();
        Heights();
        Tracks();
        world.ground = F;
        if (input.chunkDetail)
        {
            if (input.baseSolids != null) world.solids.AddRange(input.baseSolids);
            Thickets();
            Undergrowth();
            Keep(tuftMeshes[0]);
            Keep(thicketMesh);
            return;
        }
        Ground();
        Waters();
        Stones();
        Structures();
        Flags();
        Trees();
        Keep(groundMesh);
        Keep(GroundFar());
        Keep(waterMesh);
        Keep(rockMesh);
        Keep(thingMesh);
        Keep(wireMesh);
        Keep(treeMeshes[0]);
        Keep(treeFarMesh);
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
                case WorldLineKind.NarrowMarsh:
                    // A narrow marsh: a shallow wet strip with pools here and there
                    Groove(c, -0.12f, 1.6f, 0f, false);
                    for (int q = 0; q + 3 < c.Length; q += 2)
                        ForNear(c[q], c[q + 1], c[q + 2], c[q + 3], 2.5f, (k, d, qx, qz) => bog[k] = Math.Max(bog[k], 1f - Sm(0.6f, 1.8f, d)));
                    break;
            }
        }

        // Lakes and ponds lie a little lower than their shores
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                byte cell = CellAt(i - R, j - R);
                if (Base(cell) == WorldCell.Water) F[j * N + i] -= 0.45f;
                // Pools in a marsh are hollows with water in them
                else if (MarshPool(i - R - 0.5f, j - R - 0.5f, (cell & WorldCell.Marsh) != 0, bog[j * N + i])) F[j * N + i] -= 0.3f;
            }
        }
    }

    // The map's heights with a little roughness, before knolls, cliffs and ditches
    float Plain(float x, float z) => BaseHeight(x, z) - baseHeight + (TvRandom.Patches((x + ox) * 0.35f + 31f, (z + oz) * 0.35f + 17f) - 0.5f) * 0.22f;

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
            // A rock cliff drops at once (a vertical face is built on it, CliffFaces); an earth bank slopes
            F[pair.Key] -= drop * (rocky ? Sm(-0.25f, 0.45f, side) : Sm(-0.5f, 0.9f, side)) * (1f - Sm(1.2f, 10f, d));
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
                float ax = x + ox, az = z + oz;
                byte cell = CellAt(x + (TvRandom.Noise(ax * 0.4f, az * 0.4f) - 0.5f) * 3f, z + (TvRandom.Noise(ax * 0.4f + 9f, az * 0.4f) - 0.5f) * 3f);
                int kind = Base(cell);
                float f = TvRandom.Patches(ax * 0.12f, az * 0.12f), f2 = TvRandom.Noise(ax * 0.9f, az * 0.9f);
                TvColor c;
                bool hard = false;
                switch (kind)
                {
                    case WorldCell.Open: case WorldCell.OpenTrees: c = Open.Lerp(OpenGreen, f); break;
                    case WorldCell.RoughOpen: case WorldCell.RoughOpenTrees: c = Rough.Lerp(RoughGreen, f); break;
                    case WorldCell.LightGreen: c = LightGreen.Lerp(floor.a, f * 0.5f); break;
                    case WorldCell.Green: c = MidGreen; break;
                    case WorldCell.DarkGreen: c = DarkGreen; break;
                    case WorldCell.Cultivated: c = Field.Lerp(Field2, Sm(0.4f, 0.6f, TvRandom.Noise(ax * 0.05f, az * 0.6f))); break;
                    case WorldCell.Water: c = Water.Lerp(WaterDeep, f); hard = true; break;
                    case WorldCell.BareRock: c = Rock.Lerp(LichenRock, f2 * 0.6f); hard = true; break;
                    case WorldCell.Sand: c = SandColor.Shade(0.94f + f2 * 0.1f); break;
                    case WorldCell.Paved: case WorldCell.Building: c = Paved.Shade(0.95f + f2 * 0.08f); hard = true; break;
                    case WorldCell.Plot: c = Plot.Lerp(OpenGreen, f); break;
                    default:
                        c = floor.a.Lerp(floor.b, Sm(0.35f, 0.7f, f)).Lerp(floor.c, Sm(0.55f, 0.8f, TvRandom.Noise(ax * 0.2f + 40f, az * 0.2f)) * 0.7f);
                        break;
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
                // Steep ground and cliff faces show the rock
                float sx = F[k + (i < N - 1 ? 1 : 0)] - F[k - (i > 0 ? 1 : 0)], sz = F[k + (j < N - 1 ? N : 0)] - F[k - (j > 0 ? N : 0)];
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
                if (Base(cell) == WorldCell.Water) { kind[a] = 1; continue; }
                float wet = Math.Max(Math.Max(water[a], water[a + 1]), Math.Max(water[a + N], water[a + N + 1]));
                if (wet > 0.55f) { kind[a] = 2; continue; }
                float strip = Math.Max(Math.Max(bog[a], bog[a + 1]), Math.Max(bog[a + N], bog[a + N + 1]));
                if (MarshPool(x, z, (cell & WorldCell.Marsh) != 0 && Base(cell) != WorldCell.Building, strip)) kind[a] = 3;
            }
        }
        // One level for every corner: the lowest ground round it, so the surface is smooth and lies below the banks
        var level = new float[N * N];
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
                        lo = Math.Min(lo, F[jj * N + ii]);
                    }
                level[j * N + i] = lo;
            }
        }
        var corner = new int[N * N];
        for (int i = 0; i < corner.Length; i++) corner[i] = -1;
        for (int j = 0; j < N - 1; j++)
        {
            for (int i = 0; i < N - 1; i++)
            {
                int a = j * N + i;
                byte k = kind[a];
                if (k == 0) continue;
                float lift = k == 1 ? 0.3f : k == 2 ? 0.12f : 0.12f;
                int V(int ci, int cj)
                {
                    int q = cj * N + ci;
                    if (corner[q] >= 0) return corner[q];
                    float x = ci - R, z = cj - R;
                    // The sky shows in the water in broad streaks; marsh pools are dark and brown
                    float glint = TvRandom.Noise((x + ox) * 0.12f + 3f, (z + oz) * 0.05f);
                    TvColor c = k == 3 ? bogC.Shade(0.9f + 0.2f * glint) : deep.Lerp(sky, 0.15f + 0.45f * glint);
                    corner[q] = waterMesh.Vertex(x, level[q] + lift, z, 0f, 1f, 0f, c);
                    return corner[q];
                }
                int v0 = V(i, j), v1 = V(i + 1, j), v2 = V(i, j + 1), v3 = V(i + 1, j + 1);
                waterMesh.triangles.Add(v0); waterMesh.triangles.Add(v2); waterMesh.triangles.Add(v1);
                waterMesh.triangles.Add(v1); waterMesh.triangles.Add(v2); waterMesh.triangles.Add(v3);
            }
        }
    }

    // Open water in a marsh or a narrow marsh strip: where the noise is high
    bool MarshPool(float x, float z, bool marsh, float strip)
    {
        x += ox;
        z += oz;
        float n = TvRandom.Patches(x * 0.13f + 11f, z * 0.13f - 4f) + TvRandom.Noise(x * 0.6f, z * 0.6f) * 0.1f;
        return (marsh && n > 0.6f) || (strip > 0.55f && TvRandom.Patches(x * 0.3f + 11f, z * 0.3f - 4f) > 0.38f);
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
        TvColor granite = TvColor.Hex("#75736d"), granite2 = TvColor.Hex("#6c6b68"), mossG = TvColor.Hex("#667a3a");
        TvShape RockShape(float sx, float sy, float sz, float oy, int detail, int cuts, TvColor c)
        {
            var b = new TvShapeBuilder(random);
            b.Rock(sx, sy, sz, oy, detail, cuts, 0.3f, c, mossG, mossy, 0.12f);
            return b.Done();
        }
        rocks = new[]
        {
            RockShape(1f, 0.75f, 1f, 0.3f, 1, 7, granite), RockShape(1.15f, 0.6f, 0.85f, 0.24f, 1, 8, granite2),
            RockShape(0.9f, 0.85f, 1f, 0.33f, 1, 6, granite2), RockShape(1.2f, 0.5f, 0.95f, 0.2f, 1, 8, granite)
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
    }

    // ---- Stones, boulder clusters, stony ground, cairns: exactly where the map has them ----

    void AddRock(float x, float z, float s, float sink, bool solid, float tint = -1f)
    {
        if (!Inside(x, z, 1.5f)) return;
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
                for (float t = 0f; t < len; t += 1.3f)
                {
                    float f = t / len, d = Rr(1.4f, 3.4f);
                    AddRock(Lerp(c[q], c[q + 2], f) + cliff.dx * d, Lerp(c[q + 1], c[q + 3], f) + cliff.dz * d, Rr(0.2f, 0.6f), 0.08f, false, Rr(0.8f, 1f));
                }
            }
        }
        CliffFaces();
    }

    // A rock cliff stands straight up (2026-11-19, Anders: a cliff is a wall, not a slope): a rough vertical face along
    // the line, from the ground on the high side down to the foot on the low side, jagged a little along its length
    void CliffFaces()
    {
        foreach (var cliff in cliffs)
        {
            float[] c = cliff.xz;
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
                float sx = bx - ax, sz = bz - az, len = (float)Math.Sqrt(sx * sx + sz * sz);
                if (len < 0.05f) continue;
                if (!Inside((ax + bx) / 2f, (az + bz) / 2f, chunk ? 0f : 2f)) continue;
                // The low side, as the drop was made
                float nx = -sz / len, nz = sx / len;
                if (nx * cliff.dx + nz * cliff.dz < 0f) { nx = -nx; nz = -nz; }
                int steps = Math.Max(1, (int)Math.Ceiling(len / 0.7f));
                int first = -1;
                for (int k = 0; k <= steps; k++)
                {
                    float f = k / (float)steps, px = ax + sx * f, pz = az + sz * f;
                    float jag = (TvRandom.Noise((px + ox) * 1.7f, (pz + oz) * 1.7f) - 0.5f) * 0.35f;
                    float fx = px + nx * (0.15f + jag), fz = pz + nz * (0.15f + jag);
                    float top = H(px - nx * 0.7f, pz - nz * 0.7f) + 0.1f, foot = H(px + nx * 1.1f, pz + nz * 1.1f) - 0.35f;
                    if (top < foot + 0.5f) top = foot + 0.5f;
                    float lean = (TvRandom.Noise((px + ox) * 0.9f + 7f, (pz + oz) * 0.9f) - 0.5f) * 0.5f;
                    TvColor face = (TvRandom.Noise((px + ox) * 0.5f, (pz + oz) * 0.5f + 3f) > 0.5f) ? Rock : RockDark;
                    int v0 = rockMesh.Vertex(fx - nx * 0.12f, top, fz - nz * 0.12f, nx + lean * nz, 0.15f, nz - lean * nx, face);
                    rockMesh.Vertex(fx + nx * 0.05f, foot, fz + nz * 0.05f, nx + lean * nz, 0.05f, nz - lean * nx, face);
                    if (k > 0)
                    {
                        int a = first, b = first + 1, cc = v0, d = v0 + 1;   // a, cc: tops; b, d: feet
                        // Front faces must run clockwise seen from the low side
                        float ux = fx - (rockMesh.positions[a * 3]), uz = fz - (rockMesh.positions[a * 3 + 2]);
                        bool flip = (ux * nz - uz * nx) < 0f;
                        if (flip)
                        {
                            rockMesh.triangles.Add(a); rockMesh.triangles.Add(cc); rockMesh.triangles.Add(b);
                            rockMesh.triangles.Add(cc); rockMesh.triangles.Add(d); rockMesh.triangles.Add(b);
                        }
                        else
                        {
                            rockMesh.triangles.Add(a); rockMesh.triangles.Add(b); rockMesh.triangles.Add(cc);
                            rockMesh.triangles.Add(cc); rockMesh.triangles.Add(b); rockMesh.triangles.Add(d);
                        }
                    }
                    first = v0;
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
        TvColor stone = TvColor.Hex("#7e7b74"), wood = TvColor.Hex("#6a5a48"), pole = TvColor.Hex("#4e4033"), wire = TvColor.Hex("#2b2b2b"),
            steel = TvColor.Hex("#8c9094"), house = TvColor.Hex("#b4472f"), roof = TvColor.Hex("#3f3b38");
        TvColor grey = TvColor.Hex("#857d70"), wireC = TvColor.Hex("#9a9a94");
        TvShape wallPiece = Box(1f, 1f, 0.55f, stone), wallCap = Box(1f, 0.14f, 0.68f, TvColor.Hex("#6c6a64"));

        foreach (var line in lines)
        {
            float[] c = line.xz;
            switch (line.kind)
            {
                case WorldLineKind.HighWall:
                    Along(c, 1.2f, (x, z, yaw, len) =>
                    {
                        // Unity turns clockwise round y for a positive angle seen from above; the mesh copy turns the same way
                        thingMesh.Add(wallPiece, x, H(x, z) - 0.1f, z, len + 0.05f, 1.9f, 1f, yaw, Rr(0.94f, 1.04f));
                        thingMesh.Add(wallCap, x, H(x, z) + 1.78f, z, len + 0.05f, 1f, 1f, yaw);
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
                    Along(c, 2.4f, (x, z, yaw, len) =>
                    {
                        float y = H(x, z);
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
                    Along(c, 3f, (x, z, yaw, len) =>
                    {
                        float y = H(x, z);
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
                        Bar(thingMesh, ax, H(ax, az) + 0.2f, az, bx, H(bx, bz) + 0.2f, bz, 0.19f, 0.13f, 6, TvColor.Hex("#5e5346"));
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
                case WorldPointKind.RootStock:
                {
                    // The root plate of a fallen tree, standing on edge
                    float yaw = Rr(0f, 6.283f);
                    thingMesh.Add(rootPlate, x, y - 0.3f, z, 1.2f, 1.1f, 1.2f, yaw);
                    // The trunk lies away from the plate
                    float tx = (float)Math.Sin(yaw), tz = (float)Math.Cos(yaw);
                    Bar(thingMesh, x + tx * 0.3f, y + 0.55f, z + tz * 0.3f, x + tx * 7f, H(x + tx * 7f, z + tz * 7f) + 0.2f, z + tz * 7f, 0.2f, 0.12f, 6, TvColor.Hex("#5e5346"));
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
        int reach = chunk ? R + 40 : R;
        double e0 = Math.Floor((input.centerE - reach - layer.minE) / layer.cell), n0 = Math.Floor((input.centerN - reach - layer.minN) / layer.cell);
        int cells = (int)(2 * reach / layer.cell) + 2;
        var isHouse = new bool[cells * cells];
        float CellX(int i) => (float)(layer.minE + (e0 + i + 0.5) * layer.cell - input.centerE);
        float CellZ(int j) => (float)(layer.minN + (n0 + j + 0.5) * layer.cell - input.centerN);
        for (int j = 0; j < cells; j++)
            for (int i = 0; i < cells; i++)
                isHouse[j * cells + i] = Base(layer.At(input.centerE + CellX(i), input.centerN + CellZ(j))) == WorldCell.Building
                                         && (chunk || Inside(CellX(i), CellZ(j), 2f));

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
            if (chunk)
            {
                float mx = 0f, mz = 0f;
                foreach (int k in group) { mx += CellX(k % cells); mz += CellZ(k / cells); }
                if (!Inside(mx / group.Count, mz / group.Count, 0f)) continue;
            }
            House(group, cells, CellX, CellZ);
        }
    }

    // A pitched roof along x: two thick slabs meeting at the ridge, reaching out over the walls by over
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

                bool near = chunk || x * x + z * z < 70f * 70f;
                int pick = random.Value() < 0.5f ? 0 : 1;
                TvShape shape, far;
                float trunk;
                switch (tree)
                {
                    case Tree.Pine: shape = (near ? pineNear : pineFar)[pick]; far = pineFar[pick]; trunk = 0.3f; break;
                    case Tree.Pine2: shape = (near ? pine2Near : pine2Far)[pick]; far = pine2Far[pick]; trunk = 0.28f; break;
                    case Tree.Spruce: shape = spruceShape[pick]; far = spruceFar; trunk = 0.45f; break;   // the low branches are in the way too
                    case Tree.Birch: shape = (near ? birchNear : birchFar)[pick]; far = birchFar[pick]; trunk = 0.22f; break;
                    default: shape = (near ? beechNear : beechFar)[pick]; far = beechFar[pick]; trunk = 0.42f; break;
                }
                float y = H(x, z) - 0.15f, sy = s * Rr(0.9f, 1.12f), yaw = Rr(0f, 6.283f), tint = Rr(0.82f, 1.12f), tx = Rr(-0.03f, 0.03f), tz = Rr(-0.03f, 0.03f);
                treeMeshes[Tile(x, z)].Add(shape, x, y, z, s, sy, s, yaw, tint, tx, tz);
                // A piece of the run-yourself world: the same tree, cheaper, for when it is far from the runner
                if (chunk) treeFarMesh.Add(far, x, y, z, s, sy, s, yaw, tint, tx, tz);
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
                    default: continue;
                }
                if (random.Value() > p || Blocked(x, z, 0.8f)) continue;
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
            if (!chunk && x * x + z * z < 18f * 18f)
            {
                if (shape == tuftBerry) shape = tuftBerryNear;
                else if (shape == tuftHeath) shape = tuftHeathNear;
            }
            tuftMeshes[Tile(x, z)].Add(shape, x, H(x, z) - 0.02f, z, s, s, s, Rr(0f, 6.283f), tint);
            world.tufts++;
        }
    }
}
