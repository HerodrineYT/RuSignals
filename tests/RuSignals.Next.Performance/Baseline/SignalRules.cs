namespace RuSignals;

public enum SignalRole { Through, Entry, Exit, Route, Distant, Repeater, Shunting, Turntable, BufferStop }
public enum Indication { Stop, Yellow, FlashingYellow, DoubleYellow, DoubleYellowUpperFlashing, Green, ShuntingWhite, Invitation, Dark }
public enum NextPermission { Stop, Normal, Reduced }

// No Unity dependencies: the same rules are exercised by the scenario runner.
public struct SignalState
{
    public bool BlockReady, RouteBlocked, ReservedByOther, ReservationRequired, Occupied;
    public bool Diverging, Shunting, ShuntingAllowed, Connected, NextIsEntry;
    public NextPermission Next;
}

public static class SignalRules
{
    // Keep aspect IDs stable for consumers of DVSignals Signal.CurrentAspect.
    // API bridges and individual consumers may support different ID sets.
    public static string AspectId(Indication indication) => indication switch
    {
        Indication.Stop => "STOP",
        Indication.Green => "OPEN",
        Indication.Yellow => "CAUTION",
        Indication.FlashingYellow => "CAUTION_FLASHING",
        Indication.DoubleYellow => "DIVERGING_CAUTION",
        Indication.DoubleYellowUpperFlashing => "DIVERGING_OPEN",
        Indication.ShuntingWhite => "SHUNTING_OPEN",
        Indication.Invitation => "INVITATION",
        Indication.Dark => "DARK",
        _ => "UNKNOWN"
    };

    public static bool IsTrainPermission(Indication indication) =>
        indication == Indication.Green || indication == Indication.Yellow ||
        indication == Indication.FlashingYellow || indication == Indication.DoubleYellow ||
        indication == Indication.DoubleYellowUpperFlashing;

    public static Indication Select(SignalRole role, SignalState state)
    {
        if (role == SignalRole.BufferStop) return Indication.Stop;
        if (role == SignalRole.Turntable) return state.Connected ? Indication.ShuntingWhite : Indication.Stop;
        if (role == SignalRole.Repeater) return state.Next == NextPermission.Stop ? Indication.Dark : Indication.Green;
        if (role == SignalRole.Distant) return Approach(state.Next);
        if (!state.BlockReady || state.RouteBlocked || state.ReservedByOther || state.ReservationRequired)
            return Indication.Stop;
        if (role == SignalRole.Shunting || state.Shunting)
            return state.ShuntingAllowed ? Indication.ShuntingWhite : Indication.Stop;
        if (state.Occupied) return Indication.Stop;
        // Through signals have three lenses; junctions use the Route profile.
        if (role != SignalRole.Through && state.Diverging)
            return state.Next == NextPermission.Stop ? Indication.DoubleYellow : Indication.DoubleYellowUpperFlashing;
        if (state.Next == NextPermission.Stop) return Indication.Yellow;
        if (state.Next == NextPermission.Reduced &&
            (role == SignalRole.Entry || role == SignalRole.Route || (role == SignalRole.Through && state.NextIsEntry)))
            return Indication.FlashingYellow;
        return Indication.Green;
    }

    private static Indication Approach(NextPermission next) => next == NextPermission.Stop ? Indication.Yellow :
        next == NextPermission.Reduced ? Indication.FlashingYellow : Indication.Green;

    public static NextPermission Permission(Indication indication)
    {
        switch (indication)
        {
            case Indication.DoubleYellow:
            case Indication.DoubleYellowUpperFlashing: return NextPermission.Reduced;
            case Indication.Green:
            case Indication.Yellow:
            case Indication.FlashingYellow: return NextPermission.Normal;
            // A white shunting or invitation light is not an open train route.
            default: return NextPermission.Stop;
        }
    }

    public static float PassingSpeed(Indication indication) =>
        indication == Indication.DoubleYellow || indication == Indication.DoubleYellowUpperFlashing ? 40f :
        indication == Indication.Invitation ? 20f : indication == Indication.ShuntingWhite ? 25f : 0f;
}
