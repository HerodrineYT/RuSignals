using System;
namespace RuSignals;
public static class BlockMath
{
    public const double Minimum=400,Maximum=1000,Target=700;
    public static double[] Boundaries(double distance)
    {
        if(!(distance>=808)||double.IsInfinity(distance))return Array.Empty<double>();
        int count=Math.Max(2,(int)Math.Ceiling(distance/Target));
        var result=new double[count-1];
        for(int i=0;i<result.Length;i++)result[i]=(i+1)*distance/count;
        return result;
    }
    public static double MovementAllowance(double before,double after)
    {
        double free=Math.Min(Math.Min(before,after)-Minimum-4,Maximum-4-Math.Max(before,after));
        return Math.Max(0,Math.Min(40,free*.5));
    }
    public static bool Contains(double a,double b,double position,double padding=0)=>position>=Math.Min(a,b)-padding&&position<=Math.Max(a,b)+padding;
    public static bool Overlaps(double a,double b,double c,double d)=>Math.Min(Math.Max(a,b),Math.Max(c,d))>Math.Max(Math.Min(a,b),Math.Min(c,d))+.01;
}
