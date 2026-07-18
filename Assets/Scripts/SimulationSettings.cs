using UnityEngine;

public static class SimulationSettings
{
    private const string AgentCountKey = "Simulation.AgentCount";
    private const string YoungPctKey = "Simulation.YoungPct";
    private const string AdultPctKey = "Simulation.AdultPct";
    private const string ElderlyPctKey = "Simulation.ElderlyPct";
    private const string DisabledPctKey = "Simulation.DisabledPct";

    public static int AgentCount { get; private set; } = 10;
    public static float YoungPct { get; private set; } = 33f;
    public static float AdultPct { get; private set; } = 34f;
    public static float ElderlyPct { get; private set; } = 33f;
    public static float DisabledPct { get; private set; } = 15f;

    public static void Save(int agentCount, float young, float adult, float elderly, float disabled)
    {
        AgentCount = Mathf.Clamp(agentCount, 0, 50);
        YoungPct = Mathf.Clamp(young, 0f, 100f);
        AdultPct = Mathf.Clamp(adult, 0f, 100f);
        ElderlyPct = Mathf.Clamp(elderly, 0f, 100f);
        DisabledPct = Mathf.Clamp(disabled, 0f, 100f);

        PlayerPrefs.SetInt(AgentCountKey, AgentCount);
        PlayerPrefs.SetFloat(YoungPctKey, YoungPct);
        PlayerPrefs.SetFloat(AdultPctKey, AdultPct);
        PlayerPrefs.SetFloat(ElderlyPctKey, ElderlyPct);
        PlayerPrefs.SetFloat(DisabledPctKey, DisabledPct);
        PlayerPrefs.Save();
    }

    public static void Load()
    {
        AgentCount = PlayerPrefs.HasKey(AgentCountKey) ? PlayerPrefs.GetInt(AgentCountKey) : 10;
        YoungPct = PlayerPrefs.HasKey(YoungPctKey) ? PlayerPrefs.GetFloat(YoungPctKey) : 33f;
        AdultPct = PlayerPrefs.HasKey(AdultPctKey) ? PlayerPrefs.GetFloat(AdultPctKey) : 34f;
        ElderlyPct = PlayerPrefs.HasKey(ElderlyPctKey) ? PlayerPrefs.GetFloat(ElderlyPctKey) : 33f;
        DisabledPct = PlayerPrefs.HasKey(DisabledPctKey) ? PlayerPrefs.GetFloat(DisabledPctKey) : 15f;

        AgentCount = Mathf.Clamp(AgentCount, 0, 50);
        YoungPct = Mathf.Clamp(YoungPct, 0f, 100f);
        AdultPct = Mathf.Clamp(AdultPct, 0f, 100f);
        ElderlyPct = Mathf.Clamp(ElderlyPct, 0f, 100f);
        DisabledPct = Mathf.Clamp(DisabledPct, 0f, 100f);
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(AgentCountKey);
        PlayerPrefs.DeleteKey(YoungPctKey);
        PlayerPrefs.DeleteKey(AdultPctKey);
        PlayerPrefs.DeleteKey(ElderlyPctKey);
        PlayerPrefs.DeleteKey(DisabledPctKey);
        PlayerPrefs.Save();
    }
}
