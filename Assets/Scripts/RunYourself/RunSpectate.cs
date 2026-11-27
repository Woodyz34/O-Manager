using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Run it yourself online: watching the others after the finish (2026-11-19, Anders). Waiting at the finish for the
// rest was dull, so a list of the runners shows on the left (only after the finish; while running it would only take
// up the screen). Click a runner to see through their eyes: their head, their map when they hold it up (the leg they
// look at, turned, zoomed and dragged as they have it), their running. The list says how far everyone has got ("Leg 3 of 7") and the
// times of those in. The clock is the race clock everyone shares (everyone started together).
public partial class RunSession
{
    Canvas watchCanvas;
    RectTransform watchList;
    TextMeshProUGUI watchTitle;
    NetworkPlayer watching;
    float nextWatchRefresh;
    float watchedDoneSince = -1f;
    float shownPitch;
    RectTransform watchCard;
    Button watchTab;
    static readonly float[] WatchWidths = { 300f, 400f, 520f, 640f };
    int watchWidthStep = 1;
    bool watchOpen = true;
    float watchSlide = 1f;       // 1 = pushed out, 0 = pushed in
    int inOrder;                 // controls punched in order so far (for "Leg 3 of 7" on the others' lists)
    int watchedPunched;          // the controls the runner watched had punched, to beep when they punch the next
    bool watchedIn;              // the runner watched was in, to beep once at their finish

    bool Watching => watching != null && remotes.ContainsKey(watching);

    // The list of runners on the left, built when this runner is in and others still run
    void OpenWatchList()
    {
        if (watchCanvas != null) return;
        watchCanvas = new GameObject("Run watch", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
        watchCanvas.transform.SetParent(transform, false);
        watchCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        watchCanvas.sortingOrder = 60;
        own.Add(watchCanvas);
        ScreenSettings.Fit(watchCanvas.GetComponent<CanvasScaler>());

        // Flush against the left edge like a leaderboard (2026-11-23): can be pushed in and out with the tab on its
        // right edge, and made narrower or wider with the - and + buttons
        Image card = CourseUI.CardPanel("Runners", watchCanvas.transform);
        watchCard = card.rectTransform;
        watchCard.anchorMin = new Vector2(0f, 0f);
        watchCard.anchorMax = new Vector2(0f, 1f);
        watchCard.pivot = new Vector2(0f, 0.5f);
        watchCard.sizeDelta = new Vector2(WatchWidths[watchWidthStep], -200f);
        watchCard.anchoredPosition = new Vector2(watchOpen ? 0f : -WatchWidths[watchWidthStep], 0f);
        watchSlide = watchOpen ? 1f : 0f;

        watchTitle = CourseUI.Text("Title", card.transform, "Watch a runner", 28, CourseUI.Ink, FontStyles.Bold);
        RectTransform titleRect = watchTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(18f, -86f);
        titleRect.offsetMax = new Vector2(-104f, -14f);
        watchTitle.richText = true;

        Button smaller = CourseUI.IconButton("Smaller", card.transform, CourseUI.Icon.Minus, CourseUI.ButtonKind.Light, () => ResizeWatch(-1), 20);
        CourseUI.Place((RectTransform)smaller.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-54f, -22f), new Vector2(42f, 42f));
        HoverTip.Add(smaller, "Make the list narrower");
        Button larger = CourseUI.IconButton("Larger", card.transform, CourseUI.Icon.Plus, CourseUI.ButtonKind.Light, () => ResizeWatch(1), 20);
        CourseUI.Place((RectTransform)larger.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -22f), new Vector2(42f, 42f));
        HoverTip.Add(larger, "Make the list wider");

        Button tab = CourseUI.IconButton("Tab", card.transform, CourseUI.Icon.Arrow, CourseUI.ButtonKind.Secondary, () => { watchOpen = !watchOpen; }, 22);
        watchTab = tab;
        CourseUI.Place((RectTransform)tab.transform, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(46f, 120f));
        HoverTip.Add(tab, "Show or hide the list");

        watchList = CourseUI.ScrollList("List", card.transform, out ScrollRect scroll);
        RectTransform viewport = scroll.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(14f, 16f);
        viewport.offsetMax = new Vector2(-10f, -96f);
        CourseUI.Column(watchList.gameObject, 6, new RectOffset(0, 18, 0, 6));
        FastScroll.Add(scroll)?.OnLightCard();
        nextWatchRefresh = 0f;
    }

    void ResizeWatch(int by)
    {
        watchWidthStep = Mathf.Clamp(watchWidthStep + by, 0, WatchWidths.Length - 1);
        if (watchCard != null) watchCard.sizeDelta = new Vector2(WatchWidths[watchWidthStep], watchCard.sizeDelta.y);
    }

    // Slides the list in and out of the screen edge
    void SlideWatch()
    {
        if (watchCard == null) return;
        watchSlide = Mathf.MoveTowards(watchSlide, watchOpen ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        float eased = watchSlide * watchSlide * (3f - 2f * watchSlide);
        watchCard.anchoredPosition = new Vector2(-WatchWidths[watchWidthStep] * (1f - eased), 0f);
        CourseUI.PointIcon(watchTab, !watchOpen);
    }

    void CloseWatchList()
    {
        StopWatching();
        if (watchCanvas != null)
        {
            own.Remove(watchCanvas);
            Destroy(watchCanvas.gameObject);
        }
        watchCanvas = null;
        watchCard = null;
    }

    double ownE, ownN;
    float ownYaw;
    bool ownSaved;

    void Watch(NetworkPlayer p)
    {
        if (p == null || p == NetworkPlayer.Local || !remotes.ContainsKey(p)) return;
        // Where this runner stands, to go back there when the watching stops
        if (!ownSaved && player != null)
        {
            ownE = player.e;
            ownN = player.n;
            ownYaw = player.yaw;
            ownSaved = true;
        }
        watching = p;
        watchedDoneSince = -1f;
        // Their punches from now on: no beep for those already made
        watchedPunched = p.run3dPunched;
        watchedIn = p.raceFinished;
        shownPitch = p.run3dPitch;
        nextWatchRefresh = 0f;
    }

    void StopWatching()
    {
        watching = null;
        if (player != null)
        {
            player.puppet = false;
            if (ownSaved)
            {
                player.e = ownE;
                player.n = ownN;
                player.yaw = ownYaw;
                player.speed = 0f;
            }
        }
        ownSaved = false;
        if (handMap != null) handMap.SetUp(false);
    }

    // Every frame after the finish: the list now and then, and the eyes of the runner watched
    void WatchTick(float dt)
    {
        if (watchCanvas == null) return;
        SlideWatch();
        if (Time.unscaledTime >= nextWatchRefresh)
        {
            nextWatchRefresh = Time.unscaledTime + 0.5f;
            FillWatchList();
        }
        if (!Watching)
        {
            if (watching != null)
            {
                StopWatching();
                autoPicked = false;
            }
            return;
        }
        Remote r = remotes[watching];
        if (watching.Object == null || !watching.Object.IsValid || !r.placed)
        {
            StopWatching();
            // Only watching: on to someone else by itself (it used to stop there, at the start, with nobody moving)
            autoPicked = false;
            return;
        }
        // Their beeps, heard as if standing beside them (2026-11-27, Anders): a control punched, and the finish
        BeepForWatched();

        // A runner who is in: stay a moment, then on to someone still running
        if (watching.raceFinished)
        {
            if (watchedDoneSince < 0f) watchedDoneSince = Time.unscaledTime;
            if (Time.unscaledTime - watchedDoneSince > 4f)
            {
                NetworkPlayer next = NextRunner();
                if (next != null) Watch(next);
                else StopWatching();
                return;
            }
        }

        // The runner's eyes: our own camera put where they are, looking where they look
        player.puppet = true;
        player.e = r.shown.x;
        player.n = r.shown.y;
        player.yaw = r.yaw;
        shownPitch = Mathf.LerpAngle(shownPitch, watching.run3dPitch, 1f - Mathf.Exp(-dt * 12f));
        player.pitch = shownPitch;
        player.puppetSpeed = watching.raceFinished ? 0f : watching.run3dSpeed;
        player.puppetFallen = (watching.run3dFlags & FlagFallen) != 0;
        // Their map, when they hold it up: the leg they look at, turned as they have it
        bool up = (watching.run3dFlags & FlagMapUp) != 0 && !watching.raceFinished;
        if (handMap.Up != up) handMap.SetUp(up);
        handMap.Follow(watching.run3dMapLeg, watching.run3dMapTurn, watching.run3dMapAt, watching.run3dMapSize);
    }

    // The runner watched punched a control (run3dPunched went up) or came in (raceFinished): the same beep as their own
    void BeepForWatched()
    {
        if (sounds == null || watching == null) return;
        if (watching.raceFinished)
        {
            if (!watchedIn) sounds.Finish();
            watchedIn = true;
            watchedPunched = watching.run3dPunched;
            return;
        }
        int punched = watching.run3dPunched;
        // The finish counts in run3dPunched too, but its beep comes with raceFinished
        int controls = world != null ? world.FlagCount - 1 : int.MaxValue;
        if (punched > watchedPunched && watchedPunched < controls) sounds.Punch();
        watchedPunched = punched;
    }

    // The first runner still out, after the one watched now in the list's order
    NetworkPlayer NextRunner()
    {
        List<NetworkPlayer> all = Players();
        int start = Mathf.Max(0, all.IndexOf(watching));
        for (int k = 1; k <= all.Count; k++)
        {
            NetworkPlayer p = all[(start + k) % all.Count];
            if (p != NetworkPlayer.Local && !p.raceFinished && remotes.ContainsKey(p)) return p;
        }
        return null;
    }

    void FillWatchList()
    {
        foreach (Transform child in watchList) Destroy(child.gameObject);
        int legs = world != null ? world.FlagCount : 1;
        List<NetworkPlayer> all = Players();
        // Those in first, quickest first, the disqualified after them; then those still out, the furthest first
        all.Sort((a, b) =>
        {
            if (a.raceFinished != b.raceFinished) return a.raceFinished ? -1 : 1;
            if (a.raceFinished && a.raceDisqualified != b.raceDisqualified) return a.raceDisqualified ? 1 : -1;
            if (a.raceFinished) return a.relayTotals[0].CompareTo(b.relayTotals[0]);
            return b.run3dPunched.CompareTo(a.run3dPunched);
        });
        int inCount = 0;
        foreach (NetworkPlayer p in all) if (p.raceFinished) inCount++;
        // Someone who joined in the middle of the race has no clock of the race (it started before they came)
        string clock = watchOnly ? "" : "   Race clock " + RaceTime(gameSeconds * player.gameSpeed);
        watchTitle.text = "Watch a runner\n<size=70%><color=#5B7563>" + inCount + " of " + all.Count + " in" + clock + "</color></size>";

        int place = 0;
        foreach (NetworkPlayer p in all)
        {
            bool me = p == NetworkPlayer.Local;
            string name = p.nickName.ToString();
            if (string.IsNullOrWhiteSpace(name)) name = "Player " + p.Object.StateAuthority.PlayerId;
            string state;
            if (p.raceFinished && p.raceDisqualified) state = "DSQ  " + RaceTime(p.relayTotals[0]);
            else if (p.raceFinished)
            {
                place++;
                state = place + ".  " + RaceTime(p.relayTotals[0]);
            }
            else state = "Leg " + Mathf.Min(p.run3dPunched + 1, legs) + " of " + legs;
            bool on = p == watching;
            Color colour = me ? ColourOf(p, 0) : remotes.TryGetValue(p, out Remote r) ? r.track.color : ColourOf(p, 1);
            AddWatchRow(p, name + (me ? " (you)" : ""), state, colour, on, !me && remotes.ContainsKey(p));
        }
        if (watching != null)
        {
            Button stop = CourseUI.Button("Stop", watchList, "Stop watching", CourseUI.ButtonKind.Light, () =>
            {
                StopWatching();
                nextWatchRefresh = 0f;
            }, 22);
            CourseUI.Size(stop, 52);
        }
    }

    void AddWatchRow(NetworkPlayer p, string name, string state, Color colour, bool on, bool canWatch)
    {
        Image row = CourseUI.Panel("Row", watchList, on ? CourseUI.Forest : CourseUI.Soft);
        CourseUI.Size(row, 62);
        if (canWatch)
        {
            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;
            button.onClick.AddListener(() => Watch(p));
            HoverTip.Add(button, "See what " + name + " sees");
        }
        Image dot = CourseUI.Panel("Colour", row.transform, colour, false);
        CourseUI.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(14f, 14f));
        dot.raycastTarget = false;
        TextMeshProUGUI text = CourseUI.Text("Name", row.transform, "<b>" + name + "</b>\n<size=80%>" + state + "</size>", 22,
            on ? Color.white : CourseUI.Ink);
        text.richText = true;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        CourseUI.Stretch(text.rectTransform, 38, 10, 4, 4);
    }
}
