using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Sounds for the whole game's interface, made at start so no scene needs setting up:
//   a click on every button, a lighter click on toggles and dropdowns, a tick on sliders,
//   and the start sound (forest, the start clock's beeps) the first time the main menu opens
//   (not after the intro, which has its own sound).
// The sounds are in Resources/Sounds (made by Docs/ui_sounds.py). They follow the master volume
// in the Esc menu, since that sets AudioListener.volume.
//
// It runs before the EventSystem every frame, so it sees what is under the mouse before a click
// closes a panel or loads a scene (otherwise buttons that hide themselves made no sound).
// Anything that takes a click counts, not only Buttons: toggles, list rows, links in text.
[DefaultExecutionOrder(-32000)]
public class UISounds : MonoBehaviour
{
    const float ClickVolume = 0.4f;   // 2026-11-15: all a bit lower
    const float StartVolume = 0.5f;
    const float DragLimit = 25f;     // a press that moves further than this is a drag (scrolling), not a click

    static UISounds instance;
    static bool startPlayed;

    AudioSource source;
    AudioListener backupEar;
    float nextEarCheck;
    AudioClip click;
    AudioClip toggle;
    AudioClip tick;
    AudioClip startup;

    GameObject pressed;
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
        // Some race scenes have no AudioListener until the player's camera is made, and then nothing
        // can be heard. This one is switched on only while no other listener is there.
        backupEar = gameObject.AddComponent<AudioListener>();
        backupEar.enabled = false;
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
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit); not a new race
        if (mode == LoadSceneMode.Additive) return;
        pressed = null;
        CheckEar();
        // The intro film has its own sound, so the start sound is left out when the game opened with it
        if (scene.name == "Intro")
        {
            startPlayed = true;
        }
        // Once per run, when the game opens on the main menu (not when coming back to it)
        if (!startPlayed && scene.name == "MainMenu")
        {
            startPlayed = true;
            Play(startup, StartVolume);
        }
    }

    void Update()
    {
        if (Time.unscaledTime >= nextEarCheck)
        {
            nextEarCheck = Time.unscaledTime + 0.5f;
            CheckEar();
        }

        // Keys that work like buttons: Esc (the Esc menu, skipping the pack show) and Enter on a selected button
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Play(click, ClickVolume);
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != null && selected.GetComponent<ISubmitHandler>() != null && selected.GetComponent<TMP_InputField>() == null
                && selected.GetComponent<InputField>() == null && Usable(selected))
            {
                Play(SoundFor(selected), ClickVolume);
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            pressed = null;
            pressedAt = Input.mousePosition;
            GameObject hit = TopUnderMouse();
            if (hit == null) return;
            GameObject clickable = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            if (clickable != null && Usable(clickable))
            {
                pressed = clickable;
                return;
            }
            // Sliders, scroll bars and text fields take a press, not a click: they answer at once
            GameObject pressable = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit);
            if (pressable != null && Usable(pressable) && pressable.GetComponent<Selectable>() != null)
            {
                Play(tick, ClickVolume);
            }
        }
        else if (Input.GetMouseButtonUp(0) && pressed != null)
        {
            GameObject hit = TopUnderMouse();
            GameObject released = hit != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit) : null;
            bool moved = ((Vector2)Input.mousePosition - pressedAt).magnitude > DragLimit;
            if (released == pressed && !moved)
            {
                Play(SoundFor(pressed), ClickVolume);
            }
            pressed = null;
        }
    }

    void CheckEar()
    {
        bool other = false;
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (listener != backupEar && listener.isActiveAndEnabled)
            {
                other = true;
                break;
            }
        }
        if (backupEar.enabled == other) backupEar.enabled = !other;
    }

    AudioClip SoundFor(GameObject target)
    {
        if (target.GetComponent<Toggle>() != null || target.GetComponent<TMP_Dropdown>() != null || target.GetComponent<Dropdown>() != null)
        {
            return toggle;
        }
        if (target.GetComponent<TMP_InputField>() != null || target.GetComponent<InputField>() != null)
        {
            return tick;
        }
        return click;
    }

    // A button that is greyed out makes no sound
    static bool Usable(GameObject target)
    {
        if (target.GetComponent<ClickCatcher>() != null) return false;
        Selectable selectable = target.GetComponent<Selectable>();
        return selectable == null || (selectable.IsActive() && selectable.IsInteractable());
    }

    // The topmost thing under the mouse that the EventSystem would send the click to
    GameObject TopUnderMouse()
    {
        EventSystem events = EventSystem.current;
        if (events == null)
        {
            return null;
        }
        PointerEventData pointer = new PointerEventData(events) { position = Input.mousePosition };
        hits.Clear();
        events.RaycastAll(pointer, hits);
        return hits.Count > 0 ? hits[0].gameObject : null;
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip != null && source != null)
        {
            source.PlayOneShot(clip, volume);
        }
    }
}
