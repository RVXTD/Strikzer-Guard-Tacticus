using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public class CharMovement : MonoBehaviour
{
    private NavMeshAgent agent;
    private GameObject player;

    [Header("Movement Settings")]
    public float moveSpeed = 10f;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        player = GameObject.FindGameObjectWithTag("Player");
    }

    private void Update()
    {
        // Check if the left mouse button was pressed this frame
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Get the mouse position on the screen
            Vector2 mousePosition = Mouse.current.position.ReadValue();

            // Create a ray from the camera through the mouse position
            Ray ray = Camera.main.ScreenPointToRay(mousePosition);

            // Check if the ray hits something
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Tell the NavMeshAgent to move to the hit position
                agent.SetDestination(hit.point);
                Debug.Log("Player has moved to: " + player.transform.position.x + ", " + player.transform.position.y + ", " + player.transform.position.z);
            }
            else
            {
                Debug.Log("Player is unable to move to this position.");
            }
        }
    }
}