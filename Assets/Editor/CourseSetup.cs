using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// One-time setup that turns the old "one scene per course" layout into courses as data:
//  - a MapDefinition for Illern with the grid's real-world bounds and the map pictures,
//  - the four IllernKartan Träning scenes as course files in Resources/Courses,
//  - small thumbnails of the map pictures for the menus,
//  - the shared Competition scene (a copy of IllernKartan Träning with a CourseLoader),
//  - the CourseBuilder scene, build settings, the main menu button and the lobby labels.
// Safe to run again: everything it makes is rebuilt.
public static class CourseSetup
{
    const string MapId = "illern";
    const string MapDefPath = "Assets/Resources/MapDefs/Illern.asset";
    const string CoursesFolder = "Assets/Resources/Courses";
    const string ThumbFolder = "Assets/Resources/CourseThumbs";
    const string SkinPath = "Assets/Resources/UI/CourseUISkin.asset";
    const string GridAsset = "Assets/Resources/2d-gridJson/output_grid_bin.bytes";
    const string TemplateScene = "Assets/Scenes/IllernKartan Träning.unity";
    const string CompetitionScene = "Assets/Scenes/Competition.unity";
    const string BuilderScene = "Assets/Scenes/CourseBuilder.unity";
    const string MainMenuScene = "Assets/Scenes/MainMenu.unity";
    const string LobbyScene = "Assets/Scenes/Lobby.unity";

    // The old course scenes, the background id they become and the course name
    static readonly (string scene, string background, string courseId, string courseName)[] OldCourses =
    {
        ("Assets/Scenes/IllernKartan Träning.unity", "traning1", "illern-traning-1", "Illern Träning"),
        ("Assets/Scenes/IllernKartan Träning 2.unity", "traning2", "illern-traning-2", "Illern Träning 2"),
        ("Assets/Scenes/IllernKartan Träning 3.unity", "traning3", "illern-traning-3", "Illern Träning 3"),
        ("Assets/Scenes/IllernKartan Träning 4.unity", "traning4", "illern-traning-4", "Illern Träning 4"),
    };

    // New courses are drawn on a picture without a printed course. "Illern utan bana" is the Träning 3 picture
    // with the purple course removed; it has the same size and placement as that picture.
    const string PlaceholderFrom = "traning3";
    const string CleanPicture = "Assets/Resources/KartBild/Illern utan bana.png";

    static StringBuilder log;

    [MenuItem("Tools/O-Manager/Set up courses (map, courses, scenes)")]
    public static void RunMenu()
    {
        Debug.Log(Run());
    }

    public static string Run()
    {
        log = new StringBuilder();
        // The old Illern map and its four training courses were removed, so this one-time setup would only bring them back
        return "Retired: the old Illern map was removed from the game (use the Illern 2025 map).";
    }

    static string RunOld()
    {
        log = new StringBuilder();
        if (EditorApplication.isPlaying) return "Aborted: exit Play mode first.";
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty) return "Aborted: scene '" + SceneManager.GetSceneAt(i).name + "' has unsaved changes.";
        }
        string startScene = SceneManager.GetActiveScene().path;

        CreateSkin();
        MapDefinition map = CreateMap();
        ConvertCourses(map);
        CreateCompetitionScene();
        CreateBuilderScene();
        UpdateBuildSettings();
        AddMenuButton();
        RenameLobbyLabels();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!string.IsNullOrEmpty(startScene) && File.Exists(startScene))
        {
            EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
        }
        return log.ToString();
    }

    // ---- UI skin ----

    static void CreateSkin()
    {
        CourseUISkin skin = LoadOrCreate<CourseUISkin>(SkinPath);
        skin.rounded = ModernUIBuilder.ImportSprite(ModernUIBuilder.RoundedPath, 32);
        skin.pill = ModernUIBuilder.ImportSprite(ModernUIBuilder.PillPath, 63);
        skin.background = AssetDatabase.LoadAssetAtPath<Sprite>(ModernUIBuilder.BackgroundPath);
        skin.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ModernUIBuilder.FontPath);
        EditorUtility.SetDirty(skin);
        log.AppendLine("skin rounded=" + (skin.rounded != null) + " pill=" + (skin.pill != null) + " font=" + (skin.font != null));
    }

    // ---- Map ----

    static MapDefinition CreateMap()
    {
        TextAsset gridBytes = AssetDatabase.LoadAssetAtPath<TextAsset>(GridAsset);
        if (gridBytes == null || !GridBinary.TryRead(gridBytes.bytes, out GridData data, out float[,] speed, out _))
        {
            throw new System.Exception("CourseSetup: could not read " + GridAsset);
        }

        MapDefinition map = LoadOrCreate<MapDefinition>(MapDefPath);
        map.id = MapId;
        map.displayName = "Illern";
        map.gridName = "output_grid";
        map.minx = data.bounds.minx;
        map.miny = data.bounds.miny;
        map.maxx = data.bounds.maxx;
        map.maxy = data.bounds.maxy;
        map.gridSize = data.grid_size;
        map.rows = speed.GetLength(0);
        map.cols = speed.GetLength(1);
        map.backgrounds = new List<MapBackground>();
        Directory.CreateDirectory(ThumbFolder);

        foreach (var old in OldCourses)
        {
            Scene scene = EditorSceneManager.OpenScene(old.scene, OpenSceneMode.Additive);
            SpriteRenderer picture = FindMapPicture(scene);
            MapBackground background = Describe(picture, old.background);
            background.courseInPicture = true;
            map.backgrounds.Add(background);
            EditorSceneManager.CloseScene(scene, true);
        }

        MapBackground placeholderSource = map.backgrounds.Find(b => b.id == PlaceholderFrom);
        MapBackground clean = JsonUtility.FromJson<MapBackground>(JsonUtility.ToJson(placeholderSource));
        clean.id = MapId;
        clean.courseInPicture = false;
        if (File.Exists(CleanPicture))
        {
            clean.spritePath = ResourcesPath(CleanPicture);
            clean.thumbnailPath = ResourcesPath(ThumbFolder + "/" + MapId + ".png");
            MakeThumbnail(AssetDatabase.LoadAssetAtPath<Sprite>(CleanPicture), ThumbFolder + "/" + MapId + ".png");
        }
        else
        {
            log.AppendLine("WARNING: no clean map picture, new courses are drawn on " + PlaceholderFrom);
        }
        map.backgrounds.Insert(0, clean);
        map.defaultBackground = MapId;

        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
        log.AppendLine($"map {map.displayName}: {map.cols}x{map.rows} cells, bounds {map.minx},{map.miny} - {map.maxx},{map.maxy}, {map.backgrounds.Count} pictures");
        return map;
    }

    static SpriteRenderer FindMapPicture(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.CompareTag("MapImage") || (renderer.sprite != null && renderer.sprite.texture.width > 2000))
                {
                    return renderer;
                }
            }
        }
        throw new System.Exception("CourseSetup: no map picture in " + scene.path);
    }

    static MapBackground Describe(SpriteRenderer picture, string id)
    {
        Sprite sprite = picture.sprite;
        string assetPath = AssetDatabase.GetAssetPath(sprite);
        MapBackground background = new MapBackground
        {
            id = id,
            spritePath = ResourcesPath(assetPath),
            thumbnailPath = ResourcesPath(ThumbFolder + "/" + id + ".png"),
            position = picture.transform.position,
            rotation = picture.transform.eulerAngles.z,
            scale = picture.transform.lossyScale.x,
            pixelSize = sprite.rect.size,
            pivotPixels = sprite.pivot,
            pixelsPerUnit = sprite.pixelsPerUnit
        };
        MakeThumbnail(sprite, ThumbFolder + "/" + id + ".png");
        return background;
    }

    static void MakeThumbnail(Sprite sprite, string path)
    {
        const int width = 1024;
        int height = Mathf.RoundToInt(width * sprite.rect.height / sprite.rect.width);
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture texture = sprite.texture;
        Vector2 scale = new Vector2(sprite.rect.width / texture.width, sprite.rect.height / texture.height);
        Vector2 offset = new Vector2(sprite.rect.x / texture.width, sprite.rect.y / texture.height);
        Graphics.Blit(texture, target, scale, offset);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D result = new Texture2D(width, height, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        File.WriteAllBytes(path, result.EncodeToPNG());
        Object.DestroyImmediate(result);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.isReadable = true;            // CourseThumbnail draws the course on a copy
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }

    // ---- Courses ----

    static void ConvertCourses(MapDefinition map)
    {
        Directory.CreateDirectory(CoursesFolder);
        foreach (var old in OldCourses)
        {
            Scene scene = EditorSceneManager.OpenScene(old.scene, OpenSceneMode.Additive);
            PathfindingManager manager = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                manager = root.GetComponentInChildren<PathfindingManager>(true);
                if (manager != null) break;
            }
            List<Transform> points = manager.controlPoints;
            CourseData course = new CourseData
            {
                id = old.courseId,
                name = old.courseName,
                author = "O-Manager",
                authorId = "",
                mapId = map.id,
                background = old.background,
                builtIn = true,
                createdAt = 0,
                start = map.FromWorld(points[0].position),
                finish = map.FromWorld(points[points.Count - 1].position)
            };
            for (int i = 1; i < points.Count - 1; i++)
            {
                course.controls.Add(map.FromWorld(points[i].position));
            }

            // Check the round trip: the converted points must land where the old ones were
            float worst = 0f;
            List<CoursePoint> converted = course.AllPoints();
            for (int i = 0; i < points.Count; i++)
            {
                worst = Mathf.Max(worst, Vector2.Distance(map.ToWorld(converted[i]), points[i].position));
            }
            EditorSceneManager.CloseScene(scene, true);

            string path = CoursesFolder + "/" + old.courseId + ".json";
            File.WriteAllText(path, CourseLibrary.ToJson(course));
            AssetDatabase.ImportAsset(path);
            log.AppendLine($"course {course.name}: {course.controls.Count} controls, {course.LengthText()}, largest position change {worst:F5} units");
        }
    }

    // ---- Scenes ----

    static void CreateCompetitionScene()
    {
        if (!File.Exists(CompetitionScene))
        {
            AssetDatabase.CopyAsset(TemplateScene, CompetitionScene);
        }
        Scene scene = EditorSceneManager.OpenScene(CompetitionScene, OpenSceneMode.Single);
        PathfindingManager manager = Object.FindFirstObjectByType<PathfindingManager>(FindObjectsInactive.Include);
        SpriteRenderer picture = FindMapPicture(scene);

        // The control points now come from the course, so the ones placed by hand go
        foreach (Transform point in manager.controlPoints)
        {
            if (point != null) Object.DestroyImmediate(point.gameObject);
        }
        manager.controlPoints = new List<Transform>();

        CourseLoader loader = manager.GetComponent<CourseLoader>();
        if (loader == null) loader = manager.gameObject.AddComponent<CourseLoader>();
        loader.pathfindingManager = manager;
        loader.mapRenderer = picture;
        loader.pointParent = null;

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        log.AppendLine("Competition scene saved=" + EditorSceneManager.SaveScene(scene));
    }

    static void CreateBuilderScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 30f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = CourseUI.Ink;
        camera.transform.position = new Vector3(0, 0, -10);
        cameraObject.AddComponent<AudioListener>();

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        CourseBuilder builder = new GameObject("Course builder").AddComponent<CourseBuilder>();
        builder.mapCamera = camera;

        log.AppendLine("CourseBuilder scene saved=" + EditorSceneManager.SaveScene(scene, BuilderScene));
    }

    static void UpdateBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        HashSet<string> oldCourseScenes = new HashSet<string>();
        foreach (var old in OldCourses) oldCourseScenes.Add(old.scene);

        // The old course scenes stay in the project for now but are no longer built
        for (int i = 0; i < scenes.Count; i++)
        {
            if (oldCourseScenes.Contains(scenes[i].path)) scenes[i] = new EditorBuildSettingsScene(scenes[i].path, false);
        }
        foreach (string path in new[] { CompetitionScene, BuilderScene })
        {
            if (!scenes.Exists(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
        log.AppendLine("build settings: " + scenes.Count + " scenes");
    }

    static void AddMenuButton()
    {
        Scene scene = EditorSceneManager.OpenScene(MainMenuScene, OpenSceneMode.Single);
        GameObject canvasObject = GameObject.Find("MainMenu");
        Transform home = canvasObject != null ? canvasObject.transform.Find("Home screen") : null;
        if (home == null)
        {
            log.AppendLine("main menu: Home screen not found, button not added");
            return;
        }
        Transform existing = home.Find("Course builder");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        Transform quit = home.Find("Quit");

        GameObject buttonObject = Object.Instantiate(quit.gameObject, home);
        buttonObject.name = "Course builder";
        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.anchorMin = new Vector2(1, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-48, 44);
        rect.sizeDelta = new Vector2(300, rect.sizeDelta.y);
        Button button = buttonObject.GetComponent<Button>();
        button.image.color = CourseUI.Accent;
        TextMeshProUGUI label = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
        label.text = "Course builder";
        label.color = Color.white;
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        }
        OpenCourseBuilder opener = canvasObject.GetComponent<OpenCourseBuilder>();
        if (opener == null) opener = canvasObject.AddComponent<OpenCourseBuilder>();
        UnityEventTools.AddPersistentListener(button.onClick, opener.Open);

        EditorSceneManager.MarkSceneDirty(scene);
        log.AppendLine("main menu button saved=" + EditorSceneManager.SaveScene(scene));
    }

    static void RenameLobbyLabels()
    {
        Scene scene = EditorSceneManager.OpenScene(LobbyScene, OpenSceneMode.Single);
        int changed = 0;
        foreach (TextMeshProUGUI text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string path = text.transform.parent != null ? text.transform.parent.name + "/" + text.name : text.name;
            if (path == "Map/Caption" && text.text == "MAP") { text.text = "COURSE"; changed++; }
            else if (path == "Change Map/Text (TMP)") { text.text = "Change course"; changed++; }
            else if (path == "SelectMapPanel/Rubrik") { text.text = "Choose course"; changed++; }
            else continue;
            EditorUtility.SetDirty(text);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        log.AppendLine("lobby labels changed=" + changed + " saved=" + EditorSceneManager.SaveScene(scene));
    }

    // ---- Helpers ----

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
        }
        return asset;
    }

    static string ResourcesPath(string assetPath)
    {
        const string marker = "/Resources/";
        int index = assetPath.IndexOf(marker);
        string relative = index >= 0 ? assetPath.Substring(index + marker.Length) : assetPath;
        return Path.ChangeExtension(relative, null).Replace('\\', '/');
    }
}
