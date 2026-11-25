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

    // A person of a look in the given joints. child: a child of about 1.2 m. detail: 0 for crowds far off, 1 near.
    // (2026-11-25) The body is TvHuman's smooth skinned body, the same kind of person as the runners: the joints
    // below are turned into a pose of its bones and the skin follows.
    public static TvShape Figure(TvRandom random, int look, bool child, Joints j, int detail = 1)
    {
        int l = Math.Abs(look);
        TvHuman.Look lk = TownLook(l, child, j.sit, detail);
        TvHuman.Body body = TvHuman.Make(lk, "town/" + l + "/" + (child ? 1 : 0) + "/" + (j.sit ? 1 : 0) + "/" + detail);
        TvHuman.Rig rig = TownRig(body, j);
        TvShape shape = TvHuman.Skin(body, rig);
        // Things in the right hand: a phone, a flag
        if (j.phone || j.flag)
        {
            var b = new TvShapeBuilder(random);
            V3 h = rig.wrist[1] + (rig.hand[1] - rig.wrist[1]) * 0.5f;
            if (j.phone)
                b.Box(h.x - 0.04f, h.y - 0.02f, h.z + 0.01f, h.x + 0.04f, h.y + 0.12f, h.z + 0.025f, TvColor.Hex("#1c1d22"));
            if (j.flag)
            {
                Beam(b, h.x, h.y - 0.05f, h.z, h.x, h.y + 0.5f, h.z, 0.02f, 0.02f, Wood);
                SwedishFlag(b, h.x, h.y + 0.5f, h.z, 0.4f, 0f);
            }
            shape = TvHuman.Join(shape, b.Done());
        }
        // The town's shapes face -z
        TvHuman.TurnAround(shape);
        return shape;
    }

    // What a person of a look wears and how they are built (the same mapping as the first town people)
    public static TvHuman.Look TownLook(int l, bool child, bool sit, int detail)
    {
        bool female = l % 2 == 1;
        var lk = new TvHuman.Look
        {
            female = female,
            child = child,
            height = child ? 1.12f + 0.08f * (l / 3 % 3) : (female ? 1.64f : 1.78f) + 0.05f * (l * 7 % 5 - 2),
            girth = child ? 0.95f : 0.88f + 0.09f * (l * 5 % 5),
            skin = Skins[l * 3 % Skins.Length == 6 && l % 5 != 0 ? 1 : l * 3 % Skins.Length],
            hair = Hairs[(l / 2 + l * 3) % Hairs.Length],
            top = Clothes[(l * 7 + 1) % Clothes.Length],
            bottom = Trousers[(l / 3) % Trousers.Length],
            shoe = l % 4 == 1 ? TvColor.Hex("#e8e6e0") : l % 4 == 2 ? TvColor.Hex("#5a3a28") : Shoe,
            sole = TvColor.Hex("#3a3532"),
            detail = detail
        };
        if (!child && !female && l % 9 == 4) lk.hair = Hairs[4];   // some grey
        lk.top2 = lk.top.Shade(0.8f);
        lk.sleeves = l % 3 == 2 ? 1 : 2;
        bool skirt = female && !sit && !child && l % 3 == 0;
        lk.legs = skirt ? 2 : (l % 7 == 3 && !female ? 1 : 0);
        if (skirt) { lk.skirt = Clothes[(l * 3 + 4) % Clothes.Length].Shade(0.9f); lk.bottom = TvColor.Hex("#2a2a30"); }
        lk.beard = !female && !child && l % 5 == 1;
        lk.hairStyle = female ? 4 + (l / 2) % 4 : (l / 2) % 4;
        return lk;
    }

    // The bones' pose for a town person's joints (facing +z here; Figure turns the shape round)
    static TvHuman.Rig TownRig(TvHuman.Body body, Joints j)
    {
        TvHuman.Rig rest = body.rest;
        var r = rest.Copy();
        float kb = body.kb;
        float hipY = (j.sit ? 0.47f + 0.05f * kb : rest.hip.y) + j.rise;
        V3 bodyUp = new V3(0f, (float)Math.Cos(j.lean), (float)Math.Sin(j.lean));
        r.hip = new V3(0f, hipY, 0f);
        float waistUp = (rest.waist - rest.hip).Length, chestUp = rest.chest.y - rest.hip.y, neckLen = (rest.neckTop - rest.chest).Length;
        r.waist = r.hip + bodyUp * waistUp;
        r.chest = r.hip + bodyUp * chestUp;
        V3 neckDir = new V3(0f, (float)Math.Cos(j.lean * 0.5f + 0.1f), (float)Math.Sin(j.lean * 0.5f + 0.1f));
        r.neckTop = r.chest + neckDir * neckLen;
        r.right = V3.Right; r.chestRight = V3.Right;
        // The head: turned by yaw, nodding by pitch (forward is +z)
        float cy = (float)Math.Cos(j.headYaw), sy = (float)Math.Sin(j.headYaw), cp = (float)Math.Cos(j.headPitch), sp = (float)Math.Sin(j.headPitch);
        r.headFwd = new V3(sy * cp, -sp, cy * cp);
        r.headUp = new V3(sy * sp, cp, cy * sp);
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            float hip = i == 0 ? j.hipL : j.hipR, knee = i == 0 ? j.kneeL : j.kneeR;
            V3 hj = r.hip + new V3(rest.hipJ[i].x, 0f, 0f);
            V3 kn = hj + new V3(rest.knee[i].x - rest.hipJ[i].x, -body.thigh * (float)Math.Cos(hip), body.thigh * (float)Math.Sin(hip));
            float sa = hip - knee;
            V3 an = kn + new V3(rest.ankle[i].x - rest.knee[i].x, -body.shin * (float)Math.Cos(sa), body.shin * (float)Math.Sin(sa));
            if (!j.sit && an.y < rest.ankle[i].y)
            {
                // A foot never goes into the ground: the body rises on it
                float lift = rest.ankle[i].y - an.y;
                an.y += lift; kn.y += lift * 0.5f;
            }
            // The foot points ahead, flat on the ground when standing
            V3 toe = an + (rest.toe[i] - rest.ankle[i]);
            r.hipJ[i] = hj; r.knee[i] = kn; r.ankle[i] = an; r.toe[i] = toe;

            float arm = i == 0 ? j.armL : j.armR, raise = i == 0 ? j.raiseL : j.raiseR, elbow = i == 0 ? j.elbowL : j.elbowR;
            V3 sh = r.chest + bodyUp * (rest.shoulder[i].y - rest.chest.y) + new V3(rest.shoulder[i].x, 0f, 0f);
            V3 Dir(float swing)
            {
                float fy = -(float)Math.Cos(swing + j.lean), fz = (float)Math.Sin(swing + j.lean);
                return new V3(side * Math.Abs(fy) * (float)Math.Sin(raise) + side * 0.06f, fy * (float)Math.Cos(raise), fz).Normalized;
            }
            V3 el = sh + Dir(arm) * body.upper;
            V3 fd = Dir(arm + elbow);
            V3 wr = el + fd * body.fore;
            r.shoulder[i] = sh; r.elbow[i] = el; r.wrist[i] = wr; r.hand[i] = wr + fd * body.handLen;
        }
        return r;
    }
}
