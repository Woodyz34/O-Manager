using Fusion;
using UnityEngine;
using static Unity.Collections.Unicode;

public class MyNetworkBehaviour : NetworkBehaviour
{
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void Rpc_ButtonPressed(RpcInfo info = default)
    {
        // info.Source är PlayerRef som skickade RPC:t
        PlayerRef sender = info.Source;

        
        Debug.Log($"Spelare {sender} tryckte på knappen.");

        if( sender == PlayerRef.None)
        {
            Debug.Log("sender is None");
            sender = Runner.LocalPlayer;
        }

        // Gör olika saker beroende på vem det var:
        AgentMovement player = Runner.GetPlayerObject(sender).GetComponent<AgentMovement>();
        player.RemoveLine();
        

        // Eller om du vill ge olika roller:
        if (sender.PlayerId == 1)
        {
           
        }
        else
        {
           
        }
    }

    private void DoAdminAction()
    {
        Debug.Log("Admin gjorde något speciellt!");
    }

    private void DoRegularAction()
    {
        Debug.Log("Vanlig spelare tryckte på knappen.");
    }
}
