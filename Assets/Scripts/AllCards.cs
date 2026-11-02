using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AllCards : MonoBehaviour
{
    public List<CardData> AllCardsList = new List<CardData>();
    private static readonly Lazy<AllCards> instance = new Lazy<AllCards>(() => new AllCards());

    private AllCards()
    {
        // Konstruktorlogik här
    }

    public static AllCards getInstance()
    {
        return instance.Value;
    }
    public List<CardData> GetAllCardsList()
    {
        return AllCardsList;
    }

    // The runners come from the Eventor points (eventor/compute_points.py writes
    // Resources/Runners/runners.json). Speed and technique are kept as decimals; cards show them rounded.
    public void SetAllCards()
    {
        AllCardsList.Clear();
        TextAsset file = Resources.Load<TextAsset>("Runners/runners");
        RunnerFile data = file != null ? JsonUtility.FromJson<RunnerFile>(file.text) : null;
        if (data == null || data.runners == null || data.runners.Count == 0)
        {
            Debug.LogError("No runners found in Resources/Runners/runners.json");
            return;
        }

        foreach (RunnerEntry r in data.runners)
        {
            // A real photo in Resources/Profilbilder wins over the drawn placeholder
            Sprite photo = Emblems.Photo(r.name);
            if (photo == null) photo = Emblems.Portrait(string.IsNullOrEmpty(r.id) ? r.name : r.id, r.man);
            CardData card = new CardData(r.name, r.speed, r.technique, Emblems.Flag(r.country), Emblems.Club(r.club), r.klass, r.man, photo);
            card.id = r.id;
            card.countryName = r.country;
            card.clubName = r.club;
            card.inPacks = r.inPacks;
            AllCardsList.Add(card);
        }
    }

    [Serializable]
    class RunnerFile
    {
        public string computed;
        public List<RunnerEntry> runners;
    }

    [Serializable]
    class RunnerEntry
    {
        public string id;
        public string name;
        public bool man;
        public string klass;
        public string country;
        public string club;
        public float speed;
        public float technique;
        public bool inPacks;
    }
}
