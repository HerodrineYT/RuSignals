using System.Linq;
// Capture the original palette in a separate Editor process. The current
// release is never loaded alongside the reference mod assembly.
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

public static class UnityPaletteBaselineCapture
{
    [Serializable] public sealed class Entry { public int family,tint;public int[] rgba_bits; }
    [Serializable] public sealed class Report { public string version,dll_sha256,scope;public Entry[] entries; }
    public static void Run()
    {
        try
        {
            string root=Environment.GetEnvironmentVariable("RUS_V2_ROOT"),game=Environment.GetEnvironmentVariable("RUS_V2_GAME"),sdk=Environment.GetEnvironmentVariable("RUS_V2_SDK");
            AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>
            {
                foreach(string dir in new[]{sdk,Path.Combine(game,"DerailValley_Data/Managed"),Path.Combine(game,"DerailValley_Data/Managed/UnityModManager")})
                {string path=Path.Combine(dir,new AssemblyName(args.Name).Name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path);}
                return null;
            };
            string version=Environment.GetEnvironmentVariable("RUS_PALETTE_REFERENCE_VERSION")??"3.0.11";
            int tintCount=version=="3.0.11"?32:96;
            string dll=Environment.GetEnvironmentVariable("RUS_PALETTE_REFERENCE_DLL");var assembly=Assembly.Load(File.ReadAllBytes(dll));
            var flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;var method=assembly.GetTypes().Single(t=>t.Name=="LensVariation").GetMethod("Colour",flags);
            var palette=assembly.GetTypes().Single(t=>t.Name=="LampPalette");var names=new[]{"Red","Yellow","Green","Blue","White"};var entries=new Entry[5*tintCount];
            for(int family=0;family<5;family++)for(int tint=0;tint<tintCount;tint++)
            {
                var source=(Color)palette.GetField(names[family],flags).GetValue(null);var colour=(Color)method.Invoke(null,new object[]{source,tint});
                entries[family*tintCount+tint]=new Entry{family=family,tint=tint,rgba_bits=new[]{Bits(colour.r),Bits(colour.g),Bits(colour.b),Bits(colour.a)}};
            }
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(dll))).Replace("-","").ToLowerInvariant();
            var report=new Report{version=version,dll_sha256=hash,entries=entries,scope="Original Colour method from the released "+version+" DLL, actual Unity/Mono; no current assembly loaded, no game or host started."};
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUS_V2_OUTPUT"),"palette-baseline-"+version+".json"),JsonUtility.ToJson(report,true));
            Debug.Log("RUS_PALETTE_BASELINE_PASS "+entries.Length+" original colours / separate process / "+hash);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);throw;}
    }
    private static int Bits(float value)=>BitConverter.ToInt32(BitConverter.GetBytes(value),0);
}
