using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class Spawner : MonoBehaviour, INetworkRunnerCallbacks
{
    public NetworkPlayer playerPrefab;
    private PathfindingManager pathfindingManager;
    SessionListUIHandler sessionListUIHandler;
    [SerializeField] private GameObject loadingScreenPrefab;
    private GameObject loadingScreenInstance;
    private Slider loadingSlider;

    Dictionary<int, NetworkPlayer> mapTokenIDWithNetworkPlayer;



    public static Vector3 LastLocalPlayerPosition;

    public static int currenKontroll { get; set; }

    public static bool hasFinished { get; set; }
    public static bool canRunNextSegment { get; set; }
    public static int currentTargetIndex { get; set; }
    public static int currentKontroll { get; set; }
    public static List<Vector2Int> path { get; set; }

    public static bool shouldMiss;
    public static Transform ThisControll;
    public static Transform targetControl;
    public static bool isDrawing;
    public static bool timeToDraw;

    private void Awake()
    {
        mapTokenIDWithNetworkPlayer = new Dictionary<int, NetworkPlayer>();
        sessionListUIHandler = FindObjectOfType<SessionListUIHandler>(true);
    }


    private int GetPlayerToken(NetworkRunner runner, PlayerRef player)
    {
        if(runner.LocalPlayer == player)
        {
            return ConnectionTokenUtils.HashToken(GameManager.Instance.GetConnectionsToken());
        }
        else
        {
            var token = runner.GetPlayerConnectionToken(player);

            if(token != null)
            {
                return ConnectionTokenUtils.HashToken(token);
            }
            Debug.LogError("GetPlayerToken returned invalid token");
            return 0;
        }
    }

    public void SetConnectionTokenMapping(int token, NetworkPlayer networkPlayer)
    {
        mapTokenIDWithNetworkPlayer.Add(token, networkPlayer);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"OnPlayerJoined fired on {(runner.IsServer ? "SERVER" : "CLIENT")}, PlayerRef={player}");
        if (pathfindingManager == null)
        {
            pathfindingManager = FindObjectOfType<PathfindingManager>();
        }
        if (runner.IsServer || player == runner.LocalPlayer)
        {
            int playerToken = GetPlayerToken(runner, player);

            Debug.Log($"OnPlayerJoined we are server or local, Connection token {playerToken}");

            if (mapTokenIDWithNetworkPlayer.TryGetValue(playerToken, out NetworkPlayer networkPlayer))
            {
                if (networkPlayer != null && networkPlayer.gameObject != null)
                {
                    Debug.Log($"Found old connection token for token {playerToken}. Assigning controls to that player");

                    networkPlayer.GetComponent<NetworkObject>().AssignInputAuthority(player);
                    networkPlayer.Spawned();
                }
                else
                {
                    // Objektet är redan destruerat, spawn nytt istället
                    Debug.Log($"Old NetworkPlayer for token {playerToken} was destroyed. Spawning a new one.");
                    SpawnNewPlayer(runner, player, playerToken);
                }
            }
            else
            {
                SpawnNewPlayer(runner, player, playerToken);
            }

        }
        else
        {
            Debug.Log("OnPLayerJoined remote");
        }
    }

    private void SpawnNewPlayer(NetworkRunner runner, PlayerRef player, int playerToken)
    {
        bool isLobbyScene = ActiveScene.Name == "Lobby";
        Vector3 spawnPosition;

        if (isLobbyScene)
        {
            spawnPosition = NetworkPlayer.LobbySpot(runner, player);
        }
        else
        {
            // Single player starts straight in the competition scene, which may not have run its Awake yet
            if (pathfindingManager == null) pathfindingManager = FindObjectOfType<PathfindingManager>();
            spawnPosition = pathfindingManager != null ? pathfindingManager.startPoint : Vector3.zero;
        }

        NetworkPlayer spawnedNetworkPlayer = runner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
        spawnedNetworkPlayer.GetComponent<AgentMovement>().SetInitialPosition(spawnPosition);

        spawnedNetworkPlayer.token = playerToken;
        mapTokenIDWithNetworkPlayer[playerToken] = spawnedNetworkPlayer;
    }


    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        var tokenData = runner.GetPlayerConnectionToken(player);
        if (tokenData == null)
        {
            Debug.LogWarning($"Player {player.PlayerId} left but no connection token exists.");
            return; // Hoppa över
        }

        int playerToken = ConnectionTokenUtils.HashToken(tokenData);

        if (mapTokenIDWithNetworkPlayer.ContainsKey(playerToken))
        {
            mapTokenIDWithNetworkPlayer.Remove(playerToken);
        }
    }



    public void OnInput(NetworkRunner runner, NetworkInput input)
    {

    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("OnConnectedToServer");
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.Log("OnConnectFailed");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        Debug.Log("OnConnectRequest");
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"OnDisconnectedFromServer reason={reason}");
    }

    public async void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log("OnHostMigration");

        if (NetworkPlayer.Local != null)
        {
            LastLocalPlayerPosition = NetworkPlayer.Local.transform.position;
            Debug.Log($"Saved local position before migration: {LastLocalPlayerPosition}");
            var agent = NetworkPlayer.Local.GetComponent<AgentMovement>();
            currentKontroll = agent.currentKontroll;
            hasFinished = agent.hasFinished;
            canRunNextSegment = agent.canRunNextSegment;
            currentTargetIndex = agent.currentTargetIndex;
            path = agent.path;
            shouldMiss = agent.RunnerCollision.shouldMiss;
            ThisControll = agent.manualPathDrawer.ThisControll;
            targetControl = agent.manualPathDrawer.targetControl;
            isDrawing = agent.manualPathDrawer.isDrawing;
            timeToDraw = agent.manualPathDrawer.timeToDraw;
            Debug.Log("currenKontroll " + agent.currentKontroll);
            Debug.Log("currenTargetIndex " + agent.currentTargetIndex);

        }

        await runner.Shutdown(shutdownReason: ShutdownReason.HostMigration);

        FindObjectOfType<NetworkRunnerHandler>().StartHostMigration(hostMigrationToken);
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (loadingScreenInstance != null)
        {
            StartCoroutine(HideLoadingScreen());
        }
        Debug.Log("OnScenLoadDone happend");
        
    }

    private IEnumerator HideLoadingScreen()
    {
        yield return new WaitForSeconds(0.1f);

        if (loadingScreenInstance != null)
        {
            loadingScreenInstance.SetActive(false);
        }
    }

    public void OnSceneLoadProgress(NetworkRunner runner, float progress)
    {
        if (loadingSlider != null)
        {
            // Smidig progress (kan också dela med 0.9f om Fusion rapporterar 0.9 max)
            float smoothedProgress = Mathf.Clamp01(progress / 0.9f);
            loadingSlider.value = smoothedProgress;
        }
    }
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        // Om prefab finns och inte redan är instansierad, skapa en instans
        if (loadingScreenPrefab != null && loadingScreenInstance == null)
        {
            loadingScreenInstance = Instantiate(loadingScreenPrefab);

            // Hitta slider i den instansierade prefab:en
            loadingSlider = loadingScreenInstance.GetComponentInChildren<Slider>(true);
        }

        if (loadingScreenInstance != null)
        {
            loadingScreenInstance.SetActive(true);
            if (loadingSlider != null)
                loadingSlider.value = 0f;
        }
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        if(sessionListUIHandler == null)
        {
            return;
        }

        // The list has arrived, so a lobby can be created now. (This was only done when the list was empty,
        // so nobody could create a lobby while someone else had one open.)
        sessionListUIHandler.dontCreateGame = false;

        if(sessionList.Count == 0)
        {
            Debug.Log("Joined lobby no sessions found");

            sessionListUIHandler.OnNoSessionsFound();
        }
        else
        {
            sessionListUIHandler.ClearList();

            foreach(SessionInfo sessionInfo in sessionList)
            {
                sessionListUIHandler.AddToList(sessionInfo);

                Debug.Log($"Found session {sessionInfo.Name} playerCount {sessionInfo.PlayerCount}");
            }
        }
    }

    public void OnHostMigrationCleanUp()
    {
        foreach(KeyValuePair<int, NetworkPlayer> entry in mapTokenIDWithNetworkPlayer)
        {
            NetworkObject networkObjectInDictionary = entry.Value.GetComponent<NetworkObject>();

            if (networkObjectInDictionary.InputAuthority.IsNone)
            {
                networkObjectInDictionary.Runner.Despawn(networkObjectInDictionary);
            }
        }
        LastLocalPlayerPosition = Vector3.zero;
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("OnShoutDown" + " " + shutdownReason);
        // A lost connection or a lobby that could not be joined: take the player to a page that works
        SessionGuard.OnShutdown(runner, shutdownReason);
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }
}
