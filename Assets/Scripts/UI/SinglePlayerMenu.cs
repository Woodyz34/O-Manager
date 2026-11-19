using System;
using System.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The single-player page. Since 2026-11-13 it looks and works like the multiplayer lobby, so nothing is new when
// you go from one to the other: the same strip at the top (runner pool, game mode, course), the same "Runner setup"
// card with Change color / Change course / Runner pool / Game mode, the lobby's own course window (My courses,
// Community, AI course) and its runner pool and game mode windows. Only the game modes that work alone are offered
// (Normal, Route analysis, Route choice game: ExtraModes.Solo).
// No server and no lobby: the competition runs in Fusion's Single mode on this computer, with the same scene and
// rules as a multiplayer game with one player. When the results are closed, you come back here.
public class SinglePlayerMenu : MonoBehaviour
{
    public NetworkRunner runnerPrefab;        // Prefabs/Network/NetworkRunnerPF (has the Spawner)

    // The runner pool and game modes picked here, read by NetworkPlayer and ExtraModes when the competition starts
    public static string ChosenGameMode { get; private set; }
    public static ExtraMode ChosenModes { get; private set; }

    // What was picked last, so the page looks the same when you come back after a race
    static string pool = GameModes.YourRunners;
    static ExtraMode modes = ExtraMode.None;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ChosenGameMode = null;
        ChosenModes = ExtraMode.None;
        pool = GameModes.YourRunners;
        poolLoaded = false;
        modes = ExtraMode.None;
    }

    static bool poolLoaded;

    const string BackdropSprite = "Sprites/e2bf7ebc-02a1-4b78-b52f-e0211eeac746";   // the lobby's forest picture

    MapHandler mapHandler;
    MapSelectUI courseWindow;
    TextMeshProUGUI poolValue;
    TextMeshProUGUI modeValue;
    TextMeshProUGUI courseValue;
    TextMeshProUGUI poolInfo;
    RawImage coursePicture;
    AspectRatioFitter coursePictureFit;
    TextMeshProUGUI courseName;
    TextMeshProUGUI courseFacts;
    TextMeshProUGUI problemText;
    TextMeshProUGUI moneyText;
    Button startButton;
    bool starting;
    float nextRefresh;
    string shownState;

    void Start()
    {
        ChosenGameMode = null;
        ChosenModes = ExtraMode.None;
        // The first time this session: the runner pool picked last time (here or in a lobby)
        if (!poolLoaded)
        {
            poolLoaded = true;
            pool = GameModeHandler.LastPool ?? GameModes.YourRunners;
        }
        if (GameManager.Instance == null)
        {
            GameManager manager = new GameObject("GameManager").AddComponent<GameManager>();
            manager.SetConnectionToken(ConnectionTokenUtils.NewToken());
        }
        else if (GameManager.Instance.GetConnectionsToken() == null)
        {
            GameManager.Instance.SetConnectionToken(ConnectionTokenUtils.NewToken());
        }
        string name = PlayerPrefs.GetString(LocalPrefs.Key("PlayerNickName"), "");
        GameManager.Instance.playerNickName = string.IsNullOrWhiteSpace(name) ? "You" : name;

        mapHandler = gameObject.AddComponent<MapHandler>();
        Build();
        Refresh();

        // A weekly or daily contest from the contests page: start it at once with its own course, runner and mode
        if (ContestRun.StartPending) StartContest();
    }

    CourseData Chosen => mapHandler != null ? mapHandler.course : CourseLibrary.CurrentOrDefault;

    // ---- Page ----

    void Build()
    {
        GameObject canvasObject = new GameObject("Single player UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        ScreenSettings.Fit(canvasObject.GetComponent<CanvasScaler>());

        // The lobby's forest picture, covering the old page underneath
        Image back = CourseUI.Panel("Background", canvasObject.transform, CourseUI.Hex("DCE8D6"), false);
        CourseUI.Stretch(back.rectTransform);
        back.gameObject.AddComponent<RectMask2D>();
        Sprite forest = Resources.Load<Sprite>(BackdropSprite);
        if (forest != null)
        {
            Image picture = CourseUI.Panel("Forest", back.transform, Color.white, false);
            picture.sprite = forest;
            picture.raycastTarget = false;
            AspectRatioFitter fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = forest.rect.width / Mathf.Max(1f, forest.rect.height);
        }
        Transform page = back.transform;

        // Top left: out of here (the lobby's "Leave lobby")
        Button leave = CourseUI.Button("Back to menu", page, "Back to menu", CourseUI.ButtonKind.Light, () => SceneManager.LoadScene("MainMenu"), 26);
        CourseUI.Place((RectTransform)leave.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48, -40), new Vector2(250, 66));

        // Top right: your money
        Image moneyPill = CourseUI.Panel("Money pill", page, CourseUI.Card);
        if (CourseUI.Skin != null && CourseUI.Skin.pill != null) moneyPill.sprite = CourseUI.Skin.pill;
        CourseUI.Place(moneyPill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48, -40), new Vector2(220, 66));
        moneyText = CourseUI.Text("Amount", moneyPill.transform, "", 26, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(moneyText.rectTransform, 12, 12, 0, 0);

        // Top middle: what you will play, as in the lobby
        Image setup = CourseUI.CardPanel("Game setup card", page);
        CourseUI.Place(setup.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(1180, 96));
        HorizontalLayoutGroup row = CourseUI.Row(setup.gameObject, 24, new RectOffset(30, 30, 10, 10));
        row.childForceExpandWidth = false;
        row.childAlignment = TextAnchor.MiddleCenter;
        poolValue = HeaderColumn(setup.transform, "RUNNER POOL", 300);
        modeValue = HeaderColumn(setup.transform, "GAME MODE", 300);
        courseValue = HeaderColumn(setup.transform, "COURSE", 460);
        HoverTip.Add(poolValue.transform.parent, () => GameModes.ExplainPool(pool, (modes & ExtraModes.NoRunners) != 0));
        HoverTip.Add(modeValue.transform.parent, () => ExtraModes.Explain(modes));
        HoverTip.Add(courseValue.transform.parent, UIHints.Course);
        HoverTip.Add(leave, "Back to the main menu.");
        HoverTip.Add(moneyPill, UIHints.Money);

        // Left: the lobby's "Runner setup" card
        Image left = CourseUI.CardPanel("Runner setup", page);
        CourseUI.Place(left.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48, -170), new Vector2(460, 560));
        CourseUI.Column(left.gameObject, 14, new RectOffset(28, 28, 24, 24));
        TextMeshProUGUI setupTitle = CourseUI.Text("Title", left.transform, "Runner setup", 36, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(setupTitle, 48);
        SetupButton(left.transform, "Change color", PlayerColorPicker.Open, UIHints.ChangeColor);
        SetupButton(left.transform, "Change course", OpenCourses, UIHints.ChangeCourse);
        SetupButton(left.transform, "Runner pool", OpenPools, UIHints.RunnerPool);
        SetupButton(left.transform, "Game mode", OpenModes, UIHints.GameMode);
        poolInfo = CourseUI.Text("Pool info", left.transform, "", 20, CourseUI.InkMuted);
        CourseUI.Size(poolInfo, 120);

        // Right: the chosen course (where the lobby has its list of players)
        Image right = CourseUI.CardPanel("Course", page);
        CourseUI.Place(right.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48, -170), new Vector2(560, 640));
        CourseUI.Column(right.gameObject, 10, new RectOffset(28, 28, 24, 24));
        TextMeshProUGUI courseTitle = CourseUI.Text("Title", right.transform, "Course", 36, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(courseTitle, 48);
        Image frame = CourseUI.Panel("Picture", right.transform, CourseUI.CardSolid);
        CourseUI.Size(frame, 300);
        frame.gameObject.AddComponent<RectMask2D>();
        Button frameButton = frame.gameObject.AddComponent<Button>();
        frameButton.targetGraphic = frame;
        frameButton.onClick.AddListener(OpenCourses);
        coursePicture = CourseUI.Rect("Map", frame.transform).gameObject.AddComponent<RawImage>();
        coursePicture.raycastTarget = false;
        CourseUI.Stretch(coursePicture.rectTransform);
        coursePictureFit = coursePicture.gameObject.AddComponent<AspectRatioFitter>();
        coursePictureFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        courseName = CourseUI.Text("Name", right.transform, "", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(courseName, 38);
        courseFacts = CourseUI.Text("Facts", right.transform, "", 21, CourseUI.InkMuted);
        CourseUI.Size(courseFacts, 60);
        problemText = CourseUI.Text("Problem", right.transform, "", 20, CourseUI.Danger);
        CourseUI.Size(problemText, 90);

        // Bottom middle: the lobby's "Start game"
        startButton = CourseUI.Button("Start game", page, "Start game", CourseUI.ButtonKind.Primary, StartGame, 40);
        CourseUI.Place((RectTransform)startButton.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 56), new Vector2(400, 100));

        HoverTip.Add(startButton, "Start the race with these settings.");
        HoverTip.Add(frame, "The course you will race. Click to choose another one.");

        BuildCourseWindow(page);
    }

    static TextMeshProUGUI HeaderColumn(Transform parent, string caption, float width)
    {
        RectTransform column = CourseUI.Rect(caption, parent);
        CourseUI.Column(column.gameObject, 2, new RectOffset(0, 0, 0, 0));
        CourseUI.Size(column, 76, width);
        TextMeshProUGUI captionText = CourseUI.Text("Caption", column, caption, 17, CourseUI.InkMuted, FontStyles.Bold, TextAlignmentOptions.Center);
        captionText.characterSpacing = 8;
        CourseUI.Size(captionText, 24);
        TextMeshProUGUI value = CourseUI.Text("Value", column, "...", 28, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Center);
        value.richText = true;
        value.enableWordWrapping = false;
        value.enableAutoSizing = true;
        value.fontSizeMax = 28;
        value.fontSizeMin = 14;
        CourseUI.Size(value, 44);
        return value;
    }

    static void SetupButton(Transform parent, string label, UnityEngine.Events.UnityAction click, string tip)
    {
        Button button = CourseUI.Button(label, parent, label, CourseUI.ButtonKind.Light, click, 26);
        CourseUI.Size(button, 66);
        HoverTip.Add(button, tip);
    }

    // The lobby's course window (MapSelectUI) with its tabs, search and AI course, built here in code
    void BuildCourseWindow(Transform page)
    {
        Image window = CourseUI.Panel("SelectMapPanel", page, CourseUI.CardSolid);
        CourseUI.Stretch(window.rectTransform);
        TextMeshProUGUI heading = CourseUI.Text("Heading", window.transform, "Choose course", 48, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(1200, 70));
        RectTransform list = CourseUI.ScrollList("Scroll", window.transform, out _);
        RectTransform viewport = (RectTransform)list.parent;
        viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 1f);
        viewport.pivot = new Vector2(0.5f, 0.5f);
        GridLayoutGroup grid = list.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.Flexible;

        courseWindow = gameObject.AddComponent<MapSelectUI>();
        courseWindow.mapListParent = list;
        courseWindow.mapHandler = mapHandler;
        courseWindow.mapSelectPanel = window.gameObject;

        Button close = CourseUI.Button("Close", window.transform, "Close", CourseUI.ButtonKind.Light, courseWindow.ClosePanel, 26);
        CourseUI.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36, -36), new Vector2(180, 60));
        HoverTip.Add(close, "Close this window.");
        window.gameObject.SetActive(false);
    }

    void OpenCourses()
    {
        if (courseWindow == null) return;
        courseWindow.OpenPanel();
        courseWindow.mapSelectPanel.transform.SetAsLastSibling();
    }

    void OpenPools()
    {
        ChoicePopup.Open("Runner pool", GameModeSelect.PoolOptions(pool), GameModes.PoolId(pool),
            id => GameModeSelect.Choose(id, pool, mode =>
            {
                pool = mode;
                GameModeHandler.LastPool = mode;
                nextRefresh = 0f;
            }),
            "Who you can run with. The same runner pools as in a multiplayer lobby.");
    }

    void OpenModes()
    {
        ModesPopup.Open(modes, chosen =>
        {
            modes = chosen & ExtraModes.Solo;
            nextRefresh = 0f;
        }, ExtraModes.Solo, "The game modes that work when you play alone.");
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        Refresh();
    }

    void Refresh()
    {
        if (startButton == null) return;
        CourseData course = Chosen;
        string problem = Problem();
        string money = Money.Instance != null ? Money.Instance.GetMoney() + " kr" : "";
        string state = (course != null ? course.id + course.name : "") + "|" + pool + "|" + (int)modes + "|" + problem + "|" + starting
                       + "|" + GameModes.OwnedRunnerCount() + "|" + money;
        if (state == shownState) return;
        shownState = state;

        moneyText.text = money;
        moneyText.transform.parent.gameObject.SetActive(money.Length > 0);
        bool noRunners = (modes & ExtraModes.NoRunners) != 0;
        poolValue.text = noRunners ? "No runners" : GameModes.PoolName(pool);
        modeValue.text = ExtraModes.Describe(modes);
        courseValue.text = course != null ? LobbySettingsUI.CourseTitle(course) : "Pick a course";

        int owned = GameModes.OwnedRunnerCount();
        if (noRunners)
            poolInfo.text = ExtraModes.Describe(modes) + " gives you a set runner, so the runner pool does not matter.";
        else if (GameModes.IsYourRunners(pool))
            poolInfo.text = "You run with the runners you own: you have " + owned + (owned == 1 ? " runner." : " runners.")
                            + " Runners you are missing for the legs are lent for the race.";
        else
            poolInfo.text = GameModes.PoolDescription(pool);

        Texture2D thumbnail = course != null ? CourseThumbnail.Get(course) : null;
        coursePicture.texture = thumbnail;
        coursePicture.enabled = thumbnail != null;
        if (thumbnail != null) coursePictureFit.aspectRatio = thumbnail.width / Mathf.Max(1f, thumbnail.height);
        courseName.text = course != null ? course.name : "No course chosen";
        if (course != null)
        {
            string author = string.IsNullOrEmpty(course.author) ? "" : "by " + course.author + "  ·  ";
            courseFacts.text = author + CourseFilter.Facts(course);
        }
        else
        {
            courseFacts.text = "Press Change course to pick one.";
        }
        problemText.text = problem ?? "";

        startButton.interactable = course != null && problem == null && !starting;
    }

    // Why the chosen course can't be run with what is picked, or null
    string Problem()
    {
        CourseData course = Chosen;
        string courseProblem = CourseLibrary.StartProblem(course);
        if (courseProblem != null) return courseProblem;
        string modeProblem = ExtraModes.CourseProblem(modes, course);
        if (modeProblem != null) return modeProblem;
        // Too few runners for a relay's legs: the missing ones are lent (2026-11-16)
        return null;
    }

    // ---- Starting and ending a game ----

    async void StartGame()
    {
        if (starting) return;
        string problem = Problem();
        if (problem != null)
        {
            UIToast.Show(problem);
            return;
        }
        starting = true;
        Refresh();

        // After a race on an AI course, a new one with the same settings is laid, as in the lobby
        if (AICoursePanel.ShouldRemake(Chosen))
        {
            await RemakeAICourse();
            if (this == null) return;
        }

        CourseData course = Chosen;
        CourseLibrary.Current = course;
        ChosenGameMode = string.IsNullOrWhiteSpace(pool) ? GameModes.YourRunners : pool;
        ChosenModes = modes & ExtraModes.Solo;
        if (MapSelectUI.IsAICourse(course)) AICoursePanel.RacedCourseId = course.id;
        await Launch();
    }

    // A weekly or daily contest (ContestRun): the contest's course, its runner values and game mode, nothing to pick
    async void StartContest()
    {
        if (starting) return;
        starting = true;
        GameObject cover = new GameObject("Contest cover", typeof(Canvas), typeof(CanvasScaler));
        Canvas coverCanvas = cover.GetComponent<Canvas>();
        coverCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        coverCanvas.sortingOrder = 200;
        ScreenSettings.Fit(cover.GetComponent<CanvasScaler>());
        Image sheet = CourseUI.Panel("Sheet", cover.transform, CourseUI.CardSolid, false);
        CourseUI.Stretch(sheet.rectTransform);
        TextMeshProUGUI text = CourseUI.Text("Text", sheet.transform, ContestRun.SpanName(ContestRun.Active) + "\n" + ContestRun.Title(ContestRun.Active) + "\n\nStarting...",
            44, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(text.rectTransform, 60, 60, 60, 60);

        CourseLibrary.Current = ContestRun.ActiveCourse;
        ChosenGameMode = ContestRun.GameModeFor(ContestRun.Active);
        ChosenModes = ContestRun.ModesFor(ContestRun.Active) & ExtraModes.Solo;
        ContestRun.Started();
        bool ok = await Launch();
        if (!ok)
        {
            ContestRun.Leave();
            SceneManager.LoadScene("MainMenu");
        }
        else if (cover != null)
        {
            Destroy(cover, 1.5f);
        }
    }

    // Starts the competition in Fusion's Single mode with what is set in CourseLibrary.Current and the Chosen* values
    async Task<bool> Launch()
    {
        NetworkRunner runner = Instantiate(runnerPrefab);
        runner.name = "Single player runner";
        runner.ProvideInput = true;
        INetworkSceneManager sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();
        if (sceneManager == null) sceneManager = runner.gameObject.AddComponent<SafeNetworkSceneManager>();
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/" + CourseScenes.Competition + ".unity");
        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Single,
            SessionName = "single-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Scene = SceneRef.FromIndex(sceneIndex),
            SceneManager = sceneManager,
            ConnectionToken = GameManager.Instance.GetConnectionsToken()
        });
        if (!result.Ok)
        {
            Debug.LogError("SinglePlayerMenu: could not start: " + result.ShutdownReason);
            starting = false;
            ChosenGameMode = null;
            ChosenModes = ExtraMode.None;
            if (this != null)
            {
                UIToast.Show("The game could not start: " + result.ShutdownReason);
                Refresh();
            }
            return false;
        }
        return true;
    }

    // The same card and loading bar as in the lobby (ReadyUIHandler)
    async Task RemakeAICourse()
    {
        CourseGenerator.Progress progress = new CourseGenerator.Progress();
        UIProgressCard card = UIProgressCard.Show("Laying a new AI course with the same length and controls...");
        Task<CourseGenerator.Result> making = AICoursePanel.Remake(progress);
        while (!making.IsCompleted)
        {
            await Task.Yield();
            if (this == null) { card.Close(); return; }
            card.Set(AICoursePanel.DescribeProgress(progress, out float share), share);
        }
        CourseGenerator.Result result;
        try { result = await making; }
        catch (Exception e) { result = new CourseGenerator.Result { error = e.Message }; }
        card.Close();
        if (this == null) return;
        if (result.course != null)
        {
            mapHandler.Select(result.course);
            UIToast.Show("New AI course laid. Starting...", 3f, CourseUI.Forest);
        }
        else
        {
            UIToast.Show("Could not lay a new AI course (" + result.error + "). Running the same course again.");
            AICoursePanel.RacedCourseId = null;
        }
    }

    // Leaving a single-player game: close it and come back to this page
    public static void EndGame(NetworkRunner runner)
    {
        ChosenGameMode = null;
        ChosenModes = ExtraMode.None;
        // A contest race goes back to the contests page in the main menu
        if (ContestRun.Active != null)
        {
            ContestRun.Leave();
            SceneLoader.LeaveSession(runner, "MainMenu");
            return;
        }
        SceneLoader.LeaveSession(runner, "SingelPlayer");
    }
}
