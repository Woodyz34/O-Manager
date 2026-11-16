using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Keeps the installed game up to date by itself.
//
// When the game starts it asks GitHub for the newest release in the public download repository
// (the same place the website's Download button points to). If that release is newer than this game
// (Application.version, set by Tools > O-Manager > Build release), it downloads the file for this computer in the
// background while the intro plays. On the main menu a card shows the progress; when the download is done:
//   Windows: O-Manager-Setup.exe runs silently, replaces the game files and starts the game again.
//   Mac:     O-Manager-Mac.zip is unpacked, and when the game has closed a small script swaps in the new
//            O-Manager.app and opens it.
//   Linux:   O-Manager-Linux.tar.gz is unpacked, and when the game has closed a small script copies the new files
//            over the game's folder and starts it again.
// Saved runners and money are in PlayerPrefs (registry / Library/Preferences / ~/.config), never in the game's
// folder, so an update never touches them.
//
// On a Mac that runs the game straight from Downloads (not moved to Applications), macOS runs it from a hidden,
// read-only copy, and on Mac/Linux the game's folder may not be writable. Then the card only says that a new
// version is out and offers the website instead.
//
// "Later" stops it for this time; the next start tries again. Nothing happens in the Unity Editor, without
// internet, or when GitHub cannot be reached: the game just starts as usual.
//
// The bottom right corner of the main menu shows the version number.
public class GameUpdater : MonoBehaviour
{
    public const string ReleasesRepo = "Woodyz34/O-Manager-Download"; // same as website/config.js
    public const string SetupAssetName = "O-Manager-Setup.exe";      // Windows file attached to every release
    public const string MacAssetName = "O-Manager-Mac.zip";          // Mac file (O-Manager.app inside)
    public const string LinuxAssetName = "O-Manager-Linux.tar.gz";   // Linux file (O-Manager folder inside)
    public const string WebsiteUrl = "https://o-manager.se/#download";
    const string MainMenuScene = "MainMenu";

    static GameUpdater instance;

    enum State { Checking, UpToDate, Downloading, Ready, Installing, Failed, Cancelled, Manual }
    State state = State.Checking;

    string newVersion;
    string notes;
    string downloadUrl;
    long downloadSize;
    string setupPath;
    UnityWebRequest download;
    string manualReason;   // why the game cannot update itself here (State.Manual)
    string failReason;     // shown on the card when State.Failed

    // The card on the main menu
    GameObject root;
    GameObject card;
    TextMeshProUGUI titleText;
    TextMeshProUGUI statusText;
    TextMeshProUGUI notesText;
    RectTransform barFill;
    Button laterButton;
    Button retryButton;
    TextMeshProUGUI versionLabel;

    [Serializable] class ReleaseInfo { public string tag_name; public string body; public bool draft; public bool prerelease; public AssetInfo[] assets; }
    [Serializable] class AssetInfo { public string name; public string browser_download_url; public long size; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        if (instance != null)
        {
            return;
        }
        GameObject go = new GameObject("Game updater");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameUpdater>();
    }

    void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Build();
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        bool canUpdate = !Application.isEditor && AssetName != null;
        if (canUpdate)
        {
            StartCoroutine(CheckAndDownload());
        }
        else
        {
            state = State.UpToDate;
        }
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        download?.Abort();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            Refresh();
        }
    }

    static bool OnMainMenu => SceneManager.GetActiveScene().name == MainMenuScene;

    // ---------- Checking and downloading ----------

    IEnumerator CheckAndDownload()
    {
        state = State.Checking;
        using (UnityWebRequest request = UnityWebRequest.Get("https://api.github.com/repos/" + ReleasesRepo + "/releases/latest"))
        {
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("User-Agent", "O-Manager-Updater");
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                // No internet, no release yet (404) or GitHub busy: play as usual
                UnityEngine.Debug.Log("Update check skipped: " + request.error);
                state = State.UpToDate;
                yield break;
            }

            ReleaseInfo release = null;
            try { release = JsonUtility.FromJson<ReleaseInfo>(request.downloadHandler.text); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("Update check: bad answer from GitHub. " + e.Message); }
            if (release == null || release.draft || release.prerelease || !IsNewer(release.tag_name, Application.version))
            {
                state = State.UpToDate;
                yield break;
            }

            AssetInfo setup = null;
            if (release.assets != null)
            {
                foreach (AssetInfo asset in release.assets)
                {
                    if (asset != null && string.Equals(asset.name, AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        setup = asset;
                    }
                }
            }
            if (setup == null || string.IsNullOrEmpty(setup.browser_download_url))
            {
                UnityEngine.Debug.LogWarning("Update " + release.tag_name + " has no " + AssetName + ".");
                state = State.UpToDate;
                yield break;
            }

            newVersion = CleanVersion(release.tag_name);
            notes = ShortNotes(release.body);
            downloadUrl = setup.browser_download_url;
            downloadSize = setup.size;
        }

        manualReason = CannotSelfUpdateReason() ?? TriedBeforeReason();
        if (manualReason != null)
        {
            UnityEngine.Debug.Log("Update " + newVersion + " found, but this game cannot replace itself: " + manualReason);
            state = State.Manual;
            Refresh();
            yield break;
        }

        yield return Download();
    }

    // ---------- Computer types ----------

    // The release file for this computer, or null when this kind of computer has none
    static string AssetName
    {
        get
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer: return SetupAssetName;
                case RuntimePlatform.OSXPlayer: return MacAssetName;
                case RuntimePlatform.LinuxPlayer: return LinuxAssetName;
                default: return null;
            }
        }
    }

    static bool IsMac => Application.platform == RuntimePlatform.OSXPlayer;
    static bool IsLinux => Application.platform == RuntimePlatform.LinuxPlayer;

    // Mac: Application.dataPath is ".../O-Manager.app/Contents"
    static string MacAppPath => Path.GetDirectoryName(Application.dataPath.TrimEnd('/'));

    // Linux: Application.dataPath is ".../O-Manager_Data", next to the program "O-Manager.x86_64"
    static string LinuxFolder => Path.GetDirectoryName(Application.dataPath.TrimEnd('/'));
    static string LinuxDataName => Path.GetFileName(Application.dataPath.TrimEnd('/'));
    static string LinuxProgram
    {
        get
        {
            string name = LinuxDataName;
            if (name.EndsWith("_Data")) name = name.Substring(0, name.Length - 5);
            return Path.Combine(LinuxFolder, name + ".x86_64");
        }
    }

    // null when the game can update itself, otherwise a sentence for the card
    static string CannotSelfUpdateReason()
    {
        if (IsMac)
        {
            string app = MacAppPath;
            if (app.Contains("/AppTranslocation/") || !app.EndsWith(".app"))
            {
                return "Move O-Manager to the Applications folder and start it from there, and it will update by itself.";
            }
            if (!CanWriteIn(Path.GetDirectoryName(app)))
            {
                return "This computer does not let the game replace itself. Download the new version from o-manager.se.";
            }
        }
        else if (IsLinux)
        {
            if (!CanWriteIn(LinuxFolder) || !File.Exists(LinuxProgram))
            {
                return "The game's folder cannot be changed by the game. Download the new version from o-manager.se.";
            }
        }
        return null;
    }

    // Each install attempt is counted. If the game still starts as the old version after two tries (the swap was
    // stopped by the computer), it stops trying, so it does not close and reopen at every start.
    const string TriedKey = "OManager.Update.Tried";

    string TriedBeforeReason()
    {
        string[] tried = PlayerPrefs.GetString(TriedKey, "").Split('|');
        if (tried.Length == 2 && tried[0] == newVersion && int.TryParse(tried[1], out int count) && count >= 2)
        {
            return "The update did not finish on this computer. Download the new version from o-manager.se.";
        }
        return null;
    }

    void CountAttempt()
    {
        string[] tried = PlayerPrefs.GetString(TriedKey, "").Split('|');
        int count = tried.Length == 2 && tried[0] == newVersion && int.TryParse(tried[1], out int n) ? n : 0;
        PlayerPrefs.SetString(TriedKey, newVersion + "|" + (count + 1));
        PlayerPrefs.Save();
    }

    static bool CanWriteIn(string folder)
    {
        try
        {
            string test = Path.Combine(folder, ".o-manager-write-test");
            File.WriteAllText(test, "");
            File.Delete(test);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    IEnumerator Download()
    {
        state = State.Downloading;
        failReason = null;
        Refresh();
        string extension = IsMac ? ".zip" : IsLinux ? ".tar.gz" : ".exe";
        setupPath = Path.Combine(Path.GetTempPath(), "O-Manager-Update-" + newVersion + extension);

        // Already downloaded on an earlier start?
        if (File.Exists(setupPath) && downloadSize > 0 && new FileInfo(setupPath).Length == downloadSize)
        {
            state = State.Ready;
            Refresh();
            yield break;
        }

        download = new UnityWebRequest(downloadUrl, UnityWebRequest.kHttpVerbGET);
        download.downloadHandler = new DownloadHandlerFile(setupPath) { removeFileOnAbort = true };
        download.SetRequestHeader("User-Agent", "O-Manager-Updater");
        yield return download.SendWebRequest();

        if (state == State.Cancelled)
        {
            download.Dispose();
            download = null;
            yield break;
        }
        bool ok = download.result == UnityWebRequest.Result.Success
                  && File.Exists(setupPath)
                  && (downloadSize <= 0 || new FileInfo(setupPath).Length == downloadSize);
        if (!ok)
        {
            UnityEngine.Debug.LogWarning("Update download failed: " + download.error);
            failReason = "The update could not be downloaded. Check your internet connection.";
        }
        download.Dispose();
        download = null;
        state = ok ? State.Ready : State.Failed;
        Refresh();
    }

    void Update()
    {
        if (state == State.Downloading && download != null && barFill != null)
        {
            float progress = download.downloadProgress;
            barFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
            ulong bytes = download.downloadedBytes;
            string total = downloadSize > 0 ? " of " + Mb(downloadSize) : "";
            statusText.text = "Downloading " + Mb((long)bytes) + total + "…";
        }
        // Install as soon as the player can see what is happening (on the main menu)
        if (state == State.Ready && OnMainMenu && card != null && card.activeInHierarchy)
        {
            StartCoroutine(Install());
        }
    }

    IEnumerator Install()
    {
        state = State.Installing;
        Refresh();
        yield return new WaitForSecondsRealtime(1.2f); // let the player read "Installing"

        if (IsMac || IsLinux)
        {
            yield return InstallUnix();
            yield break;
        }

        try
        {
            ProcessStartInfo start = new ProcessStartInfo(setupPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1")
            {
                UseShellExecute = true,
            };
            Process.Start(start);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Could not start the update: " + e.Message);
            InstallFailed();
            yield break;
        }
        CountAttempt();
        Application.Quit();
    }

    // Mac and Linux: unpack now (so a bad file is found while the game still runs), then leave the swap to a
    // small script that waits for the game to close. A running program cannot replace its own files safely.
    IEnumerator InstallUnix()
    {
        string work = Path.Combine(Path.GetTempPath(), "O-Manager-Update-" + newVersion);
        string unpackError = null;
        bool done = false;
        bool mac = IsMac;
        string archive = setupPath;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                // ditto (Mac) and tar keep the "may run" flags on the game's programs
                if (mac) RunAndWait("/usr/bin/ditto", "-x -k \"" + archive + "\" \"" + work + "\"");
                else RunAndWait("tar", "-xzf \"" + archive + "\" -C \"" + work + "\"");
            }
            catch (Exception e)
            {
                unpackError = e.Message;
            }
            done = true;
        });
        while (!done)
        {
            yield return null;
        }

        string newCopy = null;
        if (unpackError == null)
        {
            newCopy = IsMac
                ? FirstOrNull(Directory.GetDirectories(work, "*.app"))
                : FirstOrNull(Directory.GetDirectories(work));
            if (newCopy == null) unpackError = "the downloaded file has no game in it";
        }
        if (unpackError != null)
        {
            UnityEngine.Debug.LogWarning("Could not unpack the update: " + unpackError);
            TryDelete(setupPath); // probably broken: download it again next time
            InstallFailed();
            yield break;
        }

        try
        {
            string script = Path.Combine(Path.GetTempPath(), "o-manager-update.sh");
            File.WriteAllText(script, IsMac ? MacScript(newCopy, work) : LinuxScript(newCopy, work));
            ProcessStartInfo start = new ProcessStartInfo("/usr/bin/nohup", "/bin/sh \"" + script + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(start);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Could not start the update: " + e.Message);
            InstallFailed();
            yield break;
        }
        CountAttempt();
        Application.Quit();
    }

    string MacScript(string newApp, string work)
    {
        string app = MacAppPath;
        return "#!/bin/sh\n" +
               "PID=" + Process.GetCurrentProcess().Id + "\n" +
               "APP=" + Quote(app) + "\n" +
               "NEW=" + Quote(newApp) + "\n" +
               "while kill -0 \"$PID\" 2>/dev/null; do sleep 0.5; done\n" +
               "xattr -dr com.apple.quarantine \"$NEW\" 2>/dev/null\n" +
               "rm -rf \"$APP.old\"\n" +
               "if mv \"$APP\" \"$APP.old\"; then\n" +
               "  if mv \"$NEW\" \"$APP\"; then rm -rf \"$APP.old\"; else mv \"$APP.old\" \"$APP\"; fi\n" +
               "fi\n" +
               "rm -rf " + Quote(work) + " " + Quote(setupPath) + "\n" +
               "open \"$APP\"\n";
    }

    string LinuxScript(string newFolder, string work)
    {
        return "#!/bin/sh\n" +
               "PID=" + Process.GetCurrentProcess().Id + "\n" +
               "DIR=" + Quote(LinuxFolder) + "\n" +
               "NEW=" + Quote(newFolder) + "\n" +
               "while kill -0 \"$PID\" 2>/dev/null; do sleep 0.5; done\n" +
               "cp -a \"$NEW/.\" \"$DIR/\"\n" +
               "chmod +x " + Quote(LinuxProgram) + "\n" +
               "rm -rf " + Quote(work) + " " + Quote(setupPath) + "\n" +
               "cd \"$DIR\" && nohup " + Quote(LinuxProgram) + " >/dev/null 2>&1 &\n";
    }

    // A path in single quotes for the shell
    static string Quote(string path)
    {
        return "'" + path.Replace("'", "'\\''") + "'";
    }

    static void RunAndWait(string program, string arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo(program, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        using (Process process = Process.Start(start))
        {
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Exception(program + " stopped with code " + process.ExitCode + ": " + errors);
            }
        }
    }

    static string FirstOrNull(string[] items) => items != null && items.Length > 0 ? items[0] : null;

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception) { }
    }

    void InstallFailed()
    {
        failReason = "The update could not be installed. You can download it from o-manager.se.";
        state = State.Failed;
        Refresh();
    }

    void OnLater()
    {
        if (download != null)
        {
            download.Abort();
        }
        state = State.Cancelled;
        Refresh();
    }

    void OnRetry()
    {
        if (state == State.Manual)
        {
            Application.OpenURL(WebsiteUrl);
            return;
        }
        if (string.IsNullOrEmpty(downloadUrl))
        {
            return;
        }
        StartCoroutine(Download());
    }

    // ---------- Versions ----------

    static string CleanVersion(string tag)
    {
        tag = (tag ?? "").Trim();
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
        return tag;
    }

    static Version ParseVersion(string text)
    {
        text = CleanVersion(text);
        // Keep only "1.2.3" from things like "1.2.3-beta"
        int end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
        text = text.Substring(0, end).Trim('.');
        if (text.Length == 0) return null;
        if (!text.Contains(".")) text += ".0";
        return Version.TryParse(text, out Version v) ? v : null;
    }

    // True when the release tag is a higher version than the running game. 1.0 and 1.0.0 count as the same.
    public static bool IsNewer(string tag, string current)
    {
        Version remote = ParseVersion(tag);
        Version local = ParseVersion(current);
        if (remote == null || local == null) return false;
        return Normal(remote) > Normal(local);
    }

    static Version Normal(Version v)
    {
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    static string Mb(long bytes)
    {
        return (bytes / 1048576f).ToString("0") + " MB";
    }

    // The first few lines of the release text, without Markdown signs
    static string ShortNotes(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        string[] lines = body.Replace("\r", "").Split('\n');
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        int count = 0;
        foreach (string raw in lines)
        {
            string line = raw.Trim().TrimStart('#', '*', '-', ' ').Replace("**", "");
            if (line.Length == 0) continue;
            if (count == 5) { text.Append("\n…"); break; }
            if (count > 0) text.Append('\n');
            text.Append("• ").Append(line);
            count++;
        }
        return text.ToString();
    }

    // ---------- The card ----------

    void Build()
    {
        root = new GameObject("Updater canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 240; // above the pages, below the Esc menu
        ScreenSettings.Fit(root.GetComponent<CanvasScaler>());

        versionLabel = CourseUI.Text("Version", root.transform, "Version " + Application.version, 18,
                                     CourseUI.Hex("FFFFFF", 0.75f), FontStyles.Normal, TextAlignmentOptions.BottomRight);
        CourseUI.Place(versionLabel.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 10), new Vector2(400, 28));

        GameObject veil = CourseUI.Panel("Veil", root.transform, CourseUI.Hex("10241A", 0.55f), false).gameObject;
        CourseUI.Stretch((RectTransform)veil.transform);

        Image panel = CourseUI.CardPanel("Card", veil.transform);
        card = veil;
        CourseUI.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 0));
        CourseUI.Column(panel.gameObject, 16, new RectOffset(48, 48, 40, 40));
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        titleText = CourseUI.Text("Title", panel.transform, "New version", 42, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(titleText, 54);

        notesText = CourseUI.Text("Notes", panel.transform, "", 22, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        notesText.overflowMode = TextOverflowModes.Overflow; // the card's column layout sizes it to the text

        Image track = CourseUI.Panel("Bar", panel.transform, CourseUI.FieldBorder);
        CourseUI.Size(track, 16);
        Image fill = CourseUI.Panel("Fill", track.transform, CourseUI.Accent);
        barFill = fill.rectTransform;
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = new Vector2(0, 1);
        barFill.offsetMin = Vector2.zero;
        barFill.offsetMax = Vector2.zero;

        statusText = CourseUI.Text("Status", panel.transform, "", 22, CourseUI.InkMuted, FontStyles.Italic, TextAlignmentOptions.Center);
        CourseUI.Size(statusText, 30);

        RectTransform row = CourseUI.Rect("Buttons", panel.transform);
        CourseUI.Row(row.gameObject, 12, new RectOffset(0, 0, 4, 0));
        CourseUI.Size(row, 64);
        retryButton = CourseUI.Button("Retry", row, "Try again", CourseUI.ButtonKind.Primary, OnRetry, 24);
        CourseUI.Size(retryButton, -1, -1, 1);
        laterButton = CourseUI.Button("Later", row, "Later", CourseUI.ButtonKind.Light, OnLater, 24);
        CourseUI.Size(laterButton, -1, -1, 1);

        HoverTip.Add(laterButton, "Play this version now. The game tries to update again next time it starts.");

        Refresh();
    }

    void Refresh()
    {
        if (root == null) return;
        bool menu = OnMainMenu;
        versionLabel.gameObject.SetActive(menu);

        bool show = menu && (state == State.Downloading || state == State.Ready || state == State.Installing || state == State.Failed
                             || state == State.Manual);
        card.SetActive(show);
        // Off everywhere but the main menu, so no other page takes it for an open pop-up
        root.SetActive(menu);
        if (!show) return;

        titleText.text = state == State.Manual ? "Version " + newVersion + " is out" : "Updating to version " + newVersion;
        notesText.text = string.IsNullOrEmpty(notes) ? "" : notes;
        notesText.gameObject.SetActive(!string.IsNullOrEmpty(notes));
        barFill.transform.parent.gameObject.SetActive(state != State.Manual);
        retryButton.gameObject.SetActive(state == State.Failed || state == State.Manual);
        CourseUI.SetButtonText(retryButton, state == State.Manual ? "Open o-manager.se" : "Try again");
        laterButton.gameObject.SetActive(state != State.Installing);
        CourseUI.SetButtonText(laterButton, state == State.Failed || state == State.Manual ? "Play anyway" : "Later");

        switch (state)
        {
            case State.Downloading:
                statusText.text = "Downloading…";
                break;
            case State.Ready:
            case State.Installing:
                barFill.anchorMax = new Vector2(1, 1);
                statusText.text = "Installing. The game starts again by itself.";
                break;
            case State.Failed:
                statusText.text = failReason ?? "The update could not be downloaded. Check your internet connection.";
                break;
            case State.Manual:
                statusText.text = manualReason;
                break;
        }
    }
}
