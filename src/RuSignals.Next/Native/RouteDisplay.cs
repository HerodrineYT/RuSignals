using Signals.Common.Displays;
using Signals.Game;
using Signals.Game.Displays;
using Signals.Game.Railway;
using UnityEngine;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
public sealed class RouteDisplayDefinition:DisplayBaseDefinition
{
    public SignalRole Role;public bool Small;public GameObject Visual;public RoutePanel Panel;
}
public sealed class RouteDisplay:DisplayBase<RouteDisplayDefinition>,IHudDisplayable
{
    private readonly TrackInfo[] departure=new TrackInfo[1];
    private ulong lastMask=ulong.MaxValue;
    private string hudSource,hudText="";
    string IHudDisplayable.DisplayText
    {
        get
        {
            string text=DisplayText;
            if(text!=hudSource){hudSource=text;hudText=string.IsNullOrEmpty(text)?"":"<size=125%>"+text+"</size>";}
            return hudText;
        }
    }
    public RouteDisplay(DisplayBaseDefinition definition,Signal signal):base(definition,signal){signal.AspectChanged+=Changed;}
    private void Changed(Signals.Game.Aspects.IAspect _)=>UpdateDisplay();
    public override void UpdateDisplay()
    {
        bool visible=RouteBoardRules.Enabled(Definition.Role,Definition.Small,Signal.IsShunting,Controller.GroupJunction!=null);
        if(Definition.Visual!=null&&Definition.Visual.activeSelf!=visible)Definition.Visual.SetActive(visible);
        string text="";var block=Signal.Block;
        if(visible&&!Signal.IsOff&&Signal.CurrentAspect?.GetDefinition() is RuAspectDefinition aspect&&SignalRules.IsTrainPermission(aspect.Indication)&&
            block!=null&&!block.ShouldBeUpdated&&!block.IsSelfLoop&&DecisionPass.Evaluate(Signal,aspect.Role)==aspect.Indication)
        {
            if(RouteDirection.OutsideStation(Controller.GroupJunction))text=RouteDirection.ForRoute(block,Controller.GroupJunction);
            else
            {
                if(Definition.Role==SignalRole.Exit&&Controller.PlacementInfo is SignalPlacementInfo placement)
                {departure[0]=new TrackInfo(placement.Track,placement.Direction);text=RouteSymbols.Number(TrackUtils.NextTrackTrimmedNumber(departure));}
                if(text.Length==0)text=RouteSymbols.Number(AutomaticBlock.RouteTrackNumber(block));
            }
        }
        DisplayText=text;ulong mask=RouteSymbols.Mask(text,Definition.Small);
        if(mask==lastMask)return;lastMask=mask;Definition.Panel.Show(mask);
    }
}
