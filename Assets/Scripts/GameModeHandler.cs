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
    public const string SameStatsMode = "Same stats";

    // Single player offers the first two; the lobby all three
    public static readonly string[] Pools = { YourRunners, ChooseAny };
    public static readonly string[] LobbyPools = { YourRunners, ChooseAny, SameStatsMode };

    public const int MinSpeed = 40, MaxSpeed = 100, MinTechnique = 20, MaxTechnique = 100;

    public static string PoolName(string mode)
    {
        if (string.IsNullOrEmpty(mode)) return "...";
        if (IsSameStats(mode))
        {
            return TryGetSameStats(mode, out int speed, out int technique)
                ? "Same stats  <size=70%>speed " + speed + ", technique " + technique + "</size>"
                : "Same stats";
        }
        return IsYourRunners(mode) ? "Your runners" : mode == ChooseAny ? "Any runner" : mode;
    }

    public static string PoolDescription(string mode)
    {
        if (IsSameStats(mode)) return "You set speed and technique. Everyone picks any runner, and all run with your values.";
        return IsYourRunners(mode) ? "Everyone races with runners from their own collection." : "Everyone may pick any runner in the game.";
    }

    // The option id of a mode ("Same stats|85|70" is the option "Same stats")
    public static string PoolId(string mode) => IsSameStats(mode) ? SameStatsMode : mode;

    public static bool IsSameStats(string mode) => !string.IsNullOrEmpty(mode) && mode.StartsWith(SameStatsMode);

    public static string MakeSameStats(int speed, int technique)
    {
        return SameStatsMode + "|" + Mathf.Clamp(speed, MinSpeed, MaxSpeed) + "|" + Mathf.Clamp(technique, MinTechnique, MaxTechnique);
    }

    public static bool TryGetSameStats(string mode, out int speed, out int technique)
    {
        speed = 0;
        technique = 0;
        if (!IsSameStats(mode)) return false;
        string[] parts = mode.Split('|');
        if (parts.Length < 3 || !int.TryParse(parts[1], out speed) || !int.TryParse(parts[2], out technique)) return false;
        speed = Mathf.Clamp(speed, MinSpeed, MaxSpeed);
        technique = Mathf.Clamp(technique, MinTechnique, MaxTechnique);
        return true;
    }

    // The values every runner uses in the competition being played (x = speed, y = technique), or null when the
    // runners keep their own. Set on each player when the runner picking starts.
    public static Vector2? SameStats
    {
        get => sameStats;
        set
        {
            if (sameStats != value) sameStatsCopies.Clear();
            sameStats = value;
        }
    }
    static Vector2? sameStats;
    static readonly Dictionary<CardData, CardData> sameStatsCopies = new Dictionary<CardData, CardData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSameStats()
    {
        sameStats = null;
        sameStatsCopies.Clear();
    }

    // The runners to pick from: as they are, or copies with the host's values in "Same stats". The same copy is
    // given back every time, so a relay can tell which runners are already picked.
    public static List<CardData> WithSameStats(List<CardData> cards)
    {
        if (sameStats == null || cards == null) return cards;
        List<CardData> result = new List<CardData>(cards.Count);
        foreach (CardData card in cards)
        {
            if (card == null) continue;
            if (!sameStatsCopies.TryGetValue(card, out CardData copy))
            {
                copy = card.WithStats(sameStats.Value.x, sameStats.Value.y);
                sameStatsCopies[card] = copy;
            }
            result.Add(copy);
        }
        return result;
    }

    public static bool IsYourRunners(string mode) => !string.IsNullOrEmpty(mode) && mode.Contains(YourRunners);

    // How many different runners the player owns
    public static int OwnedRunnerCount() => RunnerCollection.OwnedCards().Count;

    // In "Your runners" a relay can only be run when every player owns at least as many runners as it has legs.
    // Returns why not, or null when it is fine.
    public static string RelayNeedsMoreRunners(CourseData course, string mode)
    {
        if (course == null || !course.IsRelay() || !IsYourRunners(mode)) return null;
        foreach (NetworkPlayer player in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid) continue;
            int owned = player.Object.HasStateAuthority ? OwnedRunnerCount() : player.ownedRunners;
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
