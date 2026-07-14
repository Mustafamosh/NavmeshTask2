// FireAudio.cs
// NEW FILE. Put one of these in the scene, on an empty GameObject.
//
// Why a single roaming source rather than a sound on each fire prefab
//   FireSpread spawns and destroys fire chunk prefabs constantly as the fire moves,
//   up to maxFires at once. An AudioSource on the fire prefab would mean dozens of
//   overlapping crackle loops, each restarting from zero every time a chunk
//   respawns. That sounds like static, and it is wasteful.
//
//   Instead there is one AudioSource. It glides toward whichever fire is nearest to
//   the camera, and its volume rises with the total number of burning cells, so a
//   small fire crackles quietly and a large one roars. To the listener it reads as
//   one growing fire, which is what it actually is.
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class FireAudio : MonoBehaviour
{
    [Header("Clip")]
    [Tooltip("A seamless looping fire crackle. Tick Loop on the AudioSource.")]
    public AudioClip fireLoop;

    [Header("Volume scaling")]
    [Tooltip("Burning cell count at which the fire reaches full volume.")]
    public float cellsForFullVolume = 200f;

    [Tooltip("Volume when only a handful of cells are alight, so a small fire is still audible.")]
    [Range(0f, 1f)] public float minVolume = 0.15f;

    [Header("Movement")]
    [Tooltip("How quickly the source glides to the nearest fire. Low values avoid it snapping around.")]
    public float followSmoothing = 2f;

    [Tooltip("How often the nearest fire is recalculated, in seconds.")]
    public float retargetInterval = 0.5f;

    [Header("3D sound")]
    public float minDistance = 3f;
    public float maxDistance = 30f;

    private AudioSource source;
    private FireSpread fireSpread;
    private Camera cam;
    private float timer = 0f;
    private Vector3 target;

    void Start()
    {
        source = GetComponent<AudioSource>();
        fireSpread = FindAnyObjectByType<FireSpread>();
        cam = Camera.main;

        if (fireLoop != null)
            source.clip = fireLoop;

        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 1f;          // fully 3D, so it comes from the fire
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.volume = 0f;

        target = transform.position;

        if (source.clip != null)
            source.Play();
    }

    void Update()
    {
        if (source.clip == null) return;

        timer += Time.deltaTime;
        if (timer >= retargetInterval)
        {
            timer = 0f;
            RetargetNearestFire();
        }

        // Glide rather than teleport, otherwise the sound jumps across the room
        // every time a closer fire chunk spawns.
        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * followSmoothing);

        ApplyVolume();
    }

    void RetargetNearestFire()
    {
        GameObject[] fires = HazardSettings.Instance != null
            ? HazardSettings.Instance.GetFires()
            : GameObject.FindGameObjectsWithTag("Fire");

        if (fires.Length == 0) return;

        // Nearest to the camera, not to this object, since the camera is the listener.
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

    void ApplyVolume()
    {
        AudioManager am = AudioManager.Instance;

        bool audible = am == null || am.FireAudible;
        source.mute = !audible;

        if (!audible) return;

        int burning = fireSpread != null ? fireSpread.GetBurningCellsCount() : 0;

        if (burning <= 0)
        {
            source.volume = 0f;
            return;
        }

        // Volume rises with fire size, from minVolume up to full.
        float scale = Mathf.Lerp(minVolume, 1f, Mathf.Clamp01(burning / cellsForFullVolume));
        float master = am != null ? am.FireVolume : 1f;

        source.volume = scale * master;
    }
}