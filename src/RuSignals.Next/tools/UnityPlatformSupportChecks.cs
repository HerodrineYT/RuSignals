// Independent regression for the two rear platform struts. Reads the actual
// archived RSM and invokes the production C# importer repair, when available.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private sealed class PlatformData
    {
        internal Vector3[] Positions,Normals;
        internal Vector2[] UV;
        internal int[] Indices;
    }
    private sealed class PlatformPiece
    {
        internal int[] VertexIds,TriangleIds;
        internal Bounds Bounds;
    }
    private static PlatformData PlatformRead(string path)
    {
        using(var r=new BinaryReader(File.OpenRead(path)))
        {
            if(System.Text.Encoding.ASCII.GetString(r.ReadBytes(8))!="RUSMESH2"||r.ReadInt32()!=1)throw new Exception("Unexpected platform mesh format");
            r.ReadBytes(r.ReadInt32());r.ReadBytes(r.ReadInt32());r.ReadInt32();
            int vc=r.ReadInt32(),ic=r.ReadInt32();var data=new PlatformData{Positions=new Vector3[vc],Normals=new Vector3[vc],UV=new Vector2[vc],Indices=new int[ic]};
            for(int i=0;i<vc;i++){data.Positions[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());data.Normals[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());data.UV[i]=new Vector2(r.ReadSingle(),r.ReadSingle());}
            for(int i=0;i<ic;i++)data.Indices[i]=r.ReadInt32();return data;
        }
    }
    private static PlatformPiece[] PlatformPieces(PlatformData data)
    {
        var p=Enumerable.Range(0,data.Positions.Length).ToArray();var welded=new Dictionary<Vector3,int>();
        int Find(int i){while(p[i]!=i){p[i]=p[p[i]];i=p[i];}return i;}
        void Join(int a,int b){p[Find(a)]=Find(b);}
        for(int i=0;i<p.Length;i++){var v=data.Positions[i];var q=new Vector3(Mathf.Round(v.x*100000),Mathf.Round(v.y*100000),Mathf.Round(v.z*100000));if(welded.TryGetValue(q,out int other))Join(i,other);else welded.Add(q,i);}
        for(int i=0;i<data.Indices.Length;i+=3){Join(data.Indices[i],data.Indices[i+1]);Join(data.Indices[i],data.Indices[i+2]);}
        return Enumerable.Range(0,data.Indices.Length/3).GroupBy(t=>Find(data.Indices[t*3])).Select(g=>
        {
            var tris=g.ToArray();var ids=tris.SelectMany(t=>data.Indices.Skip(t*3).Take(3)).Distinct().ToArray();var b=new Bounds(data.Positions[ids[0]],Vector3.zero);foreach(int i in ids)b.Encapsulate(data.Positions[i]);
            return new PlatformPiece{VertexIds=ids,TriangleIds=tris,Bounds=b};
        }).ToArray();
    }
    private static int[][] PlatformCaps(PlatformData data,PlatformPiece piece)
    {
        var groups=piece.VertexIds.GroupBy(i=>{var v=data.Positions[i];return new Vector3(Mathf.Round(v.x*100000),Mathf.Round(v.y*100000),Mathf.Round(v.z*100000));}).Select(g=>g.First()).OrderBy(i=>data.Positions[i].y).ToArray();
        if(groups.Length!=8)throw new Exception("A platform strut must have eight actual geometric corners");
        return new[]{groups.Take(4).ToArray(),groups.Skip(4).ToArray()};
    }
    private static Vector3 PlatformCenter(PlatformData data,int[] ids)=>ids.Aggregate(Vector3.zero,(a,i)=>a+data.Positions[i])/ids.Length;
    private static Vector3 PlatformClosest(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        // Closest point on an actual triangle (Ericson region tests).
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);if(d1<=0&&d2<=0)return a;
        var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
        float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
        var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
        float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
        float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/(d4-d3+d5-d6));
        float inv=1/(va+vb+vc);return a+ab*(vb*inv)+ac*(vc*inv);
    }
    private static bool PlatformInside(PlatformData data,PlatformPiece piece,Vector3 point)
    {
        var center=piece.VertexIds.Aggregate(Vector3.zero,(sum,i)=>sum+data.Positions[i])/piece.VertexIds.Length;
        foreach(int t in piece.TriangleIds)
        {
            var a=data.Positions[data.Indices[t*3]];var b=data.Positions[data.Indices[t*3+1]];var c=data.Positions[data.Indices[t*3+2]];var n=Vector3.Cross(b-a,c-a).normalized;
            if(Vector3.Dot(n,center-a)>0)n=-n;
            if(Vector3.Dot(n,point-a)>.00001f)return false;
        }
        return true;
    }
    private static float PlatformSolidGap(PlatformData data,PlatformPiece piece,Vector3 point)
    {
        if(PlatformInside(data,piece,point))return 0;
        float distance=float.PositiveInfinity;
        foreach(int t in piece.TriangleIds){var ix=data.Indices;distance=Mathf.Min(distance,Vector3.Distance(point,PlatformClosest(point,data.Positions[ix[t*3]],data.Positions[ix[t*3+1]],data.Positions[ix[t*3+2]])));}
        return distance;
    }
    private static float[] PlatformCapLengths(PlatformData data,int[] ids)
    {
        var lengths=new List<float>();for(int i=0;i<ids.Length;i++)for(int j=i+1;j<ids.Length;j++)lengths.Add(Vector3.Distance(data.Positions[ids[i]],data.Positions[ids[j]]));return lengths.OrderBy(x=>x).ToArray();
    }
    private static bool PlatformSame(float a,float b)=>BitConverter.ToInt32(BitConverter.GetBytes(a),0)==BitConverter.ToInt32(BitConverter.GetBytes(b),0);
    private static bool PlatformSame(Vector3 a,Vector3 b)=>PlatformSame(a.x,b.x)&&PlatformSame(a.y,b.y)&&PlatformSame(a.z,b.z);
    private static string PlatformNumber(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
    private static string PlatformVector(Vector3 p)=>"["+PlatformNumber(p.x)+","+PlatformNumber(p.y)+","+PlatformNumber(p.z)+"]";
    private static void PlatformSupportChecks(Assembly assembly,string repo,string output)
    {
        var path=Path.Combine(repo,"src/RuSignals.Next/Assets/SolidHoods/Semaphore/semaphore_static.rsm");
        var before=PlatformRead(path);var after=PlatformRead(path);var pieces=PlatformPieces(before);
        var struts=pieces.Where(p=>p.TriangleIds.Length==12&&p.Bounds.min.y>6.90f&&p.Bounds.max.y<7.20f&&p.Bounds.min.z<-.90f&&p.Bounds.max.z<-.40f&&p.Bounds.size.x<.03f).ToArray();
        var deck=pieces.Single(p=>p.TriangleIds.Length==12&&Mathf.Abs(p.Bounds.size.x-.52f)<.0001f&&Mathf.Abs(p.Bounds.size.y-.033f)<.0001f&&Mathf.Abs(p.Bounds.size.z-.91f)<.0001f&&p.Bounds.center.y>7.1f);
        var mast=pieces.Where(p=>p.Bounds.size.y>7.5f&&p.Bounds.min.z>-.25f&&p.Bounds.max.z<.25f).ToArray();
        if(struts.Length!=2||mast.Length!=8)throw new Exception("Frozen platform regression fixture changed unexpectedly");
        var method=assembly.GetType("RuSignals.PlatformSupportFit")?.GetMethod("Apply",All);int adjusted=method==null?0:(int)method.Invoke(null,new object[]{after.Positions,after.Normals,after.Indices});
        int lowerContacts=0,upperContacts=0,changedOtherVertices=0,changedUV=0;float worstCapShape=0,worstUpperDrift=0,worstNormalError=0;var records=new List<string>();
        foreach(var strut in struts)
        {
            var caps=PlatformCaps(before,strut);var oldBottom=PlatformCenter(before,caps[0]);var bottom=PlatformCenter(after,caps[0]);var top=PlatformCenter(after,caps[1]);
            float lower=mast.Min(p=>PlatformSolidGap(after,p,bottom)),upper=PlatformSolidGap(after,deck,top);if(lower<=.0001f)lowerContacts++;if(upper<=.0001f)upperContacts++;
            worstUpperDrift=Mathf.Max(worstUpperDrift,Vector3.Distance(top,PlatformCenter(before,caps[1])));
            foreach(var cap in caps){var original=PlatformCapLengths(before,cap);var actual=PlatformCapLengths(after,cap);for(int i=0;i<original.Length;i++)worstCapShape=Mathf.Max(worstCapShape,Mathf.Abs(original[i]-actual[i]));}
            foreach(int t in strut.TriangleIds)
            {
                int a=after.Indices[t*3],b=after.Indices[t*3+1],c=after.Indices[t*3+2];var n=Vector3.Cross(after.Positions[b]-after.Positions[a],after.Positions[c]-after.Positions[a]).normalized;
                foreach(int i in new[]{a,b,c})worstNormalError=Mathf.Max(worstNormalError,1-Mathf.Abs(Vector3.Dot(n,after.Normals[i].normalized)));
            }
            records.Add("{\"old_bottom\":"+PlatformVector(oldBottom)+",\"bottom\":"+PlatformVector(bottom)+",\"top\":"+PlatformVector(top)+",\"actual_mast_gap_m\":"+PlatformNumber(lower)+",\"actual_platform_gap_m\":"+PlatformNumber(upper)+"}");
        }
        var permitted=new HashSet<int>(struts.SelectMany(p=>p.VertexIds));
        for(int i=0;i<before.Positions.Length;i++)
        {
            if(!permitted.Contains(i)&&(!PlatformSame(before.Positions[i],after.Positions[i])||!PlatformSame(before.Normals[i],after.Normals[i])))changedOtherVertices++;
            if(!PlatformSame(before.UV[i].x,after.UV[i].x)||!PlatformSame(before.UV[i].y,after.UV[i].y))changedUV++;
        }
        bool indicesSame=before.Indices.SequenceEqual(after.Indices);
        var settledPositions=(Vector3[])after.Positions.Clone();var settledNormals=(Vector3[])after.Normals.Clone();var settledIndices=(int[])after.Indices.Clone();
        int repeated=method==null?0:(int)method.Invoke(null,new object[]{after.Positions,after.Normals,after.Indices});
        bool idempotent=repeated==0&&settledIndices.SequenceEqual(after.Indices)&&settledPositions.Zip(after.Positions,(a,b)=>PlatformSame(a,b)).All(x=>x)&&settledNormals.Zip(after.Normals,(a,b)=>PlatformSame(a,b)).All(x=>x);
        bool passedNow=adjusted==2&&lowerContacts==2&&upperContacts==2&&changedOtherVertices==0&&changedUV==0&&indicesSame&&idempotent&&worstCapShape<.0001f&&worstUpperDrift<.0001f&&worstNormalError<.001f;
        string hash;using(var algorithm=SHA256.Create())hash=BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
        File.WriteAllText(Path.Combine(output,"platform-support-verification.json"),"{\"passed\":"+passedNow.ToString().ToLowerInvariant()+",\"supports\":2,\"adjusted_supports\":"+adjusted+",\"mast_angles\":8,\"lower_contacts\":"+lowerContacts+",\"upper_contacts\":"+upperContacts+",\"unrelated_changed_vertices\":"+changedOtherVertices+",\"changed_uvs\":"+changedUV+",\"indices_identical\":"+indicesSame.ToString().ToLowerInvariant()+",\"idempotent\":"+idempotent.ToString().ToLowerInvariant()+",\"maximum_cap_shape_error_m\":"+PlatformNumber(worstCapShape)+",\"maximum_upper_cap_centre_drift_m\":"+PlatformNumber(worstUpperDrift)+",\"maximum_face_normal_error\":"+PlatformNumber(worstNormalError)+",\"rsm_sha256\":\""+hash+"\",\"struts\":["+string.Join(",",records)+"],\"scope\":\"Actual RSM arrays passed to production C# repair; exact triangular convex-solid mast/platform containment or closest-point distance; original top endpoint retained, cross-sections rigid, idempotent repeated import, all other positions/normals and all UV/index data preserved. No AABB-only contact oracle.\"}");
        Check(passedNow,"Both platform support endpoints meet the actual fixed mast and deck without changing unrelated geometry");
        Debug.Log("RUS_PLATFORM_SUPPORT_PASS C# repair / two actual platform and mast contacts / exact unrelated mesh preservation");
    }
}
