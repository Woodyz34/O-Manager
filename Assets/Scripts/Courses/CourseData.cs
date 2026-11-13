using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// A point on a course in the map's real-world coordinates (SWEREF 99 TM, metres).
// Stored this way, rather than as grid cells or Unity positions, so a course still fits if the grid is rebuilt.
[Serializable]
public struct CoursePoint
{
    // Doubles, because a float can only hold a northing like 6 429 169 m to about half a metre
    public double e; // easting
    public double n; // northing

    public CoursePoint(double e, double n)
    {
        this.e = e;
        this.n = n;
    }
}

// One orienteering course: a start, the controls in order and a finish, laid on one of the maps.
[Serializable]
public class CourseData
{
    public const int MaxControls = 50;      // controls (and variants) on one course
    public const int MaxNameLength = 32;
    public const int MaxAuthorLength = 20;

    public string id;
    public string name;
    public string author;
    public string authorId;          // Unity player id of the author, empty for the courses shipped with the game
    public string mapId;
    public string background;        // which picture of the map to show; empty = the map's own clean picture
    public bool builtIn;
    public long createdAt;           // unix seconds
    public string terrain;           // "forest" or "sprint", chosen when the course is saved; empty = the map's type
    public CoursePoint start;
    public List<CoursePoint> controls = new List<CoursePoint>();
    public CoursePoint finish;
    // Out-of-bounds areas (purple stripes) the runners must not enter; they count for every leg of a relay
    public List<ForbiddenArea> forbidden = new List<ForbiddenArea>();
    // Relays on forest maps: the controls (and the finish) with a TV camera. During the race a small 3D view of the
    // control shows the runners passing it (TV/TvDirector). Only common controls, the ones every team runs.
    public const int MaxTvControls = 6;
    public List<CoursePoint> tvControls = new List<CoursePoint>();

    public bool HasTvControl(CoursePoint point)
    {
        if (tvControls == null) return false;
        foreach (CoursePoint tv in tvControls) if (Math.Abs(tv.e - point.e) < 1.5 && Math.Abs(tv.n - point.n) < 1.5) return true;
        return false;
    }

    // ---- Relay ----
    // A relay is run by teams: every team member runs one leg, and every leg has its own course (relayCourses; legs can
    // also share one). A course is a start, a row of items and a finish. An item is a control, or a fork: a stretch
    // between the common control (or start) before it and the common control (or finish) after it that can be run along
    // different branches. A branch has any number of controls, also none (straight to the common control after).
    // A fork can be on several legs. Then every team gets one branch of it per leg, and with forkEveryVariant (the usual
    // "Farsta" forking) every team runs every branch once over those legs, so all teams run the same course in the end.
    public const string KindRelay = "relay";
    public const int MinLegs = 2;
    public const int MaxLegs = 10;
    public const int MaxLobbyChars = 2040;                      // NetworkPlayer has four lobby strings of 512

    public string kind;                                         // empty = individual course, "relay" = relay
    public int legs;                                            // relay: how many legs (runners per team)
    public List<int> legCourse = new List<int>();               // relay: the course each leg runs (index into relayCourses)
    public List<RelayLegCourse> relayCourses = new List<RelayLegCourse>();
    public List<RelayFork> relayForks = new List<RelayFork>();
    public bool forkEveryVariant = true;                        // true: every team runs every branch of a fork once; false: random

    // Only in relays saved by earlier versions of the builder; Normalize turns them into the courses and forks above
    public List<ControlFork> forks = new List<ControlFork>();
    public List<RelayCourse> extraCourses = new List<RelayCourse>();
    public List<bool> forkedLegs = new List<bool>();

    public bool IsRelay() => kind == KindRelay;

    public const string Forest = "forest";
    public const string Sprint = "sprint";

    // A sprint course: no mistakes, the runners run the route as it is drawn
    public bool IsSprint()
    {
        if (!string.IsNullOrEmpty(terrain)) return terrain == Sprint;
        MapDefinition map = MapDefinition.FindExact(mapId);
        return map != null && map.IsSprint;
    }

    public static string BranchLetter(int branch) => ((char)('A' + branch)).ToString();

    public int LegCourseIndex(int leg)
    {
        if (relayCourses.Count == 0) return 0;
        if (leg < 0 || leg >= legCourse.Count) return 0;
        return Mathf.Clamp(legCourse[leg], 0, relayCourses.Count - 1);
    }

    public RelayLegCourse LegCourse(int leg) => relayCourses.Count > 0 ? relayCourses[LegCourseIndex(leg)] : null;

    public RelayFork Fork(int index) => index >= 0 && index < relayForks.Count ? relayForks[index] : null;

    // The legs whose course has this fork (a course shared by two legs counts twice)
    public List<int> LegsUsingFork(int forkIndex)
    {
        List<int> result = new List<int>();
        for (int leg = 0; leg < legs; leg++)
        {
            RelayLegCourse course = LegCourse(leg);
            if (course != null && course.items.Exists(item => item.fork == forkIndex)) result.Add(leg);
        }
        return result;
    }

    // A forked leg: not every team runs the same controls on it
    public bool LegForked(int leg)
    {
        RelayLegCourse course = IsRelay() ? LegCourse(leg) : null;
        if (course == null) return false;
        foreach (RelayItem item in course.items)
        {
            RelayFork fork = Fork(item.fork);
            if (fork != null && fork.branches.Count > 1) return true;
        }
        return false;
    }

    public bool AnyForks()
    {
        for (int leg = 0; leg < legs; leg++)
        {
            if (LegForked(leg)) return true;
        }
        return false;
    }

    // Which branch of every fork each leg of one team runs: [leg][fork index], -1 where the leg doesn't have the fork.
    // With forkEveryVariant every branch of a fork is run on one of the legs that have it (the builder makes sure a fork
    // has no more branches than legs); the rest are random.
    public int[][] AssignBranches(System.Random random)
    {
        int legCount = Math.Max(1, legs);
        int[][] result = new int[legCount][];
        for (int leg = 0; leg < legCount; leg++)
        {
            result[leg] = new int[relayForks.Count];
            for (int f = 0; f < relayForks.Count; f++) result[leg][f] = -1;
        }
        for (int f = 0; f < relayForks.Count; f++)
        {
            int count = Math.Max(1, relayForks[f].branches.Count);
            List<int> forkLegs = LegsUsingFork(f);
            List<int> order = new List<int>();
            if (forkEveryVariant)
            {
                for (int b = 0; b < count && order.Count < forkLegs.Count; b++) order.Add(b);
            }
            while (order.Count < forkLegs.Count) order.Add(random.Next(count));
            for (int k = order.Count - 1; k > 0; k--)
            {
                int swap = random.Next(k + 1);
                (order[k], order[swap]) = (order[swap], order[k]);
            }
            for (int k = 0; k < forkLegs.Count; k++) result[forkLegs[k]][f] = order[k];
        }
        return result;
    }

    // The way one team runs a leg: its start, controls (with its branch of every fork) and finish, and which of those
    // points are common to every team (the start, the controls outside the forks and the finish)
    public LegRoute Route(int leg, int[] branchOfFork)
    {
        LegRoute route = new LegRoute();
        if (!IsRelay())
        {
            // An individual course: the start, every control and the finish are run by everyone
            route.course = this;
            for (int i = 0; i < controls.Count + 2; i++)
            {
                route.commonOrdinal.Add(i);
                route.segmentBranch.Add(-1);
            }
            return route;
        }
        RelayLegCourse relayCourse = LegCourse(leg);
        CourseData result = new CourseData
        {
            id = id, name = name, author = author, authorId = authorId, mapId = mapId, background = background,
            builtIn = builtIn, createdAt = createdAt, terrain = terrain, forbidden = forbidden
        };
        route.course = result;
        if (relayCourse == null) return route;
        result.start = relayCourse.start;
        result.finish = relayCourse.finish;
        route.commonOrdinal.Add(0);
        route.segmentBranch.Add(-1);
        int pendingBranch = -1;
        foreach (RelayItem item in relayCourse.items)
        {
            if (item.fork < 0)
            {
                result.controls.Add(item.point);
                route.commonOrdinal.Add(route.segmentBranch.Count);
                route.segmentBranch.Add(pendingBranch);
                pendingBranch = -1;
                continue;
            }
            RelayFork fork = Fork(item.fork);
            if (fork == null || fork.branches.Count == 0) continue;
            int branch = branchOfFork != null && item.fork < branchOfFork.Length && branchOfFork[item.fork] >= 0 ? branchOfFork[item.fork] : 0;
            branch = Mathf.Clamp(branch, 0, fork.branches.Count - 1);
            foreach (CoursePoint point in fork.branches[branch].controls)
            {
                result.controls.Add(point);
                route.commonOrdinal.Add(-1);
            }
            pendingBranch = fork.branches.Count > 1 ? branch : -1;
        }
        route.commonOrdinal.Add(route.segmentBranch.Count);
        route.segmentBranch.Add(pendingBranch);
        return route;
    }

    // Turns relays from earlier versions into courses and forks, and keeps the course's own start, controls and finish
    // as the first leg's course (branch A everywhere), which is what thumbnails and the lobby show
    public void Normalize()
    {
        if (!IsRelay()) return;
        legs = Mathf.Clamp(legs, MinLegs, MaxLegs);
        if (relayCourses.Count == 0)
        {
            ConvertOldRelay();
        }
        while (legCourse.Count < legs) legCourse.Add(Mathf.Max(0, legCourse.Count > 0 ? legCourse[legCourse.Count - 1] : 0));
        if (legCourse.Count > legs) legCourse.RemoveRange(legs, legCourse.Count - legs);
        for (int leg = 0; leg < legs; leg++) legCourse[leg] = Mathf.Clamp(legCourse[leg], 0, Mathf.Max(0, relayCourses.Count - 1));
        forks.Clear();
        extraCourses.Clear();
        forkedLegs.Clear();
        RefreshMainFields();
    }

    public void RefreshMainFields()
    {
        if (!IsRelay() || relayCourses.Count == 0) return;
        CourseData first = Route(0, null).course;
        start = first.start;
        controls = first.controls;
        finish = first.finish;
    }

    // Before courses per leg and forks with branches, a relay had one course (the main fields, with single-control
    // variants in forks), extra courses, and either legCourse or a forked/straight flag per leg
    void ConvertOldRelay()
    {
        List<RelayCourse> oldCourses = new List<RelayCourse> { new RelayCourse { start = start, controls = controls, finish = finish, forks = forks } };
        oldCourses.AddRange(extraCourses);
        if (legCourse.Count == 0)
        {
            bool someForked = false, someStraight = false;
            for (int leg = 0; leg < legs; leg++)
            {
                if (leg < forkedLegs.Count && forkedLegs[leg]) someForked = true;
                else someStraight = true;
            }
            if (oldCourses[0].VariantPointCount() > 0 && someForked && someStraight)
            {
                oldCourses.Add(new RelayCourse { start = start, controls = new List<CoursePoint>(controls), finish = finish });
                for (int leg = 0; leg < legs; leg++) legCourse.Add(leg < forkedLegs.Count && forkedLegs[leg] ? 0 : oldCourses.Count - 1);
            }
            else
            {
                for (int leg = 0; leg < legs; leg++) legCourse.Add(0);
                if (!someForked) oldCourses[0].forks = new List<ControlFork>();
            }
        }
        foreach (RelayCourse old in oldCourses)
        {
            RelayLegCourse course = new RelayLegCourse { start = old.start, finish = old.finish };
            for (int c = 0; c < old.controls.Count; c++)
            {
                if (old.VariantCount(c) < 2)
                {
                    course.items.Add(new RelayItem { fork = -1, point = old.controls[c] });
                    continue;
                }
                RelayFork fork = new RelayFork();
                for (int v = 0; v < old.VariantCount(c); v++)
                {
                    fork.branches.Add(new RelayBranch { controls = new List<CoursePoint> { old.ControlVariant(c, v) } });
                }
                relayForks.Add(fork);
                course.items.Add(new RelayItem { fork = relayForks.Count - 1 });
            }
            relayCourses.Add(course);
        }
    }

    // Start, every control and the finish, in running order. This is what the game runs through.
    public List<CoursePoint> AllPoints()
    {
        List<CoursePoint> points = new List<CoursePoint>(controls.Count + 2) { start };
        points.AddRange(controls);
        points.Add(finish);
        return points;
    }

    // Straight-line length from start to finish in metres (for a relay: the first leg, branch A of every fork)
    public float LengthMeters()
    {
        List<CoursePoint> points = AllPoints();
        float total = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            double de = points[i].e - points[i - 1].e;
            double dn = points[i].n - points[i - 1].n;
            total += (float)Math.Sqrt(de * de + dn * dn);
        }
        return total;
    }

    public string LengthText()
    {
        return KmText(LengthMeters());
    }

    public static string KmText(float meters)
    {
        return (meters / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
    }

    public static string NewId()
    {
        return Guid.NewGuid().ToString("N").Substring(0, 12);
    }

    // ---- Compact text form, small enough to send to the other players in the lobby ----
    // v2|id|mapId|background|builtIn|name|author|authorId|points
    // The points are whole metres written with 64 symbols per digit: the start as two 4-digit numbers, then every
    // later point as the step from the point before as two 3-digit numbers (up to 131 km either way). 50 controls
    // take about 300 characters.
    // Relays use v5: the points of every course one after the other (start, the controls outside forks, finish), then the
    // controls of every fork's branches, and one more field:  legs.legCourses.mode.courses.forks
    // e.g. "4.AABB.e.A*B**~*A*.CBA~CAAA" = 4 legs, legs 1-2 run course 0 and legs 3-4 course 1, every team runs every
    // branch; course 0 is fork 0, a control, fork 1, two controls; course 1 is a control, fork 0, a control; fork 0 has
    // 2 branches (C) with 1 (B) and 0 (A) controls, fork 1 has 2 branches (C) with 0 and 0... (each fork: number of
    // branches, then the number of controls on each branch).
    // (v3 and v4 were earlier relay formats; they are still read.)
    // Last comes the course type when it is set: "s" sprint, "f" forest (older versions of the game ignore it).
    // Out-of-bounds areas come after that as a field starting with "z": for every area the number of corners (one
    // digit), then its corners, written like the points above (the very first corner in full, then steps).

    const string Digits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    const int StepOffset = 131072; // 64^3 / 2

    public string ToCompact()
    {
        bool relay = IsRelay();
        StringBuilder sb = new StringBuilder();
        sb.Append(relay ? "v5|" : "v2|").Append(Clean(id)).Append('|').Append(Clean(mapId)).Append('|').Append(Clean(background)).Append('|')
          .Append(builtIn ? '1' : '0').Append('|').Append(Clean(name)).Append('|').Append(Clean(author)).Append('|').Append(Clean(authorId)).Append('|');
        List<CoursePoint> points = new List<CoursePoint>();
        if (relay)
        {
            foreach (RelayLegCourse course in relayCourses)
            {
                points.Add(course.start);
                foreach (RelayItem item in course.items) if (item.fork < 0) points.Add(item.point);
                points.Add(course.finish);
            }
            foreach (RelayFork fork in relayForks)
            {
                foreach (RelayBranch branch in fork.branches) points.AddRange(branch.controls);
            }
        }
        else
        {
            points = AllPoints();
        }
        long lastE = 0, lastN = 0;
        for (int i = 0; i < points.Count; i++)
        {
            long e = (long)Math.Round(points[i].e);
            long n = (long)Math.Round(points[i].n);
            if (i == 0)
            {
                AppendDigits(sb, e, 4);
                AppendDigits(sb, n, 4);
            }
            else
            {
                AppendDigits(sb, e - lastE + StepOffset, 3);
                AppendDigits(sb, n - lastN + StepOffset, 3);
            }
            lastE = e;
            lastN = n;
        }
        if (relay)
        {
            sb.Append('|').Append(legs).Append('.');
            for (int leg = 0; leg < legs; leg++) sb.Append(Digits[LegCourseIndex(leg)]);
            sb.Append('.').Append(forkEveryVariant ? 'e' : 'r').Append('.');
            for (int ci = 0; ci < relayCourses.Count; ci++)
            {
                if (ci > 0) sb.Append('~');
                foreach (RelayItem item in relayCourses[ci].items) sb.Append(item.fork < 0 ? '*' : Digits[item.fork]);
            }
            sb.Append('.');
            for (int f = 0; f < relayForks.Count; f++)
            {
                if (f > 0) sb.Append('~');
                sb.Append(Digits[relayForks[f].branches.Count]);
                foreach (RelayBranch branch in relayForks[f].branches) sb.Append(Digits[branch.controls.Count]);
            }
        }
        if (!string.IsNullOrEmpty(terrain)) sb.Append('|').Append(terrain == Sprint ? 's' : 'f');
        // Areas in a "z" field, uncrossable boundary lines in a "y" field (versions before the lines skip it)
        AppendForbidden(sb, false);
        AppendForbidden(sb, true);
        // TV controls in a "t" field: the first point in full, then steps
        if (tvControls != null && tvControls.Count > 0)
        {
            sb.Append("|t");
            long tvE = 0, tvN = 0;
            for (int i = 0; i < tvControls.Count && i < MaxTvControls; i++)
            {
                long e = (long)Math.Round(tvControls[i].e), n = (long)Math.Round(tvControls[i].n);
                if (i == 0)
                {
                    AppendDigits(sb, e, 4);
                    AppendDigits(sb, n, 4);
                }
                else
                {
                    AppendDigits(sb, e - tvE + StepOffset, 3);
                    AppendDigits(sb, n - tvN + StepOffset, 3);
                }
                tvE = e;
                tvN = n;
            }
        }
        return sb.ToString();
    }

    static List<CoursePoint> ReadTvControls(string text)
    {
        var points = new List<CoursePoint>();
        int index = 1;   // after the "t"
        long e = 0, n = 0;
        while (index < text.Length && points.Count < MaxTvControls)
        {
            if (points.Count == 0)
            {
                e = ReadDigits(text, ref index, 4);
                n = ReadDigits(text, ref index, 4);
            }
            else
            {
                e += ReadDigits(text, ref index, 3) - StepOffset;
                n += ReadDigits(text, ref index, 3) - StepOffset;
            }
            points.Add(new CoursePoint(e, n));
        }
        return points;
    }

    void AppendForbidden(StringBuilder sb, bool lines)
    {
        if (forbidden == null || !forbidden.Exists(a => a != null && a.line == lines && a.IsValid)) return;
        sb.Append(lines ? "|y" : "|z");
        bool first = true;
        long lastE = 0, lastN = 0;
        foreach (ForbiddenArea area in forbidden)
        {
            if (area == null || area.line != lines || !area.IsValid) continue;
            int count = Math.Min(area.corners.Count, Digits.Length - 1);
            sb.Append(Digits[count]);
            for (int k = 0; k < count; k++)
            {
                long e = (long)Math.Round(area.corners[k].e);
                long n = (long)Math.Round(area.corners[k].n);
                if (first)
                {
                    AppendDigits(sb, e, 4);
                    AppendDigits(sb, n, 4);
                    first = false;
                }
                else
                {
                    AppendDigits(sb, e - lastE + StepOffset, 3);
                    AppendDigits(sb, n - lastN + StepOffset, 3);
                }
                lastE = e;
                lastN = n;
            }
        }
    }

    static List<ForbiddenArea> ReadForbidden(string text, bool lines)
    {
        List<ForbiddenArea> areas = new List<ForbiddenArea>();
        int index = 1;   // after the "z" or "y"
        bool first = true;
        long e = 0, n = 0;
        while (index < text.Length)
        {
            int count = Digits.IndexOf(text[index++]);
            if (count < 0) throw new FormatException("bad area");
            ForbiddenArea area = new ForbiddenArea { line = lines };
            for (int k = 0; k < count; k++)
            {
                if (first)
                {
                    e = ReadDigits(text, ref index, 4);
                    n = ReadDigits(text, ref index, 4);
                    first = false;
                }
                else
                {
                    e += ReadDigits(text, ref index, 3) - StepOffset;
                    n += ReadDigits(text, ref index, 3) - StepOffset;
                }
                area.corners.Add(new CoursePoint(e, n));
            }
            areas.Add(area);
        }
        return areas;
    }

    static void AppendDigits(StringBuilder sb, long value, int count)
    {
        char[] digits = new char[count];
        for (int k = count - 1; k >= 0; k--)
        {
            digits[k] = Digits[(int)(value & 63)];
            value >>= 6;
        }
        sb.Append(digits);
    }

    static long ReadDigits(string text, ref int index, int count)
    {
        long value = 0;
        for (int k = 0; k < count; k++)
        {
            int digit = Digits.IndexOf(text[index++]);
            if (digit < 0) throw new FormatException("bad digit");
            value = (value << 6) | (long)digit;
        }
        return value;
    }

    // The built-in courses are on every player's computer, so only their id needs sending
    public string ToLobbyString()
    {
        return builtIn ? "b|" + id : ToCompact();
    }

    public static CourseData FromLobbyString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (text.StartsWith("b|"))
        {
            return CourseLibrary.FindBuiltIn(text.Substring(2));
        }
        return FromCompact(text);
    }

    public static CourseData FromCompact(string text)
    {
        try
        {
            string[] parts = text.Split('|');
            bool v3 = parts.Length >= 10 && parts[0] == "v3";
            bool v4 = parts.Length >= 10 && parts[0] == "v4";
            bool v5 = parts.Length >= 10 && parts[0] == "v5";
            if (parts.Length < 9 || (parts[0] != "v2" && !v3 && !v4 && !v5))
            {
                return null;
            }
            CourseData course = new CourseData
            {
                id = parts[1],
                mapId = parts[2],
                background = parts[3],
                builtIn = parts[4] == "1",
                name = parts[5],
                author = parts[6],
                authorId = parts[7]
            };
            // The course type comes after the points (v2) or after the relay field (v3 to v5)
            int terrainPart = parts[0] == "v2" ? 9 : 10;
            if (parts.Length > terrainPart) course.terrain = parts[terrainPart] == "s" ? Sprint : parts[terrainPart] == "f" ? Forest : null;
            for (int p = terrainPart; p < parts.Length; p++)
            {
                if (parts[p].StartsWith("z")) course.forbidden.AddRange(ReadForbidden(parts[p], false));
                if (parts[p].StartsWith("y")) course.forbidden.AddRange(ReadForbidden(parts[p], true));
                if (parts[p].StartsWith("t")) course.tvControls = ReadTvControls(parts[p]);
            }
            string coords = parts[8];
            if (coords.Length < 14 || (coords.Length - 8) % 6 != 0)
            {
                return null;
            }
            List<CoursePoint> points = new List<CoursePoint>();
            int index = 0;
            long e = ReadDigits(coords, ref index, 4);
            long n = ReadDigits(coords, ref index, 4);
            points.Add(new CoursePoint(e, n));
            while (index < coords.Length)
            {
                e += ReadDigits(coords, ref index, 3) - StepOffset;
                n += ReadDigits(coords, ref index, 3) - StepOffset;
                points.Add(new CoursePoint(e, n));
            }

            if (v5)
            {
                ReadRelayV5(course, parts[9], points);
                if (!course.MakeSafe()) return null;
                course.Normalize();
                return course;
            }
            if (v4)
            {
                ReadRelayV4(course, parts[9], points);
                if (!course.MakeSafe()) return null;
                course.Normalize();
                return course;
            }

            int variantPoints = 0;
            if (v3)
            {
                string[] relayParts = parts[9].Split('.');
                course.kind = KindRelay;
                course.legs = int.Parse(relayParts[0], CultureInfo.InvariantCulture);
                foreach (char c in relayParts[1]) course.forkedLegs.Add(c == '1');
                course.forkEveryVariant = relayParts[2] != "r";
                string forkText = relayParts.Length > 3 ? relayParts[3] : "";
                for (int k = 0; k + 1 < forkText.Length; k += 2)
                {
                    int control = Digits.IndexOf(forkText[k]);
                    int count = Digits.IndexOf(forkText[k + 1]);
                    if (control < 0 || count < 2) throw new FormatException("bad fork");
                    course.forks.Add(new ControlFork { control = control });
                    variantPoints += count - 1;
                }
            }

            int mainCount = points.Count - variantPoints;
            if (mainCount < 2) throw new FormatException("too few points");
            course.start = points[0];
            course.finish = points[mainCount - 1];
            course.controls = points.GetRange(1, mainCount - 2);
            int next = mainCount;
            if (v3)
            {
                string forkText = parts[9].Split('.').Length > 3 ? parts[9].Split('.')[3] : "";
                for (int k = 0; k < course.forks.Count; k++)
                {
                    int count = Digits.IndexOf(forkText[k * 2 + 1]);
                    course.forks[k].variants = points.GetRange(next, count - 1);
                    next += count - 1;
                }
            }
            if (!course.MakeSafe()) return null;
            if (v3) course.Normalize();
            return course;
        }
        catch (Exception e)
        {
            Debug.LogWarning("CourseData: could not read course text: " + e.Message);
            return null;
        }
    }

    static void ReadRelayV5(CourseData course, string relayText, List<CoursePoint> points)
    {
        string[] relayParts = relayText.Split('.');
        course.kind = KindRelay;
        course.legs = int.Parse(relayParts[0], CultureInfo.InvariantCulture);
        foreach (char c in relayParts[1])
        {
            int ci = Digits.IndexOf(c);
            if (ci < 0) throw new FormatException("bad leg course");
            course.legCourse.Add(ci);
        }
        course.forkEveryVariant = relayParts[2] != "r";
        int next = 0;
        foreach (string text in relayParts[3].Split('~'))
        {
            RelayLegCourse relayCourse = new RelayLegCourse { start = points[next++] };
            foreach (char c in text)
            {
                if (c == '*')
                {
                    relayCourse.items.Add(new RelayItem { fork = -1, point = points[next++] });
                }
                else
                {
                    int f = Digits.IndexOf(c);
                    if (f < 0) throw new FormatException("bad fork");
                    relayCourse.items.Add(new RelayItem { fork = f });
                }
            }
            relayCourse.finish = points[next++];
            course.relayCourses.Add(relayCourse);
        }
        string forkText = relayParts.Length > 4 ? relayParts[4] : "";
        if (forkText.Length > 0)
        {
            foreach (string text in forkText.Split('~'))
            {
                RelayFork fork = new RelayFork();
                int branches = Digits.IndexOf(text[0]);
                for (int b = 0; b < branches; b++)
                {
                    int count = Digits.IndexOf(text[1 + b]);
                    fork.branches.Add(new RelayBranch { controls = points.GetRange(next, count) });
                    next += count;
                }
                course.relayForks.Add(fork);
            }
        }
        if (next != points.Count) throw new FormatException("point count does not match");
    }

    static void ReadRelayV4(CourseData course, string relayText, List<CoursePoint> points)
    {
        string[] relayParts = relayText.Split('.');
        course.kind = KindRelay;
        course.legs = int.Parse(relayParts[0], CultureInfo.InvariantCulture);
        foreach (char c in relayParts[1])
        {
            int ci = Digits.IndexOf(c);
            if (ci < 0) throw new FormatException("bad leg course");
            course.legCourse.Add(ci);
        }
        course.forkEveryVariant = relayParts[2] != "r";
        string[] courseTexts = relayParts[3].Split('~');
        int next = 0;
        for (int ci = 0; ci < courseTexts.Length; ci++)
        {
            string text = courseTexts[ci];
            int controlCount = Digits.IndexOf(text[0]);
            if (controlCount < 0) throw new FormatException("bad control count");
            RelayCourse relayCourse = new RelayCourse
            {
                start = points[next],
                controls = points.GetRange(next + 1, controlCount),
                finish = points[next + 1 + controlCount]
            };
            next += controlCount + 2;
            for (int k = 1; k + 1 < text.Length; k += 2)
            {
                int control = Digits.IndexOf(text[k]);
                int count = Digits.IndexOf(text[k + 1]);
                if (control < 0 || count < 2) throw new FormatException("bad fork");
                relayCourse.forks.Add(new ControlFork { control = control, variants = points.GetRange(next, count - 1) });
                next += count - 1;
            }
            if (ci == 0)
            {
                course.start = relayCourse.start;
                course.controls = relayCourse.controls;
                course.finish = relayCourse.finish;
                course.forks = relayCourse.forks;
            }
            else
            {
                course.extraCourses.Add(relayCourse);
            }
        }
        if (next != points.Count) throw new FormatException("point count does not match");
    }

    static string Clean(string value)
    {
        return string.IsNullOrEmpty(value) ? "" : value.Replace("|", "/").Replace(";", ",");
    }

    // ---- Courses from other players ----
    // A course that comes from the cloud or from the host of a lobby was written by somebody else's game, and that
    // game can be an old version, a broken one or a tampered one. MakeSafe tidies such a course before it is shown
    // or run: texts are cut to their normal length and can not carry formatting, missing lists become empty, and a
    // course that is too big or has impossible coordinates is refused (false). Call it before Normalize.
    public const int MaxRelayForks = 64;    // a fork's number is one symbol in the lobby text
    const int MaxForkBranches = 63;         // and so is the number of branches
    const double MaxCoordinate = 1e8;   // metres; far outside any map

    public bool MakeSafe()
    {
        id = (id ?? "").Trim();
        if (!SafeToken(id, 64)) return false;
        mapId = (mapId ?? "").Trim();
        if (mapId.Length == 0 || mapId.Length > 64) return false;
        mapId = SafeText(mapId, 64);
        name = SafeText(name, MaxNameLength);
        if (name.Length == 0) name = "Course";
        author = SafeText(author, MaxAuthorLength);
        authorId = SafeText(authorId, 64);
        background = SafeText(background, 64);
        if (terrain != Forest && terrain != Sprint) terrain = null;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (createdAt < 0) createdAt = 0;
        if (createdAt > now + 86400) createdAt = now;   // a date in the future would keep the course on top of the list for ever

        if (controls == null) controls = new List<CoursePoint>();
        if (controls.Count > MaxControls) return false;
        if (!Finite(start) || !Finite(finish) || !Finite(controls)) return false;

        if (forbidden == null) forbidden = new List<ForbiddenArea>();
        forbidden.RemoveAll(a => a == null || a.corners == null);
        if (forbidden.Count > ForbiddenArea.MaxAreas) forbidden.RemoveRange(ForbiddenArea.MaxAreas, forbidden.Count - ForbiddenArea.MaxAreas);
        foreach (ForbiddenArea area in forbidden)
        {
            if (area.corners.Count > ForbiddenArea.MaxCorners) area.corners.RemoveRange(ForbiddenArea.MaxCorners, area.corners.Count - ForbiddenArea.MaxCorners);
            if (!Finite(area.corners)) return false;
        }

        if (tvControls == null) tvControls = new List<CoursePoint>();
        if (tvControls.Count > MaxTvControls) tvControls.RemoveRange(MaxTvControls, tvControls.Count - MaxTvControls);
        if (!Finite(tvControls)) return false;

        if (legCourse == null) legCourse = new List<int>();
        if (relayCourses == null) relayCourses = new List<RelayLegCourse>();
        if (relayForks == null) relayForks = new List<RelayFork>();
        if (forks == null) forks = new List<ControlFork>();
        if (extraCourses == null) extraCourses = new List<RelayCourse>();
        if (forkedLegs == null) forkedLegs = new List<bool>();
        if (!IsRelay())
        {
            // An individual course has no use for the relay parts
            legCourse.Clear();
            relayCourses.Clear();
            relayForks.Clear();
            forks.Clear();
            extraCourses.Clear();
            forkedLegs.Clear();
            tvControls.Clear();   // TV controls are for relays only
            return true;
        }

        if (legCourse.Count > MaxLegs || forkedLegs.Count > MaxLegs) return false;
        if (relayCourses.Count > MaxLegs || extraCourses.Count > MaxLegs) return false;
        if (relayForks.Count > MaxRelayForks || forks.Count > MaxControls) return false;
        foreach (RelayFork fork in relayForks)
        {
            if (fork == null) return false;
            if (fork.branches == null) fork.branches = new List<RelayBranch>();
            if (fork.branches.Count > MaxForkBranches) return false;
            foreach (RelayBranch branch in fork.branches)
            {
                if (branch == null) return false;
                if (branch.controls == null) branch.controls = new List<CoursePoint>();
                if (branch.controls.Count > MaxControls || !Finite(branch.controls)) return false;
            }
        }
        foreach (RelayLegCourse course in relayCourses)
        {
            if (course == null) return false;
            if (course.items == null) course.items = new List<RelayItem>();
            if (course.items.Count > 2 * MaxControls || !Finite(course.start) || !Finite(course.finish)) return false;
            foreach (RelayItem item in course.items)
            {
                if (item == null || item.fork < -1 || item.fork >= relayForks.Count) return false;
                if (item.fork < 0 && !Finite(item.point)) return false;
            }
        }
        // Relays from earlier versions of the builder
        foreach (ControlFork fork in forks)
        {
            if (fork == null) return false;
            if (fork.variants == null) fork.variants = new List<CoursePoint>();
            if (fork.variants.Count > MaxForkBranches || !Finite(fork.variants)) return false;
        }
        foreach (RelayCourse course in extraCourses)
        {
            if (course == null) return false;
            if (course.controls == null) course.controls = new List<CoursePoint>();
            if (course.forks == null) course.forks = new List<ControlFork>();
            if (course.controls.Count > MaxControls || course.forks.Count > MaxControls) return false;
            if (!Finite(course.start) || !Finite(course.finish) || !Finite(course.controls)) return false;
            foreach (ControlFork fork in course.forks)
            {
                if (fork == null) return false;
                if (fork.variants == null) fork.variants = new List<CoursePoint>();
                if (fork.variants.Count > MaxForkBranches || !Finite(fork.variants)) return false;
            }
        }
        return true;
    }

    static bool Finite(CoursePoint point)
    {
        return !double.IsNaN(point.e) && !double.IsNaN(point.n) && Math.Abs(point.e) < MaxCoordinate && Math.Abs(point.n) < MaxCoordinate;
    }

    static bool Finite(List<CoursePoint> points)
    {
        foreach (CoursePoint point in points)
        {
            if (!Finite(point)) return false;
        }
        return true;
    }

    // Letters, digits, '-' and '_' only (ids)
    static bool SafeToken(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength) return false;
        foreach (char c in value)
        {
            if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_')) return false;
        }
        return true;
    }

    // A text another player wrote: one line, no formatting tags (the pages show names as rich text), not too long
    public static string SafeText(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        StringBuilder sb = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (char c in value)
        {
            if (c == '<' || c == '>' || char.IsControl(c)) continue;
            sb.Append(c);
            if (sb.Length >= maxLength) break;
        }
        return sb.ToString().Trim();
    }
}

// One course of a relay: legs that run the same course are forked against each other by its variants
[Serializable]
public class RelayCourse
{
    public CoursePoint start;
    public List<CoursePoint> controls = new List<CoursePoint>();
    public CoursePoint finish;
    public List<ControlFork> forks = new List<ControlFork>();

    public ControlFork ForkOf(int control)
    {
        foreach (ControlFork fork in forks)
        {
            if (fork.control == control) return fork;
        }
        return null;
    }

    // How many ways there are to run a control: 1 for a normal control, more for a forked one
    public int VariantCount(int control)
    {
        ControlFork fork = ForkOf(control);
        return 1 + (fork != null ? fork.variants.Count : 0);
    }

    // Variant 0 is the control in the controls list, 1.. are the fork's other points
    public CoursePoint ControlVariant(int control, int variant)
    {
        if (variant <= 0) return controls[control];
        return ForkOf(control).variants[variant - 1];
    }

    public int VariantPointCount()
    {
        int count = 0;
        foreach (ControlFork fork in forks) count += fork.variants.Count;
        return count;
    }

    public int MostVariants()
    {
        int most = 1;
        for (int c = 0; c < controls.Count; c++) most = Math.Max(most, VariantCount(c));
        return most;
    }

    public List<CoursePoint> AllPoints()
    {
        List<CoursePoint> points = new List<CoursePoint>(controls.Count + 2) { start };
        points.AddRange(controls);
        points.Add(finish);
        return points;
    }

    // Straight-line length from start to finish in metres, along variant 0
    public float LengthMeters()
    {
        List<CoursePoint> points = AllPoints();
        float total = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            double de = points[i].e - points[i - 1].e;
            double dn = points[i].n - points[i - 1].n;
            total += (float)Math.Sqrt(de * de + dn * dn);
        }
        return total;
    }
}

// The other places a forked control can be run at. Variant 0 is the control in the course's controls.
[Serializable]
public class ControlFork
{
    public int control;                                  // index into the course's controls
    public List<CoursePoint> variants = new List<CoursePoint>();
}

// One leg's course in a relay: a start, controls and forks in running order, and a finish
[Serializable]
public class RelayLegCourse
{
    public CoursePoint start;
    public List<RelayItem> items = new List<RelayItem>();
    public CoursePoint finish;
}

// A control (fork = -1) or a fork (index into CourseData.relayForks) on a relay course
[Serializable]
public class RelayItem
{
    public int fork = -1;
    public CoursePoint point;
}

// The part of a relay course between two common controls that can be run in different ways
[Serializable]
public class RelayFork
{
    public List<RelayBranch> branches = new List<RelayBranch>();
}

[Serializable]
public class RelayBranch
{
    public List<CoursePoint> controls = new List<CoursePoint>();
}

// How one team runs one leg (CourseData.Route)
public class LegRoute
{
    public CourseData course;                              // start, controls in running order, finish
    public readonly List<int> commonOrdinal = new List<int>(); // per point (start = 0 ... finish): its number among the common points, -1 on a branch
    public readonly List<int> segmentBranch = new List<int>(); // per common point: the branch run to get there from the common point before, -1 = no fork
    public int CommonCount => segmentBranch.Count;
}
