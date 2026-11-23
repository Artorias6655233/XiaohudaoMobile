using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BNG {
    public class VRTextInputPro : MonoBehaviour {

        TMP_InputField thisInputField;

        public bool AttachToVRKeyboard = true;

        public bool ActivateKeyboardOnSelect = true;
        public bool DeactivateKeyboardOnDeselect = false;

        public VRKeyboardPro AttachedKeyboard;

        bool isFocused, wasFocused = false;

        public float distance = 0.5f;

        public Vector2 offset = new Vector2(0, -0.3f);

        void Awake() {
            thisInputField = GetComponent<TMP_InputField>();
            
            if(thisInputField) {
                AttachedKeyboard.AttachToInputField(thisInputField);
            }
        }

        void Update() {

            isFocused = thisInputField != null && thisInputField.isFocused;

            // Check if our input field is now focused
            if(isFocused == true && wasFocused == false) {
                OnInputSelect();
            }
            else if (isFocused == false && wasFocused == true) {
                OnInputDeselect();
            }
            
            wasFocused = isFocused;
        }

        public void OnInputSelect() {

            // Enable keyboard if disabled
            if (ActivateKeyboardOnSelect && AttachedKeyboard != null && !AttachedKeyboard.GetComponent<CanvasGroup>().interactable) {
                //thisInputField.selectionAnchorPosition = thisInputField.text.Length;
                //thisInputField.selectionFocusPosition = thisInputField.text.Length;
                thisInputField.caretPosition = thisInputField.text.Length;
                // Move keyboard in the front of camera.
                ViewInCameraFront.AdjustKeyboardPos(AttachedKeyboard.transform, distance, offset);
                //AttachedKeyboard.gameObject.SetActive(true);
                VRKeyboardPro.SetCGActive(AttachedKeyboard.GetComponent<CanvasGroup>(), true);
                AttachedKeyboard.AttachToInputField(thisInputField);
            }
        }

        public void OnInputDeselect() {
            if (DeactivateKeyboardOnDeselect && AttachedKeyboard != null && AttachedKeyboard.GetComponent<CanvasGroup>().interactable) {
                //AttachedKeyboard.gameObject.SetActive(false);
               VRKeyboardPro.SetCGActive(AttachedKeyboard.GetComponent<CanvasGroup>(), false);
            }
        }
    }
}


