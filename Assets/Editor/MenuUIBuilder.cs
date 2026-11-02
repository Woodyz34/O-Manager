using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UI = ModernUIBuilder;

// Builds the main menu (home, pack shop, My Runners), the runner card and the pack opening show.
// Uses the same theme as ModernUIBuilder. Run it again from Tools > O-Manager > Apply Menu UI
// after changing layout or colours here.
public static class MenuUIBuilder
{
    const string Dir = "Assets/UI/Menu/";
    const string AudioDir = "Assets/Audio/Packs/";
    const string CardPrefabPath = "Assets/Prefabs/UI/RunnerCard.prefab";
    const string GameCardPath = "Assets/Prefabs/Object/Card.prefab";
    const string OpeningPath = "Assets/Resources/UI/PackOpening.prefab";
    const string MenuScene = "Assets/Scenes/MainMenu.unity";
    const string VignettePath = "Assets/UI/Modern/ui_vignette.png";
    const string ResultPath = "Temp/menuui_result.txt";

    public const float CardWidth = 430f;
    public const float CardHeight = 662f;

    static readonly Color Stage = UI.Hex("050B08");
    static readonly Vector2 Top = new Vector2(0.5f, 1f);
    static readonly Vector2 Bottom = new Vector2(0.5f, 0f);

    internal static Sprite glow, rays, spark, streak, beam, shine, gradient, topo, vignette;
    static Sprite cardMask, cardBack, pack, packTop, packBody;
    static readonly Sprite[] fronts = new Sprite[4];
    static Material additive;
    static GameObject cardPrefab;

    [MenuItem("Tools/O-Manager/Apply Menu UI (main menu, packs, runner cards)")]
    public static void ApplyAllMenu()
    {
        Debug.Log(ApplyAll());
    }

    [MenuItem("Tools/O-Manager/Testing/Add 1000 kr (in Play mode)")]
    public static void AddTestMoney()
    {
        if (Money.Instance == null)
        {
            Debug.Log("Start the game first (Play mode), then add the money.");
            return;
        }
        Money.Instance.AddMoney(1000);
    }

    [MenuItem("Tools/O-Manager/Testing/Add 100 000 kr (in Play mode)")]
    public static void AddLotsOfTestMoney()
    {
        if (Money.Instance == null)
        {
            Debug.Log("Start the game first (Play mode), then add the money.");
            return;
        }
        Money.Instance.AddMoney(100000);
    }

    [MenuItem("Tools/O-Manager/Testing/Reset runner collection")]
    public static void ResetCollection()
    {
        RunnerCollection.Reset();
        Debug.Log("Runner collection emptied. The next pack is the free first pack again.");
    }

    // Used when the builder is started from outside the Editor: runs on the next Editor tick
    // and leaves the outcome in a text file.
    public static void RunDeferred()
    {
        File.WriteAllText(ResultPath, "running");
        EditorApplication.delayCall += () =>
        {
            string result;
            try
            {
                result = "done\n" + ApplyAll();
            }
            catch (Exception exception)
            {
                result = "failed\n" + exception + "\n" + UI.log;
            }
            File.WriteAllText(ResultPath, result);
        };
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

        UI.LoadAssets();
        LoadMenuAssets();
        Directory.CreateDirectory("Assets/Resources/UI");
        Directory.CreateDirectory("Assets/Prefabs/UI");

        BuildCardPrefab();
        UpgradeGameCard();
        BuildOpeningPrefab();

        Scene scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
        BuildMenu();
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        UI.log.AppendLine(scene.name + " saved=" + EditorSceneManager.SaveScene(scene));

        AssetDatabase.SaveAssets();
        return UI.log.ToString();
    }

    // ---------- assets ----------

    internal static void LoadMenuAssets()
    {
        glow = Import("fx_glow");
        rays = Import("fx_rays");
        spark = Import("fx_spark");
        streak = Import("fx_streak");
        beam = Import("fx_beam");
        shine = Import("fx_shine");
        gradient = Import("fx_gradient");
        topo = Import("topo", false, true);
        cardMask = Import("card_mask", true);
        cardBack = Import("card_back", true);
        pack = Import("pack", true);
        packTop = Import("pack_top", true);
        packBody = Import("pack_body", true);
        fronts[(int)RunnerTier.Bronze] = Import("card_front_bronze", true);
        fronts[(int)RunnerTier.Silver] = Import("card_front_silver", true);
        fronts[(int)RunnerTier.Gold] = Import("card_front_gold", true);
        fronts[(int)RunnerTier.Elite] = Import("card_front_elite", true);
        vignette = UI.ImportSprite(VignettePath, 0);

        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Dir + "UIAdditive.shader");
        string materialPath = Dir + "UI Additive.mat";
        additive = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (additive == null)
        {
            additive = new Material(shader);
            AssetDatabase.CreateAsset(additive, materialPath);
        }
        else if (additive.shader != shader)
        {
            additive.shader = shader;
            EditorUtility.SetDirty(additive);
        }
        UI.log.AppendLine("menu assets glow=" + (glow != null) + " fronts=" + (fronts[3] != null) + " pack=" + (pack != null) + " additive=" + (shader != null));
    }

    // Big pictures get mipmaps so they stay smooth when a card is drawn small or tilted
    static Sprite Import(string name, bool large = false, bool repeat = false)
    {
        string path = Dir + name + ".png";
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
        }
        if (importer == null)
        {
            UI.log.AppendLine("missing " + path);
            return null;
        }
        TextureWrapMode wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        TextureImporterCompression compression = large ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single
            || importer.mipmapEnabled != large || importer.wrapMode != wrap || importer.textureCompression != compression)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = large;
            importer.alphaIsTransparency = true;
            importer.filterMode = large ? FilterMode.Trilinear : FilterMode.Bilinear;
            importer.wrapMode = wrap;
            importer.textureCompression = compression;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static AudioClip Clip(string name)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + name + ".wav");
        if (clip == null) UI.log.AppendLine("missing sound " + name);
        return clip;
    }

    // ---------- the runner card ----------

    static void BuildCardPrefab()
    {
        GameObject root = new GameObject("RunnerCard", typeof(RectTransform));
        root.layer = 5;
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(CardWidth, CardHeight);
        RunnerCardView view = root.AddComponent<RunnerCardView>();
        Color ink = RunnerCards.TierInk(RunnerTier.Gold);

        Image glowImage = Fx(root.transform, "Glow", glow, UIAnim.WithAlpha(RunnerCards.TierColor(RunnerTier.Gold), 0f));
        UI.Place(glowImage.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(CardWidth * 2.1f, CardHeight * 1.7f));

        // The card's shape. Everything inside is cut to it.
        Image shade = Img(root.transform, "Shape", cardMask, RunnerCards.TierShade(RunnerTier.Gold));
        UI.Stretch(shade.rectTransform, 0, 0, 0, 0);
        shade.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        Transform inside = shade.transform;

        Image photo = Img(inside, "Photo", null, Color.white);
        UI.Place(photo.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(2, -46), new Vector2(292, 448));

        Image front = Img(inside, "Front", fronts[(int)RunnerTier.Gold], Color.white);
        UI.Stretch(front.rectTransform, 0, 0, 0, 0);

        Vector2 topLeft = new Vector2(0, 1);
        TextMeshProUGUI overall = Label(inside, "Overall", "88", 96, ink, TextAlignmentOptions.Center);
        UI.Place(overall.rectTransform, topLeft, UI.Center, new Vector2(84, -130), new Vector2(160, 104));
        TextMeshProUGUI klass = Label(inside, "Class", "H21", 30, ink, TextAlignmentOptions.Center);
        klass.characterSpacing = 6;
        UI.Place(klass.rectTransform, topLeft, UI.Center, new Vector2(84, -198), new Vector2(160, 36));
        Image line1 = Line(inside, "Line 1", topLeft, new Vector2(84, -224), new Vector2(64, 2), ink);
        Image flag = Img(inside, "Flag", null, Color.white);
        UI.Place(flag.rectTransform, topLeft, UI.Center, new Vector2(84, -260), new Vector2(76, 48));
        Image line2 = Line(inside, "Line 2", topLeft, new Vector2(84, -296), new Vector2(64, 2), ink);
        Image club = Img(inside, "Club", null, Color.white);
        club.preserveAspect = true;
        UI.Place(club.rectTransform, topLeft, UI.Center, new Vector2(84, -340), new Vector2(70, 70));

        TextMeshProUGUI runnerName = Label(inside, "Name", "RUNNER NAME", 40, ink, TextAlignmentOptions.Center);
        UI.Place(runnerName.rectTransform, Top, UI.Center, new Vector2(0, -425), new Vector2(366, 56));
        runnerName.enableAutoSizing = true;
        runnerName.fontSizeMin = 20;
        runnerName.fontSizeMax = 40;
        runnerName.overflowMode = TextOverflowModes.Truncate;
        Image divider = Line(inside, "Divider", Top, new Vector2(0, -459), new Vector2(330, 2), ink);

        TextMeshProUGUI speed = Label(inside, "Speed", "80", 58, ink, TextAlignmentOptions.Center);
        UI.Place(speed.rectTransform, Top, UI.Center, new Vector2(-92, -498), new Vector2(160, 64));
        TextMeshProUGUI speedLabel = Label(inside, "Speed label", "SPEED", 18, ink, TextAlignmentOptions.Center);
        speedLabel.characterSpacing = 12;
        UI.Place(speedLabel.rectTransform, Top, UI.Center, new Vector2(-92, -541), new Vector2(180, 26));
        TextMeshProUGUI technique = Label(inside, "Technique", "99", 58, ink, TextAlignmentOptions.Center);
        UI.Place(technique.rectTransform, Top, UI.Center, new Vector2(92, -498), new Vector2(160, 64));
        TextMeshProUGUI techniqueLabel = Label(inside, "Technique label", "TECHNIQUE", 18, ink, TextAlignmentOptions.Center);
        techniqueLabel.characterSpacing = 12;
        UI.Place(techniqueLabel.rectTransform, Top, UI.Center, new Vector2(92, -541), new Vector2(180, 26));
        Image split = Line(inside, "Split", Top, new Vector2(0, -514), new Vector2(2, 78), ink);

        TextMeshProUGUI tierText = Label(inside, "Tier", "GOLD", 17, ink, TextAlignmentOptions.Center);
        tierText.characterSpacing = 18;
        UI.Place(tierText.rectTransform, Top, UI.Center, new Vector2(0, -586), new Vector2(220, 24));

        Image shineImage = Fx(inside, "Shine", shine, new Color(1, 1, 1, 0.55f));
        UI.Place(shineImage.rectTransform, UI.Center, UI.Center, new Vector2(-420, 0), new Vector2(320, 900));
        shineImage.gameObject.SetActive(false);

        view.shade = shade;
        view.front = front;
        view.tierFronts = (Sprite[])fronts.Clone();
        view.glow = glowImage;
        view.photo = photo;
        view.flag = flag;
        view.club = club;
        view.overallText = overall;
        view.klassText = klass;
        view.nameText = runnerName;
        view.speedText = speed;
        view.techniqueText = technique;
        view.tierText = tierText;
        view.inked = new Graphic[] { overall, klass, runnerName, speed, technique };
        view.inkedLabels = new Graphic[] { speedLabel, techniqueLabel, tierText };
        view.inkedSoft = new Graphic[] { line1, line2, divider, split };
        view.shine = shineImage.rectTransform;

        // A sample runner, so the prefab shows what a finished card looks like
        view.Show(new CardData("Gustav Bergman", 80f, 99f,
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Emblem/Flaggor/Sweden.gif"),
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Emblem/Klubbar/IKHP Huskvarna Idrottsklubb.png"), "H21", true,
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Profilbilder/Gustav Bergman.jpg")));

        cardPrefab = PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        UI.log.AppendLine("runner card prefab built");
    }

    // The card you pick a runner from in a competition gets the new look.
    // Its old texts and pictures are switched off, not deleted.
    static void UpgradeGameCard()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GameCardPath);
        CardDisplay display = root.GetComponent<CardDisplay>();
        Transform previous = root.transform.Find("RunnerCard");
        if (previous != null)
        {
            UnityEngine.Object.DestroyImmediate(previous.gameObject);
        }
        foreach (Transform child in root.transform)
        {
            if (child.name != "Button")
            {
                child.gameObject.SetActive(false);
            }
        }
        Image outline = root.GetComponent<Image>();
        if (outline != null) outline.enabled = false;

        RunnerCardView view = AddCard(root.transform, Vector2.zero, 1f);
        view.transform.SetAsFirstSibling();
        Rect size = ((RectTransform)root.transform).rect;
        float scale = Mathf.Min(size.width / CardWidth, size.height / CardHeight);
        view.transform.localScale = new Vector3(scale, scale, 1f);
        display.view = view;

        // The card grows a little under the mouse on the Choose a runner screen
        UIHover hover = UI.GetOrAdd<UIHover>(root);
        hover.target = (RectTransform)view.transform;
        hover.hoverScale = 1.05f;

        PrefabUtility.SaveAsPrefabAsset(root, GameCardPath);
        PrefabUtility.UnloadPrefabContents(root);
        UI.log.AppendLine("in-game card prefab upgraded, scale " + scale);
    }

    static RunnerCardView AddCard(Transform parent, Vector2 position, float scale)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab, parent);
        instance.name = "RunnerCard";
        RectTransform rect = (RectTransform)instance.transform;
        UI.Place(rect, UI.Center, UI.Center, position, new Vector2(CardWidth, CardHeight));
        rect.localScale = new Vector3(scale, scale, 1f);
        return instance.GetComponent<RunnerCardView>();
    }

    // ---------- the pack opening ----------

    static void BuildOpeningPrefab()
    {
        GameObject root = new GameObject("PackOpening");
        root.transform.position = new Vector3(0, -500, 0);
        PackOpening show = root.AddComponent<PackOpening>();

        AudioSource sfx = root.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        AudioSource loop = root.AddComponent<AudioSource>();
        loop.playOnAwake = false;
        loop.loop = true;
        loop.clip = Clip("drone");

        GameObject cameraObject = new GameObject("Stage Camera");
        cameraObject.transform.SetParent(root.transform, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.fieldOfView = 45f;
        camera.depth = 20;
        camera.cullingMask = 1 << 5;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f;

        GameObject canvasObject = new GameObject("Stage", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasObject.layer = 5;
        canvasObject.transform.SetParent(root.transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 10f;
        canvas.sortingOrder = 100;
        UI.SetupScaler(canvasObject.transform);
        Transform stage = canvasObject.transform;

        Image backdrop = Img(stage, "Backdrop", null, Stage, true);
        UI.Stretch(backdrop.rectTransform, 0, 0, 0, 0);
        Image contours = Img(stage, "Contours", topo, UIAnim.WithAlpha(UI.Forest, 0.13f));
        contours.type = Image.Type.Tiled;
        UI.Stretch(contours.rectTransform, 0, 0, 0, 0);

        Image beamLeft = Fx(stage, "Beam left", beam, new Color(1, 1, 1, 0));
        UI.Place(beamLeft.rectTransform, Top, Top, new Vector2(-640, 70), new Vector2(560, 1500));
        Image beamRight = Fx(stage, "Beam right", beam, new Color(1, 1, 1, 0));
        UI.Place(beamRight.rectTransform, Top, Top, new Vector2(640, 70), new Vector2(560, 1500));
        Image stageRays = Fx(stage, "Rays", rays, new Color(1, 1, 1, 0));
        UI.Place(stageRays.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(1900, 1900));
        Image stageGlow = Fx(stage, "Glow", glow, new Color(1, 1, 1, 0));
        UI.Place(stageGlow.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(1500, 1500));
        Image dark = Img(stage, "Vignette", vignette, new Color(0, 0, 0, 0.9f));
        UI.Stretch(dark.rectTransform, 0, 0, 0, 0);

        UIParticles dust = Particles(stage, "Dust", glow);
        dust.streamRate = 16f;
        dust.stream = new ParticleBurst
        {
            origin = Vector2.zero, spread = new Vector2(960, 540), angleMin = 60, angleMax = 120,
            speedMin = 8, speedMax = 45, sizeMin = 6, sizeMax = 22, endSize = 0.6f, lifeMin = 2.5f, lifeMax = 5.5f,
            gravity = -4f, drag = 0f, spin = 0f, twinkle = 0.6f,
            colorA = new Color(1f, 0.95f, 0.8f, 0.5f), colorB = new Color(1f, 1f, 1f, 0.2f)
        };

        RectTransform shaker = Box(stage, "Shaker");
        UI.Stretch(shaker, 0, 0, 0, 0);

        // The pack
        RectTransform packRoot = Box(shaker, "Pack");
        UI.Place(packRoot, UI.Center, UI.Center, Vector2.zero, new Vector2(470, 683));
        Image body = Img(packRoot, "Body", packBody, Color.white, true);
        UI.Stretch(body.rectTransform, 0, 0, 0, 0);
        Button packButton = body.gameObject.AddComponent<Button>();
        packButton.transition = Selectable.Transition.None;
        Image top = Img(packRoot, "Top", packTop, Color.white);
        UI.Stretch(top.rectTransform, 0, 0, 0, 0);
        Image seam = Fx(packRoot, "Seam glow", glow, new Color(1, 1, 1, 0));
        UI.Place(seam.rectTransform, UI.Center, UI.Center, new Vector2(0, 276), new Vector2(680, 130));

        // Country
        RectTransform flagGroup = Box(shaker, "Flag");
        UI.Place(flagGroup, UI.Center, UI.Center, Vector2.zero, new Vector2(416, 266));
        CanvasGroup flagCanvas = flagGroup.gameObject.AddComponent<CanvasGroup>();
        Image flagGlow = Fx(flagGroup, "Glow", glow, new Color(1, 1, 1, 0.5f));
        UI.Place(flagGlow.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(1000, 760));
        Image flagBorder = Img(flagGroup, "Border", UI.rounded, UI.Cream);
        flagBorder.type = Image.Type.Sliced;
        flagBorder.pixelsPerUnitMultiplier = 3f;
        UI.Stretch(flagBorder.rectTransform, 0, 0, 0, 0);
        Image flagImage = Img(flagGroup, "Picture", null, Color.white);
        UI.Stretch(flagImage.rectTransform, 8, 8, 8, 8);
        TextMeshProUGUI country = Label(shaker, "Country", "", 84, UI.Cream, TextAlignmentOptions.Center);
        UI.Place(country.rectTransform, UI.Center, UI.Center, new Vector2(0, -160), new Vector2(1800, 104));
        UI.ApplyShadow(country);

        // Stats
        RectTransform stats = Box(shaker, "Stats");
        UI.Place(stats, UI.Center, UI.Center, Vector2.zero, new Vector2(1240, 560));
        RectTransform meta = Box(stats, "Class and club");
        UI.Place(meta, UI.Center, UI.Center, new Vector2(0, 225), new Vector2(520, 110));
        CanvasGroup metaCanvas = meta.gameObject.AddComponent<CanvasGroup>();
        Image clubImage = Img(meta, "Club", null, Color.white);
        clubImage.preserveAspect = true;
        UI.Place(clubImage.rectTransform, UI.Center, UI.Center, new Vector2(-80, 0), new Vector2(100, 100));
        TextMeshProUGUI klass = Label(meta, "Class", "H21", 70, UI.Cream, TextAlignmentOptions.MidlineLeft);
        UI.Place(klass.rectTransform, UI.Center, UI.Center, new Vector2(95, 0), new Vector2(230, 90));

        TextMeshProUGUI speedValue, techniqueValue, overallValue, speedLabel, techniqueLabel, overallLabel;
        CanvasGroup speedBlock = StatBlock(stats, "Speed", "SPEED", new Vector2(-400, -20), 180, out speedValue, out speedLabel);
        CanvasGroup techniqueBlock = StatBlock(stats, "Technique", "TECHNIQUE", new Vector2(400, -20), 180, out techniqueValue, out techniqueLabel);
        CanvasGroup overallBlock = StatBlock(stats, "Overall", "OVERALL", new Vector2(0, -20), 250, out overallValue, out overallLabel);
        overallLabel.color = UI.Cream;
        Image ring = Fx(overallBlock.transform, "Glow", glow, new Color(1, 1, 1, 0.5f));
        ring.transform.SetAsFirstSibling();
        UI.Place(ring.rectTransform, UI.Center, UI.Center, new Vector2(0, 10), new Vector2(640, 640));

        // Photo and name
        RectTransform portraitGroup = Box(shaker, "Portrait");
        UI.Place(portraitGroup, UI.Center, UI.Center, Vector2.zero, new Vector2(352, 540));
        CanvasGroup portraitCanvas = portraitGroup.gameObject.AddComponent<CanvasGroup>();
        Image portraitGlow = Fx(portraitGroup, "Glow", glow, new Color(1, 1, 1, 0.4f));
        UI.Place(portraitGlow.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(1100, 1200));
        Image frame = Img(portraitGroup, "Frame", UI.rounded, Color.white);
        frame.type = Image.Type.Sliced;
        frame.pixelsPerUnitMultiplier = 3f;
        UI.Stretch(frame.rectTransform, -8, -8, -8, -8);
        Image inner = Img(portraitGroup, "Behind photo", null, Stage);
        UI.Stretch(inner.rectTransform, 0, 0, 0, 0);
        Image portrait = Img(portraitGroup, "Photo", null, Color.white);
        portrait.type = Image.Type.Filled;
        portrait.fillMethod = Image.FillMethod.Vertical;
        portrait.fillOrigin = (int)Image.OriginVertical.Bottom;
        UI.Stretch(portrait.rectTransform, 0, 0, 0, 0);
        Image scan = Fx(portraitGroup, "Scan line", streak, new Color(1, 1, 1, 0));
        UI.Place(scan.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(780, 90));

        TextMeshProUGUI runnerName = Label(shaker, "Name", "", 118, UI.Cream, TextAlignmentOptions.Center);
        UI.Place(runnerName.rectTransform, UI.Center, UI.Center, new Vector2(0, -335), new Vector2(1800, 150));
        runnerName.enableAutoSizing = true;
        runnerName.fontSizeMin = 60;
        runnerName.fontSizeMax = 118;
        UI.ApplyShadow(runnerName);

        // The finished card
        RectTransform holder = Box(shaker, "Card");
        UI.Place(holder, UI.Center, UI.Center, Vector2.zero, new Vector2(CardWidth, CardHeight));
        Image cardRays = Fx(holder, "Rays", rays, new Color(1, 1, 1, 0));
        UI.Place(cardRays.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(1750, 1750));
        Image back = Img(holder, "Back", cardBack, Color.white);
        UI.Stretch(back.rectTransform, 0, 0, 0, 0);
        RectTransform face = Box(holder, "Face");
        UI.Stretch(face, 0, 0, 0, 0);
        CanvasGroup faceCanvas = face.gameObject.AddComponent<CanvasGroup>();
        RunnerCardView card = AddCard(face, Vector2.zero, 1f);

        // Light and text above everything
        UIParticles sparks = Particles(stage, "Sparks", spark);
        Image pillar = Fx(stage, "Pillar", glow, new Color(1, 1, 1, 0));
        UI.Place(pillar.rectTransform, UI.Center, UI.Center, new Vector2(0, 430), new Vector2(380, 1700));
        Image flare = Fx(stage, "Flare", streak, new Color(1, 1, 1, 0));
        UI.Place(flare.rectTransform, UI.Center, UI.Center, Vector2.zero, new Vector2(1500, 170));
        Image flash = Img(stage, "Flash", null, new Color(1, 1, 1, 0));
        UI.Stretch(flash.rectTransform, 0, 0, 0, 0);

        TextMeshProUGUI title = Label(stage, "Title", "NEW RUNNER!", 66, UI.Cream, TextAlignmentOptions.Center);
        title.characterSpacing = 12;
        UI.Place(title.rectTransform, UI.Center, UI.Center, new Vector2(0, 480), new Vector2(1700, 84));
        UI.ApplyShadow(title);
        TextMeshProUGUI subtitle = Label(stage, "Subtitle", "GOLD", 32, UI.Cream, TextAlignmentOptions.Center);
        subtitle.characterSpacing = 8;
        UI.Place(subtitle.rectTransform, UI.Center, UI.Center, new Vector2(0, 420), new Vector2(1700, 46));
        TextMeshProUGUI hint = Label(stage, "Hint", "CLICK THE PACK TO OPEN", 30, UI.Cream, TextAlignmentOptions.Center);
        hint.characterSpacing = 14;
        UI.Place(hint.rectTransform, Bottom, Bottom, new Vector2(0, 74), new Vector2(1200, 44));

        RectTransform buttons = Box(stage, "Buttons");
        UI.Place(buttons, Bottom, Bottom, new Vector2(0, 44), new Vector2(1300, 80));
        CanvasGroup buttonsCanvas = buttons.gameObject.AddComponent<CanvasGroup>();
        HorizontalLayoutGroup row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 28;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = false;
        row.childControlHeight = false;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        Button continueButton = NewButton(buttons, "Continue", "Continue", UI.ButtonKind.Light, new Vector2(260, 76), 30);
        Button another = NewButton(buttons, "Open another", "Open another (200 kr)", UI.ButtonKind.Primary, new Vector2(430, 76), 30);
        Button toRunners = NewButton(buttons, "My runners", "My runners", UI.ButtonKind.Secondary, new Vector2(280, 76), 30);
        Button skip = NewButton(stage, "Skip", "Skip", UI.ButtonKind.Soft, new Vector2(150, 54), 24);
        UI.Place((RectTransform)skip.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-48, 44), new Vector2(150, 54));

        show.stageGroup = canvasObject.GetComponent<CanvasGroup>();
        show.shaker = shaker;
        show.rays = stageRays;
        show.glow = stageGlow;
        show.beamLeft = beamLeft;
        show.beamRight = beamRight;
        show.flash = flash;
        show.pillar = pillar;
        show.streak = flare;
        show.sparks = sparks;
        show.pack = packRoot;
        show.packBody = body;
        show.packTop = top;
        show.seamGlow = seam;
        show.packButton = packButton;
        show.hint = hint;
        show.flagGroup = flagGroup;
        show.flagCanvas = flagCanvas;
        show.flagImage = flagImage;
        show.countryText = country;
        show.statsGroup = stats;
        show.metaRow = metaCanvas;
        show.clubImage = clubImage;
        show.klassText = klass;
        show.speedBlock = speedBlock;
        show.speedValue = speedValue;
        show.techniqueBlock = techniqueBlock;
        show.techniqueValue = techniqueValue;
        show.overallBlock = overallBlock;
        show.overallValue = overallValue;
        show.overallLabel = overallLabel;
        show.tierTinted = new Graphic[] { flagGlow, speedLabel, techniqueLabel, overallValue, ring, portraitGlow };
        show.portraitGroup = portraitGroup;
        show.portraitCanvas = portraitCanvas;
        show.portrait = portrait;
        show.portraitFrame = frame;
        show.scanLine = scan;
        show.nameText = runnerName;
        show.cardHolder = holder;
        show.card = card;
        show.cardFace = faceCanvas;
        show.cardBack = back;
        show.cardRays = cardRays;
        show.titleText = title;
        show.subtitleText = subtitle;
        show.buttons = buttonsCanvas;
        show.continueButton = continueButton;
        show.anotherButton = another;
        show.runnersButton = toRunners;
        show.skipButton = skip;
        show.anotherLabel = another.GetComponentInChildren<TextMeshProUGUI>(true);
        show.sfx = sfx;
        show.loop = loop;
        show.whoosh = Clip("whoosh");
        show.riser = Clip("riser");
        show.boom = Clip("boom");
        show.hit = Clip("hit");
        show.tick = Clip("tick");
        show.reveal = Clip("reveal");
        show.shimmer = Clip("shimmer");
        show.click = Clip("click");
        show.coin = Clip("coin");

        PrefabUtility.SaveAsPrefabAsset(root, OpeningPath);
        UnityEngine.Object.DestroyImmediate(root);
        UI.log.AppendLine("pack opening prefab built");
    }

    static CanvasGroup StatBlock(Transform parent, string name, string caption, Vector2 position, float size, out TextMeshProUGUI value, out TextMeshProUGUI label)
    {
        RectTransform block = Box(parent, name);
        UI.Place(block, UI.Center, UI.Center, position, new Vector2(400, 340));
        value = Label(block, "Value", "0", size, UI.Cream, TextAlignmentOptions.Center);
        UI.Place(value.rectTransform, UI.Center, UI.Center, new Vector2(0, 20), new Vector2(400, size * 1.15f));
        UI.ApplyShadow(value);
        label = Label(block, "Label", caption, 32, UI.Cream, TextAlignmentOptions.Center);
        label.characterSpacing = 18;
        UI.Place(label.rectTransform, UI.Center, UI.Center, new Vector2(0, -size * 0.52f - 20f), new Vector2(400, 44));
        return block.gameObject.AddComponent<CanvasGroup>();
    }

    // ---------- the main menu scene ----------

    static void BuildMenu()
    {
        GameObject canvasObject = GameObject.Find("MainMenu");
        Transform canvas = canvasObject.transform;
        UI.SetupScaler(canvas);
        if (Camera.main != null)
        {
            Camera.main.backgroundColor = UI.Ink;
            EditorUtility.SetDirty(Camera.main);
        }

        // The three pictures that were the whole menu before are kept and framed as tiles
        Transform compete = FindDeep(canvas, "New Competition");
        Transform shopArt = FindDeep(canvas, "Shop");
        Transform runnersArt = FindDeep(canvas, "Your runners");
        foreach (Transform art in new[] { compete, shopArt, runnersArt })
        {
            art.SetParent(canvas, false);
        }
        foreach (string generated in new[] { "Background", "Shade", "Home screen", "Shop screen", "Runners screen", "Money pill" })
        {
            Transform old = canvas.Find(generated);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        MainMenuUI menu = UI.GetOrAdd<MainMenuUI>(canvasObject);
        AudioSource audio = UI.GetOrAdd<AudioSource>(canvasObject);
        audio.playOnAwake = false;

        Image backgroundImage = Img(canvas, "Background", UI.background, Color.white);
        AspectRatioFitter fitter = backgroundImage.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        if (UI.background != null) fitter.aspectRatio = UI.background.rect.width / UI.background.rect.height;
        UI.Stretch(backgroundImage.rectTransform, 0, 0, 0, 0);
        Image shadeImage = Img(canvas, "Shade", gradient, UIAnim.WithAlpha(UI.Ink, 0.6f));
        RectTransform shadeRect = shadeImage.rectTransform;
        shadeRect.anchorMin = new Vector2(0, 0);
        shadeRect.anchorMax = new Vector2(1, 0);
        shadeRect.pivot = Bottom;
        shadeRect.anchoredPosition = Vector2.zero;
        shadeRect.sizeDelta = new Vector2(0, 560);

        // ---- Home ----
        RectTransform home = NewScreen(canvas, "Home screen");
        TextMeshProUGUI title = Label(home, "Game Title", "O-MANAGER", 100, UI.Cream, TextAlignmentOptions.Center);
        title.characterSpacing = 2;
        UI.Place(title.rectTransform, Top, Top, new Vector2(0, -34), new Vector2(1400, 130));
        UI.ApplyShadow(title);
        TextMeshProUGUI subtitle = Label(home, "Subtitle", "ORIENTEERING MANAGER", 30, UI.Cream, TextAlignmentOptions.Center);
        subtitle.characterSpacing = 28;
        UI.Place(subtitle.rectTransform, Top, Top, new Vector2(0, -170), new Vector2(1200, 44));
        UI.ApplyShadow(subtitle);

        Tile(home, compete, "Tile compete", -470, "Race against your friends");
        TextMeshProUGUI shopCaption = Tile(home, shopArt, "Tile shop", 0, "Runner packs");
        TextMeshProUGUI runnersCaption = Tile(home, runnersArt, "Tile runners", 470, "Your collection");
        Wire(shopArt.GetComponent<Button>(), menu.ShowShop);
        Wire(runnersArt.GetComponent<Button>(), menu.ShowRunners);

        Button quit = NewButton(home, "Quit", "Quit", UI.ButtonKind.Light, new Vector2(150, 56), 26);
        UI.Place((RectTransform)quit.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(48, 44), new Vector2(150, 56));
        UI.AddDropShadow(quit.gameObject, 6);
        Wire(quit, menu.Quit);

        Button builder = NewButton(home, "Course builder", "Course builder", UI.ButtonKind.Primary, new Vector2(300, 56), 26);
        UI.Place((RectTransform)builder.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-48, 44), new Vector2(300, 56));
        UI.AddDropShadow(builder.gameObject, 6);
        Wire(builder, UI.GetOrAdd<OpenCourseBuilder>(canvasObject).Open);

        // ---- Shop ----
        RectTransform shop = NewScreen(canvas, "Shop screen");
        Header(shop, "Runner packs", menu.ShowHome);

        RectTransform shopCard = Box(shop, "Card");
        UI.Place(shopCard, UI.Center, UI.Center, new Vector2(0, -60), new Vector2(1260, 740));
        shopCard.gameObject.AddComponent<Image>();
        UI.StyleCard(shopCard.gameObject);

        Image packGlow = Img(shopCard, "Pack glow", glow, UIAnim.WithAlpha(UI.Accent, 0.9f));
        UI.Place(packGlow.rectTransform, UI.Center, UI.Center, new Vector2(-330, 10), new Vector2(860, 860));
        Image packShadow = Img(shopCard, "Pack shadow", glow, UIAnim.WithAlpha(UI.Ink, 0.5f));
        UI.Place(packShadow.rectTransform, UI.Center, UI.Center, new Vector2(-330, -318), new Vector2(430, 70));
        Image packImage = Img(shopCard, "Pack", pack, Color.white);
        UI.Place(packImage.rectTransform, UI.Center, UI.Center, new Vector2(-330, 14), new Vector2(404, 587));

        Vector2 leftMiddle = new Vector2(0, 0.5f);
        float column = 30f;   // x of the text column, measured from the middle of the card
        TextMeshProUGUI packName = Label(shopCard, "Heading", "Runner Pack", 60, UI.Ink, TextAlignmentOptions.MidlineLeft);
        UI.Place(packName.rectTransform, UI.Center, leftMiddle, new Vector2(column, 262), new Vector2(540, 72));
        TextMeshProUGUI about = Label(shopCard, "About", "One runner in every pack.\nThe better the runner, the rarer the card.", 27, UI.InkMuted, TextAlignmentOptions.TopLeft);
        about.fontStyle = FontStyles.Normal;
        about.textWrappingMode = TextWrappingModes.Normal;
        about.lineSpacing = 12;
        UI.Place(about.rectTransform, UI.Center, leftMiddle, new Vector2(column, 168), new Vector2(560, 90));

        RectTransform odds = Box(shopCard, "Chances");
        UI.Place(odds, UI.Center, leftMiddle, new Vector2(column, 48), new Vector2(560, 122));
        UI.SoftSurface(odds.gameObject.AddComponent<Image>());
        TextMeshProUGUI oddsHeading = Label(odds, "Heading", "CHANCE PER PACK", 19, UI.InkMuted, TextAlignmentOptions.MidlineLeft);
        oddsHeading.characterSpacing = 12;
        UI.Place(oddsHeading.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -18), new Vector2(500, 28));
        TextMeshProUGUI oddsText = Label(odds, "Odds", "Elite 25%     Gold 75%", 30, UI.Ink, TextAlignmentOptions.MidlineLeft);
        oddsText.fontStyle = FontStyles.Normal;
        UI.Place(oddsText.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(26, 18), new Vector2(510, 44));

        TextMeshProUGUI owned = Label(shopCard, "Owned", "You have 0 of 6 runners", 27, UI.Ink, TextAlignmentOptions.MidlineLeft);
        UI.Place(owned.rectTransform, UI.Center, leftMiddle, new Vector2(column, -58), new Vector2(560, 40));

        Button buy = NewButton(shopCard, "Buy", "Open pack   200 kr", UI.ButtonKind.Primary, new Vector2(560, 100), 38);
        UI.Place((RectTransform)buy.transform, UI.Center, leftMiddle, new Vector2(column, -170), new Vector2(560, 100));
        UI.AddDropShadow(buy.gameObject, 6);
        UIHover buyHover = buy.gameObject.AddComponent<UIHover>();
        buyHover.hoverScale = 1.03f;
        Wire(buy, menu.BuyPack);
        TextMeshProUGUI note = Label(shopCard, "Note", "", 24, UI.InkMuted, TextAlignmentOptions.TopLeft);
        note.fontStyle = FontStyles.Normal;
        note.textWrappingMode = TextWrappingModes.Normal;
        UI.Place(note.rectTransform, UI.Center, leftMiddle, new Vector2(column, -262), new Vector2(560, 60));

        // ---- My Runners ----
        RectTransform runners = NewScreen(canvas, "Runners screen");
        RunnerCollectionUI collection = BuildCollection(runners, menu);

        MoneyPill(canvas);

        menu.canvas = canvasObject.GetComponent<Canvas>();
        menu.homePanel = home.gameObject;
        menu.shopPanel = shop.gameObject;
        menu.runnersPanel = runners.gameObject;
        menu.shopCaption = shopCaption;
        menu.runnersCaption = runnersCaption;
        menu.packImage = packImage.rectTransform;
        menu.packShadow = packShadow.rectTransform;
        menu.buyButton = buy;
        menu.buyLabel = buy.GetComponentInChildren<TextMeshProUGUI>(true);
        menu.noteText = note;
        menu.oddsText = oddsText;
        menu.ownedText = owned;
        menu.audioSource = audio;
        menu.clickSound = Clip("click");
        EditorUtility.SetDirty(menu);
        EditorUtility.SetDirty(collection);

        shop.gameObject.SetActive(false);
        runners.gameObject.SetActive(false);
        UI.log.AppendLine("main menu built");
    }

    static RunnerCollectionUI BuildCollection(RectTransform screen, MainMenuUI menu)
    {
        RunnerCollectionUI ui = screen.gameObject.AddComponent<RunnerCollectionUI>();
        TextMeshProUGUI count = Header(screen, "My runners", menu.ShowHome);

        RunnerFilterBar filters = BuildFilterBar(screen);

        // The grid of cards
        RectTransform cards = Box(screen, "Cards");
        UI.Stretch(cards, 100, 366, 100, 36);
        Image panel = cards.gameObject.AddComponent<Image>();
        panel.sprite = UI.rounded;
        panel.type = Image.Type.Sliced;
        panel.color = UIAnim.WithAlpha(UI.Ink, 0.42f);
        ScrollRect scroll = cards.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 50;
        RectTransform viewport = Box(cards, "Viewport");
        UI.Stretch(viewport, 12, 12, 12, 12);
        viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Box(viewport, "Content");
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = Top;
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(258, 400);
        grid.spacing = new Vector2(20, 20);
        grid.padding = new RectOffset(20, 20, 16, 16);
        grid.childAlignment = TextAnchor.UpperCenter;
        ContentSizeFitter contentFit = content.gameObject.AddComponent<ContentSizeFitter>();
        contentFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;

        // One cell, copied for every runner
        RectTransform cellRect = Box(cards, "Cell template");
        cellRect.sizeDelta = new Vector2(258, 400);
        Image cellHit = cellRect.gameObject.AddComponent<Image>();
        cellHit.color = new Color(1, 1, 1, 0);
        Button cellButton = cellRect.gameObject.AddComponent<Button>();
        cellButton.transition = Selectable.Transition.None;
        RectTransform cellCard = Box(cellRect, "Card");
        UI.Stretch(cellCard, 0, 0, 0, 0);
        RunnerCardView cellView = AddCard(cellCard, Vector2.zero, 0.585f);
        cellView.shineEvery = 6f;
        Image badge = Img(cellRect, "Copies", UI.pill, UI.Accent);
        badge.type = Image.Type.Sliced;
        badge.pixelsPerUnitMultiplier = 128f / 36f;
        UI.Place(badge.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-2, -6), new Vector2(66, 36));
        TextMeshProUGUI badgeText = Label(badge.transform, "Text", "x2", 22, Color.white, TextAlignmentOptions.Center);
        UI.Stretch(badgeText.rectTransform, 0, 0, 0, 0);
        UIHover cellHover = cellRect.gameObject.AddComponent<UIHover>();
        cellHover.target = cellCard;
        cellHover.hoverScale = 1.05f;
        RunnerCell cell = cellRect.gameObject.AddComponent<RunnerCell>();
        cell.view = cellView;
        cell.button = cellButton;
        cell.copiesBadge = badge.gameObject;
        cell.copiesText = badgeText;
        cellRect.gameObject.SetActive(false);

        // Shown when there is nothing in the grid
        RectTransform empty = Box(cards, "Empty");
        UI.Place(empty, UI.Center, UI.Center, Vector2.zero, new Vector2(1000, 300));
        TextMeshProUGUI emptyText = Label(empty, "Text", "You have no runners yet.\nOpen a pack to get your first one.", 36, UI.Cream, TextAlignmentOptions.Center);
        emptyText.textWrappingMode = TextWrappingModes.Normal;
        emptyText.lineSpacing = 14;
        UI.Place(emptyText.rectTransform, UI.Center, UI.Center, new Vector2(0, 50), new Vector2(1000, 130));
        Button toShop = NewButton(empty, "Open a pack", "Open a pack", UI.ButtonKind.Primary, new Vector2(320, 80), 32);
        UI.Place((RectTransform)toShop.transform, UI.Center, UI.Center, new Vector2(0, -80), new Vector2(320, 80));
        Wire(toShop, menu.ShowShop);

        // Close-up of one runner
        RectTransform detail = Box(screen, "Close-up");
        UI.Stretch(detail, 0, 0, 0, 0);
        Image dim = detail.gameObject.AddComponent<Image>();
        dim.color = UIAnim.WithAlpha(UI.Hex("0B1A12"), 0.995f);
        Button close = detail.gameObject.AddComponent<Button>();
        close.transition = Selectable.Transition.None;
        Image detailGlow = Img(detail, "Glow", glow, UIAnim.WithAlpha(UI.Cream, 0.16f));
        UI.Place(detailGlow.rectTransform, UI.Center, UI.Center, new Vector2(-300, 0), new Vector2(1500, 1500));
        RunnerCardView detailCard = AddCard(detail, new Vector2(-300, 0), 1.3f);
        detailCard.shineEvery = 3.5f;
        Vector2 leftMiddle = new Vector2(0, 0.5f);
        TextMeshProUGUI detailTitle = Label(detail, "Name", "Runner name", 68, UI.Cream, TextAlignmentOptions.MidlineLeft);
        UI.Place(detailTitle.rectTransform, UI.Center, leftMiddle, new Vector2(80, 250), new Vector2(820, 84));
        TextMeshProUGUI detailInfo = Label(detail, "Info", "", 38, UI.Cream, TextAlignmentOptions.TopLeft);
        detailInfo.fontStyle = FontStyles.Normal;
        detailInfo.textWrappingMode = TextWrappingModes.Normal;
        detailInfo.lineSpacing = 22;
        UI.Place(detailInfo.rectTransform, UI.Center, new Vector2(0, 1), new Vector2(80, 180), new Vector2(820, 520));
        TextMeshProUGUI closeHint = Label(detail, "Hint", "CLICK ANYWHERE TO CLOSE", 22, UIAnim.WithAlpha(UI.Cream, 0.7f), TextAlignmentOptions.Center);
        closeHint.characterSpacing = 14;
        UI.Place(closeHint.rectTransform, Bottom, Bottom, new Vector2(0, 50), new Vector2(900, 34));
        detail.gameObject.SetActive(false);

        ui.grid = content;
        ui.cellTemplate = cell;
        ui.scroll = scroll;
        ui.countText = count;
        ui.emptyState = empty.gameObject;
        ui.emptyText = emptyText;
        ui.emptyShopButton = toShop.gameObject;
        ui.filters = filters;
        ui.detailPanel = detail.gameObject;
        ui.detailCard = detailCard;
        ui.detailTitle = detailTitle;
        ui.detailInfo = detailInfo;
        ui.detailClose = close;
        return ui;
    }

    // The cream bar with search, sorting and filters. My Runners and Choose a runner share it.
    internal static RunnerFilterBar BuildFilterBar(Transform parent)
    {
        RectTransform bar = Box(parent, "Filters");
        UI.Place(bar, Top, Top, new Vector2(0, -196), new Vector2(1720, 152));
        bar.gameObject.AddComponent<Image>();
        UI.StyleCard(bar.gameObject);

        TMP_InputField search = NewInput(bar, "Search");
        UI.StyleInput(search, Vector2.zero, "Search runner...");
        UI.Place((RectTransform)search.transform, UI.Center, UI.Center, new Vector2(-600, 36), new Vector2(460, 60));
        UI.Stretch(search.textViewport, 22, 6, 22, 6);
        search.pointSize = 28;
        ((TextMeshProUGUI)search.textComponent).fontSize = 28;
        ((TextMeshProUGUI)search.placeholder).fontSize = 28;

        Caption(bar, "Sort label", "SORT BY", new Vector2(-282, 36), 130);
        string[] sortNames = { "Overall", "Speed", "Technique", "Name" };
        float[] sortWidths = { 140, 130, 170, 120 };
        Button[] sortButtons = new Button[sortNames.Length];
        float x = -200;
        for (int i = 0; i < sortNames.Length; i++)
        {
            sortButtons[i] = Chip(bar, "Sort " + sortNames[i], sortNames[i], new Vector2(x + sortWidths[i] / 2f, 36), sortWidths[i]);
            x += sortWidths[i] + 12;
        }
        Button clear = NewButton(bar, "Clear", "Clear filters", UI.ButtonKind.Secondary, new Vector2(190, 50), 22);
        UI.Place((RectTransform)clear.transform, UI.Center, UI.Center, new Vector2(735, 36), new Vector2(190, 50));

        Caption(bar, "Tier label", "TIER", new Vector2(-800, -36), 80);
        string[] tierNames = { "All", "Elite", "Gold", "Silver", "Bronze" };
        Button[] tierButtons = new Button[tierNames.Length];
        x = -750;
        for (int i = 0; i < tierNames.Length; i++)
        {
            tierButtons[i] = Chip(bar, "Tier " + tierNames[i], tierNames[i], new Vector2(x + 60, -36), 120);
            x += 130;
        }

        TextMeshProUGUI speedLabel = Label(bar, "Min speed label", "Min speed  <b>0</b>", 24, UI.Ink, TextAlignmentOptions.MidlineLeft);
        speedLabel.fontStyle = FontStyles.Normal;
        UI.Place(speedLabel.rectTransform, UI.Center, UI.Center, new Vector2(30, -36), new Vector2(200, 40));
        Slider speedSlider = NewSlider(bar, "Min speed", new Vector2(230, -36), 190);
        TextMeshProUGUI techniqueLabel = Label(bar, "Min technique label", "Min technique  <b>0</b>", 24, UI.Ink, TextAlignmentOptions.MidlineLeft);
        techniqueLabel.fontStyle = FontStyles.Normal;
        UI.Place(techniqueLabel.rectTransform, UI.Center, UI.Center, new Vector2(495, -36), new Vector2(250, 40));
        Slider techniqueSlider = NewSlider(bar, "Min technique", new Vector2(730, -36), 190);

        RunnerFilterBar filters = bar.gameObject.AddComponent<RunnerFilterBar>();
        filters.search = search;
        filters.sortButtons = sortButtons;
        filters.tierButtons = tierButtons;
        filters.minSpeed = speedSlider;
        filters.minTechnique = techniqueSlider;
        filters.minSpeedLabel = speedLabel;
        filters.minTechniqueLabel = techniqueLabel;
        filters.clearButton = clear;
        filters.chipOn = UI.Ink;
        filters.chipOff = UI.Hex("E4DCC2");
        filters.chipOnText = UI.Cream;
        filters.chipOffText = UI.Ink;
        return filters;
    }

    // A full-screen layer of the menu
    static RectTransform NewScreen(Transform canvas, string name)
    {
        RectTransform screen = Box(canvas, name);
        UI.Stretch(screen, 0, 0, 0, 0);
        return screen;
    }

    // Back button, heading and a line under it. Returns the line's text.
    static TextMeshProUGUI Header(Transform screen, string heading, UnityAction back)
    {
        Button backButton = NewButton(screen, "Back", "Back", UI.ButtonKind.Light, new Vector2(170, 60), 28);
        UI.Place((RectTransform)backButton.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -48), new Vector2(170, 60));
        UI.AddDropShadow(backButton.gameObject, 6);
        Wire(backButton, back);

        TextMeshProUGUI title = Label(screen, "Heading", heading.ToUpperInvariant(), 76, UI.Cream, TextAlignmentOptions.Center);
        title.characterSpacing = 4;
        UI.Place(title.rectTransform, Top, Top, new Vector2(0, -36), new Vector2(1200, 100));
        UI.ApplyShadow(title);
        TextMeshProUGUI line = Label(screen, "Subheading", "", 28, UI.Cream, TextAlignmentOptions.Center);
        line.characterSpacing = 10;
        UI.Place(line.rectTransform, Top, Top, new Vector2(0, -136), new Vector2(1200, 40));
        UI.ApplyShadow(line);
        return line;
    }

    // One of the big pictures on the home screen, framed like a card
    internal static TextMeshProUGUI Tile(Transform home, Transform art, string name, float x, string caption)
    {
        RectTransform tile = Box(home, name);
        UI.Place(tile, UI.Center, UI.Center, new Vector2(x, -84), new Vector2(424, 676));
        tile.gameObject.AddComponent<Image>();
        UI.StyleCard(tile.gameObject);
        tile.GetComponent<Image>().color = UI.CardSolid;
        tile.GetComponent<Image>().raycastTarget = false;

        RectTransform frame = Box(tile, "Picture");
        UI.Place(frame, Top, Top, new Vector2(0, -14), new Vector2(396, 594));
        Image mask = frame.gameObject.AddComponent<Image>();
        mask.sprite = UI.rounded;
        mask.type = Image.Type.Sliced;
        mask.pixelsPerUnitMultiplier = 1.4f;
        mask.raycastTarget = false;
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        art.SetParent(frame, false);
        UI.Stretch((RectTransform)art, 0, 0, 0, 0);

        TextMeshProUGUI text = Label(tile, "Caption", caption, 25, UI.Ink, TextAlignmentOptions.Center);
        UI.Place(text.rectTransform, Bottom, Bottom, new Vector2(0, 14), new Vector2(400, 40));

        UIHover hover = tile.gameObject.AddComponent<UIHover>();
        hover.target = tile;
        hover.hoverScale = 1.04f;
        hover.lift = 12f;
        return text;
    }

    internal static void MoneyPill(Transform canvas)
    {
        RectTransform pill = Box(canvas, "Money pill");
        Image image = pill.gameObject.AddComponent<Image>();
        image.sprite = UI.pill;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 128f / 60f;
        image.color = UI.Cream;
        image.raycastTarget = false;
        UI.Place(pill, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -48), new Vector2(220, 60));
        HorizontalLayoutGroup group = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(20, 26, 0, 0);
        group.spacing = 12;
        group.childAlignment = TextAnchor.MiddleCenter;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        ContentSizeFitter fit = pill.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        UI.AddDropShadow(pill.gameObject, 6);

        Image coin = Img(pill, "Coin", UI.pill, UI.Accent);
        LayoutElement coinSize = coin.gameObject.AddComponent<LayoutElement>();
        coinSize.minWidth = coinSize.preferredWidth = 22;
        coinSize.minHeight = coinSize.preferredHeight = 22;
        TextMeshProUGUI amount = Label(pill, "Amount", "0 kr", 28, UI.Ink, TextAlignmentOptions.Center);
        MoneyLabel label = pill.gameObject.AddComponent<MoneyLabel>();
        label.text = amount;
        LayoutRebuilder.ForceRebuildLayoutImmediate(pill);
    }

    // ---------- building blocks ----------

    internal static RectTransform Box(Transform parent, string name)
    {
        GameObject created = new GameObject(name, typeof(RectTransform));
        created.layer = 5;
        created.transform.SetParent(parent, false);
        return (RectTransform)created.transform;
    }

    internal static Image Img(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
    {
        Image image = Box(parent, name).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    // A picture that adds light instead of covering what is behind it
    static Image Fx(Transform parent, string name, Sprite sprite, Color color)
    {
        Image image = Img(parent, name, sprite, color);
        image.material = additive;
        return image;
    }

    static Image Line(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color ink)
    {
        Image line = Img(parent, name, null, UIAnim.WithAlpha(ink, 0.45f));
        UI.Place(line.rectTransform, anchor, UI.Center, position, size);
        return line;
    }

    static UIParticles Particles(Transform parent, string name, Sprite sprite)
    {
        RectTransform rect = Box(parent, name);
        UI.Place(rect, UI.Center, UI.Center, Vector2.zero, new Vector2(100, 100));
        UIParticles particles = rect.gameObject.AddComponent<UIParticles>();
        particles.texture = sprite != null ? sprite.texture : null;
        particles.material = additive;
        particles.raycastTarget = false;
        return particles;
    }

    internal static TextMeshProUGUI Label(Transform parent, string name, string content, float size, Color color, TextAlignmentOptions alignment)
    {
        return UI.Text(parent, name, content, size, color, FontStyles.Bold, alignment);
    }

    static void Caption(Transform parent, string name, string content, Vector2 position, float width)
    {
        TextMeshProUGUI text = Label(parent, name, content, 19, UI.InkMuted, TextAlignmentOptions.MidlineLeft);
        text.characterSpacing = 12;
        UI.Place(text.rectTransform, UI.Center, UI.Center, position, new Vector2(width, 30));
    }

    internal static Button NewButton(Transform parent, string name, string label, UI.ButtonKind kind, Vector2 size, float fontSize)
    {
        RectTransform rect = Box(parent, name);
        rect.sizeDelta = size;
        rect.gameObject.AddComponent<Image>();
        Button button = rect.gameObject.AddComponent<Button>();
        RectTransform text = Box(rect, "Text (TMP)");
        text.gameObject.AddComponent<TextMeshProUGUI>();
        UI.StyleButton(button, kind, size.y, fontSize, label);
        return button;
    }

    static Button Chip(Transform parent, string name, string label, Vector2 position, float width)
    {
        Button chip = NewButton(parent, name, label, UI.ButtonKind.Light, new Vector2(width, 48), 23);
        UI.Place((RectTransform)chip.transform, UI.Center, UI.Center, position, new Vector2(width, 48));
        return chip;
    }

    static Slider NewSlider(Transform parent, string name, Vector2 position, float width)
    {
        RectTransform root = Box(parent, name);
        UI.Place(root, UI.Center, UI.Center, position, new Vector2(width, 30));
        Slider slider = root.gameObject.AddComponent<Slider>();

        Image track = Img(root, "Background", UI.pill, UI.FieldBorder, true);
        track.raycastPadding = new Vector4(0, -12, 0, -12);   // the thin track is easier to click with some margin
        track.type = Image.Type.Sliced;
        track.pixelsPerUnitMultiplier = 16f;
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0, 0.5f);
        trackRect.anchorMax = new Vector2(1, 0.5f);
        trackRect.sizeDelta = new Vector2(0, 8);

        RectTransform fillArea = Box(root, "Fill Area");
        fillArea.anchorMin = new Vector2(0, 0.5f);
        fillArea.anchorMax = new Vector2(1, 0.5f);
        fillArea.sizeDelta = new Vector2(0, 8);
        Image fill = Img(fillArea, "Fill", UI.pill, UI.Accent);
        fill.type = Image.Type.Sliced;
        fill.pixelsPerUnitMultiplier = 16f;
        fill.rectTransform.sizeDelta = Vector2.zero;

        RectTransform handleArea = Box(root, "Handle Slide Area");
        UI.Stretch(handleArea, 13, 0, 13, 0);
        Image handle = Img(handleArea, "Handle", UI.pill, UI.Ink, true);
        // The slider stretches its handle to the height of this area (30), so -4 makes it a 26 pixel circle
        handle.rectTransform.sizeDelta = new Vector2(26, -4);

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0;
        slider.maxValue = 99;
        slider.wholeNumbers = true;
        slider.value = 0;
        return slider;
    }

    static TMP_InputField NewInput(Transform parent, string name)
    {
        RectTransform root = Box(parent, name);
        root.gameObject.AddComponent<Image>();
        TMP_InputField input = root.gameObject.AddComponent<TMP_InputField>();
        RectTransform area = Box(root, "Text Area");
        area.gameObject.AddComponent<RectMask2D>();
        UI.Stretch(area, 22, 6, 22, 6);
        TextMeshProUGUI placeholder = Box(area, "Placeholder").gameObject.AddComponent<TextMeshProUGUI>();
        UI.Stretch(placeholder.rectTransform, 0, 0, 0, 0);
        TextMeshProUGUI text = Box(area, "Text").gameObject.AddComponent<TextMeshProUGUI>();
        UI.Stretch(text.rectTransform, 0, 0, 0, 0);
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        return input;
    }

    // Makes the button call the method, without stacking up duplicates when the builder runs again
    static void Wire(Button button, UnityAction action)
    {
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            if (button.onClick.GetPersistentTarget(i) is MainMenuUI)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            }
        }
        UnityEventTools.AddPersistentListener(button.onClick, action);
        EditorUtility.SetDirty(button);
    }

    internal static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != root && child.name == name)
            {
                return child;
            }
        }
        return null;
    }
}
