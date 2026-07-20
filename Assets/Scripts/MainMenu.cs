using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenu : MonoBehaviour
{
    [Header("UI")]
    public Slider agentCountSlider;
    public Slider youngSlider;
    public Slider adultSlider;
    public Slider elderlySlider;
    public Slider disabledSlider;
    public TMP_Text agentCountLabel;
    public TMP_Text youngLabel;
    public TMP_Text adultLabel;
    public TMP_Text elderlyLabel;
    public TMP_Text disabledLabel;
    public Button startButton;
    public Button backButton;

    [Header("Scene")]
    public string simulationSceneName = "3Simulation";
    public string startMenuSceneName = "1Start-Menu";

    private bool updatingSliders = false;

    void Start()
    {
        SimulationSettings.Load();

        if (agentCountSlider != null)
        {
            agentCountSlider.wholeNumbers = true;
            agentCountSlider.minValue = 0;
            agentCountSlider.maxValue = 50;
            agentCountSlider.value = SimulationSettings.AgentCount;
            agentCountSlider.onValueChanged.AddListener(_ => RefreshLabels());
        }

        HookAgeSlider(youngSlider);
        HookAgeSlider(adultSlider);
        HookAgeSlider(elderlySlider);

        if (disabledSlider != null)
        {
            disabledSlider.minValue = 0;
            disabledSlider.maxValue = 100;
            disabledSlider.value = SimulationSettings.DisabledPct;
            disabledSlider.onValueChanged.AddListener(_ => RefreshLabels());
        }

        if (youngSlider != null) youngSlider.value = SimulationSettings.YoungPct;
        if (adultSlider != null) adultSlider.value = SimulationSettings.AdultPct;
        if (elderlySlider != null) elderlySlider.value = SimulationSettings.ElderlyPct;

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(StartSimulation);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(ReturnToStartMenu);
        }

        RefreshLabels();
    }

    void HookAgeSlider(Slider slider)
    {
        if (slider == null) return;
        slider.minValue = 0;
        slider.maxValue = 100;
        slider.onValueChanged.AddListener(_ => OnAgeChanged(slider));
    }

    void OnAgeChanged(Slider changed)
    {
        if (updatingSliders) return;
        NormalizeAges(changed);
        RefreshLabels();
    }

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

    public void StartSimulation()
    {
        int count = agentCountSlider != null ? (int)agentCountSlider.value : SimulationSettings.AgentCount;
        float young = youngSlider != null ? youngSlider.value : SimulationSettings.YoungPct;
        float adult = adultSlider != null ? adultSlider.value : SimulationSettings.AdultPct;
        float elderly = elderlySlider != null ? elderlySlider.value : SimulationSettings.ElderlyPct;
        float disabled = disabledSlider != null ? disabledSlider.value : SimulationSettings.DisabledPct;

        SimulationSettings.Save(count, young, adult, elderly, disabled);
        SceneManager.LoadScene(simulationSceneName);
    }

    public void ReturnToStartMenu()
    {
        SceneManager.LoadScene(startMenuSceneName);
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