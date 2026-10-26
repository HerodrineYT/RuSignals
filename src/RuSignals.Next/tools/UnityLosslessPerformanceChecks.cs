// Benchmarks execute the actual release DLL on Unity's Mono, not SDK doubles.
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
    [Serializable] public sealed class LiveBenchRow
    {
        public string name;public int iterations;public double median_ms;public long allocated_bytes;
    }
    [Serializable] public sealed class LiveOptimizationReport
    {
        public string version,dll_sha256,reference_version,reference_dll_sha256,unity,scope;
        public bool live_collection_replacement,light_state_matches_reference,position_changes_preserved,game_fps_measured;
        public int lighting_scenarios;public LiveBenchRow[] collection_reference,collection_current,lighting;
    }
    private static LiveBenchRow LiveMeasure(string name,int iterations,Action action)
    {
        var allocation=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Static|BindingFlags.Public);
        var allocated=allocation==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),allocation);
        for(int i=0;i<32;i++)action();
        var samples=new List<LiveBenchRow>();
        for(int repeat=0;repeat<5;repeat++)
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();var watch=new System.Diagnostics.Stopwatch();
            long before=allocated==null?0:allocated();watch.Start();for(int i=0;i<iterations;i++)action();watch.Stop();
            samples.Add(new LiveBenchRow{name=name,iterations=iterations,median_ms=watch.Elapsed.TotalMilliseconds,allocated_bytes=allocated==null?-1:allocated()-before});
        }
        return samples.OrderBy(s=>s.median_ms).ElementAt(2);
    }
    private static string DllHash(string path)
    {using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
    private static void LosslessPerformanceChecks(Assembly assembly,Assembly sdk,Component clock,string root,string output,string dll)
    {
        var baselinePath=Environment.GetEnvironmentVariable("RUS_BASELINE_DLL");
        var current=assembly.GetType("RuSignals.NativeSdk",true);
        Func<Type,string,Func<object>> reader=(owner,name)=>Expression.Lambda<Func<object>>(Expression.Convert(Expression.Property(null,owner.GetProperty(name,All)),typeof(object))).Compile();
        var oldRows=new List<LiveBenchRow>();var newRows=new List<LiveBenchRow>();long checksum=0;
        foreach(var name in new[]{"ManualOccupancy","Reservations","ReservationTracks","Timers","Times"})
        {
            var read=reader(current,name);
            string field=name=="ManualOccupancy"?"s_occupied":name=="Reservations"?"s_signals":name=="ReservationTracks"?"s_reservations":name=="Timers"?"s_clearRoutines":"s_times";
            var native=sdk.GetType(name=="ManualOccupancy"?"Signals.Game.Railway.TrackChecker":"Signals.Game.Railway.TrackReserver",true).GetField(field,All);
            // The cached FieldInfo.GetValue path used in 3.0.8. Loading two
            // RuSignals identities into Mono can rebind generated accessors
            // to the current assembly and spoil a getter comparison.
            Func<object> previous=()=>native.GetValue(null);
            object saved=native.GetValue(null);Check(ReferenceEquals(read(),saved)&&ReferenceEquals(previous(),saved),"Fast and 3.0.8 getters read the same native collection: "+name);
            try
            {
                var replacement=Activator.CreateInstance(native.FieldType);native.SetValue(null,replacement);
                Check(ReferenceEquals(read(),replacement)&&ReferenceEquals(previous(),replacement),"SDK field replacement is visible without stale collection caching: "+name);
            }
            finally{native.SetValue(null,saved);}
            Check(ReferenceEquals(read(),saved),"World collection restoration is immediately visible: "+name);
            oldRows.Add(LiveMeasure(name,100000,()=>{if(previous()!=null)checksum++;}));
            newRows.Add(LiveMeasure(name,100000,()=>{if(read()!=null)checksum++;}));
        }
        Check(checksum==5L*2*(32+5*100000),"Actual native collection benchmarks execute every getter");
        int scenarios=0;var lighting=new List<LiveBenchRow>();
        var type=assembly.GetType("RuSignals.SpillView",true);
        var refresh=(Action<Vector3?>)Delegate.CreateDelegate(typeof(Action<Vector3?>),clock,clock.GetType().GetMethod("RefreshSpills",All));
        var gain=(Func<float,float>)Delegate.CreateDelegate(typeof(Func<float,float>),assembly.GetType("RuSignals.LampVisualRules",true).GetMethod("SpillGain"));
        var positionParameter=Expression.Parameter(typeof(Component));
        var position=Expression.Lambda<Func<Component,Vector3>>(Expression.Property(Expression.Convert(positionParameter,type),type.GetProperty("Position",All)),positionParameter).Compile();
        foreach(int count in new[]{256,1024})
        {
            var holder=new GameObject("Lossless lighting workload "+count);var views=new List<Component>();var sources=new Dictionary<Component,Light[]>();
            for(int i=0;i<count;i++)
            {
                var go=new GameObject("Spill benchmark "+i);go.transform.SetParent(holder.transform,false);go.transform.localPosition=new Vector3(i%64*4,0,i/64*4);
                var view=go.AddComponent(type);((Behaviour)view).enabled=false;var lamps=new Light[2];
                for(int j=0;j<2;j++){var lamp=new GameObject("Native source "+j);lamp.transform.SetParent(go.transform,false);lamps[j]=lamp.AddComponent<Light>();lamps[j].cullingMask=j==0?32:64;}
                type.GetField("Lights").SetValue(view,lamps);type.GetField("Masks").SetValue(view,new[]{32,64});((Behaviour)view).enabled=true;views.Add(view);sources.Add(view,lamps);
            }
            try
            {
                Action<Vector3?> verify=camera=>
                {
                    // Stable LINQ ordering is the original strict '<' insertion
                    // order, including equal distances after registry swaps.
                    var registered=((IEnumerable)Field(clock,"spills")).Cast<Component>().ToArray();
                    var nearest=registered.Where(v=>v!=null&&((Behaviour)v).isActiveAndEnabled&&camera.HasValue)
                        .Select(v=>new{view=v,distance=(sources[v][0].transform.position-camera.Value).sqrMagnitude})
                        .Where(v=>v.distance<=10000).OrderBy(v=>v.distance).Take(8).ToDictionary(v=>v.view,v=>gain(Mathf.Sqrt(v.distance)));
                    refresh(camera);bool equal=true;
                    foreach(var view in views)
                    {
                        float expected=nearest.TryGetValue(view,out var found)?found:0;
                        equal&=(float)Field(view,"gain")==expected;
                        for(int j=0;j<2;j++)equal&=sources[view][j].cullingMask==(expected>0?(j==0?32:64):0)&&sources[view][j].shadows==(expected>0?LightShadows.Hard:LightShadows.None);
                    }
                    Check(equal,"Actual spill gain, masks and shadows equal 3.0.8 nearest-eight policy / "+count+" / "+scenarios);scenarios++;
                };
                foreach(var camera in new Vector3?[]{null,Vector3.zero,new Vector3(30,0,0),new Vector3(65,0,0),new Vector3(100,0,0),new Vector3(254,0,14),new Vector3(800,0,0)})verify(camera);
                views[0].gameObject.SetActive(false);verify(Vector3.zero);views[0].gameObject.SetActive(true);verify(Vector3.zero);
                for(int i=0;i<20;i++)verify(new Vector3((i*37)%256,i%3,(i*19)%64));
                holder.transform.position+=new Vector3(700,5,-310);verify(new Vector3(730,5,-310));
                var view0=views[0];var first=sources[view0][0];first.transform.localPosition=Vector3.up*2;
                Check(position(view0)==first.transform.position,"Position cache follows lamp and floating-origin movement / "+count);
                var replacement=new GameObject("Replaced native source");replacement.transform.SetParent(view0.transform,false);replacement.transform.localPosition=new Vector3(1,3,2);
                sources[view0][0]=replacement.AddComponent<Light>();sources[view0][0].cullingMask=first.cullingMask;sources[view0][0].shadows=first.shadows;
                Check(position(view0)==sources[view0][0].transform.position,"Position cache rebinds when the first native light changes / "+count);verify(new Vector3(730,5,-310));
                UnityEngine.Object.DestroyImmediate(replacement);
                Check(position(view0)==view0.transform.position,"Destroyed source safely returns to native root position / "+count);
                sources[view0][0]=first;Check(position(view0)==first.transform.position,"Restored source is rebound after native destruction / "+count);
                verify(null);int cursor=0;
                lighting.Add(LiveMeasure("spill selection "+count,200,()=>{refresh(holder.transform.position+new Vector3((cursor++*17)%256,0,20));}));
            }
            finally{UnityEngine.Object.DestroyImmediate(holder);}
        }
        Check((int)clock.GetType().GetProperty("SpillCount",All).GetValue(clock,null)==0,"Large lighting workloads leave no registered groups");
        var report=new LiveOptimizationReport{version=assembly.GetType("RuSignals.Mod",true).GetField("Version").GetRawConstantValue().ToString(),dll_sha256=DllHash(dll),reference_version="3.0.8",reference_dll_sha256=DllHash(baselinePath),unity=Application.unityVersion,
            scope="Real SDK fields and Unity Mono; synthetic 256/1024 groups, two lights per group, no game map or FPS. -1 allocated_bytes means runtime counter unavailable.",
            live_collection_replacement=true,light_state_matches_reference=true,position_changes_preserved=true,lighting_scenarios=scenarios,collection_reference=oldRows.ToArray(),collection_current=newRows.ToArray(),lighting=lighting.ToArray()};
        File.WriteAllText(Path.Combine(output,"lossless-runtime-verification.json"),JsonUtility.ToJson(report,true));
        Debug.Log("RUS_LOSSLESS_RUNTIME_PASS five live SDK getters / native field replacement / "+scenarios+" lighting scenarios / 256 and 1024 groups / exact gain, masks, shadows / source movement and replacement");
    }
}
