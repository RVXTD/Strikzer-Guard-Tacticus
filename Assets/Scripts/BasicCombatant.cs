using UnityEngine;

public class BasicCombatant : MonoBehaviour
{
    public string unitName = "Unit";
    public bool isPlayer = true;
    public int speed = 10;
    public int health = 100;
    public int attackDamage = 25;

    public bool IsDefeated => health <= 0;
    public bool HasAttacked { get; private set; }

    public void StartTurn()
    {
        HasAttacked = false;
    }

    public void Attack(BasicCombatant target)
    {
        if (IsDefeated || HasAttacked ||
            target == null || target.IsDefeated ||
            target == this || target.isPlayer == isPlayer)
        {
            return;
        }

        target.health = Mathf.Max(
            0, target.health - attackDamage);

        HasAttacked = true;

        Debug.Log(unitName + " attacked " + target.unitName);

        if (target.IsDefeated)
        {
            Debug.Log(target.unitName + " was defeated!");
        }
    }
}