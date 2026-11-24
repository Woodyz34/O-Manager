using System;
using System.Collections.Generic;

// The pictures of the forest's texture atlas (Resources/Textures/TvForestAtlas.png, painted by Docs/forest_atlas.py):
// 5 x 5 tiles of 512 px, each with 16 px padding round its picture. A textured corner carries u, v (0..1 over the
// tile's picture; bark and stone repeat past 1) and the tile's number. The shader (TvAtlasLit) cuts the leaves, needles
// and stems out of their tile by its alpha. Tiles 0-4 are bark and 20-24 stone; 5-19 are leaves and plants.
public static class TvAtlas
{
    public const int PerSide = 5;
    public const float Padding = 16f / 512f;

    public const int PineBarkLow = 0, PineBarkHigh = 1, SpruceBark = 2, BirchBark = 3, BeechBark = 4,
        PineNeedles = 5, SpruceSpray = 6, BirchLeaves = 7, BroadLeaves = 8, Bilberry = 9, Grass = 10, Fern = 11,
        Heather = 12, Litter = 13, Twig = 14, DeadTwigs = 15;

    // Tiles from this one on are leaves and plants: lit softly through, as light comes through a leaf
    public const int FirstFoliage = 5;
}

// Textured parts of trees and plants: bark round a bent limb, flat cards with a picture of needles, leaves or a whole
// plant on them. Plain C#, like the rest of the shape builder.
public partial class TvShapeBuilder
{
    public bool Empty => p.Count == 0;

    // A textured corner: its own u, v and tile
    int VT(float x, float y, float z, float nx, float ny, float nz, TvColor col, float u, float v, int tile)
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
                VT(px + ox * r, py + oy * r, pz + oz * r, ox, oy, oz, col, i / (float)sides * around, dist / along, tile);
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
