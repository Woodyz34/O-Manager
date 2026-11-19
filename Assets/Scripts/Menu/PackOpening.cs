using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The pack opening show. The pack flies in, shakes and bursts, and then the runner is revealed
// piece by piece: country, club, stats, photo, name and finally the card itself.
// The timings and sizes are all in this file; the objects it moves are in Resources/UI/PackOpening.prefab,
// which is built by Tools > O-Manager > Developer > Apply Menu UI.
public class PackOpening : MonoBehaviour
{
    public enum Exit { Continue, OpenAnother, Runners }
    enum Phase { Intro, Waiting, Charging, Walkout, Done, Closing }

    [Header("Stage")]
    public CanvasGroup stageGroup;
    public RectTransform shaker;
    public Image rays;
    public Image glow;
    public Image beamLeft;
    public Image beamRight;
    public Image flash;
    public Image pillar;
    public Image streak;
    public UIParticles sparks;

    [Header("Pack")]
    public RectTransform pack;
    public Image packBody;
    public Image packTop;
    public Image seamGlow;
    public Button packButton;
    public TextMeshProUGUI hint;

    [Header("Country")]
    public RectTransform flagGroup;
    public CanvasGroup flagCanvas;
    public Image flagImage;
    public TextMeshProUGUI countryText;

    [Header("Stats")]
    public RectTransform statsGroup;
    public CanvasGroup metaRow;
    public Image clubImage;
    public TextMeshProUGUI klassText;
    public CanvasGroup speedBlock;          // physics (forest speed); the name is kept for the saved scene
    public TextMeshProUGUI speedValue;
    public CanvasGroup sprintBlock;         // speed from the sprints, made from speedBlock when missing
    public TextMeshProUGUI sprintValue;
    public CanvasGroup techniqueBlock;
    public TextMeshProUGUI techniqueValue;
    public CanvasGroup overallBlock;
    public TextMeshProUGUI overallValue;
    public TextMeshProUGUI overallLabel;
    public Graphic[] tierTinted;

    [Header("Photo and name")]
    public RectTransform portraitGroup;
    public CanvasGroup portraitCanvas;
    public Image portrait;
    public Image portraitFrame;
    public Image scanLine;
    public TextMeshProUGUI nameText;

    [Header("Card")]
    public RectTransform cardHolder;
    public RunnerCardView card;
    public CanvasGroup cardFace;
    public Image cardBack;
    public Image cardRays;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI subtitleText;
    public CanvasGroup buttons;
    public Button continueButton;
    public Button anotherButton;
    public Button runnersButton;
    public Button skipButton;
    public TextMeshProUGUI anotherLabel;

    [Header("Sound")]
    public AudioSource sfx;
    public AudioSource loop;
    public AudioClip whoosh;
    public AudioClip riser;
    public AudioClip boom;
    public AudioClip hit;
    public AudioClip tick;
    public AudioClip reveal;
    public AudioClip shimmer;
    public AudioClip click;
    public AudioClip coin;
    [Range(0f, 1f)] public float volume = 0.8f;

    const float PackY = 10f;
    const float FlagY = 80f;
    const float PortraitY = 40f;
    const float NameY = -335f;
    const float CardScale = 1.1f;
    const float CardY = -14f;
    const float DroneVolume = 0.45f;
    // The pack opening was loud next to the rest of the game (2026-11-15: a bit lower). The prefab keeps its own
    // volume, so the lowering is here.
    const float Quieter = 0.6f;
    float Level => volume * Quieter;
    static readonly Vector2 ChipLeft = new Vector2(-700f, 405f);
    static readonly Vector2 ChipRight = new Vector2(650f, 405f);
    static readonly Vector2 ChipClub = new Vector2(-545f, 405f);   // next to the flag in the corner
    const float ClubSize = 300f;
    static readonly Color Neutral = new Color(1f, 0.93f, 0.78f, 1f);

    PackResult result;
    Action<Exit> onClosed;
    Phase phase;
    RunnerTier tier;
    Color tierColor;
    bool openRequested;
    float clock;
    float shake;
    float tickAt;

    // Lighting follows these targets smoothly, so each step only has to say what it wants
    Color lightColor = Neutral;
    Color lightTarget = Neutral;
    float raysAlpha, raysTarget, raysSpeed = 8f;
    float glowAlpha, glowTarget;
    float beamAlpha, beamTarget;
    float cardRaysAlpha, cardRaysTarget;
    Vector2 tilt;

    // The club's own moment comes right after the country. Made from the country's pieces the first
    // time it is needed, so the prefab needs no rebuild.
    RectTransform clubGroup;
    CanvasGroup clubCanvas;
    Image clubBadge;
    TextMeshProUGUI clubTitle;

    void Awake()
    {
        packButton.onClick.AddListener(RequestOpen);
        skipButton.onClick.AddListener(Skip);
        continueButton.onClick.AddListener(() => Close(Exit.Continue));
        anotherButton.onClick.AddListener(() => Close(Exit.OpenAnother));
        runnersButton.onClick.AddListener(() => Close(Exit.Runners));
    }

    // Called once when the player opens the pack (clicks it). The shop pays for the pack and adds the runner then.
    public Action Opened;

    public void Play(PackResult packResult, Action<Exit> closed)
    {
        result = packResult;
        onClosed = closed;
        tier = RunnerCards.Tier(result.card);
        tierColor = RunnerCards.TierColor(tier);
        card.Show(result.card);
        Prepare();
        StartCoroutine(Sequence());
    }

    // "Open packs" (RunnerRules / OpenPacks.cs): a free pack in a competition. The runner is only on loan, so the
    // card says so and the only button goes on (to the next pack or to placing the runners).
    string loanTitle;
    string loanButton;

    public void PlayLoan(CardData runner, string title, string buttonText, Action closed)
    {
        loanTitle = title;
        loanButton = buttonText;
        // The competition has its own screens (overlay canvases) above any camera: the show goes over them all
        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;
        }
        Play(new PackResult { card = runner, isNew = false, paid = 0 }, exit => closed?.Invoke());
    }

    // ---------- the show ----------

    void Prepare()
    {
        stageGroup.alpha = 0f;
        flash.color = UIAnim.WithAlpha(Color.white, 0f);
        pillar.gameObject.SetActive(false);
        streak.gameObject.SetActive(false);
        seamGlow.color = UIAnim.WithAlpha(tierColor, 0f);
        hint.alpha = 0f;

        packButton.interactable = false;
        Place(pack, new Vector2(0, -950f), 0.45f);
        HideWalkout();

        cardHolder.gameObject.SetActive(false);
        cardRays.color = UIAnim.WithAlpha(tierColor, 0f);
        titleText.alpha = 0f;
        subtitleText.alpha = 0f;
        buttons.alpha = 0f;
        buttons.interactable = false;
        buttons.blocksRaycasts = false;
        skipButton.gameObject.SetActive(false);
        if (moneyPill != null) moneyPill.SetActive(false);

        foreach (Graphic graphic in tierTinted)
        {
            if (graphic != null) graphic.color = UIAnim.WithAlpha(tierColor, graphic.color.a);
        }
    }

    void HideWalkout()
    {
        flagGroup.gameObject.SetActive(false);
        countryText.alpha = 0f;
        if (clubGroup != null) clubGroup.gameObject.SetActive(false);
        if (clubTitle != null) clubTitle.alpha = 0f;
        statsGroup.gameObject.SetActive(false);
        metaRow.alpha = 0f;
        MakeSprintBlock();
        speedBlock.alpha = 0f;
        if (sprintBlock != null) sprintBlock.alpha = 0f;
        techniqueBlock.alpha = 0f;
        overallBlock.alpha = 0f;
        portraitGroup.gameObject.SetActive(false);
        scanLine.color = UIAnim.WithAlpha(Color.white, 0f);
        nameText.alpha = 0f;
    }

    IEnumerator Sequence()
    {
        // The pack flies in
        phase = Phase.Intro;
        loop.volume = 0f;
        loop.Play();
        Sound(whoosh, 0.8f);
        lightTarget = Neutral;
        glowTarget = 0.32f;
        raysTarget = 0.10f;
        yield return UIAnim.Run(1.1f, t =>
        {
            stageGroup.alpha = Mathf.Clamp01(t * 3f);
            loop.volume = t * DroneVolume * Level;
            float e = UIAnim.OutCubic(t);
            Place(pack, new Vector2(0, Mathf.Lerp(-950f, PackY, e)), Mathf.Lerp(0.45f, 1f, e), Mathf.Lerp(540f, 0f, e), Mathf.Lerp(-25f, 0f, e));
        });

        // It waits for the player
        phase = Phase.Waiting;
        packButton.interactable = true;
        while (!openRequested)
        {
            yield return null;
        }
        packButton.interactable = false;
        Opened?.Invoke();

        yield return Charge();
        yield return Burst();
        yield return RevealCountry();
        yield return RevealClub();
        yield return RevealStats();
        yield return RevealPortrait();
        yield return RevealName();
        yield return Finale(false);
    }

    // The pack shakes harder and harder while light in the runner's tier colour leaks out
    IEnumerator Charge()
    {
        phase = Phase.Charging;
        skipButton.gameObject.SetActive(true);
        Sound(riser, 0.9f);
        float leak = 0f;
        yield return UIAnim.Run(2.05f, t =>
        {
            float p = t * t;
            shake = 2f + 20f * p;
            lightTarget = Color.Lerp(Neutral, tierColor, t);
            glowTarget = Mathf.Lerp(0.32f, 0.95f, t);
            raysTarget = Mathf.Lerp(0.10f, 0.55f, t);
            raysSpeed = Mathf.Lerp(8f, 150f, p);
            Place(pack, new Vector2(0, PackY), 1f + 0.1f * t, Mathf.Sin(clock * 31f) * 5f * t, Mathf.Sin(clock * 47f) * 3.5f * t);
            seamGlow.color = UIAnim.WithAlpha(tierColor, t);
            seamGlow.rectTransform.localScale = new Vector3(0.3f + 1.0f * t, 0.5f + 1.3f * t, 1f);
            float zoom = 1f + 0.06f * t;
            shaker.localScale = new Vector3(zoom, zoom, 1f);

            leak += (5f + 80f * p) * Time.unscaledDeltaTime;
            int due = Mathf.FloorToInt(leak);
            if (due > 0)
            {
                leak -= due;
                sparks.Emit(new ParticleBurst
                {
                    count = due, origin = new Vector2(0, PackY + 290f), spread = new Vector2(190f, 6f),
                    angleMin = 55f, angleMax = 125f, speedMin = 200f, speedMax = 620f,
                    sizeMin = 12f, sizeMax = 34f, endSize = 0.2f, lifeMin = 0.4f, lifeMax = 1.0f,
                    gravity = 420f, drag = 0.8f, spin = 200f, twinkle = 0.5f,
                    colorA = tierColor, colorB = Color.white
                });
            }
        });
    }

    // The top tears off and everything goes white
    IEnumerator Burst()
    {
        phase = Phase.Walkout;
        Sound(boom, 1f);
        shake = 48f;
        shaker.localScale = Vector3.one;
        flash.color = Color.white;
        lightTarget = tierColor;
        beamTarget = 0.45f;
        raysTarget = 0.16f;
        raysSpeed = 12f;
        glowTarget = 0.4f;
        sparks.Emit(Radial(new Vector2(0, PackY + 200f), 230, 300f, 1600f));

        packTop.rectTransform.SetParent(shaker, true);
        Vector2 topStart = packTop.rectTransform.anchoredPosition;
        pillar.gameObject.SetActive(true);
        yield return UIAnim.Run(0.9f, t =>
        {
            flash.color = UIAnim.WithAlpha(Color.white, Fade(t * 2.4f));
            packTop.rectTransform.anchoredPosition = topStart + new Vector2(620f * t, 1000f * t - 560f * t * t);
            packTop.rectTransform.localRotation = Quaternion.Euler(0, 0, -330f * t);
            packTop.color = UIAnim.WithAlpha(Color.white, 1f - t * t);

            float drop = UIAnim.InCubic(Mathf.Clamp01(t * 1.3f));
            Place(pack, new Vector2(0, PackY - 1300f * drop), 1.1f - 0.2f * drop);
            packBody.color = UIAnim.WithAlpha(Color.white, 1f - drop);
            seamGlow.color = UIAnim.WithAlpha(tierColor, 1f - t);

            pillar.color = UIAnim.WithAlpha(tierColor, 1f - t);
            pillar.rectTransform.localScale = new Vector3(0.5f + 2.2f * t, 1f + 0.6f * t, 1f);
        });
        pack.gameObject.SetActive(false);
        packTop.gameObject.SetActive(false);
        pillar.gameObject.SetActive(false);
        yield return UIAnim.Wait(0.25f);
    }

    IEnumerator RevealCountry()
    {
        CardData runner = result.card;
        if (runner.country == null)
        {
            // No flag picture for this country: show only the country's name
            if (string.IsNullOrEmpty(runner.countryName))
            {
                yield break;
            }
            countryText.text = RunnerCards.CountryName(runner);
            yield return UIAnim.Run(0.6f, t =>
            {
                countryText.alpha = t;
                countryText.characterSpacing = Mathf.Lerp(70f, 22f, UIAnim.OutCubic(t));
            });
            yield return UIAnim.Wait(0.85f);
            yield return UIAnim.Run(0.45f, t => countryText.alpha = 1f - t);
            yield break;
        }
        Rect rect = runner.country.rect;
        float height = 300f;
        float width = Mathf.Clamp(height * rect.width / rect.height, 300f, 620f);
        flagGroup.sizeDelta = new Vector2(width + 16f, height + 16f);
        flagImage.sprite = runner.country;
        countryText.text = RunnerCards.CountryName(runner);
        flagGroup.gameObject.SetActive(true);

        Hit(0.9f, 12f);
        Flare(new Vector2(0, FlagY));
        yield return UIAnim.Run(0.5f, t =>
        {
            float e = UIAnim.OutCubic(t);
            flagCanvas.alpha = Mathf.Clamp01(t * 2.5f);
            Place(flagGroup, new Vector2(0, FlagY), Mathf.Lerp(2.6f, 1f, e), Mathf.Lerp(-75f, 0f, e));
        });
        yield return UIAnim.Run(0.6f, t =>
        {
            countryText.alpha = t;
            countryText.characterSpacing = Mathf.Lerp(70f, 22f, UIAnim.OutCubic(t));
        });
        yield return UIAnim.Wait(0.85f);
        yield return UIAnim.Run(0.45f, t =>
        {
            float e = UIAnim.InOutCubic(t);
            Place(flagGroup, Vector2.Lerp(new Vector2(0, FlagY), ChipLeft, e), Mathf.Lerp(1f, 0.3f, e));
            countryText.alpha = 1f - t;
        });
    }

    void MakeClubPieces()
    {
        if (clubGroup != null) return;
        clubGroup = Instantiate(flagGroup.gameObject, flagGroup.parent).GetComponent<RectTransform>();
        clubGroup.name = "Club";
        clubGroup.SetSiblingIndex(flagGroup.GetSiblingIndex() + 1);
        clubCanvas = clubGroup.GetComponent<CanvasGroup>();
        clubBadge = clubGroup.Find("Picture").GetComponent<Image>();
        clubBadge.preserveAspect = true;
        clubBadge.rectTransform.offsetMin = Vector2.zero;
        clubBadge.rectTransform.offsetMax = Vector2.zero;
        // Badges have their own shape, so no cream frame behind them like the flag has
        Transform border = clubGroup.Find("Border");
        if (border != null) border.gameObject.SetActive(false);
        clubGroup.sizeDelta = new Vector2(ClubSize, ClubSize);

        clubTitle = Instantiate(countryText, countryText.transform.parent);
        clubTitle.name = "Club name";
        clubTitle.transform.SetSiblingIndex(countryText.transform.GetSiblingIndex() + 1);
        clubTitle.enableAutoSizing = true;
        clubTitle.fontSizeMax = countryText.fontSize;
        clubTitle.fontSizeMin = 40f;
        clubTitle.textWrappingMode = TextWrappingModes.NoWrap;
        clubTitle.alpha = 0f;
        clubGroup.gameObject.SetActive(false);
    }

    // The club comes in the same way as the country: the badge lands, the name is written under it,
    // and then the badge moves to the corner next to the flag
    IEnumerator RevealClub()
    {
        CardData runner = result.card;
        if (string.IsNullOrEmpty(runner.clubName) && runner.clubb == null)
        {
            yield break;
        }
        MakeClubPieces();
        clubTitle.text = Emblems.NoBreakAtDash(runner.clubName ?? "");
        clubTitle.characterSpacing = 70f;
        bool badge = runner.clubb != null;
        if (badge)
        {
            clubBadge.sprite = runner.clubb;
            clubCanvas.alpha = 0f;
            Place(clubGroup, new Vector2(0, FlagY), 2.6f, 75f);
            clubGroup.gameObject.SetActive(true);
            Hit(0.95f, 12f);
            Flare(new Vector2(0, FlagY));
            yield return UIAnim.Run(0.5f, t =>
            {
                float e = UIAnim.OutCubic(t);
                clubCanvas.alpha = Mathf.Clamp01(t * 2.5f);
                Place(clubGroup, new Vector2(0, FlagY), Mathf.Lerp(2.6f, 1f, e), Mathf.Lerp(75f, 0f, e));
            });
        }
        yield return UIAnim.Run(0.6f, t =>
        {
            clubTitle.alpha = t;
            clubTitle.characterSpacing = Mathf.Lerp(70f, 22f, UIAnim.OutCubic(t));
        });
        yield return UIAnim.Wait(0.85f);
        yield return UIAnim.Run(0.45f, t =>
        {
            float e = UIAnim.InOutCubic(t);
            if (badge) Place(clubGroup, Vector2.Lerp(new Vector2(0, FlagY), ChipClub, e), Mathf.Lerp(1f, 0.3f, e));
            clubTitle.alpha = 1f - t;
        });
    }

    IEnumerator RevealStats()
    {
        CardData runner = result.card;
        // The club has had its own moment already, so this row shows only the class, in the middle
        clubImage.enabled = false;
        klassText.rectTransform.anchoredPosition = Vector2.zero;
        klassText.alignment = TextAlignmentOptions.Center;
        klassText.text = runner.klass;
        speedValue.text = "0";
        if (sprintValue != null) sprintValue.text = "0";
        techniqueValue.text = "0";
        overallValue.text = RunnerCards.Overall(runner).ToString();
        overallLabel.text = "OVERALL";
        overallLabel.rectTransform.localScale = Vector3.one;
        Place(statsGroup, Vector2.zero, 1f);
        statsGroup.gameObject.SetActive(true);

        Sound(whoosh, 0.35f);
        yield return UIAnim.Run(0.35f, t => metaRow.alpha = t);
        yield return UIAnim.Wait(0.1f);
        yield return CountUp(speedBlock, speedValue, Mathf.RoundToInt(runner.physics), 1.0f);
        if (sprintBlock != null)
        {
            if (runner.HasSprintSpeed) yield return CountUp(sprintBlock, sprintValue, Mathf.RoundToInt(runner.sprintSpeed), 1.03f);
            else
            {
                // Never ran a sprint: a dash, and the race uses physics on hard ground too
                sprintValue.text = "-";
                yield return UIAnim.Run(0.3f, t => sprintBlock.alpha = t);
            }
        }
        yield return CountUp(techniqueBlock, techniqueValue, Mathf.RoundToInt(runner.technique), 1.06f);
        yield return UIAnim.Wait(0.35f);

        // The overall rating tells you how good the runner is, so it lands hardest
        Hit(1.12f, 24f);
        Flare(Vector2.zero);
        sparks.Emit(Radial(Vector2.zero, 50, 200f, 800f));
        RectTransform overall = (RectTransform)overallBlock.transform;
        yield return UIAnim.Run(0.4f, t =>
        {
            overallBlock.alpha = Mathf.Clamp01(t * 3f);
            float s = Mathf.Lerp(3f, 1f, UIAnim.OutCubic(t));
            overall.localScale = new Vector3(s, s, 1f);
        });
        if (tier == RunnerTier.Elite)
        {
            // The best runners get one more beat
            yield return UIAnim.Wait(0.45f);
            overallLabel.text = "ELITE";
            Sound(boom, 0.6f);
            Hit(1.26f, 30f);
            Flare(new Vector2(0, -170f));
            sparks.Emit(Radial(Vector2.zero, 110, 300f, 1300f));
            glowTarget = 0.75f;
            yield return UIAnim.Run(0.3f, t =>
            {
                float s = Mathf.Lerp(2.2f, 1.25f, UIAnim.OutCubic(t));
                overallLabel.rectTransform.localScale = new Vector3(s, s, 1f);
            });
            yield return UIAnim.Wait(0.4f);
            glowTarget = 0.4f;
        }
        yield return UIAnim.Wait(0.9f);
        yield return UIAnim.Run(0.45f, t =>
        {
            float e = UIAnim.InOutCubic(t);
            Place(statsGroup, Vector2.Lerp(Vector2.zero, ChipRight, e), Mathf.Lerp(1f, 0.34f, e));
        });
    }

    // The left side shows two stats since 2026-11-13: PHYSICS above and SPEED (sprint) below, a little smaller.
    // Made here from the physics block so the saved scene needs no rebuild.
    void MakeSprintBlock()
    {
        if (sprintBlock != null || speedBlock == null) return;
        RectTransform physicsRect = (RectTransform)speedBlock.transform;
        sprintBlock = Instantiate(speedBlock, physicsRect.parent);
        sprintBlock.name = "Sprint speed";
        RectTransform sprintRect = (RectTransform)sprintBlock.transform;
        Vector2 at = physicsRect.anchoredPosition;
        physicsRect.anchoredPosition = new Vector2(at.x, at.y + 115f);
        sprintRect.anchoredPosition = new Vector2(at.x, at.y - 145f);
        foreach (RectTransform block in new[] { physicsRect, sprintRect })
        {
            TextMeshProUGUI[] texts = block.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI text in texts)
            {
                bool isValue = text.name == "Value";
                if (isValue)
                {
                    text.fontSize = 120f;
                    text.rectTransform.anchoredPosition = new Vector2(0f, 20f);
                    text.rectTransform.sizeDelta = new Vector2(text.rectTransform.sizeDelta.x, 140f);
                }
                else
                {
                    text.fontSize = 26f;
                    text.rectTransform.anchoredPosition = new Vector2(0f, -82f);
                    text.text = block == physicsRect ? "PHYSICS" : "SPEED";
                }
                if (isValue && block == sprintRect) sprintValue = text;
            }
        }
    }

    IEnumerator CountUp(CanvasGroup block, TextMeshProUGUI value, int target, float pitch)
    {
        RectTransform rect = (RectTransform)block.transform;
        rect.localScale = Vector3.one;
        int shown = -1;
        yield return UIAnim.Run(0.62f, t =>
        {
            block.alpha = Mathf.Clamp01(t * 6f);
            int number = Mathf.RoundToInt(Mathf.Lerp(0f, target, UIAnim.OutCubic(t)));
            if (number != shown)
            {
                shown = number;
                value.text = number.ToString();
                if (clock - tickAt > 0.045f)
                {
                    tickAt = clock;
                    Sound(tick, 0.5f);
                }
            }
        });
        value.text = target.ToString();
        Hit(pitch, 9f);
        yield return UIAnim.Run(0.22f, t =>
        {
            float s = Mathf.Lerp(1.28f, 1f, UIAnim.OutCubic(t));
            rect.localScale = new Vector3(s, s, 1f);
        });
        yield return UIAnim.Wait(0.2f);
    }

    // The photo is uncovered from the bottom, so the face comes last
    IEnumerator RevealPortrait()
    {
        CardData runner = result.card;
        if (runner.RunnerPicture == null)
        {
            yield break;
        }
        Rect rect = runner.RunnerPicture.rect;
        float height = 600f;
        float width = Mathf.Min(520f, height * rect.width / rect.height);
        portraitGroup.sizeDelta = new Vector2(width, height);
        portrait.sprite = runner.RunnerPicture;
        portrait.fillAmount = 0f;
        portraitFrame.color = tierColor;
        scanLine.rectTransform.sizeDelta = new Vector2(width * 2.2f, 90f);
        Place(portraitGroup, new Vector2(0, PortraitY), 0.9f);
        portraitGroup.gameObject.SetActive(true);

        Sound(whoosh, 0.45f);
        glowTarget = 0.22f;
        raysTarget = 0.10f;
        yield return UIAnim.Run(0.3f, t =>
        {
            portraitCanvas.alpha = t;
            Place(portraitGroup, new Vector2(0, PortraitY), Mathf.Lerp(0.9f, 1f, UIAnim.OutCubic(t)));
        });
        yield return UIAnim.Run(1.05f, t => Uncover(Mathf.Lerp(0f, 0.5f, UIAnim.OutCubic(t)), height, 1f));
        yield return UIAnim.Wait(0.38f);

        Hit(1.2f, 20f);
        glowTarget = 0.55f;
        raysTarget = 0.22f;
        sparks.Emit(Radial(new Vector2(0, PortraitY), 90, 250f, 1100f));
        yield return UIAnim.Run(0.22f, t => Uncover(Mathf.Lerp(0.5f, 1f, t), height, 1f));
        yield return UIAnim.Run(0.4f, t =>
        {
            flash.color = UIAnim.WithAlpha(Color.white, 0.5f * Fade(t * 1.3f));
            scanLine.color = UIAnim.WithAlpha(Color.white, 1f - t);
        });
        yield return UIAnim.Wait(0.6f);
    }

    void Uncover(float amount, float height, float lineAlpha)
    {
        portrait.fillAmount = amount;
        scanLine.rectTransform.anchoredPosition = new Vector2(0, -height / 2f + amount * height);
        scanLine.color = UIAnim.WithAlpha(Color.white, lineAlpha);
    }

    IEnumerator RevealName()
    {
        nameText.text = result.card.RunnerName != null ? result.card.RunnerName.ToUpperInvariant() : "";
        Vector2 from = portraitGroup.anchoredPosition;
        yield return UIAnim.Run(0.25f, t => portraitGroup.anchoredPosition = from + new Vector2(0, 60f * UIAnim.OutCubic(t)));

        Hit(1.3f, 26f);
        Flare(new Vector2(0, NameY));
        yield return UIAnim.Run(0.3f, t =>
        {
            float e = UIAnim.OutCubic(t);
            nameText.alpha = Mathf.Clamp01(t * 3f);
            float s = Mathf.Lerp(2.8f, 1f, e);
            nameText.rectTransform.localScale = new Vector3(s, s, 1f);
            nameText.characterSpacing = Mathf.Lerp(40f, 6f, e);
        });
        yield return UIAnim.Wait(1.0f);
    }

    // Everything comes together as the runner's card
    IEnumerator Finale(bool skipped)
    {
        skipButton.gameObject.SetActive(false);
        if (!skipped)
        {
            Vector2 flagFrom = flagGroup.anchoredPosition, statsFrom = statsGroup.anchoredPosition, portraitFrom = portraitGroup.anchoredPosition;
            Vector2 clubFrom = clubGroup != null ? clubGroup.anchoredPosition : Vector2.zero;
            yield return UIAnim.Run(0.22f, t =>
            {
                float e = UIAnim.InCubic(t);
                Place(flagGroup, Vector2.Lerp(flagFrom, Vector2.zero, e), 0.3f * (1f - e));
                if (clubGroup != null) Place(clubGroup, Vector2.Lerp(clubFrom, Vector2.zero, e), 0.3f * (1f - e));
                Place(statsGroup, Vector2.Lerp(statsFrom, Vector2.zero, e), 0.34f * (1f - e));
                Place(portraitGroup, Vector2.Lerp(portraitFrom, Vector2.zero, e), 1f - e);
                nameText.alpha = 1f - t;
                flash.color = UIAnim.WithAlpha(Color.white, t);
            });
        }
        HideWalkout();
        pack.gameObject.SetActive(false);
        packTop.gameObject.SetActive(false);
        pillar.gameObject.SetActive(false);
        shaker.localScale = Vector3.one;
        stageGroup.alpha = 1f;
        loop.volume = DroneVolume * Level * 0.6f;

        phase = Phase.Done;
        Sound(reveal, 1f);
        if (tier == RunnerTier.Elite) Sound(boom, 0.7f);
        lightTarget = tierColor;
        beamTarget = 0.45f;
        raysTarget = 0f;
        glowTarget = 0.45f;
        cardRaysTarget = 0.4f;
        cardHolder.gameObject.SetActive(true);
        card.SetGlow(0f);

        yield return UIAnim.Run(1.15f, t =>
        {
            float e = UIAnim.OutCubic(t);
            float angle = Mathf.Lerp(900f, 0f, e);
            Place(cardHolder, new Vector2(0, Mathf.Lerp(-280f, CardY, e)), Mathf.Lerp(0.25f, CardScale, e), angle);
            ShowCardSide(Mathf.Cos(angle * Mathf.Deg2Rad) > 0f);
            flash.color = UIAnim.WithAlpha(Color.white, 0.95f * Fade(t * 3.2f));
        });
        ShowCardSide(true);
        flash.color = UIAnim.WithAlpha(Color.white, 0f);

        shake = 14f;
        Sound(shimmer, 0.8f);
        card.PlayShine();
        card.shineEvery = 3.2f;
        card.SetGlow(0.55f);
        int confetti = tier == RunnerTier.Elite ? 220 : tier == RunnerTier.Gold ? 140 : 80;
        sparks.Emit(Radial(Vector2.zero, confetti, 300f, 1400f));
        if (tier >= RunnerTier.Gold)
        {
            // glitter keeps falling around the best cards
            sparks.stream = new ParticleBurst
            {
                origin = new Vector2(0, 80f), spread = new Vector2(420f, 420f), angleMin = 0f, angleMax = 360f,
                speedMin = 10f, speedMax = 70f, sizeMin = 10f, sizeMax = 30f, endSize = 0.3f,
                lifeMin = 0.7f, lifeMax = 1.6f, gravity = 30f, drag = 0.5f, spin = 120f, twinkle = 0.8f,
                colorA = tierColor, colorB = Color.white
            };
            sparks.streamRate = tier == RunnerTier.Elite ? 26f : 12f;
        }

        if (loanTitle != null)
        {
            titleText.text = loanTitle;
            subtitleText.text = RunnerCards.TierName(tier) + "   <color=#FBF4DA>OVERALL " + RunnerCards.Overall(result.card) + "   ON LOAN</color>";
        }
        else if (result.isNew)
        {
            titleText.text = tier == RunnerTier.Elite ? "ELITE RUNNER!" : "NEW RUNNER!";
            subtitleText.text = RunnerCards.TierName(tier) + "   <color=#FBF4DA>OVERALL " + RunnerCards.Overall(result.card) + "</color>";
        }
        else
        {
            // Packs only hold runners the player is missing, so this is only seen with an old save
            titleText.text = "RUNNER";
            subtitleText.text = RunnerCards.TierName(tier) + "   <color=#FBF4DA>OVERALL " + RunnerCards.Overall(result.card) + "</color>";
        }
        subtitleText.color = tierColor;
        yield return UIAnim.Run(0.4f, t =>
        {
            titleText.alpha = t;
            subtitleText.alpha = t;
            titleText.characterSpacing = Mathf.Lerp(40f, 12f, UIAnim.OutCubic(t));
        });
        if (loanTitle != null)
        {
            anotherButton.gameObject.SetActive(false);
            runnersButton.gameObject.SetActive(false);
            TextMeshProUGUI label = continueButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null && loanButton != null)
            {
                label.text = loanButton;
                // Long texts like "Place the runners" must fit: wider button and a text that shrinks if needed
                RectTransform buttonRect = (RectTransform)continueButton.transform;
                buttonRect.sizeDelta = new Vector2(Mathf.Max(buttonRect.sizeDelta.x, 520f), Mathf.Max(buttonRect.sizeDelta.y, 96f));
                label.enableWordWrapping = false;
                label.enableAutoSizing = true;
                label.fontSizeMin = 18f;
                label.fontSizeMax = Mathf.Max(label.fontSize, 40f);
            }
        }
        else
        {
            int price = PackShop.CurrentPrice();
            anotherButton.gameObject.SetActive(PackShop.CanBuy());
            anotherLabel.text = price == 0 ? "Open free pack" : "Open another (" + price + " kr)";
            ShowMoney();
        }
        yield return UIAnim.Wait(0.3f);
        buttons.blocksRaycasts = true;
        buttons.interactable = true;
        yield return UIAnim.Run(0.35f, t => buttons.alpha = t);
    }

    // Your money in the top right corner while you choose to open another pack or go to My Runners (2026-11-19), in
    // the same pill as on the other pages
    GameObject moneyPill;

    void ShowMoney()
    {
        if (moneyPill == null)
        {
            Canvas canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null) return;
            Image pill = CourseUI.Panel("Money pill", canvas.transform, CourseUI.Card);
            if (CourseUI.Skin != null && CourseUI.Skin.pill != null) pill.sprite = CourseUI.Skin.pill;
            CourseUI.Place(pill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48, -40), new Vector2(220, 66));
            TextMeshProUGUI amount = CourseUI.Text("Amount", pill.transform, "", 26, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            CourseUI.Stretch(amount.rectTransform, 12, 12, 0, 0);
            pill.gameObject.SetActive(false);
            MoneyLabel label = pill.gameObject.AddComponent<MoneyLabel>();
            label.text = amount;
            moneyPill = pill.gameObject;
        }
        moneyPill.transform.SetAsLastSibling();
        moneyPill.SetActive(true);
    }

    void ShowCardSide(bool faceUp)
    {
        cardFace.alpha = faceUp ? 1f : 0f;
        cardBack.enabled = !faceUp;
    }

    // ---------- input ----------

    void RequestOpen()
    {
        if (phase == Phase.Waiting)
        {
            openRequested = true;
            Sound(click, 0.7f);
        }
    }

    // Jumps straight to the finished card
    public void Skip()
    {
        if (phase != Phase.Charging && phase != Phase.Walkout)
        {
            return;
        }
        StopAllCoroutines();
        sfx.Stop();
        shake = 0f;
        streak.gameObject.SetActive(false);
        StartCoroutine(Finale(true));
    }

    void Close(Exit exit)
    {
        if (phase != Phase.Done || !buttons.interactable)
        {
            return;
        }
        phase = Phase.Closing;
        buttons.interactable = false;
        Sound(click, 0.7f);
        StartCoroutine(CloseRoutine(exit));
    }

    IEnumerator CloseRoutine(Exit exit)
    {
        float startVolume = loop.volume;
        yield return UIAnim.Run(0.25f, t =>
        {
            stageGroup.alpha = 1f - t;
            loop.volume = startVolume * (1f - t);
        });
        loop.Stop();
        if (onClosed != null)
        {
            onClosed(exit);
        }
    }

    // ---------- every frame ----------

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        clock += dt;
        float k = 1f - Mathf.Exp(-6f * dt);

        lightColor = Color.Lerp(lightColor, lightTarget, k);
        raysAlpha = Mathf.Lerp(raysAlpha, raysTarget, k);
        glowAlpha = Mathf.Lerp(glowAlpha, glowTarget, k);
        beamAlpha = Mathf.Lerp(beamAlpha, beamTarget, k);
        cardRaysAlpha = Mathf.Lerp(cardRaysAlpha, cardRaysTarget, k);

        rays.rectTransform.Rotate(0, 0, raysSpeed * dt);
        rays.color = UIAnim.WithAlpha(lightColor, raysAlpha);
        glow.color = UIAnim.WithAlpha(lightColor, glowAlpha * (0.85f + 0.15f * Mathf.Sin(clock * 2.2f)));
        beamLeft.color = UIAnim.WithAlpha(lightColor, beamAlpha);
        beamRight.color = UIAnim.WithAlpha(lightColor, beamAlpha);
        beamLeft.rectTransform.localRotation = Quaternion.Euler(0, 0, 30f + Mathf.Sin(clock * 0.7f) * 9f);
        beamRight.rectTransform.localRotation = Quaternion.Euler(0, 0, -30f + Mathf.Sin(clock * 0.6f + 1.3f) * 9f);
        cardRays.rectTransform.Rotate(0, 0, -11f * dt);
        cardRays.color = UIAnim.WithAlpha(tierColor, cardRaysAlpha);

        shaker.anchoredPosition = shake > 0.05f ? UnityEngine.Random.insideUnitCircle * shake : Vector2.zero;
        if (phase != Phase.Charging)
        {
            shake = Mathf.Lerp(shake, 0f, 1f - Mathf.Exp(-7f * dt));
        }

        float hintWanted = phase == Phase.Waiting ? 0.65f + 0.35f * Mathf.Sin(clock * 3.5f) : 0f;
        hint.alpha = Mathf.Lerp(hint.alpha, hintWanted, 1f - Mathf.Exp(-10f * dt));

        if (phase == Phase.Waiting)
        {
            Place(pack, new Vector2(0, PackY + Mathf.Sin(clock * 1.6f) * 10f), 1f, Mathf.Sin(clock * 0.9f) * 11f, Mathf.Sin(clock * 1.3f) * 1.5f);
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                UISounds.Click();
                RequestOpen();
            }
        }
        else if (phase == Phase.Charging || phase == Phase.Walkout)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Skip();
            }
        }
        else if (phase == Phase.Done && buttons.interactable)
        {
            // The finished card floats and leans towards the mouse
            Vector2 mouse = Input.mousePosition;
            Vector2 lean = new Vector2(
                Mathf.Clamp((mouse.x / Mathf.Max(1, Screen.width) - 0.5f) * 2f, -1f, 1f),
                Mathf.Clamp((mouse.y / Mathf.Max(1, Screen.height) - 0.5f) * 2f, -1f, 1f));
            tilt = Vector2.Lerp(tilt, lean, 1f - Mathf.Exp(-5f * dt));
            cardHolder.anchoredPosition = new Vector2(0, CardY + Mathf.Sin(clock * 1.4f) * 7f);
            cardHolder.localRotation = Quaternion.Euler(-tilt.y * 7f, tilt.x * 11f + Mathf.Sin(clock * 0.8f) * 3f, 0);
        }
    }

    // ---------- helpers ----------

    // How strong a flash is as it dies away, from 1 at t = 0 to 0 at t = 1. It drops quickly,
    // because a half-transparent white still looks bright.
    static float Fade(float t)
    {
        float left = 1f - Mathf.Clamp01(t);
        return left * left * left * left;
    }

    static void Place(RectTransform rect, Vector2 position, float scale, float turnY = 0f, float turnZ = 0f)
    {
        rect.anchoredPosition = position;
        rect.localScale = new Vector3(scale, scale, scale);
        rect.localRotation = Quaternion.Euler(0, turnY, turnZ);
    }

    ParticleBurst Radial(Vector2 origin, int count, float speedMin, float speedMax)
    {
        return new ParticleBurst
        {
            count = count, origin = origin, spread = new Vector2(30f, 30f), angleMin = 0f, angleMax = 360f,
            speedMin = speedMin, speedMax = speedMax, sizeMin = 18f, sizeMax = 72f, endSize = 0.2f,
            lifeMin = 0.6f, lifeMax = 1.7f, gravity = 380f, drag = 1.5f, spin = 220f, twinkle = 0.5f,
            colorA = tierColor, colorB = Color.white
        };
    }

    // A wide streak of light across whatever was just revealed
    void Flare(Vector2 position)
    {
        StartCoroutine(FlareRoutine(position));
    }

    IEnumerator FlareRoutine(Vector2 position)
    {
        streak.gameObject.SetActive(true);
        streak.rectTransform.anchoredPosition = position;
        yield return UIAnim.Run(0.55f, t =>
        {
            streak.color = UIAnim.WithAlpha(Color.Lerp(Color.white, tierColor, t), 1f - t);
            streak.rectTransform.localScale = new Vector3(0.4f + 1.9f * UIAnim.OutCubic(t), 1f + 1.5f * (1f - t), 1f);
        });
        streak.gameObject.SetActive(false);
    }

    void Hit(float pitch, float shakeAmount)
    {
        shake = Mathf.Max(shake, shakeAmount);
        if (hit == null) return;
        // Each hit gets its own source, so a new pitch does not bend the tail of the one before
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.clip = hit;
        source.pitch = pitch;
        source.volume = 0.9f * Level;
        source.Play();
        Destroy(source, hit.length / pitch + 0.2f);
    }

    void Sound(AudioClip clip, float level)
    {
        if (clip != null && sfx != null)
        {
            sfx.PlayOneShot(clip, level * Level);
        }
    }
}
