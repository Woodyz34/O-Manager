using UnityEditor;

// Pictures dropped into the card picture folders are imported as one whole sprite each,
// so a flag, club badge or photo is never cut into several small sprites.
public class CardPictureImporter : AssetPostprocessor
{
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
    }
}
