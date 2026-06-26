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
        { "Bathrooms", 0 }
    };

    // Track currently inside agents with their entry times
    private static Dictionary<string, Dictionary<string, float>> agentsInZone = new Dictionary<string, Dictionary<string, float>>
    {
        { "Main Hall", new Dictionary<string, float>() },
        { "Classroom", new Dictionary<string, float>() },
        { "Offices", new Dictionary<string, float>() },
        { "Bathrooms", new Dictionary<string, float>() }
    };

    // Track all entries and exits history
    private static Dictionary<string, List<AgentZoneEntry>> zoneHistory = new Dictionary<string, List<AgentZoneEntry>>
    {
        { "Main Hall", new List<AgentZoneEntry>() },
        { "Classroom", new List<AgentZoneEntry>() },
        { "Offices", new List<AgentZoneEntry>() },
        { "Bathrooms", new List<AgentZoneEntry>() }
    };

    private static int totalAgentsExited = 0;
    private static int tickNumber = 0;
    private static string csvFileName = "ZoneOccupancyRecords.csv";
    private static string CsvFilePath => Path.Combine(Application.persistentDataPath, csvFileName);
   
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Agent"))
        {
            string zoneName = gameObject.name;
            if (zoneCounts.ContainsKey(zoneName) && agentsInZone.ContainsKey(zoneName))
            {
                // Get agent ID from AgentNoise script
                AgentNoise agentNoise = other.GetComponent<AgentNoise>();
                string agentId = agentNoise != null ? agentNoise.agentId : "unknown";

                zoneCounts[zoneName]++;
                agentsInZone[zoneName][agentId] = Time.time;
                
                Debug.Log($"Agent {agentId} entered {zoneName} at {Time.time}. Occupancy: {zoneCounts[zoneName]}");
                PrintZoneStatus(zoneName);
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
                // Get agent ID from AgentNoise script
                AgentNoise agentNoise = other.GetComponent<AgentNoise>();
                string agentId = agentNoise != null ? agentNoise.agentId : "unknown";

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

                totalAgentsExited++;
                
                Debug.Log($"Agent {agentId} exited {zoneName} at {Time.time}. Occupancy: {zoneCounts[zoneName]}");
                PrintZoneStatus(zoneName);

                // Create SimulationRecord when 30 agents have exited
                if (totalAgentsExited == 30)
                {
                    CreateZoneOccupancyRecord(zoneName);
                }
            }
        }
    }

    private void PrintZoneStatus(string zoneName)
    {
        Debug.Log($"--- {zoneName} Status ---");
        Debug.Log($"Current Count: {zoneCounts[zoneName]}");
        Debug.Log($"Agents Inside: {string.Join(", ", agentsInZone[zoneName].Keys)}");
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

    private static void CreateZoneOccupancyRecord(string zoneName)
    {
        SimulationRecord record = new SimulationRecord(
            id: $"ZONE-{zoneName}",
            type: SensorType.ZoneOccupancy,
            loc: zoneName,
            time: Time.time,
            tick: tickNumber++
        );

        string path = CsvFilePath;
        string header = "sensorId,sensorType,location,timestamp,tickNumber,totalAgentsExited,zoneCount,mainHall,classroom,offices,bathrooms";
        if (!File.Exists(path))
        {
            File.WriteAllText(path, header + "\n");
        }

        string line = string.Join(",", new string[]
        {
            record.sensorId,
            record.sensorType.ToString(),
            record.location,
            record.timestamp.ToString(),
            record.tickNumber.ToString(),
            totalAgentsExited.ToString(),
            zoneCounts[zoneName].ToString(),
            zoneCounts["Main Hall"].ToString(),
            zoneCounts["Classroom"].ToString(),
            zoneCounts["Offices"].ToString(),
            zoneCounts["Bathrooms"].ToString()
        });

        File.AppendAllText(path, line + "\n");
    }
}
