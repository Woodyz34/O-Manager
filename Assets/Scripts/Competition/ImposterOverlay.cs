using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The dark, flickering overlay used in imposter mode: the role intro when the competition starts,
// the small role reminder during the race, and the reveal at the end.
public class ImposterOverlay : MonoBehaviour
{
    public static ImposterOverlay Instance;

    [Header("Stage")]
    public GameObject stage;
    public CanvasGroup stageGroup;
    public Image vignette;
    public Image flash;
    public RectTransform shakeRoot;
    public TextMeshProUGUI smallText;
    public TextMeshProUGUI bigText;
    public TextMeshProUGUI verdictText;
    public TextMeshProUGUI detailText;

    [Header("Role reminder")]
    public GameObject roleChip;
    public TextMeshProUGUI roleChipText;

    [Header("Colours")]
    public Color boneColor = new Color(0.95f, 0.91f, 0.86f);
    public Color bloodColor = new Color(0.88f, 0.16f, 0.24f);
    public Color escapeColor = new Color(0.55f, 1f, 0.5f);

    const string GlitchCharacters = "#%&$@!?/|=+*^~";

    bool introPlayed;
    float pulseSpeed = 1f;
    float pulseStrength = 0.35f;

    public class RevealResult
    {
        public bool caught;
        public string detail;
    }

    public static ImposterOverlay GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }
        GameObject prefab = Resources.Load<GameObject>("UI/ImposterOverlay");
        if (prefab == null)
        {
            Debug.LogError("Missing prefab Resources/UI/ImposterOverlay");
            return null;
        }
        return Instantiate(prefab).GetComponent<ImposterOverlay>();
    }

    // Shown to the local player when a competition in imposter mode starts
    public static void ShowRoleIntro()
    {
        ImposterOverlay overlay = GetOrCreate();
        if (overlay != null && !overlay.introPlayed)
        {
            overlay.introPlayed = true;
            overlay.StartCoroutine(overlay.IntroRoutine());
        }
    }

    void Awake()
    {
        Instance = this;
        stage.SetActive(false);
        roleChip.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void HideRoleChip()
    {
        roleChip.SetActive(false);
    }

    void Update()
    {
        if (!stage.activeSelf)
        {
            return;
        }
        // A red heartbeat around the edges of the screen
        float beat = Mathf.Repeat(Time.unscaledTime * pulseSpeed, 1f);
        float pulse = Mathf.Exp(-beat * 9f) + 0.6f * Mathf.Exp(-Mathf.Abs(beat - 0.28f) * 14f);
        Color color = vignette.color;
        color.a = Mathf.Clamp01(0.35f + pulseStrength * pulse);
        vignette.color = color;
    }

    // ---- Role intro ----

    IEnumerator IntroRoutine()
    {
        // The host's choice of imposter may arrive a moment after the scene has loaded
        PlayerRef imposter = PlayerRef.None;
        float giveUp = Time.unscaledTime + 5f;
        while (!CompetitionFlow.TryGetImposter(out imposter) && Time.unscaledTime < giveUp)
        {
            yield return null;
        }
        NetworkPlayer local = NetworkPlayer.Local;
        if (imposter.IsNone || local == null || local.Object == null)
        {
            yield break;
        }
        bool amImposter = local.Object.StateAuthority == imposter;

        ResetStage();
        pulseSpeed = 0.9f;
        pulseStrength = amImposter ? 0.45f : 0.25f;
        smallText.text = "IMPOSTER MODE";
        bigText.text = amImposter ? "YOU ARE THE IMPOSTER" : "ONE OF YOU IS AN IMPOSTER";
        bigText.color = amImposter ? bloodColor : boneColor;
        detailText.text = amImposter
            ? "Finish last without getting caught."
            : "Someone will try to finish last. Remember who looks suspicious.";
        SetAlpha(bigText, 0f);
        SetAlpha(detailText, 0f);

        yield return Fade(stageGroup, 0f, 1f, 0.5f);
        yield return Flicker(smallText, 0.5f);
        StartCoroutine(Shake(0.35f, 14f));
        yield return SlamIn(bigText, 0.3f);
        yield return FadeText(detailText, 0f, 0.9f, 0.4f);
        yield return FlickerHold(bigText, 2.6f);
        yield return Fade(stageGroup, 1f, 0f, 0.6f);
        stage.SetActive(false);

        if (!CompetitionFlow.ResultsShown)
        {
            roleChipText.text = amImposter ? "You are the imposter: finish last" : "Imposter mode: who is trying to lose?";
            roleChip.SetActive(true);
        }
    }

    // ---- Reveal ----

    // Plays the reveal. "decide" is called at the moment of truth, so the last votes are counted.
    public IEnumerator PlayReveal(List<string> names, string imposterName, Func<RevealResult> decide)
    {
        HideRoleChip();
        ResetStage();
        pulseSpeed = 0.8f;
        pulseStrength = 0.3f;
        smallText.text = "THE IMPOSTER WAS";
        bigText.text = "";
        bigText.color = boneColor;

        yield return Fade(stageGroup, 0f, 1f, 0.6f);
        yield return Flicker(smallText, 0.7f);

        // Names race past, slower and slower, while the heartbeat speeds up
        SetAlpha(bigText, 1f);
        float duration = 3.2f;
        float elapsed = 0f;
        float nextSwap = 0f;
        int index = UnityEngine.Random.Range(0, Mathf.Max(1, names.Count));
        while (elapsed < duration)
        {
            float progress = elapsed / duration;
            pulseSpeed = Mathf.Lerp(0.9f, 2.4f, progress);
            pulseStrength = Mathf.Lerp(0.3f, 0.6f, progress);
            if (elapsed >= nextSwap)
            {
                index++;
                string shown = names.Count > 0 ? names[index % names.Count] : "???";
                bigText.text = Glitch(shown.ToUpperInvariant(), Mathf.Lerp(0.55f, 0.15f, progress));
                nextSwap = elapsed + Mathf.Lerp(0.05f, 0.32f, progress * progress);
            }
            SetAlpha(bigText, UnityEngine.Random.Range(0.6f, 1f));
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // The truth
        RevealResult result = decide != null ? decide() : new RevealResult { detail = "" };
        bigText.text = imposterName.ToUpperInvariant();
        bigText.color = bloodColor;
        pulseSpeed = 1.2f;
        pulseStrength = 0.65f;
        StartCoroutine(FlashRoutine(0.55f, 0.35f));
        StartCoroutine(Shake(0.5f, 26f));
        yield return SlamIn(bigText, 0.28f);
        yield return new WaitForSecondsRealtime(0.9f);

        verdictText.text = result.caught ? "CAUGHT!" : "GOT AWAY!";
        verdictText.color = result.caught ? bloodColor : escapeColor;
        StartCoroutine(Shake(0.3f, 12f));
        yield return SlamIn(verdictText, 0.25f);
        detailText.text = result.detail;
        yield return FadeText(detailText, 0f, 0.9f, 0.4f);
        yield return FlickerHold(bigText, 2.4f);

        yield return Fade(stageGroup, 1f, 0f, 0.7f);
        stage.SetActive(false);
    }

    // ---- Effects ----

    void ResetStage()
    {
        stage.SetActive(true);
        stageGroup.alpha = 0f;
        shakeRoot.anchoredPosition = Vector2.zero;
        smallText.color = boneColor;
        SetAlpha(smallText, 0f);
        bigText.transform.localScale = Vector3.one;
        SetAlpha(bigText, 0f);
        verdictText.text = "";
        verdictText.transform.localScale = Vector3.one;
        detailText.text = "";
        detailText.color = boneColor;
        SetAlpha(detailText, 0f);
        Color flashColor = flash.color;
        flashColor.a = 0f;
        flash.color = flashColor;
    }

    static void SetAlpha(Graphic graphic, float alpha)
    {
        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }

    static string Glitch(string text, float amount)
    {
        char[] characters = text.ToCharArray();
        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] != ' ' && UnityEngine.Random.value < amount)
            {
                characters[i] = GlitchCharacters[UnityEngine.Random.Range(0, GlitchCharacters.Length)];
            }
        }
        return new string(characters);
    }

    IEnumerator Fade(CanvasGroup group, float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        group.alpha = to;
    }

    IEnumerator FadeText(Graphic graphic, float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            SetAlpha(graphic, Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }
        SetAlpha(graphic, to);
    }

    // Comes on like a failing lamp
    IEnumerator Flicker(Graphic graphic, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            SetAlpha(graphic, UnityEngine.Random.value < 0.35f ? 0.15f : 0.9f);
            yield return null;
        }
        SetAlpha(graphic, 0.9f);
    }

    // Stays lit, with an occasional nervous flicker
    IEnumerator FlickerHold(Graphic graphic, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            SetAlpha(graphic, UnityEngine.Random.value < 0.06f ? 0.45f : 1f);
            yield return null;
        }
        SetAlpha(graphic, 1f);
    }

    IEnumerator SlamIn(Graphic graphic, float seconds)
    {
        SetAlpha(graphic, 1f);
        Transform target = graphic.transform;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float progress = t / seconds;
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            float scale = Mathf.Lerp(2.4f, 1f, eased);
            target.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }
        target.localScale = Vector3.one;
    }

    IEnumerator Shake(float seconds, float strength)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            shakeRoot.anchoredPosition = UnityEngine.Random.insideUnitCircle * strength * (1f - t / seconds);
            yield return null;
        }
        shakeRoot.anchoredPosition = Vector2.zero;
    }

    IEnumerator FlashRoutine(float startAlpha, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            SetAlpha(flash, Mathf.Lerp(startAlpha, 0f, t / seconds));
            yield return null;
        }
        SetAlpha(flash, 0f);
    }
}
