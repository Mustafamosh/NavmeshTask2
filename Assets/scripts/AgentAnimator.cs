using UnityEngine;
using UnityEngine.AI;

public class AgentAnimator : MonoBehaviour
{
    private Animator animator;
    private NavMeshAgent navAgent;

    void Start()
    {
        navAgent = GetComponentInParent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        float speed = navAgent.velocity.magnitude;
        animator.SetFloat("Speed", speed);
    }
}