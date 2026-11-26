using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Filled four-cornered pieces on a UI picture, with corners 0..1 across the parent picture, so they grow and shrink
// with the picture when it is zoomed (unlike UIRouteLine, whose width stays the same on the screen). Used for the
// purple stripes of out-of-bounds areas in the contest replay (ContestReplay, 2026-11-26).
public class UIQuads : MaskableGraphic
{
    List<Vector2[]> quads;

    public static UIQuads Make(string name, RectTransform parent, Color color)
    {
        RectTransform rect = CourseUI.Rect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();   // before the Graphic, or it caches a missing renderer and draws nothing
        UIQuads graphic = rect.gameObject.AddComponent<UIQuads>();
        CourseUI.Stretch(rect);
        graphic.color = color;
        graphic.raycastTarget = false;
        return graphic;
    }

    // Each entry: four corners in order round the piece
    public void Set(List<Vector2[]> uvQuads)
    {
        quads = uvQuads;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (quads == null) return;
        Rect r = rectTransform.rect;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        foreach (Vector2[] q in quads)
        {
            if (q == null || q.Length < 4) continue;
            int start = vh.currentVertCount;
            for (int k = 0; k < 4; k++)
            {
                v.position = new Vector2(r.xMin + q[k].x * r.width, r.yMin + q[k].y * r.height);
                vh.AddVert(v);
            }
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
