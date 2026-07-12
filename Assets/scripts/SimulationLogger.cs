// SimulationLogger.cs
//
// CHANGES IN THIS VERSION
//   - The log file is now opened in Awake instead of Start. Previously agents ran
//     their Start before the logger ran its own, so SimulationLogger.filePath was
//     still empty and every spawn PROFILE record was silently thrown away. Opening
//     the file in Awake means the path exists before any agent needs it.
//   - The vulnerability count now matches the simplified disability model, so it
//     counts elderly agents, agents using a mobility aid, and agents that have
//     become injured.
//   - All existing behavior is preserved.
using UnityEngine;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Central logger. Runs on a fixed tick and writes one JSON object per line
/// capturing zones, agents, hazard, and events into a single JSONL file.
/// This file is the input the AI coach reads later.
/// </summary>
public class SimulationLogger : MonoBehaviour
{
    // === 1. Settings and Shared State ===
    public float tickInterval = 0.4f;
    public static string filePath;
    public FireSpread fireSpread;
    private bool alarmLogged = false;

    // === 2. Exit Approach Zones ===
    [System.Serializable]
    public class ExitApproach
    {
        public string exitName;
        public Transform approachZone;
    }
    public List<ExitApproach> exitApproaches = new List<ExitApproach>();

    // === 3. Internal Counters ===
    private float tickTimer = 0f;
    private int tickNumber = 0;
    private Dictionary<string, bool> approachBlocked = new Dictionary<string, bool>();

    // The file must exist before any agent Start runs, otherwise spawn profile
    // records are dropped. Awake is the earliest safe place to do this.
    void Awake()
    {
        string folder = Application.persistentDataPath + "/llm-coach";
        Directory.CreateDirectory(folder);
        filePath = folder + "/simulation_data.jsonl";

        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    void Start()
    {
        if (fireSpread == null)
            fireSpread = FindAnyObjectByType<FireSpread>();
    }

    void Update()
    {
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

        // === Zone occupancy ===
        Dictionary<string, int> zoneCounts = ZoneOccupancy.GetZoneCounts();
        foreach (var zone in zoneCounts)
        {
            SimulationRecord record = new SimulationRecord("ZONE-" + zone.Key, SensorType.ZoneOccupancy, zone.Key, timestamp, tickNumber);
            record.value = zone.Value;
            WriteRecord(record);
        }

        // === Agent telemetry, only agents still active in the scene ===
        AgentDataTracker[] agents = FindObjectsByType<AgentDataTracker>();

        int vulnerableInside = 0;   // elderly, using a mobility aid, or injured
        int criticalHealth = 0;     // below one third health and still inside

        foreach (AgentDataTracker agent in agents)
        {
            SimulationRecord record = new SimulationRecord("Logger", SensorType.AgentTelemetry, agent.currentZone, timestamp, tickNumber);

            record.agentId = agent.agentId;
            record.speed = agent.speed;
            record.hasExited = agent.hasExited;
            record.timeEnteringZone = agent.timeEnteringZone;
            record.exitTime = agent.exitTime;

            // Per agent context for the AI coach.
            record.ageBand = agent.ageBand;
            record.disability = agent.spawnDisability;
            record.mobilityStatus = agent.mobilityStatus;
            record.health = agent.health;
            record.maxHealth = agent.maxHealth;
            record.hazardBand = agent.hazardBand;
            record.distanceToFire = agent.distanceToFire;
            record.fireDamageTotal = agent.fireDamageTotal;
            record.visibilityDamageTotal = agent.visibilityDamageTotal;

            record.eventDetails =
                agent.agentId +
                " | Age: " + agent.ageBand +
                " | Disability: " + agent.spawnDisability +
                " | Mobility: " + agent.mobilityStatus +
                " | Health: " + agent.health.ToString("F1") +
                " | Hazard: " + agent.hazardBand +
                " | Zone: " + agent.currentZone;

            if (fireSpread != null)
            {
                FireSpread.HazardReport hr = fireSpread.IsHazardous3D(agent.transform.position);
                record.hazardSeverity = hr.severity;
                record.hazardStatus = hr.status;
            }

            WriteRecord(record);

            bool isVulnerable =
                agent.ageBand == "Elderly" ||
                agent.spawnDisability == "MobilityAid" ||
                agent.mobilityStatus.Contains("Injured");

            if (isVulnerable) vulnerableInside++;
            if (agent.maxHealth > 0f && agent.health / agent.maxHealth < 0.33f) criticalHealth++;
        }

        // === Smoke detector readings ===
        SmokeDetectorNode[] detectors = FindObjectsByType<SmokeDetectorNode>();
        foreach (SmokeDetectorNode detector in detectors)
        {
            SimulationRecord smokeRecord = new SimulationRecord(
                "SMK-" + detector.gameObject.name,
                SensorType.SmokeDetector,
                detector.nodeZone,
                timestamp,
                tickNumber
            );
            smokeRecord.value = detector.smokeReading;
            smokeRecord.eventDetails = detector.smokeDetected ? "Smoke detected" : "Clear";
            WriteRecord(smokeRecord);
        }

        // === Global hazard, burning cell count ===
        if (fireSpread != null)
        {
            SimulationRecord hazardRecord = new SimulationRecord("HAZ-Global", SensorType.Hazard, "Global", timestamp, tickNumber);
            hazardRecord.value = fireSpread.GetBurningCellsCount();
            hazardRecord.eventDetails = "Burning cells this tick";
            WriteRecord(hazardRecord);
        }

        // === Alarm event, logged once ===
        if (FireAlarmSystem.Instance != null && FireAlarmSystem.Instance.alarmActive && !alarmLogged)
        {
            alarmLogged = true;
            string who = FireAlarmSystem.Instance.firstDetectorName;
            string zone = FireAlarmSystem.Instance.firstDetectorZone;
            LogEvent("EVENT-Alarm", zone, "Global alarm started, first detector " + who + " in " + zone, timestamp, tickNumber);
        }

        // === Blocked exit events ===
        if (fireSpread != null)
        {
            foreach (ExitApproach ex in exitApproaches)
            {
                if (ex.approachZone == null) continue;

                FireSpread.HazardReport hr = fireSpread.IsHazardous3D(ex.approachZone.position);
                bool blockedNow = hr.hazardous && hr.severity >= 0.7f;
                bool blockedBefore = approachBlocked.ContainsKey(ex.exitName) && approachBlocked[ex.exitName];

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

        // === Summary tick, whole building totals ===
        SimulationRecord summaryRecord = new SimulationRecord("Sys-Summary", SensorType.SimulationEvent, "Global", timestamp, tickNumber);
        summaryRecord.value = agents.Length;
        summaryRecord.eventDetails =
            "Inside:" + agents.Length +
            " Exited:" + AgentDataTracker.agentsExited +
            " Trapped:" + AgentDataTracker.agentsTrapped +
            " VulnerableStillInside:" + vulnerableInside +
            " CriticalHealthStillInside:" + criticalHealth;
        WriteRecord(summaryRecord);
    }

    public static void WriteRecord(SimulationRecord record)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        using (StreamWriter writer = new StreamWriter(filePath, append: true))
        {
            writer.WriteLine(JsonUtility.ToJson(record));
        }
    }

    public static void LogEvent(string id, string location, string details, float time, int tick)
    {
        SimulationRecord ev = new SimulationRecord(id, SensorType.SimulationEvent, location, time, tick);
        ev.eventDetails = details;
        WriteRecord(ev);
    }
}