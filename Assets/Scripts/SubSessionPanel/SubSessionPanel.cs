using System.Collections;
using UnityEngine;

public class SubSessionPanel : Panel
{
    public virtual SubSessionType subSessionType { get; }

    public virtual IEnumerator Start()
    {
        yield return new WaitUntil(() => Session.Instance != null);
        Session.Instance.subSessionPanels[subSessionType] = this;
        gameObject.SetActive(false); // Hide the panel initially
        Session.Instance.BeforeSubSessionTypeChanged += BeforeSubSessionTypeChanged;
        Session.Instance.OnSubSessionTypeChanged += OnSubSessionTypeChanged;
    }

    public virtual void BeforeSubSessionTypeChanged()
    {
        Session.Instance.companyRoot.SetActive(false);
    }

    public virtual void OnSubSessionTypeChanged()
    {
        gameObject.SetActive(Session.Instance.subSessionType == subSessionType);
    }
}
