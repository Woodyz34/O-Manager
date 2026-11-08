using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MapImport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using UnityEngine;
using UnityEngine.Networking;
using PlayerData = Unity.Services.CloudSave.Models.Data.Player;

// Maps players have imported, shared with everyone through Unity Cloud until they are built into the game.
//
// When a player imports a map, the map file is already sent in to Cloud Save Player Files (CloudMaps). Those files
// are private, so sharing adds two things:
//  - the player's Public data key "sharedMaps": the maps they share (name, type, contour interval, map edge, and the
//    keys of their files). No contact details: those stay in the private map-<id>-info.json.
//    "coursesUpdated" is set too, so the lobby's course query (CloudCourses) finds the player and returns this key.
//  - the Cloud Code script SharedMapFiles (Docs/cloud-code/SharedMapFiles.js, pasted into the Unity Cloud dashboard),
//    which hands out short-lived download links to the map-<id>-source / map-<id>-picture.png files of a player.
// Other computers download the map file and build the map from it themselves (MapImportPanel.BuildPackage), into
// <persistent data>/Maps/<id>/ with map.json "sharedBy" set, so it works like a map imported there.
//
// When the map has been built into the game (a MapDefinition in Resources with the same id), the downloaded copies
// are deleted, and the sharing player's game takes the map out of "sharedMaps" and deletes its files in the cloud.
// The game's owner's Unity also deletes the files when a sent-in map is imported (SentMapsDownloader).
public static class SharedMaps
{
    public const string SharedKey = "sharedMaps";
    const string ScriptName = "SharedMapFiles";
    const string HaveSharedPref = "SharedMaps.HaveShared";   // this player has shared a map (so there may be something to tidy)

    [Serializable]
    public class SharedMap
    {
        public string id;
        public string name;
        public string mapType;
        public string author;
        public string sourceKey;          // map-<id>-source.<ext>
        public string pictureKey;         // map-<id>-picture.png, or null when the game draws the picture
        public long sourceBytes;
        public double contourInterval;
        public double[] boundary;         // paper mm, or null for the whole map
        public long sharedAt;             // unix seconds
        [JsonIgnore] public string ownerId;
    }

    // Raised on the main thread when a shared map has been downloaded (or a copy removed): lists of maps and courses
    // should be shown again
    public static event Action Changed;

    public static bool Busy { get; private set; }
    public static string Downloading { get; private set; }   // name of the map being downloaded, or null

    static readonly List<SharedMap> others = new List<SharedMap>();
    static readonly HashSet<string> failed = new HashSet<string>();   // ownerId/id tried this session without luck
    static bool checkedThisSession;

    // ---- Sharing (the player who imported the map) ----

    public static async Task ShareAsync(MapPackageInfo info, string sourceKey, string pictureKey, long sourceBytes)
    {
        List<SharedMap> mine = await LoadMineAsync();
        mine.RemoveAll(m => m.id == info.id);
        mine.Add(new SharedMap
        {
            id = info.id,
            name = info.name,
            mapType = info.mapType,
            author = info.author,
            sourceKey = sourceKey,
            pictureKey = pictureKey,
            sourceBytes = sourceBytes,
            contourInterval = info.contourInterval,
            boundary = info.boundary != null && info.boundary.Length >= 6 ? info.boundary : null,
            sharedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        await SaveMineAsync(mine);
        PlayerPrefs.SetInt(HaveSharedPref, 1);
        Debug.Log("SharedMaps: sharing " + info.name + " (" + info.id + ") with everyone");
    }

    // Takes a map out of this player's shared maps (it was made private)
    public static async Task UnshareAsync(string id)
    {
        List<SharedMap> mine = await LoadMineAsync();
        if (mine.RemoveAll(m => m.id == id) > 0) await SaveMineAsync(mine);
    }

    static async Task<List<SharedMap>> LoadMineAsync()
    {
        var loaded = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { SharedKey },
            new PlayerData.LoadOptions(new PlayerData.PublicReadAccessClassOptions()));
        if (loaded.TryGetValue(SharedKey, out Item item))
        {
            try
            {
                return JsonConvert.DeserializeObject<List<SharedMap>>(item.Value.GetAs<string>()) ?? new List<SharedMap>();
            }
            catch (Exception e)
            {
                Debug.LogWarning("SharedMaps: could not read my shared maps: " + e.Message);
            }
        }
        return new List<SharedMap>();
    }

    static async Task SaveMineAsync(List<SharedMap> mine)
    {
        var data = new Dictionary<string, object>
        {
            { SharedKey, JsonConvert.SerializeObject(mine) },
            { "coursesUpdated", DateTimeOffset.UtcNow.ToUnixTimeSeconds() }   // so the course query finds this player
        };
        await CloudSaveService.Instance.Data.Player.SaveAsync(data, new PlayerData.SaveOptions(new PlayerData.PublicWriteAccessClassOptions()));
    }

    // A map built into the game is no longer shared: built-in maps are the non-local ones in MapDefinition.All
    static bool IsBuiltIn(string id) => MapDefinition.All.Exists(m => m.id == id && !m.isLocal);

    // ---- Listing (called by CloudCourses with every player the course query returns) ----

    // Called with the "sharedMaps" value of each player. Afterwards Sync downloads what is missing.
    public static void BeginListing()
    {
        others.Clear();
        listedOwners.Clear();
    }

    // Players whose shared maps were listed: a downloaded copy of theirs that they no longer share was made
    // private (or deleted) and is deleted here too
    static readonly HashSet<string> listedOwners = new HashSet<string>();

    public static void AddListed(string ownerId, string json)
    {
        try
        {
            List<SharedMap> list = JsonConvert.DeserializeObject<List<SharedMap>>(json);
            if (list == null) return;
            listedOwners.Add(ownerId);
            foreach (SharedMap m in list)
            {
                if (m == null || !ValidId(m.id) || m.sourceKey == null || !m.sourceKey.StartsWith("map-" + m.id + "-source.", StringComparison.Ordinal)) continue;
                m.ownerId = ownerId;
                others.Add(m);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("SharedMaps: skipped unreadable shared maps from " + ownerId + ": " + e.Message);
        }
    }

    static bool ValidId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 48) return false;
        foreach (char c in id)
            if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-')) return false;
        return true;
    }

    // Once per game start: look for shared maps (the course query) and download them. The lobby's course list does
    // the same query when it opens, and that also starts a Sync.
    public static async void CheckOnce()
    {
        if (checkedThisSession) return;
        checkedThisSession = true;
        try
        {
            CourseLibrary.SetCommunity(await CloudCourses.FetchCommunityAsync());
        }
        catch (Exception e)
        {
            checkedThisSession = false;   // try again next time the main menu opens
            Debug.LogWarning("SharedMaps: could not look for shared maps: " + e.Message);
        }
    }

    // ---- Downloading (everyone else) ----

    // Downloads and builds the shared maps this computer lacks, one at a time. Runs on the main thread (the heavy
    // work is on background threads). Never throws.
    public static async void Sync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            RemoveBuiltInCopies();
            await CleanUpMineAsync();
            string me = CloudCourses.PlayerId;
            foreach (SharedMap m in new List<SharedMap>(others))
            {
                if (m.ownerId == me || IsBuiltIn(m.id) || failed.Contains(m.ownerId + "/" + m.id)) continue;
                MapPackageInfo local = ReadLocal(m.id);
                if (local != null)
                {
                    // Already here, or a map with the same id imported on this computer (that one is kept)
                    if (local.sharedBy != m.ownerId || local.sharedAt >= m.sharedAt) continue;
                }
                try
                {
                    Downloading = m.name;
                    Changed?.Invoke();
                    await DownloadAndBuild(m);
                    UIToast.Show("The map " + m.name + " was downloaded: a player shared it.", 5f);
                }
                catch (Exception e)
                {
                    failed.Add(m.ownerId + "/" + m.id);
                    Debug.LogWarning("SharedMaps: could not get the shared map " + m.name + " (" + m.id + "): " + e);
                }
                finally
                {
                    Downloading = null;
                    MapDefinition.Refresh();
                    Changed?.Invoke();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("SharedMaps: " + e);
        }
        finally
        {
            Busy = false;
        }
    }

    static MapPackageInfo ReadLocal(string id)
    {
        string json = Path.Combine(MapPackages.LocalRoot, id, "map.json");
        if (!File.Exists(json)) return null;
        try { return JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(json)); }
        catch { return null; }
    }

    static async Task DownloadAndBuild(SharedMap m)
    {
        Debug.Log($"SharedMaps: downloading {m.name} ({m.id}, {m.sourceBytes / 1024} kB) shared by {m.ownerId}");
        var keys = new List<string> { m.sourceKey };
        if (!string.IsNullOrEmpty(m.pictureKey)) keys.Add(m.pictureKey);
        JObject links = await FileLinks(m.ownerId, keys);
        byte[] source = await Download(links, m.sourceKey);
        byte[] ownPng = string.IsNullOrEmpty(m.pictureKey) ? null : await Download(links, m.pictureKey);

        string target = Path.Combine(MapPackages.LocalRoot, m.id);
        string building = target + ".download";
        string sourceName = m.sourceKey.Substring(("map-" + m.id + "-").Length);
        await Task.Run(() =>
        {
            MapFile file = MapPackageBuilder.ReadMapFile(source);
            if (m.contourInterval > 0) file.contourInterval = m.contourInterval;
            if (m.boundary != null && m.boundary.Length >= 6) file.boundary = m.boundary;
            string type = m.mapType == "sprint" || m.mapType == "both" ? m.mapType : "forest";
            MapPackage package = MapImportPanel.BuildPackage(file, m.name, type, ownPng, null);
            package.info.id = m.id;
            package.info.sourceFile = sourceName;
            package.info.author = m.author;
            package.info.permissionConfirmed = true;   // confirmed by the player who shared it
            package.info.sharedBy = m.ownerId;
            package.info.sharedAt = m.sharedAt;
            // Built next to the real folder and moved in when done, so a half-built map is never listed
            if (Directory.Exists(building)) Directory.Delete(building, true);
            MapPackageBuilder.WriteFolder(building, package, ownPng);
            foreach (string line in package.log) Debug.Log("SharedMaps: " + line);
        });
        if (ownPng != null)
        {
            try { MapPackages.WriteThumbnail(building, ownPng); }
            catch (Exception e) { Debug.LogWarning("SharedMaps: no thumbnail: " + e.Message); }
        }
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.Move(building, target);
        Debug.Log("SharedMaps: " + m.name + " is ready in " + target);
    }

    // Deletes downloaded copies of maps that are now built into the game
    static void RemoveBuiltInCopies()
    {
        try
        {
            if (!Directory.Exists(MapPackages.LocalRoot)) return;
            bool removed = false;
            foreach (string folder in Directory.GetDirectories(MapPackages.LocalRoot))
            {
                string name = Path.GetFileName(folder);
                if (name.EndsWith(".download"))
                {
                    Directory.Delete(folder, true);   // left over from a download that was cut off
                    continue;
                }
                MapPackageInfo info = ReadLocal(name);
                if (info == null || string.IsNullOrEmpty(info.sharedBy)) continue;
                bool builtIn = IsBuiltIn(info.id);
                bool withdrawn = listedOwners.Contains(info.sharedBy) && !others.Exists(m => m.ownerId == info.sharedBy && m.id == info.id);
                if (!builtIn && !withdrawn) continue;
                Directory.Delete(folder, true);
                removed = true;
                Debug.Log("SharedMaps: " + info.name + (builtIn ? " is built into the game now" : " is no longer shared by the player who imported it") + "; the downloaded copy was deleted");
            }
            if (removed)
            {
                MapDefinition.Refresh();
                Changed?.Invoke();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("SharedMaps: could not tidy the downloaded maps: " + e.Message);
        }
    }

    // The sharing player's side: maps of mine that are built into the game now are taken out of the cloud
    static async Task CleanUpMineAsync()
    {
        if (!CloudCourses.IsSignedIn || PlayerPrefs.GetInt(HaveSharedPref, 0) == 0) return;
        List<SharedMap> mine = await LoadMineAsync();
        List<SharedMap> done = mine.FindAll(m => IsBuiltIn(m.id));
        if (done.Count == 0) return;
        List<FileItem> files = await CloudSaveService.Instance.Files.Player.ListAllAsync();
        foreach (SharedMap m in done)
        {
            string prefix = "map-" + m.id + "-";
            foreach (FileItem f in files)
            {
                if (!f.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                try { await CloudSaveService.Instance.Files.Player.DeleteAsync(f.Key); }
                catch (Exception e) { Debug.LogWarning("SharedMaps: could not delete " + f.Key + ": " + e.Message); }
            }
            Debug.Log("SharedMaps: " + m.name + " is built into the game now; it was taken out of the cloud");
        }
        mine.RemoveAll(m => done.Contains(m));
        await SaveMineAsync(mine);
    }

    // ---- Unity Cloud calls ----

    // Asks the Cloud Code script for download links to a player's map files: { "<key>": { signedUrl, httpMethod, requiredHeaders } }
    internal static async Task<JObject> FileLinks(string ownerId, List<string> keys)
    {
        string url = "https://cloud-code.services.api.unity.com/v1/projects/" + Application.cloudProjectId + "/scripts/" + ScriptName;
        string body = JsonConvert.SerializeObject(new { @params = new { ownerId, keys = string.Join(",", keys) } });
        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + AuthenticationService.Instance.AccessToken);
            await Send(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                // The script says no (for instance a private map this player has not been let in to yet)
                string said = ScriptError(request.downloadHandler.text);
                if (said != null) throw new Exception(said);
                string why = request.responseCode == 404
                    ? "the Cloud Code script " + ScriptName + " is not set up in Unity Cloud yet (see Docs/cloud-code/SharedMapFiles.js)"
                    : request.responseCode + " " + request.error + ": " + request.downloadHandler.text;
                throw new Exception("Could not get the download links: " + why);
            }
            JObject response = JObject.Parse(request.downloadHandler.text);
            if (!(response["output"] is JObject output) || !(output["files"] is JObject files))
                throw new Exception("Unexpected answer from " + ScriptName + ": " + request.downloadHandler.text);
            return files;
        }
    }

    // The message a Cloud Code script threw, or null
    static string ScriptError(string body)
    {
        try
        {
            JObject answer = JObject.Parse(body);
            string detail = (string)answer["detail"] ?? (string)answer["message"];
            if (detail != null && detail.Contains("Not let in")) return "Not let in yet";
        }
        catch { /* not JSON */ }
        return null;
    }

    internal static async Task<byte[]> Download(JObject links, string key, Action<float> progress = null)
    {
        if (!(links[key] is JObject link) || string.IsNullOrEmpty((string)link["signedUrl"]))
            throw new Exception("No download link for " + key + " (the file is not in the cloud any more?)");
        using (var request = new UnityWebRequest((string)link["signedUrl"], (string)link["httpMethod"] ?? "GET"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            if (link["requiredHeaders"] is JObject headers)
            {
                foreach (var h in headers)
                {
                    string value = h.Value is JArray list ? string.Join(",", list) : (string)h.Value;
                    if (!string.IsNullOrEmpty(value)) request.SetRequestHeader(h.Key, value);
                }
            }
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                progress?.Invoke(request.downloadProgress);
                await Task.Yield();
            }
            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception("Download of " + key + " failed: " + request.responseCode + " " + request.error);
            return request.downloadHandler.data;
        }
    }

    static async Task Send(UnityWebRequest request)
    {
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        while (!operation.isDone) await Task.Yield();
    }
}
