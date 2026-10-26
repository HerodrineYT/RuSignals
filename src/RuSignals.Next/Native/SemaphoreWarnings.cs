using System;
using System.Collections.Generic;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Generation;
using Signals.Game.Railway;
using UnityEngine;
namespace RuSignals;

internal static class SemaphoreWarnings
{
    internal static bool Home(BasicSignalController controller)
    {
        if(controller?.Definition==null||controller.Definition.GetComponentInChildren<SemaphoreVisual>(true)==null)return false;
        var signal=controller.GetControllerSignal();
        return signal!=null&&signal.Definition.Aspects[0] is RuAspectDefinition aspect&&(aspect.Role==SignalRole.Through||aspect.Role==SignalRole.Entry||aspect.Role==SignalRole.Route);
    }
    internal static void Ensure()
    {
        if(SignalManager.CurrentPack?.ModId!=Mod.Id||SignalManager.Instance==null)return;
        var controllers=new List<BasicSignalController>(SignalManager.Instance.AllControllers);
        var covered=new HashSet<BasicSignalController>();
        foreach(var controller in controllers)if(controller is DistantSignalController warning&&!warning.IsRepeater)covered.Add(warning.Home);
        int added=0;
        foreach(var home in controllers)
        {
            if(!Home(home)||covered.Contains(home)||!TryPosition(home,controllers,out var placement,out float distance))continue;
            var prefab=SignalManager.CurrentPack.OldDistantSignal;if(prefab==null)continue;
            var point=placement.Track.GetKinkedPointSet().points[placement.PointIndex];
            var definition=SignalPlacer.InstantiateFromDef(prefab,point.position,placement.Direction==TrackDirection.Out?point.forward:-point.forward,false,placement.Track);
            definition.gameObject.SetActive(true);
            NativeSdk.AddWarning(new DistantSignalController(definition,home,placement,distance,false));covered.Add(home);added++;
        }
        if(added>0)Debug.Log("[RuSignals] Added "+added+" semaphore distant signals; native repeaters retained.");
    }
    internal static bool TryPosition(BasicSignalController home,List<BasicSignalController> controllers,out SignalPlacementInfo placement,out float distance)
    {
        placement=default;distance=0;if(!(home.PlacementInfo is SignalPlacementInfo start))return false;
        const double wanted=300;double remaining=wanted,span=start.Span;
        var track=start.Track;var direction=start.Direction;var visited=new HashSet<RailTrack>();
        while(track!=null&&visited.Add(track)&&visited.Count<=64)
        {
            // A warning is placed on the unbranched approach to its home.
            if(track.isJunctionTrack||SignalPlacer.IsLogicYardTrack(track)||TurntableHelper.IsTurntableEnd(track))return false;
            var points=track.GetKinkedPointSet();double available=direction==TrackDirection.Out?points.span-span:span;
            double end=span+(direction==TrackDirection.Out?1:-1)*Math.Min(available,remaining);
            foreach(var controller in controllers)
                if(controller!=home&&!(controller is DistantSignalController)&&controller.PlacementInfo is SignalPlacementInfo other&&other.Track==track&&other.Direction==direction&&controller.Signals.Length>0&&
                   other.Span>Math.Min(span,end)+.1&&other.Span<Math.Max(span,end)-.1)return false;
            if(available>=remaining)
            {
                int index=points.GetPointIndexForSpan(end);distance=(float)(wanted-remaining+Math.Abs(points.points[index].span-span));
                if(distance<290||distance>310)return false;
                placement=new SignalPlacementInfo(track,direction,index,points.points[index].span);return true;
            }
            remaining-=available;var next=TrackWalker.WalkTracks(track,direction,1);if(next.Count!=1)return false;
            track=next[0].Track;direction=next[0].Direction;span=direction==TrackDirection.Out?0:track.GetKinkedPointSet().span;
        }
        return false;
    }
}
