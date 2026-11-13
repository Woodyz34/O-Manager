using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the 3D view of a TV control.
//  - In a relay with TV controls (TvDirector, a TV production): while something is on TV the screen is shared, the
//    map (GPS) on the left half and the TV picture on the right, with the split times of the TV control. A click on
//    the picture makes it fill the screen. Mistakes are marked on the map while they happen.
//  - In the course builder ("View" on a TV control): straight to full screen, with three test runners passing.
// Full screen has the TV camera (stands beside the control and turns after the runners) and a free camera (drag to
// turn, right-drag or WASD to move, wheel to go nearer). Close or Escape goes back.
public class TvViewer : MonoBehaviour
{
    static TvViewer instance;

    // While the TV fills the screen, or the mouse is over the TV half, the map must not move with the mouse
    public static bool BlocksMapInput => instance != null && (instance.fullscreen || (instance.split && Input.mousePosition.x > Screen.width * 0.5f));

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

    RectTransform canvasRect;
    GameObject panel;
    RawImage panelImage;
    TextMeshProUGUI panelTitle, panelCaption, splitText;
    RectTransform splitBox;
    GameObject paceChip, captionChip;
    GameObject full;
    RawImage fullImage;
    RectTransform topBar;
    TextMeshProUGUI fullTitle, help, status;
    Button cameraButton;
    bool fullscreen;
    bool split;                 // the screen is shared between the map and the TV
    bool freeCamera;
    Vector3 lastMouse;
    bool dragging;
    Camera mapCamera;
    TvWorldView drawn;
    readonly List<TvDirector.Split> splits = new List<TvDirector.Split>();
    readonly List<(Vector3 position, string name)> mistakes = new List<(Vector3, string)>();
    readonly List<TextMeshProUGUI> mistakeLabels = new List<TextMeshProUGUI>();
    readonly Dictionary<string, float> mistakeSeen = new Dictionary<string, float>();
    readonly Dictionary<string, Vector3> mistakeAt = new Dictionary<string, Vector3>();
    readonly List<string> mistakeGone = new List<string>();

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
        viewer.canvasRect = (RectTransform)root.transform;
        viewer.Build(root.transform);
        instance = viewer;
        return viewer;
    }

    static TextMeshProUGUI Chip(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, out GameObject chip)
    {
        Image back = CourseUI.Panel(name, parent, CourseUI.Hex("10241A", 0.82f));
        CourseUI.Place(back.rectTransform, anchor, anchor, position, size);
        back.raycastTarget = false;
        chip = back.gameObject;
        TextMeshProUGUI text = CourseUI.Text("Text", back.transform, "", fontSize, Color.white, FontStyles.Bold);
        CourseUI.Stretch(text.rectTransform, 16, 16, 2, 2);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }

    void Build(Transform root)
    {
        // The TV half of a shared screen: the right half
        Image half = CourseUI.Panel("TV half", root, Color.black, false);
        half.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        half.rectTransform.anchorMax = Vector2.one;
        half.rectTransform.offsetMin = Vector2.zero;
        half.rectTransform.offsetMax = Vector2.zero;
        panel = half.gameObject;
        Button open = panel.AddComponent<Button>();
        open.transition = Selectable.Transition.None;
        open.onClick.AddListener(OpenFullscreen);
        panelImage = new GameObject("Picture", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        panelImage.transform.SetParent(panel.transform, false);
        CourseUI.Stretch(panelImage.rectTransform);
        panelImage.raycastTarget = false;
        // A thin line between the map and the TV
        Image line = CourseUI.Panel("Edge", panel.transform, CourseUI.Hex("10241A"), false);
        line.rectTransform.anchorMin = Vector2.zero;
        line.rectTransform.anchorMax = new Vector2(0f, 1f);
        line.rectTransform.pivot = new Vector2(0f, 0.5f);
        line.rectTransform.sizeDelta = new Vector2(4f, 0f);
        line.rectTransform.anchoredPosition = Vector2.zero;
        line.raycastTarget = false;
        panelTitle = Chip("Title", panel.transform, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(560f, 48f), 24, out _);
        panelCaption = Chip("Caption", panel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(640f, 52f), 24, out captionChip);
        panelCaption.alignment = TextAlignmentOptions.Center;
        captionChip.SetActive(false);
        Image box = CourseUI.Panel("Split times", panel.transform, CourseUI.Hex("10241A", 0.82f));
        splitBox = box.rectTransform;
        CourseUI.Place(splitBox, Vector2.zero, Vector2.zero, new Vector2(20f, 20f), new Vector2(430f, 60f));
        box.raycastTarget = false;
        splitText = CourseUI.Text("Text", box.transform, "", 22, Color.white);
        CourseUI.Stretch(splitText.rectTransform, 16, 16, 10, 10);
        splitText.alignment = TextAlignmentOptions.TopLeft;
        splitText.raycastTarget = false;
        panel.SetActive(false);

        // On the map half: the race runs at TV pace now
        TextMeshProUGUI pace = Chip("Pace", root, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(440f, 44f), 20, out paceChip);
        pace.text = "<color=#E8742A>ON TV</color>  ·  the race runs in real time";
        paceChip.SetActive(false);

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
        Button close = CourseUI.Button("Close", bar.transform, "Close", CourseUI.ButtonKind.Primary, CloseFullscreen, 22);
        CourseUI.Size(close, 56, 160);

        help = CourseUI.Text("Help", full.transform, "", 20, Color.white);
        CourseUI.Place(help.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(1400f, 34f));
        help.alignment = TextAlignmentOptions.Center;
        help.raycastTarget = false;
        status = CourseUI.Text("Status", full.transform, "", 30, Color.white, FontStyles.Bold);
        CourseUI.Place(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 80f));
        status.alignment = TextAlignmentOptions.Center;
        status.raycastTarget = false;
        full.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (previewView != null) Destroy(previewView.gameObject);
        SetSplit(false);
    }

    void OpenFullscreen()
    {
        fullscreen = true;
        full.SetActive(true);
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
    }

    void ToggleCamera()
    {
        freeCamera = !freeCamera;
        TvWorldView view = CurrentView;
        if (freeCamera && view != null) view.StartFreeCamera();
        RefreshButtons();
    }

    void RefreshButtons()
    {
        CourseUI.SetButtonText(cameraButton, freeCamera ? "TV camera" : "Free camera");
        help.text = freeCamera ? "Drag to turn  ·  right-drag or WASD to move  ·  wheel to go nearer" : "";
    }

    TvWorldView CurrentView => preview ? previewView : director != null ? director.View : null;

    // The map's camera: the local player's
    Camera MapCamera()
    {
        if (mapCamera != null && mapCamera.isActiveAndEnabled) return mapCamera;
        mapCamera = null;
        foreach (CameraController controller in FindObjectsByType<CameraController>(FindObjectsSortMode.None))
        {
            if (controller.cam != null && controller.cam.isActiveAndEnabled)
            {
                mapCamera = controller.cam;
                break;
            }
        }
        if (mapCamera == null) mapCamera = Camera.main;
        return mapCamera;
    }

    // Shares the screen: the map draws in the left half only, the TV has the right half
    void SetSplit(bool on)
    {
        split = on;
        Camera cam = on ? MapCamera() : mapCamera;
        if (cam == null) return;
        Rect rect = on ? new Rect(0f, 0f, 0.5f, 1f) : new Rect(0f, 0f, 1f, 1f);
        if (cam.rect != rect) cam.rect = rect;
    }

    void Update()
    {
        if (preview)
        {
            UpdatePreview();
            return;
        }
        if (director == null)
        {
            // The race is over
            Destroy(gameObject);
            return;
        }
        bool onAir = director.OnAir;
        TvWorldView view = director.View;
        if (!onAir && fullscreen) CloseFullscreen();
        SetSplit(onAir && !fullscreen);
        if (panel.activeSelf != split) panel.SetActive(split);
        if (paceChip.activeSelf != split) paceChip.SetActive(split);
        ShowMistakes();

        // Only the world on TV is drawn
        if (drawn != null && drawn != view) drawn.SetDrawing(false);
        drawn = view;
        if (!onAir) return;

        string caption = "";
        if (view == null) caption = director.Failed ? "No picture from this place" : "The camera is on its way ...";
        director.Splits(splits);
        if (view != null && !director.AtStart && !director.RunnerInPicture && splits.Count == 0) caption = "The first runners should be here any moment ...";
        string title = "<color=#E8742A>LIVE</color>  " + director.Title;
        if (fullscreen)
        {
            fullTitle.text = title + (freeCamera ? "  ·  free camera" : "");
            status.text = caption;
        }
        else
        {
            panelTitle.text = title + "  <size=16><color=#C7D6DA>click for full screen</color></size>";
            panelCaption.text = caption;
            if (captionChip.activeSelf != (caption != "")) captionChip.SetActive(caption != "");
            ShowSplits();
        }
        if (view == null)
        {
            (fullscreen ? fullImage : panelImage).color = Color.black;
            return;
        }
        view.SetDrawing(true);
        if (fullscreen)
        {
            fullImage.texture = view.Texture(Screen.width, Screen.height);
            fullImage.color = Color.white;
        }
        else
        {
            panelImage.texture = view.Texture(Screen.width / 2, Screen.height);
            panelImage.color = Color.white;
        }
        if (fullscreen && freeCamera) FreeCameraInput(view);
        else view.TvShot(director.Subject, Time.unscaledDeltaTime);
    }

    void UpdatePreview()
    {
        TvWorldView view = previewView;
        if (view == null) return;
        ShowTestRunners(view);
        view.SetDrawing(true);
        fullImage.texture = view.Texture(Screen.width, Screen.height);
        fullTitle.text = "TV control" + (freeCamera ? "  ·  free camera" : "");
        if (freeCamera) FreeCameraInput(view);
        else view.TvShot(testSubject, Time.unscaledDeltaTime);
    }

    // The split times at the TV control: the leader's time and how far behind the others are
    void ShowSplits()
    {
        bool any = splits.Count > 0;
        if (splitBox.gameObject.activeSelf != any) splitBox.gameObject.SetActive(any);
        if (!any) return;
        int rows = Mathf.Min(splits.Count, 8);
        var text = new System.Text.StringBuilder();
        text.Append("<b>Split times</b>");
        for (int i = 0; i < rows; i++)
        {
            TvDirector.Split s = splits[i];
            string time = i == 0 ? CompetitionFlow.FormatTime(s.seconds) : "+" + CompetitionFlow.FormatTime(s.seconds - splits[0].seconds);
            // The place in the team's colour, made lighter so it shows on the dark box
            string colour = ColorUtility.ToHtmlStringRGB(Color.Lerp(s.color, Color.white, 0.35f));
            text.Append("\n<b><color=#").Append(colour).Append(">").Append(i + 1).Append("</color></b><pos=34>")
                .Append(s.name).Append("<pos=300>").Append(time);
        }
        splitText.text = text.ToString();
        splitBox.sizeDelta = new Vector2(430f, 24f + 30f * (rows + 1));
    }

    // ---- Mistakes on the map: a label at every runner who is making a mistake, kept a moment so it is seen ----

    void ShowMistakes()
    {
        director.Mistakes(mistakes);
        float now = Time.unscaledTime;
        foreach (var m in mistakes)
        {
            mistakeSeen[m.name] = now;
            mistakeAt[m.name] = m.position;
        }
        mistakeGone.Clear();
        foreach (var pair in mistakeSeen) if (now - pair.Value > 1.5f) mistakeGone.Add(pair.Key);
        foreach (string name in mistakeGone)
        {
            mistakeSeen.Remove(name);
            mistakeAt.Remove(name);
        }
        Camera cam = MapCamera();
        int used = 0;
        if (cam != null && !fullscreen)
        {
            foreach (var pair in mistakeAt)
            {
                Vector3 screen = cam.WorldToScreenPoint(pair.Value);
                if (!cam.pixelRect.Contains(screen)) continue;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local)) continue;
                if (used == mistakeLabels.Count)
                {
                    TextMeshProUGUI made = Chip("Mistake", transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380f, 36f), 20, out GameObject chip);
                    ((RectTransform)chip.transform).pivot = new Vector2(0.5f, 0f);
                    made.color = CourseUI.Accent;
                    made.alignment = TextAlignmentOptions.Center;
                    chip.transform.SetAsFirstSibling();   // under the TV half
                    mistakeLabels.Add(made);
                }
                TextMeshProUGUI label = mistakeLabels[used++];
                label.text = "MISTAKE  ·  " + pair.Key;
                GameObject box = label.transform.parent.gameObject;
                ((RectTransform)box.transform).anchoredPosition = local + new Vector2(0f, 30f);
                if (!box.activeSelf) box.SetActive(true);
            }
        }
        for (int i = used; i < mistakeLabels.Count; i++)
        {
            GameObject box = mistakeLabels[i].transform.parent.gameObject;
            if (box.activeSelf) box.SetActive(false);
        }
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
