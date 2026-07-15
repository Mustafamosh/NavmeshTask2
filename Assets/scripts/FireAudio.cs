// FireAudio.cs
// NEW FILE. Put on an empty GameObject in the scene, with an AudioSource.
//
// The fire starts on a click, so this source stays completely silent until at
// least one cell is actually burning. The moment the fire ignites it fades in,
// grows louder as the fire spreads, and fades back to silence if it burns out.
//
// FIX IN THIS VERSION
//   The fire that actually burns is created on click and is named
//   FireSpread_Spawned. The pre placed FireSpread is only a template and never
//   really burns. The previous version cached the template with FindAnyObjectByType
//   in Start, so it watched a fire that stayed at zero burning cells and never made
//   a sound. This version keeps looking for FireSpread_Spawned until it appears,
//   exactly like SmokeDetectorNode already does.
//
// One roaming source, not a sound per fire chunk, because FireSpread spawns and
// destroys chunks constantly. Dozens of overlapping loops restarting every respawn
// would sound like static. One source that follows the nearest fire to the camera
// reads as a single growing fire, which is what it is.
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class FireAudio : MonoBehaviour
{
    [Header("Clip")]
    [Tooltip("A seamless looping fire crackle.")]
    public AudioClip fireLoop;

    [Header("Volume scaling")]
    [Tooltip("Burning cell count at which the fire reaches full volume.")]
    public float cellsForFullVolume = 100f;

    [Tooltip("Floor volume once any fire exists, so a small fire is still clearly heard.")]
    [Range(0f, 1f)] public float minVolume = 0.5f;

    [Tooltip("How fast the sound fades in when the fire starts and out when it dies.")]
    public float fadeSpeed = 2f;

    [Header("Movement")]
    public float followSmoothing = 2f;
    public float retargetInterval = 0.5f;

    [Header("3D sound")]
    public float minDistance = 3f;
    public float maxDistance = 30f;

    private AudioSource source;
    private FireSpread fireSpread;
    private Camera cam;
    private float timer = 0f;
    private Vector3 target;
    private float currentVol = 0f;
    private bool started = false;

    void Start()
    {
        source = GetComponent<AudioSource>();
        cam = Camera.main;

        if (fireLoop != null) source.clip = fireLoop;

        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.volume = 0f;

        target = transform.position;
        // Deliberately not playing yet. Nothing sounds until the fire is clicked.
    }

    void Update()
    {
        if (source.clip == null) return;

        // Keep hunting for the spawned fire until it exists.
        ResolveSpawnedFire();

        int burning = fireSpread != null ? fireSpread.GetBurningCellsCount() : 0;

        // Silent until the fire actually starts.
        if (burning <= 0 && !started) return;

        // First ignition. Begin the loop once, then let volume handle the rest.
        if (burning > 0 && !started)
        {
            started = true;
            source.Play();
        }

        timer += Time.deltaTime;
        if (timer >= retargetInterval)
        {
            timer = 0f;
            RetargetNearestFire();
        }

        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * followSmoothing);

        ApplyVolume(burning);
    }

    // Prefer the clicked fire named FireSpread_Spawned. Only fall back to any
    // FireSpread if a spawned one is not present, so a manually placed fire still
    // makes sound in a test scene.
    void ResolveSpawnedFire()
    {
        if (fireSpread != null && fireSpread.name == "FireSpread_Spawned")
            return;

        FireSpread[] all = FindObjectsByType<FireSpread>(FindObjectsSortMode.None);

        foreach (FireSpread candidate in all)
        {
            if (candidate.name == "FireSpread_Spawned")
            {
                fireSpread = candidate;
                return;
            }
        }

        if (fireSpread == null && all.Length > 0)
            fireSpread = all[0];
    }

    void RetargetNearestFire()
    {
        GameObject[] fires = HazardSettings.Instance != null
            ? HazardSettings.Instance.GetFires()
            : GameObject.FindGameObjectsWithTag("Fire");

        if (fires.Length == 0) return;

        Vector3 ear = cam != null ? cam.transform.position : transform.position;

        float nearest = Mathf.Infinity;
        foreach (GameObject fire in fires)
        {
            if (fire == null) continue;
            float d = Vector3.Distance(ear, fire.transform.position);
            if (d < nearest)
            {
                nearest = d;
                target = fire.transform.position;
            }
        }
    }

    void ApplyVolume(int burning)
    {
        AudioManager am = AudioManager.Instance;

        bool audible = am == null || am.FireAudible;
        source.mute = !audible;

        float goal = 0f;
        if (audible && burning > 0)
        {
            float scale = Mathf.Lerp(minVolume, 1f, Mathf.Clamp01(burning / cellsForFullVolume));
            float master = am != null ? am.FireVolume : 1f;
            goal = scale * master;
        }

        currentVol = Mathf.MoveTowards(currentVol, goal, fadeSpeed * Time.deltaTime);
        source.volume = currentVol;
    }
}