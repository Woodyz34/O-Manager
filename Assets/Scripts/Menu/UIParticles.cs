using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// One burst of particles: where they start, how fast they fly and how they look.
[Serializable]
public struct ParticleBurst
{
    public int count;
    public Vector2 origin;
    public Vector2 spread;       // random offset around the origin
    public float angleMin;       // direction in degrees, 0 = right, 90 = up
    public float angleMax;
    public float speedMin;
    public float speedMax;
    public float sizeMin;
    public float sizeMax;
    public float endSize;        // size at the end of life, as a share of the start size
    public float lifeMin;
    public float lifeMax;
    public float gravity;        // pixels per second squared, pulling down
    public float drag;
    public float spin;           // degrees per second, either way
    public float twinkle;        // 0 = steady, 1 = flickers
    public Color colorA;
    public Color colorB;
}

// Sparks, dust and confetti drawn straight into the UI, so they work on any canvas
// without a particle system or an extra camera.
[RequireComponent(typeof(CanvasRenderer))]
public class UIParticles : MaskableGraphic
{
    public Texture texture;

    [Header("Steady stream (optional)")]
    public float streamRate;
    public ParticleBurst stream;

    struct Particle
    {
        public Vector2 position;
        public Vector2 velocity;
        public float size;
        public float endSize;
        public float life;
        public float age;
        public float rotation;
        public float spin;
        public float gravity;
        public float drag;
        public float twinkle;
        public float seed;
        public Color color;
    }

    readonly List<Particle> particles = new List<Particle>(256);
    float streamDebt;

    public override Texture mainTexture
    {
        get { return texture != null ? texture : s_WhiteTexture; }
    }

    public void Emit(ParticleBurst burst)
    {
        for (int i = 0; i < burst.count; i++)
        {
            float angle = UnityEngine.Random.Range(burst.angleMin, burst.angleMax) * Mathf.Deg2Rad;
            float speed = UnityEngine.Random.Range(burst.speedMin, burst.speedMax);
            particles.Add(new Particle
            {
                position = burst.origin + new Vector2(UnityEngine.Random.Range(-burst.spread.x, burst.spread.x), UnityEngine.Random.Range(-burst.spread.y, burst.spread.y)),
                velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                size = UnityEngine.Random.Range(burst.sizeMin, burst.sizeMax),
                endSize = burst.endSize,
                life = Mathf.Max(0.05f, UnityEngine.Random.Range(burst.lifeMin, burst.lifeMax)),
                rotation = UnityEngine.Random.Range(0f, 360f),
                spin = UnityEngine.Random.Range(-burst.spin, burst.spin),
                gravity = burst.gravity,
                drag = burst.drag,
                twinkle = burst.twinkle,
                seed = UnityEngine.Random.Range(0f, 100f),
                color = Color.Lerp(burst.colorA, burst.colorB, UnityEngine.Random.value)
            });
        }
        SetVerticesDirty();
    }

    public void Clear()
    {
        particles.Clear();
        SetVerticesDirty();
    }

    void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        if (streamRate > 0f)
        {
            streamDebt += streamRate * dt;
            int due = Mathf.FloorToInt(streamDebt);
            if (due > 0)
            {
                streamDebt -= due;
                ParticleBurst burst = stream;
                burst.count = due;
                Emit(burst);
            }
        }

        if (particles.Count == 0)
        {
            return;
        }
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle p = particles[i];
            p.age += dt;
            if (p.age >= p.life)
            {
                particles.RemoveAt(i);
                continue;
            }
            p.velocity.y -= p.gravity * dt;
            p.velocity *= Mathf.Clamp01(1f - p.drag * dt);
            p.position += p.velocity * dt;
            p.rotation += p.spin * dt;
            particles[i] = p;
        }
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float time = Application.isPlaying ? Time.unscaledTime : 0f;
        for (int i = 0; i < particles.Count; i++)
        {
            Particle p = particles[i];
            float t = p.age / p.life;
            float alpha = Mathf.Clamp01(t / 0.12f) * (1f - Mathf.SmoothStep(0.45f, 1f, t));
            if (p.twinkle > 0f)
            {
                alpha *= 1f - p.twinkle * (0.5f + 0.5f * Mathf.Sin(time * 14f + p.seed * 7f));
            }
            Color c = p.color * color;
            c.a *= alpha;
            Color32 c32 = c;

            float half = p.size * Mathf.Lerp(1f, p.endSize, t) * 0.5f;
            float rad = p.rotation * Mathf.Deg2Rad;
            Vector2 right = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * half;
            Vector2 up = new Vector2(-right.y, right.x);

            int start = vh.currentVertCount;
            vh.AddVert(p.position - right - up, c32, new Vector2(0, 0));
            vh.AddVert(p.position - right + up, c32, new Vector2(0, 1));
            vh.AddVert(p.position + right + up, c32, new Vector2(1, 1));
            vh.AddVert(p.position + right - up, c32, new Vector2(1, 0));
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
