using System;
using System.Collections.Generic;
using Signals.Common;
using Signals.Common.Aspects;
using Signals.Common.Displays;
using UnityEngine;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
internal sealed partial class PackFactory
{
    private readonly SignalAssets assets;
    private readonly Transform holder;
    private readonly SignalReservingObjectDefinition phone;
    private readonly SignalNameDisplayDefinition nameHud;
    internal PackFactory(SignalAssets library,Transform parent,SignalReservingObjectDefinition telephone,SignalNameDisplayDefinition originalNameHud)
    {assets=library;holder=parent;phone=telephone;nameHud=originalNameHud;}
    internal static SignalNameDisplayDefinition OriginalNameHud(SignalPack original)
    {
        foreach(var controller in original.AllControllers)
            foreach(var display in controller.GetComponentsInChildren<SignalNameDisplayDefinition>(true))
                if(display.HUDSprite!=null)return display;
        throw new InvalidOperationException("DVSignals name HUD template unavailable");
    }
    internal SignalPack Create()
    {
        var pack=assets.Own(ScriptableObject.CreateInstance<SignalPack>());pack.name=pack.ModName="RuSignals";pack.ModId=Mod.Id;pack.Author="Herodrine_";pack.Version=pack.ExporterVersion=Mod.Version;
        var through=Electric(RuModelCatalog.mast,SignalRole.Through);var route=Electric(RuModelCatalog.junction,SignalRole.Route);
        var shunting=Electric(RuModelCatalog.shunting,SignalRole.Shunting);var mast=Electric(RuModelCatalog.shunting_mast,SignalRole.Shunting);
        pack.Signal=through;pack.DivergingSignal=pack.LeftJunctionSignal=pack.RightJunctionSignal=route;
        pack.EntrySignal=Electric(RuModelCatalog.entry,SignalRole.Entry);
        pack.ExitSignal=Electric(RuModelCatalog.exit_small_two_yellows,SignalRole.Exit);
        pack.ExitPassengerSignal=Electric(RuModelCatalog.entry,SignalRole.Exit);pack.ExitMainlineSignal=Electric(RuModelCatalog.junction,SignalRole.Exit);
        pack.ShuntingSignal=pack.SpacingSignal=shunting;pack.MajorShuntingSignal=pack.JunctionShuntingSignal=mast;
        pack.DistantSignal=Electric(RuModelCatalog.distant,SignalRole.Distant);pack.RepeaterSignal=Electric(RuModelCatalog.repeater,SignalRole.Repeater);
        pack.CombinedSignal=pack.CombinedLeftJunctionSignal=pack.CombinedRightJunctionSignal=route;
        pack.TurntableSignal=Electric(RuModelCatalog.turntable,SignalRole.Turntable);pack.BufferStopSignal=Electric(RuModelCatalog.turntable,SignalRole.BufferStop);pack.BufferStopSignalIsStatic=true;
        pack.DistantSignalDistance=300;pack.RepeaterSignalDistance=100;pack.EntryFormat="{3}";pack.ExitFormat="{3} {4}";pack.MainlineFormat="{0}";
        Semaphores(pack);return pack;
    }
    private static GameObject Child(Transform parent,string name){var go=new GameObject(name);go.transform.SetParent(parent,false);return go;}
    private SignalControllerDefinition Controller(string name,float offset)
    {
        var controller=Child(holder,name).AddComponent<SignalControllerDefinition>();controller.Offset=offset;controller.FlipPrefabWhenInOppositeSide=false;return controller;
    }
    private void Telephone(SignalControllerDefinition controller,SignalDefinition definition,SignalRole role)
    {
        if(role!=SignalRole.Entry&&role!=SignalRole.Exit)return;
        var copy=UnityEngine.Object.Instantiate(phone,controller.transform,false);copy.gameObject.SetActive(true);copy.name="PhoneReserver";
        definition.PhysicalReserver=copy;var trackside=copy.GetComponent<TracksideObject>();if(trackside==null)throw new InvalidOperationException("Native telephone missing placement");controller.TracksideObjects=new[]{trackside};
    }
    private SignalControllerDefinition Electric(ModelSpec model,SignalRole role)
    {
        bool dwarf=model.InstallationDepth<=.5f;
        float offset=dwarf?(role==SignalRole.Exit?1.85f:1.25f):2.05f;
        var controller=Controller(model.Key+" / "+role,-offset);var root=Child(controller.transform,"Signal");root.transform.localPosition=Vector3.down*model.InstallationDepth;
        var definition=root.AddComponent<SignalDefinition>();definition.SynchroniseLamps=false;definition.SelfActsAsDistant=role<=SignalRole.Route;
        var parts=assets.Model(model.Key);var renderers=new Dictionary<string,MeshRenderer>();
        foreach(var part in parts)renderers.Add(part.Name,assets.PutPart(root.transform,part));
        var lamps=new Dictionary<string,SignalLightDefinition>();var bounds=new Bounds(model.Lenses[0].Position,Vector3.zero);
        float lowest=float.PositiveInfinity,top=float.NegativeInfinity;
        foreach(var part in parts)top=Mathf.Max(top,part.Full.bounds.max.y);
        foreach(var lens in model.Lenses)
        {
            var renderer=renderers[lens.Name];var part=Array.Find(parts,p=>p.Name==lens.Name);Color colour=LampPalette.Get(lens.Name);
            renderer.sharedMaterial=assets.Glass(model.Key+" "+lens.Name,colour,part.UvMin,part.UvMax,false,.68f,.05f);
            var light=renderer.gameObject.AddComponent<SignalLightDefinition>();light.Renderers=new Renderer[]{renderer};light.Colour=colour;light.LightIntensity=LampVisualRules.Intensity(lens.Name=="Blue");
            // One bounded time-based fade drives glass, halo and spill. Native
            // smoothing stays off so the output reaches both exact endpoints.
            light.Binary=false;light.Lag=0;
            light.Glare=Child(root.transform,lens.Name+" glare").transform;light.Glare.localPosition=lens.Position+new Vector3(0,0,.01f);
            light.Light=Child(light.Glare,"Lamp spill").AddComponent<Light>();light.Light.type=LightType.Spot;light.Light.color=colour;light.Light.range=LampVisualRules.Range;light.Light.spotAngle=LampVisualRules.SpotAngle;
            // The metal hood must occlude spill onto the neighbouring heads.
            // Small hard shadow maps suffice for this short-range light.
            light.Light.shadows=LightShadows.Hard;light.Light.shadowResolution=UnityEngine.Rendering.LightShadowResolution.Low;
            light.Light.shadowBias=.005f;light.Light.shadowNormalBias=0;light.Light.shadowNearPlane=.01f;
            light.Light.intensity=0;light.Light.renderMode=LightRenderMode.ForcePixel;
            lamps.Add(lens.Name,light);
            bounds.Encapsulate(lens.Position);lowest=Mathf.Min(lowest,lens.Position.y);
        }
        var hover=root.AddComponent<BoxCollider>();bounds.Expand(new Vector3(.36f,.36f,.50f));hover.center=bounds.center;hover.size=bounds.size;hover.isTrigger=true;
        var spill=root.AddComponent<SpillView>();spill.Lights=new Light[model.Lenses.Length];spill.Masks=new int[spill.Lights.Length];spill.Indicators=new IndicatorEmission[spill.Lights.Length];spill.Intensities=new float[spill.Lights.Length];
        for(int i=0;i<spill.Lights.Length;i++){var lamp=lamps[model.Lenses[i].Name];spill.Lights[i]=lamp.Light;spill.Masks[i]=spill.Lights[i].cullingMask;spill.Intensities[i]=lamp.LightIntensity;}
        Transform panel=null;
        if(RouteBoardRules.HasHousing(role,model.InstallationDepth<.5f))panel=Board(definition,lowest,role);
        definition.Aspects=Aspects(root.transform,role,lamps);definition.OffStateHUDSprite=assets.Icon(Indication.Dark);
        if(dwarf&&role==SignalRole.Exit)foreach(RuAspectDefinition aspect in definition.Aspects)aspect.AutoShunting=true;
        float plateTop=dwarf?.49f:Mathf.Min(model.InstallationDepth+3.3f,lowest-.85f);
        if(panel!=null)foreach(var part in assets.Model("route_pointer"))plateTop=Mathf.Min(plateTop,panel.localPosition.y+part.Full.bounds.min.y-.25f);
        // The repeater's pole is behind its heads; lens centres are not a mounting datum.
        float postFront=model.Key=="repeater"||model.Key=="distant"?-.3736f:model.Key=="entry"||model.Key=="junction"?.1427f:dwarf?(model.Key=="exit_small_two_yellows"?.1338f:.154f):.1264f;
        float labelScale=dwarf?.65f:1;
        var label=Nameplate(definition,new Vector3(dwarf?bounds.center.x:0,plateTop,postFront+.024f*labelScale),dwarf);
        if(role==SignalRole.Shunting)controller.ShuntingSignals=new[]{definition};else controller.Signals=new[]{definition};
        Telephone(controller,definition,role);
        if(model.InstallationDepth>.5f)
        {
            var view=controller.gameObject.AddComponent<ClearanceView>();int count=parts.Length;
            view.Filters=new MeshFilter[count];view.Full=new Mesh[count];view.Compact=new Mesh[count];view.Tunnel=new Mesh[count];
            bool first=true;var heads=new Bounds();var volumes=new List<Bounds>();
            for(int i=0;i<count;i++)
            {
                view.Filters[i]=renderers[parts[i].Name].GetComponent<MeshFilter>();view.Full[i]=parts[i].Full;view.Compact[i]=parts[i].Compact;
                if(!TunnelMountRules.IsHeadPart(parts[i].Name))continue;
                view.Tunnel[i]=parts[i].Tunnel;var b=parts[i].Tunnel.bounds;b.center+=root.transform.localPosition;
                if(parts[i].OvalTunnel!=null){var oval=parts[i].OvalTunnel.bounds;oval.center+=root.transform.localPosition;b.Encapsulate(oval);}
                foreach(var section in parts[i].TunnelSections){var volume=section;volume.center+=root.transform.localPosition;volumes.Add(volume);}
                if(parts[i].OvalTunnelSections!=null)foreach(var section in parts[i].OvalTunnelSections){var volume=section;volume.center+=root.transform.localPosition;volumes.Add(volume);}
                if(first){heads=b;first=false;}else heads.Encapsulate(b);
            }
            view.Glares=new Transform[model.Lenses.Length];view.GlarePositions=new Vector3[view.Glares.Length];
            for(int i=0;i<view.Glares.Length;i++){view.Glares[i]=lamps[model.Lenses[i].Name].Glare;view.GlarePositions[i]=view.Glares[i].localPosition;}
            view.Hover=hover;view.HoverCenter=hover.center;view.Depth=model.InstallationDepth;view.Top=top;view.HeadBottom=lowest-.55f;
            view.Lowering=ClearanceRules.Lowering(top,lowest,model.InstallationDepth,panel!=null);
            view.Label=label;view.NormalLabel=label.localPosition;
            view.Panel=panel;if(panel!=null){view.NormalPanel=panel.localPosition;view.CompactPanel=new Vector3(0,RouteBoardRules.OffsetY(lowest,view.Lowering,RouteBoardRules.CompactScale),RouteBoardRules.OffsetZ(RouteBoardRules.CompactScale));}
            float labelTop=Mathf.Min(plateTop-view.Lowering,lowest-view.Lowering-.7f);
            if(panel!=null)foreach(var part in assets.Model("route_pointer"))labelTop=Mathf.Min(labelTop,view.CompactPanel.y+part.Tunnel.bounds.min.y*RouteBoardRules.CompactScale-.10f);
            view.CompactLabel=new Vector3(label.localPosition.x,Mathf.Max(model.InstallationDepth+.32f,labelTop),postFront+.024f*ClearanceRules.LabelScale);
            var labelSize=new Vector3(.56f,.30f,.08f)*ClearanceRules.LabelScale;
            var labelCentre=new Vector3(0,-.135f,-.02f)*ClearanceRules.LabelScale;
            var envelope=heads;envelope.Encapsulate(new Bounds(view.CompactLabel+root.transform.localPosition+labelCentre,labelSize));
            if(panel!=null)
            {
                var boardParts=assets.Model("route_pointer");view.PanelFilters=new MeshFilter[boardParts.Length];view.PanelFull=new Mesh[boardParts.Length];view.PanelTunnel=new Mesh[boardParts.Length];
                for(int i=0;i<boardParts.Length;i++)
                {
                    var part=boardParts[i];view.PanelFilters[i]=panel.Find(part.Name).GetComponent<MeshFilter>();view.PanelFull[i]=part.Full;view.PanelTunnel[i]=part.Tunnel;
                    var b=part.Tunnel.bounds;b.center=b.center*RouteBoardRules.CompactScale+view.CompactPanel+root.transform.localPosition;b.size*=RouteBoardRules.CompactScale;envelope.Encapsulate(b);
                    foreach(var section in part.TunnelSections){var volume=section;volume.center=volume.center*RouteBoardRules.CompactScale+view.CompactPanel+root.transform.localPosition;volume.size*=RouteBoardRules.CompactScale;volumes.Add(volume);}
                }
            }
            var mount=controller.gameObject.AddComponent<TunnelMount>();view.WallMount=mount;
            view.WallLabel=view.CompactLabel;view.WallLabel.z=envelope.min.z-.10f+TunnelMountRules.TubeThickness*.5f+.024f*ClearanceRules.LabelScale;
            volumes.Add(new Bounds(view.WallLabel+root.transform.localPosition+labelCentre,labelSize));
            // Glass envelopes fully inside their housing add no information
            // to the fit. Prune them once, keeping physics work bounded.
            for(int i=volumes.Count-1;i>=0;i--)
                for(int j=0;j<volumes.Count;j++)
                    if(j!=i&&volumes[j].Contains(volumes[i].min)&&volumes[j].Contains(volumes[i].max)&&
                       (volumes[j]!=volumes[i]||j<i)){volumes.RemoveAt(i);break;}
            mount.Volumes=volumes.ToArray();
            mount.LabelBottom=view.WallLabel.y+root.transform.localPosition.y-.27f*ClearanceRules.LabelScale;
            mount.SignalRoot=root.transform;mount.OriginalPosition=root.transform.localPosition;mount.Heads=heads;mount.Envelope=envelope;
            mount.Console=Child(controller.transform,"Tunnel wall console").transform;mount.Console.gameObject.SetActive(false);
            mount.Spine=assets.ConsolePart(mount.Console,"Head and indicator spine");mount.UpperArm=assets.ConsolePart(mount.Console,"Upper wall arm");mount.LowerArm=assets.ConsolePart(mount.Console,"Lower wall arm");mount.Brace=assets.ConsolePart(mount.Console,"Diagonal brace");
            mount.LowerBrace=assets.ConsolePart(mount.Console,"Lower arm brace");
            mount.UpperPlate=assets.ConsolePart(mount.Console,"Upper wall plate");mount.LowerPlate=assets.ConsolePart(mount.Console,"Lower wall plate");
            mount.UpperBolts=assets.ConsoleBolts(mount.Console,"Upper four anchor bolts");mount.LowerBolts=assets.ConsoleBolts(mount.Console,"Lower four anchor bolts");
            mount.HeadNecks=new Transform[model.Lenses.Length];mount.HeadFixings=new Vector3[model.Lenses.Length];
            for(int i=0;i<model.Lenses.Length;i++)
            {
                var lens=model.Lenses[i];mount.HeadNecks[i]=assets.ConsolePart(mount.Console,lens.Name+" housing fixing");
                mount.HeadFixings[i]=lens.Position+root.transform.localPosition-Vector3.up*view.Lowering-Vector3.forward*.20f;
            }
            if(panel!=null)
            {
                mount.PanelNeck=assets.ConsolePart(mount.Console,"Indicator fixing");mount.PanelCenter=view.CompactPanel.y+root.transform.localPosition.y+RouteBoardRules.MountY*RouteBoardRules.CompactScale;
                mount.PanelRear=view.CompactPanel.z-.15f*RouteBoardRules.CompactScale+.025f;
            }
        }
        if(RoundedGeometry.Available(model.Key))
        {
            var heads=Array.FindAll(parts,p=>p.OvalFull!=null);var shape=controller.gameObject.AddComponent<ShieldView>();shape.Placement=controller.GetComponent<ClearanceView>();
            int n=heads.Length;shape.Filters=new MeshFilter[n];shape.Renderers=new Renderer[n];shape.Full=new Mesh[n];shape.Compact=new Mesh[n];shape.Tunnel=new Mesh[n];shape.OvalFull=new Mesh[n];shape.OvalCompact=new Mesh[n];shape.OvalTunnel=new Mesh[n];shape.OriginalMaterials=new Material[n];shape.OvalMaterials=new Material[n];
            var rounded=assets.Model(dwarf?"RoundedDwarf/"+model.Key:"Rounded/"+model.Key+"_full");
            for(int i=0;i<n;i++){var part=heads[i];shape.Filters[i]=renderers[part.Name].GetComponent<MeshFilter>();shape.Renderers[i]=renderers[part.Name];shape.Full[i]=part.Full;shape.Compact[i]=part.Compact??part.Full;shape.Tunnel[i]=part.Tunnel??part.Full;shape.OvalFull[i]=part.OvalFull;shape.OvalCompact[i]=part.OvalCompact;shape.OvalTunnel[i]=part.OvalTunnel;shape.OriginalMaterials[i]=renderers[part.Name].sharedMaterial;shape.OvalMaterials[i]=part.Name.StartsWith("Body_",StringComparison.Ordinal)?rounded[0].Material:shape.OriginalMaterials[i];}
            if(dwarf)
            {
                shape.Glares=new Transform[model.Lenses.Length];shape.SquareGlares=new Vector3[shape.Glares.Length];shape.OvalGlares=new Vector3[shape.Glares.Length];
                for(int i=0;i<shape.Glares.Length;i++)
                {
                    string name=model.Lenses[i].Name;shape.Glares[i]=lamps[name].Glare;shape.SquareGlares[i]=shape.Glares[i].localPosition;
                    shape.OvalGlares[i]=Array.Find(rounded,p=>p.Name==name).LensCentre+Vector3.forward*.03f;
                    bounds.Encapsulate(shape.OvalGlares[i]);
                }
                hover.center=bounds.center;hover.size=bounds.size;
                shape.Label=label;shape.SquareLabel=label.localPosition;shape.OvalLabel=new Vector3(label.localPosition.x,label.localPosition.y,.16213f+.024f*labelScale);
            }
        }
        return controller;
    }
    private Transform Nameplate(SignalDefinition definition,Vector3 position,bool small=false)
    {
        var plate=assets.Nameplate(definition.transform,!small);plate.transform.localPosition=position;
        if(small)plate.transform.localScale=Vector3.one*.65f;
        var display=plate.gameObject.AddComponent<NameplateDefinition>();display.Plate=plate;
        display.Mode=DisplayBaseDefinition.UpdateMode.Always;display.DisableWhenSignalIsOff=false;
        display.HUDSprite=nameHud.HUDSprite;display.HUDTextColour=nameHud.HUDTextColour;display.HUDDisplayOrder=nameHud.HUDDisplayOrder;
        var displays=definition.Displays??Array.Empty<DisplayBaseDefinition>();
        Array.Resize(ref displays,displays.Length+1);displays[displays.Length-1]=display;definition.Displays=displays;
        return plate.transform;
    }
    private Transform Board(SignalDefinition definition,float lowest,SignalRole role)
    {
        var board=Child(definition.transform,"RUS route indicator");board.transform.localPosition=new Vector3(0,RouteBoardRules.OffsetY(lowest,0,1),RouteBoardRules.OffsetZ(1));assets.PutModel(board.transform,"route_pointer");
        var dots=Child(board.transform,"42 lamps / shared mesh");var filter=dots.AddComponent<MeshFilter>();filter.sharedMesh=assets.PanelMesh(0,false);
        var renderer=dots.AddComponent<MeshRenderer>();renderer.sharedMaterials=new[]{assets.PanelOff,assets.PanelOn};
        var panel=board.AddComponent<RoutePanel>();panel.Surface=filter;
        var display=board.AddComponent<RouteDisplayDefinition>();display.Role=role;display.Visual=board;display.Panel=panel;
        display.Mode=DisplayBaseDefinition.UpdateMode.Always;display.DisableWhenSignalIsOff=false;display.HUDTextColour=Color.white;display.HUDSprite=assets.RouteTextIcon();
        definition.Displays=new DisplayBaseDefinition[]{display};return board.transform;
    }
    private AspectBaseDefinition[] Aspects(Transform parent,SignalRole role,Dictionary<string,SignalLightDefinition> lamps)
    {
        var indications=SignalCatalogue.Profile(role);var result=new AspectBaseDefinition[indications.Length];
        for(int i=0;i<indications.Length;i++)
        {
            var value=indications[i];var d=Child(parent,value.ToString()).AddComponent<RuAspectDefinition>();result[i]=d;
            d.Role=role;d.Indication=value;d.Id=SignalRules.AspectId(value);d.ManualOnly=value==Indication.Invitation;
            d.DisallowPassing=value==Indication.Stop;d.RequireAcknowledging=value==Indication.Invitation;d.PassingSpeed=SignalRules.PassingSpeed(value);d.UsePassingSpeed=d.PassingSpeed>0;
            var on=new List<SignalLightDefinition>();var flash=new List<SignalLightDefinition>();
            var pattern=SignalCatalogue.Pattern(role,value);
            foreach(var lens in SignalCatalogue.Lenses)
            {if((pattern.Steady&lens)!=0)on.Add(lamps[lens.ToString()]);if((pattern.Blinking&lens)!=0)flash.Add(lamps[lens.ToString()]);}
            d.OnLights=on.ToArray();d.BlinkingLights=flash.ToArray();d.HUDSprite=assets.Icon(value,role==SignalRole.Shunting);
        }
        return result;
    }
}
