using System;
using UnityEngine;

public class Health : MonoBehaviour, IHealthSource
{
    [SerializeField] float maxHealth = 100f;
    float current;

    public float Normalized => maxHealth <= 0f ? 0f : current / maxHealth;
    public bool IsDepleted => current <= 0f;

    public event Action<float, float> Changed;
    public event Action Depleted;

    void Awake() => current = maxHealth;


    public void ChangeByAmount(float delta) => Set(current + delta);
    public void ChangeByFraction(float fractionOfMax) => Set(current + fractionOfMax * maxHealth);
    public void SetNormalized(float normalized) => Set(normalized * maxHealth);

    public void SetMax(float newMax, bool preserveFraction = true)
    {
        float frac = Normalized;
        maxHealth = Mathf.Max(0f, newMax);
        Set(preserveFraction ? frac * maxHealth : current);
    }

    void Set(float value)
    {
        float prev = Normalized;
        bool wasDepleted = IsDepleted;

        current = Mathf.Clamp(value, 0f, maxHealth);

        if (!Mathf.Approximately(prev, Normalized))
            Changed?.Invoke(Normalized, prev);

        if (!wasDepleted && IsDepleted)
            Depleted?.Invoke();
    }
}
