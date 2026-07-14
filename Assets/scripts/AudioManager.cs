// AudioManager.cs
// NEW FILE. Put one of these in the scene.
//
// Central switchboard for every sound in the simulation. Fire, alarm, and screams
// can each be muted independently, and there is a master mute for the whole thing.
//
// This matters for the user study. Participants may be wearing headphones, and a
// wall of overlapping screams is genuinely unpleasant, so there needs to be a way
// to turn it off without editing prefabs.
//
// The alarm is handled differently from the others. FireAlarmSystem and
// SmokeDetectorNode already own the alarm audio, so instead of editing those files
// this script reaches out and mutes their AudioSources directly. Nothing in the
// existing alarm logic changes.
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Master")]
    public bool masterMute = false;
    [Range(0f, 1f)] public float masterVolume = 1f;

    [Header("Fire")]
    public bool muteFire = false;
    [Range(0f, 1f)] public float fireVolume = 0.7f;

    [Header("Alarm")]
    public bool muteAlarm = false;
    [Range(0f, 1f)] public float alarmVolume = 0.5f;

    [Header("Screams")]
    public bool muteScreams = false;
    [Range(0f, 1f)] public float screamVolume = 0.6f;

    [Tooltip("Hard ceiling on how many agents can be screaming at once, so the mix never turns into noise.")]
    public int maxConcurrentScreams = 4;

    // How many screams are sounding right now. AgentScream asks before it plays.
    private int activeScreams = 0;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        // The alarm lives on the smoke detectors, which are not ours to edit, so we
        // mute them from out here instead. This runs every frame because detectors
        // start sounding partway through the run rather than at startup.
        ApplyAlarmSettings();
    }

    void ApplyAlarmSettings()
    {
        SmokeDetectorNode[] detectors = FindObjectsByType<SmokeDetectorNode>(FindObjectsSortMode.None);

        foreach (SmokeDetectorNode detector in detectors)
        {
            AudioSource src = detector.GetComponent<AudioSource>();
            if (src == null) continue;

            src.mute = masterMute || muteAlarm;
            src.volume = alarmVolume * masterVolume;
        }
    }

    // ---------------- Queried by the other audio scripts ----------------

    public bool FireAudible => !masterMute && !muteFire;
    public float FireVolume => fireVolume * masterVolume;

    public bool ScreamsAudible => !masterMute && !muteScreams;
    public float ScreamVolume => screamVolume * masterVolume;

    /// <summary>
    /// An agent asks permission before screaming. This keeps the number of
    /// simultaneous screams under the cap so the mix stays readable.
    /// </summary>
    public bool RequestScream()
    {
        if (!ScreamsAudible) return false;
        if (activeScreams >= maxConcurrentScreams) return false;

        activeScreams++;
        return true;
    }

    /// <summary>
    /// Called by the agent once its scream clip has finished.
    /// </summary>
    public void ReleaseScream()
    {
        activeScreams = Mathf.Max(0, activeScreams - 1);
    }

    // ---------------- Hooks for a UI mute button ----------------

    public void ToggleMasterMute()
    {
        masterMute = !masterMute;
    }

    public void ToggleScreams()
    {
        muteScreams = !muteScreams;
    }

    public void ToggleFire()
    {
        muteFire = !muteFire;
    }

    public void ToggleAlarm()
    {
        muteAlarm = !muteAlarm;
    }
}