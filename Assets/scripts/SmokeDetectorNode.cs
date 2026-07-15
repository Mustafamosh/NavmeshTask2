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
    private bool hasLoggedMissingFireSpread;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        ResolveFireSpread();
    }

    void Update()
    {
        ResolveFireSpread();

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

    void ResolveFireSpread()
    {
        if (fireSpread != null)
            return;

        FireSpread[] fireSpreads = FindObjectsByType<FireSpread>(FindObjectsSortMode.None);

        foreach (FireSpread candidate in fireSpreads)
        {
            if (candidate.name == "FireSpread_Spawned")
            {
                fireSpread = candidate;
                break;
            }
        }

        if (fireSpread == null && !hasLoggedMissingFireSpread)
        {
            Debug.LogWarning($"{gameObject.name}: Waiting for FireSpread_Spawned to be spawned.");
            hasLoggedMissingFireSpread = true;
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