using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The weekly and daily contests page, opened from the main menu (2026-11-19, the user's wish).
// Six contests each week (Monday 00:00 to Sunday 23:59, Swedish time): route choice game, race and run it yourself in
// 3D, each in the forest and on a sprint map. Three each day: route choice game, race and 3D, each on a sprint or a
// forest map as the cloud draws it (2026-11-20). All on AI courses. One try each, played alone. The leaderboard, the replays and the route choices of the others show only after your own run. Prize money is
// paid when the contest is over (the next time the game is opened): see Docs/cloud-code/Contests.js for the amounts.
public class ContestsPage : MonoBehaviour
{
    const string NameKey = "Contests.Name";
    static ContestsPage open;
    static bool claimedThisSession;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        open = null;
        claimedThisSession = false;
    }

    bool weekly = false;   // Today first (2026-11-19, the user)
    // The "Results" tab: the contests that are over, with their leaderboards and GPS routes (2026-11-24, the user)
    bool results;
    List<ContestRecent> recent;
    string recentError;
    bool recentBusy;
    Button resultsTab;
    List<ContestInfo> contests;
    string error;
    bool busy;

    RectTransform cards;
    ScrollRect cardsScroll;
    const float CardsWidth = 1820f;
    const float CardGap = 26f;
    const float MinCardWidth = 430f;
    // With more than four contests (the week has six) the cards are this wide, so a fifth one peeks in at the right
    // edge and shows there are more (2026-11-20, the user: hard to see that there were six)
    const float ScrollCardWidth = 400f;
    float cardWidth = MinCardWidth;
    int shownCount;
    Button leftArrow, rightArrow;
    Scrollbar cardsBar;
    TextMeshProUGUI countText;
    TextMeshProUGUI info;
    TextMeshProUGUI moneyText;
    Button weekTab, dayTab;
    TMP_InputField nameField;
    GameObject popup;
    GameObject loadingBox;
    RectTransform[] spinnerDots;
    Image[] spinnerImages;
    TextMeshProUGUI loadingText;

    public static bool IsOpen => open != null;

    public static void Open()
    {
        if (open != null) return;
        GameObject go = new GameObject("Contests page", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<ContestsPage>();
        open.Build();
        open.Reload();
        ShowTimePaid();
    }

    // "You were out 23 min 10 s and got 460 kr" after a contest run (2026-11-24, the user). No place here: you run
    // alone, so it would always say 1 of 1. The prize for the place comes when the contest is over.
    static void ShowTimePaid()
    {
        List<ContestRun.TimePaid> paid = ContestRun.TakeTimePaid();
        if (paid.Count == 0) return;
        int total = 0;
        List<string> lines = new List<string>();
        foreach (ContestRun.TimePaid p in paid)
        {
            total += p.kr;
            lines.Add(DescribeId(p.id, p.sprint) + ": you were out <b>" + CompetitionRewards.Duration(p.seconds) + "</b> and got <b>+" + Kr(p.kr) + "</b>");
        }
        // How the time money is counted is not told (2026-11-24, the user: only the developer needs to know)
        lines.Add("<size=85%><color=#5B7563>The prize for your place comes when the contest is over.</color></size>");
        ShowPrizePopup(lines, new List<string>(), total, 0, "You got " + Kr(total) + " for your run!");
    }

    void OnEnable() { Money.Changed += ShowMoney; PrizePacks.Changed += ShowPacks; }
    void OnDisable() { Money.Changed -= ShowMoney; PrizePacks.Changed -= ShowPacks; }

    Button packsButton;

    // "Open prize packs (2)" in the header while packs won in contests wait to be opened
    void ShowPacks()
    {
        if (packsButton == null) return;
        int count = PrizePacks.Count;
        packsButton.gameObject.SetActive(count > 0);
        TextMeshProUGUI label = packsButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = "Open prize packs (" + count + ")";
    }

    void OnDestroy()
    {
        if (open == this) open = null;
    }

    public static string PlayerName
    {
        get
        {
            string name = PlayerPrefs.GetString(LocalPrefs.Key(NameKey), "");
            if (string.IsNullOrWhiteSpace(name)) name = PlayerPrefs.GetString(LocalPrefs.Key("PlayerNickName"), "");
            return name.Trim();
        }
    }

    static string Kr(int amount) => amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ") + " kr";

    // ---- Page ----

    void Build()
    {
        Image back = CourseUI.Panel("Background", transform, CourseUI.Hex("DCE8D6"), false);
        CourseUI.Stretch(back.rectTransform);
        Sprite forest = Resources.Load<Sprite>("Sprites/e2bf7ebc-02a1-4b78-b52f-e0211eeac746");
        if (forest != null)
        {
            back.gameObject.AddComponent<RectMask2D>();
            Image picture = CourseUI.Panel("Forest", back.transform, Color.white, false);
            picture.sprite = forest;
            picture.raycastTarget = false;
            AspectRatioFitter fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = forest.rect.width / Mathf.Max(1f, forest.rect.height);
        }
        Transform page = back.transform;

        Button leave = CourseUI.Button("Back", page, "Back to menu", CourseUI.ButtonKind.Light, Close, 26);
        CourseUI.Place((RectTransform)leave.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48, -40), new Vector2(250, 66));

        // The one round money pill, as on every page (MoneyLabel.CreatePill)
        MoneyLabel.CreatePill(page, new Vector2(-48, -40), out moneyText);
        ShowMoney();

        // Top: title, the week/day tabs and your name
        Image head = CourseUI.CardPanel("Header", page);
        CourseUI.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(1180, 200));
        TextMeshProUGUI title = CourseUI.Text("Title", head.transform, "Contests", 44, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32, -18), new Vector2(400, 60));
        dayTab = CourseUI.Button("Day", head.transform, "Today", CourseUI.ButtonKind.Primary, () => { weekly = false; results = false; Fill(); }, 24);
        CourseUI.Place((RectTransform)dayTab.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32, -90), new Vector2(200, 58));
        weekTab = CourseUI.Button("Week", head.transform, "This week", CourseUI.ButtonKind.Light, () => { weekly = true; results = false; Fill(); }, 24);
        CourseUI.Place((RectTransform)weekTab.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(246, -90), new Vector2(200, 58));
        resultsTab = CourseUI.Button("Results", head.transform, "Results", CourseUI.ButtonKind.Light, ShowResults, 24);
        CourseUI.Place((RectTransform)resultsTab.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(460, -90), new Vector2(200, 58));
        HoverTip.Add(resultsTab, "Contests that are over: leaderboards and everyone's GPS routes (daily contests 7 days, weekly 30 days)");
        info = CourseUI.Text("Info", head.transform, "", 20, CourseUI.InkMuted);
        info.richText = true;
        CourseUI.Place(info.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32, 6), new Vector2(1116, 44));

        TextMeshProUGUI nameCaption = CourseUI.Text("Name caption", head.transform, "YOUR NAME ON THE LEADERBOARDS", 16, CourseUI.InkMuted, FontStyles.Bold);
        CourseUI.Place(nameCaption.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32, -24), new Vector2(380, 26));
        nameField = CourseUI.Input("Name", head.transform, "Write your name", 24, value =>
        {
            PlayerPrefs.SetString(LocalPrefs.Key(NameKey), value.Trim());
            PlayerPrefs.Save();
        });
        CourseUI.Place((RectTransform)nameField.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32, -56), new Vector2(380, 58));
        nameField.text = PlayerName;
        HoverTip.Add(nameField, "The name the others see next to your result.");

        packsButton = CourseUI.Button("Packs", head.transform, "Open prize packs", CourseUI.ButtonKind.Primary, () => PrizePacks.OpenAll(), 22);
        CourseUI.Place((RectTransform)packsButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32, 14), new Vector2(380, 54));
        HoverTip.Add(packsButton, "Runner packs you won in the contests.");
        ShowPacks();

        // The cards side by side; with more than four (the week has six, 2026-11-20) they scroll sideways
        RectTransform viewport = CourseUI.Rect("Cards view", page);
        CourseUI.Place(viewport, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -260), new Vector2(CardsWidth, 740));
        viewport.gameObject.AddComponent<RectMask2D>();
        Image catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = new Color(0, 0, 0, 0);
        cards = CourseUI.Rect("Cards", viewport);
        cards.anchorMin = new Vector2(0f, 1f);
        cards.anchorMax = new Vector2(0f, 1f);
        cards.pivot = new Vector2(0f, 1f);
        cards.anchoredPosition = Vector2.zero;
        cards.sizeDelta = new Vector2(CardsWidth, 720);
        HorizontalLayoutGroup row = CourseUI.Row(cards.gameObject, CardGap, new RectOffset(0, 0, 0, 0));
        row.childForceExpandWidth = false;
        row.childControlWidth = true;
        row.childAlignment = TextAnchor.UpperLeft;
        cardsScroll = viewport.gameObject.AddComponent<ScrollRect>();
        cardsScroll.content = cards;
        cardsScroll.viewport = viewport;
        cardsScroll.horizontal = true;
        cardsScroll.vertical = false;
        cardsScroll.movementType = ScrollRect.MovementType.Clamped;
        cardsScroll.scrollSensitivity = 70f;
        cardsScroll.inertia = true;

        // A scrollbar under the cards, arrows on their sides and "1-4 of 6", shown when the cards do not all fit
        Image barBack = CourseUI.Panel("Cards scrollbar", page, CourseUI.Hex("FBF4DA", 0.85f));
        CourseUI.Place(barBack.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -1012), new Vector2(CardsWidth, 22));
        RectTransform slide = CourseUI.Rect("Sliding area", barBack.transform);
        CourseUI.Stretch(slide, 3, 3, 3, 3);
        Image handle = CourseUI.Panel("Handle", slide, CourseUI.Accent);
        CourseUI.Stretch(handle.rectTransform);
        cardsBar = barBack.gameObject.AddComponent<Scrollbar>();
        cardsBar.handleRect = handle.rectTransform;
        cardsBar.targetGraphic = handle;
        cardsBar.direction = Scrollbar.Direction.LeftToRight;
        cardsScroll.horizontalScrollbar = cardsBar;
        // AutoHide: the ScrollRect itself hides the bar when all cards fit (Today's three). Permanent switched it back on
        // whatever the page did (2026-11-20, the user saw it under Today).
        cardsScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        leftArrow = CourseUI.Button("Left", page, "<", CourseUI.ButtonKind.Primary, () => StepCards(-1), 40);
        CourseUI.Place((RectTransform)leftArrow.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-CardsWidth / 2f - 6f, -600), new Vector2(72, 120));
        rightArrow = CourseUI.Button("Right", page, ">", CourseUI.ButtonKind.Primary, () => StepCards(1), 40);
        CourseUI.Place((RectTransform)rightArrow.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(CardsWidth / 2f + 6f, -600), new Vector2(72, 120));
        HoverTip.Add(leftArrow, "The contests to the left");
        HoverTip.Add(rightArrow, "More contests to the right");

        countText = CourseUI.Text("Count", page, "", 22, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(countText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -1046), new Vector2(700, 34));
        cardsScroll.onValueChanged.AddListener(_ => ShowScroll());
        ShowScroll();
        BuildLoading(page);
    }

    // A centred loading card with a spinning ring, instead of a message card squeezed into the left of the card row
    // (2026-11-20, the user: ugly)
    void BuildLoading(Transform page)
    {
        Image box = CourseUI.CardPanel("Loading", page);
        box.raycastTarget = false;
        CourseUI.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(720, 380));
        loadingBox = box.gameObject;
        const int dots = 12;
        spinnerDots = new RectTransform[dots];
        spinnerImages = new Image[dots];
        for (int i = 0; i < dots; i++)
        {
            Image dot = CourseUI.Panel("Dot", box.transform, CourseUI.Accent);
            dot.raycastTarget = false;
            float angle = i * Mathf.PI * 2f / dots;
            CourseUI.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(Mathf.Sin(angle) * 56f, 50f + Mathf.Cos(angle) * 56f), new Vector2(18, 18));
            spinnerDots[i] = dot.rectTransform;
            spinnerImages[i] = dot;
        }
        loadingText = CourseUI.Text("Text", box.transform, "Loading the contests", 34, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(loadingText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 100), new Vector2(660, 50));
        TextMeshProUGUI sub = CourseUI.Text("Sub", box.transform, "Asking Unity Cloud for today's and this week's contests", 22, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 46), new Vector2(660, 40));
        loadingBox.SetActive(false);
    }

    void AnimateLoading()
    {
        if (loadingBox == null || !loadingBox.activeSelf) return;
        float t = Time.unscaledTime;
        int head = Mathf.FloorToInt(t * 12f) % spinnerDots.Length;
        for (int i = 0; i < spinnerDots.Length; i++)
        {
            int behind = (head - i + spinnerDots.Length) % spinnerDots.Length;
            Color c = CourseUI.Accent;
            c.a = Mathf.Lerp(1f, 0.15f, behind / (float)(spinnerDots.Length - 1));
            spinnerImages[i].color = c;
        }
        loadingText.text = "Loading the contests" + new string('.', 1 + Mathf.FloorToInt(t * 2f) % 3);
    }

    void ShowMoney()
    {
        if (moneyText == null) return;
        moneyText.text = Money.Instance != null ? Kr(Money.Instance.GetMoney()) : "";
        moneyText.transform.parent.gameObject.SetActive(Money.Instance != null);
    }

    public void Close()
    {
        Destroy(gameObject);
    }

    void Update()
    {
        AnimateLoading();
        if (popup == null && Input.GetKeyDown(KeyCode.Escape) && FindObjectOfType<ContestReplay>() == null && FindObjectOfType<PackOpening>() == null) Close();
    }

    // ---- Loading ----

    async void Reload()
    {
        busy = true;
        error = null;
        Fill();
        try
        {
            if (await ContestRun.SendUnsentAsync()) UIToast.Show("Your last contest result has been sent.", 5f, CourseUI.Forest);
        }
        catch (Exception e) { Debug.LogWarning("Contests: unsent result: " + e.Message); }
        try
        {
            await ClaimPrizes(true);
            contests = await ContestCloud.StatusAsync();
        }
        catch (Exception e)
        {
            error = e.Message;
        }
        if (this == null) return;
        busy = false;
        Fill();
    }

    // Pays the prize money of contests that are over; also from the main menu in the background
    public static async void ClaimInBackground()
    {
        if (claimedThisSession) return;
        claimedThisSession = true;
        try { await ClaimPrizes(false); }
        catch (Exception e) { Debug.Log("Contests: no prizes checked (" + e.Message + ")"); }
    }

    static async Task ClaimPrizes(bool fromPage)
    {
        List<ContestPaid> paid = await ContestCloud.ClaimAsync();
        claimedThisSession = true;
        if (paid.Count == 0) return;
        int total = 0;
        int packs = 0;
        List<string> lines = new List<string>();
        foreach (ContestPaid p in paid)
        {
            total += p.prize;
            string what = DescribeId(p.id, p.sprint);
            if (!p.finished)
            {
                lines.Add(what + ": did not finish");
                continue;
            }
            List<string> won = new List<string>();
            if (p.packs != null)
            {
                foreach (string kind in p.packs) won.Add(PrizePacks.Name(kind));
                packs += p.packs.Count;
                PrizePacks.Add(p.packs);
            }
            if (p.prize > 0) won.Add(Kr(p.prize));
            lines.Add(what + ": " + CompetitionFlow.Ordinal(p.place) + " of " + p.of + "  <b>+" + string.Join(" + ", won) + "</b>");
        }
        if (total > 0 && Money.Instance != null) Money.Instance.AddMoney(total);
        GameAnalytics.Record("contest_prize", "kr", total, "packs", packs, "contests", paid.Count);
        ShowPrizePopup(lines, paid.ConvertAll(p => p.id), total, packs);
    }

    // "Weekly route choice game · Forest (week 47)"
    static string DescribeId(string id, bool sprint)
    {
        ContestInfo c = new ContestInfo { id = id, span = id.Substring(0, 1), kind = id.Substring(id.IndexOf('_') + 1), sprint = sprint };
        string when = c.Weekly ? "week " + id.Substring(6, 2).TrimStart('0') : id.Substring(1, 10);
        return ContestRun.SpanName(c) + " " + ContestRun.Title(c) + " (" + when + ")";
    }

    // ---- Cards ----

    void Fill()
    {
        if (cards == null) return;
        CourseUI.SetButtonColor(weekTab, weekly && !results ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        CourseUI.SetButtonColor(dayTab, !weekly && !results ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        if (resultsTab != null) CourseUI.SetButtonColor(resultsTab, results ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        foreach (Transform child in cards) Destroy(child.gameObject);
        shownCount = 0;
        cards.sizeDelta = new Vector2(CardsWidth, cards.sizeDelta.y);
        cards.anchoredPosition = Vector2.zero;
        ShowScroll();
        if (results)
        {
            if (loadingBox != null) loadingBox.SetActive(recentBusy);
            FillResults();
            return;
        }

        if (loadingBox != null) loadingBox.SetActive(busy);
        if (busy)
        {
            info.text = "";
            return;
        }
        if (contests == null)
        {
            info.text = "";
            Message("Could not load the contests.\n\n" + error, true);
            return;
        }
        List<ContestInfo> shown = contests.FindAll(c => c.Weekly == weekly);
        if (shown.Count == 0)
        {
            Message("No contests right now.", true);
            return;
        }
        ContestInfo first = shown[0];
        TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(first.endsAt) - DateTimeOffset.UtcNow;
        string leftText = left.TotalHours >= 24 ? (int)left.TotalDays + " d " + left.Hours + " h" : Mathf.Max(0, (int)left.TotalHours) + " h " + Mathf.Max(0, left.Minutes) + " min";
        int n = shown.Count;
        info.text = (weekly ? "Monday 00:00 to Sunday 23:59" : "00:00 to 23:59") + " Swedish time.  Ends in <b>" + leftText + "</b>.  One try each, played alone."
                    + (n > 4 ? "  <b>" + n + " contests</b>: use the arrows to see them all." : "");
        cardWidth = n > 4 ? ScrollCardWidth : Mathf.Max(MinCardWidth, (CardsWidth - CardGap * (n - 1)) / n);
        cards.sizeDelta = new Vector2(Mathf.Max(CardsWidth, n * cardWidth + (n - 1) * CardGap), cards.sizeDelta.y);
        cards.anchoredPosition = Vector2.zero;
        shownCount = n;
        foreach (ContestInfo c in shown) Card(c);
        ShowScroll();
    }

    // ---- Results: the contests that are over ----

    async void ShowResults()
    {
        results = true;
        if (recentBusy) { Fill(); return; }
        recentBusy = true;
        recentError = null;
        Fill();
        try { recent = await ContestCloud.RecentAsync(); }
        catch (Exception e) { recentError = e.Message; }
        if (this == null) return;
        recentBusy = false;
        if (results) Fill();
    }

    void FillResults()
    {
        info.text = "Contests that are over. GPS routes are kept <b>7 days</b> after a daily contest and <b>30 days</b> after a weekly one.";
        if (recentBusy) return;
        if (recent == null)
        {
            Message("Could not load the results.\n\n" + recentError, false);
            return;
        }
        if (recent.Count == 0)
        {
            Message("No contests have ended lately.", false);
            return;
        }
        Image card = CourseUI.CardPanel("Results", cards);
        CourseUI.Size(card, 700, CardsWidth);
        RectTransform list = CourseUI.ScrollList("Rows", card.transform, out ScrollRect scroll);
        RectTransform viewport = (RectTransform)scroll.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(30, 24);
        viewport.offsetMax = new Vector2(-30, -24);
        CourseUI.Column(list.gameObject, 8, new RectOffset(0, 0, 0, 0));
        FastScroll.Add(scroll);
        foreach (ContestRecent r in recent)
        {
            ContestRecent item = r;
            ContestInfo c = r.Info();
            Image row = CourseUI.Panel("Row", list, r.mine != null ? CourseUI.Hex("E8742A", 0.14f) : CourseUI.Soft);
            CourseUI.Size(row, 74);
            TextMeshProUGUI what = CourseUI.Text("What", row.transform, "<b>" + ContestRun.SpanName(c) + ": " + ContestRun.Title(c) + "</b>\n<size=18><color=#5B7563>"
                + WhenText(c) + "</color></size>", 23, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            what.richText = true;
            CourseUI.Place(what.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(620, 70));
            string winner = r.winner != null ? "Winner: <b>" + r.winner.name + "</b>  " + ResultText(c, r.winner.score, r.winner.seconds) : "Nobody finished";
            TextMeshProUGUI won = CourseUI.Text("Winner", row.transform, winner + "\n<size=18><color=#5B7563>" + r.entries + (r.entries == 1 ? " runner" : " runners") + "</color></size>",
                22, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            won.richText = true;
            CourseUI.Place(won.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(650, 0), new Vector2(520, 70));
            string mine = r.mine == null ? "<color=#5B7563>You did not run it</color>"
                : !r.mine.finished ? "<color=#C0392B>You did not finish</color>"
                : "You: <b>" + CompetitionFlow.Ordinal(r.mine.place) + "</b> of " + r.entries + "  " + ResultText(c, r.mine.score, r.mine.seconds);
            TextMeshProUGUI me = CourseUI.Text("Mine", row.transform, mine, 22, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            me.richText = true;
            CourseUI.Place(me.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(1190, 0), new Vector2(330, 70));
            Button open = CourseUI.Button("Open", row.transform, c.RouteChoice ? "Leaderboard & routes" : "Leaderboard & GPS", CourseUI.ButtonKind.Secondary, () => ShowPastBoard(item.id), 20);
            CourseUI.Place((RectTransform)open.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(250, 56));
        }
    }

    // "Saturday 22 November" or "Week 47 (17-23 Nov)"
    static string WhenText(ContestInfo c)
    {
        System.Globalization.CultureInfo english = System.Globalization.CultureInfo.InvariantCulture;
        DateTime end = c.endsAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(c.endsAt).LocalDateTime : DateTime.Now;
        if (!c.Weekly) return end.AddHours(-12).ToString("dddd d MMMM", english);
        DateTime monday = end.AddDays(-7);
        string week = c.id != null && c.id.Length >= 8 ? c.id.Substring(6, 2).TrimStart('0') : "";
        return "Week " + week + " (" + monday.ToString("d MMM", english) + " - " + end.AddHours(-12).ToString("d MMM", english) + ")";
    }

    static string ResultText(ContestInfo c, int score, int seconds)
    {
        return c.RouteChoice ? RouteChoiceGame.Points(score) + " p" : CompetitionFlow.FormatTime(seconds);
    }

    // A contest that is over, from the results list or the prize pop-up
    async void ShowPastBoard(string id)
    {
        ContestBoard board;
        try { board = await ContestCloud.BoardAsync(id); }
        catch (Exception e) { UIToast.Show(e.Message); return; }
        if (this == null) return;
        ContestInfo c = new ContestInfo
        {
            id = id, span = id.Substring(0, 1), kind = id.Substring(id.IndexOf('_') + 1),
            sprint = board.sprint, endsAt = board.endsAt, course = board.course
        };
        ShowBoardSheet(c, board);
    }

    // From the prize pop-up (over any screen): the contests page with that contest's leaderboard on top
    public static void OpenPast(string id)
    {
        if (open == null) Open();
        if (open != null) open.ShowPastBoard(id);
    }

    // How far the cards are scrolled: arrows only where there is more, the bar and "1-4 of 6" only when not all fit
    void ShowScroll()
    {
        if (cards == null || leftArrow == null) return;
        float hidden = cards.sizeDelta.x - CardsWidth;
        bool scrolls = shownCount > 4 && hidden > 1f && !busy;
        float x = -cards.anchoredPosition.x;
        leftArrow.gameObject.SetActive(scrolls && x > 4f);
        rightArrow.gameObject.SetActive(scrolls && x < hidden - 4f);
        countText.gameObject.SetActive(scrolls);
        if (!scrolls) return;
        float step = cardWidth + CardGap;
        int firstSeen = Mathf.Clamp(Mathf.RoundToInt(x / step), 0, shownCount - 1);
        int lastSeen = Mathf.Clamp(Mathf.FloorToInt((x + CardsWidth + CardGap) / step), firstSeen + 1, shownCount);
        countText.text = "Contests " + (firstSeen + 1) + "-" + lastSeen + " of " + shownCount;
    }

    // One arrow click moves a whole card
    void StepCards(int direction)
    {
        float hidden = cards.sizeDelta.x - CardsWidth;
        if (hidden <= 0f) return;
        float step = cardWidth + CardGap;
        float x = Mathf.Clamp(Mathf.Round(-cards.anchoredPosition.x / step + direction) * step, 0f, hidden);
        cardsScroll.StopMovement();
        cardsScroll.horizontalNormalizedPosition = x / hidden;
        ShowScroll();
    }

    void Message(string text, bool retry)
    {
        Image card = CourseUI.CardPanel("Message", cards);
        CourseUI.Size(card, 300, 900);
        TextMeshProUGUI label = CourseUI.Text("Text", card.transform, text, 28, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(label.rectTransform, 30, 30, 30, retry ? 100 : 30);
        if (retry)
        {
            Button again = CourseUI.Button("Again", card.transform, "Try again", CourseUI.ButtonKind.Primary, Reload, 24);
            CourseUI.Place((RectTransform)again.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(260, 60));
        }
    }

    void Card(ContestInfo c)
    {
        Image card = CourseUI.CardPanel(c.id, cards);
        CourseUI.Size(card, 700, cardWidth);
        CourseUI.Column(card.gameObject, 10, new RectOffset(26, 26, 24, 24));

        TextMeshProUGUI kind = CourseUI.Text("Kind", card.transform, (c.RouteChoice ? "ROUTE CHOICE GAME" : c.Run3D ? "RUN IT YOURSELF (3D)" : "RACE") + "  ·  " + (c.Sprint ? "SPRINT" : "FOREST"), 18, CourseUI.Accent, FontStyles.Bold);
        kind.enableWordWrapping = false;
        kind.enableAutoSizing = true;
        kind.fontSizeMin = 12;
        kind.fontSizeMax = 18;
        CourseUI.Size(kind, 26);
        string heading = c.RouteChoice ? (c.Sprint ? "Sprint route choices" : "Forest route choices")
                         : c.Run3D ? (c.Sprint ? "Sprint run in 3D" : "Forest run in 3D")
                         : (c.Sprint ? "Sprint race" : "Forest race");
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, heading, 32, CourseUI.Ink, FontStyles.Bold);
        title.enableWordWrapping = false;
        title.enableAutoSizing = true;
        title.fontSizeMin = 20;
        title.fontSizeMax = 32;
        CourseUI.Size(title, 44);

        CourseData course = ContestRun.ReadCourse(c.course);
        Image frame = CourseUI.Panel("Picture", card.transform, CourseUI.CardSolid);
        CourseUI.Size(frame, 186);
        frame.gameObject.AddComponent<RectMask2D>();
        Texture2D thumbnail = course != null ? CourseThumbnail.Get(course) : null;
        if (thumbnail != null && (c.Started || c.Done))
        {
            RawImage map = CourseUI.Rect("Map", frame.transform).gameObject.AddComponent<RawImage>();
            map.texture = thumbnail;
            map.raycastTarget = false;
            CourseUI.Stretch(map.rectTransform);
            AspectRatioFitter fit = map.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = thumbnail.width / Mathf.Max(1f, thumbnail.height);
        }
        else
        {
            // The course stays a secret until you run it, so nobody gets to study it first (2026-11-19, the user)
            TextMeshProUGUI hidden = CourseUI.Text("Hidden", frame.transform, "Random map and AI course\nshown when you start", 22, CourseUI.InkMuted, FontStyles.Bold, TextAlignmentOptions.Center);
            CourseUI.Stretch(hidden.rectTransform, 16, 16, 16, 16);
        }

        TextMeshProUGUI rules = CourseUI.Text("Rules", card.transform, "<b>" + ContestRun.CourseText(c, course) + "</b>\n" + ContestRun.RunnerText(c), 19, CourseUI.InkMuted);
        rules.richText = true;
        CourseUI.Size(rules, 96);
        TextMeshProUGUI prizes = CourseUI.Text("Prizes", card.transform, PrizeText(c), 19, CourseUI.Ink);
        prizes.richText = true;
        prizes.raycastTarget = true;
        HoverTip.Add(prizes, PacksTip(c));
        CourseUI.Size(prizes, 78);

        string state;
        if (c.Done) state = "<color=#3F7D54><b>Your result: " + (c.RouteChoice ? RouteChoiceGame.Points(c.mine.score) + " points" : CompetitionFlow.FormatTime(c.mine.seconds)) + "</b></color>";
        else if (c.Started) state = "<color=#C0392B><b>You started but did not finish.</b></color> That was your try; you can still see the leaderboard.";
        else state = c.entries + (c.entries == 1 ? " player has" : " players have") + " run it. Run it to see the leaderboard.";
        TextMeshProUGUI status = CourseUI.Text("Status", card.transform, state, 20, CourseUI.Ink);
        status.richText = true;
        status.raycastTarget = false;   // a long text must never sit over the Leaderboard button
        status.enableAutoSizing = true;
        status.fontSizeMin = 14;
        status.fontSizeMax = 20;
        CourseUI.Size(status, 56);

        if (c.Started)
        {
            Button board = CourseUI.Button("Leaderboard", card.transform, "Leaderboard (" + c.entries + ")", CourseUI.ButtonKind.Secondary, () => ShowBoard(c), 24);
            CourseUI.Size(board, 64);
        }
        else if (!c.Started)
        {
            Button run = CourseUI.Button("Run", card.transform, "Run (one try)", CourseUI.ButtonKind.Primary, () => AskStart(c), 26);
            CourseUI.Size(run, 64);
        }
    }

    // "Prizes: 1st Golden pack, 2nd Silver pack + Bronze pack, 3rd Silver pack, 4th-6th 250 kr+, everyone who finishes 200 kr."
    static string PrizeText(ContestInfo c)
    {
        if (!ContestCloud.PrizeRules.TryGetValue(c.span, out ContestPrizeRules rules) || rules.top == null || rules.top.Length < 3) return "";
        // The same sum as the cloud's prize(): base money plus a share of the bonus pot that grows with every player
        float bonus = rules.growth * c.entries;
        bool grows = rules.growth > 0;
        string Place(int i)
        {
            List<string> parts = new List<string>();
            if (rules.packs != null && i < rules.packs.Length && rules.packs[i] != null)
                foreach (string kind in rules.packs[i]) parts.Add(PrizePacks.Name(kind));
            float share = rules.share != null && i < rules.share.Length ? rules.share[i] : 0f;
            int money = Mathf.RoundToInt((rules.top[i] + bonus * share / 100f) / 5f) * 5;
            if (money > 0) parts.Add(Kr(money));
            return parts.Count > 0 ? string.Join(" + ", parts) : "-";
        }
        string text = "Prizes: 1st <b>" + Place(0) + "</b>, 2nd " + Place(1) + ", 3rd " + Place(2) + ", 4th-6th "
               + Kr(rules.next[rules.next.Length - 1]) + "+, everyone who finishes " + Kr(rules.tookPart) + ".";
        return grows ? text + " The top 3 get more money with every player." : text;
    }

    static string PacksTip(ContestInfo c)
    {
        return "Golden pack: " + PrizePacks.Odds(PrizePacks.Weekly) + ".\nSilver pack: " + PrizePacks.Odds(PrizePacks.Daily)
               + ".\nBronze pack: the shop's pack.\nPacks only hold runners you do not have yet."
               + "\nAt the finish you also get money for the time you were out.";
    }

    // ---- Starting ----

    void AskStart(ContestInfo c)
    {
        string name = nameField != null ? nameField.text.Trim() : PlayerName;
        if (string.IsNullOrEmpty(name))
        {
            UIToast.Show("Write your name first: it is shown on the leaderboard.");
            if (nameField != null) nameField.ActivateInputField();
            return;
        }
        Popup(ContestRun.SpanName(c) + ": " + ContestRun.Title(c),
            "You get <b>one try</b>. It starts as soon as you press Start, and leaving the race counts as your try.\n\n" + ContestRun.RunnerText(c),
            "Start", () => StartContest(c, name));
    }

    async void StartContest(ContestInfo c, string name)
    {
        if (busy) return;
        busy = true;
        UIProgressCard card = null;
        try
        {
            // Lay the course if nobody has yet (the same settings and seed whoever does it). Otherwise the cloud sends
            // the course only together with the start, so nobody can look at it without using their try.
            string courseJson = null;
            if (!c.laid)
            {
                CourseGenerator.Progress progress = new CourseGenerator.Progress();
                card = UIProgressCard.Show("You are first: laying this contest's AI course...");
                Task<string> laying = ContestRun.LayCourseAsync(c, progress);
                while (!laying.IsCompleted)
                {
                    await Task.Yield();
                    if (this == null) return;
                    card.Set(AICoursePanel.DescribeProgress(progress, out float share), share);
                }
                courseJson = await laying;
                card.Close();
                card = null;
            }

            card = UIProgressCard.Show("Starting the contest...");
            card.Set("Asking Unity Cloud", 0.9f);
            string startedJson = await ContestCloud.StartAsync(c.id, name, courseJson, ContestRun.AllMapIds());
            CourseData started = ContestRun.ReadCourse(startedJson);
            if (this == null) { card.Close(); return; }
            string problem = started == null ? "The contest course could not be read. Update the game." : CourseLibrary.StartProblem(started);
            if (problem != null) throw new ContestException(problem);
            card.Set("Loading the race", 1f);
            ContestRun.Begin(c, started);
        }
        catch (Exception e)
        {
            if (card != null) card.Close();
            UIToast.Show(e.Message);
            busy = false;
            if (this != null) Reload();
        }
    }

    // ---- Leaderboard ----

    // After a DNF the button did nothing (2026-11-24, the user): now the board always opens. Any error is shown and
    // logged, and when the cloud has no board for you (an older script, a run that never reached it) you still see
    // yourself as "Did not finish": no place, no prize and no money for the time out.
    bool boardLoading;

    async void ShowBoard(ContestInfo c)
    {
        if (boardLoading) return;
        boardLoading = true;
        ContestBoard board = null;
        try
        {
            board = await ContestCloud.BoardAsync(c.id);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Contests: leaderboard " + c.id + ": " + e);
            if (c.Done) UIToast.Show(e.Message);
            board = null;
        }
        finally { boardLoading = false; }
        if (this == null) return;
        if (board == null)
        {
            // Only your own DNF row, so the button never does nothing
            if (c.Done) return;
            board = new ContestBoard();
        }
        if (board.rows == null) board.rows = new List<ContestRow>();
        if (!c.Done && board.rows.Find(r => r.me) == null)
            board.rows.Add(new ContestRow { name = PlayerName, dnf = true, me = true, packs = new List<string>() });
        // A DNF has no place, prize or packs, whatever came back
        foreach (ContestRow r in board.rows)
        {
            if (!r.dnf) continue;
            r.place = 0;
            r.prize = 0;
            r.packs = new List<string>();
        }
        if (string.IsNullOrEmpty(board.course)) board.course = c.course;
        try { ShowBoardSheet(c, board); }
        catch (Exception e)
        {
            Debug.LogException(e);
            UIToast.Show("Could not show the leaderboard: " + e.Message);
        }
    }

    // The leaderboard: a summary (winner, your place, who beat you), every runner with their result and prize, and a
    // GPS switch on each row to pick whose routes to watch (2026-11-24, the user). No limit on how many since
    // 2026-11-26 (the user), with Show all / Hide all, and for races a button that shows the picked runners' routes
    // straight away, without the replay.
    void ShowBoardSheet(ContestInfo c, ContestBoard board)
    {
        List<ContestRow> rows = board.rows;
        float height = Mathf.Clamp(140 + 250 + rows.Count * 58f, 530f, 960f);
        GameObject sheet = PopupFrame(ContestRun.SpanName(c) + ": " + ContestRun.Title(c), 1260, height);
        Transform body = sheet.transform.GetChild(0);

        TextMeshProUGUI note = CourseUI.Text("Summary", body, Summary(c, board), 21, CourseUI.Ink);
        note.richText = true;
        CourseUI.Place(note.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -92), new Vector2(1180, 76));

        RectTransform list = CourseUI.ScrollList("Rows", body, out ScrollRect scroll);
        RectTransform viewport = (RectTransform)scroll.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(40, 166);
        viewport.offsetMax = new Vector2(-40, -176);
        CourseUI.Column(list.gameObject, 6, new RectOffset(0, 0, 0, 0));
        FastScroll.Add(scroll);

        // Picked for the map: the top three and you at first
        bool kept = board.ReplaysKept;
        List<ContestRow> picked = rows.FindAll(r => !r.dnf && (r.place <= 3 || r.me));
        Button watch = null, routes = null;
        List<(ContestRow row, Button toggle)> toggles = new List<(ContestRow, Button)>();
        void ShowWatch()
        {
            if (watch == null) return;
            string what = c.RouteChoice ? "See route choices" : "Watch GPS replay";
            CourseUI.SetButtonText(watch, !kept ? "GPS routes deleted" : what + " (" + picked.Count + ")");
            watch.interactable = kept && picked.Count > 0;
            if (routes != null) routes.interactable = kept && picked.Count > 0;
        }

        foreach (ContestRow r in rows)
        {
            ContestRow row = r;
            Image line = CourseUI.Panel("Row", list, r.me ? CourseUI.Hex("E8742A", 0.22f) : CourseUI.Soft);
            CourseUI.Size(line, 52);
            string result = r.dnf ? "Did not finish" : ResultText(c, r.score, r.seconds);
            TextMeshProUGUI place = CourseUI.Text("Place", line.transform, r.dnf ? "-" : r.place + ".", 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            CourseUI.Place(place.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(60, 50));
            TextMeshProUGUI who = CourseUI.Text("Name", line.transform, r.name + (r.me ? "  (you)" : ""), 24, CourseUI.Ink, r.me ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            CourseUI.Place(who.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(90, 0), new Vector2(440, 50));
            TextMeshProUGUI res = CourseUI.Text("Result", line.transform, result, 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            res.enableAutoSizing = true;
            res.fontSizeMin = 16;
            res.fontSizeMax = 24;
            CourseUI.Place(res.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(540, 0), new Vector2(160, 50));
            // The gap to the winner
            ContestRow first = rows.Count > 0 && !rows[0].dnf ? rows[0] : null;
            string gap = r.dnf || first == null || r.place <= 1 ? "" : c.RouteChoice
                ? "-" + RouteChoiceGame.Points(first.score - r.score) + " p"
                : "+" + RelayStats.Gap(r.seconds - first.seconds);
            TextMeshProUGUI behind = CourseUI.Text("Gap", line.transform, gap, 20, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            CourseUI.Place(behind.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(710, 0), new Vector2(110, 50));
            List<string> won = new List<string>();
            if (r.packs != null) foreach (string kind in r.packs) won.Add(PrizePacks.Name(kind));
            if (r.prize > 0) won.Add(Kr(r.prize));
            TextMeshProUGUI prize = CourseUI.Text("Prize", line.transform, string.Join(" + ", won), 20, CourseUI.Forest, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            prize.enableAutoSizing = true;
            prize.fontSizeMin = 14;
            prize.fontSizeMax = 20;
            CourseUI.Place(prize.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(830, 0), new Vector2(200, 50));
            if (r.dnf || !kept) continue;
            Button toggle = null;
            toggle = CourseUI.Button("GPS", line.transform, "", CourseUI.ButtonKind.Light, () =>
            {
                if (picked.Contains(row)) picked.Remove(row);
                else picked.Add(row);
                ShowToggle(toggle, picked.Contains(row), c.RouteChoice);
                ShowWatch();
            }, 18);
            CourseUI.Place((RectTransform)toggle.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(124, 40));
            HoverTip.Add(toggle, c.RouteChoice ? "Show this runner's picks" : "Show this runner's GPS route on the map");
            ShowToggle(toggle, picked.Contains(row), c.RouteChoice);
            toggles.Add((row, toggle));
        }

        string keptText = board.replaysUntil <= 0 ? "" : kept
            ? "GPS routes are kept until " + DateTimeOffset.FromUnixTimeMilliseconds(board.replaysUntil).LocalDateTime.ToString("d MMMM", System.Globalization.CultureInfo.InvariantCulture) + "."
            : "The GPS routes of this contest have been deleted.";
        TextMeshProUGUI keep = CourseUI.Text("Kept", body, (c.RouteChoice ? "Pick runners on the right, then see their picks leg by leg. " : "Pick runners on the right, then see their routes or watch the replay: the whole course or one leg at a time. ") + keptText,
            18, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        keep.enableAutoSizing = true;
        keep.fontSizeMin = 13;
        keep.fontSizeMax = 18;
        CourseUI.Place(keep.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 106), new Vector2(1180, 48));

        // Show all / Hide all: then tick the ones wanted
        void PickAll(bool all)
        {
            picked.Clear();
            if (all) foreach (ContestRow r in rows) if (!r.dnf) picked.Add(r);
            foreach (var (row, toggle) in toggles) ShowToggle(toggle, picked.Contains(row), c.RouteChoice);
            ShowWatch();
        }
        if (kept && toggles.Count > 0)
        {
            Button showAll = CourseUI.Button("Show all", body, "Show all", CourseUI.ButtonKind.Light, () => PickAll(true), 20);
            CourseUI.Place((RectTransform)showAll.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 34), new Vector2(170, 64));
            HoverTip.Add(showAll, "Pick every runner who finished");
            Button hideAll = CourseUI.Button("Hide all", body, "Hide all", CourseUI.ButtonKind.Light, () => PickAll(false), 20);
            CourseUI.Place((RectTransform)hideAll.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(222, 34), new Vector2(170, 64));
            HoverTip.Add(hideAll, "Pick nobody, then tick the runners you want to see");
        }
        watch = CourseUI.Button("Watch", body, "", CourseUI.ButtonKind.Primary, () => OpenReplays(c, board, new List<ContestRow>(picked), false), 24);
        CourseUI.Place((RectTransform)watch.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 34), new Vector2(400, 64));
        if (!c.RouteChoice && kept)
        {
            // Only the route choices: everyone's whole route at once, no replay (2026-11-26, the user)
            routes = CourseUI.Button("Routes", body, "See route choices", CourseUI.ButtonKind.Secondary, () => OpenReplays(c, board, new List<ContestRow>(picked), true), 22);
            CourseUI.Place((RectTransform)routes.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-456, 34), new Vector2(300, 64));
            HoverTip.Add(routes, "The picked runners' whole routes on the map at once, without the replay. One leg at a time with the arrows.");
        }
        ShowWatch();
    }

    static void ShowToggle(Button toggle, bool on, bool routeChoice)
    {
        CourseUI.SetButtonColor(toggle, on ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        CourseUI.SetButtonText(toggle, on ? (routeChoice ? "Shown" : "On map") : (routeChoice ? "Show" : "GPS"));
    }

    // "14 runners. Winner: Bo 31:20. You: 5th, +2:31 behind the winner; 4 runners beat you."
    static string Summary(ContestInfo c, ContestBoard board)
    {
        List<ContestRow> done = board.rows.FindAll(r => !r.dnf);
        if (done.Count == 0)
        {
            ContestRow me = board.rows.Find(r => r.me);
            return "Nobody has finished yet." + (me != null && me.dnf ? "  You did not finish: no place, no prize and no money for the time out." : "");
        }
        ContestRow first = done[0];
        List<ContestRow> winners = done.FindAll(r => r.place == 1);
        string text = done.Count + (done.Count == 1 ? " runner" : " runners") + ".  " + (winners.Count > 1 ? "Winners: " : "Winner: ")
            + "<b>" + string.Join(", ", winners.ConvertAll(r => r.name)) + "</b> " + ResultText(c, first.score, first.seconds) + ".";
        ContestRow mine = board.rows.Find(r => r.me);
        if (mine != null && mine.dnf) text += "  You did not finish: no place, no prize and no money for the time out.";
        else if (mine != null && mine.place == 1) text += winners.Count > 1 ? "  <b>You share the win!</b>" : "  <b>You won!</b>";
        else if (mine != null)
        {
            int ahead = done.FindAll(r => r.place < mine.place).Count;
            string gap = c.RouteChoice ? RouteChoiceGame.Points(first.score - mine.score) + " points" : RelayStats.Gap(mine.seconds - first.seconds);
            text += "  You: <b>" + CompetitionFlow.Ordinal(mine.place) + "</b>, " + gap + " behind the winner; " + ahead + (ahead == 1 ? " runner beat you." : " runners beat you.");
            ContestRow next = done.FindLast(r => r.place < mine.place);
            if (next != null && next.place > 1)
            {
                string nextGap = c.RouteChoice ? RouteChoiceGame.Points(next.score - mine.score) + " points" : RelayStats.Gap(mine.seconds - next.seconds);
                text += " Just ahead of you: " + next.name + " (" + nextGap + ").";
            }
        }
        text += board.over ? "" : "\n<color=#5B7563>Prize money is paid when the contest is over; it can still change as more players run.</color>";
        return text;
    }

    async void OpenReplays(ContestInfo c, ContestBoard board, List<ContestRow> picked, bool routesOnly)
    {
        if (picked.Count == 0) return;
        CourseData course = ContestRun.ReadCourse(board.course);
        if (course == null) course = ContestRun.ReadCourse(c.course);
        if (course == null)
        {
            // Read the board again: the course may have been laid since
            try
            {
                ContestBoard fresh = await ContestCloud.BoardAsync(c.id);
                course = ContestRun.ReadCourse(fresh.course);
            }
            catch (Exception) { }
        }
        if (course == null)
        {
            UIToast.Show("The course of this contest is gone (it is deleted together with the GPS routes).");
            return;
        }
        if (CourseLibrary.StartProblem(course) != null)
        {
            UIToast.Show("The map of this contest is not on this computer.");
            return;
        }
        // The cloud sends ContestCloud.MaxReplays at a time, so many runners are fetched in turns
        Dictionary<string, string> replays = new Dictionary<string, string>();
        UIProgressCard card = picked.Count > ContestCloud.MaxReplays ? UIProgressCard.Show("Fetching the GPS routes...") : null;
        try
        {
            for (int i = 0; i < picked.Count; i += ContestCloud.MaxReplays)
            {
                if (card != null) card.Set((i + 1) + "-" + Mathf.Min(picked.Count, i + ContestCloud.MaxReplays) + " of " + picked.Count, (float)i / picked.Count);
                List<ContestRow> part = picked.GetRange(i, Mathf.Min(ContestCloud.MaxReplays, picked.Count - i));
                Dictionary<string, string> got = await ContestCloud.ReplaysAsync(c.id, part.ConvertAll(r => r.playerId));
                foreach (var pair in got) replays[pair.Key] = pair.Value;
                if (this == null) { if (card != null) card.Close(); return; }
            }
        }
        catch (Exception e)
        {
            if (card != null) card.Close();
            UIToast.Show(e.Message);
            return;
        }
        if (card != null) card.Close();
        if (this == null) return;
        List<ContestReplay.Runner> runners = new List<ContestReplay.Runner>();
        picked.Sort((a, b) => a.place.CompareTo(b.place));
        foreach (ContestRow r in picked)
        {
            if (!replays.TryGetValue(r.playerId, out string json)) continue;
            ContestReplay.Runner runner = new ContestReplay.Runner { name = r.name, place = r.place, me = r.me };
            if (ContestReplay.Read(json, course, runner)) runners.Add(runner);
        }
        if (runners.Count == 0)
        {
            UIToast.Show("No GPS routes saved for these runners.");
            return;
        }
        if (runners.Count < picked.Count) UIToast.Show((picked.Count - runners.Count) + " of the runners have no GPS route saved.");
        ContestReplay.Open(course, c.RouteChoice, runners, null, routesOnly);
    }

    // ---- Pop-ups ----

    GameObject PopupFrame(string heading, float width, float height)
    {
        if (popup != null) Destroy(popup);
        Image dim = CourseUI.Panel("Popup", transform, new Color(0.05f, 0.1f, 0.07f, 0.6f), false);
        CourseUI.Stretch(dim.rectTransform);
        popup = dim.gameObject;
        // A click outside the card closes it too (2026-11-22, the user), not only Close
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(() => Destroy(dim.gameObject));
        Image card = CourseUI.CardPanel("Card", dim.transform);
        card.color = CourseUI.CardSolid;
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, heading, 36, CourseUI.Ink, FontStyles.Bold);
        // One line that shrinks to fit: "Weekly contest: Route choice game · Forest" was cut off in the start pop-up
        // (2026-11-24, the user)
        title.enableWordWrapping = false;
        title.enableAutoSizing = true;
        title.fontSizeMin = 18;
        title.fontSizeMax = 36;
        CourseUI.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(width - 260, 56));
        Button close = CourseUI.Button("Close", card.transform, "Close", CourseUI.ButtonKind.Light, () => Destroy(dim.gameObject), 22);
        CourseUI.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(160, 56));
        return dim.gameObject;
    }

    void Popup(string heading, string text, string action, Action onAction)
    {
        GameObject sheet = PopupFrame(heading, 1000, 440);
        Transform body = sheet.transform.GetChild(0);
        TextMeshProUGUI label = CourseUI.Text("Text", body, text, 24, CourseUI.Ink);
        label.richText = true;
        CourseUI.Stretch(label.rectTransform, 40, 40, 110, 120);
        Button go = CourseUI.Button("Action", body, action, CourseUI.ButtonKind.Primary, () =>
        {
            Destroy(sheet);
            onAction();
        }, 28);
        CourseUI.Place((RectTransform)go.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(320, 70));
    }

    // The prize money that was just paid, over whatever screen is showing. Each contest has a Leaderboard button
    // that opens its leaderboard and GPS routes (2026-11-24, the user: more fun than only the money)
    static void ShowPrizePopup(List<string> lines, List<string> ids, int total, int packs, string headingText = null)
    {
        GameObject go = new GameObject("Contest prizes", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        Image dim = CourseUI.Panel("Dim", go.transform, new Color(0.05f, 0.1f, 0.07f, 0.6f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(() => Destroy(go));
        Image card = CourseUI.CardPanel("Card", dim.transform);
        card.color = CourseUI.CardSolid;
        card.gameObject.AddComponent<ClickCatcher>();
        float height = 270 + 66 * lines.Count;
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, Mathf.Min(940, height)));
        string heading = packs > 0 ? "You won " + (packs == 1 ? "a runner pack" : packs + " runner packs") + (total > 0 ? " and " + Kr(total) : "") + "!"
                         : total > 0 ? "Contest prize money: +" + Kr(total) : "Contest results";
        if (headingText != null) heading = headingText;
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, heading, 38, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(1140, 56));
        RectTransform list = CourseUI.ScrollList("Lines", card.transform, out ScrollRect scroll);
        RectTransform viewport = (RectTransform)scroll.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(40, 110);
        viewport.offsetMax = new Vector2(-40, -100);
        CourseUI.Column(list.gameObject, 6, new RectOffset(0, 0, 0, 0));
        for (int i = 0; i < lines.Count; i++)
        {
            string id = i < ids.Count ? ids[i] : null;
            Image row = CourseUI.Panel("Line", list, CourseUI.Soft);
            CourseUI.Size(row, 60);
            TextMeshProUGUI text = CourseUI.Text("Text", row.transform, lines[i], 22, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            text.richText = true;
            text.enableAutoSizing = true;
            text.fontSizeMin = 15;
            text.fontSizeMax = 22;
            CourseUI.Stretch(text.rectTransform, 16, 250, 4, 4);
            if (id == null) continue;
            Button board = CourseUI.Button("Leaderboard", row.transform, "Leaderboard & GPS", CourseUI.ButtonKind.Secondary, () =>
            {
                Destroy(go);
                OpenPast(id);
            }, 18);
            CourseUI.Place((RectTransform)board.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(226, 46));
            HoverTip.Add(board, "See where you came, who beat you, and watch everyone's GPS routes");
        }
        if (packs > 0)
        {
            Button open = CourseUI.Button("Open", card.transform, packs == 1 ? "Open it now" : "Open them now", CourseUI.ButtonKind.Primary, () =>
            {
                Destroy(go);
                PrizePacks.OpenAll();
            }, 28);
            CourseUI.Place((RectTransform)open.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-150, 30), new Vector2(280, 66));
            Button later = CourseUI.Button("Later", card.transform, "Later", CourseUI.ButtonKind.Light, () => Destroy(go), 26);
            CourseUI.Place((RectTransform)later.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(150, 30), new Vector2(240, 66));
            HoverTip.Add(later, "They wait on the contests page (Open prize packs).");
            return;
        }
        Button ok = CourseUI.Button("OK", card.transform, "Nice!", CourseUI.ButtonKind.Primary, () => Destroy(go), 28);
        CourseUI.Place((RectTransform)ok.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, 66));
    }
}
