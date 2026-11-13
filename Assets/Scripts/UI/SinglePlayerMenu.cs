using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The single-player page: pick a course (search, individual or relay) and a game mode, then run it on your own.
// No server and no lobby: the competition runs in Fusion's Single mode on this computer, with the same scene and
// rules as a multiplayer game with one player. When the results are closed, you come back here.
public class SinglePlayerMenu : MonoBehaviour
{
    public NetworkRunner runnerPrefab;        // Prefabs/Network/NetworkRunnerPF (has the Spawner)

    // The mode picked here, read by NetworkPlayer when the competition starts
    public static string ChosenGameMode { get; private set; }

    static readonly string[] Modes = GameModes.Pools;

    CourseData chosen;
    string mode = GameModes.YourRunners;
    CourseFilter.Kind kind = CourseFilter.Kind.All;
    TMP_InputField searchField;
    Button kindButton;
    RectTransform courseList;
    TextMeshProUGUI chosenText;
    TextMeshProUGUI runnersText;
    readonly Dictionary<string, Button> modeButtons = new Dictionary<string, Button>();
    Button startButton;
    bool starting;

    void Start()
    {
        ChosenGameMode = null;
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

        chosen = CourseLibrary.Current ?? (CourseLibrary.BuiltIn.Count > 0 ? CourseLibrary.BuiltIn[0] : null);
        Build();
        Refresh();
    }

    // ---- Page ----

    void Build()
    {
        GameObject canvasObject = new GameObject("Single player UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        ScreenSettings.Fit(scaler);

        // Covers the old page underneath
        Image back = CourseUI.Panel("Background", canvasObject.transform, CourseUI.Hex("DCE8D6"), false);
        CourseUI.Stretch(back.rectTransform);

        TextMeshProUGUI title = CourseUI.Text("Title", back.transform, "Single player", 60, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(1200, 80));

        // Left: the courses. Both cards hang from the top middle, so on a wide screen they stay
        // together in the middle instead of drifting apart to the edges.
        Image left = CourseUI.CardPanel("Courses", back.transform);
        CourseUI.Place(left.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(-900, -130), new Vector2(1060, 900));
        CourseUI.Column(left.gameObject, 12, new RectOffset(28, 28, 24, 24));
        TextMeshProUGUI coursesTitle = CourseUI.Text("Title", left.transform, "Course", 34, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(coursesTitle, 44);

        RectTransform filterRow = CourseUI.Rect("Filter", left.transform);
        CourseUI.Row(filterRow.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(filterRow, 52);
        searchField = CourseUI.Input("Search", filterRow, "Search courses (name, author, relay...)", 40, _ => Refresh());
        CourseUI.Size(searchField, 52, 700);
        kindButton = CourseUI.Button("Kind", filterRow, CourseFilter.KindLabel(kind), CourseUI.ButtonKind.Light, () =>
        {
            kind = CourseFilter.Next(kind);
            CourseUI.SetButtonText(kindButton, CourseFilter.KindLabel(kind));
            CourseUI.SetButtonColor(kindButton, kind == CourseFilter.Kind.All ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
            Refresh();
        }, 22);
        CourseUI.Size(kindButton, 52, 280);

        courseList = CourseUI.ScrollList("List", left.transform, out _);
        CourseUI.Column(courseList.gameObject, 6, new RectOffset(0, 8, 0, 0));
        LayoutElement listLayout = CourseUI.Size(courseList.parent.GetComponent<Image>(), 600);
        listLayout.flexibleHeight = 1;

        // Right: game mode and start
        Image right = CourseUI.CardPanel("Play", back.transform);
        CourseUI.Place(right.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(900, -130), new Vector2(680, 900));
        CourseUI.Column(right.gameObject, 14, new RectOffset(28, 28, 24, 24));
        TextMeshProUGUI modeTitle = CourseUI.Text("Title", right.transform, "Runner pool", 34, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(modeTitle, 44);
        foreach (string m in Modes)
        {
            string captured = m;
            Button button = CourseUI.Button(m, right.transform, GameModes.PoolName(m), CourseUI.ButtonKind.Light, () => { mode = captured; Refresh(); }, 24);
            CourseUI.Size(button, 58);
            modeButtons[m] = button;
        }
        runnersText = CourseUI.Text("Runners", right.transform, "", 21, CourseUI.InkMuted);
        CourseUI.Size(runnersText, 90);

        TextMeshProUGUI chosenTitle = CourseUI.Text("Chosen title", right.transform, "Chosen course", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(chosenTitle, 38);
        chosenText = CourseUI.Text("Chosen", right.transform, "", 24, CourseUI.Ink);
        CourseUI.Size(chosenText, 110);

        LayoutElement spacer = CourseUI.Size(CourseUI.Rect("Space", right.transform), 10);
        spacer.flexibleHeight = 1;

        startButton = CourseUI.Button("Start", right.transform, "Start", CourseUI.ButtonKind.Primary, StartGame, 32);
        CourseUI.Size(startButton, 76);
        Button backButton = CourseUI.Button("Back", right.transform, "Back to menu", CourseUI.ButtonKind.Light, () => SceneManager.LoadScene("MainMenu"), 24);
        CourseUI.Size(backButton, 56);
    }

    void Refresh()
    {
        foreach (var pair in modeButtons)
        {
            CourseUI.SetButtonColor(pair.Value, pair.Key == mode ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
        }
        int owned = GameModes.OwnedRunnerCount();
        runnersText.text = GameModes.IsYourRunners(mode)
            ? "You run with the runners you own: you have " + owned + (owned == 1 ? " runner." : " runners.") + (owned < RunnerCollection.RaceMinimum ? " You borrow random runners up to 10 for the race." : "")
            : "Pick any runner in the game.";

        foreach (Transform child in courseList) Destroy(child.gameObject);
        // No built-in courses: the player's own and the community's
        List<CourseData> courses = new List<CourseData>(CourseLibrary.Mine());
        foreach (CourseData course in CourseLibrary.Community) if (!courses.Exists(c => c.id == course.id)) courses.Add(course);
        string search = searchField != null ? searchField.text : "";
        int shown = 0;
        foreach (CourseData course in courses)
        {
            if (!CourseFilter.Matches(course, search, kind)) continue;
            if (!MapDefinition.Exists(course.mapId)) continue;   // a map imported on someone else's computer
            shown++;
            CourseData captured = course;
            bool selected = chosen != null && chosen.id == course.id;
            RectTransform row = CourseUI.Rect("Course " + course.name, courseList);
            CourseUI.Row(row.gameObject, 10, new RectOffset(8, 0, 0, 0)).childForceExpandWidth = false;
            CourseUI.Size(row, 56);
            string author = string.IsNullOrEmpty(course.author) ? "" : "  <color=#5B7563>by " + course.author + "</color>";
            TextMeshProUGUI label = CourseUI.Text("Label", row, "<b>" + course.name + "</b>" + author + "\n<size=80%><color=#5B7563>" + CourseFilter.Facts(course) + "</color></size>", 22, CourseUI.Ink);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            CourseUI.Size(label, 56, -1, 1);
            Button choose = CourseUI.Button("Choose", row, selected ? "Chosen" : "Choose", selected ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Primary, () => { chosen = captured; Refresh(); }, 20);
            CourseUI.Size(choose, 48, 150);
        }
        if (shown == 0)
        {
            TextMeshProUGUI empty = CourseUI.Text("Empty", courseList, "No courses match the search.", 22, CourseUI.InkMuted, FontStyles.Italic);
            CourseUI.Size(empty, 40);
        }

        string problem = Problem();
        chosenText.text = chosen == null ? "Pick a course on the left."
            : "<b>" + chosen.name + "</b>\n<size=85%>" + CourseFilter.Facts(chosen) + "</size>" + (problem != null ? "\n<size=80%><color=#C0392B>" + problem + "</color></size>" : "");
        startButton.interactable = chosen != null && problem == null && !starting;
    }

    // Why the chosen course can't be run in the chosen mode, or null
    string Problem()
    {
        if (chosen == null) return null;
        if (chosen.IsRelay() && GameModes.IsYourRunners(mode))
        {
            int owned = GameModes.RaceRunnerCount();
            if (owned < chosen.legs)
            {
                return "This relay has " + chosen.legs + " legs, but you only have " + owned + (owned == 1 ? " runner" : " runners") + ". Pick a relay with fewer legs or another game mode.";
            }
        }
        return null;
    }

    // ---- Starting and ending a game ----

    async void StartGame()
    {
        if (starting || chosen == null || Problem() != null) return;
        starting = true;
        Refresh();
        CourseLibrary.Current = chosen;
        ChosenGameMode = mode;

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
            if (this != null)
            {
                UIToast.Show("The game could not start: " + result.ShutdownReason);
                Refresh();
            }
        }
    }

    // Leaving a single-player game: close it and come back to this page
    public static void EndGame(NetworkRunner runner)
    {
        ChosenGameMode = null;
        SceneLoader.LeaveSession(runner, "SingelPlayer");
    }
}
