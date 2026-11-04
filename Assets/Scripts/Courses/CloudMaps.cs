using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MapImport;
using Newtonsoft.Json;
using Unity.Services.CloudSave;
using UnityEngine;

// Sends a map a player imported to the game's owner, so it can be built into the next version of the game.
//
// A map is far bigger than the 5 MB of player data Cloud Save allows (that is where the courses are), so the map file
// itself goes into the player's Cloud Save Files (1 GB per player). Only the player and the game's owner can read
// those: the owner downloads them in the Unity Cloud dashboard (Cloud Save > Player Files) and builds them in with
// Tools > O-Manager > Import map file. Every file is named map-<id>-...:
//   map-<id>-source.ocd / .omap / .xmap   the map file
//   map-<id>-picture.png                  the player's own picture, when they gave one
//   map-<id>-info.json                    MapPackageInfo, with the permission the player confirmed
// The player's (private) data key "mapSubmissions" lists what they have sent, newest last.
public static class CloudMaps
{
    const string SubmissionsKey = "mapSubmissions";

    [Serializable]
    public class Submission
    {
        public string id;
        public string name;
        public string mapType;
        public string sourceFile;
        public string author;
        public string sender;
        public string ownerClub;
        public string contact;
        public long sentAt;               // unix seconds
        public bool permissionConfirmed;
        public string permissionText;
    }

    public const string PermissionText =
        "I have permission from the club that owns this map to use it in O-Manager, and the club knows how the game uses it. " +
        "I am responsible for this, not the maker of the game.";

    public static async Task SubmitAsync(MapPackageInfo info, byte[] source, string sourceName, byte[] ownPng)
    {
        if (!info.permissionConfirmed) throw new InvalidOperationException("The club's permission has to be confirmed first.");
        await CloudCourses.SignInAsync();
        string prefix = "map-" + info.id + "-";
        string extension = System.IO.Path.GetExtension(sourceName).ToLowerInvariant();
        await CloudSaveService.Instance.Files.Player.SaveAsync(prefix + "source" + extension, source);
        if (ownPng != null)
        {
            await CloudSaveService.Instance.Files.Player.SaveAsync(prefix + "picture.png", ownPng);
        }
        await CloudSaveService.Instance.Files.Player.SaveAsync(prefix + "info.json", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(info, true)));

        var submission = new Submission
        {
            id = info.id,
            name = info.name,
            mapType = info.mapType,
            sourceFile = sourceName,
            author = info.author,
            sender = info.sender,
            ownerClub = info.ownerClub,
            contact = info.contact,
            sentAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            permissionConfirmed = true,
            permissionText = PermissionText
        };
        List<Submission> list = new List<Submission>();
        var loaded = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { SubmissionsKey });
        if (loaded.TryGetValue(SubmissionsKey, out var item))
        {
            try
            {
                list = JsonConvert.DeserializeObject<List<Submission>>(item.Value.GetAs<string>()) ?? list;
            }
            catch (Exception e)
            {
                Debug.LogWarning("CloudMaps: could not read the earlier submissions: " + e.Message);
            }
        }
        list.RemoveAll(s => s.id == submission.id);
        list.Add(submission);
        await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { SubmissionsKey, JsonConvert.SerializeObject(list) } });
        Debug.Log($"CloudMaps: sent {info.name} ({source.Length / 1024} kB) as {prefix}*");
    }
}
