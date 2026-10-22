
using UnityEngine;
using UnityEngine.InputSystem;

public class TurnInputs : MonoBehaviour
{
    public TurnSystem turnManager;

    private bool selectingTarget;

    public void BeginAttack()
    {
        BasicCombatant active = turnManager.CurrentUnit;

        if (active == null || !active.isPlayer ||
            active.IsDefeated || active.HasAttacked)
            return;

        selectingTarget = true;

        CharMovement movement =
            active.GetComponent<CharMovement>();

        if (movement != null)
            movement.enabled = false;

        Debug.Log("Click an enemy to attack.");
    }

    
private void Update()
{
    if (Keyboard.current == null)
        return;

    // Press E to end the player's turn.
    if (Keyboard.current.eKey.wasPressedThisFrame)
    {
        EndPlayerTurn();
        return;
    }

    // Press F to enter attack-target selection.
    if (Keyboard.current.fKey.wasPressedThisFrame)
    {
        BeginAttack();
    }

    // Handle clicking an enemy while selecting a target.
    if (!selectingTarget ||
        Mouse.current == null ||
        !Mouse.current.leftButton.wasPressedThisFrame)
        return;

    Ray ray = Camera.main.ScreenPointToRay(
        Mouse.current.position.ReadValue());

    if (!Physics.Raycast(ray, out RaycastHit hit))
        return;

    BasicCombatant target =
        hit.collider.GetComponentInParent<BasicCombatant>();

    if (target == null)
    {
        Debug.Log("Select an enemy.");
        return;
    }

    BasicCombatant active = turnManager.CurrentUnit;

    if (active == null || target.isPlayer ||
        target.IsDefeated)
    {
        Debug.Log("Invalid target.");
        return;
    }

    active.Attack(target);
    selectingTarget = false;

    CharMovement movement =
        active.GetComponent<CharMovement>();

    if (movement != null)
        movement.enabled = true;
    else if (movement = null)
        {
            Debug.Log("Player cannot move, it isn't their turn");
        }
}


    public void EndPlayerTurn()
    {
        BasicCombatant active = turnManager.CurrentUnit;

        if (active == null || !active.isPlayer)
            return;

        selectingTarget = false;

        CharMovement movement =
            active.GetComponent<CharMovement>();

        if (movement != null)
            movement.enabled = false;

        turnManager.EndTurn();
    }
}
