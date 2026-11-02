using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The whole result list of one race and class, with split times, opened from a runner's merits.
// The data is written by eventor/compute_points.py to Resources/Runners/Results/<name>.json.
// Left: place, name, time and time behind (stays put). Right: every control, scrolls sideways.
// Each split cell shows the time at the control, and under it the leg time and its rank on that leg.
public class RaceResults : MonoBehaviour
{
    const float RowHeight = 66f;
    const float LeftWidth = 660f;
    const float ColumnWidth = 124f;
    const float HeaderHeight = 54f;

    static readonly Color Dim = CourseUI.Hex("0B1A12", 0.9f);
    static readonly Color Stripe = CourseUI.Hex("1F3D2B", 0.045f);
    static readonly Color Mine = CourseUI.Hex("E8742A", 0.2f);
    static readonly Color Line = CourseUI.Hex("1F3D2B", 0.16f);
    const string Muted = "<color=#5B7563>";
    const string Best = "<color=#E8742A><b>";
    const string Good = "<color=#3F7D54><b>";

    [Serializable]
    class ResultFile
    {
        public string race;
        public string klass;
        public string date;
        public string distance;
        public int length;
        public int finishers;
        public List<string> controls;
        public List<ResultRow> runners;
    }

    [Serializable]
    class ResultRow
    {
        public string id;
        public string name;
        public string club;
        public int place;
        public int time;
        public List<int> splits;
    }

    ScrollRect leftScroll;
    ScrollRect rightScroll;
    RectTransform rightHeaderContent;
    bool syncing;

    // Opens the results over everything in the parent. Returns null when the file is missing.
    public static RaceResults Open(Transform parent, string file, string runnerId, string runnerName)
    {
        TextAsset asset = string.IsNullOrEmpty(file) ? null : Resources.Load<TextAsset>("Runners/Results/" + file);
        if (asset == null)
        {
            Debug.LogWarning("No results file Runners/Results/" + file);
            return null;
        }
        ResultFile data = JsonUtility.FromJson<ResultFile>(asset.text);
        if (data == null || data.runners == null || data.runners.Count == 0) return null;

        RectTransform root = CourseUI.Rect("Race results", parent);
        CourseUI.Stretch(root);
        RaceResults view = root.gameObject.AddComponent<RaceResults>();
        view.Build(data, runnerId, runnerName);
        return view;
    }

    public void Close()
    {
        Destroy(gameObject);
    }

    void Build(ResultFile data, string runnerId, string runnerName)
    {
        // A dark backdrop that closes the list when clicked outside the card. The card is not inside it,
        // so clicks on the card do not close anything.
        Image dim = CourseUI.Panel("Backdrop", transform, Dim, false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", transform);
        card.color = CourseUI.CardSolid;
        CourseUI.Stretch(card.rectTransform, 70, 70, 46, 46);
        Transform c = card.transform;

        int mine = FindRunner(data, runnerId, runnerName);
        int legs = data.controls.Count + 1;
        int[,] legTime = new int[data.runners.Count, legs];
        int[,] legRank = new int[data.runners.Count, legs];
        Legs(data, legTime, legRank);

        // Title and facts
        TextMeshProUGUI title = CourseUI.Text("Title", c, data.race, 46, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -26), new Vector2(1300, 60));
        title.enableWordWrapping = false;
        TextMeshProUGUI facts = CourseUI.Text("Facts", c, Facts(data), 25, CourseUI.InkMuted);
        CourseUI.Place(facts.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(46, -88), new Vector2(1400, 36));
        facts.richText = true;
        if (mine >= 0)
        {
            ResultRow row = data.runners[mine];
            TextMeshProUGUI me = CourseUI.Text("Runner", c, "", 25, CourseUI.Ink);
            CourseUI.Place(me.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(46, -126), new Vector2(1400, 36));
            me.richText = true;
            me.text = "<color=#E8742A><b>" + row.name + "</b></color>   " + Ordinal(row.place) + " of " + Math.Max(data.finishers, data.runners.Count) +
                      "   " + Muted + Behind(row.time - data.runners[0].time, true) + "</color>   " + BestLegs(legRank, mine, legs);
        }
        Button close = CourseUI.Button("Close", c, "Close", CourseUI.ButtonKind.Secondary, Close, 26);
        CourseUI.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -30), new Vector2(170, 62));

        // Table area
        float top = 176f;
        RectTransform table = CourseUI.Rect("Table", c);
        CourseUI.Stretch(table, 30, 30, top, 30);

        // Header row: fixed on the left, scrolls with the splits on the right
        Image headerBack = CourseUI.Panel("Header", table, CourseUI.Hex("1F3D2B", 0.09f), false);
        CourseUI.Place(headerBack.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, HeaderHeight));
        headerBack.rectTransform.anchorMax = new Vector2(1, 1);
        headerBack.rectTransform.sizeDelta = new Vector2(0, HeaderHeight);
        TextMeshProUGUI leftHead = CellText("Left header", headerBack.transform, 22);
        CourseUI.Place(leftHead.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(LeftWidth - 16, HeaderHeight));
        leftHead.text = "<b>#<pos=70>Name<pos=410>Time<pos=530>Behind</b>";
        leftHead.color = CourseUI.InkMuted;

        RectTransform headerView = CourseUI.Rect("Splits header", headerBack.transform);
        headerView.anchorMin = new Vector2(0, 0);
        headerView.anchorMax = new Vector2(1, 1);
        headerView.offsetMin = new Vector2(LeftWidth, 0);
        headerView.offsetMax = Vector2.zero;
        headerView.gameObject.AddComponent<RectMask2D>();
        rightHeaderContent = CourseUI.Rect("Content", headerView);
        rightHeaderContent.anchorMin = rightHeaderContent.anchorMax = new Vector2(0, 0.5f);
        rightHeaderContent.pivot = new Vector2(0, 0.5f);
        rightHeaderContent.sizeDelta = new Vector2(legs * ColumnWidth + 20, HeaderHeight);
        TextMeshProUGUI splitHead = CellText("Text", rightHeaderContent, 21);
        CourseUI.Stretch(splitHead.rectTransform, 10, 0, 0, 0);
        splitHead.color = CourseUI.InkMuted;
        StringBuilder head = new StringBuilder("<b>");
        for (int j = 0; j < legs; j++)
        {
            head.Append("<pos=").Append(j * ColumnWidth).Append(">");
            head.Append(j < legs - 1 ? (j + 1) + " <size=16>" + Muted + "(" + data.controls[j] + ")</color></size>" : "Finish");
        }
        splitHead.text = head.Append("</b>").ToString();

        // Body: two scroll views that move up and down together
        float bodyHeight = data.runners.Count * RowHeight;
        const float BarSpace = 26f;   // room for the sideways scroll bar under the splits
        RectTransform leftContent = ScrollView("Names", table, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, BarSpace), new Vector2(LeftWidth, -HeaderHeight), out leftScroll);
        leftContent.sizeDelta = new Vector2(LeftWidth, bodyHeight);
        leftScroll.horizontal = false;
        RectTransform rightContent = ScrollView("Splits", table, new Vector2(0, 0), new Vector2(1, 1), new Vector2(LeftWidth, BarSpace), new Vector2(0, -HeaderHeight), out rightScroll);
        rightContent.sizeDelta = new Vector2(legs * ColumnWidth + 20, bodyHeight);
        rightScroll.horizontal = true;
        rightScroll.horizontalScrollbar = SideBar(table, BarSpace);
        rightScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        // A thin line between names and splits
        Image divider = CourseUI.Panel("Divider", table, Line, false);
        divider.rectTransform.anchorMin = new Vector2(0, 0);
        divider.rectTransform.anchorMax = new Vector2(0, 1);
        divider.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        divider.rectTransform.offsetMin = new Vector2(LeftWidth - 1, 0);
        divider.rectTransform.offsetMax = new Vector2(LeftWidth + 1, 0);
        divider.raycastTarget = false;

        int winner = data.runners[0].time;
        for (int i = 0; i < data.runners.Count; i++)
        {
            ResultRow row = data.runners[i];
            Color back = i == mine ? Mine : i % 2 == 1 ? Stripe : Color.clear;
            RowBack(leftContent, i, back);
            RowBack(rightContent, i, back);

            TextMeshProUGUI left = CellText("Row " + (i + 1), leftContent, 25);
            CourseUI.Place(left.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -i * RowHeight), new Vector2(LeftWidth - 20, RowHeight));
            string name = Clip(row.name, 24);
            string club = Clip(row.club, 30);
            left.text = "<b>" + row.place + "</b><pos=70><b>" + name + "</b><pos=410>" + Clock(row.time) +
                        "<pos=530>" + (row.time == winner ? "" : Behind(row.time - winner, false)) +
                        "\n<pos=70><size=19>" + Muted + club + "</color></size>";

            TextMeshProUGUI right = CellText("Row " + (i + 1), rightContent, 23);
            CourseUI.Place(right.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -i * RowHeight), new Vector2(legs * ColumnWidth, RowHeight));
            StringBuilder cells = new StringBuilder();
            StringBuilder legLine = new StringBuilder("\n<size=19>");
            for (int j = 0; j < legs; j++)
            {
                int at = j < legs - 1 ? (j < row.splits.Count ? row.splits[j] : -1) : row.time;
                cells.Append("<pos=").Append(j * ColumnWidth).Append(">").Append(at >= 0 ? Clock(at) : "-");
                legLine.Append("<pos=").Append(j * ColumnWidth).Append(">");
                int rank = legRank[i, j];
                if (rank > 0)
                {
                    string open = rank == 1 ? Best : rank <= 3 ? Good : Muted;
                    string shut = rank <= 3 ? "</b></color>" : "</color>";
                    legLine.Append(open).Append(Clock(legTime[i, j])).Append(" (").Append(rank).Append(")").Append(shut);
                }
            }
            right.text = cells.Append(legLine).Append("</size>").ToString();
        }

        // Keep both sides and the header in step
        rightScroll.onValueChanged.AddListener(_ => Sync(rightScroll, leftScroll));
        leftScroll.onValueChanged.AddListener(_ => Sync(leftScroll, rightScroll));

        // Start with the runner's own row in view
        if (mine >= 0)
        {
            Canvas.ForceUpdateCanvases();
            float view = leftScroll.viewport.rect.height;
            float y = Mathf.Clamp(mine * RowHeight - view / 2f + RowHeight / 2f, 0f, Mathf.Max(0f, bodyHeight - view));
            leftContent.anchoredPosition = new Vector2(0, y);
            rightContent.anchoredPosition = new Vector2(rightContent.anchoredPosition.x, y);
        }
    }

    void Sync(ScrollRect from, ScrollRect to)
    {
        if (rightHeaderContent != null) rightHeaderContent.anchoredPosition = new Vector2(rightScroll.content.anchoredPosition.x, 0);
        if (syncing) return;
        syncing = true;
        to.content.anchoredPosition = new Vector2(to.content.anchoredPosition.x, from.content.anchoredPosition.y);
        to.velocity = new Vector2(to.velocity.x, 0f);
        syncing = false;
    }

    static RectTransform ScrollView(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, out ScrollRect scroll)
    {
        Image viewport = CourseUI.Panel(name, parent, new Color(1, 1, 1, 0.001f), false);
        RectTransform rect = viewport.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        viewport.gameObject.AddComponent<RectMask2D>();
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        RectTransform content = CourseUI.Rect("Content", rect);
        content.anchorMin = content.anchorMax = new Vector2(0, 1);
        content.pivot = new Vector2(0, 1);
        scroll.content = content;
        scroll.viewport = rect;
        scroll.vertical = true;
        scroll.scrollSensitivity = 40f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        return content;
    }

    // A slim bar under the splits for scrolling sideways
    static Scrollbar SideBar(RectTransform table, float space)
    {
        Image track = CourseUI.Panel("Sideways", table, CourseUI.Hex("1F3D2B", 0.08f));
        RectTransform rect = track.rectTransform;
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.offsetMin = new Vector2(LeftWidth + 8, 4);
        rect.offsetMax = new Vector2(-8, space - 6);
        RectTransform area = CourseUI.Rect("Area", rect);
        CourseUI.Stretch(area);
        Image handle = CourseUI.Panel("Handle", area, CourseUI.Hex("3F7D54", 0.75f));
        CourseUI.Stretch(handle.rectTransform);
        Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.LeftToRight;
        return bar;
    }

    static void RowBack(RectTransform content, int i, Color color)
    {
        if (color.a <= 0f) return;
        Image back = CourseUI.Panel("Back", content, color, false);
        back.raycastTarget = false;
        back.rectTransform.anchorMin = new Vector2(0, 1);
        back.rectTransform.anchorMax = new Vector2(1, 1);
        back.rectTransform.pivot = new Vector2(0.5f, 1);
        back.rectTransform.anchoredPosition = new Vector2(0, -i * RowHeight);
        back.rectTransform.sizeDelta = new Vector2(0, RowHeight);
    }

    static TextMeshProUGUI CellText(string name, Transform parent, float size)
    {
        TextMeshProUGUI text = CourseUI.Text(name, parent, "", size, CourseUI.Ink);
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.richText = true;
        text.lineSpacing = -6;
        return text;
    }

    // Leg times and the rank on every leg (0 when the split is missing)
    static void Legs(ResultFile data, int[,] legTime, int[,] legRank)
    {
        int legs = data.controls.Count + 1;
        for (int i = 0; i < data.runners.Count; i++)
        {
            ResultRow row = data.runners[i];
            for (int j = 0; j < legs; j++)
            {
                int at = j < legs - 1 ? (j < row.splits.Count ? row.splits[j] : -1) : row.time;
                int before = j == 0 ? 0 : (j - 1 < row.splits.Count ? row.splits[j - 1] : -1);
                legTime[i, j] = at >= 0 && before >= 0 && at >= before ? at - before : -1;
            }
        }
        List<int> times = new List<int>();
        for (int j = 0; j < legs; j++)
        {
            times.Clear();
            for (int i = 0; i < data.runners.Count; i++)
            {
                if (legTime[i, j] >= 0) times.Add(legTime[i, j]);
            }
            times.Sort();
            for (int i = 0; i < data.runners.Count; i++)
            {
                legRank[i, j] = legTime[i, j] >= 0 ? times.BinarySearchFirst(legTime[i, j]) + 1 : 0;
            }
        }
    }

    static int FindRunner(ResultFile data, string id, string name)
    {
        for (int i = 0; i < data.runners.Count; i++)
        {
            if (!string.IsNullOrEmpty(id) && data.runners[i].id == id) return i;
        }
        string bare = name;
        if (!string.IsNullOrEmpty(bare) && bare.Contains(" (")) bare = bare.Substring(0, bare.IndexOf(" ("));
        for (int i = 0; i < data.runners.Count; i++)
        {
            if (data.runners[i].name == bare) return i;
        }
        return -1;
    }

    static string Facts(ResultFile data)
    {
        List<string> parts = new List<string> { data.klass };
        if (!string.IsNullOrEmpty(data.distance)) parts.Add(data.distance);
        if (data.length > 0) parts.Add((data.length / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km");
        parts.Add(data.controls.Count + " controls");
        parts.Add(Math.Max(data.finishers, data.runners.Count) + " finishers");
        parts.Add(data.date);
        return string.Join("   <color=#C7BC98>|</color>   ", parts);
    }

    static string BestLegs(int[,] legRank, int row, int legs)
    {
        int wins = 0;
        int top3 = 0;
        for (int j = 0; j < legs; j++)
        {
            if (legRank[row, j] == 1) wins++;
            if (legRank[row, j] >= 1 && legRank[row, j] <= 3) top3++;
        }
        return Muted + "Best on " + wins + (wins == 1 ? " leg" : " legs") + ", top 3 on " + top3 + "</color>";
    }

    public static string Clock(int seconds)
    {
        if (seconds < 0) return "-";
        int h = seconds / 3600;
        int m = seconds / 60 % 60;
        int s = seconds % 60;
        return h > 0 ? h + ":" + m.ToString("00") + ":" + s.ToString("00") : m + ":" + s.ToString("00");
    }

    // "+3:12", or "winner" for the fastest
    public static string Behind(int seconds, bool sayWinner)
    {
        if (seconds <= 0) return sayWinner ? "winner" : "";
        return "+" + Clock(seconds);
    }

    public static string Ordinal(int n)
    {
        int lastTwo = n % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return n + "th";
        switch (n % 10)
        {
            case 1: return n + "st";
            case 2: return n + "nd";
            case 3: return n + "rd";
            default: return n + "th";
        }
    }

    static string Clip(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? "";
        return text.Substring(0, max - 3).TrimEnd() + "...";   // the game font has no ellipsis character
    }
}

static class RankExtensions
{
    // Index of the first item equal to value in a sorted list, so ties share a rank
    public static int BinarySearchFirst(this List<int> sorted, int value)
    {
        int lo = 0;
        int hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sorted[mid] < value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
