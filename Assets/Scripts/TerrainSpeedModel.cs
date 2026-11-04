using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

// How hills change a runner's speed, depending on the ground.
//
// The grid comes with a speed factor per cell and direction, made by an older script as
//   downhill: 1 + x        uphill: 1 / (1 + 2x)
// where x grows with the height difference to the neighbouring cell. The factors in the grid describe one consistent
// height model (going round any square adds up to zero, and diagonals are divided by their longer distance), with
// x = 5 x gradient. This turns x back into a gradient and works out a new factor from it:
//
// Uphill: Minetti et al. 2002, "Energy cost of walking and running at extreme uphill and downhill slopes"
//   (J Appl Physiol 93:1039-1046). The energy a runner needs per metre is
//   C(i) = 155.4i^5 - 30.4i^4 - 43.3i^3 + 46.3i^2 + 19.5i + 3.6 J/kg/m, so at the same effort the speed is C(0) / C(i).
//   +5 % gives 77 % of the flat speed, +10 % 60 %, +20 % 40 %.
// Downhill: Minetti's cost keeps falling down to about -20 %, but runners can't use all of it: they have to brake
//   and watch their footing. Langmuir's correction to Naismith's rule says gentle descents are faster than flat ground
//   and descents steeper than about 12 degrees (21 %) are slower, and Strava's grade-adjusted pace (2017) has its
//   largest gain around -9 %. So the gain grows to a peak and then turns into a loss, and the ground decides how early:
//     road or path (grid speed above 1):   +15 % at -10 %, back to flat speed at -20 %
//     runnable forest or open land (1):     +8 % at  -7 %, back to flat speed at -14 %
//     slow forest, thickets (below 1):      +4 % at  -5 %, back to flat speed at -10 %
//   Steeper than that, every extra percent of gradient costs speed, fastest in rough forest, and very steep slopes
//   end at a quarter of the flat speed.
// The ground's own slowness (forest, thicket, marsh) is still the grid's base speed; this factor is multiplied on top.
public static class TerrainSpeedModel
{
    // x in the grid's old factors is this many times the gradient (rise / run)
    public const float OldFactorPerGradient = 5f;

    public const float MinFactor = 0.25f;
    const float MaxUphillGradient = 0.45f; // Minetti measured up to +/-45 %

    // Arrays already converted, so a grid that is cached and loaded twice is not converted twice
    static readonly ConditionalWeakTable<float[,], object> converted = new ConditionalWeakTable<float[,], object>();

    // Replaces the old slope factors with the new model, cell by cell, using the ground of the cell the runner leaves
    public static void Apply(float[,] speedGrid, Dictionary<string, float[,]> slopeFactors)
    {
        if (speedGrid == null || slopeFactors == null)
        {
            return;
        }
        int rows = speedGrid.GetLength(0);
        int cols = speedGrid.GetLength(1);
        foreach (float[,] factors in slopeFactors.Values)
        {
            if (factors == null || converted.TryGetValue(factors, out _))
            {
                continue;
            }
            converted.Add(factors, null);
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    float ground = speedGrid[i, j];
                    if (ground <= 0.01f)
                    {
                        continue; // nobody runs here
                    }
                    factors[i, j] = Factor(GradientFromOldFactor(factors[i, j]), ground);
                }
            }
        }
    }

    // Factors that are already in the new model (made from heights), so Apply leaves them alone
    public static void MarkConverted(float[,] factors)
    {
        if (factors != null && !converted.TryGetValue(factors, out _))
        {
            converted.Add(factors, null);
        }
    }

    // Positive = uphill
    public static float GradientFromOldFactor(float oldFactor)
    {
        if (oldFactor >= 1f)
        {
            return -(oldFactor - 1f) / OldFactorPerGradient;
        }
        float x = (1f / Mathf.Max(oldFactor, 0.0001f) - 1f) / 2f;
        return x / OldFactorPerGradient;
    }

    // Speed compared with flat ground, for a gradient (positive = uphill) on ground with this grid speed
    public static float Factor(float gradient, float groundSpeed)
    {
        if (gradient >= 0f)
        {
            float i = Mathf.Min(gradient, MaxUphillGradient);
            return Mathf.Max(MinFactor, EnergyCost(0f) / EnergyCost(i));
        }

        float down = -gradient;
        float peakGain, peakGradient, falloff;
        if (groundSpeed > 1.05f)
        {
            peakGain = 0.15f; peakGradient = 0.10f; falloff = 1.5f;   // road or path
        }
        else if (groundSpeed >= 0.95f)
        {
            peakGain = 0.08f; peakGradient = 0.07f; falloff = 2.5f;   // runnable forest, open land
        }
        else
        {
            peakGain = 0.04f; peakGradient = 0.05f; falloff = 3.0f;   // slow forest
        }

        float u = down / peakGradient;
        if (u <= 2f)
        {
            // Rises to the peak and falls back to flat speed at twice the peak gradient
            return 1f + peakGain * (2f * u - u * u);
        }
        return Mathf.Max(MinFactor, 1f - falloff * (down - 2f * peakGradient));
    }

    // Minetti et al. 2002, J/kg per metre of running at gradient i
    public static float EnergyCost(float i)
    {
        return (((((155.4f * i - 30.4f) * i - 43.3f) * i + 46.3f) * i + 19.5f) * i) + 3.6f;
    }
}
