using UnityEngine;
using UnityEngine.SceneManagement;

// Main menu button that opens the course builder
public class OpenCourseBuilder : MonoBehaviour
{
    public void Open()
    {
        SceneManager.LoadScene(CourseScenes.Builder);
    }
}
