using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The weekly and daily contests page, opened from the main menu (2026-11-19, the user's wish).
// Four contests each week (Monday 00:00 to Sunday 23:59, Swedish time) and four each day: route choice game in the
// forest and on a sprint map, and a race in the forest and on a sprint map, all on AI courses. One try each, played
// alone. The leaderboard, the replays and the route choices of the others show only after your own run. Prize money is
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
    TextMeshProUGUI info;
    TextMeshProUGUI moneyText;
    Button weekTab, dayTab;
    TMP_InputField nameField;
    GameObject popup;

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
    }

    void OnEnable() { Money.Changed += ShowMoney; }
    void OnDisable() { Money.Changed -= ShowMoney; }

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

        Image moneyPill = CourseUI.Panel("Money pill", page, CourseUI.Card);
        if (CourseUI.Skin != null && CourseUI.Skin.pill != null) moneyPill.sprite = CourseUI.Skin.pill;
        CourseUI.Place(moneyPill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48, -40), new Vector2(220, 66));
        moneyText = CourseUI.Text("Amount", moneyPill.transform, "", 26, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(moneyText.rectTransform, 12, 12, 0, 0);
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
        CourseUI.Place(info.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32, 6), new Vector2(720, 44));

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

        cards = CourseUI.Rect("Cards", page);
        CourseUI.Place(cards, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -260), new Vector2(1820, 720));
        HorizontalLayoutGroup row = CourseUI.Row(cards.gameObject, 26, new RectOffset(0, 0, 0, 0));
        row.childForceExpandWidth = true;
        row.childAlignment = TextAnchor.UpperCenter;
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
        if (popup == null && Input.GetKeyDown(KeyCode.Escape) && FindObjectOfType<ContestReplay>() == null) Close();
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
        List<string> lines = new List<string>();
        foreach (ContestPaid p in paid)
        {
            total += p.prize;
            string what = DescribeId(p.id);
            lines.Add(p.finished ? what + ": " + CompetitionFlow.Ordinal(p.place) + " of " + p.of + "  <b>+" + Kr(p.prize) + "</b>" : what + ": did not finish");
        }
        if (total > 0 && Money.Instance != null) Money.Instance.AddMoney(total);
        ShowPrizePopup(lines, total);
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

        if (busy || contests == null)
        {
            info.text = busy ? "Loading the contests..." : "";
            Message(busy ? "Loading the contests..." : "Could not load the contests.\n\n" + error, !busy);
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
        info.text = (weekly ? "Monday 00:00 to Sunday 23:59" : "00:00 to 23:59") + " Swedish time.  Ends in <b>" + leftText + "</b>.  One try each, played alone.";
        foreach (ContestInfo c in shown) Card(c);
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
        CourseUI.Size(card, 700, 430);
        CourseUI.Column(card.gameObject, 10, new RectOffset(26, 26, 24, 24));

        TextMeshProUGUI kind = CourseUI.Text("Kind", card.transform, (c.RouteChoice ? "ROUTE CHOICE GAME" : "RACE") + "  ·  " + (c.Sprint ? "SPRINT" : "FOREST"), 18, CourseUI.Accent, FontStyles.Bold);
        CourseUI.Size(kind, 26);
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, c.RouteChoice ? (c.Sprint ? "Sprint route choices" : "Forest route choices") : (c.Sprint ? "Sprint race" : "Forest race"), 32, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 44);

        CourseData course = ContestRun.ReadCourse(c.course);
        Image frame = CourseUI.Panel("Picture", card.transform, CourseUI.CardSolid);
        CourseUI.Size(frame, 206);
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
        CourseUI.Size(rules, 76);
        TextMeshProUGUI prizes = CourseUI.Text("Prizes", card.transform, PrizeText(c), 19, CourseUI.Ink);
        prizes.richText = true;
        CourseUI.Size(prizes, 78);

        string state;
        if (c.Done) state = "<color=#3F7D54><b>Your result: " + (c.RouteChoice ? RouteChoiceGame.Points(c.mine.score) + " points" : CompetitionFlow.FormatTime(c.mine.seconds)) + "</b></color>";
        else if (c.Started) state = "<color=#C0392B><b>You started but did not finish.</b></color> The leaderboard opens when the contest is over.";
        else state = c.entries + (c.entries == 1 ? " player has" : " players have") + " run it. Run it to see the leaderboard.";
        TextMeshProUGUI status = CourseUI.Text("Status", card.transform, state, 20, CourseUI.Ink);
        status.richText = true;
        CourseUI.Size(status, 56);

        if (c.Done)
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

    static string PrizeText(ContestInfo c)
    {
        if (!ContestCloud.PrizeRules.TryGetValue(c.span, out ContestPrizeRules rules) || rules.top == null || rules.top.Length < 3) return "";
        float growth = 1f + rules.growth * Mathf.Max(0, c.entries - 3) / (float)(rules.top[0] + rules.top[1] + rules.top[2]);
        int First(int i) => Mathf.RoundToInt(rules.top[i] * growth / 5f) * 5;
        return "Prizes now: 1st <b>" + Kr(First(0)) + "</b>, 2nd " + Kr(First(1)) + ", 3rd " + Kr(First(2)) + ", 4th-6th "
               + Kr(rules.next[rules.next.Length - 1]) + "+, everyone who finishes " + Kr(rules.tookPart) + ". Top 3 grow with every player.";
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
            // Lay the course if nobody has yet (the same settings and seed whoever does it)
            string courseJson = c.course;
            if (string.IsNullOrEmpty(courseJson))
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
                courseJson = await ContestCloud.SetCourseAsync(c.id, await laying);
                card.Close();
                card = null;
            }
            CourseData course = ContestRun.ReadCourse(courseJson);
            string problem = CourseLibrary.StartProblem(course);
            if (course == null) problem = "The contest course could not be read. Update the game and try again.";
            if (problem != null) throw new ContestException(problem + " (Your try is not used.)");

            card = UIProgressCard.Show("Starting the contest...");
            card.Set("Asking Unity Cloud", 0.9f);
            courseJson = await ContestCloud.StartAsync(c.id, name);
            CourseData started = ContestRun.ReadCourse(courseJson) ?? course;
            if (this == null) { card.Close(); return; }
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

        GameObject sheet = PopupFrame(ContestRun.SpanName(c) + ": " + ContestRun.Title(c), 1100, 900);
        Transform body = sheet.transform.GetChild(0);
        TextMeshProUGUI note = CourseUI.Text("Note", body, rows.Count + (rows.Count == 1 ? " runner" : " runners")
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
            string result = c.RouteChoice ? RouteChoiceGame.Points(r.score) + " p" : CompetitionFlow.FormatTime(r.seconds);
            TextMeshProUGUI place = CourseUI.Text("Place", row.transform, r.place + ".", 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            CourseUI.Place(place.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(60, 50));
            TextMeshProUGUI who = CourseUI.Text("Name", row.transform, r.name + (r.me ? "  (you)" : ""), 24, CourseUI.Ink, r.me ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            CourseUI.Place(who.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(90, 0), new Vector2(520, 50));
            TextMeshProUGUI res = CourseUI.Text("Result", row.transform, result, 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            CourseUI.Place(res.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(620, 0), new Vector2(180, 50));
            TextMeshProUGUI prize = CourseUI.Text("Prize", row.transform, Kr(r.prize), 22, CourseUI.Forest, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            CourseUI.Place(prize.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(820, 0), new Vector2(170, 50));
        }

        // The top three and you, on the map
        List<ContestRow> picked = rows.FindAll(r => r.place <= 3 || r.me);
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
        Image card = CourseUI.CardPanel("Card", dim.transform);
        card.color = CourseUI.CardSolid;
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
    static void ShowPrizePopup(List<string> lines, int total)
    {
        GameObject go = new GameObject("Contest prizes", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        Image dim = CourseUI.Panel("Dim", go.transform, new Color(0.05f, 0.1f, 0.07f, 0.6f), false);
        CourseUI.Stretch(dim.rectTransform);
        Image card = CourseUI.CardPanel("Card", dim.transform);
        card.color = CourseUI.CardSolid;
        float height = 260 + 40 * lines.Count;
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, Mathf.Min(900, height)));
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, total > 0 ? "Contest prize money: +" + Kr(total) : "Contest results", 38, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(940, 56));
        TextMeshProUGUI text = CourseUI.Text("Lines", card.transform, string.Join("\n", lines), 24, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Top);
        text.richText = true;
        CourseUI.Stretch(text.rectTransform, 40, 40, 100, 110);
        Button ok = CourseUI.Button("OK", card.transform, "Nice!", CourseUI.ButtonKind.Primary, () => Destroy(go), 28);
        CourseUI.Place((RectTransform)ok.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, 66));
    }
}
