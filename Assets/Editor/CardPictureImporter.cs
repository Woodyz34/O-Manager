using UnityEditor;

// Pictures dropped into the card picture folders are imported as one whole sprite each,
// so a flag, club badge or photo is never cut into several small sprites.
public class CardPictureImporter : AssetPostprocessor
{
    // Flags and club badges are only shown small, so big pictures are scaled down when imported
    const int EmblemMaxSize = 512;

    // Raised when the settings below change, so Unity imports the pictures again
    public override uint GetVersion() { return 2; }

    static readonly string[] Folders =
    {
        "Assets/Resources/Emblem/",
        "Assets/Resources/Profilbilder/",
        "Assets/Resources/Basbilder/",
    };

    void OnPreprocessTexture()
    {
        bool inFolder = false;
        foreach (string folder in Folders)
        {
            if (assetPath.StartsWith(folder)) inFolder = true;
        }
        if (!inFolder) return;

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        if (assetPath.StartsWith("Assets/Resources/Emblem/")) importer.maxTextureSize = EmblemMaxSize;
    }
}
