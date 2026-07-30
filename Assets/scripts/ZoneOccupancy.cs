// ZoneOccupancy.cs
//
// CHANGES IN THIS VERSION
//   - All System.IO file writing has been removed. The CSV was written to
//     Application.dataPath, which is a URL in WebGL and a read only folder in a
//     desktop build, so every write threw DirectoryNotFoundException. Because the
//     write sat inside AssignZone and ClearZone, the exception aborted zone
//     assignment and prevented agents from being removed on exit.
//   - Rows are now buffered in memory and can be fetched with GetCsv, so the data
//     is still available for the dashboard without ever touching a filesystem.
//
// PREVIOUS BEHAVIOUR, UNCHANGED
//   An agent belongs to exactly ONE zone at a time. Ownership goes to the most
//   recently entered volume, falling back to any other volume the agent is still
//   standing in, so the sum of zone counts always equals the number inside.
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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

    // How many colliders of a given agent currently overlap a given zone.
    private static Dictionary<string, Dictionary<string, int>> agentZoneTriggerCounts
        = new Dictionary<string, Dictionary<string, int>>();

    private static Dictionary<string, List<AgentZoneEntry>> zoneHistory
        = new Dictionary<string, List<AgentZoneEntry>>();

    private static List<string> zoneOrder = new List<string>();
    private static bool zonesRegistered = false;

    private static int totalAgentsExited = 0;
    private static int tickNumber = 0;

    // Every CSV row produced this run, held in memory instead of on disk.
    private static readonly List<string> csvRows = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        agentCurrentZone.Clear();
        agentEntryTime.Clear();
        agentZoneTriggerCounts.Clear();
        zoneHistory.Clear();
        zoneOrder.Clear();
        csvRows.Clear();
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

        csvRows.Clear();

        GameObject[] zoneObjects = GameObject.FindGameObjectsWithTag("Zone");
        foreach (GameObject zoneObj in zoneObjects)
            RegisterZone(zoneObj.name);
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

                agentZoneTriggerCounts[zoneObj.name][id] = 1;

                // The tightest containing volume is the real room.
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
                hit = true;
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

        if (triggerCount > 0) return;

        string current;
        if (!agentCurrentZone.TryGetValue(agentId, out current)) return;
        if (current != zoneName) return;

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
    // The dictionary is now updated BEFORE the record is created, so even if
    // record creation ever fails the occupancy state stays correct.
    private static void AssignZone(string agentId, string zoneName)
    {
        string previous;
        bool hadPrevious = agentCurrentZone.TryGetValue(agentId, out previous);

        if (hadPrevious && previous == zoneName) return;

        agentCurrentZone[agentId] = zoneName;
        agentEntryTime[agentId] = Time.time;

        if (hadPrevious)
        {
            CloseHistory(agentId, previous);
            CreateZoneOccupancyRecord(previous, agentId, "Exit", 0f, Time.time);
        }

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
            time: SimulationLogger.GetSimulationTime(),
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

        // Goes into the main jsonl log, which is what the dashboard reads and what
        // the Stop button downloads.
        SimulationLogger.WriteRecord(record);

        // Also kept as a CSV row in memory, for anyone who wants the flat format.
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

        csvRows.Add(string.Join(",", fields));
    }

    private static string BuildCsvHeader()
    {
        string baseHeader = "sensorId,sensorTypeString,location,timestamp,tickNumber,value,agentId,speed,hasExited,timeEnteringZone,exitTime,eventDetails,totalAgentsExited,zoneCount";
        string zoneColumns = string.Join(",", zoneOrder.Select(z => "zone_" + z.Replace(" ", "_")));
        return zoneColumns.Length > 0 ? baseHeader + "," + zoneColumns : baseHeader;
    }

    /// <summary>
    /// The whole run as a CSV string, header included. Nothing is written to disk,
    /// so this is safe in WebGL. Call it if you ever want a CSV download alongside
    /// the jsonl one.
    /// </summary>
    public static string GetCsv()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(BuildCsvHeader()).Append('\n');
        foreach (string row in csvRows)
            sb.Append(row).Append('\n');
        return sb.ToString();
    }
}