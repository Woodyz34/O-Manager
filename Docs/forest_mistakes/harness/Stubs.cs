// Stand-ins for the Unity types the forest mistakes use, so the real game code
// (Assets/Scripts/Mistakes/*.cs) can be compiled and run outside Unity with mono. See README.md.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized { get { float m = magnitude; return m > 1e-9f ? new Vector2(x / m, y / m) : new Vector2(0, 0); } }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator *(float k, Vector2 a) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator /(Vector2 a, float k) => new Vector2(a.x / k, a.y / k);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object o) => o is Vector2 && this == (Vector2)o;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
    }
    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static float Distance(Vector2Int a, Vector2Int b) => (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    }
    public static class Mathf
    {
        public const float PI = (float)Math.PI, Deg2Rad = (float)(Math.PI / 180.0);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Abs(float v) => Math.Abs(v);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Exp(float a) => (float)Math.Exp(a);
        public static float Log(float a) => (float)Math.Log(a);
        public static float Sin(float a) => (float)Math.Sin(a);
        public static float Cos(float a) => (float)Math.Cos(a);
        public static float Floor(float a) => (float)Math.Floor(a);
        public static int FloorToInt(float a) => (int)Math.Floor(a);
    }
    public static class Debug
    {
        public static Action<string> sink;
        public static void Log(object o) { if (sink != null) sink(o.ToString()); }
        public static void LogError(object o) { Console.Error.WriteLine(o); }
    }
    public class Coroutine { public Stack<IEnumerator> stack = new Stack<IEnumerator>(); public bool stopped; public float wake; }
    public class WaitForSeconds { public float seconds; public WaitForSeconds(float s) { seconds = s; } }
    public class Transform { public Vector3 position; }
}

namespace Harness
{
    using UnityEngine;
    public class Course { public bool IsSprint() => false; }
}

public static class CourseLoader { public static Harness.Course Loaded = new Harness.Course(); }
public static class TerrainSpeedModel { public static float RaceSpeedScale() => 1f; }

public class PathfindingManager
{
    public float[,] speedGrid2D;
    public float cellSize;   // world units
}

// The parts of the game's AgentMovement the mistakes talk to, with a simple A* and a clock of its own
public class AgentMovement
{
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    public PathfindingManager pathfindingManager;
    public UnityEngine.Vector3 CurrentControlPosition;
    public float runnerStatTec;
    public string runnerName = "Test";
    public bool isWaiting;
    public float UnscaledBaseSpeed = 0.36f;   // world units per second: 36 m/s, the game runs 10 x real time
    public List<UnityEngine.Vector2Int> path;
    public float time;                        // game seconds
    readonly List<UnityEngine.Coroutine> running = new List<UnityEngine.Coroutine>();

    public UnityEngine.Vector2Int WorldToGridPositionCentered(UnityEngine.Vector3 w)
    {
        int rows = pathfindingManager.speedGrid2D.GetLength(0), cols = pathfindingManager.speedGrid2D.GetLength(1);
        float cs = pathfindingManager.cellSize;
        int c = (int)Math.Floor(w.x / cs + cols / 2f), r = (int)Math.Floor(w.y / cs + rows / 2f);
        return new UnityEngine.Vector2Int(Math.Max(0, Math.Min(cols - 1, c)), Math.Max(0, Math.Min(rows - 1, r)));
    }
    public UnityEngine.Vector3 GridToWorld(UnityEngine.Vector2Int c)
    {
        int rows = pathfindingManager.speedGrid2D.GetLength(0), cols = pathfindingManager.speedGrid2D.GetLength(1);
        float cs = pathfindingManager.cellSize;
        return new UnityEngine.Vector3((c.x + 0.5f - cols / 2f) * cs, (c.y + 0.5f - rows / 2f) * cs, 0);
    }
    public void SetMistakePath(List<UnityEngine.Vector2Int> p) { if (p != null && p.Count > 0) { path = p; pathIndex = 0; } }
    public int pathIndex;

    public IEnumerator FindPathAsync(UnityEngine.Vector2Int start, UnityEngine.Vector2Int goal, Action<List<UnityEngine.Vector2Int>> ready)
    {
        ready(AStar(start, goal));
        yield break;
    }

    public UnityEngine.Coroutine StartCoroutine(IEnumerator e)
    {
        var c = new UnityEngine.Coroutine(); c.stack.Push(e); c.wake = time;
        running.Add(c); Step(c);
        return c;
    }
    public void StopCoroutine(UnityEngine.Coroutine c) { if (c != null) { c.stopped = true; running.Remove(c); } }
    void Step(UnityEngine.Coroutine c)
    {
        while (!c.stopped && c.stack.Count > 0)
        {
            IEnumerator top = c.stack.Peek();
            if (!top.MoveNext()) { c.stack.Pop(); continue; }
            object y = top.Current;
            if (y is IEnumerator) { c.stack.Push((IEnumerator)y); continue; }
            if (y is UnityEngine.Coroutine) { continue; }
            if (y is UnityEngine.WaitForSeconds) { c.wake = time + ((UnityEngine.WaitForSeconds)y).seconds; return; }
            c.wake = time; return;
        }
        running.Remove(c);
    }
    // Runs the coroutines that are due; returns the next wake time (or -1)
    public float Pump()
    {
        bool again = true;
        while (again)
        {
            again = false;
            foreach (var c in running.ToArray())
                if (!c.stopped && c.wake <= time + 1e-6f) { Step(c); again = running.Contains(c) && c.wake <= time + 1e-6f; if (again) break; }
        }
        float next = -1f;
        foreach (var c in running) if (next < 0f || c.wake < next) next = c.wake;
        return next;
    }
    public void ClearCoroutines() { foreach (var c in running) c.stopped = true; running.Clear(); }

    // 8-neighbour A*, time = distance / speed
    public List<UnityEngine.Vector2Int> AStar(UnityEngine.Vector2Int s, UnityEngine.Vector2Int g)
    {
        float[,] sp = pathfindingManager.speedGrid2D;
        int rows = sp.GetLength(0), cols = sp.GetLength(1), n = rows * cols;
        if (sp[g.y, g.x] <= 0.05f) return null;
        var cost = new float[n]; var from = new int[n]; var done = new bool[n];
        for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; from[i] = -1; }
        var heap = new SortedSet<(float, int)>();
        int si = s.y * cols + s.x, gi = g.y * cols + g.x;
        cost[si] = 0; heap.Add((H(s.x, s.y, g), si));
        while (heap.Count > 0)
        {
            var top = heap.Min; heap.Remove(top);
            int i = top.Item2; if (done[i]) continue; done[i] = true;
            if (i == gi) break;
            int r = i / cols, c = i % cols;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int y = r + dr, x = c + dc;
                    if (y < 0 || x < 0 || y >= rows || x >= cols || sp[y, x] <= 0.05f) continue;
                    if (dr != 0 && dc != 0 && (sp[r, x] <= 0.05f || sp[y, c] <= 0.05f)) continue;
                    int j = y * cols + x; if (done[j]) continue;
                    float step = (dr != 0 && dc != 0 ? 1.41421f : 1f) * 2f / (sp[y, x] + sp[r, c]);
                    float nc = cost[i] + step;
                    if (nc < cost[j]) { cost[j] = nc; from[j] = i; heap.Add((nc + H(x, y, g), j)); }
                }
        }
        if (from[gi] < 0 && gi != si) return null;
        var p = new List<UnityEngine.Vector2Int>();
        for (int i = gi; i >= 0; i = from[i]) { p.Add(new UnityEngine.Vector2Int(i % cols, i / cols)); if (i == si) break; }
        p.Reverse();
        return p;
    }
    static float H(int x, int y, UnityEngine.Vector2Int g)
    {
        int dx = Math.Abs(x - g.x), dy = Math.Abs(y - g.y);
        return (Math.Max(dx, dy) + 0.41421f * Math.Min(dx, dy)) / 1.6f;
    }
}
