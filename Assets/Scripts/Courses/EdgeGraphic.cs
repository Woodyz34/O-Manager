using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// A closed line through points, drawn as thin quads
public class EdgeGraphic : MaskableGraphic
{
    List<Vector2> points;
    public float thickness = 3f;

    public void SetPoints(List<Vector2> list)
    {
        points = list;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points == null || points.Count < 2) return;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i], b = points[(i + 1) % points.Count];
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) continue;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (thickness / 2f);
            int start = vh.currentVertCount;
            v.position = a - n; vh.AddVert(v);
            v.position = a + n; vh.AddVert(v);
            v.position = b + n; vh.AddVert(v);
            v.position = b - n; vh.AddVert(v);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
