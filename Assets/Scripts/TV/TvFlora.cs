using System;
using System.Collections.Generic;

// The pictures of the forest's texture atlas (Resources/Textures/TvForestAtlas.png, painted by Docs/forest_atlas.py):
// 7 x 7 tiles of 512 px, each with 16 px padding round its picture. A textured corner carries u, v (0..1 over the
// tile's picture; bark and stone repeat past 1) and the tile's number. The shader (TvAtlasLit) cuts the leaves, needles
// and stems out of their tile by its alpha. Tiles 0-4 are bark, 20-24 stone and wood, 25-31 the town's walls, roofs and windows; 5-19 are leaves and plants.
public static class TvAtlas
{
    public const int PerSide = 7;
    public const float Padding = 16f / 512f;

    public const int PineBarkLow = 0, PineBarkHigh = 1, SpruceBark = 2, BirchBark = 3, BeechBark = 4,
        PineNeedles = 5, SpruceSpray = 6, BirchLeaves = 7, BroadLeaves = 8, Bilberry = 9, Grass = 10, Fern = 11,
        Heather = 12, Litter = 13, Twig = 14, DeadTwigs = 15, Lingonberry = 16, Sedge = 17, Juniper = 18, Flowers = 19,
        Granite = 20, CliffRock = 21, DeadWood = 22, PathSoil = 23, Moss = 24,
        Plaster = 25, Brick = 26, WoodPanel = 27, RoofTiles = 28, MetalSheet = 29, Concrete = 30, Glass = 31,
        MossSock = 32, BilberryTop = 33, LichenTop = 34, PineBarkLow2 = 35,
        // 2026-11-25 (Anders' photos of clear-cuts and heather): logging slash, a heather bush from above, churned soil
        Slash = 36, HeatherTop = 37, Soil = 38;

    // Tiles from this one on are leaves and plants: lit softly through, as light comes through a leaf
    public const int FirstFoliage = 5;
}

// Textured parts of trees and plants: bark round a bent limb, flat cards with a picture of needles, leaves or a whole
// plant on them. Plain C#, like the rest of the shape builder.
public partial class TvShapeBuilder
{
    public bool Empty => p.Count == 0;

    // A textured corner: its own u, v and tile
    int VT(float x, float y, float z, float nx, float ny, float nz, TvColor col, float u, float v, float tile)
    {
        int index = p.Count / 3;
        while (uv.Count < index * 3) { uv.Add(0f); uv.Add(0f); uv.Add(-1f); }
        p.Add(x); p.Add(y); p.Add(z);
        n.Add(nx); n.Add(ny); n.Add(nz);
        c.Add(col.r); c.Add(col.g); c.Add(col.b);
        uv.Add(u); uv.Add(v); uv.Add(tile);
        return index;
    }

    // ---- Bark: a round limb along a line of points (a trunk, a branch, a root) ----
    // pts: x, y, z for each point; radius at each point; the bark picture repeats `around` times round it and once
    // every `along` metres up it. shade: colour at each point (darker low down, lighter on top).
    // Unevenness of the next limbs (0 = a clean round tube): each corner out or in by up to this share of the radius,
    // the same round the seam, so a trunk's outline is not a perfect cylinder
    public float LimbRough;
    // Moves the picture along a limb (metres), so trees made from the same picture do not line up
    public float LimbShift;

    public void Limb(float[] pts, float[] radius, int sides, int tile, float around, float along, TvColor[] shade, float twist = 0f)
    {
        int count = pts.Length / 3;
        if (count < 2) return;
        // A frame that turns with the line (parallel transport), so the bark does not twist where the limb bends
        float tx = pts[3] - pts[0], ty = pts[4] - pts[1], tz = pts[5] - pts[2];
        Norm(ref tx, ref ty, ref tz);
        float ux, uy, uz;
        if (Math.Abs(ty) < 0.9f) { ux = tz; uy = 0f; uz = -tx; }
        else { ux = 1f; uy = 0f; uz = 0f; }
        Norm(ref ux, ref uy, ref uz);
        int first = p.Count / 3;
        float dist = 0f;
        for (int k = 0; k < count; k++)
        {
            float px = pts[k * 3], py = pts[k * 3 + 1], pz = pts[k * 3 + 2];
            if (k > 0)
            {
                float dx = px - pts[k * 3 - 3], dy = py - pts[k * 3 - 2], dz = pz - pts[k * 3 - 1];
                dist += (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            // The direction here: between the segments before and after
            int a = Math.Max(0, k - 1), b = Math.Min(count - 1, k + 1);
            float nx = pts[b * 3] - pts[a * 3], ny = pts[b * 3 + 1] - pts[a * 3 + 1], nz = pts[b * 3 + 2] - pts[a * 3 + 2];
            Norm(ref nx, ref ny, ref nz);
            // Keep u at right angles to the new direction
            float d = ux * nx + uy * ny + uz * nz;
            ux -= nx * d; uy -= ny * d; uz -= nz * d;
            Norm(ref ux, ref uy, ref uz);
            float vx = ny * uz - nz * uy, vy = nz * ux - nx * uz, vz = nx * uy - ny * ux;
            float r = radius[k];
            TvColor col = shade[Math.Min(k, shade.Length - 1)];
            for (int i = 0; i <= sides; i++)
            {
                float ang = i / (float)sides * 6.2831853f + twist * dist;
                float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
                float ox = ux * ca + vx * sa, oy = uy * ca + vy * sa, oz = uz * ca + vz * sa;
                float rr = LimbRough > 0f ? r * (1f + LimbRough * (TvRandom.Noise((i % sides) * 1.37f + 0.5f, dist * 1.3f + first * 0.11f) - 0.5f) * 2f) : r;
                VT(px + ox * rr, py + oy * rr, pz + oz * rr, ox, oy, oz, col, i / (float)sides * around, (dist + LimbShift) / along, tile);
            }
        }
        int ring = sides + 1;
        for (int k = 0; k + 1 < count; k++)
            for (int i = 0; i < sides; i++)
            {
                int a = first + k * ring + i, b = a + 1, q = a + ring, e = q + 1;
                // Facing out (the frame turns counter-clockwise seen from the tip)
                t.Add(a); t.Add(q); t.Add(b);
                t.Add(b); t.Add(q); t.Add(e);
            }
    }

    static void Norm(ref float x, ref float y, ref float z)
    {
        float l = (float)Math.Sqrt(x * x + y * y + z * z);
        if (l < 1e-9f) { x = 0f; y = 1f; z = 0f; return; }
        x /= l; y /= l; z /= l;
    }

    // A flat triangle with a repeating picture laid on it from the side it faces most (a stone's face): `scale` metres
    // per repeat. Faces away from (cx, cy, cz).
    public void FlatTriTex(float ax, float ay, float az, float bx, float by, float bz, float qx, float qy, float qz,
                           float cx, float cy, float cz, TvColor col, float tile, float scale, float shift = 0f)
    {
        float ux = bx - ax, uy = by - ay, uz = bz - az, vx = qx - ax, vy = qy - ay, vz = qz - az;
        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        float ox = (ax + bx + qx) / 3f - cx, oy = (ay + by + qy) / 3f - cy, oz = (az + bz + qz) / 3f - cz;
        bool flip = nx * ox + ny * oy + nz * oz < 0f;
        if (flip) { nx = -nx; ny = -ny; nz = -nz; }
        float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len < 1e-9f) return;
        nx /= len; ny /= len; nz /= len;
        float k = 1f / scale;
        float ax2 = Math.Abs(nx), ay2 = Math.Abs(ny), az2 = Math.Abs(nz);
        void UV(float x, float y, float z, out float u, out float v)
        {
            if (ay2 >= ax2 && ay2 >= az2) { u = x * k + shift; v = z * k; }
            else if (ax2 >= az2) { u = z * k + shift; v = y * k; }
            else { u = x * k + shift; v = y * k; }
        }
        UV(ax, ay, az, out float u0, out float v0);
        UV(bx, by, bz, out float u1, out float v1);
        UV(qx, qy, qz, out float u2, out float v2);
        int a = VT(ax, ay, az, nx, ny, nz, col, u0, v0, tile);
        int b = flip ? VT(qx, qy, qz, nx, ny, nz, col, u2, v2, tile) : VT(bx, by, bz, nx, ny, nz, col, u1, v1, tile);
        int q = flip ? VT(bx, by, bz, nx, ny, nz, col, u1, v1, tile) : VT(qx, qy, qz, nx, ny, nz, col, u2, v2, tile);
        t.Add(a); t.Add(b); t.Add(q);
    }

    // A round flat disc facing up (the cut top of a stump): the picture laid across it
    public void Disk(float x, float y, float z, float r, int sides, int tile, TvColor col, float scale)
    {
        int centre = VT(x, y, z, 0f, 1f, 0f, col, 0.5f, 0.5f, tile);
        int first = p.Count / 3;
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * 6.2831853f, ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            VT(x + ca * r, y, z + sa * r, 0f, 1f, 0f, col, 0.5f + ca * r / scale, 0.5f + sa * r / scale, tile);
        }
        for (int i = 0; i < sides; i++) { t.Add(centre); t.Add(first + i + 1); t.Add(first + i); }
    }

    // ---- Cards: a flat picture (needles, leaves, a plant), drawn from both sides ----
    // Corners: o (where v = 0, u = 0.5: the base of the picture), along the side vector s (u from 0 to 1 across, s is
    // half the width) and up the vector up (v from v0 to v1). The light uses the normal given (for a tree crown: out of
    // the crown, so the crown is lit as a whole and not card by card). bend: the top half turns by this much towards
    // the normal's opposite (a drooping branch, an arching fern): the card is two quads.
    public void Card(float ox, float oy, float oz, float sx, float sy, float sz, float upx, float upy, float upz,
                     float nx, float ny, float nz, int tile, TvColor bottom, TvColor top, float bend = 0f, float v0 = 0f, float v1 = 1f,
                     float u0 = 0f, float u1 = 1f)
    {
        Norm(ref nx, ref ny, ref nz);
        if (bend == 0f)
        {
            int a = VT(ox - sx, oy - sy, oz - sz, nx, ny, nz, bottom, u0, v0, tile);
            int b = VT(ox + sx, oy + sy, oz + sz, nx, ny, nz, bottom, u1, v0, tile);
            int q = VT(ox + sx + upx, oy + sy + upy, oz + sz + upz, nx, ny, nz, top, u1, v1, tile);
            int d = VT(ox - sx + upx, oy - sy + upy, oz - sz + upz, nx, ny, nz, top, u0, v1, tile);
            t.Add(a); t.Add(b); t.Add(q);
            t.Add(a); t.Add(q); t.Add(d);
            return;
        }
        // Two quads: the lower half straight, the upper half bent down by `bend` (a share of the length)
        float hx = upx * 0.5f, hy = upy * 0.5f, hz = upz * 0.5f;
        float mx = ox + hx, my = oy + hy, mz = oz + hz;
        // The bent half: the same length, pulled down (y) by bend
        float ex = hx, ey = hy - bend * (float)Math.Sqrt(upx * upx + upy * upy + upz * upz), ez = hz;
        float hl = (float)Math.Sqrt(hx * hx + hy * hy + hz * hz), el = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
        if (el > 1e-6f) { ex *= hl / el; ey *= hl / el; ez *= hl / el; }
        float vm = (v0 + v1) * 0.5f;
        TvColor mid = bottom.Lerp(top, 0.5f);
        int a0 = VT(ox - sx, oy - sy, oz - sz, nx, ny, nz, bottom, u0, v0, tile);
        int b0 = VT(ox + sx, oy + sy, oz + sz, nx, ny, nz, bottom, u1, v0, tile);
        int b1 = VT(mx + sx, my + sy, mz + sz, nx, ny, nz, mid, u1, vm, tile);
        int a1 = VT(mx - sx, my - sy, mz - sz, nx, ny, nz, mid, u0, vm, tile);
        int b2 = VT(mx + ex + sx, my + ey + sy, mz + ez + sz, nx, ny, nz, top, u1, v1, tile);
        int a2 = VT(mx + ex - sx, my + ey - sy, mz + ez - sz, nx, ny, nz, top, u0, v1, tile);
        t.Add(a0); t.Add(b0); t.Add(b1); t.Add(a0); t.Add(b1); t.Add(a1);
        t.Add(a1); t.Add(b1); t.Add(b2); t.Add(a1); t.Add(b2); t.Add(a2);
    }

    // A cushion of moss on the forest floor (2026-11-25, Anders' photos of spruce forest: the ground is soft green humps
    // over old stumps and stones): a low lumpy dome, wide radius rx by rz, h high, its edge sunk a little into the ground.
    // The moss picture lies flat over it from above (u, v from x, z), every `size` metres.
    public void Mound(float rx, float rz, float h, int tile, TvColor low, TvColor high, float size)
    {
        int rings = 5, around = 14;
        int first = p.Count / 3;
        float ph1 = (float)(random.Value() * 6.283), ph2 = (float)(random.Value() * 6.283);
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;                 // 0 at the top, 1 at the edge
            for (int j = 0; j < (i == 0 ? 1 : around); j++)
            {
                float a = j * 6.2832f / around;
                float wob = 1f + 0.16f * (float)Math.Sin(a * 2f + ph1) + 0.1f * (float)Math.Sin(a * 3f + ph2);
                float r = t * wob;
                float x = (float)Math.Cos(a) * rx * r, z = (float)Math.Sin(a) * rz * r;
                float y = h * (1f - t * t) * (1f - t * t) * (0.85f + 0.15f * wob) - 0.05f * t * t;   // flat at the edge: no step
                float nx = (float)Math.Cos(a) * t * 0.9f, nz = (float)Math.Sin(a) * t * 0.9f, ny = 1f;
                Norm(ref nx, ref ny, ref nz);
                VT(x, y, z, nx, ny, nz, high.Lerp(low, t * t), x / size, z / size, tile);
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < around; j++)
            {
                int j1 = (j + 1) % around;
                int b0 = first + 1 + i * around + j, b1 = first + 1 + i * around + j1;
                if (i == 0) { t.Add(first); t.Add(b1); t.Add(b0); continue; }
                int a0 = first + 1 + (i - 1) * around + j, a1 = first + 1 + (i - 1) * around + j1;
                t.Add(a0); t.Add(a1); t.Add(b1); t.Add(a0); t.Add(b1); t.Add(b0);
            }
    }

    // A plant on the ground as a star of cards leaning out from its middle (a bilberry clump, a tussock, heather):
    // reads as a plant from the side and as a round clump from above. Its light is the ground's (normals up, leaning
    // a little out), so it sits in the ground's colour.
    public void Star(float x, float y, float z, int cards, float width, float height, float lean, int tile, TvColor bottom, TvColor top,
                     float turn, float spread = 0.15f)
    {
        for (int i = 0; i < cards; i++)
        {
            float a = turn + i * 3.14159265f / cards + (random.Value() - 0.5f) * 0.5f;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            // Each card sits a little off the middle, leaning out on its own side
            float off = (random.Value() - 0.5f) * 2f * spread * width;
            float side = random.Value() < 0.5f ? -1f : 1f;
            float px = x - sa * off, pz = z + ca * off;
            float w = width * (0.75f + 0.5f * random.Value()) * 0.5f, h = height * (0.8f + 0.4f * random.Value());
            float l = lean * (0.6f + 0.8f * random.Value()) * side;
            float upx = -sa * l * h, upz = ca * l * h;
            // Normal: up, tipped a little towards the lean
            Card(px, y, pz, ca * w, 0f, sa * w, upx, h, upz, -sa * l * 0.5f, 1f, ca * l * 0.5f, tile, bottom, top,
                 0f, 0f, 1f, random.Value() < 0.5f ? 0f : 1f, random.Value() < 0.5f ? 1f : 0f);
        }
    }

    // A strip lying on the ground along a line (a path's trodden earth): points xs, zs with their distance along vs,
    // five corners across, each at the ground's own height (lifted a little) with the ground's slope as its normal.
    // u runs across (0.02 .. 0.98, so the picture's ragged edges show), v along.
    public void Strip(float[] xs, float[] zs, float[] vs, float half, Func<float, float, float> height, int tile, TvColor color, float lift)
    {
        const int Across = 5;
        int rows = xs.Length;
        if (rows < 2) return;
        int first = p.Count / 3;
        for (int i = 0; i < rows; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(rows - 1, i + 1);
            float dx = xs[b] - xs[a], dz = zs[b] - zs[a];
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-5f) { dx = 1f; dz = 0f; len = 1f; }
            float sx = -dz / len, sz = dx / len;
            for (int k = 0; k < Across; k++)
            {
                float f = k / (float)(Across - 1), off = (f * 2f - 1f) * half;
                float x = xs[i] + sx * off, z = zs[i] + sz * off;
                float y = height(x, z);
                float gx = height(x - 0.5f, z) - height(x + 0.5f, z), gz = height(x, z - 0.5f) - height(x, z + 0.5f);
                float nl = (float)Math.Sqrt(gx * gx + 1f + gz * gz);
                VT(x, y + lift, z, gx / nl, 1f / nl, gz / nl, color, 0.02f + f * 0.96f, vs[i], tile);
            }
        }
        for (int i = 0; i + 1 < rows; i++)
            for (int k = 0; k + 1 < Across; k++)
            {
                int a = first + i * Across + k, b = a + 1, c2 = a + Across, d = c2 + 1;
                t.Add(a); t.Add(c2); t.Add(b);
                t.Add(b); t.Add(c2); t.Add(d);
            }
    }

    // A card lying on the ground (litter, a stick): corners at the ground's own heights, normals the ground's
    public void GroundCard(float x, float z, float halfLength, float halfWidth, float yaw, Func<float, float, float> height, int tile,
                           TvColor color, float v0 = 0f, float v1 = 1f, float lift = 0.012f)
    {
        float ca = (float)Math.Cos(yaw), sa = (float)Math.Sin(yaw);
        float[] cx = { -halfLength, halfLength, halfLength, -halfLength }, cz = { -halfWidth, -halfWidth, halfWidth, halfWidth };
        float[] u = { 0f, 1f, 1f, 0f }, v = { v0, v0, v1, v1 };
        int[] k = new int[4];
        for (int i = 0; i < 4; i++)
        {
            float px = x + cx[i] * ca - cz[i] * sa, pz = z + cx[i] * sa + cz[i] * ca;
            float h = height(px, pz);
            // The ground's slope here, for the light
            float gx = height(px + 0.5f, pz) - height(px - 0.5f, pz), gz = height(px, pz + 0.5f) - height(px, pz - 0.5f);
            float nx = -gx, ny = 1f, nz = -gz;
            Norm(ref nx, ref ny, ref nz);
            k[i] = VT(px, h + lift, pz, nx, ny, nz, color, u[i], v[i], tile);
        }
        t.Add(k[0]); t.Add(k[2]); t.Add(k[1]);
        t.Add(k[0]); t.Add(k[3]); t.Add(k[2]);
    }
}
