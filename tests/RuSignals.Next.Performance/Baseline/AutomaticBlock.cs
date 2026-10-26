using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Generation;
using Signals.Game.Railway;
using UnityEngine;
using GameSignal = Signals.Game.Signal;

namespace RuSignals;

// Native TrackBlock objects remain the public API; exact span limits supplement
// their track list. There are no fake RailTracks and no alteration of game rails.
public static class AutomaticBlock
{
    private sealed class Slice
    {
        internal RailTrack Track;
        internal TrackDirection Direction;
        internal double From, To;
        internal double Length => Math.Abs(To - From);
    }
    private sealed class Window
    {
        internal Slice[] Slices;
        internal TrackBlock Route;
        // Geometry is immutable for this block. Occupancy and reservations are
        // deliberately never cached here: both can change within the same frame.
        internal HashSet<RailTrack> Primary;
    }
    private sealed class Corridor
    {
        internal Slice[] Slices;
        internal int Direction = 1;
        internal bool Occupied, Unknown, DirectionConflict;
    }
    private sealed class BlockController : TrackSignalController
    {
        internal BlockController(SignalControllerDefinition def, SignalPlacementInfo info)
            : base(def, info.Track, TravelDirection(info.Direction), info) { Type = SignalType.Mainline; PrefabType = PrefabType.Mainline; }
        public override bool ShouldUpdate() => true;
        public override IEnumerable<(GameSignal Signal, Dictionary<BasicSignalController,float> Controllers)> GetPotentialNextControllers()
        {
            foreach (var signal in Signals)
                if (signal.Block?.NextController != null)
                    yield return (signal, new Dictionary<BasicSignalController,float>{{signal.Block.NextController,signal.Block.Length}});
        }
    }
    private static ConditionalWeakTable<TrackBlock,Window> Windows = new ConditionalWeakTable<TrackBlock,Window>();
    private static readonly Dictionary<TrackInfo,List<BasicSignalController>> Boundaries = new Dictionary<TrackInfo,List<BasicSignalController>>();
    private static readonly Dictionary<RailTrack,(Corridor Corridor,TrackDirection Direction)> Corridors = new Dictionary<RailTrack,(Corridor,TrackDirection)>();
    private static readonly List<Corridor> UniqueCorridors = new List<Corridor>();
    private static readonly List<BlockController> Created = new List<BlockController>();
    private static readonly Dictionary<BasicSignalController,(double Min,double Max)> MovementLimits = new Dictionary<BasicSignalController,(double,double)>();
    private static Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints> Intersections;
    private static readonly ConstructorInfo BlockConstructor = AccessTools.Constructor(typeof(TrackBlock),new[]{typeof(IEnumerable<TrackInfo>),typeof(BasicSignalController)});
    private static readonly FieldInfo ManualOccupied = AccessTools.Field(typeof(TrackChecker),"s_occupied");
    private static bool Enabled;
    private static int PollFrame = -1;
    public static int SignalCount => Created.Count;
    internal static bool IsSignal(GameSignal signal) => signal?.Controller is BlockController;
    // PlacementInfo.Direction points toward the approaching driver (the face).
    // TrackInfo.Direction points along the protected route. They are opposite
    // on the placement track, as in DVSignals' native junction/branch placers.
    internal static TrackDirection TravelDirection(TrackDirection facing) => facing==TrackDirection.Out?TrackDirection.In:TrackDirection.Out;

    internal static void Patch(Harmony harmony)
    {
        PatchMethod(typeof(StationControllerCache),"Generate",nameof(Generate));
        PatchMethod(typeof(SignalManager),"ClearCaches",nameof(Reset));
        PatchMethod(typeof(SignalManager),"OnDestroy",nameof(Reset));
        PatchMethod(typeof(GameSignal),nameof(GameSignal.SetBlock),nameof(SetBlock));
        PatchMethod(typeof(TrackBlock),nameof(TrackBlock.IsOccupied),nameof(Occupied));
        PatchMethod(typeof(TrackBlock),nameof(TrackBlock.IsOccupiedIgnoreLogic),nameof(OccupiedIgnoreLogic));
        PatchMethod(typeof(TrackBlock),nameof(TrackBlock.TracksMatch),nameof(TracksMatch));
        harmony.Patch(AccessTools.PropertyGetter(typeof(TrackBlock),nameof(TrackBlock.ShouldBeUpdated)),
            postfix:new HarmonyMethod(typeof(AutomaticBlock),nameof(RouteChanged)));
        harmony.Patch(AccessTools.Method(typeof(BasicSignalController),nameof(BasicSignalController.OffsetOnTrack)),
            prefix:new HarmonyMethod(typeof(AutomaticBlock),nameof(ConstrainMovement)),
            postfix:new HarmonyMethod(typeof(AutomaticBlock),nameof(PositionChanged)));
        harmony.Patch(AccessTools.Method(typeof(TrackReserver),nameof(TrackReserver.ReserveForSignal),new[]{typeof(GameSignal)}),
            prefix:new HarmonyMethod(typeof(AutomaticBlock),nameof(BeforeReserve)));
        TrackChecker.OnMapBuilt += MapBuilt;
        IntervalReservations.Patch(harmony);
        void PatchMethod(Type type,string method,string prefix) => harmony.Patch(AccessTools.Method(type,method),prefix:new HarmonyMethod(typeof(AutomaticBlock),prefix));
    }
    private static void MapBuilt(Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints> map) => Intersections = map;
    private static void ConstrainMovement(BasicSignalController __instance,ref int pointIndexOffset)
    {
        if(!MovementLimits.TryGetValue(__instance,out var limit) || !__instance.PlacementInfo.HasValue)return;
        var p=__instance.PlacementInfo.Value;var set=p.Track.GetKinkedPointSet();
        int index=Math.Max(0,Math.Min(set.points.Length-1,p.PointIndex+pointIndexOffset));
        while(index>p.PointIndex && set.points[index].span>limit.Max)index--;
        while(index<p.PointIndex && set.points[index].span<limit.Min)index++;
        pointIndexOffset=index-p.PointIndex;
    }
    private static void PositionChanged(BasicSignalController __instance,bool __result)
    {
        if(!__result || !IsRuTrain(__instance.GetControllerSignal()) || !__instance.PlacementInfo.HasValue)return;
        var p=__instance.PlacementInfo.Value;
        // DVSignals updates PointIndex when avoiding a crossing, but leaves Span
        // at its old value. Both the block boundary and RSG must follow the mast.
        p.Span=p.Track.GetKinkedPointSet().points[p.PointIndex].span;
        AccessTools.PropertySetter(typeof(BasicSignalController),nameof(BasicSignalController.PlacementInfo)).Invoke(__instance,new object[]{p});
        if(Enabled)foreach(var c in SignalManager.Instance.AllControllers)c.FlagAllBlocksForUpdating();
    }
    internal static void Cleanup() { TrackChecker.OnMapBuilt -= MapBuilt; Reset(); }
    private static void Reset()
    {
        Enabled=false; Windows=new ConditionalWeakTable<TrackBlock,Window>(); Boundaries.Clear();
        Created.Clear(); MovementLimits.Clear(); Corridors.Clear(); UniqueCorridors.Clear(); Intersections=null; PollFrame=-1;
        IntervalReservations.Reset();
    }
    private static bool IsRuTrain(GameSignal signal) => signal != null && !signal.IsShunting &&
        signal.AllAspects.Any(a => a.GetDefinition() is RuAspectDefinition r &&
            (r.Role==SignalRole.Through || r.Role==SignalRole.Entry || r.Role==SignalRole.Exit || r.Role==SignalRole.Route));
    private static double Length(RailTrack track) => track.GetKinkedPointSet().span;
    private static Slice Full(RailTrack track, TrackDirection direction) => new Slice {
        Track=track,Direction=direction,From=direction==TrackDirection.Out?0:Length(track),To=direction==TrackDirection.Out?Length(track):0};
    private static bool Eligible(RailTrack track) => track != null && !track.isJunctionTrack &&
        !track.name.StartsWith("[Y]",StringComparison.Ordinal) && !track.name.StartsWith("[#]",StringComparison.Ordinal) &&
        SignalPlacer.IsLogicYardTrack(track)==false && Length(track)>1 && !TurntableHelper.IsTurntableEnd(track);

    // Runs before StationControllerCache and MultiplayerIntegration.StartInstance.
    // Registry order is the same deterministic order used by DV's track hash.
    private static void Generate()
    {
        try { Build(); }
        catch(Exception ex)
        {
            // Roll back partial generation, leaving the original pack usable.
            foreach(var controller in Created.ToArray())controller.Destroy();
            Reset();
            foreach(var c in SignalManager.Instance.AllControllers.ToArray())c.FlagAllBlocksForUpdating();
            foreach(var c in SignalManager.Instance.AllControllers.ToArray())c.UpdateBlocks();
            Debug.LogError("[RUS Signals] automatic block generation rolled back: "+ex);
        }
    }
    private static void Build()
    {
        if (SignalManager.CurrentPack?.ModId != Mod.Id || Enabled) return;
        var manager=SignalManager.Instance;
        var originals=manager.AllControllers.ToArray();
        var used=new HashSet<RailTrack>();
        foreach(var track in RailTrackRegistryBase.RailTracks)
        {
            if(!Eligible(track)||used.Contains(track))continue;
            foreach(var direction in new[]{TrackDirection.Out,TrackDirection.In})
            {
                var previous=TrackWalker.WalkTracks(track,direction==TrackDirection.Out?TrackDirection.In:TrackDirection.Out,1);
                if(previous.Count>0 && Eligible(previous[0].Track))continue;
                var run=new List<Slice>(); var visited=new HashSet<RailTrack>();
                var current=track; var dir=direction;
                while(Eligible(current)&&visited.Add(current)&&run.Count<64)
                {
                    run.Add(Full(current,dir));
                    var next=TrackWalker.WalkTracks(current,dir,1);
                    if(next.Count==0)break;
                    current=next[0].Track; dir=next[0].Direction;
                }
                foreach(var piece in run)used.Add(piece.Track);
                if(run.Count==0 || BlockMath.Boundaries(run.Sum(s=>s.Length)).Length==0)break;
                var corridor=new Corridor{Slices=run.ToArray()};
                int count=Created.Count;
                AddDirection(run,originals);
                AddDirection(run.AsEnumerable().Reverse().Select(s=>new Slice{Track=s.Track,Direction=s.Direction==TrackDirection.Out?TrackDirection.In:TrackDirection.Out,From=s.To,To=s.From}).ToList(),originals);
                if(Created.Count>count)foreach(var piece in run)Corridors[piece.Track]=(corridor,piece.Direction);
                break;
            }
        }
        // Build the distinct list once, after all physical tracks have been
        // assigned. Reuse it for the existing once-per-frame direction poll.
        UniqueCorridors.Clear();
        UniqueCorridors.AddRange(Corridors.Values.Select(p=>p.Corridor).Distinct());
        Enabled=Created.Count>0;
        if(Enabled)
        {
            // On AB the last through signal warns of the station entry. Avoid
            // keeping another standalone warning head just before that entry.
            foreach(var distant in originals.OfType<DistantSignalController>())
            {
                if(distant.IsRepeater || !(distant.Home.PlacementInfo is SignalPlacementInfo p) || !Corridors.ContainsKey(p.Track))continue;
                var callback=(Action<GameSignal,Signals.Game.Aspects.IAspect>)Delegate.CreateDelegate(
                    typeof(Action<GameSignal,Signals.Game.Aspects.IAspect>),distant,AccessTools.Method(typeof(DistantSignalController),"UpdateFromHome"));
                distant.Home.AnyAspectChanged-=callback;
                ((System.Collections.IList)AccessTools.Field(typeof(SignalManager),"_distantSignals").GetValue(manager)).Remove(distant);
                distant.Destroy();
            }
            foreach(var c in manager.AllControllers.ToArray())c.FlagAllBlocksForUpdating();
            foreach(var c in manager.AllControllers.ToArray())c.UpdateBlocks();
        }
        Debug.Log("[RUS Signals] automatic block: "+Created.Count+" native controllers; "+UniqueCorridors.Count+" corridors; spacing 400-1000 m (target 700 m).");
    }
    private static void AddDirection(List<Slice> run,BasicSignalController[] originals)
    {
        // Do not equip a dead-end siding or a route without a known main boundary.
        var raw=TrackBlock.CreateUntilMainSignal(run[0].Track,run[0].Direction);
        if(raw.IsSelfLoop || raw.NextController==null || !IsRuTrain(raw.NextController.GetControllerSignal()))return;
        double total=run.Sum(s=>s.Length);
        var anchors=new List<double>{0,total};
        var existing=new Dictionary<BasicSignalController,double>();
        foreach(var controller in originals)
        {
            if(!controller.PlacementInfo.HasValue || !IsRuTrain(controller.GetControllerSignal()))continue;
            var p=controller.PlacementInfo.Value; double offset=0;
            foreach(var s in run)
            {
                if(s.Track==p.Track && s.Direction==TravelDirection(p.Direction))
                {
                    double anchor=offset+Math.Abs(p.Span-s.From);
                    anchors.Add(anchor);existing[controller]=anchor;
                }
                offset+=s.Length;
            }
        }
        var ordered=anchors.Distinct().OrderBy(x=>x).ToArray();
        for(int a=0;a<ordered.Length-1;a++)
        {
        double interval=ordered[a+1]-ordered[a];
        var positions=BlockMath.Boundaries(interval);
        if(positions.Length>0)
        {
            // Existing end signals participate in spacing as well: a native
            // crossing correction must not leave an undersized first/last block.
            double endMove=BlockMath.MovementAllowance(positions[0],interval-positions.Last());
            foreach(var anchor in existing.Where(p=>Math.Abs(p.Value-ordered[a])<.01 || Math.Abs(p.Value-ordered[a+1])<.01))
            {
                double span=anchor.Key.PlacementInfo.Value.Span;
                var limits=(Min:span-endMove,Max:span+endMove);
                if(MovementLimits.TryGetValue(anchor.Key,out var old))limits=(Math.Max(old.Min,limits.Min),Math.Min(old.Max,limits.Max));
                MovementLimits[anchor.Key]=limits;
            }
        }
        for(int b=0;b<positions.Length;b++)
        {
            double position=positions[b];
            double distance=ordered[a]+position; Slice piece=null;
            foreach(var s in run){if(distance<=s.Length){piece=s;break;}distance-=s.Length;}
            if(piece==null)continue;
            var set=piece.Track.GetKinkedPointSet();
            var index=set.GetPointIndexForSpan(piece.From+(piece.Direction==TrackDirection.Out?distance:-distance));
            var point=set.points[index];
            var info=new SignalPlacementInfo(piece.Track,TravelDirection(piece.Direction),index,point.span);
            var definition=SignalPlacer.InstantiateFromDef(SignalManager.CurrentPack.Signal,point.position,
                piece.Direction==TrackDirection.Out?-point.forward:point.forward,false,piece.Track);
            definition.gameObject.SetActive(true);
            var c=new BlockController(definition,info);
            foreach(var signal in c.Signals)signal.AllowReserving=false;
            c.NameOverride="АБ-"+(Created.Count+1);
            Created.Add(c);
            double before=position-(b==0?0:positions[b-1]);
            double after=(b==positions.Length-1?interval:positions[b+1])-position;
            double movement=BlockMath.MovementAllowance(before,after);
            MovementLimits[c]=(point.span-movement,point.span+movement);
            var key=new TrackInfo(piece.Track,piece.Direction);
            if(!Boundaries.TryGetValue(key,out var list))Boundaries[key]=list=new List<BasicSignalController>();
            list.Add(c);
        }
        }
    }
    private static void SetBlock(GameSignal __instance,ref TrackBlock block)
    {
        if(!Enabled || block==null || block.Tracks.Length==0 || !IsRuTrain(__instance) ||
            !__instance.Controller.PlacementInfo.HasValue || Windows.TryGetValue(block,out _))return;
        var placement=__instance.Controller.PlacementInfo.Value;
        var travel=TravelDirection(placement.Direction);
        var list=new List<Slice>();
        int start=Array.FindIndex(block.Tracks,x=>x.Track==placement.Track && x.Direction==travel);
        if(start<0){var s=Full(placement.Track,travel);s.From=placement.Span;list.Add(s);start=0;}
        for(int i=start;i<block.Tracks.Length;i++)
        {
            var t=block.Tracks[i]; var s=Full(t.Track,t.Direction);
            if(t.Track==placement.Track)s.From=placement.Span;
            list.Add(s);
        }
        var next=block.NextController;
        if(next?.PlacementInfo is SignalPlacementInfo end)
        {
            var endTravel=TravelDirection(end.Direction);
            int index=list.FindIndex(s=>s.Track==end.Track && s.Direction==endTravel);
            if(index>=0){list[index].To=end.Span;list.RemoveRange(index+1,list.Count-index-1);}
            else
            {
                var last=list.LastOrDefault();
                var walk=last==null?null:TrackWalker.WalkTracks(last.Track,last.Direction,1);
                if(walk!=null && walk.Count==1 && walk[0].Track==end.Track && walk[0].Direction==endTravel)
                {var s=Full(end.Track,endTravel);s.To=end.Span;list.Add(s);}
            }
        }
        bool changed=__instance.Controller is BlockController;
        for(int i=0;i<list.Count;i++)
        {
            var s=list[i];
            if(!Boundaries.TryGetValue(new TrackInfo(s.Track,s.Direction),out var candidates))continue;
            var c=candidates.Where(c=>c!=__instance.Controller && c.Exists &&
                BlockMath.Contains(s.From,s.To,c.PlacementInfo.Value.Span) &&
                (c.PlacementInfo.Value.Span-s.From)*(s.Direction==TrackDirection.Out?1:-1)>.01)
                .OrderBy(c=>Math.Abs(c.PlacementInfo.Value.Span-s.From)).FirstOrDefault();
            if(c==null)continue;
            s.To=c.PlacementInfo.Value.Span;list.RemoveRange(i+1,list.Count-i-1);next=c;changed=true;break;
        }
        if(!changed)return;
        list.RemoveAll(s=>s.Length<.001);
        if(list.Count==0)return;
        var replacement=(TrackBlock)BlockConstructor.Invoke(new object[]{list.Select(s=>new TrackInfo(s.Track,s.Direction)).ToArray(),next});
        AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.Length)).Invoke(replacement,new object[]{(float)list.Sum(s=>s.Length)});
        AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.IsSelfLoop)).Invoke(replacement,new object[]{block.IsSelfLoop});
        AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.IsDeadEnd)).Invoke(replacement,new object[]{next==null && block.IsDeadEnd});
        // Keep the native route for its station/track metadata only. Occupancy,
        // reservations and the public boundary still use this short interval.
        Windows.Add(replacement,new Window{Slices=list.ToArray(),Route=block,Primary=new HashSet<RailTrack>(list.Select(s=>s.Track))}); block=replacement;
    }
    private static void RouteChanged(TrackBlock __instance,ref bool __result)
    {
        if(!__result && Windows.TryGetValue(__instance,out var window))
            __result=window.Route.ShouldBeUpdated;
    }
    internal static string RouteTrackNumber(TrackBlock block)
    {
        if(block==null || block.ShouldBeUpdated)return "";
        return (Windows.TryGetValue(block,out var window)?window.Route:block).TrackTrimmerNumber;
    }
    internal static TrackInfo[] RouteTracks(TrackBlock block) =>
        block==null || block.ShouldBeUpdated ? Array.Empty<TrackInfo>() :
        (Windows.TryGetValue(block,out var window)?window.Route:block).Tracks;
    private static bool Occupied(TrackBlock __instance,CrossingCheckMode crossingMode,ref bool __result) =>
        TestOccupied(__instance,crossingMode,false,ref __result);
    private static bool OccupiedIgnoreLogic(TrackBlock __instance,CrossingCheckMode crossingMode,ref bool __result) =>
        TestOccupied(__instance,crossingMode,true,ref __result);
    private static bool TestOccupied(TrackBlock block,CrossingCheckMode mode,bool ignoreLogic,ref bool result)
    {
        if(!Windows.TryGetValue(block,out var window))return true;
        result=false;
        var manually=ManualOccupied.GetValue(null) as HashSet<RailTrack>;
        foreach(var s in window.Slices)
        {
            if(ignoreLogic && SignalPlacer.IsLogicYardTrack(s.Track))continue;
            if(manually?.Contains(s.Track)==true || BogiesIn(s)) {result=true;return false;}
            // Preserve crossing protection, conservatively for the whole physical
            // track. Unlike TrackChecker.IsOccupied this does not include its bogies.
            if(Intersections!=null && Intersections.TryGetValue(s.Track,out var crossings) &&
                (mode==CrossingCheckMode.WholeTrack?crossings.TestTracks():mode==CrossingCheckMode.IntersectionOnly && crossings.TestIntersections()))
            {result=true;return false;}
        }
        foreach(var extra in block.ExtraTracks)
            if(!window.Primary.Contains(extra) && (!ignoreLogic||!SignalPlacer.IsLogicYardTrack(extra)) && TrackChecker.IsOccupied(extra,mode))
            {result=true;break;}
        return false;
    }
    private static bool BogiesIn(Slice slice)
    {
        foreach(var bogie in RailTrackOnTrackBogiesExtensions.BogiesOnTrack(slice.Track))
        {
            if(bogie==null || bogie.traveller==null)return true;
            // Pivot-to-outer-axle clearance keeps a block occupied at a boundary.
            if(BlockMath.Contains(slice.From,slice.To,bogie.traveller.Span,4))return true;
        }
        return false;
    }
    public static bool TryContains(TrackBlock block,RailTrack track,double span,int direction,out bool contains)
    {
        contains=false;if(block==null || !Windows.TryGetValue(block,out var w))return false;
        if(!block.ShouldBeUpdated)foreach(var s in w.Slices)
            if(s.Track==track && (s.Direction==TrackDirection.Out?1:-1)==direction && BlockMath.Contains(s.From,s.To,span))
            {contains=true;break;}
        return true;
    }
    internal static bool HasWindow(TrackBlock block) => block!=null && Windows.TryGetValue(block,out _);
    internal static bool SameDirection(GameSignal a,GameSignal b)
    {
        foreach(var p in ProtectedDirections(a))foreach(var q in ProtectedDirections(b))
            if(q.Corridor==p.Corridor && q.Direction==p.Direction)return true;
        return false;
    }
    internal static bool Conflicts(TrackBlock a,TrackBlock b)
    {
        Windows.TryGetValue(a,out var x);Windows.TryGetValue(b,out var y);
        int countA=x==null?a.Tracks.Length:x.Slices.Length,countB=y==null?b.Tracks.Length:y.Slices.Length;
        for(int i=0;i<countA;i++)for(int j=0;j<countB;j++)
        {
            var trackA=x==null?a.Tracks[i].Track:x.Slices[i].Track;
            var trackB=y==null?b.Tracks[j].Track:y.Slices[j].Track;
            if(trackA!=trackB)continue;
            if(BlockMath.Overlaps(x==null?0:x.Slices[i].From,x==null?Length(trackA):x.Slices[i].To,
                y==null?0:y.Slices[j].From,y==null?Length(trackB):y.Slices[j].To))return true;
        }
        foreach(var track in a.ExtraTracks)
            if(!PrimaryContains(a,x,track) && b.AllTracks.Contains(track))return true;
        foreach(var track in b.ExtraTracks)
            if(!PrimaryContains(b,y,track) && a.AllTracks.Contains(track))return true;
        if(Intersections!=null && Intersections.Count>0)
        {
            var otherTracks=b.AllTracks;
            foreach(var track in a.AllTracks)
                if(Intersections.TryGetValue(track,out var crossings))
                    foreach(var other in otherTracks)if(crossings.HasIntersectionWithTrack(other))return true;
        }
        return false;
    }
    private static bool PrimaryContains(TrackBlock block,Window window,RailTrack track)
    {
        if(window!=null)return window.Primary.Contains(track);
        foreach(var info in block.Tracks)if(info.Track==track)return true;
        return false;
    }
    private static bool TracksMatch(TrackBlock a,TrackBlock b,ref bool __result)
    {
        Window x=null,y=null;
        if(a!=null)Windows.TryGetValue(a,out x);
        if(b!=null)Windows.TryGetValue(b,out y);
        if(x==null&&y==null)return true;
        __result=false;
        if(x==null || y==null || x.Slices.Length!=y.Slices.Length)return false;
        for(int i=0;i<x.Slices.Length;i++)
        {
            var p=x.Slices[i];var q=y.Slices[i];
            if(p.Track!=q.Track || p.Direction!=q.Direction || !(Math.Abs(p.From-q.From)<.001) || !(Math.Abs(p.To-q.To)<.001))return false;
        }
        __result=true;
        return false;
    }
    // A value enumerator avoids a yield iterator and captured LINQ predicates
    // on every aspect/reservation query. It still reads the live block each time.
    private struct DirectionEnumerator
    {
        private readonly Slice[] slices;
        private int index;
        public (Corridor Corridor,int Direction) Current { get; private set; }
        internal DirectionEnumerator(Slice[] value) { slices=value;index=0;Current=default; }
        public DirectionEnumerator GetEnumerator()=>this;
        public bool MoveNext()
        {
            while(index<slices.Length)
            {
                var s=slices[index++];
                if(!Corridors.TryGetValue(s.Track,out var p))continue;
                Current=(p.Corridor,p.Direction==s.Direction?1:-1);return true;
            }
            return false;
        }
    }
    private static DirectionEnumerator ProtectedDirections(GameSignal signal) =>
        new DirectionEnumerator(signal?.Block!=null && Windows.TryGetValue(signal.Block,out var w)?w.Slices:Array.Empty<Slice>());
    private static void Poll()
    {
        if(PollFrame==Time.frameCount)return;PollFrame=Time.frameCount;
        var manual=ManualOccupied.GetValue(null) as HashSet<RailTrack>;
        foreach(var corridor in UniqueCorridors)
        {
            bool was=corridor.Occupied; corridor.Occupied=false;int moving=0;bool conflict=false;
            foreach(var s in corridor.Slices)
            {
                if(manual?.Contains(s.Track)==true){corridor.Occupied=true;conflict=true;}
                foreach(var bogie in RailTrackOnTrackBogiesExtensions.BogiesOnTrack(s.Track))
                {
                    corridor.Occupied=true;
                    if(bogie==null || bogie.traveller==null){conflict=true;continue;}
                    if(bogie.rb==null)continue;
                    var set=s.Track.GetKinkedPointSet();var p=set.points[set.GetPointIndexForSpan(bogie.traveller.Span)];
                    float speed=Vector3.Dot(bogie.rb.velocity,p.forward)*(s.Direction==TrackDirection.Out?1:-1);
                    if(Math.Abs(speed)<.05f)continue;
                    int direction=speed>0?1:-1;
                    if(moving!=0 && moving!=direction)conflict=true;
                    moving=direction;
                }
            }
            bool opposingReserve=moving!=0 && HasOpposingReservation(corridor,moving);
            if(conflict || opposingReserve || (was && !corridor.Unknown && moving!=0 && moving!=corridor.Direction))
                corridor.DirectionConflict=true;
            if(!was && corridor.Occupied)
            {
                corridor.Unknown=moving==0 || corridor.DirectionConflict;
                if(!corridor.Unknown)corridor.Direction=moving;
            }
            if(corridor.Unknown && moving!=0 && !corridor.DirectionConflict){corridor.Direction=moving;corridor.Unknown=false;}
            if(corridor.DirectionConflict)corridor.Unknown=true;
            if(!corridor.Occupied){corridor.Unknown=false;corridor.DirectionConflict=false;}
        }
    }
    internal static bool DirectionAllowed(GameSignal signal)
    {
        if(!Enabled)return true;Poll();
        foreach(var p in ProtectedDirections(signal))
            if(p.Corridor.Unknown || (p.Corridor.Occupied && p.Corridor.Direction!=p.Direction) ||
                HasOpposingReservation(p.Corridor,p.Direction,signal))return false;
        return true;
    }
    // A clear, unreserved corridor has no persistent direction lock. Otherwise
    // an expired phone request would strand the reverse route until another
    // manual request. Occupation establishes the direction automatically; a
    // live native reservation may lock it before the train enters.
    private static bool HasOpposingReservation(Corridor corridor,int direction,GameSignal except=null)
    {
        foreach(var reserved in IntervalReservations.NativeSignals)
        {
            if(!SignalManager.Instance.TryGetSignal(reserved.Id,out var owner) || owner==except)continue;
            foreach(var p in ProtectedDirections(owner))
                if(p.Corridor==corridor && p.Direction!=direction)return true;
        }
        return false;
    }
    private static bool BeforeReserve(GameSignal signal,ref bool __result)
    {
        if(!Enabled || !IsRuTrain(signal))return true;
        if(IsSignal(signal)){__result=false;return false;}
        signal.Controller.UpdateBlocks();
        if(TryReserveDirection(signal))return true;
        __result=false;return false;
    }
    internal static bool TryReserveDirection(GameSignal signal)
    {
        if(!Enabled || !IsRuTrain(signal))return true;
        Poll();
        foreach(var p in ProtectedDirections(signal))
        {
            if(p.Corridor.Unknown || (p.Corridor.Occupied && p.Corridor.Direction!=p.Direction))return false;
            if(HasOpposingReservation(p.Corridor,p.Direction,signal))return false;
        }
        // Validation must not change direction before the native reservation
        // succeeds. DirectionAllowed reads the actual reservation set.
        return true;
    }
}
