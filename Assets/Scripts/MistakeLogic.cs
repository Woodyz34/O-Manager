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
    // Start is called before the first frame update
    public void Start()
    {
        if (SceneManager.GetActiveScene().name == "Lobby")
        {
            return;
        }
        agentMovement = GetComponent<AgentMovement>();
    }

    // Update is called once per frame
    void Update()
    {
        if (SceneManager.GetActiveScene().name == "Lobby" || agentMovement == null || !agentMovement.Object.HasInputAuthority)
        {
            return;
        }
        // No mistakes on sprint courses for now: the runners run the route as it is drawn
        if (CourseLoader.Loaded != null && CourseLoader.Loaded.IsSprint())
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
