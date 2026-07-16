// SimulationLogger.cs
//
// CHANGE IN THIS VERSION
//   Logging is now controlled by the Start and Stop buttons through the controller.
//   The file path is prepared in Awake but not cleared. BeginRun clears the file
//   and turns logging on. StopLogging turns it off and leaves the finished JSON on
//   disk. Nothing is written while the user is still in Setup, so the spawn and
//   despawn churn of setting up agents does not pollute the log.
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class SimulationLogger : MonoBehaviour
{
    public float tickInterval = 0.4f;
    public static string filePath;
    public static bool IsLogging = false;
    public FireSpread fireSpread;

    private bool alarmLogged = false;

    [System.Serializable]
    public class ExitApproach
    {
        public string exitName;
        public Transform approachZone;
    }
    public List<ExitApproach> exitApproaches = new List<ExitApproach>();

    private float tickTimer = 0f;
    private int tickNumber = 0;
    private Dictionary<string, bool> approachBlocked = new Dictionary<string, bool>();

    void Awake()
    {
        SetupPath();
        IsLogging = false;
    }

    void Start()
    {
        if (fireSpread == null)
            fireSpread = FindAnyObjectByType<FireSpread>();
    }

    void SetupPath()
    {
        string folder = Application.persistentDataPath + "/llm-coach";
        Directory.CreateDirectory(folder);
        filePath = folder + "/simulation_data.jsonl";
    }

    // Called by the controller when the user presses Start.
    public void BeginRun()
    {
        if (string.IsNullOrEmpty(filePath)) SetupPath();
        if (File.Exists(filePath)) File.Delete(filePath);

        tickTimer = 0f;
        tickNumber = 0;
        alarmLogged = false;
        approachBlocked.Clear();
        IsLogging = true;
    }

    // Called by the controller on Stop. The file stays on disk, complete.
    public void StopLogging()
    {
        IsLogging = false;
    }

    void Update()
    {
        if (!IsLogging) return;

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

        Dictionary<string, int> zoneCounts = ZoneOccupancy.GetZoneCounts();
        foreach (var zone in zoneCounts)
        {
            SimulationRecord record = new SimulationRecord("ZONE-" + zone.Key, SensorType.ZoneOccupancy, zone.Key, timestamp, tickNumber);
            record.value = zone.Value;
            WriteRecord(record);
        }

        AgentDataTracker[] agents = FindObjectsByType<AgentDataTracker>(FindObjectsSortMode.None);

        int vulnerableInside = 0;
        int criticalHealth = 0;

        foreach (AgentDataTracker agent in agents)
        {
            SimulationRecord record = new SimulationRecord("Logger", SensorType.AgentTelemetry, agent.currentZone, timestamp, tickNumber);

            record.agentId = agent.agentId;
            record.speed = agent.speed;
            record.hasExited = agent.hasExited;
            record.timeEnteringZone = agent.timeEnteringZone;
            record.exitTime = agent.exitTime;

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

        SmokeDetectorNode[] detectors = FindObjectsByType<SmokeDetectorNode>(FindObjectsSortMode.None);
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

        if (fireSpread != null)
        {
            SimulationRecord hazardRecord = new SimulationRecord("HAZ-Global", SensorType.Hazard, "Global", timestamp, tickNumber);
            hazardRecord.value = fireSpread.GetBurningCellsCount();
            hazardRecord.eventDetails = "Burning cells this tick";
            WriteRecord(hazardRecord);
        }

        if (FireAlarmSystem.Instance != null && FireAlarmSystem.Instance.alarmActive && !alarmLogged)
        {
            alarmLogged = true;
            string who = FireAlarmSystem.Instance.firstDetectorName;
            string zone = FireAlarmSystem.Instance.firstDetectorZone;
            LogEvent("EVENT-Alarm", zone, "Global alarm started, first detector " + who + " in " + zone, timestamp, tickNumber);
        }

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
                        ? (ex.exitName + " blocked by fire in " + ex.approachZone.name)
                        : (ex.exitName + " clear again");
                    LogEvent("EVENT-" + ex.exitName, ex.exitName, details, timestamp, tickNumber);
                }
            }
        }

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
        if (!IsLogging) return;
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