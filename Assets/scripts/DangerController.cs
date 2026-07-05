using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// The brain that reacts to danger. Sensors only report, this decides what to do.
/// Each tick it checks the fire near every agent. If an agent is inside the
/// danger radius of a fire, it speeds the agent up and logs a warning event once.
/// </summary>
public class DangerController : MonoBehaviour
{
    // === 1. Settings ===
    public float dangerRadius = 6f;       // How close fire has to be to trigger a warning
    public float boostedSpeed = 3.5f;     // Speed an agent moves at once warned
    public float checkInterval = 0.4f;    // How often the controller checks, matches the tick

    // === 2. Internal State ===
    private float timer = 0f;
    private HashSet<string> alreadyWarned = new HashSet<string>();  // Agents already sped up, so we warn once
    private int tickNumber = 0;

    void Update()
    {
        // Only check on the interval, not every frame
        timer += Time.deltaTime;
        if (timer < checkInterval) return;
        timer = 0f;
        tickNumber++;

        GameObject[] fires = GameObject.FindGameObjectsWithTag("Fire");
        if (fires.Length == 0) return;

        AgentDataTracker[] agents = FindObjectsByType<AgentDataTracker>();
        foreach (AgentDataTracker agent in agents)
        {
            if (agent.hasExited) continue;

            // Find the distance to the nearest fire
            float nearest = Mathf.Infinity;
            foreach (GameObject fire in fires)
            {
                float d = Vector3.Distance(agent.transform.position, fire.transform.position);
                if (d < nearest) nearest = d;
            }

            // If fire is within the danger radius, speed this agent up
            if (nearest <= dangerRadius)
            {
                NavMeshAgent nav = agent.GetComponent<NavMeshAgent>();
                if (nav != null && nav.speed < boostedSpeed)
                    nav.speed = boostedSpeed;

                // Log the warning only the first time it happens for this agent
                if (!alreadyWarned.Contains(agent.agentId))
                {
                    alreadyWarned.Add(agent.agentId);
                    SimulationLogger.LogEvent(
                        "EVENT-Warning-" + agent.agentId,
                        agent.currentZone,
                        agent.agentId + " warned, fire within " + dangerRadius + "m, speeding up",
                        Time.time,
                        tickNumber
                    );
                }
            }
        }
    }
}