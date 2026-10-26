using System.Collections.Generic;
using HarmonyLib;
using Signals.Game.Lights;
using UnityEngine;
namespace RuSignals;
// Steady changes retain the bounded fade. Blinking uses the original DV Update
// and native smoothing from 2.0.9. No extra per-lamp Update or coroutine.
internal static class LampTiming
{
    private sealed class FadeState
    {
        internal readonly bool Smooth;
        internal bool NativeBlink;
        private float value,from,target;private double start,last;private bool clock;
        internal FadeState(bool smooth){Smooth=smooth;}
        internal void Seed(float level){value=from=level;clock=false;}
        internal float Advance(float next,double time)
        {
            // Rebase on a clock reset, retaining continuity during an interrupted
            // fade. A paused game clock holds the current visual brightness.
            if(!clock||time<last){from=value;start=time;clock=true;}
            if(next!=target){from=value;target=next;start=time;}
            last=time;return value=Smooth?SignalTiming.Fade(from,target,time-start):target;
        }
    }
    private static readonly Dictionary<LampControl,FadeState> lamps=new Dictionary<LampControl,FadeState>();
    private static Harmony harmony;
    private static bool patched;
    private static readonly System.Reflection.MethodInfo nativeUpdate=AccessTools.Method(typeof(LampControl),"Update");
    private static readonly System.Reflection.MethodInfo prefix=AccessTools.Method(typeof(LampTiming),nameof(Update));
    internal static void Install(Harmony owner){harmony=owner;}
    private static void Patch()
    {
        if(patched||harmony==null)return;
        harmony.Patch(nativeUpdate,prefix:new HarmonyMethod(prefix));patched=true;
    }
    private static void Unpatch(){if(!patched)return;harmony.Unpatch(nativeUpdate,prefix);patched=false;}
    internal static void Uninstall(){Reset();harmony=null;}
    private static readonly AccessTools.FieldRef<IndicatorEmission,float> nativeValue=AccessTools.FieldRefAccess<IndicatorEmission,float>("smoothValue");
    private static readonly AccessTools.FieldRef<IndicatorEmission,float> nativeVelocity=AccessTools.FieldRefAccess<IndicatorEmission,float>("smoothVelo");
    internal static void Register(SignalLight light)
    {
        if(light?.Lamp==null)return;bool visible=false;
        // Hidden mechanical API lamps remain instantaneous. Visible electric
        // lenses and the semaphore's auxiliary moon-white lamp fade normally.
        if(light.Definition.Renderers!=null)foreach(var renderer in light.Definition.Renderers)
            if(renderer!=null&&renderer.GetComponent<MeshFilter>()!=null){visible=true;break;}
        Patch();lamps[light.Lamp]=new FadeState(visible);
    }
    internal static bool Update(LampControl __instance)=>Sample(__instance,Time.time);
    internal static bool Sample(LampControl lamp,double time)
    {
        if(!lamps.TryGetValue(lamp,out var fade))return true;
        if(double.IsNaN(time)||double.IsInfinity(time)||time<0)return false;
        if(lamp.lampState==LampControl.LampState.Blinking)
        {
            if(!fade.NativeBlink)
            {
                // Seed only when entering the mode, so a steady lit lamp does
                // not jump to the native smoother's stale previous value.
                nativeValue(lamp.lampInd)=lamp.lampInd.EmissionValue;
                nativeVelocity(lamp.lampInd)=0;
                lamp.lampInd.lag=fade.Smooth?SignalTiming.NativeBlinkLag:0;
                fade.NativeBlink=true;
            }
            return true;
        }
        if(fade.NativeBlink)
        {
            fade.Seed(lamp.lampInd.EmissionValue);lamp.lampInd.lag=0;
            fade.NativeBlink=false;
        }
        float target=lamp.lampState==LampControl.LampState.On?1:0;
        float value=Mathf.Lerp(lamp.lampInd.minValue,lamp.lampInd.maxValue,fade.Advance(target,time));
        if(lamp.lampInd.Value!=value)lamp.lampInd.Value=value;
        return false;
    }
    internal static void Remove(global::Signals.Game.Signal __instance)
    {if(__instance.AllLights!=null)foreach(var light in __instance.AllLights)if(light!=null&&light.Lamp!=null)lamps.Remove(light.Lamp);if(lamps.Count==0)Unpatch();}
    internal static void Reset(){lamps.Clear();Unpatch();}
}
