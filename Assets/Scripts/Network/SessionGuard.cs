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
        QuietKickLog.Install();
#if UNITY_EDITOR
        // Several editors (clones) on one computer share the graphics card; capping the frame rate keeps the card from timing out
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
#endif
    }

    // Photon logs "DisconnectMessage Code: 104 Kick Player" as a red error when the host leaves the lobby.
    // That is an expected end of a session (the game already handles it), so it is shown as a warning instead.
    class QuietKickLog : ILogHandler
    {
        static QuietKickLog instance;
        readonly ILogHandler inner;

        QuietKickLog(ILogHandler inner) { this.inner = inner; }

        public static void Install()
        {
            if (instance != null && Debug.unityLogger.logHandler == instance) return;
            instance = new QuietKickLog(Debug.unityLogger.logHandler);
            Debug.unityLogger.logHandler = instance;
        }

        public void LogFormat(UnityEngine.LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (logType == UnityEngine.LogType.Error && format != null && format.Contains("DisconnectMessage") && args != null && args.Length > 0
                && (args[0] as string ?? "").Contains("Code: 104"))
                logType = UnityEngine.LogType.Warning;
            inner.LogFormat(logType, context, format, args);
        }

        public void LogException(System.Exception exception, UnityEngine.Object context) { inner.LogException(exception, context); }
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

    // The text when Photon has no room for more players (its CCU limit). Photon never throws anyone out to make room:
    // the players already in keep playing, and a new one is told to come back later.
    public const string MultiplayerFull =
        "Sorry, multiplayer is full right now. Try again a little later. Until then you can play single player " +
        "or do something else in the game.";

    // Leaves the multiplayer pages and shows the message on the page before them
    public static void MultiplayerIsFull()
    {
        if (handling) return;
        handling = true;
        pendingMessage = MultiplayerFull;
        SceneManager.LoadScene("NewCompetition");
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
                return MultiplayerFull;
            case ShutdownReason.IncompatibleConfiguration:
                return "That lobby runs another version of the game.";
        }
        return connecting
            ? "Could not connect (" + reason + "). Check the internet connection and try again."
            : "The connection to the game was lost (" + reason + "). Check the internet connection.";
    }
}
