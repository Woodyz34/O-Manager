using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One runner's way round a relay leg, for the replay
public class RelayTrack
{
    public string label;
    public bool isLocal;
    public Color color;
    public readonly List<float> times = new List<float>();     // real seconds since the leg started
    public readonly List<Vector3> positions = new List<Vector3>();
    public Transform look;     // the runner's own dot, copied for the replay (null for test ghosts)
    // A plain round dot in the track's own colour instead of a copy of the runner's look (run it yourself, 2026-11-20,
    // Anders: the copied circle came out white and at its forest size on a sprint map)
    public bool plainDot;
    // The times are the runner's own race clock (game seconds / GameSecondsPerSecond), the same clock the split times
    // are punched on, so route analysis can cut the legs at the punches (2026-11-20)
    public bool runnerClock;
}

// Records where every runner on the leg is, a few times a second, from the moment the leg starts until the statistics come up
public class RelayReplayRecorder : MonoBehaviour
{
    const float Interval = 0.15f;

    static readonly Color[] Palette =
    {
        new Color(0.91f, 0.45f, 0.16f), new Color(0.13f, 0.47f, 0.85f), new Color(0.20f, 0.62f, 0.33f),
        new Color(0.80f, 0.16f, 0.30f), new Color(0.55f, 0.30f, 0.75f), new Color(0.10f, 0.65f, 0.70f),
        new Color(0.85f, 0.70f, 0.10f), new Color(0.45f, 0.30f, 0.20f), new Color(0.30f, 0.30f, 0.30f), new Color(0.95f, 0.35f, 0.70f)
    };

    readonly Dictionary<IPackRunner, RelayTrack> tracks = new Dictionary<IPackRunner, RelayTrack>();
    int leg = -1;
    bool recording;
    float startedAt;
    float next;

    public List<RelayTrack> Tracks
    {
        get
        {
            List<RelayTrack> list = new List<RelayTrack>(tracks.Values);
            list.Sort((a, b) => b.isLocal.CompareTo(a.isLocal));
            if (useRunnerClocks)
            {
                for (int i = 0; i < list.Count; i++) list[i] = WithoutStops(list[i]);
            }
            return list;
        }
    }

    // Individual races (2026-11-15): everyone waits at every control and draws the next stretch, so a replay on one
    // shared clock started everyone again at each control. Now every sample is stamped with that runner's own race
    // clock (which also counts the drawing), and the stops are spread over the stretch that follows: the runner
    // moves without stopping, a little slower where they drew for long, and passes every control at its real split
    // time. So the replay shows the real gaps and who catches up.
    bool useRunnerClocks;
    AgentMovement localAgent;

    public void Begin(int legIndex, AgentMovement agent, bool runnerClocks)
    {
        Begin(legIndex, agent);
        useRunnerClocks = runnerClocks;
    }

    // The runner's race clock in replay seconds (game seconds / GameSecondsPerSecond)
    float ClockOf(IPackRunner runner)
    {
        float seconds;
        if (runner is NetworkPlayer player && !player.IsLocalRunner)
        {
            seconds = RelayRace.TeamSeconds(player);
        }
        else
        {
            Clock clock = localAgent != null ? localAgent.GetComponent<Clock>() : null;
            seconds = clock != null ? clock.hourCount * 3600 + clock.minuterCount * 60 + clock.sekundercount : 0f;
        }
        return seconds / PackRunning.GameSecondsPerSecond;
    }

    // A gap this long (in replay seconds, = 6 game seconds) between two samples is a stop at a control
    const float StopGap = 6f / PackRunning.GameSecondsPerSecond;

    static RelayTrack WithoutStops(RelayTrack track)
    {
        RelayTrack smooth = new RelayTrack { label = track.label, isLocal = track.isLocal, color = track.color, look = track.look, plainDot = track.plainDot, runnerClock = true };
        smooth.positions.AddRange(track.positions);
        int n = track.times.Count;
        if (n == 0) return smooth;
        float previousEnd = 0f;   // the race starts at 0 for everyone; the first stretch takes the first drawing too
        int start = 0;
        for (int i = 1; i <= n; i++)
        {
            bool end = i == n || track.times[i] - track.times[i - 1] > StopGap;
            if (!end) continue;
            int last = i - 1;
            float from = track.times[start];
            float to = track.times[last];
            float begin = Mathf.Min(previousEnd, from);
            for (int j = start; j <= last; j++)
            {
                float f = to > from ? (track.times[j] - from) / (to - from) : 1f;
                smooth.times.Add(Mathf.Lerp(begin, to, f));
            }
            previousEnd = to;
            start = i;
        }
        return smooth;
    }

    public void Begin(int legIndex, AgentMovement agent)
    {
        tracks.Clear();
        leg = legIndex;
        useRunnerClocks = false;
        localAgent = agent;
        recording = true;
        startedAt = Time.time;
        next = 0f;
        playTime = 0f;
        lastSample = Time.time;
    }

    // The replay's clock only runs while someone is moving, so the waits (drawing, everyone at a control) are left out
    float playTime;
    float lastSample;

    public void Stop()
    {
        recording = false;
    }

    void Update()
    {
        if (!recording || Time.time < next) return;
        next = Time.time + Interval;
        float step = Time.time - lastSample;
        lastSample = Time.time;
        bool anyMoved = false;
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (runner == null || !runner.IsValidRunner || runner.RelayLeg != leg) continue;
            if (!tracks.TryGetValue(runner, out RelayTrack known) || known.positions.Count == 0) { anyMoved = true; continue; }
            float moved = Vector2.Distance(known.positions[known.positions.Count - 1], runner.RunnerPosition);
            if (moved >= 0.005f && moved <= 0.6f) anyMoved = true;
        }
        if (anyMoved) playTime += step;
        float t = playTime;
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (runner == null || !runner.IsValidRunner || runner.RelayLeg != leg) continue;
            if (!tracks.TryGetValue(runner, out RelayTrack track))
            {
                track = new RelayTrack { label = runner.RunnerLabel, isLocal = runner.IsLocalRunner, color = Palette[tracks.Count % Palette.Length] };
                if (track.isLocal) track.color = Palette[0];
                // The same colour and dot as the runner on the map
                if (runner is NetworkPlayer player)
                {
                    BodyHandler body = player.GetComponent<BodyHandler>();
                    if (body != null && body.cirkel != null)
                    {
                        Color own = body.cirkel.material != null ? body.cirkel.material.color : body.cirkel.color;
                        own.a = 1f;
                        track.color = own;
                        track.look = player.transform;
                    }
                }
                tracks[runner] = track;
            }
            Vector3 position = runner.RunnerPosition;
            if (track.positions.Count > 0 && Vector2.Distance(track.positions[track.positions.Count - 1], position) < 0.005f) continue;
            if (track.positions.Count > 0 && Vector2.Distance(track.positions[track.positions.Count - 1], position) > 0.6f)
            {
                // Moved to the start of the leg: forget where it stood before
                track.positions.Clear();
                track.times.Clear();
            }
            float stamp = useRunnerClocks ? ClockOf(runner) : t;
            // A remote clock arrives a moment after the position: never let a runner's time go backwards
            if (track.times.Count > 0) stamp = Mathf.Max(stamp, track.times[track.times.Count - 1]);
            track.times.Add(stamp);
            track.positions.Add(position);
        }
    }
}

// Plays back the recorded leg on the map: every runner's line grows as the time goes, with a dot and the team's name
// at the front. The camera is taken over while it plays and given back when it closes.
public class RelayReplayView : MonoBehaviour
{
    List<RelayTrack> tracks;
    System.Action onClose;
    Camera cam;
    CameraController cameraController;
    Vector3 savedPosition;
    Quaternion savedRotation;
    float savedSize;

    readonly List<LineRenderer> lines = new List<LineRenderer>();
    readonly List<Transform> markers = new List<Transform>();
    GameObject bar;
    TextMeshProUGUI timeText;
    Button playButton;
    Button speedButton;
    Slider slider;
    float time;
    float endTime;
    float speed = 2f;
    bool playing = true;
    bool settingSlider;
    static Material material;

    // The replay on the screen, if any: the Follow runner button hides and the clock shows the replay's time
    public static RelayReplayView Current { get; private set; }

    public static RelayReplayView Open(List<RelayTrack> tracks, System.Action onClose)
    {
        RelayReplayView view = new GameObject("Relay replay").AddComponent<RelayReplayView>();
        view.tracks = tracks;
        view.onClose = onClose;
        Current = view;
        view.Build();
        return view;
    }

    void Build()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        cameraController = local != null ? local.GetComponent<CameraController>() : null;
        cam = cameraController != null && cameraController.cam != null ? cameraController.cam : Camera.main;
        if (cameraController != null)
        {
            cameraController.enabled = false;
            cameraController.HideFollowButton();
        }
        savedPosition = cam.transform.position;
        savedRotation = cam.transform.rotation;
        savedSize = cam.orthographicSize;

        // Fit the whole leg on the screen, north up
        Bounds bounds = new Bounds();
        bool any = false;
        foreach (RelayTrack track in tracks)
        {
            foreach (Vector3 p in track.positions)
            {
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
            if (track.times.Count > 0) endTime = Mathf.Max(endTime, track.times[track.times.Count - 1]);
        }
        PathfindingManager pathfinding = FindObjectOfType<PathfindingManager>();
        if (pathfinding != null)
        {
            foreach (Transform point in pathfinding.controlPoints)
            {
                if (!any) { bounds = new Bounds(point.position, Vector3.zero); any = true; }
                else bounds.Encapsulate(point.position);
            }
        }
        cam.transform.rotation = Quaternion.identity;
        cam.transform.position = new Vector3(bounds.center.x, bounds.center.y - bounds.extents.y * 0.08f, -10f);
        cam.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / Mathf.Max(0.1f, cam.aspect)) * 1.2f + 0.3f;

        if (material == null) material = new Material(Shader.Find("Sprites/Default"));
        // Sizes follow the zoom, so the lines and dots are as easy to see on a long leg as on a short one
        fitSize = cam.orthographicSize;
        float lineWidth = cam.orthographicSize * 0.012f;
        baseLineWidth = lineWidth;
        float dotSize = cam.orthographicSize * 0.045f;
        foreach (RelayTrack track in tracks)
        {
            LineRenderer line = new GameObject("Replay line " + track.label).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = material;
            line.widthMultiplier = lineWidth;
            line.startColor = line.endColor = track.color;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.sortingOrder = 40;
            line.useWorldSpace = true;
            lines.Add(line);

            // A runner who left the game has no dot of its own any more: a plain round one (2026-11-20, Anders: it was a square)
            GameObject marker = track.plainDot || track.look == null ? PlainDot(track, dotSize * 1.3f) : CopyLook(track.look);
            if (marker == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(marker.GetComponent<Collider>());
                marker.transform.localScale = Vector3.one * dotSize;
                Renderer markerRenderer = marker.GetComponent<Renderer>();
                markerRenderer.material = new Material(material) { color = track.color };
                markerRenderer.sortingOrder = 41;
            }
            else
            {
                // The runner's own dot, made as big as the replay needs to be seen on the whole map
                float size = 0f;
                foreach (SpriteRenderer part in marker.GetComponentsInChildren<SpriteRenderer>()) size = Mathf.Max(size, part.bounds.size.x);
                if (size > 0.0001f) marker.transform.localScale *= Mathf.Max(1f, dotSize * 1.3f / size);
            }
            marker.name = "Replay runner " + track.label;
            marker.transform.SetParent(transform, true);
            TextMeshPro label = new GameObject("Name").AddComponent<TextMeshPro>();
            label.transform.SetParent(marker.transform, false);
            float markerScale = Mathf.Max(0.0001f, marker.transform.lossyScale.x);
            label.transform.localScale = Vector3.one * (dotSize / markerScale);
            label.transform.localPosition = new Vector3(0f, 1.3f * dotSize / markerScale, 0f);
            label.rectTransform.sizeDelta = new Vector2(12f, 2f);
            label.text = track.label;
            label.fontSize = 11f;
            label.fontStyle = FontStyles.Bold;
            label.color = track.color;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.sortingOrder = 42;
            markers.Add(marker.transform);
            markerScales.Add(marker.transform.localScale);
        }

        HideRealRunners();
        BuildBar();
        Apply();
    }

    // A round dot in the track's colour with a dark ring round it, the size the replay draws every runner at least
    GameObject PlainDot(RelayTrack track, float diameter)
    {
        RunnerMarker look = track.look != null ? track.look.GetComponentInChildren<RunnerMarker>(true) : null;
        // Gone (the runner left): the round dot of any runner still here
        if (look == null || look.circle == null) look = FindAnyObjectByType<RunnerMarker>(FindObjectsInactive.Include);
        Sprite round = look != null && look.circle != null ? look.circle.sprite : null;
        if (round == null) return null;
        Sprite ringSprite = look.ring != null ? look.ring.sprite : null;
        GameObject root = new GameObject("Look");
        float size = Mathf.Max(0.0001f, round.bounds.size.x);
        root.transform.localScale = Vector3.one * (diameter / size);
        SpriteRenderer dot = new GameObject("Dot").AddComponent<SpriteRenderer>();
        dot.transform.SetParent(root.transform, false);
        dot.sprite = round;
        dot.sharedMaterial = material;
        dot.color = new Color(track.color.r, track.color.g, track.color.b, 1f);
        dot.sortingOrder = 41;
        if (ringSprite != null)
        {
            SpriteRenderer ring = new GameObject("Ring").AddComponent<SpriteRenderer>();
            ring.transform.SetParent(root.transform, false);
            ring.transform.localScale = Vector3.one * (size * 1.08f / Mathf.Max(0.0001f, ringSprite.bounds.size.x));
            ring.sprite = ringSprite;
            ring.sharedMaterial = material;
            ring.color = new Color(0.12f, 0.16f, 0.13f, 1f);
            ring.sortingOrder = 42;
        }
        return root;
    }

    // A copy of how the runner looks on the map (its dot and rings), in its own colour
    static GameObject CopyLook(Transform source)
    {
        GameObject root = new GameObject("Look");
        root.transform.position = source.position;
        root.transform.localScale = source.lossyScale;
        foreach (SpriteRenderer original in source.GetComponentsInChildren<SpriteRenderer>(true))
        {
            // Only the dot and its rings, not the map pictures the runner carries
            if (original.transform != source && original.name != "Dot" && original.name != "Ring") continue;
            if (!original.enabled || !original.gameObject.activeInHierarchy) continue;
            GameObject part = new GameObject(original.name);
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = original.transform == source ? Vector3.zero : source.InverseTransformPoint(original.transform.position);
            part.transform.localScale = original.transform == source ? Vector3.one : original.transform.localScale;
            SpriteRenderer copy = part.AddComponent<SpriteRenderer>();
            copy.sprite = original.sprite;
            copy.color = original.color;
            if (original.sharedMaterial != null) copy.material = new Material(original.material);
            copy.sortingOrder = 41 + (original.transform == source ? 0 : 1);
        }
        return root.transform.childCount > 0 ? root : null;
    }

    // The real runners stay where they finished; they are hidden while the replay plays
    readonly List<Behaviour> hiddenBehaviours = new List<Behaviour>();
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();

    void HideRealRunners()
    {
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (!(runner is MonoBehaviour behaviour) || behaviour == null) continue;
            foreach (Renderer r in behaviour.GetComponentsInChildren<Renderer>())
            {
                if (r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
            }
            foreach (Canvas c in behaviour.GetComponentsInChildren<Canvas>())
            {
                if (c.enabled) { c.enabled = false; hiddenBehaviours.Add(c); }
            }
        }
    }

    void ShowRealRunners()
    {
        foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
        foreach (Behaviour b in hiddenBehaviours) if (b != null) b.enabled = true;
        hiddenRenderers.Clear();
        hiddenBehaviours.Clear();
    }

    void BuildBar()
    {
        GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
        if (canvas == null) return;
        Image panel = CourseUI.CardPanel("Replay controls", canvas.transform);
        bar = panel.gameObject;
        CourseUI.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 24), new Vector2(1180, 96));
        CourseUI.Row(panel.gameObject, 14, new RectOffset(20, 20, 18, 18)).childForceExpandWidth = false;

        playButton = CourseUI.Button("Play", panel.transform, "Pause", CourseUI.ButtonKind.Secondary, TogglePlay, 22);
        CourseUI.Size(playButton, 58, 130);
        speedButton = CourseUI.Button("Speed", panel.transform, "Speed x2", CourseUI.ButtonKind.Light, NextSpeed, 22);
        CourseUI.Size(speedButton, 58, 150);
        slider = CourseUI.SliderControl("Time", panel.transform, 0f, Mathf.Max(0.1f, endTime), 0f, false, value =>
        {
            if (settingSlider) return;
            time = value;
            Apply();
        });
        CourseUI.Size(slider, 58, 420);
        timeText = CourseUI.Text("Clock", panel.transform, "", 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(timeText, 58, 170);
        Button close = CourseUI.Button("Close", panel.transform, "Back to statistics", CourseUI.ButtonKind.Primary, Close, 20);
        CourseUI.Size(close, 58, 220);
    }

    void TogglePlay()
    {
        if (!playing && time >= endTime) time = 0f;
        playing = !playing;
        CourseUI.SetButtonText(playButton, playing ? "Pause" : "Play");
    }

    void NextSpeed()
    {
        speed = speed >= 16f ? 1f : speed * 2f;
        CourseUI.SetButtonText(speedButton, "Speed x" + speed);
    }

    // Zoom and move the map while the replay plays (2026-11-15: on narrow screens the leaderboard hid the finish).
    // Mouse wheel zooms towards the mouse, dragging with any mouse button moves the map. Lines and dots keep their
    // size on the screen.
    float fitSize;
    float baseLineWidth;
    readonly List<Vector3> markerScales = new List<Vector3>();
    bool dragging;
    Vector3 dragFrom;

    void HandleMapInput()
    {
        if (cam == null) return;
        bool overUI = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        float scroll = Input.mouseScrollDelta.y;
        if (!overUI && Mathf.Abs(scroll) > 0.01f)
        {
            Vector3 before = cam.ScreenToWorldPoint(Input.mousePosition);
            float size = cam.orthographicSize * Mathf.Pow(0.88f, scroll);
            cam.orthographicSize = Mathf.Clamp(size, fitSize * 0.08f, fitSize * 1.6f);
            Vector3 after = cam.ScreenToWorldPoint(Input.mousePosition);
            cam.transform.position += new Vector3(before.x - after.x, before.y - after.y, 0f);
            Rescale();
        }
        bool anyDown = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        bool anyHeld = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
        if (anyDown && !overUI)
        {
            dragging = true;
            dragFrom = cam.ScreenToWorldPoint(Input.mousePosition);
        }
        if (!anyHeld) dragging = false;
        if (dragging)
        {
            Vector3 now = cam.ScreenToWorldPoint(Input.mousePosition);
            cam.transform.position += new Vector3(dragFrom.x - now.x, dragFrom.y - now.y, 0f);
        }
    }

    void Rescale()
    {
        float ratio = fitSize > 0f ? cam.orthographicSize / fitSize : 1f;
        foreach (LineRenderer line in lines) if (line != null) line.widthMultiplier = baseLineWidth * ratio;
        for (int k = 0; k < markers.Count && k < markerScales.Count; k++)
        {
            if (markers[k] != null) markers[k].localScale = markerScales[k] * ratio;
        }
    }

    void Update()
    {
        HandleMapInput();
        if (!playing) return;
        time += Time.deltaTime * speed;
        if (time >= endTime)
        {
            time = endTime;
            playing = false;
            CourseUI.SetButtonText(playButton, "Play");
        }
        Apply();
    }

    void Apply()
    {
        for (int k = 0; k < tracks.Count; k++)
        {
            RelayTrack track = tracks[k];
            int count = 0;
            while (count < track.times.Count && track.times[count] <= time) count++;
            LineRenderer line = lines[k];
            line.positionCount = count;
            for (int i = 0; i < count; i++) line.SetPosition(i, new Vector3(track.positions[i].x, track.positions[i].y, -1.3f));
            bool shown = count > 0;
            markers[k].gameObject.SetActive(shown);
            if (shown)
            {
                Vector3 position = track.positions[count - 1];
                if (count < track.times.Count)
                {
                    float span = track.times[count] - track.times[count - 1];
                    float f = span > 0f ? (time - track.times[count - 1]) / span : 0f;
                    position = Vector3.Lerp(position, track.positions[count], Mathf.Clamp01(f));
                }
                markers[k].position = new Vector3(position.x, position.y, -1.35f);
            }
        }
        if (timeText != null)
        {
            timeText.text = "+" + RelayStats.Time(Mathf.RoundToInt(time * PackRunning.GameSecondsPerSecond));
        Clock clock = NetworkPlayer.Local != null ? NetworkPlayer.Local.clock : null;
        if (clock != null) clock.ShowReplayTime(Mathf.RoundToInt(time * PackRunning.GameSecondsPerSecond));
        }
        if (slider != null)
        {
            settingSlider = true;
            slider.value = time;
            settingSlider = false;
        }
    }

    public void Close()
    {
        cam.transform.position = savedPosition;
        cam.transform.rotation = savedRotation;
        cam.orthographicSize = savedSize;
        if (cameraController != null) cameraController.enabled = true;
        ShowRealRunners();
        if (bar != null) Destroy(bar);
        Destroy(gameObject);
        onClose?.Invoke();
    }

    // Closed by a scene change (the host took everyone back to the lobby) rather than by Close()
    void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
            Clock clock = NetworkPlayer.Local != null ? NetworkPlayer.Local.clock : null;
            if (clock != null) clock.ShowOwnTime();
        }
        if (cameraController != null) cameraController.enabled = true;
        // The runners live on into the next race: they must not stay hidden
        ShowRealRunners();
    }
}
