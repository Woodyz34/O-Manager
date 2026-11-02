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
    // Resources/Runners/runners.json). The hand-made cards below are only used when that file
    // is missing, or kept out of the packs for players who already own them.
    public void SetAllCards()
    {
        AllCardsList.Clear();
        List<CardData> handMade = HandMadeCards();
        TextAsset file = Resources.Load<TextAsset>("Runners/runners");
        RunnerFile data = file != null ? JsonUtility.FromJson<RunnerFile>(file.text) : null;
        if (data == null || data.runners == null || data.runners.Count == 0)
        {
            AllCardsList.AddRange(handMade);
            return;
        }

        HashSet<string> names = new HashSet<string>();
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
            names.Add(r.name);
        }

        // Old hand-made cards that are not in the Eventor list stay for players who own them
        foreach (CardData card in handMade)
        {
            if (!names.Contains(card.RunnerName))
            {
                card.inPacks = false;
                AllCardsList.Add(card);
            }
        }
    }

    List<CardData> HandMadeCards()
    {
        List<CardData> list = new List<CardData>();
        Sprite sweden = Emblems.Flag("SWEDEN");
        Sprite ikhp = Emblems.Club("IKHP Huskvarna Idrottsklubb");
        list.Add(new CardData("Axel Elmblad", 79f, 98f, sweden, ikhp, "H21", true, Emblems.Photo("Axel Elmblad")));
        list.Add(new CardData("August Mollén", 84f, 85f, sweden, ikhp, "H21", true, Emblems.Photo("August Mollén")));
        list.Add(new CardData("Axel Granqvist", 82f, 94f, sweden, ikhp, "H21", true, Emblems.Photo("Axel Granqvist")));
        list.Add(new CardData("Eetu Savolainen", 78f, 96f, sweden, ikhp, "H21", true, Emblems.Photo("Eetu Savolainen")));
        list.Add(new CardData("Gustav Bergman", 80f, 99f, sweden, ikhp, "H21", true, Emblems.Photo("Gustav Bergman")));
        list.Add(new CardData("Gustav Runefors", 77f, 95f, sweden, ikhp, "H21", true, Emblems.Photo("Gustav Runefors")));
        return list;
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
