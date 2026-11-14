using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Choose a runner screen before a competition. CardManager decides which runners are offered (CardManager.Offered);
// this hides and sorts them after what is set in the filter bar, and shows them as cards. Only the cards that can be
// seen are made (VirtualGrid), and they are filled with other runners as the list scrolls: with hundreds of runners
// the screen used to freeze while every card was made, and scrolled slowly.
public class RunnerPickerUI : MonoBehaviour
{
    public RunnerFilterBar filters;
    public RectTransform grid;
    public ScrollRect scroll;
    public TextMeshProUGUI countText;
    public GameObject emptyState;
    [Tooltip("Scale given to every card, so they are the same size as in My Runners.")]
    public float cardScale = 1f;

    CardManager manager;
    VirtualGrid virtualGrid;
    int seenVersion = -1;
    readonly List<CardData> shown = new List<CardData>();

    void OnEnable()
    {
        filters.Changed += Apply;
        seenVersion = -1;
        FastScroll.Add(scroll);
    }

    void OnDisable()
    {
        filters.Changed -= Apply;
    }

    CardManager Manager()
    {
        if (manager == null) manager = FindAnyObjectByType<CardManager>(FindObjectsInactive.Include);
        return manager;
    }

    void LateUpdate()
    {
        // The runners arrive a moment after the screen opens, and change between the legs of a relay
        CardManager cards = Manager();
        if (cards != null && cards.OfferedVersion != seenVersion)
        {
            Apply();
        }
        virtualGrid?.Refresh();
    }

    RectTransform MakeCard()
    {
        GameObject card = Instantiate(Manager().CardPrefab, grid);
        card.transform.localScale = new Vector3(cardScale, cardScale, 1f);
        return (RectTransform)card.transform;
    }

    void FillCard(RectTransform card, int index)
    {
        CardData data = shown[index];
        card.GetComponent<CardDisplay>().SetValues(data);
        Manager().MarkCard(card, data);
    }

    void Apply()
    {
        CardManager cards = Manager();
        if (cards == null) return;
        seenVersion = cards.OfferedVersion;
        if (virtualGrid == null) virtualGrid = new VirtualGrid(scroll, grid, MakeCard, FillCard);

        shown.Clear();
        foreach (CardData card in cards.Offered)
        {
            if (filters.Matches(card)) shown.Add(card);
        }
        shown.Sort(filters.Compare);
        virtualGrid.SetCount(shown.Count, true);

        int total = cards.Offered.Count;
        countText.text = shown.Count == total
            ? total + " runners to choose from"
            : shown.Count + " of " + total + " runners shown";
        emptyState.SetActive(shown.Count == 0 && total > 0);
    }
}
