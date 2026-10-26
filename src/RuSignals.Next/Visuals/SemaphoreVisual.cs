using Signals.Game;
using UnityEngine;
using SovietSemaphore;
namespace RuSignals;
using Signal = global::Signals.Game.Signal;
public sealed class SemaphoreVisual:MonoBehaviour
{
    public bool TwoArms;
    public bool FixedLanterns=true;
    public Transform Upper,Lower,Lever,UpperSelector,LowerSelector,UpperRod,LowerRod;
    public Renderer Red,Green,Yellow,UpperDark,LowerDark;
    public Transform UpperDriveArm,LowerDriveArm,DriveWheel,UpperRoller,LowerRoller,ControlCableA,ControlCableB;
    public float OpenSeconds=1.5f,CloseSeconds=1.17f;
    public SemaphoreState State{get;private set;}
    public bool Moving{get;private set;}
    internal int SchedulerSlot=-1;
    private float upper,lower=-90,drive,startDrive,targetDrive,time,duration;
    private Signal signal;
    internal void Bind(Signal source)
    {
        Unbind();signal=source;signal.AspectChanged+=Changed;Read(true);
    }
    internal void Unbind(){if(signal!=null)signal.AspectChanged-=Changed;signal=null;VisualScheduler.Sleep(this);}
    private void Changed(Signals.Game.Aspects.IAspect _)=>Read(false);
    private void Read(bool immediate)
    {
        var aspect=signal.CurrentAspect;var definition=aspect?.GetDefinition() as RuAspectDefinition;
        SetState(aspect==null?SemaphoreState.Dark:definition==null?SemaphoreState.Stop:SemaphoreRules.Pose(definition.Role,definition.Indication,signal.IsShunting,TwoArms),immediate);
    }
    public void SetState(SemaphoreState next,bool immediate=false)
    {
        if(next==SemaphoreState.Diverging&&!TwoArms)next=SemaphoreState.Stop;
        if(next==State&&!immediate)return;
        State=next;startDrive=drive;targetDrive=next==SemaphoreState.Main?-1:next==SemaphoreState.Diverging?1:0;
        duration=Mathf.Max(.01f,targetDrive!=0&&drive==0?OpenSeconds:CloseSeconds);time=0;
        if(immediate)drive=targetDrive;
        Moving=drive!=targetDrive;
        Apply();if(Moving)VisualScheduler.Wake(this);else VisualScheduler.Sleep(this);
    }
    internal void Advance(float seconds)
    {
        if(!Moving||seconds<=0)return;
        time=Mathf.Min(duration,time+seconds);float t=time/duration;t=t*t*(3-2*t);
        drive=Mathf.Lerp(startDrive,targetDrive,t);Apply();
        if(time>=duration){Moving=false;VisualScheduler.Sleep(this);}
    }
    private static void Rotate(Transform t,float angle){if(t!=null)t.localRotation=Quaternion.AngleAxis(angle,Vector3.forward);}
    private static readonly Vector3 RodPin=new Vector3(.1847759f,-.07653669f,0);
    private static void PlaceRod(Transform rod,Transform arm,float sourceX,float sourceLength,float length,float plane)
    {
        if(rod==null)return;
        var top=arm.localPosition+arm.localRotation*RodPin;top.z=plane;
        float scale=length/sourceLength;
        rod.localScale=new Vector3(1,scale,1);rod.localRotation=Quaternion.identity;
        rod.localPosition=top-new Vector3(sourceX,1.04f*scale+length,-.59f);
    }
    private void Apply()
    {
        // One control wheel, with two groove branches and independent coaxial
        // output arms. Changing branch passes through its closed middle stop.
        upper=45*Mathf.Abs(drive);lower=-90+135*Mathf.Max(drive,0);
        Rotate(Upper,-upper);Rotate(Lower,-lower);Rotate(UpperDriveArm,-upper);Rotate(LowerDriveArm,-lower);
        Rotate(DriveWheel,drive*60);
        PlaceRod(UpperRod,Upper,.13f,6.96f,7.08f,.48f);PlaceRod(LowerRod,Lower,.065f,5.68f,5.80f,.54f);
        float cableTravel=drive*Mathf.PI/3*.1525f;
        if(ControlCableA!=null){var p=ControlCableA.localPosition;p.y=.55f+cableTravel;ControlCableA.localPosition=p;}
        if(ControlCableB!=null){var p=ControlCableB.localPosition;p.y=.55f-cableTravel;ControlCableB.localPosition=p;}
        bool clear=State!=SemaphoreState.Dark&&(State==SemaphoreState.Main||State==SemaphoreState.Diverging)&&upper>=44.9f;
        Red.enabled=false;Green.enabled=clear;
        if(Yellow!=null)Yellow.enabled=clear&&State==SemaphoreState.Diverging&&lower>=44.9f;
        UpperDark.enabled=!Green.enabled;if(LowerDark!=null)LowerDark.enabled=Yellow==null||!Yellow.enabled;

    }
    private void OnDestroy(){Unbind();}
}
