using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

// The intro that plays when the game starts, before the main menu (scene Intro, first in the build list).
//
// A short film rendered in Blender (Resources/Intro/intro.mp4, made by Docs/blender_intro.py): a run through
// a misty morning forest that stops at a control flag. When the run stops, the O-MANAGER title slams in on top
// with the SI beep (Resources/Intro/intro_sound.wav, made by Docs/intro_sound.py), and then the main menu opens.
// The main menu is loaded in the background while the film plays, so it opens at once.
//
// A click or any key skips it. Everything is built here in code, so the scene only holds this script.
public class IntroScene : MonoBehaviour
{
    const string NextScene = "MainMenu";
    const float TitleAt = 4.9f;       // seconds into the film when the run has stopped at the flag
    const float TitleHold = 2.4f;     // how long the title is shown before the main menu
    const float FadeIn = 0.5f;
    const float FadeOut = 0.45f;
    const float SkipFade = 0.25f;

    static readonly Color Cream = new Color32(0xFB, 0xF4, 0xDA, 0xFF);
    static readonly Color Accent = new Color32(0xE8, 0x74, 0x2A, 0xFF);
    static readonly Color Ink = new Color32(0x1F, 0x3D, 0x2B, 0xFF);

    VideoPlayer video;
    AudioSource sound;
    AsyncOperation menuLoad;

    Image black;
    Image flash;
    RawImage shade;
    RectTransform emblem;
    RawImage emblemImage;
    TextMeshProUGUI title;
    TextMeshProUGUI subtitle;
    RectTransform bar;
    TextMeshProUGUI skipHint;

    bool leaving;
    float started;

    void Start()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Camera").AddComponent<Camera>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Ink;   // shown only if the film cannot be played

        BuildUI();

        sound = gameObject.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        sound.spatialBlend = 0f;
        sound.volume = 0.75f;
        sound.clip = Resources.Load<AudioClip>("Intro/intro_sound");

        VideoClip clip = Resources.Load<VideoClip>("Intro/intro");
        if (clip != null)
        {
            video = cam.gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.clip = clip;
            video.isLooping = false;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.renderMode = VideoRenderMode.CameraFarPlane;
            video.targetCamera = cam;
            video.aspectRatio = VideoAspectRatio.FitOutside;   // fills the screen on any shape, cutting the edges
            video.waitForFirstFrame = true;
            video.Prepare();
        }

        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // Wait for the film to be ready (but never long: without it the title is shown on green)
        float wait = 0f;
        while (video != null && !video.isPrepared && wait < 3f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }
        if (video != null && video.isPrepared)
        {
            video.Play();
        }
        if (sound.clip != null)
        {
            sound.Play();
        }
        started = Time.unscaledTime;

        // Load the main menu behind the film so it opens without a wait
        menuLoad = SceneManager.LoadSceneAsync(NextScene);
        if (menuLoad != null)
        {
            menuLoad.allowSceneActivation = false;
        }

        StartCoroutine(Fade(black, 1f, 0f, FadeIn));
        StartCoroutine(ShowSkipHint());

        while (Elapsed() < TitleAt)
        {
            yield return null;
        }
        yield return ShowTitle();

        float until = TitleAt + TitleHold;
        while (Elapsed() < until)
        {
            yield return null;
        }
        Leave(FadeOut);
    }

    float Elapsed()
    {
        // The film's own clock when it plays, so the title lands on the flag even if the film started late
        if (video != null && video.isPlaying)
        {
            return (float)video.time;
        }
        return Time.unscaledTime - started;
    }

    void Update()
    {
        if (leaving || Time.unscaledTime - started < 0.3f || started == 0f)
        {
            return;
        }
        if (Input.anyKeyDown)
        {
            Leave(SkipFade);
        }
    }

    void Leave(float fade)
    {
        if (leaving)
        {
            return;
        }
        leaving = true;
        StopAllCoroutines();
        StartCoroutine(LeaveRoutine(fade));
    }

    IEnumerator LeaveRoutine(float fade)
    {
        float from = black.color.a;
        float startVolume = sound != null ? sound.volume : 0f;
        for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
        {
            float k = t / fade;
            SetAlpha(black, Mathf.Lerp(from, 1f, k));
            if (sound != null) sound.volume = Mathf.Lerp(startVolume, 0f, k);
            yield return null;
        }
        SetAlpha(black, 1f);
        if (menuLoad != null)
        {
            menuLoad.allowSceneActivation = true;
        }
        else
        {
            SceneManager.LoadScene(NextScene);
        }
    }

    // ---------------------------------------------------------------- the title

    IEnumerator ShowTitle()
    {
        StartCoroutine(Fade(flash, 0.75f, 0f, 0.4f));
        StartCoroutine(FadeRaw(shade, 0f, 1f, 0.5f));

        // The flag emblem lands first, the name right after, then the orange line grows and the subtitle appears
        StartCoroutine(Pop(emblem, emblemImage, null, 1.9f, 0.45f, 0f));
        StartCoroutine(Pop((RectTransform)title.transform, null, title, 1.35f, 0.4f, 0.08f));
        StartCoroutine(Grow(bar, 560f, 0.45f, 0.25f));
        StartCoroutine(FadeText(subtitle, 0.5f, 0.45f));
        yield return null;
    }

    IEnumerator Pop(RectTransform rt, RawImage raw, TextMeshProUGUI text, float fromScale, float time, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            float k = t / time;
            float s = Mathf.LerpUnclamped(fromScale, 1f, BackOut(k));
            rt.localScale = new Vector3(s, s, 1f);
            float a = Mathf.Clamp01(k * 2.5f);
            if (raw != null) raw.color = new Color(1f, 1f, 1f, a);
            if (text != null) text.alpha = a;
            yield return null;
        }
        rt.localScale = Vector3.one;
        if (raw != null) raw.color = Color.white;
        if (text != null) text.alpha = 1f;
    }

    IEnumerator Grow(RectTransform rt, float width, float time, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / time, 3f);
            rt.sizeDelta = new Vector2(width * k, rt.sizeDelta.y);
            yield return null;
        }
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
    }

    IEnumerator FadeText(TextMeshProUGUI text, float delay, float time)
    {
        yield return new WaitForSecondsRealtime(delay);
        float spacingFrom = 40f, spacingTo = 18f;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            float k = t / time;
            text.alpha = k;
            text.characterSpacing = Mathf.Lerp(spacingFrom, spacingTo, 1f - Mathf.Pow(1f - k, 3f));
            yield return null;
        }
        text.alpha = 1f;
        text.characterSpacing = spacingTo;
    }

    IEnumerator ShowSkipHint()
    {
        yield return new WaitForSecondsRealtime(1.2f);
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
        {
            skipHint.alpha = 0.6f * t / 0.6f;
            yield return null;
        }
        skipHint.alpha = 0.6f;
    }

    static float BackOut(float k)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float x = k - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    IEnumerator Fade(Image img, float from, float to, float time)
    {
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            SetAlpha(img, Mathf.Lerp(from, to, t / time));
            yield return null;
        }
        SetAlpha(img, to);
    }

    IEnumerator FadeRaw(RawImage img, float from, float to, float time)
    {
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            Color c = img.color;
            c.a = Mathf.Lerp(from, to, t / time);
            img.color = c;
            yield return null;
        }
        Color e = img.color;
        e.a = to;
        img.color = e;
    }

    static void SetAlpha(Graphic g, float a)
    {
        Color c = g.color;
        c.a = a;
        g.color = c;
    }

    // ---------------------------------------------------------------- building the page

    void BuildUI()
    {
        GameObject canvasGo = new GameObject("Intro canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        ScreenSettings.Fit(canvasGo.GetComponent<CanvasScaler>());
        RectTransform root = (RectTransform)canvasGo.transform;

        // Darkens the top of the picture so the title reads well on the bright sky
        shade = new GameObject("Shade", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        Stretch(shade.rectTransform, root);
        shade.texture = TopShade();
        shade.color = new Color(1f, 1f, 1f, 0f);
        shade.raycastTarget = false;

        emblemImage = new GameObject("Emblem", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        emblem = emblemImage.rectTransform;
        emblem.SetParent(root, false);
        emblem.anchorMin = emblem.anchorMax = new Vector2(0.5f, 1f);
        emblem.sizeDelta = new Vector2(170f, 170f);
        emblem.anchoredPosition = new Vector2(0f, -150f);
        emblemImage.texture = Resources.Load<Texture2D>("Intro/emblem");
        emblemImage.color = new Color(1f, 1f, 1f, 0f);
        emblemImage.raycastTarget = false;
        if (emblemImage.texture == null) emblemImage.enabled = false;

        title = Text(root, "Title", "O-MANAGER", 150f, Cream, FontStyles.Bold, new Vector2(0f, -330f), new Vector2(1400f, 180f));
        title.characterSpacing = 6f;
        title.alpha = 0f;
        Shadow(title);

        bar = new GameObject("Line", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        bar.SetParent(root, false);
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, 8f);
        bar.anchoredPosition = new Vector2(0f, -425f);
        Image barImage = bar.GetComponent<Image>();
        barImage.color = Accent;
        barImage.raycastTarget = false;

        subtitle = Text(root, "Subtitle", "ORIENTEERING MANAGER", 34f, Cream, FontStyles.Bold, new Vector2(0f, -470f), new Vector2(1400f, 60f));
        subtitle.alpha = 0f;
        Shadow(subtitle);

        skipHint = Text(root, "Skip", "Click or press any key to skip", 24f, Cream, FontStyles.Normal, Vector2.zero, new Vector2(600f, 40f));
        RectTransform hintRt = skipHint.rectTransform;
        hintRt.anchorMin = hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(1f, 0f);
        hintRt.anchoredPosition = new Vector2(-40f, 30f);
        skipHint.alignment = TextAlignmentOptions.BottomRight;
        skipHint.alpha = 0f;

        flash = new GameObject("Flash", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        Stretch(flash.rectTransform, root);
        flash.color = new Color(1f, 0.96f, 0.86f, 0f);
        flash.raycastTarget = false;

        black = new GameObject("Black", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        Stretch(black.rectTransform, root);
        black.color = Color.black;
        black.raycastTarget = false;
    }

    static void Stretch(RectTransform rt, RectTransform parent)
    {
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI Text(RectTransform parent, string name, string text, float size, Color color, FontStyles style, Vector2 pos, Vector2 box)
    {
        TextMeshProUGUI t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        RectTransform rt = t.rectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.sizeDelta = box;
        rt.anchoredPosition = pos;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    static void Shadow(TextMeshProUGUI t)
    {
        Material m = t.fontMaterial;   // an own copy, so other texts are not changed
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor("_UnderlayColor", new Color(0.04f, 0.09f, 0.06f, 0.75f));
        m.SetFloat("_UnderlayOffsetX", 0.6f);
        m.SetFloat("_UnderlayOffsetY", -0.6f);
        m.SetFloat("_UnderlaySoftness", 0.5f);
        t.fontMaterial = m;
    }

    // A dark-to-clear gradient for the top half of the screen
    static Texture2D TopShade()
    {
        const int h = 64;
        Texture2D tex = new Texture2D(1, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < h; y++)
        {
            float k = y / (h - 1f);                    // 0 at the bottom, 1 at the top
            float a = Mathf.SmoothStep(0f, 0.62f, (k - 0.35f) / 0.65f);
            tex.SetPixel(0, y, new Color(0.02f, 0.06f, 0.04f, a));
        }
        tex.Apply();
        return tex;
    }
}
