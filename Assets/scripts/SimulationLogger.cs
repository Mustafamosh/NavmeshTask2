using UnityEngine;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Central logger. Runs on a fixed tick and writes one JSON object per line
/// capturing zones, agents, hazard, and events into a single file.
/// This file is the input the AI coach reads later.
/// </summary>
public class SimulationLogger : MonoBehaviour
{
    // === 1. Settings and Shared State ===
    public float tickInterval = 0.4f;   // How often a set of records is written, in seconds
    public static string filePath;      // Full path to the output file, shared so other scripts can write too
    public FireSpread fireSpread;       // Reference to the fire model, dragged in from the Hierarchy

    // === 2. Exit Approach Zones ===
    // Each exit is reachable only through the zone in front of it.
    // If that zone is on fire, the exit is effectively blocked.
    [System.Serializable]
    public class ExitApproach
    {
        public string exitName;          // e.g. Exit 2
        public Transform approachZone;   // the hallway object in front of it
    }
    public List<ExitApproach> exitApproaches = new List<ExitApproach>();

    // === 3. Internal Counters ===
    private float tickTimer = 0f;        // Counts up until it reaches tickInterval
    private int tickNumber = 0;          // Goes up by one every tick
    private Dictionary<string, bool> approachBlocked = new Dictionary<string, bool>();  // Whether each exit approach was blocked last tick

    void Start()
    {
        // Build the output path and clear any old file from a previous run
        // Make sure the llm-coach folder exists, then save the file inside it
        string folder = Application.persistentDataPath + "/llm-coach";
        System.IO.Directory.CreateDirectory(folder);
        filePath = folder + "/simulation_data.jsonl";

        if (File.Exists(filePath)) 
        File.Delete(filePath);

        // Find the fire model automatically if it was not assigned in the Inspector
        if (fireSpread == null)
            fireSpread = FindAnyObjectByType<FireSpread>();
    }

    void Update()
    {
        // Wait until enough time has passed, then log one full tick
        tickTimer += Time.deltaTime;
        if (tickTimer >= tickInterval)
        {
            tickTimer = 0f;
            tickNumber++;
            LogTick();
        }
    }

    void LogTick()
    {
        float timestamp = Time.time;

        // === Zone occupancy, how many agents are in each zone right now ===
        Dictionary<string, int> zoneCounts = ZoneOccupancy.GetZoneCounts();
        foreach (var zone in zoneCounts)
        {
            SimulationRecord record = new SimulationRecord("ZONE-" + zone.Key, SensorType.ZoneOccupancy, zone.Key, timestamp, tickNumber);
            record.value = zone.Value;  // The count for this zone
            WriteRecord(record);
        }

        // === Agent telemetry, one record per agent with the fire danger at its position ===
        AgentDataTracker[] agents = FindObjectsByType<AgentDataTracker>();
        foreach (AgentDataTracker agent in agents)
        {
            SimulationRecord record = new SimulationRecord("Logger", SensorType.AgentTelemetry, agent.currentZone, timestamp, tickNumber);
            record.agentId = agent.agentId;
            record.speed = agent.speed;
            record.hasExited = agent.hasExited;
            record.timeEnteringZone = agent.timeEnteringZone;
            record.exitTime = agent.exitTime;

            // Ask the fire model how dangerous this agent's exact spot is
            if (fireSpread != null)
            {
                FireSpread.HazardReport hr = fireSpread.IsHazardous3D(agent.transform.position);
                record.hazardSeverity = hr.severity;
                record.hazardStatus = hr.status;
            }
            WriteRecord(record);
        }

        // === Smoke detector placeholder, stays zero until Fatmah's script is connected ===
        SimulationRecord smokeRecord = new SimulationRecord("SMK-Placeholder", SensorType.SmokeDetector, "Unknown", timestamp, tickNumber);
        smokeRecord.value = 0f;
        WriteRecord(smokeRecord);

        // === Global hazard, how much of the building is burning this tick ===
        if (fireSpread != null)
        {
            SimulationRecord hazardRecord = new SimulationRecord("HAZ-Global", SensorType.Hazard, "Global", timestamp, tickNumber);
            hazardRecord.value = fireSpread.GetBurningCellsCount();
            hazardRecord.eventDetails = "Burning cells this tick";
            WriteRecord(hazardRecord);
        }

        // === Blocked exit events, based on the approach zone, not the exit block ===
        if (fireSpread != null)
        {
            foreach (ExitApproach ex in exitApproaches)
            {
                if (ex.approachZone == null) continue;

                // Read the fire at the zone that leads to this exit
                FireSpread.HazardReport hr = fireSpread.IsHazardous3D(ex.approachZone.position);
                bool blockedNow = hr.hazardous && hr.severity >= 0.7f;
                bool blockedBefore = approachBlocked.ContainsKey(ex.exitName) && approachBlocked[ex.exitName];

                // Only write when the state changes, so each block or clear is logged once
                if (blockedNow != blockedBefore)
                {
                    approachBlocked[ex.exitName] = blockedNow;
                    string details = blockedNow
                        ? (ex.exitName + " blocked, fire in " + ex.approachZone.name)
                        : (ex.exitName + " clear again");
                    LogEvent("EVENT-" + ex.exitName, ex.exitName, details, timestamp, tickNumber);
                }
            }
        }

        // === Summary, whole building totals for this tick ===
        SimulationRecord summaryRecord = new SimulationRecord("Sys-Summary", SensorType.SimulationEvent, "Global", timestamp, tickNumber);
        summaryRecord.value = FindObjectsByType<AgentDataTracker>().Length;
        summaryRecord.eventDetails = "Inside:" + FindObjectsByType<AgentExitNavigator>().Length +
                                     " Exited:" + AgentExitBehavior.agentsExited +
                                     " Trapped:" + AgentDataTracker.agentsTrapped;
        WriteRecord(summaryRecord);
    }

    /// <summary>
    /// Appends one record to the file as a single JSON line.
    /// Public and static so any script, like AgentDataTracker, can log too.
    /// </summary>
    public static void WriteRecord(SimulationRecord record)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        using (StreamWriter writer = new StreamWriter(filePath, append: true))
        {
            writer.WriteLine(JsonUtility.ToJson(record));
        }
    }

    /// <summary>
    /// Logs a one off event, such as a danger warning or an exit block.
    /// Any script can call this so events all land in the same file.
    /// </summary>
    public static void LogEvent(string id, string location, string details, float time, int tick)
    {
        SimulationRecord ev = new SimulationRecord(id, SensorType.SimulationEvent, location, time, tick);
        ev.eventDetails = details;
        WriteRecord(ev);
    }
}