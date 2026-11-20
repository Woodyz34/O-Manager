using System;

// The old stone town of southern Europe (2026-11-20, Anders: sprint maps from Spain, Portugal and the like need a
// poorer old town, not a Swedish one: cobbled lanes, houses of stone, most of them whitewashed, granite round the
// doors and windows, a painted band at the foot of the wall, terracotta roofs, small iron balconies, lanterns on
// brackets, flower pots, washing on lines). Chosen with the map's terrain look (MapImport.TerrainLook.town).
public static partial class TvCityShapes
{
    public enum Style { Swedish, OldStone }

    static readonly TvColor Granite = TvColor.Hex("#8f8b82"), GraniteDark = TvColor.Hex("#6e6a63"), Terracotta = TvColor.Hex("#b85a3a"),
        PotClay = TvColor.Hex("#b0603c"), Rubble = TvColor.Hex("#978d7e"), Geranium = TvColor.Hex("#d8283a");
    // Whitewash in many tones (old, newer, a little yellow), a few ochre and pale-blue houses
    static readonly TvColor[] Limewash =
    {
        TvColor.Hex("#f2efe7"), TvColor.Hex("#ebe5d6"), TvColor.Hex("#e6dfcd"), TvColor.Hex("#f0e9d2"), TvColor.Hex("#e2d9c4"),
        TvColor.Hex("#e8c87a"), TvColor.Hex("#eee8dc"), TvColor.Hex("#d9e0e6"), TvColor.Hex("#e9e2d0")
    };
    // The painted band at the foot of the wall
    static readonly TvColor[] Dados =
        { TvColor.Hex("#8c9196"), TvColor.Hex("#3d6ea8"), TvColor.Hex("#d9a52e"), TvColor.Hex("#8a2f2a"), TvColor.Hex("#5d7d98"), TvColor.Hex("#9b968c") };
    static readonly TvColor[] OldWood =
        { TvColor.Hex("#5b3a26"), TvColor.Hex("#4a2e20"), TvColor.Hex("#6b4a2e"), TvColor.Hex("#2f5a3c"), TvColor.Hex("#36587a"), TvColor.Hex("#5e2a24") };
    static readonly TvColor[] RoofTiles =
        { TvColor.Hex("#b85a3a"), TvColor.Hex("#a24c32"), TvColor.Hex("#c76a44"), TvColor.Hex("#9a5238"), TvColor.Hex("#b4663f") };

    // A building in the given style; the Swedish town is the one above
    public static TvShape Building(TvRandom random, Kind kind, float len, float wid, int floors, int look, Style style, float plinth = 0.25f)
    {
        if (style == Style.Swedish) return Building(random, kind, len, wid, floors, look, plinth);
        switch (kind)
        {
            case Kind.Church: return StoneChurch(random, len, wid, plinth);
            case Kind.TownHall: return StoneHouse(random, len, wid, 3, look, plinth, true);
            case Kind.Villa: return StoneHouse(random, len, wid, 1 + look % 2, look, plinth, false);
            default: return StoneHouse(random, len, wid, Math.Max(1, Math.Min(floors, 4)), look, plinth, false);
        }
    }

    // An old town house of stone: whitewashed (now and then bare stone), stained by the years, a painted band at the
    // foot, granite corners and frames, small windows with shutters, wooden doors, iron balconies, a low tiled roof
    // with wide eaves. grand: the town hall's version, with a long balcony, a clock gable and banners.
    static TvShape StoneHouse(TvRandom random, float len, float wid, int floors, int look, float plinth, bool grand)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f, fh = grand ? 3.6f : 2.9f, top = plinth + fh * floors + 0.25f;
        bool bare = !grand && look % 7 == 5;
        TvColor wall = bare ? Rubble : Limewash[look % Limewash.Length];
        TvColor dado = Dados[(look * 5 + 1) % Dados.Length], wood = OldWood[(look * 3 + 2) % OldWood.Length];
        TvColor? shutter = look % 3 == 0 ? OldWood[(look + 3) % OldWood.Length] : (TvColor?)null;
        bool quoins = grand || look % 3 != 2;
        b.Box(-hl - 0.04f, -0.8f, -hw - 0.04f, hl + 0.04f, plinth, hw + 0.04f, GraniteDark);
        b.Box(-hl, plinth, -hw, hl, top, hw, wall);

        if (bare)
        {
            // Rubble stone: a jumble of stones a little out of the wall, in many greys and browns
            for (int face = 0; face < 4; face++)
            {
                float along = face < 2 ? hl : hw;
                int n = (int)(along * (top - plinth) * 0.9f);
                for (int i = 0; i < n; i++)
                {
                    float u = -along + 2f * along * random.Value(), y = plinth + (top - plinth) * random.Value();
                    float sw = 0.25f + 0.3f * random.Value(), sh = 0.16f + 0.18f * random.Value();
                    TvColor c = Rubble.Lerp(random.Value() < 0.5f ? TvColor.Hex("#c2b8a6") : TvColor.Hex("#6c6458"), random.Value() * 0.6f);
                    FaceBox(b, face, hl, hw, u - sw / 2f, y, u + sw / 2f, Math.Min(top, y + sh), 0f, 0.04f, c);
                }
            }
        }
        else
        {
            // The painted band at the foot of the wall
            float dh = 0.8f + 0.3f * ((look * 7) % 3);
            b.Box(-hl - 0.03f, plinth, -hw - 0.03f, hl + 0.03f, plinth + dh, hw + 0.03f, dado);
            // Age: a grey shadow under the eaves, damp stains near the foot, patches where the whitewash fell off
            b.Box(-hl - 0.015f, top - 0.6f, -hw - 0.015f, hl + 0.015f, top, hw + 0.015f, wall.Shade(0.9f));
            for (int face = 0; face < 4 && !TvShapeBuilder.Lean; face++)
            {
                float along = face < 2 ? hl : hw;
                int n = 2 + (int)(along / 3f);
                for (int i = 0; i < n; i++)
                {
                    float u = -along + 0.4f + (2f * along - 0.8f) * random.Value();
                    bool low = random.Value() < 0.55f;
                    float y = low ? plinth + dh : plinth + dh + (top - plinth - dh) * random.Value();
                    float sw = 0.5f + 1.4f * random.Value(), sh = low ? 0.3f + 0.6f * random.Value() : 0.3f + 0.5f * random.Value();
                    TvColor c = low ? wall.Lerp(TvColor.Hex("#8a8a78"), 0.25f + 0.2f * random.Value()) : wall.Lerp(Rubble, 0.35f + 0.3f * random.Value());
                    FaceBox(b, face, hl, hw, u - sw / 2f, y, u + sw / 2f, Math.Min(top - 0.6f, y + sh), 0f, 0.012f, c);
                }
            }
        }
        // Granite corners: long and short blocks in turn up the whole wall
        if (quoins && TvShapeBuilder.Lean)
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f })
                    b.Box(su > 0 ? hl - 0.4f : -hl - 0.05f, plinth, sv > 0 ? hw - 0.4f : -hw - 0.05f, su > 0 ? hl + 0.05f : -hl + 0.4f, top - 0.2f, sv > 0 ? hw + 0.05f : -hw + 0.4f, Granite);
        else if (quoins)
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f })
                    for (int k = 0; plinth + k * 0.45f < top - 0.2f; k++)
                    {
                        float y = plinth + k * 0.45f, l = k % 2 == 0 ? 0.55f : 0.32f;
                        TvColor g = Granite.Lerp(GraniteDark, (k * 37 % 10) / 20f);
                        b.Box(su > 0 ? hl - l : -hl - 0.05f, y, sv > 0 ? hw - 0.32f : -hw - 0.05f, su > 0 ? hl + 0.05f : -hl + l, y + 0.42f, sv > 0 ? hw + 0.05f : -hw + 0.32f, g);
                        b.Box(su > 0 ? hl - 0.32f : -hl - 0.05f, y, sv > 0 ? hw - l : -hw - 0.05f, su > 0 ? hl + 0.05f : -hl + 0.32f, y + 0.42f, sv > 0 ? hw + 0.05f : -hw + l, g);
                    }

        // Doors, windows and balconies on the long sides; a window or two on the ends
        int count = Math.Max(1, (int)(len / 3.2f));
        for (int face = 0; face < 4; face++)
        {
            bool front = face < 2;
            float along = front ? hl : hw;
            int n = front ? count : Math.Max(1, (int)(wid / 4.5f));
            for (int i = 0; i < n; i++)
            {
                float u = -along + 2f * along * (i + 0.5f) / n;
                bool door = front && i == (look + face) % n;
                if (door)
                {
                    float dw = grand ? 1.1f : 0.6f, dt = grand ? 3.0f : 2.25f;
                    FaceBox(b, face, hl, hw, u - dw - 0.24f, plinth - 0.05f, u + dw + 0.24f, plinth + dt + 0.26f, -0.02f, 0.1f, Granite);
                    FaceBox(b, face, hl, hw, u - dw, plinth, u + dw, plinth + dt, -0.02f, 0.06f, wood);
                    // Old planks, a big iron ring
                    for (float pu = u - dw + 0.2f; pu < u + dw; pu += 0.2f)
                        FaceBox(b, face, hl, hw, pu - 0.01f, plinth + 0.05f, pu + 0.01f, plinth + dt - 0.05f, 0.06f, 0.07f, wood.Shade(0.75f));
                    FaceBox(b, face, hl, hw, u + dw * 0.5f - 0.05f, plinth + 1.05f, u + dw * 0.5f + 0.05f, plinth + 1.2f, 0.06f, 0.1f, Iron);
                    // A worn stone step
                    FaceBox(b, face, hl, hw, u - dw - 0.3f, plinth - 0.25f, u + dw + 0.3f, plinth, 0f, 0.45f, Granite.Shade(0.95f));
                    if (look % 2 == 0) Pots(b, face, hl, hw, u + dw + 0.6f, plinth, look + i);
                }
                else if (front)
                {
                    // On the street floor: a small barred window, or a second (stable) door
                    if ((i + look) % 4 == 3) { FaceBox(b, face, hl, hw, u - 0.75f, plinth - 0.05f, u + 0.75f, plinth + 2.3f, -0.02f, 0.09f, Granite);
                                               FaceBox(b, face, hl, hw, u - 0.55f, plinth, u + 0.55f, plinth + 2.1f, -0.02f, 0.06f, OldWood[(i + look) % OldWood.Length]); }
                    else Window(b, face, hl, hw, u, plinth + 1.3f, 0.6f, 0.75f, null, true);
                }
                else if ((i + look) % 2 == 1) Window(b, face, hl, hw, u, plinth + 1.3f, 0.5f, 0.6f, null, true);

                for (int f = 1; f < floors; f++)
                {
                    float y = plinth + fh * f;
                    bool balcony = front && (grand ? f == 1 : (i + f + look) % 3 == 0);
                    if (!front && (i + f) % 2 == 0) continue;
                    if (balcony && !grand)
                    {
                        Window(b, face, hl, hw, u, y + 0.1f, 0.85f, 2.1f, shutter, false);
                        Balcony(b, face, hl, hw, u, y, 1.5f, 0.55f, (i + f + look) % 2 == 0);
                    }
                    else Window(b, face, hl, hw, u, y + 0.85f, 0.85f, 1.25f, shutter, false);
                    // Washing hung out of a window now and then
                    if (front && !grand && (i * 7 + f * 3 + look) % 11 == 4) Washing(b, face, hl, hw, u, y + 0.75f);
                }
            }
            if (grand && front)
            {
                // The town hall: one long balcony over the door, banners under it
                float y = plinth + fh;
                Balcony(b, face, hl, hw, 0f, y, len * 0.6f, 0.8f, false);
                for (int k = -1; k <= 1; k += 2)
                {
                    float u = k * len * 0.18f;
                    FaceBox(b, face, hl, hw, u - 0.45f, y - 2.2f, u + 0.45f, y - 0.1f, 0.85f, 0.88f, k < 0 ? TvColor.Hex("#a8282a") : TvColor.Hex("#e0b030"));
                }
            }
        }
        // A lantern on a bracket at one corner of the front
        WallLantern(b, 0, hl, hw, -hl + 0.9f, plinth + 3.0f);
        // A downpipe
        b.Tube(0.055f, 0.055f, top - plinth, plinth, 6, Pipe.Lerp(GraniteDark, 0.4f), null, 0f, 1f, hl - 0.45f, -hw - 0.09f);

        // The roof: low, tiled, with wide eaves and rows of round tiles, a white band under the edge
        b.Box(-hl - 0.12f, top - 0.18f, -hw - 0.12f, hl + 0.12f, top, hw + 0.12f, bare ? Granite : TvColor.Hex("#f4f1ea"));
        TvColor tiles = RoofTiles[(look * 3 + 1) % RoofTiles.Length];
        float rise = Math.Min(hw * 0.42f, 2.3f), over = 0.55f;
        HipRoof(b, hl, hw, top, rise, over, tiles);
        if (!TvShapeBuilder.Lean) TileRows(b, hl, hw, top, rise, over, tiles);
        if (grand)
        {
            // A clock gable over the middle of the front, with a little bell
            float s = -1f;
            b.Box(-1.6f, top, s * hw - 0.05f, 1.6f, top + 2.4f, s * hw + 0.6f, wall);
            b.Triangle(-1.6f, top + 2.4f, s * hw - 0.05f, 1.6f, top + 2.4f, s * hw - 0.05f, 0f, top + 3.3f, s * hw - 0.05f, 0f, top + 2.6f, 0f, wall);
            b.Box(-0.6f, top + 0.6f, s * hw - 0.1f, 0.6f, top + 1.8f, s * hw - 0.04f, White);
            b.Box(-0.04f, top + 1.15f, s * hw - 0.13f, 0.04f, top + 1.7f, s * hw - 0.09f, Iron);
            b.Box(-0.04f, top + 1.15f, s * hw - 0.13f, 0.4f, top + 1.23f, s * hw - 0.09f, Iron);
        }
        if (look % 2 == 1)
        {
            float cx = hl * 0.4f;
            b.Box(cx - 0.35f, top, -0.3f, cx + 0.35f, top + rise + 0.7f, 0.4f, wall);
            b.Box(cx - 0.45f, top + rise + 0.7f, -0.4f, cx + 0.45f, top + rise + 0.82f, 0.5f, tiles);
        }
        return b.Done();
    }

    // A box on one face of a house (face: 0 = -z, 1 = +z, 2 = -x, 3 = +x): u along the face, y up, d0..d1 out from it
    static void FaceBox(TvShapeBuilder b, int face, float hl, float hw, float u0, float y0, float u1, float y1, float d0, float d1, TvColor c)
    {
        float depth = face < 2 ? hw : hl, sign = face == 0 || face == 2 ? -1f : 1f;
        float a = sign * (depth + d0), e = sign * (depth + d1);
        if (face < 2) b.Box(u0, y0, Math.Min(a, e), u1, y1, Math.Max(a, e), c);
        else b.Box(Math.Min(a, e), y0, u0, Math.Max(a, e), y1, u1, c);
    }

    // A small window deep in a thick granite frame, white inner frame, sometimes wooden shutters, sometimes iron bars
    static void Window(TvShapeBuilder b, int face, float hl, float hw, float u, float y, float w, float h, TvColor? shutter, bool bars)
    {
        FaceBox(b, face, hl, hw, u - w / 2f - 0.17f, y - 0.17f, u + w / 2f + 0.17f, y + h + 0.17f, -0.02f, 0.07f, Granite);
        FaceBox(b, face, hl, hw, u - w / 2f, y, u + w / 2f, y + h, -0.02f, 0.075f, White);
        FaceBox(b, face, hl, hw, u - w / 2f + 0.06f, y + 0.06f, u + w / 2f - 0.06f, y + h - 0.06f, -0.02f, 0.08f, Glass);
        FaceBox(b, face, hl, hw, u - 0.025f, y + 0.06f, u + 0.025f, y + h - 0.06f, -0.02f, 0.085f, White);
        FaceBox(b, face, hl, hw, u - w / 2f - 0.22f, y - 0.24f, u + w / 2f + 0.22f, y - 0.15f, 0f, 0.16f, Granite.Shade(0.95f));
        if (shutter.HasValue)
            foreach (float s in new[] { -1f, 1f })
            {
                float c = u + s * (w / 2f + 0.17f + w / 4f + 0.02f);
                FaceBox(b, face, hl, hw, c - w / 4f, y - 0.05f, c + w / 4f, y + h + 0.05f, 0.03f, 0.08f, shutter.Value);
                for (float sy = y + 0.12f; sy < y + h && !TvShapeBuilder.Lean; sy += 0.18f)
                    FaceBox(b, face, hl, hw, c - w / 4f + 0.04f, sy, c + w / 4f - 0.04f, sy + 0.04f, 0.08f, 0.095f, shutter.Value.Shade(0.75f));
            }
        if (bars && !TvShapeBuilder.Lean)
            for (float bu = u - w / 2f + 0.12f; bu < u + w / 2f - 0.05f; bu += 0.14f)
                FaceBox(b, face, hl, hw, bu - 0.015f, y, bu + 0.015f, y + h, 0.12f, 0.15f, Iron);
    }

    // A small wrought-iron balcony: a granite slab on two brackets, black bars, flower pots on some
    static void Balcony(TvShapeBuilder b, int face, float hl, float hw, float u, float y, float w, float deep, bool flowers)
    {
        FaceBox(b, face, hl, hw, u - w / 2f, y - 0.12f, u + w / 2f, y + 0.02f, 0f, deep, Granite);
        foreach (float s in new[] { -1f, 1f })
            FaceBox(b, face, hl, hw, u + s * (w / 2f - 0.15f) - 0.06f, y - 0.4f, u + s * (w / 2f - 0.15f) + 0.06f, y - 0.12f, 0f, deep * 0.7f, Granite.Shade(0.9f));
        FaceBox(b, face, hl, hw, u - w / 2f, y + 0.92f, u + w / 2f, y + 0.97f, deep - 0.05f, deep, Iron);
        if (TvShapeBuilder.Lean)
        {
            // Away from the runner the bars are one dark railing
            FaceBox(b, face, hl, hw, u - w / 2f, y, u + w / 2f, y + 0.92f, deep - 0.04f, deep - 0.015f, Iron.Lerp(Granite, 0.3f));
            return;
        }
        for (float bu = u - w / 2f + 0.03f; bu <= u + w / 2f; bu += 0.13f)
            FaceBox(b, face, hl, hw, bu - 0.012f, y, bu + 0.012f, y + 0.95f, deep - 0.04f, deep - 0.015f, Iron);
        foreach (float s in new[] { -1f, 1f })
            for (float d = 0.05f; d < deep; d += 0.13f)
                FaceBox(b, face, hl, hw, u + s * w / 2f - 0.012f, y, u + s * w / 2f + 0.012f, y + 0.95f, d, d + 0.025f, Iron);
        if (!flowers) return;
        for (int k = 0; k < 3; k++)
        {
            float pu = u - w / 2f + 0.3f + k * (w - 0.6f) / 2f;
            Pot(b, face, hl, hw, pu, y + 0.02f, deep * 0.55f, 0.13f, k % 2 == 0 ? Geranium : Flowers[(k + 1) % Flowers.Length]);
        }
    }

    // A clay pot with a plant: on face `face` at u, standing on y, d out from the wall
    static void Pot(TvShapeBuilder b, int face, float hl, float hw, float u, float y, float d, float r, TvColor bloom)
    {
        float depth = face < 2 ? hw : hl, sign = face == 0 || face == 2 ? -1f : 1f;
        float x = face < 2 ? u : sign * (depth + d), z = face < 2 ? sign * (depth + d) : u;
        b.Tube(r * 0.75f, r, r * 1.6f, y, 7, PotClay, null, 0f, 1f, x, z);
        b.Blob(r * 1.3f, 1f, 0.9f, 1f, x, y + r * 2.2f, z, 0.35f, 0, true, Leaf);
        for (int k = 0; k < 3; k++)
            b.Blob(r * 0.45f, 1f, 1f, 1f, x + (k - 1) * r * 0.6f, y + r * 2.9f + (k % 2) * r * 0.3f, z + (k % 2 - 0.5f) * r * 0.5f, 0.2f, 0, true, bloom);
    }

    // Two or three pots on the ground beside a door
    static void Pots(TvShapeBuilder b, int face, float hl, float hw, float u, float y, int look)
    {
        Pot(b, face, hl, hw, u, y - 0.25f, 0.3f, 0.2f, Geranium);
        Pot(b, face, hl, hw, u + 0.45f, y - 0.25f, 0.25f, 0.15f, Flowers[look % Flowers.Length]);
        if (look % 3 == 0) Pot(b, face, hl, hw, u + 0.2f, y - 0.25f, 0.62f, 0.26f, Leaf);
    }

    // Washing on a line out from a window: a few pieces of clothing in bright colours
    static void Washing(TvShapeBuilder b, int face, float hl, float hw, float u, float y)
    {
        FaceBox(b, face, hl, hw, u - 0.9f, y + 0.02f, u + 0.9f, y + 0.04f, 0.4f, 0.42f, White);
        for (int k = 0; k < 4; k++)
        {
            float pu = u - 0.75f + k * 0.42f, ph = 0.35f + 0.2f * ((k * 3) % 3);
            FaceBox(b, face, hl, hw, pu, y - ph, pu + 0.32f, y + 0.02f, 0.4f, 0.415f, Clothes[(k * 4 + (int)Math.Abs(u * 3f)) % Clothes.Length].Lerp(White, 0.15f));
        }
    }

    // An old lantern on a curly iron bracket out from the wall
    static void WallLantern(TvShapeBuilder b, int face, float hl, float hw, float u, float y)
    {
        FaceBox(b, face, hl, hw, u - 0.04f, y - 0.25f, u + 0.04f, y + 0.25f, 0f, 0.05f, Iron);
        FaceBox(b, face, hl, hw, u - 0.025f, y + 0.12f, u + 0.025f, y + 0.17f, 0.05f, 0.62f, Iron);
        FaceBox(b, face, hl, hw, u - 0.02f, y - 0.2f, u + 0.02f, y - 0.16f, 0.05f, 0.35f, Iron);
        FaceBox(b, face, hl, hw, u - 0.02f, y - 0.2f, u + 0.02f, y + 0.12f, 0.33f, 0.37f, Iron);
        FaceBox(b, face, hl, hw, u - 0.13f, y - 0.32f, u + 0.13f, y + 0.08f, 0.47f, 0.73f, Glow);
        FaceBox(b, face, hl, hw, u - 0.17f, y + 0.08f, u + 0.17f, y + 0.14f, 0.43f, 0.77f, Iron);
        FaceBox(b, face, hl, hw, u - 0.15f, y - 0.36f, u + 0.15f, y - 0.32f, 0.45f, 0.75f, Iron);
    }

    // Rows of round tiles down the roof: thin ridges from the eaves up towards the top, and a ridge cap
    static void TileRows(TvShapeBuilder b, float hl, float hw, float y, float rise, float over, TvColor tiles)
    {
        float l = hl + over, w = hw + over, top = y + rise, slope = rise / w;
        TvColor dark = tiles.Shade(0.8f);
        foreach (float sv in new[] { -1f, 1f })
            for (float x = -l + 0.3f; x < l - 0.2f; x += 0.42f)
            {
                // Along the slope from the eave to where the hip cuts it
                float reach = Math.Min(w, w - (Math.Abs(x) - (l - w)));
                if (reach < 0.3f) continue;
                float z0 = sv * w, z1 = sv * (w - reach);
                Beam(b, x, y + 0.08f, z0, x, y + 0.08f + reach * slope, z1, 0.13f, 0.08f, dark);
            }
        Beam(b, -(l - w), top + 0.06f, 0f, l - w, top + 0.06f, 0f, 0.2f, 0.12f, dark);
    }

    // A stone church of the old town: a whitewashed nave with granite corners, a front with a bell gable on top
    // (two arches with bells), a round window and a granite door, a tiled roof
    static TvShape StoneChurch(TvRandom random, float len, float wid, float plinth)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f, eaves = plinth + Math.Max(6.5f, wid * 0.6f), rise = hw * 0.5f;
        TvColor wall = Limewash[0];
        b.Box(-hl - 0.1f, -0.8f, -hw - 0.1f, hl + 0.1f, plinth, hw + 0.1f, GraniteDark);
        b.Box(-hl, plinth, -hw, hl, eaves, hw, wall);
        b.Box(-hl - 0.02f, plinth, -hw - 0.02f, hl + 0.02f, plinth + 1.0f, hw + 0.02f, Granite);
        foreach (float su in new[] { -1f, 1f })
            foreach (float sv in new[] { -1f, 1f })
                b.Box(su * hl - 0.6f, plinth, sv * hw - 0.6f, su * hl + 0.06f * su + (su < 0 ? 0.6f : 0f), eaves + 0.2f, sv * hw + 0.06f * sv + (sv < 0 ? 0.6f : 0f), Granite);
        // High small windows along the sides
        int n = Math.Max(2, (int)(len / 5f));
        for (int i = 0; i < n; i++)
        {
            float u = -hl + len * (i + 0.5f) / n;
            foreach (int face in new[] { 0, 1 })
            {
                FaceBox(b, face, hl, hw, u - 0.55f, eaves - 3.2f, u + 0.55f, eaves - 0.9f, -0.02f, 0.08f, Granite);
                FaceBox(b, face, hl, hw, u - 0.35f, eaves - 3.0f, u + 0.35f, eaves - 1.1f, -0.02f, 0.09f, Glass);
            }
        }
        // The front at -x: a gable wall up past the roof, the bell gable on top
        float gTop = eaves + rise + 0.4f;
        b.Triangle(-hl - 0.02f, eaves, -hw, -hl - 0.02f, eaves, hw, -hl - 0.02f, gTop, 0f, 0f, eaves, 0f, wall);
        b.Triangle(hl + 0.02f, eaves, -hw, hl + 0.02f, eaves, hw, hl + 0.02f, eaves + rise, 0f, 0f, eaves, 0f, wall);
        float bw = Math.Min(hw * 0.8f, 3.2f), bh = 3.6f;
        b.Box(-hl - 0.05f, eaves, -bw, -hl + 0.7f, gTop + bh, bw, wall);   // the front wall carried up through the roof
        b.Box(-hl - 0.12f, gTop + bh, -bw - 0.15f, -hl + 0.77f, gTop + bh + 0.3f, bw + 0.15f, Granite);
        b.Triangle(-hl - 0.05f, gTop + bh + 0.3f, -bw, -hl - 0.05f, gTop + bh + 0.3f, bw, -hl - 0.05f, gTop + bh + 1.4f, 0f, 0f, gTop + bh, 0f, wall);
        b.Triangle(-hl + 0.7f, gTop + bh + 0.3f, -bw, -hl + 0.7f, gTop + bh + 0.3f, bw, -hl + 0.7f, gTop + bh + 1.4f, 0f, 0f, gTop + bh, 0f, wall);
        Beam(b, -hl + 0.33f, gTop + bh + 1.4f, 0f, -hl + 0.33f, gTop + bh + 2.4f, 0f, 0.08f, 0.08f, Iron);
        Beam(b, -hl + 0.33f, gTop + bh + 2.05f, -0.35f, -hl + 0.33f, gTop + bh + 2.05f, 0.35f, 0.08f, 0.08f, Iron);
        foreach (float s in new[] { -1f, 1f })
        {
            float z = s * bw * 0.45f;
            b.Box(-hl - 0.08f, gTop + 0.6f, z - 0.6f, -hl + 0.73f, gTop + 2.6f, z + 0.6f, TvColor.Hex("#1e2226"));
            b.Cone(0.45f, 0.75f, gTop + 1.2f, 8, TvColor.Hex("#8a6a2e"), -hl + 0.33f, z);
            b.Box(-hl + 0.31f, gTop + 1.95f, z - 0.03f, -hl + 0.35f, gTop + 2.6f, z + 0.03f, Iron);
        }
        // Round window and the door with its granite frame and steps
        b.Box(-hl - 0.1f, eaves - 1.6f, -1.1f, -hl, eaves + 0.6f, 1.1f, Granite);
        b.Box(-hl - 0.12f, eaves - 1.35f, -0.85f, -hl, eaves + 0.35f, 0.85f, Glass);
        b.Box(-hl - 0.14f, plinth, -1.7f, -hl, plinth + 4.0f, 1.7f, Granite);
        b.Box(-hl - 0.16f, plinth, -1.2f, -hl, plinth + 3.4f, 1.2f, OldWood[1]);
        for (int s = 0; s < 3; s++) b.Box(-hl - 1.6f + s * 0.4f, plinth - 0.8f + s * 0.27f, -2.6f, -hl, plinth - 0.53f + s * 0.27f, 2.6f, Granite.Shade(0.95f - s * 0.03f));
        // The roof
        var roofB = new TvShapeBuilder(random);
        GableRoof(roofB, hl, hw, eaves, rise, 0.4f, Terracotta, wall);
        Append(b, roofB.Done(), 0f, 0f);
        return b.Done();
    }

    // ---- Things in the old town ----

    // A rough stone wall, 2 m long, round its middle along x (gardens, terraces)
    public static TvShape StoneWall(TvRandom random, float height = 1.4f)
    {
        var b = new TvShapeBuilder(random);
        b.Box(-1f, 0f, -0.28f, 1f, height, 0.28f, Rubble);
        for (int i = 0; i < 14; i++)
        {
            float x = -0.95f + 1.9f * random.Value(), y = height * random.Value(), w = 0.25f + 0.25f * random.Value(), h = 0.15f + 0.15f * random.Value();
            TvColor c = Rubble.Lerp(random.Value() < 0.5f ? TvColor.Hex("#c2b8a6") : TvColor.Hex("#6c6458"), 0.2f + 0.5f * random.Value());
            float zs = random.Value() < 0.5f ? -1f : 1f;
            b.Box(x - w / 2f, y, zs * 0.28f - 0.02f, x + w / 2f, Math.Min(height, y + h), zs * 0.31f, c);
        }
        b.Box(-1.02f, height, -0.32f, 1.02f, height + 0.12f, 0.32f, Granite);
        // Moss and grass on top here and there
        b.Blob(0.18f, 1.6f, 0.5f, 1f, -0.4f + random.Value(), height + 0.13f, 0f, 0.3f, 0, true, TvColor.Hex("#6f8a3e"));
        return b.Done();
    }

    // An orange tree: a short trunk, a dark round crown with oranges
    public static TvShape OrangeTree(TvRandom random, int look)
    {
        var b = new TvShapeBuilder(random);
        float h = 1.4f + 0.3f * (look % 3), r = 1.3f + 0.2f * (look % 2);
        b.Tube(0.12f, 0.09f, h, 0f, 6, TvColor.Hex("#5a4636"));
        b.Blob(r, 1f, 0.9f, 1f, 0f, h + r * 0.7f, 0f, 0.25f, 1, true, TvColor.Hex("#2f5a2c"), TvColor.Hex("#4f7a3a"), h, h + r * 1.6f);
        for (int i = 0; i < 14; i++)
        {
            float a = i * 2.4f, e = 0.3f + 0.6f * ((i * 7) % 10) / 10f;
            float x = (float)Math.Cos(a) * r * 0.95f * (float)Math.Cos(e), z = (float)Math.Sin(a) * r * 0.95f * (float)Math.Cos(e);
            b.Blob(0.08f, 1f, 1f, 1f, x, h + r * 0.7f + (float)Math.Sin(e) * r * 0.6f - 0.2f, z, 0f, 0, false, TvColor.Hex("#f08a1c"));
        }
        return b.Done();
    }

    // Washing on a line across a lane, len long, along x round the middle (hung at the height it is placed)
    public static TvShape WashingLine(TvRandom random, float len, int look)
    {
        var b = new TvShapeBuilder(random);
        Beam(b, -len / 2f, 0f, 0f, len / 2f, 0f, 0f, 0.02f, 0.02f, White);
        int n = (int)(len / 0.55f);
        for (int k = 0; k < n; k++)
        {
            if ((k + look) % 4 == 3) continue;
            float x = -len / 2f + 0.3f + k * 0.55f, h = 0.35f + 0.25f * ((k * 5 + look) % 3) / 2f;
            TvColor c = Clothes[(k * 3 + look) % Clothes.Length].Lerp(White, 0.15f);
            b.Box(x, -h, -0.01f, x + 0.4f, 0f, 0.01f, c);
        }
        return b.Done();
    }

    // Stone steps up a slope: steps of 0.16 m, going up towards -z, w wide
    public static TvShape StoneSteps(TvRandom random, int steps, float w)
    {
        var b = new TvShapeBuilder(random);
        for (int s = 0; s < steps; s++)
            b.Box(-w / 2f, -0.6f, -s * 0.45f - 0.45f, w / 2f, 0.16f * (s + 1), -s * 0.45f, Granite.Lerp(GraniteDark, (s * 37 % 10) / 25f));
        return b.Done();
    }

    // ---- Far away: the same houses in a few boxes (the run-yourself town draws these past the near distance) ----

    // A house as a box with its roof and a dark band of windows per floor, in about the colours of the full one
    public static TvShape Lod(TvRandom random, Kind kind, float len, float wid, int floors, int look, Style style, float plinth)
    {
        var b = new TvShapeBuilder(random);
        float hl = len / 2f, hw = wid / 2f, top, rise;
        TvColor wall, roof;
        bool flat = false;
        if (style == Style.OldStone)
        {
            bool bare = kind != Kind.TownHall && look % 7 == 5;
            wall = kind == Kind.Church ? Limewash[0] : bare ? Rubble : Limewash[look % Limewash.Length];
            roof = RoofTiles[(look * 3 + 1) % RoofTiles.Length];
            top = plinth + (kind == Kind.TownHall ? 3.6f * 3 : 2.9f * Math.Max(1, Math.Min(floors, 4))) + 0.25f;
            rise = Math.Min(hw * 0.42f, 2.3f);
        }
        else
        {
            switch (kind)
            {
                case Kind.Apartments:
                    wall = new[] { TvColor.Hex("#9a5a42"), TvColor.Hex("#e2d6b8"), TvColor.Hex("#c7a06a"), TvColor.Hex("#b9cbd2"), TvColor.Hex("#e7c26a"), TvColor.Hex("#c98d78") }[look % 6];
                    roof = TvColor.Hex("#5a5c5c"); top = plinth + 2.9f * floors + 0.3f; rise = 0f; flat = true; break;
                case Kind.Villa:
                    wall = new[] { TvColor.Hex("#8f3426"), TvColor.Hex("#e0c27a"), TvColor.Hex("#dfe3e0"), TvColor.Hex("#9cc0d8"), TvColor.Hex("#a9c49a"), TvColor.Hex("#e9d9a6") }[look % 6];
                    roof = Roofs[(look * 7 + 1) % Roofs.Length]; top = plinth + (len > 11f ? 5.2f : 2.7f); rise = hw * 0.75f; break;
                case Kind.Hall:
                    wall = look % 2 == 0 ? TvColor.Hex("#c9c6bc") : TvColor.Hex("#b45a3c"); roof = TvColor.Hex("#4e5254"); top = plinth + 7f; rise = 0f; flat = true; break;
                default:
                    wall = Facades[look % Facades.Length]; roof = Roofs[(look * 11 + 1) % Roofs.Length];
                    top = plinth + 3.6f + 3.1f * (Math.Max(1, floors) - 1); rise = Math.Min(hw * 0.75f, 4.5f); break;
            }
        }
        b.Box(-hl, -0.6f, -hw, hl, top, hw, wall);
        int bands = Math.Max(1, (int)((top - plinth) / 3f));
        for (int f = 0; f < bands; f++)
        {
            float y = plinth + 0.9f + f * (top - plinth) / bands;
            b.Box(-hl + 0.5f, y, -hw - 0.04f, hl - 0.5f, y + 1.2f, hw + 0.04f, wall.Lerp(Glass, 0.6f));
        }
        if (flat) b.Box(-hl - 0.1f, top, -hw - 0.1f, hl + 0.1f, top + 0.4f, hw + 0.1f, roof);
        else HipRoof(b, hl, hw, top, Math.Max(0.5f, rise), 0.3f, roof);
        return b.Done();
    }

    // A few clay pots with flowers, as they stand by doors and on steps in the old town
    public static TvShape PotCluster(TvRandom random, int look)
    {
        var b = new TvShapeBuilder(random);
        Pot(b, 0, 0f, 0f, 0f, 0f, 0f, 0.22f, Geranium);
        Pot(b, 0, 0f, 0f, 0.45f, 0f, 0.1f, 0.16f, Flowers[look % Flowers.Length]);
        if (look % 2 == 0) Pot(b, 0, 0f, 0f, 0.2f, 0f, 0.5f, 0.28f, Leaf);
        return b.Done();
    }
}
