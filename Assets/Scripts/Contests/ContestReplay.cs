using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The replays of a weekly or daily contest, shown on the contests page (no race scene needed): the map picture with
// the course, and
//  - for a race: the runners' ways round, growing as the time goes, with a dot and the name at the front; the whole
//    course or one leg at a time, everyone starting the leg together, with the leg times (2026-11-24, the user);
//  - for the route choice game: one leg at a time with its routes, and which route each runner picked.
// Only shown after the player has run the contest themselves (the cloud says no before that).
public class ContestReplay : MonoBehaviour
{
    // ---- What a replay holds (ContestRun writes it) ----

    public class ChoiceRoute
    {
        public int colour;
        public float seconds;
        public List<Vector3> world = new List<Vector3>();   // when written, in the race scene
        public List<CoursePoint> points = new List<CoursePoint>();   // when read back
    }

    public class ChoiceLeg
    {
        public int leg;
        public int fastest;
        public int pick;      // -1 time ran out
        public int points;
        public List<ChoiceRoute> routes = new List<ChoiceRoute>();
    }

    public class Runner
    {
        public string name;
        public int place;
        public bool me;
        public Color color;
        public List<float> times = new List<float>();          // game seconds
        public List<CoursePoint> track = new List<CoursePoint>();
        public List<ChoiceLeg> legs = new List<ChoiceLeg>();
        public List<float> controlTimes = new List<float>();   // when each control (1.., then the finish) was reached
        public bool shown = true;
        // drawn
        public UIRouteLine line;
        public RectTransform dot;
        public TextMeshProUGUI dotLabel;
        public TextMeshProUGUI listLabel;
        // legs: the track point at each control (ends[0] = 0, the start) and the time on each leg (game seconds)
        public int[] ends;
        public float[] legSeconds;
        public int viewFrom, viewTo;
        public float viewOffset;
    }

    static readonly Color[] Palette =
    {
        new Color(0.91f, 0.45f, 0.16f), new Color(0.13f, 0.47f, 0.85f), new Color(0.20f, 0.62f, 0.33f),
        new Color(0.80f, 0.16f, 0.30f), new Color(0.55f, 0.30f, 0.75f), new Color(0.10f, 0.65f, 0.70f), new Color(0.85f, 0.70f, 0.10f),
        new Color(0.45f, 0.30f, 0.20f), new Color(0.95f, 0.35f, 0.70f), new Color(0.30f, 0.30f, 0.30f), new Color(0.55f, 0.75f, 0.15f)
    };

    // Reads one replay; false when it can not be read
    public static bool Read(string json, CourseData course, Runner runner)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            JObject o = JObject.Parse(json);
            CoursePoint origin = course.AllPoints()[0];
            string kind = (string)o["k"];
            if (kind == "race")
            {
                JArray t = (JArray)o["t"], p = (JArray)o["p"];
                int n = Mathf.Min(t.Count, p.Count / 2);
                for (int i = 0; i < n; i++)
                {
                    runner.times.Add((int)t[i] / 10f);
                    runner.track.Add(new CoursePoint(origin.e + (int)p[2 * i] / 10.0, origin.n + (int)p[2 * i + 1] / 10.0));
                }
                if (o["c"] is JArray c) foreach (JToken x in c) runner.controlTimes.Add((int)x / 10f);
                return n > 0;
            }
            if (kind == "rc")
            {
                foreach (JObject l in (JArray)o["legs"])
                {
                    ChoiceLeg leg = new ChoiceLeg { leg = (int)l["l"], fastest = (int)l["f"], pick = (int)l["pick"], points = (int)l["pts"] };
                    foreach (JObject r in (JArray)l["r"])
                    {
                        ChoiceRoute route = new ChoiceRoute { colour = (int)r["c"], seconds = (float)r["s"] };
                        JArray p = (JArray)r["p"];
                        for (int i = 0; i + 1 < p.Count; i += 2)
                            route.points.Add(new CoursePoint(origin.e + (int)p[i] / 10.0, origin.n + (int)p[i + 1] / 10.0));
                        leg.routes.Add(route);
                    }
                    runner.legs.Add(leg);
                }
                return runner.legs.Count > 0;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("ContestReplay: unreadable replay: " + e.Message);
        }
        return false;
    }

    // ---- The view ----

    CourseData course;
    MapDefinition map;
    MapBackground background;
    bool routeChoice;
    List<Runner> runners;
    Action onClose;

    RectTransform viewport;
    RawImage picture;
    RectTransform overlay;
    float fitScale;        // picture pixels -> UI units when fitted
    float zoom = 1f;
    Vector2 areaCenterUV;
    RectTransform side;
    TextMeshProUGUI clockText;
    Slider slider;
    Button playButton, speedButton;
    TextMeshProUGUI legTitle;
    RectTransform legList;
    readonly List<UIRouteLine> legLines = new List<UIRouteLine>();
    float time, endTime;
    float speed = 1f;      // x1 = the race's own pace (20 game seconds per real second)
    bool playing = true;
    bool settingSlider;
    int legIndex;
    List<int> legNumbers = new List<int>();
    int raceLegs;          // race: the number of legs (start - 1 ... last - finish)
    int raceLeg;           // race: 0 = the whole course, else the leg shown
    TextMeshProUGUI raceLegTitle;
    TextMeshProUGUI legTimes;
    bool dragging;
    Vector2 dragFrom;
    bool routesOnly;       // race: every route drawn whole, no dots, the clock stopped
    Button routesButton;
    // Follow runner (2026-11-26, Anders: in every replay): the map keeps your runner (else the first shown one) in the
    // middle, zoomed in. Off: the whole course or leg fitted again.
    Button followButton;
    bool follow;
    const float FollowZoom = 4f;
    readonly List<(Runner runner, Image image)> runnerButtons = new List<(Runner, Image)>();

    // Any number of runners (2026-11-26, the user: no limit of 10): the palette first, then colours spread round the
    // colour wheel so neighbours still differ
    static Color ColourFor(int i)
    {
        if (i < Palette.Length - 1) return Palette[1 + i];
        float hue = (0.13f + (i - Palette.Length + 1) * 0.61803f) % 1f;
        float value = (i / 3) % 2 == 0 ? 0.78f : 0.60f;
        return Color.HSVToRGB(hue, 0.75f, value);
    }

    // routesOnly: the race's routes drawn whole at once, no replay (2026-11-26, the user: "se vägvalen utan att
    // behöva kolla på reprisen"); the Replay button starts the replay from there
    public static ContestReplay Open(CourseData course, bool routeChoice, List<Runner> runners, Action onClose, bool routesOnly = false)
    {
        GameObject go = new GameObject("Contest replay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        ContestReplay view = go.AddComponent<ContestReplay>();
        view.course = course;
        view.routeChoice = routeChoice;
        view.runners = runners;
        view.onClose = onClose;
        view.routesOnly = routesOnly;
        for (int i = 0; i < runners.Count; i++) runners[i].color = runners[i].me ? Palette[0] : ColourFor(i);
        view.Build();
        return view;
    }

    void Build()
    {
        map = MapDefinition.Find(course.mapId);
        background = map != null ? map.GetBackground(course.background) : null;

        Image sheet = CourseUI.Panel("Sheet", transform, CourseUI.CardSolid, false);
        CourseUI.Stretch(sheet.rectTransform);

        // The map, left of the side panel
        Image frame = CourseUI.Panel("Map", sheet.transform, CourseUI.Hex("DCE8D6"), false);
        frame.rectTransform.anchorMin = Vector2.zero;
        frame.rectTransform.anchorMax = Vector2.one;
        frame.rectTransform.offsetMin = new Vector2(0, routeChoice ? 0 : 110);
        frame.rectTransform.offsetMax = new Vector2(-480, 0);
        frame.gameObject.AddComponent<RectMask2D>();
        viewport = frame.rectTransform;
        picture = CourseUI.Rect("Picture", viewport).gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        picture.rectTransform.anchorMin = picture.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        picture.rectTransform.pivot = Vector2.zero;
        Sprite sprite = background != null ? MapPackages.LoadSprite(background) : null;
        if (sprite != null)
        {
            picture.texture = sprite.texture;
            Rect r = sprite.textureRect;
            picture.uvRect = new Rect(r.x / sprite.texture.width, r.y / sprite.texture.height, r.width / sprite.texture.width, r.height / sprite.texture.height);
        }
        overlay = CourseUI.Rect("Overlay", picture.rectTransform);
        CourseUI.Stretch(overlay);

        // The side panel
        Image panel = CourseUI.Panel("Side", sheet.transform, CourseUI.Card, false);
        panel.rectTransform.anchorMin = new Vector2(1, 0);
        panel.rectTransform.anchorMax = new Vector2(1, 1);
        panel.rectTransform.pivot = new Vector2(1, 0.5f);
        panel.rectTransform.sizeDelta = new Vector2(480, 0);
        panel.rectTransform.anchoredPosition = Vector2.zero;
        side = panel.rectTransform;
        TextMeshProUGUI title = CourseUI.Text("Title", side, routeChoice ? "Route choices" : "Replay", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -24), new Vector2(424, 56));
        TextMeshProUGUI sub = CourseUI.Text("Course", side, course.name + "\n" + CourseFilter.Facts(course), 20, CourseUI.InkMuted);
        CourseUI.Place(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -82), new Vector2(424, 64));
        Button close = CourseUI.Button("Close", side, "Close", CourseUI.ButtonKind.Light, Close, 24);
        CourseUI.Place((RectTransform)close.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(424, 62));

        if (sprite == null)
        {
            TextMeshProUGUI missing = CourseUI.Text("Missing", viewport, "The map of this course is not on this computer.", 30, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            CourseUI.Stretch(missing.rectTransform, 40, 40, 40, 40);
            return;
        }

        DrawCourse();
        if (routeChoice) BuildChoices(); else BuildRace();
        Fit();
    }

    // ---- Map placement ----

    Vector2 UV(CoursePoint p) => background.WorldToUV(map.ToWorld(p));
    Vector2 UV(Vector3 world) => background.WorldToUV(world);

    void Fit()
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        void Add(Vector2 uv) { min = Vector2.Min(min, uv); max = Vector2.Max(max, uv); }
        List<CoursePoint> all = course.AllPoints();
        if (raceLeg > 0 && raceLeg < all.Count)
        {
            // One leg: its two controls and everyone's way between them
            Add(UV(all[raceLeg - 1]));
            Add(UV(all[raceLeg]));
            foreach (Runner r in runners) for (int i = r.viewFrom; i <= r.viewTo && i < r.track.Count; i++) Add(UV(r.track[i]));
        }
        else
        {
            foreach (CoursePoint p in all) Add(UV(p));
            foreach (Runner r in runners)
            {
                foreach (CoursePoint p in r.track) Add(UV(p));
                foreach (ChoiceLeg l in r.legs) foreach (ChoiceRoute route in l.routes) foreach (CoursePoint p in route.points) Add(UV(p));
            }
        }
        Vector2 size = background.pixelSize;
        areaCenterUV = (min + max) / 2f;
        Rect view = viewport.rect;
        Vector2 extent = Vector2.Scale(max - min, size) * 1.15f + Vector2.one * 40f;
        fitScale = Mathf.Min(view.width / Mathf.Max(1f, extent.x), view.height / Mathf.Max(1f, extent.y));
        zoom = follow ? FollowZoom : 1f;
        Place(areaCenterUV);
    }

    // Puts the picture so the given point of it is in the middle of the map area
    void Place(Vector2 centerUV)
    {
        Vector2 size = background.pixelSize * fitScale * zoom;
        picture.rectTransform.sizeDelta = size;
        picture.rectTransform.anchoredPosition = -Vector2.Scale(centerUV, size);
    }

    Vector2 CenterUV()
    {
        Vector2 size = picture.rectTransform.sizeDelta;
        Vector2 pos = picture.rectTransform.anchoredPosition;
        return new Vector2(-pos.x / Mathf.Max(1f, size.x), -pos.y / Mathf.Max(1f, size.y));
    }

    void HandleInput()
    {
        if (background == null) return;
        Vector2 mouse = Input.mousePosition;
        bool overMap = RectTransformUtility.RectangleContainsScreenPoint(viewport, mouse, null);
        float scroll = Input.mouseScrollDelta.y;
        if (overMap && Mathf.Abs(scroll) > 0.01f)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(picture.rectTransform, mouse, null, out Vector2 before);
            Vector2 beforeUV = new Vector2(before.x / picture.rectTransform.sizeDelta.x, before.y / picture.rectTransform.sizeDelta.y);
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1.15f, scroll), 0.5f, 12f);
            Vector2 size = background.pixelSize * fitScale * zoom;
            picture.rectTransform.sizeDelta = size;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, mouse, null, out Vector2 mouseInView);
            picture.rectTransform.anchoredPosition = mouseInView - viewport.rect.center - Vector2.Scale(beforeUV, size);
        }
        if (overMap && !follow && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
        {
            dragging = true;
            dragFrom = mouse;
        }
        if (!(Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))) dragging = false;
        if (dragging)
        {
            Canvas canvas = GetComponent<Canvas>();
            float factor = canvas != null ? canvas.scaleFactor : 1f;
            picture.rectTransform.anchoredPosition += (mouse - dragFrom) / Mathf.Max(0.01f, factor);
            dragFrom = mouse;
        }
    }

    // ---- The course ----

    void DrawCourse()
    {
        DrawForbidden();
        Color purple = CourseRenderer.CoursePurple;
        List<CoursePoint> points = course.AllPoints();
        float size = CourseRenderer.SizeFor(map);
        float radius = 37f * size * MapDefinition.WorldUnitsPerMeter;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 center = map.ToWorld(points[i]);
            List<Vector2> ring = new List<Vector2>();
            bool start = i == 0;
            int corners = start ? 3 : 24;
            float angle0 = 0f;
            if (start && points.Count > 1)
            {
                Vector3 toward = map.ToWorld(points[1]) - center;
                angle0 = Mathf.Atan2(toward.y, toward.x);
            }
            for (int k = 0; k < corners; k++)
            {
                float a = angle0 + k * Mathf.PI * 2f / corners;
                ring.Add(UV(center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius * (start ? 1.3f : 1f)));
            }
            UIRouteLine.Make("Control " + i, overlay, purple, 3f).Set(ring, true);
            if (i == points.Count - 1 && i > 0)
            {
                List<Vector2> inner = new List<Vector2>();
                for (int k = 0; k < corners; k++)
                {
                    float a = k * Mathf.PI * 2f / corners;
                    inner.Add(UV(center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius * 0.7f));
                }
                UIRouteLine.Make("Finish", overlay, purple, 3f).Set(inner, true);
            }
            if (i > 0)
            {
                Vector3 from = map.ToWorld(points[i - 1]);
                Vector3 d = (center - from).normalized;
                if ((center - from).magnitude > radius * 2.4f)
                {
                    List<Vector2> legLine = new List<Vector2> { UV(from + d * radius * (i == 1 ? 1.3f : 1f)), UV(center - d * radius) };
                    UIRouteLine.Make("Leg " + i, overlay, purple, 2.5f).Set(legLine, false);
                }
            }
        }
    }

    // The course's out-of-bounds areas (the AI sprint barriers too), uncrossable boundaries and bridges, as in the race
    // (ForbiddenAreaRenderer): purple north-south stripes with a border, a thick purple line, two thin side lines.
    // Before 2026-11-26 the replay left them out, so the routes seemed to go round nothing (the user).
    void DrawForbidden()
    {
        if (!ForbiddenAreas.Any(course)) return;
        bool sprint = course.IsSprint() || map.IsSprint;
        float m = MapDefinition.WorldUnitsPerMeter;
        float spacing = (sprint ? 3.2f : 12f) * m;
        float half = spacing * 0.32f / 2f;
        float bridgeHalf = (sprint ? 2.2f : 7f) * m;
        Color purple = CourseRenderer.CoursePurple;
        List<Vector2[]> stripes = new List<Vector2[]>();
        List<(List<Vector2> points, bool loop, float width)> lines = new List<(List<Vector2>, bool, float)>();
        foreach (ForbiddenArea area in course.forbidden)
        {
            if (area == null || !area.IsValid) continue;
            List<Vector3> corners = area.corners.ConvertAll(c => map.ToWorld(c));
            if (area.bridge)
            {
                for (int k = 0; k + 1 < corners.Count; k++)
                {
                    Vector3 d = corners[k + 1] - corners[k];
                    if (d.sqrMagnitude < 1e-8f) continue;
                    Vector3 side = new Vector3(-d.y, d.x, 0f).normalized * bridgeHalf;
                    lines.Add((new List<Vector2> { UV(corners[k] + side), UV(corners[k + 1] + side) }, false, 2f));
                    lines.Add((new List<Vector2> { UV(corners[k] - side), UV(corners[k + 1] - side) }, false, 2f));
                }
                continue;
            }
            if (area.line)
            {
                lines.Add((corners.ConvertAll(c => UV(c)), false, 5f));
                continue;
            }
            lines.Add((corners.ConvertAll(c => UV(c)), true, 2f));
            // Stripes: at every step of the spacing across the area, the stretches of that north-south line inside it
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (Vector3 c in corners) { minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x); }
            List<float> ys = new List<float>();
            for (float x = Mathf.Ceil(minX / spacing) * spacing; x <= maxX; x += spacing)
            {
                ys.Clear();
                for (int k = 0; k < corners.Count; k++)
                {
                    Vector3 a = corners[k], b = corners[(k + 1) % corners.Count];
                    if ((a.x <= x && b.x > x) || (b.x <= x && a.x > x))
                        ys.Add(a.y + (x - a.x) / (b.x - a.x) * (b.y - a.y));
                }
                ys.Sort();
                for (int k = 0; k + 1 < ys.Count; k += 2)
                {
                    stripes.Add(new[]
                    {
                        UV(new Vector3(x - half, ys[k], 0f)), UV(new Vector3(x + half, ys[k], 0f)),
                        UV(new Vector3(x + half, ys[k + 1], 0f)), UV(new Vector3(x - half, ys[k + 1], 0f))
                    });
                }
                if (stripes.Count > 20000) break;   // a guard against broken data
            }
        }
        if (stripes.Count > 0) UIQuads.Make("Out-of-bounds stripes", overlay, purple).Set(stripes);
        for (int i = 0; i < lines.Count; i++) UIRouteLine.Make("Out-of-bounds " + i, overlay, purple, lines[i].width).Set(lines[i].points, lines[i].loop);
    }

    // ---- Race replay ----

    void BuildRace()
    {
        PrepareLegs();
        foreach (Runner r in runners)
        {
            r.viewFrom = 0;
            r.viewTo = Mathf.Min(r.times.Count, r.track.Count) - 1;
            r.viewOffset = 0f;
            if (r.times.Count > 0) endTime = Mathf.Max(endTime, r.times[r.times.Count - 1]);
            r.line = UIRouteLine.Make("Track " + r.name, overlay, r.color, r.me ? 5f : 4f);
            Image dot = CourseUI.Panel("Runner " + r.name, overlay, r.color);
            if (CourseUI.Skin != null && CourseUI.Skin.pill != null) dot.sprite = CourseUI.Skin.pill;
            dot.raycastTarget = false;
            Outline outline = dot.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(2, -2);
            r.dot = dot.rectTransform;
            r.dot.anchorMin = r.dot.anchorMax = Vector2.zero;
            r.dot.sizeDelta = new Vector2(22, 22);
            TextMeshProUGUI label = CourseUI.Text("Name", r.dot, r.name, 20, r.color, FontStyles.Bold, TextAlignmentOptions.Center);
            label.enableWordWrapping = false;
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.white;
            CourseUI.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0, 4), new Vector2(260, 28));
            r.dotLabel = label;
        }

        // The whole course or one leg at a time
        float top = -160f;
        if (raceLegs > 0)
        {
            RectTransform nav = CourseUI.Rect("Leg buttons", side);
            CourseUI.Place(nav, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, top), new Vector2(424, 56));
            CourseUI.Row(nav.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
            Button previous = CourseUI.Button("Previous", nav, "<", CourseUI.ButtonKind.Light, () => ShowRaceLeg(raceLeg - 1), 24);
            Button whole = CourseUI.Button("Leg", nav, "Whole course", CourseUI.ButtonKind.Light, () => ShowRaceLeg(raceLeg + 1), 21);
            Button next = CourseUI.Button("Next", nav, ">", CourseUI.ButtonKind.Light, () => ShowRaceLeg(raceLeg + 1), 24);
            CourseUI.Size(previous, 56, 70);
            CourseUI.Size(whole, 56, 264);
            CourseUI.Size(next, 56, 70);
            raceLegTitle = whole.GetComponentInChildren<TextMeshProUGUI>(true);
            HoverTip.Add(whole, "One leg at a time: everyone starts the leg together, so you can compare the route choices and the leg times. Arrow keys work too.");
            top -= 70f;
        }

        // Who is shown: Show all / Hide all, then any runner clicked on or off (2026-11-26, the user)
        RectTransform allRow = CourseUI.Rect("Show all or none", side);
        CourseUI.Place(allRow, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, top), new Vector2(424, 48));
        CourseUI.Row(allRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        Button showAll = CourseUI.Button("Show all", allRow, "Show all", CourseUI.ButtonKind.Light, () => ShowAll(true), 20);
        Button hideAll = CourseUI.Button("Hide all", allRow, "Hide all", CourseUI.ButtonKind.Light, () => ShowAll(false), 20);
        CourseUI.Size(showAll, 48, -1, 1);
        CourseUI.Size(hideAll, 48, -1, 1);
        HoverTip.Add(hideAll, "Hide everyone, then click the runners you want to see");
        top -= 58f;
        TextMeshProUGUI caption = CourseUI.Text("Runners caption", side, "Runners (click to show or hide)", 20, CourseUI.InkMuted, FontStyles.Bold);
        CourseUI.Place(caption.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, top), new Vector2(424, 30));
        legTimes = caption;
        RectTransform list = CourseUI.ScrollList("Runners", side, out ScrollRect listScroll);
        RectTransform listView = (RectTransform)listScroll.transform;
        listView.anchorMin = new Vector2(0, 0);
        listView.anchorMax = new Vector2(1, 1);
        listView.offsetMin = new Vector2(28, 110);
        listView.offsetMax = new Vector2(-28, top - 36);
        CourseUI.Column(list.gameObject, 8, new RectOffset(0, 0, 0, 0));
        FastScroll.Add(listScroll);
        foreach (Runner r in runners)
        {
            Runner runner = r;
            string label = (r.place > 0 ? r.place + ". " : "") + r.name + (r.times.Count > 0 ? "   " + CompetitionFlow.FormatTime(Mathf.RoundToInt(r.times[r.times.Count - 1])) : "");
            Button b = CourseUI.Button("Runner " + r.name, list, label, CourseUI.ButtonKind.Light, null, 22);
            CourseUI.Size(b, 52);
            r.listLabel = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (r.listLabel != null) r.listLabel.richText = true;
            Image swatch = CourseUI.Panel("Colour", b.transform, r.color);
            if (CourseUI.Skin != null && CourseUI.Skin.pill != null) swatch.sprite = CourseUI.Skin.pill;
            CourseUI.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(20, 20));
            Image look = b.GetComponent<Image>();
            runnerButtons.Add((runner, look));
            b.onClick.AddListener(() =>
            {
                runner.shown = !runner.shown;
                ShowRunnerButton(runner, look);
                Apply();
            });
            ShowRunnerButton(runner, look);
        }

        // The time bar under the map
        Image bar = CourseUI.Panel("Time bar", viewport.parent, CourseUI.Card, false);
        bar.rectTransform.anchorMin = new Vector2(0, 0);
        bar.rectTransform.anchorMax = new Vector2(1, 0);
        bar.rectTransform.pivot = new Vector2(0, 0);
        bar.rectTransform.offsetMin = new Vector2(0, 0);
        bar.rectTransform.offsetMax = new Vector2(-480, 110);
        HorizontalLayoutGroup row = CourseUI.Row(bar.gameObject, 14, new RectOffset(24, 24, 24, 24));
        row.childAlignment = TextAnchor.MiddleLeft;
        playButton = CourseUI.Button("Play", bar.transform, "Pause", CourseUI.ButtonKind.Secondary, TogglePlay, 22);
        CourseUI.Size(playButton, 60, 130);
        speedButton = CourseUI.Button("Speed", bar.transform, "Speed x" + speed, CourseUI.ButtonKind.Light, NextSpeed, 22);
        CourseUI.Size(speedButton, 60, 160);
        slider = CourseUI.SliderControl("Time", bar.transform, 0f, Mathf.Max(1f, endTime), 0f, false, v =>
        {
            if (settingSlider) return;
            if (routesOnly)
            {
                // Dragging the time bar goes back to the replay, from where it was dragged to
                routesOnly = false;
                CourseUI.SetButtonText(routesButton, "Routes only");
                CourseUI.SetButtonColor(routesButton, CourseUI.ButtonKind.Light);
                if (followButton != null) followButton.gameObject.SetActive(true);
            }
            time = v;
            Apply();
        });
        CourseUI.Size(slider, 60, -1, 1);
        clockText = CourseUI.Text("Clock", bar.transform, "", 28, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(clockText, 60, 150);
        followButton = CourseUI.Button("Follow runner", bar.transform, "Follow runner: OFF", CourseUI.ButtonKind.Light, () => SetFollow(!follow), 20);
        CourseUI.Size(followButton, 60, 250);
        HoverTip.Add(followButton, "The map follows your runner, zoomed in. Off: the whole course or leg on the screen.");
        routesButton = CourseUI.Button("Routes", bar.transform, "", CourseUI.ButtonKind.Light, () => SetRoutesOnly(!routesOnly), 22);
        CourseUI.Size(routesButton, 60, 190);
        HoverTip.Add(routesButton, "Routes only: everyone's whole route at once, without the replay. Works for the whole course and for one leg.");
        SetRoutesOnly(routesOnly);
    }

    static void ShowRunnerButton(Runner runner, Image look)
    {
        if (look != null) look.color = runner.shown ? CourseUI.Soft : new Color(1, 1, 1, 0.3f);
    }

    void ShowAll(bool shown)
    {
        foreach (var (runner, look) in runnerButtons)
        {
            runner.shown = shown;
            ShowRunnerButton(runner, look);
        }
        Apply();
    }

    // Routes only: the clock at the end and stopped, so every route is drawn whole; off again: the replay from the start
    void SetRoutesOnly(bool on)
    {
        routesOnly = on;
        if (routesButton != null) CourseUI.SetButtonText(routesButton, on ? "Replay" : "Routes only");
        if (routesButton != null) CourseUI.SetButtonColor(routesButton, on ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        if (on && follow) SetFollow(false);
        if (followButton != null) followButton.gameObject.SetActive(!on);
        time = on ? endTime : 0f;
        playing = !on;
        if (playButton != null) CourseUI.SetButtonText(playButton, playing ? "Pause" : "Play");
        Apply();
    }

    void SetFollow(bool on)
    {
        follow = on;
        if (followButton != null)
        {
            CourseUI.SetButtonText(followButton, on ? "Follow runner: ON" : "Follow runner: OFF");
            CourseUI.SetButtonColor(followButton, on ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        }
        if (background == null) return;
        if (on) zoom = Mathf.Max(zoom, FollowZoom);
        else Fit();
        FollowCenter();
    }

    // The map is put so the followed runner is in the middle (your own, else the first one shown)
    void FollowCenter()
    {
        if (!follow || background == null) return;
        Runner who = null;
        foreach (Runner r in runners) if (r.shown && r.dot != null && r.dot.gameObject.activeSelf && r.me) { who = r; break; }
        if (who == null) foreach (Runner r in runners) if (r.shown && r.dot != null && r.dot.gameObject.activeSelf) { who = r; break; }
        if (who == null) return;
        Place(who.dot.anchorMin);
    }

    void TogglePlay()
    {
        if (routesOnly)
        {
            SetRoutesOnly(false);
            return;
        }
        if (!playing && time >= endTime) time = 0f;
        playing = !playing;
        CourseUI.SetButtonText(playButton, playing ? "Pause" : "Play");
    }

    void NextSpeed()
    {
        speed = speed >= 16f ? 1f : speed * 2f;
        CourseUI.SetButtonText(speedButton, "Speed x" + speed);
    }

    void Apply()
    {
        if (routeChoice || background == null) return;
        foreach (Runner r in runners)
        {
            float clock = time + r.viewOffset;
            int last = r.viewFrom - 1;     // the last point reached
            while (last + 1 <= r.viewTo && r.times[last + 1] <= clock) last++;
            int count = last - r.viewFrom + 1;
            List<Vector2> shown = new List<Vector2>(Mathf.Max(0, count) + 1);
            for (int i = 0; i < count; i++) shown.Add(UV(r.track[r.viewFrom + i]));
            bool any = r.viewTo >= r.viewFrom;
            Vector2 at = count > 0 ? shown[count - 1] : (any ? UV(r.track[r.viewFrom]) : Vector2.zero);
            if (count > 0 && last < r.viewTo)
            {
                float span = r.times[last + 1] - r.times[last];
                float f = span > 0f ? (clock - r.times[last]) / span : 0f;
                at = Vector2.Lerp(at, UV(r.track[last + 1]), Mathf.Clamp01(f));
                shown.Add(at);
            }
            r.line.gameObject.SetActive(r.shown);
            r.line.Set(shown, false);
            r.dot.gameObject.SetActive(r.shown && any && !(routesOnly && time >= endTime));
            r.dot.anchorMin = r.dot.anchorMax = at;
            r.dot.anchoredPosition = Vector2.zero;
            if (r.dotLabel != null)
            {
                float leg = raceLeg > 0 && r.legSeconds != null ? r.legSeconds[raceLeg] : 0f;
                r.dotLabel.text = r.name + (raceLeg > 0 && any && last >= r.viewTo && leg > 0 ? "  " + CompetitionFlow.FormatTime(Mathf.RoundToInt(leg)) : "");
            }
        }
        if (clockText != null) clockText.text = CompetitionFlow.FormatTime(Mathf.RoundToInt(time));
        if (slider != null)
        {
            settingSlider = true;
            slider.value = time;
            settingSlider = false;
        }
    }

    // ---- Race legs ----

    // Where each runner reached each control: from the times the game saved with the replay, or (older replays) where
    // the track came closest to the control
    void PrepareLegs()
    {
        List<CoursePoint> controls = course.AllPoints();
        raceLegs = controls.Count - 1;
        if (raceLegs < 1) { raceLegs = 0; return; }
        foreach (Runner r in runners)
        {
            int n = Mathf.Min(r.times.Count, r.track.Count);
            r.ends = new int[raceLegs + 1];
            r.legSeconds = new float[raceLegs + 1];
            int from = 0;
            for (int k = 1; k <= raceLegs; k++)
            {
                int end;
                if (r.controlTimes.Count >= raceLegs)
                {
                    float at = r.controlTimes[k - 1];
                    end = from;
                    while (end + 1 < n && r.times[end + 1] <= at + 0.05f) end++;
                }
                else end = Nearest(r, controls[k], from, n);
                r.ends[k] = end;
                if (end > from && end < n) r.legSeconds[k] = r.times[end] - r.times[from];
                from = end;
            }
        }
    }

    // The first time the track comes within 0.35 of the control (as RouteCompareView does), or else its closest point
    int Nearest(Runner r, CoursePoint control, int from, int n)
    {
        Vector3 target = map.ToWorld(control);
        int hit = -1, closest = from;
        float best = float.MaxValue;
        for (int i = from; i < n; i++)
        {
            float d = Vector2.Distance(map.ToWorld(r.track[i]), target);
            if (d < best) { best = d; closest = i; }
            if (d <= 0.35f) { hit = i; break; }
        }
        if (hit < 0) return closest;
        best = Vector2.Distance(map.ToWorld(r.track[hit]), target);
        for (int i = hit + 1; i < n; i++)
        {
            float d = Vector2.Distance(map.ToWorld(r.track[i]), target);
            if (d > 0.35f) break;
            if (d < best) { best = d; hit = i; }
        }
        return hit;
    }

    void ShowRaceLeg(int leg)
    {
        if (raceLegs <= 0) return;
        raceLeg = ((leg % (raceLegs + 1)) + raceLegs + 1) % (raceLegs + 1);
        endTime = 0f;
        foreach (Runner r in runners)
        {
            int n = Mathf.Min(r.times.Count, r.track.Count);
            if (raceLeg == 0)
            {
                r.viewFrom = 0;
                r.viewTo = n - 1;
                r.viewOffset = 0f;
            }
            else
            {
                r.viewFrom = r.ends[raceLeg - 1];
                r.viewTo = r.ends[raceLeg] > r.viewFrom ? r.ends[raceLeg] : r.viewFrom - 1;
                r.viewOffset = r.viewFrom < n ? r.times[r.viewFrom] : 0f;
            }
            if (r.viewTo >= r.viewFrom && r.viewTo < n) endTime = Mathf.Max(endTime, r.times[r.viewTo] - r.viewOffset);
        }
        List<CoursePoint> controls = course.AllPoints();
        if (raceLegTitle != null)
            raceLegTitle.text = raceLeg == 0 ? "Whole course" : "Leg " + raceLeg + ": " + (raceLeg == 1 ? "Start" : (raceLeg - 1).ToString()) + " - " + (raceLeg == raceLegs ? "Finish" : raceLeg.ToString());

        // The runners' list: total time, or the time on the leg with the place and the gap to the fastest
        List<Runner> order = runners.FindAll(r => raceLeg == 0 || (r.legSeconds != null && r.legSeconds[raceLeg] > 0));
        if (raceLeg > 0) order.Sort((a, b) => a.legSeconds[raceLeg].CompareTo(b.legSeconds[raceLeg]));
        float best = raceLeg > 0 && order.Count > 0 ? order[0].legSeconds[raceLeg] : 0f;
        foreach (Runner r in runners)
        {
            if (r.listLabel == null) continue;
            if (raceLeg == 0)
            {
                r.listLabel.text = (r.place > 0 ? r.place + ". " : "") + r.name + (r.times.Count > 0 ? "   " + CompetitionFlow.FormatTime(Mathf.RoundToInt(r.times[r.times.Count - 1])) : "");
                continue;
            }
            int index = order.IndexOf(r);
            float seconds = r.legSeconds[raceLeg];
            r.listLabel.text = index < 0 ? r.name + "   <color=#5B7563>no time</color>"
                : (index + 1) + ". " + r.name + "   <b>" + CompetitionFlow.FormatTime(Mathf.RoundToInt(seconds)) + "</b>"
                  + (index > 0 ? "  <color=#5B7563>+" + RelayStats.Gap(Mathf.RoundToInt(seconds - best)) + "</color>" : "");
        }
        if (legTimes != null) legTimes.text = raceLeg == 0 ? "Runners (click to show or hide)" : "Time on the leg (click to show or hide)";
        if (slider != null) slider.maxValue = Mathf.Max(1f, endTime);
        time = routesOnly ? endTime : 0f;
        playing = !routesOnly;
        if (playButton != null) CourseUI.SetButtonText(playButton, playing ? "Pause" : "Play");
        Fit();
        Apply();
    }

    // ---- Route choices ----

    void BuildChoices()
    {
        SortedSet<int> numbers = new SortedSet<int>();
        foreach (Runner r in runners) foreach (ChoiceLeg l in r.legs) numbers.Add(l.leg);
        legNumbers = new List<int>(numbers);

        RectTransform nav = CourseUI.Rect("Leg buttons", side);
        CourseUI.Place(nav, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -160), new Vector2(424, 60));
        CourseUI.Row(nav.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        Button previous = CourseUI.Button("Previous", nav, "<  Leg", CourseUI.ButtonKind.Light, () => ShowLeg(legIndex - 1), 22);
        legTitle = CourseUI.Text("Leg", nav, "", 26, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        Button next = CourseUI.Button("Next", nav, "Leg  >", CourseUI.ButtonKind.Light, () => ShowLeg(legIndex + 1), 22);
        CourseUI.Size(previous, 60, 130);
        CourseUI.Size(legTitle, 60, 140);
        CourseUI.Size(next, 60, 130);

        legList = CourseUI.Rect("Picks", side);
        CourseUI.Place(legList, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -236), new Vector2(424, 640));
        CourseUI.Column(legList.gameObject, 6, new RectOffset(0, 0, 0, 0));

        if (legNumbers.Count == 0)
        {
            legTitle.text = "No legs";
            return;
        }
        ShowLeg(0);
    }

    void ShowLeg(int index)
    {
        if (legNumbers.Count == 0) return;
        legIndex = (index % legNumbers.Count + legNumbers.Count) % legNumbers.Count;
        int number = legNumbers[legIndex];
        legTitle.text = "Leg " + number;
        foreach (UIRouteLine line in legLines) if (line != null) Destroy(line.gameObject);
        legLines.Clear();
        foreach (Transform child in legList) Destroy(child.gameObject);

        // The routes of this leg, from whoever has it (they are the same for everyone)
        ChoiceLeg shownLeg = null;
        foreach (Runner r in runners)
        {
            shownLeg = r.legs.Find(l => l.leg == number);
            if (shownLeg != null) break;
        }
        if (shownLeg == null) return;
        List<CoursePoint> controls = course.AllPoints();
        for (int i = 0; i < shownLeg.routes.Count; i++)
        {
            ChoiceRoute route = shownLeg.routes[i];
            bool fastest = i == shownLeg.fastest;
            List<Vector2> uv = new List<Vector2>();
            if (number - 1 >= 0 && number - 1 < controls.Count) uv.Add(UV(controls[number - 1]));
            foreach (CoursePoint p in route.points) uv.Add(UV(p));
            if (number < controls.Count) uv.Add(UV(controls[number]));
            Color colour = RouteChoiceGame.Colours[Mathf.Clamp(route.colour, 0, RouteChoiceGame.Colours.Length - 1)];
            UIRouteLine edge = UIRouteLine.Make("Edge " + i, overlay, new Color(0.08f, 0.08f, 0.08f, 0.6f), fastest ? 10f : 7f);
            edge.Set(uv, false);
            UIRouteLine line = UIRouteLine.Make("Route " + i, overlay, colour, fastest ? 7f : 4.5f);
            line.Set(uv, false);
            if (fastest) { edge.transform.SetAsLastSibling(); line.transform.SetAsLastSibling(); }
            legLines.Add(edge);
            legLines.Add(line);
        }

        // The routes, fastest first, with how much slower each is and who picked it
        List<int> order = new List<int>();
        for (int i = 0; i < shownLeg.routes.Count; i++) order.Add(i);
        order.Sort((a, b) => shownLeg.routes[a].seconds.CompareTo(shownLeg.routes[b].seconds));
        float best = shownLeg.routes[order[0]].seconds;
        foreach (int i in order)
        {
            ChoiceRoute route = shownLeg.routes[i];
            string colourName = RouteChoiceGame.ColourNames[Mathf.Clamp(route.colour, 0, RouteChoiceGame.ColourNames.Length - 1)];
            string head = "<b>" + colourName + "</b>  " + (i == shownLeg.fastest ? "fastest" : "+" + RelayStats.Gap(route.seconds - best));
            List<string> who = new List<string>();
            foreach (Runner r in runners)
            {
                ChoiceLeg theirs = r.legs.Find(l => l.leg == number);
                if (theirs != null && theirs.pick == i) who.Add(r.name + (theirs.points > 0 ? " (+" + theirs.points + ")" : ""));
            }
            AddPickRow(RouteChoiceGame.Colours[Mathf.Clamp(route.colour, 0, RouteChoiceGame.Colours.Length - 1)], head,
                who.Count > 0 ? string.Join(", ", who) : "nobody");
        }
        List<string> late = new List<string>();
        foreach (Runner r in runners)
        {
            ChoiceLeg theirs = r.legs.Find(l => l.leg == number);
            if (theirs != null && theirs.pick < 0) late.Add(r.name);
        }
        if (late.Count > 0) AddPickRow(CourseUI.InkMuted, "<b>Time ran out</b>", string.Join(", ", late));
    }

    void AddPickRow(Color colour, string head, string who)
    {
        Image row = CourseUI.Panel("Pick", legList, CourseUI.Soft);
        CourseUI.Size(row, 96);
        Image swatch = CourseUI.Panel("Colour", row.transform, colour);
        CourseUI.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(12, 72));
        TextMeshProUGUI text = CourseUI.Text("Text", row.transform, head + "\n<size=19>" + who + "</size>", 23, CourseUI.Ink);
        text.richText = true;
        // Many runners on one route (no limit since 2026-11-26): the names shrink to fit
        text.enableAutoSizing = true;
        text.fontSizeMin = 11;
        text.fontSizeMax = 23;
        CourseUI.Stretch(text.rectTransform, 36, 12, 8, 8);
    }

    // ---- Every frame ----

    void Update()
    {
        HandleInput();
        if (routeChoice)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow)) ShowLeg(legIndex + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) ShowLeg(legIndex - 1);
        }
        else if (raceLegs > 0 && Input.GetKeyDown(KeyCode.RightArrow)) ShowRaceLeg(raceLeg + 1);
        else if (raceLegs > 0 && Input.GetKeyDown(KeyCode.LeftArrow)) ShowRaceLeg(raceLeg - 1);
        if (!routeChoice && playing && background != null)
        {
            time += Time.unscaledDeltaTime * PackRunning.GameSecondsPerSecond * speed;
            if (time >= endTime)
            {
                time = endTime;
                playing = false;
                CourseUI.SetButtonText(playButton, "Play");
            }
            Apply();
        }
        FollowCenter();
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    public void Close()
    {
        Destroy(gameObject);
        onClose?.Invoke();
    }
}
