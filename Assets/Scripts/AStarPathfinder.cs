using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class AStarPathfinder
{
    private int width;
    private int height;
    private float[,] speedGrid;
    private Dictionary<string, float[,]> slopeFactors;
    private System.Random rng = new System.Random();
    private float[,] originalGrid; // Sparar originalet


    public AStarPathfinder(float[,] speedGrid, Dictionary<string, float[,]> slopeFactors)
    {
        this.speedGrid = (float[,])speedGrid.Clone();  // jobbar på en egen kopia
        this.originalGrid = (float[,])speedGrid.Clone(); // sparar originalet

        this.height = speedGrid.GetLength(0);
        this.width = speedGrid.GetLength(1);
        this.slopeFactors = slopeFactors;

        // The estimate of the cost left must never be higher than the real cost, or A* reopens cells over and over
        // (one search on the 2 m sprint grid took 10 s). The cheapest step is the fastest cell on the best slope.
        // Worked out once per grid (the same grids are searched many times)
        lock (scaleCache)
        {
            if (!scaleCache.TryGetValue(speedGrid, out heuristicScale))
            {
                float fastest = 0.01f;
                foreach (float s in speedGrid) if (s > fastest) fastest = s;
                float bestSlope = 1f;
                if (slopeFactors != null)
                    foreach (float[,] factors in slopeFactors.Values)
                        if (factors != null)
                            foreach (float f in factors) if (f > bestSlope) bestSlope = f;
                heuristicScale = 1f / (fastest * bestSlope);
                if (speedGrid.Length > 100000)   // small cut-outs are quick anyway
                {
                    if (scaleCache.Count > 8) scaleCache.Clear();   // grids of maps played earlier
                    scaleCache[speedGrid] = heuristicScale;
                }
            }
        }
    }

    private static readonly Dictionary<float[,], float> scaleCache = new Dictionary<float[,], float>(new ReferenceComparer());

    private class ReferenceComparer : IEqualityComparer<float[,]>
    {
        public bool Equals(float[,] a, float[,] b) => ReferenceEquals(a, b);
        public int GetHashCode(float[,] a) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a);
    }

    private float heuristicScale;

    // Octile distance (diagonal steps cost 1.414) at the fastest possible speed
    private float Estimate(int x, int y, Vector2Int goal)
    {
        int dx = x > goal.x ? x - goal.x : goal.x - x;
        int dy = y > goal.y ? y - goal.y : goal.y - y;
        int lo = dx < dy ? dx : dy, hi = dx < dy ? dy : dx;
        return (hi + 0.414f * lo) * heuristicScale;
    }


    /// <summary>
    /// Returnerar en slumpad väg bland de k kortaste.
    /// </summary>
    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal, int k = 5)
    {
        var allPaths = FindPathsWithMiddlePenalty(start, goal, k, 2f, 1);
        if (allPaths.Count == 0) return null;

        int index = rng.Next(0, allPaths.Count);
        return allPaths[index];
    }

    /// <summary>
    /// Returnerar alla k kortaste vägar.
    /// </summary>
    public List<List<Vector2Int>> GetAllPaths(Vector2Int start, Vector2Int goal, int k = 5)
    {
        return FindPathsWithMiddlePenalty(start, goal, k, 2f, 1);
    }

    // -------- Yen’s algorithm (som i förra koden) --------
    public List<List<Vector2Int>> FindKShortestPaths(Vector2Int start, Vector2Int goal, int k)
    {
        var resultPaths = new List<List<Vector2Int>>();

        var bestPath = InternalFindPath(start, goal);
        if (bestPath == null) return resultPaths;

        resultPaths.Add(bestPath);
        ApplyPenaltyToPath(bestPath, 2f);
        var candidates = new SimplePriorityQueue<List<Vector2Int>>();

        for (int i = 0; i < bestPath.Count - 1 && resultPaths.Count < k; i++)
        {
            var spurNode = bestPath[i];
            var rootPath = bestPath.Take(i + 1).ToList();

            var removedEdges = new List<(Vector2Int, Vector2Int)>();
            foreach (var path in resultPaths)
            {
                if (path.Count > i && rootPath.SequenceEqual(path.Take(i + 1)))
                {
                    removedEdges.Add((path[i], path[i + 1]));
                }
            }

            var spurPath = InternalFindPath(spurNode, goal, null, removedEdges);
            
            if (spurPath != null && spurPath.Count > 1)
            {
                var totalPath = new List<Vector2Int>(rootPath);
                totalPath.AddRange(spurPath.Skip(1));

                if (!resultPaths.Any(p => PathsEqual(p, totalPath)))
                {
                    float cost = CalculatePathCost(totalPath);
                    candidates.Enqueue(totalPath, cost);
                }
            }
        }

        while (resultPaths.Count < k && candidates.Count > 0)
        {
            var nextBest = candidates.Dequeue();
            resultPaths.Add(nextBest);
        }

        return resultPaths;
    }

    private bool PathsEqual(List<Vector2Int> a, List<Vector2Int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    public float CalculatePathCost(List<Vector2Int> path)
    {
        float cost = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2Int from = path[i];
            Vector2Int to = path[i + 1];

            float cellSpeed = speedGrid[to.y, to.x];
            if (cellSpeed <= 0f) return 10000f;

            string dirName = GetDirectionName(from, to);

            float slopeFactor = 1f;
            if (slopeFactors != null && slopeFactors.ContainsKey(dirName))
            {
                slopeFactor = slopeFactors[dirName][from.y, from.x];
            }

            float distance = (Mathf.Abs(to.x - from.x) > 0 && Mathf.Abs(to.y - from.y) > 0) ? 1.414f : 1f;
            cost += (distance / cellSpeed) / slopeFactor;
        }

        return cost;
    }

    private string GetDirectionName(Vector2Int from, Vector2Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;

        if (dx == 0 && dy > 0) return "N";
        if (dx > 0 && dy > 0) return "NE";
        if (dx > 0 && dy == 0) return "E";
        if (dx > 0 && dy < 0) return "SE";
        if (dx == 0 && dy < 0) return "S";
        if (dx < 0 && dy < 0) return "SW";
        if (dx < 0 && dy == 0) return "W";
        if (dx < 0 && dy > 0) return "NW";

        return "N";
    }

    private static readonly int[] dirX = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private static readonly int[] dirY = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly string[] dirNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    // Återanvänds mellan sökningar så att varje sökning slipper skapa nya tabeller
    private float[] gScore;
    private int[] cameFrom;
    private float[] heapKeys = new float[1024];
    private int[] heapItems = new int[1024];
    private int heapCount;

    private List<Vector2Int> InternalFindPath(Vector2Int start, Vector2Int goal, float[,] customGrid = null,
                                              List<(Vector2Int, Vector2Int)> blockedEdges = null)
    {
        float[,] grid = customGrid ?? speedGrid;
        if (!IsInsideGrid(start) || !IsInsideGrid(goal)) return null;

        int cellCount = width * height;
        if (gScore == null)
        {
            gScore = new float[cellCount];
            cameFrom = new int[cellCount];
        }
        for (int i = 0; i < cellCount; i++)
        {
            gScore[i] = float.PositiveInfinity;
            cameFrom[i] = -1;
        }
        heapCount = 0;

        float[][,] slopes = new float[8][,];
        if (slopeFactors != null)
        {
            for (int d = 0; d < 8; d++)
                slopeFactors.TryGetValue(dirNames[d], out slopes[d]);
        }

        int startIndex = start.y * width + start.x;
        int goalIndex = goal.y * width + goal.x;

        gScore[startIndex] = 0;
        HeapPush(startIndex, 0);

        while (heapCount > 0)
        {
            float currentF = heapKeys[0];
            int current = HeapPop();
            int cx = current % width;
            int cy = current / width;
            float currentG = gScore[current];

            // En cell kan ligga flera gånger i kön. Hoppa över poster som har blivit omsprungna av en billigare väg.
            if (currentF > currentG + Estimate(cx, cy, goal) + 0.0001f && current != startIndex)
                continue;

            if (current == goalIndex) return ReconstructPath(current);

            for (int d = 0; d < 8; d++)
            {
                int nx = cx + dirX[d];
                int ny = cy + dirY[d];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;

                float cellSpeed = grid[ny, nx];
                if (cellSpeed <= 0.01f) continue;
                // No cutting a corner between two closed cells (a fence drawn diagonally across the grid)
                if (dirX[d] != 0 && dirY[d] != 0 && (grid[cy, nx] <= 0.01f || grid[ny, cx] <= 0.01f)) continue;

                if (blockedEdges != null && blockedEdges.Contains((new Vector2Int(cx, cy), new Vector2Int(nx, ny))))
                    continue;

                float distance = (dirX[d] != 0 && dirY[d] != 0) ? 1.414f : 1f;

                float slopeFactor = 1f;
                if (slopes[d] != null)
                {
                    slopeFactor = slopes[d][cy, cx];
                }

                float tentativeGScore = currentG + (distance / cellSpeed) / slopeFactor;

                int neighbor = ny * width + nx;
                if (tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    HeapPush(neighbor, tentativeGScore + Estimate(nx, ny, goal));
                }
            }
        }

        return null;
    }

    // Min-heap: den billigaste cellen ligger alltid först, så varje uttag kostar log(n) i stället för att gå igenom hela kön
    private void HeapPush(int item, float key)
    {
        if (heapCount == heapKeys.Length)
        {
            System.Array.Resize(ref heapKeys, heapCount * 2);
            System.Array.Resize(ref heapItems, heapCount * 2);
        }

        int i = heapCount++;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (heapKeys[parent] <= key) break;
            heapKeys[i] = heapKeys[parent];
            heapItems[i] = heapItems[parent];
            i = parent;
        }
        heapKeys[i] = key;
        heapItems[i] = item;
    }

    private int HeapPop()
    {
        int result = heapItems[0];
        heapCount--;
        float key = heapKeys[heapCount];
        int item = heapItems[heapCount];

        int i = 0;
        while (true)
        {
            int child = 2 * i + 1;
            if (child >= heapCount) break;
            if (child + 1 < heapCount && heapKeys[child + 1] < heapKeys[child]) child++;
            if (key <= heapKeys[child]) break;
            heapKeys[i] = heapKeys[child];
            heapItems[i] = heapItems[child];
            i = child;
        }
        heapKeys[i] = key;
        heapItems[i] = item;
        return result;
    }

    private bool IsInsideGrid(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;
    }

    private List<Vector2Int> ReconstructPath(int current)
    {
        var totalPath = new List<Vector2Int>();
        while (current >= 0)
        {
            totalPath.Add(new Vector2Int(current % width, current / width));
            current = cameFrom[current];
        }
        totalPath.Reverse();
        return totalPath;
    }
    /// <summary>
    /// Straffar en path så att den blir mindre attraktiv vid nästa sökning.
    /// </summary>
    public void ApplyPenaltyToPath(List<Vector2Int> path, float penaltyFactor = 2f)
    {
        foreach (var pos in path)
        {
            // Minska hastigheten (men inte till 0!)
            speedGrid[pos.y, pos.x] /= penaltyFactor;
        }
    }
    public void ResetGrid()
    {
        this.speedGrid = (float[,])originalGrid.Clone();
    }
    public void ApplyPenaltyToMiddleOfPath(List<Vector2Int> path, float penaltyFactor = 2f, int margin = 1)
    {
        if (path == null || path.Count <= 2 * margin) return;

        for (int i = margin; i < path.Count - margin; i++)
        {
            Vector2Int pos = path[i];
            speedGrid[pos.y, pos.x] /= penaltyFactor;
        }
    }
    public List<List<Vector2Int>> FindPathsWithMiddlePenalty(Vector2Int start, Vector2Int goal, int iterations = 10, float penaltyFactor = 2f, int margin = 1)
    {
        var resultPaths = new List<List<Vector2Int>>();

        for (int i = 0; i < iterations; i++)
        {
            var bestPath = InternalFindPath(start, goal);
            if (bestPath == null) break;

            resultPaths.Add(bestPath);

            // Sätt penalty på mitten av pathen
            ApplyPenaltyToMiddleOfPath(bestPath, penaltyFactor, margin);
        }

        return resultPaths;
    }

}
