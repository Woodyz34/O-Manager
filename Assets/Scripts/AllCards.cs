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

        Sprite sweden = Resources.Load<Sprite>("Country/sverige");
        HashSet<string> names = new HashSet<string>();
        foreach (RunnerEntry r in data.runners)
        {
            Sprite flag = r.country == "SWEDEN" ? sweden : null;
            Sprite photo = Resources.Load<Sprite>("RunnerPicture/" + r.name);
            CardData card = new CardData(r.name, r.speed, r.technique, flag, null, r.klass, r.man, photo);
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
        list.Add(new CardData("Axel Elmblad", 79f, 98f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Axel Elmblad")));
        list.Add(new CardData("August Mollén", 84f, 85f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/August Mollén")));
        list.Add(new CardData("Axel Granqvist", 82f, 94f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Axel Granqvist")));
        list.Add(new CardData("Eetu Savolainen", 78f, 96f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Eetu Savolainen")));
        list.Add(new CardData("Gustav Bergman", 80f, 99f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Gustav Bergman")));
        list.Add(new CardData("Gustav Runefors", 77f, 95f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Gustav Runefors")));
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
