using System;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void RouteHoverLayoutChecks(Assembly assembly,object assets,string game,string output)
    {
        int start=passed;
        var ui=Assembly.LoadFrom(System.IO.Path.Combine(game,"DerailValley_Data/Managed/UnityEngine.UI.dll"));
        var type=ui.GetType("UnityEngine.UI.VerticalLayoutGroup",true);
        var root=new GameObject("Native hover layout regression fixture",typeof(RectTransform));
        try
        {
            var layout=root.AddComponent(type);
            foreach(string property in new[]{"childControlWidth","childControlHeight","childForceExpandWidth","childForceExpandHeight"})type.GetProperty(property).SetValue(layout,false,null);
            type.GetProperty("childAlignment").SetValue(layout,TextAnchor.UpperLeft,null);
            // SignalHover.GetSize: 120 * sprite.rect.size / 256. The native
            // left-aligned vertical stack takes its width from its widest item.
            Func<string,Sprite,RectTransform> item=(name,sprite)=>
            {
                var go=new GameObject(name,typeof(RectTransform));var rect=(RectTransform)go.transform;rect.SetParent(root.transform,false);
                rect.sizeDelta=sprite.rect.size*(120f/256f);return rect;
            };
            var aspect=(Sprite)Call(assets,"Icon",Enum.Parse(assembly.GetType("RuSignals.Indication",true),"Green"),false);
            var aspectRect=item("Aspect",aspect);var routeRect=item("Route",(Sprite)Call(assets,"RouteTextIcon"));
            var nameRect=item("Name",(Sprite)Field(originalHud,"HUDSprite"));
            Call(layout,"CalculateLayoutInputHorizontal");Call(layout,"CalculateLayoutInputVertical");
            float width=(float)type.GetProperty("preferredWidth").GetValue(layout,null),height=(float)type.GetProperty("preferredHeight").GetValue(layout,null);
            ((RectTransform)root.transform).sizeDelta=new Vector2(width,height);Call(layout,"SetLayoutHorizontal");Call(layout,"SetLayoutVertical");
            Check(Mathf.Abs(width-60)<.001f,"Enlarged route lettering must not widen the native hover card from 60 to 75 pixels");
            Check(Mathf.Abs(height-(120+nameRect.sizeDelta.y))<.001f,"Route text keeps the original hover row height");
            float aspectX=aspectRect.TransformPoint(aspectRect.rect.center).x,routeX=routeRect.TransformPoint(routeRect.rect.center).x;
            Check(Mathf.Abs(aspectX-routeX)<.001f,"Route and aspect centres coincide in the actual native vertical layout");
            Check(routeRect.sizeDelta==aspectRect.sizeDelta,"Route row does not stretch the shared HUD stack");
            System.IO.File.WriteAllText(System.IO.Path.Combine(output,"route-hover-layout-verification.json"),"{\"checks\":"+(passed-start)+",\"card_width\":"+width+",\"original_row_height\":60,\"centres_aligned\":true,\"scope\":\"Actual installed UnityEngine.UI.VerticalLayoutGroup with native sprite sizes, original DVSignals name sprite and SignalHover sizing formula in isolated Unity; no rendered game HUD or interactive Alt session\"}");
            Debug.Log("RUS_ROUTE_HOVER_LAYOUT_PASS "+(passed-start)+" / 60 px width / original row height / aligned centres");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
