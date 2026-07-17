// SimulationUI.cs
// NEW FILE. Put on the side panel, drag the controls into its fields.
//
// This wires the panel to the controller and spawner. The three age sliders are
// kept summing to exactly 100. When the user drags one, the other two share the
// remainder in proportion, so the total is always 100 with no error state needed.
//
// Agent count and type are only editable in Setup. Start, Stop, Pause, and the
// audio controls are always available.
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimulationUI : MonoBehaviour
{
    [Header("Core references")]
    public SimulationController controller;
    public AgentSpawner spawner;
    public AudioManager audioManager;

    [Header("Agent count")]
    public Slider agentCountSlider;
    public TMP_Text agentCountLabel;

    [Header("Age percentage sliders")]
    public Slider youngSlider;
    public Slider adultSlider;
    public Slider elderlySlider;
    public TMP_Text youngLabel;
    public TMP_Text adultLabel;
    public TMP_Text elderlyLabel;

    [Header("Disabled percentage")]
    public Slider disabledSlider;
    public TMP_Text disabledLabel;

    [Header("Buttons")]
    public Button startButton;
    public Button stopButton;
    public Button pauseButton;

    [Header("Audio")]
    public Slider masterVolumeSlider;
    public UnityEngine.UI.Toggle muteToggle;
    
    private bool updatingSliders = false;

    void Start()
    {
        if (agentCountSlider != null)
        {
            agentCountSlider.wholeNumbers = true;
            agentCountSlider.minValue = 0;
            agentCountSlider.maxValue = spawner != null ? spawner.maxAgents : 50;
            agentCountSlider.onValueChanged.AddListener(OnAgentCountChanged);
        }

        HookAgeSlider(youngSlider);
        HookAgeSlider(adultSlider);
        HookAgeSlider(elderlySlider);

        if (disabledSlider != null)
        {
            disabledSlider.minValue = 0;
            disabledSlider.maxValue = 100;
            disabledSlider.onValueChanged.AddListener(_ => { PushDistribution(); RefreshLabels(); });
        }

        if (startButton != null) startButton.onClick.AddListener(OnStart);
        if (stopButton != null) stopButton.onClick.AddListener(OnStop);
        if (pauseButton != null) pauseButton.onClick.AddListener(OnPause);

        if (masterVolumeSlider != null) masterVolumeSlider.onValueChanged.AddListener(OnVolume);
        if (muteToggle != null) muteToggle.onValueChanged.AddListener(OnMute);

        // Sensible starting split.
        updatingSliders = true;
        if (youngSlider != null) youngSlider.value = 33;
        if (adultSlider != null) adultSlider.value = 34;
        if (elderlySlider != null) elderlySlider.value = 33;
        updatingSliders = false;

        RefreshLabels();
        SetSetupControlsInteractable(true);
    }

    void HookAgeSlider(Slider s)
    {
        if (s == null) return;
        s.minValue = 0;
        s.maxValue = 100;
        s.onValueChanged.AddListener(_ => OnAgeChanged(s));
    }

    void OnAgentCountChanged(float v)
    {
        if (spawner != null) spawner.SetAgentCount((int)v);
        RefreshLabels();
    }

    void OnAgeChanged(Slider changed)
    {
        if (updatingSliders) return;
        NormalizeAges(changed);
        PushDistribution();
        RefreshLabels();
    }

    // Keep young plus adult plus elderly equal to 100. The two sliders the user did
    // not touch share whatever is left, in proportion to their current values.
    void NormalizeAges(Slider changed)
    {
        if (youngSlider == null || adultSlider == null || elderlySlider == null) return;

        updatingSliders = true;

        float remaining = 100f - changed.value;

        Slider a, b;
        if (changed == youngSlider) { a = adultSlider; b = elderlySlider; }
        else if (changed == adultSlider) { a = youngSlider; b = elderlySlider; }
        else { a = youngSlider; b = adultSlider; }

        float sumOthers = a.value + b.value;
        if (sumOthers <= 0.001f)
        {
            a.value = remaining / 2f;
            b.value = remaining / 2f;
        }
        else
        {
            a.value = remaining * (a.value / sumOthers);
            b.value = remaining * (b.value / sumOthers);
        }

        updatingSliders = false;
    }

    void PushDistribution()
    {
        if (spawner == null) return;
        spawner.SetDistribution(
            youngSlider != null ? youngSlider.value : 33f,
            adultSlider != null ? adultSlider.value : 34f,
            elderlySlider != null ? elderlySlider.value : 33f,
            disabledSlider != null ? disabledSlider.value : 0f
        );
    }

    void OnStart()
    {
        if (controller != null) controller.StartSimulation();
        SetSetupControlsInteractable(false);
    }

    void OnStop()
    {
        if (controller != null) controller.StopSimulation();

        updatingSliders = true;
        if (agentCountSlider != null) agentCountSlider.value = 0;
        updatingSliders = false;

        SetSetupControlsInteractable(true);
        RefreshLabels();
    }

    void OnPause()
    {
        if (controller != null) controller.PauseSimulation();
    }

    void OnVolume(float v)
    {
        if (audioManager != null) audioManager.SetMasterVolume(v);
    }

    void OnMute(bool on)
    {
        if (audioManager != null)
        {
            audioManager.masterMute = on;
            audioManager.SaveSettings();
        }
    }

    // Only the agent setup controls lock after Start. Buttons and audio stay live.
    void SetSetupControlsInteractable(bool on)
    {
        if (agentCountSlider != null) agentCountSlider.interactable = on;
        if (youngSlider != null) youngSlider.interactable = on;
        if (adultSlider != null) adultSlider.interactable = on;
        if (elderlySlider != null) elderlySlider.interactable = on;
        if (disabledSlider != null) disabledSlider.interactable = on;
    }

    void RefreshLabels()
    {
        if (agentCountLabel != null && agentCountSlider != null)
            agentCountLabel.text = "Agents: " + (int)agentCountSlider.value;
        if (youngLabel != null && youngSlider != null)
            youngLabel.text = "Young: " + Mathf.RoundToInt(youngSlider.value) + "%";
        if (adultLabel != null && adultSlider != null)
            adultLabel.text = "Adult: " + Mathf.RoundToInt(adultSlider.value) + "%";
        if (elderlyLabel != null && elderlySlider != null)
            elderlyLabel.text = "Elderly: " + Mathf.RoundToInt(elderlySlider.value) + "%";
        if (disabledLabel != null && disabledSlider != null)
            disabledLabel.text = "Disabled: " + Mathf.RoundToInt(disabledSlider.value) + "%";
    }
}