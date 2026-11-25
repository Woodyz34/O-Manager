using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// One runner's line in the final standings.
public class Standing
{
    public PlayerRef player;
    public string name;
    public bool finished;
    public int seconds;
    public int place;      // 1 = winner. Runners who did not finish share last place.
    public bool isLocal;
}

// Decides when a competition is over and what happens then: the results screen, the imposter vote
// and the reveal. Each player runs this for their own NetworkPlayer; only the host (master client)
// is allowed to move everyone on to the next step.
public static class CompetitionFlow
{
    public const float GuessSeconds = 30f;

    public static bool ResultsShown { get; private set; }
    public static bool RevealStarted { get; private set; }
    public static float GuessDeadline { get; private set; }

    static float sceneLoadedAt;
    static float resultsShownAt;
    static float allFinishedSince;
    // When the last runner reached the finish (the statistics / GPS screen), or -1 while someone is still out
    static float everyoneDoneAt;
    // The host's played time for the whole lobby, sent with the results so everyone gets the same, or -1
    static float sharedPlayed;
    static bool resultsRequested;
    static bool revealRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        ResetState();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit); not a new race
        if (mode == LoadSceneMode.Additive) return;
        ResetState();
    }

    static void ResetState()
    {
        ResultsShown = false;
        RevealStarted = false;
        resultsRequested = false;
        revealRequested = false;
        allFinishedSince = -1f;
        everyoneDoneAt = -1f;
        sharedPlayed = -1f;
        rememberedImposter = PlayerRef.None;
        sceneLoadedAt = GamePause.PlayClock;
    }

    // How long this competition has been played, in real seconds, for the prize money (2026-11-19). Since 2026-11-24
    // (the user) it is the same for everyone in the lobby: from entering the competition until the last runner is
    // in (the statistics / GPS screen), so a fast runner who waits for the others is not paid less. The host's
    // value comes with the results; before that, and alone, it is this computer's own clock.
    public static float PlayedSeconds
    {
        get
        {
            if (sharedPlayed >= 0f) return sharedPlayed;
            return LocalPlayedSeconds;
        }
    }

    static float LocalPlayedSeconds
    {
        get
        {
            float end = everyoneDoneAt >= 0f ? everyoneDoneAt : ResultsShown ? resultsShownAt : GamePause.PlayClock;
            return Mathf.Max(0f, end - sceneLoadedAt);
        }
    }

    // Every runner is in: the played time stops here, also while the statistics / GPS screen stays open
    public static void MarkEveryoneDone()
    {
        if (everyoneDoneAt >= 0f || ResultsShown || !InCompetition()) return;
        everyoneDoneAt = GamePause.PlayClock;
    }

    // The host's played time, sent to everyone with the results
    static int HostPlayedSeconds => Mathf.RoundToInt(LocalPlayedSeconds);

    static bool InCompetition()
    {
        return ActiveScene.Name != "Lobby";
    }

    // Called every frame for the local player's own NetworkPlayer.
    public static void Tick(NetworkPlayer local)
    {
        if (local == null || local.Runner == null || !InCompetition())
        {
            return;
        }
        // Give the new scene a moment so values left over from the previous competition are reset first
        if (GamePause.PlayClock - sceneLoadedAt < 3f)
        {
            return;
        }

        // Tell everyone when we have reached the last control, with the exact time the clock stopped at
        if (!ResultsShown && !local.spectator && !local.raceFinished && local.agentMovement != null && local.agentMovement.hasFinished)
        {
            Clock clock = local.clock;
            if (clock != null)
            {
                local.secoundCount = (int)clock.sekundercount;
                local.minuterCount = clock.minuterCount;
                local.hourCount = clock.hourCount;
            }
            local.raceFinished = true;
        }

        if (!IsHostRunner(local.Runner))
        {
            return;
        }

        if (!ResultsShown)
        {
            // A race without a relay: the played time stops when the last runner is in (a relay stops it after the
            // last leg, RelayRace)
            if (RelayRace.Active == null && AllPlayers(p => p.raceFinished))
            {
                MarkEveryoneDone();
            }
            // Everyone is in: show the results automatically. In a relay the host shows them from the statistics screen.
            if ((RelayRace.Active != null && !RelayRace.Active.FinalResultsAllowed)
                || (IndividualRace.Active != null && !IndividualRace.Active.FinalResultsAllowed))
            {
                allFinishedSince = -1f;
            }
            else if (AllPlayers(p => p.raceFinished))
            {
                if (allFinishedSince < 0f)
                {
                    allFinishedSince = GamePause.PlayClock;
                }
                else if (!resultsRequested && GamePause.PlayClock - allFinishedSince > 1.5f)
                {
                    resultsRequested = true;
                    local.RPC_ShowResults(HostPlayedSeconds);
                }
            }
            else
            {
                allFinishedSince = -1f;
            }
        }
        else if (!RevealStarted && !revealRequested && TryGetImposter(out PlayerRef imposter))
        {
            // Imposter mode: reveal when everyone has guessed, or when the time is up
            bool everyoneGuessed = GamePause.PlayClock - resultsShownAt > 3f
                && AllPlayers(p => p.hasGuessedImposter || p.Object.StateAuthority == imposter);
            if (everyoneGuessed || GamePause.PlayClock > GuessDeadline)
            {
                HostRevealImposter();
            }
        }
    }

    // ---- Host actions ----

    public static bool LocalIsHost()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        return local != null && IsHostRunner(local.Runner);
    }

    // The host of a shared session, or the only player in a single-player game
    public static bool IsHostRunner(NetworkRunner runner)
    {
        return runner != null && (runner.IsSharedModeMasterClient || runner.GameMode == GameMode.Single);
    }

    // Ends the competition for everyone and shows the results, also for runners who are still out on the course
    public static void HostShowResults()
    {
        if (ResultsShown || !LocalIsHost() || !InCompetition())
        {
            return;
        }
        resultsRequested = true;
        NetworkPlayer.Local.RPC_ShowResults(HostPlayedSeconds);
    }

    public static void HostRevealImposter()
    {
        if (!ResultsShown || RevealStarted || !LocalIsHost())
        {
            return;
        }
        revealRequested = true;
        NetworkPlayer.Local.RPC_RevealImposter();
    }

    // The host's button text for going back after a race: the lobby, or the single player page
    public static string BackButtonText
    {
        get
        {
            NetworkPlayer local = NetworkPlayer.Local;
            bool single = local != null && local.Runner != null && local.Runner.GameMode == GameMode.Single;
            if (single && ContestRun.Active != null) return "Back to contests";
            return single ? "Back to single player" : "Back to the lobby";
        }
    }

    public static void HostReturnToLobby()
    {
        if (!LocalIsHost())
        {
            return;
        }
        ReturnToLobby.LoadLobby(NetworkPlayer.Local.Runner);
    }

    // ---- Received by every player ----

    public static void OnShowResults(int hostPlayedSeconds)
    {
        if (ResultsShown || !InCompetition())
        {
            return;
        }
        ResultsShown = true;
        resultsShownAt = GamePause.PlayClock;
        // The same played time for everyone in the lobby: the host's
        if (hostPlayedSeconds >= 0) sharedPlayed = hostPlayedSeconds;
        GuessDeadline = GamePause.PlayClock + GuessSeconds;

        StopLocalRunner();
        if (ImposterOverlay.Instance != null)
        {
            ImposterOverlay.Instance.HideRoleChip();
        }
        GameAnalytics.Record("race_finished", "seconds", Mathf.RoundToInt(PlayedSeconds));
        EndScreenUI.Show();
    }

    public static void OnRevealImposter()
    {
        if (!ResultsShown || RevealStarted)
        {
            return;
        }
        RevealStarted = true;
        if (EndScreenUI.Current != null)
        {
            EndScreenUI.Current.BeginReveal();
        }
    }

    // The competition is over, so a runner who is still out on the course stops where they are
    static void StopLocalRunner()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null)
        {
            return;
        }
        AgentMovement agent = local.agentMovement;
        if (agent != null)
        {
            agent.hasFinished = true;
            if (agent.manualPathDrawer != null)
            {
                agent.manualPathDrawer.timeToDraw = false;
                agent.manualPathDrawer.isDrawing = false;
            }
        }
        if (local.clock != null)
        {
            local.clock.StopForResults();
        }
    }

    // ---- Standings ----

    public static List<Standing> ComputeStandings()
    {
        List<Standing> standings = new List<Standing>();
        foreach (NetworkPlayer p in ValidPlayers())
        {
            string playerName = p.nickName.ToString();
            standings.Add(new Standing
            {
                player = p.Object.StateAuthority,
                name = string.IsNullOrWhiteSpace(playerName) ? "Player " + p.Object.StateAuthority.PlayerId : playerName,
                finished = p.raceFinished,
                seconds = p.hourCount * 3600 + p.minuterCount * 60 + p.secoundCount,
                isLocal = p.Object.HasStateAuthority
            });
        }

        standings.Sort((a, b) =>
        {
            if (a.finished != b.finished) return a.finished ? -1 : 1;
            if (a.finished && a.seconds != b.seconds) return a.seconds.CompareTo(b.seconds);
            return a.player.PlayerId.CompareTo(b.player.PlayerId);
        });

        for (int i = 0; i < standings.Count; i++)
        {
            Standing current = standings[i];
            if (!current.finished)
            {
                current.place = standings.Count;
            }
            else if (i > 0 && standings[i - 1].finished && standings[i - 1].seconds == current.seconds)
            {
                current.place = standings[i - 1].place; // same time, same place
            }
            else
            {
                current.place = i + 1;
            }
        }
        return standings;
    }

    public static string FormatTime(int totalSeconds)
    {
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }

    public static string Ordinal(int number)
    {
        int lastTwo = number % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return number + "th";
        switch (number % 10)
        {
            case 1: return number + "st";
            case 2: return number + "nd";
            case 3: return number + "rd";
            default: return number + "th";
        }
    }

    // ---- Imposter mode ----

    public static bool IsImposterMode(string gameMode)
    {
        return !string.IsNullOrEmpty(gameMode) && gameMode.IndexOf("Imposter", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // The host stores the imposter on its own player object, so look at every player
    public static bool TryGetImposter(out PlayerRef imposter)
    {
        foreach (NetworkPlayer p in ValidPlayers())
        {
            if (!p.imposter.IsNone)
            {
                rememberedImposter = p.imposter;
                imposter = p.imposter;
                return true;
            }
        }
        // The host may have left during the competition and taken its player object with it,
        // so fall back on what we saw earlier in this competition
        if (!rememberedImposter.IsNone && InCompetition())
        {
            imposter = rememberedImposter;
            return true;
        }
        imposter = PlayerRef.None;
        return false;
    }

    static PlayerRef rememberedImposter;

    public static PlayerRef PickRandomPlayer(NetworkRunner runner)
    {
        List<PlayerRef> players = new List<PlayerRef>(runner.ActivePlayers);
        if (players.Count == 0)
        {
            return PlayerRef.None;
        }
        return players[UnityEngine.Random.Range(0, players.Count)];
    }

    public static NetworkPlayer FindPlayer(PlayerRef player)
    {
        foreach (NetworkPlayer p in ValidPlayers())
        {
            if (p.Object.StateAuthority == player)
            {
                return p;
            }
        }
        return null;
    }

    // The players in the race: a player who joined in the middle only watches (NetworkPlayer.spectator)
    static IEnumerable<NetworkPlayer> ValidPlayers()
    {
        foreach (NetworkPlayer p in NetworkPlayer.All())
        {
            if (NetworkPlayer.Races(p))
            {
                yield return p;
            }
        }
    }

    static bool AllPlayers(Func<NetworkPlayer, bool> condition)
    {
        int count = 0;
        foreach (NetworkPlayer p in ValidPlayers())
        {
            count++;
            if (!condition(p))
            {
                return false;
            }
        }
        return count > 0;
    }
}

// How much money a competition pays (2026-11-19, the user's new economy: some players earned money fast with
// courses that were over in seconds).
//  - Time played: 10 kr for every 30 seconds from the start to the results, nothing under 30 seconds, at most 2000 kr.
//  - Place: 50 kr for every runner beaten, 50 more for the winner.
//  - Nothing at all without reaching the finish (or after leaving).
//  - The imposter gets no place money in the top third, then more and more steeply the further back it comes, and
//    an extra boost for coming last without being found.
public static class CompetitionRewards
{
    public const int DidNotFinish = 0;
    public const int PerHalfMinute = 10;
    public const float MinPlayedSeconds = 30f;
    public const int MaxForTime = 2000;
    public const int PerRunnerBeaten = 50;

    public const int CorrectGuess = 150;
    public const int ImposterEscapeBase = 100;
    public const int ImposterEscapePerRunner = 50;
    public const int ImposterPlaceStep = 50;

    // The base money for the time played (only for those who finished)
    public static int ForTime(float seconds)
    {
        if (seconds < MinPlayedSeconds) return 0;
        return Mathf.Min(MaxForTime, Mathf.FloorToInt(seconds / 30f) * PerHalfMinute);
    }

    // "23 min 10 s"
    public static string Duration(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return (minutes > 0 ? minutes + " min " : "") + Mathf.FloorToInt(seconds % 60f) + " s";
    }

    public static string TimeLabel(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        return seconds < MinPlayedSeconds ? "Played under 30 s"
            : "Played " + (minutes > 0 ? minutes + " min " : "") + Mathf.FloorToInt(seconds % 60f) + " s";
    }

    // 50 kr for every runner beaten, and nothing extra for the win (2026-11-19, the user: winning against one player gives
    // +50, not +100). A tie beats nobody (2026-11-19, the user: two
    // players who both score 0 in the route choice game, or both stop at once, should not both get money for a win),
    // so it is the number of runners strictly behind that counts, not the place.
    public static int ForBeaten(int beaten, int runners, bool finished)
    {
        if (!finished) return DidNotFinish;
        beaten = Mathf.Clamp(beaten, 0, Mathf.Max(0, runners - 1));
        return PerRunnerBeaten * beaten;
    }

    // The runners strictly behind me in a race: slower, or not at the finish
    public static int Beaten(Standing me, List<Standing> standings)
    {
        if (me == null || !me.finished || standings == null) return 0;
        int beaten = 0;
        foreach (Standing s in standings)
        {
            if (s == me) continue;
            if (!s.finished || s.seconds > me.seconds) beaten++;
        }
        return beaten;
    }

    // The imposter's top places that pay nothing: the top third (1 of 3, 2 of 6, 3 of 8 or 9)
    public static int ImposterFreePlaces(int runners) => Mathf.CeilToInt(runners / 3f);

    // The imposter wants to finish last: nothing in the top third, then 50, 140, 260, 400, 560 ... kr for every place
    // further back (9 runners: 4th 50 kr, last 740 kr)
    public static int ForImposterPlace(int place, int runners, bool finished)
    {
        if (!finished) return 0;
        int back = place - ImposterFreePlaces(runners);
        if (back <= 0) return 0;
        return Mathf.RoundToInt(ImposterPlaceStep * Mathf.Pow(back, 1.5f) / 10f) * 10;
    }

    // Last and not found: a boost that grows with the number of runners
    public static int ForImposterLastUnseen(int place, int runners, bool finished, bool caught)
    {
        if (!finished || caught || runners < 3 || place != runners) return 0;
        return PerRunnerBeaten * (runners - 1);
    }

    public static int ForImposterEscape(int otherRunners)
    {
        return otherRunners <= 0 ? 0 : ImposterEscapeBase + ImposterEscapePerRunner * otherRunners;
    }
}
