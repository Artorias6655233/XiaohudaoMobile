using TMPro;
using UnityEngine;

public class CanvasPOI : MonoBehaviour
{
    public Transform target;
    
    [Header("[Optional]")]
    public new Camera camera;

    public GameObject visiblePivot;
    
    public TMP_Text poiText;
    
    // Start is called before the first frame update
    private void Start()
    {
        if (camera == null)
            camera = Camera.main;
    }

    // Update is called once per frame
    private void Update()
    {
        transform.SetPositionAndRotation(target.position, target.rotation);
        LockViewToCamera();
    }

    private void LockViewToCamera()
    {
        transform.LookAt(camera.transform);
        transform.Rotate(Vector3.up * 180, Space.Self);
    }

    public void SetText(string text)
    {
        poiText.SetText(text);
    }
    
    public void SetVisible(bool visible)
    {
        visiblePivot.SetActive(visible);
    }
    
}
