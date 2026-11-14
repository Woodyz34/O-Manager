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
        detailPanel.SetActive(true);
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
