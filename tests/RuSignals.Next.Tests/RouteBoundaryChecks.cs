using RuSignals;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
internal static class RouteBoundaryChecks
{
    internal static void Run(Action<string,bool> check)
    {
        foreach(var role in new[]{SignalRole.Through,SignalRole.Entry,SignalRole.Exit,SignalRole.Route})
        foreach(int sign in new[]{1,-1})
        {
            AutomaticBlock.Reset();TrackReserver.ClearAll();SignalManager.Instance=new();
            var track=new RailTrack(500);var travel=sign==1?TrackDirection.Out:TrackDirection.In;
            var face=AutomaticBlock.TravelDirection(travel);double mast=sign==1?100:400;
            var controller=new TrackSignalController(new(),track,travel,new(track,face,(int)mast,mast));
            foreach(var aspect in controller.Signals[0].AllAspects)((RuAspectDefinition)aspect.GetDefinition()).Role=role;
            controller.UpdateBlocks();var signal=controller.Signals[0];var bogie=new Bogie();track.bogies.Add(bogie);
            foreach(double distance in new[]{20d,4,3,1,.01})
            {
                bogie.traveller.Span=mast-distance*sign;
                check(role+" / "+sign+" approach "+distance+"m does not occupy forward route",!signal.Block.IsOccupied(CrossingCheckMode.Ignore));
            }
            foreach(double distance in new[]{0d,.01,1,20})
            {
                bogie.traveller.Span=mast+distance*sign;
                check(role+" / "+sign+" crossing mast occupies forward route",signal.Block.IsOccupied(CrossingCheckMode.Ignore));
            }
            track.bogies.Clear();TrackChecker.Mark(track);
            check(role+" / "+sign+" manual closure is retained",signal.Block.IsOccupied(CrossingCheckMode.Ignore));TrackChecker.Clear(track);
            track.bogies.Add(bogie);bogie.traveller.Span=double.NaN;
            check(role+" / "+sign+" unknown bogie coordinate fails closed",signal.Block.IsOccupied(CrossingCheckMode.Ignore));track.bogies.Clear();
        }
        // Native TrackBlock expands every traversed turnout to all its legs,
        // including the incoming rail behind a mast exactly at its endpoint.
        foreach(int sign in new[]{1,-1})foreach(bool diverging in new[]{false,true})
        {
            AutomaticBlock.Reset();TrackReserver.ClearAll();SignalManager.Instance=new();SignalsMod.Settings.SpecialReservation=true;
            var approach=new RailTrack(300);var branch=new RailTrack(30){isJunctionTrack=true,name=diverging?"[track diverging]":"[track through]"};var other=new RailTrack(30);
            var junction=new Junction{inBranch=new(){track=approach}};junction.outBranches.Add(new(){track=branch});junction.outBranches.Add(new(){track=other});branch.inJunction=junction;
            var travel=sign==1?TrackDirection.Out:TrackDirection.In;var face=AutomaticBlock.TravelDirection(travel);double mast=sign==1?300:0;
            var controller=new TrackSignalController(new(),branch,TrackDirection.Out,new(approach,face,(int)mast,mast));
            foreach(var aspect in controller.Signals[0].AllAspects)((RuAspectDefinition)aspect.GetDefinition()).Role=SignalRole.Entry;
            controller.UpdateBlocks();var signal=controller.Signals[0];var bogie=new Bogie();bogie.traveller.Span=mast-20*sign;approach.bogies.Add(bogie);
            check("Endpoint entry excludes its approach from native extras / "+sign+" / "+diverging,!signal.Block.IsOccupied(CrossingCheckMode.Ignore));
            check("Endpoint entry opens with a waiting locomotive without reservation / "+sign+" / "+diverging,SignalRules.IsTrainPermission(DecisionPass.Evaluate(signal,SignalRole.Entry)));
            var previous=new TrackSignalController(new(),approach,travel,new(approach,face,(int)(mast-50*sign),mast-50*sign));approach.Ends[travel]=controller;previous.UpdateBlocks();
            check("Endpoint entry does not reserve the preceding throat / "+sign+" / "+diverging,!AutomaticBlock.Conflicts(signal.Block,previous.Signals[0].Block)&&!AutomaticBlock.Conflicts(previous.Signals[0].Block,signal.Block));
            TrackChecker.Mark(other);check("Other fouled turnout leg still stops entry",signal.Block.IsOccupied(CrossingCheckMode.Ignore));TrackChecker.Clear(other);
            bogie.traveller.Span=10;branch.bogies.Add(bogie);check("Forward turnout occupancy still stops entry",DecisionPass.Evaluate(signal,SignalRole.Entry)==Indication.Stop);branch.bogies.Clear();
            var branchHead=new TrackSignalController(new(),branch,TrackDirection.Out,new(branch,TrackDirection.In,10,10));branchHead.UpdateBlocks();
            check("Mast on outgoing turnout leg excludes the common approach behind it",!branchHead.Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore));
        }
        SignalsMod.Settings.SpecialReservation=false;
        foreach(int sign in new[]{1,-1})
        {
            AutomaticBlock.Reset();TrackReserver.ClearAll();SignalManager.Instance=new();
            var track=new RailTrack(500);var other=new RailTrack(100);var travel=sign==1?TrackDirection.Out:TrackDirection.In;double mast=sign==1?100:400;
            var controller=new TrackSignalController(new(),track,travel,new(track,AutomaticBlock.TravelDirection(travel),(int)mast,mast));controller.UpdateBlocks();
            var points=new TrackChecker.TrackIntersectionPoints(track);var datum=new UnityEngine.Transform{position=new(0,0,(float)(mast-30*sign))};points.IntersectionPoints.Add((other,datum));other.bogies.Add(new Bogie());
            var map=new Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints>{{track,points}};AutomaticBlock.MapBuilt(map);
            check("Crossing before owning mast cannot close forward interval / "+sign,!controller.Signals[0].Block.IsOccupied(CrossingCheckMode.IntersectionOnly));
            var reverse=new TrackChecker.TrackIntersectionPoints(other);reverse.IntersectionPoints.Add((track,datum));map.Add(other,reverse);
            var crossingBlock=TrackBlock.CreateUntilMainSignal(other,TrackDirection.Out);
            check("Crossing before mast does not reserve unrelated forward interval in either order / "+sign,!AutomaticBlock.Conflicts(controller.Signals[0].Block,crossingBlock)&&!AutomaticBlock.Conflicts(crossingBlock,controller.Signals[0].Block));
            datum.position=new(0,0,(float)(mast+30*sign));AutomaticBlock.MapBuilt(map);
            check("Rebuilt forward crossing retains fouling protection / "+sign,controller.Signals[0].Block.IsOccupied(CrossingCheckMode.IntersectionOnly));
            check("Crossing after mast reserves conflict in either order / "+sign,AutomaticBlock.Conflicts(controller.Signals[0].Block,crossingBlock)&&AutomaticBlock.Conflicts(crossingBlock,controller.Signals[0].Block));
            check("Native ignore-crossings mode remains effective / "+sign,!controller.Signals[0].Block.IsOccupied(CrossingCheckMode.Ignore));
        }
        foreach(int sign in new[]{1,-1})
        {
            AutomaticBlock.Reset();TrackReserver.ClearAll();SignalManager.Instance=new();SignalsMod.Settings.SpecialReservation=false;
            var line=new RailTrack(8000);RailTrackRegistryBase.RailTracks=new[]{line};var travel=sign==1?TrackDirection.Out:TrackDirection.In;
            TrackSignalController Head(TrackDirection d,double span)=>new(new(),line,d,new(line,AutomaticBlock.TravelDirection(d),(int)span,span));
            var entry=Head(travel,sign==1?100:7900);var next=Head(travel,sign==1?7900:100);
            var opposing=Head(AutomaticBlock.TravelDirection(travel),sign==1?7900:100);var back=Head(AutomaticBlock.TravelDirection(travel),sign==1?100:7900);
            line.Ends[travel]=next;line.Ends[AutomaticBlock.TravelDirection(travel)]=back;
            foreach(var aspect in entry.Signals[0].AllAspects)((RuAspectDefinition)aspect.GetDefinition()).Role=SignalRole.Entry;
            AutomaticBlock.Generate();foreach(var c in SignalManager.Instance.AllControllers)c.UpdateBlocks();
            var bogie=new Bogie();bogie.traveller.Span=sign==1?50:7950;line.bogies.Add(bogie);UnityEngine.Time.frameCount++;
            check("Initially stationary train behind entry does not lock its forward direction / "+sign,SignalRules.IsTrainPermission(DecisionPass.Evaluate(entry.Signals[0],SignalRole.Entry)));
            check("Unknown stationary train ahead still blocks the opposite approach / "+sign,DecisionPass.Evaluate(opposing.Signals[0],SignalRole.Through)==Indication.Stop);
            bogie.traveller.Span=sign==1?150:7850;UnityEngine.Time.frameCount++;
            check("Stationary train crossing entry still occupies its first block / "+sign,DecisionPass.Evaluate(entry.Signals[0],SignalRole.Entry)==Indication.Stop);
        }
    }
}
