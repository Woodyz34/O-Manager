using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The undergrowth of the run-yourself world as copies of a few ready-made plants (2026-11-26, Anders chose it: Fossum
// on Ultra still stopped now and then for the memory clean-up, and he wants room for more undergrowth some day).
//
// Before, every 32 m square of undergrowth was one big mesh made on a background thread: a million corners for a whole
// piece, tens of MB of arrays that became garbage once handed to the graphics card. Now each plant shape (bilberry,
// heather, grass, fern ...) is made once, in a few shades, and a square only says where each plant stands (TvWorld.
// tuftRecords, seven numbers a plant); the graphics card draws the copies (instancing), like the trees (RunTrees).
// The thinning with distance (TvAtlasLit's _TvThin) works on the copies too: each plant's own number comes from where
// it stands (the uv.w of these meshes is -1, "work it out in the shader"), and the plants fade out on their share of
// the pixels towards the end of the undergrowth's reach (_TvLod), so a square coming or going never pops.
public class RunTufts
{
    public const int Shades = 6;
    const float TintLow = 0.42f, TintHigh = 1.22f;

    readonly Mesh[] meshes;               // shape s, shade k at s * Shades + k
    readonly List<Matrix4x4>[] lists;
    readonly Material source;
    Material material;
    readonly int shapeCount;

    // What one square's plants are: for each copy mesh, where its plants stand (null: none of that one)
    public class Set
    {
        public Matrix4x4[][] byMesh;
        public int count;
        public RunTufts from;             // made for these copies (a set from before a change of graphics level is skipped)
    }

    RunTufts(int quality, Material atlas)
    {
        source = atlas;
        TvShape[] shapes = TvWorldBuilder.TuftShapes(quality);
        shapeCount = shapes.Length;
        meshes = new Mesh[shapes.Length * Shades];
        lists = new List<Matrix4x4>[meshes.Length];
        for (int s = 0; s < shapes.Length; s++)
        {
            for (int k = 0; k < Shades; k++)
            {
                var m = new TvMesh { name = "Plant " + s + "." + k, ranks = new List<float>() };
                m.Add(shapes[s], 0f, 0f, 0f, 1f, 1f, 1f, 0f, Tint(k));
                for (int i = 0; i < m.ranks.Count; i++) m.ranks[i] = -1f;   // the shader works out each copy's own number
                Mesh mesh = TvWorldView.ToMesh(m);
                mesh.name = m.name;
                mesh.UploadMeshData(true);
                meshes[s * Shades + k] = mesh;
                lists[s * Shades + k] = new List<Matrix4x4>();
            }
        }
    }

    static float Tint(int shade) => TintLow + (TintHigh - TintLow) * (shade + 0.5f) / Shades;

    // Null when this computer can't draw copies (the squares then make undergrowth meshes as before)
    public static RunTufts Create(int quality, Material atlas)
    {
        if (!SystemInfo.supportsInstancing || atlas == null) return null;
        try { return new RunTufts(quality, atlas); }
        catch (Exception e)
        {
            Debug.LogWarning("RunTufts: the undergrowth is built as meshes (" + e.Message + ")");
            return null;
        }
    }

    public void Destroy()
    {
        foreach (Mesh m in meshes) if (m != null) UnityEngine.Object.Destroy(m);
        if (material != null) UnityEngine.Object.Destroy(material);
    }

    // ---- A square's plants (background thread) ----

    // offset: where the square's own (0, 0, 0) is in the run world. The records' list goes back to the pool.
    public Set Prepare(List<float> records, Vector3 offset)
    {
        if (records == null) return null;
        int size = TvWorld.TuftRecordSize, count = records.Count / size;
        var counts = new int[meshes.Length];
        var which = new int[count];
        for (int t = 0; t < count; t++)
        {
            int o = t * size;
            int shape = (int)records[o];
            if (shape < 0 || shape >= shapeCount) { which[t] = -1; continue; }
            // The nearest shade, a little of where it stands mixed in so neighbours of the same tint differ
            float x = records[o + 1] + offset.x, z = records[o + 3] + offset.z;
            int hash = (int)((uint)(Mathf.FloorToInt(x * 5.3f) * 73856093 ^ Mathf.FloorToInt(z * 4.1f) * 19349663) % 1000u);
            float f = (records[o + 6] - TintLow) / (TintHigh - TintLow) * Shades - 0.5f + (hash / 1000f - 0.5f) * 0.8f;
            int k = Mathf.Clamp(Mathf.RoundToInt(f), 0, Shades - 1);
            which[t] = shape * Shades + k;
            counts[which[t]]++;
        }
        var set = new Set { byMesh = new Matrix4x4[meshes.Length][], count = count, from = this };
        var fill = new int[meshes.Length];
        for (int i = 0; i < meshes.Length; i++) if (counts[i] > 0) set.byMesh[i] = new Matrix4x4[counts[i]];
        for (int t = 0; t < count; t++)
        {
            int i = which[t];
            if (i < 0) continue;
            int o = t * size;
            float x = records[o + 1], y = records[o + 2], z = records[o + 3], s = records[o + 4], yaw = records[o + 5];
            float c = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            var m = new Matrix4x4();
            // scaled, turned and moved, as TvMesh.Add does it
            m.m00 = s * c; m.m02 = s * sn; m.m11 = s; m.m20 = -s * sn; m.m22 = s * c;
            m.m03 = offset.x + x; m.m13 = offset.y + y; m.m23 = offset.z + z;
            m.m33 = 1f;
            set.byMesh[i][fill[i]++] = m;
        }
        TvMesh.GiveFloats(records);
        return set;
    }

    // ---- Every frame ----

    public void Clear()
    {
        foreach (List<Matrix4x4> l in lists) l.Clear();
    }

    int drawn;
    public int Drawn => drawn;

    // A square's plants (toWorld: when the run world is moved from the scene's origin)
    public void Add(Set set, Matrix4x4 toWorld, bool moved)
    {
        if (set == null || set.from != this) return;
        for (int i = 0; i < set.byMesh.Length; i++)
        {
            Matrix4x4[] where = set.byMesh[i];
            if (where == null) continue;
            if (!moved) lists[i].AddRange(where);
            else foreach (Matrix4x4 m in where) lists[i].Add(toWorld * m);
        }
    }

    static readonly int LodId = Shader.PropertyToID("_TvLod");

    // reach: where the undergrowth ends (it fades out over band metres before that)
    public void Draw(Bounds around, float reach, float band)
    {
        if (material == null) material = new Material(source) { name = "Undergrowth copies" };
        material.CopyPropertiesFromMaterial(source);
        material.enableInstancing = true;
        material.SetVector(LodId, new Vector4(0f, 1f, reach, band));
        var rp = new RenderParams(material)
        {
            worldBounds = around,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = true,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
        };
        const int Batch = 1023;
        drawn = 0;
        for (int i = 0; i < meshes.Length; i++)
        {
            List<Matrix4x4> l = lists[i];
            if (l.Count == 0 || meshes[i] == null) continue;
            drawn += l.Count;
            for (int start = 0; start < l.Count; start += Batch)
                Graphics.RenderMeshInstanced(rp, meshes[i], 0, l, Math.Min(Batch, l.Count - start), start);
        }
    }
}
