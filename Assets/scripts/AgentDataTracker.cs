// AgentDataTracker.cs
// Consolidated: absorbs AgentExitBehavior and AgentAnimator.
// AgentExitBehavior.cs and AgentAnimator.cs can be deleted.
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections.Generic;

public class AgentDataTracker : MonoBehaviour
{
    [Header("ID Display")]
    public TextMeshPro idLabel;

    // --- Schema Fields (public for logger to read) ---
    public string agentId;
    public float speed;
    public string currentZone = "Unknown";
    public float timeEnteringZone;
    public bool hasExited = false;
    public float exitTime = 0f;

    // --- Path History ---
    public List<string> pathHistory = new List<string>();

    // --- Trapped Counter ---
    public static int agentsTrapped = 0;

    // --- Fire Kill Radius (from AgentDataTracker) ---
    public float fireKillRadius = 1.5f;

    // --- Exit Detection Radius (absorbed from AgentExitBehavior) ---
    public float exitRadius = 1.5f;

    // --- Exit Counter (was in AgentExitBehavior) ---
    public static int agentsExited = 0;

    // --- Private ---
    private NavMeshAgent navAgent;
    private Animator agentAnimator;     // Absorbed from AgentAnimator
    private GameObject[] exits;
    private bool lifecycleEnded = false; // Guard so exit and fire-kill never both fire

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

        // Absorbed from AgentAnimator: find the Animator on a child object
        agentAnimator = GetComponentInChildren<Animator>();

        // Cache exits once at start (absorbed from AgentExitBehavior)
        exits = GameObject.FindGameObjectsWithTag("Exit");
    }

    void Update()
    {
        if (lifecycleEnded) return;

        // --- Speed tracking and animation (absorbed from AgentAnimator) ---
        if (navAgent != null)
        {
            speed = navAgent.velocity.magnitude;

            if (agentAnimator != null)
                agentAnimator.SetFloat("Speed", speed);
        }

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

        // --- Fire kill check ---
        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");
        foreach (GameObject fire in fires)
        {
            if (fire == null) continue;
            if (Vector3.Distance(transform.position, fire.transform.position) < fireKillRadius)
            {
                RecordTrapped();
                return;
            }
        }
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
    // exitName tells us exactly which exit was used, so the JSONL log is specific.
    public void RecordExit(string exitName)
    {
        if (lifecycleEnded) return;
        lifecycleEnded = true;

        hasExited = true;
        exitTime = Time.time;
        agentsExited++;

        pathHistory.Add("Exited via " + exitName + " | T=" + exitTime.ToString("F2"));

        WriteLifecycleRecord("Exited via " + exitName + " | Path: " + string.Join(" > ", pathHistory));

        ZoneOccupancy.ForceRemoveAgent(currentZone, agentId);   
        Destroy(gameObject);
    }

    // Called when the agent is killed by fire proximity.
    public void RecordTrapped()
    {
        if (lifecycleEnded) return;
        lifecycleEnded = true;

        agentsTrapped++;
        pathHistory.Add("Trapped at " + currentZone + " | T=" + Time.time.ToString("F2"));

        WriteLifecycleRecord("Trapped at " + currentZone + " | Path: " + string.Join(" > ", pathHistory));

        ZoneOccupancy.ForceRemoveAgent(currentZone, agentId);   
        Destroy(gameObject);
    }

    // Single write point for both exit and trapped outcomes.
    // Replaces the old WriteExitRecord which was also called from AgentExitBehavior,
    // eliminating the double-log risk.
    private void WriteLifecycleRecord()
    {
        WriteLifecycleRecord(hasExited
            ? ("Exited | Path: " + string.Join(" > ", pathHistory))
            : ("Trapped | Path: " + string.Join(" > ", pathHistory)));
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
        record.eventDetails = details;
        SimulationLogger.WriteRecord(record);
    }
}