using UnityEngine;
using UnityEngine.SceneManagement;

// How fast the race runs on the map (2026-11-19): the host's "Race speed" (HostSettings), changed in the lobby or
// during the race. The whole game runs faster or slower through Time.timeScale, so the runners, the clocks and the
// mistakes all keep in step. A relay with TV controls sets the pace itself (TvDirector), with the same setting
// while nothing is on TV. While a route is drawn the pace is normal, so the drawing time costs the same as before.
public static class RacePace
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A new page (lobby, main menu, a new race) always starts at the normal pace
        if (mode == LoadSceneMode.Single) Time.timeScale = 1f;
    }

    // The pace on the map while nothing is on TV
    public static float MapPace(AgentMovement agent)
    {
        if (ActiveScene.Name == "Lobby" || CompetitionFlow.ResultsShown) return 1f;
        if (agent == null || agent.manualPathDrawer == null || agent.manualPathDrawer.timeToDraw) return 1f;
        return HostSettings.RaceSpeed;
    }

    // Every frame, from the local player
    public static void Apply(AgentMovement agent)
    {
        if (TvDirector.Active != null) return;   // the TV production sets the pace
        if (GamePause.Active) return;            // the Esc menu has stopped the game; its pace comes back after
        float want = MapPace(agent);
        if (!Mathf.Approximately(Time.timeScale, want)) Time.timeScale = want;
    }

    public static void Normal()
    {
        Time.timeScale = 1f;
    }
}
