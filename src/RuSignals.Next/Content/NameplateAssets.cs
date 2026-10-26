using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace RuSignals;

internal sealed partial class SignalAssets
{
    private Mesh horizontalPlate;private Mesh[] verticalPlates;
    private Material plateEnamel,plateRim,nameInk;private Font nameFont;
    private float nameLineStep,nameCellHeight;
    internal NameplateView Nameplate(Transform parent,bool vertical)
    {
        if(horizontalPlate==null)
        {
            horizontalPlate=PlateMesh(false,1);verticalPlates=new Mesh[6];
            for(int i=0;i<verticalPlates.Length;i++)verticalPlates[i]=PlateMesh(true,i+1);
            plateRim=Material("Name plate black rim",new Color(.025f,.029f,.032f));plateRim.SetFloat("_Glossiness",.15f);
            plateEnamel=Material("Name plate white enamel",new Color(.83f,.82f,.76f));plateEnamel.SetFloat("_Glossiness",.25f);
            nameFont=Resources.GetBuiltinResource<Font>("Arial.ttf");
            var alphabet=new System.Text.StringBuilder();for(char c=' ';c<='~';c++)alphabet.Append(c);for(char c='\u0400';c<='\u045f';c++)alphabet.Append(c);
            nameFont.RequestCharactersInTexture(alphabet.ToString(),64,FontStyle.Bold);
            // Unity's unrounded font line advance differs from Font.lineHeight.
            // Measure the native TextMesh layout once, before world placement.
            var probe=new GameObject("Name lettering metrics");var sample=probe.AddComponent<TextMesh>();
            sample.font=nameFont;sample.fontSize=64;sample.fontStyle=FontStyle.Bold;sample.characterSize=.040f;sample.anchor=TextAnchor.UpperCenter;
            var sampleRenderer=sample.GetComponent<Renderer>();sampleRenderer.enabled=false;
            sample.text="M";nameCellHeight=sampleRenderer.bounds.size.y;
            sample.text="M\nM";nameLineStep=sampleRenderer.bounds.size.y-nameCellHeight;
            UnityEngine.Object.Destroy(probe);
            if(nameLineStep<=0||nameCellHeight<=0)throw new System.InvalidOperationException("Native font layout unavailable");
            nameInk=Material("Shared depth-tested name lettering",new Color(.018f,.020f,.022f));nameInk.mainTexture=nameFont.material.mainTexture;
            nameInk.SetFloat("_Mode",1);nameInk.SetFloat("_Cutoff",.3f);nameInk.SetFloat("_Glossiness",0);nameInk.EnableKeyword("_ALPHATEST_ON");nameInk.SetOverrideTag("RenderType","TransparentCutout");nameInk.renderQueue=(int)RenderQueue.AlphaTest;
            Font.textureRebuilt+=NameAtlasChanged;
        }
        var go=new GameObject("Signal designation plates");go.transform.SetParent(parent,false);
        var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=vertical?verticalPlates[0]:horizontalPlate;
        go.AddComponent<MeshRenderer>().sharedMaterials=new[]{plateRim,plateEnamel};
        var text=new GameObject("Native signal name");text.transform.SetParent(go.transform,false);text.transform.localPosition=new Vector3(0,-.135f,.010f);text.transform.localRotation=Quaternion.Euler(0,180,0);
        var letters=text.AddComponent<TextMesh>();letters.font=nameFont;letters.fontSize=64;letters.characterSize=.040f;letters.lineSpacing=1;letters.fontStyle=FontStyle.Bold;letters.anchor=TextAnchor.UpperCenter;letters.alignment=TextAlignment.Center;letters.color=Color.white;
        var renderer=text.GetComponent<MeshRenderer>();renderer.sharedMaterial=nameInk;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        var view=go.AddComponent<NameplateView>();view.Letters=letters;view.Board=filter;view.Vertical=vertical;view.VerticalMeshes=verticalPlates;view.HorizontalMesh=horizontalPlate;view.LineStep=nameLineStep;view.CellHeight=nameCellHeight;return view;
    }
    private Mesh PlateMesh(bool vertical,int rows)
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var rim=new List<int>();var enamel=new List<int>();
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 n,List<int> indices)
        {int at=vertices.Count;vertices.AddRange(new[]{a,b,c,d});for(int i=0;i<4;i++)normals.Add(n);indices.AddRange(new[]{at,at+1,at+2,at,at+2,at+3});}
        float half=vertical?.11f:.27f;
        for(int row=0;row<rows;row++)
        {
            float top=-row*.28f,bottom=top-.27f;
            var a=new Vector3(-half,bottom,0);var b=new Vector3(half,bottom,0);var c=new Vector3(half,top,0);var d=new Vector3(-half,top,0);
            var back=Vector3.back*.02f;
            Quad(a,b,c,d,Vector3.forward,rim);Quad(b+back,a+back,d+back,c+back,Vector3.back,rim);
            Quad(a+back,b+back,b,a,Vector3.down,rim);Quad(d,c,c+back,d+back,Vector3.up,rim);
            Quad(b+back,c+back,c,b,Vector3.right,rim);Quad(a+back,a,d,d+back,Vector3.left,rim);
            Quad(new Vector3(-half+.008f,bottom+.008f,.004f),new Vector3(half-.008f,bottom+.008f,.004f),new Vector3(half-.008f,top-.008f,.004f),new Vector3(-half+.008f,top-.008f,.004f),Vector3.forward,enamel);
            // Two fastener heads per enamel plate, in the same draw call.
            foreach(float y in new[]{top-.018f,bottom+.018f})
                Quad(new Vector3(-.004f,y-.004f,.006f),new Vector3(.004f,y-.004f,.006f),new Vector3(.004f,y+.004f,.006f),new Vector3(-.004f,y+.004f,.006f),Vector3.forward,rim);
            // Mounting lugs share the rim mesh and physically enter the pole.
            foreach(float y in new[]{top-.04f,bottom+.04f})
            {
                float x=.035f,h=.012f,z=-.06f;
                Quad(new Vector3(x,y-h,z),new Vector3(-x,y-h,z),new Vector3(-x,y+h,z),new Vector3(x,y+h,z),Vector3.back,rim);
                Quad(new Vector3(-x,y+h,z),new Vector3(x,y+h,z),new Vector3(x,y+h,-.02f),new Vector3(-x,y+h,-.02f),Vector3.up,rim);
                Quad(new Vector3(-x,y-h,-.02f),new Vector3(x,y-h,-.02f),new Vector3(x,y-h,z),new Vector3(-x,y-h,z),Vector3.down,rim);
                Quad(new Vector3(-x,y-h,z),new Vector3(-x,y+h,z),new Vector3(-x,y+h,-.02f),new Vector3(-x,y-h,-.02f),Vector3.left,rim);
                Quad(new Vector3(x,y-h,-.02f),new Vector3(x,y+h,-.02f),new Vector3(x,y+h,z),new Vector3(x,y-h,z),Vector3.right,rim);
            }
        }
        var mesh=Own(new Mesh{name=vertical?"Shared vertical designation / "+rows:"Shared horizontal designation",vertices=vertices.ToArray(),normals=normals.ToArray(),subMeshCount=2});
        mesh.SetTriangles(rim,0);mesh.SetTriangles(enamel,1);mesh.RecalculateBounds();mesh.UploadMeshData(true);return mesh;
    }
    private void NameAtlasChanged(Font font){if(font==nameFont&&nameInk!=null)nameInk.mainTexture=font.material.mainTexture;}
}
