using UnityEngine;
namespace RuSignals;
public sealed class RoutePanel:MonoBehaviour
{
    public MeshFilter Surface;public bool Small;
    internal int Slot=-1;
    private ulong requested;
    public void Show(ulong mask){requested=mask;VisualScheduler.Register(this);ApplyPower(SignalPower.IsOut);}
    internal void ApplyPower(bool outage){if(Surface!=null)Surface.sharedMesh=SignalAssets.Current.PanelMesh(outage?0:requested,Small);}
    private void OnDestroy(){VisualScheduler.Unregister(this);}
}
