using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Secret task mode: every player except one gets the same secret task for the race, like "overrun every control".
// The one without a task is the imposter: they are only told that the others have a task, and have to work out
// what it is by watching the others and blend in. At the end everyone guesses who the imposter was (the same vote
// and reveal as in the Imposter mode).
// The host picks the task when the game starts and shares it on its own player (NetworkPlayer.secretTask) as one
// number: task + 100 * control (the control is only used by tasks about one leg).
public static class SecretTasks
{
    public enum Task
    {
        None = 0,
        BadLeg = 1,
        OverrunControls = 2,
        StrangeRoutes = 3,
        KeepLeft = 4,
        KeepRight = 5,
        PathsOnly = 6,
        StraightLines = 7,
        FromBehind = 8,
    }

    // The tasks the host draws from. Add or remove tasks here.
    static readonly Task[] Pool =
    {
        Task.BadLeg, Task.OverrunControls, Task.StrangeRoutes, Task.KeepLeft, Task.KeepRight,
        Task.PathsOnly, Task.StraightLines, Task.FromBehind,
    };

    // Called by the host when the game starts. Returns the shared number (never 0).
    public static int Pick(CourseData course)
    {
        int controls = course != null && course.controls != null ? course.controls.Count : 0;
        List<Task> pool = new List<Task>(Pool);
        // A task about one leg needs a few controls, so the slow leg is not the first one
        if (controls < 3) pool.Remove(Task.BadLeg);
        Task task = pool[Random.Range(0, pool.Count)];
        int control = task == Task.BadLeg ? UnityEngine.Random.Range(2, controls + 1) : 0;
        return (int)task + 100 * control;
    }

    public static Task TaskOf(int code) => (Task)(code % 100);
    public static int ControlOf(int code) => code / 100;

    // The task as the players with a task read it
    public static string Text(int code)
    {
        int control = ControlOf(code);
        switch (TaskOf(code))
        {
            case Task.BadLeg: return "Run well all the way, except on the leg to control " + control + ": lose a lot of time there.";
            case Task.OverrunControls: return "Miss every control: run a bit past it, then turn back to it.";
            case Task.StrangeRoutes: return "Make really strange route choices to every control.";
            case Task.KeepLeft: return "Always pass on the left side of the straight line to the next control.";
            case Task.KeepRight: return "Always pass on the right side of the straight line to the next control.";
            case Task.PathsOnly: return "Stay on paths and roads whenever you can. Never cut through the terrain.";
            case Task.StraightLines: return "Run as straight as you can at every control, even through rough ground.";
            case Task.FromBehind: return "Come into every control from behind, from the side facing away from you.";
            default: return "";
        }
    }

    // Short form for the small reminder during the race
    public static string Short(int code)
    {
        int control = ControlOf(code);
        switch (TaskOf(code))
        {
            case Task.BadLeg: return "Lose time on the leg to control " + control;
            case Task.OverrunControls: return "Overrun every control";
            case Task.StrangeRoutes: return "Strange route choices";
            case Task.KeepLeft: return "Keep left of the straight line";
            case Task.KeepRight: return "Keep right of the straight line";
            case Task.PathsOnly: return "Paths and roads only";
            case Task.StraightLines: return "Straight at every control";
            case Task.FromBehind: return "Into every control from behind";
            default: return "";
        }
    }

    // ---- In the competition ----

    static int remembered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        remembered = 0;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        remembered = 0;
    }

    // True in a secret task competition
    public static bool On => ExtraModes.Is(ExtraMode.SecretTask);

    // The task of this competition, once the host's choice has arrived. Remembered, so it can still be shown
    // at the reveal if the host has left.
    public static bool TryGet(out int code)
    {
        foreach (NetworkPlayer p in UnityEngine.Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (p.Object == null || !p.Object.IsValid) continue;
            if (p.secretTask != 0)
            {
                remembered = p.secretTask;
                break;
            }
        }
        code = remembered;
        return code != 0 && SceneManager.GetActiveScene().name != "Lobby";
    }
}
