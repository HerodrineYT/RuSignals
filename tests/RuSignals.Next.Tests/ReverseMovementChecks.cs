using RuSignals;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using UnityEngine;
internal static class ReverseMovementChecks
{
    internal static void Run(Action<string,bool> check)
    {
        foreach(var role in new[]{SignalRole.Through,SignalRole.Entry,SignalRole.Exit,SignalRole.Route})foreach(int sign in new[]{1,-1})
        {
            AutomaticBlock.Reset();TrackReserver.ClearAll();SignalManager.Instance=new();SignalsMod.Settings.SpecialReservation=false;
            var line=new RailTrack(8000);RailTrackRegistryBase.RailTracks=new[]{line};var travel=sign==1?TrackDirection.Out:TrackDirection.In;
            TrackSignalController Head(TrackDirection d,double span)=>new(new(),line,d,new(line,AutomaticBlock.TravelDirection(d),(int)span,span));
            double mast=sign==1?100:7900;var owner=Head(travel,mast);var end=Head(travel,sign==1?7900:100);var back=Head(AutomaticBlock.TravelDirection(travel),mast);
            var oppositeFollower=Head(AutomaticBlock.TravelDirection(travel),sign==1?7900:100);
            line.Ends[travel]=end;line.Ends[AutomaticBlock.TravelDirection(travel)]=back;
            foreach(var aspect in owner.Signals[0].AllAspects)((RuAspectDefinition)aspect.GetDefinition()).Role=role;
            AutomaticBlock.Generate();foreach(var c in SignalManager.Instance.AllControllers)c.UpdateBlocks();
            var front=new Bogie();var rear=new Bogie();line.bogies.Add(front);line.bogies.Add(rear);
            void Move(double first,double last,int movement)
            {
                front.traveller.Span=mast+first*sign;rear.traveller.Span=mast+last*sign;
                front.rb.velocity=rear.rb.velocity=new(0,0,movement*sign);Time.frameCount++;
            }
            bool Open()=>SignalRules.IsTrainPermission(DecisionPass.Evaluate(owner.Signals[0],role));
            string name=role+" / "+sign;
            Move(-20,-40,1);check(name+" clear route before first entry opens",Open());
            for(int cycle=0;cycle<3;cycle++)
            {
                Move(30,10,1);check(name+" occupied route closes / "+cycle,!Open());
                Move(1,-19,-1);check(name+" remaining bogie beyond mast holds red / "+cycle,!Open());
                check(name+" consistent reverse motion does not latch the free following block / "+cycle,SignalRules.IsTrainPermission(DecisionPass.Evaluate(oppositeFollower.Signals[0],SignalRole.Entry)));
                Move(-5,-25,-1);check(name+" fully reversing out reopens without emptying physical corridor / "+cycle,Open());
                Move(-5,-25,0);check(name+" stopping after retreat leaves route available / "+cycle,Open());
                Move(-5,-25,1);check(name+" changing direction for reentry does not latch red / "+cycle,Open());
            }
            var obstruction=new Bogie();obstruction.traveller.Span=mast+40*sign;line.bogies.Add(obstruction);Time.frameCount++;
            check(name+" another car in the forward block keeps red",!Open());line.bogies.Remove(obstruction);Time.frameCount++;
            check(name+" removing forward car releases route with approach still occupied",Open());
            TrackChecker.Mark(line);Time.frameCount++;check(name+" manual closure still stops a reversed-out train",!Open());TrackChecker.Clear(line);Time.frameCount++;
            check(name+" clearing manual closure releases route without unloading train",Open());
            var other=Head(travel,mast);other.UpdateBlocks();check(name+" forward route can be explicitly reserved",TrackReserver.ReserveForSignal(other.Signals[0]));
            check(name+" foreign reservation still stops entry",!Open());TrackReserver.ClearFromSignal(other.Signals[0]);Time.frameCount++;
            check(name+" releasing foreign reservation restores gameplay route",Open());
            obstruction.traveller.Span=mast+2500*sign;obstruction.rb.velocity=new(0,0,-sign);line.bogies.Add(obstruction);Time.frameCount++;
            check(name+" simultaneous opposing movement ahead remains restrictive",!Open());line.bogies.Remove(obstruction);Time.frameCount++;
            check(name+" live movement conflict clears without emptying approach",Open());
        }
    }
}
