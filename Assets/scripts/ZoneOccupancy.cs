using UnityEngine;
using System.Collections.Generic;
using System.IO;

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

    // Track currently inside agents with their entry times
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

    // Track nested trigger counts for agents within a zone to ignore internal transitions
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

    // Track all entries and exits history
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

    private static int totalAgentsExited = 0;
    private static int tickNumber = 0;
    private static string csvFileName = "ZoneOccupancyRecords.csv";
    private static string CsvFilePath => Path.Combine(Application.dataPath, csvFileName);
   
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Agent"))
        {
            string zoneName = gameObject.name;
            if (zoneCounts.ContainsKey(zoneName) && agentsInZone.ContainsKey(zoneName))
            {
                // Get agent ID from AgentDataTracker script
                AgentDataTracker agentTracker = other.GetComponent<AgentDataTracker>();
                string agentId = agentTracker != null ? agentTracker.agentId : "unknown";

                float entryTime = Time.time;
                int triggerCount = 0;
                agentZoneTriggerCounts[zoneName].TryGetValue(agentId, out triggerCount);
                triggerCount++;
                agentZoneTriggerCounts[zoneName][agentId] = triggerCount;

                if (triggerCount == 1)
                {
                    zoneCounts[zoneName]++;
                    agentsInZone[zoneName][agentId] = entryTime;
                    // Debug.Log($"Agent {agentId} entered {zoneName} at {entryTime}. Occupancy: {zoneCounts[zoneName]}");
                    // PrintZoneStatus(zoneName);
                    CreateZoneOccupancyRecord(zoneName, agentId, "Enter", entryTime, 0f);
                }
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Agent"))
        {
            string zoneName = gameObject.name;
            if (zoneCounts.ContainsKey(zoneName) && agentsInZone.ContainsKey(zoneName))
            {
                // Get agent ID from AgentDataTracker script
                AgentDataTracker agentTracker = other.GetComponent<AgentDataTracker>();
                string agentId = agentTracker != null ? agentTracker.agentId : "unknown";

                int triggerCount = agentZoneTriggerCounts[zoneName].ContainsKey(agentId) ? agentZoneTriggerCounts[zoneName][agentId] : 0;
                if (triggerCount > 0)
                {
                    triggerCount--;
                    agentZoneTriggerCounts[zoneName][agentId] = triggerCount;
                }

                if (triggerCount == 0)
                {
                    zoneCounts[zoneName]--;
                    
                    if (agentsInZone[zoneName].ContainsKey(agentId))
                    {
                        float entryTime = agentsInZone[zoneName][agentId];
                        float exitTime = Time.time;
                        
                        // Record in history
                        zoneHistory[zoneName].Add(new AgentZoneEntry
                        {
                            agentId = agentId,
                            entryTime = entryTime,
                            exitTime = exitTime
                        });
                        
                        agentsInZone[zoneName].Remove(agentId);
                    }

                    totalAgentsExited = AgentExitBehavior.agentsExited;
                    float currentExitTime = Time.time;
                    
                    // Debug.Log($"Agent {agentId} exited {zoneName} at {currentExitTime}. Occupancy: {zoneCounts[zoneName]}");
                    // PrintZoneStatus(zoneName);
                    CreateZoneOccupancyRecord(zoneName, agentId, "Exit", 0f, currentExitTime);
                }
            }
        }
    }

    private void PrintZoneStatus(string zoneName)
    {
        // Debug.Log($"--- {zoneName} Status ---");
        // Debug.Log($"Current Count: {zoneCounts[zoneName]}");
        // Debug.Log($"Agents Inside: {string.Join(", ", agentsInZone[zoneName].Keys)}");
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

    private static void CreateZoneOccupancyRecord(string zoneName, string agentId, string eventType, float entryTime, float exitTime)
    {
        SimulationRecord record = new SimulationRecord(
            id: $"ZONE-{zoneName}-{eventType}",
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
        record.eventDetails = eventType == "Enter" ? "Agent entered zone" : "Agent exited zone";

        string path = CsvFilePath;
        string header = "sensorId,sensorTypeString,location,timestamp,tickNumber,value,agentId,speed,hasExited,timeEnteringZone,exitTime,eventDetails,totalAgentsExited,zoneCount,mainHall,classroom,offices,bathrooms,exit1,exit2,exit3,hallway1,hallway2,hallway3";
        EnsureCsvHeader(path, header);

        string line = string.Join(",", new string[]
        {
            record.sensorId,
            record.sensorTypeString,
            record.location,
            record.timestamp.ToString(),
            record.tickNumber.ToString(),
            record.value.ToString(),
            record.agentId,
            record.speed.ToString(),
            record.hasExited.ToString(),
            record.timeEnteringZone.ToString(),
            record.exitTime.ToString(),
            record.eventDetails,
            totalAgentsExited.ToString(),
            zoneCounts[zoneName].ToString(),
            zoneCounts["Main Hall"].ToString(),
            zoneCounts["Classroom"].ToString(),
            zoneCounts["Offices"].ToString(),
            zoneCounts["Bathrooms"].ToString(),
            zoneCounts["Exit 1"].ToString(),
            zoneCounts["Exit 2"].ToString(),
            zoneCounts["Exit 3"].ToString(),
            zoneCounts["Hallway 1"].ToString(),
            zoneCounts["Hallway 2"].ToString(),
            zoneCounts["Hallway 3"].ToString()
        });

        File.AppendAllText(path, line + "\n");
    }

    private static void EnsureCsvHeader(string path, string header)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, header + "\n");
            return;
        }

        string[] existingLines = File.ReadAllLines(path);
        if (existingLines.Length == 0 || existingLines[0] != header)
        {
            string backupPath = path + ".old";
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(path, backupPath);
            File.WriteAllText(path, header + "\n");
        }
    }
}
