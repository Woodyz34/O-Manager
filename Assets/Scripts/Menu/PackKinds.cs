using UnityEngine;

// The three kinds of runner pack (2026-11-24, the user): Bronze is the one bought in the shop, Silver is the daily
// contest prize and Golden is the weekly contest prize. Each has its own metal look, so the player knows which pack
// is being opened. The pictures are Resources/UI/Packs/<kind>_pack*.png.
public static class PackKinds
{
    public const string Bronze = "bronze";
    public const string Silver = "silver";
    public const string Gold = "gold";

    public static string Name(string kind) => kind == Gold ? "Golden pack" : kind == Silver ? "Silver pack" : "Bronze pack";

    // The colour of the light around the pack while it waits and shakes
    public static Color Light(string kind)
    {
        if (kind == Gold) return new Color(1f, 0.86f, 0.38f, 1f);
        if (kind == Silver) return new Color(0.86f, 0.92f, 1f, 1f);
        return new Color(1f, 0.70f, 0.46f, 1f);
    }

    // Colour of the words that name the pack
    public static Color Text(string kind)
    {
        if (kind == Gold) return new Color(1f, 0.90f, 0.45f, 1f);
        if (kind == Silver) return new Color(0.90f, 0.94f, 1f, 1f);
        return new Color(1f, 0.78f, 0.58f, 1f);
    }

    public static Sprite Pack(string kind) => Resources.Load<Sprite>("UI/Packs/" + kind + "_pack");
}
