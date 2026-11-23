using System;

// The town on a sprint map in 3D (2026-11-20, Anders: houses of different kinds, a church and a town hall, asphalt,
// street lamps, litter bins, park benches, fine fences, and people out in the town who watch the race). Low-poly
// shapes with their colours in the corners, like the rest of the 3D world, made once and copied many times.
// Measures in metres; y up; a building is built round its middle on the ground, its length along x.
public static partial class TvCityShapes
{
    // ---- Colours ----

    static readonly TvColor Iron = TvColor.Hex("#262b28"), LampGreen = TvColor.Hex("#2f4038"), Glow = TvColor.Hex("#fff0b8"),
        BinGreen = TvColor.Hex("#2e6a3c"), Steel = TvColor.Hex("#8b9092"), Wood = TvColor.Hex("#8a5a34"), WoodDark = TvColor.Hex("#6c4426"),
        Glass = TvColor.Hex("#2b3640"), GlassShop = TvColor.Hex("#34434c"), White = TvColor.Hex("#f1eee6"), Plinth = TvColor.Hex("#6e6b66"),
        RoofDark = TvColor.Hex("#43484b"), Copper = TvColor.Hex("#5c9783"), Tile = TvColor.Hex("#8e3f2e"), Gold = TvColor.Hex("#d4b04c"),
        Skin = TvColor.Hex("#e2b38e"), SkinDark = TvColor.Hex("#9a6a4a"), Shoe = TvColor.Hex("#2a2a2c");

    // A street of old Swedish town houses: ochre, terracotta, pink, sage, light blue, pale yellow... no two the same
    // next to each other (2026-11-20, Anders: the first town was too much one colour)
    public static readonly TvColor[] Facades =
    {
        TvColor.Hex("#e0ad48"), TvColor.Hex("#c8714c"), TvColor.Hex("#ecdfc0"), TvColor.Hex("#97b98f"),
        TvColor.Hex("#e39c98"), TvColor.Hex("#9fbfd8"), TvColor.Hex("#f1df98"), TvColor.Hex("#b4523e"),
        TvColor.Hex("#7fa9a2"), TvColor.Hex("#d9895a"), TvColor.Hex("#c7b5d9"), TvColor.Hex("#f2ece0")
    };
    static readonly TvColor[] Trims = { White, White, TvColor.Hex("#f3e4c0"), TvColor.Hex("#2f4a3a"), White, TvColor.Hex("#6e2b25"), TvColor.Hex("#2c3e56") };
    static readonly TvColor[] Doors =
        { TvColor.Hex("#2f6b4c"), TvColor.Hex("#9a2f2a"), TvColor.Hex("#2c4a7b"), TvColor.Hex("#5b3a26"), TvColor.Hex("#d6a42c"), TvColor.Hex("#3b3f45") };
    static readonly TvColor[] Roofs =
        { TvColor.Hex("#43484b"), TvColor.Hex("#9a442f"), TvColor.Hex("#5f9a86"), TvColor.Hex("#6b2e24"), TvColor.Hex("#2f3336"), TvColor.Hex("#b0583a") };
    static readonly TvColor[] Flowers =
        { TvColor.Hex("#e23d5a"), TvColor.Hex("#f2c230"), TvColor.Hex("#e86a2a"), TvColor.Hex("#f4f0f2"), TvColor.Hex("#c04fb0"), TvColor.Hex("#ff8fb0") };
    static readonly TvColor Leaf = TvColor.Hex("#3f7a3a"), Pipe = TvColor.Hex("#6d7275"), Rubber = TvColor.Hex("#1d1e20");
    static readonly TvColor[] Awnings = { TvColor.Hex("#2f5d46"), TvColor.Hex("#8f2f2a"), TvColor.Hex("#2c4a6b"), TvColor.Hex("#b7782a") };
    static readonly TvColor[] Clothes =
    {
        TvColor.Hex("#c0392b"), TvColor.Hex("#2c5aa0"), TvColor.Hex("#3d8b5a"), TvColor.Hex("#e6b333"), TvColor.Hex("#7a4b8c"),
        TvColor.Hex("#e07b39"), TvColor.Hex("#3a3f45"), TvColor.Hex("#d9d4c7"), TvColor.Hex("#5ea3c9"), TvColor.Hex("#b83b6e")
    };
    static readonly TvColor[] Trousers = { TvColor.Hex("#2b3446"), TvColor.Hex("#3b3b3d"), TvColor.Hex("#5a4a3a"), TvColor.Hex("#38506e") };
    static readonly TvColor[] Hair = { TvColor.Hex("#3a2a1e"), TvColor.Hex("#b08a4a"), TvColor.Hex("#6b4a2e"), TvColor.Hex("#d8c08a"), TvColor.Hex("#9a9a96"), TvColor.Hex("#1e1a18") };

    // ---- Helpers ----

    // A box along the line from a to b, w wide and h high (square to the line; h is measured upwards)
    public static void Beam(TvShapeBuilder b, float ax, float ay, float az, float bx, float by, float bz, float w, float h, TvColor color)
    {
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-5f) return;
        dx /= len; dy /= len; dz /= len;
        // Across: level and square to the line (or along x for a line straight up)
        float sx = dz, sz = -dx, sl = (float)Math.Sqrt(sx * sx + sz * sz);
        if (sl < 1e-4f) { sx = 1f; sz = 0f; sl = 1f; }
        sx /= sl; sz /= sl;
        // Up: square to both
        float ux = dy * sz - dz * 0f, uy = dz * sx - dx * sz, uz = dx * 0f - dy * sx;
        if (uy < 0f) { ux = -ux; uy = -uy; uz = -uz; }
        float hw = w * 0.5f, hh = h * 0.5f;
        var c = new float[24];
        for (int i = 0; i < 8; i++)
        {
            float s = (i & 1) == 0 ? -hw : hw, u = (i & 2) == 0 ? -hh : hh;
            bool end = (i & 4) != 0;
            c[i * 3] = (end ? bx : ax) + sx * s + ux * u;
            c[i * 3 + 1] = (end ? by : ay) + uy * u;
            c[i * 3 + 2] = (end ? bz : az) + sz * s + uz * u;
        }
        float mx = (ax + bx) / 2f, my = (ay + by) / 2f, mz = (az + bz) / 2f;
        void Q(int p, int q, int r, int s)
        {
            b.Triangle(c[p * 3], c[p * 3 + 1], c[p * 3 + 2], c[q * 3], c[q * 3 + 1], c[q * 3 + 2], c[r * 3], c[r * 3 + 1], c[r * 3 + 2], mx, my, mz, color);
            b.Triangle(c[p * 3], c[p * 3 + 1], c[p * 3 + 2], c[r * 3], c[r * 3 + 1], c[r * 3 + 2], c[s * 3], c[s * 3 + 1], c[s * 3 + 2], mx, my, mz, color);
        }
        Q(0, 1, 3, 2); Q(4, 5, 7, 6); Q(0, 1, 5, 4); Q(2, 3, 7, 6); Q(0, 2, 6, 4); Q(1, 3, 7, 5);
    }

    static void Quad(TvShapeBuilder b, float[] q, float cx, float cy, float cz, TvColor color)
    {
        b.Triangle(q[0], q[1], q[2], q[3], q[4], q[5], q[6], q[7], q[8], cx, cy, cz, color);
        b.Triangle(q[0], q[1], q[2], q[6], q[7], q[8], q[9], q[10], q[11], cx, cy, cz, color);
    }

    // A four-sided roof from the eaves (a rectangle hl x hw at height y) to a ridge along x (or a point when square)
    static void HipRoof(TvShapeBuilder b, float hl, float hw, float y, float rise, float over, TvColor color)
    {
        float l = hl + over, w = hw + over, ridge = Math.Max(0f, l - w), top = y + rise;
        float cy = y + rise * 0.3f;
        b.Triangle(-l, y, -w, l, y, -w, ridge, top, 0f, 0f, cy, 0f, color);
        b.Triangle(-l, y, -w, ridge, top, 0f, -ridge, top, 0f, 0f, cy, 0f, color);
        b.Triangle(-l, y, w, l, y, w, ridge, top, 0f, 0f, cy, 0f, color);
        b.Triangle(-l, y, w, ridge, top, 0f, -ridge, top, 0f, 0f, cy, 0f, color);
        b.Triangle(l, y, -w, l, y, w, ridge, top, 0f, 0f, cy, 0f, color);
        b.Triangle(-l, y, -w, -l, y, w, -ridge, top, 0f, 0f, cy, 0f, color);
        // The underside, so it is not see-through from below
        b.Triangle(-l, y, -w, l, y, -w, l, y, w, 0f, y + 1f, 0f, color.Shade(0.6f));
        b.Triangle(-l, y, -w, l, y, w, -l, y, w, 0f, y + 1f, 0f, color.Shade(0.6f));
    }

    // A pitched roof along x with gables at the ends (walls up to the ridge in the wall colour)
    static void GableRoof(TvShapeBuilder b, float hl, float hw, float eaves, float rise, float over, TvColor roof, TvColor wall)
    {
        float ridge = eaves + rise, slope = rise / Math.Max(0.01f, hw);
        foreach (float end in new[] { -hl, hl })
            b.Triangle(end, eaves, -hw, end, eaves, hw, end, ridge, 0f, 0f, eaves, 0f, wall);
        foreach (float side in new[] { -1f, 1f })
        {
            float ze = side * (hw + over), ye = eaves - over * slope;
            const float T = 0.18f;
            float[] top = { -hl - over, ye + T, ze, hl + over, ye + T, ze, hl + over, ridge + T, 0f, -hl - over, ridge + T, 0f };
            Quad(b, top, 0f, eaves, 0f, roof);
            float[] under = { -hl - over, ye, ze, hl + over, ye, ze, hl + over, ridge, 0f, -hl - over, ridge, 0f };
            Quad(b, under, 0f, ridge + 5f, 0f, roof.Shade(0.55f));
            float[] edge = { -hl - over, ye, ze, hl + over, ye, ze, hl + over, ye + T, ze, -hl - over, ye + T, ze };
            Quad(b, edge, 0f, ye, 0f, roof.Shade(0.8f));
        }
    }

    // Windows in a row on one face of a box (face: 0 = -z, 1 = +z, 2 = -x, 3 = +x), with white frames
    // flowers: that share of the windows get a flower box under them (needs random)
    static void WindowRow(TvShapeBuilder b, int face, float hl, float hw, float y, float h, float w, float spacing, TvColor frame, TvColor glass,
                          bool bars = true, int skip = -1, TvRandom random = null, float flowers = 0f, TvColor? shutters = null)
    {
        float along = face < 2 ? hl : hw, depth = face < 2 ? hw : hl;
        int count = Math.Max(1, (int)((2f * along - 0.8f) / spacing));
        float sign = face == 0 || face == 2 ? -1f : 1f;
        for (int i = 0; i < count; i++)
        {
            if (i == skip) continue;
            float u = -along + 2f * along * (i + 0.5f) / count;
            void Box(float u0, float y0, float d0, float u1, float y1, float d1, TvColor c)
            {
                if (face < 2) b.Box(u0, y0, sign * d0, u1, y1, sign * d1, c);
                else b.Box(sign * d0, y0, u0, sign * d1, y1, u1, c);
            }
            Box(u - w / 2f - 0.09f, y - 0.09f, depth - 0.02f, u + w / 2f + 0.09f, y + h + 0.09f, depth + 0.04f, frame);
            Box(u - w / 2f, y, depth - 0.02f, u + w / 2f, y + h, depth + 0.06f, glass);
            if (bars)
            {
                Box(u - 0.035f, y, depth - 0.02f, u + 0.035f, y + h, depth + 0.07f, frame);
                Box(u - w / 2f, y + h * 0.62f - 0.03f, depth - 0.02f, u + w / 2f, y + h * 0.62f + 0.03f, depth + 0.07f, frame);
            }
            // A sill below
            Box(u - w / 2f - 0.14f, y - 0.16f, depth - 0.02f, u + w / 2f + 0.14f, y - 0.09f, depth + 0.12f, frame);
            // A light in some windows: warm glass behind a curtain
            if (random != null && random.Value() < 0.18f)
                Box(u - w / 2f + 0.04f, y + h * 0.66f, depth - 0.01f, u + w / 2f - 0.04f, y + h - 0.04f, depth + 0.065f, TvColor.Hex("#d9c9a0"));
            if (shutters.HasValue)
                foreach (float sd in new[] { -1f, 1f })
                    Box(u + sd * (w / 2f + 0.12f) - 0.21f, y - 0.05f, depth - 0.02f, u + sd * (w / 2f + 0.12f) + 0.21f, y + h + 0.05f, depth + 0.08f, shutters.Value);
            if (random != null && random.Value() < flowers)
            {
                // A flower box: a green trough with a row of blooms
                Box(u - w / 2f - 0.05f, y - 0.42f, depth, u + w / 2f + 0.05f, y - 0.16f, depth + 0.3f, TvColor.Hex("#3d5a3a"));
                TvColor bloom = Flowers[(int)(random.Value() * Flowers.Length) % Flowers.Length];
                int n = Math.Max(3, (int)(w / 0.22f));
                for (int k = 0; k < n; k++)
                {
                    float fu = u - w / 2f + w * (k + 0.5f) / n;
                    float fy = y - 0.14f + 0.06f * (k % 2);
                    if (face < 2) b.Blob(0.12f, 1f, 0.8f, 1f, fu, fy, sign * (depth + 0.17f), 0.3f, 0, true, k % 3 == 1 ? Leaf : bloom);
                    else b.Blob(0.12f, 1f, 0.8f, 1f, sign * (depth + 0.17f), fy, fu, 0.3f, 0, true, k % 3 == 1 ? Leaf : bloom);
                }
            }
        }
    }

    // ---- Street things ----

    // An old-style street lamp: a dark green pole with a fluted foot, an arm and a lantern
    public static TvShape StreetLamp(TvRandom random, int flowers = -1)
    {
        var b = new TvShapeBuilder(random);
        if (flowers >= 0)
        {
            // A hanging basket of flowers on the pole (summer in the town centre)
            b.Tube(0.28f, 0.2f, 0.25f, 2.75f, 8, TvColor.Hex("#4a3a2a"));
            for (int i = 0; i < 9; i++)
            {
                float a = i * 0.7f;
                b.Blob(0.14f, 1f, 0.9f, 1f, (float)Math.Cos(a) * 0.27f, 2.95f - (i % 3) * 0.12f, (float)Math.Sin(a) * 0.27f, 0.3f, 0, true,
                       i % 3 == 2 ? Leaf : Flowers[(flowers + i / 3) % Flowers.Length]);
            }
        }
        b.Tube(0.15f, 0.12f, 0.55f, 0f, 8, Iron);
        b.Tube(0.075f, 0.055f, 4.1f, 0.55f, 8, LampGreen);
        b.Tube(0.09f, 0.09f, 0.08f, 2.2f, 8, Iron);
        Beam(b, 0f, 4.45f, 0f, 0.85f, 4.6f, 0f, 0.06f, 0.06f, LampGreen);
        Beam(b, 0f, 4.05f, 0f, 0.5f, 4.55f, 0f, 0.035f, 0.035f, LampGreen);   // the curl under the arm
        b.Tube(0.03f, 0.03f, 0.12f, 4.48f, 6, Iron, null, 0f, 1f, 0.85f, 0f);
        b.Tube(0.13f, 0.2f, 0.36f, 4.06f, 6, Glow, null, 0f, 1f, 0.85f, 0f);
        b.Cone(0.27f, 0.22f, 4.42f, 6, Iron, 0.85f, 0f);
        b.Tube(0.06f, 0.06f, 0.05f, 4.0f, 6, Iron, null, 0f, 1f, 0.85f, 0f);
        return b.Done();
    }

    // A round green litter bin on a post
    public static TvShape Bin(TvRandom random)
    {
        var b = new TvShapeBuilder(random);
        b.Tube(0.04f, 0.04f, 0.95f, 0f, 6, Steel);
        b.Tube(0.21f, 0.23f, 0.52f, 0.35f, 10, BinGreen, null, 0f, 1f, 0f, 0.27f);
        b.Tube(0.245f, 0.245f, 0.05f, 0.86f, 10, BinGreen.Shade(0.7f), null, 0f, 1f, 0f, 0.27f);
        b.Tube(0.12f, 0.0f, 0.05f, 0.88f, 8, Iron, null, 0f, 1f, 0f, 0.27f);
        return b.Done();
    }

    // A park bench: wooden slats on black iron sides, 1.8 m long; you sit facing -z
    public static TvShape Bench(TvRandom random)
    {
        var b = new TvShapeBuilder(random);
        foreach (float x in new[] { -0.75f, 0.75f })
        {
            Beam(b, x, 0f, -0.22f, x, 0.44f, -0.2f, 0.06f, 0.05f, Iron);       // front leg
            Beam(b, x, 0f, 0.2f, x, 0.9f, 0.32f, 0.06f, 0.05f, Iron);          // back leg and back
            Beam(b, x, 0.43f, -0.26f, x, 0.43f, 0.24f, 0.06f, 0.05f, Iron);    // under the seat
            Beam(b, x, 0.62f, -0.24f, x, 0.62f, 0.05f, 0.05f, 0.04f, Iron);    // arm rest
            Beam(b, x, 0.44f, -0.22f, x, 0.62f, -0.22f, 0.05f, 0.04f, Iron);
        }
        for (int i = 0; i < 4; i++)
        {
            float z = -0.22f + i * 0.13f;
            b.Box(-0.92f, 0.45f, z, 0.92f, 0.49f, z + 0.09f, i % 2 == 0 ? Wood : WoodDark);
        }
        for (int i = 0; i < 3; i++)
        {
            float y = 0.58f + i * 0.12f, z = 0.235f + (y - 0.45f) * 0.17f;
            b.Box(-0.92f, y, z, 0.92f, y + 0.09f, z + 0.035f, i % 2 == 0 ? Wood : WoodDark);
        }
        return b.Done();
    }

    // A short bollard
    public static TvShape Bollard(TvRandom random)
    {
        var b = new TvShapeBuilder(random);
        b.Tube(0.09f, 0.08f, 0.8f, 0f, 8, Iron);
        b.Tube(0.08f, 0.0f, 0.06f, 0.8f, 8, Iron);
        b.Tube(0.085f, 0.085f, 0.04f, 0.62f, 8, Gold);
        return b.Done();
    }

    // A big round flower pot with flowers
    public static TvShape Planter(TvRandom random)
    {
        var b = new TvShapeBuilder(random);
        b.Tube(0.45f, 0.55f, 0.55f, 0f, 10, TvColor.Hex("#9c968c"));
        b.Tube(0.5f, 0.5f, 0.02f, 0.53f, 10, TvColor.Hex("#4a3a2a"));
        b.Blob(0.42f, 1f, 0.55f, 1f, 0f, 0.7f, 0f, 0.3f, 1, false, TvColor.Hex("#3f7a3a"));
        TvColor[] flowers = { TvColor.Hex("#e23d5a"), TvColor.Hex("#f2c230"), TvColor.Hex("#e86a2a"), TvColor.Hex("#f4f0f2") };
        for (int i = 0; i < 9; i++)
        {
            float a = i * 2.4f, r = 0.12f + 0.25f * ((i * 37) % 10) / 10f;
            b.Blob(0.07f, 1f, 0.8f, 1f, (float)Math.Cos(a) * r, 0.86f + 0.04f * (i % 3), (float)Math.Sin(a) * r, 0.2f, 0, true, flowers[i % flowers.Length]);
        }
        return b.Done();
    }

    // A black iron fence, 2 m: posts with knobs, two rails, spear-topped bars (along x, round the middle)
    public static TvShape IronFence(TvRandom random)
    {
        var b = new TvShapeBuilder(random);
        b.Box(-1f, 0f, -0.05f, -0.92f, 1.25f, 0.05f, Iron);
        b.Blob(0.07f, 1f, 1f, 1f, -0.96f, 1.3f, 0f, 0f, 0, true, Iron);
        b.Box(-1f, 0.12f, -0.02f, 1f, 0.17f, 0.02f, Iron);
        b.Box(-1f, 1.0f, -0.02f, 1f, 1.05f, 0.02f, Iron);
        for (int i = 0; i < 14; i++)
        {
            float x = -0.86f + i * 0.13f;
            b.Box(x - 0.012f, 0.05f, -0.012f, x + 0.012f, 1.12f, 0.012f, Iron);
            b.Cone(0.03f, 0.09f, 1.12f, 4, Iron, x, 0f);
        }
        return b.Done();
    }

    // A white wooden picket fence, 2 m, for gardens
    public static TvShape PicketFence(TvRandom random)
    {
        var b = new TvShapeBuilder(random);
        b.Box(-1f, 0f, -0.045f, -0.91f, 0.95f, 0.045f, White);
        b.Box(-1f, 0.22f, 0.02f, 1f, 0.3f, 0.05f, White);
        b.Box(-1f, 0.65f, 0.02f, 1f, 0.73f, 0.05f, White);
        for (int i = 0; i < 11; i++)
        {
            float x = -0.84f + i * 0.17f;
            b.Box(x - 0.04f, 0.05f, 0.05f, x + 0.04f, 0.8f, 0.07f, White);
            b.Triangle(x - 0.04f, 0.8f, 0.07f, x + 0.04f, 0.8f, 0.07f, x, 0.88f, 0.07f, x, 0.8f, 0f, White);
        }
        return b.Done();
    }

    // A Swedish flag hanging out from a pole top at (x, y, z), w long, turned by yaw (blue with a yellow cross)
    static void SwedishFlag(TvShapeBuilder b, float x, float y, float z, float w, float yaw)
    {
        float h = w * 0.625f, c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
        TvColor blue = TvColor.Hex("#1f5ea8"), yellow = TvColor.Hex("#f2c230");
        void Part(float u0, float v0, float u1, float v1, TvColor col, float off)
        {
            // In the flag's plane (u along the flag, v down from the top), a hair in front of or behind the blue
            float ax = x + c * u0, az = z + s * u0, bx = x + c * u1, bz = z + s * u1;
            b.Box(Math.Min(ax, bx) - Math.Abs(s) * off, y - v1, Math.Min(az, bz) - Math.Abs(c) * off,
                  Math.Max(ax, bx) + Math.Abs(s) * off, y - v0, Math.Max(az, bz) + Math.Abs(c) * off, col);
        }
        Part(0f, 0f, w, h, blue, 0.01f);
        Part(w * 0.3125f, 0f, w * 0.4375f, h, yellow, 0.025f);
        Part(0f, h * 0.4f, w, h * 0.6f, yellow, 0.025f);
    }

    // A white flag pole with a Swedish flag, 8 m
    public static TvShape FlagPole(TvRandom random)
    {
        var b = new TvShapeBuilder(random);
        b.Box(-0.3f, 0f, -0.3f, 0.3f, 0.15f, 0.3f, TvColor.Hex("#9c968c"));
        b.Tube(0.07f, 0.045f, 8f, 0.15f, 8, White);
        b.Blob(0.08f, 1f, 1f, 1f, 0f, 8.2f, 0f, 0f, 0, false, Gold);
        SwedishFlag(b, 0.06f, 8f, 0f, 2.4f, 0f);
        return b.Done();
    }

    // A street tree: a straight trunk in an iron grate and a round crown of a few lumps, look picks size and green
    public static TvShape StreetTree(TvRandom random, int look)
    {
        var b = new TvShapeBuilder(random);
        TvColor[] greens = { TvColor.Hex("#4f8a3c"), TvColor.Hex("#5f9a44"), TvColor.Hex("#3f7a3a"), TvColor.Hex("#7aa84a") };
        TvColor leaf = greens[look % greens.Length];
        float hgt = 3.2f + (look % 3) * 0.6f, r = 1.9f + (look % 2) * 0.5f;
        b.Box(-0.7f, 0f, -0.7f, 0.7f, 0.03f, 0.7f, Iron);
        b.Tube(0.16f, 0.11f, hgt, 0f, 7, TvColor.Hex("#5a4636"));
        b.Blob(r, 1f, 0.85f, 1f, 0f, hgt + r * 0.6f, 0f, 0.25f, 1, true, leaf.Shade(0.8f), leaf.Lerp(TvColor.Hex("#c8d870"), 0.25f), hgt, hgt + r * 1.5f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * 1.57f + look;
            b.Blob(r * 0.6f, 1f, 0.8f, 1f, (float)Math.Cos(a) * r * 0.6f, hgt + r * 0.5f + (i % 2) * 0.4f, (float)Math.Sin(a) * r * 0.6f, 0.3f, 0, true, leaf);
        }
        return b.Done();
    }

    // A parked car along x (front to +x), in one of many colours: body, cabin with windows, wheels, lights
    public static TvShape Car(TvRandom random, int look)
    {
        var b = new TvShapeBuilder(random);
        TvColor[] paints = { TvColor.Hex("#b8282a"), TvColor.Hex("#2a5aa8"), TvColor.Hex("#e8e6e0"), TvColor.Hex("#2b2d30"), TvColor.Hex("#9aa3a8"),
                             TvColor.Hex("#2f6b4c"), TvColor.Hex("#e0a020"), TvColor.Hex("#6a2a5a"), TvColor.Hex("#8fc0d8") };
        TvColor paint = paints[look % paints.Length];
        bool van = look % 7 == 5;
        float l = van ? 2.4f : 2.15f, w = 0.88f;
        b.Box(-l, 0.3f, -w, l, 0.95f, w, paint);
        if (van) b.Box(-l + 0.1f, 0.95f, -w + 0.05f, l - 0.9f, 1.9f, w - 0.05f, paint);
        else
        {
            // The cabin: sloped glass front and back
            float c0 = -1.2f, c1 = 0.9f, top = 1.45f;
            b.Box(c0 + 0.3f, 0.95f, -w + 0.08f, c1 - 0.35f, top, w - 0.08f, paint);
            b.Box(c0 + 0.35f, 1.0f, -w + 0.06f, c1 - 0.4f, top - 0.08f, w - 0.06f, Glass);
            b.Triangle(c1 - 0.35f, top, -w + 0.08f, c1 - 0.35f, 0.95f, -w + 0.08f, c1 + 0.25f, 0.95f, -w + 0.08f, 0f, 1f, 0f, Glass);
            b.Triangle(c1 - 0.35f, top, w - 0.08f, c1 - 0.35f, 0.95f, w - 0.08f, c1 + 0.25f, 0.95f, w - 0.08f, 0f, 1f, 0f, Glass);
            float[] ws = { c1 - 0.35f, top, -w + 0.08f, c1 - 0.35f, top, w - 0.08f, c1 + 0.25f, 0.95f, w - 0.08f, c1 + 0.25f, 0.95f, -w + 0.08f };
            Quad(b, ws, 0f, 0.6f, 0f, Glass);
            float[] rs = { c0 + 0.3f, top, -w + 0.08f, c0 + 0.3f, top, w - 0.08f, c0 - 0.2f, 0.95f, w - 0.08f, c0 - 0.2f, 0.95f, -w + 0.08f };
            Quad(b, rs, 0f, 0.6f, 0f, Glass);
            b.Triangle(c0 + 0.3f, top, -w + 0.08f, c0 + 0.3f, 0.95f, -w + 0.08f, c0 - 0.2f, 0.95f, -w + 0.08f, 0f, 1f, 0f, Glass);
            b.Triangle(c0 + 0.3f, top, w - 0.08f, c0 + 0.3f, 0.95f, w - 0.08f, c0 - 0.2f, 0.95f, w - 0.08f, 0f, 1f, 0f, Glass);
        }
        if (van) b.Box(l - 0.9f, 1.0f, -w + 0.04f, l - 0.85f, 1.75f, w - 0.04f, Glass);
        foreach (float x in new[] { -l + 0.65f, l - 0.7f })
            foreach (float z in new[] { -w + 0.05f, w - 0.05f })
            {
                b.Box(x - 0.33f, 0f, z - 0.12f, x + 0.33f, 0.62f, z + 0.12f, Rubber);
                b.Box(x - 0.18f, 0.16f, z - 0.14f, x + 0.18f, 0.46f, z + 0.14f, Steel);
            }
        foreach (float z in new[] { -w + 0.25f, w - 0.25f })
        {
            b.Box(l - 0.02f, 0.68f, z - 0.15f, l + 0.03f, 0.82f, z + 0.15f, TvColor.Hex("#f4f0d8"));
            b.Box(-l - 0.03f, 0.68f, z - 0.15f, -l + 0.02f, 0.82f, z + 0.15f, TvColor.Hex("#c0201c"));
        }
        b.Box(l - 0.05f, 0.3f, -w - 0.02f, l + 0.06f, 0.45f, w + 0.02f, Iron);
        b.Box(-l - 0.06f, 0.3f, -w - 0.02f, -l + 0.05f, 0.45f, w + 0.02f, Iron);
        return b.Done();
    }

    // A bicycle standing along x, in a bright colour
    public static TvShape Bicycle(TvRandom random, int look)
    {
        var b = new TvShapeBuilder(random);
        TvColor frame = Clothes[(look * 3 + 1) % Clothes.Length];
        foreach (float x in new[] { -0.55f, 0.55f })
            for (int i = 0; i < 10; i++)
            {
                float a0 = i * 0.6283f, a1 = (i + 1) * 0.6283f;
                Beam(b, x + (float)Math.Cos(a0) * 0.33f, 0.34f + (float)Math.Sin(a0) * 0.33f, 0f, x + (float)Math.Cos(a1) * 0.33f, 0.34f + (float)Math.Sin(a1) * 0.33f, 0f, 0.04f, 0.04f, Rubber);
            }
        Beam(b, -0.55f, 0.34f, 0f, -0.05f, 0.34f, 0f, 0.035f, 0.035f, frame);
        Beam(b, -0.05f, 0.34f, 0f, -0.2f, 0.85f, 0f, 0.04f, 0.04f, frame);
        Beam(b, -0.55f, 0.34f, 0f, -0.2f, 0.8f, 0f, 0.035f, 0.035f, frame);
        Beam(b, -0.2f, 0.8f, 0f, 0.42f, 0.78f, 0f, 0.04f, 0.04f, frame);
        Beam(b, -0.05f, 0.34f, 0f, 0.42f, 0.78f, 0f, 0.04f, 0.04f, frame);
        Beam(b, 0.55f, 0.34f, 0f, 0.45f, 0.98f, 0f, 0.035f, 0.035f, frame);
        Beam(b, 0.45f, 0.98f, -0.25f, 0.45f, 0.98f, 0.25f, 0.035f, 0.035f, Iron);
        b.Box(-0.33f, 0.86f, -0.07f, -0.08f, 0.92f, 0.07f, Iron);
        if (look % 2 == 0) b.Box(0.45f, 0.7f, -0.15f, 0.8f, 0.92f, 0.15f, TvColor.Hex("#a07a4a"));   // a basket
        return b.Done();
    }

    // A café table with two chairs and a big parasol in a bright colour
    public static TvShape CafeSet(TvRandom random, int look)
    {
        var b = new TvShapeBuilder(random);
        TvColor cloth = Awnings[look % Awnings.Length].Lerp(White, look % 2 == 0 ? 0f : 0.35f);
        b.Tube(0.04f, 0.04f, 0.72f, 0f, 6, Iron);
        b.Tube(0.35f, 0.35f, 0.04f, 0.72f, 10, White);
        b.Tube(0.03f, 0.03f, 2.2f, 0.76f, 6, Steel);
        // The parasol: eight panels, white and coloured in turn
        for (int i = 0; i < 8; i++)
        {
            float a0 = i * 0.7854f, a1 = (i + 1) * 0.7854f;
            b.Triangle((float)Math.Cos(a0) * 1.4f, 2.45f, (float)Math.Sin(a0) * 1.4f, (float)Math.Cos(a1) * 1.4f, 2.45f, (float)Math.Sin(a1) * 1.4f,
                       0f, 2.95f, 0f, 0f, 2.0f, 0f, i % 2 == 0 ? cloth : White);
            b.Triangle((float)Math.Cos(a0) * 1.4f, 2.45f, (float)Math.Sin(a0) * 1.4f, (float)Math.Cos(a1) * 1.4f, 2.45f, (float)Math.Sin(a1) * 1.4f,
                       0f, 2.95f, 0f, 0f, 4.0f, 0f, cloth.Shade(0.6f));
        }
        // Two chairs, facing the table
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = sx * 0.62f;
            b.Box(x - 0.2f, 0.44f, -0.2f, x + 0.2f, 0.48f, 0.2f, Wood);
            foreach (float cx in new[] { -0.17f, 0.17f })
                foreach (float cz in new[] { -0.17f, 0.17f })
                    b.Box(x + cx - 0.02f, 0f, cz - 0.02f, x + cx + 0.02f, 0.45f, cz + 0.02f, Iron);
            b.Box(x + sx * 0.18f - 0.02f, 0.48f, -0.2f, x + sx * 0.2f + 0.02f, 0.9f, 0.2f, Wood);
        }
        // A cup and a little vase
        b.Tube(0.04f, 0.045f, 0.08f, 0.76f, 6, White, null, 0f, 1f, 0.12f, 0.05f);
        b.Tube(0.03f, 0.03f, 0.12f, 0.76f, 6, Glass, null, 0f, 1f, -0.1f, -0.08f);
        b.Blob(0.05f, 1f, 1f, 1f, -0.1f, 0.92f, -0.08f, 0f, 0, true, Flowers[look % Flowers.Length]);
        return b.Done();
    }

    // ---- Buildings ----

    public enum Kind { TownHouse, Apartments, Villa, Church, TownHall, Hall, School }

    // A building of the given kind, len x wid, built round its middle; floors for the town houses and blocks
    public static TvShape Building(TvRandom random, Kind kind, float len, float wid, int floors, int look, float plinth = 0.4f)
    {
        switch (kind)
        {
            case Kind.Church: return Church(random, len, wid, plinth);
            case Kind.TownHall: return TownHall(random, len, wid, plinth);
            case Kind.Apartments: return Apartments(random, len, wid, floors, look, plinth);
            case Kind.Villa: return Villa(random, len, wid, look, plinth);
            case Kind.Hall: return Hall(random, len, wid, look, plinth);
            case Kind.School: return Apartments(random, len, wid, Math.Max(2, Math.Min(floors, 3)), look, plinth);
            default: return TownHouse(random, len, wid, floors, look, plinth);
        }
    }

    // A town house of the old town centre: plastered in its own colour, corner stones or pilasters, shops with
    // awnings and painted signs on the street floor, a coloured front door, windows with frames, flower boxes and
    // sometimes shutters, drainpipes, a cornice, a hip roof with dormers and chimneys
    static TvShape TownHouse(TvRandom random, float len, float wid, int floors, int look, float plinth)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f, fh = 3.1f, ground = 3.6f;
        float top = plinth + ground + fh * (floors - 1);
        TvColor wall = Facades[look % Facades.Length], band = wall.Lerp(White, 0.55f), awning = Awnings[(look * 3 + 1) % Awnings.Length];
        TvColor trim = Trims[(look * 5 + 2) % Trims.Length], door = Doors[(look * 7 + 3) % Doors.Length];
        TvColor roof = Roofs[(look * 11 + 1) % Roofs.Length];
        TvColor? shutters = look % 4 == 3 ? Doors[(look + 2) % Doors.Length].Shade(0.9f) : (TvColor?)null;
        b.Box(-hl - 0.05f, -0.6f, -hw - 0.05f, hl + 0.05f, plinth, hw + 0.05f, Plinth);
        b.Box(-hl, plinth, -hw, hl, top, hw, wall);
        // Splash and dirt along the foot of the wall
        b.Box(-hl - 0.03f, plinth, -hw - 0.03f, hl + 0.03f, plinth + 0.45f, hw + 0.03f, wall.Shade(0.72f));
        // The corners: stone blocks in a light colour, or flat pilasters up the whole wall
        foreach (float su in new[] { -1f, 1f })
            foreach (float sv in new[] { -1f, 1f })
            {
                if (look % 3 == 1 || TvShapeBuilder.Lean)
                    b.Box(su * hl - 0.3f, plinth + 0.45f, sv * hw - 0.3f, su * hl + 0.06f * su + (su > 0 ? 0f : 0.3f), top, sv * hw + 0.06f * sv + (sv > 0 ? 0f : 0.3f), band);
                else
                    for (float y = plinth + ground; y < top - 0.4f; y += 0.62f)
                    {
                        float l = ((int)((y - plinth) / 0.62f) % 2 == 0) ? 0.62f : 0.38f;
                        b.Box(su * hl - (su > 0 ? l : -0.06f), y, sv * hw - (sv > 0 ? l : -0.06f), su * hl + (su > 0 ? 0.06f : -l) * 1f + (su < 0 ? 0f : 0f),
                              y + 0.5f, sv * hw + (sv > 0 ? 0.06f : -l), band);
                    }
            }
        // The street floor: big shop windows on the long sides, a door where there is no awning, a band above
        b.Box(-hl - 0.06f, plinth + ground - 0.35f, -hw - 0.06f, hl + 0.06f, plinth + ground - 0.1f, hw + 0.06f, band);
        int count = Math.Max(1, (int)((len - 0.8f) / 3.6f));
        int doorAt = -1;
        for (int i = 0; i < count; i++) if ((i + look) % 3 == 2) { doorAt = i; break; }
        for (int face = 0; face < 2; face++)
        {
            float sign = face == 0 ? -1f : 1f;
            WindowRow(b, face, hl, hw, plinth + 0.45f, 2.3f, 2.2f, 3.6f, TvColor.Hex("#2a2a28"), GlassShop, false, doorAt);
            for (int i = 0; i < count; i++)
            {
                float u = -hl + len * (i + 0.5f) / count;
                if (i == doorAt)
                {
                    // A front door in its own colour, a glass light above it, a stone step
                    b.Box(u - 0.75f, plinth, sign * hw - 0.04f, u + 0.75f, plinth + 3.0f, sign * (hw + 0.1f), trim);
                    b.Box(u - 0.58f, plinth, sign * hw - 0.04f, u + 0.58f, plinth + 2.3f, sign * (hw + 0.14f), door);
                    b.Box(u - 0.5f, plinth + 2.4f, sign * hw - 0.04f, u + 0.5f, plinth + 2.85f, sign * (hw + 0.13f), Glass);
                    b.Box(u - 0.04f, plinth + 1.0f, sign * (hw + 0.14f) - 0.02f, u + 0.04f, plinth + 1.3f, sign * (hw + 0.2f), Gold);
                    b.Box(u - 0.95f, plinth - 0.3f, sign * hw, u + 0.95f, plinth, sign * (hw + 0.6f), TvColor.Hex("#9c968c"));
                    continue;
                }
                TvColor shop = Awnings[(i * 3 + look) % Awnings.Length];
                if ((i + look) % 4 != 1)
                {
                    // An awning over the shop, striped with white
                    float[] q = { u - 1.3f, plinth + 2.95f, sign * hw, u + 1.3f, plinth + 2.95f, sign * hw,
                                  u + 1.3f, plinth + 2.55f, sign * (hw + 1.0f), u - 1.3f, plinth + 2.55f, sign * (hw + 1.0f) };
                    Quad(b, q, u, plinth, sign * (hw - 1f), shop);
                    Quad(b, q, u, plinth + 6f, sign * (hw + 3f), shop.Shade(0.6f));
                    for (int k = 0; k < 4; k++)
                    {
                        float s0 = u - 1.3f + 0.65f * k + 0.2f;
                        b.Box(s0, plinth + 2.36f, sign * (hw + 0.97f) - 0.02f, s0 + 0.25f, plinth + 2.57f, sign * (hw + 0.97f) + 0.02f, k % 2 == 0 ? White : shop);
                    }
                }
                // A painted sign above the shop window: a board with light letters
                TvColor board = (i + look) % 2 == 0 ? TvColor.Hex("#1f2422") : shop.Shade(0.8f);
                b.Box(u - 1.2f, plinth + 3.0f, sign * hw - 0.02f, u + 1.2f, plinth + 3.38f, sign * (hw + 0.08f), board);
                int letters = 4 + (i + look) % 4;
                for (int k = 0; k < letters; k++)
                {
                    float lu = u - 0.95f + 1.9f * (k + 0.5f) / letters;
                    b.Box(lu - 0.07f, plinth + 3.08f, sign * (hw + 0.08f) - 0.01f, lu + 0.07f, plinth + 3.3f, sign * (hw + 0.1f), k % 5 == 3 ? board : (board.r < 60 ? Gold : White));
                }
            }
        }
        // Upper floors: windows all round with flower boxes on some, a thin band between floors
        for (int f = 1; f < floors; f++)
        {
            float y = plinth + ground + fh * (f - 1) + 0.85f;
            for (int face = 0; face < 4; face++)
                WindowRow(b, face, hl, hw, y, 1.55f, 1.0f, 2.4f, trim, Glass, true, -1, random, face < 2 ? 0.3f : 0f, face < 2 ? shutters : null);
            if (f > 1) b.Box(-hl - 0.04f, y - 0.62f, -hw - 0.04f, hl + 0.04f, y - 0.5f, hw + 0.04f, band);
        }
        // Drainpipes down the front corners
        foreach (float su in new[] { -1f, 1f })
            foreach (float sv in new[] { -1f, 1f })
                b.Tube(0.06f, 0.06f, top - plinth, plinth, 6, Pipe, null, 0f, 1f, su * (hl - 0.35f), sv * (hw + 0.1f));
        // The cornice and the roof, with dormers and chimneys
        b.Box(-hl - 0.25f, top - 0.1f, -hw - 0.25f, hl + 0.25f, top + 0.3f, hw + 0.25f, band);
        float rise = Math.Min(hw * 0.75f, 4.5f), eave = top + 0.3f;
        HipRoof(b, hl, hw, eave, rise, 0.1f, roof);
        int dormers = len > 9f ? (int)(len / 5.5f) : 0;
        for (int i = 0; i < dormers; i++)
        {
            float u = -hl + len * (i + 0.5f) / dormers;
            foreach (float sv in new[] { -1f, 1f }) Dormer(b, u, sv, hw, eave, rise, wall, roof, trim);
        }
        float[] chimneys = len > 12f ? new[] { -hl * 0.55f, hl * 0.45f } : new[] { hl * 0.45f };
        foreach (float cx in chimneys)
        {
            TvColor brick = look % 2 == 0 ? TvColor.Hex("#8e4a35") : wall.Shade(0.85f);
            b.Box(cx - 0.4f, top, -0.35f, cx + 0.4f, eave + rise + 0.9f, 0.45f, brick);
            b.Box(cx - 0.5f, eave + rise + 0.9f, -0.45f, cx + 0.5f, eave + rise + 1.05f, 0.55f, TvColor.Hex("#3a3a3a"));
        }
        return b.Done();
    }

    // A dormer on a hip roof: a small gabled window box at u along the house, on the side sv (-1/+1)
    static void Dormer(TvShapeBuilder b, float u, float sv, float hw, float eave, float rise, TvColor wall, TvColor roof, TvColor trim)
    {
        float zf = hw * 0.62f, zb = hw * 0.15f;                      // front and back (in from the wall)
        float yRoof = eave + rise * (1f - zf / hw);                 // the roof under the front
        float w = 0.75f, h = 1.35f, peak = 0.6f;
        b.Box(u - w, yRoof - 0.3f, sv > 0 ? zb : -zf, u + w, yRoof + h, sv > 0 ? zf : -zb, wall);
        float ff = sv * zf;
        b.Box(u - 0.45f, yRoof + 0.15f, ff - 0.02f * sv, u + 0.45f, yRoof + 1.15f, ff + 0.05f * sv, trim);
        b.Box(u - 0.35f, yRoof + 0.25f, ff - 0.02f * sv, u + 0.35f, yRoof + 1.05f, ff + 0.08f * sv, Glass);
        // Its own little roof: two slopes from the front back to the main roof, a gable in the wall colour
        float yt = yRoof + h, o = 0.15f;
        float zbk = sv * zb, zfr = sv * (zf + o);
        b.Triangle(u - w, yt, ff, u + w, yt, ff, u, yt + peak, ff, u, yt, ff - sv, wall);
        float[] l = { u - w - o, yt - 0.08f, zfr, u, yt + peak, zfr, u, yt + peak, zbk, u - w - o, yt - 0.08f, zbk };
        float[] r = { u + w + o, yt - 0.08f, zfr, u, yt + peak, zfr, u, yt + peak, zbk, u + w + o, yt - 0.08f, zbk };
        Quad(b, l, u, yt - 1f, ff - sv, roof);
        Quad(b, r, u, yt - 1f, ff - sv, roof);
        Quad(b, l, u - 3f, yt + 3f, ff, roof.Shade(0.6f));
        Quad(b, r, u + 3f, yt + 3f, ff, roof.Shade(0.6f));
    }

    // A block of flats from the 1950s-60s: brick or light plaster, flat roof, balconies on one side
    static TvShape Apartments(TvRandom random, float len, float wid, int floors, int look, float plinth)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f, fh = 2.9f, top = plinth + fh * floors + 0.3f;
        TvColor[] walls = { TvColor.Hex("#9a5a42"), TvColor.Hex("#e2d6b8"), TvColor.Hex("#c7a06a"), TvColor.Hex("#b9cbd2"), TvColor.Hex("#e7c26a"), TvColor.Hex("#c98d78") };
        TvColor[] panels = { TvColor.Hex("#c8d3d6"), TvColor.Hex("#b6573e"), TvColor.Hex("#3f6f8f"), TvColor.Hex("#e0b53a"), TvColor.Hex("#5f8f5a"), TvColor.Hex("#f2efe6") };
        TvColor wall = walls[look % walls.Length], panel = panels[(look * 5 + 1) % panels.Length];
        b.Box(-hl - 0.05f, -0.6f, -hw - 0.05f, hl + 0.05f, plinth, hw + 0.05f, Plinth);
        b.Box(-hl, plinth, -hw, hl, top, hw, wall);
        b.Box(-hl - 0.1f, top, -hw - 0.1f, hl + 0.1f, top + 0.45f, hw + 0.1f, TvColor.Hex("#5a5c5c"));
        for (int f = 0; f < floors; f++)
        {
            float y = plinth + fh * f + 0.9f;
            for (int face = 0; face < 4; face++) WindowRow(b, face, hl, hw, y, 1.3f, 1.25f, 3.0f, White, Glass, false, face == 1 && f == 0 ? 1000 : -1, random, face == 1 ? 0.15f : 0f);
        }
        // Balconies on the -z side, every other window
        int count = Math.Max(1, (int)((len - 0.8f) / 3.0f));
        for (int f = 1; f < floors; f++)
            for (int i = 0; i < count; i += 2)
            {
                float u = -hl + len * (i + 0.5f) / count, y = plinth + fh * f + 0.1f;
                b.Box(u - 1.3f, y - 0.15f, -hw - 1.2f, u + 1.3f, y, -hw, TvColor.Hex("#8d8a84"));
                b.Box(u - 1.3f, y, -hw - 1.2f, u + 1.3f, y + 1.0f, -hw - 1.12f, (i / 2 + f) % 3 == 0 ? panel.Lerp(White, 0.3f) : panel);
                if (random.Value() < 0.35f)
                    for (int k = 0; k < 4; k++)
                        b.Blob(0.16f, 1f, 0.8f, 1f, u - 0.9f + k * 0.6f, y + 1.1f, -hw - 1.1f, 0.3f, 0, true, k % 2 == 0 ? Leaf : Flowers[(i + f + k) % Flowers.Length]);
            }
        // The stairwell door on +z
        b.Box(-0.8f, plinth - 0.05f, hw - 0.05f, 0.8f, plinth + 2.4f, hw + 0.12f, White);
        b.Box(-0.65f, plinth, hw, 0.65f, plinth + 2.25f, hw + 0.16f, Doors[(look * 3 + 2) % Doors.Length]);
        // A light band along the bottom and the edge of the roof in the balconies' colour
        b.Box(-hl - 0.03f, plinth, -hw - 0.03f, hl + 0.03f, plinth + 0.5f, hw + 0.03f, wall.Shade(0.75f));
        b.Box(-hl - 0.12f, top + 0.3f, -hw - 0.12f, hl + 0.12f, top + 0.5f, hw + 0.12f, panel);
        b.Box(-1.2f, plinth + 2.5f, hw, 1.2f, plinth + 2.65f, hw + 1.0f, TvColor.Hex("#5a5c5c"));
        return b.Done();
    }

    // A wooden villa: red or yellow with white corners and windows, a tiled gable roof, a porch
    static TvShape Villa(TvRandom random, float len, float wid, int look, float plinth)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f, eaves = plinth + (len > 11f ? 5.2f : 2.7f);
        TvColor[] walls = { TvColor.Hex("#8f3426"), TvColor.Hex("#e0c27a"), TvColor.Hex("#dfe3e0"), TvColor.Hex("#9cc0d8"), TvColor.Hex("#a9c49a"), TvColor.Hex("#e9d9a6") };
        TvColor wall = walls[look % walls.Length];
        TvColor trim = look % walls.Length == 2 ? TvColor.Hex("#3d5a73") : look % walls.Length == 5 ? TvColor.Hex("#2f4a3a") : White;
        b.Box(-hl - 0.05f, -0.6f, -hw - 0.05f, hl + 0.05f, plinth, hw + 0.05f, Plinth);
        b.Box(-hl, plinth, -hw, hl, eaves, hw, wall);
        foreach (float su in new[] { -1f, 1f })
            foreach (float sv in new[] { -1f, 1f })
                b.Box(su * hl - 0.1f, plinth, sv * hw - 0.1f, su * hl + 0.1f, eaves, sv * hw + 0.1f, trim);
        int levels = eaves - plinth > 4f ? 2 : 1;
        for (int f = 0; f < levels; f++)
            for (int face = 0; face < 4; face++)
                WindowRow(b, face, hl, hw, plinth + 0.9f + f * 2.6f, 1.15f, 0.95f, 2.6f, trim, Glass, true, face == 1 && f == 0 ? 0 : -1, random, f == 0 ? 0.35f : 0f);
        // Standing wooden panels: a thin darker line every 0.5 m on the long sides
        for (float u = -hl + 0.5f; u < hl - 0.3f; u += 0.5f)
            foreach (float sv in new[] { -1f, 1f })
                b.Box(u - 0.025f, plinth + 0.05f, sv * hw - 0.02f, u + 0.025f, eaves - 0.05f, sv * (hw + 0.025f), wall.Shade(0.8f));
        // Door and porch on +z
        float du = -hl + len * 0.5f / Math.Max(1, (int)((len - 0.8f) / 2.6f));
        b.Box(du - 0.55f, plinth, hw - 0.02f, du + 0.55f, plinth + 2.15f, hw + 0.1f, trim);
        b.Box(du - 0.45f, plinth, hw, du + 0.45f, plinth + 2.05f, hw + 0.14f, Doors[(look * 5 + 1) % Doors.Length]);
        b.Box(du - 1.1f, plinth - 0.3f, hw, du + 1.1f, plinth, hw + 1.4f, TvColor.Hex("#8a8680"));
        GableRoof(b, hl, hw, eaves, hw * 0.75f, 0.4f, Roofs[(look * 7 + 1) % Roofs.Length], wall);
        b.Box(hl * 0.4f - 0.3f, eaves + hw * 0.3f, -0.6f, hl * 0.4f + 0.3f, eaves + hw * 0.75f + 0.8f, 0f, TvColor.Hex("#7d746c"));
        return b.Done();
    }

    // A white church: a long nave with tall windows and a steep roof, a tower at the west end with a copper spire
    // and a golden cross, clock faces on the tower
    static TvShape Church(TvRandom random, float len, float wid, float plinth)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f;
        float tower = Math.Min(wid * 0.55f, 7f), th = tower / 2f;
        float naveFrom = -hl + tower;                     // the nave from the tower to the east end
        float nl = (hl - naveFrom) / 2f, nc = (hl + naveFrom) / 2f;
        float eaves = plinth + Math.Max(6f, wid * 0.55f);
        TvColor wall = White, roof = RoofDark;
        b.Box(-hl - 0.1f, -0.8f, -hw - 0.1f, hl + 0.1f, plinth, hw + 0.1f, Plinth);
        b.Box(naveFrom, plinth, -hw, hl, eaves, hw, wall);
        // Tall windows along the nave
        int count = Math.Max(2, (int)(nl * 2f / 4f));
        for (int i = 0; i < count; i++)
        {
            float u = naveFrom + 2f * nl * (i + 0.5f) / count;
            foreach (float sv in new[] { -1f, 1f })
            {
                b.Box(u - 0.6f, plinth + 1.6f, sv * hw - 0.03f, u + 0.6f, eaves - 1.2f, sv * hw + 0.05f, Glass);
                b.Box(u - 0.4f, eaves - 1.2f, sv * hw - 0.03f, u + 0.4f, eaves - 0.7f, sv * hw + 0.05f, Glass);
                b.Box(u - 0.05f, plinth + 1.6f, sv * hw - 0.03f, u + 0.05f, eaves - 0.7f, sv * hw + 0.07f, wall);
            }
        }
        // The nave's roof (made round its own middle, moved along)
        var roofB = new TvShapeBuilder(random);
        GableRoof(roofB, nl, hw, eaves, hw * 1.1f, 0.5f, roof, wall);
        Append(b, roofB.Done(), nc, 0f);
        // The tower
        float towerTop = eaves + hw * 1.1f + 5f;
        b.Box(-hl, plinth, -th, -hl + tower, towerTop, th, wall);
        b.Box(-hl - 0.15f, towerTop - 0.3f, -th - 0.15f, -hl + tower + 0.15f, towerTop + 0.2f, th + 0.15f, wall.Shade(0.9f));
        float tx = -hl + th;
        // Sound openings and clock faces on all four sides
        foreach (int face in new[] { 0, 1, 2, 3 })
        {
            float y = towerTop - 2.4f;
            if (face < 2)
            {
                float s = face == 0 ? -1f : 1f;
                b.Box(tx - 0.5f, y, s * th - 0.03f, tx + 0.5f, y + 1.5f, s * th + 0.05f, Glass);
                b.Box(tx - 0.8f, y - 2.6f, s * th - 0.03f, tx + 0.8f, y - 1.0f, s * th + 0.08f, TvColor.Hex("#1d2b3a"));
                b.Box(tx - 0.07f, y - 1.85f, s * th, tx + 0.07f, y - 1.05f, s * th + 0.12f, Gold);
            }
            else
            {
                float s = face == 2 ? -1f : 1f;
                b.Box(tx + s * th - 0.03f, y, -0.5f, tx + s * th + 0.05f, y + 1.5f, 0.5f, Glass);
                b.Box(tx + s * th - 0.03f, y - 2.6f, -0.8f, tx + s * th + 0.08f, y - 1.0f, 0.8f, TvColor.Hex("#1d2b3a"));
                b.Box(tx + s * th, y - 1.85f, -0.07f, tx + s * th + 0.12f, y - 1.05f, 0.07f, Gold);
            }
        }
        // The door at the foot of the tower
        b.Box(-hl - 0.08f, plinth, -0.9f, -hl + 0.05f, plinth + 3.2f, 0.9f, TvColor.Hex("#5b3a26"));
        // The spire: copper, eight-sided, with a golden ball and cross
        float spire = Math.Max(8f, tower * 1.6f);
        b.Cone(th * 1.15f, spire, towerTop + 0.2f, 8, Copper, tx, 0f);
        b.Blob(0.25f, 1f, 1f, 1f, tx, towerTop + spire + 0.2f, 0f, 0f, 0, false, Gold);
        b.Box(tx - 0.06f, towerTop + spire + 0.3f, -0.06f, tx + 0.06f, towerTop + spire + 1.7f, 0.06f, Gold);
        b.Box(tx - 0.45f, towerTop + spire + 1.2f, -0.06f, tx + 0.45f, towerTop + spire + 1.32f, 0.06f, Gold);
        // A low round apse at the east end
        b.Tube(hw * 0.6f, hw * 0.6f, eaves - plinth - 1.5f, plinth, 10, wall, null, 0f, 1f, hl, 0f);
        b.Cone(hw * 0.68f, hw * 0.7f, eaves - 1.5f, 10, roof, hl, 0f);
        return b.Done();
    }

    // A town hall of brick: two tall floors, light stone bands, a broad stair to the door, a clock tower in the middle
    static TvShape TownHall(TvRandom random, float len, float wid, float plinth)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f, top = plinth + 8.6f;
        TvColor brick = TvColor.Hex("#8e4a35"), stone = TvColor.Hex("#e3dccb");
        b.Box(-hl - 0.1f, -0.6f, -hw - 0.1f, hl + 0.1f, plinth + 0.5f, hw + 0.1f, stone.Shade(0.8f));
        b.Box(-hl, plinth, -hw, hl, top, hw, brick);
        b.Box(-hl - 0.08f, plinth + 4.1f, -hw - 0.08f, hl + 0.08f, plinth + 4.35f, hw + 0.08f, stone);
        b.Box(-hl - 0.3f, top - 0.2f, -hw - 0.3f, hl + 0.3f, top + 0.35f, hw + 0.3f, stone);
        for (int f = 0; f < 2; f++)
            for (int face = 0; face < 4; face++)
                WindowRow(b, face, hl, hw, plinth + 1.2f + f * 4.3f, 2.2f, 1.2f, 2.8f, stone, Glass, true, face == 0 && f == 0 ? Math.Max(1, (int)((len - 0.8f) / 2.8f)) / 2 : -1);
        // The main door and stair on -z
        b.Box(-1.4f, plinth, -hw - 0.15f, 1.4f, plinth + 3.6f, -hw + 0.02f, stone);
        b.Box(-1.0f, plinth, -hw - 0.2f, 1.0f, plinth + 3.1f, -hw + 0.02f, TvColor.Hex("#4a2e20"));
        for (int s = 0; s < 3; s++) b.Box(-2.4f + s * 0.3f, plinth - 0.5f + s * 0.17f, -hw - 1.6f + s * 0.4f, 2.4f - s * 0.3f, plinth - 0.33f + s * 0.17f, -hw, stone.Shade(0.9f));
        HipRoof(b, hl, hw, top + 0.35f, Math.Min(hw * 0.7f, 5f), 0.1f, Copper.Shade(0.85f));
        // The clock tower
        float t = Math.Min(4.2f, wid * 0.4f), tt = top + 9f;
        b.Box(-t / 2f, top, -t / 2f, t / 2f, tt, t / 2f, brick);
        b.Box(-t / 2f - 0.15f, tt - 0.25f, -t / 2f - 0.15f, t / 2f + 0.15f, tt + 0.15f, t / 2f + 0.15f, stone);
        foreach (float s in new[] { -1f, 1f })
        {
            b.Box(-1.1f, tt - 3.3f, s * t / 2f - 0.03f, 1.1f, tt - 1.1f, s * t / 2f + 0.08f, White);
            b.Box(-0.07f, tt - 2.5f, s * t / 2f, 0.07f, tt - 1.4f, s * t / 2f + 0.14f, Iron);
            b.Box(-0.6f, tt - 2.27f, s * t / 2f, 0.0f, tt - 2.13f, s * t / 2f + 0.14f, Iron);
            b.Box(s * t / 2f - 0.03f, tt - 3.3f, -1.1f, s * t / 2f + 0.08f, tt - 1.1f, 1.1f, White);
        }
        b.Cone(t * 0.75f, 5.5f, tt + 0.15f, 4, Copper, 0f, 0f);
        // Two Swedish flags on short poles out from the front, and flowers in urns by the stair
        foreach (float fx in new[] { -hl * 0.55f, hl * 0.55f })
        {
            Beam(b, fx, plinth + 5.2f, -hw, fx, plinth + 6.6f, -hw - 1.6f, 0.06f, 0.06f, White);
            SwedishFlag(b, fx, plinth + 6.6f, -hw - 1.6f, 1.2f, 0.25f);
        }
        foreach (float ux in new[] { -2.9f, 2.9f })
        {
            b.Tube(0.35f, 0.45f, 0.7f, plinth - 0.5f, 8, stone.Shade(0.85f), null, 0f, 1f, ux, -hw - 1.2f);
            for (int k = 0; k < 5; k++) b.Blob(0.17f, 1f, 0.8f, 1f, ux - 0.2f + 0.1f * k, plinth + 0.3f, -hw - 1.2f + (k % 2) * 0.15f, 0.3f, 0, true, k % 2 == 0 ? Flowers[k % Flowers.Length] : Leaf);
        }
        b.Box(-0.05f, tt + 5.5f, -0.05f, 0.05f, tt + 7f, 0.05f, Gold);
        return b.Done();
    }

    // A long low hall (a school, a sports hall, a shop): light panels, a band of windows, a flat roof
    static TvShape Hall(TvRandom random, float len, float wid, int look, float plinth)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f, top = plinth + 7f;
        TvColor wall = look % 2 == 0 ? TvColor.Hex("#c9c6bc") : TvColor.Hex("#b45a3c");
        b.Box(-hl - 0.05f, -0.6f, -hw - 0.05f, hl + 0.05f, plinth, hw + 0.05f, Plinth);
        b.Box(-hl, plinth, -hw, hl, top, hw, wall);
        for (int face = 0; face < 4; face++) WindowRow(b, face, hl, hw, plinth + 3.8f, 1.6f, 3.2f, 4.0f, TvColor.Hex("#3b3d3e"), Glass, false);
        for (int face = 0; face < 2; face++) WindowRow(b, face, hl, hw, plinth + 0.9f, 1.6f, 2.0f, 6.0f, TvColor.Hex("#3b3d3e"), Glass, false);
        b.Box(-hl - 0.2f, top, -hw - 0.2f, hl + 0.2f, top + 0.5f, hw + 0.2f, TvColor.Hex("#4e5254"));
        // A coloured band round the top and a big sign on the front
        TvColor stripe = Awnings[look % Awnings.Length];
        b.Box(-hl - 0.04f, top - 1.1f, -hw - 0.04f, hl + 0.04f, top - 0.6f, hw + 0.04f, stripe);
        b.Box(-3.5f, top - 2.9f, -hw - 0.12f, 3.5f, top - 1.5f, -hw, White);
        for (int k = 0; k < 7; k++) b.Box(-3f + k * 0.9f, top - 2.6f, -hw - 0.16f, -2.5f + k * 0.9f, top - 1.8f, -hw - 0.1f, stripe);
        return b.Done();
    }

    // A shape added into a builder, moved along x and z
    static void Append(TvShapeBuilder b, TvShape s, float x, float z)
    {
        for (int i = 0; i < s.triangles.Length; i += 3)
        {
            int a = s.triangles[i], c = s.triangles[i + 1], d = s.triangles[i + 2];
            float nx = s.normals[a * 3], ny = s.normals[a * 3 + 1], nz = s.normals[a * 3 + 2];
            float px = s.positions[a * 3] + x, py = s.positions[a * 3 + 1], pz = s.positions[a * 3 + 2] + z;
            b.Triangle(px, py, pz, s.positions[c * 3] + x, s.positions[c * 3 + 1], s.positions[c * 3 + 2] + z,
                       s.positions[d * 3] + x, s.positions[d * 3 + 1], s.positions[d * 3 + 2] + z,
                       px - nx, py - ny, pz - nz, new TvColor(s.colors[a * 3], s.colors[a * 3 + 1], s.colors[a * 3 + 2]));
        }
    }

    // ---- People who watch ----

    public enum Pose { Stand, Cheer, Sit, Walk, Child }

    // A person in a pose, facing -z, standing on y = 0 (sitting: on a seat 0.47 m up). look picks the person.
    // The jointed figure of TvPeople.cs in one frame of an animation.
    public static TvShape Person(TvRandom random, Pose pose, int look)
    {
        switch (pose)
        {
            case Pose.Cheer: return Figure(random, look, false, Animate(Act.Cheer, 1, look));
            case Pose.Sit: return Figure(random, look, false, Animate(Act.Sit, 0, look));
            case Pose.Walk: return Figure(random, look, false, Animate(Act.Walk, 2, look));
            case Pose.Child: return Figure(random, look, true, Animate(Act.Idle, 0, look));
            default: return Figure(random, look, false, Animate(Act.Idle, 0, look));
        }
    }

    // A dog on a lead's length, facing -z
    public static TvShape Dog(TvRandom random, int look)
    {
        var b = new TvShapeBuilder(random);
        TvColor fur = look % 3 == 0 ? TvColor.Hex("#c08a4a") : look % 3 == 1 ? TvColor.Hex("#2e2a28") : TvColor.Hex("#e8e2d4");
        b.Blob(0.17f, 1f, 0.75f, 1.9f, 0f, 0.42f, 0f, 0.05f, 1, false, fur);
        b.Blob(0.12f, 1f, 1f, 1.2f, 0f, 0.6f, -0.33f, 0.05f, 1, false, fur);
        b.Blob(0.06f, 1f, 0.9f, 1.4f, 0f, 0.57f, -0.47f, 0f, 0, false, fur.Shade(0.8f));
        b.Blob(0.03f, 1f, 1f, 1f, 0f, 0.6f, -0.55f, 0f, 0, false, Shoe);
        foreach (float x in new[] { -0.09f, 0.09f })
            foreach (float z in new[] { -0.22f, 0.22f })
                Beam(b, x, 0.4f, z, x, 0f, z, 0.06f, 0.06f, fur);
        Beam(b, 0f, 0.48f, 0.3f, 0f, 0.68f, 0.45f, 0.05f, 0.05f, fur);
        foreach (float x in new[] { -0.07f, 0.07f }) b.Cone(0.04f, 0.1f, 0.68f, 4, fur.Shade(0.8f), x, -0.3f);
        return b.Done();
    }
}
