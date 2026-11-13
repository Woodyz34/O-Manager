using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

// The 3D world of one TV control in the scene: the meshes TvWorldBuilder made, a sun, a camera that draws into a
// texture (shown by TvViewer) and the stick figures of the runners. The world is put far away from the 2D map
// (Origin), and only one is kept at a time. The fog, the sky light and the shadows are switched on only while the TV
// camera draws, so the rest of the game looks as before.
public class TvWorldView : MonoBehaviour
{
    public static readonly Vector3 Origin = new Vector3(30000f, 0f, 0f);
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
    public bool freeCamera;
    Vector3 lookAt;
    float tvFov = 34f;
    // Free camera: turns round a point
    Vector3 orbitTarget;
    float yaw, pitch = 18f, distance = 14f;

    static Shader shader;

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
        int count = source.VertexCount;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var colors = new Color32[count];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(source.positions[i * 3], source.positions[i * 3 + 1], source.positions[i * 3 + 2]);
            normals[i] = new Vector3(source.normals[i * 3], source.normals[i * 3 + 1], source.normals[i * 3 + 2]);
            colors[i] = new Color32(source.colors[i * 3], source.colors[i * 3 + 1], source.colors[i * 3 + 2], 255);
        }
        var mesh = new Mesh { name = source.name };
        if (count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors32 = colors;
        mesh.SetTriangles(source.triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Builds the world of a control. The heavy part runs on a background thread; returns null when the map has none.
    public static async Task<TvWorldView> Create(MapDefinition map, CoursePoint control)
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
            seed = 1
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        TvWorld world = await Task.Run(() => TvWorldBuilder.Build(input));
        long built = watch.ElapsedMilliseconds;
        var go = new GameObject("TV world");
        go.transform.position = Origin;
        TvWorldView view = go.AddComponent<TvWorldView>();
        view.control = control;
        view.mapId = map.id;
        view.world = world;
        view.Make();
        Debug.Log($"TvWorldView: built the world at ({control.e:0}, {control.n:0}) on {map.id} in {built} ms (+ {watch.ElapsedMilliseconds - built} ms meshes): "
                  + $"{world.vertices} corners, {world.trees} trees, {world.rocks} stones, {world.tufts} tufts");
        return view;
    }

    void Make()
    {
        Material material = NewMaterial(Color.white);
        materials.Add(material);
        foreach (TvMesh source in world.meshes)
        {
            Mesh mesh = ToMesh(source);
            meshes.Add(mesh);
            var go = new GameObject(source.name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = source.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        // The sun in the south-east, a little low, as in the look test
        sun = new GameObject("TV sun").AddComponent<Light>();
        sun.transform.SetParent(transform, false);
        sun.type = LightType.Directional;
        sun.color = new Color32(0xff, 0xf1, 0xd6, 255);
        sun.intensity = 1.15f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.75f;
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
        cam.enabled = false;

        float y = world.HeightAt(world.camX, world.camZ) + 1.7f;
        cam.transform.localPosition = new Vector3(world.camX, y, world.camZ);
        lookAt = new Vector3(world.flagX, world.HeightAt(world.flagX, world.flagZ) + 0.8f, world.flagZ);
        cam.transform.localRotation = Quaternion.LookRotation(lookAt - cam.transform.localPosition);
        StartFreeCamera();

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
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.Default) { name = "TV", antiAliasing = 2 };
            cam.targetTexture = texture;
            cam.aspect = width / (float)height;
        }
        return texture;
    }

    public void SetDrawing(bool on)
    {
        if (cam != null && cam.enabled != on) cam.enabled = on;
    }

    bool savedFog;
    Color savedFogColor;
    FogMode savedFogMode;
    float savedFogStart, savedFogEnd, savedShadowDistance;
    SphericalHarmonicsL2 savedAmbient;
    AmbientMode savedAmbientMode;
    ShadowQuality savedShadows;
    bool drawing;

    void BeforeDraw(Camera c)
    {
        if (c != cam || drawing) return;
        drawing = true;
        savedFog = RenderSettings.fog;
        savedFogColor = RenderSettings.fogColor;
        savedFogMode = RenderSettings.fogMode;
        savedFogStart = RenderSettings.fogStartDistance;
        savedFogEnd = RenderSettings.fogEndDistance;
        savedAmbient = RenderSettings.ambientProbe;
        savedAmbientMode = RenderSettings.ambientMode;
        savedShadowDistance = QualitySettings.shadowDistance;
        savedShadows = QualitySettings.shadows;

        RenderSettings.fog = true;
        RenderSettings.fogColor = Sky;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 45f;
        RenderSettings.fogEndDistance = 230f;
        // Light from the sky above and a darker green-brown from the ground
        var probe = new SphericalHarmonicsL2();
        Color skyLight = new Color(0.81f, 0.88f, 0.93f) * 0.55f, groundLight = new Color(0.32f, 0.3f, 0.22f) * 0.55f;
        probe.AddAmbientLight((skyLight + groundLight) * 0.5f);
        probe.AddDirectionalLight(Vector3.up, (skyLight - groundLight) * 0.5f, 1f);
        RenderSettings.ambientMode = AmbientMode.Custom;
        RenderSettings.ambientProbe = probe;
        QualitySettings.shadowDistance = 90f;
        if (QualitySettings.shadows == ShadowQuality.Disable) QualitySettings.shadows = ShadowQuality.All;
        sun.enabled = true;
    }

    void AfterDraw(Camera c)
    {
        if (c != cam || !drawing) return;
        drawing = false;
        sun.enabled = false;
        RenderSettings.fog = savedFog;
        RenderSettings.fogColor = savedFogColor;
        RenderSettings.fogMode = savedFogMode;
        RenderSettings.fogStartDistance = savedFogStart;
        RenderSettings.fogEndDistance = savedFogEnd;
        RenderSettings.ambientMode = savedAmbientMode;
        RenderSettings.ambientProbe = savedAmbient;
        QualitySettings.shadowDistance = savedShadowDistance;
        QualitySettings.shadows = savedShadows;
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
    }

    readonly Dictionary<object, TvStickFigure> figures = new Dictionary<object, TvStickFigure>();
    readonly HashSet<object> seen = new HashSet<object>();
    readonly List<object> gone = new List<object>();

    // The runners to show now; the ones not in the list are taken away. Returns the one nearest the control, if any.
    public Vector3? ShowRunners(List<RunnerAt> runners)
    {
        seen.Clear();
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
            float x = r.x, z = r.z;
            world.PushOut(ref x, ref z, 0.25f);
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
            float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 2.8f, toUnit));
            figure.Pose(x, world.HeightAt(x, z), z, moved, unit, near);
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

    // ---- The camera ----

    // The TV camera stands still and turns after the runner nearest the control, zooming in on runners far away
    public void TvShot(Vector3? subject, float dt)
    {
        Vector3 flag = new Vector3(world.flagX, world.HeightAt(world.flagX, world.flagZ) + 0.8f, world.flagZ);
        Vector3 position = new Vector3(world.camX, world.HeightAt(world.camX, world.camZ) + 1.7f, world.camZ);
        Vector3 target = flag;
        float fov = 38f;
        if (subject.HasValue)
        {
            float d = Vector3.Distance(position, subject.Value);
            target = Vector3.Lerp(flag, subject.Value, d < 14f ? 0.5f : 0.85f);
            fov = Mathf.Clamp(Mathf.Atan2(5f, d) * 2f * Mathf.Rad2Deg, 14f, 42f);
        }
        float k = 1f - Mathf.Exp(-dt * 3f);
        lookAt = Vector3.Lerp(lookAt, target, k);
        tvFov = Mathf.Lerp(tvFov, fov, 1f - Mathf.Exp(-dt * 1.5f));
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
