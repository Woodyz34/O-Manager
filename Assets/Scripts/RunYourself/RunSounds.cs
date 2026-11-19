using System;
using System.Collections.Generic;
using UnityEngine;

// The sounds of the run-yourself game mode. Footsteps, landings, the rustle of a thicket and the groans of a fall are
// real recordings (Resources/RunSounds, from "Oculus Audio Pack 01" by Oculus VR, LLC, CC BY 4.0: the credit is
// shown in the 3D settings window and in Docs/run-sounds-credits.md). The kind of ground decides which recording is
// played and how (earth, grass, hard ground, a squelch in a marsh, a splash in a stream). The breathing is made in
// code (no free recording of a running breath was to be found) and follows the steps, as a runner's does; a
// recorded breath can replace it: put breath_man_1.wav ... breath_woman_10.wav (one whole breath each) in
// Resources/RunSounds. The beeps of the punching units and the start clock are made in code too.
public class RunSounds : MonoBehaviour
{
    const int Rate = 44100;
    public const string Credit = "Footsteps and voices: \"Oculus Audio Pack 01\" by Oculus VR, LLC, CC BY 4.0";

    AudioSource steps, under, breath, voice, beeps;
    AudioClip[] dirt, grass, hard, landDirt, landGrass, landHard, brush, squelch, splash;
    AudioClip[] inhale, exhale, wholeBreaths, groans, grunts;
    AudioClip punch, finish, startShort, startLong;
    System.Random random = new System.Random();
    int stepsInBreath;
    bool breathIn = true;
    float effortNow;

    public static RunSounds Create(Transform parent, bool woman, int seed)
    {
        RunSounds s = new GameObject("Run sounds").AddComponent<RunSounds>();
        s.transform.SetParent(parent, false);
        s.random = new System.Random(seed);
        s.Make(woman);
        return s;
    }

    readonly System.Collections.Generic.List<(AudioSource source, float level, RunSettings.Sound kind)> sources =
        new System.Collections.Generic.List<(AudioSource, float, RunSettings.Sound)>();
    float nextVolume;

    AudioSource Source(float volume, RunSettings.Sound kind)
    {
        AudioSource a = gameObject.AddComponent<AudioSource>();
        a.playOnAwake = false;
        a.spatialBlend = 0f;
        sources.Add((a, volume, kind));
        a.volume = Mathf.Clamp01(volume * RunSettings.Gain(kind));
        return a;
    }

    // The sound sliders count at once, also while the settings are open in a race
    void Update()
    {
        if (Time.unscaledTime < nextVolume) return;
        nextVolume = Time.unscaledTime + 0.2f;
        foreach (var s in sources) s.source.volume = Mathf.Clamp01(s.level * RunSettings.Gain(s.kind));
    }

    void Make(bool woman)
    {
        steps = Source(0.7f, RunSettings.Sound.Steps);
        under = Source(0.45f, RunSettings.Sound.Steps);
        breath = Source(0.32f, RunSettings.Sound.Breathing);
        voice = Source(0.85f, RunSettings.Sound.Voice);
        beeps = Source(0.6f, RunSettings.Sound.Beeps);

        dirt = Load("run_dirt", 5);
        grass = Load("run_grass", 5);
        hard = Load("run_hard", 5);
        landDirt = Load("land_dirt", 2);
        landGrass = Load("land_grass", 2);
        landHard = Load("land_hard", 2);
        brush = Load("brush", 4);
        squelch = Made("squelch", 4, Squelch);
        splash = Made("splash", 4, Splash);

        // Two recorded voices for each, one picked at random for the race
        string who = (woman ? "woman" : "man") + (1 + random.Next(2));
        groans = Load("groan_" + who, 3);
        grunts = Load("grunt_" + who, 1);

        // The breath: recorded whole breaths if there are any, otherwise made for one of five people
        wholeBreaths = Load("breath_" + (woman ? "woman" : "man"), 10);
        int person = random.Next(5);
        float tract = (woman ? 1.18f : 1f) * (0.92f + 0.04f * person);
        inhale = Made("inhale", 4, i => Breath(0.32f, false, tract, i));
        exhale = Made("exhale", 4, i => Breath(0.42f, true, tract, i));

        punch = Tone(2730f, 0.16f, 0.5f);
        finish = Tone(2730f, 0.5f, 0.55f);
        startShort = Tone(1000f, 0.18f, 0.5f);
        startLong = Tone(1400f, 0.75f, 0.55f);
    }

    static AudioClip[] Load(string name, int count)
    {
        var list = new List<AudioClip>();
        for (int i = 1; i <= count; i++)
        {
            AudioClip clip = Resources.Load<AudioClip>("RunSounds/" + name + "_" + i);
            if (clip != null) list.Add(clip);
        }
        return list.ToArray();
    }

    AudioClip[] Made(string name, int count, Func<int, float[]> make)
    {
        var list = new AudioClip[count];
        for (int i = 0; i < count; i++) list[i] = ToClip(name + " " + i, make(i));
        return list;
    }

    static AudioClip ToClip(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    AudioClip Pick(AudioClip[] clips) => clips == null || clips.Length == 0 ? null : clips[random.Next(clips.Length)];

    float Vary(float around, float spread) => around * (1f - spread + 2f * spread * (float)random.NextDouble());

    // ---- Playing ----

    // One footstep. strength 0..1: how fast the runner goes.
    public void Footstep(RunTerrain.Surface surface, float strength)
    {
        float loud = Mathf.Clamp(0.45f + 0.55f * strength, 0.25f, 1f);
        AudioClip clip;
        float pitch, volume = loud, extra = 0f;
        AudioClip layer = null;
        switch (surface)
        {
            case RunTerrain.Surface.Road: case RunTerrain.Surface.Paved: clip = Pick(hard); pitch = 1f; volume *= 0.75f; break;
            case RunTerrain.Surface.Rock: clip = Pick(hard); pitch = 1.12f; volume *= 0.55f; break;
            case RunTerrain.Surface.Path: case RunTerrain.Surface.Sand: clip = Pick(dirt); pitch = 1f; break;
            case RunTerrain.Surface.Forest: clip = Pick(dirt); pitch = 0.86f; volume *= 0.8f; break;   // soft moss and needles
            case RunTerrain.Surface.Open: clip = Pick(grass); pitch = 1f; break;
            case RunTerrain.Surface.Rough: clip = Pick(grass); pitch = 0.92f; layer = random.NextDouble() < 0.35 ? Pick(brush) : null; extra = 0.35f; break;
            case RunTerrain.Surface.Thicket: clip = Pick(grass); pitch = 0.88f; layer = Pick(brush); extra = 0.75f; break;
            case RunTerrain.Surface.Marsh: clip = Pick(grass); pitch = 0.8f; volume *= 0.6f; layer = Pick(squelch); extra = 0.8f; break;
            case RunTerrain.Surface.Water: clip = Pick(squelch); pitch = 0.9f; volume *= 0.5f; layer = Pick(splash); extra = 0.9f; break;
            default: clip = Pick(dirt); pitch = 1f; break;
        }
        if (clip != null)
        {
            steps.pitch = Vary(pitch, 0.06f);
            steps.PlayOneShot(clip, Vary(volume, 0.12f));
        }
        if (layer != null)
        {
            under.pitch = Vary(1f, 0.1f);
            under.PlayOneShot(layer, Vary(extra * loud, 0.2f));
        }

        // The breath follows the steps: in over a few steps, out over a few, fewer steps the harder the work
        stepsInBreath++;
        int stepsPerHalf = effortNow > 0.7f ? 2 : effortNow > 0.35f ? 3 : 4;
        if (stepsInBreath >= stepsPerHalf || (breathIn && stepsInBreath >= stepsPerHalf - 1 && effortNow > 0.85f))
        {
            stepsInBreath = 0;
            Breath(breathIn);
            breathIn = !breathIn;
        }
    }

    public void Land(RunTerrain.Surface surface)
    {
        AudioClip[] set = surface == RunTerrain.Surface.Road || surface == RunTerrain.Surface.Paved || surface == RunTerrain.Surface.Rock ? landHard
                        : surface == RunTerrain.Surface.Path || surface == RunTerrain.Surface.Forest || surface == RunTerrain.Surface.Sand ? landDirt : landGrass;
        AudioClip clip = Pick(set);
        if (clip == null) return;
        steps.pitch = Vary(1f, 0.05f);
        steps.PlayOneShot(clip, 0.9f);
    }

    // effort 0..1: resting to flat out. Standing still the breath goes on by itself; running it follows the steps.
    float nextStillBreath;

    public void Breathe(float effort, bool running)
    {
        effortNow = Mathf.Clamp01(effort);
        if (running) return;
        if (Time.time < nextStillBreath) return;
        Breath(breathIn);
        breathIn = !breathIn;
        float cycle = Mathf.Lerp(3.2f, 1.1f, effortNow);
        nextStillBreath = Time.time + cycle * (breathIn ? 0.55f : 0.45f) * Vary(1f, 0.08f);
    }

    void Breath(bool inward)
    {
        float level = 0.08f + 0.92f * effortNow * effortNow;
        if (wholeBreaths.Length > 0)
        {
            if (!inward) return;   // a recorded breath has both halves
            breath.pitch = Vary(1f, 0.04f);
            breath.PlayOneShot(Pick(wholeBreaths), level);
            return;
        }
        breath.pitch = Vary(1f + 0.06f * effortNow, 0.04f);
        breath.PlayOneShot(Pick(inward ? inhale : exhale), level * (inward ? 0.7f : 1f));
    }

    public void Groan()
    {
        AudioClip clip = Pick(groans);
        if (clip == null) return;
        voice.pitch = Vary(1f, 0.04f);
        voice.PlayOneShot(clip, 1f);
        stepsInBreath = -4;
    }

    public void Grunt()
    {
        AudioClip clip = Pick(grunts);
        if (clip == null) return;
        voice.pitch = Vary(1.05f, 0.05f);
        voice.PlayOneShot(clip, 0.55f);
    }

    public void Punch() => beeps.PlayOneShot(punch, 1f);
    public void Finish() => beeps.PlayOneShot(finish, 1f);
    public void StartBeep(bool last) => beeps.PlayOneShot(last ? startLong : startShort, 1f);

    // ---- Making the sounds ----

    float Noise() => (float)random.NextDouble() * 2f - 1f;

    // A two-pole band-pass filter (a resonance), as in the audio cookbook
    class Band
    {
        float b0, b2, a1, a2, x1, x2, y1, y2;

        public Band(float freq, float q)
        {
            float w = 2f * Mathf.PI * Mathf.Clamp(freq, 20f, Rate * 0.45f) / Rate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
            b0 = alpha / a0;
            b2 = -alpha / a0;
            a1 = -2f * Mathf.Cos(w) / a0;
            a2 = (1f - alpha) / a0;
        }

        public float Run(float x)
        {
            float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x;
            y2 = y1; y1 = y;
            return y;
        }
    }

    static float[] Normalize(float[] d, float peak)
    {
        float max = 0.0001f;
        foreach (float v in d) max = Mathf.Max(max, Mathf.Abs(v));
        for (int i = 0; i < d.Length; i++) d[i] *= peak / max;
        return d;
    }

    float[] Squelch(int variant)
    {
        var d = new float[(int)(0.22f * Rate)];
        var low = new Band(380f + 80f * variant, 2.2f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Clamp01(t / 0.025f) * Mathf.Exp(-t / 0.07f);
            float wobble = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * (28f + 6f * variant) * t);
            d[i] = low.Run(Noise()) * env * wobble;
        }
        return Normalize(d, 0.75f);
    }

    float[] Splash(int variant)
    {
        var d = new float[(int)(0.3f * Rate)];
        var high = new Band(1800f + 400f * variant, 0.6f);
        var drops = new Band(3200f, 4f);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Clamp01(t / 0.006f) * Mathf.Exp(-t / 0.09f);
            float v = high.Run(Noise()) * env;
            if (t > 0.05f && random.NextDouble() < 0.002f) v += drops.Run(Noise() * 4f);
            else v += drops.Run(0f) * 0.9f;
            d[i] = v;
        }
        return Normalize(d, 0.75f);
    }

    // A breath: only air, no voice. Pink noise (as rushing air sounds) through the broad resonances of the mouth and
    // throat; out through the mouth with a quick start and a long fall, in with more hiss and a softer start. The
    // loudness wavers a little, as a real breath does.
    float[] Breath(float length, bool outward, float tract, int variant)
    {
        length = Vary(length, 0.15f);
        var d = new float[(int)(length * Rate)];
        float f1 = (outward ? 620f : 900f) * tract * Vary(1f, 0.08f), f2 = (outward ? 1500f : 2300f) * tract * Vary(1f, 0.08f);
        var r1 = new Band(f1, 0.8f);
        var r2 = new Band(f2, 1.1f);
        var hiss = new Band(outward ? 3800f : 5200f, 0.9f);
        float b0 = 0f, b1 = 0f, b2 = 0f, flutter = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate, u = t / length;
            // Pink noise (Paul Kellet's economy filter)
            float white = Noise();
            b0 = 0.99765f * b0 + white * 0.0990460f;
            b1 = 0.96300f * b1 + white * 0.2965164f;
            b2 = 0.57000f * b2 + white * 1.0526913f;
            float pink = (b0 + b1 + b2 + white * 0.1848f) * 0.25f;
            float env = outward ? Mathf.Clamp01(u / 0.12f) * Mathf.Pow(1f - u, 1.4f)
                                : Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(u)), 1.3f);
            if ((i & 255) == 0) flutter = 0.85f + 0.3f * (float)random.NextDouble();
            float v = r1.Run(pink) * 1.0f + r2.Run(pink) * 0.6f + hiss.Run(white) * (outward ? 0.12f : 0.3f) + pink * 0.15f;
            d[i] = v * env * flutter;
        }
        // Soften the edges of the flutter steps
        for (int i = 1; i < d.Length; i++) d[i] = d[i] * 0.6f + d[i - 1] * 0.4f;
        return Normalize(d, outward ? 0.7f : 0.5f);
    }

    static AudioClip Tone(float freq, float length, float level)
    {
        var d = new float[(int)(length * Rate)];
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Clamp01(t / 0.005f) * Mathf.Clamp01((length - t) / 0.01f);
            // A little square in the sine, as a small piezo beeper sounds
            float s = Mathf.Sin(2f * Mathf.PI * freq * t);
            d[i] = (s * 0.8f + Mathf.Sign(s) * 0.2f) * env * level;
        }
        return ToClip("Beep " + freq, d);
    }
}
