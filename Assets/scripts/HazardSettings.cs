// HazardSettings.cs
// One place in the scene that owns the hazard rings and the drain rates.
// Every agent reads from this at runtime, so you can drag the sliders while the
// simulation is playing and watch the whole crowd respond live.
//
// RETUNED IN THIS VERSION
//   - Drain rates cut hard, so agents degrade slowly and get trapped gradually
//     instead of collapsing within a few seconds.
//   - Low visibility ring pulled in close to the fire, since smoke should not
//     reach halfway across the building.
//   - Speed penalties softened, so agents near fire slow down but do not crawl.
//
// There is no smoke object in the scene yet. Low visibility is modelled as a
// ring around anything tagged Fire. When real smoke arrives, only the
// visibility branch inside AgentNoise needs to change.
using UnityEngine;

public class HazardSettings : MonoBehaviour
{
    public static HazardSettings Instance;

    [Header("Hazard rings, measured from the nearest object tagged Fire")]
    [Tooltip("Inside the fire. Strongest band.")]
    public float inFireRadius = 1.2f;

    [Tooltip("Right next to the fire.")]
    public float nearFireRadius = 2.5f;

    [Tooltip("Low visibility. Kept tight, smoke should hug the fire.")]
    public float lowVisibilityRadius = 4f;

    [Header("Health drain, fraction of max health lost per second")]
    [Tooltip("At 0.15 an agent standing in the fire survives about 7 seconds.")]
    [Range(0f, 1f)] public float inFireDrainPerSec = 0.15f;

    [Tooltip("At 0.07 an agent beside the fire survives about 14 seconds.")]
    [Range(0f, 1f)] public float nearFireDrainPerSec = 0.07f;

    [Tooltip("At 0.02 low visibility alone takes about 50 seconds to trap someone.")]
    [Range(0f, 1f)] public float lowVisibilityDrainPerSec = 0.02f;

    [Header("Speed penalty while inside each band, multiplies agent speed")]
    [Range(0.1f, 1f)] public float inFireSpeedFactor = 0.80f;
    [Range(0.1f, 1f)] public float nearFireSpeedFactor = 0.90f;
    [Range(0.1f, 1f)] public float lowVisibilitySpeedFactor = 0.95f;

    [Header("Performance")]
    [Tooltip("How often the shared fire list is refreshed, in seconds.")]
    public float fireCacheInterval = 0.25f;

    private GameObject[] cachedFires = new GameObject[0];
    private float cacheTimer = 0f;

    void Awake()
    {
        Instance = this;
        RefreshFireCache();
    }

    void Update()
    {
        // Sanity guard so the rings can never cross over each other in the Inspector.
        nearFireRadius = Mathf.Max(nearFireRadius, inFireRadius);
        lowVisibilityRadius = Mathf.Max(lowVisibilityRadius, nearFireRadius);

        cacheTimer += Time.deltaTime;
        if (cacheTimer >= fireCacheInterval)
        {
            cacheTimer = 0f;
            RefreshFireCache();
        }
    }

    void RefreshFireCache()
    {
        // FireSpread spawns and destroys fire chunks at runtime, so this list must
        // be refreshed rather than cached once at startup.
        cachedFires = GameObject.FindGameObjectsWithTag("Fire");
    }

    /// <summary>
    /// Distance from a world position to the nearest active fire.
    /// Returns infinity when no fire exists in the scene.
    /// </summary>
    public float DistanceToNearestFire(Vector3 position)
    {
        float nearest = Mathf.Infinity;

        foreach (GameObject fire in cachedFires)
        {
            if (fire == null) continue;
            float d = Vector3.Distance(position, fire.transform.position);
            if (d < nearest) nearest = d;
        }

        return nearest;
    }

    public GameObject[] GetFires()
    {
        return cachedFires;
    }
}