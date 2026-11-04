using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using MapImport;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Tools > O-Manager > Download sent-in maps: fetches the maps players have sent in (Cloud Save Player Files named
// map-<id>-*, see CloudMaps) with the Cloud Save admin API, so the browser is not needed, and imports them.
//
// It needs a service account (Unity Cloud dashboard > Administration > Service accounts) with the project role
// Cloud Save Viewer. Its key id and secret are kept in EditorPrefs on this computer only, never in the project.
// The files land in <project>/SentInMaps/<player>/ (outside Assets, not in git).
public class SentMapsDownloader : EditorWindow
{
    const string Api = "https://services.api.unity.com/cloud-save/v1/files/projects/";
    const string KeyIdPref = "O-Manager.SentMaps.KeyId";
    const string SecretPref = "O-Manager.SentMaps.Secret";
    const string EnvironmentPref = "O-Manager.SentMaps.EnvironmentId";

    string keyId, secret, environmentId, projectId;
    string status = "";
    bool busy;
    Vector2 scroll;
    readonly List<Found> found = new List<Found>();

    class Found
    {
        public string player, id, source, picture;
        public MapPackageInfo info;
        public bool imported;
    }

    static string DownloadRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SentInMaps"));

    [MenuItem("Tools/O-Manager/Download sent-in maps...")]
    static void Open()
    {
        var window = GetWindow<SentMapsDownloader>(true, "Download sent-in maps");
        window.minSize = new Vector2(560, 420);
    }

    void OnEnable()
    {
        keyId = EditorPrefs.GetString(KeyIdPref, "");
        secret = EditorPrefs.GetString(SecretPref, "");
        environmentId = EditorPrefs.GetString(EnvironmentPref, "");
        projectId = CloudProjectSettings.projectId;
        ScanFolder();   // maps downloaded earlier
    }

    // ---- Automatic check: once each time Unity starts, when the key is saved, look for maps sent in since last time ----

    const string CheckedThisSession = "O-Manager.SentMaps.Checked";
    const string SeenPrefix = "O-Manager.SentMaps.Seen.";

    class Remote
    {
        public string player, prefix;                 // prefix = "map-<id>-"
        public readonly List<string> keys = new List<string>();
        public string stamp = "";                     // the files' modified dates: changes when the map is sent again
        public string SeenKey => SeenPrefix + player + "/" + prefix;
        public string SourceFile => keys.Find(k => k.StartsWith(prefix + "source"));
    }

    [InitializeOnLoadMethod]
    static void CheckOnStart()
    {
        if (Application.isBatchMode || SessionState.GetBool(CheckedThisSession, false)) return;
        SessionState.SetBool(CheckedThisSession, true);
        if (string.IsNullOrEmpty(EditorPrefs.GetString(SecretPref, "")) || string.IsNullOrEmpty(EditorPrefs.GetString(EnvironmentPref, ""))) return;
        EditorApplication.delayCall += () => _ = CheckForNew(false);
    }

    [MenuItem("Tools/O-Manager/Check for new sent-in maps")]
    static void CheckNow() => _ = CheckForNew(true);

    static async Task CheckForNew(bool askedFor)
    {
        try
        {
            string projectId = CloudProjectSettings.projectId;
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(EditorPrefs.GetString(SecretPref, "")))
            {
                if (askedFor) Open();
                return;
            }
            List<Remote> fresh;
            using (HttpClient http = Client(EditorPrefs.GetString(KeyIdPref, ""), EditorPrefs.GetString(SecretPref, "")))
            {
                string root = Root(projectId, EditorPrefs.GetString(EnvironmentPref, ""));
                fresh = (await ListMaps(http, root)).FindAll(m => m.SourceFile != null && EditorPrefs.GetString(m.SeenKey, "") != m.stamp);
                if (fresh.Count == 0)
                {
                    if (askedFor) EditorUtility.DisplayDialog("Sent-in maps", "No new maps have been sent in.", "OK");
                    return;
                }
                foreach (Remote m in fresh) await DownloadMap(http, root, m);
            }

            var lines = new List<string>();
            foreach (Remote m in fresh)
            {
                MapPackageInfo i = ReadInfo(m);
                lines.Add(i == null ? "- " + m.prefix : "- " + i.name + " (" + i.mapType + "), sent by " + Or(i.sender) + ", " + Or(i.ownerClub) + ", " + Or(i.contact) +
                    (i.permissionConfirmed ? "" : "  PERMISSION NOT CONFIRMED"));
            }
            int choice = EditorUtility.DisplayDialogComplex("New maps sent in",
                fresh.Count + (fresh.Count == 1 ? " new map has" : " new maps have") + " been sent in:\n\n" + string.Join("\n", lines) +
                "\n\nImport them into the game now? (Check that the club allows it before you release the game.)",
                "Import now", "Later", "Ignore these");
            if (choice == 1) return;   // asked again next time Unity starts
            var results = new List<string>();
            foreach (Remote m in fresh)
            {
                if (choice == 0) results.Add(MapImportMenu.ImportSent(Path.Combine(DownloadRoot, m.player, m.SourceFile)));
                EditorPrefs.SetString(m.SeenKey, m.stamp);
            }
            if (choice == 0) EditorUtility.DisplayDialog("New maps sent in", string.Join("\n\n", results), "OK");
        }
        catch (Exception e)
        {
            Debug.LogWarning("Sent-in maps: could not check for new maps: " + e.Message);
            if (askedFor) EditorUtility.DisplayDialog("Sent-in maps", "Could not check: " + e.Message, "OK");
        }
    }

    static string Or(string s) => string.IsNullOrEmpty(s) ? "?" : s;

    static MapPackageInfo ReadInfo(Remote m)
    {
        string path = Path.Combine(DownloadRoot, m.player, m.prefix + "info.json");
        try { return File.Exists(path) ? JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    static HttpClient Client(string keyId, string secret)
    {
        var http = new HttpClient();
        string auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(keyId.Trim() + ":" + secret.Trim()));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        return http;
    }

    static string Root(string projectId, string environmentId) => Api + projectId + "/environments/" + environmentId.Trim() + "/players";

    // Every map in Cloud Save, grouped by player and map id
    static async Task<List<Remote>> ListMaps(HttpClient http, string root)
    {
        var maps = new List<Remote>();
        foreach (JObject p in await Pages(http, root, "id"))
        {
            string player = (string)p["id"];
            foreach (JObject f in await Pages(http, root + "/" + player + "/items", "key"))
            {
                string key = (string)f["key"];
                if (!key.StartsWith("map-")) continue;
                int at = Math.Max(key.LastIndexOf("-source", StringComparison.Ordinal),
                         Math.Max(key.LastIndexOf("-info.json", StringComparison.Ordinal), key.LastIndexOf("-picture.png", StringComparison.Ordinal)));
                if (at <= 0) continue;
                string prefix = key.Substring(0, at + 1);
                Remote map = maps.Find(m => m.player == player && m.prefix == prefix);
                if (map == null) maps.Add(map = new Remote { player = player, prefix = prefix });
                map.keys.Add(key);
                map.stamp += (string)f["modified"]?["date"] + ";";
            }
        }
        return maps;
    }

    static async Task DownloadMap(HttpClient http, string root, Remote map)
    {
        string folder = Path.Combine(DownloadRoot, map.player);
        Directory.CreateDirectory(folder);
        foreach (string key in map.keys)
        {
            JObject detail = JObject.Parse(await Get(http, root + "/" + map.player + "/items/" + Uri.EscapeDataString(key)));
            File.WriteAllBytes(Path.Combine(folder, key), await Download(detail));
        }
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("Uses a service account with the role Cloud Save Viewer (Unity Cloud dashboard > Administration > " +
            "Service accounts). The key is saved on this computer only.\nEnvironment ID: dashboard > Environments, the ID of 'production'.", MessageType.Info);
        EditorGUILayout.LabelField("Project ID", string.IsNullOrEmpty(projectId) ? "(project not linked to Unity Cloud)" : projectId);
        keyId = EditorGUILayout.TextField("Key ID", keyId);
        secret = EditorGUILayout.PasswordField("Secret key", secret);
        environmentId = EditorGUILayout.TextField("Environment ID", environmentId);

        using (new EditorGUI.DisabledScope(busy || string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(keyId) ||
                                           string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(environmentId)))
        {
            if (GUILayout.Button("Download all sent-in maps", GUILayout.Height(30)))
            {
                EditorPrefs.SetString(KeyIdPref, keyId.Trim());
                EditorPrefs.SetString(SecretPref, secret.Trim());
                EditorPrefs.SetString(EnvironmentPref, environmentId.Trim());
                _ = DownloadAll();
            }
        }
        EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);

        if (found.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Maps in " + DownloadRoot, EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Found map in found)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    MapPackageInfo i = map.info;
                    EditorGUILayout.LabelField(i != null ? i.name + "  (" + i.mapType + ")" : map.id, EditorStyles.boldLabel);
                    if (i != null)
                    {
                        EditorGUILayout.LabelField("Sent by", string.IsNullOrEmpty(i.sender) ? "(not given)" : i.sender);
                        EditorGUILayout.LabelField("Club", string.IsNullOrEmpty(i.ownerClub) ? "(not given)" : i.ownerClub);
                        EditorGUILayout.LabelField("Contact", string.IsNullOrEmpty(i.contact) ? "(not given)" : i.contact);
                        EditorGUILayout.LabelField("Permission", i.permissionConfirmed ? "Confirmed by the sender" : "NOT confirmed");
                    }
                    EditorGUILayout.LabelField("Player", map.player);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(busy || map.source == null))
                        {
                            if (GUILayout.Button(map.imported ? "Imported (again?)" : "Import into the game"))
                            {
                                string result = MapImportMenu.ImportSent(map.source);
                                map.imported = !result.StartsWith("Failed");
                                status = result;
                            }
                        }
                        if (GUILayout.Button("Show folder")) EditorUtility.RevealInFinder(map.source ?? DownloadRoot);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    async Task DownloadAll()
    {
        busy = true;
        found.Clear();
        try
        {
            using (HttpClient http = Client(keyId, secret))
            {
                string root = Root(projectId, environmentId);
                SetStatus("Finding the maps players have sent in...");
                List<Remote> maps = await ListMaps(http, root);
                foreach (Remote m in maps)
                {
                    SetStatus("Downloading " + m.prefix + "...");
                    await DownloadMap(http, root, m);
                    EditorPrefs.SetString(m.SeenKey, m.stamp);   // not offered again when Unity starts
                }
                ScanFolder();
                SetStatus($"Downloaded {maps.Count} maps.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Download sent-in maps: " + e);
            SetStatus("Failed: " + e.Message + (e.Message.Contains("401") || e.Message.Contains("403")
                ? "\nCheck the key id and secret, the environment ID and that the service account has the role Cloud Save Viewer on this project." : ""));
        }
        finally
        {
            busy = false;
            Repaint();
        }
    }

    // Every result of a paged list (20 at a time)
    static async Task<List<JObject>> Pages(HttpClient http, string url, string idField)
    {
        var all = new List<JObject>();
        string after = null;
        while (true)
        {
            string page = await Get(http, url + "?pageSize=20" + (after == null ? "" : "&after=" + Uri.EscapeDataString(after)));
            var results = (JArray)JObject.Parse(page)["results"];
            if (results == null || results.Count == 0) break;
            foreach (JObject r in results) all.Add(r);
            if (results.Count < 20) break;
            after = (string)results[results.Count - 1][idField];
        }
        return all;
    }

    static async Task<string> Get(HttpClient http, string url)
    {
        using (HttpResponseMessage response = await http.GetAsync(url))
        {
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception((int)response.StatusCode + " " + response.ReasonPhrase + ": " + body);
            return body;
        }
    }

    // The file itself, from the signed URL (without the service account's key)
    static async Task<byte[]> Download(JObject detail)
    {
        using (var plain = new HttpClient())
        using (var request = new HttpRequestMessage(new HttpMethod((string)detail["httpMethod"] ?? "GET"), (string)detail["signedUrl"]))
        {
            if (detail["requiredHeaders"] is JObject headers)
            {
                foreach (var h in headers)
                {
                    string value = h.Value is JArray list ? string.Join(",", list) : (string)h.Value;
                    request.Headers.TryAddWithoutValidation(h.Key, value);
                }
            }
            using (HttpResponseMessage response = await plain.SendAsync(request))
            {
                if (!response.IsSuccessStatusCode) throw new Exception("Download failed: " + (int)response.StatusCode + " " + response.ReasonPhrase);
                return await response.Content.ReadAsByteArrayAsync();
            }
        }
    }

    void ScanFolder()
    {
        found.Clear();
        if (!Directory.Exists(DownloadRoot)) return;
        foreach (string folder in Directory.GetDirectories(DownloadRoot))
        {
            foreach (string source in Directory.GetFiles(folder, "map-*-source.*"))
            {
                string name = Path.GetFileName(source);
                string prefix = name.Substring(0, name.LastIndexOf("source", StringComparison.Ordinal));
                var map = new Found { player = Path.GetFileName(folder), id = prefix.Substring(4).TrimEnd('-'), source = source };
                string infoPath = Path.Combine(folder, prefix + "info.json");
                if (File.Exists(infoPath))
                {
                    try { map.info = JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(infoPath)); }
                    catch (Exception e) { Debug.LogWarning("Download sent-in maps: could not read " + infoPath + ": " + e.Message); }
                }
                found.Add(map);
            }
        }
    }

    void SetStatus(string text)
    {
        status = text;
        Repaint();
    }
}
