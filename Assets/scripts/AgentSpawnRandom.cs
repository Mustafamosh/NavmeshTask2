using UnityEngine;

public class AgentSpawnRandom : MonoBehaviour
{
    public GameObject[] characterModels;

    void Awake()
    {
        foreach (GameObject model in characterModels)
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.enabled = false;

        System.Random rng = new System.Random(GetHashCode() ^ System.Environment.TickCount);
        int pick = rng.Next(0, characterModels.Length);

        foreach (SkinnedMeshRenderer smr in characterModels[pick].GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.enabled = true;
            
        Debug.Log("Agent picked: " + characterModels[pick].name);
    }
}