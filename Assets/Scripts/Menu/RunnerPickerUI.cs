using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Choose a runner screen before a competition. CardManager still decides which cards are offered
// and puts them in the grid; this hides, sorts and sizes them after what is set in the filter bar.
public class RunnerPickerUI : MonoBehaviour
{
    public RunnerFilterBar filters;
    public RectTransform grid;
    public ScrollRect scroll;
    public TextMeshProUGUI countText;
    public GameObject emptyState;
    [Tooltip("Scale given to every card, so they are the same size as in My Runners.")]
    public float cardScale = 1f;

    int seenCards = -1;

    void OnEnable()
    {
        filters.Changed += Apply;
        seenCards = -1;
        FastScroll.Add(scroll);
    }

    void OnDisable()
    {
        filters.Changed -= Apply;
    }

    void LateUpdate()
    {
        // The cards arrive a moment after the screen opens
        if (grid.childCount != seenCards)
        {
            Apply();
        }
    }

    void Apply()
    {
        seenCards = grid.childCount;
        List<CardDisplay> cards = new List<CardDisplay>();
        foreach (Transform child in grid)
        {
            CardDisplay card = child.GetComponent<CardDisplay>();
            if (card != null && card.CardData != null)
            {
                cards.Add(card);
            }
        }

        List<CardDisplay> shown = new List<CardDisplay>();
        foreach (CardDisplay card in cards)
        {
            bool match = filters.Matches(card.CardData);
            card.transform.localScale = new Vector3(cardScale, cardScale, 1f);
            if (card.gameObject.activeSelf != match) card.gameObject.SetActive(match);
            if (match) shown.Add(card);
        }
        shown.Sort((a, b) => filters.Compare(a.CardData, b.CardData));
        for (int i = 0; i < shown.Count; i++)
        {
            shown[i].transform.SetSiblingIndex(i);
        }

        countText.text = shown.Count == cards.Count
            ? cards.Count + " runners to choose from"
            : shown.Count + " of " + cards.Count + " runners shown";
        emptyState.SetActive(shown.Count == 0 && cards.Count > 0);
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }
}
