using System;
using System.Threading.Tasks;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;

// Anonymous usage statistics with Unity Analytics (2026-11-19): how many play, when, and what they do.
// Shared start of Unity's online services lives here too (CloudCourses uses it), so the services start only once.
// Players can switch it off in the Esc menu ("Usage statistics"). Nothing personal is sent: no name, no e-mail.
// Unity adds the automatic events itself (new player, session start, game running = who is online when).
public static class GameAnalytics
{
    const string OptOutKey = "Analytics.OptOut";
    static Task servicesTask;
    static bool collecting;

    public static bool Enabled
    {
        get { return PlayerPrefs.GetInt(OptOutKey, 0) == 0; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        Begin();
    }

    // Starts Unity's services (once) and, unless the player opted out, the statistics
    public static Task EnsureServices()
    {
        if (servicesTask == null || servicesTask.IsFaulted || servicesTask.IsCanceled || (servicesTask.IsCompleted && UnityServices.State != ServicesInitializationState.Initialized))
        {
            servicesTask = Init();
        }
        return servicesTask;
    }

    static async Task Init()
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
    }

    static async void Begin()
    {
        try
        {
            await EnsureServices();
            ApplyConsent();
            Record("game_opened", "version", Application.version, "platform", Application.platform.ToString());
        }
        catch (Exception e)
        {
            // No internet or services down: the game goes on without statistics
            Debug.Log("GameAnalytics: not started: " + e.Message);
        }
    }

    static void ApplyConsent()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            return;
        }
        if (Enabled && !collecting)
        {
            AnalyticsService.Instance.StartDataCollection();
            collecting = true;
        }
        else if (!Enabled && collecting)
        {
            AnalyticsService.Instance.StopDataCollection();
            collecting = false;
        }
    }

    public static void SetEnabled(bool on)
    {
        PlayerPrefs.SetInt(OptOutKey, on ? 0 : 1);
        PlayerPrefs.Save();
        try { ApplyConsent(); } catch (Exception e) { Debug.Log("GameAnalytics: " + e.Message); }
    }

    // Records one event. Extra values come as name, value, name, value ... (text, whole numbers, decimals or true/false)
    public static void Record(string eventName, params object[] nameValuePairs)
    {
        try
        {
            if (!Enabled || !collecting || UnityServices.State != ServicesInitializationState.Initialized)
            {
                return;
            }
            CustomEvent e = new CustomEvent(eventName);
            for (int i = 0; i + 1 < nameValuePairs.Length; i += 2)
            {
                string key = nameValuePairs[i] as string;
                object value = nameValuePairs[i + 1];
                if (key == null || value == null) continue;
                if (value is string s) e[key] = s;
                else if (value is bool b) e[key] = b;
                else if (value is int n) e[key] = n;
                else if (value is long l) e[key] = l;
                else if (value is float f) e[key] = f;
                else if (value is double d) e[key] = d;
                else e[key] = value.ToString();
            }
            AnalyticsService.Instance.RecordEvent(e);
        }
        catch (Exception ex)
        {
            Debug.Log("GameAnalytics: " + ex.Message);
        }
    }
}
