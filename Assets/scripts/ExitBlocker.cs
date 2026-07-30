// ExitBlocker.cs
// NEW FILE. One in the scene.
//
// Holds one entry per exit, each pointing at a barrier object that starts hidden.
// A Block Exit button calls ToggleExit with that exit index. Blocking is only
// allowed while the simulation is Running. Every block and unblock is written to
// the log with the exit name and the time, so the AI coach can say the user sealed
// an exit and see how the crowd reacted.
using UnityEngine;
using System.Collections.Generic;

public class ExitBlocker : MonoBehaviour
{
    [System.Serializable]
    public class ExitEntry
    {
        [Tooltip("Readable name, for example Exit 1. Used in the log.")]
        public string exitName;

        [Tooltip("The barrier that appears in front of the exit. Starts hidden.")]
        public GameObject barrier;
    }

    public List<ExitEntry> exits = new List<ExitEntry>();

    private bool blockingAllowed = false;

    void Start()
    {
        // Every barrier begins hidden.
        foreach (ExitEntry e in exits)
            if (e.barrier != null) e.barrier.SetActive(false);
    }

    public void SetBlockingAllowed(bool allowed)
    {
        blockingAllowed = allowed;
    }

    // Wire each Block Exit button to this, passing the index of the exit.
    public void ToggleExit(int index)
    {
        if (!blockingAllowed) return;
        if (index < 0 || index >= exits.Count) return;

        ExitEntry e = exits[index];
        if (e.barrier == null) return;

        bool nowBlocked = !e.barrier.activeSelf;
        e.barrier.SetActive(nowBlocked);
        Log(e.exitName, nowBlocked);
    }

    void Log(string exitName, bool blocked)
    {
        string details = blocked
            ? (exitName + " blocked by an obstacle the user placed at T=" + SimulationLogger.GetSimulationTime().ToString("F1"))
            : (exitName + " obstacle removed by the user at T=" + SimulationLogger.GetSimulationTime().ToString("F1"));

        SimulationLogger.LogEvent("EVENT-UserBlock-" + exitName, exitName, details, SimulationLogger.GetSimulationTime(), -1);
    }

    public void UnblockAll()
    {
        foreach (ExitEntry e in exits)
            if (e.barrier != null) e.barrier.SetActive(false);
    }
}