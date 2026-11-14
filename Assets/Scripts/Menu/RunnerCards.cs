using System.Collections.Generic;
using UnityEngine;

public enum RunnerTier { Bronze, Silver, Gold, Elite }

// What the menu, the packs and the cards need to know about a runner beyond the raw numbers.
// Change the limits and colours here and every screen follows.
public static class RunnerCards
{
    // A runner's tier comes from their overall rating (80 % speed, 20 % technique, see Overall).
    // Set again 2026-11-09 for the 80/20 overall (978 runners in packs): Elite about the top 8 %,
    // Gold the next 15 %, Silver the next 26 %, Bronze the rest.
    public const int EliteFrom = 88;
    public const int GoldFrom = 82;
    public const int SilverFrom = 73;

    // Speed counts four times as much as technique (user's choice 2026-11-09). One point of speed costs about
    // 1.8 s per control at speed 75 (legs of about 125 s for a speed-100 runner), one point of technique 0.3 s
    // (2 points per mistake minute per 100 controls): 6 to 8 times as much. 80/20 ranks the cards almost exactly
    // as the expected race time does and still lets technique count.
    public const float SpeedWeight = 0.8f;
    public static float OverallExact(CardData card)
    {
        return SpeedWeight * card.physics + (1f - SpeedWeight) * card.technique;
    }

    public static List<CardData> All()
    {
        AllCards cards = AllCards.getInstance();
        if (cards.GetAllCardsList().Count == 0)
        {
            cards.SetAllCards();
        }
        return cards.GetAllCardsList();
    }

    // The runners a pack can give. Frozen runners (no longer eligible) are left out.
    public static List<CardData> InPacks()
    {
        return All().FindAll(c => c.inPacks);
    }

    public static CardData Find(string runnerName)
    {
        foreach (CardData card in All())
        {
            if (card.RunnerName == runnerName)
            {
                return card;
            }
        }
        return null;
    }

    public static int Overall(CardData card)
    {
        return Mathf.FloorToInt(OverallExact(card) + 0.5f);
    }

    public static RunnerTier Tier(CardData card)
    {
        int overall = Overall(card);
        if (overall >= EliteFrom) return RunnerTier.Elite;
        if (overall >= GoldFrom) return RunnerTier.Gold;
        if (overall >= SilverFrom) return RunnerTier.Silver;
        return RunnerTier.Bronze;
    }

    public static string TierName(RunnerTier tier)
    {
        return tier.ToString().ToUpperInvariant();
    }

    // The colour of the light around a card of this tier
    public static Color TierColor(RunnerTier tier)
    {
        switch (tier)
        {
            case RunnerTier.Elite: return Hex("FF9A3C");
            case RunnerTier.Gold: return Hex("FFD45A");
            case RunnerTier.Silver: return Hex("CFE2F3");
            default: return Hex("E0965A");
        }
    }

    // The colour of the text printed on a card of this tier
    public static Color TierInk(RunnerTier tier)
    {
        switch (tier)
        {
            case RunnerTier.Elite: return Hex("FFE7A6");
            case RunnerTier.Gold: return Hex("3A2A05");
            case RunnerTier.Silver: return Hex("1E2A33");
            default: return Hex("2E1606");
        }
    }

    // The colour behind the runner's photo
    public static Color TierShade(RunnerTier tier)
    {
        switch (tier)
        {
            case RunnerTier.Elite: return Hex("0B1F15");
            case RunnerTier.Gold: return Hex("8A6A1E");
            case RunnerTier.Silver: return Hex("6C7884");
            default: return Hex("6E3B1E");
        }
    }

    // Flags are stored as pictures, so the country's name is read from the picture's file name
    public static string CountryName(CardData card)
    {
        if (!string.IsNullOrEmpty(card.countryName))
        {
            return card.countryName;
        }
        if (card.country == null)
        {
            return "";
        }
        return EnglishCountry(card.country.name);
    }

    // A flag's file name, in Swedish or English, as the country's English name in capitals
    public static string EnglishCountry(string fileName)
    {
        string key = fileName.Trim().ToLowerInvariant();
        switch (key)
        {
            case "sverige": return "SWEDEN";
            case "norge": return "NORWAY";
            case "finland": case "suomi": return "FINLAND";
            case "danmark": return "DENMARK";
            case "schweiz": return "SWITZERLAND";
            case "frankrike": return "FRANCE";
            case "tyskland": return "GERMANY";
            case "storbritannien": return "GREAT BRITAIN";
            case "tjeckien": return "CZECHIA";
            case "estland": return "ESTONIA";
            case "lettland": return "LATVIA";
            case "ukraina": return "UKRAINE";
            case "ungern": return "HUNGARY";
            case "österrike": return "AUSTRIA";
            case "polen": return "POLAND";
            case "spanien": return "SPAIN";
            case "nederländerna": case "holland": return "NETHERLANDS";
            case "slovakien": return "SLOVAKIA";
            case "litauen": return "LITHUANIA";
            case "kanada": return "CANADA";
            case "italien": return "ITALY";
            case "australien": return "AUSTRALIA";
            case "usa": return "UNITED STATES";
            case "nya zeeland": return "NEW ZEALAND";
            case "kroatien": return "CROATIA";
            case "hongkong": case "hong kong": return "HONG KONG CHINA";
            case "belgien": return "BELGIUM";
            case "ryssland": return "RUSSIA";
            case "vitryssland": case "belarus": return "BELARUS";
            case "irland": return "IRELAND";
            case "slovenien": return "SLOVENIA";
            case "rumänien": return "ROMANIA";
            case "bulgarien": return "BULGARIA";
            case "turkiet": return "TURKEY";
            case "kina": return "CHINA";
            case "sydafrika": return "SOUTH AFRICA";
            case "brasilien": return "BRAZIL";
            default: return fileName.Trim().ToUpperInvariant();
        }
    }

    static Color Hex(string hex)
    {
        Color color;
        ColorUtility.TryParseHtmlString("#" + hex, out color);
        return color;
    }
}
