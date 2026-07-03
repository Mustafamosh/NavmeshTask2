using UnityEngine;
using UnityEngine.AI;

public class AgentExitNavigator : MonoBehaviour
{
    private NavMeshAgent agent;
    private Transform selectedExit;

    [Header("Evacuation Trigger")]
    public bool waitForTrigger = true;
    public bool isEvacuating = false;

    [Header("Fire Reaction")]
    public float directFireReactionRadius = 6f;

    [Header("Navigation")]
    public float repathInterval = 0.5f;
    public float fireDangerRadius = 5f;
    public float firePenalty = 1000f;

    private float timer;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.autoRepath = true;

        if (waitForTrigger)
        {
            agent.isStopped = true;
        }
        else
        {
            StartEvacuation("manual start");
        }
    }

    void Update()
    {
        if (!isEvacuating)
        {
            CheckFireOrAlarmTrigger();
            return;
        }

        timer += Time.deltaTime;

        if (timer >= repathInterval)
        {
            timer = 0f;
            ChooseBestExit();
        }
    }

    void CheckFireOrAlarmTrigger()
    {
        if (IsCloseToFire())
        {
            StartEvacuation("close to fire");
            return;
        }

        if (FireAlarmSystem.Instance != null && FireAlarmSystem.Instance.alarmActive)
        {
            StartEvacuation("heard global alarm");
            return;
        }
    }

    bool IsCloseToFire()
    {
        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");

        foreach (GameObject fire in fires)
        {
            float distance = Vector3.Distance(transform.position, fire.transform.position);

            if (distance <= directFireReactionRadius)
                return true;
        }

        return false;
    }

    public void StartEvacuation(string reason)
    {
        if (isEvacuating) return;

        isEvacuating = true;
        agent.isStopped = false;
        ChooseBestExit();

        Debug.Log(gameObject.name + " started evacuation because: " + reason);
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

            float totalScore = pathLength + dangerScore;

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
                    score += firePenalty;
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