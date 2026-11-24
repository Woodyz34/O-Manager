using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The My Runners screen: every runner pulled from packs, with search, sorting and filters.
public class RunnerCollectionUI : MonoBehaviour
{
    [Header("Grid")]
    public RectTransform grid;
    public RunnerCell cellTemplate;
    public ScrollRect scroll;
    public TextMeshProUGUI countText;
    public GameObject emptyState;
    public TextMeshProUGUI emptyText;
    public GameObject emptyShopButton;

    [Header("Search and filters")]
    public RunnerFilterBar filters;

    [Header("Close-up")]
    public GameObject detailPanel;
    public RunnerCardView detailCard;
    public TextMeshProUGUI detailTitle;
    public TextMeshProUGUI detailInfo;
    public Button detailClose;

    bool wired;
    TextMeshProUGUI meritsText;
    Button historyButton;
    GameObject historyPopup;
    // The chart shows when a runner has this many rating updates (2026-11-24, the user: "när det har fått 3 punkter")
    const int MinHistoryPoints = 3;
    CardData shownCard;
    RaceResults openResults;
    // Only the cards that can be seen are made (VirtualGrid); they show the runners in 'shownCards'
    VirtualGrid virtualGrid;
    readonly List<CardData> shownCards = new List<CardData>();

    void Awake()
    {
        Wire();
    }

    void Wire()
    {
        if (wired) return;
        wired = true;
        if (detailClose != null) detailClose.onClick.AddListener(() => detailPanel.SetActive(false));
        cellTemplate.gameObject.SetActive(false);
        MakeMeritsText();
        MakeHistoryButton();
        FastScroll.Add(scroll);
        virtualGrid = new VirtualGrid(scroll, grid, MakeCell, FillCell);
    }

    RectTransform MakeCell()
    {
        RunnerCell cell = Instantiate(cellTemplate, grid);
        RunnerCell captured = cell;
        cell.button.onClick.AddListener(() => ShowDetail(captured.view.Card));
        return (RectTransform)cell.transform;
    }

    void FillCell(RectTransform card, int index)
    {
        card.GetComponent<RunnerCell>().Show(shownCards[index]);
    }

    void LateUpdate()
    {
        virtualGrid?.Refresh();
    }

    // The close-up gets a "best races" list under the numbers. Made here so the saved scene needs no rebuild.
    void MakeMeritsText()
    {
        if (detailInfo == null || meritsText != null) return;
        detailInfo.fontSize = 30;   // small enough for eight lines, with the club
        detailInfo.lineSpacing = 0;
        RectTransform info = detailInfo.rectTransform;
        info.sizeDelta = new Vector2(info.sizeDelta.x, 330);

        meritsText = Instantiate(detailInfo, info.parent);
        meritsText.name = "Merits";
        meritsText.text = "";
        meritsText.fontSize = 25;
        meritsText.lineSpacing = 0;
        meritsText.richText = true;
        meritsText.raycastTarget = true;
        // Each race is a link: hovering lights it up, clicking opens the whole result list
        MeritLinks links = meritsText.gameObject.AddComponent<MeritLinks>();
        links.text = meritsText;
        links.Hovered = index => { if (shownCard != null) meritsText.text = MeritsText(shownCard, index); };
        links.Clicked = OpenResults;
        links.Missed = () => detailPanel.SetActive(false);
        RectTransform rect = meritsText.rectTransform;
        rect.anchoredPosition = info.anchoredPosition + new Vector2(0, -345);
        rect.sizeDelta = new Vector2(info.sizeDelta.x - 40, 330);
    }

    void OnEnable()
    {
        Wire();
        RunnerCollection.Changed += Rebuild;
        filters.Changed += Rebuild;
        if (detailPanel != null) detailPanel.SetActive(false);
        Rebuild();
    }

    void OnDisable()
    {
        if (historyPopup != null) Destroy(historyPopup);
        RunnerCollection.Changed -= Rebuild;
        filters.Changed -= Rebuild;
    }

    void Rebuild()
    {
        List<CardData> owned = RunnerCollection.OwnedCards();
        int total = RunnerCards.InPacks().Count;

        List<CardData> shown = shownCards;
        shown.Clear();
        foreach (CardData card in owned)
        {
            if (filters.Matches(card))
            {
                shown.Add(card);
            }
        }
        shown.Sort(filters.Compare);

        // Only the cards in sight are made and filled; the others when the list is scrolled to them
        virtualGrid.SetCount(shown.Count, true);

        countText.text = owned.Count + " of " + total + " collected";

        emptyState.SetActive(shown.Count == 0);
        if (shown.Count == 0)
        {
            bool nothingOwned = owned.Count == 0;
            emptyText.text = nothingOwned
                ? "You have no runners yet.\nOpen a pack to get your first one."
                : "No runners match your search.";
            if (emptyShopButton != null) emptyShopButton.SetActive(nothingOwned);
        }
    }

    void ShowDetail(CardData card)
    {
        if (detailPanel == null || card == null) return;
        detailCard.Show(card);
        detailCard.PlayShine();
        RunnerTier tier = RunnerCards.Tier(card);
        shownCard = card;
        if (openResults != null) openResults.Close();
        detailTitle.text = card.RunnerName;
        detailInfo.text =
            "<color=#" + ColorUtility.ToHtmlStringRGB(RunnerCards.TierColor(tier)) + "><b>" + RunnerCards.TierName(tier) + "</b></color>\n\n" +
            Row("Overall", RunnerCards.Overall(card).ToString()) +
            Row("Physics", Mathf.RoundToInt(card.physics).ToString()) +
            Row("Speed", card.SprintSpeedText) +
            Row("Technique", Mathf.RoundToInt(card.technique).ToString()) +
            Row("Class", card.klass) +
            Row("Country", Title(RunnerCards.CountryName(card)) +
                (card.countryGuessed ? "  </b><color=#FBF4DABB><size=24>guessed from club</size></color><b>" : "")) +
            Row("Club", string.IsNullOrEmpty(card.clubName) ? "-" : card.clubName);
        if (meritsText != null) meritsText.text = MeritsText(card, -1);
        if (historyPopup != null) Destroy(historyPopup);
        if (historyButton != null) historyButton.gameObject.SetActive(card.history != null && card.history.Count >= MinHistoryPoints);
        detailPanel.SetActive(true);
    }

    // "Rating history" at the top right of the numbers, for runners with at least three rating updates. Made here so
    // the saved scene needs no rebuild.
    void MakeHistoryButton()
    {
        if (detailInfo == null || historyButton != null) return;
        historyButton = CourseUI.Button("Rating history", detailInfo.transform, "Rating history", CourseUI.ButtonKind.Primary, ShowHistory, 22);
        CourseUI.Place((RectTransform)historyButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0, 0), new Vector2(250, 50));
        HoverTip.Add(historyButton, "How this runner's physics, speed and technique have changed at each rating update");
        historyButton.gameObject.SetActive(false);
    }

    // A line chart of the runner's physics, speed and technique at every rating update
    void ShowHistory()
    {
        if (shownCard == null || shownCard.history == null || detailPanel == null) return;
        if (historyPopup != null) Destroy(historyPopup);
        // Its own canvas over everything, so it is the same size whatever the size of the close-up
        GameObject go = new GameObject("Rating history", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        historyPopup = go;
        Image dim = CourseUI.Panel("Dim", go.transform, new Color(0.05f, 0.1f, 0.07f, 0.6f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(() => Destroy(go));
        Image card = CourseUI.CardPanel("Card", dim.transform);
        card.color = CourseUI.CardSolid;
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400, 780));
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, shownCard.RunnerName, 36, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -26), new Vector2(1200, 50));
        TextMeshProUGUI sub = CourseUI.Text("Sub", card.transform, "Physics, speed and technique at each rating update", 22, CourseUI.InkMuted);
        CourseUI.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40, -78), new Vector2(1200, 32));
        Button close = CourseUI.Button("Close", card.transform, "X", CourseUI.ButtonKind.Light, () => Destroy(go), 24);
        CourseUI.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20, -20), new Vector2(56, 56));

        LineChart chart = LineChart.Build(card.transform);
        RectTransform rect = chart.Rect;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(40, 40);
        rect.offsetMax = new Vector2(-40, -124);
        List<string> labels = new List<string>();
        LineChart.Series physics = new LineChart.Series { name = "Physics", color = CourseUI.Hex("3F7D54") };
        LineChart.Series speed = new LineChart.Series { name = "Speed", color = CourseUI.Hex("2E6DA4") };
        LineChart.Series technique = new LineChart.Series { name = "Technique", color = CourseUI.Accent };
        foreach (RatingPoint point in shownCard.history)
        {
            labels.Add(DayText(point.d));
            physics.values.Add(point.p);
            speed.values.Add(point.s >= 0 ? point.s : float.NaN);
            technique.values.Add(point.t);
        }
        System.Func<float, string> whole = v => Mathf.RoundToInt(v).ToString();
        physics.format = speed.format = technique.format = whole;
        chart.Set(labels, new List<LineChart.Series> { physics, speed, technique });
    }

    static string DayText(string day)
    {
        if (!DateTime.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime date))
            return day;
        return date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    void OpenResults(int index)
    {
        if (shownCard == null || shownCard.merits == null || index < 0 || index >= shownCard.merits.Count) return;
        if (openResults != null) openResults.Close();
        openResults = RaceResults.Open(detailPanel.transform, shownCard.merits[index].results, shownCard.id, shownCard.RunnerName);
        if (openResults == null) UIToast.Show("The full results for this race are not in the game yet.");
    }

    // The runner's best races of the last year: place, field size and time behind the winner.
    // They are picked by score, which counts how strong the field was, so 20th in a world class
    // field can rank above a win against a weak one. The hovered race is lit up.
    static string MeritsText(CardData card, int hovered)
    {
        const string faint = "<color=#FBF4DABB>";
        string text = "<b><cspace=6>BEST RACES THIS YEAR</cspace></b>  " + faint + "<size=20>click a race for the results</size></color>\n";
        if (card.merits == null || card.merits.Count == 0)
        {
            return text + faint + "No rated races yet.</color>";
        }
        for (int i = 0; i < card.merits.Count; i++)
        {
            RunnerMerit merit = card.merits[i];
            bool lit = i == hovered;
            string name = lit ? "<color=#E8742A><u>" + Short(merit.race) + "</u></color>" : Short(merit.race);
            string behind = merit.behind > 0 ? RaceResults.Behind(merit.behind, false) : "winner";
            text += "<link=\"" + i + "\">" + (i + 1) + ".<indent=36><b>" + name + "</b>  " + faint + merit.klass + "</color>\n" +
                    "<b>" + RaceResults.Ordinal(merit.place) + "</b> of " + merit.of +
                    "   <b>" + behind + "</b>" +
                    "   " + faint + merit.date + "</color>" +
                    (lit ? "   <color=#E8742A><b>Results ></b></color>" : "") + "</indent></link>\n";
        }
        return text;
    }

    // Some Eventor names are very long ("Swedish League, #5, medel (WRE) + D/H18-20, ..."), so they are cut
    static string Short(string race)
    {
        const int Max = 44;
        if (string.IsNullOrEmpty(race) || race.Length <= Max) return race;
        return race.Substring(0, Max - 3).TrimEnd(' ', ',', '+', '(') + "...";   // the game font has no ellipsis character
    }

    // One line of the close-up: caption on the left, value in a second column
    static string Row(string caption, string value)
    {
        return caption + "<pos=260><b>" + value + "</b>\n";
    }

    static string Title(string upper)
    {
        if (string.IsNullOrEmpty(upper)) return "";
        return upper.Substring(0, 1) + upper.Substring(1).ToLowerInvariant();
    }
}
