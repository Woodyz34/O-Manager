using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The player's own settings for the run-yourself game mode, kept between games on this computer: how good the 3D
// world looks (and so how quick it is on a weak computer), how fast the game runs compared with real life, whether
// the runner is a woman or a man (the breathing and the sounds), and how quick the mouse turns the head.
public static class RunSettings
{
    public enum Quality { Low = 0, Medium = 1, High = 2 }

    const string QualityKey = "RunYourself.Quality", SpeedKey = "RunYourself.Speed", WomanKey = "RunYourself.Woman",
        MouseKey = "RunYourself.Mouse", HandKey = "RunYourself.RightHand";

    public static Quality Level
    {
        get => (Quality)Mathf.Clamp(PlayerPrefs.GetInt(LocalPrefs.Key(QualityKey), (int)DefaultQuality()), 0, 2);
        set => PlayerPrefs.SetInt(LocalPrefs.Key(QualityKey), (int)value);
    }

    // How many times quicker than real life the runner moves and the race clock runs. The race time shown is still
    // the real-life time. 2026-11-19, after the first test: 3 times was too slow; 3, 4 and 5, with 4 to start with.
    public static readonly float[] Speeds = { 3f, 4f, 5f };

    public static float GameSpeed
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(LocalPrefs.Key(SpeedKey), 4f), 1f, 6f);
        set => PlayerPrefs.SetFloat(LocalPrefs.Key(SpeedKey), value);
    }

    public static bool Woman
    {
        get => PlayerPrefs.GetInt(LocalPrefs.Key(WomanKey), 0) == 1;
        set => PlayerPrefs.SetInt(LocalPrefs.Key(WomanKey), value ? 1 : 0);
    }

    // Which hand holds the map (and the thumb compass). Most hold it in the left hand.
    public static bool RightHand
    {
        get => PlayerPrefs.GetInt(LocalPrefs.Key(HandKey), 0) == 1;
        set => PlayerPrefs.SetInt(LocalPrefs.Key(HandKey), value ? 1 : 0);
    }

    // Degrees the head turns per unit of mouse movement
    public static float MouseSpeed
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(LocalPrefs.Key(MouseKey), 2f), 0.3f, 6f);
        set => PlayerPrefs.SetFloat(LocalPrefs.Key(MouseKey), value);
    }

    // A guess for a computer that has never chosen: a weak graphics card gets Low
    static Quality DefaultQuality()
    {
        int memory = SystemInfo.graphicsMemorySize;
        if (memory > 0 && memory < 1500) return Quality.Low;
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore && memory < 3000) return Quality.Low;
        return Quality.Medium;
    }

    // How much higher the hills are than on the map: the 10 m height grid rounds off small knolls and slopes, and
    // every contour should be seen and felt (2026-11-19, Anders: always twice, not a setting)
    public const float Hills = 2f;

    // ---- Sound levels (2026-11-19, Anders: good sounds but a bit loud; one slider per kind and one for all, 50 % to
    // start with so they can go both up and down) ----

    public enum Sound { All = 0, Steps = 1, Breathing = 2, Voice = 3, Beeps = 4 }
    static readonly string[] SoundNames = { "All sounds", "Footsteps", "Breathing", "Voice (falls, landings)", "Beeps (start, controls, finish)" };
    static readonly float[] cachedVolume = { -1f, -1f, -1f, -1f, -1f };

    public static float Volume(Sound kind)
    {
        int i = (int)kind;
        if (cachedVolume[i] < 0f) cachedVolume[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(LocalPrefs.Key("RunYourself.Volume." + kind), 0.5f));
        return cachedVolume[i];
    }

    public static void SetVolume(Sound kind, float value)
    {
        cachedVolume[(int)kind] = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(LocalPrefs.Key("RunYourself.Volume." + kind), cachedVolume[(int)kind]);
    }

    // What a sound of this kind is multiplied by: 0.6 of the first version's level at 50 % (it was too loud), up to
    // 1.2 times it at 100 %, times the "all sounds" share the same way
    public static float Gain(Sound kind) => 0.6f * (2f * Volume(kind)) * (2f * Volume(Sound.All));

    public static string QualityName(Quality q) => q == Quality.Low ? "Low" : q == Quality.High ? "High" : "Medium";

    // ---- What each level means ----

    public struct Look
    {
        public float view;            // metres: the world is built and drawn this far out; the fog hides the edge
        public float near;            // metres: closer than this, the full trees and ground are drawn
        public float detail;          // metres: closer than this, undergrowth and thickets are drawn
        public float undergrowth;     // share of the undergrowth a TV control has (1 = as much)
        public int shadows;           // 0 = none, 1 = hard, 2 = soft
        public float shadowDistance;
        public int msaa;
    }

    public static Look Current => For(Level);

    public static Look For(Quality q)
    {
        switch (q)
        {
            case Quality.Low:
                return new Look { view = 150f, near = 50f, detail = 26f, undergrowth = 0.06f, shadows = 0, shadowDistance = 0f, msaa = 1 };
            case Quality.High:
                return new Look { view = 330f, near = 96f, detail = 56f, undergrowth = 0.22f, shadows = 2, shadowDistance = 60f, msaa = 4 };
            default:
                return new Look { view = 230f, near = 72f, detail = 40f, undergrowth = 0.12f, shadows = 1, shadowDistance = 32f, msaa = 2 };
        }
    }

    // ---- The settings window (single player page: "3D settings") ----

    static GameObject open;

    public static bool IsOpen => open != null;

    public static void Open(System.Action changed = null)
    {
        if (open != null) Object.Destroy(open);
        open = new GameObject("Run settings", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = open.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;
        ScreenSettings.Fit(open.GetComponent<CanvasScaler>());

        Image dim = CourseUI.Panel("Dim", open.transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Image card = CourseUI.CardPanel("Card", open.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 840));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "3D settings", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, RunSession.Active != null
            ? "Mouse speed and sounds count at once; the rest from your next race. Saved on this computer."
            : "For \"Run it yourself\". Saved on this computer.", 21, CourseUI.InkMuted);
        CourseUI.Size(hint, 30);

        // Two columns: the race on the left, hills and sounds on the right
        RectTransform columns = CourseUI.Rect("Columns", card.transform);
        CourseUI.Row(columns.gameObject, 40, new RectOffset(0, 0, 0, 0)).childForceExpandHeight = false;
        CourseUI.Size(columns, 610);
        RectTransform left = CourseUI.Rect("Left", columns), right = CourseUI.Rect("Right", columns);
        foreach (RectTransform column in new[] { left, right })
        {
            CourseUI.Column(column.gameObject, 10, new RectOffset(0, 0, 0, 0));
            CourseUI.Size(column, -1, -1, 1f);
        }

        Chips(left, "Graphics", new[] { "Low", "Medium", "High" }, (int)Level, i => { Level = (Quality)i; changed?.Invoke(); },
            "Low: shorter view, no shadows, little undergrowth. For weak computers. High: long view and soft shadows.");
        int speedIndex = 1;   // 4 x
        for (int i = 0; i < Speeds.Length; i++) if (Mathf.Approximately(Speeds[i], GameSpeed)) speedIndex = i;
        Chips(left, "Game speed", new[] { "3 x real life", "4 x real life", "5 x real life" }, speedIndex, i => { GameSpeed = Speeds[i]; changed?.Invoke(); },
            "How much quicker than real life you run. The race time shown is still the real-life time.");
        Chips(left, "Your runner", new[] { "Man", "Woman" }, Woman ? 1 : 0, i => { Woman = i == 1; changed?.Invoke(); },
            "Your breathing and your groans when you fall.");
        Chips(left, "Map hand", new[] { "Left hand", "Right hand" }, RightHand ? 1 : 0, i => { RightHand = i == 1; changed?.Invoke(); },
            "The hand that holds the map and the thumb compass.");

        TextMeshProUGUI mouseLabel = CourseUI.Text("Mouse label", left, "Mouse speed", 24, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(mouseLabel, 32);
        Slider mouse = CourseUI.SliderControl("Mouse", left, 0.3f, 6f, MouseSpeed, false, v => MouseSpeed = v);
        CourseUI.Size(mouse, 40);

        for (int k = 0; k < SoundNames.Length; k++)
        {
            Sound kind = (Sound)k;
            if (kind == Sound.Breathing) continue;   // no breathing sounds for now
            TextMeshProUGUI label = CourseUI.Text(SoundNames[k] + " label", right, "", 22, CourseUI.Ink, k == 0 ? FontStyles.Bold : FontStyles.Normal);
            label.richText = true;
            CourseUI.Size(label, 28);
            System.Action show = () => label.text = SoundNames[(int)kind] + "  <color=#7A8A7F>" + Mathf.RoundToInt(Volume(kind) * 100f) + " %</color>";
            show();
            Slider slider = CourseUI.SliderControl(kind + " volume", right, 0f, 1f, Volume(kind), false, v => { SetVolume(kind, v); show(); });
            CourseUI.Size(slider, 34);
        }

        Button done = CourseUI.Button("Done", card.transform, "Done", CourseUI.ButtonKind.Primary, () => Object.Destroy(open), 24);
        CourseUI.Size(done, 58);
        TextMeshProUGUI credit = CourseUI.Text("Credit", card.transform, RunSounds.Credit, 15, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Size(credit, 22);
    }

    static void Chips(Transform parent, string label, string[] names, int chosen, System.Action<int> pick, string tip)
    {
        TextMeshProUGUI caption = CourseUI.Text(label, parent, label, 24, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(caption, 32);
        RectTransform row = CourseUI.Rect(label + " row", parent);
        HorizontalLayoutGroup layout = CourseUI.Row(row.gameObject, 12, new RectOffset(0, 0, 0, 0));
        layout.childForceExpandWidth = true;
        CourseUI.Size(row, 56);
        var buttons = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            buttons[i] = CourseUI.Button(names[i], row, names[i], i == chosen ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light, () =>
            {
                for (int k = 0; k < buttons.Length; k++) CourseUI.SetButtonColor(buttons[k], k == index ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
                pick(index);
            }, 22);
        }
        HoverTip.Add(row, tip);
    }
}
