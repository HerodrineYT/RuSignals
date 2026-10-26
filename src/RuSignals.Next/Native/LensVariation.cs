using System.Collections.Generic;
using HarmonyLib;
using Signals.Game;
using Signals.Game.Lights;
using UnityEngine;
namespace RuSignals;
using Signal=global::Signals.Game.Signal;

// Applied once after native names are assigned. Reuses the indicator's own
// property block, so native fades/blinking preserve the optical profile.
internal static class LensVariation
{
    private static readonly HashSet<Signal> applied=new HashSet<Signal>();
    private static readonly AccessTools.FieldRef<IndicatorEmission,MaterialPropertyBlock> Properties=
        AccessTools.FieldRefAccess<IndicatorEmission,MaterialPropertyBlock>("emissionPropertyBlock");
    private static readonly int Optics=Shader.PropertyToID("_LensVariant"),Tint=Shader.PropertyToID("_Color"),Gloss=Shader.PropertyToID("_Glossiness");
    internal const int LegacyTintCount=32,AddedTintCount=64,TintCount=LegacyTintCount+AddedTintCount;
    // Hue families include the supplied ruby, sea-green, blue and amber
    // references. The reference bloom, white cores and ring patterns are not used.
    private static readonly Color[] Reds={new Color(1,.005f,.45f),new Color(1,.006f,.30f),new Color(1,.012f,.115f),new Color(1,.018f,.01f),new Color(1,.14f,.008f)};
    // The whole yellow family stays clearly separated from the red family's
    // orange edge, including dim rings, halo and the environment light.
    private static readonly Color[] Yellows={new Color(1,.62f,.003f),new Color(1,.70f,.004f),new Color(1,.78f,.006f),new Color(1,.86f,.010f),new Color(1,.94f,.018f)};
    private static readonly Color[] Greens={new Color(.008f,1,.045f),new Color(.008f,1,.30f),new Color(.008f,1,.59f),new Color(.008f,1,.75f),new Color(.025f,1,.82f)};
    private static readonly Color[] Blues={new Color(.003f,.28f,1),new Color(.004f,.13f,1),new Color(.006f,.025f,1),new Color(.04f,.006f,1),new Color(.13f,.008f,1)};
    private static readonly Color[] Whites={new Color(.55f,.72f,1),new Color(.68f,.80f,1),new Color(.84f,.90f,1),new Color(1,1,1),new Color(1,.96f,.86f)};
    internal static float Gain(string key)=>.78f+.22f*((Hash(key)>>8)&255)/255f;
    internal static int TintIndex(string key)=>(int)((Hash(key)>>16)%TintCount);
    internal static Color Colour(Color source,int tint)
    {
        tint=Mathf.Clamp(tint,0,TintCount-1);
        if(source.r>.9f&&source.g>=.1f&&source.g<.7f)
        {
            float yellowPosition=tint/(TintCount-1f)*(Yellows.Length-1);
            int yellowSegment=Mathf.Min((int)yellowPosition,Yellows.Length-2);
            return Color.Lerp(Yellows[yellowSegment],Yellows[yellowSegment+1],yellowPosition-yellowSegment);
        }
        if(tint<LegacyTintCount)return LegacyColour(source,tint);
        var stops=LampPalette.IsBlue(source)?Blues:source.r>.9f&&source.g<.1f?Reds:source.r>.9f&&source.g<.7f?Yellows:source.g>.9f&&source.r<.1f?Greens:Whites;
        float position=(tint-LegacyTintCount)/(AddedTintCount-1f)*(stops.Length-1);
        int segment=Mathf.Min((int)position,stops.Length-2);
        return Color.Lerp(stops[segment],stops[segment+1],position-segment);
    }
    private static Color LegacyColour(Color source,int tint)
    {
        float t=Mathf.Clamp01(tint/31f);
        if(LampPalette.IsBlue(source))return new Color(Mathf.Lerp(.001f,.045f,1-t),Mathf.Lerp(.003f,.09f,t),1);
        if(source.r>.9f&&source.g<.1f)return new Color(1,Mathf.Lerp(.003f,.09f,t),Mathf.Lerp(.003f,.04f,1-t));
        if(source.r>.9f&&source.g<.7f)return new Color(1,Mathf.Lerp(.28f,.68f,t),Mathf.Lerp(.003f,.025f,t));
        if(source.g>.9f&&source.r<.1f)return new Color(Mathf.Lerp(.003f,.035f,t),1,Mathf.Lerp(.02f,.34f,t));
        return new Color(Mathf.Lerp(.55f,1,t),Mathf.Lerp(.70f,1,t),1);
    }
    private static uint Hash(string key)
    {
        unchecked{uint hash=2166136261;foreach(char c in key??""){hash^=c;hash*=16777619;}hash^=hash>>16;hash*=0x85ebca6b;hash^=hash>>13;return hash;}
    }
    internal static int Profile(string key)=>(int)((Hash(key)>>5)&1);
    internal static void Refresh()
    {
        foreach(var controller in SignalManager.Instance.AllControllers)foreach(var signal in controller.AllSignals)
        {Apply(signal);if(signal.DistantSignal!=null)Apply(signal.DistantSignal);}
    }
    internal static void Apply(Signal signal)
    {
        if(signal?.AllLights==null||signal.Definition.Aspects.Length==0||!(signal.Definition.Aspects[0] is RuAspectDefinition)||!applied.Add(signal))return;
        // One physical controller/mast has one optical construction, including
        // any additional heads; colour and brightness still vary per lamp.
        int profile=Profile(signal.Controller.Name);
        foreach(var lamp in signal.AllLights)
        {
            var indicator=lamp?.Lamp?.lampInd;var def=lamp?.Definition;
            if(indicator==null||def?.Renderers==null||def.Renderers.Length!=1)continue;
            var renderer=def.Renderers[0];var material=renderer?.sharedMaterial;
            if(material==null||material.shader.name!="RuSignals/Signal glass four rings")continue;
            string key=signal.Name+"/"+def.name;float gain=Gain(key);
            Color source=def.Colour;
            var colour=Colour(source,TintIndex(key));
            var properties=Properties(indicator);if(properties==null)continue;
            properties.SetFloat(Optics,profile);
            properties.SetColor(Tint,Color.Lerp(new Color(.022f,.026f,.030f),colour,.04f));
            properties.SetFloat(Gloss,material.GetFloat(Gloss)+(profile==0?0:.025f));
            bool blue=LampPalette.IsBlue(source);
            indicator.emissionColor=colour*(LampVisualRules.Emission(blue)*gain);
            indicator.glareColor=colour*(LampVisualRules.Glare(blue)*gain);
            if(indicator.emissionLight!=null)indicator.emissionLight.color=colour;
            var spill=def.GetComponentInParent<SpillView>();
            if(spill!=null)spill.SetIntensity(indicator,def.LightIntensity*gain);
            else indicator.lightIntensity=def.LightIntensity*gain;
            // Refresh the current level without changing logical lamp state.
            indicator.Value=indicator.Value;
        }
    }
    internal static void Remove(Signal __instance)=>applied.Remove(__instance);
    internal static void Reset()=>applied.Clear();
}
