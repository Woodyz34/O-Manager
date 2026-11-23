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
        // 6 (paths and roads only) was taken out, the user did not want it
        StraightLines = 7,
        FromBehind = 8,
        Zigzag = 9,
        SecretPlace = 10,
    }

    // The tasks the host draws from. Add or remove tasks here.
    public static readonly Task[] Pool =
    {
        Task.BadLeg, Task.OverrunControls, Task.StrangeRoutes, Task.KeepLeft, Task.KeepRight,
        Task.StraightLines, Task.FromBehind, Task.Zigzag, Task.SecretPlace,
    };

    // Called by the host when the game starts. Returns the shared number (never 0).
    public static int Pick(CourseData course, int tasksOff = 0)
    {
        int controls = course != null && course.controls != null ? course.controls.Count : 0;
        List<Task> pool = Available(course, tasksOff);
        Task task = pool[Random.Range(0, pool.Count)];
        // BadLeg: the control at the end of the slow leg. SecretPlace: a seed, so every player works out the same place.
        int param = task == Task.BadLeg ? UnityEngine.Random.Range(2, controls + 1)
            : task == Task.SecretPlace ? UnityEngine.Random.Range(1, 10000) : 0;
        return (int)task + 100 * param;
    }

    // The tasks the host's filter leaves in, for this course
    public static List<Task> Available(CourseData course, int tasksOff)
    {
        int controls = course != null && course.controls != null ? course.controls.Count : 0;
        List<Task> pool = new List<Task>();
        foreach (Task t in Pool)
        {
            if ((tasksOff & Bit(t)) != 0) continue;
            // A task about one leg needs a few controls, so the slow leg is not the first one
            if (t == Task.BadLeg && controls < 3) continue;
            pool.Add(t);
        }
        return pool;
    }

    public static int Bit(Task t) => 1 << (int)t;

    // Why the game cannot start with this filter, or null
    public static string StartProblem(CourseData course, int tasksOff)
    {
        if (Available(course, tasksOff).Count > 0) return null;
        return "Secret task has no task left to give. Open Game mode > Secret task > Tasks and turn at least one on.";
    }

    // The host's filter, remembered for the next race in the same session; published to the others by LobbySettingsUI
    public static int HostOff;

    // Name and one-line explanation of a task, for the filter list
    public static string Label(Task t)
    {
        switch (t)
        {
            case Task.BadLeg: return "Lose time on one leg";
            case Task.OverrunControls: return "Overrun every control";
            case Task.StrangeRoutes: return "Strange route choices";
            case Task.KeepLeft: return "Keep left of the straight line";
            case Task.KeepRight: return "Keep right of the straight line";
            case Task.StraightLines: return "Straight at every control";
            case Task.FromBehind: return "Into every control from behind";
            case Task.Zigzag: return "Zigzag on every leg";
            case Task.SecretPlace: return "Run through a secret place";
            default: return t.ToString();
        }
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
            case Task.StraightLines: return "Run as straight as you can at every control, even through rough ground.";
            case Task.FromBehind: return "Come into every control from behind, from the side facing away from you.";
            case Task.Zigzag: return "Zigzag on every leg.";
            case Task.SecretPlace: return "Run through the secret place (the gold ring on the map) at some point in the race. Only you task-holders can see it.";
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
            case Task.StraightLines: return "Straight at every control";
            case Task.FromBehind: return "Into every control from behind";
            case Task.Zigzag: return "Zigzag on every leg";
            case Task.SecretPlace: return "Run through the gold ring";
            default: return "";
        }
    }

    // ---- In the competition ----

    static int remembered;
    static int rememberedOff;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        remembered = 0;
        rememberedOff = 0;
        HostOff = 0;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        remembered = 0;
        rememberedOff = 0;
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
                // The host's task filter sits on the same player
                rememberedOff = p.lobbyTasksOff;
                break;
            }
        }
        code = remembered;
        return code != 0 && ActiveScene.Name != "Lobby";
    }

    // The tasks the host could have drawn this race: what a caught imposter picks from. The real task is
    // always in it, even if the filter could not be read.
    public static List<Task> Possible(int code)
    {
        List<Task> tasks = Available(CourseLibrary.Current, rememberedOff);
        Task real = TaskOf(code);
        if (real != Task.None && !tasks.Contains(real)) tasks.Add(real);
        tasks.Sort((a, b) => System.Array.IndexOf(Pool, a).CompareTo(System.Array.IndexOf(Pool, b)));
        return tasks;
    }
}

// Secret task "secret place": a gold ring on the map that only the players with the task see. Every player works
// out the same place from the shared seed: on a random leg, beside the straight line, on ground you can run on.
public class SecretPlaceMarker : MonoBehaviour
{
    public const float RingMeters = 18f;

    static SecretPlaceMarker instance;
    int seed;
    GameObject ring;
    TMPro.TextMeshPro label;
    Vector3 center;
    float radius;

    public static void Show(int seed)
    {
        if (instance == null) instance = GameModeRuntime.Host.gameObject.AddComponent<SecretPlaceMarker>();
        instance.seed = seed;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (ring != null) Destroy(ring);
    }

    void Update()
    {
        if (ring == null)
        {
            PathfindingManager manager = FindObjectOfType<PathfindingManager>();
            if (manager == null || manager.speedGrid2D == null || manager.controlPoints == null || manager.controlPoints.Count < 2) return;
            if (!TryPlace(manager, seed, out Vector3 place)) { enabled = false; return; }
            Build(place);
        }
        // The race camera turns with each leg, so the name turns with it and stays upright
        Camera cam = Camera.main;
        if (cam != null && label != null)
        {
            label.transform.rotation = cam.transform.rotation;
            label.transform.position = center - cam.transform.up * radius * 1.6f + Vector3.back * 0.01f;
        }
        if (CompetitionFlow.ResultsShown && ring.activeSelf) ring.SetActive(false);
    }

    static bool Runnable(PathfindingManager manager, Vector3 world)
    {
        Vector2Int cell = manager.WorldToGridPositionCentered(world);
        return manager.speedGrid2D[cell.y, cell.x] > 0.01f;
    }

    static bool TryPlace(PathfindingManager manager, int seed, out Vector3 place)
    {
        float u = MapDefinition.WorldUnitsPerMeter;
        System.Random random = new System.Random(seed);
        List<Transform> points = manager.controlPoints;
        // A few tries, in case a leg has no runnable ground beside it
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int leg = random.Next(0, points.Count - 1);
            if (points[leg] == null || points[leg + 1] == null) continue;
            Vector2 a = points[leg].position;
            Vector2 b = points[leg + 1].position;
            float length = Vector2.Distance(a, b);
            if (length < 60f * u) continue;
            Vector2 along = (b - a) / length;
            Vector2 side = new Vector2(-along.y, along.x) * (random.Next(2) == 0 ? -1f : 1f);
            float t = 0.35f + 0.3f * (float)random.NextDouble();
            float off = Mathf.Clamp(length * (0.2f + 0.2f * (float)random.NextDouble()), 30f * u, 150f * u);
            Vector2 wanted = a + (b - a) * t + side * off;
            // The nearest runnable spot, looking round in growing circles
            for (float r = 0f; r <= 40f * u; r += 4f * u)
            {
                int steps = r <= 0f ? 1 : 16;
                for (int k = 0; k < steps; k++)
                {
                    float angle = k * Mathf.PI * 2f / steps;
                    Vector3 test = new Vector3(wanted.x + Mathf.Cos(angle) * r, wanted.y + Mathf.Sin(angle) * r, -1.35f);
                    if (Runnable(manager, test))
                    {
                        place = test;
                        return true;
                    }
                }
            }
        }
        place = Vector3.zero;
        return false;
    }

    void Build(Vector3 place)
    {
        float u = MapDefinition.WorldUnitsPerMeter;
        center = place;
        radius = RingMeters * u;
        List<Vector3> circle = new List<Vector3>();
        for (int i = 0; i <= 40; i++)
        {
            float angle = i * Mathf.PI * 2f / 40f;
            circle.Add(new Vector3(place.x + Mathf.Cos(angle) * radius, place.y + Mathf.Sin(angle) * radius, place.z));
        }
        Material material = new Material(Shader.Find("Sprites/Default"));
        ring = FlatLine.Make("Secret place", circle, new Color(1f, 0.78f, 0.1f, 0.95f), 4f * u, 29, material, place.z);

        label = new GameObject("Secret place name").AddComponent<TMPro.TextMeshPro>();
        label.transform.SetParent(ring.transform, false);
        label.transform.position = new Vector3(place.x, place.y, place.z - 0.01f);
        label.rectTransform.sizeDelta = new Vector2(radius * 8f, radius);
        label.text = "Secret place";
        label.fontSize = radius * 5f;
        label.fontStyle = TMPro.FontStyles.Bold;
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.color = new Color(0.55f, 0.38f, 0f);
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(255, 255, 255, 220);
        label.sortingOrder = 30;
    }
}
