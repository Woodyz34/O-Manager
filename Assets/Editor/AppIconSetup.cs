using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Makes the O-Manager icon (Assets/Art/Icon/OManagerIcon.png, drawn by Docs/app_icon.py) the game's icon,
// so the built game shows it on the .exe, in the taskbar and on the window instead of the Unity logo.
// It is set when Unity opens the project (and again if someone clears it). It can also be set from
// Tools > O-Manager > Developer > Set game icon.
[InitializeOnLoad]
public class AppIconSetup : AssetPostprocessor
{
    const string IconPath = "Assets/Art/Icon/OManagerIcon.png";

    static AppIconSetup()
    {
        EditorApplication.delayCall += () => Apply(false);
    }

    [MenuItem("Tools/O-Manager/Developer (rarely needed)/Set game icon", false, 122)]
    static void ApplyFromMenu()
    {
        Apply(true);
    }

    static void Apply(bool tell)
    {
        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon == null)
        {
            if (tell) Debug.LogWarning("Game icon not found at " + IconPath);
            return;
        }
        Texture2D[] current = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
        if (current != null && current.Length > 0 && current[0] == icon)
        {
            if (tell) Debug.Log("The O-Manager icon is already the game icon.");
            return;
        }
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        AssetDatabase.SaveAssets();
        Debug.Log("Set the O-Manager icon as the game icon.");
    }

    // The icon and the intro's emblem are kept sharp: no compression
    void OnPreprocessTexture()
    {
        if (assetPath != IconPath && !assetPath.StartsWith("Assets/Resources/Intro/"))
        {
            return;
        }
        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
    }
}
