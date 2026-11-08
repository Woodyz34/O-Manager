using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// A route-style line on a UI picture: pieces of the full width with round joins and round ends, the same width on
// the screen however far the picture is zoomed. Points are 0..1 across the parent picture.
public class UIRouteLine : MaskableGraphic
{
    List<Vector2> points;
    bool loop;
    float lineWidth;
    const int RoundSteps = 10;

    public static UIRouteLine Make(string name, RectTransform parent, Color color, float width)
    {
        RectTransform rect = CourseUI.Rect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();   // before the Graphic, or it caches a missing renderer and draws nothing
        UIRouteLine line = rect.gameObject.AddComponent<UIRouteLine>();
        CourseUI.Stretch(rect);
        line.color = color;
        line.lineWidth = width;
        line.raycastTarget = false;
        return line;
    }

    public void Set(List<Vector2> uvPoints, bool closedLoop)
    {
        points = uvPoints;
        loop = closedLoop;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points == null || points.Count == 0) return;
        Rect r = rectTransform.rect;
        float h = lineWidth / 2f;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        Vector2 At(int i) => new Vector2(r.xMin + points[i].x * r.width, r.yMin + points[i].y * r.height);

        int count = points.Count;
        int pieces = loop && count >= 3 ? count : count - 1;
        for (int i = 0; i < pieces; i++)
        {
            Vector2 a = At(i), b = At((i + 1) % count);
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) continue;
            Vector2 n = new Vector2(-d.y, d.x).normalized * h;
            int start = vh.currentVertCount;
            v.position = a - n; vh.AddVert(v);
            v.position = a + n; vh.AddVert(v);
            v.position = b + n; vh.AddVert(v);
            v.position = b - n; vh.AddVert(v);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
        // Round joins and ends: a small disc at every point (the line is solid, so the overlaps do not show)
        for (int i = 0; i < count; i++)
        {
            Vector2 c = At(i);
            int centre = vh.currentVertCount;
            v.position = c; vh.AddVert(v);
            for (int k = 0; k <= RoundSteps; k++)
            {
                float angle = k * Mathf.PI * 2f / RoundSteps;
                v.position = c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * h;
                vh.AddVert(v);
                if (k > 0) vh.AddTriangle(centre, centre + k, centre + k + 1);
            }
        }
    }
}
