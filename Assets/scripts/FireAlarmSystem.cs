using UnityEngine;
using System.Collections;

public class FireAlarmSystem : MonoBehaviour
{
    public static FireAlarmSystem Instance;

    [Header("Alarm State")]
    public bool alarmActive = false;

    [Header("Alarm Timing")]
    public float ringDelay = 0.5f;      // Time after the first detection before all detectors ring

    [Header("First Detector Log")]
    public bool firstDetectorRecorded = false;
    public string firstDetectorName;
    public string firstDetectorZone;
    public float firstSmokeReading;
    public float detectionTime;

    private bool alarmStarting = false;

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
        // Only start the countdown once
        if (alarmActive || alarmStarting)
            return;

        alarmStarting = true;
        StartCoroutine(RingAllAfterDelay(ringDelay));
    }

    private IEnumerator RingAllAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        alarmActive = true;
        Debug.Log("ALL DETECTORS RINGING");

        // Every detector starts sounding at the same moment.
        // Agents decide on their own if a sounding detector is close enough to hear.
        SmokeDetectorNode[] detectors = FindObjectsByType<SmokeDetectorNode>(FindObjectsSortMode.None);
        foreach (SmokeDetectorNode detector in detectors)
        {
            detector.isSounding = true;
            detector.PlayAlarmSound();
        }
    }
}