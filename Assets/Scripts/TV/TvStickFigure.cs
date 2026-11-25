using System.Collections.Generic;
using UnityEngine;

// A runner in the 3D views (TV controls, run-yourself mass starts): a real-looking orienteer in the team's colours.
// Since 2026-11-25 (Anders: "the same type of person, as human as possible") the runner is one smooth skinned body
// (TV/TvHuman.cs: trunk, limbs, hands, shoes, a sculpted face and hair, an orienteering suit with side panels, long
// O-trousers or shorts and long socks) moved by 16 bones in a SkinnedMeshRenderer, and runs in one of twelve running
// styles (TV/TvRunnerGait.cs: hips and shoulders turning, arms tight, flapping or crossing, heel or forefoot landing,
// heel kick, bounce). It holds a paper map in one hand and wears a number bib. Looks and style are picked from the
// name, so the same runner always looks and runs the same way. Near the control the runner slows its stride and
// reaches for the punching unit with the other hand. (Named "stick figure" from its first version.)
public class TvStickFigure
{
    static Mesh mapMesh, bibMesh;
    static Material paintedMaterial;

    readonly GameObject root;
    readonly Transform map, bib;
    readonly Transform[] bones = new Transform[TvHuman.BoneCount];
    readonly Mesh bodyMesh;
    readonly TvRunnerGait gait;

    public float phase { get => gait.phase; set => gait.phase = value; }
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
    public float Pace => gait.Pace;
    public float slope, hop, hopHeight = 0.4f;     // smoothed: the ground's rise ahead, and how far into a jump over a trunk
    // After the finish: tired (0..1, bent over with the hands on the knees getting the breath back) and walking (0..1)
    public float tired, walk;
    public int readMode = -1;        // -1 reads the map now and then by itself; 1 reading (the player holds it up); 0 not
    public float leaveX, leaveZ;     // the way it walks off after the finish
    // The map stays in one hand (the left for most runners); the other hand punches
    public int mapHand { get => gait.mapHand; set => gait.mapHand = value; }

    public TvStickFigure(Transform parent, string name, Color color, int seed)
    {
        MakeShared();
        root = new GameObject("Runner " + name);
        root.transform.SetParent(parent, false);
        var random = new System.Random(seed);
        Color32 c = color;
        TvHuman.Look look = TvHuman.RunnerLook(random, new TvColor(c.r, c.g, c.b));
        TvHuman.Body body = TvHuman.Make(look);
        gait = new TvRunnerGait(body, random);

        // The skin: one mesh on 16 bones
        bodyMesh = new Mesh { name = "Runner body" };
        int vc = body.VertexCount;
        var vertices = new Vector3[vc];
        var normals = new Vector3[vc];
        var colors = new Color32[vc];
        var weights = new BoneWeight[vc];
        for (int i = 0; i < vc; i++)
        {
            vertices[i] = new Vector3(body.pos[i * 3], body.pos[i * 3 + 1], body.pos[i * 3 + 2]);
            normals[i] = new Vector3(body.nrm[i * 3], body.nrm[i * 3 + 1], body.nrm[i * 3 + 2]);
            colors[i] = new Color32(body.col[i * 3], body.col[i * 3 + 1], body.col[i * 3 + 2], 255);
            float w0 = body.w0[i];
            weights[i] = body.b0[i] == body.b1[i] || w0 >= 0.999f
                ? new BoneWeight { boneIndex0 = body.b0[i], weight0 = 1f }
                : new BoneWeight { boneIndex0 = body.b0[i], weight0 = w0, boneIndex1 = body.b1[i], weight1 = 1f - w0 };
        }
        bodyMesh.vertices = vertices;
        bodyMesh.normals = normals;
        bodyMesh.colors32 = colors;
        bodyMesh.triangles = body.tri;
        bodyMesh.boneWeights = weights;
        var bind = new Matrix4x4[TvHuman.BoneCount];
        for (int b = 0; b < TvHuman.BoneCount; b++)
        {
            TvHuman.Frame f = body.restFrames[b];
            var go = new GameObject("Bone " + b);
            go.transform.SetParent(root.transform, false);
            bones[b] = go.transform;
            bind[b] = Matrix4x4.TRS(V(f.o), Turn(f), Vector3.one).inverse;
        }
        bodyMesh.bindposes = bind;
        bodyMesh.RecalculateBounds();

        var skinGo = new GameObject("Body");
        skinGo.transform.SetParent(root.transform, false);
        var skin = skinGo.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = bodyMesh;
        skin.bones = bones;
        skin.rootBone = bones[TvHuman.Pelvis];
        skin.localBounds = new Bounds(Vector3.zero, new Vector3(2.4f, 2.4f, 2.4f));
        skin.quality = SkinQuality.Bone2;
        skin.sharedMaterial = paintedMaterial;
        skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        Pose(bones, body.restFrames);

        map = Part("Map", mapMesh);
        bib = Part("Bib", bibMesh);
    }

    static Vector3 V(V3 v) => new Vector3(v.x, v.y, v.z);
    static V3 V(Vector3 v) => new V3(v.x, v.y, v.z);
    static Quaternion Turn(TvHuman.Frame f) => Quaternion.LookRotation(V(f.z), V(f.y));

    static void Pose(Transform[] bones, TvHuman.Frame[] frames)
    {
        for (int b = 0; b < frames.Length; b++)
        {
            bones[b].localPosition = V(frames[b].o);
            bones[b].localRotation = Turn(frames[b]);
        }
    }

    Transform Part(string name, Mesh mesh)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = paintedMaterial;
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
        Object.Destroy(bodyMesh);
    }

    // ---- Meshes, made once ----

    static void MakeShared()
    {
        if (mapMesh != null) return;
        paintedMaterial = TvWorldView.NewMaterial(Color.white);   // the colours are in the meshes

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


    // Moves the runner (on the ground at x, z) by the distance it ran since the last frame and poses it.
    // ground: the ground's height there; unit: the punching unit; near: 0 far from the control, 1 at it.
    // slope: how much the ground rises per metre ahead (negative downhill); hop: 0..1 through a jump over a fallen trunk.
    public void Pose(float x, float ground, float z, float moved, Vector3 unit, float near, float dt = 0f, float slope = 0f, float hop = 0f, float hurdle = 0.4f)
    {
        gait.forward = V(forward);
        gait.tired = tired;
        gait.walk = walk;
        gait.readMode = readMode;
        gait.Pose(x, ground, z, moved, V(unit), near, dt, slope, hop, hurdle);
        position = V(gait.position);
        Pose(bones, TvHuman.Frames(gait.rig));
        map.localPosition = V(gait.mapPos);
        map.localRotation = Quaternion.LookRotation(V(gait.mapFwd), V(gait.mapUp));
        bib.localPosition = V(gait.bibPos);
        bib.localRotation = Quaternion.LookRotation(V(gait.bibFwd), V(gait.bibUp));
    }
}
