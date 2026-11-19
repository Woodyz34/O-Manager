using System.Collections.Generic;
using UnityEngine;

// What came out of a pack.
public class PackResult
{
    public CardData card;
    public bool isNew;
    public int paid;
    public bool committed;   // paid for and added to the collection (PackShop.Commit)
}

// Prices and odds for runner packs. One pack always holds exactly one runner,
// and only runners the player does not have yet, so there are never duplicates.
public static class PackShop
{
    public const int PackPrice = 200;

    // The very first pack costs nothing, so a new player gets a runner to start with
    public static readonly bool FirstPackFree = true;

    // How likely each runner of a tier is compared to the others. Higher means more common.
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

    // The chance, from 0 to 1, that a pack holds a runner of this tier
    public static float Chance(RunnerTier tier)
    {
        float total = 0f;
        float ofTier = 0f;
        foreach (CardData card in Available())
        {
            RunnerTier cardTier = RunnerCards.Tier(card);
            total += Weight(cardTier);
            if (cardTier == tier)
            {
                ofTier += Weight(cardTier);
            }
        }
        return total > 0f ? ofTier / total : 0f;
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
        return new PackResult { card = Draw(), paid = CurrentPrice(), isNew = true };
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
        result.isNew = RunnerCollection.AddFromPack(result.card.RunnerName);
        GameAnalytics.Record("pack_opened", "tier", RunnerCards.Tier(result.card).ToString(), "paid", result.paid, "is_new", result.isNew);
    }

    static CardData Draw()
    {
        List<CardData> cards = Available();
        float total = 0f;
        foreach (CardData card in cards)
        {
            total += Weight(RunnerCards.Tier(card));
        }
        float roll = Random.value * total;
        foreach (CardData card in cards)
        {
            roll -= Weight(RunnerCards.Tier(card));
            if (roll <= 0f)
            {
                return card;
            }
        }
        return cards[cards.Count - 1];
    }
}
