using System.Collections.Generic;
using BNG;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LZWModule
{

    /// <summary>
    /// A simple laser pointer that draws a line with a dot at the end
    /// </summary>
    public class HighLightModule : MonoBehaviour
    {

        public float MaxRange = 25f;
        public LayerMask ValidLayers;
        public Transform LaserEnd;

        public bool Active = true;

        LineRenderer line;

        public UnityAction<RaycastState, GameObject, GameObject> onRaycastChanged;

        private RaycastState raycastState;
        protected GameObject hitObject;

        // A variable to prevent loss high light when specific UI clicked.
        private bool preventLossHighLightState;
        [HideInInspector]
        public List<object> preventLossHighLightUIs = new List<object>();

        public InputBridge inputBridge
        {
            get
            {
                return FindObjectOfType<InputBridge>();
            }
        }

        // Start is called before the first frame update
        protected virtual void Start()
        {
            raycastState = RaycastState.Loss;

            line = GetComponent<LineRenderer>();
            onRaycastChanged += OnRaycastChanged;
        }

        protected virtual void LateUpdate()
        {
            if (preventLossHighLightState == true)
                return;

            RaycastHit hit;
            // 
#if UNITY_WEBGL || (UNITY_ANDROID && !USE_PICO)
            if (Application.isEditor || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                if (Input.GetMouseButtonDown(0) == false)
                    return;

                Camera camera = Camera.main;
                if (camera == null)
                    return;

                Vector3 mousePos = Input.mousePosition;
                Ray ray = camera.ScreenPointToRay(Input.mousePosition);

                if (Active && Physics.Raycast(ray, out hit, MaxRange, ValidLayers, QueryTriggerInteraction.Ignore))
                {
                    // INSERT >>>
                    GameObject _hitObject = hit.transform.gameObject;
                    if (raycastState == RaycastState.Loss || _hitObject.Equals(hitObject) == false)
                    {
                        GameObject oldObject = hitObject;
                        hitObject = _hitObject;
                        raycastState = RaycastState.Hit;
                        onRaycastChanged?.Invoke(RaycastState.Hit, oldObject, _hitObject);
                    }
                }
                else
                {
                    // INSERT >>>
                    if (raycastState == RaycastState.Hit)
                    {
                        raycastState = RaycastState.Loss;
                        onRaycastChanged?.Invoke(RaycastState.Loss, hitObject, null);
                    }
                }

                return;
            }
#elif UNITY_ANDROID && USE_PICO
            // Editor & Android/Pico
            if (Active && Physics.Raycast(transform.position, transform.forward, out hit, MaxRange, ValidLayers, QueryTriggerInteraction.Ignore))
            {
                line.enabled = true;

                line.useWorldSpace = true;
                line.SetPosition(0, transform.position);
                line.SetPosition(1, hit.point);

                // Add dot at line's end
                LaserEnd.gameObject.SetActive(true);
                LaserEnd.position = hit.point;
                LaserEnd.rotation = Quaternion.FromToRotation(Vector3.forward, hit.normal);
                if (inputBridge && (inputBridge.RightGripDown || (Application.isEditor && Input.GetKeyDown(KeyCode.Z))))
                {
                    // INSERT >>>
                    GameObject _hitObject = hit.transform.gameObject;
                    if (raycastState == RaycastState.Loss || _hitObject.Equals(hitObject) == false)
                    {
                        GameObject oldObject = hitObject;
                        hitObject = _hitObject;
                        raycastState = RaycastState.Hit;
                        onRaycastChanged?.Invoke(RaycastState.Hit, oldObject, _hitObject);
                    }
                }
            }
            else
            {
                LaserEnd.gameObject.SetActive(false);
                if (line)
                    line.enabled = false;
                line.useWorldSpace = false;
                //line.SetPosition(0, transform.localPosition);
                line.SetPosition(0, Vector3.zero);
                line.SetPosition(1, new Vector3(0, 0, MaxRange));
                LaserEnd.gameObject.SetActive(false);
                if (inputBridge && (inputBridge.RightGripDown || (Application.isEditor && Input.GetKeyDown(KeyCode.Z))))
                {
                    // INSERT >>>
                    if (raycastState == RaycastState.Hit)
                    {
                        raycastState = RaycastState.Loss;
                        onRaycastChanged?.Invoke(RaycastState.Loss, hitObject, null);
                    }
                }
            }
#endif
        }

        protected virtual void OnRaycastChanged(RaycastState raycastState, GameObject hitObjectOld, GameObject hitObjectNew)
        {
            Debug.LogWarning($"OnRaycastChanged: {raycastState}, {hitObjectOld?.name}, {hitObjectNew?.name}");
            if (raycastState == RaycastState.Hit)
            {
                SetHighLightState(hitObjectOld, false);
                SetHighLightState(hitObjectNew, true);
            }
            else if (raycastState == RaycastState.Loss)
            {
                if (hitObjectOld != null)
                {
                    SetHighLightState(hitObjectOld, false);
                }
            }
        }

        protected void SetHighLightState(GameObject gameObject, bool state)
        {
            if (gameObject)
                gameObject.layer = LayerMask.NameToLayer(state ? "SelectObj" : "Default");
        }

        protected virtual void SetPreventLossHighLightState(bool value)
        {
            preventLossHighLightState = value;
        }

        public virtual void AddPreventLossHighLightUIs<T>(T ui) where T : Image
        {
            EventTrigger eventTrigger = ui.GetComponent<EventTrigger>();
            if (eventTrigger == null)
                eventTrigger = ui.gameObject.AddComponent<EventTrigger>();

            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerEnter;
            entry.callback.AddListener(_ => {
                SetPreventLossHighLightState(true);
            });

            EventTrigger.Entry entry2 = new EventTrigger.Entry();
            entry2.eventID = EventTriggerType.PointerExit;
            entry2.callback.AddListener(_ => {
                SetPreventLossHighLightState(false);
            });

            eventTrigger.triggers.Add(entry);
            eventTrigger.triggers.Add(entry2);

            preventLossHighLightUIs.Add(ui);
        }

        public virtual void RemovePreventLossHighLightUIs<T>(T ui) where T : TMP_InputField
        {
            preventLossHighLightUIs.Remove(ui);
        }

        public enum RaycastState
        {
            Loss,
            Hit,
        }
    }
}
