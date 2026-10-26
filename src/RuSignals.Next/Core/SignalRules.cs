namespace RuSignals;

// These enum values and aspect IDs form the compatibility boundary with saved
// native definitions and cab adapters. The decision engine itself is stateless.
public enum SignalRole { Through, Entry, Exit, Route, Distant, Repeater, Shunting, Turntable, BufferStop }
public enum Indication { Stop, Yellow, FlashingYellow, DoubleYellow, DoubleYellowUpperFlashing, Green, ShuntingWhite, Invitation, Dark }
public enum NextPermission { Stop, Normal, Reduced }
public struct SignalState
{
    public bool BlockReady,RouteBlocked,ReservedByOther,ReservationRequired,Occupied;
    public bool Diverging,Shunting,ShuntingAllowed,Connected,NextIsEntry,DepartureFromSide,TerminalArrival;
    public NextPermission Next;
    // A movement authority and an advance indication are separate inputs.
    public bool TrainRoute=>BlockReady&&!RouteBlocked&&!ReservedByOther&&!ReservationRequired&&!Occupied&&!Shunting;
}
public static class SignalRules
{
    private static readonly string[] Ids={"STOP","CAUTION","CAUTION_FLASHING","DIVERGING_CAUTION","DIVERGING_OPEN","OPEN","SHUNTING_OPEN","INVITATION","DARK"};
    public static string AspectId(Indication value)=>(uint)value<Ids.Length?Ids[(int)value]:"UNKNOWN";
    public static bool IsTrainPermission(Indication value)=>value>=Indication.Yellow&&value<=Indication.Green;
    public static NextPermission Permission(Indication value)=>value==Indication.DoubleYellow||value==Indication.DoubleYellowUpperFlashing
        ?NextPermission.Reduced:IsTrainPermission(value)?NextPermission.Normal:NextPermission.Stop;
    public static float PassingSpeed(Indication value)=>Permission(value)==NextPermission.Reduced?40:value==Indication.Invitation?20:value==Indication.ShuntingWhite?25:0;
    public static Indication Select(SignalRole role,SignalState s)
    {
        if(role==SignalRole.BufferStop)return Indication.Stop;
        if(role==SignalRole.Turntable)return s.Connected?Indication.ShuntingWhite:Indication.Stop;
        if(role==SignalRole.Repeater)return s.Next==NextPermission.Stop?Indication.Dark:Indication.Green;
        if(role==SignalRole.Distant)return Advance(s.Next);
        // No colour of the next signal can bypass an unproved current route.
        if(!s.BlockReady||s.RouteBlocked||s.ReservedByOther||s.ReservationRequired)return Indication.Stop;
        if(role==SignalRole.Shunting||s.Shunting)return Manoeuvre(s);
        if(!s.TrainRoute)return Indication.Stop;
        switch(role)
        {
            case SignalRole.Through:return ThreeAspectBlock(s);
            case SignalRole.Entry:return Arrival(s);
            case SignalRole.Route:return StationRoute(s);
            case SignalRole.Exit:return Departure(s);
            default:return Indication.Stop;
        }
    }
    private static Indication Manoeuvre(SignalState s)=>s.ShuntingAllowed?Indication.ShuntingWhite:Indication.Stop;
    private static Indication ThreeAspectBlock(SignalState s)=>Advance(s.Next);
    // A proved reception route may end at a buffer stop without another train
    // signal. Admit it restrictively; the absence of a next signal is not a
    // reason to demand a through route or advertise an unrestricted departure.
    private static Indication Arrival(SignalState s)=>s.TerminalArrival?Indication.DoubleYellow:s.Diverging?Turnout(s.Next):Advance(s.Next);
    private static Indication StationRoute(SignalState s)=>s.Diverging?Turnout(s.Next):Advance(s.Next);
    // Warn of the next reduced train indication on every straight route,
    // including exits. A side-track turnout's physical limit is independent.
    private static Indication Departure(SignalState s)=>!s.DepartureFromSide&&s.Diverging?Turnout(s.Next):Advance(s.Next);
    private static Indication Advance(NextPermission next)=>next==NextPermission.Stop?Indication.Yellow:next==NextPermission.Reduced?Indication.FlashingYellow:Indication.Green;
    private static Indication Turnout(NextPermission next)=>next==NextPermission.Stop?Indication.DoubleYellow:Indication.DoubleYellowUpperFlashing;
}
