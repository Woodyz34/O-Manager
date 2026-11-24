using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The world ranking, opened from the main menu (2026-11-24, the user: "vem är den bästa spelaren i O-Manager").
// The best players of the weekly and daily contests of the last 30 days: overall first (the average of the route
// choice game, the race and the 3D run), then each game, and each game on forest or sprint maps. Place, points, the
// gap to the one ahead and how many places a player moved since yesterday. Clicking a player shows a line chart of
// their points and places day by day. The points are worked out by the Cloud Code script (Contests.js,
// "World ranking"); the page only shows them.
public class RankingPage : MonoBehaviour
{
    static RankingPage open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { open = null; }

    static readonly string[] Games = { "overall", "rc", "race", "run" };
    static readonly string[] GameNames = { "Overall", "Route choice game", "Race", "Run it yourself (3D)" };
    static readonly string[] Maps = { "", "forest", "sprint" };
    static readonly string[] MapNames = { "All maps", "Forest", "Sprint" };

    static readonly Color Gold = CourseUI.Hex("C9A227");
    static readonly Color Silver = CourseUI.Hex("8E9AA6");
    static readonly Color Bronze = CourseUI.Hex("B0703C");
    static readonly Color Up = CourseUI.Hex("3F7D54");
    static readonly Color Down = CourseUI.Hex("C0392B");
    static readonly Color LineRc = CourseUI.Hex("3F7D54");
    static readonly Color LineRace = CourseUI.Hex("2E6DA4");
    static readonly Color LineRun = CourseUI.Hex("8E44AD");

    int game;      // index in Games
    int map;       // index in Maps (not for overall)
    RankingData data;
    string error;
    bool busy;

    TextMeshProUGUI info;
    Button[] gameTabs;
    Button[] mapTabs;
    RectTransform mapRow;
    RectTransform list;
    ScrollRect listScroll;
    TextMeshProUGUI listHeading;
    TextMeshProUGUI footer;
    TextMeshProUGUI message;
    GameObject popup;

    public static void Open()
    {
        if (open != null) return;
        GameObject go = new GameObject("Ranking page", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<RankingPage>();
        open.Build();
        open.Reload();
    }

    void OnDestroy()
    {
        if (open == this) open = null;
    }

    public void Close() => Destroy(gameObject);

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (popup != null) Destroy(popup);
        else Close();
    }

    string ListKey => game == 0 ? "overall" : Games[game] + (map == 0 ? "" : "-" + Maps[map]);

    string ListName
    {
        get
        {
            if (game == 0) return "Overall";
            return GameNames[game] + (map == 0 ? "" : " · " + MapNames[map]);
        }
    }

    // ---- Page ----

    void Build()
    {
        Image back = CourseUI.Panel("Background", transform, CourseUI.Hex("DCE8D6"), false);
        CourseUI.Stretch(back.rectTransform);
        Sprite forest = Resources.Load<Sprite>("Sprites/e2bf7ebc-02a1-4b78-b52f-e0211eeac746");
        if (forest != null)
        {
            back.gameObject.AddComponent<RectMask2D>();
            Image picture = CourseUI.Panel("Forest", back.transform, Color.white, false);
            picture.sprite = forest;
            picture.raycastTarget = false;
            AspectRatioFitter fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = forest.rect.width / Mathf.Max(1f, forest.rect.height);
        }
        Transform page = back.transform;

        Button leave = CourseUI.Button("Back", page, "Back to menu", CourseUI.ButtonKind.Light, Close, 26);
        CourseUI.Place((RectTransform)leave.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48, -40), new Vector2(250, 66));

        // Header: the name, what it is and when it was updated, and how the points work
        Image head = CourseUI.CardPanel("Header", page);
        CourseUI.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(1180, 150));
        TextMeshProUGUI title = CourseUI.Text("Title", head.transform, "World Ranking", 46, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32, -16), new Vector2(700, 62));
        info = CourseUI.Text("Info", head.transform, "", 21, CourseUI.InkMuted);
        info.richText = true;
        CourseUI.Place(info.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32, 12), new Vector2(860, 62));
        Button how = CourseUI.Button("How", head.transform, "How it works", CourseUI.ButtonKind.Secondary, ShowRules, 22);
        CourseUI.Place((RectTransform)how.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-28, 0), new Vector2(230, 58));
        HoverTip.Add(how, "How the ranking points are worked out");

        // The game tabs, and under them forest or sprint (not for overall)
        RectTransform gameRow = CourseUI.Rect("Games", page);
        CourseUI.Place(gameRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -202), new Vector2(1180, 58));
        CourseUI.Row(gameRow.gameObject, 14, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        gameTabs = new Button[Games.Length];
        for (int i = 0; i < Games.Length; i++)
        {
            int index = i;
            gameTabs[i] = CourseUI.Button(Games[i], gameRow, GameNames[i], CourseUI.ButtonKind.Light, () => { game = index; Fill(); }, 23);
        }
        HoverTip.Add(gameTabs[0], "The average of the route choice game, the race and the 3D run: the best all-round player");

        mapRow = CourseUI.Rect("Maps", page);
        CourseUI.Place(mapRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -272), new Vector2(660, 50));
        CourseUI.Row(mapRow.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        mapTabs = new Button[Maps.Length];
        for (int i = 0; i < Maps.Length; i++)
        {
            int index = i;
            mapTabs[i] = CourseUI.Button(MapNames[i], mapRow, MapNames[i], CourseUI.ButtonKind.Light, () => { map = index; Fill(); }, 21);
        }

        // The list
        Image card = CourseUI.CardPanel("List", page);
        card.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        card.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        card.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        card.rectTransform.offsetMin = new Vector2(-590, 30);
        card.rectTransform.offsetMax = new Vector2(590, -336);
        listHeading = CourseUI.Text("Heading", card.transform, "", 26, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(listHeading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -12), new Vector2(1120, 38));

        RectTransform columns = CourseUI.Rect("Columns", card.transform);
        columns.anchorMin = new Vector2(0, 1);
        columns.anchorMax = new Vector2(1, 1);
        columns.pivot = new Vector2(0.5f, 1);
        columns.offsetMin = new Vector2(20, -82);
        columns.offsetMax = new Vector2(-20, -52);
        Column(columns, "#", ColPlace, 60, TextAlignmentOptions.Center);
        Column(columns, "PLAYER", ColName, 400, TextAlignmentOptions.Left);
        Column(columns, "CONTESTS", ColCount, ColCountWidth, TextAlignmentOptions.Center);
        Column(columns, "POINTS", ColPoints, ColPointsWidth, TextAlignmentOptions.Center);
        Column(columns, "GAP", ColGap, ColGapWidth, TextAlignmentOptions.Center);

        list = CourseUI.ScrollList("Rows", card.transform, out listScroll);
        RectTransform viewport = (RectTransform)listScroll.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(20, 70);
        viewport.offsetMax = new Vector2(-20, -88);
        CourseUI.Column(list.gameObject, 6, new RectOffset(0, 0, 0, 6));
        listScroll.scrollSensitivity = 60f;
        FastScroll.Add(listScroll)?.OnLightCard();

        footer = CourseUI.Text("You", card.transform, "", 22, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Center);
        footer.richText = true;
        CourseUI.Place(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 14), new Vector2(1120, 44));

        message = CourseUI.Text("Message", card.transform, "", 28, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        message.richText = true;
        CourseUI.Stretch(message.rectTransform, 80, 80, 140, 140);
        Fill();
    }

    // Column positions in a row (x from the left edge, width)
    const float ColPlace = 8, ColMove = 72, ColName = 150, ColCount = 600, ColCountWidth = 140, ColPoints = 760,
                ColPointsWidth = 160, ColGap = 940, ColGapWidth = 190;

    static void Column(RectTransform parent, string text, float x, float width, TextAlignmentOptions align)
    {
        TextMeshProUGUI label = CourseUI.Text(text, parent, text, 16, CourseUI.InkMuted, FontStyles.Bold, align);
        label.characterSpacing = 6;
        PlaceInRow(label.rectTransform, x, width);
    }

    static void PlaceInRow(RectTransform rect, float x, float width, float top = 0, float bottom = 0)
    {
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 0.5f);
        rect.offsetMin = new Vector2(x, bottom);
        rect.offsetMax = new Vector2(x + width, -top);
    }

    // ---- Loading ----

    async void Reload()
    {
        busy = true;
        error = null;
        Fill();
        try
        {
            data = await ContestCloud.RankingAsync();
        }
        catch (Exception e)
        {
            error = e.Message;
        }
        if (this == null) return;
        busy = false;
        Fill();
    }

    // ---- The list ----

    void Fill()
    {
        if (list == null) return;
        for (int i = 0; i < gameTabs.Length; i++) CourseUI.SetButtonColor(gameTabs[i], i == game ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        for (int i = 0; i < mapTabs.Length; i++) CourseUI.SetButtonColor(mapTabs[i], i == map ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
        mapRow.gameObject.SetActive(game != 0);
        foreach (Transform child in list) Destroy(child.gameObject);
        listHeading.text = ListName;
        footer.text = "";

        int days = data != null && data.rules != null && data.rules.days > 0 ? data.rules.days : 30;
        info.text = "The best players in the weekly and daily contests of the last " + days + " days.\n" +
                    (data != null && !string.IsNullOrEmpty(data.day)
                        ? "Updated every day when the daily contest is over. Last: <b>" + DayText(data.day, true) + "</b>"
                        : "Updated every day when the daily contest is over.");

        if (busy) { message.text = "Loading the ranking..."; return; }
        if (error != null) { message.text = "<color=#C0392B>" + error + "</color>"; return; }
        List<RankRow> rows = data != null && data.lists.TryGetValue(ListKey, out List<RankRow> found) ? found : null;
        if (rows == null || rows.Count == 0)
        {
            message.text = "Nobody in this list yet.\n<size=80%>Run the weekly and daily contests: a contest counts here when it is over and at least two players finished it.</size>";
            ShowMine();
            return;
        }
        message.text = "";
        float leader = rows[0].pts;
        for (int i = 0; i < rows.Count; i++)
        {
            float ahead = i > 0 ? rows[i - 1].pts : rows[i].pts;
            AddRow(rows[i], ahead, leader);
        }
        listScroll.verticalNormalizedPosition = 1f;
        ShowMine();
    }

    void ShowMine()
    {
        if (data == null) return;
        int count = data.counts.TryGetValue(ListKey, out int n) ? n : 0;
        if (data.mine.TryGetValue(ListKey, out RankRow mine) && mine != null)
        {
            footer.text = "You: <b>" + CompetitionFlow.Ordinal(mine.place) + "</b> of " + count + "   ·   <b>" + Pts(mine.pts) + "</b> points   ·   "
                          + mine.c + (mine.c == 1 ? " contest" : " contests") + MoveText(mine, true);
        }
        else
        {
            footer.text = "<color=#5B7563>You are not in this list yet: finish a contest of this kind and you are in it the day after it ends.</color>";
        }
    }

    void AddRow(RankRow r, float ahead, float leader)
    {
        bool me = data != null && r.p == data.me;
        Image row = CourseUI.Panel("Row", list, me ? CourseUI.Hex("FCE3CF") : CourseUI.Soft);
        CourseUI.Size(row, 68);
        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = row;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.93f, 0.93f, 0.93f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f);
        button.colors = colors;
        RankRow captured = r;
        button.onClick.AddListener(() => OpenPlayer(captured));

        Color placeColor = r.place == 1 ? Gold : r.place == 2 ? Silver : r.place == 3 ? Bronze : CourseUI.Ink;
        TextMeshProUGUI place = CourseUI.Text("Place", row.transform, r.place.ToString(), r.place <= 3 ? 32 : 28, placeColor, FontStyles.Bold, TextAlignmentOptions.Center);
        PlaceInRow(place.rectTransform, ColPlace, 60);

        TextMeshProUGUI move = CourseUI.Text("Move", row.transform, MoveText(r, false), 18, CourseUI.InkMuted, FontStyles.Bold, TextAlignmentOptions.Left);
        move.richText = true;
        PlaceInRow(move.rectTransform, ColMove, 74);

        TextMeshProUGUI name = CourseUI.Text("Name", row.transform, (r.n ?? "Runner") + (me ? "  <color=#E8742A><size=80%>(you)</size></color>" : ""), 25, CourseUI.Ink, FontStyles.Bold);
        name.richText = true;
        name.enableWordWrapping = false;
        PlaceInRow(name.rectTransform, ColName, 440, 6, 30);
        TextMeshProUGUI sub = CourseUI.Text("Sub", row.transform, SubText(r), 17, CourseUI.InkMuted);
        sub.enableWordWrapping = false;
        PlaceInRow(sub.rectTransform, ColName, 440, 38, 4);

        TextMeshProUGUI count = CourseUI.Text("Count", row.transform, r.c.ToString(), 24, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Center);
        PlaceInRow(count.rectTransform, ColCount, ColCountWidth);

        TextMeshProUGUI points = CourseUI.Text("Points", row.transform, Pts(r.pts), 28, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        PlaceInRow(points.rectTransform, ColPoints, ColPointsWidth);

        string gapText = r.place == 1 ? "<color=#5B7563>leader</color>"
            : "<b>-" + Pts(ahead - r.pts) + "</b>\n<size=70%><color=#5B7563>-" + Pts(leader - r.pts) + " to 1st</color></size>";
        TextMeshProUGUI gap = CourseUI.Text("Gap", row.transform, gapText, 22, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Center);
        gap.richText = true;
        gap.lineSpacing = -12;
        PlaceInRow(gap.rectTransform, ColGap, ColGapWidth);
        HoverTip.Add(button, "Click to see how " + (r.n ?? "this player") + " has done over time");
    }

    // Overall: the three games' points; a game list: the plain average of the contests
    static string SubText(RankRow r)
    {
        if (r.parts != null && r.parts.Length >= 3)
            return "Route choice " + Pts(r.parts[0]) + "   ·   Race " + Pts(r.parts[1]) + "   ·   3D " + Pts(r.parts[2]);
        return "Average " + Pts(r.avg) + " per contest";
    }

    // "+3" green or "-2" red: places moved since the update before; "new" for a player not in the list then
    static string MoveText(RankRow r, bool sentence)
    {
        if (r.was <= 0)
            return sentence ? "   ·   <color=#3F7D54>new in the list</color>" : "<color=#3F7D54>new</color>";
        int moved = r.was - r.place;
        if (moved == 0) return sentence ? "   ·   same place as yesterday" : "<color=#5B7563>=</color>";
        string text = (moved > 0 ? "+" : "-") + Mathf.Abs(moved);
        string hex = ColorUtility.ToHtmlStringRGB(moved > 0 ? Up : Down);
        return sentence ? "   ·   <color=#" + hex + ">" + text + (Mathf.Abs(moved) == 1 ? " place" : " places") + "</color> since yesterday"
                        : "<color=#" + hex + ">" + text + "</color>";
    }

    static string Pts(float points) => points.ToString("0.0", CultureInfo.InvariantCulture);

    // "23 Nov" (with the year when asked, or when it is not this year)
    static string DayText(string day, bool withYear = false)
    {
        if (!DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)) return day;
        bool year = withYear || date.Year != DateTime.Now.Year;
        return date.ToString(year ? "d MMM yyyy" : "d MMM", CultureInfo.InvariantCulture);
    }

    // ---- How it works ----

    void ShowRules()
    {
        RankRules rules = data != null && data.rules != null ? data.rules : new RankRules { days = 30, weekly = 2, daily = 1, full = 4, minFinishers = 2 };
        string text =
            "<b>Points in a contest.</b> Every weekly and daily contest that is over gives each player who finished it points. " +
            "In the race and the 3D run: <b>100 × the winner's time / your time</b>, so the winner gets 100 and twice the winner's time gives 50. " +
            "In the route choice game: <b>100 × your points / the winner's points</b>. So how close you were counts, not only your place. " +
            "A contest counts when at least " + rules.minFinishers + " players finished it.\n\n" +
            "<b>Your points in a list</b> are your average over the last " + rules.days + " days, where a weekly contest counts " +
            (rules.weekly == 2 * rules.daily ? "twice" : rules.weekly + " times") + " as much as a daily one. " +
            "Until you have " + rules.full + " daily contests' worth (or " + (rules.full / Mathf.Max(1, rules.weekly)) + " weekly) the missing ones count as 0, " +
            "so one lucky win is not enough to top the list.\n\n" +
            "<b>Overall</b> is the average of your route choice game, race and 3D points. A game you have not played counts as 0, so the best all-round player is at the top.\n\n" +
            "<b>Updated</b> once a day, when the daily contest is over (3 hours after midnight, Swedish time, when late runs are in). " +
            "<color=#3F7D54>+2</color> / <color=#C0392B>-1</color> is how many places a player moved since the update before.\n\n" +
            "Click a player to see their points and places day by day.";
        GameObject go = MakePopup("How it works", new Vector2(1180, 760), out Transform card);
        TextMeshProUGUI body = CourseUI.Text("Body", card, text, 23, CourseUI.Ink);
        body.richText = true;
        CourseUI.Stretch(body.rectTransform, 44, 44, 96, 40);
        popup = go;
    }

    GameObject MakePopup(string heading, Vector2 size, out Transform card)
    {
        if (popup != null) Destroy(popup);
        GameObject go = new GameObject("Ranking popup", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        CourseUI.Stretch((RectTransform)go.transform);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 140;
        Image dim = CourseUI.Panel("Dim", go.transform, new Color(0.05f, 0.1f, 0.07f, 0.6f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(() => Destroy(go));
        Image panel = CourseUI.CardPanel("Card", dim.transform);
        panel.color = CourseUI.CardSolid;
        panel.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        TextMeshProUGUI title = CourseUI.Text("Title", panel.transform, heading, 36, CourseUI.Ink, FontStyles.Bold);
        title.richText = true;
        CourseUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -26), new Vector2(size.x - 160, 52));
        Button close = CourseUI.Button("Close", panel.transform, "X", CourseUI.ButtonKind.Light, () => Destroy(go), 24);
        CourseUI.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20, -20), new Vector2(56, 56));
        card = panel.transform;
        return go;
    }

    // ---- A player over time ----

    string playerList;     // the list the chart shows
    bool showPlaces;       // places instead of points
    List<RankDay> playerDays;
    LineChart chart;
    TextMeshProUGUI playerSummary;
    TextMeshProUGUI playerBest;
    Button pointsTab, placesTab;

    void OpenPlayer(RankRow r)
    {
        playerList = ListKey;
        showPlaces = false;
        playerDays = null;
        GameObject go = MakePopup(r.n ?? "Runner", new Vector2(1500, 900), out Transform card);
        popup = go;

        playerSummary = CourseUI.Text("Summary", card, ListName + ":  <b>" + CompetitionFlow.Ordinal(r.place) + "</b>  ·  <b>" + Pts(r.pts) + "</b> points  ·  "
                                      + r.c + (r.c == 1 ? " contest" : " contests") + " in the last " + (data != null && data.rules != null ? data.rules.days : 30) + " days",
                                      22, CourseUI.InkMuted);
        playerSummary.richText = true;
        CourseUI.Place(playerSummary.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -80), new Vector2(1100, 34));

        pointsTab = CourseUI.Button("Points", card, "Points", CourseUI.ButtonKind.Primary, () => { showPlaces = false; ShowChart(); }, 22);
        CourseUI.Place((RectTransform)pointsTab.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-236, -84), new Vector2(170, 50));
        placesTab = CourseUI.Button("Places", card, "Place", CourseUI.ButtonKind.Light, () => { showPlaces = true; ShowChart(); }, 22);
        CourseUI.Place((RectTransform)placesTab.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50, -84), new Vector2(170, 50));

        chart = LineChart.Build(card, "Loading...");
        RectTransform rect = chart.Rect;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(40, 90);
        rect.offsetMax = new Vector2(-40, -150);

        playerBest = CourseUI.Text("Best", card, "", 22, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Center);
        playerBest.richText = true;
        CourseUI.Place(playerBest.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(1400, 50));
        ShowChart();
        LoadPlayer(r.p, go);
    }

    async void LoadPlayer(string playerId, GameObject forPopup)
    {
        List<RankDay> days;
        try
        {
            days = await ContestCloud.RankHistoryAsync(playerId);
        }
        catch (Exception e)
        {
            if (this == null || popup != forPopup || forPopup == null) return;
            playerBest.text = "<color=#C0392B>" + e.Message + "</color>";
            chart.EmptyMessage = "Could not load the history.";
            chart.Set(new List<string>(), new List<LineChart.Series>());
            return;
        }
        if (this == null || popup != forPopup || forPopup == null) return;
        playerDays = days ?? new List<RankDay>();
        chart.EmptyMessage = "No history yet: the chart gets a new point every day the player is in the ranking.";
        ShowChart();
    }

    void ShowChart()
    {
        if (chart == null) return;
        CourseUI.SetButtonColor(pointsTab, showPlaces ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Primary);
        CourseUI.SetButtonColor(placesTab, showPlaces ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        if (playerDays == null)
        {
            chart.Set(new List<string>(), new List<LineChart.Series>());
            playerBest.text = "";
            return;
        }

        List<string> labels = new List<string>();
        foreach (RankDay d in playerDays) labels.Add(DayText(d.d));
        List<LineChart.Series> lines = new List<LineChart.Series>();
        string key = playerList;
        if (showPlaces)
        {
            LineChart.Series places = new LineChart.Series { name = "Place", color = CourseUI.Accent, format = v => CompetitionFlow.Ordinal(Mathf.RoundToInt(v)) };
            foreach (RankDay d in playerDays) places.values.Add(Value(d, key, 1));
            lines.Add(places);
            chart.Set(labels, lines, true, 1f, float.NaN);
        }
        else
        {
            lines.Add(PointsLine(key == "overall" ? "Overall" : ListNameOf(key), CourseUI.Accent, key));
            if (key == "overall")
            {
                // With the three games under it, so you see which one pulls the overall up or down
                lines.Add(PointsLine("Route choice", LineRc, "rc"));
                lines.Add(PointsLine("Race", LineRace, "race"));
                lines.Add(PointsLine("3D", LineRun, "run"));
            }
            chart.Set(labels, lines, false, float.NaN, float.NaN);
        }
        playerBest.text = BestText(key);
    }

    LineChart.Series PointsLine(string name, Color color, string key)
    {
        LineChart.Series s = new LineChart.Series { name = name, color = color, format = Pts };
        foreach (RankDay d in playerDays) s.values.Add(Value(d, key, 0));
        return s;
    }

    static float Value(RankDay d, string key, int index)
    {
        if (d.r == null || !d.r.TryGetValue(key, out float[] v) || v == null || v.Length <= index) return float.NaN;
        return v[index];
    }

    static string ListNameOf(string key)
    {
        string[] parts = key.Split('-');
        int g = Array.IndexOf(Games, parts[0]);
        string name = g >= 0 ? GameNames[g] : key;
        if (parts.Length > 1) name += " · " + (parts[1] == "sprint" ? "Sprint" : "Forest");
        return name;
    }

    // The peak: the best place and the most points ever in this list, and when
    string BestText(string key)
    {
        int bestPlace = int.MaxValue, bestOf = 0;
        string placeDay = null, pointsDay = null;
        float bestPoints = float.MinValue;
        int ranked = 0;
        foreach (RankDay d in playerDays)
        {
            float place = Value(d, key, 1);
            if (float.IsNaN(place)) continue;
            ranked++;
            if (place < bestPlace) { bestPlace = Mathf.RoundToInt(place); bestOf = Mathf.RoundToInt(Value(d, key, 2)); placeDay = d.d; }
            float points = Value(d, key, 0);
            if (!float.IsNaN(points) && points > bestPoints) { bestPoints = points; pointsDay = d.d; }
        }
        if (ranked == 0) return "<color=#5B7563>No history in this list yet: the chart grows by one point every day the player is in the ranking.</color>";
        return "Best place: <b>" + CompetitionFlow.Ordinal(bestPlace) + "</b> of " + bestOf + " (" + DayText(placeDay) + ")"
               + "     ·     Most points: <b>" + Pts(bestPoints) + "</b> (" + DayText(pointsDay) + ")"
               + "     ·     " + ranked + (ranked == 1 ? " day" : " days") + " in the ranking";
    }
}
