using System;

public interface IHealthSource
{

    float Normalized { get; }
    bool IsDepleted { get; }


    event Action<float, float> Changed;


    event Action Depleted;
}
