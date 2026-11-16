using System;
using System.Collections.Generic;

// Searching and filtering course lists (the lobby's course page and the single-player page)
public static class CourseFilter
{
    public enum Kind { All, Individual, Relay, Forest, Sprint }

    public static string KindLabel(Kind kind)
    {
        switch (kind)
        {
            case Kind.Individual: return "Type: Individual";
            case Kind.Relay: return "Type: Relay";
            case Kind.Forest: return "Type: Forest";
            case Kind.Sprint: return "Type: Sprint";
            default: return "Type: All";
        }
    }

    public static readonly string[] KindNames = { "All courses", "Individual", "Relay", "Forest", "Sprint" };

    // Several types at once (2026-11-16, mix and match): ticked among Individual/Relay and among Forest/Sprint.
    // Within a pair any ticked one matches; nothing ticked in a pair = both.
    public static readonly string[] TypeOptions = { "Individual", "Relay", "Forest", "Sprint" };

    public static string TypesLabel(HashSet<string> types)
    {
        if (types == null || types.Count == 0 || types.Count == TypeOptions.Length) return "Type: All";
        var names = new List<string>();
        foreach (string t in TypeOptions) if (types.Contains(t)) names.Add(t);
        return "Type: " + string.Join(", ", names);
    }

    public static bool MatchesTypes(CourseData course, HashSet<string> types)
    {
        if (course == null) return false;
        if (types == null || types.Count == 0) return true;
        bool format = types.Contains("Individual") || types.Contains("Relay");
        if (format && !types.Contains(course.IsRelay() ? "Relay" : "Individual")) return false;
        bool ground = types.Contains("Forest") || types.Contains("Sprint");
        if (ground && !types.Contains(course.IsSprint() ? "Sprint" : "Forest")) return false;
        return true;
    }

    public static bool Matches(CourseData course, string search, HashSet<string> types)
    {
        return MatchesTypes(course, types) && Matches(course, search, Kind.All);
    }

    public static Kind Next(Kind kind) => (Kind)(((int)kind + 1) % 5);

    // The search matches the course name, the author and words like "relay" or "forked"
    public static bool Matches(CourseData course, string search, Kind kind)
    {
        if (course == null) return false;
        if (kind == Kind.Relay && !course.IsRelay()) return false;
        if (kind == Kind.Individual && course.IsRelay()) return false;
        if (kind == Kind.Forest && course.IsSprint()) return false;
        if (kind == Kind.Sprint && !course.IsSprint()) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        string text = (course.name + " " + course.author + " " + (course.IsRelay() ? "relay stafett " + course.legs + " legs" + (course.AnyForks() ? " forked gafflad" : "") : "individual") + (course.IsSprint() ? " sprint" : " forest skog")).ToLowerInvariant();
        foreach (string word in search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!text.Contains(word)) return false;
        }
        return true;
    }

    public static string Facts(CourseData course)
    {
        return course.IsRelay()
            ? "Relay, " + course.legs + " legs" + (course.AnyForks() ? ", forked" : "")
            : course.controls.Count + " controls  ·  " + course.LengthText();
    }
}
