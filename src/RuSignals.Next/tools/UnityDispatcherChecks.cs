// Real ADS adapters are loaded without Main.Load, IPC, auth or a web host.
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using System.Linq.Expressions;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void DwarfOffsetChecks(Assembly assembly,object controller,string slot)
    {
        if(slot!="ExitSignal")return;
        var def=(Component)controller.GetType().GetProperty("Definition").GetValue(controller,null);
        Check(Math.Abs((float)Field(def,"Offset")+1.85f)<.00001f,"Dwarf exit is displaced 0.30 m away from its track on the native right side");
        var shape=def.GetComponent(assembly.GetType("RuSignals.ShieldView",true));var original=def.transform.position;
        foreach(bool oval in new[]{false,true})foreach(bool opposite in new[]{false,true})
        {
            Call(shape,"SetOval",oval);def.transform.position=new Vector3(opposite?1.85f:-1.85f,0,0);
            var visible=def.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.name!="PhoneReserver"&&r.GetComponent<TextMesh>()==null&&r.sharedMaterial?.shader.name!="RuSignals/Signal halo").ToArray();
            // Translation affects the full imported assembly, including lenses,
            // foundation, label and lamp datum; the rail placement stays native.
            Check(visible.All(r=>opposite?r.bounds.min.x>1.25f:r.bounds.max.x< -1.25f),"Dwarf exit complete geometry stays clear of a 2.50 m wide low vehicle envelope / oval "+oval+" / opposite "+opposite);
            if(!opposite){var bounds=visible[0].bounds;foreach(var r in visible)bounds.Encapsulate(r.bounds);Debug.Log("RUS_DWARF_CLEARANCE_BOUNDS oval="+oval+" "+bounds);}
        }
        def.transform.position=original;Call(shape,"SetOval",false);Debug.Log("RUS_DWARF_OFFSET_PASS native right side / 0.30 m outward / square and round / both placement sides / whole assembly");
    }
    private static void DispatcherChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,object harmony,Func<string,Vector3,Vector3,int,Component> rail)
    {
        string path=Environment.GetEnvironmentVariable("RUS_ADS_DIR");if(string.IsNullOrEmpty(path))return;
        var integration=assembly.GetType("RuSignals.DispatcherIntegration",true);
        string gameDll=Environment.GetEnvironmentVariable("RUS_ADS_GAME_DLL")??Path.Combine(path,"AdvancedDispatcherSystem.dll");
        var game=Assembly.LoadFrom(gameDll);var ads=Assembly.LoadFrom(Path.Combine(path,"AdvancedDispatcherSystem.Signals.dll"));
        Check((int)integration.GetField("installed",All).GetValue(null).GetType().GetProperty("Count").GetValue(integration.GetField("installed",All).GetValue(null),null)==1,"Late ADS assembly discovery installs without a menu polling loop");
        integration.GetMethod("Remove",All).Invoke(null,null);
        var adapterType=ads.GetType("AdvancedDispatcherSystem.Signals.Adapter",true);var adapter=Activator.CreateInstance(adapterType);
        var planType=game.GetType("AdvancedDispatcherSystem.Core.RoutePlan",true);
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var signalType=sdk.GetType("Signals.Game.Signal",true);
        var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);var infoType=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var direction=infoType.GetField("Direction").FieldType;
        var outDir=Enum.Parse(direction,"Out");var inDir=Enum.Parse(direction,"In");
        var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];
        var manager=UnityEngine.Object.FindObjectOfType(sdk.GetType("Signals.Game.SignalManager",true));
        var loaded=manager.GetType().GetField("s_loaded",All);object oldLoaded=loaded.GetValue(null);loaded.SetValue(null,true);
        var registry=(IList)manager.GetType().GetProperty("AllControllers").GetValue(manager,null);var saved=registry.Cast<object>().ToArray();registry.Clear();
        var ownedControllers=new System.Collections.Generic.List<object>();
        var a=rail("ADS approach",new Vector3(3000,0,3000),Vector3.forward,500);
        var b=rail("ADS branch",new Vector3(3000,0,3500),Vector3.forward,500);
        var c=rail("ADS wrong branch",new Vector3(3001,0,3500),new Vector3(.1f,0,1).normalized,500);
        var reserve=sdk.GetType("Signals.Game.Railway.TrackReserver",true);
        Action<object,string,object> set=(obj,name,value)=>obj.GetType().GetField(name,All).SetValue(obj,value);
        Func<Component,object> def=clone=>
        {
            foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true))foreach(var reference in component.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
                if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
            foreach(var d in clone.GetComponentsInChildren(common.GetType("Signals.Common.SignalDefinition",true),true))set(d,"PhysicalReserver",null);
            clone.gameObject.SetActive(true);return clone;
        };
        Func<object,object> signal=owner=>((Array)basic.GetProperty("Signals").GetValue(owner,null)).GetValue(0);
        Func<Component,double,object> placed=(t,span)=>Create(placement,t,inDir,(int)(span*2),span,false);
        Func<Type,object[],object> controller=(type,args)=>{var value=Create(type,args);ownedControllers.Add(value);registry.Add(value);return value;};
        Func<Component,double,double,bool,object> block=(t,start,end,window)=>
        {
            var infos=Array.CreateInstance(infoType,1);infos.SetValue(Create(infoType,t,end>start?outDir:inDir),0);
            var value=Create(blockType,null,(float)Math.Abs(end-start));blockType.GetProperty("Tracks").SetValue(value,infos,null);
            foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(value,key=="_trackTrimmedNumber"?"2":key=="_station"?"ADS":"");
            if(window)
            {
                var spanType=assembly.GetType("RuSignals.RailSpan",true);var spans=Array.CreateInstance(spanType,1);spans.SetValue(Create(spanType,t,end>start?outDir:inDir,start,end),0);
                var network=assembly.GetType("RuSignals.AutomaticBlock",true).GetField("Network",All).GetValue(null);
                var full=Create(blockType,null,500f);blockType.GetProperty("Tracks").SetValue(full,infos,null);
                foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(full,key=="_trackTrimmedNumber"?"2":key=="_station"?"ADS":"");
                Call(Field(network,"Windows"),"Add",value,Create(assembly.GetType("RuSignals.BlockWindow",true),full,spans));
            }
            return value;
        };
        var getTrackType=adapterType.GetField("getTrack",All).FieldType;var arg=Expression.Parameter(getTrackType.GetGenericArguments()[0],"track");
        var identify=Expression.Lambda(getTrackType,Expression.Condition(Expression.Equal(arg,Expression.Constant(null,arg.Type)),Expression.Constant(null,typeof(string)),Expression.Property(Expression.Convert(arg,typeof(UnityEngine.Object)),"name")),arg).Compile();
        set(adapter,"getTrack",identify);set(adapter,"manager",manager);set(adapter,"active",true);
        foreach(string callback in new[]{"sendSignal","sendBlock"}){var type=adapterType.GetField(callback,All).FieldType;var parameter=Expression.Parameter(type.GetGenericArguments()[0]);set(adapter,callback,Expression.Lambda(type,Expression.Empty(),parameter).Compile());}
        set(adapter,"removeCapturedBlock",(Action<string>)(_=>{}));
        var nativeRegistry=(IDictionary)Field(adapter,"registry");
        Func<string,object,string[],int[],object> plan=(id,s,tracks,dirs)=>
        {
            var value=Activator.CreateInstance(planType);set(value,"id",id);set(value,"tracks",tracks);set(value,"directions",dirs);set(value,"startSpan",0d);set(value,"reservedSignals",new[]{"s"+Field(s,"Id")});set(value,"reservationTracks",tracks);set(value,"reservationMode","protected");return value;
        };
        var match=adapterType.GetMethod("RouteSignalMatches",All);Func<object,object,bool> matches=(s,p)=>(bool)match.Invoke(adapter,new[]{s,p});
        Func<object,string> acquire=p=>
        {
            var method=adapterType.GetMethod("ReserveRoute",All);var args=new object[method.GetParameters().Length];args[0]=p;args[1]=(Func<int,bool,bool>)((id,on)=>true);args[2]=null;if(args.Length>3)args[3]=null;
            return (string)method.Invoke(adapter,args);
        };
        Action<string> release=id=>Check(Call(adapter,"ReleaseRoute",id,(Func<int,bool,bool>)((s,on)=>true))==null,"ADS releases route "+id);
        bool newer=adapterType.GetMethod("NativeOwnsBlock",All)!=null;
        try
        {
            var one=controller(basic,new[]{def(UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"))),placed(a,100)});var s1=signal(one);Call(s1,"SetBlock",block(a,100,200,true));set(s1,"NameOverride","ADS1");nativeRegistry.Add(Field(s1,"Id"),s1);
            var two=controller(basic,new[]{def(UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"))),placed(a,300)});var s2=signal(two);Call(s2,"SetBlock",block(a,300,400,true));set(s2,"NameOverride","ADS2");nativeRegistry.Add(Field(s2,"Id"),s2);
            Check((bool)reserve.GetMethod("ReserveForSignal",new[]{signalType}).Invoke(null,new[]{s1}),"Native reservation accepts the first interval");
            if(newer)Check((bool)adapterType.GetMethod("HasForeignTrackOwner",All).Invoke(null,new[]{s2}),"Unadapted ADS mistakes a disjoint interval for a foreign whole-track owner");
            reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{s1});
            var junctionType=a.GetType().GetField("inJunction").FieldType;var junctionParent=new GameObject("junc-l ADS native fixture");var junctionGo=new GameObject("ADS native gate");junctionGo.transform.SetParent(junctionParent.transform);junctionGo.SetActive(false);var junction=junctionGo.AddComponent(junctionType);
            var branches=(IList)Field(junction,"outBranches");var branchType=branches.GetType().GetGenericArguments()[0];
            Func<Component,bool,object> branch=(t,first)=>{var item=Create(branchType);set(item,"track",t);set(item,"first",first);return item;};
            set(junction,"inBranch",branch(a,false));branches.Add(branch(b,true));branches.Add(branch(c,true));set(junction,"selectedBranch",(byte)0);
            var junctionController=controller(sdk.GetType("Signals.Game.Controllers.JunctionSignalController",true),new[]{def(UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"))),junction,b,placed(a,450)});
            var gate=signal(junctionController);Call(gate,"SetBlock",block(b,0,500,false));nativeRegistry.Add(Field(gate,"Id"),gate);
            var route=plan("gate",gate,new[]{a.name,b.name},new[]{1,1});
            Check(matches(gate,route),"Both reference ADS adapters accept the ordinary forward native gate");
            var loop=plan("oriented-loop",gate,new[]{a.name,c.name,a.name,b.name},new[]{-1,1,1,1});
            Check(matches(gate,loop)==newer,"Reference ADS 1.0.0 selects the first wrong-direction occurrence; 1.0.1 resolves the oriented occurrence");
            // Existing assemblies are handled as well as late AssemblyLoad.
            integration.GetMethod("Install",All).Invoke(null,new[]{harmony});
            Check(matches(gate,route),"Adapted ADS resolves a mast on the approach and native block on the branch");
            Check(matches(gate,loop),"Adapted ADS binds the native gate to the correct oriented loop occurrence");
            Check(!matches(gate,plan("bad-branch",gate,new[]{a.name,c.name},new[]{1,1})),"ADS still rejects the wrong turnout branch");
            Check(!matches(gate,plan("bad-direction",gate,new[]{a.name,b.name},new[]{1,-1})),"ADS still rejects the wrong branch orientation");
            Check(!matches(gate,plan("before-gate",gate,new[]{a.name},new[]{1})),"Route ending before the gate selects no outgoing block");
            var passedGate=plan("passed",gate,new[]{a.name,b.name},new[]{1,1});set(passedGate,"startSpan",451d);Check(!matches(gate,passedGate),"Already passed mast is not new authority");
            Call(junctionController,"Destroy");ownedControllers.Remove(junctionController);registry.Remove(junctionController);nativeRegistry.Remove(Field(gate,"Id"));UnityEngine.Object.DestroyImmediate(((Component)basic.GetProperty("Definition").GetValue(junctionController,null)).gameObject);UnityEngine.Object.DestroyImmediate(junctionParent);
            var approachController=controller(sdk.GetType("Signals.Game.Controllers.TrackSignalController",true),new[]{def(UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"))),b,outDir,placed(a,450)});
            var approachHead=signal(approachController);Call(approachHead,"SetBlock",block(b,0,500,false));nativeRegistry.Add(Field(approachHead,"Id"),approachHead);
            var approachPlan=plan("physical-approach",approachHead,new[]{a.name,b.name},new[]{1,1});
            Check(matches(approachHead,approachPlan),"ADS accepts a production interval starting at the physical mast before the native starting rail");
            Check(!matches(approachHead,plan("ends-before-start",approachHead,new[]{a.name},new[]{1})),"ADS cannot reserve an approach-only route ending before the native starting rail");
            var passedApproach=plan("passed-approach",approachHead,new[]{a.name,b.name},new[]{1,1});set(passedApproach,"startSpan",451d);
            Check(!matches(approachHead,passedApproach),"Clipping does not select a mast already behind the route origin");
            Check(acquire(approachPlan)==null,"Actual ADS acquisition accepts the correctly clipped approach-to-native block");
            Check((bool)Call(adapter,"RouteReservationsIntact","physical-approach"),"Corrected approach reservation survives actual ADS ownership validation");
            release("physical-approach");
            Call(approachController,"Destroy");ownedControllers.Remove(approachController);registry.Remove(approachController);nativeRegistry.Remove(Field(approachHead,"Id"));UnityEngine.Object.DestroyImmediate(((Component)basic.GetProperty("Definition").GetValue(approachController,null)).gameObject);
            // Starting at 250 excludes the first head but retains its existing
            // native claim, so ADS must not confuse its representative with s2.
            Check((bool)reserve.GetMethod("ReserveForSignal",new[]{signalType}).Invoke(null,new[]{s1}),"Independent first interval remains natively reserved");
            var second=plan("interval",s2,new[]{a.name},new[]{1});set(second,"startSpan",250d);
            Call(s2,"TurnOff");
            Check(acquire(second)==null,"Real ADS reserves the second disjoint interval on the same native rail");
            Check(!(bool)signalType.GetProperty("IsOff").GetValue(s2,null),"ADS reservation evaluates a previously off distant native signal immediately");
            Check((bool)Call(adapter,"RouteReservationsIntact","interval"),"ADS owns its actual interval even when another head represents the rail");
            int cleared=0,made=0;var actionType=reserve.GetField("ReservationCleared").FieldType;var p=Expression.Parameter(signalType);
            Action<bool> counted=on=>{if(on)made++;else cleared++;};
            var clearDelegate=Expression.Lambda(actionType,Expression.Invoke(Expression.Constant(counted),Expression.Constant(false)),p).Compile();
            var madeDelegate=Expression.Lambda(actionType,Expression.Invoke(Expression.Constant(counted),Expression.Constant(true)),p).Compile();
            var clearField=reserve.GetField("ReservationCleared");var madeField=reserve.GetField("ReservationMade");
            clearField.SetValue(null,Delegate.Combine((Delegate)clearField.GetValue(null),clearDelegate));madeField.SetValue(null,Delegate.Combine((Delegate)madeField.GetValue(null),madeDelegate));
            Check((bool)reserve.GetMethod("UpdateReservation").Invoke(null,new[]{s2}),"ADS guard accepts native reservation refresh");
            Check(cleared==0&&made==0&&(bool)Call(adapter,"RouteReservationsIntact","interval"),"Native refresh does not emit a false route cancellation before ADS validation");
            clearField.SetValue(null,Delegate.Remove((Delegate)clearField.GetValue(null),clearDelegate));madeField.SetValue(null,Delegate.Remove((Delegate)madeField.GetValue(null),madeDelegate));
            reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{s2});Check((bool)reserve.GetMethod("HasReservation",new[]{signalType}).Invoke(null,new[]{s2}),"Protected ADS route prevents ordinary native cancellation");
            release("interval");Check(!(bool)reserve.GetMethod("HasReservation",new[]{signalType}).Invoke(null,new[]{s2})&&(bool)reserve.GetMethod("HasReservation",new[]{signalType}).Invoke(null,new[]{s1}),"ADS release clears only its own interval");
            Call(s2,"SetBlock",block(a,150,350,true));var overlap=plan("overlap",s2,new[]{a.name},new[]{1});set(overlap,"startSpan",250d);
            Check(acquire(overlap)=="RESERVATION_CONFLICT","Real ADS still refuses overlapping interval protection");
            reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{s1});Call(s2,"SetBlock",block(a,300,400,true));
            Check(acquire(second)==null,"ADS can reserve again after cleanup");Call(s2,"SetBlock",block(a,300,410,true));
            Check(!(bool)Call(adapter,"RouteReservationsIntact","interval"),"ADS detects changed interval endpoints even on an identical physical rail");
            Check(!(bool)reserve.GetMethod("UpdateReservation").Invoke(null,new[]{s2}),"Changed interval invalidates ADS reservation instead of renewing stale protection");
            release("interval");
            integration.GetMethod("Remove",All).Invoke(null,null);Check(integration.GetField("owner",All).GetValue(null)==null,"Optional dispatcher lifecycle removes its owner");
            integration.GetMethod("Install",All).Invoke(null,new[]{harmony});
            var report="{\"adapter\":\""+(newer?"1.0.1":"1.0.0")+"\",\"reference_mismatch_reproduced\":true,\"branch_direction_rejections\":true,\"disjoint_intervals\":true,\"overlap_rejected\":true,\"refresh_retains_route\":true,\"protected_release\":true,\"scope\":\"Actual ADS/SDK DLLs in Unity; synthetic rails; no dispatcher host, save or multiplayer\"}";
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"dispatcher-verification.json"),report);
            Debug.Log("RUS_DISPATCHER_PASS "+(newer?"1.0.1":"1.0.0")+" / real adapter / native branch / direction / intervals / refresh / protected release / lifecycle");
        }
        catch(Exception error){Debug.LogException(error);throw;}
        finally
        {
            integration.GetMethod("Remove",All).Invoke(null,null);
            ((IDisposable)adapter).Dispose();reserve.GetMethod("ClearAll").Invoke(null,null);
            foreach(var value in ownedControllers){var clone=(Component)basic.GetProperty("Definition").GetValue(value,null);Call(value,"Destroy");UnityEngine.Object.DestroyImmediate(clone.gameObject);}
            foreach(var t in new[]{a,b,c})UnityEngine.Object.DestroyImmediate(t.gameObject);
            registry.Clear();foreach(var value in saved)registry.Add(value);loaded.SetValue(null,oldLoaded);
        }
    }
}
