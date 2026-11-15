using UnityEngine;
using UnityEngine.UI;

// The button that switches the left mouse button between drawing and rubbing out the route (the scenes'
// "UndoButton"). It shows an eraser while drawing, and a pencil while rubbing out (to go back to drawing). It is only
// there while the player draws. The mouse pointer is a pencil while drawing on the map and an eraser while rubbing out.
public class EraserButton : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        var go = new GameObject("Eraser button");
        DontDestroyOnLoad(go);
        go.AddComponent<EraserButton>();
    }

    static Sprite eraser, pencil;
    static Texture2D eraserCursor, pencilCursor;
    GameObject button;
    Image image;
    ManualPathDrawer drawer;
    float nextLook, nextDrawerLook;
    int cursorShown;   // 0 the normal pointer, 1 pencil, 2 eraser

    void Update()
    {
        if (button == null && Time.unscaledTime >= nextLook)
        {
            nextLook = Time.unscaledTime + 0.5f;
            button = GameObject.Find("UndoButton");
            if (button != null) Dress(button);
        }
        if (drawer == null && Time.unscaledTime >= nextDrawerLook)
        {
            nextDrawerLook = Time.unscaledTime + 0.5f;
            NetworkPlayer local = NetworkPlayer.Local;
            AgentMovement agent = local != null ? local.GetComponent<AgentMovement>() : null;
            drawer = agent != null ? agent.manualPathDrawer : null;
        }
        UpdateCursor();
        if (button == null || drawer == null) return;
        // These modes hide the button themselves
        if (RouteChoiceGame.On || RouteAnalysis.On) return;
        bool show = drawer.timeToDraw;
        if (button.activeSelf != show) button.SetActive(show);
        if (image != null)
        {
            Sprite want = drawer.Erasing ? pencil : eraser;
            if (image.sprite != want) image.sprite = want;
        }
    }

    void UpdateCursor()
    {
        int want = 0;
        if (drawer != null && drawer.timeToDraw && !EscMenu.IsOpen && !GameModeTools.TakesMouse
            && !drawer.IsPointerOverTaggedCanvas())
        {
            want = drawer.Erasing ? 2 : 1;
        }
        if (want == cursorShown) return;
        cursorShown = want;
        MakeCursors();
        if (want == 1) Cursor.SetCursor(pencilCursor, PencilHotspot, CursorMode.Auto);
        else if (want == 2) Cursor.SetCursor(eraserCursor, new Vector2(CursorSize / 2f, CursorSize / 2f), CursorMode.Auto);
        else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void OnDisable()
    {
        if (cursorShown != 0) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        cursorShown = 0;
    }

    void Dress(GameObject button)
    {
        image = button.GetComponent<Image>();
        if (image == null) return;
        MakeSprites();
        image.sprite = eraser;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        // The old arrow or text on the button
        foreach (Transform child in button.transform) child.gameObject.SetActive(false);
        // The button switches between drawing and rubbing out (it used to take away the last bit of the line)
        Button b = button.GetComponent<Button>();
        if (b != null)
        {
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(() => { if (drawer != null) drawer.ToggleErasing(); });
        }
    }

    static void MakeSprites()
    {
        if (eraser == null) eraser = Sprite.Create(Draw(), new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        if (pencil == null) pencil = Sprite.Create(DrawPencil(), new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
    }

    const int CursorSize = 32;
    // The pencil's tip, in cursor pixels counted from the top left
    static readonly Vector2 PencilHotspot = new Vector2((Size / 2f + PencilTip) * CursorSize / Size, (Size / 2f - PencilTip) * CursorSize / Size);

    static void MakeCursors()
    {
        MakeSprites();
        if (pencilCursor == null) pencilCursor = Shrink(pencil.texture, CursorSize);
        if (eraserCursor == null) eraserCursor = Shrink(eraser.texture, CursorSize);
    }

    // A smaller copy for the mouse pointer (each new pixel is the alpha-weighted mean of the pixels it covers)
    static Texture2D Shrink(Texture2D source, int size)
    {
        Color32[] src = source.GetPixels32();
        int n = source.width / size;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int dy = 0; dy < n; dy++)
                {
                    for (int dx = 0; dx < n; dx++)
                    {
                        Color32 c = src[(y * n + dy) * source.width + x * n + dx];
                        float w = c.a / 255f;
                        r += c.r * w; g += c.g * w; b += c.b * w; a += w;
                    }
                }
                pixels[y * size + x] = a <= 0f ? new Color32(0, 0, 0, 0)
                    : new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / (n * n) * 255f));
            }
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = source.name + " cursor", filterMode = FilterMode.Bilinear };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    const int Size = 128;

    // An eraser lying at a slant: a pink rubber end, a blue sleeve, a dark edge
    static Texture2D Draw()
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Eraser", filterMode = FilterMode.Bilinear };
        var pixels = new Color32[Size * Size];
        Color32 edge = new Color32(31, 61, 43, 255), rubber = new Color32(242, 150, 150, 255), sleeve = new Color32(63, 111, 181, 255),
                stripe = new Color32(240, 240, 235, 255);
        float angle = 35f * Mathf.Deg2Rad, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        const float HalfLong = 50f, HalfShort = 24f, Corner = 8f, Line = 5f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float px = x - Size / 2f + 0.5f, py = y - Size / 2f + 0.5f;
                float u = px * cos + py * sin, v = -px * sin + py * cos;
                // Distance to a rounded box
                float qx = Mathf.Abs(u) - (HalfLong - Corner), qy = Mathf.Abs(v) - (HalfShort - Corner);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - Corner;
                Color32 c = u < -12f ? rubber : u < -6f ? stripe : sleeve;
                if (outside > -Line || Mathf.Abs(u + 12f) < Line * 0.4f) c = edge;
                float alpha = Mathf.Clamp01(0.5f - outside);
                c.a = (byte)(alpha * 255f);
                pixels[y * Size + x] = c;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    // Where the pencil's tip is: this far left and down from the middle, in texture pixels
    const float PencilTip = -40f;

    // A pencil lying at a slant with its tip at the bottom left: a dark point, bare wood, a yellow body, a grey band
    // and a pink rubber at the top right
    static Texture2D DrawPencil()
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Pencil", filterMode = FilterMode.Bilinear };
        var pixels = new Color32[Size * Size];
        Color32 edge = new Color32(31, 61, 43, 255), lead = new Color32(40, 40, 40, 255), wood = new Color32(236, 200, 150, 255),
                body = new Color32(240, 170, 40, 255), band = new Color32(170, 175, 180, 255), rubber = new Color32(242, 150, 150, 255);
        const float Half = 56f, Width = 15f, Point = 18f, Line = 4f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float px = x - Size / 2f + 0.5f, py = y - Size / 2f + 0.5f;
                float u = (px + py) * 0.70710678f, v = (py - px) * 0.70710678f;   // u along the pencil, from the tip up
                float half = u < -Half + Point ? Width * (u + Half) / Point : Width;
                float inside = Mathf.Min(half - Mathf.Abs(v), Mathf.Min(u + Half, Half - u));
                float alpha = Mathf.Clamp01(inside + 0.5f);
                if (alpha <= 0f) { pixels[y * Size + x] = new Color32(0, 0, 0, 0); continue; }
                Color32 c = u < -Half + 7f ? lead : u < -Half + Point ? wood : u < Half - 18f ? body : u < Half - 11f ? band : rubber;
                bool outline = inside < Line && u >= -Half + 7f;
                if (outline || Mathf.Abs(u - (-Half + Point)) < Line * 0.4f || Mathf.Abs(u - (Half - 18f)) < Line * 0.4f) c = edge;
                c.a = (byte)(alpha * 255f);
                pixels[y * Size + x] = c;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }
}
