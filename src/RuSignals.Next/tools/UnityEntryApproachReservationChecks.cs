using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void EntryApproachReservationChecks(Assembly sdk,Assembly assembly,object pack,Func<string,Vector3,Vector3,int,Component> rail)
    {
        int initial=passed;var ab=assembly.GetType("RuSignals.AutomaticBlock",true);var networkField=ab.GetField("Network",All);var savedNetwork=networkField.GetValue(null);
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var signalType=sdk.GetType("Signals.Game.Signal",true);var info=sdk.GetType("Signals.Game.Railway.TrackInfo",true);
        var direction=info.GetField("Direction").FieldType;var outDir=Enum.Parse(direction,"Out");var inDir=Enum.Parse(direction,"In");
        var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);
        var spanType=assembly.GetType("RuSignals.RailSpan",true);var windowType=assembly.GetType("RuSignals.BlockWindow",true);var corridorType=assembly.GetType("RuSignals.Corridor",true);
        var reserve=sdk.GetType("Signals.Game.Railway.TrackReserver",true);var reserveMethod=reserve.GetMethod("ReserveForSignal",new[]{signalType});var hasReservation=reserve.GetMethod("HasReservation",new[]{signalType});
        var settings=sdk.GetType("Signals.Game.SignalsMod",true).GetProperty("Settings",All).GetValue(null,null);var option=settings.GetType().GetField("SpecialReservation");bool savedOption=(bool)option.GetValue(settings);
        var manual=assembly.GetType("RuSignals.NativeSdk",true).GetProperty("ManualOccupancy",All).GetValue(null,null);
        var track=rail("Receiving entry corridor terminal fragment",new Vector3(8200,0,8200),Vector3.forward,500);
        var bogie=FixtureBogie(track,out var traveller,out var bogies);Call(bogies,"Remove",bogie);
        var owners=new List<object>();object network=null;
        Action poll=()=>network.GetType().GetField("polledFrame",All).SetValue(network,-1);
        Func<object,bool> reserved=s=>(bool)hasReservation.Invoke(null,new[]{s});
        Func<object,bool> acquire=s=>(bool)reserveMethod.Invoke(null,new[]{s});
        Func<object,bool> directionAllowed=s=>{poll();return (bool)ab.GetMethod("DirectionAllowed",All).Invoke(null,new[]{s});};
        Func<object,string> indication=s=>
        {
            poll();Call(s,"UpdateAspect",true);var selected=signalType.GetProperty("CurrentAspect").GetValue(s,null);
            return selected==null?"Dark":Field(Call(selected,"GetDefinition"),"Indication").ToString();
        };
        Func<object,object> native=travel=>
        {
            var infos=Array.CreateInstance(info,1);infos.SetValue(Create(info,track,travel),0);var block=Create(blockType,null,500f);blockType.GetProperty("Tracks").SetValue(block,infos,null);
            foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(block,"");return block;
        };
        Func<double,double,object> bounded=(start,end)=>
        {
            var travel=end>start?outDir:inDir;var block=native(travel);blockType.GetProperty("Length").GetSetMethod(true).Invoke(block,new object[]{(float)Math.Abs(end-start)});
            var spans=Array.CreateInstance(spanType,1);spans.SetValue(Create(spanType,track,travel,start,end),0);
            Call(Field(network,"Windows"),"Add",block,Create(windowType,native(travel),spans));return block;
        };
        Func<string,double,double,object> head=(slot,start,end)=>
        {
            var clone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);
            var controller=Create(basic,clone,Create(placement,track,end>start?inDir:outDir,(int)(start*2),start,false));owners.Add(controller);
            var s=((Array)basic.GetProperty("Signals").GetValue(controller,null)).GetValue(0);Call(s,"SetBlock",bounded(start,end));return s;
        };
        try
        {
            option.SetValue(settings,false);
            foreach(string slot in new[]{"EntrySignal","OldEntrySignal"})foreach(int sign in new[]{1,-1})
            {
                reserve.GetMethod("ClearAll").Invoke(null,null);network=Create(savedNetwork.GetType());networkField.SetValue(null,network);
                var spans=Array.CreateInstance(spanType,1);spans.SetValue(Create(spanType,track,outDir,0d,500d),0);var path=Create(corridorType,spans);
                Call(Field(network,"Corridors"),"Add",path);var tracks=Field(network,"Tracks");Call(tracks,"Add",track,Create(tracks.GetType().GetGenericArguments()[1],path,outDir));
                network.GetType().GetField("Ready",All).SetValue(network,true);
                Func<double,double> position=p=>sign==1?p:500-p;string name=slot+" / "+sign;
                var entry=head(slot,position(400),position(500));var rear=head("Signal",position(300),position(200));
                Check((bool)Field(network,"Ready"),name+" actual generated-corridor policy enabled");
                Check(Math.Abs((float)blockType.GetProperty("Length").GetValue(signalType.GetProperty("Block").GetValue(entry,null),null)-100)<.001,name+" actual entry claims only final 100 m");
                Check(acquire(rear)&&reserved(rear),name+" actual native opposite approach interval acquired");
                Check(!(bool)ab.GetMethod("Conflicts",All).Invoke(null,new[]{signalType.GetProperty("Block").GetValue(entry,null),signalType.GetProperty("Block").GetValue(rear,null)}),name+" saved primary intervals are physically disjoint");
                Check(!(bool)reserve.GetMethod("IsSignalReservedByAnother").Invoke(null,new[]{entry}),name+" actual native foreign reservation query proves reception free");
                Check(directionAllowed(entry),name+" opposing authority strictly behind mast does not veto receiving direction");
                Check(indication(entry)=="Yellow",name+" actual native UpdateAspect opens free receiving entry");
                Check(acquire(entry)&&reserved(entry)&&reserved(rear),name+" native reserve accepts receiving entry and retains separate approach reservation");
                reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{entry});
                Call(traveller,"MoveToSpan",position(380));Call(bogies,"Add",bogie);
                Check(directionAllowed(entry),name+" stationary locomotive entirely before mast preserves receiving direction");
                Check(indication(entry)=="Yellow",name+" stationary locomotive before mast does not close actual entry");
                Check(acquire(entry)&&reserved(rear),name+" native reserve also accepts stopped approach locomotive");
                reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{entry});
                Call(traveller,"MoveToSpan",position(410));
                Check(indication(entry)=="Stop",name+" actual bogie beyond mast still closes entry");
                Call(bogies,"Remove",bogie);poll();
                Call(manual,"Add",track);
                Check(!directionAllowed(entry)&&indication(entry)=="Stop",name+" manual rail closure remains restrictive");
                Check(!acquire(entry),name+" manual closure still prevents native entry reservation");
                Call(manual,"Remove",track);poll();
                reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{rear});
                var obstruction=head("Signal",position(430),position(410));
                Check(acquire(obstruction),name+" foreign opposing authority ahead acquired");
                Check(!directionAllowed(entry)&&indication(entry)=="Stop",name+" any authority physically ahead still closes entry");
                Check(!acquire(entry),name+" actual forward overlap prevents native entry reservation");
                // Changing an owner's live block must not replace its already
                // protected footprint in the receiving-entry exception.
                Call(obstruction,"SetBlock",bounded(position(300),position(200)));
                Check(!directionAllowed(entry),name+" saved protection ahead remains restrictive after owner's live route moves behind");
                Check((bool)reserve.GetMethod("IsSignalReservedByAnother").Invoke(null,new[]{entry})&&!acquire(entry),name+" native conflict uses saved protected footprint");
                reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{obstruction});
                var opaque=head("Signal",position(300),position(200));
                signalType.GetProperty("Block").GetSetMethod(true).Invoke(opaque,new[]{native(sign==1?inDir:outDir)});
                Check(acquire(opaque),name+" foreign native full-rail block acquires ordinary protection");
                Check(indication(entry)=="Stop"&&!acquire(entry),name+" unknown full-rail reservation cannot be discarded as behind");
                reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{opaque});
                rear=head("Signal",position(300),position(200));Check(acquire(rear),name+" reverse approach authority restored for non-entry role guards");
                foreach(string ordinarySlot in new[]{"Signal","ExitPassengerSignal","DivergingSignal"})
                {
                    var ordinary=head(ordinarySlot,position(400),position(500));
                    Check(!directionAllowed(ordinary)&&indication(ordinary)=="Stop",name+" "+ordinarySlot+" retains whole-corridor direction policy");
                    Check(!acquire(ordinary),name+" "+ordinarySlot+" native reservation still refuses opposing corridor");
                }
                reserve.GetMethod("ClearAll").Invoke(null,null);
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"entry-approach-reservation-verification.json"),"{\"checks\":"+(passed-initial)+",\"profiles\":2,\"directions\":2,\"ready_corridor\":true,\"native_update_and_reserve\":true,\"saved_footprint_guard\":true,\"manual_overlap_unknown_and_nonentry_guards\":true,\"scope\":\"Actual SDK UpdateAspect and TrackReserver, production corridor proof and saved interval reservations; synthetic rails, no game save, server or multiplayer calls\"}");
            Debug.Log("RUS_ENTRY_APPROACH_RESERVATION_PASS "+(passed-initial)+" / electric and mechanical / both directions / active native corridor / saved route guards");
        }
        finally
        {
            option.SetValue(settings,savedOption);reserve.GetMethod("ClearAll").Invoke(null,null);Call(manual,"Remove",track);Call(bogies,"Remove",bogie);UnityEngine.Object.DestroyImmediate(bogie.gameObject);
            foreach(var controller in owners){var clone=(Component)basic.GetProperty("Definition").GetValue(controller,null);Call(controller,"Destroy");UnityEngine.Object.DestroyImmediate(clone.gameObject);}
            networkField.SetValue(null,savedNetwork);UnityEngine.Object.DestroyImmediate(track.gameObject);
        }
    }
}
