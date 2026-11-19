using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The host's "Lobby settings" window (2026-11-19): opened from the gear button in the lobby's top right corner, and
// during the race from the Esc menu. Every change counts at once for everyone (HostSettings), also during the race.
// The other players can open it to see the settings, but cannot change them.
public class HostSettingsPopup : MonoBehaviour
{
    static HostSettingsPopup open;

    HostSettings.Values values;
    bool canChange;
    readonly System.Collections.Generic.List<Action> refreshers = new System.Collections.Generic.List<Action>();

    public static bool IsOpen => open != null;

    public static void Open()
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Lobby settings", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 260;   // above the Esc menu it can be opened from
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<HostSettingsPopup>();
        open.canChange = NetworkPlayer.Local == null || CompetitionFlow.LocalIsHost();
        open.values = open.canChange ? HostSettings.Mine : HostSettings.Current;
        open.Build();
    }

    void Build()
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 980));
        card.gameObject.AddComponent<ClickCatcher>();

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Lobby settings", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(820, 52));
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, canChange
            ? "Only the host changes these. They count for everyone at once, also during the race."
            : "The host's settings. Only the host can change them.", 21, CourseUI.InkMuted);
        CourseUI.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(820, 30));

        RectTransform list = CourseUI.ScrollList("Settings", card.transform, out ScrollRect scroll);
        RectTransform viewport = scroll.GetComponent<RectTransform>();
        CourseUI.Place(viewport, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -120), new Vector2(840, 740));
        CourseUI.Column(list.gameObject, 10, new RectOffset(16, 16, 6, 12));

        AddHeading(list, "Speed");
        AddSlider(list, "Race speed on the map", "How fast the race runs while you follow it on the map.",
            HostSettings.MinRaceSpeed, HostSettings.MaxRaceSpeed, () => values.raceSpeedTenths, v => values.raceSpeedTenths = v, Times);
        AddSlider(list, "Running speed on TV", "How fast the runners run while a TV control is on, times real running pace.",
            HostSettings.MinTvSpeed, HostSettings.MaxTvSpeed, () => values.tvSpeedTenths, v => values.tvSpeedTenths = v, Times);

        AddHeading(list, "TV controls in relays");
        AddSlider(list, "Length of every TV shot", "Shorter shots make the relay go faster.",
            HostSettings.MinShotPercent / 10, HostSettings.MaxShotPercent / 10, () => values.tvShotPercent / 10, v => values.tvShotPercent = v * 10, v => (v * 10) + " %");
        AddSlider(list, "Runners shown at every TV control", "Only the first ones to get there are filmed.",
            1, HostSettings.MaxShown + 1, () => values.tvControlShown == 0 ? HostSettings.MaxShown + 1 : values.tvControlShown,
            v => values.tvControlShown = v > HostSettings.MaxShown ? 0 : v, v => v > HostSettings.MaxShown ? "All" : "Top " + v);
        AddToggle(list, "Start of every leg on TV", "Off: only the start of the first leg.", () => values.tvStartEveryLeg, v => values.tvStartEveryLeg = v);
        AddSlider(list, "Runners shown at the start", "Only the first ones off are filmed.",
            1, HostSettings.MaxShown + 1, () => values.tvStartShown == 0 ? HostSettings.MaxShown + 1 : values.tvStartShown,
            v => values.tvStartShown = v > HostSettings.MaxShown ? 0 : v, v => v > HostSettings.MaxShown ? "All" : "Top " + v);
        AddToggle(list, "Finish of every leg on TV", "Off: only the finish of the last leg.", () => values.tvFinishEveryLeg, v => values.tvFinishEveryLeg = v);
        AddSlider(list, "Runners shown at the finish", "Only the first ones in are filmed.",
            1, HostSettings.MaxShown + 1, () => values.tvFinishShown == 0 ? HostSettings.MaxShown + 1 : values.tvFinishShown,
            v => values.tvFinishShown = v > HostSettings.MaxShown ? 0 : v, v => v > HostSettings.MaxShown ? "All" : "Top " + v);

        AddHeading(list, "Game modes");
        AddSlider(list, "Bears per player and leg", "Bears mode: how many bears everyone can hide on every leg.",
            1, HostSettings.MaxPerLeg, () => values.bearsPerLeg, v => values.bearsPerLeg = v, v => v.ToString());
        AddSlider(list, "Control moves per player and leg", "Move a control mode: how many controls everyone can move on every leg.",
            1, HostSettings.MaxPerLeg, () => values.movesPerLeg, v => values.movesPerLeg = v, v => v.ToString());

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Place(buttons, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(820, 64));
        CourseUI.Row(buttons.gameObject, 16, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        if (canChange)
        {
            Button reset = CourseUI.Button("Defaults", buttons, "Defaults", CourseUI.ButtonKind.Light, () =>
            {
                values = HostSettings.Defaults;
                Save();
                foreach (Action refresh in refreshers) refresh();
            }, 24);
            CourseUI.Size(reset, -1, -1, 1);
        }
        Button close = CourseUI.Button("Close", buttons, "Close", CourseUI.ButtonKind.Primary, Close, 24);
        CourseUI.Size(close, -1, -1, 1);
    }

    static string Times(int tenths) => (tenths / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x";

    void Save()
    {
        if (!canChange) return;
        HostSettings.Mine = values;
    }

    void AddHeading(Transform parent, string text)
    {
        TextMeshProUGUI heading = CourseUI.Text("Heading", parent, text, 28, CourseUI.Forest, FontStyles.Bold);
        CourseUI.Size(heading, 44);
    }

    // A row: name and explanation on the left, the value on the right, the slider under them
    void AddSlider(Transform parent, string name, string explanation, int min, int max, Func<int> get, Action<int> set, Func<int, string> show)
    {
        RectTransform row = Row(parent, name, explanation, 128, out TextMeshProUGUI value);
        Slider slider = CourseUI.SliderControl("Slider", row, min, max, get(), true, v =>
        {
            set(Mathf.RoundToInt(v));
            value.text = show(Mathf.RoundToInt(v));
            Save();
        });
        CourseUI.Place(slider.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 8), new Vector2(760, 36));
        slider.interactable = canChange;
        value.text = show(get());
        refreshers.Add(() =>
        {
            slider.SetValueWithoutNotify(get());
            value.text = show(get());
        });
    }

    void AddToggle(Transform parent, string name, string explanation, Func<bool> get, Action<bool> set)
    {
        RectTransform row = Row(parent, name, explanation, 84, out TextMeshProUGUI value);
        value.gameObject.SetActive(false);
        Button button = null;
        button = CourseUI.Button("Toggle", row, "", CourseUI.ButtonKind.Secondary, () =>
        {
            if (!canChange) return;
            set(!get());
            Save();
            Show(button, get());
        }, 22);
        CourseUI.Place(button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8, 0), new Vector2(130, 52));
        button.interactable = canChange;
        Show(button, get());
        refreshers.Add(() => Show(button, get()));
    }

    static void Show(Button button, bool on)
    {
        CourseUI.SetButtonText(button, on ? "On" : "Off");
        CourseUI.SetButtonColor(button, on ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
    }

    RectTransform Row(Transform parent, string name, string explanation, float height, out TextMeshProUGUI value)
    {
        Image tile = CourseUI.Panel(name, parent, CourseUI.Soft);
        CourseUI.Size(tile, height);
        RectTransform row = tile.rectTransform;
        TextMeshProUGUI label = CourseUI.Text("Name", row, "<b>" + name + "</b>\n<size=19><color=#5B7563>" + explanation + "</color></size>", 24, CourseUI.Ink);
        label.richText = true;
        label.enableWordWrapping = true;
        CourseUI.Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20, -8), new Vector2(620, 66));
        value = CourseUI.Text("Value", row, "", 30, CourseUI.Accent, FontStyles.Bold, TextAlignmentOptions.Right);
        CourseUI.Place(value.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20, -10), new Vector2(160, 44));
        return row;
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
