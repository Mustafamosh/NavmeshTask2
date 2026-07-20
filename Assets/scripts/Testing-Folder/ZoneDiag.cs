using UnityEngine;
using System.Linq;

public class ZoneDiag : MonoBehaviour
{
    void Start()
    {
        var zones = GameObject.FindGameObjectsWithTag("Zone");
        Debug.Log("Objects tagged Zone: " + zones.Length);

        foreach (var g in zones.GroupBy(z => z.name))
            Debug.Log("Zone name '" + g.Key + "' appears " + g.Count() + " times");

        var occ = FindObjectsByType<ZoneOccupancy>(FindObjectsSortMode.None);
        Debug.Log("ZoneOccupancy components in scene: " + occ.Length);

        foreach (var o in occ)
            Debug.Log(o.gameObject.name + " colliders: "
                + o.GetComponents<Collider>().Length);

        foreach (var kv in ZoneOccupancy.GetZoneCounts())
            Debug.Log("PRE RUN COUNT " + kv.Key + " = " + kv.Value);
    }
}