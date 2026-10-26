using System;
using System.Collections.Generic;
using UnityEngine;
namespace RuSignals;
// Temporary import data. Coincident seam vertices belong to the same piece.
internal sealed class MeshPieces
{
    internal sealed class Piece { internal Bounds Bounds;internal readonly List<int> Indices=new List<int>(); }
    internal readonly Vector3[] Vertices,Normals;internal readonly Vector2[] Uv;
    internal readonly List<Piece> Pieces=new List<Piece>();
    internal MeshPieces(Mesh source)
    {
        Vertices=source.vertices;Normals=source.normals;Uv=source.uv;var indices=source.triangles;
        var parents=new int[Vertices.Length];var welded=new Dictionary<(int,int,int),int>();
        for(int i=0;i<Vertices.Length;i++)
        {
            var v=Vertices[i];var position=((int)Math.Round(v.x*100000),(int)Math.Round(v.y*100000),(int)Math.Round(v.z*100000));
            if(welded.TryGetValue(position,out int previous))parents[i]=previous;else{parents[i]=i;welded.Add(position,i);}
        }
        int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
        for(int i=0;i<indices.Length;i+=3){int root=Find(indices[i]);parents[Find(indices[i+1])]=root;parents[Find(indices[i+2])]=root;}
        var groups=new Dictionary<int,Piece>();
        foreach(int index in indices)
        {
            int root=Find(index);
            if(!groups.TryGetValue(root,out var piece)){piece=new Piece{Bounds=new Bounds(Vertices[index],Vector3.zero)};groups.Add(root,piece);Pieces.Add(piece);}
            piece.Bounds.Encapsulate(Vertices[index]);piece.Indices.Add(index);
        }
    }
    internal Mesh Build(string name,IEnumerable<Piece> pieces,float lower=0)
    {
        var remap=new Dictionary<int,int>();var vs=new List<Vector3>();var ns=new List<Vector3>();var uv=new List<Vector2>();var ids=new List<int>();
        foreach(var piece in pieces)foreach(int index in piece.Indices)
        {
            if(!remap.TryGetValue(index,out int mapped)){mapped=vs.Count;remap.Add(index,mapped);vs.Add(Vertices[index]-Vector3.up*lower);ns.Add(Normals[index]);uv.Add(Uv[index]);}
            ids.Add(mapped);
        }
        if(ids.Count==0||ids.Count%3!=0)throw new InvalidOperationException("Empty housing: "+name);
        var mesh=new Mesh{name=name,vertices=vs.ToArray(),normals=ns.ToArray(),uv=uv.ToArray(),triangles=ids.ToArray()};mesh.RecalculateBounds();return mesh;
    }
}
