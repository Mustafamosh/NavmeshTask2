// AgentDataTracker.cs
// Consolidated: absorbs AgentExitBehavior and AgentAnimator.
//
// CHANGES IN THIS VERSION
//   - The spawn PROFILE record is no longer dropped. In the previous run zero
//     profile records were written, because agents spawned before
//     SimulationLogger had set its file path, and WriteProfileRecord returned
//     early. It now retries every frame until the logger is ready, then writes
//     once and stops.
//   - Disability is simplified to None or MobilityAid. Injury is reported
//     separately through mobilityStatus, so an agent can be recorded as both
//     using a mobility aid and injured at the same time.
//   - Fire is not an instant kill. AgentNoise drains health from the fire, near
//     fire, and low visibility bands, and calls RecordTrapped at zero health.
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections.Generic;

public class AgentDataTracker : MonoBehaviour
{
    [Header("ID Display")]
    public TextMeshPro idLabel;

    // --- Schema Fields (public so the logger can read them) ---
    public string agentId;
    public float speed;
    public string currentZone = "Unknown";
    public float timeEnteringZone;
    public bool hasExited = false;
    public float exitTime = 0f;

    // --- Profile mirror, refreshed from AgentNoise every frame ---
    public string ageBand = "Adult";
    public string spawnDisability = "None";
    public string mobilityStatus = "Able";
    public float health = 100f;
    public float maxHealth = 100f;
    public string hazardBand = "Clear";
    public float distanceToFire = -1f;
    public float fireDamageTotal = 0f;
    public float visibilityDamageTotal = 0f;
    public string trapReason = "None";

    // --- Path History ---
    public List<string> pathHistory = new List<string>();

    // --- Counters ---
    public static int agentsTrapped = 0;
    public static int agentsExited = 0;

    // --- Exit Detection Radius (absorbed from AgentExitBehavior) ---
    public float exitRadius = 1.5f;

    // --- Private ---
    private NavMeshAgent navAgent;
    private Animator agentAnimator;      // Absorbed from AgentAnimator
    private AgentNoise profile;          // Source of truth for age, disability, health
    private GameObject[] exits;
    private bool lifecycleEnded = false; // Guard so exit and trap never both fire
    private bool profileWritten = false; // Guard so the spawn record writes exactly once

    // --- ID Counter ---
    private static int nextId = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        nextId = 0;
        agentsTrapped = 0;
        agentsExited = 0;
    }

    void Awake()
    {
        agentId = "Agent-" + nextId++;

        if (idLabel == null)
            idLabel = GetComponentInChildren<TextMeshPro>();

        if (idLabel != null)
            idLabel.text = agentId;
    }

    void Start()
    {
        navAgent = GetComponent<NavMeshAgent>();
        agentAnimator = GetComponentInChildren<Animator>();
        profile = GetComponent<AgentNoise>();

        // Cache exits once at start (absorbed from AgentExitBehavior)
        exits = GameObject.FindGameObjectsWithTag("Exit");

        MirrorProfile();
        TryWriteProfileRecord();
    }

    void Update()
    {
        if (lifecycleEnded) return;

        // Retry the spawn record until the logger has opened its file.
        if (!profileWritten) TryWriteProfileRecord();

        // --- Speed tracking and animation (absorbed from AgentAnimator) ---
        if (navAgent != null)
        {
            speed = navAgent.velocity.magnitude;
            if (agentAnimator != null)
                agentAnimator.SetFloat("Speed", speed);
        }

        // --- Keep the logged profile in sync with AgentNoise ---
        MirrorProfile();

        // --- Exit proximity check (absorbed from AgentExitBehavior) ---
        foreach (GameObject exit in exits)
        {
            if (exit == null) continue;
            if (Vector3.Distance(transform.position, exit.transform.position) < exitRadius)
            {
                RecordExit(exit.name);
                return;
            }
        }

        // NOTE: fire is no longer an instant kill here. AgentNoise handles the
        // gradual health drain and calls RecordTrapped once health reaches zero.
    }

    void MirrorProfile()
    {
        if (profile == null) return;

        ageBand = profile.ageBand.ToString();
        spawnDisability = profile.spawnDisability.ToString();
        mobilityStatus = profile.mobilityStatus;
        health = profile.health;
        maxHealth = profile.maxHealth;
        hazardBand = profile.currentBand.ToString();
        distanceToFire = float.IsInfinity(profile.distanceToFire) ? -1f : profile.distanceToFire;
        fireDamageTotal = profile.fireDamageTotal;
        visibilityDamageTotal = profile.visibilityDamageTotal;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Zone"))
        {
            currentZone = other.gameObject.name;
            timeEnteringZone = Time.time;
            pathHistory.Add(currentZone + " | T=" + timeEnteringZone.ToString("F2"));
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Zone"))
            currentZone = "Transition";
    }

    // Called when the agent reaches an exit.
    public void RecordExit(string exitName)
    {
        if (lifecycleEnded) return;
        lifecycleEnded = true;

        hasExited = true;
        exitTime = Time.time;
        agentsExited++;

        pathHistory.Add("Exited via " + exitName + " | T=" + exitTime.ToString("F2"));

        string details =
            "Exited via " + exitName +
            " | Age: " + ageBand +
            " | Disability: " + spawnDisability +
            " | Mobility at exit: " + mobilityStatus +
            " | Health remaining: " + health.ToString("F1") + " of " + maxHealth.ToString("F0") +
            " | Fire damage taken: " + fireDamageTotal.ToString("F1") +
            " | Visibility damage taken: " + visibilityDamageTotal.ToString("F1") +
            " | Path: " + string.Join(" > ", pathHistory);

        WriteLifecycleRecord(details);

        ZoneOccupancy.ForceRemoveAgent(currentZone, agentId);
        Destroy(gameObject);
    }

    // Called by AgentNoise when health reaches zero.
    // reason is Fire or LowVisibility, whichever caused more cumulative damage.
    public void RecordTrapped(string reason, float fireDamage, float visibilityDamage)
    {
        if (lifecycleEnded) return;
        lifecycleEnded = true;

        agentsTrapped++;
        trapReason = reason;
        fireDamageTotal = fireDamage;
        visibilityDamageTotal = visibilityDamage;

        pathHistory.Add("Trapped at " + currentZone + " | T=" + Time.time.ToString("F2"));

        string details =
            "Trapped at " + currentZone +
            " | Cause: " + reason +
            " | Age: " + ageBand +
            " | Disability: " + spawnDisability +
            " | Mobility at collapse: " + mobilityStatus +
            " | Fire damage taken: " + fireDamage.ToString("F1") +
            " | Visibility damage taken: " + visibilityDamage.ToString("F1") +
            " | Distance to nearest fire: " + distanceToFire.ToString("F2") +
            " | Survived: " + Time.time.ToString("F1") + " seconds" +
            " | Path: " + string.Join(" > ", pathHistory);

        WriteLifecycleRecord(details);

        ZoneOccupancy.ForceRemoveAgent(currentZone, agentId);
        Destroy(gameObject);
    }

    // Backward compatible overload.
    public void RecordTrapped(string reason)
    {
        RecordTrapped(reason, fireDamageTotal, visibilityDamageTotal);
    }

    // Written once per agent, as soon as the logger has a file open, so the AI
    // knows who each person is before anything happens to them.
    private void TryWriteProfileRecord()
    {
        if (profileWritten) return;
        if (string.IsNullOrEmpty(SimulationLogger.filePath)) return;

        profileWritten = true;
        MirrorProfile();

        SimulationRecord record = new SimulationRecord(
            id: "PROFILE-" + agentId,
            type: SensorType.AgentProfile,
            loc: currentZone,
            time: Time.time,
            tick: -1
        );

        record.agentId = agentId;
        record.ageBand = ageBand;
        record.disability = spawnDisability;
        record.mobilityStatus = mobilityStatus;
        record.health = health;
        record.maxHealth = maxHealth;
        record.speed = profile != null ? profile.baseEvacSpeed : 0f;
        record.eventDetails =
            "Spawn profile" +
            " | Age: " + ageBand +
            " | Disability: " + spawnDisability +
            " | Base evacuation speed: " + (profile != null ? profile.baseEvacSpeed.ToString("F2") : "0") +
            " | Starting health: " + health.ToString("F0");

        SimulationLogger.WriteRecord(record);
    }

    private void WriteLifecycleRecord(string details)
    {
        if (string.IsNullOrEmpty(SimulationLogger.filePath)) return;

        SimulationRecord record = new SimulationRecord(
            id: "LIFECYCLE-" + agentId,
            type: SensorType.AgentTelemetry,
            loc: currentZone,
            time: Time.time,
            tick: -1
        );

        record.agentId = agentId;
        record.speed = 0f;
        record.hasExited = hasExited;
        record.exitTime = exitTime;
        record.ageBand = ageBand;
        record.disability = spawnDisability;
        record.mobilityStatus = mobilityStatus;
        record.health = health;
        record.maxHealth = maxHealth;
        record.hazardBand = hazardBand;
        record.distanceToFire = distanceToFire;
        record.fireDamageTotal = fireDamageTotal;
        record.visibilityDamageTotal = visibilityDamageTotal;
        record.trapReason = trapReason;
        record.eventDetails = details;

        SimulationLogger.WriteRecord(record);
    }
}