using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A simple laser pointer that draws a line with a dot at the end
/// </summary>
public class SelectedLaserPointer : MonoBehaviour {

    public float MaxRange = 25f;
    public LayerMask ValidLayers;
    public Transform LaserEnd;

    public bool Active = true;

    LineRenderer line;

    /// <summary>
    /// NewObject, OldObject, hit?
    /// </summary>
    public UnityAction<GameObject, GameObject, bool> onHit;

    private GameObject NewHitObject { get; set; }
    private  GameObject OldHitObject { get; set; }

    // Start is called before the first frame update
    private void Start() {
        line = GetComponent<LineRenderer>();
    }

    private void LateUpdate() {
        if(Active) {

            line.enabled = true;

            RaycastHit hit;
            if (Physics.Raycast(transform.position, transform.forward, out hit, MaxRange, ValidLayers, QueryTriggerInteraction.Ignore)) {
                line.useWorldSpace = true;
                line.SetPosition(0, transform.position);
                line.SetPosition(1, hit.point);

                // Add dot at line's end
                LaserEnd.gameObject.SetActive(true);
                LaserEnd.position = hit.point;
                LaserEnd.rotation = Quaternion.FromToRotation(Vector3.forward, hit.normal);

                OldHitObject = NewHitObject;
                NewHitObject = hit.collider.gameObject;
                onHit?.Invoke(NewHitObject, OldHitObject, true);
            }
            else {
                line.useWorldSpace = false;
                line.SetPosition(0, transform.localPosition);
                line.SetPosition(1, new Vector3(0, 0, MaxRange));
                LaserEnd.gameObject.SetActive(false);
                
                OldHitObject = NewHitObject;
                NewHitObject = null;
                onHit?.Invoke(NewHitObject, OldHitObject, false);
            }
        }
        else {
            LaserEnd.gameObject.SetActive(false);

            if (line) {
                line.enabled = false;
            }
            
            OldHitObject = NewHitObject;
            NewHitObject = null;
            onHit?.Invoke(NewHitObject, OldHitObject, false);
        }
    }
}
