using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.Networking;

// One weekly or daily contest, as the Cloud Code script "Contests" describes it (Docs/cloud-code/Contests.js)
public class ContestInfo
{
    public string id;          // "w2026-47_rc-forest", "d2026-11-19_race-sprint"
    public string span;        // "w" weekly, "d" daily
    public string kind;        // weekly: rc-forest, rc-sprint, race-forest, race-sprint, run-forest, run-sprint; daily: rc, race, run
    public bool sprint;        // a sprint map (else forest): for the daily contests the cloud draws it
    public long startsAt;      // unix milliseconds
    public long endsAt;
    public long seed;          // the same for everyone: picks the map and lays the course
    public int technique;      // race-forest: the technique of the contest runner
    public string course;      // the course as JSON: only once I have started it (or it is over), so nobody sees it first
    public bool laid;          // someone has laid the course
    public float length;       // its straight-line length in metres, when laid
    public int controls;       // its number of controls, when laid
    public int entries;        // finished runs so far
    public ContestEntry mine;  // null when I have not started it

    public bool Weekly => span == "w";
    public bool RouteChoice => kind != null && (kind == "rc" || kind.StartsWith("rc-"));
    public bool Run3D => kind != null && kind.StartsWith("run");
    public bool Sprint => sprint || (kind != null && kind.EndsWith("sprint"));
    public bool Started => mine != null;
    public bool Done => mine != null && mine.state == "done";
    public DateTime EndsLocal => DateTimeOffset.FromUnixTimeMilliseconds(endsAt).LocalDateTime;
}

public class ContestEntry
{
    public string state;       // "started" (no result: left or crashed) or "done"
    public int score;
    public int seconds;
}

public class ContestRow
{
    public string playerId;
    public string name;
    public int score;
    public int seconds;
    public int place;
    public int prize;
    public List<string> packs;   // runner packs won (PrizePacks): "weekly", "daily", "normal"
    public bool dnf;             // started but never handed in a result
    public bool me;
}

public class ContestPaid
{
    public string id;
    public bool sprint;
    public int place;
    public int of;
    public int prize;
    public List<string> packs;
    public bool finished;
}

// A leaderboard as the cloud sends it: the rows, and the course and its dates so a contest that is over can be
// shown without the status (2026-11-24)
public class ContestBoard
{
    public List<ContestRow> rows = new List<ContestRow>();
    public string course;
    public bool over;
    public bool sprint;
    public long endsAt;
    public long replaysUntil;   // the GPS routes are deleted then (daily 7 days after the end, weekly 30 days)

    public bool ReplaysKept => replaysUntil <= 0 || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < replaysUntil;
}

// A contest that ended lately (the "recent" action): its winner, how many finished and my place
public class ContestRecent
{
    public string id;
    public string span;
    public string kind;
    public bool sprint;
    public long endsAt;
    public long replaysUntil;
    public int entries;
    public ContestWinner winner;
    public ContestMine mine;     // null when I did not run it

    public class ContestWinner { public string name; public int score; public int seconds; }
    public class ContestMine { public int place; public int score; public int seconds; public bool finished; }

    public ContestInfo Info() => new ContestInfo { id = id, span = span, kind = kind, sprint = sprint, endsAt = endsAt };
}

// One player in a world ranking list
public class RankRow
{
    public string p;          // player id
    public string n;          // name
    public float pts;         // ranking points: the weighted average, at least RANK_FULL weight
    public float avg;         // the plain weighted average of the contests (game lists only)
    public int c;             // contests in the list during the last 30 days
    public float[] parts;     // overall only: route choice game, race, 3D
    public int place;
    public int was;           // place at the update before, 0 = not in the list then
    public int ww;            // weekly contests won, all time
    public int wd;            // daily contests won, all time
    public Dictionary<string, int[]> wk;   // wins per list: "rc", "rc-forest", ... -> [weekly, daily]

    // Wins in one list; "overall" is every win
    public int[] Wins(string key)
    {
        if (key == "overall" || string.IsNullOrEmpty(key)) return new[] { ww, wd };
        if (wk != null && wk.TryGetValue(key, out int[] a) && a != null && a.Length >= 2) return a;
        return new[] { 0, 0 };
    }
}

// A player's contest wins of all time (counted when a contest with 2+ finishers comes into the ranking)
public class RankWins
{
    public int ww;            // weekly
    public int wd;            // daily
    public int rc, race, run; // by game
    public Dictionary<string, int[]> wk;   // wins per list: "rc", "rc-forest", ... -> [weekly, daily]

    public int[] Wins(string key)
    {
        if (key == "overall" || string.IsNullOrEmpty(key)) return new[] { ww, wd };
        if (wk != null && wk.TryGetValue(key, out int[] a) && a != null && a.Length >= 2) return a;
        return new[] { 0, 0 };
    }

    public int Total => ww + wd;
}

// One player's ranking history and wins (the "rankHistory" action)
public class RankHistory
{
    public List<RankDay> days = new List<RankDay>();
    public RankWins wins = new RankWins();
}

public class RankRules
{
    public int days;          // the window, 30
    public int weekly;        // weight of a weekly contest, 2
    public int daily;         // weight of a daily contest, 1
    public int full;          // the least weight a player is divided by, 4
    public int minFinishers;  // a contest counts when at least this many finished, 2
}

public class RankingData
{
    public string day;        // the last day whose daily contest is in, "2026-11-23"
    public long updatedAt;
    public string me;
    public Dictionary<string, List<RankRow>> lists;   // "overall", "rc", "race", "run", "rc-forest", "rc-sprint", ...
    public Dictionary<string, RankRow> mine;
    public Dictionary<string, int> counts;            // players in each list (only the top 200 are sent)
    public RankRules rules;
    public RankWins myWins;
}

// One day of a player's ranking history: list -> [points, place, players in the list]
public class RankDay
{
    public string d;
    public string n;
    public Dictionary<string, float[]> r;
}

public class ContestPrizeRules
{
    public int[] top;
    public string[][] packs;
    public int[] next;
    public int tookPart;
    public int growth;     // kr more in the top three's bonus pot for every player who finishes
    public int[] share;    // how the pot is shared among places 1-3, in percent
    // 2026-11-26 (the user): everyone gets more with more players, and 4th to last rise evenly toward 3rd.
    // Missing (0/null) from an older cloud script; then the old fields above are used.
    public int[] topGrowth;   // kr more for places 1-3 for every player who finishes
    public int everyone;      // kr more for every place for every player who finishes
    public int fourth;        // 4th gets this % of 3rd's money

    public bool NewRules => topGrowth != null && topGrowth.Length >= 3 && fourth > 0;

    // The same sum as the cloud's prize() in Docs/cloud-code/Contests.js
    public int Prize(int place, int players)
    {
        int n = System.Math.Max(players, place);
        if (place <= 3) return Round5(top[place - 1] + (topGrowth[place - 1] + everyone) * (float)n);
        float last = tookPart + everyone * (float)n;
        float fourthKr = System.Math.Max(last, Prize(3, n) * fourth / 100f);
        if (n <= 4) return Round5(last);
        return Round5(last + (fourthKr - last) * (n - place) / (n - 4f));
    }

    static int Round5(float kr) => (int)System.Math.Round(kr / 5f, System.MidpointRounding.AwayFromZero) * 5;
}

// Talks to the Cloud Code script "Contests" with the player's own sign-in. Everything that matters (one try each,
// the leaderboard hidden until you have run, the prize money) is decided by the script, not by the game.
public static class ContestCloud
{
    const string ScriptName = "Contests";

    public static Dictionary<string, ContestPrizeRules> PrizeRules = new Dictionary<string, ContestPrizeRules>();

    public static async Task<List<ContestInfo>> StatusAsync()
    {
        JObject output = await Call("status", null);
        if (output["prizes"] is JObject prizes)
        {
            foreach (var pair in prizes) PrizeRules[pair.Key] = pair.Value.ToObject<ContestPrizeRules>();
        }
        return output["contests"] != null ? output["contests"].ToObject<List<ContestInfo>>() : new List<ContestInfo>();
    }

    // Uses up the one try; answers with the course to run. courseJson lays it when nobody has yet (else the first
    // one stays); maps are the maps this game has, so a course on a missing map is refused before the try is used.
    public static async Task<string> StartAsync(string id, string name, string courseJson, IEnumerable<string> maps)
    {
        JObject output = await Call("start", new { id, name, course = courseJson, maps = string.Join(",", maps) });
        return (string)output["course"];
    }

    public static async Task<List<ContestRow>> SubmitAsync(string id, int score, int seconds, string replay)
    {
        JObject output = await Call("submit", new { id, score, seconds, replay });
        return Rows(output);
    }

    public static async Task<ContestBoard> BoardAsync(string id)
    {
        JObject output = await Call("board", new { id });
        ContestBoard board = output.ToObject<ContestBoard>() ?? new ContestBoard();
        if (board.rows == null) board.rows = new List<ContestRow>();
        return board;
    }

    // The contests that ended lately and still have their GPS routes, newest first
    public static async Task<List<ContestRecent>> RecentAsync()
    {
        JObject output = await Call("recent", null);
        return output["contests"] != null ? output["contests"].ToObject<List<ContestRecent>>() : new List<ContestRecent>();
    }

    // How many runners' replays the cloud sends at once
    public const int MaxReplays = 10;

    // playerId -> replay JSON
    public static async Task<Dictionary<string, string>> ReplaysAsync(string id, IEnumerable<string> players)
    {
        JObject output = await Call("replays", new { id, players = string.Join(",", players) });
        Dictionary<string, string> result = new Dictionary<string, string>();
        if (output["replays"] is JObject replays)
        {
            foreach (var pair in replays) result[pair.Key] = (string)pair.Value;
        }
        return result;
    }

    // The world ranking (2026-11-24): the lists, my own row in each and the rules (Contests.js, "World ranking")
    public static async Task<RankingData> RankingAsync()
    {
        JObject output = await Call("ranking", null);
        RankingData data = output.ToObject<RankingData>() ?? new RankingData();
        if (data.lists == null) data.lists = new Dictionary<string, List<RankRow>>();
        if (data.mine == null) data.mine = new Dictionary<string, RankRow>();
        if (data.counts == null) data.counts = new Dictionary<string, int>();
        return data;
    }

    // One player's ranking day by day (oldest first) and their wins of all time
    public static async Task<RankHistory> RankHistoryAsync(string playerId)
    {
        JObject output = await Call("rankHistory", new { player = playerId });
        RankHistory result = new RankHistory();
        if (output["days"] != null) result.days = output["days"].ToObject<List<RankDay>>() ?? new List<RankDay>();
        if (output["wins"] != null) result.wins = output["wins"].ToObject<RankWins>() ?? new RankWins();
        return result;
    }

    public static async Task<List<ContestPaid>> ClaimAsync()
    {
        JObject output = await Call("claim", null);
        return output["paid"] != null ? output["paid"].ToObject<List<ContestPaid>>() : new List<ContestPaid>();
    }

    static List<ContestRow> Rows(JObject output)
    {
        return output["rows"] != null ? output["rows"].ToObject<List<ContestRow>>() : new List<ContestRow>();
    }

    static async Task<JObject> Call(string action, object payload)
    {
        await CloudCourses.SignInAsync();
        string url = "https://cloud-code.services.api.unity.com/v1/projects/" + Application.cloudProjectId + "/scripts/" + ScriptName;
        string payloadJson = payload != null ? JsonConvert.SerializeObject(payload) : null;
        Dictionary<string, string> parameters = new Dictionary<string, string> { { "action", action } };
        if (payloadJson != null) parameters["payload"] = payloadJson;
        string body = JsonConvert.SerializeObject(new Dictionary<string, object> { { "params", parameters } });
        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + AuthenticationService.Instance.AccessToken);
            request.timeout = 30;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();
            if (request.result != UnityWebRequest.Result.Success)
            {
                string said = ScriptError(request.downloadHandler != null ? request.downloadHandler.text : "");
                if (said != null) throw new ContestException(said);
                if (request.responseCode == 404)
                    throw new ContestException("The contests are not set up in Unity Cloud yet (the Cloud Code script \"Contests\", see Docs/cloud-code/Contests.js).");
                if (request.result == UnityWebRequest.Result.ConnectionError)
                    throw new ContestException("Could not reach Unity Cloud. Check the internet connection.");
                Debug.LogWarning("ContestCloud " + action + ": " + request.responseCode + " " + request.error + ": " + request.downloadHandler.text);
                throw new ContestException("Unity Cloud said no (" + request.responseCode + "). Try again in a moment.");
            }
            JObject response = JObject.Parse(request.downloadHandler.text);
            if (!(response["output"] is JObject output))
                throw new ContestException("Unexpected answer from the contests: " + request.downloadHandler.text);
            return output;
        }
    }

    // The messages the script throws on purpose, so the player is told why
    static readonly string[] Known =
    {
        "You have already run this contest", "Run the contest first", "This contest is over", "Too late to hand in",
        "Start the contest first", "Already handed in", "The course is not laid yet", "The contest is busy, try again",
        "Replay too big", "Bad course", "The map of this contest is missing"
    };

    static string ScriptError(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        foreach (string known in Known)
        {
            if (body.IndexOf(known, StringComparison.Ordinal) >= 0) return known + ".";
        }
        return null;
    }
}

public class ContestException : Exception
{
    public ContestException(string message) : base(message) { }
}
