using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

    // Each colour always sits in the same place, left to right blue, green, yellow, red, and has its own key
    // (1 to 4 at first, changeable with the Keys button; saved per computer)
    public static readonly int[] LeftToRight = { 1, 2, 3, 0 };
    static readonly KeyCode[] DefaultKeys = { KeyCode.Alpha4, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 };

    public static KeyCode Key(int colour)
    {
        string saved = PlayerPrefs.GetString("RouteChoice.Key." + ColourNames[colour], "");
        if (saved.Length > 0 && System.Enum.TryParse(saved, out KeyCode key)) return key;
        return DefaultKeys[colour];
    }

    public static void SetKey(int colour, KeyCode key)
    {
        // A key can only answer one colour: the colour that had it gets this colour's old key
        KeyCode old = Key(colour);
        for (int c = 0; c < ColourNames.Length; c++)
        {
            if (c != colour && Key(c) == key) PlayerPrefs.SetString("RouteChoice.Key." + ColourNames[c], old.ToString());
        }
        PlayerPrefs.SetString("RouteChoice.Key." + ColourNames[colour], key.ToString());
        PlayerPrefs.Save();
    }

    // True when the colour's key was pressed this frame (the number keys also work on the number pad)
    public static bool Pressed(int colour)
    {
        KeyCode key = Key(colour);
        if (Input.GetKeyDown(key)) return true;
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return Input.GetKeyDown(KeyCode.Keypad0 + (key - KeyCode.Alpha0));
        return false;
    }

    public static string KeyName(KeyCode key)
    {
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
        if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num " + (int)(key - KeyCode.Keypad0);
        switch (key)
        {
            case KeyCode.LeftArrow: return "Left";
            case KeyCode.RightArrow: return "Right";
            case KeyCode.UpArrow: return "Up";
            case KeyCode.DownArrow: return "Down";
        }
        return key.ToString();
    }

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
        for (int i = 0; i < local.choiceLegs.Length; i++) local.choiceLegs.Set(i, 0);
        local.SetHasChoosenRunner();
        GameObject board = GameObject.Find("LeaderboardPanel");
        if (board != null) board.SetActive(false);
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

    // ---- Every player's answer on every leg, shared on NetworkPlayer.choiceLegs ----
    // 0 = not answered yet; otherwise (route picked + 2, so -1 = time ran out) + 8 x points

    public static void WriteLeg(NetworkPlayer player, int leg, int pick, int points)
    {
        if (player == null || player.Object == null || !player.Object.IsValid || !player.Object.HasStateAuthority) return;
        if (leg < 0 || leg >= player.choiceLegs.Length) return;
        player.choiceLegs.Set(leg, (pick + 2) + 8 * Mathf.Max(0, points));
    }

    // pick -2 = not answered (yet), -1 = time ran out, else the index of the route
    public static void ReadLeg(NetworkPlayer player, int leg, out int pick, out int points)
    {
        pick = -2;
        points = 0;
        if (player == null || player.Object == null || !player.Object.IsValid || leg < 0 || leg >= player.choiceLegs.Length) return;
        int value = player.choiceLegs[leg];
        if (value <= 0) return;
        pick = (value & 7) - 2;
        points = value >> 3;
    }

    // ---- Prize money ----
    // Everyone who takes part gets a little, and a good place among the others gives more (as in a race)
    public const int TookPart = CompetitionRewards.FinishBase;

    public static int ForPlace(int place, int players)
    {
        int reward = CompetitionRewards.PerRunnerBeaten * Mathf.Max(0, players - place);
        if (place == 1 && players >= 2) reward += CompetitionRewards.WinnerBonus;
        return reward;
    }
}

// The questions, the answers and the results, on the map with a panel to the left
public class RouteChoiceView : MonoBehaviour
{
    enum Step { Loading, Asking, Answered, Done }
    enum Page { Legs, Money }

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
    float answeredAt;
    int[] picks;       // per leg: the route picked, -1 time ran out, -2 no question
    Page page;
    readonly List<Button> tabButtons = new List<Button>();
    Button endButton;  // the host sends everyone back to the lobby, at any time
    Button keysButton;
    float endConfirmUntil;
    bool paid;
    int lastPlace;     // the place among the others the last time the standings were looked at (0 = unknown)
    int lastPlayers;

    GameObject panel;
    TextMeshProUGUI heading;
    TextMeshProUGUI info;
    RectTransform body;
    RectTransform timerFill;
    TextMeshProUGUI timerText;
    TextMeshProUGUI scoreText;
    Button nextButton;
    Button previousLegButton, nextLegButton;
    readonly List<Button> choiceButtons = new List<Button>();
    GameObject keysPanel;
    readonly List<Button> keyButtons = new List<Button>();
    int rebinding = -1; // the colour waiting for a new key, or -1
    // The Esc menu leaves Escape alone while the keys are being changed (and on the frame the panel closes)
    public static bool KeysOpen;
    public static int KeysClosedFrame = -1;
    readonly List<GameObject> hiddenHud = new List<GameObject>();
    bool fitted;
    float fittedSize;
    Vector3 fittedPosition;
    Quaternion fittedRotation = Quaternion.identity;
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
        picks = new int[legs + 1];
        for (int k = 0; k <= legs; k++) picks[k] = -2;
        Vector2Int[] cells = new Vector2Int[legs + 1];
        for (int k = 0; k <= legs; k++) cells[k] = manager.WorldToGridPositionCentered(manager.controlPoints[k].position);
        RouteChoiceGenerator generator = new RouteChoiceGenerator(manager.speedGrid2D, manager.slopeFactors, manager.cellSize,
            agent.originalBaseSpeed, PackRunning.GameSecondsPerSecond,
            CourseLoader.LoadedMap != null ? CourseLoader.LoadedMap.IsSprint : manager.cellSize / MapDefinition.WorldUnitsPerMeter < 5f);
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
        CourseUI.Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -20), new Vector2(226, 90));
        heading.enableAutoSizing = true;
        heading.fontSizeMin = 22;
        heading.fontSizeMax = 34;
        heading.enableWordWrapping = false;
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

        keysButton = CourseUI.Button("Keys", card.transform, "Keys", CourseUI.ButtonKind.Light, ToggleKeys, 20);
        CourseUI.Place((RectTransform)keysButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28, -66), new Vector2(110, 40));

        // Like "End competition for everyone" in a race: when someone has crashed or takes too long
        endButton = CourseUI.Button("End for all", card.transform, "End for all", CourseUI.ButtonKind.Danger, OnEndForAll, 18);
        CourseUI.Place((RectTransform)endButton.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-148, -66), new Vector2(176, 40));
        // "Sure? Click again" is longer than "End for all": shrink the text to fit rather than cut it off
        TextMeshProUGUI endLabel = endButton.GetComponentInChildren<TextMeshProUGUI>();
        if (endLabel != null)
        {
            endLabel.enableWordWrapping = false;
            endLabel.enableAutoSizing = true;
            endLabel.fontSizeMin = 12;
            endLabel.fontSizeMax = 18;
        }
        endButton.gameObject.SetActive(CompetitionFlow.LocalIsHost());

        nextButton = CourseUI.Button("Next", card.transform, "Next leg", CourseUI.ButtonKind.Primary, OnNext, 24);
        CourseUI.Place((RectTransform)nextButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28, 22), new Vector2(504, 60));
        // Looking through the legs after the game: the leg before and after, without going back to the table
        previousLegButton = CourseUI.Button("PreviousLeg", card.transform, "<  Previous leg", CourseUI.ButtonKind.Light, () => StepLeg(-1), 20);
        CourseUI.Place((RectTransform)previousLegButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28, 92), new Vector2(246, 52));
        nextLegButton = CourseUI.Button("NextLeg", card.transform, "Next leg  >", CourseUI.ButtonKind.Light, () => StepLeg(1), 20);
        CourseUI.Place((RectTransform)nextLegButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(286, 92), new Vector2(246, 52));
        previousLegButton.gameObject.SetActive(false);
        nextLegButton.gameObject.SetActive(false);
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
        if (endButton != null) endButton.gameObject.SetActive(false);
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

    void LateUpdate()
    {
        KeepFit();
    }

    void Update()
    {
        if (failed) return;
        if (rebinding >= 0) ListenForKey();
        else if (keysPanel != null && Input.GetKeyDown(KeyCode.Escape)) ToggleKeys();
        bool keysOpen = keysPanel != null; // while the keys are being changed, key presses do not answer
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
                List<RouteChoice> asked = results[leg].choices;
                for (int i = 0; i < asked.Count && step == Step.Asking && !keysOpen; i++)
                {
                    if (RouteChoiceGame.Pressed(asked[i].colour)) Answer(i);
                }
                if (step == Step.Asking && left <= 0f) Answer(-1);
                break;

            case Step.Answered:
                // Any key goes on (the answer key again too), so the game can be played fast. A short pause first,
                // so a key held a moment too long does not skip the answer.
                if (!keysOpen && Time.unscaledTime - answeredAt > 0.25f && AnyKeyForNext()) OnNext();
                break;

            case Step.Done:
                if (legShown > 0)
                {
                    // Looking at a leg on the map: the arrow keys go to the leg before or after, Enter or
                    // Backspace back to the table (other keys do nothing, so a stray key does not throw you out)
                    if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow)) StepLeg(-1);
                    else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow)) StepLeg(1);
                    else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Backspace)) ShowOverlay(Page.Legs);
                    break;
                }
                if (Time.unscaledTime > nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 0.5f;
                    if (overlay == null || !overlay.activeSelf) ReadStandings();
                    else if (page == Page.Legs) FillTable();
                    else if (!paid) FillMoney();
                    else ReadStandings();
                }
                break;
        }
        RefreshEndButton();
    }

    // A key on the keyboard, not a mouse button (clicks are for the buttons) and not Escape (the menu)
    static bool AnyKeyForNext()
    {
        if (!Input.anyKeyDown) return false;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) return false;
        return !Input.GetKeyDown(KeyCode.Escape);
    }

    // ---- The host ends the game for everyone ----

    void OnEndForAll()
    {
        if (!CompetitionFlow.LocalIsHost()) return;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        // A big step, so the button asks once more
        if (Time.unscaledTime >= endConfirmUntil)
        {
            endConfirmUntil = Time.unscaledTime + 5f;
            RefreshEndButton();
            return;
        }
        CompetitionFlow.HostReturnToLobby();
    }

    void RefreshEndButton()
    {
        if (endButton == null) return;
        bool show = step != Step.Done && !failed && CompetitionFlow.LocalIsHost();
        if (endButton.gameObject.activeSelf != show) endButton.gameObject.SetActive(show);
        if (show) CourseUI.SetButtonText(endButton, Time.unscaledTime < endConfirmUntil ? "Sure? Click again" : "End for all");
    }

    void Ask(RouteChoiceLeg current)
    {
        question++;
        step = Step.Asking;
        askedAt = Time.unscaledTime;
        string fromName = leg - 1 == 0 ? "Start" : "Control " + (leg - 1);
        string toName = leg == legs ? "finish" : "control " + leg;
        heading.text = "Leg " + leg + " of " + legs;
        // No lengths while guessing: only the routes on the map
        info.text = fromName + " to " + toName + ".\n<color=#1F3D2B><b>Which route is fastest?</b></color>";

        FitCamera(current);
        DrawRoutes(current, -1);
        HideHud();

        ClearBody();
        // One coloured box per route, in the colours' fixed order from left to right; click it or press its key
        RectTransform row = CourseUI.Rect("Choices", body);
        CourseUI.Row(row.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(row, 150);
        foreach (int colour in RouteChoiceGame.LeftToRight)
        {
            int index = current.choices.FindIndex(c => c.colour == colour);
            if (index < 0) continue;
            Button button = CourseUI.Button("Choice " + RouteChoiceGame.ColourNames[colour], row, "", CourseUI.ButtonKind.Light, () => Answer(index), 24);
            button.GetComponent<Image>().color = RouteChoiceGame.Colours[colour];
            CourseUI.Size(button, 150, -1, 1);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.color = colour == 3 ? CourseUI.Ink : Color.white; // dark text on yellow
            label.text = ChoiceLabel(colour);
            label.enableWordWrapping = false;
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
        picks[leg] = choice;
        RouteChoiceGame.WriteLeg(NetworkPlayer.Local, leg, choice, points);
        score += points;
        if (correct) right++;
        step = Step.Answered;
        answeredAt = Time.unscaledTime;
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
        CourseUI.SetButtonText(nextButton, last ? "See the results  <size=70%>(any key)</size>" : "Next leg  <size=70%>(any key)</size>");
        nextButton.gameObject.SetActive(true);
    }

    // ---- Changing the keys ----

    void ToggleKeys()
    {
        // A clicked button stays selected, and Enter or Space would press it again
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (keysPanel != null)
        {
            Destroy(keysPanel);
            keysPanel = null;
            rebinding = -1;
            KeysOpen = false;
            KeysClosedFrame = Time.frameCount;
            return;
        }
        KeysOpen = true;
        Image sheet = CourseUI.CardPanel("Keys", panel.transform);
        keysPanel = sheet.gameObject;
        Color solid = sheet.color;
        solid.a = 1f;
        sheet.color = solid;
        CourseUI.Place(sheet.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -116), new Vector2(504, 420));
        TextMeshProUGUI title = CourseUI.Text("Title", sheet.transform, "Keys for the routes", 26, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24, -18), new Vector2(456, 40));
        keyButtons.Clear();
        int k = 0;
        foreach (int colour in RouteChoiceGame.LeftToRight)
        {
            int c = colour;
            Image swatch = CourseUI.Panel("Colour", sheet.transform, RouteChoiceGame.Colours[c]);
            CourseUI.Place(swatch.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(24, -96 - k * 64), new Vector2(44, 20));
            TextMeshProUGUI name = CourseUI.Text("Name", sheet.transform, RouteChoiceGame.ColourNames[c], 22, CourseUI.Ink);
            CourseUI.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(84, -96 - k * 64), new Vector2(180, 40));
            name.alignment = TextAlignmentOptions.MidlineLeft;
            Button button = CourseUI.Button("Key " + c, sheet.transform, "", CourseUI.ButtonKind.Light, () => StartRebind(c), 22);
            CourseUI.Place((RectTransform)button.transform, new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-24, -96 - k * 64), new Vector2(200, 52));
            keyButtons.Add(button);
            k++;
        }
        Button done = CourseUI.Button("Done", sheet.transform, "Done", CourseUI.ButtonKind.Primary, ToggleKeys, 22);
        CourseUI.Place((RectTransform)done.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(456, 52));
        ShowKeys();
    }

    void ShowKeys()
    {
        for (int k = 0; k < keyButtons.Count; k++)
        {
            int c = RouteChoiceGame.LeftToRight[k];
            CourseUI.SetButtonText(keyButtons[k], rebinding == c ? "Press a key..." : RouteChoiceGame.KeyName(RouteChoiceGame.Key(c)));
        }
        RefreshChoiceKeys();
    }

    void StartRebind(int colour)
    {
        rebinding = colour;
        ShowKeys();
    }

    // Waiting for the new key: the first key pressed is it (Escape keeps the old one)
    void ListenForKey()
    {
        if (!Input.anyKeyDown) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            KeysClosedFrame = Time.frameCount;
            rebinding = -1;
            ShowKeys();
            return;
        }
        foreach (KeyCode key in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (key == KeyCode.None || key >= KeyCode.Mouse0) continue; // no mouse or joystick buttons
            if (!Input.GetKeyDown(key)) continue;
            RouteChoiceGame.SetKey(rebinding, key);
            rebinding = -1;
            ShowKeys();
            return;
        }
    }

    // The key shown on the route boxes, after a change
    void RefreshChoiceKeys()
    {
        if (step != Step.Asking || results == null || leg < 1 || leg > legs || results[leg] == null) return;
        foreach (Button button in choiceButtons)
        {
            foreach (RouteChoice choice in results[leg].choices)
            {
                if (button.name != "Choice " + RouteChoiceGame.ColourNames[choice.colour]) continue;
                button.GetComponentInChildren<TextMeshProUGUI>().text = ChoiceLabel(choice.colour);
            }
        }
    }

    // The key and the colour on a route box
    static string ChoiceLabel(int colour)
    {
        return "<size=150%><b>" + RouteChoiceGame.KeyName(RouteChoiceGame.Key(colour)) + "</b></size>\n<b>" + RouteChoiceGame.ColourNames[colour] + "</b>";
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
    // No big map: a table over the whole screen with everyone's points on every leg. Click a leg to see it on the
    // map again, turned as when it was asked, with your pick, the other routes and who took which.

    GameObject overlay;
    TextMeshProUGUI overlayHeading;
    TextMeshProUGUI overlayInfo;
    RectTransform overlayBody;
    ScrollRect overlayScroll;
    Button overlayLobby;
    TextMeshProUGUI panelHeading;
    TextMeshProUGUI panelInfo;
    RectTransform panelBody;
    string tableShown;       // what the table showed last, so it is only built again when something changed
    int legShown = -1;       // the leg looked at on the map, or -1 while the table is up

    void Finish()
    {
        step = Step.Done;
        ShowTimer(false);
        nextButton.gameObject.SetActive(false);
        if (keysButton != null) keysButton.gameObject.SetActive(false);
        RefreshEndButton();
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.Object != null && local.Object.IsValid)
        {
            local.choiceScore = score;
            local.choiceRight = right;
            local.choiceAnswered = question;
            local.choiceQuestions = question;
            local.choiceDone = true;
        }
        ClearDrawn();
        panelHeading = heading;
        panelInfo = info;
        panelBody = body;
        BuildOverlay();
        ShowOverlay(Page.Legs);
    }

    void BuildOverlay()
    {
        GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
        if (canvas == null) return;
        // Covers the map completely
        Image sheet = CourseUI.Panel("Route choice results", canvas.transform, CourseUI.CardSolid, false);
        CourseUI.Stretch(sheet.rectTransform);
        overlay = sheet.gameObject;

        overlayHeading = CourseUI.Text("Heading", sheet.transform, "Results", 44, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(overlayHeading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(70, -40), new Vector2(900, 60));
        overlayInfo = CourseUI.Text("Info", sheet.transform, "", 24, CourseUI.InkMuted);
        CourseUI.Place(overlayInfo.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(70, -104), new Vector2(1100, 70));

        RectTransform tabs = CourseUI.Rect("Tabs", sheet.transform);
        CourseUI.Place(tabs, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-70, -46), new Vector2(460, 54));
        CourseUI.Row(tabs.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        tabButtons.Clear();
        string[] names = { "Legs", "Prize money" };
        for (int i = 0; i < names.Length; i++)
        {
            Page target = (Page)i;
            Button tab = CourseUI.Button("Tab " + names[i], tabs, names[i], CourseUI.ButtonKind.Light, () => ShowOverlay(target), 22);
            CourseUI.Size(tab, 54, -1, 1);
            tabButtons.Add(tab);
        }

        overlayBody = CourseUI.ScrollList("Rows", sheet.transform, out overlayScroll);
        RectTransform list = (RectTransform)overlayScroll.transform;
        list.anchorMin = Vector2.zero;
        list.anchorMax = Vector2.one;
        list.offsetMin = new Vector2(70, 120);
        list.offsetMax = new Vector2(-90, -190);
        CourseUI.Column(overlayBody.gameObject, 6, new RectOffset(0, 0, 0, 0)).childForceExpandHeight = false;
        FastScroll.Add(overlayScroll);

        bool host = CompetitionFlow.LocalIsHost();
        overlayLobby = CourseUI.Button("Lobby", sheet.transform, host ? "Back to the lobby" : "The host takes everyone back to the lobby",
            CourseUI.ButtonKind.Primary, CompetitionFlow.HostReturnToLobby, 24);
        CourseUI.Place((RectTransform)overlayLobby.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-70, 36), new Vector2(560, 64));
        overlayLobby.interactable = host;
    }

    void ShowOverlay(Page show)
    {
        if (failed || overlay == null) return;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        legShown = -1;
        ClearDrawn();
        if (panel != null) panel.SetActive(false);
        overlay.SetActive(true);
        heading = overlayHeading;
        info = overlayInfo;
        body = overlayBody;
        page = show;
        tableShown = null;
        for (int i = 0; i < tabButtons.Count; i++) CourseUI.SetButtonColor(tabButtons[i], i == (int)show ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        if (show == Page.Legs) FillTable();
        else FillMoney();
    }

    // One row per leg with a question, one column per player (best first): the colour they picked and their points
    void FillTable()
    {
        if (failed || overlayBody == null) return;
        List<NetworkPlayer> players = ReadStandings();
        System.Text.StringBuilder seen = new System.Text.StringBuilder();
        foreach (NetworkPlayer p in players)
        {
            seen.Append(p.GetInstanceID()).Append(':').Append(p.choiceScore).Append(p.choiceDone ? 'd' : 'p');
            for (int k = 1; k <= legs && k < p.choiceLegs.Length; k++) seen.Append(',').Append(p.choiceLegs[k]);
            seen.Append(';');
        }
        string now = seen.ToString();
        if (now == tableShown) return;
        tableShown = now;

        heading.text = "Results";
        int waiting = 0;
        foreach (NetworkPlayer p in players) if (!p.choiceDone) waiting++;
        info.text = (question == 0 ? "This course has no legs with a clear route choice. Try another course or a longer one."
                : "You got <b>" + right + " of " + question + "</b> right: <b>" + RouteChoiceGame.Points(score) + " points</b>.")
            + (waiting > 0 ? "  " + waiting + (waiting == 1 ? " player is" : " players are") + " still playing." : "")
            + "\n<size=85%>Click a leg to see it on the map.</size>";

        float scrollAt = overlayScroll != null ? overlayScroll.verticalNormalizedPosition : 1f;
        ClearBody();
        float legWidth = 170f;

        // The players, best first, with their total
        RectTransform top = CourseUI.Rect("Players", overlayBody);
        CourseUI.Row(top.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(top, 74);
        TextMeshProUGUI corner = CourseUI.Text("Leg", top, "", 22, CourseUI.InkMuted);
        CourseUI.Size(corner, 74, legWidth);
        for (int i = 0; i < players.Count; i++)
        {
            NetworkPlayer p = players[i];
            bool me = p.Object.HasStateAuthority;
            Image cell = CourseUI.Panel("Player", top, me ? new Color(0.91f, 0.45f, 0.16f, 0.18f) : new Color(1f, 1f, 1f, 0.6f));
            CourseUI.Size(cell, 74, 0, 1);
            Image swatch = CourseUI.Panel("Colour", cell.transform, RouteEntry.ColorOf(p, i));
            CourseUI.Place(swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(10, 50));
            string status = p.choiceDone ? p.choiceRight + " of " + p.choiceQuestions + " right" : "playing, " + p.choiceAnswered + " answered";
            TextMeshProUGUI name = CourseUI.Text("Name", cell.transform, PlaceOf(p, players) + ". <b>" + BearMode.Name(p) + "</b>" + (me ? " (you)" : "")
                + "\n<size=80%><b>" + RouteChoiceGame.Points(p.choiceScore) + "</b> <color=#5B7563>· " + status + "</color></size>", 21, CourseUI.Ink);
            CourseUI.Stretch(name.rectTransform, 30, 8, 4, 4);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.enableWordWrapping = false;
        }

        // One row per leg
        for (int k = 1; k <= legs; k++)
        {
            if (picks[k] == -2) continue;
            int shown = k;
            RouteChoiceLeg r = results[k];
            Image rowImage = CourseUI.Panel("Leg " + k, overlayBody, new Color(1f, 1f, 1f, 0.55f));
            CourseUI.Size(rowImage, 52);
            Button row = rowImage.gameObject.AddComponent<Button>();
            row.targetGraphic = rowImage;
            ColorBlock colors = row.colors;
            colors.highlightedColor = new Color(0.93f, 0.9f, 0.8f);
            colors.pressedColor = new Color(0.85f, 0.82f, 0.7f);
            row.colors = colors;
            row.onClick.AddListener(() => ShowLeg(shown));
            CourseUI.Row(rowImage.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
            TextMeshProUGUI label = CourseUI.Text("Leg", rowImage.transform, "  Leg " + k + "  <size=80%><color=#5B7563>></color></size>", 22, CourseUI.Ink, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            CourseUI.Size(label, 52, legWidth);
            foreach (NetworkPlayer p in players)
            {
                RectTransform cell = CourseUI.Rect("Cell", rowImage.transform);
                CourseUI.Size(cell, 52, 0, 1);
                RouteChoiceGame.ReadLeg(p, k, out int pick, out int points);
                string text;
                if (pick == -2) text = "<color=#5B7563>-</color>";
                else if (pick < 0) text = "<color=#C0392B>time up</color>";
                else if (pick >= r.choices.Count) text = "?";
                else
                {
                    Image swatch = CourseUI.Panel("Picked", cell, RouteChoiceGame.Colours[r.choices[pick].colour]);
                    swatch.raycastTarget = false;
                    CourseUI.Place(swatch.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(34, 22));
                    text = points > 0 ? "<b>+" + RouteChoiceGame.Points(points) + "</b>" : "<color=#C0392B>0</color>";
                }
                TextMeshProUGUI value = CourseUI.Text("Points", cell, text, 22, CourseUI.Ink);
                CourseUI.Stretch(value.rectTransform, 58, 8, 0, 0);
                value.alignment = TextAlignmentOptions.MidlineLeft;
                value.raycastTarget = false;
            }
        }
        Canvas.ForceUpdateCanvases();
        if (overlayScroll != null) overlayScroll.verticalNormalizedPosition = scrollAt;
    }

    // One leg on the map again: all its routes, fastest first, with who picked each one
    void ShowLeg(int k)
    {
        if (results == null || k < 1 || k > legs || results[k] == null || picks[k] == -2) return;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        legShown = k;
        overlay.SetActive(false);
        if (panel != null) panel.SetActive(true);
        heading = panelHeading;
        info = panelInfo;
        body = panelBody;
        leg = k;
        RouteChoiceLeg current = results[k];
        int fastest = current.Fastest;
        FitCamera(current);
        DrawRoutes(current, fastest);

        string fromName = k - 1 == 0 ? "Start" : "Control " + (k - 1);
        string toName = k == legs ? "finish" : "control " + k;
        heading.text = "Leg " + k + " of " + legs;
        int mine = picks[k];
        info.text = fromName + " to " + toName + ". " + (mine < 0 ? "<color=#C0392B><b>Your time ran out.</b></color>"
            : mine == fastest ? "<color=#3F7D54><b>You picked the fastest.</b></color>"
            : "<color=#C0392B><b>You picked " + RouteChoiceGame.ColourNames[current.choices[mine].colour].ToLowerInvariant() + ".</b></color>");

        List<NetworkPlayer> players = ReadStandings();
        ClearBody();
        List<int> order = new List<int>();
        for (int i = 0; i < current.choices.Count; i++) order.Add(i);
        order.Sort((a, b) => current.choices[a].seconds.CompareTo(current.choices[b].seconds));
        int bestSeconds = Mathf.RoundToInt(current.choices[fastest].seconds);
        foreach (int i in order)
        {
            RouteChoice c = current.choices[i];
            int seconds = Mathf.RoundToInt(c.seconds);
            List<string> who = new List<string>();
            foreach (NetworkPlayer p in players)
            {
                RouteChoiceGame.ReadLeg(p, k, out int pick, out _);
                if (pick == i) who.Add(p.Object.HasStateAuthority ? "you" : BearMode.Name(p));
            }
            string time = i == fastest ? RelayStats.Time(seconds) : "<color=#C0392B>+" + (seconds - bestSeconds) + " s</color>";
            string name = "<b>" + RouteChoiceGame.ColourNames[c.colour] + "</b>  <size=75%><color=#5B7563>" + Mathf.RoundToInt(c.meters) + " m</color></size>"
                + "\n<size=75%><color=#5B7563>" + (who.Count > 0 ? string.Join(", ", who) : "nobody") + "</color></size>";
            AddRow(RouteChoiceGame.Colours[c.colour], name, time, i == mine);
        }

        nextButton.onClick.RemoveAllListeners();
        nextButton.onClick.AddListener(() => ShowOverlay(Page.Legs));
        CourseUI.SetButtonText(nextButton, "Back to the results");
        nextButton.interactable = true;
        nextButton.gameObject.SetActive(true);
        // Only the buttons that lead somewhere (no "Next leg" on the last leg)
        previousLegButton.gameObject.SetActive(Neighbour(k, -1) > 0);
        nextLegButton.gameObject.SetActive(Neighbour(k, 1) > 0);
    }

    // The leg asked before (-1) or after (+1) leg k, or -1 when there is none
    int Neighbour(int k, int direction)
    {
        if (results == null) return -1;
        for (int j = k + direction; j >= 1 && j <= legs; j += direction)
        {
            if (results[j] != null && picks[j] != -2) return j;
        }
        return -1;
    }

    void StepLeg(int direction)
    {
        int j = Neighbour(legShown, direction);
        if (j > 0) ShowLeg(j);
    }

    // The players in the game, best score first; also notes your place for the prize money
    List<NetworkPlayer> ReadStandings()
    {
        List<NetworkPlayer> players = new List<NetworkPlayer>();
        foreach (NetworkPlayer p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (p.Object != null && p.Object.IsValid) players.Add(p);
        }
        players.Sort((a, b) => b.choiceScore.CompareTo(a.choiceScore));
        foreach (NetworkPlayer p in players)
        {
            if (!p.Object.HasStateAuthority) continue;
            lastPlace = PlaceOf(p, players);
            lastPlayers = players.Count;
        }
        return players;
    }

    // Same points, same place
    static int PlaceOf(NetworkPlayer player, List<NetworkPlayer> players)
    {
        int place = 1;
        foreach (NetworkPlayer p in players) if (p.choiceScore > player.choiceScore) place++;
        return place;
    }

    // The prize money is paid once everyone is done, so the places are final
    void FillMoney()
    {
        if (failed || body == null) return;
        heading.text = "Prize money";
        if (paid)
        {
            ShowPaid();
            return;
        }
        ClearBody();
        List<NetworkPlayer> players = ReadStandings();
        List<string> waiting = new List<string>();
        foreach (NetworkPlayer p in players) if (!p.choiceDone) waiting.Add(BearMode.Name(p));
        if (waiting.Count > 0)
        {
            info.text = "Waiting for " + string.Join(", ", waiting) + " to finish. The money is paid when everyone is done.";
            TextMeshProUGUI note = CourseUI.Text("Note", body, "Everyone gets " + RouteChoiceGame.TookPart + " kr for taking part, and more for a good place among the others.", 20, CourseUI.InkMuted);
            CourseUI.Size(note, 60);
            return;
        }
        Pay(lastPlace, lastPlayers, true);
        ShowPaid();
    }

    int paidTookPart;
    int paidPlace;
    int paidPlaceOf;
    int paidForPlace;
    bool paidTrophy;

    // Money for taking part, and for the place when the game was played to the end
    void Pay(int place, int players, bool finished)
    {
        if (paid) return;
        paid = true;
        paidTookPart = RouteChoiceGame.TookPart;
        paidPlace = finished ? place : 0;
        paidPlaceOf = players;
        paidForPlace = finished && place > 0 ? RouteChoiceGame.ForPlace(place, players) : 0;
        paidTrophy = finished && place == 1 && players >= 2;
        if (Money.Instance != null)
        {
            Money.Instance.AddMoney(paidTookPart + paidForPlace);
            if (paidTrophy) Money.Instance.AddTrophy(1);
        }
    }

    void ShowPaid()
    {
        ClearBody();
        info.text = paidPlace > 0
            ? "You came <b>" + CompetitionFlow.Ordinal(paidPlace) + " of " + paidPlaceOf + "</b>."
            : "Thanks for taking part.";
        AddRow(CourseUI.Accent, "Took part", "+" + paidTookPart + " kr", false);
        if (paidPlace > 0) AddRow(CourseUI.Accent, CompetitionFlow.Ordinal(paidPlace) + " place of " + paidPlaceOf, "+" + paidForPlace + " kr", false);
        AddRow(CourseUI.Forest, "<b>Total</b>" + (paidTrophy ? "\n<size=75%><color=#5B7563>+1 trophy for the win</color></size>" : ""), "+" + (paidTookPart + paidForPlace) + " kr", true);
        if (Money.Instance != null)
        {
            TextMeshProUGUI balance = CourseUI.Text("Balance", body, "You now have " + Money.Instance.GetMoney() + " kr", 20, CourseUI.InkMuted);
            CourseUI.Size(balance, 34);
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
            List<Vector3> points = WorldPoints(leg, c.points);
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
    List<Vector3> WorldPoints(int onLeg, List<Vector2> line)
    {
        List<Vector3> points = new List<Vector3>(line.Count + 2);
        points.Add(manager.controlPoints[onLeg - 1].position);
        foreach (Vector2 p in line) points.Add(GridToWorld(p));
        points.Add(manager.controlPoints[onLeg].position);
        return points;
    }

    Vector3 GridToWorld(Vector2 p)
    {
        Vector3 origin = manager.GridToWorldPosition(0, 0);
        return new Vector3(origin.x + p.x * manager.cellSize, origin.y + p.y * manager.cellSize, origin.z);
    }

    // The leg turned so the start is at the bottom and the control at the top, with both controls and every route
    // on the screen to the right of the panel, and half a control circle to spare
    void FitCamera(RouteChoiceLeg current)
    {
        if (manager == null || cam == null || leg < 1 || leg > legs) return;
        Vector3 from = manager.controlPoints[leg - 1].position;
        Vector3 to = manager.controlPoints[leg].position;
        List<Vector3> points = new List<Vector3> { from, to };
        if (current != null)
        {
            foreach (RouteChoice c in current.choices)
            {
                foreach (Vector2 p in c.points) points.Add(GridToWorld(p));
            }
        }
        Vector2 d = to - from;
        float angle = d.sqrMagnitude > 1e-6f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : 90f;
        Fit(points, Quaternion.Euler(0f, 0f, angle - 90f), 60f * MapDefinition.WorldUnitsPerMeter);
    }

    // How much of the screen's width the panel covers, from the left edge
    float PanelShare()
    {
        RectTransform rect = panel != null ? (RectTransform)panel.transform : null;
        if (rect == null || Screen.width <= 0) return 0.35f;
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera ui = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        float right = 0f;
        foreach (Vector3 c in corners) right = Mathf.Max(right, RectTransformUtility.WorldToScreenPoint(ui, c).x);
        return Mathf.Clamp(right / Screen.width, 0f, 0.7f);
    }

    void Fit(List<Vector3> points, Quaternion rotation, float margin)
    {
        // Measured along the turned screen's axes
        Vector3 across = rotation * Vector3.right;
        Vector3 along = rotation * Vector3.up;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 p in points)
        {
            float x = Vector3.Dot(p, across), y = Vector3.Dot(p, along);
            minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
            minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
        }
        // The map is shown in the part of the screen right of the panel, with a small gap on both sides
        float left = PanelShare() + 0.02f;
        float usable = Mathf.Max(0.2f, 1f - left - 0.02f);
        float aspect = Mathf.Max(0.1f, cam.aspect);
        float halfWidth = (maxX - minX) * 0.5f * 1.05f + margin;
        float halfHeight = (maxY - minY) * 0.5f * 1.05f + margin;
        float size = Mathf.Max(halfHeight, halfWidth / (aspect * usable));
        // The middle of the free part, from the middle of the screen, in world units
        float shift = (left + usable * 0.5f - 0.5f) * 2f * size * aspect;
        float cx = (minX + maxX) * 0.5f - shift;
        float cy = (minY + maxY) * 0.5f;
        Vector3 centre = across * cx + along * cy;
        fittedSize = size;
        fittedRotation = rotation;
        fittedPosition = new Vector3(centre.x, centre.y, -10f);
        fitted = true;
        cam.transform.rotation = rotation;
        cam.orthographicSize = size;
        cam.transform.position = fittedPosition;
    }

    // The race start view can still turn the camera to the leg after the first fit (it runs once the network game
    // starts): the map is always shown as fitted
    void KeepFit()
    {
        if (!fitted || cam == null) return;
        if (Quaternion.Angle(cam.transform.rotation, fittedRotation) < 0.01f) return;
        cam.transform.rotation = fittedRotation;
        cam.orthographicSize = fittedSize;
        cam.transform.position = fittedPosition;
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

    // The drawing screen's Undo button and the race clock mean nothing here
    void HideHud()
    {
        GameObject undo = GameObject.Find("UndoButton");
        if (undo != null) { undo.SetActive(false); hiddenHud.Add(undo); }
        foreach (Text box in FindObjectsByType<Text>(FindObjectsSortMode.None))
        {
            if (box.name != "Sekunder" || box.transform.parent == null) continue;
            GameObject clock = box.transform.parent.gameObject;
            if (!clock.activeSelf) continue;
            clock.SetActive(false);
            hiddenHud.Add(clock);
        }
    }

    void OnDestroy()
    {
        // The host took everyone back to the lobby before the money was paid: you still get paid for taking part,
        // and for your place if you had played to the end
        if (!paid && !failed && question > 0) Pay(lastPlace, lastPlayers, step == Step.Done && lastPlace > 0);
        foreach (GameObject hidden in hiddenHud) if (hidden != null) hidden.SetActive(true);
        KeysOpen = false;
        RouteCompareView.HideRunners(false);
        ClearDrawn();
        if (panel != null) Destroy(panel);
        if (overlay != null) Destroy(overlay);
    }
}
