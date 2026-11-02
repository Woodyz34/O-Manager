using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Sounds for the whole game's interface, made at start so no scene needs setting up:
//   a click on every button, a lighter click on toggles and dropdowns, a tick on sliders,
//   and the start sound (forest, the start clock's beeps) the first time the main menu opens.
// The sounds are in Resources/Sounds (made by Docs/ui_sounds.py). They follow the master volume
// in the Esc menu, since that sets AudioListener.volume.
public class UISounds : MonoBehaviour
{
    const float ClickVolume = 0.55f;
    const float StartVolume = 0.7f;
    const float DragLimit = 25f;     // a press that moves further than this is a drag (scrolling), not a click

    static UISounds instance;
    static bool startPlayed;

    AudioSource source;
    AudioClip click;
    AudioClip toggle;
    AudioClip tick;
    AudioClip startup;

    Selectable pressed;
    Vector2 pressedAt;
    readonly List<RaycastResult> hits = new List<RaycastResult>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        startPlayed = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        if (instance != null)
        {
            return;
        }
        GameObject go = new GameObject("UI sounds");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UISounds>();
    }

    // Lets other scripts play the button click, e.g. for a key that works like a button
    public static void Click()
    {
        if (instance != null) instance.Play(instance.click, ClickVolume);
    }

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        click = Resources.Load<AudioClip>("Sounds/ui_click");
        toggle = Resources.Load<AudioClip>("Sounds/ui_toggle");
        tick = Resources.Load<AudioClip>("Sounds/ui_tick");
        startup = Resources.Load<AudioClip>("Sounds/startup");
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        pressed = null;
        // Once per run, when the game opens on the main menu (not when coming back to it)
        if (!startPlayed && scene.name == "MainMenu")
        {
            startPlayed = true;
            Play(startup, StartVolume);
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            pressed = SelectableUnderMouse();
            pressedAt = Input.mousePosition;
            // Sliders and scroll bars answer at once, since they are dragged
            if (pressed is Slider || pressed is Scrollbar)
            {
                Play(tick, ClickVolume);
                pressed = null;
            }
        }
        else if (Input.GetMouseButtonUp(0) && pressed != null)
        {
            Selectable released = SelectableUnderMouse();
            bool moved = ((Vector2)Input.mousePosition - pressedAt).magnitude > DragLimit;
            if (released == pressed && !moved)
            {
                Play(SoundFor(pressed), ClickVolume);
            }
            pressed = null;
        }
    }

    AudioClip SoundFor(Selectable selectable)
    {
        if (selectable is Toggle || selectable is TMP_Dropdown || selectable is Dropdown)
        {
            return toggle;
        }
        if (selectable is TMP_InputField || selectable is InputField)
        {
            return tick;
        }
        return click;
    }

    // The button (or toggle, slider...) that the mouse is over and that can be used right now
    Selectable SelectableUnderMouse()
    {
        EventSystem events = EventSystem.current;
        if (events == null)
        {
            return null;
        }
        PointerEventData pointer = new PointerEventData(events) { position = Input.mousePosition };
        hits.Clear();
        events.RaycastAll(pointer, hits);
        if (hits.Count == 0 || hits[0].gameObject == null)
        {
            return null;
        }
        // Only the topmost thing counts, so a button under a panel makes no sound
        Selectable selectable = hits[0].gameObject.GetComponentInParent<Selectable>();
        if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable())
        {
            return null;
        }
        return selectable;
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip != null && source != null)
        {
            source.PlayOneShot(clip, volume);
        }
    }
}
