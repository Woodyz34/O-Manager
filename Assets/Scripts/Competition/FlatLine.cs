using System.Collections.Generic;
using UnityEngine;

// A route line drawn as its own flat mesh, the same width all the way. Unity's LineRenderer works out the line's
// sideways direction at each point from the points round it, and where two points lie very close or the line turns
// back on itself (common at the controls, where the route goes from the control's exact place to its grid cell's
// centre) that direction flips, so the whole next piece turned into a thin wedge. Here every piece is a rectangle
// of the full width, corners are filled with a round join on the outside and meet in a single point on the inside
// (so a see-through line has no darker overlaps), and both ends get round caps.
public class FlatLine : MonoBehaviour
{
    Mesh mesh;

    public static GameObject Make(string name, List<Vector3> points, Color color, float width, int order, Material material, float z = -1.3f)
    {
        GameObject go = new GameObject(name);
        FlatLine line = go.AddComponent<FlatLine>();
        MeshFilter filter = go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        line.mesh = Build(points, color, width * 0.5f, z);
        line.mesh.name = name;
        filter.sharedMesh = line.mesh;
        return go;
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    const float ArcStep = Mathf.PI / 10f;

    static Mesh Build(List<Vector3> input, Color color, float h, float z)
    {
        List<Vector2> p = Clean(input, h * 2f);
        List<Vector3> v = new List<Vector3>();
        List<int> t = new List<int>();

        if (p.Count == 1)
        {
            // Only one point: a round dot
            Arc(v, t, p[0], p[0], Vector2.right * h, Mathf.PI * 2f, z);
        }
        else if (p.Count >= 2)
        {
            int n = p.Count;
            Vector2[] d = new Vector2[n - 1];
            float[] len = new float[n - 1];
            for (int k = 0; k < n - 1; k++)
            {
                Vector2 piece = p[k + 1] - p[k];
                len[k] = piece.magnitude;
                d[k] = piece / len[k];
            }
            // Where each piece starts (outL/outR) and ends (inL/inR), on its left and right side
            Vector2[] outL = new Vector2[n], outR = new Vector2[n], inL = new Vector2[n], inR = new Vector2[n];

            // Start cap: a half circle behind the first point
            Vector2 n0 = Left(d[0]);
            outL[0] = p[0] + n0 * h;
            outR[0] = p[0] - n0 * h;
            Arc(v, t, p[0], p[0], -n0 * h, -Mathf.PI, z);

            for (int i = 1; i < n - 1; i++)
            {
                Vector2 d1 = d[i - 1], d2 = d[i];
                Vector2 n1 = Left(d1), n2 = Left(d2);
                float cross = d1.x * d2.y - d1.y * d2.x;
                float dot = Vector2.Dot(d1, d2);
                if (Mathf.Abs(cross) < 1e-4f && dot > 0f)
                {
                    // Straight on
                    inL[i] = outL[i] = p[i] + n1 * h;
                    inR[i] = outR[i] = p[i] - n1 * h;
                    continue;
                }
                // How far along each piece the inside corner point lies
                float along = dot > -0.98f ? h * Mathf.Abs(cross) / (1f + dot) : float.MaxValue;
                bool left = cross > 0f;     // turns left: the inside of the corner is on the left
                if (along <= 0.5f * Mathf.Min(len[i - 1], len[i]))
                {
                    Vector2 m = n1 + n2;
                    Vector2 inner = p[i] + (left ? 1f : -1f) * m * (2f * h / m.sqrMagnitude);
                    if (left)
                    {
                        inL[i] = outL[i] = inner;
                        inR[i] = p[i] - n1 * h;
                        outR[i] = p[i] - n2 * h;
                    }
                    else
                    {
                        inR[i] = outR[i] = inner;
                        inL[i] = p[i] + n1 * h;
                        outL[i] = p[i] + n2 * h;
                    }
                    Vector2 from = left ? -n1 : n1, to = left ? -n2 : n2;
                    Arc(v, t, inner, p[i], from * h, Vector2.SignedAngle(from, to) * Mathf.Deg2Rad, z);
                }
                else
                {
                    // Very sharp turn on short pieces: the pieces overlap a little on the inside, the outside is round
                    inL[i] = p[i] + n1 * h;
                    inR[i] = p[i] - n1 * h;
                    outL[i] = p[i] + n2 * h;
                    outR[i] = p[i] - n2 * h;
                    Vector2 from = left ? -n1 : n1, to = left ? -n2 : n2;
                    Arc(v, t, p[i], p[i], from * h, Vector2.SignedAngle(from, to) * Mathf.Deg2Rad, z);
                }
            }

            // End cap: a half circle past the last point
            Vector2 nl = Left(d[n - 2]);
            inL[n - 1] = p[n - 1] + nl * h;
            inR[n - 1] = p[n - 1] - nl * h;
            Arc(v, t, p[n - 1], p[n - 1], nl * h, -Mathf.PI, z);

            // The pieces themselves
            for (int k = 0; k < n - 1; k++)
            {
                int b = v.Count;
                v.Add(new Vector3(outL[k].x, outL[k].y, z));
                v.Add(new Vector3(outR[k].x, outR[k].y, z));
                v.Add(new Vector3(inL[k + 1].x, inL[k + 1].y, z));
                v.Add(new Vector3(inR[k + 1].x, inR[k + 1].y, z));
                t.Add(b); t.Add(b + 2); t.Add(b + 1);
                t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
            }
        }

        Mesh mesh = new Mesh();
        if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(v);
        Color[] colors = new Color[v.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = color;
        mesh.colors = colors;
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);

    // A fan of triangles from 'hub' to an arc round 'centre' that starts at centre + start and turns 'angle' radians
    static void Arc(List<Vector3> v, List<int> t, Vector2 hub, Vector2 centre, Vector2 start, float angle, float z)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(angle) / ArcStep));
        int h = v.Count;
        v.Add(new Vector3(hub.x, hub.y, z));
        for (int s = 0; s <= steps; s++)
        {
            float a = angle * s / steps;
            float c = Mathf.Cos(a), sn = Mathf.Sin(a);
            Vector2 r = new Vector2(start.x * c - start.y * sn, start.x * sn + start.y * c);
            v.Add(new Vector3(centre.x + r.x, centre.y + r.y, z));
            if (s > 0)
            {
                t.Add(h); t.Add(h + s); t.Add(h + s + 1);
            }
        }
    }

    // Points closer than a part of the line width to the one before are dropped, and so are short pieces that turn
    // back sharply (the tiny step from the control's exact place to its cell centre), so the line runs smoothly
    static List<Vector2> Clean(List<Vector3> points, float width)
    {
        List<Vector2> kept = new List<Vector2>();
        if (points == null || points.Count == 0) return kept;
        float near = Mathf.Max(width * 0.5f, 0.0001f);
        kept.Add(points[0]);
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (Vector2.Distance(points[i], kept[kept.Count - 1]) >= near) kept.Add(points[i]);
        }
        if (points.Count > 1)
        {
            Vector2 end = points[points.Count - 1];
            if (kept.Count > 1 && Vector2.Distance(kept[kept.Count - 1], end) < near) kept[kept.Count - 1] = end;
            else if (Vector2.Distance(kept[kept.Count - 1], end) > 1e-5f) kept.Add(end);
        }

        float shortPiece = width * 3f;
        bool changed = true;
        while (changed && kept.Count > 2)
        {
            changed = false;
            for (int i = 1; i < kept.Count - 1; i++)
            {
                Vector2 into = kept[i] - kept[i - 1];
                Vector2 outOf = kept[i + 1] - kept[i];
                if (Mathf.Min(into.magnitude, outOf.magnitude) < shortPiece && Vector2.Angle(into, outOf) > 100f)
                {
                    kept.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
        // Taking a point out of a spike can leave two points on the same spot
        for (int i = kept.Count - 1; i > 0; i--)
        {
            if (Vector2.Distance(kept[i], kept[i - 1]) < 1e-5f) kept.RemoveAt(i);
        }
        return kept;
    }
}
