using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// TASK 3 — HAZARD MODEL (Unity C# port of Version 4, JS prototype)
// Fire spread (Cellular Automata) + smoke logging (zone model fed by a
// per-cell smoke data grid). Data layer mirrors index.js 1:1.
//
// Attach to an empty GameObject. Assign materials in the Inspector.
// Ignition point is set in the Inspector (ignitionX, ignitionZ).
// ============================================================================
public class FireSpread : MonoBehaviour
{
    // --- Grid / scene config ---
    [Header("Grid Setup")]
    public float cellSize = 2f;
    public int cols = 20;
    public int rows = 20; // Z-axis. +Z side holds the doorway.

    

    [Header("Doorway (single source of truth)")]
    public int doorwayZEdge; // set in Start() = rows - 1
    public int doorwayXStart = 8;
    public int doorwayXEnd = 11;

    [Header("Ignition Point (choose where the hazard begins)")]
    [Tooltip("Grid X coordinate of the initial fire cell")]
    public int ignitionX = 10;
    [Tooltip("Grid Z coordinate of the initial fire cell")]
    public int ignitionZ = 3;

    [Header("Tick Rate")]
    public float tickInterval = 0.4f; // seconds, matches setInterval(tick, 400)

    [Header("Tuning Constants")]
    public float baseSpreadProb = 0.03f;
    public Vector2 draftVector = new Vector2(0f, 0.5f); // (x, z)
    public float draftSpreadBonus = 0.02f;

    // smoke parameters (removed)

    [Header("Fire Visuals")]
    public GameObject firePrefab;

    [Header("Floor Surface")]
    [Tooltip("Optional floor GameObject that defines the world surface where fire can spread.")]
    public GameObject floorObject;
    [Tooltip("When true, only cells that hit the floor object's collider will be considered valid spread cells.")]
    public bool requireFloorSurface = false;
    [Tooltip("Height above the floor from which to raycast downward when validating floor cells.")]
    public float floorSurfaceRaycastHeight = 2f;

    // Internal world origin for the grid (world coord of grid cell 0,0)
    private float gridOriginX = 0f;
    private float gridOriginZ = 0f;
    private float floorSurfaceY = 0f;

    [Header("Fire Behavior")]
    [Tooltip("When true, fire can transition from FULL_BURNING to EXTINGUISHING and eventually BURNT.")]
    public bool allowFireExtinguishing = true;
    [Tooltip("Maximum number of active fire instances allowed in the scene. Set 0 for unlimited.")]
    public int maxFires = 100;

    [Header("Logging")]
    public bool enableTickLog = true;
    public int logEveryNTicks = 10;

    // --- Fire states ---
    public enum FireState
    {
        UNBURNT = 0,
        EARLY_BURNING = 1,
        FULL_BURNING = 2,
        EXTINGUISHING = 3,
        BURNT = 4,
        WALL = 5,
        DOOR = 6 // walkable opening in the boundary; vents smoke; acts as EXIT
    }

    // --- Hazard report struct (returned by IsHazardous3D) ---
    public struct HazardReport
    {
        public bool hazardous;
        public string status;
        public float severity;   // 0..1
    }

    // --- Data grids ---
    private FireState[,] fireGrid;
    // private float[,] smokeGrid; // smoke disabled
    private GameObject[,] fireInstances;

    // (smoke logic removed)

    // --- Visual layer ---
    private float tickTimer = 0f;
    private int tickCount = 0;
    private int burningCellsCount = 0;

    private static readonly Vector2Int[] NEIGHBORS = new Vector2Int[]
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    // ------------------------------------------------------------------
    // SETUP
    // ------------------------------------------------------------------
    void Start()
    {
        // Center the grid on this GameObject so it works at any world position
        floorSurfaceY = (floorObject != null) ? floorObject.transform.position.y : transform.position.y;
        gridOriginX = transform.position.x - (cols * cellSize) / 2f;
        gridOriginZ = transform.position.z - (rows * cellSize) / 2f;

        doorwayZEdge = rows - 1;

        fireGrid = new FireState[cols, rows];
        // smoke grid removed
        fireInstances = new GameObject[cols, rows];

        // smoke/ceiling logic removed

        // --- Ignition point; use this GameObject's world position as the start cell ---
        Vector3 startPos = transform.position;
        ignitionX = Mathf.RoundToInt((startPos.x - gridOriginX) / cellSize);
        ignitionZ = Mathf.RoundToInt((startPos.z - gridOriginZ) / cellSize);

        if (IsInBounds(ignitionX, ignitionZ))
        {
            fireGrid[ignitionX, ignitionZ] = FireState.EARLY_BURNING;
        }
        else
        {
            Debug.LogWarning($"Ignition point ({ignitionX},{ignitionZ}) is out of bounds " +
                             $"(cols={cols}, rows={rows}). No cell ignited.");
        }

        UpdateFireSpawn();
    }

    // Single source of truth for building layout.
    // When Task 1's real floor plan is wired in, only this changes
    // (or is replaced by sampling their BIM grid).
    FireState LayoutCell(int x, int z)
    {
        if (!IsFloorCell(x, z)) return FireState.WALL;
        if (x == 0 || x == cols - 1 || z == 0) return FireState.WALL;
        if (z == doorwayZEdge)
        {
            return (x >= doorwayXStart && x <= doorwayXEnd)
                ? FireState.DOOR
                : FireState.WALL;
        }
        return FireState.UNBURNT;
    }

    bool IsInBounds(int x, int z) => x >= 0 && x < cols && z >= 0 && z < rows;

    // ------------------------------------------------------------------
    // TICK LOOP — decoupled from frame rate (Unity equiv. of setInterval)
    // ------------------------------------------------------------------
    void Update()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer >= tickInterval)
        {
            tickTimer -= tickInterval;
            SimulationTick();
        }
    }

    bool IsOpenCell(int x, int z)
    {
        if (x < 0 || x >= cols || z < 0 || z >= rows) return false;
        return fireGrid[x, z] != FireState.WALL;
    }

    Vector3 CellCenter(int x, int z)
    {
        return new Vector3(gridOriginX + x * cellSize, floorSurfaceY + 0.01f, gridOriginZ + z * cellSize);
    }

    bool IsFloorCell(int x, int z)
    {
        if (!IsInBounds(x, z)) return false;
        if (!requireFloorSurface || floorObject == null) return true;

        Vector3 worldPoint = new Vector3(gridOriginX + x * cellSize, floorSurfaceY + floorSurfaceRaycastHeight, gridOriginZ + z * cellSize);
        if (Physics.Raycast(worldPoint, Vector3.down, out RaycastHit hit, floorSurfaceRaycastHeight * 2f))
        {
            if (hit.collider != null && (hit.collider.gameObject == floorObject || hit.collider.transform.IsChildOf(floorObject.transform)))
                return true;
        }
        return false;
    }

    bool IsIgnitable(int x, int z)
    {
        return IsOpenCell(x, z) &&
            (fireGrid[x, z] == FireState.UNBURNT || fireGrid[x, z] == FireState.DOOR);
    }

    void SimulationTick()
    {
        // ---------- 1) FIRE STATE TRANSITIONS ----------
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
                    // Only FULL_BURNING (S=2) spreads, per Sun et al.
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
                        if (IsIgnitable(nx, nz) &&
                            (nextFireGrid[nx, nz] == FireState.UNBURNT || nextFireGrid[nx, nz] == FireState.DOOR))
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

        // smoke logic removed

        fireGrid = nextFireGrid;

        UpdateFireSpawn();

        // smoke logic removed

        // --- Tick log ---
        tickCount++;
        if (enableTickLog && tickCount % logEveryNTicks == 0)
        {
            Debug.Log($"[Tick {tickCount}] Burning: {burningCellsCount} cells");
        }
    }

    void UpdateFireSpawn()
    {
        if (firePrefab == null) return;
        // Count current active fires
        int currentFires = 0;
        for (int x = 0; x < cols; x++)
            for (int z = 0; z < rows; z++)
                if (fireInstances[x, z] != null) currentFires++;

        for (int x = 0; x < cols; x++)
        {
            for (int z = 0; z < rows; z++)
            {
                FireState state = fireGrid[x, z];
                bool shouldExist = state == FireState.EARLY_BURNING ||
                                   state == FireState.FULL_BURNING ||
                                   state == FireState.EXTINGUISHING;

                if (shouldExist)
                {
                    if (fireInstances[x, z] == null)
                    {
                        // If maxFires <= 0 then unlimited
                        if (maxFires <= 0 || currentFires < maxFires)
                        {
                            fireInstances[x, z] = Instantiate(firePrefab, CellCenter(x, z), Quaternion.identity, transform);
                            currentFires++;
                        }
                    }
                }
                else
                {
                    if (fireInstances[x, z] != null)
                    {
                        Destroy(fireInstances[x, z]);
                        fireInstances[x, z] = null;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // PUBLIC INTERFACE FOR TASK 2 (agents)
    // ------------------------------------------------------------------
    public HazardReport IsHazardous3D(Vector3 position)
    {
        int gridX = Mathf.RoundToInt((position.x - gridOriginX) / cellSize);
        int gridZ = Mathf.RoundToInt((position.z - gridOriginZ) / cellSize);

        // --- Out of bounds: escaped, or invalid? ---
        if (gridX < 0 || gridX >= cols || gridZ < 0 || gridZ >= rows)
        {
            bool exitedThroughDoor =
                gridZ >= rows &&
                gridX >= doorwayXStart && gridX <= doorwayXEnd;

            if (exitedThroughDoor)
            {
                return new HazardReport { hazardous = false, status = "EXIT_REACHED", severity = 0f };
            }
            return new HazardReport { hazardous = true, status = "IMPASSABLE_OUT_OF_BOUNDS", severity = 1f };
        }

        FireState floorState = fireGrid[gridX, gridZ];

        // --- Solid geometry ---
        if (floorState == FireState.WALL)
        {
            return new HazardReport { hazardous = true, status = "SOLID_WALL_COLLISION", severity = 1f };
        }

        // --- Direct fire (thermal) at walking height ---
        if (position.y < 1.0f)
        {
            if (floorState == FireState.FULL_BURNING)
                return new HazardReport { hazardous = true, status = "DIRECT_FIRE_THERMAL", severity = 1.0f };
            if (floorState == FireState.EARLY_BURNING)
                return new HazardReport { hazardous = true, status = "DIRECT_FIRE_THERMAL", severity = 0.7f };
        }

        // --- Clear (smoke disabled) ---
        return new HazardReport
        {
            hazardous = false,
            status = "CLEAR_AIR",
            severity = 0f
        };
    }
}