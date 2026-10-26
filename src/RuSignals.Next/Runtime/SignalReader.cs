using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
internal static class SignalReader
{
    internal static long Reads;
    internal static SignalState Read(Signal signal,SignalRole role)
    {
        Reads++;
        var s=new SignalState{Shunting=signal.IsShunting||(signal.Operation==SignalOperationMode.SemiManual&&signal.ManualOverrideAspect<0),ShuntingAllowed=signal.ShuntingAllowed};
        var controller=signal.Controller;
        // The SDK generator marks side/storage departure tracks as Exit;
        // ExitMainline/ExitPax retain the main-track turnout indications.
        s.DepartureFromSide=role==SignalRole.Exit&&controller.PrefabType==PrefabType.Exit
            &&!(signal.Definition.Aspects[0] is RuAspectDefinition model&&model.Mechanical);
        if(role==SignalRole.BufferStop)return s;
        if(role==SignalRole.Turntable){s.Connected=controller is TurntableSignalController table&&table.IsConnected;return s;}
        if(role!=SignalRole.Repeater&&role!=SignalRole.Distant)
        {
            var block=signal.Block;
            s.BlockReady=Ready(block);
            if(!s.BlockReady)return s;
            s.RouteBlocked=Blocked(signal,block);
            if(s.RouteBlocked)return s;
            s.ReservationRequired=NeedsReservation(signal,role);
            s.ReservedByOther=TrackReserver.IsSignalReservedByAnother(signal);
            if(s.ReservationRequired||s.ReservedByOther)return s;
            s.TerminalArrival=role==SignalRole.Entry&&block.IsDeadEnd&&block.NextController==null;
            if(role==SignalRole.Exit&&!s.Shunting&&signal.Definition.Aspects[0] is RuAspectDefinition combined&&combined.AutoShunting)
            {
                // The native train block skips shunting/spacing signals. Its Station
                // is the FIRST yard encountered, not the route destination.
                var route=AutomaticBlock.FullRoute(block);var end=route.NextController;
                string origin=SignalDesignations.Station(controller);
                string destination=end!=null?SignalDesignations.Station(end):route.Station;
                bool localBoundary=end!=null&&end.Type!=SignalType.Entry&&end!=controller;
                s.Shunting=CombinedExitRules.Shunting(origin,destination,route.IsDeadEnd,localBoundary,route.Length);
            }
            s.Occupied=!s.Shunting&&role!=SignalRole.Shunting&&block.IsOccupied(CrossingCheckMode.IntersectionOnly);
            if(s.Occupied||s.Shunting||role==SignalRole.Shunting)return s;
            foreach(var track in block.Tracks)if(track.IsJunctionTrack&&JunctionRouteRules.Diverging(track.Track.name)){s.Diverging=true;break;}
        }
        var nextController=signal.GetNextController();
        s.NextIsEntry=nextController!=null&&nextController.Type==SignalType.Entry;
        if(nextController!=null&&nextController!=controller)s.Next=ReadPermission(nextController.GetControllerSignal());
        return s;
    }
    // Only train reception becomes automatic; native shunting reservations stay explicit.
    private static bool NeedsReservation(Signal signal,SignalRole? role)=>SignalsMod.Settings.SpecialReservation
        &&(role!=SignalRole.Entry||signal.IsShunting||signal.Operation==SignalOperationMode.SemiManual&&signal.ManualOverrideAspect<0)
        &&!AutomaticBlock.IsSignal(signal)&&!TrackReserver.HasReservation(signal);
    private static bool Ready(TrackBlock block)=>block!=null&&block.Tracks.Length>0&&!block.ShouldBeUpdated;
    private static bool Blocked(Signal signal,TrackBlock block)=>block.IsSelfLoop||!Aligned(signal.Controller,block)||!AutomaticBlock.DirectionAllowed(signal);
    internal static bool Aligned(BasicSignalController controller,TrackBlock block)
    {
        if(controller.RequiredJunctionBranch is int required&&(controller.GroupJunction==null||controller.GroupJunction.selectedBranch!=required))return false;
        foreach(var track in block.Tracks)
        {
            if(!track.IsJunctionTrack)continue;
            var junction=track.Track.inJunction;
            if(junction==null||junction.selectedBranch<0||junction.selectedBranch>=junction.outBranches.Count||junction.outBranches[junction.selectedBranch].track!=track.Track)return false;
            if(!SignalManager.Instance.TryGetJunctionGroup(junction,out var group))continue;
            var owner=track.Direction==TrackDirection.Out?group.JunctionSignal:group.TryGetControllerForTrack(track.Track,out var branch)?branch:group.ReverseJunctionSignal;
            if(owner?.RequiredJunctionBranch is int selected&&selected!=junction.selectedBranch)return false;
        }
        return true;
    }
    private static NextPermission ReadPermission(Signal next)
    {
        var aspect=next?.CurrentAspect;var block=next?.Block;
        if(next==null||next.IsOff||next.IsShunting||aspect==null||aspect.DisallowPassing||!Ready(block))return NextPermission.Stop;
        // Prove the home route even when its displayed aspect is stale or
        // supplied by another pack. Warning/repeater lamps grant no authority.
        var definition=aspect.GetDefinition() as RuAspectDefinition;
        if(Blocked(next,block)||NeedsReservation(next,definition?.Role)||TrackReserver.IsSignalReservedByAnother(next)||block.IsOccupied(CrossingCheckMode.IntersectionOnly))return NextPermission.Stop;
        if(definition==null)return aspect.GetDefinition().UsePassingSpeed?NextPermission.Reduced:NextPermission.Normal;
        if(definition.Role==SignalRole.Distant||definition.Role==SignalRole.Repeater||!SignalRules.IsTrainPermission(definition.Indication))return NextPermission.Stop;
        return SignalRules.Permission(definition.Indication);
    }
}
