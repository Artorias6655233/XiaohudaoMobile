using System;
using System.Collections;
using System.Collections.Generic;
using BNG;
using UnityEngine;
using UnityEngine.EventSystems;

public class HitController : MonoBehaviour
{
    public Transform trackingSpace;
    public static HitController Instance { get; private set; }
    public void Start()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public event Action<InputBridge, RaycastHit> OnRaycastHit;

    void Update()
    {
        var inputBridge = InputBridge.Instance;

        var pos = inputBridge.GetControllerLocalPosition(ControllerHand.Right);
        pos = trackingSpace.TransformPoint(pos);
        var rot = inputBridge.GetControllerLocalRotation(ControllerHand.Right);
        rot = trackingSpace.rotation * rot * Quaternion.Euler(45, 0, 0);

        // Check if the user is pointing at an object with a MeshRenderer
        RaycastHit hit;
        Ray ray = new Ray(pos, rot * Vector3.forward);
        if (Physics.Raycast(ray, out hit))
        {
            OnRaycastHit?.Invoke(inputBridge, hit);
        }
    }

}
