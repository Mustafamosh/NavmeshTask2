// SimulationController.cs
// One in the scene. The brain of the publishable flow.
//
// The scene has no Start button, so the run begins automatically when the scene
// loads. Fire clicking, exit blocking, and logging are live immediately.
//
// Stop hands the finished jsonl log to the user as a browser download, tears the
// run down, and returns to the start menu. The run does not continue afterwards,
// since the configuration controls only exist in the main menu.
using UnityEngine;
using UnityEngine.SceneManagement;

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

    [Header("Scene to return to on Stop")]
    [Tooltip("Must match the name in the Scene List exactly.")]
    public string startMenuSceneName = "1Start-Menu";

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
        // Time scale first, because the download and the scene load must not run
        // while the game is frozen from a Pause.
        Time.timeScale = 1f;

        // Hand the finished log to the user before anything is destroyed.
        if (logger != null)
        {
            string filename = logger.DownloadLog();
            Debug.Log("Simulation log handed to the user as " + filename);
        }

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

        state = State.Setup;

        ReturnToStartMenu();
    }

    void ReturnToStartMenu()
    {
        if (Application.CanStreamedLevelBeLoaded(startMenuSceneName))
        {
            SceneManager.LoadScene(startMenuSceneName);
        }
        else
        {
            Debug.LogWarning("SimulationController: scene " + startMenuSceneName +
                             " is not in the Scene List, so Stop could not return to it.");
        }
    }

    public bool AgentControlsLocked => state != State.Setup;
}