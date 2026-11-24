using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using MapImport;

// The whole map in 3D for the run-yourself game mode, built round the runner as they run.
//
// The map is cut into squares of 64 m ("pieces"). Only the pieces within the view distance of the player's 3D quality
// (RunSettings) exist; they are built on background threads by the TV controls' world builder (TvWorldBuilder, in
// its piece mode, so they fit together without seams and look the same on every computer), handed to the graphics
// card a few at a time so the game never stops for it, and thrown away when the runner has left them behind. The fog
// hides the edge.
// To keep weak computers going:
//  - every piece is a handful of meshes, so the graphics card skips every piece outside the picture;
//  - a piece close to the runner draws its full trees and its ground at 1 m; further away a cheaper copy of the same
//    trees and the ground at 4 m (TvMesh.Near / Far);
//  - the undergrowth and the young trees of thickets (by far the most triangles) are only made for the pieces right
//    round the runner, and thrown away again when the runner moves on;
//  - shadows only close by, and none on Low.
public class RunWorld : MonoBehaviour
{
    public const float PieceSize = 64f;
    const int Half = 32;
    // The 3D world lies far below the 2D map of the competition scene, so neither camera sees the other's things
    public static readonly Vector3 Origin = new Vector3(0f, -6000f, 0f);
    static readonly Color Sky = new Color32(0xc7, 0xd6, 0xda, 255);

    public RunTerrain terrain;
    public TvWorldData.Data data;
    TvPassable passable;                // the 2D race's grid: no house where it is open (a town)
    CourseData course;                  // its out-of-bounds areas and barriers are put up as taped fences
    public double anchorE, anchorN;     // the map point at Origin
    public float baseHeight;            // the map height at Origin
    RunSettings.Look look;

    Material plainMaterial, groundMaterial, pavedMaterial;
    RunPeople people;
    Light sun;
    float savedShadowDistance;
    int savedCascades;
    ShadowQuality savedShadows;
    ShadowResolution savedResolution;
    ShadowProjection savedProjection;

    // The flags of the course: the controls in order, then the finish. Where each stands is known once its piece is built.
    List<CoursePoint> flagPoints = new List<CoursePoint>();
    double[] flagE, flagN;

    class Piece
    {
        public int ix, iz;
        public double ce, cn;               // the centre on the map
        public GameObject root;
        public readonly List<Renderer> near = new List<Renderer>(), far = new List<Renderer>();
        public readonly List<Mesh> meshes = new List<Mesh>();
        public TvWorld world;
        public float[] solids;              // x, z, r for each, in metres from Origin
        public float[] walls;               // ax, az, bx, bz for each house wall, in metres from Origin
        public List<int> flags;             // which flags stand in it, in the order given to the builder
        public bool ready, building, gone;
        public GameObject detail;
        public readonly List<Mesh> detailMeshes = new List<Mesh>();
        public bool detailBuilding;
        public float dist;
        public bool nearOn;
        public readonly List<Renderer> lean = new List<Renderer>();   // a town's leaner houses, hidden under the full ones
        public bool leanHidden, peopleOn;
    }

    class Done
    {
        public Piece piece;
        public bool detail;
        public TvWorld world;
        public List<TvWorldView.MeshArrays> arrays;
    }

    readonly Dictionary<long, Piece> pieces = new Dictionary<long, Piece>();
    readonly ConcurrentQueue<Done> finished = new ConcurrentQueue<Done>();
    readonly Queue<Done> uploading = new Queue<Done>();
    int jobs;
    const int MaxJobs = 2;
    float nextPlan;
    double playerE, playerN;
    bool hasPlayer;

    // While the race loads, more time per frame goes to handing meshes over
    public bool Loading;

    static long Key(int ix, int iz) => ((long)ix << 32) ^ (uint)iz;

    // ---- Setting up ----

    public static RunWorld Create(TvWorldData.Data data, RunTerrain terrain, CoursePoint start, List<CoursePoint> flags, TvPassable passable = null, CourseData course = null)
    {
        RunWorld world = new GameObject("Run world").AddComponent<RunWorld>();
        world.data = data;
        world.passable = passable;
        world.course = course;
        world.terrain = terrain;
        // Whole metres, so every computer cuts the map the same way
        world.anchorE = Math.Floor(start.e);
        world.anchorN = Math.Floor(start.n);
        world.baseHeight = Mathf.Round(terrain.MapHeight(world.anchorE, world.anchorN));
        world.flagPoints = new List<CoursePoint>(flags);
        world.flagE = new double[flags.Count];
        world.flagN = new double[flags.Count];
        for (int i = 0; i < flags.Count; i++)
        {
            world.flagE[i] = flags[i].e;
            world.flagN[i] = flags[i].n;
        }
        world.Make();
        world.BuildTape();
        return world;
    }

    // ---- The course's out-of-bounds areas and barriers: taped fences (2026-11-20, Anders) ----
    // Red and white tape on thin posts round every out-of-bounds area and along every uncrossable boundary, as at a
    // real sprint race, so they are seen; the runner is kept out by the 2D race's grid (TvPassable, RunTerrain).

    Mesh tapeMesh;

    void BuildTape()
    {
        var v = new List<Vector3>();
        var nrm = new List<Vector3>();
        var col = new List<Color32>();
        var tri = new List<int>();
        Color32 red = new Color32(214, 38, 32, 255), white = new Color32(246, 244, 238, 255), post = new Color32(70, 74, 70, 255);
        float Y(double e, double n) => terrain.MapHeight(e, n) - baseHeight;

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 colour)
        {
            Vector3 normal = Vector3.Cross(b - a, d - a).normalized;
            // Both sides
            for (int side = 0; side < 2; side++)
            {
                int k = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                Vector3 nn = side == 0 ? normal : -normal;
                for (int q = 0; q < 4; q++) { nrm.Add(nn); col.Add(colour); }
                if (side == 0) { tri.Add(k); tri.Add(k + 1); tri.Add(k + 2); tri.Add(k); tri.Add(k + 2); tri.Add(k + 3); }
                else { tri.Add(k); tri.Add(k + 2); tri.Add(k + 1); tri.Add(k); tri.Add(k + 3); tri.Add(k + 2); }
            }
        }

        void Post(double e, double n)
        {
            float y = Y(e, n);
            const float W = 0.035f, Top = 1.05f;
            Vector3 c = ToScene(e, n, 0f);
            Vector3 lo = new Vector3(0f, y - 0.4f, 0f), hi = new Vector3(0f, y + Top, 0f);
            Vector3[] corner = { new Vector3(-W, 0, -W), new Vector3(W, 0, -W), new Vector3(W, 0, W), new Vector3(-W, 0, W) };
            for (int s = 0; s < 4; s++)
            {
                Vector3 a = corner[s], b = corner[(s + 1) % 4];
                int k = v.Count;
                v.Add(c + a + lo); v.Add(c + b + lo); v.Add(c + b + hi); v.Add(c + a + hi);
                Vector3 nn = Vector3.Cross(b - a, Vector3.up).normalized;
                for (int q = 0; q < 4; q++) { nrm.Add(-nn); col.Add(post); }
                tri.Add(k); tri.Add(k + 2); tri.Add(k + 1); tri.Add(k); tri.Add(k + 3); tri.Add(k + 2);
            }
        }

        // One taped line through the points (closed: back to the first)
        void TapeLine(List<(double e, double n)> pts, bool closed)
        {
            int count = pts.Count, sides = closed ? count : count - 1;
            for (int s = 0; s < sides; s++)
            {
                var a = pts[s];
                var b = pts[(s + 1) % count];
                double de = b.e - a.e, dn = b.n - a.n, length = Math.Sqrt(de * de + dn * dn);
                if (length < 0.05) continue;
                // Posts about every 3 m; the tape in stripes of 0.5 m, red and white
                int posts = Math.Max(1, (int)Math.Ceiling(length / 3.0));
                for (int p = 0; p < posts; p++) Post(a.e + de * p / posts, a.n + dn * p / posts);
                int stripes = Math.Max(1, (int)Math.Round(length / 0.5));
                for (int k = 0; k < stripes; k++)
                {
                    double e0 = a.e + de * k / stripes, n0 = a.n + dn * k / stripes, e1 = a.e + de * (k + 1) / stripes, n1 = a.n + dn * (k + 1) / stripes;
                    float y0 = Y(e0, n0), y1 = Y(e1, n1);
                    Color32 c = k % 2 == 0 ? red : white;
                    foreach (float h in new[] { 0.95f, 0.55f })
                    {
                        Vector3 p0 = ToScene(e0, n0, y0 + h), p1 = ToScene(e1, n1, y1 + h);
                        Quad(p0, p1, p1 + Vector3.up * 0.07f, p0 + Vector3.up * 0.07f, c);
                    }
                }
                if (!closed || s == sides - 1) Post(b.e, b.n);
            }
        }

        if (course != null && course.forbidden != null)
        {
            foreach (ForbiddenArea area in course.forbidden)
            {
                if (area == null || !area.IsValid || area.bridge) continue;
                var pts = new List<(double e, double n)>(area.corners.Count);
                foreach (CoursePoint q in area.corners) pts.Add((q.e, q.n));
                TapeLine(pts, !area.line);
            }
        }

        // The map's own out-of-bounds areas (the purple hatching printed on the map, a map maker's forbidden area) near
        // the course (2026-11-20, Anders: they were invisible walls), along the straight lines the map draws them with.
        // Gardens (olive green) get no tape since 2026-11-23: hedges, walls and fences show where they start (Anders:
        // "snyggare om man kan göra det bara naturligt"; TvWorldBuilderYards). A map built before 2026-11-23 keeps no
        // outlines: its gardens and out-of-bounds cells all get the garden edges then.
        TvGardens outlines = data != null && data.town != null ? data.town.gardens : null;
        if (outlines != null && flagE.Length > 0)
        {
            double minE = anchorE, maxE = anchorE, minN = anchorN, maxN = anchorN;
            for (int i = 0; i < flagE.Length; i++)
            {
                minE = Math.Min(minE, flagE[i]); maxE = Math.Max(maxE, flagE[i]);
                minN = Math.Min(minN, flagN[i]); maxN = Math.Max(maxN, flagN[i]);
            }
            const double Margin = 150.0;
            foreach (float[] ring in outlines.Outlines(minE - Margin, minN - Margin, maxE + Margin, maxN + Margin, MapImport.WorldAreaKind.OutOfBounds))
            {
                var pts = new List<(double e, double n)>(ring.Length / 2);
                for (int i = 0; i + 3 < ring.Length; i += 2) pts.Add((ring[i], ring[i + 1]));   // the last point is the first again
                if (pts.Count >= 3) TapeLine(pts, true);
            }
        }
        if (v.Count == 0) return;
        tapeMesh = new Mesh { name = "Out of bounds tape" };
        if (v.Count > 65000) tapeMesh.indexFormat = IndexFormat.UInt32;
        tapeMesh.SetVertices(v);
        tapeMesh.SetNormals(nrm);
        tapeMesh.SetColors(col);
        tapeMesh.SetTriangles(tri, 0);
        tapeMesh.RecalculateBounds();
        var go = new GameObject("Out of bounds tape");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = tapeMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = plainMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = look.shadows > 0;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void Make()
    {
        look = RunSettings.Current;
        plainMaterial = TvWorldView.NewObjectMaterial();
        groundMaterial = TvWorldView.NewMaterial(Color.white);
        groundMaterial.SetTexture("_DetailTex", TvWorldView.Grain());
        groundMaterial.SetFloat("_Detail", 1f);
        if (data.town != null)
        {
            // A town's streets and squares: asphalt, or cobbles in the old stone town
            pavedMaterial = TvWorldView.NewMaterial(Color.white);
            bool cobbles = data.town.style == TvCityShapes.Style.OldStone;
            pavedMaterial.SetTexture("_DetailTex", cobbles ? RunPeople.Cobbles() : RunPeople.Asphalt());
            pavedMaterial.SetFloat("_Detail", cobbles ? 0.9f : 0.75f);
            people = gameObject.AddComponent<RunPeople>();
            people.Init(plainMaterial, look.shadows > 0);
        }
        else
        {
            // The forest's tarmac roads (2026-11-20)
            pavedMaterial = TvWorldView.NewMaterial(Color.white);
            pavedMaterial.SetTexture("_DetailTex", RunPeople.Asphalt());
            pavedMaterial.SetFloat("_Detail", 0.75f);
        }
        foreach (Material m in new[] { plainMaterial, groundMaterial, pavedMaterial })
        {
            if (m == null) continue;
            // The fog thickens towards the view distance, where the world ends
            m.SetFloat("_FogStart", look.view * 0.3f);
            m.SetFloat("_FogEnd", look.view * 0.96f);
        }

        // The sun in the south-east, as at the TV controls
        sun = new GameObject("Run sun").AddComponent<Light>();
        sun.transform.SetParent(transform, false);
        sun.type = LightType.Directional;
        sun.color = new Color32(0xff, 0xf1, 0xd6, 255);
        sun.intensity = 3f;
        sun.shadows = look.shadows == 0 ? LightShadows.None : look.shadows == 1 ? LightShadows.Hard : LightShadows.Soft;
        sun.shadowStrength = 0.9f;
        sun.shadowBias = 0.05f;
        sun.shadowNormalBias = 0.4f;
        sun.transform.rotation = Quaternion.LookRotation(-new Vector3(55f, 70f, -48f).normalized);

        savedShadowDistance = QualitySettings.shadowDistance;
        savedShadows = QualitySettings.shadows;
        savedCascades = QualitySettings.shadowCascades;
        savedResolution = QualitySettings.shadowResolution;
        savedProjection = QualitySettings.shadowProjection;
        QualitySettings.shadowDistance = look.shadowDistance;
        QualitySettings.shadows = look.shadows == 0 ? ShadowQuality.Disable : look.shadows == 1 ? ShadowQuality.HardOnly : ShadowQuality.All;
        QualitySettings.shadowCascades = look.shadows == 2 ? 2 : 1;
        QualitySettings.shadowResolution = look.shadows == 2 ? ShadowResolution.High : ShadowResolution.Medium;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
    }

    public static Color SkyColor => Sky;
    public float ViewDistance => look.view;
    public int Msaa => look.msaa;

    void OnDestroy()
    {
        QualitySettings.shadowDistance = savedShadowDistance;
        QualitySettings.shadows = savedShadows;
        QualitySettings.shadowCascades = savedCascades;
        QualitySettings.shadowResolution = savedResolution;
        QualitySettings.shadowProjection = savedProjection;
        foreach (Piece p in pieces.Values) Drop(p);
        pieces.Clear();
        if (plainMaterial != null) Destroy(plainMaterial);
        if (groundMaterial != null) Destroy(groundMaterial);
        if (pavedMaterial != null) Destroy(pavedMaterial);
        if (tapeMesh != null) Destroy(tapeMesh);
    }

    // ---- Map points and the scene ----

    public Vector3 ToScene(double e, double n, float y) => Origin + new Vector3((float)(e - anchorE), y, (float)(n - anchorN));

    public void FromScene(Vector3 p, out double e, out double n)
    {
        e = anchorE + (p.x - Origin.x);
        n = anchorN + (p.z - Origin.z);
    }

    // The height of the ground (y above the base height), with the bumps, ditches and cliffs of the piece when it is built
    public float GroundY(double e, double n)
    {
        int ix = (int)Math.Floor((e - anchorE) / PieceSize), iz = (int)Math.Floor((n - anchorN) / PieceSize);
        if (pieces.TryGetValue(Key(ix, iz), out Piece p) && p.world != null)
        {
            float g = p.world.HeightAt((float)(e - p.ce), (float)(n - p.cn));
            // Beside a tunnel, over its walls: the top of its slab (the ground under it rises behind the walls)
            if (p.world.tunnels.Count > 0)
            {
                TunnelSpot s = Tunnel(e, n);
                if (s.slab) return Mathf.Max(g, s.roof);
            }
            return g;
        }
        return terrain.MapHeight(e, n) - baseHeight;
    }

    // The height of the ground on a level: over a tunnel (upper), the top of its roof, where the road goes over
    public float GroundY(double e, double n, bool upper)
    {
        if (upper)
        {
            TunnelSpot s = Tunnel(e, n);
            if (s.inside) return Mathf.Max(s.roof, GroundY(e, n));
        }
        return GroundY(e, n);
    }

    // Where a point is to a tunnel of the town (TvWorld.tunnels): inside it (under its roof), between its mouths (on
    // the road over it, or in it), and the height of its roof's top there
    public struct TunnelSpot
    {
        public bool inside, between, slab;     // slab: over the tunnel's wall, beside its inside
        public float roof;
    }

    public TunnelSpot Tunnel(double e, double n)
    {
        var s = new TunnelSpot();
        int ix = (int)Math.Floor((e - anchorE) / PieceSize), iz = (int)Math.Floor((n - anchorN) / PieceSize);
        if (!pieces.TryGetValue(Key(ix, iz), out Piece p) || p.world == null) return s;
        List<float> t = p.world.tunnels;
        float x = (float)(e - p.ce), z = (float)(n - p.cn);
        for (int i = 0; i + 7 < t.Count; i += 8)
        {
            float dx = x - t[i], dz = z - t[i + 1], along = dx * t[i + 2] + dz * t[i + 3], across = Mathf.Abs(-dx * t[i + 3] + dz * t[i + 2]);
            float len = t[i + 4], half = t[i + 5];
            if (along < 0f || along > len || across > half + 45f) continue;
            s.between = true;
            if (across <= half + 0.1f)
            {
                s.inside = true;
                s.roof = Mathf.Lerp(t[i + 6], t[i + 7], along / Mathf.Max(0.1f, len)) + TvWorldBuilder.TunnelRoof;
                return s;
            }
            if (across <= half + 1.9f)
            {
                s.slab = true;
                s.roof = Mathf.Lerp(t[i + 6], t[i + 7], along / Mathf.Max(0.1f, len)) + TvWorldBuilder.TunnelRoof;
            }
        }
        return s;
    }

    // Moves a point out of the trunks, stones, houses and poles of the pieces round it
    public void PushOut(ref double e, ref double n, float reach)
    {
        float x = (float)(e - anchorE), z = (float)(n - anchorN);
        int ix = (int)Math.Floor(x / PieceSize), iz = (int)Math.Floor(z / PieceSize);
        float wr = reach + 0.15f;
        for (int pass = 0; pass < 2; pass++)
        {
            // A house is built by the piece its middle is in, so a long one reaches two pieces away
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (!pieces.TryGetValue(Key(ix + dx, iz + dz), out Piece p)) continue;
                    // The house walls: kept at reach and a little more (the plinth stands out), on whichever side the runner is
                    float[] w = p.walls;
                    if (w != null)
                        for (int i = 0; i + 3 < w.Length; i += 4)
                        {
                            float ax = w[i], az = w[i + 1], bx = w[i + 2], bz = w[i + 3];
                            if (x < Mathf.Min(ax, bx) - wr || x > Mathf.Max(ax, bx) + wr
                                || z < Mathf.Min(az, bz) - wr || z > Mathf.Max(az, bz) + wr) continue;
                            float sx = bx - ax, sz = bz - az, len2 = sx * sx + sz * sz;
                            float t = len2 > 1e-6f ? Mathf.Clamp01(((x - ax) * sx + (z - az) * sz) / len2) : 0f;
                            float qx = x - (ax + sx * t), qz = z - (az + sz * t), d2 = qx * qx + qz * qz;
                            if (d2 >= wr * wr) continue;
                            float d = Mathf.Sqrt(d2);
                            if (d < 0.001f)
                            {
                                // On the wall: out to the outside (the wall turns left round the house, so right of it)
                                float l = Mathf.Sqrt(len2);
                                if (l < 1e-4f) continue;
                                qx = sz / l; qz = -sx / l; d = 0f;
                                x += qx * wr;
                                z += qz * wr;
                                continue;
                            }
                            x += qx / d * (wr - d);
                            z += qz / d * (wr - d);
                        }
                }
            }
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!pieces.TryGetValue(Key(ix + dx, iz + dz), out Piece p) || p.solids == null) continue;
                    float[] s = p.solids;
                    for (int i = 0; i + 2 < s.Length; i += 3)
                    {
                        float ox = x - s[i], oz = z - s[i + 1], r = s[i + 2] + reach;
                        if (ox > r || ox < -r || oz > r || oz < -r) continue;
                        float d2 = ox * ox + oz * oz;
                        if (d2 >= r * r) continue;
                        float d = Mathf.Sqrt(d2);
                        if (d < 0.001f) { x += r; continue; }
                        x += ox / d * (r - d);
                        z += oz / d * (r - d);
                    }
                }
            }
        }
        e = anchorE + x;
        n = anchorN + z;
    }

    // Where the flag of a control (or the finish, last) stands; until its piece is built, the course's own point
    public void Flag(int index, out double e, out double n)
    {
        e = flagE[index];
        n = flagN[index];
    }

    public int FlagCount => flagE.Length;

    // ---- Building round the runner ----

    public void SetPlayer(double e, double n)
    {
        playerE = e;
        playerN = n;
        hasPlayer = true;
        if (people != null) people.SetPlayer(ToScene(e, n, 0f));
    }

    // Share of the pieces within reach that are built and handed over (for the loading bar)
    public float Ready(double e, double n, float reach)
    {
        int total = 0, ready = 0;
        foreach (Piece p in pieces.Values)
        {
            if (Distance(p, e, n) > reach) continue;
            total++;
            if (p.ready) ready++;
        }
        return total == 0 ? 0f : ready / (float)total;
    }

    float Distance(Piece p, double e, double n)
    {
        double dx = Math.Max(0, Math.Abs(e - p.ce) - Half), dz = Math.Max(0, Math.Abs(n - p.cn) - Half);
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    void Update()
    {
        if (!hasPlayer) return;
        if (Time.unscaledTime >= nextPlan)
        {
            nextPlan = Time.unscaledTime + 0.15f;
            Plan();
        }
        while (finished.TryDequeue(out Done done)) uploading.Enqueue(done);
        HandOver();
    }

    void Plan()
    {
        float view = look.view;
        int ix0 = (int)Math.Floor((playerE - view - anchorE) / PieceSize), ix1 = (int)Math.Floor((playerE + view - anchorE) / PieceSize);
        int iz0 = (int)Math.Floor((playerN - view - anchorN) / PieceSize), iz1 = (int)Math.Floor((playerN + view - anchorN) / PieceSize);
        for (int iz = iz0; iz <= iz1; iz++)
        {
            for (int ix = ix0; ix <= ix1; ix++)
            {
                long key = Key(ix, iz);
                if (pieces.ContainsKey(key)) continue;
                var p = new Piece { ix = ix, iz = iz, ce = anchorE + (ix + 0.5) * PieceSize, cn = anchorN + (iz + 0.5) * PieceSize };
                if (Distance(p, playerE, playerN) > view) continue;
                // Pieces wholly off the map are never built
                if (!terrain.OnMap(p.ce - Half + 1, p.cn - Half + 1) && !terrain.OnMap(p.ce + Half - 1, p.cn + Half - 1)
                    && !terrain.OnMap(p.ce - Half + 1, p.cn + Half - 1) && !terrain.OnMap(p.ce + Half - 1, p.cn - Half + 1)) continue;
                pieces[key] = p;
            }
        }

        var drop = new List<long>();
        Piece nextBase = null, nextDetail = null;
        foreach (var pair in pieces)
        {
            Piece p = pair.Value;
            p.dist = Distance(p, playerE, playerN);
            if (p.dist > view + 40f)
            {
                drop.Add(pair.Key);
                continue;
            }
            if (!p.ready && !p.building && (nextBase == null || p.dist < nextBase.dist)) nextBase = p;
            if (p.ready && p.detail == null && !p.detailBuilding && p.dist <= look.detail && (nextDetail == null || p.dist < nextDetail.dist)) nextDetail = p;
            if (p.detail != null && p.dist > look.detail + 16f) DropDetail(p);
            if (p.ready)
            {
                bool nearOn = p.dist < look.near;
                if (nearOn != p.nearOn)
                {
                    p.nearOn = nearOn;
                    foreach (Renderer r in p.near) r.enabled = nearOn;
                    foreach (Renderer r in p.far) r.enabled = !nearOn;
                    if (p.leanHidden) foreach (Renderer r in p.lean) r.enabled = false;
                }
                // A town's people walk and watch only round the runner
                if (people != null)
                {
                    bool want = p.dist < look.near + 30f && p.world != null && p.world.people.Count > 0;
                    if (want != p.peopleOn)
                    {
                        p.peopleOn = want;
                        if (want) people.Spawn(pair.Key, p.root.transform, p.world);
                        else people.Despawn(pair.Key);
                    }
                }
            }
        }
        foreach (long key in drop)
        {
            Drop(pieces[key]);
            pieces.Remove(key);
        }

        // The nearest piece first; a piece right round the runner before any undergrowth
        while (jobs < MaxJobs && (nextBase != null || nextDetail != null))
        {
            if (nextBase != null && (nextDetail == null || nextBase.dist <= nextDetail.dist + 40f))
            {
                StartBase(nextBase);
                nextBase = NextUnbuilt();
            }
            else
            {
                StartDetail(nextDetail);
                nextDetail = null;
            }
        }
    }

    Piece NextUnbuilt()
    {
        Piece best = null;
        foreach (Piece p in pieces.Values)
            if (!p.ready && !p.building && (best == null || p.dist < best.dist)) best = p;
        return best;
    }

    TvWorldInput Input(Piece p)
    {
        return new TvWorldInput
        {
            layer = data.layer,
            heights = data.heights,
            look = data.look,
            chunk = true,
            radius = Half,
            centerE = p.ce,
            centerN = p.cn,
            anchorE = anchorE,
            anchorN = anchorN,
            baseHeight = baseHeight,
            seed = 1,
            town = data.town,
            passable = passable,
            // The houses in full detail only on the High 3D quality; the others draw the leaner ones close by too
            townRich = look.undergrowth > 0.15f
        };
    }

    void StartBase(Piece p)
    {
        p.building = true;
        TvWorldInput input = Input(p);
        // The flags that stand in this piece
        input.flags = new List<float>();
        p.flags = new List<int>();
        for (int i = 0; i < flagPoints.Count; i++)
        {
            double x = flagPoints[i].e - p.ce, z = flagPoints[i].n - p.cn;
            if (x < -Half || x >= Half || z < -Half || z >= Half) continue;
            input.flags.Add((float)x);
            input.flags.Add((float)z);
            p.flags.Add(i);
        }
        jobs++;
        Task.Run(() =>
        {
            var done = new Done { piece = p };
            try
            {
                done.world = TvWorldBuilder.Build(input);
                done.arrays = new List<TvWorldView.MeshArrays>(done.world.meshes.Count);
                foreach (TvMesh m in done.world.meshes) done.arrays.Add(new TvWorldView.MeshArrays(m));
                done.world.meshes.Clear();
            }
            catch (Exception e)
            {
                Debug.LogError("RunWorld: could not build the piece at " + p.ce.ToString("0") + ", " + p.cn.ToString("0") + ": " + e);
            }
            finished.Enqueue(done);
        });
    }

    void StartDetail(Piece p)
    {
        p.detailBuilding = true;
        TvWorldInput input = Input(p);
        input.chunkDetail = true;
        input.baseSolids = p.world.solids;
        input.undergrowthShare = look.undergrowth;
        jobs++;
        Task.Run(() =>
        {
            var done = new Done { piece = p, detail = true };
            try
            {
                TvWorld w = TvWorldBuilder.Build(input);
                done.arrays = new List<TvWorldView.MeshArrays>(w.meshes.Count);
                foreach (TvMesh m in w.meshes) done.arrays.Add(new TvWorldView.MeshArrays(m));
            }
            catch (Exception e)
            {
                Debug.LogError("RunWorld: could not build the undergrowth at " + p.ce.ToString("0") + ", " + p.cn.ToString("0") + ": " + e);
            }
            finished.Enqueue(done);
        });
    }

    // Meshes go to the graphics card a few at a time: about 4 ms a frame while running, more while loading
    void HandOver()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int budget = Loading ? 40 : 4;
        while (uploading.Count > 0 && watch.ElapsedMilliseconds < budget)
        {
            Done done = uploading.Dequeue();
            jobs--;
            Piece p = done.piece;
            if (done.detail) p.detailBuilding = false;
            else p.building = false;
            long key = Key(p.ix, p.iz);
            bool kept = pieces.TryGetValue(key, out Piece now) && now == p;
            if (!kept || done.arrays == null)
            {
                // Left behind while it was being built, or it failed: built again if it is needed after all
                continue;
            }
            if (done.detail) MakeDetail(p, done.arrays);
            else MakeBase(p, done);
        }
    }

    GameObject Root(Piece p, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = ToScene(p.ce, p.cn, 0f);
        return go;
    }

    Renderer AddMesh(Transform parent, TvWorldView.MeshArrays source, List<Mesh> keep)
    {
        Mesh mesh = TvWorldView.ToMesh(source);
        mesh.UploadMeshData(true);
        keep.Add(mesh);
        var go = new GameObject(source.name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = source.name == "Ground paved" && pavedMaterial != null ? pavedMaterial
            : source.name.StartsWith("Ground", StringComparison.Ordinal) ? groundMaterial : plainMaterial;
        r.shadowCastingMode = source.castShadows && look.shadows > 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
        r.receiveShadows = look.shadows > 0;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        r.allowOcclusionWhenDynamic = false;
        return r;
    }

    void MakeBase(Piece p, Done done)
    {
        p.world = done.world;
        p.root = Root(p, "Piece " + p.ix + "," + p.iz);
        p.nearOn = p.dist < look.near;
        foreach (TvWorldView.MeshArrays m in done.arrays)
        {
            Renderer r = AddMesh(p.root.transform, m, p.meshes);
            if (m.group == TvMesh.Near) { p.near.Add(r); r.enabled = p.nearOn; }
            if (m.name == "Town houses") p.lean.Add(r);
            else if (m.group == TvMesh.Far) { p.far.Add(r); r.enabled = !p.nearOn; }
        }
        // The solids, in metres from Origin
        List<TvSolid> list = p.world.solids;
        float ox = (float)(p.ce - anchorE), oz = (float)(p.cn - anchorN);
        p.solids = new float[list.Count * 3];
        for (int i = 0; i < list.Count; i++)
        {
            p.solids[i * 3] = list[i].x + ox;
            p.solids[i * 3 + 1] = list[i].z + oz;
            p.solids[i * 3 + 2] = list[i].r;
        }
        List<float> walls = p.world.walls;
        p.walls = new float[walls.Count];
        for (int i = 0; i + 3 < walls.Count; i += 4)
        {
            p.walls[i] = walls[i] + ox;
            p.walls[i + 1] = walls[i + 1] + oz;
            p.walls[i + 2] = walls[i + 2] + ox;
            p.walls[i + 3] = walls[i + 3] + oz;
        }
        // Where the flags really stand (beside the stone or knoll they mark)
        for (int i = 0; i < p.flags.Count && i * 2 + 1 < p.world.flagSpots.Count; i++)
        {
            flagE[p.flags[i]] = p.ce + p.world.flagSpots[i * 2];
            flagN[p.flags[i]] = p.cn + p.world.flagSpots[i * 2 + 1];
        }
        p.ready = true;
    }

    void MakeDetail(Piece p, List<TvWorldView.MeshArrays> arrays)
    {
        if (p.root == null) return;
        if (p.dist > look.detail + 16f) return;   // the runner has moved on meanwhile
        p.detail = new GameObject("Detail");
        p.detail.transform.SetParent(p.root.transform, false);
        foreach (TvWorldView.MeshArrays m in arrays)
        {
            AddMesh(p.detail.transform, m, p.detailMeshes);
            // The full houses stand where the leaner ones were
            if (m.name == "Town houses full")
            {
                p.leanHidden = true;
                foreach (Renderer r in p.lean) r.enabled = false;
            }
        }
    }

    void DropDetail(Piece p)
    {
        foreach (Mesh m in p.detailMeshes) if (m != null) Destroy(m);
        p.detailMeshes.Clear();
        if (p.detail != null) Destroy(p.detail);
        p.detail = null;
        if (p.leanHidden)
        {
            p.leanHidden = false;
            foreach (Renderer r in p.lean) if (r != null) r.enabled = p.nearOn;
        }
    }

    void Drop(Piece p)
    {
        p.gone = true;
        if (p.peopleOn && people != null) people.Despawn(Key(p.ix, p.iz));
        p.peopleOn = false;
        DropDetail(p);
        p.lean.Clear();
        foreach (Mesh m in p.meshes) if (m != null) Destroy(m);
        p.meshes.Clear();
        if (p.root != null) Destroy(p.root);
        p.root = null;
        p.world = null;
        p.solids = null;
        p.ready = false;
    }
}
