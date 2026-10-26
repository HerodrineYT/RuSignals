using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void ReservationBoundaryChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,object harmony,Func<string,Vector3,Vector3,int,Component> rail)
    {
        var ab=assembly.GetType("RuSignals.AutomaticBlock",true);var networkField=ab.GetField("Network",All);var saved=networkField.GetValue(null);
        var network=Create(saved.GetType());networkField.SetValue(null,network);
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var info=sdk.GetType("Signals.Game.Railway.TrackInfo",true);
        var direction=info.GetField("Direction").FieldType;var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];
        var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);var reserve=sdk.GetType("Signals.Game.Railway.TrackReserver",true);
        var getLength=sdk.GetType("Signals.Game.Extensions",true).GetMethod("GetLength",All);var prefix=typeof(UnityRewriteCheck).GetMethod("TerminalFixtureLength",All);
        var patch=harmony.GetType().GetMethods().Single(m=>m.Name=="Patch"&&m.GetParameters().Length>=4);var args=new object[patch.GetParameters().Length];args[0]=getLength;args[1]=Create(harmony.GetType().Assembly.GetType("HarmonyLib.HarmonyMethod",true),prefix);patch.Invoke(harmony,args);
        var track=rail("Reservation boundary rail",new Vector3(7000,0,7000),Vector3.forward,500);
        var owners=new System.Collections.Generic.List<object>();int firstCount=passed;
        Func<double,object,object> owner=(span,face)=>
        {
            var clone=UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);
            var value=Create(basic,clone,Create(placement,track,face,(int)(span*2),span,false));owners.Add(value);return value;
        };
        Func<object,object,object> native=(travel,next)=>
        {
            var infos=Array.CreateInstance(info,1);infos.SetValue(Create(info,track,travel),0);
            var value=Create(blockType,next,500f);blockType.GetProperty("Tracks").SetValue(value,infos,null);
            foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(value,"");return value;
        };
        Func<object,object> signal=c=>((Array)basic.GetProperty("Signals").GetValue(c,null)).GetValue(0);
        Func<object,double,int,bool> contains=(block,span,sign)=>
        {
            var a=new object[]{block,track,span,sign,false};bool known=(bool)ab.GetMethod("TryContains").Invoke(null,a);Check(known,"Boundary is an actual production interval");return (bool)a[4];
        };
        try
        {
            foreach(bool ready in new[]{false,true})foreach(int sign in new[]{1,-1})
            {
                network.GetType().GetField("Ready",All).SetValue(network,ready);
                var travel=Enum.Parse(direction,sign==1?"Out":"In");var face=Enum.Parse(direction,sign==1?"In":"Out");
                double own=sign==1?100:400,end=sign==1?400:100;
                var c=owner(own,face);var next=owner(end,face);var head=signal(c);var source=native(travel,next);Call(head,"SetBlock",source);
                var actual=signal(c).GetType().GetProperty("Block").GetValue(head,null);
                Check(!ReferenceEquals(source,actual),"Native train block is clipped even without an automatic cut / "+ready+" / "+sign);
                Check(Math.Abs((float)blockType.GetProperty("Length").GetValue(actual,null)-300)<.001,"Block length is between the two masts");
                Check(!contains(actual,own-20*sign,sign),"Approach before the owning signal is excluded");
                Check(contains(actual,own+20*sign,sign),"Route after the owning signal is included");
                Check(!contains(actual,end+20*sign,sign),"Route beyond the next signal is excluded");
                Check((bool)reserve.GetMethod("ReserveForSignal",new[]{head.GetType()}).Invoke(null,new[]{head}),"Native reservation accepts the corrected block");
                var aspects=((Array)head.GetType().GetProperty("AllAspects").GetValue(head,null)).Cast<object>().ToArray();
                Call(head,"ChangeAspect",Array.FindIndex(aspects,a=>Field(Call(a,"GetDefinition"),"Indication").ToString()=="Green"));
                var previous=owner(own-50*sign,face);var previousHead=signal(previous);Call(previousHead,"SetBlock",native(travel,c));
                var previousBlock=previousHead.GetType().GetProperty("Block").GetValue(previousHead,null);
                Check(!(bool)ab.GetMethod("Conflicts",All).Invoke(null,new[]{actual,previousBlock}),"Green signal ahead does not reserve the preceding throat signal's block");
                Check((bool)reserve.GetMethod("ReserveForSignal",new[]{head.GetType()}).Invoke(null,new[]{previousHead}),"Previous throat head can reserve its own adjacent interval while the next is reserved");
                Call(previousHead,"UpdateAspect",true);
                var selected=previousHead.GetType().GetProperty("CurrentAspect").GetValue(previousHead,null);
                Check(Field(Call(selected,"GetDefinition"),"Indication").ToString()=="Green","Previous throat head opens green with the next green reservation intact");
                reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{previousHead});
                var spanType=assembly.GetType("RuSignals.RailSpan",true);var query=native(travel,null);
                Action<double,double> window=(start,finish)=>
                {
                    var spans=Array.CreateInstance(spanType,1);spans.SetValue(Create(spanType,track,travel,start,finish),0);
                    query=native(travel,null);Call(Field(network,"Windows"),"Add",query,Create(assembly.GetType("RuSignals.BlockWindow",true),native(travel,null),spans));
                };
                window(own-30*sign,own-10*sign);
                Check(!(bool)ab.GetMethod("Conflicts",All).Invoke(null,new[]{actual,query}),"Reservation does not claim an independent approach interval");
                window(own+10*sign,own+30*sign);
                Check((bool)ab.GetMethod("Conflicts",All).Invoke(null,new[]{actual,query}),"Reservation still protects its actual forward interval");
                reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{head});
            }
            foreach(int sign in new[]{1,-1})
            {
                var travel=Enum.Parse(direction,sign==1?"Out":"In");var face=Enum.Parse(direction,sign==1?"In":"Out");var c=owner(sign==1?500:0,face);var head=signal(c);Call(head,"SetBlock",native(travel,null));
                var empty=head.GetType().GetProperty("Block").GetValue(head,null);
                Check(((Array)blockType.GetProperty("Tracks").GetValue(empty,null)).Length==0,"A mast at the route end cannot claim the whole approach as fallback");
                Check(!(bool)reserve.GetMethod("ReserveForSignal",new[]{head.GetType()}).Invoke(null,new[]{head}),"An empty forward route cannot acquire a reservation");
                Call(head,"UpdateAspect",true);var aspect=head.GetType().GetProperty("CurrentAspect").GetValue(head,null);
                Check(Field(Call(aspect,"GetDefinition"),"Indication").ToString()=="Stop","An empty forward route remains restrictive");
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"reservation-boundary-verification.json"),"{\"checks\":"+(passed-firstCount)+",\"both_directions\":true,\"without_ab\":true,\"last_block_without_cut\":true,\"approach_excluded\":true,\"forward_protected\":true,\"empty_forward_route_rejected\":true,\"previous_throat_opens_green\":true,\"scope\":\"Actual SDK SetBlock and TrackReserver; synthetic geometry, not supplied save\"}");
            Debug.Log("RUS_RESERVATION_BOUNDARY_PASS "+(passed-firstCount)+" / both directions / no AB and final AB block / approach excluded");
        }
        finally
        {
            reserve.GetMethod("ClearAll").Invoke(null,null);
            foreach(var c in owners){var def=(Component)basic.GetProperty("Definition").GetValue(c,null);Call(c,"Destroy");UnityEngine.Object.DestroyImmediate(def.gameObject);}
            networkField.SetValue(null,saved);UnityEngine.Object.DestroyImmediate(track.gameObject);
            var unpatch=harmony.GetType().GetMethods().Single(m=>m.Name=="Unpatch"&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(MethodInfo));unpatch.Invoke(harmony,new object[]{getLength,prefix});
        }
    }
}
