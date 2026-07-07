// ZoneOccupancy.cs
// Changes from previous version:
//   - CSV output completely removed (was writing to Application.dataPath separately)
//   - All zone entry/exit events now write to the shared JSONL via SimulationLogger.WriteRecord
//   - This puts zone data in the same file the AI coach reads, with richer eventDetails
using UnityEngine;
using System.Collections.Generic;

public class ZoneOccupancy : MonoBehaviour
{
    public struct AgentZoneEntry
    {
        public string agentId;
        public float entryTime;
        public float exitTime;
    }

    private static Dictionary<string, int> zoneCounts = new Dictionary<string, int>
    {
        { "Main Hall", 0 },
        { "Classroom", 0 },
        { "Offices", 0 },
        { "Bathrooms", 0 },
        { "Exit 1", 0 },
        { "Exit 2", 0 },
        { "Exit 3", 0 },
        { "Hallway 1", 0 },
        { "Hallway 2", 0 },
        { "Hallway 3", 0 }
    };

    private static Dictionary<string, Dictionary<string, float>> agentsInZone = new Dictionary<string, Dictionary<string, float>>
    {
        { "Main Hall", new Dictionary<string, float>() },
        { "Classroom", new Dictionary<string, float>() },
        { "Offices", new Dictionary<string, float>() },
        { "Bathrooms", new Dictionary<string, float>() },
        { "Exit 1", new Dictionary<string, float>() },
        { "Exit 2", new Dictionary<string, float>() },
        { "Exit 3", new Dictionary<string, float>() },
        { "Hallway 1", new Dictionary<string, float>() },
        { "Hallway 2", new Dictionary<string, float>() },
        { "Hallway 3", new Dictionary<string, float>() }
    };

    private static Dictionary<string, Dictionary<string, int>> agentZoneTriggerCounts = new Dictionary<string, Dictionary<string, int>>
    {
        { "Main Hall", new Dictionary<string, int>() },
        { "Classroom", new Dictionary<string, int>() },
        { "Offices", new Dictionary<string, int>() },
        { "Bathrooms", new Dictionary<string, int>() },
        { "Exit 1", new Dictionary<string, int>() },
        { "Exit 2", new Dictionary<string, int>() },
        { "Exit 3", new Dictionary<string, int>() },
        { "Hallway 1", new Dictionary<string, int>() },
        { "Hallway 2", new Dictionary<string, int>() },
        { "Hallway 3", new Dictionary<string, int>() }
    };

    private static Dictionary<string, List<AgentZoneEntry>> zoneHistory = new Dictionary<string, List<AgentZoneEntry>>
    {
        { "Main Hall", new List<AgentZoneEntry>() },
        { "Classroom", new List<AgentZoneEntry>() },
        { "Offices", new List<AgentZoneEntry>() },
        { "Bathrooms", new List<AgentZoneEntry>() },
        { "Exit 1", new List<AgentZoneEntry>() },
        { "Exit 2", new List<AgentZoneEntry>() },
        { "Exit 3", new List<AgentZoneEntry>() },
        { "Hallway 1", new List<AgentZoneEntry>() },
        { "Hallway 2", new List<AgentZoneEntry>() },
        { "Hallway 3", new List<AgentZoneEntry>() }
    };

    private static int tickNumber = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        foreach (var key in new List<string>(zoneCounts.Keys))
        {
            zoneCounts[key] = 0;
            agentsInZone[key].Clear();
            agentZoneTriggerCounts[key].Clear();
            zoneHistory[key].Clear();
        }
        tickNumber = 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Agent")) return;

        string zoneName = gameObject.name;
        if (!zoneCounts.ContainsKey(zoneName)) return;

        AgentDataTracker agentTracker = other.GetComponent<AgentDataTracker>();
        string agentId = agentTracker != null ? agentTracker.agentId : "unknown";
        float entryTime = Time.time;

        agentZoneTriggerCounts[zoneName].TryGetValue(agentId, out int triggerCount);
        triggerCount++;
        agentZoneTriggerCounts[zoneName][agentId] = triggerCount;

        if (triggerCount == 1)
        {
            zoneCounts[zoneName]++;
            agentsInZone[zoneName][agentId] = entryTime;
            WriteZoneRecord(zoneName, agentId, "Enter", entryTime, 0f);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Agent")) return;

        string zoneName = gameObject.name;
        if (!zoneCounts.ContainsKey(zoneName)) return;

        AgentDataTracker agentTracker = other.GetComponent<AgentDataTracker>();
        string agentId = agentTracker != null ? agentTracker.agentId : "unknown";

        int triggerCount = agentZoneTriggerCounts[zoneName].ContainsKey(agentId)
            ? agentZoneTriggerCounts[zoneName][agentId]
            : 0;

        if (triggerCount > 0)
        {
            triggerCount--;
            agentZoneTriggerCounts[zoneName][agentId] = triggerCount;
        }

        if (triggerCount == 0)
        {
            zoneCounts[zoneName] = Mathf.Max(0, zoneCounts[zoneName] - 1);

            float exitTime = Time.time;
            float entryTime = 0f;

            if (agentsInZone[zoneName].ContainsKey(agentId))
            {
                entryTime = agentsInZone[zoneName][agentId];
                zoneHistory[zoneName].Add(new AgentZoneEntry
                {
                    agentId = agentId,
                    entryTime = entryTime,
                    exitTime = exitTime
                });
                agentsInZone[zoneName].Remove(agentId);
            }

            WriteZoneRecord(zoneName, agentId, "Exit", entryTime, exitTime);
        }
    }

    // Writes a zone event directly to the shared JSONL file via SimulationLogger.
    // The eventDetails field is rich so the AI coach can read flow through the building.
    private static void WriteZoneRecord(string zoneName, string agentId, string eventType, float entryTime, float exitTime)
    {
        SimulationRecord record = new SimulationRecord(
            id: "ZONE-" + zoneName + "-" + eventType + "-" + agentId,
            type: SensorType.ZoneOccupancy,
            loc: zoneName,
            time: Time.time,
            tick: tickNumber++
        );

        record.agentId = agentId;
        record.value = zoneCounts[zoneName];
        record.speed = 0f;
        record.hasExited = eventType == "Exit";
        record.timeEnteringZone = entryTime;
        record.exitTime = exitTime;

        // Rich details for AI ingestion: who moved where, current zone population,
        // and how long they spent in the zone on exit
        float dwellTime = (eventType == "Exit" && entryTime > 0f) ? (exitTime - entryTime) : 0f;
        record.eventDetails = eventType == "Enter"
            ? agentId + " entered " + zoneName + " | Zone count now: " + zoneCounts[zoneName]
            : agentId + " exited " + zoneName + " after " + dwellTime.ToString("F2") + "s | Zone count now: " + zoneCounts[zoneName];

        SimulationLogger.WriteRecord(record);
    }

    public static Dictionary<string, int> GetZoneCounts()
    {
        return new Dictionary<string, int>(zoneCounts);
    }

    public static Dictionary<string, float> GetAgentsInZone(string zoneName)
    {
        if (agentsInZone.ContainsKey(zoneName))
            return new Dictionary<string, float>(agentsInZone[zoneName]);
        return new Dictionary<string, float>();
    }

    public static List<AgentZoneEntry> GetZoneHistory(string zoneName)
    {
        if (zoneHistory.ContainsKey(zoneName))
            return new List<AgentZoneEntry>(zoneHistory[zoneName]);
        return new List<AgentZoneEntry>();
    }
}