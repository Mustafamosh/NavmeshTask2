using System;
using UnityEngine;

/// <summary>
/// Defines the types of records we are capturing.
/// StructuralDamage is new. It reports how badly each room has burned, so the AI
/// coach knows which parts of the building were lost and when.
/// </summary>
public enum SensorType
{
    ZoneOccupancy,
    SmokeDetector,
    AgentTelemetry,
    SimulationEvent,
    Hazard,
    AgentProfile,
    StructuralDamage,
    Obstacle
}

/// <summary>
/// A unified data format that EVERY sensor, agent, and event uses.
/// This ensures a single data shape for clean streaming and LLM ingestion.
/// </summary>
[Serializable]
public class SimulationRecord
{
    // --- 1. General Fields (used by all) ---
    public string sensorId;         // Unique ID, for example SMK-Lobby, PROFILE-Agent-4, DMG-Hallway 2
    public SensorType sensorType;   // Enum for Unity logic
    public string sensorTypeString; // String form for clean JSON output
    public string location;         // Room or zone name
    public float timestamp;         // Simulation-relative seconds, starting at 0 for a run
    public int tickNumber;          // Internal simulation sync tick

    // --- 2. Sensor Fields ---
    public float value;             // For example 0.85 fire severity or 5.0 occupancy

    // --- 3. Agent Fields ---
    public string agentId;          // Unique agent ID
    public float speed;             // Current velocity
    public bool hasExited;          // Did they safely evacuate
    public float timeEnteringZone;  // Time they entered the current location
    public float exitTime;          // Time they completed evacuation

    // --- 3b. Agent Profile and Health Fields ---
    public string ageBand;              // Young, Adult, or Elderly
    public string disability;           // Spawn disability, None or MobilityAid
    public string mobilityStatus;       // Effective mobility, also driven by falling health
    public float health;                // Current health
    public float maxHealth;             // Starting health
    public string hazardBand;           // Clear, LowVisibility, NearFire, or InFire
    public float distanceToFire;        // Metres to the nearest fire, negative one when no fire exists
    public float fireDamageTotal;       // Cumulative health lost to fire and near fire
    public float visibilityDamageTotal; // Cumulative health lost to low visibility
    public string trapReason;           // Fire or LowVisibility, whichever did more damage

    // --- 3c. Structural Damage Fields ---
    public float charLevel;         // Average char across the room, 0 untouched up to 1 destroyed
    public int surfacesDestroyed;   // How many surfaces in the room are fully charred
    public int surfacesTotal;       // How many burnable surfaces the room has
    public string damageLabel;      // Scorched, HeavilyDamaged, or Destroyed

    // --- 4. Event Fields ---
    public string eventDetails;     // Text context, for example Exit A blocked by fire

    // --- 5. Hazard Fields ---
    public float hazardSeverity;    // Fire severity at this point, 0 clear up to 1 full fire
    public string hazardStatus;     // Text status, for example CLEAR_AIR or DIRECT_FIRE_THERMAL

    public SimulationRecord(string id, SensorType type, string loc, float time, int tick)
    {
        sensorId = id;
        sensorType = type;
        sensorTypeString = type.ToString();
        location = loc;
        timestamp = time;
        tickNumber = tick;
    }
}