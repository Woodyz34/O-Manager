using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MistakeLogic : MonoBehaviour
{
    public RunnerCollision runnerDetector;
    private AgentMovement agentMovement;
    private static System.Random random = new System.Random();
    // The mistakes from before ForestMistakes (draw misses and long angle misses). Off since 2026-11-22.
    public static bool UseOldMistakes = false;
    // Start is called before the first frame update
    public void Start()
    {
        if (ActiveScene.Name == "Lobby")
        {
            return;
        }
        agentMovement = GetComponent<AgentMovement>();
    }

    // Update is called once per frame
    void Update()
    {
        // Timed for the 3D mode's slow-frame log (RunPerf, 2026-11-26)
        long perfStart = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateTimed();
        RunPerf.Add(RunPerf.Part.Mistakes, System.Diagnostics.Stopwatch.GetTimestamp() - perfStart);
    }

    void UpdateTimed()
    {
        if (ActiveScene.Name == "Lobby" || agentMovement == null || !agentMovement.Object.HasInputAuthority)
        {
            return;
        }
        // No mistakes on sprint courses for now: the runners run the route as it is drawn
        if (CourseLoader.Loaded != null && CourseLoader.Loaded.IsSprint())
        {
            runnerDetector.shouldMiss = false;
            return;
        }
        // Forest mistakes are made by ForestMistakes now (direction and parallel errors, reading in on the map),
        // once the map's objects are read. The old logic no longer stands in until then (2026-11-22): its long miss ran
        // a straight line 20-45 degrees off for 1.3 times the distance to the control (kilometres on a long leg), and
        // when the map's objects could not be read it did that all race, every race on that map. No mistakes until
        // the forest mistakes are ready.
        if (MapFeatureIndex.Ready || !UseOldMistakes)
        {
            runnerDetector.shouldMiss = false;
            return;
        }
        if (runnerDetector.cantBäckMiss || runnerDetector.cantStigMiss || runnerDetector.cantBeståndsgränsMiss)
        {
            return;
        }
        else if(runnerDetector.shouldMiss)
        {
            runnerDetector.shouldMiss = false;
            // agentMovement.runnerStatTec är redan omräknad till en bom-risk (0-1) och sätts om när spelaren
            // väljer löpare, så den läses här i stället för att sparas en gång när scenen laddas.
            if (ShouldRunnerMiss(agentMovement.runnerStatTec * 3f))
            {
                agentMovement.DoDrawMistake();
            }
        }
    }

    private bool ShouldRunnerMiss(float chanceFaktor)
    {

        if (chanceFaktor <= 0) return false;   // 0% chans
        if (chanceFaktor >= 1) return true;    // 100% chans

        return random.NextDouble() < chanceFaktor;

    }
}
