using System;
using System.Collections.Generic;
using System.IO;
using MapImport;
using UnityEngine;

// Maps made from map files (OCAD / OpenOrienteering Mapper), as the game uses them.
//
// A map package is a folder with:
//   map.json     what the map is (MapPackageInfo)
//   grid         the walkability grid (GridBinary version 2)
//   points       stones, knolls, pits ... (GeoJSON)
//   lines        paths, streams, vegetation boundaries ... (GeoJSON)
//   picture      the map picture, covering the grid exactly
//   thumb        a small copy of the picture for menus
//
// Built-in maps live in Resources/MapPackages/<id>/ (made in the editor with Tools > O-Manager > Import map file)
// and have a MapDefinition asset in Resources/MapDefs. Their pictures are not in Resources: see MapPictureFiles.
// Maps a player imports in the game are saved in <persistent data>/Maps/<id>/ and only exist on that computer until they are built into the game.
// Paths that start with "file:" point at those files on disk; anything else is a Resources path.
public static class MapPackages
{
    public const string FilePrefix = "file:";
    public const string ResourcesFolder = "MapPackages";

    // A ParrelSync clone keeps its maps in a folder of its own (2026-11-19): the clone and the main editor share
    // persistentDataPath, so the clone's downloads and clean-ups rebuilt and deleted the main editor's maps under it
    // ("could not read grid file"). With a folder each, a clone behaves like a second computer.
    public static string LocalRoot => Path.Combine(Application.persistentDataPath, LocalPrefs.Key("Maps"));

    // True when the map's grid can be read from disk (or is built into the game)
    public static bool GridExists(string gridName)
    {
        if (string.IsNullOrEmpty(gridName)) return false;
        if (IsFile(gridName)) return File.Exists(gridName.Substring(FilePrefix.Length) + ".bytes");
        return true;
    }

    // ---- Reading files from either place ----

    public static bool IsFile(string path) => path != null && path.StartsWith(FilePrefix, StringComparison.Ordinal);

    // Grids of imported maps are given as a path; the old grids by a name in Resources/2d-gridJson
    public static bool IsPackagePath(string gridName) => gridName != null && (IsFile(gridName) || gridName.Contains("/"));

    // The bytes of a package file, without extension in the path (Resources) or with it (disk)
    public static byte[] LoadBytes(string path, string extension)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (IsFile(path))
        {
            string file = path.Substring(FilePrefix.Length) + extension;
            return File.Exists(file) ? Unpack(File.ReadAllBytes(file)) : null;
        }
        TextAsset asset = Resources.Load<TextAsset>(path);
        if (asset == null) return null;
        byte[] bytes = asset.bytes;
        Resources.UnloadAsset(asset);
        return Unpack(bytes);
    }

    // Built-in package files over GitHub's 100 MB limit are kept gzipped (MapImportMenu, 2026-11-25):
    // a big sprint grid such as Oslo's is 123 MB raw and about 50 MB packed. Plain files pass through.
    public static byte[] Unpack(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 2 || bytes[0] != 0x1f || bytes[1] != 0x8b) return bytes;
        using (var input = new MemoryStream(bytes))
        using (var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
        using (var output = new MemoryStream(bytes.Length * 3))
        {
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }

    static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    // The map picture: a built-in map's ready picture file (MapPictureFiles), an older picture in Resources,
    // or a PNG on disk for a map imported in the game
    public static Sprite LoadSprite(MapBackground background)
    {
        if (background == null || string.IsNullOrEmpty(background.spritePath)) return null;
        string builtInId = MapPictureFiles.BuiltInId(background.spritePath);
        if (builtInId != null) return LoadBuiltInSprite(background, builtInId);
        if (!IsFile(background.spritePath))
        {
            return Resources.Load<Sprite>(background.spritePath);
        }
        if (spriteCache.TryGetValue(background.spritePath, out Sprite cached) && cached != null) return cached;
        Texture2D texture = LoadTexture(background.spritePath.Substring(FilePrefix.Length) + ".png", false);
        if (texture == null) return null;
        Sprite sprite = MakeSprite(texture, texture.width, texture.height, background,
            Path.GetFileName(Path.GetDirectoryName(background.spritePath.Substring(FilePrefix.Length))));
        spriteCache[background.spritePath] = sprite;
        return sprite;
    }

    static Sprite MakeSprite(Texture2D texture, int width, int height, MapBackground background, string name)
    {
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, width, height),
            new Vector2(background.pivotPixels.x / width, background.pivotPixels.y / height),
            background.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    // The built-in pictures loaded lately, newest last. Only the last two are kept: a Resources picture used to be
    // let go when the scene changed, and these are hundreds of MB each on the graphics card.
    static readonly List<string> builtInOrder = new List<string>();
    const int BuiltInKept = 2;

    static Sprite LoadBuiltInSprite(MapBackground background, string id)
    {
        string key = background.spritePath;
        if (spriteCache.TryGetValue(key, out Sprite cached) && cached != null)
        {
            builtInOrder.Remove(key);
            builtInOrder.Add(key);
            return cached;
        }

        Texture2D texture = null;
        int width = 0, height = 0;
#if UNITY_EDITOR
        // In the editor the ready file can be older than the PNG (a map imported again, not yet prepared):
        // then the PNG itself is shown, which takes a little longer to load
        if (MapPictureFiles.IsUpToDate(id))
        {
            texture = MapPictureFiles.Load(MapPictureFiles.ReadyFile(id), out MapPictureFiles.Header header);
            width = header.pictureWidth;
            height = header.pictureHeight;
        }
        if (texture == null)
        {
            texture = LoadTexture(MapPictureFiles.SourcePng(id), false);
            if (texture != null) { width = texture.width; height = texture.height; }
        }
#else
        texture = MapPictureFiles.Load(MapPictureFiles.ReadyFile(id), out MapPictureFiles.Header header);
        width = header.pictureWidth;
        height = header.pictureHeight;
#endif
        // A game built before the pictures moved out of Resources
        if (texture == null) return Resources.Load<Sprite>(background.spritePath);

        Sprite sprite = MakeSprite(texture, width, height, background, id);
        spriteCache[key] = sprite;
        builtInOrder.Remove(key);
        builtInOrder.Add(key);
        while (builtInOrder.Count > BuiltInKept)
        {
            string oldest = builtInOrder[0];
            builtInOrder.RemoveAt(0);
            if (spriteCache.TryGetValue(oldest, out Sprite old) && old != null)
            {
                MapPictureFiles.Free(old.texture);
                MapPictureFiles.Free(old);
            }
            spriteCache.Remove(oldest);
        }
        return sprite;
    }

    public static Texture2D LoadThumbnail(MapBackground background)
    {
        if (background == null || string.IsNullOrEmpty(background.thumbnailPath)) return null;
        if (!IsFile(background.thumbnailPath)) return Resources.Load<Texture2D>(background.thumbnailPath);
        return LoadTexture(background.thumbnailPath.Substring(FilePrefix.Length) + ".png", true);
    }

    static readonly Dictionary<string, Texture2D> thumbCache = new Dictionary<string, Texture2D>();

    // Forgets the pictures loaded from a map folder, after the map has been built again
    public static void ForgetCached(string folder)
    {
        string sprite = FilePrefix + folder.Replace('\\', '/') + "/";
        string file = folder.Replace('\\', '/');
        foreach (string key in new List<string>(spriteCache.Keys))
            if (key.Replace('\\', '/').StartsWith(sprite, StringComparison.Ordinal)) spriteCache.Remove(key);
        foreach (string key in new List<string>(thumbCache.Keys))
            if (key.Replace('\\', '/').StartsWith(file, StringComparison.Ordinal)) thumbCache.Remove(key);
    }

    static Texture2D LoadTexture(string file, bool cache)
    {
        if (cache && thumbCache.TryGetValue(file, out Texture2D cached) && cached != null) return cached;
        if (!File.Exists(file)) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
        if (!texture.LoadImage(File.ReadAllBytes(file), false))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Trilinear;
        texture.anisoLevel = 4;
        if (!cache)
        {
            // Big pictures: compress on the graphics card and drop the copy in normal memory
            if (texture.width % 4 == 0 && texture.height % 4 == 0) texture.Compress(false);
            texture.Apply(false, true);
        }
        else
        {
            thumbCache[file] = texture;
        }
        return texture;
    }

    // ---- Map definitions ----

    // A map definition for a package; root is "file:<folder>/" or "MapPackages/<id>/"
    public static MapDefinition Definition(MapPackageInfo info, string root)
    {
        var map = ScriptableObject.CreateInstance<MapDefinition>();
        Fill(map, info, root);
        return map;
    }

    public static void Fill(MapDefinition map, MapPackageInfo info, string root)
    {
        map.name = info.name;
        map.id = info.id;
        map.displayName = info.name;
        map.mapType = info.mapType;
        map.editedAt = info.editedAt;
        map.gridName = root + "grid";
        map.pointsPath = root + "points";
        map.linesPath = root + "lines";
        map.minx = info.minx;
        map.miny = info.miny;
        map.maxx = info.maxx;
        map.maxy = info.maxy;
        map.gridSize = info.cellSize;
        map.rows = info.rows;
        map.cols = info.cols;
        map.defaultBackground = "map";
        float worldWidth = info.cols * info.cellSize * MapDefinition.WorldUnitsPerMeter;
        bool placed = info.pixelsPerMeter > 0f;
        map.backgrounds = new List<MapBackground>
        {
            new MapBackground
            {
                id = "map",
                spritePath = root + "picture",
                thumbnailPath = root + "thumb",
                position = Vector3.zero,   // the grid is centred on the origin, and so is the picture's pivot
                rotation = 0f,
                scale = 1f,
                courseInPicture = false,
                pixelSize = new Vector2(info.pictureWidth, info.pictureHeight),
                pivotPixels = placed ? new Vector2(info.pivotX, info.pivotY) : new Vector2(info.pictureWidth / 2f, info.pictureHeight / 2f),
                pixelsPerUnit = placed ? info.pixelsPerMeter / MapDefinition.WorldUnitsPerMeter : info.pictureWidth / Mathf.Max(0.001f, worldWidth)
            }
        };
    }

    // Maps imported on this computer
    public static List<MapDefinition> LoadLocal()
    {
        var list = new List<MapDefinition>();
        try
        {
            if (!Directory.Exists(LocalRoot)) return list;
            foreach (string folder in Directory.GetDirectories(LocalRoot))
            {
                string json = Path.Combine(folder, "map.json");
                if (!File.Exists(json)) continue;
                try
                {
                    MapPackageInfo info = JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(json));
                    if (info == null || string.IsNullOrEmpty(info.id)) continue;
                    MapDefinition map = Definition(info, FilePrefix + folder.Replace('\\', '/') + "/");
                    map.isLocal = true;
                    map.isPrivate = info.isPrivate;
                    map.isSharedCopy = !string.IsNullOrEmpty(info.sharedBy);
                    map.folder = folder;
                    list.Add(map);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("MapPackages: could not read " + json + ": " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("MapPackages: could not list " + LocalRoot + ": " + e.Message);
        }
        return list;
    }

    // ---- Saving ----

    // A short id from the name, unique among the maps there already are
    public static string MakeId(string name)
    {
        string id = Slug(name);
        string unique = id;
        for (int n = 2; MapDefinition.All.Exists(m => m.id == unique) || Directory.Exists(Path.Combine(LocalRoot, unique)); n++)
        {
            unique = id + "-" + n;
        }
        return unique;
    }

    // Lower case letters, digits and dashes
    public static string Slug(string name)
    {
        var chars = new System.Text.StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') chars.Append(c);
            else if (c == 'å' || c == 'ä') chars.Append('a');
            else if (c == 'ö') chars.Append('o');
            else if (chars.Length > 0 && chars[chars.Length - 1] != '-') chars.Append('-');
        }
        string id = chars.ToString().Trim('-');
        if (id.Length == 0) id = "map";
        if (id.Length > 40) id = id.Substring(0, 40);
        return id;
    }

    // A thumbnail for a picture the player gave (the drawn pictures get theirs in MapPackageBuilder.WriteFolder)
    public static void WriteThumbnail(string folder, byte[] png)
    {
        var picture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!picture.LoadImage(png, false))
        {
            UnityEngine.Object.DestroyImmediate(picture);
            throw new System.IO.InvalidDataException("The picture could not be read as a PNG.");
        }
        Texture2D thumb = Downscale(picture, MapPackageBuilder.ThumbnailSide);
        File.WriteAllBytes(Path.Combine(folder, "thumb.png"), thumb.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(thumb);
        UnityEngine.Object.DestroyImmediate(picture);
    }

    public static Texture2D Downscale(Texture2D source, int maxSide)
    {
        float f = Mathf.Min(1f, maxSide / (float)Mathf.Max(source.width, source.height));
        int w = Mathf.Max(1, Mathf.RoundToInt(source.width * f)), h = Mathf.Max(1, Mathf.RoundToInt(source.height * f));
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }
}
