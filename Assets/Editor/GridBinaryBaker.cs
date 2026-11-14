using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// Bakar om grid-JSON-filerna i Resources/2d-gridJson till snabba binärfiler (<namn>_bin.bytes).
// Körs automatiskt när en JSON-fil importeras eller är nyare än sin binärfil, och går även att köra från menyn.
public class GridBinaryBaker : AssetPostprocessor
{
    private const string GridFolder = "Assets/Resources/2d-gridJson";

    [InitializeOnLoadMethod]
    private static void BakeOutdatedOnLoad()
    {
        // Vänta tills editorn har laddat klart innan vi rör AssetDatabase
        EditorApplication.delayCall += () => BakeAll(false);
    }

    [MenuItem("Tools/O-Manager/Developer (rarely needed)/Rebake old Illern grid data", false, 120)]
    private static void BakeAllFromMenu()
    {
        BakeAll(true);
    }

    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        foreach (string path in importedAssets)
        {
            if (IsGridJson(path))
            {
                Bake(path);
            }
        }
    }

    private static bool IsGridJson(string assetPath)
    {
        return assetPath.StartsWith(GridFolder + "/") && assetPath.EndsWith(".json");
    }

    private static string BinaryPathFor(string jsonPath)
    {
        return Path.Combine(Path.GetDirectoryName(jsonPath), Path.GetFileNameWithoutExtension(jsonPath) + GridBinary.Suffix + ".bytes").Replace('\\', '/');
    }

    private static void BakeAll(bool force)
    {
        if (!Directory.Exists(GridFolder))
        {
            return;
        }

        foreach (string file in Directory.GetFiles(GridFolder, "*.json"))
        {
            string jsonPath = file.Replace('\\', '/');
            string binaryPath = BinaryPathFor(jsonPath);
            if (force || !File.Exists(binaryPath) || File.GetLastWriteTimeUtc(jsonPath) > File.GetLastWriteTimeUtc(binaryPath))
            {
                Bake(jsonPath);
            }
        }
    }

    private static void Bake(string jsonPath)
    {
        string binaryPath = BinaryPathFor(jsonPath);
        try
        {
            GridData data = JsonConvert.DeserializeObject<GridData>(File.ReadAllText(jsonPath));
            using (var stream = File.Create(binaryPath))
            {
                GridBinary.Write(data, stream);
            }
            AssetDatabase.ImportAsset(binaryPath);
            Debug.Log($"GridBinaryBaker: bakade {binaryPath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"GridBinaryBaker: kunde inte baka {jsonPath}: {e}");
        }
    }
}
