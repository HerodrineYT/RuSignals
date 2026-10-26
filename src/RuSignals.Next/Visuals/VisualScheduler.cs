using System.Collections.Generic;
using UnityEngine;
namespace RuSignals;
public sealed class VisualScheduler:MonoBehaviour
{
    internal static VisualScheduler Current;
    private readonly List<SemaphoreVisual> animations=new List<SemaphoreVisual>();
    private readonly List<ClearanceView> masts=new List<ClearanceView>();
    private readonly List<SpillView> spills=new List<SpillView>();
    private readonly List<RoutePanel> panels=new List<RoutePanel>();
    private const int SpillBudget=8;
    private const float SpillRangeSquared=LampVisualRules.SpillStart*LampVisualRules.SpillStart;
    private readonly SpillView[] nearest=new SpillView[SpillBudget];
    private readonly float[] distances=new float[SpillBudget];
    private float nextSpill;
    internal int SpillCount=>spills.Count;
    private int cursor;
    internal int MovingCount=>animations.Count;
    internal static void Wake(SemaphoreVisual view)
    {
        if(Current==null||view.SchedulerSlot>=0)return;
        view.SchedulerSlot=Current.animations.Count;Current.animations.Add(view);Current.enabled=true;
    }
    internal static void Sleep(SemaphoreVisual view)
    {
        if(Current==null){view.SchedulerSlot=-1;return;}
        var list=Current.animations;int index=view.SchedulerSlot;
        if(index<0||index>=list.Count||list[index]!=view){view.SchedulerSlot=-1;return;}
        int last=list.Count-1;var moved=list[last];list[index]=moved;moved.SchedulerSlot=index;list.RemoveAt(last);view.SchedulerSlot=-1;Current.Idle();
    }
    internal static void Register(ClearanceView view){if(Current!=null&&!Current.masts.Contains(view)){Current.masts.Add(view);Current.enabled=true;}}
    internal static void Unregister(ClearanceView view){if(Current!=null){Current.masts.Remove(view);Current.Idle();}}
    internal static void Register(RoutePanel panel){if(Current==null||panel.Slot>=0)return;panel.Slot=Current.panels.Count;Current.panels.Add(panel);Current.enabled=true;}
    internal static void Unregister(RoutePanel panel)
    {
        if(Current==null){panel.Slot=-1;return;}var list=Current.panels;int index=panel.Slot;
        if(index>=0&&index<list.Count&&list[index]==panel){int last=list.Count-1;list[index]=list[last];list[index].Slot=index;list.RemoveAt(last);}panel.Slot=-1;Current.Idle();
    }
    internal static void Register(SpillView view)
    {
        if(Current==null||view.Slot>=0)return;
        view.Admit(false);view.SetGain(0);view.Slot=Current.spills.Count;Current.spills.Add(view);Current.nextSpill=0;Current.enabled=true;
    }
    internal static void Unregister(SpillView view)
    {
        if(Current==null){view.Slot=-1;return;}
        int i=view.Slot;var list=Current.spills;
        if(i<0||i>=list.Count||list[i]!=view){view.Slot=-1;return;}
        int last=list.Count-1;list[i]=list[last];list[i].Slot=i;list.RemoveAt(last);view.Slot=-1;view.Admit(false);Current.Idle();
    }
    internal void RefreshSpills(Vector3? camera)
    {
        int count=0;
        for(int i=0;i<nearest.Length;i++)nearest[i]=null;
        if(camera.HasValue)foreach(var view in spills)
        {
            if(view==null||!view.isActiveAndEnabled)continue;
            float d=(view.Position-camera.Value).sqrMagnitude;if(d>SpillRangeSquared)continue;
            int j=count;while(j>0&&d<distances[j-1])j--;
            if(j>=SpillBudget)continue;
            int end=Mathf.Min(count,SpillBudget-1);
            for(int k=end;k>j;k--){nearest[k]=nearest[k-1];distances[k]=distances[k-1];}
            nearest[j]=view;distances[j]=d;if(count<SpillBudget)count++;
        }
        for(int i=0;i<count;i++)
        {
            var view=nearest[i];view.Selected=true;
            float gain=LampVisualRules.SpillGain(Mathf.Sqrt(distances[i]));view.SetGain(gain);view.Admit(gain>0);
        }
        // Membership is marked on only the selected eight. Retire the rest in
        // one pass, without searching the eight entries again for every view.
        foreach(var view in spills)
        {
            if(view==null)continue;
            if(view.Selected){view.Selected=false;continue;}
            view.SetGain(0);view.Admit(false);
        }
    }
    private bool Empty=>animations.Count==0&&masts.Count==0&&spills.Count==0&&panels.Count==0;
    private void Idle(){if(Empty)enabled=false;}
    private void Awake(){Current=this;enabled=false;}
    private void Update()
    {
        if(Empty)return;
        if(SignalPower.Refresh())foreach(var panel in panels)if(panel!=null)panel.ApplyPower(SignalPower.IsOut);
        if(Time.unscaledTime>=nextSpill)
        {
            nextSpill=Time.unscaledTime+.2f;var camera=PlayerManager.ActiveCamera;
            RefreshSpills(camera!=null?(Vector3?)camera.transform.position:null);
        }
        for(int i=animations.Count-1;i>=0;i--){var view=animations[i];if(view!=null)view.Advance(Time.deltaTime);}
        // Bounded work even during a large world's initial placement. Each mast
        // decides its own next due time; off-camera physics is never polled.
        int budget=Mathf.Min(8,masts.Count);
        for(int i=0;i<budget;i++){if(cursor>=masts.Count)cursor=0;masts[cursor++].Poll(Time.unscaledTime);}
    }
    private void OnDestroy()
    {
        foreach(var view in animations)if(view!=null)view.SchedulerSlot=-1;
        foreach(var view in spills)if(view!=null){view.Slot=-1;view.SetGain(1);view.Admit(true);}
        foreach(var panel in panels)if(panel!=null)panel.Slot=-1;panels.Clear();
        spills.Clear();for(int i=0;i<nearest.Length;i++)nearest[i]=null;
        animations.Clear();masts.Clear();if(Current==this)Current=null;
    }
}
