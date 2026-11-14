using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

// The 3D world of one TV control in the scene: the meshes TvWorldBuilder made, a sun, a camera that draws into a
// texture (shown by TvViewer) and the stick figures of the runners. The world is put far away from the 2D map
// (Origin), and only one is kept at a time. The sun and its shadows are switched on only while the TV camera draws, so
// the rest of the game looks as before; the light, the tone and the fog are worked out in the shader (TvVertexLit).
public class TvWorldView : MonoBehaviour
{
    // Every world gets a place of its own, far from the map and from the other worlds
    static readonly Vector3 Origin = new Vector3(4000f, 0f, 0f);
    const float Apart = 1000f;
    const int Places = 12;
    static int nextPlace;
    static readonly Color Sky = new Color32(0xc7, 0xd6, 0xda, 255);

    public CoursePoint control;
    public string mapId;
    public TvWorld world;
    public Camera cam;
    Light sun;
    RenderTexture texture;
    readonly List<Material> materials = new List<Material>();
    readonly List<Mesh> meshes = new List<Mesh>();

    // ---- Camera ----
    Vector3 lookAt;
    float tvFov = 34f;
    Vector3 camShift;
    bool hadSubject;
    // Free camera (only in the course builder's View): turns round a point
    Vector3 orbitTarget;
    float yaw, pitch = 18f, distance = 14f;

    // A new shot: the camera goes straight to the first runner
    public void NewShot()
    {
        hadSubject = false;
        camShift = Vector3.zero;
    }

    static Shader shader;
    static Texture2D grain;

    // Fine light and dark grains for the ground, the same every time
    static Texture2D Grain()
    {
        if (grain != null) return grain;
        const int Size = 128;
        var random = new System.Random(7);
        var pixels = new Color32[Size * Size];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = (byte)(200 + random.Next(55));
            pixels[i] = new Color32(v, v, v, 255);
        }
        grain = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "TV ground grain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        grain.SetPixels32(pixels);
        grain.Apply(true, true);
        return grain;
    }

    public static Material NewMaterial(Color color)
    {
        if (shader == null) shader = Resources.Load<Shader>("Shaders/TvVertexLit");
        if (shader == null) shader = Shader.Find("O-Manager/TvVertexLit");
        var material = new Material(shader != null ? shader : Shader.Find("Diffuse"));
        material.color = color;
        return material;
    }

    public static Mesh ToMesh(TvShape shape, string name)
    {
        var m = new TvMesh { name = name };
        m.Add(shape, 0f, 0f, 0f, 1f, 1f, 1f, 0f);
        return ToMesh(m);
    }

    public static Mesh ToMesh(TvMesh source)
    {
        return ToMesh(new MeshArrays(source));
    }

    // A mesh's corners as Unity wants them. Made on a background thread, so the main thread only hands them over.
    public class MeshArrays
    {
        public string name;
        public bool castShadows;
        public Vector3[] vertices, normals;
        public Color32[] colors;
        public int[] triangles;

        public MeshArrays(TvMesh source)
        {
            name = source.name;
            castShadows = source.castShadows;
            int count = source.VertexCount;
            vertices = new Vector3[count];
            normals = new Vector3[count];
            colors = new Color32[count];
            List<float> p = source.positions, n = source.normals;
            List<byte> c = source.colors;
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]);
                normals[i] = new Vector3(n[i * 3], n[i * 3 + 1], n[i * 3 + 2]);
                colors[i] = new Color32(c[i * 3], c[i * 3 + 1], c[i * 3 + 2], 255);
            }
            triangles = source.triangles.ToArray();
        }
    }

    static Mesh ToMesh(MeshArrays source)
    {
        var mesh = new Mesh { name = source.name };
        if (source.vertices.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = source.vertices;
        mesh.normals = source.normals;
        mesh.colors32 = source.colors;
        mesh.SetTriangles(source.triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Builds the world of a control. The heavy part runs on a background thread; returns null when the map has none.
    public static async Task<TvWorldView> Create(MapDefinition map, CoursePoint control, bool start = false, float camDirX = 0f, float camDirZ = 0f,
                                                  float legDirX = 0f, float legDirZ = 0f)
    {
        TvWorldData.Data data = await TvWorldData.LoadAsync(map);
        if (data == null) return null;
        var input = new TvWorldInput
        {
            layer = data.layer,
            heights = data.heights,
            look = data.look,
            centerE = control.e,
            centerN = control.n,
            radius = 110f,
            seed = 1,
            start = start,
            camDirX = camDirX,
            camDirZ = camDirZ,
            legDirX = legDirX,
            legDirZ = legDirZ
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // The world and the meshes' corners are made in the background; the race goes on meanwhile
        List<MeshArrays> arrays = null;
        TvWorld world = await Task.Run(() =>
        {
            TvWorld w = TvWorldBuilder.Build(input);
            arrays = new List<MeshArrays>(w.meshes.Count);
            foreach (TvMesh m in w.meshes) arrays.Add(new MeshArrays(m));
            return w;
        });
        long built = watch.ElapsedMilliseconds;
        var go = new GameObject("TV world");
        go.transform.position = Origin + new Vector3(nextPlace * Apart, 0f, 0f);
        nextPlace = (nextPlace + 1) % Places;
        TvWorldView view = go.AddComponent<TvWorldView>();
        view.control = control;
        view.mapId = map.id;
        view.world = world;
        view.Make();
        // The meshes go to the graphics card a few at a time, a little every frame, so the race never stops for it
        var frame = System.Diagnostics.Stopwatch.StartNew();
        long onMain = 0;
        foreach (MeshArrays m in arrays)
        {
            if (view == null) return null;   // the race ended meanwhile
            if (frame.ElapsedMilliseconds >= 4)
            {
                onMain += frame.ElapsedMilliseconds;
                await Task.Yield();          // the rest next frame
                if (view == null) return null;
                frame.Restart();
            }
            view.AddMesh(m);
        }
        onMain += frame.ElapsedMilliseconds;
        world.meshes.Clear();                // the plain copies are not needed any more
        Debug.Log($"TvWorldView: built the world at ({control.e:0}, {control.n:0}) on {map.id} in {built} ms (+ {onMain} ms meshes over {watch.ElapsedMilliseconds - built} ms): "
                  + $"{world.vertices} corners in {arrays.Count} tiles, {world.trees} trees, {world.rocks} stones, {world.tufts} tufts");
        return view;
    }

    Transform drawn;            // the meshes; switched off while this world is not on TV
    Material plainMaterial, groundMaterial;

    void AddMesh(MeshArrays source)
    {
        Mesh mesh = ToMesh(source);
        mesh.UploadMeshData(true);   // the graphics card has it now; no copy kept in memory
        meshes.Add(mesh);
        var go = new GameObject(source.name);
        go.transform.SetParent(drawn, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = source.name.StartsWith("Ground", StringComparison.Ordinal) ? groundMaterial : plainMaterial;
        r.shadowCastingMode = source.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        r.receiveShadows = true;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void Make()
    {
        plainMaterial = NewMaterial(Color.white);
        materials.Add(plainMaterial);
        // The ground has a fine grain, so it does not look painted
        groundMaterial = NewMaterial(Color.white);
        groundMaterial.SetTexture("_DetailTex", Grain());
        groundMaterial.SetFloat("_Detail", 1f);
        materials.Add(groundMaterial);
        drawn = new GameObject("Meshes").transform;
        drawn.SetParent(transform, false);
        drawn.gameObject.SetActive(false);   // until it is on TV

        // The sun in the south-east, a little low, as in the look test
        sun = new GameObject("TV sun").AddComponent<Light>();
        sun.transform.SetParent(transform, false);
        sun.type = LightType.Directional;
        sun.color = new Color32(0xff, 0xf1, 0xd6, 255);
        sun.intensity = 3f;   // the strongest light, so it is the one the shader gets; its colour is set in the shader
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        sun.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
        sun.shadowBias = 0.05f;
        sun.shadowNormalBias = 0.4f;
        sun.transform.rotation = Quaternion.LookRotation(-new Vector3(55f, 70f, -48f).normalized);
        sun.enabled = false;

        cam = new GameObject("TV camera").AddComponent<Camera>();
        cam.transform.SetParent(transform, false);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Sky;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 420f;
        cam.fieldOfView = tvFov;
        cam.depth = -50;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.renderingPath = RenderingPath.Forward;
        cam.enabled = false;

        float y = world.HeightAt(world.camX, world.camZ) + 1.7f;
        cam.transform.localPosition = new Vector3(world.camX, y, world.camZ);
        lookAt = new Vector3(world.flagX, world.HeightAt(world.flagX, world.flagZ) + 0.8f, world.flagZ);
        cam.transform.localRotation = Quaternion.LookRotation(lookAt - cam.transform.localPosition);

        Camera.onPreCull += BeforeDraw;
        Camera.onPostRender += AfterDraw;
    }

    // ---- Drawing into a texture ----

    // The texture the viewer shows, made to the size it needs
    public RenderTexture Texture(int width, int height)
    {
        width = Mathf.Max(64, width);
        height = Mathf.Max(64, height);
        if (texture == null || texture.width != width || texture.height != height)
        {
            if (texture != null)
            {
                cam.targetTexture = null;
                texture.Release();
                Destroy(texture);
            }
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.Default) { name = "TV", antiAliasing = 4 };
            cam.targetTexture = texture;
            cam.aspect = width / (float)height;
        }
        return texture;
    }

    // Only the world on TV is drawn; the others are switched off altogether, so no camera spends time on them
    public void SetDrawing(bool on)
    {
        if (cam != null && cam.enabled != on) cam.enabled = on;
        if (drawn != null && drawn.gameObject.activeSelf != on) drawn.gameObject.SetActive(on);
    }

    float savedShadowDistance;
    int savedCascades;
    ShadowQuality savedShadows;
    ShadowResolution savedResolution;
    ShadowProjection savedProjection;
    bool drawing;

    // The sun and good shadows only while the TV camera draws, so the rest of the game looks as before.
    // The light itself is worked out in the shader (TvVertexLit); the sun here gives its direction and the shadows.
    void BeforeDraw(Camera c)
    {
        if (c != cam || drawing) return;
        drawing = true;
        savedShadowDistance = QualitySettings.shadowDistance;
        savedShadows = QualitySettings.shadows;
        savedCascades = QualitySettings.shadowCascades;
        savedResolution = QualitySettings.shadowResolution;
        savedProjection = QualitySettings.shadowProjection;
        QualitySettings.shadowDistance = 130f;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        sun.enabled = true;
    }

    void AfterDraw(Camera c)
    {
        if (c != cam || !drawing) return;
        drawing = false;
        sun.enabled = false;
        QualitySettings.shadowDistance = savedShadowDistance;
        QualitySettings.shadows = savedShadows;
        QualitySettings.shadowCascades = savedCascades;
        QualitySettings.shadowResolution = savedResolution;
        QualitySettings.shadowProjection = savedProjection;
    }

    void OnDestroy()
    {
        Camera.onPreCull -= BeforeDraw;
        Camera.onPostRender -= AfterDraw;
        if (drawing) AfterDraw(cam);
        foreach (TvStickFigure f in figures.Values) f.Destroy();
        figures.Clear();
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
        foreach (Mesh m in meshes) Destroy(m);
        foreach (Material m in materials) Destroy(m);
    }

    // ---- Runners ----

    public struct RunnerAt
    {
        public object key;
        public string label;
        public Color color;
        public float x, z;      // metres from the control, east and north
        public bool finishing;  // finished in the race already: runs the last metres to the finish unit on its own
    }

    readonly Dictionary<object, TvStickFigure> figures = new Dictionary<object, TvStickFigure>();
    readonly HashSet<object> seen = new HashSet<object>();
    readonly List<object> gone = new List<object>();
    bool punching;
    // The runner's figure is in the picture and has not touched the punching unit yet: its split time waits for the punch
    // When the runner touched the punching unit on this pass (unscaled time), or -1
    public float PunchedAt(object key)
    {
        return figures.TryGetValue(key, out TvStickFigure figure) && figure.everPunched ? figure.punchedAt : -1f;
    }

    public bool PunchToCome(object key)
    {
        return world.hasUnit && figures.TryGetValue(key, out TvStickFigure figure) && !figure.everPunched;
    }

    // A runner is on its way to the flag or punching right now: the TV stays on it
    public bool Punching => punching;

    // The runners to show now; the ones not in the list are taken away. Returns the one nearest the control, if any.
    public Vector3? ShowRunners(List<RunnerAt> runners)
    {
        seen.Clear();
        punching = false;
        Vector3? nearest = null;
        float best = float.MaxValue;
        Vector3 unit = new Vector3(world.unitX, world.unitY, world.unitZ);
        foreach (RunnerAt r in runners)
        {
            seen.Add(r.key);
            if (!figures.TryGetValue(r.key, out TvStickFigure figure))
            {
                figure = new TvStickFigure(transform, r.label, r.color, (r.label ?? "").GetHashCode());
                figures[r.key] = figure;
            }
            figure.finishing = r.finishing;
            ToThePunch(figure, r.x, r.z);
            // Round the trees and stones with room to spare, and smoothly: the runner swerves, it does not bump
            float px = figure.shownX, pz = figure.shownZ;
            world.PushOut(ref px, ref pz, 0.5f);
            float ease = figure.placed ? 0.18f : 1f;
            figure.swerveX = Mathf.Lerp(figure.swerveX, px - figure.shownX, ease);
            figure.swerveZ = Mathf.Lerp(figure.swerveZ, pz - figure.shownZ, ease);
            float x = figure.shownX + figure.swerveX, z = figure.shownZ + figure.swerveZ;
            world.PushOut(ref x, ref z, 0.2f);   // never inside anything
            if (figure.punch == 1 || (figure.punch == 2 && Time.unscaledTime - figure.punchedAt < 0.6f)) punching = true;
            float moved = 0f;
            if (figure.placed)
            {
                Vector3 step = new Vector3(x - figure.position.x, 0f, z - figure.position.z);
                moved = step.magnitude;
                if (moved > 20f) moved = 0f;   // a jump: no running
                else if (moved > 0.01f) figure.forward = Vector3.Slerp(figure.forward, step / moved, Mathf.Clamp01(moved * 1.5f)).normalized;
            }
            figure.placed = true;
            float toUnit = new Vector2(x - world.unitX, z - world.unitZ).magnitude;
            float near = world.hasUnit ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 2.8f, toUnit)) : 0f;
            // The lie of the ground ahead (steps, slopes, cliffs) and the fallen trunks to jump
            float dt = Time.unscaledDeltaTime, rise = 0f;
            if (figure.placed)
            {
                Vector3 f = figure.forward;
                rise = (world.HeightAt(x + f.x * 0.6f, z + f.z * 0.6f) - world.HeightAt(x - f.x * 0.6f, z - f.z * 0.6f)) / 1.2f;
            }
            figure.slope = Mathf.Lerp(figure.slope, rise, Mathf.Clamp01(dt * 8f));
            float hurdle = 0f;
            float jump = figure.Pace > 1f && near < 0.5f ? world.Hurdle(x, z, out hurdle) : 0f;
            figure.hop = Mathf.Lerp(figure.hop, jump, Mathf.Clamp01(dt * 18f));
            if (jump > 0f) figure.hopHeight = hurdle;
            figure.Pose(x, world.HeightAt(x, z), z, moved, unit, near, dt, figure.slope, figure.hop, figure.hopHeight);
            figure.SetVisible(!WalkedOff(figure));
            if (Leaving(figure)) continue;   // the camera lets it walk out of the picture
            float d = x * x + z * z;
            if (d < best)
            {
                best = d;
                nearest = figure.position + Vector3.up * 1.1f;
            }
        }
        gone.Clear();
        foreach (object key in figures.Keys) if (!seen.Contains(key)) gone.Add(key);
        foreach (object key in gone)
        {
            figures[key].Destroy();
            figures.Remove(key);
        }
        return nearest;
    }

    // The route a runner really runs may cut the corner at a control, or pass some metres from it. On TV every runner
    // leaves its route for the last metres, goes up to the flag, punches and runs back onto its route. Only what is
    // shown changes; the race is as it was. The figure moves as far as the runner really moved, a little more to catch up.
    void ToThePunch(TvStickFigure f, float x, float z)
    {
        float moved = 0f;
        if (f.tracked)
        {
            float mx = x - f.realX, mz = z - f.realZ;
            moved = Mathf.Sqrt(mx * mx + mz * mz);
            if (moved > 20f)
            {
                // A jump (a new leg): start again
                moved = 0f;
                f.punch = 0;
                f.punched = false;
                f.everPunched = false;
                f.nearest = float.MaxValue;
                f.shownX = x;
                f.shownZ = z;
            }
            else if (moved > 0.001f)
            {
                f.headX = Mathf.Lerp(f.headX, mx / moved, 0.3f);
                f.headZ = Mathf.Lerp(f.headZ, mz / moved, 0.3f);
            }
        }
        else
        {
            f.shownX = x;
            f.shownZ = z;
        }
        f.tracked = true;
        f.realX = x;
        f.realZ = z;
        if (!world.hasUnit)
        {
            f.shownX = x;
            f.shownZ = z;
            return;
        }
        float ux = world.unitX - x, uz = world.unitZ - z, toUnit = Mathf.Sqrt(ux * ux + uz * uz);
        if (f.punch == 0)
        {
            f.shownX = x;
            f.shownZ = z;
            if (f.punched)
            {
                // Gone well away: ready for the next time it comes. Until then it does not punch again,
                // however it turns on its way out.
                if (toUnit > 45f) { f.punched = false; f.nearest = float.MaxValue; }
            }
            else
            {
                // Every runner that comes near punches: within 16 m whatever way it is heading, or when it has
                // passed its nearest point a little further off
                if (toUnit < f.nearest) f.nearest = toUnit;
                if (toUnit < 16f || (f.nearest < 32f && toUnit > f.nearest + 1.5f) || f.finishing) f.punch = 1;
            }
            if (toUnit > 45f && !f.punched) f.nearest = float.MaxValue;
        }
        if (f.punch == 1)
        {
            // To the punching unit, stopping an arm's length from it
            float sx = f.shownX - world.unitX, sz = f.shownZ - world.unitZ, s = Mathf.Sqrt(sx * sx + sz * sz);
            if (s < 0.01f) { sx = 1f; sz = 0f; s = 1f; }
            Vector2 stand = new Vector2(world.unitX + sx / s * 0.55f, world.unitZ + sz / s * 0.55f);
            // A little quicker than the runner really moves, and more so once the runner is on its way out again
            bool leaving = toUnit > f.nearest + 1f;
            if (toUnit < f.nearest) f.nearest = toUnit;
            // A runner who has finished in the race runs the last metres on its own, at running pace
            float step = f.finishing ? Mathf.Max(moved, Time.unscaledDeltaTime * 5f) : moved * (leaving ? 1.9f : 1.2f);
            Vector2 at = Vector2.MoveTowards(new Vector2(f.shownX, f.shownZ), stand, step);
            f.shownX = at.x;
            f.shownZ = at.y;
            if ((at - stand).sqrMagnitude < 0.01f)
            {
                f.punch = 2;
                f.punched = true;
                f.everPunched = true;
                f.punchedAt = Time.unscaledTime;
            }
            else if (toUnit > 60f) { f.punch = 2; f.punched = true; }   // the runner is far away by now: give up
        }
        else if (f.punch == 2)
        {
            // A touch of the unit, then back onto the route
            if (f.finishing) { WalkOff(f); return; }   // a finisher gets its breath back and walks away
            if (Time.unscaledTime - f.punchedAt < 0.25f) return;
            Vector2 at = Vector2.MoveTowards(new Vector2(f.shownX, f.shownZ), new Vector2(x, z), moved * 1.6f + 0.002f);
            f.shownX = at.x;
            f.shownZ = at.y;
            if ((at - new Vector2(x, z)).sqrMagnitude < 0.02f) f.punch = 0;
        }
    }

    // After the finish punch: bent over getting the breath back, then a slow walk sideways and away from the camera,
    // out of the picture, and gone after a few seconds
    const float BreathFrom = 0.3f, WalkFrom = 1.8f, GoneAt = 6f;
    void WalkOff(TvStickFigure f)
    {
        float since = Time.unscaledTime - f.punchedAt, dt = Time.unscaledDeltaTime;
        if (since < WalkFrom)
        {
            f.tired = Mathf.MoveTowards(f.tired, since > BreathFrom ? 1f : 0f, dt * 2.5f);
            return;
        }
        if (f.leaveX == 0f && f.leaveZ == 0f)
        {
            // Across the camera's view, on the side the runner was going anyway, and a little away from the camera
            float cx = world.unitX - world.camX, cz = world.unitZ - world.camZ, c = Mathf.Sqrt(cx * cx + cz * cz);
            if (c < 0.01f) { cx = 1f; cz = 0f; c = 1f; }
            cx /= c; cz /= c;
            float px = -cz, pz = cx;
            if (px * f.headX + pz * f.headZ < 0f) { px = -px; pz = -pz; }
            float lx = px * 0.8f + cx * 0.6f, lz = pz * 0.8f + cz * 0.6f, l = Mathf.Sqrt(lx * lx + lz * lz);
            f.leaveX = lx / l;
            f.leaveZ = lz / l;
        }
        f.tired = Mathf.MoveTowards(f.tired, 0f, dt * 1.5f);
        f.walk = Mathf.MoveTowards(f.walk, 1f, dt * 1.5f);
        float speed = 1.3f * f.walk;
        f.shownX += f.leaveX * speed * dt;
        f.shownZ += f.leaveZ * speed * dt;
    }

    // Gone off after the finish: not shown, and the camera does not follow it
    static bool WalkedOff(TvStickFigure f) => f.finishing && f.punch == 2 && f.everPunched && Time.unscaledTime - f.punchedAt > GoneAt;
    static bool Leaving(TvStickFigure f) => f.finishing && f.punch == 2 && f.everPunched && Time.unscaledTime - f.punchedAt > WalkFrom;

    // ---- The camera ----

    // The TV camera stands still and turns after the runner nearest the control, zooming in on runners far away
    public void TvShot(Vector3? subject, float dt)
    {
        Vector3 flag = new Vector3(world.flagX, world.HeightAt(world.flagX, world.flagZ) + 0.8f, world.flagZ);
        Vector3 position = new Vector3(world.camX, world.HeightAt(world.camX, world.camZ) + 1.7f, world.camZ);
        // The camera is always on a runner: it starts on the runner, and when the runner is gone it stays where it
        // was looking and does not swing back to the empty control
        Vector3 target = hadSubject || subject.HasValue ? lookAt : flag;
        float fov = hadSubject ? tvFov : 38f;
        bool cut = subject.HasValue && !hadSubject;
        hadSubject |= subject.HasValue;
        if (subject.HasValue)
        {
            float d = Vector3.Distance(position, subject.Value);
            // The runner in the middle of the picture, a little towards the control when it is near the camera
            target = Vector3.Lerp(flag, subject.Value, d < 8f ? 0.8f : 1f);
            // The whole runner in the picture, with some room round it
            fov = Mathf.Clamp(Mathf.Atan2(3.4f, d) * 2f * Mathf.Rad2Deg, 13f, 58f);
            // A runner coming straight at the camera: the camera steps aside, so nobody runs through it
            Vector3 away = position - subject.Value;
            away.y = 0f;
            float gap = away.magnitude;
            const float Room = 4.5f;
            Vector3 aside = Vector3.zero;
            if (gap < Room)
            {
                Vector3 side = Vector3.Cross(Vector3.up, flag - position).normalized;
                if (Vector3.Dot(side, away) < 0f) side = -side;
                aside = (gap > 0.05f ? Vector3.Lerp(away / gap, side, 0.6f).normalized : side) * (Room - gap);
            }
            camShift = Vector3.Lerp(camShift, aside, 1f - Mathf.Exp(-dt * 6f));
        }
        else camShift = Vector3.Lerp(camShift, Vector3.zero, 1f - Mathf.Exp(-dt * 2f));
        position += camShift;
        position.y = world.HeightAt(position.x, position.z) + 1.7f;
        // Follows quickly, so the runner is never out of the picture, but not so stiffly that it looks unnatural.
        // A new subject far from the old one is cut to and not panned to.
        float k = 1f - Mathf.Exp(-dt * 14f);
        if (cut || (target - lookAt).sqrMagnitude > 20f * 20f) k = 1f;
        lookAt = Vector3.Lerp(lookAt, target, k);
        tvFov = cut ? fov : Mathf.Lerp(tvFov, fov, 1f - Mathf.Exp(-dt * 5f));
        cam.transform.localPosition = position;
        cam.transform.localRotation = Quaternion.LookRotation(lookAt - position);
        cam.fieldOfView = tvFov;
    }

    // The free camera: drag turns it round the point it looks at, the wheel goes nearer, WASD and right-drag move
    // move: how far to move the point, as a share of the distance to it (x right, y forward)
    public void FreeCamera(Vector2 turn, Vector2 move, float zoom)
    {
        yaw += turn.x * 0.25f;
        pitch = Mathf.Clamp(pitch - turn.y * 0.25f, -5f, 80f);
        distance = Mathf.Clamp(distance * (1f - zoom * 0.12f), 2f, 160f);
        Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = heading * Vector3.forward, right = heading * Vector3.right;
        orbitTarget += (forward * move.y + right * move.x) * Mathf.Max(2f, distance);
        float limit = world.radius - 5f;
        orbitTarget.x = Mathf.Clamp(orbitTarget.x, -limit, limit);
        orbitTarget.z = Mathf.Clamp(orbitTarget.z, -limit, limit);
        orbitTarget.y = world.HeightAt(orbitTarget.x, orbitTarget.z) + 0.8f;
        Vector3 position = orbitTarget + Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -distance);
        // Never under the ground
        float ground = world.HeightAt(position.x, position.z) + 0.4f;
        if (position.y < ground) position.y = ground;
        cam.transform.localPosition = position;
        cam.transform.localRotation = Quaternion.LookRotation(orbitTarget - position);
        cam.fieldOfView = 50f;
    }

    // The free camera starts where the TV camera is
    public void StartFreeCamera()
    {
        Vector3 position = cam.transform.localPosition;
        orbitTarget = lookAt;
        Vector3 back = position - orbitTarget;
        distance = Mathf.Max(2f, back.magnitude);
        yaw = Mathf.Atan2(-back.x, -back.z) * Mathf.Rad2Deg;
        pitch = Mathf.Asin(Mathf.Clamp(back.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
    }
}
