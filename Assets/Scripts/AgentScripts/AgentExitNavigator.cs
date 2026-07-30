// AgentExitNavigator.cs
//
// Changes in this version:
//   - Added calm pre alarm wandering. Before the alarm, agents roam to random
//     walkable points instead of standing frozen. The moment fire or the alarm
//     triggers them, control hands fully to the evacuation logic and the wander
//     never runs again.
//   - alarmHearingRadius stays at 18 so agents hear the building alarm.
//   - This script does NOT set agent.speed. AgentNoise owns speed entirely, calm
//     while wandering and full while evacuating, scaled by age, disability,
//     health, and hazard band.
using UnityEngine;
using UnityEngine.AI;

public class AgentExitNavigator : MonoBehaviour
{
    private NavMeshAgent agent;
    private Transform selectedExit;

    [Header("Evacuation Trigger")]
    public bool waitForTrigger = true;
    public bool isEvacuating = false;

    [Header("Pre alarm wander")]
    public bool wanderBeforeAlarm = true;
    [Tooltip("How far from its current position an agent looks for its next idle destination.")]
    public float wanderRadius = 6f;
    [Tooltip("Small pause range at each wander destination, so agents do not pace nonstop.")]
    public float minWanderPause = 0.5f;
    public float maxWanderPause = 3f;
    public float wanderArriveBuffer = 0.5f;

    [Header("Fire Reaction")]
    public float directFireReactionRadius = 6f;
    public float alarmHearingRadius = 18f;

    [Header("Navigation")]
    public float repathInterval = 0.5f;
    public float fireDangerRadius = 5f;
    public float firePenalty = 1000f;
    public float fleeDistance = 5f;

    private float timer;
    private float wanderPauseTimer = 0f;
    private bool fleeLogged = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.autoRepath = true;

        if (waitForTrigger)
        {
            if (wanderBeforeAlarm)
            {
                agent.isStopped = false;
                PickWanderDestination();
            }
            else
            {
                agent.isStopped = true;
            }
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
            if (wanderBeforeAlarm) WanderStep();
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

    // ---------------- Pre alarm wandering ----------------

    void WanderStep()
    {
        // Short idle pause after arriving somewhere, so the crowd looks natural
        // rather than every agent pacing without stopping.
        if (wanderPauseTimer > 0f)
        {
            wanderPauseTimer -= Time.deltaTime;
            if (wanderPauseTimer <= 0f)
                PickWanderDestination();
            return;
        }

        if (agent.pathPending) return;

        if (agent.remainingDistance <= agent.stoppingDistance + wanderArriveBuffer)
            wanderPauseTimer = Random.Range(minWanderPause, maxWanderPause);
    }

    void PickWanderDestination()
    {
        Vector3 random = transform.position + Random.insideUnitSphere * wanderRadius;
        random.y = transform.position.y;

        if (NavMesh.SamplePosition(random, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    // ---------------- Trigger checks ----------------

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
        // Uses the shared fire cache when HazardSettings is present, so the whole
        // crowd is not calling FindGameObjectsWithTag every single frame.
        if (HazardSettings.Instance != null)
            return HazardSettings.Instance.DistanceToNearestFire(transform.position) <= directFireReactionRadius;

        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");
        foreach (GameObject fire in fires)
        {
            if (fire == null) continue;
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
        wanderPauseTimer = 0f;
        agent.isStopped = false;
        agent.ResetPath();
        ChooseBestExit();

        // Logged per agent so the AI knows exactly when and why each person moved.
        AgentDataTracker tracker = GetComponent<AgentDataTracker>();
        if (tracker != null)
        {
            SimulationLogger.LogEvent(
                "EVENT-Evac-" + tracker.agentId,
                tracker.currentZone,
                tracker.agentId + " started evacuating because " + reason +
                " | Age: " + tracker.ageBand +
                " | Disability: " + tracker.spawnDisability +
                " | Mobility: " + tracker.mobilityStatus +
                " | Health: " + tracker.health.ToString("F1"),
                SimulationLogger.GetSimulationTime(),
                0
            );
        }

        Debug.Log(gameObject.name + " started evacuation because: " + reason);
    }

    // ---------------- Evacuation ----------------

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
        GameObject[] fires = HazardSettings.Instance != null
            ? HazardSettings.Instance.GetFires()
            : GameObject.FindGameObjectsWithTag("Fire");

        if (fires.Length == 0) return;

        Vector3 myPos = transform.position;
        Vector3 nearestFire = myPos;
        float nearest = Mathf.Infinity;

        foreach (GameObject fire in fires)
        {
            if (fire == null) continue;
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
            string extra = tracker != null
                ? (" | Age: " + tracker.ageBand + " | Mobility: " + tracker.mobilityStatus + " | Health: " + tracker.health.ToString("F1"))
                : "";

            SimulationLogger.LogEvent(
                "EVENT-Flee-" + id,
                zone,
                id + " had no exit path, moving away from fire" + extra,
                SimulationLogger.GetSimulationTime(),
                0
            );
        }
    }

    float GetFireDangerScore(NavMeshPath path)
    {
        GameObject[] fires = HazardSettings.Instance != null
            ? HazardSettings.Instance.GetFires()
            : GameObject.FindGameObjectsWithTag("Fire");

        float score = 0f;

        foreach (Vector3 corner in path.corners)
            foreach (GameObject fire in fires)
            {
                if (fire == null) continue;
                if (Vector3.Distance(corner, fire.transform.position) < fireDangerRadius)
                    score += firePenalty;
            }

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