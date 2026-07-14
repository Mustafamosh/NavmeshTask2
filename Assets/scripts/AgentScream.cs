// AgentScream.cs
// NEW FILE. Put this on the agent prefab, alongside AgentNoise and AgentDataTracker.
//
// An agent screams when it is genuinely in trouble, meaning it has entered the
// NearFire or InFire band, or its health has dropped below half. It does not scream
// in light smoke, and it does not scream continuously, because twenty agents wailing
// nonstop is unbearable and would make the user study unpleasant.
//
// Rules
//   - Screams once on entering danger, then goes quiet for a cooldown.
//   - Voice is male or female, rolled once at spawn, with a random clip from that set.
//   - Pitch is nudged slightly per agent so the same clip never sounds identical twice.
//   - Asks AudioManager for permission first, so no more than a few agents scream at
//     once and the mix stays readable.
//
// This is purely presentational. Nothing here is logged and nothing here affects
// agent behaviour or the AI coach.
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AgentScream : MonoBehaviour
{
    public enum Voice { Male, Female }

    [Header("Voice")]
    [Tooltip("Rolled at spawn. Purely an audio choice, it does not affect behaviour.")]
    public Voice voice = Voice.Male;

    [Tooltip("Chance this agent uses the female clip set.")]
    [Range(0f, 1f)] public float femaleChance = 0.5f;

    [Header("Clips")]
    public AudioClip[] maleScreams;
    public AudioClip[] femaleScreams;

    [Header("Trigger conditions")]
    [Tooltip("Scream when health drops below this fraction of max.")]
    [Range(0f, 1f)] public float lowHealthThreshold = 0.5f;

    [Tooltip("Seconds before this agent is allowed to scream again.")]
    public float cooldown = 6f;

    [Tooltip("Random delay before screaming, so a group entering fire together does not shout in unison.")]
    public float maxReactionDelay = 0.6f;

    [Header("Sound")]
    [Range(0f, 1f)] public float clipVolume = 1f;
    public float minPitch = 0.92f;
    public float maxPitch = 1.08f;
    public float minDistance = 2f;
    public float maxDistance = 25f;

    private AudioSource source;
    private AgentNoise profile;

    private float cooldownTimer = 0f;
    private float pendingDelay = -1f;
    private bool releasePending = false;
    private float releaseTimer = 0f;

    // Remembers the last state so we scream on ENTERING danger rather than every
    // frame we happen to be in it.
    private bool wasInDanger = false;
    private bool hasScreamedForLowHealth = false;

    void Start()
    {
        source = GetComponent<AudioSource>();
        profile = GetComponent<AgentNoise>();

        voice = Random.value < femaleChance ? Voice.Female : Voice.Male;

        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;          // fully 3D, so screams come from the person
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
    }

    void Update()
    {
        if (profile == null) return;

        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        // Tell the manager when our clip has finished, so it can free up a slot.
        if (releasePending)
        {
            releaseTimer -= Time.deltaTime;
            if (releaseTimer <= 0f)
            {
                releasePending = false;
                if (AudioManager.Instance != null)
                    AudioManager.Instance.ReleaseScream();
            }
        }

        // A queued scream waiting out its reaction delay.
        if (pendingDelay >= 0f)
        {
            pendingDelay -= Time.deltaTime;
            if (pendingDelay <= 0f)
            {
                pendingDelay = -1f;
                PlayScream();
            }
            return;
        }

        EvaluateDanger();
    }

    void EvaluateDanger()
    {
        bool inDangerousBand =
            profile.currentBand == AgentNoise.HazardBand.NearFire ||
            profile.currentBand == AgentNoise.HazardBand.InFire;

        bool lowHealth =
            profile.maxHealth > 0f &&
            profile.health / profile.maxHealth < lowHealthThreshold;

        // Scream on the moment of entering a dangerous band, not the whole time.
        if (inDangerousBand && !wasInDanger)
            QueueScream();

        // Scream once when health first crosses below half.
        if (lowHealth && !hasScreamedForLowHealth)
        {
            hasScreamedForLowHealth = true;
            QueueScream();
        }

        wasInDanger = inDangerousBand;
    }

    void QueueScream()
    {
        if (cooldownTimer > 0f) return;
        if (pendingDelay >= 0f) return;

        pendingDelay = Random.Range(0f, maxReactionDelay);
    }

    void PlayScream()
    {
        AudioClip[] set = voice == Voice.Female ? femaleScreams : maleScreams;

        if (set == null || set.Length == 0) return;

        AudioManager am = AudioManager.Instance;

        // Ask before shouting. If too many agents are already screaming, stay quiet
        // but still burn the cooldown, so we do not immediately try again.
        if (am != null && !am.RequestScream())
        {
            cooldownTimer = cooldown;
            return;
        }

        AudioClip clip = set[Random.Range(0, set.Length)];
        if (clip == null) return;

        source.pitch = Random.Range(minPitch, maxPitch);
        source.volume = clipVolume * (am != null ? am.ScreamVolume : 1f);
        source.PlayOneShot(clip);

        cooldownTimer = cooldown;

        // Release the slot once the clip has run its course.
        if (am != null)
        {
            releasePending = true;
            releaseTimer = clip.length / Mathf.Max(0.1f, source.pitch);
        }
    }

    // If the agent is destroyed mid scream, free the slot so it is not lost forever.
    void OnDestroy()
    {
        if (releasePending && AudioManager.Instance != null)
            AudioManager.Instance.ReleaseScream();
    }
}