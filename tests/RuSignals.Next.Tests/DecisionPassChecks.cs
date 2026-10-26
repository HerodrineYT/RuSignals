using RuSignals;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
internal static class DecisionPassChecks
{
    internal static void Run(Action<string,bool> check)
    {
        AutomaticBlock.Reset();TrackReserver.ClearAll();SignalManager.Instance=new();SignalsMod.Settings.SpecialReservation=false;
        var track=new RailTrack(1000);
        var occupiedBogie=new Bogie();occupiedBogie.traveller.Span=50;
        var controller=new TrackSignalController(new(),track,TrackDirection.Out,new(track,TrackDirection.In,10,10));controller.UpdateBlocks();var signal=controller.Signals[0];
        long reads=SignalReader.Reads;
        DecisionPass.Begin(signal,out var outer);
        try
        {
            foreach(var aspect in signal.AllAspects)((RuAspect)aspect).MeetsConditions();
            check("new engine reads live state once for all aspect candidates",SignalReader.Reads==reads+1);
            check("new scoped engine preserves yellow at end of route",DecisionPass.Evaluate(signal,SignalRole.Through)==Indication.Yellow);
            var otherTrack=new RailTrack(100);var otherController=new TrackSignalController(new(),otherTrack,TrackDirection.Out,new(otherTrack,TrackDirection.In,0,0));otherController.UpdateBlocks();
            var other=otherController.Signals[0];DecisionPass.Begin(other,out var nested);
            try{check("nested native pass has independent decision",DecisionPass.Evaluate(other,SignalRole.Through)==Indication.Yellow);}
            finally{DecisionPass.End(nested);}
            // SetBlock during nested work invalidates the restored parent too.
            reads=SignalReader.Reads;DecisionPass.Evaluate(signal,SignalRole.Through);
            check("nested mutation invalidates saved outer decision",SignalReader.Reads==reads+1);
            track.bogies.Add(occupiedBogie);DecisionPass.Invalidate();
            check("event invalidation closes a formerly clear cached decision",DecisionPass.Evaluate(signal,SignalRole.Through)==Indication.Stop);
            track.bogies.Clear();DecisionPass.Invalidate();
            check("event invalidation reopens after occupancy clears",DecisionPass.Evaluate(signal,SignalRole.Through)==Indication.Yellow);
        }
        finally{DecisionPass.End(outer);}
        track.bogies.Add(occupiedBogie);
        check("no permission cache survives native pass exit",DecisionPass.Evaluate(signal,SignalRole.Through)==Indication.Stop);
        track.bogies.Clear();
        check("external same-frame query sees newly cleared track",DecisionPass.Evaluate(signal,SignalRole.Through)==Indication.Yellow);
        DecisionPass.Begin(signal,out var previous);
        try{DecisionPass.Evaluate(signal,SignalRole.Through);throw new InvalidOperationException("simulated native failure");}
        catch(InvalidOperationException){}
        finally{DecisionPass.End(previous);}
        track.bogies.Add(occupiedBogie);
        check("exception cleanup does not leak previous decision",DecisionPass.Evaluate(signal,SignalRole.Through)==Indication.Stop);
        track.bogies.Clear();
        reads=SignalReader.Reads;DecisionPass.Begin(signal,out previous);
        try{DecisionPass.Evaluate(signal,SignalRole.Through);DecisionPass.Evaluate(signal,SignalRole.Repeater);check("different profile cannot reuse another role's decision",SignalReader.Reads==reads+2);}
        finally{DecisionPass.End(previous);}
        check("public aspect IDs remain stable",SignalRules.AspectId(Indication.Stop)=="STOP"&&SignalRules.AspectId(Indication.Green)=="OPEN"&&SignalRules.AspectId(Indication.Invitation)=="INVITATION");
    }
}
