using System;

// Searching and filtering course lists (the lobby's course page and the single-player page)
public static class CourseFilter
{
    public enum Kind { All, Individual, Relay }

    public static string KindLabel(Kind kind)
    {
        switch (kind)
        {
            case Kind.Individual: return "Type: Individual";
            case Kind.Relay: return "Type: Relay";
            default: return "Type: All";
        }
    }

    public static Kind Next(Kind kind) => (Kind)(((int)kind + 1) % 3);

    // The search matches the course name, the author and words like "relay" or "forked"
    public static bool Matches(CourseData course, string search, Kind kind)
    {
        if (course == null) return false;
        if (kind == Kind.Relay && !course.IsRelay()) return false;
        if (kind == Kind.Individual && course.IsRelay()) return false;
        if (string.IsNullOrWhiteSpace(search)) return true;
        string text = (course.name + " " + course.author + " " + (course.IsRelay() ? "relay stafett " + course.legs + " legs" + (course.AnyForks() ? " forked gafflad" : "") : "individual")).ToLowerInvariant();
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
