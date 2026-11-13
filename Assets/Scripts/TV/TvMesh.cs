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
    public readonly List<float> positions = new List<float>();   // x, y, z
    public readonly List<float> normals = new List<float>();
    public readonly List<byte> colors = new List<byte>();        // r, g, b (sRGB)
    public readonly List<int> triangles = new List<int>();

    public int VertexCount => positions.Count / 3;

    public int Vertex(float x, float y, float z, float nx, float ny, float nz, TvColor c)
    {
        positions.Add(x); positions.Add(y); positions.Add(z);
        normals.Add(nx); normals.Add(ny); normals.Add(nz);
        colors.Add(c.r); colors.Add(c.g); colors.Add(c.b);
        return positions.Count / 3 - 1;
    }

    // A copy of a shape, scaled (sx, sy, sz), turned round the y axis, tilted a little and moved to (x, y, z).
    // tint multiplies the colours (1 = as they are).
    public void Add(TvShape shape, float x, float y, float z, float sx, float sy, float sz, float yaw, float tint = 1f, float tiltX = 0f, float tiltZ = 0f)
    {
        int first = VertexCount;
        float cos = (float)Math.Cos(yaw), sin = (float)Math.Sin(yaw);
        float[] p = shape.positions, n = shape.normals;
        byte[] c = shape.colors;
        bool tilt = tiltX != 0f || tiltZ != 0f;
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
            colors.Add(Tint(c[i], tint)); colors.Add(Tint(c[i + 1], tint)); colors.Add(Tint(c[i + 2], tint));
        }
        int[] t = shape.triangles;
        for (int i = 0; i < t.Length; i++) triangles.Add(first + t[i]);
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
}

// Puts a shape together from blobs, cylinders and cones
public class TvShapeBuilder
{
    readonly List<float> p = new List<float>(), n = new List<float>();
    readonly List<byte> c = new List<byte>();
    readonly List<int> t = new List<int>();
    readonly TvRandom random;

    public TvShapeBuilder(TvRandom random) { this.random = random; }

    public TvShape Done()
    {
        return new TvShape { positions = p.ToArray(), normals = n.ToArray(), colors = c.ToArray(), triangles = t.ToArray() };
    }

    int V(float x, float y, float z, float nx, float ny, float nz, TvColor col)
    {
        p.Add(x); p.Add(y); p.Add(z);
        n.Add(nx); n.Add(ny); n.Add(nz);
        c.Add(col.r); c.Add(col.g); c.Add(col.b);
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
    public void Box(float x0, float y0, float z0, float x1, float y1, float z1, TvColor color)
    {
        float cx = (x0 + x1) / 2f, cy = (y0 + y1) / 2f, cz = (z0 + z1) / 2f;
        void Quad(float ax, float ay, float az, float bx, float by, float bz, float qx, float qy, float qz, float dx, float dy, float dz)
        {
            FlatTri(ax, ay, az, bx, by, bz, qx, qy, qz, cx, cy, cz, color, color, color);
            FlatTri(ax, ay, az, qx, qy, qz, dx, dy, dz, cx, cy, cz, color, color, color);
        }
        Quad(x0, y0, z0, x1, y0, z0, x1, y1, z0, x0, y1, z0);
        Quad(x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1);
        Quad(x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0);
        Quad(x1, y0, z0, x1, y0, z1, x1, y1, z1, x1, y1, z0);
        Quad(x0, y1, z0, x1, y1, z0, x1, y1, z1, x0, y1, z1);
        Quad(x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1);
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
