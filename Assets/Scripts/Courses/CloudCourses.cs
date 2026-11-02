using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using Unity.Services.Core;
using UnityEngine;
using PlayerData = Unity.Services.CloudSave.Models.Data.Player;

// Publishes courses with Unity Cloud Save so other players can run them.
//
// Every player signs in anonymously. A player's published courses are stored as Public player data
// (anyone can read it, only the owner can write it) under two keys:
//   "courses"        the courses as JSON
//   "coursesUpdated" when they were last changed (unix seconds)
// The lobby lists everyone's courses with a query on "coursesUpdated", which needs a Public player-data
// index on that key in the Unity Cloud dashboard (Cloud Save > Indexes).
public static class CloudCourses
{
    const string CoursesKey = "courses";
    const string UpdatedKey = "coursesUpdated";
    // Unity Cloud Save allows 5 MiB of Public data per player. A course is about 1 kB, so this is thousands of courses.
    public const int MaxPublicBytes = 5 * 1024 * 1024;
    const int SafetyMargin = 16 * 1024;

    const int MaxPlayersPerFetch = 100;   // the newest 100 players who published something
    const int MaxCommunityCourses = 100;  // and at most 100 courses from them in the lobby

    static Task signInTask;

    public static string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : "";

    public static bool IsSignedIn =>
        UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

    public static Task SignInAsync()
    {
        // Also start again when an earlier sign-in finished but the services were reset since
        // (the Editor keeps static fields between Play sessions when domain reload is off)
        if (signInTask == null || signInTask.IsFaulted || signInTask.IsCanceled || (signInTask.IsCompleted && !IsSignedIn))
        {
            signInTask = SignIn();
        }
        return signInTask;
    }

    static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            InitializationOptions options = new InitializationOptions();
            if (Application.isEditor)
            {
                // ParrelSync clones on the same computer would otherwise share one anonymous player (same idea as Money.Key)
                options.SetProfile("editor" + (Animator.StringToHash(Application.dataPath) & 0x7fffffff));
            }
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        Debug.Log("CloudCourses: signed in as " + AuthenticationService.Instance.PlayerId);
    }

    // Replaces this player's published courses with the given list
    public static async Task PublishAsync(List<CourseData> myCourses)
    {
        string json = JsonConvert.SerializeObject(myCourses);
        if (!FitsInCloud(json))
        {
            throw new InvalidOperationException("Your courses are over Unity Cloud's 5 MB limit. Delete some courses first.");
        }
        await SignInAsync();
        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { CoursesKey, json },
            { UpdatedKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
        };
        await CloudSaveService.Instance.Data.Player.SaveAsync(data, new PlayerData.SaveOptions(new PlayerData.PublicWriteAccessClassOptions()));
    }

    // Every course other players have published, newest first
    public static async Task<List<CourseData>> FetchCommunityAsync()
    {
        await SignInAsync();
        Query query = new Query(
            new List<FieldFilter> { new FieldFilter(UpdatedKey, 0, FieldFilter.OpOptions.GT, false) },
            new HashSet<string> { CoursesKey },
            0,
            MaxPlayersPerFetch);
        List<EntityData> results = await CloudSaveService.Instance.Data.Player.QueryAsync(query, new PlayerData.QueryOptions());

        string me = PlayerId;
        List<CourseData> courses = new List<CourseData>();
        foreach (EntityData entity in results)
        {
            if (entity.Id == me || entity.Data == null)
            {
                continue;
            }
            foreach (Item item in entity.Data)
            {
                if (item.Key != CoursesKey)
                {
                    continue;
                }
                try
                {
                    List<CourseData> theirs = JsonConvert.DeserializeObject<List<CourseData>>(item.Value.GetAs<string>());
                    if (theirs == null) continue;
                    foreach (CourseData course in theirs)
                    {
                        if (course == null || course.controls == null) continue;
                        course.builtIn = false;
                        course.authorId = entity.Id;
                        course.Normalize();
                        courses.Add(course);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("CloudCourses: skipped unreadable courses from " + entity.Id + ": " + e.Message);
                }
            }
        }
        courses.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));
        if (courses.Count > MaxCommunityCourses) courses.RemoveRange(MaxCommunityCourses, courses.Count - MaxCommunityCourses);
        return courses;
    }

    // How much of the 5 MiB a list of courses takes when published
    public static int PublishedBytes(List<CourseData> courses)
    {
        return System.Text.Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(courses));
    }

    static bool FitsInCloud(string json)
    {
        return System.Text.Encoding.UTF8.GetByteCount(json) <= MaxPublicBytes - SafetyMargin;
    }

    public static bool FitsInCloud(List<CourseData> courses)
    {
        return PublishedBytes(courses) <= MaxPublicBytes - SafetyMargin;
    }

    // A short explanation for the player when something went wrong
    public static string Describe(Exception e)
    {
        Exception inner = e is AggregateException aggregate && aggregate.InnerException != null ? aggregate.InnerException : e;
        string message = inner.Message ?? "";
        if (inner is InvalidOperationException)
        {
            return message;
        }
        if (message.IndexOf("index", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "The course list is not set up in Unity Cloud yet (missing index).";
        }
        if (inner is RequestFailedException || inner is ServicesInitializationException)
        {
            return "Could not reach Unity Cloud. Check the internet connection.";
        }
        return message;
    }
}
