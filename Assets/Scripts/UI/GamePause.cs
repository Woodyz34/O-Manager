using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// The game stands still while the Esc menu (or the 3D settings opened from it) is open, so you can stop, do
// something else and come back without losing time (2026-11-24, the user). Every game mode: a normal race, the route
// choice game and Run it yourself (3D), also in the weekly and daily contests.
// Only when playing alone (single player and contests): online the others go on racing in the same race, so a
// multiplayer game is never paused.
// The race runs on Time.deltaTime (runners, clocks, the 3D runner), so Time.timeScale 0 stops all of it. The race's
// pace (RacePace, TvDirector) comes back when the menu closes. The time spent paused does not count as played
// time for the money (PlayClock).
public static class GamePause
{
    public static bool Active { get; private set; }

    static float pausedTotal;   // real seconds spent paused, all together
    static float pausedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Active = false;
        pausedTotal = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A new page never starts paused (RacePace sets the normal pace on the same call)
        if (mode == LoadSceneMode.Single && Active) Resume();
    }

    // Real time without the paused time: what the played time for the money is measured with
    public static float PlayClock => Time.unscaledTime - pausedTotal - (Active ? Time.unscaledTime - pausedAt : 0f);

    // Every frame, from the Esc menu
    public static void Check(bool menuOpen)
    {
        bool want = menuOpen && CanPause();
        if (want && !Active) Pause();
        else if (!want && Active) Resume();
        // RacePace and the TV production set the pace every frame; while paused they leave it (see there), and
        // anything else that set it is put back to a stop
        if (Active && Time.timeScale != 0f) Time.timeScale = 0f;
    }

    static void Pause()
    {
        Active = true;
        pausedAt = Time.unscaledTime;
        Time.timeScale = 0f;
    }

    // The normal pace; the race's own pace (RacePace, TvDirector) is set again on the next frame
    static void Resume()
    {
        pausedTotal += Time.unscaledTime - pausedAt;
        Active = false;
        Time.timeScale = 1f;
    }

    // Alone in a race: a single-player network session (the contests start one too) and not on a menu page
    static bool CanPause()
    {
        if (NetworkPlayer.Local == null || NetworkPlayer.Local.Runner == null) return false;
        NetworkRunner runner = NetworkPlayer.Local.Runner;
        if (!runner.IsRunning || runner.GameMode != GameMode.Single) return false;
        string scene = ActiveScene.Name;
        return scene != "MainMenu" && scene != "MultiPlayer" && scene != "SingelPlayer" && scene != "NewCompetition"
               && scene != "Lobby" && scene != CourseScenes.Builder;
    }
}
