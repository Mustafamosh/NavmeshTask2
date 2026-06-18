using UnityEngine;
using UnityEngine.AI;

public class AgentExitNavigator : MonoBehaviour
{
    private NavMeshAgent agent;
    private Transform selectedExit;

    public float repathInterval = 0.5f;
    public float fireDangerRadius = 5f;
    public float firePenalty = 1000f;

    private float timer;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.autoRepath = true;
        ChooseBestExit();
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= repathInterval)
        {
            timer = 0f;
            ChooseBestExit();
        }
    }

    void ChooseBestExit()
    {
        GameObject[] exits = GameObject.FindGameObjectsWithTag("Exit");

        Transform bestExit = null;
        float bestScore = Mathf.Infinity;

        foreach (GameObject exitObj in exits)
        {
            NavMeshPath path = new NavMeshPath();

            bool hasPath = NavMesh.CalculatePath(
                transform.position,
                exitObj.transform.position,
                NavMesh.AllAreas,
                path
            );

            if (!hasPath || path.status != NavMeshPathStatus.PathComplete)
                continue;

            float pathLength = GetPathLength(path);
            float dangerScore = GetFireDangerScore(path);

            float densityScore = GetDensityScore(path);
            float totalScore = pathLength + dangerScore + densityScore;

            if (totalScore < bestScore)
            {
                bestScore = totalScore;
                bestExit = exitObj.transform;
            }
        }

        if (bestExit != null)
        {
            selectedExit = bestExit;
            agent.isStopped = false;
            agent.ResetPath();
            agent.SetDestination(selectedExit.position);

            //Debug.Log(gameObject.name + " moving to safest exit: " + selectedExit.name);
        }
    }

    float GetFireDangerScore(NavMeshPath path)
    {
        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");
        float score = 0f;

        foreach (Vector3 corner in path.corners)
        {
            foreach (GameObject fire in fires)
            {
                float distance = Vector3.Distance(corner, fire.transform.position);

                if (distance < fireDangerRadius)
                {
                    score += firePenalty;
                }
            }
        }

        return score;
    }

    float GetDensityScore(NavMeshPath path)
    {
        float score = 0f;

        foreach (Vector3 corner in path.corners)
        {
            Collider[] nearby = Physics.OverlapSphere(corner, 2f);
            foreach (Collider col in nearby)
            {
                if (col.GetComponent<AgentExitNavigator>() != null && col.gameObject != gameObject)
                    score += 50f;
            }
        }

        return score;
    }

    float GetPathLength(NavMeshPath path)
    {
        float length = 0f;

        for (int i = 1; i < path.corners.Length; i++)
        {
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return length;
    }
}