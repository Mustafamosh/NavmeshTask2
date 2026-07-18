// ZoneOccupancy.cs
//
// CHANGES IN THIS VERSION
//   - ResetRuntimeCounts clears the live occupancy numbers so the Stop button can
//     wipe the board without reloading the scene.
//   - ResyncFromScene recounts every agent against every zone at the moment the run
//     starts, so the counts are accurate even though agents were spawned and moved
//     around during setup, when trigger events are not a reliable source of truth.
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public class ZoneOccupancy : MonoBehaviour
{
    public struct AgentZoneEntry
    {
        public string agentId;
        public float entryTime;
        public float exitTime;
    }

    private static Dictionary<string, int> zoneCounts = new Dictionary<string, int>();
    private static Dictionary<string, Dictionary<string, float>> agentsInZone = new Dictionary<string, Dictionary<string, float>>();
    private static Dictionary<string, Dictionary<string, int>> agentZoneTriggerCounts = new Dictionary<string, Dictionary<string, int>>();
    private static Dictionary<string, List<AgentZoneEntry>> zoneHistory = new Dictionary<string, List<AgentZoneEntry>>();
    private static List<string> zoneOrder = new List<string>();
    private static bool zonesRegistered = false;

    private static int totalAgentsExited = 0;
    private static int tickNumber = 0;
    private static string csvFileName = "ZoneOccupancyRecords.csv";
    private static string CsvFilePath => Path.Combine(Application.dataPath, csvFileName);

    private void Awake()
    {
        RegisterAllZones();
    }

    private static void RegisterAllZones()
    {
        if (zonesRegistered) return;
        zonesRegistered = true;

        ResetCsvFile();

        GameObject[] zoneObjects = GameObject.FindGameObjectsWithTag("Zone");
        foreach (GameObject zoneObj in zoneObjects)
        {
            RegisterZone(zoneObj.name);
        }
    }

    private static void ResetCsvFile()
    {
        string path = CsvFilePath;
        if (File.Exists(path))
            File.Delete(path);

        string backupPath = path + ".old";
        if (File.Exists(backupPath))
            File.Delete(backupPath);
    }

    private static void RegisterZone(string zoneName)
    {
        if (zoneCounts.ContainsKey(zoneName)) return;

        zoneCounts[zoneName] = 0;
        agentsInZone[zoneName] = new Dictionary<string, float>();
        agentZoneTriggerCounts[zoneName] = new Dictionary<string, int>();
        zoneHistory[zoneName] = new List<AgentZoneEntry>();
        zoneOrder.Add(zoneName);
    }

    // Clears the live numbers but keeps the zones registered.
    public static void ResetRuntimeCounts()
    {
        foreach (string key in new List<string>(zoneCounts.Keys))
            zoneCounts[key] = 0;

        foreach (var kv in agentsInZone) kv.Value.Clear();
        foreach (var kv in agentZoneTriggerCounts) kv.Value.Clear();

        totalAgentsExited = 0;
    }

    // Recount everyone from scratch at the start of a run.
    public static void ResyncFromScene()
    {
        RegisterAllZones();
        ResetRuntimeCounts();

        GameObject[] agents = GameObject.FindGameObjectsWithTag("Agent");
        GameObject[] zones = GameObject.FindGameObjectsWithTag("Zone");

        foreach (GameObject agent in agents)
        {
            AgentDataTracker tr = agent.GetComponent<AgentDataTracker>();
            string id = tr != null ? tr.agentId : "unknown";
            Vector3 pos = agent.transform.position;

            foreach (GameObject zone in zones)
            {
                bool inside = false;
                foreach (Collider col in zone.GetComponents<Collider>())
                {
                    if (col == null) continue;
                    if ((col.ClosestPoint(pos) - pos).sqrMagnitude < 0.0001f) { inside = true; break; }
                }

                if (inside)
                {
                    string zn = zone.name;
                    RegisterZone(zn);
                    zoneCounts[zn]++;
                    agentsInZone[zn][id] = Time.time;
                    agentZoneTriggerCounts[zn][id] = 1;
                }
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Agent")) return;

        string zoneName = gameObject.name;
        RegisterZone(zoneName);

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
            CreateZoneOccupancyRecord(zoneName, agentId, "Enter", entryTime, 0f);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Agent")) return;

        string zoneName = gameObject.name;
        RegisterZone(zoneName);

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

                zoneHistory[zoneName].Add(new AgentZoneEntry
                {
                    agentId = agentId,
                    entryTime = entryTime,
                    exitTime = exitTime
                });

                agentsInZone[zoneName].Remove(agentId);
            }

            totalAgentsExited = AgentDataTracker.agentsExited;
            float currentExitTime = Time.time;

            CreateZoneOccupancyRecord(zoneName, agentId, "Exit", 0f, currentExitTime);
        }
    }

    public static void ForceRemoveAgent(string zoneName, string agentId)
    {
        if (!zoneCounts.ContainsKey(zoneName)) return;

        int triggerCount = agentZoneTriggerCounts[zoneName].ContainsKey(agentId) ? agentZoneTriggerCounts[zoneName][agentId] : 0;
        if (triggerCount <= 0) return;

        agentZoneTriggerCounts[zoneName][agentId] = 0;
        zoneCounts[zoneName]--;

        float entryTime = 0f;
        float exitTime = Time.time;

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

        totalAgentsExited = AgentDataTracker.agentsExited;
        CreateZoneOccupancyRecord(zoneName, agentId, "ForcedExit", entryTime, exitTime);
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
        record.hasExited = eventType == "Exit" || eventType == "ForcedExit";
        record.timeEnteringZone = entryTime;
        record.exitTime = exitTime;
        record.eventDetails = eventType == "Enter" ? "Agent entered zone"
            : eventType == "ForcedExit" ? "Agent exited zone (destroyed on exit/trapped)"
            : "Agent exited zone";

        string path = CsvFilePath;
        string header = BuildCsvHeader();
        EnsureCsvHeader(path, header);

        List<string> fields = new List<string>
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
            zoneCounts[zoneName].ToString()
        };

        foreach (string z in zoneOrder)
        {
            fields.Add(zoneCounts[z].ToString());
        }

        File.AppendAllText(path, string.Join(",", fields) + "\n");
    }

    private static string BuildCsvHeader()
    {
        string baseHeader = "sensorId,sensorTypeString,location,timestamp,tickNumber,value,agentId,speed,hasExited,timeEnteringZone,exitTime,eventDetails,totalAgentsExited,zoneCount";
        string zoneColumns = string.Join(",", zoneOrder.Select(z => "zone_" + z.Replace(" ", "_")));
        return zoneColumns.Length > 0 ? baseHeader + "," + zoneColumns : baseHeader;
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