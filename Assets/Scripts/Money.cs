using System;
using UnityEngine;

// The player's personal money and trophies. Saved on this computer, so they survive between game sessions.
public class Money : MonoBehaviour
{
    public static Money Instance;

    // Raised whenever money or trophies change, so UI can refresh
    public static event Action Changed;

    private int money;
    private int trophy;

    private void Awake()
    {
        if(Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        Load();
    }

    // In the Editor every project folder gets its own save, so ParrelSync clones on the same computer
    // do not share one wallet while testing multiplayer. A built game uses one save.
    private static string Key(string name)
    {
        string key = "OManager." + name;
        if (Application.isEditor)
        {
            key += "." + Animator.StringToHash(Application.dataPath);
        }
        return key;
    }

    private void Load()
    {
        money = PlayerPrefs.GetInt(Key("Money"), 0);
        trophy = PlayerPrefs.GetInt(Key("Trophy"), 0);
    }

    private void Save()
    {
        PlayerPrefs.SetInt(Key("Money"), money);
        PlayerPrefs.SetInt(Key("Trophy"), trophy);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public int GetMoney()
    {
        return money;
    }
    public int GetTrophy()
    {
        return trophy;
    }

    public void AddMoney(int amount)
    {
        money += amount;
        Save();
    }

    public void AddTrophy(int amount)
    {
        trophy += amount;
        Save();
    }

    public void SetMoney(int amount)
    {
        money = amount;
        Save();
    }

    public void SetTrophy(int amount)
    {
        trophy = amount;
        Save();
    }
}
