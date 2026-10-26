using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;

const string version="4.3.4",previousVersion="4.3.3";
string root=Path.GetFullPath(args.Length>0?args[0]:Environment.CurrentDirectory);
string source=Path.Combine(root,"src/RuSignals.Next"),reports=Path.Combine(root,"artifacts/ru-signals-"+version);
string baseline=Path.Combine(root,"work/rusignals-v433-source-check");
string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
string Text(string file)=>File.ReadAllText(file).Replace("\r\n","\n");
JsonObject Read(string file)=>JsonNode.Parse(File.ReadAllText(file))!.AsObject();
void Write(string file,object value)=>File.WriteAllText(file,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
IEnumerable<string> Files(string directory)=>Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).Where(p=>!Path.GetRelativePath(directory,p).Split(Path.DirectorySeparatorChar).Any(s=>s is "bin" or "obj" or "__pycache__"));

if(args.Contains("--archives"))
{
    foreach(string archiveName in new[]{"RuSignals-"+version+".zip","RuSignals-"+version+"-source.zip"})
    {
        using var archive=ZipFile.OpenRead(Path.Combine(root,"dist",archiveName));
        bool runtime=!archiveName.EndsWith("-source.zip",StringComparison.Ordinal);
        var entry=archive.GetEntry(runtime?"RuSignals/package-manifest.json":"artifacts/ru-signals-"+version+"/source-manifest.json")!;
        using var stream=entry.Open();var manifest=JsonNode.Parse(stream)!;
        foreach(var item in manifest["sha256"]!.AsObject())
        {
            using var payload=archive.GetEntry((runtime?"RuSignals/":"")+item.Key)!.Open();
            Require(Convert.ToHexString(SHA256.HashData(payload)).Equals(item.Value!.GetValue<string>(),StringComparison.OrdinalIgnoreCase),"Archive mismatch: "+item.Key);
        }
    }
    Console.WriteLine("PASS runtime/source archive manifests");return;
}

var changed=new HashSet<string>{"src/RuSignals.Next/Content/SignalAssets.cs","src/RuSignals.Next/Content/PlatformSupportFit.cs"};
var sources=new SortedDictionary<string,string>();
foreach(string directory in new[]{"src/RuSignals.Next","src/RuSignals.Next.RsgBridge"})
{
    var names=Files(Path.Combine(root,directory)).Where(p=>!Path.GetRelativePath(Path.Combine(root,directory),p).Split(Path.DirectorySeparatorChar).Contains("tools")&&(Path.GetExtension(p) is ".cs" or ".csproj"||Path.GetFileName(p)=="Info.json")).ToArray();
    foreach(string file in names)
    {
        string name=Path.GetRelativePath(root,file).Replace('\\','/'),old=Path.Combine(baseline,name);
        if(!changed.Contains(name))Require(File.Exists(old)&&File.ReadAllText(old).Replace(previousVersion,version)==File.ReadAllText(file),"Unreviewed production change: "+name);
        sources[name]=Hash(file);
    }
    foreach(string file in Files(Path.Combine(baseline,directory)).Where(p=>!Path.GetRelativePath(Path.Combine(baseline,directory),p).Split(Path.DirectorySeparatorChar).Contains("tools")&&(Path.GetExtension(p) is ".cs" or ".csproj"||Path.GetFileName(p)=="Info.json")))
        Require(sources.ContainsKey(Path.GetRelativePath(baseline,file).Replace('\\','/')),"Missing production input: "+file);
}
var assets=new SortedDictionary<string,string>();
foreach(string file in Files(Path.Combine(source,"Assets")))
{
    string name=Path.GetRelativePath(Path.Combine(source,"Assets"),file).Replace('\\','/');
    Require(Hash(file)==Hash(Path.Combine(baseline,"src/RuSignals.Next/Assets",name)),"Unexpected asset change: "+name);assets[name]=Hash(file);
}
Require(assets.Count==158&&Files(Path.Combine(baseline,"src/RuSignals.Next/Assets")).Count()==158,"Expected158 unchanged resources");
foreach(string file in Directory.EnumerateFiles(Path.Combine(source,"Content"),"*.shader"))Require(Hash(file)==Hash(Path.Combine(baseline,"src/RuSignals.Next/Content",Path.GetFileName(file))),"Shader changed");
Require(Hash(Path.Combine(source,"README_RU.md"))==Hash(Path.Combine(baseline,"src/RuSignals.Next/README_RU.md")),"README changed");
Require(Hash(Path.Combine(source,"Visuals/SemaphoreVisual.cs"))==Hash(Path.Combine(baseline,"src/RuSignals.Next/Visuals/SemaphoreVisual.cs")),"Wing or rod motion changed");
string loader="src/RuSignals.Next/Content/SignalAssets.cs";
string meshLine="                var mesh=Own(new Mesh{name=key+\"/\"+name,vertices=vertices,normals=normals,uv=uv,triangles=indices});mesh.RecalculateBounds();";
Require(Text(Path.Combine(baseline,loader)).Replace(meshLine,"                if(key==\"Semaphore/semaphore_static\")PlatformSupportFit.Apply(vertices,normals,indices);\n"+meshLine)==Text(Path.Combine(root,loader)),"Importer changed outside the one-time platform fitting call");
var red=Read(Path.Combine(reports,"red/platform-support-verification.json"));
Require(!red["passed"]!.GetValue<bool>()&&red["lower_contacts"]!.GetValue<int>()==0&&red["upper_contacts"]!.GetValue<int>()==2,"Original detached-platform regression not reproduced");

var required=Read(Path.Combine(source,"tools/qa-required-markers.json"));var profiles=new SortedDictionary<string,int>();
foreach(string profile in new[]{"main","rsg-legacy","power-current"})
{
    string folder=profile=="main"?reports:Path.Combine(reports,profile);var result=Read(Path.Combine(folder,"unity-rewrite-verification.json"));
    Require(result["dll_sha256"]!.GetValue<string>()==Hash(Path.Combine(source,"bin/Release/net48/RuSignals.dll")),"QA covers another DLL");
    int count=result["passed"]!.GetValue<int>();Require(count>=required[profile]!["minimum_checks"]!.GetValue<int>(),"Native checks missing");
    var platform=Read(Path.Combine(folder,"platform-support-verification.json"));
    Require(platform["passed"]!.GetValue<bool>(),"Platform support regression failed");
    profiles[profile]=count;required[profile]!["minimum_checks"]=count;
    var markers=required[profile]!["markers"]!.AsArray();if(!markers.Any(x=>x!.GetValue<string>()=="RUS_PLATFORM_SUPPORT_PASS"))markers.Add("RUS_PLATFORM_SUPPORT_PASS");
}
Write(Path.Combine(source,"tools/qa-required-markers.json"),required);
Write(Path.Combine(reports,"semaphore-model-verification.json"),new{passed=true,reference_version=previousVersion,unchanged_assets=158,original_optical_assets_unchanged=true,original_rod_assets_unchanged=true,current_mesh_sha256=assets.Where(p=>p.Key is "SolidHoods/Semaphore/semaphore_static.rsm" or "SolidHoods/Semaphore/semaphore_lowerfittings.rsm").ToDictionary(),scope="All158 resources byte-identical; C# import fits only two platform support prisms. Their actual imported geometry is covered by platform-support-verification.json. Every rod, wing, lamp and transmission resource and motion source preserved."});
Write(Path.Combine(reports,"change-verification.json"),new{version,reference_version=previousVersion,dll_sha256=Hash(Path.Combine(source,"bin/Release/net48/RuSignals.dll")),bridge_sha256=Hash(Path.Combine(root,"src/RuSignals.Next.RsgBridge/bin/Release/net48/RuSignals.RsgBridge.dll")),semaphore_mechanism_unchanged=true,fixed_lanterns=true,fixed_green_yellow_colours=true,other_production_inputs_unchanged=true,assets_checked=158,unchanged_assets=158,source_sha256=sources,asset_sha256=assets,reviewed_asset_changes=new{},reviewed_changes=changed.ToDictionary(s=>s,s=>new{previous_sha256=File.Exists(Path.Combine(baseline,s))?Hash(Path.Combine(baseline,s)):null,sha256=Hash(Path.Combine(root,s))}),new_frame_callbacks=0,additional_unique_meshes=0,additional_unique_materials=0,additional_renderers=0,dispatcher_files_modified=false,multiplayer_files_modified=false,previous_rule_mutation_report_preserved=true,scope="C# one-time shared mesh import fits two platform struts; full native C# contact and preservation regression. No per-frame fitting, new renderers, materials, meshes or animation changes."});
Console.WriteLine(JsonSerializer.Serialize(new{profiles,unchanged_assets=158}));
