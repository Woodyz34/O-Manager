using UnityEngine;

// The pictures of the thumb compass, drawn in code once (2026-11-19, Anders: the old compass of 3D blocks was hard to
// read; he wanted it as clear as a real thumb compass). Smooth edges by coverage, 4 pixels per millimetre.
public static class RunCompassArt
{
    const int Ppm = 4;
    static Texture2D plate, capsule, needle;

    // The clear baseplate: rounded, a faint tint, a dark rim, a red direction arrow at the top, millimetre scale
    // marks along the inner edge and a few lines along the direction of travel
    public static Texture2D Baseplate()
    {
        if (plate != null) return plate;
        int w = 58 * Ppm, h = 92 * Ppm;
        Color[] px = Blank(w, h);
        Color tint = new Color(0.86f, 0.93f, 0.95f, 0.28f), rim = new Color(0.12f, 0.16f, 0.18f, 0.9f);
        Color red = new Color(0.86f, 0.12f, 0.1f, 1f), mark = new Color(0.08f, 0.1f, 0.12f, 0.95f);
        float r = 7f * Ppm;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = RoundRect(x + 0.5f, y + 0.5f, w, h, r);   // < 0 inside
                float inside = Mathf.Clamp01(0.5f - d);
                if (inside <= 0f) continue;
                float edge = Mathf.Clamp01(1.5f * Ppm * 0.5f - Mathf.Abs(d + 1.2f * Ppm)) ;
                Over(px, w, x, y, tint, inside);
                Over(px, w, x, y, rim, Mathf.Clamp01(edge) * inside);
            }
        // Direction arrow at the top
        float cx = w * 0.5f, top = h - 6f * Ppm;
        Triangle(px, w, h, new Vector2(cx, top), new Vector2(cx - 7f * Ppm, top - 13f * Ppm), new Vector2(cx + 7f * Ppm, top - 13f * Ppm), red);
        Rect(px, w, h, cx - 1.6f * Ppm, top - 30f * Ppm, cx + 1.6f * Ppm, top - 12f * Ppm, red);
        // Lines along the direction of travel either side of the capsule
        for (int s = -1; s <= 1; s += 2)
            Rect(px, w, h, cx + s * 22f * Ppm - 0.3f * Ppm, 8f * Ppm, cx + s * 22f * Ppm + 0.3f * Ppm, h - 24f * Ppm, mark * new Color(1, 1, 1, 0.55f));
        // A millimetre scale on the outer long edge (the side away from the thumb is chosen by the hand's mirror)
        for (int mm = 0; mm <= 70; mm++)
        {
            float y = 10f * Ppm + mm * Ppm, len = (mm % 10 == 0 ? 5f : mm % 5 == 0 ? 3.5f : 2f) * Ppm;
            Rect(px, w, h, 2.6f * Ppm, y - 0.18f * Ppm, 2.6f * Ppm + len, y + 0.18f * Ppm, mark);
            Rect(px, w, h, w - 2.6f * Ppm - len, y - 0.18f * Ppm, w - 2.6f * Ppm, y + 0.18f * Ppm, mark);
        }
        return plate = Make("Compass plate", w, h, px);
    }

    // The capsule: clear liquid in a black ring, with north-south lines to line up with the map's north lines
    public static Texture2D Capsule()
    {
        if (capsule != null) return capsule;
        int s = 50 * Ppm;
        Color[] px = Blank(s, s);
        float c = s * 0.5f, rOut = s * 0.5f - 1f, ring = 2.6f * Ppm;
        Color liquid = new Color(0.93f, 0.97f, 0.97f, 0.5f), black = new Color(0.07f, 0.07f, 0.08f, 1f);
        Color line = new Color(0.85f, 0.15f, 0.12f, 0.7f), tick = new Color(0.07f, 0.07f, 0.08f, 0.9f);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float inside = Mathf.Clamp01(rOut - d + 0.5f);
                if (inside <= 0f) continue;
                Over(px, s, x, y, liquid, inside);
                Over(px, s, x, y, black, Mathf.Clamp01(d - (rOut - ring) + 0.5f) * inside);
                // Ticks every 30 degrees just inside the ring
                if (d > rOut - ring - 3f * Ppm && d < rOut - ring)
                {
                    float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float off = Mathf.Abs(Mathf.DeltaAngle(a, Mathf.Round(a / 30f) * 30f)) * Mathf.Deg2Rad * d;
                    Over(px, s, x, y, tick, Mathf.Clamp01(0.45f * Ppm - off + 0.5f));
                }
                // Two red north lines
                for (int k = -1; k <= 1; k += 2)
                    if (d < rOut - ring) Over(px, s, x, y, line, Mathf.Clamp01(0.3f * Ppm - Mathf.Abs(dx - k * 9f * Ppm) + 0.5f));
            }
        return capsule = Make("Compass capsule", s, s, px);
    }

    // The needle: red north half, white south half with a dark outline, a dark hub
    public static Texture2D Needle()
    {
        if (needle != null) return needle;
        int w = 9 * Ppm, h = 46 * Ppm;
        Color[] px = Blank(w, h);
        float cx = w * 0.5f, cy = h * 0.5f, half = w * 0.5f - 1f;
        Color red = new Color(0.9f, 0.1f, 0.08f, 1f), white = new Color(0.98f, 0.98f, 0.96f, 1f), dark = new Color(0.06f, 0.06f, 0.07f, 1f);
        Vector2 top = new Vector2(cx, h - 1f), bottom = new Vector2(cx, 1f), l = new Vector2(cx - half, cy), r = new Vector2(cx + half, cy);
        Triangle(px, w, h, top, l, r, dark, 1.1f);
        Triangle(px, w, h, bottom, r, l, dark, 1.1f);
        Triangle(px, w, h, top, l, r, red, -0.6f * Ppm);
        Triangle(px, w, h, bottom, r, l, white, -0.6f * Ppm);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                Over(px, w, x, y, dark, Mathf.Clamp01(2.2f * Ppm - d + 0.5f));
                Over(px, w, x, y, new Color(0.75f, 0.75f, 0.72f, 1f), Mathf.Clamp01(1.0f * Ppm - d + 0.5f));
            }
        return needle = Make("Compass needle", w, h, px);
    }

    // ---- Drawing helpers ----

    static Color[] Blank(int w, int h)
    {
        Color[] px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, 0f);
        return px;
    }

    static Texture2D Make(string name, int w, int h, Color[] px)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        t.SetPixels(px);
        t.Apply(true, true);
        return t;
    }

    // Source-over blend of a colour with the given coverage
    static void Over(Color[] px, int w, int x, int y, Color c, float cover)
    {
        float a = c.a * cover;
        if (a <= 0f) return;
        Color d = px[y * w + x];
        float outA = a + d.a * (1f - a);
        if (outA <= 1e-5f) return;
        Color o = (c * a + d * d.a * (1f - a)) / outA;
        o.a = outA;
        px[y * w + x] = o;
    }

    static float RoundRect(float x, float y, float w, float h, float r)
    {
        float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r), qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static void Rect(Color[] px, int w, int h, float x0, float y0, float x1, float y1, Color c)
    {
        for (int y = Mathf.Max(0, (int)y0 - 1); y < Mathf.Min(h, (int)y1 + 2); y++)
            for (int x = Mathf.Max(0, (int)x0 - 1); x < Mathf.Min(w, (int)x1 + 2); x++)
            {
                float cx = Mathf.Clamp01(Mathf.Min(x + 1f, x1) - Mathf.Max(x, x0));
                float cy = Mathf.Clamp01(Mathf.Min(y + 1f, y1) - Mathf.Max(y, y0));
                Over(px, w, x, y, c, cx * cy);
            }
    }

    // A filled triangle (counter-clockwise or not), grown by `grow` pixels, with smooth edges
    static void Triangle(Color[] px, int w, int h, Vector2 a, Vector2 b, Vector2 c, Color col, float grow = 0f)
    {
        float sign = Mathf.Sign((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(Edge(a, b, p, sign), Mathf.Min(Edge(b, c, p, sign), Edge(c, a, p, sign))) + grow;
                Over(px, w, x, y, col, Mathf.Clamp01(d + 0.5f));
            }
    }

    static float Edge(Vector2 a, Vector2 b, Vector2 p, float sign)
    {
        Vector2 e = b - a;
        return sign * (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / Mathf.Max(1e-5f, e.magnitude);
    }
}
