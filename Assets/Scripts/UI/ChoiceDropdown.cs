using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A drop-down list under a button where one thing is picked. The list closes when something is picked or
// when the player clicks outside it. (MultiSelectDropdown is the one where several things can be ticked.)
public class ChoiceDropdown : MonoBehaviour
{
    const float RowHeight = 56f;

    static ChoiceDropdown open;

    public static bool IsOpen { get { return open != null; } }

    // labels are the texts shown; selected is the index of the current one; chosen gets the index picked.
    public static void Open(RectTransform below, string[] labels, int selected, Action<int> chosen)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Dropdown", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<ChoiceDropdown>();
        open.Build(below, labels, selected, chosen);
    }

    void Build(RectTransform below, string[] labels, int selected, Action<int> chosen)
    {
        Image outside = CourseUI.Panel("Outside", transform, new Color(0f, 0f, 0f, 0.001f), false);
        CourseUI.Stretch(outside.rectTransform);
        Button outsideButton = outside.gameObject.AddComponent<Button>();
        outsideButton.transition = Selectable.Transition.None;
        outsideButton.onClick.AddListener(Close);

        // As wide as the button (at least 260), under it, kept inside the screen
        Vector3[] corners = new Vector3[4];
        below.GetWorldCorners(corners);
        Canvas parentCanvas = below.GetComponentInParent<Canvas>();
        Camera cam = parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? parentCanvas.worldCamera : null;
        // Not from root's own size: this canvas was made this frame and is not scaled yet, so on a screen bigger
        // than 1920x1080 the list landed outside the screen (the course builder's Type button, at the right edge)
        Vector2 bottomLeft = ScreenSettings.ScreenToFitted(RectTransformUtility.WorldToScreenPoint(cam, corners[0]));
        Vector2 bottomRight = ScreenSettings.ScreenToFitted(RectTransformUtility.WorldToScreenPoint(cam, corners[3]));
        float width = Mathf.Max(260f, bottomRight.x - bottomLeft.x);
        float height = 12f + labels.Length * (RowHeight + 4f) + 8f;
        Rect r = ScreenSettings.FittedRect();

        Image card = CourseUI.CardPanel("Panel", transform);
        RectTransform panel = card.rectTransform;
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 4, new RectOffset(10, 10, 10, 10));
        Shadow shadow = card.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.12f, 0.24f, 0.17f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -8f);
        float x = Mathf.Clamp(bottomLeft.x, r.xMin + 10f, r.xMax - width - 10f);
        float top = Mathf.Max(bottomLeft.y - 6f, r.yMin + height + 10f);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0f, 1f);
        panel.sizeDelta = new Vector2(width, height);
        panel.anchoredPosition = new Vector2(x, top);

        float widest = 0f;
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            bool on = i == selected;
            Image row = CourseUI.Panel("Row " + labels[i], card.transform, on ? CourseUI.Forest : Color.white, false);
            CourseUI.Size(row, RowHeight);
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;
            ColorBlock colors = button.colors;
            colors.normalColor = on ? CourseUI.Forest : new Color(1f, 1f, 1f, 0.001f);
            colors.highlightedColor = on ? CourseUI.Forest : new Color(0.91f, 0.45f, 0.16f, 0.16f);
            colors.pressedColor = new Color(0.91f, 0.45f, 0.16f, 0.3f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            CourseUI.Row(row.gameObject, 10, new RectOffset(18, 18, 6, 6));
            TextMeshProUGUI text = CourseUI.Text("Text", row.transform, labels[i], 26, on ? Color.white : CourseUI.Ink, on ? FontStyles.Bold : FontStyles.Normal);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            CourseUI.Size(text, 42, -1, 1);
            widest = Mathf.Max(widest, text.GetPreferredValues(labels[i], float.PositiveInfinity, 42f).x);
            button.onClick.AddListener(() =>
            {
                Close();
                chosen?.Invoke(index);
            });
        }

        // Wide enough for the longest text (2026-11-20: "Copy a leg" cut its texts off), kept inside the screen
        float needed = Mathf.Min(widest + 2f * 18f + 2f * 10f + 8f, r.width - 20f);
        if (needed > width)
        {
            width = needed;
            x = Mathf.Clamp(bottomLeft.x, r.xMin + 10f, r.xMax - width - 10f);
            panel.sizeDelta = new Vector2(width, height);
            panel.anchoredPosition = new Vector2(x, top);
        }
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
