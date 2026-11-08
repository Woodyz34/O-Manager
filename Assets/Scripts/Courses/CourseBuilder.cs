using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The course builder page: pick a map, click out the start, the controls and the finish, check that every
// control can be reached, then save the course and publish it so other players can run it.
//
// Mouse: left click places a mark (or drags one that is already there), right click on a mark removes it,
// right or middle drag moves the map, the wheel zooms.
//
// A course can also be a relay (the panel on the left): up to 10 legs, each with its own course (or the same course
// as the leg before). A control can be turned into a fork: a stretch between the common control before and the common
// control after it that can be run along different branches, each with any number of controls (or none). The forks
// of other legs are shown faded; clicking one puts the whole fork, with its common controls, on the leg being laid.
// A fork on several legs is "Farsta" forked: every team runs every branch once over those legs.
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
    }

    class EditCourse
    {
        public Vector3? start;
        public List<EditItem> items = new List<EditItem>();
        public Vector3? finish;
    }

    class EditFork
    {
        public List<List<Vector3>> branches = new List<List<Vector3>>();
    }

    readonly List<EditCourse> courses = new List<EditCourse> { new EditCourse() };
    readonly List<EditFork> forks = new List<EditFork>();
    readonly List<int> legCourse = new List<int>();   // relay: the course each leg runs
    bool relay;
    string terrain = CourseData.Forest;   // forest or sprint course, for sorting and for mistakes
    int legs = 3;
    bool forkEveryVariant = true;
    int selectedLeg;

    EditCourse Cur => courses[relay && selectedLeg < legCourse.Count ? legCourse[selectedLeg] : 0];

    CourseData editing;            // the saved course being changed, or null for a new one
    Tool tool = Tool.Start;
    int branchFork = -1, branchIndex = -1;   // where the Branch tool adds controls
    Vector3? errorPoint;

    // Out-of-bounds areas, for the whole course (every leg of a relay): finished ones, and the one being clicked out
    readonly List<List<Vector3>> areas = new List<List<Vector3>>();
    readonly List<List<Vector3>> boundaries = new List<List<Vector3>>();   // uncrossable boundary lines
    List<Vector3> openArea;
    bool openIsLine;          // the open shape is a boundary line
    bool drawLine;            // the Forbidden tool draws boundary lines, not areas
    Button boundaryButton;

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
    }

    readonly List<Mark> marks = new List<Mark>();
    readonly List<Mark> shadows = new List<Mark>();
    Mark dragging;
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
    Image relayPanel;
    TextMeshProUGUI legsLabel;
    RectTransform legButtonRow;
    Button sameCourseButton;
    Button forkModeButton;
    TextMeshProUGUI relayInfo;
    GameObject forkPopup;
    GameObject canvasObject;

    const float PanelWidth = 560f;

    void Start()
    {
        if (mapCamera == null) mapCamera = Camera.main;
        BuildUI();
        List<MapDefinition> maps = MapDefinition.All;
        if (maps.Count == 0)
        {
            SetStatus("No maps found in Resources/MapDefs.", true);
            return;
        }
        SelectMap(maps[0]);
        RefreshMyCourses();
        // Sign in early so publishing is quick later; failure is shown when saving
        _ = CloudCourses.SignInAsync();
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
        }
        courseRenderer.sizeFactor = CourseRenderer.SizeFor(map);
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
        // Leave room for the panel on the right
        // The canvas is scaled so all of 1920x1080 fits (ScreenSettings), so on a wide screen it is wider than 1920
        float uiScale = Mathf.Min(Screen.width / ScreenSettings.ReferenceResolution.x, Screen.height / ScreenSettings.ReferenceResolution.y);
        float canvasWidth = Screen.width / Mathf.Max(0.01f, uiScale);
        float visibleWidthShare = 1f - PanelWidth / Mathf.Max(1f, canvasWidth);
        mapCamera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / (mapCamera.aspect * visibleWidthShare)) * 1.02f;
        float worldPanel = mapCamera.orthographicSize * mapCamera.aspect * 2f * (1f - visibleWidthShare);
        mapCamera.transform.position = new Vector3(bounds.center.x + worldPanel / 2f, bounds.center.y, -10f);
    }

    // ---- Input ----

    void Update()
    {
        if (map == null || busy || forkPopup != null || MapImportPanel.IsOpen || MapEditPanel.IsOpen || mapPicker != null || coursesPopup != null) return;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                      && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        Vector3 mouseWorld = mapCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = -1f;

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

        if (tool == Tool.Forbidden)
        {
            UpdateForbidden(mouseWorld, overUI, typing);
            return;
        }

        if (moveType.HasValue)
        {
            if (Input.GetMouseButtonDown(0) && !overUI) FinishMove(mouseWorld);
            else if (Input.GetMouseButtonUp(1) && !panning && !overUI) CancelMove();
            return;
        }

        // Right click (without dragging) on a mark removes it
        if (Input.GetMouseButtonUp(1) && !panning && !overUI)
        {
            Mark hit = Hit(marks, mouseWorld);
            if (hit != null) RemoveMark(hit);
        }

        // Left click: drag a mark that is already there, use a faded fork, or place a new mark
        if (Input.GetMouseButtonDown(0) && !overUI)
        {
            Mark hit = tool == Tool.Branch ? null : Hit(marks, mouseWorld);
            Mark shadow = relay && hit == null ? Hit(shadows, mouseWorld, 1.6f) : null;
            if (hit != null)
            {
                dragging = hit;
            }
            else if (shadow != null)
            {
                AskUseFork(shadow.fork, mouseWorld);
            }
            else
            {
                Place(mouseWorld);
            }
        }
        if (dragging != null)
        {
            if (Input.GetMouseButton(0))
            {
                SetMarkPosition(dragging, mouseWorld);
                Redraw();
            }
            else
            {
                // Dropped: it must be somewhere a runner can stand
                string problem = CheckPlace(dragging.position);
                errorPoint = null;
                if (problem != null)
                {
                    SetStatus(MarkName(dragging) + ": " + problem, true);
                    errorPoint = dragging.position;
                }
                else
                {
                    SetStatus("", false);
                }
                dragging = null;
                Redraw();
            }
        }
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
    // The shapes are numbered areas first, then lines.

    int ShapeCount => areas.Count + boundaries.Count;
    List<Vector3> Shape(int i) => i < areas.Count ? areas[i] : boundaries[i - areas.Count];
    bool ShapeIsLine(int i) => i >= areas.Count;

    void RemoveShape(int i)
    {
        if (i < areas.Count) areas.RemoveAt(i);
        else boundaries.RemoveAt(i - areas.Count);
    }

    string OpenName => openIsLine ? "uncrossable boundary" : "out-of-bounds area";

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
                if (hit >= 0)
                {
                    boundaries.RemoveAt(hit);
                    SetStatus("The uncrossable boundary was removed.", false);
                }
                else
                {
                    int inside = areas.FindLastIndex(area => ForbiddenAreas.Contains(area, mouseWorld));
                    if (inside >= 0)
                    {
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
            if (ShapeCount >= ForbiddenArea.MaxAreas)
            {
                SetStatus("A course can have at most " + ForbiddenArea.MaxAreas + " out-of-bounds areas and boundaries together.", true);
                return;
            }
            openArea = new List<Vector3>();
            openIsLine = drawLine;
        }
        if (openArea.Count >= ForbiddenArea.MaxCorners)
        {
            SetStatus("An " + OpenName + " can have at most " + ForbiddenArea.MaxCorners + " corners. "
                + (openIsLine ? "Click the last corner again to finish it." : "Click the first corner to close it."), true);
            return;
        }
        if (CornerCount() >= ForbiddenArea.MaxCornersTotal)
        {
            SetStatus("All out-of-bounds areas and boundaries together can have at most " + ForbiddenArea.MaxCornersTotal + " corners.", true);
            return;
        }
        world.z = -1f;
        openArea.Add(world);
        SetStatus("", false);
        Redraw();
    }

    // The open area or line becomes a finished one if it has enough corners (three for an area, two for a line)
    void CloseOpenArea()
    {
        if (openArea == null) return;
        if (openIsLine)
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
        areaRenderer.DrawEditing(areas, boundaries, openArea, openIsLine, tool == Tool.Forbidden, bad);
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
        if (!gone) SetMarkPosition(new Mark { type = moveType.Value, item = moveItem }, world);
        moveType = null;
        errorPoint = null;
        SetStatus(gone ? "" : moveName + " was moved.", false);
        Redraw();
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
        EditCourse course = Cur;
        switch (tool)
        {
            case Tool.Start:
                course.start = world;
                SetTool(Tool.Control);
                break;
            case Tool.Control:
                if (ControlCount(course) >= CourseData.MaxControls)
                {
                    SetStatus("A course can have at most " + CourseData.MaxControls + " controls.", true);
                    return;
                }
                course.items.Add(new EditItem { point = world });
                break;
            case Tool.Branch:
                if (branchFork < 0 || branchFork >= forks.Count || branchIndex >= forks[branchFork].branches.Count)
                {
                    SetTool(Tool.Control);
                    return;
                }
                if (ControlCount(course) >= CourseData.MaxControls)
                {
                    SetStatus("A course can have at most " + CourseData.MaxControls + " controls.", true);
                    return;
                }
                forks[branchFork].branches[branchIndex].Add(world);
                break;
            case Tool.Finish:
                course.finish = world;
                break;
        }
        Redraw();
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

    void RemoveMark(Mark mark)
    {
        EditCourse course = Cur;
        switch (mark.type)
        {
            case MarkType.Start: course.start = null; break;
            case MarkType.Finish: course.finish = null; break;
            case MarkType.Control: course.items.RemoveAt(mark.item); break;
            case MarkType.BranchControl: forks[mark.fork].branches[mark.branch].RemoveAt(mark.index); break;
        }
        if (tool == Tool.Branch) SetTool(Tool.Control);
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
        drawn = new List<CourseRenderer.Mark>();
        lines = new List<CourseRenderer.Line>();
        EditCourse course = Cur;
        Color purple = CourseRenderer.CoursePurple;

        List<(Vector3 point, CourseRenderer.MarkKind kind)> previous = new List<(Vector3, CourseRenderer.MarkKind)>();
        if (course.start.HasValue)
        {
            marks.Add(new Mark { type = MarkType.Start, position = course.start.Value, label = "Start" });
            previous.Add((course.start.Value, CourseRenderer.MarkKind.Start));
        }
        int number = 0;
        for (int i = 0; i < course.items.Count; i++)
        {
            EditItem item = course.items[i];
            number++;
            if (item.fork < 0)
            {
                marks.Add(new Mark { type = MarkType.Control, item = i, position = item.point, label = number.ToString() });
                foreach (var from in previous) lines.Add(new CourseRenderer.Line { from = from.point, to = item.point, fromKind = from.kind, toKind = CourseRenderer.MarkKind.Control, color = purple });
                previous = new List<(Vector3, CourseRenderer.MarkKind)> { (item.point, CourseRenderer.MarkKind.Control) };
                continue;
            }
            EditFork fork = forks[item.fork];
            List<(Vector3, CourseRenderer.MarkKind)> ends = new List<(Vector3, CourseRenderer.MarkKind)>();
            for (int b = 0; b < fork.branches.Count; b++)
            {
                List<(Vector3 point, CourseRenderer.MarkKind kind)> branchPrevious = previous;
                for (int k = 0; k < fork.branches[b].Count; k++)
                {
                    Vector3 point = fork.branches[b][k];
                    string label = number + CourseData.BranchLetter(b) + (k > 0 ? (k + 1).ToString() : "");
                    marks.Add(new Mark { type = MarkType.BranchControl, item = i, fork = item.fork, branch = b, index = k, position = point, label = label });
                    foreach (var from in branchPrevious) lines.Add(new CourseRenderer.Line { from = from.point, to = point, fromKind = from.kind, toKind = CourseRenderer.MarkKind.Control, color = purple });
                    branchPrevious = new List<(Vector3, CourseRenderer.MarkKind)> { (point, CourseRenderer.MarkKind.Control) };
                }
                foreach (var end in branchPrevious) if (!ends.Contains(end)) ends.Add(end);
            }
            previous = ends;
        }
        if (course.finish.HasValue)
        {
            marks.Add(new Mark { type = MarkType.Finish, position = course.finish.Value, label = "Finish" });
            foreach (var from in previous) lines.Add(new CourseRenderer.Line { from = from.point, to = course.finish.Value, fromKind = from.kind, toKind = CourseRenderer.MarkKind.Finish, color = purple });
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

        if (relay) AddShadowForks(drawn, lines);
    }

    // The forks of other legs that this leg doesn't have yet, faded, with the common controls around them.
    // Clicking one of their controls offers to put the whole fork on this leg.
    void AddShadowForks(List<CourseRenderer.Mark> drawn, List<CourseRenderer.Line> lines)
    {
        Color faded = CourseRenderer.ShadowPurple;
        EditCourse course = Cur;
        for (int f = 0; f < forks.Count; f++)
        {
            if (course.items.Exists(item => item.fork == f)) continue;
            if (!FindForkSource(f, out Vector3 entry, out Vector3 exit, out bool entryIsStart, out bool exitIsFinish, out int sourceLeg)) continue;
            EditFork fork = forks[f];
            drawn.Add(new CourseRenderer.Mark { position = entry, kind = entryIsStart ? CourseRenderer.MarkKind.Start : CourseRenderer.MarkKind.Control, color = faded, label = "", toward = Vector3.up });
            drawn.Add(new CourseRenderer.Mark { position = exit, kind = exitIsFinish ? CourseRenderer.MarkKind.Finish : CourseRenderer.MarkKind.Control, color = faded, label = "" });
            // The common controls before and after the fork can be clicked too (this leg's own marks win where they overlap)
            shadows.Add(new Mark { type = MarkType.Shadow, fork = f, position = entry, label = "" });
            shadows.Add(new Mark { type = MarkType.Shadow, fork = f, position = exit, label = "" });
            for (int b = 0; b < fork.branches.Count; b++)
            {
                Vector3 from = entry;
                CourseRenderer.MarkKind fromKind = entryIsStart ? CourseRenderer.MarkKind.Start : CourseRenderer.MarkKind.Control;
                for (int k = 0; k < fork.branches[b].Count; k++)
                {
                    Vector3 point = fork.branches[b][k];
                    string label = "F" + (f + 1) + CourseData.BranchLetter(b) + (k > 0 ? (k + 1).ToString() : "");
                    shadows.Add(new Mark { type = MarkType.Shadow, fork = f, branch = b, index = k, position = point, label = label });
                    drawn.Add(new CourseRenderer.Mark { position = point, kind = CourseRenderer.MarkKind.Control, color = faded, label = label });
                    lines.Add(new CourseRenderer.Line { from = from, to = point, fromKind = fromKind, toKind = CourseRenderer.MarkKind.Control, color = faded });
                    from = point;
                    fromKind = CourseRenderer.MarkKind.Control;
                }
                lines.Add(new CourseRenderer.Line { from = from, to = exit, fromKind = fromKind, toKind = exitIsFinish ? CourseRenderer.MarkKind.Finish : CourseRenderer.MarkKind.Control, color = faded });
            }
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

    void Redraw()
    {
        BuildMarks(out List<CourseRenderer.Mark> drawn, out List<CourseRenderer.Line> lines);
        courseRenderer.DrawMarks(drawn, lines);
        RedrawAreas();
        RefreshPointList();
        RefreshHint();
        RefreshRelayPanel();
    }

    // ---- Forks ----

    int MaxBranches => forkEveryVariant ? legs : 26;

    // Turns control i into a fork: the control is branch A, and the next clicks lay branch B
    void MakeFork(int i)
    {
        EditCourse course = Cur;
        if ((i > 0 && course.items[i - 1].fork >= 0) || (i < course.items.Count - 1 && course.items[i + 1].fork >= 0))
        {
            SetStatus("Next to a fork there must be a common control that every team runs, so this control can't be a fork too.", true);
            return;
        }
        if (MaxBranches < 2)
        {
            SetStatus("A fork needs at least two legs.", true);
            return;
        }
        EditFork fork = new EditFork();
        fork.branches.Add(new List<Vector3> { course.items[i].point });
        fork.branches.Add(new List<Vector3>());
        forks.Add(fork);
        course.items[i] = new EditItem { fork = forks.Count - 1 };
        StartBranch(forks.Count - 1, 1);
        SetStatus("Control " + (i + 1) + " is now a fork. Click where branch B's first control should be.", false);
    }

    void AddBranch(int f, bool direct)
    {
        if (forks[f].branches.Count >= MaxBranches)
        {
            SetStatus("Fork " + (f + 1) + " already has " + forks[f].branches.Count + " branches, the most it can have: the relay has " + legs
                + " legs, and every team runs every branch once. Add legs, or choose random branches.", true);
            return;
        }
        forks[f].branches.Add(new List<Vector3>());
        if (direct)
        {
            SetStatus("Branch " + CourseData.BranchLetter(forks[f].branches.Count - 1) + " goes straight to the common control after the fork.", false);
            Redraw();
            return;
        }
        StartBranch(f, forks[f].branches.Count - 1);
    }

    void StartBranch(int f, int b)
    {
        branchFork = f;
        branchIndex = b;
        SetTool(Tool.Branch);
        Redraw();
    }

    void RemoveBranch(int f, int b)
    {
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
            }
        }
        Redraw();
    }

    void RemoveForkFromLeg(int i)
    {
        Cur.items.RemoveAt(i);
        if (tool == Tool.Branch) SetTool(Tool.Control);
        Redraw();
    }

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

    // A faded fork was clicked: put the whole fork on this leg, or just a control where it was clicked
    void AskUseFork(int f, Vector3 clicked)
    {
        if (!FindForkSource(f, out Vector3 entry, out Vector3 exit, out bool entryIsStart, out bool exitIsFinish, out int sourceLeg)) return;
        Transform canvas = canvasObject.transform;
        Image dim = CourseUI.Panel("Fork question", canvas, new Color(0f, 0f, 0f, 0.35f), false);
        CourseUI.Stretch(dim.rectTransform);
        forkPopup = dim.gameObject;
        Image card = CourseUI.CardPanel("Card", dim.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-200, 0), new Vector2(700, 330));
        CourseUI.Column(card.gameObject, 14, new RectOffset(32, 32, 28, 28));
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Fork " + (f + 1) + " from leg " + (sourceLeg + 1), 32, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 42);
        int branches = forks[f].branches.Count;
        TextMeshProUGUI text = CourseUI.Text("Text", card.transform,
            "Put the whole fork on leg " + (selectedLeg + 1) + "? It has " + branches + " branches, and comes with the common "
            + (entryIsStart ? "start" : "control") + " before it and the common " + (exitIsFinish ? "finish" : "control") + " after it.", 22, CourseUI.InkMuted);
        CourseUI.Size(text, 96);
        Button use = CourseUI.Button("Use", card.transform, "Use the whole fork", CourseUI.ButtonKind.Primary, () => { CloseForkPopup(); UseFork(f); }, 24);
        CourseUI.Size(use, 54);
        RectTransform row = CourseUI.Rect("Row", card.transform);
        CourseUI.Row(row.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(row, 50);
        CourseUI.Button("New control", row, "New control here instead", CourseUI.ButtonKind.Light, () => { CloseForkPopup(); Place(clicked); }, 20);
        CourseUI.Button("Cancel", row, "Cancel", CourseUI.ButtonKind.Light, CloseForkPopup, 20);
    }

    void CloseForkPopup()
    {
        if (forkPopup != null) Destroy(forkPopup);
        forkPopup = null;
    }

    void UseFork(int f)
    {
        if (!FindForkSource(f, out Vector3 entry, out Vector3 exit, out bool entryIsStart, out bool exitIsFinish, out int _)) return;
        EditCourse course = Cur;
        if (course.items.Exists(item => item.fork == f))
        {
            SetStatus("This leg already has fork " + (f + 1) + ".", true);
            return;
        }
        if (course.items.Count > 0 && course.items[course.items.Count - 1].fork >= 0 && !entryIsStart)
        {
            // The common control before the fork is added below, so two forks are kept apart
        }
        if (entryIsStart)
        {
            if (course.items.Count > 0)
            {
                SetStatus("Fork " + (f + 1) + " starts at the start, so it has to be the first thing on the leg. Remove this leg's controls first, or lay a new control.", true);
                return;
            }
            course.start = entry;
        }
        else
        {
            Vector3? last = course.items.Count == 0 ? course.start : course.items[course.items.Count - 1].fork < 0 ? course.items[course.items.Count - 1].point : (Vector3?)null;
            if (!last.HasValue || Vector2.Distance(last.Value, entry) > 0.02f)
            {
                course.items.Add(new EditItem { point = entry });
            }
        }
        course.items.Add(new EditItem { fork = f });
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
        SetStatus("Fork " + (f + 1) + " is now on leg " + (selectedLeg + 1) + " too, with the common controls around it." + (exitIsFinish ? " It ends at the finish." : " Carry on from the control after it."), false);
        Redraw();
    }

    // ---- Legs ----

    void SelectLeg(int leg)
    {
        selectedLeg = Mathf.Clamp(leg, 0, legs - 1);
        CopyStartAndFinishFromLegBefore();
        EditCourse course = Cur;
        errorPoint = null;
        dragging = null;
        SetTool(!course.start.HasValue ? Tool.Start : Tool.Control);
        SetStatus("", false);
        Redraw();
    }

    void ChangeLegs(int delta)
    {
        legs = Mathf.Clamp(legs + delta, CourseData.MinLegs, CourseData.MaxLegs);
        // Every new leg gets its own course
        while (legCourse.Count < legs)
        {
            courses.Add(new EditCourse());
            legCourse.Add(courses.Count - 1);
        }
        while (legCourse.Count > legs) legCourse.RemoveAt(legCourse.Count - 1);
        if (selectedLeg >= legs)
        {
            SelectLeg(legs - 1);
            return;
        }
        Redraw();
    }

    // Lets the selected leg run the same course as the leg before, or gives it back a course of its own (a copy)
    void ToggleSameCourse()
    {
        if (selectedLeg == 0) return;
        if (legCourse[selectedLeg] == legCourse[selectedLeg - 1])
        {
            EditCourse shared = Cur;
            EditCourse own = new EditCourse { start = shared.start, finish = shared.finish };
            foreach (EditItem item in shared.items) own.items.Add(new EditItem { fork = item.fork, point = item.point });
            courses.Add(own);
            legCourse[selectedLeg] = courses.Count - 1;
            SetStatus("Leg " + (selectedLeg + 1) + " now has its own course, a copy of leg " + selectedLeg + "'s.", false);
        }
        else
        {
            legCourse[selectedLeg] = legCourse[selectedLeg - 1];
            SetStatus("Leg " + (selectedLeg + 1) + " runs the same course as leg " + selectedLeg + ". Its forks count once per leg.", false);
        }
        SelectLeg(selectedLeg);
    }

    void ToggleForkMode()
    {
        forkEveryVariant = !forkEveryVariant;
        Redraw();
    }

    void SetRelay(bool on)
    {
        relay = on;
        if (typeButton != null) CourseUI.SetButtonText(typeButton, relay ? "Type: Relay" : "Type: Individual");
        if (relayPanel != null) relayPanel.gameObject.SetActive(relay);
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
    }

    void RefreshRelayPanel()
    {
        if (relayPanel == null || !relay) return;
        legsLabel.text = legs + " legs";
        foreach (Transform child in legButtonRow) Destroy(child.gameObject);
        for (int leg = 0; leg < legs && leg < legCourse.Count; leg++)
        {
            int captured = leg;
            EditCourse course = courses[legCourse[leg]];
            bool forked = course.items.Exists(item => item.fork >= 0 && forks[item.fork].branches.Count > 1);
            string kind = leg > 0 && legCourse[leg] == legCourse[leg - 1] ? "= leg " + leg : forked ? "forked" : course.items.Count == 0 ? "empty" : "straight";
            Button button = CourseUI.Button("Leg " + (leg + 1), legButtonRow, "Leg " + (leg + 1) + "\n" + kind,
                leg == selectedLeg ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light, () => SelectLeg(captured), 15);
            button.GetComponentInChildren<TextMeshProUGUI>().enableWordWrapping = true;
        }
        sameCourseButton.gameObject.SetActive(selectedLeg > 0);
        bool same = selectedLeg > 0 && legCourse[selectedLeg] == legCourse[selectedLeg - 1];
        CourseUI.SetButtonText(sameCourseButton, same ? "Give leg " + (selectedLeg + 1) + " its own course" : "Same course as leg " + selectedLeg);
        CourseUI.SetButtonText(forkModeButton, forkEveryVariant ? "Every team runs every branch" : "Branches are random");
        relayInfo.text = "Each leg has its own course. Turn a control into a fork with \"Fork\" in the control list; a branch can have several controls, or none.\n"
            + "Faded controls are forks on other legs: click one to put the whole fork on this leg. "
            + (forkEveryVariant ? "Every team runs every branch of a fork once, so a fork needs as many legs as branches." : "Each team gets a random branch of every fork.");
    }

    // ---- Clearing, checking and saving ----

    void ClearCourse()
    {
        courses.Clear();
        forks.Clear();
        legCourse.Clear();
        areas.Clear();
        boundaries.Clear();
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
        }
        for (int leg = 0; leg < legs; leg++) course.legCourse.Add(usedCourses.IndexOf(legCourse[leg]));
        course.RefreshMainFields();
        return course;
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
        if (!relay)
        {
            EditCourse single = Cur;
            if (!single.start.HasValue) return "Place the start first.";
            if (single.items.Count == 0) return "Add at least one control.";
            if (!single.finish.HasValue) return "Place the finish.";
        }
        else
        {
            for (int leg = 0; leg < legs; leg++)
            {
                EditCourse course = courses[legCourse[leg]];
                bool empty = !course.start.HasValue && !course.finish.HasValue && course.items.Count == 0;
                string missing = empty ? "has no course yet. Lay it, or let it run the same course as the leg before"
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
        selectedLeg = leg;
        CopyStartAndFinishFromLegBefore();
        dragging = null;
        SetTool(Cur.start.HasValue ? Tool.Control : Tool.Start);
    }

    // A new leg starts with the same start and finish as the leg before, since relays usually share them
    void CopyStartAndFinishFromLegBefore()
    {
        EditCourse course = Cur;
        if (selectedLeg > 0 && !course.start.HasValue && !course.finish.HasValue && course.items.Count == 0)
        {
            EditCourse before = courses[legCourse[selectedLeg - 1]];
            course.start = before.start;
            course.finish = before.finish;
        }
    }

    async void OnSave()
    {
        if (string.IsNullOrWhiteSpace(nameField.text))
        {
            SetStatus("Give the course a name.", true);
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

        PlayerPrefs.SetString("PlayerNickName", authorField.text.Trim());
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
        if (course.forbidden != null)
        {
            foreach (ForbiddenArea area in course.forbidden)
            {
                if (area == null || !area.IsValid) continue;
                (area.line ? boundaries : areas).Add(area.corners.ConvertAll(p => map.ToWorld(p)));
            }
        }
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
                EditCourse edit = new EditCourse { start = map.ToWorld(relayCourse.start), finish = map.ToWorld(relayCourse.finish) };
                foreach (RelayItem item in relayCourse.items)
                {
                    edit.items.Add(item.fork < 0 ? new EditItem { point = map.ToWorld(item.point) } : new EditItem { fork = item.fork });
                }
                courses.Add(edit);
            }
            for (int leg = 0; leg < legs; leg++) legCourse.Add(course.LegCourseIndex(leg));
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
    void ToggleForbidden(bool line)
    {
        if (tool == Tool.Forbidden && drawLine == line)
        {
            SetTool(Cur.start.HasValue ? Tool.Control : Tool.Start);
            return;
        }
        if (tool == Tool.Forbidden) CloseOpenArea();
        drawLine = line;
        SetTool(Tool.Forbidden);
        Redraw();
    }

    void SetTool(Tool chosen)
    {
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
        if (boundaryButton != null) CourseUI.SetButtonColor(boundaryButton, tool == Tool.Forbidden && drawLine ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
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
        else if (tool == Tool.Control) text = "Click on the map to add control " + (course.items.Count + 1) + ". Choose Finish when you are done.";
        else if (tool == Tool.Branch && branchFork >= 0 && branchFork < forks.Count && branchIndex < forks[branchFork].branches.Count)
            text = "Click to add control " + (forks[branchFork].branches[branchIndex].Count + 1) + " of fork " + (branchFork + 1) + ", branch "
                + CourseData.BranchLetter(branchIndex) + ". Choose Controls when the branch is done.";
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
            : "\nDrag a mark to move it, right-click it to remove it. Right-drag moves the map, the wheel zooms.");

        if (summaryText != null)
        {
            int forkCount = course.items.FindAll(item => item.fork >= 0).Count;
            string length = !relay && course.start.HasValue && course.finish.HasValue ? "  ·  " + BuildCourse().LengthText() + " straight line" : "";
            summaryText.text = relay
                ? "Leg " + (selectedLeg + 1) + "  ·  " + ControlCount(course) + " controls" + (forkCount > 0 ? ", " + forkCount + (forkCount == 1 ? " fork" : " forks") : "")
                : course.items.Count + " controls" + length;
            if (areas.Count > 0) summaryText.text += "  ·  " + areas.Count + (areas.Count == 1 ? " out-of-bounds area" : " out-of-bounds areas");
            if (boundaries.Count > 0) summaryText.text += "  ·  " + boundaries.Count + (boundaries.Count == 1 ? " boundary" : " boundaries");
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

        Image panel = CourseUI.CardPanel("Panel", canvasObject.transform);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(1, 0);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(1, 0.5f);
        panelRect.sizeDelta = new Vector2(PanelWidth, -48);
        panelRect.anchoredPosition = new Vector2(-24, 0);
        // Everything must fit in the panel's height (1080 - 48 on the reference screen), with the buttons at the bottom
        // always visible: the two lists give up room first (down to their minimum heights) when space runs short
        CourseUI.Column(panel.gameObject, 8, new RectOffset(28, 28, 20, 20));

        // The title, with New course in the top-right corner, so the buttons at the bottom have room for their names
        RectTransform titleRow = CourseUI.Rect("Title row", panel.transform);
        CourseUI.Row(titleRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(titleRow, 44);
        TextMeshProUGUI title = CourseUI.Text("Title", titleRow, "Course builder", 38, CourseUI.Ink, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.enableWordWrapping = false;
        CourseUI.Size(title, 44, -1, 1);
        Button newCourse = CourseUI.Button("New", titleRow, "New course", CourseUI.ButtonKind.Light, ClearCourse, 20);
        CourseUI.Size(newCourse, 40, 150);

        RectTransform mapRow = CourseUI.Rect("Map row", panel.transform);
        CourseUI.Row(mapRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(mapRow, 48);
        mapButton = CourseUI.Button("Map", mapRow, "Map", CourseUI.ButtonKind.Light, OpenMapPicker, 22);
        typeButton = CourseUI.Button("Type", mapRow, "Type: Individual", CourseUI.ButtonKind.Light, () => SetRelay(!relay), 22);

        nameField = CourseUI.Input("Course name", panel.transform, "Course name", CourseData.MaxNameLength, null);
        CourseUI.Size(nameField, 54);
        authorField = CourseUI.Input("Your name", panel.transform, "Your name", CourseData.MaxAuthorLength, null);
        CourseUI.Size(authorField, 54);
        authorField.text = PlayerPrefs.GetString("PlayerNickName", GameManager.Instance != null ? GameManager.Instance.playerNickName : "");

        RectTransform toolRow = CourseUI.Rect("Tools", panel.transform);
        CourseUI.Row(toolRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(toolRow, 54);
        toolButtons[Tool.Start] = CourseUI.Button("Start", toolRow, "Start", CourseUI.ButtonKind.Light, () => SetTool(Tool.Start), 24);
        toolButtons[Tool.Control] = CourseUI.Button("Control", toolRow, "Controls", CourseUI.ButtonKind.Light, () => SetTool(Tool.Control), 24);
        toolButtons[Tool.Finish] = CourseUI.Button("Finish", toolRow, "Finish", CourseUI.ButtonKind.Light, () => SetTool(Tool.Finish), 24);

        // Out-of-bounds areas and uncrossable boundaries: tools of their own, under the course tools
        RectTransform forbiddenRow = CourseUI.Rect("Out of bounds", panel.transform);
        CourseUI.Row(forbiddenRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(forbiddenRow, 44);
        forbiddenButton = CourseUI.Button("Out of bounds", forbiddenRow, "Out-of-bounds area", CourseUI.ButtonKind.Light, () => ToggleForbidden(false), 20);
        boundaryButton = CourseUI.Button("Boundary", forbiddenRow, "Uncrossable line", CourseUI.ButtonKind.Light, () => ToggleForbidden(true), 20);

        // Room for four lines: the tool's own hint can take two, the mouse help after it two more
        hintText = CourseUI.Text("Hint", panel.transform, "", 19, CourseUI.InkMuted);
        CourseUI.Size(hintText, 92);

        summaryText = CourseUI.Text("Summary", panel.transform, "", 24, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(summaryText, 32);

        pointList = CourseUI.ScrollList("Points", panel.transform, out _);
        CourseUI.Column(pointList.gameObject, 4, new RectOffset(0, 8, 0, 0));
        LayoutElement pointsLayout = CourseUI.Size(pointList.parent.GetComponent<Image>(), 110);
        pointsLayout.flexibleHeight = 1;
        pointsLayout.minHeight = 40;

        statusText = CourseUI.Text("Status", panel.transform, "", 19, CourseUI.Forest, FontStyles.Bold);
        CourseUI.Size(statusText, 56);
        statusText.overflowMode = TextOverflowModes.Overflow;
        // Long messages shrink to fit the smaller box instead of running over the buttons under it
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 14;
        statusText.fontSizeMax = 19;

        // Forest or sprint course: chosen after the course is laid; the map's type is the default
        RectTransform terrainRow = CourseUI.Rect("Course type", panel.transform);
        CourseUI.Row(terrainRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(terrainRow, 44);
        TextMeshProUGUI terrainLabel = CourseUI.Text("Label", terrainRow, "Course type:", 21, CourseUI.Ink, FontStyles.Bold);
        terrainLabel.alignment = TextAlignmentOptions.MidlineLeft;
        forestButton = CourseUI.Button("Forest", terrainRow, "Forest", CourseUI.ButtonKind.Light, () => SetTerrain(CourseData.Forest), 20);
        sprintButton = CourseUI.Button("Sprint", terrainRow, "Sprint", CourseUI.ButtonKind.Light, () => SetTerrain(CourseData.Sprint), 20);
        SetTerrain(terrain);

        RectTransform actionRow = CourseUI.Rect("Actions", panel.transform);
        CourseUI.Row(actionRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(actionRow, 58);
        saveButton = CourseUI.Button("Save", actionRow, "Save & publish", CourseUI.ButtonKind.Primary, OnSave, 24);

        // My courses: a button that opens a big pop-up with search and filters (the list in the panel was too small)
        myCoursesButton = CourseUI.Button("My courses", panel.transform, "My courses", CourseUI.ButtonKind.Secondary, OpenMyCourses, 22);
        CourseUI.Size(myCoursesButton, 50);

        RectTransform bottomRow = CourseUI.Rect("Bottom", panel.transform);
        CourseUI.Row(bottomRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(bottomRow, 52);
        CourseUI.Button("Import map", bottomRow, "Import map", CourseUI.ButtonKind.Light, OpenMapImport, 22);
        // Opened from the multiplayer lobby: back to the lobby, the network game is still running
        if (LobbyVisit.Active) CourseUI.Button("Back", bottomRow, "Back to lobby", CourseUI.ButtonKind.Light, LobbyVisit.Close, 22);
        else CourseUI.Button("Back", bottomRow, "Back to menu", CourseUI.ButtonKind.Light, () => SceneLoader.Load("MainMenu", "Back to the menu"), 22);

        BuildRelayPanel(canvasObject.transform);
    }

    void BuildRelayPanel(Transform canvas)
    {
        relayPanel = CourseUI.CardPanel("Relay panel", canvas);
        CourseUI.Place(relayPanel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(440, 600));
        CourseUI.Column(relayPanel.gameObject, 10, new RectOffset(22, 22, 20, 20));

        TextMeshProUGUI title = CourseUI.Text("Title", relayPanel.transform, "Relay", 32, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 40);

        RectTransform legsRow = CourseUI.Rect("Legs", relayPanel.transform);
        CourseUI.Row(legsRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(legsRow, 48);
        Button minus = CourseUI.Button("Fewer", legsRow, "-", CourseUI.ButtonKind.Light, () => ChangeLegs(-1), 28);
        CourseUI.Size(minus, 46, 60);
        legsLabel = CourseUI.Text("Count", legsRow, "", 26, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(legsLabel, 46, 150);
        Button plus = CourseUI.Button("More", legsRow, "+", CourseUI.ButtonKind.Light, () => ChangeLegs(1), 28);
        CourseUI.Size(plus, 46, 60);

        TextMeshProUGUI legHint = CourseUI.Text("Leg hint", relayPanel.transform, "Click a leg to lay its course:", 19, CourseUI.InkMuted);
        CourseUI.Size(legHint, 26);

        legButtonRow = CourseUI.Rect("Leg buttons", relayPanel.transform);
        GridLayoutGroup legGrid = legButtonRow.gameObject.AddComponent<GridLayoutGroup>();
        legGrid.cellSize = new Vector2(74, 52);
        legGrid.spacing = new Vector2(5, 5);
        legGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        legGrid.constraintCount = 5;
        CourseUI.Size(legButtonRow, 110);

        sameCourseButton = CourseUI.Button("Same course", relayPanel.transform, "", CourseUI.ButtonKind.Light, ToggleSameCourse, 19);
        CourseUI.Size(sameCourseButton, 44);

        forkModeButton = CourseUI.Button("Fork mode", relayPanel.transform, "", CourseUI.ButtonKind.Secondary, ToggleForkMode, 20);
        CourseUI.Size(forkModeButton, 44);

        relayInfo = CourseUI.Text("Info", relayPanel.transform, "", 17, CourseUI.InkMuted);
        CourseUI.Size(relayInfo, 150);

        relayPanel.gameObject.SetActive(relay);
    }

    void RefreshPointList()
    {
        if (pointList == null) return;
        foreach (Transform child in pointList) Destroy(child.gameObject);
        EditCourse course = Cur;
        Vector3? previous = course.start;
        if (course.start.HasValue)
        {
            AddRow("Start", 38, MovingColor(MarkType.Start, -1), FontStyles.Normal, 6,
                ("Move", CourseUI.ButtonKind.Light, () => StartMove(MarkType.Start, -1, "The start")),
                ("Remove", CourseUI.ButtonKind.Light, () => { course.start = null; Redraw(); }));
        }
        int number = 0;
        for (int i = 0; i < course.items.Count; i++)
        {
            int index = i;
            EditItem item = course.items[i];
            number++;
            if (item.fork < 0)
            {
                string text = "Control " + number + Meters(previous, item.point);
                bool bad = errorPoint.HasValue && Vector2.Distance(errorPoint.Value, item.point) < 0.08f;
                string controlName = "Control " + number;
                Color rowColor = bad ? CourseUI.Danger : MovingColor(MarkType.Control, index);
                if (relay)
                {
                    AddRow(text, 38, rowColor, bad ? FontStyles.Bold : FontStyles.Normal, 6,
                        ("Fork", CourseUI.ButtonKind.Secondary, () => MakeFork(index)),
                        ("Move", CourseUI.ButtonKind.Light, () => StartMove(MarkType.Control, index, controlName)),
                        ("Remove", CourseUI.ButtonKind.Light, () => { course.items.RemoveAt(index); Redraw(); }));
                }
                else
                {
                    AddRow(text, 38, rowColor, bad ? FontStyles.Bold : FontStyles.Normal, 6,
                        ("Move", CourseUI.ButtonKind.Light, () => StartMove(MarkType.Control, index, controlName)),
                        ("Remove", CourseUI.ButtonKind.Light, () => { course.items.RemoveAt(index); Redraw(); }));
                }
                previous = item.point;
                continue;
            }

            int f = item.fork;
            EditFork fork = forks[f];
            List<int> forkLegs = LegsWithFork(f);
            List<string> legNames = forkLegs.ConvertAll(l => (l + 1).ToString());
            bool full = fork.branches.Count >= MaxBranches;
            AddRow("<b>Fork " + (f + 1) + "</b>  " + fork.branches.Count + " branches", 38, CourseUI.Ink, FontStyles.Normal, 6,
                ("+ Branch", full ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary, () => AddBranch(f, false)),
                ("+ Direct", full ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary, () => AddBranch(f, true)),
                ("Remove", CourseUI.ButtonKind.Light, () => RemoveForkFromLeg(index)));
            AddRow("<color=#5B7563>at " + number + ", on " + (forkLegs.Count == 1 ? "leg " : "legs ") + string.Join(", ", legNames)
                + (forkEveryVariant && fork.branches.Count > forkLegs.Count ? ": needs " + (fork.branches.Count - forkLegs.Count) + " more" : "") + "</color>", 26, CourseUI.Ink, FontStyles.Normal, 34);
            for (int b = 0; b < fork.branches.Count; b++)
            {
                int branch = b;
                List<string> names = new List<string>();
                for (int k = 0; k < fork.branches[b].Count; k++) names.Add(number + CourseData.BranchLetter(b) + (k > 0 ? (k + 1).ToString() : ""));
                string text = CourseData.BranchLetter(b) + ":  " + (names.Count > 0 ? string.Join(", ", names) : "<i>straight to the next common control</i>");
                bool active = tool == Tool.Branch && branchFork == f && branchIndex == b;
                AddRow(text, 34, active ? CourseUI.Accent : CourseUI.Ink, active ? FontStyles.Bold : FontStyles.Normal, 34,
                    ("+ Control", CourseUI.ButtonKind.Light, () => StartBranch(f, branch)),
                    ("Remove", CourseUI.ButtonKind.Light, () => RemoveBranch(f, branch)));
            }
            previous = null;
        }
        if (course.finish.HasValue)
        {
            AddRow("Finish" + Meters(previous, course.finish.Value), 38, MovingColor(MarkType.Finish, -1), FontStyles.Normal, 6,
                ("Move", CourseUI.ButtonKind.Light, () => StartMove(MarkType.Finish, -1, "The finish")),
                ("Remove", CourseUI.ButtonKind.Light, () => { course.finish = null; Redraw(); }));
        }
        for (int a = 0; a < areas.Count; a++)
        {
            int index = a;
            AddRow("<color=#8A0A94>Out-of-bounds area " + (a + 1) + "</color>  <color=#5B7563>" + areas[a].Count + " corners</color>", 38, CourseUI.Ink, FontStyles.Normal, 6,
                ("Remove", CourseUI.ButtonKind.Light, () => { areas.RemoveAt(index); Redraw(); }));
        }
        for (int b = 0; b < boundaries.Count; b++)
        {
            int index = b;
            AddRow("<color=#8A0A94>Uncrossable boundary " + (b + 1) + "</color>  <color=#5B7563>" + boundaries[b].Count + " corners</color>", 38, CourseUI.Ink, FontStyles.Normal, 6,
                ("Remove", CourseUI.ButtonKind.Light, () => { boundaries.RemoveAt(index); Redraw(); }));
        }
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

    void AddRow(string text, float height, Color color, FontStyles style, int indent, params (string label, CourseUI.ButtonKind kind, UnityEngine.Events.UnityAction action)[] buttons)
    {
        RectTransform row = CourseUI.Rect("Row", pointList);
        CourseUI.Row(row.gameObject, 6, new RectOffset(indent, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(row, height);
        TextMeshProUGUI label = CourseUI.Text("Label", row, text, 19, color, style);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        CourseUI.Size(label, height, -1, 1);
        foreach (var button in buttons)
        {
            Button made = CourseUI.Button(button.label, row, button.label, button.kind, button.action, 15);
            CourseUI.Size(made, height - 4, button.label.Length > 7 ? 104 : 90);
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
