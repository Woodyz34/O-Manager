using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Changes scene behind the game's loading page (the forest page with the bobbing control flag),
// for scenes that are not loaded through the network: main menu <-> course builder.
// The page is copied from the first scene that has one (the main menu) and kept for later, so the
// course builder, which has no loading page of its own, can use it too.
public class SceneLoader : MonoBehaviour
{
    const float MinimumShown = 0.6f;    // seconds, so the page does not just flash by

    static GameObject cachedPage;
    static SceneLoader runner;      // on its own object that is always active (the page is hidden between uses)
    static bool loading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        cachedPage = null;
        runner = null;
        loading = false;
        SceneManager.sceneLoaded -= RememberPage;
        SceneManager.sceneLoaded += RememberPage;
    }

    // The page is copied as soon as a scene that has one opens (2026-11-22). Before, it was only copied the first
    // time the loader was used, and the lobby has no page of its own: opening the course builder from the lobby
    // showed no loading page at all, only a frozen lobby.
    static void RememberPage(Scene scene, LoadSceneMode mode)
    {
        if (cachedPage == null) Page();
    }

    public static void Load(string scene, string message)
    {
        if (loading) return;
        GameObject page = Page();
        if (page == null)
        {
            SceneManager.LoadScene(scene);
            return;
        }
        loading = true;
        page.SetActive(true);
        Runner().StartCoroutine(runner.Run(page, scene, message, LoadSceneMode.Single, null));
    }

    // Loads a scene on top of the one that is open (the lobby's shop, My Runners and course builder visits),
    // behind the same loading page. done gets the new scene once its objects have started.
    public static void LoadAdditive(string scene, string message, Action<Scene> done)
    {
        if (loading) return;
        loading = true;
        GameObject page = Page();
        if (page != null) page.SetActive(true);
        Runner().StartCoroutine(runner.Run(page, scene, message, LoadSceneMode.Additive, done));
    }

    static SceneLoader Runner()
    {
        if (runner == null)
        {
            GameObject go = new GameObject("Scene loader");
            DontDestroyOnLoad(go);
            runner = go.AddComponent<SceneLoader>();
        }
        return runner;
    }

    // Leaves a network game and then changes scene. The scene only changes once the runner has shut down:
    // loading it while the runner is still leaving made Photon kick the player and log an error.
    // 2026-11-26 (Anders: ending a race on Fossum stood still for about 4 s before the menu came): the loading page
    // with its turning ring is shown first, the race's own objects (the 2D map has some 70 000, plus the 3D world)
    // are cleared a few thousand a frame so the page keeps moving, and the new scene loads in the background.
    public static void LeaveSession(NetworkRunner runner, string scene, string message = "Leaving")
    {
        loading = true;
        Runner().StartCoroutine(Leave(runner, scene, message));
    }

    static IEnumerator Leave(NetworkRunner networkRunner, string scene, string message)
    {
        GameObject page = Page();
        LoadingScreenUI ui = page != null ? page.GetComponent<LoadingScreenUI>() : null;
        if (ui != null) ui.SetMessage(message);
        if (ui != null && ui.slider != null) ui.slider.value = 0f;
        if (page != null) page.SetActive(true);
        yield return null;   // the page is drawn before anything heavy starts
        yield return null;

        // Leaving on purpose: not a lost connection (SessionGuard)
        SessionGuard.Leaving = true;
        Task shutdown = null;
        try
        {
            if (networkRunner != null && networkRunner.IsRunning) shutdown = networkRunner.Shutdown();
        }
        catch (Exception e)
        {
            Debug.LogWarning("SceneLoader: leaving the session failed: " + e.Message);
        }
        float waitUntil = Time.unscaledTime + 10f;
        while (shutdown != null && !shutdown.IsCompleted && Time.unscaledTime < waitUntil) yield return null;
        if (shutdown != null && shutdown.IsFaulted) Debug.LogWarning("SceneLoader: leaving the session failed: " + shutdown.Exception?.GetBaseException().Message);
        SessionGuard.Leaving = false;

        yield return ClearActiveScene(ui);
        yield return runner.Run(page, scene, message, LoadSceneMode.Single, null);
    }

    // Switches the open scene off and destroys it a few thousand objects a frame, so the loading page keeps turning
    // instead of the whole lot going in one frame when the next scene opens. Objects that belong to the network
    // (a runner, network objects) are left for the scene change.
    static IEnumerator ClearActiveScene(LoadingScreenUI ui)
    {
        const int PerFrame = 4000;
        Scene scene = SceneManager.GetActiveScene();
        var doomed = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == null) continue;
            if (root.GetComponentInChildren<NetworkRunner>(true) != null || root.GetComponentInChildren<NetworkObject>(true) != null) continue;
            doomed.Add(root);
        }
        // Off first: no script of the race runs (or misses a destroyed object) while it is taken apart
        foreach (GameObject root in doomed)
        {
            try { root.SetActive(false); }
            catch (Exception e) { Debug.LogWarning("SceneLoader: " + e.Message); }
        }
        yield return null;
        int total = 0, done = 0;
        foreach (GameObject root in doomed) if (root != null) total += root.transform.childCount + 1;
        int thisFrame = 0;
        foreach (GameObject root in doomed)
        {
            if (root == null) continue;
            Transform t = root.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Destroy(t.GetChild(i).gameObject);
                done++;
                if (++thisFrame >= PerFrame)
                {
                    thisFrame = 0;
                    if (ui != null && ui.slider != null && total > 0) ui.slider.value = 0.3f * done / total;
                    yield return null;
                }
            }
            Destroy(root);
            done++;
            thisFrame += 50;
        }
        yield return null;
    }

    // The loading page over a network scene change (Fusion loads the scene itself; Spawner calls these)
    public static void ShowBusy(string message)
    {
        GameObject page = Page();
        if (page == null) return;
        LoadingScreenUI ui = page.GetComponent<LoadingScreenUI>();
        if (ui != null) ui.SetMessage(message);
        if (ui != null && ui.slider != null) ui.slider.value = 0f;
        page.SetActive(true);
    }

    public static void HideBusy()
    {
        if (loading || cachedPage == null) return;
        cachedPage.SetActive(false);
    }

    static GameObject Page()
    {
        if (cachedPage != null) return cachedPage;
        foreach (LoadingScreenUI ui in FindObjectsByType<LoadingScreenUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ui.transform.parent != null) continue;
            cachedPage = Instantiate(ui.gameObject);
            cachedPage.name = "Loading page (kept)";
            cachedPage.SetActive(false);
            DontDestroyOnLoad(cachedPage);
            Canvas canvas = cachedPage.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = true;   // in case the original was switched off (a lobby visit hides the lobby's canvases)
                canvas.sortingOrder = 1000;
            }
            return cachedPage;
        }
        return null;
    }

    IEnumerator Run(GameObject page, string scene, string message, LoadSceneMode mode, Action<Scene> done)
    {
        LoadingScreenUI ui = page != null ? page.GetComponent<LoadingScreenUI>() : null;
        if (ui != null) ui.SetMessage(message);
        Slider slider = ui != null ? ui.slider : null;
        if (slider != null) slider.value = 0f;
        if (page != null) page.SetActive(true);
        float shownAt = Time.unscaledTime;
        yield return null;   // let the page draw before the loading starts

        // Let the loading thread work in the background without starving the frames that draw the page, so the
        // spinner keeps turning while the scene loads (2026-11-23)
        ThreadPriority before = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = ThreadPriority.Low;
        AsyncOperation operation = SceneManager.LoadSceneAsync(scene, mode);
        operation.allowSceneActivation = false;
        while (operation.progress < 0.9f)
        {
            if (slider != null) slider.value = operation.progress / 0.9f * 0.9f;
            yield return null;
        }
        while (Time.unscaledTime - shownAt < MinimumShown)
        {
            if (slider != null) slider.value = Mathf.Lerp(slider.value, 0.95f, 0.2f);
            yield return null;
        }
        operation.allowSceneActivation = true;
        while (!operation.isDone) yield return null;
        Application.backgroundLoadingPriority = before;
        if (slider != null) slider.value = 1f;
        yield return null;   // one frame for the new scene's Start methods
        if (page != null) page.SetActive(false);
        loading = false;
        if (done != null)
        {
            Scene loaded = SceneManager.GetSceneByName(scene);
            done(loaded);
        }
    }
}
