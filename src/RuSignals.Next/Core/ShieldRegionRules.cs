using System;
namespace RuSignals;

// Only the appearance depends on geography. Train decisions never use this.
internal static class ShieldRegionRules
{
    internal const float IndustrialRadius=1800;
    internal static bool IndustrialStation(string id)=>
        string.Equals(id,"HB",StringComparison.OrdinalIgnoreCase)||
        string.Equals(id,"CW",StringComparison.OrdinalIgnoreCase)||
        string.Equals(id,"OR",StringComparison.OrdinalIgnoreCase)||
        string.Equals(id,"FF",StringComparison.OrdinalIgnoreCase)||
        string.Equals(id,"MF",StringComparison.OrdinalIgnoreCase);
    internal static bool Near(float dx,float dz)=>dx*dx+dz*dz<=IndustrialRadius*IndustrialRadius;
}
