using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A drop-down list under a button where several things can be ticked at once. It stays open while the player
// ticks, changes the chosen set straight away, and closes when the player clicks outside it or presses Done.
// A search field at the top is there for long lists (countries, clubs).
public class MultiSelectDropdown : MonoBehaviour
{
    const float PanelWidth = 440f;
    const float RowHeight = 54f;
    const float ListHeight = 440f;

    static MultiSelectDropdown open;

    RectTransform panel;
    RectTransform content;
    HashSet<string> selected;
    string[] options;
    Func<string, string> label;
    Action changed;
    string filter = "";
    readonly Dictionary<string, Image> boxes = new Dictionary<string, Image>();
    TextMeshProUGUI countText;

    public static bool IsOpen { get { return open != null; } }

    // options are the ids; label turns an id into the text shown (null = the id itself).
    // selected is changed in place and changed is called after every tick.
    public static void Open(RectTransform below, string heading, string[] options, HashSet<string> selected,
        bool searchable, Func<string, string> label, Action changed)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Dropdown: " + heading, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<MultiSelectDropdown>();
        open.selected = selected;
        open.options = options;
        open.label = label ?? (id => id);
        open.changed = changed;
        open.Build(below, heading, searchable);
    }

    void Build(RectTransform below, string heading, bool searchable)
    {
        Image outside = CourseUI.Panel("Outside", transform, new Color(0f, 0f, 0f, 0.001f), false);
        CourseUI.Stretch(outside.rectTransform);
        Button outsideButton = outside.gameObject.AddComponent<Button>();
        outsideButton.transition = Selectable.Transition.None;
        outsideButton.onClick.AddListener(Close);

        float height = 30f + 56f + (searchable ? 62f : 0f) + ListHeight + 70f;
        Image card = CourseUI.CardPanel("Panel", transform);
        panel = card.rectTransform;
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 8, new RectOffset(18, 18, 14, 14));
        Shadow shadow = card.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.12f, 0.24f, 0.17f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -8f);

        // Under the button, kept inside the screen
        Vector3[] corners = new Vector3[4];
        below.GetWorldCorners(corners);
        Canvas parentCanvas = below.GetComponentInParent<Canvas>();
        Camera cam = parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? parentCanvas.worldCamera : null;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        // Not from root's own size: this canvas was made this frame and is not scaled yet
        Vector2 local = ScreenSettings.ScreenToFitted(screen);
        Rect r = ScreenSettings.FittedRect();
        float x = Mathf.Clamp(local.x, r.xMin + 10f, r.xMax - PanelWidth - 10f);
        float top = Mathf.Max(local.y - 6f, r.yMin + height + 10f);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(PanelWidth, height);
        panel.anchoredPosition = new Vector2(x, top);

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, heading, 30, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 44);

        if (searchable)
        {
            TMP_InputField search = CourseUI.Input("Search", card.transform, "Search...", 40, text =>
            {
                filter = text == null ? "" : text.Trim();
                Fill();
            });
            CourseUI.Size(search, 54);
        }

        ScrollRect scroll;
        content = CourseUI.ScrollList("List", card.transform, out scroll);
        CourseUI.Size(scroll, ListHeight);
        CourseUI.Column(content.gameObject, 4, new RectOffset(0, 0, 0, 0));
        scroll.scrollSensitivity = 40f;

        Image footer = CourseUI.Panel("Footer", card.transform, Color.clear, false);
        CourseUI.Size(footer, 54);
        CourseUI.Row(footer.gameObject, 12, new RectOffset(0, 0, 0, 0));
        countText = CourseUI.Text("Count", footer.transform, "", 22, CourseUI.InkMuted);
        countText.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(countText, 54, -1, 1);
        Button clear = CourseUI.Button("Clear", footer.transform, "Clear", CourseUI.ButtonKind.Light, () =>
        {
            selected.Clear();
            Fill();
            changed?.Invoke();
        }, 22);
        CourseUI.Size(clear, 54, 110);
        Button done = CourseUI.Button("Done", footer.transform, "Done", CourseUI.ButtonKind.Primary, Close, 22);
        CourseUI.Size(done, 54, 110);

        Fill();
    }

    // The rows that match the search
    void Fill()
    {
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        boxes.Clear();
        foreach (string option in options)
        {
            string text = label(option);
            if (filter.Length > 0 && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            AddRow(option, text);
        }
        if (content.childCount == 0)
        {
            TextMeshProUGUI none = CourseUI.Text("None", content, "Nothing found", 24, CourseUI.InkMuted);
            CourseUI.Size(none, RowHeight);
        }
        PaintCount();
    }

    void AddRow(string id, string text)
    {
        Image row = CourseUI.Panel("Row " + id, content, Color.white, false);
        CourseUI.Size(row, RowHeight);
        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = row;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.001f);
        colors.highlightedColor = new Color(0.91f, 0.45f, 0.16f, 0.16f);
        colors.pressedColor = new Color(0.91f, 0.45f, 0.16f, 0.3f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        CourseUI.Row(row.gameObject, 14, new RectOffset(10, 10, 6, 6));

        Image box = CourseUI.Panel("Box", row.transform, CourseUI.FieldBorder, false);
        CourseUI.Size(box, 34, 34);
        box.raycastTarget = false;
        boxes[id] = box;
        Image tick = CourseUI.Panel("Tick", box.transform, Color.white, false);
        CourseUI.Stretch(tick.rectTransform, 4, 4, 4, 4);
        tick.raycastTarget = false;
        tick.gameObject.name = "Tick";

        TextMeshProUGUI name = CourseUI.Text("Text", row.transform, text, 26, CourseUI.Ink);
        name.alignment = TextAlignmentOptions.MidlineLeft;
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.raycastTarget = false;
        CourseUI.Size(name, 42, -1, 1);

        PaintBox(id);
        button.onClick.AddListener(() =>
        {
            if (!selected.Add(id)) selected.Remove(id);
            PaintBox(id);
            PaintCount();
            changed?.Invoke();
        });
    }

    void PaintBox(string id)
    {
        Image box;
        if (!boxes.TryGetValue(id, out box)) return;
        bool on = selected.Contains(id);
        box.color = on ? CourseUI.Forest : CourseUI.FieldBorder;
        Transform tick = box.transform.Find("Tick");
        if (tick != null) tick.GetComponent<Image>().color = on ? CourseUI.Forest : Color.white;
        // A ticked box is solid green, an empty one white inside a border
        Image inner = tick != null ? tick.GetComponent<Image>() : null;
        if (inner != null) inner.enabled = !on;
    }

    void PaintCount()
    {
        if (countText != null) countText.text = selected.Count == 0 ? "Nothing ticked" : selected.Count + " ticked";
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
