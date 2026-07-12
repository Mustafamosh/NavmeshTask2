// HazardSettings.cs
// NEW FILE-- PhaseIII
// One place in the scene that owns the hazard rings and the drain rates.
// Every agent reads from this at runtime, so you can drag the sliders while the
// simulation is playing and watch the behaviour change live across all agents.
//
// There is no smoke in the scene yet. Low visibility is therefore modelled as a
// wide ring around anything tagged Fire. When real smoke arrives later, only the
// visibility branch in AgentNoise needs to change. Nothing else moves.
using UnityEngine;

public class HazardSettings : MonoBehaviour
{
    public static HazardSettings Instance;

    [Header("Hazard rings, measured from the nearest object tagged Fire")]
    [Tooltip("Inside the fire. Strongest ring.")]
    public float inFireRadius = 1.5f;

    [Tooltip("Right next to the fire.")]
    public float nearFireRadius = 3.5f;

    [Tooltip("Stand in for low visibility smoke until real smoke exists.")]
    public float lowVisibilityRadius = 7f;

    [Header("Health drain, fraction of max health lost per second")]
    [Range(0f, 1f)] public float inFireDrainPerSec = 0.40f;
    [Range(0f, 1f)] public float nearFireDrainPerSec = 0.20f;
    [Range(0f, 1f)] public float lowVisibilityDrainPerSec = 0.05f;

    [Header("Speed penalty while inside each ring, multiplies agent speed")]
    [Range(0.1f, 1f)] public float inFireSpeedFactor = 0.50f;
    [Range(0.1f, 1f)] public float nearFireSpeedFactor = 0.75f;
    [Range(0.1f, 1f)] public float lowVisibilitySpeedFactor = 0.90f;

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
        // Sanity guard so the rings never cross over each other in the Inspector.
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
        // FireSpread spawns its fire chunks at runtime, so this list must be
        // refreshed rather than cached once at Start.
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