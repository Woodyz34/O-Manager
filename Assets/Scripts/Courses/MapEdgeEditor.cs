using System;
using System.Collections.Generic;
using MapImport;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The "Map edge" pop-up of the map import: a small picture of the whole map file with the edge of the finished map
// drawn on it (MapBoundary.Guess, sprint maps only). Everything outside the edge is left out of the game. The player drags
// the corners, drags the small dot halfway along a side to make a new corner, and right-clicks a corner to remove it.
// "Draw new edge" starts over: each click on the map adds a corner, and a click on the first corner (or Done) closes it.
public class MapEdgeEditor : MonoBehaviour
{
    MapPreview preview;
    double[] guessed;
    Action<double[]> onDone;
    List<Vector2> points;              // corners in picture pixels (x right, y down); null = the whole map
    double[] chosen;                   // what Done gives back while no corner has been moved
    bool moved;
    bool drawing;                      // "Draw new edge": clicks on the map add corners
    double[] beforeDrawing;            // the edge to go back to if fewer than 3 corners are drawn

    RectTransform imageRect, frameRect;
    Vector2 shown;                     // size of the picture on screen (grows when zoomed in)
    Vector2 baseShown, area;           // the whole picture fitted in the frame; the frame's size
    float zoom = 1f;
    Vector2 pan;                       // where the picture's middle sits in the frame
    const float MaxZoom = 16f;
    EdgeGraphic lines;
    readonly List<GameObject> handles = new List<GameObject>();
    TextMeshProUGUI hint;
    Texture2D texture;

    static readonly Color EdgeColor = CourseUI.Hex("E8742A");

    public static MapEdgeEditor Open(Transform canvas, MapPreview preview, double[] current, double[] guessed, Action<double[]> onDone)
    {
        var dim = CourseUI.Panel("Map edge", canvas, new Color(0.08f, 0.14f, 0.1f, 0.7f), false);
        CourseUI.Stretch(dim.rectTransform);
        MapEdgeEditor editor = dim.gameObject.AddComponent<MapEdgeEditor>();
        editor.preview = preview;
        editor.guessed = guessed;
        editor.onDone = onDone;
        editor.chosen = current;
        editor.points = editor.ToPixels(current);
        editor.Build(dim.transform);
        return editor;
    }

    void OnDestroy()
    {
        if (texture != null) Destroy(texture);
    }

    // ---- Layout ----

    void Build(Transform root)
    {
        Image card = CourseUI.CardPanel("Card", root);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 1000));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Map edge", 34, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(1100, 44));
        hint = CourseUI.Text("Hint", card.transform, "", 19, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(1100, 54));

        // The picture, as big as fits
        area = new Vector2(1100, 760);
        float scale = Mathf.Min(area.x / preview.width, area.y / preview.height);
        baseShown = shown = new Vector2(preview.width * scale, preview.height * scale);
        texture = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false);
        texture.LoadRawTextureData(preview.rgba);
        texture.Apply(false, true);
        Image frame = CourseUI.Panel("Frame", card.transform, Color.white, false);
        CourseUI.Place(frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -130), area);
        frame.gameObject.AddComponent<RectMask2D>();
        frameRect = frame.rectTransform;
        // Behind the picture: catches the wheel (zoom) and drags on the map (move it)
        Image view = CourseUI.Panel("View", frame.transform, new Color(1, 1, 1, 0));
        CourseUI.Stretch(view.rectTransform);
        view.raycastTarget = true;
        view.gameObject.AddComponent<EdgeView>().editor = this;
        RawImage picture = CourseUI.Rect("Picture", frame.transform).gameObject.AddComponent<RawImage>();
        picture.texture = texture;
        picture.uvRect = new Rect(0, 1, 1, -1);     // the preview's row 0 is the top
        picture.raycastTarget = false;
        imageRect = picture.rectTransform;
        CourseUI.Place(imageRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, shown);

        RectTransform edgeRect = CourseUI.Rect("Edge", imageRect);
        edgeRect.gameObject.AddComponent<CanvasRenderer>();     // before the Graphic, or it caches a missing renderer and draws nothing
        lines = edgeRect.gameObject.AddComponent<EdgeGraphic>();
        CourseUI.Stretch(lines.rectTransform);
        lines.color = EdgeColor;
        lines.raycastTarget = false;

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Place(buttons, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(1100, 58));
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        Button zoomOut = CourseUI.Button("Zoom out", buttons, "-", CourseUI.ButtonKind.Light, () => Zoom(Vector2.zero, 0.67f), 28);
        CourseUI.Size(zoomOut, -1, 70, 0);
        Button zoomIn = CourseUI.Button("Zoom in", buttons, "+", CourseUI.ButtonKind.Light, () => Zoom(Vector2.zero, 1.5f), 28);
        CourseUI.Size(zoomIn, -1, 70, 0);
        HoverTip.Add(zoomOut, "Zoom out. The mouse wheel does the same.");
        HoverTip.Add(zoomIn, "Zoom in. The mouse wheel does the same.");
        HoverTip.Add(CourseUI.Button("Whole map", buttons, "Whole map", CourseUI.ButtonKind.Light, () => { drawing = false; chosen = null; moved = false; points = null; Redraw(); }, 22),
            "Use the whole picture, with no edge. Nothing is left out.");
        HoverTip.Add(CourseUI.Button("Draw", buttons, "Draw new edge", CourseUI.ButtonKind.Secondary, StartDrawing, 22),
            "Draw the edge yourself: click on the map for each corner, then click the first corner (or Done) to close it. Drag the map to move it while you draw.");
        if (guessed != null)
        {
            Button guess = CourseUI.Button("Guess", buttons, "Use the guess", CourseUI.ButtonKind.Secondary, () => { drawing = false; chosen = guessed; moved = false; points = ToPixels(guessed); Redraw(); }, 22);
            HoverTip.Add(guess, "Use the edge that the game guessed from the map file.");
        }
        HoverTip.Add(CourseUI.Button("Done", buttons, "Done", CourseUI.ButtonKind.Primary, Done, 22), "Keep this edge for the map.");
        Redraw();
    }

    void Done()
    {
        if (drawing && !FinishDrawing()) { onDone?.Invoke(beforeDrawing); Destroy(gameObject); return; }
        onDone?.Invoke(moved ? ToPaper(points) : chosen);
        Destroy(gameObject);
    }

    // ---- Drawing a new edge ----

    void StartDrawing()
    {
        if (!drawing) beforeDrawing = moved ? ToPaper(points) : chosen;
        drawing = true;
        points = new List<Vector2>();
        moved = true;
        Redraw();
    }

    // Closes the drawn line; false when it has fewer than 3 corners (then the old edge comes back)
    bool FinishDrawing()
    {
        drawing = false;
        if (points != null && points.Count >= 3) { Redraw(); return true; }
        chosen = beforeDrawing;
        moved = false;
        points = ToPixels(beforeDrawing);
        Redraw();
        return false;
    }

    // A click on the map (not a drag): a new corner while drawing
    internal void ClickAt(Vector2 screen, Camera cam)
    {
        if (!drawing) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(imageRect, screen, cam, out Vector2 local)) return;
        points.Add(ToPixel(local));
        Redraw();
    }

    // A left click on a corner: the first one closes the line while drawing
    internal void CornerClicked(EdgeHandle handle)
    {
        if (drawing && !handle.middle && handle.index == 0 && points.Count >= 3) FinishDrawing();
    }

    // ---- Corners ----

    List<Vector2> ToPixels(double[] poly)
    {
        if (poly == null || poly.Length < 6) return null;
        var list = new List<Vector2>();
        for (int i = 0; i + 1 < poly.Length; i += 2) list.Add(new Vector2((float)preview.PixelX(poly[i]), (float)preview.PixelY(poly[i + 1])));
        return list;
    }

    double[] ToPaper(List<Vector2> list)
    {
        if (list == null || list.Count < 3) return null;
        var poly = new double[list.Count * 2];
        for (int i = 0; i < list.Count; i++)
        {
            poly[2 * i] = preview.PaperX(list[i].x);
            poly[2 * i + 1] = preview.PaperY(list[i].y);
        }
        return poly;
    }

    Vector2 ToLocal(Vector2 pixel) => new Vector2((pixel.x / preview.width - 0.5f) * shown.x, (0.5f - pixel.y / preview.height) * shown.y);

    Vector2 ToPixel(Vector2 local) => new Vector2(
        Mathf.Clamp((local.x / shown.x + 0.5f) * preview.width, 0, preview.width),
        Mathf.Clamp((0.5f - local.y / shown.y) * preview.height, 0, preview.height));

    void Redraw()
    {
        foreach (GameObject h in handles) Destroy(h);
        handles.Clear();
        hint.text = drawing
            ? (points.Count < 3 ? "Click on the map where the edge should go, one click per corner. Drag the map to move it; the wheel zooms."
                                : "Keep clicking to add corners. Click the first corner (or Done) to close the edge. Right-click a corner to remove it.")
            : points == null
            ? "The whole map file is used. Click \"Draw new edge\" to leave out unfinished parts" + (guessed != null ? ", or \"Use the guess\" to see where the game thinks the finished map ends." : ".")
            : "Everything outside the orange line is left out. Drag the corners; drag a small dot to make a new corner; right-click a corner to remove it. The wheel or + and - zoom; drag the map to move it.";
        if (points == null)
        {
            lines.SetPoints(null);
            return;
        }
        lines.closed = !drawing;
        for (int i = 0; i < points.Count; i++)
        {
            int next = (i + 1) % points.Count;
            AddHandle(i, false, ToLocal(points[i]));
            if (!drawing) AddHandle(i, true, (ToLocal(points[i]) + ToLocal(points[next])) / 2f);
        }
        UpdateLines();
    }

    void UpdateLines()
    {
        var local = new List<Vector2>();
        if (points != null) foreach (Vector2 p in points) local.Add(ToLocal(p));
        lines.SetPoints(local);
    }

    void AddHandle(int index, bool middle, Vector2 at)
    {
        Image dot = CourseUI.Panel(middle ? "Middle " + index : "Corner " + index, imageRect, middle ? Color.white : EdgeColor);
        float size = middle ? 10 : 14;
        CourseUI.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), at, new Vector2(size, size));
        // Corners: orange with a white ring; middle dots: white with an orange ring, so they show on white paper too
        Outline ring = dot.gameObject.AddComponent<Outline>();
        ring.effectColor = middle ? EdgeColor : Color.white;
        ring.effectDistance = new Vector2(2, -2);
        EdgeHandle handle = dot.gameObject.AddComponent<EdgeHandle>();
        handle.editor = this;
        handle.index = index;
        handle.middle = middle;
        handles.Add(dot.gameObject);
    }

    // A corner (or a new one from a side's middle) follows the pointer
    internal void BeginDrag(EdgeHandle handle)
    {
        if (!handle.middle || points == null) return;
        int at = handle.index + 1;
        points.Insert(at, ToPixel(handle.Rect.anchoredPosition));
        handle.index = at;
        handle.middle = false;
    }

    internal void Drag(EdgeHandle handle, PointerEventData data)
    {
        if (handle.middle || points == null || handle.index >= points.Count) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(imageRect, data.position, data.pressEventCamera, out Vector2 local)) return;
        Vector2 pixel = ToPixel(local);
        points[handle.index] = pixel;
        moved = true;
        handle.Rect.anchoredPosition = ToLocal(pixel);
        UpdateLines();
    }

    internal void EndDrag()
    {
        Redraw();   // the middle dots move to the new sides
    }

    // ---- Zoom and move ----

    // Zooms round a point on the screen (null: the middle of the frame), keeping that spot of the map under it
    internal void ZoomAt(Vector2 screen, Camera cam, float factor)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frameRect, screen, cam, out Vector2 at)) at = Vector2.zero;
        Zoom(at, factor);
    }


    void Zoom(Vector2 at, float factor)
    {
        float next = Mathf.Clamp(zoom * factor, 1f, MaxZoom);
        if (Mathf.Approximately(next, zoom)) return;
        Vector2 spot = new Vector2((at.x - pan.x) / shown.x, (at.y - pan.y) / shown.y);   // -0.5..0.5 across the picture
        zoom = next;
        shown = baseShown * zoom;
        pan = at - new Vector2(spot.x * shown.x, spot.y * shown.y);
        ApplyView(true);
    }

    internal void PanBy(Vector2 screen, Vector2 delta, Camera cam)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frameRect, screen, cam, out Vector2 now)) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frameRect, screen - delta, cam, out Vector2 before)) return;
        pan += now - before;
        ApplyView(false);
    }

    void ApplyView(bool resized)
    {
        // The picture may not leave the frame: at most its own overhang each way
        float maxX = Mathf.Max(0, (shown.x - area.x) / 2f), maxY = Mathf.Max(0, (shown.y - area.y) / 2f);
        pan = new Vector2(Mathf.Clamp(pan.x, -maxX, maxX), Mathf.Clamp(pan.y, -maxY, maxY));
        imageRect.sizeDelta = shown;
        imageRect.anchoredPosition = pan;
        if (resized) Redraw();   // corners and lines sit at the new size
    }

    internal void Remove(EdgeHandle handle)
    {
        if (handle.middle || points == null || (!drawing && points.Count <= 3)) return;
        points.RemoveAt(handle.index);
        moved = true;
        Redraw();
    }
}
