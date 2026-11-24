using System.Collections.Generic;
using Fusion;
using UnityEngine;

// A lobby that is out on a race can be joined (2026-11-24, Anders): the new player watches the race (Spectator) and
// races with the others from the next race on. Before, a racing lobby was closed (SessionInfo.IsOpen = false) and the
// lobby list said "racing" with no Join button. Now the lobby stays open, and a session property "racing" (1 while a
// race is on, 0 in the lobby) lets the lobby list say "racing" and the Join button say "Watch".
public static class SessionRacing
{
    public const string Key = "racing";

    // Given when the lobby is created, so the property is one the lobby list can see
    public static Dictionary<string, SessionProperty> NewLobbyProperties()
    {
        return new Dictionary<string, SessionProperty> { { Key, 0 } };
    }

    // The host: the race starts (true) or the lobby is back (false). Never closes the lobby any more.
    public static void Set(NetworkRunner runner, bool racing)
    {
        if (runner == null || runner.SessionInfo == null || !runner.SessionInfo.IsValid) return;
        runner.SessionInfo.IsOpen = true;
        try
        {
            runner.SessionInfo.UpdateCustomProperties(new Dictionary<string, SessionProperty> { { Key, racing ? 1 : 0 } });
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("SessionRacing: could not set the racing flag: " + e.Message);
        }
    }

    // A lobby in the list that is out racing: the new flag, or closed the old way (an older version of the game)
    public static bool IsRacing(SessionInfo info)
    {
        if (info == null || !info.IsValid) return false;
        if (!info.IsOpen) return true;
        var props = info.Properties;
        if (props == null || !props.TryGetValue(Key, out SessionProperty value)) return false;
        return value.IsInt && (int)value == 1;
    }

    // Only a lobby closed the old way can not be joined
    public static bool CanJoin(SessionInfo info) => info != null && info.IsValid && info.IsOpen;
}
