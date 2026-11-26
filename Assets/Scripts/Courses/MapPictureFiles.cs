using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

// The built-in map pictures, kept out of Unity's own build (2026-11-26).
//
// The pictures are huge (Oslo 16084 x 16380 pixels). As textures in Resources they were imported and written into every
// build again, came out uncompressed (4.2 GB of the 4.7 GB game), and made Build release take 20-30 minutes, most of it
// Inno Setup squeezing those 4.2 GB. Now:
//   MapPictures/<id>/picture.png                     the picture as made by Import map file (in git, outside Assets,
//                                                    so Unity never imports it)
//   Assets/StreamingAssets/MapPictures/<id>.mappic   the same picture, BC7-compressed with all its mipmaps, ready for
//                                                    the graphics card (not in git; made by MapPictureCache in the
//                                                    editor, only again when the PNG has changed)
// Unity copies StreamingAssets into every build as plain files, so a build no longer touches the pictures at all.
// The game reads the .mappic straight into a texture: no PNG decoding and no compressing while a race loads.
public static class MapPictureFiles
{
    public const string FolderName = "MapPictures";
    public const string Extension = ".mappic";
    const string Prefix = MapPackages.ResourcesFolder + "/";
    const string Suffix = "/picture";

    const int Magic = 0x46504D4F;   // "OMPF"
    public const int Version = 1;

    // The map id for a built-in map's picture path ("MapPackages/<id>/picture"), or null for anything else
    public static string BuiltInId(string spritePath)
    {
        if (string.IsNullOrEmpty(spritePath) || MapPackages.IsFile(spritePath)) return null;
        if (!spritePath.StartsWith(Prefix, StringComparison.Ordinal) || !spritePath.EndsWith(Suffix, StringComparison.Ordinal)) return null;
        string id = spritePath.Substring(Prefix.Length, spritePath.Length - Prefix.Length - Suffix.Length);
        return id.Length == 0 || id.Contains("/") ? null : id;
    }

    // Where the ready picture is: Assets/StreamingAssets/MapPictures in the editor, <game>_Data/StreamingAssets/... in a build
    public static string ReadyFolder => Path.Combine(Application.streamingAssetsPath, FolderName);
    public static string ReadyFile(string id) => Path.Combine(ReadyFolder, id + Extension);

    // What a .mappic says about itself
    public struct Header
    {
        public int width, height;           // the texture (a multiple of 4)
        public int pictureWidth, pictureHeight;   // the map picture inside it, from the bottom-left corner
        public TextureFormat format;
        public int mipCount;
        public long pngLength;              // the PNG it was made from, so the editor can tell when it is out of date
        public string pngHash;
        public long dataLength;
    }

    public static bool TryReadHeader(string file, out Header header)
    {
        header = default;
        try
        {
            if (!File.Exists(file)) return false;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                return ReadHeader(reader, out header);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    static bool ReadHeader(BinaryReader reader, out Header header)
    {
        header = default;
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version) return false;
        header.width = reader.ReadInt32();
        header.height = reader.ReadInt32();
        header.pictureWidth = reader.ReadInt32();
        header.pictureHeight = reader.ReadInt32();
        header.format = (TextureFormat)reader.ReadInt32();
        header.mipCount = reader.ReadInt32();
        header.pngLength = reader.ReadInt64();
        header.pngHash = reader.ReadString();
        header.dataLength = reader.ReadInt64();
        return header.width > 0 && header.height > 0 && header.mipCount > 0 && header.dataLength > 0;
    }

    // Writes a texture (compressed or not, with its mipmaps) and what it was made from
    public static void Write(string file, Texture2D texture, int pictureWidth, int pictureHeight, long pngLength, string pngHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        NativeArray<byte> raw = texture.GetRawTextureData<byte>();
        string temp = file + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(texture.width);
            writer.Write(texture.height);
            writer.Write(pictureWidth);
            writer.Write(pictureHeight);
            writer.Write((int)texture.format);
            writer.Write(texture.mipmapCount);
            writer.Write(pngLength);
            writer.Write(pngHash ?? "");
            writer.Write((long)raw.Length);
            var buffer = new byte[4 << 20];
            for (int at = 0; at < raw.Length; at += buffer.Length)
            {
                int n = Math.Min(buffer.Length, raw.Length - at);
                NativeArray<byte>.Copy(raw, at, buffer, 0, n);
                writer.Write(buffer, 0, n);
            }
        }
        if (File.Exists(file)) File.Delete(file);
        File.Move(temp, file);
    }

    // Reads a .mappic into a texture on the graphics card (no copy kept in normal memory). Null when it can't.
    public static Texture2D Load(string file, out Header header)
    {
        header = default;
        if (!File.Exists(file)) return null;
        Texture2D texture = null;
        try
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var reader = new BinaryReader(stream))
            {
                if (!ReadHeader(reader, out header))
                {
                    Debug.LogWarning("Map picture " + file + " is not a picture this version of the game can read.");
                    return null;
                }
                if (!SystemInfo.SupportsTextureFormat(header.format))
                {
                    Debug.LogWarning("Map picture " + file + ": this graphics card has no " + header.format + "; Unity unpacks it, which is slower.");
                }
                texture = new Texture2D(header.width, header.height, header.format, header.mipCount, false);
                NativeArray<byte> raw = texture.GetRawTextureData<byte>();
                if (raw.Length != header.dataLength)
                {
                    Debug.LogWarning("Map picture " + file + " has " + header.dataLength + " bytes, the texture needs " + raw.Length + ".");
                    Free(texture);
                    return null;
                }
                // Straight from the file into the texture, a few MB at a time, so a 350 MB picture is never a
                // 350 MB array in the game's own memory
                var buffer = new byte[4 << 20];
                for (int at = 0; at < raw.Length;)
                {
                    int n = reader.Read(buffer, 0, Math.Min(buffer.Length, raw.Length - at));
                    if (n <= 0) throw new EndOfStreamException("the file ends early");
                    NativeArray<byte>.Copy(buffer, 0, raw, at, n);
                    at += n;
                }
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            texture.Apply(false, true);
            return texture;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Map picture " + file + " could not be read: " + e.Message);
            if (texture != null) Free(texture);
            return null;
        }
    }

    // Destroy works in a running game, DestroyImmediate when an editor tool loads a map outside Play mode
    public static void Free(UnityEngine.Object thing)
    {
        if (thing == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(thing);
        else UnityEngine.Object.DestroyImmediate(thing);
    }

#if UNITY_EDITOR
    // ---- In the editor only: the PNGs and whether the ready file still matches them ----

    public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    public static string SourceFolder => Path.Combine(ProjectRoot, FolderName);
    public static string SourcePng(string id) => Path.Combine(SourceFolder, id, "picture.png");

    // A ready file is up to date when it was made from a PNG of the same length and contents. The contents are only
    // compared (hashed) when the PNG is newer than the ready file, so most checks just look at two dates.
    public static bool IsUpToDate(string id)
    {
        string png = SourcePng(id), ready = ReadyFile(id);
        if (!File.Exists(png) || !TryReadHeader(ready, out Header header)) return false;
        var pngInfo = new FileInfo(png);
        if (pngInfo.Length != header.pngLength) return false;
        if (pngInfo.LastWriteTimeUtc <= File.GetLastWriteTimeUtc(ready)) return true;
        bool same = HashOf(png) == header.pngHash;
        if (same) File.SetLastWriteTimeUtc(ready, DateTime.UtcNow);   // a checkout touched the PNG; no need to hash it next time
        return same;
    }

    public static string HashOf(string file)
    {
        using (var sha = System.Security.Cryptography.SHA1.Create())
        using (var stream = File.OpenRead(file))
        {
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
#endif
}
