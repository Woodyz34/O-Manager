using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The map in the runner's hand (run-yourself game mode). The competition scene's own 2D map with the course drawn on
// it is filmed by a camera of its own into a picture held up in front of the eyes. Left mouse lifts the map or lowers
// it, the scroll wheel zooms, the right mouse button held drags it, and Q / E show the leg before or after the one
// shown, framed to fit. A compass in the corner points north, so the map can be turned the right way by turning
// oneself. The runner's own position is never shown. The map shakes as RunPlayer.Shake says: running fast, downhill,
// in rough ground or a thicket it is hard to read; jogging, uphill or on a road it is steady.
public class RunMap : MonoBehaviour
{
    const float PanelWidth = 700f, PanelHeight = 860f;   // canvas units (1080 high)

    RunPlayer player;
    MapDefinition map;
    List<CoursePoint> points;
    float unitsPerMetre = 1f;

    Camera mapCam;
    RenderTexture texture;
    Canvas canvas;
    RectTransform paper;
    RawImage picture;
    RectTransform needle;
    int mask;

    public bool Up { get; private set; }
    float lift;                      // 0 down .. 1 up (animated)
    public int Leg { get; private set; }

    Vector3 lookAt, lookAtTarget;    // the 2D world point at the middle of the map
    float size, sizeTarget;          // the camera's orthographic size (2D world units)
    float minSize, maxSize;

    public static RunMap Create(RunPlayer player, MapDefinition map, List<CoursePoint> points, int cullingMask)
    {
        RunMap m = new GameObject("Run map").AddComponent<RunMap>();
        m.player = player;
        m.map = map;
        m.points = points;
        m.mask = cullingMask;
        m.Build();
        return m;
    }

    public Canvas Canvas => canvas;

    void Build()
    {
        CoursePoint a = points[0];
        Vector3 wa = map.ToWorld(a), wb = map.ToWorld(new CoursePoint(a.e + 100.0, a.n));
        unitsPerMetre = Mathf.Max(1e-5f, Mathf.Abs(wb.x - wa.x) / 100f);
        minSize = 40f * unitsPerMetre;
        maxSize = 2500f * unitsPerMetre;

        int pixelsHigh = Mathf.Clamp(Mathf.RoundToInt(PanelHeight * Screen.height / 1080f), 400, 1600);
        int pixelsWide = Mathf.RoundToInt(pixelsHigh * PanelWidth / PanelHeight);
        texture = new RenderTexture(pixelsWide, pixelsHigh, 16, RenderTextureFormat.ARGB32) { name = "Run map", antiAliasing = 2 };

        mapCam = new GameObject("Run map camera").AddComponent<Camera>();
        mapCam.transform.SetParent(transform, false);
        mapCam.orthographic = true;
        mapCam.cullingMask = mask;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = Color.white;
        mapCam.nearClipPlane = 0.01f;
        mapCam.farClipPlane = 100f;
        mapCam.targetTexture = texture;
        mapCam.allowHDR = false;
        mapCam.enabled = false;

        canvas = new GameObject("Run map canvas", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        canvas.transform.SetParent(transform, false);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        ScreenSettings.Fit(canvas.GetComponent<CanvasScaler>());

        // The paper: a white sheet with a thin edge
        Image sheet = CourseUI.Panel("Map", canvas.transform, new Color(0.96f, 0.95f, 0.92f), false);
        paper = sheet.rectTransform;
        CourseUI.Place(paper, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, -PanelHeight), new Vector2(PanelWidth + 16f, PanelHeight + 16f));
        sheet.raycastTarget = false;
        picture = new GameObject("Picture").AddComponent<RawImage>();
        picture.transform.SetParent(paper, false);
        picture.texture = texture;
        picture.raycastTarget = false;
        CourseUI.Stretch(picture.rectTransform, 8, 8, 8, 8);

        // The compass, in the top right corner of the map
        Image dial = CourseUI.Panel("Compass", paper, new Color(0.15f, 0.2f, 0.17f, 0.82f), true);
        CourseUI.Place(dial.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(110f, 110f));
        dial.raycastTarget = false;
        needle = CourseUI.Rect("Needle", dial.transform);
        CourseUI.Place(needle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 88f));
        Image north = CourseUI.Panel("North", needle, new Color(0.86f, 0.16f, 0.14f), false);
        CourseUI.Place(north.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(14f, 44f));
        north.raycastTarget = false;
        Image south = CourseUI.Panel("South", needle, Color.white, false);
        CourseUI.Place(south.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(14f, 44f));
        south.raycastTarget = false;
        TMPro.TextMeshProUGUI n = CourseUI.Text("N", dial.transform, "N", 20, Color.white, TMPro.FontStyles.Bold, TMPro.TextAlignmentOptions.Center);
        CourseUI.Place(n.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 2f), new Vector2(30f, 24f));
        n.raycastTarget = false;

        FrameLeg(0, true);
    }

    // ---- Legs ----

    public void FrameLeg(int leg, bool at = false)
    {
        Leg = Mathf.Clamp(leg, 0, points.Count - 2);
        Vector3 a = map.ToWorld(points[Leg]), b = map.ToWorld(points[Leg + 1]);
        lookAtTarget = (a + b) * 0.5f;
        float aspect = PanelWidth / PanelHeight;
        float half = Mathf.Max(Mathf.Abs(b.y - a.y), Mathf.Abs(b.x - a.x) / aspect) * 0.5f;
        sizeTarget = Mathf.Clamp(half * 1.25f + 90f * unitsPerMetre, minSize, maxSize);
        if (at)
        {
            lookAt = lookAtTarget;
            size = sizeTarget;
        }
    }

    public void SetUp(bool up)
    {
        Up = up;
        player.mapUp = up;
    }

    // ---- Every frame ----

    public void Tick(float dt, bool input)
    {
        if (input)
        {
            if (Input.GetMouseButtonDown(0)) SetUp(!Up);
            if (Up)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f) sizeTarget = Mathf.Clamp(sizeTarget * Mathf.Pow(0.87f, scroll), minSize, maxSize);
                if (Input.GetKeyDown(KeyCode.Q)) FrameLeg(Leg - 1);
                if (Input.GetKeyDown(KeyCode.E)) FrameLeg(Leg + 1);
            }
        }
        bool drag = input && Up && Input.GetMouseButton(1);
        player.lookHeld = drag;
        if (drag)
        {
            // The map follows the mouse: one screen pixel is this much of the 2D world
            float perPixel = 2f * size / Mathf.Max(1f, PanelHeight * Screen.height / 1080f);
            float k = 18f * perPixel;   // the locked mouse gives movement in its own units, not pixels
            Vector3 move = new Vector3(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"), 0f) * k;
            lookAtTarget -= move;
            lookAt -= move;
        }

        float follow = 1f - Mathf.Exp(-dt * 10f);
        lookAt = Vector3.Lerp(lookAt, lookAtTarget, follow);
        size = Mathf.Lerp(size, sizeTarget, follow);

        lift = Mathf.MoveTowards(lift, Up ? 1f : 0f, dt * 4.5f);
        float shown = Mathf.SmoothStep(0f, 1f, lift);
        mapCam.enabled = lift > 0.001f;
        paper.gameObject.SetActive(lift > 0.001f);
        if (!mapCam.enabled) return;

        mapCam.orthographicSize = size;
        mapCam.aspect = PanelWidth / PanelHeight;
        mapCam.transform.position = new Vector3(lookAt.x, lookAt.y, -10f);
        mapCam.transform.rotation = Quaternion.identity;

        // The hand moves with the steps, and more the harder the running is
        float shake = player.Shake + player.Jolt * 0.6f;
        float phase = player.StepPhase * Mathf.PI * 2f;
        float t = Time.time;
        float noiseX = Mathf.PerlinNoise(t * 7.3f, 0.3f) - 0.5f, noiseY = Mathf.PerlinNoise(0.7f, t * 6.1f) - 0.5f;
        float noiseR = Mathf.PerlinNoise(t * 5.2f, 2.9f) - 0.5f;
        Vector2 offset = new Vector2(Mathf.Sin(phase) * 22f + noiseX * 60f, Mathf.Abs(Mathf.Cos(phase)) * 26f + noiseY * 50f) * shake;
        float angle = (Mathf.Sin(phase) * 2.5f + noiseR * 9f) * shake;
        float y = Mathf.Lerp(-PanelHeight * 0.55f, PanelHeight * 0.5f + 30f, shown);
        paper.anchoredPosition = new Vector2(0f, y) + offset;
        paper.localRotation = Quaternion.Euler(0f, 0f, angle);

        // The needle points to north as the runner sees it: turned by the way the runner faces
        needle.localRotation = Quaternion.Euler(0f, 0f, player.yaw);
    }

    public void Hide()
    {
        SetUp(false);
        lift = 0f;
        mapCam.enabled = false;
        paper.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
    }
}
