using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The map in the runner's hand (run-yourself game mode). The competition scene's own 2D map with the course drawn on
// it is filmed by a camera of its own into a picture printed on a sheet of paper held in the runner's hand, in 3D in
// front of the eyes, with a thumb compass on the thumb (2026-11-19, Anders, after Catching Features: the hand and the
// thumb are seen, the arm is not; the map is turned so the leg runs from the bottom to the top; the compass needle
// always points to the real north, so the runner can turn until it lies along the map's north lines and run on the
// bearing). Left mouse lifts the map up or lowers it, with the hand bringing it up and taking it down. The scroll
// wheel zooms, the right mouse button held drags it, Q / E show the leg before or after the one shown.
// The map is held in the left hand unless the player picks the right (RunSettings.RightHand); standing still, the
// other hand takes hold too. The map shakes as RunPlayer.Shake says: running fast, downhill, in rough ground or a
// thicket it is hard to read; jogging, uphill or on a road it is steady.
// Everything in the hand is drawn by a camera of its own after the world, so trees never cut through the map.
public class RunMap : MonoBehaviour
{
    public const int HandLayer = 30;
    const float PaperW = 0.24f, PaperH = 0.30f;     // metres: the printed map
    const float Aspect = PaperW / PaperH;

    RunPlayer player;
    MapDefinition map;
    List<CoursePoint> points;
    float unitsPerMetre = 1f;

    Camera mapCam, handCam;
    RenderTexture texture;
    Transform rig, held, otherHand;
    Transform needle, compass;
    bool rightHand;
    int mask;

    public bool Up { get; private set; }
    float lift;                      // 0 down .. 1 up (animated)
    float otherGrip;                 // 0 .. 1: the second hand on the map (standing still)
    public int Leg { get; private set; }

    Vector3 lookAt, lookAtTarget;    // the 2D world point at the middle of the map
    float size, sizeTarget;          // the camera's orthographic size (2D world units)
    float turn, turnTarget;          // degrees the map is turned (the leg's bearing: the leg points up)
    float minSize, maxSize;

    static Mesh cube, capsule, cylinder;
    static Material skinMaterial;

    public static RunMap Create(RunPlayer player, MapDefinition map, List<CoursePoint> points, int cullingMask)
    {
        RunMap m = new GameObject("Run map").AddComponent<RunMap>();
        m.player = player;
        m.map = map;
        m.points = points;
        m.mask = cullingMask;
        m.rightHand = RunSettings.RightHand;
        m.Build();
        return m;
    }

    void Build()
    {
        CoursePoint a = points[0];
        Vector3 wa = map.ToWorld(a), wb = map.ToWorld(new CoursePoint(a.e + 100.0, a.n));
        unitsPerMetre = Mathf.Max(1e-5f, Mathf.Abs(wb.x - wa.x) / 100f);
        minSize = 40f * unitsPerMetre;
        maxSize = 2500f * unitsPerMetre;

        // The picture of the map: sharp enough to read at the size it is held
        int pixelsHigh = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.85f), 480, 1500);
        int pixelsWide = Mathf.RoundToInt(pixelsHigh * Aspect);
        texture = new RenderTexture(pixelsWide, pixelsHigh, 16, RenderTextureFormat.ARGB32) { name = "Run map", antiAliasing = 2 };

        mapCam = new GameObject("Run map camera").AddComponent<Camera>();
        mapCam.transform.SetParent(transform, false);
        mapCam.orthographic = true;
        mapCam.cullingMask = mask & ~(1 << HandLayer);
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = Color.white;
        mapCam.nearClipPlane = 0.01f;
        mapCam.farClipPlane = 100f;
        mapCam.targetTexture = texture;
        mapCam.allowHDR = false;
        mapCam.aspect = Aspect;
        mapCam.enabled = false;

        // The eyes for what is in the hand, drawn on top of the world
        player.cam.cullingMask &= ~(1 << HandLayer);
        handCam = new GameObject("Run hand camera").AddComponent<Camera>();
        handCam.transform.SetParent(player.cam.transform, false);
        handCam.clearFlags = CameraClearFlags.Depth;
        handCam.cullingMask = 1 << HandLayer;
        handCam.fieldOfView = player.cam.fieldOfView;
        handCam.nearClipPlane = 0.02f;
        handCam.farClipPlane = 4f;
        handCam.depth = player.cam.depth + 1;
        handCam.allowHDR = false;
        handCam.allowMSAA = player.cam.allowMSAA;
        handCam.useOcclusionCulling = false;

        rig = new GameObject("Hand rig").transform;
        rig.SetParent(handCam.transform, false);
        held = new GameObject("Map in hand").transform;
        held.SetParent(rig, false);
        BuildPaper();
        BuildHand(held, rightHand, true);
        otherHand = BuildHand(held, !rightHand, false);
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = HandLayer;

        FrameLeg(0, true);
        Place(0f);
    }

    // ---- The paper and the hands ----

    void BuildPaper()
    {
        // A world-space canvas, 1 unit = 1 mm, with the map picture on a white sheet
        Canvas canvas = new GameObject("Paper", typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = handCam;
        RectTransform rect = (RectTransform)canvas.transform;
        rect.SetParent(held, false);
        rect.sizeDelta = new Vector2(PaperW * 1000f + 12f, PaperH * 1000f + 12f);
        rect.localScale = Vector3.one * 0.001f;
        rect.localPosition = Vector3.zero;
        Image sheet = new GameObject("Sheet").AddComponent<Image>();
        sheet.transform.SetParent(rect, false);
        sheet.color = new Color(0.97f, 0.96f, 0.93f);
        sheet.raycastTarget = false;
        CourseUI.Stretch(sheet.rectTransform);
        RawImage picture = new GameObject("Map").AddComponent<RawImage>();
        picture.transform.SetParent(rect, false);
        picture.texture = texture;
        picture.raycastTarget = false;
        CourseUI.Stretch(picture.rectTransform, 6, 6, 6, 6);
    }

    // A hand holding the bottom corner of the map: the thumb on the front, the fingers behind, a cuff at the wrist.
    // The holding hand carries the thumb compass.
    Transform BuildHand(Transform parent, bool right, bool withCompass)
    {
        Transform hand = new GameObject(right ? "Right hand" : "Left hand").transform;
        hand.SetParent(parent, false);
        float side = right ? 1f : -1f;
        // The corner held: a little in from the edge, low on the sheet
        hand.localPosition = new Vector3(side * (PaperW * 0.5f - 0.035f), -PaperH * 0.5f + 0.045f, 0f);
        Color skin = new Color32(0xe0, 0xb1, 0x8f, 255), skinDark = new Color32(0xc8, 0x96, 0x74, 255);
        Color sleeve = new Color32(0x24, 0x3b, 0x2c, 255), band = new Color32(0xe8, 0x74, 0x2a, 255);

        // The fingers behind the sheet, showing below its edge
        for (int i = 0; i < 4; i++)
        {
            float x = side * (-0.012f + i * 0.017f) * -1f;
            Part(capsule, skinDark, hand, new Vector3(x, -0.035f - i * 0.004f, 0.012f), new Vector3(0.017f, 0.032f, 0.017f), Quaternion.Euler(0f, 0f, side * 8f));
        }
        // The palm and the wrist, below and behind the corner
        Part(cube, skin, hand, new Vector3(side * 0.006f, -0.075f, 0.02f), new Vector3(0.085f, 0.07f, 0.03f), Quaternion.Euler(-15f, 0f, side * 10f));
        Part(cylinder, sleeve, hand, new Vector3(side * 0.012f, -0.14f, 0.035f), new Vector3(0.07f, 0.05f, 0.06f), Quaternion.Euler(-20f, 0f, side * 12f));
        Part(cylinder, band, hand, new Vector3(side * 0.010f, -0.105f, 0.03f), new Vector3(0.073f, 0.008f, 0.063f), Quaternion.Euler(-20f, 0f, side * 12f));
        // The thumb on the front of the sheet, pointing up and in
        Part(capsule, skin, hand, new Vector3(-side * 0.004f, -0.01f, -0.012f), new Vector3(0.022f, 0.036f, 0.02f), Quaternion.Euler(0f, 0f, side * 28f));

        if (withCompass) BuildCompass(hand, side);
        return hand;
    }

    // A thumb compass: a clear baseplate on the thumb with a direction arrow, and the capsule with a needle that
    // always points to the real north
    void BuildCompass(Transform hand, float side)
    {
        compass = new GameObject("Thumb compass").transform;
        compass.SetParent(hand, false);
        compass.localPosition = new Vector3(-side * 0.012f, 0.012f, -0.03f);
        Part(cube, new Color(0.82f, 0.86f, 0.88f), compass, new Vector3(0f, 0.01f, 0.004f), new Vector3(0.044f, 0.07f, 0.004f), Quaternion.identity);
        Part(cube, new Color(0.9f, 0.2f, 0.15f), compass, new Vector3(0f, 0.04f, 0f), new Vector3(0.006f, 0.014f, 0.003f), Quaternion.identity);
        // The capsule: a dark ring, a pale face
        Part(cylinder, new Color(0.15f, 0.17f, 0.18f), compass, new Vector3(0f, 0.012f, -0.001f), new Vector3(0.038f, 0.0025f, 0.038f), Quaternion.Euler(90f, 0f, 0f));
        Part(cylinder, new Color(0.93f, 0.94f, 0.9f), compass, new Vector3(0f, 0.012f, -0.0035f), new Vector3(0.032f, 0.0012f, 0.032f), Quaternion.Euler(90f, 0f, 0f));
        // North lines on the face
        for (int i = -1; i <= 1; i++)
            Part(cube, new Color(0.85f, 0.25f, 0.2f), compass, new Vector3(i * 0.007f, 0.012f, -0.0048f), new Vector3(0.0008f, 0.026f, 0.0006f), Quaternion.identity);
        needle = new GameObject("Needle").transform;
        needle.SetParent(compass, false);
        needle.localPosition = new Vector3(0f, 0.012f, -0.0065f);
        Part(cube, new Color(0.9f, 0.12f, 0.1f), needle, new Vector3(0f, 0f, 0.0075f), new Vector3(0.0035f, 0.0012f, 0.015f), Quaternion.identity);
        Part(cube, Color.white, needle, new Vector3(0f, 0f, -0.0075f), new Vector3(0.0035f, 0.0012f, 0.015f), Quaternion.identity);
    }

    static void Part(Mesh shape, Color color, Transform parent, Vector3 position, Vector3 scale, Quaternion rotation)
    {
        Prepare();
        GameObject go = new GameObject("Part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = shape;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        Material m = new Material(skinMaterial) { color = color };
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // Unity's own cube, capsule and cylinder, white in every corner (the world's shader takes its colour from them)
    static void Prepare()
    {
        if (skinMaterial == null) skinMaterial = TvWorldView.NewMaterial(Color.white);
        if (cube == null) cube = White(Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
        if (capsule == null) capsule = White(Resources.GetBuiltinResource<Mesh>("Capsule.fbx"));
        if (cylinder == null) cylinder = White(Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"));
    }

    static Mesh White(Mesh source)
    {
        Mesh m = Instantiate(source);
        var colors = new Color32[m.vertexCount];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
        m.colors32 = colors;
        return m;
    }

    // ---- Legs ----

    static float Bearing(CoursePoint a, CoursePoint b) => Mathf.Atan2((float)(b.e - a.e), (float)(b.n - a.n)) * Mathf.Rad2Deg;

    public void FrameLeg(int leg, bool at = false)
    {
        Leg = Mathf.Clamp(leg, 0, points.Count - 2);
        Vector3 a = map.ToWorld(points[Leg]), b = map.ToWorld(points[Leg + 1]);
        lookAtTarget = (a + b) * 0.5f;
        // Turned so the leg runs from the bottom of the map to the top
        float bearing = Bearing(points[Leg], points[Leg + 1]);
        turnTarget = turn + Mathf.DeltaAngle(turn, bearing);
        float length = (b - a).magnitude;
        sizeTarget = Mathf.Clamp(length * 0.5f * 1.3f + 90f * unitsPerMetre, minSize, maxSize);
        if (at)
        {
            lookAt = lookAtTarget;
            size = sizeTarget;
            turn = turnTarget;
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
            // The map follows the mouse, in the map's own turned directions
            float k = 0.012f * size;
            Vector3 move = Quaternion.Euler(0f, 0f, -turn) * new Vector3(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"), 0f) * k;
            lookAtTarget -= move;
            lookAt -= move;
        }

        float follow = 1f - Mathf.Exp(-dt * 10f);
        lookAt = Vector3.Lerp(lookAt, lookAtTarget, follow);
        size = Mathf.Lerp(size, sizeTarget, follow);
        turn = Mathf.Lerp(turn, turnTarget, 1f - Mathf.Exp(-dt * 7f));

        lift = Mathf.MoveTowards(lift, Up ? 1f : 0f, dt * 2.6f);
        bool still = player.speed < 0.4f && !player.Fallen;
        otherGrip = Mathf.MoveTowards(otherGrip, still && Up ? 1f : 0f, dt * 3f);
        Place(dt);

        mapCam.enabled = lift > 0.001f;
        if (!mapCam.enabled) return;
        mapCam.orthographicSize = size;
        mapCam.transform.position = new Vector3(lookAt.x, lookAt.y, -10f);
        // The camera turned the leg's way round: the leg's direction is up in the picture
        mapCam.transform.rotation = Quaternion.Euler(0f, 0f, -turn);
    }

    // Where the hand holds the map: brought up in an arc from below, tilted back to be read, with the shake
    void Place(float dt)
    {
        float t = Mathf.SmoothStep(0f, 1f, lift);
        float side = rightHand ? 1f : -1f;
        Vector3 down = new Vector3(side * 0.16f, -0.42f, 0.30f), up = new Vector3(side * 0.025f, -0.075f, 0.40f);
        Vector3 position = Vector3.Lerp(down, up, t) + new Vector3(0f, -Mathf.Sin(Mathf.PI * t) * 0.03f, Mathf.Sin(Mathf.PI * t) * 0.05f);
        Quaternion rotation = Quaternion.Slerp(Quaternion.Euler(70f, side * 25f, side * -30f), Quaternion.Euler(28f, 0f, side * -3f), t);

        // The hand moves with the steps, and more the harder the running is
        float shake = player.Shake + player.Jolt * 0.6f;
        float phase = player.StepPhase * Mathf.PI * 2f;
        float time = Time.time;
        float nx = Mathf.PerlinNoise(time * 7.3f, 0.3f) - 0.5f, ny = Mathf.PerlinNoise(0.7f, time * 6.1f) - 0.5f;
        float nr = Mathf.PerlinNoise(time * 5.2f, 2.9f) - 0.5f;
        position += new Vector3(Mathf.Sin(phase) * 0.006f + nx * 0.016f, Mathf.Abs(Mathf.Cos(phase)) * 0.008f + ny * 0.014f, 0f) * shake;
        rotation *= Quaternion.Euler(ny * 6f * shake, nx * 4f * shake, (Mathf.Sin(phase) * 2f + nr * 8f) * shake);
        rig.localPosition = position;
        rig.localRotation = rotation;
        rig.gameObject.SetActive(lift > 0.001f);

        // Standing still, the other hand comes up to hold the other corner
        if (otherHand != null)
        {
            otherHand.gameObject.SetActive(otherGrip > 0.01f);
            float g = Mathf.SmoothStep(0f, 1f, otherGrip);
            otherHand.localPosition = new Vector3(-side * (PaperW * 0.5f - 0.035f), -PaperH * 0.5f + 0.045f - (1f - g) * 0.2f, 0f);
        }

        // The needle points to the real north, whichever way the map and the runner are turned
        if (needle != null)
        {
            Vector3 normal = needle.parent.TransformDirection(Vector3.back);
            Vector3 north = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (north.sqrMagnitude > 1e-6f) needle.rotation = Quaternion.LookRotation(north.normalized, -normal);
        }
    }

    public void Hide()
    {
        SetUp(false);
        lift = 0f;
        mapCam.enabled = false;
        rig.gameObject.SetActive(false);
    }

    // The 2D view for the replay: the hand camera off
    public void SetVisible(bool on)
    {
        if (handCam != null) handCam.enabled = on;
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
