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
            return list;
        }
    }

    public void Begin(int legIndex, AgentMovement agent)
    {
        tracks.Clear();
        leg = legIndex;
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
            track.times.Add(t);
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

    public static RelayReplayView Open(List<RelayTrack> tracks, System.Action onClose)
    {
        RelayReplayView view = new GameObject("Relay replay").AddComponent<RelayReplayView>();
        view.tracks = tracks;
        view.onClose = onClose;
        view.Build();
        return view;
    }

    void Build()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        cameraController = local != null ? local.GetComponent<CameraController>() : null;
        cam = cameraController != null && cameraController.cam != null ? cameraController.cam : Camera.main;
        if (cameraController != null) cameraController.enabled = false;
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
        float lineWidth = cam.orthographicSize * 0.012f;
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

            GameObject marker = track.look != null ? CopyLook(track.look) : null;
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
        }

        HideRealRunners();
        BuildBar();
        Apply();
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

    void Update()
    {
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
}
