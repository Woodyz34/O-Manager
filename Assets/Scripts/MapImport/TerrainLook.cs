using System;

// What the terrain of a map looks like, chosen when the map is imported (or edited). The map file says where the
// forest, the marshes and the stones are, but not what the ground is made of or which trees grow there: a white
// forest on Gotland is bare limestone with thin grass, in Småland it is moss and blueberry under pine and spruce.
// The 3D view of a TV control (TV/TvWorldBuilder.cs) builds its world from this. It changes nothing about how the
// runners run, so it can be changed without building the map again.
namespace MapImport
{
    [Serializable]
    public class TerrainLook
    {
        // Ground under the trees: moss, heather, grass, hard, sand, stone, lichen
        public string ground = Moss;
        // Trees: mixed (pine and spruce), pine, spruce, birch, beech (beech and other broad-leaved forest)
        public string forest = Mixed;
        // 0 = clean forest floor, 0.5 = normal, 1 = a lot of undergrowth
        public float undergrowth = 0.5f;
        // The player's own words about the terrain. Saved for later; the game does not read it yet.
        public string text = "";

        public const string Moss = "moss", Heather = "heather", Grass = "grass", Hard = "hard", Sand = "sand",
            Stone = "stone", Lichen = "lichen";
        public const string Mixed = "mixed", Pine = "pine", Spruce = "spruce", Birch = "birch", Beech = "beech";
        public const int MaxTextLength = 400;

        public static readonly string[] Grounds = { Moss, Heather, Grass, Hard, Sand, Stone, Lichen };
        public static readonly string[] Forests = { Mixed, Pine, Spruce, Birch, Beech };

        public static string GroundName(string ground)
        {
            switch (ground)
            {
                case Heather: return "Heather";
                case Grass: return "Tall grass";
                case Hard: return "Hard, bare ground";
                case Sand: return "Sand";
                case Stone: return "Stone ground, short grass";
                case Lichen: return "Lichen and needles";
                default: return "Moss and blueberry";
            }
        }

        public static string ForestName(string forest)
        {
            switch (forest)
            {
                case Pine: return "Pine";
                case Spruce: return "Spruce";
                case Birch: return "Birch";
                case Beech: return "Broad-leaved";
                default: return "Mixed conifer";
            }
        }

        public TerrainLook Copy() => (TerrainLook)MemberwiseClone();

        // Values a file or another player sent: anything unknown becomes the normal look
        public TerrainLook Safe()
        {
            var look = new TerrainLook();
            if (Array.IndexOf(Grounds, ground) >= 0) look.ground = ground;
            if (Array.IndexOf(Forests, forest) >= 0) look.forest = forest;
            look.undergrowth = float.IsNaN(undergrowth) ? 0.5f : Math.Max(0f, Math.Min(1f, undergrowth));
            string t = text ?? "";
            look.text = t.Length > MaxTextLength ? t.Substring(0, MaxTextLength) : t;
            return look;
        }

        public bool IsDefault() => SameAs(new TerrainLook());

        public bool SameAs(TerrainLook o)
        {
            if (o == null) o = new TerrainLook();
            return (ground ?? Moss) == (o.ground ?? Moss) && (forest ?? Mixed) == (o.forest ?? Mixed)
                   && Math.Abs(undergrowth - o.undergrowth) < 0.005f && (text ?? "") == (o.text ?? "");
        }

        public string Describe()
        {
            string amount = undergrowth < 0.25f ? "little undergrowth" : undergrowth > 0.75f ? "a lot of undergrowth" : "normal undergrowth";
            return "Terrain look: " + ForestName(forest).ToLowerInvariant() + ", " + GroundName(ground).ToLowerInvariant() + ", " + amount + ".";
        }
    }
}
