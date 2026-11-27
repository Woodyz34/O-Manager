using System;
using System.Threading.Tasks;
using UnityEngine;

// The ground of a map as the run-yourself mode and the TV controls both build it (2026-11-27, Anders: the TV controls
// must run the same way as 3D run, and get every update of the ground too). ONE place decides which heights and edge
// lines the 3D world gets: change the ground here (or in RunContours) and both the run-yourself world and the TV
// worlds follow. Nothing else should read TvWorldData's plain 10 m grid for a 3D world.
public static class RunGround
{
    static TvWorldData.Data kept;       // the last result: a relay builds a world for every TV control of one map
    static string keptKey;

    // The map's world data with the detailed ground from the contours and the edge lines attached.
    // progress (optional): a text for the loading card, called on the main thread.
    public static async Task<TvWorldData.Data> LoadAsync(MapDefinition mapDef, TvWorldData.Data plain, Action<string, float> progress = null)
    {
        if (plain == null || plain.layer == null || mapDef == null) return plain;
        byte[] linesJson = TvWorldData.LinesJson(mapDef), pointsJson = TvWorldData.PointsJson(mapDef);
        string key = mapDef.id + "|" + (linesJson != null ? linesJson.Length : 0) + "|" + (pointsJson != null ? pointsJson.Length : 0)
                     + "|" + RunSettings.Hills.ToString("0.00");
        if (kept != null && keptKey == key && ReferenceEquals(kept.layer, plain.layer)
            && kept.look == plain.look && kept.town == plain.town) return kept;

        TvWorldData.Data data = WithHills(plain, RunSettings.Hills);
        // The knolls and hollows the mapper drew with contours and form lines, which the 10 m height grid rounds away
        if (progress != null) progress("Shaping the ground from the contours (the first time on a map takes a little while)", 0.06f);
        float interval = TvWorldData.ContourInterval(mapDef);
        string mapId = mapDef.id;
        // The ground from the contours is kept on disk; a new version of the map's lines (an import) gets a new file
        string cacheFile = System.IO.Path.Combine(Application.persistentDataPath, "RunHeights",
            Safe(mapId) + "-" + (linesJson != null ? linesJson.Length : 0) + "-" + (pointsJson != null ? pointsJson.Length : 0) + "-v4.bytes");
        TvHeights detailed = data.heights;
        // The map's edge lines of areas (banks, boundaries, paved edges): the 3D ground follows them up close
        try { await Task.Run(() => TvEdges.Attach(plain.layer, linesJson)); }
        catch (Exception e) { Debug.LogWarning("RunGround: edges: " + e); }
        // Made once with the map (import, Edit map, a shared map's download, the built-in maps: runheights.bytes); worked
        // out here only for a map built before 2026-11-25, and then kept on this computer
        byte[] baked = TvWorldData.RunHeightsBytes(mapDef);
        TvHeights fromPackage = null;
        if (baked != null)
        {
            try { fromPackage = await Task.Run(() => RunContours.FromPackage(mapId, baked, linesJson, pointsJson)); }
            catch (Exception e) { Debug.LogWarning("RunGround: baked ground: " + e); }
            if (fromPackage == null) Debug.Log("RunGround: the map's runheights.bytes is from another version; the ground is worked out again");
        }
        if (fromPackage != null) detailed = fromPackage;
        else
        {
            try { detailed = await Task.Run(() => RunContours.Detailed(mapId, data.heights, linesJson, pointsJson, interval, m => Debug.Log("RunContours: " + m), cacheFile)); }
            catch (Exception e) { Debug.LogWarning("RunGround: contours: " + e); }
        }
        TvWorldData.Data result = new TvWorldData.Data { mapId = plain.mapId, layer = plain.layer, heights = detailed, look = plain.look, speeds = plain.speeds, town = plain.town };
        kept = result;
        keptKey = key;
        return result;
    }

    // The 2D race's grid, which keeps houses out of the ways a runner can pass (town); null when the scene has none.
    public static TvPassable Passable(MapDefinition map)
    {
        PathfindingManager manager = UnityEngine.Object.FindAnyObjectByType<PathfindingManager>();
        if (manager == null || manager.speedGrid2D == null || map == null) return null;
        float[,] speed = manager.speedGrid2D;
        if (speed.GetLength(0) != map.rows || speed.GetLength(1) != map.cols)
        {
            Debug.LogWarning("RunGround: the scene's grid is not this map's (" + speed.GetLength(0) + "x" + speed.GetLength(1) + ")");
            return null;
        }
        byte[] kinds = map.gridName != null && map.gridName.EndsWith("grid")
            ? MapPackages.LoadBytes(map.gridName.Substring(0, map.gridName.Length - 4) + "kinds", ".bytes") : null;
        return new TvPassable(speed, ForbiddenAreas.CompetitionCells, map.minx, map.miny, map.maxx, map.maxy, map.IsSprint, kinds);
    }

    // The world with the hills made higher (a copy). Heights are stretched round the mean, so the lowest and highest
    // ground move apart and every slope gets steeper by the same share.
    static TvWorldData.Data WithHills(TvWorldData.Data data, float scale)
    {
        TvHeights source = data.heights;
        if (source == null || source.h == null || source.h.Length == 0 || Mathf.Approximately(scale, 1f)) return data;
        double sum = 0;
        foreach (float v in source.h) sum += v;
        float mean = (float)(sum / source.h.Length);
        TvHeights higher = new TvHeights { cell = source.cell, minx = source.minx, miny = source.miny, rows = source.rows, cols = source.cols, h = new float[source.h.Length] };
        for (int i = 0; i < source.h.Length; i++) higher.h[i] = mean + (source.h[i] - mean) * scale;
        return new TvWorldData.Data { mapId = data.mapId, layer = data.layer, heights = higher, look = data.look, speeds = data.speeds, town = data.town };
    }

    static string Safe(string name)
    {
        var b = new System.Text.StringBuilder();
        foreach (char ch in name ?? "map") b.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
        return b.ToString();
    }
}
