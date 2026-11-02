using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

// Where courses come from: the ones shipped with the game (Resources/Courses), the ones this player has made
// (saved on this computer and published to the cloud) and the ones other players have published.
public static class CourseLibrary
{
    static List<CourseData> builtIn;
    static List<CourseData> community = new List<CourseData>();

    // The course the next competition is run on. The host picks it in the lobby and the other players
    // receive it from the host; the competition scene reads it when it loads.
    public static CourseData Current { get; set; }

    public static CourseData CurrentOrDefault
    {
        get
        {
            if (Current == null)
            {
                Current = BuiltIn.Count > 0 ? BuiltIn[0] : null;
            }
            return Current;
        }
    }

    static string lastLobbyString;

    // A player who is not the host takes the course the host has published in the lobby
    public static void ApplyLobbyString(string text)
    {
        if (string.IsNullOrEmpty(text) || text == lastLobbyString)
        {
            return;
        }
        CourseData course = CourseData.FromLobbyString(text);
        if (course != null)
        {
            lastLobbyString = text;
            Current = course;
        }
    }

    // Reads the host's course straight from the host's player object. Used when the competition scene loads,
    // in case the host changed course just before starting.
    public static void ApplyFromHost()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.Runner == null || local.Runner.IsSharedModeMasterClient)
        {
            return;
        }
        foreach (NetworkPlayer player in UnityEngine.Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player != local && player.Object != null && player.Object.IsValid && player.lobbyCourse.Length > 0)
            {
                ApplyLobbyString(player.LobbyCourseText());
                return;
            }
        }
    }

    // ---- Shipped with the game ----

    public static List<CourseData> BuiltIn
    {
        get
        {
            if (builtIn == null)
            {
                builtIn = new List<CourseData>();
                foreach (TextAsset asset in Resources.LoadAll<TextAsset>("Courses"))
                {
                    CourseData course = Parse(asset.text);
                    if (course != null)
                    {
                        course.builtIn = true;
                        builtIn.Add(course);
                    }
                }
                builtIn.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            }
            return builtIn;
        }
    }

    public static CourseData FindBuiltIn(string id)
    {
        foreach (CourseData course in BuiltIn)
        {
            if (course.id == id) return course;
        }
        return null;
    }

    // ---- Made by this player ----

    static string Folder => Path.Combine(Application.persistentDataPath, "Courses");

    public static List<CourseData> Mine()
    {
        List<CourseData> courses = new List<CourseData>();
        if (!Directory.Exists(Folder))
        {
            return courses;
        }
        foreach (string file in Directory.GetFiles(Folder, "*.json"))
        {
            try
            {
                CourseData course = Parse(File.ReadAllText(file));
                if (course != null) courses.Add(course);
            }
            catch (Exception e)
            {
                Debug.LogWarning("CourseLibrary: could not read " + file + ": " + e.Message);
            }
        }
        courses.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));
        return courses;
    }

    public static void SaveMine(CourseData course)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, course.id + ".json"), JsonConvert.SerializeObject(course, Formatting.Indented));
    }

    public static void DeleteMine(string id)
    {
        string file = Path.Combine(Folder, id + ".json");
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    // ---- Published by other players ----

    public static List<CourseData> Community => community;

    public static void SetCommunity(List<CourseData> courses)
    {
        community = courses ?? new List<CourseData>();
    }

    // ---- JSON ----

    public static CourseData Parse(string json)
    {
        try
        {
            CourseData course = JsonConvert.DeserializeObject<CourseData>(json);
            if (course == null || course.controls == null) return null;
            course.Normalize();
            return course;
        }
        catch (Exception e)
        {
            Debug.LogWarning("CourseLibrary: could not read course: " + e.Message);
            return null;
        }
    }

    public static string ToJson(CourseData course)
    {
        return JsonConvert.SerializeObject(course, Formatting.Indented);
    }
}
