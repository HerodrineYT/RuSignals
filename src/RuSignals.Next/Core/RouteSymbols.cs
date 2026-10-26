using System;
using System.Globalization;
namespace RuSignals;
public static partial class RouteSymbols
{
    private static readonly string[] Digits={"111101101101101101111","010110010010010010111","111001001111100100111","111001001111001001111","101101101111001001001","111100100111001001111","111100100111101101111","111001001010010010010","111101101111101101111","111101101111001001111"};
    private static readonly string[] Numbers=new string[100];
    private static readonly ulong[,] Drawings=new ulong[2,100];
    private static readonly ulong[,] Arrows=new ulong[2,3];
    static RouteSymbols()
    {
        for(int n=0;n<100;n++)Numbers[n]=n==0?"":n.ToString(CultureInfo.InvariantCulture);
        for(int size=0;size<2;size++)
        {
            for(int n=1;n<100;n++)
            {
                if(n<20){Drawings[size,n]=Original((n%10).ToString(CultureInfo.InvariantCulture),size!=0)|(n>=10?Original("10",size!=0):0);continue;}
                ulong mask=0;
                for(int row=0;row<7;row++)for(int col=0;col<6;col++)
                    if(Digits[col<3?n/10:n%10][row*3+col%3]=='1')mask|=1UL<<(row*6+col);
                Drawings[size,n]=mask;
            }
            Arrows[size,0]=Original("Left",size!=0);Arrows[size,2]=Original("Right",size!=0);
            for(int row=0;row<7;row++)Arrows[size,1]|=1UL<<(row*6+2);
        }
    }
    private static int Parse(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return 0;
        int n=0;bool started=false,ended=false;
        foreach(char c in text)
        {
            if(char.IsWhiteSpace(c)){if(started)ended=true;continue;}
            if(ended||c<'0'||c>'9')return 0;
            started=true;n=n*10+c-'0';if(n>99)return 0;
        }
        return n;
    }
    public static string Number(string text)=>Numbers[Parse(text)];
    public static ulong Mask(string text,bool small=false)
    {
        int size=small?1:0;
        if(text=="\\"||text=="Left")return Arrows[size,0];
        if(text=="|")return Arrows[size,1];
        if(text=="/"||text=="Right")return Arrows[size,2];
        return text=="-"?Original("-",small):Drawings[size,Parse(text)];
    }
}
