using UnityEngine;
using UnityEngine.UI;

public class HealthBarView : MonoBehaviour
{
    [SerializeField] Image fill;  
    [SerializeField] Image trailFill;  
    [SerializeField] float fillSpeed = 4f;
    [SerializeField] float trailDelay = 0.4f;
    [SerializeField] float trailSpeed = 1.5f;

    IHealthSource source;
    float target;
    float trailHoldUntil;

    public void Bind(IHealthSource newSource)
    {
        if (source != null) source.Changed -= OnChanged;
        source = newSource;
        if (source == null) return;

        source.Changed += OnChanged;
        target = source.Normalized;
        fill.fillAmount = target;
        if (trailFill) trailFill.fillAmount = target;
    }

    void OnChanged(float now, float previous)
    {
        target = now;
        if (now < previous) trailHoldUntil = Time.time + trailDelay;
    }

    void Update()
    {
        if (!fill) return;
        fill.fillAmount = Mathf.MoveTowards(fill.fillAmount, target, fillSpeed * Time.deltaTime);

        if (!trailFill) return;
        if (trailFill.fillAmount < target)
            trailFill.fillAmount = target; 
        else if (Time.time >= trailHoldUntil)
            trailFill.fillAmount = Mathf.MoveTowards(trailFill.fillAmount, target, trailSpeed * Time.deltaTime);
    }

    void OnDestroy()
    {
        if (source != null) source.Changed -= OnChanged;
    }
}
