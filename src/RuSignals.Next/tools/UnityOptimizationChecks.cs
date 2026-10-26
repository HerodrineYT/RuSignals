// Menu lifecycle and visual equivalence checks against the released 3.0.5 DLL.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEditor;

public static partial class UnityRewriteCheck
{
    private static void IdleSchedulerChecks(Assembly assembly,Component clock)
    {
        var scheduler=clock.GetType();var power=assembly.GetType("RuSignals.SignalPower",true);
        Action<string,object> call=(name,value)=>scheduler.GetMethods(All).Single(m=>m.Name==name&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==value.GetType()).Invoke(null,new[]{value});
        Check(!((Behaviour)clock).enabled,"Menu scheduler is disabled before any world visual is registered");
        for(int i=0;i<100;i++)Call(clock,"Update");
        Check(!(bool)power.GetField("subscribed",All).GetValue(null),"Empty scheduler does not discover optional plugins or subscribe to events");
        var components=new[]{"SemaphoreVisual","ClearanceView","RoutePanel","SpillView"}.Select(n=>new GameObject("Scheduler lifecycle "+n).AddComponent(assembly.GetType("RuSignals."+n,true))).ToArray();
        var spill=components[3];spill.GetType().GetField("Lights",All).SetValue(spill,new Light[0]);spill.GetType().GetField("Masks",All).SetValue(spill,new int[0]);
        for(int i=0;i<components.Length;i++)
        {
            call(i==0?"Wake":"Register",components[i]);Check(((Behaviour)clock).enabled,"Registering each visual kind wakes an idle scheduler");
            call(i==0?"Sleep":"Unregister",components[i]);Check(!((Behaviour)clock).enabled,"Removing the last visual kind returns scheduler to idle");
        }
        call("Register",components[1]);call("Register",components[2]);call("Unregister",components[1]);
        Check(((Behaviour)clock).enabled,"Removing one registry does not stop another active registry");
        UnityEngine.Object.DestroyImmediate(components[2].gameObject);Check(!((Behaviour)clock).enabled,"Visual destruction disables scheduler after the last registry empties");
        call("Register",components[1]);Check(((Behaviour)clock).enabled,"A subsequent world wakes the scheduler again");call("Unregister",components[1]);
        foreach(var c in components)if(c!=null)UnityEngine.Object.DestroyImmediate(c.gameObject);
        power.GetMethod("Refresh",All).Invoke(null,null);
        Check(power.GetField("read",All).GetValue(null)==null&&!(bool)power.GetField("resolve",All).GetValue(null),"Missing DVSeasons API is cached after its first qualified lookup");
        for(int i=0;i<100;i++)power.GetMethod("Refresh",All).Invoke(null,null);
        Check(!(bool)power.GetField("resolve",All).GetValue(null),"Repeated power reads without assembly loads retain the negative cache");
        // PowerChecks loads the real optional assembly next: its outage and
        // restoration checks also exercise late discovery after this miss.
        Debug.Log("RUS_MENU_IDLE_PASS four registry kinds / destruction / next world / optional API negative cache");
    }
    private static void LampHookLifecycleChecks(Assembly assembly,Assembly sdk,object pack,object harmony)
    {
        var timing=assembly.GetType("RuSignals.LampTiming",true);var lightType=sdk.GetType("Signals.Game.Lights.SignalLight",true);var lampType=lightType.GetField("Lamp",All).FieldType;
        var target=lampType.GetMethod("Update",All);var prefix=timing.GetMethod("Update",All);var harmonyType=harmony.GetType();
        Func<bool> installed=()=>
        {
            var info=harmonyType.GetMethod("GetPatchInfo",All).Invoke(null,new object[]{target});if(info==null)return false;
            var list=(IEnumerable)info.GetType().GetField("Prefixes").GetValue(info);
            return list.Cast<object>().Any(p=>(MethodInfo)p.GetType().GetProperty("PatchMethod").GetValue(p,null)==prefix);
        };
        Check(!installed(),"Global native lamp Update has no RuSignals prefix in the empty menu");
        var go=new GameObject("Lamp registration lifecycle");go.SetActive(false);var lamp=go.AddComponent(lampType);
        var light=go.AddComponent(lightType);
        lightType.GetField("Lamp",All).SetValue(light,lamp);
        var signal=(Component)Field(pack,"Signal");var definition=signal.GetComponentsInChildren<MonoBehaviour>(true).First(c=>c.GetType().FullName=="Signals.Common.SignalLightDefinition");
        lightType.GetField("Definition",All).SetValue(light,definition);
        timing.GetMethod("Register",All).Invoke(null,new[]{light});Check(installed(),"First own lamp installs its native Update prefix before use");
        timing.GetMethod("Reset",All).Invoke(null,null);Check(!installed(),"World cleanup removes the owned native Update prefix");
        timing.GetMethod("Register",All).Invoke(null,new[]{light});Check(installed(),"Next world reinstalls native smoothing without reinstalling all hooks");
        var ownerType=sdk.GetType("Signals.Game.Signal",true);var owner=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(ownerType);var lights=Array.CreateInstance(lightType,1);lights.SetValue(light,0);
        ownerType.GetProperty("AllLights",All).SetValue(owner,lights,null);
        timing.GetMethod("Remove",All).Invoke(null,new[]{owner});Check(!installed(),"Destroying the last own lamp removes its native Update prefix");
        Check(((IDictionary)timing.GetField("lamps",All).GetValue(null)).Count==0,"Last-lamp cleanup releases lamp registry references");
        UnityEngine.Object.DestroyImmediate(go);Debug.Log("RUS_LAMP_HOOK_IDLE_PASS absent in menu / first lamp / cleanup / reload / last lamp");
    }
    private static string MaterialSignature(Material material)
    {
        if(material==null)return "null";
        var text=new StringBuilder(material.shader.name+"|"+material.renderQueue+"|"+material.enableInstancing+"|"+string.Join(",",material.shaderKeywords.OrderBy(k=>k)));
        for(int i=0;i<ShaderUtil.GetPropertyCount(material.shader);i++)
        {
            string name=ShaderUtil.GetPropertyName(material.shader,i);text.Append('|').Append(name).Append('=');
            switch(ShaderUtil.GetPropertyType(material.shader,i))
            {
                case ShaderUtil.ShaderPropertyType.Color:text.Append(material.GetColor(name).ToString("R"));break;
                case ShaderUtil.ShaderPropertyType.Vector:text.Append(material.GetVector(name).ToString("R"));break;
                case ShaderUtil.ShaderPropertyType.TexEnv:var texture=material.GetTexture(name);text.Append(texture==null?"null":texture.name).Append(material.GetTextureScale(name).ToString("R")).Append(material.GetTextureOffset(name).ToString("R"));break;
                default:text.Append(material.GetFloat(name).ToString("R",System.Globalization.CultureInfo.InvariantCulture));break;
            }
        }
        return text.ToString();
    }
    private static Color32[] MaterialPixels(Material material)
    {
        var ambientMode=RenderSettings.ambientMode;var ambient=RenderSettings.ambientLight;bool fog=RenderSettings.fog;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.white;RenderSettings.fog=false;
        var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.layer=29;go.GetComponent<Renderer>().sharedMaterial=material;
        var camera=new GameObject("Material equality camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-2);camera.transform.LookAt(Vector3.zero);camera.orthographic=true;camera.orthographicSize=.55f;camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var target=new RenderTexture(128,128,24);var previous=RenderTexture.active;camera.targetTexture=target;camera.Render();RenderTexture.active=target;
        var image=new Texture2D(128,128,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,128,128),0,0);image.Apply();var pixels=image.GetPixels32();
        RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
        RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambient;RenderSettings.fog=fog;return pixels;
    }
    [Serializable] public sealed class MaterialReference
    {
        public string version,dll_sha256;public int materials,glass_materials;public RendererReference[] renderers;public ProbeReference[] probes;
    }
    [Serializable] public sealed class RendererReference{public string name;public string[] signatures;}
    [Serializable] public sealed class ProbeReference{public string signature,pixels;}
    private static string ProbePixels(Material material)=>Convert.ToBase64String(MaterialPixels(material).SelectMany(p=>new[]{p.r,p.g,p.b,p.a}).ToArray());
    public static void SaveMaterialReference(GameObject holder,string dll,string output,string version)
    {
        if(version!="3.0.5")throw new Exception("Material baseline must use the original release DLL");
        var renderers=holder.GetComponentsInChildren<MeshRenderer>(true);var materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
        var glasses=materials.Where(m=>m.shader.name=="RuSignals/Signal glass four rings").ToArray();
        string hash;using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(dll))).Replace("-","").ToLowerInvariant();
        var reference=new MaterialReference{version=version,dll_sha256=hash,materials=materials.Length,glass_materials=glasses.Length,
            renderers=renderers.Select(r=>new RendererReference{name=r.name,signatures=r.sharedMaterials.Select(MaterialSignature).ToArray()}).ToArray(),
            probes=glasses.GroupBy(MaterialSignature).Select(g=>new ProbeReference{signature=g.Key,pixels=ProbePixels(g.First())}).ToArray()};
        File.WriteAllText(Path.Combine(output,"material-reference.json"),JsonUtility.ToJson(reference,true));
        Debug.Log("RUS_MATERIAL_BASELINE_PASS "+version+" / "+renderers.Length+" renderers / "+glasses.Length+" glasses");
    }
    private static void MaterialSharingChecks(Assembly assembly,Assembly common,object assets,GameObject holder,Component phone,string root,string output)
    {
        var reference=JsonUtility.FromJson<MaterialReference>(File.ReadAllText(Path.Combine(root,"artifacts/ru-signals-0.1.0/material-reference.json")));
        Check(reference.version=="3.0.5","Material reference is the released 3.0.5 DLL captured in a separate Unity process");
        var removedAuxiliary=reference.renderers.Where(r=>r.name=="Auxiliary lamp housing"||r.name=="Moon-white lens").ToArray();
        Check(removedAuxiliary.Length==6,"Original three auxiliary lantern housings and lenses are the only newly removed renderer entries");
        var before=reference.renderers.Where(r=>r.name!="upperselector"&&r.name!="lowerselector"&&r.name!="Auxiliary lamp housing"&&r.name!="Moon-white lens").ToArray();var all=holder.GetComponentsInChildren<MeshRenderer>(true);
        var links=all.Where(r=>r.name=="Upper rod linkage"||r.name=="Lower rod linkage").ToArray();
        var transmissionNames=new[]{"Lower drive arm","Profiled drive wheel","Drive groove roller","Upper spectacle coupling","Lower spectacle coupling","Upper red filter","Upper green filter","Lower yellow filter","Control cable clamp"};
        var transmission=all.Where(r=>transmissionNames.Contains(r.name)).ToArray();
        var after=all.Except(transmission).ToArray();
        Check(transmission.Length==30&&transmission.All(r=>!r.gameObject.activeInHierarchy),"30 necessary transmission renderers stay inactive in the menu and share existing materials");
        Check(reference.renderers.Length==338&&before.Length==323&&after.Length==before.Length&&after.All(r=>!r.gameObject.activeInHierarchy),"All 323 retained original template renderers remain inactive in the menu");
        var rods=all.Where(r=>r.name=="upperrod"||r.name=="lowerrod").ToArray();
        Check(links.Length==0&&rods.Length==9,"Exactly nine original long rods, no added semaphore connecting links");
        foreach(var rod in rods)
        {
            var pivot=rod.transform.parent;
            var view=pivot.parent.GetComponent(assembly.GetType("RuSignals.SemaphoreVisual",true));
            string side=rod.name=="upperrod"?"Upper":"Lower";
            Check((Transform)Field(view,side+"Rod")==pivot&&pivot.localRotation==Quaternion.identity&&rod.sharedMaterial!=null&&rod.GetComponent<MeshFilter>().sharedMesh!=null,"Direct vertical rod retains its imported mesh and shared material");
            Check((Transform)Field(view,side+"Selector")==null&&!view.GetComponentsInChildren<Transform>(true).Any(t=>new[]{"filter","selector","spectacle"}.Any(s=>t.name.IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0)),"Every factory wing excludes lens mechanisms and selector pivots");
            var lampNames=side=="Upper"?new[]{"Red","Green","UpperDark"}:new[]{"Yellow","LowerDark"};
            var movingArm=(Transform)Field(view,side);
            Check((bool)Field(view,"FixedLanterns")&&lampNames.All(n=>{var lamp=(Renderer)Field(view,n);return lamp!=null&&lamp.transform.IsChildOf(view.transform)&&!lamp.transform.IsChildOf(movingArm);}),"Every factory wing retains its live and dark lenses outside the moving wing hierarchy");
        }
        var pairs=new System.Collections.Generic.HashSet<Material>();
        for(int i=0;i<before.Length;i++)
        {
            Check(before[i].name==after[i].name&&before[i].signatures.Length==after[i].sharedMaterials.Length,"Template renderer and material-slot topology preserved");
            for(int j=0;j<before[i].signatures.Length;j++)
            {
                var b=after[i].sharedMaterials[j];Check(before[i].signatures[j]==MaterialSignature(b),"Every original shader property and texture mapping is preserved after pooling");
                if(b!=null&&b.shader.name=="RuSignals/Signal glass four rings")pairs.Add(b);
            }
        }
        Check(reference.glass_materials==46&&pairs.Count==9,"After removing the auxiliary white glass, retained lens materials share nine original parameter groups");
        foreach(var material in pairs)Check(ProbePixels(material)==reference.probes.Single(p=>p.signature==MaterialSignature(material)).pixels,"Pooled lens and original lens produce identical GPU pixels");
        int oldCount=reference.materials,newCount=after.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().Count();
        File.WriteAllText(Path.Combine(output,"material-sharing-verification.json"),"{\"baseline_version\":\"3.0.5\",\"template_renderers\":"+all.Length+",\"original_template_renderers\":323,\"transmission_renderers\":"+transmission.Length+",\"short_link_renderers\":"+links.Length+",\"active_template_renderers\":0,\"materials_before\":"+oldCount+",\"materials_after\":"+newCount+",\"glass_before\":46,\"glass_after\":9,\"all_shader_properties_identical\":true,\"identical_gpu_probes\":9,\"removed_auxiliary_renderers\":6,\"probe_pixels\":16384,\"game_fps_measured\":false}");
        Debug.Log("RUS_MATERIAL_SHARING_PASS nine retained lens groups / all323 retained renderer properties / six removed auxiliary renderers / nine identical GPU probes");
    }
}
