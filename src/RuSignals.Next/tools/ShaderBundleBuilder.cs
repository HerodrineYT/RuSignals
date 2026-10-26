using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class ShaderBundleBuilder
{
    public static void Build()
    {
        string root=Environment.GetEnvironmentVariable("RUS_V2_ROOT");
        var shaders=new[]{"Assets/RuGenerated/SignalGlass.shader","Assets/RuGenerated/SignalHalo.shader","Assets/RuGenerated/SignalStructure.shader"};
        var assets=new System.Collections.Generic.List<string>(shaders);
        foreach(var asset in shaders)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(asset));File.Copy(Path.Combine(root,"src/RuSignals.Next/Content",Path.GetFileName(asset)),asset,true);
            AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceUpdate);var shader=AssetDatabase.LoadAssetAtPath<Shader>(asset);
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Lamp shader compilation failed: "+asset);
        }
        foreach(string name in new[]{"t_signal.png","t_signal_new.png","t_signalfooting.png","semaphore_albedo.png"})
        {
            string asset="Assets/RuGenerated/Style/"+name;Directory.CreateDirectory(Path.GetDirectoryName(asset));
            File.Copy(Path.Combine(root,"src/RuSignals.Next/Assets/Style",name),asset,true);
            AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(asset);
            importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.None;
            importer.isReadable=false;importer.mipmapEnabled=true;importer.maxTextureSize=1024;importer.npotScale=TextureImporterNPOTScale.ToNearest;
            importer.textureCompression=TextureImporterCompression.Compressed;importer.filterMode=FilterMode.Trilinear;importer.wrapMode=name=="semaphore_albedo.png"?TextureWrapMode.Clamp:TextureWrapMode.Repeat;importer.anisoLevel=4;
            importer.SaveAndReimport();assets.Add(asset);
        }
        string mask="Assets/RuGenerated/Original/t_signalfooting.png";Directory.CreateDirectory(Path.GetDirectoryName(mask));
        File.Copy(Path.Combine(root,"src/RuSignals.Next/Assets/t_signalfooting.png"),mask,true);AssetDatabase.ImportAsset(mask,ImportAssetOptions.ForceUpdate);
        var alpha=(TextureImporter)AssetImporter.GetAtPath(mask);alpha.textureType=TextureImporterType.Default;alpha.isReadable=false;alpha.mipmapEnabled=true;
        alpha.sRGBTexture=true;alpha.alphaSource=TextureImporterAlphaSource.FromInput;alpha.textureCompression=TextureImporterCompression.Uncompressed;
        alpha.filterMode=FilterMode.Trilinear;alpha.wrapMode=TextureWrapMode.Repeat;alpha.anisoLevel=4;alpha.SaveAndReimport();assets.Add(mask);
        string output=Path.Combine(root,"work/ru-v2/shader-bundle");Directory.CreateDirectory(output);
        var manifest=BuildPipeline.BuildAssetBundles(output,new[]{new AssetBundleBuild{assetBundleName="ru_lenses",assetNames=assets.ToArray()}},BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64);
        if(manifest==null)throw new InvalidOperationException("Lens shader bundle failed");
        var probe=AssetBundle.LoadFromFile(Path.Combine(output,"ru_lenses"));
        if(probe==null)throw new InvalidOperationException("Built bundle cannot be loaded; enable com.unity.modules.assetbundle in the Editor project");
        try
        {
            foreach(var asset in shaders){var compiled=probe.LoadAsset<Shader>(asset);if(compiled==null||!compiled.isSupported)throw new InvalidOperationException("Compiled lamp shader is unsupported: "+asset);}
            foreach(var asset in assets)if(asset.EndsWith(".png",StringComparison.Ordinal))
            {
                var texture=probe.LoadAsset<Texture2D>(asset);
                if(texture==null||texture.width!=1024||texture.height!=1024||texture.isReadable)throw new InvalidOperationException("Invalid compiled shared atlas: "+asset);
                Debug.Log("RUS_ATLAS_PASS "+asset+" / "+texture.format+" / 1024 / mipmaps / no CPU copy");
            }
        }
        finally{probe.Unload(true);}
        string destination=Path.Combine(root,"src/RuSignals.Next/Assets/Shaders/ru_lenses");Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(Path.Combine(output,"ru_lenses"),destination,true);
        Debug.Log("RUS_LENS_SHADER_PASS four optical rings / Windows64 / "+new FileInfo(destination).Length+" bytes");
    }
}
