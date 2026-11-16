using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The lobby's course page. Lists the courses that come with the game, the ones this player has made
// and the ones other players have published, with the course name and who made it. The host picks one here.
public class MapSelectUI : MonoBehaviour
{
    public GameObject mapButtonPrefab;      // no longer used; the cards are built in code
    public Transform mapListParent;         // grid the course cards go in
    public MapHandler mapHandler;
    public GameObject mapSelectPanel;

    enum Filter { All, BuiltIn, Mine, Community }

    Filter filter = Filter.All;
    readonly Dictionary<Filter, Button> tabs = new Dictionary<Filter, Button>();
    TextMeshProUGUI statusText;
    TMP_InputField searchField;
    Button kindButton;
    readonly HashSet<string> types = new HashSet<string>();   // ticked course types; none = all
    bool fetching;
    float lastFetch = -999f;
    string fetchError;

    private void Start()
    {
        BuildChrome();
        mapSelectPanel.SetActive(false);
        SharedMaps.Changed += OnSharedMapsChanged;
    }

    void OnDestroy()
    {
        SharedMaps.Changed -= OnSharedMapsChanged;
    }

    // A map another player shared has been downloaded: its courses can be shown now
    void OnSharedMapsChanged()
    {
        if (this != null && mapSelectPanel != null && mapSelectPanel.activeSelf) PopulateMapList();
    }

    // Tabs and status line above the list, and a grid sized for course cards
    void BuildChrome()
    {
        GridLayoutGroup grid = mapListParent.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(400, 520);
            grid.spacing = new Vector2(36, 36);
            grid.padding = new RectOffset(80, 80, 24, 60);
            grid.childAlignment = TextAnchor.UpperCenter;
        }

        // A pop-up card over the lobby, so its Close button doesn't cover "Leave lobby"
        CourseUI.MakePopup(mapSelectPanel, mapListParent.parent as RectTransform, 330f);

        RectTransform tabRow = CourseUI.Rect("Course tabs", mapSelectPanel.transform);
        CourseUI.Place(tabRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(1700, 64));
        CourseUI.Row(tabRow.gameObject, 16, new RectOffset(0, 0, 0, 0)).childAlignment = TextAnchor.MiddleCenter;
        AddTab(tabRow, Filter.All, "All");
        AddTab(tabRow, Filter.Mine, "My courses");
        AddTab(tabRow, Filter.Community, "Community");
        Button ai = CourseUI.Button("AI course", tabRow, "AI course", CourseUI.ButtonKind.Secondary, OpenAICourse, 24);
        CourseUI.Size(ai, 56, 210);
        Button refresh = CourseUI.Button("Refresh", tabRow, "Refresh", CourseUI.ButtonKind.Light, () => FetchCommunity(true), 24);
        CourseUI.Size(refresh, 56, 170);
        HoverTip.Add(ai, "Let the game lay a new course for you on any map, with the length and type you choose. AI courses are not saved.");
        HoverTip.Add(refresh, "Fetch the newest community courses again.");

        // Search and relay/individual filter
        RectTransform searchRow = CourseUI.Rect("Course search", mapSelectPanel.transform);
        CourseUI.Place(searchRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -222), new Vector2(900, 52));
        CourseUI.Row(searchRow.gameObject, 16, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        searchField = CourseUI.Input("Search", searchRow, "Search courses (name, author, relay...)", 40, _ => PopulateMapList());
        CourseUI.Size(searchField, 52, 600);
        HoverTip.Add(searchField, "Search for a course by its name, its maker or its map.");
        kindButton = CourseUI.Button("Kind", searchRow, CourseFilter.TypesLabel(types), CourseUI.ButtonKind.Light, () =>
        {
            MultiSelectDropdown.Open((RectTransform)kindButton.transform, "Course type", CourseFilter.TypeOptions, types, false, null, () =>
            {
                CourseUI.SetButtonText(kindButton, CourseFilter.TypesLabel(types));
                CourseUI.SetButtonColor(kindButton, types.Count == 0 ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
                PopulateMapList();
            });
        }, 22);
        CourseUI.Size(kindButton, 52, 270);
        HoverTip.Add(kindButton, "Tick the course types to list, as many as you like: Relay + Sprint lists sprint relays. Nothing ticked lists all.");

        statusText = CourseUI.Text("Course status", mapSelectPanel.transform, "", 24, CourseUI.InkMuted, FontStyles.Italic, TextAlignmentOptions.Center);
        CourseUI.Place(statusText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -288), new Vector2(1400, 34));
        UpdateTabs();
    }

    void AddTab(Transform parent, Filter which, string label)
    {
        Button tab = CourseUI.Button("Tab " + label, parent, label, CourseUI.ButtonKind.Light, () =>
        {
            filter = which;
            UpdateTabs();
            PopulateMapList();
        }, 24);
        CourseUI.Size(tab, 56, 210);
        HoverTip.Add(tab, which == Filter.All ? "Every course: the ready-made ones, yours and the community's."
            : which == Filter.Mine ? "Only the courses you have made yourself."
            : "Courses that other players have made and shared.");
        tabs[which] = tab;
    }

    void UpdateTabs()
    {
        foreach (var pair in tabs)
        {
            CourseUI.SetButtonColor(pair.Value, pair.Key == filter ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        }
    }

    // The course pictures are made when their card comes into sight (a long community list made them all at once,
    // which froze the screen), a few each frame. A picture that was thrown out of CourseThumbnail's store to save
    // memory is made again when its card is seen.
    readonly List<(RawImage picture, CourseData course)> pictures = new List<(RawImage, CourseData)>();
    readonly Vector3[] corners = new Vector3[4];
    ScrollRect listScroll;

    void LateUpdate()
    {
        if (pictures.Count == 0 || mapSelectPanel == null || !mapSelectPanel.activeInHierarchy) return;
        if (listScroll == null) listScroll = mapListParent.GetComponentInParent<ScrollRect>(true);
        RectTransform view = listScroll != null && listScroll.viewport != null ? listScroll.viewport
            : listScroll != null ? (RectTransform)listScroll.transform : mapListParent.parent as RectTransform;
        if (view == null) return;
        Rect seen = WorldRect(view);
        // Half a screen more above and below, so the pictures are there when they scroll in
        seen.yMin -= seen.height * 0.5f;
        seen.yMax += seen.height * 0.5f;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var entry in pictures)
        {
            if (entry.picture == null || entry.picture.texture != null) continue;
            if (!seen.Overlaps(WorldRect(entry.picture.rectTransform))) continue;
            Texture2D thumbnail = CourseThumbnail.Get(entry.course);
            // No picture to be had (the map has none): a stand-in, so it is not tried again every frame
            entry.picture.texture = thumbnail != null ? thumbnail : Texture2D.blackTexture;
            entry.picture.enabled = thumbnail != null;
            if (watch.ElapsedMilliseconds > 8) break;   // the rest next frame
        }
    }

    Rect WorldRect(RectTransform rect)
    {
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    void PopulateMapList()
    {
        foreach (Transform child in mapListParent)
            Destroy(child.gameObject);
        pictures.Clear();

        string myId = CloudCourses.PlayerId;
        List<(CourseData course, string tag)> list = new List<(CourseData, string)>();
        // A course made by the AI only exists until another one is picked, so it is shown first while chosen
        if (filter == Filter.All && IsAICourse(mapHandler.course))
            list.Add((mapHandler.course, "AI"));
        // No built-in courses are listed (the game has none to offer): only the players' own, the community's and AI courses
        List<CourseData> mine = CourseLibrary.Mine();
        if (filter == Filter.All || filter == Filter.Mine)
            foreach (CourseData course in mine) list.Add((course, "Yours"));
        // A course of your own is only listed as "Yours", also when it comes back from the cloud under another
        // player id (made in the Unity Editor, which signs in as its own anonymous player, or before a reinstall)
        HashSet<string> mineIds = new HashSet<string>();
        foreach (CourseData course in mine) if (!string.IsNullOrEmpty(course.id)) mineIds.Add(course.id);
        if (filter == Filter.All || filter == Filter.Community)
            foreach (CourseData course in CourseLibrary.Community)
                if (course.authorId != myId && !mineIds.Contains(course.id)) list.Add((course, "Community"));

        string search = searchField != null ? searchField.text : "";
        list.RemoveAll(entry => !CourseFilter.Matches(entry.course, search, types));
        // Courses on a map this computer does not have (a map someone imported on their own computer)
        list.RemoveAll(entry => !MapDefinition.Exists(entry.course.mapId));
        foreach (var entry in list)
        {
            AddCard(entry.course, entry.tag);
        }
        UpdateStatus(list.Count);
    }

    void UpdateStatus(int shown)
    {
        if (statusText == null) return;
        if (fetching)
            statusText.text = "Looking for courses from other players...";
        else if (!string.IsNullOrEmpty(fetchError) && (filter == Filter.All || filter == Filter.Community))
            statusText.text = fetchError;
        else if (shown == 0 && filter == Filter.Mine)
            statusText.text = "You have not made any courses yet. Make one with the Course builder in the main menu.";
        else if (shown == 0 && ((searchField != null && searchField.text.Trim().Length > 0) || types.Count > 0))
            statusText.text = "No courses match the search.";
        else if (shown == 0)
            statusText.text = "No courses here yet.";
        else if (SharedMaps.Downloading != null)
            statusText.text = "Downloading the map " + SharedMaps.Downloading + " that a player shared...";
        else
            statusText.text = "";
    }

    void AddCard(CourseData course, string tag)
    {
        bool selected = mapHandler.course != null && mapHandler.course.id == course.id;
        Image card = CourseUI.CardPanel("Course " + course.name, mapListParent);
        if (selected)
        {
            Outline outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = CourseUI.Accent;
            outline.effectDistance = new Vector2(5, -5);
        }

        // Map picture with the course on it
        Image frame = CourseUI.Panel("Picture", card.transform, CourseUI.CardSolid);
        CourseUI.Place(frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(368, 300));
        frame.gameObject.AddComponent<RectMask2D>();
        // The picture itself is made when the card comes into sight (LateUpdate)
        RawImage picture = CourseUI.Rect("Map", frame.transform).gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        picture.enabled = false;
        CourseUI.Stretch(picture.rectTransform);
        pictures.Add((picture, course));

        Color tagColor = tag == "Built-in" ? CourseUI.Forest : tag == "Yours" ? CourseUI.Accent : tag == "AI" ? (Color)CourseRenderer.CoursePurple : CourseUI.Ink;
        Image tagPill = CourseUI.Panel("Tag", card.transform, tagColor);
        if (CourseUI.Skin != null && CourseUI.Skin.pill != null) tagPill.sprite = CourseUI.Skin.pill;
        CourseUI.Place(tagPill.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28, -28), new Vector2(150, 40));
        TextMeshProUGUI tagText = CourseUI.Text("Text", tagPill.transform, tag, 20, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(tagText.rectTransform);

        if (course.IsRelay())
        {
            // Relays get a second pill, and say whether they have forks
            Image relayPill = CourseUI.Panel("Relay tag", card.transform, CourseRenderer.CoursePurple);
            if (CourseUI.Skin != null && CourseUI.Skin.pill != null) relayPill.sprite = CourseUI.Skin.pill;
            CourseUI.Place(relayPill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28, -28), new Vector2(150, 40));
            TextMeshProUGUI relayText = CourseUI.Text("Text", relayPill.transform, course.AnyForks() ? "Relay, forked" : "Relay", 20, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            CourseUI.Stretch(relayText.rectTransform);
        }

        TextMeshProUGUI title = CourseUI.Text("Name", card.transform, course.name, 30, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -330), new Vector2(360, 40));
        title.enableWordWrapping = false;

        string author = string.IsNullOrEmpty(course.author) ? "Unknown" : course.author;
        TextMeshProUGUI by = CourseUI.Text("Author", card.transform, "by " + author, 24, CourseUI.InkMuted);
        CourseUI.Place(by.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -372), new Vector2(360, 32));
        by.enableWordWrapping = false;

        MapDefinition map = MapDefinition.Find(course.mapId);
        string facts = (course.IsRelay() ? "Relay, " + course.legs + " legs  ·  " : course.controls.Count + " controls  ·  ")
            + course.LengthText() + (course.IsSprint() ? "  ·  Sprint" : "  ·  Forest") + (map != null ? "  ·  " + map.displayName : "");
        TextMeshProUGUI details = CourseUI.Text("Details", card.transform, facts, 22, CourseUI.Ink);
        CourseUI.Place(details.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -410), new Vector2(360, 30));
        details.enableWordWrapping = false;

        // In "Your runners" a relay needs a runner of your own for every leg
        GameModeHandler modeHandler = FindObjectOfType<GameModeHandler>();
        string tooFew = GameModes.RelayNeedsMoreRunners(course, modeHandler != null ? modeHandler.gameMode : GameModes.YourRunners);
        Button choose = CourseUI.Button("Choose", card.transform, tooFew != null ? "Needs " + course.legs + " runners" : selected ? "Chosen" : "Choose",
            tooFew != null ? CourseUI.ButtonKind.Light : selected ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Primary,
            () => { if (tooFew != null) UIToast.Show(tooFew); else SelectCourse(course); }, 24);
        CourseUI.Place((RectTransform)choose.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(240, 54));
    }

    public static bool IsAICourse(CourseData course)
    {
        return course != null && course.id != null && course.id.StartsWith("ai-");
    }

    void OpenAICourse()
    {
        AICoursePanel.Open(mapSelectPanel.transform, SelectCourse);
    }

    public void SelectCourse(CourseData course)
    {
        mapHandler.Select(course);
        Debug.Log($"Course selected: {course.name} by {course.author}");
        mapSelectPanel.SetActive(false);
    }

    // Kept for old button wiring: picks a course by name
    public void SelectMap(string mapName)
    {
        foreach (CourseData course in CourseLibrary.BuiltIn)
        {
            if (course.name == mapName)
            {
                SelectCourse(course);
                return;
            }
        }
    }

    async void FetchCommunity(bool force)
    {
        // At most every 5 minutes unless Refresh is pressed: every fetch counts against Unity Cloud's free reads
        if (fetching || (!force && Time.unscaledTime - lastFetch < 300f))
        {
            return;
        }
        fetching = true;
        fetchError = null;
        UpdateStatus(1);
        try
        {
            CourseLibrary.SetCommunity(await CloudCourses.FetchCommunityAsync());
        }
        catch (Exception e)
        {
            Debug.LogWarning("MapSelectUI: could not fetch community courses: " + e);
            fetchError = CloudCourses.Describe(e);
        }
        fetching = false;
        lastFetch = Time.unscaledTime;
        if (this != null && mapSelectPanel.activeSelf)
        {
            PopulateMapList();
        }
    }

    public void OpenPanel()
    {
        mapSelectPanel.SetActive(true);
        PopulateMapList();
        FetchCommunity(false);
    }

    public void ClosePanel()
    {
        mapSelectPanel.SetActive(false);
    }
}
