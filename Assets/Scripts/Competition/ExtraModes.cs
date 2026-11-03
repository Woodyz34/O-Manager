using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// The game modes the host can turn on in the lobby, on top of the runner pool. Several can be used together,
// except Route analysis, which is always played on its own.
[Flags]
public enum ExtraMode
{
    None = 0,
    Imposter = 1,
    Bears = 2,
    MoveControl = 4,
    RouteAnalysis = 8,
}

public static class ExtraModes
{
    public static readonly ExtraMode[] All = { ExtraMode.Imposter, ExtraMode.Bears, ExtraMode.MoveControl, ExtraMode.RouteAnalysis };

    // Modes that cannot be combined with any other mode
    public const ExtraMode Alone = ExtraMode.RouteAnalysis;

    // Modes that only work on individual courses, not relays
    public const ExtraMode IndividualOnly = ExtraMode.Bears | ExtraMode.MoveControl | ExtraMode.RouteAnalysis;

    // Set on the value the host shares, so "nothing turned on" can be told apart from "not arrived yet"
    public const int KnownBit = 1 << 30;

    public static string Title(ExtraMode mode)
    {
        switch (mode)
        {
            case ExtraMode.Imposter: return "Imposter";
            case ExtraMode.Bears: return "Bears";
            case ExtraMode.MoveControl: return "Move a control";
            case ExtraMode.RouteAnalysis: return "Route analysis";
            default: return mode.ToString();
        }
    }

    public static string Description(ExtraMode mode)
    {
        switch (mode)
        {
            case ExtraMode.Imposter: return "One random player secretly has to finish last. Can you spot who?";
            case ExtraMode.Bears: return "Everyone hides one bear while drawing. Runners who find it get scared and run round it.";
            case ExtraMode.MoveControl: return "Everyone may move one control up to 100 m while drawing. Good routes can turn bad.";
            case ExtraMode.RouteAnalysis: return "No runner cards: draw how you would run yourself, then compare everyone's route choices. Played on its own.";
            default: return "";
        }
    }

    // "Normal", or the modes that are on: "Imposter + Bears"
    public static string Describe(ExtraMode modes)
    {
        if (modes == ExtraMode.None) return "Normal";
        List<string> names = new List<string>();
        foreach (ExtraMode mode in All)
        {
            if ((modes & mode) != 0) names.Add(Title(mode));
        }
        return string.Join(" + ", names);
    }

    // Turning one mode on or off. A mode that is played alone turns the others off, and the other way round.
    public static ExtraMode Toggle(ExtraMode modes, ExtraMode mode)
    {
        if ((modes & mode) != 0) return modes & ~mode;
        if ((mode & Alone) != 0) return mode;
        return (modes & ~Alone) | mode;
    }

    // Why the modes cannot be played on this course, or null when they can
    public static string CourseProblem(ExtraMode modes, CourseData course)
    {
        if (course == null || !course.IsRelay()) return null;
        ExtraMode bad = modes & IndividualOnly;
        if (bad == ExtraMode.None) return null;
        return "\"" + Describe(bad) + "\" only works on individual courses, not relays. Choose another course or game mode.";
    }

    // ---- In the competition ----

    static ExtraMode active;
    static bool known;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        known = false;
        active = ExtraMode.None;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        known = false;
        active = ExtraMode.None;
    }

    // True once the host's choice has arrived (or there is nothing to wait for)
    public static bool Known
    {
        get
        {
            Refresh();
            return known;
        }
    }

    // The modes of the competition being played. The host stores them on its own player when the game starts.
    public static ExtraMode Active
    {
        get
        {
            Refresh();
            return active;
        }
    }

    public static bool Is(ExtraMode mode) => (Active & mode) != 0;

    static void Refresh()
    {
        if (known) return;
        if (SceneManager.GetActiveScene().name == "Lobby") return;
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.Runner != null && local.Runner.GameMode == GameMode.Single)
        {
            // A single player game has no lobby, so no extra modes
            known = true;
            active = ExtraMode.None;
            return;
        }
        foreach (NetworkPlayer p in UnityEngine.Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (p.Object == null || !p.Object.IsValid) continue;
            int value = p.competitionModes;
            if ((value & KnownBit) != 0)
            {
                known = true;
                active = (ExtraMode)(value & ~KnownBit);
                // The relay check is done in the lobby; this is only a guard
                if (RelayRace.Active != null) active &= ~IndividualOnly;
                Debug.Log("Game modes in this competition: " + Describe(active));
                return;
            }
        }
    }
}
