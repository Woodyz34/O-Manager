using System;
using System.Collections.Generic;
using MapImport;

// The ground and the water cut along the map's own lines (2026-11-24, Anders: "Vattenkanten måste vara helt skarp ...
// Allt ska vara skarpa linjer"). The ground is a grid of 1 m squares: before, a quay wall, a bank, the edges of the
// road over the Tranås tunnel and its causeway, and a garden's edge ran across the squares, so the ground sloped
// down from the street to the water inside the squares beside the wall (water showed on the street side of the
// wall, the road sank towards the lake under its fence) and the colours and the water's edge zigzagged.
// Now every triangle a line passes through is split exactly along the line:
//  - a step line (a bank, a quay or other wall, the causeway's sides): each side keeps its own height up to the line,
//    so the street stays level right up to the wall and the step is a clean vertical face along the line (hidden in
//    the wall where one stands there);
//  - a colour line (a garden's outline): the heights run on, only the colours change exactly on the line;
//  - the water's surface is cut along the water's lines too and kept only on the water's side, so no water shows on
//    the land side of a wall and its edge is the map's line.
public partial class TvWorldBuilder
{
    struct StepSeg
    {
        public float ax, az, bx, bz, nx, nz, len;   // nx, nz: the left normal (of a to b)
        public int id;
        public sbyte water;                         // the water's side: 1 left, -1 right, 0 none
        public bool step;                           // each side keeps its own height (else: colours only)
        public bool force;                          // a step even where the heights differ little
    }

    List<StepSeg> stepSegs;
    List<int>[] stepGrid;
    int stepCols;
    const float StepCell = 4f;
    float[] waterTop;                               // the water's surface at each ground corner (NaN: no lake there)
    Dictionary<int, float> cutLevel;                // the paving level of a ground corner made by a cut (KeepTown)
    float[] roadLevel;                              // how far inside the road over a tunnel a corner is (town)

    void AddStep(float ax, float az, float bx, float bz, int id, bool step, sbyte water, bool force = false)
    {
        float sx = bx - ax, sz = bz - az, sl = (float)Math.Sqrt(sx * sx + sz * sz);
        if (sl < 0.05f) return;
        if (Math.Max(ax, bx) < -R - 3f || Math.Min(ax, bx) > R + 3f || Math.Max(az, bz) < -R - 3f || Math.Min(az, bz) > R + 3f) return;
        stepSegs.Add(new StepSeg { ax = ax, az = az, bx = bx, bz = bz, nx = -sz / sl, nz = sx / sl, len = sl, id = id, step = step, water = water, force = force });
    }

    // Which side of a segment the water is on: 1 left, -1 right, 0 neither or both
    sbyte WaterSideOf(float ax, float az, float bx, float bz, float reach)
    {
        float sx = bx - ax, sz = bz - az, sl = (float)Math.Sqrt(sx * sx + sz * sz);
        if (sl < 0.05f) return 0;
        float nx = -sz / sl, nz = sx / sl, mx = (ax + bx) / 2f, mz = (az + bz) / 2f;
        bool wl = Base(SideCell(mx + nx * reach, mz + nz * reach)) == WorldCell.Water, wr = Base(SideCell(mx - nx * reach, mz - nz * reach)) == WorldCell.Water;
        return wl == wr ? (sbyte)0 : wl ? (sbyte)1 : (sbyte)-1;
    }

    void GatherSteps()
    {
        stepSegs = new List<StepSeg>();
        int id = 0;
        // The map's bank lines: the water on one side
        TvEdges edges = TvEdges.Of(layer);
        if (edges != null)
        {
            float lo = -R - 3f, hi = R + 3f;
            for (int li = 0; li < edges.lines.Count; li++, id++)
            {
                float[] b = edges.bounds[li];
                if (b[2] - cE < lo || b[0] - cE > hi || b[3] - cN < lo || b[1] - cN > hi) continue;
                float[] xy = edges.lines[li];
                for (int q = 0; q + 3 < xy.Length; q += 2)
                {
                    float ax = (float)(xy[q] - input.centerE), az = (float)(xy[q + 1] - input.centerN);
                    float bx = (float)(xy[q + 2] - input.centerE), bz = (float)(xy[q + 3] - input.centerN);
                    sbyte w = WaterSideOf(ax, az, bx, bz, 1.3f);
                    if (w != 0) AddStep(ax, az, bx, bz, id, true, w);
                }
            }
        }
        if (input.town != null && lines != null)
        {
            // A town's walls: a quay wall (the water on one side), or a wall with the ground higher on one side
            foreach (var line in lines)
            {
                id++;
                bool wall = line.kind == WorldLineKind.Wall || line.kind == WorldLineKind.HighWall;
                bool edge = wall || line.kind == WorldLineKind.Barrier || line.kind == WorldLineKind.HighFence || line.kind == WorldLineKind.Fence;
                if (!edge) continue;
                float[] c = line.xz;
                for (int q = 0; q + 3 < c.Length; q += 2)
                {
                    sbyte w = WaterSideOf(c[q], c[q + 1], c[q + 2], c[q + 3], 1.6f);
                    if (w != 0 || wall) AddStep(c[q], c[q + 1], c[q + 2], c[q + 3], id, true, w);
                }
            }
            // The road over a tunnel and on along its causeway: its sides, along the middle of its walls
            if (tunnelPlans != null)
                foreach (TunnelPlan p in tunnelPlans)
                {
                    // The walls across its mouths, holding the raised road (TunnelPortals)
                    if (!p.underHouse)
                        foreach (float tEnd in new[] { 0.6f, p.len - 0.6f })
                        {
                            id++;
                            float mx = p.ax + p.ux * tEnd, mz = p.az + p.uz * tEnd, reach = p.half + 40f;
                            AddStep(mx + p.uz * reach, mz - p.ux * reach, mx - p.uz * reach, mz + p.ux * reach, id, true, 0);
                        }
                    // The edges of its slab: the road beside it level right up to the slab, not sloping in under it
                    // (its face showed over the ground in a zigzag)
                    if (!p.underHouse)
                        foreach (float side in new[] { -1f, 1f })
                        {
                            id++;
                            float o = (p.half + 1.8f) * side, ox0 = -p.uz * o, oz0 = p.ux * o;
                            AddStep(p.ax + ox0, p.az + oz0, p.ax + p.ux * p.len + ox0, p.az + p.uz * p.len + oz0, id, true, 0, true);
                        }
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Causeway cw = side < 0f ? p.wayA : p.wayB;
                        if (cw == null || p.underHouse) continue;
                        float nx = -p.uz * side, nz = p.ux * side, mx = p.ax + p.ux * p.len / 2f, mz = p.az + p.uz * p.len / 2f;
                        foreach (float o in new[] { -1f, 1f })
                        {
                            id++;
                            float px = float.NaN, pz = 0f;
                            float s1 = cw.ext + 30f;
                            for (float s = p.half + 0.6f; s <= s1 + 0.01f; s += 1f)
                            {
                                cw.Edges(s, out float ea, out float eb);
                                float q = o < 0f ? ea - 0.3f : eb + 0.3f;
                                float x = mx + nx * s + p.ux * q, z = mz + nz * s + p.uz * q;
                                if (!float.IsNaN(px))
                                {
                                    // The water's side: outwards, where there is water beside the road
                                    sbyte w = 0;
                                    if (s <= cw.ext + 1f)
                                    {
                                        float wx = (px + x) / 2f + p.ux * o * 1.5f, wz = (pz + z) / 2f + p.uz * o * 1.5f;
                                        if (Base(CellAt(wx, wz)) == WorldCell.Water)
                                        {
                                            float sx = x - px, sz = z - pz;
                                            w = (-sz * (wx - px) + sx * (wz - pz)) >= 0f ? (sbyte)1 : (sbyte)-1;
                                        }
                                    }
                                    AddStep(px, pz, x, z, id, true, w);
                                }
                                px = x; pz = z;
                            }
                        }
                    }
                }
            // The gardens' outlines: only the colours
            if (input.town.gardens != null)
                foreach (WorldArea a in input.town.gardens.rings)
                {
                    id++;
                    if (a.kind != WorldAreaKind.Garden) continue;
                    if (a.maxE - input.centerE < -R - 3 || a.minE - input.centerE > R + 3 || a.maxN - input.centerN < -R - 3 || a.minN - input.centerN > R + 3) continue;
                    float[] xy = a.xy;
                    int n = xy.Length / 2;
                    for (int q = 0; q < n; q++)
                    {
                        int r = (q + 1) % n;
                        AddStep((float)(xy[q * 2] - input.centerE), (float)(xy[q * 2 + 1] - input.centerN),
                                (float)(xy[r * 2] - input.centerE), (float)(xy[r * 2 + 1] - input.centerN), id, false, 0);
                    }
                }
        }
        // Each segment in the 4 m cells it passes near
        stepCols = (int)Math.Ceiling((2f * R + 8f) / StepCell);
        stepGrid = new List<int>[stepCols * stepCols];
        for (int s = 0; s < stepSegs.Count; s++)
        {
            StepSeg g = stepSegs[s];
            int i0 = StepCol(Math.Min(g.ax, g.bx) - 0.5f), i1 = StepCol(Math.Max(g.ax, g.bx) + 0.5f);
            int j0 = StepCol(Math.Min(g.az, g.bz) - 0.5f), j1 = StepCol(Math.Max(g.az, g.bz) + 0.5f);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    (stepGrid[j * stepCols + i] ?? (stepGrid[j * stepCols + i] = new List<int>())).Add(s);
        }
    }

    int StepCol(float v) => Math.Max(0, Math.Min(stepCols - 1, (int)Math.Floor((v + R + 4f) / StepCell)));

    // A corner of a piece of a triangle being cut
    struct PV
    {
        public int idx;                             // its corner in the mesh (-1: new)
        public float x, y, z, nx, ny, nz, lvl;
        public byte r, g, b;
    }

    PV PvOf(TvMesh m, int v)
    {
        List<float> p = m.positions, n = m.normals;
        float lvl = -1f;
        if (m == groundMesh) lvl = v < N * N ? (paveLevel != null ? paveLevel[v] : -1f) : cutLevel.TryGetValue(v, out float l) ? l : -1f;
        return new PV
        {
            idx = v, x = p[v * 3], y = p[v * 3 + 1], z = p[v * 3 + 2], nx = n[v * 3], ny = n[v * 3 + 1], nz = n[v * 3 + 2], lvl = lvl,
            r = m.colors[v * 3], g = m.colors[v * 3 + 1], b = m.colors[v * 3 + 2]
        };
    }

    int Emit(TvMesh m, PV v)
    {
        if (v.idx >= 0) return v.idx;
        float nl = (float)Math.Sqrt(v.nx * v.nx + v.ny * v.ny + v.nz * v.nz);
        if (nl < 1e-5f) { v.nx = 0f; v.ny = 1f; v.nz = 0f; nl = 1f; }
        int i = m.Vertex(v.x, v.y, v.z, v.nx / nl, v.ny / nl, v.nz / nl, new TvColor(v.r, v.g, v.b));
        if (m == groundMesh) cutLevel[i] = v.lvl;
        return i;
    }

    // The water's surface near a point (NaN: none)
    float WaterTopNear(float x, float z)
    {
        if (waterTop == null) return float.NaN;
        int i = (int)Math.Round(x + R), j = (int)Math.Round(z + R);
        float best = float.NaN;
        for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int ii = i + di, jj = j + dj;
                if (ii < 0 || jj < 0 || ii >= N || jj >= N) continue;
                float w = waterTop[jj * N + ii];
                if (!float.IsNaN(w) && (float.IsNaN(best) || w > best)) best = w;
            }
        return best;
    }

    static readonly TvColor StepFace = TvColor.Hex("#8a867e");

    // Cut every triangle of a mesh along the step and colour lines it crosses. water: the water's surface (cut along
    // the water's lines only, and kept on the water's side)
    void CutSteps(TvMesh mesh, bool water)
    {
        if (stepSegs == null || stepSegs.Count == 0 || mesh == null) return;
        var t = mesh.triangles;
        var kept = new List<int>(t.Count + 256);
        var pieces = new List<List<PV>>();
        var next = new List<List<PV>>();
        var cand = new List<int>();
        var seen = new HashSet<int>();
        var skirts = new List<PV>();
        for (int q = 0; q + 2 < t.Count; q += 3)
        {
            int v0 = t[q], v1 = t[q + 1], v2 = t[q + 2];
            List<float> p = mesh.positions;
            float minX = Math.Min(p[v0 * 3], Math.Min(p[v1 * 3], p[v2 * 3])), maxX = Math.Max(p[v0 * 3], Math.Max(p[v1 * 3], p[v2 * 3]));
            float minZ = Math.Min(p[v0 * 3 + 2], Math.Min(p[v1 * 3 + 2], p[v2 * 3 + 2])), maxZ = Math.Max(p[v0 * 3 + 2], Math.Max(p[v1 * 3 + 2], p[v2 * 3 + 2]));
            cand.Clear();
            seen.Clear();
            for (int j = StepCol(minZ); j <= StepCol(maxZ); j++)
                for (int i = StepCol(minX); i <= StepCol(maxX); i++)
                {
                    List<int> l = stepGrid[j * stepCols + i];
                    if (l == null) continue;
                    foreach (int s in l)
                    {
                        if (water && stepSegs[s].water == 0) continue;
                        StepSeg g = stepSegs[s];
                        if (Math.Max(g.ax, g.bx) < minX - 0.1f || Math.Min(g.ax, g.bx) > maxX + 0.1f || Math.Max(g.az, g.bz) < minZ - 0.1f || Math.Min(g.az, g.bz) > maxZ + 0.1f)
                        {
                            // Not through the triangle; on the water a land piece beside it is still dropped below
                            if (!water) continue;
                        }
                        if (seen.Add(s)) cand.Add(s);
                    }
                }
            if (cand.Count == 0) { kept.Add(v0); kept.Add(v1); kept.Add(v2); continue; }
            pieces.Clear();
            pieces.Add(new List<PV> { PvOf(mesh, v0), PvOf(mesh, v1), PvOf(mesh, v2) });
            bool changed = false;
            foreach (int s in cand)
            {
                StepSeg g = stepSegs[s];
                next.Clear();
                foreach (List<PV> piece in pieces)
                {
                    if (CutPiece(piece, g, water, next, skirts)) changed = true;
                    else next.Add(piece);
                }
                pieces.Clear();
                pieces.AddRange(next);
            }
            foreach (List<PV> piece in pieces)
            {
                if (water && OnLandSide(piece, cand)) { changed = true; continue; }
                if (piece.Count < 3) continue;
                int first = Emit(mesh, piece[0]);
                int prev = Emit(mesh, piece[1]);
                for (int m = 2; m < piece.Count; m++)
                {
                    int cur = Emit(mesh, piece[m]);
                    kept.Add(first); kept.Add(prev); kept.Add(cur);
                    prev = cur;
                }
            }
            if (!changed && pieces.Count == 1) continue;
        }
        // The vertical faces of the steps, seen from both sides
        for (int m = 0; m + 3 < skirts.Count; m += 4)
        {
            int a = Emit(mesh, skirts[m]), b = Emit(mesh, skirts[m + 1]), c = Emit(mesh, skirts[m + 2]), d = Emit(mesh, skirts[m + 3]);
            kept.Add(a); kept.Add(b); kept.Add(c); kept.Add(a); kept.Add(c); kept.Add(d);
            kept.Add(a); kept.Add(c); kept.Add(b); kept.Add(a); kept.Add(d); kept.Add(c);
        }
        t.Clear();
        t.AddRange(kept);
    }

    // A piece of the water's surface wholly on the land side of a water line beside it (within 1.5 m)
    bool OnLandSide(List<PV> piece, List<int> cand)
    {
        float cx = 0f, cz = 0f;
        foreach (PV v in piece) { cx += v.x; cz += v.z; }
        cx /= piece.Count; cz /= piece.Count;
        float best = 1.5f;
        int side = 0;
        foreach (int s in cand)
        {
            StepSeg g = stepSegs[s];
            if (g.water == 0) continue;
            float dx = g.bx - g.ax, dz = g.bz - g.az;
            float u = ((cx - g.ax) * dx + (cz - g.az) * dz) / (g.len * g.len);
            if (u < -0.02f || u > 1.02f) continue;
            float d = (cx - g.ax) * g.nx + (cz - g.az) * g.nz;
            if (Math.Abs(d) < best) { best = Math.Abs(d); side = Math.Sign(d) == g.water ? 1 : -1; }
        }
        return side < 0;
    }

    // Split a piece along a segment's line where the segment passes through it: the two parts into "into"
    bool CutPiece(List<PV> piece, StepSeg g, bool water, List<List<PV>> into, List<PV> skirts)
    {
        int n = piece.Count;
        var d = new float[n];
        bool pos = false, neg = false;
        for (int i = 0; i < n; i++)
        {
            d[i] = (piece[i].x - g.ax) * g.nx + (piece[i].z - g.az) * g.nz;
            if (d[i] > 0.01f) pos = true;
            else if (d[i] < -0.01f) neg = true;
        }
        if (!pos || !neg) return false;
        // Where the line crosses the piece's edges, and whether that is on the segment itself
        float ux = (g.bx - g.ax) / g.len, uz = (g.bz - g.az) / g.len;
        float c0 = float.NaN, c1 = float.NaN;
        for (int i = 0; i < n; i++)
        {
            int k = (i + 1) % n;
            if ((d[i] >= 0f) == (d[k] >= 0f)) continue;
            float tt = d[i] / (d[i] - d[k]);
            float x = piece[i].x + (piece[k].x - piece[i].x) * tt, z = piece[i].z + (piece[k].z - piece[i].z) * tt;
            float u = (x - g.ax) * ux + (z - g.az) * uz;
            if (float.IsNaN(c0)) c0 = u; else c1 = u;
        }
        if (float.IsNaN(c1) || Math.Max(c0, c1) < -0.05f || Math.Min(c0, c1) > g.len + 0.05f) return false;
        // A step: the two sides' heights differ
        bool step = false;
        if (!water && g.step)
        {
            float sp = 0f, sn = 0f;
            int np = 0, nn = 0;
            for (int i = 0; i < n; i++) { if (d[i] >= 0f) { sp += piece[i].y; np++; } else { sn += piece[i].y; nn++; } }
            step = g.force || Math.Abs(sp / np - sn / nn) > 0.25f;
        }
        var cuts = new PV[2, 2];        // [crossing, side]
        int found = 0;
        for (int side = 0; side < 2; side++)
        {
            bool plus = side == 0;
            var part = new List<PV>(n + 2);
            found = 0;
            for (int i = 0; i < n; i++)
            {
                int k = (i + 1) % n;
                PV a = piece[i], b = piece[k];
                if ((d[i] >= 0f) == plus) part.Add(a);
                if ((d[i] >= 0f) != (d[k] >= 0f))
                {
                    float tt = d[i] / (d[i] - d[k]);
                    PV own = (d[i] >= 0f) == plus ? a : b;
                    var c = new PV
                    {
                        idx = -1,
                        x = a.x + (b.x - a.x) * tt, z = a.z + (b.z - a.z) * tt,
                        y = step ? own.y : a.y + (b.y - a.y) * tt,
                        nx = step ? own.nx : a.nx + (b.nx - a.nx) * tt, ny = step ? own.ny : a.ny + (b.ny - a.ny) * tt, nz = step ? own.nz : a.nz + (b.nz - a.nz) * tt,
                        r = own.r, g = own.g, b = own.b,
                        lvl = a.lvl + (b.lvl - a.lvl) * tt
                    };
                    // Each side's paving as its own corner has it
                    c.lvl = own.lvl >= 0f ? Math.Max(c.lvl, 0.02f) : Math.Min(c.lvl, -0.02f);
                    // The land beside the water: never below the water's surface (else the water's edge would hang in the air)
                    if (step && g.water != 0 && (plus ? 1 : -1) != g.water)
                    {
                        float wt = WaterTopNear(c.x, c.z);
                        if (!float.IsNaN(wt) && c.y < wt + 0.08f) c.y = wt + 0.08f;
                    }
                    part.Add(c);
                    if (found < 2) cuts[found++, side] = c;
                }
            }
            if (part.Count >= 3) into.Add(part);
        }
        if (step && found == 2)
        {
            // The face between: from each crossing's high copy down to its low copy
            for (int m = 0; m < 2; m++) { }
            PV a0 = cuts[0, 0], a1 = cuts[1, 0], b0 = cuts[0, 1], b1 = cuts[1, 1];
            bool plusHigh = a0.y + a1.y >= b0.y + b1.y;
            PV hi0 = plusHigh ? a0 : b0, hi1 = plusHigh ? a1 : b1;
            float fnx = plusHigh ? -g.nx : g.nx, fnz = plusHigh ? -g.nz : g.nz;
            TvColor face = g.water != 0 ? StepFace : new TvColor(hi0.r, hi0.g, hi0.b).Shade(0.78f);
            PV F0(PV v) { v.idx = -1; v.nx = fnx; v.ny = 0f; v.nz = fnz; v.r = face.r; v.g = face.g; v.b = face.b; v.lvl = -9f; return v; }
            skirts.Add(F0(a0)); skirts.Add(F0(a1)); skirts.Add(F0(b1)); skirts.Add(F0(b0));
        }
        return true;
    }
}
