// FireSpread.cs
//
// CHANGE IN THIS VERSION
//   One addition only. A new public method GetHeatLevel(Vector3) reports how much
//   fire heat is present at a world position, checking that cell AND the eight
//   cells around it.
//
//   The neighbour check is the important part. BakeEnvironmentGrid marks any cell
//   containing a wall as FireState.WALL, and wall cells are never ignitable, so a
//   wall cell never burns. If BurnableSurface only read the cell a wall sits in, it
//   would always read zero and walls would never scorch. Reading the surrounding
//   cells is what lets a wall char from the fire burning next to it.
//
//   Nothing else in this file changed. The fire model, the smoke model, the grid,
//   and the visuals are all exactly as they were.
using System.Collections.Generic;
using UnityEngine;

public class FireSpread : MonoBehaviour
{
    [Header("Grid Setup")]
    public float cellSize = 2f;
    public bool autoCalculateGridSize = true;
    public int cols = 20;
    public int rows = 20;

    [Header("Environment Detection")]
    public GameObject floorObject;
    public LayerMask obstacleLayer;
    public float obstacleCheckHeight = 2f;

    [Header("Tick Rate")]
    public float tickInterval = 0.4f;

    [Header("Tuning Constants")]
    public float baseSpreadProb = 0.03f;
    public Vector2 draftVector = new Vector2(0f, 0.5f);
    public float draftSpreadBonus = 0.02f;

    [Header("Smoke Model")]
    public float earlyBurnSmokeAmount = 0.05f;
    public float fullBurnSmokeAmount = 0.1f;
    public float smokeSpreadFactor = 0.9f;
    public float smokeFadeFactor = 0.98f;

    [Header("Visual Optimization")]
    public int visualChunkSize = 4;
    public GameObject firePrefab;

    [Header("Fire Behavior")]
    public bool allowFireExtinguishing = true;
    public bool enableFireStart = true;
    public int maxFires = 100;

    [Header("Logging")]
    public bool enableTickLog = true;
    public int logEveryNTicks = 10;

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
    private float[,] smokeGrid;

    private GameObject[,] chunkInstances;
    private int chunkCols;
    private int chunkRows;

    private float tickTimer = 0f;
    private int tickCount = 0;
    private int burningCellsCount = 0;

    private static readonly Vector2Int[] NEIGHBORS = new Vector2Int[]
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    void Start()
    {
        InitializeGridBounds();

        fireGrid = new FireState[cols, rows];
        smokeGrid = new float[cols, rows];

        BakeEnvironmentGrid();
        
        if (enableFireStart)
        {
            IgniteAtTransform();
        }

        chunkCols = Mathf.CeilToInt((float)cols / visualChunkSize);
        chunkRows = Mathf.CeilToInt((float)rows / visualChunkSize);
        chunkInstances = new GameObject[chunkCols, chunkRows];

        UpdateFireSpawn();
    }

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
                    floorSurfaceY = b.max.y;
                    return;
                }
            }
        }

        gridOriginX = transform.position.x - (cols * cellSize) / 2f;
        gridOriginZ = transform.position.z - (rows * cellSize) / 2f;
    }

    int GetObstacleMask()
    {
        int mask = obstacleLayer.value;
        int ignoreLayer = LayerMask.NameToLayer("Ignore");

        if (ignoreLayer >= 0)
            mask |= 1 << ignoreLayer;

        return mask;
    }

    void BakeEnvironmentGrid()
    {
        Vector3 halfExtents = new Vector3(cellSize / 2.1f, obstacleCheckHeight / 2f, cellSize / 2.1f);
        int obstacleMask = GetObstacleMask();

        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                Vector3 cellCenter = CellCenter(x, z);
                Vector3 checkCenter = new Vector3(cellCenter.x, floorSurfaceY + (obstacleCheckHeight / 2f), cellCenter.z);

                if (Physics.CheckBox(checkCenter, halfExtents, Quaternion.identity, obstacleMask))
                    fireGrid[x, z] = FireState.WALL;
                else
                    fireGrid[x, z] = FireState.UNBURNT;
            }
        }
    }

    void IgniteAtTransform()
    {
        Vector3 startPos = transform.position;
        int startX = Mathf.RoundToInt((startPos.x - gridOriginX) / cellSize);
        int startZ = Mathf.RoundToInt((startPos.z - gridOriginZ) / cellSize);

        if (IsInBounds(startX, startZ) && fireGrid[startX, startZ] != FireState.WALL)
            fireGrid[startX, startZ] = FireState.EARLY_BURNING;
        else
            Debug.LogWarning("FireSpread GameObject is placed out of bounds or inside a wall. Adjust its position.");
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
        return new Vector3(
            gridOriginX + (x * cellSize) + (cellSize / 2f),
            floorSurfaceY + 0.01f,
            gridOriginZ + (z * cellSize) + (cellSize / 2f)
        );
    }

    bool IsInBounds(int x, int z)
    {
        return x >= 0 && x < cols && z >= 0 && z < rows;
    }

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
                    if (Random.value < 0.08f)
                        nextFireGrid[x, z] = FireState.FULL_BURNING;
                }
                else if (state == FireState.FULL_BURNING)
                {
                    if (allowFireExtinguishing && Random.value < 0.02f)
                        nextFireGrid[x, z] = FireState.EXTINGUISHING;

                    foreach (var n in NEIGHBORS)
                    {
                        int nx = x + n.x;
                        int nz = z + n.y;

                        if (IsIgnitable(nx, nz) && nextFireGrid[nx, nz] == FireState.UNBURNT)
                        {
                            float draftInfluence = n.x * draftVector.x + n.y * draftVector.y;
                            float prob = baseSpreadProb + draftInfluence * draftSpreadBonus;

                            if (Random.value < prob)
                                nextFireGrid[nx, nz] = FireState.EARLY_BURNING;
                        }
                    }
                }
                else if (state == FireState.EXTINGUISHING)
                {
                    if (Random.value < 0.05f)
                        nextFireGrid[x, z] = FireState.BURNT;
                }
            }
        }

        fireGrid = nextFireGrid;
        UpdateSmoke();
        UpdateFireSpawn();

        tickCount++;

        if (enableTickLog && tickCount % logEveryNTicks == 0)
            Debug.Log($"[Tick {tickCount}] Burning: {burningCellsCount} cells");
    }

    void UpdateSmoke()
    {
        float[,] nextSmoke = (float[,])smokeGrid.Clone();

        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                if (fireGrid[x, z] == FireState.EARLY_BURNING)
                    nextSmoke[x, z] = Mathf.Min(0.6f, nextSmoke[x, z] + earlyBurnSmokeAmount);

                if (fireGrid[x, z] == FireState.FULL_BURNING)
                    nextSmoke[x, z] = Mathf.Min(1f, nextSmoke[x, z] + fullBurnSmokeAmount);

                foreach (var n in NEIGHBORS)
                {
                    int nx = x + n.x;
                    int nz = z + n.y;

                    if (IsInBounds(nx, nz) && fireGrid[nx, nz] != FireState.WALL)
                    {
                        nextSmoke[nx, nz] = Mathf.Max(
                            nextSmoke[nx, nz],
                            smokeGrid[x, z] * smokeSpreadFactor
                        );
                    }
                }

                nextSmoke[x, z] *= smokeFadeFactor;
            }
        }

        smokeGrid = nextSmoke;
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

                if (state == FireState.EARLY_BURNING ||
                    state == FireState.FULL_BURNING ||
                    state == FireState.EXTINGUISHING)
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
                if (chunkInstances[cx, cz] != null)
                    currentFires++;

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

    public float GetSmokeLevel(Vector3 position)
    {
        int gridX = Mathf.FloorToInt((position.x - gridOriginX) / cellSize);
        int gridZ = Mathf.FloorToInt((position.z - gridOriginZ) / cellSize);

        if (!IsInBounds(gridX, gridZ))
            return 0f;

        if (fireGrid[gridX, gridZ] == FireState.WALL)
            return 0f;

        FireState state = fireGrid[gridX, gridZ];

        if (state == FireState.FULL_BURNING)
            return 1.0f;

        if (state == FireState.EARLY_BURNING)
            return 0.7f;

        if (state == FireState.EXTINGUISHING)
            return Mathf.Max(smokeGrid[gridX, gridZ], 0.4f);

        return smokeGrid[gridX, gridZ];
    }

    // ==========================================================
    // NEW: heat lookup for scorching surfaces
    // ==========================================================

    /// <summary>
    /// How much fire heat is present at a world position, from 0 for no heat up to
    /// 1 for a fully burning cell.
    ///
    /// This checks the cell at the position AND the eight cells surrounding it.
    /// The surrounding check exists because a wall always sits in a cell marked
    /// FireState.WALL, and wall cells never ignite, so a wall would otherwise read
    /// zero heat forever and never scorch. Reading the neighbours lets a wall char
    /// from the fire burning beside it.
    ///
    /// A cell that has already finished burning returns no heat, since char is
    /// permanent and is stored on the surface itself rather than recomputed here.
    /// </summary>
    public float GetHeatLevel(Vector3 position)
    {
        if (fireGrid == null) return 0f;

        int gridX = Mathf.FloorToInt((position.x - gridOriginX) / cellSize);
        int gridZ = Mathf.FloorToInt((position.z - gridOriginZ) / cellSize);

        float highest = 0f;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int nx = gridX + dx;
                int nz = gridZ + dz;

                if (!IsInBounds(nx, nz)) continue;

                float heat = HeatForState(fireGrid[nx, nz]);

                // The cell the surface actually sits in counts fully. Neighbouring
                // cells count for slightly less, so a wall directly in the fire
                // blackens faster than one merely beside it.
                if (dx != 0 || dz != 0)
                    heat *= 0.8f;

                if (heat > highest) highest = heat;
            }
        }

        return highest;
    }

    private float HeatForState(FireState state)
    {
        switch (state)
        {
            case FireState.FULL_BURNING: return 1.0f;
            case FireState.EARLY_BURNING: return 0.6f;
            case FireState.EXTINGUISHING: return 0.4f;
            default: return 0f;   // UNBURNT, BURNT, and WALL give off no heat
        }
    }

    public HazardReport IsHazardous3D(Vector3 position)
    {
        int gridX = Mathf.FloorToInt((position.x - gridOriginX) / cellSize);
        int gridZ = Mathf.FloorToInt((position.z - gridOriginZ) / cellSize);

        if (!IsInBounds(gridX, gridZ))
            return new HazardReport { hazardous = false, status = "OUTSIDE_HAZARD_ZONE", severity = 0f };

        FireState floorState = fireGrid[gridX, gridZ];

        if (floorState == FireState.WALL)
            return new HazardReport { hazardous = true, status = "SOLID_WALL_COLLISION", severity = 1f };

        if (position.y < 1.0f)
        {
            if (floorState == FireState.FULL_BURNING)
                return new HazardReport { hazardous = true, status = "DIRECT_FIRE_THERMAL", severity = 1.0f };

            if (floorState == FireState.EARLY_BURNING)
                return new HazardReport { hazardous = true, status = "DIRECT_FIRE_THERMAL", severity = 0.7f };
        }

        return new HazardReport { hazardous = false, status = "CLEAR_AIR", severity = 0f };
    }

    public int GetBurningCellsCount()
    {
        return burningCellsCount;
    }
}