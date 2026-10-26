using System;
using Signals.Game;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;

// A scope is exactly one synchronous native UpdateAspect invocation. It is NOT
// a frame cache. Mutation/event boundaries invalidate even a nested saved scope.
internal static class DecisionPass
{
    internal struct Scope
    {
        internal Signal Signal;
        internal SignalRole Role;
        internal Indication Result;
        internal bool Valid;
        internal long Revision;
    }
    [ThreadStatic] private static Scope current;
    [ThreadStatic] private static long revision;
    internal static void Begin(Signal signal,out Scope previous){previous=current;current=new Scope{Signal=signal};}
    internal static void End(Scope previous){current=previous;}
    internal static void Invalidate(){unchecked{revision++;}current.Valid=false;}
    internal static Indication Evaluate(Signal signal,SignalRole role)
    {
        if(ReferenceEquals(current.Signal,signal)&&current.Valid&&current.Role==role&&current.Revision==revision)return current.Result;
        long before=revision;
        Indication value=SignalRules.Select(role,SignalReader.Read(signal,role));
        if(ReferenceEquals(current.Signal,signal)&&before==revision)
        {current.Role=role;current.Result=value;current.Revision=revision;current.Valid=true;}
        return value;
    }
}
