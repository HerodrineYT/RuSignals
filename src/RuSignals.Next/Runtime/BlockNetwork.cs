using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;

internal readonly struct RailSpan
{
    internal readonly RailTrack Track;
    internal readonly TrackDirection Direction;
    internal readonly double Start,End;
    internal double Length=>Math.Abs(End-Start);
    internal RailSpan(RailTrack track,TrackDirection direction,double start,double end){Track=track;Direction=direction;Start=start;End=end;}
    internal static RailSpan Whole(RailTrack track,TrackDirection direction)=>new RailSpan(track,direction,direction==TrackDirection.Out?0:track.GetKinkedPointSet().span,direction==TrackDirection.Out?track.GetKinkedPointSet().span:0);
    internal RailSpan Reverse()=>new RailSpan(Track,AutomaticBlock.TravelDirection(Direction),End,Start);
    internal RailSpan Until(double end)=>new RailSpan(Track,Direction,Start,end);
}
internal sealed class BlockWindow
{
    internal readonly TrackBlock Route;
    internal readonly RailSpan[] Spans;
    internal readonly HashSet<RailTrack> Primary=new HashSet<RailTrack>();
    internal object CrossingSource;
    internal int CrossingRevision=-1;
    internal Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints> CrossingQueries;
    internal BlockWindow(TrackBlock route,RailSpan[] spans){Route=route;Spans=spans;foreach(var s in spans)Primary.Add(s.Track);}
}
internal sealed class Corridor
{
    internal readonly RailSpan[] Spans;
    internal bool Occupied,Unknown,Conflict;
    internal int Direction=1;
    internal Corridor(RailSpan[] spans){Spans=spans;}
}
internal sealed class AutoController:TrackSignalController
{
    internal AutoController(SignalControllerDefinition definition,SignalPlacementInfo placement):base(definition,placement.Track,AutomaticBlock.TravelDirection(placement.Direction),placement)
    {Type=SignalType.Mainline;PrefabType=PrefabType.Mainline;}
    public override bool ShouldUpdate()=>true;
    public override IEnumerable<(Signal Signal,Dictionary<BasicSignalController,float> Controllers)> GetPotentialNextControllers()
    {
        foreach(var signal in Signals)
        {
            var next=signal.Block?.NextController;
            if(next!=null)yield return (signal,new Dictionary<BasicSignalController,float>{{next,signal.Block.Length}});
        }
    }
}
internal sealed partial class BlockNetwork
{
    internal readonly ConditionalWeakTable<TrackBlock,BlockWindow> Windows=new ConditionalWeakTable<TrackBlock,BlockWindow>();
    internal readonly List<AutoController> Controllers=new List<AutoController>();
    internal readonly Dictionary<RailTrack,(Corridor Path,TrackDirection Forward)> Tracks=new Dictionary<RailTrack,(Corridor,TrackDirection)>();
    internal readonly List<Corridor> Corridors=new List<Corridor>();
    internal readonly Dictionary<TrackInfo,List<BasicSignalController>> Cuts=new Dictionary<TrackInfo,List<BasicSignalController>>();
    internal readonly Dictionary<BasicSignalController,(double Min,double Max)> MoveLimits=new Dictionary<BasicSignalController,(double,double)>();
    internal Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints> Crossings;
    internal int CrossingRevision;
    internal bool Ready;
    private int polledFrame=-1;
    internal static bool Train(Signal signal)
    {
        if(signal==null||signal.IsShunting)return false;
        foreach(var aspect in signal.AllAspects)
            if(aspect.GetDefinition() is RuAspectDefinition d&&d.Role<=SignalRole.Route)return true;
        return false;
    }
}
