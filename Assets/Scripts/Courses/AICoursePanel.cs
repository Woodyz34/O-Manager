using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The lobby's "AI course" window: choose the map, long, middle or sprint distance, the length and the number of
// controls, and the game lays a course for you. The course is only kept for this lobby; it is never saved or published.
public class AICoursePanel : MonoBehaviour
{
    static AICoursePanel instance;

    // The settings of the AI course in use, so a new course of the same kind, length and number of
    // controls can be laid when the lobby starts again after a race (see ReadyUIHandler.StartGame).
    public static CourseGenerator.Settings LastSettings { get; private set; }
    static MapDefinition lastMap;
    // The AI course that was last raced. Starting again with it gets a fresh course instead.
    public static string RacedCourseId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        LastSettings = null;
        lastMap = null;
        RacedCourseId = null;
    }

    // True when this course is an AI course that has already been raced and can be remade
    public static bool ShouldRemake(CourseData course)
    {
        return course != null && LastSettings != null && lastMap != null && !string.IsNullOrEmpty(course.id)
               && course.id.StartsWith("ai-") && course.id == RacedCourseId;
    }

    // Lays a new course with the same settings and a new seed. Null and the reason when it fails.
    public static async Task<CourseGenerator.Result> Remake()
    {
        MapGrid grid = MapGrid.Load(lastMap.gridName);
        CourseGenerator.Settings settings = new CourseGenerator.Settings
        {
            kind = LastSettings.kind,
            lengthKm = LastSettings.lengthKm,
            controls = LastSettings.controls,
            seed = Environment.TickCount
        };
        MapDefinition map = lastMap;
        return await Task.Run(() => CourseGenerator.Generate(map, grid, settings));
    }

    Action<CourseData> onUse;
    CourseGenerator.Kind kind = CourseGenerator.Kind.Middle;
    float lengthKm = 4f;
    int controls = 12;
    MapDefinition map;
    const string MapPref = "AICourse.Map";

    TextMeshProUGUI intro;
    Button mapButton;
    GameObject mapPicker;
    RectTransform mapPickerList;
    TMP_InputField mapSearch;
    string mapFilter = "all";
    readonly Dictionary<string, Button> mapFilterButtons = new Dictionary<string, Button>();
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
        // Start on the map of the course chosen in the lobby, else the map used last time
        MapDefinition start = null;
        CourseData current = CourseLibrary.Current;
        if (current != null && !string.IsNullOrEmpty(current.mapId)) start = MapDefinition.All.Find(m => m.id == current.mapId);
        if (start == null) start = MapDefinition.All.Find(m => m.id == PlayerPrefs.GetString(MapPref, ""));
        if (start == null && MapDefinition.All.Count > 0) start = MapDefinition.All[0];
        if (start != instance.map) instance.SetMap(start);
        instance.gameObject.SetActive(true);
        instance.transform.SetAsLastSibling();
    }

    static AICoursePanel Build(Transform parent)
    {
        // Dim the course list behind the window
        Image shade = CourseUI.Panel("AI course", parent, new Color(0.12f, 0.24f, 0.17f, 0.55f), false);
        CourseUI.Stretch(shade.rectTransform);
        AICoursePanel panel = shade.gameObject.AddComponent<AICoursePanel>();

        Image card = CourseUI.CardPanel("Window", shade.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 820));

        // Left: settings
        RectTransform left = CourseUI.Rect("Settings", card.transform);
        CourseUI.Place(left, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -36), new Vector2(560, 750));
        CourseUI.Column(left.gameObject, 14, new RectOffset(0, 0, 0, 0));

        TextMeshProUGUI title = CourseUI.Text("Title", left, "AI course", 42, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 54);
        panel.intro = CourseUI.Text("Intro", left, "", 21, CourseUI.InkMuted);
        CourseUI.Size(panel.intro, 84);

        // The map: a button showing the chosen map, which opens a list of every map
        RectTransform mapRow = CourseUI.Rect("Map row", left);
        CourseUI.Row(mapRow.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(mapRow, 56);
        TextMeshProUGUI mapLabel = CourseUI.Text("Label", mapRow, "Map:", 26, CourseUI.Ink, FontStyles.Bold);
        mapLabel.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(mapLabel, 56).preferredWidth = 80;
        panel.mapButton = CourseUI.Button("Map", mapRow, "Map", CourseUI.ButtonKind.Light, panel.OpenMapPicker, 24);
        CourseUI.Size(panel.mapButton, 56).flexibleWidth = 1;

        RectTransform kindRow = CourseUI.Rect("Kind", left);
        CourseUI.Row(kindRow.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(kindRow, 56);
        panel.kindButtons[CourseGenerator.Kind.Long] = CourseUI.Button("Long", kindRow, "Long distance", CourseUI.ButtonKind.Light, () => panel.SetKind(CourseGenerator.Kind.Long), 24);
        panel.kindButtons[CourseGenerator.Kind.Middle] = CourseUI.Button("Middle", kindRow, "Middle distance", CourseUI.ButtonKind.Light, () => panel.SetKind(CourseGenerator.Kind.Middle), 24);
        panel.kindButtons[CourseGenerator.Kind.Sprint] = CourseUI.Button("Sprint", kindRow, "Sprint", CourseUI.ButtonKind.Light, () => panel.SetKind(CourseGenerator.Kind.Sprint), 24);
        panel.kindHint = CourseUI.Text("Kind hint", left, "", 20, CourseUI.InkMuted);
        CourseUI.Size(panel.kindHint, 52);

        // The length in steps of 100 m
        panel.lengthLabel = CourseUI.Text("Length label", left, "", 26, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(panel.lengthLabel, 34);
        panel.lengthSlider = CourseUI.SliderControl("Length", left, CourseGenerator.MinLengthKm * 10f, CourseGenerator.MaxLengthKm * 10f, panel.lengthKm * 10f, true, v => panel.SetLength(v / 10f));
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
        CourseUI.Place(panel.resultText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -510), new Vector2(560, 150));

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Place(buttons, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 36), new Vector2(560, 60));
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        Button cancel = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, () => shade.gameObject.SetActive(false), 24);
        panel.useButton = CourseUI.Button("Use", buttons, "Use this course", CourseUI.ButtonKind.Primary, panel.Use, 24);
        panel.useButton.interactable = false;

        panel.SetMap(MapDefinition.All.Count > 0 ? MapDefinition.All[0] : null);
        return panel;
    }

    TextMeshProUGUI kindHint;

    // A new map: show only the kinds of course it can have, and forget the course made on the old map
    void SetMap(MapDefinition chosen)
    {
        map = chosen;
        if (map != null) PlayerPrefs.SetString(MapPref, map.id);
        string mapName = map != null ? map.displayName : "No map";
        string type = map == null ? "" : map.mapType == "both" ? "Forest and sprint" : map.IsSprint ? "Sprint" : "Forest";
        CourseUI.SetButtonText(mapButton, mapName + (type.Length > 0 ? "   <size=18><color=#5B7563>" + type + "</color></size>" : ""));
        intro.text = "The game lays a course for you on " + (map != null ? map.displayName : "the map") + " and checks that every control can be reached. It is only used in this lobby and is not saved.";

        List<CourseGenerator.Kind> kinds = CourseGenerator.KindsFor(map);
        foreach (var pair in kindButtons) pair.Value.gameObject.SetActive(kinds.Contains(pair.Key));
        List<string> hints = new List<string>();
        if (kinds.Contains(CourseGenerator.Kind.Long)) hints.Add("Long: mostly short legs with a few long ones.");
        if (kinds.Contains(CourseGenerator.Kind.Middle)) hints.Add("Middle: only short to medium-short legs.");
        if (kinds.Contains(CourseGenerator.Kind.Sprint)) hints.Add("Sprint: short legs through streets and parks, no mistakes.");
        kindHint.text = string.Join("\n", hints);

        generated = null;
        if (useButton != null) useButton.interactable = false;
        if (preview != null) { preview.texture = null; preview.enabled = false; }
        if (resultText != null) { resultText.color = CourseUI.Ink; resultText.text = "Choose the settings and press Make course."; }
        if (generateButton != null) CourseUI.SetButtonText(generateButton, "Make course");

        if (!kinds.Contains(kind))
        {
            // Sprint maps start on a sprint of 3 km, forest maps on a middle distance of 4 km
            bool sprint = kinds.Contains(CourseGenerator.Kind.Sprint) && !kinds.Contains(CourseGenerator.Kind.Middle);
            lengthKm = sprint ? 3f : 4f;
            controls = sprint ? 15 : 12;
            SetKind(sprint ? CourseGenerator.Kind.Sprint : CourseGenerator.Kind.Middle);
        }
        else SetKind(kind);
    }

    void SetKind(CourseGenerator.Kind chosen)
    {
        kind = chosen;
        foreach (var pair in kindButtons)
        {
            CourseUI.SetButtonColor(pair.Value, pair.Key == kind ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        }
        // Sprint has its own range of lengths
        lengthSlider.minValue = CourseGenerator.MinLengthFor(kind) * 10f;
        lengthSlider.maxValue = CourseGenerator.MaxLengthFor(kind) * 10f;
        lengthKm = Mathf.Clamp(lengthKm, CourseGenerator.MinLengthFor(kind), CourseGenerator.MaxLengthFor(kind));
        lengthSlider.SetValueWithoutNotify(Mathf.Round(lengthKm * 10f));
        UpdateLimits();
    }

    // ---- Choosing a map: a pop-up with a search field and forest/sprint filters ----

    void OpenMapPicker()
    {
        if (busy || mapPicker != null) return;
        Image dim = CourseUI.Panel("Choose a map", transform, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
        CourseUI.Stretch(dim.rectTransform);
        mapPicker = dim.gameObject;
        Image card = CourseUI.CardPanel("Card", dim.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 760));
        CourseUI.Column(card.gameObject, 12, new RectOffset(32, 32, 28, 28));
        CourseUI.Size(CourseUI.Text("Title", card.transform, "Choose a map", 34, CourseUI.Ink, FontStyles.Bold), 44);

        mapSearch = CourseUI.Input("Search", card.transform, "Search for a map", 40, _ => FillMapPicker());
        CourseUI.Size(mapSearch, 52);

        RectTransform filters = CourseUI.Rect("Filters", card.transform);
        CourseUI.Row(filters.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(filters, 46);
        mapFilterButtons.Clear();
        foreach (var f in new[] { ("all", "All maps"), ("forest", "Forest"), ("sprint", "Sprint") })
        {
            string key = f.Item1;
            mapFilterButtons[key] = CourseUI.Button(f.Item2, filters, f.Item2, CourseUI.ButtonKind.Light, () => { mapFilter = key; FillMapPicker(); }, 20);
        }

        mapPickerList = CourseUI.ScrollList("Maps", card.transform, out _);
        CourseUI.Column(mapPickerList.gameObject, 6, new RectOffset(0, 8, 0, 0));
        CourseUI.Size(mapPickerList.parent.GetComponent<Image>(), 420).flexibleHeight = 1;

        CourseUI.Size(CourseUI.Button("Close", card.transform, "Cancel", CourseUI.ButtonKind.Light, CloseMapPicker, 22), 52);
        FillMapPicker();
    }

    void FillMapPicker()
    {
        if (mapPickerList == null) return;
        foreach (var pair in mapFilterButtons)
            CourseUI.SetButtonColor(pair.Value, pair.Key == mapFilter ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
        for (int i = mapPickerList.childCount - 1; i >= 0; i--) Destroy(mapPickerList.GetChild(i).gameObject);
        string search = mapSearch != null ? mapSearch.text.Trim().ToLowerInvariant() : "";
        int shown = 0;
        foreach (MapDefinition m in MapDefinition.All)
        {
            if (mapFilter == "forest" && !m.IsForest) continue;
            if (mapFilter == "sprint" && !m.IsSprint) continue;
            string type = m.mapType == "both" ? "Forest and sprint" : m.mapType == "sprint" ? "Sprint" : "Forest";
            if (search.Length > 0 && !(m.displayName + " " + type).ToLowerInvariant().Contains(search)) continue;
            MapDefinition chosen = m;
            string typeColor = m == map ? "#D8E6DC" : "#5B7563";   // readable on the dark green of the chosen map too
            Button button = CourseUI.Button(m.displayName, mapPickerList, m.displayName + "   <size=18><color=" + typeColor + ">" + type + "</color></size>",
                m == map ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light, () => { CloseMapPicker(); if (chosen != map) SetMap(chosen); }, 22);
            button.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
            CourseUI.Size(button, 52);
            shown++;
        }
        if (shown == 0) CourseUI.Size(CourseUI.Text("None", mapPickerList, "No maps match.", 20, CourseUI.InkMuted, FontStyles.Italic), 40);
    }

    void CloseMapPicker()
    {
        if (mapPicker != null) Destroy(mapPicker);
        mapPicker = null;
        mapPickerList = null;
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
        int max = CourseGenerator.MaxControlsFor(kind, lengthKm);
        controlsSlider.minValue = min;
        controlsSlider.maxValue = max;
        controls = Mathf.Clamp(controls, min, max);
        controlsSlider.SetValueWithoutNotify(controls);
        controlsHint.text = $"{min} to {max} controls for this length. At most one control per {Mathf.RoundToInt(CourseGenerator.MinMetersPerControlFor(kind))} m, and never more than {CourseData.MaxControls}."
                            + (kind == CourseGenerator.Kind.Middle ? " Middle distance needs enough controls to keep the legs short."
                               : kind == CourseGenerator.Kind.Sprint ? " A sprint needs enough controls to keep the legs short."
                               : " A long course needs enough controls for its legs to fit on the map.");
        UpdateLabels();
    }

    void UpdateLabels()
    {
        lengthLabel.text = "Length: " + lengthKm.ToString("0.0") + " km";
        controlsLabel.text = "Controls: " + controls + "  <size=80%><color=#5B7563>(about " + Mathf.RoundToInt(lengthKm * 1000f / (controls + 1)) + " m per leg)</color></size>";
    }

    async void Generate()
    {
        if (busy || map == null || mapPicker != null) return;
        busy = true;
        generateButton.interactable = false;
        useButton.interactable = false;
        resultText.color = CourseUI.Ink;
        resultText.text = kind == CourseGenerator.Kind.Sprint ? "Laying the course and looking for legs with good route choices..." : "Laying the course and checking every leg...";

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
        resultText.text = $"<b>{generated.name}</b>\n{generated.controls.Count} controls  ·  {generated.LengthText()}  ·  legs {Mathf.RoundToInt(result.shortestLeg)} to {Mathf.RoundToInt(result.longestLeg)} m\nEvery control can be reached."
                          + (result.routeChoiceLegs >= 0 ? $" {result.routeChoiceLegs} of {result.legCount} legs have a close route choice." : "");
        useButton.interactable = true;
    }

    void Use()
    {
        if (generated == null) return;
        LastSettings = new CourseGenerator.Settings { kind = kind, lengthKm = lengthKm, controls = controls };
        lastMap = map;
        gameObject.SetActive(false);
        onUse?.Invoke(generated);
    }
}
