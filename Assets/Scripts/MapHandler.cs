using UnityEngine;

// The course the host has picked in the lobby. The name is what the lobby shows; the course itself
// is kept in CourseLibrary.Current so the competition scene can find it after the scene change.
public class MapHandler : MonoBehaviour
{
    public string map;

    public CourseData course => CourseLibrary.CurrentOrDefault;

    void Awake()
    {
        CourseData current = CourseLibrary.CurrentOrDefault;
        map = current != null ? current.name : "";
    }

    public void Select(CourseData chosen)
    {
        CourseLibrary.Current = chosen;
        map = chosen != null ? chosen.name : "";
    }
}
