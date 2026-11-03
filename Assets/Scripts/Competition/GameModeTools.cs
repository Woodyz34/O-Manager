using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Made in the competition scene when a game mode needs something of its own there (Bears, Move a control).
// Holds the bear watcher and the tools panel, and runs the game modes' animations.
public class GameModeRuntime : MonoBehaviour
{
    public static GameModeRuntime Instance { get; private set; }

    // Something to run the game modes' coroutines on, made if needed
    public static GameModeRuntime Host
    {
        get
        {
            if (Instance == null) new GameObject("Game modes").AddComponent<GameModeRuntime>();
            return Instance;
        }
    }

    public static void StartIfNeeded(NetworkPlayer local)
    {
        if (!BearMode.On && !MoveControlMode.On) return;
        GameModeRuntime runtime = Host;
        if (local.manager != null) MoveControlMode.Capture(local.manager);
        if (BearMode.On && runtime.GetComponent<BearWatcher>() == null) runtime.gameObject.AddComponent<BearWatcher>();
        if (runtime.GetComponent<GameModeTools>() == null) runtime.gameObject.AddComponent<GameModeTools>();
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}

// The panel shown while drawing a stretch when Bears or Move a control is on: hide your bear, move the control you
// are drawing to. Clicks on the map go to the tool while one is picked, not to the drawing.
public class GameModeTools : MonoBehaviour
{
    enum Tool { None, Bear, Move }

    static GameModeTools instance;

    Tool tool = Tool.None;
    int clickFrame = -1;
    GameObject panel;
    Button bearButton;
    Button moveButton;
    TextMeshProUGUI hint;
    GameObject bearGhost;
    SpriteRenderer bearGhostSprite;
    GameObject moveGhost;
    LineRenderer reachCircle;
    float hintUntil;
    string hintOverride;

    static float U => MapDefinition.WorldUnitsPerMeter;

    // True while a tool takes the clicks on the map (and on the frame a click was used by one)
    public static bool TakesMouse => instance != null && (instance.tool != Tool.None || instance.clickFrame == Time.frameCount);

    void Awake()
    {
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (panel != null) Destroy(panel);
        if (bearGhost != null) Destroy(bearGhost);
        if (moveGhost != null) Destroy(moveGhost);
        if (reachCircle != null) Destroy(reachCircle.gameObject);
    }

    void Update()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        AgentMovement agent = local != null ? local.agentMovement : null;
        bool drawing = agent != null && local.Object != null && local.Object.IsValid && agent.manualPathDrawer != null
            && agent.manualPathDrawer.timeToDraw && !agent.manualPathDrawer.relayMode && !agent.WaitingForAllPlayersToDraw
            && !CompetitionFlow.ResultsShown && agent.pathfindingManager != null;

        if (!drawing)
        {
            SetTool(Tool.None);
            if (panel != null && panel.activeSelf) panel.SetActive(false);
            return;
        }
        if (panel == null) Build();
        if (panel == null) return;
        if (!panel.activeSelf) panel.SetActive(true);
        Refresh(local, agent);

        if (tool == Tool.None) return;
        // Right mouse button lets go of the tool
        if (Input.GetMouseButtonDown(1))
        {
            SetTool(Tool.None);
            return;
        }
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 mouse = cam.ScreenToWorldPoint(Input.mousePosition);
        mouse.z = -1.5f;
        bool overUI = agent.manualPathDrawer.IsPointerOverTaggedCanvas() || EscMenu.IsOpen;

        if (tool == Tool.Bear)
        {
            string problem = BearMode.PlaceProblem(mouse, agent.pathfindingManager);
            ShowBearGhost(mouse, problem == null && !overUI);
            if (Input.GetMouseButtonDown(0) && !overUI)
            {
                clickFrame = Time.frameCount;
                if (problem != null)
                {
                    Flash(problem);
                    return;
                }
                BearMode.Place(local, mouse);
                SetTool(Tool.None);
                Flash("Your bear is hidden. Only you can see it until someone finds it.");
            }
        }
        else if (tool == Tool.Move)
        {
            int index = agent.currentKontroll;
            Vector3 place = MoveControlMode.Clamp(index, mouse);
            string problem = MoveControlMode.PlaceProblem(place, agent);
            ShowMoveGhost(index, place, problem == null && !overUI);
            if (Input.GetMouseButtonDown(0) && !overUI)
            {
                clickFrame = Time.frameCount;
                if (problem != null)
                {
                    Flash(problem);
                    return;
                }
                MoveControlMode.PlaceLocal(local, agent, place);
                SetTool(Tool.None);
                Flash("Control " + index + " moved. The others find out when everyone has drawn. Now draw your line to its new place.");
            }
        }
    }

    void Build()
    {
        GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
        if (canvas == null) return;
        Image card = CourseUI.CardPanel("Game mode tools", canvas.transform);
        panel = card.gameObject;
        CourseUI.Place(card.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24, 40), new Vector2(360, 10));
        VerticalLayoutGroup column = CourseUI.Column(panel, 10, new RectOffset(20, 20, 18, 20));
        column.childForceExpandHeight = false;
        ContentSizeFitter fit = panel.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CourseUI.Text("Title", panel.transform, "GAME MODES", 22, CourseUI.InkMuted, FontStyles.Bold);
        CourseUI.Size(title, 28);
        if (BearMode.On)
        {
            bearButton = CourseUI.Button("Bear", panel.transform, "", CourseUI.ButtonKind.Secondary, () => SetTool(tool == Tool.Bear ? Tool.None : Tool.Bear), 22);
            CourseUI.Size(bearButton, 56);
        }
        if (MoveControlMode.On)
        {
            moveButton = CourseUI.Button("Move", panel.transform, "", CourseUI.ButtonKind.Secondary, () => SetTool(tool == Tool.Move ? Tool.None : Tool.Move), 22);
            CourseUI.Size(moveButton, 56);
        }
        hint = CourseUI.Text("Hint", panel.transform, "", 18, CourseUI.Ink);
        CourseUI.Size(hint, 96);
        hint.overflowMode = TextOverflowModes.Overflow;
        panel.SetActive(false);
    }

    void Refresh(NetworkPlayer local, AgentMovement agent)
    {
        string text = null;
        if (bearButton != null)
        {
            bool placed = local.bearPlaced;
            bearButton.interactable = !placed;
            CourseUI.SetButtonText(bearButton, tool == Tool.Bear ? "Cancel" : placed ? "Bear hidden" : "Hide your bear");
            CourseUI.SetButtonColor(bearButton, tool == Tool.Bear ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
            if (tool == Tool.Bear) text = "Click on the map where your bear should hide (not near a control). Right-click to cancel.";
        }
        if (moveButton != null)
        {
            string cannot = MoveControlMode.CannotMove(local, agent);
            bool movedHere = local.moveUsed && local.moveControl == agent.currentKontroll;
            moveButton.interactable = cannot == null;
            CourseUI.SetButtonText(moveButton, tool == Tool.Move ? "Cancel"
                : movedHere ? "Move control " + agent.currentKontroll + " again"
                : cannot == null ? "Move control " + agent.currentKontroll
                : local.moveUsed ? "Move used" : "Move a control");
            CourseUI.SetButtonColor(moveButton, tool == Tool.Move ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
            if (tool == Tool.Move) text = "Click where control " + agent.currentKontroll + " should be, at most " + Mathf.RoundToInt(MoveControlMode.MaxMeters) + " m from where it was laid. Right-click to cancel.";
            else if (text == null && cannot != null && !local.moveUsed) text = cannot;
        }
        if (text == null)
        {
            text = !string.IsNullOrEmpty(hintOverride) && Time.unscaledTime < hintUntil ? hintOverride
                : (bearButton != null && !local.bearPlaced ? "One bear each, for the whole race. " : "")
                  + (moveButton != null && !local.moveUsed ? "One control move each: the control you are drawing to." : "");
        }
        hint.text = text;
    }

    void Flash(string message)
    {
        hintOverride = message;
        hintUntil = Time.unscaledTime + 6f;
    }

    void SetTool(Tool next)
    {
        if (tool == next) return;
        tool = next;
        if (bearGhost != null) bearGhost.SetActive(false);
        if (moveGhost != null) moveGhost.SetActive(false);
        if (reachCircle != null) reachCircle.gameObject.SetActive(false);
        if (next != Tool.None)
        {
            hintOverride = null;
            // A line half drawn is dropped, so the click does not carry on drawing it
            NetworkPlayer local = NetworkPlayer.Local;
            if (local != null && local.agentMovement != null && local.agentMovement.manualPathDrawer != null && local.agentMovement.manualPathDrawer.isDrawing)
            {
                local.agentMovement.manualPathDrawer.CancelCurrentLine();
            }
        }
    }

    void ShowBearGhost(Vector3 at, bool ok)
    {
        if (bearGhost == null)
        {
            bearGhost = new GameObject("Bear to hide");
            bearGhostSprite = bearGhost.AddComponent<SpriteRenderer>();
            bearGhostSprite.sprite = ModeBanner.BearSprite;
            bearGhostSprite.sortingOrder = 36;
            bearGhost.transform.localScale = Vector3.one * BearMode.SizeMeters * U;
        }
        bearGhost.SetActive(true);
        bearGhost.transform.position = at;
        bearGhostSprite.color = ok ? new Color(1f, 1f, 1f, 0.75f) : new Color(1f, 0.45f, 0.45f, 0.6f);
    }

    void ShowMoveGhost(int index, Vector3 at, bool ok)
    {
        if (moveGhost == null)
        {
            moveGhost = MoveControlMode.ControlMark("Control to move", "", CourseRenderer.CoursePurple, 36);
        }
        if (reachCircle == null)
        {
            reachCircle = new GameObject("How far it may move").AddComponent<LineRenderer>();
            reachCircle.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            reachCircle.useWorldSpace = true;
            reachCircle.loop = true;
            reachCircle.widthMultiplier = 3f * U;
            reachCircle.startColor = reachCircle.endColor = new Color(0.91f, 0.45f, 0.16f, 0.9f);
            reachCircle.sortingOrder = 35;
            reachCircle.positionCount = 64;
        }
        Vector3 original = MoveControlMode.Original(index);
        reachCircle.gameObject.SetActive(true);
        float radius = MoveControlMode.MaxMeters * U;
        for (int k = 0; k < 64; k++)
        {
            float a = k * Mathf.PI * 2f / 64;
            reachCircle.SetPosition(k, new Vector3(original.x + Mathf.Cos(a) * radius, original.y + Mathf.Sin(a) * radius, -1.45f));
        }
        moveGhost.SetActive(true);
        moveGhost.transform.position = new Vector3(at.x, at.y, -1.5f);
        Color color = ok ? CourseRenderer.CoursePurple : CourseRenderer.ErrorRed;
        color.a = 0.85f;
        LineRenderer line = moveGhost.GetComponent<LineRenderer>();
        line.startColor = line.endColor = color;
    }
}
