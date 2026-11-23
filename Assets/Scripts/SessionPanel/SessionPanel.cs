using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Video;

public class SessionPanel : Panel
{
    public SessionType sessionType;
    public virtual IEnumerator Start()
    {
        yield return new WaitUntil(() => Session.Instance != null);
        gameObject.SetActive(false); // Hide the panel initially
        Session.Instance.sessionPanels[sessionType] = this;
        Session.Instance.BeforeSessionTypeChanged += BeforeSessionTypeChanged;
        Session.Instance.OnSessionTypeChanged += OnSessionTypeChanged;
    }

    private void BeforeSessionTypeChanged()
    {
        Session.Instance.subSessionType = SubSessionType.None;
    }

    public virtual void OnSessionTypeChanged()
    {
        gameObject.SetActive(Session.Instance.sessionType == sessionType);
    }
}