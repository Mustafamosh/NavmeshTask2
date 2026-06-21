using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class AgentNoise : MonoBehaviour
{
    [Tooltip("Minimum speed for the NavMeshAgent")]
    public float minSpeed = 1.5f;

    [Tooltip("Maximum speed for the NavMeshAgent")]
    public float maxSpeed = 2.5f;

    [Tooltip("If true, randomize speed in Awake; otherwise randomize in Start")]
    public bool randomizeOnAwake = false;

    NavMeshAgent agent;

    void Awake()
    {
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
