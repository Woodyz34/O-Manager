using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Applies the modern look to the MultiPlayer and Lobby scenes.
// Change the colours below and run Tools > O-Manager > Developer > Apply Modern UI to restyle both scenes again.
public static class ModernUIBuilder
{
    // ---- Theme ----
    internal static readonly Color Card = Hex("F6F0DC", 0.94f);
    internal static readonly Color CardSolid = Hex("F3EDD9");
    internal static readonly Color CardShadow = Hex("1F3D2B", 0.25f);
    internal static readonly Color Ink = Hex("1F3D2B");
    internal static readonly Color InkMuted = Hex("5B7563");
    internal static readonly Color Cream = Hex("FBF4DA");
    internal static readonly Color Accent = Hex("E8742A");
    internal static readonly Color Forest = Hex("3F7D54");
    internal static readonly Color Soft = Hex("FFFFFF", 0.78f);
    internal static readonly Color FieldBorder = Hex("C7BC98");

    internal const string MultiPlayerScene = "Assets/Scenes/MultiPlayer.unity";
    internal const string LobbyScene = "Assets/Scenes/Lobby.unity";
    internal const string RowPrefab = "Assets/Prefabs/UI/SessionListItem.prefab";
    internal const string RoundedPath = "Assets/UI/Modern/ui_rounded.png";
    internal const string PillPath = "Assets/UI/Modern/ui_pill.png";
    internal const string BackgroundPath = "Assets/Resources/Sprites/e2bf7ebc-02a1-4b78-b52f-e0211eeac746.png";
    internal const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    internal const string ShadowMaterialPath = "Assets/UI/Modern/LiberationSans SDF - Soft Shadow.mat";

    internal enum ButtonKind { Primary, Secondary, Light, Soft }

    internal static Sprite rounded;
    internal static Sprite pill;
    internal static Sprite background;
    internal static Material shadowMaterial;
    internal static StringBuilder log;

    [MenuItem("Tools/O-Manager/Developer (rarely needed)/Apply Modern UI (MultiPlayer + Lobby)", false, 103)]
    public static void ApplyAllMenu()
    {
        Debug.Log(ApplyAll());
    }

    public static string ApplyAll()
    {
        log = new StringBuilder();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                return "Aborted: scene '" + SceneManager.GetSceneAt(i).name + "' has unsaved changes. Save it first.";
            }
        }
        if (EditorApplication.isPlaying)
        {
            return "Aborted: exit Play mode first.";
        }

        string startScene = SceneManager.GetActiveScene().path;

        LoadAssets();
        StyleRowPrefab();

        Scene scene = EditorSceneManager.OpenScene(MultiPlayerScene, OpenSceneMode.Single);
        BuildMultiPlayer(scene);
        Finish(scene);

        scene = EditorSceneManager.OpenScene(LobbyScene, OpenSceneMode.Single);
        BuildLobby(scene);
        Finish(scene);

        if (!string.IsNullOrEmpty(startScene) && startScene != LobbyScene)
        {
            EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
        }
        return log.ToString();
    }

    internal static void Finish(Scene scene)
    {
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        log.AppendLine(scene.name + " saved=" + saved);
    }

    // ---- Assets ----

    internal static void LoadAssets()
    {
        rounded = ImportSprite(RoundedPath, 32);
        pill = ImportSprite(PillPath, 63);
        background = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
        shadowMaterial = SoftShadowMaterial();
        log.AppendLine("assets rounded=" + (rounded != null) + " pill=" + (pill != null) + " background=" + (background != null) + " shadowMat=" + (shadowMaterial != null));
    }

    // A soft green shadow under light text, so titles stay readable on the artwork
    internal static Material SoftShadowMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(ShadowMaterialPath);
        if (material == null)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) return null;
            material = new Material(font.material);
            AssetDatabase.CreateAsset(material, ShadowMaterialPath);
        }
        material.EnableKeyword("UNDERLAY_ON");
        material.SetColor("_UnderlayColor", new Color(0.07f, 0.18f, 0.12f, 0.55f));
        material.SetFloat("_UnderlayOffsetX", 0.25f);
        material.SetFloat("_UnderlayOffsetY", -0.45f);
        material.SetFloat("_UnderlayDilate", 0.1f);
        material.SetFloat("_UnderlaySoftness", 0.3f);
        EditorUtility.SetDirty(material);
        return material;
    }

    internal static Sprite ImportSprite(string path, int border)
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
        }
        Vector4 wanted = new Vector4(border, border, border, border);
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != wanted || importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.spriteBorder = wanted;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ---- Session list row prefab ----

    internal static void StyleRowPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(RowPrefab);
        StyleRow(root);
        PrefabUtility.SaveAsPrefabAsset(root, RowPrefab);
        PrefabUtility.UnloadPrefabContents(root);
        log.AppendLine("row prefab styled");
    }

    internal static void StyleRow(GameObject root)
    {
        Image bg = GetOrAdd<Image>(root);
        SoftSurface(bg);
        bg.raycastTarget = false;

        LayoutElement rootLayout = GetOrAdd<LayoutElement>(root);
        rootLayout.minHeight = 76;
        rootLayout.preferredHeight = 76;

        HorizontalLayoutGroup group = root.GetComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(32, 20, 0, 0);
        group.spacing = 24;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;

        SessionInfoListUIItem item = root.GetComponent<SessionInfoListUIItem>();

        StyleText(item.sessionNameText, 32, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        item.sessionNameText.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement nameLayout = GetOrAdd<LayoutElement>(item.sessionNameText.gameObject);
        nameLayout.flexibleWidth = 1;
        nameLayout.minWidth = 100;

        StyleText(item.playerCountText, 28, InkMuted, FontStyles.Normal, TextAlignmentOptions.Midline);
        LayoutElement countLayout = GetOrAdd<LayoutElement>(item.playerCountText.gameObject);
        countLayout.minWidth = 160;
        countLayout.preferredWidth = 160;

        StyleButton(item.joinButton, ButtonKind.Secondary, 52, 26, null);
        LayoutElement joinLayout = GetOrAdd<LayoutElement>(item.joinButton.gameObject);
        joinLayout.minWidth = 150;
        joinLayout.preferredWidth = 150;
        joinLayout.minHeight = 52;
        joinLayout.preferredHeight = 52;
    }

    // ---- MultiPlayer scene ----

    internal static void BuildMultiPlayer(Scene scene)
    {
        MultiPlayerHandler handler = Object.FindFirstObjectByType<MultiPlayerHandler>(FindObjectsInactive.Include);
        Transform canvas = handler.transform;
        SetupScaler(canvas);

        // Background fills the screen without stretching the artwork
        Transform bg = canvas.Find("Background");
        if (bg != null)
        {
            RectTransform bgRect = (RectTransform)bg;
            Image bgImage = bg.GetComponent<Image>();
            bgImage.raycastTarget = false;
            AspectRatioFitter fitter = GetOrAdd<AspectRatioFitter>(bg.gameObject);
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            if (bgImage.sprite != null)
            {
                fitter.aspectRatio = bgImage.sprite.rect.width / bgImage.sprite.rect.height;
            }
            Stretch(bgRect, 0, 0, 0, 0);
        }

        // Title
        Transform title = canvas.Find("Game Title");
        if (title != null)
        {
            TextMeshProUGUI titleText = title.GetComponent<TextMeshProUGUI>();
            Place((RectTransform)title, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(1400, 130));
            StyleText(titleText, 100, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            titleText.characterSpacing = 2;
            titleText.raycastTarget = false;
            ApplyShadow(titleText);

            TextMeshProUGUI subtitle = Text(canvas, "Subtitle", "MULTIPLAYER", 30, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            subtitle.transform.SetSiblingIndex(title.GetSiblingIndex() + 1);
            subtitle.characterSpacing = 28;
            Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(900, 44));
            ApplyShadow(subtitle);
        }

        // Player name card
        Transform details = handler.playerDetailPanel.transform;
        Place((RectTransform)details, Center, Center, new Vector2(0, -80), new Vector2(820, 470));
        StyleCard(details.gameObject);
        CardHeading(details, "Player name", "Other players will see this name in the lobby.");
        StyleInput(handler.playerNameinputField, new Vector2(0, -8), "Enter player name...");
        Button find = details.Find("FindCompetition").GetComponent<Button>();
        Place((RectTransform)find.transform, Center, Center, new Vector2(0, -138), new Vector2(420, 88));
        StyleButton(find, ButtonKind.Primary, 88, 36, "Find competition");

        // Create lobby card
        Transform create = handler.createSessionPanel.transform;
        Place((RectTransform)create, Center, Center, new Vector2(0, -80), new Vector2(820, 470));
        StyleCard(create.gameObject);
        CardHeading(create, "Create a lobby", "Give the lobby a name so your friends can find it.");
        StyleInput(handler.sessionNameinputField, new Vector2(0, -8), "Enter lobby name...");
        Button createButton = create.Find("Create session").GetComponent<Button>();
        Place((RectTransform)createButton.transform, Center, Center, new Vector2(0, -138), new Vector2(420, 88));
        StyleButton(createButton, ButtonKind.Primary, 88, 36, "Create lobby");
        Transform back = create.Find("Return");
        if (back != null)
        {
            Place((RectTransform)back, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -28), new Vector2(150, 56));
            StyleButton(back.GetComponent<Button>(), ButtonKind.Soft, 56, 26, "Back");
        }

        // Status card
        Transform status = handler.statusPanel.transform;
        Place((RectTransform)status, Center, Center, new Vector2(0, -80), new Vector2(820, 300));
        StyleCard(status.gameObject);
        Transform statusText = status.Find("StatusText");
        if (statusText != null)
        {
            Stretch((RectTransform)statusText, 40, 40, 40, 40);
            StyleText(statusText.GetComponent<TextMeshProUGUI>(), 44, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        }

        // Session list card
        Transform list = handler.sessionBrowserPanel.transform;
        Place((RectTransform)list, Center, Center, new Vector2(0, -110), new Vector2(1180, 720));
        StyleCard(list.gameObject);

        Transform oldBar = list.Find("BG");
        if (oldBar != null) oldBar.gameObject.SetActive(false);
        Transform oldHeader = list.Find("SessionListItem");
        if (oldHeader != null) oldHeader.gameObject.SetActive(false);

        TextMeshProUGUI listHeading = Text(list, "Heading", "Open lobbies", 44, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        Place(listHeading.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -36), new Vector2(600, 64));

        Button newLobby = list.Find("Create  New Lobby").GetComponent<Button>();
        Place((RectTransform)newLobby.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -36), new Vector2(250, 64));
        StyleButton(newLobby, ButtonKind.Primary, 64, 28, "+ New lobby");

        TextMeshProUGUI colName = Text(list, "Column lobby", "LOBBY", 20, InkMuted, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        colName.characterSpacing = 14;
        Place(colName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -122), new Vector2(300, 30));
        TextMeshProUGUI colPlayers = Text(list, "Column players", "PLAYERS", 20, InkMuted, FontStyles.Bold, TextAlignmentOptions.Midline);
        colPlayers.characterSpacing = 14;
        colPlayers.rectTransform.localScale = Vector3.one;
        colPlayers.rectTransform.anchorMin = colPlayers.rectTransform.anchorMax = new Vector2(1, 1);
        colPlayers.rectTransform.pivot = new Vector2(0.5f, 1);
        colPlayers.rectTransform.anchoredPosition = new Vector2(-322, -122);
        colPlayers.rectTransform.sizeDelta = new Vector2(200, 30);

        SessionListUIHandler listHandler = list.GetComponent<SessionListUIHandler>();
        VerticalLayoutGroup rows = listHandler.verticalLayoutGroup;
        Stretch((RectTransform)rows.transform, 48, 164, 48, 40);
        rows.padding = new RectOffset(0, 0, 0, 0);
        rows.spacing = 12;
        rows.childAlignment = TextAnchor.UpperCenter;
        rows.childControlWidth = true;
        rows.childControlHeight = true;
        rows.childForceExpandWidth = true;
        rows.childForceExpandHeight = false;
        Image rowsImage = rows.GetComponent<Image>();
        if (rowsImage != null) rowsImage.raycastTarget = false;

        if (listHandler.statusText != null)
        {
            RectTransform statusRect = listHandler.statusText.rectTransform;
            Place(statusRect, Center, Center, new Vector2(0, -60), new Vector2(900, 120));
            StyleText(listHandler.statusText, 34, InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rows.transform);
        log.AppendLine("MultiPlayer styled");
    }

    // ---- Lobby scene ----

    internal static void BuildLobby(Scene scene)
    {
        ReadyUIHandler ready = Object.FindFirstObjectByType<ReadyUIHandler>(FindObjectsInactive.Include);
        Transform canvas = ready.transform;
        SetupScaler(canvas);

        // Forest backdrop behind the runners
        Camera cam = Camera.main;
        GameObject backdrop = GameObject.Find("Backdrop");
        if (backdrop == null)
        {
            backdrop = new GameObject("Backdrop");
            SceneManager.MoveGameObjectToScene(backdrop, scene);
            backdrop.transform.SetAsFirstSibling();
        }
        SpriteRenderer backdropRenderer = GetOrAdd<SpriteRenderer>(backdrop);
        backdropRenderer.sprite = background;
        backdropRenderer.sortingOrder = -1000;
        CameraBackdrop backdropFit = GetOrAdd<CameraBackdrop>(backdrop);
        backdropFit.targetCamera = cam;
        backdropFit.Fit();

        // Top row
        Place(ready.countDownText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -48), new Vector2(900, 80));
        StyleText(ready.countDownText, 54, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        ready.countDownText.raycastTarget = false;
        ApplyShadow(ready.countDownText);

        Button leave = canvas.Find("LeaveLobby").GetComponent<Button>();
        Place((RectTransform)leave.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -48), new Vector2(240, 64));
        StyleButton(leave, ButtonKind.Light, 64, 28, "Leave lobby");
        AddDropShadow(leave.gameObject, 6);

        Button start = ready.buttonReady.GetComponent<Button>();
        Place((RectTransform)start.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 64), new Vector2(380, 96));
        StyleButton(start, ButtonKind.Primary, 96, 40, "Start game");
        AddDropShadow(start.gameObject, 8);

        // Runner setup card
        Transform setup = canvas.Find("Player setup panel");
        RectTransform setupRect = (RectTransform)setup;
        Place(setupRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -160), new Vector2(420, 300));
        StyleCard(setup.gameObject);
        StackVertically(setup.gameObject, 28, 18);

        TextMeshProUGUI setupHeader = setup.Find("Header text").GetComponent<TextMeshProUGUI>();
        StyleText(setupHeader, 36, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        setupHeader.text = "Runner setup";

        Transform inner = setup.Find("InnerPanel");
        Image innerImage = inner.GetComponent<Image>();
        if (innerImage != null) innerImage.enabled = false;
        StackVertically(inner.gameObject, 0, 12, false);

        StyleSetupButton(inner.Find("Change player outfit"), "Change color");
        StyleSetupButton(inner.Find("Change Map"), "Change course");
        StyleSetupButton(inner.Find("Change GameMode"), "Change game mode");

        // Players card
        Transform players = canvas.Find("Players panel");
        if (players == null)
        {
            GameObject playersObject = new GameObject("Players panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            playersObject.layer = canvas.gameObject.layer;
            players = playersObject.transform;
            players.SetParent(canvas, false);
            players.SetSiblingIndex(setup.GetSiblingIndex() + 1);
        }
        Place((RectTransform)players, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -160), new Vector2(420, 300));
        StyleCard(players.gameObject);
        StackVertically(players.gameObject, 28, 18);

        Transform headerRow = Child(players, "Header row");
        HorizontalLayoutGroup headerGroup = GetOrAdd<HorizontalLayoutGroup>(headerRow.gameObject);
        headerGroup.childAlignment = TextAnchor.MiddleLeft;
        headerGroup.childControlWidth = true;
        headerGroup.childControlHeight = true;
        headerGroup.childForceExpandWidth = false;
        headerGroup.childForceExpandHeight = false;
        TextMeshProUGUI playersTitle = Text(headerRow, "Title", "Players", 36, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        GetOrAdd<LayoutElement>(playersTitle.gameObject).flexibleWidth = 1;
        TextMeshProUGUI count = Text(headerRow, "Count", "", 28, InkMuted, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

        Transform rowList = Child(players, "List");
        StackVertically(rowList.gameObject, 0, 10, false);

        Transform template = Child(rowList, "Row template");
        Image templateImage = GetOrAdd<Image>(template.gameObject);
        SoftSurface(templateImage);
        templateImage.raycastTarget = false;
        LayoutElement templateLayout = GetOrAdd<LayoutElement>(template.gameObject);
        templateLayout.minHeight = 60;
        templateLayout.preferredHeight = 60;
        HorizontalLayoutGroup templateGroup = GetOrAdd<HorizontalLayoutGroup>(template.gameObject);
        templateGroup.padding = new RectOffset(22, 22, 0, 0);
        templateGroup.spacing = 14;
        templateGroup.childAlignment = TextAnchor.MiddleLeft;
        templateGroup.childControlWidth = true;
        templateGroup.childControlHeight = true;
        templateGroup.childForceExpandWidth = false;
        templateGroup.childForceExpandHeight = false;

        Transform dot = Child(template, "Dot");
        Image dotImage = GetOrAdd<Image>(dot.gameObject);
        dotImage.sprite = pill;
        dotImage.type = Image.Type.Simple;
        dotImage.color = Forest;
        dotImage.raycastTarget = false;
        LayoutElement dotLayout = GetOrAdd<LayoutElement>(dot.gameObject);
        dotLayout.minWidth = dotLayout.preferredWidth = 16;
        dotLayout.minHeight = dotLayout.preferredHeight = 16;

        TextMeshProUGUI rowName = Text(template, "Name", "Player name", 28, Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        rowName.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement rowNameLayout = GetOrAdd<LayoutElement>(rowName.gameObject);
        rowNameLayout.flexibleWidth = 1;
        rowNameLayout.minWidth = 60;
        Text(template, "Tag", "You", 22, InkMuted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
        template.gameObject.SetActive(false);

        TextMeshProUGUI empty = Text(players, "Empty text", "Waiting for players...", 26, InkMuted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

        LobbyPlayerListUI playerList = GetOrAdd<LobbyPlayerListUI>(players.gameObject);
        playerList.countText = count;
        playerList.emptyText = empty;
        playerList.listParent = rowList;
        playerList.rowTemplate = template.gameObject;
        playerList.localDotColor = Accent;
        playerList.otherDotColor = Forest;
        EditorUtility.SetDirty(playerList);

        // Map and game mode pickers
        StylePicker(canvas.Find("SelectMapPanel"), "Choose course");
        StylePicker(canvas.Find("SelectGameModePanel"), "Choose game mode");

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)inner);
        LayoutRebuilder.ForceRebuildLayoutImmediate(setupRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)players);
        log.AppendLine("Lobby styled");
    }

    internal static void StyleSetupButton(Transform button, string label)
    {
        if (button == null) return;
        StyleButton(button.GetComponent<Button>(), ButtonKind.Soft, 64, 28, label);
        LayoutElement layout = GetOrAdd<LayoutElement>(button.gameObject);
        layout.minHeight = 64;
        layout.preferredHeight = 64;
    }

    internal static void StylePicker(Transform panel, string heading)
    {
        if (panel == null) return;
        Image image = panel.GetComponent<Image>();
        image.sprite = null;
        image.color = CardSolid;

        Transform title = panel.Find("Rubrik");
        if (title != null)
        {
            TextMeshProUGUI titleText = title.GetComponent<TextMeshProUGUI>();
            StyleText(titleText, 56, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            titleText.text = heading;
        }
        Transform close = panel.Find("CloseButton");
        if (close != null)
        {
            Place((RectTransform)close, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -48), new Vector2(170, 60));
            StyleButton(close.GetComponent<Button>(), ButtonKind.Secondary, 60, 28, "Close");
        }
    }

    // ---- Building blocks ----

    internal static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    internal static void SetupScaler(Transform canvas)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) return;
        // Same rule as in the running game (ScreenSettings): the whole 1920x1080 page fits on any screen shape
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ScreenSettings.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    }

    internal static void StyleCard(GameObject card)
    {
        Image image = GetOrAdd<Image>(card);
        image.sprite = rounded;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
        image.color = Card;
        AddDropShadow(card, 10);
    }

    internal static void AddDropShadow(GameObject target, float distance)
    {
        Shadow shadow = GetOrAdd<Shadow>(target);
        shadow.effectColor = CardShadow;
        shadow.effectDistance = new Vector2(0, -distance);
        shadow.useGraphicAlpha = true;
    }

    internal static void SoftSurface(Image image)
    {
        image.sprite = rounded;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2f;
        image.color = Soft;
    }

    internal static void CardHeading(Transform card, string heading, string hint)
    {
        TextMeshProUGUI headingText = Text(card, "Heading", heading, 46, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        Place(headingText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -56), new Vector2(520, 64));
        TextMeshProUGUI hintText = Text(card, "Hint", hint, 26, InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        Place(hintText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -124), new Vector2(740, 40));
    }

    internal static void StyleInput(TMP_InputField input, Vector2 position, string placeholder)
    {
        RectTransform rect = (RectTransform)input.transform;
        Place(rect, Center, Center, position, new Vector2(620, 84));

        Image border = input.GetComponent<Image>();
        border.sprite = rounded;
        border.type = Image.Type.Sliced;
        border.pixelsPerUnitMultiplier = 2f;
        border.color = FieldBorder;

        Transform fill = Child(input.transform, "Fill");
        fill.SetAsFirstSibling();
        Image fillImage = GetOrAdd<Image>(fill.gameObject);
        fillImage.sprite = rounded;
        fillImage.type = Image.Type.Sliced;
        fillImage.pixelsPerUnitMultiplier = 2.2f;
        fillImage.color = Color.white;
        fillImage.raycastTarget = false;
        Stretch((RectTransform)fill, 3, 3, 3, 3);

        if (input.textViewport != null)
        {
            Stretch(input.textViewport, 26, 8, 26, 8);
        }
        if (input.textComponent != null)
        {
            StyleText((TextMeshProUGUI)input.textComponent, 36, Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        }
        TextMeshProUGUI placeholderText = input.placeholder as TextMeshProUGUI;
        if (placeholderText != null)
        {
            StyleText(placeholderText, 36, new Color(InkMuted.r, InkMuted.g, InkMuted.b, 0.75f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            placeholderText.text = placeholder;
            placeholderText.enabled = true;
        }
        // The hint used to be stored as the field's real value; make it a proper placeholder instead
        if (input.text.Trim('​', ' ').StartsWith("Enter "))
        {
            input.text = "";
        }
        input.caretColor = Ink;
        input.customCaretColor = true;
        input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        input.pointSize = 36;
        EditorUtility.SetDirty(input);
    }

    internal static void StyleButton(Button button, ButtonKind kind, float height, float fontSize, string label)
    {
        Color fill = Accent;
        Color text = Color.white;
        switch (kind)
        {
            case ButtonKind.Secondary: fill = Forest; text = Cream; break;
            case ButtonKind.Light: fill = Cream; text = Ink; break;
            case ButtonKind.Soft: fill = Soft; text = Ink; break;
        }

        Image image = button.GetComponent<Image>();
        if (kind == ButtonKind.Soft)
        {
            image.sprite = rounded;
            image.pixelsPerUnitMultiplier = 2f;
        }
        else
        {
            image.sprite = pill;
            image.pixelsPerUnitMultiplier = 128f / height;
        }
        image.type = Image.Type.Sliced;
        image.color = fill;
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI labelText = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (labelText != null)
        {
            Stretch(labelText.rectTransform, 12, 0, 12, 0);
            StyleText(labelText, fontSize, text, FontStyles.Bold, TextAlignmentOptions.Center);
            labelText.raycastTarget = false;
            if (label != null) labelText.text = label;
            EditorUtility.SetDirty(labelText);
        }
        EditorUtility.SetDirty(button);
    }

    internal static void StackVertically(GameObject target, int padding, float spacing, bool fitHeight = true)
    {
        VerticalLayoutGroup group = GetOrAdd<VerticalLayoutGroup>(target);
        group.padding = new RectOffset(padding, padding, padding, padding);
        group.spacing = spacing;
        group.childAlignment = TextAnchor.UpperCenter;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        if (fitHeight)
        {
            ContentSizeFitter fitter = GetOrAdd<ContentSizeFitter>(target);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    internal static void StyleText(TextMeshProUGUI text, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        text.enableAutoSizing = false;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        EditorUtility.SetDirty(text);
    }

    internal static void ApplyShadow(TextMeshProUGUI text)
    {
        if (shadowMaterial != null && text.font != null && text.font.name == "LiberationSans SDF")
        {
            text.fontSharedMaterial = shadowMaterial;
        }
    }

    internal static TextMeshProUGUI Text(Transform parent, string name, string content, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        Transform child = Child(parent, name);
        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(child.gameObject);
        text.text = content;
        text.raycastTarget = false;
        StyleText(text, size, color, style, alignment);
        return text;
    }

    internal static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            GameObject created = new GameObject(name, typeof(RectTransform));
            created.layer = parent.gameObject.layer;
            child = created.transform;
            child.SetParent(parent, false);
        }
        return child;
    }

    internal static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null) component = target.AddComponent<T>();
        return component;
    }

    internal static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.localScale = Vector3.one;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    internal static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.localScale = Vector3.one;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    internal static Color Hex(string hex, float alpha = 1f)
    {
        Color color;
        ColorUtility.TryParseHtmlString("#" + hex, out color);
        color.a = alpha;
        return color;
    }
}
