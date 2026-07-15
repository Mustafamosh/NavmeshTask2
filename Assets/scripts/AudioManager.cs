// AudioManager.cs
// NEW FILE. Put one of these in the scene.
//
// Central switchboard for fire and alarm sound. Screams have been removed.
//
// Two layers of volume, and they persist in two different ways.
//   1. The values you set in the Inspector are saved into the scene file, which is
//      committed to git. So the defaults you choose push with the project and every
//      teammate and every fresh clone gets them.
//   2. Any change made while the game is running is saved to PlayerPrefs, which
//      lives on the player's own machine. This is the layer a published game needs,
//      so a player's chosen volume survives closing and reopening the game. It does
//      NOT push to git, which is correct, because one player's volume should not
//      become everyone's default.
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Master")]
    public bool masterMute = false;
    [Range(0f, 1f)] public float masterVolume = 1f;

    [Header("Fire, loud, the clip is faint on its own")]
    public bool muteFire = false;
    [Range(0f, 2f)] public float fireVolume = 1.4f;

    [Header("Alarm, low so the scene does not get noisy")]
    public bool muteAlarm = false;
    [Range(0f, 1f)] public float alarmVolume = 0.25f;

    [Header("Persistence")]
    [Tooltip("Load the player's saved volumes on start. Turn off during design so you always see your Inspector defaults.")]
    public bool loadSavedSettings = true;

    // PlayerPrefs keys.
    private const string KEY_MASTER_VOL = "audio_master_vol";
    private const string KEY_MASTER_MUTE = "audio_master_mute";
    private const string KEY_FIRE_VOL = "audio_fire_vol";
    private const string KEY_FIRE_MUTE = "audio_fire_mute";
    private const string KEY_ALARM_VOL = "audio_alarm_vol";
    private const string KEY_ALARM_MUTE = "audio_alarm_mute";

    void Awake()
    {
        Instance = this;
        if (loadSavedSettings) LoadSettings();
    }

    void Update()
    {
        // The alarm audio lives on the smoke detectors, which are not ours to edit,
        // so we mute and set their volume from out here. It runs every frame because
        // detectors only begin sounding partway through the run, never at startup.
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

    // ---------------- Queried by FireAudio ----------------

    public bool FireAudible => !masterMute && !muteFire;
    public float FireVolume => fireVolume * masterVolume;

    // ---------------- Persistence ----------------

    public void SaveSettings()
    {
        PlayerPrefs.SetFloat(KEY_MASTER_VOL, masterVolume);
        PlayerPrefs.SetInt(KEY_MASTER_MUTE, masterMute ? 1 : 0);
        PlayerPrefs.SetFloat(KEY_FIRE_VOL, fireVolume);
        PlayerPrefs.SetInt(KEY_FIRE_MUTE, muteFire ? 1 : 0);
        PlayerPrefs.SetFloat(KEY_ALARM_VOL, alarmVolume);
        PlayerPrefs.SetInt(KEY_ALARM_MUTE, muteAlarm ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void LoadSettings()
    {
        masterVolume = PlayerPrefs.GetFloat(KEY_MASTER_VOL, masterVolume);
        masterMute = PlayerPrefs.GetInt(KEY_MASTER_MUTE, masterMute ? 1 : 0) == 1;
        fireVolume = PlayerPrefs.GetFloat(KEY_FIRE_VOL, fireVolume);
        muteFire = PlayerPrefs.GetInt(KEY_FIRE_MUTE, muteFire ? 1 : 0) == 1;
        alarmVolume = PlayerPrefs.GetFloat(KEY_ALARM_VOL, alarmVolume);
        muteAlarm = PlayerPrefs.GetInt(KEY_ALARM_MUTE, muteAlarm ? 1 : 0) == 1;
    }

    // ---------------- Hooks for UI sliders and buttons ----------------
    // Wire a slider OnValueChanged to these, then call SaveSettings on release.

    public void SetMasterVolume(float v) { masterVolume = v; SaveSettings(); }
    public void SetFireVolume(float v) { fireVolume = v; SaveSettings(); }
    public void SetAlarmVolume(float v) { alarmVolume = v; SaveSettings(); }

    public void ToggleMasterMute() { masterMute = !masterMute; SaveSettings(); }
    public void ToggleFire() { muteFire = !muteFire; SaveSettings(); }
    public void ToggleAlarm() { muteAlarm = !muteAlarm; SaveSettings(); }
}