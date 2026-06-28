using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.IO;

public class SimulationLogger : MonoBehaviour
{
    public float tickInterval = 0.4f;
    public static string filePath;

    private float tickTimer = 0f;
    private int tickNumber = 0;

    void Start()
    {
        filePath = Application.persistentDataPath + "/simulation_data.jsonl";

        if (File.Exists(filePath))
            File.Delete(filePath);
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

        // --- Zone Occupancy Records ---
        Dictionary<string, int> zoneCounts = ZoneOccupancy.GetZoneCounts();
        foreach (var zone in zoneCounts)
        {
            SimulationRecord record = new SimulationRecord(
                id: "ZONE-" + zone.Key,
                type: SensorType.ZoneOccupancy,
                loc: zone.Key,
                time: timestamp,
                tick: tickNumber
            );
            record.value = zone.Value;
            WriteRecord(record);
        }

        // --- Agent Telemetry Records ---
        AgentDataTracker[] agents = FindObjectsByType<AgentDataTracker>();
        foreach (AgentDataTracker agent in agents)
        {
            SimulationRecord record = new SimulationRecord(
                id: "Logger",
                type: SensorType.AgentTelemetry,
                loc: agent.currentZone,
                time: timestamp,
                tick: tickNumber
            );
            record.agentId = agent.agentId;
            record.speed = agent.speed;
            record.hasExited = agent.hasExited;
            record.timeEnteringZone = agent.timeEnteringZone;
            record.exitTime = agent.exitTime;
            WriteRecord(record);
        }

        // --- Smoke Detector Records (placeholder until Fatmah's script is ready) ---
        SimulationRecord smokeRecord = new SimulationRecord(
            id: "SMK-Placeholder",
            type: SensorType.SmokeDetector,
            loc: "Unknown",
            time: timestamp,
            tick: tickNumber
        );
        smokeRecord.value = 0f;
        // TODO: replace with Fatmah's smoke detector readings
        WriteRecord(smokeRecord);

        // --- Overall Summary Record ---
        SimulationRecord summaryRecord = new SimulationRecord(
            id: "Sys-Summary",
            type: SensorType.SimulationEvent,
            loc: "Global",
            time: timestamp,
            tick: tickNumber
        );
        summaryRecord.value = FindObjectsByType<AgentDataTracker>().Length;
        summaryRecord.eventDetails = "Inside:" + FindObjectsByType<AgentExitNavigator>().Length +
                                     " Exited:" + AgentExitBehavior.agentsExited +
                                     " Trapped:" + AgentDataTracker.agentsTrapped;
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
}