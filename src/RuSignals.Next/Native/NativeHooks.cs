using System;
using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Lights;
using Signals.Game.Railway;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
internal static class NativeHooks
{
    internal static void Install(Harmony h)
    {
        void Prefix(Type type,string name,Type owner,string method)=>h.Patch(AccessTools.Method(type,name),prefix:new HarmonyMethod(owner,method));
        var ab=typeof(AutomaticBlock);var reservations=typeof(IntervalReservations);
        Prefix(typeof(Signals.Game.Generation.SignalPlacer),"CreateTurntableSignals",typeof(TurntableApproaches),nameof(TurntableApproaches.Generate));
        Prefix(typeof(TurntableSignalController),"TracksUpdated",typeof(TurntableApproaches),nameof(TurntableApproaches.Connection));
        Prefix(typeof(BasicSignalController),nameof(BasicSignalController.OffsetOnTrack),typeof(TurntableApproaches),nameof(TurntableApproaches.ConstrainMovement));
        Prefix(typeof(StationControllerCache),"Generate",ab,nameof(AutomaticBlock.Generate));
        h.Patch(AccessTools.Method(typeof(StationControllerCache),"Generate"),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(WorldRegistered)));
        Prefix(typeof(SignalManager),"ClearCaches",typeof(SignalDesignations),nameof(SignalDesignations.Reset));Prefix(typeof(SignalManager),"OnDestroy",typeof(SignalDesignations),nameof(SignalDesignations.Reset));
        Prefix(typeof(SignalManager),"ClearCaches",typeof(ShieldRegions),nameof(ShieldRegions.Reset));Prefix(typeof(SignalManager),"OnDestroy",typeof(ShieldRegions),nameof(ShieldRegions.Reset));
        Prefix(typeof(SignalManager),"ClearCaches",ab,nameof(AutomaticBlock.Reset));Prefix(typeof(SignalManager),"OnDestroy",ab,nameof(AutomaticBlock.Reset));
        Prefix(typeof(SignalManager),"ClearCaches",typeof(SignalSpeedLimits),nameof(SignalSpeedLimits.Reset));Prefix(typeof(SignalManager),"OnDestroy",typeof(SignalSpeedLimits),nameof(SignalSpeedLimits.Reset));
        Prefix(typeof(Signal),nameof(Signal.SetBlock),ab,nameof(AutomaticBlock.SetBlock));
        Prefix(typeof(TrackBlock),nameof(TrackBlock.IsOccupied),ab,nameof(AutomaticBlock.Occupied));Prefix(typeof(TrackBlock),nameof(TrackBlock.IsOccupiedIgnoreLogic),ab,nameof(AutomaticBlock.OccupiedIgnoreLogic));
        Prefix(typeof(TrackBlock),nameof(TrackBlock.TracksMatch),ab,nameof(AutomaticBlock.TracksMatch));
        h.Patch(AccessTools.PropertyGetter(typeof(TrackBlock),nameof(TrackBlock.ShouldBeUpdated)),postfix:new HarmonyMethod(ab,nameof(AutomaticBlock.RouteChanged)));
        h.Patch(AccessTools.Method(typeof(BasicSignalController),nameof(BasicSignalController.OffsetOnTrack)),prefix:new HarmonyMethod(ab,nameof(AutomaticBlock.ConstrainMovement)),postfix:new HarmonyMethod(ab,nameof(AutomaticBlock.PositionChanged)));
        var reserve=AccessTools.Method(typeof(TrackReserver),nameof(TrackReserver.ReserveForSignal),new[]{typeof(Signal)});
        h.Patch(reserve,prefix:new HarmonyMethod(ab,nameof(AutomaticBlock.BeforeReserve)));
        h.Patch(reserve,prefix:new HarmonyMethod(reservations,nameof(IntervalReservations.Reserve)){priority=Priority.Last});
        h.Patch(AccessTools.Method(typeof(TrackReserver),nameof(TrackReserver.UpdateReservation)),prefix:new HarmonyMethod(reservations,nameof(IntervalReservations.Update)){priority=Priority.Last});
        Prefix(typeof(TrackReserver),"ClearFromSignalInternal",reservations,nameof(IntervalReservations.Clear));
        Prefix(typeof(TrackReserver),nameof(TrackReserver.IsSignalReservedByAnother),reservations,nameof(IntervalReservations.Conflict));
        h.Patch(AccessTools.Method(typeof(TrackReserver),nameof(TrackReserver.ClearAll)),postfix:new HarmonyMethod(reservations,nameof(IntervalReservations.Reset)));
        h.Patch(AccessTools.Method(typeof(Signal),nameof(Signal.UpdateAspect)),prefix:new HarmonyMethod(typeof(NativeHooks),nameof(BeginPass)),finalizer:new HarmonyMethod(typeof(NativeHooks),nameof(EndPass)));
        Prefix(typeof(Signal),nameof(Signal.ChangeAspect),typeof(NativeHooks),nameof(ChangingAspect));
        foreach(string method in new[]{nameof(Signal.ChangeOperationMode),nameof(Signal.SetAspectOverride),nameof(Signal.SetShuntingStatus)})Prefix(typeof(Signal),method,typeof(DecisionPass),nameof(DecisionPass.Invalidate));
        h.Patch(AccessTools.Method(typeof(Signal),nameof(Signal.TurnOff)),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(TurnedOff)));
        h.Patch(AccessTools.Method(typeof(Signal),nameof(Signal.SetBlock)),postfix:new HarmonyMethod(typeof(SignalSpeedLimits),nameof(SignalSpeedLimits.Invalidate)));
        h.Patch(AccessTools.Method(typeof(BasicSignalController),nameof(BasicSignalController.Destroy)),postfix:new HarmonyMethod(typeof(SignalSpeedLimits),nameof(SignalSpeedLimits.Invalidate)));
        h.Patch(AccessTools.Constructor(typeof(BasicSignalController),new[]{typeof(SignalControllerDefinition),typeof(SignalPlacementInfo?)}),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(ControllerCreated)));
        h.Patch(AccessTools.Method(typeof(BasicSignalController),nameof(BasicSignalController.OffsetOnTrack)),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(ControllerMoved)));
        h.Patch(AccessTools.Method(typeof(BasicSignalController),nameof(BasicSignalController.FlipSide)),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(ControllerFlipped)));
        h.Patch(AccessTools.Method(typeof(SignalLight),nameof(SignalLight.Initialize)),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(LampCreated)));
        h.Patch(AccessTools.Method(typeof(SignalLight),"CreateGlare"),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(GlareCreated)));
        LampTiming.Install(h);
        h.Patch(AccessTools.Method(typeof(Signal),nameof(Signal.Destroy)),postfix:new HarmonyMethod(typeof(LampTiming),nameof(LampTiming.Remove)));
        h.Patch(AccessTools.Method(typeof(Signal),nameof(Signal.Destroy)),postfix:new HarmonyMethod(typeof(LensVariation),nameof(LensVariation.Remove)));
        Prefix(typeof(SignalManager),"ClearCaches",typeof(LensVariation),nameof(LensVariation.Reset));Prefix(typeof(SignalManager),"OnDestroy",typeof(LensVariation),nameof(LensVariation.Reset));
        Prefix(typeof(SignalManager),"ClearCaches",typeof(LampTiming),nameof(LampTiming.Reset));Prefix(typeof(SignalManager),"OnDestroy",typeof(LampTiming),nameof(LampTiming.Reset));
        TrackChecker.OnMapBuilt+=AutomaticBlock.MapBuilt;
        DispatcherIntegration.Install(h);
        NativeOptimizations.Install(h);
    }
    internal static void Remove(){NativeOptimizations.Remove();DispatcherIntegration.Remove();LensVariation.Reset();TrackChecker.OnMapBuilt-=AutomaticBlock.MapBuilt;AutomaticBlock.Reset();SignalSpeedLimits.Reset();ShieldRegions.Reset();SignalDesignations.Reset();LampTiming.Uninstall();SignalPower.Reset();}
    private static void WorldRegistered(){SemaphoreWarnings.Ensure();ShieldRegions.Refresh();SignalDesignations.Refresh();LensVariation.Refresh();}
    private static void BeginPass(Signal __instance,out DecisionPass.Scope __state)=>DecisionPass.Begin(__instance,out __state);
    private static Exception EndPass(Exception __exception,DecisionPass.Scope __state){DecisionPass.End(__state);return __exception;}
    private static void ChangingAspect(Signal __instance,int newAspect){if(newAspect!=__instance.CurrentAspectIndex){DecisionPass.Invalidate();SignalSpeedLimits.Invalidate();}}
    private static bool Ours(Signal signal)
    {
        // SignalLight.Initialize runs inside the Signal constructor while its
        // AllAspects array is still being built. Definition is already assigned.
        var aspects=signal?.Definition?.Aspects;
        return aspects!=null&&aspects.Length>0&&aspects[0] is RuAspectDefinition;
    }
    private static void TurnedOff(Signal __instance,bool __result)
    {if(__result){DecisionPass.Invalidate();SignalSpeedLimits.Invalidate();if(Ours(__instance))__instance.Controller.AnyAspectChanged?.Invoke(__instance,null);}}
    private static void ControllerCreated(BasicSignalController __instance)
    {
        SignalSpeedLimits.Invalidate();
        __instance.Definition.GetComponent<ClearanceView>()?.Bind(__instance);
        __instance.Definition.GetComponent<ShieldView>()?.Bind(__instance);
        foreach(var signal in __instance.AllSignals)signal.Definition.GetComponentInChildren<SemaphoreVisual>(true)?.Bind(signal);
    }
    private static void LampCreated(SignalLight __instance,Signal signal)
    {
        if(!Ours(signal))return;bool blue=LampPalette.IsBlue(__instance.Definition.Colour);
        __instance.Lamp.lampInd.emissionColor=__instance.Definition.Colour*LampVisualRules.Emission(blue);
        __instance.Lamp.lampInd.glareColor=__instance.Definition.Colour*LampVisualRules.Glare(blue);
        __instance.Definition.GetComponentInParent<SpillView>()?.Bind(__instance.Lamp.lampInd);
        LampTiming.Register(__instance);
    }
    private static void GlareCreated(SignalLight __instance,UnityEngine.Renderer __result)
    {
        if(__result==null||!Ours(__instance.Signal)||SignalAssets.Current==null)return;
        var mesh=__result.GetComponent<UnityEngine.MeshFilter>();if(mesh==null)return;
        mesh.sharedMesh=SignalAssets.Current.GlareMesh();__result.sharedMaterial=SignalAssets.Current.Glare();
        var lens=__instance.Definition.Renderers[0].GetComponent<UnityEngine.MeshFilter>();
        float radius=UnityEngine.Mathf.Max(lens.sharedMesh.bounds.extents.x,lens.sharedMesh.bounds.extents.y);
        __result.transform.localScale=UnityEngine.Vector3.one*(radius*1.2f);
    }
    private static void ControllerMoved(BasicSignalController __instance,bool __result){if(__result)ControllerFlipped(__instance);}
    private static void ControllerFlipped(BasicSignalController __instance)
    {SignalSpeedLimits.Invalidate();if(__instance.Definition!=null){__instance.Definition.GetComponent<ClearanceView>()?.Reposition();__instance.Definition.GetComponent<ShieldView>()?.Bind(__instance);}}
}
