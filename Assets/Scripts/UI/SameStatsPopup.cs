using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The host's window for the runner pool "Same stats": two sliders for the speed and technique that every runner in
// the competition gets. When the lobby's course is a relay, a row of leg buttons lets the host set other values for
// each leg (2026-11-11). The last values are remembered between games.
public class SameStatsPopup : MonoBehaviour
{
    const string SpeedKey = "Lobby.SameStatsSpeed";
    const string TechniqueKey = "Lobby.SameStatsTechnique";
    const string LegsKey = "Lobby.SameStatsLegs";
    const int DefaultValue = 85;

    static SameStatsPopup open;

    // One speed/technique pair per leg (just one outside relays)
    readonly List<Vector2Int> legs = new List<Vector2Int>();
    int leg;
    TextMeshProUGUI speedLabel;
    TextMeshProUGUI techniqueLabel;
    Slider speedSlider;
    Slider techniqueSlider;
    RectTransform legRow;
    readonly List<TextMeshProUGUI> legTexts = new List<TextMeshProUGUI>();

    // current = the mode the lobby has now; its values are shown if it already is "Same stats"
    public static void Open(string current, Action<string> chosen)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Same stats", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 201;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<SameStatsPopup>();

        // The values now in the lobby, else the last ones used, else the old single pair
        if (!GameModes.TryGetSameStatsLegs(current, out List<Vector2Int> saved)
            && !GameModes.TryGetSameStatsLegs(PlayerPrefs.GetString(LegsKey, ""), out saved))
        {
            saved = new List<Vector2Int> { new Vector2Int(PlayerPrefs.GetInt(SpeedKey, DefaultValue), PlayerPrefs.GetInt(TechniqueKey, DefaultValue)) };
        }

        // A relay in the lobby: one pair per leg (legs without values of their own start with the last leg's)
        CourseData course = CourseLibrary.Current;
        int count = course != null && course.IsRelay() ? Mathf.Clamp(course.legs, 1, GameModes.MaxLegs) : 1;
        for (int i = 0; i < count; i++)
        {
            Vector2Int v = GameModes.LegValues(saved, i);
            open.legs.Add(new Vector2Int(Mathf.Clamp(v.x, GameModes.MinSpeed, GameModes.MaxSpeed), Mathf.Clamp(v.y, GameModes.MinTechnique, GameModes.MaxTechnique)));
        }
        open.Build(chosen);
    }

    bool Relay => legs.Count > 1;

    void Build(Action<string> chosen)
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(Relay ? 900 : 760, Relay ? 680 : 520));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 14, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Same stats", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, Relay
            ? "The relay has " + legs.Count + " legs. Pick a leg and set its values: every runner on that leg gets them, whoever the players pick."
            : "Every runner in the competition gets these values, whoever the players pick.", 21, CourseUI.InkMuted);
        CourseUI.Size(hint, 56);
        hint.enableWordWrapping = true;

        if (Relay)
        {
            legRow = CourseUI.Rect("Legs", card.transform);
            CourseUI.Row(legRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
            CourseUI.Size(legRow, 56);
        }

        speedLabel = StatRow("Physics", GameModes.MinSpeed, GameModes.MaxSpeed, legs[0].x, v => { SetLeg(new Vector2Int(v, legs[leg].y)); }, card.transform, out speedSlider);
        techniqueLabel = StatRow("Technique", GameModes.MinTechnique, GameModes.MaxTechnique, legs[0].y, v => { SetLeg(new Vector2Int(legs[leg].x, v)); }, card.transform, out techniqueSlider);

        if (Relay)
        {
            Button copy = CourseUI.Button("Copy to all legs", card.transform, "Use these values on every leg", CourseUI.ButtonKind.Secondary, () =>
            {
                Vector2Int v = legs[leg];
                for (int i = 0; i < legs.Count; i++) legs[i] = v;
                ShowLeg(leg);
            }, 22);
            CourseUI.Size(copy, 52);
        }

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 16, new RectOffset(0, 0, 10, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 68);
        Button cancel = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Close, 24);
        CourseUI.Size(cancel, -1, -1, 1);
        Button use = CourseUI.Button("Use", buttons, "Use these values", CourseUI.ButtonKind.Primary, () =>
        {
            string mode = GameModes.MakeSameStats(legs);
            PlayerPrefs.SetInt(SpeedKey, legs[0].x);
            PlayerPrefs.SetInt(TechniqueKey, legs[0].y);
            PlayerPrefs.SetString(LegsKey, mode);
            PlayerPrefs.Save();
            chosen?.Invoke(mode);
            Close();
        }, 24);
        CourseUI.Size(use, -1, -1, 1);

        ShowLeg(0);
    }

    static TextMeshProUGUI StatRow(string name, int min, int max, int value, Action<int> changed, Transform parent, out Slider slider)
    {
        TextMeshProUGUI label = CourseUI.Text(name + " label", parent, "", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(label, 40);
        slider = CourseUI.SliderControl(name, parent, min, max, value, true, v => changed(Mathf.RoundToInt(v)));
        CourseUI.Size(slider, 44);
        return label;
    }

    void SetLeg(Vector2Int values)
    {
        legs[leg] = values;
        Refresh();
    }

    // Shows one leg's values on the sliders
    void ShowLeg(int index)
    {
        leg = Mathf.Clamp(index, 0, legs.Count - 1);
        speedSlider.SetValueWithoutNotify(legs[leg].x);
        techniqueSlider.SetValueWithoutNotify(legs[leg].y);
        BuildLegButtons();
        Refresh();
    }

    // One button per leg with its values; the leg being set is orange
    void BuildLegButtons()
    {
        if (!Relay) return;
        foreach (Transform child in legRow) Destroy(child.gameObject);
        legTexts.Clear();
        for (int i = 0; i < legs.Count; i++)
        {
            int index = i;
            Button button = CourseUI.Button("Leg " + (i + 1), legRow, "",
                i == leg ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light, () => ShowLeg(index), 18);
            CourseUI.Size(button, -1, -1, 1);
            legTexts.Add(button.GetComponentInChildren<TextMeshProUGUI>());
        }
    }

    void Refresh()
    {
        string which = Relay ? "Leg " + (leg + 1) + "  " : "";
        speedLabel.text = which + "Physics  <color=#E8742A>" + legs[leg].x + "</color>";
        techniqueLabel.text = which + "Technique  <color=#E8742A>" + legs[leg].y + "</color>";
        for (int i = 0; i < legTexts.Count && i < legs.Count; i++)
        {
            if (legTexts[i] == null) continue;
            legTexts[i].text = (legs.Count > 6 ? "" : "Leg ") + (i + 1) + "\n<size=75%>" + legs[i].x + "/" + legs[i].y + "</size>";
            legTexts[i].enableWordWrapping = false;
        }
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
