using System;
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

// Builds the results screen and the imposter overlay prefabs, and restyles the in-game HUD
// (leaderboard, clock, buttons) in the competition scenes. Uses the same theme as ModernUIBuilder.
// Run it again from Tools > O-Manager > Developer > Apply Game UI after changing colours or layout here.
public static class GameUIBuilder
{
    const string EndScreenPath = "Assets/Resources/UI/EndScreen.prefab";
    const string OverlayPath = "Assets/Resources/UI/ImposterOverlay.prefab";
    const string DashboardPath = "Assets/Prefabs/UI/GameDashboard.prefab";
    const string LeaderboardEntryPath = "Assets/Prefabs/UI/LeaderboardEntry.prefab";
    const string VignettePath = "Assets/UI/Modern/ui_vignette.png";
    const string ImposterModePath = "Assets/Resources/GameModes/Imposter.png";
    const string ModeTemplatePath = "Assets/Resources/GameModes/Choose a runner of three.png";
    const string ResultPath = "Temp/gameui_result.txt";

    static readonly string[] GameScenes =
    {
        "Assets/Scenes/IllernKartan Träning.unity",
        "Assets/Scenes/IllernKartan Träning 2.unity",
        "Assets/Scenes/IllernKartan Träning 3.unity",
        "Assets/Scenes/IllernKartan Träning 4.unity",
    };

    // Spooky palette for the imposter overlay
    static readonly Color Night = UI.Hex("0B0612", 1f);
    static readonly Color Bone = UI.Hex("F2E9DC");
    static readonly Color Blood = UI.Hex("E0283C");
    static readonly Color ChipDark = UI.Hex("1A0E24", 0.92f);

    static Sprite vignette;

    [MenuItem("Tools/O-Manager/Developer (rarely needed)/Apply Game UI (end screen + in-game HUD)", false, 101)]
    public static void ApplyAllMenu()
    {
        Debug.Log(ApplyAll());
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
        vignette = UI.ImportSprite(VignettePath, 0);
        ImportImposterModePicture();

        Directory.CreateDirectory("Assets/Resources/UI");
        AssetDatabase.Refresh();

        BuildOverlayPrefab();
        BuildEndScreenPrefab();
        StyleLeaderboardEntryPrefab();
        StyleDashboardPrefab();

        foreach (string path in GameScenes)
        {
            if (!File.Exists(path))
            {
                UI.log.AppendLine("missing scene " + path);
                continue;
            }
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            StyleGameScene();
            Save(scene);
        }

        Scene multiPlayer = EditorSceneManager.OpenScene("Assets/Scenes/MultiPlayer.unity", OpenSceneMode.Single);
        MultiPlayerHandler handler = UnityEngine.Object.FindFirstObjectByType<MultiPlayerHandler>(FindObjectsInactive.Include);
        AddMoneyPill(handler.transform, false);
        Save(multiPlayer);

        Scene lobby = EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity", OpenSceneMode.Single);
        ReadyUIHandler ready = UnityEngine.Object.FindFirstObjectByType<ReadyUIHandler>(FindObjectsInactive.Include);
        AddMoneyPill(ready.transform, true);
        AddLobbySettings(ready);
        Save(lobby);

        AssetDatabase.SaveAssets();
        return UI.log.ToString();
    }

    static void Save(Scene scene)
    {
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        UI.log.AppendLine(scene.name + " saved=" + EditorSceneManager.SaveScene(scene));
    }

    // Imposter is now an option in the lobby, not a game mode of its own, so its picture leaves the mode list
    static void ImportImposterModePicture()
    {
        if (File.Exists(ImposterModePath))
        {
            string error = AssetDatabase.MoveAsset(ImposterModePath, "Assets/UI/Modern/imposter_mode.png");
            UI.log.AppendLine("imposter picture moved out of the game modes " + error);
        }
    }

    // In Shared Mode the scene's networked object must follow the host role. Otherwise it is deleted
    // (or left without an owner) when the host leaves, which breaks the lobby or the competition.
    static void FollowTheHost(GameObject target)
    {
        Fusion.NetworkObject networkObject = target != null ? target.GetComponent<Fusion.NetworkObject>() : null;
        if (networkObject == null)
        {
            UI.log.AppendLine("no NetworkObject on " + (target != null ? target.name : "null"));
            return;
        }
        SerializedObject serialized = new SerializedObject(networkObject);
        SerializedProperty flags = serialized.FindProperty("Flags");
        int wanted = (int)(Fusion.NetworkObjectFlags.V1 | Fusion.NetworkObjectFlags.MasterClientObject);
        UI.log.AppendLine("network flags on " + target.name + ": " + flags.intValue + " -> " + wanted);
        flags.intValue = wanted;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(networkObject);
    }

    // The lobby card that tells everyone what the host has picked, and the host's Imposter switch
    static void AddLobbySettings(ReadyUIHandler ready)
    {
        Transform canvas = ready.transform;
        FollowTheHost(canvas.gameObject);

        Transform card = canvas.Find("Game setup card");
        if (card == null)
        {
            card = UI.Child(canvas, "Game setup card");
            card.SetSiblingIndex(0);
        }
        UI.StyleCard(card.gameObject);
        card.GetComponent<Image>().pixelsPerUnitMultiplier = 1.4f;
        card.GetComponent<Image>().raycastTarget = false;
        UI.Place((RectTransform)card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -136), new Vector2(900, 96));
        Row(card.gameObject, 36, 36, 44);
        HorizontalLayoutGroup cardRow = card.GetComponent<HorizontalLayoutGroup>();
        cardRow.padding = new RectOffset(36, 36, 16, 16);
        cardRow.childAlignment = TextAnchor.MiddleCenter;
        ContentSizeFitter cardFitter = UI.GetOrAdd<ContentSizeFitter>(card.gameObject);
        cardFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        cardFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI mode = Setting(card, "Game mode", "GAME MODE");
        TextMeshProUGUI imposter = Setting(card, "Imposter", "IMPOSTER");
        TextMeshProUGUI map = Setting(card, "Map", "COURSE");

        Transform inner = canvas.Find("Player setup panel/InnerPanel");
        Transform toggle = inner.Find("Imposter toggle");
        if (toggle == null)
        {
            toggle = UI.Child(inner, "Imposter toggle");
            UI.GetOrAdd<Image>(toggle.gameObject);
            UI.GetOrAdd<Button>(toggle.gameObject);
            UI.Text(toggle, "Label", "Imposter: Off", 28, UI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        }
        Button toggleButton = toggle.GetComponent<Button>();
        UI.StyleButton(toggleButton, UI.ButtonKind.Soft, 64, 28, "Imposter: Off");
        Height(toggle.gameObject, 64);

        LobbySettingsUI settings = UI.GetOrAdd<LobbySettingsUI>(canvas.gameObject);
        settings.gameModeHandler = ready.gameModeHandler;
        settings.mapHandler = ready.mapHandler;
        settings.modeText = mode;
        settings.imposterText = imposter;
        settings.mapText = map;
        settings.imposterToggle = toggleButton;
        settings.imposterToggleText = toggle.GetComponentInChildren<TextMeshProUGUI>(true);
        settings.toggleOffColor = UI.Soft;
        settings.toggleOnColor = UI.Accent;
        settings.toggleOffTextColor = UI.Ink;
        EditorUtility.SetDirty(settings);

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)card);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.Find("Player setup panel"));
        UI.log.AppendLine("lobby settings added");
    }

    static TextMeshProUGUI Setting(Transform card, string name, string caption)
    {
        Transform block = UI.Child(card, name);
        UI.StackVertically(block.gameObject, 0, 2, false);
        block.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
        block.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        TextMeshProUGUI captionText = UI.Text(block, "Caption", caption, 16, UI.InkMuted, FontStyles.Bold, TextAlignmentOptions.Center);
        captionText.characterSpacing = 14;
        return UI.Text(block, "Value", "...", 26, UI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
    }

    // ---- Results screen ----

    static void BuildEndScreenPrefab()
    {
        GameObject root = NewCanvasRoot("EndScreen", 500);
        CanvasGroup rootGroup = root.AddComponent<CanvasGroup>();
        EndScreenUI ui = root.AddComponent<EndScreenUI>();
        Transform t = root.transform;

        Image bg = Img(t, "Background");
        bg.sprite = UI.background;
        bg.color = Color.white;
        AspectRatioFitter fitter = UI.GetOrAdd<AspectRatioFitter>(bg.gameObject);
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = UI.background != null ? UI.background.rect.width / UI.background.rect.height : 1.5f;
        UI.Stretch(bg.rectTransform, 0, 0, 0, 0);

        TextMeshProUGUI title = UI.Text(t, "Title", "Results", 96, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        UI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(1400, 120));
        UI.ApplyShadow(title);
        TextMeshProUGUI subtitle = UI.Text(t, "Subtitle", "COMPETITION FINISHED", 28, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        subtitle.characterSpacing = 26;
        UI.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -142), new Vector2(1200, 40));
        UI.ApplyShadow(subtitle);

        // Standings
        Transform card = UI.Child(t, "Standings card");
        UI.Place((RectTransform)card, UI.Center, UI.Center, new Vector2(-300, -40), new Vector2(980, 700));
        UI.StyleCard(card.gameObject);
        TextMeshProUGUI heading = UI.Text(card, "Heading", "Standings", 40, UI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        UI.Place(heading.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -30), new Vector2(500, 56));
        TextMeshProUGUI runnerColumn = UI.Text(card, "Column runner", "RUNNER", 18, UI.InkMuted, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        runnerColumn.characterSpacing = 14;
        UI.Place(runnerColumn.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(106, -102), new Vector2(300, 26));
        TextMeshProUGUI timeColumn = UI.Text(card, "Column time", "TIME", 18, UI.InkMuted, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        timeColumn.characterSpacing = 14;
        UI.Place(timeColumn.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-68, -102), new Vector2(200, 26));

        Transform rows = UI.Child(card, "Rows");
        UI.Stretch((RectTransform)rows, 44, 138, 44, 30);
        UI.StackVertically(rows.gameObject, 0, 6, false);

        Transform row = UI.Child(rows, "Row template");
        Image rowImage = UI.GetOrAdd<Image>(row.gameObject);
        UI.SoftSurface(rowImage);
        rowImage.raycastTarget = false;
        row.gameObject.AddComponent<CanvasGroup>();
        Height(row.gameObject, 46);
        Row(row.gameObject, 10, 24, 16);

        Image badge = Img(row, "Badge");
        badge.sprite = UI.pill;
        badge.type = Image.Type.Simple;
        badge.color = UI.Forest;
        badge.raycastTarget = false;
        Fixed(badge.gameObject, 34, 34);
        TextMeshProUGUI place = UI.Text(badge.transform, "Place", "1", 20, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        UI.Stretch(place.rectTransform, 0, 0, 0, 0);

        TextMeshProUGUI rowName = UI.Text(row, "Name", "Runner name", 26, UI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        rowName.overflowMode = TextOverflowModes.Ellipsis;
        Flexible(rowName.gameObject, 80);
        TextMeshProUGUI rowTag = UI.Text(row, "Tag", "YOU", 18, UI.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        rowTag.characterSpacing = 8;
        TextMeshProUGUI rowTime = UI.Text(row, "Time", "00:00", 26, UI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        Width(rowTime.gameObject, 150);

        // Side card: reward, or the imposter vote
        Transform side = UI.Child(t, "Side card");
        UI.Place((RectTransform)side, UI.Center, UI.Center, new Vector2(530, -40), new Vector2(560, 700));
        UI.StyleCard(side.gameObject);

        Transform reward = UI.Child(side, "Reward panel");
        UI.Stretch((RectTransform)reward, 40, 36, 40, 36);
        UI.StackVertically(reward.gameObject, 0, 14, false);
        UI.Text(reward, "Heading", "Your reward", 40, UI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        TextMeshProUGUI total = UI.Text(reward, "Total", "+0 kr", 92, UI.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        Transform lines = UI.Child(reward, "Lines");
        UI.StackVertically(lines.gameObject, 0, 10, false);
        Transform line = UI.Child(lines, "Line template");
        Row(line.gameObject, 0, 0, 12);
        TextMeshProUGUI lineLabel = UI.Text(line, "Label", "1st place of 4", 24, UI.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        lineLabel.textWrappingMode = TextWrappingModes.Normal;
        Flexible(lineLabel.gameObject, 80);
        UI.Text(line, "Amount", "+0 kr", 24, UI.Ink, FontStyles.Bold, TextAlignmentOptions.TopRight);
        Image divider = Img(reward, "Divider");
        divider.color = new Color(UI.InkMuted.r, UI.InkMuted.g, UI.InkMuted.b, 0.35f);
        divider.raycastTarget = false;
        Height(divider.gameObject, 2);
        Transform balanceRow = UI.Child(reward, "Balance row");
        Row(balanceRow.gameObject, 0, 0, 12);
        TextMeshProUGUI balanceLabel = UI.Text(balanceRow, "Label", "Your money", 26, UI.InkMuted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Flexible(balanceLabel.gameObject, 80);
        TextMeshProUGUI balance = UI.Text(balanceRow, "Value", "0 kr", 34, UI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);

        Transform vote = UI.Child(side, "Vote panel");
        UI.Stretch((RectTransform)vote, 40, 36, 40, 36);
        UI.StackVertically(vote.gameObject, 0, 12, false);
        TextMeshProUGUI voteTitle = UI.Text(vote, "Title", "Who was the imposter?", 38, UI.Ink, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        voteTitle.textWrappingMode = TextWrappingModes.Normal;
        TextMeshProUGUI voteHint = UI.Text(vote, "Hint", "One runner was trying to finish last.", 24, UI.InkMuted, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        voteHint.textWrappingMode = TextWrappingModes.Normal;
        Transform buttons = UI.Child(vote, "Buttons");
        UI.StackVertically(buttons.gameObject, 0, 6, false);
        Transform voteButton = UI.Child(buttons, "Button template");
        UI.GetOrAdd<Image>(voteButton.gameObject);
        Button voteButtonComponent = UI.GetOrAdd<Button>(voteButton.gameObject);
        UI.Text(voteButton, "Label", "Runner name", 24, UI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        UI.StyleButton(voteButtonComponent, UI.ButtonKind.Soft, 46, 24, null);
        Height(voteButton.gameObject, 46);
        TextMeshProUGUI voteStatus = UI.Text(vote, "Status", "", 22, UI.InkMuted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);

        // Footer
        Button lobbyButton = NewButton(t, "Lobby button", "Back to lobby", UI.ButtonKind.Primary, new Vector2(380, 80), 34);
        Button revealButton = NewButton(t, "Reveal now button", "Reveal now", UI.ButtonKind.Light, new Vector2(320, 68), 28);
        foreach (Button button in new[] { lobbyButton, revealButton })
        {
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0, 36);
            UI.AddDropShadow(button.gameObject, 8);
        }
        TextMeshProUGUI waiting = UI.Text(t, "Waiting text", "", 28, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        UI.Place(waiting.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 52), new Vector2(1400, 44));
        UI.ApplyShadow(waiting);

        ui.rootGroup = rootGroup;
        ui.subtitleText = subtitle;
        ui.rowParent = rows;
        ui.rowTemplate = row.gameObject;
        ui.rewardPanel = reward.gameObject;
        ui.rewardTotalText = total;
        ui.rewardLineParent = lines;
        ui.rewardLineTemplate = line.gameObject;
        ui.balanceText = balance;
        ui.votePanel = vote.gameObject;
        ui.voteTitleText = voteTitle;
        ui.voteHintText = voteHint;
        ui.voteButtonParent = buttons;
        ui.voteButtonTemplate = voteButton.gameObject;
        ui.voteStatusText = voteStatus;
        ui.lobbyButton = lobbyButton;
        ui.revealNowButton = revealButton;
        ui.waitingText = waiting;
        ui.accentColor = UI.Accent;
        ui.winnerBadgeColor = UI.Accent;
        ui.badgeColor = UI.Forest;
        ui.rowColor = UI.Soft;

        SavePrefab(root, EndScreenPath);
    }

    // ---- Imposter overlay ----

    static void BuildOverlayPrefab()
    {
        GameObject root = NewCanvasRoot("ImposterOverlay", 600);
        ImposterOverlay overlay = root.AddComponent<ImposterOverlay>();
        Transform t = root.transform;

        Transform stage = UI.Child(t, "Stage");
        UI.Stretch((RectTransform)stage, 0, 0, 0, 0);
        CanvasGroup stageGroup = stage.gameObject.AddComponent<CanvasGroup>();

        Image dark = Img(stage, "Dark");
        dark.color = Night;
        UI.Stretch(dark.rectTransform, 0, 0, 0, 0);

        Image vignetteImage = Img(stage, "Vignette");
        vignetteImage.sprite = vignette;
        vignetteImage.color = new Color(0.7f, 0.07f, 0.17f, 0.5f);
        vignetteImage.raycastTarget = false;
        UI.Stretch(vignetteImage.rectTransform, 0, 0, 0, 0);

        Transform shake = UI.Child(stage, "Shake root");
        UI.Stretch((RectTransform)shake, 0, 0, 0, 0);

        TextMeshProUGUI small = UI.Text(shake, "Small text", "THE IMPOSTER WAS", 34, Bone, FontStyles.Bold, TextAlignmentOptions.Center);
        small.characterSpacing = 30;
        UI.Place(small.rectTransform, UI.Center, UI.Center, new Vector2(0, 180), new Vector2(1600, 50));

        TextMeshProUGUI big = UI.Text(shake, "Big text", "RUNNER NAME", 130, Bone, FontStyles.Bold, TextAlignmentOptions.Center);
        UI.Place(big.rectTransform, UI.Center, UI.Center, new Vector2(0, 30), new Vector2(1700, 200));
        big.enableAutoSizing = true;
        big.fontSizeMin = 56;
        big.fontSizeMax = 130;
        big.characterSpacing = 6;

        TextMeshProUGUI verdict = UI.Text(shake, "Verdict text", "CAUGHT!", 84, Blood, FontStyles.Bold, TextAlignmentOptions.Center);
        UI.Place(verdict.rectTransform, UI.Center, UI.Center, new Vector2(0, -150), new Vector2(1200, 110));
        verdict.rectTransform.localRotation = Quaternion.Euler(0, 0, -5f);
        verdict.characterSpacing = 10;

        TextMeshProUGUI detail = UI.Text(shake, "Detail text", "2 of 3 runners guessed right.", 32, Bone, FontStyles.Normal, TextAlignmentOptions.Center);
        UI.Place(detail.rectTransform, UI.Center, UI.Center, new Vector2(0, -270), new Vector2(1500, 90));
        detail.textWrappingMode = TextWrappingModes.Normal;

        Image flash = Img(stage, "Flash");
        flash.color = new Color(1f, 1f, 1f, 0f);
        flash.raycastTarget = false;
        UI.Stretch(flash.rectTransform, 0, 0, 0, 0);

        // Small reminder of your role during the race
        Transform chip = UI.Child(t, "Role chip");
        Image chipImage = UI.GetOrAdd<Image>(chip.gameObject);
        chipImage.sprite = UI.pill;
        chipImage.type = Image.Type.Sliced;
        chipImage.pixelsPerUnitMultiplier = 128f / 52f;
        chipImage.color = ChipDark;
        chipImage.raycastTarget = false;
        UI.Place((RectTransform)chip, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(420, 52));
        Row(chip.gameObject, 22, 26, 12);
        chip.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        ContentSizeFitter chipFitter = UI.GetOrAdd<ContentSizeFitter>(chip.gameObject);
        chipFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        chipFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        Image dot = Img(chip, "Dot");
        dot.sprite = UI.pill;
        dot.type = Image.Type.Simple;
        dot.color = Blood;
        dot.raycastTarget = false;
        Fixed(dot.gameObject, 14, 14);
        TextMeshProUGUI chipText = UI.Text(chip, "Text", "Imposter mode", 24, Bone, FontStyles.Bold, TextAlignmentOptions.Center);

        overlay.stage = stage.gameObject;
        overlay.stageGroup = stageGroup;
        overlay.vignette = vignetteImage;
        overlay.flash = flash;
        overlay.shakeRoot = (RectTransform)shake;
        overlay.smallText = small;
        overlay.bigText = big;
        overlay.verdictText = verdict;
        overlay.detailText = detail;
        overlay.roleChip = chip.gameObject;
        overlay.roleChipText = chipText;
        overlay.boneColor = Bone;
        overlay.bloodColor = Blood;

        SavePrefab(root, OverlayPath);
    }

    // ---- In-game HUD ----

    static void StyleLeaderboardEntryPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LeaderboardEntryPath);
        Image image = UI.GetOrAdd<Image>(root);
        UI.SoftSurface(image);
        image.pixelsPerUnitMultiplier = 2.4f;
        image.raycastTarget = false;
        Height(root, 44);
        Row(root, 14, 14, 10);

        SerializedObject entry = new SerializedObject(root.GetComponent<LeaderboardEntryUI>());
        TextMeshProUGUI position = entry.FindProperty("positionText").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI name = entry.FindProperty("nameText").objectReferenceValue as TextMeshProUGUI;
        TextMeshProUGUI time = entry.FindProperty("timeText").objectReferenceValue as TextMeshProUGUI;
        if (position != null)
        {
            UI.StyleText(position, 20, UI.InkMuted, FontStyles.Bold, TextAlignmentOptions.Midline);
            Width(position.gameObject, 26);
        }
        if (name != null)
        {
            UI.StyleText(name, 22, UI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Flexible(name.gameObject, 60);
        }
        if (time != null)
        {
            UI.StyleText(time, 20, UI.Ink, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            Width(time.gameObject, 100);
        }
        PrefabUtility.SaveAsPrefabAsset(root, LeaderboardEntryPath);
        PrefabUtility.UnloadPrefabContents(root);
        UI.log.AppendLine("leaderboard entry styled");
    }

    static void StyleDashboardPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(DashboardPath);
        UI.Stretch((RectTransform)root.transform, 0, 0, 0, 0);

        // Clock in a pill at the top right
        Transform clock = root.transform.Find("Clock");
        if (clock != null)
        {
            UI.Place((RectTransform)clock, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-32, -32), new Vector2(250, 68));
            Image clockBg = Img(clock, "Bg");
            clockBg.transform.SetAsFirstSibling();
            clockBg.sprite = UI.pill;
            clockBg.type = Image.Type.Sliced;
            clockBg.pixelsPerUnitMultiplier = 128f / 68f;
            clockBg.color = UI.Card;
            clockBg.raycastTarget = false;
            UI.Stretch(clockBg.rectTransform, 0, 0, 0, 0);
            UI.AddDropShadow(clockBg.gameObject, 6);

            ClockDigits(clock.Find("Timmar"), 26, 62, TextAnchor.MiddleRight);
            ClockDigits(clock.Find("Minuter"), 88, 62, TextAnchor.MiddleRight);
            ClockDigits(clock.Find("Sekunder"), 152, 90, TextAnchor.MiddleLeft);
        }

        // "Time: ..." shown when you reach the finish
        Transform yourTime = root.transform.Find("Your time");
        if (yourTime != null)
        {
            // A small card at the top edge (Clock.PlaceFinishTimeAtTop sets the same), so it does not hide the race
            UI.Place((RectTransform)yourTime, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(460, 96));
            Text text = yourTime.GetComponent<Text>();
            text.fontSize = 40;
            text.fontStyle = FontStyle.Bold;
            text.color = UI.Ink;
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            Outline oldOutline = yourTime.GetComponent<Outline>();
            if (oldOutline != null) UnityEngine.Object.DestroyImmediate(oldOutline);

            // A card behind the text. It sits on its own canvas one step back, because a child image
            // would otherwise be drawn on top of the text it belongs to.
            Image timeBg = Img(yourTime, "Bg");
            UI.Stretch(timeBg.rectTransform, 0, 0, 0, 0);
            timeBg.sprite = UI.rounded;
            timeBg.type = Image.Type.Sliced;
            timeBg.pixelsPerUnitMultiplier = 1f;
            timeBg.color = UI.Card;
            timeBg.raycastTarget = false;
            UI.AddDropShadow(timeBg.gameObject, 8);
            Canvas timeBgCanvas = UI.GetOrAdd<Canvas>(timeBg.gameObject);
            // Set through the serialized fields: the plain property is ignored while the prefab has no parent canvas
            SerializedObject timeBgSerialized = new SerializedObject(timeBgCanvas);
            timeBgSerialized.FindProperty("m_OverrideSorting").boolValue = true;
            timeBgSerialized.FindProperty("m_SortingOrder").intValue = -1;
            timeBgSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // The host's button that ends the competition
        Transform end = root.transform.Find("Retrurn To Lobby");
        if (end != null)
        {
            Button button = end.GetComponent<Button>();
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                if (button.onClick.GetPersistentTarget(i) == null)
                {
                    UnityEventTools.RemovePersistentListener(button.onClick, i); // old link to a deleted object
                }
            }
            UI.Place((RectTransform)end, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 56), new Vector2(420, 88));
            UI.StyleButton(button, UI.ButtonKind.Primary, 88, 36, "End competition");
            UI.AddDropShadow(end.gameObject, 8);
        }

        PrefabUtility.SaveAsPrefabAsset(root, DashboardPath);
        PrefabUtility.UnloadPrefabContents(root);
        UI.log.AppendLine("dashboard styled");
    }

    static void ClockDigits(Transform digits, float x, float width, TextAnchor alignment)
    {
        if (digits == null) return;
        RectTransform rect = (RectTransform)digits;
        rect.localScale = Vector3.one;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0);
        rect.sizeDelta = new Vector2(width, 56);
        Text text = digits.GetComponent<Text>();
        text.fontSize = 38;
        text.fontStyle = FontStyle.Bold;
        text.color = UI.Ink;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
    }

    static void StyleGameScene()
    {
        GameObject canvasObject = GameObject.FindGameObjectWithTag("Canvas");
        if (canvasObject == null)
        {
            UI.log.AppendLine("no Canvas tag in " + SceneManager.GetActiveScene().name);
            return;
        }
        Transform canvas = canvasObject.transform;

        // Undo button: round, top left
        Transform undo = canvas.Find("UndoButton");
        if (undo != null)
        {
            Image image = undo.GetComponent<Image>();
            if (undo.Find("Icon") == null)
            {
                Image icon = Img(undo, "Icon");
                icon.sprite = image.sprite;
                icon.raycastTarget = false;
                icon.transform.SetAsFirstSibling();
                UI.Stretch(icon.rectTransform, 0, 0, 0, 0);
                image.sprite = UI.pill;
                image.type = Image.Type.Simple;
                image.color = Color.white;
                UI.GetOrAdd<Mask>(undo.gameObject).showMaskGraphic = true;
            }
            UI.Place((RectTransform)undo, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -32), new Vector2(84, 84));
        }

        // Leaderboard card under the undo button
        Transform panel = canvas.Find("LeaderboardPanel");
        if (panel != null)
        {
            ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
            if (fitter != null) UnityEngine.Object.DestroyImmediate(fitter);
            UI.Place((RectTransform)panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -136), new Vector2(430, 300));
            UI.StyleCard(panel.gameObject);
            panel.GetComponent<Image>().raycastTarget = false;

            Transform header = panel.Find("Placement");
            if (header != null)
            {
                TextMeshProUGUI headerText = header.GetComponent<TextMeshProUGUI>();
                headerText.text = "Leaderboard";
                UI.StyleText(headerText, 28, UI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                headerText.raycastTarget = false;
                UI.Place((RectTransform)header, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -12), new Vector2(320, 40));
            }

            Transform scroll = panel.Find("Scroll View");
            if (scroll != null)
            {
                UI.Stretch((RectTransform)scroll, 14, 62, 14, 14);
                Image scrollImage = scroll.GetComponent<Image>();
                if (scrollImage != null) scrollImage.color = new Color(1, 1, 1, 0);
                ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
                scrollRect.horizontal = false;
                scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

                Transform viewport = scroll.Find("Viewport");
                if (viewport != null)
                {
                    UI.Stretch((RectTransform)viewport, 0, 0, 0, 0);
                    Transform content = viewport.Find("Content");
                    if (content != null)
                    {
                        RectTransform contentRect = (RectTransform)content;
                        contentRect.anchorMin = new Vector2(0, 1);
                        contentRect.anchorMax = new Vector2(1, 1);
                        contentRect.pivot = new Vector2(0.5f, 1);
                        contentRect.anchoredPosition = Vector2.zero;
                        contentRect.sizeDelta = new Vector2(0, contentRect.sizeDelta.y);
                        VerticalLayoutGroup group = content.GetComponent<VerticalLayoutGroup>();
                        group.padding = new RectOffset(0, 0, 0, 0);
                        group.spacing = 6;
                        group.childControlWidth = true;
                        group.childControlHeight = true;
                        group.childForceExpandWidth = true;
                        group.childForceExpandHeight = false;
                    }
                }
                Transform scrollbar = scroll.Find("Scrollbar Vertical");
                if (scrollbar != null)
                {
                    RectTransform barRect = (RectTransform)scrollbar;
                    barRect.sizeDelta = new Vector2(6, barRect.sizeDelta.y);
                }
            }
        }

        // Follow-runner switch next to the undo button. CameraController shows it while the runner is running.
        Transform follow = canvas.Find("FollowRunnerButton");
        if (follow == null)
        {
            follow = UI.Child(canvas, "FollowRunnerButton");
            UI.GetOrAdd<Image>(follow.gameObject);
            UI.GetOrAdd<Button>(follow.gameObject);
            UI.Text(follow, "Text (TMP)", "Follow runner: OFF", 24, UI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            if (undo != null) follow.SetSiblingIndex(undo.GetSiblingIndex() + 1);
        }
        UI.Place((RectTransform)follow, new Vector2(0, 1), new Vector2(0, 1), new Vector2(132, -42), new Vector2(280, 64));
        UI.StyleButton(follow.GetComponent<Button>(), UI.ButtonKind.Light, 64, 24, "Follow runner: OFF");
        UI.AddDropShadow(follow.gameObject, 6);
        follow.gameObject.SetActive(false);

        // The object that carries the leaderboard and the course must survive a host change
        LeaderboardManager leaderboard = UnityEngine.Object.FindFirstObjectByType<LeaderboardManager>(FindObjectsInactive.Include);
        FollowTheHost(leaderboard != null ? leaderboard.gameObject : null);

        Transform waiting = canvas.Find("WaitingForPlayersToConnect/WaitingForPlayersToConnectText");
        if (waiting != null)
        {
            TextMeshProUGUI waitingText = waiting.GetComponent<TextMeshProUGUI>();
            UI.StyleText(waitingText, 72, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            UI.ApplyShadow(waitingText);
        }
        UI.log.AppendLine("HUD styled in " + SceneManager.GetActiveScene().name);
    }

    // ---- Money pill ----

    static void AddMoneyPill(Transform canvas, bool left)
    {
        Transform pill = canvas.Find("Money pill");
        if (pill == null)
        {
            pill = UI.Child(canvas, "Money pill");
            Transform subtitle = canvas.Find("Subtitle");
            pill.SetSiblingIndex(subtitle != null ? subtitle.GetSiblingIndex() + 1 : 0);
        }
        Image image = UI.GetOrAdd<Image>(pill.gameObject);
        image.sprite = UI.pill;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 128f / 60f;
        image.color = UI.Cream;
        image.raycastTarget = false;
        Vector2 corner = left ? new Vector2(0, 1) : new Vector2(1, 1);
        UI.Place((RectTransform)pill, corner, corner, new Vector2(left ? 48 : -48, -48), new Vector2(220, 60));
        Row(pill.gameObject, 20, 26, 12);
        pill.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        ContentSizeFitter fitter = UI.GetOrAdd<ContentSizeFitter>(pill.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        UI.AddDropShadow(pill.gameObject, 6);

        Image coin = Img(pill, "Coin");
        coin.sprite = UI.pill;
        coin.type = Image.Type.Simple;
        coin.color = UI.Accent;
        coin.raycastTarget = false;
        Fixed(coin.gameObject, 22, 22);
        TextMeshProUGUI amount = UI.Text(pill, "Amount", "0 kr", 28, UI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);

        MoneyLabel label = UI.GetOrAdd<MoneyLabel>(pill.gameObject);
        label.text = amount;
        EditorUtility.SetDirty(label);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)pill);
        UI.log.AppendLine("money pill in " + canvas.name);
    }

    // ---- Building blocks ----

    static GameObject NewCanvasRoot(string name, int sortingOrder)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.layer = LayerMask.NameToLayer("UI");
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        UI.SetupScaler(root.transform);
        return root;
    }

    static void SavePrefab(GameObject root, string path)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = root.layer;
        }
        PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
        UnityEngine.Object.DestroyImmediate(root);
        UI.log.AppendLine(path + " saved=" + success);
    }

    static Image Img(Transform parent, string name)
    {
        return UI.GetOrAdd<Image>(UI.Child(parent, name).gameObject);
    }

    static Button NewButton(Transform parent, string name, string label, UI.ButtonKind kind, Vector2 size, float fontSize)
    {
        Transform child = UI.Child(parent, name);
        UI.GetOrAdd<Image>(child.gameObject);
        Button button = UI.GetOrAdd<Button>(child.gameObject);
        UI.Text(child, "Label", label, fontSize, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        ((RectTransform)child).sizeDelta = size;
        UI.StyleButton(button, kind, size.y, fontSize, label);
        return button;
    }

    // Lays the children out in a row, each at its own preferred size
    static void Row(GameObject target, int left, int right, float spacing)
    {
        HorizontalLayoutGroup group = UI.GetOrAdd<HorizontalLayoutGroup>(target);
        group.padding = new RectOffset(left, right, 0, 0);
        group.spacing = spacing;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
    }

    static void Height(GameObject target, float height)
    {
        LayoutElement layout = UI.GetOrAdd<LayoutElement>(target);
        layout.minHeight = height;
        layout.preferredHeight = height;
    }

    static void Width(GameObject target, float width)
    {
        LayoutElement layout = UI.GetOrAdd<LayoutElement>(target);
        layout.minWidth = width;
        layout.preferredWidth = width;
    }

    static void Fixed(GameObject target, float width, float height)
    {
        Width(target, width);
        Height(target, height);
    }

    static void Flexible(GameObject target, float minWidth)
    {
        LayoutElement layout = UI.GetOrAdd<LayoutElement>(target);
        layout.minWidth = minWidth;
        layout.flexibleWidth = 1;
    }
}
