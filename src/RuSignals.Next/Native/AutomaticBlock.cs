using System;
using System.Collections.Generic;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using UnityEngine;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;

// Stable boundary for the cab adapter and native Harmony callbacks. State is
// owned by a replaceable world instance, never by static per-map collections.
public static class AutomaticBlock
{
    internal static BlockNetwork Network=new BlockNetwork();
    public static int SignalCount=>Network.Controllers.Count;
    internal static TrackDirection TravelDirection(TrackDirection face)=>face==TrackDirection.Out?TrackDirection.In:TrackDirection.Out;
    internal static bool IsSignal(Signal signal)=>signal?.Controller is AutoController;
    internal static bool HasWindow(TrackBlock block)=>block!=null&&Network.Windows.TryGetValue(block,out _);
    public static bool TryContains(TrackBlock block,RailTrack track,double span,int direction,out bool contains)=>Network.Contains(block,track,span,direction,out contains);
    internal static bool Conflicts(TrackBlock a,TrackBlock b)=>Network.Conflict(a,b);
    internal static bool SameDirection(Signal a,Signal b)=>Network.SameDirection(a,b);
    internal static bool DirectionAllowed(Signal signal)=>Network.Allows(signal);
    internal static bool TryReserveDirection(Signal signal)=>!BlockNetwork.Train(signal)||Network.Allows(signal);
    internal static TrackBlock FullRoute(TrackBlock block)=>block!=null&&Network.Windows.TryGetValue(block,out var window)?window.Route:block;
    internal static string RouteTrackNumber(TrackBlock block)=>block==null||block.ShouldBeUpdated?"":FullRoute(block).TrackTrimmerNumber;
    internal static TrackInfo[] RouteTracks(TrackBlock block)=>block==null||block.ShouldBeUpdated?Array.Empty<TrackInfo>():FullRoute(block).Tracks;
    internal static void Reset(){Network=new BlockNetwork();IntervalReservations.Reset();DecisionPass.Invalidate();}
    internal static void MapBuilt(Dictionary<RailTrack,TrackChecker.TrackIntersectionPoints> map){Network.Crossings=map;Network.CrossingRevision++;DecisionPass.Invalidate();}
    internal static void Generate()
    {
        try{Network.Generate();}
        catch(Exception error)
        {
            foreach(var c in Network.Controllers.ToArray())c.Destroy();Reset();BlockNetwork.RebuildAll();
            Debug.LogError("[RuSignals] AB rolled back: "+error);
        }
    }
    internal static void SetBlock(Signal __instance,ref TrackBlock block){DecisionPass.Invalidate();Network.Shorten(__instance,ref block);}
    internal static void RouteChanged(TrackBlock __instance,ref bool __result)
    {if(!__result&&Network.Windows.TryGetValue(__instance,out var window))__result=window.Route.ShouldBeUpdated;}
    internal static bool TestOccupied(TrackBlock block,CrossingCheckMode mode,bool ignoreLogic,ref bool result)=>!Network.Occupancy(block,mode,ignoreLogic,out result);
    internal static bool Occupied(TrackBlock __instance,CrossingCheckMode crossingMode,ref bool __result)=>TestOccupied(__instance,crossingMode,false,ref __result);
    internal static bool OccupiedIgnoreLogic(TrackBlock __instance,CrossingCheckMode crossingMode,ref bool __result)=>TestOccupied(__instance,crossingMode,true,ref __result);
    internal static bool TracksMatch(TrackBlock a,TrackBlock b,ref bool __result)=>!Network.Match(a,b,out __result);
    internal static void ConstrainMovement(BasicSignalController __instance,ref int pointIndexOffset)=>Network.LimitMovement(__instance,ref pointIndexOffset);
    internal static void PositionChanged(BasicSignalController __instance,bool __result)
    {
        if(!__result||!BlockNetwork.Train(__instance.GetControllerSignal())||!__instance.PlacementInfo.HasValue)return;
        NativeSdk.RefreshSpan(__instance);DecisionPass.Invalidate();
        if(Network.Ready)foreach(var controller in SignalManager.Instance.AllControllers)controller.FlagAllBlocksForUpdating();
    }
    internal static bool BeforeReserve(Signal signal,ref bool __result)
    {
        DecisionPass.Invalidate();
        if(!Network.Ready||!BlockNetwork.Train(signal))return true;
        if(IsSignal(signal)){__result=false;return false;}
        signal.Controller.UpdateBlocks();if(TryReserveDirection(signal))return true;__result=false;return false;
    }
}
