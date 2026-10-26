using System;
using System.Collections.Generic;
using System.IO;
using Signals.Common;
using SovietSemaphore;
using UnityEngine;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
internal sealed partial class PackFactory
{
    private Material metal,red,green,yellow,dark;
    private SemaphorePart[] semaphoreParts;
    internal void ReadSemaphores(string path)
    {
        semaphoreParts=SemaphoreManifest.Read(File.ReadAllText(Path.Combine(path,"Semaphore/semaphore_manifest.json")));
        metal=assets.Material("Semaphore enamel",Color.white);metal.mainTexture=assets.Texture("Style/semaphore_albedo.png");
        metal.SetTexture("_MetallicGlossMap",assets.Texture("Semaphore/semaphore_metallic_smoothness.png",true));metal.SetFloat("_GlossMapScale",1);metal.EnableKeyword("_METALLICGLOSSMAP");
        metal.SetTexture("_BumpMap",assets.Texture("Semaphore/semaphore_normal_unity.png",true));metal.SetFloat("_BumpScale",.18f);metal.EnableKeyword("_NORMALMAP");
        red=assets.Glass("Semaphore red",new Color(1,.012f,.004f),Vector2.zero,Vector2.one,true,tint:new Color(1,.012f,.004f)*.4f);
        green=assets.Glass("Semaphore green",new Color(.006f,1,.11f),Vector2.zero,Vector2.one,true,tint:new Color(.006f,1,.11f)*.4f);
        yellow=assets.Glass("Semaphore yellow",new Color(1,.42f,.006f),Vector2.zero,Vector2.one,true,tint:new Color(1,.42f,.006f)*.4f);
        dark=assets.Glass("Semaphore unlit",new Color(.25f,.32f,.38f),Vector2.zero,Vector2.one,tint:new Color(.25f,.32f,.38f)*.4f);
    }
    private void Semaphores(SignalPack pack)
    {
        pack.EnableOldVersions=true;
        pack.OldSignal=Mechanical(SignalRole.Through,false);var route=Mechanical(SignalRole.Route,true);
        pack.OldDivergingSignal=pack.OldLeftJunctionSignal=pack.OldRightJunctionSignal=route;
        pack.OldEntrySignal=Mechanical(SignalRole.Entry,true);var exit=Mechanical(SignalRole.Exit,true);
        pack.OldExitSignal=pack.OldExitPassengerSignal=pack.OldExitMainlineSignal=exit;
        var shunting=Mechanical(SignalRole.Shunting,false);
        pack.OldShuntingSignal=pack.OldMajorShuntingSignal=pack.OldJunctionShuntingSignal=pack.OldSpacingSignal=shunting;
        pack.OldTurntableSignal=Mechanical(SignalRole.Turntable,false);pack.OldBufferStopSignal=pack.BufferStopSignal;pack.OldBufferStopSignalIsStatic=true;
        pack.OldDistantSignal=pack.DistantSignal;pack.OldRepeaterSignal=pack.RepeaterSignal;
    }
    internal SemaphoreVisual Mechanism(Transform parent,bool two)
    {
        var root=Child(parent,two?"Soviet semaphore / two arms":"Soviet semaphore / single arm");
        var nodes=new Dictionary<string,Transform>{{"SovietSemaphore",root.transform}};var renderers=new Dictionary<string,Renderer>();
        foreach(var item in semaphoreParts)
        {
            if(!two&&item.key.IndexOf("lower",StringComparison.OrdinalIgnoreCase)>=0)continue;
            if(item.key.EndsWith("selector",StringComparison.OrdinalIgnoreCase))continue;
            if(!nodes.TryGetValue(item.parent,out var pivot)){pivot=Child(root.transform,item.parent).transform;pivot.localPosition=new Vector3(item.pivot[0],item.pivot[1],item.pivot[2]);nodes.Add(item.parent,pivot);}
            var part=assets.Model("Semaphore/"+item.key)[0];
            Material material=item.mesh=="UpperRedLens"?red:item.mesh=="UpperGreenLens"?green:item.mesh=="LowerYellowLens"?yellow:metal;
            var renderer=assets.PutPart(pivot,part,material,item.mesh);renderers.Add(item.mesh,renderer);nodes.Add(item.mesh,renderer.transform);
            if(item.mesh=="UpperRedLens"||item.mesh=="LowerYellowLens")
            {string name=item.mesh=="UpperRedLens"?"UpperGlassUnlit":"LowerGlassUnlit";renderers.Add(name,assets.PutPart(pivot,part,dark,name));}
        }
        Transform Node(string key)=>nodes.TryGetValue(key,out var node)?node:null;
        Renderer Lens(string key)=>renderers.TryGetValue(key,out var lens)?lens:null;
        var view=root.AddComponent<SemaphoreVisual>();view.TwoArms=two;
        view.Upper=Node("UpperArmPivot");view.Lower=Node("LowerArmPivot");view.Lever=Node("DriveLeverPivot");view.UpperSelector=Node("UpperSelectorPivot");view.LowerSelector=Node("LowerSelectorPivot");view.UpperRod=Node("UpperPullRod");view.LowerRod=Node("LowerPullRod");
        view.Red=Lens("UpperRedLens");view.Green=Lens("UpperGreenLens");view.Yellow=Lens("LowerYellowLens");view.UpperDark=Lens("UpperGlassUnlit");view.LowerDark=Lens("LowerGlassUnlit");
        Transmission(view,root.transform,two);view.SetState(SemaphoreState.Stop,true);return view;
    }
    private SignalControllerDefinition Mechanical(SignalRole role,bool two)
    {
        var controller=Controller("Semaphore / "+role,-2.15f);var root=Child(controller.transform,"Signal");root.transform.localPosition=Vector3.down*.80f;
        var definition=root.AddComponent<SignalDefinition>();definition.SynchroniseLamps=false;definition.SelfActsAsDistant=false;
        Mechanism(root.transform,two);var lamps=new Dictionary<string,SignalLightDefinition>();
        foreach(string name in new[]{"Yellow","Green","Red","Yellow2","MoonWhite","Blue"})
        {
            var go=Child(root.transform,"API "+name);var lamp=go.AddComponent<SignalLightDefinition>();lamp.Colour=LampPalette.Get(name);lamp.Binary=true;lamp.Lag=0;
            var invisible=go.AddComponent<MeshRenderer>();invisible.enabled=false;lamp.Renderers=new Renderer[]{invisible};lamp.LightIntensity=0;
            lamps.Add(name,lamp);
        }
        definition.Aspects=Aspects(root.transform,role,lamps);definition.OffStateHUDSprite=assets.Icon(Indication.Dark);
        foreach(RuAspectDefinition aspect in definition.Aspects){aspect.Mechanical=true;aspect.HUDSprite=assets.MechanicalIcon(role,aspect.Indication,two);}
        if(role==SignalRole.Shunting)controller.ShuntingSignals=new[]{definition};else controller.Signals=new[]{definition};
        var hover=root.AddComponent<BoxCollider>();hover.isTrigger=true;hover.center=new Vector3(-.6f,7.6f,0);hover.size=new Vector3(2.8f,3.2f,1);
        Nameplate(definition,new Vector3(0,4.1f,.257f));Telephone(controller,definition,role);return controller;
    }
}
