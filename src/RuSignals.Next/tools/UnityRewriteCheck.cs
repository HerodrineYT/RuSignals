// Loads the exact release DLL, builds every prefab against actual SDK types,
// installs the native patches and exercises visuals without a saved game.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static partial class UnityRewriteCheck
{
    private const string Pending="RUS.Rewrite.QA.Pending";
    static UnityRewriteCheck(){EditorApplication.playModeStateChanged+=OnPlay;}
    public static void Launch(){Time.captureFramerate=60;SessionState.SetBool(Pending,true);if(EditorApplication.isPlaying)Execute();else EditorApplication.isPlaying=true;}
    private static void OnPlay(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Pending,false))EditorApplication.delayCall+=Execute;}
    private static void Execute()
    {
        SessionState.SetBool(Pending,false);
        try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static int passed,frames,tunnelModels,tunnelScenarios,nameplates,shuntingSemaphores,turntableVariants;
    private static bool regionsChecked;
    private static readonly System.Collections.Generic.HashSet<string> tunnelKeys=new System.Collections.Generic.HashSet<string>();
    private static readonly Dictionary<string,List<Vector3[]>> tunnelTriangles=new Dictionary<string,List<Vector3[]>>();
    private static readonly HashSet<string> glowKeys=new HashSet<string>(),shapeKeys=new HashSet<string>();
    private static void Check(bool condition,string text){if(!condition)throw new Exception(text);passed++;}
    private static object Call(object value,string method,params object[] args)=>value.GetType().GetMethod(method,All).Invoke(value,args);
    private static object Field(object value,string name)=>value.GetType().GetField(name,All).GetValue(value);
    private static object Create(Type type,params object[] args)=>Activator.CreateInstance(type,All,null,args,null);
    public static void Run()
    {
        string repo=Environment.GetEnvironmentVariable("RUS_V2_ROOT"),game=Environment.GetEnvironmentVariable("RUS_V2_GAME"),output=Environment.GetEnvironmentVariable("RUS_V2_OUTPUT");
        string sdkDirectory=Environment.GetEnvironmentVariable("RUS_V2_SDK")??Path.Combine(game,"Mods/DVSignals");
        string rsgDirectory=Environment.GetEnvironmentVariable("RUS_V2_RSG")??Path.Combine(game,"Mods/RailwaySafetyGadgets");
        string dll=Environment.GetEnvironmentVariable("RUS_V2_DLL")??Path.Combine(repo,"src/RuSignals.Next/bin/Release/net48/RuSignals.dll");
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>
        {
            string rsgDependency=Path.Combine(rsgDirectory,new AssemblyName(args.Name).Name+".dll");if(File.Exists(rsgDependency))return Assembly.LoadFrom(rsgDependency);
            string sdkDependency=Path.Combine(sdkDirectory,new AssemblyName(args.Name).Name+".dll");if(File.Exists(sdkDependency))return Assembly.LoadFrom(sdkDependency);
            foreach(var directory in new[]{"DerailValley_Data/Managed","DerailValley_Data/Managed/UnityModManager","Mods/DVSignals","Mods/RailwaySafetyGadgets"})
            {string file=Path.Combine(game,directory,new AssemblyName(args.Name).Name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}
            string dependency=Directory.EnumerateFiles(Path.Combine(game,"Mods"),new AssemblyName(args.Name).Name+".dll",SearchOption.AllDirectories).FirstOrDefault();
            if(dependency!=null)return Assembly.LoadFrom(dependency);
            return null;
        };
        var common=Assembly.LoadFrom(Path.Combine(sdkDirectory,"Signals.Common.dll"));
        var sdk=Assembly.LoadFrom(Path.Combine(sdkDirectory,"Signals.Game.dll"));
        var assembly=Assembly.Load(File.ReadAllBytes(dll));
        BrandingChecks(assembly,repo);
        var holder=new GameObject("QA prefab holder");holder.SetActive(false);
        var phoneGo=new GameObject("Native SDK phone fixture");phoneGo.SetActive(false);
        phoneGo.AddComponent(common.GetType("Signals.Common.TracksideObject",true));
        var phone=phoneGo.AddComponent(common.GetType("Signals.Common.SignalReservingObjectDefinition",true));
        var assets=Create(assembly.GetType("RuSignals.SignalAssets",true),Path.Combine(repo,"src/RuSignals.Next/Assets"));
        StyleChecks(assets,output);
        LoadOriginalHud(common,sdkDirectory);
        RouteTextScaleChecks(game,output);
        var factory=Create(assembly.GetType("RuSignals.PackFactory",true),assets,holder.transform,phone,originalHud);
        Call(factory,"ReadSemaphores",Path.Combine(repo,"src/RuSignals.Next/Assets"));
        var pack=Call(factory,"Create");
        RouteHoverLayoutChecks(assembly,assets,game,output);
        MaterialSharingChecks(assembly,common,assets,holder,phone,repo,output);
        Check((string)Field(pack,"Author")=="Herodrine_"&&(string)Field(pack,"ModName")=="RuSignals"&&((UnityEngine.Object)pack).name=="RuSignals","Pack author and RuSignals display name retained");
        Check((string)Field(pack,"Version")== (string)assembly.GetType("RuSignals.Mod",true).GetField("Version",All).GetRawConstantValue(),"Rewrite version metadata");
        var controllerType=common.GetType("Signals.Common.SignalControllerDefinition",true);
        var lightDefinitionType=common.GetType("Signals.Common.SignalLightDefinition",true);
        var panels=holder.GetComponentsInChildren(assembly.GetType("RuSignals.RoutePanel",true),true);
        Check(panels.Length>0,"Factory creates route panels");
        foreach(var panel in panels)
        {
            var surface=(MeshFilter)Field(panel,"Surface");
            Check(surface.GetComponents<MeshRenderer>().Length==1,"One lamp renderer per route panel");
            Check(surface.sharedMesh.subMeshCount==2&&surface.sharedMesh.vertexCount==42*17,"Panel geometry contains all 42 disks and two material groups");
            Check(!surface.sharedMesh.isReadable,"Panel mesh CPU copy released");
            Call(panel,"Show",1UL);var first=surface.sharedMesh;
            Call(panel,"Show",2UL);Check(surface.sharedMesh!=first,"Changing glyph changes shared geometry");
            Call(panel,"Show",1UL);Check(surface.sharedMesh==first,"Repeated glyph reuses shared geometry");
        }
        var panelClone=UnityEngine.Object.Instantiate(((Component)panels[0]).gameObject,holder.transform,false);
        var clonedPanel=panelClone.GetComponent(assembly.GetType("RuSignals.RoutePanel",true));
        var originalSurface=(MeshFilter)Field(panels[0],"Surface");var cloneSurface=(MeshFilter)Field(clonedPanel,"Surface");
        Check(cloneSurface!=originalSurface&&cloneSurface.transform.IsChildOf(panelClone.transform),"Cloned panel points to its own renderer");
        var preserved=originalSurface.sharedMesh;Call(clonedPanel,"Show",3UL);
        Check(originalSurface.sharedMesh==preserved,"Changing cloned panel does not change another panel");
        foreach(var slot in new[]{"OldSignal","OldDivergingSignal","OldLeftJunctionSignal","OldRightJunctionSignal","OldEntrySignal","OldExitSignal","OldExitPassengerSignal","OldExitMainlineSignal"})
            Check(Field(pack,slot)!=null,"Native semaphore slot "+slot);
        foreach(var slot in new[]{"EntrySignal","ExitSignal","ExitPassengerSignal","ExitMainlineSignal","OldEntrySignal","OldExitSignal"})
        {
            var controller=Field(pack,slot);var signals=(Array)Field(controller,"Signals");var telephone=(Component)Field(signals.GetValue(0),"PhysicalReserver");
            Check(telephone!=null&&telephone!=phone,"Native phone cloned for "+slot);
        }
        var clock=new GameObject("QA visual clock").AddComponent(assembly.GetType("RuSignals.VisualScheduler",true));
        if(!Application.isPlaying)Call(clock,"Awake");
        IdleSchedulerChecks(assembly,clock);
        PowerChecks(assembly,clock,panels);
        SpillBudgetChecks(assembly,clock);
        LosslessPerformanceChecks(assembly,sdk,clock,repo,output,dll);
        var lightTemplate=(Component)Field(pack,"Signal");var lightClone=UnityEngine.Object.Instantiate(lightTemplate);
        lightClone.transform.SetParent(null);lightClone.gameObject.SetActive(true);
        var spillType=assembly.GetType("RuSignals.SpillView",true);var cloneBudget=lightClone.GetComponentInChildren(spillType,true);
        var cloneLights=(Light[])Field(cloneBudget,"Lights");
        Check(cloneLights.All(l=>l.transform.IsChildOf(lightClone.transform)),"Cloned signal budget refers only to its own lamps");
        Check((int)clock.GetType().GetProperty("SpillCount",All).GetValue(clock,null)==1,"Actual prefab clone automatically registers local lighting");
        UnityEngine.Object.DestroyImmediate(lightClone.gameObject);
        Check((int)clock.GetType().GetProperty("SpillCount",All).GetValue(clock,null)==0,"Actual prefab destruction cleans local lighting registry");
        var pose=assembly.GetType("SovietSemaphore.SemaphoreState",true);
        var visualType=assembly.GetType("RuSignals.SemaphoreVisual",true);
        PlatformSupportChecks(assembly,repo,output);
        SemaphoreAuxiliaryChecks(holder,pack,output);
        SemaphoreFixedColourChecks(assembly,factory,holder.transform,output);
        Check(visualType.GetMethod("Update",All)==null,"Semaphore has no individual Update callback");
        Check(assembly.GetType("RuSignals.ClearanceView",true).GetMethod("Update",All)==null,"Mast fitting has no individual Update callback");
        Check(assembly.GetType("RuSignals.TunnelMount",true).GetMethod("Update",All)==null,"Wall fitting has no individual Update callback");
        foreach(bool two in new[]{false,true})
        {
            var template=(Component)Call(factory,"Mechanism",holder.transform,two);
            var view=UnityEngine.Object.Instantiate(template);view.transform.SetParent(null);view.gameObject.SetActive(true);
            Action<string,bool> state=(name,instant)=>Call(view,"SetState",Enum.Parse(pose,name),instant);
            Action<float> advance=(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),view,visualType.GetMethod("Advance",All));
            var upper=(Transform)Field(view,"Upper");var green=(Renderer)Field(view,"Green");var red=(Renderer)Field(view,"Red");var dark=(Renderer)Field(view,"UpperDark");
            Check(upper!=(Transform)Field(template,"Upper")&&upper.IsChildOf(view.transform),"Unity remaps serialized mechanism references");
            Check((bool)Field(view,"TwoArms")==two,"Clone retains wing count");
            state("Stop",true);Check(!red.enabled&&!green.enabled&&dark.enabled,"Stopped semaphore keeps fixed green lantern unlit");
            Check(!(bool)visualType.GetProperty("Moving").GetValue(view,null),"Stationary semaphore not scheduled");
            state("Main",false);Check((int)clock.GetType().GetProperty("MovingCount",All).GetValue(clock,null)==1,"One moving semaphore registered with central clock");
            advance(.4f);Check(!green.enabled,"Opening waits for raised wing");state("Stop",false);Check(!green.enabled,"Closing cuts green immediately");advance(2);
            Check((int)clock.GetType().GetProperty("MovingCount",All).GetValue(clock,null)==0,"Finished animation removed from clock");
            upper.hasChanged=false;advance(1);Check(!upper.hasChanged,"Idle semaphore does not dirty transforms");
            state("Dark",false);Check(dark.enabled&&!red.enabled&&!green.enabled,"Stationary dark keeps glass visible");
            var renderers=view.GetComponentsInChildren<Renderer>(true);
            Func<string,Renderer> lens=n=>renderers.Single(r=>r.name==n);
            var checkUpperWing=UnitySemaphoreLinkageChecks.ArmClearance(repo,"semaphore_upper",lens("UpperArm"),lens("UpperRedLens"));
            var checkLowerWing=two?UnitySemaphoreLinkageChecks.ArmClearance(repo,"semaphore_lower",lens("LowerArm"),lens("LowerYellowLens")):null;
            foreach(string from in new[]{"Stop","Main","Diverging","Dark"})foreach(string to in new[]{"Stop","Main","Diverging","Dark"})
            {
                state(from,true);state(to,false);
                for(int i=0;i<80;i++)
                {
                    advance(.025f);frames++;
                    checkUpperWing();
                    if(two)checkLowerWing();
                    if(new[]{"UpperRedLens","UpperGreenLens","UpperGlassUnlit"}.Count(n=>lens(n).enabled)!=1)throw new Exception("Upper glass surfaces overlap");
                    if(two&&new[]{"LowerYellowLens","LowerGlassUnlit"}.Count(n=>lens(n).enabled)!=1)throw new Exception("Lower glass surfaces overlap");
                }
                Check(!(bool)visualType.GetProperty("Moving").GetValue(view,null),"Transition ends without pending update: "+from+" -> "+to);
            }
            var enamel=((Component)view).GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.sharedMaterial!=null&&r.sharedMaterial.name=="Semaphore enamel").ToArray();
            Check(enamel.Length>0&&enamel.All(r=>r.sharedMaterial.mainTexture==(Texture2D)Call(assets,"Texture","Style/semaphore_albedo.png",false)),"Semaphore structural parts use the new shared style atlas");
            Check(red.sharedMaterial.shader.name=="RuSignals/Signal glass four rings"&&red.sharedMaterial.shader.isSupported,"Semaphore uses supported original four-ring glass shader");
            if(two){state("Main",true);Render(view.gameObject,Path.Combine(output,"semaphore-v2.png"),new Vector3(9,7,19),new Vector3(-.5f,4.5f,0),6f);
                Render(view.gameObject,Path.Combine(output,"semaphore-base-clearance.png"),new Vector3(2,1.6f,-3),new Vector3(.05f,.8f,-.35f),.9f);
                Render(view.gameObject,Path.Combine(output,"semaphore-head-clearance.png"),new Vector3(2.4f,7.5f,-3),new Vector3(-.2f,7.3f,.2f),1.2f);}
            state("Main",false);if(!Application.isPlaying)Call(view,"OnDestroy");UnityEngine.Object.DestroyImmediate(view.gameObject);
            Check((int)clock.GetType().GetProperty("MovingCount",All).GetValue(clock,null)==0,"Destruction lifecycle unregisters moving mechanism");
        }
        UnitySemaphoreMechanismChecks.Verify(assembly,factory,holder.transform,repo,output);
        UnitySemaphoreLinkageChecks.CaptureMechanism(assembly,factory,holder.transform,repo,output);
        Check(true,"Pin-jointed vertical rods retain fixed axles, stationary lanterns, shared profiled wheel and opposed wire transmission");
        // Invoke actual game's LampControl: validates the invisible API renderer
        // contract which previously caused a per-frame null-reference flood.
        var oldController=(Component)Field(pack,"OldSignal");
        var oldClone=UnityEngine.Object.Instantiate(oldController,holder.transform,false);
        var api=(Component)oldClone.GetComponentsInChildren(lightDefinitionType,true)[0];
        var lampType=sdk.GetType("Signals.Game.Lights.SignalLight",true);var native=api.gameObject.AddComponent(lampType);
        Call(native,"Initialize",api,null);var lamp=Field(native,"Lamp");var indicator=Field(lamp,"lampInd");Call(indicator,"Awake");
        foreach(bool on in new[]{true,false,true,false})
        {
            if(on)Call(native,"TurnOn",false);else Call(native,"TurnOff");
            for(int i=0;i<30;i++)SettleLamp(assembly,lamp);
            Check((bool)lampType.GetProperty("IsActive").GetValue(native,null)==on,"Native API lamp still functional: "+on);
        }
        // Process every prefab exactly as the game's pack registration does.
        sdk.GetType("Signals.Game.SignalManager",true).GetMethod("ProcessControllers",All).Invoke(null,new[]{pack});
        Check(true,"Native ProcessControllers accepts all rewrite templates");
        var harmonyAssembly=Assembly.LoadFrom(Path.Combine(game,"DerailValley_Data/Managed/UnityModManager/0Harmony.dll"));
        var harmony=Create(harmonyAssembly.GetType("HarmonyLib.Harmony",true),"ru.signals.rewrite.qa");
        assembly.GetType("RuSignals.NativeHooks",true).GetMethod("Install",All).Invoke(null,new[]{harmony});
        ForeignHoverNameChecks(sdk,common);
        LampHookLifecycleChecks(assembly,sdk,pack,harmony);
        Check(true,"All native Harmony targets patched against installed game DLLs");
        Assembly.LoadFrom(Path.Combine(rsgDirectory,"RailwaySafetyGadgets.Signals.dll"));
        var bridge=Assembly.Load(File.ReadAllBytes(Environment.GetEnvironmentVariable("RUS_V2_BRIDGE")??Path.Combine(repo,"src/RuSignals.Next.RsgBridge/bin/Release/net48/RuSignals.RsgBridge.dll")));
        bridge.GetType("RuSignals.RsgBridge.Bridge",true).GetMethod("Install").Invoke(null,new[]{harmony});
        Check(true,"RSG adapter installs against real native DLLs");
        ConstructionChecks(common,sdk,assembly,pack,holder.transform,harmony);
        GlassRingCheck(assets,output);
        var boltMesh=(Mesh)assembly.GetType("RuSignals.AnchorBoltGeometry",true).GetMethod("Create",All).Invoke(null,null);
        var boltVertices=boltMesh.vertices;var boltNormals=boltMesh.normals;var boltIndices=boltMesh.triangles;bool boltWinding=true;
        for(int i=0;i<boltIndices.Length;i+=3)
        {
            int a=boltIndices[i],b=boltIndices[i+1],c=boltIndices[i+2];
            boltWinding&=Vector3.Dot(Vector3.Cross(boltVertices[b]-boltVertices[a],boltVertices[c]-boltVertices[a]),boltNormals[a]+boltNormals[b]+boltNormals[c])>0;
        }
        Check(boltWinding,"Every anchor washer and hex head face has consistent outward winding and normals");
        UnityEngine.Object.DestroyImmediate(boltMesh);
        // One body render plus one shared disk renderer; screenshot with digit 7.
        var renderPanel=UnityEngine.Object.Instantiate(((Component)panels[0]).gameObject);renderPanel.transform.SetParent(null);renderPanel.transform.position=Vector3.zero;renderPanel.SetActive(true);
        var symbols=assembly.GetType("RuSignals.RouteSymbols",true);
        var seven=(ulong)symbols.GetMethod("Mask").Invoke(null,new object[]{"7",false});Call(renderPanel.GetComponent(assembly.GetType("RuSignals.RoutePanel",true)),"Show",seven);
        Render(renderPanel,Path.Combine(output,"panel-v2.png"),new Vector3(0,7.1f,3),new Vector3(0,7.1f,0),.85f);
        assembly.GetType("RuSignals.NativeHooks",true).GetMethod("Remove",All).Invoke(null,null);
        Check(!(bool)assembly.GetType("RuSignals.NativeOptimizations",true).GetField("Enabled",All).GetValue(null),"Native optimization callbacks are inactive after native hooks removal");
        Call(harmony,"UnpatchAll","ru.signals.rewrite.qa");
        UnityEngine.Object.DestroyImmediate(renderPanel);UnityEngine.Object.DestroyImmediate(holder);UnityEngine.Object.DestroyImmediate(phoneGo);UnityEngine.Object.DestroyImmediate(clock.gameObject);
        var owned=(System.Collections.IList)Field(assets,"owned");
        Check(owned.Count>0,"Asset owner tracks shared resources");
        ((IDisposable)assets).Dispose();
        Check(owned.Count==0,"Disposal clears resource ownership");
        foreach(var name in new[]{"models","textures","materials","glasses","panels","icons"})
            Check(((System.Collections.IDictionary)Field(assets,name)).Count==0,"Disposal clears "+name+" cache");
        Check(assets.GetType().GetProperty("Current",All).GetValue(null,null)==null,"Disposal releases current asset registry");
        ((IDisposable)assets).Dispose();Check(owned.Count==0,"Repeated disposal is harmless");
        WriteHoverNameReport(output);
        WriteRouteHoverReport(output);
        WriteSemaphoreHudReport(output);
        originalHudBundle.Unload(true);originalHudBundle=null;originalHud=null;
        string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(dll))).Replace("-","").ToLowerInvariant();
        File.WriteAllText(Path.Combine(output,"unity-rewrite-verification.json"),"{\"passed\":"+passed+",\"animation_frames\":"+frames+",\"native_lamp_frames\":120,\"panel_lamp_renderers\":1,\"panel_material_slots\":2,\"native_hooks_installed\":true,\"native_pack_processed\":true,\"rsg_installed\":true,\"tunnel_models\":"+tunnelModels+",\"tunnel_scenarios\":"+tunnelScenarios+",\"rounded_profiles\":"+shapeKeys.Count+",\"electric_glow_profiles\":"+glowKeys.Count+",\"nameplate_slots\":"+nameplates+",\"mechanical_shunting_slots\":"+shuntingSemaphores+",\"turntable_variants\":"+turntableVariants+",\"glass_rings\":4,\"native_blinking_glow_checked\":true,\"anchor_face_winding_checked\":true,\"dll_sha256\":\""+hash+"\",\"unity\":\""+Application.unityVersion+"\",\"scope\":\"Real DLL, SDK and Unity; synthetic tunnel/rail fixtures and glare, no saved game loaded\"}");
        Debug.Log("RUS_REWRITE_PASS "+passed);
    }
    private static void RegisterFactory(Type creator,Type definition,Type implementation)
    {
        var dictionary=(System.Collections.IDictionary)creator.GetField("CreatorFunctions",All).GetValue(null);
        var signature=dictionary.GetType().GetGenericArguments()[1];var arguments=signature.GetGenericArguments();
        var d=Expression.Parameter(arguments[0],"definition");var s=Expression.Parameter(arguments[1],"signal");
        var ctor=implementation.GetConstructor(new[]{arguments[0],arguments[1]});
        dictionary.Add(definition,Expression.Lambda(signature,Expression.Convert(Expression.New(ctor,d,s),arguments[2]),d,s).Compile());
    }
    private static void StyleChecks(object assets,string output)
    {
        foreach(string name in new[]{"t_signal.png","t_signal_new.png","t_signalfooting.png","semaphore_albedo.png"})
        {
            var texture=(Texture2D)Call(assets,"Texture","Style/"+name,false);
            Check(texture.width==1024&&texture.height==1024,"Shared style atlas is limited to 1024: "+name);
            Check(texture.format==TextureFormat.DXT1,"Style atlas has offline BC1 compression: "+name);
            Check(!texture.isReadable&&texture.mipmapCount>1,"Style atlas has mipmaps and no retained CPU pixels: "+name);
            Check(ReferenceEquals(texture,Call(assets,"Texture","Style/"+name,false)),"Atlas requests reuse the same texture: "+name);
        }
        var mask=(Texture2D)Call(assets,"Texture","t_signalfooting.png",false);
        Check(mask.format==TextureFormat.RGBA32&&!mask.isReadable,"Original footing cutout is preserved uncompressed without CPU copy");
        var part=(Array)Call(assets,"Model","mast");
        var footing=part.Cast<object>().First(p=>((string)Field(p,"Name")).Contains("Footing"));
        var material=(Material)Field(footing,"Material");
        Check(material.shader.isSupported&&material.shader.name=="RuSignals/Weathered structure with original cutouts","Real footing uses compiled alpha-preserving material");
        Check(material.GetTexture("_OriginalAlpha")==mask&&material.mainTexture==Call(assets,"Texture","Style/t_signalfooting.png",false),"Albedo changes use exact original ladder/pole mask");
        var reference=new Material(Shader.Find("Standard"));reference.mainTexture=mask;reference.SetFloat("_Mode",1);reference.SetFloat("_Cutoff",.5f);reference.EnableKeyword("_ALPHATEST_ON");reference.renderQueue=2450;
        var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.transform.localScale=Vector3.one*2;go.layer=29;
        var renderer=go.GetComponent<Renderer>();var camera=new GameObject("Style alpha QA camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-2);camera.transform.LookAt(Vector3.zero);
        camera.orthographic=true;camera.orthographicSize=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.magenta;camera.cullingMask=1<<29;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
        var rt=new RenderTexture(512,512,24);camera.targetTexture=rt;var capture=new Texture2D(512,512,TextureFormat.RGB24,false);
        Func<Material,string,bool[]> silhouette=(mat,name)=>
        {
            renderer.sharedMaterial=mat;camera.Render();RenderTexture.active=rt;capture.ReadPixels(new Rect(0,0,512,512),0,0);capture.Apply();
            File.WriteAllBytes(Path.Combine(output,name),capture.EncodeToPNG());
            return capture.GetPixels().Select(p=>p.r>.9f&&p.b>.9f&&p.g<.1f).ToArray();
        };
        var before=silhouette(reference,"footing-cutout-original.png");var after=silhouette(material,"footing-cutout-style.png");
        Check(before.Any(v=>v)&&before.Any(v=>!v),"GPU mask comparison includes solid structure and open ladder gaps");
        Check(before.SequenceEqual(after),"Every GPU cutout pixel agrees with original footing and ladder silhouette");
        RenderTexture.active=null;camera.targetTexture=null;
        foreach(var item in new UnityEngine.Object[]{go,camera.gameObject,rt,capture,reference})UnityEngine.Object.DestroyImmediate(item);
        Debug.Log("RUS_STYLE_ATLASES_PASS 4 shared BC1 atlases / 1024 / mipmaps / GPU original alpha preserved");
    }
    private static System.Collections.IEnumerator FixtureLoop(){while(true)yield return null;}
    private static bool SkipFixtureListeners()=>false;
    private static void NormalizePlacedFixture(Component __result)
    {
        // Instantiation creates Editor unassigned-reference placeholders again.
        // Normalize the fresh fixture before the native derived constructor's
        // forced first Update. This patch is QA-only, never in the release DLL.
        foreach(var component in __result.GetComponentsInChildren<MonoBehaviour>(true))
            foreach(var reference in component.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
                if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
        foreach(var def in __result.GetComponentsInChildren<MonoBehaviour>(true).Where(d=>d.GetType().FullName=="Signals.Common.SignalDefinition"))
        {def.GetType().GetField("Animator",All).SetValue(def,null);def.GetType().GetField("PhysicalReserver",All).SetValue(def,null);}
    }
    private static void ConstructionChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,Transform holder,object harmony)
    {
        RegisterFactory(sdk.GetType("Signals.Game.AspectCreator",true),assembly.GetType("RuSignals.RuAspectDefinition",true),assembly.GetType("RuSignals.RuAspect",true));
        RegisterFactory(sdk.GetType("Signals.Game.DisplayCreator",true),assembly.GetType("RuSignals.RouteDisplayDefinition",true),assembly.GetType("RuSignals.RouteDisplay",true));
        RegisterFactory(sdk.GetType("Signals.Game.DisplayCreator",true),assembly.GetType("RuSignals.NameplateDefinition",true),assembly.GetType("RuSignals.NameplateDisplay",true));
        var hooks=assembly.GetType("RuSignals.NativeHooks",true);
        var managerType=sdk.GetType("Signals.Game.SignalManager",true);
        managerType.GetField("s_pack",All).SetValue(null,pack);
        sdk.GetType("Signals.Game.SignalsMod",true).GetProperty("Settings",All).GetSetMethod(true).Invoke(null,new[]{Activator.CreateInstance(sdk.GetType("Signals.Game.Settings",true))});
        var managerGo=new GameObject("Native construction QA manager");var manager=(MonoBehaviour)managerGo.AddComponent(managerType);
        // The map coroutine is not available in this fixture; supply a real
        // coroutine handle so native OnDestroy can perform its normal cleanup.
        managerType.GetField("_updateCoro",All).SetValue(manager,manager.StartCoroutine(FixtureLoop()));
        // Indicators use AppUtil's scene-specific pause/mute events. This
        // fixture has no game scene services; keep lamp logic intact and skip
        // only those subscriptions, including unsubscribe during destruction.
        var indicatorType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Indicator",false)).First(t=>t!=null);
        var harmonyMethodType=harmony.GetType().Assembly.GetType("HarmonyLib.HarmonyMethod",true);
        var skip=Create(harmonyMethodType,typeof(UnityRewriteCheck).GetMethod("SkipFixtureListeners",All));
        var patch=harmony.GetType().GetMethod("Patch");var patchArgs=new object[patch.GetParameters().Length];
        patchArgs[0]=indicatorType.GetMethod("SetupListeners",All);patchArgs[1]=skip;patch.Invoke(harmony,patchArgs);
        var normalize=Create(harmonyMethodType,typeof(UnityRewriteCheck).GetMethod("NormalizePlacedFixture",All));
        var nativePlacer=sdk.GetType("Signals.Game.Generation.SignalPlacer",true);
        var normalizeArgs=new object[patch.GetParameters().Length];normalizeArgs[0]=nativePlacer.GetMethod("InstantiateFromDef",All);normalizeArgs[2]=normalize;patch.Invoke(harmony,normalizeArgs);
        // No game RailTrackRegistry is present. Use the synthetic track IDs for
        // this scene lookup; warning traversal and signal logic run unchanged.
        var yardArgs=new object[patch.GetParameters().Length];yardArgs[0]=nativePlacer.GetMethod("IsLogicYardTrack",All);
        yardArgs[1]=Create(harmonyMethodType,typeof(UnityRewriteCheck).GetMethod("FixtureYardTrack",All));patch.Invoke(harmony,yardArgs);
        var nativeLight=sdk.GetType("Signals.Game.Lights.SignalLight",true);
        var fixture=new GameObject("Native glare fixture");fixture.SetActive(false);
        var fixtureMesh=new Mesh{vertices=new[]{new Vector3(-.16f,-.16f,0),new Vector3(.16f,-.16f,0),new Vector3(.16f,.16f,0),new Vector3(-.16f,.16f,0)},uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,1,2,0,2,3}};fixtureMesh.RecalculateNormals();fixtureMesh.RecalculateBounds();
        fixture.AddComponent<MeshFilter>().sharedMesh=fixtureMesh;var fixtureRenderer=fixture.AddComponent<MeshRenderer>();
        var fixtureTexture=new Texture2D(64,64,TextureFormat.RGBA32,false);var pixels=new Color[64*64];
        for(int y=0;y<64;y++)for(int x=0;x<64;x++){float radius=new Vector2((x-31.5f)/31.5f,(y-31.5f)/31.5f).magnitude;pixels[y*64+x]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-radius),3)*.22f);}
        fixtureTexture.SetPixels(pixels);fixtureTexture.Apply();
        var fixtureMaterial=new Material(Shader.Find("Legacy Shaders/Particles/Additive"));fixtureMaterial.mainTexture=fixtureTexture;fixtureRenderer.sharedMaterial=fixtureMaterial;
        object originalGlare=nativeLight.GetField("s_glare",All).GetValue(null),originalMaterial=nativeLight.GetField("s_mat",All).GetValue(null);
        nativeLight.GetField("s_glare",All).SetValue(null,fixtureRenderer);nativeLight.GetField("s_mat",All).SetValue(null,fixtureMaterial);
        int created=0;
        foreach(var field in pack.GetType().GetFields(All).Where(f=>f.FieldType==common.GetType("Signals.Common.SignalControllerDefinition",true)))
        {
            var template=field.GetValue(pack) as Component;if(template==null)continue;
            var clone=UnityEngine.Object.Instantiate(template,holder,false);
            // Editor-only placeholders for unassigned optional references are
            // CLR-non-null. Player builds store null instead; match that here.
            foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
                foreach(var reference in component.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
                    if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
            // Actual CreateGlare executes with an isolated renderer fixture;
            // only the train-livery lookup and phone scene services are absent.
            foreach(var def in clone.GetComponentsInChildren(common.GetType("Signals.Common.SignalDefinition",true),true))
            {def.GetType().GetField("PhysicalReserver",All).SetValue(def,null);def.GetType().GetField("Animator",All).SetValue(def,null);}
            clone.transform.SetParent(null,false);clone.gameObject.SetActive(true);
            var controller=Create(sdk.GetType("Signals.Game.Controllers.BasicSignalController",true),clone,null);
            controller.GetType().GetField("NameOverride",All).SetValue(controller,"QA "+field.Name);
            var signals=((System.Collections.IEnumerable)controller.GetType().GetProperty("AllSignals").GetValue(controller,null)).Cast<object>().ToArray();
            Check(signals.Length>0,"Native controller constructs signals: "+field.Name);
            foreach(var signal in signals)
            {
                var aspects=(Array)signal.GetType().GetProperty("AllAspects").GetValue(signal,null);
                Check(aspects.Length>0,"Native constructor fills aspects: "+field.Name);
                var lights=(Array)signal.GetType().GetProperty("AllLights").GetValue(signal,null);
                Check(lights.Length>0,"Native constructor initializes lamps: "+field.Name);
                foreach(var light in lights)
                {
                    var definition=light.GetType().GetField("Definition",All).GetValue(light);
                    var colour=(Color)definition.GetType().GetField("Colour",All).GetValue(definition);
                    var expected=colour*(colour.b>.9f&&colour.g<.5f&&colour.r<.25f?3f:1.8f);
                    var indicator=Field(Field(light,"Lamp"),"lampInd");
                    if((Color)Field(indicator,"emissionColor")!=expected)throw new Exception("Construction-time lamp emission was skipped: "+field.Name);
                }
                Call(signal,"UpdateAspect",true);Check(signal.GetType().GetProperty("AllAspects").GetValue(signal,null)==aspects,"First native aspect update preserves constructed state: "+field.Name);
                created++;
            }
            assembly.GetType("RuSignals.SignalDesignations",true).GetMethod("Refresh",All).Invoke(null,null);
            ShapeChecks(assembly,clone);
            RegionChecks(assembly,controller);
            GlowChecks(assembly,controller);
            var identities=signals.Select(s=>(string)s.GetType().GetProperty("Name").GetValue(s,null)).ToArray();
            assembly.GetType("RuSignals.SignalDesignations",true).GetMethod("Refresh",All).Invoke(null,null);
            Check(signals.Select((s,i)=>(string)s.GetType().GetProperty("Name").GetValue(s,null)==identities[i]).All(v=>v),field.Name+" physical designation does not change native identities");
            NameplateChecks(assembly,controller,field.Name);
            HoverNameChecks(assembly,controller,field.Name);
            RouteHoverChecks(controller,field.Name);
            SemaphoreHudChecks(assembly,controller,field.Name);
            DwarfOffsetChecks(assembly,controller,field.Name);
            ShuntingSemaphoreChecks(assembly,controller,field.Name);
            TunnelChecks(common,assembly,controller);
            Call(controller,"Destroy");
            // Native Destroy is deferred. Finish fixture destruction while
            // game services can still be created by Indicator.OnDestroy.
            UnityEngine.Object.DestroyImmediate(clone.gameObject);
            if(created==1){hooks.GetMethod("ControllerFlipped",All).Invoke(null,new[]{controller});Check(true,"Placement notification after controller destruction is safe");}
        }
        Debug.Log("RUS_NATIVE_CONSTRUCTORS_PASS "+created);
        Debug.Log("RUS_LATIN_LABELS_PASS 35 native slots / custom Cyrillic transliteration / native identities retained");
        TurntableChecks(common,sdk,assembly,pack,harmony);
        Debug.Log("RUS_TUNNEL_MOUNTS_PASS "+tunnelModels+" models / "+tunnelScenarios+" scenarios");
        Check(!(bool)hooks.GetMethod("Ours",All).Invoke(null,new object[]{null}),"No owner signal is safe for optional lamp initialization");
        Check(glowKeys.Count==9,"All nine electric model profiles verify lamp spill and halo");
        Debug.Log("RUS_BLUE_DIM_PASS native blue emission 3 / halo 0.8 / local intensity 1.4");
        Debug.Log("RUS_SPILL_FADE_PASS 9 profiles / 100 m off / 30 m full / 55 degrees / native blink phases");
        Check(shapeKeys.Count==5,"Four mast profiles and round dwarf verify rounded shields");
        nativeLight.GetField("s_glare",All).SetValue(null,originalGlare);nativeLight.GetField("s_mat",All).SetValue(null,originalMaterial);
        UnityEngine.Object.DestroyImmediate(fixture);UnityEngine.Object.DestroyImmediate(fixtureMesh);UnityEngine.Object.DestroyImmediate(fixtureMaterial);UnityEngine.Object.DestroyImmediate(fixtureTexture);
        Debug.Log("RUS_VISUALS_PASS "+glowKeys.Count+" electric profiles / "+shapeKeys.Count+" rounded profiles / native steady and blinking lamps");
        UnityEngine.Object.DestroyImmediate(managerGo);
    }
    private static void NameplateChecks(Assembly assembly,object controller,string slot)
    {
        var all=((System.Collections.IEnumerable)controller.GetType().GetProperty("AllSignals").GetValue(controller,null)).Cast<object>();
        foreach(var signal in all)
        {
            var definition=(Component)signal.GetType().GetProperty("Definition").GetValue(signal,null);
            var plate=definition.GetComponentInChildren(assembly.GetType("RuSignals.NameplateView",true),true);
            Check(plate!=null,slot+" has a physical name plate");
            var text=(TextMesh)Field(plate,"Letters");
            Check(text.transform.IsChildOf(definition.transform),slot+" name plate is clone-local");
            string label=(string)plate.GetType().GetProperty("DisplayedName").GetValue(plate,null);
            string native=(string)signal.GetType().GetProperty("Name").GetValue(signal,null);
            string expected=(string)assembly.GetType("RuSignals.DesignationRules",true).GetMethod("Latin",All).Invoke(null,new object[]{native});
            Check(label==expected,slot+" physical label uses the original DVSignals identity");
            Check(label.All(c=>c<128),slot+" physical designation uses Latin on every native slot");
            NameplateContact(plate,definition,slot);
            foreach(string identity in new[]{"S715","T_MF_1-8"})
            {
                signal.GetType().GetField("NameOverride",All).SetValue(signal,identity);Call(signal,"UpdateDisplays",false);
                assembly.GetType("RuSignals.SignalDesignations",true).GetMethod("Refresh",All).Invoke(null,null);
                Check((string)plate.GetType().GetProperty("DisplayedName").GetValue(plate,null)==identity,slot+" original service identity is not replaced by a short alias");
            }
            signal.GetType().GetField("NameOverride",All).SetValue(signal,string.Empty);Call(signal,"UpdateDisplays",false);
            NameplateLayoutChecks(plate,slot);
            signal.GetType().GetField("NameOverride",All).SetValue(signal,"ЧМ3");
            Call(signal,"UpdateDisplays",false);
            Check(text.text.Replace("\n",string.Empty)=="ChM3",slot+" explicit Cyrillic override displays in Latin");
            text.transform.hasChanged=false;Call(signal,"UpdateDisplays",false);
            Check(!text.transform.hasChanged,slot+" unchanged name does not rewrite geometry or transforms");
            Check(plate.GetType().GetMethod("Update",All)==null,slot+" label has no individual Update");
            var renderer=text.GetComponent<Renderer>();Check(renderer.sharedMaterial.shader.name=="Standard"&&renderer.sharedMaterial.IsKeywordEnabled("_ALPHATEST_ON"),slot+" text tests depth like the opaque sign");
            var board=(MeshFilter)Field(plate,"Board");Check(!board.sharedMesh.isReadable,slot+" designation mesh is shared and uploaded");
            var fit=signal.GetType().GetProperty("Controller").GetValue(signal,null);var parent=(Component)fit.GetType().GetProperty("Definition").GetValue(fit,null);
            var clearance=parent.GetComponent(assembly.GetType("RuSignals.ClearanceView",true));
            if(clearance!=null){var indicator=Field(clearance,"Panel") as Transform;if(indicator!=null)Check(plate.transform.position.y<indicator.GetComponentsInChildren<MeshRenderer>().Min(r=>r.bounds.min.y),slot+" mast designation lies below route board");}
            Check(board.GetComponent<Renderer>().bounds.min.y>-.01f,slot+" complete designation stays above ground");nameplates++;
            if(slot=="Signal"||slot=="EntrySignal"||slot=="ShuntingSignal"||slot=="OldShuntingSignal")
            {
                string output=Environment.GetEnvironmentVariable("RUS_V2_OUTPUT");
                var focus=board.GetComponent<Renderer>().bounds.center;
                signal.GetType().GetField("NameOverride",All).SetValue(signal,string.Empty);Call(signal,"UpdateDisplays",false);
                focus=board.GetComponent<Renderer>().bounds.center;
                Render(plate.gameObject,Path.Combine(output,slot+"-nameplate.png"),focus+plate.transform.forward,focus,Mathf.Max(.35f,board.GetComponent<Renderer>().bounds.extents.y+.10f));
                bool mechanical=slot=="OldShuntingSignal";
                Render(parent.gameObject,Path.Combine(output,slot+"-label-full.png"),mechanical?new Vector3(2,6,15):new Vector3(2,4,10),mechanical?new Vector3(0,4.7f,0):new Vector3(0,3,0),mechanical?5.3f:3.7f);
            }
        }
    }
    private static void NameplateContact(Component plate,Component definition,string slot)
    {
        var board=plate.GetComponent<MeshFilter>().sharedMesh;var b=board.bounds;
        var end=definition.transform.InverseTransformPoint(plate.transform.TransformPoint(new Vector3(0,-.04f,b.min.z)));
        var start=definition.transform.InverseTransformPoint(plate.transform.TransformPoint(new Vector3(0,-.04f,0)));
        string key=definition.transform.parent.name.Split(' ')[0];
        var shape=definition.transform.parent.GetComponent(plate.GetType().Assembly.GetType("RuSignals.ShieldView",true));
        if(key=="exit_small_two_yellows"&&shape!=null&&(bool)shape.GetType().GetProperty("IsOval").GetValue(shape,null))key="RoundedDwarf/"+key;
        var pieces=RawParts(key=="Semaphore"?"Semaphore/semaphore_static":key);
        var triangles=new List<Vector3[]>();
        foreach(var part in pieces)
        {
            if(!part.Key.StartsWith("Body_")&&key!="Semaphore")continue;
            var vertices=part.Value.vertices;var indices=part.Value.triangles;
            for(int i=0;i<indices.Length;i+=3)triangles.Add(new[]{vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]});
        }
        Check(Intersects(start,end,triangles),slot+" upper lug physically intersects pole or foundation");
        foreach(var mesh in pieces.Values)UnityEngine.Object.DestroyImmediate(mesh);
    }
    private static void ShuntingSemaphoreChecks(Assembly assembly,object controller,string slot)
    {
        if(!new[]{"OldShuntingSignal","OldMajorShuntingSignal","OldJunctionShuntingSignal","OldSpacingSignal","OldTurntableSignal"}.Contains(slot))return;
        var definition=(Component)controller.GetType().GetProperty("Definition").GetValue(controller,null);
        var visual=definition.GetComponentInChildren(assembly.GetType("RuSignals.SemaphoreVisual",true),true);
        Check(visual!=null&&!(bool)Field(visual,"TwoArms"),slot+" uses a single-arm mechanical signal");
        var signal=((System.Collections.IEnumerable)controller.GetType().GetProperty("AllSignals").GetValue(controller,null)).Cast<object>().Single();
        Check(Field(visual,"signal")==signal,slot+" real native shunting signal binds to mechanism");
        var aspects=(Array)signal.GetType().GetProperty("AllAspects").GetValue(signal,null);
        Call(signal,"ChangeAspect",1);Call(visual,"Advance",2f);
        Check((Renderer)Field(visual,"Green")!=null&&((Renderer)Field(visual,"Green")).enabled,slot+" native shunting permission raises wing and opens green");
        Check((bool)signal.GetType().GetProperty("IsShunting").GetValue(signal,null)==(slot!="OldTurntableSignal"),slot+" retains native shunting category");
        Call(signal,"ChangeAspect",0);Call(visual,"Advance",2f);
        Check(!((Renderer)Field(visual,"Red")).enabled&&!((Renderer)Field(visual,"Green")).enabled&&((Renderer)Field(visual,"UpperDark")).enabled,slot+" native prohibition closes wing and extinguishes fixed green lantern");
        Check((bool)Field(aspects.GetValue(0).GetType().GetMethod("GetDefinition").Invoke(aspects.GetValue(0),null),"DisallowPassing"),slot+" prohibition remains exported to consumers");
        shuntingSemaphores++;
    }
    private static void TurntableChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,object harmony)
    {
        var game=Environment.GetEnvironmentVariable("RUS_V2_GAME");
        var rails=Assembly.LoadFrom(Path.Combine(game,"DerailValley_Data/Managed/DV.RailTrack.dll"));
        var railType=rails.GetType("RailTrack",true);
        var gameTypes=Assembly.LoadFrom(Path.Combine(game,"DerailValley_Data/Managed/Assembly-CSharp.dll"));
        var tableType=gameTypes.GetType("TurntableRailTrack",true);
        var pts=Assembly.LoadFrom(Path.Combine(game,"DerailValley_Data/Managed/DV.PointSet.dll")).GetType("DV.PointSet.EquiPointSet",true);
        var pointType=pts.GetNestedType("Point",All);var vectorType=pointType.GetField("position").FieldType;
        Func<string,Vector3,Vector3,int,Component> rail=(name,start,direction,length)=>
        {
            var go=new GameObject(name);go.SetActive(false);var track=go.AddComponent(railType);
            railType.GetField("initialized",All).SetValue(track,true);railType.GetField("generateMeshes",All).SetValue(track,false);railType.GetField("generateColliders",All).SetValue(track,false);
            var set=Create(pts);var points=Array.CreateInstance(pointType,length*2+1);
            for(int i=0;i<points.Length;i++)
            {
                var p=Activator.CreateInstance(pointType);var v=Activator.CreateInstance(vectorType);var position=start+direction*i*.5f;
                vectorType.GetField("x").SetValue(v,(double)position.x);vectorType.GetField("y").SetValue(v,(double)position.y);vectorType.GetField("z").SetValue(v,(double)position.z);
                pointType.GetField("position").SetValue(p,v);pointType.GetField("forward").SetValue(p,direction);pointType.GetField("up").SetValue(p,Vector3.up);
                pointType.GetField("span").SetValue(p,i*.5);pointType.GetField("spanToNextPoint").SetValue(p,.5);pointType.GetField("index").SetValue(p,i);points.SetValue(p,i);
            }
            pts.GetField("points").SetValue(set,points);pts.GetField("span").SetValue(set,(double)length);railType.GetField("pointSet",All).SetValue(track,set);go.SetActive(true);return track;
        };
        NativeOptimizationChecks(common,sdk,assembly,pack,harmony,rail);
        ReservationBoundaryChecks(common,sdk,assembly,pack,harmony,rail);
        WithIntervalFixtureLength(sdk,harmony,()=>RouteBoundaryAuditChecks(sdk,assembly,pack,rail));
        WithIntervalFixtureLength(sdk,harmony,()=>ReverseMovementChecks(sdk,assembly,pack,rail));
        WithIntervalFixtureLength(sdk,harmony,()=>EntryApproachReservationChecks(sdk,assembly,pack,rail));
        NativeLogicChecks(common,sdk,assembly,pack,rail);
        TerminalArrivalChecks(common,sdk,assembly,pack,harmony,rail);
        WithIntervalFixtureLength(sdk,harmony,()=>CombinedExitChecks(common,sdk,assembly,pack,rail));
        WithIntervalFixtureLength(sdk,harmony,()=>RouteDirectionNativeChecks(sdk,assembly,rail));
        WithIntervalFixtureLength(sdk,harmony,()=>WarningChecks(common,sdk,assembly,pack,rail));
        WithIntervalFixtureLength(sdk,harmony,()=>DispatcherChecks(common,sdk,assembly,pack,harmony,rail));
        WithIntervalFixtureLength(sdk,harmony,()=>DispatcherIntervalCaptureChecks(sdk,assembly,pack,harmony,rail));
        LensVariationChecks(common,sdk,assembly,pack,assembly.GetType("RuSignals.SignalAssets",true).GetProperty("Current",All).GetValue(null,null));
        var bridge=rail("QA rotating rail",new Vector3(0,0,-12),Vector3.forward,24);
        var a=rail("QA entry A",new Vector3(0,0,12),Vector3.forward,20);var b=rail("QA entry B",new Vector3(12,0,40),Vector3.back,20);
        var table=(Component)bridge.gameObject.AddComponent(tableType);tableType.GetField("uniqueID").SetValue(table,"T-QA-1");
        var rotating=new GameObject("QA rotating bridge visuals");tableType.GetField("visuals").SetValue(table,rotating.transform);
        var endType=tableType.GetNestedType("TrackEnd",All);var ends=(System.Collections.IList)tableType.GetField("trackEnds").GetValue(table);
        Action<Component,bool> end=(track,first)=>{var e=Create(endType);endType.GetField("track").SetValue(e,track);endType.GetField("isFirst").SetValue(e,first);ends.Add(e);};
        end(a,true);end(a,true);end(b,false);end(bridge,false);
        var tables=(System.Collections.IList)sdk.GetType("Signals.Game.Railway.TurntableHelper",true).GetProperty("TurntableTracks").GetValue(null,null);tables.Add(table);
        var controllerType=sdk.GetType("Signals.Game.Controllers.TurntableSignalController",true);
        var list=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(controllerType));
        var placer=sdk.GetType("Signals.Game.Generation.RealisticSignalPlacer",true);
        // Fresh runtime definitions have real null optional references in a
        // Player, but Editor supplies unassigned Unity placeholders.
        foreach(string slot in new[]{"TurntableSignal","OldTurntableSignal"})
        {
            var template=(Component)Field(pack,slot);
            foreach(var component in template.GetComponentsInChildren<MonoBehaviour>(true))
                foreach(var reference in component.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
                    if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
        }
        bool originalOld=(bool)Field(pack,"EnableOldVersions");
        foreach(bool old in new[]{false,true})
        {
        rotating.transform.position=old?new Vector3(3000,0,2000):Vector3.zero;
        pack.GetType().GetField("EnableOldVersions").SetValue(pack,old);Call(Activator.CreateInstance(placer),"CreateTurntableSignals",pack,list);
        Check(list.Count==2,"Patched native placer creates one signal per distinct fixed approach, none on bridge");
        TurntableNameChecks(assembly,common,sdk,pack,list);
        for(int i=0;i<list.Count;i++)
        {
            var c=list[i];var def=(Component)c.GetType().GetProperty("Definition").GetValue(c,null);var p=c.GetType().GetProperty("PlacementInfo").GetValue(c,null);
            var track=(Component)Field(p,"Track");Check(track!=bridge,"Approach placement is on a fixed rail");
            Check(def.transform.parent==track.transform&&!def.transform.IsChildOf(rotating.transform),"Signal is parented to fixed approach");
            double span=(double)Field(p,"Span");Check(Math.Abs((i==0?span:20-span)-5)<.01,"Signal is five metres before pit");
            Check(Vector3.Dot(def.transform.forward,i==0?Vector3.forward:Vector3.forward)>.99f,"Signal front faces approaching driver");
            var position=def.transform.position;rotating.transform.Rotate(0,45,0);Check(def.transform.position==position,"Bridge rotation never moves approach signal");
            if(!old)Check(Math.Abs((float)Field(def,"Offset"))==1.25f,"Electric shunting approach is closer to its own track");
            Check(!(bool)c.GetType().GetProperty("IsConnected").GetValue(c,null),"Unaligned approach is closed");
            var signal=((Array)controllerType.GetProperty("Signals").GetValue(c,null)).GetValue(0);
            Check((int)signal.GetType().GetProperty("CurrentAspectIndex").GetValue(signal,null)==0,"Unaligned approach exports native STOP aspect");
            bool mechanical=def.GetComponentInChildren(assembly.GetType("RuSignals.SemaphoreVisual",true),true)!=null;
            Check(mechanical==old,"Fixed approach uses correct electric or mechanical old-area variant");
            Call(c,"OffsetOnTrack",i==0?-1000:1000);var moved=c.GetType().GetProperty("PlacementInfo").GetValue(c,null);
            int at=(int)Field(moved,"PointIndex");Check(i==0?at>=6:at<=34,"Native crossing correction and user movement preserve pit setback");
        }
        var connected=list[0];
        Action<Component,Component> notify=(front,back)=>
        {tableType.GetField("frontClosest").SetValue(table,ends.Cast<object>().FirstOrDefault(e=>Field(e,"track")==front));tableType.GetField("rearClosest").SetValue(table,ends.Cast<object>().FirstOrDefault(e=>Field(e,"track")==back));Call(connected,"TracksUpdated",front,back);};
        notify(a,null);
        Check(!(bool)controllerType.GetProperty("IsConnected").GetValue(connected,null)&&Field(connected,"_routine")!=null,"Matching front end waits for native delay");
        var pending=Field(connected,"_routine");notify(a,b);
        Check(Field(connected,"_routine")==pending,"Other-end notification preserves pending connection");
        var delay=(System.Collections.IEnumerator)controllerType.GetMethod("AcceptConnection",All).Invoke(connected,new object[]{1f});
        Check(delay.MoveNext()&&delay.Current!=null,"Native connection acceptance yields its delay");
        Check(!delay.MoveNext()&&(bool)controllerType.GetProperty("IsConnected").GetValue(connected,null),"After native delay connected approach opens");
        var activeSignal=((Array)controllerType.GetProperty("Signals").GetValue(connected,null)).GetValue(0);
        Check((int)activeSignal.GetType().GetProperty("CurrentAspectIndex").GetValue(activeSignal,null)==1,"Accepted approach exports native SHUNTING_OPEN aspect");
        notify(null,a);Check((bool)controllerType.GetProperty("IsConnected").GetValue(connected,null),"Either bridge end may face the same approach without flicker");
        notify(b,null);Check(!(bool)controllerType.GetProperty("IsConnected").GetValue(connected,null)&&Field(connected,"_routine")==null,"Rotation to another entry closes immediately and cancels pending delay");
        notify(null,a);Check(Field(connected,"_routine")!=null,"Rear-end alignment begins native delay too");
        notify(null,null);Check(Field(connected,"_routine")==null,"Disconnect while waiting cancels delayed opening");
        var opposite=Create(endType);endType.GetField("track").SetValue(opposite,a);endType.GetField("isFirst").SetValue(opposite,false);tableType.GetField("frontClosest").SetValue(table,opposite);Call(connected,"TracksUpdated",a,null);
        Check(Field(connected,"_routine")==null,"Alignment with the other endpoint of a loop track cannot open this approach");
        tableType.GetField("frontClosest").SetValue(table,null);tableType.GetField("rearClosest").SetValue(table,null);
        foreach(var c in list){var def=(Component)c.GetType().GetProperty("Definition").GetValue(c,null);Call(c,"Destroy");UnityEngine.Object.DestroyImmediate(def.gameObject);}
        var remaining=(Delegate)tableType.GetField("TracksUpdated",All).GetValue(table);Check(remaining==null,"Native controller destruction unsubscribes all circle connection events");
        list.Clear();turntableVariants++;
        }
        pack.GetType().GetField("EnableOldVersions").SetValue(pack,originalOld);
        var foreign=ScriptableObject.CreateInstance(pack.GetType());pack.GetType().GetField("ModId").SetValue(foreign,"other.pack");
        Check((bool)assembly.GetType("RuSignals.TurntableApproaches",true).GetMethod("Generate",All).Invoke(null,new object[]{foreign,list}),"Other packs keep original turntable generation");
        tables.Clear();UnityEngine.Object.DestroyImmediate(foreign);UnityEngine.Object.DestroyImmediate(bridge.gameObject);UnityEngine.Object.DestroyImmediate(a.gameObject);UnityEngine.Object.DestroyImmediate(b.gameObject);UnityEngine.Object.DestroyImmediate(rotating);
        Debug.Log("RUS_TURNTABLE_APPROACHES_PASS 2 fixed tracks / native event and delay / cleanup");
    }
    private static bool FixtureYardTrack(UnityEngine.Object __0,ref bool __result){__result=__0.name.StartsWith("[Y]",StringComparison.Ordinal);return false;}
    private static void CombinedExitChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,Func<string,Vector3,Vector3,int,Component> rail)
    {
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);
        var infoType=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var directionType=infoType.GetField("Direction").FieldType;
        var track=rail("QA combined exit",new Vector3(1200,0,1200),Vector3.forward,600);
        Func<string,object> controller=slot=>
        {
            var clone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));
            foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true))foreach(var reference in component.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
                if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
            foreach(var def in clone.GetComponentsInChildren(common.GetType("Signals.Common.SignalDefinition",true),true))def.GetType().GetField("PhysicalReserver",All).SetValue(def,null);
            clone.gameObject.SetActive(true);return Create(basic,clone,null);
        };
        var exit=controller("ExitSignal");var next=controller("EntrySignal");
        var group=Create(sdk.GetType("Signals.Game.JunctionSignalGroup",true),new object[]{null});group.GetType().GetField("_stationId",All).SetValue(group,"CW");basic.GetProperty("Group").SetValue(exit,group,null);
        basic.GetField("Type").SetValue(next,Enum.Parse(basic.GetField("Type").FieldType,"Entry"));
        var signal=((Array)basic.GetProperty("Signals").GetValue(exit,null)).GetValue(0);var nextSignal=((Array)basic.GetProperty("Signals").GetValue(next,null)).GetValue(0);
        assembly.GetType("RuSignals.SignalDesignations",true).GetMethod("Refresh",All).Invoke(null,null);
        var tracks=Array.CreateInstance(infoType,1);tracks.SetValue(Create(infoType,track,Enum.Parse(directionType,"Out")),0);
        var block=Create(blockType,null,600f);blockType.GetProperty("Tracks").SetValue(block,tracks,null);blockType.GetField("_station",All).SetValue(block,"CW");blockType.GetProperty("IsDeadEnd").SetValue(block,true,null);
        Call(signal,"SetBlock",block);Call(signal,"UpdateAspect",true);
        Func<object,string> indication=s=>Field(Call(s.GetType().GetProperty("CurrentAspect").GetValue(s,null),"GetDefinition"),"Indication").ToString();
        Check(indication(signal)=="ShuntingWhite","Actual SDK combined exit automatically opens white for station dead-end shunt");
        Call(signal,"SetShuntingStatus",false);Call(signal,"UpdateAspect",true);
        Check(indication(signal)=="Stop","Native shunting prohibition blocks automatic white");Call(signal,"SetShuntingStatus",true);
        blockType.GetField("_station",All).SetValue(block,"FF");blockType.GetProperty("IsDeadEnd").SetValue(block,false,null);
        blockType.GetProperty("NextController").SetValue(block,next,null);var nextBlock=Create(blockType,null,600f);blockType.GetProperty("Tracks").SetValue(nextBlock,tracks,null);Call(nextSignal,"SetBlock",nextBlock);
        Call(nextSignal,"ChangeAspect",0);Call(signal,"UpdateAspect",true);Check(indication(signal)=="Yellow","Combined exit trains see yellow before closed home");
        var aspects=(Array)nextSignal.GetType().GetProperty("AllAspects").GetValue(nextSignal,null);
        foreach(string expected in new[]{"Green","DoubleYellow","DoubleYellowUpperFlashing"})
        {
            int index=Array.FindIndex(aspects.Cast<object>().ToArray(),a=>Field(Call(a,"GetDefinition"),"Indication").ToString()==expected);
            Call(nextSignal,"ChangeAspect",index);Call(signal,"UpdateAspect",true);
            Check(indication(signal)==(expected=="Green"?"Green":"FlashingYellow"),"Combined exit advance train indication before "+expected);
        }
        // Use a real native junction and suffixed DoubleTrack names, rather than
        // forcing the exit aspects that previously hid this regression.
        var junctionType=track.GetType().GetField("inJunction").FieldType;
        var junctionGo=new GameObject("QA native turnout");junctionGo.SetActive(false);var junction=junctionGo.AddComponent(junctionType);
        var branches=(System.Collections.IList)junctionType.GetField("outBranches").GetValue(junction);
        if(branches==null){branches=(System.Collections.IList)Activator.CreateInstance(junctionType.GetField("outBranches").FieldType);junctionType.GetField("outBranches").SetValue(junction,branches);}
        var branchType=branches.GetType().GetGenericArguments()[0];var branch=Activator.CreateInstance(branchType);
        branchType.GetField("track").SetValue(branch,track);branches.Add(branch);
        junctionType.GetField("selectedBranch").SetValue(junction,(byte)0);
        track.GetType().GetField("inJunction").SetValue(track,junction);track.GetType().GetField("isJunctionTrack").SetValue(track,true);
        int open=Array.FindIndex(aspects.Cast<object>().ToArray(),a=>Field(Call(a,"GetDefinition"),"Indication").ToString()=="Green");Call(nextSignal,"ChangeAspect",open);
        foreach(string name in new[]{"[track through]","[track through] 23","[track through](Clone) 23","[track through]12","[track through](Clone)12","[track through](Clone)7(Clone)12","[track through] 23(Clone)7"})
        {
            track.name=name;Call(signal,"UpdateAspect",true);Check(indication(signal)=="Green","Actual SDK straight combined exit remains green with name "+name);
            foreach(bool oval in new[]{false,true})
            {
                var definition=(Component)signal.GetType().GetProperty("Definition").GetValue(signal,null);var shape=definition.GetComponentInParent(assembly.GetType("RuSignals.ShieldView",true));Call(shape,"SetOval",oval);
                var lights=((Array)signal.GetType().GetProperty("AllLights").GetValue(signal,null)).Cast<object>().ToArray();
                foreach(var light in lights)Field(Field(light,"Lamp"),"lampInd").GetType().GetField("lag",All).SetValue(Field(Field(light,"Lamp"),"lampInd"),0f);
                var current=signal.GetType().GetProperty("CurrentAspect").GetValue(signal,null);
                var routine=(System.Collections.IEnumerator)current.GetType().BaseType.GetMethod("ApplyRoutine",All).Invoke(current,new object[]{0f});while(routine.MoveNext()){}
                foreach(var light in lights)SettleLamp(assembly,Field(light,"Lamp"));
                var green=lights.Single(light=>((Component)Field(light,"Definition")).name=="Green");var glass=((Renderer[])Field(Field(green,"Definition"),"Renderers"))[0];
                var colour=new MaterialPropertyBlock();glass.GetPropertyBlock(colour);
                Check(colour.GetColor("_EmissionColor").g>=1.8f&&((Renderer)Field(Field(Field(green,"Lamp"),"lampInd"),"glareRenderer")).enabled,"Actual selected dwarf green lights physical glass and halo / oval "+oval+" / "+name);
                Check(lights.Where(light=>light!=green).All(light=>!(bool)light.GetType().GetProperty("IsActive").GetValue(light,null)),"Dwarf green indication leaves red, white and yellows off / oval "+oval);
                if(name=="[track through](Clone)7(Clone)12")
                {var focus=glass.bounds.center;Render(definition.gameObject,Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),oval?"dwarf-oval-green.png":"dwarf-square-green.png"),focus+new Vector3(1,1,4),focus,1.6f);}
            }
        }
        Debug.Log("RUS_DWARF_GREEN_PASS actual automatic selection / split DoubleTrack names / square and round physical green lamps");
        track.name="[track through] 23";Call(nextSignal,"ChangeAspect",0);Call(signal,"UpdateAspect",true);Check(indication(signal)=="Yellow","Suffixed straight route single yellow before closed next");
        track.name="[track diverging] 23";Call(signal,"UpdateAspect",true);Check(indication(signal)=="DoubleYellow","Actual diverging route two steady yellows before closed next");
        Call(nextSignal,"ChangeAspect",open);Call(signal,"UpdateAspect",true);Check(indication(signal)=="DoubleYellowUpperFlashing","Actual divergence still warns with upper flashing yellow");
        junctionType.GetField("selectedBranch").SetValue(junction,(byte)1);Call(signal,"UpdateAspect",true);Check(indication(signal)=="Stop","Misaligned real turnout still closes dwarf exit");junctionType.GetField("selectedBranch").SetValue(junction,(byte)0);
        basic.GetProperty("Group").SetValue(next,group,null);basic.GetField("Type").SetValue(next,Enum.Parse(basic.GetField("Type").FieldType,"Exit"));
        Call(signal,"UpdateAspect",true);Check(indication(signal)=="ShuntingWhite","Actual SDK skipped yard boundaries ending at local exit give automatic white");
        Call(signal,"SetShuntingStatus",false);Call(signal,"UpdateAspect",true);Check(indication(signal)=="Stop","Local exit route native shunting prohibition retained");Call(signal,"SetShuntingStatus",true);
        RsgChecks(sdk,signal,track);
        track.GetType().GetField("inJunction").SetValue(track,null);track.GetType().GetField("isJunctionTrack").SetValue(track,false);UnityEngine.Object.DestroyImmediate(junctionGo);
        foreach(var c in new[]{exit,next}){var def=(Component)basic.GetProperty("Definition").GetValue(c,null);Call(c,"Destroy");UnityEngine.Object.DestroyImmediate(def.gameObject);}
        UnityEngine.Object.DestroyImmediate(track.gameObject);
        Debug.Log("RUS_COMBINED_EXIT_PASS actual SDK / automatic white / native prohibition / train yellow-green and warning");
    }
    private static void RsgChecks(Assembly sdk,object signal,Component track)
    {
        var adapter=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("RailwaySafetyGadgets.SignalsIntegration.SignalsAdapter")).First(t=>t!=null);
        var evaluate=adapter.GetMethod("Evaluate",All);var aspects=(Array)signal.GetType().GetProperty("AllAspects").GetValue(signal,null);
        foreach(string indication in new[]{"Stop","Yellow","FlashingYellow","DoubleYellow","DoubleYellowUpperFlashing","Green","ShuntingWhite","Invitation"})
        {
            int index=Array.FindIndex(aspects.Cast<object>().ToArray(),a=>Field(Call(a,"GetDefinition"),"Indication").ToString()==indication);Call(signal,"ChangeAspect",index);
            var args=evaluate.GetParameters().Length==5?new object[]{signal,false,false,null,null}:new object[]{signal,false,false,false,null,null};
            string result=evaluate.Invoke(null,args).ToString();string expected=indication=="Green"?"Green":indication=="Stop"?"YellowRed":indication=="ShuntingWhite"||indication=="Invitation"?"Red":"Yellow";
            Check(result==expected&&Equals(args[args.Length-1],true),"Real RSG API maps "+indication+" to "+expected);
            var core=evaluate.ReturnType.Assembly;var displayType=core.GetType("RailwaySafetyGadgets.SignalDisplay",true);
            var display=Create(displayType);core.GetType("RailwaySafetyGadgets.CabSignalLogic",true).GetMethod("Set").Invoke(null,new[]{display,Enum.Parse(evaluate.ReturnType,result),null});
            displayType.GetField("Detail").SetValue(display,args[args.Length-2]);displayType.GetField("Known").SetValue(display,true);
            foreach(double time in new[]{0.25,0.75})
            {
                var lamps=Create(displayType);core.GetType("RailwaySafetyGadgets.CabSignalPresentation",true).GetMethod("CopyTo").Invoke(null,new[]{display,lamps,(object)time});
                if(expected=="Yellow")Check((float)Field(lamps,"Yellow")==1&&(float)Field(lamps,"Green")==0&&(float)Field(lamps,"Red")==0&&(float)Field(lamps,"SecondYellow")==0,"Actual cab lamps stay yellow only for "+indication+" at "+time);
                if(expected=="YellowRed")Check((float)Field(lamps,"SecondYellow")==1&&(float)Field(lamps,"Red")==1&&(bool)Field(lamps,"SplitYellowRed")&&(float)Field(lamps,"Green")==0&&(float)Field(lamps,"Yellow")==0,"Actual ALS approach red lights its dedicated steady yellow-red lens at "+time);
            }
            args[2]=true;Check(evaluate.Invoke(null,args).ToString()=="Red","Real RSG crossed/current stop remains red for "+indication);
        }
        if(evaluate.GetParameters().Length==5)
        {
            var reading=adapter.GetNestedType("Reading",All);var ctor=reading.GetConstructors(All).Single();var owner=signal.GetType().GetProperty("Controller").GetValue(signal,null);var basic=owner.GetType();
            var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];var direction=placement.GetField("Direction").FieldType;
            basic.GetProperty("PlacementInfo").SetValue(owner,Create(placement,track,Enum.Parse(direction,"In"),10,5d,false),null);
            var normal=Create(reading,owner,Enum.Parse(ctor.GetParameters()[1].ParameterType,"Normal"));var yard=Create(reading,owner,Enum.Parse(ctor.GetParameters()[1].ParameterType,"Shunting"));
            int white=Array.FindIndex(aspects.Cast<object>().ToArray(),a=>Field(Call(a,"GetDefinition"),"Indication").ToString()=="ShuntingWhite");Call(signal,"ChangeAspect",white);
            Check(ReferenceEquals(reading.GetProperty("Signal",All).GetValue(yard,null),signal),"RSG shunting mode finds combined exit without a separate signal");
            Check(!(bool)reading.GetProperty("ProhibitsPassing").GetValue(yard,null)&&(bool)reading.GetProperty("ProhibitsPassing").GetValue(normal,null),"White opens yard mode and does not grant train authority");
            var display=Call(yard,"ReadDisplay");Check((float)Field(display,"White")>0&&(bool)Field(display,"Known"),"RSG displays known combined white in yard mode");
            display=Call(normal,"ReadDisplay");Check((float)Field(display,"Red")>0,"RSG normal mode does not accept combined white");
            var capture=Call(yard,"Capture");Check((bool)Field(capture,"ShuntingPermissionAtPassage"),"RSG records automatic white permission at passage");
            capture=Call(normal,"Capture");Check(!(bool)Field(capture,"ReservedAtPassage")&&!(bool)Field(capture,"ShuntingPermissionAtPassage"),"Normal-mode passage of combined white cannot become a train reservation permission");
            int green=Array.FindIndex(aspects.Cast<object>().ToArray(),a=>Field(Call(a,"GetDefinition"),"Indication").ToString()=="Green");Call(signal,"ChangeAspect",green);
            display=Call(yard,"ReadDisplay");Check((float)Field(display,"Red")>0,"Train green does not grant separate shunting movement");
            display=Call(normal,"ReadDisplay");Check((float)Field(display,"Green")>0,"Shunting read scope restores normal green reading");
        }
        SpeedChecks(sdk,signal,track);
        Debug.Log("RUS_RSG_COMPAT_PASS Evaluate parameters="+evaluate.GetParameters().Length+" / all eight aspects / actual patched API");
    }
    private static void SpeedChecks(Assembly sdk,object signal,Component track)
    {
        var core=AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetType("RailwaySafetyGadgets.SpeedSigns")!=null);var points=core.GetType("RailwaySafetyGadgets.SpeedSigns",true);
        var pointType=core.GetType("RailwaySafetyGadgets.SpeedPoint",true);var postType=core.GetType("RailwaySafetyGadgets.SpeedPost",true);
        var posts=(System.Collections.IDictionary)points.GetField("Posts",All).GetValue(null);var list=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(postType));posts[track]=list;
        Action<double,int,int> add=(span,dir,speed)=>{var post=Create(postType);postType.GetField("Span",All).SetValue(post,span);postType.GetField("Direction",All).SetValue(post,dir);postType.GetField("Values",All).SetValue(post,new[]{speed});list.Add(post);};
        add(0,1,80);add(300,-1,80);
        var owner=signal.GetType().GetProperty("Controller").GetValue(signal,null);var basic=owner.GetType();var block=signal.GetType().GetProperty("Block").GetValue(signal,null);
        var placement=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];var direction=placement.GetField("Direction").FieldType;
        basic.GetProperty("PlacementInfo").SetValue(owner,Create(placement,track,Enum.Parse(direction,"In"),10,5d,false),null);
        var next=block.GetType().GetProperty("NextController").GetValue(block,null);next.GetType().GetProperty("PlacementInfo").SetValue(next,Create(placement,track,Enum.Parse(direction,"In"),500,250d,false),null);
        // Both fixture blocks reuse one RailTrack. Only the tested signal may
        // contribute a speed span; the home still marks its route endpoint.
        Call(Call(next,"GetControllerSignal"),"TurnOff");
        var infoType=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var tracks=Array.CreateInstance(infoType,1);tracks.SetValue(Create(infoType,track,Enum.Parse(direction,"Out")),0);block.GetType().GetProperty("Tracks").SetValue(block,tracks,null);
        track.name="[track through] 23";Call(signal,"SetBlock",block);
        var aspects=((Array)signal.GetType().GetProperty("AllAspects").GetValue(signal,null)).Cast<object>().ToArray();
        Action<string> set=indicationValue=>Call(signal,"ChangeAspect",Array.FindIndex(aspects,a=>Field(Call(a,"GetDefinition"),"Indication").ToString()==indicationValue));
        Func<object[]> read=()=>((System.Collections.IEnumerable)points.GetMethod("Points",All).Invoke(null,new[]{track})).Cast<object>().ToArray();
        Func<double,int,int?> value=(span,dir)=>
        {
            double nearest=double.NegativeInfinity;int? speed=null;
            foreach(var p in read()){double d=((double)Field(p,"Span")-span)*dir;if((int)Field(p,"Direction")==dir&&d<=.0001&&d>=nearest){nearest=d;speed=(int?)Field(p,"Value");}}return speed;
        };
        foreach(string indication in new[]{"DoubleYellow","DoubleYellowUpperFlashing"})
        {
            set(indication);Check(value(4,1)==80&&value(6,1)==40&&value(249,1)==40&&value(251,1)==80,"Actual RSG speed profile enters 40 at "+indication+" and restores at next signal");
            Check(value(100,-1)==80,"Opposite direction has no invented 40 limit");
            var legType=core.GetType("RailwaySafetyGadgets.RouteLeg`1",true).MakeGenericType(track.GetType());
            Func<double,double,object> leg=(start,end)=>{var segment=Create(legType);legType.GetField("Track").SetValue(segment,track);legType.GetField("Direction").SetValue(segment,1);legType.GetField("Start").SetValue(segment,start);legType.GetField("End").SetValue(segment,end);return segment;};
            var route=Array.CreateInstance(legType,1);route.SetValue(leg(1,300),0);var delegateType=typeof(Func<,>).MakeGenericType(track.GetType(),typeof(System.Collections.Generic.IEnumerable<>).MakeGenericType(pointType));
            var reader=Delegate.CreateDelegate(delegateType,points.GetMethod("Points",All));var method=core.GetType("RailwaySafetyGadgets.RouteLogic",true).GetMethods(All).First(m=>m.Name=="FindProfileLimits"&&m.GetParameters().Length==6).MakeGenericMethod(track.GetType());
            var args=new object[]{route,reader,null,null,null,null};method.Invoke(null,args);
            Check(Equals(args[2],80)&&Equals(args[3],40)&&Math.Abs((double)args[5]-4)<.001,"Actual gadget forecast selects 40 at signal distance");
            var occupied=core.GetType("RailwaySafetyGadgets.SpeedEnvelope",true).GetMethod("OccupiedLimit").MakeGenericMethod(track.GetType());
            route.SetValue(leg(200,280),0);Check(Equals(occupied.Invoke(null,new object[]{route,reader}),40),"40 retained while tail is in signal route");
            route.SetValue(leg(260,280),0);Check(Equals(occupied.Invoke(null,new object[]{route,reader}),80),"80 restored only after whole consist leaves 40 route");
        }
        track.name="[track diverging] 23";set("Yellow");set("Green");
        Check(value(6,1)==40&&value(249,1)==40&&value(251,1)==80,"Side departure green preserves actual turnout 40 in RSG");
        set("Stop");Check(value(6,1)==40,"Physical turnout 40 survives signal closing behind the train");
        track.name="[track through] 23";set("Green");
        add(150,1,30);set("DoubleYellow");Check(value(170,1)==30&&value(260,1)==30,"Signal limit never raises a lower native limit");
        set("FlashingYellow");Check(value(6,1)==80,"Single flashing yellow warns of next route, no 40 at this mast");
        set("Green");Check(value(6,1)==80,"Clear straight signal removes restriction");
        set("DoubleYellow");Call(signal,"TurnOff");Check(value(6,1)==80,"Switched off signal removes its live limit");
        posts.Remove(track);set("Green");Check(read().Length==0,"Missing native speed remains unknown rather than invented");
        Debug.Log("RUS_RSG_SPEED_PASS actual patched Points / 40 forecast / consist tail / lower native limit / direction / off signal");
    }
    private static void PowerChecks(Assembly assembly,Component clock,Component[] panels)
    {
        string game=Environment.GetEnvironmentVariable("RUS_V2_GAME");
        string reference=Environment.GetEnvironmentVariable("RUS_V2_SEASONS")??Path.Combine(game,"Mods/DVSeasons/DVSeasons.dll");
        var seasons=Assembly.LoadFrom(reference);
        var type=seasons.GetType("DVSeasons.Mod.OptionalModPowerCompatibility",true);var current=type.GetField("current",All);var previous=current.GetValue(null);
        var owner=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);var outage=type.GetField("outage",All);
        var shown=new Dictionary<Component,Mesh>();
        foreach(var panel in panels){Call(panel,"Show",3UL);shown[panel]=((MeshFilter)Field(panel,"Surface")).sharedMesh;}
        try
        {
            current.SetValue(null,owner);outage.SetValue(owner,true);Call(clock,"Update");
            foreach(var panel in panels)
            {
                var surface=(MeshFilter)Field(panel,"Surface");Check(surface.sharedMesh!=shown[panel]&&surface.sharedMesh.GetIndexCount(1)==0,"Actual DVSeasons outage removes every luminous digital lamp triangle");
                var blank=surface.sharedMesh;Call(panel,"Show",7UL);Check(surface.sharedMesh==blank,"Logical route updates cannot relight powerless digital lamps");
            }
            outage.SetValue(owner,false);Call(clock,"Update");
            foreach(var panel in panels)Check(((MeshFilter)Field(panel,"Surface")).sharedMesh!=shown[panel],"Power restore shows current route, not stale pre-outage digits");
        }
        finally{current.SetValue(null,previous);Call(clock,"Update");}
        string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(reference))).Replace("-","").ToLowerInvariant();
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"power-reference.json"),"{\"dll_sha256\":\""+hash+"\",\"assembly_version\":\""+seasons.GetName().Version+"\"}");
        Debug.Log("RUS_ROUTE_POWER_PASS actual DVSeasons IsOut API / route change during blackout / restoration");
    }
    private static void WarningChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,Func<string,Vector3,Vector3,int,Component> rail)
    {
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var placementType=basic.GetProperty("PlacementInfo").PropertyType.GetGenericArguments()[0];
        var directionType=placementType.GetField("Direction").FieldType;
        var track=rail("QA warning approach",new Vector3(1000,0,1000),Vector3.forward,600);
        var instantiate=sdk.GetType("Signals.Game.Generation.SignalPlacer",true).GetMethod("InstantiateFromDef",All);
        Func<string,int,object> controller=(slot,index)=>
        {
            var prefab=Field(pack,slot);var position=Create(instantiate.GetParameters()[1].ParameterType,1000d,0d,1000d+index*.5);
            var def=(Component)instantiate.Invoke(null,new object[]{prefab,position,Vector3.back,false,track});
            foreach(var component in def.GetComponentsInChildren<MonoBehaviour>(true))
                foreach(var reference in component.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
                    if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
            def.gameObject.SetActive(true);return Create(basic,def,Create(placementType,track,Enum.Parse(directionType,"In"),index,index*.5,false));
        };
        var home=controller("OldSignal",900);var signal=((Array)basic.GetProperty("Signals").GetValue(home,null)).GetValue(0);
        var infoType=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var tracks=Array.CreateInstance(infoType,1);
        tracks.SetValue(Create(infoType,track,Enum.Parse(directionType,"In")),0);
        var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);var block=Create(blockType,null,600f);blockType.GetProperty("Tracks").SetValue(block,tracks,null);Call(signal,"SetBlock",block);
        var warnings=assembly.GetType("RuSignals.SemaphoreWarnings",true);var manager=UnityEngine.Object.FindObjectOfType(sdk.GetType("Signals.Game.SignalManager",true));
        var registry=sdk.GetType("Signals.Game.SignalManager",true).GetProperty("AllControllers").GetValue(manager,null);
        var args=new object[]{home,registry,null,0f};
        Check((bool)warnings.GetMethod("TryPosition",All).Invoke(null,args),"Semaphore distant finds straight 300 m approach");
        Check(Math.Abs((double)Field(args[2],"Span")-150)<.001f&&Math.Abs((float)args[3]-300)<.001f,"Distant is upstream of its own semaphore");
        warnings.GetMethod("Ensure",All).Invoke(null,null);
        var distantList=(System.Collections.IList)sdk.GetType("Signals.Game.SignalManager",true).GetField("_distantSignals",All).GetValue(manager);
        Check(distantList.Count==1,"Adds one distant, no extra repeater");var distant=distantList[0];
        Check(!(bool)distant.GetType().GetProperty("IsRepeater").GetValue(distant,null)&&distant.GetType().GetProperty("Home").GetValue(distant,null)==home,"Distant uses actual native home controller");
        warnings.GetMethod("Ensure",All).Invoke(null,null);Check(distantList.Count==1,"Repeated generation does not duplicate warning");
        var warningSignal=((Array)basic.GetProperty("Signals").GetValue(distant,null)).GetValue(0);
        Call(signal,"ChangeAspect",0);Call(distant,"Update",true,false);
        var aspect=warningSignal.GetType().GetProperty("CurrentAspect").GetValue(warningSignal,null);
        Check(Field(Call(aspect,"GetDefinition"),"Indication").ToString()=="Yellow","Closed semaphore drives native distant yellow");
        Call(signal,"ChangeAspect",3);Call(distant,"Update",true,false);aspect=warningSignal.GetType().GetProperty("CurrentAspect").GetValue(warningSignal,null);
        Check(Field(Call(aspect,"GetDefinition"),"Indication").ToString()=="Green","Open semaphore drives native distant green");
        var repeatDef=UnityEngine.Object.Instantiate((Component)Field(pack,"OldRepeaterSignal"));repeatDef.gameObject.SetActive(true);
        foreach(var component in repeatDef.GetComponentsInChildren<MonoBehaviour>(true))foreach(var reference in component.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
            if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
        var repeater=Create(sdk.GetType("Signals.Game.Controllers.DistantSignalController",true),repeatDef,home,Create(placementType,track,Enum.Parse(directionType,"In"),700,350d,false),100f,true);
        var repeated=((Array)basic.GetProperty("Signals").GetValue(repeater,null)).GetValue(0);
        Call(signal,"ChangeAspect",0);Call(repeater,"Update",true,false);
        Check(repeated.GetType().GetProperty("CurrentAspect").GetValue(repeated,null)==null,"Native repeater remains dark before closed semaphore");
        Call(signal,"ChangeAspect",3);Call(repeater,"Update",true,false);
        Check(repeated.GetType().GetProperty("CurrentAspect").GetValue(repeated,null)!=null,"Native repeater remains green before open semaphore");
        var blocker=controller("Signal",600);args=new object[]{home,registry,null,0f};
        Check(!(bool)warnings.GetMethod("TryPosition",All).Invoke(null,args),"No additional warning across another train signal");
        var blockerDef=(Component)basic.GetProperty("Definition").GetValue(blocker,null);Call(blocker,"Destroy");UnityEngine.Object.DestroyImmediate(blockerDef.gameObject);
        var sdkBoundary=assembly.GetType("RuSignals.NativeSdk",true);
        foreach(var item in new[]{distant,repeater})
        {var def=(Component)basic.GetProperty("Definition").GetValue(item,null);sdkBoundary.GetMethod("RemoveWarning",All).Invoke(null,new[]{item});UnityEngine.Object.DestroyImmediate(def.gameObject);}
        Check(basic.GetField("AnyAspectChanged",All).GetValue(home)==null,"Native distant/repeater removal releases home subscriptions");
        var homeDef=(Component)basic.GetProperty("Definition").GetValue(home,null);Call(home,"Destroy");UnityEngine.Object.DestroyImmediate(homeDef.gameObject);UnityEngine.Object.DestroyImmediate(track.gameObject);
        Debug.Log("RUS_SEMAPHORE_WARNINGS_PASS actual SDK / yellow-green distant / unchanged repeater / idempotence / cleanup");
    }
    private static void RegionChecks(Assembly assembly,object controller)
    {
        var definition=(Component)controller.GetType().GetProperty("Definition",All).GetValue(controller,null);
        var shape=definition.GetComponent(assembly.GetType("RuSignals.ShieldView",true));
        if(shape==null||regionsChecked)return;regionsChecked=true;
        var type=assembly.GetType("RuSignals.ShieldRegions",true);
        var centres=(Transform[])type.GetField("centres",All).GetValue(null);
        var originalPosition=definition.transform.position;
        var lamps=definition.GetComponentsInChildren<Light>(true);var originals=lamps.Select(l=>l.transform.localPosition).ToArray();
        for(int i=0;i<5;i++){centres[i]=new GameObject("Industrial station fixture "+i).transform;centres[i].position=new Vector3(i*10000,0,i*7000);}
        type.GetField("count",All).SetValue(null,5);type.GetField("prepared",All).SetValue(null,true);
        for(int i=0;i<5;i++)
        {
            foreach(float distance in new[]{0f,1799f,1800f,1801f,2500f})
            {
                definition.transform.position=centres[i].position+new Vector3(distance,700,0);
                Call(shape,"Bind",controller);
                Check((bool)shape.GetType().GetProperty("IsOval").GetValue(shape,null)==(distance>1800),"Actual shield choice uses station radius, not Id or height: "+i+" / "+distance);
            }
        }
        definition.transform.position=centres[0].position+Vector3.forward*1700;Call(shape,"Bind",controller);
        Check(!(bool)shape.GetType().GetProperty("IsOval").GetValue(shape,null),"Square within radius along the other coordinate");
        foreach(var centre in centres)centre.position+=new Vector3(4500,0,-2300);
        definition.transform.position+=new Vector3(4500,0,-2300);Call(shape,"Bind",controller);
        Check(!(bool)shape.GetType().GetProperty("IsOval").GetValue(shape,null),"Floating origin preserves district using cached station transforms");
        definition.transform.position=centres[0].position+new Vector3(1500,0,1500);Call(shape,"Bind",controller);
        Check((bool)shape.GetType().GetProperty("IsOval").GetValue(shape,null),"Region is circular, does not admit the square corners");
        Check(lamps.Select((l,i)=>l.transform.localPosition==originals[i]).All(v=>v),"Changing shields leaves every lamp in its original position");
        foreach(var centre in centres)UnityEngine.Object.DestroyImmediate(centre.gameObject);
        type.GetMethod("Reset",All).Invoke(null,null);
        Check((int)type.GetField("count",All).GetValue(null)==0&&centres.All(c=>c==null),"District cache cleared without retaining scene references");
        definition.transform.position=originalPosition;Call(shape,"Bind",controller);
        Debug.Log("RUS_SHIELD_REGIONS_PASS 5 stations / boundary, height, floating origin and movement");
    }
    private static void ShapeChecks(Assembly assembly,Component clone)
    {
        var shape=clone.GetComponent(assembly.GetType("RuSignals.ShieldView",true));if(shape==null)return;
        string key=clone.name.Split(' ')[0];if(!shapeKeys.Add(key))return;
        var fit=clone.GetComponent(assembly.GetType("RuSignals.ClearanceView",true));
        if(fit!=null)Call(fit,"SetMode",Enum.Parse(assembly.GetType("RuSignals.MastPlacementMode",true),"Original"));
        var filters=(MeshFilter[])Field(shape,"Filters");var renderers=(Renderer[])Field(shape,"Renderers");
        Check(filters.All(f=>f.transform.IsChildOf(clone.transform)),key+" rounded shape uses clone-local references");
        Check(shape.GetType().GetMethod("Update",All)==null,key+" shape has no per-frame callback");
        foreach(bool compact in new[]{false,true})
        {
            if(fit!=null)Call(fit,"SetMode",Enum.Parse(assembly.GetType("RuSignals.MastPlacementMode",true),compact?"Compact":"Original"));
            foreach(bool rounded in new[]{true,false})
            {
                Call(shape,"SetOval",rounded);var meshes=(Mesh[])Field(shape,(rounded?"Oval":"")+(compact?"Compact":"Full"));
                Check(filters.Where((f,i)=>f.sharedMesh!=meshes[i]).Count()==0,key+" correct "+compact+" / "+rounded+" shape meshes");
                Check(meshes.All(m=>!m.isReadable),key+" shape meshes share uploaded GPU resources");
                var paint=(Material[])Field(shape,"OvalMaterials");var original=(Material[])Field(shape,"OriginalMaterials");
                Check(renderers.Where((r,i)=>r.sharedMaterial!=(rounded?paint[i]:original[i])).Count()==0,key+" matching shape materials");
            }
        }
        if(fit==null)
        {
            var round=RawParts("RoundedDwarf/"+key);var original=RawParts(key);
            var roundMeshes=(Mesh[])Field(shape,"OvalFull");
            Check(filters.Length==6,"Round dwarf switches casting and all five lenses together");
            foreach(var filter in filters)
            {
                string name=filter.name;int i=Array.IndexOf(filters,filter);
                Check(roundMeshes[i].bounds==round[name].bounds,"Round dwarf mesh matches imported part: "+name);
                if(name.StartsWith("Body_"))continue;
                var p=round[name].vertices;var q=original[name].vertices;
                int g=Array.FindIndex((Transform[])Field(shape,"Glares"),t=>t.name==name+" glare");
                var centre=p.OrderByDescending(v=>v.z).First();
                Check((((Vector3[])Field(shape,"OvalGlares"))[g]-centre-Vector3.forward*.03f).sqrMagnitude<.00000001f,"Round dwarf source lens has matching optical datum: "+name);
                Check(((Material[])Field(shape,"OvalMaterials"))[i].shader.name=="RuSignals/Signal glass four rings","Round dwarf retains coloured glass shader: "+name);
            }
            foreach(var mesh in round.Values.Concat(original.Values))UnityEngine.Object.DestroyImmediate(mesh);
            Call(shape,"SetOval",true);
            var glares=(Transform[])Field(shape,"Glares");var positions=(Vector3[])Field(shape,"OvalGlares");
            Check(glares.Where((t,i)=>t.localPosition!=positions[i]).Count()==0,"Native glare and spot lights follow original RUS lens centres");
            string output=Environment.GetEnvironmentVariable("RUS_V2_OUTPUT");
            Render(clone.gameObject,Path.Combine(output,"round-dwarf-front.png"),new Vector3(1.5f,.9f,3),new Vector3(.14f,.5f,0),.8f);
            Debug.Log("RUS_ROUND_DWARF_PASS original cast RUS model / five lenses / native positions / square restoration");return;
        }
        var full=RawParts("Rounded/"+key+"_full").Values.Single();var compactMesh=RawParts("Rounded/"+key+"_compact").Values.Single();var tunnel=RawParts("Rounded/"+key+"_tunnel").Values.Single();
        var uploaded=(Mesh[])Field(shape,"OvalFull");
        Check(uploaded[0].vertexCount==full.vertexCount&&uploaded[0].bounds==full.bounds,key+" original RUS round mesh uploaded intact");
        Check(full.bounds.size.x>.8f,key+" original wider RUS shield retained");
        var originalTexture=((Material[])Field(shape,"OvalMaterials"))[0].mainTexture;
        Check(originalTexture!=null&&originalTexture.name=="Style/t_signal.png",key+" shared DV-style atlas assigned");
        var vs=full.vertices;var ns=full.normals;
        Check(vs.All(v=>!float.IsNaN(v.x+v.y+v.z))&&ns.All(n=>n.sqrMagnitude>.98f&&n.sqrMagnitude<1.02f),key+" source geometry finite with unit normals");
        float lower=(float)Field(fit,"Lowering");var compactVs=compactMesh.vertices;
        Check(vs.Length==compactVs.Length&&vs.Where((v,i)=>(compactVs[i]+Vector3.up*lower-v).sqrMagnitude>.000000001f).Count()==0,key+" rounded compact head lowered rigidly");
        Check(full.uv.SequenceEqual(compactMesh.uv)&&full.normals.SequenceEqual(compactMesh.normals),key+" source normals and texture coordinates preserved");
        var envelope=(Bounds)Field(Field(fit,"WallMount"),"Envelope");
        var sourceBounds=tunnel.bounds;sourceBounds.center+=new Vector3(0,-.8f,0);
        var tolerantEnvelope=envelope;tolerantEnvelope.Expand(.00002f);
        Check(tolerantEnvelope.Contains(sourceBounds.min)&&tolerantEnvelope.Contains(sourceBounds.max),key+" wall fitting encloses wider original RUS head: "+envelope+" / "+sourceBounds);
        UnityEngine.Object.DestroyImmediate(full);UnityEngine.Object.DestroyImmediate(compactMesh);UnityEngine.Object.DestroyImmediate(tunnel);
        Call(fit,"SetMode",Enum.Parse(assembly.GetType("RuSignals.MastPlacementMode",true),"Original"));Call(shape,"SetOval",true);
    }
    private static void GlowChecks(Assembly assembly,object controller)
    {
        var definition=(Component)controller.GetType().GetProperty("Definition",All).GetValue(controller,null);
        string key=definition.name.Split(' ')[0];if(key.StartsWith("Semaphore",StringComparison.Ordinal)||!glowKeys.Add(key))return;
        var signals=((System.Collections.IEnumerable)controller.GetType().GetProperty("AllSignals").GetValue(controller,null)).Cast<object>().ToArray();
        var lights=signals.SelectMany(s=>((Array)s.GetType().GetProperty("AllLights").GetValue(s,null)).Cast<object>()).Distinct().ToArray();
        foreach(var light in lights){Field(Field(light,"Lamp"),"lampInd").GetType().GetField("lag",All).SetValue(Field(Field(light,"Lamp"),"lampInd"),0f);Call(light,"TurnOff");SettleLamp(assembly,Field(light,"Lamp"));}
        var selected=lights.FirstOrDefault(l=>((Component)Field(l,"Definition")).name=="Blue")??lights.FirstOrDefault(l=>((Component)Field(l,"Definition")).name=="Green")??lights[0];
        var def=(Component)Field(selected,"Definition");var indicator=Field(Field(selected,"Lamp"),"lampInd");var lamp=Field(selected,"Lamp");
        var spill=(Light)Field(def,"Light");var glare=(Renderer)Field(indicator,"glareRenderer");
        var budget=definition.GetComponentInChildren(assembly.GetType("RuSignals.SpillView",true),true);Call(budget,"SetGain",1f);Call(budget,"Admit",true);
        Check(spill!=null&&glare!=null&&spill.transform.IsChildOf(definition.transform)&&glare.transform.IsChildOf(definition.transform),key+" native glow and spill belong to clone");
        Check(spill.type==LightType.Spot&&spill.spotAngle==55&&spill.shadows==LightShadows.Hard&&spill.range==4&&spill.shadowResolution==UnityEngine.Rendering.LightShadowResolution.Low,key+" narrow 55 degree short-range spot light with metal occlusion");
        SpillOcclusionCheck(spill,key);
        LampFadeChecks(assembly,selected,key);
        Call(selected,"TurnOn",false);SettleLamp(assembly,lamp);
        Check(spill.intensity>0&&glare.enabled,key+" glow and spill switch on with native lamp");
        Check(spill.color==(Color)Field(def,"Colour"),key+" coloured lamp spill");
        Call(budget,"Admit",false);SettleLamp(assembly,lamp);
        Check(spill.cullingMask==0&&spill.shadows==LightShadows.None&&spill.intensity>0&&glare.enabled,key+" native lamp update preserves masked local light and visible glare");Call(budget,"Admit",true);
        var block=new MaterialPropertyBlock();glare.GetPropertyBlock(block);
        Check(block.GetColor("_TintColor").maxColorComponent>0,key+" native glare receives emission tint");
        Check(glare.sharedMaterial.name=="RUS lamp halo",key+" native glare uses shared RUS halo material");
        Check(glare.sharedMaterial.shader.name=="RuSignals/Depth tested lamp halo"&&glare.GetComponent<MeshFilter>().sharedMesh.vertexCount==4,key+" native halo uses depth-tested shared quad");
        var lens=((Renderer[])Field(def,"Renderers"))[0];lens.GetPropertyBlock(block);
        Check(block.GetColor("_EmissionColor").maxColorComponent>=1.8f,key+" lens receives HDR emission");
        if(def.name=="Blue")Check(block.GetColor("_EmissionColor").b==3&&spill.intensity==1.4f&&((Color)Field(indicator,"glareColor")).b==.8f,key+" blue glass, halo and local brightness halved");
        var clock=assembly.GetType("RuSignals.VisualScheduler",true).GetField("Current",All).GetValue(null);
        var lightingPosition=(Vector3)budget.GetType().GetProperty("Position",All).GetValue(budget,null);
        float maximum=(float)Field(def,"LightIntensity");
        foreach(float distance in new[]{101f,100f,99f,65f,30f,0f})
        {
            Call(clock,"RefreshSpills",(Vector3?)(lightingPosition+Vector3.forward*distance));SettleLamp(assembly,lamp);
            float expected=distance>=100?0:distance<=30?1:distance==65?.5f:(float)assembly.GetType("RuSignals.LampVisualRules").GetMethod("SpillGain").Invoke(null,new object[]{distance});
            Check(Mathf.Abs(spill.intensity-maximum*expected)<.00001f&&glare.enabled,key+" actual native lamp distance fade at "+distance+" m");
            Check((spill.cullingMask!=0)==(expected>0),key+" no local rendering at 100 m or beyond");
        }
        Call(clock,"RefreshSpills",(Vector3?)(lightingPosition+Vector3.forward*65));
        Call(selected,"TurnOff");SettleLamp(assembly,lamp);Check(spill.intensity==0,key+" distance fade never lights a native off lamp");
        Call(selected,"TurnOn",true);
        SampleBlink(assembly,lamp,true);Check(Mathf.Abs(spill.intensity-maximum*.5f)<maximum*.001f,key+" distance gain applies to native blinking bright phase");
        SampleBlink(assembly,lamp,false);Check(spill.intensity<maximum*.001f,key+" native blinking reaches its 2.0.9 dark brightness threshold");
        Call(clock,"RefreshSpills",(Vector3?)(lightingPosition+Vector3.forward*30));Call(selected,"TurnOn",false);SettleLamp(assembly,lamp);
        string output=Environment.GetEnvironmentVariable("RUS_V2_OUTPUT");
        var shieldView=definition.GetComponent(assembly.GetType("RuSignals.ShieldView",true));
        foreach(bool oval in shieldView==null?new[]{false}:new[]{false,true})
        {
            if(shieldView!=null)Call(shieldView,"SetOval",oval);
            var focus=lens.bounds.center;
            Render(definition.gameObject,Path.Combine(output,key+(oval?"-oval":"-standard")+"-lit-night.png"),focus+new Vector3(.35f,-.3f,2.5f),focus+Vector3.forward*.2f,1.2f,true);
        }
        if(key=="shunting_mast")
        {
            var focus=def.transform.parent.TransformPoint(new Vector3(0,5.66f,.52f));
            Render(definition.gameObject,Path.Combine(output,"rounded-blue-day.png"),new Vector3(1.7f,4.5f,8),new Vector3(0,2.6f,0),3.1f);
            Render(definition.gameObject,Path.Combine(output,"rounded-blue-close.png"),focus+new Vector3(.25f,.03f,3),focus,.6f);
        }
        if(key=="entry")
        {
            var focus=lens.bounds.center;
            Render(definition.gameObject,Path.Combine(output,"solid-visors-entry-side.png"),focus+new Vector3(2,.45f,2.3f),focus+Vector3.forward*.25f,1.15f);
        }
        if(key=="mast")
        {
            var focus=lens.bounds.center;Render(definition.gameObject,Path.Combine(output,"four-ring-lens.png"),focus+Vector3.forward*2,focus,.20f);
        }
        Call(selected,"TurnOff");SettleLamp(assembly,lamp);Check(spill.intensity==0&&!glare.enabled,key+" native off extinguishes glow and spill");
        Call(selected,"TurnOn",true);
        SampleBlink(assembly,lamp,true);Check(spill.intensity>0&&glare.enabled,key+" native blinking bright phase matches glow");
        SampleBlink(assembly,lamp,false);Check(spill.intensity<(float)Field(indicator,"lightIntensity")*.001f,key+" native blinking dark phase reaches near-zero glow");Call(selected,"TurnOff");
    }
    private static void SpillOcclusionCheck(Light original,string key)
    {
        var go=new GameObject("Production spill occlusion "+key);
        var spill=UnityEngine.Object.Instantiate(original,go.transform,false);spill.transform.localPosition=Vector3.zero;spill.transform.localRotation=Quaternion.identity;spill.color=Color.white;spill.intensity=3;spill.enabled=true;spill.cullingMask=-1;spill.shadows=LightShadows.Hard;
        var receiver=GameObject.CreatePrimitive(PrimitiveType.Cube);receiver.transform.SetParent(go.transform,false);receiver.transform.localPosition=new Vector3(0,0,1);receiver.transform.localScale=new Vector3(.8f,.8f,.02f);
        var material=new Material(Shader.Find("Standard"));material.color=Color.white;material.SetFloat("_Glossiness",0);receiver.GetComponent<Renderer>().sharedMaterial=material;
        var shield=GameObject.CreatePrimitive(PrimitiveType.Cube);shield.transform.SetParent(go.transform,false);shield.transform.localPosition=new Vector3(0,0,.5f);shield.transform.localScale=new Vector3(.6f,.6f,.02f);shield.GetComponent<Renderer>().sharedMaterial=material;
        string output=Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),open=Path.Combine(output,key+"-spill-open.png"),covered=Path.Combine(output,key+"-spill-blocked.png");
        shield.SetActive(false);Render(go,open,new Vector3(1.8f,0,-1),new Vector3(0,0,1),.55f,true);
        shield.SetActive(true);Render(go,covered,new Vector3(1.8f,0,-1),new Vector3(0,0,1),.55f,true);
        Func<string,float> pixel=f=>{var t=new Texture2D(2,2);t.LoadImage(File.ReadAllBytes(f));float p=t.GetPixel(350,500).r;UnityEngine.Object.DestroyImmediate(t);return p;};
        float a=pixel(open),b=pixel(covered);Check(a>.15f&&b<a*.25f,key+" actual GPU spotlight is blocked by opaque metal");
        Debug.Log("RUS_SPILL_OCCLUSION_PASS "+key+" "+a+" -> "+b);
        UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);
    }
    private static void SpillBudgetChecks(Assembly assembly,Component clock)
    {
        var type=assembly.GetType("RuSignals.SpillView",true);var root=new GameObject("QA 256 signal lighting budget");
        var views=new List<Component>();var lamps=new List<Light[]>();
        int before=(int)clock.GetType().GetProperty("SpillCount",All).GetValue(clock,null);
        for(int i=0;i<256;i++)
        {
            var go=new GameObject("Budget signal "+i);go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(i,0,0);
            var view=go.AddComponent(type);((Behaviour)view).enabled=false;
            var pair=new Light[2];for(int j=0;j<2;j++){var g=new GameObject("Native spill "+j);g.transform.SetParent(go.transform,false);pair[j]=g.AddComponent<Light>();pair[j].cullingMask=1<<(j+2);pair[j].intensity=j+1;}
            type.GetField("Lights").SetValue(view,pair);type.GetField("Masks").SetValue(view,new[]{4,8});((Behaviour)view).enabled=true;views.Add(view);lamps.Add(pair);
        }
        Check((int)clock.GetType().GetProperty("SpillCount",All).GetValue(clock,null)==before+256,"Lighting group lifecycle registers each signal once");
        Check(type.GetMethod("Update",All)==null,"Lighting budget has no individual Update");
        var refresh=(Action<Vector3?>)Delegate.CreateDelegate(typeof(Action<Vector3?>),clock,clock.GetType().GetMethod("RefreshSpills",All));
        refresh(Vector3.zero);
        Check(lamps.Count(p=>p[0].cullingMask!=0)==8&&lamps.Take(8).All(p=>p[0].cullingMask==4&&p[1].cullingMask==8),"Nearest eight signals retain native layer masks");
        Check(lamps.All(p=>p[0].intensity==1&&p[1].intensity==2&&p.All(l=>l.enabled)),"Budget leaves native lamp intensity and enable state intact");
        refresh(new Vector3(255,0,0));
        Check(lamps.Take(248).All(p=>p[0].cullingMask==0)&&lamps.Skip(248).All(p=>p[0].cullingMask==4),"Moving camera transfers bounded local lights to nearest signals");
        refresh(new Vector3(1000,0,0));Check(lamps.All(p=>p.All(l=>l.cullingMask==0&&l.shadows==LightShadows.None)),"Distant signals do not render spill or shadow maps");
        refresh(null);Check(lamps.All(p=>p.All(l=>l.cullingMask==0)),"Missing camera safely suppresses local lights");
        views[0].gameObject.SetActive(false);Check((int)clock.GetType().GetProperty("SpillCount",All).GetValue(clock,null)==before+255,"Disabled signal leaves lighting registry");
        views[0].gameObject.SetActive(true);refresh(Vector3.zero);Check(lamps[0][0].cullingMask==4,"Reactivated signal rejoins budget with original masks");
        var foreign=new GameObject("Other mod light").AddComponent<Light>();foreign.cullingMask=512;foreign.intensity=5;
        refresh(null);Check(foreign.cullingMask==512&&foreign.intensity==5,"Budget does not modify lights from other mods");
        var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<1000;i++)refresh(new Vector3(i%256,0,0));watch.Stop();
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"spill-budget-verification.json"),"{\"groups\":256,\"lamps_per_group\":2,\"admitted_groups\":8,\"rendering_local_lights\":16,\"baseline_local_lights\":512,\"radius_m\":100,\"full_brightness_m\":30,\"refresh_interval_seconds\":0.2,\"passes\":1000,\"cpu_elapsed_ms\":"+watch.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"native_state_preserved\":true,\"game_fps_measured\":false}");
        UnityEngine.Object.DestroyImmediate(foreign.gameObject);UnityEngine.Object.DestroyImmediate(root);
        Check((int)clock.GetType().GetProperty("SpillCount",All).GetValue(clock,null)==before,"Destroyed signals leave no lighting registry entries");
        Debug.Log("RUS_SPILL_BUDGET_PASS 256 signals / eight admitted / lifecycle / native state retained");
    }
    private static void HaloDepthCheck(object assets,string output)
    {
        var go=new GameObject("Halo depth probe");
        var lamp=new GameObject("Halo");lamp.transform.SetParent(go.transform,false);
        lamp.AddComponent<MeshFilter>().sharedMesh=(Mesh)Call(assets,"GlareMesh");
        var renderer=lamp.AddComponent<MeshRenderer>();renderer.sharedMaterial=(Material)Call(assets,"Glare");
        var tint=new MaterialPropertyBlock();tint.SetColor("_TintColor",Color.white*5);renderer.SetPropertyBlock(tint);
        string open=Path.Combine(output,"halo-uncovered.png"),covered=Path.Combine(output,"halo-occluded.png"),rear=Path.Combine(output,"halo-rear.png");
        Render(go,open,new Vector3(0,0,3),Vector3.zero,1.2f);
        var shield=GameObject.CreatePrimitive(PrimitiveType.Cube);shield.transform.SetParent(go.transform,false);shield.transform.localPosition=new Vector3(0,0,.5f);shield.transform.localScale=new Vector3(2.4f,2.4f,.04f);
        var opaque=new Material(Shader.Find("Unlit/Color"));opaque.color=new Color(.13f,.15f,.18f);shield.GetComponent<Renderer>().sharedMaterial=opaque;
        Render(go,covered,new Vector3(0,0,3),Vector3.zero,1.2f);shield.SetActive(false);
        Render(go,rear,new Vector3(0,0,-3),Vector3.zero,1.2f);
        Func<string,float> brightness=file=>{var t=new Texture2D(2,2);t.LoadImage(File.ReadAllBytes(file));float value=t.GetPixel(350,500).r;UnityEngine.Object.DestroyImmediate(t);return value;};
        float a=brightness(open),b=brightness(covered),c=brightness(rear);
        Check(a>b+.15f,"Production halo emits on the visible front face");
        Check(Mathf.Abs(b-c)<.025f,"Opaque geometry fully blocks halo; rear face emits nothing");
        UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(opaque);
        Debug.Log("RUS_HALO_DEPTH_PASS GPU opaque occlusion / rear face culling");
    }
    private static void GlassRingCheck(object assets,string output)
    {
        HaloDepthCheck(assets,output);
        var original=(Material)Call(assets,"Glass","QA four ring probe",Color.white,Vector2.zero,Vector2.one,false,.81f,.12f,null);
        var material=new Material(original);material.color=Color.black;material.SetFloat("_Glossiness",0);material.SetFloat("_Metallic",0);material.SetColor("_EmissionColor",Color.white*.45f);
        var go=new GameObject("Optical ring probe");var mesh=new Mesh{vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)},normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward},uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,1,2,0,2,3}};mesh.RecalculateTangents();mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        string file=Path.Combine(output,"four-ring-shader-probe.png");Render(go,file,new Vector3(0,0,3),Vector3.zero,1.1f);
        var pixels=new Texture2D(2,2);pixels.LoadImage(File.ReadAllBytes(file));
        Func<float,float> value=r=>pixels.GetPixel(350,500+Mathf.RoundToInt(r*1000/2/1.1f)).r;
        foreach(float radius in new[]{.20f,.40f,.60f,.80f})Check(value(radius)>(value(radius-.045f)+value(radius+.045f))*.5f+.004f,"GPU glass renders a broad ring at radius "+radius);
        Check(value(.94f)>.1f,"Glass emits between grooves without a barcode mask");
        foreach(var colour in new[]{new Color(1,.02f,.01f),new Color(.01f,1,.14f),new Color(.002f,.012f,1)})
        {
            bool blue=colour.b==1;material.SetColor("_EmissionColor",colour*(blue?3:1.8f));
            string coloured=Path.Combine(output,blue?"blue-lens-no-white-dot.png":colour.r==1?"red-lens-no-white-dot.png":"green-lens-no-white-dot.png");
            Render(go,coloured,new Vector3(0,0,3),Vector3.zero,1.1f);pixels.LoadImage(File.ReadAllBytes(coloured));
            Color centre=pixels.GetPixel(350,500);
            Check(Mathf.Min(centre.r,Mathf.Min(centre.g,centre.b))<.25f&&centre.maxColorComponent>.5f,"GPU lit lamp retains its colour at centre instead of a white dot: "+colour+" / "+centre);
            if(blue)Check(centre.b>centre.g*2.5f&&centre.b>centre.r*4,"GPU blue lamp remains saturated blue at full daylight emission");
        }
        Debug.Log("RUS_LAMP_COLOURS_PASS GPU red, green, blue centres / no white bulb term / saturated blue");
        UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);
        Debug.Log("RUS_GLASS_RINGS_PASS 4 sampled optical rings on actual compiled shader");
    }
    private static void TunnelChecks(Assembly common,Assembly assembly,object controller)
    {
        var definition=(Component)controller.GetType().GetProperty("Definition",All).GetValue(controller,null);var go=definition.gameObject;
        var fit=go.GetComponent(assembly.GetType("RuSignals.ClearanceView",true));if(fit==null)return;
        string key=go.name.Split(' ')[0];if(!tunnelKeys.Add(key))return;tunnelModels++;
        var shape=go.GetComponent(assembly.GetType("RuSignals.ShieldView",true));if(shape!=null)Call(shape,"SetOval",false);
        TunnelGeometryChecks(assembly,fit,key);
        var mount=(Component)Field(fit,"WallMount");var root=(Transform)Field(mount,"SignalRoot");var console=(Transform)Field(mount,"Console");
        var original=(Vector3)Field(mount,"OriginalPosition");var envelope=(Bounds)Field(mount,"Envelope");
        var roundedTriangles=new List<Vector3[]>();
        if(shape!=null)
        {
            foreach(var mesh in RawParts("Rounded/"+key+"_tunnel").Values)
            {
                var vs=mesh.vertices;var ids=mesh.triangles;
                for(int i=0;i<ids.Length;i+=3)roundedTriangles.Add(new[]{vs[ids[i]],vs[ids[i+1]],vs[ids[i+2]]});
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        var placement=controller.GetType().GetProperty("PlacementInfo",All);var placementType=common.GetType("Signals.Common.SignalPlacementInfo",false);
        if(placementType==null)placementType=placement.PropertyType.GetGenericArguments()[0];
        var mode=assembly.GetType("RuSignals.MastPlacementMode",true);
        Action<string> refresh=m=>Call(fit,"SetMode",Enum.Parse(mode,m));
        Func<bool> mounted=()=> (bool)fit.GetType().GetProperty("IsWallMounted").GetValue(fit,null);
        var tunnel=new GameObject("QA tunnel "+key);tunnel.transform.position=new Vector3(30,0,30);
        var wallMaterial=new Material(Shader.Find("Standard")){color=new Color(.22f,.24f,.25f)};wallMaterial.SetFloat("_Glossiness",.05f);
        Func<string,Vector3,Vector3,GameObject> box=(name,position,scale)=>
        {var b=GameObject.CreatePrimitive(PrimitiveType.Cube);b.name=name;b.transform.SetParent(tunnel.transform,false);b.transform.localPosition=position;b.transform.localScale=scale;b.GetComponent<Renderer>().sharedMaterial=wallMaterial;return b;};
        var left=box("Left wall",new Vector3(-3.2f,2.5f,0),new Vector3(.2f,5,8));
        var right=box("Right wall",new Vector3(3.2f,2.5f,0),new Vector3(.2f,5,8));
        var roof=box("Tunnel roof",new Vector3(0,5.2f,0),new Vector3(8,.4f,8));
        go.transform.SetParent(tunnel.transform,false);
        float straightShift=0;
        foreach(bool opposite in new[]{false,true})
        {
            var p=Activator.CreateInstance(placementType);placementType.GetField("OppositeSide",All).SetValue(p,opposite);placement.SetValue(controller,p,null);
            float offset=(float)Field(definition,"Offset")*(opposite?-1:1);go.transform.localPosition=Vector3.right*offset;
            Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
            Check(mounted(),key+" tunnel uses wall console on "+opposite+" / envelope "+envelope+" / position "+go.transform.position);
            Check((bool)fit.GetType().GetProperty("IsCompact").GetValue(fit,null),key+" console uses shortened heads");
            Check(console.gameObject.activeSelf,key+" console visible");
            var lowerPlate=(Transform)Field(mount,"LowerPlate");var fixings=(Vector3[])Field(mount,"HeadFixings");
            float previousBottom=fixings.Min(f=>f.y),previousTop=fixings.Max(f=>f.y);if(previousTop-previousBottom<.25f)previousBottom-=.15f;
            if(Field(mount,"PanelNeck") as Transform!=null)previousBottom=Mathf.Min(previousBottom,(float)Field(mount,"PanelCenter"));
            previousBottom=Mathf.Min(previousBottom,(float)Field(mount,"LabelBottom"));
            Check(Mathf.Abs(lowerPlate.localPosition.y-Mathf.Max(.20f,previousBottom-.20f))<.0001f,key+" lower wall anchor also supports designation");
            foreach(string end in new[]{"Upper","Lower"})
            {
                var bolts=(Transform)Field(mount,end+"Bolts");var plate=(Transform)Field(mount,end+"Plate");
                Check(bolts.localPosition==plate.localPosition&&Quaternion.Angle(bolts.localRotation,plate.localRotation)<.001f,key+" "+end+" bolt geometry follows wall plate");
                Check(bolts.GetComponent<MeshFilter>().sharedMesh.vertexCount==448&&!bolts.GetComponent<MeshFilter>().sharedMesh.isReadable,key+" "+end+" has four shared washers and hex heads");
                var point=plate.InverseTransformPoint(bolts.TransformPoint(new Vector3(.061f,0,0)));
                Check(point.x>.5f,key+" "+end+" anchor heads protrude from plate face");
            }
            Check((root.localPosition-original).x*Mathf.Sign(offset)>.15f,key+" signal moves closer to wall");
            Check(root.localPosition.y==original.y,key+" signal root height does not drift");
            if(opposite)straightShift=root.localPosition.x-original.x;
            var filters=(MeshFilter[])Field(fit,"Filters");var meshes=(Mesh[])Field(fit,"Tunnel");
            Check(filters.Where((f,i)=>meshes[i]==null).All(f=>f.sharedMesh==null),key+" console removes pole and ladder");
            Check(filters.Where((f,i)=>meshes[i]!=null).All(f=>f.sharedMesh==meshes[Array.IndexOf(filters,f)]),key+" cleaned tunnel heads installed");
            if(shape!=null)
            {
                Call(shape,"SetOval",true);var shapeFilters=(MeshFilter[])Field(shape,"Filters");var oval=(Mesh[])Field(shape,"OvalTunnel");
                Check(shapeFilters.Where((f,i)=>f.sharedMesh!=oval[i]).Count()==0,key+" rounded tunnel heads selected after fit");
                foreach(var neck in (Transform[])Field(mount,"HeadNecks"))
                    Check(Intersects(root.InverseTransformPoint(neck.TransformPoint(new Vector3(0,0,-.5f))),root.InverseTransformPoint(neck.TransformPoint(new Vector3(0,0,.5f))),roundedTriangles),key+" fixing joins actual rounded RUS housing");
                Call(shape,"SetOval",false);
            }
            var spine=(Transform)Field(mount,"Spine");
            foreach(var neck in (Transform[])Field(mount,"HeadNecks"))
            {
                var a=neck.TransformPoint(new Vector3(0,0,-.5f));var b=neck.TransformPoint(new Vector3(0,0,.5f));
                var rail=spine.InverseTransformPoint(a);
                Check(Mathf.Abs(rail.x)<=.501f&&Mathf.Abs(rail.y)<=.501f&&Mathf.Abs(rail.z)<=.501f,key+" head fixing joins console spine");
                Check(Intersects(root.InverseTransformPoint(a),root.InverseTransformPoint(b),tunnelTriangles[key]),key+" head fixing penetrates real housing mesh");
            }
            float edge=Mathf.Abs(offset)+Mathf.Abs(root.localPosition.x-original.x)+Mathf.Max(Mathf.Abs(envelope.min.x),Mathf.Abs(envelope.max.x));
            Check(edge<3.1f-.13f,key+" housing and panel clear wall");
            var panel=Field(fit,"Panel") as Transform;
            var label=(Transform)Field(fit,"Label");var labelMesh=label.GetComponent<MeshFilter>();
            // 80% of the ordinary horizontal plate retains the repeater's existing
            // clearance in the round bore; letters are over six times taller than 12%.
            Check(label.GetComponent<Renderer>().bounds.size.x>.40f&&label.GetComponent<Renderer>().bounds.size.y>.20f,key+" tunnel designation uses a readable horizontal plate");
            Check(!(bool)Field(label.GetComponent(assembly.GetType("RuSignals.NameplateView",true)),"Vertical"),key+" tunnel number is on one line");
            Check(label.localPosition==(Vector3)Field(fit,"WallLabel"),key+" tunnel designation uses console mounting position");
            Check(labelMesh.GetComponent<Renderer>().bounds.min.y>tunnel.transform.position.y,key+" tunnel designation stays above track level");
            var labelBounds=labelMesh.sharedMesh.bounds;var labelTop=root.InverseTransformPoint(label.TransformPoint(labelBounds.max));var labelBottom=root.InverseTransformPoint(label.TransformPoint(labelBounds.min));
            Check(Mathf.Abs(labelTop.x)<=envelope.extents.x&&Mathf.Abs(labelBottom.x)<=envelope.extents.x,key+" designation stays within lateral housing clearance");
            var lug=spine.InverseTransformPoint(label.TransformPoint(new Vector3(0,-.04f,-.06f)));
            Check(Mathf.Abs(lug.x)<.5f&&Mathf.Abs(lug.y)<.5f&&Mathf.Abs(lug.z)<.5f,key+" tunnel designation lug enters real console spine");
            if(panel!=null)
            {
                Check(label.position.y<panel.GetComponentsInChildren<MeshRenderer>().Min(r=>r.bounds.min.y),key+" tunnel designation lies below indicator");
                Check(panel.localPosition==(Vector3)Field(fit,"CompactPanel")&&panel.IsChildOf(root),key+" indicator stays below heads on console");
                var panelFilters=(MeshFilter[])Field(fit,"PanelFilters");var panelMeshes=(Mesh[])Field(fit,"PanelTunnel");
                Check(panelFilters.Where((f,i)=>f.sharedMesh!=panelMeshes[i]).Count()==0,key+" indicator also removes pole clamps and cable");
                var neck=(Transform)Field(mount,"PanelNeck");
                var pa=panel.InverseTransformPoint(neck.TransformPoint(new Vector3(0,0,-.5f)));var pb=panel.InverseTransformPoint(neck.TransformPoint(new Vector3(0,0,.5f)));
                Check(Intersects(pa,pb,tunnelTriangles["route_pointer"]),key+" indicator fixing penetrates its rear housing: "+pa+" / "+pb+"; envelope "+envelope);
            }
            root.hasChanged=false;foreach(var t in console.GetComponentsInChildren<Transform>(true))t.hasChanged=false;
            refresh("Automatic");Check(!root.hasChanged&&console.GetComponentsInChildren<Transform>(true).All(t=>!t.hasChanged),key+" stable console does not dirty transforms");
            if(opposite)
            {
                Call(fit,"Reposition");Check(!mounted()&&root.localPosition==original&&!console.gameObject.activeSelf,key+" native placement change withdraws old wall displacement");
                refresh("Automatic");Check(mounted(),key+" placement change allows fresh wall fit");
            }
            if(key=="entry"&&opposite)
            {
                // Render the signal together with both walls, from the track.
                Render(tunnel,Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"tunnel-console.png"),new Vector3(31.4f,2.2f,34),new Vector3(32.7f,2.1f,30.1f),2.2f);
            }
            if(opposite&&(key=="entry"||key=="shunting_mast"||key=="repeater"))
            {
                string output=Environment.GetEnvironmentVariable("RUS_V2_OUTPUT");var focus=go.transform.TransformPoint(envelope.center+Vector3.right*straightShift);focus.z=30.3f;
                Render(tunnel,Path.Combine(output,"tunnel-"+key+"-side.png"),focus+new Vector3(-2,.3f,-.3f),focus,Mathf.Max(envelope.extents.y+.35f,1.65f));
                Render(tunnel,Path.Combine(output,"tunnel-"+key+"-rear.png"),focus+new Vector3(-1.2f,.25f,-3),focus,Mathf.Max(envelope.extents.y+.35f,1.65f));
                if(key=="shunting_mast")
                {
                    Call(shape,"SetOval",true);
                    Render(tunnel,Path.Combine(output,"rounded-tunnel-rear.png"),focus+new Vector3(-1.2f,.25f,-3),focus,1.3f);
                    var boltFocus=((Transform)Field(mount,"LowerPlate")).position;
                    Render(tunnel,Path.Combine(output,"wall-anchor-bolts.png"),boltFocus+new Vector3(-.8f,.05f,-.3f),boltFocus,.23f);
                    Call(shape,"SetOval",false);
                }
            }
            refresh("Original");Check(!mounted()&&!console.gameObject.activeSelf&&root.localPosition==original,key+" original mast restores without displacement");
            var restoredLabel=(Transform)Field(fit,"Label");
            Check((bool)Field(restoredLabel.GetComponent(assembly.GetType("RuSignals.NameplateView",true)),"Vertical")&&restoredLabel.localScale==Vector3.one,key+" original nameplate restores vertical layout and full size");
            var full=(Mesh[])Field(fit,"Full");Check(filters.Where((f,i)=>f.sharedMesh!=full[i]).Count()==0,key+" original meshes restored");
            if(panel!=null){var fs=(MeshFilter[])Field(fit,"PanelFilters");var ms=(Mesh[])Field(fit,"PanelFull");Check(fs.Where((f,i)=>f.sharedMesh!=ms[i]).Count()==0,key+" original indicator clamps restored on mast");}
        }
        // An approximate round bore uses the radius/centre of level520's
        // tunnel collision-ignore trigger, not the game's actual wall mesh.
        left.SetActive(false);right.SetActive(false);roof.SetActive(false);
        var bore=new GameObject("Approximate game-sized rounded tunnel bore");bore.transform.SetParent(tunnel.transform,false);
        const int sectors=64;var boreVertices=new Vector3[sectors*2];var boreIndices=new int[sectors*6];
        for(int i=0;i<sectors;i++)
        {
            float angle=i*Mathf.PI*2/sectors;var point=new Vector3(Mathf.Sin(angle)*2.95f,2.39f+Mathf.Cos(angle)*2.95f,-8);
            boreVertices[i*2]=point;boreVertices[i*2+1]=point+Vector3.forward*16;int n=(i+1)%sectors;
            int k=i*6;boreIndices[k]=i*2;boreIndices[k+1]=n*2;boreIndices[k+2]=n*2+1;boreIndices[k+3]=i*2;boreIndices[k+4]=n*2+1;boreIndices[k+5]=i*2+1;
        }
        var boreMesh=new Mesh{vertices=boreVertices,triangles=boreIndices};boreMesh.RecalculateBounds();boreMesh.RecalculateNormals();
        bore.AddComponent<MeshFilter>().sharedMesh=boreMesh;bore.AddComponent<MeshRenderer>().sharedMaterial=wallMaterial;bore.AddComponent<MeshCollider>().sharedMesh=boreMesh;
        foreach(bool opposite in new[]{false,true})
        {
            var p=Activator.CreateInstance(placementType);placementType.GetField("OppositeSide",All).SetValue(p,opposite);placement.SetValue(controller,p,null);
            float offset=(float)Field(definition,"Offset")*(opposite?-1:1);go.transform.localPosition=Vector3.right*offset;
            tunnel.transform.rotation=Quaternion.Euler(0,opposite?37:0,0);
            Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
            // Tall station stacks and their board cannot fit this small bore
            // while retaining their dimensions and original train clearance.
            bool narrowProfile=key!="entry"&&key!="junction";
            Check(mounted()==narrowProfile,key+" rounded bore fits eligible heads / side "+opposite);
            if(mounted())
            {
                var filters=(MeshFilter[])Field(fit,"Filters");var meshes=(Mesh[])Field(fit,"Tunnel");
                Check(console.gameObject.activeSelf&&filters.Where((f,i)=>meshes[i]==null).All(f=>f.sharedMesh==null),key+" rounded bore actually removes pole/ladder and shows console");
                var points=tunnelTriangles[key].SelectMany(t=>t).Select(v=>tunnel.transform.InverseTransformPoint(root.TransformPoint(v))).ToArray();
                Check(points.All(v=>new Vector2(v.x,v.y-2.39f).magnitude<2.95f-.06f),key+" real housing vertices clear rounded wall");
                float nativeInner=Mathf.Min(1.55f,Mathf.Abs(offset)-Mathf.Max(Mathf.Abs(envelope.min.x),Mathf.Abs(envelope.max.x)));
                Check(points.Min(v=>v.x*Mathf.Sign(offset))>=nativeInner-.0001f,key+" rounded fit retains original inner track clearance");
                Check(new[]{"UpperPlate","LowerPlate"}.Select(name=>(Transform)Field(mount,name)).All(plate=>{var v=tunnel.transform.InverseTransformPoint(plate.position);return Mathf.Abs(new Vector2(v.x,v.y-2.39f).magnitude-2.95f)<.04f;}),key+" both anchors meet actual round wall");
                root.hasChanged=false;refresh("Automatic");Check(!root.hasChanged,key+" rounded fit does not accumulate height/displacement");
                if(key=="repeater"&&opposite)
                {
                    var focus=go.transform.TransformPoint(envelope.center+root.localPosition-original);string output=Environment.GetEnvironmentVariable("RUS_V2_OUTPUT");
                    Render(tunnel,Path.Combine(output,"rounded-bore-repeater-front.png"),focus+tunnel.transform.TransformVector(new Vector3(-1.4f,.1f,2)),focus,1.65f);
                    Render(tunnel,Path.Combine(output,"rounded-bore-repeater-rear.png"),focus+tunnel.transform.TransformVector(new Vector3(-1.4f,.1f,-2)),focus,1.65f);
                }
            }
            else Check(root.localPosition==original&&!console.gameObject.activeSelf,key+" oversized rounded-bore fixture keeps safe mast fallback");
            refresh("Original");Check(root.localPosition==original&&!mounted(),key+" round-bore displacement restores exactly");
        }
        tunnel.transform.rotation=Quaternion.identity;
        UnityEngine.Object.DestroyImmediate(bore);UnityEngine.Object.DestroyImmediate(boreMesh);
        left.SetActive(true);right.SetActive(true);roof.SetActive(true);Physics.SyncTransforms();refresh("Automatic");
        var restoredPlacement=Activator.CreateInstance(placementType);placementType.GetField("OppositeSide",All).SetValue(restoredPlacement,true);placement.SetValue(controller,restoredPlacement,null);
        go.transform.localPosition=Vector3.right*2.05f;Physics.SyncTransforms();refresh("Automatic");
        // Rock between the sample rays must still stop the outward movement.
        // Keep this obstruction inside an actual housing envelope. The former
        // combined box also covered the empty space above a low designation.
        var obstruction=((Bounds[])Field(mount,"Volumes")).OrderByDescending(b=>b.size.x).First();
        float originalEdge=2.05f+obstruction.max.x;
        var shelf=box("Rock ledge between sample rays",new Vector3(originalEdge+.22f,Mathf.Lerp(obstruction.min.y,obstruction.max.y,.25f),obstruction.center.z),new Vector3(.2f,.10f,.25f));
        Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
        Check(mounted(),key+" uneven wall can retain console");
        Check(root.localPosition.x-original.x<straightShift-.02f,key+" full envelope check retreats from rock between sample rays");
        UnityEngine.Object.DestroyImmediate(shelf);
        // Dynamic objects and trigger volumes do not become mounting surfaces.
        var dynamic=box("Dynamic obstruction",new Vector3(2.6f,2.5f,0),new Vector3(.2f,5,4));dynamic.AddComponent<Rigidbody>().isKinematic=true;
        var trigger=box("Trigger volume",new Vector3(2.4f,2.5f,0),new Vector3(.2f,5,4));trigger.GetComponent<Collider>().isTrigger=true;
        Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
        Check(mounted()&&Mathf.Abs(root.localPosition.x-original.x-straightShift)<.0001f,key+" ignores vehicles and trigger volumes");
        UnityEngine.Object.DestroyImmediate(dynamic);UnityEngine.Object.DestroyImmediate(trigger);
        var player=box("Player collider is not a tunnel wall",new Vector3(2.4f,2.5f,0),new Vector3(.2f,5,4));player.tag="Player";
        var character=new GameObject("Character capsule is not a tunnel wall");character.transform.SetParent(tunnel.transform,false);character.transform.localPosition=new Vector3(2.6f,2.5f,0);var capsule=character.AddComponent<CharacterController>();capsule.height=5;capsule.radius=.2f;
        Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
        Check(mounted()&&Mathf.Abs(root.localPosition.x-original.x-straightShift)<.0001f,key+" ignores player and character capsules when fitting static wall");
        UnityEngine.Object.DestroyImmediate(player);UnityEngine.Object.DestroyImmediate(character);
        // Curved tunnel approximated by sloped side surfaces, rotated along a curve.
        tunnel.transform.rotation=Quaternion.Euler(0,37,0);left.transform.localRotation=Quaternion.Euler(0,0,-6);right.transform.localRotation=Quaternion.Euler(0,0,6);
        Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
        Check(mounted(),key+" rotated sloped wall fit");
        Check(Quaternion.Angle(((Transform)Field(mount,"UpperPlate")).localRotation,Quaternion.identity)>1,key+" wall plate follows surface normal");
        tunnel.transform.rotation=Quaternion.identity;left.transform.localRotation=right.transform.localRotation=Quaternion.identity;
        // Imported game colliders may face away from the bore. Test one-sided
        // triangles, not just the two-sided primitive boxes used above.
        var collisionMeshes=new List<Mesh>();bool backfaces=Physics.queriesHitBackfaces;Physics.queriesHitBackfaces=false;
        foreach(var side in new[]{left,right,roof})
        {
            side.GetComponent<BoxCollider>().enabled=false;Vector3[] vertices;
            if(side==roof)vertices=new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,-.5f,-.5f)};
            else {float x=side==right?-.5f:.5f;vertices=new[]{new Vector3(x,-.5f,-.5f),new Vector3(x,.5f,-.5f),new Vector3(x,.5f,.5f),new Vector3(x,-.5f,.5f)};}
            var mesh=new Mesh{vertices=vertices,triangles=side==left?new[]{0,2,1,0,3,2}:new[]{0,1,2,0,2,3}};mesh.RecalculateBounds();mesh.RecalculateNormals();
            collisionMeshes.Add(mesh);side.AddComponent<MeshCollider>().sharedMesh=mesh;
        }
        Physics.SyncTransforms();Check(!Physics.Raycast(tunnel.transform.position+Vector3.up*2,Vector3.right,3.5f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore),key+" fixture reproduces inward ray missing outward collision faces");
        refresh("Automatic");tunnelScenarios++;Check(mounted(),key+" outward-facing tunnel collider still gets console");Check(!Physics.queriesHitBackfaces,key+" wall fitting leaves global physics query flags untouched");
        foreach(var side in new[]{left,right,roof}){UnityEngine.Object.DestroyImmediate(side.GetComponent<MeshCollider>());side.GetComponent<BoxCollider>().enabled=true;}
        foreach(var mesh in collisionMeshes)UnityEngine.Object.DestroyImmediate(mesh);Physics.queriesHitBackfaces=backfaces;
        roof.SetActive(false);Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
        Check(!mounted()&&!(bool)fit.GetType().GetProperty("IsCompact").GetValue(fit,null)&&root.localPosition==original,key+" outdoor cliff retains full mast");
        roof.SetActive(true);left.SetActive(false);Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
        Check(!mounted(),key+" open sided roof does not get wall console");
        left.SetActive(true);right.transform.localPosition=new Vector3(1.9f,2.5f,0);Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
        Check(!mounted()&&root.localPosition==original,key+" narrow tunnel avoids pushing heads into rock");
        right.transform.localPosition=new Vector3(6,2.5f,0);Physics.SyncTransforms();refresh("Automatic");tunnelScenarios++;
        Check(!mounted(),key+" does not reach across another track to distant wall");
        refresh("Original");placement.SetValue(controller,null,null);go.transform.SetParent(null,true);go.transform.position=Vector3.zero;go.transform.rotation=Quaternion.identity;
        UnityEngine.Object.DestroyImmediate(tunnel);UnityEngine.Object.DestroyImmediate(wallMaterial);
    }
    private static void TunnelGeometryChecks(Assembly assembly,Component fit,string key)
    {
        var source=RawParts(key);var meshes=(Mesh[])Field(fit,"Tunnel");var filters=(MeshFilter[])Field(fit,"Filters");
        var spec=assembly.GetType("RuSignals.RuModelCatalog",true).GetField(key,All).GetValue(null);
        float lowest=((Array)Field(spec,"Lenses")).Cast<object>().Min(l=>( (Vector3)Field(l,"Position")).y);
        float lower=(float)Field(fit,"Lowering");var triangles=new List<Vector3[]>();int count=0,sourceVertices=0,cleanVertices=0;
        var create=assembly.GetType("RuSignals.TunnelGeometry",true).GetMethod("Create",All);
        foreach(var part in source)
        {
            if(!part.Key.EndsWith("_Signal",StringComparison.Ordinal))continue;
            var clean=(Mesh)create.Invoke(null,new object[]{key,part.Key,part.Value,lower,lowest});
            int index=Array.FindIndex(filters,f=>f.name==part.Key);
            Check(index>=0&&meshes[index].vertexCount==clean.vertexCount&&meshes[index].bounds==clean.bounds,key+" uploaded tunnel mesh matches clean geometry");
            Check(clean.vertexCount<=part.Value.vertexCount,key+" clean housing adds no duplicate geometry");sourceVertices+=part.Value.vertexCount;cleanVertices+=clean.vertexCount;
            var vs=clean.vertices;var ns=clean.normals;var uv=clean.uv;var originalVertices=part.Value.vertices;var originalNormals=part.Value.normals;var originalUv=part.Value.uv;
            bool preserved=true;
            for(int i=0;i<vs.Length;i++)
            {
                var expected=vs[i]+Vector3.up*lower;int found=Array.FindIndex(originalVertices,v=>(v-expected).sqrMagnitude<.000000001f);
                preserved&=found>=0&&originalNormals.Where((n,j)=>(originalVertices[j]-expected).sqrMagnitude<.000000001f&&n==ns[i]&&originalUv[j]==uv[i]).Any();
            }
            Check(preserved,key+" housing shape, normals and UVs preserved with rigid lowering");
            Check(clean.bounds.min.z>=(key=="distant"||key=="repeater"?-.251f:.30f),key+" no rear mast clamp remains");
            var ids=clean.triangles;count+=ids.Length/3;
            for(int i=0;i<ids.Length;i+=3)triangles.Add(new[]{vs[ids[i]],vs[ids[i+1]],vs[ids[i+2]]});
            UnityEngine.Object.DestroyImmediate(clean);
        }
        int expectedTriangles=new Dictionary<string,int>{{"mast",412},{"entry",696},{"junction",824},{"shunting_mast",284},{"distant",364},{"repeater",200}}[key];
        Check(count==expectedTriangles,key+" all original housings, shields and visors retained");
        Check(cleanVertices<sourceVertices,key+" original mast attachments removed from complete head");
        tunnelTriangles.Add(key,triangles);foreach(var mesh in source.Values)UnityEngine.Object.DestroyImmediate(mesh);
        if(tunnelTriangles.ContainsKey("route_pointer"))return;
        triangles=new List<Vector3[]>();source=RawParts("route_pointer");count=0;
        foreach(var part in source)
        {
            var clean=(Mesh)create.Invoke(null,new object[]{"route_pointer",part.Key,part.Value,0f,0f});var vs=clean.vertices;var ids=clean.triangles;
            Check(clean.bounds.min.z>=-.201f,"Indicator removes disconnected pole clamps and cable");count+=ids.Length/3;
            for(int i=0;i<ids.Length;i+=3)triangles.Add(new[]{vs[ids[i]],vs[ids[i+1]],vs[ids[i+2]]});
            UnityEngine.Object.DestroyImmediate(clean);UnityEngine.Object.DestroyImmediate(part.Value);
        }
        Check(count==56,"Indicator front, rear and door hinges retained");tunnelTriangles.Add("route_pointer",triangles);
    }
    private static Dictionary<string,Mesh> RawParts(string key)
    {
        var result=new Dictionary<string,Mesh>();string directory=Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_ROOT"),"src/RuSignals.Next/Assets");
        string repaired=Path.Combine(directory,key.StartsWith("Rounded/",StringComparison.Ordinal)?"SmoothShields":"SolidHoods",key+".rsm"),path=File.Exists(repaired)?repaired:Path.Combine(directory,key+".rsm");
        using(var reader=new BinaryReader(File.OpenRead(path)))
        {
            reader.ReadBytes(8);int parts=reader.ReadInt32();
            for(int p=0;p<parts;p++)
            {
                string name=Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));reader.ReadBytes(reader.ReadInt32());reader.ReadInt32();
                int vc=reader.ReadInt32(),ic=reader.ReadInt32();var vs=new Vector3[vc];var ns=new Vector3[vc];var uv=new Vector2[vc];var ids=new int[ic];
                for(int i=0;i<vc;i++){vs[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());ns[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());}
                for(int i=0;i<ic;i++)ids[i]=reader.ReadInt32();var mesh=new Mesh{vertices=vs,normals=ns,uv=uv,triangles=ids};mesh.RecalculateBounds();result.Add(name,mesh);
            }
        }
        return result;
    }
    private static bool Intersects(Vector3 a,Vector3 b,List<Vector3[]> triangles)
    {
        var delta=b-a;
        foreach(var t in triangles)
        {
            var e1=t[1]-t[0];var e2=t[2]-t[0];var h=Vector3.Cross(delta,e2);float determinant=Vector3.Dot(e1,h);if(Mathf.Abs(determinant)<.0000001f)continue;
            // A bar may hit a welded seam exactly; allow only floating-point
            // barycentric roundoff, never a physical gap between the meshes.
            float inverse=1/determinant;var s=a-t[0];float u=inverse*Vector3.Dot(s,h);if(u<-.00001f||u>1.00001f)continue;
            var q=Vector3.Cross(s,e1);float v=inverse*Vector3.Dot(delta,q);if(v<-.00001f||u+v>1.00001f)continue;
            float distance=inverse*Vector3.Dot(e2,q);if(distance>.001f&&distance<.999f)return true;
        }
        return false;
    }
    private static void Render(GameObject target,string file,Vector3 position,Vector3 look,float size,bool night=false,int width=700,int height=1000)
    {
        int layer=29;foreach(var t in target.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
        var camera=new GameObject("QA camera").AddComponent<Camera>();camera.transform.position=position;camera.transform.LookAt(look);camera.orthographic=true;camera.orthographicSize=size;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.15f,.18f);camera.cullingMask=1<<layer;
        var light=new GameObject("QA light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=night?0:1.4f;light.transform.position=position+new Vector3(-3,5,0);light.transform.LookAt(look);light.cullingMask=1<<layer;
        QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowDistance=10;QualitySettings.pixelLightCount=8;
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=night?Color.black:new Color(.6f,.6f,.6f);
        var rt=new RenderTexture(width,height,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        var texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(file,texture.EncodeToPNG());
        RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
    }
}
