
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public class TurnSystem : MonoBehaviour
{
    public List<BasicCombatant> units = new List<BasicCombatant>();

    public BasicCombatant CurrentUnit { get; private set; }

    public System.Action OnTurnChanged;

    private int currentIndex = -1;

    private void Start()
    {
        units = units
            .Where(u => u != null)
            .OrderByDescending(u => u.speed)
            .ToList();

        // Disable movement until a unit's turn begins.
        foreach (BasicCombatant unit in units)
        {
            CharMovement movement =
                unit.GetComponent<CharMovement>();

            if (movement != null)
                movement.enabled = false;
        }

        StartNextTurn();
    }

    public void StartNextTurn()
    {
        if (units.Count == 0)
            return;

        // Check if either team has been eliminated.
        bool playersAlive = units.Any(
            u => u != null && !u.IsDefeated && u.isPlayer);

        bool enemiesAlive = units.Any(
            u => u != null && !u.IsDefeated && !u.isPlayer);

        if (!playersAlive || !enemiesAlive)
        {
            CurrentUnit = null;
            OnTurnChanged?.Invoke();
            Debug.Log("Combat is over!");
            return;
        }

        int checkedUnits = 0;

        do
        {
            currentIndex = (currentIndex + 1) % units.Count;
            CurrentUnit = units[currentIndex];
            checkedUnits++;
        }
        while (
            (CurrentUnit == null || CurrentUnit.IsDefeated)
            && checkedUnits < units.Count
        );

        if (CurrentUnit == null || CurrentUnit.IsDefeated)
            return;

        CurrentUnit.StartTurn();

        // Stop every agent, then enable only the active unit.
        foreach (BasicCombatant unit in units)
        {
            if (unit == null) continue;

            CharMovement movement =
                unit.GetComponent<CharMovement>();

            if (movement != null)
                movement.enabled = false;

            NavMeshAgent agent =
                unit.GetComponent<NavMeshAgent>();

            if (agent != null && agent.isOnNavMesh)
                agent.ResetPath();
        }

        if (CurrentUnit.isPlayer)
        {
            CharMovement activeMovement =
                CurrentUnit.GetComponent<CharMovement>();

            if (activeMovement != null)
                activeMovement.enabled = true;
        }

        Debug.Log("Current turn: " + CurrentUnit.unitName);
        OnTurnChanged?.Invoke();
    }

    public void EndTurn()
    {
        if (CurrentUnit == null)
            return;

        StartNextTurn();
    }

    public List<BasicCombatant> GetTurnOrder()
    {
        List<BasicCombatant> order = new List<BasicCombatant>();

        if (CurrentUnit == null)
            return order;

        for (int i = 0; i < units.Count; i++)
        {
            int index = (currentIndex + i) % units.Count;
            BasicCombatant unit = units[index];

            if (unit != null && !unit.IsDefeated)
                order.Add(unit);
        }

        return order;
    }
}
