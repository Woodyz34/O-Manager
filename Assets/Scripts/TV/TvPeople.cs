using System;

// The town's people as real-looking figures (2026-11-20, Anders: more realistic people, more animations, real body
// shapes and faces). A figure is built from a jointed body (hips, knees, shoulders, elbows, the trunk leaning, the
// head turning), so the same person can stand, walk, clap, cheer, wave, look at a phone, sit or jump; each animation
// is a short loop of frames (Animate) that RunYourself/RunPeople.cs shows one after the other.
// Bodies differ by look: men and women, tall and short, slim and broad, children; faces have eyes, brows, nose,
// mouth, ears and a little colour in the cheeks; hair in several styles, some beards, skirts.
// Faces -z, standing on y = 0.
public static partial class TvCityShapes
{
    // How the joints stand (radians). hip/knee: the thigh swung forward and the knee bent; arm: the arm swung forward
    // (pi/2 straight ahead, pi straight up), raise: out to the side (negative: in across the body), elbow: bent
    public struct Joints
    {
        public float hipL, hipR, kneeL, kneeR, armL, armR, raiseL, raiseR, elbowL, elbowR, lean, headYaw, headPitch, rise;
        public bool sit, phone, flag;
    }

    public enum Act { Idle, Walk, Clap, Cheer, Wave, Phone, Sit, Jump }
    public const int Frames = 8;

    static readonly TvColor[] Skins =
        { TvColor.Hex("#f1c9a5"), TvColor.Hex("#e6b590"), TvColor.Hex("#d9a37c"), TvColor.Hex("#eec0a0"), TvColor.Hex("#c98f68"), TvColor.Hex("#e9bc96"), TvColor.Hex("#a8754f") };
    static readonly TvColor[] Hairs =
        { TvColor.Hex("#3a2a1e"), TvColor.Hex("#b08a4a"), TvColor.Hex("#6b4a2e"), TvColor.Hex("#d8c08a"), TvColor.Hex("#8f8f8a"), TvColor.Hex("#1e1a18"), TvColor.Hex("#8a3f22"), TvColor.Hex("#c9a56a") };
    static readonly TvColor Eye = TvColor.Hex("#f4f1ea"), Iris = TvColor.Hex("#2c2a28"), Lips = TvColor.Hex("#a8545a"), Blush = TvColor.Hex("#e58f86");

    // One frame of an animation (frame 0 .. Frames-1 goes once round the loop)
    public static Joints Animate(Act act, int frame, int look)
    {
        float ph = frame * 6.2831853f / Frames, s = (float)Math.Sin(ph), c = (float)Math.Cos(ph);
        float s2 = (float)Math.Sin(2f * ph), c2 = (float)Math.Cos(2f * ph);
        var j = new Joints();
        switch (act)
        {
            case Act.Walk:
                j.hipL = 0.42f * s; j.hipR = -0.42f * s;
                j.kneeL = 0.08f + 0.65f * Math.Max(0f, c); j.kneeR = 0.08f + 0.65f * Math.Max(0f, -c);
                j.armL = -0.38f * s; j.armR = 0.38f * s;
                j.raiseL = j.raiseR = 0.05f;
                j.elbowL = 0.3f + 0.2f * Math.Max(0f, -s); j.elbowR = 0.3f + 0.2f * Math.Max(0f, s);
                j.lean = 0.05f; j.rise = 0.025f * Math.Abs(c);
                j.headYaw = 0.05f * s;
                break;
            case Act.Clap:
                // Hands meet in front of the chest twice a loop
                j.armL = j.armR = 1.05f;
                j.raiseL = j.raiseR = -0.28f + 0.3f * (0.5f + 0.5f * c2);
                j.elbowL = j.elbowR = 1.05f;
                j.kneeL = j.kneeR = 0.06f;
                j.headPitch = -0.05f;
                break;
            case Act.Cheer:
                // Both arms up, shaking
                j.armL = j.armR = 2.75f + 0.12f * s2;
                j.raiseL = 0.35f + 0.12f * s2; j.raiseR = 0.35f - 0.12f * s2;
                j.elbowL = 0.25f + 0.25f * Math.Max(0f, s2); j.elbowR = 0.25f + 0.25f * Math.Max(0f, -s2);
                j.lean = -0.06f; j.headPitch = -0.2f;
                j.kneeL = j.kneeR = 0.1f + 0.1f * Math.Abs(s);
                j.flag = look % 4 == 0;
                break;
            case Act.Wave:
                // One hand high, waving from the elbow; the other on the hip
                j.armR = 2.45f; j.raiseR = 0.45f; j.elbowR = 0.55f + 0.5f * s2;
                j.armL = 0.15f; j.raiseL = 0.45f; j.elbowL = 1.5f;
                j.headYaw = 0.1f * s; j.kneeL = 0.05f; j.kneeR = 0.1f;
                break;
            case Act.Phone:
                // Looking down at a phone held in both hands, thumbs moving
                j.armL = j.armR = 0.45f;
                j.raiseL = j.raiseR = -0.12f;
                j.elbowL = 1.75f + 0.05f * s; j.elbowR = 1.75f - 0.05f * s;
                j.headPitch = 0.45f; j.lean = 0.04f; j.phone = true;
                j.hipL = 0.03f * s; j.kneeR = 0.08f;
                break;
            case Act.Sit:
                j.sit = true;
                j.hipL = j.hipR = 1.5f; j.kneeL = j.kneeR = 1.45f;
                j.armL = j.armR = 0.75f; j.elbowL = j.elbowR = 0.55f; j.raiseL = j.raiseR = 0.05f;
                j.lean = -0.08f; j.headYaw = 0.35f * s;
                break;
            case Act.Jump:
                // Jumping up and down with the arms up (the height of the jump is added by RunPeople)
                float crouch = Math.Max(0f, -c);
                j.kneeL = j.kneeR = 0.15f + 0.9f * crouch; j.hipL = j.hipR = 0.08f + 0.5f * crouch;
                j.rise = -0.12f * crouch;
                j.armL = j.armR = 2.6f - 0.6f * crouch; j.raiseL = j.raiseR = 0.4f; j.elbowL = j.elbowR = 0.3f;
                j.lean = 0.15f * crouch; j.headPitch = -0.15f;
                break;
            default:
                // Standing: shifting the weight, looking about, the arms hanging easy
                j.hipL = 0.03f * s; j.hipR = -0.03f * s; j.kneeL = 0.04f + 0.06f * Math.Max(0f, s); j.kneeR = 0.04f + 0.06f * Math.Max(0f, -s);
                j.armL = j.armR = 0.06f; j.raiseL = j.raiseR = 0.05f;
                j.elbowL = 0.18f + 0.06f * s; j.elbowR = 0.18f - 0.06f * s;
                j.headYaw = 0.35f * s; j.headPitch = 0.05f * c;
                j.lean = 0.01f;
                break;
        }
        return j;
    }

    // A person of a look in the given joints. child: a child of about 1.2 m
    public static TvShape Figure(TvRandom random, int look, bool child, Joints j)
    {
        var b = new TvShapeBuilder(random);
        int l = Math.Abs(look);
        bool female = l % 2 == 1;
        float height = child ? 1.12f + 0.08f * (l / 3 % 3) : (female ? 1.63f : 1.76f) + 0.05f * (l * 7 % 5 - 2);
        float k = height / 1.75f, girth = child ? 0.95f : 0.88f + 0.09f * (l * 5 % 5), head = child ? 1.3f : 1f;
        TvColor skin = Skins[l * 3 % Skins.Length == 6 && l % 5 != 0 ? 1 : l * 3 % Skins.Length];
        TvColor hair = Hairs[(l / 2 + l * 3) % Hairs.Length];
        if (!child && !female && l % 9 == 4) hair = Hairs[4];   // some grey
        TvColor top = Clothes[(l * 7 + 1) % Clothes.Length], legs = Trousers[(l / 3) % Trousers.Length];
        TvColor shoe = l % 4 == 1 ? TvColor.Hex("#e8e6e0") : l % 4 == 2 ? TvColor.Hex("#5a3a28") : Shoe;
        bool skirt = female && !j.sit && l % 3 == 0, shortSleeves = l % 3 == 2, beard = !female && !child && l % 5 == 1;
        TvColor skirtC = Clothes[(l * 3 + 4) % Clothes.Length].Shade(0.9f);
        int style = female ? 4 + (l / 2) % 4 : (l / 2) % 4;

        float hipY = (j.sit ? 0.5f : 0.93f * k) + j.rise;
        float hipW = (female ? 0.1f : 0.09f) * k * girth, shW = (female ? 0.165f : 0.19f) * k * (0.94f + 0.06f * girth);
        float thigh = 0.43f * k, shin = 0.43f * k, upper = 0.3f * k, fore = 0.27f * k;
        float cl = (float)Math.Cos(j.lean), sl = (float)Math.Sin(j.lean);
        // A point on the trunk: dy up from the hips, x across, z forward(-)/back(+), the trunk leaning forward by lean
        void T(float x, float dy, float z, out float px, out float py, out float pz)
        {
            px = x;
            py = hipY + dy * cl + z * sl;
            pz = -dy * sl + z * cl;
        }

        // ---- Legs ----
        foreach (float side in new[] { -1f, 1f })
        {
            float hip = side < 0 ? j.hipL : j.hipR, knee = side < 0 ? j.kneeL : j.kneeR;
            float hx = side * hipW, hy = hipY, hz = 0f;
            float kx = hx, ky = hy - thigh * (float)Math.Cos(hip), kz = -thigh * (float)Math.Sin(hip);
            float sa = hip - knee;
            float ax = hx, ay = ky - shin * (float)Math.Cos(sa), az = kz - shin * (float)Math.Sin(sa);
            if (!j.sit && ay < 0.07f) { float lift = 0.07f - ay; ay += lift; ky += lift * 0.5f; }
            TvColor lower = skirt ? (l % 2 == 1 ? skin : TvColor.Hex("#2a2a30")) : legs;
            b.Limb(hx, hy, hz, kx, ky, kz, 0.078f * k * girth, 0.056f * k * girth, 7, legs);
            b.Blob(0.057f * k * girth, 1f, 1f, 1f, kx, ky, kz, 0f, 0, false, skirt ? lower : legs);
            b.Limb(kx, ky, kz, ax, ay, az, 0.054f * k * girth, 0.037f * k, 7, lower);
            // The shoe points the way the body faces, flat on the ground when standing
            b.Blob(0.052f * k, 0.85f, 0.6f, 1.75f, ax, ay - 0.035f * k, az - 0.055f * k, 0f, 0, false, shoe);
        }

        // ---- Trunk ----
        float px, py, pz;
        T(0f, 0.02f * k, 0f, out px, out py, out pz);
        b.Blob(0.15f * k * girth, female ? 1.25f : 1.12f, 0.72f, 0.85f, px, py, pz, 0f, 1, false, legs);
        // Waist and chest overlap into one trunk, broader at the shoulders for men
        T(0f, 0.19f * k, 0f, out px, out py, out pz);
        b.Blob(0.155f * k * girth, female ? 0.92f : 1.0f, 1.35f, 0.78f, px, py, pz, 0f, 1, false, top);
        T(0f, 0.36f * k, 0.005f, out px, out py, out pz);
        b.Blob(0.17f * k * (0.92f + 0.08f * girth), female ? 1.0f : 1.16f, 1.05f, 0.74f, px, py, pz, 0f, 1, false, top);
        if (female && !child)
            foreach (float side in new[] { -1f, 1f })
            {
                T(side * 0.065f * k, 0.33f * k, -0.085f * k, out px, out py, out pz);
                b.Blob(0.065f * k, 1f, 0.9f, 0.8f, px, py, pz, 0f, 0, false, top);
            }
        // A neckline (a V of skin) and a belt
        T(0f, 0.5f * k, -0.105f * k, out px, out py, out pz);
        b.Blob(0.036f * k, 1.1f, 1.3f, 0.35f, px, py, pz, 0f, 0, false, skin);
        if (skirt)
        {
            T(0f, 0.08f * k, 0f, out float wx, out float wy, out float wz);
            b.Limb(wx, wy, wz, wx, Math.Max(0.4f, wy - 0.45f * k), wz, 0.15f * k * girth, 0.23f * k * girth, 9, skirtC);
        }
        else
        {
            T(0f, 0.08f * k, 0f, out px, out py, out pz);
            b.Blob(0.148f * k * girth, female ? 1.2f : 1.1f, 0.18f, 0.85f, px, py, pz, 0f, 0, false, TvColor.Hex("#2b2622"));
        }

        // ---- Arms ----
        float hold = 0f, holdX = 0f, holdY = 0f, holdZ = 0f;
        foreach (float side in new[] { -1f, 1f })
        {
            float arm = side < 0 ? j.armL : j.armR, raise = side < 0 ? j.raiseL : j.raiseR, elbow = side < 0 ? j.elbowL : j.elbowR;
            T(side * shW, 0.47f * k, 0.01f, out float sx, out float sy, out float sz);
            // Down, swung forward by arm, then out to the side by raise (the trunk's lean added)
            void Dir(float swing, out float dx, out float dy, out float dz)
            {
                float fy = -(float)Math.Cos(swing + j.lean), fz = -(float)Math.Sin(swing + j.lean);
                // Out to the side the same way whether the arm hangs or is held up
                dx = side * Math.Abs(fy) * (float)Math.Sin(raise); dy = fy * (float)Math.Cos(raise); dz = fz;
            }
            Dir(arm, out float ux, out float uy, out float uz);
            float ex = sx + ux * upper, ey = sy + uy * upper, ez = sz + uz * upper;
            Dir(arm + elbow, out float fx, out float fy2, out float fz);
            float wx = ex + fx * fore, wy = ey + fy2 * fore, wz = ez + fz * fore;
            b.Blob(0.062f * k * girth, 1f, 0.9f, 1f, sx, sy, sz, 0f, 1, false, top);
            b.Limb(sx, sy, sz, ex, ey, ez, 0.062f * k * girth, 0.049f * k, 7, top);
            b.Blob(0.049f * k, 1f, 1f, 1f, ex, ey, ez, 0f, 1, false, shortSleeves ? skin : top);
            b.Limb(ex, ey, ez, wx, wy, wz, 0.047f * k, 0.034f * k, 7, shortSleeves ? skin : top);
            if (!shortSleeves) b.Blob(0.036f * k, 1f, 1f, 1f, wx, wy, wz, 0f, 0, false, top);
            float hx = wx + fx * 0.05f * k, hy = wy + fy2 * 0.05f * k, hz = wz + fz * 0.05f * k;
            b.Blob(0.045f * k, 0.75f, 1.15f, 0.55f, hx, hy, hz, 0f, 0, false, skin);
            if (side > 0) { hold = 1f; holdX = hx; holdY = hy; holdZ = hz; }
        }
        if (j.phone && hold > 0f)
            b.Box(holdX - 0.12f, holdY - 0.02f, holdZ - 0.06f, holdX - 0.04f, holdY + 0.12f, holdZ - 0.04f, TvColor.Hex("#1c1d22"));
        if (j.flag && hold > 0f)
        {
            Beam(b, holdX, holdY - 0.05f, holdZ, holdX, holdY + 0.5f, holdZ, 0.02f, 0.02f, Wood);
            SwedishFlag(b, holdX, holdY + 0.5f, holdZ, 0.4f, 0f);
        }

        // ---- Neck and head ----
        T(0f, 0.5f * k, 0.01f, out float nx, out float ny, out float nz);
        T(0f, 0.62f * k, 0.0f, out float cx, out float cy, out float cz);
        float r = 0.112f * k * head;
        cy += 0.02f * k + (head - 1f) * 0.08f;
        b.Limb(nx, ny, nz, cx, cy - r * 0.55f, cz + 0.01f, 0.05f * k, 0.047f * k, 7, skin);
        float cyaw = (float)Math.Cos(j.headYaw), syaw = (float)Math.Sin(j.headYaw), cp = (float)Math.Cos(j.headPitch), sp = (float)Math.Sin(j.headPitch);
        // A point on the head (in units of its radius): turned by the head's yaw and nod
        void Hd(float x, float y, float z, float size, float ex, float ey, float ez, TvColor col, int detail = 0)
        {
            float y1 = y * cp - z * sp, z1 = y * sp + z * cp;   // nod: forward (-z) goes down
            float x2 = x * cyaw + z1 * syaw, z2 = -x * syaw + z1 * cyaw;
            b.Blob(size * r, ex, ey, ez, cx + x2 * r, cy + y1 * r, cz + z2 * r, 0f, detail, false, col);
        }
        Hd(0f, 0f, 0f, 1f, female ? 0.88f : 0.92f, 1.1f, 1f, skin, 1);
        Hd(0f, -0.42f, -0.18f, 0.72f, female ? 0.78f : 0.88f, 0.62f, 0.85f, skin, 1);           // jaw and chin
        Hd(0f, -0.08f, -0.97f, 0.17f, 0.75f, 1.15f, 1f, skin.Shade(0.94f));                       // nose
        foreach (float side in new[] { -1f, 1f })
        {
            Hd(side * 0.35f, 0.12f, -0.86f, 0.13f, 1.25f, 0.8f, 0.55f, Eye);                       // eye
            Hd(side * 0.35f, 0.11f, -0.93f, 0.075f, 1f, 1f, 0.5f, Iris);                           // iris
            Hd(side * 0.36f, 0.31f, -0.9f, 0.14f, 1.5f, 0.32f, 0.5f, hair.Shade(0.85f));          // brow
            Hd(side * 0.95f, 0.0f, 0.02f, 0.21f, 0.45f, 1f, 0.75f, skin.Shade(0.95f));             // ear
            Hd(side * 0.48f, -0.22f, -0.72f, 0.17f, 1f, 0.7f, 0.5f, skin.Lerp(Blush, 0.12f));        // cheek
        }
        Hd(0f, -0.43f, -0.9f, 0.2f, 1.45f, 0.32f, 0.5f, Lips);                                      // mouth, a smile
        if (beard) Hd(0f, -0.58f, -0.42f, 0.62f, 0.95f, 0.68f, 0.82f, hair, 1);
        // Hair
        switch (style)
        {
            case 0: Hd(0f, 0.2f, 0.1f, 1.05f, 0.97f, 0.95f, 1.0f, hair, 1); break;                                   // short
            case 1: Hd(0f, 0.22f, 0.1f, 1.05f, 0.98f, 0.95f, 1.02f, hair, 1); Hd(0.35f, 0.62f, -0.55f, 0.45f, 1.3f, 0.5f, 0.9f, hair); break;   // swept to the side
            case 2: Hd(0f, 0.2f, 0.12f, 1.02f, 0.95f, 0.92f, 0.98f, hair, 1); break;                                // cropped
            case 3: Hd(0f, 0.25f, 0.1f, 1.1f, 1.02f, 0.98f, 1.05f, hair, 1); Hd(0f, 0.62f, -0.35f, 0.55f, 1.4f, 0.6f, 1f, hair); break;       // curly top
            case 4:                                                                                                   // long and straight
                Hd(0f, 0.2f, 0.1f, 1.07f, 1.0f, 0.97f, 1.04f, hair, 1);
                Hd(0f, -0.55f, 0.48f, 0.95f, 1.08f, 1.55f, 0.55f, hair, 1);
                Hd(-0.82f, -0.45f, -0.05f, 0.38f, 0.55f, 1.8f, 0.8f, hair); Hd(0.82f, -0.45f, -0.05f, 0.38f, 0.55f, 1.8f, 0.8f, hair);
                break;
            case 5:                                                                                                   // ponytail
                Hd(0f, 0.2f, 0.12f, 1.06f, 0.98f, 0.95f, 1.03f, hair, 1);
                Hd(0f, 0.05f, 1.02f, 0.3f, 0.8f, 0.8f, 0.8f, hair);
                Hd(0f, -0.55f, 1.12f, 0.32f, 0.8f, 2.1f, 0.8f, hair);
                break;
            case 6:                                                                                                   // a bun on top
                Hd(0f, 0.2f, 0.12f, 1.06f, 0.98f, 0.95f, 1.03f, hair, 1);
                Hd(0f, 0.95f, 0.45f, 0.42f, 1f, 0.85f, 1f, hair);
                break;
            default:                                                                                                  // a bob
                Hd(0f, 0.18f, 0.12f, 1.08f, 1.02f, 0.98f, 1.06f, hair, 1);
                Hd(-0.78f, -0.38f, 0.1f, 0.42f, 0.6f, 1.3f, 1.1f, hair); Hd(0.78f, -0.38f, 0.1f, 0.42f, 0.6f, 1.3f, 1.1f, hair);
                Hd(0f, -0.35f, 0.62f, 0.75f, 1.2f, 1.0f, 0.7f, hair);
                break;
        }
        return b.Done();
    }
}
