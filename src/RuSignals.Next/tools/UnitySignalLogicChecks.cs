using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void SampleBlink(Assembly assembly,object lamp,bool bright)
    {
        var indicator=Field(lamp,"lampInd");float value=0;
        for(int frame=0;frame<20000;frame++)
        {
            // Invoke the patched native Update until its actual brightness
            // reaches the requested end of the SmoothDamp blink phase.
            Call(lamp,"Update");value=(float)indicator.GetType().GetProperty("EmissionValue").GetValue(indicator,null);
            if(bright?value>.999f:value<.0009999871f)break;
        }
        Check(bright?value>.999f:value<.0009999871f,"Native 2.0.9 blinking reaches its "+(bright?"bright":"dark")+" threshold");
    }
    private static void SettleLamp(Assembly assembly,object lamp)
    {
        var sample=assembly.GetType("RuSignals.LampTiming",true).GetMethod("Sample",All);double start=Time.time;
        for(int frame=0;frame<=15;frame++)if((bool)sample.Invoke(null,new[]{lamp,(object)(start+frame/60d)}))Call(lamp,"Update");
    }
    private static void LampFadeChecks(Assembly assembly,object light,string key)
    {
        var lamp=Field(light,"Lamp");var indicator=Field(lamp,"lampInd");
        var sample=assembly.GetType("RuSignals.LampTiming",true).GetMethod("Sample",All);
        Action<double> tick=t=>{if((bool)sample.Invoke(null,new[]{lamp,(object)t}))throw new Exception(key+" visible lamp missed fade registry");};
        Func<float> level=()=> (float)indicator.GetType().GetProperty("EmissionValue").GetValue(indicator,null);
        var definition=(Component)Field(light,"Definition");var glass=((Renderer[])Field(definition,"Renderers"))[0];
        var glare=(Renderer)Field(indicator,"glareRenderer");var spill=(Light)Field(definition,"Light");var block=new MaterialPropertyBlock();
        Check(!(bool)Field(indicator,"binary")&&(float)Field(indicator,"lag")==0,key+" visible lens accepts fractional brightness without a second smoothing filter");
        Call(light,"TurnOff");SettleLamp(assembly,lamp);
        Call(light,"TurnOn",false);tick(11);Check(level()==0,key+" turn-on begins at previous off brightness");
        tick(11.09);Check(Mathf.Abs(level()-.5f)<.00001f,key+" native lamp is halfway through a smooth turn-on");
        glass.GetPropertyBlock(block);Check(Mathf.Abs(block.GetColor("_EmissionColor").maxColorComponent-((Color)Field(indicator,"emissionColor")).maxColorComponent*.5f)<.00001f,key+" actual glass follows fractional brightness");
        glare.GetPropertyBlock(block);Check(glare.enabled&&Mathf.Abs(block.GetColor("_TintColor").maxColorComponent-((Color)Field(indicator,"glareColor")).maxColorComponent*.5f)<.00001f,key+" actual halo fades with the glass");
        Check(Mathf.Abs(spill.intensity-(float)Field(indicator,"lightIntensity")*.5f)<.00001f,key+" local illumination fades with the glass");
        if(key=="mast")
        {
            var focus=glass.bounds.center;
            Render(definition.transform.parent.gameObject,System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"lamp-fade-half.png"),focus+new Vector3(.35f,-.3f,2.5f),focus,.45f,true);
        }
        tick(11.2);Check(level()==1,key+" turn-on reaches exact full brightness");
        Call(light,"TurnOff");tick(11.2);Check(level()==1,key+" turn-off begins continuously from current brightness");
        tick(11.29);Check(Mathf.Abs(level()-.5f)<.00001f,key+" turn-off fades through intermediate brightness");
        tick(11.29);Check(Mathf.Abs(level()-.5f)<.00001f,key+" paused clock does not advance fading");
        Call(light,"TurnOn",false);tick(11.29);Check(Mathf.Abs(level()-.5f)<.00001f,key+" rapid reversal has no brightness jump");
        tick(11.49);Check(level()==1,key+" reversed fade finishes at full brightness");
        Call(light,"TurnOff");tick(11.5);tick(11.7);Check(level()==0&&!glare.enabled&&spill.intensity==0,key+" fade finishes fully dark without residual glow");
        NativeBlinkChecks(assembly,light,key,tick,level);
        Call(light,"TurnOff");SettleLamp(assembly,lamp);
        Debug.Log("RUS_LAMP_FADE_PASS "+key+" / steady 0.18 s / glass, halo and spill / exact endpoints / interruption / pause");
    }
    private static void NativeBlinkChecks(Assembly assembly,object light,string key,Action<double> tick,Func<float> level)
    {
        var lamp=Field(light,"Lamp");var indicator=Field(lamp,"lampInd");var definition=(Component)Field(light,"Definition");
        var glass=((Renderer[])Field(definition,"Renderers"))[0];var halo=(Renderer)Field(indicator,"glareRenderer");var spill=(Light)Field(definition,"Light");
        var reference=GameObject.CreatePrimitive(PrimitiveType.Quad);reference.name="Unregistered native 2.0.9 blink reference";reference.SetActive(false);
        var nativeIndicator=reference.AddComponent(indicator.GetType());
        nativeIndicator.GetType().GetField("renderers",All).SetValue(nativeIndicator,new Renderer[]{reference.GetComponent<Renderer>()});
        nativeIndicator.GetType().GetField("binary",All).SetValue(nativeIndicator,false);nativeIndicator.GetType().GetField("lag",All).SetValue(nativeIndicator,.15f);
        var nativeLamp=reference.AddComponent(lamp.GetType());nativeLamp.GetType().GetField("lampInd",All).SetValue(nativeLamp,nativeIndicator);
        reference.SetActive(true);
        nativeLamp.GetType().GetField("blinkState",All).SetValue(nativeLamp,Field(lamp,"blinkState"));
        Call(nativeLamp,"SetLampState",Enum.Parse(Field(lamp,"lampState").GetType(),"Blinking"),false);
        Call(light,"TurnOn",true);
        Check((bool)assembly.GetType("RuSignals.LampTiming",true).GetMethod("Sample",All).Invoke(null,new[]{lamp,(object)20d}),key+" blinking delegates to original LampControl.Update");
        Check((float)Field(indicator,"lag")==.15f&&!(bool)Field(indicator,"binary"),key+" native blinking has exact 2.0.9 smoothing parameters");
        Check(Time.deltaTime>0&&Time.deltaTime<.1f,key+" native comparison uses a positive simulation frame interval");
        float error=0,glassError=0,haloError=0,spillError=0,minimum=1,maximum=0;int intermediate=0,transitions=0;bool previous=false;
        var block=new MaterialPropertyBlock();var rows=new System.Text.StringBuilder();
        for(int frame=0;frame<480;frame++)
        {
            Call(lamp,"Update");Call(nativeLamp,"Update");float actual=level();
            float expected=(float)nativeIndicator.GetType().GetProperty("EmissionValue").GetValue(nativeIndicator,null);
            error=Mathf.Max(error,Mathf.Abs(actual-expected));minimum=Mathf.Min(minimum,actual);maximum=Mathf.Max(maximum,actual);
            if(actual>.05f&&actual<.95f)intermediate++;bool bright=actual>.5f;if(bright!=previous)transitions++;previous=bright;
            glass.GetPropertyBlock(block);glassError=Mathf.Max(glassError,Mathf.Abs(block.GetColor("_EmissionColor").maxColorComponent-((Color)Field(indicator,"emissionColor")).maxColorComponent*actual));
            halo.GetPropertyBlock(block);haloError=Mathf.Max(haloError,Mathf.Abs(block.GetColor("_TintColor").maxColorComponent-((Color)Field(indicator,"glareColor")).maxColorComponent*actual));
            spillError=Mathf.Max(spillError,Mathf.Abs(spill.intensity-(float)Field(indicator,"lightIntensity")*actual));
            if(key=="mast")rows.Append(frame).Append(',').Append((frame*Time.deltaTime).ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(actual.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(expected.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        }
        Check(error==0,key+" every brightness sample matches the unmodified native 2.0.9 blink reference");
        Check(minimum<.001f&&maximum>.999f&&transitions>=4,key+" native blinking repeats through both brightness thresholds");
        Check(intermediate>40,key+" native blinking contains gradual rising and falling brightness");
        Check(glassError<.00001f&&haloError<.00001f&&spillError<.00001f,key+" glass, halo and spill follow the native blink waveform");
        if(key=="mast")System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"native-blink-waveform.csv"),"frame,time_seconds,current,reference_2_0_9\n"+rows);
        float before=level();Call(light,"TurnOn",false);tick(30);Check(level()==before&&(float)Field(indicator,"lag")==0,key+" blinking to steady preserves current brightness without double smoothing");
        tick(30.2);Check(level()==1,key+" native blink returns to an exact steady endpoint");
        Call(light,"TurnOn",true);bool native=(bool)assembly.GetType("RuSignals.LampTiming",true).GetMethod("Sample",All).Invoke(null,new[]{lamp,(object)30.2});Check(native&&level()==1,key+" steady to native blink starts continuously");
        Call(light,"TurnOff");tick(30.2);Check(level()==1,key+" off begins continuously after native blink");tick(30.4);Check(level()==0,key+" off after native blink leaves no residual glow");
        UnityEngine.Object.DestroyImmediate(reference);
        Debug.Log("RUS_NATIVE_BLINK_PASS "+key+" / 2.0.9 lag 0.15 / 480 exact native reference samples / glass, halo, spill / continuous mode changes");
    }
    private static void MechanicalLampTimingChecks(Assembly assembly,object signal)
    {
        var lights=((Array)signal.GetType().GetProperty("AllLights").GetValue(signal,null)).Cast<object>().ToArray();
        var green=lights.Single(light=>((Component)Field(light,"Definition")).name=="API Green");
        var white=lights.Single(light=>((Component)Field(light,"Definition")).name=="API MoonWhite");
        var sample=assembly.GetType("RuSignals.LampTiming",true).GetMethod("Sample",All);
        Action<object,double> tick=(light,time)=>sample.Invoke(null,new[]{Field(light,"Lamp"),(object)time});
        Func<object,float> level=light=>(float)Field(Field(light,"Lamp"),"lampInd").GetType().GetProperty("EmissionValue").GetValue(Field(Field(light,"Lamp"),"lampInd"),null);
        Call(green,"TurnOff");tick(green,12);Check(level(green)==0,"Hidden semaphore API lamp switches off immediately");
        Call(green,"TurnOn",false);tick(green,12.01);Check(level(green)==1,"Hidden semaphore API lamp switches on immediately");
        Call(white,"TurnOff");tick(white,12);Check(level(white)==0,"Retained hidden white API lamp switches off immediately");
        Call(white,"TurnOn",false);tick(white,12.01);Check(level(white)==1,"Retained hidden white API lamp accepts native on state");
        Call(white,"TurnOff");tick(white,12.02);Check(level(white)==0,"Retained hidden white API lamp returns to off state");
        var definition=(Component)Field(white,"Definition");
        Check((Transform)Field(definition,"Glare")==null&&(float)Field(definition,"LightIntensity")==0,"Removed physical auxiliary lamp has no glare or light source");
        Check(((Renderer[])Field(definition,"Renderers")).All(r=>r.GetComponent<MeshFilter>()==null)&&definition.GetComponentsInChildren<MeshFilter>(true).Length==0,"Retained API renderer has no visible geometry or auxiliary lens");
        Debug.Log("RUS_SEMAPHORE_API_LAMP_PASS native on/off retained / no auxiliary geometry or light");
    }
    private static void ApplyLamps(object signal)
    {
        var current=signal.GetType().GetProperty("CurrentAspect").GetValue(signal,null);
        if(current==null)return;
        var assembly=current.GetType().Assembly;
        var routine=(System.Collections.IEnumerator)current.GetType().BaseType.GetMethod("ApplyRoutine",All).Invoke(current,new object[]{0f});
        int yields=0;while(routine.MoveNext())if(++yields>3)throw new Exception("Lamp application stalled");
        foreach(var light in (Array)signal.GetType().GetProperty("AllLights").GetValue(signal,null))
            SettleLamp(assembly,Field(light,"Lamp"));
    }
    private static void SingleYellowWarningChecks(Assembly assembly,object signal,string slot)
    {
        ApplyLamps(signal);
        var lights=((Array)signal.GetType().GetProperty("AllLights").GetValue(signal,null)).Cast<object>().ToArray();
        var yellow=lights.Single(light=>((Component)Field(light,"Definition")).name=="Yellow");
        foreach(bool on in new[]{true,false})
        {
            SampleBlink(assembly,Field(yellow,"Lamp"),on);
            Check((bool)yellow.GetType().GetProperty("IsActive").GetValue(yellow,null)==on,slot+" single yellow has a physical bright/dark phase");
            Check(lights.Where(light=>!ReferenceEquals(light,yellow)).All(light=>!(bool)light.GetType().GetProperty("IsActive").GetValue(light,null)),slot+" advance warning lights no green or second yellow");
        }
    }
    private static void UpperYellowChecks(Assembly assembly,object signal,string slot)
    {
        var definition=(Component)signal.GetType().GetProperty("Definition").GetValue(signal,null);
        Check(!(bool)Field(definition,"SynchroniseLamps"),slot+" aspect switching does not wait for all previous lamps to go dark");
        var lights=((Array)signal.GetType().GetProperty("AllLights").GetValue(signal,null)).Cast<object>().ToArray();
        var upper=lights.Single(light=>((Component)Field(light,"Definition")).name=="Yellow");
        var lower=lights.Single(light=>((Component)Field(light,"Definition")).name=="Yellow2");
        Check(((Transform)Field(Field(upper,"Definition"),"Glare")).position.y>((Transform)Field(Field(lower,"Definition"),"Glare")).position.y,slot+" blinking Yellow is physically above steady Yellow2");
        Check(lights.All(light=>{float lag=(float)Field(Field(Field(light,"Lamp"),"lampInd"),"lag");return lag==0||lag==.15f;}),slot+" visible lamps use either steady fade or native 2.0.9 smoothing");
        var shape=definition.GetComponentInParent(assembly.GetType("RuSignals.ShieldView",true));
        foreach(bool oval in shape==null?new[]{false}:new[]{false,true})
        {
            if(shape!=null)Call(shape,"SetOval",oval);
            ApplyLamps(signal);
            foreach(int phase in new[]{0,1,2,3,4,5})
            {
                bool on=phase%2==0;var lamp=Field(upper,"Lamp");SampleBlink(assembly,lamp,on);
                Check(Field(lamp,"lampState").ToString()=="Blinking",slot+" upper keeps logical blink state / "+oval+" / "+phase);
                Check((bool)upper.GetType().GetProperty("IsActive").GetValue(upper,null)==on,slot+" upper physical bright/dark phase / "+oval+" / "+phase);
                Check(Field(Field(lower,"Lamp"),"lampState").ToString()=="On"&&(bool)lower.GetType().GetProperty("IsActive").GetValue(lower,null),slot+" lower stays steady in both upper phases / "+oval+" / "+phase);
                var indicator=Field(lamp,"lampInd");var glass=((Renderer[])Field(Field(upper,"Definition"),"Renderers"))[0];var mpb=new MaterialPropertyBlock();glass.GetPropertyBlock(mpb);
                Check((mpb.GetColor("_EmissionColor").maxColorComponent>.01f)==on,slot+" upper glass follows phase / "+oval+" / "+phase);
                var glare=(Renderer)Field(indicator,"glareRenderer");Check(glare!=null&&glare.enabled==((float)indicator.GetType().GetProperty("EmissionValue").GetValue(indicator,null)>0),slot+" upper halo follows phase / "+oval+" / "+phase);
                if(phase==0||phase==1)
                {
                    var focus=glass.bounds.center;
                    Render(definition.gameObject,System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),slot+(oval?"-oval":"-square")+(on?"-upper-on.png":"-upper-off.png")),focus+new Vector3(.8f,.25f,3),focus+Vector3.down*.3f,1.1f,true);
                }
            }
        }
        Call(signal,"TurnOff");foreach(var light in lights)SettleLamp(assembly,Field(light,"Lamp"));
        Check(lights.All(light=>!(bool)light.GetType().GetProperty("IsActive").GetValue(light,null)),slot+" native off extinguishes both yellows");
        Check(!(bool)assembly.GetType("RuSignals.LampTiming",true).GetMethod("Sample",All).Invoke(null,new[]{Field(upper,"Lamp"),(object).2}),slot+" owned off state stays under the fade clock");
        Debug.Log("RUS_UPPER_YELLOW_PASS "+slot+" / automatic indication / two physical phases / lower steady / native off");
    }
    private static void NativeLogicChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,Func<string,Vector3,Vector3,int,Component> rail)
    {
        var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);var blockType=sdk.GetType("Signals.Game.Railway.TrackBlock",true);
        var infoType=sdk.GetType("Signals.Game.Railway.TrackInfo",true);var directionType=infoType.GetField("Direction").FieldType;
        Func<string,object> controller=slot=>
        {
            var clone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));
            foreach(var component in clone.GetComponentsInChildren<MonoBehaviour>(true))foreach(var reference in component.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))
                if(typeof(UnityEngine.Object).IsAssignableFrom(reference.FieldType)&&reference.GetValue(component) is UnityEngine.Object optional&&optional==null)reference.SetValue(component,null);
            foreach(var def in clone.GetComponentsInChildren(common.GetType("Signals.Common.SignalDefinition",true),true))def.GetType().GetField("PhysicalReserver",All).SetValue(def,null);
            clone.gameObject.SetActive(true);return Create(basic,clone,null);
        };
        Func<object,object> signalOf=c=>((Array)basic.GetProperty("Signals").GetValue(c,null)).GetValue(0);
        Func<object,string> indication=s=>s.GetType().GetProperty("CurrentAspect").GetValue(s,null) is object aspect?Field(Call(aspect,"GetDefinition"),"Indication").ToString():"Dark";
        var track=rail("[track through](Clone)12",new Vector3(2200,0,2200),Vector3.forward,600);
        var tracks=Array.CreateInstance(infoType,1);tracks.SetValue(Create(infoType,track,Enum.Parse(directionType,"Out")),0);
        Func<object> block=()=>
        {
            var b=Create(blockType,null,600f);blockType.GetProperty("Tracks").SetValue(b,tracks,null);
            // No RailTrackRegistry in the fixture; cached native metadata is
            // supplied while route/occupancy/points/aspects remain real SDK.
            foreach(string key in new[]{"_station","_yard","_trackNumber","_trackTrimmedNumber","_trackType"})blockType.GetField(key,All).SetValue(b,key=="_trackTrimmedNumber"?"2":key=="_station"?"FF":"");
            return b;
        };
        var home=controller("EntrySignal");basic.GetField("Type").SetValue(home,Enum.Parse(basic.GetField("Type").FieldType,"Entry"));
        var next=signalOf(home);Call(next,"SetBlock",block());var aspects=((Array)next.GetType().GetProperty("AllAspects").GetValue(next,null)).Cast<object>().ToArray();
        Action<string> nextAspect=value=>Call(next,"ChangeAspect",Array.FindIndex(aspects,a=>Field(Call(a,"GetDefinition"),"Indication").ToString()==value));
        var junctionType=track.GetType().GetField("inJunction").FieldType;var junctionGo=new GameObject("Normative route turnout");junctionGo.SetActive(false);var junction=junctionGo.AddComponent(junctionType);
        var branches=(System.Collections.IList)junctionType.GetField("outBranches").GetValue(junction);
        if(branches==null){branches=(System.Collections.IList)Activator.CreateInstance(junctionType.GetField("outBranches").FieldType);junctionType.GetField("outBranches").SetValue(junction,branches);}
        var branchType=branches.GetType().GetGenericArguments()[0];var branch=Activator.CreateInstance(branchType);branchType.GetField("track").SetValue(branch,track);branches.Add(branch);junctionType.GetField("selectedBranch").SetValue(junction,(byte)0);
        track.GetType().GetField("inJunction").SetValue(track,junction);track.GetType().GetField("isJunctionTrack").SetValue(track,true);
        foreach(string slot in new[]{"Signal","EntrySignal","DivergingSignal","ExitSignal","ExitPassengerSignal","ExitMainlineSignal","DistantSignal","RepeaterSignal","OldExitSignal"})
        {
            var owner=controller(slot);var signal=signalOf(owner);var own=block();blockType.GetProperty("NextController").SetValue(own,home,null);Call(signal,"SetBlock",own);
            var def=(Component)signal.GetType().GetProperty("Definition").GetValue(signal,null);var role=Field(((Array)Field(def,"Aspects")).GetValue(0),"Role").ToString();
            bool station=role=="Entry"||role=="Exit"||role=="Route";
            basic.GetField("PrefabType").SetValue(owner,Enum.Parse(basic.GetField("PrefabType").FieldType,"ExitMainline"));
            AutomaticEntryChecks(sdk,assembly,signal,track,junction,role,nextAspect,indication);
            Debug.Log("RUS_ROLE_FIXTURE "+slot);
            track.name="[track through](Clone)12";nextAspect("Stop");Call(signal,"UpdateAspect",true);Check(indication(signal)==(role=="Repeater"?"Dark":"Yellow"),slot+" straight before closed home");
            nextAspect("Green");Call(signal,"UpdateAspect",true);Check(indication(signal)=="Green",slot+" straight before confirmed open home");
            foreach(string nextType in new[]{"Entry","Mainline"})foreach(string reduced in new[]{"DoubleYellow","DoubleYellowUpperFlashing"})
            {
                basic.GetField("Type").SetValue(home,Enum.Parse(basic.GetField("Type").FieldType,nextType));
                nextAspect(reduced);Call(signal,"UpdateAspect",true);
                Check(indication(signal)==(role=="Repeater"?"Green":"FlashingYellow"),slot+" advance warning before "+reduced+" / next type "+nextType);
                if(role!="Repeater"&&slot!="OldExitSignal")SingleYellowWarningChecks(assembly,signal,slot);
            }
            basic.GetField("Type").SetValue(home,Enum.Parse(basic.GetField("Type").FieldType,"Entry"));
            if(station)
            {
                track.name="[track diverging] 12";nextAspect("Stop");Call(signal,"UpdateAspect",true);Check(indication(signal)=="DoubleYellow",slot+" main-track divergence before closed home");
                ApplyLamps(signal);nextAspect("Green");Call(signal,"UpdateAspect",true);Check(indication(signal)=="DoubleYellowUpperFlashing",slot+" main-track divergence before open home");
                if(slot!="OldExitSignal")UpperYellowChecks(assembly,signal,slot);
                basic.GetField("PrefabType").SetValue(owner,Enum.Parse(basic.GetField("PrefabType").FieldType,"Exit"));Call(signal,"UpdateAspect",true);
                Check(indication(signal)==(role=="Exit"&&slot!="OldExitSignal"?"Green":"DoubleYellowUpperFlashing"),slot+" side departure distinguishes electrical AB and mechanical route");
            }
            junctionType.GetField("selectedBranch").SetValue(junction,(byte)1);Call(signal,"UpdateAspect",true);Check(indication(signal)==(role=="Repeater"?"Dark":role=="Distant"?"Yellow":"Stop"),slot+" unproved turnout cannot give permission");junctionType.GetField("selectedBranch").SetValue(junction,(byte)0);
            Call(next,"TurnOff");track.name="[track through] 12";Call(signal,"UpdateAspect",true);Check(indication(signal)==(role=="Repeater"?"Dark":"Yellow"),slot+" home off makes advance restrictive");
            if(slot=="OldExitSignal")MechanicalLampTimingChecks(assembly,signal);
            Call(owner,"Destroy");UnityEngine.Object.DestroyImmediate(((Component)basic.GetProperty("Definition").GetValue(owner,null)).gameObject);
        }
        Call(home,"Destroy");UnityEngine.Object.DestroyImmediate(((Component)basic.GetProperty("Definition").GetValue(home,null)).gameObject);UnityEngine.Object.DestroyImmediate(track.gameObject);UnityEngine.Object.DestroyImmediate(junctionGo);
        Check((int)assembly.GetType("RuSignals.LampTiming",true).GetField("lamps",All).GetValue(null).GetType().GetProperty("Count").GetValue(assembly.GetType("RuSignals.LampTiming",true).GetField("lamps",All).GetValue(null),null)==0,"Every destroyed native logic controller unregisters its lamps");
        Debug.Log("RUS_ROLE_LOGIC_PASS 9 native profiles / ISI role-specific selection / physical upper blink / restrictive route proof / lamp cleanup");
        Debug.Log("RUS_ADVANCE_WARNING_PASS 9 native profiles / 2 reduced aspects / 2 next-controller types / 7 electric profiles bright-dark / repeaters unchanged");
    }
}
