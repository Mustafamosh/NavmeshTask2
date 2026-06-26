using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class UICounter : MonoBehaviour
{
    public TextMeshProUGUI insideText;
    public TextMeshProUGUI exitedText;
    public TextMeshProUGUI mainHallText;
    public TextMeshProUGUI classroomText;
    public TextMeshProUGUI officesText;
    public TextMeshProUGUI bathroomsText;

    void Update()
    {
        int exited = AgentExitBehavior.agentsExited;
        int inside = FindObjectsByType<AgentExitNavigator>().Length;

        insideText.text = "Agents Inside: " + inside;
        exitedText.text = "Agents Exited: " + exited;

        // Display zone occupancy counts
        Dictionary<string, int> zoneCounts = ZoneOccupancy.GetZoneCounts();
        
        if (zoneCounts.ContainsKey("Main Hall"))
            mainHallText.text = "Main Hall: " + zoneCounts["Main Hall"];
        if (zoneCounts.ContainsKey("Classroom"))
            classroomText.text = "Classroom: " + zoneCounts["Classroom"];
        if (zoneCounts.ContainsKey("Offices"))
            officesText.text = "Offices: " + zoneCounts["Offices"];
        if (zoneCounts.ContainsKey("Bathrooms"))
            bathroomsText.text = "Bathrooms: " + zoneCounts["Bathrooms"];
    }
}