using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StartUp
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]

    public static void InstantiatePrefabs()
    {
        Debug.Log("-- Instatiating objects --");
        GameObject[] prefabsToInstantiate = Resources.LoadAll<GameObject>("InstantiateOnLoad/");

        foreach (GameObject prefab in prefabsToInstantiate)
        {
            GameObject.Instantiate(prefab);
            Debug.Log($"instantiate one prefab {prefab.name} with {prefab}");
        }

        Debug.Log("-- Instatiating objects done --");
    }
}
