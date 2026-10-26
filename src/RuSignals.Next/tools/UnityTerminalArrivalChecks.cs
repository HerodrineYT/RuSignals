using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    // Sector A at SM is a fan of reception tracks whose OUT ends connect to
    // turnout branches and whose IN ends terminate. Exercise that orientation
    // with the actual SDK walker and block, rather than assigning IsDeadEnd.
    private static bool TerminalFixtureLength(Component __0,ref double __result)
    {var points=Call(__0,"GetKinkedPointSet");__result=(double)Field(points,"span");return false;}
    private static void TerminalArrivalChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,object harmony,Func<string,Vector3,Vector3,int,Component> rail)
    {
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);
        var direction=sdk.GetType("Signals.Game.Railway.TrackInfo",true).GetField("Direction").FieldType;
        var created=new System.Collections.Generic.List<GameObject>();
        var incoming=rail("SM C approach",new Vector3(4000,0,4000),Vector3.forward,300);created.Add(incoming.gameObject);
        var branch=rail("[track through]",new Vector3(4000,0,4300),Vector3.forward,30);created.Add(branch.gameObject);
        var alternative=rail("[track diverging]",new Vector3(4000,0,4300),new Vector3(.1f,0,1).normalized,30);created.Add(alternative.gameObject);
        var terminal=rail("[Y]_[SM]_[A-03-S]",new Vector3(4000,0,4630),Vector3.back,300);created.Add(terminal.gameObject);
        var junctionType=branch.GetType().GetField("inJunction").FieldType;
        var parent=new GameObject("junc-left SM sector A fixture");parent.SetActive(false);created.Add(parent);
        var junctionGo=new GameObject("in_junction");junctionGo.transform.SetParent(parent.transform,false);var junction=junctionGo.AddComponent(junctionType);
        var branchType=junctionType.GetNestedType("Branch",All);
        Func<Component,bool,object> link=(track,first)=>{var value=Create(branchType);branchType.GetField("track").SetValue(value,track);branchType.GetField("first").SetValue(value,first);return value;};
        var branches=(IList)Activator.CreateInstance(junctionType.GetField("outBranches").FieldType);branches.Add(link(branch,true));branches.Add(link(alternative,true));
        junctionType.GetField("outBranches").SetValue(junction,branches);junctionType.GetField("inBranch").SetValue(junction,link(incoming,false));
        junctionType.GetField("selectedBranch").SetValue(junction,(byte)0);
        foreach(var t in new[]{branch,alternative}){t.GetType().GetField("inJunction").SetValue(t,junction);t.GetType().GetField("isJunctionTrack").SetValue(t,true);}
        branch.GetType().GetField("outBranch").SetValue(branch,link(terminal,false));terminal.GetType().GetField("outBranch").SetValue(terminal,link(branch,false));
        var extensions=sdk.GetType("Signals.Game.Extensions",true);var getLength=extensions.GetMethod("GetLength",All);
        var harmonyMethod=harmony.GetType().Assembly.GetType("HarmonyLib.HarmonyMethod",true);var patch=harmony.GetType().GetMethods().Single(m=>m.Name=="Patch"&&m.GetParameters().Length>=4);
        var prefix=typeof(UnityRewriteCheck).GetMethod("TerminalFixtureLength",All);var args=new object[patch.GetParameters().Length];args[0]=getLength;args[1]=Create(harmonyMethod,prefix);patch.Invoke(harmony,args);
        var reserve=sdk.GetType("Signals.Game.Railway.TrackReserver",true);var settings=sdk.GetType("Signals.Game.SignalsMod",true).GetProperty("Settings",All).GetValue(null,null);
        var special=settings.GetType().GetField("SpecialReservation");bool saved=(bool)special.GetValue(settings);
        var manual=assembly.GetType("RuSignals.NativeSdk",true).GetProperty("ManualOccupancy",All).GetValue(null,null);
        var owners=new System.Collections.Generic.List<object>();
        var waiting=FixtureBogie(incoming,out var approachTraveller,out var approachBogies);
        try
        {
            special.SetValue(settings,false);
            var block=blockType.GetMethod("CreateUntilMainSignal").Invoke(null,new[]{(object)branch,Enum.Parse(direction,"Out"),null});
            Check((bool)blockType.GetProperty("IsDeadEnd").GetValue(block,null),"SM A orientation: actual SDK walker reaches the buffer stop");
            Check(blockType.GetProperty("NextController").GetValue(block,null)==null,"Terminal reception requires no downstream train signal");
            var tracks=((Array)blockType.GetProperty("Tracks").GetValue(block,null)).Cast<object>().ToArray();
            Check(tracks.Length==2&&ReferenceEquals(Field(tracks[1],"Track"),terminal)&&Field(tracks[1],"Direction").ToString()=="In","SM A native traversal flips into the reception track toward its dead end");
            foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(block,key=="_station"?"SM":key=="_yard"?"A":key=="_trackType"?"S":"3");
            foreach(string slot in new[]{"EntrySignal","OldEntrySignal"})
            {
                var clone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);
                var owner=Create(basic,clone,null);owners.Add(owner);basic.GetField("Type").SetValue(owner,Enum.Parse(basic.GetField("Type").FieldType,"Entry"));
                var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];
                basic.GetProperty("PlacementInfo").SetValue(owner,Create(placement,incoming,Enum.Parse(direction,"In"),600,300d,false),null);
                var signal=((Array)basic.GetProperty("Signals").GetValue(owner,null)).GetValue(0);Call(signal,"SetBlock",block);
                Func<bool> limited=()=>((IEnumerable)assembly.GetType("RuSignals.SignalSpeedLimits",true).GetMethod("OnTrack").Invoke(null,new object[]{terminal})).Cast<object>().Any(s=>(int)Field(s,"Limit")==40&&(int)Field(s,"Direction")==-1);
                Func<string> indication=()=>Field(Call(signal.GetType().GetProperty("CurrentAspect").GetValue(signal,null),"GetDefinition"),"Indication").ToString();
                Action<string,string> expect=(value,label)=>{Call(signal,"UpdateAspect",true);Check(indication()==value,slot+" / "+label);};
                branch.name="[track through]";expect("DoubleYellow","clear straight terminal reception opens with two steady yellows");
                Check(limited(),slot+" terminal reception exports a direction-specific 40 limit to cab adapters");
                branch.name="[track diverging]";expect("DoubleYellow","clear diverging terminal reception opens restrictively");
                foreach(double position in new[]{50d,280,299.9})
                {
                    Call(approachTraveller,"MoveToSpan",position);
                    expect("DoubleYellow","waiting locomotive before entry does not occupy straight or side terminal route / "+position);
                }
                var bounded=signal.GetType().GetProperty("Block").GetValue(signal,null);
                Check(!((Array)blockType.GetProperty("ExtraTracks").GetValue(bounded,null)).Cast<object>().Contains(incoming),slot+" approach is absent from the native turnout extra tracks");
                branch.name="[track through]";Call(manual,"Add",terminal);expect("Stop","occupied reception track stops entry");
                Check(limited(),slot+" terminal 40 limit survives entry closing behind the train even without a diverging turnout");Call(manual,"Remove",terminal);
                Call(manual,"Add",alternative);expect("Stop","fouled unselected turnout branch stops entry");Call(manual,"Remove",alternative);
                junctionType.GetField("selectedBranch").SetValue(junction,(byte)1);expect("Stop","unaligned turnout stops entry");junctionType.GetField("selectedBranch").SetValue(junction,(byte)0);
                special.SetValue(settings,true);expect("DoubleYellow","terminal entry opens without its own reservation under the native reservation option");
                branch.name="[track diverging]";expect("DoubleYellow","diverging terminal entry opens without reservation");branch.name="[track through]";
                Check(!(bool)reserve.GetMethod("HasReservation",new[]{signal.GetType()}).Invoke(null,new[]{signal}),slot+" open terminal entry does not acquire a reservation implicitly");
                Check((bool)reserve.GetMethod("ReserveForSignal",new[]{signal.GetType()}).Invoke(null,new[]{signal}),slot+" native reservation accepts the terminal route");
                expect("DoubleYellow","own native reservation remains compatible with terminal entry");reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{signal});
                var otherClone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));NormalizePlacedFixture(otherClone);otherClone.gameObject.SetActive(true);
                var other=Create(basic,otherClone,null);owners.Add(other);var otherSignal=((Array)basic.GetProperty("Signals").GetValue(other,null)).GetValue(0);Call(otherSignal,"SetBlock",block);
                Check((bool)reserve.GetMethod("ReserveForSignal",new[]{otherSignal.GetType()}).Invoke(null,new[]{otherSignal}),slot+" conflicting native holder reserves the same receiving track");
                expect("Stop","another route's reservation stops terminal entry");reserve.GetMethod("ClearFromSignal").Invoke(null,new[]{otherSignal});
                blockType.GetProperty("IsSelfLoop").SetValue(bounded,true,null);expect("Stop","self loop cannot masquerade as a terminal route");blockType.GetProperty("IsSelfLoop").SetValue(bounded,false,null);
                expect("DoubleYellow","cleared route restores terminal reception");
                var legClone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));NormalizePlacedFixture(legClone);legClone.gameObject.SetActive(true);
                var legOwner=Create(basic,legClone,Create(placement,branch,Enum.Parse(direction,"In"),20,10d,false));owners.Add(legOwner);
                var legSignal=((Array)basic.GetProperty("Signals").GetValue(legOwner,null)).GetValue(0);Call(legSignal,"SetBlock",block);Call(legSignal,"UpdateAspect",true);
                var legAspect=legSignal.GetType().GetProperty("CurrentAspect").GetValue(legSignal,null);
                Check(Field(Call(legAspect,"GetDefinition"),"Indication").ToString()=="DoubleYellow",slot+" mast on outgoing turnout leg ignores the waiting locomotive behind it");
            }
            Debug.Log("RUS_TERMINAL_ARRIVAL_PASS native SM A topology / OUT-to-IN traversal / electric and mechanical entries / occupancy, fouling, points, reservation and loop guards");
        }
        finally
        {
            special.SetValue(settings,saved);Call(manual,"Remove",terminal);Call(manual,"Remove",alternative);
            Call(approachBogies,"Remove",waiting);UnityEngine.Object.DestroyImmediate(waiting.gameObject);
            foreach(var owner in owners)
            {
                var definition=(Component)basic.GetProperty("Definition").GetValue(owner,null);Call(owner,"Destroy");
                // Destroy while fixture-only listener patches still exist,
                // before native cleanup can attempt scene-service creation.
                if(definition!=null)UnityEngine.Object.DestroyImmediate(definition.gameObject);
            }
            var unpatch=harmony.GetType().GetMethods().Single(m=>m.Name=="Unpatch"&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(MethodInfo));unpatch.Invoke(harmony,new object[]{getLength,prefix});
            foreach(var go in created)UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
