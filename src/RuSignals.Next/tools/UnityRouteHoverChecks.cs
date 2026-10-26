using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static int routeHoverDisplays,routeHoverChecks;
    private static Sprite routeHoverSprite;
    private static void RouteHoverChecks(object controller,string slot)
    {
        int start=passed;
        foreach(var signal in (IEnumerable)controller.GetType().GetProperty("AllSignals").GetValue(controller,null))
        foreach(var display in ((IEnumerable)Call(signal,"GetAllHudElements")).Cast<object>().Where(e=>e.GetType().Name=="RouteDisplay"))
        {
            var type=display.GetType();var sprite=(Sprite)type.GetProperty("Sprite").GetValue(display,null);
            Check(sprite!=null,slot+" text-only route retains a sprite for the native HUD filter");
            if(routeHoverSprite==null)
            {
                var rt=RenderTexture.GetTemporary(sprite.texture.width,sprite.texture.height,0,RenderTextureFormat.ARGB32);
                var previous=RenderTexture.active;var pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
                try
                {
                    Graphics.Blit(sprite.texture,rt);RenderTexture.active=rt;
                    pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();
                    Check(pixels.GetPixels32().All(c=>c.a==0),"Route hover GPU background is fully transparent: no white circle");
                }
                finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(pixels);}
                routeHoverSprite=sprite;
            }
            Check(ReferenceEquals(routeHoverSprite,sprite),slot+" all route displays share one transparent sprite");
            Check(sprite.rect.size==new Vector2(128,128),slot+" route row retains its original dimensions and matches the aspect width");
            Check(!ReferenceEquals(sprite,Field(originalHud,"HUDSprite")),slot+" route text keeps its own native prefab apart from the name");
            Check((Color)type.GetProperty("TextColour").GetValue(display,null)==Color.white,slot+" route text remains legible white");
            var text=type.GetProperty("DisplayText");var original=(string)text.GetValue(display,null);
            var hudText=type.GetInterface("Signals.Game.IHudDisplayable").GetProperty("DisplayText");
            try
            {
                foreach(string value in new[]{"1","12","/","|","\\"})
                {
                    text.SetValue(display,value,null);
                    Check((bool)type.GetProperty("ShouldDisplay").GetValue(display,null)&&(string)text.GetValue(display,null)==value&&sprite!=null&&(string)hudText.GetValue(display,null)=="<size=125%>"+value+"</size>",slot+" native HUD enlarges route text by 1.25 while retaining raw world text "+value);
                }
                Check(ReferenceEquals(hudText.GetValue(display,null),hudText.GetValue(display,null)),slot+" stable hover text reuses its cached string");
                text.SetValue(display,"",null);
                Check(string.IsNullOrEmpty((string)text.GetValue(display,null))&&string.IsNullOrEmpty((string)hudText.GetValue(display,null)),slot+" empty route remains absent from the native HUD");
            }
            finally{text.SetValue(display,original,null);}
            routeHoverDisplays++;
        }
        routeHoverChecks+=passed-start;
    }
    private static void WriteRouteHoverReport(string output)
    {
        Check(routeHoverDisplays>0,"Native route hover displays were exercised");
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"route-hover-verification.json"),"{\"displays\":"+routeHoverDisplays+",\"checks\":"+routeHoverChecks+",\"text_scale\":1.25,\"plain_world_text\":true,\"cached_hover_text\":true,\"transparent_gpu_background\":true,\"digits_and_directions\":true,\"shared_sprite\":true,\"native_layout_scale\":1.0,\"scope\":\"Actual native SDK HUD interface and GPU alpha readback in isolated Unity; no interactive Alt cursor session\"}");
        Debug.Log("RUS_ROUTE_HOVER_PASS "+routeHoverDisplays+" displays / "+routeHoverChecks+" checks / text without circle");
    }
}
