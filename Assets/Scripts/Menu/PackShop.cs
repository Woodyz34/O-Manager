using System.Collections.Generic;
using UnityEngine;

// What came out of a pack.
public class PackResult
{
    public CardData card;
    public bool isNew;
    public int paid;
    public bool committed;   // paid for and added to the collection (PackShop.Commit)
    public int money;        // above 0: the tier was already complete, so the pack pays this much instead of a new runner
}

// Prices and odds for runner packs. One pack holds one runner the player does not have yet, so there are never
// duplicates. If the tier that was drawn is already complete, the pack pays money instead (MoneyForTier).
public static class PackShop
{
    public const int PackPrice = 200;

    // The very first pack costs nothing, so a new player gets a runner to start with
    public static readonly bool FirstPackFree = true;

    // The chance of each tier, in percent (2026-11-19, the user: fixed odds, and Elite harder to get: 1 % instead of
    // about 2 %, the percent moved to Gold). Before, every runner had a weight and the odds followed how many runners of
    // each tier there were. A tier the player has collected completely drops out, and the others share its part.
    public static int Percent(RunnerTier tier)
    {
        switch (tier)
        {
            case RunnerTier.Elite: return 1;
            case RunnerTier.Gold: return 5;
            case RunnerTier.Silver: return 14;
            default: return 80;
        }
    }

    // How likely each runner of a tier is compared to the others, for the runners the game hands out itself (Same stats,
    // RunnerRules). Packs use the fixed odds above.
    public static int Weight(RunnerTier tier)
    {
        switch (tier)
        {
            case RunnerTier.Elite: return 1;
            case RunnerTier.Gold: return 3;
            case RunnerTier.Silver: return 6;
            default: return 10;
        }
    }

    // Money a pack pays when its tier is already completely collected (2026-11-27, the user: the odds stay fixed, so
    // Elite does not get easier to get, and a complete tier gives money instead)
    public static int MoneyForTier(RunnerTier tier)
    {
        switch (tier)
        {
            case RunnerTier.Elite: return 2000;
            case RunnerTier.Gold: return 800;
            case RunnerTier.Silver: return 300;
            default: return 50;
        }
    }

    public static int CurrentPrice()
    {
        return FirstPackFree && RunnerCollection.PacksOpened == 0 ? 0 : PackPrice;
    }

    public static bool CanBuy()
    {
        return Money.Instance != null && !AllCollected() && Money.Instance.GetMoney() >= CurrentPrice();
    }

    // The runners a pack can hold: in the packs and not owned yet
    public static List<CardData> Available()
    {
        List<CardData> left = new List<CardData>();
        foreach (CardData card in RunnerCards.InPacks())
        {
            if (!RunnerCollection.Owns(card.RunnerName))
            {
                left.Add(card);
            }
        }
        return left;
    }

    // True when the player has every runner that packs can hold
    public static bool AllCollected()
    {
        return Available().Count == 0;
    }

    // The chance, from 0 to 1, that a pack rolls this tier. Always the fixed odds: a complete tier gives money.
    public static float Chance(RunnerTier tier)
    {
        return Percent(tier) / 100f;
    }

    static float Chance(RunnerTier tier, List<CardData> available)
    {
        bool[] has = new bool[4];
        foreach (CardData card in available) has[(int)RunnerCards.Tier(card)] = true;
        float total = 0f;
        for (int i = 0; i < has.Length; i++) if (has[i]) total += Percent((RunnerTier)i);
        return total > 0f && has[(int)tier] ? Percent(tier) / total : 0f;
    }

    // Picks the runner a pack will hold, without paying or adding anything yet. The pack is paid for and its runner
    // added by Commit, the moment the player actually opens it in the pack opening. Before (until 2026-11-14) the
    // runner was added at once, so a pack opening that never got shown (game closed, error) still gave the runner
    // and used up the free pack. Returns null when the player cannot afford it.
    public static PackResult Buy()
    {
        if (!CanBuy())
        {
            return null;
        }
        return Draw();
    }

    // Pays for the pack and puts its runner in the collection. Safe to call more than once.
    public static void Commit(PackResult result)
    {
        if (result == null || result.committed) return;
        result.committed = true;
        if (result.paid > 0 && Money.Instance != null)
        {
            Money.Instance.AddMoney(-result.paid);
        }
        if (result.money > 0)
        {
            // Tier already complete: money, and the runner shown is one the player owns
            result.isNew = false;
            if (Money.Instance != null) Money.Instance.AddMoney(result.money);
        }
        else
        {
            result.isNew = RunnerCollection.AddFromPack(result.card.RunnerName);
        }
        GameAnalytics.Record("pack_opened", "tier", RunnerCards.Tier(result.card).ToString(), "paid", result.paid, "is_new", result.isNew, "money", result.money);
    }

    // First the tier, by the fixed odds (every tier counts, also complete ones); then any runner of that tier the
    // player does not have yet. If the tier is complete the pack pays money and shows a runner of the tier.
    static PackResult Draw()
    {
        float roll = Random.value * (Percent(RunnerTier.Elite) + Percent(RunnerTier.Gold) + Percent(RunnerTier.Silver) + Percent(RunnerTier.Bronze));
        RunnerTier picked = RunnerTier.Bronze;
        for (int i = (int)RunnerTier.Elite; i >= 0; i--)
        {
            picked = (RunnerTier)i;
            roll -= Percent(picked);
            if (roll <= 0f) break;
        }
        List<CardData> unowned = Available().FindAll(c => RunnerCards.Tier(c) == picked);
        PackResult result = new PackResult { paid = CurrentPrice(), isNew = true };
        if (unowned.Count > 0)
        {
            result.card = unowned[Random.Range(0, unowned.Count)];
            return result;
        }
        // Complete tier: money, and show one of the tier's runners (the player owns it); if the tier has no runner at
        // all, any runner of the collection
        List<CardData> shown = RunnerCards.InPacks().FindAll(c => RunnerCards.Tier(c) == picked);
        if (shown.Count == 0) shown = RunnerCards.InPacks();
        result.card = shown[Random.Range(0, shown.Count)];
        result.money = MoneyForTier(picked);
        result.isNew = false;
        return result;
    }

    // A runner from these by the shop's fixed tier odds; also used for the normal pack won in a contest (PrizePacks)
    public static CardData DrawFrom(List<CardData> cards)
    {
        float roll = Random.value;
        RunnerTier picked = RunnerTier.Bronze;
        bool found = false;
        for (int i = (int)RunnerTier.Elite; i >= 0 && !found; i--)
        {
            float chance = Chance((RunnerTier)i, cards);
            if (chance <= 0f) continue;
            picked = (RunnerTier)i;
            roll -= chance;
            if (roll <= 0f) found = true;
        }
        List<CardData> ofTier = cards.FindAll(c => RunnerCards.Tier(c) == picked);
        if (ofTier.Count == 0) ofTier = cards;
        return ofTier[Random.Range(0, ofTier.Count)];
    }
}
