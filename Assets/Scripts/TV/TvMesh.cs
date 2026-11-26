using System;
using System.Collections.Generic;

// Triangles with a colour per corner, as plain lists: what TvWorldBuilder makes and TvWorldView turns into Unity
// meshes. Plain C# without Unity, so the world can be built (and tested) outside the game.
// Axes as in Unity: x east, y up, z north, in metres from the TV control. A triangle faces the side from which its
// corners run clockwise; Tri() turns it the right way round from a hint of where "outside" is.
public class TvMesh
{
    public string name;
    public bool castShadows = true;
    // The run-yourself world (RunYourself/RunWorld.cs): drawn always, only near the runner, or only far from them
    public const byte Always = 0, Near = 1, Far = 2;
    public byte group = Always;
    public readonly List<float> positions;   // x, y, z
    public readonly List<float> normals;
    public readonly List<byte> colors;       // r, g, b (sRGB)
    public readonly List<int> triangles;
    // Textured meshes only (the bark and the leaves of the trees, the undergrowth, the forest floor's litter;
    // TvAtlas): u, v and which picture of the atlas, for each corner. Empty for the plain coloured meshes.
    public readonly List<float> uvs;

    public TvMesh() : this(false) { }

    // pooled: the lists come from a pool of lists already grown big (the run-yourself world's pieces, 2026-11-26,
    // Anders on Fossum Ultra: the garbage of growing lists of a million corners stopped the game now and then); give
    // them back with Recycle once the mesh is made from them
    public TvMesh(bool pooled)
    {
        positions = pooled ? Pool<float>.Take() : new List<float>();
        normals = pooled ? Pool<float>.Take() : new List<float>();
        colors = pooled ? Pool<byte>.Take() : new List<byte>();
        triangles = pooled ? Pool<int>.Take() : new List<int>();
        uvs = pooled ? Pool<float>.Take() : new List<float>();
    }

    // The lists back to the pool; the mesh is empty and must not be used after this
    public void Recycle()
    {
        Pool<float>.Give(positions);
        Pool<float>.Give(normals);
        Pool<byte>.Give(colors);
        Pool<int>.Give(triangles);
        Pool<float>.Give(uvs);
        if (ranks != null) Pool<float>.Give(ranks);
        ranks = null;
    }

    public static List<float> PooledFloats() => Pool<float>.Take();

    static class Pool<T>
    {
        static readonly System.Collections.Concurrent.ConcurrentBag<List<T>> bag = new System.Collections.Concurrent.ConcurrentBag<List<T>>();
        const int MaxKept = 48;

        public static List<T> Take() => bag.TryTake(out List<T> list) ? list : new List<T>();

        public static void Give(List<T> list)
        {
            // Only lists worth keeping: big ones, and not too many of them
            if (list == null || list.Capacity < 4096 || bag.Count >= MaxKept) return;
            list.Clear();
            bag.Add(list);
        }
    }
    // The run-yourself undergrowth (2026-11-26, Anders: Ultra lagged): each shape's own number 0..1 (the same for all
    // its corners, 0 for a corner added on its own), so further from the runner only the shapes below a share are
    // drawn (TvAtlasLit thins them by it). null: not kept.
    public List<float> ranks;

    public int VertexCount => positions.Count / 3;

    public bool Textured => uvs.Count > 0;

    // Fills in "no picture" (tile -1) for the corners before `count` that have no u, v yet: a mesh that holds both
    // textured and plain shapes gives every corner a u, v, tile
    public void PadUvs(int count)
    {
        while (uvs.Count < count * 3) { uvs.Add(0f); uvs.Add(0f); uvs.Add(-1f); }
    }

    public int Vertex(float x, float y, float z, float nx, float ny, float nz, TvColor c)
    {
        positions.Add(x); positions.Add(y); positions.Add(z);
        normals.Add(nx); normals.Add(ny); normals.Add(nz);
        colors.Add(c.r); colors.Add(c.g); colors.Add(c.b);
        if (ranks != null) ranks.Add(0f);
        return positions.Count / 3 - 1;
    }

    // A copy of a shape, scaled (sx, sy, sz), turned round the y axis, tilted a little and moved to (x, y, z).
    // tint multiplies the colours (1 = as they are).
    // lifeX, lifeZ: where the tree's own life (TreeLife) is drawn from, when not where it stands (the run-yourself
    // world's ready-made tree variants, RunTrees)
    public void Add(TvShape shape, float x, float y, float z, float sx, float sy, float sz, float yaw, float tint = 1f, float tiltX = 0f, float tiltZ = 0f,
                    float lifeX = float.NaN, float lifeZ = float.NaN)
    {
        int first = VertexCount;
        float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw);
        float[] p = shape.positions, n = shape.normals;
        byte[] c = shape.colors;
        float[] su = shape.uvs;
        bool tilt = tiltX != 0f || tiltZ != 0f;
        // Every tree its own (2026-11-25, Anders: "no tree like another"): its bark a little warmer or colder,
        // greyer or greener, its picture moved round and up, and moss and lichen of its own on it (TreeLife)
        TreeLife life = su != null ? new TreeLife(float.IsNaN(lifeX) ? x : lifeX, float.IsNaN(lifeZ) ? z : lifeZ) : null;
        for (int i = 0; i < p.Length; i += 3)
        {
            float px = p[i] * sx, py = p[i + 1] * sy, pz = p[i + 2] * sz;
            if (tilt) { px += py * tiltX; pz += py * tiltZ; }
            positions.Add(x + px * cos + pz * sin);
            positions.Add(y + py);
            positions.Add(z - px * sin + pz * cos);
            // Normals: uneven scaling would need the inverse, but the shapes are scaled almost evenly
            float nx = n[i], ny = n[i + 1], nz = n[i + 2];
            normals.Add(nx * cos + nz * sin);
            normals.Add(ny);
            normals.Add(-nx * sin + nz * cos);
            if (life != null && TreeLife.IsBark(su[i + 2]))
            {
                colors.Add(Tint(c[i], tint * life.r)); colors.Add(Tint(c[i + 1], tint * life.g)); colors.Add(Tint(c[i + 2], tint * life.b));
            }
            else if (life != null && TreeLife.IsStone(su[i + 2]))
            {
                colors.Add(Tint(c[i], tint * life.sr)); colors.Add(Tint(c[i + 1], tint * life.sg)); colors.Add(Tint(c[i + 2], tint * life.sb));
            }
            else
            {
                colors.Add(Tint(c[i], tint)); colors.Add(Tint(c[i + 1], tint)); colors.Add(Tint(c[i + 2], tint));
            }
        }
        if (ranks != null)
        {
            // Where the shape stands decides its number, so it is the same on every computer
            double h = Math.Sin(x * 12.9898 + z * 78.233) * 43758.5453;
            float rank = 0.002f + 0.998f * (float)(h - Math.Floor(h));
            for (int i = 0; i < p.Length; i += 3) ranks.Add(rank);
        }
        int[] t = shape.triangles;
        for (int i = 0; i < t.Length; i++) triangles.Add(first + t[i]);
        if (su != null)
        {
            PadUvs(first);
            for (int i = 0; i < su.Length; i += 3)
            {
                float tile = su[i + 2];
                if (TreeLife.IsBark(tile))
                {
                    // u goes round a whole number of times, so any shift round is seamless
                    uvs.Add(su[i] + life.du); uvs.Add(su[i + 1] + life.dv); uvs.Add(life.Code(tile, p[i], p[i + 1], p[i + 2]));
                }
                else if (TreeLife.IsStone(tile))
                {
                    uvs.Add(su[i] + life.du); uvs.Add(su[i + 1] + life.dv); uvs.Add(life.StoneCode(tile));
                }
                else { uvs.Add(su[i]); uvs.Add(su[i + 1]); uvs.Add(tile); }
            }
        }
        else if (uvs.Count > 0) PadUvs(VertexCount);
    }

    // Cuts meshes into square tiles (tiles x tiles over the square of 2 x radius round the control), each triangle to
    // the tile its middle is in. The TV camera stands still and sees only a narrow slice of the world, and the
    // graphics card skips every tile outside the picture (and its shadow), so less is drawn. The picture is the same:
    // every triangle is kept as it was, only in another mesh.
    public static List<TvMesh> SplitIntoTiles(List<TvMesh> sources, string name, bool castShadows, float radius, int tiles)
    {
        var result = new TvMesh[tiles * tiles];
        float size = 2f * radius / tiles;
        foreach (TvMesh source in sources)
        {
            // Corners added after the last textured one get "no picture" too
            if (source.uvs.Count > 0) source.PadUvs(source.VertexCount);
            List<float> p = source.positions, n = source.normals;
            List<byte> c = source.colors;
            List<int> t = source.triangles;
            // Which tile each triangle goes to, then each tile's triangles with their own corners
            var byTile = new List<int>[tiles * tiles];
            var remap = new int[source.VertexCount];   // the corner's number in the tile being made, + 1 (0 = not yet)
            var stamp = new int[source.VertexCount];   // which tile that number belongs to, + 1
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                int a = t[i] * 3, b = t[i + 1] * 3, d = t[i + 2] * 3;
                float x = (p[a] + p[b] + p[d]) / 3f, z = (p[a + 2] + p[b + 2] + p[d + 2]) / 3f;
                int ti = (int)((x + radius) / size), tj = (int)((z + radius) / size);
                ti = ti < 0 ? 0 : ti >= tiles ? tiles - 1 : ti;
                tj = tj < 0 ? 0 : tj >= tiles ? tiles - 1 : tj;
                int k = tj * tiles + ti;
                (byTile[k] ?? (byTile[k] = new List<int>())).Add(i);
            }
            for (int k = 0; k < byTile.Length; k++)
            {
                if (byTile[k] == null) continue;
                TvMesh tile = result[k] ?? (result[k] = new TvMesh { name = name + " " + k, castShadows = castShadows });
                foreach (int i in byTile[k])
                {
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int v = t[i + corner];
                        int nv = remap[v] - 1;
                        if (stamp[v] != k + 1)
                        {
                            stamp[v] = k + 1;
                            nv = tile.VertexCount;
                            tile.positions.Add(p[v * 3]); tile.positions.Add(p[v * 3 + 1]); tile.positions.Add(p[v * 3 + 2]);
                            tile.normals.Add(n[v * 3]); tile.normals.Add(n[v * 3 + 1]); tile.normals.Add(n[v * 3 + 2]);
                            tile.colors.Add(c[v * 3]); tile.colors.Add(c[v * 3 + 1]); tile.colors.Add(c[v * 3 + 2]);
                            if (source.uvs.Count > 0)
                            {
                                tile.PadUvs(nv);
                                tile.uvs.Add(source.uvs[v * 3]); tile.uvs.Add(source.uvs[v * 3 + 1]); tile.uvs.Add(source.uvs[v * 3 + 2]);
                            }
                            else if (tile.uvs.Count > 0) tile.PadUvs(nv + 1);
                            remap[v] = nv + 1;
                        }
                        tile.triangles.Add(nv);
                    }
                }
            }
        }
        var list = new List<TvMesh>();
        foreach (TvMesh m in result)
            if (m != null && m.VertexCount > 0)
            {
                if (m.uvs.Count > 0) m.PadUvs(m.VertexCount);
                list.Add(m);
            }
        return list;
    }

    static byte Tint(byte v, float tint)
    {
        float f = v * tint;
        return (byte)(f < 0f ? 0f : f > 255f ? 255f : f);
    }
}

public struct TvColor
{
    public byte r, g, b;

    public TvColor(byte r, byte g, byte b) { this.r = r; this.g = g; this.b = b; }

    // "#55683a"
    public static TvColor Hex(string hex)
    {
        int v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new TvColor((byte)(v >> 16), (byte)((v >> 8) & 255), (byte)(v & 255));
    }

    public TvColor Lerp(TvColor to, float t)
    {
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        return new TvColor((byte)(r + (to.r - r) * t), (byte)(g + (to.g - g) * t), (byte)(b + (to.b - b) * t));
    }

    public TvColor Shade(float f)
    {
        return new TvColor(Clamp(r * f), Clamp(g * f), Clamp(b * f));
    }

    static byte Clamp(float v) => (byte)(v < 0f ? 0f : v > 255f ? 255f : v);
}

// A small low-poly shape (a tree, a rock, a tuft of blueberry) made once and copied many times into a TvMesh
public class TvShape
{
    public float[] positions, normals;
    public byte[] colors;
    public int[] triangles;
    public float[] uvs;          // u, v, atlas tile for each corner (TvAtlas), or null for a plain coloured shape
}

// Puts a shape together from blobs, cylinders and cones
public partial class TvShapeBuilder
{
    readonly List<float> p = new List<float>(), n = new List<float>();
    readonly List<byte> c = new List<byte>();
    readonly List<int> t = new List<int>();
    readonly TvRandom random;

    public TvShapeBuilder(TvRandom random) { this.random = random; }

    // The u, v and atlas tile of every corner, only once a textured part (TvFlora.cs) has been added
    readonly List<float> uv = new List<float>();

    public TvShape Done()
    {
        float[] uvs = null;
        if (uv.Count > 0)
        {
            while (uv.Count < p.Count) { uv.Add(0f); uv.Add(0f); uv.Add(-1f); }
            uvs = uv.ToArray();
        }
        return new TvShape { positions = p.ToArray(), normals = n.ToArray(), colors = c.ToArray(), triangles = t.ToArray(), uvs = uvs };
    }

    int V(float x, float y, float z, float nx, float ny, float nz, TvColor col)
    {
        p.Add(x); p.Add(y); p.Add(z);
        n.Add(nx); n.Add(ny); n.Add(nz);
        c.Add(col.r); c.Add(col.g); c.Add(col.b);
        if (uv.Count > 0) { uv.Add(0f); uv.Add(0f); uv.Add(-1f); }
        return p.Count / 3 - 1;
    }

    // A flat triangle that faces away from (cx, cy, cz)
    void FlatTri(float ax, float ay, float az, float bx, float by, float bz, float qx, float qy, float qz,
                 float cx, float cy, float cz, TvColor ca, TvColor cb, TvColor cc)
    {
        float ux = bx - ax, uy = by - ay, uz = bz - az, vx = qx - ax, vy = qy - ay, vz = qz - az;
        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        float ox = (ax + bx + qx) / 3f - cx, oy = (ay + by + qy) / 3f - cy, oz = (az + bz + qz) / 3f - cz;
        bool flip = nx * ox + ny * oy + nz * oz < 0f;
        if (flip) { nx = -nx; ny = -ny; nz = -nz; }
        float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len < 1e-9f) return;
        nx /= len; ny /= len; nz /= len;
        int a = V(ax, ay, az, nx, ny, nz, ca);
        int b = flip ? V(qx, qy, qz, nx, ny, nz, cc) : V(bx, by, bz, nx, ny, nz, cb);
        int q = flip ? V(bx, by, bz, nx, ny, nz, cb) : V(qx, qy, qz, nx, ny, nz, cc);
        t.Add(a); t.Add(b); t.Add(q);
    }

    // ---- A lumpy ball: a tree crown, a rock, a tuft ----

    static readonly float[] IcoV;
    static readonly int[] IcoF =
    {
        0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
        3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
    };
    static float[] ico1V;   // one subdivision: 42 corners, 80 faces
    static int[] ico1F;

    static TvShapeBuilder()
    {
        float g = (1f + (float)Math.Sqrt(5.0)) / 2f;
        float[] raw = { -1, g, 0, 1, g, 0, -1, -g, 0, 1, -g, 0, 0, -1, g, 0, 1, g, 0, -1, -g, 0, 1, -g, g, 0, -1, g, 0, 1, -g, 0, -1, -g, 0, 1 };
        IcoV = new float[raw.Length];
        for (int i = 0; i < raw.Length; i += 3)
        {
            float l = (float)Math.Sqrt(raw[i] * raw[i] + raw[i + 1] * raw[i + 1] + raw[i + 2] * raw[i + 2]);
            IcoV[i] = raw[i] / l; IcoV[i + 1] = raw[i + 1] / l; IcoV[i + 2] = raw[i + 2] / l;
        }
        var verts = new List<float>(IcoV);
        var faces = new List<int>();
        var mid = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (mid.TryGetValue(key, out int m)) return m;
            float x = verts[a * 3] + verts[b * 3], y = verts[a * 3 + 1] + verts[b * 3 + 1], z = verts[a * 3 + 2] + verts[b * 3 + 2];
            float l = (float)Math.Sqrt(x * x + y * y + z * z);
            verts.Add(x / l); verts.Add(y / l); verts.Add(z / l);
            m = verts.Count / 3 - 1;
            mid[key] = m;
            return m;
        }
        for (int f = 0; f < IcoF.Length; f += 3)
        {
            int a = IcoF[f], b = IcoF[f + 1], q = IcoF[f + 2];
            int ab = Mid(a, b), bq = Mid(b, q), qa = Mid(q, a);
            faces.AddRange(new[] { a, ab, qa, b, bq, ab, q, qa, bq, ab, bq, qa });
        }
        ico1V = verts.ToArray();
        ico1F = faces.ToArray();
    }

    // radius r, stretched (sx, sy, sz), centre (ox, oy, oz); jitter moves every corner in or out by up to that share.
    // detail 0 = 20 faces, 1 = 80. flat = hard facets (trees, rocks); otherwise round (small tufts, far fewer corners).
    // top: the colour blends to it from y0 to y1 (in the shape's own height).
    public void Blob(float r, float sx, float sy, float sz, float ox, float oy, float oz, float jitter, int detail, bool flat,
                     TvColor color, TvColor? top = null, float y0 = 0f, float y1 = 1f)
    {
        if (Lean && r * Math.Max(sx, Math.Max(sy, sz)) < 0.2f) return;
        float[] sv = detail > 0 ? ico1V : IcoV;
        int[] sf = detail > 0 ? ico1F : IcoF;
        int count = sv.Length / 3;
        var v = new float[sv.Length];
        for (int i = 0; i < count; i++)
        {
            float s = r * (1f + (random.Value() - 0.5f) * jitter);
            v[i * 3] = sv[i * 3] * s * sx + ox;
            v[i * 3 + 1] = sv[i * 3 + 1] * s * sy + oy;
            v[i * 3 + 2] = sv[i * 3 + 2] * s * sz + oz;
        }
        TvColor At(float y) => top.HasValue ? color.Lerp(top.Value, Smooth(y0, y1, y)) : color;
        if (flat)
        {
            for (int f = 0; f < sf.Length; f += 3)
            {
                int a = sf[f] * 3, b = sf[f + 1] * 3, q = sf[f + 2] * 3;
                FlatTri(v[a], v[a + 1], v[a + 2], v[b], v[b + 1], v[b + 2], v[q], v[q + 1], v[q + 2], ox, oy, oz,
                    At(v[a + 1]), At(v[b + 1]), At(v[q + 1]));
            }
            return;
        }
        int first = p.Count / 3;
        for (int i = 0; i < count; i++)
        {
            V(v[i * 3], v[i * 3 + 1], v[i * 3 + 2], sv[i * 3], sv[i * 3 + 1], sv[i * 3 + 2], At(v[i * 3 + 1]));
        }
        for (int f = 0; f < sf.Length; f += 3)
        {
            // Turned so the face looks away from the middle of the ball
            int a = sf[f] * 3, b = sf[f + 1] * 3, q = sf[f + 2] * 3;
            float ux = sv[b] - sv[a], uy = sv[b + 1] - sv[a + 1], uz = sv[b + 2] - sv[a + 2];
            float vx = sv[q] - sv[a], vy = sv[q + 1] - sv[a + 1], vz = sv[q + 2] - sv[a + 2];
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            bool outward = nx * sv[a] + ny * sv[a + 1] + nz * sv[a + 2] > 0f;
            t.Add(first + sf[f]); t.Add(first + sf[outward ? f + 1 : f + 2]); t.Add(first + sf[outward ? f + 2 : f + 1]);
        }
    }

    // ---- A rock: a lumpy ball broken by flat planes, every face coloured on its own ----
    // Stretched (sx, sy, sz) round (0, oy, 0); cuts flat breaks; moss covers that share of the faces that look up;
    // lichen: the share of faces with pale spots. Looks like split granite rather than a smooth pebble.
    // textured (2026-11-25): the faces get the granite picture of the forest atlas (TvAtlas.Granite), the mossy ones
    // the moss picture; the colours are then light tints over the pictures.
    public void Rock(float sx, float sy, float sz, float oy, int detail, int cuts, float jitter, TvColor stone, TvColor moss,
                     float mossShare, float lichen, float bottom = -0.35f, bool textured = false)
    {
        float[] sv = detail > 0 ? ico1V : IcoV;
        int[] sf = detail > 0 ? ico1F : IcoF;
        int count = sv.Length / 3;
        var v = new float[sv.Length];
        for (int i = 0; i < count; i++)
        {
            float s = 1f + (random.Value() - 0.5f) * jitter;
            v[i * 3] = sv[i * 3] * s * sx;
            v[i * 3 + 1] = sv[i * 3 + 1] * s * sy;
            v[i * 3 + 2] = sv[i * 3 + 2] * s * sz;
        }
        for (int k = 0; k < cuts; k++)
        {
            float nx = random.Value() * 2f - 1f, ny = random.Value() * 1.3f - 0.3f, nz = random.Value() * 2f - 1f;
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (l < 0.1f) continue;
            nx /= l; ny /= l; nz /= l;
            float reach = (float)Math.Sqrt(nx * nx * sx * sx + ny * ny * sy * sy + nz * nz * sz * sz) * (0.45f + random.Value() * 0.3f);
            for (int i = 0; i < count; i++)
            {
                float d = v[i * 3] * nx + v[i * 3 + 1] * ny + v[i * 3 + 2] * nz - reach;
                if (d <= 0f) continue;
                v[i * 3] -= nx * d; v[i * 3 + 1] -= ny * d; v[i * 3 + 2] -= nz * d;
            }
        }
        // It sits in the ground: the underside is flat
        float floor = bottom * sy;
        for (int i = 0; i < count; i++) if (v[i * 3 + 1] < floor) v[i * 3 + 1] = floor;
        for (int i = 0; i < count; i++) v[i * 3 + 1] += oy;

        TvColor pale = TvColor.Hex("#c4c6ba"), yellow = TvColor.Hex("#b3ad78"), dark = stone.Shade(0.72f);
        float seed = random.Value() * 50f;
        for (int f = 0; f < sf.Length; f += 3)
        {
            int a = sf[f] * 3, b = sf[f + 1] * 3, q = sf[f + 2] * 3;
            float ux = v[b] - v[a], uy = v[b + 1] - v[a + 1], uz = v[b + 2] - v[a + 2];
            float wx = v[q] - v[a], wy = v[q + 1] - v[a + 1], wz = v[q + 2] - v[a + 2];
            float nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            float cx = (v[a] + v[b] + v[q]) / 3f, cy = (v[a + 1] + v[b + 1] + v[q + 1]) / 3f - oy, cz = (v[a + 2] + v[b + 2] + v[q + 2]) / 3f;
            if (nx * cx + ny * cy + nz * cz < 0f) { nx = -nx; ny = -ny; nz = -nz; }
            float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-9f) continue;
            nx /= nl; ny /= nl; nz /= nl;
            // Faces on one break share the normal, so they share the colour and read as one flat face
            float grain = TvRandom.Noise(nx * 1.6f + seed, nz * 1.6f + ny * 1.1f) * 0.7f + TvRandom.Noise(cx * 2.3f, cz * 2.3f + cy) * 0.3f;
            TvColor col = stone.Shade(0.86f + 0.28f * grain);
            // Damp and shaded low down
            col = col.Lerp(dark, Smooth(0.15f, -0.3f, cy / Math.Max(0.01f, sy)) * 0.7f);
            float up = Smooth(0.45f, 0.9f, ny) * Smooth(0.42f, 0.6f, TvRandom.Noise(cx * 1.8f - seed, cz * 1.8f + 3f) + mossShare - 0.5f);
            if (textured)
            {
                // The picture brings the grain, cracks and lichen; the colour only shades it (damp foot, faces). The
                // moss is painted by the shader where the stone faces up, in patches (2026-11-25: no more whole
                // triangles of moss); how much rides in the tile's fraction (StoneLife, TvMesh.Add)
                float shade = 0.82f + 0.3f * grain - 0.25f * Smooth(0.15f, -0.3f, cy / Math.Max(0.01f, sy));
                TvColor tint = new TvColor(255, 252, 246).Shade(shade);
                FlatTriTex(v[a], v[a + 1], v[a + 2], v[b], v[b + 1], v[b + 2], v[q], v[q + 1], v[q + 2], 0f, oy, 0f, tint,
                           TvAtlas.Granite + 0.02f + 0.47f * Math.Min(0.999f, mossShare * 1.6f), 0.9f, seed);
                continue;
            }
            if (up > 0.02f) col = col.Lerp(moss.Shade(0.9f + 0.2f * grain), Math.Min(1f, up * 1.15f));
            else if (random.Value() < lichen) col = col.Lerp(random.Value() < 0.6f ? pale : yellow, 0.55f);
            FlatTri(v[a], v[a + 1], v[a + 2], v[b], v[b + 1], v[b + 2], v[q], v[q + 1], v[q + 2], 0f, oy, 0f, col, col, col);
        }
    }

    // An upright tube from y to y + h, radius r0 at the bottom and r1 at the top (a trunk, a pole), round shading
    public void Tube(float r0, float r1, float h, float y, int sides, TvColor bottom, TvColor? top = null, float y0 = 0f, float y1 = 1f,
                     float ox = 0f, float oz = 0f)
    {
        int first = p.Count / 3;
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * 6.2831853f, cx = (float)Math.Cos(a), cz = (float)Math.Sin(a);
            V(ox + cx * r0, y, oz + cz * r0, cx, 0f, cz, top.HasValue ? bottom.Lerp(top.Value, Smooth(y0, y1, y)) : bottom);
            V(ox + cx * r1, y + h, oz + cz * r1, cx, 0f, cz, top.HasValue ? bottom.Lerp(top.Value, Smooth(y0, y1, y + h)) : bottom);
        }
        for (int i = 0; i < sides; i++)
        {
            int a = first + i * 2, b = a + 1, q = a + 2, d = a + 3;
            t.Add(a); t.Add(b); t.Add(q);
            t.Add(q); t.Add(b); t.Add(d);
        }
    }

    // A round limb from a to b, radius r0 at a and r1 at b, round shading, open ends (a ball at each joint closes
    // them): arms and legs of the town's people
    public void Limb(float ax, float ay, float az, float bx, float by, float bz, float r0, float r1, int sides, TvColor c0, TvColor? c1 = null)
    {
        float dx = bx - ax, dy = by - ay, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-5f) return;
        dx /= len; dy /= len; dz /= len;
        float ux, uy, uz;
        if (Math.Abs(dy) < 0.9f) { ux = dz; uy = 0f; uz = -dx; }
        else { ux = 1f; uy = 0f; uz = 0f; }
        float ul = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz);
        ux /= ul; uy /= ul; uz /= ul;
        float vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
        TvColor top = c1 ?? c0;
        int first = p.Count / 3;
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * 6.2831853f, ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            float nx = ux * ca + vx * sa, ny = uy * ca + vy * sa, nz = uz * ca + vz * sa;
            V(ax + nx * r0, ay + ny * r0, az + nz * r0, nx, ny, nz, c0);
            V(bx + nx * r1, by + ny * r1, bz + nz * r1, nx, ny, nz, top);
        }
        float wx = uy * vz - uz * vy, wy = uz * vx - ux * vz, wz = ux * vy - uy * vx;
        bool flip = wx * dx + wy * dy + wz * dz > 0f;
        for (int i = 0; i < sides; i++)
        {
            int a = first + i * 2, b = a + 1, q = a + 2, d = a + 3;
            if (flip) { t.Add(a); t.Add(q); t.Add(b); t.Add(q); t.Add(d); t.Add(b); }
            else { t.Add(a); t.Add(b); t.Add(q); t.Add(q); t.Add(b); t.Add(d); }
        }
    }

    // A cone standing on y (a layer of spruce branches, a tuft of grass), flat facets
    public void Cone(float r, float h, float y, int sides, TvColor color, float ox = 0f, float oz = 0f)
    {
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * 6.2831853f, a1 = (i + 1) / (float)sides * 6.2831853f;
            FlatTri(ox + (float)Math.Cos(a0) * r, y, oz + (float)Math.Sin(a0) * r,
                    ox + (float)Math.Cos(a1) * r, y, oz + (float)Math.Sin(a1) * r,
                    ox, y + h, oz, ox, y + h * 0.3f, oz, color, color, color);
        }
    }

    // A box from (x0, y0, z0) to (x1, y1, z1)
    // Lean: the houses of a run-yourself town a little away from the runner. Small bits (glazing bars, flower heads,
    // letters) are left out and boxes get no bottom. Set on the thread that builds them, round the build.
    [ThreadStatic] public static bool Lean;

    public void Box(float x0, float y0, float z0, float x1, float y1, float z1, TvColor color)
    {
        if (Lean && Math.Abs(x1 - x0) < 0.28f && Math.Abs(y1 - y0) < 0.28f && Math.Abs(z1 - z0) < 0.28f) return;
        float cx = (x0 + x1) / 2f, cy = (y0 + y1) / 2f, cz = (z0 + z1) / 2f;
        // Each side is 4 corners shared by its two triangles
        void Quad(float ax, float ay, float az, float bx, float by, float bz, float qx, float qy, float qz, float dx, float dy, float dz)
        {
            float ux = bx - ax, uy = by - ay, uz = bz - az, vx = qx - ax, vy = qy - ay, vz = qz - az;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float ox = (ax + qx) / 2f - cx, oy = (ay + qy) / 2f - cy, oz = (az + qz) / 2f - cz;
            bool flip = nx * ox + ny * oy + nz * oz < 0f;
            if (flip) { nx = -nx; ny = -ny; nz = -nz; }
            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-9f) return;
            nx /= len; ny /= len; nz /= len;
            // Each side a shade of its own (a few per cent), so a house or a box is not one flat colour
            float mx = (ax + qx) * 0.5f, my = (ay + qy) * 0.5f, mz = (az + qz) * 0.5f;
            TvColor col = color.Shade(0.95f + 0.1f * TvRandom.Hash((int)(mx * 7.31f + mz * 3.17f), (int)(my * 5.71f + nx * 11f + nz * 13f)));
            int a = V(ax, ay, az, nx, ny, nz, col), b = V(bx, by, bz, nx, ny, nz, col);
            int q = V(qx, qy, qz, nx, ny, nz, col), d = V(dx, dy, dz, nx, ny, nz, col);
            if (flip) { t.Add(a); t.Add(q); t.Add(b); t.Add(a); t.Add(d); t.Add(q); }
            else { t.Add(a); t.Add(b); t.Add(q); t.Add(a); t.Add(q); t.Add(d); }
        }
        Quad(x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0);
        Quad(x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1);
        Quad(x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0);
        Quad(x1, y0, z0, x1, y0, z1, x1, y1, z1, x1, y1, z0);
        Quad(x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1);
        if (!Lean) Quad(x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1);
    }

    // One flat triangle with its own colours, facing away from (cx, cy, cz)
    public void Triangle(float ax, float ay, float az, float bx, float by, float bz, float qx, float qy, float qz,
                         float cx, float cy, float cz, TvColor color)
    {
        FlatTri(ax, ay, az, bx, by, bz, qx, qy, qz, cx, cy, cz, color, color, color);
    }

    public static float Smooth(float a, float b, float x)
    {
        x = (x - a) / (b - a);
        x = x < 0f ? 0f : x > 1f ? 1f : x;
        return x * x * (3f - 2f * x);
    }
}

// The same numbers every time for the same seed, so a TV control looks the same each time it is built
public class TvRandom
{
    uint state;

    public TvRandom(int seed) { state = (uint)seed * 2654435761u + 0x9E3779B9u; if (state == 0) state = 1; }

    public float Value()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / 16777216f;
    }

    public float Range(float a, float b) => a + (b - a) * Value();

    // Starts the numbers again from a place (whole metres) and a purpose, so the same place always gets the same
    // numbers (the run-yourself world, built piece by piece)
    public void Reseed(int i, int j, int salt)
    {
        uint h = (uint)i * 374761393u + (uint)j * 668265263u + (uint)salt * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        state = h == 0 ? 1u : h;
    }

    // Smooth noise over the plane, 0..1, the same for the same place
    public static float Noise(float x, float y)
    {
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y);
        float fx = x - i, fy = y - j, u = fx * fx * (3f - 2f * fx), v = fy * fy * (3f - 2f * fy);
        float a = Hash(i, j), b = Hash(i + 1, j), c = Hash(i, j + 1), d = Hash(i + 1, j + 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    // Three layers of noise: patches with smaller patches in them
    public static float Patches(float x, float y)
    {
        return Noise(x, y) * 0.55f + Noise(x * 2.1f, y * 2.1f) * 0.3f + Noise(x * 4.3f, y * 4.3f) * 0.15f;
    }

    public static float Hash(int i, int j)
    {
        uint h = (uint)i * 374761393u + (uint)j * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }
}

// What makes one tree its own (from where it stands, so a tree is the same in every build): the shade of its bark,
// where its bark picture starts, how much moss grows up its foot and on which side (the damp side), and how much crust
// lichen it carries. The moss and lichen are drawn by the shader (TvAtlasLit) in patches from the tree's own place in
// the world; the amounts ride in the spare fraction of the tile number (the tile is the nearest whole number):
// fraction = 0.02 + 0.47 * (lichen / 4 + moss * 0.24), lichen 0..3, moss 0..1. A plain whole number = none.
public class TreeLife
{
    public float r = 1f, g = 1f, b = 1f, du, dv;
    readonly float mossTop, mossLevel, wetX, wetZ;
    readonly int lichen;

    public static bool IsBark(float tile) => (tile > -0.5f && tile < 4.5f) || (tile > 34.5f && tile < 35.5f);
    public static bool IsStone(float tile) => tile > 19.5f && tile < 20.5f;

    // A stone's own (2026-11-25, Anders: every stone personal): grey, warm pink-red, dark, pale or brownish granite,
    // and its own share of moss (some bare, some green all over their top)
    public float sr = 1f, sg = 1f, sb = 1f;
    readonly float stoneMoss;

    public TreeLife(float x, float z)
    {
        uint h = (uint)((int)Math.Floor(x * 7.31f) * 73856093) ^ (uint)((int)Math.Floor(z * 5.17f) * 19349663);
        float Next() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return (h & 0xffffff) / 16777216f; }
        Next(); Next();
        float lum = 0.86f + 0.24f * Next(), warm = -0.05f + 0.13f * Next(), green = 0.03f * Next() * Next();
        r = lum * (1f + warm); g = lum * (1f + green); b = lum * (1f - warm - green * 0.5f);
        du = Next(); dv = Next() * 3f;
        // Most trees a little moss at the foot, some a lot and high up, a few none
        float m = Next();
        mossLevel = m < 0.12f ? 0f : 0.35f + 0.65f * Next();
        mossTop = 0.25f + 2.2f * m * m;
        float a = Next() * 6.2832f;
        wetX = (float)Math.Sin(a); wetZ = (float)Math.Cos(a);
        lichen = (int)(Next() * 3.999f);
        float k = Next(), sl = 0.88f + 0.24f * Next();
        if (k < 0.35f) { sr = sl; sg = sl; sb = sl * 1.02f; }                          // grey
        else if (k < 0.55f) { sr = sl * 1.12f; sg = sl * 0.97f; sb = sl * 0.9f; }      // pink-red granite
        else if (k < 0.7f) { sr = sl * 0.74f; sg = sl * 0.75f; sb = sl * 0.78f; }      // dark
        else if (k < 0.85f) { sr = sl * 1.14f; sg = sl * 1.12f; sb = sl * 1.06f; }     // pale
        else { sr = sl * 1.04f; sg = sl * 0.96f; sb = sl * 0.84f; }                    // brownish
        float m2 = Next();
        stoneMoss = m2 < 0.2f ? 0f : 0.3f + 1.4f * m2;
    }

    // A stone's tile with its moss share in the fraction (the shape's share times the stone's own)
    public float StoneCode(float tile)
    {
        float t = (float)Math.Round(tile), shapeMoss = Math.Max(0f, (tile - t - 0.02f) / 0.47f);
        float moss = Math.Max(0f, Math.Min(0.999f, shapeMoss * stoneMoss));
        return moss < 0.01f ? t : t + 0.02f + 0.47f * moss;
    }

    // The tile number with this corner's moss and the tree's lichen in its fraction (shape coordinates, y up from the foot)
    public float Code(float tile, float x, float y, float z)
    {
        float t = (float)Math.Round(tile);
        float species = t == TvAtlas.SpruceBark ? 1f : t == TvAtlas.BirchBark ? 0.45f : t == TvAtlas.PineBarkHigh ? 0.3f : 0.7f;
        float rr = (float)Math.Sqrt(x * x + z * z);
        float side = rr > 1e-4f ? (x * wetX + z * wetZ) / rr : 0f;
        float k = Math.Max(0f, Math.Min(1f, 1f - (y + 0.2f) / mossTop));
        float moss = mossLevel * species * k * (0.45f + 0.55f * Math.Max(0f, side)) + mossLevel * species * 0.25f * Math.Max(0f, 1f - y / 0.6f);
        // A birch: the "moss" is its black, cracked foot instead, up to 0.3-1.8 m (the shader paints it)
        if (t == TvAtlas.BirchBark) moss = Math.Max(0f, Math.Min(1f, 1.2f - (y + 0.1f) / (0.35f + mossTop * 0.6f)));
        moss = Math.Max(0f, Math.Min(0.999f, moss));
        int li = t == TvAtlas.BirchBark ? Math.Min(lichen, 1) : lichen;
        return t + 0.02f + 0.47f * (li / 4f + moss * 0.24f);
    }
}
