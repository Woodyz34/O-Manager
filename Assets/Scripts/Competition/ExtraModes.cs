using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// The game modes the host can turn on in the lobby, on top of the runner pool. Several can be used together,
// except Route analysis and the Route choice game, which are always played on their own, and the two imposter
// modes (Imposter and Secret task), of which only one can be on.
[Flags]
public enum ExtraMode
{
    None = 0,
    Imposter = 1,
    Bears = 2,
    MoveControl = 4,
    RouteAnalysis = 8,
    RouteChoice = 16,
    SecretTask = 32,
}

public static class ExtraModes
{
    public static readonly ExtraMode[] All = { ExtraMode.Imposter, ExtraMode.SecretTask, ExtraMode.Bears, ExtraMode.MoveControl, ExtraMode.RouteAnalysis, ExtraMode.RouteChoice };

    // Every mode, for "show them all"
    public static ExtraMode AllMask
    {
        get
        {
            ExtraMode all = ExtraMode.None;
            foreach (ExtraMode mode in All) all |= mode;
            return all;
        }
    }

    // Modes that can be played alone in single player (2026-11-13). The imposter modes need someone to fool, a bear
    // you hid yourself is no surprise, and a control you moved yourself is no trick.
    public const ExtraMode Solo = ExtraMode.RouteAnalysis | ExtraMode.RouteChoice;

    // Modes that cannot be combined with any other mode
    public const ExtraMode Alone = ExtraMode.RouteAnalysis | ExtraMode.RouteChoice;

    // The two imposter modes both pick one imposter, so only one of them can be on
    public const ExtraMode ImposterKinds = ExtraMode.Imposter | ExtraMode.SecretTask;

    // The imposter modes need at least this many players, or the imposter is too easy to spot
    public const int MinImposterPlayers = 3;

    public static int PlayersInLobby()
    {
        int n = 0;
        foreach (NetworkPlayer p in UnityEngine.Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (p.Object != null && p.Object.IsValid) n++;
        }
        return n;
    }

    // Why this mode cannot be played with this many players, or null when it can
    public static string NotWithPlayers(ExtraMode mode, int players)
    {
        if ((mode & ImposterKinds) != 0 && players < MinImposterPlayers)
            return "You need at least " + MinImposterPlayers + " players for this game mode";
        return null;
    }

    // Modes where nobody picks a runner (everyone gets the same one), so the runner pool does not matter
    public const ExtraMode NoRunners = ExtraMode.RouteAnalysis | ExtraMode.RouteChoice;

    // Modes that only work on individual courses, not relays
    public const ExtraMode IndividualOnly = ExtraMode.SecretTask | ExtraMode.Bears | ExtraMode.MoveControl | ExtraMode.RouteAnalysis | ExtraMode.RouteChoice;

    // Modes that only work in the forest, not on sprint courses (Imposter works on both)
    public const ExtraMode ForestOnly = ExtraMode.Bears | ExtraMode.MoveControl;

    // Set on the value the host shares, so "nothing turned on" can be told apart from "not arrived yet"
    public const int KnownBit = 1 << 30;

    public static string Title(ExtraMode mode)
    {
        switch (mode)
        {
            case ExtraMode.Imposter: return "Imposter";
            case ExtraMode.SecretTask: return "Secret task";
            case ExtraMode.Bears: return "Bears";
            case ExtraMode.MoveControl: return "Move a control";
            case ExtraMode.RouteAnalysis: return "Route analysis";
            case ExtraMode.RouteChoice: return "Route choice game";
            default: return mode.ToString();
        }
    }

    public static string Description(ExtraMode mode)
    {
        switch (mode)
        {
            case ExtraMode.Imposter: return "One random player secretly has to finish last. Can you spot who?";
            case ExtraMode.SecretTask: return "Everyone but one gets a secret task. The imposter isn't told it and must blend in.";
            case ExtraMode.Bears: return "Everyone hides one bear while drawing. Runners who find it get scared and run round it. Forest only.";
            case ExtraMode.MoveControl: return "Everyone may move one control up to 100 m while drawing. Good routes can turn bad. Forest only.";
            case ExtraMode.RouteAnalysis: return "Nobody runs: everyone draws the whole course the way they would run it, then you compare the route choices. Played on its own.";
            case ExtraMode.RouteChoice: return "Nobody runs: on each leg, pick the fastest of 2 to 4 routes. Points for right answers, more the quicker you are. Played on its own.";
            default: return "";
        }
    }

    // The hover text of the game mode shown in the lobby: explains the modes that are on now, nothing else
    public static string Explain(ExtraMode modes)
    {
        if (modes == ExtraMode.None) return "Normal: no extra game modes. Everyone just races to finish first.";
        List<string> lines = new List<string>();
        foreach (ExtraMode mode in All)
        {
            if ((modes & mode) != 0) lines.Add(Title(mode) + ": " + Description(mode));
        }
        return string.Join("\n", lines);
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
        if ((mode & ImposterKinds) != 0) modes &= ~ImposterKinds;
        return (modes & ~Alone) | mode;
    }

    // Why the modes cannot be played on this course, or null when they can
    public static string CourseProblem(ExtraMode modes, CourseData course)
    {
        if (course == null) return null;
        if (course.IsRelay())
        {
            ExtraMode notRelay = modes & IndividualOnly;
            if (notRelay != ExtraMode.None) return "\"" + Describe(notRelay) + "\" only works on individual courses, not relays. Choose another course or game mode.";
        }
        if (course.IsSprint())
        {
            ExtraMode notSprint = modes & ForestOnly;
            if (notSprint != ExtraMode.None) return "\"" + Describe(notSprint) + "\" only works on forest courses, not sprint. Choose another course or game mode.";
        }
        return null;
    }

    // Why this mode cannot be ticked for the course chosen in the lobby, or null when it can
    public static string NotOn(ExtraMode mode, CourseData course)
    {
        if (course == null) return null;
        if ((mode & IndividualOnly) != 0 && course.IsRelay()) return "You can't have this game mode on a relay";
        if ((mode & ForestOnly) != 0 && course.IsSprint()) return "You can't have this game mode on sprint";
        return null;
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
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit); not a new race
        if (mode == LoadSceneMode.Additive) return;
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

    // Imposter and Secret task: nobody may see who placed a bear or moved a control (it would give the imposter
    // away); in the other modes the names are shown (2026-11-15)
    public static bool HideOwners => (Active & ImposterKinds) != 0;

    static void Refresh()
    {
        if (known) return;
        if (ActiveScene.Name == "Lobby") return;
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.Runner != null && local.Runner.GameMode == GameMode.Single)
        {
            // A single player game has no lobby: the modes come from the single player page
            known = true;
            active = SinglePlayerMenu.ChosenModes & Solo;
            if (RelayRace.Active != null) active &= ~IndividualOnly;
            if (CourseLoader.Loaded != null && CourseLoader.Loaded.IsSprint()) active &= ~ForestOnly;
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
                if (CourseLoader.Loaded != null && CourseLoader.Loaded.IsSprint()) active &= ~ForestOnly;
                Debug.Log("Game modes in this competition: " + Describe(active));
                return;
            }
        }
    }
}
