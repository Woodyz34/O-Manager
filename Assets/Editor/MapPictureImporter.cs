using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// The built-in map pictures (Assets/Resources/MapPackages/<id>/picture.png) are huge: up to 16384 pixels each way.
// With Unity's automatic format they ended up as plain RGB24 with mipmaps, so one map took up to 1 GB of memory
// (Oslo 1 GB, Fossum 0.9 GB, Ängaryd sprint 0.4 GB), the release build was 5 GB and Build release ran the
// editor out of memory. Here every map picture is told outright to use BC7 on PC, Mac and Linux:
// 1 byte per pixel instead of 3 (a third of the memory and download), and BC7 keeps thin lines and
// small text sharp, unlike the older DXT1.
public class MapPictureImporter : AssetPostprocessor
{
    const string Folder = "Assets/Resources/MapPackages/";
    const int MaxSide = 16384;

    // Raised when the settings below change, so Unity imports the map pictures again
    public override uint GetVersion() { return 1; }

    static bool IsMapPicture(string path)
    {
        return path.StartsWith(Folder) && path.EndsWith("/picture.png");
    }

    void OnPreprocessTexture()
    {
        if (!IsMapPicture(assetPath)) return;

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.maxTextureSize = MaxSide;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.crunchedCompression = false;
        importer.isReadable = false;   // no extra copy in normal memory

        TextureImporterPlatformSettings pc = importer.GetPlatformTextureSettings("Standalone");
        pc.overridden = true;
        pc.maxTextureSize = MaxSide;
        pc.format = TextureImporterFormat.BC7;
        pc.textureCompression = TextureImporterCompression.Compressed;
        pc.compressionQuality = 50;   // Normal; Best takes much longer and looks the same on a map
        pc.crunchedCompression = false;
        importer.SetPlatformTextureSettings(pc);
    }

    // Says so in the Console if a map picture still came out uncompressed, so it is noticed before a release
    void OnPostprocessTexture(Texture2D texture)
    {
        if (!IsMapPicture(assetPath)) return;
        if (GraphicsFormatUtility.IsCompressedFormat(texture.format)) return;
        Debug.LogWarning("Map picture " + assetPath + " was imported uncompressed (" + texture.format + ", " +
                         texture.width + " x " + texture.height + "). It will use a lot of memory; check its import settings.");
    }
}
