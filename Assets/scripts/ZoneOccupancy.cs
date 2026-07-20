// ZoneOccupancy.cs
//
// CHANGES IN THIS VERSION
//   - An agent now belongs to exactly ONE zone at a time. Zone trigger volumes
//     overlap each other around doorways, and the previous version faithfully
//     counted an agent in every volume it touched, which inflated the totals to
//     roughly double the real headcount while agents were walking.
//   - Ownership goes to the most recently entered volume. On leaving a volume the
//     agent falls back to any other volume it is still standing in, so there is no
//     gap where an agent belongs to no zone at all.
//   - Because the map holds one entry per agent, the sum of all zone counts is
//     always equal to the number of agents inside the building, by construction.
//   - Agent identity is resolved with GetComponentInParent, so a collider on a
//     child character model resolves to the correct agent.
//   - ResyncFromScene iterates AgentDataTracker components rather than objects
//     tagged Agent, and picks the tightest containing volume.
//   - ForceRemoveAgent clears the agent regardless of which zone name is passed.
//   - Statics are cleared on subsystem registration, so returning from the main
//     menu to the simulation scene starts from a clean board.
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

    // The single zone each agent is currently assigned to. Source of truth.
    private static Dictionary<string, string> agentCurrentZone
        = new Dictionary<string, string>();

    // When the agent was assigned to that zone.
    private static Dictionary<string, float> agentEntryTime
        = new Dictionary<string, float>();

    // How many colliders of a given agent currently overlap a given zone. Used to
    // work out which volume to fall back to when the agent leaves one.
    private static Dictionary<string, Dictionary<string, int>> agentZoneTriggerCounts
        = new Dictionary<string, Dictionary<string, int>>();

    private static Dictionary<string, List<AgentZoneEntry>> zoneHistory
        = new Dictionary<string, List<AgentZoneEntry>>();

    private static List<string> zoneOrder = new List<string>();
    private static bool zonesRegistered = false;

    private static int totalAgentsExited = 0;
    private static int tickNumber = 0;
    private static string csvFileName = "ZoneOccupancyRecords.csv";
    private static string CsvFilePath => Path.Combine(Application.dataPath, csvFileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        agentCurrentZone.Clear();
        agentEntryTime.Clear();
        agentZoneTriggerCounts.Clear();
        zoneHistory.Clear();
        zoneOrder.Clear();
        zonesRegistered = false;
        totalAgentsExited = 0;
        tickNumber = 0;
    }

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
            RegisterZone(zoneObj.name);
    }

    private static void ResetCsvFile()
    {
        string path = CsvFilePath;
        if (File.Exists(path)) File.Delete(path);

        string backupPath = path + ".old";
        if (File.Exists(backupPath)) File.Delete(backupPath);
    }

    private static void RegisterZone(string zoneName)
    {
        if (agentZoneTriggerCounts.ContainsKey(zoneName)) return;

        agentZoneTriggerCounts[zoneName] = new Dictionary<string, int>();
        zoneHistory[zoneName] = new List<AgentZoneEntry>();
        zoneOrder.Add(zoneName);
    }

    // Resolves the owning agent from any collider, including one sitting on a
    // child character model. Returns null when the collider is not an agent.
    private static AgentDataTracker ResolveAgent(Collider other)
    {
        if (other == null) return null;
        return other.GetComponentInParent<AgentDataTracker>();
    }

    public static void ResetRuntimeCounts()
    {
        agentCurrentZone.Clear();
        agentEntryTime.Clear();
        foreach (var kv in agentZoneTriggerCounts) kv.Value.Clear();
        totalAgentsExited = 0;
    }

    // Recount everyone from scratch at the start of a run.
    public static void ResyncFromScene()
    {
        RegisterAllZones();
        ResetRuntimeCounts();

        AgentDataTracker[] agents =
            Object.FindObjectsByType<AgentDataTracker>(FindObjectsSortMode.None);

        GameObject[] zoneObjects = GameObject.FindGameObjectsWithTag("Zone");

        foreach (AgentDataTracker tracker in agents)
        {
            if (tracker == null) continue;
            if (string.IsNullOrEmpty(tracker.agentId)) continue;

            string id = tracker.agentId;
            Vector3 pos = tracker.transform.position;

            string bestZone = null;
            float bestVolume = float.MaxValue;

            foreach (GameObject zoneObj in zoneObjects)
            {
                float volume;
                if (!IsInsideZoneObject(zoneObj, pos, out volume)) continue;

                RegisterZone(zoneObj.name);

                // Mark the overlap so a later trigger exit can fall back correctly.
                agentZoneTriggerCounts[zoneObj.name][id] = 1;

                // The tightest containing volume is the real room. A hallway or an
                // exit volume that spills over is always the larger one.
                if (volume < bestVolume)
                {
                    bestVolume = volume;
                    bestZone = zoneObj.name;
                }
            }

            if (bestZone != null)
            {
                agentCurrentZone[id] = bestZone;
                agentEntryTime[id] = Time.time;
            }
        }
    }

    // Containment test that does not rely on ClosestPoint alone, which silently
    // fails on non convex mesh colliders and reports every point as inside.
    private static bool IsInsideZoneObject(GameObject zoneObj, Vector3 pos, out float volume)
    {
        volume = float.MaxValue;
        bool inside = false;

        foreach (Collider col in zoneObj.GetComponents<Collider>())
        {
            if (col == null || !col.enabled) continue;
            if (!col.bounds.Contains(pos)) continue;

            MeshCollider mc = col as MeshCollider;
            bool hit;

            if (mc != null && !mc.convex)
                hit = true;   // ClosestPoint is unreliable here, bounds is the best available
            else
                hit = (col.ClosestPoint(pos) - pos).sqrMagnitude < 0.0001f;

            if (!hit) continue;

            inside = true;
            Vector3 s = col.bounds.size;
            float v = s.x * s.y * s.z;
            if (v < volume) volume = v;
        }

        return inside;
    }

    private void OnTriggerEnter(Collider other)
    {
        AgentDataTracker tracker = ResolveAgent(other);
        if (tracker == null) return;

        string agentId = tracker.agentId;
        if (string.IsNullOrEmpty(agentId)) return;

        string zoneName = gameObject.name;
        RegisterZone(zoneName);

        int triggerCount;
        agentZoneTriggerCounts[zoneName].TryGetValue(agentId, out triggerCount);
        agentZoneTriggerCounts[zoneName][agentId] = triggerCount + 1;

        AssignZone(agentId, zoneName);
    }

    private void OnTriggerExit(Collider other)
    {
        AgentDataTracker tracker = ResolveAgent(other);
        if (tracker == null) return;

        string agentId = tracker.agentId;
        if (string.IsNullOrEmpty(agentId)) return;

        string zoneName = gameObject.name;
        RegisterZone(zoneName);

        int triggerCount;
        if (!agentZoneTriggerCounts[zoneName].TryGetValue(agentId, out triggerCount)) return;
        if (triggerCount <= 0) return;

        triggerCount--;
        agentZoneTriggerCounts[zoneName][agentId] = triggerCount;

        // Still overlapping this volume with another collider, nothing changes.
        if (triggerCount > 0) return;

        // Only matters if this was the zone the agent was assigned to.
        string current;
        if (!agentCurrentZone.TryGetValue(agentId, out current)) return;
        if (current != zoneName) return;

        // Hand the agent to any other volume it is still standing in.
        string fallback = FindOverlappingZone(agentId, zoneName);

        if (fallback != null)
            AssignZone(agentId, fallback);
        else
            ClearZone(agentId, "Exit");
    }

    private static string FindOverlappingZone(string agentId, string excludeZone)
    {
        foreach (var kv in agentZoneTriggerCounts)
        {
            if (kv.Key == excludeZone) continue;

            int c;
            if (kv.Value.TryGetValue(agentId, out c) && c > 0)
                return kv.Key;
        }
        return null;
    }

    // Moves the agent into a zone, closing off the previous one first.
    private static void AssignZone(string agentId, string zoneName)
    {
        string previous;
        if (agentCurrentZone.TryGetValue(agentId, out previous))
        {
            if (previous == zoneName) return;
            CloseHistory(agentId, previous);
            CreateZoneOccupancyRecord(previous, agentId, "Exit", 0f, Time.time);
        }

        agentCurrentZone[agentId] = zoneName;
        agentEntryTime[agentId] = Time.time;
        CreateZoneOccupancyRecord(zoneName, agentId, "Enter", Time.time, 0f);
    }

    private static void ClearZone(string agentId, string eventType)
    {
        string previous;
        if (!agentCurrentZone.TryGetValue(agentId, out previous)) return;

        float entryTime = agentEntryTime.ContainsKey(agentId) ? agentEntryTime[agentId] : 0f;

        CloseHistory(agentId, previous);
        agentCurrentZone.Remove(agentId);
        agentEntryTime.Remove(agentId);

        totalAgentsExited = AgentDataTracker.agentsExited;
        CreateZoneOccupancyRecord(previous, agentId, eventType, entryTime, Time.time);
    }

    private static void CloseHistory(string agentId, string zoneName)
    {
        if (!zoneHistory.ContainsKey(zoneName)) return;

        float entryTime = agentEntryTime.ContainsKey(agentId) ? agentEntryTime[agentId] : 0f;

        zoneHistory[zoneName].Add(new AgentZoneEntry
        {
            agentId = agentId,
            entryTime = entryTime,
            exitTime = Time.time
        });
    }

    // The zoneName argument is kept for compatibility with existing call sites but
    // is not trusted, because currentZone is often "Transition" or "Unknown" at the
    // moment an agent is destroyed.
    public static void ForceRemoveAgent(string zoneName, string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;

        ClearZone(agentId, "ForcedExit");

        foreach (var kv in agentZoneTriggerCounts)
            if (kv.Value.ContainsKey(agentId)) kv.Value[agentId] = 0;
    }

    public static void ForceRemoveAgent(string agentId)
    {
        ForceRemoveAgent(null, agentId);
    }

    public static Dictionary<string, int> GetZoneCounts()
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();

        foreach (string z in zoneOrder)
            counts[z] = 0;

        foreach (var kv in agentCurrentZone)
        {
            if (!counts.ContainsKey(kv.Value)) counts[kv.Value] = 0;
            counts[kv.Value]++;
        }

        return counts;
    }

    public static Dictionary<string, float> GetAgentsInZone(string zoneName)
    {
        Dictionary<string, float> result = new Dictionary<string, float>();

        foreach (var kv in agentCurrentZone)
        {
            if (kv.Value != zoneName) continue;
            result[kv.Key] = agentEntryTime.ContainsKey(kv.Key) ? agentEntryTime[kv.Key] : 0f;
        }

        return result;
    }

    public static List<AgentZoneEntry> GetZoneHistory(string zoneName)
    {
        if (zoneHistory.ContainsKey(zoneName))
            return new List<AgentZoneEntry>(zoneHistory[zoneName]);
        return new List<AgentZoneEntry>();
    }

    private static int CountIn(string zoneName)
    {
        int n = 0;
        foreach (var kv in agentCurrentZone)
            if (kv.Value == zoneName) n++;
        return n;
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
        record.value = CountIn(zoneName);
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
            CountIn(zoneName).ToString()
        };

        foreach (string z in zoneOrder)
            fields.Add(CountIn(z).ToString());

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
            if (File.Exists(backupPath)) File.Delete(backupPath);

            File.Move(path, backupPath);
            File.WriteAllText(path, header + "\n");
        }
    }
}