using UnityEngine;

// The pictures of the thumb compass, drawn in code once (2026-11-19, Anders: the old compass of 3D blocks was hard to
// read; he wanted it as clear as a real thumb compass). Smooth edges by coverage, 4 pixels per millimetre.
public static class RunCompassArt
{
    const int Ppm = 4;
    static Texture2D plate, capsule, needle;

    // The plate of a thumb compass (2026-11-19, Anders: it looked like a baseplate compass, not a thumb compass):
    // clear and a little tinted, square at the back where the thumb goes, narrowing to a rounded point in front with
    // a red direction arrow and a dark rubber strap across the back that goes round the thumb. 36 x 62 mm; the front is up.
    public const float PlateW = 36f, PlateH = 62f, StrapFrom = 5f, StrapTo = 13f, CapsuleY = 29f, CapsuleD = 30f, TaperFrom = 32f;

    public static Texture2D Baseplate()
    {
        if (plate != null) return plate;
        int w = (int)PlateW * Ppm, h = (int)PlateH * Ppm;
        Color[] px = Blank(w, h);
        Color tint = new Color(0.86f, 0.93f, 0.95f, 0.3f), rim = new Color(0.12f, 0.16f, 0.18f, 0.9f);
        Color red = new Color(0.86f, 0.12f, 0.1f, 1f);
        Color strap = new Color(0.16f, 0.17f, 0.19f, 0.96f), strapLight = new Color(0.36f, 0.38f, 0.41f, 0.95f);
        float cx = w * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = ThumbPlate(x + 0.5f, y + 0.5f);   // < 0 inside, in pixels
                float inside = Mathf.Clamp01(0.5f - d);
                if (inside <= 0f) continue;
                Over(px, w, x, y, tint, inside);
                Over(px, w, x, y, rim, Mathf.Clamp01(0.75f * Ppm - Mathf.Abs(d + 0.9f * Ppm)) * inside);
                // The rubber strap across the back, with a lighter ridge along it
                float sy = (y + 0.5f) / Ppm;
                if (sy > StrapFrom && sy < StrapTo)
                {
                    float edge = Mathf.Clamp01(Mathf.Min(sy - StrapFrom, StrapTo - sy) * Ppm * 0.5f);
                    Over(px, w, x, y, strap, edge * inside);
                    float ridge = 1f - Mathf.Abs(sy - (StrapFrom + StrapTo) * 0.5f) / 1.2f;
                    if (ridge > 0f) Over(px, w, x, y, strapLight, ridge * 0.6f * inside);
                }
            }
        // The red direction arrow at the front point, and the direction line from the capsule up to it
        float tipY = h - 3f * Ppm;
        Triangle(px, w, h, new Vector2(cx, tipY), new Vector2(cx - 3.2f * Ppm, tipY - 8f * Ppm), new Vector2(cx + 3.2f * Ppm, tipY - 8f * Ppm), red);
        Rect(px, w, h, cx - 0.5f * Ppm, (CapsuleY + CapsuleD * 0.5f + 0.5f) * Ppm, cx + 0.5f * Ppm, tipY - 7.5f * Ppm, red);
        return plate = Make("Thumb compass plate", w, h, px);
    }

    // The outline of the thumb plate as a distance (pixels, negative inside): straight sides at the back with round
    // corners, then narrowing to a blunt point in front
    static float ThumbPlate(float x, float y)
    {
        float w = PlateW * Ppm, h = PlateH * Ppm, mx = Mathf.Abs(x - w * 0.5f);
        float straightTo = TaperFrom * Ppm, tip = 2.5f * Ppm, corner = 4f * Ppm;
        float d = mx - w * 0.5f;
        if (y > straightTo)
        {
            // The slanted side: from the full width down to the blunt tip, measured square to the side
            float run = h - straightTo, drop = w * 0.5f - tip;
            float half = w * 0.5f - drop * (y - straightTo) / run;
            d = (mx - half) * run / Mathf.Sqrt(run * run + drop * drop);
        }
        d = Mathf.Max(d, y - h);
        // The back edge and its round corners
        d = Mathf.Max(d, -y);
        if (y < corner && mx > w * 0.5f - corner)
        {
            float qx = mx - (w * 0.5f - corner), qy = corner - y;
            d = Mathf.Sqrt(qx * qx + qy * qy) - corner;
        }
        return d;
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
