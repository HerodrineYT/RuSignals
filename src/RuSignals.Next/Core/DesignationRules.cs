using System;
using System.Text;
using System.Text.RegularExpressions;
namespace RuSignals;

internal static class DesignationRules
{
    private static readonly Regex TrackNumber=new Regex(@"(\d+)[A-Za-z]*$",RegexOptions.Compiled);
    internal static string Number(string track)
    {
        var match=TrackNumber.Match(track??string.Empty);
        return match.Success&&int.TryParse(match.Groups[1].Value,out int number)&&number>0?number.ToString():null;
    }
    internal static bool ServiceName(string name)=>string.IsNullOrEmpty(name)||name.StartsWith("АБ-",StringComparison.Ordinal)||name.StartsWith("AB-",StringComparison.Ordinal)||name.StartsWith("T_",StringComparison.Ordinal)||name.StartsWith("T-",StringComparison.Ordinal);
    internal static string Format(SignalRole role,bool even,int index,string trackNumber=null,string home=null)
    {
        string side=even?"Ch":"N";
        switch(role)
        {
            case SignalRole.Shunting:case SignalRole.Turntable:return "M"+index;
            case SignalRole.BufferStop:return "T"+index;
            case SignalRole.Entry:return side+(index>1?index.ToString():string.Empty);
            case SignalRole.Exit:return side+(trackNumber??index.ToString());
            case SignalRole.Route:return side+"M"+index;
            case SignalRole.Distant:return "P"+Latin(home??side+index);
            case SignalRole.Repeater:return "Pv"+Latin(home??side+index);
            default:return (index*2-(even?0:1)).ToString();
        }
    }
    internal static string Suffix(int duplicate)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return duplicate<alphabet.Length?alphabet[duplicate].ToString():(duplicate+1).ToString();
    }
    internal static string Latin(string value)
    {
        if(string.IsNullOrEmpty(value))return value;
        const string upper="АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ";
        const string lower="абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        StringBuilder result=null;
        for(int i=0;i<value.Length;i++)
        {
            char c=value[i];int index=upper.IndexOf(c);bool capital=index>=0;
            if(!capital)index=lower.IndexOf(c);
            if(index<0){result?.Append(c);continue;}
            if(result==null){result=new StringBuilder(value.Length+4);result.Append(value,0,i);}
            string text=Letters[index];result.Append(capital?text:text.ToLowerInvariant());
        }
        return result==null?value:result.ToString();
    }
    private static readonly string[] Letters={"A","B","V","G","D","E","Yo","Zh","Z","I","Y","K","L","M","N","O","P","R","S","T","U","F","Kh","Ts","Ch","Sh","Shch","","Y","","E","Yu","Ya"};
}
