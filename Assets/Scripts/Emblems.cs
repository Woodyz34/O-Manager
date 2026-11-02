using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Finds the pictures that go on a runner card by name:
//   Resources/Emblem/Flaggor   one picture per country, named after the country,
//                              e.g. "Denmark.png" or "Danmark.png"
//   Resources/Emblem/Klubbar   one picture per club, named exactly like the club in Eventor,
//                              e.g. "OK Denseln.png" (the club's short form also works when it
//                              is the first word in capitals, e.g. "IKHP.png")
//   Resources/Profilbilder     real photos, named exactly like the runner, e.g. "Tove Alexandersson.jpg"
//   Resources/Basbilder        placeholder portraits, "Herr_..." and "Dam_...", used until a
//                              runner has a real photo
// Capitals, spaces and dashes in the file names do not matter. Pictures are looked up once and kept.
public static class Emblems
{
    const string FlagFolder = "Emblem/Flaggor";
    const string ClubFolder = "Emblem/Klubbar";
    const string PortraitFolder = "Basbilder";
    const string PhotoFolder = "Profilbilder";

    static Dictionary<string, Sprite> flags;
    static Dictionary<string, Sprite> clubs;
    static Dictionary<string, Sprite> photos;
    static List<Sprite> men;
    static List<Sprite> women;

    public static Sprite Flag(string country)
    {
        if (string.IsNullOrEmpty(country)) return null;
        if (flags == null)
        {
            flags = new Dictionary<string, Sprite>();
            foreach (Sprite sprite in LoadFolder(FlagFolder))
            {
                // Swedish file names work too: "Danmark" counts as "DENMARK"
                flags[Key(RunnerCards.EnglishCountry(sprite.name))] = sprite;
                flags[Key(sprite.name)] = sprite;
            }
        }
        Sprite found;
        return flags.TryGetValue(Key(country), out found) ? found : null;
    }

    public static Sprite Club(string club)
    {
        if (string.IsNullOrEmpty(club)) return null;
        if (clubs == null)
        {
            clubs = new Dictionary<string, Sprite>();
            foreach (Sprite sprite in LoadFolder(ClubFolder))
            {
                clubs[Key(sprite.name)] = sprite;
            }
        }
        Sprite found;
        if (clubs.TryGetValue(Key(club), out found)) return found;

        // "IKHP Huskvarna Idrottsklubb" also finds "IKHP.png"
        string first = club.Trim().Split(' ')[0];
        if (first.Length >= 3 && first == first.ToUpperInvariant() && clubs.TryGetValue(Key(first), out found))
        {
            return found;
        }
        return null;
    }

    // A real photo of the runner, or null. "Anna Svensson (OK Denseln)" also finds "Anna Svensson.jpg".
    public static Sprite Photo(string runnerName)
    {
        if (string.IsNullOrEmpty(runnerName)) return null;
        if (photos == null)
        {
            photos = new Dictionary<string, Sprite>();
            foreach (Sprite sprite in LoadFolder(PhotoFolder))
            {
                photos[Key(sprite.name)] = sprite;
            }
        }
        Sprite found;
        if (photos.TryGetValue(Key(runnerName), out found)) return found;
        int bracket = runnerName.IndexOf(" (");
        if (bracket > 0 && photos.TryGetValue(Key(runnerName.Substring(0, bracket)), out found)) return found;
        return null;
    }

    // The same runner always gets the same placeholder portrait
    public static Sprite Portrait(string seed, bool man)
    {
        if (men == null)
        {
            men = new List<Sprite>();
            women = new List<Sprite>();
            foreach (Sprite sprite in LoadFolder(PortraitFolder))
            {
                string name = sprite.name.ToLowerInvariant();
                if (name.StartsWith("herr")) men.Add(sprite);
                else if (name.StartsWith("dam")) women.Add(sprite);
            }
            men.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            women.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }
        List<Sprite> list = man ? men : women;
        if (list.Count == 0) list = man ? women : men;
        if (list.Count == 0) return null;
        return list[(int)(StableHash(seed ?? "") % (uint)list.Count)];
    }

    // Lower case letters and digits only, so "Great Britain", "GREAT-BRITAIN" and "greatbritain" match
    public static string Key(string text)
    {
        StringBuilder key = new StringBuilder(text.Length);
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) key.Append(c);
        }
        return key.ToString();
    }

    // Pictures dropped in the folder may be imported as plain textures, or cut into several
    // sprites ("Norway_0", "Norway_1"...). Those get one sprite of the whole picture instead.
    static List<Sprite> LoadFolder(string folder)
    {
        List<Sprite> result = new List<Sprite>();
        HashSet<string> names = new HashSet<string>();
        foreach (Sprite sprite in Resources.LoadAll<Sprite>(folder))
        {
            Texture2D texture = sprite.texture;
            bool whole = texture == null || sprite.name == texture.name;
            if (whole && names.Add(sprite.name))
            {
                result.Add(sprite);
            }
        }
        foreach (Texture2D texture in Resources.LoadAll<Texture2D>(folder))
        {
            if (names.Add(texture.name))
            {
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = texture.name;
                result.Add(sprite);
            }
        }
        return result;
    }

    // Keeps a name with a dash on one line, so "Tolered-Utby Ol-klubb" is not split at "Ol-"
    public static string NoBreakAtDash(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string[] words = text.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Contains("-")) words[i] = "<nobr>" + words[i] + "</nobr>";
        }
        return string.Join(" ", words);
    }

    static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash = (hash ^ c) * 16777619;
        }
        return hash;
    }
}
