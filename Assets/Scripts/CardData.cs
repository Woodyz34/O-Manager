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
    public List<RunnerMerit> merits = new List<RunnerMerit>();   // best races of the last year, best first

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

    // The same runner with other values ("Same stats" in the lobby)
    public CardData WithStats(float newSpeed, float newTechnique)
    {
        CardData copy = (CardData)MemberwiseClone();
        copy.speed = newSpeed;
        copy.technique = newTechnique;
        return copy;
    }
}

// One of a runner's best races of the year. The score is 100 * (time a 100-rated runner would
// run in that race) / own time, so it already counts how strong the field was.
[System.Serializable]
public class RunnerMerit
{
    public string race;       // competition name, e.g. "EM lång"
    public string date;       // yyyy-mm-dd
    public string klass;
    public int place;
    public int of;            // finishers in the class
    public int behind;        // seconds behind the class winner
    public float score;
    public float field;       // mean rating of the 5 best runners in the class
    public string results;    // file in Resources/Runners/Results with the whole class and split times
}
