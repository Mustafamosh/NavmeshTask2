// AgentSpawner.cs
// NEW FILE. One in the scene.
//
// Spawns agents from a single prefab, scattered across the zone box colliders and
// snapped to the NavMesh. The count updates live as the user drags the slider, and
// the age and disability split is applied as exact counts, not random chances, so
// the percentages are respected precisely.
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
    [Tooltip("Tries per agent to find a spot on the NavMesh inside a zone.")]
    public int samplePositionTries = 12;
    public float sampleRadius = 3f;

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

    // Every box collider on every object tagged Zone is a place agents can appear.
    // Bigger rooms get proportionally more people through area weighting.
    void CollectZones()
    {
        zoneColliders.Clear();
        zoneWeights.Clear();
        totalWeight = 0f;

        GameObject[] zones = GameObject.FindGameObjectsWithTag(zoneTag);
        foreach (GameObject z in zones)
        {
            foreach (Collider col in z.GetComponents<Collider>())
            {
                if (col == null) continue;
                Bounds b = col.bounds;
                float area = Mathf.Max(0.01f, b.size.x * b.size.z);
                zoneColliders.Add(col);
                zoneWeights.Add(area);
                totalWeight += area;
            }
        }
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

            if (NavMesh.SamplePosition(p, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
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