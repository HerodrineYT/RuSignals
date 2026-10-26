using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEditor;

public static partial class UnityRewriteCheck
{
    private static AssetBundle originalHudBundle;
    private static object originalHud;
    private static object LoadOriginalHud(Assembly common,string sdk)
    {
        originalHudBundle=AssetBundle.LoadFromFile(Path.Combine(sdk,"signal_bundle"));
        if(originalHudBundle==null)throw new Exception("Original DVSignals bundle cannot be loaded");
        var packType=common.GetType("Signals.Common.SignalPack",true);
        var nativePack=originalHudBundle.LoadAsset("assets/signalcreator/default/default.asset",packType);
        if(nativePack==null)throw new Exception("Original DVSignals pack is absent");
        var controller=(Component)Field(nativePack,"Signal");
        originalHud=controller.GetComponentsInChildren(common.GetType("Signals.Common.Displays.SignalNameDisplayDefinition",true),true).First(c=>Field(c,"HUDSprite") is Sprite);
        return originalHud;
    }
    public static void CaptureOriginalHud()
    {
        try
        {
            string root=Environment.GetEnvironmentVariable("RUS_V2_ROOT"),sdk=Environment.GetEnvironmentVariable("RUS_V2_SDK");
            AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>
            {
                foreach(var directory in new[]{sdk,Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_GAME"),"DerailValley_Data/Managed")})
                {var file=Path.Combine(directory,new AssemblyName(args.Name).Name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}
                return null;
            };
            var common=Assembly.LoadFrom(Path.Combine(sdk,"Signals.Common.dll"));var definition=LoadOriginalHud(common,sdk);
            var sprite=(Sprite)Field(definition,"HUDSprite");
            Debug.Log("RUS_ORIGINAL_HUD_PASS "+sprite.name+" / "+sprite.rect+" / "+Field(definition,"HUDTextColour")+" / "+Field(definition,"HUDDisplayOrder"));
            originalHudBundle.Unload(true);EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
