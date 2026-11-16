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

    // The speed a runner races with in the forest (physics). On hard ground the race uses the sprint speed in part
    // or in full, see SurfaceFactor.
    public static float RaceSpeed(CardData card)
    {
        if (card == null) return 0f;
        return card.man ? card.physics : card.physics * WomenSpeedFactor;
    }

    // ---- Physics in the forest, speed on hard ground (2026-11-13) ----
    //
    // The grid only knows how fast each cell is, so the kind of ground is read from that (SymbolRules, normal sliders):
    //   asphalt, paved areas 1.5 and up        -> all sprint speed
    //   roads, gravel 1.4, vehicle tracks 1.35  -> about 80 % sprint speed, 20 % physics
    //   footpaths 1.25, small paths 1.2         -> about half and half
    //   open land, forest and slower (1.15 and below) -> all physics
    // Grass and open land on a sprint map (1.3) end up at about 65 % sprint speed.
    // Between the points the share goes in a straight line.
    public static float SprintShare(float groundSpeed)
    {
        if (groundSpeed <= 1.15f) return 0f;
        if (groundSpeed <= 1.2f) return Mathf.Lerp(0f, 0.5f, (groundSpeed - 1.15f) / 0.05f);
        if (groundSpeed <= 1.4f) return Mathf.Lerp(0.5f, 0.8f, (groundSpeed - 1.2f) / 0.2f);
        if (groundSpeed <= 1.5f) return Mathf.Lerp(0.8f, 1f, (groundSpeed - 1.4f) / 0.1f);
        return 1f;
    }

    // What the race speed (made from physics) is multiplied by on ground of this grid speed: 1 in the forest, and on
    // hard ground the mix of sprint speed and physics compared with physics. A runner without a sprint speed gets 1.
    public static float SurfaceFactor(float physics, float sprintSpeed, float groundSpeed)
    {
        if (sprintSpeed < 0f || physics <= 0f) return 1f;
        float share = SprintShare(groundSpeed);
        if (share <= 0f) return 1f;
        return (share * sprintSpeed + (1f - share) * physics) / physics;
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
    // "Random runners": how many random runners everyone gets on loan, or 0
    public static int RandomCount { get; private set; }
    public static readonly List<CardData> Opened = new List<CardData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Cap = 0;
        OpenPacksRound = false;
        RandomCount = 0;
        Opened.Clear();
    }

    public static void BeginRound(string mode)
    {
        Cap = GameModes.Cap(mode);
        OpenPacksRound = GameModes.IsOpenPacks(mode);
        RandomCount = GameModes.RandomCount(mode);
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
        List<CardData> sorted = new List<CardData>(pool);
        sorted.RemoveAll(c => c == null);
        sorted.Sort((a, b) => BudgetCost(a).CompareTo(BudgetCost(b)));
        return Cheapest(sorted, legs, repeats, null, out long total, out _) && total <= budget;
    }

    // The cheapest runners for the legs from a pool sorted cheapest first, leaving out one runner (exclude)
    static bool Cheapest(List<CardData> sorted, IList<LegSex> legs, bool repeats, string exclude, out long total, out HashSet<string> used)
    {
        used = new HashSet<string>();
        total = 0;
        foreach (LegSex pass in new[] { LegSex.Women, LegSex.Men, LegSex.Any })
        {
            foreach (LegSex leg in legs)
            {
                if (leg != pass) continue;
                CardData cheapest = null;
                foreach (CardData c in sorted)
                {
                    if (c.RunnerName == exclude || !Fits(c, leg) || (!repeats && used.Contains(c.RunnerName))) continue;
                    cheapest = c;
                    break;
                }
                if (cheapest == null) return false;
                used.Add(cheapest.RunnerName);
                total += BudgetCost(cheapest);
            }
        }
        return true;
    }

    // The runners that may run the next leg: they fit the leg, and the legs after it must still be possible with the
    // runners left and (with a max average) the overall left. Fast enough for every runner in the game: the cheapest
    // way to fill the rest is worked out once, and again only for the few runners it uses.
    public static List<CardData> AllowedList(List<CardData> pool, List<CardData> picks, IList<LegSex> legs, int cap)
    {
        List<CardData> result = new List<CardData>();
        int leg = picks.Count;
        if (leg >= legs.Count) return result;
        bool repeats = CountUnique(pool) < legs.Count;
        List<CardData> left = pool.FindAll(c => c != null && (repeats || !picks.Exists(p => p.RunnerName == c.RunnerName)));
        List<CardData> sorted = new List<CardData>(left);
        sorted.Sort((a, b) => BudgetCost(a).CompareTo(BudgetCost(b)));
        List<LegSex> rest = new List<LegSex>();
        for (int i = leg + 1; i < legs.Count; i++) rest.Add(legs[i]);
        long budget = cap > 0 ? (long)cap * legs.Count - Spent(picks) : long.MaxValue;

        bool baseOk = Cheapest(sorted, rest, repeats, null, out long baseTotal, out HashSet<string> baseUsed);
        HashSet<string> seen = new HashSet<string>();
        foreach (CardData card in left)
        {
            if (!seen.Add(card.RunnerName) || !Fits(card, legs[leg])) continue;
            bool ok = baseOk;
            long restCost = baseTotal;
            if (!repeats && baseUsed.Contains(card.RunnerName)) ok = Cheapest(sorted, rest, repeats, card.RunnerName, out restCost, out _);
            if (ok && (budget == long.MaxValue || restCost + BudgetCost(card) <= budget)) result.Add(card);
        }
        return result;
    }

    public static bool Allowed(CardData candidate, List<CardData> pool, List<CardData> picks, IList<LegSex> legs, int cap)
    {
        return candidate != null && AllowedList(pool, picks, legs, cap).Exists(c => c.RunnerName == candidate.RunnerName);
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

    // "Your runners": the player's own runners (2026-11-16: no longer filled up to 10). When they can't fill the legs
    // (too few runners, too few women or men, or too strong for the max average), only the missing runners are lent:
    // random ones, or the cheapest with a max average. "Random runners" goes the same way from its random runners.
    public static List<CardData> YourRunnersPool(IList<LegSex> legs, int cap)
    {
        List<CardData> pool = RunnerCollection.CardsForRace();
        int budget = cap > 0 ? cap * legs.Count : int.MaxValue;
        if (CanFill(pool, legs, budget)) return pool;

        List<CardData> spare = RunnerCards.InPacks().FindAll(c => !pool.Exists(p => p.RunnerName == c.RunnerName));
        List<CardData> lent = new List<CardData>();
        if (cap <= 0)
        {
            // Without a max average only the number counts: the missing women, the missing men, then anyone
            System.Random random = new System.Random();
            int women = 0, men = 0, womenLegs = 0, menLegs = 0;
            HashSet<string> names = new HashSet<string>();
            foreach (CardData card in pool)
            {
                if (card == null || !names.Add(card.RunnerName)) continue;
                if (card.man) men++; else women++;
            }
            foreach (LegSex leg in legs)
            {
                if (leg == LegSex.Women) womenLegs++;
                else if (leg == LegSex.Men) menLegs++;
            }
            void LendOne(bool? man)
            {
                List<CardData> fitting = man.HasValue ? spare.FindAll(c => c.man == man.Value) : spare;
                if (fitting.Count == 0) return;
                CardData next = fitting[random.Next(fitting.Count)];
                spare.Remove(next);
                pool.Add(next);
                lent.Add(next);
                if (next.man) men++; else women++;
            }
            while (women < womenLegs && spare.Exists(c => !c.man)) LendOne(false);
            while (men < menLegs && spare.Exists(c => c.man)) LendOne(true);
            while (women + men < legs.Count && spare.Count > 0) LendOne(null);
        }
        // With a max average (or if the count above was not enough): one cheap woman and one cheap man at a time
        spare.Sort((a, b) => BudgetCost(a).CompareTo(BudgetCost(b)));
        for (int round = 0; round < GameModes.MaxLegs * 2 && !CanFill(pool, legs, budget); round++)
        {
            foreach (bool man in new[] { false, true })
            {
                if (CanFill(pool, legs, budget)) break;
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
