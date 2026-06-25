using UnityEngine;
using TMPro;

public class UICounter : MonoBehaviour
{
    public TextMeshProUGUI insideText;
    public TextMeshProUGUI exitedText;
    public TextMeshProUGUI trappedText;

    void Update()
    {
        int exited = AgentExitBehavior.agentsExited;
        int inside = FindObjectsByType<AgentExitNavigator>().Length;
        int trapped = AgentDataTracker.agentsTrapped;

        insideText.text = "Agents Inside: " + inside;
        exitedText.text = "Agents Exited: " + exited;
        trappedText.text = "Agents Trapped: " + trapped;
    }
}