using UnityEngine;


public class HealthDemoPanel : MonoBehaviour
{
    [SerializeField] Health player;
    [SerializeField] Health enemy;

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 260, 400), GUI.skin.box);
        DrawControls("PLAYER", player);
        GUILayout.Space(12);
        DrawControls("ENEMY", enemy);
        GUILayout.EndArea();
    }

    void DrawControls(string label, Health h)
    {
        if (!h) return;
        GUILayout.Label($"{label}  ({h.Normalized:P0})");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-10 flat")) h.ChangeByAmount(-10f);
        if (GUILayout.Button("-25%"))     h.ChangeByFraction(-0.25f);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+10 flat")) h.ChangeByAmount(10f);
        if (GUILayout.Button("+25%"))     h.ChangeByFraction(0.25f);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Full"))     h.SetNormalized(1f);
        if (GUILayout.Button("Kill"))     h.SetNormalized(0f);
        GUILayout.EndHorizontal();
    }
}
