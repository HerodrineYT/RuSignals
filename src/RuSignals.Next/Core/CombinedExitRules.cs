using System;
namespace RuSignals;
internal static class CombinedExitRules
{
    internal static bool Shunting(string origin,string destination,bool deadEnd,bool localBoundary,float length)=>
        !string.IsNullOrEmpty(origin)&&string.Equals(origin,destination,StringComparison.Ordinal)&&length>=0&&!float.IsInfinity(length)&&(localBoundary||(deadEnd&&length<=1000));
}
