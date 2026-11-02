using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The player's own colour (the dot, the trail and the drawn line), saved between games
public static class PlayerColor
{
    const string Key = "PlayerColor";

    // Networked as 0xRRGGBB + 1, so 0 can mean "no colour chosen"
    public static int Pack(Color color)
    {
        Color32 c = color;
        return ((c.r << 16) | (c.g << 8) | c.b) + 1;
    }

    public static Color Unpack(int packed)
    {
        int rgb = Mathf.Max(0, packed - 1);
        return new Color32((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255), 255);
    }

    public static bool HasSaved => PlayerPrefs.HasKey(Key);

    public static Color Saved
    {
        get
        {
            if (ColorUtility.TryParseHtmlString("#" + PlayerPrefs.GetString(Key, ""), out Color color)) return color;
            return new Color(0.91f, 0.45f, 0.16f);
        }
    }

    public static int SavedPacked() => HasSaved ? Pack(Saved) : 0;

    public static void Save(Color color)
    {
        PlayerPrefs.SetString(Key, ColorUtility.ToHtmlStringRGB(color));
        PlayerPrefs.Save();
    }
}

// "Change color" in the lobby: pick any colour on a colour wheel (plus brightness, or a few ready ones),
// see it on your runner at once, and keep it for every game after this one
public class PlayerColorPicker : MonoBehaviour
{
    static PlayerColorPicker open;

    static readonly string[] Presets =
    {
        "E8742A", "D7263D", "F2C14E", "3F7D54", "2E86DE", "7B3FB0", "222222", "F4F1E8"
    };

    Color color;
    Color before;
    Image preview;
    ColorWheel wheel;
    Slider valueSlider;
    float hue;
    float saturation;
    TextMeshProUGUI hexText;
    bool setting;

    public static void Open()
    {
        if (open != null) return;
        GameObject go = new GameObject("Color picker", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        open = go.AddComponent<PlayerColorPicker>();
        open.Build();
    }

    void Build()
    {
        before = PlayerColor.Saved;
        color = before;

        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Image card = CourseUI.CardPanel("Card", dim.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 900));
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 26, 28));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Your colour", 38, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 48);

        RectTransform previewRow = CourseUI.Rect("Preview row", card.transform);
        CourseUI.Row(previewRow.gameObject, 16, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(previewRow, 70);
        preview = CourseUI.Panel("Preview", previewRow, color);
        CourseUI.Size(preview, 70, 70);
        hexText = CourseUI.Text("Hex", previewRow, "", 26, CourseUI.InkMuted, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        CourseUI.Size(hexText, 70, 300);

        // The wheel, centred in its own row
        RectTransform wheelRow = CourseUI.Rect("Wheel row", card.transform);
        CourseUI.Size(wheelRow, 400);
        wheel = ColorWheel.Create(wheelRow, 390);
        wheel.Picked = (h, s) => { hue = h; saturation = s; FromSliders(); };

        valueSlider = Labeled(card.transform, "Brightness", 0f, 1f, v => FromSliders());

        RectTransform swatches = CourseUI.Rect("Swatches", card.transform);
        GridLayoutGroup grid = swatches.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(56, 44);
        grid.spacing = new Vector2(10, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 8;
        grid.childAlignment = TextAnchor.MiddleCenter;
        CourseUI.Size(swatches, 46);
        foreach (string hex in Presets)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color swatch);
            Image image = CourseUI.Panel("Swatch " + hex, swatches, swatch);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => SetColor(swatch, true));
        }


        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Cancel, 24);
        CourseUI.Button("Save", buttons, "Save colour", CourseUI.ButtonKind.Primary, Save, 24);

        SetColor(color, true);
    }

    Slider Labeled(Transform parent, string label, float min, float max, Action<float> changed)
    {
        TextMeshProUGUI text = CourseUI.Text(label, parent, label, 20, CourseUI.InkMuted, FontStyles.Bold);
        CourseUI.Size(text, 24);
        Slider slider = CourseUI.SliderControl(label + " slider", parent, min, max, 0f, false, v =>
        {
            if (!setting) changed(v);
        });
        CourseUI.Size(slider, 34);
        return slider;
    }

    void FromSliders()
    {
        SetColor(Color.HSVToRGB(hue, saturation, valueSlider.value), false);
    }

    void SetColor(Color chosen, bool moveSliders)
    {
        color = chosen;
        color.a = 1f;
        preview.color = color;
        hexText.text = "#" + ColorUtility.ToHtmlStringRGB(color);
        if (moveSliders)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            hue = h;
            saturation = s;
            setting = true;
            valueSlider.value = v;
            setting = false;
        }
        if (wheel != null) wheel.Show(hue, saturation, valueSlider.value, color);
        ShowOnRunner(color);
    }

    // Shown on your own runner straight away; the others see it when it is saved
    static void ShowOnRunner(Color color)
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null) return;
        BodyHandler body = local.GetComponent<BodyHandler>();
        if (body != null) body.ApplyColor(color);
    }

    void Save()
    {
        PlayerColor.Save(color);
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.Object != null && local.Object.HasStateAuthority)
        {
            local.playerColor = PlayerColor.Pack(color);
        }
        Close();
    }

    void Cancel()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null) local.UpdateAppearance();
        Close();
    }

    void Close()
    {
        open = null;
        Destroy(gameObject);
    }
}

// A short message at the bottom of the screen that goes away by itself
public static class UIToast
{
    static GameObject current;

    public static void Show(string message, float seconds = 6f)
    {
        if (current != null) UnityEngine.Object.Destroy(current);
        GameObject go = new GameObject("Toast", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        Image card = CourseUI.CardPanel("Message", go.transform);
        card.raycastTarget = false;
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(1100, 110));
        TextMeshProUGUI text = CourseUI.Text("Text", card.transform, message, 26, CourseUI.Danger, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(text.rectTransform, 24, 24, 10, 10);
        current = go;
        UnityEngine.Object.Destroy(go, seconds);
    }
}
