using UnityEngine;

public class SmokeDetectorNode : MonoBehaviour
{
    [Header("Public logger values")]
    public float smokeReading = 0f;
    public bool smokeDetected = false;
    public bool isSounding = false;      // True once the building alarm is ringing
    public string nodeZone = "Main Hall";

    [Header("Detection")]
    public float detectionThreshold = 0.1f;
    public float scanRadius = 8f;
    public float scanStep = 1f;

    private FireSpread fireSpread;
    private AudioSource audioSource;

    void Start()
    {
        fireSpread = FindFirstObjectByType<FireSpread>();
        audioSource = GetComponent<AudioSource>();

        if (fireSpread == null)
            Debug.LogError($"{gameObject.name}: No FireSpread found.");
    }

    void Update()
    {
        if (fireSpread == null)
            return;

        smokeReading = ScanNearbySmoke();
        smokeDetected = smokeReading >= detectionThreshold;

        // The first detector to sense smoke tells the alarm system, which then rings everywhere
        if (smokeDetected && FireAlarmSystem.Instance != null)
        {
            FireAlarmSystem.Instance.RecordFirstDetector(this);
            FireAlarmSystem.Instance.TriggerAlarm();
        }
    }

    public void PlayAlarmSound()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource != null && !audioSource.isPlaying)
            audioSource.Play();
    }

    float ScanNearbySmoke()
    {
        float highestSmoke = 0f;

        for (float x = -scanRadius; x <= scanRadius; x += scanStep)
        {
            for (float z = -scanRadius; z <= scanRadius; z += scanStep)
            {
                Vector3 checkPos = transform.position + new Vector3(x, 0, z);
                checkPos.y = 0.5f;

                float smoke = fireSpread.GetSmokeLevel(checkPos);
                highestSmoke = Mathf.Max(highestSmoke, smoke);
            }
        }

        return highestSmoke;
    }
}