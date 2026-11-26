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
//  - the trees are copies of a few ready-made ones drawn by the graphics card (RunTrees, 2026-11-25): the full trees
//    close by, plainer shapes further out, and beyond that a picture of each tree on a card. Only on a computer that
//    can't draw copies are tree meshes made per piece, as below:
//  - the full trees are only made for the pieces near the runner (2026-11-25, Anders: big maps lagged): a piece
//    further out is built with the far copy alone, built again for its full trees when the runner comes within
//    reach, and they are thrown away when the runner has gone on;
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
    AnisotropicFiltering savedAniso;
    bool anisoSaved;

    Material plainMaterial, groundMaterial, pavedMaterial, atlasMaterial;
    int quality;                        // RunSettings.Level when the race started, 0..3
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
        // A forest piece's undergrowth comes in four 32 m squares (2026-11-26, Anders chose it): each is a Piece of its
        // own for its detail and its cross-fade (parent: the 64 m piece it belongs to); a town's stays whole (its houses)
        public Piece[] quads;
        public Piece parent;
        public float dist;
        public float onCourse = -1f;        // how far the piece is from the course's legs (-1: not worked out yet)
        public bool nearOn;
        public readonly List<Renderer> lean = new List<Renderer>();   // a town's leaner houses, hidden under the full ones
        public bool leanHidden, peopleOn;
        // The full trees: made with the piece when it was close, else by a build of their own when the runner comes near
        public bool treesNear, treesBuilding, treesOwn;
        public readonly List<Renderer> farTrees = new List<Renderer>();   // the far copy, drawn until the full trees are there
        public readonly List<Renderer> treeRenderers = new List<Renderer>();
        public readonly List<Mesh> treeMeshes = new List<Mesh>();
        // RunTrees: where the piece's trees stand, its picture cards, and which kind of tree it draws now
        // (0 full, 1 far shapes, 2 pictures)
        public RunTrees.Set treeSet;
        public Mesh cardMesh;
        public Renderer cardRenderer;
        public int treeTier;
        public float minY, maxY;
        // A cross-fade between two levels of detail going on (FadeTime): what comes, what goes, what then to do
        public bool hidden;
        public float cardFadeStart = -1f;   // the picture cards fading in (cardsComing) or out
        public bool cardsComing;                 // wholly behind a hill from the runner's eyes (Hidden): not drawn at all
        public float fadeStart = -1f;
        public readonly List<Renderer> fadeIn = new List<Renderer>(), fadeOut = new List<Renderer>();
        public Action fadeDone;
    }

    class Done
    {
        public Piece piece;
        public bool detail, trees;
        public TvWorld world;
        public List<TvWorldView.MeshArrays> arrays;
        public RunTrees.Set treeSet;
        public int made;            // how many of arrays have their mesh made (HandOver, one at a time)
    }

    readonly Dictionary<long, Piece> pieces = new Dictionary<long, Piece>();
    readonly ConcurrentQueue<Done> finished = new ConcurrentQueue<Done>();
    readonly Queue<Done> uploading = new Queue<Done>();
    int jobs;
    // Two builds at once while loading (fast start); in the race only one on Ultra: its pieces make the most garbage,
    // and one at a time keeps the memory clean-up small (2026-11-26, Anders chose it)
    int MaxJobs => Loading || rebuilding || quality < (int)RunSettings.Quality.Ultra ? 2 : 1;

    // ---- Which piece is built next (2026-11-26, Anders: build what is right round me and where the course goes; the
    // pieces far away can wait and come slowly, a little now and then) ----
    List<CoursePoint> courseLine = new List<CoursePoint>();   // the start, the controls and the finish
    const float CourseCorridor = 90f;    // a piece this close to a leg counts as on the course
    const float FarCalm = 0.35f;         // seconds between two far pieces started while running
    float lastFarStart = -10f;

    float CourseDistance(Piece p)
    {
        if (p.onCourse >= 0f) return p.onCourse;
        double best = double.MaxValue;
        for (int i = 0; i + 1 < courseLine.Count; i++)
        {
            double ax = courseLine[i].e, az = courseLine[i].n, bx = courseLine[i + 1].e, bz = courseLine[i + 1].n;
            double dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
            double t = l2 > 1e-6 ? Math.Max(0.0, Math.Min(1.0, ((p.ce - ax) * dx + (p.cn - az) * dz) / l2)) : 0.0;
            double qx = p.ce - (ax + dx * t), qz = p.cn - (az + dz * t);
            best = Math.Min(best, Math.Sqrt(qx * qx + qz * qz));
        }
        p.onCourse = courseLine.Count < 2 ? 1e6f : (float)Math.Max(0.0, best - Half);
        return p.onCourse;
    }

    // How soon a piece is wanted: its distance, less on the course's legs
    float Want(Piece p) => CourseDistance(p) < CourseCorridor ? p.dist * 0.6f : p.dist;

    // Close enough that it must come at once (else it waits its turn, one at a time)
    bool Urgent(Piece p) => Loading || Want(p) < look.near + PieceSize;
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
        world.courseLine = new List<CoursePoint>(flags.Count + 1) { start };
        world.courseLine.AddRange(flags);
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

    // The memory cleaning (GC) gets more time each frame while running, so it keeps up with the building of the world
    // and does not have to stop the whole game for a long cleaning now and then (2026-11-26: 20-480 ms stops)
    ulong savedGcSlice;
    bool gcSliceSaved;

    void Make()
    {
        if (UnityEngine.Scripting.GarbageCollector.isIncremental)
        {
            savedGcSlice = UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds;
            gcSliceSaved = true;
            UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = 6000000;
        }
        look = RunSettings.Current;
        quality = (int)RunSettings.Level;
        plainMaterial = TvWorldView.NewObjectMaterial();
        // The forest floor's moss and needles on the ground; only a hint of it on a town's lawns
        groundMaterial = TvWorldView.NewGroundMaterial(data.town != null ? 0.35f : 1f);
        // Bark, needles, leaves, undergrowth and litter (TvAtlasLit); no wind on Low
        atlasMaterial = TvWorldView.NewAtlasMaterial();
        atlasMaterial.SetFloat("_Wind", quality == 0 ? 0f : 1f);
        // How sharp the trees and stones are up close (2026-11-25, Anders: clear steps between the levels): Low has no
        // relief and no grain, Medium relief out to 15 m, High the sharp bark and relief to 30 m, Ultra to 50 m
        TvWorldView.SharpBark(atlasMaterial, quality >= 2);
        atlasMaterial.SetFloat("_BumpFar", look.reliefFar);
        surfaceGrain = atlasMaterial.GetFloat("_Surface");
        if (quality == 0) atlasMaterial.SetFloat("_Surface", 0f);
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
        foreach (Material m in new[] { plainMaterial, groundMaterial, pavedMaterial, atlasMaterial })
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
        QualitySettings.shadowCascades = look.shadows >= 3 ? 4 : look.shadows == 2 ? 2 : 1;
        QualitySettings.shadowResolution = look.shadows >= 3 ? ShadowResolution.VeryHigh : look.shadows == 2 ? ShadowResolution.High : ShadowResolution.Medium;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;
        // Low: the pictures (bark, needles, ground) at half size, which weak graphics chips read far quicker
        // (2026-11-26, Anders: Low must run smoothly on a weak laptop, even if it looks plainer)
        savedMipLimit = QualitySettings.globalTextureMipmapLimit;
        if (quality <= (int)RunSettings.Quality.Low) QualitySettings.globalTextureMipmapLimit = Math.Max(1, savedMipLimit);

        MakeTrees();
        savedAniso = QualitySettings.anisotropicFiltering;
        anisoSaved = true;
        QualitySettings.anisotropicFiltering = look.aniso;
    }

    float surfaceGrain = 1f;

    void MakeTrees()
    {
        // The trees as copies of a few (null: this computer can't, the pieces make tree meshes)
        trees?.Destroy();
        trees = RunTrees.Create(quality, atlasMaterial, look.shadows > 0, sun);
        if (trees != null)
        {
            // The finest trees only right round the runner; shadows only as far as they are drawn (RunTrees)
            // 2026-11-26: the finest trees to 20 m on Ultra, 15 m on High (Anders: Ultra need not have full detail at 20 m) (on Low and Medium the trees are one level
            // out to the near distance anyway); the trees' shadows only to 50 m (70 on Ultra)
            trees.shadowReach = Mathf.Min(look.shadowDistance + 4f, quality >= (int)RunSettings.Quality.Ultra ? 70f : 50f);
            // The changes between the levels are gradual over a stretch: longest on Ultra (2026-11-26, Anders: start
            // a little earlier and take a longer stretch)
            trees.fullBand = quality >= (int)RunSettings.Quality.Ultra ? 14f : 10f;
            trees.nearBand = quality >= (int)RunSettings.Quality.Ultra ? 35f : quality >= (int)RunSettings.Quality.High ? 25f : quality >= 1 ? 15f : 10f;
            trees.fullReach = quality >= (int)RunSettings.Quality.Ultra ? 24f : quality >= (int)RunSettings.Quality.High ? 18f
                : Mathf.Min(look.near, look.shadows > 0 ? trees.shadowReach : look.near);
            trees.nearReach = look.near;
            // Fewer far trees further out (2026-11-26, Anders: 8 000-10 000 far trees on Ultra): all of them up to
            // 40 % of the view, a third of them at 90 %, each fading out on its share of the pixels
            trees.farThinStart = Mathf.Max(look.near + 30f, look.view * 0.4f);
            trees.farThinEnd = Mathf.Max(trees.farThinStart + 20f, look.view * 0.9f);
            trees.farThinShare = 0.33f;
        }
        if (trees != null)
        {
            trees.SetFog(look.view * 0.3f, look.view * 0.96f);
            Debug.Log("RunWorld: trees drawn as copies" + (trees.HasImpostors ? ", pictures beyond " + FarTreesReach.ToString("0") + " m" : " (no pictures)"));
        }
    }

    // ---- A new graphics level in the middle of a race (2026-11-26, Anders: changing it did nothing until the next
    // race; it should count at once) ----
    // Everything that depends on the level is set again and the pieces are built anew round the runner, nearest
    // first and quickly (as while loading); until a piece is ready the ground is the map's own heights.

    bool rebuilding;
    float nextLevelCheck;

    public void ChangeQuality()
    {
        int q = (int)RunSettings.Level;
        if (q == quality) return;
        quality = q;
        look = RunSettings.Current;
        atlasMaterial.SetFloat("_Wind", quality == 0 ? 0f : 1f);
        TvWorldView.SharpBark(atlasMaterial, quality >= 2);
        atlasMaterial.SetFloat("_BumpFar", look.reliefFar);
        atlasMaterial.SetFloat("_Surface", quality == 0 ? 0f : surfaceGrain);
        foreach (Material m in new[] { plainMaterial, groundMaterial, pavedMaterial, atlasMaterial })
        {
            if (m == null) continue;
            m.SetFloat("_FogStart", look.view * 0.3f);
            m.SetFloat("_FogEnd", look.view * 0.96f);
        }
        if (sun != null) sun.shadows = look.shadows == 0 ? LightShadows.None : look.shadows == 1 ? LightShadows.Hard : LightShadows.Soft;
        QualitySettings.shadowDistance = look.shadowDistance;
        QualitySettings.shadows = look.shadows == 0 ? ShadowQuality.Disable : look.shadows == 1 ? ShadowQuality.HardOnly : ShadowQuality.All;
        QualitySettings.shadowCascades = look.shadows >= 3 ? 4 : look.shadows == 2 ? 2 : 1;
        QualitySettings.shadowResolution = look.shadows >= 3 ? ShadowResolution.VeryHigh : look.shadows == 2 ? ShadowResolution.High : ShadowResolution.Medium;
        if (savedMipLimit >= 0) QualitySettings.globalTextureMipmapLimit = quality <= (int)RunSettings.Quality.Low ? Math.Max(1, savedMipLimit) : savedMipLimit;
        QualitySettings.anisotropicFiltering = look.aniso;
        if (people != null) people.Init(plainMaterial, look.shadows > 0);
        if (viewCamera != null)
        {
            viewCamera.farClipPlane = look.view + 20f;
            viewCamera.allowMSAA = look.msaa > 1;
        }
        MakeTrees();
        foreach (Piece p in pieces.Values) Drop(p);
        pieces.Clear();
        hiddenPieces.Clear();
        rebuilding = true;
        Loading = true;
        nextPlan = 0f;
        Debug.Log("RunWorld: graphics changed to " + RunSettings.QualityName((RunSettings.Quality)quality) + " during the race");
    }

    public static Color SkyColor => Sky;
    public float ViewDistance => look.view;
    public int Msaa => look.msaa;

    void OnDestroy()
    {
        if (gcSliceSaved) UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = savedGcSlice;
        Shader.SetGlobalVector(ThinId, Vector4.zero);
        probe?.Dispose();
        probe = null;
        QualitySettings.shadowDistance = savedShadowDistance;
        if (savedMipLimit >= 0) QualitySettings.globalTextureMipmapLimit = savedMipLimit;
        QualitySettings.shadows = savedShadows;
        QualitySettings.shadowCascades = savedCascades;
        QualitySettings.shadowResolution = savedResolution;
        QualitySettings.shadowProjection = savedProjection;
        if (anisoSaved) QualitySettings.anisotropicFiltering = savedAniso;
        foreach (Piece p in pieces.Values) Drop(p);
        pieces.Clear();
        if (plainMaterial != null) Destroy(plainMaterial);
        if (groundMaterial != null) Destroy(groundMaterial);
        if (pavedMaterial != null) Destroy(pavedMaterial);
        if (atlasMaterial != null) Destroy(atlasMaterial);
        if (tapeMesh != null) Destroy(tapeMesh);
        trees?.Destroy();
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
            float x = (float)(e - p.ce), z = (float)(n - p.cn);
            float g = p.world.HeightAt(x, z);
            // On a cliff's rock top: its top is the ground, not the step hidden under it (2026-11-26, Anders on Fossum)
            if (p.world.caps.Count > 0 && p.world.CapAt(x, z, out float cap) && cap > g) g = cap;
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
    // full (the start): the undergrowth round the runner is there too and has finished fading in, so nothing comes
    // into sight once the race is shown
    public float Ready(double e, double n, float reach, bool full = false)
    {
        int total = 0, ready = 0;
        foreach (Piece p in pieces.Values)
        {
            float d = Distance(p, e, n);
            if (d > reach) continue;
            total++;
            if (!p.ready) continue;
            if (full && d <= look.detail && !DetailDone(p, e, n)) continue;
            ready++;
        }
        return total == 0 ? 0f : ready / (float)total;
    }

    // The undergrowth round (e, n) in this piece is there and has faded in
    bool DetailDone(Piece p, double e, double n)
    {
        Piece[] parts = DetailParts(p);
        if (parts == null) return p.detail != null && p.fadeStart < 0f;
        foreach (Piece q in parts)
            if (Distance(q, e, n) <= look.detail && (q.detail == null || q.fadeStart >= 0f)) return false;
        return true;
    }

    float Distance(Piece p, double e, double n)
    {
        double half = p.parent != null ? QuadHalf : Half;
        double dx = Math.Max(0, Math.Abs(e - p.ce) - half), dz = Math.Max(0, Math.Abs(n - p.cn) - half);
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // ---- The undergrowth in 32 m squares ----
    // Smaller builds make less garbage at once (fewer and shorter memory clean-ups), and only the squares within the
    // undergrowth's reach are built, not the whole 64 m piece (2026-11-26, Anders: more undergrowth some day)

    const int QuadHalf = Half / 2;

    // The pieces whose undergrowth is built: the four squares of a forest piece, a town piece itself
    Piece[] DetailParts(Piece p)
    {
        if (data.town != null) return null;
        if (p.quads == null)
        {
            p.quads = new Piece[4];
            for (int k = 0; k < 4; k++)
            {
                double qe = p.ce + (k % 2 == 0 ? -QuadHalf : QuadHalf), qn = p.cn + (k < 2 ? -QuadHalf : QuadHalf);
                p.quads[k] = new Piece { ix = p.ix, iz = p.iz, ce = qe, cn = qn, parent = p, ready = true };
            }
        }
        return p.quads;
    }

    // Whether a piece (or one of its squares) is still the one wanted where it stands
    bool Current(Piece p)
    {
        Piece owner = p.parent ?? p;
        return pieces.TryGetValue(Key(owner.ix, owner.iz), out Piece now) && now == owner && !p.gone && !owner.gone;
    }

    void Update()
    {
        if (!hasPlayer) return;
        // A new graphics level chosen in the Esc menu counts at once
        if (Time.unscaledTime >= nextLevelCheck)
        {
            nextLevelCheck = Time.unscaledTime + 0.5f;
            if ((int)RunSettings.Level != quality) ChangeQuality();
        }
        // After a change of graphics: quick building until the pieces round the runner are there again
        if (rebuilding && Ready(playerE, playerN, Mathf.Min(look.view, 90f)) >= 1f)
        {
            rebuilding = false;
            Loading = false;
        }
        long t0 = perf.ElapsedTicks;
        if (Time.unscaledTime >= nextPlan)
        {
            nextPlan = Time.unscaledTime + 0.15f;
            Plan();
        }
        long t1 = perf.ElapsedTicks;
        while (finished.TryDequeue(out Done done)) uploading.Enqueue(done);
        HandOver();
        long t2 = perf.ElapsedTicks;
        Reveal();
        DrawTrees();
        Fades();
        long t3 = perf.ElapsedTicks;
        RunPerf.Add(RunPerf.Part.World, t3 - t0);
        // The frame time Unity gives now is the frame before's, so that frame's own parts go with it
        Watch(lastPlanTicks, lastHandTicks, lastTreeTicks);
        lastPlanTicks = t1 - t0;
        lastHandTicks = t2 - t1;
        lastTreeTicks = t3 - t2;
    }

    long lastPlanTicks, lastHandTicks, lastTreeTicks;

    // ---- What a slow frame was spent on, in the log (2026-11-25, Anders: big maps lagged; the log says why) ----
    // A frame over 50 ms is written as "RunPerf: ..." (at most every 2 s): the whole frame, this world's own parts of
    // it (planning, handing meshes to the graphics card, the trees), whether the memory was cleaned up (GC), and how
    // many pieces there are. Seen in the Editor's console or the player's log file.

    readonly System.Diagnostics.Stopwatch perf = System.Diagnostics.Stopwatch.StartNew();
    int lastGc = -1;
    float nextPerfLog;
    double worstFrame, worstPlan, worstHand, worstTrees;
    int slowFrames;
    int savedMipLimit = -1;
    RunPerf probe;   // the rest of the frame: Unity's own profiler markers (RunPerf.cs)

    void Watch(long plan, long hand, long tree)
    {
        double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double frame = Time.unscaledDeltaTime * 1000.0;
        int gc = GC.CollectionCount(0);
        bool cleaned = lastGc >= 0 && gc != lastGc;
        lastGc = gc;
        bool slow = !Loading && frame >= 50.0;
        if (probe == null) probe = new RunPerf();
        probe.Frame(frame, slow);
        if (!slow) return;
        slowFrames++;
        if (frame > worstFrame) { worstFrame = frame; worstPlan = plan * ms; worstHand = hand * ms; worstTrees = tree * ms; }
        if (Time.unscaledTime < nextPerfLog) return;
        nextPerfLog = Time.unscaledTime + 2f;
        int ready = 0;
        foreach (Piece p in pieces.Values) if (p.ready) ready++;
        Debug.Log("RunPerf: " + slowFrames + " slow frames, worst " + worstFrame.ToString("0") + " ms (plan " + worstPlan.ToString("0.0")
                  + ", meshes to the graphics card " + worstHand.ToString("0.0") + ", trees " + worstTrees.ToString("0.0") + " ms)"
                  + (cleaned ? ", memory cleaned (GC) this frame" : "") + "; pieces " + ready + "/" + pieces.Count
                  + ", waiting " + uploading.Count + ", jobs " + jobs + ", trees as " + (trees != null ? "copies (" + trees.Stats + ")" : "meshes")
                  + ", quality " + RunSettings.QualityName((RunSettings.Quality)quality)
                  + (slowMesh != null ? ", slowest mesh: " + slowMesh : "")
                  + "\nRunPerf whole frame: " + probe.TakeWorst());
        slowFrames = 0;
        worstFrame = 0;
        slowMesh = null;
        slowMeshMs = 0;
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
        Piece nextBase = null, nextDetail = null, nextTrees = null;
        foreach (var pair in pieces)
        {
            Piece p = pair.Value;
            p.dist = Distance(p, playerE, playerN);
            if (p.dist > view + 40f)
            {
                drop.Add(pair.Key);
                continue;
            }
            if (!p.ready && !p.building && (nextBase == null || Want(p) < Want(nextBase))) nextBase = p;
            Piece[] parts = p.ready ? DetailParts(p) : null;
            if (parts == null)
            {
                if (p.ready && p.detail == null && !p.detailBuilding && p.dist <= look.detail && (nextDetail == null || p.dist < nextDetail.dist)) nextDetail = p;
                if (p.detail != null && p.dist > look.detail + 16f) DropDetail(p, true);
            }
            else if (p.dist <= look.detail + 16f || p.quads[0].detail != null || p.quads[1].detail != null || p.quads[2].detail != null || p.quads[3].detail != null)
            {
                foreach (Piece q in parts)
                {
                    q.dist = Distance(q, playerE, playerN);
                    if (q.detail == null && !q.detailBuilding && q.dist <= look.detail && (nextDetail == null || q.dist < nextDetail.dist)) nextDetail = q;
                    if (q.detail != null && q.dist > look.detail + 16f) DropDetail(q, true);
                }
            }
            if (p.ready && !p.treesNear && !p.treesBuilding && p.dist <= TreesReach && (nextTrees == null || p.dist < nextTrees.dist)) nextTrees = p;
            if (p.treesOwn && p.dist > TreesReach + 60f) DropTrees(p);
            if (p.ready && trees != null && p.treeSet != null)
            {
                int tier = p.dist < look.near ? 0 : p.dist < FarTreesReach || p.cardRenderer == null ? 1 : 2;
                if (tier != p.treeTier)
                {
                    // The pictures cross-fade with the trees: coming, they fade in over the trees, which go on being
                    // drawn until the pictures are whole; going, the trees are there at once and the pictures fade out
                    if (p.cardRenderer != null && (tier == 2) != (p.treeTier == 2)) CardFade(p, tier == 2);
                    p.treeTier = tier;
                }
            }
            if (p.ready && !p.hidden && p.root != null && Hidden(p)) Hide(p, true);
            if (p.ready)
            {
                bool nearOn = p.dist < look.near;
                if (nearOn != p.nearOn)
                {
                    p.nearOn = nearOn;
                    ShowTier(p, true);
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
        while (jobs < MaxJobs && (nextBase != null || nextDetail != null || nextTrees != null))
        {
            // The full trees close by come before the ground far away, then the undergrowth
            if (nextTrees != null && (nextBase == null || nextTrees.dist <= nextBase.dist + 60f))
            {
                StartTrees(nextTrees);
                nextTrees = null;
            }
            else if (nextBase != null && (nextDetail == null || nextBase.dist <= nextDetail.dist + 40f))
            {
                // A far piece only when nothing else is being built, and not more often than every FarCalm seconds:
                // less garbage at once, so the memory clean-up does not stop the game
                if (!Urgent(nextBase))
                {
                    if (jobs > 0 || Time.unscaledTime - lastFarStart < FarCalm)
                    {
                        nextBase = null;
                        continue;
                    }
                    lastFarStart = Time.unscaledTime;
                }
                StartBase(nextBase);
                nextBase = NextUnbuilt();
            }
            else if (nextDetail != null)
            {
                StartDetail(nextDetail);
                nextDetail = null;
            }
            else break;
        }
    }

    // ---- The trees as copies (RunTrees) ----

    RunTrees trees;
    // The runner's eyes (RunSession sets it): pieces with far trees outside its picture are not drawn
    public Camera viewCamera;
    readonly Plane[] frustum = new Plane[6];

    // Out to here the far tree shapes; beyond, the pictures
    // (only when there are pictures: RunTrees.UsePictures)
    float FarTreesReach => look.near;

    static readonly int ThinId = Shader.PropertyToID("_TvThin"), ThinCenterId = Shader.PropertyToID("_TvLodCenter");

    void DrawTrees()
    {
        // The undergrowth thins out with distance (2026-11-26, Anders chose it for Ultra's lag): all of it up to 40 % of
        // its reach, a third of it from 85 % on, each tuft fading out on its share of the pixels (TvAtlasLit)
        Vector3 runnerAt = transform.TransformPoint(ToScene(playerE, playerN, 0f));
        Shader.SetGlobalVector(ThinCenterId, new Vector4(runnerAt.x, runnerAt.z, 0f, 0f));
        Shader.SetGlobalVector(ThinId, new Vector4(look.detail * 0.4f, look.detail * 0.85f, 0.33f, 1f));
        if (trees == null) return;
        trees.Clear();
        bool cull = viewCamera != null && viewCamera.isActiveAndEnabled;
        if (cull) GeometryUtility.CalculateFrustumPlanes(viewCamera, frustum);
        // Each tree is tested against the picture too (RunTrees), and close ones outside it give only their shadows
        trees.SetView(cull ? frustum : null, sun != null ? sun.transform.forward : Vector3.down);
        Matrix4x4 toWorld = transform.localToWorldMatrix;
        bool moved = !toWorld.isIdentity;
        Vector3 me = ToScene(playerE, playerN, 0f);
        foreach (Piece p in pieces.Values)
        {
            if (!p.ready || p.hidden || p.treeSet == null) continue;
            // As pictures: the trees only while the pictures fade in over them
            if (p.treeTier == 2 && !(p.cardFadeStart >= 0f && p.cardsComing)) continue;
            // The full trees close by are always drawn (their shadows fall into the picture from behind too)
            // (and pieces whose trees are all beyond the shadows: nothing of them reaches the picture from outside it)
            if (cull && (p.treeTier == 1 || p.dist > trees.shadowReach))
            {
                Vector3 c = transform.TransformPoint(ToScene(p.ce, p.cn, (p.minY + p.maxY) * 0.5f + 10f));
                var b = new Bounds(c, new Vector3(PieceSize + 16f, p.maxY - p.minY + 50f, PieceSize + 16f));
                if (!GeometryUtility.TestPlanesAABB(frustum, b)) continue;
            }
            // (a piece only just past the near distance looks at each tree: the runner may have come closer since)
            trees.Add(p.treeSet, p.treeTier == 1 && p.dist > look.near + 12f, me.x, me.z, toWorld, moved);
        }
        Vector3 at = transform.TransformPoint(ToScene(playerE, playerN, 0f));
        trees.Draw(new Bounds(at, new Vector3(look.view * 2f + 200f, 4000f, look.view * 2f + 200f)));
    }

    // How close the runner comes before a piece gets its full trees (a little before they are drawn)
    float TreesReach => look.near + 24f;

    // Which of a piece's meshes are drawn: the full ones close by, the far copies further out (and the far trees as long
    // as the full ones aren't made yet)
    // smooth: the change cross-fades (FadeTime) instead of popping
    void ShowTier(Piece p, bool smooth = false)
    {
        EndFade(p);
        bool nearOn = p.nearOn;
        foreach (Renderer r in p.near) if (r != null) Show(p, r, nearOn && !(p.leanHidden && p.lean.Contains(r)), smooth);
        foreach (Renderer r in p.far) if (r != null) Show(p, r, !nearOn || (!p.treesNear && p.farTrees.Contains(r)), smooth);
        if (smooth) StartFade(p, null);
    }

    void Show(Piece p, Renderer r, bool on, bool smooth)
    {
        if (!smooth) r.enabled = on;
        else if (on && !r.enabled) p.fadeIn.Add(r);
        else if (!on && r.enabled) p.fadeOut.Add(r);
    }

    // ---- Pieces behind hills (2026-11-26, Anders: "don't draw what can't be seen") ----
    // A piece far enough out is not drawn at all when the hill tops between it and the runner's eyes hide even the
    // tops of its tallest trees and houses at nine points over it. What is hidden in part (the foot of a slope behind
    // a knoll, the tree trunks with their tops showing) is still drawn: the graphics card leaves out its hidden
    // pixels itself (the depth test). A hidden piece is looked at again every frame, so it shows the moment its top
    // comes into sight over the crest.

    const float HideFrom = 64f;              // closer pieces are always drawn
    readonly List<Piece> hiddenPieces = new List<Piece>();
    readonly List<Renderer> hideList = new List<Renderer>();

    void Hide(Piece p, bool hide)
    {
        p.hidden = hide;
        // Only not drawn: switching the whole piece off and on (SetActive) took every renderer out of Unity's lists and
        // put it back, the likely cause of a 36 ms frame on Fossum (2026-11-26)
        if (p.root != null)
        {
            p.root.GetComponentsInChildren(true, hideList);
            foreach (Renderer r in hideList) r.forceRenderingOff = hide;
            hideList.Clear();
        }
        if (hide) { if (!hiddenPieces.Contains(p)) hiddenPieces.Add(p); }
        else hiddenPieces.Remove(p);
    }

    void Reveal()
    {
        for (int i = hiddenPieces.Count - 1; i >= 0; i--)
        {
            Piece p = hiddenPieces[i];
            if (p.gone || p.root == null || !Hidden(p)) Hide(p, false);
        }
    }

    bool Hidden(Piece p)
    {
        if (p.dist < HideFrom || p.world == null) return false;
        // The eyes, a little high (the head bobs, the runner jumps)
        float eye = GroundY(playerE, playerN) + 2.8f;
        float top = p.maxY + (data.town != null ? 45f : 32f);
        for (int a = -1; a <= 1; a++)
        {
            for (int b = -1; b <= 1; b++)
            {
                double te = p.ce + a * (Half - 1.0), tn = p.cn + b * (Half - 1.0);
                if (!Blocked(eye, te, tn, top)) return false;
            }
        }
        return true;
    }

    // Whether the ground rises above the line from the eyes to (te, tn) at height top somewhere between them
    bool Blocked(float eye, double te, double tn, float top)
    {
        double de = te - playerE, dn = tn - playerN;
        double len = Math.Sqrt(de * de + dn * dn);
        if (len < 20.0) return false;
        int steps = Mathf.Clamp((int)(len / 6.0), 4, 60);
        for (int i = 1; i < steps; i++)
        {
            double f = (double)i / steps;
            float ground = GroundY(playerE + de * f, playerN + dn * f) - 0.5f;
            if (ground > eye + (top - eye) * (float)f) return true;
        }
        return false;
    }

    // ---- Cross-fades between the levels of detail (2026-11-26, Anders: no sharp changes, no houses popping up) ----
    // For FadeTime both levels are drawn, each on its own share of the pixels (the _Fade dither in TvAtlasLit and
    // TvVertexLit), the coming one more and more; then the going one is switched off. Games call it a dithered
    // LOD cross-fade.

    const float FadeTime = 0.8f;
    readonly List<Piece> fading = new List<Piece>();
    MaterialPropertyBlock fadeBlock;

    void StartFade(Piece p, Action done)
    {
        if (p.fadeIn.Count == 0 && p.fadeOut.Count == 0)
        {
            done?.Invoke();
            return;
        }
        p.fadeStart = Time.unscaledTime;
        p.fadeDone = done;
        foreach (Renderer r in p.fadeIn) if (r != null) r.enabled = true;
        SetFade(p, 0f);
        if (!fading.Contains(p)) fading.Add(p);
    }

    void SetFade(Piece p, float t)
    {
        if (fadeBlock == null) fadeBlock = new MaterialPropertyBlock();
        fadeBlock.SetFloat(FadeId, t);
        foreach (Renderer r in p.fadeIn) if (r != null) r.SetPropertyBlock(fadeBlock);
        fadeBlock.SetFloat(FadeId, -1f - t);
        foreach (Renderer r in p.fadeOut) if (r != null) r.SetPropertyBlock(fadeBlock);
    }

    static readonly int FadeId = Shader.PropertyToID("_Fade");

    // Finishes a piece's cross-fade at once: what came stays, what went is switched off
    void EndFade(Piece p)
    {
        if (p.fadeStart < 0f) return;
        foreach (Renderer r in p.fadeIn) if (r != null) r.SetPropertyBlock(null);
        foreach (Renderer r in p.fadeOut)
        {
            if (r == null) continue;
            r.SetPropertyBlock(null);
            r.enabled = false;
        }
        p.fadeIn.Clear();
        p.fadeOut.Clear();
        p.fadeStart = -1f;
        fading.Remove(p);
        Action done = p.fadeDone;
        p.fadeDone = null;
        done?.Invoke();
    }

    void CardFade(Piece p, bool coming)
    {
        p.cardsComing = coming;
        p.cardFadeStart = Time.unscaledTime;
        p.cardRenderer.enabled = true;
        SetCardFade(p, 0f);
        if (!cardFading.Contains(p)) cardFading.Add(p);
    }

    readonly List<Piece> cardFading = new List<Piece>();

    void SetCardFade(Piece p, float t)
    {
        if (p.cardRenderer == null) return;
        if (fadeBlock == null) fadeBlock = new MaterialPropertyBlock();
        fadeBlock.SetFloat(FadeId, p.cardsComing ? t : -1f - t);
        p.cardRenderer.SetPropertyBlock(fadeBlock);
    }

    void Fades()
    {
        for (int i = cardFading.Count - 1; i >= 0; i--)
        {
            Piece p = cardFading[i];
            float t = (Time.unscaledTime - p.cardFadeStart) / FadeTime;
            if (p.cardRenderer == null || p.gone || t >= 1f)
            {
                if (p.cardRenderer != null)
                {
                    p.cardRenderer.SetPropertyBlock(null);
                    p.cardRenderer.enabled = p.cardsComing;
                }
                p.cardFadeStart = -1f;
                cardFading.RemoveAt(i);
            }
            else SetCardFade(p, t);
        }
        for (int i = fading.Count - 1; i >= 0; i--)
        {
            Piece p = fading[i];
            float t = (Time.unscaledTime - p.fadeStart) / FadeTime;
            if (t >= 1f) EndFade(p);
            else SetFade(p, t);
        }
    }

    Piece NextUnbuilt()
    {
        Piece best = null;
        foreach (Piece p in pieces.Values)
            if (!p.ready && !p.building && (best == null || Want(p) < Want(best))) best = p;
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
            // The houses in full detail only on the High and Ultra 3D qualities; the others draw the leaner ones close by too
            townRich = quality >= (int)RunSettings.Quality.High,
            quality = quality
        };
    }

    void StartBase(Piece p)
    {
        p.building = true;
        TvWorldInput input = Input(p);
        input.skipNearTrees = p.dist > TreesReach;
        input.treeRecords = trees != null;
        RunTrees copies = trees;
        Vector3 offset = ToScene(p.ce, p.cn, 0f);
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
                if (copies != null) done.treeSet = copies.Prepare(done.world.treeRecords, offset);
                done.arrays = Parts(done.world.meshes, false);
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
        Piece owner = p.parent ?? p;
        input.baseSolids = owner.world.solids;
        int radius = Half;
        if (p.parent != null)
        {
            // A square: its own centre and size, and the piece's stones and trunks moved to that centre (the ones near it)
            radius = QuadHalf;
            input.radius = QuadHalf;
            float sx = (float)(owner.ce - p.ce), sz = (float)(owner.cn - p.cn);
            var near = new List<TvSolid>();
            foreach (TvSolid t in owner.world.solids)
            {
                float x = t.x + sx, z = t.z + sz;
                if (Math.Abs(x) > QuadHalf + t.r + 2f || Math.Abs(z) > QuadHalf + t.r + 2f) continue;
                near.Add(new TvSolid { x = x, z = z, r = t.r });
            }
            input.baseSolids = near;
        }
        input.undergrowthShare = look.undergrowth;
        jobs++;
        Task.Run(() =>
        {
            var done = new Done { piece = p, detail = true };
            try
            {
                TvWorld w = TvWorldBuilder.Build(input);
                done.arrays = Parts(w.meshes, true, radius);
            }
            catch (Exception e)
            {
                Debug.LogError("RunWorld: could not build the undergrowth at " + p.ce.ToString("0") + ", " + p.cn.ToString("0") + ": " + e);
            }
            finished.Enqueue(done);
        });
    }

    // The full trees of a piece that was built far away: the piece is built again (the same trees in the same places)
    // and only its full trees are kept
    void StartTrees(Piece p)
    {
        p.treesBuilding = true;
        TvWorldInput input = Input(p);
        input.flags = new List<float>();
        for (int i = 0; i < flagPoints.Count; i++)
        {
            double x = flagPoints[i].e - p.ce, z = flagPoints[i].n - p.cn;
            if (x < -Half || x >= Half || z < -Half || z >= Half) continue;
            input.flags.Add((float)x);
            input.flags.Add((float)z);
        }
        jobs++;
        Task.Run(() =>
        {
            var done = new Done { piece = p, trees = true };
            try
            {
                TvWorld w = TvWorldBuilder.Build(input);
                w.meshes.RemoveAll(m => !IsFullTrees(m.name, m.group));
                done.arrays = Parts(w.meshes, false);
            }
            catch (Exception e)
            {
                Debug.LogError("RunWorld: could not build the trees at " + p.ce.ToString("0") + ", " + p.cn.ToString("0") + ": " + e);
            }
            finished.Enqueue(done);
        });
    }

    // A piece's meshes as the graphics card gets them, in parts of at most MaxCorners corners so none stops the game
    // when it is handed over (2026-11-26, Anders on Fossum Ultra: a piece's undergrowth of 1-1.5 million corners cost
    // 100-200 ms). The undergrowth, thickets and litter are also cut into 16 m squares, so the graphics card skips the
    // squares behind the runner and beside the picture.
    const int MaxCorners = 48000;

    static List<TvWorldView.MeshArrays> Parts(List<TvMesh> meshes, bool detail, int radius = Half)
    {
        var list = new List<TvWorldView.MeshArrays>(meshes.Count);
        foreach (TvMesh m in meshes)
        {
            bool cut = detail && m.name != "Town houses full";
            // the undergrowth in 16 m squares, whatever size the build
            list.AddRange(TvWorldView.MeshArrays.Split(m, radius, cut ? Math.Max(1, radius / 8) : 1, MaxCorners));
            m.Recycle();
        }
        return list;
    }

    static bool IsFullTrees(string name, int group) => group == TvMesh.Near && name == "Trees";

    void MakeTrees(Piece p, List<TvWorldView.MeshArrays> arrays)
    {
        if (p.root == null || p.treesNear || p.dist > TreesReach + 60f) return;
        foreach (TvWorldView.MeshArrays m in arrays)
        {
            Renderer r = AddMesh(p.root.transform, m, p.treeMeshes);
            p.near.Add(r);
            p.treeRenderers.Add(r);
        }
        p.treesNear = true;
        p.treesOwn = true;
        ShowTier(p, true);
        if (Loading) EndFade(p);
    }

    void DropTrees(Piece p)
    {
        foreach (Renderer r in p.treeRenderers)
        {
            p.near.Remove(r);
            if (r != null) Destroy(r.gameObject);
        }
        p.treeRenderers.Clear();
        foreach (Mesh m in p.treeMeshes) if (m != null) Destroy(m);
        p.treeMeshes.Clear();
        p.treesNear = false;
        p.treesOwn = false;
        ShowTier(p, true);
    }

    // Meshes go to the graphics card a few at a time: about 4 ms a frame while running, more while loading
    void HandOver()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int budget = Loading ? 40 : 4;
        while (uploading.Count > 0 && watch.ElapsedMilliseconds < budget)
        {
            // One mesh at a time (2026-11-26, Anders on Fossum: one piece's meshes all at once cost 60-170 ms): a
            // piece is only put together once all its meshes are made, and the time is checked after each
            Done next = uploading.Peek();
            Piece np = next.piece;
            bool wanted = Current(np);
            if (wanted && next.arrays != null && next.made < next.arrays.Count)
            {
                TvWorldView.MeshArrays a = next.arrays[next.made++];
                long m0 = perf.ElapsedTicks;
                a.mesh = TvWorldView.ToMesh(a);
                a.Release();
                a.mesh.UploadMeshData(true);
                NoteMesh(a, perf.ElapsedTicks - m0);
                continue;
            }
            Done done = uploading.Dequeue();
            jobs--;
            Piece p = done.piece;
            if (done.detail) p.detailBuilding = false;
            else if (done.trees) p.treesBuilding = false;
            else p.building = false;
            bool kept = Current(p);
            if (!kept || done.arrays == null)
            {
                // Left behind while it was being built, or it failed: built again if it is needed after all
                if (done.arrays != null) foreach (TvWorldView.MeshArrays a in done.arrays) { a.Release(); if (a.mesh != null) { Destroy(a.mesh); a.mesh = null; } }
                continue;
            }
            if (done.detail) MakeDetail(p, done.arrays);
            else if (done.trees) MakeTrees(p, done.arrays);
            else MakeBase(p, done);
            // Meshes made ahead that were not used after all (the runner moved on meanwhile)
            foreach (TvWorldView.MeshArrays a in done.arrays) { a.Release(); if (a.mesh != null) { Destroy(a.mesh); a.mesh = null; } }
        }
    }

    GameObject Root(Piece p, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = ToScene(p.ce, p.cn, 0f);
        return go;
    }

    // The slowest single mesh since the last RunPerf line: which, how big, how long
    string slowMesh;
    double slowMeshMs;

    void NoteMesh(TvWorldView.MeshArrays a, long ticks)
    {
        double ms = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (ms <= slowMeshMs) return;
        slowMeshMs = ms;
        slowMesh = a.name + " " + a.count.ToString("N0") + " corners " + ms.ToString("0.0") + " ms";
    }

    Renderer AddMesh(Transform parent, TvWorldView.MeshArrays source, List<Mesh> keep)
    {
        Mesh mesh = source.mesh;
        source.mesh = null;
        if (mesh == null)
        {
            long m0 = perf.ElapsedTicks;
            mesh = TvWorldView.ToMesh(source);
            source.Release();
            mesh.UploadMeshData(true);
            NoteMesh(source, perf.ElapsedTicks - m0);
        }
        keep.Add(mesh);
        var go = new GameObject(source.name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = source.HasUvs ? atlasMaterial
            : source.name == "Ground paved" && pavedMaterial != null ? pavedMaterial
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
            if (m.group == TvMesh.Near) p.near.Add(r);
            if (IsFullTrees(m.name, m.group)) p.treesNear = true;
            if (m.name == "Town houses") p.lean.Add(r);
            else if (m.group == TvMesh.Far)
            {
                p.far.Add(r);
                if (m.name == "Trees far") p.farTrees.Add(r);
            }
        }
        ShowTier(p);
        // The trees as copies (RunTrees): nothing more to make for them; the picture cards for far away
        float lo = float.MaxValue, hi = float.MinValue;
        if (p.world.ground != null) foreach (float g in p.world.ground) { if (g < lo) lo = g; if (g > hi) hi = g; }
        p.minY = lo <= hi ? lo : 0f;
        p.maxY = lo <= hi ? hi : 0f;
        if (trees != null && done.treeSet != null)
        {
            p.treeSet = done.treeSet;
            p.treesNear = true;
            p.cardMesh = trees.CardMesh(done.treeSet, PieceSize);
            if (p.cardMesh != null)
            {
                var go = new GameObject("Tree pictures");
                go.transform.SetParent(p.root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = p.cardMesh;
                var cr = go.AddComponent<MeshRenderer>();
                cr.sharedMaterial = trees.impostorMaterial;
                cr.shadowCastingMode = ShadowCastingMode.Off;
                cr.receiveShadows = false;
                cr.lightProbeUsage = LightProbeUsage.Off;
                cr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                cr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                p.cardRenderer = cr;
            }
            p.treeTier = p.dist < look.near ? 0 : p.dist < FarTreesReach || p.cardRenderer == null ? 1 : 2;
            if (p.cardRenderer != null) p.cardRenderer.enabled = p.treeTier == 2;
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
        Piece owner = p.parent ?? p;
        if (owner.root == null) return;
        if (p.dist > look.detail + 16f) return;   // the runner has moved on meanwhile
        EndFade(p);
        p.detail = new GameObject("Detail");
        p.detail.transform.SetParent(owner.root.transform, false);
        if (p.parent != null) p.detail.transform.localPosition = new Vector3((float)(p.ce - owner.ce), 0f, (float)(p.cn - owner.cn));
        foreach (TvWorldView.MeshArrays m in arrays)
        {
            Renderer added = AddMesh(p.detail.transform, m, p.detailMeshes);
            added.enabled = false;
            p.fadeIn.Add(added);
            // The full houses stand where the leaner ones were
            if (m.name == "Town houses full")
            {
                p.leanHidden = true;
                foreach (Renderer r in p.lean) if (r != null && r.enabled) p.fadeOut.Add(r);
            }
        }
        // The undergrowth and the full houses fade in, the leaner houses out (at once while loading: nobody sees it)
        StartFade(p, null);
        if (Loading) EndFade(p);
    }

    // smooth: the detail fades out (and the leaner houses in) before it is thrown away
    void DropDetail(Piece p, bool smooth = false)
    {
        EndFade(p);
        GameObject old = p.detail;
        var oldMeshes = new List<Mesh>(p.detailMeshes);
        p.detailMeshes.Clear();
        p.detail = null;
        Action throwAway = () =>
        {
            foreach (Mesh m in oldMeshes) if (m != null) Destroy(m);
            if (old != null) Destroy(old);
        };
        bool leanBack = p.leanHidden;
        p.leanHidden = false;
        if (!smooth || old == null)
        {
            if (leanBack) foreach (Renderer r in p.lean) if (r != null) r.enabled = p.nearOn;
            throwAway();
            return;
        }
        foreach (Renderer r in old.GetComponentsInChildren<Renderer>()) if (r.enabled) p.fadeOut.Add(r);
        if (leanBack && p.nearOn) foreach (Renderer r in p.lean) if (r != null && !r.enabled) p.fadeIn.Add(r);
        StartFade(p, throwAway);
    }

    void Drop(Piece p)
    {
        p.gone = true;
        EndFade(p);
        hiddenPieces.Remove(p);
        p.hidden = false;
        if (p.peopleOn && people != null) people.Despawn(Key(p.ix, p.iz));
        p.peopleOn = false;
        DropDetail(p);
        if (p.quads != null)
            foreach (Piece q in p.quads)
            {
                q.gone = true;
                EndFade(q);
                DropDetail(q);
            }
        p.quads = null;
        foreach (Mesh m in p.treeMeshes) if (m != null) Destroy(m);
        p.treeMeshes.Clear();
        p.treeRenderers.Clear();
        p.treesNear = p.treesOwn = false;
        p.farTrees.Clear();
        if (p.cardMesh != null) Destroy(p.cardMesh);
        p.cardMesh = null;
        p.cardRenderer = null;
        p.treeSet = null;
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
