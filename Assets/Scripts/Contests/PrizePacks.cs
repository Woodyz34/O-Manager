using System;
using System.Collections.Generic;
using UnityEngine;

// Runner packs won in the weekly and daily contests (2026-11-19, the user). The cloud says which packs a place wins
// (Docs/cloud-code/Contests.js, PRIZES); they wait here, saved on this computer, until the player opens them.
//   Golden pack (id weekly): only Silver, Gold or Elite, the best pack in the game. Won by 1st in a weekly contest.
//   Silver pack (id daily): only Silver, Gold or Elite, a bit worse. Won by 2nd and 3rd in a weekly contest and 1st in a daily one.
//   Bronze pack (id normal): the shop's pack (PackShop). Won by 2nd in a weekly contest, together with a daily pack.
// Like the shop's packs, they only hold runners the player does not have yet.
public static class PrizePacks
{
    public const string Weekly = "weekly";
    public const string Daily = "daily";
    public const string Normal = "normal";

    // The chance (percent) of each tier in a pack. Change them here.
    public static int Percent(string kind, RunnerTier tier)
    {
        if (kind == Weekly)
        {
            switch (tier) { case RunnerTier.Elite: return 15; case RunnerTier.Gold: return 35; case RunnerTier.Silver: return 50; default: return 0; }
        }
        if (kind == Daily)
        {
            switch (tier) { case RunnerTier.Elite: return 5; case RunnerTier.Gold: return 25; case RunnerTier.Silver: return 70; default: return 0; }
        }
        return 0;
    }

    // Money instead, for a pack that has nothing left to give (the player owns every runner it could hold)
    static int MoneyInstead(string kind) => kind == Weekly ? 1500 : kind == Daily ? 700 : PackShop.PackPrice;

    // 2026-11-24 (the user): the packs are named by metal. The weekly prize is the Golden pack, the daily prize the
// Silver pack and the shop's pack the Bronze pack. The ids above stay (the cloud and saved games use them).
    public static string KindOf(string kind) => kind == Weekly ? PackKinds.Gold : kind == Daily ? PackKinds.Silver : PackKinds.Bronze;
    public static string Name(string kind) => PackKinds.Name(KindOf(kind));

    public static string Odds(string kind)
    {
        if (kind != Weekly && kind != Daily) return "the shop's pack";
        return "Elite " + Percent(kind, RunnerTier.Elite) + " %, Gold " + Percent(kind, RunnerTier.Gold) + " %, Silver " + Percent(kind, RunnerTier.Silver) + " %";
    }

    // ---- Waiting packs, saved so a closed game does not lose them ----

    const string Key = "Contests.PrizePacks";

    public static event Action Changed;

    public static List<string> Waiting()
    {
        string saved = PlayerPrefs.GetString(LocalPrefs.Key(Key), "");
        List<string> kinds = new List<string>();
        foreach (string kind in saved.Split(','))
        {
            if (kind == Weekly || kind == Daily || kind == Normal) kinds.Add(kind);
        }
        return kinds;
    }

    public static int Count => Waiting().Count;

    static void Save(List<string> kinds)
    {
        PlayerPrefs.SetString(LocalPrefs.Key(Key), string.Join(",", kinds));
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static void Add(IEnumerable<string> kinds)
    {
        if (kinds == null) return;
        List<string> waiting = Waiting();
        foreach (string kind in kinds)
        {
            if (kind == Weekly || kind == Daily || kind == Normal) waiting.Add(kind);
        }
        Save(waiting);
    }

    static void Remove(string kind)
    {
        List<string> waiting = Waiting();
        if (waiting.Remove(kind)) Save(waiting);
    }

    // The best pack is opened first
    static int Rank(string kind) => kind == Weekly ? 0 : kind == Daily ? 1 : 2;

    // ---- Drawing a runner ----

    public static CardData Draw(string kind)
    {
        List<CardData> left = PackShop.Available();
        if (left.Count == 0) return null;
        if (kind != Weekly && kind != Daily) return PackShop.DrawFrom(left);

        // A tier by the pack's odds (among the tiers that still have runners left), then any runner of it
        Dictionary<RunnerTier, List<CardData>> byTier = new Dictionary<RunnerTier, List<CardData>>();
        foreach (CardData card in left)
        {
            RunnerTier tier = RunnerCards.Tier(card);
            if (Percent(kind, tier) <= 0) continue;
            if (!byTier.TryGetValue(tier, out List<CardData> list)) byTier[tier] = list = new List<CardData>();
            list.Add(card);
        }
        if (byTier.Count == 0) return PackShop.DrawFrom(left);   // only Bronze left: better than nothing
        int total = 0;
        foreach (RunnerTier tier in byTier.Keys) total += Percent(kind, tier);
        int roll = UnityEngine.Random.Range(0, total);
        foreach (var pair in byTier)
        {
            roll -= Percent(kind, pair.Key);
            if (roll < 0) return pair.Value[UnityEngine.Random.Range(0, pair.Value.Count)];
        }
        foreach (List<CardData> any in byTier.Values) return any[UnityEngine.Random.Range(0, any.Count)];
        return null;
    }

    // ---- Opening them all, one show after another ----

    static bool opening;

    public static void OpenAll(Action done = null)
    {
        if (opening) return;
        opening = true;
        OpenNext(done);
    }

    static void OpenNext(Action done)
    {
        List<string> waiting = Waiting();
        if (waiting.Count == 0)
        {
            opening = false;
            done?.Invoke();
            return;
        }
        waiting.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        string kind = waiting[0];
        CardData runner = Draw(kind);
        PackOpening prefab = Resources.Load<PackOpening>("UI/PackOpening");
        if (runner == null || prefab == null)
        {
            Remove(kind);
            if (runner == null)
            {
                int money = MoneyInstead(kind);
                if (Money.Instance != null) Money.Instance.AddMoney(money);
                UIToast.Show("You already have every runner a " + Name(kind) + " can hold: " + money + " kr instead.", 5f, CourseUI.Forest);
            }
            else
            {
                Debug.LogWarning("PrizePacks: PackOpening prefab is missing; the runner is given without the show.");
                RunnerCollection.AddFromPack(runner.RunnerName);
            }
            OpenNext(done);
            return;
        }
        int after = waiting.Count - 1;
        PackOpening show = UnityEngine.Object.Instantiate(prefab);
        // The runner is the player's the moment the pack is opened, like a bought pack
        show.Opened = () =>
        {
            Remove(kind);
            RunnerCollection.AddFromPack(runner.RunnerName);
        };
        show.PlayLoan(runner, Name(kind).ToUpperInvariant(), after > 0 ? "Next pack (" + after + " left)" : "Done", () =>
        {
            UnityEngine.Object.Destroy(show.gameObject);
            OpenNext(done);
        }, false, KindOf(kind));
    }
}
