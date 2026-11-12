using UnityEngine;

// Settings that belong to one player on one computer (their name, lobby name, colour).
// When the game is tested with a ParrelSync clone of the project, the clone's editor and the main editor share the
// same saved settings (PlayerPrefs live in one place per company and product name), so a name or colour chosen in
// one showed up in the other, and after a race everyone had the colour picked last. A clone therefore keeps these
// under keys of its own. A real build on another computer is not affected (it has its own settings anyway).
public static class LocalPrefs
{
    static string suffix;

    public static string Key(string name)
    {
        if (suffix == null)
        {
            suffix = "";
#if UNITY_EDITOR
            // ParrelSync names a clone's folder "<project>_clone_<n>"
            string path = Application.dataPath.Replace('\\', '/');
            int at = path.LastIndexOf("_clone_");
            if (at >= 0)
            {
                string rest = path.Substring(at + 7);
                int slash = rest.IndexOf('/');
                suffix = ".clone" + (slash >= 0 ? rest.Substring(0, slash) : rest);
            }
#endif
        }
        return name + suffix;
    }
}
