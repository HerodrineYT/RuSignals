using System;
namespace RuSignals;
public static partial class RouteSymbols
{
    private static readonly string[] Numbers=BuildNumbers();
    private static readonly ulong[] FullMasks=BuildMasks(false),SmallMasks=BuildMasks(true);
    private static string[] BuildNumbers()
    {
        var values=new string[100];values[0]="";
        for(int i=1;i<values.Length;i++)values[i]=i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return values;
    }
    private static ulong[] BuildMasks(bool small)
    {
        var masks=new ulong[100];
        for(int i=1;i<masks.Length;i++)masks[i]=DrawNumber(i,small);
        return masks;
    }
    public static string Number(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return "";
        value=value.Trim();
        foreach(char c in value)if(c<'0'||c>'9')return "";
        return int.TryParse(value,out int n) && n>0 && n<100 ? Numbers[n] : "";
    }
    public static ulong Mask(string value,bool small=false)
    {
        // Lamp columns run left to right as seen by the approaching driver.
        if(value=="\\")return Original("Left",small);
        if(value=="/")return Original("Right",small);
        if(value=="|")
        {
            ulong line=0;
            for(int row=0;row<7;row++)line|=1UL<<(row*6+2);
            return line;
        }
        if(value=="Left" || value=="Right" || value=="-")return Original(value,small);
        if(!int.TryParse(Number(value),out int n))return 0;
        return (small?SmallMasks:FullMasks)[n];
    }
    private static ulong DrawNumber(int n,bool small)
    {
        if(n<10)return Original(Numbers[n],small);
        if(n<20)return Original("10",small)|Original((n%10).ToString(System.Globalization.CultureInfo.InvariantCulture),small);
        // RUS has a dedicated tens-1 column. DV can name tracks above 19;
        // those use a compact 3x7 drawing for each digit on the same 42 lamps.
        string[] digits={"111101101101101101111","010110010010010010111","111001001111100100111","111001001111001001111","101101101111001001001","111100100111001001111","111100100111101101111","111001001010010010010","111101101111101101111","111101101111001001111"};
        ulong mask=0;
        for(int row=0;row<7;row++)for(int col=0;col<3;col++)
        {
            if(digits[n/10][row*3+col]=='1')mask|=1UL<<(row*6+col);
            if(digits[n%10][row*3+col]=='1')mask|=1UL<<(row*6+col+3);
        }
        return mask;
    }
}
