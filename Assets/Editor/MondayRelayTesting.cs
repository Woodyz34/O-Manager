using UnityEditor;
using UnityEngine;

// Testing the Monday relay (MondayRelay) without waiting for Monday 21:00: the game's clock for the relay is moved so
// the lobby is open and the relay starts in a few minutes. Editor clones (ParrelSync) on this computer get the same.
public static class MondayRelayTesting
{
    [MenuItem("Tools/O-Manager/Testing/Monday relay: start in 4 minutes (lobby open now)", false, 70)]
    public static void StartSoon()
    {
        MondayRelay.TestStartIn(4);
        Debug.Log("Monday relay: the lobby is open now and the relay starts in 4 minutes (test clock). Join it from the main menu.");
    }

    [MenuItem("Tools/O-Manager/Testing/Monday relay: start in 8 minutes (lobby opens in 3)", false, 71)]
    public static void StartLater()
    {
        MondayRelay.TestStartIn(8);
        Debug.Log("Monday relay: the lobby opens in 3 minutes and the relay starts in 8 minutes (test clock).");
    }

    [MenuItem("Tools/O-Manager/Testing/Monday relay: real clock again", false, 72)]
    public static void RealClock()
    {
        MondayRelay.TestReset();
        Debug.Log("Monday relay: back on the real clock (Monday 21:00 Swedish time).");
    }
}
