using UnityEngine;

public class SxrHandLaser : MonoBehaviour
{
    public Vector3 runtimeEuler = new (45, 0, 0);

    private void Start()
    {
#if !UNITY_EDITOR
        transform.localEulerAngles = runtimeEuler;
#endif
    }
}
