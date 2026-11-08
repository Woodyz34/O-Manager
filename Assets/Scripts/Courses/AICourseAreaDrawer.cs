using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The AI course's "Choose the area" window: the map, and the player holds the left mouse button and draws a ring
// round the part they want the course in. Letting go closes the ring. Outside the ring is dimmed.
// The wheel (or + and -) zooms round the mouse, as in the map import's Map edge window; dragging with the right or
// middle mouse button moves the map. Zoomed in, the full-size map picture is used so it stays sharp.
// The ring is drawn like a route in the race (a red line with a dark edge) and kept in map coordinates, so it fits
// the grid whatever the picture's size (see CourseGenerator.Settings.region).
public class AICourseAreaDrawer : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IScrollHandler
{
    const int OverlayWidth = 1024;       // the dimming is drawn at this width; its height follows the picture
    const float MinStepPixels = 4f;      // a new point while drawing at least this far from the last, on the screen
    const int MaxPoints = 1500;
    const float MinShare = 0.0005f;      // a ring smaller than this share of the picture is a slip, not an area
    const float MaxZoom = 16f;
    const float SharpFromZoom = 1.8f;    // from this zoom on, the full-size picture is shown instead of the thumbnail

    MapDefinition map;
    MapBackground background;
    Action onClosed;

    RectTransform frameRect, content;
    RawImage picture;
    bool fullPicture;
    Vector2 baseShown, area;
    float zoom = 1f;
    Vector2 pan;
    RawImage dimImage;
    Texture2D dim;
    Color32[] pixels;
    int width, height;
    UIRouteLine edgeLine, routeLine;
    TextMeshProUGUI info;

    readonly List<Vector2> stroke = new List<Vector2>();   // 0..1 on the picture, while drawing or the closed ring
    bool drawing, panning;
    bool closed;

    static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    static readonly Color32 Dim = new Color32(18, 30, 22, 140);
    // The same as a route in the race: red with a dark edge (RouteChoiceGame.Colours[0], RouteAnalysis edge)
    static readonly Color RouteRed = new Color(0.86f, 0.15f, 0.15f);
    static readonly Color RouteEdge = new Color(0.12f, 0.12f, 0.12f);
    const float LineWidth = 6.5f;        // screen pixels at 1080p, about as thick as a route on the race map
    const float EdgeWidth = LineWidth * 1.45f;

    // Opens the window over 'parent'. onDone gets the ring in map coordinates, or null for the whole map;
    // it is not called when the player cancels. onClosed is called when the window closes, whichever way.
    public static void Open(Transform parent, MapDefinition map, List<CoursePoint> current, Action<List<CoursePoint>> onDone, Action onClosed)
    {
        Image shade = CourseUI.Panel("Choose the area", parent, new Color(0.08f, 0.14f, 0.1f, 0.6f), false);
        CourseUI.Stretch(shade.rectTransform);
        Image card = CourseUI.CardPanel("Card", shade.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560, 960));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Choose the area", 36, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -26), new Vector2(600, 48));
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform,
            "Hold the left mouse button and draw a ring round the part of the map you find most fun; let go to close it. Draw again to start over. "
            + "The wheel or + and - zoom; drag with the right mouse button to move the map. The controls stay inside the ring; routes may leave it.", 20, CourseUI.InkMuted);
        CourseUI.Place(hint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -76), new Vector2(1480, 56));

        // The frame catches every pointer event (the pictures and lines in it do not), and hides what is zoomed out of it
        Image frame = CourseUI.Panel("Frame", card.transform, CourseUI.CardSolid);
        Vector2 frameSize = new Vector2(1480, 712);
        CourseUI.Place(frame.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -136), frameSize);
        frame.gameObject.AddComponent<RectMask2D>();
        frame.raycastTarget = true;
        AICourseAreaDrawer drawer = frame.gameObject.AddComponent<AICourseAreaDrawer>();
        drawer.frameRect = frame.rectTransform;
        drawer.area = frameSize;
        drawer.map = map;
        drawer.onClosed = onClosed;

        MapBackground background = map != null ? map.GetBackground(null) : null;
        drawer.background = background;
        Texture2D texture = background != null ? MapPackages.LoadThumbnail(background) : null;
        float aspect = texture != null ? texture.width / (float)texture.height
                     : background != null && background.pixelSize.y > 0 ? background.pixelSize.x / background.pixelSize.y : 1.4f;
        Vector2 size = new Vector2(frameSize.x - 20, (frameSize.x - 20) / aspect);
        if (size.y > frameSize.y - 20) size = new Vector2((frameSize.y - 20) * aspect, frameSize.y - 20);
        drawer.baseShown = size;

        // Everything that zooms and moves together: the picture, the dimming and the ring
        drawer.content = CourseUI.Rect("Map", frame.transform);
        CourseUI.Place(drawer.content, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);

        drawer.picture = CourseUI.Rect("Picture", drawer.content).gameObject.AddComponent<RawImage>();
        CourseUI.Stretch(drawer.picture.rectTransform);
        drawer.picture.texture = texture;
        drawer.picture.color = texture != null ? Color.white : new Color(0.85f, 0.85f, 0.8f);
        drawer.picture.raycastTarget = false;

        drawer.dimImage = CourseUI.Rect("Dim", drawer.content).gameObject.AddComponent<RawImage>();
        CourseUI.Stretch(drawer.dimImage.rectTransform);
        drawer.dimImage.raycastTarget = false;
        drawer.width = OverlayWidth;
        drawer.height = Mathf.Max(8, Mathf.RoundToInt(OverlayWidth / aspect));
        drawer.dim = new Texture2D(drawer.width, drawer.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        drawer.pixels = new Color32[drawer.width * drawer.height];
        drawer.dimImage.texture = drawer.dim;

        drawer.edgeLine = UIRouteLine.Make("Ring edge", drawer.content, RouteEdge, EdgeWidth);
        drawer.routeLine = UIRouteLine.Make("Ring", drawer.content, RouteRed, LineWidth);

        // Bottom: what is chosen, and the buttons
        drawer.info = CourseUI.Text("Info", card.transform, "", 22, CourseUI.Ink);
        CourseUI.Place(drawer.info.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 30), new Vector2(640, 60));
        drawer.info.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Place(buttons, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 30), new Vector2(820, 60));
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(CourseUI.Button("Zoom out", buttons, "-", CourseUI.ButtonKind.Light, () => drawer.Zoom(Vector2.zero, 0.67f), 28), 60, 70);
        CourseUI.Size(CourseUI.Button("Zoom in", buttons, "+", CourseUI.ButtonKind.Light, () => drawer.Zoom(Vector2.zero, 1.5f), 28), 60, 70);
        CourseUI.Size(CourseUI.Button("Whole map", buttons, "Whole map", CourseUI.ButtonKind.Light, () => { drawer.stroke.Clear(); drawer.closed = false; drawer.Redraw(); }, 22), 60, -1, 1);
        CourseUI.Size(CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, () => Destroy(shade.gameObject), 22), 60, -1, 1);
        CourseUI.Size(CourseUI.Button("Use", buttons, "Use", CourseUI.ButtonKind.Primary, () =>
        {
            List<CoursePoint> ring = drawer.closed ? drawer.ToMap() : null;
            Destroy(shade.gameObject);
            onDone?.Invoke(ring);
        }, 22), 60, -1, 1);

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
        if (dim != null) Destroy(dim);
        onClosed?.Invoke();
    }

    // ---- The mouse: left draws, right or middle moves the map, the wheel zooms ----

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            panning = true;
            return;
        }
        if (!ToUV(eventData.position, eventData.pressEventCamera, out Vector2 uv)) return;
        stroke.Clear();
        stroke.Add(Clamp(uv));
        drawing = true;
        closed = false;
        Redraw();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            if (panning) PanBy(eventData.position, eventData.delta, eventData.pressEventCamera);
            return;
        }
        if (!drawing || stroke.Count >= MaxPoints) return;
        if (!ToUV(eventData.position, eventData.pressEventCamera, out Vector2 uv)) return;
        uv = Clamp(uv);
        // A new point every few pixels on the screen, so the ring is as fine when zoomed in as out
        Vector2 last = stroke[stroke.Count - 1];
        Vector2 shown = content.rect.size;
        if (Vector2.Distance(new Vector2(uv.x * shown.x, uv.y * shown.y), new Vector2(last.x * shown.x, last.y * shown.y)) < MinStepPixels) return;
        stroke.Add(uv);
        UpdateLines();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            panning = false;
            return;
        }
        if (!drawing) return;
        drawing = false;
        // A ring needs a few points and some size; a click or a short line is forgotten
        closed = stroke.Count >= 3 && Mathf.Abs(SignedArea(stroke)) >= MinShare;
        if (!closed) stroke.Clear();
        Redraw();
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (drawing) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frameRect, eventData.position, eventData.enterEventCamera, out Vector2 at)) at = Vector2.zero;
        Zoom(at, eventData.scrollDelta.y > 0 ? 1.25f : 0.8f);
    }

    bool ToUV(Vector2 screen, Camera cam, out Vector2 uv)
    {
        uv = default;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screen, cam, out Vector2 local)) return false;
        Rect r = content.rect;
        uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        return true;
    }

    static Vector2 Clamp(Vector2 uv) => new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));

    // ---- Zoom and move (as MapEdgeEditor) ----

    // Zooms round a point in the frame (its middle is 0,0), keeping that spot of the map under it
    void Zoom(Vector2 at, float factor)
    {
        float next = Mathf.Clamp(zoom * factor, 1f, MaxZoom);
        if (Mathf.Approximately(next, zoom)) return;
        Vector2 shown = baseShown * zoom;
        Vector2 spot = new Vector2((at.x - pan.x) / shown.x, (at.y - pan.y) / shown.y);   // -0.5..0.5 across the picture
        zoom = next;
        shown = baseShown * zoom;
        pan = at - new Vector2(spot.x * shown.x, spot.y * shown.y);
        ApplyView();
        if (zoom >= SharpFromZoom) UseFullPicture();
    }

    void PanBy(Vector2 screen, Vector2 delta, Camera cam)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frameRect, screen, cam, out Vector2 now)) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(frameRect, screen - delta, cam, out Vector2 before)) return;
        pan += now - before;
        ApplyView();
    }

    void ApplyView()
    {
        // The picture may not leave the frame: at most its own overhang each way
        Vector2 shown = baseShown * zoom;
        float maxX = Mathf.Max(0, (shown.x - area.x) / 2f), maxY = Mathf.Max(0, (shown.y - area.y) / 2f);
        pan = new Vector2(Mathf.Clamp(pan.x, -maxX, maxX), Mathf.Clamp(pan.y, -maxY, maxY));
        content.sizeDelta = shown;
        content.anchoredPosition = pan;
        UpdateLines();   // the lines keep their width on the screen
    }

    // The thumbnail is blurry when zoomed in: load the map's own picture once (the race uses the same one)
    void UseFullPicture()
    {
        if (fullPicture || background == null) return;
        fullPicture = true;
        Sprite sprite = MapPackages.LoadSprite(background);
        if (sprite == null || sprite.texture == null) return;
        Rect r = sprite.textureRect;
        picture.texture = sprite.texture;
        picture.uvRect = new Rect(r.x / sprite.texture.width, r.y / sprite.texture.height, r.width / sprite.texture.width, r.height / sprite.texture.height);
    }

    // ---- The dimming and the ring ----

    void Redraw()
    {
        bool dimOutside = closed && stroke.Count >= 3;
        Color32 fill = dimOutside ? Dim : Clear;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
        if (dimOutside)
        {
            // Clear the inside again: one scanline through the middle of every row (even-odd)
            List<Vector2> poly = stroke.ConvertAll(uv => new Vector2(uv.x * width, uv.y * height));
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
        dim.SetPixels32(pixels);
        dim.Apply(false);
        UpdateLines();
        ShowInfo();
    }

    void UpdateLines()
    {
        edgeLine.Set(stroke, closed);
        routeLine.Set(stroke, closed);
        if (drawing && info != null) info.text = "Drawing... let go to close the ring.";
    }

    void ShowInfo()
    {
        if (info == null) return;
        if (drawing) { info.text = "Drawing... let go to close the ring."; return; }
        info.text = closed
            ? "<b>Your area:</b> about " + AreaText(ToMap()) + ". Press Use to lay the AI course in it."
            : "<b>Whole map.</b> The course can go anywhere on the map.";
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
        CoursePoint? last = null;
        foreach (Vector2 uv in stroke)
        {
            // The inverse of MapBackground.WorldToUV
            Vector2 pixel = new Vector2(uv.x * background.pixelSize.x, uv.y * background.pixelSize.y);
            Vector3 local = (Vector3)((pixel - background.pivotPixels) / background.pixelsPerUnit) * background.scale;
            Vector3 world = Quaternion.Euler(0f, 0f, background.rotation) * local + background.position;
            CoursePoint p = map.FromWorld(world);
            p = new CoursePoint(Math.Round(p.e), Math.Round(p.n));
            if (last.HasValue && last.Value.e == p.e && last.Value.n == p.n) continue;   // zoomed in, several points share a metre
            ring.Add(p);
            last = p;
        }
        return ring;
    }
}
