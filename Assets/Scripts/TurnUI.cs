
using System.Text;
using TMPro;
using UnityEngine;

public class TurnUI : MonoBehaviour
{
    public TurnSystem turnManager;
    public TMP_Text activeTurnText;
    public TMP_Text turnQueueText;

    private void OnEnable()
    {
        if (turnManager != null)
            turnManager.OnTurnChanged += RefreshUI;
    }

    private void Start()
    {
        RefreshUI();
    }

    private void OnDisable()
    {
        if (turnManager != null)
            turnManager.OnTurnChanged -= RefreshUI;
    }

    public void RefreshUI()
    {
        if (turnManager == null)
            return;

        BasicCombatant current = turnManager.CurrentUnit;

        activeTurnText.text = current != null
            ? current.unitName + "'s Turn"
            : "Combat Over";

        StringBuilder text = new StringBuilder();

        foreach (BasicCombatant unit in turnManager.GetTurnOrder())
        {
            if (unit == current)
                text.Append("▶ ");
            else
                text.Append("   ");

            text.AppendLine(unit.unitName);
        }

        turnQueueText.text = text.ToString();
    }
}
