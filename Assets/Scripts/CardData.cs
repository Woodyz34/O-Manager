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
