using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The search field, the sort and tier buttons and the two sliders above a list of runner cards.
// It only keeps track of what the player has asked for. The screen that owns the list (My Runners,
// Choose a runner) asks it which runners to show and in what order.
public class RunnerFilterBar : MonoBehaviour
{
    public enum SortBy { Overall, Speed, Technique, Name }

    public TMP_InputField search;
    public Button[] sortButtons;          // in the order of SortBy
    public Button[] tierButtons;          // All, Elite, Gold, Silver, Bronze
    public Slider minSpeed;
    public Slider minTechnique;
    public TextMeshProUGUI minSpeedLabel;
    public TextMeshProUGUI minTechniqueLabel;
    public Button clearButton;
    public Color chipOn = Color.white;
    public Color chipOff = Color.gray;
    public Color chipOnText = Color.black;
    public Color chipOffText = Color.black;

    // Raised when the player changes anything in the bar
    public event Action Changed;

    SortBy sortBy = SortBy.Overall;
    int tierFilter = -1;                  // -1 = all tiers
    bool wired;

    void Awake()
    {
        Wire();
    }

    void OnEnable()
    {
        Wire();
        Paint();
    }

    void Wire()
    {
        if (wired) return;
        wired = true;
        for (int i = 0; i < sortButtons.Length; i++)
        {
            SortBy choice = (SortBy)i;
            sortButtons[i].onClick.AddListener(() => { sortBy = choice; Notify(); });
        }
        for (int i = 0; i < tierButtons.Length; i++)
        {
            int choice = TierOfButton(i);
            tierButtons[i].onClick.AddListener(() => { tierFilter = choice; Notify(); });
        }
        search.onValueChanged.AddListener(_ => Notify());
        minSpeed.onValueChanged.AddListener(_ => Notify());
        minTechnique.onValueChanged.AddListener(_ => Notify());
        if (clearButton != null) clearButton.onClick.AddListener(Clear);
    }

    // Button 0 is "All", then Elite down to Bronze
    static int TierOfButton(int index)
    {
        return index == 0 ? -1 : (int)RunnerTier.Elite - (index - 1);
    }

    void Notify()
    {
        Paint();
        Changed?.Invoke();
    }

    public bool Filtering
    {
        get
        {
            return tierFilter >= 0 || !string.IsNullOrWhiteSpace(search.text)
                || minSpeed.value > minSpeed.minValue || minTechnique.value > minTechnique.minValue;
        }
    }

    public bool Matches(CardData card)
    {
        string wanted = search.text == null ? "" : search.text.Trim();
        if (wanted.Length > 0 && (card.RunnerName == null || card.RunnerName.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0)) return false;
        if (tierFilter >= 0 && (int)RunnerCards.Tier(card) != tierFilter) return false;
        if (Mathf.RoundToInt(card.speed) < minSpeed.value) return false;
        if (Mathf.RoundToInt(card.technique) < minTechnique.value) return false;
        return true;
    }

    // Best first for the three numbers, A to Z for names
    public int Compare(CardData a, CardData b)
    {
        int result = 0;
        switch (sortBy)
        {
            case SortBy.Overall: result = RunnerCards.Overall(b).CompareTo(RunnerCards.Overall(a)); break;
            case SortBy.Speed: result = b.speed.CompareTo(a.speed); break;
            case SortBy.Technique: result = b.technique.CompareTo(a.technique); break;
        }
        return result != 0 ? result : string.Compare(a.RunnerName, b.RunnerName, StringComparison.OrdinalIgnoreCase);
    }

    public void Clear()
    {
        tierFilter = -1;
        search.SetTextWithoutNotify("");
        minSpeed.SetValueWithoutNotify(minSpeed.minValue);
        minTechnique.SetValueWithoutNotify(minTechnique.minValue);
        Notify();
    }

    void Paint()
    {
        minSpeedLabel.text = "Min speed  <b>" + Mathf.RoundToInt(minSpeed.value) + "</b>";
        minTechniqueLabel.text = "Min technique  <b>" + Mathf.RoundToInt(minTechnique.value) + "</b>";
        if (clearButton != null) clearButton.gameObject.SetActive(Filtering);
        for (int i = 0; i < sortButtons.Length; i++) PaintChip(sortButtons[i], i == (int)sortBy);
        for (int i = 0; i < tierButtons.Length; i++) PaintChip(tierButtons[i], TierOfButton(i) == tierFilter);
    }

    void PaintChip(Button chip, bool on)
    {
        chip.targetGraphic.color = on ? chipOn : chipOff;
        TextMeshProUGUI label = chip.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.color = on ? chipOnText : chipOffText;
    }
}
