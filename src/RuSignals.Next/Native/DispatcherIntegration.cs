using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using UnityEngine;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;

// ADS loads its optional signal adapter late. Discover only that assembly;
// there is no menu polling or compile-time dependency on the dispatcher.
internal static class DispatcherIntegration
{
    private const string AssemblyName="AdvancedDispatcherSystem.Signals";
    private static Harmony owner;
    private static SynchronizationContext context;
    private static int thread;
    private static readonly HashSet<Assembly> installed=new HashSet<Assembly>();
    private static readonly List<KeyValuePair<MethodInfo,MethodInfo>> attached=new List<KeyValuePair<MethodInfo,MethodInfo>>();
    private static FieldInfo getTrack,routeTracks,routeDirections,startSpan;
    private static FieldInfo registry,planTracks,routeMembers;
    private static FieldInfo legacyOwner,legacyOwned,legacyRoutes,legacyPublish;
    private static MethodInfo legacyRelease;
    private static bool legacyMatching;
    internal static void Install(Harmony harmony)
    {
        Remove();owner=harmony;context=SynchronizationContext.Current;thread=Thread.CurrentThread.ManagedThreadId;
        AppDomain.CurrentDomain.AssemblyLoad+=Loaded;
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())if(assembly.GetName().Name==AssemblyName)Attach(assembly);
    }
    internal static void Remove()
    {
        AppDomain.CurrentDomain.AssemblyLoad-=Loaded;
        if(owner!=null)foreach(var patch in attached)owner.Unpatch(patch.Key,patch.Value);
        attached.Clear();owner=null;context=null;installed.Clear();
        getTrack=routeTracks=routeDirections=startSpan=null;
        registry=planTracks=routeMembers=null;
        legacyOwner=legacyOwned=legacyRoutes=legacyPublish=null;legacyRelease=null;
        legacyMatching=false;
    }
    private static void Loaded(object sender,AssemblyLoadEventArgs args)
    {
        if(args.LoadedAssembly.GetName().Name!=AssemblyName)return;
        var current=owner;
        if(Thread.CurrentThread.ManagedThreadId==thread||context==null)Attach(args.LoadedAssembly);
        else context?.Post(_=>{if(owner==current)Attach(args.LoadedAssembly);},null);
    }
    private static void Attach(Assembly assembly)
    {
        if(owner==null||installed.Contains(assembly))return;
        var adapter=assembly.GetType("AdvancedDispatcherSystem.Signals.Adapter",false);if(adapter==null)return;
        var patches=new List<KeyValuePair<MethodInfo,MethodInfo>>();
        try
        {
            var reserveRoute=AccessTools.DeclaredMethod(adapter,"ReserveRoute");
            registry=AccessTools.DeclaredField(adapter,"registry");routeMembers=AccessTools.DeclaredField(adapter,"routeSignals");
            getTrack=AccessTools.DeclaredField(adapter,"getTrack");
            planTracks=reserveRoute==null?null:AccessTools.DeclaredField(reserveRoute.GetParameters()[0].ParameterType,"tracks");
            if(registry==null||routeMembers==null||planTracks==null||getTrack?.FieldType!=typeof(Func<RailTrack,string>))throw new InvalidOperationException("Unsupported ADS signal registry");
            var refresh=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),nameof(RouteReserved));
            foreach(string target in new[]{"ReserveRoute","ReplaceRoute"})
            {
                var method=AccessTools.DeclaredMethod(adapter,target);if(method==null)continue;
                owner.Patch(method,postfix:new HarmonyMethod(refresh));patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(method,refresh));
            }
            var release=AccessTools.DeclaredMethod(adapter,"ReleaseRoute");
            var before=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),nameof(BeforeRelease));var after=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),nameof(RouteReleased));
            owner.Patch(release,prefix:new HarmonyMethod(before),postfix:new HarmonyMethod(after));
            patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(release,before));patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(release,after));
            void Prefix(string target,string method)
            {
                var original=AccessTools.DeclaredMethod(adapter,target,new[]{typeof(Signal)});if(original==null)return;
                if(original.ReturnType!=typeof(bool))throw new InvalidOperationException("Unsupported ADS method: "+target);
                var prefix=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),method);
                owner.Patch(original,prefix:new HarmonyMethod(prefix));patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(original,prefix));
            }
            Prefix("NativeOwnsBlock",nameof(OwnsBlock));Prefix("HasForeignTrackOwner",nameof(ForeignOwner));
            var occupied=AccessTools.DeclaredMethod(adapter,"BlockOccupied",new[]{typeof(TrackBlock)});
            if(occupied!=null&&occupied.ReturnType==typeof(bool))
            {
                var interval=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),nameof(BlockOccupied));
                owner.Patch(occupied,prefix:new HarmonyMethod(interval));patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(occupied,interval));
            }
            var match=AccessTools.DeclaredMethod(adapter,"RouteSignalMatches");
            if(match==null||match.ReturnType!=typeof(bool)||match.GetParameters().Length!=2||match.GetParameters()[0].ParameterType!=typeof(Signal))throw new InvalidOperationException("Unsupported ADS route matcher");
            var plan=match.GetParameters()[1].ParameterType;
            routeTracks=AccessTools.DeclaredField(plan,"tracks");routeDirections=AccessTools.DeclaredField(plan,"directions");startSpan=AccessTools.DeclaredField(plan,"startSpan");
            if(routeTracks?.FieldType!=typeof(string[])||routeDirections?.FieldType!=typeof(int[])||startSpan?.FieldType!=typeof(double))throw new InvalidOperationException("Unsupported ADS route data");
            legacyMatching=AccessTools.DeclaredMethod(adapter,"ApplicableRouteIndices")==null;
            var matchPrefix=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),nameof(LegacyMatch));
            owner.Patch(match,prefix:new HarmonyMethod(matchPrefix));patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(match,matchPrefix));
            var applies=AccessTools.DeclaredMethod(adapter,"RouteSignalApplies");
            if(applies!=null)
            {
                var appliesPrefix=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),nameof(IntervalApplies));
                owner.Patch(applies,prefix:new HarmonyMethod(appliesPrefix));patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(applies,appliesPrefix));
            }
            // 1.0.1 already handles native starting rails and oriented loop
            // occurrences. Repair the old matcher only when that API is absent.
            if(legacyMatching)
            {
                legacyOwner=AccessTools.DeclaredField(adapter,"guardOwner");legacyOwned=AccessTools.DeclaredField(adapter,"ownedReservations");legacyRoutes=AccessTools.DeclaredField(adapter,"routeSignals");legacyPublish=AccessTools.DeclaredField(adapter,"publishReservation");legacyRelease=AccessTools.DeclaredMethod(adapter,"ReleaseRoute");
                if(legacyOwner==null||legacyOwned==null||legacyRoutes==null||legacyPublish==null||legacyRelease==null)throw new InvalidOperationException("Unsupported ADS reservation guard");
                var guard=AccessTools.DeclaredMethod(adapter,"GuardUpdate");var intact=AccessTools.DeclaredMethod(adapter,"RouteReservationsIntact");
                var updatePrefix=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),nameof(LegacyUpdate));var intactPostfix=AccessTools.DeclaredMethod(typeof(DispatcherIntegration),nameof(LegacyIntact));
                owner.Patch(guard,prefix:new HarmonyMethod(updatePrefix));patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(guard,updatePrefix));
                owner.Patch(intact,postfix:new HarmonyMethod(intactPostfix));patches.Add(new KeyValuePair<MethodInfo,MethodInfo>(intact,intactPostfix));
            }
            attached.AddRange(patches);installed.Add(assembly);Debug.Log("[RuSignals] Advanced Dispatcher signal adapter connected.");
        }
        catch(Exception error)
        {
            foreach(var patch in patches)owner.Unpatch(patch.Key,patch.Value);
            Debug.LogWarning("[RuSignals] Advanced Dispatcher adapter unavailable: "+error.Message);
        }
    }
    private static bool Ours(Signal signal)=>signal?.Definition?.Aspects?.Length>0&&signal.Definition.Aspects[0] is RuAspectDefinition;
    private static void RouteReserved(object __instance,object __0,string __result)
    {
        if(__result!=null||!SignalManager.Running||SignalManager.Instance==null)return;
        var tracks=new HashSet<string>((string[])planTracks.GetValue(__0));var identify=(Func<RailTrack,string>)getTrack.GetValue(__instance);
        var members=new HashSet<Signal>();
        foreach(DictionaryEntry pair in (IDictionary)registry.GetValue(__instance))
            if(pair.Value is Signal signal&&Ours(signal)&&signal.Controller.PlacementInfo.HasValue&&tracks.Contains(identify(signal.Controller.PlacementInfo.Value.Track)))members.Add(signal);
        // Evaluate downstream first, including unreservable automatic heads.
        // Native modes, occupancy and turnout proof choose the resulting aspect.
        var visited=new HashSet<Signal>();
        void Refresh(Signal signal)
        {
            if(!visited.Add(signal)||signal.Block==null||signal.Block.ShouldBeUpdated)return;
            var next=signal.GetNextController();if(next!=null)foreach(var head in next.AllSignals)if(members.Contains(head))Refresh(head);
            signal.UpdateAspect(true);
        }
        foreach(var signal in members)Refresh(signal);
    }
    private static void BeforeRelease(object __instance,string __0,out Signal[] __state)
    {
        var values=new List<Signal>();
        if(((IDictionary)routeMembers.GetValue(__instance))[__0] is IEnumerable members)foreach(Signal signal in members)if(Ours(signal))values.Add(signal);
        __state=values.ToArray();
    }
    private static void RouteReleased(string __result,Signal[] __state)
    {
        if(__result!=null||!SignalManager.Running||SignalManager.Instance==null)return;
        foreach(var signal in __state)if(signal.Controller.Exists&&signal.Block!=null&&!signal.Block.ShouldBeUpdated)signal.UpdateAspect(true);
    }
    private static bool OwnsBlock(Signal __0,ref bool __result)
    {
        if(!Ours(__0)||!AutomaticBlock.HasWindow(__0.Block))return true;
        __result=IntervalReservations.OwnsCurrentBlock(__0);return false;
    }
    private static bool BlockOccupied(TrackBlock __0,ref bool __result)
    {
        // ADS's rail cache merges every interval on the same physical rail.
        // Ask the SDK for the actual bounded block; native/other packs retain
        // ADS's own cache, crossing policy and occupation implementation.
        if(!AutomaticBlock.HasWindow(__0))return true;
        __result=__0.IsOccupied(CrossingCheckMode.IntersectionOnly);return false;
    }
    private static bool ForeignOwner(Signal __0,ref bool __result)
    {
        if(!Ours(__0)||!AutomaticBlock.HasWindow(__0.Block))return true;
        __result=TrackReserver.IsSignalReservedByAnother(__0)||!AutomaticBlock.TryReserveDirection(__0);return false;
    }
    private static bool LegacyMatch(object __instance,Signal __0,object __1,ref bool __result)
    {
        if(!Ours(__0)||(!legacyMatching&&!AutomaticBlock.HasWindow(__0.Block)))return true;
        __result=Matches(__0,(string[])routeTracks.GetValue(__1),(int[])routeDirections.GetValue(__1),(double)startSpan.GetValue(__1),(Func<RailTrack,string>)getTrack.GetValue(__instance));return false;
    }
    private static bool IntervalApplies(object __instance,Signal __0,object __1,ref bool __result)
    {
        if(!Ours(__0)||!AutomaticBlock.HasWindow(__0.Block))return true;
        __result=Matches(__0,(string[])routeTracks.GetValue(__1),(int[])routeDirections.GetValue(__1),(double)startSpan.GetValue(__1),(Func<RailTrack,string>)getTrack.GetValue(__instance),false);return false;
    }
    private static void LegacyIntact(object __instance,string __0,ref bool __result)
    {
        if(!__result)return;
        if(((IDictionary)legacyRoutes.GetValue(__instance))[__0] is IEnumerable members)
            foreach(Signal signal in members)if(Ours(signal)&&AutomaticBlock.HasWindow(signal.Block)&&!IntervalReservations.OwnsCurrentBlock(signal)){__result=false;return;}
    }
    private static bool LegacyUpdate(Signal __0,ref bool __1,ref bool __result)
    {
        if(!Ours(__0)||!AutomaticBlock.HasWindow(__0.Block))return true;
        var adapter=legacyOwner.GetValue(null);if(adapter==null)return true;
        if(!(((IDictionary)legacyOwned.GetValue(adapter))[__0] is string route)||IntervalReservations.OwnsCurrentBlock(__0))return true;
        legacyRelease.Invoke(adapter,new[]{(object)route,legacyPublish.GetValue(adapter)});__1=false;__result=false;return false;
    }
    private static bool Matches(Signal signal,string[] route,int[] directions,double start,Func<RailTrack,string> identify,bool checkBlock=true)
    {
        if(signal?.Block==null||!signal.Controller.PlacementInfo.HasValue||route==null||directions?.Length!=route.Length||identify==null)return false;
        var paths=signal.Block.Tracks;if(paths==null||paths.Length==0)return false;
        var placement=signal.Controller.PlacementInfo.Value;var junction=signal.Controller as JunctionSignalController;
        var track=signal.Controller as TrackSignalController;
        bool atMast=AutomaticBlock.HasWindow(signal.Block)&&paths[0].Track==placement.Track&&paths[0].Direction==AutomaticBlock.TravelDirection(placement.Direction);
        var gate=junction!=null||atMast?placement.Track:track?.StartingTrack??placement.Track;
        int direction=junction!=null||atMast||track==null?(placement.Direction==TrackDirection.In?1:-1):(track.Direction==TrackDirection.Out?1:-1);
        string gateId=identify(gate);if(string.IsNullOrEmpty(gateId)||double.IsNaN(start)||double.IsInfinity(start))return false;
        for(int index=0;index<route.Length;index++)
        {
            if(route[index]!=gateId||directions[index]!=direction)continue;
            double origin=index==0?start:direction>0?0:double.MaxValue;
            double span=placement.Track==gate?placement.Span:direction>0?0:double.MaxValue;
            if(double.IsNaN(span)||direction*(span-origin)<-.01)continue;
            int blockIndex=index;
            if(junction!=null)
            {
                var j=junction.Junction;if(j?.inBranch?.track==null||j.outBranches==null)continue;
                int incoming=index;string id=identify(j.inBranch.track);
                while(incoming<route.Length&&route[incoming]!=id)incoming++;
                if(incoming+1>=route.Length||directions[incoming]!=(j.inBranch.first?-1:1))continue;
                var heads=signal.IsShunting?junction.ShuntingSignals:junction.Signals;int head=Array.IndexOf(heads,signal);
                if(head<0||heads.Length==0)continue;
                bool bound=false;
                for(int b=0;b<j.outBranches.Count;b++)if(b%heads.Length==head&&identify(j.outBranches[b].track)==route[incoming+1]&&directions[incoming+1]==(j.outBranches[b].first?1:-1)){bound=true;break;}
                if(!bound)continue;
                if(identify(paths[0].Track)!=route[index])blockIndex=incoming+1;
            }
            if(atMast&&junction==null&&track!=null&&track.StartingTrack!=placement.Track)
            {
                int nativeStart=Array.FindIndex(paths,t=>t.Track==track.StartingTrack&&t.Direction==track.Direction);
                if(nativeStart<0||blockIndex+nativeStart>=route.Length)continue;
            }
            if(!checkBlock)return true;
            bool follows=true;
            for(int b=0;b<paths.Length&&blockIndex+b<route.Length;b++)
                if(identify(paths[b].Track)!=route[blockIndex+b]||(paths[b].Direction==TrackDirection.Out?1:-1)!=directions[blockIndex+b]){follows=false;break;}
            if(follows)return true;
        }
        return false;
    }
}
