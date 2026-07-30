// SimulationLogger.cs
//
// CHANGE IN THIS VERSION
//   Records are now buffered in memory instead of being appended to disk one line
//   at a time. Opening a stream per record was slow on desktop and is not viable in
//   WebGL, where there is no real filesystem. BeginRun clears the buffer, and the
//   whole run is turned into a single jsonl string only when it is handed over.
//   In WebGL the string goes to the browser as a download. In the editor and in a
//   desktop build it is written to persistentDataPath as before, so testing outside
//   the browser still produces a file on disk.
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

public class SimulationLogger : MonoBehaviour
{
    public float tickInterval = 0.4f;
    public static string filePath;
    public static bool IsLogging = false;
    public FireSpread fireSpread;

    // Every record written during the current run, one json object per entry.
    private static readonly List<string> buffer = new List<string>();
    private static float runStartTime = 0f;

    private bool alarmLogged = false;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void DownloadFileFromUnity(string filename, string content);
#endif

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
        // WebGL has no real filesystem, so this path is only used by the editor and
        // by desktop builds. It is harmless to compute it either way.
        string folder = Application.persistentDataPath + "/llm-coach";
        Directory.CreateDirectory(folder);
        filePath = folder + "/simulation_data.jsonl";
    }

    // Called by the controller when the run begins.
    public void BeginRun()
    {
        buffer.Clear();

        runStartTime = Time.time;
        tickTimer = 0f;
        tickNumber = 0;
        alarmLogged = false;
        approachBlocked.Clear();
        IsLogging = true;

        SimulationRecord startRecord = new SimulationRecord("EVENT-RunStart", SensorType.SimulationEvent, "Global", 0f, 0);
        startRecord.eventDetails = "Simulation run started";
        WriteRecord(startRecord);
    }

    // Called by the controller on Stop.
    public void StopLogging()
    {
        IsLogging = false;
        runStartTime = 0f;
    }

    public static float GetSimulationTime()
    {
        if (!IsLogging) return 0f;
        return Time.time - runStartTime;
    }

    /// <summary>
    /// Turns the buffered run into a single jsonl file and hands it to the user.
    /// In WebGL this opens the browser download prompt. Elsewhere it writes to
    /// persistentDataPath so the same call works while testing in the editor.
    /// Returns the filename that was produced.
    /// </summary>
    public string DownloadLog()
    {
        IsLogging = false;

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = "simulation_data_" + stamp + ".jsonl";

        StringBuilder sb = new StringBuilder();
        foreach (string line in buffer)
            sb.Append(line).Append('\n');

        string content = sb.ToString();

#if UNITY_WEBGL && !UNITY_EDITOR
        DownloadFileFromUnity(filename, content);
#else
        string folder = Application.persistentDataPath + "/llm-coach";
        Directory.CreateDirectory(folder);
        string outPath = folder + "/" + filename;
        File.WriteAllText(outPath, content, new UTF8Encoding(false));
        Debug.Log("Log written to " + outPath);
#endif

        return filename;
    }

    public static int BufferedRecordCount => buffer.Count;

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
        float timestamp = GetSimulationTime();

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
        buffer.Add(JsonUtility.ToJson(record));
    }

    public static void LogEvent(string id, string location, string details, float time, int tick, SensorType sensorType = SensorType.SimulationEvent)
    {
        SimulationRecord ev = new SimulationRecord(id, sensorType, location, GetSimulationTime(), tick);
        ev.eventDetails = details;
        WriteRecord(ev);
    }
}