using System;
using System.IO;
using System.Threading.Tasks;
using MapImport;
using UnityEngine;

// What the 3D view of a TV control is built from: the map package's world layer (world.bytes, written by the map
// import), the heights of its grid and the terrain look in map.json. Maps imported on this computer before the world
// layer existed get it made from the map file kept in their folder the first time it is needed.
// Sprint maps get no TV controls yet.
public static class TvWorldData
{
    public class Data
    {
        public string mapId;
        public WorldLayer layer;
        public TvHeights heights;
        public TerrainLook look;
        public TerrainSpeeds speeds;     // the map's terrain sliders (run-yourself game mode); never null
    }

    static Data cached;

    // After a map was built again (Edit map)
    public static void Forget()
    {
        cached = null;
    }

    static string Root(MapDefinition map)
    {
        string grid = map != null ? map.gridName : null;
        if (string.IsNullOrEmpty(grid) || !MapPackages.IsPackagePath(grid) || !grid.EndsWith("grid", StringComparison.Ordinal)) return null;
        return grid.Substring(0, grid.Length - "grid".Length);
    }

    static string LocalSource(MapDefinition map, out MapPackageInfo info)
    {
        info = null;
        if (map == null || string.IsNullOrEmpty(map.folder)) return null;
        try
        {
            string json = Path.Combine(map.folder, "map.json");
            if (!File.Exists(json)) return null;
            info = JsonUtility.FromJson<MapPackageInfo>(File.ReadAllText(json));
            if (info == null) return null;
            string source = PrivateMaps.SourceFile(map.folder, info);
            return File.Exists(source) ? source : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // True when a TV control can be shown on the map
    public static bool HasWorld(MapDefinition map)
    {
        if (map == null || map.IsSprint) return false;
        if (cached != null && cached.mapId == map.id) return true;
        string root = Root(map);
        if (root == null) return false;
        if (MapPackages.IsFile(root))
        {
            if (File.Exists(root.Substring(MapPackages.FilePrefix.Length) + "world.bytes")) return true;
            return LocalSource(map, out _) != null;
        }
        TextAsset asset = Resources.Load<TextAsset>(root + "world");
        if (asset == null) return false;
        Resources.UnloadAsset(asset);
        return true;
    }

    // Reads (or makes) the map's world data. Call on the main thread; the heavy part runs in the background.
    public static async Task<Data> LoadAsync(MapDefinition map)
    {
        if (map == null || map.IsSprint) return null;
        string root = Root(map);
        if (root == null) return null;
        bool known = cached != null && cached.mapId == map.id;
        byte[] world = known ? null : MapPackages.LoadBytes(root + "world", ".bytes");
        byte[] grid = known ? null : MapPackages.LoadBytes(map.gridName, ".bytes");
        byte[] json = MapPackages.LoadBytes(root + "map", ".json");
        TerrainLook look = null;
        MapPackageInfo info = null;
        try
        {
            if (json != null) info = JsonUtility.FromJson<MapPackageInfo>(System.Text.Encoding.UTF8.GetString(json));
            look = info?.look;
        }
        catch (Exception e)
        {
            Debug.LogWarning("TvWorldData: could not read map.json of " + map.id + ": " + e.Message);
        }
        // The look is read every time, since it can be changed in Edit map without building the map again
        TerrainSpeeds speeds = info?.terrain ?? new TerrainSpeeds();
        if (known) return new Data { mapId = map.id, layer = cached.layer, heights = cached.heights, look = (look ?? new TerrainLook()).Safe(), speeds = speeds };
        string source = world == null ? LocalSource(map, out _) : null;
        string folder = map.folder;
        if (world == null && source == null)
        {
            Debug.LogWarning("TvWorldData: " + map.id + " has no world layer; import the map again to get TV controls");
            return null;
        }
        Data data = await Task.Run(() =>
        {
            WorldLayer layer;
            if (world != null)
            {
                layer = WorldLayer.FromBytes(world);
            }
            else
            {
                // A map imported before the world layer: make it from the map file, like the import does, and keep it
                MapFile file = MapPackageBuilder.ReadMapFile(File.ReadAllBytes(source));
                if (info != null && info.boundary != null && info.boundary.Length >= 6) file.boundary = info.boundary;
                MapExtent whole = MapExtent.Of(file);
                MapExtent extent = MapBoundary.Clip(whole, file.boundary);
                double mpm = file.MetersPerMm;
                double offE = info != null ? info.offsetEast : 1000 - Math.Floor(whole.minX * mpm);
                double offN = info != null ? info.offsetNorth : 1000 - Math.Floor(whole.minY * mpm);
                layer = WorldLayer.Build(file, extent, offE, offN);
                try { File.WriteAllBytes(Path.Combine(folder, "world.bytes"), layer.ToBytes()); }
                catch (Exception) { }
            }
            return new Data { mapId = map.id, layer = layer, heights = TvHeights.Read(grid), look = (look ?? new TerrainLook()).Safe(), speeds = speeds };
        });
        cached = data;
        return data;
    }
}
