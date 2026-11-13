using UnityEngine;

// A runner in the 3D view of a TV control: a stick figure of thin cylinders with a ball for a head, in the team's
// colour. Each runner gets one of a few running styles (high knees, bouncy, arms flapping ...), picked from its name, so
// the same runner always runs the same way. Near the control the runner slows its stride and reaches for the punching
// unit with the hand on the unit's side.
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

    static Mesh limbMesh, headMesh;
    static Material darkMaterial, skinMaterial;

    readonly GameObject root;
    readonly Transform torso, neck, thighR, shinR, thighL, shinL, upperR, foreR, upperL, foreL, head;
    readonly Material jersey;
    readonly Style style;
    public float phase;
    public Vector3 forward = Vector3.forward;
    public Vector3 position;
    public bool placed;
    // Going up to the flag to punch (TvWorldView.ToThePunch): 0 = on its route, 1 = to the flag, 2 = back to the route
    public int punch;
    public bool punched, tracked;
    public float shownX, shownZ, realX, realZ, headX, headZ, punchedAt;

    public TvStickFigure(Transform parent, string name, Color color, int seed)
    {
        MakeShared();
        root = new GameObject("Runner " + name);
        root.transform.SetParent(parent, false);
        jersey = TvWorldView.NewMaterial(color);
        var random = new System.Random(seed);
        Style s = Styles[random.Next(Styles.Length)];
        // A little of each runner's own
        float J() => 0.9f + (float)random.NextDouble() * 0.2f;
        s.cad *= J(); s.stride *= J(); s.knee *= J(); s.arm *= J(); s.lean *= J();
        style = s;
        phase = (float)random.NextDouble() * 6.28f;
        torso = Part("Torso", limbMesh, jersey, 0.085f);
        neck = Part("Neck", limbMesh, skinMaterial, 0.035f);
        thighR = Part("Thigh R", limbMesh, darkMaterial, 0.05f);
        shinR = Part("Shin R", limbMesh, darkMaterial, 0.04f);
        thighL = Part("Thigh L", limbMesh, darkMaterial, 0.05f);
        shinL = Part("Shin L", limbMesh, darkMaterial, 0.04f);
        upperR = Part("Arm R", limbMesh, jersey, 0.036f);
        foreR = Part("Forearm R", limbMesh, skinMaterial, 0.03f);
        upperL = Part("Arm L", limbMesh, jersey, 0.036f);
        foreL = Part("Forearm L", limbMesh, skinMaterial, 0.03f);
        head = Part("Head", headMesh, skinMaterial, 1f);
    }

    static void MakeShared()
    {
        if (limbMesh != null) return;
        // A cylinder of radius 1 from y = 0 to y = 1, scaled to each limb
        var b = new TvShapeBuilder(new TvRandom(3));
        b.Tube(1f, 1f, 1f, 0f, 6, new TvColor(255, 255, 255));
        limbMesh = TvWorldView.ToMesh(b.Done(), "Limb");
        b = new TvShapeBuilder(new TvRandom(4));
        b.Blob(0.115f, 1f, 1.1f, 1f, 0f, 0f, 0f, 0f, 1, false, new TvColor(255, 255, 255));
        headMesh = TvWorldView.ToMesh(b.Done(), "Head");
        darkMaterial = TvWorldView.NewMaterial(new Color32(0x22, 0x26, 0x2b, 255));
        skinMaterial = TvWorldView.NewMaterial(new Color32(0xe0, 0xb1, 0x90, 255));
    }

    Transform Part(string name, Mesh mesh, Material material, float radius)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        go.transform.localScale = new Vector3(radius, 1f, radius);
        return go.transform;
    }

    public void SetVisible(bool visible)
    {
        if (root.activeSelf != visible) root.SetActive(visible);
    }

    public void Destroy()
    {
        Object.Destroy(root);
        Object.Destroy(jersey);
    }

    static void Segment(Transform t, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float length = d.magnitude;
        float r = t.localScale.x;
        t.localPosition = a;
        t.localRotation = length > 1e-5f ? Quaternion.FromToRotation(Vector3.up, d / length) : Quaternion.identity;
        t.localScale = new Vector3(r, Mathf.Max(1e-4f, length), r);
    }

    // Moves the runner (on the ground at x, z) by the distance it ran since the last frame and poses it.
    // ground: the ground's height there; unit: the punching unit; near: 0 far from the control, 1 at it.
    public void Pose(float x, float ground, float z, float moved, Vector3 unit, float near)
    {
        Style st = style;
        phase += moved / 2.8f * 6.2831853f * st.cad;
        position = new Vector3(x, ground, z);
        Vector3 fwd = forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 P(float f, float u, float s) => position + fwd * f + Vector3.up * u + right * s;

        float ph = phase, lean = st.lean + near * 0.25f;
        float low = 0f;
        float kxR = 0, kyR = 0, fxR = 0, fyR = 0, kxL = 0, kyL = 0, fxL = 0, fyL = 0;
        for (int k = 0; k < 2; k++)
        {
            float p = ph + (k == 1 ? Mathf.PI : 0f);
            float th = st.stride * (1f - near * 0.5f) * Mathf.Sin(p) + 0.12f;
            float kn = 0.22f + st.knee * Mathf.Max(0f, Mathf.Cos(p + 0.55f));
            float kx = 0.46f * Mathf.Sin(th), ky = -0.46f * Mathf.Cos(th), a2 = th - kn;
            float fx = kx + 0.46f * Mathf.Sin(a2), fy = ky - 0.46f * Mathf.Cos(a2);
            if (k == 0) { kxR = kx; kyR = ky; fxR = fx; fyR = fy; }
            else { kxL = kx; kyL = ky; fxL = fx; fyL = fy; }
            low = Mathf.Min(low, fy);
        }
        float hipY = -low + 0.03f + st.bounce * 0.055f * (0.5f + 0.5f * Mathf.Cos(2f * ph)) * (1f - near * 0.6f);
        Vector3 hip = P(0f, hipY, 0f), chest = P(Mathf.Sin(lean) * 0.55f, hipY + Mathf.Cos(lean) * 0.55f, 0f);
        Segment(torso, hip, chest);
        Vector3 headPos = P(Mathf.Sin(lean) * 0.55f + 0.04f, hipY + Mathf.Cos(lean) * 0.55f + 0.2f, 0f);
        Segment(neck, chest, headPos);
        head.localPosition = headPos;
        head.localRotation = Quaternion.LookRotation(fwd);
        head.localScale = Vector3.one;

        Segment(thighR, P(0f, hipY, 0.1f), P(kxR, hipY + kyR, 0.1f));
        Segment(shinR, P(kxR, hipY + kyR, 0.1f), P(fxR, hipY + fyR, 0.1f));
        Segment(thighL, P(0f, hipY, -0.1f), P(kxL, hipY + kyL, -0.1f));
        Segment(shinL, P(kxL, hipY + kyL, -0.1f), P(fxL, hipY + fyL, -0.1f));

        float sf = Mathf.Sin(lean) * 0.5f, su = hipY + Mathf.Cos(lean) * 0.5f;
        int punchSide = Vector3.Dot(unit - position, right) > 0f ? 1 : -1;
        for (int side = 1; side >= -1; side -= 2)
        {
            float p = ph + (side < 0 ? Mathf.PI : 0f);
            float a = -st.arm * Mathf.Sin(p) * 0.9f, el = st.elbow + 0.25f * Mathf.Sin(p), fl = st.flare * 0.16f * (1f + 0.6f * Mathf.Sin(p + 1f));
            Vector3 shoulder = P(sf - Mathf.Sin(p) * 0.03f * side, su, 0.19f * side);
            Vector3 elbow = P(sf + 0.3f * Mathf.Sin(a), su - 0.3f * Mathf.Cos(a), (0.19f + fl) * side);
            Vector3 hand = P(sf + 0.3f * Mathf.Sin(a) + 0.27f * Mathf.Sin(a + el), su - 0.3f * Mathf.Cos(a) - 0.27f * Mathf.Cos(a + el), (0.19f + fl * 0.4f - 0.06f) * side);
            if (side == punchSide && near > 0f)
            {
                Vector3 target = unit + Vector3.up * 0.05f, d = target - shoulder;
                float dist = Mathf.Min(d.magnitude, 0.62f);
                d.Normalize();
                Vector3 reach = shoulder + d * dist, mid = shoulder + d * dist * 0.52f;
                mid.y -= Mathf.Sqrt(Mathf.Max(0f, 0.0961f - (dist * 0.52f) * (dist * 0.52f))) * 0.9f;
                elbow = Vector3.Lerp(elbow, mid, near);
                hand = Vector3.Lerp(hand, reach, near);
            }
            Segment(side > 0 ? upperR : upperL, shoulder, elbow);
            Segment(side > 0 ? foreR : foreL, elbow, hand);
        }
    }
}
