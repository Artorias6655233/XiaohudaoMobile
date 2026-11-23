using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Billboard : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        // Make the object always face the camera
        if (Camera.main != null)
        {
            transform.LookAt(Camera.main.transform);
            transform.rotation = transform.rotation * Quaternion.Euler(0, 180, 0); // Keep the rotation on the Y axis only
        }
    }
}
