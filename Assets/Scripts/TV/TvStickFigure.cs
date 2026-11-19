using System.Collections.Generic;
using UnityEngine;

// A runner in the 3D view of a TV control: a rounded orienteer in the team's colours. Smooth tapered limbs joined by
// round joints, a loose nylon top and long nylon trousers, shoes, a number bib, a close-fitting cap of hair and a paper
// map held up in one hand.
// Each runner gets one of a few running styles (high knees, bouncy, arms flapping ...), its skin and hair colour picked
// from its name, so the same runner always looks and runs the same way. Near the control the runner slows its stride
// and reaches for the punching unit with the other hand. (Named "stick figure" from its first version.)
public class TvStickFigure
{
    public struct Style
    {
        public float cad, stride, knee, bounce, arm, flare, lean, elbow;
    }

    static readonly Style[] Styles =
    {
        new Style { cad = 1f, stride = 0.74f, knee = 1.35f, bounce = 0.6f, arm = 0.75f, flare = 0.1f, lean = 0.2f, elbow = 1.55f },
        new Style { cad = 1.1f, stride = 0.62f, knee = 1.1f, bounce = 1.9f, arm = 0.5f, flare = 0.05f, lean = 0.1f, elbow = 1.75f },
        new Style { cad = 0.93f, stride = 0.86f, knee = 1.5f, bounce = 0.9f, arm = 1.15f, flare = 0.9f, lean = 0.3f, elbow = 1.2f },
        new Style { cad = 1.04f, stride = 0.7f, knee = 1.25f, bounce = 0.4f, arm = 0.6f, flare = 0.3f, lean = 0.24f, elbow = 1.6f },
        new Style { cad = 0.98f, stride = 0.8f, knee = 1.6f, bounce = 0.7f, arm = 0.9f, flare = 0.45f, lean = 0.34f, elbow = 1.4f }
    };

    static readonly Color32[] Skins =
    {
        new Color32(0xe0, 0xb1, 0x90, 255), new Color32(0xd9, 0xa5, 0x82, 255), new Color32(0xe6, 0xbc, 0x9a, 255),
        new Color32(0xcc, 0x95, 0x70, 255), new Color32(0xdb, 0xab, 0x88, 255), new Color32(0xa8, 0x72, 0x4e, 255)
    };
    static readonly Color32[] Hairs =
    {
        new Color32(0x4a, 0x33, 0x24, 255), new Color32(0x6b, 0x4a, 0x2e, 255), new Color32(0xc9, 0xa3, 0x5a, 255),
        new Color32(0x2a, 0x24, 0x20, 255), new Color32(0x8a, 0x62, 0x3c, 255), new Color32(0xd8, 0xbd, 0x7e, 255),
        new Color32(0x7a, 0x3e, 0x24, 255)
    };

    // ---- Shared meshes and materials ----

    static readonly Dictionary<string, Mesh> tubes = new Dictionary<string, Mesh>();
    static Mesh ball, cap, mapMesh, bibMesh;
    static Material darkMaterial, shoeMaterial, soleMaterial, paintedMaterial;

    // ---- This runner ----

    readonly GameObject root;
    readonly Material jersey, trousers, skin, hair;
    readonly Style style;
    readonly List<Material> own = new List<Material>();

    // Body parts. Limbs are tubes from one joint to the next (scaled in length only); joints are balls.
    readonly Transform pelvis, seat, torso, shoulders, bib, neck, head, hairCap, map;
    readonly Leg[] legs = new Leg[2];
    readonly Arm[] arms = new Arm[2];

    class Leg
    {
        public Transform hip, thigh, knee, shin, ankle, shoe, sole;
    }

    class Arm
    {
        public Transform shoulder, sleeve, upper, elbow, fore, hand;
    }

    public float phase;
    public Vector3 forward = Vector3.forward;
    public Vector3 position;
    public bool placed;
    // Going up to the flag to punch (TvWorldView.ToThePunch): 0 = on its route, 1 = to the flag, 2 = back to the route
    public int punch;
    public bool punched, tracked, finishing;
    public bool mayPunch = true;   // false while it only passes the control on the way to another one
    public bool everPunched;    // has touched the punching unit on this pass (for the split time on TV)
    public float shownX, shownZ, realX, realZ, headX, headZ, punchedAt, swerveX, swerveZ;
    public float nearest = float.MaxValue;
    float pace = 4f;
    // Map reading: a runner looks at the map now and then (and on the way in to a control), and in between runs with the
    // map hand swinging like the other one
    float readEvery, readFor, readClock, read = 1f;
    System.Random mapRandom;
    public float Pace => pace;
    public float slope, hop, hopHeight = 0.4f;     // smoothed: the ground's rise ahead, and how far into a jump over a trunk
    // After the finish: tired (0..1, bent over with the hands on the knees getting the breath back) and walking (0..1)
    public float tired, walk;
    public int readMode = -1;        // -1 reads the map now and then by itself; 1 reading (the player holds it up); 0 not
    public float leaveX, leaveZ;     // the way it walks off after the finish
    // The map stays in one hand (the left for most runners); the other hand punches. Only when the unit is on the
    // map hand's side does the runner take the map over to the other hand for the punch.
    public int mapHand;              // -1 left, 1 right (run it yourself: the player's own choice)
    bool swapped;
    float still, clock;

    public TvStickFigure(Transform parent, string name, Color color, int seed)
    {
        MakeShared();
        root = new GameObject("Runner " + name);
        root.transform.SetParent(parent, false);
        var random = new System.Random(seed);
        Style s = Styles[random.Next(Styles.Length)];
        // A little of each runner's own
        float J() => 0.85f + (float)random.NextDouble() * 0.3f;
        s.cad *= J(); s.stride *= J(); s.knee *= J(); s.arm *= J() * 1.2f; s.lean *= J(); s.flare *= J();
        style = s;
        readEvery = 3f + (float)random.NextDouble() * 4f;
        readFor = 1.2f + (float)random.NextDouble() * 1.8f;
        readClock = (float)random.NextDouble() * readEvery;
        mapRandom = random;
        mapHand = random.NextDouble() < 0.85 ? -1 : 1;
        phase = (float)random.NextDouble() * 6.28f;

        jersey = Own(color);
        skin = Own(Skins[random.Next(Skins.Length)]);
        hair = Own(Hairs[random.Next(Hairs.Length)]);
        // Long nylon trousers: the team's colour, a little darker, or black
        trousers = random.NextDouble() < 0.7 ? Own(Color.Lerp(color, Color.black, 0.5f)) : darkMaterial;

        pelvis = Part("Trousers top", Tube(0.125f, 0.12f, 0.7f), trousers);
        seat = Part("Seat", ball, trousers);
        // A loose nylon top: wide, hanging a little over the waist
        torso = Part("Top", Tube(0.15f, 0.165f, 0.62f), jersey);
        shoulders = Part("Shoulders", cap, jersey);
        bib = Part("Bib", bibMesh, paintedMaterial);
        neck = Part("Neck", Tube(0.055f, 0.05f, 1f), skin);
        head = Part("Head", ball, skin);
        hairCap = Part("Hair", cap, hair);
        map = Part("Map", mapMesh, paintedMaterial);
        for (int i = 0; i < 2; i++)
        {
            legs[i] = new Leg
            {
                hip = Part("Hip", ball, trousers),
                thigh = Part("Thigh", Tube(0.075f, 0.064f, 1f), trousers),
                knee = Part("Knee", ball, trousers),
                shin = Part("Shin", Tube(0.06f, 0.05f, 1f), trousers),
                ankle = Part("Ankle", ball, trousers),
                shoe = Part("Shoe", ball, shoeMaterial),
                sole = Part("Sole", ball, soleMaterial)
            };
            arms[i] = new Arm
            {
                shoulder = Part("Shoulder", ball, jersey),
                sleeve = Part("Sleeve", Tube(0.06f, 0.055f, 1f), jersey),
                upper = Part("Upper arm", Tube(0.047f, 0.042f, 1f), skin),
                elbow = Part("Elbow", ball, skin),
                fore = Part("Forearm", Tube(0.042f, 0.032f, 1f), skin),
                hand = Part("Hand", ball, skin)
            };
        }
    }

    Material Own(Color color)
    {
        Material m = TvWorldView.NewMaterial(color);
        own.Add(m);
        return m;
    }

    Transform Part(string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go.transform;
    }

    public void SetVisible(bool visible)
    {
        if (root.activeSelf != visible) root.SetActive(visible);
    }

    public void Destroy()
    {
        Object.Destroy(root);
        foreach (Material m in own) Object.Destroy(m);
    }

    // ---- Meshes, made once ----

    static void MakeShared()
    {
        if (ball != null) return;
        ball = Sphere(1f, "Ball");
        cap = Sphere(0.56f, "Cap");
        darkMaterial = TvWorldView.NewMaterial(new Color32(0x23, 0x27, 0x2e, 255));
        shoeMaterial = TvWorldView.NewMaterial(new Color32(0xf2, 0xf2, 0xee, 255));
        soleMaterial = TvWorldView.NewMaterial(new Color32(0xe8, 0x74, 0x2a, 255));
        paintedMaterial = TvWorldView.NewMaterial(Color.white);   // the colours are in the mesh

        // The map: a sheet of paper with yellow open land, green thickets, brown contours and the purple course
        var b = new TvShapeBuilder(new TvRandom(5));
        b.Box(-0.085f, -0.002f, -0.11f, 0.085f, 0.002f, 0.11f, TvColor.Hex("#f6f4ec"));
        b.Box(-0.075f, 0.002f, -0.09f, -0.01f, 0.003f, -0.03f, TvColor.Hex("#f6c54a"));
        b.Box(0.015f, 0.002f, 0.0f, 0.07f, 0.003f, 0.08f, TvColor.Hex("#8cc96a"));
        b.Box(-0.07f, 0.002f, 0.05f, -0.03f, 0.003f, 0.09f, TvColor.Hex("#8cc96a"));
        b.Box(0.03f, 0.002f, -0.095f, 0.075f, 0.003f, -0.045f, TvColor.Hex("#f6c54a"));
        b.Box(-0.02f, 0.003f, -0.06f, -0.016f, 0.0035f, 0.04f, TvColor.Hex("#b06a2a"));
        b.Box(0.012f, 0.003f, -0.08f, 0.016f, 0.0035f, -0.01f, TvColor.Hex("#b06a2a"));
        b.Box(-0.004f, 0.0035f, -0.07f, 0.002f, 0.0042f, 0.06f, TvColor.Hex("#a8327c"));
        b.Box(-0.02f, 0.0035f, 0.05f, 0.02f, 0.0042f, 0.056f, TvColor.Hex("#a8327c"));
        mapMesh = TvWorldView.ToMesh(b.Done(), "Map");

        // The number bib: white with a dark number
        b = new TvShapeBuilder(new TvRandom(6));
        b.Box(-0.085f, -0.06f, -0.005f, 0.085f, 0.06f, 0.005f, TvColor.Hex("#f4f2ea"));
        b.Box(-0.05f, -0.012f, 0.005f, 0.05f, 0.012f, 0.008f, TvColor.Hex("#1f3d2b"));
        b.Box(-0.07f, 0.035f, 0.005f, 0.07f, 0.045f, 0.008f, TvColor.Hex("#e8742a"));
        bibMesh = TvWorldView.ToMesh(b.Done(), "Bib");
    }

    // A smooth tube from y = 0 (radius r0) to y = 1 (radius r1), flattened front to back by depth, scaled in length
    // when it is placed
    static Mesh Tube(float r0, float r1, float depth)
    {
        string key = r0.ToString("0.000") + "/" + r1.ToString("0.000") + "/" + depth.ToString("0.00");
        if (tubes.TryGetValue(key, out Mesh made)) return made;
        const int Sides = 14;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= Sides; i++)
        {
            float a = i / (float)Sides * Mathf.PI * 2f, c = Mathf.Cos(a), s = Mathf.Sin(a);
            Vector3 n = new Vector3(c, 0f, s * depth).normalized;
            vertices.Add(new Vector3(c * r0, 0f, s * r0 * depth));
            vertices.Add(new Vector3(c * r1, 1f, s * r1 * depth));
            normals.Add(n);
            normals.Add(n);
        }
        for (int i = 0; i < Sides; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            Tri(vertices, normals, triangles, a, b, c);
            Tri(vertices, normals, triangles, b, d, c);
        }
        made = Build("Tube " + key, vertices, normals, triangles);
        tubes[key] = made;
        return made;
    }

    // A smooth ball of radius 1; share < 1 keeps only the top of it (a cap, from the top down to share x 180 degrees)
    static Mesh Sphere(float share, string name)
    {
        const int Rings = 12, Around = 18;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= Rings; i++)
        {
            float t = i / (float)Rings * Mathf.PI * share;
            for (int j = 0; j <= Around; j++)
            {
                float p = j / (float)Around * Mathf.PI * 2f;
                Vector3 v = new Vector3(Mathf.Sin(t) * Mathf.Cos(p), Mathf.Cos(t), Mathf.Sin(t) * Mathf.Sin(p));
                vertices.Add(v);
                normals.Add(v);
            }
        }
        for (int i = 0; i < Rings; i++)
        {
            for (int j = 0; j < Around; j++)
            {
                int a = i * (Around + 1) + j, b = a + 1, c = a + Around + 1, d = c + 1;
                if (i > 0) Tri(vertices, normals, triangles, a, b, c);
                Tri(vertices, normals, triangles, b, d, c);
            }
        }
        return Build(name, vertices, normals, triangles);
    }

    // A triangle turned to face outwards (Unity draws the side where the corners run clockwise)
    static void Tri(List<Vector3> v, List<Vector3> n, List<int> t, int a, int b, int c)
    {
        Vector3 face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (Vector3.Dot(face, n[a] + n[b] + n[c]) < 0f) { int x = b; b = c; c = x; }
        t.Add(a);
        t.Add(b);
        t.Add(c);
    }

    static Mesh Build(string name, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
    {
        var colors = new Color32[vertices.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.colors32 = colors;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---- Placing the parts ----

    // A tube from a to b
    static void Segment(Transform t, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float length = d.magnitude;
        t.localPosition = a;
        t.localRotation = length > 1e-5f ? Quaternion.FromToRotation(Vector3.up, d / length) : Quaternion.identity;
        t.localScale = new Vector3(1f, Mathf.Max(1e-4f, length), 1f);
    }

    // A tube from a to b whose flat side faces forward
    static void Segment(Transform t, Vector3 a, Vector3 b, Vector3 front)
    {
        Vector3 d = b - a;
        float length = d.magnitude;
        Vector3 up = length > 1e-5f ? d / length : Vector3.up;
        t.localPosition = a;
        t.localRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(front, up), up);
        t.localScale = new Vector3(1f, Mathf.Max(1e-4f, length), 1f);
    }

    static void Ball(Transform t, Vector3 at, float r)
    {
        t.localPosition = at;
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(r, r, r);
    }

    static void Ball(Transform t, Vector3 at, Quaternion turn, Vector3 size)
    {
        t.localPosition = at;
        t.localRotation = turn;
        t.localScale = size;
    }

    // Blends an arm towards a hand position, bending the elbow outwards and keeping the arm its own length
    static void Reach(Vector3 shoulder, Vector3 target, int side, Vector3 right, float share, ref Vector3 elbow, ref Vector3 hand)
    {
        Vector3 d = target - shoulder;
        float dist = Mathf.Min(d.magnitude, 0.55f);
        d.Normalize();
        Vector3 to = shoulder + d * dist;
        Vector3 bend = Vector3.ProjectOnPlane(right * side + Vector3.down * 0.3f, d).normalized;
        float half = dist * 0.52f;
        Vector3 mid = shoulder + d * half + bend * Mathf.Sqrt(Mathf.Max(0f, 0.0841f - half * half));
        elbow = Vector3.Lerp(elbow, mid, share);
        hand = Vector3.Lerp(hand, to, share);
    }

    // Moves the runner (on the ground at x, z) by the distance it ran since the last frame and poses it.
    // ground: the ground's height there; unit: the punching unit; near: 0 far from the control, 1 at it.
    // slope: how much the ground rises per metre ahead (negative downhill); hop: 0..1 through a jump over a fallen trunk.
    public void Pose(float x, float ground, float z, float moved, Vector3 unit, float near, float dt = 0f, float slope = 0f, float hop = 0f, float hurdle = 0.4f)
    {
        Style st = style;
        // Hills: shorter steps with high knees and a forward lean uphill, longer steps leaning back downhill.
        // Steep ground and cliffs: hands to the ground going up, a deep crouch going down.
        float up = Mathf.Clamp(slope, -1.3f, 1.3f);
        float climb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.95f, up));
        float crouch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.5f, -0.95f, up));
        float uphill = Mathf.Clamp01(up / 0.45f), downhill = Mathf.Clamp01(-up / 0.45f);
        st.stride *= 1f - 0.3f * uphill + 0.12f * downhill * (1f - crouch) - 0.3f * climb - 0.45f * crouch;
        st.knee *= 1f + 0.35f * uphill - 0.25f * downhill;
        st.bounce *= 1f - 0.7f * Mathf.Max(climb, crouch);
        // Stride and rhythm follow the speed, as for a real runner: the rhythm changes only a little (about 75 double
        // steps a minute slow, 95 fast) and the length of the step makes up the rest. A fast runner takes long strides
        // (not quick little steps that look sped up); a slow one (thick forest, marsh, uphill) short choppy steps at
        // nearly the same rhythm, which looks hard going rather than slow motion.
        if (dt > 1e-5f) pace = Mathf.Lerp(pace, moved / dt, Mathf.Clamp01(dt * 9f));
        float cadence = Mathf.Clamp(1.12f + 0.07f * pace, 1.18f, 1.62f) * st.cad * (1f - 0.22f * walk);
        float cycle = Mathf.Max(pace, 0.3f) / cadence;            // metres per double step
        float stepShare = Mathf.Clamp(cycle / 2.8f, 0.3f, 1.45f);
        float effort = Mathf.InverseLerp(0.3f, 1.3f, stepShare);  // 0 short choppy steps .. 1 full flight
        st.stride = Mathf.Min(st.stride * stepShare, 1.0f);
        st.knee *= Mathf.Lerp(0.62f, 1.12f, effort) * (1f - 0.45f * walk);
        st.arm *= Mathf.Lerp(0.72f, 1.1f, effort) * (1f - 0.45f * walk);
        st.bounce *= Mathf.Lerp(0.55f, 1.15f, effort) * (1f - 0.85f * walk);
        if (dt > 1e-5f && pace > 0.4f) phase += dt * cadence * 6.2831853f;
        else phase += moved / 0.84f * 6.2831853f;
        clock += dt;
        position = new Vector3(x, ground, z);
        Vector3 fwd = forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 P(float f, float u, float s) => position + fwd * f + Vector3.up * u + right * s;

        float ph = phase;
        readClock += dt;
        if (readClock > readEvery + readFor)
        {
            readClock = 0f;
            readEvery = 3f + (float)mapRandom.NextDouble() * 5f;
            readFor = 1f + (float)mapRandom.NextDouble() * 2f;
        }
        // Standing still (and not at the unit, not done for): both hands on the map, reading it carefully
        bool standing = pace < 0.7f && near < 0.05f && tired < 0.05f && walk < 0.05f && hop < 0.05f;
        still = Mathf.MoveTowards(still, standing ? 1f : 0f, dt * 2.5f);
        bool reading = readMode >= 0 ? readMode == 1 && tired < 0.3f
                     : (readClock > readEvery || near > 0.05f || climb > 0.3f || standing) && tired < 0.3f && walk < 0.5f;
        read = Mathf.MoveTowards(read, reading ? 1f : 0f, dt * 3f);
        float readNow = Mathf.SmoothStep(0f, 1f, read);
        float stillNow = Mathf.SmoothStep(0f, 1f, still);
        float lean = st.lean + near * 0.25f + 0.2f * uphill + 0.5f * climb - 0.16f * downhill * (1f - crouch) + 0.05f * crouch
                     + 0.07f * (1f - effort) * (1f - walk) - 0.12f * walk - 0.1f * stillNow;
        // Bent over getting the breath back, the back rising and falling with each breath
        lean = Mathf.Lerp(lean, 0.75f + 0.05f * Mathf.Sin(clock * 6f), tired);

        // Legs: the angle of the thigh and of the shin, from straight down, forward positive
        float low = 0f;
        var knee = new float[2, 2];
        var foot = new float[2, 2];
        var shinAngle = new float[2];
        for (int k = 0; k < 2; k++)
        {
            float p = ph + (k == 1 ? Mathf.PI : 0f);
            // Thighs come forward and knees bend in a crouch, on a steep climb and in the air over a trunk
            // Over a trunk the leading leg reaches ahead and the trailing one tucks up
            float lead = Mathf.Sin(p) > 0f ? 1f : 0f;
            float th = st.stride * (1f - near * 0.5f) * Mathf.Sin(p) + 0.12f + 0.55f * crouch + 0.3f * climb + hop * (0.65f * lead + 0.15f);
            float kn = 0.22f + st.knee * Mathf.Max(0f, Mathf.Cos(p + 0.55f)) + 1.05f * crouch + 0.35f * climb + hop * (1.3f - 0.9f * lead);
            float kx = 0.45f * Mathf.Sin(th), ky = -0.45f * Mathf.Cos(th), a2 = th - kn;
            knee[k, 0] = kx; knee[k, 1] = ky;
            foot[k, 0] = kx + 0.45f * Mathf.Sin(a2); foot[k, 1] = ky - 0.45f * Mathf.Cos(a2);
            shinAngle[k] = a2;
            // On a slope the ground under a foot ahead of the body is higher (or lower)
            low = Mathf.Min(low, foot[k, 1] - up * foot[k, 0]);
        }
        float hipY = -low + 0.09f + st.bounce * 0.055f * (0.5f + 0.5f * Mathf.Cos(2f * ph)) * (1f - near * 0.6f) + Mathf.Clamp(hurdle * 1.05f, 0.42f, 0.95f) * hop;

        // The body
        Vector3 bodyUp = (fwd * Mathf.Sin(lean) + Vector3.up * Mathf.Cos(lean)).normalized;
        Vector3 front = Vector3.Cross(right, bodyUp);
        Vector3 hip = P(0f, hipY, 0f);
        Vector3 waist = hip + Vector3.up * 0.04f;
        Vector3 chest = waist + bodyUp * 0.5f;
        Quaternion bodyTurn = Quaternion.LookRotation(front, bodyUp);
        Segment(pelvis, hip, waist + bodyUp * 0.08f, front);
        Ball(seat, hip, bodyTurn, new Vector3(0.135f, 0.07f, 0.095f));
        // The top hangs a little below the waist, so the trousers never show through it
        Segment(torso, waist - bodyUp * 0.09f, chest, front);
        Ball(shoulders, chest, bodyTurn, new Vector3(0.165f, 0.075f, 0.102f));
        bib.localPosition = waist + bodyUp * 0.28f + front * 0.098f;
        bib.localRotation = bodyTurn;
        bib.localScale = Vector3.one;
        Vector3 neckTop = chest + bodyUp * 0.06f;
        Segment(neck, chest - bodyUp * 0.03f, neckTop);
        Vector3 headPos = neckTop + Vector3.up * 0.095f + fwd * 0.025f;
        // Head bowed to the map while reading
        Quaternion headTurn = Quaternion.LookRotation(fwd) * Quaternion.Euler(14f * readNow + 8f * stillNow - 30f * tired, 0f, 0f);
        Vector3 headSize = new Vector3(0.103f, 0.121f, 0.112f);
        Ball(head, headPos, headTurn, headSize);
        // Hair: a cap that follows the head, tipped back so it covers the back of the head and stops above the forehead
        Ball(hairCap, headPos, headTurn * Quaternion.Euler(-22f, 0f, 0f), headSize * 1.06f);

        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? 0.085f : -0.085f;
            Leg leg = legs[k];
            Vector3 h = P(0f, hipY, s), kn = P(knee[k, 0], hipY + knee[k, 1], s), an = P(foot[k, 0], hipY + foot[k, 1], s);
            Ball(leg.hip, h, 0.072f);
            Segment(leg.thigh, h, kn);
            Ball(leg.knee, kn, 0.063f);
            Segment(leg.shin, kn, an);
            Ball(leg.ankle, an, 0.05f);
            // The foot: flat on the ground under the body, toes pointing down when the leg swings back
            Quaternion footTurn = Quaternion.LookRotation(fwd) * Quaternion.Euler(Mathf.Clamp(-shinAngle[k] * Mathf.Rad2Deg * 0.5f, -25f, 45f), 0f, 0f);
            Ball(leg.shoe, an + Vector3.up * -0.035f + footTurn * new Vector3(0f, 0f, 0.06f), footTurn, new Vector3(0.05f, 0.042f, 0.13f));
            Ball(leg.sole, an + Vector3.up * -0.065f + footTurn * new Vector3(0f, 0f, 0.06f), footTurn, new Vector3(0.051f, 0.016f, 0.135f));
        }

        // Arms: one hand reaches for the punching unit near the control, the other holds the map up to read
        float sf = Mathf.Sin(lean) * 0.47f, su = hipY + 0.04f + Mathf.Cos(lean) * 0.47f;
        int unitSide = Vector3.Dot(unit - position, right) > 0f ? 1 : -1;
        if (near <= 0.001f) swapped = false;
        else if (near > 0.15f && unitSide == mapHand && !swapped) swapped = true;   // stays swapped until it has left the unit
        int mapSide = swapped ? -mapHand : mapHand;
        int punchSide = -mapSide;
        Vector3 mapHandAt = Vector3.zero;
        // The map hand first, so the free hand knows where the map is
        for (int i = 0; i < 2; i++)
        {
            int side = i == 0 ? mapSide : -mapSide;
            int k = side == 1 ? 0 : 1;
            Arm arm = arms[k];
            float p = ph + (side < 0 ? Mathf.PI : 0f);
            float a = -st.arm * Mathf.Sin(p) * 0.9f, el = st.elbow + 0.25f * Mathf.Sin(p), fl = st.flare * 0.16f * (1f + 0.6f * Mathf.Sin(p + 1f));
            float handIn = 0.16f + fl * 0.4f - 0.05f;
            if (side == mapSide)
            {
                // Reading: the map hand held up in front, moving only a little with the stride.
                // Otherwise it swings with the stride like the other arm, a little less.
                a = Mathf.Lerp(a * 0.8f, 0.35f + 0.08f * Mathf.Sin(p), readNow);
                el = Mathf.Lerp(el, 1.55f, readNow);
                fl = Mathf.Lerp(fl, 0.02f, readNow);
                handIn = Mathf.Lerp(handIn, 0.08f, readNow);
                // Standing: the map lower and nearer the middle, held in both hands
                a = Mathf.Lerp(a, 0.3f, stillNow);
                el = Mathf.Lerp(el, 1.35f, stillNow);
                handIn = Mathf.Lerp(handIn, 0.03f, stillNow);
            }
            Vector3 shoulder = P(sf - Mathf.Sin(p) * 0.02f * side, su, 0.17f * side);
            Vector3 elbow = P(sf + 0.29f * Mathf.Sin(a), su - 0.29f * Mathf.Cos(a), (0.18f + fl) * side);
            Vector3 hand = P(sf + 0.29f * Mathf.Sin(a) + 0.27f * Mathf.Sin(a + el), su - 0.29f * Mathf.Cos(a) - 0.27f * Mathf.Cos(a + el), handIn * side);
            if (climb > 0f)
            {
                // Hands to the ground ahead, one after the other
                float f = 0.4f + 0.1f * Mathf.Sin(p), lift = 0.1f * Mathf.Max(0f, Mathf.Sin(p));
                Reach(shoulder, P(f, up * f + 0.05f + lift, 0.19f * side), side, right, climb, ref elbow, ref hand);
            }
            if (crouch > 0f)
            {
                // Arms out for balance, the downhill hand low and back ready to catch a slip
                Vector3 to = side == mapSide ? P(0.3f, hipY + 0.25f, 0.4f * side) : P(-0.15f, hipY - 0.05f, 0.42f * side);
                Reach(shoulder, to, side, right, crouch * 0.85f, ref elbow, ref hand);
            }
            if (hop > 0f)
            {
                // Arms up and out in the air
                hand += (Vector3.up * 0.12f + right * 0.08f * side) * hop;
                elbow += right * 0.06f * side * hop;
            }
            if (tired > 0f)
            {
                // Hands on the knees
                Reach(shoulder, P(0.24f, hipY - 0.2f, 0.11f * side), side, right, tired, ref elbow, ref hand);
            }
            if (side != mapSide && stillNow > 0f)
            {
                // The free hand on the other edge of the map
                Reach(shoulder, mapHandAt + right * (0.15f * side) + Vector3.up * 0.01f, side, right, stillNow, ref elbow, ref hand);
            }
            if (side == mapSide) mapHandAt = hand;
            if (side == punchSide && near > 0f)
            {
                Vector3 target = unit + Vector3.up * 0.05f, d = target - shoulder;
                float dist = Mathf.Min(d.magnitude, 0.6f);
                d.Normalize();
                Vector3 reach = shoulder + d * dist, mid = shoulder + d * dist * 0.52f;
                mid.y -= Mathf.Sqrt(Mathf.Max(0f, 0.0841f - (dist * 0.52f) * (dist * 0.52f))) * 0.9f;
                elbow = Vector3.Lerp(elbow, mid, near);
                hand = Vector3.Lerp(hand, reach, near);
            }
            Ball(arm.shoulder, shoulder, 0.06f);
            Segment(arm.sleeve, shoulder, Vector3.Lerp(shoulder, elbow, 0.62f));
            Segment(arm.upper, Vector3.Lerp(shoulder, elbow, 0.58f), elbow);
            Ball(arm.elbow, elbow, 0.043f);
            Segment(arm.fore, elbow, hand);
            Ball(arm.hand, hand, Quaternion.LookRotation(hand - elbow), new Vector3(0.04f, 0.045f, 0.052f));
            if (side == mapSide)
            {
                // The map in the hand, tipped up towards the face
                map.localPosition = hand + Vector3.up * 0.025f + fwd * 0.03f - right * (0.075f * side * stillNow);   // in both hands: between them
                // Read: tipped up to the face; carried: folded down along the forearm
                Quaternion readTurn = Quaternion.LookRotation(fwd) * Quaternion.Euler(-50f, 0f, 12f * side);
                Quaternion carryTurn = Quaternion.LookRotation(hand - elbow) * Quaternion.Euler(-10f, 0f, 75f * side);
                map.localRotation = Quaternion.Slerp(carryTurn, readTurn, readNow);
                map.localScale = Vector3.one;
            }
        }
    }
}
