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

    // ---- Runners on loan ----
    // Runners the player only has for this round; never added to the collection, gone after the round.
    // Since 2026-11-16 "Your runners" lends only what is missing: one runner when the player owns none, and in a
    // relay the runners (and women or men) the legs need (RunnerRules.YourRunnersPool). It used to fill up to 10.
    // "Random runners" lends the host's number of random runners and the player picks only among those.
    public const int RaceMinimum = 1;
    static List<CardData> loans;
    static bool randomLent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void InitLoans()
    {
        loans = null;
        randomLent = false;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Back on a menu or in the lobby: the round is over, the runners on loan go back
    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (mode == UnityEngine.SceneManagement.LoadSceneMode.Additive) return;
        string n = scene.name;
        if (n == "Lobby" || n == "MainMenu" || n == "SingelPlayer" || n == "NewCompetition" || n == "MultiPlayer")
        {
            loans = null;
            randomLent = false;
        }
    }

    public static bool IsOnLoan(string runnerName) => loans != null && loans.Exists(c => c.RunnerName == runnerName);

    // The runners the player can race with in "Your runners": their own, plus the ones lent for this round.
    // In "Random runners" (RunnerRules.RandomCount) only the random runners lent for the round.
    public static List<CardData> CardsForRace()
    {
        if (loans == null) loans = new List<CardData>();
        System.Random random = new System.Random();
        int randomCount = RunnerRules.RandomCount;
        if (randomCount > 0)
        {
            if (!randomLent)
            {
                randomLent = true;
                List<CardData> every = RunnerCards.InPacks();
                while (loans.Count < randomCount && every.Count > 0)
                {
                    int i = random.Next(every.Count);
                    if (!loans.Exists(c => c.RunnerName == every[i].RunnerName)) loans.Add(every[i]);
                    every.RemoveAt(i);
                }
            }
            return new List<CardData>(loans);
        }

        List<CardData> owned = OwnedCards();
        // Owns no runner at all: one random runner on loan (woman or man)
        if (owned.Count == 0 && loans.Count == 0)
        {
            List<CardData> free = RunnerCards.InPacks();
            if (free.Count > 0) loans.Add(free[random.Next(free.Count)]);
        }
        owned.AddRange(loans.FindAll(c => !owned.Exists(o => o.RunnerName == c.RunnerName)));
        return owned;
    }

    // Lends these runners too for this round (a relay with women's or men's legs, or a max average overall, that
    // the player's own runners can't fill). Gone after the round like the other loans.
    public static void Lend(List<CardData> cards)
    {
        if (cards == null || cards.Count == 0) return;
        if (loans == null) loans = new List<CardData>();
        foreach (CardData card in cards)
        {
            if (card != null && (RunnerRules.RandomCount > 0 || !Owns(card.RunnerName)) && !loans.Exists(c => c.RunnerName == card.RunnerName)) loans.Add(card);
        }
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
