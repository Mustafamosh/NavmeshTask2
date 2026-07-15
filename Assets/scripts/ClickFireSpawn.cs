using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace NavmeshInt
{
    public class ClickFireSpawn : MonoBehaviour
    {
        [Header("Camera used for raycasting")]
        public Camera mainCamera;

        private FireSpread templateFireSpread;
        private GameObject spawnedFireSpreadObj;

        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            // Find the existing FireSpread in the scene to use as a template
            templateFireSpread = FindAnyObjectByType<FireSpread>();
            if (templateFireSpread == null)
            {
                Debug.LogError("ClickFireSpawn: No FireSpread found in the scene. Please add one.");
            }
        }

        private void Update()
        {
            if (Mouse.current == null || mainCamera == null || templateFireSpread == null)
            {
                return;
            }

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                // Check if a spawned fire already exists
                if (spawnedFireSpreadObj != null)
                {
                    Debug.LogWarning("Only one fire can be spawned at a time. Destroy the existing fire first.");
                    return;
                }

                Vector2 mousePosition = Mouse.current.position.ReadValue();
                Ray ray = mainCamera.ScreenPointToRay(mousePosition);

                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                    {
                        SpawnFireSpreadAtPosition(navHit.position);
                    }
                    else
                    {
                        Debug.LogWarning("Clicked area is not on the NavMesh.");
                    }
                }
            }
        }

        private void SpawnFireSpreadAtPosition(Vector3 position)
        {
            // Create a new empty GameObject to hold the FireSpread component
            spawnedFireSpreadObj = new GameObject("FireSpread_Spawned");
            spawnedFireSpreadObj.transform.position = position;
            spawnedFireSpreadObj.transform.rotation = Quaternion.identity;

            // Add and configure the FireSpread component
            FireSpread newFireSpread = spawnedFireSpreadObj.AddComponent<FireSpread>();

            // Copy configuration from the template
            CopyFireSpreadSettings(templateFireSpread, newFireSpread);

            // Ensure the floor object is assigned
            if (newFireSpread.floorObject == null) 
            {
                GameObject floorObject = GameObject.Find("Floor");
                if (floorObject != null)
                {
                    newFireSpread.floorObject = floorObject;
                }
                else
                {
                    Debug.LogWarning("FireSpread spawned but no Floor object found in the scene.");
                }
            }

            Debug.Log("FireSpread spawned at: " + position);
        }

        private void CopyFireSpreadSettings(FireSpread source, FireSpread destination)
        {
            // Grid Setup
            destination.cellSize = source.cellSize;
            destination.autoCalculateGridSize = source.autoCalculateGridSize;
            destination.cols = source.cols;
            destination.rows = source.rows;

            // Environment Detection
            destination.floorObject = source.floorObject;
            destination.obstacleLayer = source.obstacleLayer;
            destination.obstacleCheckHeight = source.obstacleCheckHeight;

            // Tick Rate
            destination.tickInterval = source.tickInterval;

            // Tuning Constants
            destination.baseSpreadProb = source.baseSpreadProb;
            destination.draftVector = source.draftVector;
            destination.draftSpreadBonus = source.draftSpreadBonus;

            // Smoke Model
            destination.earlyBurnSmokeAmount = source.earlyBurnSmokeAmount;
            destination.fullBurnSmokeAmount = source.fullBurnSmokeAmount;
            destination.smokeSpreadFactor = source.smokeSpreadFactor;
            destination.smokeFadeFactor = source.smokeFadeFactor;

            // Visual Optimization
            destination.visualChunkSize = source.visualChunkSize;
            destination.firePrefab = source.firePrefab;

            // Fire Behavior
            destination.allowFireExtinguishing = source.allowFireExtinguishing;
            destination.enableFireStart = true; // Ensure fire start is enabled for the new instance
            destination.maxFires = source.maxFires;

            // Logging
            destination.enableTickLog = source.enableTickLog;
            destination.logEveryNTicks = source.logEveryNTicks;
        }
    }
}