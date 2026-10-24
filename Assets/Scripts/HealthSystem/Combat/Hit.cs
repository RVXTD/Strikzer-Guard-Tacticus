using UnityEngine;

// Optional, for later. Delete this folder and Health/HealthUI/Demo still compile perfectly fine.
public class Hit : MonoBehaviour
{
    public enum Mode { Flat, FractionOfMax }

    [SerializeField] Mode mode = Mode.Flat;
    [SerializeField] float amount = 10f;

    void OnTriggerEnter(Collider other)
    {
        if (!other.TryGetComponent(out Health health)) return;

        if (mode == Mode.Flat) health.ChangeByAmount(-amount);
        else                   health.ChangeByFraction(-amount);
    }
}
