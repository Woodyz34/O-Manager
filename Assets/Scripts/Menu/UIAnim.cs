using System;
using System.Collections;
using UnityEngine;

// Small helpers for code-driven UI animation.
public static class UIAnim
{
    // Calls step with 0..1 over the given time, ending on exactly 1
    public static IEnumerator Run(float seconds, Action<float> step)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            step(elapsed / seconds);
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        step(1f);
    }

    public static IEnumerator Wait(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
    }

    public static float OutCubic(float t)
    {
        t = 1f - t;
        return 1f - t * t * t;
    }

    public static float OutQuint(float t)
    {
        t = 1f - t;
        return 1f - t * t * t * t * t;
    }

    public static float InCubic(float t)
    {
        return t * t * t;
    }

    public static float InOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    // Overshoots a little before settling
    public static float OutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    public static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
