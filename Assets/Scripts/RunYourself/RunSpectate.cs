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
    int inOrder;                 // controls punched in order so far (for "Leg 3 of 7" on the others' lists)

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

        Image card = CourseUI.CardPanel("Runners", watchCanvas.transform);
        RectTransform rect = card.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.sizeDelta = new Vector2(400f, -200f);
        rect.anchoredPosition = new Vector2(20f, 0f);

        watchTitle = CourseUI.Text("Title", card.transform, "Watch a runner", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(watchTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(360f, 70f));
        watchTitle.richText = true;

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

    void CloseWatchList()
    {
        StopWatching();
        if (watchCanvas != null)
        {
            own.Remove(watchCanvas);
            Destroy(watchCanvas.gameObject);
        }
        watchCanvas = null;
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
        if (Time.unscaledTime >= nextWatchRefresh)
        {
            nextWatchRefresh = Time.unscaledTime + 0.5f;
            FillWatchList();
        }
        if (!Watching)
        {
            if (watching != null) StopWatching();
            return;
        }
        Remote r = remotes[watching];
        if (watching.Object == null || !watching.Object.IsValid || !r.placed)
        {
            StopWatching();
            return;
        }
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
        // Those in first, quickest first; then those still out, the furthest first
        all.Sort((a, b) =>
        {
            if (a.raceFinished != b.raceFinished) return a.raceFinished ? -1 : 1;
            if (a.raceFinished) return a.relayTotals[0].CompareTo(b.relayTotals[0]);
            return b.run3dPunched.CompareTo(a.run3dPunched);
        });
        int inCount = 0;
        foreach (NetworkPlayer p in all) if (p.raceFinished) inCount++;
        string clock = RaceTime(gameSeconds * player.gameSpeed);
        watchTitle.text = "Watch a runner\n<size=70%><color=#5B7563>" + inCount + " of " + all.Count + " in   Race clock " + clock + "</color></size>";

        int place = 0;
        foreach (NetworkPlayer p in all)
        {
            bool me = p == NetworkPlayer.Local;
            string name = p.nickName.ToString();
            if (string.IsNullOrWhiteSpace(name)) name = "Player " + p.Object.StateAuthority.PlayerId;
            string state;
            if (p.raceFinished)
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
