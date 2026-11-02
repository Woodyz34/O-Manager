using System.Collections.Generic;
using UnityEngine;

// What came out of a pack.
public class PackResult
{
    public CardData card;
    public bool isNew;
    public int paid;
    public int refund;   // money back for a runner the player already had
}

// Prices and odds for runner packs. One pack always holds exactly one runner.
public static class PackShop
{
    public const int PackPrice = 200;
    public const int DuplicateRefund = 50;

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
        return Money.Instance != null && RunnerCards.InPacks().Count > 0 && Money.Instance.GetMoney() >= CurrentPrice();
    }

    // The chance, from 0 to 1, that a pack holds a runner of this tier
    public static float Chance(RunnerTier tier)
    {
        float total = 0f;
        float ofTier = 0f;
        foreach (CardData card in RunnerCards.InPacks())
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

    // Pays for a pack and adds its runner to the collection. Returns null when the player cannot afford it.
    public static PackResult Buy()
    {
        if (!CanBuy())
        {
            return null;
        }
        int price = CurrentPrice();
        CardData card = Draw();
        if (price > 0)
        {
            Money.Instance.AddMoney(-price);
        }
        PackResult result = new PackResult { card = card, paid = price };
        result.isNew = RunnerCollection.AddFromPack(card.RunnerName);
        if (!result.isNew && DuplicateRefund > 0)
        {
            result.refund = DuplicateRefund;
            Money.Instance.AddMoney(result.refund);
        }
        return result;
    }

    static CardData Draw()
    {
        List<CardData> cards = RunnerCards.InPacks();
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
