using System;
namespace RuSignals;
internal static class JunctionRouteRules
{
    // DoubleTrack appends a position-derived number to duplicated native tracks.
    // Only the original through marker and its known suffixes indicate straight.
    internal static bool Diverging(string name)
    {
        const string marker="[track through]";
        if(name==null||!name.StartsWith(marker,StringComparison.Ordinal))return true;
        // SplitTrackForJunction concatenates the index without a space and
        // splitting an already cloned segment repeats the Clone/index suffix.
        for(int i=marker.Length;i<name.Length;)
        {
            if(CloneSuffix(name,i)){i+=7;continue;}
            if(char.IsWhiteSpace(name[i])||(name[i]>='0'&&name[i]<='9')){i++;continue;}
            return true;
        }
        return false;
    }
    private static bool CloneSuffix(string name,int index)=>name.Length-index>=7&&string.CompareOrdinal(name,index,"(Clone)",0,7)==0;
}
