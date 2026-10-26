using System;
using UnityEngine;
namespace RuSignals;
internal static class LampPalette
{
    internal static readonly Color Red=new Color(1,.02f,.01f),Yellow=new Color(1,.48f,.01f),Green=new Color(.01f,1,.14f),Blue=new Color(.002f,.012f,1),White=new Color(.8f,.88f,1);
    internal static bool IsBlue(Color c)=>c.b>.9f&&c.g<.5f&&c.r<.25f;
    internal static Color Get(string name)=>name.StartsWith("Yellow",StringComparison.Ordinal)?Yellow:name.StartsWith("Green",StringComparison.Ordinal)?Green:name=="Red"?Red:name=="Blue"?Blue:White;
    internal static Color[] Hud(Indication value)
    {
        switch(value)
        {
            case Indication.Stop:return new[]{Red};
            case Indication.Green:return new[]{Green};
            case Indication.Yellow:case Indication.FlashingYellow:return new[]{Yellow};
            case Indication.DoubleYellow:case Indication.DoubleYellowUpperFlashing:return new[]{Yellow,Yellow};
            case Indication.ShuntingWhite:return new[]{White};
            case Indication.Invitation:return new[]{Red,White};
            default:return new[]{new Color(.2f,.2f,.2f)};
        }
    }
}
