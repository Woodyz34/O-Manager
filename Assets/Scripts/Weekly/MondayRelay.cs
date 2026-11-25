using System;
using System.Collections.Generic;
using System.Globalization;
using Fusion;
using UnityEngine;

// The Monday relay (2026-11-25, Anders): every Monday at 21:00 Swedish time a big relay in a lobby that the game
// makes by itself. The lobby opens 5 minutes before the start. Nobody is the host: everyone joins the same lobby
// (a fixed name for the week), the first one in is the host without knowing it (Photon's master client, which moves
// to another player when it leaves), and everything a host does is done on the clock (MondayRelayLobby, RelayRace).
//
// The relay: three random relays made by the players (published in the course builder) that have at least 3 legs,
// every leg longer than 3 km. Everyone in the lobby votes, and the one with the most votes is run. Every leg gets a
// random class rule (D18, H18, D20, H20, D21, H21): only runners of that class may run it. The pool is Your runners
// (Anders 2026-11-25): a player without a runner of a leg's class gets one on loan (RunnerRules.YourRunnersPool).
//
// Times are worked out from the computer's clock in UTC, turned into Swedish time here (no time zone database: the
// game runs on Windows, Mac and Linux, and the names of the zones differ).
public static class MondayRelay
{
    // ---- The schedule ----

    public const DayOfWeek Day = DayOfWeek.Monday;
    public const int StartHour = 21;                         // Swedish time
    public const int StartMinute = 0;
    public static readonly TimeSpan OpensBefore = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan VoteClosesBefore = TimeSpan.FromSeconds(25);
    // A lobby that has not started by then (nobody was in it at 21:00) still starts when someone comes
    public static readonly TimeSpan LateStart = TimeSpan.FromMinutes(15);
    // The lobby belongs to this week's relay this long after the start (watching, the results, the way out)
    public static readonly TimeSpan LastsFor = TimeSpan.FromHours(3);

    // The rules for the relays that can come up
    public const int MinLegs = 3;
    public const float MinLegMeters = 3000f;                  // every leg longer than 3 km (along branch A)
    public const int Candidates = 3;

    // How long things may take when nobody is the host (seconds of real time)
    public const float PickSeconds = 120f;                    // picking the runners for every leg
    public const float DrawSeconds = 300f;                    // drawing one whole leg (the leg starts as soon as everyone has drawn)
    public const float StatsSeconds = 60f;                    // the statistics between the legs
    public const float FinalStatsSeconds = 30f;               // the statistics after the last leg, then the results
    public const float MapWaitSeconds = 180f;                 // waiting for everyone to have the map after 21:00

    // One week's relay
    public struct Event
    {
        public DateTime startUtc;
        public DateTime OpensUtc => startUtc - OpensBefore;
        public DateTime VoteClosesUtc => startUtc - VoteClosesBefore;
        public DateTime LateStartUtc => startUtc + LateStart;
        public DateTime EndsUtc => startUtc + LastsFor;
        // The Swedish date of the relay, e.g. "2026-11-30"
        public string Key => SwedishTime(startUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string FunName => MondayRelay.FunName(Key);
        public string SessionName => MondayRelay.SessionName(Key);
        public bool IsValid => startUtc != default;
    }

    public static DateTime NowUtc => DateTime.UtcNow + TestOffset;

    // Testing (Tools > O-Manager > Testing > Monday relay ...): the game's clock for the Monday relay is moved so the next
    // relay is a few minutes away. Kept in PlayerPrefs without the clone suffix, so editor clones (ParrelSync) on the
    // same computer share it. Never set in a normal game.
    public const string TestOffsetKey = "MondayRelay.TestOffsetSeconds";
    static TimeSpan TestOffset
    {
        get
        {
            string text = PlayerPrefs.GetString(TestOffsetKey, "");
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }
    }

    // Moves the clock so the next relay starts in the given number of minutes (its lobby opens 5 minutes before)
    public static void TestStartIn(double minutes)
    {
        PlayerPrefs.DeleteKey(TestOffsetKey);
        DateTime now = DateTime.UtcNow;
        Event next = Next(now + OpensBefore);   // a relay whose lobby is not open yet even with the 5 minutes added
        double offset = (next.startUtc - now).TotalSeconds - minutes * 60.0;
        PlayerPrefs.SetString(TestOffsetKey, offset.ToString("R", CultureInfo.InvariantCulture));
        PlayerPrefs.Save();
    }

    public static void TestReset()
    {
        PlayerPrefs.DeleteKey(TestOffsetKey);
        PlayerPrefs.Save();
    }

    // The relay whose lobby is open or racing now, else the next one
    public static Event Current(DateTime nowUtc)
    {
        Event last = Last(nowUtc);
        if (last.IsValid && nowUtc < last.EndsUtc) return last;
        return Next(nowUtc);
    }

    // The latest relay whose lobby has opened (it may be over)
    public static Event Last(DateTime nowUtc)
    {
        // Next is the first relay whose lobby has not opened yet, so the one the week before it has
        Event next = Next(nowUtc);
        return new Event { startUtc = StartUtcOf(SwedishTime(next.startUtc).Date.AddDays(-7)) };
    }

    // The next relay whose lobby has not opened yet
    public static Event Next(DateTime nowUtc)
    {
        DateTime local = SwedishTime(nowUtc).Date;
        for (int i = 0; i < 15; i++)
        {
            DateTime day = local.AddDays(i);
            if (day.DayOfWeek != Day) continue;
            DateTime start = StartUtcOf(day);
            if (start - OpensBefore > nowUtc) return new Event { startUtc = start };
        }
        return new Event { startUtc = StartUtcOf(local.AddDays(7)) };
    }

    static DateTime StartUtcOf(DateTime swedishDay)
    {
        DateTime local = new DateTime(swedishDay.Year, swedishDay.Month, swedishDay.Day, StartHour, StartMinute, 0, DateTimeKind.Unspecified);
        // Swedish time is UTC+1, or UTC+2 in summer; try summer first and check it
        DateTime summer = DateTime.SpecifyKind(local.AddHours(-2), DateTimeKind.Utc);
        if (IsSummerTime(summer)) return summer;
        return DateTime.SpecifyKind(local.AddHours(-1), DateTimeKind.Utc);
    }

    // ---- Swedish time (CET/CEST) ----

    // Summer time runs from 01:00 UTC on the last Sunday of March to 01:00 UTC on the last Sunday of October
    public static bool IsSummerTime(DateTime utc)
    {
        DateTime begins = LastSundayUtc(utc.Year, 3);
        DateTime ends = LastSundayUtc(utc.Year, 10);
        return utc >= begins && utc < ends;
    }

    static DateTime LastSundayUtc(int year, int month)
    {
        DateTime day = new DateTime(year, month, DateTime.DaysInMonth(year, month), 1, 0, 0, DateTimeKind.Utc);
        while (day.DayOfWeek != DayOfWeek.Sunday) day = day.AddDays(-1);
        return day;
    }

    public static DateTime SwedishTime(DateTime utc)
    {
        return DateTime.SpecifyKind(utc.AddHours(IsSummerTime(utc) ? 2 : 1), DateTimeKind.Unspecified);
    }

    // ---- Names ----

    // A new fun name every week, the same for everyone (picked by the date)
    static readonly string[] FunNames =
    {
        "The Monday Night Relay",
        "The Headlamp Relay",
        "Monday Mass Start",
        "The Changeover Club",
        "The Blueberry Relay",
        "The Last Control Relay",
        "Monday Map Madness",
        "The Compass Cup",
        "The Mossy Monday Relay",
        "The Punch Card Relay",
        "The Spruce Forest Relay",
        "The Dog Leg Derby",
        "The Night Mila Light",
        "The Contour Line Relay",
    };

    public static string FunName(string key)
    {
        return FunNames[(int)(Hash("name|" + key) % (uint)FunNames.Length)];
    }

    // The lobby's name in Photon. One per week, so everyone who joins this week's relay ends up in the same lobby.
    public static string SessionName(string key)
    {
        DateTime date;
        string day = DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            ? date.ToString("d MMM", CultureInfo.InvariantCulture) : key;
        return FunName(key) + " · " + day;
    }

    // ---- Is this the Monday relay? ----

    // The relay of the lobby the game is in now, or an invalid event when it is a normal lobby
    public static Event SessionEvent(NetworkRunner runner)
    {
        if (runner == null || runner.SessionInfo == null || !runner.SessionInfo.IsValid) return default;
        string name = runner.SessionInfo.Name;
        if (string.IsNullOrEmpty(name)) return default;
        DateTime now = NowUtc;
        Event last = Last(now);
        if (last.IsValid && name == last.SessionName) return last;
        Event next = Next(now);
        if (name == next.SessionName) return next;
        return default;
    }

    public static bool InSession(NetworkRunner runner) => SessionEvent(runner).IsValid;

    // In the Monday relay's lobby or race now
    public static bool Active
    {
        get
        {
            NetworkPlayer local = NetworkPlayer.Local;
            return local != null && local.Runner != null && local.Runner.GameMode == GameMode.Shared && InSession(local.Runner);
        }
    }

    public static Event ActiveEvent
    {
        get
        {
            NetworkPlayer local = NetworkPlayer.Local;
            return local != null ? SessionEvent(local.Runner) : default;
        }
    }

    // Set by the main menu's Monday relay card: the multiplayer page joins the lobby at once
    public static bool AutoJoin { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        AutoJoin = false;
        cachedKey = null;
        seenChoices = "";
        seenState = 0;
        seenPick = 0;
    }

    // ---- What the lobby has seen of the host's choices (kept when the host leaves and someone else takes over) ----

    static string cachedKey;
    static string seenChoices = "";
    static int seenState;
    static int seenPick;

    public const int StateNone = 0, StateVoting = 1, StateChosen = 2, StateStarted = 3;

    public static void Remember(string key, string choices, int state, int pick)
    {
        if (cachedKey != key)
        {
            cachedKey = key;
            seenChoices = "";
            seenState = 0;
            seenPick = 0;
        }
        if (!string.IsNullOrEmpty(choices)) seenChoices = choices;
        seenState = Mathf.Max(seenState, state);
        if (pick > 0) seenPick = pick;
    }

    public static void Recall(string key, out string choices, out int state, out int pick)
    {
        if (cachedKey != key)
        {
            choices = "";
            state = 0;
            pick = 0;
            return;
        }
        choices = seenChoices;
        state = seenState;
        pick = seenPick;
    }

    // ---- The relays that can come up ----

    // Why a relay can't be the Monday relay, or null when it can. Not strict: the legs may be any length (only when no
    // relay at all follows the rules).
    public static string NotEligible(CourseData course, bool strict = true)
    {
        if (course == null || !course.IsRelay()) return "not a relay";
        if (course.legs < MinLegs) return "fewer than " + MinLegs + " legs";
        for (int leg = 0; strict && leg < course.legs; leg++)
        {
            CourseData legCourse = course.Route(leg, null).course;
            if (legCourse == null || legCourse.LengthMeters() <= MinLegMeters) return "leg " + (leg + 1) + " is not longer than 3 km";
        }
        if (course.ToLobbyString().Length > CourseData.MaxLobbyChars) return "too big to send to the lobby";
        if (!MapForEveryone(course.mapId)) return "the map is not shared";
        return null;
    }

    // A map that everyone can get: built into the game, or shared by a player (SharedMaps)
    static bool MapForEveryone(string mapId)
    {
        if (string.IsNullOrEmpty(mapId)) return false;
        MapDefinition map = MapDefinition.FindExact(mapId);
        if (map != null && !map.isLocal) return true;              // built into the game
        if (map != null && map.isSharedCopy) return true;          // a copy of a map another player shares
        return SharedMaps.IsListed(mapId);
    }

    // Three relays at random among the ones that may come up. The same list gives the same three for everyone in the
    // same week (the order of the list does not matter: they are sorted by id first).
    public static List<CourseData> PickCandidates(List<CourseData> all, string key, bool strict)
    {
        List<CourseData> eligible = new List<CourseData>();
        HashSet<string> ids = new HashSet<string>();
        foreach (CourseData course in all)
        {
            if (course == null || string.IsNullOrEmpty(course.id) || !ids.Add(course.id)) continue;
            string why;
            try { why = NotEligible(course, strict); }
            catch (Exception e) { why = e.Message; }
            if (why == null) eligible.Add(course);
        }
        eligible.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        System.Random random = new System.Random((int)Hash("candidates|" + key));
        List<CourseData> picked = new List<CourseData>();
        while (picked.Count < Candidates && eligible.Count > 0)
        {
            int index = random.Next(eligible.Count);
            picked.Add(eligible[index]);
            eligible.RemoveAt(index);
        }
        return picked;
    }

    // ---- The class of every leg ----

    public static readonly LegSex[] Classes = { LegSex.D18, LegSex.H18, LegSex.D20, LegSex.H20, LegSex.D21, LegSex.H21 };

    // A random class for every leg, the same for everyone in the lobby (from the date and the relay). Never the same
    // class on two legs in a row, and both women's and men's legs when there are 3 legs or more.
    public static List<LegSex> LegClasses(string key, string courseId, int legs)
    {
        System.Random random = new System.Random((int)Hash("classes|" + key + "|" + courseId));
        List<LegSex> result = new List<LegSex>();
        for (int tries = 0; tries < 50; tries++)
        {
            result.Clear();
            for (int leg = 0; leg < legs; leg++)
            {
                LegSex pick;
                do { pick = Classes[random.Next(Classes.Length)]; }
                while (leg > 0 && pick == result[leg - 1]);
                result.Add(pick);
            }
            bool women = result.Exists(RunnerRules.IsWomen);
            bool men = result.Exists(RunnerRules.IsMen);
            if (legs < 3 || (women && men)) break;
        }
        return result;
    }

    // The leg rules of the relay run now in the Monday relay's lobby, or null outside it
    public static List<LegSex> ActiveLegRules(CourseData course)
    {
        if (course == null || !course.IsRelay() || course.legs <= 0) return null;
        Event e = ActiveEvent;
        if (!e.IsValid) return null;
        return LegClasses(e.Key, course.id ?? "", course.legs);
    }

    public static string ClassList(List<LegSex> classes)
    {
        List<string> names = new List<string>();
        foreach (LegSex c in classes) names.Add(RunnerRules.SexName(c));
        return string.Join("  ·  ", names);
    }

    // ---- Helpers ----

    // FNV-1a: the same number on every computer (string.GetHashCode is not)
    public static uint Hash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash & 0x7FFFFFFF;
    }

    // "4 d 3 h", "2 h 15 min", "4:12"
    public static string Countdown(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        if (left.TotalDays >= 1) return (int)left.TotalDays + " d " + left.Hours + " h";
        if (left.TotalHours >= 1) return (int)left.TotalHours + " h " + left.Minutes + " min";
        return (int)left.TotalMinutes + ":" + left.Seconds.ToString("00");
    }

    public static string SecondsText(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }
}
