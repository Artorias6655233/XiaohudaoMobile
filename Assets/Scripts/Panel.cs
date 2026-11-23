using UnityEngine;

public class Panel : MonoBehaviour
{
    public virtual void Awake()
    {
        gameObject.AddComponent<UnityEngine.CanvasGroup>();
        var animator = gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("SessionPanel");
    }
}
