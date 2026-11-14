using TMPro;
using UnityEngine;
using UnityEngine.UI;

// F3 shows how smoothly the game runs: frames per second, the average time of a frame and the slowest frame of the
// last second (a hitch shows there even when the average is fine). Press F3 again to hide it. Nothing is made or
// counted until it is first opened.
public class PerfOverlay : MonoBehaviour
{
    static PerfOverlay instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Performance overlay (F3)");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PerfOverlay>();
    }

    GameObject panel;
    TextMeshProUGUI text;
    int frames;
    float time, worst, shownFor;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F3))
        {
            if (panel == null) Build();
            panel.SetActive(!panel.activeSelf);
            frames = 0;
            time = worst = shownFor = 0f;
        }
        if (panel == null || !panel.activeSelf) return;

        float dt = Time.unscaledDeltaTime;
        frames++;
        time += dt;
        if (dt > worst) worst = dt;
        shownFor += dt;
        if (shownFor < 0.5f) return;
        float average = time / frames;
        text.text = Mathf.RoundToInt(1f / Mathf.Max(0.0001f, average)) + " FPS\n<size=70%>" +
                    (average * 1000f).ToString("0.0") + " ms a frame, slowest " + (worst * 1000f).ToString("0") + " ms</size>";
        frames = 0;
        time = worst = shownFor = 0f;
    }

    void Build()
    {
        var root = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;   // above everything
        ScreenSettings.Fit(root.GetComponent<CanvasScaler>());

        Image back = CourseUI.Panel("Performance", root.transform, new Color(0.06f, 0.14f, 0.09f, 0.82f), false);
        back.raycastTarget = false;
        CourseUI.Place(back.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(330f, 74f));
        text = CourseUI.Text("Text", back.transform, "...", 26, Color.white, FontStyles.Bold);
        text.raycastTarget = false;
        CourseUI.Stretch(text.rectTransform, 14, 10, 6, 6);
        panel = root;
        panel.SetActive(false);
    }
}
