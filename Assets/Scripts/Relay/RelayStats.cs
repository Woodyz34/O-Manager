using System.Collections.Generic;
using UnityEngine;

// One team's result on one relay leg, with its split times and the estimated time lost on each split.
public class RelayTeamResult
{
    public NetworkPlayer player;
    public string team;
    public string runner;
    public bool isLocal;

    public int legTime;        // seconds
    public int total;          // team clock after the leg
    public int prevTotal;      // team clock after the leg before (0 on the first leg)
    public int place;          // after this leg, by total time
    public int prevPlace;      // after the leg before, 0 on the first leg
    public int legPlace;       // by leg time

    // Split times, index 1..n+1 (controls and the finish); index 0 is unused
    // Splits are between the common points of the leg (the start, the controls every team runs, the finish); where a
    // fork is in between, the teams compared are the ones that ran the same branch
    public bool hasSplits;
    public int[] clocks;       // team clock when leaving the start (0), at every common control and at the finish
    public int[] variants;     // the branch run on the way to each common point (-1 = no fork there)
    public int[] split;
    public int[] splitPlace;   // among the teams that ran the same way (the same branch)
    public int[] cumPlace;     // at the common point, among all teams (race position)
    public int[] loss;         // estimated seconds lost (WinSplits), 0 where no mistake
    public bool[] mistake;
    public int totalLoss;
}

// Statistics for a relay leg, worked out from what every team's NetworkPlayer shares.
//
// Time lost is estimated the way WinSplits does it (obasen.orientering.se/winsplits, help: "terms"):
//   reference on a split = the average of the fastest 25 % of the split times (the teams that ran the same split)
//   performance index on a split = split time / reference
//   the runner's normal performance = the median of their indices over the leg, weighted by the length of the splits
//     (the reference time is used for the length), or the plain median when that is lower (few splits, see below)
//   expected split time = reference x normal performance;  time lost = split time - expected split time
//   a split counts as a mistake when the time lost is more than 30 seconds and more than 25 % (WinSplits' defaults)
// With only one team on a split there is nothing to compare with, so nothing is marked.
public static class RelayStats
{
    public const int MistakeSeconds = 30;
    public const float MistakeShare = 0.25f;

    public static List<RelayTeamResult> ForLeg(IRaceStats race, int leg)
    {
        int points = race.Course.Route(leg, null).CommonCount;
        List<RelayTeamResult> teams = new List<RelayTeamResult>();
        foreach (NetworkPlayer player in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (!NetworkPlayer.Races(player)) continue;
            RelayTeamResult result = new RelayTeamResult
            {
                player = player,
                team = string.IsNullOrWhiteSpace(player.nickName.ToString()) ? "Team " + player.Object.StateAuthority.PlayerId : player.nickName.ToString(),
                runner = player.relayRunnerName.ToString(),
                isLocal = player.Object.HasStateAuthority,
                legTime = player.relayLegTimes[leg],
                total = player.relayTotals[leg],
                prevTotal = leg > 0 ? player.relayTotals[leg - 1] : 0
            };
            if (result.total <= 0) continue; // has not finished the leg (left the game)
            result.hasSplits = player.relaySplitLeg == leg && player.relaySplitCount >= points;
            result.clocks = new int[points];
            result.variants = new int[points];
            if (result.hasSplits)
            {
                for (int i = 0; i < points; i++)
                {
                    result.clocks[i] = player.relaySplits[i];
                    result.variants[i] = player.relaySplitVariants[i] - 1;
                }
            }
            result.split = new int[points];
            result.splitPlace = new int[points];
            result.cumPlace = new int[points];
            result.loss = new int[points];
            result.mistake = new bool[points];
            teams.Add(result);
        }
        Compute(teams, leg, points);
        return teams;
    }

    // Places, split times and mistakes for teams whose times and clocks are filled in (public so it can be tested)
    public static void Compute(List<RelayTeamResult> teams, int leg, int points)
    {
        foreach (RelayTeamResult t in teams)
        {
            if (t.split == null) t.split = new int[points];
            if (t.splitPlace == null) t.splitPlace = new int[points];
            if (t.cumPlace == null) t.cumPlace = new int[points];
            if (t.loss == null) t.loss = new int[points];
            if (t.mistake == null) t.mistake = new bool[points];
        }
        Rank(teams, t => t.total, (t, p) => t.place = p);
        Rank(teams, t => t.legTime, (t, p) => t.legPlace = p);
        if (leg > 0) Rank(teams, t => t.prevTotal, (t, p) => t.prevPlace = p);

        List<RelayTeamResult> withSplits = teams.FindAll(t => t.hasSplits);
        for (int c = 1; c < points; c++)
        {
            foreach (RelayTeamResult t in withSplits) t.split[c] = Mathf.Max(0, t.clocks[c] - t.clocks[c - 1]);
        }

        // Places on each split and at each control
        for (int c = 1; c < points; c++)
        {
            int captured = c;
            foreach (List<RelayTeamResult> group in GroupBy(withSplits, t => t.variants[captured]))
            {
                Rank(group, t => t.split[captured], (t, p) => t.splitPlace[captured] = p);
            }
            Rank(withSplits, t => t.clocks[captured], (t, p) => t.cumPlace[captured] = p);
        }

        EstimateMistakes(withSplits, points);
    }

    // ---- WinSplits time loss ----

    static void EstimateMistakes(List<RelayTeamResult> teams, int points)
    {
        float[,] reference = new float[teams.Count, points];
        for (int c = 1; c < points; c++)
        {
            int captured = c;
            foreach (List<RelayTeamResult> group in GroupBy(teams, t => t.variants[captured]))
            {
                List<int> times = group.ConvertAll(t => t.split[captured]);
                times.Sort();
                int fastest = Mathf.Max(1, Mathf.CeilToInt(times.Count * 0.25f));
                float sum = 0f;
                for (int k = 0; k < fastest; k++) sum += times[k];
                float average = Mathf.Max(1f, sum / fastest);
                foreach (RelayTeamResult t in group) reference[teams.IndexOf(t), captured] = group.Count > 1 ? average : 0f;
            }
        }

        for (int i = 0; i < teams.Count; i++)
        {
            RelayTeamResult t = teams[i];
            List<float> indices = new List<float>();
            List<float> weights = new List<float>();
            for (int c = 1; c < points; c++)
            {
                if (reference[i, c] <= 0f) continue;
                indices.Add(t.split[c] / reference[i, c]);
                weights.Add(reference[i, c]);
            }
            if (indices.Count == 0) continue;
            // The weighted median alone failed with few splits (2026-11-22, the user: 26 min on a long first split vs 8 min
            // for the other runner gave 0:00): that one long split carried over half the weight, so the mistake itself
            // became the runner's normal pace. The plain median is used when it is lower, so one bad split can never
            // make itself look normal.
            float normal = Mathf.Min(WeightedMedian(indices, weights), LowerMedian(indices));
            for (int c = 1; c < points; c++)
            {
                if (reference[i, c] <= 0f) continue;
                float expected = reference[i, c] * normal;
                float lost = t.split[c] - expected;
                if (lost > MistakeSeconds && lost > MistakeShare * expected)
                {
                    t.mistake[c] = true;
                    t.loss[c] = Mathf.RoundToInt(lost);
                    t.totalLoss += t.loss[c];
                }
            }
        }
    }

    // The median; with an even number of values the lower of the two middle ones
    static float LowerMedian(List<float> values)
    {
        List<float> sorted = new List<float>(values);
        sorted.Sort();
        return sorted[(sorted.Count - 1) / 2];
    }

    static float WeightedMedian(List<float> values, List<float> weights)
    {
        List<int> order = new List<int>();
        for (int k = 0; k < values.Count; k++) order.Add(k);
        order.Sort((a, b) => values[a].CompareTo(values[b]));
        float half = 0f;
        foreach (float w in weights) half += w;
        half /= 2f;
        float run = 0f;
        foreach (int k in order)
        {
            run += weights[k];
            if (run >= half) return values[k];
        }
        return values[order[order.Count - 1]];
    }

    // ---- Helpers ----

    static List<List<RelayTeamResult>> GroupBy(List<RelayTeamResult> teams, System.Func<RelayTeamResult, int> key)
    {
        Dictionary<int, List<RelayTeamResult>> groups = new Dictionary<int, List<RelayTeamResult>>();
        foreach (RelayTeamResult t in teams)
        {
            int k = key(t);
            if (!groups.TryGetValue(k, out List<RelayTeamResult> list))
            {
                list = new List<RelayTeamResult>();
                groups[k] = list;
            }
            list.Add(t);
        }
        return new List<List<RelayTeamResult>>(groups.Values);
    }

    // 1 = lowest value; equal values share a place
    static void Rank(List<RelayTeamResult> teams, System.Func<RelayTeamResult, int> value, System.Action<RelayTeamResult, int> set)
    {
        List<RelayTeamResult> sorted = new List<RelayTeamResult>(teams);
        sorted.Sort((a, b) => value(a).CompareTo(value(b)));
        for (int k = 0; k < sorted.Count; k++)
        {
            int place = k > 0 && value(sorted[k]) == value(sorted[k - 1]) ? PlaceOf(sorted, k - 1, value) : k + 1;
            set(sorted[k], place);
        }
    }

    static int PlaceOf(List<RelayTeamResult> sorted, int index, System.Func<RelayTeamResult, int> value)
    {
        while (index > 0 && value(sorted[index]) == value(sorted[index - 1])) index--;
        return index + 1;
    }

    public static string Time(int seconds)
    {
        return CompetitionFlow.FormatTime(Mathf.Max(0, seconds));
    }

    // A time difference: "42 s" under a minute, "1:10" from a minute up (2026-11-22, the user: 70 s is hard to read)
    public static string Gap(float seconds)
    {
        int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return s < 60 ? s + " s" : s / 60 + ":" + (s % 60).ToString("00");
    }
}

// What the statistics screen remembers about legs that are over, for the table with all the legs
public class RelayHistory
{
    readonly Dictionary<int, Dictionary<NetworkPlayer, string>> runners = new Dictionary<int, Dictionary<NetworkPlayer, string>>();

    public void Capture(IRaceStats race, int leg)
    {
        Dictionary<NetworkPlayer, string> names = new Dictionary<NetworkPlayer, string>();
        foreach (NetworkPlayer player in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (!NetworkPlayer.Races(player)) continue;
            names[player] = player.relayRunnerName.ToString();
        }
        runners[leg] = names;
    }

    public string Runner(int leg, NetworkPlayer player, int currentLeg)
    {
        if (leg == currentLeg) return player.relayRunnerName.ToString();
        return runners.TryGetValue(leg, out var names) && names.TryGetValue(player, out string name) ? name : "";
    }
}
