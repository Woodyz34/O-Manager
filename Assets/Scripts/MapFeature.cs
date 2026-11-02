using System.Collections.Generic;
using UnityEngine;

public class MapFeature : MonoBehaviour
{
    public string featureType; // t.ex. "Sten", "Stig", "Sankmark"
    public float area;         // om polygon  yta
    public float length;       // om linje  längd
    public float size;         // om punkt  ev. storlek

    // Färdigtvättade varianter av featureType. De räknas ut en gång per objekt i stället för vid varje kollision,
    // eftersom löparen träffar väldigt många objekt medan den springer.
    private string cachedFeatureType;
    private string nameAfterFirstSpace;
    private string nameAfterPrefix;

    private static readonly Dictionary<string, string> withoutSpacesCache = new Dictionary<string, string>();

    // featureType utan allt fram till första mellanslaget (inklusive det) och utan mellanslag
    public string NameAfterFirstSpace
    {
        get
        {
            RefreshCachedNames();
            return nameAfterFirstSpace;
        }
    }

    // featureType utan de 6 första tecknen och utan mellanslag
    public string NameAfterPrefix
    {
        get
        {
            RefreshCachedNames();
            return nameAfterPrefix;
        }
    }

    private void RefreshCachedNames()
    {
        if (nameAfterFirstSpace != null && ReferenceEquals(cachedFeatureType, featureType))
        {
            return;
        }
        cachedFeatureType = featureType;
        string name = featureType ?? "";

        int spaceIndex = name.IndexOf(' ');
        string afterSpace = spaceIndex >= 0 ? name.Substring(spaceIndex + 1) : name;
        nameAfterFirstSpace = afterSpace.Replace(" ", "").Trim();

        string afterPrefix = name.Length > 6 ? name.Substring(6) : name;
        nameAfterPrefix = afterPrefix.Replace(" ", "");
    }

    public static string WithoutSpaces(string objectName)
    {
        if (!withoutSpacesCache.TryGetValue(objectName, out string cleaned))
        {
            cleaned = objectName.Replace(" ", "").Trim();
            withoutSpacesCache[objectName] = cleaned;
        }
        return cleaned;
    }
}
