// BurnDamageTracker.cs
// NEW FILE. Put one of these in the scene.
//
// Every BurnableSurface knows how charred it is and which room it belongs to.
// This script rolls that up per room and writes it to the log, so the AI coach can
// say things like "the fire destroyed Hallway 2, which is why nobody used Exit 2"
// rather than only knowing where the agents were.
//
// It writes two kinds of record.
//   1. A rolling StructuralDamage record per room, on a slow tick, carrying the
//      average char and the count of destroyed surfaces.
//   2. A one time event the moment a room crosses a damage threshold, so there is a
//      clear timestamped moment saying when each room was lost.
using UnityEngine;
using System.Collections.Generic;

public class BurnDamageTracker : MonoBehaviour
{
    [Header("Reporting")]
    [Tooltip("How often room damage is written to the log. Slower than the agent tick, since surfaces char slowly.")]
    public float reportInterval = 2f;

    [Tooltip("Average char at which a room is reported as heavily damaged.")]
    [Range(0f, 1f)] public float roomDamagedThreshold = 0.4f;

    [Tooltip("Average char at which a room is reported as destroyed.")]
    [Range(0f, 1f)] public float roomDestroyedThreshold = 0.75f;

    private BurnableSurface[] surfaces;
    private float timer = 0f;

    // Remembers which milestone each room has already announced, so the one time
    // events fire once rather than every report tick.
    private Dictionary<string, string> roomStatus = new Dictionary<string, string>();

    void Start()
    {
        // Cached once. Walls and props are not created or destroyed at runtime.
        surfaces = FindObjectsByType<BurnableSurface>(FindObjectsSortMode.None);
        Debug.Log("BurnDamageTracker found " + surfaces.Length + " burnable surfaces.");
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < reportInterval) return;
        timer = 0f;

        ReportRoomDamage();
    }

    void ReportRoomDamage()
    {
        if (surfaces == null || surfaces.Length == 0) return;
        if (string.IsNullOrEmpty(SimulationLogger.filePath)) return;

        Dictionary<string, float> totalChar = new Dictionary<string, float>();
        Dictionary<string, int> surfaceCount = new Dictionary<string, int>();
        Dictionary<string, int> destroyedCount = new Dictionary<string, int>();

        foreach (BurnableSurface s in surfaces)
        {
            if (s == null) continue;

            string zone = string.IsNullOrEmpty(s.zoneName) ? "Unknown" : s.zoneName;

            if (!totalChar.ContainsKey(zone))
            {
                totalChar[zone] = 0f;
                surfaceCount[zone] = 0;
                destroyedCount[zone] = 0;
            }

            totalChar[zone] += s.charLevel;
            surfaceCount[zone]++;
            if (s.isFullyCharred) destroyedCount[zone]++;
        }

        foreach (var kv in totalChar)
        {
            string zone = kv.Key;
            int count = surfaceCount[zone];
            if (count == 0) continue;

            float avgChar = kv.Value / count;

            // Skip untouched rooms, otherwise the log fills with zeros.
            if (avgChar < 0.01f) continue;

            string label = DamageLabel(avgChar);

            SimulationRecord record = new SimulationRecord(
                id: "DMG-" + zone,
                type: SensorType.StructuralDamage,
                loc: zone,
                time: Time.time,
                tick: -1
            );

            record.value = avgChar;
            record.charLevel = avgChar;
            record.surfacesDestroyed = destroyedCount[zone];
            record.surfacesTotal = count;
            record.damageLabel = label;
            record.eventDetails =
                zone + " structural damage" +
                " | Average char: " + (avgChar * 100f).ToString("F0") + " percent" +
                " | Surfaces destroyed: " + destroyedCount[zone] + " of " + count +
                " | Status: " + label;

            SimulationLogger.WriteRecord(record);

            AnnounceMilestone(zone, label, avgChar);
        }
    }

    string DamageLabel(float avgChar)
    {
        if (avgChar >= roomDestroyedThreshold) return "Destroyed";
        if (avgChar >= roomDamagedThreshold) return "HeavilyDamaged";
        return "Scorched";
    }

    // Fires a single clear event the first time a room reaches each stage, so the
    // AI has a timestamp for when the room was lost rather than a wall of numbers.
    void AnnounceMilestone(string zone, string label, float avgChar)
    {
        string previous = roomStatus.ContainsKey(zone) ? roomStatus[zone] : "";

        if (previous == label) return;
        if (previous == "Destroyed") return;                       // already at the worst stage
        if (previous == "HeavilyDamaged" && label == "Scorched") return; // never go backwards

        roomStatus[zone] = label;

        SimulationLogger.LogEvent(
            "EVENT-Damage-" + zone,
            zone,
            zone + " is now " + label + " | Average char: " + (avgChar * 100f).ToString("F0") + " percent",
            Time.time,
            -1
        );
    }

    /// <summary>
    /// Public so a dashboard or an end of run summary can ask which rooms burned.
    /// </summary>
    public Dictionary<string, string> GetRoomDamageSummary()
    {
        return new Dictionary<string, string>(roomStatus);
    }
}