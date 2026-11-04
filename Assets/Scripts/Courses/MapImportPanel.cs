using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MapImport;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The "Import map" pop-up in the course builder. The player picks a map file (OCAD .ocd or OpenOrienteering Mapper
// .omap/.xmap) on their computer, names the map, says if it is a forest or a sprint map, and may give a PNG of the
// map instead of the picture the game draws. They have to confirm that the club that owns the map allows it.
// The map is built on a background thread and saved in <persistent data>/Maps/<id>/ (see MapPackages).
public class MapImportPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    const double PixelsPerMm = 16;          // about 400 dpi: sharp, and the picture stays a manageable size
    const int MaxPictureSide = 16384;       // the largest texture Unity can make

    enum Browse { MapFile, Picture }

    Action<MapDefinition> onImported;
    Browse browsing = Browse.MapFile;
    string folder;
    string mapPath, picturePath;
    MapFile mapFile;
    string version;
    bool sprint;
    bool permission;
    bool working;

    // Written by the background thread, shown in Update
    volatile string progressStep;
    volatile float progressShare;

    TextMeshProUGUI folderText, fileText, statusText, browseTitle;
    RectTransform list;
    ScrollRect listScroll;
    TMP_InputField nameField;
    Button typeButton, pictureButton, permissionButton, importButton, cancelButton;
    Image permissionBox;

    public static MapImportPanel Open(Transform canvas, Action<MapDefinition> onImported)
    {
        var dim = CourseUI.Panel("Import map", canvas, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
        CourseUI.Stretch(dim.rectTransform);
        MapImportPanel panel = dim.gameObject.AddComponent<MapImportPanel>();
        panel.onImported = onImported;
        panel.Build(dim.transform);
        IsOpen = true;
        return panel;
    }

    void OnDestroy()
    {
        IsOpen = false;
    }

    // ---- Layout ----

    void Build(Transform root)
    {
        Image card = CourseUI.CardPanel("Card", root);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 960));
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));

        CourseUI.Size(CourseUI.Text("Title", card.transform, "Import a map", 40, CourseUI.Ink, FontStyles.Bold), 50);
        CourseUI.Size(CourseUI.Text("Info", card.transform,
            "Pick a map file from OCAD (.ocd, version 11 or newer) or OpenOrienteering Mapper (.omap or .xmap). " +
            "The game makes the grid, the heights and the map picture from it. The map is kept on this computer until it is built into the game.",
            20, CourseUI.InkMuted), 56);

        browseTitle = CourseUI.Text("Browse title", card.transform, "", 22, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(browseTitle, 30);

        RectTransform folderRow = CourseUI.Rect("Folder row", card.transform);
        CourseUI.Row(folderRow.gameObject, 8, new RectOffset(0, 0, 0, 0));
        CourseUI.Size(folderRow, 44);
        CourseUI.Size(CourseUI.Button("Up", folderRow, "Up", CourseUI.ButtonKind.Light, GoUp, 20), -1, 80);
        foreach (var place in Places())
        {
            string target = place.Value;
            CourseUI.Size(CourseUI.Button(place.Key, folderRow, place.Key, CourseUI.ButtonKind.Light, () => ShowFolder(target), 18), -1, 130);
        }
        folderText = CourseUI.Text("Folder", folderRow, "", 18, CourseUI.InkMuted);
        folderText.alignment = TextAlignmentOptions.MidlineLeft;
        folderText.enableWordWrapping = false;
        CourseUI.Size(folderText, -1, -1, 1);

        list = CourseUI.ScrollList("Files", card.transform, out listScroll);
        CourseUI.Column(list.gameObject, 4, new RectOffset(0, 8, 0, 0));
        CourseUI.Size(list.parent.GetComponent<Image>(), 300);

        fileText = CourseUI.Text("File", card.transform, "No map file chosen yet.", 21, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(fileText, 32);

        RectTransform optionRow = CourseUI.Rect("Options", card.transform);
        CourseUI.Row(optionRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(optionRow, 52);
        nameField = CourseUI.Input("Map name", optionRow, "Map name", 32, null);
        CourseUI.Size(nameField, -1, 380);
        typeButton = CourseUI.Button("Type", optionRow, "", CourseUI.ButtonKind.Light, () => { sprint = !sprint; Refresh(); }, 20);
        CourseUI.Size(typeButton, -1, 250);
        pictureButton = CourseUI.Button("Picture", optionRow, "", CourseUI.ButtonKind.Light, OnPictureButton, 20);
        CourseUI.Size(pictureButton, -1, 460);

        // The permission checkbox: a box that fills orange, and the text
        permissionButton = CourseUI.Button("Permission", card.transform, "", CourseUI.ButtonKind.Light, () => { permission = !permission; Refresh(); }, 18);
        CourseUI.Size(permissionButton, 76);
        TextMeshProUGUI permissionText = permissionButton.GetComponentInChildren<TextMeshProUGUI>();
        permissionText.text = "I have permission from the club that owns this map to use it in O-Manager, and the club knows how the game uses it. " +
                              "I am responsible for this, not the maker of the game.";
        permissionText.enableWordWrapping = true;
        permissionText.alignment = TextAlignmentOptions.MidlineLeft;
        permissionText.fontStyle = FontStyles.Normal;
        CourseUI.Stretch(permissionText.rectTransform, 70, 16, 4, 4);
        Image box = CourseUI.Panel("Box", permissionButton.transform, Color.white);
        CourseUI.Place(box.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(34, 34));
        Outline outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = CourseUI.Ink;
        outline.effectDistance = new Vector2(2, -2);
        permissionBox = CourseUI.Panel("Tick", box.transform, CourseUI.Accent);
        CourseUI.Stretch(permissionBox.rectTransform, 6, 6, 6, 6);

        statusText = CourseUI.Text("Status", card.transform, "", 20, CourseUI.Forest, FontStyles.Bold);
        CourseUI.Size(statusText, 56);

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        cancelButton = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Close, 24);
        importButton = CourseUI.Button("Import", buttons, "Import map", CourseUI.ButtonKind.Primary, () => StartCoroutine(Import()), 24);

        folder = PlayerPrefs.GetString("MapImport.Folder", "");
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = DefaultFolder();
        ShowFolder(folder);
    }

    static string DefaultFolder()
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) return downloads;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    static List<KeyValuePair<string, string>> Places()
    {
        var places = new List<KeyValuePair<string, string>>();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        void Add(string label, string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) places.Add(new KeyValuePair<string, string>(label, path));
        }
        Add("Downloads", Path.Combine(home, "Downloads"));
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        return places;
    }

    // ---- Browsing ----

    void GoUp()
    {
        DirectoryInfo parent = string.IsNullOrEmpty(folder) ? null : Directory.GetParent(folder);
        ShowFolder(parent != null ? parent.FullName : null);
    }

    // null shows the drives
    void ShowFolder(string path)
    {
        folder = path;
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
        browseTitle.text = browsing == Browse.MapFile ? "Choose the map file" : "Choose a PNG of the whole map (exported with all objects)";
        folderText.text = path ?? "This computer";

        try
        {
            if (path == null)
            {
                foreach (string drive in Directory.GetLogicalDrives()) AddEntry(drive, drive, true);
                return;
            }
            var folders = new List<string>(Directory.GetDirectories(path));
            folders.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in folders)
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith(".") || name.StartsWith("$")) continue;
                if ((File.GetAttributes(f) & FileAttributes.Hidden) != 0) continue;
                AddEntry(name, f, true);
            }
            var files = new List<string>(Directory.GetFiles(path));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            int shown = 0;
            foreach (string f in files)
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                bool wanted = browsing == Browse.MapFile ? ext == ".ocd" || ext == ".omap" || ext == ".xmap" : ext == ".png";
                if (!wanted) continue;
                AddEntry(Path.GetFileName(f), f, false);
                shown++;
            }
            if (shown == 0)
            {
                TextMeshProUGUI none = CourseUI.Text("None", list, browsing == Browse.MapFile ? "No map files in this folder." : "No PNG files in this folder.", 19, CourseUI.InkMuted, FontStyles.Italic);
                CourseUI.Size(none, 34);
            }
            PlayerPrefs.SetString("MapImport.Folder", path);
        }
        catch (Exception e)
        {
            TextMeshProUGUI error = CourseUI.Text("Error", list, "Could not open this folder: " + e.Message, 19, CourseUI.Danger);
            CourseUI.Size(error, 34);
        }
        listScroll.verticalNormalizedPosition = 1f;
    }

    void AddEntry(string label, string path, bool isFolder)
    {
        bool chosen = path == mapPath || path == picturePath;
        Button button = CourseUI.Button(label, list, (isFolder ? "[ ]  " : "") + label,
            chosen ? CourseUI.ButtonKind.Secondary : CourseUI.ButtonKind.Light,
            () => { if (isFolder) ShowFolder(path); else Choose(path); }, 19);
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.fontStyle = isFolder ? FontStyles.Bold : FontStyles.Normal;
        CourseUI.Size(button, 40);
    }

    void Choose(string path)
    {
        if (working) return;
        if (browsing == Browse.Picture)
        {
            picturePath = path;
            browsing = Browse.MapFile;
            ShowFolder(Path.GetDirectoryName(mapPath ?? path));
            Refresh();
            return;
        }
        mapPath = path;
        mapFile = null;
        version = null;
        nameField.text = Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
        SetStatus("Reading " + Path.GetFileName(path) + " ...", false);
        // Read it straight away, so the version and the scale can be shown and the import goes quicker
        string reading = path;
        Task.Run(() =>
        {
            byte[] bytes = File.ReadAllBytes(reading);
            string v = MapPackageBuilder.DescribeVersion(bytes);
            MapFile m = MapPackageBuilder.ReadMapFile(bytes);
            return (v, m);
        }).ContinueWith(t =>
        {
            // Picked up on the main thread in Update; a file chosen after this one wins
            if (reading == mapPath) pendingRead = t;
        });
        ShowFolder(folder);
        Refresh();
    }

    Task<(string, MapFile)> pendingRead;

    void Update()
    {
        if (pendingRead != null)
        {
            Task<(string, MapFile)> t = pendingRead;
            pendingRead = null;
            if (t.IsFaulted)
            {
                mapFile = null;
                SetStatus(Message(t.Exception), true);
            }
            else if (t.Result.Item2 != null)
            {
                version = t.Result.Item1;
                mapFile = t.Result.Item2;
                sprint = SymbolRules.LooksLikeSprint(mapFile);
                string warnings = mapFile.warnings.Count > 0 ? "  (" + mapFile.warnings.Count + " things in the file were skipped)" : "";
                SetStatus($"{version}, 1:{mapFile.scale:0}, {mapFile.objects.Count} objects. Looks like a {(sprint ? "sprint" : "forest")} map." + warnings, false);
            }
            Refresh();
        }
        if (working && progressStep != null)
        {
            SetStatus($"{progressStep} ... {Mathf.RoundToInt(progressShare * 100)} %", false);
        }
    }

    void OnPictureButton()
    {
        if (working) return;
        if (!string.IsNullOrEmpty(picturePath))
        {
            picturePath = null;      // back to the picture the game draws
            Refresh();
            return;
        }
        browsing = browsing == Browse.Picture ? Browse.MapFile : Browse.Picture;
        ShowFolder(folder);
        Refresh();
    }

    void Refresh()
    {
        fileText.text = mapPath == null ? "No map file chosen yet." : "Map file: " + Path.GetFileName(mapPath) + (version != null ? "  (" + version + ")" : "");
        CourseUI.SetButtonText(typeButton, sprint ? "Type: Sprint (2 m)" : "Type: Forest (10 m)");
        CourseUI.SetButtonText(pictureButton, browsing == Browse.Picture ? "Picture: choose a PNG above"
            : string.IsNullOrEmpty(picturePath) ? "Picture: drawn by the game" : "Picture: " + Path.GetFileName(picturePath) + "  (remove)");
        permissionBox.enabled = permission;
        bool ready = mapFile != null && permission && !working && browsing == Browse.MapFile;
        importButton.interactable = ready;
        cancelButton.interactable = !working;
        typeButton.interactable = !working;
        pictureButton.interactable = !working;
        permissionButton.interactable = !working;
    }

    void SetStatus(string message, bool error)
    {
        statusText.text = message;
        statusText.color = error ? CourseUI.Danger : CourseUI.Forest;
    }

    static string Message(Exception e)
    {
        while (e is AggregateException && e.InnerException != null) e = e.InnerException;
        if (e is OutOfMemoryException) return "The map is too big to import on this computer.";
        return e.Message;
    }

    void Close()
    {
        if (working) return;
        Destroy(gameObject);
    }

    // ---- Import ----

    IEnumerator Import()
    {
        string name = nameField.text.Trim();
        if (mapFile == null || working) yield break;
        if (!permission)
        {
            SetStatus("Tick the box to confirm you have the club's permission.", true);
            yield break;
        }
        if (name.Length == 0)
        {
            SetStatus("Give the map a name.", true);
            yield break;
        }
        working = true;
        Refresh();

        // Things that need Unity are worked out here, on the main thread
        string id = MapPackages.MakeId(name);
        string target = Path.Combine(MapPackages.LocalRoot, id);
        string author = PlayerPrefs.GetString("PlayerNickName", "");
        string picture = picturePath;
        MapFile source = mapFile;
        bool isSprint = sprint;
        string sourceName = Path.GetFileName(mapPath);
        MapPackage package = null;
        byte[] ownPng = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Task work = Task.Run(() =>
        {
            if (picture != null)
            {
                ownPng = File.ReadAllBytes(picture);
                if (!PngFile.TrySize(ownPng, out int w, out int h)) throw new InvalidDataException("The picture is not a PNG file.");
                if (w > MaxPictureSide || h > MaxPictureSide)
                    throw new InvalidDataException($"The picture is {w} x {h} pixels; the game can show at most {MaxPictureSide} pixels each way. Export it at a lower resolution.");
            }
            package = MapPackageBuilder.Build(source, name, isSprint, PixelsPerMm, MaxPictureSide, ownPng == null,
                (step, share) => { progressShare = share; progressStep = step; });
            if (ownPng != null)
            {
                PngFile.TrySize(ownPng, out int w, out int h);
                if (!MapPackageBuilder.FitOwnPicture(package, w, h, out string problem)) throw new InvalidDataException(problem);
            }
            package.info.id = id;
            package.info.sourceFile = sourceName;
            package.info.author = author;
            package.info.permissionConfirmed = true;
            MapPackageBuilder.WriteFolder(target, package, ownPng, (step, share) => { progressShare = share; progressStep = step; });
        });
        while (!work.IsCompleted) yield return null;

        if (work.IsFaulted)
        {
            working = false;
            progressStep = null;
            Debug.LogError("MapImportPanel: " + work.Exception);
            SetStatus("Could not import the map: " + Message(work.Exception), true);
            try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { /* leave it */ }
            Refresh();
            yield break;
        }
        if (ownPng != null)
        {
            try
            {
                MapPackages.WriteThumbnail(target, ownPng);
            }
            catch (Exception e)
            {
                Debug.LogWarning("MapImportPanel: no thumbnail: " + e.Message);
            }
        }
        foreach (string line in package.log) Debug.Log("MapImportPanel: " + line);
        Debug.Log($"MapImportPanel: imported {name} as {id} in {watch.Elapsed.TotalSeconds:F1} s -> {target}");

        working = false;
        progressStep = null;
        MapDefinition.Refresh();
        MapDefinition map = MapDefinition.All.Find(m => m.id == id);
        onImported?.Invoke(map);
        Destroy(gameObject);
    }
}
