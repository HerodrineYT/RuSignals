using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Aspects;
using Signals.Game.Displays;
using UnityEngine;
using UnityModManagerNet;
namespace RuSignals;
public static class Mod
{
    public const string Id="ru.signals",Version="0.1.0";
    private static SignalAssets assets;private static SignalPack pack;
    private static GameObject templates,clock;private static Harmony harmony;
    private static bool aspectFactory,displayFactory,nameFactory;
    public static bool Load(UnityModManager.ModEntry entry)
    {
        if(assets!=null){entry.Logger.Error("RuSignals already loaded");return false;}
        try
        {
            aspectFactory=AspectCreator.AddCreatorFunction<RuAspectDefinition>((d,s)=>new RuAspect(d,s));
            displayFactory=DisplayCreator.AddCreatorFunction<RouteDisplayDefinition>((d,s)=>new RouteDisplay(d,s));
            nameFactory=DisplayCreator.AddCreatorFunction<NameplateDefinition>((d,s)=>new NameplateDisplay(d,s));
            if(!aspectFactory||!displayFactory||!nameFactory)throw new InvalidOperationException("RuSignals SDK factories already registered");
            string path=Path.Combine(entry.Path,"Assets");assets=new SignalAssets(path);
            templates=new GameObject("RuSignals templates");templates.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(templates);
            SignalReservingObjectDefinition telephone=null;
            foreach(var definition in PackRegistry.Original.EntrySignal.Signals)if(definition.PhysicalReserver!=null){telephone=definition.PhysicalReserver;break;}
            if(telephone==null)throw new InvalidOperationException("DVSignals telephone template unavailable");
            var factory=new PackFactory(assets,templates.transform,telephone,PackFactory.OriginalNameHud(PackRegistry.Original));factory.ReadSemaphores(path);pack=factory.Create();PackRegistry.Register(pack);
            clock=new GameObject("RuSignals visual scheduler");UnityEngine.Object.DontDestroyOnLoad(clock);clock.AddComponent<VisualScheduler>();
            harmony=new Harmony(Id);NativeHooks.Install(harmony);Bridge(entry);
            entry.OnGUI=ControlPanel.Draw;entry.OnToggle=Toggle;entry.OnUnload=Unload;
            entry.Logger.Log("RuSignals "+Version+": signal pack registered.");return true;
        }
        catch(Exception error){entry.Logger.Error("RuSignals load failed: "+error);Cleanup();return false;}
    }
    private static void Bridge(UnityModManager.ModEntry entry)
    {
        var rsg=UnityModManager.modEntries.Find(m=>m.Info.Id=="RailwaySafetyGadgets"&&m.Active);if(rsg==null)return;
        try
        {
            Assembly.LoadFrom(Path.Combine(rsg.Path,"RailwaySafetyGadgets.Signals.dll"));
            Assembly.LoadFrom(Path.Combine(entry.Path,"RuSignals.RsgBridge.dll")).GetType("RuSignals.RsgBridge.Bridge",true).GetMethod("Install").Invoke(null,new object[]{harmony});
            entry.Logger.Log("RailwaySafetyGadgets: RuSignals adapter installed.");
        }
        catch(Exception error){entry.Logger.Warning("RailwaySafetyGadgets adapter unavailable: "+error);}
    }
    private static bool Toggle(UnityModManager.ModEntry entry,bool on)
    {
        if(!on&&PackRegistry.InUse(pack)){entry.Logger.Warning("Return to the main menu before disabling the active signal pack.");return false;}
        if(on)PackRegistry.Enable(pack);else PackRegistry.Remove(pack);if(clock!=null)clock.SetActive(on);return true;
    }
    private static bool Unload(UnityModManager.ModEntry entry){if(PackRegistry.InUse(pack))return false;Cleanup();return true;}
    private static void Cleanup()
    {
        NativeHooks.Remove();harmony?.UnpatchAll(Id);harmony=null;
        if(pack!=null)PackRegistry.Remove(pack);
        if(clock!=null)UnityEngine.Object.Destroy(clock);if(templates!=null)UnityEngine.Object.Destroy(templates);
        assets?.Dispose();assets=null;clock=templates=null;pack=null;
        if(aspectFactory)AspectCreator.RemoveCreatorFunction<RuAspectDefinition>();
        if(displayFactory)DisplayCreator.RemoveCreatorFunction<RouteDisplayDefinition>();aspectFactory=displayFactory=false;
        if(nameFactory)DisplayCreator.RemoveCreatorFunction<NameplateDefinition>();nameFactory=false;
    }
}
