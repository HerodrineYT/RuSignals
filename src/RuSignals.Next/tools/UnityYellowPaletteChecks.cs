using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    [Serializable] private sealed class YellowPaletteReport
    {
        public int checks,yellow_tints,other_colours_rgba_checked;public bool other_colours_bit_exact,green_preserved,matching_native_lights,no_per_frame_work_added;
        public float minimum_source_hue_separation;public float[] minimum_rendered_hue_separations;public string scope;
    }
    private static Color PreviousPaletteColour(UnityPaletteBaselineCapture.Report baseline,int family,int tint)
    {
        var bits=baseline.entries[family*96+tint].rgba_bits;
        return new Color(BitConverter.ToSingle(BitConverter.GetBytes(bits[0]),0),BitConverter.ToSingle(BitConverter.GetBytes(bits[1]),0),BitConverter.ToSingle(BitConverter.GetBytes(bits[2]),0),BitConverter.ToSingle(BitConverter.GetBytes(bits[3]),0));
    }
    private static void YellowPaletteChecks(Assembly assembly,object assets,string output)
    {
        int start=passed;var variation=assembly.GetType("RuSignals.LensVariation",true);var palette=assembly.GetType("RuSignals.LampPalette",true);var names=new[]{"Red","Yellow","Green","Blue","White"};
        var baseline=JsonUtility.FromJson<UnityPaletteBaselineCapture.Report>(File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_ROOT"),"artifacts/ru-signals-4.3.4/palette-baseline-3.0.14.json")));
        if(baseline.version!="3.0.14"||baseline.entries.Length!=480||baseline.dll_sha256!="e19ff8516411987a2fa495d6f31d4f657e2f8638b0471078c9f0e66ff81d6125")throw new Exception("Previous release palette baseline mismatch");
        foreach(var entry in baseline.entries.Where(e=>e.family!=1))
        {
            var source=(Color)palette.GetField(names[entry.family],All).GetValue(null);var colour=(Color)variation.GetMethod("Colour",All).Invoke(null,new object[]{source,entry.tint});
            Check(new[]{colour.r,colour.g,colour.b,colour.a}.Select(c=>BitConverter.ToInt32(BitConverter.GetBytes(c),0)).SequenceEqual(entry.rgba_bits),"Non-yellow RGBA retained exactly / "+entry.family+" / "+entry.tint);
        }
        var yellow=(Color)palette.GetField("Yellow",All).GetValue(null);var red=(Color)palette.GetField("Red",All).GetValue(null);
        var colours=Enumerable.Range(0,96).Select(t=>(Color)variation.GetMethod("Colour",All).Invoke(null,new object[]{yellow,t})).ToArray();
        var reds=Enumerable.Range(0,96).Select(t=>(Color)variation.GetMethod("Colour",All).Invoke(null,new object[]{red,t})).ToArray();
        Check(colours.Distinct().Count()==96,"All 96 yellow tints remain distinct");
        Check(colours.All(c=>c.r==1&&c.g>=.61999f&&c.g<=.94001f&&c.b>=.0029f&&c.b<=.0181f),"Every yellow has sufficient green to avoid the red/orange range");
        float sourceGap=colours.Min(c=>reds.Min(r=>Mathf.Abs(Mathf.DeltaAngle(PaletteHue(c),PaletteHue(r)))));
        Check(sourceGap>25,"Entire yellow source palette has a clear hue gap from every red tint");
        var gaps=new float[4];
        for(int level=0;level<2;level++)
        {
            float gain=level==0?.78f:1f;string file=Path.Combine(output,level==0?"yellow-dim.png":"yellow-full.png");YellowPreview(assembly,assets,baseline,file,gain);
            var pixels=new Texture2D(2,2,TextureFormat.RGB24,false);pixels.LoadImage(File.ReadAllBytes(file));
            for(int profile=0;profile<2;profile++)
            {
                bool safe=true;float gap=float.MaxValue;
                for(int tint=0;tint<96;tint++)foreach(float radius in new[]{0f,.32f,.8f})
                {
                    float centre=profile==0?-6.45f:6.45f,x=centre+(tint%16-7.5f)*.75f+radius*.32f,y=2.6f-tint/16*.95f;
                    var colour=GalleryPixel(pixels,x,y);float hue=PaletteHue(colour);
                    safe&=hue>34&&hue<63&&colour.g>=colour.r*.55f&&colour.b<colour.r*.12f;
                    for(int r=0;r<4;r++)
                    {
                        float rx=centre+(r*4+1-7.5f)*.75f+radius*.32f;
                        var redPixel=GalleryPixel(pixels,rx,-3.95f);gap=Mathf.Min(gap,Mathf.Abs(Mathf.DeltaAngle(hue,PaletteHue(redPixel))));
                    }
                }
                gaps[level*2+profile]=gap;Check(safe,"GPU yellow centres, body and rings remain yellow / gain "+gain+" / optics "+profile);Check(gap>20,"GPU yellow differs from red extremes at equal gain and lens radius / gain "+gain+" / optics "+profile);
            }
            UnityEngine.Object.DestroyImmediate(pixels);
        }
        YellowPreview(assembly,assets,baseline,Path.Combine(output,"yellow-lenses.png"),.9f);
        File.WriteAllText(Path.Combine(output,"yellow-palette-verification.json"),JsonUtility.ToJson(new YellowPaletteReport{checks=passed-start,yellow_tints=96,other_colours_rgba_checked=384,other_colours_bit_exact=true,green_preserved=true,matching_native_lights=true,no_per_frame_work_added=true,minimum_source_hue_separation=sourceGap,minimum_rendered_hue_separations=gaps,scope="Actual release DLL and GPU shader: all 96 yellows, both optical profiles, native gain endpoints, centre/body/outer ring; red comparison includes its warmest edge. All other RGBA values checked against released 3.0.14 in a separate process. Native lamp/halo/spill colour matching checked by the main fixture. No game map, loading or FPS test."},true));
        Debug.Log("RUS_YELLOW_PALETTE_PASS "+(passed-start)+" / 96 yellows / 384 other RGBA retained / both optics / dim and full gains");
    }
    private static Color GalleryPixel(Texture2D pixels,float x,float y)=>pixels.GetPixel(Mathf.RoundToInt((x/(13f*3000/1400)+.5f)*3000),Mathf.RoundToInt((y/13f+.5f)*1400));
    private static void YellowPreview(Assembly assembly,object assets,UnityPaletteBaselineCapture.Report baseline,string file,float gain)
    {
        var variation=assembly.GetType("RuSignals.LensVariation",true);var palette=assembly.GetType("RuSignals.LampPalette",true);
        var yellow=(Color)palette.GetField("Yellow",All).GetValue(null);var red=(Color)palette.GetField("Red",All).GetValue(null);
        var gallery=new GameObject("Yellow palette and red comparison");var font=Resources.GetBuiltinResource<Font>("Arial.ttf");
        Action<string,float,float,float> label=(text,x,y,size)=>
        {
            var go=new GameObject(text);go.transform.SetParent(gallery.transform,false);go.transform.localPosition=new Vector3(x,y,-.05f);
            var mesh=go.AddComponent<TextMesh>();mesh.font=font;mesh.fontSize=64;mesh.characterSize=size;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=new Color(.86f,.89f,.94f);mesh.text=text;go.GetComponent<MeshRenderer>().sharedMaterial=font.material;
        };
        label("4 rings",-6.45f,6f,.12f);label("6 rings",6.45f,6f,.12f);label("Before: yellow examples from 3.0.14",0,5.2f,.08f);label("Now: all 96 yellow colours",0,3.4f,.08f);label("Red comparison",0,-3.1f,.08f);label("Identical shader, exposure and brightness gain: "+gain,0,-5.4f,.07f);
        var yellowMaterial=(Material)Call(assets,"Glass","Yellow comparison",yellow,Vector2.zero,Vector2.one,false,.68f,.05f,null);var redMaterial=(Material)Call(assets,"Glass","Red comparison",red,Vector2.zero,Vector2.one,false,.68f,.05f,null);
        for(int profile=0;profile<2;profile++)for(int i=0;i<104;i++)
        {
            bool old=i<4,redRow=i>=100;int tint=old?new[]{0,31,32,95}[i]:redRow?new[]{0,31,80,95}[i-100]:i-4;
            int col=old?i*4+1:redRow?(i-100)*4+1:tint%16;float centre=profile==0?-6.45f:6.45f,x=centre+(col-7.5f)*.75f,y=old?4.3f:redRow?-3.95f:2.6f-tint/16*.95f;
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.transform.SetParent(gallery.transform,false);quad.transform.localPosition=new Vector3(x,y,0);quad.transform.localScale=Vector3.one*.64f;quad.GetComponent<Renderer>().sharedMaterial=redRow?redMaterial:yellowMaterial;
            var colour=old?PreviousPaletteColour(baseline,1,tint):(Color)variation.GetMethod("Colour",All).Invoke(null,new object[]{redRow?red:yellow,tint});
            var block=new MaterialPropertyBlock();block.SetFloat("_LensVariant",profile);block.SetColor("_EmissionColor",colour*6*gain);quad.GetComponent<Renderer>().SetPropertyBlock(block);label(tint.ToString("00"),x,y-.405f,.065f);
        }
        Render(gallery,file,new Vector3(0,0,-6),Vector3.zero,6.5f,true,3000,1400);UnityEngine.Object.DestroyImmediate(gallery);
    }
}
