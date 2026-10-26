using System;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void RouteTextScaleChecks(string game,string output)
    {
        int start=passed;
        var tmp=Assembly.LoadFrom(System.IO.Path.Combine(game,"DerailValley_Data/Managed/Unity.TextMeshPro.dll"));
        var type=tmp.GetType("TMPro.TextMeshProUGUI",true);var baseType=tmp.GetType("TMPro.TMP_Text",true);
        var element=baseType.GetNestedType("TextProcessingElement",All);var parse=baseType.GetMethod("ValidateHtmlTag",All);
        var go=new GameObject("Native TMP route size parser fixture",typeof(RectTransform));go.SetActive(false);
        try
        {
            var text=go.AddComponent(type);((RectTransform)go.transform).sizeDelta=new Vector2(60,60);
            Check((bool)type.GetProperty("richText").GetValue(text,null),"Actual TMP default supports percentage size markup");
            var size=baseType.GetField("m_fontSize",All);var current=baseType.GetField("m_currentFontSize",All);var stack=baseType.GetField("m_sizeStack",All);
            foreach(string value in new[]{"1","12","/","|","\\"})
            {
                string formatted="<size=125%>"+value+"</size>";var chars=Array.CreateInstance(element,formatted.Length+1);
                for(int i=0;i<formatted.Length;i++){var c=Activator.CreateInstance(element);element.GetField("unicode",All).SetValue(c,(uint)formatted[i]);chars.SetValue(c,i);}
                size.SetValue(text,24f);current.SetValue(text,24f);var sizes=stack.GetValue(text);sizes.GetType().GetMethod("SetDefault").Invoke(sizes,new object[]{24f});stack.SetValue(text,sizes);
                var open=new object[]{chars,1,0};bool accepted=(bool)parse.Invoke(text,open);
                Check(accepted&&(int)open[2]==10,"Actual TMP consumes the route size tag: "+value);
                Check(Mathf.Abs((float)current.GetValue(text)-30)<.00001f,"Actual TMP scales route font from 24 to exactly 30 points: "+value);
                var close=new object[]{chars,formatted.IndexOf("</size>",StringComparison.Ordinal)+1,0};
                Check((bool)parse.Invoke(text,close)&&Mathf.Abs((float)current.GetValue(text)-24)<.00001f,"Actual TMP restores the original size after route text: "+value);
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(go);}
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"route-text-scale-verification.json"),"{\"checks\":"+(passed-start)+",\"scale\":1.25,\"baseline_font_points\":24,\"enlarged_font_points\":30,\"digits_and_three_directions\":true,\"scope\":\"Actual installed TextMeshProUGUI percentage-size tag parser in isolated Unity, plus native HUD interface and original 128-pixel route row checks in the main fixture; no rendered game HUD or interactive Alt session\"}");
        Debug.Log("RUS_ROUTE_TEXT_SCALE_PASS "+(passed-start)+" actual TMP size parser checks / 1.25 scale");
    }
}
