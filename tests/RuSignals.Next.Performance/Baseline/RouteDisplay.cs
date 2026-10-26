using System;
using System.Linq;
using Signals.Common.Displays;
using Signals.Game;
using Signals.Game.Displays;
using Signals.Game.Railway;
using UnityEngine;

namespace RuSignals;

public sealed class RouteDisplayDefinition : DisplayBaseDefinition
{
    public MeshRenderer[] Lamps;
    public Material Lit, Unlit;
    public SignalRole Role;
    public bool Small;
    public GameObject Visual;
}
public sealed class RouteDisplay : DisplayBase<RouteDisplayDefinition>
{
    private ulong _mask=ulong.MaxValue;
    private bool _small;
    private TrackInfo[] _placementTrack;
    public RouteDisplay(DisplayBaseDefinition definition,Signals.Game.Signal signal):base(definition,signal) { signal.AspectChanged += _=>UpdateDisplay(); }
    public override void UpdateDisplay()
    {
        string text="";
        var aspect=Signal.CurrentAspect?.GetDefinition() as RuAspectDefinition;
        var block=Signal.Block;
        bool fitted=RouteBoardRules.Enabled(Definition.Role,Definition.Small,Signal.IsShunting,Controller.GroupJunction!=null);
        if(Definition.Visual!=null && Definition.Visual.activeSelf!=fitted)Definition.Visual.SetActive(fitted);
        bool open=fitted && aspect!=null && SignalRules.IsTrainPermission(aspect.Indication) && block!=null && !block.ShouldBeUpdated && !block.IsSelfLoop &&
            Signal.CurrentAspect is RuAspect current && current.MeetsConditions();
        if(open)
        {
            if(RouteDirection.OutsideStation(Controller.GroupJunction))
                text=RouteDirection.ForRoute(block,Controller.GroupJunction);
            else
            {
                if(Definition.Role==SignalRole.Exit && Controller.PlacementInfo is SignalPlacementInfo p)
                {
                    if(_placementTrack==null)_placementTrack=new TrackInfo[1];
                    _placementTrack[0]=new TrackInfo(p.Track,p.Direction);
                    text=RouteSymbols.Number(TrackUtils.NextTrackTrimmedNumber(_placementTrack));
                }
                if(text.Length==0)text=RouteSymbols.Number(AutomaticBlock.RouteTrackNumber(block));
            }
        }
        // Always evaluate the live route above; only the unchanged drawing can
        // be skipped. A switch/reservation may change without AspectChanged.
        if(_mask!=ulong.MaxValue && DisplayText==text && _small==Definition.Small)return;
        _small=Definition.Small;
        ulong mask=RouteSymbols.Mask(text,_small);
        DisplayText=text;
        if(mask==_mask)return;_mask=mask;
        for(int i=0;i<Definition.Lamps.Length;i++)Definition.Lamps[i].sharedMaterial=(mask&(1UL<<i))!=0?Definition.Lit:Definition.Unlit;
    }
}
