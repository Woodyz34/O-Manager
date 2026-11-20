using System;
using System.Collections.Generic;
using UnityEngine;

// A picture of a map and where it lies in the game world. A map can have several pictures:
// the clean one that new courses are drawn on, and the old pictures that had a course printed on them.
[Serializable]
public class MapBackground
{
    public string id;
    public string spritePath;        // sprite in a Resources folder, without extension
    public string thumbnailPath;     // small copy of the picture for menus, in a Resources folder
    public Vector3 position;
    public float rotation;           // degrees around Z
    public float scale = 1f;
    public bool courseInPicture;     // the course is already printed in the picture, so it is not drawn on top

    // The full-size sprite's measurements, so a course can be drawn on the thumbnail without loading the big picture
    public Vector2 pixelSize;
    public Vector2 pivotPixels;
    public float pixelsPerUnit = 100f;

    // Where a point in the game world lands on the picture, from 0 to 1 across and up
    public Vector2 WorldToUV(Vector3 world)
    {
        Vector3 local = Quaternion.Euler(0f, 0f, -rotation) * (world - position) / scale;
        Vector2 pixel = (Vector2)local * pixelsPerUnit + pivotPixels;
        return new Vector2(pixel.x / pixelSize.x, pixel.y / pixelSize.y);
    }
}

// Everything the game needs to know about one map: its walkability grid, where it lies in the world and its pictures.
// Kept in Resources/MapDefs so both the lobby and the competition scene can find it.
[CreateAssetMenu(menuName = "O-Manager/Map Definition")]
public class MapDefinition : ScriptableObject
{
    public const float WorldUnitsPerMeter = 0.01f; // same scale factor PathfindingManager uses for the grid

    public string id;
    public string displayName;
    public string mapType = "forest";     // "forest", "sprint" or "both" (sprint and both: 2 m cells)

    [Header("Grid: a name in Resources/2d-gridJson, or a map package path (see MapPackages)")]
    public string gridName = "output_grid";
    public float minx, miny, maxx, maxy;
    public float gridSize = 10f;
    public int rows, cols;

    [Header("Map objects (GeoJSON) for imported maps; empty uses the files set on GeoJsonSpawner")]
    public string pointsPath;
    public string linesPath;

    // Imported on this computer and not yet built into the game
    [NonSerialized] public bool isLocal;
    // Imported as private: only this player can use it (see PrivateMaps)
    [NonSerialized] public bool isPrivate;
    // A short-lived copy of the lobby host's private map, only for the races in that lobby (see PrivateMaps)
    [NonSerialized] public bool isBorrowed;
    // A copy of a public map another player shared (SharedMaps)
    [NonSerialized] public bool isSharedCopy;
    // The folder of a map imported on this computer
    [NonSerialized] public string folder;

    // This player's own copy of a map that is also built into the game (it is used instead of the built-in one here)
    [NonSerialized] public bool replacesBuiltIn;

    // Imported on this computer by this player: they can change its name, who may use it, the club and the map edge
    public bool CanEdit => isLocal && !isBorrowed && !isSharedCopy && !string.IsNullOrEmpty(folder);

    public bool IsSprint => mapType == "sprint" || mapType == "both";   // 2 m cells, sprint courses by default
    public bool IsForest => mapType != "sprint";                        // forest courses fit it too

    [Header("Pictures")]
    public string defaultBackground;
    public List<MapBackground> backgrounds = new List<MapBackground>();

    public float CellSize => gridSize * WorldUnitsPerMeter;
    float TotalWidth => cols * CellSize;
    float TotalHeight => rows * CellSize;

    public MapBackground GetBackground(string backgroundId)
    {
        if (!string.IsNullOrEmpty(backgroundId))
        {
            foreach (MapBackground background in backgrounds)
            {
                if (background.id == backgroundId) return background;
            }
        }
        foreach (MapBackground background in backgrounds)
        {
            if (background.id == defaultBackground) return background;
        }
        return backgrounds.Count > 0 ? backgrounds[0] : null;
    }

    // Real-world coordinates to the game world, the same way GeoJsonSpawner places the map objects
    public Vector3 ToWorld(CoursePoint point)
    {
        float u = (float)((point.e - minx) / ((double)maxx - minx));
        float v = (float)((point.n - miny) / ((double)maxy - miny));
        float x = u * (cols - 1) * CellSize + CellSize / 2f - TotalWidth / 2f;
        float y = v * (rows - 1) * CellSize + CellSize / 2f - TotalHeight / 2f;
        return new Vector3(x, y, -1f);
    }

    public CoursePoint FromWorld(Vector3 world)
    {
        double u = (world.x + TotalWidth / 2.0 - CellSize / 2.0) / ((cols - 1) * (double)CellSize);
        double v = (world.y + TotalHeight / 2.0 - CellSize / 2.0) / ((rows - 1) * (double)CellSize);
        return new CoursePoint(minx + u * ((double)maxx - minx), miny + v * ((double)maxy - miny));
    }

    // The grid cell a point lies in, as x = column and y = row (the same as AgentMovement.WorldToGridPositionCentered)
    public Vector2Int ToCell(CoursePoint point)
    {
        Vector3 world = ToWorld(point);
        int j = Mathf.FloorToInt((world.x + TotalWidth / 2f) / CellSize);
        int i = Mathf.FloorToInt((world.y + TotalHeight / 2f) / CellSize);
        return new Vector2Int(j, i);
    }

    public bool Contains(CoursePoint point)
    {
        Vector2Int cell = ToCell(point);
        return cell.x >= 0 && cell.x < cols && cell.y >= 0 && cell.y < rows;
    }

    // ---- Lookup ----

    static List<MapDefinition> all;

    public static List<MapDefinition> All
    {
        get
        {
            if (all == null)
            {
                all = new List<MapDefinition>(Resources.LoadAll<MapDefinition>("MapDefs"));
                // Maps imported on this computer, unless the same map is already built into the game. A map this
                // player imported and sent in keeps their own copy even once it is built in (2026-11-20, Anders: a map
                // built into the game could no longer be edited), so they can change it and build it again; on this
                // computer their copy is the one used.
                foreach (MapDefinition local in MapPackages.LoadLocal())
                {
                    int same = all.FindIndex(m => m.id == local.id);
                    if (same < 0) all.Add(local);
                    else if (local.CanEdit && !local.isPrivate)
                    {
                        local.replacesBuiltIn = true;
                        all[same] = local;
                    }
                }
                all.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
            }
            return all;
        }
    }

    // Read the list again, after a map has been imported
    public static void Refresh()
    {
        all = null;
    }

    // Borrowed maps (the lobby host's private map) are not in All, so they never show up in map lists or let
    // courses on them be picked; Exists leaves them out too. Find and FindExact use them first, so the race and the
    // course pictures in the lobby use the host's map.
    static readonly List<MapDefinition> borrowed = new List<MapDefinition>();

    public static void Borrow(MapDefinition map)
    {
        borrowed.RemoveAll(m => m.id == map.id);
        borrowed.Add(map);
    }

    public static void ForgetBorrowed()
    {
        borrowed.Clear();
    }

    public static bool Exists(string mapId)
    {
        // The old map built into the scenes is gone, so a course without a map id has no map either
        return !string.IsNullOrEmpty(mapId) && All.Exists(m => m.id == mapId);
    }

    // The map with this id, or null
    public static MapDefinition FindExact(string mapId)
    {
        if (string.IsNullOrEmpty(mapId)) return null;
        MapDefinition map = borrowed.Find(m => m.id == mapId);
        return map != null ? map : All.Find(m => m.id == mapId);
    }

    public static MapDefinition Find(string mapId)
    {
        MapDefinition map = FindExact(mapId);
        if (map != null) return map;
        return All.Count > 0 ? All[0] : null;
    }
}
