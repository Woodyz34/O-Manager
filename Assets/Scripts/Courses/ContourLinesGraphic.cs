using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Lines through points, drawn as thin quads, in several separate pieces (HeightQuestionPanel)
public class ContourLinesGraphic : MaskableGraphic
{
    List<List<Vector2>> runs;
    public float thickness = 5f;

    public void SetRuns(List<List<Vector2>> list)
    {
        runs = list;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (runs == null) return;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        foreach (List<Vector2> points in runs)
            for (int i = 0; i + 1 < points.Count; i++)
            {
                if (vh.currentVertCount > 60000) return;
                Vector2 a = points[i], b = points[i + 1];
                Vector2 d = b - a;
                if (d.sqrMagnitude < 1e-6f) continue;
                Vector2 n = new Vector2(-d.y, d.x).normalized * (thickness / 2f);
                // A little longer each way, so the joints have no gaps
                Vector2 t = d.normalized * (thickness / 2f);
                int start = vh.currentVertCount;
                v.position = a - t - n; vh.AddVert(v);
                v.position = a - t + n; vh.AddVert(v);
                v.position = b + t + n; vh.AddVert(v);
                v.position = b + t - n; vh.AddVert(v);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
    }
}
