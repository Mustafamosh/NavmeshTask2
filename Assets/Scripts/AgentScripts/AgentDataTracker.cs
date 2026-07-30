// AgentDataTracker.cs
// Consolidated: absorbs AgentExitBehavior and AgentAnimator.
//
// CHANGES IN THIS VERSION
//   - Exit detection only runs once the agent is actually evacuating. During setup
//     the agents wander freely, and without this guard a wanderer could stroll into
//     an exit and be deleted before the run even starts.
//   - The spawn PROFILE record waits until logging has actually begun, so an agent
//     that spawned during setup still records its profile the moment the run starts.
//   - ResetCounters lets the Stop button clear the running totals so the next run
//     starts from zero without reloading the scene.
//   - WriteLifecycleRecord now takes the location to log as a parameter instead of
//     always using currentZone. currentZone is usually already "Transition" by the
//     moment an agent reaches an exit (it flips the instant they leave the last
//     zone trigger), so exits were being logged with the wrong location even though
//     the real exit name was already sitting in the details text. Exits now pass
//     the actual exit name; traps still pass currentZone, which is correct there.
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections.Generic;

public class AgentDataTracker : MonoBehaviour
{
    [Header("ID Display")]
    public TextMeshPro idLabel;

    public string agentId;
    public float speed;
    public string currentZone = "Unknown";
    public float timeEnteringZone;
    public bool hasExited = false;
    public float exitTime = 0f;

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

    public List<string> pathHistory = new List<string>();

    public static int agentsTrapped = 0;
    public static int agentsExited = 0;

    public float exitRadius = 1.5f;

    private NavMeshAgent navAgent;
    private Animator agentAnimator;
    private AgentNoise profile;
    private AgentExitNavigator navigator;
    private GameObject[] exits;
    private bool lifecycleEnded = false;
    private bool profileWritten = false;

    private static int nextId = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        nextId = 0;
        agentsTrapped = 0;
        agentsExited = 0;
    }

    // Called by the Stop button through the controller.
    public static void ResetCounters()
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
        navigator = GetComponent<AgentExitNavigator>();

        exits = GameObject.FindGameObjectsWithTag("Exit");

        MirrorProfile();
        TryWriteProfileRecord();
    }

    void Update()
    {
        if (lifecycleEnded) return;

        if (!profileWritten) TryWriteProfileRecord();

        if (navAgent != null)
        {
            speed = navAgent.velocity.magnitude;
            if (agentAnimator != null)
                agentAnimator.SetFloat("Speed", speed);
        }

        MirrorProfile();

        // Only count reaching an exit once the agent is actually evacuating. A
        // wandering agent during setup must not be treated as having escaped.
        if (navigator != null && navigator.isEvacuating)
        {
            foreach (GameObject exit in exits)
            {
                if (exit == null) continue;
                if (Vector3.Distance(transform.position, exit.transform.position) < exitRadius)
                {
                    RecordExit(exit.name);
                    return;
                }
            }
        }
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

        WriteLifecycleRecord(details, exitName);

        ZoneOccupancy.ForceRemoveAgent(currentZone, agentId);
        Destroy(gameObject);
    }

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
            " | Survived: " + SimulationLogger.GetSimulationTime().ToString("F1") + " seconds" +
            " | Path: " + string.Join(" > ", pathHistory);

        WriteLifecycleRecord(details, currentZone);

        ZoneOccupancy.ForceRemoveAgent(currentZone, agentId);
        Destroy(gameObject);
    }

    public void RecordTrapped(string reason)
    {
        RecordTrapped(reason, fireDamageTotal, visibilityDamageTotal);
    }

    private void TryWriteProfileRecord()
    {
        if (profileWritten) return;
        if (!SimulationLogger.IsLogging) return;
        if (string.IsNullOrEmpty(SimulationLogger.filePath)) return;

        profileWritten = true;
        MirrorProfile();

        SimulationRecord record = new SimulationRecord(
            id: "PROFILE-" + agentId,
            type: SensorType.AgentProfile,
            loc: currentZone,
            time: SimulationLogger.GetSimulationTime(),
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

    private void WriteLifecycleRecord(string details, string location)
    {
        if (string.IsNullOrEmpty(SimulationLogger.filePath)) return;

        SimulationRecord record = new SimulationRecord(
            id: "LIFECYCLE-" + agentId,
            type: SensorType.AgentTelemetry,
            loc: location,
            time: SimulationLogger.GetSimulationTime(),
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