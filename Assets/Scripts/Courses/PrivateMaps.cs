using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MapImport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using UnityEngine;
using PlayerData = Unity.Services.CloudSave.Models.Data.Player;

// Private maps: maps a player imported as "Private" (for instance a club that does not want to hand its map out).
// Only that player can use them: they are never sent in or shared (SharedMaps), and the courses on them are hidden
// for everyone else. The owner can run them alone, draw route choices, analyse legs and so on as with any map.
//
// The one exception is a lobby race the owner hosts on the map. Then the other players in the lobby get a copy for
// the races in that lobby, which is deleted when they leave (back to the main menu, or when the game closes or
// starts again). How the copy gets to them:
//  - Owner (host): the map file (kept in the map's folder as source.<ext>) is uploaded once to the owner's own
//    Cloud Save Player Files as private-<id>-source.<ext> (and private-<id>-picture.png for an own picture). Nobody
//    else can read those files. The owner's Public data key "privateMaps" then lists the map with the Unity player
//    ids of the players in the lobby ("guests") and a time it runs out ("until").
//  - The Cloud Code script SharedMapFiles (Docs/cloud-code/SharedMapFiles.js) gives download links to private-*
//    files only to the players on that list, until it runs out.
//  - Each guest downloads the map file, builds the map like a shared map (MapImportPanel.BuildPackage) into
//    <persistent data>/Borrowed/ and uses it through MapDefinition.Borrow: it is never in the map lists.
// The host publishes its Unity player id on its player (NetworkPlayer.lobbyMapOwner) when the lobby course is on a
// private map of its own; each guest says when its copy is ready (privateMapReady) and the host waits for everyone
// before it starts the race.
public static class PrivateMaps
{
    public const string PublicKey = "privateMaps";
    const string KeyPrefix = "private-";
    const long GrantSeconds = 8 * 3600;          // how long the players in a lobby may download the map
    const long RenewBeforeSeconds = 2 * 3600;    // a host who stays long renews the grant in time

    [Serializable]
    public class Entry
    {
        public string id;
        public string name;
        public string mapType;
        public string author;
        public string sourceKey;          // private-<id>-source.<ext>
        public string pictureKey;         // private-<id>-picture.png, or null when the game draws the picture
        public long sourceBytes;
        public double contourInterval;
        public double[] boundary;         // paper mm, or null for the whole map
        public TerrainLook look;          // ground, trees and undergrowth for the 3D view, or null for the normal look
        public TerrainSpeeds terrain;     // the terrain sliders, or null for the normal speeds
        public long sharedAt;             // unix seconds, when the map file was uploaded
        public List<string> guests = new List<string>();
        public long until;                // unix seconds
    }

    static string BorrowedRoot => Path.Combine(Application.persistentDataPath, LocalPrefs.Key("Borrowed"));   // a ParrelSync clone has its own

    static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // A private map imported on this computer (not a borrowed copy)
    public static bool IsMine(MapDefinition map) => map != null && map.isPrivate && map.isLocal && !map.isBorrowed;

    // ---- Owner: hosting a lobby race on a private map ----

    static string hostedKey;          // map id + guests last saved
    static long hostedUntil;
    static bool hostBusy;
    static float hostRetryAt;

    // Shown to the host when something went wrong, or null
    public static string HostProblem { get; private set; }
    // The map is uploaded and the guests are let in
    public static bool HostReady { get; private set; }

    // Called by the lobby (LobbySettingsUI) a few times a second while this player hosts a course on its own private
    // map. guestIds: the Unity player ids of the other players. Uploads the map once and keeps the guest list current.
    public static void HostTick(MapDefinition map, List<string> guestIds)
    {
        guestIds.Sort(StringComparer.Ordinal);
        string key = map.id + "|" + string.Join(",", guestIds);
        if (key != hostedKey) HostReady = false;
        if (hostBusy || guestIds.Count == 0) return;
        if (key == hostedKey && Now < hostedUntil - RenewBeforeSeconds) return;
        if (Time.unscaledTime < hostRetryAt && key == hostedKey) return;
        HostAsync(map, guestIds, key);
    }

    // The owner changed the map (MapEditPanel): the lobby's entry is written again with the new details
    public static void MapChanged()
    {
        hostedKey = null;
    }

    static async void HostAsync(MapDefinition map, List<string> guestIds, string key)
    {
        hostBusy = true;
        hostedKey = key;
        try
        {
            await CloudCourses.SignInAsync();
            string folder = FolderOf(map);
            MapPackageInfo info = ReadInfo(folder);
            if (info == null) throw new Exception("The map's files are missing on this computer.");
            if (string.IsNullOrEmpty(info.privateSourceKey)) await UploadAsync(info, folder);
            long until = Now + GrantSeconds;
            var entry = new Entry
            {
                id = info.id,
                name = info.name,
                mapType = info.mapType,
                author = info.author,
                sourceKey = info.privateSourceKey,
                pictureKey = info.ownPicture ? KeyPrefix + info.id + "-picture.png" : null,
                sourceBytes = new FileInfo(SourceFile(folder, info)).Length,
                contourInterval = info.contourInterval,
                boundary = info.boundary != null && info.boundary.Length >= 6 ? info.boundary : null,
                terrain = info.terrain != null && !info.terrain.IsDefault() ? info.terrain.Copy() : null,
                look = info.look != null && !info.look.IsDefault() ? info.look.Safe() : null,
                sharedAt = info.sharedAt,
                guests = new List<string>(guestIds),
                until = until
            };
            // Only the map being hosted now is listed: guests of earlier lobbies are not let in any more
            var data = new Dictionary<string, object> { { PublicKey, JsonConvert.SerializeObject(new List<Entry> { entry }) } };
            await CloudSaveService.Instance.Data.Player.SaveAsync(data, new PlayerData.SaveOptions(new PlayerData.PublicWriteAccessClassOptions()));
            hostedUntil = until;
            HostProblem = null;
            HostReady = key == hostedKey;
            Debug.Log($"PrivateMaps: {info.name} ({info.id}) can be downloaded by {guestIds.Count} players in the lobby");
        }
        catch (Exception e)
        {
            hostedUntil = 0;
            hostRetryAt = Time.unscaledTime + 10f;
            HostProblem = "Could not hand your private map to the others: " + CloudCourses.Describe(e);
            Debug.LogWarning("PrivateMaps: " + e);
        }
        finally
        {
            hostBusy = false;
        }
    }

    static string FolderOf(MapDefinition map)
    {
        // gridName is "file:<folder>/grid" for maps imported on this computer
        string grid = map.gridName ?? "";
        if (!MapPackages.IsFile(grid)) return null;
        return Path.GetDirectoryName(grid.Substring(MapPackages.FilePrefix.Length));
    }

    static MapPackageInfo ReadInfo(string folder)
    {
        string json = folder != null ? Path.Combine(folder, "map.json") : null;
        if (json == null || !File.Exists(json)) return null;
        try { return JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(json)); }
        catch { return null; }
    }

    public static string SourceFile(string folder, MapPackageInfo info)
    {
        return Path.Combine(folder, "source" + Path.GetExtension(info.sourceFile ?? "").ToLowerInvariant());
    }

    // Uploads the map file (and own picture) to the owner's own Cloud Save Files, which only the owner can read
    static async Task UploadAsync(MapPackageInfo info, string folder)
    {
        string sourcePath = SourceFile(folder, info);
        if (!File.Exists(sourcePath)) throw new Exception("The map file is not kept with the map. Import the map again.");
        byte[] source = await Task.Run(() => File.ReadAllBytes(sourcePath));
        string sourceKey = KeyPrefix + info.id + "-source" + Path.GetExtension(sourcePath);
        await CloudSaveService.Instance.Files.Player.SaveAsync(sourceKey, source);
        if (info.ownPicture)
        {
            byte[] picture = await Task.Run(() => File.ReadAllBytes(Path.Combine(folder, "picture.png")));
            await CloudSaveService.Instance.Files.Player.SaveAsync(KeyPrefix + info.id + "-picture.png", picture);
        }
        info.privateSourceKey = sourceKey;
        info.sharedAt = Now;
        MapPackageBuilder.WriteInfo(folder, info);
        Debug.Log($"PrivateMaps: uploaded {info.name} ({source.Length / 1024} kB) as {sourceKey}, only for lobby races you host");
    }

    // ---- Guests: getting the host's private map ----

    static readonly Dictionary<string, MapDefinition> copies = new Dictionary<string, MapDefinition>();   // "owner/id"
    static string fetching;          // "owner/id" being fetched
    static float nextTry;
    static string triedKey;

    // What a guest sees while the map is on its way, or null
    public static string GuestStatus { get; private set; }
    public static int GuestProgress { get; private set; }

    // Called by the lobby a few times a second on the other players' computers while the host's course is on the
    // host's private map. Returns the map id once the copy is ready here, else "".
    public static string GuestTick(string ownerId, string mapId)
    {
        string key = ownerId + "/" + mapId;
        if (copies.TryGetValue(key, out MapDefinition ready))
        {
            MapDefinition.Borrow(ready);   // again, in case another copy with the same id was used in between
            GuestStatus = null;
            GuestProgress = 100;
            return mapId;
        }
        if (fetching != null) return "";
        if (triedKey != key)
        {
            triedKey = key;
            nextTry = 0f;
            GuestProgress = 0;
            GuestStatus = "Getting the host's private map ...";
        }
        if (Time.unscaledTime >= nextTry) FetchAsync(ownerId, mapId, key);
        return "";
    }

    static async void FetchAsync(string ownerId, string mapId, string key)
    {
        fetching = key;
        try
        {
            await CloudCourses.SignInAsync();
            Entry entry = await LoadEntry(ownerId, mapId);
            if (entry == null || !entry.guests.Contains(CloudCourses.PlayerId) || entry.until <= Now)
            {
                GuestStatus = "Waiting for the host to let you in to the private map ...";
                nextTry = Time.unscaledTime + 4f;
                return;
            }
            var keys = new List<string> { entry.sourceKey };
            if (!string.IsNullOrEmpty(entry.pictureKey)) keys.Add(entry.pictureKey);
            JObject links = await SharedMaps.FileLinks(ownerId, keys);
            float pictureShare = string.IsNullOrEmpty(entry.pictureKey) ? 0f : 0.3f;
            GuestStatus = "Downloading the host's private map ...";
            byte[] source = await SharedMaps.Download(links, entry.sourceKey, p => GuestProgress = Mathf.RoundToInt(p * (0.6f - pictureShare) * 100));
            byte[] ownPng = string.IsNullOrEmpty(entry.pictureKey) ? null
                : await SharedMaps.Download(links, entry.pictureKey, p => GuestProgress = Mathf.RoundToInt((0.6f - pictureShare + p * pictureShare) * 100));
            GuestStatus = "Building the host's private map ...";
            GuestProgress = 60;
            MapDefinition map = await Build(ownerId, entry, source, ownPng);
            copies[key] = map;
            MapDefinition.Borrow(map);
            GuestStatus = null;
            GuestProgress = 100;
            Debug.Log("PrivateMaps: got the host's private map " + entry.name + " (" + entry.id + ") for this lobby");
        }
        catch (Exception e)
        {
            bool notYet = e.Message.Contains("Not let in");
            GuestStatus = notYet ? "Waiting for the host to let you in to the private map ..."
                : "Could not get the host's private map (" + CloudCourses.Describe(e) + "). Trying again ...";
            nextTry = Time.unscaledTime + (notYet ? 4f : 10f);
            if (!notYet) Debug.LogWarning("PrivateMaps: " + e);
        }
        finally
        {
            fetching = null;
        }
    }

    static async Task<Entry> LoadEntry(string ownerId, string mapId)
    {
        var loaded = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { PublicKey },
            new PlayerData.LoadOptions(new PlayerData.PublicReadAccessClassOptions(ownerId)));
        if (!loaded.TryGetValue(PublicKey, out Item item)) return null;
        List<Entry> list = JsonConvert.DeserializeObject<List<Entry>>(item.Value.GetAs<string>());
        Entry entry = list?.Find(e => e != null && e.id == mapId);
        if (entry == null || entry.sourceKey == null || !entry.sourceKey.StartsWith(KeyPrefix + mapId + "-source.", StringComparison.Ordinal)) return null;
        if (entry.guests == null) entry.guests = new List<string>();
        return entry;
    }

    static async Task<MapDefinition> Build(string ownerId, Entry entry, byte[] source, byte[] ownPng)
    {
        string folder = Path.Combine(BorrowedRoot, Safe(ownerId) + "-" + Safe(entry.id));
        string sourceName = entry.sourceKey.Substring((KeyPrefix + entry.id + "-").Length);
        MapPackageInfo info = null;
        await Task.Run(() =>
        {
            MapFile file = MapPackageBuilder.ReadMapFile(source);
            if (entry.contourInterval > 0) file.contourInterval = entry.contourInterval;
            if (entry.boundary != null && entry.boundary.Length >= 6) file.boundary = entry.boundary;
            file.terrain = entry.terrain;
            file.look = entry.look;
            string type = entry.mapType == "sprint" || entry.mapType == "both" ? entry.mapType : "forest";
            MapPackage package = MapImportPanel.BuildPackage(file, entry.name, type, ownPng,
                (step, share) => GuestProgress = 60 + Mathf.RoundToInt(share * 39));
            package.info.id = entry.id;
            package.info.sourceFile = sourceName;
            package.info.author = entry.author;
            package.info.permissionConfirmed = true;
            package.info.isPrivate = true;
            package.info.sharedBy = ownerId;
            package.info.sharedAt = entry.sharedAt;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            MapPackageBuilder.WriteFolder(folder, package, ownPng);
            info = package.info;
        });
        if (ownPng != null)
        {
            try { MapPackages.WriteThumbnail(folder, ownPng); }
            catch (Exception e) { Debug.LogWarning("PrivateMaps: no thumbnail: " + e.Message); }
        }
        MapDefinition map = MapPackages.Definition(info, MapPackages.FilePrefix + folder.Replace('\\', '/') + "/");
        map.isLocal = true;
        map.isPrivate = true;
        map.isBorrowed = true;
        return map;
    }

    static string Safe(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
        return sb.ToString();
    }

    // ---- Lobby start ----

    // Why the host cannot start the race yet, or null. Only for a course on the host's own private map: everyone
    // else in the lobby has to have their copy first.
    public static string StartProblem(CourseData course)
    {
        MapDefinition map = course != null ? MapDefinition.FindExact(course.mapId) : null;
        if (!IsMine(map)) return OthersMissing(map);
        NetworkPlayer local = NetworkPlayer.Local;
        int waiting = 0, total = 0, progress = 0;
        foreach (NetworkPlayer player in NetworkPlayer.All())
        {
            if (player == local || player.Object == null || !player.Object.IsValid) continue;
            total++;
            if (player.privateMapReady.ToString() == map.id) continue;
            waiting++;
            progress += Mathf.Clamp(player.privateMapProgress, 0, 100);
        }
        if (waiting == 0) return null;
        if (HostProblem != null) return HostProblem;
        return waiting == 1 && total == 1
            ? $"Waiting for the other player to get your private map ({progress} %) ..."
            : $"Waiting for {waiting} of {total} players to get your private map ({progress / waiting} %) ...";
    }

    // A map that is not private (2026-11-19: a map the host had just imported, and the race started before the others
    // had it, so it broke for them): every player says when the map is on their computer (privateMapReady)
    static string OthersMissing(MapDefinition map)
    {
        if (map == null) return null;
        NetworkPlayer local = NetworkPlayer.Local;
        int waiting = 0, total = 0;
        foreach (NetworkPlayer player in NetworkPlayer.All())
        {
            if (player == local || player.Object == null || !player.Object.IsValid) continue;
            total++;
            if (player.privateMapReady.ToString() != map.id) waiting++;
        }
        if (waiting == 0) return null;
        return waiting == 1 && total == 1
            ? "Waiting for the other player to get the map " + map.displayName + " ..."
            : $"Waiting for {waiting} of {total} players to get the map {map.displayName} ...";
    }

    // ---- Throwing the copies away ----

    // When the player leaves the lobby (the main menu opens, not on a visit from the lobby), and when the game starts
    // or closes: the copies of other players' private maps are deleted
    public static void ForgetBorrowed()
    {
        copies.Clear();
        triedKey = null;
        GuestStatus = null;
        MapDefinition.ForgetBorrowed();
        try
        {
            if (Directory.Exists(BorrowedRoot)) Directory.Delete(BorrowedRoot, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning("PrivateMaps: could not delete the borrowed maps: " + e.Message);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void OnGameStart()
    {
        copies.Clear();
        fetching = null;
        triedKey = null;
        hostedKey = null;
        hostedUntil = 0;
        hostBusy = false;
        HostProblem = null;
        HostReady = false;
        ForgetBorrowed();
        Application.quitting -= ForgetBorrowed;
        Application.quitting += ForgetBorrowed;
    }
}
