using UnityEngine;

public class ViewInCameraFront : MonoBehaviour
{
    public enum TriggerEvent
    {
        None,
        GameObjectActive,
        CanvasGroupVisible,
    }

    public float distance = 0.5f;

    public Vector2 offset = new Vector2(0, -0.3f);

    public TriggerEvent triggerEvent;

    private bool activeLast;

    private bool canvasGroupStateLast;

    private CanvasGroup canvasGroup;

    private void Start()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        activeLast = true;
    }

    private void OnDisable()
    {
        activeLast = false;
    }

    private void Update()
    {
        if (triggerEvent == TriggerEvent.GameObjectActive)
            if (activeLast != gameObject.activeSelf)
            {
                activeLast = gameObject.activeSelf;
                AdjustKeyboardPos();
            }

        if (triggerEvent == TriggerEvent.CanvasGroupVisible)
            if (canvasGroupStateLast != canvasGroup.interactable)
            {
                canvasGroupStateLast = canvasGroup.interactable;
                AdjustKeyboardPos();
            }
    }

    private void AdjustKeyboardPos()
    {
        AdjustKeyboardPos(transform, distance, offset);
    }

    public static void AdjustKeyboardPos(Transform target, float distance, Vector2 offset)
    {
        var cam = Camera.main;
        var camTf = cam.transform;
        static Vector3 X0Z(Vector3 x) => new Vector3(x.x, 0, x.z);
        static Vector3 V2ToV3(Vector2 x) => new Vector3(x.x, x.y, 0);
        var targetPos = camTf.position + X0Z(camTf.forward).normalized * distance - V2ToV3(offset);
        target.position = targetPos;
    }
}
