using System.Text;
using UnityEngine;
namespace RuSignals;

public sealed class NameplateView:MonoBehaviour
{
    public TextMesh Letters;public MeshFilter Board;public Mesh[] VerticalMeshes;public Mesh HorizontalMesh;
    public bool Vertical;
    public float LineStep,CellHeight;
    public string DisplayedName=>shown;
    private string shown;
    public void SetVertical(bool vertical)
    {
        if(Vertical==vertical)return;
        Vertical=vertical;string text=shown;shown=null;Show(text??string.Empty);
    }
    public void Show(string value)
    {
        if(shown==value)return;shown=value;
        string text=value??string.Empty;int rows=1;
        if(Vertical)
        {
            // Long native names use at most six rows to keep the sign clear.
            int perRow=Mathf.Max(1,(text.Length+5)/6);var lines=new StringBuilder();
            for(int i=0;i<text.Length;i++){if(i>0&&i%perRow==0){lines.Append('\n');rows++;}lines.Append(text[i]);}
            text=lines.ToString();Board.sharedMesh=VerticalMeshes[rows-1];
        }
        else Board.sharedMesh=HorizontalMesh;
        var font=Letters.font;font.RequestCharactersInTexture(text,Letters.fontSize,Letters.fontStyle);
        int width=0,line=0,top=0,bottom=0;
        foreach(char c in text)
        {
            if(c=='\n'){width=Mathf.Max(width,line);line=0;continue;}
            if(font.GetCharacterInfo(c,out var glyph,Letters.fontSize,Letters.fontStyle))
            {line+=glyph.advance;top=Mathf.Max(top,glyph.maxY);bottom=Mathf.Min(bottom,glyph.minY);}
        }
        width=Mathf.Max(width,line);float unit=Letters.characterSize*.1f;
        float height=(top-bottom)*unit;
        float scale=Mathf.Min(1,Mathf.Min((Vertical?.17f:.46f)/Mathf.Max(width*unit,.001f),(Vertical?.20f:.19f)/Mathf.Max(height,.001f)));
        // Each line keeps the board's 0.28 m pitch even when a long name
        // scales down. UpperCenter fixes the first cell, independent of rows.
        Letters.lineSpacing=Vertical?.28f/(LineStep*scale):1;
        Letters.text=text;
        Letters.transform.localScale=Vector3.one*scale;
        Letters.transform.localPosition=new Vector3(0,-.135f+CellHeight*scale*.5f,.010f);
    }
}
