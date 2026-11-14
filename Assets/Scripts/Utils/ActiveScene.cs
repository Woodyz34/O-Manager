using UnityEngine.SceneManagement;

// The name of the scene being played. SceneManager.GetActiveScene().name makes a new string every time it is asked,
// and many scripts ask every frame (every runner, the camera, the clock ...), which left garbage for the garbage
// collector to clear away now and then with a small hitch. This keeps the name and reads it again only when another
// scene has become the active one (the scene's handle, a plain number, tells that without making anything).
public static class ActiveScene
{
    static string name;
    static int handle;

    public static string Name
    {
        get
        {
            Scene scene = SceneManager.GetActiveScene();
            if (name == null || scene.handle != handle)
            {
                handle = scene.handle;
                name = scene.name;
            }
            return name;
        }
    }
}
