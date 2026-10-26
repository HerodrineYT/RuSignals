using System;
using System.Collections.Generic;

namespace RuSignals;

public static class BlockMath
{
    // User-selected DV layout. All intervals include both end margins;
    // no short leftover block is appended after a fixed-step loop.
    public const double Minimum = 400, Maximum = 1000, Target = 700;
    public static double[] Boundaries(double length)
    {
        if (double.IsNaN(length) || double.IsInfinity(length) || length < 2 * (Minimum + 4))
            return Array.Empty<double>();
        int blocks = Math.Max(2,(int)Math.Ceiling(length / Target));
        var result = new List<double>();
        for (int i = 1; i < blocks; i++) result.Add(i * length / blocks);
        return result.ToArray();
    }

    // Both neighbouring masts may move: reserve half the available distance
    // and an additional four metres for rail-point quantisation.
    public static double MovementAllowance(double before,double after) => Math.Max(0,
        Math.Min(40,Math.Min((Math.Min(before,after)-Minimum-4)/2,(Maximum-4-Math.Max(before,after))/2)));

    public static bool Contains(double from, double to, double span, double padding = 0) =>
        !double.IsNaN(span) && span >= Math.Min(from,to) - padding && span <= Math.Max(from,to) + padding;
    public static bool Overlaps(double a, double b, double c, double d) =>
        Math.Min(Math.Max(a,b),Math.Max(c,d)) - Math.Max(Math.Min(a,b),Math.Min(c,d)) > .01;
}
