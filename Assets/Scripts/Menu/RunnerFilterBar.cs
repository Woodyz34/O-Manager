using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The search field, the sort and tier buttons and the two sliders above a list of runner cards.
// It only keeps track of what the player has asked for. The screen that owns the list (My Runners,
// Choose a runner) asks it which runners to show and in what order.
public class RunnerFilterBar : MonoBehaviour
{
    public enum SortBy { Overall, Speed, Technique, Name, Club, Country }

    public TMP_InputField search;
    public Button[] sortButtons;          // in the order of SortBy (the scenes have the first four, Club and Country are made here)
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
    bool reversed;                        // lowest rating first, or Z to A
    Button orderChip;
    int tierFilter = -1;                  // -1 = all tiers
    Button classChip;
    Button countryChip;
    Button clubChip;

    // What is ticked in the three drop-downs (empty = no filter). Classes are the ages, 21, 20 and 18.
    readonly HashSet<string> classes = new HashSet<string>();
    readonly HashSet<string> countries = new HashSet<string>();
    readonly HashSet<string> clubs = new HashSet<string>();
    static readonly string[] ClassAges = { "21", "20", "18" };
    bool wired;

    // Women and men are never shown together. The choice is shared by My Runners and Choose a runner,
    // and remembered between games.
    const string WomenKey = "Filters.ShowWomen";
    static bool? showWomen;
    Button womenChip;
    Button menChip;

    public static bool ShowWomen
    {
        get
        {
            if (!showWomen.HasValue) showWomen = PlayerPrefs.GetInt(WomenKey, 0) == 1;
            return showWomen.Value;
        }
        set
        {
            showWomen = value;
            PlayerPrefs.SetInt(WomenKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

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
        MakeSortChips();
        for (int i = 0; i < sortButtons.Length; i++)
        {
            SortBy choice = (SortBy)i;
            sortButtons[i].onClick.AddListener(() => ChooseSort(choice));
        }
        if (orderChip != null) orderChip.onClick.AddListener(() => { reversed = !reversed; Notify(); });
        for (int i = 0; i < tierButtons.Length; i++)
        {
            int choice = TierOfButton(i);
            tierButtons[i].onClick.AddListener(() => { tierFilter = choice; Notify(); });
        }
        search.onValueChanged.AddListener(_ => Notify());
        minSpeed.onValueChanged.AddListener(_ => Notify());
        minTechnique.onValueChanged.AddListener(_ => Notify());
        if (clearButton != null) clearButton.onClick.AddListener(Clear);
        MakeGenderChips();
        MakeClassChip();
    }

    // Two chips, Women and Men, made from a tier chip and put between the search field and "Sort by".
    // The search field gets narrower to make room. Made here so the saved scenes need no rebuild.
    void MakeGenderChips()
    {
        if (tierButtons == null || tierButtons.Length == 0) return;
        RectTransform searchRect = (RectTransform)search.transform;
        float left = searchRect.anchoredPosition.x - searchRect.sizeDelta.x / 2f;
        float y = searchRect.anchoredPosition.y;
        searchRect.sizeDelta = new Vector2(240f, searchRect.sizeDelta.y);
        searchRect.anchoredPosition = new Vector2(left + 120f, y);
        TextMeshProUGUI placeholder = search.placeholder as TextMeshProUGUI;
        if (placeholder != null) placeholder.text = "Search...";

        womenChip = CopyChip("Women", left + 240f + 18f + 58f, y, 116f);
        menChip = CopyChip("Men", left + 240f + 18f + 116f + 10f + 46f, y, 92f);
        womenChip.onClick.AddListener(() => { ShowWomen = true; Notify(); });
        menChip.onClick.AddListener(() => { ShowWomen = false; Notify(); });
    }

    // Club and Country are added after Name, and a last chip turns the order round (best or worst first).
    // The sort chips get a little narrower so all of them fit before "Clear filters".
    void MakeSortChips()
    {
        if (sortButtons.Length < 4) return;
        RectTransform first = (RectTransform)sortButtons[0].transform;
        float y = first.anchoredPosition.y;
        float left = first.anchoredPosition.x - first.sizeDelta.x / 2f;
        Button[] all = new Button[6];
        for (int i = 0; i < 4; i++) all[i] = sortButtons[i];
        all[4] = CopyChip("Club", 0, y, 80, sortButtons[0]);
        all[5] = CopyChip("Country", 0, y, 116, sortButtons[0]);
        sortButtons = all;
        orderChip = CopyChip("Best first", 0, y, 150, sortButtons[0]);
        orderChip.name = "Sort order";

        float[] widths = { 112, 92, 136, 88, 80, 116 };
        float x = left;
        for (int i = 0; i < sortButtons.Length; i++)
        {
            Place(sortButtons[i], x, y, widths[i]);
            x += widths[i] + 8f;
        }
        Place(orderChip, x, y, 150f);
    }

    static void Place(Button chip, float left, float y, float width)
    {
        RectTransform rect = (RectTransform)chip.transform;
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        rect.anchoredPosition = new Vector2(left + width / 2f, y);
        TextMeshProUGUI text = chip.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null) text.fontSize = Mathf.Min(text.fontSize, 21f);
    }

    // Picking the chip that is already on turns the order round, like the order chip
    void ChooseSort(SortBy choice)
    {
        if (choice == sortBy) reversed = !reversed;
        else
        {
            sortBy = choice;
            reversed = false;
        }
        Notify();
    }

    bool ByText
    {
        get { return sortBy == SortBy.Name || sortBy == SortBy.Club || sortBy == SortBy.Country; }
    }

    // Three drop-down chips after the tiers: Class, Country and Club. Each opens a list where several can be
    // ticked. The tier chips and the sliders get a little narrower to make room.
    void MakeClassChip()
    {
        if (tierButtons == null || tierButtons.Length == 0) return;
        RectTransform first = (RectTransform)tierButtons[0].transform;
        float y = first.anchoredPosition.y;
        float x = first.anchoredPosition.x - first.sizeDelta.x / 2f;
        for (int i = 0; i < tierButtons.Length; i++)
        {
            Place(tierButtons[i], x, y, 84f);
            x += 90f;
        }
        classChip = CopyChip("Class", 0, y, 128f, tierButtons[0]);
        classChip.name = "Class";
        Place(classChip, x + 4f, y, 128f);
        x += 4f + 128f + 8f;
        countryChip = CopyChip("Country", 0, y, 150f, tierButtons[0]);
        countryChip.name = "Country filter";
        Place(countryChip, x + 4f, y, 150f);
        x += 4f + 150f + 8f;
        clubChip = CopyChip("Club", 0, y, 150f, tierButtons[0]);
        clubChip.name = "Club filter";
        Place(clubChip, x + 4f, y, 150f);
        x += 4f + 150f + 20f;

        classChip.onClick.AddListener(() => MultiSelectDropdown.Open((RectTransform)classChip.transform, "Class",
            ClassAges, classes, false, age => (ShowWomen ? "D" : "H") + age, Notify));
        countryChip.onClick.AddListener(() => MultiSelectDropdown.Open((RectTransform)countryChip.transform, "Country",
            CountryOptions(), countries, true, TitleCase, Notify));
        clubChip.onClick.AddListener(() => MultiSelectDropdown.Open((RectTransform)clubChip.transform, "Club",
            ClubOptions(), clubs, true, null, Notify));

        MoveSliders(x, y);
    }

    // The two "Min ..." sliders move right of the drop-downs
    void MoveSliders(float left, float y)
    {
        float labelWidth = 150f;
        float sliderWidth = 130f;
        PlaceRow(minSpeedLabel.rectTransform, left, y, labelWidth);
        left += labelWidth + 8f;
        PlaceRow((RectTransform)minSpeed.transform, left, y, sliderWidth);
        left += sliderWidth + 22f;
        labelWidth = 215f;
        PlaceRow(minTechniqueLabel.rectTransform, left, y, labelWidth);
        left += labelWidth + 8f;
        PlaceRow((RectTransform)minTechnique.transform, left, y, sliderWidth);
    }

    static void PlaceRow(RectTransform rect, float left, float y, float width)
    {
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        rect.anchoredPosition = new Vector2(left + width / 2f, y);
    }

    // Every country and club on the cards, A to Z
    static string[] CountryOptions()
    {
        SortedSet<string> set = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (CardData card in RunnerCards.All())
        {
            string country = RunnerCards.CountryName(card);
            if (!string.IsNullOrWhiteSpace(country)) set.Add(country);
        }
        string[] result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }

    static string[] ClubOptions()
    {
        SortedSet<string> set = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (CardData card in RunnerCards.All())
        {
            if (!string.IsNullOrWhiteSpace(card.clubName)) set.Add(card.clubName);
        }
        string[] result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }

    static string TitleCase(string text)
    {
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
    }

    // The chip's text: its name, or what is ticked
    static string ChipText(string name, HashSet<string> chosen, Func<string, string> show, string plural)
    {
        if (chosen.Count == 0) return name + " v";
        if (chosen.Count == 1)
        {
            foreach (string one in chosen) return Shorten(show(one), 16) + " v";
        }
        return chosen.Count + " " + plural + " v";
    }

    static string Shorten(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max - 1) + "...";
    }

    string ClassLabel()
    {
        if (classes.Count == 0) return "Class v";
        string sex = ShowWomen ? "D" : "H";
        List<string> picked = new List<string>();
        foreach (string age in ClassAges) if (classes.Contains(age)) picked.Add(sex + age);
        return string.Join(" + ", picked) + " v";
    }

    bool ClassMatches(CardData card)
    {
        if (classes.Count == 0) return true;
        string klass = card.klass ?? "";
        foreach (string age in classes)
        {
            if (klass.EndsWith(age, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    Button CopyChip(string label, float x, float y, float width, Button from = null)
    {
        if (from == null) from = tierButtons[0];
        Button chip = Instantiate(from, from.transform.parent);
        chip.name = "Gender " + label;
        chip.onClick = new Button.ButtonClickedEvent();
        RectTransform rect = (RectTransform)chip.transform;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        TextMeshProUGUI text = chip.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null) text.text = label;
        return chip;
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
            return tierFilter >= 0 || classes.Count > 0 || countries.Count > 0 || clubs.Count > 0 || !string.IsNullOrWhiteSpace(search.text)
                || minSpeed.value > minSpeed.minValue || minTechnique.value > minTechnique.minValue;
        }
    }

    public bool Matches(CardData card)
    {
        string wanted = search.text == null ? "" : search.text.Trim();
        if (card.man == ShowWomen) return false;
        if (wanted.Length > 0 && !Contains(card.RunnerName, wanted) && !Contains(card.clubName, wanted)
            && !Contains(RunnerCards.CountryName(card), wanted)) return false;
        if (tierFilter >= 0 && (int)RunnerCards.Tier(card) != tierFilter) return false;
        if (!ClassMatches(card)) return false;
        if (countries.Count > 0 && !countries.Contains(RunnerCards.CountryName(card))) return false;
        if (clubs.Count > 0 && (card.clubName == null || !clubs.Contains(card.clubName))) return false;
        if (Mathf.RoundToInt(card.speed) < minSpeed.value) return false;
        if (Mathf.RoundToInt(card.technique) < minTechnique.value) return false;
        return true;
    }

    // The search finds runners by name, club or country
    static bool Contains(string text, string wanted)
    {
        return text != null && text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Best first for the three numbers, A to Z for names, clubs and countries, or the other way
    // round. Runners in the same club or country come best first, and runners without a club last.
    public int Compare(CardData a, CardData b)
    {
        int result = 0;
        switch (sortBy)
        {
            case SortBy.Overall: result = RunnerCards.Overall(b).CompareTo(RunnerCards.Overall(a)); break;
            case SortBy.Speed: result = b.speed.CompareTo(a.speed); break;
            case SortBy.Technique: result = b.technique.CompareTo(a.technique); break;
            case SortBy.Name: result = string.Compare(a.RunnerName, b.RunnerName, StringComparison.OrdinalIgnoreCase); break;
            case SortBy.Club: result = CompareText(a.clubName, b.clubName); break;
            case SortBy.Country: result = CompareText(RunnerCards.CountryName(a), RunnerCards.CountryName(b)); break;
        }
        if (result == int.MaxValue || result == int.MinValue) return result > 0 ? 1 : -1;   // an empty club stays last
        if (reversed) result = -result;
        if (result == 0 && (sortBy == SortBy.Club || sortBy == SortBy.Country))
        {
            result = (b.speed + b.technique).CompareTo(a.speed + a.technique);
        }
        return result != 0 ? result : string.Compare(a.RunnerName, b.RunnerName, StringComparison.OrdinalIgnoreCase);
    }

    // A to Z, with an empty text after everything (int.MaxValue / MinValue so the order chip leaves it last)
    static int CompareText(string a, string b)
    {
        bool noA = string.IsNullOrWhiteSpace(a);
        bool noB = string.IsNullOrWhiteSpace(b);
        if (noA || noB) return noA == noB ? 0 : noA ? int.MaxValue : int.MinValue;
        return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
    }

    public void Clear()
    {
        tierFilter = -1;
        classes.Clear();
        countries.Clear();
        clubs.Clear();
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
        if (orderChip != null)
        {
            PaintChip(orderChip, reversed);
            TextMeshProUGUI order = orderChip.GetComponentInChildren<TextMeshProUGUI>(true);
            if (order != null) order.text = ByText ? (reversed ? "Z to A" : "A to Z") : (reversed ? "Worst first" : "Best first");
        }
        for (int i = 0; i < tierButtons.Length; i++) PaintChip(tierButtons[i], TierOfButton(i) == tierFilter);
        if (classChip != null)
        {
            PaintDrop(classChip, classes.Count > 0, ClassLabel());
            PaintDrop(countryChip, countries.Count > 0, ChipText("Country", countries, TitleCase, "countries"));
            PaintDrop(clubChip, clubs.Count > 0, ChipText("Club", clubs, c => c, "clubs"));
        }
        if (womenChip != null) PaintChip(womenChip, ShowWomen);
        if (menChip != null) PaintChip(menChip, !ShowWomen);
    }

    void PaintDrop(Button chip, bool on, string text)
    {
        PaintChip(chip, on);
        TextMeshProUGUI label = chip.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = text;
            label.enableWordWrapping = false;
            label.fontSize = Mathf.Min(label.fontSize, 21f);
        }
    }

    void PaintChip(Button chip, bool on)
    {
        chip.targetGraphic.color = on ? chipOn : chipOff;
        TextMeshProUGUI label = chip.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.color = on ? chipOnText : chipOffText;
    }
}
