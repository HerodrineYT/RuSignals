using System;
using System.Collections.Generic;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Generation;
using Signals.Game.Railway;
using UnityEngine;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
internal sealed partial class BlockNetwork
{
    private static bool Eligible(RailTrack track)=>track!=null&&!track.isJunctionTrack&&!track.name.StartsWith("[Y]",StringComparison.Ordinal)&&!track.name.StartsWith("[#]",StringComparison.Ordinal)&&!SignalPlacer.IsLogicYardTrack(track)&&!TurntableHelper.IsTurntableEnd(track)&&track.GetKinkedPointSet().span>1;
    internal void Generate()
    {
        if(Ready||SignalManager.CurrentPack?.ModId!=Mod.Id)return;
        var originals=new List<BasicSignalController>(SignalManager.Instance.AllControllers).ToArray();
        var used=new HashSet<RailTrack>();
        foreach(var rail in RailTrackRegistryBase.RailTracks)
        {
            if(!Eligible(rail)||used.Contains(rail))continue;
            for(int side=0;side<2;side++)
            {
                var direction=side==0?TrackDirection.Out:TrackDirection.In;
                var previous=TrackWalker.WalkTracks(rail,AutomaticBlock.TravelDirection(direction),1);
                if(previous.Count>0&&Eligible(previous[0].Track))continue;
                var path=new List<RailSpan>();var visited=new HashSet<RailTrack>();var track=rail;var heading=direction;
                while(Eligible(track)&&visited.Add(track)&&path.Count<64)
                {
                    path.Add(RailSpan.Whole(track,heading));var next=TrackWalker.WalkTracks(track,heading,1);
                    if(next.Count==0)break;track=next[0].Track;heading=next[0].Direction;
                }
                foreach(var span in path)used.Add(span.Track);
                if(path.Count==0)break;
                int before=Controllers.Count;PlaceDirection(path,originals);
                var reverse=new List<RailSpan>(path.Count);for(int i=path.Count-1;i>=0;i--)reverse.Add(path[i].Reverse());
                PlaceDirection(reverse,originals);
                if(Controllers.Count!=before)
                {
                    var corridor=new Corridor(path.ToArray());
                    foreach(var span in path)Tracks[span.Track]=(corridor,span.Direction);
                }
                break;
            }
        }
        var unique=new HashSet<Corridor>();foreach(var link in Tracks.Values)if(unique.Add(link.Path))Corridors.Add(link.Path);
        Ready=Controllers.Count>0;
        if(!Ready)return;
        foreach(var controller in originals)
            if(controller is DistantSignalController warning&&!warning.IsRepeater&&!SemaphoreWarnings.Home(warning.Home)&&warning.Home.PlacementInfo is SignalPlacementInfo p&&Tracks.ContainsKey(p.Track))NativeSdk.RemoveWarning(warning);
        RebuildAll();
        Debug.Log("[RuSignals] AB: "+Controllers.Count+" controllers / "+Corridors.Count+" corridors; 400-1000 m.");
    }
    internal static void RebuildAll()
    {
        var controllers=new List<BasicSignalController>(SignalManager.Instance.AllControllers);
        foreach(var controller in controllers)controller.FlagAllBlocksForUpdating();
        foreach(var controller in controllers)controller.UpdateBlocks();
    }
    private void PlaceDirection(List<RailSpan> path,BasicSignalController[] original)
    {
        var route=TrackBlock.CreateUntilMainSignal(path[0].Track,path[0].Direction);
        if(route.IsSelfLoop||route.NextController==null||!Train(route.NextController.GetControllerSignal()))return;
        double total=0;foreach(var span in path)total+=span.Length;
        if(BlockMath.Boundaries(total).Length==0)return;
        var anchors=new SortedSet<double>{0,total};var existing=new Dictionary<BasicSignalController,double>();
        foreach(var controller in original)
        {
            if(!Train(controller.GetControllerSignal())||!controller.PlacementInfo.HasValue)continue;
            var placement=controller.PlacementInfo.Value;double offset=0;
            foreach(var span in path)
            {
                if(span.Track==placement.Track&&span.Direction==AutomaticBlock.TravelDirection(placement.Direction))
                {double distance=offset+Math.Abs(placement.Span-span.Start);anchors.Add(distance);existing[controller]=distance;}
                offset+=span.Length;
            }
        }
        double last=0;bool first=true;
        foreach(double end in anchors)
        {
            if(first){last=end;first=false;continue;}
            double length=end-last;var positions=BlockMath.Boundaries(length);
            if(positions.Length==0){last=end;continue;}
            double slack=BlockMath.MovementAllowance(positions[0],length-positions[positions.Length-1]);
            foreach(var anchor in existing)
                if(Math.Abs(anchor.Value-last)<.01||Math.Abs(anchor.Value-end)<.01)Limit(anchor.Key,anchor.Key.PlacementInfo.Value.Span,slack);
            for(int i=0;i<positions.Length;i++)
            {
                double along=last+positions[i];
                foreach(var span in path)
                {
                    if(along>span.Length){along-=span.Length;continue;}
                    var set=span.Track.GetKinkedPointSet();int pointIndex=set.GetPointIndexForSpan(span.Start+(span.Direction==TrackDirection.Out?along:-along));
                    var point=set.points[pointIndex];var placement=new SignalPlacementInfo(span.Track,AutomaticBlock.TravelDirection(span.Direction),pointIndex,point.span);
                    var definition=SignalPlacer.InstantiateFromDef(SignalManager.CurrentPack.Signal,point.position,span.Direction==TrackDirection.Out?-point.forward:point.forward,false,span.Track);
                    definition.gameObject.SetActive(true);var controller=new AutoController(definition,placement);
                    Controllers.Add(controller);controller.NameOverride="АБ-"+Controllers.Count;
                    foreach(var signal in controller.Signals){signal.NameOverride=controller.NameOverride;signal.AllowReserving=false;}
                    double left=positions[i]-(i==0?0:positions[i-1]),right=(i+1==positions.Length?length:positions[i+1])-positions[i];
                    Limit(controller,point.span,BlockMath.MovementAllowance(left,right));
                    var key=new TrackInfo(span.Track,span.Direction);
                    if(!Cuts.TryGetValue(key,out var list)){list=new List<BasicSignalController>();Cuts.Add(key,list);}list.Add(controller);
                    break;
                }
            }
            last=end;
        }
    }
    private void Limit(BasicSignalController controller,double position,double slack)
    {
        var range=(Min:position-slack,Max:position+slack);
        if(MoveLimits.TryGetValue(controller,out var old))range=(Math.Max(old.Min,range.Min),Math.Min(old.Max,range.Max));
        MoveLimits[controller]=range;
    }
    internal void LimitMovement(BasicSignalController controller,ref int offset)
    {
        if(!controller.PlacementInfo.HasValue||!MoveLimits.TryGetValue(controller,out var limit))return;
        var p=controller.PlacementInfo.Value;var points=p.Track.GetKinkedPointSet().points;
        int wanted=Math.Max(0,Math.Min(points.Length-1,p.PointIndex+offset));
        while(wanted>p.PointIndex&&points[wanted].span>limit.Max)wanted--;
        while(wanted<p.PointIndex&&points[wanted].span<limit.Min)wanted++;
        offset=wanted-p.PointIndex;
    }
}
