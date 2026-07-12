// AgentNoise.cs
// This file is now the per agent profile and health hub.
//
// It holds the age band, the spawn disability, and a live health value.
// Health drains from three hazard bands, all measured as pure distance to the
// nearest object tagged Fire, because there is no smoke in the scene yet.
//
//   Inside the fire        40 percent of max health per second
//   Next to the fire       20 percent of max health per second
//   Low visibility ring     5 percent of max health per second
//
// The strongest band wins, so an agent inside the fire takes 40 and not 40 plus
// 20 plus 5. All three bands also slow the agent down.
//
// When health reaches zero the agent is trapped through AgentDataTracker, and
// the log records whether fire damage or low visibility damage did more harm.
//
// Speed is calm while wandering before the alarm and full while evacuating, and
// is scaled by age, by spawn disability, by current health, and by hazard band.
//
// When real smoke is added later, only ReadHazardBand needs to change.
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class AgentNoise : MonoBehaviour
{
    // ==========================================================
    // Age
    // ==========================================================
    public enum AgeBand { Young, Adult, Elderly }

    [Header("Age, assigned at spawn")]
    public AgeBand ageBand = AgeBand.Adult;

    [Tooltip("Speed multiplier per age band. Elderly agents move slower.")]
    public float youngSpeedFactor = 1.10f;
    public float adultSpeedFactor = 1.00f;
    public float elderlySpeedFactor = 0.65f;

    [Header("Age spawn weights, they do not need to add up to 1")]
    public float youngWeight = 0.25f;
    public float adultWeight = 0.55f;
    public float elderlyWeight = 0.20f;

    // ==========================================================
    // Disability at spawn
    // ==========================================================
    public enum Disability { None, MobilityAid, Wheelchair }

    [Header("Disability at spawn")]
    public Disability spawnDisability = Disability.None;

    [Range(0f, 1f)] public float mobilityAidChance = 0.12f;
    [Range(0f, 1f)] public float wheelchairChance = 0.06f;

    public float mobilityAidFactor = 0.70f;
    public float wheelchairFactor = 0.55f;

    // ==========================================================
    // Health
    // ==========================================================
    [Header("Health")]
    public float maxHealth = 100f;
    public float health = 100f;

    [Header("Fallback hazard values, used only if no HazardSettings is in the scene")]
    public float fallbackInFireRadius = 1.5f;
    public float fallbackNearFireRadius = 3.5f;
    public float fallbackLowVisibilityRadius = 7f;
    public float fallbackInFireDrain = 0.40f;
    public float fallbackNearFireDrain = 0.20f;
    public float fallbackLowVisibilityDrain = 0.05f;

    // ==========================================================
    // Speed
    // ==========================================================
    [Header("Evacuation speed range, rolled once per agent")]
    public float minSpeed = 1.5f;
    public float maxSpeed = 2.5f;

    [Header("Calm pre alarm wander speed")]
    [Tooltip("Wander speed as a fraction of this agent evacuation speed.")]
    [Range(0.1f, 1f)] public float wanderSpeedFactor = 0.45f;

    [Tooltip("Lowest speed multiplier at zero health, so injured agents crawl rather than freeze.")]
    [Range(0.1f, 1f)] public float minHealthSpeedFactor = 0.35f;

    [Tooltip("Roll the profile in Awake rather than Start.")]
    public bool randomizeOnAwake = true;

    // ==========================================================
    // Hazard bands
    // ==========================================================
    public enum HazardBand { Clear, LowVisibility, NearFire, InFire }

    // ==========================================================
    // Read only, exposed for the tracker and the logger
    // ==========================================================
    public float baseEvacSpeed { get; private set; }
    public float fireDamageTotal { get; private set; }
    public float visibilityDamageTotal { get; private set; }
    public HazardBand currentBand { get; private set; } = HazardBand.Clear;
    public string dominantHazard { get; private set; } = "None";
    public string mobilityStatus { get; private set; } = "Able";
    public float distanceToFire { get; private set; } = Mathf.Infinity;

    // ==========================================================
    // Private
    // ==========================================================
    private NavMeshAgent agent;
    private AgentExitNavigator navigator;
    private AgentDataTracker tracker;

    private GameObject[] localFireCache = new GameObject[0];
    private float localCacheTimer = 0f;
    private const float localCacheInterval = 0.25f;

    private bool trapped = false;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (randomizeOnAwake) RollProfile();
    }

    void Start()
    {
        navigator = GetComponent<AgentExitNavigator>();
        tracker = GetComponent<AgentDataTracker>();

        if (!randomizeOnAwake) RollProfile();

        health = maxHealth;
        ApplySpeed();
    }

    // ----------------------------------------------------------
    // Profile roll
    // ----------------------------------------------------------
    void RollProfile()
    {
        // Weighted age band. A later scenario control can rewrite these weights
        // before spawn to set how many young, adult, and elderly agents appear.
        float total = Mathf.Max(0.0001f, youngWeight + adultWeight + elderlyWeight);
        float r = Random.value * total;

        if (r < youngWeight) ageBand = AgeBand.Young;
        else if (r < youngWeight + adultWeight) ageBand = AgeBand.Adult;
        else ageBand = AgeBand.Elderly;

        // Spawn disability.
        float disRoll = Random.value;
        if (disRoll < wheelchairChance) spawnDisability = Disability.Wheelchair;
        else if (disRoll < wheelchairChance + mobilityAidChance) spawnDisability = Disability.MobilityAid;
        else spawnDisability = Disability.None;

        // Evacuation speed roll, then scaled by age.
        if (minSpeed > maxSpeed) { float t = minSpeed; minSpeed = maxSpeed; maxSpeed = t; }
        float rolled = Random.Range(minSpeed, maxSpeed);
        baseEvacSpeed = rolled * AgeFactor();
    }

    float AgeFactor()
    {
        switch (ageBand)
        {
            case AgeBand.Young: return youngSpeedFactor;
            case AgeBand.Elderly: return elderlySpeedFactor;
            default: return adultSpeedFactor;
        }
    }

    float SpawnDisabilityFactor()
    {
        switch (spawnDisability)
        {
            case Disability.MobilityAid: return mobilityAidFactor;
            case Disability.Wheelchair: return wheelchairFactor;
            default: return 1f;
        }
    }

    // ----------------------------------------------------------
    // Main loop
    // ----------------------------------------------------------
    void Update()
    {
        if (trapped || agent == null) return;

        currentBand = ReadHazardBand();
        DrainHealth();
        UpdateMobilityStatus();
        ApplySpeed();

        if (health <= 0f)
        {
            health = 0f;
            TriggerTrapped();
        }
    }

    /// <summary>
    /// Pure distance hazard read. No smoke grid is used, because there is no
    /// smoke in the scene. When smoke is added later, replace only the
    /// LowVisibility branch below with a smoke lookup and leave the rest alone.
    /// </summary>
    HazardBand ReadHazardBand()
    {
        HazardSettings hs = HazardSettings.Instance;

        float inR = hs != null ? hs.inFireRadius : fallbackInFireRadius;
        float nearR = hs != null ? hs.nearFireRadius : fallbackNearFireRadius;
        float visR = hs != null ? hs.lowVisibilityRadius : fallbackLowVisibilityRadius;

        distanceToFire = hs != null
            ? hs.DistanceToNearestFire(transform.position)
            : LocalDistanceToNearestFire();

        // Strongest band wins. The drains never stack.
        if (distanceToFire <= inR) return HazardBand.InFire;
        if (distanceToFire <= nearR) return HazardBand.NearFire;
        if (distanceToFire <= visR) return HazardBand.LowVisibility;
        return HazardBand.Clear;
    }

    // Only used if there is no HazardSettings object in the scene.
    float LocalDistanceToNearestFire()
    {
        localCacheTimer += Time.deltaTime;
        if (localCacheTimer >= localCacheInterval || localFireCache.Length == 0)
        {
            localCacheTimer = 0f;
            localFireCache = GameObject.FindGameObjectsWithTag("Fire");
        }

        float nearest = Mathf.Infinity;
        foreach (GameObject fire in localFireCache)
        {
            if (fire == null) continue;
            float d = Vector3.Distance(transform.position, fire.transform.position);
            if (d < nearest) nearest = d;
        }
        return nearest;
    }

    void DrainHealth()
    {
        HazardSettings hs = HazardSettings.Instance;
        float dt = Time.deltaTime;
        float dmg;

        switch (currentBand)
        {
            case HazardBand.InFire:
                dmg = (hs != null ? hs.inFireDrainPerSec : fallbackInFireDrain) * maxHealth * dt;
                health -= dmg;
                fireDamageTotal += dmg;
                break;

            case HazardBand.NearFire:
                dmg = (hs != null ? hs.nearFireDrainPerSec : fallbackNearFireDrain) * maxHealth * dt;
                health -= dmg;
                fireDamageTotal += dmg;
                break;

            case HazardBand.LowVisibility:
                dmg = (hs != null ? hs.lowVisibilityDrainPerSec : fallbackLowVisibilityDrain) * maxHealth * dt;
                health -= dmg;
                visibilityDamageTotal += dmg;
                break;
        }

        // The dominant cause is what the trap reason and the logs report.
        if (fireDamageTotal <= 0f && visibilityDamageTotal <= 0f)
            dominantHazard = "None";
        else
            dominantHazard = fireDamageTotal >= visibilityDamageTotal ? "Fire" : "LowVisibility";
    }

    void UpdateMobilityStatus()
    {
        // A spawn disability sets the label. Otherwise falling health pushes an
        // able agent toward impaired, which is the disability from injury case.
        if (spawnDisability == Disability.Wheelchair) { mobilityStatus = "Wheelchair"; return; }
        if (spawnDisability == Disability.MobilityAid) { mobilityStatus = "MobilityAid"; return; }

        float pct = health / maxHealth;
        if (pct > 0.66f) mobilityStatus = "Able";
        else if (pct > 0.33f) mobilityStatus = "Impaired";
        else mobilityStatus = "SeverelyImpaired";
    }

    void ApplySpeed()
    {
        HazardSettings hs = HazardSettings.Instance;

        bool evacuating = navigator != null && navigator.isEvacuating;
        float regime = evacuating ? baseEvacSpeed : baseEvacSpeed * wanderSpeedFactor;

        float healthFactor = Mathf.Lerp(minHealthSpeedFactor, 1f, health / maxHealth);

        float hazardFactor = 1f;
        if (hs != null)
        {
            switch (currentBand)
            {
                case HazardBand.InFire: hazardFactor = hs.inFireSpeedFactor; break;
                case HazardBand.NearFire: hazardFactor = hs.nearFireSpeedFactor; break;
                case HazardBand.LowVisibility: hazardFactor = hs.lowVisibilitySpeedFactor; break;
            }
        }

        float finalSpeed = regime * SpawnDisabilityFactor() * healthFactor * hazardFactor;
        agent.speed = Mathf.Max(0.2f, finalSpeed);
    }

    void TriggerTrapped()
    {
        if (trapped) return;
        trapped = true;

        if (tracker != null)
            tracker.RecordTrapped(dominantHazard, fireDamageTotal, visibilityDamageTotal);
    }
}