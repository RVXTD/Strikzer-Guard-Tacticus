using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

public class CharMovement : MonoBehaviour
{
    [SerializeField] private AudioClip selectSound;//
    [SerializeField] private AudioClip moveSound;//
    
    private NavMeshAgent agent;
    private GameObject player;
    private Vector3 turnStartPos;

    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    public float maxDistance = 5f;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        player = GameObject.FindGameObjectWithTag("Player");
    }

    private void CanMoveTo(Vector3 Destination)
    {
        float distance = Vector3.Distance(turnStartPos, Destination);
    }


    private void Update()
    {
        // Check if the left mouse button was pressed this frame
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            SoundFXManager.instance.PlaySoundFXClip(selectSound,transform, 0.2f);//
            // Get the mouse position on the screen
            Vector2 mousePosition = Mouse.current.position.ReadValue();

            

            // Create a ray from the camera through the mouse position
            Ray ray = Camera.main.ScreenPointToRay(mousePosition);

            // Check if the ray hits something
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Tell the NavMeshAgent to move to the hit position
                SoundFXManager.instance.PlaySoundFXClip(moveSound,transform , 0.3f);//
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