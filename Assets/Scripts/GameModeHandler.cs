using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameModeHandler : MonoBehaviour
{
    public List<Texture2D> gameModes = new List<Texture2D>();
    public string gameMode;

    void Awake()
    {
        gameModes.Clear();
        gameModes.AddRange(Resources.LoadAll<Texture2D>("GameModes"));
        // "Your runners" first: it is the default
        gameModes.Sort((a, b) => (b.name == GameModes.YourRunners).CompareTo(a.name == GameModes.YourRunners));
        gameMode = gameModes.Count > 0 ? gameModes[0].name : GameModes.YourRunners;
    }
    public void SetGameMode(string GameMode)
    {
        gameMode = GameMode;
    }
}

// The runner pools (the lobby's "Runner pool"; the pictures in Resources/GameModes are named after them).
// The names are sent over the network, so they stay as they are; PoolName is what the screens show.
// "Choose a runner of three" was removed on 2026-11-02 (user's wish).
public static class GameModes
{
    public const string YourRunners = "Your runners";
    public const string ChooseAny = "Choose whomever you want";

    // "Same stats" (added 2026-11-04): the host sets speed and technique, everyone picks any runner and all of them
    // run with those values. Sent as "Same stats|<speed>|<technique>" (MakeSameStats).
    // Relays (2026-11-11): the host may set other values for each leg. Leg 1 stays "|<speed>|<technique>" and legs
    // 2, 3, ... follow as one more part of hex pairs: "Same stats|85|70|5A465046" = leg 2 90/70, leg 3 80/70.
    // Kept short so ten legs still fit the lobby's 64-character network string. A leg without values of its own
    // uses the last leg's values.
    public const string SameStatsMode = "Same stats";

    // The runner pools offered in the lobby and, since 2026-11-13, in single player too
    public static readonly string[] LobbyPools = { YourRunners, ChooseAny, SameStatsMode };

    // The whole scale (2026-11-13, the user wanted every value). Speed 0 would never reach the finish, so it starts at 1.
    public const int MinSpeed = 1, MaxSpeed = 100, MinTechnique = 0, MaxTechnique = 100;
    public const int MaxLegs = 10;

    public static string PoolName(string mode)
    {
        if (string.IsNullOrEmpty(mode)) return "...";
        if (IsSameStats(mode))
        {
            if (!TryGetSameStatsLegs(mode, out List<Vector2Int> legs)) return "Same stats";
            if (!AllLegsAlike(legs)) return "Same stats per leg";   // kept short for the lobby bar
            return "Same stats " + legs[0].x + "/" + legs[0].y;     // speed/technique
        }
        return IsYourRunners(mode) ? "Your runners" : mode == ChooseAny ? "Any runner" : mode;
    }

    public static string PoolDescription(string mode)
    {
        if (IsSameStats(mode)) return "You set speed and technique (for each leg in a relay). Everyone picks any runner, and all run with your values.";
        return IsYourRunners(mode) ? "Everyone races with runners from their own collection." : "Everyone may pick any runner in the game.";
    }

    // The option id of a mode ("Same stats|85|70" is the option "Same stats")
    public static string PoolId(string mode) => IsSameStats(mode) ? SameStatsMode : mode;

    public static bool IsSameStats(string mode) => !string.IsNullOrEmpty(mode) && mode.StartsWith(SameStatsMode);

    public static string MakeSameStats(int speed, int technique)
    {
        return MakeSameStats(new List<Vector2Int> { new Vector2Int(speed, technique) });
    }

    // One speed/technique pair per leg (x = speed, y = technique). Legs at the end that only repeat the leg before
    // are left out, so a plain race or a relay with the same values everywhere gives the short "Same stats|85|70".
    public static string MakeSameStats(IList<Vector2Int> legs)
    {
        List<Vector2Int> clamped = new List<Vector2Int>();
        if (legs != null)
            for (int i = 0; i < legs.Count && i < MaxLegs; i++) clamped.Add(Clamp(legs[i]));
        if (clamped.Count == 0) clamped.Add(Clamp(new Vector2Int(MaxSpeed, MaxTechnique)));
        while (clamped.Count > 1 && clamped[clamped.Count - 1] == clamped[clamped.Count - 2]) clamped.RemoveAt(clamped.Count - 1);

        string mode = SameStatsMode + "|" + clamped[0].x + "|" + clamped[0].y;
        if (clamped.Count > 1)
        {
            System.Text.StringBuilder more = new System.Text.StringBuilder();
            for (int i = 1; i < clamped.Count; i++) more.Append(clamped[i].x.ToString("X2")).Append(clamped[i].y.ToString("X2"));
            mode += "|" + more;
        }
        return mode;
    }

    static Vector2Int Clamp(Vector2Int v)
    {
        return new Vector2Int(Mathf.Clamp(v.x, MinSpeed, MaxSpeed), Mathf.Clamp(v.y, MinTechnique, MaxTechnique));
    }

    // Leg 1's values (the only ones, outside relays)
    public static bool TryGetSameStats(string mode, out int speed, out int technique)
    {
        speed = 0;
        technique = 0;
        if (!TryGetSameStatsLegs(mode, out List<Vector2Int> legs)) return false;
        speed = legs[0].x;
        technique = legs[0].y;
        return true;
    }

    // Every leg's values, leg 1 first. Legs after the last one in the list use the last one's values.
    public static bool TryGetSameStatsLegs(string mode, out List<Vector2Int> legs)
    {
        legs = null;
        if (!IsSameStats(mode)) return false;
        string[] parts = mode.Split('|');
        if (parts.Length < 3 || !int.TryParse(parts[1], out int speed) || !int.TryParse(parts[2], out int technique)) return false;
        legs = new List<Vector2Int> { Clamp(new Vector2Int(speed, technique)) };
        if (parts.Length >= 4)
        {
            string more = parts[3];
            for (int i = 0; i + 4 <= more.Length && legs.Count < MaxLegs; i += 4)
            {
                if (!int.TryParse(more.Substring(i, 2), System.Globalization.NumberStyles.HexNumber, null, out int s)) break;
                if (!int.TryParse(more.Substring(i + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out int t)) break;
                legs.Add(Clamp(new Vector2Int(s, t)));
            }
        }
        return true;
    }

    // The values of one leg (index from 0) in a list from TryGetSameStatsLegs
    public static Vector2Int LegValues(IList<Vector2Int> legs, int leg)
    {
        return legs[Mathf.Clamp(leg, 0, legs.Count - 1)];
    }

    static bool AllLegsAlike(List<Vector2Int> legs)
    {
        for (int i = 1; i < legs.Count; i++) if (legs[i] != legs[0]) return false;
        return true;
    }

    // The values every runner uses in the competition being played (x = speed, y = technique, one per relay leg),
    // or null when the runners keep their own. Set on each player when the runner picking starts.
    public static List<Vector2Int> SameStatsLegs
    {
        get => sameStatsLegs;
        set
        {
            sameStatsCopies.Clear();
            sameStatsLegs = value != null && value.Count > 0 ? value : null;
        }
    }
    static List<Vector2Int> sameStatsLegs;
    static readonly Dictionary<(CardData, Vector2Int), CardData> sameStatsCopies = new Dictionary<(CardData, Vector2Int), CardData>();

    public static bool SameStatsActive => sameStatsLegs != null;

    // The values of one leg in the competition being played (leg 0 outside relays)
    public static Vector2Int? SameStatsForLeg(int leg)
    {
        return sameStatsLegs == null ? (Vector2Int?)null : LegValues(sameStatsLegs, leg);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSameStats()
    {
        sameStatsLegs = null;
        sameStatsCopies.Clear();
    }

    // The runners to pick from: as they are, or copies with the host's values in "Same stats" (the values of that
    // relay leg; leg 0 outside relays). The same copy is given back every time for the same values.
    public static List<CardData> WithSameStats(List<CardData> cards, int leg = 0)
    {
        Vector2Int? values = SameStatsForLeg(leg);
        if (values == null || cards == null) return cards;
        List<CardData> result = new List<CardData>(cards.Count);
        foreach (CardData card in cards)
        {
            if (card == null) continue;
            var key = (card, values.Value);
            if (!sameStatsCopies.TryGetValue(key, out CardData copy))
            {
                copy = card.WithStats(values.Value.x, values.Value.y);
                sameStatsCopies[key] = copy;
            }
            result.Add(copy);
        }
        return result;
    }

    public static bool IsYourRunners(string mode) => !string.IsNullOrEmpty(mode) && mode.Contains(YourRunners);

    // How many different runners the player owns
    public static int OwnedRunnerCount() => RunnerCollection.OwnedCards().Count;

    // How many runners the player can race with in "Your runners": at least 10, with runners on loan
    public static int RaceRunnerCount() => Mathf.Max(OwnedRunnerCount(), RunnerCollection.RaceMinimum);

    // In "Your runners" a relay can only be run when every player owns at least as many runners as it has legs.
    // Returns why not, or null when it is fine.
    public static string RelayNeedsMoreRunners(CourseData course, string mode)
    {
        if (course == null || !course.IsRelay() || !IsYourRunners(mode)) return null;
        foreach (NetworkPlayer player in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid) continue;
            int owned = player.Object.HasStateAuthority ? RaceRunnerCount() : player.ownedRunners;
            if (owned < course.legs)
            {
                string who = player.Object.HasStateAuthority ? "you only have" : player.nickName.ToString() + " only has";
                return "\"" + course.name + "\" has " + course.legs + " legs, but " + who + " " + owned + (owned == 1 ? " runner" : " runners")
                    + ". Choose a relay with fewer legs, or the runner pool \"Any runner\".";
            }
        }
        return null;
    }
}
