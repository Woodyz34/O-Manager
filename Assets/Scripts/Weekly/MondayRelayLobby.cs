using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Monday relay's lobby (MondayRelay): a card in the middle of the lobby with the countdown to 21:00 and the three
// relays to vote on. Everyone clicks the relay they want; the votes are counted 25 seconds before the start and the
// relay with the most votes is run (a tie, or no votes at all, is settled by lot, the same way on every computer).
//
// There is no host to press anything, so the game of whoever is the host now (Photon's master client) does it on the
// clock: it finds the three relays, closes the vote, sets the relay and starts the race at 21:00. What it has done is
// published on its own player (NetworkPlayer.mondayChoices, mondayState, mondayPick); every other game remembers what
// it has seen (MondayRelay.Remember), so whoever takes over when the host leaves carries on from there.
public class MondayRelayLobby : MonoBehaviour
{
    static MondayRelayLobby instance;

    // The relays found by this game (when it was the host), by id, so it can set the one that wins
    static readonly Dictionary<string, CourseData> found = new Dictionary<string, CourseData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        GameObject go = new GameObject("Monday relay lobby");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MondayRelayLobby>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        found.Clear();
        fetching = false;
    }

    // ---- The three relays, as sent in NetworkPlayer.mondayChoices ----

    public class Choice
    {
        public string id;
        public string name;
        public string author;
        public string map;
        public bool sprint;
        public List<float> legKm = new List<float>();
        public int Legs => legKm.Count;
    }

    const char FieldMark = '\u001f';
    const char EntryMark = '\u001e';

    // Small enough for the 512 characters of NetworkPlayer.mondayChoices: three relays with 10 legs each fit
    public static string Encode(List<CourseData> courses)
    {
        List<string> entries = new List<string>();
        foreach (CourseData course in courses)
        {
            MapDefinition map = MapDefinition.FindExact(course.mapId);
            string mapName = map != null ? map.displayName : SharedMaps.ListedName(course.mapId) ?? "";
            List<string> km = new List<string>();
            for (int leg = 0; leg < course.legs; leg++)
            {
                float meters = course.Route(leg, null).course.LengthMeters();
                km.Add((meters / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
            entries.Add(string.Join(FieldMark.ToString(), new[]
            {
                CourseData.SafeText(course.id, 16),
                CourseData.SafeText(course.name, 28),
                CourseData.SafeText(course.author, 16),
                CourseData.SafeText(mapName, 20),
                course.IsSprint() ? "s" : "f",
                string.Join(";", km)
            }));
        }
        string text = string.Join(EntryMark.ToString(), entries);
        return text.Length > 510 ? text.Substring(0, 510) : text;
    }

    public static List<Choice> Decode(string text)
    {
        List<Choice> choices = new List<Choice>();
        if (string.IsNullOrEmpty(text)) return choices;
        foreach (string entry in text.Split(EntryMark))
        {
            string[] f = entry.Split(FieldMark);
            if (f.Length < 6 || f[0].Length == 0) continue;
            Choice c = new Choice
            {
                id = f[0],
                name = CourseData.SafeText(f[1], 28),
                author = CourseData.SafeText(f[2], 16),
                map = CourseData.SafeText(f[3], 20),
                sprint = f[4] == "s"
            };
            foreach (string km in f[5].Split(';'))
            {
                if (float.TryParse(km, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                    c.legKm.Add(Mathf.Clamp(value, 0f, 99f));
            }
            if (c.legKm.Count == 0 || c.legKm.Count > CourseData.MaxLegs) continue;
            choices.Add(c);
            if (choices.Count >= MondayRelay.Candidates) break;
        }
        return choices;
    }

    // ---- State seen in the lobby ----

    MondayRelay.Event ev;
    string choicesText = "";
    List<Choice> choices = new List<Choice>();
    int state;
    int pick;
    readonly int[] votes = new int[MondayRelay.Candidates];
    int players;
    string hostNote = "";
    float nextTick;
    float mapWaitSince = -1f;

    void Update()
    {
        bool show = ShouldShow();
        if (show && root == null) Build();
        if (root != null && root.activeSelf != show) root.SetActive(show);
        if (!show) return;

        if (Time.unscaledTime >= nextTick)
        {
            nextTick = Time.unscaledTime + 0.5f;
            Tick();
        }
        Refresh();
    }

    bool ShouldShow()
    {
        if (SceneManager.GetActiveScene().name != "Lobby") return false;
        if (LobbyVisit.Active) return false;
        return MondayRelay.Active;
    }

    void Tick()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.Object == null || !local.Object.IsValid || local.Runner == null) return;
        ev = MondayRelay.ActiveEvent;
        if (!ev.IsValid) return;
        bool host = local.Runner.IsSharedModeMasterClient;

        // What the host has published (the host's own values are read from its player too)
        string seenChoices = "";
        int seenState = 0, seenPick = 0;
        players = 0;
        for (int i = 0; i < votes.Length; i++) votes[i] = 0;
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player == null || player.Object == null || !player.Object.IsValid) continue;
            players++;
            int vote = player.mondayVote;
            if (vote >= 1 && vote <= votes.Length) votes[vote - 1]++;
            if (player.mondayState > seenState)
            {
                seenState = player.mondayState;
                seenChoices = player.mondayChoices.ToString();
                seenPick = player.mondayPick;
            }
        }
        MondayRelay.Remember(ev.Key, seenChoices, seenState, seenPick);
        MondayRelay.Recall(ev.Key, out choicesText, out state, out pick);
        if (choicesText != lastDecoded)
        {
            lastDecoded = choicesText;
            choices = Decode(choicesText);
        }

        if (host) HostTick(local);
        else if (local.mondayState != 0)
        {
            // Was the host before: the new host publishes now
            local.mondayState = 0;
            local.mondayChoices = "";
            local.mondayPick = 0;
        }
    }

    string lastDecoded;

    // ---- What the host's game does on the clock ----

    static bool fetching;

    void HostTick(NetworkPlayer local)
    {
        DateTime now = MondayRelay.NowUtc;
        ReadyUIHandler ready = FindObjectOfType<ReadyUIHandler>();
        // Nobody was here in time: this week's relay is not run any more
        if (now >= ev.LateStartUtc && state < MondayRelay.StateStarted) return;

        // 1. Find three relays to vote on
        if (state < MondayRelay.StateVoting)
        {
            if (!fetching) FindCandidates();
            hostNote = "Looking for relays...";
        }
        // 2. Close the vote
        else if (state == MondayRelay.StateVoting && now >= ev.VoteClosesUtc)
        {
            if (choices.Count == 0)
            {
                hostNote = "No relay was found for this week.";
            }
            else
            {
                pick = CountVotes() + 1;
                state = MondayRelay.StateChosen;
            }
        }
        // 3. Set the relay that won, and start at 21:00
        if (state == MondayRelay.StateChosen && pick >= 1 && pick <= choices.Count)
        {
            CourseData course = ChosenCourse();
            if (course == null)
            {
                hostNote = "Getting the relay...";
                if (!fetching) FindCandidates();   // this game took over from a host who left: fetch the relays again
            }
            else
            {
                MapHandler mapHandler = FindObjectOfType<MapHandler>();
                if (mapHandler != null && (CourseLibrary.Current == null || CourseLibrary.Current.id != course.id)) mapHandler.Select(course);
                if (now >= ev.startUtc && ready != null) TryStart(ready, course, now);
            }
        }

        // Publish (only what changed: every change is sent to everyone)
        string text = choicesText ?? "";
        if (local.mondayChoices.ToString() != text) local.mondayChoices = text;
        if (local.mondayState != state) local.mondayState = state;
        if (local.mondayPick != pick) local.mondayPick = pick;
        MondayRelay.Remember(ev.Key, text, state, pick);
    }

    void TryStart(ReadyUIHandler ready, CourseData course, DateTime now)
    {
        // The host's own game needs the map too
        MapDefinition map = MapDefinition.FindExact(course.mapId);
        if (map == null || !MapPackages.GridExists(map.gridName))
        {
            hostNote = "Getting the map...";
            SharedMaps.RefreshForLobby();
            return;
        }
        // Everyone else gets a few minutes to get the map, then the relay starts without waiting any longer
        bool waitedLongEnough = (now - ev.startUtc).TotalSeconds >= MondayRelay.MapWaitSeconds;
        string problem = PrivateMaps.StartProblem(course);
        if (problem != null && !waitedLongEnough)
        {
            hostNote = problem;
            return;
        }
        if (ready.StartMondayRelay(course, waitedLongEnough))
        {
            state = MondayRelay.StateStarted;
            hostNote = "";
        }
    }

    // The winner (0-based): the most votes; a tie or no votes at all is settled by lot, the same on every computer
    int CountVotes()
    {
        int best = -1;
        List<int> tied = new List<int>();
        for (int i = 0; i < choices.Count; i++)
        {
            if (votes[i] > best)
            {
                best = votes[i];
                tied.Clear();
            }
            if (votes[i] == best) tied.Add(i);
        }
        if (tied.Count == 0) return 0;
        return tied[(int)(MondayRelay.Hash("tie|" + ev.Key) % (uint)tied.Count)];
    }

    CourseData ChosenCourse()
    {
        if (pick < 1 || pick > choices.Count) return null;
        found.TryGetValue(choices[pick - 1].id, out CourseData course);
        return course;
    }

    async void FindCandidates()
    {
        fetching = true;
        string key = ev.Key;
        try
        {
            List<CourseData> all = new List<CourseData>();
            try { all.AddRange(await CloudCourses.FetchCommunityAsync(true)); }
            catch (Exception e) { Debug.LogWarning("Monday relay: could not fetch the players' relays: " + e.Message); }
            foreach (CourseData course in all) if (course != null && !string.IsNullOrEmpty(course.id)) found[course.id] = course;
            foreach (CourseData course in CourseLibrary.BuiltIn) if (course != null && !string.IsNullOrEmpty(course.id)) found[course.id] = course;

            if (this == null || ev.Key != key) return;
            // Only the first host finds the relays; a host who takes over keeps the ones already shown
            MondayRelay.Recall(key, out string already, out int seenState, out _);
            if (seenState >= MondayRelay.StateVoting) return;

            List<CourseData> picked = MondayRelay.PickCandidates(all, key, true);
            // Too few players' relays this week: built-in relays fill up the three
            if (picked.Count < MondayRelay.Candidates)
            {
                List<CourseData> builtIn = MondayRelay.PickCandidates(CourseLibrary.BuiltIn, key, true);
                foreach (CourseData course in builtIn)
                {
                    if (picked.Count >= MondayRelay.Candidates) break;
                    if (!picked.Exists(c => c.id == course.id)) picked.Add(course);
                }
            }
            // Still none: any relay with 3 legs or more, however long the legs are
            if (picked.Count == 0)
            {
                List<CourseData> everything = new List<CourseData>(all);
                everything.AddRange(CourseLibrary.BuiltIn);
                picked = MondayRelay.PickCandidates(everything, key, false);
            }
            if (picked.Count == 0)
            {
                hostNote = "No relay with " + MondayRelay.MinLegs + " legs or more, every leg longer than 3 km, was found.";
                return;
            }
            choicesText = Encode(picked);
            lastDecoded = null;
            choices = Decode(choicesText);
            state = MondayRelay.StateVoting;
            MondayRelay.Remember(key, choicesText, state, 0);
        }
        finally
        {
            fetching = false;
        }
    }

    // ---- The card ----

    GameObject root;
    TextMeshProUGUI title;
    TextMeshProUGUI clock;
    TextMeshProUGUI status;
    TextMeshProUGUI footer;
    readonly List<Button> rows = new List<Button>();
    readonly List<TextMeshProUGUI> rowTexts = new List<TextMeshProUGUI>();
    readonly List<TextMeshProUGUI> rowVotes = new List<TextMeshProUGUI>();

    void Build()
    {
        root = new GameObject("Monday relay card", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4;   // above the lobby, under the chat (150), pop-ups and the Esc menu
        ScreenSettings.Fit(root.GetComponent<CanvasScaler>());

        Image card = CourseUI.CardPanel("Card", root.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(900, 780));

        TextMeshProUGUI caption = CourseUI.Text("Caption", card.transform, "MONDAY RELAY  ·  EVERY MONDAY AT 21:00", 18, CourseUI.InkMuted, FontStyles.Bold, TextAlignmentOptions.Center);
        caption.characterSpacing = 6f;
        CourseUI.Place(caption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(840, 30));

        title = CourseUI.Text("Title", card.transform, "", 40, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -58), new Vector2(840, 54));

        clock = CourseUI.Text("Clock", card.transform, "", 30, CourseUI.Accent, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(clock.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -116), new Vector2(840, 40));

        status = CourseUI.Text("Status", card.transform, "", 20, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Place(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(840, 54));

        for (int i = 0; i < MondayRelay.Candidates; i++)
        {
            int index = i;
            Button row = CourseUI.Button("Relay " + (i + 1), card.transform, "", CourseUI.ButtonKind.Light, () => Vote(index + 1), 20);
            CourseUI.Place((RectTransform)row.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -228 - i * 150), new Vector2(840, 136));
            // The button's own label is replaced by a left-aligned block of text and the votes on the right
            TextMeshProUGUI own = row.GetComponentInChildren<TextMeshProUGUI>();
            if (own != null) own.gameObject.SetActive(false);
            TextMeshProUGUI text = CourseUI.Text("Relay text", row.transform, "", 19, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            CourseUI.Stretch(text.rectTransform, 28, 170, 8, 8);
            text.enableWordWrapping = true;
            TextMeshProUGUI voteText = CourseUI.Text("Votes", row.transform, "", 22, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            CourseUI.Place(voteText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-26, 0), new Vector2(150, 110));
            rows.Add(row);
            rowTexts.Add(text);
            rowVotes.Add(voteText);
        }

        footer = CourseUI.Text("Footer", card.transform, "", 18, CourseUI.InkMuted, FontStyles.Italic, TextAlignmentOptions.Center);
        CourseUI.Place(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(840, 64));
    }

    void Vote(int which)
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.Object == null || !local.Object.HasStateAuthority) return;
        if (state != MondayRelay.StateVoting || which < 1 || which > choices.Count) return;
        local.mondayVote = local.mondayVote == which ? 0 : which;   // a second click takes the vote back
        nextTick = 0f;
    }

    void Refresh()
    {
        if (!ev.IsValid || title == null) return;
        DateTime now = MondayRelay.NowUtc;
        title.text = ev.FunName;
        NetworkPlayer local = NetworkPlayer.Local;
        int myVote = local != null && local.Object != null && local.Object.IsValid ? local.mondayVote : 0;

        // The clock line and what is going on
        string clockText;
        string statusText;
        if (state >= MondayRelay.StateStarted || now >= ev.LateStartUtc)
        {
            MondayRelay.Event next = MondayRelay.Next(now);
            clockText = "This week's relay is over";
            statusText = "Next: " + next.FunName + ", " + MondayRelay.SwedishTime(next.startUtc).ToString("dddd d MMMM", System.Globalization.CultureInfo.InvariantCulture)
                + " at 21:00. Leave the lobby from the top left.";
        }
        else if (now < ev.startUtc)
        {
            clockText = "Starts in " + MondayRelay.Countdown(ev.startUtc - now);
            if (state == MondayRelay.StateVoting)
            {
                statusText = choices.Count == 1
                    ? "Only one relay could be found this week. It starts by itself at 21:00."
                    : "Vote for the relay you want to run. The votes are counted " + (int)MondayRelay.VoteClosesBefore.TotalSeconds + " seconds before the start.";
            }
            else if (state == MondayRelay.StateChosen && pick >= 1 && pick <= choices.Count)
            {
                statusText = "<b>" + choices[pick - 1].name + "</b> won the vote! Get ready to pick your runners.";
            }
            else statusText = hostNote.Length > 0 ? hostNote : "Looking for this week's relays...";
        }
        else
        {
            clockText = "Starting now...";
            statusText = hostNote.Length > 0 && CompetitionFlow.LocalIsHost() ? hostNote : "Waiting for everyone to be ready.";
        }
        clock.text = clockText;
        status.text = statusText;

        // The relays
        for (int i = 0; i < rows.Count; i++)
        {
            bool has = i < choices.Count;
            if (rows[i].gameObject.activeSelf != has) rows[i].gameObject.SetActive(has);
            if (!has) continue;
            Choice c = choices[i];
            bool won = state >= MondayRelay.StateChosen && pick == i + 1;
            bool lost = state >= MondayRelay.StateChosen && pick != i + 1;
            List<LegSex> classes = MondayRelay.LegClasses(ev.Key, c.id, c.Legs);
            List<string> legs = new List<string>();
            for (int leg = 0; leg < c.Legs; leg++)
                legs.Add("<b>" + RunnerRules.SexName(classes[leg]) + "</b> " + c.legKm[leg].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km");
            string kind = c.sprint ? "<color=#E8742A>Sprint</color>" : "<color=#3F7D54>Forest</color>";
            string by = c.author.Length > 0 ? "  ·  by " + c.author : "";
            string map = c.map.Length > 0 ? "  ·  " + c.map : "";
            rowTexts[i].text = "<size=125%><b>" + c.name + "</b></size>\n<size=90%>" + c.Legs + " legs  ·  " + kind + map + by + "</size>\n<size=90%>" + string.Join("   ", legs) + "</size>";
            int count = votes[i];
            rowVotes[i].text = (won ? "<color=#E8742A>WINNER</color>\n" : myVote == i + 1 ? "<size=80%>Your vote</size>\n" : "")
                + count + (count == 1 ? " vote" : " votes");
            CourseUI.ButtonKind kindOfRow = won ? CourseUI.ButtonKind.Primary : myVote == i + 1 ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light;
            Image image = rows[i].GetComponent<Image>();
            Color back = kindOfRow == CourseUI.ButtonKind.Primary ? CourseUI.Hex("E8742A", 0.22f) : kindOfRow == CourseUI.ButtonKind.Secondary ? CourseUI.Hex("3F7D54", 0.20f) : CourseUI.Soft;
            if (lost) back.a *= 0.45f;
            image.color = back;
            rows[i].interactable = state == MondayRelay.StateVoting && now < ev.VoteClosesUtc;
        }

        footer.text = players + (players == 1 ? " player" : " players") + " in the lobby.  Your runners: every leg has a class (D18, H20 ...) that only runners of that class may run. Missing one? You get one on loan.";
    }

    // The bar at the top of the lobby says the same. After LobbySettingsUI.Update has written it, so it doesn't flicker.
    void LateUpdate()
    {
        if (root == null || !root.activeSelf || !ev.IsValid) return;
        LobbySettingsUI settings = FindObjectOfType<LobbySettingsUI>();
        if (settings != null)
        {
            if (settings.modeText != null) settings.modeText.text = "Your runners";
            if (settings.imposterText != null) settings.imposterText.text = "Monday relay";
            if (settings.mapText != null && state < MondayRelay.StateChosen) settings.mapText.text = "Vote below";
        }
    }
}
