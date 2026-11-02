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
    public void SetAllCards()
    {
        AllCardsList.Add(new CardData("Axel Elmblad", 79f, 98f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Axel Elmblad")));
        AllCardsList.Add(new CardData("August Mollén", 84f, 85f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/August Mollén")));
        AllCardsList.Add(new CardData("Axel Granqvist", 82f, 94f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Axel Granqvist")));
        AllCardsList.Add(new CardData("Eetu Savolainen", 78f, 96f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Eetu Savolainen")));
        AllCardsList.Add(new CardData("Gustav Bergman", 80f, 99f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Gustav Bergman")));
        AllCardsList.Add(new CardData("Gustav Runefors", 77f, 95f, Resources.Load<Sprite>("Country/sverige"), Resources.Load<Sprite>("Clubs/IKHP"), "H21", true, Resources.Load<Sprite>("RunnerPicture/Gustav Runefors")));
    }
}
