using UnityEngine;

// The pictures of the thumb compass, drawn in code once (2026-11-19, Anders: the old compass of 3D blocks was hard to
// read; he wanted it as clear as a real thumb compass). Smooth edges by coverage, 4 pixels per millimetre.
public static class RunCompassArt
{
    const int Ppm = 4;
    static Texture2D plate, capsule, needle;

    // A thumb compass as orienteers use it (2026-11-20, Anders' pictures of a real one, seen from above): a clear plate
    // with one long straight edge (a black line, an orange line beside it) from the front tip to the back tip, a wing
    // at the front with two dotted grip circles and an orange-and-black stripe on its edge, a lobe at the side with
    // X-shaped slots where the black strap goes round the thumb, and the big capsule in a silver ring beside the edge
    // at the back, with a small tab beside it with another orange-and-black stripe. Made for the left thumb (the right
    // one is the mirror image); millimetres round the thumb under the strap, the front up.
    public const float PlateW = 63f, PlateH = 72f, OriginX = 13f, OriginY = 34f;
    public const float CapsuleX = 30.5f, CapsuleY = -9f, CapsuleD = 33f, EdgeX = 25.7f;
    static readonly Vector2[] Outline =
    {
        new Vector2(23f, 35.8f), new Vector2(25.5f, 36.2f), new Vector2(25.7f, 8f), new Vector2(43f, 6.5f), new Vector2(47.5f, 5.5f),
        new Vector2(47.5f, -5.5f), new Vector2(44.5f, -19f), new Vector2(32.5f, -28f), new Vector2(25.7f, -32.2f), new Vector2(21.5f, -27f),
        new Vector2(11f, -13.5f), new Vector2(-7.5f, -5.5f), new Vector2(-11f, 2.5f), new Vector2(-1f, 13.8f), new Vector2(-1f, 30.5f)
    };

    public static Texture2D Baseplate()
    {
        if (plate != null) return plate;
        int w = (int)PlateW * Ppm, h = (int)PlateH * Ppm;
        Color[] px = Blank(w, h);
        Color tint = new Color(0.9f, 0.95f, 0.97f, 0.34f), rim = new Color(0.62f, 0.68f, 0.72f, 0.9f), shine = new Color(1f, 1f, 1f, 0.6f);
        Color orange = new Color(0.97f, 0.45f, 0.08f, 1f), black = new Color(0.05f, 0.05f, 0.06f, 1f), frost = new Color(1f, 1f, 1f, 0.55f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 mm = new Vector2((x + 0.5f) / Ppm - OriginX, (y + 0.5f) / Ppm - OriginY);
                float d = PlateDistance(mm) * Ppm;           // < 0 inside, in pixels
                float inside = Mathf.Clamp01(0.5f - d);
                if (inside <= 0f) continue;
                Over(px, w, x, y, tint, inside);
                // A cut edge of clear plastic: a grey rim with a bright line just inside it
                Over(px, w, x, y, rim, Mathf.Clamp01(0.45f * Ppm - Mathf.Abs(d + 0.45f * Ppm)) * inside);
                Over(px, w, x, y, shine, Mathf.Clamp01(0.3f * Ppm - Mathf.Abs(d + 1.3f * Ppm)) * inside);
                // The two dotted grip circles on the front wing
                float g1 = Vector2.Distance(mm, new Vector2(12f, 24f)), g2 = Vector2.Distance(mm, new Vector2(15.5f, 18.5f));
                if (g1 < 3.4f || g2 < 1.9f)
                {
                    float dots = Mathf.Abs(Mathf.Sin(mm.x * 9f)) * Mathf.Abs(Mathf.Sin(mm.y * 9f));
                    Over(px, w, x, y, frost, Mathf.Clamp01((g1 < 3.4f ? 3.4f - g1 : 1.9f - g2) * Ppm) * (0.35f + 0.65f * dots));
                }
            }
        // The long straight edge: black on the edge itself, an orange line beside it, the whole length
        Line(px, w, h, new Vector2(EdgeX - 0.25f, 35.9f), new Vector2(EdgeX - 0.25f, -31.6f), 0.22f, black);
        Line(px, w, h, new Vector2(21.8f, 33.5f), new Vector2(21.8f, -26.4f), 0.2f, orange);
        // Orange-and-black stripes on the wing's outer edge and on the tab beside the capsule
        Line(px, w, h, new Vector2(-0.55f, 14.6f), new Vector2(-0.55f, 30.2f), 0.25f, black);
        Line(px, w, h, new Vector2(0.35f, 14.6f), new Vector2(0.35f, 30.3f), 0.55f, orange);
        Line(px, w, h, new Vector2(47.05f, 5.2f), new Vector2(47.05f, -5.2f), 0.25f, black);
        Line(px, w, h, new Vector2(46.15f, 5.4f), new Vector2(46.15f, -5.2f), 0.55f, orange);
        // The lobe with its X-shaped slots and two holes, frosted
        foreach (Vector2 c in new[] { new Vector2(-2f, 4.5f), new Vector2(10.5f, -2.5f) })
        {
            Vector2 u = new Vector2(0.75f, 0.66f) * 5.2f, v = new Vector2(-0.66f, 0.75f) * 3.6f;
            Line(px, w, h, c - u - v, c + u + v, 0.55f, frost);
            Line(px, w, h, c - u + v, c + u - v, 0.55f, frost);
        }
        Dot(px, w, h, new Vector2(5f, 5f), 0.8f, frost);
        Dot(px, w, h, new Vector2(2.5f, -3f), 0.8f, frost);
        // The black elastic strap through the slots, over the thumb, ribbed, with a clear buckle at its end
        Color strap = new Color(0.1f, 0.1f, 0.11f, 0.97f), rib = new Color(0.26f, 0.27f, 0.29f, 0.9f);
        Vector2 sa = new Vector2(-10f, 15f), sb = new Vector2(9.5f, -13f);   // across the thumb
        Vector2 se = (sb - sa).normalized, sn = new Vector2(-se.y, se.x);
        float slen = Vector2.Distance(sa, sb), shalf = 5.2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 mm = new Vector2((x + 0.5f) / Ppm - OriginX, (y + 0.5f) / Ppm - OriginY);
                float t = Vector2.Dot(mm - sa, se), q = Vector2.Dot(mm - sa, sn);
                float cover = Mathf.Clamp01(Mathf.Min(t, slen - t) * Ppm + 0.5f) * Mathf.Clamp01((shalf - Mathf.Abs(q)) * Ppm + 0.5f);
                if (cover <= 0f) continue;
                Over(px, w, x, y, strap, cover);
                float ribbed = Mathf.Abs(Mathf.Sin(q * 2.4f + t * 0.3f));
                Over(px, w, x, y, rib, cover * Mathf.Clamp01(1.2f - ribbed * 2f) * 0.5f);
            }
        Color buckle = new Color(0.8f, 0.85f, 0.88f, 0.95f);
        Vector2 b0 = sa - se * 0.4f;
        Line(px, w, h, b0 + sn * (shalf + 0.9f), b0 - sn * (shalf + 0.9f), 0.45f, buckle);
        Line(px, w, h, b0 + se * 2.6f + sn * (shalf + 0.9f), b0 + se * 2.6f - sn * (shalf + 0.9f), 0.45f, buckle);
        Line(px, w, h, b0 + sn * (shalf + 0.9f), b0 + se * 2.6f + sn * (shalf + 0.9f), 0.45f, buckle);
        Line(px, w, h, b0 - sn * (shalf + 0.9f), b0 + se * 2.6f - sn * (shalf + 0.9f), 0.45f, buckle);
        return plate = Make("Thumb compass plate", w, h, px);
    }

    // Distance (mm) to the plate's outline, negative inside (any outline, convex or not)
    static float PlateDistance(Vector2 p)
    {
        float best = float.MaxValue;
        bool inside = false;
        for (int i = 0, j = Outline.Length - 1; i < Outline.Length; j = i++)
        {
            Vector2 a = Outline[j], b = Outline[i], e = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, e) / Mathf.Max(1e-6f, e.sqrMagnitude));
            best = Mathf.Min(best, Vector2.Distance(p, a + e * t));
            if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) / (b.y - a.y) * e.x) inside = !inside;
        }
        return inside ? -best : best;
    }

    // The capsule: clear liquid in a thick silver ring
    public static Texture2D Capsule()
    {
        if (capsule != null) return capsule;
        int s = (int)CapsuleD * Ppm;
        Color[] px = Blank(s, s);
        float c = s * 0.5f, rOut = s * 0.5f - 1f, ring = 2.2f * Ppm;
        Color liquid = new Color(0.92f, 0.97f, 0.98f, 0.3f);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float inside = Mathf.Clamp01(rOut - d + 0.5f);
                if (inside <= 0f) continue;
                Over(px, s, x, y, liquid, inside);
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

    // The needle of a thumb compass (Anders' pictures): a long flat bar, the north half red with a white stripe down
    // the middle and a notch at its tip, the south half white with a thin grey line, a thin dark edge, a small pivot
    public static Texture2D Needle()
    {
        if (needle != null) return needle;
        int w = 7 * Ppm, h = 28 * Ppm;
        Color[] px = Blank(w, h);
        float cx = w * 0.5f, cy = h * 0.5f, half = 3.1f * Ppm, end = 0.6f * Ppm;
        Color red = new Color(0.9f, 0.08f, 0.07f, 1f), white = new Color(0.98f, 0.98f, 0.96f, 1f), dark = new Color(0.12f, 0.12f, 0.13f, 1f);
        Color grey = new Color(0.62f, 0.63f, 0.64f, 1f);
        Rect(px, w, h, cx - half - 0.25f * Ppm, end - 0.25f * Ppm, cx + half + 0.25f * Ppm, h - end + 0.25f * Ppm, dark);
        Rect(px, w, h, cx - half, cy, cx + half, h - end, red);
        Rect(px, w, h, cx - half, end, cx + half, cy, white);
        Rect(px, w, h, cx - 0.55f * Ppm, cy + 1.2f * Ppm, cx + 0.55f * Ppm, h - end, white);
        Triangle(px, w, h, new Vector2(cx - half, h - end + 0.5f), new Vector2(cx + half, h - end + 0.5f), new Vector2(cx, h - end - 1.6f * Ppm), white);
        Rect(px, w, h, cx - 0.12f * Ppm, end, cx + 0.12f * Ppm, cy - 0.6f * Ppm, grey);
        Rect(px, w, h, cx - half, cy - 0.15f * Ppm, cx + half, cy + 0.15f * Ppm, grey);
        Dot(px, w, h, new Vector2(cx / Ppm, cy / Ppm), 0.9f, new Color(0.8f, 0.8f, 0.78f, 1f), true);
        Dot(px, w, h, new Vector2(cx / Ppm, cy / Ppm), 0.35f, dark, true);
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
