using UnityEngine;
namespace RuSignals;
// Shared model variants retain source geometry and their own optical datums.
public sealed class ShieldView:MonoBehaviour
{
    public ClearanceView Placement;
    public MeshFilter[] Filters;public Renderer[] Renderers;
    public Mesh[] Full,Compact,Tunnel,OvalFull,OvalCompact,OvalTunnel;
    public Material[] OriginalMaterials,OvalMaterials;
    public Transform[] Glares;public Vector3[] SquareGlares,OvalGlares;
    public Transform Label;public Vector3 SquareLabel,OvalLabel;
    public bool IsOval{get;private set;}
    internal void Bind(Signals.Game.Controllers.BasicSignalController controller)=>SetOval(!ShieldRegions.Square(controller));
    public void SetOval(bool oval){IsOval=oval;Apply();}
    internal void Apply()
    {
        bool tunnel=Placement!=null&&Placement.IsWallMounted,compact=Placement!=null&&Placement.IsCompact;
        var meshes=IsOval?(tunnel?OvalTunnel:compact?OvalCompact:OvalFull):(tunnel?Tunnel:compact?Compact:Full);
        for(int i=0;i<Filters.Length;i++){Filters[i].sharedMesh=meshes[i];Renderers[i].sharedMaterial=IsOval?OvalMaterials[i]:OriginalMaterials[i];}
        if(Glares!=null)for(int i=0;i<Glares.Length;i++)Glares[i].localPosition=IsOval?OvalGlares[i]:SquareGlares[i];
        if(Label!=null)Label.localPosition=IsOval?OvalLabel:SquareLabel;
    }
}
