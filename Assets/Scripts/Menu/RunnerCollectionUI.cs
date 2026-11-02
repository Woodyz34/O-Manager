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
                cells[i].Show(shown[i], RunnerCollection.Count(shown[i].RunnerName));
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
        int copies = RunnerCollection.Count(card.RunnerName);
        detailTitle.text = card.RunnerName;
        detailInfo.text =
            "<color=#" + ColorUtility.ToHtmlStringRGB(RunnerCards.TierColor(tier)) + "><b>" + RunnerCards.TierName(tier) + "</b></color>\n\n" +
            Row("Overall", RunnerCards.Overall(card).ToString()) +
            Row("Speed", Mathf.RoundToInt(card.speed).ToString()) +
            Row("Technique", Mathf.RoundToInt(card.technique).ToString()) +
            Row("Class", card.klass) +
            Row("Country", Title(RunnerCards.CountryName(card))) + "\n" +
            (copies > 1 ? "You have " + copies + " copies" : "You have 1 copy");
        detailPanel.SetActive(true);
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
