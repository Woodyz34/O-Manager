using System.Collections;
using Fusion;
using UnityEngine.SceneManagement;

// Fusion's scene manager, but it stops quietly when the network game has already ended while a scene was loading.
// Without this a lost connection (for example DisconnectByServerTimeout) in the middle of a scene change gave
// a NullReferenceException in NetworkSceneManagerDefault.OnSceneLoaded, because the runner was already gone.
public class SafeNetworkSceneManager : NetworkSceneManagerDefault
{
    protected override IEnumerator OnSceneLoaded(SceneRef sceneRef, Scene scene, NetworkLoadSceneParameters sceneParams)
    {
        if (!StillRunning()) yield break;
        IEnumerator inner = base.OnSceneLoaded(sceneRef, scene, sceneParams);
        while (true)
        {
            if (!StillRunning()) yield break;
            if (!inner.MoveNext()) yield break;
            yield return inner.Current;
        }
    }

    bool StillRunning()
    {
        NetworkRunner runner = Runner;
        return runner != null && runner && !runner.IsShutdown;
    }
}
