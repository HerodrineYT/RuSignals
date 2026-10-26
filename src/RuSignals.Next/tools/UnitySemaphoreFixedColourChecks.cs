using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void SemaphoreFixedColourChecks(Assembly assembly,object factory,Transform holder,string output)
    {
        var pose=assembly.GetType("SovietSemaphore.SemaphoreState",true);
        int samples=0,redFrames=0,colourChanges=0,wrongStates=0;string firstFailure="";
        foreach(bool two in new[]{false,true})
        {
            var template=(Component)Call(factory,"Mechanism",holder,two);
            var clone=UnityEngine.Object.Instantiate(template);clone.transform.SetParent(null);
            try
            {
                foreach(var view in new[]{template,clone})
                {
                    var red=(Renderer)Field(view,"Red");var green=(Renderer)Field(view,"Green");var yellow=Field(view,"Yellow") as Renderer;
                    var lamps=two?new[]{green,yellow}:new[]{green};
                    var materials=lamps.Select(r=>r.sharedMaterial).ToArray();
                    var colours=materials.Select(m=>m.GetColor("_EmissionColor")).ToArray();
                    if(!(colours[0].g>colours[0].r&&colours[0].g>colours[0].b)||two&&!(colours[1].r>colours[1].b&&colours[1].g>colours[1].b))colourChanges++;
                    foreach(string from in new[]{"Stop","Main","Diverging","Dark"})foreach(string to in new[]{"Stop","Main","Diverging","Dark"})
                    {
                        Call(view,"SetState",Enum.Parse(pose,from),true);Call(view,"SetState",Enum.Parse(pose,to),false);
                        for(int frame=0;frame<=80;frame++)
                        {
                            if(frame>0)Call(view,"Advance",.025f);samples++;
                            if(red.enabled){redFrames++;if(firstFailure=="")firstFailure=(two?"two":"one")+" arms "+from+" -> "+to+" frame "+frame;}
                            for(int lamp=0;lamp<lamps.Length;lamp++)if(lamps[lamp].sharedMaterial!=materials[lamp]||materials[lamp].GetColor("_EmissionColor")!=colours[lamp])colourChanges++;
                        }
                        string state=view.GetType().GetProperty("State").GetValue(view,null).ToString();
                        bool main=state=="Main",diverging=state=="Diverging";
                        if(green.enabled!=(main||diverging)||(yellow!=null&&yellow.enabled)!=diverging)wrongStates++;
                        if(((Renderer)Field(view,"UpperDark")).enabled==green.enabled||two&&((Renderer)Field(view,"LowerDark")).enabled==yellow.enabled)wrongStates++;
                    }
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(clone.gameObject);UnityEngine.Object.DestroyImmediate(template.gameObject);}
        }
        File.WriteAllText(Path.Combine(output,"semaphore-fixed-colours-verification.json"),"{\"pose_samples\":"+samples+",\"red_visible_frames\":"+redFrames+",\"colour_changes\":"+colourChanges+",\"wrong_final_states\":"+wrongStates+",\"first_failure\":\""+firstFailure+"\",\"upper_colour\":\"green\",\"lower_colour\":\"yellow\",\"scope\":\"Actual factory and cloned renderers, all16 transitions including Dark, unchanged material references and emission colours throughout motion, final lit/unlit states\"}");
        Check(redFrames==0&&colourChanges==0&&wrongStates==0,"Semaphore lamps only switch on/off with fixed green above and yellow below: "+firstFailure);
        Debug.Log("RUS_SEMAPHORE_FIXED_COLOURS_PASS "+samples+" poses");
    }
}
