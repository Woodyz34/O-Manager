using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The host's window for the runner pool "Same stats": two sliders for the speed and technique that every runner in
// the competition gets. The last values are remembered between games.
public class SameStatsPopup : MonoBehaviour
{
    const string SpeedKey = "Lobby.SameStatsSpeed";
    const string TechniqueKey = "Lobby.SameStatsTechnique";
    const int DefaultValue = 85;

    static SameStatsPopup open;

    int speed;
    int technique;
    TextMeshProUGUI speedLabel;
    TextMeshProUGUI techniqueLabel;

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

        if (!GameModes.TryGetSameStats(current, out open.speed, out open.technique))
        {
            open.speed = PlayerPrefs.GetInt(SpeedKey, DefaultValue);
            open.technique = PlayerPrefs.GetInt(TechniqueKey, DefaultValue);
        }
        open.speed = Mathf.Clamp(open.speed, GameModes.MinSpeed, GameModes.MaxSpeed);
        open.technique = Mathf.Clamp(open.technique, GameModes.MinTechnique, GameModes.MaxTechnique);
        open.Build(chosen);
    }

    void Build(Action<string> chosen)
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 520));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 14, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Same stats", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform,
            "Every runner in the competition gets these values, whoever the players pick.", 21, CourseUI.InkMuted);
        CourseUI.Size(hint, 56);
        hint.enableWordWrapping = true;

        speedLabel = StatRow("Speed", GameModes.MinSpeed, GameModes.MaxSpeed, speed, v => { speed = v; Refresh(); }, card.transform);
        techniqueLabel = StatRow("Technique", GameModes.MinTechnique, GameModes.MaxTechnique, technique, v => { technique = v; Refresh(); }, card.transform);
        Refresh();

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 16, new RectOffset(0, 0, 10, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 68);
        Button cancel = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Close, 24);
        CourseUI.Size(cancel, -1, -1, 1);
        Button use = CourseUI.Button("Use", buttons, "Use these values", CourseUI.ButtonKind.Primary, () =>
        {
            PlayerPrefs.SetInt(SpeedKey, speed);
            PlayerPrefs.SetInt(TechniqueKey, technique);
            PlayerPrefs.Save();
            chosen?.Invoke(GameModes.MakeSameStats(speed, technique));
            Close();
        }, 24);
        CourseUI.Size(use, -1, -1, 1);
    }

    static TextMeshProUGUI StatRow(string name, int min, int max, int value, Action<int> changed, Transform parent)
    {
        TextMeshProUGUI label = CourseUI.Text(name + " label", parent, "", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(label, 40);
        Slider slider = CourseUI.SliderControl(name, parent, min, max, value, true, v => changed(Mathf.RoundToInt(v)));
        CourseUI.Size(slider, 44);
        return label;
    }

    void Refresh()
    {
        speedLabel.text = "Speed  <color=#E8742A>" + speed + "</color>";
        techniqueLabel.text = "Technique  <color=#E8742A>" + technique + "</color>";
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
