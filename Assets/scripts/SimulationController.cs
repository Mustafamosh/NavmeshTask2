// SimulationController.cs
// NEW FILE. One in the scene. The brain of the publishable flow.
//
// States
//   Setup    The user sets agent count and type. Agents spawn live and wander.
//            Fire clicking and exit blocking are OFF. Logging is OFF.
//   Running  Agent controls locked. Fire clicking and exit blocking ON. Logging ON.
//   Paused   Everything frozen with timeScale 0. Press again to resume.
//
// Stop clears everything back to Setup and leaves the finished JSON log on disk.
//
// CHANGE IN THIS VERSION
//   The scene no longer has a Start button, so the run begins automatically the
//   moment the scene loads. Fire clicking, exit blocking, and logging are all live
//   immediately. Stop and Pause still work exactly as before.
using UnityEngine;

public class SimulationController : MonoBehaviour
{
    public enum State { Setup, Running, Paused }
    public State state { get; private set; } = State.Setup;

    [Header("References")]
    public AgentSpawner spawner;
    [Tooltip("Drag your ClickFireSpawn component here. It is enabled only while Running.")]
    public MonoBehaviour fireClickComponent;
    public SimulationLogger logger;
    public ExitBlocker exitBlocker;

    void Start()
    {
        EnterSetup();
        StartSimulation();
    }

    public void EnterSetup()
    {
        state = State.Setup;
        Time.timeScale = 1f;

        if (fireClickComponent != null) fireClickComponent.enabled = false;
        if (exitBlocker != null) exitBlocker.SetBlockingAllowed(false);
        if (logger != null) logger.StopLogging();
    }

    public void StartSimulation()
    {
        if (state == State.Running) return;

        state = State.Running;
        Time.timeScale = 1f;

        // Count zone occupancy accurately from the agents that ended setup inside
        // each room, then begin the fresh log.
        ZoneOccupancy.ResyncFromScene();
        if (logger != null) logger.BeginRun();

        if (fireClickComponent != null) fireClickComponent.enabled = true;
        if (exitBlocker != null) exitBlocker.SetBlockingAllowed(true);
    }

    public void PauseSimulation()
    {
        if (state == State.Running) { state = State.Paused; Time.timeScale = 0f; }
        else if (state == State.Paused) { state = State.Running; Time.timeScale = 1f; }
    }

    public void StopSimulation()
    {
        // Finalize the log first so the JSON on disk is complete.
        if (logger != null) logger.StopLogging();
        Time.timeScale = 1f;

        // Remove the clicked fire and any stray fire chunks.
        GameObject spawnedFire = GameObject.Find("FireSpread_Spawned");
        if (spawnedFire != null) Destroy(spawnedFire);
        foreach (GameObject f in GameObject.FindGameObjectsWithTag("Fire"))
            if (f != null) Destroy(f);

        // Remove barriers.
        if (exitBlocker != null) exitBlocker.UnblockAll();

        // Remove agents.
        if (spawner != null) spawner.ClearAll();

        // Reset the running totals so the next run starts clean.
        AgentDataTracker.ResetCounters();
        ZoneOccupancy.ResetRuntimeCounts();

        EnterSetup();
    }

    public bool AgentControlsLocked => state != State.Setup;
}