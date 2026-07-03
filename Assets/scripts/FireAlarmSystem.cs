using UnityEngine;

public class FireAlarmSystem : MonoBehaviour
{
    public static FireAlarmSystem Instance;

    [Header("Alarm State")]
    public bool alarmActive = false;

    [Header("First Detector Log")]
    public bool firstDetectorRecorded = false;
    public string firstDetectorName;
    public string firstDetectorZone;
    public float firstSmokeReading;
    public float detectionTime;

    void Awake()
    {
        Instance = this;
    }

    public void RecordFirstDetector(SmokeDetectorNode detector)
    {
        if (firstDetectorRecorded)
            return;

        firstDetectorRecorded = true;

        firstDetectorName = detector.gameObject.name;
        firstDetectorZone = detector.nodeZone;
        firstSmokeReading = detector.smokeReading;
        detectionTime = Time.time;

        Debug.Log("========== FIRE EVENT ==========");
        Debug.Log("First Sensor Triggered: " + firstDetectorName);
        Debug.Log("Zone: " + firstDetectorZone);
        Debug.Log("Smoke Reading: " + firstSmokeReading.ToString("F2"));
        Debug.Log("Time: " + detectionTime.ToString("F2") + " seconds");
        Debug.Log("===============================");
    }

    public void TriggerAlarm()
    {
        if (alarmActive)
            return;

        alarmActive = true;

        Debug.Log("GLOBAL FIRE ALARM ACTIVATED");

        PlayAllDetectorAlarms();
        EvacuateAllAgents();
    }

    void PlayAllDetectorAlarms()
    {
        SmokeDetectorNode[] detectors = FindObjectsByType<SmokeDetectorNode>(FindObjectsSortMode.None);

        foreach (SmokeDetectorNode detector in detectors)
        {
            detector.PlayAlarmSound();
        }
    }

    void EvacuateAllAgents()
    {
        AgentExitNavigator[] agents = FindObjectsByType<AgentExitNavigator>(FindObjectsSortMode.None);

        foreach (AgentExitNavigator agent in agents)
        {
            agent.StartEvacuation("global alarm");
        }
    }
}