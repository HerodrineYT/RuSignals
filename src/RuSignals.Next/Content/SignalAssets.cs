using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
namespace RuSignals;

// One owner per loaded pack. Every imported mesh/texture is shared, uploaded
// once, and released together; no instance owns a duplicate texture or glyph.
internal sealed partial class SignalAssets:IDisposable
{
    internal static SignalAssets Current{get;private set;}
    internal sealed class Part
    {
        internal string Name;internal Mesh Full,Compact,Tunnel,OvalFull,OvalCompact,OvalTunnel;internal Material Material;
        internal Vector2 UvMin,UvMax;internal Vector3 LensCentre;
        internal Bounds[] TunnelSections,OvalTunnelSections,FullSections;
    }
    private readonly string root;
    private readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
    private readonly Dictionary<string,Part[]> models=new Dictionary<string,Part[]>();
    private readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
    private readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
    private readonly Dictionary<(Color,Color,Vector2,Vector2,float,float),Material> glasses=new Dictionary<(Color,Color,Vector2,Vector2,float,float),Material>();
    private readonly Dictionary<(ulong,bool),Mesh> panels=new Dictionary<(ulong,bool),Mesh>();
    private readonly Dictionary<string,Sprite> icons=new Dictionary<string,Sprite>();
    private Mesh consoleMesh,boltMesh,glareMesh;private Material consoleMaterial,boltMaterial,glareMaterial;
    private readonly Shader glassShader,haloShader,structureShader;
    internal readonly Material PanelOff,PanelOn;
    internal SignalAssets(string directory)
    {
        root=directory;
        var bundle=AssetBundle.LoadFromFile(Path.Combine(root,"Shaders/ru_lenses"));
        if(bundle==null)throw new InvalidDataException("Lens shader bundle unavailable");
        try
        {
            glassShader=Own(bundle.LoadAsset<Shader>("Assets/RuGenerated/SignalGlass.shader"));haloShader=Own(bundle.LoadAsset<Shader>("Assets/RuGenerated/SignalHalo.shader"));structureShader=Own(bundle.LoadAsset<Shader>("Assets/RuGenerated/SignalStructure.shader"));if(glassShader==null||haloShader==null||structureShader==null)throw new InvalidDataException("Signal shader missing from bundle");
            foreach(string name in new[]{"t_signal.png","t_signal_new.png","t_signalfooting.png","semaphore_albedo.png"})
                CompiledTexture("Style/"+name,bundle.LoadAsset<Texture2D>("Assets/RuGenerated/Style/"+name));
            CompiledTexture("t_signalfooting.png",bundle.LoadAsset<Texture2D>("Assets/RuGenerated/Original/t_signalfooting.png"));
        }
        finally{bundle.Unload(false);}
        Current=this;
        PanelOff=Material("panel off",new Color(.025f,.026f,.028f));PanelOff.SetFloat("_Glossiness",.55f);
        PanelOn=Material("panel on",new Color(.9f,.94f,1));PanelOn.SetFloat("_Glossiness",.55f);
        PanelOn.EnableKeyword("_EMISSION");PanelOn.SetColor("_EmissionColor",new Color(.95f,.97f,1)*RouteBoardRules.Emission);
    }
    internal T Own<T>(T value)where T:UnityEngine.Object{owned.Add(value);return value;}
    private void CompiledTexture(string relative,Texture2D texture)
    {
        if(texture==null)throw new InvalidDataException("Compiled atlas missing: "+relative);
        Own(texture);texture.name=relative;texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=4;texture.wrapMode=relative.EndsWith("semaphore_albedo.png",StringComparison.Ordinal)?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
        textures.Add(relative+":"+false,texture);
    }
    internal Material Material(string name,Color colour)
    {
        var shader=Shader.Find("Standard");if(shader==null)throw new InvalidOperationException("Standard shader unavailable");
        return Own(new Material(shader){name=name,color=colour,enableInstancing=true});
    }
    internal Texture2D Texture(string relative,bool linear=false)
    {
        string key=relative+":"+linear;if(textures.TryGetValue(key,out var texture))return texture;
        var path=Path.GetFullPath(Path.Combine(root,relative));
        if(!path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Texture outside asset directory");
        texture=Own(new Texture2D(2,2,TextureFormat.RGBA32,true,linear));
        if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),true))throw new InvalidDataException("Invalid texture: "+relative);
        texture.name=relative;texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=4;
        texture.wrapMode=relative.StartsWith("lens_glass_",StringComparison.Ordinal)||relative.StartsWith("Semaphore",StringComparison.Ordinal)?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
        textures.Add(key,texture);return texture;
    }
    private Material Atlas(string file,bool cutout)
    {
        string key=file+":"+cutout;if(materials.TryGetValue(key,out var material))return material;
        material=Material(key,Color.white);material.mainTexture=Texture("Style/"+file);material.SetFloat("_Glossiness",.14f);
        if(cutout){material.shader=structureShader;material.SetTexture("_OriginalAlpha",Texture(file));material.SetFloat("_Cutoff",.5f);material.renderQueue=(int)RenderQueue.AlphaTest;}
        materials.Add(key,material);return material;
    }
    internal Part[] Model(string key)
    {
        if(models.TryGetValue(key,out var cached))return cached;
        var spec=Array.Find(RuModelCatalog.All,m=>m.Key==key);
        string relative=(spec!=null||key.StartsWith("Rounded/",StringComparison.Ordinal))?"SolidHoods/"+key:key;
        if(key.StartsWith("Rounded/",StringComparison.Ordinal))relative="SmoothShields/"+key;
        if(key.StartsWith("Semaphore/",StringComparison.Ordinal)&&File.Exists(Path.Combine(root,"SolidHoods/"+key+".rsm")))relative="SolidHoods/"+key;
        using(var input=new BinaryReader(File.OpenRead(Path.Combine(root,relative+".rsm"))))
        {
            if(Encoding.ASCII.GetString(input.ReadBytes(8))!="RUSMESH2")throw new InvalidDataException("Unsupported mesh: "+key);
            var result=new Part[Count(input,64)];
            for(int i=0;i<result.Length;i++)
            {
                string name=Text(input),atlas=Text(input);bool cutout=input.ReadInt32()!=0;
                int vc=Count(input,65535),ic=Count(input,1000000);if(ic%3!=0)throw new InvalidDataException("Invalid triangle count");
                var vertices=new Vector3[vc];var normals=new Vector3[vc];var uv=new Vector2[vc];
                var low=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var high=-low;
                for(int j=0;j<vc;j++){vertices[j]=Vector(input);normals[j]=Vector(input);uv[j]=new Vector2(Number(input),Number(input));low=Vector2.Min(low,uv[j]);high=Vector2.Max(high,uv[j]);}
                var indices=new int[ic];for(int j=0;j<ic;j++){indices[j]=input.ReadInt32();if((uint)indices[j]>=vc)throw new InvalidDataException("Invalid mesh index");}
                if(key=="Semaphore/semaphore_static")PlatformSupportFit.Apply(vertices,normals,indices);
                var mesh=Own(new Mesh{name=key+"/"+name,vertices=vertices,normals=normals,uv=uv,triangles=indices});mesh.RecalculateBounds();
                var centre=vertices[0];foreach(var vertex in vertices)if(vertex.z>centre.z)centre=vertex;
                result[i]=new Part{Name=name,Full=mesh,UvMin=low,UvMax=high,LensCentre=centre,Material=key.StartsWith("Semaphore/",StringComparison.Ordinal)?null:Atlas(atlas,cutout)};
                if(key.StartsWith("Rounded/",StringComparison.Ordinal))result[i].FullSections=TunnelFitGeometry.Sections(mesh);
            }
            if(input.BaseStream.Position!=input.BaseStream.Length)throw new InvalidDataException("Trailing mesh data");
            if(spec!=null&&spec.InstallationDepth>.5f)
            {
                float top=float.NegativeInfinity,lens=float.PositiveInfinity;
                foreach(var p in result)top=Mathf.Max(top,p.Full.bounds.max.y);foreach(var l in spec.Lenses)lens=Mathf.Min(lens,l.Position.y);
                float lower=ClearanceRules.Lowering(top,lens,spec.InstallationDepth,RouteBoardRules.ModelHasBoard(key)),head=lens-.55f;
                foreach(var part in result)
                {
                    var vertices=part.Full.vertices;var normals=part.Full.normals;
                    for(int j=0;j<vertices.Length;j++)
                    {
                        float y=vertices[j].y;vertices[j].y=ClearanceRules.Height(y,head,lower);
                        if(y>ClearanceRules.FoundationTop&&y<head)normals[j]=new Vector3(normals[j].x,normals[j].y/ClearanceRules.ShaftScale(head,lower),normals[j].z).normalized;
                    }
                    part.Compact=Own(new Mesh{name=part.Full.name+" compact",vertices=vertices,normals=normals,uv=part.Full.uv,triangles=part.Full.triangles});part.Compact.RecalculateBounds();
                    if(TunnelMountRules.IsHeadPart(part.Name))
                    {
                        part.Tunnel=part.Name.StartsWith("Body_",StringComparison.Ordinal)?Own(TunnelGeometry.Create(key,part.Name,part.Full,lower,lens)):part.Compact;
                    }
                    if(RoundedGeometry.Available(key)&&part.Name.EndsWith("_Signal",StringComparison.Ordinal))
                    {
                        part.OvalFull=Model("Rounded/"+key+"_full")[0].Full;part.OvalCompact=Model("Rounded/"+key+"_compact")[0].Full;
                        var oval=Model("Rounded/"+key+"_tunnel")[0];part.OvalTunnel=oval.Full;part.OvalTunnelSections=oval.FullSections;
                    }
                    if(!part.Name.StartsWith("Body_",StringComparison.Ordinal))part.Compact.RecalculateTangents();
                    if(part.Tunnel!=null)part.TunnelSections=TunnelFitGeometry.Sections(part.Tunnel);
                    if(part.Tunnel!=null&&part.Tunnel!=part.Compact)part.Tunnel.UploadMeshData(true);part.Compact.UploadMeshData(true);
                }
            }
            if(key=="route_pointer")foreach(var part in result){part.Tunnel=Own(TunnelGeometry.Create(key,part.Name,part.Full,0,0));part.TunnelSections=TunnelFitGeometry.Sections(part.Tunnel);part.Tunnel.UploadMeshData(true);}
            if(key=="exit_small_two_yellows")
            {
                var rounded=Model("RoundedDwarf/"+key);
                foreach(var part in result)
                {
                    var alternate=Array.Find(rounded,p=>p.Name==part.Name);
                    if(alternate==null)throw new InvalidDataException("Round dwarf part missing: "+part.Name);
                    part.OvalFull=part.OvalCompact=part.OvalTunnel=alternate.Full;
                }
            }
            foreach(var part in result){if(key.StartsWith("Semaphore/",StringComparison.Ordinal)||!part.Name.StartsWith("Body_",StringComparison.Ordinal))part.Full.RecalculateTangents();part.Full.UploadMeshData(true);}
            models.Add(key,result);return result;
        }
    }
    internal Material Glare()
    {
        if(glareMaterial!=null)return glareMaterial;
        glareMaterial=Own(new Material(haloShader){name="RUS lamp halo",enableInstancing=true});
        return glareMaterial;
    }
    internal Mesh GlareMesh()
    {
        if(glareMesh!=null)return glareMesh;
        glareMesh=Own(new Mesh{name="Shared depth tested lamp halo",vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)},normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward},uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,1,2,0,2,3}});
        glareMesh.RecalculateBounds();glareMesh.UploadMeshData(true);return glareMesh;
    }
    internal MeshRenderer PutPart(Transform parent,Part part,Material material=null,string name=null)
    {
        var go=new GameObject(name??part.Name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=part.Full;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material??part.Material;return renderer;
    }
    internal void PutModel(Transform parent,string key){foreach(var part in Model(key))PutPart(parent,part);}
    internal Transform ConsolePart(Transform parent,string name)
    {
        if(consoleMesh==null)
        {
            var vertices=new Vector3[24];var normals=new Vector3[24];var indices=new int[36];
            var sides=new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            for(int f=0;f<sides.Length;f++)
            {
                var n=sides[f];var u=Mathf.Abs(n.y)>.5f?Vector3.right:Vector3.up;var v=Vector3.Cross(n,u);var c=n*.5f;int i=f*4,t=f*6;
                vertices[i]=c-(u+v)*.5f;vertices[i+1]=c+(u-v)*.5f;vertices[i+2]=c+(u+v)*.5f;vertices[i+3]=c+(-u+v)*.5f;
                for(int j=0;j<4;j++)normals[i+j]=n;
                indices[t]=i;indices[t+1]=i+1;indices[t+2]=i+2;indices[t+3]=i;indices[t+4]=i+2;indices[t+5]=i+3;
            }
            consoleMesh=Own(new Mesh{name="Shared tunnel console tube",vertices=vertices,normals=normals,triangles=indices});consoleMesh.RecalculateBounds();consoleMesh.UploadMeshData(true);
            consoleMaterial=Material("Tunnel console steel",new Color(.20f,.22f,.23f));consoleMaterial.SetFloat("_Metallic",.65f);consoleMaterial.SetFloat("_Glossiness",.28f);
        }
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=consoleMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial=consoleMaterial;return go.transform;
    }
    internal Transform ConsoleBolts(Transform parent,string name)
    {
        if(boltMesh==null)
        {
            boltMesh=Own(AnchorBoltGeometry.Create());boltMesh.UploadMeshData(true);
            boltMaterial=Material("Wall anchor galvanised steel",new Color(.52f,.54f,.55f));boltMaterial.SetFloat("_Metallic",.85f);boltMaterial.SetFloat("_Glossiness",.42f);
        }
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=boltMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial=boltMaterial;return go.transform;
    }
    internal Material Glass(string name,Color colour,Vector2 min,Vector2 max,bool lit=false,float smoothness=.81f,float metallic=.12f,Color? tint=null)
    {
        var span=max-min;
        if(span.x<=0||span.y<=0)throw new InvalidDataException("Degenerate lens UVs");
        var scale=new Vector2(1/span.x,1/span.y);var offset=-Vector2.Scale(min,scale);
        Color glassTint=tint??Color.Lerp(new Color(.022f,.026f,.030f),colour,.04f),emission=lit?colour*1.5f:Color.black;
        var key=(glassTint,emission,scale,offset,smoothness,metallic);
        if(glasses.TryGetValue(key,out var material))return material;
        material=Own(new Material(glassShader){name=name,color=glassTint,enableInstancing=true});
        material.mainTexture=Texture2D.whiteTexture;material.mainTextureScale=scale;material.mainTextureOffset=offset;
        material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",emission);material.SetFloat("_Glossiness",smoothness);material.SetFloat("_Metallic",metallic);glasses.Add(key,material);return material;
    }
    internal Mesh PanelMesh(ulong mask,bool small)
    {
        var key=(mask,small);if(panels.TryGetValue(key,out var cached))return cached;
        var positions=small?RouteLamps.route_pointer_small:RouteLamps.route_pointer;const int sides=16,stride=sides+1;
        var vertices=new Vector3[positions.Length*stride];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
        var on=new List<int>();var off=new List<int>();
        for(int i=0;i<positions.Length;i++)
        {
            int first=i*stride;var p=positions[i];var center=new Vector3(p.x,p.y,p.z+.005f);vertices[first]=center;
            var indices=(mask&(1UL<<i))==0?off:on;
            for(int j=0;j<=sides;j++)normals[first+j]=Vector3.forward;
            for(int j=0;j<sides;j++)
            {
                double angle=j*2*Math.PI/sides;vertices[first+j+1]=center+new Vector3((float)Math.Cos(angle)*.034f,(float)Math.Sin(angle)*.034f,0);
                indices.Add(first);indices.Add(first+j+1);indices.Add(first+(j+1)%sides+1);
            }
        }
        var mesh=Own(new Mesh{name="RUS panel "+mask,vertices=vertices,normals=normals,uv=uv,subMeshCount=2});
        mesh.SetTriangles(off,0);mesh.SetTriangles(on,1);mesh.RecalculateBounds();mesh.UploadMeshData(true);panels.Add(key,mesh);return mesh;
    }
    internal Sprite RouteTextIcon()
    {
        const string key="route-text";if(icons.TryGetValue(key,out var icon))return icon;
        // Native HUD requires a sprite even for text. Keep the original row size so the native stack stays aligned.
        const int side=128;var texture=Own(new Texture2D(side,side,TextureFormat.RGBA32,false));
        texture.SetPixels32(new Color32[side*side]);texture.Apply(false,true);
        icon=Own(Sprite.Create(texture,new Rect(0,0,side,side),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect));
        icons.Add(key,icon);return icon;
    }
    internal Sprite Icon(Indication indication,bool shunting=false)
    {
        string key=indication+":"+shunting;if(icons.TryGetValue(key,out var cached))return cached;
        var palette=LampPalette.Hud(indication);
        if(shunting&&indication==Indication.Stop)palette[0]=LampPalette.Blue;
        bool blinking=indication==Indication.FlashingYellow||indication==Indication.DoubleYellowUpperFlashing||indication==Indication.Invitation;
        return DrawIcon(key,palette,blinking?(indication==Indication.Invitation?1:0):-1);
    }
    internal Sprite MechanicalIcon(SignalRole role,Indication indication,bool twoArms)
    {
        var state=SemaphoreRules.Pose(role,indication,false,twoArms);
        if(state==SovietSemaphore.SemaphoreState.Main)return Icon(Indication.Green);
        if(state==SovietSemaphore.SemaphoreState.Diverging)return DrawIcon("semaphore-diverging",new[]{LampPalette.Green,LampPalette.Yellow},-1);
        return Icon(Indication.Dark);
    }
    private Sprite DrawIcon(string key,Color[] palette,int blinking)
    {
        if(icons.TryGetValue(key,out var icon))return icon;
        const int side=128;var pixels=new Color[side*side];
        for(int i=0;i<palette.Length;i++)
        {
            float cy=palette.Length==1?63.5f:94-60*i,radius=palette.Length==1?36:23;
            for(int y=0;y<side;y++)for(int x=0;x<side;x++)
            {
                float dx=x-63.5f,dy=y-cy,d=dx*dx+dy*dy;
                bool flash=i==blinking;
                if(d<radius*radius||(flash&&d>(radius+4)*(radius+4)&&d<(radius+8)*(radius+8)&&((int)((Math.Atan2(dy,dx)+Math.PI)*12/Math.PI)&1)==0))pixels[y*side+x]=palette[i];
            }
        }
        var texture=Own(new Texture2D(side,side,TextureFormat.RGBA32,false));texture.SetPixels(pixels);texture.Apply(false,true);
        icon=Own(Sprite.Create(texture,new Rect(0,0,side,side),new Vector2(.5f,.5f)));icons.Add(key,icon);return icon;
    }
    private static int Count(BinaryReader reader,int max){int n=reader.ReadInt32();if(n<=0||n>max)throw new InvalidDataException("Invalid mesh count");return n;}
    private static string Text(BinaryReader reader){int n=Count(reader,1024);var b=reader.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return Encoding.UTF8.GetString(b);}
    private static float Number(BinaryReader reader){float n=reader.ReadSingle();if(float.IsNaN(n)||float.IsInfinity(n))throw new InvalidDataException("Non-finite vertex");return n;}
    private static Vector3 Vector(BinaryReader reader)=>new Vector3(Number(reader),Number(reader),Number(reader));
    public void Dispose(){Font.textureRebuilt-=NameAtlasChanged;foreach(var item in owned)if(item!=null)UnityEngine.Object.Destroy(item);owned.Clear();models.Clear();textures.Clear();materials.Clear();glasses.Clear();panels.Clear();icons.Clear();if(ReferenceEquals(Current,this))Current=null;}
}
