// Isolated regression fixture: actual ADS adapter/SDK, no Main.Load or host.
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void DispatcherIntervalCaptureChecks(Assembly sdk,Assembly assembly,object pack,object harmony,Func<string,Vector3,Vector3,int,Component> rail)
    {
        string path=Environment.GetEnvironmentVariable("RUS_ADS_DIR");if(string.IsNullOrEmpty(path))return;
        int initial=passed,red=0;
        var ads=Assembly.LoadFrom(Path.Combine(path,"AdvancedDispatcherSystem.Signals.dll"));
        var adapterType=ads.GetType("AdvancedDispatcherSystem.Signals.Adapter",true);var adapter=Activator.CreateInstance(adapterType);
        var occupancy=adapterType.GetMethod("BlockOccupied",All);var occupiedCache=adapterType.GetField("occupiedTracks",All);
        var integration=assembly.GetType("RuSignals.DispatcherIntegration",true);var ab=assembly.GetType("RuSignals.AutomaticBlock",true);
        var networkField=ab.GetField("Network",All);var previous=networkField.GetValue(null);var network=Create(previous.GetType());networkField.SetValue(null,network);
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var info=sdk.GetType("Signals.Game.Railway.TrackInfo",true);
        var dir=info.GetField("Direction").FieldType;var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];
        var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);var mode=Enum.Parse(blockType.GetMethod("IsOccupied").GetParameters()[0].ParameterType,"IntersectionOnly");
        var reserve=sdk.GetType("Signals.Game.Railway.TrackReserver",true);var signalType=sdk.GetType("Signals.Game.Signal",true);
        var manual=assembly.GetType("RuSignals.NativeSdk",true).GetProperty("ManualOccupancy",All).GetValue(null,null);
        var track=rail("ADS capture regression Road 6",new Vector3(7900,0,7900),Vector3.forward,500);
        var bogie=FixtureBogie(track,out var traveller,out var bogies);var owners=new List<object>();
        Func<object,bool> native=b=>(bool)Call(b,"IsOccupied",mode);
        Func<object,bool> captured=b=>{if(occupiedCache!=null)Call(occupiedCache.GetValue(adapter),"Clear");return (bool)occupancy.Invoke(adapter,new[]{b});};
        Func<double,double,object> bounded=(start,end)=>
        {
            var travel=Enum.Parse(dir,end>start?"Out":"In");var infos=Array.CreateInstance(info,1);infos.SetValue(Create(info,track,travel),0);
            Func<float,object> make=length=>
            {
                var value=Create(blockType,null,length);blockType.GetProperty("Tracks").SetValue(value,infos,null);
                foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(value,"");
                return value;
            };
            var source=make(500);var block=make((float)Math.Abs(end-start));var spanType=assembly.GetType("RuSignals.RailSpan",true);
            var spans=Array.CreateInstance(spanType,1);spans.SetValue(Create(spanType,track,travel,start,end),0);
            Call(Field(network,"Windows"),"Add",block,Create(assembly.GetType("RuSignals.BlockWindow",true),source,spans));return block;
        };
        Func<object,double,bool,object> head=(block,mast,opposite)=>
        {
            var clone=UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);
            var controller=Create(basic,clone,Create(placement,track,Enum.Parse(dir,opposite?"Out":"In"),(int)(mast*2),mast,false));owners.Add(controller);
            var value=((Array)basic.GetProperty("Signals").GetValue(controller,null)).GetValue(0);Call(value,"SetBlock",block);return value;
        };
        try
        {
            integration.GetMethod("Remove",All).Invoke(null,null);
            foreach(int sign in new[]{1,-1})
            {
                double start=sign==1?100:400,end=sign==1?200:300;
                var first=bounded(start,end);var other=bounded(sign==1?300:200,sign==1?400:100);
                Call(traveller,"MoveToSpan",start-20*sign);
                Check(!native(first),"Actual clipped native block excludes a locomotive on approach / "+sign);
                Check(captured(first),"RED: actual unadapted ADS misreports whole rail occupied by an approach locomotive / "+sign);red++;
                Call(traveller,"MoveToSpan",start+20*sign);
                Check(native(first)&&!native(other),"Two disjoint intervals on one actual rail have different occupancy / "+sign);
                Check(captured(first)&&captured(other),"RED: actual unadapted ADS merges disjoint interval occupancy / "+sign);red++;
                var firstContains=new object[]{first,track,start+20*sign,sign,false};var otherContains=new object[]{other,track,start+20*sign,sign,false};
                Check((bool)ab.GetMethod("TryContains").Invoke(null,firstContains)&&(bool)firstContains[4]&&(bool)ab.GetMethod("TryContains").Invoke(null,otherContains)&&!(bool)otherContains[4],"Actual native interval geometry distinguishes blocks sharing the same rail / "+sign);
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"dispatcher-interval-capture-red.json"),"{\"unadapted_red_cases\":"+red+",\"source\":\"Actual unpatched ADS BlockOccupied versus patched SDK TrackBlock.IsOccupied\",\"both_directions\":true}");
            integration.GetMethod("Install",All).Invoke(null,new[]{harmony});
            foreach(int sign in new[]{1,-1})
            {
                double start=sign==1?100:400,end=sign==1?200:300;
                var first=bounded(start,end);var other=bounded(sign==1?300:200,sign==1?400:100);
                Call(traveller,"MoveToSpan",start-20*sign);
                Check(!captured(first)&&!captured(other),"Adapted ADS excludes approach occupation from both disjoint blocks / "+sign);
                Call(traveller,"MoveToSpan",start+20*sign);
                Check(captured(first)&&!captured(other),"Adapted ADS reports only the interval actually occupied / "+sign);
                Call(traveller,"MoveToSpan",start);
                Check(captured(first),"Bogie exactly at the owning mast occupies its block / "+sign);
                Call(traveller,"MoveToSpan",end+3*sign);
                Check(captured(first),"Existing tail margin remains conservative / "+sign);
                Call(traveller,"MoveToSpan",start-20*sign);
                Call(manual,"Add",track);
                Check(captured(first)&&captured(other),"Manual whole-rail closure remains restrictive for every interval / "+sign);
                Call(manual,"Remove",track);
            }
            // Native/foreign blocks keep ADS's original cache and whole-rail contract.
            var nativeInfos=Array.CreateInstance(info,1);nativeInfos.SetValue(Create(info,track,Enum.Parse(dir,"Out")),0);
            var whole=Create(blockType,null,500f);blockType.GetProperty("Tracks").SetValue(whole,nativeInfos,null);
            Check(captured(whole),"Foreign native block still reports whole-rail occupation");
            var oneBlock=bounded(100,200);var one=head(oneBlock,100,false);
            var disjoint=head(bounded(300,400),300,false);var overlap=head(bounded(150,250),150,false);
            var opposing=head(bounded(200,100),200,true);
            Check((bool)reserve.GetMethod("ReserveForSignal",new[]{signalType}).Invoke(null,new[]{one}),"Actual native reservation accepts first bounded block");
            Check(!(bool)reserve.GetMethod("IsSignalReservedByAnother").Invoke(null,new[]{disjoint}),"Disjoint block has no foreign reservation conflict");
            Check((bool)reserve.GetMethod("ReserveForSignal",new[]{signalType}).Invoke(null,new[]{disjoint}),"Actual native reservation accepts disjoint block on same physical rail");
            Check((bool)reserve.GetMethod("IsSignalReservedByAnother").Invoke(null,new[]{overlap}),"Actual overlap remains a foreign reservation conflict");
            Check(!(bool)reserve.GetMethod("ReserveForSignal",new[]{signalType}).Invoke(null,new[]{overlap}),"Native reservation refuses actual overlap");
            Check((bool)reserve.GetMethod("IsSignalReservedByAnother").Invoke(null,new[]{opposing}),"Opposite authority across the same interval remains restricted");
            string report="{\"checks\":"+(passed-initial)+",\"unadapted_red_cases\":"+red+",\"adapter\":\""+ads.GetName().Version+"\",\"approach_excluded\":true,\"disjoint_occupancy\":true,\"native_fallback\":true,\"manual_overlap_opposing_guards\":true,\"scope\":\"Actual loaded ADS BlockOccupied and SDK native interval occupancy/reservations, synthetic rails; no server, save or multiplayer calls\"}";
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"dispatcher-interval-capture-verification.json"),report);
            Debug.Log("RUS_DISPATCHER_INTERVAL_CAPTURE_PASS "+(passed-initial)+" / RED "+red+" / actual adapter capture / both directions / manual, overlap and native fallback");
        }
        finally
        {
            reserve.GetMethod("ClearAll").Invoke(null,null);Call(manual,"Remove",track);Call(bogies,"Remove",bogie);UnityEngine.Object.DestroyImmediate(bogie.gameObject);
            foreach(var controller in owners){var clone=(Component)basic.GetProperty("Definition").GetValue(controller,null);Call(controller,"Destroy");UnityEngine.Object.DestroyImmediate(clone.gameObject);}
            ((IDisposable)adapter).Dispose();networkField.SetValue(null,previous);UnityEngine.Object.DestroyImmediate(track.gameObject);
            integration.GetMethod("Install",All).Invoke(null,new[]{harmony});
        }
    }
}
