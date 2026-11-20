using System;
using System.Collections.Generic;
using MapImport;

// What the ground does to the runner in the run-yourself game mode (RunSession): how quick it is, how rough (the map
// shakes more, and a tired runner stumbles more easily), what it sounds like, and where the runner cannot go at all.
// Read from the map's world layer (what grows in every 2 m cell, and its lines), with the map's own terrain speed
// sliders (TerrainSpeeds, set at import), so the ground is as quick here as in the rest of the game.
// Plain C#: made on a background thread when the race loads.
public class RunTerrain
{
    public enum Surface : byte { Forest, Open, Rough, Thicket, Marsh, Water, Road, Path, Paved, Rock, Sand }

    public struct Ground
    {
        public float speed;       // compared with white forest at the middle of every slider (1)
        public float rough;       // 0.3 on a road, 1 in white forest, up to 1.7 in a thicket or a boulder field
        public Surface surface;
        public bool blocked;      // nobody gets through: a lake, a house, a garden, impassable dark green, outside the map
        public bool stairs;       // on stairs: quick up, slow down (RunPlayer.Speed)
    }

    readonly WorldLayer layer;
    readonly TvHeights heights;
    readonly TerrainSpeeds speeds;
    readonly double maxE, maxN;

    // The lines that matter to a runner, cut into pieces and sorted into squares, so a place only looks at a few
    const float Bucket = 16f;
    readonly int bucketCols, bucketRows;
    readonly List<int>[] buckets;
    readonly List<Piece> pieces = new List<Piece>();

    struct Piece
    {
        public WorldLineKind kind;
        public float ax, az, bx, bz;     // metres from the layer's lower left corner
        public float way;                // a bridge or tunnel: metres from its middle line that are on it
    }

    // Bridges and tunnels of a town (TvTown): a way over water and through houses, fences and walls
    const WorldLineKind BridgeKind = (WorldLineKind)250, TunnelKind = (WorldLineKind)251;
    // The course setter's uncrossable boundaries and bridges (ForbiddenArea), and the stairs of a town
    const WorldLineKind CourseLineKind = (WorldLineKind)252, CourseBridgeKind = (WorldLineKind)253, TownStairsKind = (WorldLineKind)254;

    // The 2D race's own grid: on a sprint map it says where the runner may go (2026-11-20). Null when the scene has none.
    readonly TvPassable passable;
    // A sprint map (a town) imported before the Impassable cell: its dark green (ISSprOM 410) is impassable
    readonly bool oldSprintGreen;
    // The course setter's out-of-bounds areas, in metres from the layer's lower left corner
    readonly List<float[]> outOfBounds = new List<float[]>();

    static bool IsWay(WorldLineKind k) => k == BridgeKind || k == TunnelKind || k == CourseBridgeKind || k == WorldLineKind.Bridge;
    static bool IsStairs(WorldLineKind k) => k == WorldLineKind.Stairs || k == TownStairsKind;

    // forbidden: the course's out-of-bounds areas, uncrossable boundaries and bridges (the same ones the 2D runners keep to)
    public RunTerrain(WorldLayer layer, TvHeights heights, TerrainSpeeds speeds, TvTown town = null, List<ForbiddenArea> forbidden = null,
                      TvPassable passable = null)
    {
        this.layer = layer;
        this.passable = passable;
        this.heights = heights;
        this.speeds = speeds ?? new TerrainSpeeds();
        // A sprint map's world layer from before 2026-11-20 has no Impassable cells: its dark green is ISSprOM 410
        oldSprintGreen = town != null && layer.version < 2;
        maxE = layer.minE + layer.cols * layer.cell;
        maxN = layer.minN + layer.rows * layer.cell;
        bucketCols = Math.Max(1, (int)Math.Ceiling(layer.cols * layer.cell / Bucket));
        bucketRows = Math.Max(1, (int)Math.Ceiling(layer.rows * layer.cell / Bucket));
        buckets = new List<int>[bucketCols * bucketRows];
        var all = new List<(WorldLineKind kind, float[] xy)>();
        var wayHalf = new Dictionary<float[], float>();
        foreach (WorldLine line in layer.lines)
            if (line.xy != null && Matters(line.kind)) all.Add((line.kind, line.xy));
        if (town != null)
        {
            // A bridge or tunnel drawn as two side lines is one line down its middle, as wide as the sides are apart
            for (int b = 0; b < town.bridges.Count; b++)
            {
                all.Add((BridgeKind, town.bridges[b]));
                if (b < town.bridgeWidths.Count && town.bridgeWidths[b] > 0.5f) wayHalf[town.bridges[b]] = Math.Min(5f, town.bridgeWidths[b] / 2f) + 0.2f;
            }
            for (int b = 0; b < town.tunnels.Count; b++)
            {
                all.Add((TunnelKind, town.tunnels[b]));
                if (b < town.tunnelWidths.Count && town.tunnelWidths[b] > 0.5f) wayHalf[town.tunnels[b]] = Math.Min(5f, town.tunnelWidths[b] / 2f);
            }
            foreach (float[] xy in town.stairs) all.Add((TownStairsKind, xy));
        }
        if (forbidden != null)
        {
            foreach (ForbiddenArea area in forbidden)
            {
                if (area == null || !area.IsValid) continue;
                var xy = new float[area.corners.Count * 2 + (area.line ? 0 : 2)];
                for (int k = 0; k < area.corners.Count; k++)
                {
                    xy[2 * k] = (float)area.corners[k].e;
                    xy[2 * k + 1] = (float)area.corners[k].n;
                }
                if (area.line)
                {
                    all.Add((area.bridge ? CourseBridgeKind : CourseLineKind, xy));
                    continue;
                }
                // An area: its border is a line nobody crosses, and inside it is closed
                xy[xy.Length - 2] = xy[0];
                xy[xy.Length - 1] = xy[1];
                all.Add((CourseLineKind, xy));
                var local = new float[xy.Length];
                for (int k = 0; k + 1 < xy.Length; k += 2)
                {
                    local[k] = (float)(xy[k] - layer.minE);
                    local[k + 1] = (float)(xy[k + 1] - layer.minN);
                }
                outOfBounds.Add(local);
            }
        }
        foreach (var line in all)
        {
            for (int i = 0; i + 3 < line.xy.Length; i += 2)
            {
                var p = new Piece
                {
                    kind = line.kind,
                    way = wayHalf.TryGetValue(line.xy, out float wh) ? Math.Max(1.4f, wh) : 1.4f,
                    ax = (float)(line.xy[i] - layer.minE), az = (float)(line.xy[i + 1] - layer.minN),
                    bx = (float)(line.xy[i + 2] - layer.minE), bz = (float)(line.xy[i + 3] - layer.minN)
                };
                int index = pieces.Count;
                pieces.Add(p);
                float Pad = Math.Max(4f, p.way + 1f);
                int i0 = Math.Max(0, (int)((Math.Min(p.ax, p.bx) - Pad) / Bucket)), i1 = Math.Min(bucketCols - 1, (int)((Math.Max(p.ax, p.bx) + Pad) / Bucket));
                int j0 = Math.Max(0, (int)((Math.Min(p.az, p.bz) - Pad) / Bucket)), j1 = Math.Min(bucketRows - 1, (int)((Math.Max(p.az, p.bz) + Pad) / Bucket));
                for (int j = j0; j <= j1; j++)
                    for (int k = i0; k <= i1; k++)
                        (buckets[j * bucketCols + k] ?? (buckets[j * bucketCols + k] = new List<int>())).Add(index);
            }
        }
    }

    static bool Matters(WorldLineKind kind)
    {
        switch (kind)
        {
            case WorldLineKind.WideRoad: case WorldLineKind.Road: case WorldLineKind.Track: case WorldLineKind.Footpath:
            case WorldLineKind.SmallPath: case WorldLineKind.FaintPath: case WorldLineKind.Ride: case WorldLineKind.Railway:
            case WorldLineKind.Stream: case WorldLineKind.SmallStream: case WorldLineKind.Ditch: case WorldLineKind.NarrowMarsh:
            case WorldLineKind.Cliff: case WorldLineKind.SmallCliff: case WorldLineKind.HighFence: case WorldLineKind.HighWall:
            case WorldLineKind.Wall: case WorldLineKind.RuinedWall: case WorldLineKind.Fence: case WorldLineKind.RuinedFence: case WorldLineKind.Log:
            case WorldLineKind.Hedge: case WorldLineKind.Barrier: case WorldLineKind.Stairs: case WorldLineKind.Bridge:
                return true;
            default:
                return false;
        }
    }

    // ---- Heights ----

    // The map's own heights (without the small bumps the 3D pieces add); used before a piece is built
    public float MapHeight(double e, double n) => heights != null ? heights.At(e, n) : 0f;

    public bool OnMap(double e, double n) => e > layer.minE + 1 && n > layer.minN + 1 && e < maxE - 1 && n < maxN - 1;

    // ---- The ground at a place ----

    public Ground At(double e, double n)
    {
        var g = new Ground { speed = 1f, rough = 1f, surface = Surface.Forest };
        if (!OnMap(e, n))
        {
            g.blocked = true;
            return g;
        }
        byte cell = layer.At(e, n);
        float forest = speeds.Ground(TerrainKind.Forest, 1f);
        switch (cell & WorldCell.BaseMask)
        {
            case WorldCell.Open: g.speed = speeds.Ground(TerrainKind.Open, 1.15f); g.rough = 0.7f; g.surface = Surface.Open; break;
            case WorldCell.OpenTrees: g.speed = speeds.Ground(TerrainKind.Open, 1.1f); g.rough = 0.75f; g.surface = Surface.Open; break;
            case WorldCell.RoughOpen: g.speed = speeds.Ground(TerrainKind.RoughOpen, 0.8f); g.rough = 1.3f; g.surface = Surface.Rough; break;
            case WorldCell.RoughOpenTrees: g.speed = speeds.Ground(TerrainKind.RoughOpen, 0.8f); g.rough = 1.25f; g.surface = Surface.Rough; break;
            case WorldCell.LightGreen: g.speed = speeds.Ground(TerrainKind.LightGreen, 0.7f); g.rough = 1.35f; g.surface = Surface.Thicket; break;
            case WorldCell.Green: g.speed = speeds.Ground(TerrainKind.MidGreen, 0.55f); g.rough = 1.55f; g.surface = Surface.Thicket; break;
            case WorldCell.DarkGreen:
                g.speed = speeds.Ground(TerrainKind.DarkGreen, 0.35f); g.rough = 1.7f; g.surface = Surface.Thicket;
                if (oldSprintGreen) g.blocked = true;   // ISSprOM 410: impassable vegetation (maps imported before the Impassable cell)
                break;
            case WorldCell.Cultivated: g.speed = speeds.Ground(TerrainKind.Open, 1f); g.rough = 0.8f; g.surface = Surface.Open; break;
            case WorldCell.Water: g.blocked = true; g.surface = Surface.Water; break;
            case WorldCell.BareRock: g.speed = speeds.Ground(TerrainKind.Open, 1.15f); g.rough = 0.8f; g.surface = Surface.Rock; break;
            case WorldCell.Sand: g.speed = 0.8f * forest; g.rough = 0.8f; g.surface = Surface.Sand; break;
            case WorldCell.Paved: g.speed = speeds.Ground(TerrainKind.Paved, 1.5f); g.rough = 0.3f; g.surface = Surface.Paved; break;
            case WorldCell.Building: g.blocked = true; g.surface = Surface.Paved; break;
            case WorldCell.Plot: g.blocked = true; g.surface = Surface.Open; break;   // a private garden: out of bounds
            case WorldCell.Impassable: g.blocked = true; g.rough = 1.7f; g.surface = Surface.Thicket; break;
            default: g.speed = forest; g.rough = 1f; g.surface = Surface.Forest; break;
        }
        if (g.speed <= 0.02f) g.blocked = true;
        if (passable != null && passable.sprint)
        {
            // A town: closed exactly where the 2D race is closed. Where the map is open but the 3D world has a
            // house, water or a garden (a passage the 2D game opens), the runner runs on paving.
            bool closed = passable.Closed(e, n) && !OnWay(e, n);
            if (g.blocked && !closed)
            {
                g.speed = speeds.Ground(TerrainKind.Paved, 1.5f);
                g.rough = 0.3f;
                g.surface = Surface.Paved;
            }
            g.blocked = closed;
            if (closed) return g;
        }
        if (outOfBounds.Count > 0)
        {
            float ox = (float)(e - layer.minE), oz = (float)(n - layer.minN);
            foreach (float[] poly in outOfBounds)
            {
                if (Inside(poly, ox, oz)) { g.blocked = true; break; }
            }
        }
        if ((cell & WorldCell.Marsh) != 0)
        {
            g.speed *= speeds.Ground(TerrainKind.Marsh, 0.8f) / Math.Max(0.05f, forest);
            g.rough = Math.Max(g.rough, 1.35f);
            g.surface = Surface.Marsh;
        }
        if ((cell & WorldCell.Undergrowth) != 0)
        {
            g.speed *= 0.8f;
            g.rough = Math.Max(g.rough, 1.3f);
            if (g.surface == Surface.Forest) g.surface = Surface.Rough;
        }
        if ((cell & WorldCell.Stony) != 0)
        {
            g.speed *= speeds.StonyFactor(0.8f);
            g.rough = Math.Max(g.rough, 1.45f);
        }
        if ((cell & WorldCell.Boulders) != 0)
        {
            g.speed *= speeds.StonyFactor(0.6f);
            g.rough = Math.Max(g.rough, 1.6f);
        }

        // Roads, paths and streams close by
        float x = (float)(e - layer.minE), z = (float)(n - layer.minN);
        List<int> near = BucketAt(x, z);
        if (near == null) return g;
        foreach (int index in near)
        {
            Piece p = pieces[index];
            float d = Dist(x, z, p);
            switch (p.kind)
            {
                case WorldLineKind.WideRoad: OnTrack(ref g, d, 3f, speeds.Line(TerrainKind.Road, 1.5f), 0.3f, Surface.Road); break;
                case WorldLineKind.Road: OnTrack(ref g, d, 2.2f, speeds.Line(TerrainKind.Road, 1.4f), 0.32f, Surface.Road); break;
                case WorldLineKind.Track: OnTrack(ref g, d, 1.7f, speeds.Line(TerrainKind.Road, 1.35f), 0.4f, Surface.Path); break;
                case WorldLineKind.Footpath: OnTrack(ref g, d, 1.1f, speeds.Line(TerrainKind.Path, 1.25f), 0.5f, Surface.Path); break;
                case WorldLineKind.SmallPath: OnTrack(ref g, d, 0.9f, speeds.Line(TerrainKind.Path, 1.2f), 0.55f, Surface.Path); break;
                case WorldLineKind.FaintPath: OnTrack(ref g, d, 0.8f, speeds.Line(TerrainKind.Path, 1.1f), 0.65f, Surface.Path); break;
                case WorldLineKind.Ride: OnTrack(ref g, d, 1.8f, speeds.Line(TerrainKind.Path, 1.05f), 0.7f, Surface.Open); break;
                case WorldLineKind.Railway: OnTrack(ref g, d, 2.4f, 1.1f, 0.9f, Surface.Rock); break;
                case WorldLineKind.SmallStream:
                    if (d < 0.7f) { g.speed *= 0.6f; g.rough = Math.Max(g.rough, 1.3f); g.surface = Surface.Water; }
                    break;
                case WorldLineKind.Stream:
                    if (d < 1.4f) { g.speed *= 0.4f; g.rough = Math.Max(g.rough, 1.5f); g.surface = Surface.Water; }
                    break;
                case WorldLineKind.Ditch:
                    if (d < 0.6f) { g.speed *= 0.85f; g.rough = Math.Max(g.rough, 1.2f); }
                    break;
                case WorldLineKind.NarrowMarsh:
                    if (d < 1.2f) { g.speed *= 0.8f; g.rough = Math.Max(g.rough, 1.3f); g.surface = Surface.Marsh; }
                    break;
            }
        }
        // On a bridge or in a tunnel: a hard, quick way, whatever is under or round it
        foreach (int index in near)
        {
            Piece p = pieces[index];
            if (!IsWay(p.kind)) continue;
            if (Dist(x, z, p) > p.way) continue;
            g.blocked = false;
            g.speed = Math.Max(g.speed, speeds.Ground(TerrainKind.Paved, 1.5f));
            g.rough = 0.3f;
            g.surface = Surface.Paved;
            break;
        }
        // On stairs: up as quick as a road, down slowly (2026-11-20, Anders; RunPlayer.Speed slows the way down)
        foreach (int index in near)
        {
            Piece p = pieces[index];
            if (!IsStairs(p.kind) || Dist(x, z, p) > 1.2f) continue;
            g.blocked = false;
            g.speed = SymbolRules.StairsSpeed * forest;
            g.stairs = true;
            g.rough = 1.2f;
            g.surface = Surface.Paved;
            break;
        }
        return g;
    }

    // On a bridge or in a tunnel
    bool OnWay(double e, double n)
    {
        float x = (float)(e - layer.minE), z = (float)(n - layer.minN);
        List<int> near = BucketAt(x, z);
        if (near == null) return false;
        foreach (int index in near)
        {
            Piece p = pieces[index];
            if (IsWay(p.kind) && Dist(x, z, p) <= p.way) return true;
        }
        return false;
    }

    static void OnTrack(ref Ground g, float d, float half, float speed, float rough, Surface surface)
    {
        if (d > half || g.blocked) return;
        if (speed > g.speed || rough < g.rough)
        {
            g.speed = Math.Max(g.speed, speed);
            g.rough = Math.Min(g.rough, rough);
            g.surface = surface;
        }
    }

    // ---- Where the runner cannot go ----

    // True when a runner reach metres wide cannot stand here: impassable ground, an impassable cliff, a high fence or wall
    public bool Blocked(double e, double n, float reach)
    {
        if (At(e, n).blocked) return true;
        float x = (float)(e - layer.minE), z = (float)(n - layer.minN);
        List<int> near = BucketAt(x, z);
        if (near == null) return false;
        foreach (int index in near)
        {
            Piece p = pieces[index];
            if ((IsWay(p.kind) && Dist(x, z, p) < p.way) || (IsStairs(p.kind) && Dist(x, z, p) < 1.2f)) return false;
        }
        foreach (int index in near)
        {
            Piece p = pieces[index];
            if (Impassable(p.kind) && Dist(x, z, p) < reach + 0.15f)
                return true;
        }
        return false;
    }

    // True when the step from a to b goes over something a runner jumps (a stone wall, a fence, a fallen trunk), or
    // through an impassable cliff, fence or wall (then blocked is true)
    // A small cliff (smallCliff true) is a passable rock face of about 1.5 m: a runner jumps down it or climbs up it
    public bool Crosses(double ae, double an, double be, double bn, out float height, out bool blocked, out bool smallCliff)
    {
        height = 0f;
        blocked = false;
        smallCliff = false;
        float ax = (float)(ae - layer.minE), az = (float)(an - layer.minN), bx = (float)(be - layer.minE), bz = (float)(bn - layer.minN);
        List<int> near = BucketAt((ax + bx) / 2f, (az + bz) / 2f);
        if (near == null) return false;
        bool any = false;
        // Through a tunnel, over a bridge or on stairs nothing is in the way
        foreach (int index in near)
        {
            Piece p = pieces[index];
            if ((IsWay(p.kind) || IsStairs(p.kind)) && Dist((ax + bx) / 2f, (az + bz) / 2f, p) < Math.Max(1.6f, p.way)) return false;
        }
        foreach (int index in near)
        {
            Piece p = pieces[index];
            float h;
            switch (p.kind)
            {
                case WorldLineKind.Wall: h = 0.8f; break;
                case WorldLineKind.RuinedWall: h = 0.4f; break;
                case WorldLineKind.Fence: h = 1f; break;
                case WorldLineKind.RuinedFence: h = 0.6f; break;
                case WorldLineKind.Log: h = 0.45f; break;
                case WorldLineKind.Cliff: case WorldLineKind.HighFence: case WorldLineKind.HighWall:
                case WorldLineKind.Hedge: case WorldLineKind.Barrier: case CourseLineKind: h = -1f; break;
                case WorldLineKind.SmallCliff: h = -2f; break;
                default: continue;
            }
            if (!Intersect(ax, az, bx, bz, p.ax, p.az, p.bx, p.bz)) continue;
            if (h == -2f) smallCliff = true;
            else if (h < 0f) blocked = true;
            else height = Math.Max(height, h);
            any = true;
        }
        return any;
    }

    // How far the nearest line of this kind group is, for the sounds and for the hurdles ahead (null = far)
    List<int> BucketAt(float x, float z)
    {
        int i = (int)(x / Bucket), j = (int)(z / Bucket);
        if (i < 0 || j < 0 || i >= bucketCols || j >= bucketRows) return null;
        return buckets[j * bucketCols + i];
    }

    // Lines nobody gets over or through
    static bool Impassable(WorldLineKind k) =>
        k == WorldLineKind.Cliff || k == WorldLineKind.HighFence || k == WorldLineKind.HighWall || k == WorldLineKind.Hedge
        || k == WorldLineKind.Barrier || k == CourseLineKind;

    // Even-odd test: is (x, z) inside the closed ring xy (x0, z0, x1, z1, ...)?
    static bool Inside(float[] xy, float x, float z)
    {
        bool inside = false;
        int count = xy.Length / 2;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            float xi = xy[2 * i], zi = xy[2 * i + 1], xj = xy[2 * j], zj = xy[2 * j + 1];
            if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
        }
        return inside;
    }

    static float Dist(float px, float pz, Piece p)
    {
        float dx = p.bx - p.ax, dz = p.bz - p.az, l = dx * dx + dz * dz;
        float t = l > 0f ? ((px - p.ax) * dx + (pz - p.az) * dz) / l : 0f;
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        float qx = p.ax + dx * t - px, qz = p.az + dz * t - pz;
        return (float)Math.Sqrt(qx * qx + qz * qz);
    }

    static bool Intersect(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz)
    {
        float d1 = Cross(cx, cz, dx, dz, ax, az), d2 = Cross(cx, cz, dx, dz, bx, bz);
        float d3 = Cross(ax, az, bx, bz, cx, cz), d4 = Cross(ax, az, bx, bz, dx, dz);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    static float Cross(float ax, float az, float bx, float bz, float px, float pz) => (bx - ax) * (pz - az) - (bz - az) * (px - ax);
}
