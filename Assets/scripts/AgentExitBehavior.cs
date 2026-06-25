using UnityEngine;
using UnityEngine.AI;

public class AgentExitBehavior : MonoBehaviour
{
    private NavMeshAgent agent;
    private GameObject[] exits;
    public float exitRadius = 1.5f;
    public static int agentsExited = 0;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        exits = GameObject.FindGameObjectsWithTag("Exit");
    }

    void Update()
    {
        foreach (GameObject exit in exits)
        {
            if (Vector3.Distance(transform.position, exit.transform.position) < exitRadius)
            {
                AgentDataTracker tracker = GetComponent<AgentDataTracker>();
                if (tracker != null)
                {
                    tracker.RecordExit();
                }

                agentsExited++;
                Destroy(gameObject);
                return;
            }
        }
    }
}