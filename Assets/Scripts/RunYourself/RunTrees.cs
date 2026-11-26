using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The trees of the run-yourself world as copies of a few ready-made ones (2026-11-25, Anders: big maps lagged).
//
// Before, every piece of the world made its own big tree meshes (about 250 000 corners a piece on High), kept them and
// handed them to the graphics card, which cost time on the main thread and a lot of memory. Now:
//  - every tree shape is made once, in four shades (each with its own moss and lichen on the bark), and a piece only
//    says where each tree stands (TvWorld.treeRecords); the graphics card draws the copies (instancing), the full
//    trees close by and the plainer far shapes further out;
//  - beyond that every tree is one upright card with a picture of its kind (an impostor), taken of the real trees
//    from four sides when the race loads (TvImpostor shader).
// Plain arrays are made on the background threads (Prepare); the main thread only draws (Draw).
//
// Levels of detail per tree (2026-11-26, Anders: "the closest must look very fine, then a little worse step by step,
// no sharp changes"), chosen by each tree's own distance every frame, so the changes come one tree at a time on a
// circle round the runner instead of a whole 64 m piece at once:
//  - closest (FullReach: 30 m on High, 40 m on Ultra): the trees of the chosen quality;
//  - then out to the near distance: the plainer Medium trees (about a third of the triangles on High);
//  - then the far shapes (about a tenth), then the pictures.
// Only trees within the shadow distance cast shadows: before, every full tree out to the near distance was drawn into
// the shadow maps again although no shadow is drawn that far out.
public class RunTrees
{
    public const int Shades = 4, Views = 4;
    const int TileW = 128, TileH = 256;
    const int BakeLayer = 29;
    static readonly float[] ShadeTint = { 0.86f, 0.95f, 1.03f, 1.1f };
    static readonly Color32 Key = new Color32(255, 0, 255, 255);

    readonly Mesh[] nearMeshes, farMeshes;
    readonly Mesh[] midMeshes;                  // the Medium quality's trees, same order; = nearMeshes on Low and Medium
    readonly List<Matrix4x4>[] nearLists, farLists, midLists, midPlainLists;
    readonly Material material;
    public Material impostorMaterial;           // null when the pictures could not be made: far shapes all the way out
    Texture2D impostorTexture;
    // For each far shape (its row of pictures): the picture's width and height and how far below the foot it starts,
    // in the shape's own metres
    float[] cardW, cardH, cardBelow;
    readonly int nearCount, farCount;
    readonly bool shadows;

    public bool HasImpostors => impostorMaterial != null;

    // What one piece's trees are: which shape copy each is, and where it stands (a matrix from the run world's space)
    public class Set
    {
        public int[] near, far;
        public Matrix4x4[] where;
        public Vector3[] cardFeet;            // the picture cards (Views): four corners each
        public Vector2[] cardCorner, cardBelow;
        public Vector4[] card;
        public Color32[] cardTint;
        public int[] cardTriangles;
        public int Count => where != null ? where.Length : 0;
    }

    RunTrees(int quality, Material atlas, bool shadows)
    {
        material = atlas;
        this.shadows = shadows;
        TvShape[] near = TvWorldBuilder.TreeShapes(quality, true), far = TvWorldBuilder.TreeShapes(quality, false);
        nearCount = near.Length;
        farCount = far.Length;
        nearMeshes = Copies(near, "Tree");
        farMeshes = Copies(far, "Tree far");
        TvShape[] mid = quality >= 2 ? TvWorldBuilder.TreeShapes(1, true) : null;
        midMeshes = mid != null && mid.Length == near.Length ? Copies(mid, "Tree mid") : nearMeshes;
        nearLists = Lists(nearMeshes.Length);
        farLists = Lists(farMeshes.Length);
        midLists = Lists(nearMeshes.Length);
        midPlainLists = Lists(nearMeshes.Length);
    }

    static List<Matrix4x4>[] Lists(int n)
    {
        var lists = new List<Matrix4x4>[n];
        for (int i = 0; i < n; i++) lists[i] = new List<Matrix4x4>();
        return lists;
    }

    // Null when this computer can't draw copies (the race then builds the tree meshes as before)
    public static RunTrees Create(int quality, Material atlas, bool shadows, Light sun)
    {
        if (!SystemInfo.supportsInstancing || atlas == null) return null;
        try
        {
            atlas.enableInstancing = true;
            var trees = new RunTrees(quality, atlas, shadows);
            try { trees.Bake(quality, sun); }
            catch (Exception e)
            {
                Debug.LogWarning("RunTrees: no pictures for the far trees (" + e.Message + "); far shapes all the way out");
                trees.impostorMaterial = null;
            }
            return trees;
        }
        catch (Exception e)
        {
            Debug.LogWarning("RunTrees: the trees are built as meshes (" + e.Message + ")");
            return null;
        }
    }

    Mesh[] Copies(TvShape[] shapes, string name)
    {
        var meshes = new Mesh[shapes.Length * Shades];
        for (int s = 0; s < shapes.Length; s++)
        {
            for (int k = 0; k < Shades; k++)
            {
                var m = new TvMesh { name = name + " " + s + "." + k };
                // Each shade its own moss and lichen and bark start (TreeLife) from a place of its own
                m.Add(shapes[s], 0f, 0f, 0f, 1f, 1f, 1f, 0f, ShadeTint[k], 0f, 0f, 37.3f * k + 5.1f + s * 3.7f, 11.9f * k + 2.3f + s * 1.3f);
                Mesh mesh = TvWorldView.ToMesh(m);
                mesh.name = m.name;
                mesh.UploadMeshData(true);
                meshes[s * Shades + k] = mesh;
            }
        }
        return meshes;
    }

    public void Destroy()
    {
        foreach (Mesh m in nearMeshes) if (m != null) UnityEngine.Object.Destroy(m);
        foreach (Mesh m in farMeshes) if (m != null) UnityEngine.Object.Destroy(m);
        if (midMeshes != nearMeshes) foreach (Mesh m in midMeshes) if (m != null) UnityEngine.Object.Destroy(m);
        if (impostorMaterial != null) UnityEngine.Object.Destroy(impostorMaterial);
        if (impostorTexture != null) UnityEngine.Object.Destroy(impostorTexture);
    }

    public void SetFog(float start, float end)
    {
        if (impostorMaterial == null) return;
        impostorMaterial.SetFloat("_FogStart", start);
        impostorMaterial.SetFloat("_FogEnd", end);
    }

    // ---- The pictures of the far trees, taken when the race loads ----

    void Bake(int quality, Light sun)
    {
        Shader shader = Resources.Load<Shader>("Shaders/TvImpostor");
        if (shader == null) shader = Shader.Find("O-Manager/TvImpostor");
        if (shader == null || !shader.isSupported) throw new Exception("no impostor shader");
        TvShape[] near = TvWorldBuilder.TreeShapes(quality, true);
        int rows = farCount;
        cardW = new float[rows];
        cardH = new float[rows];
        cardBelow = new float[rows];
        int width = Views * TileW, height = rows * TileH;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Tree pictures", antiAliasing = 1 };
        rt.Create();
        var camGo = new GameObject("Tree picture camera");
        var treeGo = new GameObject("Tree for its picture") { layer = BakeLayer };
        Camera cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.cullingMask = 1 << BakeLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Key;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.useOcclusionCulling = false;
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 300f;
        cam.targetTexture = rt;
        var filter = treeGo.AddComponent<MeshFilter>();
        var renderer = treeGo.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        Vector3 spot = new Vector3(0f, 20000f, 0f);
        treeGo.transform.position = spot;

        // No fog, no wind and no relief in the picture (the fog is added where the card stands)
        float fogStart = material.GetFloat("_FogStart"), fogEnd = material.GetFloat("_FogEnd"), wind = material.GetFloat("_Wind");
        float bumpFar = material.HasProperty("_BumpFar") ? material.GetFloat("_BumpFar") : 0f;
        material.SetFloat("_FogStart", 1e6f);
        material.SetFloat("_FogEnd", 2e6f);
        material.SetFloat("_Wind", 0f);
        if (material.HasProperty("_BumpFar")) material.SetFloat("_BumpFar", 0f);
        var made = new List<Mesh>();
        try
        {
            for (int r = 0; r < rows; r++)
            {
                TvShape shape = near[Mathf.Clamp(TvWorldBuilder.NearOf(quality, r), 0, near.Length - 1)];
                var m = new TvMesh { name = "Picture tree" };
                m.Add(shape, 0f, 0f, 0f, 1f, 1f, 1f, 0f, 1f, 0f, 0f, 21.7f + r, 8.3f + r);
                Mesh mesh = TvWorldView.ToMesh(m);
                made.Add(mesh);
                filter.sharedMesh = mesh;
                // The picture holds the whole tree: as high as the tree, half as wide (the tile is 1 x 2)
                float minY = float.MaxValue, maxY = float.MinValue, reach = 0f;
                float[] p = shape.positions;
                for (int i = 0; i + 2 < p.Length; i += 3)
                {
                    minY = Math.Min(minY, p[i + 1]);
                    maxY = Math.Max(maxY, p[i + 1]);
                    reach = Math.Max(reach, Math.Max(Math.Abs(p[i]), Math.Abs(p[i + 2])));
                }
                if (minY > maxY) { minY = 0f; maxY = 1f; }
                float half = Math.Max((maxY - minY) * 0.5f, reach * 2f) * 1.04f;
                cardH[r] = half * 2f;
                cardW[r] = half * 2f * TileW / TileH;
                cardBelow[r] = -minY;
                Vector3 middle = spot + new Vector3(0f, minY + half, 0f);
                cam.orthographicSize = half;
                for (int k = 0; k < Views; k++)
                {
                    float a = k * 2f * Mathf.PI / Views;
                    cam.transform.position = middle + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 120f;
                    cam.transform.LookAt(middle, Vector3.up);
                    cam.rect = new Rect(k / (float)Views, r / (float)rows, 1f / Views, 1f / rows);
                    cam.Render();
                }
            }

            RenderTexture before = RenderTexture.active;
            RenderTexture.active = rt;
            impostorTexture = new Texture2D(width, height, TextureFormat.RGBA32, true, false) { name = "Tree pictures", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            impostorTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            RenderTexture.active = before;
            Color32[] px = impostorTexture.GetPixels32();
            CutOut(px, width, height);
            impostorTexture.SetPixels32(px);
            impostorTexture.Apply(true, true);
        }
        finally
        {
            material.SetFloat("_FogStart", fogStart);
            material.SetFloat("_FogEnd", fogEnd);
            material.SetFloat("_Wind", wind);
            if (material.HasProperty("_BumpFar")) material.SetFloat("_BumpFar", bumpFar);
            UnityEngine.Object.Destroy(camGo);
            UnityEngine.Object.Destroy(treeGo);
            foreach (Mesh m in made) UnityEngine.Object.Destroy(m);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }
        impostorMaterial = new Material(shader) { name = "Tree pictures" };
        impostorMaterial.mainTexture = impostorTexture;
        impostorMaterial.SetFloat("_Views", Views);
        impostorMaterial.SetFloat("_Rows", rows);
        impostorMaterial.SetFloat("_Cutoff", 0.45f);
        impostorMaterial.SetFloat("_FogStart", fogStart);
        impostorMaterial.SetFloat("_FogEnd", fogEnd);
    }

    // The background colour becomes see-through; the colours next to the tree are spread into it, so the smaller
    // pictures far away (mipmaps) don't get a pink edge
    static void CutOut(Color32[] px, int w, int h)
    {
        var solid = new bool[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            Color32 c = px[i];
            solid[i] = !(c.r == Key.r && c.g == Key.g && c.b == Key.b);
            px[i].a = solid[i] ? (byte)255 : (byte)0;
        }
        var filled = (bool[])solid.Clone();
        for (int pass = 0; pass < 6; pass++)
        {
            var next = (bool[])filled.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (filled[i]) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= w) continue;
                            int j = yy * w + xx;
                            if (!filled[j]) continue;
                            r += px[j].r; g += px[j].g; b += px[j].b; n++;
                        }
                    }
                    if (n == 0) continue;
                    px[i].r = (byte)(r / n); px[i].g = (byte)(g / n); px[i].b = (byte)(b / n);
                    next[i] = true;
                }
            }
            filled = next;
        }
        for (int i = 0; i < px.Length; i++)
            if (!filled[i]) { px[i].r = 58; px[i].g = 72; px[i].b = 46; }
    }

    // ---- A piece's trees (background thread) ----

    // offset: where the piece's own (0, 0, 0) is in the run world
    public Set Prepare(List<float> records, Vector3 offset)
    {
        int size = TvWorld.RecordSize;
        int count = records.Count / size;
        var set = new Set { near = new int[count], far = new int[count], where = new Matrix4x4[count] };
        bool cards = cardH != null;
        if (cards)
        {
            set.cardFeet = new Vector3[count * 4];
            set.cardCorner = new Vector2[count * 4];
            set.cardBelow = new Vector2[count * 4];
            set.card = new Vector4[count * 4];
            set.cardTint = new Color32[count * 4];
            set.cardTriangles = new int[count * 6];
        }
        for (int t = 0; t < count; t++)
        {
            int o = t * size;
            int nearShape = (int)records[o], farShape = (int)records[o + 1];
            float x = records[o + 2], y = records[o + 3], z = records[o + 4], s = records[o + 5], sy = records[o + 6], yaw = records[o + 7];
            float tint = records[o + 8], tx = records[o + 9], tz = records[o + 10];
            // A shade near the tree's own (a little of where it stands mixed in, so neighbours differ)
            float wx = x + offset.x, wz = z + offset.z;
            int hash = (int)((uint)(Mathf.FloorToInt(wx * 3.1f) * 73856093 ^ Mathf.FloorToInt(wz * 2.7f) * 19349663) % 1000u);
            int shade = Mathf.Clamp(Mathf.RoundToInt((tint - 0.82f) / 0.30f * (Shades - 1) + (hash / 1000f - 0.5f) * 1.2f), 0, Shades - 1);
            set.near[t] = nearShape >= 0 && nearShape < nearCount ? nearShape * Shades + shade : -1;
            set.far[t] = farShape >= 0 && farShape < farCount ? farShape * Shades + shade : -1;
            float c = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            var m = new Matrix4x4();
            // the shape scaled, leaning (tilt), turned and moved, as TvMesh.Add does it
            m.m00 = s * c; m.m10 = 0f; m.m20 = -s * sn;
            m.m01 = (tx * c + tz * sn) * sy; m.m11 = sy; m.m21 = (-tx * sn + tz * c) * sy;
            m.m02 = s * sn; m.m12 = 0f; m.m22 = s * c;
            m.m03 = offset.x + x; m.m13 = offset.y + y; m.m23 = offset.z + z;
            m.m33 = 1f;
            set.where[t] = m;
            if (!cards) continue;
            int row = farShape >= 0 && farShape < farCount ? farShape : 0;
            float w = cardW[row] * s, h = cardH[row] * sy, below = cardBelow[row] * sy;
            byte tb = (byte)Mathf.Clamp(Mathf.RoundToInt(tint / 1.25f * 255f), 0, 255);
            for (int k = 0; k < 4; k++)
            {
                int v = t * 4 + k;
                set.cardFeet[v] = new Vector3(x, y, z);
                set.cardCorner[v] = new Vector2(k == 1 || k == 2 ? 1f : 0f, k >= 2 ? 1f : 0f);
                set.card[v] = new Vector4(w, h, row, yaw);
                set.cardBelow[v] = new Vector2(below, 0f);
                set.cardTint[v] = new Color32(tb, tb, tb, 255);
            }
            int q = t * 6, b = t * 4;
            set.cardTriangles[q] = b; set.cardTriangles[q + 1] = b + 2; set.cardTriangles[q + 2] = b + 1;
            set.cardTriangles[q + 3] = b; set.cardTriangles[q + 4] = b + 3; set.cardTriangles[q + 5] = b + 2;
        }
        return set;
    }

    // The picture cards of a piece as one mesh (main thread); null when there are none
    public Mesh CardMesh(Set set, float size)
    {
        if (set == null || set.cardFeet == null || set.cardFeet.Length == 0 || impostorMaterial == null) return null;
        var mesh = new Mesh { name = "Tree pictures" };
        if (set.cardFeet.Length > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(set.cardFeet);
        mesh.SetUVs(0, set.cardCorner);
        mesh.SetUVs(1, set.card);
        mesh.SetUVs(2, set.cardBelow);
        mesh.SetColors(set.cardTint);
        mesh.SetTriangles(set.cardTriangles, 0, false);
        // The corners all stand at the trees' feet; the cards reach out and up from there
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (Vector3 v in set.cardFeet) { lo = Mathf.Min(lo, v.y); hi = Mathf.Max(hi, v.y); }
        mesh.bounds = new Bounds(new Vector3(0f, (lo + hi) * 0.5f + 12f, 0f), new Vector3(size + 24f, hi - lo + 60f, size + 24f));
        mesh.UploadMeshData(true);
        return mesh;
    }

    // ---- Every frame ----

    public void Clear()
    {
        foreach (List<Matrix4x4> l in nearLists) l.Clear();
        foreach (List<Matrix4x4> l in farLists) l.Clear();
        foreach (List<Matrix4x4> l in midLists) l.Clear();
        foreach (List<Matrix4x4> l in midPlainLists) l.Clear();
    }

    // The reaches of the levels, in metres from the runner (RunWorld sets them for its quality)
    public float fullReach = 30f, nearReach = 96f, shadowReach = 60f;

    // A piece's trees, each at the level its own distance from the runner (x, z in the run world's space) gives.
    // farOnly: the whole piece is past the near distance (no need to look at each tree).
    public void Add(Set set, bool farOnly, float px, float pz, Matrix4x4 toWorld, bool moved)
    {
        if (set == null) return;
        if (farOnly)
        {
            for (int t = 0; t < set.far.Length; t++)
            {
                int i = set.far[t];
                if (i >= 0) farLists[i].Add(moved ? toWorld * set.where[t] : set.where[t]);
            }
            return;
        }
        float full2 = fullReach * fullReach, near2 = nearReach * nearReach;
        float shadow2 = shadows ? shadowReach * shadowReach : -1f;
        Matrix4x4[] where = set.where;
        for (int t = 0; t < where.Length; t++)
        {
            float dx = where[t].m03 - px, dz = where[t].m23 - pz, d2 = dx * dx + dz * dz;
            List<Matrix4x4> list;
            if (d2 < near2)
            {
                int i = set.near[t];
                if (i < 0) continue;
                list = d2 < full2 ? nearLists[i] : d2 < shadow2 ? midLists[i] : midPlainLists[i];
            }
            else
            {
                int i = set.far[t];
                if (i < 0) continue;
                list = farLists[i];
            }
            list.Add(moved ? toWorld * where[t] : where[t]);
        }
    }

    public void Draw(Bounds around)
    {
        var full = new RenderParams(material)
        {
            worldBounds = around,
            shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows = shadows,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
        };
        var plain = full;
        plain.shadowCastingMode = ShadowCastingMode.Off;
        DrawAll(full, nearMeshes, nearLists);       // the closest are always within the shadow distance
        DrawAll(full, midMeshes, midLists);
        DrawAll(plain, midMeshes, midPlainLists);
        DrawAll(plain, farMeshes, farLists);
    }

    static void DrawAll(RenderParams rp, Mesh[] meshes, List<Matrix4x4>[] lists)
    {
        const int Batch = 1023;
        for (int i = 0; i < meshes.Length; i++)
        {
            List<Matrix4x4> l = lists[i];
            if (l.Count == 0 || meshes[i] == null) continue;
            for (int start = 0; start < l.Count; start += Batch)
                Graphics.RenderMeshInstanced(rp, meshes[i], 0, l, Math.Min(Batch, l.Count - start), start);
        }
    }
}
