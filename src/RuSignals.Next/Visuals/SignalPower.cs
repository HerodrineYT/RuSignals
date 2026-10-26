using System;
using HarmonyLib;
namespace RuSignals;
// Optional DVSeasons API; no assembly dependency and no per-panel reflection.
internal static class SignalPower
{
    private static Func<bool> read;
    private static bool subscribed;
    private static volatile bool resolve=true;
    private static void AssemblyLoaded(object sender,AssemblyLoadEventArgs args){resolve=true;}
    private static void Bind()
    {
        // A missing optional API stays cached until an assembly is loaded.
        // Qualified lookup does not enumerate every type in every plugin.
        resolve=false;
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type=assembly.GetType("DVSeasons.Mod.OptionalModPowerCompatibility",false);
            if(type==null)continue;
            var method=AccessTools.PropertyGetter(type,"IsOut");
            if(method!=null&&method.ReturnType==typeof(bool)&&method.IsStatic)
                read=(Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>),method);
            break;
        }
    }
    internal static bool IsOut {get;private set;}
    internal static bool Refresh()
    {
        if(!subscribed){AppDomain.CurrentDomain.AssemblyLoad+=AssemblyLoaded;subscribed=true;}
        if(read==null&&resolve)Bind();
        bool outage=read!=null&&read();if(outage==IsOut)return false;IsOut=outage;return true;
    }
    internal static void Reset()
    {
        if(subscribed)AppDomain.CurrentDomain.AssemblyLoad-=AssemblyLoaded;
        subscribed=false;read=null;resolve=true;IsOut=false;
    }
}
