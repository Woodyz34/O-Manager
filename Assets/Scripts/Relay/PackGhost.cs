using System.Collections.Generic;
using UnityEngine;

// Test helper: a pretend runner that runs a relay leg along a fixed route at a set speed, so running behind someone and
// hanging on can be tried in a game with only one player. Not used in normal play.
public class PackGhost : MonoBehaviour, IPackRunner
{
    public List<Vector3> route = new List<Vector3>();
    public List<int> controlRouteIndex = new List<int>(); // route index of control 1, 2, ... and the finish
    public float speed = 0.8f;                            // units per real second
    public int leg;
    public string label = "Ghost";
    public bool running = true;

    int index;
    int control = 1;

    public bool IsValidRunner => this != null && isActiveAndEnabled;
    public bool IsLocalRunner => false;
    public Vector3 RunnerPosition => transform.position;
    public int RelayLeg => leg;
    public int RelayControl => control;
    public bool RelayRunning => running && index < route.Count;
    public string RunnerLabel => label;

    public static PackGhost Create(string label, List<Vector3> route, List<int> controlRouteIndex, float speed, int leg)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(go.GetComponent<Collider>());
        go.name = "Pack ghost " + label;
        go.transform.localScale = Vector3.one * 0.12f;
        Renderer renderer = go.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.1f, 0.4f, 1f, 0.9f) };
        renderer.sortingOrder = 50;
        PackGhost ghost = go.AddComponent<PackGhost>();
        ghost.label = label;
        ghost.route = route;
        ghost.controlRouteIndex = controlRouteIndex;
        ghost.speed = speed;
        ghost.leg = leg;
        go.transform.position = route.Count > 0 ? route[0] : Vector3.zero;
        return ghost;
    }

    void OnEnable() => PackRegistry.Add(this);
    void OnDisable() => PackRegistry.Remove(this);

    // When set, the ghost runs like a real runner: slower in rough ground and up hills
    public PathfindingManager terrain;

    float TerrainFactor()
    {
        if (terrain == null || terrain.speedGrid2D == null || index >= route.Count) return 1f;
        int rows = terrain.speedGrid2D.GetLength(0);
        int cols = terrain.speedGrid2D.GetLength(1);
        Vector3 p = transform.position;
        int j = Mathf.Clamp(Mathf.FloorToInt((p.x + cols * terrain.cellSize / 2f) / terrain.cellSize), 0, cols - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt((p.y + rows * terrain.cellSize / 2f) / terrain.cellSize), 0, rows - 1);
        float ground = Mathf.Max(0.1f, terrain.speedGrid2D[i, j]);
        Vector3 to = route[index] - p;
        int dx = Mathf.Abs(to.x) < 0.02f ? 0 : (int)Mathf.Sign(to.x);
        int dy = Mathf.Abs(to.y) < 0.02f ? 0 : (int)Mathf.Sign(to.y);
        string dir = dy > 0 ? (dx > 0 ? "NE" : dx < 0 ? "NW" : "N") : dy < 0 ? (dx > 0 ? "SE" : dx < 0 ? "SW" : "S") : dx > 0 ? "E" : "W";
        float slope = 1f;
        if (terrain.slopeFactors != null && terrain.slopeFactors.TryGetValue(dir, out float[,] grid)) slope = grid[i, j];
        return ground * slope * TerrainSpeedModel.RaceSpeedScale();
    }

    void Update()
    {
        if (!running || index >= route.Count) return;
        float step = speed * TerrainFactor() * Time.deltaTime;
        while (step > 0f && index < route.Count)
        {
            Vector3 target = route[index];
            Vector3 delta = target - transform.position;
            delta.z = 0f;
            float distance = delta.magnitude;
            if (distance <= step)
            {
                transform.position = new Vector3(target.x, target.y, -1.1f);
                step -= distance;
                while (control - 1 < controlRouteIndex.Count && index >= controlRouteIndex[control - 1]) control++;
                index++;
            }
            else
            {
                transform.position += delta / distance * step;
                step = 0f;
            }
        }
    }
}
