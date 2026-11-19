using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Unity.Collections.Unicode;

public class NetworkRunnerHandler : MonoBehaviour
{

    public NetworkRunner networkRunnerPrefab;

    NetworkRunner networkRunner;


    private void Awake()
    {
        NetworkRunner networkRunnerInScene = FindObjectOfType<NetworkRunner>();

        if(networkRunnerInScene != null)
        {
            networkRunner = networkRunnerInScene;
        }
    }

    INetworkSceneManager GetSceneManager(NetworkRunner runner)
    {
        // Se om det redan finns en scene manager på runnern
        var sceneManager = runner.GetComponents(typeof(MonoBehaviour)).OfType<INetworkSceneManager>().FirstOrDefault();

        // Om inte, lägg till en standard
        if (sceneManager == null)
        {
            sceneManager = runner.gameObject.AddComponent<SafeNetworkSceneManager>();
            Debug.Log("[NetworkRunnerHandler] Added SafeNetworkSceneManager");
        }
        else
        {
            Debug.Log("[NetworkRunnerHandler] Using existing SceneManager: " + sceneManager.GetType().Name);
        }

        return sceneManager;
    }

    // Start is called before the first frame update
    void Start()
    {
        if(networkRunner == null)
        {
            networkRunner = Instantiate(networkRunnerPrefab);
            networkRunner.name = "Network runner";

            if(SceneManager.GetActiveScene().name != "MultiPlayer")
            {
                var clientTask = InitializeNetworkRunner(networkRunner, GameMode.Shared, GameManager.Instance.GetConnectionsToken(), "TestLobby", NetAddress.Any(), SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex), null);
            }

            Debug.Log($"Server NetworkRunner started");
        }
    }

    public void StartHostMigration(HostMigrationToken hostMigrationToken)
    {
        networkRunner = Instantiate(networkRunnerPrefab);
        networkRunner.name = "Network runner";

        var clientTask = InitializeNetworkRunnerHostMigration(networkRunner, hostMigrationToken);
        
        Debug.Log($"Host migration started");
    }
    // The most players one lobby holds (100 since 2026-11-19, the user wanted it just because Photon's free 100 CCU plan allows it).
    // Photon's 100 CCU are for all lobbies together: when that is reached nobody is thrown out, a new player just gets the "multiplayer is full" message (SessionGuard).
    public const int MaxPlayersPerLobby = 100;

    protected virtual async Task InitializeNetworkRunner(NetworkRunner runner, GameMode gameMode, byte[] connectionToken, string sessionName, NetAddress address, SceneRef  scene, Action<NetworkRunner> initialized)
    {
        var sceneManager = GetSceneManager(runner);

        runner.ProvideInput = true; // Notera: ProvideInput används oftast för Host mode, men spelar ingen roll om det är True.

        StartGameResult result = await runner.StartGame(new StartGameArgs
        {
            Address = address,
            GameMode = gameMode, // Nu kommer detta vara GameMode.Shared
            Scene = scene,
            SessionName = sessionName,
            CustomLobbyName = "OurLobbyID",
            PlayerCount = MaxPlayersPerLobby,
            SceneManager = sceneManager,
            ConnectionToken = connectionToken
        });
        if (!result.Ok)
        {
            // Full, already racing, gone, or no internet: back to the lobby list with the reason
            // (the page used to stay on "Creating lobby" forever)
            Debug.LogWarning($"Could not start or join {sessionName}: {result.ShutdownReason} {result.ErrorMessage}");
            SessionGuard.OnShutdown(runner, result.ShutdownReason);
        }
    }

    protected virtual Task InitializeNetworkRunnerHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        var sceneManager = GetSceneManager(runner);

        runner.ProvideInput = true;

        return runner.StartGame(new StartGameArgs
        {
            SceneManager = sceneManager,
            HostMigrationToken = hostMigrationToken,
            HostMigrationResume = HostMigrationResume,
            ConnectionToken = GameManager.Instance.GetConnectionsToken()
        });
    }

    void HostMigrationResume(NetworkRunner runner)
    {
        Debug.Log("HostMigrationResume started");

        var spawner = FindObjectOfType<Spawner>();
        int localToken = ConnectionTokenUtils.HashToken(GameManager.Instance.GetConnectionsToken());

        foreach (var resumeNetworkObject in runner.GetResumeSnapshotNetworkObjects())
        {
            if (resumeNetworkObject.TryGetBehaviour<AgentMovement>(out var characterController))
            {
                Vector3 spawnPos = characterController.transform.position;
                Debug.Log("spawnPos" + spawnPos);
                Quaternion spawnRot = characterController.transform.rotation;

                runner.Spawn(resumeNetworkObject, position: spawnPos, rotation: spawnRot, onBeforeSpawned: (r, newNetworkObject) =>
                {
                    Debug.Log("pos" + runner.transform.position);
                    // Kopiera snapshot-state först
                    newNetworkObject.CopyStateFrom(resumeNetworkObject);

                    if (resumeNetworkObject.TryGetBehaviour<NetworkPlayer>(out var oldNetworkPlayer))
                    {
                        var newPlayer = newNetworkObject.GetComponent<NetworkPlayer>();
                        spawner?.SetConnectionTokenMapping(oldNetworkPlayer.token, newPlayer);

                        // Om detta är vår lokala token och vi har blivit host (server)
                        if (runner.IsServer && oldNetworkPlayer.token == localToken)
                        {
                            Vector3 restoredPos = Spawner.LastLocalPlayerPosition;
                            Debug.Log("restoredPos" + restoredPos);
                            if (newPlayer != null)
                            { 
               
                                newPlayer.RestoredPosition = restoredPos;
                                newPlayer.HasRestoredPosition = true;
                                newPlayer.hasFinished = Spawner.hasFinished;
                                newPlayer.canRunNextSegment = Spawner.canRunNextSegment;
                                newPlayer.currentTargetIndex = Spawner.currentTargetIndex;
                                newPlayer.currentKontroll = Spawner.currentKontroll;
                                newPlayer.path = Spawner.path;
                                newPlayer.shouldMiss = Spawner.shouldMiss;
                                newPlayer.ThisControll = Spawner.ThisControll;
                                newPlayer.targetControl = Spawner.targetControl;
                                newPlayer.isDrawing = Spawner.isDrawing;
                                newPlayer.timeToDraw = Spawner.timeToDraw;
                                Debug.Log("currenttragetindex " + Spawner.currentTargetIndex);
                                Debug.Log("currentkontroll " + Spawner.currentKontroll);
                            }

                            // Sätt också transform/agent direkt för att undvika hopp
                            newNetworkObject.transform.position = restoredPos;
                            Debug.Log("posRunner" + runner.transform.position);
                            Debug.Log("posNewNetworkObject" + newNetworkObject.transform.position);
                            var agent = newPlayer != null ? newPlayer.GetComponent<AgentMovement>() : null;
                            if (agent != null)
                            {
                                Debug.Log("AgentIsNotNull");
                                agent.SetInitialPosition(restoredPos);
                                Debug.Log("posRunner2" + runner.transform.position);
                                Debug.Log("posNewNetworkObject2" + newNetworkObject.transform.position);
                            }

                            Debug.Log($"[HostMigrationResume] Applied RestoredPosition for local token: {restoredPos}");
                        }
                    }
                });
            }
        }

        StartCoroutine(CleanUpHostMigrationCO());
        Debug.Log("HostMigrationResume completed");
    }



    IEnumerator CleanUpHostMigrationCO()
    {
        yield return new WaitForSeconds(10.0f);

        FindObjectOfType<Spawner>().OnHostMigrationCleanUp();
    }

    public void OnJoinLobby()
    {
        var clientTask = JoinLobby();
    }
    private async Task JoinLobby()
    {
        Debug.Log("JoinLobby started");

        string lobbyID = "OurLobbyID";

        var result = await networkRunner.JoinSessionLobby(SessionLobby.Custom, lobbyID);

        if (!result.Ok)
        {
            Debug.Log($"Unable to join lobby {lobbyID}: {result.ShutdownReason}");
            if (result.ShutdownReason == ShutdownReason.MaxCcuReached) SessionGuard.MultiplayerIsFull();
            else UIToast.Show("Could not reach the game servers. Check the internet connection and try again.");
        }
        else
        {
            Debug.Log("JoinLobby ok");
        }
    }

    public void CreateGame(string sessionName, string sceneName)
    {
        Debug.Log($"Create session {sessionName} scene {sceneName} build index {SceneUtility.GetBuildIndexByScenePath($"scenes/{sceneName}")}");

        var clientTask = InitializeNetworkRunner(networkRunner, GameMode.Shared, GameManager.Instance.GetConnectionsToken(), sessionName, NetAddress.Any(), SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath($"scenes/{sceneName}")), null);
    }

    public void JoinGame(SessionInfo sessionInfo)
    {
        Debug.Log($"Join session {sessionInfo.Name}");

        var clientTask = InitializeNetworkRunner(networkRunner, GameMode.Shared, GameManager.Instance.GetConnectionsToken(), sessionInfo.Name, NetAddress.Any(), SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex), null);
    }
  
}
