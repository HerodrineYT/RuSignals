using UnityEngine;
namespace RuSignals;
// Distance gain affects only local lighting. Native blinking, glass,
// glare and signal permissions retain their own state.
public sealed class SpillView:MonoBehaviour
{
    public Light[] Lights;
    public int[] Masks;
    public IndicatorEmission[] Indicators;
    public float[] Intensities;
    internal int Slot=-1;
    internal bool Selected;
    private Light positionLight;
    private Transform positionTransform;
    private bool admitted=true;
    private float gain=1;
    internal void Bind(IndicatorEmission indicator)
    {
        if(Indicators==null)return;
        for(int i=0;i<Lights.Length;i++)if(Lights[i]==indicator.emissionLight)
        {Indicators[i]=indicator;ApplyGain(i);return;}
    }
    internal void SetGain(float value)
    {
        value=Mathf.Clamp01(value);if(gain==value)return;gain=value;
        if(Indicators!=null)for(int i=0;i<Indicators.Length;i++)ApplyGain(i);
    }
    internal void SetIntensity(IndicatorEmission indicator,float intensity)
    {
        if(Indicators==null)return;
        for(int i=0;i<Indicators.Length;i++)if(Indicators[i]==indicator){Intensities[i]=intensity;ApplyGain(i);return;}
    }
    private void ApplyGain(int i)
    {
        var indicator=Indicators[i];if(indicator==null)return;
        indicator.lightIntensity=Intensities[i]*gain;
        if(Lights[i]!=null)Lights[i].intensity=indicator.lightIntensity*indicator.EmissionValue;
    }
    internal Vector3 Position
    {
        get
        {
            var first=Lights!=null&&Lights.Length>0?Lights[0]:null;
            if(first==null)return transform.position;
            if(positionLight!=first){positionLight=first;positionTransform=first.transform;}
            return positionTransform.position;
        }
    }
    internal void Admit(bool value)
    {
        if(value==admitted)return;admitted=value;
        for(int i=0;i<Lights.Length;i++)if(Lights[i]!=null)
        {Lights[i].cullingMask=value?Masks[i]:0;Lights[i].shadows=value?LightShadows.Hard:LightShadows.None;}
    }
    private void OnEnable(){if(Lights!=null&&Masks!=null)VisualScheduler.Register(this);}
    private void OnDisable(){VisualScheduler.Unregister(this);SetGain(0);}
    private void OnDestroy(){VisualScheduler.Unregister(this);SetGain(1);}
}
