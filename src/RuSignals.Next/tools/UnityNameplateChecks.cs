using System;
using System.IO;
using System.Linq;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void TurntableNameChecks(System.Reflection.Assembly assembly,System.Reflection.Assembly common,System.Reflection.Assembly sdk,object pack,System.Collections.IList approaches)
    {
        var manager=UnityEngine.Object.FindObjectOfType(sdk.GetType("Signals.Game.SignalManager",true));
        var registry=(System.Collections.IList)manager.GetType().GetProperty("AllControllers").GetValue(manager,null);var saved=registry.Cast<object>().ToArray();
        registry.Clear();foreach(var c in approaches)registry.Add(c);
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var fixtures=new System.Collections.Generic.List<object>();
        Func<object,object> signal=c=>((Array)c.GetType().GetProperty("Signals").GetValue(c,null)).GetValue(0);
        Func<object,Component> definition=c=>(Component)c.GetType().GetProperty("Definition").GetValue(c,null);
        var origin=definition(approaches[0]).transform.position;var tags=approaches.Cast<object>().Select(c=>(string)signal(c).GetType().GetProperty("Name").GetValue(signal(c),null)).ToArray();
        Func<string,string,float,object> nearby=(name,station,distance)=>
        {
            var clone=UnityEngine.Object.Instantiate((Component)Field(pack,"Signal"));
            foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true))foreach(var reference in component.GetType().GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))
                if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
            foreach(var def in clone.GetComponentsInChildren(common.GetType("Signals.Common.SignalDefinition",true),true))def.GetType().GetField("PhysicalReserver",All).SetValue(def,null);
            clone.transform.SetParent(null);clone.transform.position=origin+Vector3.right*distance;clone.gameObject.SetActive(true);var controller=Create(basic,clone,null);fixtures.Add(controller);
            signal(controller).GetType().GetField("NameOverride",All).SetValue(signal(controller),name);
            if(station!=null){var group=Create(sdk.GetType("Signals.Game.JunctionSignalGroup",true),new object[]{null});group.GetType().GetField("_stationId",All).SetValue(group,station);basic.GetProperty("Group").SetValue(controller,group,null);}
            return controller;
        };
        var names=assembly.GetType("RuSignals.SignalDesignations",true);Action refresh=()=>names.GetMethod("Refresh",All).Invoke(null,null);
        Func<object,string> label=c=>(string)names.GetMethod("Get",All).Invoke(null,new[]{signal(c)});
        try
        {
            var close=nearby("S715","QA",20);var farther=nearby("S800","QA",40);nearby("S999","MF",.1f);nearby("S716",null,.05f);nearby("T715","QA",100);
            refresh();Check(approaches.Cast<object>().Select(label).SequenceEqual(new[]{"T715A","T715B"}),"Table names use nearest signal in their own station, avoid a nearer foreign/unknown station and avoid occupied captions");
            Check(label(close)=="S715"&&label(farther)=="S800","Neighbouring original signal names are never changed");
            Check(approaches.Cast<object>().All(c=>((System.Collections.IEnumerable)Call(signal(c),"GetAllHudElements")).Cast<object>().Any(e=>e.GetType().Name=="NameplateDisplay"&&(string)e.GetType().GetProperty("DisplayText").GetValue(e,null)==label(c))),"Turntable Alt name card matches its nearby-based physical short name");
            var firstSignal=signal(approaches[0]);var plate=definition(approaches[0]).GetComponentInChildren(assembly.GetType("RuSignals.NameplateView",true),true);
            Check((string)plate.GetType().GetProperty("DisplayedName").GetValue(plate,null)=="T715A","Short table name reaches the physical plate");
            var focus=((MeshFilter)Field(plate,"Board")).GetComponent<Renderer>().bounds.center;
            Render(plate.gameObject,Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"short-table-"+plate.GetType().GetField("Vertical").GetValue(plate)+".png"),focus+plate.transform.forward,focus,Mathf.Max(.35f,((MeshFilter)Field(plate,"Board")).GetComponent<Renderer>().bounds.extents.y+.08f));
            definition(farther).transform.position=origin+Vector3.right;refresh();
            Check(approaches.Cast<object>().Select(label).SequenceEqual(new[]{"T800","T715A"}),"Each approach follows its own nearest numbered neighbour after a refresh");
            var labels=approaches.Cast<object>().Select(label).ToArray();registry.Clear();foreach(var c in approaches.Cast<object>().Reverse().Concat(fixtures.AsEnumerable().Reverse()))registry.Add(c);refresh();
            Check(approaches.Cast<object>().Select(label).SequenceEqual(labels),"Table abbreviation and distinct suffixes do not depend on registry order");
            firstSignal.GetType().GetField("NameOverride",All).SetValue(firstSignal,"Custom9");refresh();Check(label(approaches[0])=="Custom9","An explicit non-service table name remains original");firstSignal.GetType().GetField("NameOverride",All).SetValue(firstSignal,tags[0]);
            foreach(var c in fixtures){Call(c,"Destroy");UnityEngine.Object.DestroyImmediate(definition(c).gameObject);}fixtures.Clear();refresh();
            Check(approaches.Cast<object>().Select(label).SequenceEqual(new[]{"T1-1","T1-2"}),"Without a local numbered neighbour, table and approach numbers provide a compact fallback");
            Check(approaches.Cast<object>().Select((c,i)=>(string)signal(c).GetType().GetProperty("Name").GetValue(signal(c),null)==tags[i]).All(v=>v),"Table abbreviation never changes native names or save/phone keys");
            Debug.Log("RUS_TABLE_NAMES_PASS nearest same-station number / suffix collisions / stable order / custom name / fallback / native keys retained");
        }
        finally
        {
            foreach(var c in fixtures){Call(c,"Destroy");UnityEngine.Object.DestroyImmediate(definition(c).gameObject);}
            registry.Clear();foreach(var c in saved)registry.Add(c);refresh();
        }
    }
    private static void NameplateLayoutChecks(Component plate,string slot)
    {
        var letters=(TextMesh)Field(plate,"Letters");var board=(MeshFilter)Field(plate,"Board");bool vertical=(bool)Field(plate,"Vertical");
        string original=(string)plate.GetType().GetProperty("DisplayedName").GetValue(plate,null);
        foreach(string name in new[]{"PChM18","S715","T_MF_1-8","S_HB_123456","I1","W8","g_j"})
        {
            Call(plate,"Show",name);var lines=letters.text.Split('\n');var scale=letters.transform.localScale;
            Check(string.Concat(lines)==name,slot+" complete native designation survives line wrapping: "+name);
            Check(scale.x>0&&scale.x==scale.y&&scale.y==scale.z,slot+" lettering retains its original proportions: "+name);
            if(vertical)
            {
                float step=(float)Field(plate,"LineStep")*letters.lineSpacing*scale.y;
                Check(Mathf.Abs(step-.28f)<.000001f,slot+" rows stay at their exact plate pitch after scaling: "+name);
                Check(lines.Length<=6&&Mathf.Abs(board.sharedMesh.bounds.size.y-(lines.Length*.28f-.01f))<.00001f,slot+" line count matches the physical plates: "+name);
            }
            else Check(lines.Length==1,slot+" dwarf retains a horizontal designation: "+name);
            var centre=plate.transform.InverseTransformPoint(letters.GetComponent<Renderer>().bounds.center);
            float expected=-.135f-(lines.Length-1)*.14f;
            Check(Mathf.Abs(centre.y-expected)<.00001f&&Mathf.Abs(centre.x)<.00001f,slot+" native font layout is centred on its plate cells: "+name);
            if(slot=="Signal"&&(name=="PChM18"||name=="T_MF_1-8"))
            {
                var focus=board.GetComponent<Renderer>().bounds.center;
                Render(plate.gameObject,Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"centred-nameplate-"+name.Replace('_','-')+".png"),focus+plate.transform.forward,focus,board.GetComponent<Renderer>().bounds.extents.y+.08f);
            }
        }
        Call(plate,"Show",original);Debug.Log("RUS_NAMEPLATE_LAYOUT_PASS "+slot+" / native names / fixed 0.28 m pitch / exact font cell centres / seven labels");
    }
}
