using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// What happens when a network game ends without the player asking for it: the internet went down, the game
// servers dropped the connection, or a lobby could not be created or joined (full, already racing, gone).
// Without this the player was left on a page where nothing worked any more (a frozen competition, or the
// "Creating lobby" page forever). Now the player is taken to a page that works and told why.
public static class SessionGuard
{
    // True while the game is leaving a session on purpose (SceneLoader.LeaveSession), so that is not seen as lost
    public static bool Leaving { get; set; }

    static bool handling;
    static bool quitting;
    static string pendingMessage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        Leaving = false;
        handling = false;
        quitting = false;
        pendingMessage = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
    }

    static void OnQuitting()
    {
        quitting = true;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        handling = false;
        if (pendingMessage != null)
        {
            // The message is shown on the new page (a message made before the page change would go away with the old page)
            string message = pendingMessage;
            pendingMessage = null;
            UIToast.Show(message, 9f);
        }
    }

    // Called by the Spawner when a runner has shut down, and by NetworkRunnerHandler when a game could not start
    public static void OnShutdown(NetworkRunner runner, ShutdownReason reason)
    {
        if (Leaving || handling || quitting || reason == ShutdownReason.Ok || reason == ShutdownReason.HostMigration)
        {
            return;
        }
        string scene = SceneManager.GetActiveScene().name;
        // A single-player game has no connection to lose (its own page says so when it can not start)
        if (scene == "SingelPlayer" || IsSingle(runner))
        {
            return;
        }
        handling = true;

        bool connecting = scene == "MultiPlayer";
        Debug.LogWarning("SessionGuard: the network game ended (" + reason + ") in " + scene);
        pendingMessage = Describe(reason, connecting);
        // Creating or joining failed: the lobby list again, so the player can try once more.
        // In a lobby or a competition: the main menu.
        SceneManager.LoadScene(connecting ? "MultiPlayer" : "MainMenu");
    }

    static bool IsSingle(NetworkRunner runner)
    {
        try
        {
            return runner != null && runner.GameMode == GameMode.Single;
        }
        catch
        {
            return false;   // a runner that never got started
        }
    }

    static string Describe(ShutdownReason reason, bool connecting)
    {
        switch (reason)
        {
            case ShutdownReason.GameIsFull:
                return "That lobby is full.";
            case ShutdownReason.GameClosed:
                return "That lobby is in a competition right now. Try again when they are back in the lobby.";
            case ShutdownReason.GameNotFound:
                return "That lobby is not there any more.";
            case ShutdownReason.MaxCcuReached:
                return "The game servers are full right now. Try again in a little while.";
            case ShutdownReason.IncompatibleConfiguration:
                return "That lobby runs another version of the game.";
        }
        return connecting
            ? "Could not connect (" + reason + "). Check the internet connection and try again."
            : "The connection to the game was lost (" + reason + "). Check the internet connection.";
    }
}
