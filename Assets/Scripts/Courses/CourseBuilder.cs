using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The course builder page: pick a map, click out the start, the controls and the finish, check that every
// control can be reached, then save the course and publish it so other players can run it.
//
// Mouse: the mark that the next click places follows the mouse (a triangle, a numbered circle or a double circle).
// Left click places it, or drags a mark that is already there; clicking a line between two marks puts a control in
// between; right click on a mark removes it; right or middle drag moves the map, the wheel zooms. Shift places
// freely (no grabbing, no clicking into place), Ctrl+Z takes the last change back.
//
// Two side panels (2026-11-20): on the left the legs of a relay and the list of controls of the leg being laid, on the
// right the tools (start, controls, fork, finish, out of bounds) and Save. Both can be made smaller or bigger and be
// slid into the edge of the screen. The course's name, the maker's name and forest/sprint are asked when saving.
//
// A course can also be a relay: up to 10 legs, each with its own course (a new leg starts as a copy of the leg before,
// and "Copy a leg" copies any other leg). The Fork button (or G) puts a fork after the last control: you choose how
// many branches and which legs run it (one branch per leg, "Farsta": every team runs every branch once over those
// legs). Then you click the first control of branch A, B, C... one after the other, go through the branches again to
// add more controls ("Branch done" or Enter moves on), and the next control after that is where the branches meet.
// The forks and controls of other legs are shown faded, and the mark at the mouse clicks into place on them: on a
// plain control it becomes the same control (shared by the legs, and moved together from then on), on a fork's control
// the whole fork, with its common controls, is put on the leg being laid.
public class CourseBuilder : MonoBehaviour
{
    public Camera mapCamera;

    enum Tool { Start, Control, Finish, Branch, Forbidden }

    MapDefinition map;
    MapGrid grid;
    SpriteRenderer mapRenderer;
    CourseRenderer courseRenderer;

    // ---- What is being laid, in world positions ----

    class EditItem
    {
        public int fork = -1;          // index into forks, or -1 for a control
        public Vector3 point;
        public bool tv;                // relays: a TV control (a 3D view of it during the race)
    }

    class EditCourse
    {
        public Vector3? start;
        public List<EditItem> items = new List<EditItem>();
        public Vector3? finish;
        public bool finishTv;
    }

    class EditFork
    {
        public List<List<Vector3>> branches = new List<List<Vector3>>();
        public List<int> legs = new List<int>();   // the legs the course setter chose for it (one branch per leg)
        public int home = -1;                      // the leg it was laid on: only there can its controls be changed (2026-11-20)
    }

    readonly List<EditCourse> courses = new List<EditCourse> { new EditCourse() };
    readonly List<EditFork> forks = new List<EditFork>();
    readonly List<int> legCourse = new List<int>();   // relay: the course each leg runs
    readonly List<LegSex> legSex = new List<LegSex>();  // relay: who may run each leg (women, men or anyone)
    bool relay;
    string terrain = CourseData.Forest;   // forest or sprint course, for sorting and for mistakes
    int legs = 3;
    bool forkEveryVariant = true;
    int selectedLeg;
    // Legs changed by a fork on another leg (it was put in, or taken out and run straight): shown with a yellow dot
    // until the course setter opens them
    readonly List<int> checkLegs = new List<int>();

    // Drawing a fork's branches (2026-11-20): first the first control of every branch, one after the other, then branch
    // by branch again for more controls. The branches finished (or set to go straight) in this round:
    bool forkFirstRound;
    readonly List<int> branchesDone = new List<int>();

    EditCourse Cur => courses[relay && selectedLeg < legCourse.Count ? legCourse[selectedLeg] : 0];

    CourseData editing;            // the saved course being changed, or null for a new one
    Tool tool = Tool.Start;
    int branchFork = -1, branchIndex = -1;   // where the Branch tool adds controls
    Vector3? errorPoint;

    // Out-of-bounds areas, for the whole course (every leg of a relay): finished ones, and the one being clicked out
    readonly List<List<Vector3>> areas = new List<List<Vector3>>();
    readonly List<List<Vector3>> boundaries = new List<List<Vector3>>();   // uncrossable boundary lines
    // Bridges (2026-11-19): drawn like a path, over a lake, a stream or anything else; the runners may cross on them
    readonly List<List<Vector3>> bridges = new List<List<Vector3>>();
    List<Vector3> openArea;
    bool openIsLine;          // the open shape is a boundary line (or a bridge)
    bool openIsBridge;        // the open shape is a bridge
    bool drawLine;            // the Forbidden tool draws boundary lines, not areas
    bool drawBridge;          // the Forbidden tool draws bridges (drawLine is true too)
    Button boundaryButton;
    Button bridgeButton;

    // The Move button: the next click on the map puts this start, control or finish there
    MarkType? moveType;
    int moveItem = -1;
    string moveName;
    ForbiddenAreaRenderer areaRenderer;
    int dragArea = -1, dragCorner = -1;     // the corner being dragged (dragArea -1 = the open area)
    bool draggingCorner;
    Button forbiddenButton;

    // Marks on the map: the course being laid, and the faded forks of other legs
    enum MarkType { Start, Control, BranchControl, Finish, Shadow }

    class Mark
    {
        public MarkType type;
        public int item = -1;
        public int fork = -1;
        public int branch = -1;
        public int index = -1;
        public Vector3 position;
        public string label;
        public CourseRenderer.MarkKind kind;   // shadows: what is drawn there
    }

    readonly List<Mark> marks = new List<Mark>();
    readonly List<Mark> shadows = new List<Mark>();
    Mark dragging;

    // Controls right at a fence, a wall or an out-of-bounds area: the side the course setter picked (PunchSides)
    readonly List<PunchSide> punchSides = new List<PunchSide>();

    // A click (without dragging) on the start or a control picks it: the next controls are put in after it, in the
    // middle of the course (pick control 3 and the new one becomes 4). -1 = after the start; null = at the end.
    int? insertAfter;

    // The pick, when it still fits the course (else it is let go)
    int? InsertAfter()
    {
        if (!insertAfter.HasValue) return null;
        EditCourse course = Cur;
        int after = insertAfter.Value;
        if (tool != Tool.Control || after < -1 || after >= course.items.Count - 1 || (after == -1 && !course.start.HasValue))
        {
            insertAfter = null;
            return null;
        }
        return after;
    }

    Mark InsertAfterMark()
    {
        int? after = InsertAfter();
        if (!after.HasValue) return null;
        return after.Value < 0 ? marks.Find(m => m.type == MarkType.Start) : marks.Find(m => m.type == MarkType.Control && m.item == after.Value);
    }

    void PickInsertAfter(Mark mark)
    {
        int after = mark.type == MarkType.Start ? -1 : mark.item;
        EditCourse course = Cur;
        if (insertAfter == after || after >= course.items.Count - 1)
        {
            // Clicked again, or the last control (new ones go after it anyway): back to adding at the end
            bool was = insertAfter.HasValue;
            insertAfter = null;
            SetStatus(was ? "New controls go at the end of the course again." : "", false);
        }
        else
        {
            insertAfter = after;
            SetStatus("New controls go in after " + MarkName(mark) + ". Click it again (or press Esc) to add at the end again.", false);
        }
        cursorKey = null;
    }

    // The drag: where the mark was grabbed, where it came from, and the same control on other legs (moved with it)
    Vector3 dragGrab, dragFrom, dragMouse;
    bool dragMoved, dragUndo;
    readonly List<(EditCourse course, int slot)> dragLinks = new List<(EditCourse, int)>();   // slot: -1 start, -2 finish, else item
    readonly List<Vector3> dragSnaps = new List<Vector3>();   // other legs' controls the dragged mark clicks onto

    // The mark at the mouse: what the next click does
    enum HoverKind { None, Mark, Fork, Shared, Insert, Free }

    struct InsertSpot
    {
        public int item;       // where in the course's items the new control goes (fork -1)
        public int fork, branch, index;   // or where in a fork's branch
    }

    struct Hover
    {
        public HoverKind kind;
        public Mark mark;          // the mark or the faded control under the mouse
        public Vector3 point;      // where the click puts the mark
        public int fork;
        public InsertSpot spot;
        public string problem;     // why nothing can be put there, or null
    }

    CourseRenderer cursorRenderer;
    RectTransform cursorTip;
    TextMeshProUGUI cursorTipText;
    string cursorKey;              // what the cursor shows now, so it is only drawn again when it changes
    int drawnVersion;

    // Filled by BuildMarks: the lines of the leg being laid (a click on one adds a control there), and where the
    // next control is joined on
    readonly List<(Vector3 from, Vector3 to, InsertSpot spot)> ownLines = new List<(Vector3, Vector3, InsertSpot)>();
    readonly List<(Vector3 point, CourseRenderer.MarkKind kind)> courseTail = new List<(Vector3, CourseRenderer.MarkKind)>();
    readonly List<(Vector3 point, CourseRenderer.MarkKind kind)> branchTail = new List<(Vector3, CourseRenderer.MarkKind)>();
    (Vector3 point, CourseRenderer.MarkKind kind)? branchExit;
    string branchNextLabel = "";
    bool showOtherLegs = true;
    Button relaySettingsButton;
    Button copyLegButton;
    Button forkButton;
    Button undoButton;
    Vector3 lastPanMouse;
    bool panning;
    bool busy;

    // UI
    TMP_InputField nameField;
    TMP_InputField authorField;
    readonly Dictionary<Tool, Button> toolButtons = new Dictionary<Tool, Button>();
    TextMeshProUGUI hintText;
    TextMeshProUGUI statusText;
    TextMeshProUGUI summaryText;
    RectTransform pointList;
    Button myCoursesButton;
    Button saveButton;
    Button mapButton;
    Button typeButton;
    Button forestButton;
    Button sprintButton;
    GameObject relaySection;
    TextMeshProUGUI legsLabel;
    TextMeshProUGUI listTitle;
    Button legSexButton;
    RectTransform legButtonRow;
    GameObject canvasObject;
    SidePanelFrame leftFrame, rightFrame;
    GameObject savePopup;
    TextMeshProUGUI saveError;
    GameObject forkBar;

    const float RightPanelWidth = 470f;
    const float LeftPanelWidth = 440f;

    // The room the two side panels take on the screen, in canvas units
    float LeftRoom => leftFrame != null ? leftFrame.VisibleWidth : 0f;
    float RightRoom => rightFrame != null ? rightFrame.VisibleWidth : RightPanelWidth;

    void Start()
    {
        if (mapCamera == null) mapCamera = Camera.main;
        BuildUI();
        List<MapDefinition> maps = MapDefinition.All;
        if (maps.Count == 0)
        {
            // No map has been imported yet: Import map in the top-right corner is the way forward
            SetStatus("There are no maps yet. Press Import map to add one.", false);
            RefreshMyCourses();
            return;
        }
        SelectMap(maps[0]);
        RefreshMyCourses();
        RestoreDraft();
        // Sign in early so publishing is quick later; failure is shown when saving
        _ = CloudCourses.SignInAsync();
    }

    // ---- The work in progress is kept (2026-11-19) ----
    // Leaving the course builder (a race starting from the lobby, back to the main menu, quitting) used to lose the
    // course being laid. Now it is kept on this computer and comes back the next time the builder opens. "New course"
    // or another map starts afresh as before (the empty builder is what is kept then).

    const string DraftKey = "CourseBuilder.Draft";

    [Serializable]
    class Draft
    {
        public string course;        // CourseData as JSON
        public bool fromSaved;       // it was a saved course being changed (Save updates it)
        public string noStart = "";  // per course of the draft: "1" when it had no start yet
        public string noFinish = "";
    }

    float nextDraftSave;
    string lastDraft;

    bool HasContent()
    {
        if (nameField != null && nameField.text.Trim().Length > 0) return true;
        if (areas.Count > 0 || boundaries.Count > 0 || bridges.Count > 0) return true;
        foreach (EditCourse c in courses) if (c.start.HasValue || c.finish.HasValue || c.items.Count > 0) return true;
        return false;
    }

    // The courses in the order BuildCourse writes them (relays: only the ones a leg uses)
    List<EditCourse> DraftOrder()
    {
        List<EditCourse> order = new List<EditCourse>();
        if (!relay)
        {
            order.Add(Cur);
            return order;
        }
        for (int leg = 0; leg < legs && leg < legCourse.Count; leg++)
        {
            EditCourse c = courses[legCourse[leg]];
            if (!order.Contains(c)) order.Add(c);
        }
        return order;
    }

    void SaveDraft()
    {
        if (map == null || nameField == null) return;
        try
        {
            if (!HasContent())
            {
                if (lastDraft != "") PlayerPrefs.DeleteKey(DraftKey);
                lastDraft = "";
                return;
            }
            Draft draft = new Draft { course = CourseLibrary.ToJson(BuildCourse()), fromSaved = editing != null };
            foreach (EditCourse c in DraftOrder())
            {
                draft.noStart += c.start.HasValue ? "0" : "1";
                draft.noFinish += c.finish.HasValue ? "0" : "1";
            }
            string text = JsonUtility.ToJson(draft);
            if (text == lastDraft) return;
            lastDraft = text;
            PlayerPrefs.SetString(DraftKey, text);
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogWarning("CourseBuilder: could not keep the course being laid: " + e.Message);
        }
    }

    void RestoreDraft()
    {
        string text = PlayerPrefs.GetString(DraftKey, "");
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Draft draft = JsonUtility.FromJson<Draft>(text);
            CourseData course = draft != null && !string.IsNullOrEmpty(draft.course) ? CourseLibrary.Parse(draft.course) : null;
            if (course == null || MapDefinition.Find(course.mapId) == null) return;
            Edit(course);
            if (!draft.fromSaved) editing = null;
            List<EditCourse> order = DraftOrder();
            for (int i = 0; i < order.Count; i++)
            {
                if (i < draft.noStart.Length && draft.noStart[i] == '1') order[i].start = null;
                if (i < draft.noFinish.Length && draft.noFinish[i] == '1') order[i].finish = null;
            }
            lastDraft = text;
            SetTool(Tool.Control);
            SetStatus(draft.fromSaved ? "Welcome back: you were changing \"" + course.name + "\". Save to update it."
                                      : "Welcome back: here is the course you were laying. Press New course to start afresh.", false);
            Redraw();
        }
        catch (Exception e)
        {
            Debug.LogWarning("CourseBuilder: could not bring back the course being laid: " + e.Message);
        }
    }

    void OnDestroy()
    {
        SaveDraft();
    }

    void OnApplicationQuit()
    {
        SaveDraft();
    }

    // ---- Map ----

    void SelectMap(MapDefinition chosen)
    {
        map = chosen;
        grid = MapGrid.Load(map.gridName);
        if (mapRenderer == null)
        {
            mapRenderer = new GameObject("Map").AddComponent<SpriteRenderer>();
            courseRenderer = new GameObject("Course").AddComponent<CourseRenderer>();
            areaRenderer = new GameObject("Out-of-bounds areas").AddComponent<ForbiddenAreaRenderer>();
            cursorRenderer = new GameObject("Mark at the mouse").AddComponent<CourseRenderer>();
            cursorRenderer.sortingOrder = courseRenderer.sortingOrder + 2;
        }
        courseRenderer.sizeFactor = CourseRenderer.SizeFor(map);
        cursorRenderer.sizeFactor = courseRenderer.sizeFactor;
        areaRenderer.SetMapType(map.IsSprint);
        MapBackground background = map.GetBackground(map.defaultBackground);
        if (background != null)
        {
            mapRenderer.sprite = MapPackages.LoadSprite(background);
            mapRenderer.transform.position = new Vector3(background.position.x, background.position.y, 0f);
            mapRenderer.transform.rotation = Quaternion.Euler(0, 0, background.rotation);
            mapRenderer.transform.localScale = new Vector3(background.scale, background.scale, 1f);
        }
        if (mapButton != null) CourseUI.SetButtonText(mapButton, "Map: " + map.displayName + (MapDefinition.All.Count > 1 ? "  (change)" : ""));
        ClearCourse();
        undo.Clear();
        RefreshUndoButton();
        FitCamera();
    }

    void SetTerrain(string value)
    {
        terrain = value == CourseData.Sprint ? CourseData.Sprint : CourseData.Forest;
        if (forestButton == null) return;
        CourseUI.SetButtonColor(forestButton, terrain == CourseData.Forest ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
        CourseUI.SetButtonColor(sprintButton, terrain == CourseData.Sprint ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
    }

    // A map file from OCAD or OpenOrienteering Mapper becomes a new map to lay courses on
    void OpenMapImport()
    {
        if (busy || MapImportPanel.IsOpen) return;
        MapImportPanel.Open(canvasObject.transform, imported =>
        {
            if (imported == null) return;
            SelectMap(imported);
            RefreshMyCourses();
            // Whether it was also sent in is said in the message at the bottom of the screen
            SetStatus("Imported " + imported.displayName + ". You can lay courses on it now.", false);
        });
    }

    // ---- Choosing a map: a pop-up with a search field and forest/sprint filters ----

    GameObject mapPicker;
    RectTransform mapPickerList;
    TMP_InputField mapSearch;
    string mapFilter = "all";
    readonly Dictionary<string, Button> mapFilterButtons = new Dictionary<string, Button>();

    void OpenMapPicker()
    {
        if (busy || MapImportPanel.IsOpen || mapPicker != null) return;
        Image dim = CourseUI.Panel("Choose a map", canvasObject.transform, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
        CourseUI.Stretch(dim.rectTransform);
        mapPicker = dim.gameObject;
        Image card = CourseUI.CardPanel("Card", dim.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 820));
        CourseUI.Column(card.gameObject, 12, new RectOffset(32, 32, 28, 28));
        CourseUI.Size(CourseUI.Text("Title", card.transform, "Choose a map", 34, CourseUI.Ink, FontStyles.Bold), 44);

        mapSearch = CourseUI.Input("Search", card.transform, "Search for a map", 40, _ => FillMapPicker());
        CourseUI.Size(mapSearch, 52);

        RectTransform filters = CourseUI.Rect("Filters", card.transform);
        CourseUI.Row(filters.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(filters, 46);
        mapFilterButtons.Clear();
        foreach (var f in new[] { ("all", "All maps"), ("forest", "Forest"), ("sprint", "Sprint") })
        {
            string key = f.Item1;
            mapFilterButtons[key] = CourseUI.Button(f.Item2, filters, f.Item2, CourseUI.ButtonKind.Light, () => { mapFilter = key; FillMapPicker(); }, 20);
        }

        mapPickerList = CourseUI.ScrollList("Maps", card.transform, out _);
        CourseUI.Column(mapPickerList.gameObject, 6, new RectOffset(0, 8, 0, 0));
        CourseUI.Size(mapPickerList.parent.GetComponent<Image>(), 480).flexibleHeight = 1;

        CourseUI.Size(CourseUI.Button("Close", card.transform, "Cancel", CourseUI.ButtonKind.Light, CloseMapPicker, 22), 52);
        FillMapPicker();
    }

    void FillMapPicker()
    {
        if (mapPickerList == null) return;
        foreach (var pair in mapFilterButtons)
            CourseUI.SetButtonColor(pair.Value, pair.Key == mapFilter ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
        for (int i = mapPickerList.childCount - 1; i >= 0; i--) Destroy(mapPickerList.GetChild(i).gameObject);
        string search = mapSearch != null ? mapSearch.text.Trim().ToLowerInvariant() : "";
        int shown = 0;
        foreach (MapDefinition m in MapDefinition.All)
        {
            if (mapFilter == "forest" && !m.IsForest) continue;
            if (mapFilter == "sprint" && !m.IsSprint) continue;
            string type = m.mapType == "both" ? "Forest and sprint" : m.mapType == "sprint" ? "Sprint" : "Forest";
            if (m.isPrivate) type += "  ·  Private";
            if (search.Length > 0 && !(m.displayName + " " + type).ToLowerInvariant().Contains(search)) continue;
            MapDefinition chosen = m;
            // Maps this player imported can be edited (name, public or private, club, map edge)
            RectTransform row = CourseUI.Rect(m.displayName, mapPickerList);
            CourseUI.Row(row.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
            CourseUI.Size(row, 52);
            Button button = CourseUI.Button("Map", row, m.displayName + "   <size=18><color=#5B7563>" + type + "</color></size>",
                m.id == map?.id ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light, () => { CloseMapPicker(); if (chosen.id != map?.id) SelectMap(chosen); }, 22);
            button.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
            CourseUI.Size(button, 52, -1, 1);
            if (m.CanEdit)
            {
                Button edit = CourseUI.Button("Edit", row, "Edit", CourseUI.ButtonKind.Secondary, () => EditMap(chosen), 20);
                CourseUI.Size(edit, 52, 110);
            }
            shown++;
        }
        if (shown == 0) CourseUI.Size(CourseUI.Text("None", mapPickerList, "No maps match.", 20, CourseUI.InkMuted, FontStyles.Italic), 40);
    }

    void EditMap(MapDefinition chosen)
    {
        if (busy || MapEditPanel.IsOpen) return;
        MapEditPanel.Open(canvasObject.transform, chosen, (saved, rebuilt) =>
        {
            FillMapPicker();
            // The map on screen was deleted: show another one
            if (saved == null && map != null && map.id == chosen.id)
            {
                MapDefinition other = MapDefinition.All.Find(m => m.id != chosen.id);
                if (other != null) SelectMap(other);
                return;
            }
            if (saved == null || map == null || saved.id != map.id) return;
            // The map on screen changed: show the new version (a new edge means a new grid, so the course starts again)
            if (!rebuilt)
            {
                map = saved;
                if (mapButton != null) CourseUI.SetButtonText(mapButton, "Map: " + map.displayName + (MapDefinition.All.Count > 1 ? "  (change)" : ""));
            }
            else
            {
                SelectMap(saved);
            }
        });
    }

    void CloseMapPicker()
    {
        if (mapPicker != null) Destroy(mapPicker);
        mapPicker = null;
        mapPickerList = null;
    }

    void FitCamera()
    {
        if (mapRenderer == null || mapRenderer.sprite == null) return;
        Bounds bounds = mapRenderer.bounds;
        // Leave room for the panels on the left and the right
        // The canvas is scaled so all of 1920x1080 fits (ScreenSettings), so on a wide screen it is wider than 1920
        float uiScale = Mathf.Min(Screen.width / ScreenSettings.ReferenceResolution.x, Screen.height / ScreenSettings.ReferenceResolution.y);
        float canvasWidth = Mathf.Max(1f, Screen.width / Mathf.Max(0.01f, uiScale));
        float visibleWidthShare = Mathf.Max(0.2f, 1f - (LeftRoom + RightRoom) / canvasWidth);
        mapCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / (mapCamera.aspect * visibleWidthShare)) * 1.02f;
        float worldWidth = mapCamera.orthographicSize * mapCamera.aspect * 2f;
        // The free part of the screen is moved off the middle by half the difference between the panels
        float shift = worldWidth * (RightRoom - LeftRoom) / (2f * canvasWidth);
        mapCamera.transform.position = new Vector3(bounds.center.x + shift, bounds.center.y, -10f);
    }

    // A row of the list was clicked: the map moves so that control is in the free part of the screen
    void FocusOn(Vector3 point)
    {
        float uiScale = Mathf.Min(Screen.width / ScreenSettings.ReferenceResolution.x, Screen.height / ScreenSettings.ReferenceResolution.y);
        float canvasWidth = Mathf.Max(1f, Screen.width / Mathf.Max(0.01f, uiScale));
        float worldWidth = mapCamera.orthographicSize * mapCamera.aspect * 2f;
        float shift = worldWidth * (RightRoom - LeftRoom) / (2f * canvasWidth);
        mapCamera.transform.position = new Vector3(point.x + shift, point.y, mapCamera.transform.position.z);
    }

    // ---- Input ----

    void Update()
    {
        // Kept every few seconds too, in case the game is closed some other way
        if (Time.unscaledTime >= nextDraftSave)
        {
            nextDraftSave = Time.unscaledTime + 5f;
            SaveDraft();
        }
        if (map == null || busy || MapImportPanel.IsOpen || MapEditPanel.IsOpen || mapPicker != null || coursesPopup != null
            || (savePopup != null && savePopup.activeSelf) || forkDialog != null)
        {
            ShowCursor(default);
            return;
        }
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                      && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        Vector3 mouseWorld = mapCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = -1f;

        // Drawing a fork's branches: Enter = this branch is done, Esc = stop drawing the branches
        if (tool == Tool.Branch && !typing)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) NextBranch(true);
            else if (Input.GetKeyDown(KeyCode.Escape)) StopForkDrawing();
        }
        // G: a new fork after the last control
        else if (relay && !typing && Input.GetKeyDown(KeyCode.G) && tool != Tool.Forbidden && !moveType.HasValue && dragging == null)
        {
            OpenForkDialog(-1, null);
            return;
        }

        // Esc lets go of a picked control: new controls go at the end again
        if (insertAfter.HasValue && !typing && Input.GetKeyDown(KeyCode.Escape))
        {
            insertAfter = null;
            cursorKey = null;
            SetStatus("New controls go at the end of the course again.", false);
        }

        // Zoom towards the mouse
        float wheel = Input.mouseScrollDelta.y;
        if (!overUI && Mathf.Abs(wheel) > 0.01f)
        {
            float oldSize = mapCamera.orthographicSize;
            // Sprint maps have 2 m cells, so they can be zoomed in further
            float newSize = Mathf.Clamp(oldSize * (wheel > 0 ? 0.85f : 1.18f), map.IsSprint ? 0.4f : 1.5f, 45f);
            mapCamera.orthographicSize = newSize;
            Vector3 after = mapCamera.ScreenToWorldPoint(Input.mousePosition);
            mapCamera.transform.position += new Vector3(mouseWorld.x - after.x, mouseWorld.y - after.y, 0f);
        }

        // Pan with right or middle drag
        if ((Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) && !overUI)
        {
            panning = false;
            lastPanMouse = Input.mousePosition;
        }
        if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
        {
            Vector3 delta = Input.mousePosition - lastPanMouse;
            if (delta.sqrMagnitude > 9f) panning = true;
            if (panning)
            {
                float unitsPerPixel = mapCamera.orthographicSize * 2f / Screen.height;
                mapCamera.transform.position -= new Vector3(delta.x, delta.y, 0f) * unitsPerPixel;
                lastPanMouse = Input.mousePosition;
            }
        }
        if (!typing)
        {
            Vector3 keys = new Vector3(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"), 0f);
            mapCamera.transform.position += keys * mapCamera.orthographicSize * Time.unscaledDeltaTime;
        }

        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        if (!typing && ctrl && Input.GetKeyDown(KeyCode.Z))
        {
            Undo();
            return;
        }

        if (tool == Tool.Forbidden)
        {
            ShowCursor(default);
            UpdateForbidden(mouseWorld, overUI, typing);
            return;
        }

        // Shift: place freely, without grabbing a mark, clicking into place on another leg or adding on a line
        bool free = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (dragging != null)
        {
            UpdateDrag(mouseWorld, free);
            return;
        }

        // What the mouse is over decides what a click does, and it is shown at the mouse before the click
        bool movingMap = panning && (Input.GetMouseButton(1) || Input.GetMouseButton(2));
        Hover hover = overUI || movingMap ? default : FindHover(mouseWorld, free);
        ShowCursor(hover);

        if (moveType.HasValue)
        {
            if (Input.GetMouseButtonDown(0) && !overUI) FinishMove(hover.kind == HoverKind.None ? mouseWorld : hover.point);
            else if (Input.GetMouseButtonUp(1) && !panning && !overUI) CancelMove();
            return;
        }

        // F over the last control: it becomes the finish
        if (!typing && hover.kind == HoverKind.Mark && IsLastControl(hover.mark) && Input.GetKeyDown(KeyCode.F))
        {
            MakeFinish(hover.mark.item);
            return;
        }

        // Right click (without dragging) on a mark removes it
        if (Input.GetMouseButtonUp(1) && !panning && !overUI)
        {
            Mark hit = Hit(marks, mouseWorld);
            if (hit != null) RemoveMark(hit);
        }

        // Left click: drag a mark that is already there, use a faded fork, add a control on a line, or place a new mark
        if (Input.GetMouseButtonDown(0) && !overUI)
        {
            switch (hover.kind)
            {
                case HoverKind.Mark:
                    if (hover.mark.type == MarkType.BranchControl && ForkLocked(hover.mark.fork))
                        SetStatus(LockedText(hover.mark.fork) + " Right-click one of its controls to take it off this leg.", true);
                    else BeginDrag(hover.mark, mouseWorld, true);
                    break;
                case HoverKind.Fork: UseFork(hover.fork); break;
                case HoverKind.Insert: InsertOnLine(hover, mouseWorld); break;
                case HoverKind.None: break;
                default: Place(hover.point); break;
            }
        }
    }

    // ---- The mark at the mouse ----

    static bool Same(Vector3 a, Vector3 b) => Vector2.Distance(a, b) < 0.01f;   // within a metre: the same control

    Hover FindHover(Vector3 mouseWorld, bool free)
    {
        Hover hover = new Hover { kind = HoverKind.Free, point = mouseWorld, fork = -1 };
        bool laying = !moveType.HasValue && (tool == Tool.Control || tool == Tool.Branch);
        if (!free)
        {
            Mark own = moveType.HasValue ? null : Hit(marks, mouseWorld);
            if (own != null)
            {
                hover.kind = HoverKind.Mark;
                hover.mark = own;
                hover.point = own.position;
                return hover;
            }
            Mark shadow = relay ? Hit(shadows, mouseWorld, 1.3f) : null;
            if (shadow != null && shadow.fork >= 0)
            {
                // A fork's own control: with a control at the mouse, the whole fork clicks in
                if (!moveType.HasValue && tool == Tool.Control)
                {
                    hover.kind = HoverKind.Fork;
                    hover.mark = shadow;
                    hover.fork = shadow.fork;
                    hover.point = shadow.position;
                    return hover;
                }
                shadow = HitShared(mouseWorld, 1.3f);
            }
            if (shadow != null)
            {
                hover.kind = HoverKind.Shared;
                hover.mark = shadow;
                hover.point = shadow.position;
            }
            else if (laying && FindLine(mouseWorld, out InsertSpot spot, out Vector3 onLine))
            {
                hover.kind = HoverKind.Insert;
                hover.spot = spot;
                hover.point = onLine;
            }
        }
        hover.point.z = -1f;
        hover.problem = CheckPlace(hover.point);
        return hover;
    }

    // The faded plain control (not a fork's own) of another leg under the mouse
    Mark HitShared(Vector3 world, float reach)
    {
        return Hit(shadows.FindAll(s => s.fork < 0), world, reach);
    }

    // The line of this leg under the mouse, away from the marks at its ends
    bool FindLine(Vector3 world, out InsertSpot spot, out Vector3 onLine)
    {
        spot = default;
        onLine = world;
        float best = Mathf.Max(courseRenderer.ControlRadiusWorld * 0.35f, mapCamera.orthographicSize * 0.012f);
        float clear = courseRenderer.ControlRadiusWorld * 1.7f;
        bool found = false;
        foreach (var line in ownLines)
        {
            // A fork laid on another leg can't get controls here
            if (line.spot.fork >= 0 && ForkLocked(line.spot.fork)) continue;
            Vector2 a = line.from, b = line.to;
            float length = Vector2.Distance(a, b);
            if (length < clear * 2f) continue;
            Vector2 direction = (b - a) / length;
            float along = Vector2.Dot((Vector2)world - a, direction);
            if (along < clear || along > length - clear) continue;
            Vector2 nearest = a + direction * along;
            float distance = Vector2.Distance(nearest, world);
            if (distance >= best) continue;
            best = distance;
            spot = line.spot;
            onLine = new Vector3(nearest.x, nearest.y, -1f);
            found = true;
        }
        return found;
    }

    static readonly Color GhostPurple = new Color(0.50f, 0.04f, 0.58f, 0.8f);
    static readonly Color RubberPurple = new Color(0.50f, 0.04f, 0.58f, 0.4f);
    static readonly Color FaintPurple = new Color(0.66f, 0.13f, 0.75f, 0.3f);

    // Draws what the next click does: the mark at the mouse with the line it will get, a ring round the mark that can
    // be dragged, the fork that clicks in. A small label next to the mouse says it in words.
    void ShowCursor(Hover hover)
    {
        if (cursorRenderer == null) return;
        string tip = "";
        bool tipIsError = false;
        List<CourseRenderer.Mark> drawn = new List<CourseRenderer.Mark>();
        List<CourseRenderer.Line> lines = new List<CourseRenderer.Line>();
        Color accent = CourseUI.Accent;
        EditCourse course = Cur;

        if (hover.kind == HoverKind.Mark && hover.mark.type == MarkType.BranchControl && ForkLocked(hover.mark.fork))
        {
            drawn.Add(Ring(hover.mark, CourseRenderer.ShadowPurple));
            tip = MarkName(hover.mark) + ": " + LockedText(hover.mark.fork) + "\nRight-click to take the fork off this leg (it stays faded, click it to put it back)";
        }
        else if (hover.kind == HoverKind.Mark)
        {
            drawn.Add(Ring(hover.mark, accent));
            string also = relay && hover.mark.type != MarkType.BranchControl ? LegsText(LinksAt(hover.mark.position)) : "";
            tip = MarkName(hover.mark) + ": drag to move, right-click to remove"
                + (IsLastControl(hover.mark) ? "\nPress F to make it the finish" : "") + (also.Length > 0 ? "\nSame control on " + also + ": hold Alt while dragging to move it there too" : "");
        }
        else if (hover.kind == HoverKind.Fork)
        {
            if (FindForkSource(hover.fork, out Vector3 entry, out Vector3 exit, out bool entryIsStart, out bool exitIsFinish, out int sourceLeg))
            {
                AddForkDrawing(hover.fork, accent, entry, exit, entryIsStart, exitIsFinish, true, drawn, lines);
                tip = "Fork " + (hover.fork + 1) + " from leg " + (sourceLeg + 1) + ": click to run it on leg " + (selectedLeg + 1) + " too";
            }
        }
        else if (hover.kind != HoverKind.None)
        {
            bool snapped = hover.kind == HoverKind.Shared || hover.kind == HoverKind.Insert;
            Color color = hover.problem != null ? CourseRenderer.ErrorRed : snapped ? accent : GhostPurple;
            Color rubber = hover.problem != null ? new Color(0.86f, 0.12f, 0.12f, 0.4f) : snapped ? new Color(accent.r, accent.g, accent.b, 0.55f) : RubberPurple;
            CourseRenderer.MarkKind kind = CourseRenderer.MarkKind.Control;
            string label = "";
            List<(Vector3 point, CourseRenderer.MarkKind kind)> from = null;
            (Vector3 point, CourseRenderer.MarkKind kind)? to = null;
            if (moveType.HasValue)
            {
                kind = moveType == MarkType.Start ? CourseRenderer.MarkKind.Start : moveType == MarkType.Finish ? CourseRenderer.MarkKind.Finish : CourseRenderer.MarkKind.Control;
            }
            else if (hover.kind == HoverKind.Insert)
            {
                label = "+";
            }
            else if (tool == Tool.Start)
            {
                kind = CourseRenderer.MarkKind.Start;
            }
            else if (tool == Tool.Finish)
            {
                kind = CourseRenderer.MarkKind.Finish;
                if (!course.finish.HasValue) from = courseTail;
            }
            else if (tool == Tool.Branch)
            {
                label = branchNextLabel;
                from = branchTail;
                to = branchExit;
            }
            else if (InsertAfter().HasValue)
            {
                // Goes in after the picked control, before the one after it
                int after = InsertAfter().Value;
                label = (after + 2).ToString();
                Vector3 before = after < 0 ? course.start.Value : course.items[after].point;
                from = new List<(Vector3 point, CourseRenderer.MarkKind kind)> { (before, after < 0 ? CourseRenderer.MarkKind.Start : CourseRenderer.MarkKind.Control) };
                EditItem next = course.items[after + 1];
                if (next.fork < 0) to = (next.point, CourseRenderer.MarkKind.Control);
                Mark picked = InsertAfterMark();
                if (picked != null) drawn.Add(Ring(picked, CourseUI.Accent));
            }
            else
            {
                label = (course.items.Count + 1).ToString();
                from = courseTail;
                if (course.finish.HasValue) to = (course.finish.Value, CourseRenderer.MarkKind.Finish);
            }
            Vector3 toward = Vector3.up;
            if (kind == CourseRenderer.MarkKind.Start)
            {
                Mark first = marks.Find(m => m.type != MarkType.Start);
                if (first != null && !Same(first.position, hover.point)) toward = first.position - hover.point;
            }
            drawn.Add(new CourseRenderer.Mark { position = hover.point, kind = kind, color = color, label = label, toward = toward });
            if (hover.kind == HoverKind.Shared)
            {
                drawn.Add(new CourseRenderer.Mark { position = hover.point, kind = CourseRenderer.MarkKind.Control, color = color, label = "", grow = kind == CourseRenderer.MarkKind.Start ? 1.9f : 1.45f, thick = 0.7f });
            }
            if (from != null)
            {
                foreach (var tail in from) lines.Add(new CourseRenderer.Line { from = tail.point, to = hover.point, fromKind = tail.kind, toKind = kind, color = rubber });
            }
            if (to.HasValue) lines.Add(new CourseRenderer.Line { from = hover.point, to = to.Value.point, fromKind = kind, toKind = to.Value.kind, color = rubber });

            if (hover.problem != null)
            {
                tip = hover.problem;
                tipIsError = true;
            }
            else if (hover.kind == HoverKind.Shared)
            {
                string legsHere = LegsText(LinksAt(hover.point));
                if (legsHere.Length == 0 && IsGhost(hover.point)) legsHere = "the one you removed from this leg";
                tip = "Clicks into place: the same " + (hover.mark.kind == CourseRenderer.MarkKind.Start ? "start" : hover.mark.kind == CourseRenderer.MarkKind.Finish ? "finish" : "control")
                    + " as " + (legsHere.Length > 0 ? legsHere : "another leg");
            }
            else if (hover.kind == HoverKind.Insert)
            {
                tip = "Click to add a control here, in between";
            }
            else if (InsertAfter().HasValue && !moveType.HasValue && tool == Tool.Control)
            {
                tip = "Goes in after " + (InsertAfterMark() != null ? MarkName(InsertAfterMark()) : "the picked control");
            }
        }
        if (hover.kind == HoverKind.Mark && tool == Tool.Control && (hover.mark.type == MarkType.Start || (hover.mark.type == MarkType.Control && hover.mark.fork < 0)))
        {
            tip += "\nClick to put the next controls in after it";
        }
        DrawCursor(drawn, lines, tip, tipIsError, hover.kind + " " + hover.point.x + " " + hover.point.y + " " + hover.fork + " " + tool + " " + moveType + " " + insertAfter);
    }

    void DrawCursor(List<CourseRenderer.Mark> drawn, List<CourseRenderer.Line> lines, string tip, bool tipIsError, string what)
    {
        // Drawn again only when something changed
        string key = what + "|" + tip + "|" + drawnVersion;
        if (key != cursorKey)
        {
            cursorKey = key;
            cursorRenderer.DrawMarks(drawn, lines);
            if (cursorTip != null)
            {
                cursorTip.gameObject.SetActive(tip.Length > 0);
                cursorTipText.text = tip;
                cursorTipText.color = tipIsError ? CourseUI.Danger : CourseUI.Ink;
            }
        }
        if (cursorTip != null && cursorTip.gameObject.activeSelf)
        {
            // Next to the mouse, on the left of it when it would run into the panel or off the screen
            float uiScale = Mathf.Min(Screen.width / ScreenSettings.ReferenceResolution.x, Screen.height / ScreenSettings.ReferenceResolution.y);
            bool left = Input.mousePosition.x > Screen.width - (RightRoom + 470f) * uiScale;
            cursorTip.pivot = new Vector2(left ? 1f : 0f, 1f);
            cursorTip.position = Input.mousePosition + new Vector3(left ? -26f : 26f, -30f, 0f) * uiScale;
        }
    }

    // A ring round a mark: it can be grabbed, or it is being dragged
    CourseRenderer.Mark Ring(Mark mark, Color color)
    {
        float grow = mark.type == MarkType.Start ? 1.9f : mark.type == MarkType.Finish ? 1.45f : 1.35f;
        return new CourseRenderer.Mark { position = mark.position, kind = CourseRenderer.MarkKind.Control, color = color, label = "", grow = grow, thick = 1.5f };
    }

    // ---- Dragging a mark ----

    // The same control on other legs: starts, finishes and plain controls at this spot
    List<(EditCourse course, int slot)> LinksAt(Vector3 position)
    {
        List<(EditCourse, int)> links = new List<(EditCourse, int)>();
        if (!relay) return links;
        EditCourse current = Cur;
        List<EditCourse> seen = new List<EditCourse>();
        for (int leg = 0; leg < legs && leg < legCourse.Count; leg++)
        {
            EditCourse other = courses[legCourse[leg]];
            if (other == current || seen.Contains(other)) continue;
            seen.Add(other);
            if (other.start.HasValue && Same(other.start.Value, position)) links.Add((other, -1));
            if (other.finish.HasValue && Same(other.finish.Value, position)) links.Add((other, -2));
            for (int i = 0; i < other.items.Count; i++)
            {
                if (other.items[i].fork < 0 && Same(other.items[i].point, position)) links.Add((other, i));
            }
        }
        return links;
    }

    // "leg 2" or "legs 2, 3"
    string LegsText(List<(EditCourse course, int slot)> links)
    {
        List<int> numbers = new List<int>();
        foreach (var link in links)
        {
            foreach (int leg in LegsWithCourse(link.course)) if (!numbers.Contains(leg + 1)) numbers.Add(leg + 1);
        }
        if (numbers.Count == 0) return "";
        numbers.Sort();
        return (numbers.Count == 1 ? "leg " : "legs ") + string.Join(", ", numbers);
    }

    static void MoveLinks(List<(EditCourse course, int slot)> links, Vector3 world)
    {
        foreach (var link in links)
        {
            if (link.slot == -1) link.course.start = world;
            else if (link.slot == -2) link.course.finish = world;
            else if (link.slot < link.course.items.Count) link.course.items[link.slot].point = world;
        }
    }

    void BeginDrag(Mark mark, Vector3 mouseWorld, bool pushUndo)
    {
        dragging = mark;
        dragFrom = mark.position;
        dragGrab = mark.position - mouseWorld;
        dragGrab.z = 0f;
        dragMouse = Input.mousePosition;
        dragMoved = false;
        dragUndo = pushUndo;
        if (pushUndo) PushUndo();
        // A control only moves on this leg; with Alt held it moves on every leg that has the same control (2026-11-20,
        // the user: working on leg 2 must never change leg 1 by mistake. Before, it moved everywhere unless Alt was held.)
        bool everywhere = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        dragLinks.Clear();
        if (everywhere && mark.type != MarkType.BranchControl) dragLinks.AddRange(LinksAt(mark.position));
        // The other legs' controls it can click onto (not the ones that move with it)
        dragSnaps.Clear();
        foreach (Mark shadow in shadows)
        {
            if (shadow.fork >= 0 || (dragLinks.Count > 0 && Same(shadow.position, dragFrom))) continue;
            dragSnaps.Add(shadow.position);
        }
    }

    void MoveDragged(Vector3 world)
    {
        world.z = -1f;
        SetMarkPosition(dragging, world);
        MoveLinks(dragLinks, world);
    }

    void EndDrag(bool takeBack)
    {
        if (takeBack)
        {
            MoveDragged(dragFrom);
            if (dragUndo) DropUndo();
        }
        dragging = null;
        dragLinks.Clear();
        dragSnaps.Clear();
        cursorKey = null;
    }

    void UpdateDrag(Vector3 mouseWorld, bool free)
    {
        // Right click while dragging: back where it was
        if (Input.GetMouseButtonDown(1))
        {
            string name = MarkName(dragging);
            bool moved = dragMoved;
            EndDrag(true);
            SetStatus(moved ? name + " was put back." : "", false);
            Redraw();
            return;
        }
        if (!dragMoved && (Input.mousePosition - dragMouse).sqrMagnitude > 16f) dragMoved = true;

        Vector3 target = mouseWorld + dragGrab;
        target.z = -1f;
        bool snapped = false;
        if (dragMoved && !free)
        {
            float best = Mathf.Max(courseRenderer.ControlRadiusWorld, mapCamera.orthographicSize * 0.02f);
            Vector3 raw = target;
            foreach (Vector3 snap in dragSnaps)
            {
                float distance = Vector2.Distance(snap, raw);
                if (distance >= best) continue;
                best = distance;
                target = snap;
                snapped = true;
            }
        }

        if (Input.GetMouseButton(0))
        {
            if (dragMoved && (Vector2)target != (Vector2)dragging.position)
            {
                MoveDragged(target);
                RedrawMap();
            }
            string problem = dragMoved ? CheckPlace(dragging.position) : null;
            string also = LegsText(dragLinks);
            string tip = problem != null ? problem
                : snapped ? "Clicks into place: the same control as " + LegsText(LinksAt(dragging.position))
                : also.Length > 0 ? "Moves on " + also + " too" : "";
            List<CourseRenderer.Mark> drawn = new List<CourseRenderer.Mark> { Ring(dragging, problem != null ? CourseRenderer.ErrorRed : CourseUI.Accent) };
            DrawCursor(drawn, new List<CourseRenderer.Line>(), tip, problem != null, "drag " + dragging.position.x + " " + dragging.position.y + " " + (problem != null));
            return;
        }

        // Dropped: it must be somewhere a runner can stand, otherwise it goes back
        if (!dragMoved)
        {
            Mark clicked = dragging;
            EndDrag(false);
            if (dragUndo) DropUndo();
            if (tool == Tool.Control && (clicked.type == MarkType.Start || (clicked.type == MarkType.Control && clicked.fork < 0)))
                PickInsertAfter(clicked);
            return;
        }
        string markName = MarkName(dragging);
        string where = LegsText(dragLinks);
        string dropProblem = CheckPlace(dragging.position);
        errorPoint = null;
        if (dropProblem != null)
        {
            EndDrag(true);
            SetStatus(markName + " can't go there: " + dropProblem + " It was put back.", true);
        }
        else
        {
            Vector3 dropped = dragging.position;
            bool punched = dragging.type != MarkType.Start;
            EndDrag(false);
            SetStatus(where.Length > 0 ? markName + " was moved on " + where + " too, since it is the same control." : "", false);
            if (punched) AskSide(dropped);
        }
        Redraw();
    }

    // A click on a line of this leg: a new control there, which can be dragged on in the same press
    void InsertOnLine(Hover hover, Vector3 mouseWorld)
    {
        if (hover.problem != null)
        {
            SetStatus(hover.problem, true);
            return;
        }
        EditCourse course = Cur;
        if (ControlCount(course) >= CourseData.MaxControls)
        {
            SetStatus("A course can have at most " + CourseData.MaxControls + " controls.", true);
            return;
        }
        InsertSpot spot = hover.spot;
        PushUndo();
        if (spot.fork >= 0)
        {
            List<Vector3> branch = forks[spot.fork].branches[spot.branch];
            branch.Insert(Mathf.Clamp(spot.index, 0, branch.Count), hover.point);
        }
        else
        {
            course.items.Insert(Mathf.Clamp(spot.item, 0, course.items.Count), new EditItem { point = hover.point });
        }
        errorPoint = null;
        SetStatus("", false);
        Redraw();
        Mark added = marks.Find(m => m.type != MarkType.Start && m.type != MarkType.Finish && Same(m.position, hover.point));
        if (added != null)
        {
            SetStatus("Control " + added.label + " was added. Drag it where it should be.", false);
            BeginDrag(added, mouseWorld, false);
        }
    }

    // ---- Undo ----

    class Snapshot
    {
        public List<EditCourse> courses;
        public List<EditFork> forks;
        public List<int> legCourse;
        public List<LegSex> legSex;
        public List<int> checkLegs;
        public List<List<Vector3>> areas, boundaries, bridges;
        public List<Vector3> openArea;
        public bool relay, forkEveryVariant, openIsLine, openIsBridge;
        public int legs, selectedLeg;
        public string terrain, name;
        public CourseData editing;
    }

    readonly List<Snapshot> undo = new List<Snapshot>();

    // Called before every change to what is laid
    void PushUndo()
    {
        Snapshot snapshot = new Snapshot
        {
            courses = new List<EditCourse>(), forks = new List<EditFork>(), legCourse = new List<int>(legCourse), legSex = new List<LegSex>(legSex),
            checkLegs = new List<int>(checkLegs),
            areas = areas.ConvertAll(a => new List<Vector3>(a)), boundaries = boundaries.ConvertAll(b => new List<Vector3>(b)),
            bridges = bridges.ConvertAll(b => new List<Vector3>(b)),
            openArea = openArea != null ? new List<Vector3>(openArea) : null,
            relay = relay, forkEveryVariant = forkEveryVariant, openIsLine = openIsLine, openIsBridge = openIsBridge, legs = legs, selectedLeg = selectedLeg,
            terrain = terrain, name = nameField != null ? nameField.text : "", editing = editing
        };
        foreach (EditCourse course in courses)
        {
            EditCourse copy = new EditCourse { start = course.start, finish = course.finish, finishTv = course.finishTv };
            foreach (EditItem item in course.items) copy.items.Add(new EditItem { fork = item.fork, point = item.point, tv = item.tv });
            snapshot.courses.Add(copy);
        }
        foreach (EditFork fork in forks)
        {
            EditFork copy = new EditFork { legs = new List<int>(fork.legs), home = fork.home };
            foreach (List<Vector3> branch in fork.branches) copy.branches.Add(new List<Vector3>(branch));
            snapshot.forks.Add(copy);
        }
        undo.Add(snapshot);
        if (undo.Count > 200) undo.RemoveAt(0);
        RefreshUndoButton();
    }

    // The change that was about to be made didn't happen after all
    void DropUndo()
    {
        if (undo.Count > 0) undo.RemoveAt(undo.Count - 1);
        RefreshUndoButton();
    }

    void RefreshUndoButton()
    {
        if (undoButton != null) undoButton.interactable = undo.Count > 0;
    }

    void Undo()
    {
        if (busy) return;
        insertAfter = null;
        if (dragging != null)
        {
            // Ctrl+Z while dragging: the mark just goes back
            EndDrag(true);
            SetStatus("", false);
            Redraw();
            return;
        }
        draggingCorner = false;
        moveType = null;
        if (undo.Count == 0)
        {
            SetStatus("Nothing to undo.", false);
            RefreshUndoButton();
            Redraw();
            return;
        }
        Snapshot snapshot = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        bool wasDrawing = tool == Tool.Branch;
        courses.Clear();
        courses.AddRange(snapshot.courses);
        forks.Clear();
        forks.AddRange(snapshot.forks);
        legCourse.Clear();
        legCourse.AddRange(snapshot.legCourse);
        legSex.Clear();
        if (snapshot.legSex != null) legSex.AddRange(snapshot.legSex);
        checkLegs.Clear();
        if (snapshot.checkLegs != null) checkLegs.AddRange(snapshot.checkLegs);
        areas.Clear();
        areas.AddRange(snapshot.areas);
        boundaries.Clear();
        boundaries.AddRange(snapshot.boundaries);
        bridges.Clear();
        if (snapshot.bridges != null) bridges.AddRange(snapshot.bridges);
        openArea = snapshot.openArea;
        openIsLine = snapshot.openIsLine;
        openIsBridge = snapshot.openIsBridge;
        forkEveryVariant = snapshot.forkEveryVariant;
        legs = snapshot.legs;
        selectedLeg = snapshot.selectedLeg;
        editing = snapshot.editing;
        if (nameField != null) nameField.text = snapshot.name;
        SetTerrain(snapshot.terrain);
        errorPoint = null;
        // The tool that fits what is there now (an open area keeps the out-of-bounds tool; it is never closed here)
        if (openArea != null)
        {
            drawLine = openIsLine;
            drawBridge = openIsBridge;
            tool = Tool.Forbidden;
        }
        Tool fits = tool == Tool.Forbidden ? Tool.Forbidden : Tool.Control;
        relay = snapshot.relay;
        if (fits != Tool.Forbidden && !Cur.start.HasValue) fits = Tool.Start;
        // Undo while drawing a fork's branches takes the last control back and goes on drawing
        bool keepDrawing = wasDrawing && fits == Tool.Control && relay && branchFork >= 0 && branchFork < forks.Count
                           && Cur.items.Exists(item => item.fork == branchFork);
        if (keepDrawing)
        {
            EditFork drawn = forks[branchFork];
            if (forkFirstRound)
            {
                int empty = -1;
                for (int b = 0; b < drawn.branches.Count && empty < 0; b++) if (drawn.branches[b].Count == 0 && !branchesDone.Contains(b)) empty = b;
                if (empty >= 0) branchIndex = empty;
            }
            branchIndex = Mathf.Clamp(branchIndex, 0, Mathf.Max(0, drawn.branches.Count - 1));
            keepDrawing = drawn.branches.Count > 0;
        }
        SetTool(keepDrawing ? Tool.Branch : fits);
        SetRelay(snapshot.relay);
        SetStatus("Undone." + (undo.Count > 0 ? "" : " Nothing more to undo."), false);
        RefreshUndoButton();
        Redraw();
    }

    Mark Hit(List<Mark> list, Vector3 world, float reach = 1.2f)
    {
        float best = Mathf.Max(courseRenderer.ControlRadiusWorld * reach, mapCamera.orthographicSize * 0.02f);
        Mark hit = null;
        foreach (Mark mark in list)
        {
            float distance = Vector2.Distance(mark.position, world);
            if (distance < best)
            {
                best = distance;
                hit = mark;
            }
        }
        return hit;
    }

    // Where a start, control or finish may go: on the map, on ground a runner can reach, not out of bounds
    string CheckPlace(Vector3 world)
    {
        string problem = CourseValidator.CheckPoint(map, grid, map.FromWorld(world));
        if (problem != null) return problem;
        foreach (List<Vector3> area in areas)
        {
            if (ForbiddenAreas.Contains(area, world)) return "That is inside an out-of-bounds area.";
        }
        return null;
    }

    // ---- Out-of-bounds areas and uncrossable boundaries ----
    // Click corner after corner. An area is closed by clicking its first corner again; a boundary line is finished by
    // clicking its last corner again. Enter, or picking another tool, finishes either.
    // Drag a corner to move it; right-click a corner to remove it, right-click inside an area (or on a line) to remove it.
    // The shapes are numbered areas first, then lines, then bridges.

    int ShapeCount => areas.Count + boundaries.Count + bridges.Count;
    List<Vector3> Shape(int i) => i < areas.Count ? areas[i] : i < areas.Count + boundaries.Count ? boundaries[i - areas.Count] : bridges[i - areas.Count - boundaries.Count];
    bool ShapeIsLine(int i) => i >= areas.Count;

    void RemoveShape(int i)
    {
        if (i < areas.Count) areas.RemoveAt(i);
        else if (i < areas.Count + boundaries.Count) boundaries.RemoveAt(i - areas.Count);
        else bridges.RemoveAt(i - areas.Count - boundaries.Count);
    }

    string OpenName => openIsBridge ? "bridge" : openIsLine ? "uncrossable boundary" : "out-of-bounds area";

    void UpdateForbidden(Vector3 mouseWorld, bool overUI, bool typing)
    {
        areaRenderer.SetRubberEnd(openArea != null && !overUI && !draggingCorner ? mouseWorld : (Vector3?)null);

        if (!typing && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            CloseOpenArea();
            Redraw();
            return;
        }
        if (!typing && Input.GetKeyDown(KeyCode.Backspace) && openArea != null)
        {
            PushUndo();
            openArea.RemoveAt(openArea.Count - 1);
            if (openArea.Count == 0) openArea = null;
            Redraw();
            return;
        }

        if (Input.GetMouseButtonDown(0) && !overUI)
        {
            if (HitCorner(mouseWorld, out int a, out int c))
            {
                bool closes = a < 0 && (openIsLine ? c == openArea.Count - 1 && openArea.Count >= 2 : c == 0 && openArea.Count >= 3);
                if (closes)
                {
                    CloseOpenArea();
                    Redraw();
                    return;
                }
                PushUndo();
                dragArea = a;
                dragCorner = c;
                draggingCorner = true;
                return;
            }
            AddCorner(mouseWorld);
            return;
        }
        if (draggingCorner)
        {
            if (Input.GetMouseButton(0))
            {
                List<Vector3> shape = dragArea < 0 ? openArea : dragArea < ShapeCount ? Shape(dragArea) : null;
                if (shape != null && dragCorner < shape.Count) shape[dragCorner] = ClampToMap(mouseWorld);
                RedrawAreas();
            }
            else
            {
                draggingCorner = false;
                Redraw();
            }
            return;
        }

        if (Input.GetMouseButtonUp(1) && !panning && !overUI)
        {
            if (HitCorner(mouseWorld, out int a, out int c))
            {
                PushUndo();
                List<Vector3> shape = a < 0 ? openArea : Shape(a);
                shape.RemoveAt(c);
                if (a < 0 && shape.Count == 0) openArea = null;
                if (a >= 0 && shape.Count < (ShapeIsLine(a) ? 2 : 3)) RemoveShape(a);
                SetStatus("", false);
            }
            else
            {
                float reach = areaRenderer.HandleSize;
                int hit = boundaries.FindLastIndex(line => ForbiddenAreas.DistanceToLine(line, mouseWorld, false) < reach);
                int bridgeHit = bridges.FindLastIndex(line => ForbiddenAreas.DistanceToLine(line, mouseWorld, false) < reach * 2f);
                if (bridgeHit >= 0)
                {
                    PushUndo();
                    bridges.RemoveAt(bridgeHit);
                    SetStatus("The bridge was removed.", false);
                }
                else if (hit >= 0)
                {
                    PushUndo();
                    boundaries.RemoveAt(hit);
                    SetStatus("The uncrossable boundary was removed.", false);
                }
                else
                {
                    int inside = areas.FindLastIndex(area => ForbiddenAreas.Contains(area, mouseWorld));
                    if (inside >= 0)
                    {
                        PushUndo();
                        areas.RemoveAt(inside);
                        SetStatus("The out-of-bounds area was removed.", false);
                    }
                }
            }
            Redraw();
        }
    }

    void AddCorner(Vector3 world)
    {
        CoursePoint point = map.FromWorld(world);
        if (!map.Contains(point))
        {
            SetStatus("That is outside the map.", true);
            return;
        }
        if (openArea == null)
        {
            // No limit on how many (2026-11-19); only on the corners of them all together, below
        }
        if (openArea != null && openArea.Count >= ForbiddenArea.MaxCorners)
        {
            SetStatus((openIsBridge ? "A bridge" : openIsLine ? "An uncrossable boundary" : "An out-of-bounds area") + " can have at most " + ForbiddenArea.MaxCorners + " corners. "
                + (openIsLine ? "Click the last corner again to finish it." : "Click the first corner to close it."), true);
            return;
        }
        if (CornerCount() >= ForbiddenArea.MaxCornersTotal)
        {
            SetStatus("All out-of-bounds areas, boundaries and bridges together can have at most " + ForbiddenArea.MaxCornersTotal + " corners.", true);
            return;
        }
        world.z = -1f;
        PushUndo();
        if (openArea == null)
        {
            openArea = new List<Vector3>();
            openIsLine = drawLine || drawBridge;
            openIsBridge = drawBridge;
        }
        openArea.Add(world);
        SetStatus("", false);
        Redraw();
    }

    // The open area or line becomes a finished one if it has enough corners (three for an area, two for a line)
    void CloseOpenArea()
    {
        if (openArea == null) return;
        PushUndo();
        if (openIsBridge)
        {
            if (openArea.Count >= 2)
            {
                bridges.Add(openArea);
                SetStatus("Bridge added. The runners can cross water, a fence or anything else on it.", false);
            }
            else
            {
                SetStatus("A bridge needs at least two corners.", true);
            }
        }
        else if (openIsLine)
        {
            if (openArea.Count >= 2)
            {
                boundaries.Add(openArea);
                SetStatus("Uncrossable boundary added. The runners can not cross it.", false);
            }
            else
            {
                SetStatus("An uncrossable boundary needs at least two corners.", true);
            }
        }
        else if (openArea.Count >= 3)
        {
            areas.Add(openArea);
            string inside = MarksInside(openArea);
            SetStatus(inside != null ? inside + " is inside the out-of-bounds area. Move it out before saving." : "Out-of-bounds area added.", inside != null);
        }
        else
        {
            SetStatus("An out-of-bounds area needs at least three corners.", true);
        }
        openArea = null;
    }

    // The first start, control or finish inside an area, by name, or null
    string MarksInside(List<Vector3> area)
    {
        BuildMarks(out _, out _);
        foreach (Mark mark in marks)
        {
            if (ForbiddenAreas.Contains(area, mark.position)) return MarkName(mark);
        }
        return null;
    }

    int CornerCount()
    {
        int count = openArea != null ? openArea.Count : 0;
        foreach (List<Vector3> area in areas) count += area.Count;
        foreach (List<Vector3> line in boundaries) count += line.Count;
        foreach (List<Vector3> line in bridges) count += line.Count;
        return count;
    }

    Vector3 ClampToMap(Vector3 world)
    {
        CoursePoint point = map.FromWorld(world);
        point.e = Math.Max(map.minx, Math.Min(map.maxx, point.e));
        point.n = Math.Max(map.miny, Math.Min(map.maxy, point.n));
        return map.ToWorld(point);
    }

    // The corner under the mouse: area -1 is the open shape, otherwise a shape number (areas, then lines)
    bool HitCorner(Vector3 world, out int area, out int corner)
    {
        float best = areaRenderer.HandleSize * 1.2f;
        area = -2;
        corner = -1;
        if (openArea != null)
        {
            for (int c = 0; c < openArea.Count; c++)
            {
                float d = Vector2.Distance(openArea[c], world);
                // The corner that finishes the shape is easier to hit
                bool finishing = openIsLine ? c == openArea.Count - 1 && openArea.Count >= 2 : c == 0 && openArea.Count >= 3;
                float reach = finishing ? best * 1.6f : best;
                if (d < reach) { best = d; area = -1; corner = c; }
            }
        }
        for (int a = 0; a < ShapeCount; a++)
        {
            List<Vector3> shape = Shape(a);
            for (int c = 0; c < shape.Count; c++)
            {
                float d = Vector2.Distance(shape[c], world);
                if (d < best) { best = d; area = a; corner = c; }
            }
        }
        return corner >= 0;
    }

    void RedrawAreas()
    {
        if (areaRenderer == null) return;
        int bad = errorPoint.HasValue ? areas.FindIndex(area => ForbiddenAreas.Contains(area, errorPoint.Value)) : -1;
        areaRenderer.DrawEditing(areas, boundaries, bridges, openArea, openIsLine, tool == Tool.Forbidden, bad);
    }

    // ---- Moving a start, control or finish with the Move button in the list ----

    void StartMove(MarkType type, int item, string name)
    {
        if (tool == Tool.Forbidden || tool == Tool.Branch) SetTool(Tool.Control);
        moveType = type;
        moveItem = item;
        moveName = name;
        SetStatus("Click on the map where " + name.ToLower() + " should go. Right-click to cancel.", false);
        RefreshHint();
        RefreshPointList();
    }

    void CancelMove()
    {
        if (!moveType.HasValue) return;
        moveType = null;
        SetStatus("", false);
        RefreshHint();
        RefreshPointList();
    }

    void FinishMove(Vector3 world)
    {
        string problem = CheckPlace(world);
        if (problem != null)
        {
            SetStatus(moveName + ": " + problem, true);
            return;
        }
        world.z = -1f;
        EditCourse course = Cur;
        bool gone = moveType == MarkType.Start ? !course.start.HasValue
            : moveType == MarkType.Finish ? !course.finish.HasValue
            : moveItem < 0 || moveItem >= course.items.Count || course.items[moveItem].fork >= 0;
        string also = "";
        if (!gone)
        {
            Vector3 from = moveType == MarkType.Start ? course.start.Value : moveType == MarkType.Finish ? course.finish.Value : course.items[moveItem].point;
            // Only on this leg, unless Alt is held (2026-11-20)
            bool everywhere = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            List<(EditCourse course, int slot)> links = everywhere ? LinksAt(from) : new List<(EditCourse, int)>();
            PushUndo();
            SetMarkPosition(new Mark { type = moveType.Value, item = moveItem }, world);
            MoveLinks(links, world);
            also = LegsText(links);
        }
        bool punched = moveType != MarkType.Start;
        moveType = null;
        errorPoint = null;
        SetStatus(gone ? "" : moveName + " was moved" + (also.Length > 0 ? " on " + also + " too, since it is the same control." : "."), false);
        Redraw();
        if (!gone && punched) AskSide(world);
    }

    void Place(Vector3 world)
    {
        string problem = CheckPlace(world);
        if (problem != null)
        {
            SetStatus(problem, true);
            return;
        }
        SetStatus("", false);
        errorPoint = null;
        world.z = -1f;
        EditCourse course = Cur;
        if ((tool == Tool.Control || tool == Tool.Branch) && ControlCount(course) >= CourseData.MaxControls)
        {
            SetStatus("A course can have at most " + CourseData.MaxControls + " controls.", true);
            return;
        }
        if (tool == Tool.Branch && (branchFork < 0 || branchFork >= forks.Count || branchIndex >= forks[branchFork].branches.Count))
        {
            SetTool(Tool.Control);
            return;
        }
        PushUndo();
        DropGhost(world);
        Tool placed = tool;
        switch (tool)
        {
            case Tool.Start:
                course.start = world;
                SetTool(Tool.Control);
                break;
            case Tool.Control:
            {
                int? pickedAfter = InsertAfter();
                if (pickedAfter.HasValue)
                {
                    // In the middle of the course, after the picked control; the next one goes after this one
                    course.items.Insert(pickedAfter.Value + 1, new EditItem { point = world });
                    insertAfter = pickedAfter.Value + 1;
                }
                else course.items.Add(new EditItem { point = world });
                break;
            }
            case Tool.Branch:
                forks[branchFork].branches[branchIndex].Add(world);
                // First round: the next click is the first control of the next branch
                if (forkFirstRound) MoveToNextBranch();
                break;
            case Tool.Finish:
                course.finish = world;
                break;
        }
        Redraw();
        if (placed == Tool.Control || placed == Tool.Branch || placed == Tool.Finish) AskSide(world);
    }

    int ControlCount(EditCourse course)
    {
        int count = 0;
        foreach (EditItem item in course.items)
        {
            if (item.fork < 0) count++;
            else foreach (List<Vector3> branch in forks[item.fork].branches) count += branch.Count;
        }
        return count;
    }

    // ---- Marks ----

    void SetMarkPosition(Mark mark, Vector3 world)
    {
        mark.position = world;
        EditCourse course = Cur;
        switch (mark.type)
        {
            case MarkType.Start: course.start = world; break;
            case MarkType.Finish: course.finish = world; break;
            case MarkType.Control: course.items[mark.item].point = world; break;
            case MarkType.BranchControl: forks[mark.fork].branches[mark.branch][mark.index] = world; break;
        }
    }

    // The last control of the course (not on a fork): it can be turned into the finish (2026-11-16: before, it had to be
    // removed and a finish put in its place)
    bool IsLastControl(Mark mark)
    {
        EditCourse course = Cur;
        return mark != null && mark.type == MarkType.Control && mark.item >= 0 && mark.item == course.items.Count - 1
            && course.items[mark.item].fork < 0;
    }

    void MakeFinish(int item)
    {
        EditCourse course = Cur;
        if (item < 0 || item >= course.items.Count || course.items[item].fork >= 0) return;
        PushUndo();
        insertAfter = null;
        cursorKey = null;
        bool hadFinish = course.finish.HasValue;
        course.finish = course.items[item].point;
        course.items.RemoveAt(item);
        errorPoint = null;
        SetStatus(hadFinish ? "The last control is the finish now (the old finish was removed)." : "The last control is the finish now.", false);
        Redraw();
    }

    void RemoveMark(Mark mark)
    {
        if (mark.type == MarkType.BranchControl && ForkLocked(mark.fork))
        {
            TakeForkOff(mark.fork);
            return;
        }
        PushUndo();
        insertAfter = null;
        EditCourse course = Cur;
        if (mark.type != MarkType.Start && mark.type != MarkType.Finish) KeepGhost(course, mark.position);
        switch (mark.type)
        {
            case MarkType.Start: course.start = null; break;
            case MarkType.Finish: course.finish = null; break;
            case MarkType.Control: course.items.RemoveAt(mark.item); break;
            case MarkType.BranchControl: forks[mark.fork].branches[mark.branch].RemoveAt(mark.index); break;
        }
        if (tool == Tool.Branch && mark.type != MarkType.BranchControl) SetTool(Tool.Control);
        errorPoint = null;
        SetStatus("", false);
        Redraw();
    }

    string MarkName(Mark mark)
    {
        switch (mark.type)
        {
            case MarkType.Start: return "Start";
            case MarkType.Finish: return "Finish";
            default: return "Control " + mark.label;
        }
    }

    // Numbers the course the way it is shown: common controls 1, 2, 3...; a fork takes one number, and its branch
    // controls are that number with the branch letter (3A, 3B, and 3A2 for the second control on branch A)
    void BuildMarks(out List<CourseRenderer.Mark> drawn, out List<CourseRenderer.Line> lines)
    {
        marks.Clear();
        shadows.Clear();
        ownLines.Clear();
        courseTail.Clear();
        branchTail.Clear();
        branchExit = null;
        branchNextLabel = "";
        drawn = new List<CourseRenderer.Mark>();
        lines = new List<CourseRenderer.Line>();
        List<CourseRenderer.Line> made = lines;
        EditCourse course = Cur;
        Color purple = CourseRenderer.CoursePurple;
        bool laysBranch = tool == Tool.Branch && branchFork >= 0 && branchFork < forks.Count && branchIndex >= 0 && branchIndex < forks[branchFork].branches.Count;

        // A line of this leg, and where a control goes that is added by clicking on it
        void AddLine(Vector3 from, CourseRenderer.MarkKind fromKind, Vector3 to, CourseRenderer.MarkKind toKind, InsertSpot spot)
        {
            made.Add(new CourseRenderer.Line { from = from, to = to, fromKind = fromKind, toKind = toKind, color = purple });
            ownLines.Add((from, to, spot));
        }

        // The ends the next mark is joined to. branch: the fork branch an end belongs to (a control clicked in on the
        // line after it goes last on that branch), or -1 for a common control
        List<(Vector3 point, CourseRenderer.MarkKind kind, int fork, int branch)> previous = new List<(Vector3, CourseRenderer.MarkKind, int, int)>();
        if (course.start.HasValue)
        {
            marks.Add(new Mark { type = MarkType.Start, position = course.start.Value, label = "Start" });
            previous.Add((course.start.Value, CourseRenderer.MarkKind.Start, -1, -1));
        }
        InsertSpot After(int item, int fork, int branch)
        {
            return fork >= 0
                ? new InsertSpot { item = -1, fork = fork, branch = branch, index = forks[fork].branches[branch].Count }
                : new InsertSpot { item = item, fork = -1 };
        }
        int number = 0;
        for (int i = 0; i < course.items.Count; i++)
        {
            EditItem item = course.items[i];
            number++;
            if (item.fork < 0)
            {
                marks.Add(new Mark { type = MarkType.Control, item = i, position = item.point, label = number.ToString() });
                foreach (var from in previous) AddLine(from.point, from.kind, item.point, CourseRenderer.MarkKind.Control, After(i, from.fork, from.branch));
                previous = new List<(Vector3, CourseRenderer.MarkKind, int, int)> { (item.point, CourseRenderer.MarkKind.Control, -1, -1) };
                continue;
            }
            EditFork fork = forks[item.fork];
            var ends = new List<(Vector3 point, CourseRenderer.MarkKind kind, int fork, int branch)>();
            for (int b = 0; b < fork.branches.Count; b++)
            {
                var branchPrevious = previous;
                for (int k = 0; k < fork.branches[b].Count; k++)
                {
                    Vector3 point = fork.branches[b][k];
                    string label = number + CourseData.BranchLetter(b) + (k > 0 ? (k + 1).ToString() : "");
                    marks.Add(new Mark { type = MarkType.BranchControl, item = i, fork = item.fork, branch = b, index = k, position = point, label = label });
                    foreach (var from in branchPrevious) AddLine(from.point, from.kind, point, CourseRenderer.MarkKind.Control, new InsertSpot { item = -1, fork = item.fork, branch = b, index = k });
                    branchPrevious = new List<(Vector3, CourseRenderer.MarkKind, int, int)> { (point, CourseRenderer.MarkKind.Control, item.fork, b) };
                }
                if (laysBranch && item.fork == branchFork && b == branchIndex)
                {
                    // Where the branch being laid goes on from, and the common control it comes back to
                    foreach (var end in branchPrevious) branchTail.Add((end.point, end.kind));
                    int count = fork.branches[b].Count;
                    branchNextLabel = number + CourseData.BranchLetter(b) + (count > 0 ? (count + 1).ToString() : "");
                    if (i + 1 < course.items.Count && course.items[i + 1].fork < 0) branchExit = (course.items[i + 1].point, CourseRenderer.MarkKind.Control);
                    else if (i + 1 >= course.items.Count && course.finish.HasValue) branchExit = (course.finish.Value, CourseRenderer.MarkKind.Finish);
                }
                foreach (var end in branchPrevious)
                {
                    // A branch without controls goes straight on from the common control before the fork
                    var tagged = (end.point, end.kind, item.fork, b);
                    if (!ends.Exists(e => e.point == end.point)) ends.Add(tagged);
                }
            }
            previous = ends;
        }
        foreach (var end in previous) courseTail.Add((end.point, end.kind));
        if (course.finish.HasValue)
        {
            marks.Add(new Mark { type = MarkType.Finish, position = course.finish.Value, label = "Finish" });
            foreach (var from in previous) AddLine(from.point, from.kind, course.finish.Value, CourseRenderer.MarkKind.Finish, After(course.items.Count, from.fork, from.branch));
        }

        Mark bad = null;
        if (errorPoint.HasValue)
        {
            float best = 0.08f;
            foreach (Mark mark in marks)
            {
                float d = Vector2.Distance(mark.position, errorPoint.Value);
                if (d < best) { best = d; bad = mark; }
            }
        }
        Vector3 firstAfterStart = Vector3.zero;
        foreach (Mark mark in marks)
        {
            if (mark.type != MarkType.Start && firstAfterStart == Vector3.zero) firstAfterStart = mark.position;
        }
        foreach (Mark mark in marks)
        {
            Color color = mark == bad ? CourseRenderer.ErrorRed : purple;
            CourseRenderer.MarkKind kind = mark.type == MarkType.Start ? CourseRenderer.MarkKind.Start : mark.type == MarkType.Finish ? CourseRenderer.MarkKind.Finish : CourseRenderer.MarkKind.Control;
            drawn.Add(new CourseRenderer.Mark
            {
                position = mark.position, kind = kind, color = color,
                label = mark.type == MarkType.Start || mark.type == MarkType.Finish ? "" : mark.label,
                toward = mark.type == MarkType.Start && firstAfterStart != Vector3.zero ? firstAfterStart - mark.position : Vector3.up
            });
        }

        if (relay) AddOtherLegs(drawn, lines);
    }

    // The other legs, faded: their forks that this leg doesn't have yet (with lines and names), the common controls
    // around those forks, and (when shown) the rest of their starts, controls and finishes. The mark at the mouse
    // clicks into place on them: a fork's own control puts the whole fork on this leg, any other becomes the same control.
    void AddOtherLegs(List<CourseRenderer.Mark> drawn, List<CourseRenderer.Line> lines)
    {
        Color faded = CourseRenderer.ShadowPurple;
        EditCourse course = Cur;
        for (int f = 0; f < forks.Count; f++)
        {
            if (course.items.Exists(item => item.fork == f)) continue;
            if (!FindForkSource(f, out Vector3 entry, out Vector3 exit, out bool entryIsStart, out bool exitIsFinish, out int sourceLeg)) continue;
            AddForkDrawing(f, faded, entry, exit, entryIsStart, exitIsFinish, false, drawn, lines);
            EditFork fork = forks[f];
            for (int b = 0; b < fork.branches.Count; b++)
            {
                for (int k = 0; k < fork.branches[b].Count; k++)
                {
                    shadows.Add(new Mark { type = MarkType.Shadow, fork = f, branch = b, index = k, position = fork.branches[b][k], kind = CourseRenderer.MarkKind.Control, label = "" });
                }
            }
            AddSharedShadow(entry, entryIsStart ? CourseRenderer.MarkKind.Start : CourseRenderer.MarkKind.Control, faded, drawn);
            AddSharedShadow(exit, exitIsFinish ? CourseRenderer.MarkKind.Finish : CourseRenderer.MarkKind.Control, faded, drawn);
        }
        // Controls removed from this leg, to click back in place
        if (removedGhosts.TryGetValue(course, out List<Vector3> ghosts))
        {
            foreach (Vector3 ghost in ghosts)
            {
                if (marks.Exists(m => Same(m.position, ghost))) continue;
                AddSharedShadow(ghost, CourseRenderer.MarkKind.Control, FaintPurple, drawn);
            }
        }
        if (!showOtherLegs) return;
        List<EditCourse> seen = new List<EditCourse> { course };
        for (int leg = 0; leg < legs && leg < legCourse.Count; leg++)
        {
            EditCourse other = courses[legCourse[leg]];
            if (seen.Contains(other)) continue;
            seen.Add(other);
            if (other.start.HasValue) AddSharedShadow(other.start.Value, CourseRenderer.MarkKind.Start, FaintPurple, drawn);
            foreach (EditItem item in other.items)
            {
                if (item.fork < 0) AddSharedShadow(item.point, CourseRenderer.MarkKind.Control, FaintPurple, drawn);
            }
            if (other.finish.HasValue) AddSharedShadow(other.finish.Value, CourseRenderer.MarkKind.Finish, FaintPurple, drawn);
        }
    }

    // A start, plain control or finish of another leg. Under one of this leg's own marks it is not drawn (it is the
    // same control already), but it is still there to click onto.
    void AddSharedShadow(Vector3 position, CourseRenderer.MarkKind kind, Color color, List<CourseRenderer.Mark> drawn)
    {
        if (shadows.Exists(s => s.fork < 0 && Same(s.position, position))) return;
        shadows.Add(new Mark { type = MarkType.Shadow, fork = -1, position = position, kind = kind, label = "" });
        if (marks.Exists(m => Same(m.position, position))) return;
        drawn.Add(new CourseRenderer.Mark { position = position, kind = kind, color = color, label = "", toward = Vector3.up });
    }

    // A fork as it lies on another leg: its controls with their names and the lines through every branch, and (ends)
    // the common marks before and after it
    void AddForkDrawing(int f, Color color, Vector3 entry, Vector3 exit, bool entryIsStart, bool exitIsFinish, bool ends,
                        List<CourseRenderer.Mark> drawn, List<CourseRenderer.Line> lines)
    {
        EditFork fork = forks[f];
        CourseRenderer.MarkKind entryKind = entryIsStart ? CourseRenderer.MarkKind.Start : CourseRenderer.MarkKind.Control;
        CourseRenderer.MarkKind exitKind = exitIsFinish ? CourseRenderer.MarkKind.Finish : CourseRenderer.MarkKind.Control;
        if (ends)
        {
            drawn.Add(new CourseRenderer.Mark { position = entry, kind = entryKind, color = color, label = "", toward = Vector3.up });
            drawn.Add(new CourseRenderer.Mark { position = exit, kind = exitKind, color = color, label = "" });
        }
        for (int b = 0; b < fork.branches.Count; b++)
        {
            Vector3 from = entry;
            CourseRenderer.MarkKind fromKind = entryKind;
            for (int k = 0; k < fork.branches[b].Count; k++)
            {
                Vector3 point = fork.branches[b][k];
                string label = "F" + (f + 1) + CourseData.BranchLetter(b) + (k > 0 ? (k + 1).ToString() : "");
                drawn.Add(new CourseRenderer.Mark { position = point, kind = CourseRenderer.MarkKind.Control, color = color, label = label });
                lines.Add(new CourseRenderer.Line { from = from, to = point, fromKind = fromKind, toKind = CourseRenderer.MarkKind.Control, color = color });
                from = point;
                fromKind = CourseRenderer.MarkKind.Control;
            }
            lines.Add(new CourseRenderer.Line { from = from, to = exit, fromKind = fromKind, toKind = exitKind, color = color });
        }
    }

    // Where a fork sits on the first leg that has it: the common point before it and after it
    bool FindForkSource(int f, out Vector3 entry, out Vector3 exit, out bool entryIsStart, out bool exitIsFinish, out int sourceLeg)
    {
        entry = exit = Vector3.zero;
        entryIsStart = exitIsFinish = false;
        sourceLeg = -1;
        for (int leg = 0; leg < legs && leg < legCourse.Count; leg++)
        {
            EditCourse course = courses[legCourse[leg]];
            int i = course.items.FindIndex(item => item.fork == f);
            if (i < 0) continue;
            Vector3? before = i == 0 ? course.start : course.items[i - 1].fork < 0 ? course.items[i - 1].point : (Vector3?)null;
            Vector3? after = i == course.items.Count - 1 ? course.finish : course.items[i + 1].fork < 0 ? course.items[i + 1].point : (Vector3?)null;
            if (!before.HasValue || !after.HasValue) continue;
            entry = before.Value;
            exit = after.Value;
            entryIsStart = i == 0;
            exitIsFinish = i == course.items.Count - 1;
            sourceLeg = leg;
            return true;
        }
        return false;
    }

    // Only the marks on the map (while a mark is dragged; the lists in the panels wait until it is dropped)
    void RedrawMap()
    {
        BuildMarks(out List<CourseRenderer.Mark> drawn, out List<CourseRenderer.Line> lines);
        courseRenderer.DrawMarks(drawn, lines);
        drawnVersion++;
    }

    void Redraw()
    {
        RedrawMap();
        RedrawAreas();
        RefreshPointList();
        RefreshHint();
        RefreshRelayPanel();
    }

    // ---- Forks ----
    // A fork (2026-11-20): the Fork button (or G) opens a pop-up where the course setter picks how many branches and
    // which legs run it. With "every team runs every branch" there is one branch per leg (a 2-way fork on a 3-leg
    // relay is on 2 of the legs; the third runs straight). The fork goes in after the last control of the leg being
    // laid; the other chosen legs get it after the same control, or a copy of this leg when they are empty (or when the
    // course setter asks for it). Then the branches are drawn: the first control of A, B, C... one after the other, then
    // each branch again for more controls, and the next control after that is where the branches meet.

    // How many branches a fork may have: one per leg, or up to 10 when the teams get random branches
    int MaxBranches => forkEveryVariant ? Mathf.Max(2, legs) : 10;

    bool ValidBranch() => branchFork >= 0 && branchFork < forks.Count && branchIndex >= 0 && branchIndex < forks[branchFork].branches.Count;

    // The legs whose course has fork f
    List<int> LegsWithFork(int f)
    {
        List<int> result = new List<int>();
        for (int leg = 0; leg < legs && leg < legCourse.Count; leg++)
        {
            if (courses[legCourse[leg]].items.Exists(item => item.fork == f)) result.Add(leg);
        }
        return result;
    }

    List<int> LegsWithCourse(EditCourse course)
    {
        List<int> result = new List<int>();
        for (int leg = 0; leg < legs && leg < legCourse.Count; leg++)
        {
            if (courses[legCourse[leg]] == course) result.Add(leg);
        }
        return result;
    }

    // The legs that run fork f: the ones the course setter chose, and any that have it
    List<int> PlannedLegs(int f)
    {
        List<int> result = new List<int>(forks[f].legs);
        foreach (int leg in LegsWithFork(f)) if (!result.Contains(leg)) result.Add(leg);
        result.RemoveAll(l => l < 0 || l >= legs);
        result.Sort();
        return result;
    }

    static string LegList(List<int> list)
    {
        if (list.Count == 0) return "no legs";
        List<string> names = list.ConvertAll(l => (l + 1).ToString());
        string text = names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
        return (list.Count == 1 ? "leg " : "legs ") + text;
    }

    static bool IsEmpty(EditCourse course) => !course.start.HasValue && !course.finish.HasValue && course.items.Count == 0;

    // A leg that should run a fork but doesn't have it: the control where the fork starts is missing on it.
    // An empty leg is not counted: it gets a copy of a leg with the fork when it is opened (or saved).
    bool MissingFork(int leg, out int fork)
    {
        fork = -1;
        if (!relay || leg < 0 || leg >= legs || leg >= legCourse.Count) return false;
        EditCourse course = courses[legCourse[leg]];
        if (IsEmpty(course)) return false;
        for (int f = 0; f < forks.Count; f++)
        {
            if (forks[f].legs.Contains(leg) && !course.items.Exists(item => item.fork == f))
            {
                fork = f;
                return true;
            }
        }
        return false;
    }

    // ---- A fork belongs to the leg it was laid on (2026-11-20) ----
    // The branches of a fork are the same on every leg that runs it, so a change to them is a change on all those legs.
    // The user: working on leg 2 must never change leg 1. So the controls of a fork can only be moved, added or removed
    // on its home leg (the leg it was laid on). On the other legs it is shown, and a right-click on one of its controls
    // takes the fork off that leg only: it stays faded on the map, and a click puts it back where it was.

    // The leg where fork f can be changed: the leg it was laid on, else the first leg that has it (-1: no leg has it)
    int HomeLeg(int f)
    {
        if (f < 0 || f >= forks.Count) return -1;
        List<int> having = LegsWithFork(f);
        int home = forks[f].home;
        if (having.Contains(home)) return home;
        return having.Count > 0 ? having[0] : -1;
    }

    // True when fork f can't be changed on the leg being laid (it belongs to another leg)
    bool ForkLocked(int f)
    {
        if (!relay || f < 0 || f >= forks.Count) return false;
        int home = HomeLeg(f);
        return home >= 0 && home != selectedLeg && !LegsWithCourse(Cur).Contains(home);
    }

    string LockedText(int f) => "Fork " + (f + 1) + " is laid on leg " + (HomeLeg(f) + 1) + ", so it can only be changed there.";

    // Fork f is taken off this leg only: the leg goes straight from the control before it to the control after it
    void TakeForkOff(int f)
    {
        EditCourse course = Cur;
        if (!course.items.Exists(item => item.fork == f)) return;
        PushUndo();
        if (tool == Tool.Branch && branchFork == f) SetTool(Tool.Control);
        course = OwnCourse(selectedLeg);
        course.items.RemoveAll(item => item.fork == f);
        foreach (int leg in LegsWithCourse(course)) forks[f].legs.Remove(leg);
        if (forks[f].home == selectedLeg) forks[f].home = -1;
        insertAfter = null;
        errorPoint = null;
        bool elsewhere = LegsWithFork(f).Count > 0;
        SetStatus("Fork " + (f + 1) + " is off leg " + (selectedLeg + 1) + " now." + (elsewhere
            ? " It stays faded on the map: click one of its controls to put it back. Ctrl+Z undoes."
            : " Ctrl+Z undoes."), false);
        Redraw();
    }

    // Controls taken off a leg are kept faded on it for a while, so they can be clicked back in place (2026-11-20)
    readonly Dictionary<EditCourse, List<Vector3>> removedGhosts = new Dictionary<EditCourse, List<Vector3>>();
    const int MaxGhosts = 40;

    void KeepGhost(EditCourse course, Vector3 point)
    {
        if (!relay) return;
        if (!removedGhosts.TryGetValue(course, out List<Vector3> list))
        {
            list = new List<Vector3>();
            removedGhosts[course] = list;
        }
        list.RemoveAll(p => Same(p, point));
        list.Add(point);
        if (list.Count > MaxGhosts) list.RemoveAt(0);
    }

    bool IsGhost(Vector3 point) => removedGhosts.TryGetValue(Cur, out List<Vector3> list) && list.Exists(p => Same(p, point));

    // A control was laid at this spot: it is no longer a removed one
    void DropGhost(Vector3 point)
    {
        if (removedGhosts.TryGetValue(Cur, out List<Vector3> list)) list.RemoveAll(p => Same(p, point));
    }

    // ---- The fork pop-up ----

    GameObject forkDialog;
    RectTransform dialogCard;
    int dialogFork;                 // the fork being changed, or -1 for a new one
    int dialogAfter;                // a new fork: goes in after this item of the leg (-1 = right after the start)
    int dialogBranches;
    readonly List<int> dialogLegs = new List<int>();
    bool dialogCopy;

    // Where a new fork goes on the leg being laid (after the picked control, else after the last one), or why it can't
    string ForkPlaceProblem(int? after, out int at)
    {
        EditCourse course = Cur;
        at = after ?? (InsertAfter() ?? course.items.Count - 1);
        if (!course.start.HasValue) return "Place the start first. A fork goes after the start or after a control.";
        if (at >= 0 && at < course.items.Count && course.items[at].fork >= 0)
            return "Right after a fork there must be a control where its branches meet. Click that control first, then press Fork.";
        if (at + 1 < course.items.Count && course.items[at + 1].fork >= 0)
            return "There is already a fork right after " + (at < 0 ? "the start" : "control " + (at + 1)) + ". Put a control between them first.";
        if (ControlCount(course) + 2 > CourseData.MaxControls) return "A course can have at most " + CourseData.MaxControls + " controls.";
        if (legs < 2) return "A fork needs at least two legs.";
        return null;
    }

    // fork: -1 for a new fork (after: put it in after this item, or null for after the last control), or the fork to change
    void OpenForkDialog(int fork, int? after)
    {
        if (!relay || busy || forkDialog != null || canvasObject == null) return;
        if (tool == Tool.Branch) StopForkDrawing();
        if (tool == Tool.Forbidden) SetTool(Cur.start.HasValue ? Tool.Control : Tool.Start);
        dialogFork = fork;
        dialogLegs.Clear();
        if (fork < 0)
        {
            string problem = ForkPlaceProblem(after, out dialogAfter);
            if (problem != null)
            {
                SetStatus(problem, true);
                return;
            }
            dialogBranches = forkEveryVariant ? Mathf.Clamp(legs, 2, 3) : 2;
            FitDialogLegs();
        }
        else
        {
            if (fork >= forks.Count) return;
            dialogBranches = Mathf.Max(2, forks[fork].branches.Count);
            dialogLegs.AddRange(PlannedLegs(fork));
        }
        dialogCopy = false;
        Image dim = CourseUI.Panel("Fork", canvasObject.transform, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
        CourseUI.Stretch(dim.rectTransform);
        forkDialog = dim.gameObject;
        Image card = CourseUI.CardPanel("Card", dim.transform);
        dialogCard = card.rectTransform;
        CourseUI.Place(dialogCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 600));
        CourseUI.Column(card.gameObject, 14, new RectOffset(34, 34, 28, 28));
        FillForkDialog();
    }

    // A new fork: this leg, and the legs after it (then before it) until there is one leg per branch
    void FitDialogLegs()
    {
        if (!forkEveryVariant)
        {
            if (!dialogLegs.Contains(selectedLeg)) dialogLegs.Add(selectedLeg);
            return;
        }
        dialogLegs.RemoveAll(l => l >= legs);
        while (dialogLegs.Count > dialogBranches)
        {
            int drop = dialogLegs.FindLast(l => l != selectedLeg || dialogFork >= 0);
            dialogLegs.Remove(drop);
        }
        for (int leg = selectedLeg; leg < legs && dialogLegs.Count < dialogBranches; leg++) if (!dialogLegs.Contains(leg)) dialogLegs.Add(leg);
        for (int leg = selectedLeg - 1; leg >= 0 && dialogLegs.Count < dialogBranches; leg--) if (!dialogLegs.Contains(leg)) dialogLegs.Add(leg);
        dialogLegs.Sort();
    }

    bool ForkDialogValid(out string why)
    {
        why = null;
        if (dialogLegs.Count == 0) why = "Pick the legs that run the fork.";
        else if (forkEveryVariant && dialogLegs.Count != dialogBranches)
            why = "Pick " + dialogBranches + " legs, one for each branch (" + dialogLegs.Count + " picked). The other legs run straight.";
        else if (dialogFork < 0 && !dialogLegs.Contains(selectedLeg)) why = "The leg you are laying (leg " + (selectedLeg + 1) + ") must be one of them.";
        return why == null;
    }

    // The leg whose course is copied to empty legs (and with "Copy all of leg"): the leg being laid if it has the fork
    int DialogSourceLeg()
    {
        if (dialogFork < 0) return selectedLeg;
        if (Cur.items.Exists(item => item.fork == dialogFork)) return selectedLeg;
        List<int> having = LegsWithFork(dialogFork);
        return having.Count > 0 ? having[0] : selectedLeg;
    }

    void FillForkDialog()
    {
        if (dialogCard == null) return;
        for (int i = dialogCard.childCount - 1; i >= 0; i--)
        {
            GameObject child = dialogCard.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        Transform card = dialogCard;
        string title = dialogFork >= 0 ? "Fork " + (dialogFork + 1)
            : "New fork after " + (dialogAfter < 0 ? "the start" : "control " + (dialogAfter + 1));
        CourseUI.Size(CourseUI.Text("Title", card, title, 34, CourseUI.Ink, FontStyles.Bold), 44);

        // How many branches
        RectTransform branchRow = CourseUI.Rect("Branches", card);
        CourseUI.Row(branchRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(branchRow, 52);
        TextMeshProUGUI branchLabel = CourseUI.Text("Label", branchRow, "Branches", 22, CourseUI.Ink, FontStyles.Bold);
        branchLabel.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(branchLabel, 52, 130);
        int most = Mathf.Min(MaxBranches, 10);
        for (int n = 2; n <= most; n++)
        {
            int count = n;
            Button b = CourseUI.Button(n.ToString(), branchRow, n.ToString(), n == dialogBranches ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light, () =>
            {
                dialogBranches = count;
                if (forkEveryVariant) FitDialogLegs();
                FillForkDialog();
            }, 22);
            CourseUI.Size(b, 50, 50);
        }

        // Which legs
        RectTransform legRow = CourseUI.Rect("Legs", card);
        CourseUI.Row(legRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(legRow, 52);
        TextMeshProUGUI legLabel = CourseUI.Text("Label", legRow, "Legs", 22, CourseUI.Ink, FontStyles.Bold);
        legLabel.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(legLabel, 52, 130);
        for (int leg = 0; leg < legs; leg++)
        {
            int captured = leg;
            bool on = dialogLegs.Contains(leg);
            Button b = CourseUI.Button("Leg " + (leg + 1), legRow, (leg + 1).ToString(), on ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light, () =>
            {
                if (dialogLegs.Contains(captured))
                {
                    if (dialogFork < 0 && captured == selectedLeg) return;   // the fork is drawn on this leg
                    dialogLegs.Remove(captured);
                }
                else dialogLegs.Add(captured);
                dialogLegs.Sort();
                FillForkDialog();
            }, 22);
            CourseUI.Size(b, 50, 50);
        }

        // What happens
        ForkDialogValid(out string why);
        string info;
        if (why != null) info = "<color=#C0392B>" + why + "</color>";
        else if (forkEveryVariant)
            info = "Every team runs each branch once on " + LegList(dialogLegs) + ". After the last of them, all teams have run the same course."
                   + (legs > dialogLegs.Count ? " The other legs run straight." : "");
        else info = "Every team gets a random branch on " + LegList(dialogLegs) + ".";
        if (dialogFork >= 0)
        {
            List<int> before = PlannedLegs(dialogFork);
            List<int> off = before.FindAll(l => !dialogLegs.Contains(l));
            if (off.Count > 0) info += "\n<color=#8A6A00>" + Capital(LegList(off)) + " will run straight along branch A.</color>";
            int fewer = forks[dialogFork].branches.Count - dialogBranches;
            if (fewer > 0)
            {
                List<string> letters = new List<string>();
                for (int b = dialogBranches; b < forks[dialogFork].branches.Count; b++) letters.Add(CourseData.BranchLetter(b));
                info += "\n<color=#8A6A00>Branch " + string.Join(", ", letters) + " and " + (fewer == 1 ? "its" : "their") + " controls will be removed.</color>";
            }
        }
        TextMeshProUGUI infoText = CourseUI.Text("Info", card, info, 20, CourseUI.InkMuted);
        CourseUI.Size(infoText, 110);

        // Copy all of the leg to the other legs, or put the fork in after the same control there
        int source = DialogSourceLeg();
        Button copy = CourseUI.Button("Copy", card, "Copy all of leg " + (source + 1) + " to the other legs:  " + (dialogCopy ? "Yes" : "No"),
            dialogCopy ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light, () => { dialogCopy = !dialogCopy; FillForkDialog(); }, 20);
        CourseUI.Size(copy, 50);
        HoverTip.Add(copy, "No: legs that already have controls get the fork after the same control as on leg " + (source + 1)
            + ". Yes: the other legs become copies of leg " + (source + 1) + " (empty legs always do). Ctrl+Z takes it back.");

        RectTransform spacer = CourseUI.Rect("Space", card);
        CourseUI.Size(spacer, 4).flexibleHeight = 1;

        RectTransform buttons = CourseUI.Rect("Buttons", card);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        Button ok = CourseUI.Button("OK", buttons, dialogFork < 0 ? "Draw the branches" : "Save the fork", CourseUI.ButtonKind.Primary, ConfirmForkDialog, 24);
        CourseUI.Size(ok, 58, -1, 2);
        ok.interactable = why == null;
        Button cancel = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, CloseForkDialog, 22);
        CourseUI.Size(cancel, 58, -1, 1);
    }

    static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    void CloseForkDialog()
    {
        if (forkDialog != null) Destroy(forkDialog);
        forkDialog = null;
        dialogCard = null;
    }

    void ConfirmForkDialog()
    {
        if (!ForkDialogValid(out _)) return;
        List<int> chosen = new List<int>(dialogLegs);
        int branches = dialogBranches;
        bool copy = dialogCopy;
        int fork = dialogFork;
        int after = dialogAfter;
        CloseForkDialog();
        PushUndo();
        if (fork < 0) CreateFork(after, branches, chosen, copy);
        else ChangeFork(fork, branches, chosen, copy);
    }

    void CreateFork(int after, int branches, List<int> chosen, bool copy)
    {
        EditCourse course = Cur;
        EditFork fork = new EditFork();
        for (int b = 0; b < branches; b++) fork.branches.Add(new List<Vector3>());
        fork.legs.AddRange(chosen);
        foreach (int leg in LegsWithCourse(course)) if (!fork.legs.Contains(leg)) fork.legs.Add(leg);
        fork.legs.Sort();
        fork.home = selectedLeg;
        forks.Add(fork);
        int f = forks.Count - 1;
        course.items.Insert(Mathf.Clamp(after + 1, 0, course.items.Count), new EditItem { fork = f });
        insertAfter = null;
        string others = PutForkOnLegs(f, fork.legs, copy);
        BeginForkDrawing(f, true);
        SetStatus("Fork " + (f + 1) + ": click the first control of branch A." + others, false);
    }

    void ChangeFork(int f, int branches, List<int> chosen, bool copy)
    {
        EditFork fork = forks[f];
        List<int> before = PlannedLegs(f);
        bool more = fork.branches.Count < branches;
        while (fork.branches.Count < branches) fork.branches.Add(new List<Vector3>());
        while (fork.branches.Count > branches) fork.branches.RemoveAt(fork.branches.Count - 1);
        fork.legs.Clear();
        fork.legs.AddRange(chosen);
        // Legs taken off the fork run straight along branch A
        List<int> off = before.FindAll(l => !chosen.Contains(l));
        foreach (int leg in off) RunStraight(leg, f);
        string others = PutForkOnLegs(f, chosen.FindAll(l => !before.Contains(l)), copy);
        string message = "Fork " + (f + 1) + " is on " + LegList(PlannedLegs(f)) + " now." + others
                         + (off.Count > 0 ? " " + Capital(LegList(off)) + " run" + (off.Count == 1 ? "s" : "") + " straight now: have a look (yellow dot)." : "");
        if (more && Cur.items.Exists(item => item.fork == f))
        {
            BeginForkDrawing(f, true);
            SetStatus(message + " Click the first control of branch " + CourseData.BranchLetter(branchIndex) + ".", false);
            return;
        }
        if (more) message += " Open a leg with the fork and press + Control on the new branches to draw them.";
        SetStatus(message, false);
        Redraw();
    }

    // The fork is taken off a leg: there the leg runs along branch A's controls instead
    void RunStraight(int leg, int f)
    {
        if (leg < 0 || leg >= legs) return;
        EditCourse course = courses[legCourse[leg]];
        if (!course.items.Exists(item => item.fork == f)) return;
        course = OwnCourse(leg);
        int i = course.items.FindIndex(item => item.fork == f);
        course.items.RemoveAt(i);
        List<Vector3> straight = forks[f].branches.Count > 0 ? forks[f].branches[0] : new List<Vector3>();
        for (int k = straight.Count - 1; k >= 0; k--) course.items.Insert(i, new EditItem { point = straight[k] });
        if (leg != selectedLeg && !checkLegs.Contains(leg)) checkLegs.Add(leg);
    }

    // Puts fork f on these legs. Returns what happened, for the message under the tools.
    string PutForkOnLegs(int f, List<int> targets, bool copy)
    {
        List<int> copied = new List<int>(), put = new List<int>(), missing = new List<int>();
        foreach (int leg in targets)
        {
            if (leg < 0 || leg >= legs || leg == selectedLeg) continue;
            EditCourse course = courses[legCourse[leg]];
            if (course == Cur || course.items.Exists(item => item.fork == f)) continue;
            course = OwnCourse(leg);
            if (copy || IsEmpty(course))
            {
                // Emptied now; filled with a copy of a leg with the fork when it is opened or the course is saved, so
                // it gets everything that is laid on that leg until then
                course.start = null;
                course.finish = null;
                course.finishTv = false;
                course.items.Clear();
                copied.Add(leg);
            }
            else if (InsertForkLike(f, course))
            {
                put.Add(leg);
                if (!checkLegs.Contains(leg)) checkLegs.Add(leg);
            }
            else missing.Add(leg);
        }
        string text = "";
        if (copied.Count > 0) text += " " + Capital(LegList(copied)) + " will be a copy of this leg.";
        if (put.Count > 0) text += " The fork was put in on " + LegList(put) + " too.";
        if (missing.Count > 0) text += " " + Capital(LegList(missing)) + " " + (missing.Count == 1 ? "has" : "have") + " no control where the fork starts (red dot).";
        return text;
    }

    // Puts fork f into another leg's course after the same control as where it is on a leg that has it
    bool InsertForkLike(int f, EditCourse target)
    {
        List<EditCourse> order = new List<EditCourse> { Cur };
        order.AddRange(courses);
        EditCourse source = null;
        int i = -1;
        foreach (EditCourse c in order)
        {
            if (c == target || LegsWithCourse(c).Count == 0) continue;
            i = c.items.FindIndex(item => item.fork == f);
            if (i >= 0)
            {
                source = c;
                break;
            }
        }
        if (source == null) return false;
        int at;
        if (i == 0)
        {
            if (!source.start.HasValue || !target.start.HasValue || !Same(source.start.Value, target.start.Value)) return false;
            at = 0;
        }
        else
        {
            EditItem before = source.items[i - 1];
            if (before.fork >= 0) return false;
            int j = target.items.FindIndex(item => item.fork < 0 && Same(item.point, before.point));
            if (j < 0) return false;
            at = j + 1;
        }
        if (at < target.items.Count && target.items[at].fork >= 0) return false;
        target.items.Insert(at, new EditItem { fork = f });
        return true;
    }

    // Takes a fork off every leg (the common controls around it stay)
    void DeleteFork(int f)
    {
        PushUndo();
        if (tool == Tool.Branch && branchFork == f) SetTool(Tool.Control);
        foreach (EditCourse course in courses) course.items.RemoveAll(item => item.fork == f);
        forks[f].legs.Clear();
        SetStatus("Fork " + (f + 1) + " was removed from every leg. Ctrl+Z takes it back.", false);
        Redraw();
    }

    // ---- Drawing the branches ----

    void BeginForkDrawing(int f, bool firstRound)
    {
        if (f < 0 || f >= forks.Count) return;
        if (ForkLocked(f))
        {
            SetStatus(LockedText(f), true);
            return;
        }
        EditFork fork = forks[f];
        branchFork = f;
        forkFirstRound = firstRound;
        branchesDone.Clear();
        // Branches drawn before are left as they are; the round goes through the new (empty) ones
        if (firstRound) for (int b = 0; b < fork.branches.Count; b++) if (fork.branches[b].Count > 0) branchesDone.Add(b);
        branchIndex = 0;
        while (branchIndex < fork.branches.Count && branchesDone.Contains(branchIndex)) branchIndex++;
        if (branchIndex >= fork.branches.Count)
        {
            forkFirstRound = false;
            branchesDone.Clear();
            branchIndex = 0;
        }
        SetTool(Tool.Branch);
        Redraw();
    }

    // "+ Control" on a branch in the list: more controls on that branch only
    void StartBranch(int f, int b)
    {
        if (ForkLocked(f))
        {
            SetStatus(LockedText(f), true);
            return;
        }
        branchFork = f;
        branchIndex = b;
        forkFirstRound = false;
        branchesDone.Clear();
        for (int other = 0; other < forks[f].branches.Count; other++) if (other != b) branchesDone.Add(other);
        SetTool(Tool.Branch);
        Redraw();
    }

    // After a control in the first round, or when a branch is done: on to the next branch that still needs drawing
    void MoveToNextBranch()
    {
        if (!ValidBranch()) return;
        EditFork fork = forks[branchFork];
        int count = fork.branches.Count;
        if (forkFirstRound)
        {
            for (int step = 1; step <= count; step++)
            {
                int b = (branchIndex + step) % count;
                if (fork.branches[b].Count == 0 && !branchesDone.Contains(b))
                {
                    branchIndex = b;
                    return;
                }
            }
            forkFirstRound = false;
            for (int b = 0; b < count; b++)
            {
                if (branchesDone.Contains(b)) continue;
                branchIndex = b;
                SetStatus("Every branch has its first control. Branch " + CourseData.BranchLetter(b) + ": click more controls, or press Branch done (Enter).", false);
                return;
            }
            FinishForkDrawing();
            return;
        }
        for (int step = 1; step < count; step++)
        {
            int b = (branchIndex + step) % count;
            if (!branchesDone.Contains(b))
            {
                branchIndex = b;
                SetStatus("Branch " + CourseData.BranchLetter(b) + ": click more controls, or press Branch done (Enter).", false);
                return;
            }
        }
        FinishForkDrawing();
    }

    // "Branch done" (Enter), or "Straight on" for an empty branch
    void NextBranch(bool done)
    {
        if (tool != Tool.Branch || !ValidBranch()) return;
        if (done && !branchesDone.Contains(branchIndex)) branchesDone.Add(branchIndex);
        MoveToNextBranch();
        if (tool == Tool.Branch) Redraw();
    }

    void JumpToBranch(int b)
    {
        if (!ValidBranch() || b < 0 || b >= forks[branchFork].branches.Count) return;
        branchIndex = b;
        branchesDone.Remove(b);
        Redraw();
    }

    void FinishForkDrawing()
    {
        int f = branchFork;
        forkFirstRound = false;
        branchesDone.Clear();
        SetTool(Tool.Control);
        EditCourse course = Cur;
        int i = f >= 0 ? course.items.FindIndex(item => item.fork == f) : -1;
        if (i < 0)
        {
            Redraw();
            return;
        }
        bool last = i == course.items.Count - 1;
        SetStatus(last && !course.finish.HasValue ? "Fork " + (f + 1) + " is drawn. Now click the control where the branches meet."
            : last ? "Fork " + (f + 1) + " is drawn. The branches meet at the finish."
            : "Fork " + (f + 1) + " is drawn. The branches meet at control " + (i + 2) + ".", false);
        Redraw();
    }

    void StopForkDrawing()
    {
        if (tool == Tool.Branch) FinishForkDrawing();
    }

    // The bar at the top of the map while the branches are drawn: which branch is next, and the buttons for it
    void RefreshForkBar()
    {
        bool show = relay && tool == Tool.Branch && ValidBranch() && canvasObject != null;
        if (!show)
        {
            if (forkBar != null) forkBar.SetActive(false);
            return;
        }
        if (forkBar == null)
        {
            Image card = CourseUI.CardPanel("Fork bar", canvasObject.transform);
            forkBar = card.gameObject;
            RectTransform rect = card.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            HorizontalLayoutGroup row = CourseUI.Row(forkBar, 8, new RectOffset(18, 18, 10, 10));
            row.childForceExpandWidth = false;
            row.childAlignment = TextAnchor.MiddleCenter;
            ContentSizeFitter fitter = forkBar.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        forkBar.SetActive(true);
        forkBar.transform.SetAsLastSibling();
        ((RectTransform)forkBar.transform).anchoredPosition = new Vector2((LeftRoom - RightRoom) / 2f, -24f);
        for (int i = forkBar.transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = forkBar.transform.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        EditFork fork = forks[branchFork];
        TextMeshProUGUI title = CourseUI.Text("Title", forkBar.transform, "Fork " + (branchFork + 1) + (forkFirstRound ? "  <size=17><color=#5B7563>first controls</color></size>" : ""), 22, CourseUI.Ink, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.enableWordWrapping = false;
        CourseUI.Size(title, 46, 220);
        for (int b = 0; b < fork.branches.Count; b++)
        {
            int captured = b;
            int count = fork.branches[b].Count;
            CourseUI.ButtonKind kind = b == branchIndex ? CourseUI.ButtonKind.Primary : branchesDone.Contains(b) ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light;
            Button letter = CourseUI.Button("Branch " + CourseData.BranchLetter(b), forkBar.transform, CourseData.BranchLetter(b) + "<size=15> " + count + "</size>", kind, () => JumpToBranch(captured), 21);
            CourseUI.Size(letter, 46, 70);
            HoverTip.Add(letter, "Branch " + CourseData.BranchLetter(b) + ": " + count + (count == 1 ? " control" : " controls") + ". Click to draw on it.");
        }
        bool empty = fork.branches[branchIndex].Count == 0;
        Button next = CourseUI.Button(empty ? "Straight" : "Done", forkBar.transform, empty ? "Straight on (no control)" : "Branch done (Enter)",
            CourseUI.ButtonKind.Secondary, () => NextBranch(true), 19);
        CourseUI.Size(next, 46, 250);
        HoverTip.Add(next, empty ? "This branch has no controls of its own: it goes straight to the control where the branches meet."
                                 : "Branch " + CourseData.BranchLetter(branchIndex) + " is done: on to the next branch.");
        Button stop = CourseUI.Button("Stop", forkBar.transform, "Stop (Esc)", CourseUI.ButtonKind.Light, StopForkDrawing, 19);
        CourseUI.Size(stop, 46, 130);
        HoverTip.Add(stop, "Stop drawing the branches. Click a branch's + Control in the list to go on later.");
    }

    void RemoveBranch(int f, int b)
    {
        PushUndo();
        forks[f].branches.RemoveAt(b);
        if (tool == Tool.Branch) SetTool(Tool.Control);
        // A fork with one branch left is just controls, if no other leg has it
        if (forks[f].branches.Count == 1 && LegsWithFork(f).Count <= LegsWithCourse(Cur).Count)
        {
            EditCourse course = Cur;
            int i = course.items.FindIndex(item => item.fork == f);
            if (i >= 0)
            {
                course.items.RemoveAt(i);
                List<Vector3> rest = forks[f].branches[0];
                for (int k = rest.Count - 1; k >= 0; k--) course.items.Insert(i, new EditItem { point = rest[k] });
                forks[f].branches.Clear();
                forks[f].legs.Clear();
            }
        }
        Redraw();
    }

    // A faded fork's control was clicked: the whole fork goes on this leg, with the common controls around it
    void UseFork(int f)
    {
        if (!FindForkSource(f, out Vector3 entry, out Vector3 exit, out bool entryIsStart, out bool exitIsFinish, out int _)) return;
        EditCourse course = Cur;
        if (course.items.Exists(item => item.fork == f))
        {
            SetStatus("This leg already has fork " + (f + 1) + ".", true);
            return;
        }
        // Back where it was: the leg still has the control (or start) where the fork starts
        int at = -2;
        if (entryIsStart && course.start.HasValue && Same(course.start.Value, entry)) at = -1;
        else at = course.items.FindIndex(item => item.fork < 0 && Same(item.point, entry));
        if (at >= -1 && at + 1 < course.items.Count && course.items[at + 1].fork >= 0) at = -2;
        if (at >= -1)
        {
            if (ControlCount(course) + forks[f].branches.ConvertAll(branch => branch.Count).Sum() + 1 > CourseData.MaxControls)
            {
                SetStatus("A course can have at most " + CourseData.MaxControls + " controls, and the fork does not fit.", true);
                return;
            }
            PushUndo();
            course = OwnCourse(selectedLeg);
            course.items.Insert(at + 1, new EditItem { fork = f });
            int next = at + 2;
            bool meets = next < course.items.Count ? course.items[next].fork < 0 && Same(course.items[next].point, exit)
                : course.finish.HasValue && exitIsFinish && Same(course.finish.Value, exit);
            if (!meets && !exitIsFinish) course.items.Insert(next, new EditItem { point = exit });
            else if (!meets && exitIsFinish && next >= course.items.Count) course.finish = exit;
            foreach (int leg in LegsWithCourse(course)) if (!forks[f].legs.Contains(leg)) forks[f].legs.Add(leg);
            forks[f].legs.Sort();
            insertAfter = null;
            errorPoint = null;
            string warning = forkEveryVariant && PlannedLegs(f).Count > forks[f].branches.Count ? " It now has more legs than branches: press Edit on the fork to pick its legs again." : "";
            SetStatus("Fork " + (f + 1) + " is back on leg " + (selectedLeg + 1) + "." + warning + " Ctrl+Z takes it off again.", false);
            Redraw();
            return;
        }
        if (entryIsStart && course.items.Count > 0)
        {
            SetStatus("Fork " + (f + 1) + " starts at the start, so it has to be the first thing on the leg. Remove this leg's controls first, or hold Shift to lay a new control here.", true);
            return;
        }
        if (ControlCount(course) + 2 + forks[f].branches.ConvertAll(branch => branch.Count).Sum() > CourseData.MaxControls)
        {
            SetStatus("A course can have at most " + CourseData.MaxControls + " controls, and the fork does not fit.", true);
            return;
        }
        PushUndo();
        if (entryIsStart)
        {
            course.start = entry;
        }
        else
        {
            Vector3? last = course.items.Count == 0 ? course.start : course.items[course.items.Count - 1].fork < 0 ? course.items[course.items.Count - 1].point : (Vector3?)null;
            if (!last.HasValue || !Same(last.Value, entry))
            {
                course.items.Add(new EditItem { point = entry });
            }
        }
        course.items.Add(new EditItem { fork = f });
        foreach (int leg in LegsWithCourse(course)) if (!forks[f].legs.Contains(leg)) forks[f].legs.Add(leg);
        forks[f].legs.Sort();
        if (exitIsFinish)
        {
            course.finish = exit;
            SetTool(Tool.Finish);
        }
        else
        {
            course.items.Add(new EditItem { point = exit });
            SetTool(Tool.Control);
        }
        errorPoint = null;
        string warn = forkEveryVariant && PlannedLegs(f).Count > forks[f].branches.Count ? " It now has more legs than branches: press Edit on the fork to pick its legs again." : "";
        SetStatus("Fork " + (f + 1) + " is now on leg " + (selectedLeg + 1) + " too, with the common controls around it." + (exitIsFinish ? " It ends at the finish." : " Carry on from the control after it.") + warn + " Ctrl+Z takes it back.", false);
        Redraw();
    }

    // ---- Legs ----

    void SelectLeg(int leg)
    {
        insertAfter = null;
        if (tool == Tool.Branch)
        {
            forkFirstRound = false;
            branchesDone.Clear();
        }
        selectedLeg = Mathf.Clamp(leg, 0, legs - 1);
        checkLegs.Remove(selectedLeg);
        FillEmptyLeg(selectedLeg, false);
        EditCourse course = Cur;
        errorPoint = null;
        dragging = null;
        SetTool(!course.start.HasValue ? Tool.Start : Tool.Control);
        SetStatus(statusAfterSelect ?? "", false);
        statusAfterSelect = null;
        Redraw();
    }

    void ChangeLegs(int delta)
    {
        PushUndo();
        legs = Mathf.Clamp(legs + delta, CourseData.MinLegs, CourseData.MaxLegs);
        // Every new leg gets its own course
        while (legCourse.Count < legs)
        {
            courses.Add(new EditCourse());
            legCourse.Add(courses.Count - 1);
        }
        while (legCourse.Count > legs) legCourse.RemoveAt(legCourse.Count - 1);
        foreach (EditFork fork in forks) fork.legs.RemoveAll(l => l >= legs);
        checkLegs.RemoveAll(l => l >= legs);
        if (selectedLeg >= legs)
        {
            SelectLeg(legs - 1);
            return;
        }
        Redraw();
    }

    // The leg's own course. A leg that shares its course with another leg (relays saved before 2026-11-20 could) gets
    // a copy of it first, so a change only touches this leg.
    EditCourse OwnCourse(int leg)
    {
        EditCourse course = courses[legCourse[leg]];
        if (LegsWithCourse(course).Count <= 1) return course;
        EditCourse own = new EditCourse { start = course.start, finish = course.finish, finishTv = course.finishTv };
        foreach (EditItem item in course.items) own.items.Add(new EditItem { fork = item.fork, point = item.point, tv = item.tv });
        courses.Add(own);
        legCourse[leg] = courses.Count - 1;
        return own;
    }

    // Makes to a copy of from, for this leg: the forks the leg runs come along, other forks become straight (branch A)
    void CopyCourseInto(EditCourse from, EditCourse to, int leg)
    {
        if (from == to) return;
        to.start = from.start;
        to.finish = from.finish;
        to.finishTv = from.finishTv;
        to.items.Clear();
        foreach (EditItem item in from.items)
        {
            if (item.fork < 0)
            {
                to.items.Add(new EditItem { point = item.point, tv = item.tv });
                continue;
            }
            EditFork fork = forks[item.fork];
            if (fork.legs.Contains(leg)) to.items.Add(new EditItem { fork = item.fork });
            else if (fork.branches.Count > 0) foreach (Vector3 point in fork.branches[0]) to.items.Add(new EditItem { point = point });
        }
    }

    // An empty leg is filled when it is opened: with a copy of a leg that has a fork it should run, or else (not when
    // saving) of the leg before (2026-11-16). Returns true when it was filled.
    bool FillEmptyLeg(int leg, bool forkOnly)
    {
        if (!relay || leg < 0 || leg >= legs || leg >= legCourse.Count) return false;
        EditCourse course = courses[legCourse[leg]];
        if (!IsEmpty(course)) return false;
        int source = -1;
        int forkSource = -1;
        for (int f = 0; f < forks.Count && source < 0; f++)
        {
            if (!forks[f].legs.Contains(leg)) continue;
            foreach (int other in LegsWithFork(f))
            {
                if (other == leg) continue;
                source = other;
                forkSource = f;
                break;
            }
        }
        if (source < 0 && !forkOnly && leg > 0) source = leg - 1;
        if (source < 0) return false;
        EditCourse from = courses[legCourse[source]];
        if (from == course || IsEmpty(from)) return false;
        CopyCourseInto(from, course, leg);
        if (course.items.Count > 0)
            statusAfterSelect = "Leg " + (leg + 1) + " starts as a copy of leg " + (source + 1) + (forkSource >= 0 ? ", which has fork " + (forkSource + 1) : "")
                                + ". Change what you like, or use \"Copy a leg\" (it can also copy only the forks).";
        return true;
    }

    // "Copy a leg": the drop-down list under the button
    void OpenCopyMenu()
    {
        if (!relay || copyLegButton == null) return;
        List<string> labels = new List<string> { "Empty: start over" };
        List<int> sources = new List<int> { -1 };
        List<int> forksHere = ForksForLeg(selectedLeg);
        if (forksHere.Count > 0)
        {
            labels.Add("Only the forks: " + (forksHere.Count == 1 ? "fork " : "forks ") + string.Join(", ", forksHere.ConvertAll(f => (f + 1).ToString())));
            sources.Add(-2);
        }
        for (int leg = 0; leg < legs; leg++)
        {
            if (leg == selectedLeg) continue;
            EditCourse course = courses[legCourse[leg]];
            labels.Add("Copy of leg " + (leg + 1) + (IsEmpty(course) ? "  (empty)" : "  (" + ControlCount(course) + " controls)"));
            sources.Add(leg);
        }
        ChoiceDropdown.Open((RectTransform)copyLegButton.transform, labels.ToArray(), -1, index =>
        {
            if (index >= 0 && index < sources.Count) CopyLeg(sources[index]);
        });
    }

    // The forks this leg should run that are laid on another leg, in the order they come on their courses
    List<int> ForksForLeg(int leg)
    {
        List<(int fork, float order)> found = new List<(int, float)>();
        for (int f = 0; f < forks.Count; f++)
        {
            if (!PlannedLegs(f).Contains(leg) && !forks[f].legs.Contains(leg)) continue;
            if (!FindForkSource(f, out _, out _, out _, out _, out int sourceLeg)) continue;
            EditCourse source = courses[legCourse[sourceLeg]];
            int at = source.items.FindIndex(item => item.fork == f);
            found.Add((f, at / (float)Mathf.Max(1, source.items.Count)));
        }
        found.Sort((a, b) => a.order.CompareTo(b.order));
        return found.ConvertAll(x => x.fork);
    }

    // "Only the forks" (2026-11-20, the user: copy the forks this leg runs but lay the rest of the leg myself): the leg
    // gets each of its forks with the control before and after it, and keeps (or gets) the start and finish
    void CopyForksOnly()
    {
        List<int> chosen = ForksForLeg(selectedLeg);
        if (chosen.Count == 0) return;
        // Where each fork lies, found before this leg's controls are cleared (the leg itself may be where one lies)
        var found = new List<(int f, Vector3 entry, Vector3 exit, bool entryIsStart, bool exitIsFinish, Vector3? start, Vector3? finish)>();
        foreach (int f in chosen)
        {
            if (!FindForkSource(f, out Vector3 entry, out Vector3 exit, out bool entryIsStart, out bool exitIsFinish, out int sourceLeg)) continue;
            EditCourse source = courses[legCourse[sourceLeg]];
            found.Add((f, entry, exit, entryIsStart, exitIsFinish, source.start, source.finish));
        }
        PushUndo();
        if (tool == Tool.Branch) StopForkDrawing();
        EditCourse own = OwnCourse(selectedLeg);
        Vector3? start = own.start, finish = own.finish;
        own.items.Clear();
        int taken = 0;
        foreach (var (f, entry, exit, entryIsStart, exitIsFinish, sourceStart, sourceFinish) in found)
        {
            if (!start.HasValue) start = sourceStart;
            if (!finish.HasValue) finish = sourceFinish;
            if (entryIsStart)
            {
                if (own.items.Count > 0) continue;   // a fork from the start has to come first
                start = entry;
            }
            else
            {
                Vector3? last = own.items.Count == 0 ? start : own.items[own.items.Count - 1].fork < 0 ? own.items[own.items.Count - 1].point : (Vector3?)null;
                if (!last.HasValue || !Same(last.Value, entry)) own.items.Add(new EditItem { point = entry });
            }
            own.items.Add(new EditItem { fork = f });
            if (exitIsFinish) finish = exit;
            else own.items.Add(new EditItem { point = exit });
            foreach (int leg in LegsWithCourse(own)) if (!forks[f].legs.Contains(leg)) forks[f].legs.Add(leg);
            forks[f].legs.Sort();
            taken++;
        }
        own.start = start;
        own.finish = finish;
        checkLegs.Remove(selectedLeg);
        insertAfter = null;
        errorPoint = null;
        SetTool(own.start.HasValue ? Tool.Control : Tool.Start);
        SetStatus("Leg " + (selectedLeg + 1) + " has only its " + (taken == 1 ? "fork" : taken + " forks") + " now, with the controls before and after."
                  + " Click a control in the list to add your own controls after it.", false);
        Redraw();
    }

    void CopyLeg(int source)
    {
        if (source == -2)
        {
            CopyForksOnly();
            return;
        }
        PushUndo();
        if (tool == Tool.Branch) StopForkDrawing();
        EditCourse own = OwnCourse(selectedLeg);
        string message;
        if (source < 0)
        {
            own.items.Clear();
            message = "Leg " + (selectedLeg + 1) + " has no controls now. Start and finish are kept.";
        }
        else
        {
            CopyCourseInto(courses[legCourse[source]], own, selectedLeg);
            message = "Leg " + (selectedLeg + 1) + " is a copy of leg " + (source + 1) + " now. Change what you like.";
            List<string> straight = new List<string>();
            foreach (EditItem item in courses[legCourse[source]].items)
                if (item.fork >= 0 && !forks[item.fork].legs.Contains(selectedLeg)) straight.Add((item.fork + 1).ToString());
            if (straight.Count > 0) message += " Fork " + string.Join(", ", straight) + " is not on this leg, so it runs straight along branch A here.";
        }
        checkLegs.Remove(selectedLeg);
        insertAfter = null;
        errorPoint = null;
        SetTool(own.start.HasValue ? Tool.Control : Tool.Start);
        SetStatus(message, false);
        Redraw();
    }

    // Who may run a leg (anyone when not set)
    LegSex SexOf(int leg) => leg >= 0 && leg < legSex.Count ? legSex[leg] : LegSex.Any;

    // Anyone -> Women -> Men -> Anyone for the selected leg
    void CycleLegSex()
    {
        PushUndo();
        while (legSex.Count < legs) legSex.Add(LegSex.Any);
        LegSex now = legSex[selectedLeg];
        legSex[selectedLeg] = now == LegSex.Any ? LegSex.Women : now == LegSex.Women ? LegSex.Men : LegSex.Any;
        SetStatus("Leg " + (selectedLeg + 1) + ": " + (legSex[selectedLeg] == LegSex.Any ? "anyone may run it." : legSex[selectedLeg] == LegSex.Women ? "only women may run it." : "only men may run it."), false);
        Redraw();
    }

    void ToggleForkMode()
    {
        PushUndo();
        forkEveryVariant = !forkEveryVariant;
        Redraw();
    }

    void SetRelay(bool on)
    {
        relay = on;
        if (typeButton != null) CourseUI.SetButtonText(typeButton, relay ? "Type: Relay" : "Type: Individual");
        if (!relay && tool == Tool.Branch) SetTool(Tool.Control);
        if (relay)
        {
            // Leg 1 keeps the course laid so far; the others get courses of their own
            if (legCourse.Count == 0) legCourse.Add(0);
            while (legCourse.Count < legs)
            {
                courses.Add(new EditCourse());
                legCourse.Add(courses.Count - 1);
            }
            selectedLeg = Mathf.Clamp(selectedLeg, 0, legs - 1);
        }
        else
        {
            selectedLeg = 0;
        }
        if (courseRenderer != null) Redraw();
        else RefreshRelayPanel();
    }

    // What the dot on a leg button shows
    enum LegState { Empty, Started, Done, Forked, Check, Unfinished }

    LegState StateOf(int leg)
    {
        EditCourse course = courses[legCourse[leg]];
        if (MissingFork(leg, out _)) return LegState.Unfinished;
        if (checkLegs.Contains(leg)) return LegState.Check;
        bool planned = forks.Exists(f => f.legs.Contains(leg));
        if (planned || course.items.Exists(item => item.fork >= 0 && forks[item.fork].branches.Count > 1)) return LegState.Forked;
        if (course.start.HasValue && course.finish.HasValue && course.items.Count > 0) return LegState.Done;
        return IsEmpty(course) ? LegState.Empty : LegState.Started;
    }

    string LegTip(int leg)
    {
        EditCourse course = courses[legCourse[leg]];
        List<string> forkNames = new List<string>();
        foreach (EditItem item in course.items)
        {
            if (item.fork >= 0) forkNames.Add("fork " + (item.fork + 1));
        }
        string text = "Leg " + (leg + 1) + ": " + (IsEmpty(course) ? "empty" : ControlCount(course) + " controls") + (forkNames.Count > 0 ? ", " + string.Join(", ", forkNames) : "");
        LegSex sex = SexOf(leg);
        if (sex != LegSex.Any) text += ". Only " + (sex == LegSex.Women ? "women" : "men") + " may run it";
        switch (StateOf(leg))
        {
            case LegState.Unfinished:
                MissingFork(leg, out int f);
                text += ".\nNot finished: it should run fork " + (f + 1) + ", but it has no control where the fork starts.";
                break;
            case LegState.Check: text += ".\nChanged by a fork on another leg: have a look."; break;
            case LegState.Done: text += ".\nReady."; break;
            case LegState.Empty: text += ".\nIt starts as a copy of the leg before when you open it."; break;
            default: text += "."; break;
        }
        return text;
    }

    void RefreshRelayPanel()
    {
        if (listTitle != null) listTitle.text = relay ? "Relay" : "Controls";
        if (relaySection != null) relaySection.SetActive(relay);
        if (forkButton != null) forkButton.gameObject.SetActive(relay);
        RefreshForkBar();
        if (relaySection == null || !relay || legButtonRow == null) return;
        legsLabel.text = legs + " legs";
        for (int i = legButtonRow.childCount - 1; i >= 0; i--)
        {
            GameObject child = legButtonRow.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        for (int leg = 0; leg < legs && leg < legCourse.Count; leg++)
        {
            int captured = leg;
            LegSex sex = SexOf(leg);
            string who = sex == LegSex.Women ? "<size=14> W</size>" : sex == LegSex.Men ? "<size=14> M</size>" : "";
            Button button = CourseUI.Button("Leg " + (leg + 1), legButtonRow, (leg + 1) + who,
                leg == selectedLeg ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light, () => SelectLeg(captured), 22);
            LegState state = StateOf(leg);
            Color? dot = state == LegState.Unfinished ? CourseUI.Danger : state == LegState.Check ? CourseUI.Hex("D9A21B")
                : state == LegState.Forked ? CourseUI.Accent : state == LegState.Done ? CourseUI.Hex("3A9A5B") : (Color?)null;
            if (dot.HasValue)
            {
                // White ring, so an orange dot shows on the orange button of the leg being laid too
                Image ring = CourseUI.Panel("Dot", button.transform, Color.white);
                if (CourseUI.Skin != null && CourseUI.Skin.pill != null) ring.sprite = CourseUI.Skin.pill;
                ring.raycastTarget = false;
                CourseUI.Place(ring.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-8f, -8f), new Vector2(20f, 20f));
                Image fill = CourseUI.Panel("Fill", ring.transform, dot.Value);
                if (CourseUI.Skin != null && CourseUI.Skin.pill != null) fill.sprite = CourseUI.Skin.pill;
                fill.raycastTarget = false;
                CourseUI.Place(fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
            }
            HoverTip.Add(button, () => LegTip(captured));
        }
        LayoutElement gridSize = legButtonRow.GetComponent<LayoutElement>();
        if (gridSize != null) gridSize.preferredHeight = gridSize.minHeight = legs > 5 ? 2 * 50f + 8f : 50f;
        LegSex selectedSex = SexOf(selectedLeg);
        CourseUI.SetButtonText(legSexButton, "Runners: " + (selectedSex == LegSex.Any ? "Anyone" : selectedSex == LegSex.Women ? "Women" : "Men"));
        CourseUI.SetButtonColor(legSexButton, selectedSex == LegSex.Any ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
        bool usual = forkEveryVariant && showOtherLegs;
        CourseUI.SetButtonText(relaySettingsButton, usual ? "Settings" : "Settings*");
        CourseUI.SetButtonColor(relaySettingsButton, usual ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
    }

    // ---- Clearing, checking and saving ----

    void ClearCourse()
    {
        punchSides.Clear();
        courses.Clear();
        forks.Clear();
        removedGhosts.Clear();
        legCourse.Clear();
        legSex.Clear();
        checkLegs.Clear();
        forkFirstRound = false;
        branchesDone.Clear();
        areas.Clear();
        boundaries.Clear();
        bridges.Clear();
        openArea = null;
        draggingCorner = false;
        moveType = null;
        courses.Add(new EditCourse());
        legCourse.Add(0);
        if (relay)
        {
            while (legCourse.Count < legs)
            {
                courses.Add(new EditCourse());
                legCourse.Add(courses.Count - 1);
            }
        }
        selectedLeg = 0;
        editing = null;
        errorPoint = null;
        SetTerrain(map != null && map.IsSprint ? CourseData.Sprint : CourseData.Forest);
        if (nameField != null) nameField.text = "";
        SetTool(Tool.Start);
        SetStatus("", false);
        if (courseRenderer != null) Redraw();
    }

    CourseData BuildCourse()
    {
        CourseData course = new CourseData
        {
            id = editing != null ? editing.id : CourseData.NewId(),
            name = nameField.text.Trim(),
            author = authorField.text.Trim(),
            authorId = CloudCourses.PlayerId,
            mapId = map.id,
            background = "",
            terrain = terrain,
            builtIn = false,
            createdAt = editing != null ? editing.createdAt : DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        foreach (List<Vector3> area in areas)
        {
            if (area.Count >= 3) course.forbidden.Add(new ForbiddenArea { corners = area.ConvertAll(WholeMeters) });
        }
        foreach (List<Vector3> line in boundaries)
        {
            if (line.Count >= 2) course.forbidden.Add(new ForbiddenArea { line = true, corners = line.ConvertAll(WholeMeters) });
        }
        foreach (List<Vector3> line in bridges)
        {
            if (line.Count >= 2) course.forbidden.Add(new ForbiddenArea { line = true, bridge = true, corners = line.ConvertAll(WholeMeters) });
        }
        if (!relay)
        {
            // An individual course: forks (left from a relay) are run along branch A
            EditCourse single = Cur;
            course.start = single.start.HasValue ? WholeMeters(single.start.Value) : default;
            course.finish = single.finish.HasValue ? WholeMeters(single.finish.Value) : default;
            foreach (EditItem item in single.items)
            {
                if (item.fork < 0) course.controls.Add(WholeMeters(item.point));
                else if (forks[item.fork].branches.Count > 0) foreach (Vector3 point in forks[item.fork].branches[0]) course.controls.Add(WholeMeters(point));
            }
            AttachSides(course);
            return course;
        }

        course.kind = CourseData.KindRelay;
        course.legs = legs;
        course.forkEveryVariant = forkEveryVariant;
        // Only the courses and forks some leg uses are saved
        List<int> usedCourses = new List<int>();
        for (int leg = 0; leg < legs; leg++) if (!usedCourses.Contains(legCourse[leg])) usedCourses.Add(legCourse[leg]);
        List<int> usedForks = new List<int>();
        foreach (int ci in usedCourses)
        {
            foreach (EditItem item in courses[ci].items) if (item.fork >= 0 && !usedForks.Contains(item.fork)) usedForks.Add(item.fork);
        }
        usedForks.Sort();
        foreach (int f in usedForks)
        {
            RelayFork fork = new RelayFork();
            foreach (List<Vector3> branch in forks[f].branches)
            {
                RelayBranch relayBranch = new RelayBranch();
                foreach (Vector3 point in branch) relayBranch.controls.Add(WholeMeters(point));
                fork.branches.Add(relayBranch);
            }
            course.relayForks.Add(fork);
        }
        foreach (int ci in usedCourses)
        {
            EditCourse edit = courses[ci];
            RelayLegCourse relayCourse = new RelayLegCourse
            {
                start = edit.start.HasValue ? WholeMeters(edit.start.Value) : default,
                finish = edit.finish.HasValue ? WholeMeters(edit.finish.Value) : default
            };
            foreach (EditItem item in edit.items)
            {
                relayCourse.items.Add(item.fork < 0
                    ? new RelayItem { fork = -1, point = WholeMeters(item.point) }
                    : new RelayItem { fork = usedForks.IndexOf(item.fork) });
            }
            course.relayCourses.Add(relayCourse);
            // TV controls: common controls and the finish, each once
            if (TvAllowed)
            {
                foreach (EditItem item in edit.items)
                {
                    if (item.fork < 0 && item.tv && !course.HasTvControl(WholeMeters(item.point)) && course.tvControls.Count < CourseData.MaxTvControls)
                        course.tvControls.Add(WholeMeters(item.point));
                }
                if (edit.finishTv && edit.finish.HasValue && !course.HasTvControl(relayCourse.finish) && course.tvControls.Count < CourseData.MaxTvControls)
                    course.tvControls.Add(relayCourse.finish);
            }
        }
        for (int leg = 0; leg < legs; leg++) course.legCourse.Add(usedCourses.IndexOf(legCourse[leg]));
        char[] sexes = new char[legs];
        for (int leg = 0; leg < legs; leg++) sexes[leg] = CourseData.SexLetter(SexOf(leg));
        course.legSexes = new string(sexes).TrimEnd('a');
        if (course.legSexes.Length == 0) course.legSexes = null;
        course.RefreshMainFields();
        AttachSides(course);
        return course;
    }

    // ---- Inside or outside (2026-11-20) ----

    static bool SamePoint(CoursePoint a, CoursePoint b) => Math.Abs(a.e - b.e) < 1.5 && Math.Abs(a.n - b.n) < 1.5;

    // The chosen sides of the controls (and finishes) the course still has
    void AttachSides(CourseData course)
    {
        var used = new List<CoursePoint>();
        foreach (EditCourse c in courses)
        {
            foreach (EditItem item in c.items) if (item.fork < 0) used.Add(WholeMeters(item.point));
            if (c.finish.HasValue) used.Add(WholeMeters(c.finish.Value));
        }
        foreach (EditFork f in forks) foreach (List<Vector3> branch in f.branches) foreach (Vector3 p in branch) used.Add(WholeMeters(p));
        course.punchSides = new List<PunchSide>();
        foreach (PunchSide side in punchSides)
        {
            if (side == null || !used.Exists(p => SamePoint(p, side.control))) continue;
            if (course.punchSides.Exists(s => SamePoint(s.control, side.control))) continue;
            if (course.punchSides.Count < PunchSides.MaxSides) course.punchSides.Add(side);
        }
    }

    // A control (or the finish) just put right at a fence, a wall, a cliff or an out-of-bounds area could be punched
    // from either side: ask which, so every player in the lobby runs to the same side
    void AskSide(Vector3 world)
    {
        if (map == null || grid == null) return;
        CoursePoint point = WholeMeters(world);
        punchSides.RemoveAll(s => s == null || SamePoint(s.control, point));
        float[,] speed = grid.speed;
        var shapes = new List<ForbiddenArea>();
        foreach (List<Vector3> area in areas) if (area.Count >= 3) shapes.Add(new ForbiddenArea { corners = area.ConvertAll(WholeMeters) });
        foreach (List<Vector3> line in boundaries) if (line.Count >= 2) shapes.Add(new ForbiddenArea { line = true, corners = line.ConvertAll(WholeMeters) });
        foreach (List<Vector3> line in bridges) if (line.Count >= 2) shapes.Add(new ForbiddenArea { line = true, bridge = true, corners = line.ConvertAll(WholeMeters) });
        if (shapes.Count > 0)
        {
            speed = (float[,])grid.speed.Clone();
            ForbiddenAreas.Block(speed, map, shapes);
        }
        List<PunchSides.Option> options = PunchSides.Options(map, speed, point);
        if (options.Count < 2) return;
        var choices = new ChoicePopup.Option[options.Count];
        for (int i = 0; i < options.Count; i++)
            choices[i] = new ChoicePopup.Option { id = i.ToString(), title = options[i].title, description = options[i].description };
        // Until the course setter picks, the first side (the inside, when there is one) counts
        int first = options.FindIndex(o => o.title == "Inside");
        if (first < 0) first = 0;
        punchSides.Add(new PunchSide { control = point, at = options[first].at });
        ChoicePopup.Open("Inside or outside?", choices, first.ToString(), id =>
        {
            if (!int.TryParse(id, out int pick) || pick < 0 || pick >= options.Count) return;
            punchSides.RemoveAll(s => s == null || SamePoint(s.control, point));
            punchSides.Add(new PunchSide { control = point, at = options[pick].at });
            SetStatus("Every runner punches it from the side you picked: " + options[pick].title.ToLower() + ".", false);
        }, "The control is right at a fence, a wall, a cliff or an out-of-bounds area. Which side is it on? Everyone in the lobby gets the same.");
    }

    // Whole metres, so the course is exactly the same for the players who receive it in the lobby (CourseData.ToCompact)
    CoursePoint WholeMeters(Vector3 world)
    {
        CoursePoint point = map.FromWorld(world);
        return new CoursePoint(Math.Round(point.e), Math.Round(point.n));
    }

    // Returns null when the course is fine, otherwise what is wrong (and shows, marks and moves the camera to the bad
    // point). Finding the ways between the controls can take a while on a big 2 m grid, so it runs on a background
    // thread behind a loading screen.
    async Task<string> CheckCourseAsync()
    {
        errorPoint = null;
        if (map == null) return "There is no map yet. Import one first.";
        if (!relay)
        {
            EditCourse single = Cur;
            if (!single.start.HasValue) return "Place the start first.";
            if (single.items.Count == 0) return "Add at least one control.";
            if (!single.finish.HasValue) return "Place the finish.";
        }
        else
        {
            // Legs that should run a fork and were never opened get their copy now
            for (int leg = 0; leg < legs; leg++) FillEmptyLeg(leg, true);
            for (int leg = 0; leg < legs; leg++)
            {
                if (!MissingFork(leg, out int f)) continue;
                ShowLeg(leg);
                return "Leg " + (leg + 1) + " should run fork " + (f + 1) + ", but it has no control where the fork starts. Use \"Copy a leg\" to copy a leg with the fork, or press Edit on the fork and take leg " + (leg + 1) + " off it.";
            }
            if (forkEveryVariant)
            {
                for (int f = 0; f < forks.Count; f++)
                {
                    List<int> runs = LegsWithFork(f);
                    if (runs.Count == 0 || forks[f].branches.Count <= runs.Count) continue;
                    ShowLeg(runs[0]);
                    return "Fork " + (f + 1) + " has " + forks[f].branches.Count + " branches but only " + LegList(runs) + " run it. Every leg runs one branch: press Edit on the fork and pick more legs, or fewer branches.";
                }
            }
            for (int leg = 0; leg < legs; leg++)
            {
                EditCourse course = courses[legCourse[leg]];
                bool empty = !course.start.HasValue && !course.finish.HasValue && course.items.Count == 0;
                string missing = empty ? "has no course yet. Lay it, or use \"Copy a leg\" to copy another leg"
                    : !course.start.HasValue ? "has no start" : course.items.Count == 0 ? "has no controls" : !course.finish.HasValue ? "has no finish" : null;
                if (missing != null)
                {
                    ShowLeg(leg);
                    return "Leg " + (leg + 1) + " " + missing + ".";
                }
            }
        }

        CourseData built = BuildCourse();
        MapDefinition checkedMap = map;
        MapGrid checkedGrid = grid;
        CourseValidator.Result result;
        ShowLoading("Checking the course");
        try
        {
            result = await Task.Run(() => CourseValidator.Check(checkedMap, checkedGrid, built));
        }
        finally
        {
            HideLoading();
        }
        if (!result.ok)
        {
            if (relay && result.badLeg >= 0) ShowLeg(result.badLeg);
            if (result.hasBadPoint) errorPoint = map.ToWorld(result.badPoint);
            else if (!relay && result.badIndex >= 0) errorPoint = map.ToWorld(built.AllPoints()[Mathf.Clamp(result.badIndex, 0, built.AllPoints().Count - 1)]);
            if (errorPoint.HasValue)
            {
                Vector3 p = errorPoint.Value;
                mapCamera.transform.position = new Vector3(p.x, p.y, mapCamera.transform.position.z);
                return result.message + " It is marked in red on the map.";
            }
            return result.message;
        }
        return null;
    }

    // ---- Loading screen while the course is checked ----

    GameObject loadingOverlay;
    TextMeshProUGUI loadingText;
    string loadingMessage;

    void ShowLoading(string message)
    {
        loadingMessage = message;
        if (loadingOverlay == null)
        {
            Image dim = CourseUI.Panel("Loading", canvasObject.transform, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
            CourseUI.Stretch(dim.rectTransform);
            Image card = CourseUI.CardPanel("Card", dim.transform);
            CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 170));
            loadingText = CourseUI.Text("Text", card.transform, "", 30, CourseUI.Ink, FontStyles.Bold);
            loadingText.alignment = TextAlignmentOptions.Center;
            CourseUI.Stretch(loadingText.rectTransform, 24, 24, 20, 20);
            loadingOverlay = dim.gameObject;
        }
        loadingOverlay.SetActive(true);
        loadingOverlay.transform.SetAsLastSibling();
        busy = true;
    }

    void HideLoading()
    {
        if (loadingOverlay != null) loadingOverlay.SetActive(false);
        busy = false;
    }

    void LateUpdate()
    {
        if (loadingOverlay == null || !loadingOverlay.activeSelf) return;
        int dots = (int)(Time.unscaledTime * 2f) % 4;
        loadingText.text = loadingMessage + new string('.', dots) + new string(' ', 3 - dots) + "\n<size=20>The game is finding the way between every control.</size>";
    }

    void ShowLeg(int leg)
    {
        if (leg == selectedLeg) return;
        if (tool == Tool.Branch)
        {
            forkFirstRound = false;
            branchesDone.Clear();
        }
        selectedLeg = leg;
        checkLegs.Remove(leg);
        statusAfterSelect = null;
        dragging = null;
        SetTool(Cur.start.HasValue ? Tool.Control : Tool.Start);
    }

    string statusAfterSelect;

    async void OnSave()
    {
        if (string.IsNullOrWhiteSpace(nameField.text))
        {
            SetStatus("Give the course a name.", true);
            return;
        }
        if (map == null)
        {
            SetStatus("There is no map yet. Import one first.", true);
            return;
        }
        if (string.IsNullOrWhiteSpace(authorField.text))
        {
            SetStatus("Write your name, so others can see who made the course.", true);
            return;
        }
        if (busy) return;
        CloseOpenArea();
        string problem = await CheckCourseAsync();
        Redraw();
        if (problem != null)
        {
            SetStatus(problem + " The course was not saved.", true);
            return;
        }

        // The only limit on how many courses a player can have is Unity Cloud's 5 MB per player
        List<CourseData> afterSave = CourseLibrary.Mine();
        CourseData draft = BuildCourse();
        afterSave.RemoveAll(c => c.id == draft.id);
        afterSave.Add(draft);
        if (!CloudCourses.FitsInCloud(afterSave))
        {
            SetStatus("Your courses would be over Unity Cloud's 5 MB limit. Delete a course before saving a new one.", true);
            return;
        }

        PlayerPrefs.SetString(LocalPrefs.Key("PlayerNickName"), authorField.text.Trim());
        PlayerPrefs.Save();

        busy = true;
        saveButton.interactable = false;
        SetStatus("Saving...", false);
        try
        {
            await CloudCourses.SignInAsync();
        }
        catch (Exception)
        {
            // Saved locally below; publishing is tried again on the next save
        }
        CourseData course = BuildCourse();
        CourseLibrary.SaveMine(course);
        editing = course;
        try
        {
            await CloudCourses.PublishAsync(CourseLibrary.Mine());
            SetStatus("Saved and published. \"" + course.name + "\" is now in the course list in the lobby.", false);
        }
        catch (Exception e)
        {
            Debug.LogWarning("CourseBuilder: publish failed: " + e);
            SetStatus("Saved on this computer, but it could not be published: " + CloudCourses.Describe(e), true);
        }
        busy = false;
        saveButton.interactable = true;
        RefreshMyCourses();
    }

    void Edit(CourseData course)
    {
        MapDefinition courseMap = MapDefinition.Find(course.mapId);
        if (courseMap != map) SelectMap(courseMap);
        course.Normalize();
        relay = course.IsRelay();
        if (relay) legs = Mathf.Clamp(course.legs, CourseData.MinLegs, CourseData.MaxLegs);
        ClearCourse();
        editing = course;
        nameField.text = course.name;
        SetTerrain(course.IsSprint() ? CourseData.Sprint : CourseData.Forest);
        courses.Clear();
        legCourse.Clear();
        forks.Clear();
        removedGhosts.Clear();
        if (course.forbidden != null)
        {
            foreach (ForbiddenArea area in course.forbidden)
            {
                if (area == null || !area.IsValid) continue;
                (area.bridge ? bridges : area.line ? boundaries : areas).Add(area.corners.ConvertAll(p => map.ToWorld(p)));
            }
        }
        if (course.punchSides != null) foreach (PunchSide side in course.punchSides) if (side != null) punchSides.Add(side);
        if (relay)
        {
            forkEveryVariant = course.forkEveryVariant;
            foreach (RelayFork fork in course.relayForks)
            {
                EditFork edit = new EditFork();
                foreach (RelayBranch branch in fork.branches) edit.branches.Add(branch.controls.ConvertAll(p => map.ToWorld(p)));
                forks.Add(edit);
            }
            foreach (RelayLegCourse relayCourse in course.relayCourses)
            {
                EditCourse edit = new EditCourse
                {
                    start = map.ToWorld(relayCourse.start), finish = map.ToWorld(relayCourse.finish), finishTv = course.HasTvControl(relayCourse.finish)
                };
                foreach (RelayItem item in relayCourse.items)
                {
                    edit.items.Add(item.fork < 0
                        ? new EditItem { point = map.ToWorld(item.point), tv = course.HasTvControl(item.point) }
                        : new EditItem { fork = item.fork });
                }
                courses.Add(edit);
            }
            for (int leg = 0; leg < legs; leg++) legCourse.Add(course.LegCourseIndex(leg));
            legSex.Clear();
            for (int leg = 0; leg < legs; leg++) legSex.Add(course.SexOfLeg(leg));
            // The legs that run each fork are the legs that have it
            for (int f = 0; f < forks.Count; f++) forks[f].legs = LegsWithFork(f);
        }
        else
        {
            EditCourse edit = new EditCourse { start = map.ToWorld(course.start), finish = map.ToWorld(course.finish) };
            foreach (CoursePoint control in course.controls) edit.items.Add(new EditItem { point = map.ToWorld(control) });
            courses.Add(edit);
            legCourse.Add(0);
        }
        selectedLeg = 0;
        SetRelay(relay);
        SetTool(Tool.Control);
        SetStatus("Editing \"" + course.name + "\". Save to update it.", false);
        undo.Clear();
        RefreshUndoButton();
        Redraw();
    }

    async void Delete(CourseData course)
    {
        CourseLibrary.DeleteMine(course.id);
        if (editing != null && editing.id == course.id) editing = null;
        RefreshMyCourses();
        try
        {
            await CloudCourses.PublishAsync(CourseLibrary.Mine());
            SetStatus("\"" + course.name + "\" was deleted.", false);
        }
        catch (Exception e)
        {
            SetStatus("Deleted on this computer, but Unity Cloud could not be updated: " + CloudCourses.Describe(e), true);
        }
    }

    // ---- UI ----

    // The out-of-bounds area and boundary line buttons: pick that kind, or go back to laying the course
    void ToggleForbidden(bool line, bool bridge = false)
    {
        if (tool == Tool.Forbidden && drawLine == line && drawBridge == bridge)
        {
            SetTool(Cur.start.HasValue ? Tool.Control : Tool.Start);
            return;
        }
        if (tool == Tool.Forbidden) CloseOpenArea();
        drawLine = line;
        drawBridge = bridge;
        SetTool(Tool.Forbidden);
        Redraw();
    }

    void SetTool(Tool chosen)
    {
        if (chosen != Tool.Control) insertAfter = null;
        bool wasForbidden = tool == Tool.Forbidden;
        if (wasForbidden && chosen != Tool.Forbidden) CloseOpenArea();
        draggingCorner = false;
        moveType = null;
        tool = chosen;
        foreach (var pair in toolButtons)
        {
            CourseUI.SetButtonColor(pair.Value, pair.Key == tool ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        }
        if (forbiddenButton != null) CourseUI.SetButtonColor(forbiddenButton, tool == Tool.Forbidden && !drawLine ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        if (boundaryButton != null) CourseUI.SetButtonColor(boundaryButton, tool == Tool.Forbidden && drawLine && !drawBridge ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        if (bridgeButton != null) CourseUI.SetButtonColor(bridgeButton, tool == Tool.Forbidden && drawBridge ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        if (forkButton != null) CourseUI.SetButtonColor(forkButton, tool == Tool.Branch ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Secondary);
        if (tool != Tool.Branch)
        {
            forkFirstRound = false;
            branchesDone.Clear();
        }
        RefreshForkBar();
        if (areaRenderer != null && (wasForbidden || tool == Tool.Forbidden)) RedrawAreas();
        if (wasForbidden && tool != Tool.Forbidden) RefreshPointList();
        RefreshHint();
    }

    void RefreshHint()
    {
        if (hintText == null) return;
        EditCourse course = Cur;
        string text;
        if (moveType.HasValue) text = "Click on the map where " + moveName.ToLower() + " should go. Right-click to cancel.";
        else if (tool == Tool.Start) text = "Click on the map where the start should be.";
        else if (tool == Tool.Control) text = "Click on the map to add control " + (course.items.Count + 1) + ". Choose Finish when you are done."
                                             + (relay ? " Fork (G) splits the course into branches here." : "");
        else if (tool == Tool.Branch && branchFork >= 0 && branchFork < forks.Count && branchIndex < forks[branchFork].branches.Count)
            text = forkFirstRound
                ? "Click the first control of branch " + CourseData.BranchLetter(branchIndex) + " of fork " + (branchFork + 1) + ". The next click is the next branch."
                : "Click to add control " + (forks[branchFork].branches[branchIndex].Count + 1) + " of fork " + (branchFork + 1) + ", branch "
                  + CourseData.BranchLetter(branchIndex) + ". Press Branch done (Enter) when it is done, Esc to stop.";
        else if (tool == Tool.Forbidden && drawBridge)
            text = openArea == null ? "Click along the bridge, like drawing a path: over a lake, a stream, a fence or anything the runners should cross."
                : openArea.Count < 2 ? "Click point " + (openArea.Count + 1) + ". Backspace takes the last point back."
                : "Click point " + (openArea.Count + 1) + ", or click the last point again (or press Enter) to finish the bridge.";
        else if (tool == Tool.Forbidden && drawLine)
            text = openArea == null ? "Click the corners of an uncrossable boundary (a thick purple line, like a fence or wall the runners must not cross)."
                : openArea.Count < 2 ? "Click corner " + (openArea.Count + 1) + ". Backspace takes the last corner back."
                : "Click corner " + (openArea.Count + 1) + ", or click the last corner again (or press Enter) to finish the line.";
        else if (tool == Tool.Forbidden)
            text = openArea == null ? "Click the corners of an out-of-bounds area (purple stripes). The runners can not go in it."
                : openArea.Count < 3 ? "Click corner " + (openArea.Count + 1) + ". Backspace takes the last corner back."
                : "Click corner " + (openArea.Count + 1) + ", or click the first corner (or press Enter) to close the area.";
        else text = "Click on the map where the finish should be.";
        hintText.text = text + (tool == Tool.Forbidden
            ? "\nDrag a corner to move it, right-click it to remove it, right-click inside an area or on a line to remove it."
            : "\nDrag a mark to move it, right-click it to remove it. Click a line to add a control in between. Ctrl+Z undoes. Right-drag moves the map, the wheel zooms.");

        if (summaryText != null)
        {
            int forkCount = course.items.FindAll(item => item.fork >= 0).Count;
            string length = !relay && course.start.HasValue && course.finish.HasValue ? "  ·  " + BuildCourse().LengthText() + " straight line" : "";
            summaryText.text = relay
                ? "Leg " + (selectedLeg + 1) + "  ·  " + ControlCount(course) + " controls" + (forkCount > 0 ? ", " + forkCount + (forkCount == 1 ? " fork" : " forks") : "")
                : course.items.Count + " controls" + length;
            if (areas.Count > 0) summaryText.text += "  ·  " + areas.Count + (areas.Count == 1 ? " out-of-bounds area" : " out-of-bounds areas");
            if (boundaries.Count > 0) summaryText.text += "  ·  " + boundaries.Count + (boundaries.Count == 1 ? " boundary" : " boundaries");
            if (bridges.Count > 0) summaryText.text += "  ·  " + bridges.Count + (bridges.Count == 1 ? " bridge" : " bridges");
        }
    }

    void SetStatus(string message, bool isError)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = isError ? CourseUI.Danger : CourseUI.Forest;
    }

    void BuildUI()
    {
        canvasObject = new GameObject("Builder UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        ScreenSettings.Fit(scaler);
        if (FindObjectOfType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ---- The right panel: the tools and Save (2026-11-20: smaller; the list of controls moved to the left) ----
        Image panel = CourseUI.CardPanel("Tools panel", canvasObject.transform);
        panel.rectTransform.sizeDelta = new Vector2(RightPanelWidth, 1000f);
        // The buttons at the bottom must always be visible: the hint gives up room first when space runs short
        CourseUI.Column(panel.gameObject, 8, new RectOffset(24, 24, 18, 18));

        // The title, with -, + and > (smaller, bigger, slide into the edge)
        RectTransform titleRow = CourseUI.Rect("Title row", panel.transform);
        CourseUI.Row(titleRow.gameObject, 6, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(titleRow, 42);
        TextMeshProUGUI title = CourseUI.Text("Title", titleRow, "Course builder", 34, CourseUI.Ink, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.enableWordWrapping = false;
        title.enableAutoSizing = true;
        title.fontSizeMin = 22;
        title.fontSizeMax = 34;
        CourseUI.Size(title, 42, -1, 1);

        RectTransform mapRow = CourseUI.Rect("Map row", panel.transform);
        CourseUI.Row(mapRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(mapRow, 46);
        mapButton = CourseUI.Button("Map", mapRow, "Map", CourseUI.ButtonKind.Light, OpenMapPicker, 20);
        CourseUI.Size(mapButton, 46, -1, 1);
        Button importMap = CourseUI.Button("Import map", mapRow, "Import map", CourseUI.ButtonKind.Light, OpenMapImport, 19);
        CourseUI.Size(importMap, 46, 150);

        RectTransform typeRow = CourseUI.Rect("Type row", panel.transform);
        CourseUI.Row(typeRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(typeRow, 46);
        typeButton = CourseUI.Button("Type", typeRow, "Type: Individual", CourseUI.ButtonKind.Light, () =>
        {
            ChoiceDropdown.Open((RectTransform)typeButton.transform, new[] { "Individual", "Relay" }, relay ? 1 : 0, index =>
            {
                if ((index == 1) == relay) return;
                PushUndo();
                SetRelay(index == 1);
            });
        }, 20);
        CourseUI.Size(typeButton, 46, -1, 1);
        undoButton = CourseUI.Button("Undo", typeRow, "Undo", CourseUI.ButtonKind.Light, Undo, 19);
        CourseUI.Size(undoButton, 46, 150);
        undoButton.interactable = false;

        // Start, controls, fork (relays) and finish: what is used all the time
        RectTransform toolRow = CourseUI.Rect("Tools", panel.transform);
        CourseUI.Row(toolRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(toolRow, 54);
        toolButtons[Tool.Start] = CourseUI.Button("Start", toolRow, "Start", CourseUI.ButtonKind.Light, () => SetTool(Tool.Start), 22);
        toolButtons[Tool.Control] = CourseUI.Button("Control", toolRow, "Controls", CourseUI.ButtonKind.Light, () => SetTool(Tool.Control), 22);
        forkButton = CourseUI.Button("Fork", toolRow, "Fork", CourseUI.ButtonKind.Secondary, () =>
        {
            if (tool == Tool.Branch) StopForkDrawing();
            else OpenForkDialog(-1, null);
        }, 22);
        toolButtons[Tool.Finish] = CourseUI.Button("Finish", toolRow, "Finish", CourseUI.ButtonKind.Light, () => SetTool(Tool.Finish), 22);

        // Out-of-bounds areas, uncrossable boundaries and bridges
        RectTransform forbiddenRow = CourseUI.Rect("Out of bounds", panel.transform);
        CourseUI.Row(forbiddenRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(forbiddenRow, 42);
        forbiddenButton = CourseUI.Button("Out of bounds", forbiddenRow, "Out of bounds", CourseUI.ButtonKind.Light, () => ToggleForbidden(false), 17);
        boundaryButton = CourseUI.Button("Boundary", forbiddenRow, "Uncrossable", CourseUI.ButtonKind.Light, () => ToggleForbidden(true), 17);
        bridgeButton = CourseUI.Button("Bridge", forbiddenRow, "Bridge", CourseUI.ButtonKind.Light, () => ToggleForbidden(true, true), 17);
        HoverTip.Add(bridgeButton, "Draw a bridge like a path. The runners can cross water, a fence or anything else on it.");

        // The tool's own hint and the mouse help after it
        hintText = CourseUI.Text("Hint", panel.transform, "", 19, CourseUI.InkMuted);
        LayoutElement hintLayout = CourseUI.Size(hintText, 150);
        hintLayout.flexibleHeight = 1;
        hintLayout.minHeight = 60;
        hintText.enableAutoSizing = true;
        hintText.fontSizeMin = 14;
        hintText.fontSizeMax = 19;
        hintText.alignment = TextAlignmentOptions.TopLeft;

        statusText = CourseUI.Text("Status", panel.transform, "", 19, CourseUI.Forest, FontStyles.Bold);
        CourseUI.Size(statusText, 96);
        statusText.overflowMode = TextOverflowModes.Overflow;
        // Long messages shrink to fit the box instead of running over the buttons under it
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 13;
        statusText.fontSizeMax = 19;
        statusText.alignment = TextAlignmentOptions.TopLeft;

        // The course's name, the maker's name and forest/sprint are asked in the Save pop-up (2026-11-20)
        saveButton = CourseUI.Button("Save", panel.transform, "Save & publish...", CourseUI.ButtonKind.Primary, OpenSavePopup, 24);
        CourseUI.Size(saveButton, 58);

        // My courses: a button that opens a big pop-up with search and filters
        myCoursesButton = CourseUI.Button("My courses", panel.transform, "My courses", CourseUI.ButtonKind.Secondary, OpenMyCourses, 21);
        CourseUI.Size(myCoursesButton, 50);

        RectTransform bottomRow = CourseUI.Rect("Bottom", panel.transform);
        CourseUI.Row(bottomRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(bottomRow, 54);
        Button newCourse = CourseUI.Button("New", bottomRow, "New course", CourseUI.ButtonKind.Secondary, () => { PushUndo(); ClearCourse(); }, 21);
        CourseUI.Size(newCourse, -1, -1, 1.2f);
        // Opened from the multiplayer lobby: back to the lobby, the network game is still running
        if (LobbyVisit.Active) CourseUI.Button("Back", bottomRow, "Back to lobby", CourseUI.ButtonKind.Light, LobbyVisit.Close, 20);
        else CourseUI.Button("Back", bottomRow, "Back to menu", CourseUI.ButtonKind.Light, () => SceneLoader.Load("MainMenu", "Back to the menu"), 20);

        // ---- The left panel: the legs of a relay and the list of controls ----
        BuildLeftPanel(canvasObject.transform);

        rightFrame = SidePanelFrame.Attach(panel.rectTransform, true, "CourseBuilder.Tools", 0.9f, titleRow);
        rightFrame.changed = OnPanelsChanged;
        BuildSavePopup();
        AddBuilderTips(importMap, newCourse);

        // The label next to the mouse; it never takes clicks
        Image tip = CourseUI.Panel("Mouse label", canvasObject.transform, CourseUI.CardSolid);
        tip.raycastTarget = false;
        cursorTip = tip.rectTransform;
        cursorTip.anchorMin = cursorTip.anchorMax = Vector2.zero;
        cursorTip.pivot = new Vector2(0f, 1f);
        CourseUI.Row(tip.gameObject, 0, new RectOffset(14, 14, 8, 8));
        ContentSizeFitter fitter = tip.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        cursorTipText = CourseUI.Text("Text", tip.transform, "", 18, CourseUI.Ink, FontStyles.Bold);
        cursorTipText.enableWordWrapping = false;
        cursorTipText.overflowMode = TextOverflowModes.Overflow;
        tip.gameObject.SetActive(false);
    }

    // A panel got another size or was slid in or out: the fork bar keeps to the middle of the free map
    void OnPanelsChanged()
    {
        if (forkBar != null && forkBar.activeSelf) ((RectTransform)forkBar.transform).anchoredPosition = new Vector2((LeftRoom - RightRoom) / 2f, -24f);
    }

    void BuildLeftPanel(Transform canvas)
    {
        Image left = CourseUI.CardPanel("Course panel", canvas);
        left.rectTransform.sizeDelta = new Vector2(LeftPanelWidth, 1000f);
        CourseUI.Column(left.gameObject, 8, new RectOffset(22, 22, 18, 18));

        RectTransform head = CourseUI.Rect("Head", left.transform);
        CourseUI.Row(head.gameObject, 6, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(head, 42);
        listTitle = CourseUI.Text("Title", head, "Controls", 32, CourseUI.Ink, FontStyles.Bold);
        listTitle.alignment = TextAlignmentOptions.MidlineLeft;
        listTitle.enableWordWrapping = false;
        CourseUI.Size(listTitle, 42, -1, 1);

        // Relays: the number of legs, a button per leg (with a dot that tells how far it is), who may run the leg,
        // copying another leg, and the settings
        RectTransform section = CourseUI.Rect("Relay", left.transform);
        relaySection = section.gameObject;
        VerticalLayoutGroup sectionColumn = CourseUI.Column(relaySection, 8, new RectOffset(0, 0, 0, 0));
        sectionColumn.childControlHeight = true;

        RectTransform legsRow = CourseUI.Rect("Legs", section);
        CourseUI.Row(legsRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(legsRow, 44);
        Button minus = CourseUI.Button("Fewer", legsRow, "-", CourseUI.ButtonKind.Light, () => ChangeLegs(-1), 26);
        CourseUI.Size(minus, 44, 52);
        legsLabel = CourseUI.Text("Count", legsRow, "", 23, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(legsLabel, 44, 104);
        Button plus = CourseUI.Button("More", legsRow, "+", CourseUI.ButtonKind.Light, () => ChangeLegs(1), 26);
        CourseUI.Size(plus, 44, 52);
        relaySettingsButton = CourseUI.Button("Relay settings", legsRow, "Settings", CourseUI.ButtonKind.Light,
            () => RelaySettingsPopup.Open(() => forkEveryVariant, ToggleForkMode, () => showOtherLegs, () => { showOtherLegs = !showOtherLegs; Redraw(); }), 18);
        CourseUI.Size(relaySettingsButton, 44, -1, 1);
        HoverTip.Add(minus, "One leg fewer in the relay.");
        HoverTip.Add(plus, "One leg more in the relay.");

        legButtonRow = CourseUI.Rect("Leg buttons", section);
        GridLayoutGroup legGrid = legButtonRow.gameObject.AddComponent<GridLayoutGroup>();
        legGrid.cellSize = new Vector2(66, 50);
        legGrid.spacing = new Vector2(8, 8);
        legGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        legGrid.constraintCount = 5;
        CourseUI.Size(legButtonRow, 108);

        RectTransform legRow = CourseUI.Rect("Leg row", section);
        CourseUI.Row(legRow.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(legRow, 44);
        // Women's, men's or open leg (2026-11-13); W/M on the leg buttons
        legSexButton = CourseUI.Button("Leg runners", legRow, "Runners: Anyone", CourseUI.ButtonKind.Light, CycleLegSex, 18);
        copyLegButton = CourseUI.Button("Copy a leg", legRow, "Copy a leg...", CourseUI.ButtonKind.Light, OpenCopyMenu, 18);

        summaryText = CourseUI.Text("Summary", left.transform, "", 22, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(summaryText, 34);
        summaryText.enableWordWrapping = false;
        summaryText.enableAutoSizing = true;
        summaryText.fontSizeMin = 15;
        summaryText.fontSizeMax = 22;

        pointList = CourseUI.ScrollList("Points", left.transform, out _);
        CourseUI.Column(pointList.gameObject, 3, new RectOffset(0, 8, 0, 0));
        LayoutElement pointsLayout = CourseUI.Size(pointList.parent.GetComponent<Image>(), 200);
        pointsLayout.flexibleHeight = 1;
        pointsLayout.minHeight = 60;

        leftFrame = SidePanelFrame.Attach(left.rectTransform, false, "CourseBuilder.List", 0.85f, head);
        leftFrame.changed = OnPanelsChanged;
        relaySection.SetActive(relay);
    }

    // ---- Save & publish: the name, the maker and forest or sprint are asked here (2026-11-20) ----

    void BuildSavePopup()
    {
        Image dim = CourseUI.Panel("Save", canvasObject.transform, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
        CourseUI.Stretch(dim.rectTransform);
        savePopup = dim.gameObject;
        Image card = CourseUI.CardPanel("Card", dim.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 500));
        CourseUI.Column(card.gameObject, 14, new RectOffset(34, 34, 28, 28));
        CourseUI.Size(CourseUI.Text("Title", card.transform, "Save & publish", 34, CourseUI.Ink, FontStyles.Bold), 44);

        nameField = CourseUI.Input("Course name", card.transform, "Course name", CourseData.MaxNameLength, null);
        CourseUI.Size(nameField, 56);
        authorField = CourseUI.Input("Your name", card.transform, "Your name", CourseData.MaxAuthorLength, null);
        CourseUI.Size(authorField, 56);
        authorField.text = PlayerPrefs.GetString(LocalPrefs.Key("PlayerNickName"), GameManager.Instance != null ? GameManager.Instance.playerNickName : "");

        // Forest or sprint course; the map's type is the default
        RectTransform terrainRow = CourseUI.Rect("Course type", card.transform);
        CourseUI.Row(terrainRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(terrainRow, 48);
        TextMeshProUGUI terrainLabel = CourseUI.Text("Label", terrainRow, "Course type:", 21, CourseUI.Ink, FontStyles.Bold);
        terrainLabel.alignment = TextAlignmentOptions.MidlineLeft;
        forestButton = CourseUI.Button("Forest", terrainRow, "Forest", CourseUI.ButtonKind.Light, () => SetTerrain(CourseData.Forest), 20);
        sprintButton = CourseUI.Button("Sprint", terrainRow, "Sprint", CourseUI.ButtonKind.Light, () => SetTerrain(CourseData.Sprint), 20);
        SetTerrain(terrain);

        saveError = CourseUI.Text("Error", card.transform, "", 19, CourseUI.Danger, FontStyles.Bold);
        CourseUI.Size(saveError, 50).flexibleHeight = 1;

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        Button save = CourseUI.Button("Save", buttons, "Save & publish", CourseUI.ButtonKind.Primary, ConfirmSave, 24);
        CourseUI.Size(save, 58, -1, 2);
        Button cancel = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, () => savePopup.SetActive(false), 22);
        CourseUI.Size(cancel, 58, -1, 1);
        HoverTip.Add(nameField, "The course's name in the course lists.");
        HoverTip.Add(authorField, "Your name, shown as the maker of the course.");
        HoverTip.Add(forestButton, "Mark the course as a forest course. This decides which kind of runner speed and game rules it uses.");
        HoverTip.Add(sprintButton, "Mark the course as a sprint course: short, fast legs in a town or park.");
        HoverTip.Add(save, "Saves the course and shares it with the other players, so they can pick it in the lobby.");
        savePopup.SetActive(false);
    }

    void OpenSavePopup()
    {
        if (busy || savePopup == null) return;
        if (tool == Tool.Branch) StopForkDrawing();
        if (map == null)
        {
            SetStatus("There is no map yet. Import one first.", true);
            return;
        }
        saveError.text = "";
        savePopup.SetActive(true);
        savePopup.transform.SetAsLastSibling();
        if (string.IsNullOrWhiteSpace(nameField.text)) nameField.ActivateInputField();
    }

    void ConfirmSave()
    {
        if (string.IsNullOrWhiteSpace(nameField.text))
        {
            saveError.text = "Give the course a name.";
            return;
        }
        if (string.IsNullOrWhiteSpace(authorField.text))
        {
            saveError.text = "Write your name, so others can see who made the course.";
            return;
        }
        savePopup.SetActive(false);
        OnSave();
    }

    // Hover texts for the course builder's side panels
    void AddBuilderTips(Button importMap, Button newCourse)
    {
        HoverTip.Add(undoButton, "Takes back your last change. Ctrl+Z does the same.");
        HoverTip.Add(importMap, "Bring a new map into the game from an OCAD or OpenOrienteering Mapper file. You choose the terrain speeds and look when you import it.");
        HoverTip.Add(mapButton, "Choose which map you lay the course on.");
        HoverTip.Add(typeButton, "Click to switch. Individual: one course that everyone runs. Relay: several legs, each with its own course, and forks if you want them.");
        HoverTip.Add(toolButtons[Tool.Start], "Click on the map to place the start triangle.");
        HoverTip.Add(toolButtons[Tool.Control], "Click on the map to add the next control. Drag a control to move it, click on the line to add one in between.");
        HoverTip.Add(forkButton, "Split the course into branches after the last control (or the control you picked). You choose how many branches and which legs run them. G does the same.");
        HoverTip.Add(toolButtons[Tool.Finish], "Click on the map to place the finish.");
        HoverTip.Add(forbiddenButton, "Draw an area that is out of bounds. Runners never run through it, like a private garden or a marked-off area.");
        HoverTip.Add(boundaryButton, "Draw a line that cannot be crossed, such as a wall or a fence that runners have to go around.");
        HoverTip.Add(saveButton, "Name the course, choose forest or sprint, and share it with the other players.");
        HoverTip.Add(myCoursesButton, "Open the list of the courses you have made. You can edit or delete them.");
        HoverTip.Add(newCourse, "Clear everything and start a new course. You can undo it.");
        HoverTip.Add(copyLegButton, "Make this leg a copy of another leg, or empty it (start and finish stay). Forks that are not on this leg run straight along branch A.");
        HoverTip.Add(legSexButton, "Who may run this leg: anyone, only women or only men. Click to change.");
        HoverTip.Add(relaySettingsButton, "Fork mode (every team runs every branch, or random branches) and how the other legs show on the map.");
    }

    void RefreshPointList()
    {
        if (pointList == null) return;
        for (int c = pointList.childCount - 1; c >= 0; c--)
        {
            GameObject child = pointList.GetChild(c).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        EditCourse course = Cur;

        // A leg that should run a fork it doesn't have: what is wrong and the two ways to put it right
        if (relay && MissingFork(selectedLeg, out int missing))
        {
            List<int> having = LegsWithFork(missing);
            int copyFrom = having.Count > 0 ? having[0] : -1;
            AddRow("<color=#C0392B><b>Fork " + (missing + 1) + " should be on this leg,</b> but the leg has no control where the fork starts.</color>", 56, CourseUI.Ink, FontStyles.Normal, 6);
            List<(string, CourseUI.ButtonKind, UnityEngine.Events.UnityAction)> fixes = new List<(string, CourseUI.ButtonKind, UnityEngine.Events.UnityAction)>();
            if (copyFrom >= 0) fixes.Add(("Copy leg " + (copyFrom + 1), CourseUI.ButtonKind.Secondary, () => CopyLeg(copyFrom)));
            fixes.Add(("Take this leg off", CourseUI.ButtonKind.Light, () =>
            {
                PushUndo();
                forks[missing].legs.Remove(selectedLeg);
                SetStatus("Leg " + (selectedLeg + 1) + " no longer runs fork " + (missing + 1) + ".", false);
                Redraw();
            }));
            AddRowWith(null, "", 36, CourseUI.Ink, FontStyles.Normal, 6, false, fixes.ToArray());
        }

        Vector3? previous = course.start;
        if (course.start.HasValue)
        {
            AddRowAt(course.start.Value, "Start", 34, MovingColor(MarkType.Start, -1), FontStyles.Normal, 6,
                ("Move", CourseUI.ButtonKind.Light, () => StartMove(MarkType.Start, -1, "The start")),
                ("Remove", CourseUI.ButtonKind.Light, () => { PushUndo(); course.start = null; Redraw(); }));
        }
        int number = 0;
        for (int i = 0; i < course.items.Count; i++)
        {
            int index = i;
            EditItem item = course.items[i];
            number++;
            if (item.fork < 0)
            {
                string text = number + Meters(previous, item.point);
                bool bad = errorPoint.HasValue && Vector2.Distance(errorPoint.Value, item.point) < 0.08f;
                string controlName = "Control " + number;
                Color rowColor = bad ? CourseUI.Danger : MovingColor(MarkType.Control, index);
                bool last = index == course.items.Count - 1;
                bool forkAfter = !last && course.items[index + 1].fork < 0;
                var buttons = new List<(string label, CourseUI.ButtonKind kind, UnityEngine.Events.UnityAction action)>();
                if (relay && forkAfter) buttons.Add(("Fork", CourseUI.ButtonKind.Secondary, () => OpenForkDialog(-1, index)));
                if (last) buttons.Add(("Finish", CourseUI.ButtonKind.Light, () => MakeFinish(index)));
                buttons.Add(("Move", CourseUI.ButtonKind.Light, () => StartMove(MarkType.Control, index, controlName)));
                buttons.Add(("Remove", CourseUI.ButtonKind.Light, () => { PushUndo(); KeepGhost(course, item.point); course.items.RemoveAt(index); Redraw(); }));
                // The TV button always shows, last in the row, so it stays in the same place when the other buttons
                // come up on hover (2026-11-20, the user: a quick click on it hit Remove)
                if (relay) buttons.Add(("TV", item.tv ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light, () => ToggleTv(item, null)));
                AddRowAt(item.point, text, 34, rowColor, bad ? FontStyles.Bold : FontStyles.Normal, 6, buttons.ToArray());
                if (relay && item.tv) AddTvRow(item.point);
                previous = item.point;
                continue;
            }

            int f = item.fork;
            EditFork fork = forks[f];
            List<int> forkLegs = PlannedLegs(f);
            bool locked = ForkLocked(f);
            int home = HomeLeg(f);
            if (locked)
            {
                // Laid on another leg: shown here, changed there
                AddRowWith(null, "<b>Fork " + (f + 1) + "</b>  <color=#5B7563>" + fork.branches.Count + " branches, laid on leg " + (home + 1) + "</color>", 36, CourseUI.Ink, FontStyles.Normal, 6, true,
                    ("Go to leg " + (home + 1), CourseUI.ButtonKind.Secondary, () => SelectLeg(home)),
                    ("Take off", CourseUI.ButtonKind.Light, () => TakeForkOff(f)));
            }
            else
            {
                AddRowWith(null, "<b>Fork " + (f + 1) + "</b>  <color=#5B7563>" + fork.branches.Count + " branches, " + LegList(forkLegs) + "</color>", 36, CourseUI.Ink, FontStyles.Normal, 6, true,
                    ("Edit", CourseUI.ButtonKind.Secondary, () => OpenForkDialog(f, null)),
                    ("Draw", CourseUI.ButtonKind.Light, () => BeginForkDrawing(f, false)),
                    ("Delete", CourseUI.ButtonKind.Light, () => DeleteFork(f)));
            }
            if (forkEveryVariant && fork.branches.Count != forkLegs.Count)
            {
                string warning = fork.branches.Count > forkLegs.Count
                    ? "<color=#C0392B>More branches than legs: every leg runs one branch. Press Edit and pick " + fork.branches.Count + " legs.</color>"
                    : "<color=#8A6A00>Fewer branches than legs: some teams run a branch twice. Press Edit to pick " + fork.branches.Count + " legs.</color>";
                AddRow(warning, 48, CourseUI.Ink, FontStyles.Normal, 24);
            }
            for (int b = 0; b < fork.branches.Count; b++)
            {
                int branch = b;
                List<string> names = new List<string>();
                for (int k = 0; k < fork.branches[b].Count; k++) names.Add(number + CourseData.BranchLetter(b) + (k > 0 ? (k + 1).ToString() : ""));
                string text = CourseData.BranchLetter(b) + ":  " + (names.Count > 0 ? string.Join(", ", names) : "<i>straight on</i>");
                bool active = tool == Tool.Branch && branchFork == f && branchIndex == b;
                Vector3? focus = fork.branches[b].Count > 0 ? fork.branches[b][0] : (Vector3?)null;
                if (locked)
                {
                    AddRowWith(focus, "<color=#5B7563>" + text + "</color>", 30, CourseUI.Ink, FontStyles.Normal, 26, true);
                    continue;
                }
                AddRowWith(focus, text, 30, active ? CourseUI.Accent : CourseUI.Ink, active ? FontStyles.Bold : FontStyles.Normal, 26, true,
                    ("+ Control", CourseUI.ButtonKind.Light, () => StartBranch(f, branch)),
                    ("Remove", CourseUI.ButtonKind.Light, () => RemoveBranch(f, branch)));
            }
            previous = null;
        }
        if (course.finish.HasValue)
        {
            var buttons = new List<(string label, CourseUI.ButtonKind kind, UnityEngine.Events.UnityAction action)>();
            buttons.Add(("Move", CourseUI.ButtonKind.Light, () => StartMove(MarkType.Finish, -1, "The finish")));
            buttons.Add(("Remove", CourseUI.ButtonKind.Light, () => { PushUndo(); course.finish = null; Redraw(); }));
            if (relay) buttons.Add(("TV", course.finishTv ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light, () => ToggleTv(null, course)));
            AddRowAt(course.finish.Value, "Finish" + Meters(previous, course.finish.Value), 34, MovingColor(MarkType.Finish, -1), FontStyles.Normal, 6, buttons.ToArray());
            if (relay && course.finishTv) AddTvRow(course.finish.Value);
        }
        for (int a = 0; a < areas.Count; a++)
        {
            int index = a;
            AddRow("<color=#8A0A94>Out-of-bounds area " + (a + 1) + "</color>  <color=#5B7563>" + areas[a].Count + " corners</color>", 38, CourseUI.Ink, FontStyles.Normal, 6,
                ("Remove", CourseUI.ButtonKind.Light, () => { PushUndo(); areas.RemoveAt(index); Redraw(); }));
        }
        for (int b = 0; b < boundaries.Count; b++)
        {
            int index = b;
            AddRow("<color=#8A0A94>Uncrossable boundary " + (b + 1) + "</color>  <color=#5B7563>" + boundaries[b].Count + " corners</color>", 38, CourseUI.Ink, FontStyles.Normal, 6,
                ("Remove", CourseUI.ButtonKind.Light, () => { PushUndo(); boundaries.RemoveAt(index); Redraw(); }));
        }
        for (int b = 0; b < bridges.Count; b++)
        {
            int index = b;
            AddRow("<color=#8A0A94>Bridge " + (b + 1) + "</color>  <color=#5B7563>" + bridges[b].Count + " points</color>", 38, CourseUI.Ink, FontStyles.Normal, 6,
                ("Remove", CourseUI.ButtonKind.Light, () => { PushUndo(); bridges.RemoveAt(index); Redraw(); }));
        }
    }

    // ---- TV controls (relays on forest maps) ----

    // A relay on a forest map whose package has the 3D layer (maps imported before it have to be imported again)
    bool TvAllowed => relay && terrain != CourseData.Sprint && map != null && !map.IsSprint;

    void ToggleTv(EditItem item, EditCourse finishOf)
    {
        bool on = item != null ? item.tv : finishOf.finishTv;
        if (!on)
        {
            if (!TvAllowed)
            {
                SetStatus(terrain == CourseData.Sprint || (map != null && map.IsSprint)
                    ? "TV controls are only for forest maps so far." : "TV controls are only for relays.", true);
                return;
            }
            if (!TvWorldData.HasWorld(map))
            {
                SetStatus("This map has no 3D layer for TV controls yet. Import the map again to get it.", true);
                return;
            }
            int count = 0;
            foreach (EditCourse c in courses)
            {
                foreach (EditItem i in c.items) if (i.fork < 0 && i.tv) count++;
                if (c.finishTv) count++;
            }
            if (count >= CourseData.MaxTvControls)
            {
                SetStatus("A relay can have at most " + CourseData.MaxTvControls + " TV controls.", true);
                return;
            }
        }
        PushUndo();
        if (item != null) item.tv = !on;
        else finishOf.finishTv = !on;
        if (!on) SetStatus("TV control: during the race a small 3D view shows the runners at it. Click View to see it.", false);
        Redraw();
    }

    void AddTvRow(Vector3 point)
    {
        AddRowWith(point, "<color=#E8742A>TV camera here</color>", 32, CourseUI.Ink, FontStyles.Bold, 34, false,
            ("View", CourseUI.ButtonKind.Secondary, () => TvViewer.OpenPreview(map, map.FromWorld(point))));
    }

    // The row of the mark being moved is shown in the accent colour
    Color MovingColor(MarkType type, int item)
    {
        return moveType == type && (type != MarkType.Control || moveItem == item) ? CourseUI.Accent : CourseUI.Ink;
    }

    static string Meters(Vector3? from, Vector3 to)
    {
        if (!from.HasValue) return "";
        float meters = Vector2.Distance(from.Value, to) / MapDefinition.WorldUnitsPerMeter;
        return "  <color=#5B7563>" + Mathf.RoundToInt(meters) + " m</color>";
    }

    // A row of the list. Its buttons only show while the mouse is on the row (2026-11-20), except TV, which always shows;
    // a click on the row shows its control on the map.
    void AddRow(string text, float height, Color color, FontStyles style, int indent, params (string label, CourseUI.ButtonKind kind, UnityEngine.Events.UnityAction action)[] buttons)
    {
        AddRowWith(null, text, height, color, style, indent, true, buttons);
    }

    void AddRowAt(Vector3 focus, string text, float height, Color color, FontStyles style, int indent, params (string label, CourseUI.ButtonKind kind, UnityEngine.Events.UnityAction action)[] buttons)
    {
        AddRowWith(focus, text, height, color, style, indent, true, buttons);
    }

    void AddRowWith(Vector3? focus, string text, float height, Color color, FontStyles style, int indent, bool hoverButtons, params (string label, CourseUI.ButtonKind kind, UnityEngine.Events.UnityAction action)[] buttons)
    {
        RectTransform row = CourseUI.Rect("Row", pointList);
        CourseUI.Row(row.gameObject, 5, new RectOffset(indent, 4, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(row, height);
        BuilderRowHover hover = BuilderRowHover.Attach(row.gameObject);
        if (focus.HasValue)
        {
            Vector3 point = focus.Value;
            hover.clicked = () => FocusOn(point);
        }
        if (text.Length > 0 || buttons.Length == 0)
        {
            TextMeshProUGUI label = CourseUI.Text("Label", row, text, 18, color, style);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = height > 40;
            LayoutElement labelSize = CourseUI.Size(label, height, -1, 1);
            labelSize.minWidth = 60;
        }
        foreach (var button in buttons)
        {
            Button made = CourseUI.Button(button.label, row, button.label, button.kind, button.action, 15);
            // Wide enough for the whole word (2026-11-20: "TV" showed as "T" and "Remove" as "Remov")
            float width = button.label.Length <= 2 ? 50 : 9 * button.label.Length + 24;
            CourseUI.Size(made, height - 4, width);
            TextMeshProUGUI buttonText = made.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null) CourseUI.Stretch(buttonText.rectTransform, 6, 6, 0, 0);
            if (hoverButtons && button.label != "TV") hover.hoverOnly.Add(made.gameObject);
        }
    }

    // ---- My courses: a big pop-up with search, forest/sprint filters, Edit and Delete ----

    GameObject coursesPopup;
    RectTransform coursesList;
    TMP_InputField coursesSearch;
    TextMeshProUGUI coursesInfo;
    string coursesFilter = "all";
    string pendingDelete;          // id of the course whose Delete was pressed once; the second press deletes it
    readonly Dictionary<string, Button> coursesFilterButtons = new Dictionary<string, Button>();

    // The button text, and the pop-up's list if it is open
    void RefreshMyCourses()
    {
        List<CourseData> mine = CourseLibrary.Mine();
        if (myCoursesButton != null) CourseUI.SetButtonText(myCoursesButton, "My courses  <size=80%>(" + mine.Count + " saved)</size>");
        FillMyCourses();
    }

    void OpenMyCourses()
    {
        if (busy || MapImportPanel.IsOpen || mapPicker != null || coursesPopup != null) return;
        pendingDelete = null;
        Image dim = CourseUI.Panel("My courses", canvasObject.transform, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
        CourseUI.Stretch(dim.rectTransform);
        coursesPopup = dim.gameObject;
        Image card = CourseUI.CardPanel("Card", dim.transform);
        CourseUI.Stretch(card.rectTransform, 120, 120, 60, 60);
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 28, 28));

        RectTransform head = CourseUI.Rect("Head", card.transform);
        CourseUI.Row(head.gameObject, 16, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(head, 50);
        TextMeshProUGUI title = CourseUI.Text("Title", head, "My courses", 38, CourseUI.Ink, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(title, 50, 260);
        coursesInfo = CourseUI.Text("Info", head, "", 20, CourseUI.InkMuted);
        coursesInfo.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(coursesInfo, 50, -1, 1);
        CourseUI.Size(CourseUI.Button("Close", head, "Close", CourseUI.ButtonKind.Light, CloseMyCourses, 22), 46, 150);

        RectTransform tools = CourseUI.Rect("Search and filters", card.transform);
        CourseUI.Row(tools.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(tools, 52);
        coursesSearch = CourseUI.Input("Search", tools, "Search name, map or author", 40, _ => FillMyCourses());
        CourseUI.Size(coursesSearch, 52, -1, 1);
        coursesFilterButtons.Clear();
        foreach (var f in new[] { ("all", "All"), ("forest", "Forest"), ("sprint", "Sprint"), ("relay", "Relays") })
        {
            string key = f.Item1;
            Button filter = CourseUI.Button(f.Item2, tools, f.Item2, CourseUI.ButtonKind.Light, () => { coursesFilter = key; pendingDelete = null; FillMyCourses(); }, 20);
            CourseUI.Size(filter, 52, 130);
            coursesFilterButtons[key] = filter;
        }

        coursesList = CourseUI.ScrollList("Courses", card.transform, out ScrollRect scroll);
        scroll.scrollSensitivity = 60f;
        CourseUI.Column(coursesList.gameObject, 6, new RectOffset(0, 12, 0, 0));
        CourseUI.Size(coursesList.parent.GetComponent<Image>(), 400).flexibleHeight = 1;
        FillMyCourses();
    }

    void FillMyCourses()
    {
        if (coursesList == null) return;
        foreach (var pair in coursesFilterButtons)
            CourseUI.SetButtonColor(pair.Value, pair.Key == coursesFilter ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light);
        for (int i = coursesList.childCount - 1; i >= 0; i--) Destroy(coursesList.GetChild(i).gameObject);

        List<CourseData> mine = CourseLibrary.Mine();
        // How much of Unity Cloud's 5 MB the published courses use
        float usedKb = CloudCourses.PublishedBytes(mine) / 1024f;
        coursesInfo.text = mine.Count + " saved  ·  " + (usedKb < 1024f ? Mathf.CeilToInt(usedKb) + " kB" : (usedKb / 1024f).ToString("0.0") + " MB") + " of 5 MB used";

        mine.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));   // newest first
        string search = coursesSearch != null ? coursesSearch.text.Trim().ToLowerInvariant() : "";
        int shown = 0;
        foreach (CourseData course in mine)
        {
            bool sprint = course.IsSprint();
            if (coursesFilter == "forest" && sprint) continue;
            if (coursesFilter == "sprint" && !sprint) continue;
            if (coursesFilter == "relay" && !course.IsRelay()) continue;
            MapDefinition courseMap = MapDefinition.All.Find(m => m.id == course.mapId);
            string mapName = courseMap != null ? courseMap.displayName : "map not on this computer";
            if (search.Length > 0 && !((course.name ?? "") + " " + mapName + " " + (course.author ?? "")).ToLowerInvariant().Contains(search)) continue;
            AddCourseRow(course, mapName, sprint);
            shown++;
        }
        if (shown == 0)
        {
            string none = mine.Count == 0 ? "Nothing saved yet. Lay a course and press Save & publish." : "No courses match.";
            CourseUI.Size(CourseUI.Text("None", coursesList, none, 22, CourseUI.InkMuted, FontStyles.Italic), 44);
        }
    }

    void AddCourseRow(CourseData course, string mapName, bool sprint)
    {
        CourseData captured = course;
        Image row = CourseUI.Panel("Course " + course.name, coursesList, CourseUI.Soft);
        CourseUI.Row(row.gameObject, 12, new RectOffset(18, 12, 6, 6)).childForceExpandWidth = false;
        CourseUI.Size(row, 70);

        string facts = course.IsRelay() ? "Relay, " + course.legs + " legs" : course.controls.Count + " controls  ·  " + course.LengthText();
        string date = course.createdAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(course.createdAt).LocalDateTime.ToString("yyyy-MM-dd") : "";
        TextMeshProUGUI label = CourseUI.Text("Label", row.transform,
            "<b>" + course.name + "</b>\n<size=78%><color=#5B7563>" + mapName + "  ·  " + facts + (date.Length > 0 ? "  ·  " + date : "") + "</color></size>",
            22, CourseUI.Ink);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        CourseUI.Size(label, 58, -1, 1);

        // Forest or sprint as a coloured tag
        Image tag = CourseUI.Panel("Type", row.transform, sprint ? CourseUI.Hex("E8742A", 0.18f) : CourseUI.Hex("3F7D54", 0.18f));
        CourseUI.Size(tag, 36, 100);
        TextMeshProUGUI tagText = CourseUI.Text("Text", tag.transform, sprint ? "Sprint" : "Forest", 18, sprint ? CourseUI.Accent : CourseUI.Forest, FontStyles.Bold);
        tagText.alignment = TextAlignmentOptions.Center;
        CourseUI.Stretch(tagText.rectTransform);

        Button edit = CourseUI.Button("Edit", row.transform, "Edit", CourseUI.ButtonKind.Secondary, () => { CloseMyCourses(); Edit(captured); }, 20);
        CourseUI.Size(edit, 46, 110);
        bool confirming = pendingDelete == course.id;
        Button delete = CourseUI.Button("Delete", row.transform, confirming ? "Sure? Delete" : "Delete", CourseUI.ButtonKind.Danger, () =>
        {
            if (pendingDelete != captured.id)
            {
                pendingDelete = captured.id;
                FillMyCourses();
                return;
            }
            pendingDelete = null;
            Delete(captured);
        }, 20);
        CourseUI.Size(delete, 46, confirming ? 150 : 110);
    }

    void CloseMyCourses()
    {
        if (coursesPopup != null) Destroy(coursesPopup);
        coursesPopup = null;
        coursesList = null;
        coursesSearch = null;
        coursesInfo = null;
        pendingDelete = null;
    }
}
