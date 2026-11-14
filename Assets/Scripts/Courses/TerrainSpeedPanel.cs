using System;
using System.Collections.Generic;
using MapImport;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The "Terrain speeds" pop-up of Import map and Edit map: sliders for how quick the kinds of ground are on this map
// (see MapImport.TerrainSpeeds). One slider for how tough the terrain is overall, and under "Details" one slider per
// kind of ground. Every slider starts in the middle, which is the game's normal speed, and its ends are named after
// the neighbouring kind of ground, so a player only moves the ones that differ on their map.
// The numbers show the speed compared with normal white forest (100 %).
public class TerrainSpeedPanel : MonoBehaviour
{
    TerrainSpeeds speeds;
    Action<TerrainSpeeds> onDone;
    bool showDetails;

    class Row
    {
        public RectTransform root;
        public Slider slider;
        public TextMeshProUGUI value;
        public Func<TerrainSpeeds, float> get;
        public Action<TerrainSpeeds, float> set;
        public Func<TerrainSpeeds, string> describe;
        public bool detail;
    }

    readonly List<Row> rows = new List<Row>();
    Button detailsButton;
    TextMeshProUGUI summary;
    bool refreshing;

    public static TerrainSpeedPanel Open(Transform canvas, TerrainSpeeds current, Action<TerrainSpeeds> onDone)
    {
        var dim = CourseUI.Panel("Terrain speeds", canvas, new Color(0.08f, 0.14f, 0.1f, 0.7f), false);
        CourseUI.Stretch(dim.rectTransform);
        TerrainSpeedPanel panel = dim.gameObject.AddComponent<TerrainSpeedPanel>();
        panel.speeds = current != null ? current.Copy() : new TerrainSpeeds();
        panel.onDone = onDone;
        // Details open straight away when one of them was already moved
        TerrainSpeeds onlyOverall = new TerrainSpeeds { overall = panel.speeds.overall };
        panel.showDetails = !panel.speeds.SameAs(onlyOverall);
        panel.Build(dim.transform);
        return panel;
    }

    // One line for the import and edit pop-ups: what the sliders say now
    public static string Describe(TerrainSpeeds s)
    {
        if (s == null || s.IsDefault()) return "Terrain speeds: normal.";
        var changed = new List<string>();
        void Add(float v, string name) { if (Mathf.Abs(v - TerrainSpeeds.Middle) >= 0.005f) changed.Add(name + (v < TerrainSpeeds.Middle ? " slower" : " quicker")); }
        if (Mathf.Abs(s.overall - TerrainSpeeds.Middle) >= 0.005f) changed.Add(s.overall < TerrainSpeeds.Middle ? "tougher overall" : "easier overall");
        Add(s.roads, "roads");
        Add(s.paths, "small paths");
        Add(s.forest, "white forest");
        Add(s.lightGreen, "light green");
        Add(s.midGreen, "mid green");
        if (s.darkGreen <= 0.03f) changed.Add("dark green impassable");
        else Add(s.darkGreen, "dark green");
        Add(s.marsh, "marsh");
        Add(s.stony, "stony ground");
        Add(s.open, "open land");
        Add(s.roughOpen, "felled areas (hygge)");
        Add(s.paved, "asphalt");
        return "Terrain speeds: " + string.Join(", ", changed) + ".";
    }

    // ---- Layout ----

    void Build(Transform root)
    {
        Image card = CourseUI.CardPanel("Card", root);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 1040));
        CourseUI.Column(card.gameObject, 8, new RectOffset(36, 36, 28, 28));

        CourseUI.Size(CourseUI.Text("Title", card.transform, "Terrain speeds", 34, CourseUI.Ink, FontStyles.Bold), 42);
        TextMeshProUGUI info = CourseUI.Text("Info", card.transform,
            "How quick the ground is on this map. Terrain differs: thorny dark green you can't get through, sandy roads, white forest " +
            "as quick as a path. The middle of each slider is the game's normal. Numbers are the speed compared with normal white forest.",
            19, CourseUI.InkMuted);
        info.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(info, 56);

        AddRow(card.transform, "Overall", "Very tough", "Easy running", false,
            s => s.overall, (s, v) => s.overall = v,
            s => "x " + s.OverallFactor().ToString("0.00"));

        RectTransform detailBar = CourseUI.Rect("Details bar", card.transform);
        CourseUI.Row(detailBar.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(detailBar, 50);
        detailsButton = CourseUI.Button("Details", detailBar, "", CourseUI.ButtonKind.Light, () => { showDetails = !showDetails; Refresh(); }, 20);
        CourseUI.Size(detailsButton, -1, 300);
        HoverTip.Add(detailsButton, "Shows or hides one slider per kind of ground, if you want to fine-tune roads, marsh, green areas and so on.");
        summary = CourseUI.Text("Summary", detailBar, "", 18, CourseUI.InkMuted);
        summary.alignment = TextAlignmentOptions.MidlineLeft;
        summary.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(summary, -1, -1, 1);

        // Each kind of ground, quickest first; the slow end of each slider on the left
        AddRow(card.transform, "Asphalt", "Like open land", "Quicker", true,
            s => s.paved, (s, v) => s.paved = v, s => Percent(s.Ground(TerrainKind.Paved, TerrainSpeeds.NormalPaved)));
        AddRow(card.transform, "Roads", "Like white forest", "Like asphalt", true,
            s => s.roads, (s, v) => s.roads = v, s => Percent(s.Line(TerrainKind.Road, TerrainSpeeds.NormalRoad)));
        AddRow(card.transform, "Small paths", "Like white forest", "Like a vehicle track", true,
            s => s.paths, (s, v) => s.paths = v, s => Percent(s.Line(TerrainKind.Path, TerrainSpeeds.NormalPath)));
        AddRow(card.transform, "Open land", "Like white forest", "Like asphalt", true,
            s => s.open, (s, v) => s.open = v, s => Percent(s.Ground(TerrainKind.Open, TerrainSpeeds.NormalOpen)));
        AddRow(card.transform, "White forest", "A fight every step", "Quick as a road", true,
            s => s.forest, (s, v) => s.forest = v, s => Percent(s.Ground(TerrainKind.Forest, TerrainSpeeds.NormalForest)));
        // Rough open land (ISOM 403/404, light yellow): mostly felled areas (hygge), also heath and newly planted ground.
        // "Felled area" is the IOF's own English word for it; the Swedish word in brackets, since everyone knows "hygge"
        AddRow(card.transform, "Felled area (hygge)", "Like mid green", "Like open land", true,
            s => s.roughOpen, (s, v) => s.roughOpen = v, s => Percent(s.Ground(TerrainKind.RoughOpen, TerrainSpeeds.NormalRoughOpen)));
        AddRow(card.transform, "Marsh", "Very heavy", "Like white forest", true,
            s => s.marsh, (s, v) => s.marsh = v, s => Percent(s.Ground(TerrainKind.Marsh, TerrainSpeeds.NormalMarsh)));
        AddRow(card.transform, "Light green", "Like mid green", "Like white forest", true,
            s => s.lightGreen, (s, v) => s.lightGreen = v, s => Percent(s.Ground(TerrainKind.LightGreen, TerrainSpeeds.NormalLight)));
        AddRow(card.transform, "Mid green", "Like dark green", "Like light green", true,
            s => s.midGreen, (s, v) => s.midGreen = v, s => Percent(s.Ground(TerrainKind.MidGreen, TerrainSpeeds.NormalMid)));
        AddRow(card.transform, "Dark green", "No way through", "Like mid green", true,
            s => s.darkGreen, (s, v) => s.darkGreen = v,
            s => s.darkGreen <= 0.03f ? "Closed" : Percent(s.Ground(TerrainKind.DarkGreen, TerrainSpeeds.NormalDark)));
        AddRow(card.transform, "Stony ground", "Very slow", "No slower", true,
            s => s.stony, (s, v) => s.stony = v, s =>
            {
                int slower = Mathf.RoundToInt((1f - s.StonyFactor(TerrainSpeeds.NormalStony)) * 100f);
                return slower <= 0 ? "0 %" : "-" + slower + " %";
            });

        // Room above the buttons
        RectTransform spacer = CourseUI.Rect("Spacer", card.transform);
        spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        HoverTip.Add(CourseUI.Button("Normal", buttons, "All normal", CourseUI.ButtonKind.Light, ResetAll, 22), "Puts every slider back in the middle, the game's normal speeds.");
        HoverTip.Add(CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, () => Destroy(gameObject), 22), "Closes this window and throws away your changes.");
        HoverTip.Add(CourseUI.Button("Done", buttons, "Done", CourseUI.ButtonKind.Primary, Done, 22), "Keeps these speeds for the map.");

        Refresh();
    }

    static string Percent(float speed) => Mathf.RoundToInt(speed * 100f) + " %";

    void AddRow(Transform parent, string name, string slow, string quick, bool detail,
                Func<TerrainSpeeds, float> get, Action<TerrainSpeeds, float> set, Func<TerrainSpeeds, string> describe)
    {
        RectTransform row = CourseUI.Rect(name, parent);
        CourseUI.Row(row.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(row, 48);
        TextMeshProUGUI label = CourseUI.Text("Name", row, name, 22, CourseUI.Ink, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(label, -1, 240);   // room for "Felled area (hygge)"
        TextMeshProUGUI left = CourseUI.Text("Slow end", row, slow, 17, CourseUI.InkMuted);
        left.alignment = TextAlignmentOptions.MidlineRight;
        left.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(left, -1, 200);
        var r = new Row { root = row, get = get, set = set, describe = describe, detail = detail };
        r.slider = CourseUI.SliderControl("Slider", row, 0f, 1f, get(speeds), false, v =>
        {
            if (refreshing) return;
            // Snaps to the middle near it, so "normal" is easy to find again
            if (Mathf.Abs(v - TerrainSpeeds.Middle) < 0.025f) v = TerrainSpeeds.Middle;
            set(speeds, v);
            Refresh();
        });
        CourseUI.Size(r.slider, 40, -1, 1);
        TextMeshProUGUI right = CourseUI.Text("Quick end", row, quick, 17, CourseUI.InkMuted);
        right.alignment = TextAlignmentOptions.MidlineLeft;
        right.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(right, -1, 200);
        r.value = CourseUI.Text("Value", row, "", 20, CourseUI.Forest, FontStyles.Bold);
        r.value.alignment = TextAlignmentOptions.MidlineRight;
        CourseUI.Size(r.value, -1, 100);
        rows.Add(r);
        if (Tips.TryGetValue(name, out string tip)) HoverTip.Add(row, tip);
    }

    // Hover texts: what each kind of ground is on the map
    static readonly Dictionary<string, string> Tips = new Dictionary<string, string>
    {
        { "Overall", "Moves every kind of ground at once. Towards 'Very tough' the whole map is slower, towards 'Easy running' it is quicker. Use it for a map that is generally harder or easier than normal." },
        { "Asphalt", "Hard paved ground: tarmac roads, car parks, paved squares and pavements. The quickest ground in the game, mostly found on sprint maps." },
        { "Roads", "Roads drawn as lines on the map, such as gravel and forest roads. Quick to run on. Move the slider if the roads on this map are sandy (slower) or hard and smooth (quicker)." },
        { "Small paths", "Small paths and tracks drawn as thin lines. Quicker than the forest around them, slower than a road." },
        { "Open land", "Open ground without trees: fields, meadows and lawns. Quicker than white forest. Slide towards the slow end for tussocks or ploughed fields." },
        { "White forest", "Ordinary forest you can run straight through, shown white on the map. This is the speed everything else is compared with (100 %)." },
        { "Felled area (hygge)", "Light yellow areas on the map: felled forest with branches and stumps, heath and newly planted ground. Open to look at, but rough to run on." },
        { "Marsh", "Marshes and bogs. Soft and heavy ground that slows the runners a lot. Slide it towards 'Like white forest' for a dry marsh that is easy to cross." },
        { "Light green", "Slow-running forest, shown light green on the map: thick undergrowth or young trees you have to push through." },
        { "Mid green", "Very slow-running forest, shown mid green: really thick vegetation. Runners avoid it when there is a way around." },
        { "Dark green", "Almost impassable thicket, shown dark green. At the left end it is closed: runners cannot go through at all and have to run around." },
        { "Stony ground", "How much stones and boulders slow the runner on top of the ground below them. 'No slower' means stones make no difference." },
    };

    void Refresh()
    {
        refreshing = true;
        foreach (Row r in rows)
        {
            if (r.detail && r.root.gameObject.activeSelf != showDetails) r.root.gameObject.SetActive(showDetails);
            float v = r.get(speeds);
            if (Mathf.Abs(r.slider.value - v) > 0.0001f) r.slider.SetValueWithoutNotify(v);
            bool moved = Mathf.Abs(v - TerrainSpeeds.Middle) >= 0.005f;
            r.value.text = r.describe(speeds);
            r.value.color = moved ? CourseUI.Accent : CourseUI.Forest;
        }
        refreshing = false;
        CourseUI.SetButtonText(detailsButton, showDetails ? "Hide details" : "Details per kind of ground");
        summary.text = Describe(speeds);
    }

    void ResetAll()
    {
        speeds = new TerrainSpeeds();
        Refresh();
    }

    void Done()
    {
        onDone?.Invoke(speeds.Copy());
        Destroy(gameObject);
    }
}
