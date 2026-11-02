using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
    readonly List<RunnerCell> cells = new List<RunnerCell>();

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
    }

    // The close-up gets a "best races" list under the numbers. Made here so the saved scene needs no rebuild.
    void MakeMeritsText()
    {
        if (detailInfo == null || meritsText != null) return;
        detailInfo.fontSize = 32;
        detailInfo.lineSpacing = 0;
        RectTransform info = detailInfo.rectTransform;
        info.sizeDelta = new Vector2(info.sizeDelta.x, 330);

        meritsText = Instantiate(detailInfo, info.parent);
        meritsText.name = "Merits";
        meritsText.text = "";
        meritsText.fontSize = 25;
        meritsText.lineSpacing = 0;
        meritsText.richText = true;
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
        RunnerCollection.Changed -= Rebuild;
        filters.Changed -= Rebuild;
    }

    void Rebuild()
    {
        List<CardData> owned = RunnerCollection.OwnedCards();
        int total = RunnerCards.InPacks().Count;

        List<CardData> shown = new List<CardData>();
        foreach (CardData card in owned)
        {
            if (filters.Matches(card))
            {
                shown.Add(card);
            }
        }
        shown.Sort(filters.Compare);

        // Reuse the cells that are already there
        while (cells.Count < shown.Count)
        {
            RunnerCell cell = Instantiate(cellTemplate, grid);
            cell.gameObject.SetActive(true);
            RunnerCell captured = cell;
            cell.button.onClick.AddListener(() => ShowDetail(captured.view.Card));
            cells.Add(cell);
        }
        for (int i = 0; i < cells.Count; i++)
        {
            bool used = i < shown.Count;
            cells[i].gameObject.SetActive(used);
            if (used)
            {
                cells[i].Show(shown[i]);
            }
        }

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
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    void ShowDetail(CardData card)
    {
        if (detailPanel == null || card == null) return;
        detailCard.Show(card);
        detailCard.PlayShine();
        RunnerTier tier = RunnerCards.Tier(card);
        detailTitle.text = card.RunnerName;
        detailInfo.text =
            "<color=#" + ColorUtility.ToHtmlStringRGB(RunnerCards.TierColor(tier)) + "><b>" + RunnerCards.TierName(tier) + "</b></color>\n\n" +
            Row("Overall", RunnerCards.Overall(card).ToString()) +
            Row("Speed", Mathf.RoundToInt(card.speed).ToString()) +
            Row("Technique", Mathf.RoundToInt(card.technique).ToString()) +
            Row("Class", card.klass) +
            Row("Country", Title(RunnerCards.CountryName(card)));
        if (meritsText != null) meritsText.text = MeritsText(card);
        detailPanel.SetActive(true);
    }

    // The runner's best races of the last year. The score counts how strong the field was,
    // so 20th in a world class field can rank above a win against a weak one.
    static string MeritsText(CardData card)
    {
        const string faint = "<color=#FBF4DABB>";
        string text = "<b><cspace=6>BEST RACES THIS YEAR</cspace></b>\n";
        if (card.merits == null || card.merits.Count == 0)
        {
            return text + faint + "No rated races yet.</color>";
        }
        for (int i = 0; i < card.merits.Count; i++)
        {
            RunnerMerit merit = card.merits[i];
            text += (i + 1) + ".<indent=36><b>" + Short(merit.race) + "</b>  " + faint + merit.klass + "</color>\n" +
                    Ordinal(merit.place) + " of " + merit.of +
                    "   " + faint + "Score</color> <b>" + Mathf.RoundToInt(merit.score) + "</b>" +
                    "   " + faint + "Field</color> <b>" + Mathf.RoundToInt(merit.field) + "</b>" +
                    "   " + faint + merit.date + "</color></indent>\n";
        }
        return text;
    }

    // Some Eventor names are very long ("Swedish League, #5, medel (WRE) + D/H18-20, ..."), so they are cut
    static string Short(string race)
    {
        const int Max = 44;
        if (string.IsNullOrEmpty(race) || race.Length <= Max) return race;
        return race.Substring(0, Max - 1).TrimEnd(' ', ',', '+', '(') + "\u2026";
    }

    static string Ordinal(int n)
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
