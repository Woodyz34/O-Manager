using System;
using System.Collections.Generic;
using UnityEngine;

// The sounds of the run-yourself game mode, all made in code when the race loads, so nothing has to be recorded:
// footsteps that sound like the ground (gravel, earth, moss, twigs in a thicket, a squelching marsh, splashing water,
// hard rock), the runner's breathing (ten different people, five women and five men, one picked at random each
// race; harder uphill, at speed and late in the race), groans when the runner falls, and the beeps of the punching
// units and the start clock.
// Recorded sounds can replace the made ones: put .wav files in Assets/Resources/RunSounds named like
// groan_woman_1, groan_man_2, step_gravel_1 ... and they are used instead.
public class RunSounds : MonoBehaviour
{
    const int Rate = 44100;

    AudioSource steps, breath, voice, beeps;
    readonly Dictionary<RunTerrain.Surface, AudioClip[]> stepClips = new Dictionary<RunTerrain.Surface, AudioClip[]>();
    AudioClip[] inhale, exhale, groans, grunts;
    AudioClip punch, finish, startShort, startLong;
    System.Random random = new System.Random();
    float nextBreath;
    bool breathIn = true;

    public static RunSounds Create(Transform parent, bool woman, int seed)
    {
        RunSounds s = new GameObject("Run sounds").AddComponent<RunSounds>();
        s.transform.SetParent(parent, false);
        s.random = new System.Random(seed);
        s.Make(woman);
        return s;
    }

    AudioSource Source(float volume)
    {
        AudioSource a = gameObject.AddComponent<AudioSource>();
        a.playOnAwake = false;
        a.spatialBlend = 0f;
        a.volume = volume;
        return a;
    }

    void Make(bool woman)
    {
        steps = Source(0.55f);
        breath = Source(0.5f);
        voice = Source(0.8f);
        beeps = Source(0.6f);

        stepClips[RunTerrain.Surface.Road] = Clips("step_gravel", 4, i => Step(0.09f, 3500f, 0f, 0.6f, 0.35f, 0f, 0f, i));
        stepClips[RunTerrain.Surface.Path] = Clips("step_earth", 4, i => Step(0.07f, 1300f, 85f, 0.25f, 0.1f, 0f, 0f, i));
        stepClips[RunTerrain.Surface.Forest] = Clips("step_moss", 4, i => Step(0.08f, 800f, 75f, 0.15f, 0.05f, 0.08f, 0f, i));
        stepClips[RunTerrain.Surface.Open] = Clips("step_grass", 4, i => Step(0.08f, 1700f, 70f, 0.12f, 0.05f, 0.18f, 0f, i));
        stepClips[RunTerrain.Surface.Rough] = Clips("step_twigs", 4, i => Step(0.09f, 900f, 75f, 0.15f, 0.05f, 0.3f, 0.6f, i));
        stepClips[RunTerrain.Surface.Thicket] = Clips("step_thicket", 4, i => Step(0.1f, 800f, 70f, 0.15f, 0.05f, 0.55f, 0.8f, i));
        stepClips[RunTerrain.Surface.Marsh] = Clips("step_marsh", 4, Squelch);
        stepClips[RunTerrain.Surface.Water] = Clips("step_water", 4, Splash);
        stepClips[RunTerrain.Surface.Rock] = Clips("step_rock", 4, i => Step(0.035f, 5000f, 140f, 0.9f, 0.6f, 0f, 0f, i));
        stepClips[RunTerrain.Surface.Paved] = stepClips[RunTerrain.Surface.Rock];
        stepClips[RunTerrain.Surface.Sand] = Clips("step_sand", 4, i => Step(0.1f, 2500f, 50f, 0.2f, 0.4f, 0f, 0f, i));

        // One of ten people: five women and five men, each breathing their own way
        int person = random.Next(5);
        float pitch = (woman ? 1.25f : 1f) * (0.9f + 0.05f * person);
        float voiced = 0.04f + 0.035f * ((person * 3) % 5);
        float f0 = (woman ? 205f : 112f) * (0.92f + 0.04f * person);
        string who = woman ? "woman" : "man";
        inhale = Clips("breath_in_" + who, 3, i => Breath(0.28f + 0.03f * person, 1100f * pitch, 0.6f, 0f, f0, i));
        exhale = Clips("breath_out_" + who, 3, i => Breath(0.34f + 0.025f * person, 820f * pitch, 1f, voiced, f0, i));
        groans = Clips("groan_" + who, 3, i => Groan(f0 * (1.02f + 0.05f * i), woman, 0.42f + 0.06f * i, i));
        grunts = Clips("grunt_" + who, 2, i => Groan(f0 * 1.1f, woman, 0.16f, i + 7));

        punch = Tone(2730f, 0.16f, 0.5f);
        finish = Tone(2730f, 0.5f, 0.55f);
        startShort = Tone(1000f, 0.18f, 0.5f);
        startLong = Tone(1400f, 0.75f, 0.55f);
    }

    // A recorded sound from Resources/RunSounds when there is one, otherwise made here
    AudioClip[] Clips(string name, int count, Func<int, float[]> make)
    {
        var list = new List<AudioClip>();
        for (int i = 1; i <= count; i++)
        {
            AudioClip recorded = Resources.Load<AudioClip>("RunSounds/" + name + "_" + i);
            if (recorded != null) list.Add(recorded);
        }
        if (list.Count > 0) return list.ToArray();
        for (int i = 0; i < count; i++) list.Add(ToClip(name + " " + i, make(i)));
        return list.ToArray();
    }

    static AudioClip ToClip(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---- Playing ----

    public void Footstep(RunTerrain.Surface surface, float strength)
    {
        if (!stepClips.TryGetValue(surface, out AudioClip[] clips) || clips.Length == 0) return;
        steps.pitch = 0.9f + (float)random.NextDouble() * 0.2f;
        steps.PlayOneShot(clips[random.Next(clips.Length)], Mathf.Clamp(0.35f + 0.65f * strength, 0.2f, 1f));
    }

    // effort 0..1: resting to flat out; breathing gets quicker and louder
    public void Breathe(float effort)
    {
        if (Time.time < nextBreath) return;
        effort = Mathf.Clamp01(effort);
        AudioClip[] set = breathIn ? inhale : exhale;
        breath.pitch = 0.95f + (float)random.NextDouble() * 0.1f + effort * 0.05f;
        breath.PlayOneShot(set[random.Next(set.Length)], 0.12f + 0.75f * effort);
        float cycle = Mathf.Lerp(2.6f, 1.05f, effort);          // seconds per whole breath
        nextBreath = Time.time + cycle * (breathIn ? 0.42f : 0.58f) * (0.92f + 0.16f * (float)random.NextDouble());
        breathIn = !breathIn;
    }

    public void Groan()
    {
        voice.pitch = 0.95f + (float)random.NextDouble() * 0.1f;
        voice.PlayOneShot(groans[random.Next(groans.Length)], 1f);
        nextBreath = Time.time + 0.8f;
    }

    public void Grunt()
    {
        voice.pitch = 0.95f + (float)random.NextDouble() * 0.1f;
        voice.PlayOneShot(grunts[random.Next(grunts.Length)], 0.7f);
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

    // A footstep: a burst of filtered noise (the ground), a low thump (the foot), crackle (gravel, twigs on the ground),
    // a rustle (leaves and branches brushing the legs) and now and then a twig snapping
    float[] Step(float length, float tone, float thump, float hard, float crackle, float rustle, float snap, int variant)
    {
        float total = length + rustle * 0.25f + 0.05f;
        var d = new float[(int)(total * Rate)];
        var ground = new Band(tone * (0.85f + 0.3f * (float)random.NextDouble()), 0.9f);
        var leaves = new Band(3800f, 0.7f);
        var twig = new Band(2200f + 1200f * (float)random.NextDouble(), 6f);
        bool snaps = snap > 0f && random.NextDouble() < snap * 0.6f;
        float snapAt = 0.01f + 0.05f * (float)random.NextDouble();
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate;
            float env = t < 0.004f ? t / 0.004f : Mathf.Exp(-(t - 0.004f) / (length * (0.35f - 0.2f * hard)));
            float v = ground.Run(Noise()) * env * 1.4f;
            if (thump > 0f) v += Mathf.Sin(2f * Mathf.PI * thump * t) * Mathf.Exp(-t / 0.035f) * 0.6f;
            if (crackle > 0f && random.NextDouble() < crackle * 0.02f * env) v += Noise() * env * 1.5f;
            if (rustle > 0f) v += leaves.Run(Noise()) * rustle * Mathf.Exp(-t / (0.08f + rustle * 0.1f)) * Mathf.Clamp01(t / 0.02f);
            if (snaps && t >= snapAt) v += twig.Run(t - snapAt < 0.002f ? Noise() * 6f : 0f) * 1.2f;
            d[i] = v;
        }
        return Normalize(d, 0.8f);
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

    // A breath: air through the throat and mouth (noise through a broad resonance), with a little voice on the way
    // out when the runner is working hard
    float[] Breath(float length, float formant, float strength, float voiced, float f0, int variant)
    {
        length *= 0.9f + 0.2f * (float)random.NextDouble();
        var d = new float[(int)(length * Rate)];
        var mouth = new Band(formant * (0.95f + 0.1f * variant / 3f), 1.4f);
        var throat = new Band(formant * 2.3f, 2f);
        var vowel = new Band(formant * 0.75f, 4f);
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate, u = t / length;
            float env = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.6f)) * (1f - 0.3f * u);
            float air = Noise();
            float v = mouth.Run(air) + throat.Run(air) * 0.35f;
            if (voiced > 0f)
            {
                phase += f0 * (1f - 0.1f * u) / Rate;
                phase -= Mathf.Floor(phase);
                v += vowel.Run(phase * 2f - 1f) * voiced * 4f;
            }
            d[i] = v * env * strength;
        }
        return Normalize(d, 0.6f * strength);
    }

    // A groan when falling: a voiced "oof" (pulses from the throat through the vowel's resonances), breath on top
    float[] Groan(float f0, bool woman, float length, int variant)
    {
        var d = new float[(int)(length * Rate)];
        float f1 = woman ? 560f : 470f, f2 = woman ? 1000f : 850f, f3 = woman ? 2900f : 2500f;
        var r1 = new Band(f1 * (0.95f + 0.03f * variant), 5f);
        var r2 = new Band(f2 * (0.95f + 0.04f * variant), 6f);
        var r3 = new Band(f3, 8f);
        var air = new Band(f2, 1.5f);
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)Rate, u = t / length;
            float env = Mathf.Clamp01(t / 0.015f) * Mathf.Pow(1f - u, 1.6f);
            float pitch = f0 * (1.12f - 0.3f * u) * (1f + 0.01f * Mathf.Sin(2f * Mathf.PI * 6f * t));
            phase += pitch / Rate;
            float pulse = 0f;
            if (phase >= 1f)
            {
                phase -= 1f;
                pulse = 1f;
            }
            float voiceSound = r1.Run(pulse) * 1f + r2.Run(pulse) * 0.6f + r3.Run(pulse) * 0.2f;
            d[i] = (voiceSound * 3f + air.Run(Noise()) * 0.25f) * env;
        }
        return Normalize(d, 0.85f);
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
