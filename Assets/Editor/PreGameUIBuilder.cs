using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UI = ModernUIBuilder;
using Menu = MenuUIBuilder;

// Builds the screens between the main menu and a race, in the same look as My Runners:
//  - the New competition page
//  - the Choose a runner screen in every competition scene, with the search and filters from My Runners
//  - the loading page (the prefab used between network scenes and the copy in each scene)
// It also makes the multiplayer page show the loading page while a lobby is created or joined.
// The lobby page is not touched.
// Run it again from Tools > O-Manager > Developer > Apply Pre-game UI after changing layout or colours here.
public static class PreGameUIBuilder
{
    const string NewCompetitionScene = "Assets/Scenes/NewCompetition.unity";
    const string MultiPlayerScene = "Assets/Scenes/MultiPlayer.unity";
    const string LoadingPrefabPath = "Assets/Prefabs/UI/Loading Screen.prefab";
    const string GameCardPath = "Assets/Prefabs/Object/Card.prefab";
    const string FlagPath = "Assets/UI/Game/control_flag.png";
    const string RingPath = "Assets/UI/Game/runner_ring.png";

    // Scenes with a loading page of their own, besides the competition scenes
    static readonly string[] MenuScenes =
    {
        "Assets/Scenes/MainMenu.unity",
        "Assets/Scenes/SingelPlayer.unity",
    };

    static readonly Vector2 Top = new Vector2(0.5f, 1f);
    static readonly Vector2 Bottom = new Vector2(0.5f, 0f);
    static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    static readonly Color Night = UI.Hex("0B1A12");

    // A card in the Choose a runner grid is as big as one in My Runners
    const float PickerCardScale = 0.585f;
    static readonly Vector2 PickerCell = new Vector2(258, 400);

    static Sprite flag, ring;

    [MenuItem("Tools/O-Manager/Developer (rarely needed)/Apply Pre-game UI (new competition, choose runner, loading page)", false, 104)]
    public static void ApplyAllMenu()
    {
        Debug.Log(ApplyAll());
    }

    public static string ApplyAll()
    {
        UI.log = new StringBuilder();
        if (EditorApplication.isPlaying)
        {
            return "Aborted: exit Play mode first.";
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                return "Aborted: scene '" + SceneManager.GetSceneAt(i).name + "' has unsaved changes. Save it first.";
            }
        }
        string startScene = SceneManager.GetActiveScene().path;

        UI.LoadAssets();
        Menu.LoadMenuAssets();
        flag = UI.ImportSprite(FlagPath, 0);
        ring = AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);

        AddCardHover();
        BuildLoadingPrefab();

        Scene scene = EditorSceneManager.OpenScene(NewCompetitionScene, OpenSceneMode.Single);
        BuildNewCompetition();
        BuildLoadingInScene(scene);
        Save(scene);

        scene = EditorSceneManager.OpenScene(MultiPlayerScene, OpenSceneMode.Single);
        GameObject loading = BuildLoadingInScene(scene);
        MultiPlayerHandler handler = Object.FindFirstObjectByType<MultiPlayerHandler>(FindObjectsInactive.Include);
        handler.loadingScreen = loading;
        EditorUtility.SetDirty(handler);
        Save(scene);

        foreach (string path in MenuScenes)
        {
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            BuildLoadingInScene(scene);
            Save(scene);
        }

        foreach (string path in GameScenes())
        {
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            BuildChooseRunner();
            BuildLoadingInScene(scene);
            Save(scene);
        }

        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(startScene))
        {
            EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
        }
        return UI.log.ToString();
    }

    static void Save(Scene scene)
    {
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        UI.log.AppendLine(scene.name + " saved=" + EditorSceneManager.SaveScene(scene));
    }

    // Every competition map: the scenes whose file name starts with IllernKartan
    static List<string> GameScenes()
    {
        List<string> scenes = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path).StartsWith("IllernKartan"))
            {
                scenes.Add(path);
            }
        }
        scenes.Sort();
        return scenes;
    }

    // ---------- New competition ----------

    static void BuildNewCompetition()
    {
        GameObject canvasObject = GameObject.Find("MainMenu");
        Transform canvas = canvasObject.transform;
        UI.SetupScaler(canvas);
        if (Camera.main != null)
        {
            Camera.main.backgroundColor = UI.Ink;
            EditorUtility.SetDirty(Camera.main);
        }
        ASyncLoader loader = Object.FindFirstObjectByType<ASyncLoader>(FindObjectsInactive.Include);

        // The two pictures that were the whole page are kept and framed as tiles
        Transform multi = Menu.FindDeep(canvas, "MultiPlayer");
        Transform single = Menu.FindDeep(canvas, "SingelPlayer");
        multi.SetParent(canvas, false);
        single.SetParent(canvas, false);
        foreach (string generated in new[] { "Art", "Shade", "Back", "Heading", "Subheading", "Tile multiplayer", "Tile single player", "Money pill" })
        {
            Transform old = canvas.Find(generated);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        Forest(canvas);
        BottomShade(canvas);

        Button back = Menu.NewButton(canvas, "Back", "Back", UI.ButtonKind.Light, new Vector2(170, 60), 28);
        UI.Place((RectTransform)back.transform, TopLeft, TopLeft, new Vector2(48, -48), new Vector2(170, 60));
        UI.AddDropShadow(back.gameObject, 6);
        UnityEventTools.AddStringPersistentListener(back.onClick, loader.LoadLevelBtn, "MainMenu");
        EditorUtility.SetDirty(back);

        Heading(canvas, "Heading", "New competition");
        Subheading(canvas, "Subheading", "CHOOSE HOW TO PLAY");

        Menu.Tile(canvas, multi, "Tile multiplayer", -250, "Race your friends online");
        Menu.Tile(canvas, single, "Tile single player", 250, "Run on your own");
        Menu.MoneyPill(canvas);
        UI.log.AppendLine("new competition page built");
    }

    // ---------- Choose a runner (in every competition scene) ----------

    static void BuildChooseRunner()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        GameObject canvasObject = GameObject.FindGameObjectWithTag("Canvas");
        Transform panel = canvasObject != null ? canvasObject.transform.Find("Choose runner") : null;
        if (panel == null)
        {
            UI.log.AppendLine("no Choose runner panel in " + sceneName);
            return;
        }
        Image back = panel.GetComponent<Image>();
        back.sprite = null;
        back.color = Night;
        back.raycastTarget = true;
        Forest(panel);
        Heading(panel, "Chose a runner text", "Choose a runner");
        TextMeshProUGUI count = Subheading(panel, "Subheading", "");

        foreach (string generated in new[] { "Filters", "Empty" })
        {
            Transform old = panel.Find(generated);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        RunnerFilterBar filters = Menu.BuildFilterBar(panel);

        // The grid the cards are put in, same measures as the one in My Runners
        Transform scroll = panel.Find("Scroll");
        UI.Stretch((RectTransform)scroll, 100, 366, 100, 36);
        Image scrollImage = scroll.GetComponent<Image>();
        scrollImage.sprite = UI.rounded;
        scrollImage.type = Image.Type.Sliced;
        scrollImage.pixelsPerUnitMultiplier = 1f;
        scrollImage.color = UIAnim.WithAlpha(UI.Ink, 0.42f);
        scrollImage.raycastTarget = true;
        Mask mask = scroll.GetComponent<Mask>();
        if (mask != null) mask.showMaskGraphic = true;
        ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 50;

        RectTransform content = (RectTransform)scroll.Find("Panel");
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = Top;
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        content.localScale = Vector3.one;
        scrollRect.content = content;
        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        grid.cellSize = PickerCell;
        grid.spacing = new Vector2(20, 20);
        grid.padding = new RectOffset(32, 32, 28, 28);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.constraint = GridLayoutGroup.Constraint.Flexible;
        ContentSizeFitter fit = UI.GetOrAdd<ContentSizeFitter>(content.gameObject);
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        LayoutElement contentLayout = content.GetComponent<LayoutElement>();
        if (contentLayout != null) Object.DestroyImmediate(contentLayout);

        TextMeshProUGUI empty = UI.Text(panel, "Empty", "No runners match your search.", 36, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        UI.Place(empty.rectTransform, UI.Center, UI.Center, new Vector2(0, -165), new Vector2(1000, 60));
        empty.gameObject.SetActive(false);

        // The cards are a prefab of their own size, so they are scaled to be as big as the ones in My Runners
        Transform cardInPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameCardPath).transform.Find("RunnerCard");
        float builtIn = cardInPrefab != null ? cardInPrefab.localScale.x : 1f;

        RunnerPickerUI picker = UI.GetOrAdd<RunnerPickerUI>(panel.gameObject);
        picker.filters = filters;
        picker.grid = content;
        picker.scroll = scrollRect;
        picker.countText = count;
        picker.emptyState = empty.gameObject;
        picker.cardScale = PickerCardScale / builtIn;
        EditorUtility.SetDirty(picker);

        // Shown while the other players are still choosing
        Transform waiting = canvasObject.transform.Find("WaitingForPlayersToConnect");
        if (waiting != null)
        {
            Image waitingBack = waiting.GetComponent<Image>();
            waitingBack.sprite = null;
            waitingBack.color = Night;
            Forest(waiting);
            Image shade = Picture(waiting, "Shade");
            shade.color = UIAnim.WithAlpha(Night, 0.6f);
            shade.transform.SetSiblingIndex(1);
            UI.Stretch(shade.rectTransform, 0, 0, 0, 0);
        }
        UI.log.AppendLine("choose runner built in " + sceneName + ", card scale " + picker.cardScale);
    }

    // The cards you pick from grow a little under the mouse
    static void AddCardHover()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GameCardPath);
        Transform card = root.transform.Find("RunnerCard");
        if (card != null)
        {
            UIHover hover = UI.GetOrAdd<UIHover>(root);
            hover.target = (RectTransform)card;
            hover.hoverScale = 1.05f;
            PrefabUtility.SaveAsPrefabAsset(root, GameCardPath);
        }
        PrefabUtility.UnloadPrefabContents(root);
    }

    // ---------- Loading page ----------

    static void BuildLoadingPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LoadingPrefabPath);
        BuildLoading(root);
        PrefabUtility.SaveAsPrefabAsset(root, LoadingPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        UI.log.AppendLine("loading page prefab built");
    }

    // The scene's own loading page is a root object that is switched off until it is needed
    static GameObject BuildLoadingInScene(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Loading Screen")
            {
                BuildLoading(root);
                return root;
            }
        }
        UI.log.AppendLine("no loading page in " + scene.name);
        return null;
    }

    static void BuildLoading(GameObject root)
    {
        Transform page = root.transform;
        Slider slider = root.GetComponentInChildren<Slider>(true);
        foreach (string generated in new[] { "Art", "Shade", "Contours", "Emblem", "Heading", "Message", "Percent", "Tip label", "Tip" })
        {
            Transform old = page.Find(generated);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        UI.GetOrAdd<CanvasScaler>(root);
        UI.SetupScaler(page);
        Image back = root.GetComponent<Image>();
        back.sprite = null;
        back.color = Night;
        back.raycastTarget = true;

        // Forest, darkened so the page reads as "wait" and the light text stands out
        Forest(page);
        Image shade = Picture(page, "Shade");
        shade.color = UIAnim.WithAlpha(Night, 0.9f);
        UI.Stretch(shade.rectTransform, 0, 0, 0, 0);
        RawImage contours = UI.GetOrAdd<RawImage>(UI.Child(page, "Contours").gameObject);
        contours.texture = Menu.topo != null ? Menu.topo.texture : null;
        contours.uvRect = new Rect(0, 0, 1.875f, 1.055f);
        contours.color = UIAnim.WithAlpha(UI.Cream, 0.025f);
        contours.raycastTarget = false;
        UI.Stretch(contours.rectTransform, 0, 0, 0, 0);

        Image emblem = Picture(page, "Emblem");
        emblem.sprite = flag;
        emblem.color = Color.white;
        UI.Place(emblem.rectTransform, UI.Center, UI.Center, new Vector2(0, 190), new Vector2(170, 170));
        emblem.rectTransform.localRotation = Quaternion.Euler(0, 0, 8f);
        UI.AddDropShadow(emblem.gameObject, 12);

        TextMeshProUGUI heading = UI.Text(page, "Heading", "LOADING", 110, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        heading.characterSpacing = 18;
        UI.Place(heading.rectTransform, UI.Center, UI.Center, new Vector2(0, 0), new Vector2(1400, 130));
        UI.ApplyShadow(heading);
        TextMeshProUGUI message = UI.Text(page, "Message", "Getting things ready...", 32, UIAnim.WithAlpha(UI.Cream, 0.85f), FontStyles.Normal, TextAlignmentOptions.Center);
        message.characterSpacing = 4;
        UI.Place(message.rectTransform, UI.Center, UI.Center, new Vector2(0, -92), new Vector2(1400, 44));

        // The bar is still an ordinary Slider, so the code that fills it works as before
        if (slider != null)
        {
            StyleLoadingBar(slider);
            slider.transform.SetAsLastSibling();
        }
        TextMeshProUGUI percent = UI.Text(page, "Percent", "", 26, UI.Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        UI.Place(percent.rectTransform, UI.Center, new Vector2(0f, 0.5f), new Vector2(410, -180), new Vector2(120, 40));

        TextMeshProUGUI tipLabel = UI.Text(page, "Tip label", "TIP", 20, UI.Accent, FontStyles.Bold, TextAlignmentOptions.Center);
        tipLabel.characterSpacing = 30;
        UI.Place(tipLabel.rectTransform, Bottom, Bottom, new Vector2(0, 118), new Vector2(600, 30));
        TextMeshProUGUI tip = UI.Text(page, "Tip", "A runner with high technique makes fewer mistakes.", 28, UI.Cream, FontStyles.Normal, TextAlignmentOptions.Center);
        UI.Place(tip.rectTransform, Bottom, Bottom, new Vector2(0, 70), new Vector2(1600, 44));

        LoadingScreenUI ui = UI.GetOrAdd<LoadingScreenUI>(root);
        ui.slider = slider;
        ui.emblem = emblem.rectTransform;
        ui.messageText = message;
        ui.percentText = percent;
        ui.tipText = tip;
        ui.contours = contours;
        EditorUtility.SetDirty(ui);
    }

    // Thin light track, orange fill, and a runner's circle at the front of it
    static void StyleLoadingBar(Slider slider)
    {
        RectTransform rect = (RectTransform)slider.transform;
        UI.Place(rect, UI.Center, UI.Center, new Vector2(0, -180), new Vector2(760, 34));
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        Image track = UI.GetOrAdd<Image>(UI.Child(rect, "Background").gameObject);
        Line(track, UIAnim.WithAlpha(UI.Cream, 0.3f));
        RectTransform area = (RectTransform)UI.Child(rect, "Fill Area");
        area.anchorMin = new Vector2(0, 0.5f);
        area.anchorMax = new Vector2(1, 0.5f);
        area.pivot = UI.Center;
        area.anchoredPosition = Vector2.zero;
        area.sizeDelta = new Vector2(0, 14);
        Image fill = UI.GetOrAdd<Image>(UI.Child(area, "Fill").gameObject);
        fill.sprite = UI.pill;
        fill.type = Image.Type.Sliced;
        fill.pixelsPerUnitMultiplier = 128f / 14f;
        fill.color = UI.Accent;
        fill.raycastTarget = false;
        fill.rectTransform.sizeDelta = Vector2.zero;

        RectTransform handleArea = (RectTransform)UI.Child(rect, "Handle Slide Area");
        UI.Stretch(handleArea, 0, 0, 0, 0);
        Image handle = UI.GetOrAdd<Image>(UI.Child(handleArea, "Handle").gameObject);
        handle.sprite = UI.pill;
        handle.type = Image.Type.Simple;
        handle.color = UI.Accent;
        handle.raycastTarget = false;
        handle.rectTransform.sizeDelta = new Vector2(34, 0);
        Image handleRing = Picture(handle.transform, "Ring");
        handleRing.sprite = ring;
        handleRing.color = Color.white;
        UI.Place(handleRing.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(49, 49));

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = null;
        slider.value = 0f;
        EditorUtility.SetDirty(slider);
    }

    static void Line(Image image, Color color)
    {
        image.sprite = UI.pill;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 128f / 14f;
        image.color = color;
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2(0, 0.5f);
        rect.anchorMax = new Vector2(1, 0.5f);
        rect.pivot = UI.Center;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0, 14);
    }

    // ---------- building blocks ----------

    static Image Picture(Transform parent, string name)
    {
        Image image = UI.GetOrAdd<Image>(UI.Child(parent, name).gameObject);
        image.raycastTarget = false;
        return image;
    }

    // The forest picture behind a whole screen, filling it without being stretched
    static void Forest(Transform parent)
    {
        Image art = Picture(parent, "Art");
        art.transform.SetAsFirstSibling();
        art.sprite = UI.background;
        art.type = Image.Type.Simple;
        art.color = Color.white;
        AspectRatioFitter fitter = UI.GetOrAdd<AspectRatioFitter>(art.gameObject);
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        if (UI.background != null) fitter.aspectRatio = UI.background.rect.width / UI.background.rect.height;
        UI.Stretch(art.rectTransform, 0, 0, 0, 0);
    }

    // Darker towards the bottom, as on the main menu
    static void BottomShade(Transform parent)
    {
        Image shade = Picture(parent, "Shade");
        shade.sprite = Menu.gradient;
        shade.color = UIAnim.WithAlpha(UI.Ink, 0.6f);
        shade.transform.SetSiblingIndex(1);
        RectTransform rect = shade.rectTransform;
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = Bottom;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0, 560);
    }

    static TextMeshProUGUI Heading(Transform parent, string name, string text)
    {
        TextMeshProUGUI heading = UI.Text(parent, name, text.ToUpperInvariant(), 76, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        heading.characterSpacing = 4;
        UI.Place(heading.rectTransform, Top, Top, new Vector2(0, -36), new Vector2(1300, 100));
        UI.ApplyShadow(heading);
        return heading;
    }

    static TextMeshProUGUI Subheading(Transform parent, string name, string text)
    {
        TextMeshProUGUI line = UI.Text(parent, name, text, 28, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        line.characterSpacing = 10;
        UI.Place(line.rectTransform, Top, Top, new Vector2(0, -136), new Vector2(1400, 40));
        UI.ApplyShadow(line);
        return line;
    }
}
