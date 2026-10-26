using System;
using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Curves;
using Signals.Game.Railway;
using UnityEngine;
namespace RuSignals;
using Signal=global::Signals.Game.Signal;

// Equivalent implementations of small allocation-heavy DVSignals queries.
// No results are cached: occupation, reservations and curve points stay live.
internal static class NativeOptimizations
{
    private static readonly Guid VerifiedModule=new Guid("5ba6bfa5-0bf0-42d9-949b-cb1ec56eeeb2");
    internal static bool Enabled;
    private static Func<RailTrack,bool> logicTrack;
    internal static bool Verified(Guid module)=>module==VerifiedModule;
    private static bool Active=>Enabled&&SignalManager.CurrentPack?.ModId==Mod.Id;
    internal static void Install(Harmony harmony)
    {
        Enabled=false;
        // AssemblyVersion is always 1.0.0.0 in this SDK. Only optimize the exact
        // verified native implementation; other builds retain their own body.
        if(!Verified(typeof(TrackBlock).Module.ModuleVersionId))return;
        logicTrack=(Func<RailTrack,bool>)AccessTools.Method(typeof(TrackBlock).Assembly.GetType("Signals.Game.Extensions"),"IsLogicYardTrack",new[]{typeof(RailTrack)}).CreateDelegate(typeof(Func<RailTrack,bool>));
        void Patch(Type owner,string name,string callback)=>harmony.Patch(AccessTools.Method(owner,name),
            prefix:new HarmonyMethod(typeof(NativeOptimizations),callback){priority=Priority.Last});
        Patch(typeof(TrackBlock),nameof(TrackBlock.IsOccupied),nameof(Occupied));
        Patch(typeof(TrackBlock),nameof(TrackBlock.IsOccupiedIgnoreLogic),nameof(OccupiedIgnoreLogic));
        Patch(typeof(TrackReserver),nameof(TrackReserver.IsSignalReservedByAnother),nameof(ReservedByAnother));
        Patch(typeof(CubicBezier),nameof(CubicBezier.GetBounds),nameof(Bounds));
        Enabled=true;
    }
    internal static void Remove()=>Enabled=false;
    private static bool Occupied(TrackBlock __instance,CrossingCheckMode crossingMode,ref bool __result)
        =>ReadOccupation(__instance,crossingMode,false,ref __result);
    private static bool OccupiedIgnoreLogic(TrackBlock __instance,CrossingCheckMode crossingMode,ref bool __result)
        =>ReadOccupation(__instance,crossingMode,true,ref __result);
    private static bool ReadOccupation(TrackBlock block,CrossingCheckMode mode,bool ignoreLogic,ref bool result)
    {
        if(!Active||AutomaticBlock.HasWindow(block)||block.Tracks==null||block.ExtraTracks==null)return true;
        // Retain native order, duplicates, early exit and TrackChecker calls,
        // including fouling points and the caller's exact crossing mode.
        foreach(var info in block.Tracks)
            if((!ignoreLogic||!logicTrack(info.Track))&&TrackChecker.IsOccupied(info.Track,mode)){result=true;return false;}
        foreach(var track in block.ExtraTracks)
            if((!ignoreLogic||!logicTrack(track))&&TrackChecker.IsOccupied(track,mode)){result=true;return false;}
        result=false;return false;
    }
    private static bool ReservedByAnother(Signal signal,ref bool __result)
    {
        if(!Active||signal==null)return true;
        // IntervalReservations runs earlier and keeps its own overlap and
        // direction rules. Native fallback queries still call TrackChecker.
        __result=false;var block=signal.Block;if(block==null)return false;
        foreach(var track in block.AllTracks)
            if(TrackChecker.IsReservedByAnother(track,signal)){__result=true;break;}
        return false;
    }
    private static bool Bounds(CubicBezier __instance,ref Bounds __result)
    {
        if(!Active)return true;
        // Match AABBFromCollection(GetPoints()) exactly, including its initial
        // repeated P0 Encapsulate. Recursive intersection order is untouched.
        var bounds=new Bounds(__instance.P0,Vector3.zero);
        bounds.Encapsulate(__instance.P0);bounds.Encapsulate(__instance.P1);
        bounds.Encapsulate(__instance.P2);bounds.Encapsulate(__instance.P3);
        __result=bounds;return false;
    }
}
