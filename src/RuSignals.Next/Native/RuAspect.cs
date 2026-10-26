using Signals.Common.Aspects;
using Signals.Game;
using Signals.Game.Aspects;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
public sealed class RuAspectDefinition:AspectBaseDefinition
{
    public SignalRole Role;public Indication Indication;public bool ManualOnly,AutoShunting,Mechanical;
}
public sealed class RuAspect:AspectBase<RuAspectDefinition>
{
    public RuAspect(AspectBaseDefinition definition,Signal signal):base(definition,signal){}
    public override bool MeetsConditions()=>!Definition.ManualOnly&&DecisionPass.Evaluate(Signal,Definition.Role)==Definition.Indication;
}
