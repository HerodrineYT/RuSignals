using System.Collections.Generic;
using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Generation;
using Signals.Game.Railway;
using Signals.Game.Util;
using UnityEngine;
namespace RuSignals;

// Keep native turntable controllers, event subscriptions and the one-second
// connection delay. Only placement and which approach they protect change.
internal static class TurntableApproaches
{
    private static readonly System.Reflection.FieldInfo Routine=AccessTools.Field(typeof(TurntableSignalController),"_routine");
    internal static bool Generate(SignalPack pack,List<TurntableSignalController> turntableSignals)
    {
        if(pack.ModId!=Mod.Id)return true;
        if(!pack.HasAnyTurntableSignal)return false;
        foreach(var table in TurntableHelper.TurntableTracks)
        {
            // The native placer deliberately excludes the CS service table.
            if(table==null||table.uniqueID=="T-CS-1")continue;
            var prefab=pack.GetTurntableSignal(pack.EnableOldVersions&&OldAreaCalculator.IsWithinOldArea(table.visuals.position));
            if(prefab==null)continue;
            var seen=new HashSet<(RailTrack,bool)>();
            foreach(var end in table.trackEnds)
            {
                var track=end?.track;
                if(track==null||track==table.Track||!seen.Add((track,end.isFirst)))continue;
                var points=track.GetKinkedPointSet();if(points.span<.5)continue;
                double setback=System.Math.Min(5,points.span*.45);
                int index=points.GetPointIndexForSpan(end.isFirst?setback:points.span-setback);
                var point=points.points[index];var facing=end.isFirst?TrackDirection.Out:TrackDirection.In;
                var definition=SignalPlacer.InstantiateFromDef(prefab,point.position,end.isFirst?point.forward:-point.forward,false,track);
                definition.gameObject.SetActive(true);
                // InstantiateFromDef parents to the FIXED approach track.
                // Track is still the native rotating rail for connection events.
                var controller=new TurntableSignalController(definition,table,facing,new SignalPlacementInfo(track,facing,index,point.span));
                controller.NameOverride=table.uniqueID+"-"+seen.Count;
                foreach(var signal in controller.Signals){signal.NameOverride=controller.NameOverride;signal.AllowReserving=false;}
                controller.Update(true,false);
                turntableSignals.Add(controller);
            }
        }
        return false;
    }
    internal static bool Connection(TurntableSignalController __instance,ref RailTrack frontTrack,ref RailTrack backTrack)
    {
        var definition=__instance.Definition;
        if(definition==null||definition.Signals.Length==0||!(definition.Signals[0].Aspects[0] is RuAspectDefinition aspect)||aspect.Role!=SignalRole.Turntable)return true;
        if(!(__instance.PlacementInfo is SignalPlacementInfo placement)||placement.Track==__instance.Track.Track)return true;
        // Either end of the bridge can face this approach after a 180° turn.
        // A connection to a different approach must never open this signal.
        bool first=placement.Direction==TrackDirection.Out;
        var table=__instance.Track;
        bool Matches(TurntableRailTrack.TrackEnd end,RailTrack reported)=>end!=null&&reported==placement.Track&&end.track==placement.Track&&end.isFirst==first;
        bool aligned=Matches(table.frontClosest,frontTrack)||Matches(table.rearClosest,backTrack);
        // Native notifications concern both bridge ends. Keep an accepted or
        // pending connection when only the OTHER end has changed.
        if(aligned&&(__instance.IsConnected||Routine.GetValue(__instance)!=null))return false;
        frontTrack=backTrack=aligned?placement.Track:null;
        return true;
    }
    internal static void ConstrainMovement(BasicSignalController __instance,ref int pointIndexOffset)
    {
        if(!(__instance is TurntableSignalController table)||!(__instance.PlacementInfo is SignalPlacementInfo placement)||placement.Track==table.Track.Track)return;
        var definition=__instance.Definition;
        if(definition.Signals.Length==0||!(definition.Signals[0].Aspects[0] is RuAspectDefinition aspect)||aspect.Role!=SignalRole.Turntable)return;
        var points=placement.Track.GetKinkedPointSet();double margin=System.Math.Min(3,points.span*.3);
        int min=placement.Direction==TrackDirection.Out?points.GetPointIndexForSpan(margin):0;
        int max=placement.Direction==TrackDirection.In?points.GetPointIndexForSpan(points.span-margin):points.points.Length-1;
        long target=System.Math.Max(min,System.Math.Min(max,(long)placement.PointIndex+pointIndexOffset));
        pointIndexOffset=(int)target-placement.PointIndex;
    }
}
