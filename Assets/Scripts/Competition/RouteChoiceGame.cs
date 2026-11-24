using System.Collections;
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
    // (1 to 4 at first, changeable in the Esc menu under Key bindings; saved per computer)
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
        KeysChanged?.Invoke();
    }

    // A key was changed (in the Esc menu): the route boxes on screen show the new key
    public static event System.Action KeysChanged;

    // True when the colour's key was pressed this frame (the number keys also work on the number pad)
    public static bool Pressed(int colour)
    {
        KeyCode key = Key(colour);
        if (Input.GetKeyDown(key)) return true;
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return Input.GetKeyDown(KeyCode.Keypad0 + (key - KeyCode.Alpha0));
        return false;
    }

    // Held down, the route lines are hidden so the clean map shows. Two keys (right mouse button and Tab at first),
    // both changeable in the Esc menu; a mouse button works too.
    static readonly KeyCode[] DefaultHideKeys = { KeyCode.Mouse1, KeyCode.Tab };

    public static KeyCode HideKey(int which)
    {
        string saved = PlayerPrefs.GetString("RouteChoice.HideKey." + which, "");
        if (saved.Length > 0 && System.Enum.TryParse(saved, out KeyCode key)) return key;
        return DefaultHideKeys[which];
    }

    public static void SetHideKey(int which, KeyCode key)
    {
        PlayerPrefs.SetString("RouteChoice.HideKey." + which, key.ToString());
        PlayerPrefs.Save();
        KeysChanged?.Invoke();
    }

    public static bool HideHeld()
    {
        return Input.GetKey(HideKey(0)) || Input.GetKey(HideKey(1));
    }

    public static bool IsHideKeyDown()
    {
        return Input.GetKeyDown(HideKey(0)) || Input.GetKeyDown(HideKey(1));
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
            case KeyCode.Mouse1: return "Right mouse";
            case KeyCode.Mouse2: return "Middle mouse";
        }
        if (key >= KeyCode.Mouse3 && key <= KeyCode.Mouse6) return "Mouse " + (int)(key - KeyCode.Mouse0 + 1);
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
    // (2026-11-19: money for the time played instead of a fixed 50 kr, so a course without route choices that is over
    // in seconds pays nothing)
    public static int TookPart => CompetitionRewards.ForTime(CompetitionFlow.PlayedSeconds);

    // Only the players with a strictly lower score count as beaten: a tie beats nobody (2026-11-19)
    public static int ForBeaten(int beaten, int players) => CompetitionRewards.ForBeaten(beaten, players, true);
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
    bool paid;
    int lastPlace;     // the place among the others the last time the standings were looked at (0 = unknown)
    int lastPlayers;
    int lastBeaten;    // the players with a lower score than yours the last time the standings were looked at

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
    readonly List<GameObject> hiddenHud = new List<GameObject>();
    bool fitted;
    float fittedSize;
    Vector3 fittedPosition;
    Quaternion fittedRotation = Quaternion.identity;
    readonly List<GameObject> drawn = new List<GameObject>();
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
            agent.originalBaseSpeed * TerrainSpeedModel.RaceSpeedScale(), PackRunning.GameSecondsPerSecond,
            CourseLoader.LoadedMap != null ? CourseLoader.LoadedMap.IsSprint : manager.cellSize / MapDefinition.WorldUnitsPerMeter < 5f)
        {
            VariantSlower = RouteChoiceGenerator.Variants
        };
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

        // Changing the keys and ending the game for everyone are in the Esc menu (Key bindings, End competition)
        RouteChoiceGame.KeysChanged += RefreshChoiceKeys;

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
        if (prizeShow != null) { StopCoroutine(prizeShow); prizeShow = null; }
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

    void LateUpdate()
    {
        KeepFit();
    }

    void Update()
    {
        if (failed) return;
        ApplyPeek();
        bool keysOpen = EscMenu.IsOpen; // while the Esc menu is open (keys being changed), key presses do not answer
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
    }

    // The route lines are shown at first; hold a hide key (right mouse button or Tab at first) to see the clean map
    static bool PeekHeld()
    {
        return RouteChoiceGame.HideHeld();
    }

    void ApplyPeek()
    {
        bool hide = PeekHeld();
        foreach (GameObject go in drawn)
        {
            if (go != null && go.activeSelf == hide) go.SetActive(!hide);
        }
    }

    // A key on the keyboard, not a mouse button (clicks are for the buttons) and not Escape (the menu)
    static bool AnyKeyForNext()
    {
        if (!Input.anyKeyDown) return false;
        if (Input.GetKeyDown(KeyCode.Tab) || RouteChoiceGame.IsHideKeyDown()) return false;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) return false;
        return !Input.GetKeyDown(KeyCode.Escape);
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
        TextMeshProUGUI note = CourseUI.Text("Note", body, "A runner with speed 100 runs every route. Ground, slope and turns all count, so the shortest route is not always the fastest.\nHold Tab or the right mouse button to hide the lines and see the map.", 18, CourseUI.InkMuted);
        CourseUI.Size(note, 100);
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
                + RelayStats.Gap(mine.seconds - best.seconds) + " faster than " + RouteChoiceGame.ColourNames[mine.colour].ToLowerInvariant() + ".";
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
            string time = i == fastest ? RelayStats.Time(seconds) : "<color=#C0392B>+" + RelayStats.Gap(seconds - bestSeconds) + "</color>";
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
    Button overlayBack;
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
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.Object != null && local.Object.IsValid)
        {
            local.choiceScore = score;
            local.choiceRight = right;
            local.choiceAnswered = question;
            local.choiceQuestions = question;
            local.choiceDone = true;
        }
        SendContestResult();
        ClearDrawn();
        panelHeading = heading;
        panelInfo = info;
        panelBody = body;
        BuildOverlay();
        ShowOverlay(Page.Legs);
    }

    // A weekly or daily contest (ContestRun): the points, and every question's routes with the one picked, so the
    // others can see the route choices afterwards
    void SendContestResult()
    {
        if (!ContestRun.InRace || results == null || picks == null) return;
        List<ContestReplay.ChoiceLeg> asked = new List<ContestReplay.ChoiceLeg>();
        for (int k = 1; k < results.Length && k < picks.Length; k++)
        {
            RouteChoiceLeg made = results[k];
            if (made == null || picks[k] == -2 || made.choices == null || made.choices.Count == 0) continue;
            RouteChoiceGame.ReadLeg(NetworkPlayer.Local, k, out _, out int points);
            ContestReplay.ChoiceLeg leg = new ContestReplay.ChoiceLeg { leg = k, fastest = made.Fastest, pick = picks[k], points = points };
            foreach (RouteChoice c in made.choices)
            {
                leg.routes.Add(new ContestReplay.ChoiceRoute { colour = c.colour, seconds = c.seconds, world = WorldPoints(k, c.points) });
            }
            asked.Add(leg);
        }
        ContestRun.OnRouteChoiceFinished(score, asked);
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

        overlayBody = CourseUI.ScrollList("Rows", sheet.transform, out overlayScroll);
        RectTransform list = (RectTransform)overlayScroll.transform;
        list.anchorMin = Vector2.zero;
        list.anchorMax = Vector2.one;
        list.offsetMin = new Vector2(70, 120);
        list.offsetMax = new Vector2(-90, -190);
        CourseUI.Column(overlayBody.gameObject, 6, new RectOffset(0, 0, 0, 0)).childForceExpandHeight = false;
        FastScroll.Add(overlayScroll);

        // Bottom right: from the legs on to the results (places and prize money), and from there back to the lobby.
        // Bottom left on the results: back to the legs.
        overlayLobby = CourseUI.Button("Lobby", sheet.transform, "See results  >", CourseUI.ButtonKind.Primary, OnOverlayButton, 24);
        CourseUI.Place((RectTransform)overlayLobby.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-70, 36), new Vector2(560, 64));
        overlayBack = CourseUI.Button("Back", sheet.transform, "<  Legs", CourseUI.ButtonKind.Light, () => ShowOverlay(Page.Legs), 22);
        CourseUI.Place((RectTransform)overlayBack.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(70, 36), new Vector2(220, 64));
        overlayBack.gameObject.SetActive(false);
    }

    void OnOverlayButton()
    {
        // A contest has no results page of its own: its leaderboard and prizes are on the contests page
        if (page == Page.Legs && ContestRun.Active == null) ShowOverlay(Page.Money);
        else CompetitionFlow.HostReturnToLobby();
    }

    // The buttons at the bottom for the page shown
    void RefreshOverlayButtons()
    {
        if (overlayLobby == null) return;
        bool host = CompetitionFlow.LocalIsHost();
        if (page == Page.Legs)
        {
            CourseUI.SetButtonText(overlayLobby, ContestRun.Active != null ? CompetitionFlow.BackButtonText : "See results  >");
            overlayLobby.interactable = true;
        }
        else
        {
            CourseUI.SetButtonText(overlayLobby, host ? CompetitionFlow.BackButtonText : "The host takes everyone back to the lobby");
            overlayLobby.interactable = host;
        }
        if (overlayBack != null) overlayBack.gameObject.SetActive(page == Page.Money);
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
        RefreshOverlayButtons();
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

        heading.text = "Legs";
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
            string time = i == fastest ? RelayStats.Time(seconds) : "<color=#C0392B>+" + RelayStats.Gap(seconds - bestSeconds) + "</color>";
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
        // Everyone is done: the played time for the prize money stops here, the same for all (2026-11-24)
        if (players.Count > 0 && players.TrueForAll(p => p.choiceDone)) CompetitionFlow.MarkEveryoneDone();
        foreach (NetworkPlayer p in players)
        {
            if (!p.Object.HasStateAuthority) continue;
            lastPlace = PlaceOf(p, players);
            lastPlayers = players.Count;
            lastBeaten = 0;
            foreach (NetworkPlayer other in players) if (other.choiceScore < p.choiceScore) lastBeaten++;
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
        heading.text = "Results";
        if (paid)
        {
            ShowPaid(false);
            return;
        }
        ClearBody();
        List<NetworkPlayer> players = ReadStandings();
        List<string> waiting = new List<string>();
        foreach (NetworkPlayer p in players) if (!p.choiceDone) waiting.Add(BearMode.Name(p));
        if (waiting.Count > 0)
        {
            info.text = "Waiting for " + string.Join(", ", waiting) + " to finish. The prize money is handed out when everyone is done.";
            AddStandings(players);
            TextMeshProUGUI note = CourseUI.Text("Note", body, "Everyone who plays to the end gets " + MoneyLabel.Format(CompetitionRewards.PerHalfMinute) + " for every 30 seconds played, and more for a good place among the others.", 20, CourseUI.InkMuted);
            CourseUI.Size(note, 60);
            return;
        }
        Pay(lastPlace, lastPlayers, true);
        ShowPaid(true);
    }

    int paidTookPart;
    int paidPlace;
    int paidPlaceOf;
    int paidForPlace;
    int balanceBefore;
    Coroutine prizeShow;

    // Money for taking part, and for the place when the game was played to the end
    void Pay(int place, int players, bool finished)
    {
        if (paid) return;
        paid = true;
        // Weekly and daily contests pay their own prize money when the contest is over (2026-11-19, the user)
        if (ContestRun.Active != null) return;
        paidTookPart = finished ? RouteChoiceGame.TookPart : 0;
        paidPlace = finished ? place : 0;
        paidPlaceOf = players;
        paidForPlace = finished && place > 0 ? RouteChoiceGame.ForBeaten(lastBeaten, players) : 0;
        if (Money.Instance != null)
        {
            balanceBefore = Money.Instance.GetMoney();
            Money.Instance.AddMoney(paidTookPart + paidForPlace);
        }
    }

    // The final places, like after a race, and the prize money handed out: the first time with the lines coming in
    // one by one and the sum counting up, later at once
    void ShowPaid(bool animate)
    {
        ClearBody();
        if (prizeShow != null) StopCoroutine(prizeShow);
        prizeShow = null;
        info.text = paidPlace > 0
            ? "You came <b>" + CompetitionFlow.Ordinal(paidPlace) + " of " + paidPlaceOf + "</b>."
            : "Thanks for taking part.";
        AddStandings(ReadStandings());

        // The prize money card
        Image card = CourseUI.Panel("Prize money", body, new Color(1f, 1f, 1f, 0.7f));
        CourseUI.Size(card, 330);
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Your prize money", 26, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -18), new Vector2(500, 40));

        List<CanvasGroup> lines = new List<CanvasGroup>();
        float y = -70f;
        lines.Add(PrizeLine(card.transform, ref y, "Time played", "+" + MoneyLabel.Format(paidTookPart)));
        if (paidPlace > 0) lines.Add(PrizeLine(card.transform, ref y, CompetitionFlow.Ordinal(paidPlace) + " place of " + paidPlaceOf, "+" + MoneyLabel.Format(paidForPlace)));

        int total = paidTookPart + paidForPlace;
        TextMeshProUGUI totalLabel = CourseUI.Text("Total label", card.transform, "Total", 24, CourseUI.InkMuted, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(totalLabel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-300, 70), new Vector2(200, 60));
        TextMeshProUGUI totalText = CourseUI.Text("Total", card.transform, "+" + MoneyLabel.Format(total), 54, CourseUI.Accent, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(totalText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28, 64), new Vector2(270, 72));
        totalText.enableWordWrapping = false;
        TextMeshProUGUI balance = CourseUI.Text("Balance", card.transform, "", 20, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(balance.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28, 20), new Vector2(500, 34));
        int after = Money.Instance != null ? Money.Instance.GetMoney() : 0;
        balance.text = Money.Instance != null ? "You now have " + MoneyLabel.Format(after) : "";

        if (animate && isActiveAndEnabled) prizeShow = StartCoroutine(PrizeShow(lines, totalText, total, balance, Money.Instance != null ? balanceBefore : -1, after));
    }

    CanvasGroup PrizeLine(Transform card, ref float y, string label, string amount)
    {
        RectTransform line = CourseUI.Rect("Line", card);
        CourseUI.Place(line, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, y), new Vector2(0, 46));
        line.anchorMax = new Vector2(1f, 1f);
        line.offsetMax = new Vector2(-28, line.offsetMax.y);
        Image dot = CourseUI.Panel("Dot", line, CourseUI.Accent);
        CourseUI.Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0, 0), new Vector2(14, 14));
        TextMeshProUGUI name = CourseUI.Text("Label", line, label, 22, CourseUI.Ink);
        CourseUI.Stretch(name.rectTransform, 28, 200, 0, 0);
        name.alignment = TextAlignmentOptions.MidlineLeft;
        TextMeshProUGUI value = CourseUI.Text("Amount", line, amount, 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        CourseUI.Place(value.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0, 0), new Vector2(200, 40));
        y -= 50f;
        return line.gameObject.AddComponent<CanvasGroup>();
    }

    IEnumerator PrizeShow(List<CanvasGroup> lines, TextMeshProUGUI totalText, int total, TextMeshProUGUI balance, int before, int after)
    {
        foreach (CanvasGroup g in lines) g.alpha = 0f;
        if (totalText == null) yield break;
        totalText.text = "+0 kr";
        if (before >= 0) balance.text = "You had " + MoneyLabel.Format(before);
        yield return new WaitForSecondsRealtime(0.3f);
        // The lines slide in one by one
        foreach (CanvasGroup g in lines)
        {
            RectTransform r = (RectTransform)g.transform;
            Vector2 at = r.anchoredPosition;
            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.35f;
                g.alpha = k;
                r.anchoredPosition = at + new Vector2(40f * (1f - k) * (1f - k), 0f);
                yield return null;
            }
            g.alpha = 1f;
            r.anchoredPosition = at;
            yield return new WaitForSecondsRealtime(0.15f);
        }
        // The sum counts up, then pops
        for (float t = 0f; t < 1.1f; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / 1.1f, 3f);
            totalText.text = "+" + MoneyLabel.Format(Mathf.RoundToInt(total * k));
            if (before >= 0) balance.text = "You now have " + MoneyLabel.Format(Mathf.RoundToInt(Mathf.Lerp(before, after, k)));
            yield return null;
        }
        totalText.text = "+" + MoneyLabel.Format(total);
        if (before >= 0) balance.text = "You now have " + MoneyLabel.Format(after);
        RectTransform pop = totalText.rectTransform;
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            if (pop == null) { prizeShow = null; yield break; }
            pop.localScale = Vector3.one * (1f + 0.18f * Mathf.Sin(Mathf.PI * t / 0.3f));
            yield return null;
        }
        if (pop != null) pop.localScale = Vector3.one;
        prizeShow = null;
    }

    // Everyone in the game, best first: place, name, how many right and the points (like the list after a race)
    void AddStandings(List<NetworkPlayer> players)
    {
        for (int i = 0; i < players.Count; i++)
        {
            NetworkPlayer p = players[i];
            int place = PlaceOf(p, players);
            bool mine = p.Object.HasStateAuthority;
            Image row = CourseUI.Panel("Player", body, mine ? new Color(0.91f, 0.45f, 0.16f, 0.2f) : new Color(1f, 1f, 1f, 0.6f));
            CourseUI.Size(row, 64);
            Image badge = CourseUI.Panel("Place", row.transform, place == 1 ? CourseUI.Accent : CourseUI.Forest);
            CourseUI.Place(badge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(46, 46));
            TextMeshProUGUI number = CourseUI.Text("Number", badge.transform, place.ToString(), 24, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            CourseUI.Stretch(number.rectTransform);
            string name = "<b>" + BearMode.Name(p) + "</b>" + (mine ? " <color=#5B7563>(you)</color>" : "")
                + "  <size=80%><color=#5B7563>" + p.choiceRight + " of " + p.choiceQuestions + " right" + (p.choiceDone ? "" : ", still playing") + "</color></size>";
            TextMeshProUGUI label = CourseUI.Text("Name", row.transform, name, 24, CourseUI.Ink);
            CourseUI.Stretch(label.rectTransform, 74, 220, 4, 4);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false;
            TextMeshProUGUI points = CourseUI.Text("Points", row.transform, RouteChoiceGame.Points(p.choiceScore) + " p", 26, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            CourseUI.Place(points.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16, 0), new Vector2(200, 56));
            points.enableWordWrapping = false;
        }
        RectTransform gap = CourseUI.Rect("Gap", body);
        CourseUI.Size(gap, 12);
    }

    void ShowBackButton()
    {
        if (nextButton == null) return;
        bool host = CompetitionFlow.LocalIsHost();
        nextButton.onClick.RemoveAllListeners();
        nextButton.onClick.AddListener(CompetitionFlow.HostReturnToLobby);
        CourseUI.SetButtonText(nextButton, host ? CompetitionFlow.BackButtonText : "The host takes everyone back to the lobby");
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
            // While choosing, every route is just a little see-through, so what is right under it still shows a bit.
            // Afterwards the fastest is solid and thicker, the others a bit more see-through.
            bool highlight = fastest >= 0 && i == fastest;
            float w = highlight ? width * 1.5f : width;
            Color colour = RouteChoiceGame.Colours[c.colour];
            if (!highlight) colour.a = fastest < 0 ? 0.92f : 0.8f;
            // A dark edge under each line so it shows on every kind of ground
            Line("Route edge " + i, points, new Color(0.08f, 0.08f, 0.08f, highlight ? 0.75f : 0.5f), w * (highlight ? 1.6f : 1.45f), 40 + 2 * i);
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

    // Drawn as a flat mesh, not a LineRenderer: that one narrowed whole pieces to a thin wedge near the controls
    void Line(string name, List<Vector3> points, Color color, float width, int order)
    {
        drawn.Add(FlatLine.Make(name, points, color, width, order, material));
    }

    void ClearDrawn()
    {
        foreach (GameObject go in drawn) if (go != null) Destroy(go);
        drawn.Clear();
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
        RouteChoiceGame.KeysChanged -= RefreshChoiceKeys;
        RouteCompareView.HideRunners(false);
        // Start() switched the player's camera control off; the player lives on into the next race
        NetworkPlayer local = NetworkPlayer.Local;
        CameraController controller = local != null ? local.GetComponent<CameraController>() : null;
        if (controller != null) controller.enabled = true;
        ClearDrawn();
        if (panel != null) Destroy(panel);
        if (overlay != null) Destroy(overlay);
    }
}
