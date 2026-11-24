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
            lines.Add(DescribeId(p.id));
            lines.Add("You were out <b>" + CompetitionRewards.Duration(p.seconds) + "</b> and got <b>+" + Kr(p.kr) + "</b>");
            lines.Add("");
        }
        lines.Add("<size=85%><color=#5B7563>" + MoneyLabel.Format(CompetitionRewards.PerHalfMinute) + " for every 30 seconds out (at most "
            + MoneyLabel.Format(CompetitionRewards.MaxForTime) + "). The prize for your place comes when the contest is over.</color></size>");
        ShowPrizePopup(lines, total, 0, "You got " + Kr(total) + " for your run!");
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
        dayTab = CourseUI.Button("Day", head.transform, "Today", CourseUI.ButtonKind.Primary, () => { weekly = false; Fill(); }, 24);
        CourseUI.Place((RectTransform)dayTab.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32, -90), new Vector2(200, 58));
        weekTab = CourseUI.Button("Week", head.transform, "This week", CourseUI.ButtonKind.Light, () => { weekly = true; Fill(); }, 24);
        CourseUI.Place((RectTransform)weekTab.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(246, -90), new Vector2(200, 58));
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
            string what = DescribeId(p.id);
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
        ShowPrizePopup(lines, total, packs);
    }

    // "Weekly route choice game · Forest (week 47)"
    static string DescribeId(string id)
    {
        ContestInfo c = new ContestInfo { id = id, span = id.Substring(0, 1), kind = id.Substring(id.IndexOf('_') + 1) };
        string when = c.Weekly ? "week " + id.Substring(6, 2).TrimStart('0') : id.Substring(1, 10);
        return ContestRun.SpanName(c) + " " + ContestRun.Title(c) + " (" + when + ")";
    }

    // ---- Cards ----

    void Fill()
    {
        if (cards == null) return;
        CourseUI.SetButtonColor(weekTab, weekly ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        CourseUI.SetButtonColor(dayTab, weekly ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Primary);
        foreach (Transform child in cards) Destroy(child.gameObject);
        shownCount = 0;
        cards.sizeDelta = new Vector2(CardsWidth, cards.sizeDelta.y);
        cards.anchoredPosition = Vector2.zero;
        ShowScroll();

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
        CourseUI.Size(kind, 26);
        string heading = c.RouteChoice ? (c.Sprint ? "Sprint route choices" : "Forest route choices")
                         : c.Run3D ? (c.Sprint ? "Sprint run in 3D" : "Forest run in 3D")
                         : (c.Sprint ? "Sprint race" : "Forest race");
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, heading, 32, CourseUI.Ink, FontStyles.Bold);
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

    // "Prizes: 1st Weekly pack, 2nd Daily pack + Runner pack, 3rd Daily pack, 4th-6th 250 kr+, everyone who finishes 200 kr."
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
        return "Weekly pack: " + PrizePacks.Odds(PrizePacks.Weekly) + ".\nDaily pack: " + PrizePacks.Odds(PrizePacks.Daily)
               + ".\nRunner pack: the shop's pack.\nPacks only hold runners you do not have yet."
               + "\nAt the finish you also get " + MoneyLabel.Format(CompetitionRewards.PerHalfMinute) + " for every 30 seconds out.";
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

    async void ShowBoard(ContestInfo c)
    {
        List<ContestRow> rows;
        try { rows = await ContestCloud.BoardAsync(c.id); }
        catch (Exception e) { UIToast.Show(e.Message); return; }
        if (this == null) return;

        // As tall as the rows need, so the button sits just under the last row even with one runner (2026-11-19, the user)
        float height = Mathf.Clamp(140 + 120 + rows.Count * 58f, 420f, 900f);
        GameObject sheet = PopupFrame(ContestRun.SpanName(c) + ": " + ContestRun.Title(c), 1100, height);
        Transform body = sheet.transform.GetChild(0);
        int finished = rows.FindAll(r => !r.dnf).Count;
        TextMeshProUGUI note = CourseUI.Text("Note", body, finished + (finished == 1 ? " runner" : " runners")
            + ". Prize money is paid when the contest is over; it can still change as more players run.", 20, CourseUI.InkMuted);
        CourseUI.Place(note.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -96), new Vector2(1020, 34));

        RectTransform list = CourseUI.ScrollList("Rows", body, out ScrollRect scroll);
        RectTransform viewport = (RectTransform)scroll.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(40, 120);
        viewport.offsetMax = new Vector2(-40, -140);
        CourseUI.Column(list.gameObject, 6, new RectOffset(0, 0, 0, 0));
        FastScroll.Add(scroll);
        foreach (ContestRow r in rows)
        {
            Image row = CourseUI.Panel("Row", list, r.me ? CourseUI.Hex("E8742A", 0.22f) : CourseUI.Soft);
            CourseUI.Size(row, 52);
            string result = r.dnf ? "Did not finish" : c.RouteChoice ? RouteChoiceGame.Points(r.score) + " p" : CompetitionFlow.FormatTime(r.seconds);
            TextMeshProUGUI place = CourseUI.Text("Place", row.transform, r.dnf ? "-" : r.place + ".", 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            CourseUI.Place(place.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(60, 50));
            TextMeshProUGUI who = CourseUI.Text("Name", row.transform, r.name + (r.me ? "  (you)" : ""), 24, CourseUI.Ink, r.me ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            CourseUI.Place(who.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(90, 0), new Vector2(520, 50));
            TextMeshProUGUI res = CourseUI.Text("Result", row.transform, result, 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            res.enableAutoSizing = true;
            res.fontSizeMin = 16;
            res.fontSizeMax = 24;
            CourseUI.Place(res.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(620, 0), new Vector2(180, 50));
            List<string> won = new List<string>();
            if (r.packs != null) foreach (string kind in r.packs) won.Add(PrizePacks.Name(kind));
            if (r.prize > 0) won.Add(Kr(r.prize));
            TextMeshProUGUI prize = CourseUI.Text("Prize", row.transform, string.Join(" + ", won), 20, CourseUI.Forest, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            prize.enableAutoSizing = true;
            prize.fontSizeMin = 14;
            prize.fontSizeMax = 20;
            CourseUI.Place(prize.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(800, 0), new Vector2(210, 50));
        }

        // The top three and you, on the map
        List<ContestRow> picked = rows.FindAll(r => !r.dnf && (r.place <= 3 || r.me));
        if (picked.Count > 6) picked = picked.GetRange(0, 6);
        string label = c.RouteChoice ? "See the top 3's route choices" : "Watch the top 3's replay";
        Button watch = CourseUI.Button("Watch", body, label, CourseUI.ButtonKind.Primary, () => OpenReplays(c, picked), 24);
        CourseUI.Place((RectTransform)watch.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 34), new Vector2(460, 64));
        watch.interactable = picked.Count > 0;
    }

    async void OpenReplays(ContestInfo c, List<ContestRow> picked)
    {
        CourseData course = ContestRun.ReadCourse(c.course);
        if (course == null)
        {
            // The status was read before the course was laid: read it again
            try
            {
                contests = await ContestCloud.StatusAsync();
                ContestInfo fresh = contests.Find(x => x.id == c.id);
                course = fresh != null ? ContestRun.ReadCourse(fresh.course) : null;
            }
            catch (Exception) { }
        }
        if (course == null || CourseLibrary.StartProblem(course) != null)
        {
            UIToast.Show("The map of this contest is not on this computer.");
            return;
        }
        Dictionary<string, string> replays;
        try { replays = await ContestCloud.ReplaysAsync(c.id, picked.ConvertAll(r => r.playerId)); }
        catch (Exception e) { UIToast.Show(e.Message); return; }
        if (this == null) return;
        List<ContestReplay.Runner> runners = new List<ContestReplay.Runner>();
        foreach (ContestRow r in picked)
        {
            if (!replays.TryGetValue(r.playerId, out string json)) continue;
            ContestReplay.Runner runner = new ContestReplay.Runner { name = r.name, place = r.place, me = r.me };
            if (ContestReplay.Read(json, course, runner)) runners.Add(runner);
        }
        if (runners.Count == 0)
        {
            UIToast.Show("No replays saved for these runners.");
            return;
        }
        ContestReplay.Open(course, c.RouteChoice, runners, null);
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
        CourseUI.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(width - 260, 56));
        Button close = CourseUI.Button("Close", card.transform, "Close", CourseUI.ButtonKind.Light, () => Destroy(dim.gameObject), 22);
        CourseUI.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(160, 56));
        return dim.gameObject;
    }

    void Popup(string heading, string text, string action, Action onAction)
    {
        GameObject sheet = PopupFrame(heading, 900, 440);
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

    // The prize money that was just paid, over whatever screen is showing
    static void ShowPrizePopup(List<string> lines, int total, int packs, string headingText = null)
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
        float height = 260 + 40 * lines.Count;
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, Mathf.Min(900, height)));
        string heading = packs > 0 ? "You won " + (packs == 1 ? "a runner pack" : packs + " runner packs") + (total > 0 ? " and " + Kr(total) : "") + "!"
                         : total > 0 ? "Contest prize money: +" + Kr(total) : "Contest results";
        if (headingText != null) heading = headingText;
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, heading, 38, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(940, 56));
        TextMeshProUGUI text = CourseUI.Text("Lines", card.transform, string.Join("\n", lines), 24, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Top);
        text.richText = true;
        CourseUI.Stretch(text.rectTransform, 40, 40, 100, 110);
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
