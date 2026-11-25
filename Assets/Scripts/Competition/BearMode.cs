using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;

// Bears game mode. While drawing, every player may hide one bear somewhere on the map (GameModeTools). The bear is
// only seen by its owner until a runner comes close to it: then everyone sees a bear jump up and roar (ModeBanner),
// and from then on the bear stands on the map for all. A runner who comes close to any bear, their own too, gets scared:
// stands still for a moment and then runs a detour round the bear back to their own line. That costs a little time,
// which is all a bear does. Your own bear scares your own runner too.
//
// Each player's runner checks the bears itself (BearWatcher); the bears' places are shared on the owners' NetworkPlayers.
public static class BearMode
{
    // 2026-11-19: half the reach it had (a runner got scared from the path below the bear), and a bigger bear for
    // every bear saved on a relay (size 2 = twice the reach)
    public const float FindMeters = 22.5f;          // a runner this close finds a normal bear
    public const float AvoidMeters = 40f;           // the detour keeps about this far away from it
    public const float MinFromControlMeters = 90f;  // bears are not hidden on top of the controls
    public const float ScareSeconds = 1.0f;         // how long a scared runner stands still (real seconds)
    public const float SizeMeters = 70f;            // how big a normal bear is drawn on the map
    public const float CooldownGameSeconds = 30f;   // the same bear can scare the same runner again after this
    public const int MaxBears = 3;                  // bears per player and leg at most (the host picks 1-3)
    public const float BiggestBear = 8f;

    static float U => MapDefinition.WorldUnitsPerMeter;

    // Relays too since 2026-11-16: bears per player and leg; they stay on that leg only (RelayRace.StartLeg takes them away)
    public static bool On => ExtraModes.Is(ExtraMode.Bears);

    // A bear: its owner and its place in the owner's list
    public static int Key(PlayerRef owner, int slot) => owner.PlayerId * MaxBears + slot;

    static readonly HashSet<int> found = new HashSet<int>();
    // Relays: bears not hidden on the legs before, saved up for a bigger bear (local player)
    static int saved;
    // The stage each of the local player's bears was hidden on: it can be moved until that stage starts
    static readonly Dictionary<int, int> hiddenOn = new Dictionary<int, int>();

    public static void Reset()
    {
        found.Clear();
        saved = 0;
        hiddenOn.Clear();
    }

    public static bool IsFound(int key) => found.Contains(key);

    // A bear taken away or hidden somewhere new (next relay leg, dragged): it has to be found again
    public static void Forget(int key) => found.Remove(key);

    // ---- Sizes ----

    // A sprint map is drawn at a much bigger scale (1:4 000 against 1:15 000 in the forest), so a bear of the forest's
    // size covered whole blocks there (2026-11-19, the user). On a sprint course the bear, its reach and the detour
    // around it are this share of the forest's.
    public const float SprintScale = 0.4f;
    public static float MapScale
    {
        get
        {
            bool sprint = CourseLoader.Loaded != null ? CourseLoader.Loaded.IsSprint() : CourseLoader.LoadedMap != null && CourseLoader.LoadedMap.IsSprint;
            return sprint ? SprintScale : 1f;
        }
    }

    public static float FindRadiusMeters(float size) => FindMeters * MapScale * Mathf.Max(1f, size);
    public static float AvoidRadiusMeters(float size) => AvoidMeters * MapScale * Mathf.Max(1f, size);
    public static float DrawnMeters(float size) => SizeMeters * MapScale * Mathf.Pow(Mathf.Max(1f, size), 0.6f);
    // Controls keep clear of the bear's reach
    public static float KeepFromControlMeters(float size) => Mathf.Max(MinFromControlMeters * MapScale, FindRadiusMeters(size) + 30f * MapScale);
    // The size the local player's next bear gets
    public static float NextSize => Mathf.Min(BiggestBear, 1f + saved);
    public static int Saved => saved;

    // ---- The local player's bears ----

    public static int Placed(NetworkPlayer player)
    {
        int count = 0;
        if (player == null) return 0;
        for (int i = 0; i < MaxBears; i++) if (player.bears[i].z > 0f) count++;
        return count;
    }

    // How many more bears the local player may hide now (the host's "bears per player and leg")
    public static int Left(NetworkPlayer player) => Mathf.Max(0, Mathf.Min(MaxBears, HostSettings.BearsPerLeg) - Placed(player));

    // A relay leg or an individual race's stretch: bears can be moved while it is drawn
    static int Stage(AgentMovement agent)
    {
        if (RelayRace.Active != null) return RelayRace.CurrentLegIndex;
        return 1000 + (agent != null ? agent.currentKontroll : 0);
    }

    // Can the local player drag this bear now? Only on the stage it was hidden on, until that stage starts running
    // (2026-11-19: it used to be stuck where it was first put)
    public static bool Movable(NetworkPlayer local, AgentMovement agent, int slot)
    {
        if (local == null || agent == null || agent.manualPathDrawer == null || local.bears[slot].z <= 0f) return false;
        if (!hiddenOn.TryGetValue(slot, out int stage) || stage != Stage(agent)) return false;
        return agent.manualPathDrawer.timeToDraw || agent.WaitingForAllPlayersToDraw;
    }

    // A new relay leg: the last leg's bears go, the ones not hidden are saved for a bigger bear
    public static void NewLeg(NetworkPlayer np, int legIndex)
    {
        if (legIndex > 0 && On) saved += Left(np);
        for (int i = 0; i < MaxBears; i++) np.bears.Set(i, Vector3.zero);
        hiddenOn.Clear();
    }

    // Why a bear cannot be hidden here, or null when it can
    public static string PlaceProblem(Vector3 world, PathfindingManager manager, float size = 1f)
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
        float keep = KeepFromControlMeters(size);
        foreach (Transform point in manager.controlPoints)
        {
            if (point != null && Vector2.Distance(point.position, world) < keep * U)
            {
                return "Too close to a control. Hide the bear at least " + Mathf.RoundToInt(keep) + " m from the controls.";
            }
        }
        return null;
    }

    public static void Place(NetworkPlayer local, Vector3 world)
    {
        AgentMovement agent = local.agentMovement;
        for (int i = 0; i < MaxBears; i++)
        {
            if (local.bears[i].z > 0f) continue;
            local.bears.Set(i, new Vector3(world.x, world.y, NextSize));
            saved = 0;
            hiddenOn[i] = Stage(agent);
            return;
        }
    }

    // Dragged somewhere else while drawing
    public static void Move(NetworkPlayer local, int slot, Vector3 world)
    {
        Vector3 bear = local.bears[slot];
        if (bear.z <= 0f) return;
        local.bears.Set(slot, new Vector3(world.x, world.y, bear.z));
    }

    // Received by everyone when a runner got scared by a bear
    public static void OnScare(PlayerRef owner, int slot, Vector2 where, NetworkPlayer scared)
    {
        bool first = found.Add(Key(owner, slot));
        NetworkPlayer ownerPlayer = CompetitionFlow.FindPlayer(owner);
        bool ownerIsMe = ownerPlayer != null && ownerPlayer.Object != null && ownerPlayer.Object.HasStateAuthority;
        bool scaredIsMe = scared != null && scared.Object != null && scared.Object.HasStateAuthority;
        bool hidden = ExtraModes.HideOwners;
        string ownerName = ownerIsMe ? "your" : hidden ? "a" : (ownerPlayer != null ? Name(ownerPlayer) : "someone") + "'s";
        string runnerName = scaredIsMe ? "you" : scared != null ? Name(scared) : "a runner";
        bool ownBear = scared != null && scared == ownerPlayer;
        if (ownBear) ownerName = scaredIsMe ? "your own" : hidden ? "a" : "their own";

        if (BearWatcher.Instance != null)
        {
            BearWatcher.Instance.PlayRoar(Key(owner, slot), where, first);
        }
        if (first)
        {
            ModeBanner.Show(ModeBanner.BearSprite, "RAAAWR!", (ownBear ? Capital(runnerName) + " got scared by " + ownerName + " bear!" : Capital(ownerName) + " bear scared " + runnerName + "!"),
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
    // The way round a bear (2026-11-19, the user):
    //  - The bear itself can never be passed: the cells closer to it than the runner stands now are closed, so a bear
    //    on the only bridge sends the runner another way, however long, instead of straight through it.
    //  - The runner still keeps its own route choice: it goes back to its drawn route beyond the bear. But not to the
    //    first point of the route behind the bear if that means running back towards the bear and turning round:
    //    every point of the route ahead is tried, and the one with the quickest way there plus the rest of the route
    //    from there wins.
    public static List<Vector2Int> Detour(float[,] speed, Dictionary<string, float[,]> slopes, List<Vector2Int> path, int from,
                                          Vector2Int start, Vector2Int bearCell, float avoidCells, Vector2Int controlCell)
    {
        int rows = speed.GetLength(0);
        int cols = speed.GetLength(1);

        // The runner may move away from the bear, never closer to it than it is now (and never into the bear's middle)
        float standing = Vector2Int.Distance(start, bearCell);
        float closed = Mathf.Max(1.5f, Mathf.Min(standing - 1f, avoidCells * 0.6f));
        float[,] grid = (float[,])speed.Clone();
        int r = Mathf.CeilToInt(avoidCells);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int x = bearCell.x + dx;
                int y = bearCell.y + dy;
                if (x < 0 || y < 0 || x >= cols || y >= rows) continue;
                float d2 = dx * dx + dy * dy;
                if (d2 > avoidCells * avoidCells) continue;
                if (grid[y, x] <= 0.01f) continue;
                if (d2 <= closed * closed && !(x == start.x && y == start.y)) grid[y, x] = 0f;
                else grid[y, x] = Mathf.Max(grid[y, x] * 0.04f, 0.0105f);   // passable, just very slow
            }
        }

        // The points of the route ahead where the runner may go back to it: beyond the bear and out of its reach
        var candidates = new List<int>();
        float[] rest = null;
        if (path != null && path.Count > 0)
        {
            from = Mathf.Clamp(from, 0, path.Count - 1);
            int closest = from;
            float best = float.MaxValue;
            for (int i = from; i < path.Count; i++)
            {
                float d = Vector2Int.Distance(path[i], bearCell);
                if (d < best) { best = d; closest = i; }
            }
            for (int i = closest; i < path.Count; i++)
            {
                if (Vector2Int.Distance(path[i], bearCell) > avoidCells + 1.5f) candidates.Add(i);
            }
            // The time left along the route from each point to its end
            rest = new float[path.Count];
            for (int i = path.Count - 2; i >= 0; i--)
            {
                rest[i] = rest[i + 1] + StepCost(speed, path[i], path[i + 1]);
            }
        }

        Vector2Int goal = controlCell;
        int rejoin = -1;
        if (candidates.Count > 0)
        {
            rejoin = BestRejoin(grid, start, path, candidates, rest, rest[from]);
            if (rejoin >= 0) goal = path[rejoin];
        }

        AStarPathfinder astar = new AStarPathfinder(grid, slopes);
        List<List<Vector2Int>> paths = astar.GetAllPaths(start, goal, 1);
        if ((paths == null || paths.Count == 0 || paths[0] == null || paths[0].Count == 0) && rejoin >= 0)
        {
            // The route can not be reached round the bear: straight to the control another way
            rejoin = -1;
            paths = astar.GetAllPaths(start, controlCell, 1);
        }
        if (paths == null || paths.Count == 0 || paths[0] == null || paths[0].Count == 0) return null;
        List<Vector2Int> result = new List<Vector2Int>(paths[0]);
        if (rejoin >= 0 && rejoin + 1 < path.Count)
        {
            result.AddRange(path.GetRange(rejoin + 1, path.Count - rejoin - 1));
        }
        return result;
    }

    static float StepCost(float[,] speed, Vector2Int a, Vector2Int b)
    {
        float s = Mathf.Max(0.05f, speed[Mathf.Clamp(b.y, 0, speed.GetLength(0) - 1), Mathf.Clamp(b.x, 0, speed.GetLength(1) - 1)]);
        return Vector2Int.Distance(a, b) / s;
    }

    // The route point (index into path) with the quickest way there from start plus the rest of the route from it, or
    // -1 when none can be reached. A Dijkstra search from the runner that stops once nothing can beat the best found.
    static int BestRejoin(float[,] grid, Vector2Int start, List<Vector2Int> path, List<int> candidates, float[] rest, float routeLeft)
    {
        int rows = grid.GetLength(0), cols = grid.GetLength(1);
        var wanted = new Dictionary<int, List<int>>();
        foreach (int i in candidates)
        {
            Vector2Int c = path[i];
            if (c.x < 0 || c.y < 0 || c.x >= cols || c.y >= rows) continue;
            int key = c.y * cols + c.x;
            if (!wanted.TryGetValue(key, out List<int> list)) wanted[key] = list = new List<int>();
            list.Add(i);
        }
        if (wanted.Count == 0 || start.x < 0 || start.y < 0 || start.x >= cols || start.y >= rows) return -1;

        var dist = new float[rows * cols];
        for (int k = 0; k < dist.Length; k++) dist[k] = float.MaxValue;
        var heap = new List<KeyValuePair<float, int>>();
        int startKey = start.y * cols + start.x;
        dist[startKey] = 0f;
        Push(heap, 0f, startKey);
        // A way back that takes several times the whole rest of the route is not looked for (the control is then
        // the goal instead), so a bear on the only way across does not search the whole map
        float bestTotal = routeLeft * 4f + 400f;
        int bestIndex = -1;
        int popped = 0;
        while (heap.Count > 0 && popped < 4000000)
        {
            KeyValuePair<float, int> top = Pop(heap);
            float d = top.Key;
            int key = top.Value;
            if (d > dist[key]) continue;
            if (d >= bestTotal) break;   // nothing further can beat it
            popped++;
            if (wanted.TryGetValue(key, out List<int> indices))
            {
                foreach (int i in indices)
                {
                    float total = d + rest[i];
                    if (total < bestTotal) { bestTotal = total; bestIndex = i; }
                }
            }
            int x = key % cols, y = key / cols;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                    float s = grid[ny, nx];
                    if (s <= 0.01f) continue;
                    // No corner cutting between two closed cells
                    if (dx != 0 && dy != 0 && (grid[y, nx] <= 0.01f || grid[ny, x] <= 0.01f)) continue;
                    float nd = d + (dx != 0 && dy != 0 ? 1.41421356f : 1f) / s;
                    int nkey = ny * cols + nx;
                    if (dist[nkey] <= nd) continue;
                    dist[nkey] = nd;
                    Push(heap, nd, nkey);
                }
            }
        }
        return bestIndex;
    }

    static void Push(List<KeyValuePair<float, int>> heap, float key, int value)
    {
        heap.Add(new KeyValuePair<float, int>(key, value));
        int i = heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (heap[parent].Key <= heap[i].Key) break;
            (heap[parent], heap[i]) = (heap[i], heap[parent]);
            i = parent;
        }
    }

    static KeyValuePair<float, int> Pop(List<KeyValuePair<float, int>> heap)
    {
        KeyValuePair<float, int> top = heap[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);
        int i = 0;
        while (true)
        {
            int left = 2 * i + 1, right = left + 1, smallest = i;
            if (left < heap.Count && heap[left].Key < heap[smallest].Key) smallest = left;
            if (right < heap.Count && heap[right].Key < heap[smallest].Key) smallest = right;
            if (smallest == i) break;
            (heap[smallest], heap[i]) = (heap[i], heap[smallest]);
            i = smallest;
        }
        return top;
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

    readonly Dictionary<int, View> views = new Dictionary<int, View>();
    // When each bear last scared the local runner (Time.time): it can scare it again after a cooldown (2026-11-19:
    // a runner scared once could run straight into the same bear later)
    readonly Dictionary<int, float> scaredAt = new Dictionary<int, float>();
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
        // The race camera turns with each leg, so the bears turn with it and stay upright (like the runners' names)
        Camera cam = Camera.main;
        Quaternion upright = cam != null ? cam.transform.rotation : Quaternion.identity;
        // The bears that are seen breathe a little
        foreach (View view in views.Values)
        {
            if (view.root != null) view.root.transform.rotation = upright;
            if (view.root != null && view.root.activeSelf && view.popped)
            {
                float breath = 1f + 0.03f * Mathf.Sin(Time.time * 2.4f);
                view.sprite.transform.localScale = new Vector3(1f / breath, breath, 1f);
            }
        }
    }

    readonly Dictionary<int, Vector3> lastPlace = new Dictionary<int, Vector3>();

    // Your own bears are shown faded; someone else's only once found.
    // Relays: a bear taken away at a new leg is hidden, and a bear hidden somewhere new is unfound and can scare again.
    void SyncViews()
    {
        foreach (NetworkPlayer player in NetworkPlayer.All())
        {
            if (player.Object == null || !player.Object.IsValid) continue;
            PlayerRef owner = player.Object.StateAuthority;
            bool mine = player.Object.HasStateAuthority;
            for (int slot = 0; slot < BearMode.MaxBears; slot++)
            {
                int key = BearMode.Key(owner, slot);
                Vector3 bear = player.bears[slot];
                bool placed = bear.z > 0f;
                bool moved = !placed || !lastPlace.TryGetValue(key, out Vector3 was) || was != bear;
                if (moved && lastPlace.ContainsKey(key))
                {
                    lastPlace.Remove(key);
                    // Your own bear dragged a little while drawing is still the same bear; anything else is a new one
                    if (!placed || !mine)
                    {
                        BearMode.Forget(key);
                        scaredAt.Remove(key);
                    }
                    if (views.TryGetValue(key, out View old) && old.root != null && (!placed || !mine)) old.root.SetActive(false);
                }
                if (!placed) continue;
                lastPlace[key] = bear;
                bool found = BearMode.IsFound(key);
                if (!mine && !found) continue;
                View view = GetView(key);
                view.root.transform.position = new Vector3(bear.x, bear.y, -1.4f);
                view.root.transform.localScale = Vector3.one * BearMode.DrawnMeters(bear.z) * U;
                if (!view.root.activeSelf) view.root.SetActive(true);
                Color color = Color.white;
                color.a = found ? 1f : 0.55f;
                if (!view.popped) view.sprite.color = color;
                string size = bear.z > 1.01f ? " (" + Mathf.RoundToInt(bear.z) + "x)" : "";
                view.label.text = (mine ? "Your bear" : ExtraModes.HideOwners ? "A bear" : BearMode.Name(player) + "'s bear") + size;
            }
        }
    }

    // The local player's own bear moves at once while it is dragged, not only at the next sync
    public void ShowOwnAt(int key, Vector3 bear)
    {
        if (!views.TryGetValue(key, out View view) || view.root == null) return;
        view.root.transform.position = new Vector3(bear.x, bear.y, -1.4f);
    }

    View GetView(int key)
    {
        if (views.TryGetValue(key, out View view) && view.root != null) return view;
        view = new View();
        view.root = new GameObject("Bear " + key);
        GameObject art = new GameObject("Picture");
        art.transform.SetParent(view.root.transform, false);
        view.sprite = art.AddComponent<SpriteRenderer>();
        view.sprite.sprite = ModeBanner.BearSprite;
        view.sprite.sortingOrder = 30;
        float size = BearMode.DrawnMeters(1f) * U;
        view.root.transform.localScale = Vector3.one * size;

        view.label = new GameObject("Name").AddComponent<TextMeshPro>();
        view.label.transform.SetParent(view.root.transform, false);
        view.label.transform.localPosition = new Vector3(0f, -0.62f, 0f);
        view.label.rectTransform.sizeDelta = new Vector2(4f, 0.4f);
        view.label.fontSize = 1.6f;
        view.label.fontStyle = FontStyles.Bold;
        view.label.alignment = TextAlignmentOptions.Center;
        view.label.enableWordWrapping = false;
        view.label.color = new Color(0.36f, 0.2f, 0.08f);
        view.label.outlineWidth = 0.2f;
        view.label.outlineColor = new Color32(255, 255, 255, 200);
        view.label.sortingOrder = 31;
        views[key] = view;
        return view;
    }

    // Everyone: the bear jumps up and roars where it stands
    public void PlayRoar(int key, Vector2 where, bool first)
    {
        View view = GetView(key);
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
        roar.fontSize = 3f;
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
        // Only while actually running a stretch. Also in the middle of a mistake (2026-11-19: a lost runner ran
        // straight through a bear); then it only stands still a moment, the mistake keeps its own way.
        if (!agent.canRunNextSegment || agent.hasFinished || agent.WaitingForAllPlayersToDraw || agent.WaitingForAllPlayersToReachKontroll
            || agent.manualPathDrawer.timeToDraw || agent.manualPathDrawer.isDrawing || agent.modeHold || agent.relayHold
            || agent.path == null || agent.path.Count == 0 || CompetitionFlow.ResultsShown)
        {
            return;
        }
        Vector3 me = agent.transform.position;
        float cooldown = BearMode.CooldownGameSeconds / PackRunning.GameSecondsPerSecond;
        foreach (NetworkPlayer player in NetworkPlayer.All())
        {
            if (player.Object == null || !player.Object.IsValid) continue;
            PlayerRef owner = player.Object.StateAuthority;
            for (int slot = 0; slot < BearMode.MaxBears; slot++)
            {
                Vector3 bear = player.bears[slot];
                if (bear.z <= 0f) continue;
                int key = BearMode.Key(owner, slot);
                if (scaredAt.TryGetValue(key, out float at) && Time.time - at < cooldown) continue;
                if (Vector2.Distance(me, bear) <= BearMode.FindRadiusMeters(bear.z) * U)
                {
                    StartCoroutine(Scare(local, agent, owner, slot, bear));
                    return;
                }
            }
        }
    }

    IEnumerator Scare(NetworkPlayer local, AgentMovement agent, PlayerRef owner, int slot, Vector3 bear)
    {
        scaring = true;
        scaredAt[BearMode.Key(owner, slot)] = Time.time;
        bool lost = agent.IsMissing;
        agent.modeHold = true;
        agent.currentSpeed = 0f;
        local.RPC_BearScare(owner, slot, (Vector2)bear);

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
        float avoidCells = BearMode.AvoidRadiusMeters(bear.z) * U / manager.cellSize;
        float[,] speed = manager.speedGrid2D;
        Dictionary<string, float[,]> slopes = manager.slopeFactors;
        Task<List<Vector2Int>> task = Task.Run(() => BearMode.Detour(speed, slopes, path, from, start, bearCell, avoidCells, controlCell));

        float t = 0f;
        while (t < BearMode.ScareSeconds || (!task.IsCompleted && t < 15f))
        {
            t += Time.deltaTime;
            if (shout != null)
            {
                // Jumps up and down above the runner, upright whichever way the runner faces
                shout.transform.position = agent.transform.position + new Vector3(0f, 0.28f + 0.06f * Mathf.Abs(Mathf.Sin(t * 14f)), -0.1f);
                shout.transform.rotation = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
            }
            yield return null;
        }
        if (shout != null) Destroy(shout.gameObject);

        List<Vector2Int> detour = task.IsCompleted && !task.IsFaulted ? task.Result : null;
        if (task.IsFaulted) Debug.LogWarning("Bear detour failed: " + task.Exception);
        if (detour != null && detour.Count > 0 && agent != null && agent.currentKontroll == segmentControl && !agent.hasFinished && !lost && !agent.IsMissing)
        {
            agent.Initialize(detour);
        }
        if (agent != null) agent.modeHold = false;
        scaring = false;
    }
}
