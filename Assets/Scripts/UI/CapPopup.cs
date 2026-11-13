using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The host's window for "Max average overall" (2026-11-13), opened from the lobby's runner pool with Your runners
// or Any runner. With it on, a team's runners may have at most this overall on average (their overall together at
// most max * legs), so the strong runners must be paired with weaker ones. In an individual race it is the most
// the one runner may have. The last value is remembered between games.
public class CapPopup : MonoBehaviour
{
    const string CapKey = "Lobby.MaxAverageOverall";

    static CapPopup open;

    int cap;
    bool on;
    TextMeshProUGUI label;
    Slider slider;
    Button onButton;
    Button offButton;

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
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 560));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 14, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Max average overall", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform,
            "In a relay the team's runners may have at most this overall on average, so a strong runner on one leg needs a weaker one on another. "
            + "In an individual race it is the most the runner may have.", 21, CourseUI.InkMuted);
        hint.enableWordWrapping = true;
        CourseUI.Size(hint, 84);

        RectTransform toggles = CourseUI.Rect("On or off", card.transform);
        CourseUI.Row(toggles.gameObject, 16, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(toggles, 56);
        offButton = CourseUI.Button("Off", toggles, "Off: no limit", CourseUI.ButtonKind.Light, () => { on = false; Refresh(); }, 22);
        CourseUI.Size(offButton, -1, -1, 1);
        onButton = CourseUI.Button("On", toggles, "On: max average", CourseUI.ButtonKind.Light, () => { on = true; Refresh(); }, 22);
        CourseUI.Size(onButton, -1, -1, 1);

        label = CourseUI.Text("Value", card.transform, "", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(label, 40);
        slider = CourseUI.SliderControl("Max", card.transform, GameModes.MinCap, GameModes.MaxCap, cap, true, v =>
        {
            cap = Mathf.RoundToInt(v);
            on = true;
            Refresh();
        });
        CourseUI.Size(slider, 44);

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 16, new RectOffset(0, 0, 10, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 68);
        Button cancel = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Close, 24);
        CourseUI.Size(cancel, -1, -1, 1);
        Button use = CourseUI.Button("Use", buttons, "Use", CourseUI.ButtonKind.Primary, () =>
        {
            PlayerPrefs.SetInt(CapKey, cap);
            PlayerPrefs.Save();
            chosen?.Invoke(GameModes.WithCap(current, on ? cap : 0));
            Close();
        }, 24);
        CourseUI.Size(use, -1, -1, 1);

        Refresh();
    }

    void Refresh()
    {
        label.text = on ? "Max average overall  <color=#E8742A>" + cap + "</color>" : "<color=#5B7563>No limit</color>";
        CourseUI.SetButtonColor(onButton, on ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
        CourseUI.SetButtonColor(offButton, on ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
