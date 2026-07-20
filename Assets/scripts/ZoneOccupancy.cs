// ZoneOccupancy.cs
//
// CHANGES IN THIS VERSION
//   - Occupancy is now derived from a set of agent ids per zone, not from a
//     separate integer counter. Counting the same agent twice is impossible by
//     construction, because a dictionary key can only exist once.
//   - Agent identity is resolved with GetComponentInParent, so a collider on a
//     child character model still resolves to the correct agent instead of
//     falling back to the shared "unknown" key.
//   - ResyncFromScene now iterates AgentDataTracker components rather than every
//     object tagged Agent, so child models that carry the Agent tag can no longer
//     each add one to the room they are standing in.
//   - OnTriggerExit no longer removes an agent that was never counted, which was
//     causing phantom decrements in unrelated rooms.
//   - ForceRemoveAgent now removes the agent from every zone, because currentZone
//     is often "Transition" or "Unknown" at the moment an agent is destroyed.
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

    // Source of truth. Zone name to the set of agent ids currently inside it.
    private static Dictionary<string, Dictionary<string, float>> agentsInZone
        = new Dictionary<string, Dictionary<string, float>>();

    // How many colliders belonging to a given agent are currently overlapping a
    // given zone. Only used to know when the last one has left.
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
        agentsInZone.Clear();
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
        if (agentsInZone.ContainsKey(zoneName)) return;

        agentsInZone[zoneName] = new Dictionary<string, float>();
        agentZoneTriggerCounts[zoneName] = new Dictionary<string, int>();
        zoneHistory[zoneName] = new List<AgentZoneEntry>();
        zoneOrder.Add(zoneName);
    }

    // Resolves the owning agent from any collider, including a collider sitting on
    // a child character model. Returns null when the collider is not an agent.
    private static AgentDataTracker ResolveAgent(Collider other)
    {
        if (other == null) return null;
        return other.GetComponentInParent<AgentDataTracker>();
    }

    // Clears the live numbers but keeps the zones registered.
    public static void ResetRuntimeCounts()
    {
        foreach (var kv in agentsInZone) kv.Value.Clear();
        foreach (var kv in agentZoneTriggerCounts) kv.Value.Clear();
        totalAgentsExited = 0;
    }

    // Recount everyone from scratch at the start of a run.
    public static void ResyncFromScene()
    {
        RegisterAllZones();
        ResetRuntimeCounts();

        // Iterate real agents, one component per agent, so duplicate tagged child
        // models cannot each contribute a count.
        AgentDataTracker[] agents =
            Object.FindObjectsByType<AgentDataTracker>(FindObjectsSortMode.None);

        GameObject[] zoneObjects = GameObject.FindGameObjectsWithTag("Zone");

        foreach (AgentDataTracker tracker in agents)
        {
            if (tracker == null) continue;

            string id = string.IsNullOrEmpty(tracker.agentId) ? null : tracker.agentId;
            if (id == null) continue;

            Vector3 pos = tracker.transform.position;

            // Track which zone names this agent has already been placed in, so two
            // zone objects sharing a name cannot double count either.
            HashSet<string> placed = new HashSet<string>();

            foreach (GameObject zoneObj in zoneObjects)
            {
                string zn = zoneObj.name;
                if (placed.Contains(zn)) continue;

                if (!IsInsideZoneObject(zoneObj, pos)) continue;

                RegisterZone(zn);
                placed.Add(zn);
                agentsInZone[zn][id] = Time.time;
                agentZoneTriggerCounts[zn][id] = 1;
            }
        }
    }

    // Containment test that does not rely on ClosestPoint, which silently fails on
    // non convex mesh colliders and reports every point as inside.
    private static bool IsInsideZoneObject(GameObject zoneObj, Vector3 pos)
    {
        foreach (Collider col in zoneObj.GetComponents<Collider>())
        {
            if (col == null || !col.enabled) continue;

            if (!col.bounds.Contains(pos)) continue;

            MeshCollider mc = col as MeshCollider;
            if (mc != null && !mc.convex)
            {
                // ClosestPoint is unreliable here, so the bounds test is the best
                // available answer for a non convex mesh volume.
                return true;
            }

            if ((col.ClosestPoint(pos) - pos).sqrMagnitude < 0.0001f)
                return true;
        }
        return false;
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

        // Membership is a set, so re entering with a second collider is harmless.
        if (!agentsInZone[zoneName].ContainsKey(agentId))
        {
            agentsInZone[zoneName][agentId] = Time.time;
            CreateZoneOccupancyRecord(zoneName, agentId, "Enter", Time.time, 0f);
        }
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
        if (!agentZoneTriggerCounts[zoneName].TryGetValue(agentId, out triggerCount))
            return;

        // An agent that was never counted must not be removed.
        if (triggerCount <= 0) return;

        triggerCount--;
        agentZoneTriggerCounts[zoneName][agentId] = triggerCount;

        if (triggerCount > 0) return;

        RemoveFromZone(zoneName, agentId, "Exit");
    }

    private static void RemoveFromZone(string zoneName, string agentId, string eventType)
    {
        if (!agentsInZone.ContainsKey(zoneName)) return;
        if (!agentsInZone[zoneName].ContainsKey(agentId)) return;

        float entryTime = agentsInZone[zoneName][agentId];
        float exitTime = Time.time;

        zoneHistory[zoneName].Add(new AgentZoneEntry
        {
            agentId = agentId,
            entryTime = entryTime,
            exitTime = exitTime
        });

        agentsInZone[zoneName].Remove(agentId);
        agentZoneTriggerCounts[zoneName][agentId] = 0;

        totalAgentsExited = AgentDataTracker.agentsExited;
        CreateZoneOccupancyRecord(zoneName, agentId, eventType, entryTime, exitTime);
    }

    // The zoneName argument is kept for compatibility with existing call sites but
    // is no longer trusted, because currentZone is frequently "Transition" or
    // "Unknown" at the moment an agent is destroyed. The agent is removed from
    // every zone it is recorded in.
    public static void ForceRemoveAgent(string zoneName, string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;

        foreach (string zn in new List<string>(agentsInZone.Keys))
        {
            if (agentsInZone[zn].ContainsKey(agentId))
                RemoveFromZone(zn, agentId, "ForcedExit");
        }
    }

    public static void ForceRemoveAgent(string agentId)
    {
        ForceRemoveAgent(null, agentId);
    }

    public static Dictionary<string, int> GetZoneCounts()
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        foreach (var kv in agentsInZone)
            counts[kv.Key] = kv.Value.Count;
        return counts;
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

    private static int CountIn(string zoneName)
    {
        return agentsInZone.ContainsKey(zoneName) ? agentsInZone[zoneName].Count : 0;
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