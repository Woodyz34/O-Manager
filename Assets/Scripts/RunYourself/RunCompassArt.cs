using UnityEngine;

// The pictures of the thumb compass, drawn in code once (2026-11-19, Anders: the old compass of 3D blocks was hard to
// read; he wanted it as clear as a real thumb compass). Smooth edges by coverage, 4 pixels per millimetre.
public static class RunCompassArt
{
    const int Ppm = 4;
    static Texture2D plate, capsule, needle;

    // A thumb compass as orienteers use it (2026-11-20, Anders' photo): a clear wedge-shaped plate, a round capsule
    // in a silver ring at the front corner, a black elastic strap round the thumb behind the capsule, orange lines
    // along the edges and a red bar on the front edge. Made for the left thumb (the right one is the mirror image).
    // The picture covers x -20..36 mm and y -4..60 mm round the thumb; the front is up.
    public const float PlateW = 56f, PlateH = 64f, OriginX = 20f, OriginY = 4f;
    public const float CapsuleX = -2f, CapsuleY = 36f, CapsuleD = 32f, StrapFrom = 7f, StrapTo = 18f;
    static readonly Vector2[] Outline = { new Vector2(-18f, 58f), new Vector2(15f, 58f), new Vector2(34f, 13f), new Vector2(8f, -2f), new Vector2(-18f, -2f) };

    public static Texture2D Baseplate()
    {
        if (plate != null) return plate;
        int w = (int)PlateW * Ppm, h = (int)PlateH * Ppm;
        Color[] px = Blank(w, h);
        Color tint = new Color(0.88f, 0.94f, 0.96f, 0.3f), rim = new Color(0.55f, 0.62f, 0.66f, 0.85f), shine = new Color(1f, 1f, 1f, 0.55f);
        Color orange = new Color(0.93f, 0.4f, 0.1f, 1f), red = new Color(0.86f, 0.1f, 0.08f, 1f), black = new Color(0.05f, 0.05f, 0.06f, 1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 mm = new Vector2((x + 0.5f) / Ppm - OriginX, (y + 0.5f) / Ppm - OriginY);
                float d = PlateDistance(mm) * Ppm;           // < 0 inside, in pixels
                float inside = Mathf.Clamp01(0.5f - d);
                if (inside <= 0f) continue;
                Over(px, w, x, y, tint, inside);
                // A cut edge of clear plastic: a darker rim with a bright line just inside it
                Over(px, w, x, y, rim, Mathf.Clamp01(0.6f * Ppm - Mathf.Abs(d + 0.6f * Ppm)) * inside);
                Over(px, w, x, y, shine, Mathf.Clamp01(0.35f * Ppm - Mathf.Abs(d + 1.5f * Ppm)) * inside);
            }
        // Orange lines along the straight back edge and the slanted side, as on the photo
        Line(px, w, h, new Vector2(-15.8f, 1f), new Vector2(-15.8f, 55.5f), 0.35f, orange);
        Line(px, w, h, new Vector2(14.2f, 55.6f), new Vector2(31.6f, 13.8f), 0.35f, orange);
        Line(px, w, h, new Vector2(9f, 0.4f), new Vector2(31.2f, 12.6f), 0.3f, orange);
        // The red bar on the front edge, and a red tip on the far corner
        Line(px, w, h, new Vector2(-17.2f, 56.9f), new Vector2(-6f, 56.9f), 0.8f, red);
        Line(px, w, h, new Vector2(-17.2f, 56.9f), new Vector2(-15.5f, 56.9f), 0.85f, black);
        Line(px, w, h, new Vector2(29.5f, 16.4f), new Vector2(32.8f, 13.6f), 0.7f, red);
        // A few dots of grip on the plate, where the thumb's tip lies
        for (int i = 0; i < 14; i++)
        {
            Vector2 c = new Vector2(16f + (i % 4) * 2.2f + (i / 4 % 2) * 1.1f, 24f + (i / 4) * 2f);
            Dot(px, w, h, c, 0.45f, new Color(1f, 1f, 1f, 0.35f));
        }
        // The black elastic strap across, ribbed, with a clear buckle at the side
        Color strap = new Color(0.1f, 0.1f, 0.11f, 0.97f), rib = new Color(0.24f, 0.25f, 0.27f, 0.9f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 mm = new Vector2((x + 0.5f) / Ppm - OriginX, (y + 0.5f) / Ppm - OriginY);
                if (mm.x < -19.5f || mm.x > 19f) continue;
                float edge = Mathf.Clamp01(Mathf.Min(mm.y - StrapFrom, StrapTo - mm.y) * Ppm + 0.5f);
                if (edge <= 0f) continue;
                float side = Mathf.Clamp01(Mathf.Min(mm.x + 19.5f, 19f - mm.x) * Ppm + 0.5f);
                Over(px, w, x, y, strap, edge * side);
                float ribbed = Mathf.Abs(Mathf.Sin(mm.x * 2.6f + mm.y * 0.4f));
                Over(px, w, x, y, rib, edge * side * Mathf.Clamp01(1.2f - ribbed * 2f) * 0.5f);
            }
        RectOutline(px, w, h, new Vector2(17.5f, StrapFrom - 1.2f), new Vector2(23.5f, StrapTo + 1.2f), 0.8f, new Color(0.75f, 0.8f, 0.82f, 0.9f));
        return plate = Make("Thumb compass plate", w, h, px);
    }

    // Distance (mm) to the plate's outline, negative inside (the outline is convex)
    static float PlateDistance(Vector2 p)
    {
        float d = float.MinValue;
        for (int i = 0; i < Outline.Length; i++)
        {
            Vector2 a = Outline[i], b = Outline[(i + 1) % Outline.Length];
            Vector2 e = (b - a).normalized;
            Vector2 n = new Vector2(-e.y, e.x);              // outwards: the outline runs clockwise
            d = Mathf.Max(d, Vector2.Dot(p - a, n));
        }
        return d;
    }

    // The capsule: clear liquid in a thick silver ring, a thin black north-south line and east-west line
    public static Texture2D Capsule()
    {
        if (capsule != null) return capsule;
        int s = (int)CapsuleD * Ppm;
        Color[] px = Blank(s, s);
        float c = s * 0.5f, rOut = s * 0.5f - 1f, ring = 2.6f * Ppm;
        Color liquid = new Color(0.92f, 0.97f, 0.98f, 0.4f), line = new Color(0.08f, 0.08f, 0.09f, 0.8f);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float inside = Mathf.Clamp01(rOut - d + 0.5f);
                if (inside <= 0f) continue;
                Over(px, s, x, y, liquid, inside);
                if (d < rOut - ring)
                {
                    Over(px, s, x, y, line, Mathf.Clamp01(0.18f * Ppm - Mathf.Abs(dx) + 0.5f));
                    Over(px, s, x, y, line * new Color(1, 1, 1, 0.6f), Mathf.Clamp01(0.14f * Ppm - Mathf.Abs(dy) + 0.5f));
                }
                // The silver ring: lit from the upper left, a dark line where it meets the liquid
                float inRing = Mathf.Clamp01(d - (rOut - ring) + 0.5f) * inside;
                if (inRing > 0f)
                {
                    float light = 0.5f + 0.5f * Vector2.Dot(new Vector2(dx, dy) / Mathf.Max(1f, d), new Vector2(-0.6f, 0.8f));
                    float band = Mathf.Sin((d - (rOut - ring)) / ring * Mathf.PI);
                    Color metal = Color.Lerp(new Color(0.55f, 0.58f, 0.6f, 1f), new Color(0.95f, 0.96f, 0.97f, 1f), light * 0.7f + band * 0.3f);
                    Over(px, s, x, y, metal, inRing);
                    Over(px, s, x, y, new Color(0.2f, 0.22f, 0.24f, 0.8f), Mathf.Clamp01(0.3f * Ppm - Mathf.Abs(d - (rOut - ring))) * inside);
                }
            }
        return capsule = Make("Compass capsule", s, s, px);
    }

    // The needle of a thumb compass: a broad red north half with a white stripe down the middle, a white south half
    // with a black edge, a small hub
    public static Texture2D Needle()
    {
        if (needle != null) return needle;
        int w = 7 * Ppm, h = 28 * Ppm;
        Color[] px = Blank(w, h);
        float cx = w * 0.5f, cy = h * 0.5f;
        Color red = new Color(0.9f, 0.1f, 0.08f, 1f), white = new Color(0.98f, 0.98f, 0.96f, 1f), dark = new Color(0.06f, 0.06f, 0.07f, 1f);
        Rect(px, w, h, cx - 2.6f * Ppm, cy, cx + 2.6f * Ppm, h - 1.5f * Ppm, red);
        Triangle(px, w, h, new Vector2(cx, h - 0.2f), new Vector2(cx - 2.6f * Ppm, h - 1.6f * Ppm), new Vector2(cx + 2.6f * Ppm, h - 1.6f * Ppm), red);
        Rect(px, w, h, cx - 0.45f * Ppm, cy + 1.5f * Ppm, cx + 0.45f * Ppm, h - 2.5f * Ppm, white);
        Rect(px, w, h, cx - 2.6f * Ppm, 1f * Ppm, cx + 2.6f * Ppm, cy, dark);
        Rect(px, w, h, cx - 2.0f * Ppm, 1.6f * Ppm, cx + 2.0f * Ppm, cy - 0.3f * Ppm, white);
        Dot(px, w, h, new Vector2(cx / Ppm, cy / Ppm), 1.3f, new Color(0.75f, 0.75f, 0.72f, 1f), true);
        return needle = Make("Compass needle", w, h, px);
    }

    // A straight line between two points given in millimetres of the plate picture, half-width in mm
    static void Line(Color[] px, int w, int h, Vector2 a, Vector2 b, float half, Color col)
    {
        Vector2 pa = (a + new Vector2(OriginX, OriginY)) * Ppm, pb = (b + new Vector2(OriginX, OriginY)) * Ppm;
        Vector2 e = pb - pa;
        float len2 = Mathf.Max(1e-5f, e.sqrMagnitude), r = half * Ppm;
        int x0 = Mathf.Max(0, (int)(Mathf.Min(pa.x, pb.x) - r - 2)), x1 = Mathf.Min(w - 1, (int)(Mathf.Max(pa.x, pb.x) + r + 2));
        int y0 = Mathf.Max(0, (int)(Mathf.Min(pa.y, pb.y) - r - 2)), y1 = Mathf.Min(h - 1, (int)(Mathf.Max(pa.y, pb.y) + r + 2));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - pa, e) / len2);
                float d = Vector2.Distance(p, pa + e * t);
                Over(px, w, x, y, col, Mathf.Clamp01(r - d + 0.5f));
            }
    }

    // A round dot; pixels: the centre is in pixels / Ppm (millimetres from the picture's corner) instead of plate mm
    static void Dot(Color[] px, int w, int h, Vector2 c, float radius, Color col, bool fromCorner = false)
    {
        Vector2 pc = (fromCorner ? c : c + new Vector2(OriginX, OriginY)) * Ppm;
        float r = radius * Ppm;
        for (int y = Mathf.Max(0, (int)(pc.y - r - 2)); y < Mathf.Min(h, (int)(pc.y + r + 2)); y++)
            for (int x = Mathf.Max(0, (int)(pc.x - r - 2)); x < Mathf.Min(w, (int)(pc.x + r + 2)); x++)
                Over(px, w, x, y, col, Mathf.Clamp01(r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), pc) + 0.5f));
    }

    static void RectOutline(Color[] px, int w, int h, Vector2 a, Vector2 b, float half, Color col)
    {
        Line(px, w, h, a, new Vector2(b.x, a.y), half, col);
        Line(px, w, h, new Vector2(b.x, a.y), b, half, col);
        Line(px, w, h, b, new Vector2(a.x, b.y), half, col);
        Line(px, w, h, new Vector2(a.x, b.y), a, half, col);
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
