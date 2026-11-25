using System;
using System.Collections.Generic;

// One human body for everyone in the 3D views (2026-11-25, Anders: "the same type of person for the runners and the
// people in the sprint town, as human as possible, with human shapes").
//
// A body is one smooth, closed-up skin (no balls and sticks): the trunk, legs, arms, hands and neck are lofted from
// anatomical cross-sections (hips and buttocks, waist, ribs and chest, thighs, knees, calves, ankles, shoulders,
// biceps, forearms, mitten hands with a thumb), the shoes are shaped with a sole, and the head is a sculpted skull
// and face (cheekbones, jaw, chin, nose, brow, eye sockets) with eyes, brows, lips, ears and hair on it.
// Every corner of the skin hangs on one or two of 16 bones; a pose (Rig: where the joints are) moves the bones and
// the skin follows smoothly through the knees, elbows, hips and shoulders (linear blend skinning).
//  - TvStickFigure (the runners) hands the mesh and its bones to Unity's SkinnedMeshRenderer and moves the bones
//    every frame;
//  - TvCityShapes.Figure (the town's people) skins a few frames of each animation here (Skin) and keeps them as
//    plain meshes.
// Plain C# (no Unity), so the preview harness can draw it too. Faces +z with +x to the right; y up; metres.
public struct V3
{
    public float x, y, z;
    public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
    public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
    public static V3 operator -(V3 a) => new V3(-a.x, -a.y, -a.z);
    public static V3 operator *(V3 a, float s) => new V3(a.x * s, a.y * s, a.z * s);
    public static V3 operator *(float s, V3 a) => new V3(a.x * s, a.y * s, a.z * s);
    public static V3 operator /(V3 a, float s) => new V3(a.x / s, a.y / s, a.z / s);
    public static float Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static V3 Cross(V3 a, V3 b) => new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
    public float Length => (float)Math.Sqrt(x * x + y * y + z * z);
    public V3 Normalized { get { float l = Length; return l > 1e-8f ? this / l : new V3(0f, 1f, 0f); } }
    public static V3 Lerp(V3 a, V3 b, float t) => a + (b - a) * t;
    public static V3 ProjectOnPlane(V3 v, V3 n) => v - n * Dot(v, n);
    public static readonly V3 Up = new V3(0f, 1f, 0f), Right = new V3(1f, 0f, 0f), Fwd = new V3(0f, 0f, 1f);
}

public static class TvHuman
{
    public const int Pelvis = 0, Spine = 1, Neck = 2, Head = 3, ThighL = 4, ThighR = 5, ShinL = 6, ShinR = 7, FootL = 8, FootR = 9,
        UpperL = 10, UpperR = 11, ForeL = 12, ForeR = 13, HandL = 14, HandR = 15, BoneCount = 16;

    // Who the person is and what they wear
    public class Look
    {
        public bool female, child;
        public float height = 1.78f, girth = 1f;
        public TvColor skin = TvColor.Hex("#e6b590"), hair = TvColor.Hex("#6b4a2e");
        public TvColor top = TvColor.Hex("#3a6ea5"), top2 = TvColor.Hex("#24476e"), bottom = TvColor.Hex("#2b2e36");
        public TvColor shoe = TvColor.Hex("#e8e6e0"), sole = TvColor.Hex("#3a3a3a"), sock = TvColor.Hex("#2b2e36");
        public float sockHigh;        // socks (or gaiters) this high up the leg (metres; 0 none)
        public int sleeves = 2;       // 0 none, 1 short, 2 long
        public int legs;              // 0 long trousers or tights, 1 shorts, 2 a skirt
        public TvColor skirt = TvColor.Hex("#5a3a5a");
        public int hairStyle;         // 0 short, 1 cropped, 2 swept, 3 curly top, 4 long, 5 ponytail, 6 bun, 7 bob
        public bool beard;
        public bool suit;             // an orienteering suit: patterned top, shoes with coloured studs
        public int detail = 1;        // 0 low, 1 normal, 2 high
    }

    // Where the joints are. [0] left, [1] right.
    public class Rig
    {
        public V3 hip, waist, chest, neckTop;     // down the middle: between the hip joints, waist, between the shoulders, top of the neck
        public V3 right = V3.Right;               // the hips' right
        public V3 chestRight = V3.Right;          // the shoulders' right (they turn against the hips when running)
        public V3 headFwd = V3.Fwd, headUp = V3.Up;
        public V3[] hipJ = new V3[2], knee = new V3[2], ankle = new V3[2], toe = new V3[2];
        public V3[] shoulder = new V3[2], elbow = new V3[2], wrist = new V3[2], hand = new V3[2];

        public Rig Copy()
        {
            var r = (Rig)MemberwiseClone();
            r.hipJ = (V3[])hipJ.Clone(); r.knee = (V3[])knee.Clone(); r.ankle = (V3[])ankle.Clone(); r.toe = (V3[])toe.Clone();
            r.shoulder = (V3[])shoulder.Clone(); r.elbow = (V3[])elbow.Clone(); r.wrist = (V3[])wrist.Clone(); r.hand = (V3[])hand.Clone();
            return r;
        }
    }

    // A bone: where it starts and which way it lies (y along the bone, z its front, x = y cross z)
    public struct Frame { public V3 o, x, y, z; }

    // The made skin, standing in its rest pose
    public class Body
    {
        public Look look;
        public Rig rest;
        public Frame[] restFrames;
        public float[] pos, nrm;          // rest pose
        public byte[] col;
        public int[] tri;
        public int[] b0, b1;              // the two bones of each corner
        public float[] w0;                // weight of b0 (b1 gets the rest)
        public float[] l0, l1;            // the corner in each bone's own frame (x, y, z)
        public float[] n0, n1;            // the normal in each bone's frame
        public int VertexCount => pos.Length / 3;
        // Proportions
        public float k, kb, hs, thigh, shin, upper, fore, handLen, footLen;
    }

    // ---- Proportions and the rest pose ----

    public static Rig RestRig(Look look, out float k, out float kb, out float hs)
    {
        float h = look.child ? Math.Max(0.9f, Math.Min(1.5f, look.height)) : look.height;
        k = h / 1.78f;
        hs = look.child ? 1.32f : 1f;                     // a child's head is big for its body
        float headH = 0.215f * k * hs;
        kb = (h - headH) / 1.565f;                        // everything below the top of the neck
        float w = kb * (look.child ? 1.02f : 1f);
        float hipW = (look.female ? 0.1f : 0.092f) * w * (0.9f + 0.1f * look.girth);
        float shW = (look.female ? 0.172f : 0.198f) * w * (0.94f + 0.06f * look.girth);
        var r = new Rig
        {
            hip = new V3(0f, 0.925f * kb, 0f),
            waist = new V3(0f, 1.12f * kb, 0.004f * kb),
            chest = new V3(0f, 1.44f * kb, -0.008f * kb),
            neckTop = new V3(0f, 1.565f * kb, 0.012f * kb)
        };
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? -1f : 1f;
            r.hipJ[i] = new V3(s * hipW, 0.925f * kb, 0f);
            r.knee[i] = new V3(s * hipW * 0.93f, 0.5f * kb, 0.012f * kb);
            r.ankle[i] = new V3(s * hipW * 0.9f, 0.085f * kb, -0.01f * kb);
            r.toe[i] = new V3(s * hipW * 0.98f, 0.028f * kb, 0.15f * kb);
            r.shoulder[i] = new V3(s * shW, 1.42f * kb, -0.012f * kb);
            r.elbow[i] = new V3(s * (shW + 0.045f * kb), 1.125f * kb, -0.03f * kb);
            r.wrist[i] = new V3(s * (shW + 0.068f * kb), 0.865f * kb, -0.005f * kb);
            r.hand[i] = new V3(s * (shW + 0.074f * kb), 0.69f * kb, 0.012f * kb);
        }
        return r;
    }

    // The bones of a pose
    public static Frame[] Frames(Rig r)
    {
        var f = new Frame[BoneCount];
        f[Pelvis] = Make(r.hip, r.waist - r.hip, r.right);
        f[Spine] = Make(r.waist, r.chest - r.waist, r.chestRight);
        f[Neck] = Make(r.chest, r.neckTop - r.chest, r.chestRight);
        {
            V3 y = r.headUp.Normalized, z = V3.ProjectOnPlane(r.headFwd, y).Normalized;
            f[Head] = new Frame { o = r.neckTop, y = y, z = z, x = V3.Cross(y, z) };
        }
        for (int i = 0; i < 2; i++)
        {
            f[ThighL + i] = Make(r.hipJ[i], r.knee[i] - r.hipJ[i], r.right);
            f[ShinL + i] = Make(r.knee[i], r.ankle[i] - r.knee[i], f[ThighL + i].x);
            f[FootL + i] = Make(r.ankle[i], r.toe[i] - r.ankle[i], f[ShinL + i].x);
            f[UpperL + i] = Make(r.shoulder[i], r.elbow[i] - r.shoulder[i], r.chestRight, f[Spine].z);
            f[ForeL + i] = Make(r.elbow[i], r.wrist[i] - r.elbow[i], f[UpperL + i].x, f[UpperL + i].z);
            f[HandL + i] = Make(r.wrist[i], r.hand[i] - r.wrist[i], f[ForeL + i].x, f[ForeL + i].z);
        }
        return f;
    }

    // A frame along dir, turned so its x lies as near as it can to hint (or, if dir lies along hint, its z near alt)
    static Frame Make(V3 o, V3 dir, V3 hint, V3? alt = null)
    {
        V3 y = dir.Normalized;
        V3 z = V3.Cross(hint, y);
        if (z.Length < 0.25f && alt.HasValue)
        {
            // Nearly along the hint (an arm held out sideways): steer by the other axis instead
            V3 z2 = V3.ProjectOnPlane(alt.Value, y);
            float t = z.Length / 0.25f;
            z = V3.Lerp(z2.Normalized, z.Normalized, t);
        }
        z = z.Normalized;
        V3 x = V3.Cross(y, z).Normalized;
        z = V3.Cross(x, y);
        return new Frame { o = o, x = x, y = y, z = z };
    }

    // ---- Making the body ----

    class Mesher
    {
        public readonly List<float> p = new List<float>(), n = new List<float>();
        public readonly List<byte> c = new List<byte>();
        public readonly List<int> t = new List<int>(), b0 = new List<int>(), b1 = new List<int>();
        public readonly List<float> w = new List<float>();
        public readonly List<V3> outward = new List<V3>();   // a point inside, to turn each triangle outward

        public int V(V3 at, TvColor col, int boneA, int boneB, float wA, V3 inside)
        {
            p.Add(at.x); p.Add(at.y); p.Add(at.z);
            n.Add(0f); n.Add(0f); n.Add(0f);
            c.Add(col.r); c.Add(col.g); c.Add(col.b);
            b0.Add(boneA); b1.Add(boneB); w.Add(wA);
            outward.Add(inside);
            return p.Count / 3 - 1;
        }

        V3 P(int i) => new V3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]);

        // A triangle facing away from the inside points of its corners (as TvShapeBuilder does)
        public void Tri(int a, int b, int q)
        {
            V3 pa = P(a), pb = P(b), pq = P(q);
            V3 face = V3.Cross(pb - pa, pq - pa);
            V3 inside = (outward[a] + outward[b] + outward[q]) / 3f;
            if (V3.Dot(face, (pa + pb + pq) / 3f - inside) < 0f) { int x = b; b = q; q = x; }
            t.Add(a); t.Add(b); t.Add(q);
        }

        public void Normals()
        {
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                int a = t[i], b = t[i + 1], q = t[i + 2];
                V3 f = V3.Cross(P(b) - P(a), P(q) - P(a));
                foreach (int v in new[] { a, b, q })
                {
                    n[v * 3] += f.x; n[v * 3 + 1] += f.y; n[v * 3 + 2] += f.z;
                }
            }
            for (int v = 0; v < n.Count / 3; v++)
            {
                var nn = new V3(n[v * 3], n[v * 3 + 1], n[v * 3 + 2]);
                if (nn.Length < 1e-9f) nn = P(v) - outward[v];
                nn = nn.Normalized;
                n[v * 3] = nn.x; n[v * 3 + 1] = nn.y; n[v * 3 + 2] = nn.z;
            }
        }
    }

    // One cross-section of a loft: a rounded box (superellipse) round c, across ax and towards az
    struct Ring
    {
        public V3 c, ax, az;
        public float rx, rf, rb, ex;          // half width, depth to the front (+az) and to the back
        public float rxIn;                    // half width on the -ax side (0: same as rx)
        public int boneA, boneB;
        public float wA;
        public Func<float, V3, TvColor> color; // by the angle round (0 = +ax, pi/2 = +az) and the point
        public Func<float, float> bump;        // extra outward by the angle round (metres)
        public bool sideThighs;                // boneB is the thigh on each side (a skirt following the legs)
    }

    static void Loft(Mesher m, List<Ring> rings, int around, bool capStart, bool capEnd)
    {
        int first = m.p.Count / 3;
        foreach (Ring r in rings)
        {
            for (int j = 0; j < around; j++)
            {
                float a = j * 6.2831853f / around;
                float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                float e = 2f / r.ex;
                float px = Math.Sign(ca) * (float)Math.Pow(Math.Abs(ca), e) * (ca < 0f && r.rxIn > 0f ? r.rxIn : r.rx);
                float pz = Math.Sign(sa) * (float)Math.Pow(Math.Abs(sa), e) * (sa > 0f ? r.rf : r.rb);
                V3 at = r.c + r.ax * px + r.az * pz;
                if (r.bump != null)
                {
                    float b = r.bump(a);
                    if (b != 0f) at = at + (r.ax * ca + r.az * sa).Normalized * b;
                }
                TvColor col = r.color(a, at);
                int bb = r.sideThighs ? (ca < 0f ? ThighL : ThighR) : r.boneB;
                float wa = r.sideThighs ? Lerp(1f, r.wA, Math.Abs(ca) * 0.3f + Math.Abs(sa) * 0.7f) : r.wA;
                m.V(at, col, r.boneA, bb, wa, r.c);
            }
        }
        for (int i = 0; i + 1 < rings.Count; i++)
            for (int j = 0; j < around; j++)
            {
                int a = first + i * around + j, b = first + i * around + (j + 1) % around;
                int q = a + around, e = b + around;
                m.Tri(a, b, q);
                m.Tri(b, e, q);
            }
        void Cap(int ring, float dirSign)
        {
            Ring r = rings[ring];
            V3 along = ring + 1 < rings.Count ? rings[ring + 1].c - r.c : r.c - rings[ring - 1].c;
            V3 tip = r.c - along.Normalized * (Math.Min(r.rx, Math.Min(r.rf, r.rb)) * 0.35f * dirSign);
            int ci = m.V(tip, r.color(0f, r.c), r.boneA, r.boneB, r.wA, r.c + along.Normalized * (0.01f * dirSign));
            int start = first + ring * around;
            for (int j = 0; j < around; j++) m.Tri(start + j, start + (j + 1) % around, ci);
        }
        if (capStart) Cap(0, 1f);
        if (capEnd) Cap(rings.Count - 1, -1f);
    }

    // A closed ellipsoid on one bone (eyes, ears, lips, a bun)
    static void Ellipsoid(Mesher m, V3 c, V3 ax, V3 ay, V3 az, float rx, float ry, float rz, int bone, TvColor col, int rings, int around)
    {
        int first = m.p.Count / 3;
        for (int i = 0; i <= rings; i++)
        {
            float t = i * 3.14159265f / rings, st = (float)Math.Sin(t), ct = (float)Math.Cos(t);
            for (int j = 0; j < around; j++)
            {
                float a = j * 6.2831853f / around;
                V3 at = c + ax * (rx * st * (float)Math.Cos(a)) + ay * (ry * ct) + az * (rz * st * (float)Math.Sin(a));
                m.V(at, col, bone, bone, 1f, c);
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < around; j++)
            {
                int a = first + i * around + j, b = first + i * around + (j + 1) % around, q = a + around, e = b + around;
                if (i > 0) m.Tri(a, b, q);
                if (i < rings - 1) m.Tri(b, e, q);
            }
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // Adds two samples just either side of 'v' to a table of distances and its radii (between existing samples only)
    static void InsertPair(ref float[] at, ref float[] a, ref float[] b, ref float[] c, float v)
    {
        for (int k = 2; k < at.Length; k++)
        {
            if (!(at[k - 1] < v - 0.005f && at[k] > v + 0.005f)) continue;
            var na = new List<float>(at); var la = new List<float>(a); var lb = new List<float>(b); var lc = new List<float>(c);
            foreach (float x in new[] { v + 0.004f, v - 0.004f })
            {
                float t = (x - at[k - 1]) / (at[k] - at[k - 1]);
                na.Insert(k, x); la.Insert(k, Lerp(a[k - 1], a[k], t)); lb.Insert(k, Lerp(b[k - 1], b[k], t)); lc.Insert(k, Lerp(c[k - 1], c[k], t));
            }
            at = na.ToArray(); a = la.ToArray(); b = lb.ToArray(); c = lc.ToArray();
            return;
        }
    }

    static float Smooth(float a, float b, float x)
    {
        float t = (x - a) / (b - a);
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        return t * t * (3f - 2f * t);
    }

    static float Bell(float x, float w) => (float)Math.Exp(-(x * x) / (w * w));

    // Interpolates a table of (y, value...) rows at y
    static float[] Table(float[,] t, float y)
    {
        int rows = t.GetLength(0), cols = t.GetLength(1);
        var o = new float[cols];
        if (y <= t[0, 0]) { for (int c = 0; c < cols; c++) o[c] = t[0, c]; return o; }
        for (int r = 0; r + 1 < rows; r++)
            if (y <= t[r + 1, 0])
            {
                float u = (y - t[r, 0]) / (t[r + 1, 0] - t[r, 0]);
                u = u * u * (3f - 2f * u);
                for (int c = 0; c < cols; c++) o[c] = t[r, c] + (t[r + 1, c] - t[r, c]) * u;
                return o;
            }
        for (int c = 0; c < cols; c++) o[c] = t[rows - 1, c];
        return o;
    }

    // The trunk from the crotch to the neck (male; y, half width, front depth, back depth), at 1.78 m
    static readonly float[,] TrunkM =
    {
        { 0.80f, 0.085f, 0.055f, 0.06f },
        { 0.84f, 0.15f, 0.078f, 0.1f },
        { 0.90f, 0.172f, 0.088f, 0.118f },
        { 0.96f, 0.17f, 0.092f, 0.105f },
        { 1.02f, 0.158f, 0.096f, 0.09f },
        { 1.09f, 0.148f, 0.1f, 0.084f },
        { 1.17f, 0.152f, 0.104f, 0.085f },
        { 1.25f, 0.165f, 0.11f, 0.09f },
        { 1.33f, 0.176f, 0.118f, 0.097f },
        { 1.39f, 0.18f, 0.112f, 0.1f },
        { 1.435f, 0.168f, 0.09f, 0.092f },
        { 1.47f, 0.135f, 0.07f, 0.08f },
        { 1.5f, 0.078f, 0.058f, 0.064f }
    };
    static readonly float[,] TrunkF =
    {
        { 0.80f, 0.09f, 0.055f, 0.062f },
        { 0.84f, 0.165f, 0.08f, 0.108f },
        { 0.90f, 0.185f, 0.088f, 0.124f },
        { 0.96f, 0.178f, 0.09f, 0.11f },
        { 1.02f, 0.152f, 0.088f, 0.088f },
        { 1.09f, 0.128f, 0.086f, 0.078f },
        { 1.17f, 0.132f, 0.09f, 0.08f },
        { 1.25f, 0.145f, 0.098f, 0.084f },
        { 1.33f, 0.155f, 0.1f, 0.088f },
        { 1.39f, 0.16f, 0.096f, 0.09f },
        { 1.435f, 0.15f, 0.08f, 0.085f },
        { 1.47f, 0.12f, 0.062f, 0.072f },
        { 1.5f, 0.07f, 0.052f, 0.058f }
    };
    // The leg from the top of the thigh to the ankle (y, half width, front depth, back depth)
    static readonly float[,] LegT =
    {
        { 1.0f, 0.082f, 0.08f, 0.09f },
        { 0.93f, 0.088f, 0.085f, 0.092f },
        { 0.84f, 0.083f, 0.082f, 0.08f },
        { 0.74f, 0.074f, 0.076f, 0.068f },
        { 0.64f, 0.064f, 0.066f, 0.057f },
        { 0.56f, 0.055f, 0.058f, 0.05f },
        { 0.50f, 0.052f, 0.056f, 0.048f },
        { 0.45f, 0.05f, 0.046f, 0.054f },
        { 0.39f, 0.049f, 0.042f, 0.06f },
        { 0.32f, 0.049f, 0.04f, 0.056f },
        { 0.25f, 0.043f, 0.037f, 0.046f },
        { 0.17f, 0.034f, 0.032f, 0.036f },
        { 0.11f, 0.031f, 0.031f, 0.034f },
        { 0.07f, 0.03f, 0.032f, 0.036f }
    };

    static readonly Dictionary<string, Body> cache = new Dictionary<string, Body>();

    // The body of a look, made once (Looks with the same key share it)
    public static Body Make(Look look, string key = null)
    {
        if (key != null)
            lock (cache)
                if (cache.TryGetValue(key, out Body made)) return made;
        Body body = Build(look);
        if (key != null)
            lock (cache) cache[key] = body;
        return body;
    }

    static Body Build(Look look)
    {
        var body = new Body { look = look };
        Rig rest = RestRig(look, out float k, out float kb, out float hs);
        body.rest = rest; body.k = k; body.kb = kb; body.hs = hs;
        Frame[] rf = Frames(rest);
        body.restFrames = rf;
        body.thigh = (rest.knee[0] - rest.hipJ[0]).Length;
        body.shin = (rest.ankle[0] - rest.knee[0]).Length;
        body.upper = (rest.elbow[0] - rest.shoulder[0]).Length;
        body.fore = (rest.wrist[0] - rest.elbow[0]).Length;
        body.handLen = (rest.hand[0] - rest.wrist[0]).Length;
        body.footLen = (rest.toe[0] - rest.ankle[0]).Length;

        var m = new Mesher();
        int det = Math.Max(0, Math.Min(2, look.detail));
        int aroundBody = det == 0 ? 12 : det == 1 ? 18 : 24, aroundLimb = det == 0 ? 9 : det == 1 ? 13 : 16;
        float g = look.girth, w = kb * (look.child ? 1.02f : 1f);
        bool adultF = look.female && !look.child;
        float topEnd = look.suit ? 0.97f : 1.0f;    // where the top ends over the trousers (rest y / kb)

        // ---- The trunk ----
        {
            var rings = new List<Ring>();
            float[,] table = adultF ? TrunkF : TrunkM;
            int count = det == 0 ? 10 : det == 1 ? 15 : 20;
            // Even rings, plus a close pair at each colour edge so the colours meet in a sharp line
            var levels = new List<float>();
            for (int i = 0; i < count; i++) levels.Add(0.8f + 0.7f * i / (count - 1));
            if (det > 0)
                foreach (float edge in new[] { topEnd, look.suit ? 1.385f : topEnd + 0.035f, look.suit ? 0.9f : 1.47f })
                    { levels.Add(edge - 0.004f); levels.Add(edge + 0.004f); }
            levels.Sort();
            foreach (float yy in levels)
            {
                float[] t = Table(table, yy);
                float y = yy * kb;
                float wPel = 1f - Smooth(0.98f, 1.16f, yy), wNeck = Smooth(1.44f, 1.5f, yy) * 0.5f;
                int ba, bb; float wa;
                if (yy < 1.3f) { ba = Pelvis; bb = Spine; wa = wPel; }
                else { ba = Spine; bb = Neck; wa = 1f - wNeck; }
                float girthHere = yy < 1.42f ? (0.88f + 0.12f * g) : 1f;
                float zc = (yy > 1.2f ? -0.006f : 0.004f) * kb;
                float yy0 = yy;
                var ring = new Ring
                {
                    c = new V3(0f, y, zc), ax = V3.Right, az = V3.Fwd,
                    rx = t[1] * w * girthHere, rf = t[2] * w * girthHere * (yy > 1.0f && yy < 1.25f ? 0.9f + 0.18f * g : 1f), rb = t[3] * w * girthHere,
                    ex = yy < 0.86f ? 2.2f : 2.5f,
                    boneA = ba, boneB = bb, wA = wa,
                    color = (a, at) => TrunkColor(look, yy0, a, at, kb, topEnd),
                    bump = a =>
                    {
                        float b = 0f;
                        float front = (float)Math.Sin(a);
                        // Buttocks: two rounded lobes at the back
                        if (front < 0f) b += (adultF ? 0.024f : 0.016f) * w * Bell(yy0 - 0.9f, 0.055f) * (Bell(a - 4.712f - 0.62f, 0.42f) + Bell(a - 4.712f + 0.62f, 0.42f));
                        // Breasts (women) and chest muscles (men)
                        if (front > 0f)
                        {
                            float lobes = Bell(a - 1.5708f - 0.6f, 0.36f) + Bell(a - 1.5708f + 0.6f, 0.36f);
                            b += (adultF ? 0.034f : 0.01f) * w * Bell(yy0 - (adultF ? 1.3f : 1.34f), adultF ? 0.05f : 0.045f) * lobes;
                            // A little belly
                            b += 0.012f * w * (g - 0.85f) * Bell(yy0 - 1.07f, 0.07f) * Bell(a - 1.5708f, 0.7f);
                        }
                        // Shoulder blades
                        if (front < 0f && !look.child) b += 0.008f * w * Bell(yy0 - 1.35f, 0.05f) * (Bell(a - 4.712f - 0.7f, 0.3f) + Bell(a - 4.712f + 0.7f, 0.3f));
                        return b;
                    }
                };
                rings.Add(ring);
            }
            Loft(m, rings, aroundBody, true, true);
        }

        // ---- Legs ----
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? -1f : 1f;
            V3 hipJ = rest.hipJ[i], knee = rest.knee[i], ankle = rest.ankle[i];
            var rings = new List<Ring>();
            int count = det == 0 ? 9 : det == 1 ? 14 : 18;
            float top = 1.0f, bottom = 0.075f;
            for (int r = 0; r < count; r++)
            {
                float yy = top + (bottom - top) * r / (count - 1);
                float y = yy * kb;
                float[] t = Table(LegT, yy);
                // On the line hip - knee - ankle (above the hip joint: straight up from it)
                V3 c;
                if (y >= hipJ.y) c = hipJ + V3.Up * (y - hipJ.y);
                else if (y >= knee.y) c = V3.Lerp(knee, hipJ, (y - knee.y) / (hipJ.y - knee.y));
                else c = V3.Lerp(ankle, knee, (y - ankle.y) / (knee.y - ankle.y));
                // A woman's thighs a little fuller, a child's legs slimmer
                float full = (adultF && yy > 0.6f ? 1.06f : 1f) * (look.child ? 0.92f : 1f) * (0.86f + 0.14f * g);
                // Weights: the top half on the hips, the knee shared by thigh and shin, the ankle a little on the foot
                int ba, bb; float wa;
                if (yy > 0.96f) { ba = ThighL + i; bb = Pelvis; wa = 0.55f; }
                else if (yy > 0.56f) { ba = ThighL + i; bb = ShinL + i; wa = 1f; }
                else if (yy > 0.44f) { ba = ThighL + i; bb = ShinL + i; wa = Smooth(0.44f, 0.56f, yy); }
                else if (yy > 0.12f) { ba = ShinL + i; bb = FootL + i; wa = 1f; }
                else { ba = ShinL + i; bb = FootL + i; wa = 0.6f; }
                float yy0 = yy;
                rings.Add(new Ring
                {
                    c = c, ax = V3.Right, az = V3.Fwd,
                    rx = t[1] * w * full, rf = t[2] * w * full, rb = t[3] * w * full, ex = 2.1f,
                    boneA = ba, boneB = bb, wA = wa,
                    color = (a, at) => LegColor(look, yy0, kb),
                    bump = yy > 0.6f && yy < 0.95f ? (Func<float, float>)(a => 0.006f * w * Bell(yy0 - 0.72f, 0.1f) * Bell(a - 1.5708f + 0.35f * s, 0.6f)) : null
                });
            }
            Loft(m, rings, aroundLimb + 2, false, false);
            // The skirt hangs over the thighs
        }
        if (look.legs == 2 && !look.child)
        {
            var rings = new List<Ring>();
            float[] ys = { 1.04f, 0.96f, 0.84f, 0.7f, 0.58f };
            float[] wx = { 0.15f, 0.19f, 0.215f, 0.235f, 0.25f };
            for (int r = 0; r < ys.Length; r++)
            {
                float yy = ys[r];
                rings.Add(new Ring
                {
                    c = new V3(0f, yy * kb, -0.008f * kb), ax = V3.Right, az = V3.Fwd,
                    rx = wx[r] * w * (0.9f + 0.1f * g), rf = wx[r] * 0.72f * w, rb = wx[r] * 0.82f * w, ex = 2.2f,
                    boneA = Pelvis, boneB = ThighL, wA = 1f - 0.55f * r / (ys.Length - 1f), sideThighs = true,
                    color = (a, at) => look.skirt
                });
            }
            Loft(m, rings, aroundBody, false, false);
        }

        // ---- Shoes ----
        for (int i = 0; i < 2; i++)
        {
            V3 ankle = rest.ankle[i];
            float baseX = ankle.x + (i == 0 ? -1f : 1f) * 0.006f * kb;
            float[] zs = { -0.075f, -0.06f, -0.02f, 0.035f, 0.085f, 0.125f, 0.158f, 0.18f, 0.193f };
            float[] tops = { 0.085f, 0.1f, 0.098f, 0.078f, 0.064f, 0.056f, 0.048f, 0.04f, 0.028f };
            float[] wid = { 0.03f, 0.038f, 0.042f, 0.045f, 0.048f, 0.047f, 0.042f, 0.034f, 0.02f };
            var rings = new List<Ring>();
            float fk = kb * (look.female ? 0.94f : 1f);
            for (int r = 0; r < zs.Length; r++)
            {
                float top = tops[r] * kb, half = wid[r] * fk;
                float zz = ankle.z + zs[r] * fk;
                float side = i == 0 ? -1f : 1f;
                // The toes turn in a little to the big toe
                float xoff = r >= 6 ? -side * 0.006f * (r - 5) * kb : 0f;
                rings.Add(new Ring
                {
                    c = new V3(baseX + xoff, top * 0.5f, zz), ax = V3.Right, az = V3.Up,
                    rx = half, rf = top * 0.5f, rb = top * 0.5f, ex = 3.2f,
                    boneA = FootL + i, boneB = ShinL + i, wA = r <= 1 ? 0.85f : 1f,
                    color = (a, at) => ShoeColor(look, at.y / kb, a)
                });
            }
            Loft(m, rings, aroundLimb + 3, true, true);
        }

        // ---- Arms and hands ----
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? -1f : 1f;
            V3 sh = rest.shoulder[i], el = rest.elbow[i], wr = rest.wrist[i];
            V3 du = (el - sh).Normalized, df = (wr - el).Normalized;
            float lu = (el - sh).Length, lf = (wr - el).Length;
            float ag = (look.child ? 0.9f : 1f) * (0.85f + 0.15f * g) * (adultF ? 0.9f : 1f);
            // (distance along from the shoulder joint, half width, front, back, bone weights)
            float[] at = { -0.035f, -0.015f, 0.02f, 0.07f, 0.13f, 0.2f, 0.26f, 0.3f, 0.34f, 0.41f, 0.49f, 0.56f };
            float[] rx = { 0.04f, 0.05f, 0.052f, 0.05f, 0.046f, 0.044f, 0.039f, 0.037f, 0.043f, 0.038f, 0.031f, 0.028f };
            float[] rfr = { 0.04f, 0.05f, 0.052f, 0.048f, 0.05f, 0.044f, 0.037f, 0.036f, 0.04f, 0.035f, 0.026f, 0.02f };
            float[] rbk = { 0.04f, 0.048f, 0.05f, 0.047f, 0.044f, 0.043f, 0.04f, 0.042f, 0.041f, 0.034f, 0.026f, 0.02f };
            var rings = new List<Ring>();
            float scale = lu / 0.3f;
            if (det > 0)
            {
                // A close pair of rings where the sleeve ends, for a sharp edge
                float cut = look.sleeves == 2 ? lu + 0.23f * kb : look.sleeves == 1 ? 0.1f * kb : -0.03f * kb;
                if (look.sleeves == 2 && look.suit) cut = lu + 0.2f * kb;
                InsertPair(ref at, ref rx, ref rfr, ref rbk, cut / scale);
            }
            for (int r = 0; r < at.Length; r++)
            {
                float d = at[r] * scale;
                V3 c, dir;
                if (d < 0f) { c = sh + du * d + V3.Right * (-s * 0.02f * kb * (-d / 0.035f)); dir = du; }
                else if (d <= lu) { c = sh + du * d; dir = du; }
                else { c = el + df * Math.Min(lf, d - lu); dir = df; }
                V3 ax = V3.ProjectOnPlane(V3.Right, dir).Normalized, az = V3.Cross(ax, dir).Normalized;
                if (V3.Dot(az, V3.Fwd) < 0f) az = -az;
                int ba, bb; float wa;
                if (r == 0) { ba = UpperL + i; bb = Spine; wa = 0.45f; }
                else if (r == 1) { ba = UpperL + i; bb = Spine; wa = 0.8f; }
                else if (d < lu - 0.035f * scale) { ba = UpperL + i; bb = ForeL + i; wa = 1f; }
                else if (d <= lu + 0.045f * scale) { ba = UpperL + i; bb = ForeL + i; wa = 1f - Smooth(lu - 0.035f * scale, lu + 0.045f * scale, d); }
                else if (r == at.Length - 1) { ba = ForeL + i; bb = HandL + i; wa = 0.75f; }
                else { ba = ForeL + i; bb = HandL + i; wa = 1f; }
                float dd = d, lu0 = lu;
                rings.Add(new Ring
                {
                    c = c, ax = ax, az = az, rx = rx[r] * w * ag, rf = rfr[r] * w * ag, rb = rbk[r] * w * ag, ex = 2.1f,
                    boneA = ba, boneB = bb, wA = wa,
                    color = (a, p) => ArmColor(look, dd, lu0, kb)
                });
            }
            Loft(m, rings, aroundLimb, true, false);

            // The hand: a relaxed mitten, palm towards the thigh, fingers curled in a little, and a thumb
            V3 hd = (rest.hand[i] - wr).Normalized;
            float hl = (rest.hand[i] - wr).Length;
            V3 across = V3.ProjectOnPlane(V3.Fwd, hd).Normalized;            // the width of the hand
            V3 thick = V3.Cross(hd, across).Normalized;                        // out of the palm
            if (V3.Dot(thick, V3.Right * s) < 0f) thick = -thick;               // thick points away from the body
            float hk = (look.child ? 0.85f : adultF ? 0.92f : 1f) * kb;
            float[] ht = { -0.01f, 0.025f, 0.06f, 0.09f, 0.115f, 0.13f, 0.14f };
            float[] hw = { 0.028f, 0.036f, 0.041f, 0.043f, 0.041f, 0.035f, 0.022f };
            float[] hth = { 0.021f, 0.024f, 0.026f, 0.03f, 0.03f, 0.026f, 0.016f };
            var hr = new List<Ring>();
            for (int r = 0; r < ht.Length; r++)
            {
                float d = ht[r] / 0.175f * hl;
                float curl = Smooth(0.07f, 0.14f, ht[r]);
                V3 c = wr + hd * d - thick * (0.018f * curl * hk);
                hr.Add(new Ring
                {
                    c = c, ax = across, az = thick, rx = hw[r] * hk, rf = hth[r] * hk, rb = hth[r] * hk, ex = 2.4f,
                    boneA = HandL + i, boneB = ForeL + i, wA = r == 0 ? 0.6f : 1f,
                    color = (a, p) => look.skin
                });
            }
            Loft(m, hr, det == 0 ? 8 : 12, false, true);
            {
                var tr = new List<Ring>();
                V3 baseT = wr + hd * (0.035f / 0.175f * hl) + across * (0.03f * hk) - thick * (0.005f * hk);
                V3 tdir = (hd * 0.75f + across * 0.45f - thick * 0.35f).Normalized;
                V3 tax = V3.ProjectOnPlane(thick, tdir).Normalized, taz = V3.Cross(tax, tdir).Normalized;
                for (int r = 0; r < 4; r++)
                {
                    float rr = (0.014f - 0.0015f * r) * hk;
                    tr.Add(new Ring
                    {
                        c = baseT + tdir * (0.018f * r * hk), ax = tax, az = taz, rx = rr, rf = rr * 0.85f, rb = rr * 0.85f, ex = 2f,
                        boneA = HandL + i, boneB = HandL + i, wA = 1f, color = (a, p) => look.skin
                    });
                }
                Loft(m, tr, 7, false, true);
            }
        }

        // ---- Neck ----
        {
            var rings = new List<Ring>();
            V3 a0 = rest.chest + V3.Up * (-0.02f * kb), a1 = rest.neckTop + V3.Up * (0.035f * kb);
            for (int r = 0; r < 5; r++)
            {
                float t = r / 4f;
                V3 c = V3.Lerp(a0, a1, t);
                float rr = (0.066f - 0.012f * t) * w * (look.child ? 0.85f : adultF ? 0.88f : 0.95f + 0.05f * g);
                rings.Add(new Ring
                {
                    c = c, ax = V3.Right, az = V3.Fwd, rx = rr, rf = rr * 0.95f, rb = rr * 1.05f, ex = 2f,
                    boneA = r <= 1 ? Spine : Neck, boneB = r <= 1 ? Neck : Head, wA = r == 0 ? 0.7f : r == 1 ? 0.4f : r == 4 ? 0.4f : 1f,
                    color = (a, p) => look.skin
                });
            }
            Loft(m, rings, aroundLimb, false, false);
        }

        // ---- Head ----
        BuildHead(m, look, rest, kb, k * hs, det);

        m.Normals();
        Shadows(m, rest, kb);
        int vc = m.p.Count / 3;
        body.pos = m.p.ToArray();
        body.nrm = m.n.ToArray();
        body.col = m.c.ToArray();
        body.tri = m.t.ToArray();
        body.b0 = m.b0.ToArray();
        body.b1 = m.b1.ToArray();
        body.w0 = m.w.ToArray();
        // Each corner in its bones' own frames
        body.l0 = new float[vc * 3]; body.l1 = new float[vc * 3]; body.n0 = new float[vc * 3]; body.n1 = new float[vc * 3];
        for (int v = 0; v < vc; v++)
        {
            var p = new V3(body.pos[v * 3], body.pos[v * 3 + 1], body.pos[v * 3 + 2]);
            var n = new V3(body.nrm[v * 3], body.nrm[v * 3 + 1], body.nrm[v * 3 + 2]);
            Local(rf[body.b0[v]], p, n, body.l0, body.n0, v);
            Local(rf[body.b1[v]], p, n, body.l1, body.n1, v);
        }
        return body;
    }


    // Darkens the skin and clothes where light hardly gets in (under the arms, between the legs, under the chin, the
    // inside of the elbows and knees, folds at the waist): a painted-in ambient occlusion
    static void Shadows(Mesher m, Rig rest, float kb)
    {
        int vc = m.p.Count / 3;
        for (int v = 0; v < vc; v++)
        {
            var p = new V3(m.p[v * 3], m.p[v * 3 + 1], m.p[v * 3 + 2]);
            var n = new V3(m.n[v * 3], m.n[v * 3 + 1], m.n[v * 3 + 2]);
            float y = p.y / kb, ax = Math.Abs(p.x) / kb;
            float dark = 0f;
            // Armpits and the sides of the chest facing the arms
            dark += 0.35f * Bell(y - 1.33f, 0.06f) * Smooth(0.1f, 0.165f, ax) * Smooth(0.2f, 0.8f, Math.Abs(n.x)) * (ax < 0.2f ? 1f : 0f);
            // The inner thighs and the crotch
            dark += 0.3f * Smooth(0.92f, 0.78f, y) * Smooth(0.06f, 0.0f, ax) ;
            dark += 0.22f * Smooth(0.95f, 0.6f, y) * Smooth(0.5f, 0.95f, -n.x * Math.Sign(p.x)) * (y > 0.5f ? 1f : 0f);
            // Under the chin, and the neck
            dark += 0.25f * Bell(y - 1.53f, 0.035f) * Smooth(0.08f, 0.0f, ax) * Smooth(-0.2f, 0.6f, n.z) * (p.z > 0f ? 1f : 0f);
            // The insides of the arms (towards the body)
            if (ax > 0.17f && y < 1.38f && y > 0.75f) dark += 0.18f * Smooth(0.3f, 0.95f, -n.x * Math.Sign(p.x));
            // A little darker at the bottom of everything (light comes from above)
            dark += 0.12f * Smooth(0.3f, -0.9f, n.y);
            float f = 1f - Math.Min(0.45f, dark);
            if (f < 0.999f)
            {
                m.c[v * 3] = (byte)(m.c[v * 3] * f); m.c[v * 3 + 1] = (byte)(m.c[v * 3 + 1] * f); m.c[v * 3 + 2] = (byte)(m.c[v * 3 + 2] * f);
            }
        }
    }

    static void Local(Frame f, V3 p, V3 n, float[] l, float[] ln, int v)
    {
        V3 d = p - f.o;
        l[v * 3] = V3.Dot(d, f.x); l[v * 3 + 1] = V3.Dot(d, f.y); l[v * 3 + 2] = V3.Dot(d, f.z);
        ln[v * 3] = V3.Dot(n, f.x); ln[v * 3 + 1] = V3.Dot(n, f.y); ln[v * 3 + 2] = V3.Dot(n, f.z);
    }

    // ---- Colours of the clothes ----

    static TvColor TrunkColor(Look look, float yy, float a, V3 at, float kb, float topEnd)
    {
        if (yy < topEnd)
            return look.legs == 2 ? look.bottom : look.bottom.Shade(yy > topEnd - 0.03f ? 0.85f : 1f);
        TvColor c = look.top;
        float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
        if (look.suit)
        {
            // An orienteering top: darker panels down the sides, a shaped band across the chest, a lighter yoke
            if (Math.Abs(ca) > 0.82f) c = look.top2;
            // A shoulder yoke in the second colour, cut in a V at the front
            float yoke = 1.385f - 0.05f * Math.Max(0f, sa) * (1f - Math.Abs(ca));
            if (yy > yoke) c = look.top2;
        }
        else
        {
            // A collar of skin at the neck for a t-shirt or blouse, a belt at the bottom
            if (yy > 1.47f && sa > 0.2f && look.sleeves < 2) c = look.skin;
            if (yy < topEnd + 0.035f && look.legs != 2) c = c.Shade(0.88f);
        }
        return c;
    }

    static TvColor LegColor(Look look, float yy, float kb)
    {
        float y = yy * kb;
        if (look.legs == 2) return yy < 0.45f && !look.child ? look.bottom.Lerp(look.skin, 0.55f) : look.skin;    // tights under a skirt
        if (look.sockHigh > 0f && y < look.sockHigh && (look.legs == 1 || look.suit)) return look.sock;
        if (look.legs == 1 && yy < 0.66f) return look.skin;
        return look.bottom;
    }

    static TvColor ArmColor(Look look, float d, float upper, float kb)
    {
        if (look.sleeves == 2) return d > upper + 0.23f * kb ? look.skin : look.suit && d > upper + 0.2f * kb ? look.top2 : look.top;
        if (look.sleeves == 1) return d < 0.1f * kb ? (d > 0.085f * kb && look.suit ? look.top2 : look.top) : look.skin;
        return d < -0.03f * kb ? look.top : look.skin;
    }

    static TvColor ShoeColor(Look look, float yy, float a)
    {
        if (yy < 0.016f) return look.sole;
        if (look.suit && yy < 0.05f && Math.Abs(Math.Cos(a)) > 0.6f && yy > 0.024f) return look.sole.Lerp(look.shoe, 0.2f);
        return look.shoe;
    }

    // ---- The head: a skull and face as a smooth distance field, cast out from its middle ----

    struct Ell { public V3 c, r; public float sign; }

    static float EllDist(Ell e, V3 p)
    {
        V3 q = new V3((p.x - e.c.x) / e.r.x, (p.y - e.c.y) / e.r.y, (p.z - e.c.z) / e.r.z);
        float m = Math.Min(e.r.x, Math.Min(e.r.y, e.r.z));
        return (q.Length - 1f) * m;
    }

    static float SMin(float a, float b, float k)
    {
        float h = Math.Max(k - Math.Abs(a - b), 0f) / k;
        return Math.Min(a, b) - h * h * k * 0.25f;
    }

    static float SMax(float a, float b, float k) => -SMin(-a, -b, k);

    // Distance to the head's skin (local: the middle of the head at 0, metres for a grown man)
    static float HeadField(V3 p, bool female, bool child, float jaw)
    {
        float d = EllDist(new Ell { c = new V3(0f, 0.018f, -0.006f), r = new V3(0.076f, 0.097f, 0.098f) }, p);       // skull
        d = SMin(d, EllDist(new Ell { c = new V3(0f, -0.045f, 0.026f), r = new V3(0.052f * jaw, 0.07f, 0.068f) }, p), 0.028f);   // face and jaw
        d = SMin(d, EllDist(new Ell { c = new V3(0f, -0.098f, 0.052f), r = new V3(0.027f * jaw, 0.026f, 0.03f) }, p), 0.025f); // chin
        foreach (float s in new[] { -1f, 1f })
        {
            d = SMin(d, EllDist(new Ell { c = new V3(s * 0.042f, -0.004f, 0.048f), r = new V3(0.022f, 0.019f, 0.03f) }, p), 0.03f);   // cheekbone
            d = SMin(d, EllDist(new Ell { c = new V3(s * 0.04f, -0.068f, 0.018f), r = new V3(0.018f * jaw, 0.028f, 0.038f) }, p), 0.032f); // jaw angle
            // Eye sockets
            d = SMax(d, -EllDist(new Ell { c = new V3(s * 0.032f, 0.012f, 0.1f), r = new V3(0.019f, 0.012f, 0.022f) }, p), 0.012f);
        }
        if (!female && !child) d = SMin(d, EllDist(new Ell { c = new V3(0f, 0.034f, 0.074f), r = new V3(0.058f, 0.014f, 0.022f) }, p), 0.015f);  // brow ridge
        float nose = child ? 0.75f : 1f;
        d = SMin(d, EllDist(new Ell { c = new V3(0f, 0.006f, 0.092f), r = new V3(0.01f, 0.026f * nose, 0.013f) }, p), 0.008f);   // nose bridge
        d = SMin(d, EllDist(new Ell { c = new V3(0f, -0.028f, 0.1f), r = new V3(0.016f, 0.014f, 0.017f * nose) }, p), 0.01f);    // nose tip and wings
        d = SMin(d, EllDist(new Ell { c = new V3(0f, -0.052f, 0.086f), r = new V3(0.022f, 0.011f, 0.011f) }, p), 0.012f);           // mouth
        return d;
    }

    static void BuildHead(Mesher m, Look look, Rig rest, float kb, float hu, int det)
    {
        V3 hc = rest.neckTop + new V3(0f, 0.1f * hu, 0.012f * hu);     // the middle of the head
        bool female = look.female, child = look.child;
        float jaw = female ? 0.9f : child ? 0.88f : 1f;
        int rings = det == 0 ? 14 : det == 1 ? 22 : 40, around = det == 0 ? 18 : det == 1 ? 28 : 52;
        int first = m.p.Count / 3;
        var dirs = new V3[(rings + 1) * around];
        var surf = new V3[(rings + 1) * around];
        for (int i = 0; i <= rings; i++)
        {
            float t = i * 3.14159265f / rings;
            for (int j = 0; j < around; j++)
            {
                float a = j * 6.2831853f / around;
                // Round the vertical axis; a = 0 to the right, pi/2 to the front
                V3 d = new V3((float)(Math.Sin(t) * Math.Cos(a)), (float)Math.Cos(t), (float)(Math.Sin(t) * Math.Sin(a)));
                float lo = 0.01f, hi = 0.2f;
                for (int it = 0; it < 22; it++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (HeadField(d * mid, female, child, jaw) < 0f) lo = mid; else hi = mid;
                }
                V3 local = d * ((lo + hi) * 0.5f);
                dirs[i * around + j] = d;
                surf[i * around + j] = local;
                V3 at = hc + local * hu;
                TvColor col = look.skin;
                // Rosy cheeks, the lips, a beard
                float ch = Bell(local.x - 0.045f, 0.02f) + Bell(local.x + 0.045f, 0.02f);
                if (local.z > 0.04f) col = col.Lerp(TvColor.Hex("#e8a090"), 0.05f * ch * Bell(local.y + 0.02f, 0.02f));
                if (local.z > 0.08f && Math.Abs(local.y + 0.054f) < 0.0075f && Math.Abs(local.x) < 0.021f) col = col.Lerp(TvColor.Hex("#a8545a"), 0.75f);
                if (look.beard && local.y < -0.025f && local.z > -0.02f && !(local.z > 0.08f && Math.Abs(local.y + 0.054f) < 0.01f && Math.Abs(local.x) < 0.024f))
                    col = col.Lerp(look.hair, 0.82f);
                int bone = local.y < -0.06f && local.z < 0.0f ? Neck : Head;
                m.V(at, col, Head, bone, bone == Head ? 1f : 0.6f, hc);
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < around; j++)
            {
                int a = first + i * around + j, b = first + i * around + (j + 1) % around, q = a + around, e = b + around;
                if (i > 0) m.Tri(a, b, q);
                if (i < rings - 1) m.Tri(b, e, q);
            }

        V3 R = V3.Right, U = V3.Up, F = V3.Fwd;
        V3 L(float x, float y, float z) => hc + new V3(x, y, z) * hu;
        // Eyes, irises, brows, ears
        foreach (float s in new[] { -1f, 1f })
        {
            Ellipsoid(m, L(s * 0.032f, 0.011f, 0.081f), R, U, F, 0.0115f * hu, 0.007f * hu, 0.009f * hu, Head, TvColor.Hex("#e6e0d6"), 5, 8);
            Ellipsoid(m, L(s * 0.032f, 0.0105f, 0.0885f), R, U, F, 0.0058f * hu, 0.0058f * hu, 0.0025f * hu, Head,
                      look.hair.r + look.hair.g < 200 ? TvColor.Hex("#3b2c22") : TvColor.Hex("#4c6a80"), 4, 7);
            // The upper lid: a little skin over the top of the eye
            Ellipsoid(m, L(s * 0.032f, 0.017f, 0.0805f), R, U, F, 0.0128f * hu, 0.003f * hu, 0.0096f * hu, Head, look.skin.Shade(0.88f), 3, 8);
            Ellipsoid(m, L(s * 0.034f, 0.031f, 0.088f), R, U, F, 0.019f * hu, 0.0042f * hu, 0.006f * hu, Head, look.hair.Shade(0.85f), 3, 7);
            Ellipsoid(m, L(s * 0.077f, -0.002f, -0.006f), R, U, F, 0.011f * hu, 0.029f * hu, 0.019f * hu, Head, look.skin.Shade(0.95f), 5, 8);
        }

        // ---- Hair ----
        int style = look.hairStyle;
        float thick = style == 1 ? 0.004f : style == 3 ? 0.016f : style == 7 || style == 4 ? 0.011f : 0.008f;
        // Where hair grows on the head (local, metres): the hairline at the forehead, above the ears, down the nape
        bool Grows(V3 p)
        {
            float front = style == 2 ? 0.072f : 0.066f;
            float line = p.z > 0.04f ? front + (Math.Abs(p.x) > 0.04f ? -0.012f : 0f)
                       : p.z > -0.02f ? (Math.Abs(p.x) > 0.06f ? 0.022f : 0.035f)
                       : style == 4 || style == 7 ? -0.08f : -0.055f;
            if ((style == 4 || style == 7) && p.z < 0.05f) line = Math.Abs(p.x) > 0.06f ? -0.06f : line;
            if (p.z > 0.05f && style == 7 && Math.Abs(p.x) > 0.05f) line = -0.04f;
            return p.y > line;
        }
        int hfirst = m.p.Count / 3;
        var index = new int[(rings + 1) * around];
        for (int v = 0; v < index.Length; v++)
        {
            index[v] = -1;
            V3 p = surf[v];
            if (!Grows(p)) continue;
            // Thicker on top, thin at the edges; curls make the top lumpy
            float edge = Smooth(-0.07f, 0.06f, p.y);
            float h = thick * (0.45f + 0.55f * edge);
            if (style == 3) h += 0.006f * (float)Math.Abs(Math.Sin(p.x * 160f) * Math.Sin(p.z * 150f)) * edge;
            if (style == 2) h += 0.008f * Smooth(-0.01f, 0.04f, p.x) * Smooth(0.03f, 0.08f, p.y);
            V3 at = hc + (p + dirs[v] * h) * hu;
            TvColor col = look.hair.Shade(0.92f + 0.12f * edge);
            index[v] = m.V(at, col, Head, Head, 1f, hc);
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < around; j++)
            {
                int a = i * around + j, b = i * around + (j + 1) % around, q = a + around, e = b + around;
                if (index[a] >= 0 && index[b] >= 0 && index[q] >= 0) m.Tri(index[a], index[b], index[q]);
                if (index[b] >= 0 && index[e] >= 0 && index[q] >= 0) m.Tri(index[b], index[e], index[q]);
            }
        // Extras: a ponytail, a bun, long hair down the back
        if (style == 5)
        {
            var rr = new List<Ring>();
            V3 root = L(0f, 0.035f, -0.1f), down = (new V3(0f, -1f, -0.35f)).Normalized;
            for (int r = 0; r < 6; r++)
            {
                float t = r / 5f;
                V3 c = root + down * (0.2f * t * hu) + new V3(0f, 0f, -0.02f * (float)Math.Sin(t * 3f) * hu);
                float rad = (0.022f + 0.012f * (float)Math.Sin(t * 3.14159f) - 0.014f * t) * hu;
                rr.Add(new Ring
                {
                    c = c, ax = V3.Right, az = V3.Fwd, rx = rad, rf = rad, rb = rad, ex = 2f,
                    boneA = Head, boneB = Neck, wA = 1f - 0.5f * t, color = (a, p) => look.hair
                });
            }
            Loft(m, rr, 8, true, true);
        }
        else if (style == 6)
            Ellipsoid(m, L(0f, 0.1f, -0.045f), R, U, F, 0.035f * hu, 0.03f * hu, 0.033f * hu, Head, look.hair, 5, 9);
        else if (style == 4)
        {
            var rr = new List<Ring>();
            for (int r = 0; r < 5; r++)
            {
                float t = r / 4f;
                rr.Add(new Ring
                {
                    c = L(0f, -0.02f - 0.17f * t, -0.04f - 0.035f * t), ax = V3.Right, az = V3.Fwd,
                    rx = (0.08f + 0.012f * t) * hu, rf = (0.05f - 0.03f * t) * hu, rb = (0.055f - 0.03f * t) * hu, ex = 2.2f,
                    boneA = Head, boneB = Neck, wA = 1f - 0.7f * t, color = (a, p) => look.hair.Shade(0.95f)
                });
            }
            Loft(m, rr, 12, false, true);
        }
    }

    // ---- Posing ----

    // The body's corners and normals in a pose (as a TvShape, for meshes made once)
    public static TvShape Skin(Body body, Rig rig)
    {
        Frame[] f = Frames(rig);
        int vc = body.VertexCount;
        var pos = new float[vc * 3];
        var nrm = new float[vc * 3];
        for (int v = 0; v < vc; v++)
        {
            Frame a = f[body.b0[v]], b = f[body.b1[v]];
            float wa = body.w0[v], wb = 1f - wa;
            int i = v * 3;
            V3 pa = a.o + a.x * body.l0[i] + a.y * body.l0[i + 1] + a.z * body.l0[i + 2];
            V3 na = a.x * body.n0[i] + a.y * body.n0[i + 1] + a.z * body.n0[i + 2];
            V3 p = pa, n = na;
            if (wb > 1e-4f)
            {
                V3 pb = b.o + b.x * body.l1[i] + b.y * body.l1[i + 1] + b.z * body.l1[i + 2];
                V3 nb = b.x * body.n1[i] + b.y * body.n1[i + 1] + b.z * body.n1[i + 2];
                p = pa * wa + pb * wb;
                n = (na * wa + nb * wb).Normalized;
            }
            pos[i] = p.x; pos[i + 1] = p.y; pos[i + 2] = p.z;
            nrm[i] = n.x; nrm[i + 1] = n.y; nrm[i + 2] = n.z;
        }
        return new TvShape { positions = pos, normals = nrm, colors = (byte[])body.col.Clone(), triangles = (int[])body.tri.Clone() };
    }

    // Turns a shape half round (to face -z, as the town's shapes do)
    public static void TurnAround(TvShape s)
    {
        for (int i = 0; i < s.positions.Length; i += 3)
        {
            s.positions[i] = -s.positions[i]; s.positions[i + 2] = -s.positions[i + 2];
            s.normals[i] = -s.normals[i]; s.normals[i + 2] = -s.normals[i + 2];
        }
    }

    // Two shapes as one
    public static TvShape Join(TvShape a, TvShape b)
    {
        if (b == null || b.positions.Length == 0) return a;
        int va = a.positions.Length / 3;
        var tri = new int[a.triangles.Length + b.triangles.Length];
        Array.Copy(a.triangles, tri, a.triangles.Length);
        for (int i = 0; i < b.triangles.Length; i++) tri[a.triangles.Length + i] = b.triangles[i] + va;
        float[] Cat(float[] x, float[] y) { var r = new float[x.Length + y.Length]; Array.Copy(x, r, x.Length); Array.Copy(y, 0, r, x.Length, y.Length); return r; }
        var col = new byte[a.colors.Length + b.colors.Length];
        Array.Copy(a.colors, col, a.colors.Length); Array.Copy(b.colors, 0, col, a.colors.Length, b.colors.Length);
        return new TvShape { positions = Cat(a.positions, b.positions), normals = Cat(a.normals, b.normals), colors = col, triangles = tri };
    }

    // ---- Looks ----

    static readonly TvColor[] RunnerSkins =
        { TvColor.Hex("#f0c8a4"), TvColor.Hex("#e6b590"), TvColor.Hex("#dba882"), TvColor.Hex("#eec2a2"), TvColor.Hex("#cf9670"), TvColor.Hex("#a8754f"), TvColor.Hex("#e9bc96") };
    static readonly TvColor[] RunnerHairs =
        { TvColor.Hex("#4a3324"), TvColor.Hex("#6b4a2e"), TvColor.Hex("#c9a35a"), TvColor.Hex("#2a2420"), TvColor.Hex("#8a623c"), TvColor.Hex("#d8bd7e"), TvColor.Hex("#7a3e24") };
    static readonly TvColor[] Shoes =
        { TvColor.Hex("#e8e6e0"), TvColor.Hex("#2a2c30"), TvColor.Hex("#3a5a8a"), TvColor.Hex("#8a8f96"), TvColor.Hex("#c8d23a"), TvColor.Hex("#d84a2a") };
    static readonly TvColor[] Soles =
        { TvColor.Hex("#e8742a"), TvColor.Hex("#2b2b2b"), TvColor.Hex("#f2d23a"), TvColor.Hex("#4aa84a"), TvColor.Hex("#e8e6e0") };

    // An orienteer in a club's colours: a patterned suit top, long O-trousers (or shorts and long socks), studded shoes
    public static Look RunnerLook(Random r, TvColor team, int detail = 2)
    {
        var l = new Look { suit = true, detail = detail };
        l.female = r.NextDouble() < 0.4;
        l.height = l.female ? 1.6f + (float)r.NextDouble() * 0.15f : 1.71f + (float)r.NextDouble() * 0.16f;
        l.girth = 0.84f + (float)r.NextDouble() * 0.2f;
        l.skin = RunnerSkins[r.Next(RunnerSkins.Length)];
        l.hair = RunnerHairs[r.Next(RunnerHairs.Length)];
        l.top = team;
        float lum = (team.r * 0.3f + team.g * 0.59f + team.b * 0.11f) / 255f;
        l.top2 = lum > 0.55f ? team.Shade(0.55f) : r.NextDouble() < 0.5 ? team.Shade(0.55f) : team.Lerp(new TvColor(245, 245, 240), 0.55f);
        l.sleeves = r.NextDouble() < 0.7 ? 2 : 1;
        if (r.NextDouble() < 0.7)
        {
            l.legs = 0;
            l.bottom = r.NextDouble() < 0.6 ? team.Shade(0.45f) : TvColor.Hex("#23262c");
        }
        else
        {
            l.legs = 1;
            l.bottom = r.NextDouble() < 0.5 ? team.Shade(0.45f) : TvColor.Hex("#23262c");
            l.sockHigh = 0.44f * (l.height / 1.78f);
            l.sock = r.NextDouble() < 0.5 ? team : TvColor.Hex("#2b2e36");
        }
        l.shoe = Shoes[r.Next(Shoes.Length)];
        l.sole = Soles[r.Next(Soles.Length)];
        double h = r.NextDouble();
        l.hairStyle = l.female ? (h < 0.6 ? 5 : h < 0.8 ? 6 : h < 0.9 ? 7 : 0) : (h < 0.4 ? 0 : h < 0.65 ? 1 : h < 0.85 ? 2 : 3);
        l.beard = !l.female && r.NextDouble() < 0.15;
        return l;
    }
}
