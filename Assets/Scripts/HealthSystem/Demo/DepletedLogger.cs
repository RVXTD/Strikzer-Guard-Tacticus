using UnityEngine;

[RequireComponent(typeof(Health))]
public class DepletedLogger : MonoBehaviour
{
    void Start() => GetComponent<Health>().Depleted += () => Debug.Log($"{name} depleted");
}
