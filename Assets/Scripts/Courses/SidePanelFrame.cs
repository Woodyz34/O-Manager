using System;
using UnityEngine;
using UnityEngine.UI;

// A side panel of the course builder (2026-11-20): it runs from the top of the screen to the bottom at one edge.
// "-" and "+" make it smaller or bigger (the leaderboard in the race works the same way), and the arrow slides it
// into the edge of the screen; a small tab at the edge brings it back. The choice is remembered.
public class SidePanelFrame : MonoBehaviour
{
    public const float MinScale = 0.6f, MaxScale = 1.3f, Step = 0.1f;
    const float Margin = 24f;

    RectTransform panel;
    RectTransform parent;
    bool right;
    string key;
    float width;
    float scale;
    bool hidden;
    float lastParentHeight = -1f;
    Button hideButton;
    GameObject showTab;

    // Called after the panel got another size or was hidden or shown (the map makes room for it)
    public Action changed;

    // The room the panel takes at its edge of the screen, in canvas units (0 while it is hidden)
    public float VisibleWidth => panel == null || hidden ? 0f : width * scale + Margin;

    // panel: the card, made with its final width. controls: a row inside the card that gets the -, + and arrow buttons.
    public static SidePanelFrame Attach(RectTransform panel, bool right, string key, float defaultScale, Transform controls)
    {
        SidePanelFrame frame = panel.gameObject.AddComponent<SidePanelFrame>();
        frame.Setup(panel, right, key, defaultScale, controls);
        return frame;
    }

    void Setup(RectTransform target, bool onRight, string prefsKey, float defaultScale, Transform controls)
    {
        panel = target;
        parent = (RectTransform)panel.parent;
        right = onRight;
        key = prefsKey;
        width = panel.sizeDelta.x;
        float side = right ? 1f : 0f;
        panel.anchorMin = new Vector2(side, 1f);
        panel.anchorMax = new Vector2(side, 1f);
        panel.pivot = new Vector2(side, 1f);
        scale = Mathf.Clamp(PlayerPrefs.GetFloat(key + ".Scale", defaultScale), MinScale, MaxScale);
        hidden = PlayerPrefs.GetInt(key + ".Hidden", 0) == 1;

        Button smaller = CourseUI.Button("Smaller", controls, "-", CourseUI.ButtonKind.Light, () => Resize(-Step), 24);
        Fixed(smaller);
        Button bigger = CourseUI.Button("Bigger", controls, "+", CourseUI.ButtonKind.Light, () => Resize(Step), 24);
        Fixed(bigger);
        hideButton = CourseUI.Button("Hide", controls, right ? ">" : "<", CourseUI.ButtonKind.Secondary, Toggle, 22);
        Fixed(hideButton);
        HoverTip.Add(smaller, "Make this panel smaller, so the map gets more room.");
        HoverTip.Add(bigger, "Make this panel bigger.");
        HoverTip.Add(hideButton, "Slide this panel into the edge of the screen. The tab at the edge brings it back.");

        // While hidden only this tab shows, in the middle of the screen edge
        Image tab = CourseUI.CardPanel("Show " + panel.name, parent);
        CourseUI.Place(tab.rectTransform, new Vector2(side, 0.5f), new Vector2(side, 0.5f), Vector2.zero, new Vector2(52f, 72f));
        Button show = CourseUI.Button("Show", tab.transform, right ? "<" : ">", CourseUI.ButtonKind.Secondary, Toggle, 22);
        CourseUI.Place((RectTransform)show.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 52f));
        HoverTip.Add(show, "Show the panel again.");
        showTab = tab.gameObject;
        Apply();
    }

    static void Fixed(Button button)
    {
        LayoutElement size = button.gameObject.AddComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = 38f;
        size.minHeight = size.preferredHeight = 38f;
        size.flexibleWidth = 0f;
    }

    public void Toggle()
    {
        hidden = !hidden;
        Save();
        Apply();
    }

    void Resize(float change)
    {
        scale = Mathf.Clamp(Mathf.Round((scale + change) * 10f) / 10f, MinScale, MaxScale);
        Save();
        Apply();
    }

    void Save()
    {
        PlayerPrefs.SetFloat(key + ".Scale", scale);
        PlayerPrefs.SetInt(key + ".Hidden", hidden ? 1 : 0);
        PlayerPrefs.Save();
    }

    // The canvas gets its real size a frame after it is made (CanvasScaler), so the height is fitted again when it changes
    void LateUpdate()
    {
        if (parent != null && !Mathf.Approximately(parent.rect.height, lastParentHeight)) Apply();
    }

    void Apply()
    {
        if (panel == null) return;
        float parentHeight = parent != null ? parent.rect.height : 1080f;
        lastParentHeight = parentHeight;
        // Scaled down, the card is made taller so it still reaches from the top margin to the bottom margin
        panel.localScale = new Vector3(scale, scale, 1f);
        panel.sizeDelta = new Vector2(width, Mathf.Max(200f, (parentHeight - 2f * Margin) / scale));
        float x = hidden ? (right ? width * scale + Margin : -(width * scale + Margin)) : (right ? -Margin : Margin);
        panel.anchoredPosition = new Vector2(x, -Margin);
        if (showTab != null)
        {
            showTab.SetActive(hidden);
            showTab.transform.SetAsLastSibling();
        }
        changed?.Invoke();
    }
}
