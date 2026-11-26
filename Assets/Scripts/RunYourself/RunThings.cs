using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A sprint town's street things as copies of ready-made shapes (2026-11-26, Anders: the sprint lagged now and then,
// "make more copies, it works so well in the forest").
//
// Before, every 64 m piece of a town had its own "Town things" mesh: every lamp, bench, bin, parked car, bicycle, café
// table and street tree written out corner by corner, up to 48 000 corners a piece, tens of MB of arrays that became
// garbage once handed to the graphics card. Now each thing's shape is made once (when first seen), and a piece only
// says where each thing stands (TvWorld.thingRecords, eight numbers a thing); the graphics card draws the copies
// (instancing), like the trees (RunTrees) and the undergrowth (RunTufts). The things fade out on their share of the
// pixels towards the end of their reach (_TvLod), so nothing pops.
public class RunThings
{
    readonly Material source;
    Material material;
    readonly bool shadows;
    readonly List<Mesh> meshes = new List<Mesh>();              // by the shape's number (TvWorldBuilder.CopyShape)
    readonly List<List<Matrix4x4>> lists = new List<List<Matrix4x4>>();

    // What one piece's things are: for each shape number in it, where its copies stand
    public class Set
    {
        public int[] shapes;
        public Matrix4x4[][] where;
        public int count;
        public RunThings from;            // made for these copies (a set from before a change of graphics level is skipped)
    }

    RunThings(Material atlas, bool shadows)
    {
        source = atlas;
        this.shadows = shadows;
    }

    // Null when this computer can't draw copies (the pieces then make the "Town things" meshes as before)
    public static RunThings Create(Material atlas, bool shadows)
    {
        if (!SystemInfo.supportsInstancing || atlas == null) return null;
        return new RunThings(atlas, shadows);
    }

    public void Destroy()
    {
        foreach (Mesh m in meshes) if (m != null) UnityEngine.Object.Destroy(m);
        meshes.Clear();
        if (material != null) UnityEngine.Object.Destroy(material);
    }

    // ---- A piece's things (background thread) ----

    // offset: where the piece's own (0, 0, 0) is in the run world. The records' list goes back to the pool.
    public Set Prepare(List<float> records, Vector3 offset)
    {
        if (records == null) return null;
        int size = TvWorld.ThingRecordSize, count = records.Count / size;
        var counts = new Dictionary<int, int>();
        for (int t = 0; t < count; t++)
        {
            int shape = (int)records[t * size];
            counts.TryGetValue(shape, out int c);
            counts[shape] = c + 1;
        }
        var set = new Set { shapes = new int[counts.Count], where = new Matrix4x4[counts.Count][], count = count, from = this };
        var slot = new Dictionary<int, int>();
        var fill = new int[counts.Count];
        int k = 0;
        foreach (var pair in counts)
        {
            set.shapes[k] = pair.Key;
            set.where[k] = new Matrix4x4[pair.Value];
            slot[pair.Key] = k++;
        }
        for (int t = 0; t < count; t++)
        {
            int o = t * size;
            int i = slot[(int)records[o]];
            float x = records[o + 1], y = records[o + 2], z = records[o + 3];
            float sx = records[o + 4], sy = records[o + 5], sz = records[o + 6], yaw = records[o + 7];
            float c = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            var m = new Matrix4x4();
            // scaled, turned and moved, as TvMesh.Add does it
            m.m00 = sx * c; m.m02 = sz * sn; m.m11 = sy; m.m20 = -sx * sn; m.m22 = sz * c;
            m.m03 = offset.x + x; m.m13 = offset.y + y; m.m23 = offset.z + z;
            m.m33 = 1f;
            set.where[i][fill[i]++] = m;
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

    // A piece's things (toWorld: when the run world is moved from the scene's origin)
    public void Add(Set set, Matrix4x4 toWorld, bool moved)
    {
        if (set == null || set.from != this) return;
        for (int k = 0; k < set.shapes.Length; k++)
        {
            int i = set.shapes[k];
            while (lists.Count <= i) { lists.Add(new List<Matrix4x4>()); meshes.Add(null); }
            List<Matrix4x4> l = lists[i];
            if (!moved) l.AddRange(set.where[k]);
            else foreach (Matrix4x4 m in set.where[k]) l.Add(toWorld * m);
        }
    }

    // The shape's mesh, made the first time it is drawn. A shape without a picture gets "no picture" (tile -1) on
    // every corner, so the atlas material draws it in its own colours.
    Mesh MeshOf(int i)
    {
        if (meshes[i] != null) return meshes[i];
        TvShape shape = TvWorldBuilder.CopyShape(i);
        if (shape == null) return null;
        var m = new TvMesh { name = "Thing " + i };
        m.Add(shape, 0f, 0f, 0f, 1f, 1f, 1f, 0f);
        m.PadUvs(m.VertexCount);
        Mesh mesh = TvWorldView.ToMesh(m);
        mesh.name = m.name;
        mesh.UploadMeshData(true);
        meshes[i] = mesh;
        return mesh;
    }

    static readonly int LodId = Shader.PropertyToID("_TvLod");

    // reach: where the things end (they fade out over band metres before that)
    public void Draw(Bounds around, float reach, float band)
    {
        if (material == null) material = new Material(source) { name = "Town things copies" };
        material.CopyPropertiesFromMaterial(source);
        material.enableInstancing = true;
        material.SetVector(LodId, new Vector4(0f, 1f, reach, band));
        var rp = new RenderParams(material)
        {
            worldBounds = around,
            shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows = shadows,
            lightProbeUsage = LightProbeUsage.Off,
            reflectionProbeUsage = ReflectionProbeUsage.Off,
            motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
        };
        const int Batch = 1023;
        drawn = 0;
        for (int i = 0; i < lists.Count; i++)
        {
            List<Matrix4x4> l = lists[i];
            if (l.Count == 0) continue;
            Mesh mesh = MeshOf(i);
            if (mesh == null) continue;
            drawn += l.Count;
            for (int start = 0; start < l.Count; start += Batch)
                Graphics.RenderMeshInstanced(rp, mesh, 0, l, Math.Min(Batch, l.Count - start), start);
        }
    }
}
