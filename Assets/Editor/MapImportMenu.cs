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
// The pictures are made at full resolution (up to 16384 pixels, the most Unity allows) and moved to
// MapPictures/<id>/picture.png outside Assets, so Unity never imports them (MapPictureFiles, MapPictureCache).
public static class MapImportMenu
{
    const string PackagesFolder = "Assets/Resources/MapPackages";
    const string MapDefsFolder = "Assets/Resources/MapDefs";
    const double PixelsPerMm = 12;      // about 300 dpi like a printed map; 600 dpi used 4x the memory (2026-11-24)
    const int MaxPictureSide = 16384;

    [MenuItem("Tools/O-Manager/Import map file (OCAD or OOM)...", false, 1)]
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
                // The sender's version, so their game knows this version is built in now
                if (sent.editedAt > 0) package.info.editedAt = sent.editedAt;
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

    [MenuItem("Tools/O-Manager/Build imported maps into the game", false, 2)]
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
                foreach (string name in new[] { "grid.bytes", "kinds.bytes", "world.bytes", "runheights.bytes", "points.json", "lines.json", "picture.png", "thumb.png", "map.json" })
                {
                    string from = Path.Combine(source, name);
                    if (File.Exists(from)) File.Copy(from, Path.Combine(folder, name), true);
                }
                EnsureRunHeights(folder, info);
                Debug.Log(Bake(folder, info));
                done.Add(info.name);
            }
        }
        EditorUtility.DisplayDialog("Build imported maps",
            done.Count == 0 ? "There were no imported maps to build in." : "Built into the game:\n" + string.Join("\n", done) +
            "\n\nThe copies on this computer are still there; remove them from " + MapPackages.LocalRoot + " if you like.", "OK");
    }

    // GitHub refuses files over 100 MB (2026-11-25: Oslo's grid.bytes was 123 MB). Binary files over this size are
    // gzipped in place; MapPackages.LoadBytes unpacks them. A picture that big is saved again with 256 colours.
    const long PackAbove = 50L * 1024 * 1024;
    const long GitHubLimit = 100L * 1000 * 1000;

    static string PackBigFiles(string folder)
    {
        var log = new System.Text.StringBuilder();
        foreach (string name in new[] { "grid.bytes", "kinds.bytes", "world.bytes" })
        {
            string file = Path.Combine(folder, name);
            if (!File.Exists(file) || new FileInfo(file).Length <= PackAbove) continue;
            byte[] raw = File.ReadAllBytes(file);
            if (raw.Length >= 2 && raw[0] == 0x1f && raw[1] == 0x8b) continue;   // already packed
            using (var output = new FileStream(file + ".tmp", FileMode.Create))
            using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                gzip.Write(raw, 0, raw.Length);
            File.Copy(file + ".tmp", file, true);
            File.Delete(file + ".tmp");
            log.AppendLine($"Packed {name}: {raw.Length / 1048576} MB -> {new FileInfo(file).Length / 1048576} MB");
        }
        string picture = MapPictureFiles.SourcePng(Path.GetFileName(folder));
        string shrunk = ShrinkPicture(picture);
        if (shrunk != null) log.AppendLine(shrunk);
        foreach (string name in new[] { "lines.json", "points.json" })
        {
            string file = Path.Combine(folder, name);
            if (File.Exists(file) && new FileInfo(file).Length > GitHubLimit)
                Debug.LogWarning($"{file} is {new FileInfo(file).Length / 1000000} MB, over GitHub's 100 MB limit.");
        }
        return log.ToString();
    }

    // A map picture over ShrinkAbove (an RGBA PNG from before 2026-11-26, or one a player gave) saved again with
    // 256 colours (PngPalette): same size in pixels, looks the same, a quarter of the bytes or less.
    // Returns what was done, or null when the picture was already small enough.
    const long ShrinkAbove = 90L * 1000 * 1000;

    static string ShrinkPicture(string png)
    {
        if (!File.Exists(png) || new FileInfo(png).Length <= ShrinkAbove) return null;
        long before = new FileInfo(png).Length;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            EditorUtility.DisplayProgressBar("Map picture", "Saving " + png + " with 256 colours (under GitHub's 100 MB limit)", 0.5f);
            if (!texture.LoadImage(File.ReadAllBytes(png), false))
            {
                Debug.LogWarning(png + " could not be read, so it was not made smaller.");
                return null;
            }
            int w = texture.width, h = texture.height;
            Color32[] pixels = texture.GetPixels32();   // rows from the bottom
            var rgba = new byte[(long)w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int from = (h - 1 - y) * w, to = y * w * 4;
                for (int x = 0; x < w; x++, to += 4)
                {
                    Color32 c = pixels[from + x];
                    rgba[to] = c.r; rgba[to + 1] = c.g; rgba[to + 2] = c.b; rgba[to + 3] = 255;
                }
            }
            pixels = null;
            PngPalette.Write(png + ".tmp", rgba, w, h);
            File.Copy(png + ".tmp", png, true);
            File.Delete(png + ".tmp");
            string done = $"Map picture {png}: {before / 1000000} MB -> {new FileInfo(png).Length / 1000000} MB (256 colours)";
            Debug.Log(done);
            return done;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("Tools/O-Manager/Developer (rarely needed)/Shrink map pictures over 90 MB", false, 124)]
    static void ShrinkAllPictures()
    {
        var done = new List<string>();
        if (Directory.Exists(MapPictureFiles.SourceFolder))
            foreach (string folder in Directory.GetDirectories(MapPictureFiles.SourceFolder))
            {
                string result = ShrinkPicture(Path.Combine(folder, "picture.png"));
                if (result != null) done.Add(result);
            }
        if (done.Count > 0) MapPictureCache.PrepareAll();   // the ready pictures follow the new PNGs
        EditorUtility.DisplayDialog("Shrink map pictures",
            done.Count == 0 ? "No map picture is over 90 MB." : string.Join("\n", done) + "\n\nCommit the new picture.png files in GitHub Desktop.", "OK");
    }

    // The run-yourself 3D ground (runheights.bytes) for a package that has none or an old one (a map imported before
    // 2026-11-25), so no player has to work it out before their first race on the map
    static void EnsureRunHeights(string folder, MapPackageInfo info)
    {
        try
        {
            string file = Path.Combine(folder, "runheights.bytes");
            byte[] lines = ReadPlain(Path.Combine(folder, "lines.json")), points = ReadPlain(Path.Combine(folder, "points.json"));
            byte[] grid = ReadPlain(Path.Combine(folder, "grid.bytes"));
            if (lines == null || grid == null) return;
            if (File.Exists(file) && RunContours.FromPackage(null, MapPackages.Unpack(File.ReadAllBytes(file)), lines, points) != null) return;
            EditorUtility.DisplayProgressBar("Build imported maps", "Shaping the 3D ground of " + info.name + " from the contours", 0.5f);
            byte[] made = RunContours.Bake(TvHeights.Read(grid), lines, points, info.contourInterval > 0.5f ? info.contourInterval : 5f);
            if (made != null) File.WriteAllBytes(file, made);
            Debug.Log("Build imported maps: 3D ground of " + info.name + (made != null ? " made (" + made.Length / 1024 + " kB)" : " could not be made from the contours"));
        }
        catch (System.Exception e) { Debug.LogWarning("Build imported maps: 3D ground of " + info.name + ": " + e.Message); }
        finally { EditorUtility.ClearProgressBar(); }
    }

    static byte[] ReadPlain(string file) => File.Exists(file) ? MapPackages.Unpack(File.ReadAllBytes(file)) : null;

    // The map picture goes to MapPictures/<id>/picture.png, outside Assets: as a Unity texture in Resources it was
    // imported (a minute for a big map), written into every build uncompressed and made Build release slow.
    static string MovePictureOut(string folder, string id)
    {
        string from = Path.Combine(folder, "picture.png");
        string to = MapPictureFiles.SourcePng(id);
        if (!File.Exists(from)) return File.Exists(to) ? to : null;
        Directory.CreateDirectory(Path.GetDirectoryName(to));
        File.Copy(from, to, true);
        File.Delete(from);
        if (File.Exists(from + ".meta")) File.Delete(from + ".meta");
        return to;
    }

    // Imports the files in a package folder under Assets/Resources and makes its MapDefinition
    static string Bake(string folder, MapPackageInfo info)
    {
        if (MovePictureOut(folder, info.id) == null) Debug.LogWarning("Import map: " + info.name + " has no picture.png.");
        string packed = PackBigFiles(folder);
        AssetDatabase.Refresh();
        // The ready (compressed) picture for the game, made now so Play mode and the next build have it
        string problem = MapPictureCache.Make(info.id);
        if (problem != null) Debug.LogWarning("Import map: the ready picture of " + info.name + " could not be made now (" + problem +
                                              "); Build release tries again.");
        AssetDatabase.Refresh();
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
        return packed + $"Import map: {info.name} ({info.mapType}, {info.cols} x {info.rows} cells of {info.cellSize} m, picture {info.pictureWidth} x {info.pictureHeight}) -> {folder}, {assetPath}";
    }
}
