using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class AgentNoise : MonoBehaviour
{
    private static int nextAgentIdCounter = 0;

    [Tooltip("Minimum speed for the NavMeshAgent")]
    public float minSpeed = 1.5f;

    [Tooltip("Maximum speed for the NavMeshAgent")]
    public float maxSpeed = 2.5f;

    [Tooltip("Unique identifier for this agent")]
    public string agentId;

    [Tooltip("If true, randomize speed in Awake; otherwise randomize in Start")]
    public bool randomizeOnAwake = false;

    NavMeshAgent agent;

    void Awake()
    {
        agentId = nextAgentIdCounter++.ToString();
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) return;

        if (randomizeOnAwake)
            RandomizeSpeed();
    }

    void Start()
    {
        if (!randomizeOnAwake)
            RandomizeSpeed();
    }

    void RandomizeSpeed()
    {
        if (minSpeed > maxSpeed)
        {
            float t = minSpeed;
            minSpeed = maxSpeed;
            maxSpeed = t;
        }

        float s = Random.Range(minSpeed, maxSpeed);
        agent.speed = s;
    }
}
