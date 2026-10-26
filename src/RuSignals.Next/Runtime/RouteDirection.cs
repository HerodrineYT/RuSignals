using System;
using Signals.Game;
using Signals.Game.Railway;
using UnityEngine;
namespace RuSignals;
internal static class RouteDirection
{
    internal static bool OutsideStation(Junction junction)=>junction!=null&&!string.IsNullOrEmpty(junction.junctionData.junctionIdLong)&&string.IsNullOrEmpty(TrackUtils.JunctionStation(junction));
    internal static string ForRoute(TrackBlock block,Junction junction)
    {
        if(block==null||block.ShouldBeUpdated||junction==null||junction.selectedBranch<0||junction.selectedBranch>=junction.outBranches.Count)return "";
        var rail=junction.outBranches[junction.selectedBranch]?.track;if(rail==null)return "";
        foreach(var step in AutomaticBlock.RouteTracks(block))
        {
            if(step.Track!=rail)continue;
            if(!JunctionRouteRules.Diverging(rail.name))return "|";
            var points=rail.GetKinkedPointSet()?.points;if(points==null||points.Length<2)return "";
            Vector3 first=(Vector3)points[0].forward,last=(Vector3)points[points.Length-1].forward;
            // Compare the two exits of the same turnout. The bend of the whole
            // railway is not the direction of its diverging branch.
            foreach(var branch in junction.outBranches)
            {
                var through=branch?.track;if(through==null||through==rail||JunctionRouteRules.Diverging(through.name))continue;
                var reference=through.GetKinkedPointSet()?.points;if(reference==null||reference.Length<2)continue;
                Vector3 straight=through.inJunction==junction?(Vector3)reference[reference.Length-1].forward:-(Vector3)reference[0].forward;
                bool outgoing=step.Direction==(rail.inJunction==junction?TrackDirection.Out:TrackDirection.In);
                Vector3 selected=rail.inJunction==junction?last:-first;
                return outgoing?FromTangents(straight,selected):FromTangents(-selected,-straight);
            }
            return step.Direction==TrackDirection.Out?FromTangents(first,last):FromTangents(-last,-first);
        }
        return "";
    }
    internal static string FromTangents(Vector3 a,Vector3 b)
    {
        if(!(a.x*a.x+a.z*a.z>1e-8)||!(b.x*b.x+b.z*b.z>1e-8))return "";
        double degrees=Math.Atan2(a.z*b.x-a.x*b.z,a.x*b.x+a.z*b.z)*180/Math.PI;
        if(double.IsNaN(degrees)||double.IsInfinity(degrees))return "";
        return Math.Abs(degrees)<=1?"|":degrees>0?"/":"\\";
    }
}
