// AgentExitNavigator.cs
// Change from previous version: alarmHearingRadius updated from 2 to 18
// so agents realistically hear the building alarm.
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
    public float alarmHearingRadius = 18f;  // Updated: was 2, now 18 so agents hear the alarm across the building

    [Header("Navigation")]
    public float repathInterval = 0.5f;
    public float fireDangerRadius = 5f;
    public float firePenalty = 1000f;
    public float fleeDistance = 5f;         // How far to move when no exit is reachable

    private float timer;
    private bool fleeLogged = false;        // The cutoff is logged once, not every interval

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.autoRepath = true;

        if (waitForTrigger)
            agent.isStopped = true;
        else
            StartEvacuation("manual start");
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

        if (HearsNearbyAlarm())
        {
            StartEvacuation("heard nearby alarm");
            return;
        }
    }

    bool IsCloseToFire()
    {
        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");
        foreach (GameObject fire in fires)
        {
            if (Vector3.Distance(transform.position, fire.transform.position) <= directFireReactionRadius)
                return true;
        }
        return false;
    }

    bool HearsNearbyAlarm()
    {
        SmokeDetectorNode[] detectors = FindObjectsByType<SmokeDetectorNode>(FindObjectsSortMode.None);
        foreach (SmokeDetectorNode detector in detectors)
        {
            if (!detector.isSounding) continue;
            if (Vector3.Distance(transform.position, detector.transform.position) <= alarmHearingRadius)
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
        else
        {
            FleeFromFire();
        }
    }

    void FleeFromFire()
    {
        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");
        if (fires.Length == 0) return;

        Vector3 myPos = transform.position;
        Vector3 nearestFire = myPos;
        float nearest = Mathf.Infinity;

        foreach (GameObject fire in fires)
        {
            float d = Vector3.Distance(myPos, fire.transform.position);
            if (d < nearest)
            {
                nearest = d;
                nearestFire = fire.transform.position;
            }
        }

        Vector3 away = (myPos - nearestFire).normalized;
        Vector3 target = myPos + away * fleeDistance;

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, fleeDistance, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.ResetPath();
            agent.SetDestination(hit.position);
        }

        if (!fleeLogged)
        {
            fleeLogged = true;
            AgentDataTracker tracker = GetComponent<AgentDataTracker>();
            string id = tracker != null ? tracker.agentId : gameObject.name;
            string zone = tracker != null ? tracker.currentZone : "Unknown";
            SimulationLogger.LogEvent("EVENT-Flee-" + id, zone, id + " had no exit path, moving away from fire", Time.time, 0);
        }
    }

    float GetFireDangerScore(NavMeshPath path)
    {
        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");
        float score = 0f;

        foreach (Vector3 corner in path.corners)
            foreach (GameObject fire in fires)
                if (Vector3.Distance(corner, fire.transform.position) < fireDangerRadius)
                    score += firePenalty;

        return score;
    }

    float GetPathLength(NavMeshPath path)
    {
        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return length;
    }
}