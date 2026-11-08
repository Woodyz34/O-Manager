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
// .omap/.xmap) on their computer (the computer's own file window, dragging the file onto the game, or pasting its
// path), names the map, says if it is a forest map, a sprint map or both, and may give a PNG of the map instead of
// the picture the game draws. They have to confirm that the club that owns the map allows it.
// Public (the default): the map is sent in, so it can be built into the game, and shared with every player until
// then. Private: only this player can use it; it is not sent in or shared, and other players only get a copy for
// the races in a lobby this player hosts on it (see PrivateMaps).
// The map is built on a background thread and saved in <persistent data>/Maps/<id>/ (see MapPackages).
public class MapImportPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    public const double PixelsPerMm = 16;   // about 400 dpi: sharp, and the picture stays a manageable size
    public const int MaxPictureSide = 16384; // the largest texture Unity can make

    static readonly string[] MapExtensions = { "ocd", "omap", "xmap" };
    static readonly string[] MapTypes = { "forest", "sprint", "both" };

    Action<MapDefinition> onImported;
    string mapPath, picturePath;
    MapFile mapFile;
    MapPreview preview;                     // small picture of the whole file, for the map edge
    double[] guessedEdge, edge;             // paper mm; null = the whole map
    string version;
    string mapType = "forest";
    bool contourTyped;   // the player wrote the contour interval: changing the type no longer changes it
    bool permission;
    bool isPrivate;      // Public is the default
    bool working;

    // Written by the background thread, shown in Update
    volatile string progressStep;
    volatile float progressShare;

    TextMeshProUGUI fileText, statusText;
    TMP_InputField pathField, pictureField, nameField, contourField;
    TMP_InputField senderField, clubField, contactField;   // who sends the map in, so the game's owner can reach them
    Button typeButton, pictureButton, permissionButton, importButton, cancelButton, edgeButton;
    Button publicButton, privateButton;
    TextMeshProUGUI edgeText, visibilityText;
    RectTransform contactRow;
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
        NativeFiles.StopDrop();
    }

    // ---- Layout ----

    void Build(Transform root)
    {
        Image card = CourseUI.CardPanel("Card", root);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 960));
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));

        CourseUI.Size(CourseUI.Text("Title", card.transform, "Import a map", 36, CourseUI.Ink, FontStyles.Bold), 44);
        CourseUI.Size(CourseUI.Text("Info", card.transform,
            "Pick a map file from OCAD (.ocd, OCAD 6 or newer) or OpenOrienteering Mapper (.omap or .xmap). " +
            "The game makes the grid, the heights and the map picture from it. Public maps are shared with everyone through Unity Cloud; private maps only you can use.",
            20, CourseUI.InkMuted), 56);

        // The map file: the computer's own file window, dragging it onto the game, or its path pasted in
        CourseUI.Size(CourseUI.Text("File title", card.transform, NativeFiles.CanDrop
            ? "Drag the map file onto the game window, choose it, or paste its path"
            : NativeFiles.CanBrowse ? "Choose the map file, or paste its path" : "Paste the map file's path (in Finder: right-click, hold Option, Copy as pathname)",
            22, CourseUI.Ink, FontStyles.Bold), 30);
        RectTransform fileRow = CourseUI.Rect("File row", card.transform);
        CourseUI.Row(fileRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(fileRow, 52);
        pathField = CourseUI.Input("Path", fileRow, "C:\\Users\\...\\map.ocd", 400, null);
        CourseUI.Size(pathField, -1, -1, 1);
        pathField.onEndEdit.AddListener(p => Choose(p));
        if (NativeFiles.CanBrowse)
            CourseUI.Size(CourseUI.Button("Browse", fileRow, "Choose file...", CourseUI.ButtonKind.Secondary, BrowseMapFile, 20), -1, 220);
        NativeFiles.StartDrop(OnDropped);

        fileText = CourseUI.Text("File", card.transform, "No map file chosen yet.", 21, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(fileText, 32);

        RectTransform optionRow = CourseUI.Rect("Options", card.transform);
        CourseUI.Row(optionRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(optionRow, 52);
        TextMeshProUGUI nameLabel = CourseUI.Text("Name label", optionRow, "Name:", 20, CourseUI.Ink, FontStyles.Bold);
        nameLabel.alignment = TextAlignmentOptions.MidlineRight;
        CourseUI.Size(nameLabel, -1, 70);
        nameField = CourseUI.Input("Map name", optionRow, "The map's name in the game", 32, null);
        CourseUI.Size(nameField, -1, 330);
        typeButton = CourseUI.Button("Type", optionRow, "", CourseUI.ButtonKind.Light, NextType, 20);
        CourseUI.Size(typeButton, -1, 300);
        TextMeshProUGUI contourLabel = CourseUI.Text("Contour label", optionRow, "Contour interval (m):", 20, CourseUI.Ink, FontStyles.Bold);
        contourLabel.alignment = TextAlignmentOptions.MidlineRight;
        CourseUI.Size(contourLabel, -1, 230);
        contourField = CourseUI.Input("Contour interval", optionRow, "5", 5, null);
        contourField.contentType = TMP_InputField.ContentType.DecimalNumber;
        contourField.onValueChanged.AddListener(_ => contourTyped = true);
        CourseUI.Size(contourField, -1, 110);

        // An own picture instead of the one the game draws (optional)
        RectTransform pictureRow = CourseUI.Rect("Picture row", card.transform);
        CourseUI.Row(pictureRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(pictureRow, 52);
        pictureField = CourseUI.Input("Picture path", pictureRow, "Own map picture (PNG of the whole map), optional: path", 400, null);
        CourseUI.Size(pictureField, -1, -1, 1);
        pictureField.onEndEdit.AddListener(p => ChoosePicture(p));
        pictureButton = CourseUI.Button("Picture", pictureRow, "", CourseUI.ButtonKind.Light, OnPictureButton, 20);
        CourseUI.Size(pictureButton, -1, 300);

        // The edge of the finished map: parts of the file outside it (unfinished mapping) are left out
        RectTransform edgeRow = CourseUI.Rect("Edge row", card.transform);
        CourseUI.Row(edgeRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(edgeRow, 52);
        edgeText = CourseUI.Text("Edge", edgeRow, "", 20, CourseUI.Ink);
        edgeText.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(edgeText, -1, -1, 1);
        edgeButton = CourseUI.Button("Edge button", edgeRow, "Check map edge...", CourseUI.ButtonKind.Secondary, OpenEdge, 20);
        CourseUI.Size(edgeButton, -1, 300);

        // Who can use the map: everyone (Public, the default) or only this player (Private)
        RectTransform visibilityRow = CourseUI.Rect("Visibility row", card.transform);
        CourseUI.Row(visibilityRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(visibilityRow, 56);
        publicButton = CourseUI.Button("Public", visibilityRow, "Public", CourseUI.ButtonKind.Primary, () => { isPrivate = false; Refresh(); }, 20);
        CourseUI.Size(publicButton, -1, 150);
        privateButton = CourseUI.Button("Private", visibilityRow, "Private", CourseUI.ButtonKind.Light, () => { isPrivate = true; Refresh(); }, 20);
        CourseUI.Size(privateButton, -1, 150);
        visibilityText = CourseUI.Text("Visibility", visibilityRow, "", 17, CourseUI.InkMuted);
        visibilityText.alignment = TextAlignmentOptions.MidlineLeft;
        visibilityText.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(visibilityText, -1, -1, 1);

        // The permission checkbox: a box that fills orange, and the text
        permissionButton = CourseUI.Button("Permission", card.transform, "", CourseUI.ButtonKind.Light, () => { permission = !permission; Refresh(); }, 18);
        CourseUI.Size(permissionButton, 76);
        TextMeshProUGUI permissionText = permissionButton.GetComponentInChildren<TextMeshProUGUI>();
        permissionText.text = CloudMaps.PermissionText;
        permissionText.textWrappingMode = TextWrappingModes.Normal;
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

        // Every public map is sent in: if a club asks about its map, the game's owner knows who to ask
        RectTransform contact = CourseUI.Rect("Contact", card.transform);
        contactRow = contact;
        CourseUI.Row(contact.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(contact, 52);
        senderField = CourseUI.Input("Your name", contact, "Your full name", 40, _ => Refresh());
        clubField = CourseUI.Input("Club", contact, "Club that owns the map", 60, _ => Refresh());
        contactField = CourseUI.Input("Contact", contact, "E-mail or phone", 60, _ => Refresh());
        // The e-mail gets the most room, so a long address shows whole
        CourseUI.Size(senderField, -1, 270, 0);
        CourseUI.Size(clubField, -1, 270, 0);
        CourseUI.Size(contactField, -1, 400, 1);
        contactField.textComponent.fontSize = 21;
        contactField.placeholder.GetComponent<TextMeshProUGUI>().fontSize = 21;
        senderField.SetTextWithoutNotify(PlayerPrefs.GetString("MapImport.Sender", ""));   // no Refresh yet: the buttons below are not made
        contactField.SetTextWithoutNotify(PlayerPrefs.GetString("MapImport.Contact", ""));

        statusText = CourseUI.Text("Status", card.transform, "", 20, CourseUI.Forest, FontStyles.Bold);
        CourseUI.Size(statusText, 50);

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        cancelButton = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Close, 24);
        importButton = CourseUI.Button("Import", buttons, "Import map", CourseUI.ButtonKind.Primary, () => StartCoroutine(Import()), 24);

        Refresh();
    }

    // ---- Choosing files ----

    static string LastFolder()
    {
        string folder = PlayerPrefs.GetString("MapImport.Folder", "");
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) return folder;
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    void BrowseMapFile()
    {
        if (working) return;
        string path = NativeFiles.OpenFile("Choose the map file", "Map files (OCAD, OOM)", MapExtensions, LastFolder());
        if (path != null) Choose(path);
    }

    // A file dragged onto the game window: a PNG is the picture, anything else the map file
    void OnDropped(string path)
    {
        if (working || string.IsNullOrEmpty(path)) return;
        if (Path.GetExtension(path).ToLowerInvariant() == ".png") ChoosePicture(path);
        else Choose(path);
    }

    static string CleanPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "" : path.Trim().Trim('"', '\'');
    }

    void ChoosePicture(string path)
    {
        if (working) return;
        path = CleanPath(path);
        pictureField.SetTextWithoutNotify(path);
        if (path.Length == 0) { picturePath = null; Refresh(); return; }
        if (!File.Exists(path)) { SetStatus("There is no file at " + path, true); return; }
        if (Path.GetExtension(path).ToLowerInvariant() != ".png") { SetStatus("The picture has to be a PNG file.", true); return; }
        picturePath = path;
        Refresh();
    }

    void Choose(string path)
    {
        if (working) return;
        path = CleanPath(path);
        pathField.SetTextWithoutNotify(path);
        if (path.Length == 0 || path == mapPath) return;
        if (!File.Exists(path))
        {
            SetStatus("There is no file at " + path, true);
            return;
        }
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (Array.IndexOf(MapExtensions, ext) < 0)
        {
            SetStatus("That is not a map file. The game reads OCAD (.ocd) and OpenOrienteering Mapper (.omap, .xmap) files.", true);
            return;
        }
        PlayerPrefs.SetString("MapImport.Folder", Path.GetDirectoryName(path));
        mapPath = path;
        mapFile = null;
        preview = null;
        guessedEdge = edge = null;
        version = null;
        nameField.text = NiceName(Path.GetFileNameWithoutExtension(path));
        SetStatus("Reading " + Path.GetFileName(path) + " ...", false);
        // Read it straight away, so the version and the scale can be shown and the import goes quicker
        string reading = path;
        Task.Run(() =>
        {
            byte[] bytes = File.ReadAllBytes(reading);
            string v = MapPackageBuilder.DescribeVersion(bytes);
            MapFile m = MapPackageBuilder.ReadMapFile(bytes);
            // A small picture and a guess at where the finished map ends; a failure here never stops the import
            MapPreview p = null;
            double[] g = null;
            try
            {
                p = MapBoundary.Preview(m);
                g = MapBoundary.Guess(m, p);
            }
            catch (Exception e)
            {
                Debug.LogWarning("MapImportPanel: no map edge guess: " + e);
            }
            return (v, m, p, g);
        }).ContinueWith(t =>
        {
            // Picked up on the main thread in Update; a file chosen after this one wins
            if (reading == mapPath) pendingRead = t;
        });
        Refresh();
    }

    Task<(string, MapFile, MapPreview, double[])> pendingRead;

    void Update()
    {
        if (pendingRead != null)
        {
            Task<(string, MapFile, MapPreview, double[])> t = pendingRead;
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
                preview = t.Result.Item3;
                guessedEdge = edge = t.Result.Item4;
                mapType = SymbolRules.LooksLikeSprint(mapFile) ? "sprint" : "forest";
                contourTyped = false;
                ShowTypicalContours();
                string warnings = mapFile.warnings.Count > 0 ? "  (" + mapFile.warnings.Count + " things in the file were skipped)" : "";
                SetStatus($"{version}, 1:{mapFile.scale:0}, {mapFile.objects.Count} objects. Looks like a {mapType} map." + warnings
                    + (guessedEdge != null ? " Parts look unfinished: check the map edge." : ""), false);
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
            ChoosePicture("");      // back to the picture the game draws
            return;
        }
        if (!NativeFiles.CanBrowse)
        {
            SetStatus("Paste the PNG's path in the field to the left.", false);
            return;
        }
        string path = NativeFiles.OpenFile("Choose a PNG of the whole map", "PNG pictures", new[] { "png" },
            mapPath != null ? Path.GetDirectoryName(mapPath) : LastFolder());
        if (path != null) ChoosePicture(path);
    }

    void OpenEdge()
    {
        if (working || preview == null) return;
        MapEdgeEditor.Open(transform, preview, edge, guessedEdge, chosen => { edge = chosen; Refresh(); });
    }

    // "Tranås_sprint_20190125 ocad9 (1)" -> "Tranås sprint": no copy numbers, dates or OCAD versions
    static string NiceName(string file)
    {
        string s = file.Replace('_', ' ');
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\(\d+\)", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\b(ocad|ocd)\s*\d*\b", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(19|20)\d{2}[-.]?\d{2}[-.]?\d{2}\b", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '.');
        if (s.Length == 0) s = file;
        if (s.Length > 32) s = s.Substring(0, 32).Trim();
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    void NextType()
    {
        mapType = MapTypes[(Array.IndexOf(MapTypes, mapType) + 1) % MapTypes.Length];
        if (!contourTyped) ShowTypicalContours();
        Refresh();
    }

    // Map files don't say what contour interval they use, so the field starts with the most usual one for the type
    void ShowTypicalContours()
    {
        contourField.SetTextWithoutNotify(SymbolRules.TypicalInterval(mapType).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    void Refresh()
    {
        fileText.text = mapPath == null ? "No map file chosen yet." : "Map file: " + Path.GetFileName(mapPath) + (version != null ? "  (" + version + ")" : "");
        CourseUI.SetButtonText(typeButton, mapType == "sprint" ? "Type: Sprint map" : mapType == "both" ? "Type: Forest and sprint" : "Type: Forest map");
        CourseUI.SetButtonText(pictureButton, string.IsNullOrEmpty(picturePath)
            ? (NativeFiles.CanBrowse ? "Choose picture..." : "Drawn by the game") : "Remove picture");
        edgeText.text = mapFile == null ? "Map edge: choose a map file first."
            : edge == null ? "Map edge: the whole map file is used."
            : edge == guessedEdge ? "Map edge: the game left out parts that look unfinished. Check that it is right."
            : "Map edge: your own edge. Everything outside it is left out.";
        edgeText.color = edge != null && edge == guessedEdge ? CourseUI.Accent : CourseUI.Ink;
        edgeButton.interactable = !working && preview != null;
        permissionBox.enabled = permission;
        CourseUI.SetButtonColor(publicButton, isPrivate ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Primary);
        CourseUI.SetButtonColor(privateButton, isPrivate ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        publicButton.interactable = !working;
        privateButton.interactable = !working;
        visibilityText.text = isPrivate
            ? "Private: only you can use the map. It is not sent in or shared. Others can only race on it in a lobby you host, and they do not keep it."
            : "Public: the map is sent in and every player can use it.";
        // A private map is not sent in, so no contact details are needed
        if (contactRow.gameObject.activeSelf == isPrivate) contactRow.gameObject.SetActive(!isPrivate);
        bool ready = mapFile != null && permission && !working && (isPrivate || ContactGiven());
        importButton.interactable = ready;
        cancelButton.interactable = !working;
        typeButton.interactable = !working;
        pictureButton.interactable = !working;
        permissionButton.interactable = !working;
    }

    bool ContactGiven()
    {
        return senderField.text.Trim().Length >= 2 && clubField.text.Trim().Length >= 2 && contactField.text.Trim().Length >= 5;
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

    // Builds a map package from a map file, the same way on every computer: here, and when a map another player
    // shared is downloaded (SharedMaps). Safe on a background thread. ownPng: the player's own picture, or null.
    public static MapPackage BuildPackage(MapFile source, string name, string mapType, byte[] ownPng, Action<string, float> progress)
    {
        int w = 0, h = 0;
        if (ownPng != null)
        {
            if (!PngFile.TrySize(ownPng, out w, out h)) throw new InvalidDataException("The picture is not a PNG file.");
            if (w > MaxPictureSide || h > MaxPictureSide)
                throw new InvalidDataException($"The picture is {w} x {h} pixels; the game can show at most {MaxPictureSide} pixels each way. Export it at a lower resolution.");
        }
        bool sprintCells = mapType != "forest";   // sprint maps and maps that are both get 2 m cells
        MapPackage package = MapPackageBuilder.Build(source, name, sprintCells, PixelsPerMm, MaxPictureSide, ownPng == null, progress);
        if (ownPng != null && !MapPackageBuilder.FitOwnPicture(package, w, h, out string problem)) throw new InvalidDataException(problem);
        package.info.mapType = mapType;
        return package;
    }

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
        if (!isPrivate && !ContactGiven())
        {
            SetStatus("Write your name, the club that owns the map and your e-mail or phone, so the club can be asked about the map.", true);
            yield break;
        }
        if (!double.TryParse(contourField.text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double contours)
            || contours < 0.5 || contours > 50)
        {
            SetStatus("Write the contour interval in metres, for instance 5 or 2.5.", true);
            yield break;
        }
        bool keepPrivate = isPrivate;
        string sender = senderField.text.Trim(), club = clubField.text.Trim(), contactInfo = contactField.text.Trim();
        if (keepPrivate)
        {
            sender = club = contactInfo = "";   // not sent anywhere
        }
        else
        {
            PlayerPrefs.SetString("MapImport.Sender", sender);
            PlayerPrefs.SetString("MapImport.Contact", contactInfo);
        }
        working = true;
        Refresh();

        // Things that need Unity are worked out here, on the main thread
        string id = MapPackages.MakeId(name);
        string target = Path.Combine(MapPackages.LocalRoot, id);
        string author = PlayerPrefs.GetString("PlayerNickName", "");
        string picture = picturePath;
        MapFile source = mapFile;
        string type = mapType;
        source.contourInterval = contours;
        source.boundary = edge;
        string sourceName = Path.GetFileName(mapPath);
        string sourcePath = mapPath;
        MapPackage package = null;
        byte[] ownPng = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Task work = Task.Run(() =>
        {
            if (picture != null) ownPng = File.ReadAllBytes(picture);
            package = BuildPackage(source, name, type, ownPng, (step, share) => { progressShare = share; progressStep = step; });
            package.info.id = id;
            package.info.sourceFile = sourceName;
            package.info.author = author;
            package.info.mapType = type;
            package.info.ownerClub = club;
            package.info.sender = sender;
            package.info.contact = contactInfo;
            package.info.permissionConfirmed = true;
            package.info.isPrivate = keepPrivate;
            MapPackageBuilder.WriteFolder(target, package, ownPng, (step, share) => { progressShare = share; progressStep = step; });
            // The map file is kept with the map: a private map is handed from it to the players in a lobby its owner
            // hosts, and the map edge can be changed later (MapEditPanel)
            File.Copy(sourcePath, PrivateMaps.SourceFile(target, package.info), true);
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

        // Send it in: the map file goes to the game's owner (see CloudMaps); the map is already usable here.
        // A private map stays on this computer.
        string sentNote;
        if (keepPrivate)
        {
            sentNote = " It is private: only you can use it. Players in a lobby you host on it get it for those races.";
        }
        else
        {
            progressStep = null;
            SetStatus("Sending the map in ...", false);
            Task<byte[]> read = Task.Run(() => File.ReadAllBytes(sourcePath));
            while (!read.IsCompleted) yield return null;
            Task submit = read.IsFaulted ? Task.FromException(read.Exception) : CloudMaps.SubmitAsync(package.info, read.Result, sourceName, ownPng);
            while (!submit.IsCompleted) yield return null;
            if (submit.IsFaulted)
            {
                Debug.LogWarning("MapImportPanel: could not send the map in: " + submit.Exception);
                sentNote = " It could not be sent in (" + CloudCourses.Describe(submit.Exception) + "), so for now it is only on this computer.";
            }
            else
            {
                sentNote = " It is shared with all players until it is built into the game.";
            }
        }
        Debug.Log($"MapImportPanel: imported {name} as {id} in {watch.Elapsed.TotalSeconds:F1} s -> {target}");

        working = false;
        progressStep = null;
        MapDefinition.Refresh();
        MapDefinition map = MapDefinition.All.Find(m => m.id == id);
        onImported?.Invoke(map);
        if (sentNote.Length > 0) UIToast.Show("Imported " + name + "." + sentNote, 7f);
        Destroy(gameObject);
    }
}
