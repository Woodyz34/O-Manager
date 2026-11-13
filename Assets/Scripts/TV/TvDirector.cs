using System.Collections.Generic;
using UnityEngine;

// The TV of a relay with TV controls (CourseData.tvControls): films every runner that passes a TV control and shows the
// footage in slow motion (TvFootage) in the 3D view of the control (TvWorldView), through TvViewer.
// Only one 3D world is kept at a time; it is built for the TV control that has runners to show. Lives on the
// RelayRace's object, so it goes when the race does.
public class TvDirector : MonoBehaviour
{
    public static TvDirector Active { get; private set; }

    MapDefinition map;
    public readonly List<TvFootage> tvs = new List<TvFootage>();
    int shown;
    bool chosen;                // the player picked the TV with Next TV
    TvWorldView view;
    int builtFor = -1;
    bool building, failed;
    float quietSince;
    float lastOnAir = -99f;
    readonly List<TvWorldView.RunnerAt> runners = new List<TvWorldView.RunnerAt>();
    readonly Dictionary<IPackRunner, Color> colors = new Dictionary<IPackRunner, Color>();

    public int Shown => shown;
    public TvWorldView View => view != null && builtFor == shown ? view : null;
    public bool OnAir { get; private set; }
    public Vector3? Subject { get; private set; }
    public bool ShowTile => View != null && (OnAir || Time.time - lastOnAir < 2.5f);
    public string Title => tvs.Count == 0 ? "TV" : tvs[shown].name;

    static readonly Color[] Palette =
    {
        new Color32(0xd4, 0x35, 0x2b, 255), new Color32(0x2a, 0x6f, 0xd6, 255), new Color32(0xe8, 0xc2, 0x1f, 255),
        new Color32(0xee, 0xee, 0xea, 255), new Color32(0x3f, 0x9d, 0x54, 255), new Color32(0x9b, 0x4d, 0xca, 255)
    };

    // Starts the TV for a relay, or returns null when the course has no TV controls or the map no 3D layer
    public static TvDirector Create(GameObject host, CourseData course, MapDefinition map)
    {
        if (course == null || course.tvControls == null || course.tvControls.Count == 0 || map == null) return null;
        if (course.IsSprint() || !TvWorldData.HasWorld(map))
        {
            Debug.Log("TvDirector: the course has TV controls, but " + (course.IsSprint() ? "it is a sprint" : "the map has no 3D layer") + "; no TV");
            return null;
        }
        TvDirector director = host.AddComponent<TvDirector>();
        director.map = map;
        List<CoursePoint> common = CommonControls(course);
        for (int i = 0; i < course.tvControls.Count; i++)
        {
            CoursePoint point = course.tvControls[i];
            int number = common.FindIndex(p => System.Math.Abs(p.e - point.e) < 1.5 && System.Math.Abs(p.n - point.n) < 1.5);
            string where = number < 0 ? "" : number == common.Count - 1 ? " · Finish" : " · Control " + (number + 1);
            director.tvs.Add(new TvFootage(point, "TV " + (i + 1) + where));
        }
        Active = director;
        TvViewer.ForRace(director);
        Debug.Log("TvDirector: " + director.tvs.Count + " TV controls");
        return director;
    }

    // The controls of the first course with its finish last, to name the TV controls
    static List<CoursePoint> CommonControls(CourseData course)
    {
        var list = new List<CoursePoint>();
        if (course.relayCourses == null || course.relayCourses.Count == 0) return list;
        RelayLegCourse first = course.relayCourses[0];
        foreach (RelayItem item in first.items) if (item.fork < 0) list.Add(item.point);
        list.Add(first.finish);
        return list;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        if (view != null) Destroy(view.gameObject);
    }

    public void Choose(int index)
    {
        if (index < 0 || index >= tvs.Count) return;
        shown = index;
        chosen = true;
    }

    public void ReleaseChoice()
    {
        chosen = false;
    }

    Color ColorOf(IPackRunner runner)
    {
        if (colors.TryGetValue(runner, out Color color)) return color;
        color = Palette[colors.Count % Palette.Length];
        if (runner is Component component && component != null)
        {
            BodyHandler body = component.GetComponent<BodyHandler>();
            if (body != null && body.cirkel != null)
            {
                color = body.cirkel.sharedMaterial != null ? body.cirkel.sharedMaterial.color : body.cirkel.color;
                color.a = 1f;
            }
        }
        colors[runner] = color;
        return color;
    }

    void Update()
    {
        if (failed || tvs.Count == 0) return;
        float now = Time.time;

        // Film everyone who is out running
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (runner == null || !runner.IsValidRunner || !runner.RelayRunning) continue;
            CoursePoint p = map.FromWorld(runner.RunnerPosition);
            foreach (TvFootage tv in tvs) tv.Record(runner, runner.RunnerLabel, ColorOf(runner), p.e, p.n, now);
        }
        foreach (TvFootage tv in tvs) tv.CloseStale(now);

        // Which TV to show: stay while it has something to show, then go to the one whose runners come first
        if (!chosen && tvs.Count > 1)
        {
            bool quiet = !OnAir && !tvs[shown].HasPending();
            if (!quiet) quietSince = now;
            else if (now - quietSince > 3f)
            {
                int best = shown;
                float first = float.MaxValue;
                for (int i = 0; i < tvs.Count; i++)
                {
                    if (!tvs[i].HasPending()) continue;
                    // Footage this old is skipped anyway (TvFootage.Advance), so it counts from there
                    float start = Mathf.Max(tvs[i].NextStart(), now - 60f);
                    if (start < first)
                    {
                        first = start;
                        best = i;
                    }
                }
                shown = best;
            }
        }

        if (builtFor != shown && !building) Build(shown);

        TvWorldView current = View;
        if (current == null)
        {
            OnAir = false;
            Subject = null;
            return;
        }
        OnAir = tvs[shown].Advance(Time.deltaTime, now, runners);
        if (OnAir) lastOnAir = now;
        Subject = current.ShowRunners(runners);
    }

    async void Build(int index)
    {
        building = true;
        if (view != null) Destroy(view.gameObject);
        view = null;
        builtFor = -1;
        TvWorldView made = null;
        try
        {
            made = await TvWorldView.Create(map, tvs[index].control);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("TvDirector: could not build the TV control's world: " + e);
        }
        building = false;
        if (this == null)
        {
            if (made != null) Destroy(made.gameObject);
            return;
        }
        if (made == null)
        {
            failed = true;
            return;
        }
        view = made;
        builtFor = index;
    }
}
