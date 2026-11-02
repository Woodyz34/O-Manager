using System.Collections.Generic;
using UnityEngine;

// What came out of a pack.
public class PackResult
{
    public CardData card;
    public bool isNew;
    public int paid;
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
        return result;
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
