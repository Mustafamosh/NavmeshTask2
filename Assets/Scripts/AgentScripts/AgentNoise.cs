// AgentNoise.cs
// The per agent profile and health hub.
//
// It holds the age band, the spawn disability, and a live health value.
// Health drains from three hazard bands, all measured as pure distance to the
// nearest object tagged Fire, because there is no smoke object in the scene yet.
// The strongest band wins, so the drains never stack.
//
// CHANGES IN THIS VERSION
//   - Disability is simplified. There is now only None or MobilityAid at spawn.
//     The wheelchair category is gone.
//   - Being hurt is now its own status. A healthy agent is Able, and as health
//     falls it becomes Injured and then SeverelyInjured. An agent that spawned
//     with a mobility aid and then gets hurt is reported as MobilityAid Injured,
//     so the log always shows both facts.
//   - Health drains far more slowly, so agents degrade and get trapped gradually.
//   - The speed floor at low health is raised, so injured agents move slowly but
//     do not crawl.
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
    public float elderlySpeedFactor = 0.70f;

    [Header("Age spawn weights, they do not need to add up to 1")]
    public float youngWeight = 0.25f;
    public float adultWeight = 0.55f;
    public float elderlyWeight = 0.20f;

    // ==========================================================
    // Disability at spawn
    // ==========================================================
    public enum Disability { None, MobilityAid }

    [Header("Disability at spawn")]
    public Disability spawnDisability = Disability.None;

    [Tooltip("Chance an agent spawns already using a mobility aid.")]
    [Range(0f, 1f)] public float mobilityAidChance = 0.15f;

    [Tooltip("Speed multiplier for an agent using a mobility aid.")]
    public float mobilityAidFactor = 0.65f;

    // ==========================================================
    // Health
    // ==========================================================
    [Header("Health")]
    public float maxHealth = 100f;
    public float health = 100f;

    [Tooltip("Below this fraction of max health the agent is reported as Injured.")]
    [Range(0f, 1f)] public float injuredThreshold = 0.70f;

    [Tooltip("Below this fraction of max health the agent is reported as SeverelyInjured.")]
    [Range(0f, 1f)] public float severelyInjuredThreshold = 0.35f;

    [Header("Fallback hazard values, used only if no HazardSettings is in the scene")]
    public float fallbackInFireRadius = 1.2f;
    public float fallbackNearFireRadius = 2.5f;
    public float fallbackLowVisibilityRadius = 4f;
    public float fallbackInFireDrain = 0.15f;
    public float fallbackNearFireDrain = 0.07f;
    public float fallbackLowVisibilityDrain = 0.02f;

    // ==========================================================
    // Speed
    // ==========================================================
    [Header("Evacuation speed range, rolled once per agent")]
    public float minSpeed = 1.5f;
    public float maxSpeed = 2.5f;

    [Header("Calm pre alarm wander speed")]
    [Tooltip("Wander speed as a fraction of this agent evacuation speed.")]
    [Range(0.1f, 1f)] public float wanderSpeedFactor = 0.45f;

    [Tooltip("Speed multiplier at zero health. Raised so hurt agents keep moving.")]
    [Range(0.1f, 1f)] public float minHealthSpeedFactor = 0.60f;

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

    private bool profileRolled = false;
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
        UpdateMobilityStatus();
        ApplySpeed();
    }

    // ----------------------------------------------------------
    // Profile roll, guarded so it can only ever happen once
    // ----------------------------------------------------------
    void RollProfile()
    {
        if (profileRolled) return;
        profileRolled = true;

        // Weighted age band. A later scenario control can rewrite these weights
        // before spawn to set how many young, adult, and elderly agents appear.
        float total = Mathf.Max(0.0001f, youngWeight + adultWeight + elderlyWeight);
        float r = Random.value * total;

        if (r < youngWeight) ageBand = AgeBand.Young;
        else if (r < youngWeight + adultWeight) ageBand = AgeBand.Adult;
        else ageBand = AgeBand.Elderly;

        // Spawn disability. Only two options now, none or a mobility aid.
        spawnDisability = Random.value < mobilityAidChance
            ? Disability.MobilityAid
            : Disability.None;

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
        return spawnDisability == Disability.MobilityAid ? mobilityAidFactor : 1f;
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
    /// smoke object in the scene. When smoke is added later, replace only the
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

    /// <summary>
    /// Reports the effective mobility of the agent. A spawn mobility aid and an
    /// injury are separate facts, so an agent can be both at once and the log
    /// will say so.
    /// </summary>
    void UpdateMobilityStatus()
    {
        float pct = maxHealth > 0f ? health / maxHealth : 0f;

        string injury;
        if (pct > injuredThreshold) injury = "";
        else if (pct > severelyInjuredThreshold) injury = "Injured";
        else injury = "SeverelyInjured";

        bool aided = spawnDisability == Disability.MobilityAid;

        if (aided && injury != "") mobilityStatus = "MobilityAid " + injury;
        else if (aided) mobilityStatus = "MobilityAid";
        else if (injury != "") mobilityStatus = injury;
        else mobilityStatus = "Able";
    }

    void ApplySpeed()
    {
        HazardSettings hs = HazardSettings.Instance;

        bool evacuating = navigator != null && navigator.isEvacuating;
        float regime = evacuating ? baseEvacSpeed : baseEvacSpeed * wanderSpeedFactor;

        float healthFactor = Mathf.Lerp(minHealthSpeedFactor, 1f, maxHealth > 0f ? health / maxHealth : 0f);

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
        agent.speed = Mathf.Max(0.5f, finalSpeed);
    }

    void TriggerTrapped()
    {
        if (trapped) return;
        trapped = true;

        if (tracker != null)
            tracker.RecordTrapped(dominantHazard, fireDamageTotal, visibilityDamageTotal);
    }
}