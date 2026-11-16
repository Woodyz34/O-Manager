using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The host's window for "Max average overall" (2026-11-13), opened from the lobby's runner pool with Your runners
// or Any runner. With it on, a team's runners may have at most this overall on average (their overall together at
// most max * legs), so the strong runners must be paired with weaker ones. Relays only since 2026-11-16 (the button
// shows only with a relay course). The last value is remembered between games.
public class CapPopup : MonoBehaviour
{
    const string CapKey = "Lobby.MaxAverageOverall";

    static CapPopup open;

    int cap;
    bool on;
    TextMeshProUGUI label;
    Slider slider;

    public static void Open(string current, Action<string> chosen)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Max average overall", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 201;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<CapPopup>();
        int now = GameModes.Cap(current);
        open.on = now > 0;
        open.cap = now > 0 ? now : Mathf.Clamp(PlayerPrefs.GetInt(CapKey, GameModes.DefaultCap), GameModes.MinCap, GameModes.MaxCap);
        open.Build(current, chosen);
    }

    void Build(string current, Action<string> chosen)
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 420));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 14, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Max average overall", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        // Plainer since 2026-11-16: set the value, Use. No limit takes it away.
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform,
            "The average overall of the runners in a relay team may be at most this.", 22, CourseUI.InkMuted);
        hint.enableWordWrapping = true;
        CourseUI.Size(hint, 60);

        label = CourseUI.Text("Value", card.transform, "", 56, CourseUI.Accent, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(label, 72);
        slider = CourseUI.SliderControl("Max", card.transform, GameModes.MinCap, GameModes.MaxCap, cap, true, v =>
        {
            cap = Mathf.RoundToInt(v);
            Refresh();
        });
        CourseUI.Size(slider, 44);

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 16, new RectOffset(0, 0, 18, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 76);
        Button none = CourseUI.Button("No limit", buttons, on ? "No limit" : "Cancel", CourseUI.ButtonKind.Light, () =>
        {
            if (on) chosen?.Invoke(GameModes.WithCap(current, 0));
            Close();
        }, 24);
        CourseUI.Size(none, -1, -1, 1);
        Button use = CourseUI.Button("Use", buttons, "Use", CourseUI.ButtonKind.Primary, () =>
        {
            PlayerPrefs.SetInt(CapKey, cap);
            PlayerPrefs.Save();
            chosen?.Invoke(GameModes.WithCap(current, cap));
            Close();
        }, 24);
        CourseUI.Size(use, -1, -1, 1);

        Refresh();
    }

    void Refresh()
    {
        label.text = cap.ToString();
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
