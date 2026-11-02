using System.Collections;
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
    static bool loading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        cachedPage = null;
        loading = false;
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
        SceneLoader runner = page.GetComponent<SceneLoader>();
        if (runner == null) runner = page.AddComponent<SceneLoader>();
        runner.StartCoroutine(runner.Run(page, scene, message));
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
            if (canvas != null) canvas.sortingOrder = 1000;
            return cachedPage;
        }
        return null;
    }

    IEnumerator Run(GameObject page, string scene, string message)
    {
        LoadingScreenUI ui = page.GetComponent<LoadingScreenUI>();
        if (ui != null) ui.SetMessage(message);
        Slider slider = ui != null ? ui.slider : null;
        if (slider != null) slider.value = 0f;
        page.SetActive(true);
        float shownAt = Time.unscaledTime;
        yield return null;   // let the page draw before the loading starts

        AsyncOperation operation = SceneManager.LoadSceneAsync(scene);
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
        if (slider != null) slider.value = 1f;
        yield return null;   // one frame for the new scene's Start methods
        page.SetActive(false);
        loading = false;
    }
}
