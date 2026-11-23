using BNG;
using UnityEngine;

/// <summary>
/// 「选中危险源后按一下键就能批注」的手柄快捷键。
///
/// 【为什么是 X 不是 B】B 键已经被 SandTableMainController 占了（按下会把沙盘菜单
/// 瞬移到相机前 6 米）。两边都是 Update 里轮询 InputBridge，谁也拦不住谁，按一下 B
/// 会同时弹批注面板 + 挪走菜单。X / Y / Start / Back 四个键全项目没人用，取 X。
/// </summary>
public sealed class P0AnnotationShortcut : MonoBehaviour
{
    public P0AnnotationPanel annotationPanel;

    private void Update()
    {
        if (annotationPanel == null || annotationPanel.IsOpen || annotationPanel.IsEditingInput) return;
        if (Session.Instance == null || InputBridge.Instance == null) return;
        if (Session.Instance.subSessionType != SubSessionType.MajorHazard_CompanyManagement ||
            string.IsNullOrEmpty(Session.Instance.hazardSourceID) ||
            !InputBridge.Instance.XButtonDown) return;
        annotationPanel.Open(Session.Instance.hazardSourceID);
    }
}
