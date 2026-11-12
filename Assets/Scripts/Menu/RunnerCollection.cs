using System;
using System.Collections.Generic;
using UnityEngine;

// The runners the player has pulled from packs. Saved on this computer next to the money.
public static class RunnerCollection
{
    // Raised whenever a runner is added, so UI can refresh
    public static event Action Changed;

    [Serializable]
    class Entry
    {
        public string name;
        public int count;
    }

    [Serializable]
    class SaveData
    {
        public List<Entry> runners = new List<Entry>();
        public int packsOpened;
    }

    static SaveData data;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        data = null;
        Changed = null;
    }

    // Same idea as Money: in the Editor every project folder gets its own save, so ParrelSync clones
    // do not share one collection. A built game uses one save.
    static string Key()
    {
        string key = "OManager.Runners";
        if (Application.isEditor)
        {
            key += "." + Animator.StringToHash(Application.dataPath);
        }
        return key;
    }

    static SaveData Data()
    {
        if (data == null)
        {
            string json = PlayerPrefs.GetString(Key(), "");
            try
            {
                data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                // A damaged save must not stop the game from starting. The damaged text is kept under another
                // name, so it can be looked at (and maybe mended) afterwards.
                Debug.LogError("RunnerCollection: the saved runners could not be read: " + e.Message);
                PlayerPrefs.SetString(Key() + ".Damaged", json);
                data = null;
            }
            if (data == null) data = new SaveData();
            if (data.runners == null) data.runners = new List<Entry>();
            data.runners.RemoveAll(e => e == null || string.IsNullOrEmpty(e.name));
            if (MoveRenamed(data))
            {
                PlayerPrefs.SetString(Key(), JsonUtility.ToJson(data));
                PlayerPrefs.Save();
            }
        }
        return data;
    }

    // A runner who was two cards under two spellings is now one card. A saved card under the old
    // name moves to the new one (once, if the player had both).
    static bool MoveRenamed(SaveData save)
    {
        Dictionary<string, string> renamed = AllCardsRenamed();
        bool changed = false;
        for (int i = save.runners.Count - 1; i >= 0; i--)
        {
            Entry entry = save.runners[i];
            string to;
            if (!renamed.TryGetValue(entry.name, out to)) continue;
            changed = true;
            if (save.runners.Exists(e => e.name == to))
            {
                save.runners.RemoveAt(i);
            }
            else
            {
                entry.name = to;
            }
        }
        return changed;
    }

    static Dictionary<string, string> AllCardsRenamed()
    {
        RunnerCards.All();   // loads runners.json, where the renames are
        return AllCards.getInstance().Renamed;
    }

    static void Save()
    {
        PlayerPrefs.SetString(Key(), JsonUtility.ToJson(Data()));
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static int PacksOpened
    {
        get { return Data().packsOpened; }
    }

    // How many copies of this runner the player has
    public static int Count(string runnerName)
    {
        foreach (Entry entry in Data().runners)
        {
            if (entry.name == runnerName)
            {
                return entry.count;
            }
        }
        return 0;
    }

    public static bool Owns(string runnerName)
    {
        return Count(runnerName) > 0;
    }

    // Number of different runners owned
    public static int OwnedCount()
    {
        return OwnedCards().Count;
    }

    // Only runners that still exist in the game are listed
    public static List<CardData> OwnedCards()
    {
        List<CardData> owned = new List<CardData>();
        foreach (CardData card in RunnerCards.All())
        {
            if (Owns(card.RunnerName))
            {
                owned.Add(card);
            }
        }
        return owned;
    }

    // Adds one copy from a pack. Returns true when the runner is new to the collection.
    public static bool AddFromPack(string runnerName)
    {
        SaveData save = Data();
        save.packsOpened++;
        foreach (Entry entry in save.runners)
        {
            if (entry.name == runnerName)
            {
                entry.count++;
                Save();
                return false;
            }
        }
        save.runners.Add(new Entry { name = runnerName, count = 1 });
        Save();
        return true;
    }

    // Empties the collection. Used by Tools > O-Manager > Reset runner collection while testing.
    public static void Reset()
    {
        data = new SaveData();
        Save();
    }
}
