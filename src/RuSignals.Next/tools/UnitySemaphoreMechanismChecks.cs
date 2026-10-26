// Independent kinematic regressions: fixed axles, material hinge points,
// rigid vertical rods and physically exchanged optical filters.
// Does not replace or weaken the existing mesh-clearance/linkage checks.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class UnitySemaphoreMechanismChecks
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private const float Tolerance=.0001f,MinimumFilterTravel=.02f;
    private static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,All).Invoke(value,args);
    private static T Field<T>(object value,string name) where T:class=>value.GetType().GetField(name,All)?.GetValue(value) as T;
    private static string Num(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
    private static string Vec(Vector3 value)=>"["+Num(value.x)+","+Num(value.y)+","+Num(value.z)+"]";
    private static Vector3 Root(Component view,Transform node,Vector3 point)=>view.transform.InverseTransformPoint(node.TransformPoint(point));
    private static Vector3 LocalCenter(Renderer renderer)
    {
        var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
        if(mesh==null)throw new Exception("Physical filter/window must have actual mesh geometry: "+renderer.name);
        return mesh.bounds.center;
    }

    public static void Verify(Assembly assembly,object factory,Transform holder,string repo,string output)
    {
        Directory.CreateDirectory(output);
        var pose=assembly.GetType("SovietSemaphore.SemaphoreState",true);
        var axes=new[]{Axis(repo,"semaphore_upperrod"),Axis(repo,"semaphore_lowerrod")};
        var wheelMesh=ReadMesh(repo,"mechanism8371_wheel");
        var rollerMesh=ReadMesh(repo,"mechanism8371_roller");float rollerLow=rollerMesh.Vertices.Min(v=>v.z),rollerHigh=rollerMesh.Vertices.Max(v=>v.z);
        var rollerCap=rollerMesh.Vertices.Where(v=>Mathf.Abs(v.z-rollerLow)<.00001f).Distinct().ToArray();
        var profileTriangles=Enumerable.Range(0,wheelMesh.Indices.Length/3).Where(i=>Enumerable.Range(0,3).All(k=>{var uv=wheelMesh.UV[wheelMesh.Indices[i*3+k]];return uv.x>.5f&&uv.x<.75f&&uv.y>.75f;})).ToArray();
        if(profileTriangles.Length==0)throw new Exception("No actual machined-profile geometry in wheel export");
        float wheelRadius=wheelMesh.Vertices.Max(v=>new Vector2(v.x,v.y).magnitude);
        var meshCache=new Dictionary<string,MeshData>();
        MeshData ActualMesh(MeshFilter filter)
        {string key=filter.sharedMesh.name.Split('/')[1];if(!meshCache.TryGetValue(key,out var mesh)){mesh=ReadMesh(repo,key);meshCache.Add(key,mesh);}return mesh;}
        int samples=0,axisFailures=0,hingeFailures=0,rigidityFailures=0,filterFailures=0,clones=0;
        int transmissionFailures=0,rollerFailures=0,counterweightChecks=0;
        int rollerOtherArmCollisions=0,fixedLanternInstances=0,forbiddenLensMechanismParts=0;
        float worstWheelAxle=0,worstCableLength=0,worstRollerGap=0,minimumWeightRise=float.PositiveInfinity;
        float worstAxis=0,worstUpperHinge=0,worstLowerHinge=0,worstLength=0,worstVertical=0;
        float minimumRedTravel=float.PositiveInfinity,minimumGreenTravel=float.PositiveInfinity,worstWindowMismatch=0;
        string axisWitness="",hingeWitness="";var snapshots=new List<string>();
        foreach(bool two in new[]{false,true})
        {
            var template=(Component)Call(factory,"Mechanism",holder,two);
            Component clone=null;
            try
            {
                clone=UnityEngine.Object.Instantiate(template);clone.transform.SetParent(null);
                clone.transform.position=new Vector3(17,3,-11);clone.transform.rotation=Quaternion.Euler(3,47,-2);clone.transform.localScale=new Vector3(.7f,1.2f,1.3f);clones++;
                foreach(string name in new[]{"UpperDriveArm","LowerDriveArm","RedFilter","GreenFilter"})
                {
                    var node=Field<Component>(clone,name);if(node==null)continue;
                    if(node==Field<Component>(template,name)||!node.transform.IsChildOf(clone.transform))throw new Exception("Clone did not remap mechanism reference "+name);
                }
                foreach(var view in new[]{template,clone})
                {
                    Call(view,"SetState",Enum.Parse(pose,"Stop"),true);
                    var rods=two?new[]{Field<Transform>(view,"UpperRod"),Field<Transform>(view,"LowerRod")}:new[]{Field<Transform>(view,"UpperRod")};
                    var arms=two?new[]{Field<Transform>(view,"Upper"),Field<Transform>(view,"Lower")}:new[]{Field<Transform>(view,"Upper")};
                    var commonAxis=Field<Transform>(view,"Lever");
                    bool fixedLanterns=(bool?)view.GetType().GetField("FixedLanterns",All)?.GetValue(view)??false;
                    var windowRenderer=Field<Renderer>(view,"Red");
                    var red=Field<Renderer>(view,"RedFilter")??windowRenderer;
                    var green=Field<Renderer>(view,"GreenFilter")??Field<Renderer>(view,"Green");
                    if(windowRenderer==null||red==null||green==null)throw new Exception("Missing actual optical window or physical red/green filters");
                    var redCenter=LocalCenter(red);var greenCenter=LocalCenter(green);var windowCenter=LocalCenter(windowRenderer);
                    var windowRest=Root(view,windowRenderer.transform,windowCenter);
                    if(fixedLanterns)
                    {
                        fixedLanternInstances++;
                        foreach(string name in new[]{"RedFilter","GreenFilter","YellowFilter","UpperSelector","LowerSelector","UpperCoupling","LowerCoupling"})
                            if(Field<Component>(view,name)!=null)forbiddenLensMechanismParts++;
                        forbiddenLensMechanismParts+=view.GetComponentsInChildren<Renderer>(true).Count(r=>new[]{"filter","selector","spectacle"}.Any(s=>r.name.IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0));
                    }
                    var drives=two?new[]{Field<Transform>(view,"UpperDriveArm")??commonAxis,Field<Transform>(view,"LowerDriveArm")??commonAxis}:new[]{Field<Transform>(view,"UpperDriveArm")??commonAxis};
                    if(commonAxis==null||rods.Any(n=>n==null)||arms.Any(n=>n==null)||drives.Any(n=>n==null))throw new Exception("Missing actual semaphore mechanism transform");
                    var lowerAxes=drives.Concat(new[]{commonAxis}).Distinct().ToArray();
                    var wheel=Field<Transform>(view,"DriveWheel");var cableA=Field<Transform>(view,"ControlCableA");var cableB=Field<Transform>(view,"ControlCableB");
                    var rollers=two?new[]{Field<Transform>(view,"UpperRoller"),Field<Transform>(view,"LowerRoller")}:new[]{Field<Transform>(view,"UpperRoller")};
                    if(wheel==null||cableA==null||cableB==null||rollers.Any(r=>r==null))throw new Exception("Missing common wheel, control wires or actual followers");
                    var wheelRest=Root(view,wheel,Vector3.zero);var wheelRotation=wheel.localRotation;
                    var cableARest=Root(view,cableA,Vector3.zero);var cableBRest=Root(view,cableB,Vector3.zero);
                    var rollerRest=rollers.Select((r,i)=>drives[i].InverseTransformPoint(r.TransformPoint(Vector3.zero))).ToArray();
                    var driveFilters=drives.Select((d,i)=>d.GetComponentsInChildren<MeshFilter>(true).Single(f=>!f.transform.IsChildOf(rollers[i]))).ToArray();
                    var driveMeshes=driveFilters.Select(ActualMesh).ToArray();
                    var weights=driveMeshes.Select(m=>Components(m).Single(p=>p.Triangles==12&&Mathf.Abs(p.Bounds.size.x-.105f)<.0002f&&Mathf.Abs(p.Bounds.size.y-.12f)<.0002f&&Mathf.Abs(p.Bounds.size.z-.025f)<.0002f).Bounds.center).ToArray();
                    var driveTriangles=driveMeshes.Select(m=>Enumerable.Range(0,m.Indices.Length/3).ToArray()).ToArray();
                    var weightRest=driveFilters.Select((f,i)=>Root(view,f.transform,weights[i])).ToArray();
                    var axleRest=lowerAxes.Select(n=>Root(view,n,Vector3.zero)).ToArray();
                    var topRest=rods.Select((r,i)=>arms[i].InverseTransformPoint(r.TransformPoint(axes[i][1]))).ToArray();
                    var bottomRest=rods.Select((r,i)=>drives[i].InverseTransformPoint(r.TransformPoint(axes[i][0]))).ToArray();
                    var lengthRest=rods.Select((r,i)=>Vector3.Distance(Root(view,r,axes[i][0]),Root(view,r,axes[i][1]))).ToArray();
                    void Measure(string witness)
                    {
                        samples++;
                        string detail=(two?"two":"one")+" arms "+(view==clone?"clone":"template")+" "+witness;
                        float windowDrift=Vector3.Distance(Root(view,windowRenderer.transform,windowCenter),windowRest);
                        if(windowDrift>Tolerance)filterFailures++;
                        worstWindowMismatch=Mathf.Max(worstWindowMismatch,windowDrift);
                        float wheelDrift=Vector3.Distance(Root(view,wheel,Vector3.zero),wheelRest);worstWheelAxle=Mathf.Max(worstWheelAxle,wheelDrift);
                        if(wheelDrift>Tolerance)transmissionFailures++;
                        var direction=(Quaternion.Inverse(wheelRotation)*wheel.localRotation)*Vector3.right;
                        float angle=Mathf.Atan2(direction.y,direction.x);
                        var ca=Root(view,cableA,Vector3.zero);var cb=Root(view,cableB,Vector3.zero);
                        float da=ca.y-cableARest.y,db=cb.y-cableBRest.y;
                        float wireError=Mathf.Max(Mathf.Abs(da+db),Mathf.Abs(da-angle*wheelRadius));worstCableLength=Mathf.Max(worstCableLength,wireError);
                        if(wireError>Tolerance||Mathf.Abs(ca.x-cableARest.x)>Tolerance||Mathf.Abs(ca.z-cableARest.z)>Tolerance||Mathf.Abs(cb.x-cableBRest.x)>Tolerance||Mathf.Abs(cb.z-cableBRest.z)>Tolerance)transmissionFailures++;
                        for(int i=0;i<rollers.Length;i++)
                        {
                            if(Vector3.Distance(drives[i].InverseTransformPoint(rollers[i].TransformPoint(Vector3.zero)),rollerRest[i])>Tolerance)transmissionFailures++;
                            var b=rollers[i].GetComponent<MeshFilter>().sharedMesh.bounds;
                            var cap=wheel.InverseTransformPoint(rollers[i].TransformPoint(new Vector3(b.center.x,b.center.y,b.min.z)));
                            var outline=rollerCap.Select(v=>wheel.InverseTransformPoint(rollers[i].TransformPoint(v))-cap).Select(v=>new Vector2(v.x,v.y)).OrderBy(v=>Mathf.Atan2(v.y,v.x)).ToArray();
                            float depth=wheel.InverseTransformPoint(rollers[i].TransformPoint(new Vector3(b.center.x,b.center.y,rollerHigh))).z-cap.z;
                            float gap;bool contact=ProfileContact(cap,outline,depth,wheelMesh,profileTriangles,out gap);
                            worstRollerGap=Mathf.Max(worstRollerGap,gap);if(!contact)rollerFailures++;
                            if(two)
                            {
                                int other=1-i;var otherMesh=driveFilters[other].transform;
                                var otherCap=otherMesh.InverseTransformPoint(rollers[i].TransformPoint(new Vector3(b.center.x,b.center.y,b.min.z)));
                                var otherOutline=rollerCap.Select(v=>otherMesh.InverseTransformPoint(rollers[i].TransformPoint(v))-otherCap).Select(v=>new Vector2(v.x,v.y)).OrderBy(v=>Mathf.Atan2(v.y,v.x)).ToArray();
                                float otherDepth=otherMesh.InverseTransformPoint(rollers[i].TransformPoint(new Vector3(b.center.x,b.center.y,rollerHigh))).z-otherCap.z;
                                float ignored;if(ProfileContact(otherCap,otherOutline,otherDepth,driveMeshes[other],driveTriangles[other],out ignored))rollerOtherArmCollisions++;
                            }
                        }
                        if(two)
                        {
                            var first=Root(view,drives[0],Vector3.zero);var second=Root(view,drives[1],Vector3.zero);
                            // Coaxial pivots may differ along their common Z
                            // axle, but cannot have different transverse X/Y.
                            float transverse=new Vector2(first.x-second.x,first.y-second.y).magnitude;
                            var az=view.transform.InverseTransformVector(drives[0].TransformVector(Vector3.forward)).normalized;
                            var bz=view.transform.InverseTransformVector(drives[1].TransformVector(Vector3.forward)).normalized;
                            if(transverse>Tolerance||Vector3.Cross(az,bz).magnitude>Tolerance)axisFailures++;
                        }
                        for(int i=0;i<lowerAxes.Length;i++)
                        {
                            float drift=Vector3.Distance(Root(view,lowerAxes[i],Vector3.zero),axleRest[i]);
                            if(drift>worstAxis){worstAxis=drift;axisWitness=detail+" "+lowerAxes[i].name;}
                            if(drift>Tolerance)axisFailures++;
                        }
                        for(int i=0;i<rods.Length;i++)
                        {
                            var worldTop=rods[i].TransformPoint(axes[i][1]);var worldBottom=rods[i].TransformPoint(axes[i][0]);
                            float topDrift=Vector3.Distance(arms[i].InverseTransformPoint(worldTop),topRest[i]);
                            float bottomDrift=Vector3.Distance(drives[i].InverseTransformPoint(worldBottom),bottomRest[i]);
                            if(topDrift>worstUpperHinge||bottomDrift>worstLowerHinge)hingeWitness=detail+" "+(i==0?"upper":"lower");
                            worstUpperHinge=Mathf.Max(worstUpperHinge,topDrift);worstLowerHinge=Mathf.Max(worstLowerHinge,bottomDrift);
                            if(topDrift>Tolerance)hingeFailures++;if(bottomDrift>Tolerance)hingeFailures++;
                            var axis=view.transform.InverseTransformPoint(worldTop)-view.transform.InverseTransformPoint(worldBottom);
                            float lengthChange=Mathf.Abs(axis.magnitude-lengthRest[i]);float horizontal=new Vector2(axis.x,axis.z).magnitude;
                            worstLength=Mathf.Max(worstLength,lengthChange);worstVertical=Mathf.Max(worstVertical,horizontal);
                            if(lengthChange>Tolerance||horizontal>Tolerance||axis.y<=0)rigidityFailures++;
                        }
                    }
                    foreach(string from in new[]{"Stop","Main","Diverging","Dark"})foreach(string to in new[]{"Stop","Main","Diverging","Dark"})
                    {
                        Call(view,"SetState",Enum.Parse(pose,from),true);Call(view,"SetState",Enum.Parse(pose,to),false);
                        for(int frame=0;frame<=40;frame++){if(frame>0)Call(view,"Advance",.05f);Measure(from+" -> "+to+" frame "+frame);}
                    }
                    Call(view,"SetState",Enum.Parse(pose,"Stop"),true);Call(view,"SetState",Enum.Parse(pose,"Diverging"),false);Call(view,"Advance",.35f);Measure("interrupted open");
                    Call(view,"SetState",Enum.Parse(pose,"Main"),false);Call(view,"Advance",.2f);Measure("interrupted route change");
                    Call(view,"SetState",Enum.Parse(pose,"Stop"),false);
                    for(int frame=0;frame<=40;frame++){if(frame>0)Call(view,"Advance",.05f);Measure("interrupted close "+frame);}
                    Call(view,"SetState",Enum.Parse(pose,"Stop"),true);
                    var redStop=Root(view,red.transform,redCenter);var greenStop=Root(view,green.transform,greenCenter);
                    Call(view,"SetState",Enum.Parse(pose,"Main"),true);
                    var redMain=Root(view,red.transform,redCenter);var greenMain=Root(view,green.transform,greenCenter);
                    float redTravel=Vector3.Distance(redStop,redMain),greenTravel=Vector3.Distance(greenStop,greenMain),window=Mathf.Max(Vector3.Distance(redStop,windowRest),Vector3.Distance(greenMain,windowRest));
                    minimumRedTravel=Mathf.Min(minimumRedTravel,redTravel);minimumGreenTravel=Mathf.Min(minimumGreenTravel,greenTravel);worstWindowMismatch=Mathf.Max(worstWindowMismatch,window);
                    if(!fixedLanterns){if(redTravel<MinimumFilterTravel)filterFailures++;if(greenTravel<MinimumFilterTravel)filterFailures++;if(window>.01f)filterFailures++;}
                    else if(redTravel>Tolerance||greenTravel>Tolerance)filterFailures++;
                    snapshots.Add("{\"two_arms\":"+(two?"true":"false")+",\"clone\":"+(view==clone?"true":"false")+",\"upper_material_hinge_at_stop\":"+Vec(topRest[0])+",\"lower_drive_axis_at_stop\":"+Vec(axleRest[0])+",\"red_stop\":"+Vec(redStop)+",\"red_main\":"+Vec(redMain)+",\"green_stop\":"+Vec(greenStop)+",\"green_main\":"+Vec(greenMain)+"}");
                    for(int i=0;i<drives.Length;i++)
                    {
                        Call(view,"SetState",Enum.Parse(pose,i==0?"Main":"Diverging"),true);
                        float rise=Root(view,driveFilters[i].transform,weights[i]).y-weightRest[i].y;minimumWeightRise=Mathf.Min(minimumWeightRise,rise);counterweightChecks++;
                        if(rise<.005f)transmissionFailures++;
                    }
                }
            }
            finally
            {
                if(clone!=null)UnityEngine.Object.DestroyImmediate(clone.gameObject);
                UnityEngine.Object.DestroyImmediate(template.gameObject);
            }
        }
        string report="{\"pose_samples\":"+samples+",\"clones\":"+clones+",\"failed_fixed_axle_checks\":"+axisFailures+",\"maximum_lower_axle_translation_m\":"+Num(worstAxis)+",\"axis_witness\":\""+axisWitness+"\",\"failed_fixed_material_hinge_checks\":"+hingeFailures+",\"maximum_top_hinge_drift_in_arm_m\":"+Num(worstUpperHinge)+",\"maximum_bottom_hinge_drift_in_drive_arm_m\":"+Num(worstLowerHinge)+",\"hinge_witness\":\""+hingeWitness+"\",\"failed_rigid_vertical_rod_checks\":"+rigidityFailures+",\"maximum_rod_length_change_m\":"+Num(worstLength)+",\"maximum_rod_horizontal_axis_offset_m\":"+Num(worstVertical)+",\"failed_physical_filter_exchange_checks\":"+filterFailures+",\"minimum_red_filter_travel_m\":"+Num(minimumRedTravel)+",\"minimum_green_filter_travel_m\":"+Num(minimumGreenTravel)+",\"maximum_fixed_optical_window_mismatch_m\":"+Num(worstWindowMismatch)+",\"tolerance_m\":"+Num(Tolerance)+",\"snapshots\":["+string.Join(",",snapshots)+"],\"scope\":\"Actual factory/Apply/transforms and exported rod endpoints/filter centers; all transitions and interrupted motion; single/two arms, transformed clone; axle and material hinge points fixed, rigid vertical rods, physical red/green exchange through one fixed window. No required fixed rod X and no stationary housing contact oracle\"}";
        File.WriteAllText(Path.Combine(output,"semaphore-fixed-mechanism-verification.json"),report);Debug.Log("SEMAPHORE_FIXED_MECHANISM_VERIFICATION "+report);
        string transmissionReport="{\"pose_samples\":"+samples+",\"failed_transmission_checks\":"+transmissionFailures+",\"failed_actual_roller_profile_contacts\":"+rollerFailures+",\"maximum_wheel_axle_translation_m\":"+Num(worstWheelAxle)+",\"maximum_opposed_wire_length_error_m\":"+Num(worstCableLength)+",\"maximum_roller_profile_air_gap_lower_bound_m\":"+Num(worstRollerGap)+",\"counterweight_checks\":"+counterweightChecks+",\"minimum_counterweight_rise_on_opening_m\":"+Num(minimumWeightRise)+",\"actual_profile_triangles\":"+profileTriangles.Length+",\"actual_wheel_radius_m\":"+Num(wheelRadius)+",\"scope\":\"Actual common wheel fixed axis; equal and opposed vertical wire travel equals actual exported rim arc; followers rigidly fixed to respective coaxial arms and actual polygonal roller body intersects actual profile triangles in3D via exact Z clipping and convex polygon SAT; actual disconnected counterweight box rises on opening\"}";
        File.WriteAllText(Path.Combine(output,"semaphore-8371-transmission-verification.json"),transmissionReport);Debug.Log("SEMAPHORE_8371_TRANSMISSION_VERIFICATION "+transmissionReport);
        string optionalReport="{\"fixed_lantern_instances\":"+fixedLanternInstances+",\"forbidden_lens_mechanism_parts\":"+forbiddenLensMechanismParts+",\"actual_roller_opposite_drive_arm_collisions\":"+rollerOtherArmCollisions+",\"scope\":\"FixedLanterns explicitly removes spectacle/filter/coupling mechanisms and requires stationary optical windows; older models retain mandatory physical-filter regression. Actual roller polygon against opposite actual crank mesh, including intermediate poses\"}";
        File.WriteAllText(Path.Combine(output,"semaphore-8371-additional-verification.json"),optionalReport);Debug.Log("SEMAPHORE_8371_ADDITIONAL_VERIFICATION "+optionalReport);
        if(axisFailures+hingeFailures+rigidityFailures+filterFailures!=0)throw new Exception("Semaphore physical mechanism regression: fixed axle failures "+axisFailures+", fixed hinge failures "+hingeFailures+", rigid rod failures "+rigidityFailures+", physical filter failures "+filterFailures);
        if(transmissionFailures+rollerFailures!=0)throw new Exception("Semaphore transmission regression: transmission "+transmissionFailures+", actual roller contact "+rollerFailures);
        if(rollerOtherArmCollisions+forbiddenLensMechanismParts!=0)throw new Exception("Semaphore forbidden mechanism intersections: opposite arm/roller "+rollerOtherArmCollisions+", removed lens mechanism parts "+forbiddenLensMechanismParts);
        Debug.Log("SEMAPHORE_FIXED_MECHANISM_PASS");
        Debug.Log("SEMAPHORE_8371_TRANSMISSION_PASS");
    }

    private static Vector3[] Read(string repo,string key)
    {return ReadMesh(repo,key).Vertices;}
    private sealed class MeshData{internal Vector3[] Vertices;internal Vector2[] UV;internal int[] Indices;}
    private static MeshData ReadMesh(string repo,string key)
    {
        using(var reader=new BinaryReader(File.OpenRead(Path.Combine(repo,"src/RuSignals.Next/Assets/SolidHoods/Semaphore",key+".rsm"))))
        {
            if(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))!="RUSMESH2"||reader.ReadInt32()!=1)throw new Exception("Unexpected semaphore RSM format");
            reader.ReadBytes(reader.ReadInt32());reader.ReadBytes(reader.ReadInt32());reader.ReadInt32();
            int count=reader.ReadInt32(),ic=reader.ReadInt32();var vertices=new Vector3[count];var uv=new Vector2[count];var indices=new int[ic];
            for(int i=0;i<count;i++){vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());for(int k=0;k<3;k++)reader.ReadSingle();uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());}
            for(int i=0;i<ic;i++)indices[i]=reader.ReadInt32();return new MeshData{Vertices=vertices,UV=uv,Indices=indices};
        }
    }
    private sealed class Piece{internal Bounds Bounds;internal int Triangles;}
    private static Piece[] Components(MeshData mesh)
    {
        var parent=Enumerable.Range(0,mesh.Vertices.Length).ToArray();var positions=new Dictionary<Vector3,int>();
        int Find(int x){while(parent[x]!=x){parent[x]=parent[parent[x]];x=parent[x];}return x;}
        void Join(int a,int b){parent[Find(a)]=Find(b);}
        for(int i=0;i<parent.Length;i++){var v=mesh.Vertices[i];var key=new Vector3(Mathf.Round(v.x*100000),Mathf.Round(v.y*100000),Mathf.Round(v.z*100000));if(positions.TryGetValue(key,out var p))Join(i,p);else positions.Add(key,i);}
        for(int i=0;i<mesh.Indices.Length;i+=3){Join(mesh.Indices[i],mesh.Indices[i+1]);Join(mesh.Indices[i],mesh.Indices[i+2]);}
        return mesh.Indices.GroupBy(Find).Select(group=>{var ids=group.ToArray();var bounds=new Bounds(mesh.Vertices[ids[0]],Vector3.zero);foreach(int i in ids)bounds.Encapsulate(mesh.Vertices[i]);return new Piece{Bounds=bounds,Triangles=ids.Length/3};}).ToArray();
    }
    private static float SegmentDistance(Vector2 p,Vector2 a,Vector2 b)
    {var d=b-a;float t=d.sqrMagnitude<1e-16f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);return Vector2.Distance(p,a+d*t);}
    private static bool ProfileContact(Vector3 cap,Vector2[] outline,float depth,MeshData mesh,int[] triangles,out float airGap)
    {
        airGap=float.PositiveInfinity;float radius=outline.Max(p=>p.magnitude);
        List<Vector3> Clip(List<Vector3> input,float plane,bool lower)
        {
            var output=new List<Vector3>();if(input.Count==0)return output;
            for(int i=0;i<input.Count;i++)
            {
                var a=input[i];var b=input[(i+1)%input.Count];bool inside=lower?a.z>=plane:a.z<=plane,next=lower?b.z>=plane:b.z<=plane;
                if(inside)output.Add(a);if(inside!=next)output.Add(Vector3.Lerp(a,b,(plane-a.z)/(b.z-a.z)));
            }
            return output;
        }
        bool Disjoint(Vector2[] a,Vector2[] b)
        {
            for(int i=0;i<a.Length;i++)
            {
                var edge=a[(i+1)%a.Length]-a[i];if(edge.sqrMagnitude<1e-16f)continue;var normal=new Vector2(-edge.y,edge.x).normalized;
                float amin=a.Min(p=>Vector2.Dot(p,normal)),amax=a.Max(p=>Vector2.Dot(p,normal));
                float bmin=b.Min(p=>Vector2.Dot(p,normal)),bmax=b.Max(p=>Vector2.Dot(p,normal));
                if(amin>bmax+Tolerance||bmin>amax+Tolerance)return true;
            }
            return false;
        }
        foreach(int triangle in triangles)
        {
            var a=mesh.Vertices[mesh.Indices[triangle*3]]-cap;var b=mesh.Vertices[mesh.Indices[triangle*3+1]]-cap;var c=mesh.Vertices[mesh.Indices[triangle*3+2]]-cap;
            float lowX=Mathf.Min(a.x,Mathf.Min(b.x,c.x)),highX=Mathf.Max(a.x,Mathf.Max(b.x,c.x));
            float lowY=Mathf.Min(a.y,Mathf.Min(b.y,c.y)),highY=Mathf.Max(a.y,Mathf.Max(b.y,c.y));
            if(lowX>radius||highX< -radius||lowY>radius||highY< -radius)continue;
            float lo=Mathf.Min(a.z,Mathf.Min(b.z,c.z)),hi=Mathf.Max(a.z,Mathf.Max(b.z,c.z));
            airGap=Mathf.Min(airGap,Mathf.Max(lo-depth,Mathf.Max(-hi,0)));
            if(lo>depth+Tolerance||hi< -Tolerance)continue;
            var clipped=Clip(Clip(new List<Vector3>{a,b,c},-Tolerance,true),depth+Tolerance,false);
            if(clipped.Count==0)continue;
            var polygon=clipped.Select(p=>new Vector2(p.x,p.y)).ToArray();
            if(!Disjoint(outline,polygon)&&!Disjoint(polygon,outline))return true;
        }
        if(float.IsInfinity(airGap))airGap=1;return false;
    }
    private static Vector3 Center(Vector3[] vertices)
    {
        var bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);return bounds.center;
    }
    private static Vector3[] Axis(string repo,string key)
    {
        var vertices=Read(repo,key);float low=vertices.Min(v=>v.y),high=vertices.Max(v=>v.y);
        Vector3 Average(float y){var cap=vertices.Where(v=>Mathf.Abs(v.y-y)<.00001f).ToArray();return cap.Aggregate(Vector3.zero,(sum,v)=>sum+v)/cap.Length;}
        return new[]{Average(low),Average(high)};
    }
}
