using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Small helpers that build UI in code in the game's style: light cream cards, dark green text, orange accent.
public static class CourseUI
{
    public static readonly Color Card = Hex("F6F0DC", 0.96f);
    public static readonly Color CardSolid = Hex("F3EDD9");
    public static readonly Color Ink = Hex("1F3D2B");
    public static readonly Color InkMuted = Hex("5B7563");
    public static readonly Color Accent = Hex("E8742A");
    public static readonly Color Forest = Hex("3F7D54");
    public static readonly Color Soft = Hex("FFFFFF", 0.78f);
    public static readonly Color FieldBorder = Hex("C7BC98");
    public static readonly Color Danger = Hex("C0392B");

    static CourseUISkin skin;

    public static CourseUISkin Skin
    {
        get
        {
            if (skin == null)
            {
                skin = Resources.Load<CourseUISkin>("UI/CourseUISkin");
            }
            return skin;
        }
    }

    public static Color Hex(string hex, float alpha = 1f)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        color.a = alpha;
        return color;
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public static Image Panel(string name, Transform parent, Color color, bool rounded = true)
    {
        RectTransform rect = Rect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        if (rounded && Skin != null && Skin.rounded != null)
        {
            image.sprite = Skin.rounded;
            image.type = Image.Type.Sliced;
        }
        return image;
    }

    public static Image CardPanel(string name, Transform parent)
    {
        Image image = Panel(name, parent, Card);
        Shadow shadow = image.gameObject.AddComponent<Shadow>();
        shadow.effectColor = Hex("1F3D2B", 0.25f);
        shadow.effectDistance = new Vector2(0, -6);
        return image;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
                                       FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        RectTransform rect = Rect(name, parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (Skin != null && Skin.font != null) label.font = Skin.font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.fontStyle = style;
        label.alignment = align;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Truncate; // the game font has no ellipsis character
        label.raycastTarget = false;
        return label;
    }

    public enum ButtonKind { Primary, Secondary, Light, Danger }

    public static Button Button(string name, Transform parent, string text, ButtonKind kind, UnityAction onClick, float fontSize = 26)
    {
        Color background = kind == ButtonKind.Primary ? Accent : kind == ButtonKind.Secondary ? Forest : kind == ButtonKind.Danger ? Danger : Soft;
        Color textColor = kind == ButtonKind.Light ? Ink : Color.white;
        Image image = Panel(name, parent, background);
        if (Skin != null && Skin.pill != null)
        {
            image.sprite = Skin.pill;
        }
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image; // only set automatically when a Button is added in the editor
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.93f, 0.93f, 0.93f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.6f);
        button.colors = colors;
        if (onClick != null) button.onClick.AddListener(onClick);
        TextMeshProUGUI label = Text("Text", image.transform, text, fontSize, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 12, 12, 0, 0);
        label.enableWordWrapping = false;
        return button;
    }

    public static void SetButtonText(Button button, string text)
    {
        button.GetComponentInChildren<TextMeshProUGUI>().text = text;
    }

    public static void SetButtonColor(Button button, ButtonKind kind)
    {
        Color background = kind == ButtonKind.Primary ? Accent : kind == ButtonKind.Secondary ? Forest : kind == ButtonKind.Danger ? Danger : Soft;
        button.GetComponent<Image>().color = background;
        button.GetComponentInChildren<TextMeshProUGUI>().color = kind == ButtonKind.Light ? Ink : Color.white;
    }

    // Buttons with a drawn sign instead of a character (2026-11-24): "+", "<" and ">" were cut away in the small round
    // buttons of the leaderboards (the label is narrower than the sign), so only an empty circle showed. The signs
    // are made of plain bars, so they always show, in the label's colour.
    public enum Icon { Minus, Plus, Arrow }

    public static Button IconButton(string name, Transform parent, Icon icon, ButtonKind kind, UnityAction onClick, float iconSize = 18f)
    {
        Button button = Button(name, parent, "", kind, onClick);
        RectTransform box = Rect("Icon", button.transform);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(iconSize, iconSize);
        box.anchoredPosition = Vector2.zero;
        Color color = kind == ButtonKind.Light ? Ink : Color.white;
        float thick = Mathf.Max(2.5f, iconSize * 0.2f);
        switch (icon)
        {
            case Icon.Minus:
                Bar(box, color, Vector2.zero, new Vector2(iconSize, thick), 0f, new Vector2(0.5f, 0.5f));
                break;
            case Icon.Plus:
                Bar(box, color, Vector2.zero, new Vector2(iconSize, thick), 0f, new Vector2(0.5f, 0.5f));
                Bar(box, color, Vector2.zero, new Vector2(thick, iconSize), 0f, new Vector2(0.5f, 0.5f));
                break;
            case Icon.Arrow:
                // Points left; PointIcon turns it round
                float tip = -iconSize * 0.45f;
                Bar(box, color, new Vector2(tip + thick * 0.3f, 0f), new Vector2(iconSize * 0.9f - thick * 0.3f, thick), 0f, new Vector2(0f, 0.5f));
                Bar(box, color, new Vector2(tip, 0f), new Vector2(iconSize * 0.55f, thick), 42f, new Vector2(0f, 0.5f));
                Bar(box, color, new Vector2(tip, 0f), new Vector2(iconSize * 0.55f, thick), -42f, new Vector2(0f, 0.5f));
                break;
        }
        return button;
    }

    // Turns an arrow button's sign to the right (true) or the left (false)
    public static void PointIcon(Button button, bool right)
    {
        if (button == null) return;
        Transform box = button.transform.Find("Icon");
        if (box != null) box.localScale = new Vector3(right ? -1f : 1f, 1f, 1f);
    }

    static void Bar(RectTransform box, Color color, Vector2 at, Vector2 size, float angle, Vector2 pivot)
    {
        RectTransform bar = Rect("Bar", box);
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0.5f);
        bar.pivot = pivot;
        bar.sizeDelta = size;
        bar.anchoredPosition = at;
        bar.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image image = bar.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    public static TMP_InputField Input(string name, Transform parent, string placeholder, int characterLimit, Action<string> onChanged)
    {
        Image frame = Panel(name, parent, Color.white);
        Outline outline = frame.gameObject.AddComponent<Outline>();
        outline.effectColor = FieldBorder;
        outline.effectDistance = new Vector2(2, -2);

        RectTransform area = Rect("Text Area", frame.transform);
        Stretch(area, 16, 16, 6, 6);
        area.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI hint = Text("Placeholder", area, placeholder, 24, InkMuted, FontStyles.Italic);
        Stretch(hint.rectTransform);
        hint.alignment = TextAlignmentOptions.MidlineLeft;
        hint.enableWordWrapping = false;

        TextMeshProUGUI value = Text("Text", area, "", 24, Ink);
        Stretch(value.rectTransform);
        value.alignment = TextAlignmentOptions.MidlineLeft;
        value.enableWordWrapping = false;

        // The field is set up while switched off: TMP_InputField makes its caret when it is switched on, and only
        // if it already has its text by then (without this the text cursor never shows)
        bool wasActive = frame.gameObject.activeSelf;
        frame.gameObject.SetActive(false);
        TMP_InputField field = frame.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = value;
        field.placeholder = hint;
        field.characterLimit = characterLimit;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.caretWidth = 2;
        field.customCaretColor = true;
        field.caretColor = Ink;
        field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        if (Skin != null && Skin.font != null) field.fontAsset = Skin.font;
        if (onChanged != null) field.onValueChanged.AddListener(v => onChanged(v));
        frame.gameObject.SetActive(wasActive);
        return field;
    }

    public static VerticalLayoutGroup Column(GameObject go, float spacing, RectOffset padding, bool expandWidth = true)
    {
        VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = expandWidth;
        layout.childForceExpandHeight = false;
        return layout;
    }

    public static HorizontalLayoutGroup Row(GameObject go, float spacing, RectOffset padding)
    {
        HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        return layout;
    }

    public static LayoutElement Size(Component component, float preferredHeight = -1, float preferredWidth = -1, float flexibleWidth = -1)
    {
        LayoutElement element = component.gameObject.GetComponent<LayoutElement>();
        if (element == null) element = component.gameObject.AddComponent<LayoutElement>();
        // A fixed height also means no share of spare room (a row's layout group would otherwise ask for it)
        if (preferredHeight >= 0) { element.preferredHeight = preferredHeight; element.minHeight = preferredHeight; element.flexibleHeight = 0; }
        if (preferredWidth >= 0) { element.preferredWidth = preferredWidth; element.minWidth = preferredWidth; }
        if (flexibleWidth >= 0) element.flexibleWidth = flexibleWidth;
        return element;
    }

    // A horizontal slider with a track, an orange fill and a round handle
    public static Slider SliderControl(string name, Transform parent, float min, float max, float value, bool wholeNumbers, Action<float> onChanged)
    {
        RectTransform root = Rect(name, parent);
        Slider slider = root.gameObject.AddComponent<Slider>();

        Image track = Panel("Background", root, FieldBorder);
        if (Skin != null && Skin.pill != null) track.sprite = Skin.pill;
        track.rectTransform.anchorMin = new Vector2(0, 0.5f);
        track.rectTransform.anchorMax = new Vector2(1, 0.5f);
        track.rectTransform.sizeDelta = new Vector2(0, 12);

        RectTransform fillArea = Rect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0, 0.5f);
        fillArea.anchorMax = new Vector2(1, 0.5f);
        fillArea.sizeDelta = new Vector2(-16, 12);
        Image fill = Panel("Fill", fillArea, Accent);
        if (Skin != null && Skin.pill != null) fill.sprite = Skin.pill;
        fill.rectTransform.sizeDelta = new Vector2(10, 0);

        RectTransform handleArea = Rect("Handle Slide Area", root);
        Stretch(handleArea, 14, 14, 0, 0);
        Image handle = Panel("Handle", handleArea, Color.white);
        if (Skin != null && Skin.pill != null) handle.sprite = Skin.pill;
        handle.rectTransform.sizeDelta = new Vector2(32, -8); // 32 x 32 on a 40 high slider
        Outline outline = handle.gameObject.AddComponent<Outline>();
        outline.effectColor = Accent;
        outline.effectDistance = new Vector2(2, -2);

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = wholeNumbers;
        slider.value = value;
        if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
        return slider;
    }

    // A vertical scrolling list. Returns the content object to add rows to.
    public static RectTransform ScrollList(string name, Transform parent, out ScrollRect scroll)
    {
        Image viewportImage = Panel(name, parent, new Color(1, 1, 1, 0.001f), false);
        scroll = viewportImage.gameObject.AddComponent<ScrollRect>();
        viewportImage.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Rect("Content", viewportImage.transform);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        scroll.viewport = (RectTransform)viewportImage.transform;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }

    // Turns a full-screen menu page into a pop-up card with the lobby showing around it: the page gets a margin,
    // rounded corners and a shadow, and its scrolling list is fitted between listTop and the bottom of the card
    public static void MakePopup(GameObject page, RectTransform list, float listTop)
    {
        RectTransform rect = (RectTransform)page.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(70f, 50f);
        rect.offsetMax = new Vector2(-70f, -50f);
        Image image = page.GetComponent<Image>();
        if (image != null)
        {
            if (Skin != null && Skin.rounded != null)
            {
                image.sprite = Skin.rounded;
                image.type = Image.Type.Sliced;
            }
            if (page.GetComponent<Shadow>() == null)
            {
                Shadow shadow = page.AddComponent<Shadow>();
                shadow.effectColor = Hex("1F3D2B", 0.45f);
                shadow.effectDistance = new Vector2(0, -10);
            }
        }
        if (list != null)
        {
            float height = 1080f - 100f - listTop - 24f;
            list.sizeDelta = new Vector2(1920f - 140f - 40f, height);
            list.anchoredPosition = new Vector2(0f, -(listTop + height / 2f));
        }
    }
}
