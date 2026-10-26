using System;
using System.Collections.Generic;
using HarmonyLib;
using RailwaySafetyGadgets;
namespace RuSignals.RsgBridge;
internal static class SpeedIntegration
{
    internal static void Install(Harmony h)
    {
        var type=AccessTools.TypeByName("RailwaySafetyGadgets.SpeedSigns");var method=AccessTools.Method(type,"Points",new[]{typeof(RailTrack)});
        if(method==null||method.ReturnType!=typeof(IEnumerable<SpeedPoint>))throw new NotSupportedException("Unsupported RSG speed profile API");
        h.Patch(method,postfix:new HarmonyMethod(typeof(SpeedIntegration),nameof(Points)));
    }
    private static void Points(RailTrack track,ref IEnumerable<SpeedPoint> __result)
    {
        var restrictions=SignalSpeedLimits.OnTrack(track);if(restrictions.Count==0||__result==null)return;
        __result=Merge(__result,restrictions);
    }
    private static List<SpeedPoint> Merge(IEnumerable<SpeedPoint> native,IReadOnlyList<SignalSpeedLimits.Span> restrictions)
    {
        var original=new List<SpeedPoint>(native);var result=new List<SpeedPoint>();
        foreach(int direction in new[]{-1,1})
        {
            var coordinates=new SortedSet<double>();
            foreach(var point in original)if(point.Direction==direction)coordinates.Add(point.Span);
            foreach(var range in restrictions)if(range.Direction==direction){coordinates.Add(range.Start);coordinates.Add(range.End);}
            foreach(double span in coordinates)
            {
                int? value=null;double nearest=double.NegativeInfinity;
                foreach(var point in original)
                {
                    double distance=(point.Span-span)*direction;
                    if(point.Direction==direction&&distance<=.0001&&distance>=nearest){value=point.Value;nearest=distance;}
                }
                if(value.HasValue)foreach(var range in restrictions)if(range.Direction==direction&&range.Contains(span))value=Math.Min(value.Value,range.Limit);
                result.Add(new SpeedPoint(span,direction,value));
            }
        }
        return result;
    }
}
