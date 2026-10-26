using UnityEngine;
namespace RuSignals;
// Uses the same budgeted scheduler as mast fitting; no per-signal Update.
public sealed class TunnelMount:MonoBehaviour
{
    public Transform SignalRoot,Console,Spine,UpperArm,LowerArm,Brace,LowerBrace,UpperPlate,LowerPlate,UpperBolts,LowerBolts,PanelNeck;
    public Transform[] HeadNecks;public Vector3[] HeadFixings;
    public float PanelCenter,PanelRear,LabelBottom;
    public Vector3 OriginalPosition;
    public Bounds Envelope,Heads;
    public Bounds[] Volumes;
    public bool Mounted{get;private set;}
    internal struct Layout
    {
        internal float Shift,Rise;
        internal Vector3 Upper,Lower,UpperNormal,LowerNormal;
    }
    private Layout applied;
    internal bool TryFit(Vector3 center,float offset,out Layout layout)
    {
        layout=default;
        if(!SceneProbe.Nearest(center+Vector3.up*1.2f,Vector3.up,TunnelMountRules.RoofProbe,transform,out _,true))return false;
        var outward=transform.right*Mathf.Sign(offset);
        // An isolated rock wall outdoors is not a tunnel. Do not reach across
        // another track to a distant outer wall in a wide, multiple-track bore.
        if(!SceneProbe.Nearest(center+Vector3.up*2,-outward,TunnelMountRules.RoofProbe,transform,out _,true))return false;
        // Empty space below a wide head is not part of its housing. Probe the
        // actual head, board and narrow plate envelopes separately. A small
        // rigid rise can keep a low plate above the curved bore's lower flank.
        for(int step=0;step<=4;step++)
            if(FitAtHeight(center,outward,offset,step*.15f,out layout))return true;
        return false;
    }
    private bool FitAtHeight(Vector3 center,Vector3 outward,float offset,float rise,out Layout layout)
    {
        layout=default;if(Volumes==null||Volumes.Length==0)return false;
        float shift=TunnelMountRules.MaxOutwardShift,minimum=float.NegativeInfinity;
        foreach(var volume in Volumes)
        {
            float wall=float.PositiveInfinity;
            for(int y=0;y<3;y++)for(int z=0;z<3;z++)
            {
                var local=new Vector3(0,Mathf.Lerp(volume.min.y,volume.max.y,y*.5f)+rise,Mathf.Lerp(volume.min.z,volume.max.z,z*.5f));
                if(!SceneProbe.Nearest(center+transform.TransformVector(local),outward,TunnelMountRules.SideProbe,transform,out var hit,true,.2f))return false;
                wall=Mathf.Min(wall,hit.distance);
            }
            float halfWidth=Mathf.Max(Mathf.Abs(volume.min.x),Mathf.Abs(volume.max.x));
            if(!TunnelMountRules.Fit(Mathf.Abs(offset),wall,halfWidth,out float available))return false;
            shift=Mathf.Min(shift,available);
            minimum=Mathf.Max(minimum,Mathf.Min(TunnelMountRules.TrackClearance,Mathf.Abs(offset)-halfWidth)+halfWidth-Mathf.Abs(offset));
        }
        if(shift<minimum-.0001f)return false;
        layout.Shift=shift*Mathf.Sign(offset);
        layout.Rise=rise;
        // Check the entire housing/indicator envelope, including rock between
        // the sample rays. Retreat towards the original position if needed.
        if(!Clear(layout.Shift,rise))
        {
            if(shift<=minimum||!Clear(minimum*Mathf.Sign(offset),rise))return false;
            float low=minimum,high=shift;
            for(int i=0;i<5;i++){float middle=(low+high)*.5f;if(Clear(middle*Mathf.Sign(offset),rise))low=middle;else high=middle;}
            layout.Shift=low*Mathf.Sign(offset);
        }
        // Two anchors at the actual surface support the stack from behind.
        MountingSpan(out float top,out float bottom);float back=Envelope.min.z-.10f;
        if(!Anchor(center,outward,new Vector3(0,top+rise,back),out layout.Upper,out layout.UpperNormal)||
           !Anchor(center,outward,new Vector3(0,bottom+rise,back),out layout.Lower,out layout.LowerNormal))return false;
        return true;
    }
    private bool Clear(float shift,float rise)
    {
        foreach(var volume in Volumes)
            if(!SceneProbe.ClearBox(transform.TransformPoint(volume.center+Vector3.right*shift+Vector3.up*rise),volume.extents+Vector3.one*(TunnelMountRules.WallGap*.65f),transform.rotation,transform))return false;
        return true;
    }
    private bool Anchor(Vector3 center,Vector3 outward,Vector3 local,out Vector3 point,out Vector3 normal)
    {
        point=normal=default;
        if(!SceneProbe.Nearest(center+transform.TransformVector(local),outward,TunnelMountRules.SideProbe,transform,out var hit,true,.2f))return false;
        point=transform.InverseTransformPoint(hit.point-hit.normal*TunnelMountRules.PlateEmbed);
        normal=transform.InverseTransformDirection(hit.normal);return true;
    }
    internal void Apply(bool mounted,Layout layout)
    {
        if(!mounted)
        {
            if(!Mounted)return;
            Mounted=false;SignalRoot.localPosition=OriginalPosition;Console.gameObject.SetActive(false);return;
        }
        if(Mounted&&Mathf.Abs(applied.Shift-layout.Shift)<.005f&&Mathf.Abs(applied.Rise-layout.Rise)<.005f&&
           (applied.Upper-layout.Upper).sqrMagnitude<.000025f&&(applied.Lower-layout.Lower).sqrMagnitude<.000025f&&
           (applied.UpperNormal-layout.UpperNormal).sqrMagnitude<.000025f&&(applied.LowerNormal-layout.LowerNormal).sqrMagnitude<.000025f)return;
        Mounted=true;applied=layout;SignalRoot.localPosition=OriginalPosition+Vector3.right*layout.Shift+Vector3.up*layout.Rise;
        MountingSpan(out float top,out float bottom);top+=layout.Rise;bottom+=layout.Rise;
        float back=Envelope.min.z-.10f;var upper=new Vector3(layout.Shift,top,back);var lower=new Vector3(layout.Shift,bottom,back);
        Bar(Spine,lower-Vector3.up*.09f,upper+Vector3.up*.09f);
        Bar(UpperArm,upper,layout.Upper);Bar(LowerArm,lower,layout.Lower);
        // Brace each wall arm locally; a diagonal across the entire stack
        // obscures the heads and resembles a leftover ladder.
        float bracing=Mathf.Min(.25f,(top-bottom)*.4f);
        Bar(Brace,upper-Vector3.up*bracing,layout.Upper);Bar(LowerBrace,lower+Vector3.up*bracing,layout.Lower);
        for(int i=0;i<HeadNecks.Length;i++)
        {
            var fixing=HeadFixings[i]+Vector3.right*layout.Shift+Vector3.up*layout.Rise;Bar(HeadNecks[i],new Vector3(layout.Shift,fixing.y,back),fixing);
        }
        if(PanelNeck!=null)Bar(PanelNeck,new Vector3(layout.Shift,PanelCenter+layout.Rise,back),new Vector3(layout.Shift,PanelCenter+layout.Rise,PanelRear));
        Plate(UpperPlate,layout.Upper,layout.UpperNormal);Plate(LowerPlate,layout.Lower,layout.LowerNormal);
        BoltPose(UpperBolts,UpperPlate);BoltPose(LowerBolts,LowerPlate);
        Console.gameObject.SetActive(true);
    }
    private void MountingSpan(out float top,out float bottom)
    {
        top=float.NegativeInfinity;bottom=float.PositiveInfinity;
        foreach(var fixing in HeadFixings){top=Mathf.Max(top,fixing.y);bottom=Mathf.Min(bottom,fixing.y);}
        // A single lens still needs two spaced wall anchors.
        if(top-bottom<.25f){top+=.15f;bottom-=.15f;}
        if(PanelNeck!=null)bottom=Mathf.Min(bottom,PanelCenter);
        bottom=Mathf.Min(bottom,LabelBottom);
        bottom=Mathf.Max(.20f,bottom-.20f);
    }
    private static void Bar(Transform bar,Vector3 a,Vector3 b)
    {
        var direction=b-a;bar.localPosition=(a+b)*.5f;
        bar.localRotation=Quaternion.LookRotation(direction,Mathf.Abs(Vector3.Dot(direction.normalized,Vector3.up))>.95f?Vector3.forward:Vector3.up);
        bar.localScale=new Vector3(TunnelMountRules.TubeThickness,TunnelMountRules.TubeThickness,direction.magnitude);
    }
    private static void Plate(Transform plate,Vector3 point,Vector3 normal)
    {
        plate.localPosition=point;plate.localRotation=Quaternion.FromToRotation(Vector3.right,normal);
        plate.localScale=new Vector3(TunnelMountRules.PlateThickness,.34f,.30f);
    }
    private static void BoltPose(Transform bolts,Transform plate)
    {
        if(bolts==null)return;bolts.localPosition=plate.localPosition;bolts.localRotation=plate.localRotation;bolts.localScale=Vector3.one;
    }
}
