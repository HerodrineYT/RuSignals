using System.Collections.Generic;
using UnityEngine;
namespace RuSignals;
// One shared mesh containing the four washers and hexagonal anchor heads.
internal static class AnchorBoltGeometry
{
    internal static Mesh Create()
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var indices=new List<int>();
        foreach(float y in new[]{-.12f,.12f})foreach(float z in new[]{-.10f,.10f})
        {
            Cylinder(vertices,normals,indices,y,z,.022f,.039f,.045f,16);
            Cylinder(vertices,normals,indices,y,z,.014f,.044f,.061f,6);
        }
        var mesh=new Mesh{name="Four wall anchor bolts and washers",vertices=vertices.ToArray(),normals=normals.ToArray(),triangles=indices.ToArray()};mesh.RecalculateBounds();return mesh;
    }
    private static void Cylinder(List<Vector3> v,List<Vector3> n,List<int> t,float y,float z,float radius,float back,float front,int sides)
    {
        int center=v.Count;v.Add(new Vector3(front,y,z));n.Add(Vector3.right);
        for(int i=0;i<sides;i++)
        {
            float a=i*2*Mathf.PI/sides;v.Add(new Vector3(front,y+Mathf.Cos(a)*radius,z+Mathf.Sin(a)*radius));n.Add(Vector3.right);
            t.Add(center);t.Add(center+i+1);t.Add(center+(i+1)%sides+1);
        }
        for(int i=0;i<sides;i++)
        {
            float a=i*2*Mathf.PI/sides,b=(i+1)*2*Mathf.PI/sides;int first=v.Count;
            var p=new Vector3(0,y+Mathf.Cos(a)*radius,z+Mathf.Sin(a)*radius);var q=new Vector3(0,y+Mathf.Cos(b)*radius,z+Mathf.Sin(b)*radius);
            var normal=new Vector3(0,Mathf.Cos((a+b)*.5f),Mathf.Sin((a+b)*.5f));
            v.Add(p+Vector3.right*back);v.Add(q+Vector3.right*back);v.Add(q+Vector3.right*front);v.Add(p+Vector3.right*front);
            for(int j=0;j<4;j++)n.Add(normal);
            t.Add(first);t.Add(first+1);t.Add(first+2);t.Add(first);t.Add(first+2);t.Add(first+3);
        }
    }
}
