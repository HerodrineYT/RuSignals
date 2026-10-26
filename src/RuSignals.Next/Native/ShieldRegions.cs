using System;
using Signals.Game;
using Signals.Game.Controllers;
using UnityEngine;
namespace RuSignals;

// Five cached station transforms; no world search or per-signal Update.
// Referencing transforms keeps the radius correct after floating-origin moves.
internal static class ShieldRegions
{
    private static readonly Transform[] centres=new Transform[5];
    private static int count;
    private static bool prepared;
    internal static void Reset(){Array.Clear(centres,0,centres.Length);count=0;prepared=false;}
    private static void Prepare()
    {
        if(prepared)return;
        var stations=StationController.allStations;
        if(stations==null||stations.Count==0)return;
        count=0;Array.Clear(centres,0,centres.Length);
        foreach(var station in stations)
        {
            if(station==null||!station.StationInfoValid||!ShieldRegionRules.IndustrialStation(station.stationInfo.YardID))continue;
            if(count<centres.Length)centres[count++]=station.transform;
        }
        prepared=true;
    }
    internal static bool Square(BasicSignalController controller)
    {
        Prepare();
        // A named yard is wholly modern, including unusually long sidings.
        if(ShieldRegionRules.IndustrialStation(controller.Group?.Station))return true;
        var position=controller.Definition.transform.position;
        for(int i=0;i<count;i++)
        {
            if(centres[i]==null)continue;
            var delta=position-centres[i].position;
            if(ShieldRegionRules.Near(delta.x,delta.z))return true;
        }
        return false;
    }
    internal static void Refresh()
    {
        Reset();Prepare();
        if(SignalManager.CurrentPack?.ModId!=Mod.Id||SignalManager.Instance==null)return;
        foreach(var controller in SignalManager.Instance.AllControllers)
            controller.Definition.GetComponent<ShieldView>()?.Bind(controller);
    }
}
