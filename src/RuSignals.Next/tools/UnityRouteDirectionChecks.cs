using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void RouteDirectionNativeChecks(Assembly sdk,Assembly assembly,Func<string,Vector3,Vector3,int,Component> rail)
    {
        int start=passed;
        var through=rail("[track through]",new Vector3(7000,0,7000),Vector3.forward,30);
        var selected=rail("[track diverging]",new Vector3(7000,0,7000),Vector3.forward,30);
        var parent=new GameObject("Curved mainline route direction fixture");parent.SetActive(false);
        var junctionType=selected.GetType().GetField("inJunction").FieldType;var junction=parent.AddComponent(junctionType);
        var branchType=junctionType.GetNestedType("Branch",All);var branches=(IList)Field(junction,"outBranches");
        foreach(var track in new[]{selected,through})
        {
            var branch=Create(branchType);branchType.GetField("track").SetValue(branch,track);branchType.GetField("first").SetValue(branch,true);branches.Add(branch);
            track.GetType().GetField("inJunction").SetValue(track,junction);
        }
        var info=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var direction=info.GetField("Direction").FieldType;
        var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);
        var ctor=blockType.GetConstructors(All).Single(c=>c.GetParameters().Length==2&&c.GetParameters()[0].ParameterType.IsGenericType);
        Func<Component,string,object> block=(track,d)=>
        {var tracks=Array.CreateInstance(info,1);tracks.SetValue(Create(info,track,Enum.Parse(direction,d)),0);return ctor.Invoke(new object[]{tracks,null});};
        var method=assembly.GetType("RuSignals.RouteDirection",true).GetMethod("ForRoute",All);
        Func<object,string> arrow=b=>(string)method.Invoke(null,new[]{b,junction});
        Action<Component,bool,Vector3> tangent=(track,last,value)=>
        {var set=Call(track,"GetKinkedPointSet");var points=(Array)Field(set,"points");int at=last?points.Length-1:0;var p=points.GetValue(at);p.GetType().GetField("forward").SetValue(p,value);points.SetValue(p,at);};
        try
        {
            tangent(through,true,new Vector3(.5f,0,1));tangent(selected,true,new Vector3(.2f,0,1));
            Check(arrow(block(selected,"Out"))=="\\","Native curved mainline: relative left branch is not mistaken for right");
            Check(arrow(block(selected,"In"))=="/","Native reverse traversal flips relative direction for the driver");
            through.GetType().GetField("inJunction").SetValue(through,null);tangent(through,false,new Vector3(-.5f,0,-1));
            Check(arrow(block(selected,"Out"))=="\\","Native opposite point order on through rail retains left indication");
            selected.GetType().GetField("inJunction").SetValue(selected,null);tangent(selected,false,new Vector3(-.2f,0,-1));
            Check(arrow(block(selected,"In"))=="\\","Native reversed selected rail uses junction endpoint rather than point index");
            junctionType.GetField("selectedBranch").SetValue(junction,(byte)1);
            Check(arrow(block(through,"In"))=="|","Native through route stays vertical even on a curve");
            Check(arrow(block(selected,"In"))=="","Native stale selected branch cannot light an unrelated route");
            junctionType.GetField("selectedBranch").SetValue(junction,(byte)0);var dirty=block(selected,"In");Call(dirty,"FlagAsDirty");
            Check(arrow(dirty)=="","Native dirty route keeps direction dark");
            through.name="[unmarked track]";selected.GetType().GetField("inJunction").SetValue(selected,junction);tangent(selected,false,Vector3.forward);
            Check(arrow(block(selected,"Out"))=="/","Native unknown reference retains geometric fallback");
        }
        finally{UnityEngine.Object.DestroyImmediate(through.gameObject);UnityEngine.Object.DestroyImmediate(selected.gameObject);UnityEngine.Object.DestroyImmediate(parent);}
        System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"route-direction-verification.json"),"{\"checks\":"+(passed-start)+",\"curved_mainline\":true,\"reverse_travel\":true,\"reversed_track_point_order\":true,\"stale_route_rejected\":true,\"scope\":\"Actual SDK block and track point sets in isolated Unity; synthetic curved turnout, not the saved map at signal 973\"}");
        Debug.Log("RUS_ROUTE_DIRECTION_PASS "+(passed-start)+" native route cases");
    }
}
