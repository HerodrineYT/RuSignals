using RuSignals;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using UnityEngine;

// A receiving entry signal uses a short terminal piece of an otherwise long
// single-line corridor. A separate opposing reservation entirely on approach
// must not forbid the free station reception route beyond this mast.
internal static class EntryApproachReservationChecks
{
    private sealed class Fixture
    {
        internal readonly RailTrack Line=new(500);
        internal readonly int Sign;
        internal readonly TrackDirection Travel;
        internal readonly Signal Head;
        internal Fixture(int sign,SignalRole role)
        {
            AutomaticBlock.Reset();TrackReserver.ClearAll();SignalManager.Instance=new();SignalsMod.Settings.SpecialReservation=false;Time.frameCount++;
            Sign=sign;Travel=sign==1?TrackDirection.Out:TrackDirection.In;RailTrackRegistryBase.RailTracks=new[]{Line};
            var corridor=new Corridor(new[]{RailSpan.Whole(Line,TrackDirection.Out)});
            AutomaticBlock.Network.Corridors.Add(corridor);AutomaticBlock.Network.Tracks.Add(Line,(corridor,TrackDirection.Out));AutomaticBlock.Network.Ready=true;
            Head=Bounded(Travel,400,500,role);
        }
        internal double Position(double forward)=>Sign==1?forward:500-forward;
        internal Signal Bounded(TrackDirection direction,double start,double end,SignalRole role=SignalRole.Through)
        {
            var tail=new BasicSignalController(new(),new(Line,AutomaticBlock.TravelDirection(direction),(int)Position(end),Position(end)));
            Line.Ends[direction]=tail;
            var controller=new TrackSignalController(new(),Line,direction,new(Line,AutomaticBlock.TravelDirection(direction),(int)Position(start),Position(start)));
            var value=controller.Signals[0];foreach(var aspect in value.AllAspects)((RuAspectDefinition)aspect.GetDefinition()).Role=role;
            value.Definition.Aspects=value.AllAspects.Select(a=>a.GetDefinition()).ToArray();controller.UpdateBlocks();return value;
        }
        internal Signal Behind()=>Bounded(AutomaticBlock.TravelDirection(Travel),300,200);
        internal bool Open()=>SignalRules.IsTrainPermission(DecisionPass.Evaluate(Head,((RuAspectDefinition)Head.AllAspects[0].GetDefinition()).Role));
        internal Signal NativeWhole(RailTrack track)
        {
            var controller=new BasicSignalController(new(),new(track,TrackDirection.Out,0,0));
            controller.Signals[0].Block=TrackBlock.CreateUntilMainSignal(track,TrackDirection.Out);return controller.Signals[0];
        }
    }
    internal static void Run(Action<string,bool> check)
    {
        foreach(int sign in new[]{1,-1})
        {
            var f=new Fixture(sign,SignalRole.Entry);string name="entry approach reservation / "+sign;
            check(name+" generated-corridor policy is active",AutomaticBlock.Network.Ready&&AutomaticBlock.Network.Corridors.Count==1);
            check(name+" native entry interval is only the terminal 100 metres",Math.Abs(f.Head.Block.Length-100)<.001);
            var rear=f.Behind();
            check(name+" opposite approach reservation is acquired through native API",TrackReserver.ReserveForSignal(rear));
            check(name+" actual protected intervals do not overlap",!AutomaticBlock.Conflicts(f.Head.Block,rear.Block));
            check(name+" foreign reservation query confirms free station reception",!TrackReserver.IsSignalReservedByAnother(f.Head));
            check(name+" empty forward route opens despite opposite authority wholly behind mast",f.Open());
            check(name+" native reserve accepts entry instead of rejecting whole corridor direction",TrackReserver.ReserveForSignal(f.Head));
            check(name+" successful entry reservation retains independent approach reservation",TrackReserver.HasReservation(f.Head)&&TrackReserver.HasReservation(rear));

            f=new Fixture(sign,SignalRole.Entry);rear=f.Behind();TrackReserver.ReserveForSignal(rear);
            var waiting=new Bogie();waiting.traveller.Span=f.Position(380);f.Line.bogies.Add(waiting);Time.frameCount++;
            check(name+" stopped locomotive wholly before mast does not close free reception",f.Open());
            check(name+" native reserve accepts entry with stopped approach locomotive",TrackReserver.ReserveForSignal(f.Head));

            f=new Fixture(sign,SignalRole.Entry);var intrusion=f.Bounded(AutomaticBlock.TravelDirection(f.Travel),430,410);
            check(name+" conflicting forward reservation is acquired for obstruction test",TrackReserver.ReserveForSignal(intrusion));
            check(name+" authority physically ahead of mast still stops reception",!f.Open());
            check(name+" authority physically ahead of mast still refuses native reserve",!TrackReserver.ReserveForSignal(f.Head));
            intrusion.SetBlock(f.Behind().Block);
            check(name+" changing owner's live route cannot release saved protection ahead",!f.Open()&&!TrackReserver.ReserveForSignal(f.Head));

            f=new Fixture(sign,SignalRole.Entry);var unknown=f.NativeWhole(f.Line);
            check(name+" unknown native full-rail authority acquired",TrackReserver.ReserveForSignal(unknown));
            check(name+" unknown full-rail authority cannot be treated as behind",!f.Open()&&!TrackReserver.ReserveForSignal(f.Head));

            f=new Fixture(sign,SignalRole.Entry);TrackChecker.Mark(f.Line);Time.frameCount++;
            check(name+" manual whole-rail closure still stops entry",!f.Open());
            check(name+" manual whole-rail closure still prevents native direction reserve",!TrackReserver.ReserveForSignal(f.Head));
            TrackChecker.Clear(f.Line);

            f=new Fixture(sign,SignalRole.Entry);var fouling=new RailTrack(500);var cross=f.NativeWhole(fouling);
            check(name+" crossing foreign authority acquired",TrackReserver.ReserveForSignal(cross));
            var points=new TrackChecker.TrackIntersectionPoints(f.Line);
            points.IntersectionPoints.Add((fouling,new Transform{position=new Vector3(0,0,(float)f.Position(450))}));
            AutomaticBlock.MapBuilt(new Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints>{{f.Line,points}});
            check(name+" true fouling ahead remains a foreign reservation conflict",TrackReserver.IsSignalReservedByAnother(f.Head));
            check(name+" fouling conflict still stops reception and refuses reserve",!f.Open()&&!TrackReserver.ReserveForSignal(f.Head));

            // An extra rail need not be projected onto the main corridor to
            // retain a real crossing conflict through the ordinary proof.
            f=new Fixture(sign,SignalRole.Entry);rear=f.Behind();fouling=new RailTrack(500);
            HarmonyLib.AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.ExtraTracks)).Invoke(rear.Block,new object[]{new[]{fouling}});
            check(name+" approach interval with off-corridor fouling protection acquired",TrackReserver.ReserveForSignal(rear));
            points=new TrackChecker.TrackIntersectionPoints(f.Line);points.IntersectionPoints.Add((fouling,new Transform{position=new Vector3(0,0,(float)f.Position(450))}));
            AutomaticBlock.MapBuilt(new Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints>{{f.Line,points}});
            check(name+" off-corridor extra does not alone impose whole-corridor direction lock",AutomaticBlock.DirectionAllowed(f.Head));
            check(name+" off-corridor extra's real crossing still stops reception and native reserve",!f.Open()&&!TrackReserver.ReserveForSignal(f.Head));

            // Another primary rail lies behind the entry, while a native extra
            // protects this entire shared rail including the receiving side.
            f=new Fixture(sign,SignalRole.Entry);var side=new RailTrack(200);
            var pathSpans=sign==1?new[]{RailSpan.Whole(side,TrackDirection.Out),RailSpan.Whole(f.Line,TrackDirection.Out)}:new[]{RailSpan.Whole(f.Line,TrackDirection.Out),RailSpan.Whole(side,TrackDirection.Out)};
            var shared=new Corridor(pathSpans);AutomaticBlock.Network.Corridors.Clear();AutomaticBlock.Network.Corridors.Add(shared);AutomaticBlock.Network.Tracks.Clear();
            AutomaticBlock.Network.Tracks.Add(side,(shared,TrackDirection.Out));AutomaticBlock.Network.Tracks.Add(f.Line,(shared,TrackDirection.Out));
            var opaqueExtra=f.NativeWhole(side);var backwards=AutomaticBlock.TravelDirection(f.Travel);
            var original=TrackBlock.CreateUntilMainSignal(side,backwards);var protectedBlock=TrackBlock.CreateUntilMainSignal(side,backwards);
            var sourceStart=sign==1?100d:50d;var sourceEnd=sign==1?50d:100d;
            AutomaticBlock.Network.Windows.Add(protectedBlock,new BlockWindow(original,new[]{new RailSpan(side,backwards,sourceStart,sourceEnd)}));
            HarmonyLib.AccessTools.PropertySetter(typeof(TrackBlock),nameof(TrackBlock.ExtraTracks)).Invoke(protectedBlock,new object[]{new[]{f.Line}});opaqueExtra.Block=protectedBlock;
            check(name+" bounded opposing rear owner with whole shared-rail extra acquired",TrackReserver.ReserveForSignal(opaqueExtra));
            check(name+" unbounded extra on shared corridor cannot be treated as wholly behind",!AutomaticBlock.DirectionAllowed(f.Head));
            check(name+" shared-corridor extra still stops reception and native reserve",!f.Open()&&!TrackReserver.ReserveForSignal(f.Head));

            foreach(var role in new[]{SignalRole.Through,SignalRole.Exit,SignalRole.Route})
            {
                f=new Fixture(sign,role);rear=f.Behind();
                check(name+" "+role+" setup preserves opposing approach authority",TrackReserver.ReserveForSignal(rear));
                check(name+" "+role+" global corridor direction remains restrictive",!f.Open());
                check(name+" "+role+" native reserve retains global corridor rule",!TrackReserver.ReserveForSignal(f.Head));
            }
        }
        TrackReserver.ClearAll();AutomaticBlock.Reset();SignalManager.Instance=new();Time.frameCount++;
    }
}
