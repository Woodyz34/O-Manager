using System.Collections.Generic;
using MapImport;
using UnityEngine;

// Closed ground that is only closed because of what covers it: a garden or yard (olive green, 520) or a house, and
// not a fence, a wall, a cliff or a purple barrier (2026-11-16). The drawn line may cut across a little more of it
// (ManualPathDrawer), and controls and starts may not stand on it (CourseValidator).
public static class SoftGround
{
    static readonly Dictionary<string, byte[]> kindCache = new Dictionary<string, byte[]>();

    // One GroundKind per cell (row * cols + column), from the package's kinds.bytes; null for maps without one
    public static byte[] Kinds(MapDefinition map)
    {
        if (map == null || string.IsNullOrEmpty(map.gridName) || !map.gridName.EndsWith("grid")) return null;
        if (kindCache.TryGetValue(map.gridName, out byte[] known)) return known;
        byte[] kinds = MapPackages.LoadBytes(map.gridName.Substring(0, map.gridName.Length - 4) + "kinds", ".bytes");
        kindCache[map.gridName] = kinds;
        return kinds;
    }

    public static bool IsPlotOrHouse(byte kind) => kind == GroundKind.Plot || kind == GroundKind.Building || kind == GroundKind.Canopy;

    // The ground kind of a cell, or 255 when the map has none
    public static byte KindAt(MapDefinition map, int x, int y, int rows, int cols)
    {
        byte[] kinds = Kinds(map);
        if (kinds == null || kinds.Length != rows * cols || x < 0 || y < 0 || x >= cols || y >= rows) return 255;
        return kinds[y * cols + x];
    }

    // A closed cell of a garden or a house that no blocking line runs through and no purple area closes
    public static bool IsSoft(MapDefinition map, Vector2Int c, int rows, int cols, HashSet<int> purpleCells = null)
    {
        if (!IsPlotOrHouse(KindAt(map, c.x, c.y, rows, cols))) return false;
        if (purpleCells != null && purpleCells.Contains(c.y * cols + c.x)) return false;
        return !Passages.LineCells(map, map.gridSize < 5f).Contains(c);
    }
}
