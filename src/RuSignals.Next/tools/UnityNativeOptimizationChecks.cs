using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static List<int> queryTrace;
    private static void NativeQueryTrace(UnityEngine.Object __0){if(queryTrace!=null)queryTrace.Add(__0.GetInstanceID());}
    [Serializable] public sealed class NativeQueryReport
    {
        public string version,dll_sha256,sdk_module,scope;
        public int occupation_cases,reservation_cases,bounds_cases,intersection_cases;
        public bool exact_results,exact_query_order,foreign_pack_fallback,unknown_sdk_fallback,live_mutations;
        public LiveBenchRow[] reference,optimized;
    }
    private static bool FloatBits(float a,float b)=>BitConverter.ToInt32(BitConverter.GetBytes(a),0)==BitConverter.ToInt32(BitConverter.GetBytes(b),0);
    private static bool VectorBits(Vector3 a,Vector3 b)=>FloatBits(a.x,b.x)&&FloatBits(a.y,b.y)&&FloatBits(a.z,b.z);
    private static bool BoundsBits(Bounds a,Bounds b)=>VectorBits(a.center,b.center)&&VectorBits(a.extents,b.extents);
    private static void NativeOptimizationChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,object harmony,Func<string,Vector3,Vector3,int,Component> rail)
    {
        var fast=assembly.GetType("RuSignals.NativeOptimizations",true);var enabled=fast.GetField("Enabled",All);
        Check((bool)enabled.GetValue(null),"Native optimization verifies the actual SDK module");
        Check(!(bool)fast.GetMethod("Verified",All).Invoke(null,new object[]{Guid.NewGuid()}),"Unknown SDK module retains native implementations");
        var managerType=sdk.GetType("Signals.Game.SignalManager",true);var packField=managerType.GetField("s_pack",All);
        var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);var checker=sdk.GetType("Signals.Game.Railway.TrackChecker",true);
        var reserve=sdk.GetType("Signals.Game.Railway.TrackReserver",true);var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);
        var info=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var direction=info.GetField("Direction").FieldType;var outDir=Enum.Parse(direction,"Out");
        var native=sdk.GetType("Signals.Game.Extensions",true);var logic=native.GetMethod("IsLogicYardTrack",All);var oldLogic=fast.GetField("logicTrack",All).GetValue(null);
        var methodType=harmony.GetType().Assembly.GetType("HarmonyLib.HarmonyMethod",true);var patch=harmony.GetType().GetMethod("Patch");
        var patched=new List<Tuple<MethodInfo,MethodInfo>>();
        Action<MethodInfo,MethodInfo> add=(target,prefix)=>{var args=new object[patch.GetParameters().Length];args[0]=target;args[1]=Create(methodType,prefix);patch.Invoke(harmony,args);patched.Add(Tuple.Create(target,prefix));};
        add(logic,typeof(UnityRewriteCheck).GetMethod("FixtureYardTrack",All));
        add(checker.GetMethod("IsOccupied",All),typeof(UnityRewriteCheck).GetMethod("NativeQueryTrace",All));
        add(checker.GetMethod("IsReservedByAnother",All),typeof(UnityRewriteCheck).GetMethod("NativeQueryTrace",All));
        var occupancy=(object)assembly.GetType("RuSignals.NativeSdk",true).GetProperty("ManualOccupancy",All).GetValue(null,null);
        var reservations=(IDictionary)assembly.GetType("RuSignals.NativeSdk",true).GetProperty("ReservationTracks",All).GetValue(null,null);
        var crossingField=checker.GetField("s_intersectionMap",All);var savedCrossings=crossingField.GetValue(null);var crossings=(IDictionary)Activator.CreateInstance(crossingField.FieldType);crossingField.SetValue(null,crossings);
        var tracks=Enumerable.Range(0,7).Select(i=>rail((i==1||i==5?"[Y] ":"")+"Native query "+i,new Vector3(6000+i*20,0,6000),Vector3.forward,10)).ToArray();
        var crossingGo=new GameObject("Native query crossing datum");crossingGo.transform.position=new Vector3(6000,0,6000);
        var pointsType=checker.GetNestedType("TrackIntersectionPoints",All);var crossing=Create(pointsType,tracks[0]);var points=(IList)pointsType.GetProperty("IntersectionPoints").GetValue(crossing,null);
        var pairType=points.GetType().GetGenericArguments()[0];points.Add(Create(pairType,tracks[6],crossingGo.transform));crossings[tracks[0]]=crossing;
        var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.name="Native crossing train collider";obstacle.layer=10;obstacle.transform.position=crossingGo.transform.position+Vector3.up;Physics.SyncTransforms();
        var infos=Array.CreateInstance(info,4);for(int i=0;i<4;i++)infos.SetValue(Create(info,tracks[i],outDir),i);
        var extras=Array.CreateInstance(tracks[0].GetType(),3);extras.SetValue(tracks[4],0);extras.SetValue(tracks[5],1);extras.SetValue(tracks[0],2);
        var block=Create(blockType,null,40f);blockType.GetProperty("Tracks").SetValue(block,infos,null);blockType.GetProperty("ExtraTracks").SetValue(block,extras,null);
        foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(block,"");
        var owners=new List<object>();
        Func<object> controller=()=>{var clone=UnityEngine.Object.Instantiate((Component)Field(pack,"Signal"));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);var c=Create(basic,clone,null);owners.Add(c);return c;};
        var owner=controller();var other=controller();var signal=((Array)basic.GetProperty("Signals").GetValue(owner,null)).GetValue(0);var foreign=((Array)basic.GetProperty("Signals").GetValue(other,null)).GetValue(0);
        Call(signal,"SetBlock",block);Call(foreign,"SetBlock",block);
        var mode=blockType.GetMethod("IsOccupied").GetParameters()[0].ParameterType;var values=Enum.GetValues(mode).Cast<object>().ToArray();
        int occupationCases=0,reservationCases=0,boundsCases=0,intersectionCases=0;
        var reference=new List<LiveBenchRow>();var optimized=new List<LiveBenchRow>();
        Func<Func<bool>,bool> compare=action=>
        {
            enabled.SetValue(null,false);queryTrace=new List<int>();bool before=action();var old=queryTrace.ToArray();
            enabled.SetValue(null,true);queryTrace.Clear();bool after=action();
            bool equal=before==after&&old.SequenceEqual(queryTrace);queryTrace=null;return equal;
        };
        try
        {
            Check((bool)oldLogic.GetType().GetMethod("Invoke").Invoke(oldLogic,new object[]{tracks[1]}),"The production cached native classification delegate retains Harmony interception");
            foreach(bool collider in new[]{false,true})
            {
                obstacle.SetActive(collider);Physics.SyncTransforms();
                for(int mask=0;mask<64;mask++)
                {
                    for(int i=0;i<6;i++)Call(occupancy,(mask&(1<<i))!=0?"Add":"Remove",tracks[i]);
                    foreach(var crossingMode in values)foreach(string name in new[]{"IsOccupied","IsOccupiedIgnoreLogic"})
                    {
                        var method=blockType.GetMethod(name);Check(compare(()=>(bool)method.Invoke(block,new[]{crossingMode})),"Native occupation result and exact track-query order / "+name+" / "+crossingMode+" / "+mask+" / "+collider);occupationCases++;
                    }
                }
            }
            foreach(var t in tracks)Call(occupancy,"Remove",t);obstacle.SetActive(false);Physics.SyncTransforms();
            foreach(bool own in new[]{false,true})for(int mask=0;mask<128;mask++)
            {
                for(int i=0;i<7;i++){if((mask&(1<<i))!=0)reservations[tracks[i]]=own?signal:foreign;else reservations.Remove(tracks[i]);}
                var method=reserve.GetMethod("IsSignalReservedByAnother");Check(compare(()=>(bool)method.Invoke(null,new[]{signal})),"Native reservations include exact own/foreign, extras and crossing ownership / "+own+" / "+mask);reservationCases++;
            }
            foreach(var t in tracks)reservations.Remove(t);
            var bezier=sdk.GetType("Signals.Game.Curves.CubicBezier",true);var helper=sdk.GetType("Signals.Game.Curves.BezierHelper",true);
            var random=new System.Random(310);var curves=new List<object>();
            Func<float> sample=()=> (float)(random.NextDouble()*200-100);
            for(int i=0;i<512;i++)curves.Add(Create(bezier,new Vector3(sample(),sample(),sample()),new Vector3(sample(),sample(),sample()),new Vector3(sample(),sample(),sample()),new Vector3(sample(),sample(),sample())));
            curves.Add(Create(bezier,Vector3.zero,Vector3.zero,Vector3.zero,Vector3.zero));
            curves.Add(Create(bezier,new Vector3(-1,0,0),Vector3.zero,Vector3.right,new Vector3(2,0,0)));
            curves.Add(Create(bezier,new Vector3(float.NaN,0,0),Vector3.zero,Vector3.right,new Vector3(float.PositiveInfinity,0,0)));
            var bounds=bezier.GetMethod("GetBounds");
            foreach(var curve in curves)
            {
                enabled.SetValue(null,false);var old=(Bounds)bounds.Invoke(curve,null);enabled.SetValue(null,true);var actual=(Bounds)bounds.Invoke(curve,null);
                Check(BoundsBits(old,actual),"Native cubic bounds match every float bit / "+boundsCases);boundsCases++;
                bezier.GetField("P1").SetValue(curve,new Vector3(700,25,-310));
                enabled.SetValue(null,false);old=(Bounds)bounds.Invoke(curve,null);enabled.SetValue(null,true);actual=(Bounds)bounds.Invoke(curve,null);
                Check(BoundsBits(old,actual),"Native cubic bounds observe changed points / "+boundsCases);boundsCases++;
            }
            var intersects=helper.GetMethod("Intersects",new[]{bezier,bezier,typeof(float),typeof(Vector3).MakeByRefType()});
            for(int i=0;i<128;i++)foreach(float precision in new[]{.5f,.05f})
            {
                var args=new[]{curves[i],curves[(i*7+13)%512],(object)precision,Vector3.zero};enabled.SetValue(null,false);bool old=(bool)intersects.Invoke(null,args);var point=(Vector3)args[3];
                args[3]=Vector3.zero;enabled.SetValue(null,true);bool actual=(bool)intersects.Invoke(null,args);
                Check(old==actual&&VectorBits(point,(Vector3)args[3]),"Native recursive intersection result and point match every float bit / "+i+" / "+precision);intersectionCases++;
            }
            var foreignPack=ScriptableObject.CreateInstance(common.GetType("Signals.Common.SignalPack",true));foreignPack.GetType().GetField("ModId").SetValue(foreignPack,"fixture.other.pack");
            try
            {
                packField.SetValue(null,foreignPack);var args=new object[]{curves[0],default(Bounds)};
                Check((bool)fast.GetMethod("Bounds",All).Invoke(null,args),"Another signal pack keeps the original curve query");
                args=new object[]{block,values[0],false};Check((bool)fast.GetMethod("Occupied",All).Invoke(null,args),"Another signal pack keeps the original occupation query");
                args=new object[]{signal,false};Check((bool)fast.GetMethod("ReservedByAnother",All).Invoke(null,args),"Another signal pack keeps the original reservation query");
            }
            finally{packField.SetValue(null,pack);UnityEngine.Object.DestroyImmediate(foreignPack);}
            queryTrace=null;
            var unpatch=harmony.GetType().GetMethods().Single(m=>m.Name=="Unpatch"&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(MethodInfo));
            foreach(var pair in patched.Where(p=>p.Item1!=logic).ToArray()){unpatch.Invoke(harmony,new object[]{pair.Item1,pair.Item2});patched.Remove(pair);}
            // Compile call sites once; no MethodInfo.Invoke inside timed loops.
            var b=Expression.Parameter(typeof(object));var callBounds=Expression.Lambda<Func<object,Bounds>>(Expression.Call(Expression.Convert(b,bezier),bounds),b).Compile();
            var callOccupied=Expression.Lambda<Func<object,bool>>(Expression.Call(Expression.Convert(b,blockType),blockType.GetMethod("IsOccupied"),Expression.Constant(values[0],mode)),b).Compile();
            var callReserved=Expression.Lambda<Func<object,bool>>(Expression.Call(reserve.GetMethod("IsSignalReservedByAnother"),Expression.Convert(b,signal.GetType())),b).Compile();
            int cursor=0;long checksum=0;
            foreach(bool on in new[]{false,true})
            {
                enabled.SetValue(null,on);var rows=on?optimized:reference;
                rows.Add(LiveMeasure("native clear block, 7 entries",100000,()=>{if(callOccupied(block))checksum++;}));
                rows.Add(LiveMeasure("native clear reservation, 6 tracks",100000,()=>{if(callReserved(signal))checksum++;}));
                rows.Add(LiveMeasure("native cubic bounds",100000,()=>{if(callBounds(curves[(cursor++)%512]).size.x>0)checksum++;}));
            }
            Check(checksum>0,"Native query benchmarks execute all their call sites");
            var report=new NativeQueryReport{version=(string)assembly.GetType("RuSignals.Mod",true).GetField("Version").GetRawConstantValue(),dll_sha256=DllHash(Environment.GetEnvironmentVariable("RUS_V2_DLL")??Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_ROOT"),"src/RuSignals.Next/bin/Release/net48/RuSignals.dll")),sdk_module=blockType.Module.ModuleVersionId.ToString(),scope="Actual DVSignals methods toggled between original and integrated prefixes in Unity Mono; synthetic tracks, manual occupations and real crossing physics. Logic-yard ID classification supplied by the fixture. No game map or FPS; -1 allocation counter unavailable.",occupation_cases=occupationCases,reservation_cases=reservationCases,bounds_cases=boundsCases,intersection_cases=intersectionCases,exact_results=true,exact_query_order=true,foreign_pack_fallback=true,unknown_sdk_fallback=true,live_mutations=true,reference=reference.ToArray(),optimized=optimized.ToArray()};
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"native-optimization-verification.json"),JsonUtility.ToJson(report,true));
            Debug.Log("RUS_NATIVE_OPTIMIZATION_PASS original SDK bodies / live occupation and reservations / exact call order / bit-identical bounds and intersections / pack and SDK guards");
        }
        finally
        {
            queryTrace=null;enabled.SetValue(null,true);fast.GetField("logicTrack",All).SetValue(null,oldLogic);crossingField.SetValue(null,savedCrossings);packField.SetValue(null,pack);
            foreach(var t in tracks){Call(occupancy,"Remove",t);reservations.Remove(t);}
            var unpatch=harmony.GetType().GetMethods().Single(m=>m.Name=="Unpatch"&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(MethodInfo));
            foreach(var pair in patched)unpatch.Invoke(harmony,new object[]{pair.Item1,pair.Item2});
            foreach(var c in owners){var def=(Component)basic.GetProperty("Definition").GetValue(c,null);Call(c,"Destroy");if(def!=null)UnityEngine.Object.DestroyImmediate(def.gameObject);}
            foreach(var track in tracks)UnityEngine.Object.DestroyImmediate(track.gameObject);UnityEngine.Object.DestroyImmediate(crossingGo);UnityEngine.Object.DestroyImmediate(obstacle);
        }
    }
}
