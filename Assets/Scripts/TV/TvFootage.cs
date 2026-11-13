using System.Collections.Generic;
using UnityEngine;

// What happened at one TV control: every time a runner came within reach of it, its positions, with the time they
// were seen. The race runs 20 times faster than real time, so a runner would rush past the control in a couple of
// seconds; the TV shows the footage afterwards in slow motion instead, close to real running speed, and skips the
// stretches when nobody is there. When it falls far behind the race it plays quicker to catch up.
public class TvFootage
{
    public const float Radius = 100f;          // metres from the control that are filmed
    const float MostSlow = 14f, LeastSlow = 4f;

    public class Pass
    {
        public object key;
        public string label;
        public Color color;
        public readonly List<Vector3> samples = new List<Vector3>();   // x = east, y = north (metres from the control), z = time
        public float lastSeen;
        public float Start => samples[0].z;
        public float End => samples[samples.Count - 1].z;

        public bool At(float t, out float x, out float z)
        {
            x = z = 0f;
            if (samples.Count == 0 || t < Start || t > End) return false;
            int lo = 0, hi = samples.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (samples[mid].z <= t) lo = mid;
                else hi = mid;
            }
            Vector3 a = samples[lo], b = samples[hi];
            float f = b.z > a.z ? (t - a.z) / (b.z - a.z) : 0f;
            x = Mathf.Lerp(a.x, b.x, f);
            z = Mathf.Lerp(a.y, b.y, f);
            return true;
        }
    }

    public readonly CoursePoint control;
    public readonly string name;
    public readonly List<Pass> passes = new List<Pass>();
    readonly Dictionary<object, Pass> open = new Dictionary<object, Pass>();
    public float playTime = -1f;               // the moment of the race being shown (Time.time when it happened)

    public TvFootage(CoursePoint control, string name)
    {
        this.control = control;
        this.name = name;
    }

    // A runner seen at map position (e, n) at time now
    public void Record(object key, string label, Color color, double e, double n, float now)
    {
        float x = (float)(e - control.e), z = (float)(n - control.n);
        bool inside = x * x + z * z < Radius * Radius;
        open.TryGetValue(key, out Pass pass);
        if (pass != null && pass.samples.Count > 0)
        {
            // A jump (sent back to the start, a new leg): a new pass
            Vector3 last = pass.samples[pass.samples.Count - 1];
            if ((last.x - x) * (last.x - x) + (last.y - z) * (last.y - z) > 30f * 30f)
            {
                open.Remove(key);
                pass = null;
            }
        }
        if (inside)
        {
            if (pass == null)
            {
                pass = new Pass { key = key, label = label, color = color };
                passes.Add(pass);
                open[key] = pass;
            }
            if (pass.samples.Count == 0 || now - pass.samples[pass.samples.Count - 1].z > 0.015f) pass.samples.Add(new Vector3(x, z, now));
            pass.lastSeen = now;
        }
        else if (pass != null)
        {
            pass.samples.Add(new Vector3(x, z, now));   // one step outside, so the runner runs out of the picture
            open.Remove(key);
        }
    }

    // Runners that stopped being seen (left the race, stopped running) end their pass
    public void CloseStale(float now)
    {
        List<object> stale = null;
        foreach (var pair in open)
        {
            if (now - pair.Value.lastSeen > 1f) (stale ??= new List<object>()).Add(pair.Key);
        }
        if (stale != null) foreach (object key in stale) open.Remove(key);
    }

    // Footage not shown yet
    public bool HasPending()
    {
        foreach (Pass p in passes) if (p.End > playTime && p.samples.Count > 1) return true;
        return false;
    }

    public float NextStart()
    {
        float best = float.MaxValue;
        foreach (Pass p in passes) if (p.samples.Count > 1 && p.End > playTime) best = Mathf.Min(best, Mathf.Max(p.Start, playTime));
        return best;
    }

    // Moves the playback on by dt real seconds. Returns true while someone is on screen.
    public bool Advance(float dt, float now, List<TvWorldView.RunnerAt> into)
    {
        into.Clear();
        if (playTime < 0f)
        {
            // Shown for the first time: from the first runner filmed, but not more than a minute back
            float first = now;
            foreach (Pass p in passes) if (p.samples.Count > 0) first = Mathf.Min(first, p.Start);
            playTime = Mathf.Max(first, now - 60f);
        }
        float behind = now - playTime;
        if (behind > 90f) playTime = now - 60f;
        float slow = Mathf.Lerp(MostSlow, LeastSlow, Mathf.InverseLerp(8f, 50f, behind));
        playTime = Mathf.Min(now, playTime + dt / slow);
        Collect(into);
        if (into.Count == 0)
        {
            // Nobody there: skip to the next one who comes
            float next = NextStart();
            if (next < float.MaxValue && next > playTime)
            {
                playTime = next;
                Collect(into);
            }
        }
        passes.RemoveAll(p => p.samples.Count > 0 && p.End < playTime - 1f && !open.ContainsValue(p));
        return into.Count > 0;
    }

    void Collect(List<TvWorldView.RunnerAt> into)
    {
        foreach (Pass p in passes)
        {
            if (p.samples.Count < 2 || !p.At(playTime, out float x, out float z)) continue;
            into.Add(new TvWorldView.RunnerAt { key = p, label = p.label, color = p.color, x = x, z = z });
        }
    }
}
