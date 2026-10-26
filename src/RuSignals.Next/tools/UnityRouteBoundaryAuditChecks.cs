using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static Component FixtureBogie(Component track,out object traveller,out object bogies)
    {
        var game=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Assembly-CSharp");var go=new GameObject("Boundary audit bogie");go.SetActive(false);
        var bogie=go.AddComponent(game.GetType("Bogie",true));var field=bogie.GetType().GetField("traveller",All);
        traveller=Create(field.FieldType,Call(track,"GetKinkedPointSet"),false);field.SetValue(bogie,traveller);
        bogies=game.GetType("RailTrackOnTrackBogiesExtensions",true).GetMethod("BogiesOnTrack").Invoke(null,new object[]{track});Call(bogies,"Add",bogie);return bogie;
    }
    private static void RouteBoundaryAuditChecks(Assembly sdk,Assembly assembly,object pack,Func<string,Vector3,Vector3,int,Component> rail)
    {
        int start=passed;var ab=assembly.GetType("RuSignals.AutomaticBlock",true);var networkField=ab.GetField("Network",All);var saved=networkField.GetValue(null);var network=Create(saved.GetType());networkField.SetValue(null,network);
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var info=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var direction=info.GetField("Direction").FieldType;
        var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);var mode=blockType.GetMethod("IsOccupied").GetParameters()[0].ParameterType;
        var track=rail("Audit entry interval",new Vector3(7300,0,7300),Vector3.forward,500);var other=rail("Audit crossing leg",new Vector3(7300,0,7300),Vector3.right,100);
        var checker=sdk.GetType("Signals.Game.Railway.TrackChecker",true);var pointsType=checker.GetNestedType("TrackIntersectionPoints",All);
        var map=Activator.CreateInstance(network.GetType().GetField("Crossings",All).FieldType);var source=Create(pointsType,track);var points=(IList)pointsType.GetProperty("IntersectionPoints").GetValue(source,null);
        var datum=new GameObject("Audit crossing point");var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.name="Audit crossing obstruction";obstacle.layer=10;
        Call(map,"Add",track,source);network.GetType().GetField("Crossings",All).SetValue(network,map);
        var bogie=FixtureBogie(track,out var traveller,out var bogies);var owners=new System.Collections.Generic.List<object>();
        try
        {
            foreach(string slot in new[]{"Signal","EntrySignal","ExitSignal","DivergingSignal"})foreach(int sign in new[]{1,-1})
            {
                var travel=Enum.Parse(direction,sign==1?"Out":"In");var face=Enum.Parse(direction,sign==1?"In":"Out");double own=sign==1?100:400;
                var clone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);
                var owner=Create(basic,clone,Create(placement,track,face,(int)(own*2),own,false));owners.Add(owner);var signal=((Array)basic.GetProperty("Signals").GetValue(owner,null)).GetValue(0);
                var infos=Array.CreateInstance(info,1);infos.SetValue(Create(info,track,travel),0);var native=Create(blockType,null,500f);blockType.GetProperty("Tracks").SetValue(native,infos,null);
                Call(signal,"SetBlock",native);var block=signal.GetType().GetProperty("Block").GetValue(signal,null);var ignore=Enum.Parse(mode,"Ignore");
                foreach(double distance in new[]{20d,4,3,1,.01,0,-.01,-1,-20})
                {
                    Call(traveller,"MoveToSpan",own-distance*sign);
                    Check((bool)blockType.GetMethod("IsOccupied").Invoke(block,new[]{ignore})==(distance<=0),slot+" actual bogie / sign "+sign+" / distance before mast "+distance);
                }
                var relative=traveller.GetType().GetField("pointRelativeSpan");relative.SetValue(traveller,double.NaN);
                Check((bool)blockType.GetMethod("IsOccupied").Invoke(block,new[]{ignore}),slot+" invalid actual bogie coordinate fails closed");relative.SetValue(traveller,0d);
                Call(bogies,"Remove",bogie);
                foreach(bool ahead in new[]{false,true})
                {
                    points.Clear();double position=own+(ahead?30:-30)*sign;datum.transform.position=new Vector3(7300,0,7300+(float)position);
                    points.Add(Create(points.GetType().GetGenericArguments()[0],other,datum.transform));obstacle.transform.position=datum.transform.position+Vector3.up;Physics.SyncTransforms();
                    // New map instance is the same invalidation boundary used by
                    // TrackChecker.OnMapBuilt in production.
                    var rebuilt=Activator.CreateInstance(map.GetType());Call(rebuilt,"Add",track,source);network.GetType().GetField("Crossings",All).SetValue(network,rebuilt);
                    Check((bool)blockType.GetMethod("IsOccupied").Invoke(block,new[]{Enum.Parse(mode,"IntersectionOnly")})==ahead,slot+" actual physics crossing bounded by mast / "+sign+" / "+ahead);
                }
                obstacle.SetActive(false);points.Clear();Call(bogies,"Add",bogie);obstacle.SetActive(true);
            }
            foreach(int sign in new[]{1,-1})
            {
                var spanType=assembly.GetType("RuSignals.RailSpan",true);var corridorType=assembly.GetType("RuSignals.Corridor",true);var outDir=Enum.Parse(direction,"Out");
                var spans=Array.CreateInstance(spanType,1);spans.SetValue(Create(spanType,track,outDir,0d,500d),0);var corridor=Create(corridorType,spans);
                var paths=Field(network,"Corridors");Call(paths,"Clear");Call(paths,"Add",corridor);var tracks=Field(network,"Tracks");Call(tracks,"Clear");
                Call(tracks,"Add",track,Create(tracks.GetType().GetGenericArguments()[1],corridor,outDir));
                network.GetType().GetField("Ready",All).SetValue(network,true);network.GetType().GetField("polledFrame",All).SetValue(network,-1);
                var face=Enum.Parse(direction,sign==1?"In":"Out");var travel=Enum.Parse(direction,sign==1?"Out":"In");double own=sign==1?100:400;
                var clone=UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);
                var owner=Create(basic,clone,Create(placement,track,face,(int)(own*2),own,false));owners.Add(owner);var head=((Array)basic.GetProperty("Signals").GetValue(owner,null)).GetValue(0);
                var infos=Array.CreateInstance(info,1);infos.SetValue(Create(info,track,travel),0);var native=Create(blockType,null,500f);blockType.GetProperty("Tracks").SetValue(native,infos,null);
                foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(native,"");
                Call(head,"SetBlock",native);Call(traveller,"MoveToSpan",own-50*sign);
                Check((bool)ab.GetMethod("DirectionAllowed",All).Invoke(null,new[]{head}),"Actual stationary bogie loaded behind entry does not lock its forward corridor / "+sign);
                Call(head,"UpdateAspect",true);var aspect=head.GetType().GetProperty("CurrentAspect").GetValue(head,null);
                Check(Field(Call(aspect,"GetDefinition"),"Indication").ToString()=="Yellow","Actual entry opens before an initially stationary approach train / "+sign);
                var reverseClone=UnityEngine.Object.Instantiate((Component)Field(pack,"EntrySignal"));NormalizePlacedFixture(reverseClone);reverseClone.gameObject.SetActive(true);
                double reverseSpan=sign==1?400:100;var reverseOwner=Create(basic,reverseClone,Create(placement,track,travel,(int)(reverseSpan*2),reverseSpan,false));owners.Add(reverseOwner);
                var reverseHead=((Array)basic.GetProperty("Signals").GetValue(reverseOwner,null)).GetValue(0);var reverseInfos=Array.CreateInstance(info,1);reverseInfos.SetValue(Create(info,track,face),0);
                var reverseBlock=Create(blockType,null,500f);blockType.GetProperty("Tracks").SetValue(reverseBlock,reverseInfos,null);Call(reverseHead,"SetBlock",reverseBlock);
                foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(reverseBlock,"");
                Check(!(bool)ab.GetMethod("DirectionAllowed",All).Invoke(null,new[]{reverseHead}),"Actual unknown stationary train still blocks the opposing approach / "+sign);
                Call(reverseHead,"UpdateAspect",true);var reverseAspect=reverseHead.GetType().GetProperty("CurrentAspect").GetValue(reverseHead,null);
                Check(Field(Call(reverseAspect,"GetDefinition"),"Indication").ToString()=="Stop","Actual opposing entry remains red / "+sign);
                Call(traveller,"MoveToSpan",own+20*sign);Call(head,"UpdateAspect",true);aspect=head.GetType().GetProperty("CurrentAspect").GetValue(head,null);
                Check(Field(Call(aspect,"GetDefinition"),"Indication").ToString()=="Stop","Actual entry closes after the bogie crosses its mast / "+sign);
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"route-boundary-audit-verification.json"),"{\"checks\":"+(passed-start)+",\"actual_bogies\":true,\"actual_crossing_colliders\":true,\"both_directions\":true,\"train_profiles\":4,\"scope\":\"Actual native occupation and physics on synthetic rails; supplied saved game not tested\"}");
            Debug.Log("RUS_ROUTE_BOUNDARY_AUDIT_PASS "+(passed-start)+" / actual bogies and crossing colliders / four train profiles / both directions");
        }
        finally
        {
            Call(bogies,"Remove",bogie);UnityEngine.Object.DestroyImmediate(bogie.gameObject);UnityEngine.Object.DestroyImmediate(obstacle);UnityEngine.Object.DestroyImmediate(datum);
            foreach(var c in owners){var def=(Component)basic.GetProperty("Definition").GetValue(c,null);Call(c,"Destroy");UnityEngine.Object.DestroyImmediate(def.gameObject);}
            networkField.SetValue(null,saved);UnityEngine.Object.DestroyImmediate(track.gameObject);UnityEngine.Object.DestroyImmediate(other.gameObject);
        }
    }
}
