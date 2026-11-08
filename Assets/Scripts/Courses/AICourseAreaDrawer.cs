using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The AI course's "Choose the area" window: the whole map, and the player holds the mouse button and draws a ring
// round the part they want the course in. Letting go closes the ring. Outside the ring is dimmed. The ring is kept
// in map coordinates, so it fits the grid whatever the picture's size (see CourseGenerator.Settings.region).
public class AICourseAreaDrawer : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    const int OverlayWidth = 960;        // the overlay is drawn at this width; its height follows the picture
    const float MinStep = 0.004f;        // a new point while drawing at least this far from the last (share of the width)
    const int MaxPoints = 400;
    const float MinShare = 0.002f;       // a ring smaller than this share of the picture is a slip, not an area

    MapDefinition map;
    MapBackground background;
    Action onClosed;

    RawImage overlayImage;
    Texture2D overlay;
    Color32[] pixels;
    int width, height;
    TextMeshProUGUI info;

    readonly List<Vector2> stroke = new List<Vector2>();   // 0..1 on the picture, while drawing or the closed ring
    bool drawing;
    bool closed;

    static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    static readonly Color32 Dim = new Color32(18, 30, 22, 140);
    static readonly Color32 Line = new Color32(232, 116, 42, 255);    // the orange accent
    static readonly Color32 LineEdge = new Color32(255, 255, 255, 230);

    // Opens the window over 'parent'. onDone gets the ring in map coordinates, or null for the whole map;
    // it is not called when the player cancels. onClosed is called when the window closes, whichever way.
    public static void Open(Transform parent, MapDefinition map, List<CoursePoint> current, Action<List<CoursePoint>> onDone, Action onClosed)
    {
        Image dim = CourseUI.Panel("Choose the area", parent, new Color(0.08f, 0.14f, 0.1f, 0.6f), false);
        CourseUI.Stretch(dim.rectTransform);
        Image card = CourseUI.CardPanel("Card", dim.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560, 960));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Choose the area", 36, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -26), new Vector2(600, 48));
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform,
            "Hold the mouse button and draw a ring round the part of the map you find most fun. Let go to close the ring. "
            + "Draw again to start over. The controls stay inside the ring; the routes between them may leave it.", 20, CourseUI.InkMuted);
        CourseUI.Place(hint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -76), new Vector2(1480, 56));

        // The map, as big as fits, with the same width/height ratio as its picture
        Image frame = CourseUI.Panel("Frame", card.transform, CourseUI.CardSolid);
        CourseUI.Place(frame.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -136), new Vector2(1480, 712));
        frame.gameObject.AddComponent<RectMask2D>();

        MapBackground background = map != null ? map.GetBackground(null) : null;
        Texture2D texture = background != null ? MapPackages.LoadThumbnail(background) : null;
        float aspect = texture != null ? texture.width / (float)texture.height
                     : background != null && background.pixelSize.y > 0 ? background.pixelSize.x / background.pixelSize.y : 1.4f;
        Vector2 size = new Vector2(1460, 1460 / aspect);
        if (size.y > 692) size = new Vector2(692 * aspect, 692);

        RawImage picture = CourseUI.Rect("Map", frame.transform).gameObject.AddComponent<RawImage>();
        CourseUI.Place(picture.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        picture.texture = texture;
        picture.color = texture != null ? Color.white : new Color(0.85f, 0.85f, 0.8f);
        picture.raycastTarget = false;

        RawImage overlayImage = CourseUI.Rect("Area", frame.transform).gameObject.AddComponent<RawImage>();
        CourseUI.Place(overlayImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        overlayImage.raycastTarget = true;   // takes the drawing
        AICourseAreaDrawer drawer = overlayImage.gameObject.AddComponent<AICourseAreaDrawer>();
        drawer.map = map;
        drawer.background = background;
        drawer.onClosed = onClosed;
        drawer.overlayImage = overlayImage;
        drawer.width = OverlayWidth;
        drawer.height = Mathf.Max(8, Mathf.RoundToInt(OverlayWidth / aspect));
        drawer.overlay = new Texture2D(drawer.width, drawer.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        drawer.pixels = new Color32[drawer.width * drawer.height];
        overlayImage.texture = drawer.overlay;

        // Bottom: what is chosen, and the buttons
        drawer.info = CourseUI.Text("Info", card.transform, "", 22, CourseUI.Ink);
        CourseUI.Place(drawer.info.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 30), new Vector2(720, 60));
        drawer.info.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Place(buttons, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 30), new Vector2(720, 60));
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Button("Whole map", buttons, "Whole map", CourseUI.ButtonKind.Light, () => { drawer.stroke.Clear(); drawer.closed = false; drawer.Redraw(); }, 22);
        CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, () => Destroy(dim.gameObject), 22);
        CourseUI.Button("Use", buttons, "Use", CourseUI.ButtonKind.Primary, () =>
        {
            List<CoursePoint> ring = drawer.closed ? drawer.ToMap() : null;
            Destroy(dim.gameObject);
            onDone?.Invoke(ring);
        }, 22);

        // Show the area chosen before
        if (current != null && current.Count >= 3 && background != null)
        {
            foreach (CoursePoint p in current) drawer.stroke.Add(background.WorldToUV(map.ToWorld(p)));
            drawer.closed = true;
        }
        drawer.Redraw();
    }

    void OnDestroy()
    {
        if (overlay != null) Destroy(overlay);
        onClosed?.Invoke();
    }

    // ---- Drawing with the mouse ----

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (!ToUV(eventData, out Vector2 uv)) return;
        stroke.Clear();
        stroke.Add(uv);
        drawing = true;
        closed = false;
        Redraw();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!drawing || stroke.Count >= MaxPoints) return;
        if (!ToUV(eventData, out Vector2 uv)) return;
        uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
        Vector2 last = stroke[stroke.Count - 1];
        if (Vector2.Distance(Scaled(uv), Scaled(last)) < MinStep * width) return;
        stroke.Add(uv);
        Redraw();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!drawing) return;
        drawing = false;
        // A ring needs a few points and some size; a click or a short line is forgotten
        closed = stroke.Count >= 3 && Mathf.Abs(SignedArea(stroke)) >= MinShare;
        if (!closed) stroke.Clear();
        Redraw();
    }

    bool ToUV(PointerEventData eventData, out Vector2 uv)
    {
        RectTransform rect = overlayImage.rectTransform;
        uv = default;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local)) return false;
        Rect r = rect.rect;
        uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        return true;
    }

    Vector2 Scaled(Vector2 uv) => new Vector2(uv.x * width, uv.y * height);

    // ---- The overlay picture ----

    void Redraw()
    {
        bool dimOutside = closed && stroke.Count >= 3;
        Color32 fill = dimOutside ? Dim : Clear;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;

        List<Vector2> poly = stroke.ConvertAll(Scaled);
        if (dimOutside)
        {
            // Clear the inside again: one scanline through the middle of every row (even-odd)
            List<float> crossings = new List<float>();
            for (int y = 0; y < height; y++)
            {
                float cy = y + 0.5f;
                crossings.Clear();
                for (int k = 0; k < poly.Count; k++)
                {
                    Vector2 a = poly[k], b = poly[(k + 1) % poly.Count];
                    if ((a.y <= cy && cy < b.y) || (b.y <= cy && cy < a.y)) crossings.Add(a.x + (cy - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int from = Mathf.Max(0, Mathf.CeilToInt(crossings[k] - 0.5f));
                    int to = Mathf.Min(width - 1, Mathf.FloorToInt(crossings[k + 1] - 0.5f));
                    for (int x = from; x <= to; x++) pixels[y * width + x] = Clear;
                }
            }
        }
        int sides = dimOutside ? poly.Count : poly.Count - 1;
        for (int k = 0; k < sides; k++) DrawLine(poly[k], poly[(k + 1) % poly.Count], 6f, LineEdge);
        for (int k = 0; k < sides; k++) DrawLine(poly[k], poly[(k + 1) % poly.Count], 3.5f, Line);
        overlay.SetPixels32(pixels);
        overlay.Apply(false);

        ShowInfo();
    }

    void ShowInfo()
    {
        if (info == null) return;
        if (drawing) { info.text = "Drawing... let go to close the ring."; return; }
        if (!closed)
        {
            info.text = "<b>Whole map.</b> The course can go anywhere on the map.";
            return;
        }
        info.text = "<b>Your area:</b> about " + AreaText(ToMap()) + ". Press Use to lay the AI course in it.";
    }

    // The size of a ring in map coordinates, like "1.4 km2" or "8 ha" for small sprint areas
    public static string AreaText(List<CoursePoint> ring)
    {
        double sum = 0;
        for (int k = 0; k < ring.Count; k++)
        {
            CoursePoint a = ring[k], b = ring[(k + 1) % ring.Count];
            sum += a.e * b.n - b.e * a.n;
        }
        double squareMeters = Math.Abs(sum) / 2.0;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (squareMeters < 1000000.0) return Math.Max(1, Math.Round(squareMeters / 10000.0)).ToString("0", culture) + " ha";
        return (squareMeters / 1000000.0).ToString("0.0", culture) + " km<sup>2</sup>";
    }

    void DrawLine(Vector2 a, Vector2 b, float lineWidth, Color32 color)
    {
        float half = lineWidth / 2f;
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - half)), maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + half));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - half)), maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + half));
        Vector2 ab = b - a;
        float lengthSquared = Mathf.Max(ab.sqrMagnitude, 0.0001f);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
                if ((a + ab * t - p).sqrMagnitude <= half * half) pixels[y * width + x] = color;
            }
    }

    static float SignedArea(List<Vector2> poly)
    {
        float sum = 0f;
        for (int k = 0; k < poly.Count; k++)
        {
            Vector2 a = poly[k], b = poly[(k + 1) % poly.Count];
            sum += a.x * b.y - b.x * a.y;
        }
        return sum / 2f;
    }

    // The ring in map coordinates (whole metres, like saved courses)
    List<CoursePoint> ToMap()
    {
        List<CoursePoint> ring = new List<CoursePoint>();
        if (background == null || map == null) return ring;
        foreach (Vector2 uv in stroke)
        {
            // The inverse of MapBackground.WorldToUV
            Vector2 pixel = new Vector2(uv.x * background.pixelSize.x, uv.y * background.pixelSize.y);
            Vector3 local = (Vector3)((pixel - background.pivotPixels) / background.pixelsPerUnit) * background.scale;
            Vector3 world = Quaternion.Euler(0f, 0f, background.rotation) * local + background.position;
            CoursePoint p = map.FromWorld(world);
            ring.Add(new CoursePoint(Math.Round(p.e), Math.Round(p.n)));
        }
        return ring;
    }
}
