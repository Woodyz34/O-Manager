using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

public class CardData
{

    public float technique;
    // Physics: how fast the runner is in the forest (from long and middle races). Was called "speed" before 2026-11-13.
    public float physics;
    // Speed: how fast the runner is on hard ground, from clean sprint times (VM and World Cup sprint = 100).
    // Below 0 = no sprint race ("-" on the card); then the runner uses physics everywhere (RunnerRules.SurfaceFactor).
    public float sprintSpeed = NoValue;
    // Technique in sprints, kept for later and not shown yet (below 0 = none)
    public float sprintTechnique = NoValue;
    public const float NoValue = -1f;
    public string RunnerName;
    public Sprite country;
    public Sprite clubb;
    public Sprite RunnerPicture;
    public bool man = true;
    public string klass;

    // Set for runners that come from the Eventor points (Resources/Runners/runners.json)
    public string id;
    public string countryName;   // shown when there is no flag picture for the country
    public bool countryGuessed;  // the country is the club's country (Eventor), not known for the runner
    public string clubName;
    public bool inPacks = true;  // false = frozen: kept by players who own it, no longer in packs
    public List<RunnerMerit> merits = new List<RunnerMerit>();   // best races of the last year, best first
    // Physics, speed and technique at every rating update, oldest first (2026-11-24): the line chart in My Runners
    public List<RatingPoint> history = new List<RatingPoint>();

    public CardData(string Name, float Speed, float Technique, Sprite Country, Sprite Clubb, string Klass, bool Man, Sprite Profile)
    {
        RunnerName = Name;
        physics = Speed;
        technique = Technique;
        country = Country;
        clubb = Clubb;
        klass = Klass;
        man = Man;
        RunnerPicture = Profile;
    }

    public bool HasSprintSpeed => sprintSpeed >= 0f;

    // "87", or "-" when there is no sprint speed
    public string SprintSpeedText => HasSprintSpeed ? Mathf.RoundToInt(sprintSpeed).ToString() : "-";

    // The same runner with other values ("Same stats" in the lobby). Since 2026-11-16 the host sets the sprint speed
    // too; without one (NoValue) physics is used on every kind of ground.
    public CardData WithStats(float newPhysics, float newTechnique, float newSprintSpeed = NoValue)
    {
        CardData copy = (CardData)MemberwiseClone();
        copy.physics = newPhysics;
        copy.sprintSpeed = newSprintSpeed;
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

// A runner's numbers at one rating update (eventor/compute_points.py, add_history)
[System.Serializable]
public class RatingPoint
{
    public string d;     // yyyy-mm-dd
    public float p;      // physics
    public float s = -1; // speed (sprint), -1 = none
    public float t;      // technique
}
