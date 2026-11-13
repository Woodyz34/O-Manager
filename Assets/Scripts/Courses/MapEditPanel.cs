using System;
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
    TextMeshProUGUI visibilityText, edgeText, terrainText, lookText, statusText;
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
        panel.Build(dim.transform);
        IsOpen = true;
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
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 924));
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

        statusText = CourseUI.Text("Status", card.transform, "", 20, CourseUI.Forest, FontStyles.Bold);
        statusText.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(statusText, 60);

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        deleteButton = CourseUI.Button("Delete", buttons, "Delete map", CourseUI.ButtonKind.Danger, Delete, 24);
        cancelButton = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Close, 24);
        saveButton = CourseUI.Button("Save", buttons, "Save", CourseUI.ButtonKind.Primary, Save, 24);

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
        edgeText.text = edge == null ? "Map edge: the whole map file is used."
            : edgeChanged ? "Map edge: changed. The map is built again when you save."
            : "Map edge: only the part inside the edge is used.";
        bool sprint = mapType == "sprint" || mapType == "both";
        CourseUI.SetButtonColor(forestButton, sprint ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Primary);
        CourseUI.SetButtonColor(sprintButton, sprint ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        typeText.text = (sprint ? "Sprint map: 2 m cells, sprint courses." : "Forest map.")
                        + (TypeChanged ? " The map is built again when you save." : "");
        typeText.color = TypeChanged ? CourseUI.Accent : CourseUI.InkMuted;
        bool confirming = Time.unscaledTime < deleteConfirmUntil;
        CourseUI.SetButtonText(deleteButton, confirming ? "Sure? Click again" : "Delete map");
        terrainText.text = TerrainSpeedPanel.Describe(terrain) + (terrainChanged ? " The map is built again when you save." : "");
        terrainText.color = terrainChanged ? CourseUI.Accent : CourseUI.Ink;
        lookText.text = TerrainLookPanel.Describe(look);
        lookText.color = look.SameAs(info.look) ? CourseUI.Ink : CourseUI.Accent;
        foreach (Selectable s in new Selectable[] { publicButton, privateButton, forestButton, sprintButton, deleteButton, edgeButton, terrainButton, lookButton, cancelButton, nameField, clubField, senderField, contactField })
            s.interactable = !working;
        saveButton.interactable = !working && (isPrivate || ContactGiven());
    }

    bool TypeChanged => mapType != (string.IsNullOrEmpty(info.mapType) ? "forest" : info.mapType);

    bool ContactGiven()
    {
        return senderField.text.Trim().Length >= 2 && clubField.text.Trim().Length >= 2 && contactField.text.Trim().Length >= 5;
    }

    void SetStatus(string message, bool error)
    {
        statusText.text = message;
        statusText.color = error ? CourseUI.Danger : CourseUI.Forest;
    }

    void Update()
    {
        if (working && progressStep != null) SetStatus($"{progressStep} ... {Mathf.RoundToInt(progressShare * 100)} %", false);
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

    async void OpenEdge()
    {
        if (working) return;
        working = true;
        Refresh();
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
            MapEdgeEditor.Open(transform, preview, edge, guessed, chosen =>
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
        bool rebuild = edgeChanged || terrainChanged || TypeChanged;
        bool lookChanged = !look.SameAs(info.look);
        bool changed = name != info.name || club != (info.ownerClub ?? "") || rebuild || lookChanged
                       || (!isPrivate && (sender != (info.sender ?? "") || contact != (info.contact ?? "")));
        if (!changed && !goPrivate && !goPublic)
        {
            Close();
            return;
        }
        working = true;
        Refresh();
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
                string newType = mapType;
                string building = folder + ".rebuild";
                string sourceCopy = PrivateMaps.SourceFile(folder, info);
                updated = await Task.Run(() =>
                {
                    MapFile file = MapPackageBuilder.ReadMapFile(source);
                    if (old.contourInterval > 0) file.contourInterval = old.contourInterval;
                    file.boundary = newEdge;
                    file.terrain = newTerrain;
                    file.look = newLook;
                    MapPackage package = MapImportPanel.BuildPackage(file, name, newType, ownPng,
                        (step, share) => { progressShare = share; progressStep = step; });
                    MapPackageInfo i = package.info;
                    i.id = old.id;
                    i.sourceFile = old.sourceFile;
                    i.author = old.author;
                    i.created = old.created;
                    i.permissionConfirmed = old.permissionConfirmed;
                    i.isPrivate = old.isPrivate;
                    i.privateSourceKey = old.privateSourceKey;
                    i.sharedAt = old.sharedAt;
                    i.sender = old.sender;
                    i.ownerClub = old.ownerClub;
                    i.contact = old.contact;
                    if (Directory.Exists(building)) Directory.Delete(building, true);
                    MapPackageBuilder.WriteFolder(building, package, ownPng, (step, share) => { progressShare = share; progressStep = step; });
                    File.Copy(sourceCopy, PrivateMaps.SourceFile(building, i), true);
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
                MapPlayArea.Forget(map.id);
                MapPackages.ForgetCached(folder);
            }
            updated.name = name;
            updated.ownerClub = club;
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
