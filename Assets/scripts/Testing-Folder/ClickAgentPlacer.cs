using UnityEngine;
using UnityEngine.AI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public class ClickFireSpawn : MonoBehaviour
{
    public GameObject agentPrefab;
    public Camera mainCamera;

    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePosition = Mouse.current.position.ReadValue();

            Ray ray = mainCamera.ScreenPointToRay(mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                {
                    Instantiate(agentPrefab, navHit.position, Quaternion.identity);
                    Debug.Log("Agent spawned at: " + navHit.position);
                }
                else
                {
                    Debug.LogWarning("Clicked area is not on the NavMesh.");
                }
            }
        }
    }
}