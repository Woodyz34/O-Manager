using System;

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
