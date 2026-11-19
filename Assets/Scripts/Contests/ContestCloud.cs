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
    public string kind;        // rc-forest, rc-sprint, race-forest, race-sprint
    public long startsAt;      // unix milliseconds
    public long endsAt;
    public long seed;          // the same for everyone: picks the map and lays the course
    public int technique;      // race-forest: the technique of the contest runner
    public string course;      // the course as JSON, or null before the first player has laid it
    public int entries;        // finished runs so far
    public ContestEntry mine;  // null when I have not started it

    public bool Weekly => span == "w";
    public bool RouteChoice => kind != null && kind.StartsWith("rc-");
    public bool Sprint => kind != null && kind.EndsWith("sprint");
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
    public bool me;
}

public class ContestPaid
{
    public string id;
    public int place;
    public int of;
    public int prize;
    public bool finished;
}

public class ContestPrizeRules
{
    public int[] top;
    public int[] next;
    public int tookPart;
    public int growth;
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

    // Lays the course when nobody has yet; answers with the course everyone runs (maybe another player's)
    public static async Task<string> SetCourseAsync(string id, string courseJson)
    {
        JObject output = await Call("setCourse", new { id, course = courseJson });
        return (string)output["course"];
    }

    // Uses up the one try; answers with the course
    public static async Task<string> StartAsync(string id, string name)
    {
        JObject output = await Call("start", new { id, name });
        return (string)output["course"];
    }

    public static async Task<List<ContestRow>> SubmitAsync(string id, int score, int seconds, string replay)
    {
        JObject output = await Call("submit", new { id, score, seconds, replay });
        return Rows(output);
    }

    public static async Task<List<ContestRow>> BoardAsync(string id)
    {
        return Rows(await Call("board", new { id }));
    }

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
        "Replay too big", "Bad course"
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
