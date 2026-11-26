using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Tools > O-Manager > Build release...
//
// One click to make a new version of the game for players:
//   1. sets the version number (Player Settings > Version), which the game's updater compares with GitHub,
//   2. builds the game for each ticked computer type (the folders are emptied first):
//        Windows -> Builds/Windows, then Inno Setup (free, https://jrsoftware.org/isdl.php) on installer/O-Manager.iss
//                   makes Builds/Release/O-Manager-Setup.exe
//        Mac     -> Builds/Mac/O-Manager.app (Intel + Apple silicon), packed as Builds/Release/O-Manager-Mac.zip
//        Linux   -> Builds/Linux, packed as Builds/Release/O-Manager-Linux.tar.gz
//      Mac and Linux need "Mac Build Support (Mono)" and "Linux Build Support (Mono)" added to this Unity version
//      in Unity Hub (Installs > the gear > Add modules). Without them those boxes are greyed out.
//   3. opens that folder and the "new release" page on GitHub with the version and notes filled in.
// Then drag the files onto the GitHub page and press Publish release.
//
// Speed (2026-11-26): the map pictures are not part of Unity's build any more. Before building, MapPictureCache makes
// the compressed picture files in Assets/StreamingAssets/MapPictures, but only for maps whose PNG changed since last
// time; Unity then just copies them into each game. The computer type the editor is on now is built last, so the
// editor switches platform one time less, and the dialog at the end says how long each step took. Every installed game updates itself the
// next time it starts, and the website's Download button gives the new version for the visitor's computer.
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
    static string MacBuildDir => Path.Combine(ProjectRoot, "Builds", "Mac");
    static string LinuxBuildDir => Path.Combine(ProjectRoot, "Builds", "Linux");
    static string ReleaseDir => Path.Combine(ProjectRoot, "Builds", "Release");
    static string ScriptPath => Path.Combine(ProjectRoot, "installer", "O-Manager.iss");
    static string SetupPath => Path.Combine(ReleaseDir, GameUpdater.SetupAssetName);
    static string MacZipPath => Path.Combine(ReleaseDir, GameUpdater.MacAssetName);
    static string LinuxTarPath => Path.Combine(ReleaseDir, GameUpdater.LinuxAssetName);

    const string WindowsKey = "OManager.Release.Windows";
    const string MacKey = "OManager.Release.Mac";
    const string LinuxKey = "OManager.Release.Linux";

    bool buildWindows = true;
    bool buildMac = true;
    bool buildLinux = true;

    static bool HasModule(BuildTarget target) => BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target);

    [MenuItem("Tools/O-Manager/Build release...", false, 40)]
    static void Open()
    {
        ReleaseBuilder window = GetWindow<ReleaseBuilder>(true, "Build release", true);
        window.minSize = new Vector2(480, 520);
        window.buildWindows = EditorPrefs.GetBool(WindowsKey, true);
        window.buildMac = EditorPrefs.GetBool(MacKey, true);
        window.buildLinux = EditorPrefs.GetBool(LinuxKey, true);
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
        string typed = (version ?? "").Trim().TrimStart('v', 'V', '.', ' ');
        if (typed.Length > 0 && !GameUpdater.IsNewer(typed, PlayerSettings.bundleVersion))
        {
            EditorGUILayout.HelpBox("This is not higher than the version now (" + PlayerSettings.bundleVersion + "). The built game would say " +
                                    typed + ", so if you publish it under a higher release number, every player's game downloads it, " +
                                    "still sees an old version, and ends up with \"The update did not finish\". Use a higher number.",
                                    MessageType.Warning);
        }

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

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Computers", EditorStyles.boldLabel);
        buildWindows = EditorGUILayout.ToggleLeft("Windows (" + GameUpdater.SetupAssetName + ")", buildWindows);
        bool macOk = HasModule(BuildTarget.StandaloneOSX);
        bool linuxOk = HasModule(BuildTarget.StandaloneLinux64);
        buildMac = PlatformToggle("Mac (" + GameUpdater.MacAssetName + ")", macOk, buildMac);
        buildLinux = PlatformToggle("Linux (" + GameUpdater.LinuxAssetName + ")", linuxOk, buildLinux);
        if (!macOk || !linuxOk)
        {
            EditorGUILayout.HelpBox("To build for Mac or Linux, open Unity Hub > Installs > the gear next to this Unity version > Add modules, " +
                                    "tick \"Mac Build Support (Mono)\" and \"Linux Build Support (Mono)\", install, and restart Unity.", MessageType.Info);
        }
        if (!buildWindows)
        {
            EditorGUILayout.HelpBox("Windows is not ticked. Windows players will not get this version.", MessageType.Warning);
        }

        openGitHub = EditorGUILayout.ToggleLeft("Open the GitHub release page when done", openGitHub);

        bool any = buildWindows || (buildMac && macOk) || (buildLinux && linuxOk);
        EditorGUILayout.Space(10);
        GUI.enabled = !string.IsNullOrWhiteSpace(version) && any;
        if (GUILayout.Button("Build release", GUILayout.Height(36)))
        {
            EditorPrefs.SetString(NotesKey, notes ?? "");
            EditorPrefs.SetBool(WindowsKey, buildWindows);
            EditorPrefs.SetBool(MacKey, buildMac);
            EditorPrefs.SetBool(LinuxKey, buildLinux);
            string v = version.Trim().TrimStart('v', 'V', '.', ' ');
            bool win = buildWindows, mac = buildMac && macOk, linux = buildLinux && linuxOk;
            EditorApplication.delayCall += () => BuildRelease(v, notes ?? "", openGitHub, win, mac, linux);
            Close();
        }
        GUI.enabled = true;
    }

    // A greyed-out box when the module is missing; the choice is remembered for when it is installed
    static bool PlatformToggle(string label, bool moduleInstalled, bool value)
    {
        if (moduleInstalled) return EditorGUILayout.ToggleLeft(label, value);
        GUI.enabled = false;
        EditorGUILayout.ToggleLeft(label + "  (module not installed)", false);
        GUI.enabled = true;
        return value;
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

    static void BuildRelease(string version, string notes, bool openGitHub, bool windows, bool mac, bool linux)
    {
        var clock = Stopwatch.StartNew();
        var times = new List<string>();

        // 0. Map pictures: only new or changed maps are compressed again
        MapPictureCache.Result pictures = MapPictureCache.PrepareAll();
        times.Add("Map pictures (" + pictures.reused.Count + " unchanged, " + pictures.made.Count + " made again): " + Took(clock));
        if (pictures.failed.Count > 0)
        {
            EditorUtility.DisplayDialog("Build release", "Some map pictures could not be prepared, so those maps would have no picture " +
                                        "in the game. Nothing was built.\n\n" + pictures.Summary(), "OK");
            return;
        }

        // 1. Version
        PlayerSettings.bundleVersion = version;
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(ReleaseDir);

        BuildTarget activeBefore = EditorUserBuildSettings.activeBuildTarget;
        var made = new List<string>();
        var problems = new List<string>();
        try
        {
            // 2. One computer type at a time. A failure on one does not stop the others. Switching the editor to
            // another computer type costs a minute or two (scripts are compiled again), so the one it is on now
            // goes last: then there is no switching back afterwards.
            var steps = new List<KeyValuePair<BuildTarget, Action>>();
            if (windows) steps.Add(new KeyValuePair<BuildTarget, Action>(BuildTarget.StandaloneWindows64, () => BuildWindows(version, made, problems, times, clock)));
            if (mac) steps.Add(new KeyValuePair<BuildTarget, Action>(BuildTarget.StandaloneOSX, () => BuildMac(made, problems, times, clock)));
            if (linux) steps.Add(new KeyValuePair<BuildTarget, Action>(BuildTarget.StandaloneLinux64, () => BuildLinux(made, problems, times, clock)));
            foreach (var step in steps.Where(s => s.Key != activeBefore).Concat(steps.Where(s => s.Key == activeBefore)).ToList())
            {
                step.Value();
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            // Building Mac or Linux switches the editor to that platform; go back to what it was
            if (EditorUserBuildSettings.activeBuildTarget != activeBefore)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(activeBefore), activeBefore);
                times.Add("Switching the editor back: " + Took(clock));
            }
        }

        if (made.Count == 0)
        {
            EditorUtility.DisplayDialog("Build release", "Nothing was made.\n\n" + string.Join("\n", problems) +
                                        "\n\nTime taken:\n  " + string.Join("\n  ", times), "OK");
            return;
        }

        File.WriteAllText(Path.Combine(ReleaseDir, "notes-" + version + ".txt"), notes);

        // 3. Show the files and the GitHub page
        EditorUtility.RevealInFinder(Path.Combine(ReleaseDir, made[0]));
        if (openGitHub)
        {
            string url = "https://github.com/" + GameUpdater.ReleasesRepo + "/releases/new"
                         + "?tag=v" + Uri.EscapeDataString(version)
                         + "&title=" + Uri.EscapeDataString("O-Manager " + version)
                         + "&body=" + Uri.EscapeDataString(notes);
            Application.OpenURL(url);
        }
        string message = "Version " + version + " is ready in Builds/Release:\n\n  " + string.Join("\n  ", made) +
                         "\n\nDrag " + (made.Count == 1 ? "it" : "all of them") + " onto the GitHub page that opened and press Publish release. " +
                         "Installed games update the next time they start.";
        if (problems.Count > 0)
        {
            message += "\n\nNot made:\n" + string.Join("\n", problems);
        }
        message += "\n\nTime taken:\n  " + string.Join("\n  ", times);
        UnityEngine.Debug.Log("Release " + version + " time taken:\n" + string.Join("\n", times));
        EditorUtility.DisplayDialog("Build release", message, "OK");
    }

    // Empties folder and builds the game there. Returns false (and notes why) when it did not work.
    static bool BuildGame(BuildTarget target, string folder, string fileName, string label, List<string> problems)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            problems.Add(label + ": could not empty " + folder + " (is the game still running?) " + e.Message);
            return false;
        }

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(folder, fileName),
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };
        // Let go of textures and other assets the editor still holds from the last build or Play,
        // so the big map pictures are not in memory twice while Unity writes this build
        EditorUtility.UnloadUnusedAssetsImmediate();
        GC.Collect();
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            problems.Add(label + ": the Unity build failed (" + report.summary.result + "). See the Console.");
            return false;
        }
        // The map pictures come from Assets/StreamingAssets; without them every built-in map would be blank
        int pictureFiles = Directory.GetFiles(folder, "*" + MapPictureFiles.Extension, SearchOption.AllDirectories).Length;
        int expected = Directory.Exists(MapPictureFiles.ReadyFolder)
            ? Directory.GetFiles(MapPictureFiles.ReadyFolder, "*" + MapPictureFiles.Extension).Length : 0;
        if (pictureFiles < expected)
        {
            problems.Add(label + ": only " + pictureFiles + " of " + expected + " map pictures got into the game.");
            return false;
        }
        UnityEngine.Debug.Log("Release: " + label + " game built in " + folder + " (" + pictureFiles + " map pictures)");
        return true;
    }

    static string SizeText(string path) => (new FileInfo(path).Length / 1048576) + " MB";

    // How long since the last step, as m:ss, and starts timing the next one
    static string Took(Stopwatch clock)
    {
        TimeSpan t = clock.Elapsed;
        clock.Restart();
        return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
    }

    static void BuildWindows(string version, List<string> made, List<string> problems, List<string> times, Stopwatch clock)
    {
        bool built = BuildGame(BuildTarget.StandaloneWindows64, BuildDir, PlayerSettings.productName + ".exe", "Windows", problems);
        times.Add("Windows game: " + Took(clock));
        if (!built) return;

        string iscc = FindIscc();
        if (iscc == null)
        {
            problems.Add("Windows: Inno Setup was not found, so no setup.exe was made (the game is in " + BuildDir + ").");
            return;
        }
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
            times.Add("Windows setup.exe (Inno Setup): " + Took(clock));
        }

        if (exitCode != 0 || !File.Exists(SetupPath))
        {
            UnityEngine.Debug.LogError("Inno Setup failed (" + exitCode + "):\n" + output);
            problems.Add("Windows: Inno Setup could not make the setup. See the Console.");
            return;
        }
        made.Add(GameUpdater.SetupAssetName + " (" + SizeText(SetupPath) + ")");
        UnityEngine.Debug.Log("Release: " + SetupPath + " (" + SizeText(SetupPath) + ")");
    }

    static void BuildMac(List<string> made, List<string> problems, List<string> times, Stopwatch clock)
    {
        SetMacUniversal();
        string app = PlayerSettings.productName + ".app";
        bool built = BuildGame(BuildTarget.StandaloneOSX, MacBuildDir, app, "Mac", problems);
        times.Add("Mac game (with switching the editor to Mac): " + Took(clock));
        if (!built) return;
        try
        {
            EditorUtility.DisplayProgressBar("Build release", "Packing " + GameUpdater.MacAssetName + "…", 0.5f);
            ReleaseArchives.ZipMacApp(Path.Combine(MacBuildDir, app), MacZipPath);
            made.Add(GameUpdater.MacAssetName + " (" + SizeText(MacZipPath) + ")");
            UnityEngine.Debug.Log("Release: " + MacZipPath + " (" + SizeText(MacZipPath) + ")");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogException(e);
            problems.Add("Mac: could not pack the zip. " + e.Message);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            times.Add("Mac zip: " + Took(clock));
        }
    }

    static void BuildLinux(List<string> made, List<string> problems, List<string> times, Stopwatch clock)
    {
        bool built = BuildGame(BuildTarget.StandaloneLinux64, LinuxBuildDir, PlayerSettings.productName + ".x86_64", "Linux", problems);
        times.Add("Linux game (with switching the editor to Linux): " + Took(clock));
        if (!built) return;
        try
        {
            EditorUtility.DisplayProgressBar("Build release", "Packing " + GameUpdater.LinuxAssetName + "…", 0.5f);
            // Leave out Unity's debug symbol files, players do not need them
            foreach (string debug in Directory.GetFiles(LinuxBuildDir, "*.debug", SearchOption.TopDirectoryOnly))
            {
                File.Delete(debug);
            }
            ReleaseArchives.TarGzFolder(LinuxBuildDir, PlayerSettings.productName, LinuxTarPath);
            made.Add(GameUpdater.LinuxAssetName + " (" + SizeText(LinuxTarPath) + ")");
            UnityEngine.Debug.Log("Release: " + LinuxTarPath + " (" + SizeText(LinuxTarPath) + ")");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogException(e);
            problems.Add("Linux: could not pack the tar.gz. " + e.Message);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            times.Add("Linux tar.gz: " + Took(clock));
        }
    }

    // One Mac game that runs on both Intel and Apple silicon Macs. The setting lives in the Mac build module,
    // which is only there when the module is installed, so it is reached by name.
    static void SetMacUniversal()
    {
        try
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type settings = assembly.GetType("UnityEditor.OSXStandalone.UserBuildSettings", false);
                PropertyInfo architecture = settings?.GetProperty("architecture", BindingFlags.Public | BindingFlags.Static);
                if (architecture == null || !architecture.CanWrite) continue;
                architecture.SetValue(null, Enum.Parse(architecture.PropertyType, "x64ARM64"));
                return;
            }
            UnityEngine.Debug.LogWarning("Build release: could not find the Mac architecture setting; the Mac game uses whatever is set in Build Profiles.");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Build release: could not set the Mac game to Intel + Apple silicon: " + e.Message);
        }
    }
}
