using System;
using Signals.Game;
using Signals.Game.Railway;
using UnityEngine;

namespace RuSignals;

internal static class RouteDirection
{
    internal static bool OutsideStation(Junction junction) => junction != null &&
        !string.IsNullOrEmpty(junction.junctionData.junctionIdLong) &&
        string.IsNullOrEmpty(TrackUtils.JunctionStation(junction));

    internal static string ForRoute(TrackBlock block, Junction junction)
    {
        if (block == null || block.ShouldBeUpdated || junction == null ||
            junction.selectedBranch < 0 || junction.selectedBranch >= junction.outBranches.Count) return "";
        var selected = junction.outBranches[junction.selectedBranch]?.track;
        if (selected == null) return "";
        // Use the actual native route, even when AB shortened the occupancy
        // interval before the points. An unaligned/stale branch must stay dark.
        foreach (var info in AutomaticBlock.RouteTracks(block))
        {
            if (info.Track != selected) continue;
            // DVSignals' native identity for the through leg (not prefab name).
            if (selected.name == "[track through]") return "|";
            var points = selected.GetKinkedPointSet()?.points;
            if (points == null || points.Length < 2) return "";
            Vector3 start = (Vector3)points[0].forward, end = (Vector3)points[points.Length - 1].forward;
            if (info.Direction == TrackDirection.In) { var swap = start; start = -end; end = -swap; }
            return FromTangents(start, end);
        }
        return "";
    }

    internal static string FromTangents(Vector3 start, Vector3 end)
    {
        // Project onto the horizontal plane; slopes and map rotation must not
        // reverse a direction. Reverse travel swaps and negates the tangents.
        double a = start.x * start.x + start.z * start.z;
        double b = end.x * end.x + end.z * end.z;
        if (!(a > 1e-8) || !(b > 1e-8)) return "";
        double angle = Math.Atan2(start.z * end.x - start.x * end.z,
            start.x * end.x + start.z * end.z) * 180 / Math.PI;
        if (double.IsNaN(angle) || double.IsInfinity(angle)) return "";
        return Math.Abs(angle) <= 1 ? "|" : angle > 0 ? "/" : "\\";
    }
}
