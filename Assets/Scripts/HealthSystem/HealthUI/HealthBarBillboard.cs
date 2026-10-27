using UnityEngine;


public class HealthBarBillboard : MonoBehaviour
{
    Camera cam;

    void LateUpdate()
    {
        if (!cam) cam = Camera.main;
        if (cam) transform.rotation = cam.transform.rotation;
    }
}
