using System;

// How a runner moves (2026-11-25: moved out of TvStickFigure so the preview can draw it too, and given many more
// running styles). Pose works out where every joint of the runner's body (TvHuman) is in this frame of the run:
// the legs swing and fold with the stride, the pelvis turns with the leading leg and the shoulders turn against it,
// the arms drive with the bent elbows (some close to the body, some flapping, some crossing in front), the feet land
// on the heel or the forefoot and push off from the toes, the head bobs or stays still, the map hand reads now and
// then, and hills, cliffs, trunks to jump, the punch at the control and getting the breath back after the finish
// all change the pose. Each runner gets one of the styles, a little changed, from its seed.
// Plain C# (V3, no Unity).
public class TvRunnerGait
{
    public struct Style
    {
        public string name;
        public float cad, stride, knee, bounce, arm, flare, lean, elbow;
        public float twist;     // shoulders turning against the hips (radians at full stride)
        public float sway;      // pelvis turning with the leading leg (radians)
        public float drop;      // the hip of the swinging leg dropping (metres)
        public float cross;     // hands swinging in across the body (0 straight .. 1 to the middle and past)
        public float kick;      // heels kicking up behind (1 normal)
        public float strike;    // landing on the heel (+) or the forefoot (-)
        public float bob;       // head nodding with each step
        public float carry;     // hands carried high (elbows bent more)
        public float wide;      // feet a little apart
    }

    public static readonly Style[] Styles =
    {
        new Style { name = "smooth elite", cad = 1f, stride = 0.82f, knee = 1.45f, bounce = 0.55f, arm = 0.8f, flare = 0.1f, lean = 0.22f, elbow = 1.6f,
                    twist = 0.18f, sway = 0.12f, drop = 0.012f, cross = 0.3f, kick = 1.25f, strike = -0.1f, bob = 0.25f, carry = 0.3f, wide = 0f },
        new Style { name = "bouncy", cad = 1.1f, stride = 0.62f, knee = 1.1f, bounce = 1.9f, arm = 0.5f, flare = 0.05f, lean = 0.1f, elbow = 1.75f,
                    twist = 0.1f, sway = 0.06f, drop = 0.006f, cross = 0.15f, kick = 0.7f, strike = 0.25f, bob = 1f, carry = 0.5f, wide = 0.01f },
        new Style { name = "flapping arms", cad = 0.93f, stride = 0.86f, knee = 1.5f, bounce = 0.9f, arm = 1.15f, flare = 0.9f, lean = 0.3f, elbow = 1.2f,
                    twist = 0.26f, sway = 0.14f, drop = 0.015f, cross = 0f, kick = 1f, strike = 0f, bob = 0.6f, carry = 0f, wide = 0.015f },
        new Style { name = "neat", cad = 1.04f, stride = 0.7f, knee = 1.25f, bounce = 0.4f, arm = 0.6f, flare = 0.3f, lean = 0.24f, elbow = 1.6f,
                    twist = 0.14f, sway = 0.09f, drop = 0.008f, cross = 0.4f, kick = 0.95f, strike = 0.1f, bob = 0.3f, carry = 0.35f, wide = 0f },
        new Style { name = "high knees", cad = 0.98f, stride = 0.8f, knee = 1.6f, bounce = 0.7f, arm = 0.9f, flare = 0.45f, lean = 0.34f, elbow = 1.4f,
                    twist = 0.2f, sway = 0.12f, drop = 0.01f, cross = 0.2f, kick = 1.3f, strike = -0.15f, bob = 0.5f, carry = 0.2f, wide = 0.01f },
        new Style { name = "shuffle", cad = 1.12f, stride = 0.56f, knee = 0.85f, bounce = 0.3f, arm = 0.45f, flare = 0.15f, lean = 0.18f, elbow = 1.9f,
                    twist = 0.08f, sway = 0.15f, drop = 0.018f, cross = 0.55f, kick = 0.45f, strike = 0.35f, bob = 0.4f, carry = 0.8f, wide = 0.02f },
        new Style { name = "arm pumper", cad = 1.02f, stride = 0.78f, knee = 1.4f, bounce = 0.6f, arm = 1.3f, flare = 0.05f, lean = 0.28f, elbow = 1.5f,
                    twist = 0.3f, sway = 0.1f, drop = 0.01f, cross = 0.05f, kick = 1.15f, strike = -0.05f, bob = 0.35f, carry = 0.15f, wide = 0f },
        new Style { name = "upright", cad = 0.9f, stride = 0.9f, knee = 1.3f, bounce = 0.8f, arm = 0.7f, flare = 0.2f, lean = 0.07f, elbow = 1.35f,
                    twist = 0.15f, sway = 0.13f, drop = 0.012f, cross = 0.25f, kick = 1f, strike = 0.3f, bob = 0.5f, carry = 0.1f, wide = 0.005f },
        new Style { name = "arms across", cad = 1.05f, stride = 0.72f, knee = 1.2f, bounce = 0.7f, arm = 0.85f, flare = -0.1f, lean = 0.2f, elbow = 1.65f,
                    twist = 0.32f, sway = 0.17f, drop = 0.012f, cross = 1f, kick = 1f, strike = 0.1f, bob = 0.45f, carry = 0.4f, wide = 0f },
        new Style { name = "heel kicker", cad = 1f, stride = 0.76f, knee = 1.75f, bounce = 0.65f, arm = 0.75f, flare = 0.2f, lean = 0.26f, elbow = 1.55f,
                    twist = 0.16f, sway = 0.1f, drop = 0.01f, cross = 0.3f, kick = 1.7f, strike = -0.2f, bob = 0.3f, carry = 0.25f, wide = 0f },
        new Style { name = "long and loping", cad = 0.88f, stride = 0.95f, knee = 1.35f, bounce = 1.1f, arm = 0.95f, flare = 0.25f, lean = 0.2f, elbow = 1.3f,
                    twist = 0.22f, sway = 0.14f, drop = 0.014f, cross = 0.2f, kick = 1.2f, strike = 0.15f, bob = 0.7f, carry = 0.05f, wide = 0.008f },
        new Style { name = "stiff arms low", cad = 1.06f, stride = 0.68f, knee = 1.15f, bounce = 0.5f, arm = 0.55f, flare = 0.35f, lean = 0.16f, elbow = 0.95f,
                    twist = 0.12f, sway = 0.11f, drop = 0.01f, cross = 0.1f, kick = 0.85f, strike = 0.2f, bob = 0.4f, carry = 0f, wide = 0.01f }
    };

    readonly TvHuman.Body body;
    public Style style;
    public int styleIndex;
    public readonly TvHuman.Rig rig = new TvHuman.Rig();

    // ---- Set from outside ----
    public V3 forward = V3.Fwd;
    public float tired, walk;
    public int readMode = -1;
    public int mapHand = -1;

    // ---- The pose this frame ----
    public V3 position;
    public V3 mapPos, mapFwd = V3.Fwd, mapUp = V3.Up;
    public V3 bibPos, bibFwd = V3.Fwd, bibUp = V3.Up;
    public float phase;
    float pace = 4f;
    public float Pace => pace;

    // Map reading: now and then, and on the way in to a control
    float readEvery, readFor, readClock, read = 1f;
    readonly Random mapRandom;
    bool swapped;
    float still, clock;

    public TvRunnerGait(TvHuman.Body body, Random random)
    {
        this.body = body;
        styleIndex = random.Next(Styles.Length);
        Style s = Styles[styleIndex];
        // A little of each runner's own
        float J() => 0.85f + (float)random.NextDouble() * 0.3f;
        s.cad *= J(); s.stride *= J(); s.knee *= J(); s.arm *= J() * 1.15f; s.lean *= J(); s.flare *= J();
        s.twist *= J(); s.sway *= J(); s.cross *= J(); s.kick *= J(); s.bob *= J();
        style = s;
        readEvery = 3f + (float)random.NextDouble() * 4f;
        readFor = 1.2f + (float)random.NextDouble() * 1.8f;
        readClock = (float)random.NextDouble() * readEvery;
        mapRandom = random;
        mapHand = random.NextDouble() < 0.85 ? -1 : 1;
        phase = (float)random.NextDouble() * 6.28f;
    }


    // ---- The stride (u from 0, the foot landing, round to 1) ----
    // Thigh and knee angles (radians) for running at a steady pace, and how far the toes point down from flat
    static readonly float[] RunThigh = { 0f, 0.36f, 0.1f, 0.2f, 0.2f, 0.02f, 0.3f, -0.24f, 0.38f, -0.42f, 0.48f, -0.44f, 0.58f, -0.18f, 0.68f, 0.26f, 0.78f, 0.6f, 0.88f, 0.66f, 0.95f, 0.5f };
    static readonly float[] RunKnee = { 0f, 0.34f, 0.1f, 0.55f, 0.2f, 0.62f, 0.3f, 0.45f, 0.38f, 0.38f, 0.48f, 0.95f, 0.58f, 1.75f, 0.68f, 1.8f, 0.78f, 1.15f, 0.88f, 0.66f, 0.95f, 0.44f };
    static readonly float[] RunPitch = { 0f, 0f, 0.06f, 0f, 0.2f, 0.02f, 0.3f, 0.3f, 0.38f, 0.72f, 0.48f, 0.85f, 0.6f, 0.6f, 0.75f, 0.25f, 0.88f, 0.05f };
    static readonly float[] WalkThigh = { 0f, 0.3f, 0.15f, 0.18f, 0.3f, 0f, 0.45f, -0.2f, 0.6f, -0.32f, 0.7f, -0.15f, 0.8f, 0.12f, 0.9f, 0.3f };
    static readonly float[] WalkKnee = { 0f, 0.05f, 0.15f, 0.2f, 0.3f, 0.1f, 0.45f, 0.08f, 0.6f, 0.45f, 0.7f, 0.95f, 0.8f, 0.6f, 0.9f, 0.1f };
    static readonly float[] WalkPitch = { 0f, -0.25f, 0.1f, 0f, 0.45f, 0.1f, 0.6f, 0.5f, 0.75f, 0.2f, 0.9f, -0.1f };

    static float Frac(float x) => x - (float)Math.Floor(x);

    // A smooth loop through keyframes (u, value, u, value ...), u from 0 up to below 1
    static float Key(float[] k, float u)
    {
        int n = k.Length / 2;
        u = Frac(u);
        int i = n - 1;
        for (int j = 0; j < n; j++) if (k[j * 2] > u) { i = j - 1; break; }
        if (i < 0) i = n - 1;
        int i0 = (i - 1 + n) % n, i2 = (i + 1) % n, i3 = (i + 2) % n;
        float u1 = k[i * 2], u2 = k[i2 * 2];
        float span = u2 - u1; if (span <= 0f) span += 1f;
        float t = u - u1; if (t < 0f) t += 1f;
        t /= span;
        float p0 = k[i0 * 2 + 1], p1 = k[i * 2 + 1], p2 = k[i2 * 2 + 1], p3 = k[i3 * 2 + 1];
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
    static float InvLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-9f ? 0f : Clamp01((v - a) / (b - a));
    static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
    static float SmoothStep01(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
    static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
    static float Sin(float a) => (float)Math.Sin(a);
    static float Cos(float a) => (float)Math.Cos(a);

    // Turns v about the axis by a (radians)
    static V3 Turn(V3 v, V3 axis, float a)
    {
        float c = Cos(a), s = Sin(a);
        return v * c + V3.Cross(axis, v) * s + axis * (V3.Dot(axis, v) * (1f - c));
    }

    // An arm towards a hand position, the elbow bent outwards, the arm keeping its length
    static void Reach(V3 shoulder, V3 target, int side, V3 right, float share, float upper, float fore, ref V3 elbow, ref V3 hand)
    {
        V3 d = target - shoulder;
        float dist = Math.Min(d.Length, (upper + fore) * 0.985f);
        d = d.Normalized;
        V3 to = shoulder + d * dist;
        V3 bend = V3.ProjectOnPlane(right * side + V3.Up * -0.3f, d).Normalized;
        // How far out the elbow stands so both bones keep their lengths
        float a = (upper * upper - fore * fore + dist * dist) / (2f * dist);
        float out_ = (float)Math.Sqrt(Math.Max(0f, upper * upper - a * a));
        V3 mid = shoulder + d * a + bend * out_;
        elbow = V3.Lerp(elbow, mid, share);
        hand = V3.Lerp(hand, to, share);
    }

    // Keeps a two-bone limb its own length: the elbow (knee) on the line to it, the wrist (ankle) on from there
    static void Fix(V3 a, ref V3 b, ref V3 c, float l1, float l2)
    {
        V3 nb = a + (b - a).Normalized * l1;
        V3 nc = nb + (c - nb).Normalized * l2;
        b = nb; c = nc;
    }

    // Moves the runner (on the ground at x, z) by the distance it ran since the last frame and poses it.
    // ground: the ground's height there; unit: the punching unit; near: 0 far from the control, 1 at it.
    // slope: how much the ground rises per metre ahead (negative downhill); hop: 0..1 through a jump over a fallen trunk.
    public void Pose(float x, float ground, float z, float moved, V3 unit, float near, float dt, float slope, float hop, float hurdle)
    {
        Style st = style;
        float kb = body.kb, thighL = body.thigh, shinL = body.shin, upperL = body.upper, foreL = body.fore;
        float ankleH = body.rest.ankle[0].y;
        float hipW = Math.Abs(body.rest.hipJ[0].x), shW = Math.Abs(body.rest.shoulder[0].x);
        // Hills: shorter steps with high knees and a forward lean uphill, longer steps leaning back downhill.
        // Steep ground and cliffs: hands to the ground going up, a deep crouch going down.
        float up = Clamp(slope, -1.3f, 1.3f);
        float climb = SmoothStep01(InvLerp(0.5f, 0.95f, up));
        float crouch = SmoothStep01(InvLerp(-0.5f, -0.95f, up));
        float uphill = Clamp01(up / 0.45f), downhill = Clamp01(-up / 0.45f);
        st.stride *= 1f - 0.3f * uphill + 0.12f * downhill * (1f - crouch) - 0.3f * climb - 0.45f * crouch;
        st.knee *= 1f + 0.35f * uphill - 0.25f * downhill;
        st.bounce *= 1f - 0.7f * Math.Max(climb, crouch);
        // Stride and rhythm follow the speed, as for a real runner: the rhythm changes only a little and the length
        // of the step makes up the rest (a slow runner takes short choppy steps at nearly the same rhythm)
        if (dt > 1e-5f) pace = Lerp(pace, moved / dt, dt * 9f);
        float cadence = Clamp(1.12f + 0.07f * pace, 1.18f, 1.62f) * st.cad * (1f - 0.22f * walk);
        float cycle = Math.Max(pace, 0.3f) / cadence;
        float stepShare = Clamp(cycle / 2.8f, 0.3f, 1.45f);
        float effort = InvLerp(0.3f, 1.3f, stepShare);
        st.stride = Math.Min(st.stride * stepShare, 1.0f);
        st.knee *= Lerp(0.62f, 1.12f, effort) * (1f - 0.45f * walk);
        st.arm *= Lerp(0.72f, 1.1f, effort) * (1f - 0.45f * walk);
        st.bounce *= Lerp(0.55f, 1.15f, effort) * (1f - 0.85f * walk);
        float run = effort * (1f - walk) * (1f - tired);
        if (dt > 1e-5f && pace > 0.4f) phase += dt * cadence * 6.2831853f;
        else phase += moved / 0.84f * 6.2831853f;
        clock += dt;
        position = new V3(x, ground, z);
        V3 fwd = new V3(forward.x, 0f, forward.z).Normalized;
        V3 right = V3.Cross(V3.Up, fwd);
        V3 P(float f, float u, float s) => position + fwd * f + V3.Up * u + right * s;

        float ph = phase;
        readClock += dt;
        if (readClock > readEvery + readFor)
        {
            readClock = 0f;
            readEvery = 3f + (float)mapRandom.NextDouble() * 5f;
            readFor = 1f + (float)mapRandom.NextDouble() * 2f;
        }
        bool standing = pace < 0.7f && near < 0.05f && tired < 0.05f && walk < 0.05f && hop < 0.05f;
        still = MoveTowards(still, standing ? 1f : 0f, dt * 2.5f);
        bool reading = readMode >= 0 ? readMode == 1 && tired < 0.3f
                     : (readClock > readEvery || near > 0.05f || climb > 0.3f || standing) && tired < 0.3f && walk < 0.5f;
        read = MoveTowards(read, reading ? 1f : 0f, dt * 3f);
        float readNow = SmoothStep01(read);
        float stillNow = SmoothStep01(still);
        float lean = st.lean + near * 0.25f + 0.2f * uphill + 0.5f * climb - 0.16f * downhill * (1f - crouch) + 0.05f * crouch
                     + 0.07f * (1f - effort) * (1f - walk) - 0.12f * walk - 0.1f * stillNow;
        lean = Lerp(lean, 0.75f + 0.05f * Sin(clock * 6f), tired);

        // ---- Legs: a stride from keyframes measured on runners (and walkers), angles from straight down ----
        // u: where a leg is in its stride, 0 the foot lands, sf it pushes off, then it swings through
        float sf = Lerp(0.36f, 0.6f, walk);
        float thAmp = st.stride / 0.8f * (1f - near * 0.5f);
        float knAmp = st.knee / 1.4f * (1f + 0.6f * (st.kick - 1f) * run);
        float strikeA = st.strike > 0f ? -0.32f * st.strike : -0.6f * st.strike;   // heel: toes up; forefoot: toes down
        var kneeF = new float[2]; var kneeU = new float[2]; var footF = new float[2]; var footU = new float[2]; var shinA = new float[2]; var legU = new float[2];
        var pitchOff = new float[2];
        void Leg(float u, float lead, out float kx, out float ky, out float fx, out float fy, out float a2, out float po)
        {
            float rt = Key(RunThigh, u), rk = Key(RunKnee, u);
            rk = Math.Min(rk, 0.62f) + Math.Max(0f, rk - 0.62f) * knAmp;
            float th = Lerp(rt * thAmp, Key(WalkThigh, u) * Math.Min(1f, thAmp * 1.1f), walk);
            float kn = Lerp(rk * Lerp(0.75f, 1f, effort), Key(WalkKnee, u), walk);
            th += 0.55f * crouch + 0.3f * climb + hop * (0.65f * lead + 0.15f);
            kn += 1.05f * crouch + 0.35f * climb + hop * (1.3f - 0.9f * lead);
            kn = Math.Max(0.03f, kn);
            kx = thighL * Sin(th); ky = -thighL * Cos(th); a2 = th - kn;
            fx = kx + shinL * Sin(a2); fy = ky - shinL * Cos(a2);
            po = Lerp(Key(RunPitch, u) + strikeA * Math.Max(0f, 1f - Math.Min(u, 1f - u) / 0.07f), Key(WalkPitch, u), walk) * Lerp(0.6f, 1f, effort);
        }
        float Support(float fx, float fy) => ankleH - fy + up * fx;    // the hip's height that puts this foot on the ground
        for (int k = 0; k < 2; k++)
        {
            // k = 0 the right leg, 1 the left (half a stride later); the leg is furthest forward at sin(p) = 1
            float p = ph + (k == 1 ? (float)Math.PI : 0f);
            float u = Frac((p - 1.5707963f) / 6.2831853f + 0.88f);
            legU[k] = u;
            Leg(u, Sin(p) > 0f ? 1f : 0f, out kneeF[k], out kneeU[k], out footF[k], out footU[k], out shinA[k], out pitchOff[k]);
        }
        float hipY = Math.Max(Support(footF[0], footU[0]), Support(footF[1], footU[1]));
        // Both feet off the ground between a push-off and the other foot landing: the body flies a little higher
        if (legU[0] >= sf && legU[1] >= sf)
        {
            float last = legU[0] < legU[1] ? legU[0] : legU[1];
            float t = Clamp01((last - sf) / Math.Max(0.02f, 0.5f - sf));
            hipY += st.bounce * 0.05f * run * 4f * t * (1f - t);
        }
        hipY += Clamp(hurdle * 1.05f, 0.42f, 0.95f) * hop;

        // ---- The trunk: the pelvis turns with the leading leg, the shoulders against it ----
        V3 bodyUp = (fwd * Sin(lean) + V3.Up * Cos(lean)).Normalized;
        float swing = Sin(ph);   // + while the right leg is ahead
        float yaw = st.sway * swing * run;
        float twist = st.twist * swing * run;
        V3 hipRight = Turn(right, V3.Up, -yaw);                              // the right hip forward with the right leg
        hipRight = (hipRight + V3.Up * (st.drop / Math.Max(0.05f, hipW) * Cos(2f * ph - 0.6f) * 0.5f * run * Math.Sign(Sin(ph)))).Normalized;
        V3 chestRight = Turn(right, bodyUp, twist);                          // the right shoulder back
        V3 front = V3.Cross(chestRight, bodyUp).Normalized;
        V3 hip = P(0f, hipY, 0f);
        float waistUp = (body.rest.waist - body.rest.hip).Length, chestUp = body.rest.chest.y - body.rest.hip.y;
        // The back curves a little: the waist leans less than the chest
        V3 waistDir = (fwd * Sin(lean * 0.7f) + V3.Up * Cos(lean * 0.7f)).Normalized;
        V3 waist = hip + waistDir * waistUp;
        V3 chest = waist + bodyUp * (chestUp - waistUp);
        rig.hip = hip; rig.waist = waist; rig.chest = chest;
        rig.right = hipRight; rig.chestRight = chestRight;
        float neckLen = (body.rest.neckTop - body.rest.chest).Length;
        // The head stays fairly level: the neck leans less than the back, the eyes ahead (or down at the map)
        V3 neckDir = (fwd * Sin(lean * 0.35f + 0.12f) + V3.Up * Cos(lean * 0.35f + 0.12f)).Normalized;
        rig.neckTop = chest + neckDir * neckLen;
        float nod = (14f * readNow + 8f * stillNow - 30f * tired) * 0.01745f + st.bob * 0.06f * Sin(2f * ph + 0.8f) * run + 0.08f * climb;
        V3 headFwd = (fwd * Cos(nod) - V3.Up * Sin(nod)).Normalized;
        // The head turns a little against the shoulders' turn, to keep looking ahead
        headFwd = Turn(headFwd, V3.Up, twist * 0.25f);
        rig.headFwd = headFwd;
        rig.headUp = V3.Cross(headFwd, V3.Cross(V3.Up, headFwd)).Normalized;

        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? 1f : -1f;          // k 0 right
            int i = k == 0 ? 1 : 0;               // the rig's index: 0 left, 1 right
            V3 hj = hip + hipRight * (hipW * s) + V3.Up * 0f;
            float wide = st.wide * s;
            V3 kn = hj + fwd * kneeF[k] + V3.Up * kneeU[k] + right * (wide * 0.5f - 0.004f * s);
            V3 an = hj + fwd * footF[k] + V3.Up * footU[k] + right * (wide - 0.006f * s);
            Fix(hj, ref kn, ref an, thighL, shinL);
            // The foot: flat under the body, the toes up reaching forward to land (on the heel), pointing down to push off
            float pitch = pitchOff[k];
            pitch += 0.5f * crouch;
            float baseA = (float)Math.Atan2(body.rest.ankle[0].y - body.rest.toe[0].y, body.rest.toe[0].z - body.rest.ankle[0].z);
            float fa = baseA + pitch;
            V3 toe = an + (fwd * Cos(fa) - V3.Up * Sin(fa)) * body.footLen + right * (0.004f * s);
            rig.hipJ[i] = hj; rig.knee[i] = kn; rig.ankle[i] = an; rig.toe[i] = toe;
        }

        // ---- Arms: one hand reaches for the punching unit near the control, the other holds the map ----
        int unitSide = V3.Dot(unit - position, right) > 0f ? 1 : -1;
        if (near <= 0.001f) swapped = false;
        else if (near > 0.15f && unitSide == mapHand && !swapped) swapped = true;
        int mapSide = swapped ? -mapHand : mapHand;
        int punchSide = -mapSide;
        V3 mapHandAt = new V3();
        V3 chestFront = front;
        V3 shoulderUp = bodyUp;
        float shY = body.rest.shoulder[0].y - body.rest.chest.y;
        for (int n = 0; n < 2; n++)
        {
            int side = n == 0 ? mapSide : -mapSide;
            int i = side == 1 ? 1 : 0;
            float p = ph + (side < 0 ? (float)Math.PI : 0f);
            // The arm swings against its leg: forward while that leg is back
            float a = -st.arm * Sin(p) * 0.9f - 0.15f * run, el = st.elbow * 0.9f - 0.28f * Sin(p) * run + st.carry * 0.25f, fl = st.flare * 0.16f * (1f + 0.6f * Sin(p + 1f));
            float handIn = 0.16f + fl * 0.4f - 0.05f;
            // Hands swing in across the body as they come forward
            handIn -= st.cross * 0.11f * Math.Max(0f, -Sin(p)) * run;
            if (side == mapSide)
            {
                a = Lerp(a * 0.8f, 0.35f + 0.08f * Sin(p), readNow);
                el = Lerp(el, 1.55f, readNow);
                fl = Lerp(fl, 0.02f, readNow);
                handIn = Lerp(handIn, 0.08f, readNow);
                a = Lerp(a, 0.3f, stillNow);
                el = Lerp(el, 1.35f, stillNow);
                handIn = Lerp(handIn, 0.03f, stillNow);
            }
            V3 shoulder = chest + chestRight * (shW * side) + shoulderUp * shY;
            // The shoulder lifts a little as its arm drives forward
            shoulder = shoulder + V3.Up * (0.012f * Math.Max(0f, -Sin(p)) * run);
            // Arm positions in the body's own directions (forward, up, out)
            V3 Arm(float f, float u, float o) => shoulder + front * f + bodyUp * u + chestRight * (o * side);
            V3 elbow = Arm(upperL * Sin(a), -upperL * Cos(a), 0.01f + fl);
            // (handIn is measured from the middle of the body)
            V3 hand = Arm(upperL * Sin(a) + foreL * Sin(a + el), -upperL * Cos(a) - foreL * Cos(a + el), handIn - shW);
            if (climb > 0f)
            {
                float f = 0.4f + 0.1f * Sin(p), lift = 0.1f * Math.Max(0f, Sin(p));
                Reach(shoulder, P(f, up * f + 0.05f + lift, 0.19f * side), side, right, climb, upperL, foreL, ref elbow, ref hand);
            }
            if (crouch > 0f)
            {
                V3 to = side == mapSide ? P(0.3f, hipY + 0.25f, 0.4f * side) : P(-0.15f, hipY - 0.05f, 0.42f * side);
                Reach(shoulder, to, side, right, crouch * 0.85f, upperL, foreL, ref elbow, ref hand);
            }
            if (hop > 0f)
            {
                hand = hand + (V3.Up * 0.12f + right * (0.08f * side)) * hop;
                elbow = elbow + right * (0.06f * side * hop);
            }
            if (tired > 0f)
                Reach(shoulder, P(0.24f, hipY - 0.2f, 0.11f * side), side, right, tired, upperL, foreL, ref elbow, ref hand);
            if (side != mapSide && stillNow > 0f)
                Reach(shoulder, mapHandAt + right * (0.15f * side) + V3.Up * 0.01f, side, right, stillNow, upperL, foreL, ref elbow, ref hand);
            if (side == punchSide && near > 0f)
            {
                V3 target = unit + V3.Up * 0.05f, d = target - shoulder;
                float dist = Math.Min(d.Length, (upperL + foreL) * 0.97f);
                d = d.Normalized;
                V3 reach = shoulder + d * dist;
                float ah = (upperL * upperL - foreL * foreL + dist * dist) / (2f * dist);
                V3 mid = shoulder + d * ah;
                mid = mid + V3.Up * (-(float)Math.Sqrt(Math.Max(0f, upperL * upperL - ah * ah)) * 0.9f);
                elbow = V3.Lerp(elbow, mid, near);
                hand = V3.Lerp(hand, reach, near);
            }
            // The bones keep their lengths
            Fix(shoulder, ref elbow, ref hand, upperL, foreL);
            V3 foreDir = (hand - elbow).Normalized;
            // The hand goes on from the wrist, bent a little in a loose fist
            V3 tip = hand + (foreDir * 0.9f + front * 0.2f).Normalized * body.handLen;
            rig.shoulder[i] = shoulder; rig.elbow[i] = elbow; rig.wrist[i] = hand; rig.hand[i] = tip;
            if (side == mapSide)
            {
                mapHandAt = hand + foreDir * 0.06f;
                // The map in the hand, tipped up towards the face when reading, folded along the forearm when not
                mapPos = mapHandAt + V3.Up * 0.025f + fwd * 0.03f - right * (0.075f * side * stillNow);
                Euler(fwd, V3.Up, -50f, 12f * side, out V3 rf, out V3 ru);
                Euler(foreDir, V3.Up, -10f, 75f * side, out V3 cf, out V3 cu);
                V3 mf = V3.Lerp(cf, rf, readNow).Normalized, mu = V3.Lerp(cu, ru, readNow);
                mu = V3.ProjectOnPlane(mu, mf).Normalized;
                mapFwd = mf; mapUp = mu;
            }
        }
        // The number bib on the chest
        float bibY = (body.look.female ? 1.21f : 1.27f) * kb - body.rest.hip.y;
        bibPos = hip + waistDir * waistUp + bodyUp * (bibY - waistUp) + front * BibDepth(body, bibY + body.rest.hip.y);
        bibFwd = front;
        bibUp = bodyUp;
    }

    // How far in front of the body's middle line the chest is at a height (for the bib)
    static float BibDepth(TvHuman.Body body, float y)
    {
        float g = body.look.girth;
        float d = body.look.female ? 0.096f : 0.112f;
        return d * body.kb * (0.88f + 0.12f * g) + 0.008f;
    }

    // A turn as Unity's Quaternion.LookRotation(f, up) * Quaternion.Euler(xDeg, 0, zDeg), as forward and up
    static void Euler(V3 f, V3 up, float xDeg, float zDeg, out V3 outF, out V3 outU)
    {
        V3 F = f.Normalized;
        V3 R = V3.Cross(up, F).Normalized;
        if (R.Length < 0.5f) R = V3.Right;
        V3 U = V3.Cross(F, R);
        float x = xDeg * 0.0174533f, zr = zDeg * 0.0174533f;
        // Euler: z first (roll), then x (pitch); +x tips the forward down
        V3 F1 = F * Cos(x) - U * Sin(x);
        V3 U1 = U * Cos(x) + F * Sin(x);
        outF = F1;
        outU = (U1 * Cos(zr) - R * Sin(zr)).Normalized;
    }
}
