// AgentSpawner.cs
// One in the scene.
//
// Spawns agents from a single prefab, scattered across the zone box colliders and
// snapped to the NavMesh. The count updates live as the user drags the slider, and
// the age and disability split is applied as exact counts, not random chances, so
// the percentages are respected precisely.
//
// CHANGES IN THIS VERSION
//   - Zones whose name matches the exclusion list are no longer used as spawn
//     areas. The exit markers are tagged Zone so that occupancy tracking still
//     counts agents reaching them, but they sit outside the building, so agents
//     were legitimately spawning in the grass.
//   - The point returned by NavMesh.SamplePosition is now checked to make sure it
//     is still inside the zone collider. Sampling uses axis aligned bounds, which
//     extend past the real room on angled walls, and the snap itself can pull a
//     point up to sampleRadius away.
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class AgentSpawner : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject agentPrefab;

    [Header("Limits")]
    public int maxAgents = 50;

    [Header("Distribution, percentages")]
    [Range(0f, 100f)] public float youngPct = 33f;
    [Range(0f, 100f)] public float adultPct = 34f;
    [Range(0f, 100f)] public float elderlyPct = 33f;
    [Range(0f, 100f)] public float disabledPct = 15f;

    [Header("Spawning")]
    public string zoneTag = "Zone";

    [Tooltip("Zones whose name starts with any of these are never used for spawning. Case is ignored.")]
    public List<string> excludedZonePrefixes = new List<string> { "Exit" };

    [Tooltip("Tries per agent to find a spot on the NavMesh inside a zone.")]
    public int samplePositionTries = 12;

    [Tooltip("How far the sampled point may be pulled to reach the NavMesh. Keep small so agents do not snap through walls.")]
    public float sampleRadius = 1f;

    [Tooltip("Log which zones were used and which were excluded, once at startup.")]
    public bool logZoneSelection = true;

    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Collider> zoneColliders = new List<Collider>();
    private readonly List<float> zoneWeights = new List<float>();
    private float totalWeight = 0f;

    void Awake()
    {
        CollectZones();
        SimulationSettings.Load();

        SetAgentCount(SimulationSettings.AgentCount);
        SetDistribution(
            SimulationSettings.YoungPct,
            SimulationSettings.AdultPct,
            SimulationSettings.ElderlyPct,
            SimulationSettings.DisabledPct
        );
    }

    // Every collider on every object tagged Zone is a place agents can appear,
    // unless the zone name is excluded. Bigger rooms get proportionally more
    // people through area weighting.
    void CollectZones()
    {
        zoneColliders.Clear();
        zoneWeights.Clear();
        totalWeight = 0f;

        List<string> used = new List<string>();
        List<string> skipped = new List<string>();

        GameObject[] zones = GameObject.FindGameObjectsWithTag(zoneTag);
        foreach (GameObject z in zones)
        {
            if (IsExcluded(z.name))
            {
                skipped.Add(z.name);
                continue;
            }

            foreach (Collider col in z.GetComponents<Collider>())
            {
                if (col == null) continue;
                Bounds b = col.bounds;
                float area = Mathf.Max(0.01f, b.size.x * b.size.z);
                zoneColliders.Add(col);
                zoneWeights.Add(area);
                totalWeight += area;
            }

            used.Add(z.name);
        }

        if (logZoneSelection)
        {
            Debug.Log("AgentSpawner spawn zones: " + string.Join(", ", used));
            if (skipped.Count > 0)
                Debug.Log("AgentSpawner excluded zones: " + string.Join(", ", skipped));
        }
    }

    // Matches on prefix rather than exact name, so Exit 1, Exit 2, and Exit 3 are
    // all covered by the single entry "Exit".
    bool IsExcluded(string zoneName)
    {
        if (string.IsNullOrEmpty(zoneName)) return false;

        foreach (string prefix in excludedZonePrefixes)
        {
            if (string.IsNullOrEmpty(prefix)) continue;
            if (zoneName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public int SpawnedCount => spawned.Count;

    // ---------------- Called by the UI ----------------

    public void SetAgentCount(int n)
    {
        n = Mathf.Clamp(n, 0, maxAgents);

        while (spawned.Count < n) SpawnOne();
        while (spawned.Count > n) RemoveOne();

        ApplyDistribution();
    }

    public void SetDistribution(float young, float adult, float elderly, float disabled)
    {
        youngPct = young;
        adultPct = adult;
        elderlyPct = elderly;
        disabledPct = disabled;
        ApplyDistribution();
    }

    public void ClearAll()
    {
        foreach (GameObject a in spawned)
            if (a != null) Destroy(a);
        spawned.Clear();
    }

    // ---------------- Internals ----------------

    void SpawnOne()
    {
        if (agentPrefab == null) return;
        if (zoneColliders.Count == 0) CollectZones();
        if (zoneColliders.Count == 0) return;

        if (TryGetSpawnPoint(out Vector3 pos))
        {
            GameObject a = Instantiate(agentPrefab, pos, Quaternion.identity);
            NavMeshAgent nav = a.GetComponent<NavMeshAgent>();
            if (nav != null) nav.Warp(pos);
            spawned.Add(a);
        }
    }

    void RemoveOne()
    {
        if (spawned.Count == 0) return;

        int idx = spawned.Count - 1;
        GameObject a = spawned[idx];

        // Take the agent out of its zone count before destroying it, since
        // destroying a collider does not reliably fire OnTriggerExit.
        if (a != null)
        {
            AgentDataTracker tr = a.GetComponent<AgentDataTracker>();
            if (tr != null) ZoneOccupancy.ForceRemoveAgent(tr.currentZone, tr.agentId);
            Destroy(a);
        }

        spawned.RemoveAt(idx);
    }

    bool TryGetSpawnPoint(out Vector3 result)
    {
        for (int i = 0; i < samplePositionTries; i++)
        {
            Collider c = PickWeightedZone();
            if (c == null) break;

            Bounds b = c.bounds;
            Vector3 p = new Vector3(
                Random.Range(b.min.x, b.max.x),
                b.center.y,
                Random.Range(b.min.z, b.max.z)
            );

            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                continue;

            // The snapped point may have moved outside the room, and the random
            // point itself may have come from the corner of an axis aligned box
            // that sits beyond an angled wall. Reject anything not still inside.
            if (!IsInsideCollider(c, hit.position)) continue;

            result = hit.position;
            return true;
        }

        result = Vector3.zero;
        return false;
    }

    // ClosestPoint returns the point itself only when the point is inside the
    // collider. Non convex mesh colliders do not support it, so those fall back to
    // a bounds test, which is the best available.
    bool IsInsideCollider(Collider col, Vector3 point)
    {
        if (col == null) return false;

        MeshCollider mc = col as MeshCollider;
        if (mc != null && !mc.convex)
            return col.bounds.Contains(point);

        return (col.ClosestPoint(point) - point).sqrMagnitude < 0.01f;
    }

    Collider PickWeightedZone()
    {
        if (totalWeight <= 0f || zoneColliders.Count == 0) return null;

        float r = Random.value * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < zoneColliders.Count; i++)
        {
            cumulative += zoneWeights[i];
            if (r <= cumulative) return zoneColliders[i];
        }

        return zoneColliders[zoneColliders.Count - 1];
    }

    // Turns the percentages into exact counts and hands each agent a fixed profile,
    // so 20 percent elderly across 50 agents is exactly 10 elderly, every time.
    void ApplyDistribution()
    {
        int n = spawned.Count;
        if (n == 0) return;

        int young = Mathf.RoundToInt(n * youngPct / 100f);
        int elderly = Mathf.RoundToInt(n * elderlyPct / 100f);
        int adult = n - young - elderly;

        // Rounding can push the total off by one either way. Correct it on adults.
        while (young + adult + elderly > n) { if (adult > 0) adult--; else if (young > 0) young--; else elderly--; }
        while (young + adult + elderly < n) adult++;

        List<AgentNoise.AgeBand> ages = new List<AgentNoise.AgeBand>();
        for (int i = 0; i < young; i++) ages.Add(AgentNoise.AgeBand.Young);
        for (int i = 0; i < adult; i++) ages.Add(AgentNoise.AgeBand.Adult);
        for (int i = 0; i < elderly; i++) ages.Add(AgentNoise.AgeBand.Elderly);
        Shuffle(ages);

        int disabledCount = Mathf.RoundToInt(n * disabledPct / 100f);
        List<bool> disabled = new List<bool>();
        for (int i = 0; i < n; i++) disabled.Add(i < disabledCount);
        Shuffle(disabled);

        for (int i = 0; i < n; i++)
        {
            if (spawned[i] == null) continue;
            AgentNoise an = spawned[i].GetComponent<AgentNoise>();
            if (an != null) an.AssignProfile(ages[i], disabled[i]);
        }
    }

    void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }
}