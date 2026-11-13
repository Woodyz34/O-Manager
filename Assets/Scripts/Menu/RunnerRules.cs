using System.Collections.Generic;
using UnityEngine;

// Who may run a relay leg (set per leg in the course builder, CourseData.legSex)
public enum LegSex { Any, Women, Men }

// Women and men in the same race, and the host's rules for which runners a team may use (2026-11-13).
//
// Women are slower: the cards' speed is measured within each class (the best woman and the best man are both 100),
// so a woman's speed is scaled down in the race. How much: in the game's own Eventor results (22 races in 2026
// where both D21 and H21 ran: SM, Swedish League, WRE and others) the women's winner was 18 % slower per km than
// the men's (median; 1.215 times the time per km). On the track the gap is 8-11 % (10 000 m world records 26:11
// and 28:54), but in the forest it is bigger. So a woman runs with 82 % of her card speed: a 100 woman runs like
// an 82 man. Technique is the same for women and men.
//
// The host can also set a max average overall for a team (GameModes.Cap): in a relay the runners' overall
// together may be at most cap * legs, so a strong runner on one leg needs a weaker one on another. The overall
// counted is the card's own overall (BudgetCost), the same for women and men.
public static class RunnerRules
{
    public const float WomenSpeedFactor = 0.82f;

    // The speed a runner races with
    public static float RaceSpeed(CardData card)
    {
        if (card == null) return 0f;
        return card.man ? card.speed : card.speed * WomenSpeedFactor;
    }

    // The overall a runner costs under a max average overall: the card's own overall (user's wish 2026-11-13: a
    // woman's rating is her own, as good as a man's with the same number; only her race speed is lower)
    public static int BudgetCost(CardData card)
    {
        return card == null ? 0 : RunnerCards.Overall(card);
    }

    public static bool Fits(CardData card, LegSex sex)
    {
        if (card == null) return false;
        return sex == LegSex.Any || (sex == LegSex.Women ? !card.man : card.man);
    }

    public static string SexName(LegSex sex) => sex == LegSex.Women ? "Women" : sex == LegSex.Men ? "Men" : "Any";

    // ---- The rules of the round being played (set on each player when the runner picking starts) ----

    // The max average overall, or 0 when there is none
    public static int Cap { get; private set; }
    // "Open packs": the runners that came out of the player's packs
    public static bool OpenPacksRound { get; private set; }
    public static readonly List<CardData> Opened = new List<CardData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Cap = 0;
        OpenPacksRound = false;
        Opened.Clear();
    }

    public static void BeginRound(string mode)
    {
        Cap = GameModes.Cap(mode);
        OpenPacksRound = GameModes.IsOpenPacks(mode);
        Opened.Clear();
    }

    public static bool IsOpened(string runnerName) => OpenPacksRound && Opened.Exists(c => c.RunnerName == runnerName);

    // The leg rules of a course: one per leg in a relay, a single Any for an individual course
    public static List<LegSex> LegSexes(CourseData course)
    {
        List<LegSex> legs = new List<LegSex>();
        if (course != null && course.IsRelay())
        {
            for (int leg = 0; leg < course.legs; leg++) legs.Add(course.SexOfLeg(leg));
        }
        else
        {
            legs.Add(LegSex.Any);
        }
        return legs;
    }

    // ---- Can the legs still be filled? ----

    // True when the legs can get runners from the pool (each runner once, unless repeats are allowed) with at most
    // 'budget' overall together (BudgetCost; int.MaxValue = no limit). The cheapest way: the women's and men's legs
    // take the cheapest women and men, then the open legs the cheapest of those left.
    public static bool CanFill(List<CardData> pool, IList<LegSex> legs, int budget, bool repeats = false)
    {
        if (legs.Count == 0) return budget >= 0;
        List<CardData> sorted = new List<CardData>(pool);
        sorted.RemoveAll(c => c == null);
        sorted.Sort((a, b) => BudgetCost(a).CompareTo(BudgetCost(b)));
        HashSet<string> used = new HashSet<string>();
        long total = 0;
        foreach (LegSex pass in new[] { LegSex.Women, LegSex.Men, LegSex.Any })
        {
            foreach (LegSex leg in legs)
            {
                if (leg != pass) continue;
                CardData cheapest = sorted.Find(c => Fits(c, leg) && (repeats || !used.Contains(c.RunnerName)));
                if (cheapest == null) return false;
                used.Add(cheapest.RunnerName);
                total += BudgetCost(cheapest);
            }
        }
        return total <= budget;
    }

    // May this runner run the next leg? It must fit the leg, and the legs after it must still be possible with the
    // runners left and (with a max average) the overall left.
    public static bool Allowed(CardData candidate, List<CardData> pool, List<CardData> picks, IList<LegSex> legs, int cap)
    {
        int leg = picks.Count;
        if (leg >= legs.Count || !Fits(candidate, legs[leg])) return false;
        bool repeats = CountUnique(pool) < legs.Count;
        List<CardData> left = new List<CardData>(pool);
        if (!repeats)
        {
            left.RemoveAll(c => c == null || c.RunnerName == candidate.RunnerName || picks.Exists(p => p.RunnerName == c.RunnerName));
        }
        List<LegSex> rest = new List<LegSex>();
        for (int i = leg + 1; i < legs.Count; i++) rest.Add(legs[i]);
        int budget = cap > 0 ? cap * legs.Count - Spent(picks) - BudgetCost(candidate) : int.MaxValue;
        return budget >= 0 && CanFill(left, rest, budget, repeats);
    }

    public static int Spent(List<CardData> picks)
    {
        int spent = 0;
        foreach (CardData pick in picks) spent += BudgetCost(pick);
        return spent;
    }

    static int CountUnique(List<CardData> pool)
    {
        HashSet<string> names = new HashSet<string>();
        foreach (CardData card in pool) if (card != null) names.Add(card.RunnerName);
        return names.Count;
    }

    // "Your runners": the player's own runners (plus the usual loans up to 10). When they can't fill the legs (too
    // few women or men, or too strong for the max average), the cheapest runners that are missing are lent as well.
    public static List<CardData> YourRunnersPool(IList<LegSex> legs, int cap)
    {
        List<CardData> pool = RunnerCollection.CardsForRace();
        int budget = cap > 0 ? cap * legs.Count : int.MaxValue;
        if (CanFill(pool, legs, budget)) return pool;

        List<CardData> spare = RunnerCards.InPacks().FindAll(c => !pool.Exists(p => p.RunnerName == c.RunnerName));
        spare.Sort((a, b) => BudgetCost(a).CompareTo(BudgetCost(b)));
        List<CardData> lent = new List<CardData>();
        // One cheap woman and one cheap man at a time until the legs can be filled
        for (int round = 0; round < GameModes.MaxLegs * 2 && !CanFill(pool, legs, budget); round++)
        {
            foreach (bool man in new[] { false, true })
            {
                CardData next = spare.Find(c => c.man == man);
                if (next == null) continue;
                spare.Remove(next);
                pool.Add(next);
                lent.Add(next);
            }
        }
        RunnerCollection.Lend(lent);
        return pool;
    }

    // ---- "Open packs" ----

    // Draws one runner per leg, fitting the leg, with the shop's odds (PackShop.Weight); never the same one twice
    public static List<CardData> DrawPacks(IList<LegSex> legs)
    {
        List<CardData> drawn = new List<CardData>();
        List<CardData> all = RunnerCards.InPacks();
        foreach (LegSex leg in legs)
        {
            List<CardData> fitting = all.FindAll(c => Fits(c, leg) && !drawn.Exists(d => d.RunnerName == c.RunnerName));
            if (fitting.Count == 0) fitting = all.FindAll(c => !drawn.Exists(d => d.RunnerName == c.RunnerName));
            if (fitting.Count == 0) fitting = all;
            float total = 0f;
            foreach (CardData card in fitting) total += PackShop.Weight(RunnerCards.Tier(card));
            float roll = Random.value * total;
            CardData pick = fitting[fitting.Count - 1];
            foreach (CardData card in fitting)
            {
                roll -= PackShop.Weight(RunnerCards.Tier(card));
                if (roll <= 0f) { pick = card; break; }
            }
            drawn.Add(pick);
        }
        Opened.Clear();
        Opened.AddRange(drawn);
        return drawn;
    }

    // ---- Texts for the Choose a runner screen ----

    // "Max average 75: 212 of 300 left for 3 legs" (empty without a max)
    public static string CapText(List<CardData> picks, int legCount)
    {
        if (Cap <= 0) return "";
        if (legCount <= 1) return "Max overall " + Cap;
        int left = Cap * legCount - Spent(picks);
        int legsLeft = legCount - picks.Count;
        return "Max average " + Cap + ":  " + left + " of " + (Cap * legCount) + " left for " + legsLeft + (legsLeft == 1 ? " leg" : " legs");
    }
}
