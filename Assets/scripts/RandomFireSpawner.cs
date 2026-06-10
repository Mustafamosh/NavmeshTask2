using UnityEngine;
using UnityEngine.AI;

public class RandomFireSpawner : MonoBehaviour
{
    public GameObject firePrefab;

    public float spawnRadius = 20f;
    public float spawnInterval = 5f;
    public int maxFires = 5;

    private int fireCount = 0;

    void Start()
    {
        InvokeRepeating(nameof(SpawnFire), 2f, spawnInterval);
    }

    void SpawnFire()
    {
        if (fireCount >= maxFires)
            return;

        for (int i = 0; i < 20; i++)
        {
            Vector3 randomPoint =
                transform.position +
                Random.insideUnitSphere * spawnRadius;

            randomPoint.y = 0;

            if (NavMesh.SamplePosition(
                randomPoint,
                out NavMeshHit hit,
                5f,
                NavMesh.AllAreas))
            {
                if (NavMesh.FindClosestEdge(
                    hit.position,
                    out NavMeshHit edgeHit,
                    NavMesh.AllAreas))
                {
                    if (edgeHit.distance < 4f)
                        continue;
                }

                Instantiate(
                    firePrefab,
                    hit.position,
                    Quaternion.identity);

                fireCount++;

                Debug.Log("Fire spawned at: " + hit.position);

                return;
            }
        }
    }
}