using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Every player sees every runner at the start of each race (2026-11-15).
//
// The players' runner objects live on from the lobby into each race. Views that hide them for a while (a visit to the
// shop from the lobby, the replay, the route comparison) switch their dots and name tags off and on again; when one of
// them was cut short (the host started the race or went back to the lobby, the host changed) a player could be left
// without seeing some or all of the runners for the rest of the session. When a new scene starts, the dots and name
// tags of every player's runner are switched on again here; what each view wants hidden it hides again itself.
// Parts that are off in the prefab itself (debug pictures) are remembered when the runner appears and left off.
public static class RunnerVisibility
{
    static readonly Dictionary<NetworkPlayer, HashSet<Component>> offFromStart = new Dictionary<NetworkPlayer, HashSet<Component>>();

    public static void Remember(NetworkPlayer player)
    {
        if (player == null || offFromStart.ContainsKey(player)) return;
        var off = new HashSet<Component>();
        foreach (SpriteRenderer r in player.GetComponentsInChildren<SpriteRenderer>(true)) if (!r.enabled) off.Add(r);
        foreach (Canvas c in player.GetComponentsInChildren<Canvas>(true)) if (!c.enabled) off.Add(c);
        offFromStart[player] = off;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit hides the runners)
        if (mode == LoadSceneMode.Additive) return;
        ShowAll();
    }

    public static void ShowAll()
    {
        int shown = 0;
        // Runners that have left
        var gone = new List<NetworkPlayer>();
        foreach (NetworkPlayer key in offFromStart.Keys) if (key == null) gone.Add(key);
        foreach (NetworkPlayer key in gone) offFromStart.Remove(key);
        foreach (NetworkPlayer player in Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player == null || !offFromStart.TryGetValue(player, out HashSet<Component> keepOff)) continue;
            // Only the dots and the name tags: the drawn line (LineRenderer) is only on for your own runner
            foreach (SpriteRenderer r in player.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (r.enabled || keepOff.Contains(r)) continue;
                r.enabled = true;
                shown++;
            }
            foreach (Canvas c in player.GetComponentsInChildren<Canvas>(true))
            {
                if (c.enabled || keepOff.Contains(c)) continue;
                c.enabled = true;
                shown++;
            }
        }
        if (shown > 0) Debug.Log("RunnerVisibility: " + shown + " hidden runner parts shown again");
    }
}
