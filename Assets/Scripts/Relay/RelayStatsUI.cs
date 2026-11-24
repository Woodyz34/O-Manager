using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The screen between relay legs: how every team ran the leg, the split times with estimated mistakes, all the legs so
// far, and a replay. The clock is stopped while it is open. The host closes it for everyone by starting the next leg
// (or, after the last leg, by showing the results).
public class RelayStatsUI : MonoBehaviour
{
    enum Tab { Results, Splits, Legs, Routes }

    static readonly Color MistakeBack = new Color(0.86f, 0.22f, 0.18f, 0.18f);
    static readonly Color LocalBack = new Color(0.91f, 0.45f, 0.16f, 0.16f);
    static readonly Color HeaderBack = new Color(0.12f, 0.24f, 0.17f, 0.10f);
    const string Muted = "#5B7563";
    const string Red = "#C0392B";
    const string Green = "#3F7D54";

    IRaceStats race;
    int leg;
    bool final;
    List<RelayTrack> tracks;
    Tab tab = Tab.Results;

    GameObject root;
    TextMeshProUGUI title;
    TextMeshProUGUI subtitle;
    RectTransform content;
    ScrollRect scroll;
    TextMeshProUGUI footnote;
    Button continueButton;
    TextMeshProUGUI waitingText;
    readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
    RelayReplayView replay;
    RouteCompareView routes;
    Button replayButton;
    float nextRefresh;
    string lastSignature = "";

    public bool IsOpen => root != null && (root.activeSelf || replay != null || routes != null);

    public static RelayStatsUI Create(IRaceStats race)
    {
        RelayStatsUI ui = new GameObject("Relay statistics").AddComponent<RelayStatsUI>();
        ui.race = race;
        return ui;
    }

    public void Open(int legIndex, bool isFinal, List<RelayTrack> recorded)
    {
        leg = legIndex;
        final = isFinal;
        tracks = recorded;
        tab = Tab.Results;
        if (root == null) Build();
        root.SetActive(true);
        // "Waiting for the other runners to finish" and the host's lobby button belong to individual races
        Clock clock = NetworkPlayer.Local != null ? NetworkPlayer.Local.clock : null;
        if (clock != null)
        {
            if (clock.finishTime != null) clock.finishTime.gameObject.SetActive(false);
            if (clock.multiplayerEndScreen != null) clock.multiplayerEndScreen.SetActive(false);
            clock.HideFinishTexts = true;
        }
        lastSignature = "";
        Refresh();
    }

    public void Close()
    {
        if (replay != null) replay.Close();
        if (routes != null) routes.Close();
        if (root != null) root.SetActive(false);
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root);
    }

    void Update()
    {
        if (root == null || !root.activeSelf) return;
        // The other teams' numbers can arrive a moment after the screen opens
        if (Time.unscaledTime > nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.5f;
            Refresh();
        }
    }

    // ---- Layout ----

    void Build()
    {
        GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
        Transform parent = canvas != null ? canvas.transform : null;

        Image dim = CourseUI.Panel("Relay statistics", parent, new Color(0.06f, 0.12f, 0.08f, 0.55f), false);
        root = dim.gameObject;
        CourseUI.Stretch(dim.rectTransform);
        root.transform.SetAsLastSibling();

        Image card = CourseUI.CardPanel("Card", dim.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1760, 980));

        title = CourseUI.Text("Title", card.transform, "", 44, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -24), new Vector2(1100, 56));
        subtitle = CourseUI.Text("Subtitle", card.transform, "", 22, CourseUI.InkMuted);
        CourseUI.Place(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(42, -82), new Vector2(1300, 32));

        RectTransform tabs = CourseUI.Rect("Tabs", card.transform);
        CourseUI.Place(tabs, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -126), new Vector2(1680, 56));
        CourseUI.Row(tabs.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        AddTab(tabs, Tab.Results, "Leg results");
        AddTab(tabs, Tab.Splits, "Split times");
        AddTab(tabs, Tab.Legs, "All legs");
        AddTab(tabs, Tab.Routes, "Route choices");
        replayButton = CourseUI.Button("Replay", tabs, "Replay the leg", CourseUI.ButtonKind.Secondary, OpenReplay, 22);
        CourseUI.Size(replayButton, 54, 230);

        // A scroll view that scrolls both ways, for wide split tables
        Image viewport = CourseUI.Panel("Table", card.transform, new Color(1f, 1f, 1f, 0.55f));
        CourseUI.Place(viewport.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -196), new Vector2(1680, 620));
        viewport.gameObject.AddComponent<RectMask2D>();
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        content = CourseUI.Rect("Rows", viewport.transform);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 1f);
        content.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup rows = content.gameObject.AddComponent<VerticalLayoutGroup>();
        rows.spacing = 2;
        rows.padding = new RectOffset(10, 10, 10, 10);
        rows.childControlWidth = true;
        rows.childControlHeight = true;
        rows.childForceExpandWidth = true;
        rows.childForceExpandHeight = false;
        // The rows are at least as wide as the card; a wider table (many splits) scrolls sideways
        content.gameObject.AddComponent<LayoutElement>().minWidth = viewport.rectTransform.sizeDelta.x;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        scroll.viewport = viewport.rectTransform;
        scroll.horizontal = true;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        footnote = CourseUI.Text("Note", card.transform, "", 18, CourseUI.InkMuted);
        CourseUI.Place(footnote.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(42, 92), new Vector2(1680, 50));

        continueButton = CourseUI.Button("Continue", card.transform, "", CourseUI.ButtonKind.Primary, () => race.HostContinue(), 26);
        CourseUI.Place((RectTransform)continueButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40, 24), new Vector2(380, 62));
        waitingText = CourseUI.Text("Waiting", card.transform, "", 24, CourseUI.InkMuted, FontStyles.Italic, TextAlignmentOptions.Right);
        CourseUI.Place(waitingText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40, 34), new Vector2(900, 44));
    }

    void AddTab(Transform parent, Tab which, string label)
    {
        Button button = CourseUI.Button("Tab " + label, parent, label, CourseUI.ButtonKind.Light, () =>
        {
            tab = which;
            lastSignature = "";
            Refresh();
        }, 22);
        CourseUI.Size(button, 54, 220);
        tabButtons[which] = button;
    }

    void OpenReplay()
    {
        if (tracks == null || tracks.Count == 0) return;
        root.SetActive(false);
        replay = RelayReplayView.Open(tracks, () =>
        {
            replay = null;
            if (root != null && !CompetitionFlow.ResultsShown) root.SetActive(true);
        });
    }

    // Everyone's routes on one leg, on the map (individual competitions)
    void OpenRoutes(int leg)
    {
        root.SetActive(false);
        routes = RouteCompareView.Open(race, tracks, leg, () =>
        {
            routes = null;
            if (root != null && !CompetitionFlow.ResultsShown) root.SetActive(true);
        });
    }

    // ---- Content ----

    void Refresh()
    {
        List<RelayTeamResult> teams = RelayStats.ForLeg(race, leg);
        teams.Sort((a, b) => a.place.CompareTo(b.place));

        // Only rebuild the table when something changed, so scrolling isn't reset every half second
        string signature = tab + "|" + teams.Count;
        foreach (RelayTeamResult t in teams) signature += "|" + t.total + "/" + t.hasSplits;
        bool host = CompetitionFlow.LocalIsHost();
        signature += "|" + host;
        if (signature == lastSignature) return;
        lastSignature = signature;

        title.text = !race.IsRelay ? "Competition finished" : final ? "Relay finished: leg " + (leg + 1) + " of " + race.LegCount : "After leg " + (leg + 1) + " of " + race.LegCount;
        subtitle.text = race.LegDescription(leg) + (race.IsRelay ? ". The clock is stopped while you look." : ".");
        if (tabButtons.TryGetValue(Tab.Legs, out Button legsTab)) legsTab.gameObject.SetActive(race.IsRelay);
        if (tabButtons.TryGetValue(Tab.Routes, out Button routesTab)) routesTab.gameObject.SetActive(!race.IsRelay);
        if (tabButtons.TryGetValue(Tab.Results, out Button resultsTab)) CourseUI.SetButtonText(resultsTab, race.IsRelay ? "Leg results" : "Results");
        if (replayButton != null) CourseUI.SetButtonText(replayButton, race.IsRelay ? "Replay the leg" : "Replay");
        foreach (var pair in tabButtons)
        {
            CourseUI.SetButtonColor(pair.Value, pair.Key == tab ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        }

        continueButton.gameObject.SetActive(host);
        CourseUI.SetButtonText(continueButton, final ? (ContestRun.Active != null ? CompetitionFlow.BackButtonText : "Show the results") : "Start leg " + (leg + 2));
        waitingText.gameObject.SetActive(!host);
        waitingText.text = final ? "Waiting for the host to show the results..." : "Waiting for the host to start leg " + (leg + 2) + "...";

        foreach (Transform child in content) Destroy(child.gameObject);
        lastTeamCount = teams.Count;
        switch (tab)
        {
            case Tab.Results: BuildResults(teams); break;
            case Tab.Splits: BuildSplits(teams); break;
            case Tab.Legs: BuildLegs(teams); break;
            case Tab.Routes: BuildRoutes(teams); break;
        }
        // Alone (2026-11-19, the user): no places and no mistake estimate, they would always say 1st and 0:00
        footnote.text = tab == Tab.Legs
            ? (Alone ? "Each leg: leg time, then the total time after the leg." : "Each leg: leg time (place on the leg), then the team's total time (place after the leg).")
            : tab == Tab.Routes
            ? "Click a leg to see everyone's route on the map, next to the computer's fastest route."
            : Alone ? ""
            : "Mistakes (est.): every split is compared with the fastest 25 % of the runners on it, adjusted to the runner's own pace on the other splits. A split that took more than 30 s and 25 % longer than that is marked red, and the time lost is shown.";
    }

    // Only one team or player: places and mistake estimates mean nothing
    bool Alone => lastTeamCount <= 1;
    int lastTeamCount = 2;

    void BuildResults(List<RelayTeamResult> teams)
    {
        if (Alone)
        {
            if (race.IsRelay) Row(48, true, Cell("Team", 300), Cell("Runner", 300), Cell("Leg time", 170), Cell("Total", 170));
            else Row(48, true, Cell(race.IsRelay ? "Team" : "Player", 300), Cell("Runner", 300), Cell("Time", 170));
            foreach (RelayTeamResult t in teams)
            {
                if (race.IsRelay) Row(52, false, t.isLocal, Cell(t.team, 300, FontStyles.Bold), Cell(t.runner, 300), Cell(RelayStats.Time(t.legTime), 170), Cell(RelayStats.Time(t.total), 170));
                else Row(52, false, t.isLocal, Cell(t.team, 300, FontStyles.Bold), Cell(t.runner, 300), Cell(RelayStats.Time(t.total), 170));
            }
            return;
        }
        if (race.IsRelay) Row(48, true, Cell("Pl.", 80), Cell("+/-", 90), Cell("Team", 300), Cell("Runner", 300), Cell("Leg time", 170), Cell("Leg pl.", 110), Cell("Total", 170), Cell("Mistakes (est.)", 230));
        // One leg only: no place changes or leg columns, and the time is the race clock the places are made from
        else Row(48, true, Cell("Pl.", 80), Cell("Player", 300), Cell("Runner", 300), Cell("Time", 170), Cell("Mistakes (est.)", 230));
        foreach (RelayTeamResult t in teams)
        {
            string change = "";
            if (leg > 0 && t.prevPlace > 0)
            {
                int gained = t.prevPlace - t.place;
                change = gained > 0 ? "<color=" + Green + ">+" + gained + "</color>" : gained < 0 ? "<color=" + Red + ">" + gained + "</color>" : "<color=" + Muted + ">0</color>";
            }
            string mistakes = !t.hasSplits ? "<color=" + Muted + ">-</color>" : t.totalLoss > 0 ? "<color=" + Red + ">" + RelayStats.Time(t.totalLoss) + "</color>" : "0:00";
            if (!race.IsRelay)
            {
                Row(52, false, t.isLocal,
                    Cell(t.place.ToString(), 80, FontStyles.Bold), Cell(t.team, 300, FontStyles.Bold), Cell(t.runner, 300),
                    Cell(RelayStats.Time(t.total), 170), Cell(mistakes, 230));
                continue;
            }
            Row(52, false, t.isLocal,
                Cell(t.place.ToString(), 80, FontStyles.Bold), Cell(change, 90), Cell(t.team, 300, FontStyles.Bold), Cell(t.runner, 300),
                Cell(RelayStats.Time(t.legTime), 170), Cell(t.legPlace.ToString(), 110), Cell(RelayStats.Time(t.total), 170), Cell(mistakes, 230));
        }
    }

    void BuildSplits(List<RelayTeamResult> teams)
    {
        LegRoute layout = race.Course.Route(leg, null);
        int points = layout.CommonCount;
        List<CellData> header = new List<CellData> { Cell(race.IsRelay ? "Team" : "Player", 260) };
        // The common controls keep the numbers they have in the course builder (a fork counts as one number)
        List<string> numbers = new List<string>();
        int number = 0;
        if (race.Course.IsRelay())
        {
            foreach (RelayItem item in race.Course.LegCourse(leg).items)
            {
                number++;
                if (item.fork < 0) numbers.Add(number.ToString());
            }
        }
        else
        {
            for (int c = 1; c <= race.Course.controls.Count; c++) numbers.Add(c.ToString());
        }
        for (int c = 1; c < points; c++)
        {
            string name = c == points - 1 ? "Finish" : c - 1 < numbers.Count ? "Control " + numbers[c - 1] : c.ToString();
            header.Add(Cell(name + (layout.segmentBranch[c] >= 0 ? "\n<size=75%><color=" + Muted + ">after a fork</color></size>" : ""), 160));
        }
        Row(58, true, header.ToArray());

        foreach (RelayTeamResult t in teams)
        {
            List<CellData> cells = new List<CellData> { Cell("<b>" + t.team + "</b>\n<size=80%><color=" + Muted + ">" + t.runner + "</color></size>", 260) };
            for (int c = 1; c < points; c++)
            {
                if (!t.hasSplits)
                {
                    cells.Add(Cell("<color=" + Muted + ">-</color>", 160));
                    continue;
                }
                string branch = t.variants[c] >= 0 ? "<color=" + Muted + ">" + CourseData.BranchLetter(t.variants[c]) + "</color> " : "";
                string text = Alone
                    ? branch + "<b>" + RelayStats.Time(t.split[c]) + "</b>\n<size=80%>" + RelayStats.Time(t.clocks[c] - t.clocks[0]) + "</size>"
                    : branch + "<b>" + RelayStats.Time(t.split[c]) + "</b> (" + t.splitPlace[c] + ")"
                    + "\n<size=80%>" + RelayStats.Time(t.clocks[c] - t.clocks[0]) + " (" + t.cumPlace[c] + ")</size>";
                if (t.mistake[c] && !Alone) text += "\n<size=80%><color=" + Red + ">+" + RelayStats.Time(t.loss[c]) + "</color></size>";
                CellData cell = Cell(text, 160);
                if (t.mistake[c] && !Alone) cell.back = MistakeBack;
                cells.Add(cell);
            }
            Row(t.hasSplits && System.Array.Exists(t.mistake, m => m) ? 96 : 76, false, t.isLocal, cells.ToArray());
        }
        Row(40, false, Cell("<size=85%><color=" + Muted + ">" + (Alone ? "Each box: time from the control before, then time since the start." : race.IsRelay
            ? "Times between the controls every team runs. Each box: time from the common control before (place among the teams that ran the same branch, shown by its letter), then time since the changeover (race position there)."
            : "Each box: time from the control before (place on that split), then time since the start (place at the control).") + "</color></size>", 1500));
    }

    void BuildLegs(List<RelayTeamResult> teams)
    {
        List<CellData> header = new List<CellData> { Cell("Team", 280) };
        for (int l = 0; l <= leg; l++) header.Add(Cell("Leg " + (l + 1), 210));
        Row(48, true, header.ToArray());

        // Places on every leg, from the shared leg times and totals
        int[,] legPlace = new int[teams.Count, leg + 1];
        int[,] totalPlace = new int[teams.Count, leg + 1];
        for (int l = 0; l <= leg; l++)
        {
            for (int i = 0; i < teams.Count; i++)
            {
                int legTime = teams[i].player.relayLegTimes[l];
                int total = teams[i].player.relayTotals[l];
                int lp = 1, tp = 1;
                for (int j = 0; j < teams.Count; j++)
                {
                    if (teams[j].player.relayLegTimes[l] < legTime) lp++;
                    if (teams[j].player.relayTotals[l] < total) tp++;
                }
                legPlace[i, l] = lp;
                totalPlace[i, l] = tp;
            }
        }
        for (int i = 0; i < teams.Count; i++)
        {
            RelayTeamResult t = teams[i];
            List<CellData> cells = new List<CellData> { Cell("<b>" + t.team + "</b>", 280) };
            for (int l = 0; l <= leg; l++)
            {
                string runner = race.History.Runner(l, t.player, leg);
                cells.Add(Cell(RelayStats.Time(t.player.relayLegTimes[l]) + (Alone ? "" : " (" + legPlace[i, l] + ")")
                    + "\n<size=80%>" + RelayStats.Time(t.player.relayTotals[l]) + (Alone ? "" : " (" + totalPlace[i, l] + ")") + "</size>"
                    + (runner != "" ? "\n<size=75%><color=" + Muted + ">" + runner + "</color></size>" : ""), 210));
            }
            Row(92, false, t.isLocal, cells.ToArray());
        }
    }

    // One row per leg with every player's time on it; a click shows the routes on the map
    void BuildRoutes(List<RelayTeamResult> teams)
    {
        int points = race.Course.Route(leg, null).CommonCount;
        List<RelayTeamResult> players = new List<RelayTeamResult>(teams);
        players.Sort((a, b) => b.isLocal.CompareTo(a.isLocal));
        List<CellData> header = new List<CellData> { Cell("Leg", 230), Cell("", 190) };
        foreach (RelayTeamResult t in players) header.Add(Cell(t.team, 190));
        Row(48, true, header.ToArray());
        for (int c = 1; c < points; c++)
        {
            string name = (c - 1 == 0 ? "Start" : (c - 1).ToString()) + " - " + (c == points - 1 ? "Finish" : c.ToString());
            List<CellData> cells = new List<CellData> { Cell("<b>" + c + ".</b>  " + name, 230) };
            int captured = c;
            CellData open = Cell("<color=" + Green + "><b>Show routes ></b></color>", 190);
            open.click = () => OpenRoutes(captured);
            cells.Add(open);
            foreach (RelayTeamResult t in players)
            {
                if (!t.hasSplits)
                {
                    cells.Add(Cell("<color=" + Muted + ">-</color>", 190));
                    continue;
                }
                string text = RelayStats.Time(t.split[c]) + (Alone ? "" : " (" + t.splitPlace[c] + ")");
                CellData cell = Cell(t.splitPlace[c] == 1 && !Alone ? "<b>" + text + "</b>" : text, 190);
                cells.Add(cell);
            }
            Row(52, false, cells.ToArray());
        }
        Row(40, false, Cell("<size=85%><color=" + Muted + ">" + (RouteAnalysis.On
            ? "Everyone ran with the same runner and nobody made mistakes, so the times show which route choice was best."
            : Alone ? "Time on each leg."
            : "Time on each leg (place on the leg). The runners have different speed and technique, so the times are not only about the route.") + "</color></size>", 1500));
    }

    // ---- Table helpers ----

    class CellData
    {
        public string text;
        public float width;
        public FontStyles style;
        public Color? back;
        public UnityEngine.Events.UnityAction click;
    }

    static CellData Cell(string text, float width, FontStyles style = FontStyles.Normal)
    {
        return new CellData { text = text, width = width, style = style };
    }

    void Row(float height, bool header, params CellData[] cells)
    {
        Row(height, header, false, cells);
    }

    void Row(float height, bool header, bool local, params CellData[] cells)
    {
        Image row = CourseUI.Panel("Row", content, header ? HeaderBack : local ? LocalBack : new Color(1f, 1f, 1f, 0f), false);
        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4;
        layout.padding = new RectOffset(6, 6, 2, 2);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        LayoutElement rowSize = row.gameObject.AddComponent<LayoutElement>();
        rowSize.minHeight = height;
        rowSize.preferredHeight = height;
        for (int i = 0; i < cells.Length; i++)
        {
            CellData cell = cells[i];
            Image back = CourseUI.Panel("Cell", row.transform, cell.back ?? new Color(1f, 1f, 1f, 0f), false);
            LayoutElement size = back.gameObject.AddComponent<LayoutElement>();
            size.minWidth = cell.width;
            size.preferredWidth = cell.width;
            if (i == cells.Length - 1) size.flexibleWidth = 1; // the last column takes what is left of the row
            TextMeshProUGUI text = CourseUI.Text("Text", back.transform, cell.text, header ? 21 : 21, CourseUI.Ink,
                header ? FontStyles.Bold : cell.style, TextAlignmentOptions.MidlineLeft);
            CourseUI.Stretch(text.rectTransform, 6, 4, 2, 2);
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableWordWrapping = cell.width > 600f;
            if (cell.click != null)
            {
                back.raycastTarget = true;
                Button button = back.gameObject.AddComponent<Button>();
                button.targetGraphic = back;
                button.onClick.AddListener(cell.click);
            }
        }
    }
}
