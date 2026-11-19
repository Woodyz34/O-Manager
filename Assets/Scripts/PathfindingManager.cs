using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

public class PathfindingManager : MonoBehaviour
{
    public int howManyAI = 0;
    public bool multiplayer = false;
    public Vector3 startPoint;
    public GameObject agentPrefab;
    public string jsonFileName;

    public GridData gridData;
    public float[,] speedGrid2D;
    private AgentMovement agentMovement;
    private GameObject agentInstance;
    private SpriteAlignerAuto spriteAligner;
    private GeoJsonSpawner objectSpawner; 
    public bool spritealigner = false;
    public List<Transform> controlPoints;
    public GameObject ChooseRunnerPanel;
    public GameObject BetOnRunnerPanel;
    public bool ChooseRunnerOf3Mode = false;
    public bool ChooseWhomeverYouWantMode = false;
    // Run with the runners the player owns (RunnerCollection)
    [HideInInspector] public bool YourRunnersMode = false;

    [HideInInspector] 
    public float cellSize;

    public Transform end;
    public Transform start;

    
    public bool DrawAllPath = false; // välj i Unity Inspector
    [HideInInspector]
    public bool AddObjects = true; // välj i Unity Inspector

    public Dictionary<string, float[,]> slopeFactors; // N, NE, E, SE, S, SW, W, NW

    public void Awake()
    {
        startPoint = controlPoints[0].transform.position;
        
        LoadGridData();
        // The course's out-of-bounds areas are closed cells for every runner, the A* routes and the drawn routes
        ForbiddenAreas.ApplyToCompetition(this);
        MoveControlsOntoRunnableGround();


    }


    public void Start()
    {
        spriteAligner = GetComponent<SpriteAlignerAuto>();
        objectSpawner = GetComponent<GeoJsonSpawner>();
        if (multiplayer)
        {
            // en for loop som skapar alla agents.
        }
        else
        {
            GameObject player = createNewPlayer(agentPrefab);
            agentMovement = player.GetComponent<AgentMovement>();
            agentMovement.useManualPath = true;
            for (int i = 0; i < howManyAI; i++)
            {
                GameObject AI = createNewPlayer(agentPrefab);
                AgentMovement AIMovement = AI.GetComponent<AgentMovement>();
                AIMovement.useManualPath = false;
                AIMovement.AI = true;
            }
        }

       

        



        /*
        // Skapa plattform som Plane och skala/positionera efter bounds
        GameObject platformGO = GameObject.CreatePrimitive(PrimitiveType.Plane);

        platformGO.transform.localScale = new Vector3(
            (gridData.bounds.maxx - gridData.bounds.minx) / 10f,
            1,
            (gridData.bounds.maxy - gridData.bounds.miny) / 10f);

        platformGO.transform.position = new Vector3(
            (gridData.bounds.minx + gridData.bounds.maxx) / 2f,
            0,
            (gridData.bounds.miny + gridData.bounds.maxy) / 2f);

        platformGO.name = "GridPlatform";

        // Ladda textur från Resources mapp (se till att den ligger där)
        Texture2D texture = Resources.Load<Texture2D>("KartBild/TestTävling1");
        if (texture != null)
        {
            var renderer = platformGO.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.mainTexture = texture;
            mat.mainTextureScale = new Vector2(1, 1);
            mat.mainTextureOffset = new Vector2(0, 0);

            // Rotera UV-koordinaterna 90° CCW
            Mesh mesh = platformGO.GetComponent<MeshFilter>().mesh;
            Vector2[] uvs = mesh.uv;
            for (int i = 0; i < uvs.Length; i++)
            {
                float u = uvs[i].x;
                uvs[i].x = uvs[i].y;
                uvs[i].y = 1f - u;
            }
            mesh.uv = uvs;

            renderer.material = mat;
        }
        else
        {
            Debug.LogError("Kunde inte ladda texturen från Resources/KartBild/TestTävling1");
        }
        */
        


        
        if (spritealigner)
        {
            spriteAligner.DoSpriteAlign(this);
            DrawGrid();
        }

        if(AddObjects)
        {
            objectSpawner.LoadJson(gridData.bounds, this);
        }
    }

  

    void LoadGridData()
    {
        var loadWatch = System.Diagnostics.Stopwatch.StartNew();
        float scaleFactor = 0.01f;  // <-- prova olika (5, 10, 20 osv)
        string gridName = jsonFileName.EndsWith(".json") ? jsonFileName.Substring(0, jsonFileName.Length - 5) : jsonFileName;

        // Imported maps (MapImport) keep their grid in a map package, in Resources or on disk
        if (MapPackages.IsPackagePath(gridName))
        {
            byte[] packageGrid = MapPackages.LoadBytes(gridName, ".bytes");
            if (packageGrid != null && GridBinary.TryRead(packageGrid, out GridData packageData, out float[,] packageSpeed, out Dictionary<string, float[,]> packageSlopes))
            {
                gridData = packageData;
                speedGrid2D = packageSpeed;
                slopeFactors = packageSlopes;
                // Dark green open in a forest race on a sprint map, shut in a sprint race (before the slope model,
                // which only converts the cells someone can run in)
                RaceGreen.Apply(gridName, speedGrid2D);
                TerrainSpeedModel.Apply(speedGrid2D, slopeFactors);
                Passages.Apply(gridName, speedGrid2D);
                Tunnels.Apply(gridName, speedGrid2D);
                cellSize = gridData.grid_size * scaleFactor;
                Debug.Log($"PathfindingManager: laddade kartpaketets grid {gridName} på {loadWatch.Elapsed.TotalSeconds:F2} s");
                return;
            }
            Debug.LogError("PathfindingManager: kunde inte läsa grid " + gridName);
            return;
        }

        // Läs den bakade binärfilen om den finns (Editor/GridBinaryBaker.cs skapar den från JSON-filen).
        // Den går mycket fortare att läsa än JSON-filen, som bara används som reserv.
        TextAsset binaryAsset = Resources.Load<TextAsset>("2d-gridJson/" + gridName + GridBinary.Suffix);
        if (binaryAsset != null)
        {
            bool loaded = GridBinary.TryRead(binaryAsset.bytes, out GridData binaryData, out float[,] binarySpeedGrid, out Dictionary<string, float[,]> binarySlopeFactors);
            Resources.UnloadAsset(binaryAsset);
            if (loaded)
            {
                gridData = binaryData;
                speedGrid2D = binarySpeedGrid;
                slopeFactors = binarySlopeFactors;
                TerrainSpeedModel.Apply(speedGrid2D, slopeFactors);
                cellSize = gridData.grid_size * scaleFactor;
                Debug.Log($"PathfindingManager: laddade grid-data (binärt) på {loadWatch.Elapsed.TotalSeconds:F2} s");
                return;
            }
            Debug.LogWarning("PathfindingManager: kunde inte läsa den binära grid-filen, läser JSON i stället.");
        }

        TextAsset jsonAsset = Resources.Load<TextAsset>("2d-gridJson/" + gridName);
        if (jsonAsset == null)
        {
            Debug.LogError("PathfindingManager: no grid called " + gridName + " (the maps are imported maps now)");
            return;
        }
        string jsonText = jsonAsset.text;
        gridData = JsonConvert.DeserializeObject<GridData>(jsonText);
        cellSize = gridData.grid_size * scaleFactor;

        // Konvertera slope_factors
        slopeFactors = new Dictionary<string, float[,]>();
        foreach (var kvp in gridData.slope_factors)
        {
            string dir = kvp.Key;
            float[][] jagged = kvp.Value;
            slopeFactors[dir] = ConvertJaggedArrayTo2D(jagged);
        }

        // Konvertera base_speed_grid
        speedGrid2D = ConvertJaggedArrayTo2D(gridData.base_speed_grid);
        TerrainSpeedModel.Apply(speedGrid2D, slopeFactors);
        Debug.Log($"PathfindingManager: laddade grid-data på {loadWatch.Elapsed.TotalSeconds:F2} s");
    }


    public Vector3 GridToWorldPosition(int i, int j)
    {
        int rows = speedGrid2D.GetLength(0);
        int cols = speedGrid2D.GetLength(1);

        // Beräkna total storlek
        float totalWidth = cols * cellSize;
        float totalHeight = rows * cellSize;   //  ändrat från *1.8f

        // Centrera genom att subtrahera halva bredden/ höjden
        float x = j * cellSize + cellSize / 2f - totalWidth / 2f;
        float y = i * cellSize + cellSize / 2f - totalHeight / 2f;   //  ändrat från *1.8f

        return new Vector3(x, y, -1f);
    }

    // A control on a cliff, a fence, a boulder or a lake shore is punched from the nearest cell a runner can run on
    // (CourseValidator.RunCell). The circle on the map stays where the course setter put it; only the point the
    // runners head for moves. Called once the grid is loaded, and by relays for each new leg.
    public void MoveControlsOntoRunnableGround()
    {
        if (speedGrid2D == null || controlPoints == null) return;
        foreach (Transform point in controlPoints)
        {
            if (point == null) continue;
            Vector2Int cell = WorldToGridPosition(point.position);
            Vector2Int run = CourseValidator.RunCell(speedGrid2D, cell);
            if (run == cell || run.x < 0) continue;
            Vector3 world = GridToWorldPosition(run.y, run.x);
            point.position = new Vector3(world.x, world.y, point.position.z);
        }
        if (controlPoints.Count > 0 && controlPoints[0] != null) startPoint = controlPoints[0].position;
    }

    // The grid cell (x = column, y = row) a world position is in
    public Vector2Int WorldToGridPositionCentered(Vector3 worldPos)
    {
        return WorldToGridPosition(worldPos);
    }

    Vector2Int WorldToGridPosition(Vector3 worldPos)
    {
        int rows = speedGrid2D.GetLength(0);
        int cols = speedGrid2D.GetLength(1);

        float totalWidth = cols * cellSize;
        float totalHeight = rows * cellSize;

        // Invertera centrerings-offseten (+ half extents), sen dela med cellSize
        int j = Mathf.FloorToInt((worldPos.x + totalWidth / 2f) / cellSize); // kolumn
        int i = Mathf.FloorToInt((worldPos.y + totalHeight / 2f) / cellSize); // rad

        j = Mathf.Clamp(j, 0, cols - 1);
        i = Mathf.Clamp(i, 0, rows - 1);
        return new Vector2Int(j, i); // x=kolumn=j, y=rad=i
    }




    float[,] ConvertJaggedArrayTo2D(float[][] jaggedArray)
    {
        int rows = jaggedArray.Length;
        int cols = jaggedArray[0].Length;
        float[,] array2D = new float[rows, cols];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                array2D[i, j] = jaggedArray[i][j];
        return array2D;
    }
    void DrawGrid()
    {
        int rows = speedGrid2D.GetLength(0);
        int cols = speedGrid2D.GetLength(1);

        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                float speed = speedGrid2D[i, j];

                // Skapa en rektangel (Quad)
                GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cell.transform.localScale = new Vector3(cellSize, cellSize, 1f); //  ändrat (ingen *1.8f)


                // Placera i världen
                Vector3 pos = GridToWorldPosition(i, j);
                cell.transform.position = new Vector3(pos.x, pos.y, 0f);

                // Färgsätt efter hastighet
                var renderer = cell.GetComponent<Renderer>();
                if (Mathf.Approximately(speed, 1f))
                    renderer.material.color = Color.white;
                else if (speed >= 0.8f && speed < 1f)
                    renderer.material.color = new Color(0.6f, 1f, 0.6f); // ljusgrön
                else if (speed >= 0.1f && speed < 0.8f)
                    renderer.material.color = new Color(0f, 0.5f, 0f); // mörkgrön
                else if (speed > 1f)
                    renderer.material.color = Color.black;
                else
                    renderer.material.color = Color.red; // fallback, för speed <= 0
            }
        }
    }
    public void DrawAllPaths(List<List<Vector2Int>> paths)
    {
        int pathIndex = 0;
        foreach (var path in paths)
        {
            GameObject pathObj = new GameObject($"Path_{pathIndex}");
            var line = pathObj.AddComponent<LineRenderer>();
            line.positionCount = path.Count;
            line.widthMultiplier = 0.05f;

            // Välj färg baserat på index
            Color col = Color.Lerp(Color.red, Color.blue, pathIndex / (float)paths.Count);
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = col;
            line.endColor = col;

            for (int i = 0; i < path.Count; i++)
            {
                Vector3 worldPos = GridToWorldPosition(path[i].y, path[i].x);
                worldPos.z = -1;
                line.SetPosition(i, worldPos);
            }

            pathIndex++;
        }
    }
    private GameObject createNewPlayer(GameObject prefab)
    {
        // Skapa en kopia av prefaben i världen
        GameObject agentObj = Instantiate(prefab, startPoint, Quaternion.identity);
       
        return agentObj;
    }
    public void RemoveLine()
    {
        agentMovement.manualPathDrawer.RemoveLastPoints();
    }
}
