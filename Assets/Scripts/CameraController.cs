using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CameraController : MonoBehaviour
{
    public Camera cam;
    private PathfindingManager pathManager;

    [SerializeField]
    private float zoomStep, minCamSize, maxCamSize;

    [Header("Draw view")]
    [Tooltip("How much of the screen height the leg between two controls fills when it is time to draw. Lower means more zoomed out.")]
    [SerializeField, Range(0.3f, 0.9f)] private float legShareOfScreen = 0.6f;
    [Tooltip("The draw view is never more zoomed in than this, however short the leg is.")]
    [SerializeField] private float minLegCamSize = 4.5f;
    // The zoom the current leg needs. Scrolling out is always allowed at least this far.
    private float legCamSize;

    public SpriteRenderer mapRenderer;
    private float mapMinX, mapMaxX, mapMinY, mapMaxY;

    private Vector3 dragOrigin;


  
    Vector3 velocity = Vector3.zero;

    [Range(0, 1)]
    public float smoothTime;

    public Vector3 positionOffset;

    private bool canWrite = true;

    [Header("Följ löparen")]
    [Tooltip("Kamerans zoom när den följer löparen. Ändras om spelaren zoomar medan kameran följer.")]
    [SerializeField] private float followCamSize = 3f;
    private bool followRunner = false;
    private bool wasFollowing = false;
    private AgentMovement agent;
    private Button followButton;
    private TextMeshProUGUI followButtonText;


    public void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (SceneManager.GetActiveScene().name == "Lobby")
        {
            return;
        }
        InitializeCamera();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The object can be destroyed by another scene-loaded handler earlier in the same call (e.g. when the
        // network session shuts down as the scene changes); Unity then still calls this handler
        if (this == null)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            return;
        }
        if (scene.name == "Lobby") return;
        InitializeCamera();
    }

    private void InitializeCamera()
    {
        pathManager = FindObjectOfType<PathfindingManager>();
        GameObject mapObject = GameObject.FindGameObjectWithTag("MapImage");
        if (mapObject != null)
        {
            mapRenderer = mapObject.GetComponent<SpriteRenderer>();
            mapMinX = mapRenderer.transform.position.x - mapRenderer.bounds.size.x;
            mapMaxX = mapRenderer.transform.position.x + mapRenderer.bounds.size.x;

            mapMinY = mapRenderer.transform.position.y - mapRenderer.bounds.size.y;
            mapMaxY = mapRenderer.transform.position.y + mapRenderer.bounds.size.y;
        }
    }

    void Update()
    {
        if (cam == null || !cam.enabled)
        {
            return;
        }
        if (SceneManager.GetActiveScene().name == "Lobby")
        {
            return;
        }
        UpdateFollowButton();

        bool following = IsFollowingRunner();
        if (following && !wasFollowing)
        {
            // Byter från rit-vyn (eller översikten) till följ-vyn
            cam.orthographicSize = Mathf.Clamp(followCamSize, minCamSize, maxCamSize);
        }
        wasFollowing = following;

        // Before the competition has started the Choose a runner screen covers the map. Scrolling its list or
        // dragging on it used to zoom and move the camera behind it, so the first leg came up zoomed in.
        if (agent == null)
        {
            agent = GetComponent<AgentMovement>();
        }
        if (agent != null && agent.WaitingForAllPlayersToChooseRunner)
        {
            return;
        }

        if (!following && !EscMenu.IsOpen)
        {
            PanCamera();
        }
        
        // The wheel belongs to the list under the mouse (the leaderboard, for one), not to the map behind it
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel != 0f && !PointerOverUI() && !EscMenu.IsOpen)
        {
            if (wheel > 0f) { ZoomIn(); }
            else { ZoomOut(); }
        }

        if (following)
        {
            // Kom ihåg spelarens egen zoom till nästa gång kameran börjar följa
            followCamSize = cam.orthographicSize;
        }
    }

    // Same test as for drawing: where you cannot draw because the interface is in the way, you cannot zoom either
    private bool PointerOverUI()
    {
        return EventSystem.current != null && agent != null && agent.manualPathDrawer != null
            && agent.manualPathDrawer.IsPointerOverTaggedCanvas();
    }

    void LateUpdate()
    {
        if (cam == null || !cam.enabled || !IsFollowingRunner())
        {
            return;
        }
        // Efter att löparen har flyttats i Update, så att kameran inte ligger en bildruta efter
        cam.transform.position = new Vector3(transform.position.x, transform.position.y, -10f);
    }

    // Löparen springer (eller väntar på de andra) och spelaren ritar inte just nu
    private bool IsRunPhase()
    {
        if (agent == null)
        {
            agent = GetComponent<AgentMovement>();
        }
        return agent != null
            && agent.manualPathDrawer != null
            && !agent.manualPathDrawer.timeToDraw
            && !agent.WaitingForAllPlayersToChooseRunner
            && !agent.hasFinished;
    }

    private bool IsFollowingRunner()
    {
        return followRunner && IsRunPhase();
    }

    public void ToggleFollowRunner()
    {
        followRunner = !followRunner;
        RefreshFollowButtonText();

        if (!followRunner && IsRunPhase())
        {
            // Tillbaka till översikten över sträckan när följ-läget stängs av mitt i löpningen
            FocusOnCurrentLeg();
        }
    }

    // A new stretch has started while running (relays run the whole leg without stopping): turn the map so the next
    // control is straight up. When following the runner only the turn is taken, not the zoom.
    public void FocusOnNextStretch()
    {
        if (cam == null || agent == null) return;
        if (IsFollowingRunner())
        {
            if (pathManager == null) pathManager = FindObjectOfType<PathfindingManager>();
            int kontroll = agent.currentKontroll;
            if (pathManager == null || kontroll < 1 || kontroll >= pathManager.controlPoints.Count) return;
            Vector3 dir = pathManager.controlPoints[kontroll].position - pathManager.controlPoints[kontroll - 1].position;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            cam.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
            return;
        }
        FocusOnCurrentLeg();
    }

    private void FocusOnCurrentLeg()
    {
        if (pathManager == null)
        {
            pathManager = FindObjectOfType<PathfindingManager>();
        }
        int kontroll = agent.currentKontroll;
        if (pathManager == null || kontroll < 1 || kontroll >= pathManager.controlPoints.Count)
        {
            return;
        }
        FocusOnSegment(
            agent.WorldToGridPositionCentered(pathManager.controlPoints[kontroll - 1].position),
            agent.WorldToGridPositionCentered(pathManager.controlPoints[kontroll].position)
        );
    }

    // Knappen skapas i koden så att den finns i alla banor utan att varje scen behöver ändras.
    // Den visas bara medan löparen springer, inte medan spelaren ritar.
    private void UpdateFollowButton()
    {
        if (followButton == null)
        {
            CreateFollowButton();
            if (followButton == null)
            {
                return;
            }
        }

        bool show = IsRunPhase();
        if (followButton.gameObject.activeSelf != show)
        {
            followButton.gameObject.SetActive(show);
        }
    }

    private void CreateFollowButton()
    {
        GameObject canvasObject = GameObject.FindGameObjectWithTag("Canvas");
        if (canvasObject == null)
        {
            return;
        }

        // Use the styled button that is already in the scene when there is one
        Transform existing = canvasObject.transform.Find("FollowRunnerButton");
        if (existing != null)
        {
            followButton = existing.GetComponent<Button>();
            followButtonText = existing.GetComponentInChildren<TextMeshProUGUI>(true);
            followButton.onClick.RemoveListener(ToggleFollowRunner);
            followButton.onClick.AddListener(ToggleFollowRunner);
            RefreshFollowButtonText();
            existing.gameObject.SetActive(false);
            return;
        }

        GameObject buttonObject = new GameObject("FollowRunnerButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-640f, 442f); // uppe till vänster, bredvid ångra-knappen
        rect.sizeDelta = new Vector2(240f, 70f);

        GameObject textObject = new GameObject("Text (TMP)", typeof(RectTransform));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        followButtonText = textObject.AddComponent<TextMeshProUGUI>();
        followButtonText.alignment = TextAlignmentOptions.Center;
        followButtonText.fontSize = 26f;
        followButtonText.color = Color.black;
        followButtonText.raycastTarget = false;

        followButton = buttonObject.GetComponent<Button>();
        followButton.onClick.AddListener(ToggleFollowRunner);
        RefreshFollowButtonText();
        buttonObject.SetActive(false);
    }

    private void RefreshFollowButtonText()
    {
        if (followButtonText != null)
        {
            followButtonText.text = followRunner ? "Follow runner: ON" : "Follow runner: OFF";
        }
        if (followButton != null)
        {
            followButton.image.color = followRunner ? new Color(0.6f, 0.9f, 0.6f, 1f) : Color.white;
        }
    }

    public void FocusOnSegment(Vector2Int start, Vector2Int end)
    {
        Debug.Log(start);
        Debug.Log(end);
        Debug.Log(pathManager);
        Debug.Log(cam);
        if(pathManager == null)
        {
            pathManager = FindObjectOfType<PathfindingManager>();
        }
        Vector3 startPos = pathManager.GridToWorldPosition(start.y, start.x);
        Vector3 endPos = pathManager.GridToWorldPosition(end.y, end.x);

        // riktning och mittpunkt
        Vector3 dir = (endPos - startPos).normalized;
        Vector3 midpoint = (startPos + endPos) / 2f;
        float distance = Vector3.Distance(startPos, endPos);

        // beräkna vinkel för rotation (segment blir "lodrätt" i kamera)
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        cam.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);

        // placera kameran på mitten mellan punkterna
        Vector3 camPos = midpoint;
        camPos.z = -10f;
       


        
        // Zoom so the whole leg fits with room to spare at both ends. The leg runs straight up the screen,
        // so only the height matters. The size is worked out directly: this used to step the zoom in and out
        // from wherever it was, which could end one step too tight and gave a different view each time.
        legCamSize = Mathf.Max(minLegCamSize, distance / (2f * legShareOfScreen));
        // Start two scroll steps closer than the whole stretch (the player can scroll out to see all of it)
        cam.orthographicSize = Mathf.Max(minCamSize, legCamSize - 2f * zoomStep);
        cam.transform.position = camPos;
    }

    private void PanCamera()
    {

        if (Input.GetMouseButtonDown(1))
            dragOrigin = cam.ScreenToWorldPoint(Input.mousePosition);

        if (Input.GetMouseButton(1))
        {
            Vector3 difference = dragOrigin - cam.ScreenToWorldPoint(Input.mousePosition);



            cam.transform.position = ClampCamera(cam.transform.position + difference);

        }


    }

    private void ZoomIn()
    {
        float newSize = cam.orthographicSize - zoomStep;

        cam.orthographicSize = Mathf.Clamp(newSize, minCamSize, MaxZoomOut());
        cam.transform.position = ClampCamera(cam.transform.position);
    }

    private void ZoomOut()
    {
        float newSize = cam.orthographicSize + zoomStep;
        cam.orthographicSize = Mathf.Clamp(newSize, minCamSize, MaxZoomOut());
        cam.transform.position = ClampCamera(cam.transform.position);
    }

    // How far out the player may scroll: as wide as the map, and always far enough to see the whole
    // current leg. Without the second part one scroll step on a long leg pushed its ends off the screen.
    private float MaxZoomOut()
    {
        float mapLimit = (mapRenderer.bounds.size.x / 2f) / cam.aspect;
        return Mathf.Min(maxCamSize, Mathf.Max(mapLimit, legCamSize));
    }

    private Vector3 ClampCamera(Vector3 targetPosition)
    {
        float camHeight = cam.orthographicSize;
        float camWidth = cam.orthographicSize * cam.aspect;

        float minX = mapMinX + camWidth;
        float maxX = mapMaxX - camWidth;
        float minY = mapMinY + camHeight;
        float maxY = mapMaxY - camHeight;

        float newX = Mathf.Clamp(targetPosition.x, minX, maxX);
        float newY = Mathf.Clamp(targetPosition.y, minY, maxY);

        return new Vector3(newX, newY, targetPosition.z);
    }
    public void CanWrite()
    {
        
        canWrite = true;
        
    }
    public void CantWrite()
    {
        
        canWrite = false;
        
    }
}
