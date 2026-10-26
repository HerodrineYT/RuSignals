using Signals.Common.Displays;
using Signals.Game.Displays;
namespace RuSignals;
using Signal=global::Signals.Game.Signal;

public sealed class NameplateDefinition:DisplayBaseDefinition
{
    public NameplateView Plate;
}
public sealed class NameplateDisplay:DisplayBase<NameplateDefinition>
{
    public NameplateDisplay(DisplayBaseDefinition definition,Signal signal):base(definition,signal){}
    public override void UpdateDisplay()
    {
        DisplayText=SignalDesignations.Get(Signal);
        Definition.Plate.Show(DisplayText);
    }
}
