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
        public TvShape[] mounds, berrySet, heathSet, lichens;
        public TvShape berry, berryNear, heath, heathNear, grass, dry, shortGrass, fern, lingon, sedge, flowers, juniper;
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
        moundShapes = f.mounds;
        lichenShapes = f.lichens;
        berrySet = f.berrySet;
        heathSet = f.heathSet;
        tuftBerry = f.berry;
        tuftBerryNear = f.berryNear;
        tuftHeath = f.heath;
        tuftHeathNear = f.heathNear;
        tuftGrass = f.grass;
        tuftDry = f.dry;
        tuftShort = f.shortGrass;
        tuftFern = f.fern;
        tuftLingon = f.lingon;
        tuftSedge = f.sedge;
        tuftFlowers = f.flowers;
        tuftJuniper = f.juniper;
    }

    TvShape tuftLingon, tuftSedge, tuftFlowers, tuftJuniper;
    TvShape[] moundShapes, berrySet, heathSet, lichenShapes;

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
        float Flare = 0.6f; // how much a trunk widens at its foot (TrunkLine)

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
                float flare = 1f + Flare * (float)Math.Exp(-Math.Max(0f, y) / 0.45f);
                radius[k] = (r0 + (r1 - r0) * (float)Math.Pow(t, 0.8)) * flare;
            }
            return pts;
        }
        // How many times a picture size wide goes round a trunk of this radius (a whole number: no seam)
        float RadiusAt(float[] line, float[] rad, float y)
        {
            int n = rad.Length;
            for (int k = 1; k < n; k++)
                if (line[k * 3 + 1] >= y)
                {
                    float y0 = line[k * 3 - 2], y1 = line[k * 3 + 1];
                    float t = y1 > y0 ? (y - y0) / (y1 - y0) : 0f;
                    return rad[k - 1] + (rad[k] - rad[k - 1]) * Math.Max(0f, Math.Min(1f, t));
                }
            return rad[n - 1];
        }
        float Around(float radius, float size) => Math.Max(1f, (float)Math.Round(6.2832f * radius / size));

        // Moss and lichen growing up the foot of a trunk to `high` metres, ragged at the top (TvAtlas.MossSock)
        void MossFoot(TvShapeBuilder b, float[] trunk, float[] rad, int sides, float high)
        {
            const int Rings = 4;
            var pts = new float[Rings * 3];
            var r = new float[Rings];
            float y0 = trunk[1];
            for (int k = 0; k < Rings; k++)
            {
                float y = y0 + (high - y0) * k / (Rings - 1f);
                At(trunk, y, out float x, out float z);
                pts[k * 3] = x; pts[k * 3 + 1] = y; pts[k * 3 + 2] = z;
                float t = Math.Min(1f, (y - y0) / Math.Max(0.01f, trunk[trunk.Length - 2] - y0)) * (rad.Length - 1);
                int i = Math.Min(rad.Length - 2, (int)t);
                r[k] = (rad[i] + (rad[i + 1] - rad[i]) * (t - i)) * 1.065f + 0.006f;
            }
            b.Limb(pts, r, sides, TvAtlas.MossSock, Around(r[0], 0.5f), (high - y0) * 1.03f, new[] { Grey(0.85f), White });
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
            Flare = 0.35f; // pines hardly widen at the foot (Anders' photo)
            float thick = Rr(0.7f, 1.08f); // some pines slender, some stout
            float[] trunk = TrunkLine(h * 0.97f, Rr(0.15f, 0.6f), seg, out float[] rad, 0.21f * thick, 0.035f);
            Flare = 0.6f;
            // Thick plated bark low, thin orange bark high up (the two overlap by a piece)
            // (2026-11-25, Anders: the bark looked stretched and dark) grey-brown plates most of the way up, a picture
            // every 0.55 m both round and along the trunk, so the plates keep their shape
            int split = (int)(seg * Rr(0.22f, 0.5f));   // the orange starts 4-9 m up (Anders' photos)
            // (round 6) one of two bark pictures, moved a random bit up the trunk, and its own shade of grey or brown,
            // so no two pines look alike
            b.LimbRough = detail >= 2 ? 0.05f : 0f;
            b.LimbShift = Rr(0f, 0.8f);
            int barkTile = random.Value() < 0.5f ? TvAtlas.PineBarkLow : TvAtlas.PineBarkLow2;
            float lum = Rr(0.95f, 1.2f), warm = Rr(-0.04f, 0.05f);
            TvColor barkTint = Tint(lum * (1f + warm), lum, lum * (1f - warm));
            b.Limb(Part(trunk, 0, split + 1), PartR(rad, 0, split + 1), sides, barkTile, Around(rad[1], 0.4f), 0.8f,
                   Shades(split + 2, barkTint.Shade(0.86f), barkTint));
            b.LimbRough = 0f;
            b.LimbShift = 0f;
            var upper = PartR(rad, split, seg);
            for (int k = 0; k < upper.Length; k++) upper[k] *= 0.985f;
            b.Limb(Part(trunk, split, seg), upper, Math.Max(4, sides - 2), TvAtlas.PineBarkHigh, Around(upper[0], 0.45f), 0.45f,
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
                    if (detail >= 1 && (k % 2 == 0 || detail >= 2))
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
                for (int k = 0; k < 3; k++)
                {
                    // grey, weathered, starting just inside the bark (2026-11-25: dark stubs looked like holes)
                    float y = Rr(3f, crown0 - 0.5f), az = Rr(0f, 6.28f);
                    At(trunk, y, out float tx, out float tz);
                    float r = RadiusAt(trunk, rad, y) * 0.8f, dx = (float)Math.Cos(az), dz = (float)Math.Sin(az);
                    float[] line = BranchLine(tx + dx * r, y, tz + dz * r, dx, dz, Rr(-0.3f, 0.2f), Rr(0.2f, 0.5f), 0f, 1);
                    b.Limb(line, new[] { 0.028f, 0.01f }, 3, TvAtlas.DeadWood, 1f, 0.5f, new[] { Tint(0.78f, 0.76f, 0.74f) });
                }
            return b.Done();
        }

        // ---- Norway spruce ----
        TvShape Spruce(int detail, float height, bool young, float crown = 0.12f)
        {
            var b = new TvShapeBuilder(random);
            float h = height * Rr(0.93f, 1.07f);
            int seg = young ? 3 : detail >= 2 ? 8 : 5;
            float[] trunk = TrunkLine(h, young ? 0.03f : Rr(0.03f, 0.14f), seg, out float[] rad, young ? 0.04f : 0.21f * Rr(0.8f, 1.12f), young ? 0.008f : 0.025f, young ? -0.05f : -0.35f);
            if (!young || detail >= 2)
                b.Limb(trunk, rad, young ? 4 : detail >= 3 ? 9 : detail >= 2 ? 7 : 5, TvAtlas.SpruceBark, young ? 1f : Around(rad[1], 0.4f), 0.4f,
                       Shades(seg + 1, Tint(0.86f, 0.85f, 0.83f), White));
            // Live branches: an open-grown spruce keeps them from low down; one in a forest loses the lower half in
            // the shade and keeps only dead grey twigs there (2026-11-25, Anders' photos of spruce forest)
            float live = young ? 0.02f : h * crown * Rr(0.85f, 1.15f);
            float step = young ? (detail >= 2 ? 0.22f : 0.32f) : detail >= 3 ? 0.42f : detail == 2 ? 0.55f : detail == 1 ? 0.75f : 1.2f;
            int per = young ? (detail >= 2 ? 4 : 3) : detail >= 2 ? 5 : detail == 1 ? 4 : 3;
            float lmax = young ? h * 0.36f : (crown > 0.3f ? 2.0f : 2.7f) * Rr(0.85f, 1.1f);
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
                           Grey(shade * 0.82f), Grey(Math.Min(1f, shade * 1.1f)), detail >= 1 ? Rr(0.12f, 0.3f) : 0f);
                    // Hanging side sprays under it (the curtain of a spruce branch)
                    if (detail >= 2 && !young && rel < 0.85f)
                    {
                        float cx = tx + ux * 0.55f, cy = y + uy * 0.55f, cz = tz + uz * 0.55f;
                        float hang = ll * 0.32f;
                        b.Card(cx, cy - hang, cz, dx * ll * 0.22f, 0f, dz * ll * 0.22f, -dz * 0.05f, hang, dx * 0.05f, nx, ny * 0.6f, nz, TvAtlas.SpruceSpray,
                               Grey(shade * 0.8f), Grey(shade * 1.02f));
                    }
                }
                // Dead grey twigs under the live branches: thin dead branches sticking out all round, longer just under
                // the crown, short broken stubs low down
                if (!young && detail >= 1 && whorl == 0)
                    for (float yd = 1.0f; yd < live - 0.1f; yd += (detail >= 2 ? 0.3f : 0.6f) * Rr(0.7f, 1.3f))
                    {
                        At(trunk, yd, out float dxp, out float dzp);
                        float up = (yd - 1f) / Math.Max(0.5f, live - 1f);
                        int twigs = detail >= 2 ? 2 : 1;
                        for (int k = 0; k < twigs; k++)
                        {
                            float a = Rr(0f, 6.28f), len = Rr(0.35f, 0.7f) + 0.7f * up * Rr(0.5f, 1.2f);
                            float r0 = RadiusAt(trunk, rad, yd) * 0.85f, ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                            if (detail >= 2 && k == 0)
                            {
                                float[] line = BranchLine(dxp + ca * r0, yd, dzp + sa * r0, ca, sa, Rr(-0.45f, 0.0f), Math.Min(0.55f, len * 0.45f), -0.04f, 1);
                                b.Limb(line, new[] { 0.013f, 0.004f }, 3, TvAtlas.DeadWood, 1f, 0.5f, new[] { Tint(0.9f, 0.88f, 0.85f) });
                            }
                            else
                                b.Card(dxp + ca * r0, yd - len * 0.25f, dzp + sa * r0, -sa * 0.05f, len * 0.5f, ca * 0.05f,
                                       ca * len, -0.12f * len, sa * len, ca, 0.5f, sa, TvAtlas.DeadTwigs,
                                       Grey(0.7f), Grey(0.82f), 0f, 0f, 1f, 0f, 1f);
                        }
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
            // White with black marks all the way (2026-11-25, Anders: not a black foot and a white top); a picture every
            // 0.5 m round and along, so the marks are not stretched
            b.Limb(trunk, rad, sides, TvAtlas.BirchBark, young ? 1f : Around(rad[1], 0.5f), 1.0f, Shades(seg + 1, Grey(0.9f), White));
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
                    if (detail >= 1)
                        Clump(b, px + dx * out1 * s, py - s * 0.45f, pz + dz * out1 * s, s * 0.8f, px - tx, (py - cy) * 0.7f + 0.8f, pz - tz,
                              TvAtlas.BirchLeaves, Grey(shade * 0.85f), Grey(Math.Min(1f, shade * 1.05f)), detail >= 2 ? 3 : 2);
                    else
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
            bool pineBark = barkTile == TvAtlas.PineBarkLow; // that picture is 0.4 m round by 0.8 m up
            b.Limb(trunk, rad, sides, barkTile, Around(rad[1], pineBark ? 0.4f : 0.6f), pineBark ? 0.8f : 0.6f, Shades(seg + 1, bark.Shade(0.8f), bark));
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
                if (detail >= 1)
                    Clump(b, px, py, pz, s * 0.8f, ox, oy + 0.3f, oz, TvAtlas.BroadLeaves, Grey(shade * 0.85f), Grey(Math.Min(1f, shade * 1.05f)),
                          detail >= 2 ? 3 : 2);
                else
                    b.Card(px - upx * 0.5f, py - upy * 0.5f, pz - upz * 0.5f, sx, sy, sz, upx, upy, upz, ox, oy + 0.3f, oz, TvAtlas.BroadLeaves,
                           Grey(shade * 0.85f), Grey(Math.Min(1f, shade * 1.05f)));
            }
            return b.Done();
        }

        // Three cards crossing at one spot (2026-11-25, Anders: crowns and bushes were flat crossed pictures with no
        // volume): two standing at right angles and one lying across them, each turned at random, so a clump of
        // leaves looks full from every side and from above. Far away (cards 1 or 2) only the standing ones.
        void Clump(TvShapeBuilder b, float px, float py, float pz, float s, float nx, float ny, float nz, int tile, TvColor lo, TvColor hi, int cards = 3)
        {
            float a = Rr(0f, 6.283f), h = s * 0.5f;
            for (int i = 0; i < cards; i++)
            {
                float ai = a + i * 1.5708f + Rr(-0.25f, 0.25f), ca = (float)Math.Cos(ai), sa = (float)Math.Sin(ai);
                if (i == 2)
                {
                    // lying, tilted a little, lit from above
                    float tilt = Rr(-0.3f, 0.3f);
                    float ux = -sa * s, uy = tilt * s, uz = ca * s;
                    b.Card(px - ux * 0.5f, py - uy * 0.5f, pz - uz * 0.5f, ca * h, Rr(-0.15f, 0.15f) * h, sa * h, ux, uy, uz,
                           nx, ny + 0.6f, nz, tile, lo.Lerp(hi, 0.4f), hi);
                }
                else
                {
                    float lx = Rr(-0.3f, 0.3f) * s, lz = Rr(-0.3f, 0.3f) * s;
                    b.Card(px - lx * 0.5f, py - s * 0.5f, pz - lz * 0.5f, ca * h, 0f, sa * h, lx, s, lz, nx, ny, nz, tile, lo, hi);
                }
            }
        }

        // ---- Juniper: a narrow column of small needle clumps round a dark core, not a star of flat cards ----
        TvShape JuniperShape(int detail)
        {
            var b = new TvShapeBuilder(random);
            float h = Rr(1.3f, 2.1f), w = Rr(0.35f, 0.55f);
            b.Star(0f, -0.02f, 0f, 3, w * 1.6f, h * 0.95f, 0.04f, TvAtlas.Juniper, Grey(0.4f), Grey(0.7f), Rr(0f, 3.14f));
            int n = detail >= 2 ? 16 : 9, cards = detail >= 2 ? 3 : 2;
            for (int k = 0; k < n; k++)
            {
                float t = (k + Rr(0.1f, 0.9f)) / n, y = h * (0.12f + 0.8f * t);
                float r = w * (float)Math.Pow(1f - t * 0.85f, 0.6f) * Rr(0.6f, 1f), a = Rr(0f, 6.283f);
                float ox = (float)Math.Cos(a), oz = (float)Math.Sin(a);
                float s = Rr(0.45f, 0.7f) * (1f - 0.4f * t);
                float shade = 0.55f + 0.4f * r / w;
                Clump(b, ox * r, y, oz * r, s, ox, 0.3f, oz, TvAtlas.Juniper, Grey(shade * 0.75f), Grey(Math.Min(1f, shade * 1.1f)), cards);
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
            int cards = detail >= 3 ? 40 : detail == 2 ? 28 : detail == 1 ? 14 : 7;
            for (int k = 0; k < cards; k++)
            {
                float u = Rr(0f, 6.283f), v = (float)Math.Acos(Rr(-0.2f, 1f)), r = Rr(0.5f, 1f);
                float ox = (float)(Math.Sin(v) * Math.Cos(u)), oy = (float)Math.Cos(v), oz = (float)(Math.Sin(v) * Math.Sin(u));
                float px = ox * 0.75f * r, py = 0.75f + oy * 0.65f * r, pz = oz * 0.75f * r;
                float s = Rr(0.7f, 1f), a = Rr(0f, 6.28f);
                float shade = 0.55f + 0.45f * r * (0.7f + 0.3f * oy);
                Clump(b, px, py, pz, s * 0.75f, ox, oy + 0.4f, oz, TvAtlas.BroadLeaves, Grey(shade * 0.8f), Grey(Math.Min(1f, shade * 1.05f)),
                      detail >= 2 ? 3 : detail == 1 ? 2 : 1);
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
        TvShape Berry(int cards, float w, float h, int tops)
        {
            var b = new TvShapeBuilder(random);
            b.Star(0f, -0.02f, 0f, cards, w, h, 0.3f, TvAtlas.Bilberry, Grey(0.6f), White, Rr(0f, 3.14f), 0.25f);
            for (int i = 0; i < tops; i++)
            {
                // A roof of leaves, a little tilted, at the top of the clump
                float size = w * Rr(0.45f, 0.7f), y = h * Rr(0.55f, 0.88f), a = Rr(0f, 6.283f);
                float ox = Rr(-0.25f, 0.25f) * w, oz = Rr(-0.25f, 0.25f) * w;
                float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a), tilt = Rr(-0.3f, 0.3f) * size;
                b.Card(ox - sa * size * 0.5f, y - tilt * 0.5f, oz + ca * size * 0.5f, ca * size * 0.5f, 0f, sa * size * 0.5f,
                       sa * size, tilt, -ca * size, 0f, 1f, 0f, TvAtlas.BilberryTop, Grey(0.92f), White);
            }
            return b.Done();
        }

        // One bush you can tell apart (2026-11-25, Anders: every single bilberry bush clear, leaves and not a sea of
        // green; the same for heather): a ring of upright cards with the bush from the side, leaning out a little,
        // darker low down and at the back, and a few tilted cards on top with the bush from above. Small, so the
        // bushes stand one by one with moss or grass showing between them.
        TvShape OneBush(int sides, int tops, float r, float h, int sideTile, int topTile, float dark)
        {
            var b = new TvShapeBuilder(random);
            float turn = Rr(0f, 6.283f);
            for (int i = 0; i < sides; i++)
            {
                float a = turn + i * 6.283f / sides + Rr(-0.3f, 0.3f), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                float d = r * Rr(0.15f, 0.45f), w = r * Rr(0.75f, 1.05f), hh = h * Rr(0.8f, 1.15f), lean = Rr(0.1f, 0.3f) * hh;
                // the card faces out from the middle (its normal along ca, sa), its width runs round
                b.Card(ca * d, -0.03f, sa * d, -sa * w, 0f, ca * w, ca * lean, hh, sa * lean, ca, 0.6f, sa, sideTile,
                       Grey(dark), Grey(Math.Min(1f, dark + 0.5f)), 0f);
            }
            for (int i = 0; i < tops; i++)
            {
                float size = r * Rr(0.95f, 1.35f), y = h * Rr(0.68f, 0.95f), a = Rr(0f, 6.283f);
                float ox = Rr(-0.25f, 0.25f) * r, oz = Rr(-0.25f, 0.25f) * r;
                float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a), tilt = Rr(-0.35f, 0.35f) * size;
                b.Card(ox - sa * size * 0.5f, y - tilt * 0.5f, oz + ca * size * 0.5f, ca * size * 0.5f, 0f, sa * size * 0.5f,
                       sa * size, tilt, -ca * size, 0f, 1f, 0f, topTile, Grey(0.62f + 0.08f * i), Grey(0.9f));
            }
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
        // Five pines, three younger ones and four birches, each turned, sized, tinted and mossed its own way where it
        // stands (TvMesh.Add, TreeLife), so no two trees look alike
        f.pineNear = new[] { Pine(near, 19f, 0.6f), Pine(near, 19f, 0.66f), Pine(near, 18f, 0.55f), Pine(near, 20f, 0.7f), Pine(near, 17f, 0.58f) };
        f.pineFar = new[] { Pine(far, 19f, 0.6f), Pine(far, 19f, 0.66f), Pine(far, 18f, 0.55f), Pine(far, 20f, 0.7f), Pine(far, 17f, 0.58f) };
        // The second pine: shorter, a deeper crown (younger)
        f.pine2Near = new[] { Pine(near, 15f, 0.45f), Pine(near, 14f, 0.5f), Pine(near, 13f, 0.4f) };
        f.pine2Far = new[] { Pine(far, 15f, 0.45f), Pine(far, 14f, 0.5f), Pine(far, 13f, 0.4f) };
        // Mostly forest-grown spruces (bare grey trunks, the crown high up), one keeps its branches low (an edge tree)
        f.spruceNear = new[] { Spruce(near, 19f, false, 0.5f), Spruce(near, 21f, false, 0.42f), Spruce(near, 17f, false, 0.12f),
                               Spruce(near, 20f, false, 0.55f), Spruce(near, 18f, false, 0.35f), Spruce(near, 22f, false, 0.6f),
                               Spruce(near, 16f, false, 0.4f) };
        f.spruceFar = Spruce(far, 19f, false, 0.45f);
        f.birchNear = new[] { Birch(near, 14f, false), Birch(near, 15f, false), Birch(near, 16f, false), Birch(near, 13f, false) };
        f.birchFar = new[] { Birch(far, 14f, false), Birch(far, 15f, false), Birch(far, 16f, false), Birch(far, 13f, false) };
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
        // Bilberry a deep, slightly yellow green (2026-11-25: the first try was too bright, like a lawn)
        // Bilberry (2026-11-25, Anders' photos): one calm bright green, a rounded clump with a top of leaves seen
        // from above, so the clumps grow together into a carpet instead of a jumble of stars
        // Single bushes now (2026-11-25), a few of each so neighbours differ
        f.berrySet = new[] { OneBush(q >= 1 ? 4 : 3, 2, 0.3f, 0.3f, TvAtlas.Bilberry, TvAtlas.BilberryTop, 0.45f),
                             OneBush(q >= 1 ? 5 : 3, 2, 0.36f, 0.34f, TvAtlas.Bilberry, TvAtlas.BilberryTop, 0.42f),
                             OneBush(q >= 1 ? 4 : 3, 1, 0.26f, 0.26f, TvAtlas.Bilberry, TvAtlas.BilberryTop, 0.5f) };
        f.berry = f.berrySet[0];
        f.berryNear = OneBush(many - 1, 3, 0.34f, 0.32f, TvAtlas.Bilberry, TvAtlas.BilberryTop, 0.42f);
        f.heathSet = new[] { OneBush(q >= 1 ? 4 : 3, 1, 0.3f, 0.3f, TvAtlas.Heather, TvAtlas.HeatherTop, 0.4f),
                             OneBush(q >= 1 ? 5 : 3, 2, 0.38f, 0.36f, TvAtlas.Heather, TvAtlas.HeatherTop, 0.38f),
                             OneBush(q >= 1 ? 4 : 3, 1, 0.24f, 0.24f, TvAtlas.Heather, TvAtlas.HeatherTop, 0.45f) };
        f.heath = f.heathSet[0];
        f.heathNear = OneBush(many - 1, 2, 0.36f, 0.34f, TvAtlas.Heather, TvAtlas.HeatherTop, 0.38f);
        // Lingonberry: low, dark and glossy, on drier ground; sedge in the marsh; flowers in meadows; juniper in
        // open pine forest and pasture (a narrow upright bush, its picture on tall cards)
        f.lingon = Star(few, 0.55f, 0.2f, 0.35f, TvAtlas.Lingonberry, Grey(0.45f), Grey(0.85f));
        f.sedge = Star(few + 1, 0.5f, 0.6f, 0.35f, TvAtlas.Sedge, Grey(0.55f), Tint(0.95f, 0.95f, 0.85f));
        f.flowers = Star(few, 0.45f, 0.4f, 0.4f, TvAtlas.Flowers, Grey(0.6f), White);
        f.juniper = JuniperShape(q);
        f.grass = Star(few + 1, 0.5f, 0.55f, 0.45f, TvAtlas.Grass, Grey(0.6f), White);
        f.dry = Star(few, 0.45f, 0.5f, 0.45f, TvAtlas.Grass, Tint(0.95f, 0.85f, 0.6f), Tint(1f, 0.95f, 0.75f));
        f.shortGrass = Star(few, 0.4f, 0.22f, 0.5f, TvAtlas.Grass, Grey(0.6f), Grey(0.9f));
        f.fern = FernShape(q >= 2 ? 7 : 5);
        // Moss cushions over old stumps and stones (spruce and moss forest)
        TvShape MoundShape(float rx, float rz, float h)
        {
            var b = new TvShapeBuilder(random);
            b.Mound(rx, rz, h, TvAtlas.Moss, Grey(0.85f), White, 0.7f);
            return b.Done();
        }
        // Reindeer lichen: low lumpy cushions, never round (2026-11-25, Anders: the flat white rings looked sick)
        TvShape LichenShape(float rx, float rz, float h)
        {
            var b = new TvShapeBuilder(random);
            b.Mound(rx, rz, h, TvAtlas.ReindeerLichen, Grey(0.62f), White, 0.45f, 1f);
            return b.Done();
        }
        f.lichens = new[] { LichenShape(0.35f, 0.22f, 0.09f), LichenShape(0.5f, 0.3f, 0.12f), LichenShape(0.25f, 0.2f, 0.08f), LichenShape(0.6f, 0.4f, 0.1f) };
        f.mounds = new[] { MoundShape(0.6f, 0.45f, 0.3f), MoundShape(0.9f, 0.6f, 0.4f), MoundShape(0.45f, 0.4f, 0.24f), MoundShape(1.2f, 0.7f, 0.36f),
                              MoundShape(2.2f, 1.6f, 0.2f), MoundShape(3f, 2f, 0.24f) };
        return f;
    }

    // ---- Dead wood: fallen trunks and stumps ----

    // A fallen trunk from a to b (radius r0 at a, r1 at b): grey weathered wood, bark left in patches, a few broken
    // branch stubs, and moss along its top
    void Log(float ax, float ay, float az, float bx, float by, float bz, float r0, float r1, int tile = TvAtlas.DeadWood)
    {
        var b = new TvShapeBuilder(random);
        const int Seg = 6;
        var pts = new float[(Seg + 1) * 3];
        var rad = new float[Seg + 1];
        var shade = new TvColor[Seg + 1];
        for (int k = 0; k <= Seg; k++)
        {
            float t = k / (float)Seg, sag = (float)Math.Sin(t * Math.PI) * 0.06f;
            pts[k * 3] = ax + (bx - ax) * t;
            pts[k * 3 + 1] = ay + (by - ay) * t - sag;
            pts[k * 3 + 2] = az + (bz - az) * t;
            rad[k] = (r0 + (r1 - r0) * t) * (1f + Rr(-0.06f, 0.06f));
            shade[k] = Grey(Rr(0.8f, 1f));
        }
        float length = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay) + (bz - az) * (bz - az));
        b.Limb(pts, rad, Q >= 2 ? 9 : 6, tile, 2f, 1.2f, shade, 0.15f);
        // Moss on the top: a thin strip of moss cards lying along it
        if (Q >= 1)
        {
            float dx = (bx - ax) / Math.Max(0.01f, length), dz = (bz - az) / Math.Max(0.01f, length);
            for (float d = 0.4f; d < length - 0.4f; d += Rr(0.7f, 1.4f))
            {
                float t = d / length, r = r0 + (r1 - r0) * t;
                float px = ax + (bx - ax) * t, py = ay + (by - ay) * t + r * 0.93f, pz = az + (bz - az) * t;
                float half = Rr(0.25f, 0.45f);
                b.Card(px - dx * half, py, pz - dz * half, -dz * r * 0.7f, 0f, dx * r * 0.7f, dx * half * 2f, 0f, dz * half * 2f, 0f, 1f, 0f,
                       TvAtlas.Moss, Grey(0.85f), Grey(0.95f), 0f, 0f, 1f);
            }
            // Broken branch stubs
            for (int k = 0; k < (int)(length / 1.5f); k++)
            {
                float t = Rr(0.15f, 0.95f), r = r0 + (r1 - r0) * t, a = Rr(0f, 6.283f);
                float px = ax + (bx - ax) * t, py = ay + (by - ay) * t, pz = az + (bz - az) * t;
                float ox = (float)Math.Cos(a) * dz, oy = (float)Math.Sin(a), oz = -(float)Math.Cos(a) * dx;
                float l = Rr(0.15f, 0.5f);
                b.Limb(new[] { px + ox * r * 0.8f, py + oy * r * 0.8f, pz + oz * r * 0.8f, px + ox * (r + l), py + oy * (r + l), pz + oz * (r + l) },
                       new[] { 0.035f * Math.Min(1f, r0 / 0.15f), 0.012f }, 4, tile, 1f, 0.5f, new[] { Grey(0.75f) });
            }
        }
        woodMesh.Add(b.Done(), 0f, 0f, 0f, 1f, 1f, 1f, 0f);
    }

    // ---- Paths: trodden earth with ragged edges, now and then a pine root across (2026-11-25, Anders: more
    // realistic paths). Lies on the ground's own path colour (Tracks), a little narrower, so the colour is its soft
    // edge. Every step along a line belongs to the piece its middle is in, so a path runs on across the pieces.
    void PathStrips()
    {
        if (input.town != null) return;
        const float Step = 0.8f;
        foreach (var line in lines)
        {
            float half;
            switch (line.kind)
            {
                case WorldLineKind.Track: half = 1.25f; break;
                case WorldLineKind.Footpath: half = 0.8f; break;
                case WorldLineKind.SmallPath: half = 0.6f; break;
                case WorldLineKind.FaintPath: half = 0.42f; break;
                default: continue;
            }
            float[] c = line.xz;
            var xs = new List<float>();
            var zs = new List<float>();
            var vs = new List<float>();
            float along = 0f;
            var b = new TvShapeBuilder(random);
            void Flush()
            {
                if (xs.Count >= 2) b.Strip(xs.ToArray(), zs.ToArray(), vs.ToArray(), half, H, TvAtlas.PathSoil, Grey(0.95f), 0.035f);
                xs.Clear(); zs.Clear(); vs.Clear();
            }
            for (int q = 0; q + 3 < c.Length; q += 2)
            {
                float ax = c[q], az = c[q + 1], bx = c[q + 2], bz = c[q + 3];
                float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                int steps = Math.Max(1, (int)Math.Ceiling(len / Step));
                for (int k = 0; k < steps; k++)
                {
                    float t0 = k / (float)steps, t1 = (k + 1) / (float)steps;
                    float x0 = ax + (bx - ax) * t0, z0 = az + (bz - az) * t0, x1 = ax + (bx - ax) * t1, z1 = az + (bz - az) * t1;
                    float mx = (x0 + x1) / 2f, mz = (z0 + z1) / 2f;
                    int kind = Base(CellAt(mx, mz));
                    bool on = Inside(mx, mz, 1f) && kind != WorldCell.Water && kind != WorldCell.Paved && kind != WorldCell.Building;
                    float v0 = along / 2.2f;
                    along += len / steps;
                    if (!on) { Flush(); continue; }
                    if (xs.Count == 0) { xs.Add(x0); zs.Add(z0); vs.Add(v0); }
                    xs.Add(x1); zs.Add(z1); vs.Add(along / 2.2f);
                    // A root across a path in the forest, every few metres (Medium and up)
                    if (Q >= 2 && line.kind != WorldLineKind.Track && (kind == WorldCell.Forest || kind == WorldCell.LightGreen))
                    {
                        if (chunk) Reseed(mx * 3f, mz * 3f, 31);
                        if (random.Value() < 0.07f) Root(mx, mz, (x1 - x0) / (len / steps), (z1 - z0) / (len / steps), half);
                    }
                }
            }
            Flush();
            TvShape shape = b.Done();
            if (shape.positions.Length > 0) pathMesh.Add(shape, 0f, 0f, 0f, 1f, 1f, 1f, 0f);
        }
    }

    // A pine root lying across a path: comes out of the ground on one side, arches a little and goes under on the other
    void Root(float x, float z, float dx, float dz, float half)
    {
        float sx = -dz, sz = dx, turn = Rr(-0.5f, 0.5f);
        float ex = sx + dx * turn, ez = sz + dz * turn, l = half + Rr(0.3f, 0.9f), r = Rr(0.025f, 0.055f);
        var pts = new float[5 * 3];
        for (int k = 0; k < 5; k++)
        {
            float t = k / 4f, off = (t * 2f - 1f) * l;
            float px = x + ex * off + Rr(-0.05f, 0.05f), pz = z + ez * off + Rr(-0.05f, 0.05f);
            float lift = (float)Math.Sin(t * Math.PI) * r * 1.2f - r * 0.6f;
            pts[k * 3] = px; pts[k * 3 + 1] = H(px, pz) + 0.035f + lift; pts[k * 3 + 2] = pz;
        }
        var b = new TvShapeBuilder(random);
        b.Limb(pts, new[] { r * 1.2f, r, r * 0.9f, r * 0.8f, r * 0.6f }, 5, TvAtlas.PineBarkHigh, 1f, 0.8f, new[] { Tint(0.55f, 0.5f, 0.46f), Tint(0.7f, 0.64f, 0.58f) });
        woodMesh.Add(b.Done(), 0f, 0f, 0f, 1f, 1f, 1f, 0f);
    }

    // Stumps of felled trees in a managed forest, now and then a dead standing pine, and dead sticks on the ground
    void Stumps()
    {
        if (input.town != null) return;
        const float Step = 7f;
        float first = -R + 3f, last = R - 3f, firstX = first, firstZ = first;
        if (chunk)
        {
            firstX = (float)Math.Ceiling((-R + ox) / Step) * Step - ox;
            firstZ = (float)Math.Ceiling((-R + oz) / Step) * Step - oz;
            last = R;
        }
        for (float gz = firstZ; gz < last; gz += Step)
            for (float gx = firstX; gx < last; gx += Step)
            {
                if (chunk) Reseed(gx + Step * 0.5f, gz + Step * 0.5f, 29);
                float x = gx + Rr(0f, Step), z = gz + Rr(0f, Step);
                int kind = Base(CellAt(x, z));
                if (kind != WorldCell.Forest && kind != WorldCell.LightGreen && kind != WorldCell.OpenTrees) continue;
                float roll = random.Value();
                if (roll > 0.24f || Blocked(x, z, 1.2f)) continue;
                float y = H(x, z);
                var b = new TvShapeBuilder(random);
                if (roll < 0.11f)
                {
                    // A stump: sawn flat, the bark on, sunk a little into the moss
                    float r = Rr(0.16f, 0.3f), h = Rr(0.2f, 0.5f);
                    b.Limb(new[] { 0f, -0.15f, 0f, 0f, h * 0.5f, 0f, 0f, h, 0f }, new[] { r * 1.35f, r * 1.05f, r }, Q >= 2 ? 9 : 6,
                           TvAtlas.PineBarkLow, Math.Max(2f, (float)Math.Round(6.2832f * r / 0.4f)), 0.8f, new[] { Grey(0.7f), Grey(0.85f), Grey(0.9f) });
                    b.Disk(0f, h, 0f, r, Q >= 2 ? 9 : 6, TvAtlas.DeadWood, Tint(1f, 0.95f, 0.85f), 0.8f);
                    woodMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, Rr(0f, 6.283f), 1f, Rr(-0.05f, 0.05f), Rr(-0.05f, 0.05f));
                    world.solids.Add(new TvSolid { x = x, z = z, r = r });
                }
                else if (roll < 0.2f)
                {
                    // A fallen branch or a thin dead trunk lying in the moss: runners step over it
                    float a = Rr(0f, 6.283f), l = Rr(1.6f, 5f), r0 = Rr(0.05f, 0.13f);
                    float bx = x + (float)Math.Cos(a) * l, bz = z + (float)Math.Sin(a) * l;
                    if (Blocked(bx, bz, 0.6f)) continue;
                    Log(x, y + r0 * 0.6f, z, bx, H(bx, bz) + r0 * 0.4f, bz, r0, r0 * 0.55f, random.Value() < 0.5f ? TvAtlas.SpruceBark : TvAtlas.DeadWood);
                }
                else if (Q >= 1)
                {
                    // A dead pine, still standing: grey bare wood, broken top, a few dead branches
                    float h = Rr(5f, 11f);
                    float[] pts = { 0f, -0.3f, 0f, Rr(-0.1f, 0.1f), h * 0.5f, Rr(-0.1f, 0.1f), Rr(-0.25f, 0.25f), h, Rr(-0.25f, 0.25f) };
                    b.Limb(pts, new[] { 0.2f, 0.13f, 0.05f }, Q >= 2 ? 8 : 5, TvAtlas.DeadWood, 2f, 1.4f, new[] { Grey(0.75f), Grey(0.9f), Grey(1f) });
                    for (int k = 0; k < 6; k++)
                    {
                        float yy = Rr(h * 0.35f, h * 0.95f), a = Rr(0f, 6.283f), l = Rr(0.4f, 1.4f) * (1.1f - yy / h);
                        float[] line = { 0f, yy, 0f, (float)Math.Cos(a) * l, yy + Rr(-0.2f, 0.3f), (float)Math.Sin(a) * l };
                        b.Limb(line, new[] { 0.04f, 0.01f }, 3, TvAtlas.DeadWood, 1f, 0.5f, new[] { Grey(0.8f) });
                    }
                    woodMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, Rr(0f, 6.283f));
                    world.solids.Add(new TvSolid { x = x, z = z, r = 0.22f });
                    world.trees++;
                }
            }
    }

    // ---- A clear-cut (hygge), 2026-11-25 after Anders' photos: the ground worked by the big machines ----
    // Stumps (some fresh and pale on top, some old and grey), logging slash lying everywhere and in low heaps, thin
    // branches, stones turned up, a tall dead snag now and then, young birches and spruces coming up, rows of
    // scarified soil (markberedning) and the wheel ruts of the forwarder. The rows and ruts follow one direction per
    // stretch of map, from the map's own position, so they run on from piece to piece.
    static bool CutKind(int kind) => kind == WorldCell.RoughOpen || kind == WorldCell.RoughOpenTrees;

    void ClearCut()
    {
        if (input.town != null) return;
        const float Step = 2.5f;
        float first = -R + 2f, last = R - 2f, firstX = first, firstZ = first;
        if (chunk)
        {
            firstX = (float)Math.Ceiling((-R + ox) / Step) * Step - ox;
            firstZ = (float)Math.Ceiling((-R + oz) / Step) * Step - oz;
            last = R;
        }
        var g = new TvShapeBuilder(random);
        float Height(float x, float z) => H(x, z);
        int found = 0;
        for (float gz = firstZ; gz < last; gz += Step)
            for (float gx = firstX; gx < last; gx += Step)
            {
                if (chunk) Reseed(gx + Step * 0.5f, gz + Step * 0.5f, 31);
                float x = gx + Rr(0f, Step), z = gz + Rr(0f, Step);
                if (!Inside(x, z, 2f)) continue;
                byte cell = CellAt(x, z);
                if (!CutKind(Base(cell)) || (cell & WorldCell.Marsh) != 0) continue;
                found++;
                if (ClearAt(x, z) < 0.8f || Blocked(x, z, 0.5f)) continue;
                float y = H(x, z), roll = random.Value();
                if (roll < 0.2f)
                {
                    // A stump: low, sawn flat; fresh ones pale yellow on top, old ones grey and cracked
                    bool fresh = random.Value() < 0.55f;
                    float r = Rr(0.13f, 0.32f), h = Rr(0.1f, 0.42f);
                    var b = new TvShapeBuilder(random);
                    int bark = random.Value() < 0.5f ? TvAtlas.PineBarkLow : TvAtlas.SpruceBark;
                    b.Limb(new[] { 0f, -0.15f, 0f, 0f, h * 0.5f, 0f, 0f, h, 0f }, new[] { r * 1.4f, r * 1.05f, r }, Q >= 2 ? 9 : 6,
                           bark, Math.Max(2f, (float)Math.Round(6.2832f * r / 0.4f)), 0.8f, new[] { Grey(0.65f), Grey(0.82f), Grey(0.9f) });
                    b.Disk(0f, h, 0f, r, Q >= 2 ? 9 : 6, TvAtlas.DeadWood, fresh ? Tint(1.25f, 1.1f, 0.8f) : Grey(0.85f), 0.8f);
                    woodMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, Rr(0f, 6.283f), 1f, Rr(-0.06f, 0.06f), Rr(-0.06f, 0.06f));
                    world.solids.Add(new TvSolid { x = x, z = z, r = r });
                }
                else if (roll < 0.55f)
                {
                    // Slash lying on the ground, a branch or two over it
                    int n = 2 + (int)(random.Value() * 3.5f);
                    for (int k = 0; k < n; k++)
                    {
                        float sx = x + Rr(-1f, 1f), sz = z + Rr(-1f, 1f), size = Rr(0.55f, 1.3f);
                        g.GroundCard(sx, sz, size, size * Rr(0.7f, 1f), Rr(0f, 6.283f), Height, TvAtlas.Slash, Grey(Rr(0.72f, 1f)), 0f, 1f, Rr(0.04f, 0.12f));
                    }
                    if (random.Value() < 0.35f)
                    {
                        float a = Rr(0f, 6.283f), l = Rr(1f, 3f), r0 = Rr(0.025f, 0.06f);
                        float bx = x + (float)Math.Cos(a) * l, bz = z + (float)Math.Sin(a) * l;
                        if (!Blocked(bx, bz, 0.4f))
                            Log(x, y + r0 + 0.05f, z, bx, H(bx, bz) + r0 * 0.5f, bz, r0, r0 * 0.5f, random.Value() < 0.5f ? TvAtlas.SpruceBark : TvAtlas.DeadWood);
                    }
                }
                else if (roll < 0.585f)
                {
                    // A low heap of slash: branches leaning together
                    var b = new TvShapeBuilder(random);
                    float hr = Rr(0.7f, 1.4f), hh = Rr(0.4f, 0.85f);
                    int n = 6 + (int)(random.Value() * 4f);
                    for (int k = 0; k < n; k++)
                    {
                        float a = k * 6.283f / n + Rr(-0.3f, 0.3f), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a), w = hr * Rr(0.6f, 0.9f);
                        b.Card(ca * hr * 0.55f, -0.05f, sa * hr * 0.55f, -sa * w * 0.5f, 0f, ca * w * 0.5f, -ca * hr * 0.6f, hh, -sa * hr * 0.6f,
                               ca, 0.7f, sa, TvAtlas.Slash, Grey(0.5f), Grey(Rr(0.8f, 1f)));
                    }
                    b.Card(-hr * 0.5f, hh * 0.8f, 0f, 0f, 0f, hr * 0.5f, hr, 0f, 0f, 0f, 1f, 0f, TvAtlas.Slash, Grey(0.9f), Grey(0.9f));
                    woodMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, Rr(0f, 6.283f));
                }
                else if (roll < 0.66f)
                {
                    // Stones the machines turned up, half in the ground
                    AddRock(x, z, Rr(0.15f, 0.6f), 0.28f, false);
                }
                else if (roll < 0.7f && Q >= 1)
                {
                    // Young trees coming up
                    bool leafy = random.Value() < 0.55f;
                    TvShape[] set = leafy ? youngLeafy : youngSpruce;
                    if (set == null) continue;
                    float sc = Rr(0.25f, 0.8f);
                    (chunk ? thicketMesh : treeMeshes[Tile(x, z)]).Add(set[(int)(random.Value() * set.Length) % set.Length], x, y - 0.05f, z,
                        sc * Rr(0.85f, 1.2f), sc, sc * Rr(0.85f, 1.2f), Rr(0f, 6.283f), Rr(0.8f, 1.1f), Rr(-0.05f, 0.05f), Rr(-0.05f, 0.05f));
                    world.trees++;
                }
                else if (roll < 0.705f)
                {
                    // A tall snag: a dead trunk left standing, its top broken off
                    var b = new TvShapeBuilder(random);
                    float h = Rr(2.2f, 6f), r = Rr(0.13f, 0.22f);
                    float[] pts = { 0f, -0.3f, 0f, Rr(-0.06f, 0.06f), h * 0.5f, Rr(-0.06f, 0.06f), Rr(-0.15f, 0.15f), h, Rr(-0.15f, 0.15f) };
                    b.Limb(pts, new[] { r, r * 0.85f, r * 0.6f }, Q >= 2 ? 8 : 5, TvAtlas.DeadWood, 2f, 1.4f, new[] { Grey(0.7f), Grey(0.85f), Grey(0.95f) });
                    woodMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, Rr(0f, 6.283f));
                    world.solids.Add(new TvSolid { x = x, z = z, r = r });
                }
            }
        if (found == 0) return;

        // Rows of scarified soil and the forwarder's wheel ruts, in global rows over the whole clear-cut
        double bx0 = Math.Floor((ox) / 300f), bz0 = Math.Floor((oz) / 300f);
        float dirA = TvRandom.Patches((float)bx0 * 0.73f + 11f, (float)bz0 * 0.71f + 5f) * 6.283f;
        float dx = (float)Math.Cos(dirA), dz = (float)Math.Sin(dirA);   // along the rows
        float nx = -dz, nz = dx;                                        // across
        float corner = R * 1.42f;
        float uC = ox * nx + oz * nz, vC = ox * dx + oz * dz;           // the piece's centre in row coordinates
        void Strip(float u, float vFrom, float vTo, float step, float chance, float halfW, float len, int salt, TvColor col, float wobble)
        {
            for (float v = (float)Math.Floor((vC + vFrom) / step) * step; v < vC + vTo; v += step)
            {
                float uu = u + wobble * (float)Math.Sin(v * 0.045f + u * 0.37f);
                float gxw = uu * nx + v * dx, gzw = uu * nz + v * dz;    // global (from the anchor)
                float lx = gxw - ox, lz = gzw - oz;
                if (!Inside(lx, lz, 1f)) continue;
                random.Reseed((int)Math.Floor(gxw * 3f), (int)Math.Floor(gzw * 3f), salt);
                if (random.Value() > chance) continue;
                byte cell = CellAt(lx, lz);
                if (!CutKind(Base(cell)) || ClearAt(lx, lz) < 0.6f || Blocked(lx, lz, 0.3f)) continue;
                float yaw = (float)Math.Atan2(dz, dx) + Rr(-0.12f, 0.12f);
                g.GroundCard(lx, lz, len * Rr(0.8f, 1.1f), halfW * Rr(0.8f, 1.2f), yaw, Height, TvAtlas.Soil, col, 0f, 1f, 0.025f);
            }
        }
        // Scarified rows every 2.4 m, broken
        for (float u = (float)Math.Floor((uC - corner) / 2.4f) * 2.4f; u < uC + corner; u += 2.4f)
            Strip(u, -corner, corner, 1.5f, 0.72f, 0.36f, 0.8f, 41, Grey(0.92f), 0.25f);
        // Wheel ruts: a pair every 24 m, darker and wetter, unbroken
        for (float u = (float)Math.Floor((uC - corner) / 24f) * 24f + 7f; u < uC + corner; u += 24f)
            for (int side = -1; side <= 1; side += 2)
                Strip(u + side * 0.95f, -corner, corner, 1.2f, 0.95f, 0.26f, 0.7f, 43 + side, Tint(0.62f, 0.58f, 0.55f), 1.4f);
        if (!g.Empty) woodMesh.Add(g.Done(), 0f, 0f, 0f, 1f, 1f, 1f, 0f);
    }

    // ---- The forest floor's litter: needles, twigs, cones and leaves lying on the ground, and dead sticks ----
    // Only in the near detail (round the TV control, or the pieces right round the runner); none on Low.
    void Litter()
    {
        if (Q == 0) return;
        const float Reach = 60f;
        float per = Q >= 3 ? 0.85f : Q == 2 ? 0.5f : 0.2f;   // cards per square metre of forest (more, 2026-11-25: everywhere)
        bool spruceWood = look.forest == TerrainLook.Spruce;
        if (spruceWood) per *= 1.6f;   // dead twigs and fallen branches lie thick under spruce (Anders' photos)
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
            if (input.town != null && TownBare(x, z)) continue;
            float tint = Rr(0.75f, 1.05f);
            TvColor c = Grey(tint);
            // Reindeer lichen in pale cushions where the ground is dry and poor (Anders' photo of a pine heath):
            // the drier patches of the undergrowth (Undergrowth) and lichen or hard ground everywhere
            float patch = TvRandom.Patches((x + ox) * 0.05f + 40f, (z + oz) * 0.05f + 13f);
            bool dry = look.ground == TerrainLook.Lichen || look.ground == TerrainLook.Hard ? patch > 0.5f : patch > 0.6f && patch < 0.72f;
            if (dry && random.Value() < 0.45f)
            {
                float s = Rr(0.6f, 1.5f);
                if (lichenShapes != null)
                    litterMesh.Add(lichenShapes[(int)(random.Value() * lichenShapes.Length) % lichenShapes.Length], x, H(x, z) - 0.02f, z,
                                   s, s * Rr(0.7f, 1.2f), s * Rr(0.7f, 1.2f), Rr(0f, 6.283f), Rr(0.75f, 1f));
                continue;
            }
            if (random.Value() < (spruceWood ? 0.38f : 0.22f))
            {
                // A dead stick: long and narrow (the stick fills the middle band of its picture)
                float len = Rr(0.35f, 1.1f);
                b.GroundCard(x, z, len * 0.5f, len * 0.16f, Rr(0f, 6.283f), Height, TvAtlas.Twig, c, 0.34f, 0.66f, 0.02f);
            }
            else
            {
                // Needles, twigs and cones (the picture's upper half) or fallen leaves (its lower half), more leaves
                // under broad-leaved trees; ragged edges, so the cards melt into each other and the ground
                bool leafy = look.forest == TerrainLook.Birch || look.forest == TerrainLook.Beech ? random.Value() < 0.7f : random.Value() < 0.12f;
                float s = Rr(0.55f, 1f);
                b.GroundCard(x, z, s, s * 0.5f, Rr(0f, 6.283f), Height, TvAtlas.Litter, c, leafy ? 0f : 0.5f, leafy ? 0.5f : 1f, 0.02f);
            }
        }
        if (b.Empty) return;
        litterMesh.Add(b.Done(), 0f, 0f, 0f, 1f, 1f, 1f, 0f);
    }
}

// The run-yourself world's trees as copies of ready-made ones (RunTrees, 2026-11-25): the shapes in a fixed order, so a
// piece can say which shape each tree is by a number
public partial class TvWorldBuilder
{
    static readonly Dictionary<TvShape, int> treeIndex = new Dictionary<TvShape, int>();

    // The full trees (near) or their far copies for a 3D quality, always in the same order: pines, young pines,
    // spruces, birches, broad-leaved. Far index i is the far copy of the full shapes FarOf(i).
    public static TvShape[] TreeShapes(int quality, bool near)
    {
        int q = Math.Max(0, Math.Min(3, quality));
        Flora f;
        lock (floraLock)
        {
            if (!floraCache.TryGetValue(q, out f))
            {
                f = BuildFlora(q);
                floraCache[q] = f;
            }
        }
        var list = new List<TvShape>();
        if (near)
        {
            list.AddRange(f.pineNear); list.AddRange(f.pine2Near); list.AddRange(f.spruceNear); list.AddRange(f.birchNear); list.AddRange(f.beechNear);
        }
        else
        {
            list.AddRange(f.pineFar); list.AddRange(f.pine2Far); list.Add(f.spruceFar); list.AddRange(f.birchFar); list.AddRange(f.beechFar);
        }
        TvShape[] shapes = list.ToArray();
        lock (treeIndex)
            for (int i = 0; i < shapes.Length; i++) treeIndex[shapes[i]] = i;
        return shapes;
    }

    // A full shape that looks like far shape i (for the pictures of far trees: they are taken of the full trees)
    public static int NearOf(int quality, int far)
    {
        int q = Math.Max(0, Math.Min(3, quality));
        Flora f;
        lock (floraLock) floraCache.TryGetValue(q, out f);
        if (f == null) return 0;
        int pines = f.pineFar.Length, pines2 = f.pine2Far.Length, birches = f.birchFar.Length;
        int nPines = f.pineNear.Length, nPines2 = f.pine2Near.Length, nSpruces = f.spruceNear.Length, nBirches = f.birchNear.Length;
        if (far < pines) return Math.Min(far, nPines - 1);
        far -= pines;
        if (far < pines2) return nPines + Math.Min(far, nPines2 - 1);
        far -= pines2;
        if (far < 1) return nPines + nPines2;
        far -= 1;
        if (far < birches) return nPines + nPines2 + nSpruces + Math.Min(far, nBirches - 1);
        far -= birches;
        return nPines + nPines2 + nSpruces + nBirches + far;
    }

    float TreeIndex(TvShape shape, bool near)
    {
        if (shape == null) return -1f;
        int i;
        lock (treeIndex)
        {
            if (treeIndex.TryGetValue(shape, out i)) return i;
        }
        TreeShapes(input.quality, near);
        lock (treeIndex) return treeIndex.TryGetValue(shape, out i) ? i : -1f;
    }
}
