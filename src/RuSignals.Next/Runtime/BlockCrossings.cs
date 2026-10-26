using System;
using System.Collections.Generic;
using Signals.Game.Railway;
namespace RuSignals;
internal sealed partial class BlockNetwork
{
    // Intersection geometry is immutable between map rebuilds. Filter it once
    // per block, rather than projecting points during every occupation query.
    private TrackChecker.TrackIntersectionPoints Crossing(RailTrack track,BlockWindow window)
    {
        if(Crossings==null||!Crossings.TryGetValue(track,out var source))return null;
        if(window==null||!window.Primary.Contains(track)||source.IntersectionPoints.Count==0)return source;
        if(!ReferenceEquals(window.CrossingSource,Crossings)||window.CrossingRevision!=CrossingRevision)
        {window.CrossingSource=Crossings;window.CrossingRevision=CrossingRevision;window.CrossingQueries=null;}
        if(window.CrossingQueries==null)window.CrossingQueries=new Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints>();
        if(window.CrossingQueries.TryGetValue(track,out var result))return result;
        result=new TrackChecker.TrackIntersectionPoints(track);
        foreach(var point in source.IntersectionPoints)
        {
            var closest=RailTrack.GetClosestPoint(track,point.Position.position).Item1;
            double position=closest?.span??double.NaN;
            bool include=double.IsNaN(position)||double.IsInfinity(position);
            foreach(var span in window.Spans)
                if(span.Track==track&&BlockMath.Contains(span.Start,span.End,position)){include=true;break;}
            if(include)result.IntersectionPoints.Add(point);
        }
        window.CrossingQueries.Add(track,result);return result;
    }
}
