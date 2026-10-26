using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void BrandingChecks(Assembly assembly,string root)
    {
        int start=passed;
        Check(assembly.GetName().Name=="RuSignals","Main assembly uses the public mod name");
        Check(assembly.GetTypes().All(t=>t.Namespace==null||t.Namespace=="RuSignals"||t.Namespace.StartsWith("RuSignals.",StringComparison.Ordinal)||t.Namespace=="SovietSemaphore"||t.Namespace=="Microsoft.CodeAnalysis"||t.Namespace=="System.Runtime.CompilerServices"),"Production namespaces use the public mod name or the existing semaphore and compiler namespaces");
        var mod=assembly.GetType("RuSignals.Mod",true);
        Check((string)mod.GetField("Id",All).GetRawConstantValue()=="ru.signals","Pack registration and Harmony ownership use the new ID");
        Check(mod.GetMethod("Load",All)!=null,"Declared entry point resolves");
        var bundle=AssetBundle.LoadFromFile(Path.Combine(root,"src/RuSignals.Next/Assets/Shaders/ru_lenses"));
        Check(bundle!=null,"Renamed shader bundle loads");
        try
        {
            var names=bundle.GetAllAssetNames();
            Check(names.Length==8&&names.All(n=>n.StartsWith("assets/rugenerated/",StringComparison.Ordinal)),"All eight shader and texture bundle entries use renamed paths");
            foreach(var name in names.Where(n=>n.EndsWith(".shader",StringComparison.Ordinal)))
            {
                var shader=bundle.LoadAsset<Shader>(name);
                Check(shader!=null&&shader.isSupported&&shader.name.StartsWith("RuSignals/",StringComparison.Ordinal),"Renamed shader is supported: "+name);
            }
        }
        finally{bundle.Unload(true);}
        Debug.Log("RUS_BRANDING_PASS "+(passed-start)+" / assembly / namespaces / ID / entry / bundle / shaders");
    }
}
