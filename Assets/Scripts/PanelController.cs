using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class PanelController : MonoBehaviour
{
    private VideoPlayer videoPlayer;
    private RawImage rawImage;
    private IEnumerator Start()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        rawImage = GetComponent<RawImage>();
        videoPlayer.enabled = false;
        rawImage.enabled = false;
        yield return new WaitUntil(() => Session.Instance != null);
        Session.Instance.OnSessionTypeChanged += UpdatePanelVisibility;
        UpdatePanelVisibility();
    }

    public void UpdatePanelVisibility()
    {
        SessionType sessionType = Session.Instance.sessionType;
        Debug.Log("Updating panel visibility" + sessionType);
        if (sessionType != SessionType.None)
        {
            videoPlayer.enabled = true;
            videoPlayer.prepareCompleted += (source) =>
            {
                rawImage.texture = videoPlayer.texture;
                rawImage.enabled = true;
            };
        }
    }
}