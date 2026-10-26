using System;
using System.Collections.Generic;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
internal sealed partial class BlockNetwork
{
    internal bool Occupancy(TrackBlock block,CrossingCheckMode mode,bool ignoreLogic,out bool occupied)
    {
        occupied=false;
        if(block==null||!Windows.TryGetValue(block,out var window))return false;
        var manual=NativeSdk.ManualOccupancy;
        for(int i=0;i<window.Spans.Length;i++)
        {
            var span=window.Spans[i];
            if(ignoreLogic&&Signals.Game.Generation.SignalPlacer.IsLogicYardTrack(span.Track))continue;
            if(manual.Contains(span.Track)){occupied=true;return true;}
            foreach(var bogie in span.Track.BogiesOnTrack())
            {
                if(bogie==null||bogie.traveller==null){occupied=true;return true;}
                double position=bogie.traveller.Span;
                // A train waiting before this mast has not entered its block.
                // Retain the tail margin at the far end and at rail joins.
                if(double.IsNaN(position)||double.IsInfinity(position)||
                    (BlockMath.Contains(span.Start,span.End,position,4)&&
                    (i!=0||(position-span.Start)*(span.Direction==TrackDirection.Out?1:-1)>=0)))
                {occupied=true;return true;}
            }
            var cross=mode==CrossingCheckMode.Ignore?null:Crossing(span.Track,window);
            if(cross!=null&&
                (mode==CrossingCheckMode.WholeTrack?cross.TestTracks():mode==CrossingCheckMode.IntersectionOnly&&cross.TestIntersections())){occupied=true;return true;}
        }
        foreach(var track in block.ExtraTracks)
            if(!window.Primary.Contains(track)&&(!ignoreLogic||!Signals.Game.Generation.SignalPlacer.IsLogicYardTrack(track))&&TrackChecker.IsOccupied(track,mode)){occupied=true;break;}
        return true;
    }
    internal bool Contains(TrackBlock block,RailTrack track,double position,int direction,out bool contains)
    {
        contains=false;if(block==null||!Windows.TryGetValue(block,out var window))return false;
        if(!block.ShouldBeUpdated)foreach(var span in window.Spans)
            if(span.Track==track&&(span.Direction==TrackDirection.Out?1:-1)==direction&&BlockMath.Contains(span.Start,span.End,position)){contains=true;break;}
        return true;
    }
    internal bool Match(TrackBlock a,TrackBlock b,out bool same)
    {
        BlockWindow x=null,y=null;if(a!=null)Windows.TryGetValue(a,out x);if(b!=null)Windows.TryGetValue(b,out y);
        same=false;if(x==null&&y==null)return false;
        if(x==null||y==null||x.Spans.Length!=y.Spans.Length)return true;
        for(int i=0;i<x.Spans.Length;i++)
        {
            var p=x.Spans[i];var q=y.Spans[i];
            if(p.Track!=q.Track||p.Direction!=q.Direction||!(Math.Abs(p.Start-q.Start)<.001)||!(Math.Abs(p.End-q.End)<.001))return true;
        }
        same=true;return true;
    }
    private static bool Primary(TrackBlock block,BlockWindow window,RailTrack track)
    {
        if(window!=null)return window.Primary.Contains(track);
        foreach(var t in block.Tracks)if(t.Track==track)return true;
        return false;
    }
    internal bool Conflict(TrackBlock a,TrackBlock b)
    {
        if(a==null||b==null)return true;
        Windows.TryGetValue(a,out var x);Windows.TryGetValue(b,out var y);
        int ac=x?.Spans.Length??a.Tracks.Length,bc=y?.Spans.Length??b.Tracks.Length;
        for(int i=0;i<ac;i++)
        {
            var p=x==null?RailSpan.Whole(a.Tracks[i].Track,a.Tracks[i].Direction):x.Spans[i];
            for(int j=0;j<bc;j++)
            {
                var track=y==null?b.Tracks[j].Track:y.Spans[j].Track;
                if(track!=p.Track)continue;
                var q=y==null?RailSpan.Whole(track,b.Tracks[j].Direction):y.Spans[j];
                if(BlockMath.Overlaps(p.Start,p.End,q.Start,q.End))return true;
            }
        }
        foreach(var t in a.ExtraTracks)if(!Primary(a,x,t)&&b.AllTracks.Contains(t))return true;
        foreach(var t in b.ExtraTracks)if(!Primary(b,y,t)&&a.AllTracks.Contains(t))return true;
        if(Crossings!=null&&Crossings.Count>0)
        {
            var other=b.AllTracks;
            foreach(var t in a.AllTracks)
            {
                var cross=Crossing(t,x);if(cross==null)continue;
                foreach(var u in other)if(cross.HasIntersectionWithTrack(u))
                {
                    var reverse=Crossing(u,y);
                    if(reverse==null||reverse.HasIntersectionWithTrack(t))return true;
                }
            }
        }
        return false;
    }
    internal void Shorten(Signal signal,ref TrackBlock block)
    {
        if(block==null||block.Tracks.Length==0||!Train(signal)||!signal.Controller.PlacementInfo.HasValue||Windows.TryGetValue(block,out _))return;
        var p=signal.Controller.PlacementInfo.Value;var travel=AutomaticBlock.TravelDirection(p.Direction);
        var spans=new List<RailSpan>(block.Tracks.Length+1);int start=-1;
        for(int i=0;i<block.Tracks.Length;i++)if(block.Tracks[i].Track==p.Track&&block.Tracks[i].Direction==travel){start=i;break;}
        if(start<0){var full=RailSpan.Whole(p.Track,travel);spans.Add(new RailSpan(p.Track,travel,p.Span,full.End));start=0;}
        for(int i=start;i<block.Tracks.Length;i++)
        {
            var t=block.Tracks[i];var full=RailSpan.Whole(t.Track,t.Direction);
            spans.Add(new RailSpan(t.Track,t.Direction,i==start&&t.Track==p.Track&&t.Direction==travel?p.Span:full.Start,full.End));
        }
        var next=block.NextController;
        if(next?.PlacementInfo is SignalPlacementInfo end)
        {
            bool found=false;var dir=AutomaticBlock.TravelDirection(end.Direction);
            for(int i=0;i<spans.Count;i++)if(spans[i].Track==end.Track&&spans[i].Direction==dir&&BlockMath.Contains(spans[i].Start,spans[i].End,end.Span))
            {spans[i]=spans[i].Until(end.Span);spans.RemoveRange(i+1,spans.Count-i-1);found=true;break;}
            if(!found&&spans.Count>0)
            {
                var last=spans[spans.Count-1];var step=TrackWalker.WalkTracks(last.Track,last.Direction,1);
                if(step.Count==1&&step[0].Track==end.Track&&step[0].Direction==dir)spans.Add(RailSpan.Whole(end.Track,dir).Until(end.Span));
            }
        }
        // Every train block starts at its own mast, including the final block
        // without an automatic cut and maps with no generated AB controllers.
        // Keeping the native whole rail here also reserves the approach.
        for(int i=0;Ready&&i<spans.Count;i++)
        {
            var span=spans[i];
            if(!Cuts.TryGetValue(new TrackInfo(span.Track,span.Direction),out var candidates))continue;
            BasicSignalController nearest=null;double best=double.PositiveInfinity;
            foreach(var candidate in candidates)
            {
                if(candidate==signal.Controller||!candidate.Exists)continue;
                double position=candidate.PlacementInfo.Value.Span,distance=(position-span.Start)*(span.Direction==TrackDirection.Out?1:-1);
                if(distance>.01&&distance<best&&BlockMath.Contains(span.Start,span.End,position)){nearest=candidate;best=distance;}
            }
            if(nearest==null)continue;
            spans[i]=span.Until(nearest.PlacementInfo.Value.Span);spans.RemoveRange(i+1,spans.Count-i-1);next=nearest;break;
        }
        for(int i=spans.Count-1;i>=0;i--)if(spans[i].Length<.001)spans.RemoveAt(i);
        // At the very end of the approach rail its zero-length forward span
        // disappears. Native turnout extras must not add that rail back whole.
        RailTrack approach=spans.Exists(s=>s.Track==p.Track)?null:p.Track;
        // For a mast on an outgoing turnout leg the common incoming rail is
        // already behind the mast, even while the leg itself remains forward.
        RailTrack behindJunction=p.Track.isJunctionTrack&&travel==TrackDirection.Out?p.Track.inJunction?.inBranch?.track:null;
        bool unchanged=approach==null&&behindJunction==null&&next==block.NextController&&spans.Count==block.Tracks.Length;
        for(int i=0;unchanged&&i<spans.Count;i++)
        {
            var span=spans[i];var native=block.Tracks[i];var full=RailSpan.Whole(native.Track,native.Direction);
            unchanged=span.Track==native.Track&&span.Direction==native.Direction&&span.Start==full.Start&&span.End==full.End;
        }
        if(unchanged)return;
        var array=spans.ToArray();var result=NativeSdk.CreateBlock(array,next,block,approach,behindJunction);
        Windows.Add(result,new BlockWindow(block,array));block=result;
    }
}
