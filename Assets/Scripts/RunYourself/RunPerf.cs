using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

// What the whole of a slow frame went to, for the "RunPerf:" lines in the log (2026-11-25, Anders: Fossum still lagged
// at ~100 ms a frame, but the 3D world's own parts took under 1 ms, so the time went somewhere else).
// Reads Unity's own profiler markers (all scripts' Update, physics, rendering, shadows, waiting for the graphics card,
// the Editor itself ...) and counters (draw calls, triangles) without the Profiler window being open. Marker times
// are only there in the Editor and in development builds; elsewhere only the counters and the graphics card's time.
public class RunPerf : IDisposable
{
    struct Probe
    {
        public string label;
        public ProfilerRecorder rec;
    }

    readonly List<Probe> times = new List<Probe>();
    // Every script's own Update / LateUpdate / FixedUpdate (Unity names them "Class.Update() [Invoke]"), found once the
    // race has run a while; the worst ones are listed by name
    readonly List<Probe> scripts = new List<Probe>();
    readonly HashSet<string> scriptNames = new HashSet<string>();
    int lookedForScripts;
    float nextLook;

    // Parts of the run-yourself session's own frame (RunSession), measured by hand
    static readonly string[] partNames = { "runner", "hand map", "online", "window search", "menu check", "punch", "world",
                                           "2D runner", "2D collisions", "clock", "leaderboard", "player", "mistakes", "game mode" };
    static readonly long[] partTicks = new long[partNames.Length];
    static readonly long[] lastPartTicks = new long[partNames.Length];
    public enum Part { Runner, HandMap, Online, WindowSearch, MenuCheck, Punch, World,
                       Agent2D, Collision2D, Clock, Leaderboard, PlayerUpdate, Mistakes, GameMode }
    // Each frame's parts are kept apart: the frame time Unity gives is the frame before's, so the parts shown with
    // it are that frame's too (2026-11-26: they were mixed with the current frame's)
    static int partFrame = -1;
    static void Rotate()
    {
        int now = UnityEngine.Time.frameCount;
        if (now == partFrame) return;
        if (now == partFrame + 1) Array.Copy(partTicks, lastPartTicks, partTicks.Length);
        else Array.Clear(lastPartTicks, 0, lastPartTicks.Length);
        Array.Clear(partTicks, 0, partTicks.Length);
        partFrame = now;
    }
    public static void Add(Part part, long ticks)
    {
        Rotate();
        partTicks[(int)part] += ticks;
    }
    readonly List<Probe> counts = new List<Probe>();
    readonly FrameTiming[] timing = new FrameTiming[1];

    // The worst frame since the last line, as text
    double worstFrame;
    string worstText;

#if UNITY_EDITOR
    bool profilerWasOn;
#endif

    public RunPerf()
    {
#if UNITY_EDITOR
        // In the Editor each script's own time ("RunWorld.Update() [Invoke]") is only measured while the profiler
        // records (2026-11-26: the log said "scripts Update 120 ms" but could not say which script)
        profilerWasOn = UnityEngine.Profiling.Profiler.enabled;
        try { UnityEngine.Profiling.Profiler.enabled = true; } catch (Exception) { }
#endif
        AddTime(ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate", "scripts Update");
        AddTime(ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate", "scripts LateUpdate");
        AddTime(ProfilerCategory.Scripts, "FixedUpdate.ScriptRunBehaviourFixedUpdate", "scripts FixedUpdate");
        AddTime(ProfilerCategory.Scripts, "Update.ScriptRunDelayedDynamicFrameRate", "coroutines");
        AddTime(ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate", "physics 3D");
        AddTime(ProfilerCategory.Physics, "FixedUpdate.Physics2DFixedUpdate", "physics 2D");
        AddTime(ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering", "rendering");
        AddTime(ProfilerCategory.Render, "Shadows.RenderShadowMap", "shadows");
        AddTime(ProfilerCategory.Render, "Culling", "culling");
        AddTime(ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread", "waiting for graphics card");
        AddTime(ProfilerCategory.Render, "Gfx.WaitForGfxCommandsFromMainThread", "render thread idle");
        AddTime(ProfilerCategory.Gui, "PostLateUpdate.PlayerUpdateCanvases", "UI canvases");
        AddTime(ProfilerCategory.Gui, "PostLateUpdate.PlayerEmitCanvasGeometry", "UI geometry");
        AddTime(ProfilerCategory.Gui, "PreLateUpdate.UpdateRectTransform", "UI layout");
        AddTime(ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationBegin", "animation");
        AddTime(ProfilerCategory.Memory, "GC.Collect", "memory cleanup (GC)");
        AddTime(ProfilerCategory.Loading, "Loading.ReadObject", "loading");
        AddTime(ProfilerCategory.Internal, "EditorLoop", "Unity Editor itself");

        AddCount(ProfilerCategory.Render, "Batches Count", "draw calls");
        AddCount(ProfilerCategory.Render, "SetPass Calls Count", "set-pass");
        AddCount(ProfilerCategory.Render, "Triangles Count", "triangles");
        AddCount(ProfilerCategory.Render, "Shadow Casters Count", "shadow casters");
        AddCount(ProfilerCategory.Memory, "GC Allocated In Frame", "bytes allocated");
    }

    void AddTime(ProfilerCategory category, string marker, string label)
    {
        try { times.Add(new Probe { label = label, rec = ProfilerRecorder.StartNew(category, marker, 1) }); }
        catch (Exception) { }
    }

    void AddCount(ProfilerCategory category, string counter, string label)
    {
        try { counts.Add(new Probe { label = label, rec = ProfilerRecorder.StartNew(category, counter, 1) }); }
        catch (Exception) { }
    }

    // Each frame: the frame before is complete now (its time is in frameMs)
    public void Frame(double frameMs, bool slow)
    {
        FrameTimingManager.CaptureFrameTimings();
        Rotate();
        // Scripts are looked for again every 10 s: each script's own marker only exists once it has run
        if (UnityEngine.Time.unscaledTime >= nextLook)
        {
            lookedForScripts++;
            nextLook = UnityEngine.Time.unscaledTime + (lookedForScripts < 4 ? 5f : 10f);
            LookForScripts();
        }
        if (!slow || frameMs <= worstFrame) return;
        worstFrame = frameMs;
        worstText = Describe();
    }

    // The worst frame's breakdown, then starts over
    public string TakeWorst()
    {
        string text = worstText ?? "";
        worstText = null;
        worstFrame = 0;
        return text;
    }

    void LookForScripts()
    {
        try
        {
            var all = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(all);
            foreach (ProfilerRecorderHandle h in all)
            {
                string name = ProfilerRecorderHandle.GetDescription(h).Name;
                if (name == null || scriptNames.Contains(name)) continue;
                if (!(name.EndsWith(".Update() [Invoke]", StringComparison.Ordinal) || name.EndsWith(".LateUpdate() [Invoke]", StringComparison.Ordinal)
                      || name.EndsWith(".FixedUpdate() [Invoke]", StringComparison.Ordinal) || name.EndsWith(".Update()", StringComparison.Ordinal)
                      || name.EndsWith(".LateUpdate()", StringComparison.Ordinal) || name.StartsWith("Fusion", StringComparison.Ordinal)
                      || name.StartsWith("NetworkRunner", StringComparison.Ordinal) || name.StartsWith("Coroutine", StringComparison.Ordinal))) continue;
                scriptNames.Add(name);
                var rec = new ProfilerRecorder(h, 1);
                rec.Start();
                scripts.Add(new Probe { label = name.Replace(" [Invoke]", ""), rec = rec });
            }
        }
        catch (Exception) { }
    }

    string Describe()
    {
        var parts = new List<(double ms, string label)>();
        foreach (Probe p in times)
        {
            if (!p.rec.Valid) continue;
            double ms = p.rec.LastValue / 1e6;
            if (ms >= 0.5) parts.Add((ms, p.label));
        }
        parts.Sort((a, b) => b.ms.CompareTo(a.ms));
        var sb = new StringBuilder();
        if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
        {
            sb.Append("main thread ").Append(timing[0].cpuMainThreadFrameTime.ToString("0"))
              .Append(" ms, render thread ").Append(timing[0].cpuRenderThreadFrameTime.ToString("0"))
              .Append(" ms, graphics card ").Append(timing[0].gpuFrameTime.ToString("0")).Append(" ms");
        }
        if (parts.Count > 0)
        {
            if (sb.Length > 0) sb.Append("; ");
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(parts[i].label).Append(' ').Append(parts[i].ms.ToString("0.0")).Append(" ms");
            }
        }
        else if (sb.Length == 0) sb.Append("no marker times (not a development build)");
        // The scripts that took the longest, by name
        var worst = new List<(double ms, string label)>();
        foreach (Probe p in scripts)
        {
            if (!p.rec.Valid) continue;
            double ms = p.rec.LastValue / 1e6;
            if (ms >= 2.0) worst.Add((ms, p.label));
        }
        worst.Sort((a, b) => b.ms.CompareTo(a.ms));
        if (worst.Count > 0)
        {
            sb.Append("; slowest scripts: ");
            for (int i = 0; i < worst.Count && i < 8; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(worst[i].label).Append(' ').Append(worst[i].ms.ToString("0.0")).Append(" ms");
            }
        }
        else sb.Append("; slowest scripts: none named (" + scripts.Count + " watched)");
        // The run-yourself session's own parts
        double tick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        sb.Append("; parts:");
        for (int i = 0; i < partNames.Length; i++) sb.Append(' ').Append(partNames[i]).Append(' ').Append((lastPartTicks[i] * tick).ToString("0.0"));
        sb.Append(" ms");
        foreach (Probe p in counts)
        {
            if (!p.rec.Valid) continue;
            sb.Append("; ").Append(p.label).Append(' ').Append(p.rec.LastValue.ToString("N0"));
        }
        return sb.ToString();
    }

    public void Dispose()
    {
#if UNITY_EDITOR
        try { UnityEngine.Profiling.Profiler.enabled = profilerWasOn; } catch (Exception) { }
#endif
        foreach (Probe p in times) p.rec.Dispose();
        foreach (Probe p in counts) p.rec.Dispose();
        foreach (Probe p in scripts) p.rec.Dispose();
        scripts.Clear();
        scriptNames.Clear();
        times.Clear();
        counts.Clear();
    }
}
