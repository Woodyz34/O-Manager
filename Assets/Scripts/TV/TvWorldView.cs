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
    public bool freeCamera;
    Vector3 lookAt;
    float tvFov = 34f;
    // Free camera: turns round a point
    Vector3 orbitTarget;
    float yaw, pitch = 18f, distance = 14f;

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
    public static async Task<TvWorldView> Create(MapDefinition map, CoursePoint control, bool start = false)
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
            start = start
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        TvWorld world = await Task.Run(() => TvWorldBuilder.Build(input));
        long built = watch.ElapsedMilliseconds;
        var go = new GameObject("TV world");
        go.transform.position = Origin + new Vector3(nextPlace * Apart, 0f, 0f);
        nextPlace = (nextPlace + 1) % Places;
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
        // The ground has a fine grain, so it does not look painted
        Material ground = NewMaterial(Color.white);
        ground.SetTexture("_DetailTex", Grain());
        ground.SetFloat("_Detail", 1f);
        materials.Add(ground);
        foreach (TvMesh source in world.meshes)
        {
            Mesh mesh = ToMesh(source);
            mesh.UploadMeshData(true);   // the graphics card has it now; no copy kept in memory
            meshes.Add(mesh);
            var go = new GameObject(source.name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = source.name == "Ground" ? ground : material;
            r.shadowCastingMode = source.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

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
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.Default) { name = "TV", antiAliasing = 4 };
            cam.targetTexture = texture;
            cam.aspect = width / (float)height;
        }
        return texture;
    }

    public void SetDrawing(bool on)
    {
        if (cam != null && cam.enabled != on) cam.enabled = on;
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
            ToThePunch(ref x, ref z);
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
            float near = world.hasUnit ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 2.8f, toUnit)) : 0f;
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

    // The drawn route may cut the corner at a control, but on TV every runner goes up to the flag and punches: the
    // last metres are bent in towards the punching unit, and out again afterwards
    void ToThePunch(ref float x, ref float z)
    {
        if (!world.hasUnit) return;
        const float Whole = 14f, Core = 4f, Stand = 0.55f;
        float dx = x - world.unitX, dz = z - world.unitZ;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d >= Whole) return;
        if (d < 0.01f) { dx = 1f; dz = 0f; d = 1f; }
        float shown = Stand + (Whole - Stand) * Mathf.Clamp01((d - Core) / (Whole - Core));
        x = world.unitX + dx / d * shown;
        z = world.unitZ + dz / d * shown;
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
            // The runner in the middle of the picture, a little towards the control when it is near the camera
            target = Vector3.Lerp(flag, subject.Value, d < 8f ? 0.8f : 1f);
            fov = Mathf.Clamp(Mathf.Atan2(4.5f, d) * 2f * Mathf.Rad2Deg, 16f, 46f);
        }
        // Follows quickly, so the runner is never out of the picture, but not so stiffly that it looks unnatural.
        // A new subject far from the old one is cut to and not panned to.
        float k = 1f - Mathf.Exp(-dt * 9f);
        if ((target - lookAt).sqrMagnitude > 20f * 20f) k = 1f;
        lookAt = Vector3.Lerp(lookAt, target, k);
        tvFov = Mathf.Lerp(tvFov, fov, 1f - Mathf.Exp(-dt * 2.5f));
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
