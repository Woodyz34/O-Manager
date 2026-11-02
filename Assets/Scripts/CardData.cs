using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

public class CardData
{

    public float technique;
    public float speed;
    public string RunnerName;
    public Sprite country;
    public Sprite clubb;
    public Sprite RunnerPicture;
    public bool man = true;
    public string klass;

    // Set for runners that come from the Eventor points (Resources/Runners/runners.json)
    public string id;
    public string countryName;   // shown when there is no flag picture for the country
    public string clubName;
    public bool inPacks = true;  // false = frozen: kept by players who own it, no longer in packs

    public CardData(string Name, float Speed, float Technique, Sprite Country, Sprite Clubb, string Klass, bool Man, Sprite Profile)
    {
        RunnerName = Name;
        speed = Speed;
        technique = Technique;
        country = Country;
        clubb = Clubb;
        klass = Klass;
        man = Man;
        RunnerPicture = Profile;
    }
}
