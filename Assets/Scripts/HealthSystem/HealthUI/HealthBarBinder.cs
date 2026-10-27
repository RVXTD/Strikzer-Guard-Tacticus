using UnityEngine;


[RequireComponent(typeof(HealthBarView))]
public class HealthBarBinder : MonoBehaviour
{
    [SerializeField] Health target;

    void Start() => GetComponent<HealthBarView>().Bind(target);
}
