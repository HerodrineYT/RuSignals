using UnityEngine;
namespace RuSignals;
internal static class SceneProbe
{
    private static readonly RaycastHit[] Hits=new RaycastHit[64];
    private static readonly Collider[] Boxes=new Collider[64];
    internal static bool Nearest(Vector3 from,Vector3 direction,float distance,Transform owner,out RaycastHit nearest,bool twoSided=false,float minFacing=0)
    {
        nearest=default;float best=float.PositiveInfinity;
        int count=Physics.RaycastNonAlloc(from,direction,Hits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        // A saturated result cannot prove that a nearer wall wasn't omitted.
        if(count==Hits.Length)return false;
        for(int i=0;i<count;i++)
        {
            var c=Hits[i].collider;
            if(!Solid(c,owner)||Mathf.Abs(Vector3.Dot(Hits[i].normal,direction))<minFacing)continue;
            if(Hits[i].distance>=best)continue;best=Hits[i].distance;nearest=Hits[i];
        }
        if(twoSided)
        {
            // Some imported tunnel collision surfaces face outwards. Probe
            // their other side locally; never change global queriesHitBackfaces.
            count=Physics.RaycastNonAlloc(from+direction*distance,-direction,Hits,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            if(count==Hits.Length)return false;
            for(int i=0;i<count;i++)
            {
                if(!Solid(Hits[i].collider,owner)||Mathf.Abs(Vector3.Dot(Hits[i].normal,direction))<minFacing)continue;float d=distance-Hits[i].distance;
                if(d>=best||d<.001f)continue;best=d;nearest=Hits[i];nearest.distance=d;
            }
            if(best<float.PositiveInfinity&&Vector3.Dot(nearest.normal,direction)>0)nearest.normal=-nearest.normal;
        }
        return best<float.PositiveInfinity;
    }
    internal static bool ClearBox(Vector3 center,Vector3 half,Quaternion rotation,Transform owner)
    {
        int count=Physics.OverlapBoxNonAlloc(center,half,Boxes,rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        if(count==Boxes.Length)return false;
        for(int i=0;i<count;i++)if(Solid(Boxes[i],owner))return false;
        return true;
    }
    private static bool Solid(Collider c,Transform owner)=>c!=null&&!(c is CharacterController)&&!c.CompareTag("Player")&&!c.transform.IsChildOf(owner)&&c.attachedRigidbody==null&&c.GetType().Name!="TerrainCollider"&&c.GetComponentInParent<TrainCar>()==null;
}
