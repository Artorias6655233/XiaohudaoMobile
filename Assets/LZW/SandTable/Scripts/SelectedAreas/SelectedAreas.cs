using System;
using System.Linq;
using BNG;
using Mirror;
using UnityEngine;

public class SelectedAreas : MonoBehaviour
{
    public Transform target;
    
    public SelectedArea[] selectedAreas;

    public GameObject LocalSelectedObjectLast { get; set; }

    public SelectedArea SelectedArea { get; set; }
    
    public SelectedArea LockedArea { get; set; }
    
    public SelectedLaserPointer selectedLaserPointer;
    
    public CanvasPOI canvasPoi;

    private MirrorSyncManager mirrorSyncManager;

    [Header("The interval between two interactions.(ms)")]
    public int interactionInterval = 200;
    private float interactionTick = -1000;

    // Start is called before the first frame update
    private void Start()
    {
        canvasPoi.SetVisible(false);
        
        selectedAreas = FindObjectsOfType<SelectedArea>();

        selectedLaserPointer.onHit += OnHit;
        
        mirrorSyncManager = AvatarLoaderNetworkManager.Ins;
        mirrorSyncManager.onClientMessageReceived += OnClientMessageReceived;
    }

    private void OnClientMessageReceived(MirrorSyncMessageData arg0)
    {
        try
        {
            HitData hitData = JsonUtility.FromJson<HitData>(arg0.data);
            if (hitData.actionName == "SelectHit")
                OnHitRev(hitData);
        }
        catch (Exception e) { }
        
        try
        {
            TriggerData triggerData = JsonUtility.FromJson<TriggerData>(arg0.data);
            if (triggerData.actionName == "SelectTrigger")
                OnTriggerRev(triggerData);
        }
        catch (Exception e) { }
    }

    private void OnHit(GameObject arg0, GameObject arg1, bool arg2)
    {
        if (NetworkClient.active == false)
            return;

        //if (CanInteraction == false)
        //    return;

        if (LocalSelectedObjectLast == arg0)
            return;
        LocalSelectedObjectLast = arg0;

        var json = JsonUtility.ToJson(new HitData { actionName = "SelectHit", newObjectName = arg0 ? arg0.name : "", oldObjectName = arg1 ? arg1.name : "", selected = arg2});
        mirrorSyncManager.ClientSendToServer<MirrorSyncMessageData>(MirrorSyncMessageData.BuildData(json));
    }

    private void OnHitRev(HitData data)
    {
        var arg0 = selectedAreas.Select(x => x.gameObject).FirstOrDefault(x => x.name == data.newObjectName);
        var arg1 = selectedAreas.Select(x => x.gameObject).FirstOrDefault(x => x.name == data.oldObjectName);
        var arg2 = data.selected;
        
        if (arg2)
        {
            if (arg1 && !arg1.Equals(arg2) && !arg1.GetComponent<SelectedArea>().Equals(LockedArea))
                arg1.GetComponent<SelectedArea>().SetVisible(false);
            
            SelectedArea = arg0.GetComponent<SelectedArea>();
            SelectedArea.SetVisible(true);
        }
        else
        {
            if (arg0 && !arg0.GetComponent<SelectedArea>().Equals(LockedArea))
                arg0.GetComponent<SelectedArea>().SetVisible(false);

            if (arg1 && !arg1.GetComponent<SelectedArea>().Equals(LockedArea))
                arg1.GetComponent<SelectedArea>().SetVisible(false);
            
            SelectedArea = null;
        }
    }

    private void Update()
    {
        transform.SetPositionAndRotation(target.position, target.rotation);
        transform.localScale = Vector3.one * target.localScale.x * target.localScale.x;
        
        if (NetworkClient.active == false)
            return;
        
        if (InputBridge.Instance.RightTriggerDown || Input.GetKeyDown(KeyCode.O))
        {
            if (Time.time * 1000 - interactionTick > interactionInterval)
            {
                interactionTick = Time.time * 1000;
                var json = JsonUtility.ToJson(new TriggerData { actionName = "SelectTrigger", selectedAreaName = SelectedArea ? SelectedArea.name : "" });
                mirrorSyncManager.ClientSendToServer<MirrorSyncMessageData>(MirrorSyncMessageData.BuildData(json));
            }
        }
    }

    private void OnTriggerRev(TriggerData data)
    {
        var _SelectedArea = selectedAreas.FirstOrDefault(x => x.name == data.selectedAreaName);
        SelectedArea = _SelectedArea;
        
        if (SelectedArea)
        {
            if (LockedArea)
                LockedArea.SetVisible(false);
                
            LockedArea = SelectedArea;
            LockedArea.SetVisible(true);
                
            canvasPoi.SetText(LockedArea.targetIntroductionText);
            canvasPoi.SetVisible(true);
        }
        else
        {
            if (LockedArea)
                LockedArea.SetVisible(false);

            canvasPoi.SetText("");
            canvasPoi.SetVisible(false);
                
            LockedArea = null;
        }
    }
    
    [Serializable]
    public class HitData
    {
        public string actionName;
        public string newObjectName;
        public string oldObjectName;
        public bool selected;
    }
    
    [Serializable]
    public class TriggerData
    {
        public string actionName;
        public string selectedAreaName;
    }
}
