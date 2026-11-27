using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MapImport;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "Edit map" in the course builder's map list, for a map this player imported on this computer: its name, who may use
// it (Public or Private), the club that owns it, and the map edge (the part of the map file that is used).
//  - Public -> Private: the map is taken out of the cloud (CloudMaps.WithdrawAsync); other players' copies are deleted
//    the next time they look for shared maps.
//  - Private -> Public: the map is sent in and shared, as when it is imported as Public.
//  - A public map that changes is sent in again, so the other players get the new version.
//  - A new map edge or new terrain speeds build the map again from the map file kept with it. Maps imported before the file was kept get
//    it back from the player's own Cloud Save Files when they are public.
//  - Forest or Sprint can be changed; the map is built again, since the symbols mean different things on a sprint map.
//  - "Update map file" (2026-11-19) builds the map again from a new version of the map file, with the same id, so the
//    courses on it stay (before, the map had to be deleted and imported again, and its courses went with it).
//  - "Uncertain contours" (2026-11-27) asks about the contours whose height the game is unsure of (HeightQuestionPanel):
//    higher, lower or as high as the contour next to them. The answers are kept with the map (map.json and points.json)
//    and the 3D ground is made again with them when it is saved, without building the whole map again.
//  - Delete removes the map from this computer and from the cloud, so no player gets it any more (other players'
//    copies are deleted the next time they look for shared maps). It asks once more first.
// Courses on the map stay where they are: the map's coordinates do not depend on the edge (MapPackageBuilder.Build).
public class MapEditPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    MapDefinition map;
    MapPackageInfo info;
    Action<MapDefinition, bool> onSaved;   // the saved map, and whether it was built again (a new edge)
    bool isPrivate;
    double[] edge;
    bool edgeChanged;
    TerrainSpeeds terrain;
    TerrainLook look;          // ground, trees and undergrowth for the 3D view of a TV control; needs no rebuild
    double[] heightAnswers;    // answers about uncertain contours (HeightQuestionPanel); only the 3D ground is made again
    bool terrainChanged;
    string mapType;
    float deleteConfirmUntil = -1f;
    bool working;
    volatile string progressStep;
    volatile float progressShare;

    TMP_InputField nameField, clubField, senderField, contactField;
    Button publicButton, privateButton, edgeButton, terrainButton, lookButton, saveButton, cancelButton;
    Button forestButton, sprintButton, deleteButton;
    TextMeshProUGUI typeText;
    TextMeshProUGUI visibilityText, edgeText, terrainText, lookText, statusText, fileText, heightsText;
    Button heightsButton;
    Button fileButton;
    // A new version of the map file (2026-11-19): the map is built again from it, keeping its id, so the courses on it stay
    string newSourceName;
    bool FileChanged => newSourceName != null;
    RectTransform contactRow;

    public static void Open(Transform canvas, MapDefinition map, Action<MapDefinition, bool> onSaved)
    {
        if (IsOpen || map == null || !map.CanEdit) return;
        MapPackageInfo info = ReadInfo(map.folder);
        if (info == null)
        {
            UIToast.Show("The map's files could not be read.");
            return;
        }
        var dim = CourseUI.Panel("Edit map", canvas, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
        CourseUI.Stretch(dim.rectTransform);
        MapEditPanel panel = dim.gameObject.AddComponent<MapEditPanel>();
        panel.map = map;
        panel.info = info;
        panel.onSaved = onSaved;
        panel.isPrivate = info.isPrivate;
        panel.edge = info.boundary != null && info.boundary.Length >= 6 ? info.boundary : null;
        panel.terrain = info.terrain != null ? info.terrain.Copy() : new TerrainSpeeds();
        panel.look = info.look != null ? info.look.Safe() : new TerrainLook();
        panel.mapType = string.IsNullOrEmpty(info.mapType) ? "forest" : info.mapType;
        panel.heightAnswers = HeightAnswers.Clean(info.heightAnswers);
        panel.Build(dim.transform);
        IsOpen = true;
        // Answers given before the game stopped (they are written after every answer): saved now
        double[] pending = ReadPending(map.folder);
        if (pending != null && !SameEdge(pending, panel.heightAnswers))
        {
            panel.heightAnswers = pending;
            panel.SaveHeightsNow();
        }
        else if (pending != null) DeletePending(map.folder);
        if (info.hasHeights) panel.CountQuestions();
    }

    static MapPackageInfo ReadInfo(string folder)
    {
        try
        {
            string json = Path.Combine(folder, "map.json");
            return File.Exists(json) ? JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(json)) : null;
        }
        catch
        {
            return null;
        }
    }

    void OnDestroy()
    {
        IsOpen = false;
    }

    // ---- Layout ----

    void Build(Transform root)
    {
        Image card = CourseUI.CardPanel("Card", root);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, info.hasHeights ? 1052 : 988));
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));

        CourseUI.Size(CourseUI.Text("Title", card.transform, "Edit map", 36, CourseUI.Ink, FontStyles.Bold), 44);

        nameField = LabelledInput(card.transform, "Name:", "The map's name in the game", 32, info.name);
        clubField = LabelledInput(card.transform, "Club:", "Club that owns the map", 60, info.ownerClub);

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

        // A public map is sent in: the game's owner needs to know who to ask if the club gets in touch
        contactRow = CourseUI.Rect("Contact", card.transform);
        CourseUI.Row(contactRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(contactRow, 52);
        senderField = CourseUI.Input("Your name", contactRow, "Your full name", 40, _ => Refresh());
        contactField = CourseUI.Input("Contact", contactRow, "E-mail or phone", 60, _ => Refresh());
        CourseUI.Size(senderField, -1, 360, 0);
        CourseUI.Size(contactField, -1, 400, 1);
        senderField.SetTextWithoutNotify(!string.IsNullOrEmpty(info.sender) ? info.sender : PlayerPrefs.GetString("MapImport.Sender", ""));
        contactField.SetTextWithoutNotify(!string.IsNullOrEmpty(info.contact) ? info.contact : PlayerPrefs.GetString("MapImport.Contact", ""));

        // Forest or sprint map
        RectTransform typeRow = CourseUI.Rect("Type row", card.transform);
        CourseUI.Row(typeRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(typeRow, 56);
        forestButton = CourseUI.Button("Forest", typeRow, "Forest", CourseUI.ButtonKind.Primary, () => { mapType = "forest"; Refresh(); }, 20);
        CourseUI.Size(forestButton, -1, 150);
        sprintButton = CourseUI.Button("Sprint", typeRow, "Sprint", CourseUI.ButtonKind.Light, () => { mapType = "sprint"; Refresh(); }, 20);
        CourseUI.Size(sprintButton, -1, 150);
        typeText = CourseUI.Text("Type", typeRow, "", 17, CourseUI.InkMuted);
        typeText.alignment = TextAlignmentOptions.MidlineLeft;
        typeText.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(typeText, -1, -1, 1);

        // A new version of the map file, after fixing something in OCAD: the map is updated, not imported as a new one
        RectTransform fileRow = CourseUI.Rect("File row", card.transform);
        CourseUI.Row(fileRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(fileRow, 52);
        fileText = CourseUI.Text("File", fileRow, "", 20, CourseUI.Ink);
        fileText.alignment = TextAlignmentOptions.MidlineLeft;
        fileText.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(fileText, -1, -1, 1);
        fileButton = CourseUI.Button("File button", fileRow, "Update map file...", CourseUI.ButtonKind.Secondary, ReplaceFile, 20);
        CourseUI.Size(fileButton, -1, 300);
        HoverTip.Add(fileButton, "Pick a new version of the map file (for example after fixing something in OCAD). The map is built again from it, and every course on it stays.");

        RectTransform edgeRow = CourseUI.Rect("Edge row", card.transform);
        CourseUI.Row(edgeRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(edgeRow, 52);
        edgeText = CourseUI.Text("Edge", edgeRow, "", 20, CourseUI.Ink);
        edgeText.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(edgeText, -1, -1, 1);
        edgeButton = CourseUI.Button("Edge button", edgeRow, "Change map edge...", CourseUI.ButtonKind.Secondary, OpenEdge, 20);
        CourseUI.Size(edgeButton, -1, 300);

        RectTransform terrainRow = CourseUI.Rect("Terrain row", card.transform);
        CourseUI.Row(terrainRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(terrainRow, 52);
        terrainText = CourseUI.Text("Terrain", terrainRow, "", 20, CourseUI.Ink);
        terrainText.alignment = TextAlignmentOptions.MidlineLeft;
        terrainText.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(terrainText, -1, -1, 1);
        terrainButton = CourseUI.Button("Terrain button", terrainRow, "Terrain speeds...", CourseUI.ButtonKind.Secondary, OpenTerrain, 20);
        CourseUI.Size(terrainButton, -1, 300);

        // What the terrain looks like in the 3D view of a TV control (ground, trees, undergrowth, own words)
        RectTransform lookRow = CourseUI.Rect("Look row", card.transform);
        CourseUI.Row(lookRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(lookRow, 52);
        lookText = CourseUI.Text("Look", lookRow, "", 20, CourseUI.Ink);
        lookText.alignment = TextAlignmentOptions.MidlineLeft;
        lookText.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(lookText, -1, -1, 1);
        lookButton = CourseUI.Button("Look button", lookRow, "Terrain look...", CourseUI.ButtonKind.Secondary, OpenLook, 20);
        CourseUI.Size(lookButton, -1, 300);

        // The contours the game is unsure of: higher or lower than the one next to them (only maps with heights)
        if (info.hasHeights)
        {
            RectTransform heightsRow = CourseUI.Rect("Heights row", card.transform);
            CourseUI.Row(heightsRow.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
            CourseUI.Size(heightsRow, 52);
            heightsText = CourseUI.Text("Heights", heightsRow, "", 20, CourseUI.Ink);
            heightsText.alignment = TextAlignmentOptions.MidlineLeft;
            heightsText.textWrappingMode = TextWrappingModes.Normal;
            CourseUI.Size(heightsText, -1, -1, 1);
            heightsButton = CourseUI.Button("Heights button", heightsRow, "Uncertain contours...", CourseUI.ButtonKind.Secondary, OpenHeights, 20);
            CourseUI.Size(heightsButton, -1, 300);
            HoverTip.Add(heightsButton, "Go through the contours whose height the game is unsure of, one at a time: is it higher or lower than the contour next to it? This makes the 3D ground right.");
        }

        statusText = CourseUI.Text("Status", card.transform, "", 20, CourseUI.Forest, FontStyles.Bold);
        statusText.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(statusText, 60);

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        deleteButton = CourseUI.Button("Delete", buttons, "Delete map", CourseUI.ButtonKind.Danger, Delete, 24);
        cancelButton = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Close, 24);
        saveButton = CourseUI.Button("Save", buttons, "Save", CourseUI.ButtonKind.Primary, Save, 24);

        HoverTip.Add(nameField, "The name the map has in the game's map list.");
        HoverTip.Add(clubField, "The club that owns the map, if you know it.");
        HoverTip.Add(publicButton, "Everyone who plays the game can use this map, and it is shared through the cloud.");
        HoverTip.Add(privateButton, "Only you can use this map. It is not shared.");
        HoverTip.Add(senderField, "Your name. Only the game's owner sees it, so the owner knows who to ask about the map.");
        HoverTip.Add(contactField, "E-mail or phone number where the game's owner can reach you about this map. It is not shown to other players.");
        HoverTip.Add(forestButton, "A map for orienteering in the forest.");
        HoverTip.Add(sprintButton, "A town or park map with streets, buildings and fences. Sprint maps use their own course rules and game modes.");
        HoverTip.Add(edgeButton, "Draw where the finished map ends. Parts outside the edge are left out. Move the corners, draw a new edge, or use the whole map.");
        HoverTip.Add(terrainButton, "Set how quick each kind of ground is on this map: asphalt, roads, open land, forest, marsh and so on.");
        HoverTip.Add(lookButton, "Set what the terrain looks like in the 3D view at a TV control. Only changes how it looks, not the running.");
        HoverTip.Add(deleteButton, "Removes the map from the game for good.");
        HoverTip.Add(cancelButton, "Closes this window without saving.");
        HoverTip.Add(saveButton, "Saves your changes to the map.");

        busy = BusyOverlay.Add(card.transform);
        Refresh();
    }

    // ---- How many contours still need an answer, shown in the Heights row (2026-11-27, Anders: make it clear,
    // in the same colour as "the map needs saving") ----

    int questionsLeft = -1;            // -1: not counted yet (or could not be)
    bool counting;
    Task<Func<double[], List<RunContourField.HeightQuestion>>> finder;

    Task<Func<double[], List<RunContourField.HeightQuestion>>> Finder()
    {
        if (finder == null || finder.IsFaulted)
        {
            string folder = map.folder;
            float interval = info.contourInterval;
            finder = Task.Run(() => QuestionFinder(folder, interval));
        }
        return finder;
    }

    async void CountQuestions()
    {
        if (counting) return;
        counting = true;
        questionsLeft = -1;
        Refresh();
        try
        {
            Func<double[], List<RunContourField.HeightQuestion>> find = await Finder();
            double[] now = heightAnswers;
            List<RunContourField.HeightQuestion> found = await Task.Run(() => find(now));
            if (this == null) return;
            questionsLeft = found != null ? found.Count : -1;
        }
        catch (Exception e)
        {
            Debug.LogWarning("MapEditPanel: counting uncertain contours: " + e.Message);
            if (this == null) return;
            questionsLeft = -1;
        }
        counting = false;
        Refresh();
    }

    static TMP_InputField LabelledInput(Transform parent, string label, string placeholder, int limit, string value)
    {
        RectTransform row = CourseUI.Rect(label, parent);
        CourseUI.Row(row.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(row, 52);
        TextMeshProUGUI text = CourseUI.Text("Label", row, label, 20, CourseUI.Ink, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.MidlineRight;
        CourseUI.Size(text, -1, 90);
        TMP_InputField field = CourseUI.Input("Field", row, placeholder, limit, null);
        CourseUI.Size(field, -1, -1, 1);
        field.SetTextWithoutNotify(value ?? "");
        return field;
    }

    void Refresh()
    {
        CourseUI.SetButtonColor(publicButton, isPrivate ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Primary);
        CourseUI.SetButtonColor(privateButton, isPrivate ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        string change = isPrivate == info.isPrivate ? "" : isPrivate
            ? " Saving takes it out of the cloud, and the other players' copies are deleted."
            : " Saving sends it in and shares it with every player.";
        visibilityText.text = (isPrivate
            ? "Private: only you can use the map. Others can only race on it in a lobby you host."
            : "Public: the map is sent in and every player can use it.") + change;
        if (contactRow.gameObject.activeSelf == isPrivate) contactRow.gameObject.SetActive(!isPrivate);
        edgeText.text = edge == null ? (edgeChanged ? "Map edge: removed. The map is built again when you save." : "Map edge: the whole map file is used.")
            : edgeChanged ? "Map edge: changed. The map is built again when you save."
            : "Map edge: only the part inside the edge is used.";
        bool sprint = mapType == "sprint" || mapType == "both";
        CourseUI.SetButtonColor(forestButton, sprint ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Primary);
        CourseUI.SetButtonColor(sprintButton, sprint ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        typeText.text = (sprint ? "Sprint map: 2 m cells, sprint courses." : "Forest map.")
                        + (TypeChanged ? " The map is built again when you save."
                           : OldRules ? " Built with older rules for walls, fences, hedges, stairs, heights and gardens: Save builds it again with the new ones." : "");
        typeText.color = TypeChanged || OldRules ? CourseUI.Accent : CourseUI.InkMuted;
        bool confirming = Time.unscaledTime < deleteConfirmUntil;
        CourseUI.SetButtonText(deleteButton, confirming ? "Sure? Click again" : "Delete map");
        terrainText.text = TerrainSpeedPanel.Describe(terrain) + (terrainChanged ? " The map is built again when you save." : "");
        terrainText.color = terrainChanged ? CourseUI.Accent : CourseUI.Ink;
        fileText.text = FileChanged ? "Map file: " + newSourceName + ". The map is built again when you save."
            : "Map file: " + (string.IsNullOrEmpty(info.sourceFile) ? "the one it was imported from" : info.sourceFile);
        fileText.color = FileChanged ? CourseUI.Accent : CourseUI.Ink;
        lookText.text = TerrainLookPanel.Describe(look);
        lookText.color = look.SameAs(info.look) ? CourseUI.Ink : CourseUI.Accent;
        if (heightsText != null)
        {
            int count = HeightAnswers.Count(heightAnswers);
            string given = count == 0 ? "" : count == 1 ? " (1 answered)" : " (" + count + " answered)";
            if (counting) heightsText.text = "Heights: checking the contours ...";
            else if (questionsLeft > 0)
                heightsText.text = "Heights: " + (questionsLeft == 1 ? "1 contour needs" : questionsLeft + " contours need") + " an answer: higher or lower?" + given;
            else if (questionsLeft == 0) heightsText.text = "Heights: every contour is settled." + given;
            else heightsText.text = "Heights: " + (count == 0 ? "no answers about uncertain contours yet." : count == 1 ? "1 answer about an uncertain contour." : count + " answers about uncertain contours.");
            if (HeightsChanged) heightsText.text += " The 3D ground is made again when you save.";
            heightsText.color = HeightsChanged || questionsLeft > 0 ? CourseUI.Accent : CourseUI.Ink;
        }
        foreach (Selectable s in new Selectable[] { publicButton, privateButton, forestButton, sprintButton, deleteButton, edgeButton, terrainButton, lookButton, fileButton, heightsButton, cancelButton, nameField, clubField, senderField, contactField })
            if (s != null) s.interactable = !working;
        saveButton.interactable = !working && (isPrivate || ContactGiven());
    }

    bool HeightsChanged => !SameEdge(heightAnswers, HeightAnswers.Clean(info.heightAnswers));

    bool TypeChanged => mapType != (string.IsNullOrEmpty(info.mapType) ? "forest" : info.mapType);

    // The map was built before the latest passability rules (SymbolRules.RulesVersion): saving builds it again
    bool OldRules => info != null && info.rulesVersion < MapImport.SymbolRules.RulesVersion;

    bool ContactGiven()
    {
        return senderField.text.Trim().Length >= 2 && clubField.text.Trim().Length >= 2 && contactField.text.Trim().Length >= 5;
    }

    void SetStatus(string message, bool error)
    {
        statusText.text = message;
        statusText.color = error ? CourseUI.Danger : CourseUI.Forest;
    }

    BusyOverlay busy;   // over the card while the map is worked on, so it never looks frozen (2026-11-27, Anders)

    void Update()
    {
        if (working && progressStep != null) SetStatus($"{progressStep} ... {Mathf.RoundToInt(progressShare * 100)} %", false);
        if (busy != null)
        {
            if (working) busy.Show(statusText.color == CourseUI.Danger ? "" : statusText.text);
            else busy.Hide();
        }
        // The "Sure? Click again" on the delete button goes back after a while
        if (deleteConfirmUntil > 0f && Time.unscaledTime >= deleteConfirmUntil)
        {
            deleteConfirmUntil = -1f;
            Refresh();
        }
    }

    // ---- Deleting ----

    async void Delete()
    {
        if (working) return;
        // Deleting for everyone cannot be undone, so the button asks once more
        if (Time.unscaledTime >= deleteConfirmUntil)
        {
            deleteConfirmUntil = Time.unscaledTime + 5f;
            SetStatus("Deleting removes the map from this computer and from the cloud, so no player can use it any more. Click again to delete.", true);
            Refresh();
            return;
        }
        deleteConfirmUntil = -1f;
        working = true;
        Refresh();
        SetStatus("Deleting the map ...", false);
        string cloudNote = "";
        try
        {
            SetStatus("Taking the map out of the cloud ...", false);
            try
            {
                await CloudCourses.SignInAsync();
                if (!info.isPrivate) await CloudMaps.WithdrawAsync(info.id);
                await CloudMaps.DeleteFilesAsync("private-" + info.id + "-");
            }
            catch (Exception e)
            {
                Debug.LogWarning("MapEditPanel: could not take the map out of the cloud: " + e);
                cloudNote = " It could not be taken out of the cloud (" + CloudCourses.Describe(e) + "), so other players may still have it.";
            }
            string folder = map.folder;
            await Task.Run(() => { if (Directory.Exists(folder)) Directory.Delete(folder, true); });
            MapGrid.Forget(map.gridName);
            MapPlayArea.Forget(map.id);
            MapPackages.ForgetCached(folder);
            PrivateMaps.MapChanged();
            MapDefinition.Refresh();
            UIToast.Show("Deleted " + info.name + "." + cloudNote, 7f);
            working = false;
            onSaved?.Invoke(null, true);
            Destroy(gameObject);
        }
        catch (Exception e)
        {
            Debug.LogError("MapEditPanel: " + e);
            if (this == null) return;
            working = false;
            SetStatus("Could not delete the map: " + CloudCourses.Describe(e), true);
            Refresh();
        }
    }

    void Close()
    {
        if (working) return;
        Destroy(gameObject);
    }

    // ---- The map file ----

    byte[] sourceBytes;

    // The map file kept with the map, or (for a public map imported before it was kept) the copy in the cloud
    async Task<byte[]> SourceAsync()
    {
        if (sourceBytes != null) return sourceBytes;
        string path = PrivateMaps.SourceFile(map.folder, info);
        if (File.Exists(path))
        {
            sourceBytes = await Task.Run(() => File.ReadAllBytes(path));
            return sourceBytes;
        }
        if (info.isPrivate) throw new Exception("The map file is not kept with this map. Import the map again to change it.");
        SetStatus("Getting the map file from the cloud ...", false);
        byte[] bytes = await CloudMaps.LoadOwnSourceAsync(info.id, Path.GetExtension(path));
        await Task.Run(() => File.WriteAllBytes(path, bytes));
        sourceBytes = bytes;
        return bytes;
    }

    MapFile mapFile;
    MapPreview preview;
    double[] guessed;

    // Picks the new version of the map file and checks that the game can read it; it is used when the map is saved
    async void ReplaceFile()
    {
        if (working) return;
        string path = NativeFiles.OpenFile("Choose the new version of the map file", "Map files (OCAD, OOM)", MapImportPanel.MapExtensions, MapImportPanel.LastFolder());
        if (string.IsNullOrEmpty(path)) return;
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (Array.IndexOf(MapImportPanel.MapExtensions, ext) < 0)
        {
            SetStatus("That is not a map file. The game reads OCAD (.ocd) and OpenOrienteering Mapper (.omap, .xmap) files.", true);
            return;
        }
        working = true;
        Refresh();
        SetStatus("Reading " + Path.GetFileName(path) + " ...", false);
        try
        {
            (byte[], MapFile, MapPreview, double[]) read = await Task.Run(() =>
            {
                byte[] bytes = File.ReadAllBytes(path);
                MapFile m = MapPackageBuilder.ReadMapFile(bytes);
                MapPreview p = null;
                double[] g = null;
                try { p = MapBoundary.Preview(m); g = MapBoundary.Guess(m, p); } catch (Exception e) { Debug.LogWarning("MapEditPanel: no preview of the new file: " + e.Message); }
                return (bytes, m, p, g);
            });
            if (this == null) return;
            sourceBytes = read.Item1;
            mapFile = read.Item2;
            preview = read.Item3;
            guessed = read.Item4;
            newSourceName = Path.GetFileName(path);
            PlayerPrefs.SetString("MapImport.Folder", Path.GetDirectoryName(path));
            SetStatus("The new map file is read. Check the map edge if the map has grown, then save.", false);
        }
        catch (Exception e)
        {
            if (this == null) return;
            SetStatus("Could not read that map file: " + e.Message, true);
        }
        finally
        {
            if (this != null)
            {
                working = false;
                Refresh();
            }
        }
    }

    async void OpenEdge()
    {
        if (working) return;
        working = true;
        Refresh();
        SetStatus("Opening the map edge ...", false);
        try
        {
            if (preview == null)
            {
                byte[] bytes = await SourceAsync();
                SetStatus("Reading the map file ...", false);
                (MapFile, MapPreview, double[]) read = await Task.Run(() =>
                {
                    MapFile m = MapPackageBuilder.ReadMapFile(bytes);
                    MapPreview p = MapBoundary.Preview(m);
                    double[] g = null;
                    try { g = MapBoundary.Guess(m, p); } catch (Exception e) { Debug.LogWarning("MapEditPanel: no map edge guess: " + e.Message); }
                    return (m, p, g);
                });
                if (this == null) return;
                mapFile = read.Item1;
                preview = read.Item2;
                guessed = read.Item3;
            }
            SetStatus("", false);
            // Only sprint maps get the game's guess: on forest maps it takes white forest for unfinished mapping
            MapEdgeEditor.Open(transform, preview, edge, mapType != "forest" ? guessed : null, chosen =>
            {
                if (!SameEdge(chosen, edge))
                {
                    edge = chosen;
                    edgeChanged = true;
                }
                Refresh();
            });
        }
        catch (Exception e)
        {
            if (this == null) return;
            SetStatus("Could not open the map edge: " + CloudCourses.Describe(e), true);
        }
        finally
        {
            if (this != null)
            {
                working = false;
                Refresh();
            }
        }
    }

    void OpenTerrain()
    {
        if (working) return;
        TerrainSpeedPanel.Open(transform, terrain, chosen =>
        {
            terrain = chosen;
            terrainChanged = !chosen.SameAs(info.terrain);
            Refresh();
        });
    }

    void OpenLook()
    {
        if (working) return;
        TerrainLookPanel.Open(transform, look, chosen => { look = chosen; Refresh(); });
    }

    // The contours the game is unsure of, worked out from the map's files with the answers given so far, then asked
    // about one at a time (HeightQuestionPanel)
    async void OpenHeights()
    {
        if (working || HeightQuestionPanel.IsOpen) return;
        working = true;
        Refresh();
        SetStatus("Looking for contours the game is unsure of ...", false);
        string folder = map.folder;
        float interval = info.contourInterval;
        try
        {
            Func<double[], List<RunContourField.HeightQuestion>> find = await Finder();
            double[] now = heightAnswers;
            List<RunContourField.HeightQuestion> questions = await Task.Run(() => find(now));
            if (this == null) return;
            working = false;
            Refresh();
            if (questions == null)
            {
                SetStatus("The contours of this map could not be read.", true);
                return;
            }
            questionsLeft = questions.Count;
            Refresh();
            if (questions.Count == 0)
            {
                SetStatus("The game is sure of every contour on this map. Nothing to answer.", false);
                return;
            }
            SetStatus("", false);
            string pendingFolder = folder;
            HeightQuestionPanel.Open(transform, map, questions, heightAnswers, find, chosen =>
            {
                heightAnswers = HeightAnswers.Clean(chosen);
                Refresh();
                if (HeightsChanged) SaveHeightsNow();
                CountQuestions();
            }, chosen => WritePending(pendingFolder, chosen));
        }
        catch (Exception e)
        {
            Debug.LogWarning("MapEditPanel: uncertain contours: " + e);
            if (this == null) return;
            working = false;
            Refresh();
            SetStatus("Could not look at the contours: " + e.Message, true);
        }
    }

    // ---- Answers kept at once (2026-11-27, Anders: the game crashed before "Stop for now", and the answers were lost) ----
    // Every answer is written to heightanswers.pending in the map's folder; when the questions close (or Edit map opens
    // and finds the file) the answers go into the map straight away, without pressing Save. A public map's new answers
    // are sent to the other players with the next Save.

    const string PendingFile = "heightanswers.pending";

    static void WritePending(string folder, double[] answers)
    {
        try
        {
            double[] clean = HeightAnswers.Clean(answers);
            var parts = new List<string>();
            if (clean != null) foreach (double v in clean) parts.Add(v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllText(Path.Combine(folder, PendingFile), string.Join(",", parts));
        }
        catch (Exception e) { Debug.LogWarning("MapEditPanel: could not keep the answers: " + e.Message); }
    }

    static double[] ReadPending(string folder)
    {
        try
        {
            string file = Path.Combine(folder, PendingFile);
            if (!File.Exists(file)) return null;
            var values = new List<double>();
            foreach (string part in File.ReadAllText(file).Split(','))
                if (double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) values.Add(v);
            return values.Count == 0 ? new double[0] : HeightAnswers.Clean(values.ToArray()) ?? new double[0];
        }
        catch (Exception) { return null; }
    }

    static void DeletePending(string folder)
    {
        try { File.Delete(Path.Combine(folder, PendingFile)); } catch (Exception) { }
    }

    bool heightsToShare;   // answers saved here that a public map has not sent to the other players yet

    // Puts the answers into the map now: points.json and the 3D ground are made again (no Save needed)
    async void SaveHeightsNow()
    {
        if (working) return;
        working = true;
        Refresh();
        SetStatus("Putting your answers into the map and making the 3D ground again ...", false);
        string folder = map.folder;
        MapPackageInfo current = info;
        double[] answers = HeightAnswers.Clean(heightAnswers);
        try
        {
            await Task.Run(() =>
            {
                MapPackageBuilder.ApplyHeightAnswers(folder, current, answers, (step, share) => { progressShare = share; progressStep = step; });
                MapPackageBuilder.WriteInfo(folder, current);
            });
            progressStep = null;
            DeletePending(folder);
            TvWorldData.Forget();
            RunContours.Forget();
            heightsToShare = !info.isPrivate;
            if (this == null) return;
            working = false;
            Refresh();
            int count = HeightAnswers.Count(answers);
            SetStatus((count == 1 ? "1 answer" : count + " answers") + " about the contours saved in the map."
                      + (info.isPrivate ? "" : " Press Save to send them to the other players too."), false);
        }
        catch (Exception e)
        {
            Debug.LogWarning("MapEditPanel: could not save the answers: " + e);
            progressStep = null;
            if (this == null) return;
            working = false;
            Refresh();
            SetStatus("Could not save the answers now (" + e.Message + "). Press Save to try again.", true);
        }
    }

    // Reads the map's files once; the function gives the questions left with some answers (slow: background thread)
    static Func<double[], List<RunContourField.HeightQuestion>> QuestionFinder(string folder, float interval)
    {
        byte[] Read(string name)
        {
            string file = Path.Combine(folder, name);
            return File.Exists(file) ? MapPackageBuilder.Plain(File.ReadAllBytes(file)) : null;
        }
        byte[] grid = Read("grid.bytes"), lines = Read("lines.json"), points = Read("points.json"), world = Read("world.bytes");
        if (grid == null || lines == null) return _ => null;
        TvHeights heights = TvHeights.Read(grid);
        Func<double, double, bool> lakeAt = WorldLayer.LakeTest(world != null ? WorldLayer.FromBytes(world) : null);
        return answers => RunContourField.Questions(heights, lines, points, interval, lakeAt, HeightAnswers.AsList(answers));
    }

    static bool SameEdge(double[] a, double[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 0.001) return false;
        return true;
    }

    // ---- Saving ----

    async void Save()
    {
        if (working) return;
        string name = nameField.text.Trim();
        string club = clubField.text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Give the map a name.", true);
            return;
        }
        if (!isPrivate && !ContactGiven())
        {
            SetStatus("A public map is sent in: write your name, the club that owns the map and your e-mail or phone.", true);
            return;
        }
        string sender = senderField.text.Trim(), contact = contactField.text.Trim();
        bool goPrivate = isPrivate && !info.isPrivate;
        bool goPublic = !isPrivate && info.isPrivate;
        bool rebuild = edgeChanged || terrainChanged || TypeChanged || FileChanged || OldRules;
        bool lookChanged = !look.SameAs(info.look);
        bool heightsChanged = HeightsChanged;
        bool changed = name != info.name || club != (info.ownerClub ?? "") || rebuild || lookChanged || heightsChanged || heightsToShare
                       || (!isPrivate && (sender != (info.sender ?? "") || contact != (info.contact ?? "")));
        if (!changed && !goPrivate && !goPublic)
        {
            Close();
            return;
        }
        working = true;
        Refresh();
        SetStatus("Saving the map ...", false);
        string folder = map.folder;
        MapPackageInfo updated = info;
        string cloudNote = "";
        try
        {
            byte[] ownPng = info.ownPicture ? await Task.Run(() => File.ReadAllBytes(Path.Combine(folder, "picture.png"))) : null;

            // A new map edge or new terrain speeds: build the map again next to the old one, then swap them
            if (rebuild)
            {
                byte[] source = await SourceAsync();
                MapPackageInfo old = info;
                double[] newEdge = edge;
                TerrainSpeeds newTerrain = terrain.Copy();
                TerrainLook newLook = look.Copy();
                double[] newAnswers = heightAnswers;
                string newType = mapType;
                string building = folder + ".rebuild";
                string sourceCopy = PrivateMaps.SourceFile(folder, info);
                string newName = newSourceName;
                updated = await Task.Run(() =>
                {
                    MapFile file = MapPackageBuilder.ReadMapFile(source);
                    if (old.contourInterval > 0) file.contourInterval = old.contourInterval;
                    file.boundary = newEdge;
                    file.terrain = newTerrain;
                    file.look = newLook;
                    file.heightAnswers = newAnswers;
                    MapPackage package = MapImportPanel.BuildPackage(file, name, newType, ownPng,
                        (step, share) => { progressShare = share; progressStep = step; });
                    MapPackageInfo i = package.info;
                    i.id = old.id;
                    i.sourceFile = newName ?? old.sourceFile;
                    i.author = old.author;
                    i.created = old.created;
                    i.permissionConfirmed = old.permissionConfirmed;
                    i.isPrivate = old.isPrivate;
                    // A new map file is sent to the cloud again the next time a lobby races on the private map
                    i.privateSourceKey = newName != null ? "" : old.privateSourceKey;
                    i.sharedAt = old.sharedAt;
                    i.sender = old.sender;
                    i.ownerClub = old.ownerClub;
                    i.contact = old.contact;
                    if (Directory.Exists(building)) Directory.Delete(building, true);
                    MapPackageBuilder.WriteFolder(building, package, ownPng, (step, share) => { progressShare = share; progressStep = step; });
                    // The map file kept with the map: the new version when one was picked
                    if (newName != null) File.WriteAllBytes(PrivateMaps.SourceFile(building, i), source);
                    else File.Copy(sourceCopy, PrivateMaps.SourceFile(building, i), true);
                    return i;
                });
                progressStep = null;
                if (ownPng != null)
                {
                    try { MapPackages.WriteThumbnail(building, ownPng); }
                    catch (Exception e) { Debug.LogWarning("MapEditPanel: no thumbnail: " + e.Message); }
                }
                Directory.Delete(folder, true);
                Directory.Move(building, folder);
                MapGrid.Forget(map.gridName);
                TvWorldData.Forget();
                RunContours.Forget();
                MapPlayArea.Forget(map.id);
                MapPackages.ForgetCached(folder);
            }
            // New answers about uncertain contours only: points.json and the 3D ground are made again, not the whole map
            else if (heightsChanged)
            {
                MapPackageInfo current = info;
                double[] newAnswers = heightAnswers;
                await Task.Run(() => MapPackageBuilder.ApplyHeightAnswers(folder, current, newAnswers, (step, share) => { progressShare = share; progressStep = step; }));
                progressStep = null;
                TvWorldData.Forget();
                RunContours.Forget();
            }
            updated.heightAnswers = HeightAnswers.Clean(heightAnswers);
            updated.name = name;
            updated.ownerClub = club;
            // A new version: shared and used instead of the built-in map until this version is built in
            updated.editedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            updated.look = look.Copy();
            if (!isPrivate)
            {
                updated.sender = sender;
                updated.contact = contact;
                PlayerPrefs.SetString("MapImport.Sender", sender);
                PlayerPrefs.SetString("MapImport.Contact", contact);
            }
            MapPackageBuilder.WriteInfo(folder, updated);   // the local changes are kept even if the cloud fails below

            // The cloud: take it back, send it in, or send the new version
            try
            {
                if (goPrivate)
                {
                    SetStatus("Taking the map out of the cloud ...", false);
                    if (!File.Exists(PrivateMaps.SourceFile(folder, updated))) await SourceAsync();   // kept for lobby races
                    await CloudMaps.WithdrawAsync(updated.id);
                    updated.isPrivate = true;
                    cloudNote = " It is private now.";
                }
                else if (!isPrivate)
                {
                    SetStatus(goPublic ? "Sending the map in ..." : "Sending the new version ...", false);
                    byte[] source = await SourceAsync();
                    updated.permissionConfirmed = true;   // confirmed when it was imported
                    await CloudMaps.SubmitAsync(updated, source, updated.sourceFile, ownPng);
                    updated.isPrivate = false;
                    if (goPublic)
                    {
                        try { await CloudMaps.DeleteFilesAsync("private-" + updated.id + "-"); }
                        catch (Exception e) { Debug.LogWarning("MapEditPanel: could not delete the private files: " + e.Message); }
                        updated.privateSourceKey = "";
                    }
                    cloudNote = goPublic ? " It is public now: every player gets it." : " The other players get the new version.";
                }
                else
                {
                    PrivateMaps.MapChanged();   // the players in a lobby get the new name and edge next time
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("MapEditPanel: " + e);
                cloudNote = " But it could not be " + (goPrivate ? "taken out of" : "sent to") + " the cloud (" + CloudCourses.Describe(e) + "), so it is still "
                            + (info.isPrivate ? "private" : "public") + ". Try again later.";
            }
            MapPackageBuilder.WriteInfo(folder, updated);
            MapDefinition.Refresh();
            MapDefinition saved = MapDefinition.All.Find(m => m.id == updated.id);
            UIToast.Show("Saved " + name + "." + cloudNote, 7f);
            working = false;
            DeletePending(folder);
            onSaved?.Invoke(saved, rebuild);
            Destroy(gameObject);
        }
        catch (Exception e)
        {
            Debug.LogError("MapEditPanel: " + e);
            if (this == null) return;
            working = false;
            progressStep = null;
            SetStatus("Could not save the map: " + CloudCourses.Describe(e), true);
            Refresh();
        }
    }
}
