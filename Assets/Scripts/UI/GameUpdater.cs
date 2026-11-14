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
// (Application.version, set by Tools > O-Manager > Build release), it downloads O-Manager-Setup.exe in the
// background while the intro plays. On the main menu a card shows the progress; when the download is done
// the setup runs silently, replaces the game files and starts the game again. Saved runners and money are
// in PlayerPrefs (the registry), so an update never touches them.
//
// "Later" stops it for this time; the next start tries again. Nothing happens in the Unity Editor, without
// internet, or when GitHub cannot be reached: the game just starts as usual.
//
// The bottom right corner of the main menu shows the version number.
public class GameUpdater : MonoBehaviour
{
    public const string ReleasesRepo = "Woodyz34/O-Manager-Download"; // same as website/config.js
    public const string SetupAssetName = "O-Manager-Setup.exe";      // file attached to every release
    const string MainMenuScene = "MainMenu";

    static GameUpdater instance;

    enum State { Checking, UpToDate, Downloading, Ready, Installing, Failed, Cancelled }
    State state = State.Checking;

    string newVersion;
    string notes;
    string downloadUrl;
    long downloadSize;
    string setupPath;
    UnityWebRequest download;

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

        bool canUpdate = !Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer;
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
                    if (asset != null && string.Equals(asset.name, SetupAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        setup = asset;
                    }
                }
            }
            if (setup == null || string.IsNullOrEmpty(setup.browser_download_url))
            {
                UnityEngine.Debug.LogWarning("Update " + release.tag_name + " has no " + SetupAssetName + ".");
                state = State.UpToDate;
                yield break;
            }

            newVersion = CleanVersion(release.tag_name);
            notes = ShortNotes(release.body);
            downloadUrl = setup.browser_download_url;
            downloadSize = setup.size;
        }

        yield return Download();
    }

    IEnumerator Download()
    {
        state = State.Downloading;
        Refresh();
        setupPath = Path.Combine(Path.GetTempPath(), "O-Manager-Setup-" + newVersion + ".exe");

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
            state = State.Failed;
            Refresh();
            yield break;
        }
        PlayerPrefs.Save();
        Application.Quit();
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

        bool show = menu && (state == State.Downloading || state == State.Ready || state == State.Installing || state == State.Failed);
        card.SetActive(show);
        // Off everywhere but the main menu, so no other page takes it for an open pop-up
        root.SetActive(menu);
        if (!show) return;

        titleText.text = "Updating to version " + newVersion;
        notesText.text = string.IsNullOrEmpty(notes) ? "" : notes;
        notesText.gameObject.SetActive(!string.IsNullOrEmpty(notes));
        retryButton.gameObject.SetActive(state == State.Failed);
        laterButton.gameObject.SetActive(state != State.Installing);
        CourseUI.SetButtonText(laterButton, state == State.Failed ? "Play anyway" : "Later");

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
                statusText.text = "The update could not be downloaded. Check your internet connection.";
                break;
        }
    }
}
