using System;
namespace RuSignals;
public static class LampVisualRules
{
    public static float Emission(bool blue)=>blue?3f:RouteBoardRules.MainLampEmission;
    public static float Glare(bool blue)=>blue?.8f:1.15f;
    public static float Intensity(bool blue)=>1.4f;
    public const float Range=4f,SpotAngle=55f;
    public const float SpillStart=100f,SpillFull=30f;
    public static float SpillGain(float distance)
    {
        if(float.IsNaN(distance)||distance>=SpillStart)return 0;
        if(distance<=SpillFull)return 1;
        float t=(SpillStart-distance)/(SpillStart-SpillFull);
        return t*t*(3-2*t);
    }
}
public static class RouteBoardRules
{
    public const float Emission=.45f,MainLampEmission=1.8f,MountY=7.105f,MountZ=-.5f,Top=7.575f;
    public const float HeadMargin=.55f,HeadGap=.10f,CompactScale=.65f,CompactLowestLens=1.65f;
    public static bool HasHousing(SignalRole role,bool dwarf)=>!dwarf&&(role==SignalRole.Entry||role==SignalRole.Exit||role==SignalRole.Route);
    public static bool Enabled(SignalRole role,bool dwarf,bool shunting,bool junction)=>HasHousing(role,dwarf)&&!shunting&&(junction||role==SignalRole.Entry);
    public static bool ModelHasBoard(string key)=>key=="entry"||key=="junction"||key=="exit"||key=="exit_mainline";
    public static float LampX(float x)=>x;
    public static float OffsetY(float lens,float lower,float scale)=>lens-lower-HeadMargin-HeadGap-Top*scale;
    public static float OffsetZ(float scale)=>-MountZ*scale;
}
public static class ClearanceRules
{
    public const float FoundationTop=.25f,CompactTopAboveRail=3.15f,LabelScale=.8f;
    public static float Lowering(float top,float lens,float depth,bool routeBoard=false)=>Math.Max(0,Math.Min(top-depth-CompactTopAboveRail,lens-depth-(routeBoard?RouteBoardRules.CompactLowestLens:.85f)));
    public static float ShaftScale(float head,float lower)=>1-lower/(head-FoundationTop);
    public static float Height(float y,float head,float lower)=>y<=FoundationTop?y:y>=head?y-lower:FoundationTop+(y-FoundationTop)*ShaftScale(head,lower);
}
public static class TunnelMountRules
{
    public const float WallGap=.14f,MaxOutwardShift=1.2f,SideProbe=4.5f,RoofProbe=12f,TrackClearance=1.55f;
    public const float PlateThickness=.08f,PlateEmbed=.02f,TubeThickness=.07f;
    public static bool IsHeadPart(string name)=>!name.StartsWith("Body_",StringComparison.Ordinal)||name.EndsWith("_Signal",StringComparison.Ordinal);
    public static bool Fit(float original,float wall,float halfWidth,out float shift)
    {
        shift=0;
        if(float.IsNaN(wall)||float.IsInfinity(wall)||wall<=0||original<=0||halfWidth<=0)return false;
        float available=wall-halfWidth-WallGap;
        // A wall can be slightly inside the original mast envelope. Move the
        // head inward only while retaining the track clearance, never into rock.
        // Wide original shields (the diamond repeater) already use more of
        // the native offset. Never reduce their original inner clearance.
        float minimum=Math.Min(TrackClearance,original-halfWidth);
        if(minimum<=0||available-halfWidth<minimum)return false;
        shift=Math.Min(available-original,MaxOutwardShift);return true;
    }
}
