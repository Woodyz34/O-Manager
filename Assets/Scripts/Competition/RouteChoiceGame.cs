using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Route choice game, on sprint and forest courses. Nobody picks a runner and nobody runs. Leg by leg, the game shows
// 2 to 4 clearly different routes in red, blue, green and yellow (RouteChoiceGenerator). You pick the one you think
// is fastest for a runner with speed 100 (ground, slope and turns all count, so the shortest is not always the
// fastest). Then the times are shown: the fastest route and how many seconds slower the others are.
// A right answer gives 500 to 1000 points (more the quicker you answer), a wrong one 0. Played on its own.
//
// Every player works out the routes on their own computer from the same map, so everyone gets the same routes.
// Each player goes at their own pace; the points are shared on NetworkPlayer (choice*) for the results at the end.
public static class RouteChoiceGame
{
    public const string RunnerName = "Speed 100 runner";
    public const float AnswerSeconds = 30f;
    public const int MaxPoints = 1000;

    public static readonly Color[] Colours =
    {
        new Color(0.86f, 0.15f, 0.15f), new Color(0.13f, 0.40f, 0.90f), new Color(0.12f, 0.62f, 0.25f), new Color(0.98f, 0.80f, 0.05f)
    };
    public static readonly string[] ColourNames = { "Red", "Blue", "Green", "Yellow" };

    public static bool On => ExtraModes.Is(ExtraMode.RouteChoice) && RelayRace.Active == null;

    // Instead of the choose-a-runner screen: everyone is ready at once, with the same speed-100 runner
    public static void Begin(NetworkPlayer local)
    {
        AgentMovement agent = local.agentMovement;
        agent.SetRunner(new CardData(RunnerName, 100f, 100f, null, null, "", true, null));
        local.relayRunnerName = RunnerName;
        if (local.manager != null && local.manager.ChooseRunnerPanel != null)
        {
            local.manager.ChooseRunnerPanel.SetActive(false);
        }
        local.choiceScore = 0;
        local.choiceRight = 0;
        local.choiceAnswered = 0;
        local.choiceQuestions = 0;
        local.choiceDone = false;
        local.SetHasChoosenRunner();
        GameObject board = GameObject.Find("LeaderboardPanel");
        if (board != null) board.SetActive(false);
        UIToast.Show("Route choice game: pick the route you think is fastest on each leg. "
            + "Right answers give points, more the quicker you are.", 8f);
    }

    // Everyone is ready: start the questions
    public static void Start(AgentMovement agent)
    {
        agent.manualPathDrawer.timeToDraw = false;
        agent.canRunNextSegment = false;
        agent.WaitingForAllPlayersToDraw = true; // stops the clock
        if (GameModeRuntime.Host.GetComponent<RouteChoiceView>() == null)
        {
            GameModeRuntime.Host.gameObject.AddComponent<RouteChoiceView>();
        }
    }

    // 500 points for a right answer, plus up to 500 more the quicker it came
    public static int Points(bool right, float secondsLeft)
    {
        if (!right) return 0;
        float share = Mathf.Clamp01(secondsLeft / AnswerSeconds);
        return Mathf.RoundToInt(MaxPoints * (0.5f + 0.5f * share));
    }

    public static string Points(int points) => points.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
}

// The questions, the answers and the results, on the map with a panel to the left
public class RouteChoiceView : MonoBehaviour
{
    enum Step { Loading, Asking, Answered, Done }

    PathfindingManager manager;
    Camera cam;
    RouteChoiceLeg[] results;
    int legs;
    int leg;           // the leg being shown (1 = start to control 1)
    int question;      // questions shown so far
    int skipped;
    Step step;
    float askedAt;
    int picked = -1;
    int score;
    int right;
    bool failed;

    GameObject panel;
    TextMeshProUGUI heading;
    TextMeshProUGUI info;
    RectTransform body;
    RectTransform timerFill;
    TextMeshProUGUI timerText;
    TextMeshProUGUI scoreText;
    Button nextButton;
    readonly List<Button> choiceButtons = new List<Button>();
    readonly List<GameObject> drawn = new List<GameObject>();
    readonly List<LineRenderer> lines = new List<LineRenderer>();
    float nextRefresh;
    static Material material;

    void Start()
    {
        manager = FindObjectOfType<PathfindingManager>();
        NetworkPlayer local = NetworkPlayer.Local;
        AgentMovement agent = local != null ? local.agentMovement : null;
        CameraController controller = local != null ? local.GetComponent<CameraController>() : null;
        cam = controller != null && controller.cam != null ? controller.cam : Camera.main;
        if (controller != null) controller.enabled = false;
        if (material == null) material = new Material(Shader.Find("Sprites/Default"));
        RouteCompareView.HideRunners(true);
        Build();

        if (manager == null || manager.speedGrid2D == null || agent == null || manager.controlPoints.Count < 2)
        {
            Fail("The map is not loaded, so there are no route choices to show.");
            return;
        }

        // Work out every leg in the background, in order, so the first question comes as soon as it is ready
        legs = manager.controlPoints.Count - 1;
        results = new RouteChoiceLeg[legs + 1];
        Vector2Int[] cells = new Vector2Int[legs + 1];
        for (int k = 0; k <= legs; k++) cells[k] = manager.WorldToGridPositionCentered(manager.controlPoints[k].position);
        RouteChoiceGenerator generator = new RouteChoiceGenerator(manager.speedGrid2D, manager.slopeFactors, manager.cellSize,
            agent.originalBaseSpeed, PackRunning.GameSecondsPerSecond);
        RouteChoiceLeg[] target = results;
        Task.Run(() =>
        {
            for (int k = 1; k < target.Length; k++)
            {
                RouteChoiceLeg made;
                try
                {
                    int seed = k * 7919 + cells[k - 1].x * 104729 + cells[k - 1].y * 1299709 + cells[k].x * 31 + cells[k].y;
                    made = generator.Make(k, cells[k - 1], cells[k], seed);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    made = new RouteChoiceLeg { leg = k, skipped = "could not be worked out" };
                }
                Volatile.Write(ref target[k], made);
            }
        });
        leg = 0;
        NextLeg();
    }

    // ---- The panel ----

    void Build()
    {
        GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
        if (canvas == null) return;
        Image card = CourseUI.CardPanel("Route choice game", canvas.transform);
        panel = card.gameObject;
        CourseUI.Place(card.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24, 0), new Vector2(560, 940));

        heading = CourseUI.Text("Heading", card.transform, "Route choice game", 34, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -20), new Vector2(504, 90));
        scoreText = CourseUI.Text("Score", card.transform, "", 22, CourseUI.Forest, FontStyles.Bold, TextAlignmentOptions.TopRight);
        CourseUI.Place(scoreText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28, -26), new Vector2(220, 36));
        info = CourseUI.Text("Info", card.transform, "", 22, CourseUI.InkMuted);
        CourseUI.Place(info.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -112), new Vector2(504, 96));

        body = CourseUI.Rect("Body", card.transform);
        CourseUI.Place(body, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -214), new Vector2(504, 560));
        CourseUI.Column(body.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandHeight = false;

        // The thinking time left
        Image track = CourseUI.Panel("Timer", card.transform, new Color(0.12f, 0.24f, 0.17f, 0.15f));
        CourseUI.Place(track.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28, 100), new Vector2(390, 18));
        Image fill = CourseUI.Panel("Fill", track.transform, CourseUI.Accent);
        timerFill = fill.rectTransform;
        timerFill.anchorMin = Vector2.zero;
        timerFill.anchorMax = Vector2.one;
        timerFill.offsetMin = timerFill.offsetMax = Vector2.zero;
        timerText = CourseUI.Text("Seconds", card.transform, "", 22, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(timerText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28, 92), new Vector2(100, 34));

        nextButton = CourseUI.Button("Next", card.transform, "Next leg", CourseUI.ButtonKind.Primary, OnNext, 24);
        CourseUI.Place((RectTransform)nextButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28, 22), new Vector2(504, 60));
        ShowTimer(false);
        nextButton.gameObject.SetActive(false);
        UpdateScore();
    }

    void ShowTimer(bool show)
    {
        if (timerFill != null) timerFill.parent.gameObject.SetActive(show);
        if (timerText != null) timerText.gameObject.SetActive(show);
    }

    void UpdateScore()
    {
        if (scoreText != null) scoreText.text = RouteChoiceGame.Points(score) + " points";
    }

    void ClearBody()
    {
        choiceButtons.Clear();
        if (body == null) return;
        foreach (Transform child in body) Destroy(child.gameObject);
    }

    void Fail(string message)
    {
        failed = true;
        step = Step.Done;
        ClearBody();
        if (heading != null) heading.text = "Route choice game";
        if (info != null) info.text = message;
        ShowBackButton();
    }

    // ---- Going through the legs ----

    void NextLeg()
    {
        ClearDrawn();
        leg++;
        picked = -1;
        if (leg > legs)
        {
            Finish();
            return;
        }
        step = Step.Loading;
        ShowTimer(false);
        nextButton.gameObject.SetActive(false);
        ClearBody();
        heading.text = "Leg " + leg + " of " + legs;
        info.text = "Finding the route choices...";
        FitCamera(null);
    }

    void Update()
    {
        if (failed) return;
        switch (step)
        {
            case Step.Loading:
                RouteChoiceLeg ready = results != null && leg >= 1 && leg <= legs ? Volatile.Read(ref results[leg]) : null;
                if (ready == null) return;
                if (ready.skipped != null || ready.choices.Count < 2)
                {
                    skipped++;
                    Debug.Log("Route choice game: leg " + leg + " skipped (" + ready.skipped + ")");
                    NextLeg();
                    return;
                }
                Ask(ready);
                break;

            case Step.Asking:
                float left = RouteChoiceGame.AnswerSeconds - (Time.unscaledTime - askedAt);
                if (timerFill != null) timerFill.anchorMax = new Vector2(Mathf.Clamp01(left / RouteChoiceGame.AnswerSeconds), 1f);
                if (timerText != null) timerText.text = Mathf.CeilToInt(Mathf.Max(0f, left)) + " s";
                for (int i = 0; i < choiceButtons.Count && i < 4; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) Answer(i);
                }
                if (step == Step.Asking && left <= 0f) Answer(-1);
                break;

            case Step.Answered:
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.RightArrow)) OnNext();
                break;

            case Step.Done:
                if (Time.unscaledTime > nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 0.5f;
                    FillStandings();
                }
                break;
        }
    }

    void Ask(RouteChoiceLeg current)
    {
        question++;
        step = Step.Asking;
        askedAt = Time.unscaledTime;
        string fromName = leg - 1 == 0 ? "Start" : "Control " + (leg - 1);
        string toName = leg == legs ? "finish" : "control " + leg;
        float straight = Vector2.Distance(manager.controlPoints[leg - 1].position, manager.controlPoints[leg].position) / MapDefinition.WorldUnitsPerMeter;
        heading.text = "Leg " + leg + " of " + legs;
        info.text = fromName + " to " + toName + ", " + Mathf.RoundToInt(straight) + " m straight.\n<color=#1F3D2B><b>Which route is fastest?</b></color>";

        FitCamera(current);
        DrawRoutes(current, -1);

        ClearBody();
        for (int i = 0; i < current.choices.Count; i++)
        {
            int index = i;
            RouteChoice choice = current.choices[i];
            Button button = CourseUI.Button("Choice " + i, body, "", CourseUI.ButtonKind.Light, () => Answer(index), 24);
            CourseUI.Size(button, 72);
            Image swatch = CourseUI.Panel("Colour", button.transform, RouteChoiceGame.Colours[choice.colour]);
            CourseUI.Place(swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18, 0), new Vector2(54, 16));
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.text = "<b>" + RouteChoiceGame.ColourNames[choice.colour] + "</b>  <size=75%><color=#5B7563>" + Mathf.RoundToInt(choice.meters) + " m   (key " + (i + 1) + ")</color></size>";
            label.alignment = TextAlignmentOptions.MidlineLeft;
            CourseUI.Stretch(label.rectTransform, 90, 12, 0, 0);
            choiceButtons.Add(button);
        }
        TextMeshProUGUI note = CourseUI.Text("Note", body, "A runner with speed 100 runs every route. Ground, slope and turns all count, so the shortest route is not always the fastest.", 18, CourseUI.InkMuted);
        CourseUI.Size(note, 70);
        ShowTimer(true);
    }

    void Answer(int choice)
    {
        if (step != Step.Asking) return;
        RouteChoiceLeg current = results[leg];
        float left = Mathf.Max(0f, RouteChoiceGame.AnswerSeconds - (Time.unscaledTime - askedAt));
        int fastest = current.Fastest;
        bool correct = choice == fastest;
        int points = RouteChoiceGame.Points(correct, left);
        picked = choice;
        score += points;
        if (correct) right++;
        step = Step.Answered;
        ShowTimer(false);

        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.Object != null && local.Object.IsValid)
        {
            local.choiceScore = score;
            local.choiceRight = right;
            local.choiceAnswered = question;
        }
        UpdateScore();
        DrawRoutes(current, fastest);

        // The routes, fastest first, and how much slower the others are
        RouteChoice best = current.choices[fastest];
        string verdict;
        if (choice < 0) verdict = "<color=#C0392B><b>Time is up.</b></color> " + RouteChoiceGame.ColourNames[best.colour] + " was fastest.";
        else if (correct) verdict = "<color=#3F7D54><b>Right! +" + RouteChoiceGame.Points(points) + " points</b></color>";
        else
        {
            RouteChoice mine = current.choices[choice];
            verdict = "<color=#C0392B><b>Not quite.</b></color> " + RouteChoiceGame.ColourNames[best.colour] + " was "
                + Mathf.RoundToInt(mine.seconds - best.seconds) + " s faster than " + RouteChoiceGame.ColourNames[mine.colour].ToLowerInvariant() + ".";
        }
        info.text = verdict;

        ClearBody();
        List<int> order = new List<int>();
        for (int i = 0; i < current.choices.Count; i++) order.Add(i);
        order.Sort((a, b) => current.choices[a].seconds.CompareTo(current.choices[b].seconds));
        int bestSeconds = Mathf.RoundToInt(best.seconds);
        foreach (int i in order)
        {
            RouteChoice c = current.choices[i];
            int seconds = Mathf.RoundToInt(c.seconds);
            string time = i == fastest ? RelayStats.Time(seconds) : "<color=#C0392B>+" + (seconds - bestSeconds) + " s</color>";
            string name = "<b>" + RouteChoiceGame.ColourNames[c.colour] + "</b>" + (i == choice ? "  <size=80%>(your pick)</size>" : "")
                + "\n<size=75%><color=#5B7563>" + Mathf.RoundToInt(c.meters) + " m" + (i == fastest ? ", fastest" : ", " + RelayStats.Time(seconds)) + "</color></size>";
            AddRow(RouteChoiceGame.Colours[c.colour], name, time, i == choice);
        }

        bool last = true;
        for (int k = leg + 1; k <= legs; k++)
        {
            RouteChoiceLeg later = Volatile.Read(ref results[k]);
            if (later == null || (later.skipped == null && later.choices.Count >= 2)) { last = false; break; }
        }
        CourseUI.SetButtonText(nextButton, last ? "See the results" : "Next leg");
        nextButton.gameObject.SetActive(true);
    }

    void OnNext()
    {
        if (step == Step.Answered) NextLeg();
    }

    void AddRow(Color color, string name, string time, bool mine)
    {
        Image row = CourseUI.Panel("Row", body, mine ? new Color(0.91f, 0.45f, 0.16f, 0.16f) : new Color(1f, 1f, 1f, 0.5f));
        CourseUI.Size(row, 70);
        Image swatch = CourseUI.Panel("Colour", row.transform, color);
        CourseUI.Place(swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(44, 14));
        TextMeshProUGUI label = CourseUI.Text("Name", row.transform, name, 22, CourseUI.Ink);
        CourseUI.Stretch(label.rectTransform, 72, 150, 4, 4);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        TextMeshProUGUI value = CourseUI.Text("Time", row.transform, time, 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14, 0), new Vector2(140, 60));
        value.enableWordWrapping = false;
    }

    // ---- The end ----

    void Finish()
    {
        step = Step.Done;
        ShowTimer(false);
        nextButton.gameObject.SetActive(false);
        ClearDrawn();
        FitWholeCourse();
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.Object != null && local.Object.IsValid)
        {
            local.choiceScore = score;
            local.choiceRight = right;
            local.choiceAnswered = question;
            local.choiceQuestions = question;
            local.choiceDone = true;
        }
        heading.text = "Results";
        if (question == 0)
        {
            info.text = "This course has no legs with a clear route choice. Try another course or a longer one.";
        }
        else
        {
            info.text = "You got <b>" + right + " of " + question + "</b> right: <b>" + RouteChoiceGame.Points(score) + " points</b>."
                + (skipped > 0 ? "\n<size=85%>" + skipped + (skipped == 1 ? " leg had" : " legs had") + " no clear route choice and was left out.</size>" : "");
        }
        FillStandings();
        ShowBackButton();
    }

    void FillStandings()
    {
        if (failed || body == null) return;
        ClearBody();
        List<NetworkPlayer> players = new List<NetworkPlayer>();
        foreach (NetworkPlayer p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (p.Object != null && p.Object.IsValid) players.Add(p);
        }
        players.Sort((a, b) => b.choiceScore.CompareTo(a.choiceScore));
        int place = 0;
        foreach (NetworkPlayer p in players)
        {
            place++;
            bool me = p.Object.HasStateAuthority;
            string name = place + ". <b>" + BearMode.Name(p) + "</b>" + (me ? " (you)" : "")
                + "\n<size=75%><color=#5B7563>" + (p.choiceDone ? p.choiceRight + " of " + p.choiceQuestions + " right" : "Still playing, " + p.choiceAnswered + " answered") + "</color></size>";
            AddRow(RouteEntry.ColorOf(p, place - 1), name, RouteChoiceGame.Points(p.choiceScore), me);
        }
    }

    void ShowBackButton()
    {
        if (nextButton == null) return;
        bool host = CompetitionFlow.LocalIsHost();
        nextButton.onClick.RemoveAllListeners();
        nextButton.onClick.AddListener(CompetitionFlow.HostReturnToLobby);
        CourseUI.SetButtonText(nextButton, host ? "Back to the lobby" : "The host takes everyone back to the lobby");
        nextButton.interactable = host;
        nextButton.gameObject.SetActive(true);
    }

    // ---- The map ----

    void DrawRoutes(RouteChoiceLeg current, int fastest)
    {
        ClearDrawn();
        float width = cam.orthographicSize * 0.012f;
        for (int i = 0; i < current.choices.Count; i++)
        {
            RouteChoice c = current.choices[i];
            List<Vector3> points = WorldPoints(c.points);
            bool highlight = fastest < 0 || i == fastest;
            float w = fastest >= 0 && i == fastest ? width * 1.5f : width;
            Color colour = RouteChoiceGame.Colours[c.colour];
            if (!highlight) colour.a = 0.55f;
            // A dark edge under each line so it shows on every kind of ground
            Line("Route edge " + i, points, new Color(0.08f, 0.08f, 0.08f, highlight ? 0.75f : 0.35f), w * 1.6f, 40 + 2 * i);
            Line("Route " + i, points, colour, w, 41 + 2 * i + (fastest >= 0 && i == fastest ? 20 : 0));
        }
    }

    // The smooth line (grid coordinates, cell centres on whole numbers) on the map, from control to control
    List<Vector3> WorldPoints(List<Vector2> line)
    {
        List<Vector3> points = new List<Vector3>(line.Count + 2);
        points.Add(manager.controlPoints[leg - 1].position);
        foreach (Vector2 p in line) points.Add(GridToWorld(p));
        points.Add(manager.controlPoints[leg].position);
        return points;
    }

    Vector3 GridToWorld(Vector2 p)
    {
        Vector3 origin = manager.GridToWorldPosition(0, 0);
        return new Vector3(origin.x + p.x * manager.cellSize, origin.y + p.y * manager.cellSize, origin.z);
    }

    // Both controls and every route on the screen, to the right of the panel, with half a control circle to spare
    void FitCamera(RouteChoiceLeg current)
    {
        if (manager == null || cam == null || leg < 1 || leg > legs) return;
        Bounds bounds = new Bounds(manager.controlPoints[leg - 1].position, Vector3.zero);
        bounds.Encapsulate(manager.controlPoints[leg].position);
        if (current != null)
        {
            foreach (RouteChoice c in current.choices)
            {
                foreach (Vector2 p in c.points) bounds.Encapsulate(GridToWorld(p));
            }
        }
        Fit(bounds, 60f * MapDefinition.WorldUnitsPerMeter);
    }

    void FitWholeCourse()
    {
        if (manager == null || cam == null || manager.controlPoints.Count == 0) return;
        Bounds bounds = new Bounds(manager.controlPoints[0].position, Vector3.zero);
        foreach (Transform t in manager.controlPoints) bounds.Encapsulate(t.position);
        Fit(bounds, 80f * MapDefinition.WorldUnitsPerMeter);
    }

    void Fit(Bounds bounds, float margin)
    {
        // The panel covers the left of the screen (about a third), so the map is shown in the rest
        float size = Mathf.Max(bounds.extents.y, bounds.extents.x / Mathf.Max(0.1f, cam.aspect * 0.62f)) * 1.08f + margin;
        cam.transform.rotation = Quaternion.identity;
        cam.orthographicSize = size;
        float shift = size * cam.aspect * 0.32f;
        cam.transform.position = new Vector3(bounds.center.x - shift, bounds.center.y, -10f);
    }

    void Line(string name, List<Vector3> points, Color color, float width, int order)
    {
        LineRenderer line = new GameObject(name).AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.startColor = line.endColor = color;
        line.numCapVertices = 3;
        line.numCornerVertices = 3;
        line.sortingOrder = order;
        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++) line.SetPosition(i, new Vector3(points[i].x, points[i].y, -1.3f));
        drawn.Add(line.gameObject);
        lines.Add(line);
    }

    void ClearDrawn()
    {
        foreach (GameObject go in drawn) if (go != null) Destroy(go);
        drawn.Clear();
        lines.Clear();
    }

    void OnDestroy()
    {
        RouteCompareView.HideRunners(false);
        ClearDrawn();
        if (panel != null) Destroy(panel);
    }
}
