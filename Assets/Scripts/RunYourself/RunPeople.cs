using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The people of a town in the run-yourself game mode (sprint maps, 2026-11-20, Anders: people who watch, sit and walk
// with fun animations). TvWorldBuilderTown puts them out (TvWorld.people); those sitting on benches are part of the
// street and never move. Here, for the pieces round the runner only:
//  - watchers stand by the street; when the runner comes within 25 m they turn to follow, within 12 m they cheer
//    with their arms up and bounce on their toes (children jump);
//  - walkers stroll back and forth along the street, their legs swinging.
// Nothing is moved further than 70 m from the runner. Each pose is one small shared mesh; a person is one renderer.
public class RunPeople : MonoBehaviour
{
    const float AnimateReach = 70f, TurnReach = 25f, CheerReach = 12f;

    Material material;
    bool shadows;
    readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
    readonly Dictionary<long, List<Person>> byPiece = new Dictionary<long, List<Person>>();
    Vector3 player;
    bool hasPlayer;

    class Person
    {
        public Transform t;
        public MeshFilter filter;
        public TvWorld world;
        public Vector3 home;            // in the piece's metres
        public float yaw, len, phase, turned;
        public int kind, look, shown = -1;
    }

    enum Pose { Stand, Cheer, Child, WalkA, WalkB }

    public void Init(Material material, bool shadows)
    {
        this.material = material;
        this.shadows = shadows;
    }

    public void SetPlayer(Vector3 scene)
    {
        player = scene;
        hasPlayer = true;
    }

    Mesh MeshFor(Pose pose, int look)
    {
        int key = (int)pose * 64 + look;
        if (meshes.TryGetValue(key, out Mesh mesh)) return mesh;
        var random = new TvRandom(key * 13 + 5);
        TvShape shape;
        switch (pose)
        {
            case Pose.Cheer: shape = TvCityShapes.Person(random, TvCityShapes.Pose.Cheer, look); break;
            case Pose.Child: shape = TvCityShapes.Person(random, TvCityShapes.Pose.Child, look); break;
            case Pose.WalkA: shape = TvCityShapes.Person(random, TvCityShapes.Pose.Walk, look); break;
            case Pose.WalkB: shape = Mirror(TvCityShapes.Person(random, TvCityShapes.Pose.Walk, look)); break;
            default: shape = TvCityShapes.Person(random, TvCityShapes.Pose.Stand, look); break;
        }
        mesh = TvWorldView.ToMesh(shape, "Person");
        mesh.UploadMeshData(true);
        meshes[key] = mesh;
        return mesh;
    }

    // The walking pose with the other leg forward: mirrored left to right, the triangles turned so they still face out
    static TvShape Mirror(TvShape s)
    {
        var p = (float[])s.positions.Clone();
        var n = (float[])s.normals.Clone();
        for (int i = 0; i < p.Length; i += 3) { p[i] = -p[i]; n[i] = -n[i]; }
        var t = (int[])s.triangles.Clone();
        for (int i = 0; i + 2 < t.Length; i += 3) { int a = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = a; }
        return new TvShape { positions = p, normals = n, colors = s.colors, triangles = t };
    }

    public void Spawn(long key, Transform root, TvWorld world)
    {
        if (byPiece.ContainsKey(key) || root == null) return;
        var list = new List<Person>();
        List<float> d = world.people;
        for (int i = 0; i + 5 < d.Count; i += 6)
        {
            var person = new Person
            {
                world = world,
                home = new Vector3(d[i], 0f, d[i + 1]),
                yaw = d[i + 2],
                kind = (int)d[i + 3],
                look = (int)d[i + 4] % 64,
                len = d[i + 5],
                phase = (float)((i * 0.61803) % 1.0) * 10f
            };
            person.turned = person.yaw;
            var go = new GameObject("Person");
            go.transform.SetParent(root, false);
            person.t = go.transform;
            person.filter = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            Show(person, person.kind == 1 ? Pose.WalkA : person.kind == 2 ? Pose.Child : Pose.Stand);
            Place(person, person.home, person.yaw, 0f);
            list.Add(person);
        }
        byPiece[key] = list;
    }

    public void Despawn(long key)
    {
        if (!byPiece.TryGetValue(key, out List<Person> list)) return;
        foreach (Person p in list) if (p.t != null) Destroy(p.t.gameObject);
        byPiece.Remove(key);
    }

    void Show(Person p, Pose pose)
    {
        if (p.shown == (int)pose) return;
        p.shown = (int)pose;
        p.filter.sharedMesh = MeshFor(pose, p.look);
    }

    void Place(Person p, Vector3 at, float yaw, float lift)
    {
        float y = p.world.HeightAt(at.x, at.z) + lift;
        p.t.localPosition = new Vector3(at.x, y, at.z);
        p.t.localRotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
    }

    void Update()
    {
        if (!hasPlayer) return;
        float time = Time.time, dt = Time.deltaTime;
        foreach (List<Person> list in byPiece.Values)
        {
            foreach (Person p in list)
            {
                if (p.t == null) continue;
                Vector3 at = p.t.position;
                float dx = player.x - at.x, dz = player.z - at.z, d2 = dx * dx + dz * dz;
                if (d2 > AnimateReach * AnimateReach) continue;
                if (p.kind == 1) Walk(p, time);
                else Watch(p, time, dt, dx, dz, d2);
            }
        }
    }

    // Standing by the street: turn to the runner, cheer when close
    void Watch(Person p, float time, float dt, float dx, float dz, float d2)
    {
        float want = p.yaw + Mathf.Sin(time * 0.4f + p.phase) * 0.25f;
        if (d2 < TurnReach * TurnReach) want = Mathf.Atan2(-dx, -dz);
        p.turned = Mathf.LerpAngle(p.turned * Mathf.Rad2Deg, want * Mathf.Rad2Deg, Mathf.Clamp01(dt * 4f)) * Mathf.Deg2Rad;
        bool cheer = d2 < CheerReach * CheerReach;
        float lift = 0f;
        if (p.kind == 2)
        {
            Show(p, Pose.Child);
            if (cheer) lift = Mathf.Abs(Mathf.Sin(time * 7f + p.phase)) * 0.3f;
        }
        else
        {
            Show(p, cheer ? Pose.Cheer : Pose.Stand);
            if (cheer) lift = Mathf.Abs(Mathf.Sin(time * 8.5f + p.phase)) * 0.1f;
        }
        Place(p, p.home, p.turned, lift);
    }

    // Back and forth along the street at an easy pace
    void Walk(Person p, float time)
    {
        const float Pace = 1.3f;
        float len = Mathf.Max(1f, p.len), cycle = (time * Pace + p.phase) % (2f * len);
        bool back = cycle > len;
        float s = (back ? 2f * len - cycle : cycle) - len / 2f;
        // Facing: local -z along the street, the other way on the way back
        float fx = -Mathf.Sin(p.yaw), fz = -Mathf.Cos(p.yaw);
        Vector3 at = p.home + new Vector3(fx, 0f, fz) * s;
        Show(p, ((int)(time * 3.2f + p.phase) & 1) == 0 ? Pose.WalkA : Pose.WalkB);
        Place(p, at, back ? p.yaw + Mathf.PI : p.yaw, Mathf.Abs(Mathf.Sin(time * Mathf.PI * 3.2f)) * 0.03f);
    }

    void OnDestroy()
    {
        foreach (Mesh m in meshes.Values) if (m != null) Destroy(m);
        meshes.Clear();
    }

    // ---- The grain of a town's streets (repeated every 5 m by the shader) ----

    static Texture2D cobbles, asphalt;

    // Rounded stones about 20 cm across with dark joints between them
    public static Texture2D Cobbles()
    {
        if (cobbles != null) return cobbles;
        const int Size = 256, Cells = 26;
        var random = new System.Random(11);
        var px = new float[Cells * Cells];
        var pz = new float[Cells * Cells];
        var tone = new float[Cells * Cells];
        for (int i = 0; i < px.Length; i++)
        {
            px[i] = (float)random.NextDouble() * 0.7f + 0.15f;
            pz[i] = (float)random.NextDouble() * 0.7f + 0.15f;
            tone[i] = 0.8f + 0.2f * (float)random.NextDouble();
        }
        var pixels = new Color32[Size * Size];
        float scale = Cells / (float)Size;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float gx = x * scale, gy = y * scale;
                int cx = (int)gx, cy = (int)gy;
                float best = 9f, second = 9f;
                int which = 0;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int nx = cx + ox, ny = cy + oy, wx = (nx + Cells) % Cells, wy = (ny + Cells) % Cells, k = wy * Cells + wx;
                        float ddx = nx + px[k] - gx, ddy = ny + pz[k] - gy, d = ddx * ddx + ddy * ddy;
                        if (d < best) { second = best; best = d; which = k; }
                        else if (d < second) second = d;
                    }
                float edge = Mathf.Sqrt(second) - Mathf.Sqrt(best);
                float stone = tone[which] * (1f - 0.18f * Mathf.Sqrt(best));
                float joint = Mathf.Clamp01((edge - 0.05f) / 0.11f);
                float v = Mathf.Lerp(0.5f, stone, joint * joint * (3f - 2f * joint)) + ((float)random.NextDouble() - 0.5f) * 0.05f;
                byte b = (byte)Mathf.Clamp(v * 255f, 0f, 255f);
                pixels[y * Size + x] = new Color32(b, b, b, 255);
            }
        }
        cobbles = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Town cobbles", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        cobbles.SetPixels32(pixels);
        cobbles.Apply(true, true);
        return cobbles;
    }

    // Fine stones in tar, with a few lighter specks
    public static Texture2D Asphalt()
    {
        if (asphalt != null) return asphalt;
        const int Size = 256;
        var random = new System.Random(23);
        var pixels = new Color32[Size * Size];
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = 0.82f + 0.12f * (float)random.NextDouble();
            if (random.NextDouble() < 0.04) v = 1f;
            byte b = (byte)(v * 255f);
            pixels[i] = new Color32(b, b, b, 255);
        }
        asphalt = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Town asphalt", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        asphalt.SetPixels32(pixels);
        asphalt.Apply(true, true);
        return asphalt;
    }
}
