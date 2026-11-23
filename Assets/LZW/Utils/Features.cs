using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Features : MonoBehaviour
{
    public static void ExitApp()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsEditor:
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                break;
            case RuntimePlatform.WindowsPlayer:
                Application.Quit();
                break;
            case RuntimePlatform.Android:
                Application.Quit();
                break;
            case RuntimePlatform.IPhonePlayer:
                Application.Quit();
                break;
        }
    }

    public static void RestartScene()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    public static void SetRigidbodyKinematic(Transform parent, bool value)
    {
        var _rigids = parent.GetComponentsInChildren<Rigidbody>();
        foreach (var rigid in _rigids)
        {
            rigid.isKinematic = value;
        }
    }

    public static Vector3 GetCameraDirection(Camera camera, float _forward, float _right)
    {
        var forward = camera.transform.forward;
        var right = camera.transform.right;
        var direction = forward * _forward + right * _right;
        return new Vector3(direction.x, 0, direction.z);
    }

    public static Vector3 GetCameraDirectionY(Camera camera, float _forward, float _right)
    {
        var forward = camera.transform.forward;
        var right = camera.transform.right;
        var direction = forward * _forward + right * _right;
        return direction;
    }

    public static Vector3 GetCameraDirectionXYZ(Camera camera, float _forward, float _right, float _up)
    {
        var forward = camera.transform.forward;
        var right = camera.transform.right;
        var up = camera.transform.up;
        var direction = forward * _forward + right * _right + up * _up;
        return direction;
    }

    public static Vector3 GetCameraForwardHorizontal(Camera camera, float _forwardDistance)
    {
        var forward = camera.transform.forward + camera.transform.position;
        var forwardHorizontal = new Vector3(forward.x,
            camera.transform.position.y, forward.z) * _forwardDistance;
        return forwardHorizontal;
    }

    /// <summary>
    /// Obtain the vector after the current vector has been rotated at a certain angle. 
    /// </summary>
    /// <param name="position"></param>
    /// <param name="center"></param>
    /// <param name="axis"></param>
    /// <param name="angle"></param>
    /// <returns>targetPosition</returns>
    public static Vector3 RotateRound(Vector3 currentPosition, Vector3 center, Vector3 axis, float singleAngle)
    {
        return Quaternion.AngleAxis(singleAngle, axis) * (currentPosition - center) + center;
    }

    /// <summary>
    /// Look the panel view to target camera.
    /// </summary>
    /// <param name="panel"></param>
    /// <param name="camera"></param>
    public static void LookViewToCamera(Transform panel, Camera camera)
    {
        // The z axis of the panel faces away from the side that is rendered, therefore this code is actually looking away from the camera
        panel.LookAt(new Vector3(panel.position.x * 2 - camera.transform.position.x, panel.position.y * 2 - camera.transform.position.y, panel.position.z * 2 - camera.transform.position.z), Vector3.up);
    }

    public static bool HasTextComponent(GameObject gameObject)
    {
        bool ret = gameObject.GetComponent<TMPro.TMP_Text>() != null
            || gameObject.GetComponent<UnityEngine.UI.Text>() != null;
        return ret;
    }

    public static void SetText(GameObject gameObject, string content)
    {
        var tmpText = gameObject.GetComponent<TMPro.TMP_Text>();
        if (tmpText != null)
        {
            tmpText.text = content;
            return;
        }
        var text = gameObject.GetComponent<UnityEngine.UI.Text>();
        if (text != null)
        {
            text.text = content;
        }
    }

    public static string GetText(GameObject gameObject)
    {
        var tmpText = gameObject.GetComponent<TMPro.TMP_Text>();
        if (tmpText != null)
        {
            return tmpText.text;
        }
        var text = gameObject.GetComponent<UnityEngine.UI.Text>();
        if (text != null)
        {
            return text.text;
        }
        return null;
    }
}
