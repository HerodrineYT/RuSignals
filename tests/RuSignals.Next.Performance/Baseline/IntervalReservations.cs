using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.Utils;
using HarmonyLib;
using Signals.Game;
using Signals.Game.Railway;
using UnityEngine;
using GameSignal=Signals.Game.Signal;

namespace RuSignals;

// The native reservation set, timers, events and MP messages are retained.
// A physical RailTrack may contain several block intervals; its single-owner
// dictionary is only a conservative view for older clients of that API.
internal static class IntervalReservations
{
    private static readonly Dictionary<GameSignal,TrackBlock> Active=new Dictionary<GameSignal,TrackBlock>();
    private static readonly FieldInfo SignalsField=AccessTools.Field(typeof(TrackReserver),"s_signals");
    private static readonly FieldInfo TracksField=AccessTools.Field(typeof(TrackReserver),"s_reservations");
    private static readonly FieldInfo RoutinesField=AccessTools.Field(typeof(TrackReserver),"s_clearRoutines");
    private static readonly FieldInfo TimesField=AccessTools.Field(typeof(TrackReserver),"s_times");
    // Cache field lookup only; read the actual native collection on every query.
    internal static HashSet<GameSignal> NativeSignals => (HashSet<GameSignal>)SignalsField.GetValue(null);
    private static Dictionary<RailTrack,GameSignal> NativeTracks => (Dictionary<RailTrack,GameSignal>)TracksField.GetValue(null);
    internal static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(TrackReserver),nameof(TrackReserver.ReserveForSignal),new[]{typeof(GameSignal)}),prefix:new HarmonyMethod(typeof(IntervalReservations),nameof(Reserve)) { priority=Priority.Last });
        h.Patch(AccessTools.Method(typeof(TrackReserver),nameof(TrackReserver.UpdateReservation)),prefix:new HarmonyMethod(typeof(IntervalReservations),nameof(Update)));
        h.Patch(AccessTools.Method(typeof(TrackReserver),"ClearFromSignalInternal"),prefix:new HarmonyMethod(typeof(IntervalReservations),nameof(Clear)));
        h.Patch(AccessTools.Method(typeof(TrackReserver),nameof(TrackReserver.ClearAll)),postfix:new HarmonyMethod(typeof(IntervalReservations),nameof(Reset)));
        h.Patch(AccessTools.Method(typeof(TrackReserver),nameof(TrackReserver.IsSignalReservedByAnother)),prefix:new HarmonyMethod(typeof(IntervalReservations),nameof(Conflict)));
    }
    internal static void Reset()=>Active.Clear();
    private static bool Conflict(GameSignal signal,ref bool __result)
    {
        if(signal?.Block==null || (!AutomaticBlock.HasWindow(signal.Block) && Active.Count==0))return true;
        __result=false;
        foreach(var other in NativeSignals)
        {
            if(other.Controller==signal.Controller || other.Block==null)continue;
            // An automatic through signal is controlled by occupation and the
            // established direction, not a following train's entry reservation.
            if(AutomaticBlock.IsSignal(signal) && AutomaticBlock.SameDirection(signal,other))continue;
            if(AutomaticBlock.Conflicts(signal.Block,Active.TryGetValue(other,out var reserved)?reserved:other.Block))
            {__result=true;break;}
        }
        return false;
    }
    private static bool Reserve(GameSignal signal,ref bool __result)
    {
        if(AutomaticBlock.IsSignal(signal)){__result=false;return false;}
        signal.Controller.UpdateBlocks();
        if(!AutomaticBlock.HasWindow(signal.Block))return true;
        if(TrackReserver.IsSignalReservedByAnother(signal)){__result=false;return false;}
        if(Active.ContainsKey(signal))Remove(signal,true);
        Active[signal]=signal.Block;NativeSignals.Add(signal);RebuildRepresentatives();
        TrackReserver.ReservationMade?.Invoke(signal);__result=true;return false;
    }
    private static bool Update(GameSignal signal,ref bool __result)
    {
        if(!Active.ContainsKey(signal))return true;
        if(signal.Block==null || TrackReserver.IsSignalReservedByAnother(signal) || !AutomaticBlock.TryReserveDirection(signal)){__result=false;return false;}
        // Match native clear/made notifications while retaining the original
        // expiry coroutine and remaining time during a route rebuild.
        Remove(signal,false);
        Active[signal]=signal.Block;NativeSignals.Add(signal);RebuildRepresentatives();
        TrackReserver.ReservationMade?.Invoke(signal);__result=true;return false;
    }
    private static bool Clear(GameSignal signal,bool clearRoutine)
    {
        if(!Active.ContainsKey(signal))return true;
        Remove(signal,clearRoutine);return false;
    }
    private static void Remove(GameSignal signal,bool clearRoutine)
    {
        Active.Remove(signal);NativeSignals.Remove(signal);
        foreach(var track in NativeTracks.Where(p=>p.Value==signal).Select(p=>p.Key).ToArray())NativeTracks.Remove(track);
        if(clearRoutine)
        {
            var routines=(IDictionary)RoutinesField.GetValue(null);
            if(routines.Contains(signal))
            {
                if(routines[signal] is Coroutine routine)CoroutineManager.Instance.Stop(routine);
                routines.Remove(signal);
            }
            ((IDictionary)TimesField.GetValue(null)).Remove(signal);
        }
        RebuildRepresentatives();TrackReserver.ReservationCleared?.Invoke(signal);
    }
    private static void RebuildRepresentatives()
    {
        foreach(var track in NativeTracks.Where(p=>Active.ContainsKey(p.Value)).Select(p=>p.Key).ToArray())NativeTracks.Remove(track);
        foreach(var p in Active.OrderBy(p=>p.Key.Id))foreach(var track in p.Value.AllTracks)
            if(!NativeTracks.ContainsKey(track))NativeTracks[track]=p.Key;
    }
}
