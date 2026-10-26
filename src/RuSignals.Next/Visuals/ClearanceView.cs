using Signals.Game.Controllers;
using UnityEngine;
namespace RuSignals;
public enum MastPlacementMode { Automatic,Compact,Original }
public sealed class ClearanceView:MonoBehaviour
{
    public MeshFilter[] Filters;public Mesh[] Full,Compact,Tunnel;
    public MeshFilter[] PanelFilters;public Mesh[] PanelFull,PanelTunnel;
    public Transform[] Glares;public Vector3[] GlarePositions;
    public BoxCollider Hover;public Vector3 HoverCenter;
    public Transform Panel;public Vector3 NormalPanel,CompactPanel;
    public Transform Label;public Vector3 NormalLabel,CompactLabel,WallLabel;
    public float Lowering,HeadBottom,Top,Depth;
    public MastPlacementMode Mode;
    public TunnelMount WallMount;
    public bool IsCompact{get;private set;}
    public bool IsWallMounted=>WallMount!=null&&WallMount.Mounted;
    private BasicSignalController controller;private float next;
    internal void Bind(BasicSignalController value){controller=value;next=Time.unscaledTime+1;VisualScheduler.Register(this);}
    internal void Reposition()
    {
        next=0;
        if(!IsWallMounted)return;
        // Native moving/flipping must never retain a displacement towards the
        // old wall. Withdraw immediately, then refit on the nearby budget.
        WallMount.Apply(false,default);
        for(int i=0;i<Filters.Length;i++)Filters[i].sharedMesh=IsCompact?Compact[i]:Full[i];
        PanelMeshes(false);
        GetComponent<ShieldView>()?.Apply();
    }
    internal void Poll(float now)
    {
        if(now<next)return;next=now+3;
        if(controller==null||PlayerManager.ActiveCamera==null||controller.GetCameraDistanceSqr()>360000)return;
        Refresh();
    }
    public void SetMode(MastPlacementMode mode){Mode=mode;Refresh();}
    private void Refresh()
    {
        var layout=default(TunnelMount.Layout);bool mounted=false;
        if(Mode!=MastPlacementMode.Original&&WallMount!=null&&Placement(out var center,out float offset))mounted=WallMount.TryFit(center,offset,out layout);
        bool compact=mounted||Mode==MastPlacementMode.Compact||(Mode==MastPlacementMode.Automatic&&Obstructed());
        bool changed=compact!=IsCompact||mounted!=IsWallMounted;
        WallMount?.Apply(mounted,layout);
        if(!changed)return;IsCompact=compact;
        for(int i=0;i<Filters.Length;i++)Filters[i].sharedMesh=mounted?Tunnel[i]:compact?Compact[i]:Full[i];
        PanelMeshes(mounted);
        GetComponent<ShieldView>()?.Apply();
        var displacement=compact?Vector3.down*Lowering:Vector3.zero;
        for(int i=0;i<Glares.Length;i++)Glares[i].localPosition=GlarePositions[i]+displacement;
        Hover.center=HoverCenter+displacement;
        if(Label!=null){Label.GetComponent<NameplateView>().SetVertical(!compact);Label.localPosition=mounted?WallLabel:compact?CompactLabel:NormalLabel;Label.localScale=Vector3.one*(compact?ClearanceRules.LabelScale:1);}
        if(Panel!=null){Panel.localPosition=compact?CompactPanel:NormalPanel;Panel.localRotation=Quaternion.identity;Panel.localScale=Vector3.one*(compact?RouteBoardRules.CompactScale:1);}
    }
    private void PanelMeshes(bool mounted){if(PanelFilters!=null)for(int i=0;i<PanelFilters.Length;i++)PanelFilters[i].sharedMesh=mounted?PanelTunnel[i]:PanelFull[i];}
    private bool Obstructed()
    {
        if(!Placement(out var center,out float offset))return false;float top=Top-Depth+.2f;
        if(Blocked(center+Vector3.up*1.2f,center+Vector3.up*top))return true;
        for(int i=0;i<3;i++){float h=Mathf.Lerp(HeadBottom-Depth,top,i*.5f);if(Blocked(center+Vector3.up*h,transform.position+Vector3.up*h+transform.right*Mathf.Sign(offset)*.35f))return true;}
        return false;
    }
    private bool Blocked(Vector3 from,Vector3 to)
    {
        var ray=to-from;return SceneProbe.Nearest(from,ray.normalized,ray.magnitude,transform,out _);
    }
    private bool Placement(out Vector3 center,out float offset)
    {
        center=default;offset=0;if(controller==null||!controller.PlacementInfo.HasValue)return false;
        offset=controller.Definition.Offset*(controller.PlacementInfo.Value.OppositeSide?-1:1);
        center=transform.position-transform.right*offset;return true;
    }
    private void OnDestroy(){VisualScheduler.Unregister(this);}
}
