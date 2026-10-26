using System;
namespace RuSignals;

[Flags] internal enum LampMask { None=0,Red=1,Yellow=2,Yellow2=4,Green=8,MoonWhite=16,Blue=32 }
internal readonly struct LampPattern
{
    internal readonly LampMask Steady,Blinking;
    internal LampPattern(LampMask steady,LampMask blinking=LampMask.None){Steady=steady;Blinking=blinking;}
}
// The aspect catalogue is shared by model generation and the rule checks.
// Roles and existing native IDs retain their saved-game/API identities.
internal static class SignalCatalogue
{
    private static readonly Indication[] block={Indication.Stop,Indication.Yellow,Indication.FlashingYellow,Indication.Green};
    private static readonly Indication[] station={Indication.Stop,Indication.Invitation,Indication.DoubleYellow,Indication.DoubleYellowUpperFlashing,Indication.Yellow,Indication.FlashingYellow,Indication.Green,Indication.ShuntingWhite};
    private static readonly Indication[] warning={Indication.Yellow,Indication.FlashingYellow,Indication.Green};
    private static readonly Indication[] repeat={Indication.Green},manoeuvre={Indication.Stop,Indication.ShuntingWhite},buffer={Indication.Stop};
    internal static Indication[] Profile(SignalRole role)
    {
        switch(role)
        {
            case SignalRole.Through:return block;
            case SignalRole.Entry:case SignalRole.Route:case SignalRole.Exit:return station;
            case SignalRole.Distant:return warning;
            case SignalRole.Repeater:return repeat;
            case SignalRole.Shunting:case SignalRole.Turntable:return manoeuvre;
            default:return buffer;
        }
    }
    internal static LampPattern Pattern(SignalRole role,Indication value)
    {
        switch(value)
        {
            case Indication.Stop:return new LampPattern(role==SignalRole.Shunting?LampMask.Blue:LampMask.Red);
            case Indication.Yellow:return new LampPattern(LampMask.Yellow);
            case Indication.FlashingYellow:return new LampPattern(LampMask.None,LampMask.Yellow);
            case Indication.DoubleYellow:return new LampPattern(LampMask.Yellow|LampMask.Yellow2);
            case Indication.DoubleYellowUpperFlashing:return new LampPattern(LampMask.Yellow2,LampMask.Yellow);
            case Indication.Green:return new LampPattern(LampMask.Green);
            case Indication.ShuntingWhite:return new LampPattern(LampMask.MoonWhite);
            case Indication.Invitation:return new LampPattern(LampMask.Red,LampMask.MoonWhite);
            default:return new LampPattern(LampMask.None);
        }
    }
    internal static readonly LampMask[] Lenses={LampMask.Red,LampMask.Yellow,LampMask.Yellow2,LampMask.Green,LampMask.MoonWhite,LampMask.Blue};
}
