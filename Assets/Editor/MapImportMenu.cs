using System.Collections.Generic;
using System.IO;
using MapImport;
using UnityEditor;
using UnityEngine;

// Builds maps into the game from map files (OCAD .ocd, OpenOrienteering Mapper .omap/.xmap):
//  - Import map file: reads a file, makes the grid, the map objects and the picture, and puts them in
//    Assets/Resources/MapPackages/<id>/ with a MapDefinition in Resources/MapDefs.
//  - Build imported maps into the game: does the same for the maps imported in the game on this computer
//    (<persistent data>/Maps), for instance maps players have sent in.
// The pictures are imported at full resolution (up to 16384 pixels, the most Unity allows).
public static class MapImportMenu
{
    const string PackagesFolder = "Assets/Resources/MapPackages";
    const string MapDefsFolder = "Assets/Resources/MapDefs";
    const double PixelsPerMm = 24;      // about 600 dpi, sharp even when zoomed far in
    const int MaxPictureSide = 16384;

    [MenuItem("Tools/O-Manager/Import map file (OCAD or OOM)...")]
    public static void ImportFile()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Import map file", "Exit Play mode first.", "OK");
            return;
        }
        string file = EditorUtility.OpenFilePanelWithFilters("Map file", "", new[] { "Map files", "ocd,omap,xmap", "All files", "*" });
        if (string.IsNullOrEmpty(file)) return;
        // A map a player sent in comes as map-<id>-source.<ext> with map-<id>-info.json (and maybe map-<id>-picture.png)
        // next to it: then the name, type and contact come from there and nothing is asked
        MapPackageInfo sent = ReadSentInfo(file);
        if (sent != null)
        {
            string result0 = ImportSent(file);
            EditorUtility.DisplayDialog("Import map", result0.StartsWith("Failed") ? "Could not import the map:\n" + result0.Substring(8) :
                "Imported " + sent.name + " (" + sent.mapType + ")" +
                (string.IsNullOrEmpty(sent.sender) ? "" : "\nSent by " + sent.sender + ", " + sent.ownerClub + ", " + sent.contact) +
                (sent.permissionConfirmed ? "\nThe sender confirmed the club's permission." : "\nThe sender did NOT confirm the club's permission."), "OK");
            return;
        }
        string pictureFile = null;
        if (EditorUtility.DisplayDialog("Map picture", "Should the game draw the map picture itself, or do you have a PNG of the whole map exported from OCAD/OOM?", "Draw it", "Use a PNG"))
        {
            pictureFile = null;
        }
        else
        {
            pictureFile = EditorUtility.OpenFilePanel("PNG of the whole map", Path.GetDirectoryName(file), "png");
            if (string.IsNullOrEmpty(pictureFile)) return;
        }
        string result = Import(file, pictureFile, null, null);
        if (result.StartsWith("Failed")) EditorUtility.DisplayDialog("Import map", "Could not import the map:\n" + result.Substring(8), "OK");
    }

    // Can be called from scripts too: with name and sprint given it asks nothing (the name is the file's when null,
    // and the map type is asked when sprint is null)
    public static string Import(string file, string pictureFile, string name, bool? sprint) => Import(file, pictureFile, name, sprint, null);

    // Imports a map a player sent in (map-<id>-source.<ext> with map-<id>-info.json next to it) without asking anything
    public static string ImportSent(string sourceFile)
    {
        MapPackageInfo sent = ReadSentInfo(sourceFile);
        if (sent == null) return "Failed: there is no readable " + Path.GetFileName(sourceFile).Replace("source", "info").Split('.')[0] + ".json next to the map file.";
        return Import(sourceFile, SentFile(sourceFile, "picture.png"), sent.name, sent.mapType == "sprint" || sent.mapType == "both", sent);
    }

    // The map-<id>-<what> file next to a sent-in map-<id>-source.<ext>, or null
    static string SentFile(string file, string what)
    {
        string fileName = Path.GetFileName(file);
        int at = fileName.LastIndexOf("source", System.StringComparison.OrdinalIgnoreCase);
        if (at <= 0) return null;
        string path = Path.Combine(Path.GetDirectoryName(file), fileName.Substring(0, at) + what);
        return File.Exists(path) ? path : null;
    }

    static MapPackageInfo ReadSentInfo(string file)
    {
        string path = SentFile(file, "info.json");
        if (path == null) return null;
        try
        {
            MapPackageInfo info = JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(path));
            return info == null || string.IsNullOrEmpty(info.name) ? null : info;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Import map: could not read " + path + ": " + e.Message);
            return null;
        }
    }

    static string Import(string file, string pictureFile, string name, bool? sprint, MapPackageInfo sent)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(file);
            EditorUtility.DisplayProgressBar("Import map", "Reading " + Path.GetFileName(file), 0f);
            MapFile mapFile = MapPackageBuilder.ReadMapFile(bytes);
            if (sent != null && sent.contourInterval > 0) mapFile.contourInterval = sent.contourInterval;
            if (sent?.boundary != null && sent.boundary.Length >= 6) mapFile.boundary = sent.boundary;   // the edge the sender chose
            if (sent?.terrain != null) mapFile.terrain = sent.terrain;   // the terrain sliders the sender chose
            if (sent?.look != null) mapFile.look = sent.look;            // and the terrain look
            bool isSprint = sprint ?? SymbolRules.LooksLikeSprint(mapFile);
            if (sprint == null)
            {
                int choice = EditorUtility.DisplayDialogComplex("Map type",
                    Path.GetFileName(file) + " is " + MapPackageBuilder.DescribeVersion(bytes) + " at 1:" + mapFile.scale.ToString("0") +
                    ".\nIs it a forest map (10 m cells) or a sprint map (2 m cells)?",
                    isSprint ? "Sprint" : "Forest", "Cancel", isSprint ? "Forest" : "Sprint");
                if (choice == 1) return "Cancelled";
                if (choice == 2) isSprint = !isSprint;
            }
            name = string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(file) : name;

            byte[] ownPng = string.IsNullOrEmpty(pictureFile) ? null : File.ReadAllBytes(pictureFile);
            MapPackage package = MapPackageBuilder.Build(mapFile, name, isSprint, PixelsPerMm, MaxPictureSide, ownPng == null,
                (step, share) => EditorUtility.DisplayProgressBar("Import map", step, share));
            if (ownPng != null)
            {
                if (!PngFile.TrySize(ownPng, out int w, out int h)) throw new InvalidDataException("The picture is not a PNG.");
                if (!MapPackageBuilder.FitOwnPicture(package, w, h, out string problem)) throw new InvalidDataException(problem);
            }
            package.info.id = MapPackages.Slug(name);   // importing the same map again replaces it
            // A sent-in map keeps the id it had in the game, so courses made on the shared copy still find it
            if (!string.IsNullOrEmpty(sent?.id) && MapPackages.Slug(sent.id) == sent.id) package.info.id = sent.id;
            package.info.sourceFile = Path.GetFileName(file);
            package.info.permissionConfirmed = true;   // imported by the game's owner
            if (sent != null)
            {
                package.info.permissionConfirmed = sent.permissionConfirmed;
                package.info.author = string.IsNullOrEmpty(sent.author) ? package.info.author : sent.author;
                package.info.sender = sent.sender;
                package.info.ownerClub = sent.ownerClub;
                package.info.contact = sent.contact;
                if (!string.IsNullOrEmpty(sent.mapType)) package.info.mapType = sent.mapType;
            }
            string folder = PackagesFolder + "/" + package.info.id;
            MapPackageBuilder.WriteFolder(folder, package, ownPng, (step, share) => EditorUtility.DisplayProgressBar("Import map", step, share));
            if (ownPng != null) MapPackages.WriteThumbnail(folder, ownPng);
            string result = Bake(folder, package.info);
            foreach (string line in package.log) Debug.Log("Import map: " + line);
            Debug.Log(result);
            return result;
        }
        catch (System.Exception e)
        {
            Debug.LogError("Import map: " + e);
            return "Failed: " + e.Message;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Tools/O-Manager/Build imported maps into the game")]
    public static void BuildLocalMaps()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Build imported maps", "Exit Play mode first.", "OK");
            return;
        }
        var done = new List<string>();
        if (Directory.Exists(MapPackages.LocalRoot))
        {
            foreach (string source in Directory.GetDirectories(MapPackages.LocalRoot))
            {
                string json = Path.Combine(source, "map.json");
                if (!File.Exists(json)) continue;
                MapPackageInfo info = JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(json));
                if (info == null || string.IsNullOrEmpty(info.id)) continue;
                if (!info.permissionConfirmed)
                {
                    Debug.LogWarning("Build imported maps: " + info.name + " has no confirmed permission from the map's owner, skipped.");
                    continue;
                }
                if (info.isPrivate)
                {
                    Debug.Log("Build imported maps: " + info.name + " is private (only for the player who imported it), skipped.");
                    continue;
                }
                string folder = PackagesFolder + "/" + info.id;
                Directory.CreateDirectory(folder);
                foreach (string name in new[] { "grid.bytes", "kinds.bytes", "world.bytes", "points.json", "lines.json", "picture.png", "thumb.png", "map.json" })
                {
                    string from = Path.Combine(source, name);
                    if (File.Exists(from)) File.Copy(from, Path.Combine(folder, name), true);
                }
                Debug.Log(Bake(folder, info));
                done.Add(info.name);
            }
        }
        EditorUtility.DisplayDialog("Build imported maps",
            done.Count == 0 ? "There were no imported maps to build in." : "Built into the game:\n" + string.Join("\n", done) +
            "\n\nThe copies on this computer are still there; remove them from " + MapPackages.LocalRoot + " if you like.", "OK");
    }

    // Imports the files in a package folder under Assets/Resources and makes its MapDefinition
    static string Bake(string folder, MapPackageInfo info)
    {
        AssetDatabase.Refresh();
        string picture = folder + "/picture.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(picture);
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = info.pixelsPerMeter / MapDefinition.WorldUnitsPerMeter;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.spritePivot = new Vector2(info.pivotX / info.pictureWidth, info.pivotY / info.pictureHeight);
            importer.maxTextureSize = MaxPictureSide;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
        string thumb = folder + "/thumb.png";
        var thumbImporter = (TextureImporter)AssetImporter.GetAtPath(thumb);
        if (thumbImporter != null)
        {
            thumbImporter.textureType = TextureImporterType.Default;
            thumbImporter.isReadable = true;            // CourseThumbnail draws the course on a copy
            thumbImporter.mipmapEnabled = false;
            thumbImporter.npotScale = TextureImporterNPOTScale.None;
            thumbImporter.textureCompression = TextureImporterCompression.Uncompressed;
            thumbImporter.wrapMode = TextureWrapMode.Clamp;
            thumbImporter.SaveAndReimport();
        }

        Directory.CreateDirectory(MapDefsFolder);
        string assetPath = MapDefsFolder + "/" + info.id + ".asset";
        MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(assetPath);
        bool created = map == null;
        if (created) map = ScriptableObject.CreateInstance<MapDefinition>();
        MapPackages.Fill(map, info, "MapPackages/" + info.id + "/");
        map.name = info.id;   // an asset's name has to match its file
        if (created) AssetDatabase.CreateAsset(map, assetPath);
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
        MapDefinition.Refresh();
        return $"Import map: {info.name} ({info.mapType}, {info.cols} x {info.rows} cells of {info.cellSize} m, picture {info.pictureWidth} x {info.pictureHeight}) -> {folder}, {assetPath}";
    }
}
