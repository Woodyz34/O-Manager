using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AllCards : MonoBehaviour
{
    public List<CardData> AllCardsList = new List<CardData>();

    // Old card name -> new name, for runners that were two cards under two spellings and are now one
    // (e.g. "Miri Thrane Oedum" -> "Miri Thrane Ødum"). The collection moves saved cards to the new name.
    public readonly Dictionary<string, string> Renamed = new Dictionary<string, string>();
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
    // Resources/Runners/runners.json). Physics, speed (sprint) and technique are kept as decimals; cards show them
    // rounded. In the file "physics" is the forest speed and "speed" the sprint speed (-1 = no sprint race).
    public void SetAllCards()
    {
        AllCardsList.Clear();
        Renamed.Clear();
        TextAsset file = Resources.Load<TextAsset>("Runners/runners");
        RunnerFile data = file != null ? JsonUtility.FromJson<RunnerFile>(file.text) : null;
        if (data == null || data.runners == null || data.runners.Count == 0)
        {
            Debug.LogError("No runners found in Resources/Runners/runners.json");
            return;
        }

        if (data.renamed != null)
        {
            foreach (RenamedEntry rename in data.renamed)
            {
                if (!string.IsNullOrEmpty(rename.from) && !string.IsNullOrEmpty(rename.to)) Renamed[rename.from] = rename.to;
            }
        }

        foreach (RunnerEntry r in data.runners)
        {
            // A real photo in Resources/Profilbilder wins over the drawn placeholder
            Sprite photo = Emblems.Photo(r.name);
            if (photo == null) photo = Emblems.Portrait(string.IsNullOrEmpty(r.id) ? r.name : r.id, r.man);
            CardData card = new CardData(r.name, r.physics, r.technique, Emblems.Flag(r.country), Emblems.Club(r.club), r.klass, r.man, photo);
            card.sprintSpeed = r.speed >= 0f ? r.speed : CardData.NoValue;
            card.sprintTechnique = r.sprintTechnique >= 0f ? r.sprintTechnique : CardData.NoValue;
            card.id = r.id;
            card.countryName = r.country;
            card.countryGuessed = r.countryGuessed;
            card.clubName = r.club;
            card.inPacks = r.inPacks;
            if (r.merits != null) card.merits = r.merits;
            AllCardsList.Add(card);
        }
    }

    [Serializable]
    class RunnerFile
    {
        public string computed;
        public List<RenamedEntry> renamed;
        public List<RunnerEntry> runners;
    }

    [Serializable]
    class RenamedEntry
    {
        public string from;
        public string to;
    }

    [Serializable]
    class RunnerEntry
    {
        public string id;
        public string name;
        public bool man;
        public string klass;
        public string country;
        public bool countryGuessed;
        public string club;
        public float physics;            // forest speed
        public float speed = -1f;        // sprint speed, -1 = none
        public float technique;
        public float sprintTechnique = -1f;
        public bool inPacks;
        public List<RunnerMerit> merits;
    }
}
