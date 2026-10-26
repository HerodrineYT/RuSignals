using System.IO;
using System.Linq;
using UnityEngine;

public static partial class UnityRewriteCheck
{
    private static void SemaphoreAuxiliaryChecks(GameObject holder,object pack,string output)
    {
        var unwanted=holder.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Auxiliary lamp housing"||t.name=="Moon-white lens").ToArray();
        var renderers=unwanted.Sum(t=>t.GetComponents<Renderer>().Length);
        File.WriteAllText(Path.Combine(output,"semaphore-auxiliary-removal-verification.json"),"{\"unwanted_nodes\":"+unwanted.Length+",\"unwanted_renderers\":"+renderers+",\"scope\":\"Actual factory pack, all original semaphore controller variants including inactive templates; separate auxiliary housing and lens must not exist\"}");
        Check(unwanted.Length==0&&renderers==0,"Separate auxiliary semaphore lantern is absent from all factory templates");
        var entry=(Component)Field(pack,"OldEntrySignal");var clone=UnityEngine.Object.Instantiate(entry.gameObject);clone.transform.SetParent(null);clone.SetActive(true);
        try{Render(clone,Path.Combine(output,"semaphore-without-auxiliary.png"),new Vector3(3,5,11),new Vector3(0,4,.2f),5.2f);}
        finally{UnityEngine.Object.DestroyImmediate(clone);}
        Debug.Log("RUS_SEMAPHORE_AUXILIARY_REMOVED_PASS");
    }
}
