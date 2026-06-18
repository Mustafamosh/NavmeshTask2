using UnityEngine;
using TMPro;

public class UICounter : MonoBehaviour
{
    public TextMeshProUGUI insideText;
    public TextMeshProUGUI exitedText;

    void Update()
    {
        int exited = AgentExitBehavior.agentsExited;
        int inside = FindObjectsByType<AgentExitNavigator>().Length;

        insideText.text = "Agents Inside: " + inside;
        exitedText.text = "Agents Exited: " + exited;
    }
}