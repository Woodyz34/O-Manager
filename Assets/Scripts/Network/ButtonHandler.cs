using Fusion;
using UnityEngine;

public class ButtonHandler : MonoBehaviour
{
    private MyNetworkBehaviour myNetScript;


    public void RemoveLineClick()
    {
        if (NetworkPlayer.Local == null)
        {
            Debug.LogWarning("Ingen lokal NetworkPlayer ännu!");
            return;
        }

        // Kör logik på den lokala spelaren
        NetworkPlayer.Local.GetComponent<AgentMovement>().RemoveLine();
        Debug.Log("ButtonClick kördes på lokal klient!");
    }

   
}
