using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    [Serializable] private sealed class PaletteReferenceProof
    {
        public string label;public float reference_hue_degrees,closest_hue_error_degrees;public Color closest_palette_colour;
    }
    [Serializable] private sealed class PaletteReferenceReport
    {
        public int tints_per_colour,legacy_tints_per_colour,added_tints_per_colour,legacy_colours_checked;public bool colours_only,uniform_optics_per_mast,legacy_rgba_exact,yellow_family_retuned;public PaletteReferenceProof[] references;
        public string scope;
    }
    private static float PaletteHue(Color colour){Color.RGBToHSV(colour,out float hue,out _,out _);return hue*360;}
    private static bool IntervalFixtureLength(Component __0,ref double __result)=>TerminalFixtureLength(__0,ref __result);
    private static void WithIntervalFixtureLength(Assembly sdk,object harmony,Action test)
    {
        var method=sdk.GetType("Signals.Game.Extensions",true).GetMethod("GetLength",All);var prefix=typeof(UnityRewriteCheck).GetMethod("IntervalFixtureLength",All);
        var patch=harmony.GetType().GetMethods().Single(m=>m.Name=="Patch"&&m.GetParameters().Length>=4);var args=new object[patch.GetParameters().Length];args[0]=method;args[1]=Create(harmony.GetType().Assembly.GetType("HarmonyLib.HarmonyMethod",true),prefix);patch.Invoke(harmony,args);
        try{test();}finally{var unpatch=harmony.GetType().GetMethods().Single(m=>m.Name=="Unpatch"&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(MethodInfo));unpatch.Invoke(harmony,new object[]{method,prefix});}
    }
    private static void LensVariationChecks(Assembly common,Assembly sdk,Assembly assembly,object pack,object assets)
    {
        var variation=assembly.GetType("RuSignals.LensVariation",true);var basic=sdk.GetType("Signals.Game.Controllers.BasicSignalController",true);
        var owners=new System.Collections.Generic.List<object>();var profiles=new System.Collections.Generic.HashSet<int>();int start=passed;
        var blinkProfiles=new System.Collections.Generic.HashSet<int>();int actualYellows=0;bool actualYellowsSafe=true;
        var preview=new GameObject("Lens variation preview");
        var mapping=Shader.PropertyToID("_LensVariant");
        try
        {
            foreach(string slot in new[]{"Signal","EntrySignal","ExitSignal","ShuntingSignal"})for(int sample=0;sample<3;sample++)
            {
                var clone=UnityEngine.Object.Instantiate((Component)Field(pack,slot));NormalizePlacedFixture(clone);clone.gameObject.SetActive(true);
                var owner=Create(basic,clone,null);owners.Add(owner);var signal=((Array)basic.GetProperty(slot=="ShuntingSignal"?"ShuntingSignals":"Signals").GetValue(owner,null)).GetValue(0);
                signal.GetType().GetField("NameOverride").SetValue(signal,"VAR"+sample);basic.GetField("NameOverride").SetValue(owner,"VAR"+sample);
                var mastProfiles=new System.Collections.Generic.HashSet<int>();
                variation.GetMethod("Apply",All).Invoke(null,new[]{signal});
                foreach(var light in (Array)signal.GetType().GetProperty("AllLights").GetValue(signal,null))
                {
                    var def=(Component)Field(light,"Definition");var renderers=(Renderer[])Field(def,"Renderers");
                    if(renderers.Length!=1||renderers[0].sharedMaterial.shader.name!="RuSignals/Signal glass four rings")continue;
                    var indicator=Field(Field(light,"Lamp"),"lampInd");var spill=(Light)Field(def,"Light");var renderer=renderers[0];
                    var view=def.GetComponentInParent(assembly.GetType("RuSignals.SpillView",true));Call(view,"SetGain",1f);
                    int profile=(int)variation.GetMethod("Profile",All).Invoke(null,new object[]{"VAR"+sample});profiles.Add(profile);float gain=(float)variation.GetMethod("Gain",All).Invoke(null,new object[]{"VAR"+sample+"/"+def.name});
                    var source=(Color)Field(def,"Colour");var emission=(Color)Field(indicator,"emissionColor");var colour=spill.color;
                    if(source.r>.9f&&source.g>=.1f&&source.g<.7f){actualYellows++;actualYellowsSafe&=PaletteHue(colour)>36&&PaletteHue(colour)<58;}
                    Check(Vector3.Distance(new Vector3(emission.r,emission.g,emission.b).normalized,new Vector3(colour.r,colour.g,colour.b).normalized)<.00001f,"Lens and real environment light use the same variant hue");
                    var halo=(Color)Field(indicator,"glareColor");
                    Check(Vector3.Distance(new Vector3(halo.r,halo.g,halo.b).normalized,new Vector3(colour.r,colour.g,colour.b).normalized)<.00001f,"Halo uses the same lens and spill hue");
                    var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);float optical=block.GetFloat(mapping);mastProfiles.Add((int)optical);
                    Check(optical==profile,"Actual lens property block contains its optical ring profile");
                    Check(Mathf.Abs((float)Field(indicator,"lightIntensity")-(float)Field(def,"LightIntensity")*gain)<.00001f,"Spill uses the same fixed brightness gain");
                    var original=renderer.sharedMaterial;variation.GetMethod("Apply",All).Invoke(null,new[]{signal});
                    Check(renderer.sharedMaterial==original&&(Color)Field(indicator,"emissionColor")==emission,"Repeated binding neither clones material nor compounds brightness");
                    Call(light,"TurnOn",false);SettleLamp(assembly,Field(light,"Lamp"));renderer.GetPropertyBlock(block);
                    Check(block.GetFloat(mapping)==optical,"Native lamp updates retain the lens profile");
                    if(blinkProfiles.Add(profile))
                    {
                        Call(light,"TurnOn",true);SampleBlink(assembly,Field(light,"Lamp"),true);SampleBlink(assembly,Field(light,"Lamp"),false);
                        renderer.GetPropertyBlock(block);Check(block.GetFloat(mapping)==optical,"Native blinking retains the optical profile at both phase endpoints");
                        Call(light,"TurnOn",false);SettleLamp(assembly,Field(light,"Lamp"));
                    }
                    Call(view,"SetGain",0f);Check(spill.intensity==0,"Variant spill remains off outside local-light range");Call(view,"SetGain",1f);
                    Check(Mathf.Abs(spill.intensity-(float)Field(indicator,"lightIntensity"))<.00001f,"Variant spill restores exact full gain after distance fade");
                    Call(light,"TurnOff");SettleLamp(assembly,Field(light,"Lamp"));Check(spill.intensity==0,"Variant light turns fully off with the lamp");
                    Check(colour.maxColorComponent>.9f&&source.maxColorComponent>.9f,"Variant stays in its original signal colour family");
                }
                Check(mastProfiles.Count==1,"Every lens on the same physical mast uses one optical construction");
            }
            Check(actualYellows>0&&actualYellowsSafe,"Actual native yellow lamps use clearly yellow variant colours");
            Check(profiles.Count==2,"Only the four-ring and six-ring profiles occur across native lamps");
            Check(variation.GetMethod("Update",All)==null,"Variants add no per-lamp frame callback");
            var colours=new[]{new Color(1,.02f,.01f),new Color(1,.48f,.01f),new Color(.01f,1,.14f),new Color(.002f,.012f,1),new Color(.8f,.88f,1)};
            // Actual existing glass shader, old four-ring profile and shared
            // material; only the per-instance optical profile and emission values vary.
            var combinations=new System.Collections.Generic.HashSet<int>();
            for(int i=0;i<4096;i++)
            {
                string key="colour-variation-"+i;
                int optical=(int)variation.GetMethod("Profile",All).Invoke(null,new object[]{key}),tint=(int)variation.GetMethod("TintIndex",All).Invoke(null,new object[]{key});
                combinations.Add(optical*96+tint);
            }
            Check(combinations.Count==192,"Both optical patterns independently cover all 96 tint variants");
            string baselinePath=Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_ROOT"),"artifacts/ru-signals-0.1.0/palette-baseline-3.0.11.json");
            var baseline=JsonUtility.FromJson<UnityPaletteBaselineCapture.Report>(File.ReadAllText(baselinePath));
            if(baseline.version!="3.0.11"||baseline.dll_sha256!="59c7b428203a30e68e5a435709211eeab479782df52c7cb2482dd25770ce0315"||baseline.entries.Length!=160)throw new Exception("Original release palette baseline missing or changed");
            foreach(var entry in baseline.entries.Where(e=>e.family!=1))
            {
                var colour=(Color)variation.GetMethod("Colour",All).Invoke(null,new object[]{colours[entry.family],entry.tint});
                var bits=new[]{colour.r,colour.g,colour.b,colour.a}.Select(c=>BitConverter.ToInt32(BitConverter.GetBytes(c),0)).ToArray();
                Check(bits.SequenceEqual(entry.rgba_bits),"Original 3.0.11 RGBA retained exactly / "+entry.family+" / "+entry.tint);
            }
            var palette=new Color[5][];
            for(int family=0;family<colours.Length;family++)
            {
                var variants=Enumerable.Range(0,96).Select(t=>(Color)variation.GetMethod("Colour",All).Invoke(null,new object[]{colours[family],t})).Distinct().ToArray();palette[family]=variants;
                Check(variants.Length==96,"Each signal colour retains 32 original tints and adds 64 distinct tints");
                Check(variants.All(c=>!float.IsNaN(c.r)&&!float.IsNaN(c.g)&&!float.IsNaN(c.b)&&c.r>=0&&c.g>=0&&c.b>=0&&c.r<=1&&c.g<=1&&c.b<=1&&Math.Abs(c.maxColorComponent-1)<.00001f),"Palette contains bounded normalized RGB colours");
                Check(variants.All(c=>family==0?c.r==1&&c.g<.16f&&c.b<.5f:family==1?c.r==1&&c.g>=.61999f&&c.g<=.94001f:family==2?c.g==1&&c.r<.05f&&c.b<.85f:family==3?c.b==1&&c.r<.2f&&c.g<.3f:Mathf.Min(c.r,Mathf.Min(c.g,c.b))>=.5f),"All expanded tints remain in their native signal colour family");
            }
            var labels=new[]{"reference one ruby red","reference two raspberry red","reference two crimson edge","reference one sea green","reference two turquoise green","reference two deep blue","blue-violet extension","retuned yellow lower boundary","reference two golden yellow"};
            var families=new[]{0,0,0,2,2,3,3,1,1};var hues=new[]{354.1f,341.8f,333.2f,158.3f,165.1f,232.2f,245f,37.2f,50.7f};var proofs=new PaletteReferenceProof[hues.Length];
            for(int i=0;i<hues.Length;i++)
            {
                var closest=palette[families[i]].OrderBy(c=>Mathf.Abs(Mathf.DeltaAngle(PaletteHue(c),hues[i]))).First();float error=Mathf.Abs(Mathf.DeltaAngle(PaletteHue(closest),hues[i]));
                Check(error<1,"Expanded palette includes reference hue / "+labels[i]);
                proofs[i]=new PaletteReferenceProof{label=labels[i],reference_hue_degrees=hues[i],closest_hue_error_degrees=error,closest_palette_colour=closest};
            }
            Check(palette[4].Any(c=>c.b>c.r+.2f)&&palette[4].Any(c=>c.r>c.b+.1f),"White palette includes cool and warm glass without borrowing a coloured signal");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"palette-reference-verification.json"),JsonUtility.ToJson(new PaletteReferenceReport{tints_per_colour=96,legacy_tints_per_colour=32,added_tints_per_colour=64,legacy_colours_checked=128,legacy_rgba_exact=true,yellow_family_retuned=true,colours_only=true,uniform_optics_per_mast=true,references=proofs,scope="Actual release palette in Unity; 128 non-yellow original RGBA values compared bit for bit with released 3.0.11; yellow family intentionally retuned and its lower boundary selected for distinction from red. New reference hues sampled from supplied images; bloom, white cores, ring patterns and structures not taken from references."},true));
            for(int row=0;row<2;row++)for(int col=0;col<25;col++)
            {
                int family=col/5,tint=new[]{0,31,32,63,95}[col%5];
                var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.transform.SetParent(preview.transform,false);quad.transform.localPosition=new Vector3((col-12)*.75f,(.5f-row)*.85f,0);quad.transform.localScale=Vector3.one*.68f;
                var material=(Material)Call(assets,"Glass","Variant preview "+family,colours[family],Vector2.zero,Vector2.one,false,.68f,.05f,null);quad.GetComponent<Renderer>().sharedMaterial=material;
                var block=new MaterialPropertyBlock();block.SetFloat(mapping,row);var colour=(Color)variation.GetMethod("Colour",All).Invoke(null,new object[]{colours[family],tint});block.SetColor("_EmissionColor",colour*(family==3?3:6)*.9f);quad.GetComponent<Renderer>().SetPropertyBlock(block);
            }
            string output=Environment.GetEnvironmentVariable("RUS_V2_OUTPUT");Render(preview,Path.Combine(output,"lens-variants.png"),new Vector3(0,0,-6),Vector3.zero,1.5f,true,3200,500);
            File.WriteAllText(Path.Combine(output,"lens-variation-verification.json"),"{\"checks\":"+(passed-start)+",\"profiles\":2,\"tints_per_colour\":96,\"legacy_tints_per_colour\":32,\"added_tints_per_colour\":64,\"legacy_colours_checked\":128,\"yellow_family_retuned\":true,\"legacy_rgba_exact\":true,\"unchanged_shared_materials\":true,\"stable_identity\":true,\"uniform_optics_per_mast\":true,\"matched_lens_halo_spill\":true,\"native_blink_retains_profile\":true,\"ring_counts\":[4,6],\"per_frame_work_added\":false,\"scope\":\"Actual SDK lamps, property blocks, distance gain, original release RGBA comparison and GPU preview in Unity; no game save\"}");
            preview.SetActive(false);
            YellowPaletteChecks(assembly,assets,output);
            Debug.Log("RUS_LENS_VARIATION_PASS "+(passed-start)+" / two optical profiles / 96 tints with retuned yellows / uniform optics per mast / shared materials / native updates / matched spill");
        }
        finally
        {
            foreach(var owner in owners){var def=(Component)basic.GetProperty("Definition").GetValue(owner,null);Call(owner,"Destroy");UnityEngine.Object.DestroyImmediate(def.gameObject);}
            UnityEngine.Object.DestroyImmediate(preview);
        }
    }
}
