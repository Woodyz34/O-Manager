using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Makes the ready map picture files (Assets/StreamingAssets/MapPictures/<id>.mappic) from MapPictures/<id>/picture.png,
// only for maps whose PNG has changed since the last time. See MapPictureFiles for why.
//
// Build release runs this first. Unchanged maps take a second or two in all; a new or changed map takes about a
// minute for a big one (Oslo) while it is compressed, once. The folder is not in git: on another computer, or after
// deleting it, every map is simply made again the first time.
public static class MapPictureCache
{
    const TextureFormat Format = TextureFormat.BC7;   // 1 byte per pixel; keeps thin lines and small text sharp

    [MenuItem("Tools/O-Manager/Developer (rarely needed)/Prepare map pictures for the build", false, 123)]
    static void PrepareFromMenu()
    {
        Result result = PrepareAll();
        EditorUtility.DisplayDialog("Map pictures", result.Summary(), "OK");
    }

    public class Result
    {
        public readonly List<string> made = new List<string>();
        public readonly List<string> reused = new List<string>();
        public readonly List<string> failed = new List<string>();
        public readonly List<string> removed = new List<string>();

        public string Summary()
        {
            var lines = new List<string>();
            lines.Add(reused.Count + " map picture(s) unchanged, reused as they were.");
            if (made.Count > 0) lines.Add("Made again (new or changed): " + string.Join(", ", made));
            if (removed.Count > 0) lines.Add("Removed (map no longer in the game): " + string.Join(", ", removed));
            if (failed.Count > 0) lines.Add("FAILED: " + string.Join("; ", failed));
            return string.Join("\n", lines);
        }
    }

    // Brings every ready picture up to date with its PNG
    public static Result PrepareAll()
    {
        var result = new Result();
        string source = MapPictureFiles.SourceFolder;
        var ids = new List<string>();
        if (Directory.Exists(source))
        {
            foreach (string folder in Directory.GetDirectories(source))
            {
                if (File.Exists(Path.Combine(folder, "picture.png"))) ids.Add(Path.GetFileName(folder));
            }
        }
        ids.Sort(StringComparer.Ordinal);

        try
        {
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                EditorUtility.DisplayProgressBar("Map pictures", "Checking " + id, i / (float)Math.Max(1, ids.Count));
                if (MapPictureFiles.IsUpToDate(id))
                {
                    result.reused.Add(id);
                    continue;
                }
                EditorUtility.DisplayProgressBar("Map pictures", "Compressing " + id + " (only because it is new or changed; a big map takes a minute)",
                    i / (float)Math.Max(1, ids.Count));
                string problem = Make(id);
                if (problem == null) result.made.Add(id);
                else result.failed.Add(id + ": " + problem);
            }

            // Ready files of maps that are gone would otherwise be shipped for nothing
            string ready = MapPictureFiles.ReadyFolder;
            if (Directory.Exists(ready))
            {
                foreach (string file in Directory.GetFiles(ready, "*" + MapPictureFiles.Extension))
                {
                    string id = Path.GetFileNameWithoutExtension(file);
                    if (ids.Contains(id)) continue;
                    File.Delete(file);
                    if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
                    result.removed.Add(id);
                }
                foreach (string temp in Directory.GetFiles(ready, "*.tmp")) File.Delete(temp);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        if (result.made.Count > 0 || result.removed.Count > 0) AssetDatabase.Refresh();
        Debug.Log("Map pictures: " + result.Summary().Replace("\n", " "));
        return result;
    }

    // Compresses one map's PNG into its ready file. Returns null when it worked, else what went wrong.
    public static string Make(string id)
    {
        string png = MapPictureFiles.SourcePng(id);
        Texture2D texture = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(png);
            string hash = MapPictureFiles.HashOf(png);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!texture.LoadImage(bytes, false)) return "the PNG could not be read";
            bytes = null;
            int pictureWidth = texture.width, pictureHeight = texture.height;

            // Block compression works on 4 x 4 pixel blocks. A picture that is not a multiple of 4 gets white added to
            // the right and top; the sprite still only shows the picture itself. The copy also gets mipmaps, should
            // LoadImage have left them out (without them the map shimmers when zoomed out).
            if (pictureWidth % 4 != 0 || pictureHeight % 4 != 0 || texture.mipmapCount <= 1)
            {
                Texture2D padded = Pad(texture);
                UnityEngine.Object.DestroyImmediate(texture);
                texture = padded;
            }

            EditorUtility.CompressTexture(texture, Format, TextureCompressionQuality.Normal);
            if (texture.format != Format)
            {
                // Should not happen on a PC; DXT1 is half the size again and every graphics card has it
                Debug.LogWarning("Map picture " + id + ": " + Format + " did not work (got " + texture.format + "), trying DXT1.");
                EditorUtility.CompressTexture(texture, TextureFormat.DXT1, TextureCompressionQuality.Normal);
            }
            if (texture.format != Format && texture.format != TextureFormat.DXT1)
            {
                Debug.LogWarning("Map picture " + id + " stays uncompressed (" + texture.format + "). It works, but is 4 times bigger.");
            }

            MapPictureFiles.Write(MapPictureFiles.ReadyFile(id), texture, pictureWidth, pictureHeight, new FileInfo(png).Length, hash);
            Debug.Log("Map picture " + id + ": " + pictureWidth + " x " + pictureHeight + " " + texture.format + ", " +
                      new FileInfo(MapPictureFiles.ReadyFile(id)).Length / 1048576 + " MB");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return e.Message;
        }
        finally
        {
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
        }
    }

    static Texture2D Pad(Texture2D source)
    {
        int w = (source.width + 3) / 4 * 4, h = (source.height + 3) / 4 * 4;
        var padded = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var white = new Color32[w * h];
        for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);
        padded.SetPixels32(white);
        padded.SetPixels32(0, 0, source.width, source.height, source.GetPixels32());
        padded.Apply(true, false);
        return padded;
    }
}
