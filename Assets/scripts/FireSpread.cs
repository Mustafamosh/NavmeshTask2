using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// DYNAMIC HAZARD MODEL 
// Fire spread (Cellular Automata) utilizing Physics bounds to map to a NavMesh environment.
// ============================================================================
public class FireSpread : MonoBehaviour
{
    [Header("Grid Setup")]
    public float cellSize = 2f;
    [Tooltip("If true, automatically sizes the grid to cover the floorObject.")]
    public bool autoCalculateGridSize = true;
    [Tooltip("Used if autoCalculateGridSize is false.")]
    public int cols = 20;
    public int rows = 20; 

    [Header("Environment Detection")]
    [Tooltip("The main floor object (used for boundaries and surface height).")]
    public GameObject floorObject;
    [Tooltip("Select the layers that represent walls/furniture. Fire will not spread here.")]
    public LayerMask obstacleLayer;
    [Tooltip("Height of the check box when scanning for walls.")]
    public float obstacleCheckHeight = 2f;

    [Header("Tick Rate")]
    public float tickInterval = 0.4f;

    [Header("Tuning Constants")]
    public float baseSpreadProb = 0.03f;
    public Vector2 draftVector = new Vector2(0f, 0.5f); 
    public float draftSpreadBonus = 0.02f;

    [Header("Visual Optimization")]
    [Tooltip("Number of logical cells per visual prefab. E.g. 4 means a 4x4 logic area gets 1 visual fire.")]
    public int visualChunkSize = 4;
    public GameObject firePrefab;

    [Header("Fire Behavior")]
    public bool allowFireExtinguishing = true;
    public int maxFires = 100;

    [Header("Logging")]
    public bool enableTickLog = true;
    public int logEveryNTicks = 10;

    // --- Internal State ---
    private float gridOriginX = 0f;
    private float gridOriginZ = 0f;
    private float floorSurfaceY = 0f;

    public enum FireState
    {
        UNBURNT = 0,
        EARLY_BURNING = 1,
        FULL_BURNING = 2,
        EXTINGUISHING = 3,
        BURNT = 4,
        WALL = 5
    }

    public struct HazardReport
    {
        public bool hazardous;
        public string status;
        public float severity;   
    }

    private FireState[,] fireGrid;
    private GameObject[,] chunkInstances;
    private int chunkCols;
    private int chunkRows;

    private float tickTimer = 0f;
    private int tickCount = 0;
    private int burningCellsCount = 0;

    private static readonly Vector2Int[] NEIGHBORS = new Vector2Int[]
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    void Start()
    {
        InitializeGridBounds();

        fireGrid = new FireState[cols, rows];

        BakeEnvironmentGrid();
        IgniteAtTransform();

        chunkCols = Mathf.CeilToInt((float)cols / visualChunkSize);
        chunkRows = Mathf.CeilToInt((float)rows / visualChunkSize);
        chunkInstances = new GameObject[chunkCols, chunkRows];

        UpdateFireSpawn();
    }

    // --- 1. DYNAMIC GRID SIZING ---
    void InitializeGridBounds()
    {
        if (floorObject != null)
        {
            floorSurfaceY = floorObject.transform.position.y;

            if (autoCalculateGridSize)
            {
                Collider floorCollider = floorObject.GetComponent<Collider>();
                if (floorCollider != null)
                {
                    Bounds b = floorCollider.bounds;
                    cols = Mathf.CeilToInt(b.size.x / cellSize);
                    rows = Mathf.CeilToInt(b.size.z / cellSize);
                    gridOriginX = b.min.x;
                    gridOriginZ = b.min.z;
                    floorSurfaceY = b.max.y; // Top of the floor mesh
                    return;
                }
            }
        }
        
        // Fallback to manual setup centered on this GameObject
        gridOriginX = transform.position.x - (cols * cellSize) / 2f;
        gridOriginZ = transform.position.z - (rows * cellSize) / 2f;
    }

    // --- 2. DYNAMIC WALL DETECTION ---
    void BakeEnvironmentGrid()
    {
        Vector3 halfExtents = new Vector3(cellSize / 2.1f, obstacleCheckHeight / 2f, cellSize / 2.1f);

        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                Vector3 cellCenter = CellCenter(x, z);
                // Shift center up slightly to check above the floor
                Vector3 checkCenter = new Vector3(cellCenter.x, floorSurfaceY + (obstacleCheckHeight / 2f), cellCenter.z);

                // If the overlap box hits an obstacle (wall/furniture), mark as WALL
                if (Physics.CheckBox(checkCenter, halfExtents, Quaternion.identity, obstacleLayer))
                {
                    fireGrid[x, z] = FireState.WALL;
                }
                else
                {
                    fireGrid[x, z] = FireState.UNBURNT;
                }
            }
        }
    }

    // --- 3. ALIGN IGNITION TO WORLD SPACE ---
    void IgniteAtTransform()
    {
        Vector3 startPos = transform.position;
        int startX = Mathf.RoundToInt((startPos.x - gridOriginX) / cellSize);
        int startZ = Mathf.RoundToInt((startPos.z - gridOriginZ) / cellSize);

        if (IsInBounds(startX, startZ) && fireGrid[startX, startZ] != FireState.WALL)
        {
            fireGrid[startX, startZ] = FireState.EARLY_BURNING;
        }
        else
        {
            Debug.LogWarning("FireSpread GameObject is placed out of bounds or inside a wall! Adjust its position.");
        }
    }

    void Update()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer >= tickInterval)
        {
            tickTimer -= tickInterval;
            SimulationTick();
        }
    }

    Vector3 CellCenter(int x, int z)
    {
        return new Vector3(gridOriginX + (x * cellSize) + (cellSize / 2f), floorSurfaceY + 0.01f, gridOriginZ + (z * cellSize) + (cellSize / 2f));
    }

    bool IsInBounds(int x, int z) => x >= 0 && x < cols && z >= 0 && z < rows;

    bool IsOpenCell(int x, int z)
    {
        if (!IsInBounds(x, z)) return false;
        return fireGrid[x, z] != FireState.WALL;
    }

    bool IsIgnitable(int x, int z)
    {
        return IsOpenCell(x, z) && fireGrid[x, z] == FireState.UNBURNT;
    }

    void SimulationTick()
    {
        FireState[,] nextFireGrid = (FireState[,])fireGrid.Clone();
        burningCellsCount = 0;

        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                FireState state = fireGrid[x, z];
                if (state == FireState.EARLY_BURNING || state == FireState.FULL_BURNING)
                    burningCellsCount++;

                if (state == FireState.EARLY_BURNING)
                {
                    if (Random.value < 0.08f) nextFireGrid[x, z] = FireState.FULL_BURNING;
                }
                else if (state == FireState.FULL_BURNING)
                {
                    if (allowFireExtinguishing && Random.value < 0.02f)
                    {
                        nextFireGrid[x, z] = FireState.EXTINGUISHING;
                    }

                    foreach (var n in NEIGHBORS)
                    {
                        int nx = x + n.x, nz = z + n.y;
                        if (IsIgnitable(nx, nz) && nextFireGrid[nx, nz] == FireState.UNBURNT)
                        {
                            float draftInfluence = n.x * draftVector.x + n.y * draftVector.y;
                            float prob = baseSpreadProb + draftInfluence * draftSpreadBonus;
                            if (Random.value < prob) nextFireGrid[nx, nz] = FireState.EARLY_BURNING;
                        }
                    }
                }
                else if (state == FireState.EXTINGUISHING)
                {
                    if (Random.value < 0.05f) nextFireGrid[x, z] = FireState.BURNT;
                }
            }
        }

        fireGrid = nextFireGrid;
        UpdateFireSpawn();

        tickCount++;
        if (enableTickLog && tickCount % logEveryNTicks == 0)
        {
            Debug.Log($"[Tick {tickCount}] Burning: {burningCellsCount} cells");
        }
    }

    void UpdateFireSpawn()
    {
        if (firePrefab == null) return;

        bool[,] chunkShouldBurn = new bool[chunkCols, chunkRows];
        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                FireState state = fireGrid[x, z];
                if (state == FireState.EARLY_BURNING || state == FireState.FULL_BURNING || state == FireState.EXTINGUISHING)
                {
                    int cx = x / visualChunkSize;
                    int cz = z / visualChunkSize;
                    if (cx >= 0 && cx < chunkCols && cz >= 0 && cz < chunkRows)
                        chunkShouldBurn[cx, cz] = true;
                }
            }
        }

        int currentFires = 0;
        for (int cx = 0; cx < chunkCols; cx++)
            for (int cz = 0; cz < chunkRows; cz++)
                if (chunkInstances[cx, cz] != null) currentFires++;

        for (int cx = 0; cx < chunkCols; cx++)
        {
            for (int cz = 0; cz < chunkRows; cz++)
            {
                if (chunkShouldBurn[cx, cz])
                {
                    if (chunkInstances[cx, cz] == null)
                    {
                        if (maxFires <= 0 || currentFires < maxFires)
                        {
                            float cxCenter = gridOriginX + (cx * visualChunkSize * cellSize) + ((visualChunkSize * cellSize) / 2f);
                            float czCenter = gridOriginZ + (cz * visualChunkSize * cellSize) + ((visualChunkSize * cellSize) / 2f);
                            Vector3 spawnPos = new Vector3(cxCenter, floorSurfaceY + 0.01f, czCenter);
                            chunkInstances[cx, cz] = Instantiate(firePrefab, spawnPos, Quaternion.identity, transform);
                            currentFires++;
                        }
                    }
                }
                else
                {
                    if (chunkInstances[cx, cz] != null)
                    {
                        Destroy(chunkInstances[cx, cz]);
                        chunkInstances[cx, cz] = null;
                    }
                }
            }
        }
    }

    public HazardReport IsHazardous3D(Vector3 position)
    {
        int gridX = Mathf.FloorToInt((position.x - gridOriginX) / cellSize);
        int gridZ = Mathf.FloorToInt((position.z - gridOriginZ) / cellSize);

        // Agents that walk entirely off the mapped grid are considered safe/escaped
        if (!IsInBounds(gridX, gridZ))
        {
             return new HazardReport { hazardous = false, status = "OUTSIDE_HAZARD_ZONE", severity = 0f };
        }

        FireState floorState = fireGrid[gridX, gridZ];

        if (floorState == FireState.WALL)
        {
            return new HazardReport { hazardous = true, status = "SOLID_WALL_COLLISION", severity = 1f };
        }

        if (position.y < 1.0f)
        {
            if (floorState == FireState.FULL_BURNING)
                return new HazardReport { hazardous = true, status = "DIRECT_FIRE_THERMAL", severity = 1.0f };
            if (floorState == FireState.EARLY_BURNING)
                return new HazardReport { hazardous = true, status = "DIRECT_FIRE_THERMAL", severity = 0.7f };
        }

        return new HazardReport { hazardous = false, status = "CLEAR_AIR", severity = 0f };
    }

    /// <summary>
    /// Returns how many cells are on fire this tick.
    /// Used by the logger to record how the hazard grows over time.
    /// </summary>
    public int GetBurningCellsCount()
    {
        return burningCellsCount;
    }
    
}