using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;

// Bears game mode. While drawing, every player may hide one bear somewhere on the map (GameModeTools). The bear is
// only seen by its owner until a runner comes close to it: then everyone sees a bear jump up and roar (ModeBanner),
// and from then on the bear stands on the map for all. A runner who comes close to someone else's bear gets scared:
// stands still for a moment and then runs a detour round the bear back to their own line. That costs a little time,
// which is all a bear does. Your own runner is not afraid of your own bear.
//
// Each player's runner checks the bears itself (BearWatcher); the bears' places are shared on the owners' NetworkPlayers.
public static class BearMode
{
    public const float FindMeters = 45f;            // a runner this close finds the bear
    public const float AvoidMeters = 70f;           // the detour keeps about this far away from it
    public const float MinFromControlMeters = 90f;  // bears are not hidden on top of the controls
    public const float ScareSeconds = 1.0f;         // how long a scared runner stands still (real seconds)
    public const float SizeMeters = 70f;            // how big the bear is drawn on the map

    static float U => MapDefinition.WorldUnitsPerMeter;

    public static bool On => ExtraModes.Is(ExtraMode.Bears) && RelayRace.Active == null;

    static readonly HashSet<PlayerRef> found = new HashSet<PlayerRef>();

    public static void Reset()
    {
        found.Clear();
    }

    public static bool IsFound(PlayerRef owner) => found.Contains(owner);

    // Why a bear cannot be hidden here, or null when it can
    public static string PlaceProblem(Vector3 world, PathfindingManager manager)
    {
        if (manager == null || manager.speedGrid2D == null) return "The map is not ready yet.";
        int rows = manager.speedGrid2D.GetLength(0);
        int cols = manager.speedGrid2D.GetLength(1);
        float halfWidth = cols * manager.cellSize / 2f;
        float halfHeight = rows * manager.cellSize / 2f;
        if (world.x < -halfWidth || world.x > halfWidth || world.y < -halfHeight || world.y > halfHeight)
        {
            return "The bear has to be on the map.";
        }
        foreach (Transform point in manager.controlPoints)
        {
            if (point != null && Vector2.Distance(point.position, world) < MinFromControlMeters * U)
            {
                return "Too close to a control. Hide the bear at least " + Mathf.RoundToInt(MinFromControlMeters) + " m from the controls.";
            }
        }
        return null;
    }

    public static void Place(NetworkPlayer local, Vector3 world)
    {
        local.bearPosition = new Vector2(world.x, world.y);
        local.bearPlaced = true;
    }

    // Received by everyone when a runner got scared by a bear
    public static void OnScare(PlayerRef owner, Vector2 where, NetworkPlayer scared)
    {
        bool first = found.Add(owner);
        NetworkPlayer ownerPlayer = CompetitionFlow.FindPlayer(owner);
        bool ownerIsMe = ownerPlayer != null && ownerPlayer.Object != null && ownerPlayer.Object.HasStateAuthority;
        bool scaredIsMe = scared != null && scared.Object != null && scared.Object.HasStateAuthority;
        string ownerName = ownerIsMe ? "your" : (ownerPlayer != null ? Name(ownerPlayer) : "someone") + "'s";
        string runnerName = scaredIsMe ? "you" : scared != null ? Name(scared) : "a runner";

        if (BearWatcher.Instance != null)
        {
            BearWatcher.Instance.PlayRoar(owner, where, first);
        }
        if (first)
        {
            ModeBanner.Show(ModeBanner.BearSprite, "RAAAWR!", Capital(ownerName) + " bear scared " + runnerName + "!",
                new Color(0.55f, 0.29f, 0.12f), 3.4f, true, ModeBanner.LoadSound("bear_roar"));
        }
        else
        {
            ModeBanner.Show(ModeBanner.BearSprite, "Bear!", Capital(runnerName) + " ran into " + ownerName + " bear too.",
                new Color(0.55f, 0.29f, 0.12f), 2.2f, false, ModeBanner.LoadSound("bear_roar"));
        }
    }

    static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    public static string Name(NetworkPlayer player)
    {
        string nick = player.nickName.ToString();
        return string.IsNullOrWhiteSpace(nick) ? "Player " + player.Object.StateAuthority.PlayerId : nick;
    }

    // The way round the bear: A* from where the runner stands to the first point of its own line that is well clear of
    // the bear (or the control), with the ground near the bear made very slow so the search keeps away from it.
    // Then on along the rest of the line. Only reads the grids, so it runs on a background thread.
    public static List<Vector2Int> Detour(float[,] speed, Dictionary<string, float[,]> slopes, List<Vector2Int> path, int from,
                                          Vector2Int start, Vector2Int bearCell, float avoidCells, Vector2Int controlCell)
    {
        int rows = speed.GetLength(0);
        int cols = speed.GetLength(1);

        int rejoin = -1;
        if (path != null && path.Count > 0)
        {
            from = Mathf.Clamp(from, 0, path.Count - 1);
            int closest = from;
            float best = float.MaxValue;
            for (int i = from; i < path.Count; i++)
            {
                float d = Vector2Int.Distance(path[i], bearCell);
                if (d < best)
                {
                    best = d;
                    closest = i;
                }
            }
            for (int i = closest; i < path.Count; i++)
            {
                if (Vector2Int.Distance(path[i], bearCell) > avoidCells + 1.5f)
                {
                    rejoin = i;
                    break;
                }
            }
        }
        Vector2Int goal = rejoin >= 0 ? path[rejoin] : controlCell;

        float[,] grid = (float[,])speed.Clone();
        int r = Mathf.CeilToInt(avoidCells);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int x = bearCell.x + dx;
                int y = bearCell.y + dy;
                if (x < 0 || y < 0 || x >= cols || y >= rows) continue;
                if (dx * dx + dy * dy > avoidCells * avoidCells) continue;
                if (grid[y, x] > 0.01f)
                {
                    // Still passable (the runner may stand in it), just very slow
                    grid[y, x] = Mathf.Max(grid[y, x] * 0.04f, 0.0105f);
                }
            }
        }
        AStarPathfinder astar = new AStarPathfinder(grid, slopes);
        List<List<Vector2Int>> paths = astar.GetAllPaths(start, goal, 1);
        if (paths == null || paths.Count == 0 || paths[0] == null || paths[0].Count == 0) return null;
        List<Vector2Int> result = new List<Vector2Int>(paths[0]);
        if (rejoin >= 0 && rejoin + 1 < path.Count)
        {
            result.AddRange(path.GetRange(rejoin + 1, path.Count - rejoin - 1));
        }
        return result;
    }
}

// Lives in the competition scene while bears are on: shows the bears on the map and makes the local runner afraid of them
public class BearWatcher : MonoBehaviour
{
    public static BearWatcher Instance { get; private set; }

    class View
    {
        public GameObject root;
        public SpriteRenderer sprite;
        public TextMeshPro label;
        public bool popped;
    }

    readonly Dictionary<PlayerRef, View> views = new Dictionary<PlayerRef, View>();
    readonly HashSet<PlayerRef> scaredBy = new HashSet<PlayerRef>();
    float nextSync;
    bool scaring;

    static float U => MapDefinition.WorldUnitsPerMeter;

    void Awake()
    {
        Instance = this;
        BearMode.Reset();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (View view in views.Values)
        {
            if (view.root != null) Destroy(view.root);
        }
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.agentMovement != null) local.agentMovement.modeHold = false;
    }

    void Update()
    {
        if (!BearMode.On) return;
        if (Time.unscaledTime >= nextSync)
        {
            nextSync = Time.unscaledTime + 0.2f;
            SyncViews();
        }
        CheckLocalRunner();
        // The bears that are seen breathe a little
        foreach (View view in views.Values)
        {
            if (view.root != null && view.root.activeSelf && view.popped)
            {
                float breath = 1f + 0.03f * Mathf.Sin(Time.time * 2.4f);
                view.sprite.transform.localScale = new Vector3(1f / breath, breath, 1f);
            }
        }
    }

    // Your own bear is shown faded; someone else's only once it has been found
    void SyncViews()
    {
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid || !player.bearPlaced) continue;
            PlayerRef owner = player.Object.StateAuthority;
            bool mine = player.Object.HasStateAuthority;
            bool found = BearMode.IsFound(owner);
            if (!mine && !found) continue;
            View view = GetView(owner);
            Vector2 at = player.bearPosition;
            view.root.transform.position = new Vector3(at.x, at.y, -1.4f);
            if (!view.root.activeSelf) view.root.SetActive(true);
            Color color = Color.white;
            color.a = found ? 1f : 0.55f;
            if (!view.popped) view.sprite.color = color;
            view.label.text = mine ? (found ? "Your bear" : "Your bear (hidden)") : BearMode.Name(player) + "'s bear";
        }
    }

    View GetView(PlayerRef owner)
    {
        if (views.TryGetValue(owner, out View view) && view.root != null) return view;
        view = new View();
        view.root = new GameObject("Bear " + owner.PlayerId);
        GameObject art = new GameObject("Picture");
        art.transform.SetParent(view.root.transform, false);
        view.sprite = art.AddComponent<SpriteRenderer>();
        view.sprite.sprite = ModeBanner.BearSprite;
        view.sprite.sortingOrder = 30;
        float size = BearMode.SizeMeters * U;
        view.root.transform.localScale = Vector3.one * size;

        view.label = new GameObject("Name").AddComponent<TextMeshPro>();
        view.label.transform.SetParent(view.root.transform, false);
        view.label.transform.localPosition = new Vector3(0f, -0.62f, 0f);
        view.label.rectTransform.sizeDelta = new Vector2(4f, 0.4f);
        view.label.fontSize = 3.6f;
        view.label.fontStyle = FontStyles.Bold;
        view.label.alignment = TextAlignmentOptions.Center;
        view.label.enableWordWrapping = false;
        view.label.color = new Color(0.36f, 0.2f, 0.08f);
        view.label.outlineWidth = 0.2f;
        view.label.outlineColor = new Color32(255, 255, 255, 200);
        view.label.sortingOrder = 31;
        views[owner] = view;
        return view;
    }

    // Everyone: the bear jumps up and roars where it stands
    public void PlayRoar(PlayerRef owner, Vector2 where, bool first)
    {
        View view = GetView(owner);
        view.root.transform.position = new Vector3(where.x, where.y, -1.4f);
        view.root.SetActive(true);
        StartCoroutine(Pop(view, first));
    }

    IEnumerator Pop(View view, bool first)
    {
        view.popped = false;
        Transform art = view.sprite.transform;
        view.sprite.color = Color.white;
        TextMeshPro roar = new GameObject("Roar").AddComponent<TextMeshPro>();
        roar.transform.SetParent(view.root.transform, false);
        roar.transform.localPosition = new Vector3(0.35f, 0.75f, 0f);
        roar.rectTransform.sizeDelta = new Vector2(4f, 1f);
        roar.text = first ? "RAAAWR!" : "RAWR!";
        roar.fontSize = 6f;
        roar.fontStyle = FontStyles.Bold;
        roar.alignment = TextAlignmentOptions.Center;
        roar.enableWordWrapping = false;
        roar.color = new Color(0.86f, 0.2f, 0.1f);
        roar.outlineWidth = 0.25f;
        roar.outlineColor = new Color32(255, 255, 255, 255);
        roar.sortingOrder = 32;

        float t = 0f;
        const float duration = 1.6f;
        while (t < duration && art != null)
        {
            t += Time.deltaTime;
            // Pops up out of the ground, overshoots, then shakes as it roars
            float grow = Mathf.Clamp01(t / 0.35f);
            float scale = grow < 1f ? Mathf.Sin(grow * Mathf.PI * 0.75f) * 1.35f : 1f + 0.08f * Mathf.Sin(t * 30f) * Mathf.Clamp01((duration - t) / 1f);
            art.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            art.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 22f) * 10f * Mathf.Clamp01((duration - t) / 1.2f));
            roar.transform.localScale = Vector3.one * (0.6f + 0.5f * Mathf.Clamp01(t / 0.25f) + 0.1f * Mathf.Sin(t * 25f));
            Color c = roar.color;
            c.a = Mathf.Clamp01((duration - t) / 0.4f);
            roar.color = c;
            yield return null;
        }
        if (art != null)
        {
            art.localScale = Vector3.one;
            art.localRotation = Quaternion.identity;
        }
        if (roar != null) Destroy(roar.gameObject);
        view.popped = true;
    }

    // ---- The local runner ----

    void CheckLocalRunner()
    {
        if (scaring) return;
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.Object == null || !local.Object.HasStateAuthority) return;
        AgentMovement agent = local.agentMovement;
        if (agent == null || agent.pathfindingManager == null || agent.manualPathDrawer == null) return;
        // Only while actually running a stretch, and not in the middle of a mistake
        if (!agent.canRunNextSegment || agent.hasFinished || agent.WaitingForAllPlayersToDraw || agent.WaitingForAllPlayersToReachKontroll
            || agent.manualPathDrawer.timeToDraw || agent.manualPathDrawer.isDrawing || agent.IsMissing || agent.modeHold
            || agent.path == null || agent.path.Count == 0 || CompetitionFlow.ResultsShown)
        {
            return;
        }
        Vector3 me = agent.transform.position;
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player == local || player.Object == null || !player.Object.IsValid || !player.bearPlaced) continue;
            PlayerRef owner = player.Object.StateAuthority;
            if (scaredBy.Contains(owner)) continue;
            Vector2 bear = player.bearPosition;
            if (Vector2.Distance(me, bear) <= BearMode.FindMeters * U)
            {
                StartCoroutine(Scare(local, agent, owner, bear));
                return;
            }
        }
    }

    IEnumerator Scare(NetworkPlayer local, AgentMovement agent, PlayerRef owner, Vector2 bear)
    {
        scaring = true;
        scaredBy.Add(owner);
        agent.modeHold = true;
        agent.currentSpeed = 0f;
        local.RPC_BearScare(owner, bear);

        // "!" over the runner
        TextMeshPro shout = new GameObject("Scared").AddComponent<TextMeshPro>();
        shout.rectTransform.sizeDelta = new Vector2(1f, 1f);
        shout.text = "!";
        shout.fontSize = 3.5f;
        shout.fontStyle = FontStyles.Bold;
        shout.alignment = TextAlignmentOptions.Center;
        shout.color = new Color(0.86f, 0.2f, 0.1f);
        shout.outlineWidth = 0.3f;
        shout.outlineColor = new Color32(255, 255, 255, 255);
        shout.sortingOrder = 50;

        PathfindingManager manager = agent.pathfindingManager;
        int segmentControl = agent.currentKontroll;
        List<Vector2Int> path = new List<Vector2Int>(agent.path);
        int from = agent.currentTargetIndex;
        Vector2Int start = agent.WorldToGridPositionCentered(agent.transform.position);
        Vector2Int bearCell = agent.WorldToGridPositionCentered(new Vector3(bear.x, bear.y, 0f));
        Vector2Int controlCell = agent.WorldToGridPositionCentered(manager.controlPoints[segmentControl].position);
        float avoidCells = BearMode.AvoidMeters * U / manager.cellSize;
        float[,] speed = manager.speedGrid2D;
        Dictionary<string, float[,]> slopes = manager.slopeFactors;
        Task<List<Vector2Int>> task = Task.Run(() => BearMode.Detour(speed, slopes, path, from, start, bearCell, avoidCells, controlCell));

        float t = 0f;
        while (t < BearMode.ScareSeconds || (!task.IsCompleted && t < 6f))
        {
            t += Time.deltaTime;
            if (shout != null)
            {
                // Jumps up and down above the runner, upright whichever way the runner faces
                shout.transform.position = agent.transform.position + new Vector3(0f, 0.28f + 0.06f * Mathf.Abs(Mathf.Sin(t * 14f)), -0.1f);
                shout.transform.rotation = Quaternion.identity;
            }
            yield return null;
        }
        if (shout != null) Destroy(shout.gameObject);

        List<Vector2Int> detour = task.IsCompleted && !task.IsFaulted ? task.Result : null;
        if (task.IsFaulted) Debug.LogWarning("Bear detour failed: " + task.Exception);
        if (detour != null && detour.Count > 0 && agent != null && agent.currentKontroll == segmentControl && !agent.hasFinished)
        {
            agent.Initialize(detour);
        }
        if (agent != null) agent.modeHold = false;
        scaring = false;
    }
}
