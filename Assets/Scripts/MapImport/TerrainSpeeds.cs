using System;

// How quick the different kinds of ground are on one map, chosen with sliders when the map is imported (or edited).
// Terrain differs a lot between places: in Portugal the dark green is thorny scrub nobody gets through and the
// roads are loose sand, while Swedish white forest can be almost as quick as a road. Every value is a slider
// position from 0 to 1; 0.5 (the middle) is the game's normal value, so a slider nobody touched changes nothing.
// Each slider's ends are named after the neighbouring kind of ground ("as slow as mid green" ... "as quick as
// white forest"), so the player does not have to think in numbers.
//
// The speeds are baked into the map's grid when it is built, so every computer that builds the map from its file
// (shared maps, private maps in a lobby, maps built into the game) has to get the same values: they travel with
// MapPackageInfo, SharedMaps and PrivateMaps.
namespace MapImport
{
    // What a symbol is, for the sliders
    public enum TerrainKind
    {
        Other,      // not changed by any slider
        Road,       // roads and vehicle tracks
        Path,       // footpaths, small paths, rides
        Forest,     // white, runnable forest
        LightGreen, // slow running
        MidGreen,   // difficult to run
        DarkGreen,  // very difficult to run
        Marsh,
        Open,       // open land, fields: only the overall slider
        Stony       // stony ground and boulder fields: slows down whatever ground it lies on
    }

    [Serializable]
    public class TerrainSpeeds
    {
        public float overall = 0.5f;     // 0 = very tough terrain, 1 = easy terrain
        public float roads = 0.5f;       // 0 = as slow as white forest, 1 = as quick as asphalt
        public float paths = 0.5f;       // 0 = as slow as white forest, 1 = as quick as a vehicle track
        public float forest = 0.5f;      // 0 = a fight for every step, 1 = as quick as a road
        public float lightGreen = 0.5f;  // 0 = as slow as mid green, 1 = as quick as white forest
        public float midGreen = 0.5f;    // 0 = as slow as dark green, 1 = as quick as light green
        public float darkGreen = 0.5f;   // 0 = nobody gets through, 1 = as quick as mid green
        public float marsh = 0.5f;       // 0 = very heavy, 1 = as quick as white forest
        public float stony = 0.5f;       // 0 = very slow, 1 = no slower than the ground under it

        public const float Middle = 0.5f;

        // The game's normal speeds (white forest = 1), the middle of each slider
        public const float NormalForest = 1.0f;
        public const float NormalLight = 0.7f;
        public const float NormalMid = 0.6f;
        public const float NormalDark = 0.35f;
        public const float NormalMarsh = 0.8f;

        public static TerrainSpeeds Default => new TerrainSpeeds();

        public TerrainSpeeds Copy() => (TerrainSpeeds)MemberwiseClone();

        public bool IsDefault() =>
            Same(overall) && Same(roads) && Same(paths) && Same(forest) && Same(lightGreen)
            && Same(midGreen) && Same(darkGreen) && Same(marsh) && Same(stony);

        static bool Same(float v) => Math.Abs(v - Middle) < 0.005f;

        public bool SameAs(TerrainSpeeds o)
        {
            if (o == null) return IsDefault();
            return Math.Abs(overall - o.overall) < 0.005f && Math.Abs(roads - o.roads) < 0.005f && Math.Abs(paths - o.paths) < 0.005f
                   && Math.Abs(forest - o.forest) < 0.005f && Math.Abs(lightGreen - o.lightGreen) < 0.005f
                   && Math.Abs(midGreen - o.midGreen) < 0.005f && Math.Abs(darkGreen - o.darkGreen) < 0.005f
                   && Math.Abs(marsh - o.marsh) < 0.005f && Math.Abs(stony - o.stony) < 0.005f;
        }

        // A slider: lo at 0, normal in the middle, hi at 1, straight lines in between
        public static float Slide(float t, float lo, float normal, float hi)
        {
            t = Clamp01(t);
            return t < 0.5f ? lo + (normal - lo) * (t * 2f) : normal + (hi - normal) * ((t - 0.5f) * 2f);
        }

        static float Clamp01(float v) => float.IsNaN(v) ? 0.5f : v < 0f ? 0f : v > 1f ? 1f : v;

        // ---- The speeds the sliders give ----

        // The overall slider multiplies all off-path ground
        public float OverallFactor() => Slide(overall, 0.6f, 1f, 1.25f);

        // White forest before the overall factor
        float ForestBase() => Slide(forest, 0.45f, NormalForest, 1.45f);

        // White forest as it ends up: what the road and path sliders' slow end means
        public float ForestSpeed() => ForestBase() * OverallFactor();

        // Green and marsh are set compared with white forest, so a quick or slow white forest moves them along
        float LightShare() => Slide(lightGreen, NormalMid, NormalLight, NormalForest);
        float MidShare() => Slide(midGreen, NormalDark, NormalMid, NormalLight);
        float MarshShare() => Slide(marsh, 0.4f, NormalMarsh, NormalForest);

        // Dark green: the very left of the slider means nobody gets through (0)
        float DarkShare() => darkGreen <= 0.03f ? 0f : Slide(darkGreen, 0.06f, NormalDark, NormalMid);

        // The speed of a ground area. normal: the speed the symbol has at the middle of every slider.
        public float Ground(TerrainKind kind, float normal)
        {
            // Each kind is scaled from its normal value, so symbols of one kind that differ a little keep doing so
            // (open land 1.3, open land with scattered trees 1.2)
            float f = OverallFactor();
            float w = ForestBase() / NormalForest;
            switch (kind)
            {
                case TerrainKind.Forest: return normal * w * f;
                case TerrainKind.LightGreen: return normal * (LightShare() / NormalLight) * w * f;
                case TerrainKind.MidGreen: return normal * (MidShare() / NormalMid) * w * f;
                case TerrainKind.DarkGreen: return normal * (DarkShare() / NormalDark) * w * f;
                case TerrainKind.Marsh: return normal * (MarshShare() / NormalMarsh) * w * f;
                case TerrainKind.Open: return normal * f;
                default: return normal;
            }
        }

        // The speed along a road or path. normal: the line's speed at the middle of the sliders.
        public float Line(TerrainKind kind, float normal)
        {
            float slowest = Math.Min(ForestSpeed(), normal);
            if (kind == TerrainKind.Road) return Slide(roads, slowest, normal, normal * 1.12f);
            if (kind == TerrainKind.Path)
            {
                // The quick end: a vehicle track as the road slider makes it
                float track = Math.Max(normal, Line(TerrainKind.Road, 1.55f));
                return Slide(paths, slowest, normal, track);
            }
            return normal;
        }

        // Stony ground multiplies the speed of the ground under it. normal: its factor in the middle (0.85 = 15 % slower).
        public float StonyFactor(float normal)
        {
            return Slide(stony, normal * 0.5f, normal, 1f);
        }
    }
}
