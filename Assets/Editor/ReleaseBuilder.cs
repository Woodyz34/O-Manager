using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Tools > O-Manager > Build release (setup.exe)
//
// One click to make a new version of the game for players:
//   1. sets the version number (Player Settings > Version), which the game's updater compares with GitHub,
//   2. builds the Windows game into Builds/Windows (the folder is emptied first),
//   3. runs Inno Setup (free, https://jrsoftware.org/isdl.php) on installer/O-Manager.iss, which makes
//      Builds/Release/O-Manager-Setup.exe,
//   4. opens that folder and the "new release" page on GitHub with the version and notes filled in.
// Then drag O-Manager-Setup.exe onto the GitHub page and press Publish release. Every installed game
// updates itself the next time it starts, and the website's Download button gives the new setup.
public class ReleaseBuilder : EditorWindow
{
    const string IsccPathKey = "OManager.Release.IsccPath";
    const string NotesKey = "OManager.Release.Notes";

    string version;
    string notes;
    bool openGitHub = true;
    Vector2 scroll;

    static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    static string BuildDir => Path.Combine(ProjectRoot, "Builds", "Windows");
    static string ReleaseDir => Path.Combine(ProjectRoot, "Builds", "Release");
    static string ScriptPath => Path.Combine(ProjectRoot, "installer", "O-Manager.iss");
    static string SetupPath => Path.Combine(ReleaseDir, GameUpdater.SetupAssetName);

    [MenuItem("Tools/O-Manager/Build release (setup.exe)...", false, 40)]
    static void Open()
    {
        ReleaseBuilder window = GetWindow<ReleaseBuilder>(true, "Build release", true);
        window.minSize = new Vector2(480, 420);
        window.version = NextVersion(PlayerSettings.bundleVersion);
        window.notes = EditorPrefs.GetString(NotesKey, "");
        window.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Version now", PlayerSettings.bundleVersion);
        version = EditorGUILayout.TextField("New version", version);
        EditorGUILayout.HelpBox("Use numbers like 1.0.4. It must be higher than the last release, or the games will not update.", MessageType.None);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("What's new (shown in the game while it updates, and on GitHub)");
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(140));
        notes = EditorGUILayout.TextArea(notes, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);
        string iscc = FindIscc();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Inno Setup", iscc ?? "not found", EditorStyles.wordWrappedLabel);
        if (GUILayout.Button("Choose…", GUILayout.Width(80)))
        {
            string chosen = EditorUtility.OpenFilePanel("Find ISCC.exe (in the Inno Setup folder)", "C:\\Program Files (x86)", "exe");
            if (!string.IsNullOrEmpty(chosen)) EditorPrefs.SetString(IsccPathKey, chosen);
        }
        EditorGUILayout.EndHorizontal();
        if (iscc == null)
        {
            EditorGUILayout.HelpBox("Install Inno Setup 6 (free) first. The game is still built without it, but no setup.exe is made.", MessageType.Warning);
            if (GUILayout.Button("Download Inno Setup")) Application.OpenURL("https://jrsoftware.org/isdl.php");
        }

        openGitHub = EditorGUILayout.ToggleLeft("Open the GitHub release page when done", openGitHub);

        EditorGUILayout.Space(10);
        GUI.enabled = !string.IsNullOrWhiteSpace(version);
        if (GUILayout.Button("Build game and setup.exe", GUILayout.Height(36)))
        {
            EditorPrefs.SetString(NotesKey, notes ?? "");
            string v = version.Trim().TrimStart('v', 'V');
            EditorApplication.delayCall += () => BuildRelease(v, notes ?? "", openGitHub);
            Close();
        }
        GUI.enabled = true;
    }

    // 1.0 -> 1.0.1, 1.0.3 -> 1.0.4
    static string NextVersion(string current)
    {
        List<int> parts = (current ?? "1.0").Split('.').Select(p => int.TryParse(p.Trim(), out int n) ? n : 0).ToList();
        while (parts.Count < 3) parts.Add(0);
        parts[parts.Count - 1]++;
        return string.Join(".", parts);
    }

    static string FindIscc()
    {
        string saved = EditorPrefs.GetString(IsccPathKey, "");
        if (!string.IsNullOrEmpty(saved) && File.Exists(saved)) return saved;

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        foreach (string root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(root, "Inno Setup*").OrderByDescending(d => d))
                {
                    string exe = Path.Combine(dir, "ISCC.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch (Exception) { }
        }
        return null;
    }

    static void BuildRelease(string version, string notes, bool openGitHub)
    {
        // 1. Version
        PlayerSettings.bundleVersion = version;
        AssetDatabase.SaveAssets();

        // 2. Build the game into an empty folder, so nothing old ends up in the setup
        try
        {
            if (Directory.Exists(BuildDir)) Directory.Delete(BuildDir, true);
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Build release", "Could not empty " + BuildDir + "\nIs the game still running?\n\n" + e.Message, "OK");
            return;
        }
        Directory.CreateDirectory(BuildDir);

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(BuildDir, PlayerSettings.productName + ".exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("Build release", "The Unity build failed (" + report.summary.result + "). See the Console.", "OK");
            return;
        }
        UnityEngine.Debug.Log("Release " + version + ": game built in " + BuildDir);

        // 3. setup.exe
        string iscc = FindIscc();
        if (iscc == null)
        {
            EditorUtility.DisplayDialog("Build release", "The game is built in\n" + BuildDir + "\n\nbut Inno Setup was not found, so no setup.exe was made. Install Inno Setup 6 and try again.", "OK");
            EditorUtility.RevealInFinder(BuildDir);
            return;
        }
        Directory.CreateDirectory(ReleaseDir);
        if (File.Exists(SetupPath)) File.Delete(SetupPath);

        string args = "/Q \"/DAppVersion=" + version + "\" \"/DBuildDir=" + BuildDir + "\" \"/DOutputDir=" + ReleaseDir + "\" \"" + ScriptPath + "\"";
        string output;
        int exitCode;
        try
        {
            EditorUtility.DisplayProgressBar("Build release", "Packing O-Manager-Setup.exe with Inno Setup (a few minutes)…", 0.5f);
            ProcessStartInfo start = new ProcessStartInfo(iscc, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(ScriptPath),
            };
            using (Process process = Process.Start(start))
            {
                var errorTask = process.StandardError.ReadToEndAsync();
                output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                output += errorTask.Result;
                exitCode = process.ExitCode;
            }
        }
        catch (Exception e)
        {
            output = e.Message;
            exitCode = -1;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (exitCode != 0 || !File.Exists(SetupPath))
        {
            UnityEngine.Debug.LogError("Inno Setup failed (" + exitCode + "):\n" + output);
            EditorUtility.DisplayDialog("Build release", "Inno Setup could not make the setup. See the Console.", "OK");
            return;
        }

        File.WriteAllText(Path.Combine(ReleaseDir, "notes-" + version + ".txt"), notes);
        long mb = new FileInfo(SetupPath).Length / 1048576;
        UnityEngine.Debug.Log("Release " + version + ": " + SetupPath + " (" + mb + " MB)");

        // 4. Show the file and the GitHub page
        EditorUtility.RevealInFinder(SetupPath);
        if (openGitHub)
        {
            string url = "https://github.com/" + GameUpdater.ReleasesRepo + "/releases/new"
                         + "?tag=v" + Uri.EscapeDataString(version)
                         + "&title=" + Uri.EscapeDataString("O-Manager " + version)
                         + "&body=" + Uri.EscapeDataString(notes);
            Application.OpenURL(url);
        }
        EditorUtility.DisplayDialog("Build release",
            "O-Manager-Setup.exe for version " + version + " is ready (" + mb + " MB).\n\n" +
            "Drag it onto the GitHub page that opened and press Publish release. " +
            "Installed games update the next time they start.", "OK");
    }
}
