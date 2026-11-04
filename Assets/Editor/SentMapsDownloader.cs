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
            using (var http = new HttpClient())
            {
                string auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(keyId.Trim() + ":" + secret.Trim()));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
                string root = Api + projectId + "/environments/" + environmentId.Trim() + "/players";

                SetStatus("Finding players who have sent files...");
                var players = new List<string>();
                foreach (JObject p in await Pages(http, root, "id")) players.Add((string)p["id"]);

                int files = 0;
                foreach (string player in players)
                {
                    foreach (JObject f in await Pages(http, root + "/" + player + "/items", "key"))
                    {
                        string key = (string)f["key"];
                        if (!key.StartsWith("map-")) continue;
                        SetStatus("Downloading " + key + "...");
                        JObject detail = JObject.Parse(await Get(http, root + "/" + player + "/items/" + Uri.EscapeDataString(key)));
                        byte[] bytes = await Download(detail);
                        string folder = Path.Combine(DownloadRoot, player);
                        Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder, key), bytes);
                        files++;
                    }
                }
                ScanFolder();
                SetStatus($"Downloaded {files} files from {players.Count} players: {found.Count} maps.");
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
