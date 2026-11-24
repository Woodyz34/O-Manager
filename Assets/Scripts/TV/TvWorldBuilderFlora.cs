using System;
using System.Collections.Generic;
using MapImport;

// The forest's trees and plants with real bark and real needles and leaves (2026-11-25, Anders: "make the 3D world
// as real as it can be"): textured with the forest atlas (TvAtlas), cut out by the shader.
//  - Scots pine: a bare, slightly bent trunk with thick grey-brown plates low down and thin orange bark higher up,
//    an open crown of branches ending in brushes of long needles.
//  - Norway spruce: a cone of drooping branch sprays all the way down, dead grey twigs under the green in a forest.
//  - Birch: a white trunk with black marks and a rough dark foot, hanging twigs of small leaves.
//  - Beech and other broad-leaved trees: a smooth grey trunk and a big crown of leaf clusters.
//  - Bushes, young trees, and the undergrowth: bilberry with stems, leaves and berries, grass tussocks, heather,
//    ferns, and a litter of needles, twigs and cones on the forest floor.
// How much detail depends on the 3D quality (TvWorldInput.quality): Low has the fewest cards per tree; Ultra the most,
// with thin branches inside the crowns. The shapes are made once per quality and forest type and shared by every
// build (they are only read after that).
public partial class TvWorldBuilder
{
    class Flora
    {
        public TvShape[] pineNear, pineFar, pine2Near, pine2Far, spruceNear, birchNear, birchFar, beechNear, beechFar;
        public TvShape spruceFar, bigTree, deadPine;
        public TvShape[] youngSpruce, youngLeafy, bushes;
        public TvShape berry, berryNear, heath, heathNear, grass, dry, shortGrass, fern;
    }

    static readonly Dictionary<int, Flora> floraCache = new Dictionary<int, Flora>();
    static readonly object floraLock = new object();

    // Quality 0..3 from the input
    int Q => Math.Max(0, Math.Min(3, input.quality));

    void MakeFlora()
    {
        Flora f;
        int key = Q;
        lock (floraLock)
        {
            if (!floraCache.TryGetValue(key, out f))
            {
                f = BuildFlora(Q);
                floraCache[key] = f;
            }
        }
        pineNear = f.pineNear;
        pineFar = f.pineFar;
        pine2Near = f.pine2Near;
        pine2Far = f.pine2Far;
        spruceShape = f.spruceNear;
        spruceFar = f.spruceFar;
        birchNear = f.birchNear;
        birchFar = f.birchFar;
        beechNear = f.beechNear;
        beechFar = f.beechFar;
        youngSpruce = f.youngSpruce;
        youngLeafy = f.youngLeafy;
        bushShapes = f.bushes;
        bigTree = f.bigTree;
        tuftBerry = f.berry;
        tuftBerryNear = f.berryNear;
        tuftHeath = f.heath;
        tuftHeathNear = f.heathNear;
        tuftGrass = f.grass;
        tuftDry = f.dry;
        tuftShort = f.shortGrass;
        tuftFern = f.fern;
    }

    // ---- Colours (multiplied with the pictures; white = the picture as painted) ----
    static readonly TvColor White = new TvColor(255, 255, 255);
    static byte B(float v) => (byte)(v <= 0f ? 0 : v >= 1f ? 255 : (int)(v * 255f + 0.5f));
    static TvColor Grey(float v) => new TvColor(B(v), B(v), B(v));
    static TvColor Tint(float r, float g, float b) => new TvColor(B(r), B(g), B(b));

    static Flora BuildFlora(int q)
    {
        var random = new TvRandom(4711 + q);
        float Rr(float a, float b) => random.Range(a, b);
        var f = new Flora();

        // ---- A trunk: a slightly bent line of points, a flared foot ----
        float[] TrunkLine(float h, float bend, int segments, out float[] radius, float r0, float r1, float y0 = -0.35f)
        {
            var pts = new float[(segments + 1) * 3];
            radius = new float[segments + 1];
            float ax = Rr(-1f, 1f), az = Rr(-1f, 1f), bx = Rr(-1f, 1f), bz = Rr(-1f, 1f);
            for (int k = 0; k <= segments; k++)
            {
                float t = k / (float)segments, y = y0 + (h - y0) * t;
                float lean = bend * t * t;
                pts[k * 3] = ax * lean + bx * bend * 0.3f * (float)Math.Sin(t * 5.3f);
                pts[k * 3 + 1] = y;
                pts[k * 3 + 2] = az * lean + bz * bend * 0.3f * (float)Math.Sin(t * 4.1f + 1f);
                float flare = 1f + 0.6f * (float)Math.Exp(-Math.Max(0f, y) / 0.45f);
                radius[k] = (r0 + (r1 - r0) * (float)Math.Pow(t, 0.8)) * flare;
            }
            return pts;
        }
        // Where the trunk is at height y (for branches)
        void At(float[] pts, float y, out float x, out float z)
        {
            int count = pts.Length / 3;
            for (int k = 1; k < count; k++)
            {
                if (pts[k * 3 + 1] < y && k < count - 1) continue;
                float y0 = pts[k * 3 - 2], y1 = pts[k * 3 + 1], t = y1 > y0 ? Math.Max(0f, Math.Min(1f, (y - y0) / (y1 - y0))) : 0f;
                x = pts[k * 3 - 3] + (pts[k * 3] - pts[k * 3 - 3]) * t;
                z = pts[k * 3 - 1] + (pts[k * 3 + 2] - pts[k * 3 - 1]) * t;
                return;
            }
            x = z = 0f;
        }
        TvColor[] Shades(int count, TvColor low, TvColor high)
        {
            var s = new TvColor[count];
            for (int k = 0; k < count; k++) s[k] = low.Lerp(high, TvShapeBuilder.Smooth(0f, 0.35f, k / (float)Math.Max(1, count - 1)));
            return s;
        }
        // Part of a line of points (from point a to point b)
        float[] Part(float[] pts, int a, int b)
        {
            var r = new float[(b - a + 1) * 3];
            Array.Copy(pts, a * 3, r, 0, r.Length);
            return r;
        }
        float[] PartR(float[] rad, int a, int b)
        {
            var r = new float[b - a + 1];
            Array.Copy(rad, a, r, 0, r.Length);
            return r;
        }
        // A branch: from (x, y, z) out along (dx, dz) at an angle up (rise), length l, curving by `curve` (up > 0)
        float[] BranchLine(float x, float y, float z, float dx, float dz, float rise, float l, float curve, int segments)
        {
            var pts = new float[(segments + 1) * 3];
            for (int k = 0; k <= segments; k++)
            {
                float t = k / (float)segments, s = l * t;
                pts[k * 3] = x + dx * s * (float)Math.Cos(rise);
                pts[k * 3 + 1] = y + s * (float)Math.Sin(rise) + curve * l * t * t;
                pts[k * 3 + 2] = z + dz * s * (float)Math.Cos(rise);
            }
            return pts;
        }
        float[] Taper(int count, float r0, float r1)
        {
            var r = new float[count];
            for (int k = 0; k < count; k++) r[k] = r0 + (r1 - r0) * k / (float)Math.Max(1, count - 1);
            return r;
        }

        // ---- Scots pine ----
        TvShape Pine(int detail, float height, float crownStart)
        {
            var b = new TvShapeBuilder(random);
            float h = height * Rr(0.94f, 1.06f);
            int seg = detail >= 2 ? 10 : 6, sides = detail >= 3 ? 10 : detail >= 2 ? 8 : detail == 1 ? 6 : 5;
            float[] trunk = TrunkLine(h * 0.97f, Rr(0.15f, 0.5f), seg, out float[] rad, 0.21f, 0.035f);
            // Thick plated bark low, thin orange bark high up (the two overlap by a piece)
            int split = (int)(seg * Rr(0.42f, 0.55f));
            b.Limb(Part(trunk, 0, split + 1), PartR(rad, 0, split + 1), sides, TvAtlas.PineBarkLow, 3f, 0.9f,
                   Shades(split + 2, Tint(0.78f, 0.76f, 0.74f), Tint(1f, 0.98f, 0.96f)));
            var upper = PartR(rad, split, seg);
            for (int k = 0; k < upper.Length; k++) upper[k] *= 0.985f;
            b.Limb(Part(trunk, split, seg), upper, Math.Max(4, sides - 2), TvAtlas.PineBarkHigh, 2f, 0.6f,
                   new[] { Tint(0.9f, 0.88f, 0.85f), White });

            float crown0 = h * crownStart, crownH = h - crown0;
            float cy = crown0 + crownH * 0.55f;
            int branches = detail >= 3 ? 26 : detail == 2 ? 20 : detail == 1 ? 13 : 8;
            int cardsPer = detail >= 3 ? 7 : detail == 2 ? 5 : detail == 1 ? 3 : 2;
            float cardSize = detail >= 2 ? 1.3f : detail == 1 ? 1.6f : 2.2f;
            float az0 = Rr(0f, 6.28f);
            for (int i = 0; i < branches; i++)
            {
                float rel = (i + Rr(0.1f, 0.9f)) / branches;
                float y = crown0 + crownH * (float)Math.Pow(rel, 0.85f);
                float az = az0 + i * 2.39996f + Rr(-0.3f, 0.3f);
                float dx = (float)Math.Cos(az), dz = (float)Math.Sin(az);
                // Long and level low in the crown, short and steep at the top; the crown is flattish on top
                float l = (0.8f + 2.6f * (float)Math.Pow(1f - rel, 0.55f)) * Rr(0.75f, 1.15f);
                float rise = 0.12f + 0.55f * rel + Rr(-0.1f, 0.1f);
                At(trunk, y, out float tx, out float tz);
                float[] line = BranchLine(tx, y, tz, dx, dz, rise, l, Rr(-0.04f, 0.12f), detail >= 2 ? 3 : 2);
                if (detail >= 1)
                    b.Limb(line, Taper(line.Length / 3, 0.035f + 0.025f * (1f - rel), 0.012f), detail >= 3 ? 5 : 3, TvAtlas.SpruceBark, 1f, 0.5f,
                           new[] { Tint(0.7f, 0.62f, 0.55f) });
                // Needle brushes along the outer part, lying fairly flat, facing out of the crown
                for (int k = 0; k < cardsPer; k++)
                {
                    float t = 0.35f + 0.65f * (k + Rr(0.2f, 0.8f)) / cardsPer;
                    float px = tx + dx * l * t * (float)Math.Cos(rise), py = y + l * t * (float)Math.Sin(rise), pz = tz + dz * l * t * (float)Math.Cos(rise);
                    float size = cardSize * Rr(0.8f, 1.2f) * (0.75f + 0.35f * (1f - rel));
                    // The picture's branch runs from its lower left to its upper right: lay the card along the branch
                    float roll = Rr(-0.7f, 0.7f), turn = Rr(-0.6f, 0.6f);
                    float ux = (float)Math.Cos(az + turn) * (float)Math.Cos(rise), uy = (float)Math.Sin(rise) + Rr(-0.1f, 0.25f), uz = (float)Math.Sin(az + turn) * (float)Math.Cos(rise);
                    float sx = -(float)Math.Sin(az + turn) * (float)Math.Cos(roll), sy = (float)Math.Sin(roll), sz = (float)Math.Cos(az + turn) * (float)Math.Cos(roll);
                    float half = size * 0.5f;
                    float nx = px - tx, ny = (py - cy) * 0.8f + 0.9f * l, nz = pz - tz;
                    float shade = 0.5f + 0.32f * t;
                    TvColor c0 = Grey(shade * 0.8f), c1 = Grey(shade);
                    b.Card(px - ux * half * 0.9f, py - uy * half * 0.9f, pz - uz * half * 0.9f, sx * half, sy * half, sz * half,
                           ux * size, uy * size, uz * size, nx, ny, nz, TvAtlas.PineNeedles, c0, c1);
                    // A second brush across it, for a round clump of needles rather than a flat one
                    if (detail >= 1 && (k % 2 == 0 || detail >= 3))
                    {
                        float qx = -sy * uz + sz * uy, qy = -sz * ux + sx * uz, qz = -sx * uy + sy * ux;
                        b.Card(px - ux * half * 0.8f, py - uy * half * 0.8f, pz - uz * half * 0.8f, qx * half * 0.8f, qy * half * 0.8f, qz * half * 0.8f,
                               ux * size * 0.85f, uy * size * 0.85f, uz * size * 0.85f, nx, ny, nz, TvAtlas.PineNeedles, Grey(shade * 0.7f), Grey(shade * 0.92f));
                    }
                }
            }
            // The top: a few brushes standing up
            At(trunk, h * 0.97f, out float topX, out float topZ);
            for (int k = 0; k < (detail >= 2 ? 3 : 2); k++)
            {
                float a = az0 + k * 2.1f, s = cardSize * 0.9f;
                b.Card(topX, h * 0.9f, topZ, (float)Math.Cos(a) * s * 0.5f, 0f, (float)Math.Sin(a) * s * 0.5f, (float)Math.Sin(a) * 0.2f, s, -(float)Math.Cos(a) * 0.2f,
                       (float)Math.Sin(a), 1.2f, -(float)Math.Cos(a), TvAtlas.PineNeedles, Grey(0.9f), White);
            }
            // Old dead branch stubs low on the trunk
            if (detail >= 2)
                for (int k = 0; k < 5; k++)
                {
                    float y = Rr(2.5f, crown0 - 0.5f), az = Rr(0f, 6.28f);
                    At(trunk, y, out float tx, out float tz);
                    float[] line = BranchLine(tx, y, tz, (float)Math.Cos(az), (float)Math.Sin(az), Rr(-0.3f, 0.2f), Rr(0.25f, 0.7f), 0f, 1);
                    b.Limb(line, new[] { 0.03f, 0.012f }, 3, TvAtlas.SpruceBark, 1f, 0.5f, new[] { Tint(0.55f, 0.52f, 0.5f) });
                }
            return b.Done();
        }

        // ---- Norway spruce ----
        TvShape Spruce(int detail, float height, bool young)
        {
            var b = new TvShapeBuilder(random);
            float h = height * Rr(0.93f, 1.07f);
            int seg = young ? 3 : detail >= 2 ? 8 : 5;
            float[] trunk = TrunkLine(h, young ? 0.03f : Rr(0.05f, 0.2f), seg, out float[] rad, young ? 0.04f : 0.23f, young ? 0.008f : 0.025f, young ? -0.05f : -0.35f);
            if (!young || detail >= 2)
                b.Limb(trunk, rad, young ? 4 : detail >= 3 ? 9 : detail >= 2 ? 7 : 5, TvAtlas.SpruceBark, young ? 1f : 3f, 0.5f,
                       Shades(seg + 1, Tint(0.6f, 0.58f, 0.55f), Tint(0.92f, 0.9f, 0.88f)));
            // Live branches from low down (forest spruces lose the lowest ones in the shade)
            float live = young ? 0.02f : h * Rr(0.06f, 0.2f);
            float step = young ? (detail >= 2 ? 0.22f : 0.32f) : detail >= 3 ? 0.42f : detail == 2 ? 0.55f : detail == 1 ? 0.75f : 1.2f;
            int per = young ? (detail >= 2 ? 4 : 3) : detail >= 2 ? 5 : detail == 1 ? 4 : 3;
            float lmax = young ? h * 0.36f : 2.7f * Rr(0.85f, 1.1f);
            float az0 = Rr(0f, 6.28f);
            int whorl = 0;
            for (float y = live; y < h - (young ? 0.3f : 0.55f); y += step * Rr(0.85f, 1.15f), whorl++)
            {
                float rel = (y - live) / (h - live);
                float l = lmax * (float)Math.Pow(1f - rel, 0.92f) + (young ? 0.12f : 0.3f);
                At(trunk, y, out float tx, out float tz);
                for (int i = 0; i < per; i++)
                {
                    float az = az0 + whorl * 0.83f + i * 6.283f / per + Rr(-0.3f, 0.3f);
                    float dx = (float)Math.Cos(az), dz = (float)Math.Sin(az);
                    float ll = l * Rr(0.8f, 1.12f);
                    // Low branches droop, high ones point up
                    float rise = -0.25f + 0.55f * rel + Rr(-0.08f, 0.08f);
                    float half = ll * (young ? 0.36f : 0.34f);
                    float ux = dx * (float)Math.Cos(rise) * ll, uy = (float)Math.Sin(rise) * ll, uz = dz * (float)Math.Cos(rise) * ll;
                    // The spray lies flat, turned a little round the branch
                    float roll = Rr(-0.25f, 0.25f);
                    float sx = -dz * half * (float)Math.Cos(roll), sy = half * (float)Math.Sin(roll), sz = dx * half * (float)Math.Cos(roll);
                    float shade = 0.5f + 0.5f * Math.Min(1f, rel * 1.4f + 0.25f);
                    float nx = dx * 0.7f, ny = 1f, nz = dz * 0.7f;
                    b.Card(tx - ux * 0.04f, y, tz - uz * 0.04f, sx, sy, sz, ux, uy, uz, nx, ny, nz, TvAtlas.SpruceSpray,
                           Grey(shade * 0.7f), Grey(Math.Min(1f, shade * 1.08f)), detail >= 1 ? Rr(0.12f, 0.3f) : 0f);
                    // Hanging side sprays under it (the curtain of a spruce branch)
                    if (detail >= 2 && !young && rel < 0.85f)
                    {
                        float cx = tx + ux * 0.55f, cy = y + uy * 0.55f, cz = tz + uz * 0.55f;
                        float hang = ll * 0.32f;
                        b.Card(cx, cy - hang, cz, dx * ll * 0.22f, 0f, dz * ll * 0.22f, -dz * 0.05f, hang, dx * 0.05f, nx, ny * 0.6f, nz, TvAtlas.SpruceSpray,
                               Grey(shade * 0.55f), Grey(shade * 0.85f));
                    }
                }
                // Dead grey twigs under the live branches
                if (!young && detail >= 2 && whorl == 0)
                    for (float yd = 1.2f; yd < live - 0.2f; yd += 0.6f)
                    {
                        At(trunk, yd, out float dxp, out float dzp);
                        float a = Rr(0f, 6.28f), len = Rr(0.6f, 1.1f);
                        b.Card(dxp, yd - 0.2f, dzp, (float)Math.Sin(a) * 0.05f, len * 0.4f, -(float)Math.Cos(a) * 0.05f,
                               (float)Math.Cos(a) * len, -0.1f, (float)Math.Sin(a) * len, (float)Math.Cos(a), 0.5f, (float)Math.Sin(a), TvAtlas.DeadTwigs,
                               Grey(0.75f), Grey(0.85f), 0f, 0f, 1f, 0f, 1f);
                    }
            }
            // The leader: an upright spray at the very top
            At(trunk, h - 0.6f, out float lx, out float lz);
            for (int k = 0; k < 2; k++)
            {
                float a = az0 + k * 1.57f, s = young ? 0.25f : 0.5f;
                b.Card(lx, h - (young ? 0.35f : 0.9f), lz, (float)Math.Cos(a) * s * 0.4f, 0f, (float)Math.Sin(a) * s * 0.4f, 0f, young ? 0.5f : 1.1f, 0f,
                       (float)Math.Cos(a + 1.57f), 1f, (float)Math.Sin(a + 1.57f), TvAtlas.SpruceSpray, Grey(0.85f), White);
            }
            return b.Done();
        }

        // ---- Birch ----
        TvShape Birch(int detail, float height, bool young)
        {
            var b = new TvShapeBuilder(random);
            float h = height * Rr(0.92f, 1.08f);
            int seg = young ? 3 : detail >= 2 ? 8 : 5, sides = young ? 4 : detail >= 3 ? 9 : detail >= 2 ? 7 : 5;
            float[] trunk = TrunkLine(h * 0.95f, young ? 0.08f : Rr(0.2f, 0.6f), seg, out float[] rad, young ? 0.035f : 0.15f, young ? 0.008f : 0.03f, young ? -0.05f : -0.3f);
            b.Limb(trunk, rad, sides, TvAtlas.BirchBark, young ? 1f : 2f, 1.1f, Shades(seg + 1, Tint(0.8f, 0.8f, 0.78f), White));
            // The rough dark foot of an old birch
            if (!young && detail >= 1)
            {
                var foot = Part(trunk, 0, 2);
                var footR = PartR(rad, 0, 2);
                for (int k = 0; k < footR.Length; k++) footR[k] *= 1.03f;
                b.Limb(foot, footR, sides, TvAtlas.SpruceBark, 3f, 0.4f, new[] { Tint(0.45f, 0.43f, 0.42f), Tint(0.6f, 0.58f, 0.56f) });
            }
            float crown0 = h * (young ? 0.25f : Rr(0.4f, 0.52f)), crownH = h - crown0;
            int branches = young ? (detail >= 2 ? 5 : 3) : detail >= 3 ? 18 : detail == 2 ? 13 : detail == 1 ? 9 : 6;
            int cardsPer = young ? 2 : detail >= 3 ? 6 : detail == 2 ? 4 : detail == 1 ? 3 : 2;
            float size = young ? 0.7f : detail >= 2 ? 1.25f : detail == 1 ? 1.55f : 2.1f;
            float az0 = Rr(0f, 6.28f), cy = crown0 + crownH * 0.5f;
            for (int i = 0; i < branches; i++)
            {
                float rel = (i + Rr(0.1f, 0.9f)) / branches;
                float y = crown0 + crownH * rel * 0.92f;
                float az = az0 + i * 2.39996f + Rr(-0.3f, 0.3f);
                float dx = (float)Math.Cos(az), dz = (float)Math.Sin(az);
                float l = (young ? 0.5f : 1.1f + 2.3f * (float)Math.Sin(Math.PI * Math.Min(1f, rel * 0.9f + 0.15f))) * Rr(0.8f, 1.15f);
                float rise = 0.6f + 0.3f * rel;
                At(trunk, y, out float tx, out float tz);
                float[] line = BranchLine(tx, y, tz, dx, dz, rise, l, -0.35f, detail >= 2 ? 3 : 2);
                if (detail >= 1 && !young)
                    b.Limb(line, Taper(line.Length / 3, 0.03f + 0.02f * (1f - rel), 0.008f), detail >= 3 ? 4 : 3, TvAtlas.SpruceBark, 1f, 0.5f,
                           new[] { Tint(0.45f, 0.4f, 0.38f) });
                // Hanging twigs of leaves along the outer part
                for (int k = 0; k < cardsPer; k++)
                {
                    float t = 0.3f + 0.7f * (k + Rr(0.2f, 0.8f)) / cardsPer;
                    int seg2 = line.Length / 3 - 1;
                    float ft = t * seg2;
                    int i0 = Math.Min(seg2 - 1, (int)ft);
                    float fr = ft - i0;
                    float px = line[i0 * 3] + (line[i0 * 3 + 3] - line[i0 * 3]) * fr, py = line[i0 * 3 + 1] + (line[i0 * 3 + 4] - line[i0 * 3 + 1]) * fr,
                          pz = line[i0 * 3 + 2] + (line[i0 * 3 + 5] - line[i0 * 3 + 2]) * fr;
                    float s = size * Rr(0.8f, 1.2f);
                    float a = az + Rr(-1.2f, 1.2f);
                    float sx = -(float)Math.Sin(a) * s * 0.5f, sz = (float)Math.Cos(a) * s * 0.5f;
                    float out1 = Rr(0.05f, 0.25f);
                    float shade = 0.6f + 0.4f * t;
                    b.Card(px + dx * out1 * s, py - s * 0.9f, pz + dz * out1 * s, sx, 0f, sz, dx * out1 * s * -0.5f, s, dz * out1 * s * -0.5f,
                           px - tx, (py - cy) * 0.7f + 0.8f, pz - tz, TvAtlas.BirchLeaves, Grey(shade * 0.85f), Grey(Math.Min(1f, shade * 1.05f)));
                }
            }
            return b.Done();
        }

        // ---- Beech, oak and other broad-leaved trees: a smooth grey trunk and a big crown of leaf clusters ----
        TvShape Broad(int detail, float height, float width, int barkTile, TvColor bark)
        {
            var b = new TvShapeBuilder(random);
            float h = height * Rr(0.92f, 1.08f);
            int seg = detail >= 2 ? 6 : 4, sides = detail >= 3 ? 10 : detail >= 2 ? 8 : 5;
            float[] trunk = TrunkLine(h * 0.6f, Rr(0.2f, 0.5f), seg, out float[] rad, 0.32f, 0.16f);
            b.Limb(trunk, rad, sides, barkTile, 3f, 0.9f, Shades(seg + 1, bark.Shade(0.65f), bark));
            float cy = h * 0.62f, rx = width, ry = h * 0.33f;
            int branches = detail >= 3 ? 12 : detail == 2 ? 9 : detail == 1 ? 6 : 4;
            At(trunk, h * 0.6f, out float tx, out float tz);
            var tips = new List<float>();
            for (int i = 0; i < branches; i++)
            {
                float az = i * 2.39996f + Rr(-0.3f, 0.3f), rise = Rr(0.45f, 1.1f);
                float l = Rr(0.55f, 0.9f) * rx;
                float[] line = BranchLine(tx, h * Rr(0.45f, 0.6f), tz, (float)Math.Cos(az), (float)Math.Sin(az), rise, l, -0.1f, 3);
                if (detail >= 1) b.Limb(line, Taper(4, 0.12f, 0.03f), detail >= 2 ? 5 : 4, barkTile, 1f, 0.7f, new[] { bark.Shade(0.8f) });
                tips.Add(line[9]); tips.Add(line[10]); tips.Add(line[11]);
            }
            int cards = detail >= 3 ? 120 : detail == 2 ? 80 : detail == 1 ? 45 : 22;
            float size = detail >= 2 ? 1.7f : detail == 1 ? 2.2f : 3f;
            for (int k = 0; k < cards; k++)
            {
                // Points in the crown's shell, more on the outside and the top
                float u = Rr(0f, 6.283f), v = (float)Math.Acos(Rr(-0.55f, 1f)), r = (float)Math.Pow(Rr(0.35f, 1f), 0.5f);
                float ox = (float)(Math.Sin(v) * Math.Cos(u)), oy = (float)Math.Cos(v), oz = (float)(Math.Sin(v) * Math.Sin(u));
                float px = tx + ox * rx * r, py = cy + oy * ry * r, pz = tz + oz * rx * r;
                // Facing out, turned at random round that
                float s = size * Rr(0.8f, 1.2f);
                float ax = -oz, az = ox;
                float al = (float)Math.Sqrt(ax * ax + az * az);
                if (al < 0.1f) { ax = 1f; az = 0f; al = 1f; }
                ax /= al; az /= al;
                float roll = Rr(0f, 6.28f), cr = (float)Math.Cos(roll), sr = (float)Math.Sin(roll);
                // s = in the plane at right angles to (ox, oy, oz): mix of the horizontal tangent and its cross
                float bx = oy * az, by = oz * ax - ox * az, bz = -oy * ax;
                float sx = (ax * cr + bx * sr) * s * 0.5f, sy = (by * sr) * s * 0.5f, sz = (az * cr + bz * sr) * s * 0.5f;
                float upx = (-ax * sr + bx * cr) * s, upy = (by * cr) * s, upz = (-az * sr + bz * cr) * s;
                float shade = 0.55f + 0.45f * r * (0.75f + 0.25f * oy);
                b.Card(px - upx * 0.5f, py - upy * 0.5f, pz - upz * 0.5f, sx, sy, sz, upx, upy, upz, ox, oy + 0.3f, oz, TvAtlas.BroadLeaves,
                       Grey(shade * 0.85f), Grey(Math.Min(1f, shade * 1.05f)));
            }
            return b.Done();
        }

        // ---- A bush (hazel, rowan, willow): a few stems and leaf clusters in a dome ----
        TvShape Bush(int detail)
        {
            var b = new TvShapeBuilder(random);
            int stems = detail >= 2 ? 6 : 4;
            for (int i = 0; i < stems; i++)
            {
                float a = i * 2.4f + Rr(0f, 0.5f), lean = Rr(0.25f, 0.6f);
                float[] line = BranchLine(Rr(-0.1f, 0.1f), -0.05f, Rr(-0.1f, 0.1f), (float)Math.Cos(a), (float)Math.Sin(a), 1.57f - lean, Rr(1.1f, 1.6f), -0.1f, 3);
                b.Limb(line, Taper(4, 0.03f, 0.01f), 3, TvAtlas.BeechBark, 1f, 0.4f, new[] { Tint(0.55f, 0.5f, 0.45f) });
            }
            int cards = detail >= 3 ? 30 : detail == 2 ? 20 : detail == 1 ? 12 : 7;
            for (int k = 0; k < cards; k++)
            {
                float u = Rr(0f, 6.283f), v = (float)Math.Acos(Rr(-0.2f, 1f)), r = Rr(0.5f, 1f);
                float ox = (float)(Math.Sin(v) * Math.Cos(u)), oy = (float)Math.Cos(v), oz = (float)(Math.Sin(v) * Math.Sin(u));
                float px = ox * 0.75f * r, py = 0.75f + oy * 0.65f * r, pz = oz * 0.75f * r;
                float s = Rr(0.7f, 1f), a = Rr(0f, 6.28f);
                float shade = 0.55f + 0.45f * r * (0.7f + 0.3f * oy);
                b.Card(px, py - s * 0.5f, pz, (float)Math.Cos(a) * s * 0.5f, Rr(-0.15f, 0.15f), (float)Math.Sin(a) * s * 0.5f, ox * 0.3f, s, oz * 0.3f,
                       ox, oy + 0.4f, oz, TvAtlas.BroadLeaves, Grey(shade * 0.8f), Grey(Math.Min(1f, shade * 1.05f)));
            }
            return b.Done();
        }

        // ---- Undergrowth ----
        TvShape Star(int cards, float w, float h, float lean, int tile, TvColor c0, TvColor c1)
        {
            var b = new TvShapeBuilder(random);
            b.Star(0f, -0.02f, 0f, cards, w, h, lean, tile, c0, c1, Rr(0f, 3.14f));
            return b.Done();
        }
        TvShape FernShape(int fronds)
        {
            var b = new TvShapeBuilder(random);
            for (int i = 0; i < fronds; i++)
            {
                float a = i * 6.283f / fronds + Rr(-0.3f, 0.3f), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                float l = Rr(0.55f, 0.85f), rise = Rr(0.45f, 0.8f);
                float w = l * 0.3f;
                b.Card(0f, -0.02f, 0f, -sa * w, 0f, ca * w, ca * l * (float)Math.Cos(rise), l * (float)Math.Sin(rise), sa * l * (float)Math.Cos(rise),
                       ca * 0.3f, 1f, sa * 0.3f, TvAtlas.Fern, Grey(0.6f), White, Rr(0.35f, 0.6f));
            }
            return b.Done();
        }

        // ---- The shapes for this quality ----
        int near = q >= 3 ? 3 : q == 2 ? 2 : 1;
        int far = 0;
        f.pineNear = new[] { Pine(near, 19f, 0.6f), Pine(near, 19f, 0.66f), Pine(near, 18f, 0.55f) };
        f.pineFar = new[] { Pine(far, 19f, 0.6f), Pine(far, 19f, 0.66f), Pine(far, 18f, 0.55f) };
        // The second pine: shorter, a deeper crown (younger)
        f.pine2Near = new[] { Pine(near, 15f, 0.45f), Pine(near, 14f, 0.5f) };
        f.pine2Far = new[] { Pine(far, 15f, 0.45f), Pine(far, 14f, 0.5f) };
        f.spruceNear = new[] { Spruce(near, 18f, false), Spruce(near, 19f, false), Spruce(near, 16f, false) };
        f.spruceFar = Spruce(far, 18f, false);
        f.birchNear = new[] { Birch(near, 14f, false), Birch(near, 15f, false) };
        f.birchFar = new[] { Birch(far, 14f, false), Birch(far, 15f, false) };
        TvColor beechBark = Tint(0.95f, 0.95f, 0.93f);
        f.beechNear = new[] { Broad(near, 20f, 4.5f, TvAtlas.BeechBark, beechBark), Broad(near, 19f, 4f, TvAtlas.BeechBark, beechBark) };
        f.beechFar = new[] { Broad(far, 20f, 4.5f, TvAtlas.BeechBark, beechBark), Broad(far, 19f, 4f, TvAtlas.BeechBark, beechBark) };
        // A distinct big tree: an old oak with dark furrowed bark
        f.bigTree = Broad(Math.Max(near, 2), 13f, 5.5f, TvAtlas.PineBarkLow, Tint(0.62f, 0.6f, 0.58f));
        int young = q >= 2 ? 2 : 1;
        f.youngSpruce = new[] { Spruce(young, 2.5f, true), Spruce(young, 2.5f, true), Spruce(young, 2.4f, true) };
        f.youngLeafy = new[] { Birch(young, 1.9f, true), Birch(young, 2f, true) };
        f.bushes = new[] { Bush(near), Bush(near) };

        int few = q == 0 ? 3 : 4, many = q >= 3 ? 7 : q == 2 ? 6 : 5;
        f.berry = Star(few, 0.7f, 0.3f, 0.4f, TvAtlas.Bilberry, Tint(0.42f, 0.46f, 0.44f), Tint(0.74f, 0.8f, 0.74f));
        f.berryNear = Star(many, 0.75f, 0.32f, 0.45f, TvAtlas.Bilberry, Tint(0.42f, 0.46f, 0.44f), Tint(0.74f, 0.8f, 0.74f));
        f.heath = Star(few, 0.45f, 0.32f, 0.3f, TvAtlas.Heather, Grey(0.65f), Grey(0.95f));
        f.heathNear = Star(many, 0.5f, 0.34f, 0.35f, TvAtlas.Heather, Grey(0.65f), Grey(0.95f));
        f.grass = Star(few + 1, 0.5f, 0.55f, 0.45f, TvAtlas.Grass, Grey(0.6f), White);
        f.dry = Star(few, 0.45f, 0.5f, 0.45f, TvAtlas.Grass, Tint(0.95f, 0.85f, 0.6f), Tint(1f, 0.95f, 0.75f));
        f.shortGrass = Star(few, 0.4f, 0.22f, 0.5f, TvAtlas.Grass, Grey(0.6f), Grey(0.9f));
        f.fern = FernShape(q >= 2 ? 7 : 5);
        return f;
    }

    // ---- The forest floor's litter: needles, twigs, cones and leaves lying on the ground, and dead sticks ----
    // Only in the near detail (round the TV control, or the pieces right round the runner); none on Low.
    void Litter()
    {
        if (Q == 0) return;
        const float Reach = 60f;
        float per = Q >= 3 ? 0.5f : Q == 2 ? 0.28f : 0.12f;   // cards per square metre of forest
        float area = chunk ? (2f * R) * (2f * R) : (float)(Math.PI * Reach * Reach);
        int tries = (int)(area * per);
        var b = new TvShapeBuilder(random);
        float Height(float x, float z) => H(x, z);
        for (int i = 0; i < tries; i++)
        {
            float x, z;
            if (chunk) { x = Rr(-R, R); z = Rr(-R, R); }
            else
            {
                float a = Rr(0f, 6.283f), d = (float)Math.Sqrt(random.Value()) * Reach;
                x = (float)Math.Cos(a) * d;
                z = (float)Math.Sin(a) * d;
            }
            if (!Inside(x, z, 2f)) continue;
            byte cell = CellAt(x, z);
            int kind = Base(cell);
            bool forest = kind == WorldCell.Forest || kind == WorldCell.LightGreen || kind == WorldCell.Green || kind == WorldCell.DarkGreen
                          || kind == WorldCell.OpenTrees || kind == WorldCell.RoughOpenTrees;
            if (!forest || (cell & WorldCell.Marsh) != 0) continue;
            if (ClearAt(x, z) < 0.6f) continue;
            float tint = Rr(0.75f, 1.05f);
            TvColor c = Grey(tint);
            if (random.Value() < 0.22f)
            {
                // A dead stick: long and narrow (the stick fills the middle band of its picture)
                float len = Rr(0.35f, 1.1f);
                b.GroundCard(x, z, len * 0.5f, len * 0.16f, Rr(0f, 6.283f), Height, TvAtlas.Twig, c, 0.34f, 0.66f, 0.02f);
            }
            else
            {
                float s = Rr(0.5f, 0.9f);
                b.GroundCard(x, z, s, s, Rr(0f, 6.283f), Height, TvAtlas.Litter, c, 0f, 1f, 0.01f);
            }
        }
        if (b.Empty) return;
        litterMesh.Add(b.Done(), 0f, 0f, 0f, 1f, 1f, 1f, 0f);
    }
}
