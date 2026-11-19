using System;
using System.Collections.Generic;
using MapImport;
using UnityEngine;

// Dark green (410) means two things (2026-11-19, test night): on a sprint map (ISSprOM) it is impassable vegetation,
// on a forest map (ISOM) it is very slow forest you can fight through. The grid of a map is built once, at import,
// with the map's own meaning, so a forest race on a sprint map had green nobody could enter.
// Now the race decides: a forest race on a sprint map opens the dark green again at the forest speed (the map's dark
// green slider; at "Closed" it stays shut), and a sprint race always keeps dark green shut, also on a forest map.
// Only cells that are closed for no other reason than the green are changed: a fence, wall or cliff through the green
// keeps its cells closed, and purple out-of-bounds areas are closed after this.
public static class RaceGreen
{
    // Called by PathfindingManager right after the grid is read and before the slope model is applied
    public static void Apply(string gridName, float[,] speed)
    {
        if (speed == null || string.IsNullOrEmpty(gridName)) return;
        CourseData course = CourseLoader.Loaded;
        MapDefinition map = CourseLoader.LoadedMap;
        if (course == null || map == null || map.gridName != gridName) return;
        bool sprintRace = course.IsSprint();
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        byte[] kinds = map.IsSprint || sprintRace != map.IsSprint ? SoftGround.Kinds(map) : null;
        if (kinds == null || kinds.Length != rows * cols) return;

        if (map.IsSprint) OpenShallowWater(map, speed, kinds, rows, cols);
        if (sprintRace == map.IsSprint) return;   // the grid already means what the race means

        int changed = 0;
        if (sprintRace)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (kinds[r * cols + c] != GroundKind.DarkGreen || speed[r, c] <= 0f) continue;
                    speed[r, c] = 0f;
                    changed++;
                }
            Debug.Log("RaceGreen: sprint race on a forest map, closed " + changed + " cells of dark green");
            return;
        }

        float open = DarkGreenSpeed(map);
        if (open <= 0.01f) return;   // the map's slider says nobody gets through
        HashSet<Vector2Int> lineCells = Passages.LineCells(map, map.gridSize < 5f);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (kinds[r * cols + c] != GroundKind.DarkGreen || speed[r, c] > 0f) continue;
                if (lineCells.Contains(new Vector2Int(c, r))) continue;
                speed[r, c] = open;
                changed++;
            }
        Debug.Log("RaceGreen: forest race on a sprint map, opened " + changed + " cells of dark green at speed " + open.ToString("0.00"));
    }

    // Shallow water (302) with a blue edge may be waded through, also on a sprint map (2026-11-19, the user: water with
    // a blue edge is OK to cross, water with a black edge is not). Sprint maps imported before this closed it. Water
    // with a black bank line keeps the line's cells closed, so it still can not be entered.
    public const float ShallowWaterSpeed = 0.4f;

    static void OpenShallowWater(MapDefinition map, float[,] speed, byte[] kinds, int rows, int cols)
    {
        HashSet<Vector2Int> lineCells = null;
        int changed = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (kinds[r * cols + c] != GroundKind.ShallowWater || speed[r, c] > 0f) continue;
                if (lineCells == null) lineCells = Passages.LineCells(map, map.gridSize < 5f);
                if (lineCells.Contains(new Vector2Int(c, r))) continue;
                speed[r, c] = ShallowWaterSpeed;
                changed++;
            }
        if (changed > 0) Debug.Log("RaceGreen: opened " + changed + " cells of shallow water on a sprint map");
    }

    // Forest dark green with the map's sliders (the same rule a forest map's import uses)
    static float DarkGreenSpeed(MapDefinition map)
    {
        TerrainSpeeds speeds = null;
        try
        {
            string root = map.gridName.EndsWith("grid") ? map.gridName.Substring(0, map.gridName.Length - 4) : null;
            byte[] json = root != null ? MapPackages.LoadBytes(root + "map", ".json") : null;
            if (json != null) speeds = JsonUtility.FromJson<MapPackageInfo>(System.Text.Encoding.UTF8.GetString(json))?.terrain;
        }
        catch (Exception e)
        {
            Debug.LogWarning("RaceGreen: could not read the map's terrain sliders: " + e.Message);
        }
        if (speeds == null) speeds = TerrainSpeeds.Default;
        return Mathf.Max(0f, speeds.Ground(TerrainKind.DarkGreen, TerrainSpeeds.NormalDark));
    }
}
