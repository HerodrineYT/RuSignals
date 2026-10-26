using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static int semaphoreHudChecks,semaphoreHudSlots;
    private static readonly Dictionary<Sprite,Color32[]> semaphoreHudPixels=new Dictionary<Sprite,Color32[]>();
    private static Color32[] HudPixels(Sprite sprite)
    {
        if(semaphoreHudPixels.TryGetValue(sprite,out var cached))return cached;
        var rt=RenderTexture.GetTemporary(sprite.texture.width,sprite.texture.height,0,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
        try
        {
            Graphics.Blit(sprite.texture,rt);RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);texture.Apply();
            cached=texture.GetPixels32();semaphoreHudPixels.Add(sprite,cached);return cached;
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(texture);}
    }
    private static void SemaphoreHudChecks(Assembly assembly,object controller,string slot)
    {
        int start=passed;
        foreach(var signal in (IEnumerable)controller.GetType().GetProperty("AllSignals").GetValue(controller,null))
        {
            var definition=(Component)signal.GetType().GetProperty("Definition").GetValue(signal,null);
            var visual=definition.GetComponentInChildren(assembly.GetType("RuSignals.SemaphoreVisual",true),true);if(visual==null)continue;
            int original=(int)signal.GetType().GetProperty("CurrentAspectIndex").GetValue(signal,null);
            var aspects=(Array)signal.GetType().GetProperty("AllAspects").GetValue(signal,null);
            try
            {
                for(int i=0;i<aspects.Length;i++)
                {
                    Call(signal,"ChangeAspect",i);Call(visual,"Advance",3f);
                    var aspect=signal.GetType().GetProperty("CurrentAspect").GetValue(signal,null);var d=Call(aspect,"GetDefinition");
                    string indication=Field(d,"Indication").ToString();var sprite=(Sprite)aspect.GetType().GetProperty("Sprite").GetValue(aspect,null);
                    var pixels=HudPixels(sprite);
                    bool red=pixels.Any(c=>c.a>200&&c.r>200&&c.g<100&&c.b<100);
                    bool green=pixels.Any(c=>c.a>200&&c.g>200&&c.r<100&&c.b<100);
                    bool yellow=pixels.Any(c=>c.a>200&&c.r>200&&c.g>100&&c.b<100);
                    Check(red==((Renderer)Field(visual,"Red")).enabled,slot+" / "+indication+" HUD red agrees with physical semaphore");
                    Check(green==((Renderer)Field(visual,"Green")).enabled,slot+" / "+indication+" HUD green agrees with physical semaphore");
                    Check(yellow==((Field(visual,"Yellow") as Renderer)?.enabled??false),slot+" / "+indication+" HUD yellow agrees with physical semaphore");
                    Check(!pixels.Any(c=>c.a>200&&c.b>200&&c.r<100&&c.g<100),slot+" mechanical stop never exports an electric blue lamp");
                    Check(!pixels.Any(c=>c.a>200&&c.r>150&&c.g>150&&c.b>150),slot+" removed auxiliary white lamp never appears in mechanical HUD");
                    if(indication=="DoubleYellowUpperFlashing")
                        Check(pixels[94*128+94].a==0&&green&&yellow,slot+" open two-arm indication has steady green and yellow without a flashing ring");
                }
                semaphoreHudSlots++;
            }
            finally{Call(signal,"ChangeAspect",original);Call(visual,"Advance",3f);}
        }
        semaphoreHudChecks+=passed-start;
    }
    private static void WriteSemaphoreHudReport(string output)
    {
        Check(semaphoreHudSlots>0,"Mechanical native HUD slots were exercised");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"semaphore-hud-verification.json"),"{\"slots\":"+semaphoreHudSlots+",\"checks\":"+semaphoreHudChecks+",\"physical_lamp_colours_checked\":true,\"all_aspects_checked\":true,\"scope\":\"Actual SDK aspects and physical semaphore renderers after animation, GPU HUD pixels; no interactive Alt cursor session\"}");
        Debug.Log("RUS_SEMAPHORE_HUD_PASS "+semaphoreHudSlots+" slots / "+semaphoreHudChecks+" checks");
    }
}
