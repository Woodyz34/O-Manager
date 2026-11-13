using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the 3D view of a TV control.
//  - In a relay with TV controls (TvDirector): a small picture in the corner while runners pass a TV control; a click
//    makes it fill the screen.
//  - In the course builder ("View" on a TV control): straight to full screen, with three test runners passing.
// Full screen has the TV camera (stands beside the control and turns after the runners) and a free camera (drag to
// turn, right-drag or WASD to move, wheel to go nearer). Close or Escape goes back.
public class TvViewer : MonoBehaviour
{
    static TvViewer instance;

    // While the TV fills the screen, the map under it must not move with the mouse
    public static bool BlocksMapInput => instance != null && instance.fullscreen;

    public static bool CloseOnEscape()
    {
        if (instance == null || !instance.fullscreen) return false;
        instance.CloseFullscreen();
        return true;
    }

    TvDirector director;
    bool preview;
    TvWorldView previewView;
    bool previewFailed;
    float previewTime;

    GameObject tile;
    RawImage tileImage;
    TextMeshProUGUI tileTitle;
    GameObject full;
    RawImage fullImage;
    RectTransform topBar;
    TextMeshProUGUI fullTitle, help, status;
    Button cameraButton, nextButton;
    bool fullscreen;
    bool freeCamera;
    Vector3 lastMouse;
    bool dragging;

    const float TileWidth = 480f, TileHeight = 270f;

    public static void ForRace(TvDirector director)
    {
        if (instance != null) Destroy(instance.gameObject);
        TvViewer viewer = Make();
        viewer.director = director;
    }

    public static void OpenPreview(MapDefinition map, CoursePoint point)
    {
        if (instance != null) Destroy(instance.gameObject);
        TvViewer viewer = Make();
        viewer.preview = true;
        viewer.OpenFullscreen();
        viewer.status.text = "Building the 3D view of the control ...";
        viewer.BuildPreview(map, point);
    }

    async void BuildPreview(MapDefinition map, CoursePoint point)
    {
        TvWorldView view = null;
        try
        {
            view = await TvWorldView.Create(map, point);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("TvViewer: " + e);
        }
        if (this == null)
        {
            if (view != null) Destroy(view.gameObject);
            return;
        }
        previewView = view;
        previewFailed = view == null;
        status.text = previewFailed ? "This map has no 3D layer yet. Import the map again to get it." : "";
    }

    static TvViewer Make()
    {
        var root = new GameObject("TV view", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;   // above the race and the builder, under the pop-ups and the Esc menu
        ScreenSettings.Fit(root.GetComponent<CanvasScaler>());
        TvViewer viewer = root.AddComponent<TvViewer>();
        viewer.Build(root.transform);
        instance = viewer;
        return viewer;
    }

    void Build(Transform root)
    {
        // The corner picture, above the relay's bottom bar
        Image frame = CourseUI.Panel("TV tile", root, CourseUI.Hex("10241A", 0.9f));
        CourseUI.Place(frame.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 120f), new Vector2(TileWidth + 8f, TileHeight + 44f));
        tile = frame.gameObject;
        Button open = tile.AddComponent<Button>();
        open.transition = Selectable.Transition.None;
        open.onClick.AddListener(OpenFullscreen);
        tileImage = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        tileImage.transform.SetParent(tile.transform, false);
        CourseUI.Place(tileImage.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(TileWidth, TileHeight));
        tileImage.raycastTarget = false;
        tileTitle = CourseUI.Text("Title", tile.transform, "", 20, Color.white, FontStyles.Bold);
        CourseUI.Place(tileTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -4f), new Vector2(TileWidth - 20f, 34f));
        tileTitle.alignment = TextAlignmentOptions.MidlineLeft;
        tileTitle.raycastTarget = false;
        tile.SetActive(false);

        // Full screen
        Image back = CourseUI.Panel("TV full screen", root, Color.black, false);
        CourseUI.Stretch(back.rectTransform);
        full = back.gameObject;
        fullImage = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        fullImage.transform.SetParent(full.transform, false);
        CourseUI.Stretch(fullImage.rectTransform);
        fullImage.raycastTarget = false;

        Image bar = CourseUI.Panel("Top bar", full.transform, CourseUI.Hex("10241A", 0.75f), false);
        topBar = bar.rectTransform;
        topBar.anchorMin = new Vector2(0f, 1f);
        topBar.anchorMax = new Vector2(1f, 1f);
        topBar.pivot = new Vector2(0.5f, 1f);
        topBar.sizeDelta = new Vector2(0f, 76f);
        topBar.anchoredPosition = Vector2.zero;
        CourseUI.Row(bar.gameObject, 12, new RectOffset(24, 24, 10, 10)).childForceExpandWidth = false;
        fullTitle = CourseUI.Text("Title", bar.transform, "", 28, Color.white, FontStyles.Bold);
        fullTitle.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(fullTitle, 56, -1, 1);
        cameraButton = CourseUI.Button("Camera", bar.transform, "Free camera", CourseUI.ButtonKind.Secondary, ToggleCamera, 22);
        CourseUI.Size(cameraButton, 56, 220);
        nextButton = CourseUI.Button("Next TV", bar.transform, "Next TV", CourseUI.ButtonKind.Light, NextTv, 22);
        CourseUI.Size(nextButton, 56, 180);
        Button close = CourseUI.Button("Close", bar.transform, "Close", CourseUI.ButtonKind.Primary, CloseFullscreen, 22);
        CourseUI.Size(close, 56, 160);

        help = CourseUI.Text("Help", full.transform, "", 20, Color.white);
        CourseUI.Place(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1400f, 34f));
        help.raycastTarget = false;
        status = CourseUI.Text("Status", full.transform, "", 30, Color.white, FontStyles.Bold);
        CourseUI.Place(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 80f));
        status.raycastTarget = false;
        full.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (previewView != null) Destroy(previewView.gameObject);
    }

    void OpenFullscreen()
    {
        fullscreen = true;
        full.SetActive(true);
        tile.SetActive(false);
        RefreshButtons();
    }

    void CloseFullscreen()
    {
        if (preview)
        {
            Destroy(gameObject);
            return;
        }
        fullscreen = false;
        full.SetActive(false);
        freeCamera = false;
        dragging = false;
        director?.ReleaseChoice();
    }

    void ToggleCamera()
    {
        freeCamera = !freeCamera;
        TvWorldView view = CurrentView;
        if (freeCamera && view != null) view.StartFreeCamera();
        RefreshButtons();
    }

    void NextTv()
    {
        if (director == null || director.tvs.Count < 2) return;
        director.Choose((director.Shown + 1) % director.tvs.Count);
        freeCamera = false;
        RefreshButtons();
    }

    void RefreshButtons()
    {
        CourseUI.SetButtonText(cameraButton, freeCamera ? "TV camera" : "Free camera");
        nextButton.gameObject.SetActive(director != null && director.tvs.Count > 1);
        help.text = freeCamera ? "Drag to turn  ·  right-drag or WASD to move  ·  wheel to go nearer" : "";
    }

    TvWorldView CurrentView => preview ? previewView : director != null ? director.View : null;

    void Update()
    {
        if (!preview && director == null)
        {
            // The race is over
            Destroy(gameObject);
            return;
        }
        TvWorldView view = CurrentView;
        string title = preview ? "TV control" : director.Title;

        if (preview && view != null) ShowTestRunners(view);

        bool showTile = !preview && !fullscreen && view != null && director.ShowTile;
        if (tile.activeSelf != showTile) tile.SetActive(showTile);
        if (fullscreen && !preview && view == null) status.text = "Waiting for the camera ...";
        else if (fullscreen && !preview) status.text = director.OnAir ? "" : "Nobody at the control right now. The next runners are shown when they come.";

        if (view == null) return;
        bool draw = fullscreen || showTile;
        view.SetDrawing(draw);
        if (!draw) return;
        if (fullscreen)
        {
            fullImage.texture = view.Texture(Screen.width, Screen.height);
            fullTitle.text = title + (freeCamera ? "  ·  free camera" : "");
        }
        else
        {
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            tileImage.texture = view.Texture(Mathf.RoundToInt(TileWidth * scale), Mathf.RoundToInt(TileHeight * scale));
            tileTitle.text = "<color=#E8742A>●</color> " + title + "  <size=16><color=#C7D6DA>click to watch</color></size>";
        }

        Vector3? subject = preview ? testSubject : director.Subject;
        if (fullscreen && freeCamera) FreeCameraInput(view);
        else view.TvShot(subject, Time.unscaledDeltaTime);
    }

    void FreeCameraInput(TvWorldView view)
    {
        Vector3 mouse = Input.mousePosition;
        bool overBar = RectTransformUtility.RectangleContainsScreenPoint(topBar, mouse, null);
        bool pressed = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        if (pressed) lastMouse = mouse;
        if (pressed && !overBar) dragging = true;
        if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2)) dragging = false;
        Vector3 delta = mouse - lastMouse;
        lastMouse = mouse;
        Vector2 turn = Vector2.zero, move = Vector2.zero;
        if (dragging && Input.GetMouseButton(0)) turn = new Vector2(delta.x, delta.y);
        if (dragging && (Input.GetMouseButton(1) || Input.GetMouseButton(2))) move = new Vector2(-delta.x, -delta.y) * 0.0025f;
        float dt = Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move.y += dt;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move.y -= dt;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move.x += dt;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move.x -= dt;
        float zoom = overBar ? 0f : Input.mouseScrollDelta.y;
        view.FreeCamera(turn, move, zoom);
    }

    // ---- Test runners in the course builder: three runners pass through the control again and again ----

    Vector3? testSubject;
    readonly List<TvWorldView.RunnerAt> testRunners = new List<TvWorldView.RunnerAt>();
    static readonly (string name, Color color, float angle, float speed, float delay)[] Testers =
    {
        ("Test red", new Color32(0xd4, 0x35, 0x2b, 255), 20f, 4.5f, 0f),
        ("Test blue", new Color32(0x2a, 0x6f, 0xd6, 255), 200f, 4.3f, 3f),
        ("Test yellow", new Color32(0xe8, 0xc2, 0x1f, 255), 110f, 4.1f, 9f)
    };

    void ShowTestRunners(TvWorldView view)
    {
        previewTime += Time.unscaledDeltaTime;
        testRunners.Clear();
        foreach (var t in Testers)
        {
            const float Half = 60f;
            float cycle = 2f * Half / t.speed + 6f;
            float s = (previewTime - t.delay) % cycle;
            if (previewTime < t.delay || s > 2f * Half / t.speed) continue;
            float along = -Half + s * t.speed, a = t.angle * Mathf.Deg2Rad;
            // Straight through the control, a little to the side so the runner passes the flag
            float x = Mathf.Cos(a) * along + view.world.flagX * 0.8f, z = Mathf.Sin(a) * along + view.world.flagZ * 0.8f;
            testRunners.Add(new TvWorldView.RunnerAt { key = t.name, label = t.name, color = t.color, x = x, z = z });
        }
        testSubject = view.ShowRunners(testRunners);
    }
}
