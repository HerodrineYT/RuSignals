using System;
using System.Collections.Generic;
using Signals.Common;
using Signals.Game;
using UnityEngine;
namespace RuSignals;
using Signal=global::Signals.Game.Signal;
// Route-local limits for optional cab equipment. Indexed once after a native
// mutation; track queries never scan the complete signal registry.
public static class SignalSpeedLimits
{
    public readonly struct Span
    {
        public readonly double Start,End;public readonly int Direction,Limit;
        internal Span(double start,double end,int direction,int limit){Start=start;End=end;Direction=direction;Limit=limit;}
        public bool Contains(double position)=>(position-Start)*Direction>=-.0001&&(End-position)*Direction>.0001;
    }
    private static readonly Dictionary<RailTrack,List<Span>> spans=new Dictionary<RailTrack,List<Span>>();
    private static bool dirty=true;
    internal static void Invalidate(){dirty=true;}
    internal static void Reset(){spans.Clear();dirty=true;}
    public static IReadOnlyList<Span> OnTrack(RailTrack track)
    {
        if(dirty)Rebuild();return track!=null&&spans.TryGetValue(track,out var found)?found:Array.Empty<Span>();
    }
    private static void Rebuild()
    {
        spans.Clear();dirty=false;if(SignalManager.Instance==null)return;
        foreach(var controller in SignalManager.Instance.AllControllers)
        {
            if(!controller.Exists||!(controller.PlacementInfo is SignalPlacementInfo placement))continue;
            foreach(var signal in controller.Signals)
            {
                if(signal.IsOff||signal.IsShunting||!(signal.CurrentAspect?.GetDefinition() is RuAspectDefinition definition)||definition.Role>SignalRole.Route)continue;
                var block=signal.Block;
                if(block==null||block.ShouldBeUpdated||block.IsSelfLoop||!SignalReader.Aligned(controller,block))continue;
                bool invitation=definition.Indication==Indication.Invitation;
                if(invitation&&(!Signals.Game.Railway.TrackReserver.HasReservation(signal)||Signals.Game.Railway.TrackReserver.IsSignalReservedByAnother(signal)))continue;
                int limit=SignalRules.IsTrainPermission(definition.Indication)||invitation?(int)SignalRules.PassingSpeed(definition.Indication):0;
                // Occupation closes a signal behind the train. The physical
                // turnout limit remains in force until its whole route clears.
                if(limit==0&&definition.Role!=SignalRole.Through&&definition.Indication!=Indication.ShuntingWhite)
                {
                    if(definition.Role==SignalRole.Entry&&block.IsDeadEnd&&block.NextController==null)limit=40;
                    foreach(var info in block.Tracks)if(info.IsJunctionTrack&&JunctionRouteRules.Diverging(info.Track.name)){limit=40;break;}
                }
                if(limit<=0)continue;
                var end=block.NextController?.PlacementInfo;
                foreach(var info in block.Tracks)
                {
                    var track=info.Track;if(track==null)continue;var set=track.GetKinkedPointSet();if(set==null||set.span<=0)continue;
                    int direction=info.Direction==TrackDirection.Out?1:-1;
                    double start=direction>0?0:set.span,finish=direction>0?set.span:0;
                    if(track==placement.Track&&info.Direction==AutomaticBlock.TravelDirection(placement.Direction))start=placement.Span;
                    if(end.HasValue&&track==end.Value.Track&&info.Direction==AutomaticBlock.TravelDirection(end.Value.Direction))finish=end.Value.Span;
                    if((finish-start)*direction<=.0001)continue;
                    if(!spans.TryGetValue(track,out var values))spans.Add(track,values=new List<Span>());
                    values.Add(new Span(start,finish,direction,limit));
                }
            }
        }
    }
}
