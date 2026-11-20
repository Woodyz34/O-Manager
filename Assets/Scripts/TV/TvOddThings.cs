using System;
using MapImport;

// The X on the map (a special man-made feature, ISOM 530/531) as something odd that people have left there
// (2026-11-20, Anders: "X är lite speciella"). Which thing it is comes from the place, so everyone sees the same.
//  - In the forest: a rusty old car, a hut of branches and planks someone built, an old bathtub, a shopping trolley,
//    a pile of tyres, an old fridge.
//  - In a town on grass or in a yard: a playground thing (a swing, a slide, a seesaw, a climbing frame).
//  - In a town on a street or square: a statue, a red telephone box, a big flower pot with a tree, a sculpture.
public partial class TvWorldBuilder
{
    void OddThing(float x, float y, float z)
    {
        int hx = (int)Math.Floor((x + ox) * 2f), hz = (int)Math.Floor((z + oz) * 2f);
        random.Reseed(hx, hz, 5300);
        float pick = TvRandom.Hash(hx + 17, hz - 91), yaw = random.Range(0f, 6.283f);
        var b = new TvShapeBuilder(random);
        float r;
        if (input.town == null) r = ForestOdd(b, (int)(pick * 6f));
        else
        {
            bool street = Base(CellAt(x, z)) == WorldCell.Paved;
            r = street ? StreetOdd(b, (int)(pick * 4f)) : PlayOdd(b, (int)(pick * 4f));
        }
        thingMesh.Add(b.Done(), x, y, z, 1f, 1f, 1f, yaw);
        world.solids.Add(new TvSolid { x = x, z = z, r = r });
    }

    static readonly TvColor Rust = TvColor.Hex("#7a4a2c"), RustDark = TvColor.Hex("#4e3424"), Moss = TvColor.Hex("#5d6b3a"),
        Plank = TvColor.Hex("#7d6a52"), Steel = TvColor.Hex("#8c9094"), Tyre = TvColor.Hex("#222222"), Enamel = TvColor.Hex("#e6e2d8");

    // Returns how wide it is (the runner goes round)
    float ForestOdd(TvShapeBuilder b, int which)
    {
        switch (which)
        {
            case 0:
            {
                // A rusty old car sunk on its wheel rims, the paint gone, moss on the roof
                TvColor paint = random.Value() < 0.5f ? TvColor.Hex("#6f7d6a") : TvColor.Hex("#8a5a3c");
                b.Box(-2f, 0.15f, -0.8f, 2f, 0.85f, 0.8f, Rust.Lerp(paint, 0.35f));
                b.Box(-1.05f, 0.85f, -0.72f, 0.75f, 1.4f, 0.72f, Rust.Lerp(paint, 0.25f));
                b.Box(-1.0f, 0.9f, -0.74f, 0.7f, 1.32f, 0.74f, TvColor.Hex("#1d2020"));   // empty windows
                foreach (float wx in new[] { -1.0f, 0.7f })
                    b.Box(wx - 0.08f, 0.9f, -0.75f, wx + 0.08f, 1.36f, 0.75f, RustDark);
                b.Box(-1.08f, 1.4f, -0.75f, 0.78f, 1.48f, 0.75f, Moss);
                b.Box(-2.02f, 0.45f, -0.6f, -1.98f, 0.62f, 0.6f, RustDark);   // the grille
                foreach (float wx in new[] { -1.3f, 1.3f })
                    foreach (float wz in new[] { -0.82f, 0.82f })
                        b.Box(wx - 0.3f, -0.05f, wz - 0.06f, wx + 0.3f, 0.45f, wz + 0.06f, RustDark);
                b.Box(1.2f, 0.85f, -0.75f, 2f, 0.9f, 0.1f, Moss.Shade(0.9f));   // moss on the bonnet
                return 2.1f;
            }
            case 1:
            {
                // A hut someone built: branches leaning on a ridge pole, planks for a door, a rag of a flag
                float len = 2.6f;
                b.Limb(-len / 2f, 0f, 0f, -len / 2f, 1.7f, 0f, 0.07f, 0.06f, 5, Plank);
                b.Limb(len / 2f, 0f, 0f, len / 2f, 1.7f, 0f, 0.07f, 0.06f, 5, Plank);
                b.Limb(-len / 2f - 0.2f, 1.65f, 0f, len / 2f + 0.2f, 1.65f, 0f, 0.07f, 0.07f, 5, Plank);
                for (float u = -len / 2f; u <= len / 2f + 0.01f; u += 0.22f)
                    foreach (float s in new[] { -1f, 1f })
                        b.Limb(u + random.Range(-0.05f, 0.05f), -0.05f, s * 1.25f, u, 1.75f, -s * 0.08f, 0.05f, 0.035f, 4,
                               TvColor.Hex("#5e5346").Shade(random.Range(0.85f, 1.15f)));
                b.Box(len / 2f + 0.02f, 0f, -0.5f, len / 2f + 0.07f, 1.1f, 0.5f, Plank.Shade(1.1f));
                b.Limb(-len / 2f - 0.2f, 1.65f, 0f, -len / 2f - 0.3f, 2.5f, 0f, 0.025f, 0.02f, 4, Plank);
                b.Box(-len / 2f - 0.3f, 2.15f, 0f, -len / 2f - 0.29f, 2.45f, 0.4f, TvColor.Hex("#c0392b"));
                return 1.6f;
            }
            case 2:
            {
                // An old bathtub on lion feet, half full of rain
                b.Box(-0.8f, 0.2f, -0.38f, 0.8f, 0.75f, 0.38f, Enamel.Shade(0.92f));
                b.Box(-0.72f, 0.4f, -0.3f, 0.72f, 0.76f, 0.3f, TvColor.Hex("#4a5a52"));   // the rainwater
                b.Box(-0.82f, 0.72f, -0.4f, 0.82f, 0.8f, 0.4f, Enamel);
                b.Box(-0.7f, 0.42f, -0.28f, 0.7f, 0.745f, 0.28f, TvColor.Hex("#3d4d47"));
                foreach (float fx in new[] { -0.65f, 0.65f })
                    foreach (float fz in new[] { -0.28f, 0.28f })
                        b.Box(fx - 0.06f, 0f, fz - 0.06f, fx + 0.06f, 0.22f, fz + 0.06f, TvColor.Hex("#b08a3c"));
                return 0.9f;
            }
            case 3:
            {
                // A shopping trolley, far from any shop
                float h = 0.55f;
                for (float u = -0.45f; u <= 0.46f; u += 0.09f)
                    b.Limb(u, 0.05f, -0.3f, u, h, -0.3f, 0.012f, 0.012f, 3, Steel);
                for (float v = 0.05f; v <= h + 0.01f; v += 0.1f)
                    b.Limb(-0.45f, v, -0.3f, 0.45f, v, -0.3f, 0.012f, 0.012f, 3, Steel);
                for (float u = -0.45f; u <= 0.46f; u += 0.15f)
                    b.Limb(u, 0.05f, -0.3f, u, 0.05f, 0.3f, 0.012f, 0.012f, 3, Steel);
                b.Limb(-0.45f, h, -0.3f, -0.45f, h, 0.35f, 0.02f, 0.02f, 4, Steel);
                b.Limb(0.45f, h, -0.3f, 0.45f, h, 0.35f, 0.02f, 0.02f, 4, Steel);
                b.Limb(-0.45f, h, 0.35f, 0.45f, h, 0.35f, 0.03f, 0.03f, 5, TvColor.Hex("#c0392b"));   // the handle
                foreach (float u in new[] { -0.4f, 0.4f })
                    b.Box(u - 0.05f, 0f, 0.35f, u + 0.05f, 0.12f, 0.45f, Tyre);
                return 0.6f;
            }
            case 4:
            {
                // A pile of old tyres, grass growing through
                int n = 3 + (int)(random.Value() * 3f);
                for (int i = 0; i < n; i++)
                {
                    float dx = random.Range(-0.15f, 0.15f), dz = random.Range(-0.15f, 0.15f);
                    b.Tube(0.36f, 0.36f, 0.22f, i * 0.22f, 10, Tyre.Shade(random.Range(0.9f, 1.2f)), null, 0f, 1f, dx, dz);
                    b.Tube(0.2f, 0.2f, 0.22f, i * 0.22f, 8, TvColor.Hex("#151515"), null, 0f, 1f, dx, dz);
                }
                b.Box(-0.1f, n * 0.22f - 0.02f, -0.1f, 0.1f, n * 0.22f + 0.3f, 0.1f, Moss.Shade(1.2f));   // grass in the middle
                b.Tube(0.36f, 0.36f, 0.22f, 0f, 10, Tyre, null, 0f, 1f, 0.75f, 0.1f);
                return 0.9f;
            }
            default:
            {
                // An old fridge, its door hanging open
                b.Box(-0.32f, 0f, -0.3f, 0.32f, 1.45f, 0.3f, Enamel.Shade(0.9f));
                b.Box(-0.28f, 0.05f, -0.31f, 0.28f, 1.4f, -0.29f, TvColor.Hex("#5c5a54"));
                b.Box(-0.33f, 0.02f, -0.92f, -0.29f, 1.43f, -0.32f, Enamel.Shade(0.95f));   // the open door
                b.Box(-0.2f, 1.15f, -0.31f, 0.2f, 1.17f, 0.25f, Steel);
                b.Box(-0.2f, 0.75f, -0.31f, 0.2f, 0.77f, 0.25f, Steel);
                b.Box(-0.33f, 0f, -0.31f, 0.33f, 0.25f, 0.31f, RustDark);   // rust at the foot
                return 0.6f;
            }
        }
    }

    static readonly TvColor PlayRed = TvColor.Hex("#d23c32"), PlayYellow = TvColor.Hex("#f0c02a"), PlayBlue = TvColor.Hex("#2f74c0"),
        PlayGreen = TvColor.Hex("#3f9a4a"), Sandbox = TvColor.Hex("#d8c79a");

    float PlayOdd(TvShapeBuilder b, int which)
    {
        TvColor main = (which + (int)(random.Value() * 3f)) % 3 == 0 ? PlayRed : random.Value() < 0.5f ? PlayBlue : PlayGreen;
        switch (which)
        {
            case 0:
            {
                // A swing: an A-frame at each end, a beam, two seats on chains
                foreach (float u in new[] { -1.4f, 1.4f })
                {
                    b.Limb(u, 0f, -0.9f, u, 2.3f, 0f, 0.06f, 0.06f, 6, main);
                    b.Limb(u, 0f, 0.9f, u, 2.3f, 0f, 0.06f, 0.06f, 6, main);
                }
                b.Limb(-1.5f, 2.3f, 0f, 1.5f, 2.3f, 0f, 0.07f, 0.07f, 6, main.Shade(0.85f));
                foreach (float u in new[] { -0.6f, 0.6f })
                {
                    foreach (float s in new[] { -0.2f, 0.2f })
                        b.Limb(u + s, 2.3f, 0f, u + s, 0.45f, 0f, 0.01f, 0.01f, 3, Steel);
                    b.Box(u - 0.24f, 0.4f, -0.12f, u + 0.24f, 0.46f, 0.12f, Tyre);
                }
                b.Box(-1.2f, -0.02f, -0.6f, 1.2f, 0.03f, 0.6f, Sandbox);
                return 1.5f;
            }
            case 1:
            {
                // A slide: a ladder up to a little platform, the yellow chute down
                b.Box(-0.4f, 1.4f, -0.4f, 0.4f, 1.5f, 0.4f, Plank);
                foreach (float sx in new[] { -0.4f, 0.4f })
                    foreach (float sz in new[] { -0.4f, 0.4f })
                        b.Box(sx - 0.05f, 0f, sz - 0.05f, sx + 0.05f, 2.2f, sz + 0.05f, main);
                b.Box(-0.42f, 2.1f, -0.42f, 0.42f, 2.25f, 0.42f, main.Shade(0.85f));
                for (float v = 0.3f; v < 1.45f; v += 0.3f)
                    b.Box(-0.3f, v, -0.75f + v * 0.15f, 0.3f, v + 0.05f, -0.6f + v * 0.15f, Plank);
                b.Limb(-0.3f, 0f, -0.9f, -0.3f, 1.45f, -0.45f, 0.03f, 0.03f, 4, Steel);
                b.Limb(0.3f, 0f, -0.9f, 0.3f, 1.45f, -0.45f, 0.03f, 0.03f, 4, Steel);
                // The chute
                b.Limb(0f, 1.45f, 0.45f, 0f, 0.25f, 2.4f, 0.32f, 0.32f, 8, PlayYellow);
                return 1.4f;
            }
            case 2:
            {
                // A seesaw with two handles
                b.Box(-0.15f, 0f, -0.2f, 0.15f, 0.45f, 0.2f, main);
                b.Limb(-1.8f, 0.2f, 0f, 1.8f, 0.7f, 0f, 0.09f, 0.09f, 4, PlayYellow);
                b.Limb(-1.45f, 0.25f, 0f, -1.45f, 0.65f, 0f, 0.025f, 0.025f, 4, Steel);
                b.Limb(1.45f, 0.65f, 0f, 1.45f, 1.05f, 0f, 0.025f, 0.025f, 4, Steel);
                b.Box(-1.9f, -0.02f, -0.3f, -1.6f, 0.2f, 0.3f, Tyre);
                return 1.6f;
            }
            default:
            {
                // A climbing frame: a cube of bars, a roof of colour on top
                float s = 0.9f, h = 1.8f;
                foreach (float sx in new[] { -s, s })
                    foreach (float sz in new[] { -s, s })
                        b.Limb(sx, 0f, sz, sx, h, sz, 0.04f, 0.04f, 5, main);
                for (float v = 0.45f; v <= h + 0.01f; v += 0.45f)
                {
                    b.Limb(-s, v, -s, s, v, -s, 0.025f, 0.025f, 4, Steel);
                    b.Limb(-s, v, s, s, v, s, 0.025f, 0.025f, 4, Steel);
                    b.Limb(-s, v, -s, -s, v, s, 0.025f, 0.025f, 4, Steel);
                    b.Limb(s, v, -s, s, v, s, 0.025f, 0.025f, 4, Steel);
                }
                b.Triangle(-s, h, -s, s, h, -s, 0f, h + 0.7f, 0f, 0f, h, 0f, PlayRed);
                b.Triangle(s, h, -s, s, h, s, 0f, h + 0.7f, 0f, 0f, h, 0f, PlayYellow);
                b.Triangle(s, h, s, -s, h, s, 0f, h + 0.7f, 0f, 0f, h, 0f, PlayRed);
                b.Triangle(-s, h, s, -s, h, -s, 0f, h + 0.7f, 0f, -0f, h, 0f, PlayYellow);
                return 1.2f;
            }
        }
    }

    float StreetOdd(TvShapeBuilder b, int which)
    {
        switch (which)
        {
            case 0:
            {
                // A statue on a stone plinth: a figure of green bronze with a raised arm
                TvColor stone = TvColor.Hex("#a29d93"), bronze = TvColor.Hex("#4f8a74");
                b.Box(-0.6f, 0f, -0.6f, 0.6f, 1.3f, 0.6f, stone);
                b.Box(-0.7f, 1.3f, -0.7f, 0.7f, 1.42f, 0.7f, stone.Shade(1.08f));
                b.Limb(-0.12f, 1.42f, 0f, -0.1f, 2.3f, 0f, 0.09f, 0.08f, 6, bronze);
                b.Limb(0.12f, 1.42f, 0f, 0.1f, 2.3f, 0f, 0.09f, 0.08f, 6, bronze);
                b.Limb(0f, 2.25f, 0f, 0f, 3.0f, 0f, 0.22f, 0.18f, 7, bronze);
                b.Blob(0.17f, 1f, 1.1f, 1f, 0f, 3.2f, 0f, 0f, 1, false, bronze.Shade(1.05f));
                b.Limb(0.18f, 2.9f, 0f, 0.35f, 3.6f, 0.1f, 0.06f, 0.05f, 5, bronze);
                b.Limb(-0.18f, 2.9f, 0f, -0.3f, 2.4f, 0.05f, 0.06f, 0.05f, 5, bronze);
                return 0.8f;
            }
            case 1:
            {
                // A red telephone box, long out of use
                TvColor red = TvColor.Hex("#c0281e");
                b.Box(-0.5f, 0f, -0.5f, 0.5f, 2.4f, 0.5f, red);
                foreach (float s in new[] { -1f, 1f })
                {
                    b.Box(-0.38f, 0.9f, s * 0.5f - 0.01f, 0.38f, 2.1f, s * 0.5f + 0.01f, TvColor.Hex("#3d4a4f"));
                    b.Box(s * 0.5f - 0.01f, 0.9f, -0.38f, s * 0.5f + 0.01f, 2.1f, 0.38f, TvColor.Hex("#3d4a4f"));
                }
                b.Box(-0.55f, 2.4f, -0.55f, 0.55f, 2.55f, 0.55f, red.Shade(0.9f));
                b.Box(-0.4f, 2.2f, -0.52f, 0.4f, 2.33f, 0.52f, Enamel);
                return 0.7f;
            }
            case 2:
            {
                // A big round flower pot with a little tree and flowers
                b.Tube(0.9f, 1.0f, 0.7f, 0f, 12, TvColor.Hex("#8a8278"));
                b.Box(-0.75f, 0.6f, -0.75f, 0.75f, 0.72f, 0.75f, TvColor.Hex("#4a3a2c"));
                for (int i = 0; i < 10; i++)
                {
                    float a = i * 0.628f;
                    b.Blob(0.14f, 1f, 0.8f, 1f, (float)Math.Cos(a) * 0.6f, 0.8f, (float)Math.Sin(a) * 0.6f, 0.1f, 0, true,
                           i % 2 == 0 ? TvColor.Hex("#e23d5a") : TvColor.Hex("#f2c230"));
                }
                b.Limb(0f, 0.7f, 0f, 0f, 2.2f, 0f, 0.06f, 0.05f, 5, TvColor.Hex("#5e4a36"));
                b.Blob(0.8f, 1f, 0.9f, 1f, 0f, 2.6f, 0f, 0.2f, 1, true, TvColor.Hex("#4f8a3a"));
                return 1.0f;
            }
            default:
            {
                // A sculpture: a big ring of steel standing on its edge, and a bright ball beside it
                for (int i = 0; i < 16; i++)
                {
                    float a0 = i / 16f * 6.283f, a1 = (i + 1) / 16f * 6.283f;
                    b.Limb((float)Math.Cos(a0) * 1.1f, 1.2f + (float)Math.Sin(a0) * 1.1f, 0f,
                           (float)Math.Cos(a1) * 1.1f, 1.2f + (float)Math.Sin(a1) * 1.1f, 0f, 0.12f, 0.12f, 6, Steel.Shade(1.1f));
                }
                b.Box(-0.4f, 0f, -0.3f, 0.4f, 0.15f, 0.3f, TvColor.Hex("#6d6a64"));
                b.Blob(0.4f, 1f, 1f, 1f, 1.6f, 0.4f, 0.4f, 0f, 1, false, PlayYellow);
                return 1.3f;
            }
        }
    }
}
