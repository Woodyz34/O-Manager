using System;
using System.Collections.Generic;
using MapImport;

// The houses of a town block built from the map's own outline (2026-11-20, Anders: "alla hus ska vara exakt storlek
// på det som är på kartan", a bent house bent, terraced houses meeting without gaps and without going into each
// other, openings where the map has them). Before, every house was a box fitted round its cells, so an L-shaped or
// slanted house grew into the lane beside it and closed passages that are open on the map.
//
// Now a block (joined house cells, without the cells the 2D race lets a runner through, TvPassable) is cut into houses
// as before (SplitHouses: across its long side, flats and halls whole), and then:
//  - every cell gets the number of its house; the borders between numbers (and between a house and the street) are
//    traced on the cell corners and straightened (the steps of a slanting wall become one straight wall, a curve a
//    few short walls). A border between two houses is straightened once and used by both, so they meet exactly.
//  - each house's outline is put up as walls: towards the street a facade with a plinth, windows on every floor, a
//    door, shop windows and awnings on the street floor, bands and a cornice; towards a lower neighbour only the wall
//    above the neighbour's roof.
//  - a house with four corners gets a pitched roof: the ridge along it, with gables against its neighbours (a hip
//    roof when it stands alone); any other outline a flat roof with a low parapet.
// The full facades are built in a piece's detail (round the runner, High quality), a leaner one (no frames, sills,
// signs) within the near distance, plain walls and roofs further away.
public partial class TvWorldBuilder
{
    // While a block is cut: the houses SplitHouses makes (their cells and kind), built together afterwards
    List<(List<(float x, float z)> pts, TvCityShapes.Kind kind)> blockHouses;

    struct HouseSeg
    {
        public float ax, az, bx, bz;
        public int other;               // the house on the other side (0: the street or a yard)
    }

    class HouseOutline
    {
        public readonly List<List<HouseSeg>> rings = new List<List<HouseSeg>>();   // the first is the outside
        public float cx, cz, area;
        public TvCityShapes.Kind kind;
        public int floors, look;
        public float baseY, top, plinth;
        public TvCityShapes.Palette colour;
        public bool flat;
        public int group = -1;          // the big house or row it belongs to (TvWorldBuilderTown.HouseGroup), -1 none
        public bool row;                // a home in a row of terraced houses: a front door whatever is in front
    }

    void BuildBlockHouses(List<(List<(float x, float z)> pts, TvCityShapes.Kind kind)> houses)
    {
        if (houses.Count == 0) return;
        float c = layer.cell;
        // ---- 1. The cells of the block on a grid of their own, each with the number of its house ----
        float refX = houses[0].pts[0].x, refZ = houses[0].pts[0].z;
        int minI = int.MaxValue, minJ = int.MaxValue, maxI = int.MinValue, maxJ = int.MinValue;
        foreach (var h in houses)
            foreach (var p in h.pts)
            {
                int i = (int)Math.Round((p.x - refX) / c), j = (int)Math.Round((p.z - refZ) / c);
                minI = Math.Min(minI, i); maxI = Math.Max(maxI, i); minJ = Math.Min(minJ, j); maxJ = Math.Max(maxJ, j);
            }
        int W = maxI - minI + 3, Hh = maxJ - minJ + 3;
        var lab = new int[W * Hh];
        for (int k = 0; k < houses.Count; k++)
            foreach (var p in houses[k].pts)
            {
                int i = (int)Math.Round((p.x - refX) / c) - minI + 1, j = (int)Math.Round((p.z - refZ) / c) - minJ + 1;
                lab[j * W + i] = k + 1;
            }
        // A corner (I, J) of the grid is the lower left corner of cell (I, J); in the piece's metres:
        float x0 = refX + (minI - 1) * c - c / 2f, z0 = refZ + (minJ - 1) * c - c / 2f;
        int L(int i, int j) => i < 0 || j < 0 || i >= W || j >= Hh ? 0 : lab[j * W + i];

        // ---- 2. The borders: edges on the cell corners, each with the house on its left and on its right ----
        // An edge goes from corner a to corner b; key = J * (W + 1) + I
        var edges = new List<(int a, int b, int left, int right)>();
        for (int j = 0; j < Hh; j++)
            for (int i = 0; i < W; i++)
            {
                int here = L(i, j);
                // Between this cell and the one on its right: an edge up along x = i + 1, this cell on the left
                int east = L(i + 1, j);
                if (i + 1 < W && here != east) edges.Add((K(i + 1, j, W), K(i + 1, j + 1, W), here, east));
                // Between this cell and the one above: an edge along z = j + 1 going -x, this cell on the left
                int north = L(i, j + 1);
                if (j + 1 < Hh && here != north) edges.Add((K(i + 1, j + 1, W), K(i, j + 1, W), here, north));
            }
        // Every edge both ways round, so each house's edges have it on the left
        var byStart = new Dictionary<int, List<int>>();
        var directed = new List<(int a, int b, int left, int right)>(edges.Count * 2);
        foreach (var e in edges)
        {
            directed.Add(e);
            directed.Add((e.b, e.a, e.right, e.left));
        }
        for (int k = 0; k < directed.Count; k++)
        {
            if (!byStart.TryGetValue(directed[k].a, out var list)) byStart[directed[k].a] = list = new List<int>();
            list.Add(k);
        }

        // A corner where more than two houses (or the street) meet, or where the border touches itself, is a node:
        // the straightened borders keep it exactly
        bool Node(int key)
        {
            int I = key % (W + 1), J = key / (W + 1);
            int a = L(I - 1, J - 1), b = L(I, J - 1), d = L(I - 1, J), e = L(I, J);
            int distinct = 1 + (b != a ? 1 : 0) + (d != a && d != b ? 1 : 0) + (e != a && e != b && e != d ? 1 : 0);
            if (distinct >= 3) return true;
            // Two and two across the corner (a chess board): the border touches itself here
            return a == e && b == d && a != b;
        }

        // ---- 3a. The block's outside, straightened as one: the corners where homes meet on a front sit on that
        // straight line (2026-11-23, Anders: a long slanted row had a step in its front at every home, it is one
        // smooth wall on the map) ----
        float tol = input.passable != null ? c * 0.5f : c * 0.76f;
        var outerPos = new Dictionary<int, (float x, float z)>();
        {
            var usedO = new bool[directed.Count];
            for (int k0 = 0; k0 < directed.Count; k0++)
            {
                if (usedO[k0] || directed[k0].left <= 0 || directed[k0].right != 0) continue;
                var keys = new List<int> { directed[k0].a };
                int k = k0, guard = 0;
                while (guard++ < 100000)
                {
                    usedO[k] = true;
                    int at = directed[k].b;
                    keys.Add(at);
                    if (at == keys[0]) break;
                    int next = -1;
                    if (byStart.TryGetValue(at, out var outs))
                        foreach (int q in outs)
                            if (!usedO[q] && directed[q].left > 0 && directed[q].right == 0) { next = q; break; }
                    if (next < 0) break;
                    k = next;
                }
                if (keys.Count < 5 || keys[0] != keys[keys.Count - 1]) continue;
                var ring = new List<(float x, float z)>(keys.Count);
                foreach (int key in keys) ring.Add((x0 + key % (W + 1) * c, z0 + key / (W + 1) * c));
                var straight = StraightenLoop(ring, tol);
                // On the map's own outline where it has one: exactly straight walls and real corners
                float near = tol + 0.05f;
                var onMap = SnapRing(ring);
                if (onMap != null) { straight = onMap; near = 1.9f; }
                if (straight.Count < 3) continue;
                for (int i = 0; i < ring.Count; i++)
                {
                    // Onto the nearest wall of the straightened outside
                    float bx = ring[i].x, bz = ring[i].z, bestD = float.MaxValue;
                    for (int q = 0; q + 1 < straight.Count; q++)
                    {
                        float d = DistSeg(ring[i].x, ring[i].z, straight[q].x, straight[q].z, straight[q + 1].x, straight[q + 1].z, out float px, out float pz);
                        if (d < bestD) { bestD = d; bx = px; bz = pz; }
                    }
                    if (bestD <= near) outerPos[keys[i]] = (bx, bz);
                }
            }
        }
        (float x, float z) Corner(int key) => outerPos.TryGetValue(key, out var at) ? at : (x0 + key % (W + 1) * c, z0 + key / (W + 1) * c);

        // ---- 3. Chains: the borders between nodes, straightened once for both sides ----
        var used = new bool[directed.Count];
        var chainOf = new List<(List<(float x, float z)> pts, int left, int right, int startKey, int endKey)>();
        int Pair(int k) => (k & 1) == 0 ? k + 1 : k - 1;   // the same edge the other way round
        void Walk(int first)
        {
            var keys = new List<int> { directed[first].a };
            int left = directed[first].left, right = directed[first].right;
            int k = first;
            while (true)
            {
                used[k] = true;
                used[Pair(k)] = true;
                int at = directed[k].b;
                keys.Add(at);
                if (Node(at) || at == keys[0]) break;
                int next = -1;
                if (byStart.TryGetValue(at, out var outs))
                    foreach (int q in outs)
                        if (!used[q] && directed[q].left == left && directed[q].right == right) { next = q; break; }
                if (next < 0) break;
                k = next;
            }
            var pts = new List<(float x, float z)>(keys.Count);
            foreach (int key in keys) pts.Add(Corner(key));
            bool loop = keys[0] == keys[keys.Count - 1] && !Node(keys[0]);
            // In the run-yourself world a little tighter to the map's cells, so a wall never stands far out in a street
            // or a passage the race lets the runner through. A front is already on the straightened outside: only
            // its own corners are kept.
            float t = left == 0 || right == 0 ? 0.05f : tol;
            pts = loop ? StraightenLoop(pts, t) : Straighten(pts, t);
            chainOf.Add((pts, left, right, keys[0], keys[keys.Count - 1]));
        }
        // From the nodes first, then the loops that have none
        for (int k = 0; k < directed.Count; k++)
            if (!used[k] && directed[k].left > 0 && Node(directed[k].a)) Walk(k);
        for (int k = 0; k < directed.Count; k++)
            if (!used[k] && directed[k].left > 0) Walk(k);

        // ---- 4. Each house's rings from the chains that have it on one side ----
        var outlines = new HouseOutline[houses.Count];
        for (int h = 1; h <= houses.Count; h++)
        {
            var mine = new List<(List<(float x, float z)> pts, int other, int s, int e)>();
            foreach (var ch in chainOf)
            {
                if (ch.left == h) mine.Add((ch.pts, ch.right, ch.startKey, ch.endKey));
                else if (ch.right == h)
                {
                    var rev = new List<(float x, float z)>(ch.pts);
                    rev.Reverse();
                    mine.Add((rev, ch.left, ch.endKey, ch.startKey));
                }
            }
            var o = new HouseOutline();
            var taken = new bool[mine.Count];
            for (int s = 0; s < mine.Count; s++)
            {
                if (taken[s]) continue;
                var ring = new List<HouseSeg>();
                int at = s, guard = 0;
                while (at >= 0 && !taken[at] && guard++ < 10000)
                {
                    taken[at] = true;
                    var m = mine[at];
                    for (int q = 0; q + 1 < m.pts.Count; q++)
                        ring.Add(new HouseSeg { ax = m.pts[q].x, az = m.pts[q].z, bx = m.pts[q + 1].x, bz = m.pts[q + 1].z, other = m.other });
                    int next = -1;
                    for (int q = 0; q < mine.Count; q++)
                        if (!taken[q] && mine[q].s == m.e) { next = q; break; }
                    at = next;
                }
                ring.RemoveAll(sg => Math.Abs(sg.bx - sg.ax) + Math.Abs(sg.bz - sg.az) < 0.01f);
                if (ring.Count >= 3) o.rings.Add(ring);
            }
            if (o.rings.Count == 0) continue;
            // The outside ring first (the biggest area, turning left round the house)
            o.rings.Sort((p, q) => Math.Abs(Area(q)).CompareTo(Math.Abs(Area(p))));
            o.area = Area(o.rings[0]);
            for (int r = 1; r < o.rings.Count; r++) o.area += Area(o.rings[r]);
            float mx = 0f, mz = 0f;
            foreach (var p in houses[h - 1].pts) { mx += p.x; mz += p.z; }
            o.cx = mx / houses[h - 1].pts.Count;
            o.cz = mz / houses[h - 1].pts.Count;
            o.kind = houses[h - 1].kind;
            if (blockGroup != null && h - 1 < blockGroup.Count) o.group = blockGroup[h - 1];
            outlines[h - 1] = o;
        }

        // ---- 5. Heights and looks, from the place, so every piece agrees ----
        // The homes of one row and the wings of one house stand on one plinth when the ground lets them (3 m), so the
        // row's bands and eaves run straight along it
        var groupLow = new Dictionary<int, float>();
        var groupHigh = new Dictionary<int, float>();
        foreach (HouseOutline o in outlines)
        {
            if (o == null || o.group < 0) continue;
            foreach (var sg in o.rings[0])
            {
                float y = H(sg.ax, sg.az);
                groupLow[o.group] = groupLow.TryGetValue(o.group, out float lo) ? Math.Min(lo, y) : y;
                groupHigh[o.group] = groupHigh.TryGetValue(o.group, out float hi) ? Math.Max(hi, y) : y;
            }
        }
        for (int h = 0; h < outlines.Length; h++)
        {
            HouseOutline o = outlines[h];
            if (o == null) continue;
            int hx = (int)Math.Floor((o.cx + ox) * 0.5f), hz = (int)Math.Floor((o.cz + oz) * 0.5f);
            float h1 = TvRandom.Hash(hx, hz), h2 = TvRandom.Hash(hx + 101, hz - 37);
            o.look = (int)(h1 * 997f);
            float area = Math.Abs(o.area);
            HouseGroup grp = o.group >= 0 && groups != null && o.group < groups.Count ? groups[o.group] : null;
            if (grp != null)
            {
                // The wings of one big house, the homes of one row: one look, their own floors
                o.look = grp.look;
                o.floors = Math.Max(1, blockFloors[h]);
                o.row = grp.row;
            }
            else
            {
                if (o.kind == TvCityShapes.Kind.TownHouse)
                {
                    if ((area < 170f && GardenNearAt(o.cx, o.cz, (float)Math.Sqrt(area))) || (Old && area < 60f)) o.kind = TvCityShapes.Kind.Villa;
                }
                switch (o.kind)
                {
                    case TvCityShapes.Kind.Apartments: o.floors = 3 + (int)(h2 * 3f); break;
                    case TvCityShapes.Kind.Hall: o.floors = 1; break;
                    case TvCityShapes.Kind.School: o.floors = 2; break;
                    case TvCityShapes.Kind.Villa: o.floors = 1 + (int)(h2 * 1.99f); break;
                    default: o.floors = Old ? 1 + (int)(h2 * (area > 110f ? 3.99f : 2.5f)) : 2 + (int)(h2 * 3f); break;
                }
            }
            // Neighbours never the same colour: step on until it differs from the houses already given one (the
            // wings of one house and the homes of one row keep theirs)
            int tries = 0;
            while (grp == null && tries++ < 6 && SameAsNeighbour(o, h, outlines)) o.look += 7;
            o.colour = TvCityShapes.PaletteOf(o.look, input.town.style, o.kind);
            float lowest = float.MaxValue, highest = float.MinValue;
            foreach (var sg in o.rings[0])
            {
                float y = H(sg.ax, sg.az);
                lowest = Math.Min(lowest, y); highest = Math.Max(highest, y);
            }
            if (o.group >= 0 && groupHigh[o.group] - groupLow[o.group] <= 3f)
            {
                lowest = groupLow[o.group];
                highest = groupHigh[o.group];
            }
            o.plinth = Math.Min(3f, highest - lowest) + 0.3f;
            o.baseY = lowest - 0.25f;
            float ground = GroundFloor(o), upper = UpperFloor(o);
            o.top = o.baseY + o.plinth + ground + upper * (o.floors - 1);
        }

        // ---- 6. Build the houses whose middle is in this piece ----
        EnsureTownMeshes();
        for (int h = 0; h < outlines.Length; h++)
        {
            HouseOutline o = outlines[h];
            if (o == null || !Inside(o.cx, o.cz, 0f)) continue;
            random.Reseed((int)Math.Floor((o.cx + ox) * 0.5f), (int)Math.Floor((o.cz + oz) * 0.5f), 4300);
            if (input.chunkDetail) BuildHouse(o, outlines, townMesh, 2);
            else
            {
                BuildHouse(o, outlines, townMesh, 1);
                BuildHouse(o, outlines, townFarMesh, 0);
                // The outside walls, for the run-yourself runner to stop at (2026-11-20, Anders: up close the houses
                // went see-through: the runner stood inside a wall that reaches past the race's closed cells)
                if (input.passable != null)
                    foreach (var ring in o.rings)
                        foreach (var sg in ring)
                            if (sg.other == 0) world.AddWall(sg.ax, sg.az, sg.bx, sg.bz);
            }
            townPlaced.Add((o.cx, o.cz, 0f, -1));
        }
    }

    static int K(int i, int j, int W) => j * (W + 1) + i;

    // Storey heights: a hall is one high room, a school has high classrooms, a terraced home is low
    float GroundFloor(HouseOutline o) => o.kind == TvCityShapes.Kind.Hall ? 5.5f : o.kind == TvCityShapes.Kind.School ? 3.8f : o.row ? 2.9f : Old ? 3.2f : 3.6f;
    float UpperFloor(HouseOutline o) => o.kind == TvCityShapes.Kind.School ? 3.5f : o.row ? 2.7f : Old ? 2.9f : 3.1f;

    static float Area(List<HouseSeg> ring)
    {
        float a = 0f;
        foreach (var s in ring) a += s.ax * s.bz - s.bx * s.az;
        return a / 2f;
    }

    bool SameAsNeighbour(HouseOutline o, int self, HouseOutline[] all)
    {
        TvCityShapes.Palette mine = TvCityShapes.PaletteOf(o.look, input.town.style, o.kind);
        foreach (var ring in o.rings)
            foreach (var sg in ring)
            {
                if (sg.other <= 0 || sg.other - 1 >= self) continue;   // only the houses already given a look
                HouseOutline n = all[sg.other - 1];
                if (n == null || (o.group >= 0 && n.group == o.group)) continue;
                TvCityShapes.Palette theirs = TvCityShapes.PaletteOf(n.look, input.town.style, n.kind);
                if (theirs.wall.r == mine.wall.r && theirs.wall.g == mine.wall.g && theirs.wall.b == mine.wall.b) return true;
            }
        return false;
    }

    bool GardenNearAt(float cx, float cz, float size)
    {
        int plots = 0, all = 0;
        for (float a = 0f; a < 6.28f; a += 0.785f)
        {
            float r = size / 2f + 3f, x = cx + (float)Math.Cos(a) * r, z = cz + (float)Math.Sin(a) * r;
            int b = Base(CellAt(x, z));
            all++;
            if (b == WorldCell.Plot || b == WorldCell.Open || b == WorldCell.OpenTrees || b == WorldCell.Forest) plots++;
        }
        return plots >= all / 2;
    }

    // ---- The map's building outlines ----

    // The outside of a block (its cell corners, a loop) laid on the map's building outlines near it: each run of
    // corners along one straight wall of the map becomes that wall, and its corners where the walls meet. Null where
    // the map has no outline there (an old map, or a block the outlines do not fit).
    List<(float x, float z)> SnapRing(List<(float x, float z)> ring)
    {
        TvTown town = input.town;
        int n = ring.Count - 1;
        if (town == null || town.houseEdges.Count == 0 || n < 4) return null;
        float lx = float.MaxValue, lz = float.MaxValue, hx = float.MinValue, hz = float.MinValue;
        foreach (var r in ring) { lx = Math.Min(lx, r.x); lz = Math.Min(lz, r.z); hx = Math.Max(hx, r.x); hz = Math.Max(hz, r.z); }
        lx -= 3f; lz -= 3f; hx += 3f; hz += 3f;
        // The map's straight walls near the block: segments on one line are one wall
        var segs = new List<(float ax, float az, float bx, float bz, int line)>();
        var walls = new List<(float px, float pz, float ux, float uz)>();
        foreach (float[] xy in town.houseEdges)
        {
            for (int i = 0; i + 3 < xy.Length; i += 2)
            {
                float ax = (float)(xy[i] - input.centerE), az = (float)(xy[i + 1] - input.centerN);
                float bx = (float)(xy[i + 2] - input.centerE), bz = (float)(xy[i + 3] - input.centerN);
                if (Math.Max(ax, bx) < lx || Math.Min(ax, bx) > hx || Math.Max(az, bz) < lz || Math.Min(az, bz) > hz) continue;
                float dx = bx - ax, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 0.3f) continue;
                dx /= len; dz /= len;
                int line = -1;
                for (int w = 0; w < walls.Count && line < 0; w++)
                {
                    var L = walls[w];
                    if (Math.Abs(dx * L.uz - dz * L.ux) > 0.05f) continue;
                    float mx = (ax + bx) / 2f - L.px, mz = (az + bz) / 2f - L.pz;
                    if (Math.Abs(mx * L.uz - mz * L.ux) < 0.25f) line = w;
                }
                if (line < 0) { line = walls.Count; walls.Add((ax, az, dx, dz)); }
                segs.Add((ax, az, bx, bz, line));
            }
        }
        if (segs.Count == 0) return null;
        // Each corner of the cells on its nearest wall
        var id = new int[n];
        int missing = 0;
        for (int i = 0; i < n; i++)
        {
            float best = 1.7f;
            id[i] = -1;
            foreach (var sg in segs)
            {
                float d = DistSeg(ring[i].x, ring[i].z, sg.ax, sg.az, sg.bx, sg.bz, out _, out _);
                if (d < best) { best = d; id[i] = sg.line; }
            }
            if (id[i] < 0) missing++;
        }
        if (missing * 5 > n) return null;
        int start = -1;
        for (int i = 0; i < n && start < 0; i++) if (id[i] >= 0) start = i;
        if (start < 0) return null;
        for (int k = 1; k < n; k++)
        {
            int i = (start + k) % n;
            if (id[i] < 0) id[i] = id[(i + n - 1) % n];
        }
        // A corner or two on another wall between two runs on the same wall is noise (a wall meeting this one)
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                int prev = id[(i + n - 1) % n];
                if (id[i] == prev) continue;
                int len = 1;
                while (len < 3 && id[(i + len) % n] == id[i]) len++;
                if (len < 3 && id[(i + len) % n] == prev)
                    for (int k = 0; k < len; k++) id[(i + k) % n] = prev;
            }
        // The runs round the loop, from the start of one
        int s0 = -1;
        for (int i = 0; i < n && s0 < 0; i++) if (id[i] != id[(i + n - 1) % n]) s0 = i;
        if (s0 < 0) return null;
        var runs = new List<(int line, int first)>();
        for (int k = 0; k < n; k++)
        {
            int i = (s0 + k) % n;
            if (k == 0 || id[i] != id[(i + n - 1) % n]) runs.Add((id[i], i));
        }
        if (runs.Count < 3) return null;
        var poly = new List<(float x, float z)>();
        (float x, float z) On(int line, (float x, float z) q)
        {
            var L = walls[line];
            float t = (q.x - L.px) * L.ux + (q.z - L.pz) * L.uz;
            return (L.px + L.ux * t, L.pz + L.uz * t);
        }
        for (int k = 0; k < runs.Count; k++)
        {
            var a = walls[runs[k].line];
            var bRun = runs[(k + 1) % runs.Count];
            var b = walls[bRun.line];
            // The cells' corner where the one wall gives way to the next
            var at = ring[bRun.first];
            float cross = a.ux * b.uz - a.uz * b.ux;
            if (Math.Abs(cross) > 0.25f)
            {
                float t = ((b.px - a.px) * b.uz - (b.pz - a.pz) * b.ux) / cross;
                float vx = a.px + a.ux * t, vz = a.pz + a.uz * t;
                if ((vx - at.x) * (vx - at.x) + (vz - at.z) * (vz - at.z) < 9f) { poly.Add((vx, vz)); continue; }
            }
            // Nearly parallel walls (a step in the front): a short wall across between them
            poly.Add(On(runs[k].line, at));
            poly.Add(On(bRun.line, at));
        }
        poly.Add(poly[0]);
        return poly;
    }

    // ---- Straightening a border ----

    // The steps of the cell corners become straight walls: the points that matter (Douglas-Peucker, three quarters of a cell: the steps of a slant go, a real corner stays)
    static List<(float x, float z)> Straighten(List<(float x, float z)> pts, float tol)
    {
        if (pts.Count <= 2) return pts;
        var keep = new bool[pts.Count];
        keep[0] = keep[pts.Count - 1] = true;
        Dp(pts, 0, pts.Count - 1, keep, tol);
        var r = new List<(float x, float z)>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) r.Add(pts[i]);
        return r;
    }

    // A loop: split at its first point and the point furthest from it
    static List<(float x, float z)> StraightenLoop(List<(float x, float z)> pts, float tol)
    {
        int n = pts.Count - 1;   // the last is the first again
        if (n < 4) return pts;
        int far = 0;
        float best = -1f;
        for (int i = 1; i < n; i++)
        {
            float dx = pts[i].x - pts[0].x, dz = pts[i].z - pts[0].z, d = dx * dx + dz * dz;
            if (d > best) { best = d; far = i; }
        }
        var keep = new bool[pts.Count];
        keep[0] = keep[far] = keep[n] = true;
        Dp(pts, 0, far, keep, tol);
        Dp(pts, far, n, keep, tol);
        var r = new List<(float x, float z)>();
        for (int i = 0; i <= n; i++) if (keep[i]) r.Add(pts[i]);
        return r;
    }

    static void Dp(List<(float x, float z)> pts, int a, int b, bool[] keep, float tolerance)
    {
        if (b <= a + 1) return;
        float best = -1f;
        int at = -1;
        for (int i = a + 1; i < b; i++)
        {
            float d = DistSeg(pts[i].x, pts[i].z, pts[a].x, pts[a].z, pts[b].x, pts[b].z, out _, out _);
            if (d > best) { best = d; at = i; }
        }
        if (best <= tolerance) return;
        keep[at] = true;
        Dp(pts, a, at, keep, tolerance);
        Dp(pts, at, b, keep, tolerance);
    }

    // ---- Putting a house up ----

    // detail: 2 the full facade, 1 lean, 0 plain walls and roof (far away)
    void BuildHouse(HouseOutline o, HouseOutline[] all, TvMesh m, int detail)
    {
        TvCityShapes.Palette col = o.colour;
        float y0 = o.baseY, top = o.top, foot = y0 + o.plinth;
        float groundFloor = GroundFloor(o), upper = UpperFloor(o);

        // The roof first: it says whether the walls get gables
        List<(float x, float z)> roofCorners = o.rings.Count == 1 ? Corners(o.rings[0]) : null;
        bool four = roofCorners != null && roofCorners.Count == 4 && Convex(roofCorners);
        bool pitched = four && o.kind != TvCityShapes.Kind.Hall && o.kind != TvCityShapes.Kind.School
                       && (o.kind != TvCityShapes.Kind.Apartments || o.look % 3 == 0);
        o.flat = !pitched;

        for (int r = 0; r < o.rings.Count; r++)
        {
            foreach (HouseSeg sg in o.rings[r])
            {
                float dx = sg.bx - sg.ax, dz = sg.bz - sg.az, len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 0.05f) continue;
                float ux = dx / len, uz = dz / len, nx = uz, nz = -ux;   // along the wall; out of the house (the house is on the left)
                if (sg.other > 0)
                {
                    // Against a neighbour: only what stands above its roof
                    HouseOutline n = all[sg.other - 1];
                    float from = n != null ? n.top + (n.flat ? 0.4f : 0f) : y0;
                    if (n != null && !n.flat) from = n.top;   // under its roof's slope the wall is hidden anyway
                    if (top > from + 0.05f)
                    {
                        var look = WallLook(o);
                        PaintOn(look.tile, look.size, look.size);
                        WallQuad(m, sg.ax, sg.az, sg.bx, sg.bz, nx, nz, Math.Max(y0, from - 0.3f), top, look.color.Shade(0.95f));
                        PaintOff();
                    }
                    continue;
                }
                Facade(m, o, sg, len, ux, uz, nx, nz, detail, groundFloor, upper);
            }
        }
        if (pitched) PitchedRoof(m, o, roofCorners, detail);
        else FlatRoof(m, o, detail);
    }

    // ---- Pictures on the walls and roofs (2026-11-25, Anders: more detail on the sprint houses) ----
    // While paint is a tile of the atlas (TvAtlas: plaster, brick, boards, roof tiles ...), every face made gets that
    // picture, laid flat on it: u along the face, v up it (up the slope on a roof), paintSu x paintSv metres per
    // picture, shifted by paintOu, paintOv pictures. -1: plain colour as before. Thread-static, as pieces of the
    // town are built on several threads at once.
    [ThreadStatic] static int paintTile;
    [ThreadStatic] static bool painting;
    [ThreadStatic] static float paintSu, paintSv, paintOu, paintOv;

    static void PaintOn(int tile, float su, float sv, float ou = 0f, float ov = 0f)
    {
        painting = true; paintTile = tile; paintSu = su; paintSv = sv; paintOu = ou; paintOv = ov;
    }

    static void PaintOff() => painting = false;

    // The u, v of the corners from `first` on, from their place and normal
    static void Paint(TvMesh m, int first)
    {
        if (!painting) return;
        m.PadUvs(first);
        for (int i = first; i < m.VertexCount; i++)
        {
            float x = m.positions[i * 3], y = m.positions[i * 3 + 1], z = m.positions[i * 3 + 2];
            float nx = m.normals[i * 3], ny = m.normals[i * 3 + 1], nz = m.normals[i * 3 + 2];
            float u, v;
            if (ny > 0.97f || ny < -0.97f) { u = x; v = z; }
            else
            {
                float h = (float)Math.Sqrt(nx * nx + nz * nz);
                float tx = -nz / h, tz = nx / h;
                // Up the face: straight up on a wall, up the slope on a roof
                float bx = -nx * ny, by = nx * nx + nz * nz, bz = -nz * ny;
                float bl = (float)Math.Sqrt(bx * bx + by * by + bz * bz);
                u = x * tx + z * tz;
                v = (x * bx + y * by + z * bz) / bl;
            }
            m.uvs.Add(u / paintSu - paintOu); m.uvs.Add(v / paintSv - paintOv); m.uvs.Add(paintTile);
        }
    }

    // One flat face of a wall from a to b, from y0 to y1, set out d metres from the wall line, facing (nx, nz)
    static void WallQuad(TvMesh m, float ax, float az, float bx, float bz, float nx, float nz, float y0, float y1, TvColor c, float d = 0f)
    {
        ax += nx * d; az += nz * d; bx += nx * d; bz += nz * d;
        Face(m, ax, y0, az, bx, y0, bz, bx, y1, bz, ax, y1, az, nx, 0f, nz, c);
    }

    // A quad a b c d (in order round it) facing (nx, ny, nz)
    static void Face(TvMesh m, float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, float dx, float dy, float dz,
                     float nx, float ny, float nz, TvColor col)
    {
        float ex = bx - ax, ey = by - ay, ez = bz - az, fx = cx - ax, fy = cy - ay, fz = cz - az;
        float qx = ey * fz - ez * fy, qy = ez * fx - ex * fz, qz = ex * fy - ey * fx;
        bool flip = qx * nx + qy * ny + qz * nz < 0f;
        int a = m.Vertex(ax, ay, az, nx, ny, nz, col), b = m.Vertex(bx, by, bz, nx, ny, nz, col);
        int q = m.Vertex(cx, cy, cz, nx, ny, nz, col), d = m.Vertex(dx, dy, dz, nx, ny, nz, col);
        Paint(m, a);
        if (flip) { m.triangles.Add(a); m.triangles.Add(q); m.triangles.Add(b); m.triangles.Add(a); m.triangles.Add(d); m.triangles.Add(q); }
        else { m.triangles.Add(a); m.triangles.Add(b); m.triangles.Add(q); m.triangles.Add(a); m.triangles.Add(q); m.triangles.Add(d); }
    }

    static void Tri(TvMesh m, float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, float nx, float ny, float nz, TvColor col)
    {
        float ex = bx - ax, ey = by - ay, ez = bz - az, fx = cx - ax, fy = cy - ay, fz = cz - az;
        float qx = ey * fz - ez * fy, qy = ez * fx - ex * fz, qz = ex * fy - ey * fx;
        bool flip = qx * nx + qy * ny + qz * nz < 0f;
        int a = m.Vertex(ax, ay, az, nx, ny, nz, col), b = m.Vertex(bx, by, bz, nx, ny, nz, col), q = m.Vertex(cx, cy, cz, nx, ny, nz, col);
        Paint(m, a);
        if (flip) { m.triangles.Add(a); m.triangles.Add(q); m.triangles.Add(b); }
        else { m.triangles.Add(a); m.triangles.Add(b); m.triangles.Add(q); }
    }

    // A box on a wall: from u0 to u1 along it, y0 to y1 up, d0 to d1 out from it (front, top and the two ends)
    static void WallBox(TvMesh m, HouseSeg sg, float ux, float uz, float nx, float nz, float u0, float u1, float y0, float y1, float d0, float d1, TvColor c, bool topToo = true)
    {
        float ax = sg.ax + ux * u0, az = sg.az + uz * u0, bx = sg.ax + ux * u1, bz = sg.az + uz * u1;
        // Front
        Face(m, ax + nx * d1, y0, az + nz * d1, bx + nx * d1, y0, bz + nz * d1, bx + nx * d1, y1, bz + nz * d1, ax + nx * d1, y1, az + nz * d1, nx, 0f, nz, c);
        if (d1 - d0 < 0.015f) return;
        TvColor side = c.Shade(0.9f);
        // Top and bottom
        if (topToo) Face(m, ax + nx * d0, y1, az + nz * d0, bx + nx * d0, y1, bz + nz * d0, bx + nx * d1, y1, bz + nz * d1, ax + nx * d1, y1, az + nz * d1, 0f, 1f, 0f, c.Shade(1.04f));
        Face(m, ax + nx * d0, y0, az + nz * d0, bx + nx * d0, y0, bz + nz * d0, bx + nx * d1, y0, bz + nz * d1, ax + nx * d1, y0, az + nz * d1, 0f, -1f, 0f, side.Shade(0.8f));
        // Ends
        Face(m, ax + nx * d0, y0, az + nz * d0, ax + nx * d1, y0, az + nz * d1, ax + nx * d1, y1, az + nz * d1, ax + nx * d0, y1, az + nz * d0, -ux, 0f, -uz, side);
        Face(m, bx + nx * d0, y0, bz + nz * d0, bx + nx * d1, y0, bz + nz * d1, bx + nx * d1, y1, bz + nz * d1, bx + nx * d0, y1, bz + nz * d0, ux, 0f, uz, side);
    }

    // What a house's walls are made of: the picture, its size in metres and the colour multiplied on it. Rendered
    // walls keep the house's own colour; brick shows its own red; boards take the paint colour.
    (int tile, float size, TvColor color) WallLook(HouseOutline o)
    {
        TvColor wall = o.colour.wall, brick = new TvColor(236, 230, 224);
        switch (o.kind)
        {
            case TvCityShapes.Kind.Hall: return (TvAtlas.MetalSheet, 2f, wall);
            case TvCityShapes.Kind.School: return (TvAtlas.Brick, 1.2f, brick);
            case TvCityShapes.Kind.Villa: return Old ? (TvAtlas.Plaster, 2f, wall) : (TvAtlas.WoodPanel, 2f, wall);
            case TvCityShapes.Kind.Apartments:
                return o.look % 3 == 0 ? (TvAtlas.Brick, 1.2f, brick) : o.look % 3 == 1 ? (TvAtlas.Plaster, 2f, wall) : (TvAtlas.Concrete, 3f, wall);
        }
        if (o.row) return o.look % 2 == 0 ? (TvAtlas.WoodPanel, 2f, wall) : (TvAtlas.Brick, 1.2f, brick);
        return !Old && o.look % 4 == 1 ? (TvAtlas.Brick, 1.2f, brick) : (TvAtlas.Plaster, 2f, wall);
    }

    // The facade of one outside wall
    void Facade(TvMesh m, HouseOutline o, HouseSeg sg, float len, float ux, float uz, float nx, float nz, int detail, float groundFloor, float upper)
    {
        TvCityShapes.Palette col = o.colour;
        float y0 = o.baseY, top = o.top, foot = y0 + o.plinth;
        // The wall (plaster, brick, boards, sheet metal or concrete), and the stone foot of it
        var look = WallLook(o);
        PaintOn(look.tile, look.size, look.size);
        WallQuad(m, sg.ax, sg.az, sg.bx, sg.bz, nx, nz, foot, top, look.color);
        PaintOn(TvAtlas.Concrete, 2f, 2f);
        WallQuad(m, sg.ax, sg.az, sg.bx, sg.bz, nx, nz, y0 - 0.4f, foot, Old ? col.foot : TvCityShapes.PaletteOf(0, input.town.style, o.kind).foot.Shade(0.95f));
        PaintOff();
        if (detail == 0) return;
        // A dark band of splash at the foot (the old town: its painted band)
        WallBox(m, sg, ux, uz, nx, nz, 0f, len, foot, foot + (Old ? 0.9f : 0.45f), 0f, 0.025f, Old ? col.foot : col.wall.Shade(0.75f), false);
        // The cornice: a band that stands out under the roof, a little past the ends so the corners close
        WallBox(m, sg, ux, uz, nx, nz, -0.2f, len + 0.2f, top - 0.35f, top, 0f, detail == 2 ? 0.22f : 0.12f, col.band);

        // What the street floor looks out on: a street or square (shops and doors) or a yard
        float midX = (sg.ax + sg.bx) / 2f + nx * 2.5f, midZ = (sg.az + sg.bz) / 2f + nz * 2.5f;
        bool street = Base(CellAt(midX, midZ)) == WorldCell.Paved;
        bool shops = street && !Old && o.kind == TvCityShapes.Kind.TownHouse && o.look % 3 != 0 && len >= 3.4f;
        bool hall = o.kind == TvCityShapes.Kind.Hall, school = o.kind == TvCityShapes.Kind.School;
        bool flats = o.kind == TvCityShapes.Kind.Apartments;

        // A hall's walls of ribbed sheet metal: in its picture (WallLook)

        // Windows: one every 2.4 m (a big one every 4 m on a hall, wide bands on a school), none on a wall shorter than 1.6 m
        float spacing = hall ? 4.2f : school ? 3.0f : Old ? 2.8f : 2.4f;
        int count = (int)((len - 0.6f) / spacing);
        if (count < 1) return;
        float step = len / count;
        // The door: on the street floor of the longest street wall... one per wall that is long enough, from the place
        // (a school, a block of flats and a terraced home have doors towards their yard and garden too)
        bool doorHere = (street || o.row || school || (flats && len >= 8f)) && len >= 2.2f;
        int door = doorHere ? (int)(TvRandom.Hash((int)(sg.ax + ox), (int)(sg.az + oz)) * count) : -1;
        float ww = hall ? 2.6f : school ? 2.6f : o.row ? 1.2f : Old ? 0.85f : 1.0f, wh = hall ? 1.2f : school ? 1.65f : o.row ? 1.3f : Old ? 1.25f : 1.55f;
        for (int f = 0; f < o.floors; f++)
        {
            float fy = f == 0 ? foot : foot + groundFloor + upper * (f - 1);
            if (fy + 1.4f > top) break;
            if (f > 0 && f > 1 && detail == 2 && !Old)
                WallBox(m, sg, ux, uz, nx, nz, 0f, len, fy - 0.15f, fy - 0.03f, 0f, 0.05f, col.band, true);   // a thin band between floors
            for (int k = 0; k < count; k++)
            {
                float u = step * (k + 0.5f);
                if (f == 0)
                {
                    if (k == door)
                    {
                        // The front door in its own colour, with a light above and a step
                        WallBox(m, sg, ux, uz, nx, nz, u - 0.7f, u + 0.7f, foot, foot + 2.9f, 0f, 0.06f, col.trim);
                        WallBox(m, sg, ux, uz, nx, nz, u - 0.55f, u + 0.55f, foot, foot + 2.25f, 0f, 0.1f, col.door);
                        if (detail == 2)
                        {
                            WallBox(m, sg, ux, uz, nx, nz, u - 0.45f, u + 0.45f, foot + 2.35f, foot + 2.75f, 0f, 0.09f, col.glass);
                            WallBox(m, sg, ux, uz, nx, nz, u - 0.9f, u + 0.9f, foot - 0.3f, foot, 0f, 0.55f, TvColor.Hex("#9c968c"));
                        }
                        // A roof over the entrance of a school, a block of flats or a terraced home
                        if (detail >= 1 && (school || flats || o.row))
                            WallBox(m, sg, ux, uz, nx, nz, u - (school ? 2.2f : 1.1f), u + (school ? 2.2f : 1.1f), foot + 2.95f, foot + 3.12f, 0f, school ? 2.0f : 1.1f, TvColor.Hex("#3b3f45"));
                        continue;
                    }
                    if (shops && step >= 2.6f)
                    {
                        // A shop window, and over it a sign and (now and then) an awning
                        float sw = Math.Min(step - 0.6f, 2.4f);
                        WallBox(m, sg, ux, uz, nx, nz, u - sw / 2f - 0.1f, u + sw / 2f + 0.1f, foot + 0.35f, foot + 2.75f, 0f, 0.05f, TvColor.Hex("#2a2a28"));
                        WallBox(m, sg, ux, uz, nx, nz, u - sw / 2f, u + sw / 2f, foot + 0.45f, foot + 2.65f, 0f, 0.07f, col.shop);
                        if (detail == 2)
                        {
                            TvColor board = (k + o.look) % 2 == 0 ? TvColor.Hex("#1f2422") : col.awning.Shade(0.8f);
                            WallBox(m, sg, ux, uz, nx, nz, u - sw / 2f, u + sw / 2f, foot + 2.95f, foot + 3.3f, 0f, 0.08f, board);
                            int letters = 4 + (k + o.look) % 4;
                            for (int q = 0; q < letters; q++)
                            {
                                float lu = u - sw / 2f + 0.25f + (sw - 0.5f) * (q + 0.5f) / letters;
                                WallBox(m, sg, ux, uz, nx, nz, lu - 0.07f, lu + 0.07f, foot + 3.03f, foot + 3.22f, 0.08f, 0.1f, board.r < 60 ? TvColor.Hex("#d4b04c") : TvColor.Hex("#f1eee6"), false);
                            }
                        }
                        if ((k + o.look) % 3 != 1 && StreetAhead(sg, u, ux, uz, nx, nz, 3f))
                        {
                            // An awning sloping out over the pavement
                            float ax = sg.ax + ux * (u - sw / 2f - 0.1f), az = sg.az + uz * (u - sw / 2f - 0.1f);
                            float bx = sg.ax + ux * (u + sw / 2f + 0.1f), bz = sg.az + uz * (u + sw / 2f + 0.1f);
                            TvColor aw = TvCityShapes.PaletteOf(o.look + k * 3, input.town.style, o.kind).awning;
                            Face(m, ax, foot + 2.9f, az, bx, foot + 2.9f, bz, bx + nx, foot + 2.5f, bz + nz, ax + nx, foot + 2.5f, az + nz, nx * 0.4f, 0.9f, nz * 0.4f, aw);
                            Face(m, ax, foot + 2.9f, az, bx, foot + 2.9f, bz, bx + nx, foot + 2.5f, bz + nz, ax + nx, foot + 2.5f, az + nz, -nx * 0.4f, -0.9f, -nz * 0.4f, aw.Shade(0.6f));
                            Face(m, ax + nx, foot + 2.5f, az + nz, bx + nx, foot + 2.5f, bz + nz, bx + nx, foot + 2.3f, bz + nz, ax + nx, foot + 2.3f, az + nz, nx, 0f, nz, aw.Shade(0.9f));
                        }
                        continue;
                    }
                }
                Window(m, sg, ux, uz, nx, nz, u, f == 0 ? foot + 0.9f : fy + 0.85f, ww, wh, col, detail, f > 0 && street && !school);
                // A block of flats: balconies on its yard side, every other window from the first floor up
                if (flats && f > 0 && !street && detail >= 1 && k % 2 == 0 && step >= 2.0f)
                {
                    float bw = Math.Min(3.2f, step * 1.3f);
                    WallBox(m, sg, ux, uz, nx, nz, u - bw / 2f, u + bw / 2f, fy - 0.12f, fy + 0.04f, 0f, 1.4f, TvColor.Hex("#c9c6bf"));
                    WallBox(m, sg, ux, uz, nx, nz, u - bw / 2f, u + bw / 2f, fy + 0.04f, fy + 1.05f, 1.34f, 1.4f, col.awning);
                    if (detail == 2)
                        foreach (float side in new[] { -1f, 1f })
                            WallBox(m, sg, ux, uz, nx, nz, u + side * bw / 2f - 0.03f, u + side * bw / 2f + 0.03f, fy + 0.04f, fy + 1.05f, 0f, 1.4f, col.awning.Shade(0.9f));
                }
            }
        }
        // A drainpipe at each end of a long street wall
        if (detail == 2 && street && len > 6f)
        {
            foreach (float u in new[] { 0.35f, len - 0.35f })
                WallBox(m, sg, ux, uz, nx, nz, u - 0.06f, u + 0.06f, foot, top - 0.35f, 0.05f, 0.17f, TvColor.Hex("#6d7275"));
        }
    }

    // A window: a frame, the glass, glazing bars and a sill (lean: the frame and the glass only)
    void Window(TvMesh m, HouseSeg sg, float ux, float uz, float nx, float nz, float u, float y, float w, float h, TvCityShapes.Palette col, int detail, bool flowersMaybe)
    {
        WallBox(m, sg, ux, uz, nx, nz, u - w / 2f - 0.09f, u + w / 2f + 0.09f, y - 0.09f, y + h + 0.09f, 0f, 0.03f, Old ? col.trim : col.trim, false);
        bool lit = TvRandom.Hash((int)((sg.ax + ox) * 3f + u * 7f), (int)((sg.az + oz) * 3f + y * 5f)) < 0.16f;
        // The pane: the sky mirrored in it and the room behind (a lit room warm)
        float left = (sg.ax + ux * (u - w / 2f)) * ux + (sg.az + uz * (u - w / 2f)) * uz;
        PaintOn(TvAtlas.Glass, w, h, left / w, y / h);
        WallBox(m, sg, ux, uz, nx, nz, u - w / 2f, u + w / 2f, y, y + h, 0f, 0.045f, lit ? TvColor.Hex("#f0dca0") : col.glass.Lerp(new TvColor(255, 255, 255), 0.55f), false);
        PaintOff();
        if (detail < 2) return;
        // Glazing bars and a sill
        WallBox(m, sg, ux, uz, nx, nz, u - 0.03f, u + 0.03f, y, y + h, 0.045f, 0.06f, col.trim, false);
        WallBox(m, sg, ux, uz, nx, nz, u - w / 2f, u + w / 2f, y + h * 0.62f - 0.03f, y + h * 0.62f + 0.03f, 0.045f, 0.06f, col.trim, false);
        WallBox(m, sg, ux, uz, nx, nz, u - w / 2f - 0.13f, u + w / 2f + 0.13f, y - 0.16f, y - 0.09f, 0f, 0.13f, col.trim);
        if (col.shutter.HasValue)
            foreach (float s in new[] { -1f, 1f })
                WallBox(m, sg, ux, uz, nx, nz, u + s * (w / 2f + 0.12f) - 0.2f, u + s * (w / 2f + 0.12f) + 0.2f, y - 0.04f, y + h + 0.04f, 0f, 0.07f, col.shutter.Value);
        // Flowers in a box under some windows towards the street
        if (flowersMaybe && TvRandom.Hash((int)((sg.ax + ox) * 5f + u * 3f), (int)((sg.az + oz) * 5f + y * 7f)) < 0.25f)
        {
            WallBox(m, sg, ux, uz, nx, nz, u - w / 2f - 0.04f, u + w / 2f + 0.04f, y - 0.4f, y - 0.17f, 0f, 0.28f, TvColor.Hex("#3d5a3a"));
            TvColor bloom = ((int)(u * 7f + y)) % 2 == 0 ? TvColor.Hex("#e23d5a") : TvColor.Hex("#f2c230");
            WallBox(m, sg, ux, uz, nx, nz, u - w / 2f, u + w / 2f, y - 0.17f, y - 0.05f, 0.03f, 0.26f, bloom);
        }
    }

    // Paving ahead of a spot on a wall for at least this far (an awning must not reach over a garden or a wall)
    bool StreetAhead(HouseSeg sg, float u, float ux, float uz, float nx, float nz, float reach)
    {
        for (float s = 0.6f; s <= reach; s += 0.8f)
        {
            float x = sg.ax + ux * u + nx * s, z = sg.az + uz * u + nz * s;
            if (Base(CellAt(x, z)) != WorldCell.Paved) return false;
        }
        return true;
    }

    // ---- Roofs ----

    // The corners of a ring that turn (more than 10 degrees): the shape of the roof
    static List<(float x, float z)> Corners(List<HouseSeg> ring)
    {
        var pts = new List<(float x, float z)>();
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            HouseSeg a = ring[(i + n - 1) % n], b = ring[i];
            float ax = a.bx - a.ax, az = a.bz - a.az, bx = b.bx - b.ax, bz = b.bz - b.az;
            float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz);
            if (la < 1e-4f || lb < 1e-4f) continue;
            float cross = (ax * bz - az * bx) / (la * lb);
            float dot = (ax * bx + az * bz) / (la * lb);
            if (Math.Abs(cross) < 0.17f && dot > 0f) continue;   // straight on
            pts.Add((b.ax, b.az));
        }
        return pts;
    }

    static bool Convex(List<(float x, float z)> p)
    {
        int n = p.Count;
        for (int i = 0; i < n; i++)
        {
            var a = p[i]; var b = p[(i + 1) % n]; var c = p[(i + 2) % n];
            if ((b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x) < 0f) return false;
        }
        return true;
    }

    // How much of the roof edge from a to b is against a neighbour (0..1)
    static float SharedShare(HouseOutline o, (float x, float z) a, (float x, float z) b)
    {
        float dx = b.x - a.x, dz = b.z - a.z, len2 = dx * dx + dz * dz;
        if (len2 < 1e-4f) return 0f;
        float shared = 0f, total = 0f;
        foreach (HouseSeg sg in o.rings[0])
        {
            float mx = (sg.ax + sg.bx) / 2f, mz = (sg.az + sg.bz) / 2f;
            float d = DistSeg(mx, mz, a.x, a.z, b.x, b.z, out _, out _);
            if (d > 0.3f) continue;
            float l = (float)Math.Sqrt((sg.bx - sg.ax) * (sg.bx - sg.ax) + (sg.bz - sg.az) * (sg.bz - sg.az));
            total += l;
            if (sg.other > 0) shared += l;
        }
        return total > 0f ? shared / total : 0f;
    }

    // A pitched roof on four corners (in order round the house): the ridge between the middles of two opposite sides,
    // which become gables (walls up to the ridge) where the house meets its neighbours; a hip roof when it stands alone
    void PitchedRoof(TvMesh m, HouseOutline o, List<(float x, float z)> q, int detail)
    {
        TvCityShapes.Palette col = o.colour;
        float[] side = new float[4], shared = new float[4];
        for (int i = 0; i < 4; i++)
        {
            var a = q[i]; var b = q[(i + 1) % 4];
            side[i] = (float)Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
            shared[i] = SharedShare(o, a, b);
        }
        // Gables on the pair of opposite sides that are more against neighbours; else on the shorter pair
        float s0 = shared[0] + shared[2], s1 = shared[1] + shared[3];
        int g = Math.Abs(s0 - s1) > 0.3f ? (s0 > s1 ? 0 : 1) : (side[0] + side[2] <= side[1] + side[3] ? 0 : 1);
        bool alone = s0 + s1 < 0.05f;
        var c0 = q[g]; var c1 = q[(g + 1) % 4]; var c2 = q[(g + 2) % 4]; var c3 = q[(g + 3) % 4];
        // Gable sides: c0-c1 and c2-c3; long sides: c1-c2 and c3-c0
        (float x, float z) mA = ((c0.x + c1.x) / 2f, (c0.z + c1.z) / 2f), mB = ((c2.x + c3.x) / 2f, (c2.z + c3.z) / 2f);
        float depth = (side[g] + side[(g + 2) % 4]) / 2f, length = (float)Math.Sqrt((mB.x - mA.x) * (mB.x - mA.x) + (mB.z - mA.z) * (mB.z - mA.z));
        float pitch = Old ? 0.4f : o.kind == TvCityShapes.Kind.Villa ? 0.75f : 0.65f;
        float rise = Math.Max(1.0f, Math.Min(depth / 2f * pitch, Old ? 2.6f : 4.6f));
        float eave = o.top, ridge = eave + rise;
        // A house alone gets a hip roof: the ridge stops short of the ends (a point on a square house)
        if (alone && length > 0.1f)
        {
            float cut = Math.Min(depth / 2f, length / 2f) / length;
            var a2 = (mA.x + (mB.x - mA.x) * cut, mA.z + (mB.z - mA.z) * cut);
            var b2 = (mB.x + (mA.x - mB.x) * cut, mB.z + (mA.z - mB.z) * cut);
            mA = a2; mB = b2;
        }
        // The roof's slopes from the long sides to the ridge, reaching a little out over the street (not over a
        // neighbour), with the underside dark
        float over = detail == 0 ? 0f : 0.3f;
        void Slope((float x, float z) a, (float x, float z) b, (float x, float z) ra, (float x, float z) rb, bool outward)
        {
            float dx = b.x - a.x, dz = b.z - a.z, l = (float)Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-3f) return;
            float nx = dz / l, nz = -dx / l;   // out of the house
            float o2 = outward ? over : 0f, drop = o2 * rise / Math.Max(0.5f, depth / 2f);
            float ax = a.x + nx * o2, az = a.z + nz * o2, bx = b.x + nx * o2, bz = b.z + nz * o2;
            float ny = depth / 2f, upx = nx * rise, upz = nz * rise;
            float len = (float)Math.Sqrt(upx * upx + ny * ny + upz * upz);
            float rnx = upx / len, rny = ny / len, rnz = upz / len;
            PaintOn(TvAtlas.RoofTiles, 2f, 2f);
            Face(m, ax, eave - drop, az, bx, eave - drop, bz, rb.x, ridge, rb.z, ra.x, ridge, ra.z, rnx, rny, rnz, col.roof.Shade(1.2f));
            PaintOff();
            if (o2 > 0f) Face(m, ax, eave - drop - 0.02f, az, bx, eave - drop - 0.02f, bz, rb.x, ridge - 0.02f, rb.z, ra.x, ridge - 0.02f, ra.z, -rnx, -rny, -rnz, col.roof.Shade(0.5f));
        }
        bool Out((float x, float z) a, (float x, float z) b) => SharedShare(o, a, b) < 0.5f;
        Slope(c1, c2, mA, mB, Out(c1, c2));
        Slope(c3, c0, mB, mA, Out(c3, c0));
        if (alone)
        {
            // The hip ends: sloping triangles
            void Hip((float x, float z) a, (float x, float z) b, (float x, float z) r)
            {
                float dx = b.x - a.x, dz = b.z - a.z, l = (float)Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-3f) return;
                float nx = dz / l, nz = -dx / l;
                PaintOn(TvAtlas.RoofTiles, 2f, 2f);
                Tri(m, a.x, eave, a.z, b.x, eave, b.z, r.x, ridge, r.z, nx * 0.6f, 0.8f, nz * 0.6f, col.roof.Shade(1.12f));
                PaintOff();
            }
            Hip(c0, c1, mA);
            Hip(c2, c3, mB);
        }
        else
        {
            // Gables: the wall goes on up to the ridge
            void Gable((float x, float z) a, (float x, float z) b, (float x, float z) r)
            {
                float dx = b.x - a.x, dz = b.z - a.z, l = (float)Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-3f) return;
                Tri(m, a.x, eave, a.z, b.x, eave, b.z, r.x, ridge, r.z, dz / l, 0f, -dx / l, col.wall.Shade(0.97f));
            }
            Gable(c0, c1, mA);
            Gable(c2, c3, mB);
        }
        if (detail < 2) return;
        // A chimney or two on the ridge
        int chimneys = length > 10f ? 2 : 1;
        for (int i = 0; i < chimneys; i++)
        {
            float t = chimneys == 1 ? 0.6f : 0.25f + 0.5f * i;
            float x = mA.x + (mB.x - mA.x) * t, z = mA.z + (mB.z - mA.z) * t;
            TvColor brick = o.look % 2 == 0 ? TvColor.Hex("#8e4a35") : col.wall.Shade(0.85f);
            var b = new TvShapeBuilder(random);
            b.Box(-0.4f, 0f, -0.35f, 0.4f, rise * 0.25f + 1.2f, 0.35f, brick);
            b.Box(-0.5f, rise * 0.25f + 1.2f, -0.45f, 0.5f, rise * 0.25f + 1.35f, 0.45f, TvColor.Hex("#3a3a3a"));
            m.Add(b.Done(), x, ridge - rise * 0.25f - 0.6f, z, 1f, 1f, 1f, -(float)Math.Atan2(mB.z - mA.z, mB.x - mA.x));
        }
    }

    // A flat roof with a low parapet round its edge towards the street
    void FlatRoof(TvMesh m, HouseOutline o, int detail)
    {
        TvCityShapes.Palette col = o.colour;
        TvColor felt = Old ? TvColor.Hex("#b9a58a") : TvColor.Hex("#56595a");
        float y = o.top;
        // The roof: the outline cut into triangles (holes joined to the outside first)
        var outer = new List<(float x, float z)>();
        foreach (HouseSeg sg in o.rings[0]) outer.Add((sg.ax, sg.az));
        var holes = new List<List<(float x, float z)>>();
        for (int r = 1; r < o.rings.Count; r++)
        {
            var hole = new List<(float x, float z)>();
            foreach (HouseSeg sg in o.rings[r]) hole.Add((sg.ax, sg.az));
            holes.Add(hole);
        }
        var poly = JoinHoles(outer, holes);
        PaintOn(TvAtlas.Concrete, 3f, 3f);
        foreach (var (a, b, c) in EarClip(poly))
            Tri(m, a.x, y, a.z, b.x, y, b.z, c.x, y, c.z, 0f, 1f, 0f, felt);
        PaintOff();
        if (detail == 0) return;
        // The parapet: a low wall on the edge, its inside and top
        foreach (var ring in o.rings)
            foreach (HouseSeg sg in ring)
            {
                if (sg.other > 0)
                {
                    HouseOutline n = sg.other - 1 < 0 ? null : null;
                }
                float dx = sg.bx - sg.ax, dz = sg.bz - sg.az, len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 0.05f) continue;
                float ux = dx / len, uz = dz / len, nx = uz, nz = -ux;
                TvColor edge = o.kind == TvCityShapes.Kind.School || o.kind == TvCityShapes.Kind.Hall ? col.roof : col.band;
                WallQuad(m, sg.ax, sg.az, sg.bx, sg.bz, nx, nz, y, y + 0.45f, edge);
                WallQuad(m, sg.ax, sg.az, sg.bx, sg.bz, -nx, -nz, y, y + 0.45f, col.wall.Shade(0.85f), -0.2f);
                Face(m, sg.ax, y + 0.45f, sg.az, sg.bx, y + 0.45f, sg.bz, sg.bx - nx * 0.2f, y + 0.45f, sg.bz - nz * 0.2f, sg.ax - nx * 0.2f, y + 0.45f, sg.az - nz * 0.2f, 0f, 1f, 0f, edge.Shade(1.05f));
            }
        if (detail < 2) return;
        // A box for the stairs or a fan, now and then
        if (Math.Abs(o.area) > 60f && o.look % 2 == 0)
        {
            var b = new TvShapeBuilder(random);
            b.Box(-1.1f, 0f, -0.9f, 1.1f, 2.2f, 0.9f, col.wall.Shade(0.8f));
            if (Inside(o.cx, o.cz, 0f) && PointIn(outer, o.cx, o.cz)) m.Add(b.Done(), o.cx, y, o.cz, 1f, 1f, 1f, 0f);
        }
    }

    static bool PointIn(List<(float x, float z)> poly, float x, float z)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if ((poly[i].z > z) != (poly[j].z > z) && x < (poly[j].x - poly[i].x) * (z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    // Each hole joined to the outside by a slit from its point furthest right to the nearest corner of the outside
    static List<(float x, float z)> JoinHoles(List<(float x, float z)> outer, List<List<(float x, float z)>> holes)
    {
        var poly = new List<(float x, float z)>(outer);
        holes.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
        foreach (var hole in holes)
        {
            if (hole.Count < 3) continue;
            int hi = 0;
            for (int i = 1; i < hole.Count; i++) if (hole[i].x > hole[hi].x) hi = i;
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                float dx = poly[i].x - hole[hi].x, dz = poly[i].z - hole[hi].z, d = dx * dx + dz * dz;
                if (dx >= -0.01f && d < bd) { bd = d; best = i; }
            }
            if (bd == float.MaxValue)
                for (int i = 0; i < poly.Count; i++)
                {
                    float dx = poly[i].x - hole[hi].x, dz = poly[i].z - hole[hi].z, d = dx * dx + dz * dz;
                    if (d < bd) { bd = d; best = i; }
                }
            var joined = new List<(float x, float z)>(poly.Count + hole.Count + 2);
            for (int i = 0; i <= best; i++) joined.Add(poly[i]);
            for (int k = 0; k <= hole.Count; k++) joined.Add(hole[(hi + k) % hole.Count]);
            for (int i = best; i < poly.Count; i++) joined.Add(poly[i]);
            poly = joined;
        }
        return poly;
    }

    static float MaxX(List<(float x, float z)> p)
    {
        float m = float.MinValue;
        foreach (var q in p) m = Math.Max(m, q.x);
        return m;
    }

    // Triangles for a simple polygon turning left (ear clipping); gives up on what is left after many tries
    static List<((float x, float z) a, (float x, float z) b, (float x, float z) c)> EarClip(List<(float x, float z)> poly)
    {
        var result = new List<((float x, float z), (float x, float z), (float x, float z))>();
        var idx = new List<int>();
        for (int i = 0; i < poly.Count; i++) idx.Add(i);
        // Turning left (positive area) as the houses' outside rings do
        float area = 0f;
        for (int i = 0; i < poly.Count; i++) { var a = poly[i]; var b = poly[(i + 1) % poly.Count]; area += a.x * b.z - b.x * a.z; }
        if (area < 0f) idx.Reverse();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 5000)
        {
            bool cut = false;
            for (int k = 0; k < idx.Count; k++)
            {
                var a = poly[idx[(k + idx.Count - 1) % idx.Count]]; var b = poly[idx[k]]; var c = poly[idx[(k + 1) % idx.Count]];
                float cross = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
                if (cross <= 1e-5f) continue;
                bool empty = true;
                for (int q = 0; q < idx.Count && empty; q++)
                {
                    int v = idx[q];
                    if (v == idx[(k + idx.Count - 1) % idx.Count] || v == idx[k] || v == idx[(k + 1) % idx.Count]) continue;
                    var p = poly[v];
                    if ((p.x == a.x && p.z == a.z) || (p.x == b.x && p.z == b.z) || (p.x == c.x && p.z == c.z)) continue;
                    if (InTri(p, a, b, c)) empty = false;
                }
                if (!empty) continue;
                result.Add((a, b, c));
                idx.RemoveAt(k);
                cut = true;
                break;
            }
            if (!cut) break;
        }
        if (idx.Count == 3) result.Add((poly[idx[0]], poly[idx[1]], poly[idx[2]]));
        return result;
    }

    static bool InTri((float x, float z) p, (float x, float z) a, (float x, float z) b, (float x, float z) c)
    {
        float d1 = (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
        float d2 = (c.x - b.x) * (p.z - b.z) - (c.z - b.z) * (p.x - b.x);
        float d3 = (a.x - c.x) * (p.z - c.z) - (a.z - c.z) * (p.x - c.x);
        return d1 >= 0f && d2 >= 0f && d3 >= 0f;
    }
}
