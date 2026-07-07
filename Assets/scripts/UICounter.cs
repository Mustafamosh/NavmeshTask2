using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class UICounter : MonoBehaviour
{
    public TextMeshProUGUI insideText;
    public TextMeshProUGUI exitedText;
    public TextMeshProUGUI trappedText;
    public TextMeshProUGUI mainHallText;
    public TextMeshProUGUI classroomText;
    public TextMeshProUGUI officesText;
    public TextMeshProUGUI bathroomsText;

    void Update()
    {
        int exited = AgentDataTracker.agentsExited; // AgentExitBehavior deleted; static moved to AgentDataTracker
        int inside = FindObjectsByType<AgentExitNavigator>().Length;
        int trapped = AgentDataTracker.agentsTrapped;

        insideText.text = "Agents Inside: " + inside;
        exitedText.text = "Agents Exited: " + exited;
        if (trappedText != null)
            trappedText.text = "Agents Trapped: " + trapped;

        Dictionary<string, int> zoneCounts = ZoneOccupancy.GetZoneCounts();

        if (mainHallText != null && zoneCounts.ContainsKey("Main Hall"))
            mainHallText.text = "Main Hall: " + zoneCounts["Main Hall"];
        if (classroomText != null && zoneCounts.ContainsKey("Classroom"))
            classroomText.text = "Classroom: " + zoneCounts["Classroom"];
        if (officesText != null && zoneCounts.ContainsKey("Offices"))
            officesText.text = "Offices: " + zoneCounts["Offices"];
        if (bathroomsText != null && zoneCounts.ContainsKey("Bathrooms"))
            bathroomsText.text = "Bathrooms: " + zoneCounts["Bathrooms"];
    }
}