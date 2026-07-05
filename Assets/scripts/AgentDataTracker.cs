// AgentDataTracker.cs —  WriteExitRecord() restored
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections.Generic;
using System.IO;

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

    // --- Fire Kill Radius ---
    public float fireKillRadius = 1.5f;

    // --- Private ---
    private NavMeshAgent navAgent;

    // --- ID Counter ---
    private static int nextId = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        nextId = 0;
        agentsTrapped = 0;
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
    }

    void Update()
    {
        if (navAgent != null)
            speed = navAgent.velocity.magnitude;

        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");
        foreach (GameObject fire in fires)
        {
            if (Vector3.Distance(transform.position, fire.transform.position) < fireKillRadius)
            {
                agentsTrapped++;
                pathHistory.Add("Trapped at " + currentZone + " | T=" + Time.time.ToString("F2"));
                WriteExitRecord();
                Destroy(gameObject);
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
        {
            currentZone = "Transition";
        }
    }

    public void RecordExit()
    {
        hasExited = true;
        exitTime = Time.time;
        pathHistory.Add("Exited | T=" + exitTime.ToString("F2"));
        WriteExitRecord();
    }

    private void WriteExitRecord()
    {
        if (string.IsNullOrEmpty(SimulationLogger.filePath)) return;

        SimulationRecord record = new SimulationRecord(
            id: "Logger",
            type: SensorType.AgentTelemetry,
            loc: currentZone,
            time: Time.time,
            tick: -1
        );
        record.agentId = agentId;
        record.speed = 0f;
        record.hasExited = hasExited;
        record.exitTime = exitTime;
        // Store the outcome plus the full path the agent took, so the coach can explain the route
        record.eventDetails = (hasExited ? "Exited" : "Trapped")
            + " | Path: " + string.Join(" > ", pathHistory);
        SimulationLogger.WriteRecord(record);
    }
}