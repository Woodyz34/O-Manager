using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

// The people of a town in the run-yourself game mode (sprint maps, 2026-11-20, Anders: people who watch, sit and walk
// with fun animations). TvWorldBuilderTown puts them out (TvWorld.people); those sitting on benches are part of the
// street and never move. Here, for the pieces round the runner only:
//  - watchers stand by the street, shifting their weight and looking about, some at their phones; when the runner
//    comes within 25 m they turn to follow, within 12 m they cheer with their arms up, clap or wave (children jump);
//  - walkers stroll back and forth along the street, their steps in time with the ground they cover.
// The bodies, faces and animations are TvCityShapes.Figure / Animate (TV/TvPeople.cs).
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

    // One mesh per person look, animation and frame, made when first needed. The body (TvCityShapes.Figure) is put
    // together on a background thread and only a few are handed to the graphics card a frame (2026-11-26: made on the
    // main thread, a street full of new people cost 50-150 ms a frame on sprint maps); until a pose is ready the
    // person keeps the one shown.
    readonly ConcurrentDictionary<int, TvShape> made = new ConcurrentDictionary<int, TvShape>();
    readonly HashSet<int> asked = new HashSet<int>();
    int uploadsLeft;
    const int UploadsPerFrame = 6;

    Mesh MeshFor(TvCityShapes.Act act, int frame, int look, bool child)
    {
        int key = (((int)act * TvCityShapes.Frames + frame) * 64 + look) * 2 + (child ? 1 : 0);
        if (meshes.TryGetValue(key, out Mesh mesh)) return mesh;
        if (made.TryGetValue(key, out TvShape shape))
        {
            if (uploadsLeft <= 0) return null;
            uploadsLeft--;
            made.TryRemove(key, out _);
            mesh = TvWorldView.ToMesh(shape, "Person");
            mesh.UploadMeshData(true);
            meshes[key] = mesh;
            return mesh;
        }
        Ask(act, frame, look, child, key);
        return null;
    }

    void Ask(TvCityShapes.Act act, int frame, int look, bool child, int key)
    {
        if (!asked.Add(key)) return;
        Task.Run(() =>
        {
            try { made[key] = TvCityShapes.Figure(new TvRandom(look * 13 + 5), look, child, TvCityShapes.Animate(act, frame, look)); }
            catch (Exception e) { Debug.LogWarning("RunPeople: " + e.Message); }
        });
    }

    // All the frames of the poses a person may take, asked for ahead (when the person is put out)
    void AskAhead(Person p)
    {
        bool child = p.kind == 2;
        var acts = p.kind == 1 ? new[] { TvCityShapes.Act.Walk }
            : child ? new[] { TvCityShapes.Act.Idle, TvCityShapes.Act.Jump }
            : new[] { TvCityShapes.Act.Idle, p.look % 4 == 3 ? TvCityShapes.Act.Phone : TvCityShapes.Act.Idle,
                      p.look % 3 == 0 ? TvCityShapes.Act.Cheer : p.look % 3 == 1 ? TvCityShapes.Act.Clap : TvCityShapes.Act.Wave };
        foreach (TvCityShapes.Act act in acts)
            for (int f = 0; f < TvCityShapes.Frames; f++)
            {
                int key = (((int)act * TvCityShapes.Frames + f) * 64 + p.look) * 2 + (child ? 1 : 0);
                if (!meshes.ContainsKey(key)) Ask(act, f, p.look, child, key);
            }
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
            AskAhead(person);
            Show(person, person.kind == 1 ? TvCityShapes.Act.Walk : TvCityShapes.Act.Idle, 0);
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

    void Show(Person p, TvCityShapes.Act act, int frame)
    {
        int shown = (int)act * TvCityShapes.Frames + frame;
        if (p.shown == shown) return;
        Mesh mesh = MeshFor(act, frame, p.look, p.kind == 2);
        if (mesh == null) return;     // not made yet: the pose shown stays (none at first: not drawn)
        p.shown = shown;
        p.filter.sharedMesh = mesh;
    }

    static int Frame(float cycles) => ((int)(cycles * TvCityShapes.Frames) % TvCityShapes.Frames + TvCityShapes.Frames) % TvCityShapes.Frames;

    void Place(Person p, Vector3 at, float yaw, float lift)
    {
        float y = p.world.HeightAt(at.x, at.z) + lift;
        p.t.localPosition = new Vector3(at.x, y, at.z);
        p.t.localRotation = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
    }

    void Update()
    {
        uploadsLeft = UploadsPerFrame;
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

    // Standing by the street: looking about (or at a phone); turning to follow the runner within 25 m; cheering,
    // clapping or waving within 12 m (each by their own look); children jump up and down
    void Watch(Person p, float time, float dt, float dx, float dz, float d2)
    {
        float want = p.yaw + Mathf.Sin(time * 0.4f + p.phase) * 0.25f;
        if (d2 < TurnReach * TurnReach) want = Mathf.Atan2(-dx, -dz);
        p.turned = Mathf.LerpAngle(p.turned * Mathf.Rad2Deg, want * Mathf.Rad2Deg, Mathf.Clamp01(dt * 4f)) * Mathf.Deg2Rad;
        bool close = d2 < CheerReach * CheerReach;
        float lift = 0f;
        if (!close)
        {
            // A slow loop of about five seconds
            Show(p, p.look % 4 == 3 && p.kind != 2 ? TvCityShapes.Act.Phone : TvCityShapes.Act.Idle, Frame(time * 0.2f + p.phase));
        }
        else if (p.kind == 2)
        {
            float cyc = time * 1.4f + p.phase;
            Show(p, TvCityShapes.Act.Jump, Frame(cyc));
            lift = 0.3f * Mathf.Max(0f, Mathf.Cos(cyc * 2f * Mathf.PI));
        }
        else
        {
            switch (p.look % 3)
            {
                case 0:
                    float cyc = time * 1.2f + p.phase;
                    Show(p, TvCityShapes.Act.Cheer, Frame(cyc));
                    lift = Mathf.Abs(Mathf.Sin(cyc * 2f * Mathf.PI)) * 0.07f;
                    break;
                case 1: Show(p, TvCityShapes.Act.Clap, Frame(time * 1.1f + p.phase)); break;
                default: Show(p, TvCityShapes.Act.Wave, Frame(time * 1.0f + p.phase)); break;
            }
        }
        Place(p, p.home, p.turned, lift);
    }

    // Back and forth along the street at an easy pace, the steps in time with the ground covered
    void Walk(Person p, float time)
    {
        const float Pace = 1.3f, Stride = 1.35f;   // metres a second; metres a full step cycle (two steps)
        float len = Mathf.Max(1f, p.len), walked = time * Pace + p.phase, cycle = walked % (2f * len);
        bool back = cycle > len;
        float s = (back ? 2f * len - cycle : cycle) - len / 2f;
        // Facing: local -z along the street, the other way on the way back
        float fx = -Mathf.Sin(p.yaw), fz = -Mathf.Cos(p.yaw);
        Vector3 at = p.home + new Vector3(fx, 0f, fz) * s;
        Show(p, TvCityShapes.Act.Walk, Frame(walked / Stride));
        Place(p, at, back ? p.yaw + Mathf.PI : p.yaw, 0f);
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
