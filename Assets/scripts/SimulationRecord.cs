using System;
using UnityEngine;

/// <summary>
/// Defines the types of records we are capturing.
/// </summary>
public enum SensorType
{
    ZoneOccupancy,
    SmokeDetector,
    AgentTelemetry,
    SimulationEvent
}

/// <summary>
/// A unified data format that EVERY sensor, agent, and event uses.
/// This ensures a single data shape for clean streaming and LLM ingestion.
/// </summary>
[Serializable]
public class SimulationRecord
{
    // --- 1. General Fields (Used by all) ---
    public string sensorId;         // The unique ID (e.g., "SMK-Lobby", "Agent-402", "Event-Alarm")
    public SensorType sensorType;   // Enum for Unity Logic
    public string sensorTypeString; // String representation for clean JSON output
    public string location;         // Room or zone name
    public float timestamp;         // Real-time seconds (e.g., Time.time)
    public int tickNumber;          // Internal simulation sync tick
    
    // --- 2. Sensor Fields ---
    public float value;             // e.g., 0.85 fire severity or 5.0 occupancy

    // --- 3. Agent Fields ---
    public string agentId;          // Unique Agent ID
    public float speed;             // Current velocity
    public bool hasExited;          // Did they safely evacuate?
    public float timeEnteringZone;  // Time they entered the 'location'
    public float exitTime;          // Time they completed evacuation

    // --- 4. Event Fields ---
    public string eventDetails;     // Text context (e.g., "Exit A Blocked by Fire")

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
