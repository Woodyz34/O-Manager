using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

// A weekly or daily contest from start to result (2026-11-19, the user's wish):
//  - the contests page (ContestsPage) lays the course if nobody has yet, uses up the one try ("start") and opens the
//    single-player page, which starts the race at once with the contest's course, runner and game mode;
//  - when the race (IndividualRace) or the route choice game (RouteChoiceView) is over, the result and the replay are
//    sent to the cloud (and kept on this computer until they have arrived);
//  - leaving the race goes back to the contests page.
public static class ContestRun
{
    // The contest being run, from the moment its try is used until the player is back on the contests page
    public static ContestInfo Active { get; private set; }
    public static CourseData ActiveCourse { get; private set; }
    // Set by the contests page, taken by the single-player page to start the race
    public static bool StartPending { get; private set; }
    // Set when a contest race was left, so the main menu opens the contests page again
    public static bool ReturnToContests;
    static bool resultSent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = null;
        ActiveCourse = null;
        StartPending = false;
        ReturnToContests = false;
        resultSent = false;
    }

    public static bool InRace => Active != null && !StartPending;

    // ---- What each contest is ----

    public static string Title(ContestInfo c)
    {
        string what = c.RouteChoice ? "Route choice game" : "Race";
        return what + " · " + (c.Sprint ? "Sprint" : "Forest");
    }

    public static string SpanName(ContestInfo c) => c.Weekly ? "Weekly contest" : "Daily contest";

    // "Physics 100, speed 100, technique 37" for the races; the route choice game times its routes with a speed-100 runner
    public static string RunnerText(ContestInfo c)
    {
        if (c.RouteChoice) return "Pick the fastest route on every leg. Most points wins.";
        int technique = c.Sprint ? 100 : c.technique;
        return "Everyone runs with physics 100, speed 100, technique " + technique + ". Fastest time wins.";
    }

    public static string GameModeFor(ContestInfo c)
    {
        if (c.RouteChoice) return GameModes.YourRunners;
        int technique = c.Sprint ? GameModes.MaxTechnique : Mathf.Clamp(c.technique, GameModes.MinTechnique, GameModes.MaxTechnique);
        return GameModes.MakeSameStats(new List<Vector2Int> { new Vector2Int(100, technique) }, new List<int> { 100 });
    }

    // "Long · 9.8 km · 12 controls" before the start (2026-11-19, the user): the course once laid, else what the
    // contest's seed will lay. Never the map.
    public static string CourseText(ContestInfo c, CourseData course)
    {
        CourseGenerator.Settings planned = SettingsFor(c);
        string kind = CourseGenerator.KindName(planned.kind);
        if (course != null && course.controls != null)
            return kind + " · " + course.LengthText() + " · " + course.controls.Count + " controls";
        return kind + " · about " + planned.lengthKm.ToString("0.0", CultureInfo.InvariantCulture) + " km · about " + planned.controls + " controls";
    }

    public static ExtraMode ModesFor(ContestInfo c) => c.RouteChoice ? ExtraMode.RouteChoice : ExtraMode.None;

    // How the contest's AI course is laid (2026-11-19, the user): a random length and number of controls, drawn from the
    // contest's seed so it is the same whoever lays it. Forest: middle 4-6 km or long 8-12 km, half and half. Sprint:
    // 1.7-2.7 km.
    static CourseGenerator.Settings SettingsFor(ContestInfo c)
    {
        System.Random random = new System.Random((int)(c.seed & 0x7fffffff));
        CourseGenerator.Settings s = new CourseGenerator.Settings { seed = random.Next() };
        float averageLeg;
        if (c.Sprint)
        {
            s.kind = CourseGenerator.Kind.Sprint;
            s.barriers = true;
            s.lengthKm = Range(random, 1.7f, 2.7f);
            averageLeg = Range(random, 140f, 220f);
        }
        else if (random.NextDouble() < 0.5)
        {
            s.kind = CourseGenerator.Kind.Middle;
            s.lengthKm = Range(random, 4f, 6f);
            averageLeg = Range(random, 350f, 550f);
        }
        else
        {
            s.kind = CourseGenerator.Kind.Long;
            s.lengthKm = Range(random, 8f, 12f);
            averageLeg = Range(random, 650f, 950f);
        }
        s.lengthKm = Mathf.Round(s.lengthKm * 10f) / 10f;
        int controls = Mathf.RoundToInt(s.lengthKm * 1000f / averageLeg) - 1;
        s.controls = Mathf.Clamp(controls, CourseGenerator.MinControlsFor(s.kind, s.lengthKm), CourseGenerator.MaxControlsFor(s.kind, s.lengthKm));
        return s;
    }

    static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    // The maps built into the game for this contest (never imported or shared maps: everyone must have it)
    static List<MapDefinition> MapsFor(ContestInfo c)
    {
        List<MapDefinition> maps = new List<MapDefinition>();
        foreach (MapDefinition map in Resources.LoadAll<MapDefinition>("MapDefs"))
        {
            if (map == null) continue;
            if (c.Sprint ? map.IsSprint : map.mapType == "forest") maps.Add(map);
        }
        maps.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return maps;
    }

    public static CourseData ReadCourse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            CourseData course = JsonConvert.DeserializeObject<CourseData>(json);
            if (course == null || course.controls == null) return null;
            course.builtIn = false;
            if (!course.MakeSafe()) return null;
            course.Normalize();
            return course;
        }
        catch (Exception e)
        {
            Debug.LogWarning("ContestRun: unreadable contest course: " + e.Message);
            return null;
        }
    }

    // Lays the contest's AI course on this computer (the first player of a contest does this)
    public static async Task<string> LayCourseAsync(ContestInfo c, CourseGenerator.Progress progress)
    {
        List<MapDefinition> maps = MapsFor(c);
        if (maps.Count == 0) throw new ContestException("There is no " + (c.Sprint ? "sprint" : "forest") + " map in the game for this contest.");
        long seed = c.seed;
        Exception last = null;
        // Let the loading card show before the map's grid is read (that takes a moment on this thread)
        await Task.Yield();
        await Task.Yield();
        // A map that can not take the course: the next one
        for (int attempt = 0; attempt < maps.Count; attempt++)
        {
            MapDefinition map = maps[(int)((seed + attempt) % maps.Count)];
            MapGrid grid = MapGrid.Load(map.gridName);
            CourseGenerator.Settings settings = SettingsFor(c);
            settings.progress = progress;
            CourseGenerator.Result result;
            try { result = await Task.Run(() => CourseGenerator.Generate(map, grid, settings)); }
            catch (Exception e) { last = e; continue; }
            if (result == null || result.course == null) { last = new Exception(result != null ? result.error : "no course"); continue; }
            return Finish(c, result.course);
        }
        // No map could take a course that long: a middle distance course on the contest's map instead
        if (!c.Sprint)
        {
            MapDefinition map = maps[(int)(seed % maps.Count)];
            MapGrid grid = MapGrid.Load(map.gridName);
            CourseGenerator.Settings settings = new CourseGenerator.Settings { kind = CourseGenerator.Kind.Middle, lengthKm = 4f, controls = 10, seed = (int)(seed & 0x7fffffff), progress = progress };
            CourseGenerator.Result result = await Task.Run(() => CourseGenerator.Generate(map, grid, settings));
            if (result != null && result.course != null) return Finish(c, result.course);
        }
        throw new ContestException("Could not lay the contest course: " + (last != null ? last.Message : "no map fits"));
    }

    static string Finish(ContestInfo c, CourseData course)
    {
        course.id = "contest-" + c.id;
        course.name = SpanName(c) + " " + Title(c);
        course.author = "O-Manager";
        course.authorId = "";
        course.builtIn = false;
        course.createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return JsonConvert.SerializeObject(course);
    }

    // ---- Starting ----

    // The try is used: start the race from the single-player page
    public static void Begin(ContestInfo c, CourseData course)
    {
        Active = c;
        ActiveCourse = course;
        StartPending = true;
        resultSent = false;
        SceneManager.LoadScene("SingelPlayer");
    }

    // Called by the single-player page when it has started the race
    public static void Started()
    {
        StartPending = false;
    }

    // The race could not start, or the player left: back to the contests page
    public static void Leave()
    {
        Active = null;
        ActiveCourse = null;
        StartPending = false;
        ReturnToContests = true;
    }

    // ---- Results ----

    // A normal race is over: the time and the runner's way round, for the replay
    public static void OnRaceFinished(int seconds, RelayTrack track)
    {
        if (!InRace || resultSent || Active.RouteChoice) return;
        resultSent = true;
        Send(Active, 0, seconds, EncodeRace(track));
    }

    // The route choice game is over: the points and every leg's routes with the one picked
    public static void OnRouteChoiceFinished(int score, List<ContestReplay.ChoiceLeg> legs)
    {
        if (!InRace || resultSent || !Active.RouteChoice) return;
        resultSent = true;
        Send(Active, score, 0, EncodeChoices(legs));
    }

    const string UnsentKey = "Contests.Unsent";

    class Unsent
    {
        public string id;
        public int score;
        public int seconds;
        public string replay;
    }

    // The result on its way to the cloud. The contests page waits for it before it reads the leaderboard: without
    // that, going back to the page right after the finish read the board before the result had arrived, and you
    // were not on it (2026-11-19, the user).
    public static Task Sending { get; private set; } = Task.CompletedTask;

    static void Send(ContestInfo c, int score, int seconds, string replay)
    {
        Sending = SendNow(c, score, seconds, replay);
    }

    static async Task SendNow(ContestInfo c, int score, int seconds, string replay)
    {
        // Kept until it has arrived, so a lost connection does not lose the run
        Unsent unsent = new Unsent { id = c.id, score = score, seconds = seconds, replay = replay };
        PlayerPrefs.SetString(LocalPrefs.Key(UnsentKey), JsonConvert.SerializeObject(unsent));
        PlayerPrefs.Save();
        try
        {
            await ContestCloud.SubmitAsync(c.id, score, seconds, replay);
            PlayerPrefs.DeleteKey(LocalPrefs.Key(UnsentKey));
            PlayerPrefs.Save();
            // No message here: it covered the button on the statistics screen, and the contests page shows the result
        }
        catch (Exception e)
        {
            Debug.LogWarning("ContestRun: could not send the result: " + e.Message);
            UIToast.Show("Your contest result could not be sent yet (" + e.Message + "). It is sent when you open the contests page.");
        }
    }

    // A result that did not arrive last time; true when there was one and it is in now
    public static async Task<bool> SendUnsentAsync()
    {
        try { await Sending; }
        catch (Exception) { }
        string json = PlayerPrefs.GetString(LocalPrefs.Key(UnsentKey), "");
        if (string.IsNullOrEmpty(json)) return false;
        Unsent unsent;
        try { unsent = JsonConvert.DeserializeObject<Unsent>(json); }
        catch { unsent = null; }
        if (unsent == null || string.IsNullOrEmpty(unsent.id))
        {
            PlayerPrefs.DeleteKey(LocalPrefs.Key(UnsentKey));
            return false;
        }
        try
        {
            await ContestCloud.SubmitAsync(unsent.id, unsent.score, unsent.seconds, unsent.replay);
        }
        catch (ContestException e) when (e.Message.StartsWith("Already handed in") || e.Message.StartsWith("Too late") || e.Message.StartsWith("Start the contest"))
        {
            // Nothing more to do with it
        }
        PlayerPrefs.DeleteKey(LocalPrefs.Key(UnsentKey));
        PlayerPrefs.Save();
        return true;
    }

    // ---- Replays: in metres from the course's start, to a tenth of a metre, so they stay small ----

    static CoursePoint Origin(CourseData course)
    {
        List<CoursePoint> points = course != null ? course.AllPoints() : null;
        return points != null && points.Count > 0 ? points[0] : new CoursePoint(0, 0);
    }

    static void AppendPoint(StringBuilder s, MapDefinition map, CoursePoint origin, Vector3 world, ref bool first)
    {
        CoursePoint p = map.FromWorld(world);
        if (!first) s.Append(',');
        first = false;
        s.Append(Mathf.RoundToInt((float)((p.e - origin.e) * 10.0)).ToString(CultureInfo.InvariantCulture)).Append(',')
         .Append(Mathf.RoundToInt((float)((p.n - origin.n) * 10.0)).ToString(CultureInfo.InvariantCulture));
    }

    static string EncodeRace(RelayTrack track)
    {
        MapDefinition map = CourseLoader.LoadedMap;
        if (track == null || map == null || track.positions.Count == 0) return "";
        CoursePoint origin = Origin(ActiveCourse);
        float minStep = (map.IsSprint ? 1.5f : 4f) * MapDefinition.WorldUnitsPerMeter;
        StringBuilder points = new StringBuilder();
        StringBuilder times = new StringBuilder();
        bool firstPoint = true;
        Vector3 last = track.positions[0];
        for (int i = 0; i < track.positions.Count; i++)
        {
            bool end = i == 0 || i == track.positions.Count - 1;
            if (!end && Vector2.Distance(last, track.positions[i]) < minStep) continue;
            last = track.positions[i];
            if (!firstPoint) times.Append(',');
            float gameSeconds = i < track.times.Count ? track.times[i] * PackRunning.GameSecondsPerSecond : 0f;
            times.Append(Mathf.RoundToInt(gameSeconds * 10f).ToString(CultureInfo.InvariantCulture));
            AppendPoint(points, map, origin, track.positions[i], ref firstPoint);
        }
        return "{\"v\":1,\"k\":\"race\",\"t\":[" + times + "],\"p\":[" + points + "]}";
    }

    static string EncodeChoices(List<ContestReplay.ChoiceLeg> legs)
    {
        MapDefinition map = CourseLoader.LoadedMap;
        if (legs == null || map == null) return "";
        CoursePoint origin = Origin(ActiveCourse);
        float minStep = (map.IsSprint ? 1f : 3f) * MapDefinition.WorldUnitsPerMeter;
        StringBuilder s = new StringBuilder("{\"v\":1,\"k\":\"rc\",\"legs\":[");
        for (int k = 0; k < legs.Count; k++)
        {
            ContestReplay.ChoiceLeg leg = legs[k];
            if (k > 0) s.Append(',');
            s.Append("{\"l\":").Append(leg.leg).Append(",\"f\":").Append(leg.fastest).Append(",\"pick\":").Append(leg.pick)
             .Append(",\"pts\":").Append(leg.points).Append(",\"r\":[");
            for (int r = 0; r < leg.routes.Count; r++)
            {
                ContestReplay.ChoiceRoute route = leg.routes[r];
                if (r > 0) s.Append(',');
                s.Append("{\"c\":").Append(route.colour).Append(",\"s\":").Append(Mathf.RoundToInt(route.seconds)).Append(",\"p\":[");
                bool first = true;
                Vector3 last = route.world.Count > 0 ? route.world[0] : Vector3.zero;
                for (int i = 0; i < route.world.Count; i++)
                {
                    bool end = i == 0 || i == route.world.Count - 1;
                    if (!end && Vector2.Distance(last, route.world[i]) < minStep) continue;
                    last = route.world[i];
                    AppendPoint(s, map, origin, route.world[i], ref first);
                }
                s.Append("]}");
            }
            s.Append("]}");
        }
        s.Append("]}");
        return s.ToString();
    }
}
