using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuSignals;

internal static class PlatformSupportFit
{
    private static Vector3 Key(Vector3 v)=>new Vector3(Mathf.Round(v.x*100000),Mathf.Round(v.y*100000),Mathf.Round(v.z*100000));
    private sealed class Piece
    {
        internal readonly List<int> Vertices=new List<int>();
        internal readonly List<int> Faces=new List<int>();
        internal Bounds Bounds;
    }

    // Runs on the shared mesh arrays once, before their CPU copy is released.
    internal static int Apply(Vector3[] vertices,Vector3[] normals,int[] triangles)
    {
        var parent=new int[vertices.Length];var welded=new Dictionary<Vector3,int>();
        for(int i=0;i<parent.Length;i++)parent[i]=i;
        int Find(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        void Join(int a,int b){parent[Find(a)]=Find(b);}
        for(int i=0;i<vertices.Length;i++)
        {
            var key=Key(vertices[i]);if(welded.TryGetValue(key,out int other))Join(i,other);else welded.Add(key,i);
        }
        for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i],triangles[i+2]);}
        var pieces=new Dictionary<int,Piece>();
        for(int i=0;i<vertices.Length;i++)
        {
            int root=Find(i);
            if(!pieces.TryGetValue(root,out var piece)){piece=new Piece{Bounds=new Bounds(vertices[i],Vector3.zero)};pieces.Add(root,piece);}
            piece.Vertices.Add(i);piece.Bounds.Encapsulate(vertices[i]);
        }
        for(int i=0;i<triangles.Length;i+=3)pieces[Find(triangles[i])].Faces.Add(i);
        var braces=pieces.Values.Where(p=>p.Faces.Count==12&&p.Bounds.size.x<.03f&&p.Bounds.size.y>.15f&&p.Bounds.size.y<.4f&&p.Bounds.size.z>.45f&&p.Bounds.size.z<.60f&&p.Bounds.min.y>6.85f&&p.Bounds.max.y<7.20f&&p.Bounds.min.z<-.90f&&p.Bounds.max.z<-.40f).ToArray();
        var mast=pieces.Values.Where(p=>p.Bounds.size.y>7.5f&&p.Bounds.min.z>-.25f&&p.Bounds.max.z<.25f).ToArray();
        if(braces.Length!=2||mast.Length!=8)return 0;
        var caps=braces.Select(p=>p.Vertices.Select(i=>vertices[i]).GroupBy(Key).Select(g=>g.First()).OrderBy(v=>v.y).ToArray()).ToArray();
        if(caps.Any(p=>p.Length!=8))return 0;
        for(int beam=0;beam<braces.Length;beam++)
        {
            var points=caps[beam];var bottom=Vector3.zero;var top=Vector3.zero;
            for(int i=0;i<4;i++){bottom+=points[i]*.25f;top+=points[i+4]*.25f;}
            float nearest=float.PositiveInfinity;Vector3 anchor=bottom;
            foreach(var piece in mast)foreach(int face in piece.Faces)
            {
                var p=Closest(bottom,vertices[triangles[face]],vertices[triangles[face+1]],vertices[triangles[face+2]]);
                float distance=(p-bottom).sqrMagnitude;if(distance>=nearest)continue;
                nearest=distance;anchor=p;
            }
            // Seat the cap on an actual mast face. Angle sections are concave,
            // so moving toward their bounding-box centre can create a gap.
            var target=anchor;
            var oldAxis=(bottom-top).normalized;
            var newAxis=(target-top).normalized;
            var rotation=Quaternion.FromToRotation(oldAxis,newAxis);
            foreach(int id in braces[beam].Vertices)
            {
                bool lower=vertices[id].y<(bottom.y+top.y)*.5f;
                vertices[id]=(lower?target:top)+rotation*(vertices[id]-(lower?bottom:top));
                normals[id]=Vector3.zero;
            }
            // The older offline fit sheared the struts without correcting
            // their normals. Rebuild only these two surfaces from real faces.
            foreach(int face in braces[beam].Faces)
            {
                int a=triangles[face],b=triangles[face+1],c=triangles[face+2];
                var n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                normals[a]+=n;normals[b]+=n;normals[c]+=n;
            }
            foreach(int id in braces[beam].Vertices)normals[id]=normals[id].normalized;
        }
        return braces.Length;
    }

    private static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
        if(d1<=0&&d2<=0)return a;
        var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
        float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
        var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
        float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
        float va=d3*d6-d5*d4;if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
        float inv=1/(va+vb+vc);return a+ab*(vb*inv)+ac*(vc*inv);
    }
}
