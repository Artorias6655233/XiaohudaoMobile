using UnityEngine;

[DisallowMultipleComponent]
public class Tumbler : MonoBehaviour
{
    [SerializeField, Header("The Tumble shaking threshold.")]
    private float wobbleThreshold = 0.001f; // wobble threshold

    [Header("[Optional]")]
    public Transform followTarget;

    // Update is called once per frame
    private void LateUpdate()
    {
        WobbleAdjustment();
    }

    private void WobbleAdjustment()
    {
        Quaternion targetRotation = transform.rotation;
        Vector3 upVector = transform.up;
        if (followTarget)
        {
            transform.position = followTarget.position;
            targetRotation = followTarget.rotation;
            upVector = followTarget.up;
        }
        // Calc wobble angle.
        float angle = Vector3.Angle(transform.up, Vector3.up);
        if (angle > wobbleThreshold)
        {
            // Restore angle.
            transform.rotation = Quaternion.FromToRotation(upVector, Vector3.up) * targetRotation;
        }
    }
}

