using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ASyncLoader : MonoBehaviour
{
    [SerializeField] private GameObject loadingScreen;
    [SerializeField] private GameObject mainMenu;



    [SerializeField] private Slider loadingSlider;

    public void LoadLevelBtn(string levelToLoad)
    {
        mainMenu.SetActive(false);
        loadingScreen.SetActive(true);

        StartCoroutine(LoadLevelASync(levelToLoad));
    }

    IEnumerator LoadLevelASync(string levelToLoad)
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(levelToLoad);
        loadOperation.allowSceneActivation = false;

        float displayProgress = 0f;

        while (loadOperation.progress < 0.9f || displayProgress < 1f)
        {
            float targetProgress = Mathf.Clamp01(loadOperation.progress / 0.9f);
            displayProgress = Mathf.MoveTowards(displayProgress, targetProgress, 2f * Time.unscaledDeltaTime);
            loadingSlider.value = displayProgress;
            yield return null;
        }

        loadingSlider.value = 1f;

        // Vnta t.ex. p knapptryck eller en kort delay innan du aktiverar
        yield return new WaitForSeconds(0.2f);

        loadOperation.allowSceneActivation = true;
    }

}
