// Independent physical-contact regression against actual factory transforms
// and exported mesh vertices. This never launches a game or loads a saved world.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class UnitySemaphoreLinkageChecks
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private const float ContactTolerance=.0001f;
    private const float VerticalTolerance=.0001f;
    private static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,All).Invoke(value,args);
    private static Transform Node(object value,string name)=>(Transform)value.GetType().GetField(name,All).GetValue(value);
    private static Transform OptionalNode(object value,string name)=>(Transform)value.GetType().GetField(name,All)?.GetValue(value);
    private static string Num(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
    private static string Vec(Vector3 value)=>"["+Num(value.x)+","+Num(value.y)+","+Num(value.z)+"]";
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}

    // Cache actual disconnected components once; callers evaluate the returned
    // action after each real animation frame. The unchanged 75mm threshold
    // applies on a separating world axis in either direction for every part.
    public static Action ArmClearance(string repo,string key,Renderer arm,Renderer lens)
    {
        var components=Split(Read(repo,key)).Select(Box).ToArray();
        return ()=>
        {
            var optical=lens.bounds;
            for(int index=0;index<components.Length;index++)
            {
                var local=components[index];var first=true;var actual=new Bounds();
                for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
                {
                    var point=arm.transform.TransformPoint(new Vector3(x==0?local.min.x:local.max.x,y==0?local.min.y:local.max.y,z==0?local.min.z:local.max.z));
                    if(first){actual=new Bounds(point,Vector3.zero);first=false;}else actual.Encapsulate(point);
                }
                var gap=Vector3.Max(optical.min-actual.max,actual.min-optical.max);
                float separation=Mathf.Max(gap.x,Mathf.Max(gap.y,gap.z));
                Require(separation>=.075f-.00001f,"Optical clearance below 75mm: "+key+" component "+index+" / "+lens.name+" gap "+separation+" m");
            }
        };
    }

    public static void Verify(Assembly assembly,object factory,Transform holder,string repo,string output)
    {
        Directory.CreateDirectory(output);
        var pose=assembly.GetType("SovietSemaphore.SemaphoreState",true);
        var axes=new[]{Axis(repo,"semaphore_upperrod"),Axis(repo,"semaphore_lowerrod")};
        // Identify the real rotating eccentric from exported topology. Contact
        // with a stationary bearing box cannot substitute for moving contact.
        Geometry Cam(string key)=>Split(Read(repo,key)).Single(m=>m.Indices.Length==76*3&&Mathf.Abs(Box(m).size.x-.24f)<.0002f&&Mathf.Abs(Box(m).size.y-.24f)<.0002f&&Mathf.Abs(Box(m).size.z-.04f)<.0002f);
        var cams=new[]{Cam("semaphore_upper"),Cam("semaphore_lower")};
        var leverBarMesh=Split(Read(repo,"semaphore_lever")).Single(m=>{var b=Box(m);return Mathf.Abs(b.size.x-.73f)<.0002f&&Mathf.Abs(b.size.y-.05f)<.0002f&&Mathf.Abs(b.size.z-.048f)<.0002f;});
        var leverBar=Box(leverBarMesh);
        float worstTop=0,worstBottom=0,worstJoint=0,worstHorizontal=0,worstAngle=0,minScale=float.PositiveInfinity,maxScale=0;
        string topWitness="",bottomWitness="",verticalWitness="";
        int samples=0,contacts=0,failures=0,verticalFailures=0,linkContacts=0,forbiddenLinks=0,clones=0,idleChecks=0,interruptionSamples=0,rigidityFailures=0,motionChecks=0;
        float maximumLengthChange=0,minimumMotion=float.PositiveInfinity;
        var firstStop=new List<string>();
        foreach(bool two in new[]{false,true})
        {
            var template=(Component)Call(factory,"Mechanism",holder,two);Component clone=null;
            try
            {
                Call(template,"SetState",Enum.Parse(pose,"Stop"),true);
                var original=Node(template,"UpperRod");
                var originalPosition=original.localPosition;var originalRotation=original.localRotation;var originalScale=original.localScale;
                clone=UnityEngine.Object.Instantiate(template);clone.transform.SetParent(null);
                clone.transform.position=new Vector3(17,3,-11);clone.transform.rotation=Quaternion.Euler(3,47,-2);clone.transform.localScale=new Vector3(.7f,1.2f,1.3f);clones++;
                foreach(string name in two?new[]{"UpperRod","LowerRod","Upper","Lower","Lever"}:new[]{"UpperRod","Upper","Lever"})
                    Require(Node(clone,name)!=Node(template,name)&&Node(clone,name).IsChildOf(clone.transform),"Unity clone does not remap linkage reference "+name);
                foreach(string name in two?new[]{"UpperRodLink","LowerRodLink"}:new[]{"UpperRodLink"})
                {
                    var link=OptionalNode(clone,name);
                    if(link!=null)Require(link!=OptionalNode(template,name)&&link.IsChildOf(clone.transform),"Unity clone does not remap short linkage reference "+name);
                }
                Call(clone,"SetState",Enum.Parse(pose,"Main"),true);
                Require(original.localPosition==originalPosition&&original.localRotation==originalRotation&&original.localScale==originalScale,"Clone linkage animation mutates template");
                foreach(var view in new[]{template,clone})
                {
                    var lever=view.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="lever");
                    var unexpected=new HashSet<Transform>();
                    foreach(string name in new[]{"UpperRodLink","LowerRodLink"})
                    {var link=OptionalNode(view,name);if(link!=null)unexpected.Add(link);}
                    foreach(string name in two?new[]{"UpperRod","LowerRod"}:new[]{"UpperRod"})
                    {
                        var actualRod=Node(view,name).GetComponentInChildren<MeshFilter>(true);
                        foreach(var filter in view.GetComponentsInChildren<MeshFilter>(true))
                            if(filter!=actualRod&&filter.sharedMesh==actualRod.sharedMesh)unexpected.Add(filter.transform);
                    }
                    forbiddenLinks+=unexpected.Count;
                    var restLengths=new float[two?2:1];
                    var lowBottom=Enumerable.Repeat(float.PositiveInfinity,restLengths.Length).ToArray();
                    var highBottom=Enumerable.Repeat(float.NegativeInfinity,restLengths.Length).ToArray();
                    void Contact(string witness)
                    {
                        samples++;
                        for(int rod=0;rod<(two?2:1);rod++)
                        {
                            var node=Node(view,rod==0?"UpperRod":"LowerRod");
                            var top=view.transform.InverseTransformPoint(node.TransformPoint(axes[rod][1]));
                            var bottomWorld=node.TransformPoint(axes[rod][0]);
                            var bottomRoot=view.transform.InverseTransformPoint(bottomWorld);
                            var bottom=lever.InverseTransformPoint(bottomWorld);
                            var armMesh=view.GetComponentsInChildren<Transform>(true).Single(t=>t.name==(rod==0?"UpperArm":"LowerArm"));
                            var topInCam=armMesh.InverseTransformPoint(node.TransformPoint(axes[rod][1]));
                            float topGap=ConvexGap(topInCam,cams[rod]);
                            float bottomGap=ConvexGap(bottom,leverBarMesh);contacts+=2;
                            float jointGap=0;
                            // Direct attachment is mandatory. Additional
                            // links, bends or intermediate pivots never replace
                            // the long rod's own endpoint-to-bearing contact.
                            Require(!float.IsNaN(topGap)&&!float.IsNaN(bottomGap)&&!float.IsInfinity(topGap)&&!float.IsInfinity(bottomGap),"Non-finite linkage transform");
                            string detail=(two?"two":"one")+" arms "+(view==clone?"clone":"template")+" "+witness+" "+(rod==0?"upper":"lower");
                            var axis=top-bottomRoot;
                            float length=axis.magnitude;
                            if(restLengths[rod]==0)restLengths[rod]=length;
                            float lengthChange=Mathf.Abs(length-restLengths[rod]);
                            maximumLengthChange=Mathf.Max(maximumLengthChange,lengthChange);
                            if(lengthChange>ContactTolerance)rigidityFailures++;
                            lowBottom[rod]=Mathf.Min(lowBottom[rod],bottomRoot.y);highBottom[rod]=Mathf.Max(highBottom[rod],bottomRoot.y);
                            float horizontal=new Vector2(axis.x,axis.z).magnitude;
                            float angle=Mathf.Atan2(horizontal,Mathf.Abs(axis.y))*Mathf.Rad2Deg;
                            Require(!float.IsNaN(horizontal)&&!float.IsNaN(angle),"Non-finite long rod axis");
                            if(horizontal>worstHorizontal){worstHorizontal=horizontal;verticalWitness=detail;}
                            worstAngle=Mathf.Max(worstAngle,angle);
                            if(horizontal>VerticalTolerance||axis.y<=0)verticalFailures++;
                            if(topGap>worstTop){worstTop=topGap;topWitness=detail;}
                            if(bottomGap>worstBottom){worstBottom=bottomGap;bottomWitness=detail;}
                            if(topGap>ContactTolerance)failures++;
                            if(bottomGap>ContactTolerance)failures++;
                            minScale=Mathf.Min(minScale,node.localScale.y);maxScale=Mathf.Max(maxScale,node.localScale.y);
                            Require(Mathf.Abs(node.localScale.x-1)<.00001f&&Mathf.Abs(node.localScale.z-1)<.00001f,"Rod cross-section was scaled");
                            if(witness=="Stop snapshot"&&view==template)
                                firstStop.Add("{\"two_arms\":"+(two?"true":"false")+",\"rod\":\""+(rod==0?"upper":"lower")+"\",\"top\":"+Vec(top)+",\"bottom_in_mast\":"+Vec(bottomRoot)+",\"bottom_in_lever_mesh\":"+Vec(bottom)+",\"bearing_gap_m\":"+Num(topGap)+",\"lever_gap_m\":"+Num(bottomGap)+",\"short_link_joint_gap_m\":"+Num(jointGap)+",\"horizontal_axis_offset_m\":"+Num(horizontal)+",\"axis_angle_deg\":"+Num(angle)+"}");
                        }
                    }
                    Call(view,"SetState",Enum.Parse(pose,"Stop"),true);Contact("Stop snapshot");
                    foreach(string from in new[]{"Stop","Main","Diverging","Dark"})foreach(string to in new[]{"Stop","Main","Diverging","Dark"})
                    {
                        Call(view,"SetState",Enum.Parse(pose,from),true);Call(view,"SetState",Enum.Parse(pose,to),false);
                        for(int frame=0;frame<=40;frame++)
                        {if(frame>0)Call(view,"Advance",.05f);Contact(from+" -> "+to+" frame "+frame);}
                    }
                    Call(view,"SetState",Enum.Parse(pose,"Stop"),true);Call(view,"SetState",Enum.Parse(pose,"Diverging"),false);Call(view,"Advance",.35f);Contact("interrupted open");interruptionSamples++;
                    Call(view,"SetState",Enum.Parse(pose,"Main"),false);Call(view,"Advance",.2f);Contact("interrupted route change");interruptionSamples++;
                    Call(view,"SetState",Enum.Parse(pose,"Stop"),false);
                    for(int frame=0;frame<=40;frame++){if(frame>0)Call(view,"Advance",.05f);Contact("interrupted close frame "+frame);interruptionSamples++;}
                    foreach(string name in two?new[]{"UpperRod","LowerRod","UpperRodLink","LowerRodLink"}:new[]{"UpperRod","UpperRodLink"})
                    {
                        var rod=OptionalNode(view,name);if(rod==null)continue;var p=rod.localPosition;var r=rod.localRotation;var s=rod.localScale;
                        Call(view,"Advance",10f);
                        Require(rod.localPosition==p&&rod.localRotation==r&&rod.localScale==s,"Idle linkage keeps changing transforms");idleChecks++;
                    }
                    for(int rod=0;rod<restLengths.Length;rod++)
                    {float displacement=highBottom[rod]-lowBottom[rod];minimumMotion=Mathf.Min(minimumMotion,displacement);motionChecks++;if(displacement<.02f)rigidityFailures++;}
                }
                if(two)
                {
                    clone.transform.position=Vector3.zero;clone.transform.rotation=Quaternion.identity;clone.transform.localScale=Vector3.one;clone.gameObject.SetActive(true);
                    foreach(string state in new[]{"Stop","Main","Diverging"})
                    {
                        Call(clone,"SetState",Enum.Parse(pose,state),true);
                        RenderRear(clone.gameObject,Path.Combine(output,"semaphore-linkage-"+state+"-rear.png"),new Vector3(3.2f,4.6f,-12),new Vector3(0,4.2f,0),4.6f,700,1000);
                        RenderRear(clone.gameObject,Path.Combine(output,"semaphore-linkage-"+state+"-head-rear.png"),new Vector3(1.9f,7.4f,-5),new Vector3(.1f,7.35f,0),1.2f,1000,1000);
                        RenderRear(clone.gameObject,Path.Combine(output,"semaphore-linkage-"+state+"-head-front-side.png"),new Vector3(3.2f,7.5f,3.8f),new Vector3(.05f,7.3f,.23f),1.2f,1100,1100);
                        RenderRear(clone.gameObject,Path.Combine(output,"semaphore-linkage-"+state+"-drive-rear.png"),new Vector3(1.4f,1.2f,-3.3f),new Vector3(.1f,1,-.45f),.70f,1000,1000);
                        if(state=="Stop")
                        {
                            RenderRear(clone.gameObject,Path.Combine(output,"semaphore-drive-side.png"),new Vector3(5,1,0),new Vector3(0,.8f,-.2f),.8f,1100,1100);
                            RenderRear(clone.gameObject,Path.Combine(output,"semaphore-mechanism-side.png"),new Vector3(6,4.3f,0),new Vector3(0,4.2f,-.1f),4.6f,700,1000);
                        }
                    }
                    if(Environment.GetEnvironmentVariable("RUS_SEMAPHORE_ANIMATION")=="1")
                    {
                        var frames=Path.Combine(output,"animation");Directory.CreateDirectory(frames);int frame=0;
                        Call(clone,"SetState",Enum.Parse(pose,"Stop"),true);
                        foreach(string next in new[]{"Main","Diverging","Stop"})
                        {
                            Call(clone,"SetState",Enum.Parse(pose,next),false);
                            for(int tick=0;tick<30;tick++,frame++)
                            {
                                RenderRear(clone.gameObject,Path.Combine(frames,"head-"+frame.ToString("D4")+".png"),new Vector3(3.2f,7.5f,3.8f),new Vector3(.05f,7.3f,.23f),1.2f,640,640);
                                RenderRear(clone.gameObject,Path.Combine(frames,"drive-"+frame.ToString("D4")+".png"),new Vector3(3,1.2f,3.3f),new Vector3(.1f,.9f,.29f),.60f,640,640);
                                Call(clone,"Advance",1f/15);
                            }
                        }
                    }
                }
            }
            finally
            {
                if(clone!=null)UnityEngine.Object.DestroyImmediate(clone.gameObject);
                UnityEngine.Object.DestroyImmediate(template.gameObject);
            }
        }
        string report="{\"pose_samples\":"+samples+",\"endpoint_contacts\":"+contacts+",\"clones\":"+clones+",\"idle_checks\":"+idleChecks+",\"interruption_samples\":"+interruptionSamples+",\"failed_contacts\":"+failures+",\"worst_bearing_gap_m\":"+Num(worstTop)+",\"worst_lever_gap_m\":"+Num(worstBottom)+",\"bearing_witness\":\""+topWitness+"\",\"lever_witness\":\""+bottomWitness+"\",\"minimum_rod_length_scale\":"+Num(minScale)+",\"maximum_rod_length_scale\":"+Num(maxScale)+",\"maximum_contact_gap_m\":"+Num(ContactTolerance)+",\"stop_snapshots\":["+string.Join(",",firstStop)+"],\"rear_renders\":9,\"head_front_side_renders\":3,\"side_renders\":2,\"scope\":\"Actual factory and Apply; exported vertices; all state transitions and interrupted motion; one/two arms; translated, rotated and scaled clone; direct endpoint contact to actual convex beveled bearing and drive prism meshes; no proposed fitting-coordinate oracle\"}";
        File.WriteAllText(Path.Combine(output,"semaphore-linkage-verification.json"),report);Debug.Log("SEMAPHORE_LINKAGE_VERIFICATION "+report);
        string verticalReport="{\"pose_samples\":"+samples+",\"long_rod_axes\":"+(contacts-linkContacts)/2+",\"failed_vertical_axes\":"+verticalFailures+",\"maximum_horizontal_axis_offset_m\":"+Num(worstHorizontal)+",\"maximum_axis_angle_deg\":"+Num(worstAngle)+",\"vertical_tolerance_m\":"+Num(VerticalTolerance)+",\"vertical_witness\":\""+verticalWitness+"\",\"short_link_contacts\":"+linkContacts+",\"maximum_short_link_joint_gap_m\":"+Num(worstJoint)+",\"failed_physical_contacts\":"+failures+",\"scope\":\"Both exported endpoints of each long rod must share X and Z in mast coordinates through all poses, interrupted motion and transformed clones; direct drive-bar to single rod to bearing contact; no added links\"}";
        File.WriteAllText(Path.Combine(output,"semaphore-vertical-rods-verification.json"),verticalReport);Debug.Log("SEMAPHORE_VERTICAL_RODS_VERIFICATION "+verticalReport);
        string directReport="{\"long_rod_axes\":"+contacts/2+",\"forbidden_extra_links\":"+forbiddenLinks+",\"failed_direct_endpoint_contacts\":"+failures+",\"maximum_direct_bearing_gap_m\":"+Num(worstTop)+",\"maximum_drive_bar_gap_m\":"+Num(worstBottom)+",\"failed_vertical_axes\":"+verticalFailures+",\"scope\":\"A single vertical exported rod must directly contact the existing upper bearing and lower drive bar throughout motion; extra shared-mesh parts and UpperRodLink/LowerRodLink transforms are forbidden\"}";
        File.WriteAllText(Path.Combine(output,"semaphore-direct-rods-verification.json"),directReport);Debug.Log("SEMAPHORE_DIRECT_RODS_VERIFICATION "+directReport);
        string movingReport="{\"moving_cam_contacts\":"+contacts/2+",\"failed_endpoint_contacts\":"+failures+",\"failed_rigidity_or_motion_checks\":"+rigidityFailures+",\"maximum_rod_length_change_m\":"+Num(maximumLengthChange)+",\"motion_checks\":"+motionChecks+",\"minimum_rod_translation_m\":"+Num(minimumMotion)+",\"scope\":\"Actual 76-triangle rotating eccentric and actual lower drive bar; constant rod length through every transition, interruption and transformed clone; at least 20mm real vertical rod translation\"}";
        File.WriteAllText(Path.Combine(output,"semaphore-moving-mechanism-verification.json"),movingReport);Debug.Log("SEMAPHORE_MOVING_MECHANISM_VERIFICATION "+movingReport);
        Require(rigidityFailures==0,"Semaphore rods must translate rigidly with visible motion: "+rigidityFailures+" failures, maximum length change "+maximumLengthChange+" m, minimum translation "+minimumMotion+" m");
        Require(forbiddenLinks==0,"Additional semaphore connecting rods or link transforms are forbidden: "+forbiddenLinks);
        Require(failures==0,"Detached semaphore linkage: "+failures+" failed endpoint contacts; bearing gap "+worstTop+" m, drive bar gap "+worstBottom+" m");
        Require(verticalFailures==0,"Long semaphore rods must be vertical in mast coordinates: "+verticalFailures+" failed axes, maximum horizontal offset "+worstHorizontal+" m and angle "+worstAngle+" degrees");
        Debug.Log("SEMAPHORE_VERTICAL_RODS_PASS");
        Debug.Log("SEMAPHORE_DIRECT_RODS_PASS");
        Debug.Log("SEMAPHORE_LINKAGE_PASS");
        Debug.Log("SEMAPHORE_MOVING_MECHANISM_PASS");
    }

    public static void CaptureMechanism(Assembly assembly,object factory,Transform holder,string repo,string output)
    {
        var pose=assembly.GetType("SovietSemaphore.SemaphoreState",true);
        var view=(Component)Call(factory,"Mechanism",holder,true);view.transform.SetParent(null);view.gameObject.SetActive(true);
        try
        {
            Call(view,"SetState",Enum.Parse(pose,"Stop"),true);
            RenderRear(view.gameObject,Path.Combine(output,"mechanism-overview.png"),new Vector3(3.2f,4.6f,12),new Vector3(0,4.2f,.2f),4.6f,700,1000);
            RenderRear(view.gameObject,Path.Combine(output,"mechanism-head.png"),new Vector3(3.2f,7.5f,3.8f),new Vector3(.05f,7.3f,.4f),1.2f,900,900);
            RenderRear(view.gameObject,Path.Combine(output,"mechanism-head-rear.png"),new Vector3(3,7.7f,-3),new Vector3(0,7.35f,.28f),1.15f,1000,1000);
            RenderRear(view.gameObject,Path.Combine(output,"mechanism-head-side.png"),new Vector3(-3,7.7f,1.4f),new Vector3(-.05f,7.35f,.28f),1.15f,1000,1000);
            RenderRear(view.gameObject,Path.Combine(output,"mechanism-drive.png"),new Vector3(2.3f,1.3f,3.4f),new Vector3(0,1,.43f),.5f,900,900);
            if(Environment.GetEnvironmentVariable("RUS_SEMAPHORE_ANIMATION")!="1")return;
            var frames=Path.Combine(output,"animation");Directory.CreateDirectory(frames);int frame=0;
            foreach(string next in new[]{"Main","Diverging","Stop"})
            {
                Call(view,"SetState",Enum.Parse(pose,next),false);
                for(int tick=0;tick<30;tick++,frame++)
                {
                    RenderRear(view.gameObject,Path.Combine(frames,"head-"+frame.ToString("D4")+".png"),new Vector3(3.2f,7.5f,3.8f),new Vector3(.05f,7.3f,.4f),1.2f,640,640);
                    RenderRear(view.gameObject,Path.Combine(frames,"drive-"+frame.ToString("D4")+".png"),new Vector3(2.3f,1.3f,3.4f),new Vector3(0,1,.43f),.5f,640,640);
                    Call(view,"Advance",1f/15);
                }
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(view.gameObject);}
    }

    private sealed class Geometry{internal Vector3[] Vertices;internal int[] Indices;}
    private static Geometry Read(string repo,string key)
    {
        string path=Path.Combine(repo,"src/RuSignals.Next/Assets/SolidHoods/Semaphore",key+".rsm");
        using(var reader=new BinaryReader(File.OpenRead(path)))
        {
            Require(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))=="RUSMESH2","Unexpected geometry format");
            Require(reader.ReadInt32()==1,"Unexpected semaphore part count");
            reader.ReadBytes(reader.ReadInt32());reader.ReadBytes(reader.ReadInt32());reader.ReadInt32();
            int vc=reader.ReadInt32(),ic=reader.ReadInt32();var vs=new Vector3[vc];var indices=new int[ic];
            for(int i=0;i<vc;i++){vs[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());for(int n=0;n<5;n++)reader.ReadSingle();}
            for(int i=0;i<ic;i++)indices[i]=reader.ReadInt32();
            return new Geometry{Vertices=vs,Indices=indices};
        }
    }
    private static Vector3[] Axis(string repo,string key)
    {
        var vs=Read(repo,key).Vertices;float low=vs.Min(v=>v.y),high=vs.Max(v=>v.y);
        Vector3 Average(Vector3[] points)=>points.Aggregate(Vector3.zero,(sum,p)=>sum+p)/points.Length;
        return new[]{Average(vs.Where(v=>Mathf.Abs(v.y-low)<.00001f).ToArray()),Average(vs.Where(v=>Mathf.Abs(v.y-high)<.00001f).ToArray())};
    }
    private static Bounds Box(Geometry geometry)
    {
        var b=new Bounds(geometry.Vertices[geometry.Indices[0]],Vector3.zero);
        foreach(int i in geometry.Indices)b.Encapsulate(geometry.Vertices[i]);return b;
    }
    private static float ConvexGap(Vector3 point,Geometry geometry)
    {
        var center=Vector3.zero;foreach(int i in geometry.Indices)center+=geometry.Vertices[i];center/=geometry.Indices.Length;
        float gap=0;
        for(int i=0;i<geometry.Indices.Length;i+=3)
        {
            var a=geometry.Vertices[geometry.Indices[i]];var b=geometry.Vertices[geometry.Indices[i+1]];var c=geometry.Vertices[geometry.Indices[i+2]];
            var normal=Vector3.Cross(b-a,c-a).normalized;
            if(Vector3.Dot(normal,center-a)>0)normal=-normal;
            gap=Mathf.Max(gap,Vector3.Dot(normal,point-a));
        }
        return gap;
    }
    private static List<Geometry> Split(Geometry geometry)
    {
        var parents=Enumerable.Range(0,geometry.Vertices.Length).ToArray();var positions=new Dictionary<Vector3,int>();
        int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
        void Join(int a,int b){parents[Find(a)]=Find(b);}
        for(int i=0;i<parents.Length;i++)
        {
            var v=geometry.Vertices[i];var key=new Vector3(Mathf.Round(v.x*100000),Mathf.Round(v.y*100000),Mathf.Round(v.z*100000));
            if(positions.TryGetValue(key,out var previous))Join(i,previous);else positions.Add(key,i);
        }
        for(int i=0;i<geometry.Indices.Length;i+=3){Join(geometry.Indices[i],geometry.Indices[i+1]);Join(geometry.Indices[i],geometry.Indices[i+2]);}
        var result=new Dictionary<int,List<int>>();
        foreach(int i in geometry.Indices)
        {int root=Find(i);if(!result.TryGetValue(root,out var indices)){indices=new List<int>();result.Add(root,indices);}indices.Add(i);}
        return result.Values.Select(ids=>new Geometry{Vertices=geometry.Vertices,Indices=ids.ToArray()}).ToList();
    }
    private static void RenderRear(GameObject target,string path,Vector3 position,Vector3 look,float size,int width,int height)
    {
        const int layer=30;foreach(var t in target.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
        var camera=new GameObject("Linkage QA rear camera").AddComponent<Camera>();var light=new GameObject("Linkage QA rear light").AddComponent<Light>();
        var previousMode=RenderSettings.ambientMode;var previousAmbient=RenderSettings.ambientLight;var previousTarget=RenderTexture.active;
        RenderTexture texture=null;Texture2D pixels=null;
        try
        {
            camera.transform.position=position;camera.transform.LookAt(look);camera.orthographic=true;camera.orthographicSize=size;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.15f,.18f);camera.cullingMask=1<<layer;camera.nearClipPlane=.01f;camera.farClipPlane=50;
            light.type=LightType.Directional;light.intensity=1.3f;light.transform.position=position+new Vector3(-2,4,-2);light.transform.LookAt(look);light.cullingMask=1<<layer;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.65f,.65f,.65f);
            texture=new RenderTexture(width,height,24);camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
            pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());
        }
        finally
        {
            RenderSettings.ambientMode=previousMode;RenderSettings.ambientLight=previousAmbient;RenderTexture.active=previousTarget;camera.targetTexture=null;
            if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);
        }
    }
}
