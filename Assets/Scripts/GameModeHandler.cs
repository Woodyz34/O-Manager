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

// The names of the game modes (the pictures in Resources/GameModes are named after them)
public static class GameModes
{
    public const string YourRunners = "Your runners";
    public const string ChooseOfThree = "Choose a runner of three";
    public const string ChooseAny = "Choose whomever you want";

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
                    + ". Choose a relay with fewer legs, or another game mode.";
            }
        }
        return null;
    }
}
