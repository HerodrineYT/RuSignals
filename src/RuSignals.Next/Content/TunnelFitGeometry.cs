using System.Collections.Generic;
using UnityEngine;
namespace RuSignals;
// Horizontal sections follow the actual diamond/oval contour. A single box
// would falsely occupy its empty upper corners in a curved tunnel. Derived
// once while importing; original vertices, UVs and render meshes are untouched.
internal static class TunnelFitGeometry
{
    internal static Bounds[] Sections(Mesh mesh)
    {
        var whole=mesh.bounds;if(whole.size.y<.35f)return new[]{whole};
        const int bands=8;
        var vertices=mesh.vertices;var indices=mesh.triangles;var result=new List<Bounds>(bands);
        for(int band=0;band<bands;band++)
        {
            float low=Mathf.Lerp(whole.min.y,whole.max.y,(float)band/bands),high=Mathf.Lerp(whole.min.y,whole.max.y,(float)(band+1)/bands);
            bool found=false;var box=new Bounds();
            void Add(Vector3 point){if(!found){box=new Bounds(point,Vector3.zero);found=true;}else box.Encapsulate(point);}
            void Edge(Vector3 a,Vector3 b)
            {
                if(a.y>=low&&a.y<=high)Add(a);
                float dy=b.y-a.y;if(Mathf.Abs(dy)<.000001f)return;
                float t=(low-a.y)/dy;if(t>=0&&t<=1)Add(Vector3.LerpUnclamped(a,b,t));
                t=(high-a.y)/dy;if(t>=0&&t<=1)Add(Vector3.LerpUnclamped(a,b,t));
            }
            for(int i=0;i<indices.Length;i+=3)
            {var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];Edge(a,b);Edge(b,c);Edge(c,a);}
            if(found)result.Add(box);
        }
        return result.ToArray();
    }
}
