using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps a runner easy to spot on the map:
//  - a ring around the circle, and a coloured copy of the circle that is drawn above the route line
//  - a name tag that always sits straight above the runner on screen, however the runner or the camera turns
//  - circle and name keep a readable size when the camera is zoomed far out
//  - no trail streak when the runner is moved to a new place (joining the lobby, starting a competition)
//  - hidden while the player draws the route (circle, name and trail), so it does not cover the start of the line
//  - smaller on sprint maps, which are drawn at a larger scale (1:4000), so it looks as big on the picture as in the forest
// The ring, the copy and the name tag are objects in NetworkPlayerPF. Tools > O-Manager > Developer > Apply Runner Marker builds them.
public class RunnerMarker : MonoBehaviour
{
    [Header("Circle")]
    public SpriteRenderer circle;          // the runner's own circle. It carries the colour.
    public Transform marker;               // holds the two below and grows when the camera is zoomed out
    public SpriteRenderer dot;             // copy of the circle in the same colour
    public SpriteRenderer ring;
    [Tooltip("The circle never looks smaller than this share of the screen height. 0 turns this off.")]
    public float minCircleShare = 0.02f;
    [Tooltip("How many times its real size the circle may grow at most.")]
    public float maxGrowth = 5f;
    [Tooltip("Size of the circle on sprint maps, compared with forest maps.")]
    public float sprintSize = 0.4f;

    [Header("Name")]
    public RectTransform nameCanvas;
    public RectTransform nameTag;
    public TMP_Text nameText;
    [Tooltip("Height of the name's letters as a share of the screen height.")]
    public float nameTextShare = 0.022f;
    [Tooltip("Space between the circle and the name, as a share of the screen height.")]
    public float nameGapShare = 0.006f;

    [Header("Trail")]
    public TrailRenderer trail;
    [Tooltip("Moving further than this in one frame counts as a teleport and leaves no trail.")]
    public float teleportDistance = 1f;
    [Tooltip("Seconds without a trail after the runner appears or a new scene has loaded.")]
    public float quietAfterSceneLoad = 1f;

    float baseTrailWidth = 1f;
    float trailQuiet;
    Vector3 lastPosition;
    Vector3 nameStartScale;
    bool ready;
    ManualPathDrawer drawer;
    bool lookedForDrawer;
    bool drawing;
    float sizeFactor = 1f;

    void Awake()
    {
        if (trail != null) baseTrailWidth = trail.widthMultiplier;
        if (nameCanvas != null) nameStartScale = nameCanvas.localScale;
        ready = true;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        lastPosition = transform.position;
        QuietTrail(quietAfterSceneLoad);
        UpdateSizeFactor();
    }

    // Sprint size only in a competition on a sprint map (the scene has a CourseLoader that has loaded the map)
    void UpdateSizeFactor()
    {
        bool sprint = CourseLoader.LoadedMap != null && CourseLoader.LoadedMap.IsSprint && FindAnyObjectByType<CourseLoader>() != null;
        sizeFactor = sprint ? Mathf.Clamp(sprintSize, 0.05f, 1f) : 1f;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit); not a new race
        if (mode == LoadSceneMode.Additive) return;
        // The object can be destroyed by another scene-loaded handler earlier in the same call (e.g. when the
        // network session shuts down as the scene changes); Unity then still calls this handler
        if (this == null)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            return;
        }
        // Everyone is put in a new place when a scene starts
        QuietTrail(quietAfterSceneLoad);
        UpdateSizeFactor();
    }

    // Runs after the runner has moved and turned this frame
    void LateUpdate()
    {
        if (!ready) return;

        if (!lookedForDrawer)
        {
            drawer = GetComponentInParent<ManualPathDrawer>();
            lookedForDrawer = true;
        }
        // Hidden while this runner's player draws, and also while you draw yourself: other players' runners standing
        // at the start were in the way of your line (not in relays, where team mates run while the next one draws)
        bool wasDrawing = drawing;
        drawing = (drawer != null && drawer.timeToDraw) || LocalDrawing();
        UpdateTrail(wasDrawing && !drawing);

        Camera cam = Camera.main;
        if (cam == null) return;

        // On sprint maps only the smaller copy shows; the full-size circle would stick out under it
        if (circle != null) circle.forceRenderingOff = drawing || sizeFactor < 1f;
        if (nameCanvas != null && nameCanvas.gameObject.activeSelf == drawing) nameCanvas.gameObject.SetActive(!drawing);
        // No trail while drawing (UpdateTrail); it starts again the moment the runner sets off

        float screenHeight = cam.orthographic ? cam.orthographicSize * 2f : 0f;   // in world units
        float growth = Growth(screenHeight);
        UpdateCircle(growth);
        UpdateName(cam, screenHeight);
    }

    static bool LocalDrawing()
    {
        if (RelayRace.Active != null) return false;
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.agentMovement == null) return false;
        ManualPathDrawer own = local.agentMovement.manualPathDrawer;
        return own != null && own.timeToDraw;
    }

    // ---------- circle ----------

    float Growth(float screenHeight)
    {
        if (circle == null || circle.sprite == null || minCircleShare <= 0f || screenHeight <= 0f)
        {
            return 1f;
        }
        float diameter = circle.sprite.bounds.size.x * transform.lossyScale.x * sizeFactor;
        if (diameter <= 0f)
        {
            return 1f;
        }
        return Mathf.Clamp(minCircleShare * screenHeight / diameter, 1f, Mathf.Max(1f, maxGrowth));
    }

    void UpdateCircle(float growth)
    {
        if (marker == null) return;
        float size = growth * sizeFactor;
        marker.localScale = new Vector3(size, size, 1f);
        // (on sprint maps the trail is thinner still, so it does not hide the narrow streets and passages)
        if (trail != null) trail.widthMultiplier = baseTrailWidth * size * (sizeFactor < 1f ? 0.6f : 1f);
        if (circle == null) return;

        // The copy follows the colour the player picked, and hides when the circle is hidden
        if (dot != null && dot.sharedMaterial != circle.sharedMaterial)
        {
            dot.sharedMaterial = circle.sharedMaterial;
        }
        bool show = circle.enabled && !drawing;
        if (marker.gameObject.activeSelf != show)
        {
            marker.gameObject.SetActive(show);
        }
    }

    // ---------- name ----------

    void UpdateName(Camera cam, float screenHeight)
    {
        if (nameCanvas == null) return;
        if (nameTag != null && nameText != null)
        {
            // No empty tag for a player without a name
            bool named = !string.IsNullOrWhiteSpace(nameText.text);
            if (nameTag.gameObject.activeSelf != named) nameTag.gameObject.SetActive(named);
        }
        float parentScale = transform.lossyScale.x;
        if (parentScale <= 0f) return;

        // World units per canvas pixel: the letters keep the same height on screen at every zoom
        float scale = nameStartScale.x * parentScale;
        if (screenHeight > 0f && nameTextShare > 0f && nameText != null && nameText.fontSize > 0f)
        {
            scale = nameTextShare * screenHeight / nameText.fontSize;
        }
        float local = scale / parentScale;
        nameCanvas.localScale = new Vector3(local, local, local);

        // Straight on screen, whatever way the runner is heading
        nameCanvas.rotation = cam.transform.rotation;

        float radius = parentScale * 0.5f;
        if (ring != null && ring.sprite != null)
        {
            radius = ring.sprite.bounds.extents.x * ring.transform.lossyScale.x;
        }
        float tagHeight = nameTag != null ? nameTag.rect.height * scale : 0f;
        float above = radius + nameGapShare * screenHeight + tagHeight * 0.5f;
        nameCanvas.position = transform.position + cam.transform.up * above - cam.transform.forward * 0.3f;
    }

    // ---------- trail ----------

    void UpdateTrail(bool drawingJustEnded)
    {
        if (trail == null) return;
        Vector3 position = transform.position;
        bool teleported = (position - lastPosition).sqrMagnitude > teleportDistance * teleportDistance;
        lastPosition = position;

        if (drawing)
        {
            QuietTrail(0.3f);
            return;
        }
        if (drawingJustEnded)
        {
            // The leg starts now: the trail starts right here at the runner, not some way into the leg. It used to wait
            // a moment after drawing, and by then the runner was already a bit along the leg.
            trailQuiet = 0f;
            trail.Clear();
            trail.emitting = true;
            return;
        }
        if (teleported)
        {
            // Moved to a new place in one jump: the streak there is dropped and the trail goes on from the new place.
            // This runs after the move and before the trail takes its new point, so nothing is lost.
            trail.Clear();
            return;
        }

        if (trailQuiet > 0f)
        {
            trailQuiet -= Time.unscaledDeltaTime;
            if (trailQuiet <= 0f)
            {
                trail.Clear();
                trail.emitting = true;
            }
        }
    }

    // Drops the trail drawn so far and draws none for a moment
    void QuietTrail(float seconds)
    {
        if (trail == null) return;
        trailQuiet = Mathf.Max(trailQuiet, seconds);
        trail.emitting = false;
        trail.Clear();
    }
}
