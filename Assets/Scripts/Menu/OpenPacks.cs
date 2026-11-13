using System;
using System.Collections.Generic;
using UnityEngine;

// The runner pool "Open packs" (2026-11-13). When the competition starts, every player opens free packs on their own
// computer: one in an individual race, one per leg in a relay, each fitting its leg (a women's leg gives a woman,
// a men's leg a man, an open leg anyone). The pack opening is the same show as in the shop. Then the player picks
// from what came out as usual: the runner in an individual race, the runner for each leg in a relay.
// The runners are only on loan: nothing is added to My Runners and no money is paid.
public class OpenPacks : MonoBehaviour
{
    List<CardData> drawn;
    int next;
    bool relay;
    Action done;
    PackOpening opening;

    public static void Begin(CourseData course, Action done)
    {
        List<LegSex> legs = RunnerRules.LegSexes(course);
        OpenPacks packs = new GameObject("Open packs").AddComponent<OpenPacks>();
        packs.drawn = RunnerRules.DrawPacks(legs);
        packs.relay = course != null && course.IsRelay();
        packs.done = done;
        packs.OpenNext();
    }

    void OpenNext()
    {
        if (opening != null) Destroy(opening.gameObject);
        opening = null;
        if (next >= drawn.Count)
        {
            Finish();
            return;
        }
        PackOpening prefab = Resources.Load<PackOpening>("UI/PackOpening");
        if (prefab == null)
        {
            Debug.LogWarning("OpenPacks: PackOpening prefab is missing (Tools > O-Manager > Apply Menu UI); the runners are given without the show.");
            Finish();
            return;
        }
        CardData runner = drawn[next];
        string title = relay ? "PACK " + (next + 1) + " OF " + drawn.Count : "YOUR RUNNER";
        bool last = next == drawn.Count - 1;
        string button = !last ? "Next pack" : relay ? "Place the runners" : "Race";
        next++;
        opening = Instantiate(prefab);
        opening.PlayLoan(runner, title, button, OpenNext);
    }

    void Finish()
    {
        Action callback = done;
        done = null;
        Destroy(gameObject);
        callback?.Invoke();
    }
}
