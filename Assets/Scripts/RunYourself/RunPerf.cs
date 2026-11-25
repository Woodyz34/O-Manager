using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
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
    readonly List<Probe> counts = new List<Probe>();
    readonly FrameTiming[] timing = new FrameTiming[1];

    // The worst frame since the last line, as text
    double worstFrame;
    string worstText;

    public RunPerf()
    {
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
        foreach (Probe p in counts)
        {
            if (!p.rec.Valid) continue;
            sb.Append("; ").Append(p.label).Append(' ').Append(p.rec.LastValue.ToString("N0"));
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        foreach (Probe p in times) p.rec.Dispose();
        foreach (Probe p in counts) p.rec.Dispose();
        times.Clear();
        counts.Clear();
    }
}
