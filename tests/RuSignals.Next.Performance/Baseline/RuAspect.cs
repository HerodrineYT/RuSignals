using Signals.Common;
using Signals.Common.Aspects;
using Signals.Game;
using Signals.Game.Aspects;
using Signals.Game.Controllers;
using Signals.Game.Railway;

namespace RuSignals;

public sealed class RuAspectDefinition : AspectBaseDefinition
{
    public SignalRole Role;
    public Indication Indication;
    public bool ManualOnly;
}

public sealed class RuAspect : AspectBase<RuAspectDefinition>
{
    public RuAspect(AspectBaseDefinition definition, Signals.Game.Signal signal) : base(definition, signal) { }

    public override bool MeetsConditions() => !Definition.ManualOnly &&
        SignalRules.Select(Definition.Role, ReadState()) == Definition.Indication;

    private SignalState ReadState()
    {
        var state = new SignalState { Shunting = Signal.IsShunting || HasManualShuntingRequest(), ShuntingAllowed = Signal.ShuntingAllowed };
        if (Definition.Role == SignalRole.BufferStop) return state;
        if (Definition.Role == SignalRole.Turntable)
        {
            state.Connected = Controller is TurntableSignalController table && table.IsConnected;
            return state;
        }
        ReadNext(ref state);
        if (Definition.Role == SignalRole.Distant || Definition.Role == SignalRole.Repeater) return state;
        var block = Block;
        state.BlockReady = block != null && !block.ShouldBeUpdated && block.Tracks.Length != 0;
        if (!state.BlockReady) return state;
        state.RouteBlocked = block.IsSelfLoop || !AutomaticBlock.DirectionAllowed(Signal);
        var requiredBranch = Controller.RequiredJunctionBranch;
        var groupJunction = Controller.GroupJunction;
        if (requiredBranch.HasValue && (groupJunction == null ||
            requiredBranch.Value != groupJunction.selectedBranch))
            state.RouteBlocked = true;
        state.ReservedByOther = TrackReserver.IsSignalReservedByAnother(Signal);
        state.ReservationRequired = RequiresReservation(Signal);
        state.Occupied = !state.Shunting && Definition.Role != SignalRole.Shunting && block.IsOccupied(CrossingCheckMode.IntersectionOnly);
        // Inspect the route actually traversed, including trailing-point approaches.
        // GroupJunction.selectedBranch alone can describe a different branch.
        foreach (var info in block.Tracks)
        {
            if (!info.IsJunctionTrack) continue;
            var junction = info.Track.inJunction;
            if (junction == null || junction.selectedBranch < 0 || junction.selectedBranch >= junction.outBranches.Count)
            {
                state.RouteBlocked = true;
                continue;
            }
            // A trailing-point approach still has to follow the selected
            // branch. TrackWalker includes unaligned inbound branch tracks;
            // ignoring them would clear competing exits into the same merge.
            // Checking outbound tracks also protects a stale block after a switch change.
            if (junction.outBranches[junction.selectedBranch].track != info.Track)
                state.RouteBlocked = true;
            // Honour branch requirements on controllers encountered inside the
            // block, using the same direction selection as RequiredPathAspect.
            if (SignalManager.Instance.TryGetJunctionGroup(junction, out var group))
            {
                BasicSignalController routeController;
                if (info.Direction == TrackDirection.Out) routeController = group.JunctionSignal;
                else routeController = group.TryGetControllerForTrack(info.Track, out var branchController)
                    ? branchController : group.ReverseJunctionSignal;
                if (routeController?.RequiredJunctionBranch is int branch && branch != junction.selectedBranch)
                    state.RouteBlocked = true;
            }
            // Same through-track identity used by DVSignals 1.1.3.
            if (info.Track.name != "[track through]")
                state.Diverging = true;
        }
        return state;
    }

    // Match DVSignals' optional SpecialRequireReservationAspect. Generated AB
    // signals cannot be reserved and must keep working automatically.
    private static bool RequiresReservation(Signals.Game.Signal signal) =>
        !AutomaticBlock.IsSignal(signal) && SignalsMod.Settings.SpecialReservation &&
        !TrackReserver.HasReservation(signal);

    private bool HasManualShuntingRequest()
    {
        // SemiManual with override -1 is the safe request used by our menu:
        // it lets this aspect be selected only when the normal route checks
        // below actually produce SHUNTING_OPEN.
        return Signal.Operation == SignalOperationMode.SemiManual && Signal.ManualOverrideAspect < 0;
    }

    private void ReadNext(ref SignalState state)
    {
        state.Next = NextPermission.Stop;
        var nextController = Signal.GetNextController();
        if (nextController == null || nextController == Controller) return;
        state.NextIsEntry = nextController.Type == SignalType.Entry;
        var next = nextController.GetControllerSignal();
        var aspect = next?.CurrentAspect;
        if (next == null || next.IsShunting || aspect == null || aspect.DisallowPassing ||
            next.Block == null || next.Block.ShouldBeUpdated) return;
        var definition = aspect.GetDefinition();
        if (definition is RuAspectDefinition ru)
        {
            // Repeater lamps are indications only, never a clear block boundary.
            if (ru.Role == SignalRole.Repeater || ru.Role == SignalRole.Distant) return;
            // Controllers update sequentially. Do not trust a previous green
            // after a train entered its block or a conflicting reserve appeared.
            if(next.Block.IsOccupied(CrossingCheckMode.IntersectionOnly) ||
                TrackReserver.IsSignalReservedByAnother(next) || !AutomaticBlock.DirectionAllowed(next) ||
                RequiresReservation(next))return;
            state.Next = SignalRules.Permission(ru.Indication);
        }
        else state.Next = definition.UsePassingSpeed ? NextPermission.Reduced : NextPermission.Normal;
    }
}
