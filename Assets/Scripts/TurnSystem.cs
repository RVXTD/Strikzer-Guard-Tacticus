using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class TurnSystem : MonoBehaviour
{
    // Set up the enums for the state machine
    public enum BattleState
    {
        playerTurn,
        enemyTurn,
        isMoving,
        isAttacking
    }
    // Set up player and enemy
    private GameObject player = GameObject.FindWithTag("Player");
    private GameObject enemy = GameObject.FindWithTag("Enemy");

    //Get important player and enemy values
    private Vector3 playerStartPos;

    private Vector3 enemyStartPos;

    private float playerHP;

    private float enemyHP;



    //Potential UI code to use
    //[SerializeField] TextMeshProUGUI playerHP;
    //[SerializeField] TextMeshProUGUI playerHP;

    // Set it to be the players turn to start
    private bool playerTurn = true;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    
    }

    // player attack method
    void playerAttack()
    {
        if (!playerTurn)
        {
            // Do nothing if not player's turn
            Debug.Log("It is not the player's turn to move");
            return;
        }
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
