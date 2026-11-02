using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The lobby's "AI course" window: choose long or middle distance, the length and the number of controls,
// and the game lays a course for you. The course is only kept for this lobby; it is never saved or published.
public class AICoursePanel : MonoBehaviour
{
    static AICoursePanel instance;

    Action<CourseData> onUse;
    CourseGenerator.Kind kind = CourseGenerator.Kind.Middle;
    float lengthKm = 4f;
    int controls = 12;
    MapDefinition map;

    readonly Dictionary<CourseGenerator.Kind, Button> kindButtons = new Dictionary<CourseGenerator.Kind, Button>();
    Slider lengthSlider;
    Slider controlsSlider;
    TextMeshProUGUI lengthLabel;
    TextMeshProUGUI controlsLabel;
    TextMeshProUGUI controlsHint;
    TextMeshProUGUI resultText;
    RawImage preview;
    Button generateButton;
    Button useButton;
    CourseData generated;
    bool busy;

    public static void Open(Transform parent, Action<CourseData> onUse)
    {
        if (instance == null)
        {
            instance = Build(parent);
        }
        instance.onUse = onUse;
        instance.gameObject.SetActive(true);
        instance.transform.SetAsLastSibling();
    }

    static AICoursePanel Build(Transform parent)
    {
        // Dim the course list behind the window
        Image shade = CourseUI.Panel("AI course", parent, new Color(0.12f, 0.24f, 0.17f, 0.55f), false);
        CourseUI.Stretch(shade.rectTransform);
        AICoursePanel panel = shade.gameObject.AddComponent<AICoursePanel>();
        panel.map = MapDefinition.All.Count > 0 ? MapDefinition.All[0] : null;

        Image card = CourseUI.CardPanel("Window", shade.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 760));

        // Left: settings
        RectTransform left = CourseUI.Rect("Settings", card.transform);
        CourseUI.Place(left, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -36), new Vector2(560, 690));
        CourseUI.Column(left.gameObject, 14, new RectOffset(0, 0, 0, 0));

        TextMeshProUGUI title = CourseUI.Text("Title", left, "AI course", 42, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 54);
        TextMeshProUGUI intro = CourseUI.Text("Intro", left, "The game lays a course for you on " + (panel.map != null ? panel.map.displayName : "the map") + " and checks that every control can be reached. It is only used in this lobby and is not saved.", 21, CourseUI.InkMuted);
        CourseUI.Size(intro, 84);

        RectTransform kindRow = CourseUI.Rect("Kind", left);
        CourseUI.Row(kindRow.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(kindRow, 56);
        panel.kindButtons[CourseGenerator.Kind.Long] = CourseUI.Button("Long", kindRow, "Long distance", CourseUI.ButtonKind.Light, () => panel.SetKind(CourseGenerator.Kind.Long), 24);
        panel.kindButtons[CourseGenerator.Kind.Middle] = CourseUI.Button("Middle", kindRow, "Middle distance", CourseUI.ButtonKind.Light, () => panel.SetKind(CourseGenerator.Kind.Middle), 24);
        TextMeshProUGUI kindHint = CourseUI.Text("Kind hint", left, "Long: mostly short legs with a few long ones.\nMiddle: only short to medium-short legs.", 20, CourseUI.InkMuted);
        CourseUI.Size(kindHint, 52);

        panel.lengthLabel = CourseUI.Text("Length label", left, "", 26, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(panel.lengthLabel, 34);
        panel.lengthSlider = CourseUI.SliderControl("Length", left, CourseGenerator.MinLengthKm * 2f, CourseGenerator.MaxLengthKm * 2f, panel.lengthKm * 2f, true, v => panel.SetLength(v / 2f));
        CourseUI.Size(panel.lengthSlider, 40);

        panel.controlsLabel = CourseUI.Text("Controls label", left, "", 26, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(panel.controlsLabel, 34);
        panel.controlsSlider = CourseUI.SliderControl("Controls", left, CourseGenerator.MinControls, CourseData.MaxControls, panel.controls, true, v => panel.SetControls(Mathf.RoundToInt(v)));
        CourseUI.Size(panel.controlsSlider, 40);
        panel.controlsHint = CourseUI.Text("Controls hint", left, "", 20, CourseUI.InkMuted);
        CourseUI.Size(panel.controlsHint, 78);

        panel.generateButton = CourseUI.Button("Generate", left, "Make course", CourseUI.ButtonKind.Secondary, panel.Generate, 26);
        CourseUI.Size(panel.generateButton, 60);

        // Right: preview and the result
        Image frame = CourseUI.Panel("Preview", card.transform, CourseUI.CardSolid);
        CourseUI.Place(frame.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(560, 456));
        frame.gameObject.AddComponent<RectMask2D>();
        panel.preview = CourseUI.Rect("Map", frame.transform).gameObject.AddComponent<RawImage>();
        CourseUI.Stretch(panel.preview.rectTransform);
        panel.preview.raycastTarget = false;
        panel.preview.enabled = false;

        panel.resultText = CourseUI.Text("Result", card.transform, "Choose the settings and press Make course.", 22, CourseUI.Ink);
        CourseUI.Place(panel.resultText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -510), new Vector2(560, 100));

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Place(buttons, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 36), new Vector2(560, 60));
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        Button cancel = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, () => shade.gameObject.SetActive(false), 24);
        panel.useButton = CourseUI.Button("Use", buttons, "Use this course", CourseUI.ButtonKind.Primary, panel.Use, 24);
        panel.useButton.interactable = false;

        panel.SetKind(panel.kind);
        return panel;
    }

    void SetKind(CourseGenerator.Kind chosen)
    {
        kind = chosen;
        foreach (var pair in kindButtons)
        {
            CourseUI.SetButtonColor(pair.Value, pair.Key == kind ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        }
        UpdateLimits();
    }

    void SetLength(float km)
    {
        lengthKm = km;
        UpdateLimits();
    }

    void SetControls(int count)
    {
        controls = count;
        UpdateLabels();
    }

    // The number of controls depends on the length (at most one per 50 m) and, for middle distance,
    // there must be enough of them to keep the legs short
    void UpdateLimits()
    {
        int min = CourseGenerator.MinControlsFor(kind, lengthKm);
        int max = CourseGenerator.MaxControlsFor(lengthKm);
        controlsSlider.minValue = min;
        controlsSlider.maxValue = max;
        controls = Mathf.Clamp(controls, min, max);
        controlsSlider.SetValueWithoutNotify(controls);
        controlsHint.text = $"{min} to {max} controls for this length. At most one control per 50 m, and never more than {CourseData.MaxControls}."
                            + (kind == CourseGenerator.Kind.Middle ? " Middle distance needs enough controls to keep the legs short." : " A long course needs enough controls for its legs to fit on the map.");
        UpdateLabels();
    }

    void UpdateLabels()
    {
        lengthLabel.text = "Length: " + lengthKm.ToString("0.0") + " km";
        controlsLabel.text = "Controls: " + controls + "  <size=80%><color=#5B7563>(about " + Mathf.RoundToInt(lengthKm * 1000f / (controls + 1)) + " m per leg)</color></size>";
    }

    async void Generate()
    {
        if (busy || map == null) return;
        busy = true;
        generateButton.interactable = false;
        useButton.interactable = false;
        resultText.color = CourseUI.Ink;
        resultText.text = "Laying the course and checking every leg...";

        MapGrid grid = MapGrid.Load(map.gridName); // Resources has to be used on the main thread
        CourseGenerator.Settings settings = new CourseGenerator.Settings
        {
            kind = kind,
            lengthKm = lengthKm,
            controls = controls,
            seed = Environment.TickCount
        };
        MapDefinition chosenMap = map;
        CourseGenerator.Result result = await Task.Run(() => CourseGenerator.Generate(chosenMap, grid, settings));
        if (this == null) return;

        busy = false;
        generateButton.interactable = true;
        CourseUI.SetButtonText(generateButton, "Make another");
        if (result.course == null)
        {
            generated = null;
            resultText.color = CourseUI.Danger;
            resultText.text = result.error;
            return;
        }
        generated = result.course;
        preview.texture = CourseThumbnail.Get(generated);
        preview.enabled = preview.texture != null;
        resultText.text = $"<b>{generated.name}</b>\n{generated.controls.Count} controls  ·  {generated.LengthText()}  ·  legs {Mathf.RoundToInt(result.shortestLeg)} to {Mathf.RoundToInt(result.longestLeg)} m\nEvery control can be reached.";
        useButton.interactable = true;
    }

    void Use()
    {
        if (generated == null) return;
        gameObject.SetActive(false);
        onUse?.Invoke(generated);
    }
}
