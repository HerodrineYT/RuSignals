using UnityEngine;
namespace RuSignals;

internal sealed partial class PackFactory
{
    private void Transmission(SemaphoreVisual view,Transform root,bool two)
    {
        Transform Part(string key,string name,Transform parent=null)=>assets.PutPart(parent??root,assets.Model("Semaphore/"+key)[0],metal,name).transform;
        view.UpperDriveArm=view.Lever;view.UpperDriveArm.localPosition=new Vector3(0,.97f,.48f);
        if(two){view.LowerDriveArm=Part("mechanism8371_lower_arm","Lower drive arm");view.LowerDriveArm.localPosition=new Vector3(0,.97f,.54f);}
        view.DriveWheel=Part("mechanism8371_wheel","Profiled drive wheel");view.DriveWheel.localPosition=new Vector3(.04f,.97f,.43f);
        Transform Roller(Transform arm,float k)
        {
            var t=Part("mechanism8371_roller","Drive groove roller",arm);
            t.localPosition=new Vector3(.1847759f*k,-.07653669f*k,.4445f-arm.localPosition.z);
            t.localScale=new Vector3(1,1,arm.localPosition.z-.4445f);return t;
        }
        view.UpperRoller=Roller(view.UpperDriveArm,.35f);if(two)view.LowerRoller=Roller(view.LowerDriveArm,.70f);
        view.ControlCableA=Part("mechanism8371_clamp","Control cable clamp");view.ControlCableB=Part("mechanism8371_clamp","Control cable clamp");
        view.ControlCableA.localPosition=new Vector3(.04f-.1525f,.55f,.43f);view.ControlCableB.localPosition=new Vector3(.04f+.1525f,.55f,.43f);
    }
}
