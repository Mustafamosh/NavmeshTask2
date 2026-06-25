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
    }
}