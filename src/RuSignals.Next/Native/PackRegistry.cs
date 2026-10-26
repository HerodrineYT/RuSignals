using System;
using System.Collections;
using HarmonyLib;
using Signals.Common;
using Signals.Game;
namespace RuSignals;
internal static class PackRegistry
{
    private static readonly System.Reflection.FieldInfo PacksField=AccessTools.Field(typeof(SignalManager),"InstalledPacks");
    internal static SignalPack Original=>(SignalPack)AccessTools.Field(typeof(SignalManager),"DefaultPack").GetValue(null);
    internal static void Register(SignalPack pack)
    {
        AccessTools.Method(typeof(SignalManager),"ProcessControllers").Invoke(null,new object[]{pack});
        Enable(pack);
    }
    internal static void Enable(SignalPack pack)
    {
        ((IDictionary)PacksField.GetValue(null))[Mod.Id]=pack;
        SignalsMod.Settings.RebuildKeys();
    }
    internal static void Remove(SignalPack pack)
    {
        var packs=(IDictionary)PacksField.GetValue(null);if(ReferenceEquals(packs[Mod.Id],pack))packs.Remove(Mod.Id);
    }
    internal static bool InUse(SignalPack pack)=>SignalManager.Running&&ReferenceEquals(SignalManager.CurrentPack,pack);
}
