using System;
using System.Collections.Generic;
using Signals.Game;
using Signals.Game.Railway;
using UnityEngine;
using DV.Utils;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
internal static class IntervalReservations
{
    private static readonly Dictionary<Signal,TrackBlock> owned=new Dictionary<Signal,TrackBlock>();
    internal static TrackBlock ProtectedBlock(Signal signal)=>owned.TryGetValue(signal,out var saved)?saved:signal.Block;
    internal static void Reset(){owned.Clear();DecisionPass.Invalidate();}
    internal static bool OwnsCurrentBlock(Signal signal)
    {
        if(signal?.Block==null||!owned.TryGetValue(signal,out var saved)||!NativeSdk.Reservations.Contains(signal)||
            saved.ShouldBeUpdated||signal.Block.ShouldBeUpdated||!TrackBlock.TracksMatch(saved,signal.Block)||
            !saved.AllTracks.SetEquals(signal.Block.AllTracks))return false;
        return !TrackReserver.IsSignalReservedByAnother(signal)&&AutomaticBlock.TryReserveDirection(signal);
    }
    internal static bool Conflict(Signal signal,ref bool __result)
    {
        if(signal?.Block==null||(!AutomaticBlock.HasWindow(signal.Block)&&owned.Count==0))return true;
        __result=false;
        foreach(var holder in NativeSdk.Reservations)
        {
            if(holder.Controller==signal.Controller||holder.Block==null)continue;
            if(AutomaticBlock.IsSignal(signal)&&AutomaticBlock.SameDirection(signal,holder))continue;
            var protectedRoute=ProtectedBlock(holder);
            if(AutomaticBlock.Conflicts(signal.Block,protectedRoute)){__result=true;break;}
        }
        return false;
    }
    internal static bool Reserve(Signal signal,ref bool __result)
    {
        DecisionPass.Invalidate();
        if(AutomaticBlock.IsSignal(signal)){__result=false;return false;}
        signal.Controller.UpdateBlocks();
        if(!AutomaticBlock.HasWindow(signal.Block))return true;
        if(signal.Block.Tracks.Length==0){__result=false;return false;}
        __result=false;if(TrackReserver.IsSignalReservedByAnother(signal))return false;
        if(owned.ContainsKey(signal))Release(signal,true);
        Add(signal);__result=true;return false;
    }
    private static void Add(Signal signal)
    {
        owned[signal]=signal.Block;NativeSdk.Reservations.Add(signal);RefreshTracks();
        TrackReserver.ReservationMade?.Invoke(signal);
    }
    internal static bool Update(Signal signal,ref bool __result)
    {
        DecisionPass.Invalidate();
        if(!owned.ContainsKey(signal))return true;
        __result=false;
        if(signal.Block==null||TrackReserver.IsSignalReservedByAnother(signal)||!AutomaticBlock.TryReserveDirection(signal))return false;
        Release(signal,false);Add(signal);__result=true;return false;
    }
    internal static bool Clear(Signal signal,bool clearRoutine)
    {
        DecisionPass.Invalidate();if(!owned.ContainsKey(signal))return true;
        Release(signal,clearRoutine);return false;
    }
    private static void Release(Signal signal,bool timer)
    {
        owned.Remove(signal);NativeSdk.Reservations.Remove(signal);
        var tracks=NativeSdk.ReservationTracks;var remove=new List<RailTrack>();
        foreach(var pair in tracks)if(pair.Value==signal)remove.Add(pair.Key);
        foreach(var rail in remove)tracks.Remove(rail);
        if(timer)
        {
            var timers=NativeSdk.Timers;
            if(timers.Contains(signal)){if(timers[signal] is Coroutine routine)CoroutineManager.Instance.Stop(routine);timers.Remove(signal);}
            NativeSdk.Times.Remove(signal);
        }
        RefreshTracks();TrackReserver.ReservationCleared?.Invoke(signal);
    }
    private static void RefreshTracks()
    {
        var tracks=NativeSdk.ReservationTracks;var remove=new List<RailTrack>();
        foreach(var pair in tracks)if(owned.ContainsKey(pair.Value))remove.Add(pair.Key);
        foreach(var rail in remove)tracks.Remove(rail);
        // Deterministic representative for legacy whole-RailTrack API callers.
        var holders=new List<Signal>(owned.Keys);holders.Sort((a,b)=>a.Id.CompareTo(b.Id));
        foreach(var holder in holders)foreach(var rail in owned[holder].AllTracks)if(!tracks.ContainsKey(rail))tracks.Add(rail,holder);
    }
}
