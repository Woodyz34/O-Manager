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

    // No limit on how many courses one player publishes (user's wish 2026-11-12: every course made is welcome); only
    // Unity's own 5 MiB per player. The lobby reads the players page by page (Unity gives at most 100 per query).
    const int PlayersPerPage = 100;
    const int MaxPages = 20;                // the newest 2000 players who published something
    const int MaxCommunityCourses = 2000;   // a guard so the lobby list stays quick

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
        // Shared with the usage statistics (GameAnalytics), so the services start only once
        await GameAnalytics.EnsureServices();
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

    // Every course other players have published, newest first. includeMine: this player's published courses too (the
    // Monday relay picks among everyone's relays, also the ones of the player whose game picks them).
    public static async Task<List<CourseData>> FetchCommunityAsync(bool includeMine = false)
    {
        await SignInAsync();
        List<EntityData> results = new List<EntityData>();
        for (int page = 0; page < MaxPages; page++)
        {
            Query query = new Query(
                new List<FieldFilter> { new FieldFilter(UpdatedKey, 0, FieldFilter.OpOptions.GT, false) },
                new HashSet<string> { CoursesKey, SharedMaps.SharedKey },
                page * PlayersPerPage,
                PlayersPerPage);
            List<EntityData> got = await CloudSaveService.Instance.Data.Player.QueryAsync(query, new PlayerData.QueryOptions());
            if (got == null) break;
            results.AddRange(got);
            if (got.Count < PlayersPerPage) break;
        }

        string me = PlayerId;
        List<CourseData> courses = new List<CourseData>();
        SharedMaps.BeginListing();
        foreach (EntityData entity in results)
        {
            if ((entity.Id == me && !includeMine) || entity.Data == null)
            {
                continue;
            }
            foreach (Item item in entity.Data)
            {
                if (item.Key == SharedMaps.SharedKey)
                {
                    if (entity.Id == me) continue;   // my own maps are on this computer already
                    SharedMaps.AddListed(entity.Id, item.Value.GetAs<string>());
                    continue;
                }
                if (item.Key != CoursesKey)
                {
                    continue;
                }
                try
                {
                    List<CourseData> theirs = JsonConvert.DeserializeObject<List<CourseData>>(item.Value.GetAs<string>());
                    if (theirs == null) continue;
                    List<CourseData> usable = new List<CourseData>();
                    foreach (CourseData course in theirs)
                    {
                        if (course == null || course.controls == null) continue;
                        // One unreadable or oversized course must not take the player's other courses with it,
                        // and nothing another player wrote is trusted as it is (CourseData.MakeSafe)
                        try
                        {
                            course.builtIn = false;
                            course.authorId = entity.Id;
                            if (!course.MakeSafe()) continue;
                            course.Normalize();
                            usable.Add(course);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("CloudCourses: skipped a course from " + entity.Id + ": " + e.Message);
                        }
                    }
                    courses.AddRange(usable);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("CloudCourses: skipped unreadable courses from " + entity.Id + ": " + e.Message);
                }
            }
        }
        SharedMaps.Sync();   // downloads maps other players shared, in the background
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
