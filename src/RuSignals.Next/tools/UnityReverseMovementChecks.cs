using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void ReverseMovementChecks(Assembly sdk,Assembly assembly,object pack,Func<string,Vector3,Vector3,int,Component> rail)
    {
        int start=passed;var ab=assembly.GetType("RuSignals.AutomaticBlock",true);var networkField=ab.GetField("Network",All);var saved=networkField.GetValue(null);
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var info=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var direction=info.GetField("Direction").FieldType;
        var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);
        var reserve=sdk.GetType("Signals.Game.Railway.TrackReserver",true);var manual=assembly.GetType("RuSignals.NativeSdk",true).GetProperty("ManualOccupancy",All).GetValue(null,null);
        var settings=sdk.GetType("Signals.Game.SignalsMod",true).GetProperty("Settings",All).GetValue(null,null);var option=settings.GetType().GetField("SpecialReservation");bool savedOption=(bool)option.GetValue(settings);
        var track=rail("Reverse gameplay interval",new Vector3(7600,0,7600),Vector3.forward,500);
        var front=FixtureBogie(track,out var frontTraveller,out var bogies);var rear=FixtureBogie(track,out var rearTraveller,out var sameBogies);
        var bodies=new GameObject("Reverse gameplay velocities");var frontBody=new GameObject("Front native body");frontBody.transform.SetParent(bodies.transform);
        var rearBody=new GameObject("Rear native body");rearBody.transform.SetParent(bodies.transform);
        var rbFront=frontBody.AddComponent<Rigidbody>();rbFront.useGravity=false;var rbRear=rearBody.AddComponent<Rigidbody>();rbRear.useGravity=false;
        front.GetType().GetField("rb",All).SetValue(front,rbFront);rear.GetType().GetField("rb",All).SetValue(rear,rbRear);
        var owners=new List<object>();
        try
        {
            option.SetValue(settings,false);
            foreach(string slot in new[]{"Signal","EntrySignal","ExitPassengerSignal","DivergingSignal","OldEntrySignal","OldExitSignal"})foreach(int sign in new[]{1,-1})
            {
                var network=Create(saved.GetType());networkField.SetValue(null,network);
                var outDir=Enum.Parse(direction,"Out");var travel=Enum.Parse(direction,sign==1?"Out":"In");var face=Enum.Parse(direction,sign==1?"In":"Out");double mast=sign==1?100:400;
                var spanType=assembly.GetType("RuSignals.RailSpan",true);var spans=Array.CreateInstance(spanType,1);spans.SetValue(Create(spanType,track,outDir,0d,500d),0);
                var corridor=Create(assembly.GetType("RuSignals.Corridor",true),spans);Call(Field(network,"Corridors"),"Add",corridor);
                var tracks=Field(network,"Tracks");Call(tracks,"Add",track,Create(tracks.GetType().GetGenericArguments()[1],corridor,outDir));network.GetType().GetField("Ready",All).SetValue(network,true);
                Func<object> make=()=>
                {
                    var clone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);
                    var owner=Create(basic,clone,Create(placement,track,face,(int)(mast*2),mast,false));owners.Add(owner);var head=((Array)basic.GetProperty("Signals").GetValue(owner,null)).GetValue(0);
                    var infos=Array.CreateInstance(info,1);infos.SetValue(Create(info,track,travel),0);var native=Create(blockType,null,500f);blockType.GetProperty("Tracks").SetValue(native,infos,null);
                    foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(native,"");
                    Call(head,"SetBlock",native);return head;
                };
                var signal=make();
                var reverseClone=UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"));NormalizePlacedFixture(reverseClone);reverseClone.gameObject.SetActive(true);
                var reverseOwner=Create(basic,reverseClone,Create(placement,track,travel,sign==1?800:200,sign==1?400d:100d,false));owners.Add(reverseOwner);
                var reverseHead=((Array)basic.GetProperty("Signals").GetValue(reverseOwner,null)).GetValue(0);
                var nextClone=UnityEngine.Object.Instantiate((Component)Field(pack,"Signal"));NormalizePlacedFixture(nextClone);nextClone.gameObject.SetActive(true);
                var nextOwner=Create(basic,nextClone,Create(placement,track,travel,500,250d,false));owners.Add(nextOwner);
                var reverseInfos=Array.CreateInstance(info,1);reverseInfos.SetValue(Create(info,track,face),0);var reverseBlock=Create(blockType,nextOwner,500f);blockType.GetProperty("Tracks").SetValue(reverseBlock,reverseInfos,null);
                foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(reverseBlock,"");Call(reverseHead,"SetBlock",reverseBlock);
                Action<bool,string> expect=(open,label)=>
                {
                    // Force a fresh poll to replay successive game frames in
                    // this synchronous fixture, without changing Time itself.
                    network.GetType().GetField("polledFrame",All).SetValue(network,-1);Call(signal,"UpdateAspect",true);
                    var aspect=signal.GetType().GetProperty("CurrentAspect").GetValue(signal,null);string value=Field(Call(aspect,"GetDefinition"),"Indication").ToString();
                    Check(value==(open?"Yellow":"Stop"),slot+" / "+sign+" / "+label);
                };
                Action<double,double,int> move=(first,last,movement)=>
                {
                    Call(frontTraveller,"MoveToSpan",mast+first*sign);Call(rearTraveller,"MoveToSpan",mast+last*sign);
                    rbFront.velocity=rbRear.velocity=Vector3.forward*(movement*sign);
                };
                move(-20,-40,1);Check(rbFront.velocity.z==sign&&rbRear.velocity.z==sign,"Actual native bodies supply movement / "+slot+" / "+sign);expect(true,"approach opens");
                for(int cycle=0;cycle<3;cycle++)
                {
                    move(30,10,1);expect(false,"entry closes behind train / "+cycle);
                    move(1,-19,-1);expect(false,"last bogie beyond mast holds red / "+cycle);
                    Call(reverseHead,"UpdateAspect",true);var reverseAspect=reverseHead.GetType().GetProperty("CurrentAspect").GetValue(reverseHead,null);
                    Check(Field(Call(reverseAspect,"GetDefinition"),"Indication").ToString()=="Yellow",slot+" / "+sign+" consistent reverse movement releases the free following block / "+cycle);
                    move(-5,-25,-1);expect(true,"full reverse withdrawal reopens without clearing physical rail / "+cycle);
                    move(-5,-25,0);expect(true,"stopped after withdrawal / "+cycle);
                    move(-5,-25,1);expect(true,"direction changes for reentry / "+cycle);
                }
                Call(manual,"Add",track);expect(false,"manual closure retained");Call(manual,"Remove",track);expect(true,"manual closure releases with approach occupied");
                var holder=make();Check((bool)reserve.GetMethod("ReserveForSignal",new[]{signal.GetType()}).Invoke(null,new[]{holder}),slot+" foreign route reserved");expect(false,"foreign reservation retained");reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{holder});expect(true,"foreign release reopens");
                move(120,-25,1);rbFront.velocity=-rbRear.velocity;expect(false,"actual opposing movement and forward bogie retained");
                move(-5,-25,1);expect(true,"actual conflict clears while both bogies remain on physical rail");
                Check(!(bool)Field(corridor,"Conflict"),slot+" live conflict state clears after movement becomes consistent");
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"reverse-movement-verification.json"),"{\"checks\":"+(passed-start)+",\"profiles\":6,\"directions\":2,\"cycles_per_profile\":3,\"actual_bogies_and_velocities\":true,\"last_bogie_guard\":true,\"manual_and_foreign_reservation_guards\":true,\"live_conflict_release\":true,\"scope\":\"Actual SDK UpdateAspect, native bogies and Rigidbody velocities; successive polls on synthetic rails, no supplied save or game session\"}");
            Debug.Log("RUS_REVERSE_MOVEMENT_PASS "+(passed-start)+" / six profiles / both directions / three cycles / actual bogies and velocities / live guards");
        }
        finally
        {
            option.SetValue(settings,savedOption);reserve.GetMethod("ClearAll").Invoke(null,null);Call(manual,"Remove",track);
            Call(bogies,"Remove",front);Call(sameBogies,"Remove",rear);UnityEngine.Object.DestroyImmediate(front.gameObject);UnityEngine.Object.DestroyImmediate(rear.gameObject);UnityEngine.Object.DestroyImmediate(bodies);
            foreach(var owner in owners){var def=(Component)basic.GetProperty("Definition").GetValue(owner,null);Call(owner,"Destroy");UnityEngine.Object.DestroyImmediate(def.gameObject);}
            networkField.SetValue(null,saved);UnityEngine.Object.DestroyImmediate(track.gameObject);
        }
    }
}
